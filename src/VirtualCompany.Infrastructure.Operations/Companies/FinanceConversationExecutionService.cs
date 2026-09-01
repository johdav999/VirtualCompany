using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceConversationExecutionOptions
{
    public const string SectionName = "FinanceConversationExecution";
    public int MaximumElapsedSeconds { get; set; } = 45;
    public int MaximumReadAttempts { get; set; } = 2;
    public int MaximumPlanRevisions { get; set; } = 2;
    public int MaximumValidatedOutputCharacters { get; set; } = 32_000;
}

public sealed class FinanceConversationExecutionRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _runs = new(StringComparer.Ordinal);

    public async Task<(FinanceConversationExecutionResult Result, bool IsDuplicate)> RunOnceAsync(
        string key,
        string requestFingerprint,
        Func<Task<FinanceConversationExecutionResult>> factory)
    {
        var candidate = new Entry(requestFingerprint,
            new Lazy<Task<FinanceConversationExecutionResult>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = _runs.GetOrAdd(key, candidate);
        if (!string.Equals(entry.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            throw new AgentAiConflictException("The idempotency key is already associated with a different Finance request.");

        var isDuplicate = !ReferenceEquals(candidate, entry);
        var result = await entry.Result.Value.ConfigureAwait(false);
        return (result with { IsDuplicate = isDuplicate }, isDuplicate);
    }

    private sealed record Entry(
        string RequestFingerprint,
        Lazy<Task<FinanceConversationExecutionResult>> Result);
}

public sealed class FinanceConversationExecutionService : IFinanceConversationExecutionService
{
    private static readonly IReadOnlySet<string> TransientFailureCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "provider_timeout", "provider_unavailable", "provider_rate_limited", "timeout", "temporarily_unavailable",
        "transient_failure", "finance_read_temporarily_unavailable"
    };

    private readonly IFinanceToolPlanner _planner;
    private readonly IFinancePlanningContextProjector _contextProjector;
    private readonly IAgentToolExecutionService _toolExecutor;
    private readonly ICompanyToolRegistry _toolRegistry;
    private readonly IAgentReasoningGateway _reasoning;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly FinanceConversationExecutionRegistry _registry;
    private readonly FinanceConversationExecutionOptions _options;
    private readonly ILogger<FinanceConversationExecutionService> _logger;

    public FinanceConversationExecutionService(
        IFinanceToolPlanner planner,
        IFinancePlanningContextProjector contextProjector,
        IAgentToolExecutionService toolExecutor,
        ICompanyToolRegistry toolRegistry,
        IAgentReasoningGateway reasoning,
        ICurrentUserAccessor currentUser,
        FinanceConversationExecutionRegistry registry,
        IOptions<FinanceConversationExecutionOptions> options,
        ILogger<FinanceConversationExecutionService> logger)
    {
        _planner = planner;
        _contextProjector = contextProjector;
        _toolExecutor = toolExecutor;
        _toolRegistry = toolRegistry;
        _reasoning = reasoning;
        _currentUser = currentUser;
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FinanceConversationExecutionResult> ExecuteAsync(
        ExecuteFinanceConversationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? request.IdempotencyKey.Trim()
            : request.CorrelationId.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            request.CompanyId,
            request.AgentId,
            UserRequest = request.UserRequest.Trim(),
            request.TaskId,
            request.ConversationId,
            request.References,
            request.Context
        }));
        var key = $"{request.CompanyId:N}:{request.AgentId:N}:{request.IdempotencyKey.Trim()}";
        var (result, _) = await _registry.RunOnceAsync(key, fingerprint,
            () => ExecuteCoreAsync(request, correlationId, cancellationToken));
        return result;
    }

    private async Task<FinanceConversationExecutionResult> ExecuteCoreAsync(
        ExecuteFinanceConversationRequest request,
        string correlationId,
        CancellationToken callerCancellationToken)
    {
        var startedUtc = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        var revisions = new List<FinanceConversationPlanRevision>();
        var results = new List<FinanceConversationStepResult>();
        var missingEvidence = new List<string>();
        var plannerCalls = 0;
        var synthesisCalls = 0;
        var toolCalls = 0;
        var retryCount = 0;
        var estimatedCost = 0m;
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? _options.MaximumElapsedSeconds, 5, 120);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        FinanceConversationExecutionResult Terminal(string state, string reasonCode, string explanation,
            FinanceConversationAnswer? answer = null) =>
            new(runId, FinanceConversationExecutionVersions.ContractV1, state, reasonCode, explanation,
                request.IdempotencyKey.Trim(), correlationId, false, revisions.ToArray(), results.ToArray(), answer,
                missingEvidence.Distinct(StringComparer.Ordinal).ToArray(),
                new FinanceConversationExecutionMetrics(timer.ElapsedMilliseconds, plannerCalls, synthesisCalls,
                    toolCalls, retryCount, estimatedCost), startedUtc, DateTime.UtcNow);

        try
        {
            var planRequest = new FinanceToolPlanRequest(request.CompanyId, request.AgentId, request.UserRequest,
                request.Context, request.TaskId, request.ConversationId, correlationId, request.References);
            plannerCalls++;
            var plan = await _planner.PlanAsync(planRequest, timeout.Token);
            estimatedCost = plan.Steps.Sum(step => step.EstimatedCost);
            AddRevision(revisions, plan);

            if (plan.State != FinanceToolPlanStates.Ready)
                return Terminal(MapPlanState(plan.State), plan.ReasonCode, plan.SafeExplanation);

            if (!IsReadOrRecommendationPlan(plan, out var unsafeReason))
                return Terminal(FinanceConversationRunStates.Unsupported, "finance_conversation_non_read_plan_rejected", unsafeReason);
            if (plan.GroundedEvidence.Any(evidence => !evidence.IsFresh))
            {
                missingEvidence.AddRange(plan.GroundedEvidence.Where(evidence => !evidence.IsFresh)
                    .Select(evidence => evidence.SourceId));
                return Terminal(FinanceConversationRunStates.NeedsClarification,
                    "finance_conversation_stale_evidence",
                    "The plan depends on Finance evidence outside its declared freshness window. Refresh the evidence before execution.");
            }

            var freshnessRequest = new FinancePlanningContextProjectionRequest(request.CompanyId, request.AgentId,
                request.UserRequest, correlationId, request.References, plan.Limits.MaximumRecords);
            var freshness = await _contextProjector.CheckFreshnessAsync(
                freshnessRequest, plan.PlanningContextHash, timeout.Token);
            if (!freshness.IsCurrent)
            {
                if (revisions.Count >= _options.MaximumPlanRevisions)
                    return Terminal(FinanceConversationRunStates.Failed, freshness.ReasonCode,
                        "Finance evidence changed before execution and the bounded re-planning limit was reached.");

                plannerCalls++;
                plan = await _planner.PlanAsync(planRequest, timeout.Token);
                estimatedCost = plan.Steps.Sum(step => step.EstimatedCost);
                AddRevision(revisions, plan);
                if (plan.State != FinanceToolPlanStates.Ready || !IsReadOrRecommendationPlan(plan, out unsafeReason))
                    return Terminal(MapPlanState(plan.State), plan.ReasonCode, plan.SafeExplanation);
                if (plan.GroundedEvidence.Any(evidence => !evidence.IsFresh))
                    return Terminal(FinanceConversationRunStates.NeedsClarification,
                        "finance_conversation_stale_evidence",
                        "The revised plan still depends on Finance evidence outside its declared freshness window.");

                freshness = await _contextProjector.CheckFreshnessAsync(
                    freshnessRequest, plan.PlanningContextHash, timeout.Token);
                if (!freshness.IsCurrent)
                    return Terminal(FinanceConversationRunStates.Failed, freshness.ReasonCode,
                        "Finance evidence changed again before execution. No stale plan was executed.");
            }

            var steps = plan.Steps.OrderBy(step => step.Order).ToList();
            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                var step = steps[stepIndex];
                timeout.Token.ThrowIfCancellationRequested();
                var failedDependencies = step.Dependencies.Where(dependency =>
                    results.All(result => result.StepId != dependency || result.State != FinanceConversationStepStates.Completed)).ToArray();
                if (failedDependencies.Length > 0)
                {
                    var now = DateTime.UtcNow;
                    results.Add(new FinanceConversationStepResult(step.StepId, step.ToolName, step.ToolVersion,
                        step.ActionType, FinanceConversationStepStates.Skipped, 0, null, false, true, false,
                        "dependency_not_completed", "This step was skipped because required evidence from a dependency was unavailable.",
                        null, step.Dependencies, now, now));
                    missingEvidence.AddRange(failedDependencies.Select(value => $"dependency:{value}"));
                    continue;
                }

                var execution = await ExecuteStepAsync(request, correlationId, plan, step, timeout.Token);
                toolCalls += execution.AttemptCount;
                retryCount += Math.Max(0, execution.AttemptCount - 1);
                results.Add(execution);
                if (execution.State != FinanceConversationStepStates.Completed)
                    missingEvidence.AddRange(step.EvidenceRequirements);

                if (execution.State == FinanceConversationStepStates.Completed &&
                    RequiresReplan(execution.ValidatedOutput,
                        steps.Skip(stepIndex + 1).Any(candidate => candidate.Dependencies.Contains(step.StepId))))
                {
                    if (revisions.Count >= _options.MaximumPlanRevisions)
                    {
                        missingEvidence.Add("bounded_replanning_limit_reached");
                        continue;
                    }

                    plannerCalls++;
                    var revised = await _planner.PlanAsync(planRequest, timeout.Token);
                    AddRevision(revisions, revised);
                    if (revised.State != FinanceToolPlanStates.Ready ||
                        !IsReadOrRecommendationPlan(revised, out unsafeReason))
                        return Terminal(FinanceConversationRunStates.PartiallyCompleted,
                            revised.ReasonCode, revised.SafeExplanation);
                    var revisedFreshness = await _contextProjector.CheckFreshnessAsync(
                        freshnessRequest, revised.PlanningContextHash, timeout.Token);
                    if (!revisedFreshness.IsCurrent)
                        return Terminal(FinanceConversationRunStates.PartiallyCompleted,
                            revisedFreshness.ReasonCode,
                            "A tool result changed target resolution, but the revised plan was already stale.");

                    plan = revised;
                    estimatedCost = plan.Steps.Sum(step => step.EstimatedCost);
                    steps = revised.Steps
                        .Where(candidate => results.All(result => result.StepId != candidate.StepId))
                        .OrderBy(candidate => candidate.Order)
                        .ToList();
                    stepIndex = -1;
                }
            }

            var completed = results.Where(result => result.State == FinanceConversationStepStates.Completed).ToArray();
            var incomplete = results.Where(result => result.State != FinanceConversationStepStates.Completed).ToArray();
            if (completed.Length == 0)
                return Terminal(FinanceConversationRunStates.Failed, "finance_conversation_no_validated_results",
                    "No Finance tool produced a validated result, so the question was not answered from general model knowledge.");

            synthesisCalls++;
            var answer = await SynthesizeAsync(request, correlationId, plan, completed, missingEvidence, timeout.Token);
            if (answer is null)
                return Terminal(FinanceConversationRunStates.PartiallyCompleted, "finance_conversation_synthesis_failed",
                    "Validated Finance evidence was retrieved, but a grounded answer could not be synthesized.");

            return incomplete.Length == 0
                ? Terminal(FinanceConversationRunStates.Completed, "finance_conversation_completed",
                    "The Finance request was answered from validated tool results.", answer)
                : Terminal(FinanceConversationRunStates.PartiallyCompleted, "finance_conversation_partially_completed",
                    "The answer uses validated results, but one or more planned evidence reads did not complete.", answer);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            return Terminal(FinanceConversationRunStates.Cancelled, "finance_conversation_cancelled",
                "The Finance request was cancelled. Completed read results are reported, but no unsupported answer was generated.");
        }
        catch (OperationCanceledException)
        {
            return Terminal(FinanceConversationRunStates.TimedOut, "finance_conversation_timed_out",
                "The Finance request exceeded its bounded execution time. Completed and incomplete reads are reported explicitly.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Finance conversational execution failed safely. CompanyId: {CompanyId}; AgentId: {AgentId}; CorrelationId: {CorrelationId}",
                request.CompanyId, request.AgentId, correlationId);
            return Terminal(FinanceConversationRunStates.Failed, "finance_conversation_failed",
                "Finance execution failed safely. No unvalidated result was presented as an answer.");
        }
    }

    private async Task<FinanceConversationStepResult> ExecuteStepAsync(
        ExecuteFinanceConversationRequest request,
        string correlationId,
        FinanceToolPlan plan,
        FinanceToolPlanStep step,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var action = ToolActionTypeValues.Parse(step.ActionType);
        var maximumAttempts = action == ToolActionType.Read ? Math.Clamp(_options.MaximumReadAttempts, 1, 3) : 1;
        ExecuteAgentToolResultDto? response = null;
        var attempts = 0;
        string? errorCode = null;

        while (attempts < maximumAttempts)
        {
            attempts++;
            response = await _toolExecutor.ExecuteAsync(request.CompanyId, request.AgentId,
                new ExecuteAgentToolCommand(step.ToolName, step.ActionType, step.Scope,
                    Clone(step.NormalizedArguments), null, null, null, false, request.TaskId, null,
                    correlationId, ExpectedAuthorityVersion: plan.EffectiveAuthorityVersion,
                    ExpectedAuthorityHash: plan.EffectiveAuthorityHash), cancellationToken);
            errorCode = ReadString(response.ExecutionResult, "errorCode");
            if (IsSuccessful(response)) break;
            if (action != ToolActionType.Read || !TransientFailureCodes.Contains(errorCode ?? string.Empty)) break;
        }

        var output = response?.ExecutionResult;
        var serializedLength = output is null ? 0 : JsonSerializer.Serialize(output).Length;
        var truncated = serializedLength > _options.MaximumValidatedOutputCharacters;
        var schemaValid = output is not null && !truncated &&
                          _toolRegistry.TryGetToolDefinition(step.ToolName, out var definition) &&
                          string.Equals(definition.Version, step.ToolVersion, StringComparison.Ordinal) &&
                          ToolJsonSchemaValidator.Validate(output, definition.OutputSchema, out _);
        var success = response is not null && IsSuccessful(response) && schemaValid;
        if (!schemaValid && string.IsNullOrWhiteSpace(errorCode))
            errorCode = truncated ? "validated_output_limit_exceeded" : "output_payload_schema_validation_failed";
        var summary = success
            ? response!.Message
            : response?.Message ?? "The Finance tool did not return a validated result.";

        return new FinanceConversationStepResult(step.StepId, step.ToolName, step.ToolVersion, step.ActionType,
            success ? FinanceConversationStepStates.Completed : FinanceConversationStepStates.Failed,
            attempts, response?.ExecutionId, schemaValid, true, truncated, errorCode, summary,
            success ? Clone(output!) : null, step.Dependencies, started, DateTime.UtcNow);
    }

    private async Task<FinanceConversationAnswer?> SynthesizeAsync(
        ExecuteFinanceConversationRequest request,
        string correlationId,
        FinanceToolPlan plan,
        IReadOnlyList<FinanceConversationStepResult> completed,
        IReadOnlyList<string> missingEvidence,
        CancellationToken cancellationToken)
    {
        const int maximumSourceCharacters = 4_800;
        var toolSources = new List<(AgentAiSource Source, FinanceConversationStepResult Step)>();
        foreach (var step in completed)
        {
            var json = JsonSerializer.Serialize(step.ValidatedOutput);
            var partCount = Math.Max(1, (int)Math.Ceiling(json.Length / (decimal)maximumSourceCharacters));
            for (var part = 0; part < partCount; part++)
            {
                var offset = part * maximumSourceCharacters;
                var length = Math.Min(maximumSourceCharacters, json.Length - offset);
                var source = new AgentAiSource(
                    $"tool-result:{step.ExecutionId:N}:part-{part + 1}",
                    "validated_finance_tool_result", step.ToolName,
                    json.Substring(offset, length), step.CompletedUtc);
                toolSources.Add((source, step));
            }
        }

        var sources = toolSources.Select(item => item.Source)
            .Concat(plan.GroundedEvidence.Select(evidence => new AgentAiSource(
                evidence.SourceId, evidence.EntityType, evidence.SafeLabel,
                $"Retained planning evidence version {evidence.SourceVersion}; freshness validated before execution.",
                evidence.UpdatedUtc)))
            .ToArray();
        if (sources.Length > 50) return null;
        var instruction = $"Answer this Finance request using only the supplied validated tool results: {request.UserRequest.Trim()} " +
                          "Preserve amounts, dates, currencies, and source relationships exactly. Separate confirmed facts from inferences. " +
                          $"State unknowns and missing evidence explicitly. Missing evidence: {string.Join(", ", missingEvidence.Distinct())}. " +
                          "Do not propose or perform mutations and do not fill gaps from general knowledge.";
        var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(
            request.CompanyId, request.AgentId, AgentCapabilityIds.FinanceConversationExecution,
            FinanceConversationExecutionVersions.CapabilityV1, FinanceConversationExecutionVersions.PromptV1,
            FinanceConversationExecutionVersions.ContractV1, instruction, sources, [], [], _currentUser.UserId,
            request.TaskId, request.ConversationId, correlationId, true, plan.EffectiveAuthorityVersion,
            plan.EffectiveAuthorityHash), cancellationToken);
        if (result.FailureCode is not null) return null;

        var references = toolSources.Select(item => new FinanceConversationSourceReference(
            item.Source.Id, item.Source.Type, item.Source.Title,
            FindDate(item.Step.ValidatedOutput), FindString(item.Step.ValidatedOutput, "currency"), item.Step.EvidenceFresh,
            FindString(item.Step.ValidatedOutput, "sourceLink") ?? FindString(item.Step.ValidatedOutput, "sourceUrl") ??
            FindString(item.Step.ValidatedOutput, "link")))
            .Concat(plan.GroundedEvidence.Select(evidence => new FinanceConversationSourceReference(
                evidence.SourceId, evidence.EntityType, evidence.SafeLabel, evidence.UpdatedUtc, null,
                evidence.IsFresh)))
            .ToArray();
        return new FinanceConversationAnswer(result.Summary,
            result.Claims.Where(claim => claim.Type is "fact" or "confirmed_fact").ToArray(),
            result.Claims.Where(claim => claim.Type == "inference").ToArray(),
            result.Claims.Where(claim => claim.Type == "unknown").Select(claim => claim.Text)
                .Concat(result.Uncertainty).Concat(result.MissingEvidence).Concat(missingEvidence).Distinct().ToArray(),
            references, result.Confidence);
    }

    private static bool IsReadOrRecommendationPlan(FinanceToolPlan plan, out string reason)
    {
        foreach (var step in plan.Steps)
        {
            if (step.ActionType is not ("read" or "recommend") ||
                step.ConfirmationState != FinanceToolPlanCheckpointStates.NotRequired ||
                step.ApprovalState != FinanceToolPlanCheckpointStates.NotRequired)
            {
                reason = "Conversational Finance execution accepts read and recommendation steps only; mutation or checkpoint steps were rejected.";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool IsSuccessful(ExecuteAgentToolResultDto response) =>
        string.Equals(response.Status, "executed", StringComparison.OrdinalIgnoreCase) &&
        ReadBoolean(response.ExecutionResult, "success") == true;

    private static bool RequiresReplan(
        IReadOnlyDictionary<string, JsonNode?>? output,
        bool suppliesDeclaredDependency)
    {
        if (output is null) return false;
        var root = new JsonObject(Clone(output).Select(pair => KeyValuePair.Create(pair.Key, pair.Value)).ToArray());
        var contextChanged = FindNode(root, "planningContextChanged") is JsonValue changed &&
                             changed.TryGetValue<bool>(out var changedValue) && changedValue;
        var dependencySupplied = suppliesDeclaredDependency &&
                                 FindNode(root, "resolvedPlanningDependency") is JsonValue supplied &&
                                 supplied.TryGetValue<bool>(out var suppliedValue) && suppliedValue;
        return contextChanged || dependencySupplied;
    }

    private static string MapPlanState(string state) => state switch
    {
        FinanceToolPlanStates.NeedsClarification => FinanceConversationRunStates.NeedsClarification,
        FinanceToolPlanStates.Unsupported => FinanceConversationRunStates.Unsupported,
        _ => FinanceConversationRunStates.Failed
    };

    private static void AddRevision(ICollection<FinanceConversationPlanRevision> revisions, FinanceToolPlan plan) =>
        revisions.Add(new FinanceConversationPlanRevision(plan.PlanId, revisions.Count + 1, plan.State,
            plan.ReasonCode, plan.PlanningContextHash, plan.CreatedUtc));

    private static Dictionary<string, JsonNode?> Clone(IReadOnlyDictionary<string, JsonNode?> values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?>? values, string name) =>
        values is not null && values.TryGetValue(name, out var node) && node is JsonValue value &&
        value.TryGetValue<string>(out var text) ? text : null;

    private static bool? ReadBoolean(IReadOnlyDictionary<string, JsonNode?>? values, string name) =>
        values is not null && values.TryGetValue(name, out var node) && node is JsonValue value &&
        value.TryGetValue<bool>(out var result) ? result : null;

    private static string? FindString(IReadOnlyDictionary<string, JsonNode?>? values, string name) =>
        values is null ? null : FindNode(new JsonObject(Clone(values)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value)).ToArray()), name) is JsonValue value &&
            value.TryGetValue<string>(out var text) ? text : null;

    private static DateTime? FindDate(IReadOnlyDictionary<string, JsonNode?>? values)
    {
        foreach (var name in new[] { "asOfUtc", "updatedUtc", "generatedUtc" })
        {
            var text = FindString(values, name);
            if (DateTime.TryParse(text, out var date)) return date.ToUniversalTime();
        }
        return null;
    }

    private static JsonNode? FindNode(JsonNode? node, string name)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(name, out var direct) && direct is not null) return direct;
            foreach (var child in obj.Select(pair => pair.Value))
            {
                var found = FindNode(child, name);
                if (found is not null) return found;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                var found = FindNode(child, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Validate(ExecuteFinanceConversationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(request));
        if (request.AgentId == Guid.Empty) throw new ArgumentException("AgentId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserRequest)) throw new ArgumentException("UserRequest is required.", nameof(request));
        if (request.UserRequest.Trim().Length > 8_000) throw new ArgumentException("UserRequest is too long.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 128)
            throw new ArgumentException("A bounded idempotency key is required.", nameof(request));
        if (request.Context?.Any(item => item.CompanyId != request.CompanyId) == true)
            throw new ArgumentException("All context must belong to the requested company.", nameof(request));
    }
}
