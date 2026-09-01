using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceConversationRunOptions
{
    public const string SectionName = "FinanceConversationRuns";
    public bool WorkerEnabled { get; set; } = true;
    public int BatchSize { get; set; } = 10;
    public int PollIntervalSeconds { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 60;
    public int ConfirmationLifetimeSeconds { get; set; } = 300;
    public int RetentionDays { get; set; } = 90;
}

public sealed class FinanceConversationRunService : IFinanceConversationRunService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IFinanceToolPlanner _planner;
    private readonly IFinanceConversationRunProcessor _processor;
    private readonly IAgentEffectiveAuthorityResolver _authority;
    private readonly ICompanyToolRegistry _registry;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;
    private readonly FinanceConversationRunOptions _options;

    public FinanceConversationRunService(VirtualCompanyDbContext db, IFinanceToolPlanner planner,
        IFinanceConversationRunProcessor processor, IAgentEffectiveAuthorityResolver authority,
        ICompanyToolRegistry registry, ICurrentUserAccessor currentUser, IAuditEventWriter audit,
        TimeProvider clock, IOptions<FinanceConversationRunOptions> options)
    {
        _db = db; _planner = planner; _processor = processor; _authority = authority; _registry = registry;
        _currentUser = currentUser; _audit = audit; _clock = clock; _options = options.Value;
    }

    public async Task<FinanceConversationRunDto> StartAsync(StartFinanceConversationRunRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var actor = RequireActor();
        var normalizedKey = request.IdempotencyKey.Trim();
        var requestHash = Hash(JsonSerializer.Serialize(new
        {
            request.CompanyId, request.AgentId, UserRequest = request.UserRequest.Trim(), request.TaskId,
            request.ConversationId, request.WorkflowInstanceId, request.DelegationAuthorityId, request.References
        }));
        var existing = await _db.FinanceConversationRuns.IgnoreQueryFilters()
            .Include(x => x.Steps).Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.AgentId == request.AgentId &&
                                       x.IdempotencyKey == normalizedKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new AgentAiConflictException("The idempotency key belongs to a different Finance conversation run.");
            return Map(existing);
        }

        var plan = await _planner.PlanAsync(new FinanceToolPlanRequest(request.CompanyId, request.AgentId,
            request.UserRequest, request.Context, request.TaskId, request.ConversationId, request.CorrelationId,
            request.References), cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        var run = new FinanceConversationRun(Guid.NewGuid(), request.CompanyId, request.AgentId, actor,
            normalizedKey, requestHash, plan.CorrelationId, plan.EffectiveAuthorityVersion,
            plan.EffectiveAuthorityHash, plan.PlanningContextVersion, plan.PlanningContextHash, now,
            now.AddDays(Math.Clamp(_options.RetentionDays, 7, 365)), request.TaskId, request.ConversationId,
            request.WorkflowInstanceId, request.DelegationAuthorityId);
        run.Revisions.Add(new FinanceConversationRunRevision(Guid.NewGuid(), request.CompanyId, run.Id,
            plan.Revision, plan.PlanId, plan.State, plan.ReasonCode, plan.PlanningContextHash,
            FinanceConversationRunSerialization.Evidence(plan.GroundedEvidence), now));

        foreach (var step in plan.Steps.OrderBy(x => x.Order))
        {
            var args = FinanceConversationRunSerialization.SafeArguments(step.NormalizedArguments);
            var argsHash = FinanceApprovalContinuationBinding.ComputePayloadHash(args);
            var initial = step.ActionType == ToolActionType.Execute.ToStorageValue()
                ? FinanceConversationRunStepStatuses.AwaitingConfirmation
                : step.Dependencies.Count == 0
                    ? FinanceConversationRunStepStatuses.Ready
                    : FinanceConversationRunStepStatuses.Planned;
            run.Steps.Add(new FinanceConversationRunStep(Guid.NewGuid(), request.CompanyId, run.Id,
                step.StepId, step.Order, JsonSerializer.Serialize(step.Dependencies), step.ToolName,
                step.ToolVersion, step.ActionType, step.Scope, JsonSerializer.Serialize(args), argsHash,
                FinanceConversationRunSerialization.SafeText(step.ExpectedEffect, 1000),
                FinanceConversationRunSerialization.Evidence(plan.GroundedEvidence.Where(evidence =>
                    step.EvidenceRequirements.Contains(evidence.SourceId, StringComparer.Ordinal))),
                $"finance-run:{request.CompanyId:N}:{run.Id:N}:{step.StepId}:{step.ToolVersion}", initial, now));
        }

        var runState = plan.State switch
        {
            FinanceToolPlanStates.NeedsClarification => FinanceConversationRunStatuses.AwaitingClarification,
            FinanceToolPlanStates.Unsupported or FinanceToolPlanStates.Failed => FinanceConversationRunStatuses.Failed,
            _ when run.Steps.Any(x => x.Status == FinanceConversationRunStepStatuses.AwaitingConfirmation) => FinanceConversationRunStatuses.AwaitingConfirmation,
            _ => FinanceConversationRunStatuses.Ready
        };
        run.SetState(runState, plan.SafeExplanation, now, plan.ReasonCode);
        _db.FinanceConversationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(run, AuditEventActions.FinanceConversationRunCreated, AuditEventOutcomes.Succeeded,
            "A durable governed Finance conversation run was created.", cancellationToken);

        if (run.Status == FinanceConversationRunStatuses.Ready)
            await _processor.ProcessRunAsync(run.CompanyId, run.Id, cancellationToken);
        return await GetAsync(run.CompanyId, run.AgentId, run.Id, cancellationToken);
    }

    public async Task<FinanceConversationRunDto> GetAsync(Guid companyId, Guid agentId, Guid runId,
        CancellationToken cancellationToken) => Map(await LoadAsync(companyId, agentId, runId, cancellationToken));

    public async Task<FinanceConversationRunListResult> ListAsync(Guid companyId, Guid agentId, int take,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        take = Math.Clamp(take, 1, 100);
        var total = await _db.FinanceConversationRuns.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.AgentId == agentId, cancellationToken);
        var items = await _db.FinanceConversationRuns.IgnoreQueryFilters().AsNoTracking().Include(x => x.Steps).Include(x => x.Revisions)
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId).OrderByDescending(x => x.CreatedUtc)
            .Take(take).ToListAsync(cancellationToken);
        return new FinanceConversationRunListResult(items.Select(Map).ToArray(), total);
    }

    public async Task<FinanceConversationRunDto> ConfirmStepAsync(ConfirmFinanceConversationRunStepRequest request,
        CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var run = await LoadAsync(request.CompanyId, request.AgentId, request.RunId, cancellationToken);
        if (run.InitiatingUserId != actor) throw new UnauthorizedAccessException("Only the initiating actor may confirm this step.");
        if (run.CancelledUtc.HasValue || run.SupersededByRunId.HasValue) throw new InvalidOperationException("The run is no longer confirmable.");
        var step = run.Steps.SingleOrDefault(x => x.StepKey == request.StepId)
                   ?? throw new KeyNotFoundException("Finance run step was not found.");
        if (step.Version != request.ExpectedStepVersion) throw new DbUpdateConcurrencyException("The Finance run step changed. Refresh before confirming.");
        if (!_registry.TryGetTool(step.ToolName, out var registration) || registration.FinanceRiskClassification is null)
            throw new InvalidOperationException("The Finance tool contract is no longer current.");
        var currentAuthority = await _authority.ResolveAsync(run.CompanyId, run.AgentId, cancellationToken);
        if (!string.Equals(currentAuthority.AuthorityHash, run.EffectiveAuthorityHash, StringComparison.Ordinal))
        {
            step.MarkStale("finance_run_authority_stale", "Agent authority changed. Refresh and supersede this run.", _clock.GetUtcNow().UtcDateTime);
            run.SetState(FinanceConversationRunStatuses.Stale, "Agent authority changed before confirmation.", _clock.GetUtcNow().UtcDateTime, "finance_run_authority_stale");
            await _db.SaveChangesAsync(cancellationToken);
            return Map(run);
        }

        var payload = FinanceConversationRunSerialization.ParseArguments(step.NormalizedArgumentsJson);
        var attempt = new ToolExecutionAttempt(Guid.NewGuid(), run.CompanyId, run.AgentId, step.ToolName,
            ToolActionType.Execute, step.Scope, payload, run.TaskId, run.WorkflowInstanceId, run.CorrelationId,
            toolVersion: step.ToolVersion);
        var snapshot = await FinanceApprovalContinuationBinding.BuildTargetSnapshotAsync(_db, attempt, cancellationToken);
        var integration = await FinanceApprovalContinuationBinding.BuildIntegrationStateHashAsync(_db, run.CompanyId,
            registration.FinanceRiskClassification, cancellationToken);
        var confirmationHash = Hash(FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(snapshot) + "|" + integration);
        var now = _clock.GetUtcNow().UtcDateTime;
        step.Confirm(actor, step.NormalizedArgumentsHash, confirmationHash, currentAuthority.AuthorityHash, now,
            now.AddSeconds(Math.Clamp(_options.ConfirmationLifetimeSeconds, 30, 900)));
        run.SetState(FinanceConversationRunStatuses.Ready,
            "The exact stored step was confirmed and is ready for P0-revalidated continuation.", now);
        await _db.SaveChangesAsync(cancellationToken);
        await _processor.ProcessRunAsync(run.CompanyId, run.Id, cancellationToken);
        return await GetAsync(run.CompanyId, run.AgentId, run.Id, cancellationToken);
    }

    public async Task<FinanceConversationRunDto> CancelAsync(CancelFinanceConversationRunRequest request,
        CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var run = await LoadAsync(request.CompanyId, request.AgentId, request.RunId, cancellationToken);
        if (run.InitiatingUserId != actor) throw new UnauthorizedAccessException("Only the initiating actor may cancel this run.");
        var now = _clock.GetUtcNow().UtcDateTime;
        run.Cancel(actor, request.Reason, now);
        foreach (var step in run.Steps.Where(x => !FinanceConversationRunStepStatuses.Terminal.Contains(x.Status))) step.Cancel(now);
        await _db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(run, AuditEventActions.FinanceConversationRunCancelled, AuditEventOutcomes.Succeeded,
            run.SafeSummary, cancellationToken);
        return Map(run);
    }

    public async Task<FinanceConversationRunDto> SupersedeAsync(SupersedeFinanceConversationRunRequest request,
        CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var current = await LoadAsync(request.CompanyId, request.AgentId, request.RunId, cancellationToken);
        if (current.InitiatingUserId != actor) throw new UnauthorizedAccessException("Only the initiating actor may supersede this run.");
        var now = _clock.GetUtcNow().UtcDateTime;
        current.Cancel(actor, request.Reason, now);
        foreach (var step in current.Steps.Where(x => !FinanceConversationRunStepStatuses.Terminal.Contains(x.Status))) step.Cancel(now);
        await _db.SaveChangesAsync(cancellationToken);
        var replacement = await StartAsync(request.Replacement with { CompanyId = request.CompanyId }, cancellationToken);
        current = await LoadAsync(request.CompanyId, request.AgentId, request.RunId, cancellationToken);
        current.Supersede(replacement.Id, actor, now);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(current);
    }

    private async Task<FinanceConversationRun> LoadAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) =>
        await _db.FinanceConversationRuns.IgnoreQueryFilters().Include(x => x.Steps).ThenInclude(x => x.Attempts).Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.AgentId == agentId && x.Id == runId, cancellationToken)
        ?? throw new KeyNotFoundException("Finance conversation run was not found.");

    internal static FinanceConversationRunDto Map(FinanceConversationRun run) => new(
        run.Id, FinanceConversationRunContractVersions.V1, run.CompanyId, run.AgentId, run.InitiatingUserId,
        run.IdempotencyKey, run.CorrelationId, run.Status, run.SafeSummary, run.FinalOutcomeCode,
        run.SupersededByRunId, run.CancelledUtc, run.LeaseExpiresUtc, run.RetainUntilUtc, run.RedactedUtc,
        run.Version, run.Revisions.OrderBy(x => x.Revision).Select(x => new FinanceConversationRunRevisionDto(
            x.Revision, x.PlanId, x.PlanState, x.ReasonCode, x.PlanningContextHash,
            FinanceConversationRunSerialization.ParseEvidence(x.EvidenceReferencesJson), x.CreatedUtc)).ToArray(),
        run.Steps.OrderBy(x => x.Sequence).Select(x => new FinanceConversationRunStepDto(x.Id, x.StepKey,
            x.Sequence, JsonSerializer.Deserialize<string[]>(x.DependenciesJson) ?? [], x.ToolName, x.ToolVersion,
            x.ActionType, x.Scope, x.ExpectedEffect, x.Status, x.BusinessIdempotencyKey, x.AttemptCount,
            x.MaxAttempts, x.ToolExecutionAttemptId, x.ApprovalRequestId, x.ConfirmedUtc, x.LeaseExpiresUtc,
            x.FailureCode, x.SafeFailureSummary, FinanceConversationRunSerialization.ParseSummary(x.ResultSummaryJson),
            x.Version, x.UpdatedUtc)).ToArray(), run.CreatedUtc, run.UpdatedUtc, run.CompletedUtc);

    private Task WriteAuditAsync(FinanceConversationRun run, string action, string outcome, string summary,
        CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId,
        AuditActorTypes.User, run.InitiatingUserId, action, AuditTargetTypes.AgentToolExecution, run.Id.ToString("N"),
        outcome, summary, ["finance_conversation_run", "finance_tool_plan"],
        new Dictionary<string, string?> { ["runStatus"] = run.Status, ["agentId"] = run.AgentId.ToString("N") },
        run.CorrelationId), cancellationToken);

    private Guid RequireActor() => _currentUser.UserId is { } id && id != Guid.Empty ? id
        : throw new UnauthorizedAccessException("An authenticated Finance actor is required.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Validate(StartFinanceConversationRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty || request.AgentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (string.IsNullOrWhiteSpace(request.UserRequest) || request.UserRequest.Trim().Length > 8000) throw new ArgumentException("A bounded user request is required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 128) throw new ArgumentException("A bounded idempotency key is required.");
    }
}

internal static class FinanceConversationRunSerialization
{
    private static readonly string[] SensitiveKeys = ["password", "secret", "token", "credential", "authorization", "apiKey", "accessKey"];
    public static Dictionary<string, JsonNode?> SafeArguments(IReadOnlyDictionary<string, JsonNode?> source) =>
        source.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key,
            x => SensitiveKeys.Any(key => x.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                ? (JsonNode?)JsonValue.Create("[redacted]") : SafeNode(x.Value), StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, JsonNode?> ParseArguments(string json) => SafeArguments(
        JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json) ?? new Dictionary<string, JsonNode?>());
    public static string Evidence(IEnumerable<FinancePlanningEvidenceReference> evidence) =>
        JsonSerializer.Serialize(evidence.Take(50).Select(x => new FinancePlanningEvidenceReference(x.SourceId,
            x.SourceVersion, x.EntityType, x.EntityId, SafeText(x.SafeLabel, 200), x.UpdatedUtc, x.IsFresh)).ToArray());
    public static IReadOnlyList<FinancePlanningEvidenceReference> ParseEvidence(string json) =>
        JsonSerializer.Deserialize<FinancePlanningEvidenceReference[]>(json) ?? [];
    public static IReadOnlyDictionary<string, JsonNode?>? ParseSummary(string? json) => string.IsNullOrWhiteSpace(json)
        ? null : JsonSerializer.Deserialize<Dictionary<string, JsonNode?>>(json);
    public static string SafeText(string value, int max) => string.Join(' ', value.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries))[..Math.Min(string.Join(' ', value.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries)).Length, max)];
    private static JsonNode? SafeNode(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(SafeText(text, 500)),
        JsonArray array => new JsonArray(array.Take(50).Select(SafeNode).ToArray()),
        JsonObject obj => new JsonObject(obj.Take(50).Select(x => KeyValuePair.Create(x.Key,
            SensitiveKeys.Any(key => x.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                ? (JsonNode?)JsonValue.Create("[redacted]") : SafeNode(x.Value))).ToArray()),
        _ => node.DeepClone()
    };
}
