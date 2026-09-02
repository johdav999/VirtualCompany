using System.Security.Cryptography;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyExecutor(
    VirtualCompanyDbContext dbContext,
    IFinanceAutonomyRunService runs,
    IFinanceDurableToolExecutionService tools,
    IFinanceAutonomyWorkflowOutcomeService workflowOutcomes,
    ICompanyDocumentStorage objectStorage,
    IServiceScopeFactory scopes) : IFinanceAutonomyExecutor
{
    private const string ObjectArtifactSourceType = "object_artifact";

    public async Task<FinanceAutonomyExecutorBatchResult> ProcessBatchAsync(
        DateTime utcNow, string workerId, int batchSize, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("WorkerId is required.", nameof(workerId));
        var now = Utc(utcNow);
        var boundedBatch = Math.Clamp(batchSize, 1, 100);
        var raw = await dbContext.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(step =>
                (step.Status == FinanceAutonomyStepStatus.Queued ||
                 (step.Status == FinanceAutonomyStepStatus.Running && step.LeaseExpiresUtc <= now)) &&
                (step.Run.Status == FinanceAutonomyRunStatus.Queued || step.Run.Status == FinanceAutonomyRunStatus.Running))
            .OrderBy(step => step.UpdatedUtc)
            .ThenBy(step => step.Run.CreatedUtc)
            .ThenBy(step => step.Sequence)
            .Select(step => new Candidate(step.CompanyId, step.RunId, step.Id, step.StepKey,
                step.Run.EvidenceHash, step.Run.EvidenceObservedUtc, step.Run.PlanJson, step.Run.PlanHash,
                step.Run.PlanVersion, step.Run.BudgetHash, step.Run.PolicyVersion, step.Run.CatalogueVersion,
                step.Run.AgentId, step.Run.GrantId, step.Run.GrantVersionId, step.Run.GrantVersionNumber,
                step.Run.CapabilityId, step.Run.Trigger,
                step.Run.CorrelationId, step.Run.WorkflowInstanceId, step.AttemptCount, step.MaximumAttempts,
                step.ActionClass, step.RequestedEffectHash, step.BusinessIdempotencyKey,
                step.Run.Steps.Count, step.UpdatedUtc))
            .Take(Math.Min(800, boundedBatch * 8))
            .ToListAsync(cancellationToken);
        var candidates = FairTake(raw, boundedBatch);
        var counts = new MutableCounts { Considered = candidates.Count };
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessCandidateAsync(candidate, workerId.Trim(), now, counts, cancellationToken);
        }
        return counts.ToResult();
    }

    private async Task ProcessCandidateAsync(Candidate candidate, string workerId, DateTime now, MutableCounts counts,
        CancellationToken cancellationToken)
    {
        var definition = ParseDefinition(candidate);
        if (definition is null)
        {
            await BlockUnclaimableAsync(candidate, workerId, FinanceAutonomyRunReasonCodes.PermanentFailure,
                "The immutable step payload is missing or corrupt and cannot be executed safely.", counts, cancellationToken);
            return;
        }

        var leaseToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var leaseSeconds = 90;
        var evidenceIsCurrent = await AuthoritativeEvidenceIsCurrentAsync(candidate, cancellationToken);
        var lease = await runs.ClaimStepAsync(candidate.CompanyId,
            new(candidate.RunId, candidate.StepId, workerId, leaseToken, leaseSeconds,
                evidenceIsCurrent ? candidate.EvidenceHash : Hash($"stale:{candidate.RunId:N}:{candidate.StepId:N}")),
            cancellationToken);
        if (lease is null) return;
        counts.Claimed++;

        try
        {
            var artifactFailure = await ValidateArtifactsAsync(candidate, cancellationToken);
            if (artifactFailure is not null)
            {
                await ReleaseAsync(candidate, leaseToken, "blocked", artifactFailure.Value.Code,
                    artifactFailure.Value.Summary, null, counts, cancellationToken);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReleaseAsync(candidate, leaseToken, "queued", FinanceAutonomyRunReasonCodes.TransientFailure,
                "Object evidence could not be read. The bounded worker retry remains pending.", null, counts, cancellationToken);
            return;
        }

        var actorUserId = await dbContext.FinanceAutonomyGrantVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(version => version.CompanyId == candidate.CompanyId && version.Id == candidate.GrantVersionId)
            .Select(version => version.ReviewedByUserId ?? version.CreatedByUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (actorUserId == Guid.Empty)
        {
            await ReleaseAsync(candidate, leaseToken, "blocked", FinanceAutonomyRunReasonCodes.PermanentFailure,
                "The persisted actor for this autonomous grant is unavailable.", null, counts, cancellationToken);
            return;
        }

        var actionType = ParseActionType(definition.ActionClass);
        var payload = definition.RequestPayload is null
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : definition.RequestPayload.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
        var businessKey = string.IsNullOrWhiteSpace(definition.BusinessIdempotencyKey)
            ? $"finance-autonomy:{candidate.CompanyId:N}:{candidate.RunId:N}:{candidate.StepId:N}"
            : definition.BusinessIdempotencyKey.Trim();
        if (payload.ContainsKey("idempotencyKey")) payload["idempotencyKey"] = JsonValue.Create(businessKey);

        var command = new ExecuteAgentToolCommand(
            definition.ToolName, actionType.ToStorageValue(), definition.Scope, payload,
            definition.ThresholdCategory, definition.ThresholdKey, definition.ThresholdValue,
            definition.SensitiveAction, definition.WorkTaskId, candidate.WorkflowInstanceId,
            businessKey, definition.DelegationAuthorityId, lease.AuthorityVersion, lease.AuthorityHash,
            new FinanceAutonomyApprovalContextDto(
                candidate.RunId, candidate.StepId, candidate.StepKey, candidate.GrantId,
                candidate.GrantVersionId, candidate.GrantVersionNumber, candidate.CapabilityId,
                candidate.Trigger, candidate.PlanHash, candidate.PlanVersion, candidate.RequestedEffectHash,
                candidate.EvidenceHash, candidate.EvidenceObservedUtc, candidate.BudgetHash,
                candidate.PolicyVersion, candidate.CatalogueVersion, candidate.BusinessIdempotencyKey,
                lease.AttemptNumber, candidate.ActionCount));

        ExecuteAgentToolResultDto result;
        try
        {
            result = await ExecuteWithHeartbeatAsync(candidate, leaseToken, leaseSeconds,
                () => tools.ExecuteDurableAsync(candidate.CompanyId, candidate.AgentId, actorUserId, command, cancellationToken),
                cancellationToken);
        }
        catch (LeaseLostException)
        {
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAfterUnexpectedFailureAsync(candidate, leaseToken, actionType,
                "Worker shutdown interrupted tool acknowledgement.", counts, CancellationToken.None);
            return;
        }
        catch (Exception)
        {
            await ReleaseAfterUnexpectedFailureAsync(candidate, leaseToken, actionType,
                "Tool execution ended without a trustworthy structured outcome.", counts, cancellationToken);
            return;
        }

        var structuredStatus = ReadString(result.ExecutionResult, "status") ?? result.Status;
        var safeSummary = Truncate(result.Message, 1000);
        if (string.Equals(structuredStatus, ToolExecutionStatus.Executed.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Status, ToolExecutionStatus.Executed.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            var effectJson = CanonicalJson(result.ExecutionResult);
            var effectStatus = actionType == ToolActionType.Execute ? "internal_effect" : "no_effect";
            var templateCode = ReadTemplateCode(candidate.StepKey);
            if (templateCode is not null)
            {
                var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(templateCode)
                    ?? throw new InvalidOperationException("The reviewed Finance workflow template is unavailable.");
                var outcome = ClassifyWorkflowOutcome(candidate, template, result.ExecutionResult, now);
                FinanceAutonomyWorkflowOutcomeResult materialized;
                try
                {
                    materialized = await workflowOutcomes.MaterializeAsync(candidate.CompanyId,
                        new(candidate.RunId, candidate.StepId, template.Code, outcome, safeSummary), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ReleaseAsync(candidate, leaseToken, "queued", FinanceAutonomyRunReasonCodes.TransientFailure,
                        "The Finance review output could not be persisted idempotently; the step remains retryable.",
                        result.ExecutionId, counts, cancellationToken);
                    return;
                }
                if (materialized.Created || materialized.Reopened || materialized.Resolved)
                    effectStatus = "internal_review_work";
            }
            await runs.CompleteStepAsync(candidate.CompanyId,
                new(candidate.RunId, candidate.StepId, leaseToken, result.ExecutionId, Hash(effectJson),
                    effectStatus, safeSummary), cancellationToken);
            counts.Completed++;
            FinanceAutonomyExecutorTelemetry.Record("completed", FinanceAutonomyRunReasonCodes.StepCompleted);
            return;
        }

        if (result.ApprovalRequestId.HasValue ||
            string.Equals(structuredStatus, ToolExecutionStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            if (!result.ApprovalRequestId.HasValue)
            {
                await ReleaseAsync(candidate, leaseToken, "blocked", FinanceAutonomyRunReasonCodes.PermanentFailure,
                    "The tool reported approval was required without a durable approval reference.", result.ExecutionId,
                    counts, cancellationToken);
                return;
            }
            await runs.AwaitApprovalStepAsync(candidate.CompanyId,
                new(candidate.RunId, candidate.StepId, leaseToken, result.ApprovalRequestId.Value,
                    result.ExecutionId, safeSummary), cancellationToken);
            counts.AwaitingApproval++;
            FinanceAutonomyExecutorTelemetry.Record("awaiting_approval", FinanceAutonomyRunReasonCodes.ApprovalRequired);
            return;
        }

        if (string.Equals(structuredStatus, ToolExecutionStatus.ReconciliationRequired.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
            actionType == ToolActionType.Execute && string.Equals(result.Status, ToolExecutionStatus.Failed.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            await ReleaseAsync(candidate, leaseToken, "reconciling", FinanceAutonomyRunReasonCodes.AmbiguousOutcome,
                "The provider outcome is ambiguous. Reconcile the stable business request before any retry.",
                result.ExecutionId, counts, cancellationToken, ExtractProviderReference(result.ExecutionResult));
            return;
        }

        if (string.Equals(result.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Status, ToolExecutionStatus.Rejected.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            await ReleaseAsync(candidate, leaseToken, "blocked", FinanceAutonomyRunReasonCodes.PermanentFailure,
                safeSummary, result.ExecutionId, counts, cancellationToken);
            return;
        }

        await ReleaseAsync(candidate, leaseToken, "queued", FinanceAutonomyRunReasonCodes.TransientFailure,
            safeSummary, result.ExecutionId, counts, cancellationToken);
    }

    private async Task<ExecuteAgentToolResultDto> ExecuteWithHeartbeatAsync(
        Candidate candidate, string leaseToken, int leaseSeconds,
        Func<Task<ExecuteAgentToolResultDto>> execute, CancellationToken cancellationToken)
    {
        var execution = execute();
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(5, leaseSeconds / 3));
        while (true)
        {
            var tick = Task.Delay(heartbeatInterval, cancellationToken);
            if (await Task.WhenAny(execution, tick) == execution) return await execution;
            await using var scope = scopes.CreateAsyncScope();
            var scopedRuns = scope.ServiceProvider.GetRequiredService<IFinanceAutonomyRunService>();
            if (!await scopedRuns.HeartbeatStepAsync(candidate.CompanyId,
                    new(candidate.RunId, candidate.StepId, leaseToken, leaseSeconds), cancellationToken))
                throw new LeaseLostException();
        }
    }

    private async Task<(string Code, string Summary)?> ValidateArtifactsAsync(
        Candidate candidate, CancellationToken cancellationToken)
    {
        var sources = await dbContext.FinanceAutonomyRunSources.IgnoreQueryFilters().AsNoTracking()
            .Where(source => source.CompanyId == candidate.CompanyId && source.RunId == candidate.RunId &&
                             source.SourceType == ObjectArtifactSourceType)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            var companyPrefix = $"companies/{candidate.CompanyId:N}/";
            if (!source.EntityId.Replace('\\', '/').StartsWith(companyPrefix, StringComparison.OrdinalIgnoreCase))
                return (FinanceAutonomyRunReasonCodes.ArtifactMissing,
                    "A generated object reference is outside the company storage boundary.");
            Stream stream;
            try { stream = await objectStorage.OpenReadAsync(source.EntityId, cancellationToken); }
            catch (FileNotFoundException)
            {
                return (FinanceAutonomyRunReasonCodes.ArtifactMissing,
                    "A required generated object is missing and the step cannot claim success.");
            }
            await using (stream)
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!string.Equals(actualHash, source.ContentHash, StringComparison.OrdinalIgnoreCase))
                    return (FinanceAutonomyRunReasonCodes.ArtifactCorrupt,
                        "A required generated object failed its integrity check and the step cannot claim success.");
            }
        }
        return null;
    }

    private async Task<bool> AuthoritativeEvidenceIsCurrentAsync(
        Candidate candidate, CancellationToken cancellationToken)
    {
        var sources = await dbContext.FinanceAutonomyRunSources.IgnoreQueryFilters().AsNoTracking()
            .Where(source => source.CompanyId == candidate.CompanyId && source.RunId == candidate.RunId &&
                             source.SourceType == "authoritative_event")
            .Select(source => new { source.EntityType, source.EntityId, source.SourceVersion, source.ContentHash })
            .ToListAsync(cancellationToken);
        foreach (var group in sources.GroupBy(source => new { source.EntityType, source.EntityId }))
        {
            var latest = await dbContext.FinanceAutonomyTriggerEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(signal => signal.CompanyId == candidate.CompanyId &&
                                 signal.SourceEntityType == group.Key.EntityType &&
                                 signal.SourceEntityId == group.Key.EntityId)
                .OrderByDescending(signal => signal.OccurredUtc)
                .ThenByDescending(signal => signal.CreatedUtc)
                .Select(signal => new { signal.SourceEventVersion, signal.ContentHash })
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is null || !group.Any(source =>
                    string.Equals(source.SourceVersion, latest.SourceEventVersion, StringComparison.Ordinal) &&
                    string.Equals(source.ContentHash, latest.ContentHash, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private async Task ReleaseAfterUnexpectedFailureAsync(Candidate candidate, string leaseToken,
        ToolActionType actionType, string summary, MutableCounts counts, CancellationToken cancellationToken) =>
        await ReleaseAsync(candidate, leaseToken,
            actionType == ToolActionType.Execute ? "reconciling" : "queued",
            actionType == ToolActionType.Execute
                ? FinanceAutonomyRunReasonCodes.AmbiguousOutcome
                : FinanceAutonomyRunReasonCodes.TransientFailure,
            summary, null, counts, cancellationToken);

    private async Task BlockUnclaimableAsync(Candidate candidate, string workerId, string reasonCode,
        string summary, MutableCounts counts, CancellationToken cancellationToken)
    {
        var leaseToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var lease = await runs.ClaimStepAsync(candidate.CompanyId,
            new(candidate.RunId, candidate.StepId, workerId, leaseToken, 90, candidate.EvidenceHash), cancellationToken);
        if (lease is null) return;
        counts.Claimed++;
        await ReleaseAsync(candidate, leaseToken, "blocked", reasonCode, summary, null, counts, cancellationToken);
    }

    private async Task ReleaseAsync(Candidate candidate, string leaseToken, string nextStatus,
        string reasonCode, string summary, Guid? toolAttemptId, MutableCounts counts,
        CancellationToken cancellationToken, string? reconciliationReference = null)
    {
        var updated = await runs.ReleaseStepAsync(candidate.CompanyId,
            new(candidate.RunId, candidate.StepId, leaseToken, nextStatus, reasonCode,
                Truncate(summary, 1000), toolAttemptId, ReconciliationReference: reconciliationReference), cancellationToken);
        var step = updated.Steps.Single(x => x.Id == candidate.StepId);
        switch (step.Status)
        {
            case "queued": counts.Retried++; break;
            case "reconciling": counts.Reconciling++; break;
            case "dead_lettered": counts.DeadLettered++; break;
            default: counts.Blocked++; break;
        }
        FinanceAutonomyExecutorTelemetry.Record(step.Status, reasonCode);
    }

    private static FinanceAutonomyRunPlanStepDefinition? ParseDefinition(Candidate candidate)
    {
        try
        {
            return JsonSerializer.Deserialize<List<FinanceAutonomyRunPlanStepDefinition>>(candidate.PlanJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?.SingleOrDefault(step => string.Equals(step.StepKey, candidate.StepKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<Candidate> FairTake(IReadOnlyList<Candidate> source, int count)
    {
        var queues = source.GroupBy(x => x.CompanyId).OrderBy(x => x.Min(c => c.UpdatedUtc))
            .Select(group => new Queue<Candidate>(group)).ToList();
        var result = new List<Candidate>(count);
        while (result.Count < count && queues.Count > 0)
        {
            for (var index = 0; index < queues.Count && result.Count < count;)
            {
                result.Add(queues[index].Dequeue());
                if (queues[index].Count == 0) queues.RemoveAt(index); else index++;
            }
        }
        return result;
    }

    private static ToolActionType ParseActionType(string actionClass) =>
        actionClass.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
        actionClass.Contains("mutation", StringComparison.OrdinalIgnoreCase) ||
        actionClass.Contains("write", StringComparison.OrdinalIgnoreCase)
            ? ToolActionType.Execute
            : actionClass.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
              actionClass.Contains("draft", StringComparison.OrdinalIgnoreCase)
                ? ToolActionType.Recommend
                : ToolActionType.Read;

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?>? payload, string key) =>
        payload is not null && payload.TryGetValue(key, out var value) && value is JsonValue json &&
        json.TryGetValue<string>(out var text) ? text : null;

    internal static string ClassifyWorkflowOutcomeForTest(
        string trigger, DateTime evidenceObservedUtc, FinanceAutonomyWorkflowTemplate template,
        IReadOnlyDictionary<string, JsonNode?>? result, DateTime utcNow) =>
        ClassifyWorkflowOutcome(new Candidate(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty,
            string.Empty, evidenceObservedUtc, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, Guid.Empty, Guid.Empty, Guid.Empty, 0, template.CapabilityId,
            trigger, string.Empty, null, 0, 0, template.ActionClass, string.Empty, string.Empty, 1, utcNow),
            template, result, utcNow);

    private static string ClassifyWorkflowOutcome(Candidate candidate,
        FinanceAutonomyWorkflowTemplate template, IReadOnlyDictionary<string, JsonNode?>? result,
        DateTime utcNow)
    {
        if (Utc(utcNow) - Utc(candidate.EvidenceObservedUtc) >
            TimeSpan.FromMinutes(template.Limits.EvidenceFreshnessMinutes))
            return FinanceAutonomyWorkflowOutcomeStates.Stale;
        if (candidate.Trigger == FinanceAutonomyTriggers.BusinessEvent)
            return FinanceAutonomyWorkflowOutcomeStates.Exception;
        if (result is null || result.Count == 0)
            return FinanceAutonomyWorkflowOutcomeStates.Missing;

        var flags = new OutcomeFlags();
        foreach (var item in result) Inspect(item.Key, item.Value, flags);
        if (flags.Missing) return FinanceAutonomyWorkflowOutcomeStates.Missing;
        if (flags.Stale) return FinanceAutonomyWorkflowOutcomeStates.Stale;
        if (flags.Exception) return FinanceAutonomyWorkflowOutcomeStates.Exception;
        return FinanceAutonomyWorkflowOutcomeStates.Healthy;
    }

    private static void Inspect(string key, JsonNode? node, OutcomeFlags flags)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (node is JsonObject obj)
        {
            foreach (var item in obj) Inspect(item.Key, item.Value, flags);
            return;
        }
        if (node is JsonArray array)
        {
            if (array.Count > 0 && IsExceptionCollection(normalized)) flags.Exception = true;
            foreach (var child in array) Inspect(key, child, flags);
            return;
        }
        if (!IsTruthy(node)) return;
        if (normalized.Contains("missing", StringComparison.Ordinal)) flags.Missing = true;
        else if (normalized.Contains("stale", StringComparison.Ordinal) ||
                 normalized.Contains("expired", StringComparison.Ordinal)) flags.Stale = true;
        else if (normalized.Contains("exception", StringComparison.Ordinal) ||
                 normalized.Contains("blocker", StringComparison.Ordinal) ||
                 normalized.Contains("blocking", StringComparison.Ordinal) ||
                 normalized.Contains("overdue", StringComparison.Ordinal) ||
                 normalized.Contains("uncategorized", StringComparison.Ordinal) ||
                 normalized.Contains("failed", StringComparison.Ordinal) ||
                 normalized.Contains("requiresreview", StringComparison.Ordinal) ||
                 normalized.Contains("attention", StringComparison.Ordinal)) flags.Exception = true;
    }

    private static bool IsExceptionCollection(string key) => key is
        "transactions" or "items" or "recommendations" or "risks" or "obligations" or
        "blockers" or "exceptions" or "groups" or "jobs" or "failures";

    private static bool IsTruthy(JsonNode? node)
    {
        if (node is not JsonValue value) return false;
        if (value.TryGetValue<bool>(out var boolean)) return boolean;
        if (value.TryGetValue<int>(out var integer)) return integer > 0;
        if (value.TryGetValue<long>(out var number)) return number > 0;
        if (value.TryGetValue<decimal>(out var amount)) return amount > 0;
        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) &&
               !text.Equals("healthy", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("completed", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("executed", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadTemplateCode(string stepKey)
    {
        const string prefix = "reviewed_template:";
        return stepKey.StartsWith(prefix, StringComparison.Ordinal) ? stepKey[prefix.Length..] : null;
    }

    private static string? ExtractProviderReference(IReadOnlyDictionary<string, JsonNode?>? payload)
    {
        if (payload is null) return null;
        foreach (var key in new[] { "providerReference", "provider_reference", "providerRequestId", "requestId" })
        {
            var value = ReadString(payload, key);
            if (!string.IsNullOrWhiteSpace(value)) return Truncate(value, 240);
        }
        return null;
    }

    private static string CanonicalJson(IReadOnlyDictionary<string, JsonNode?>? payload) =>
        JsonSerializer.Serialize(payload?.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) ?? []);
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Truncate(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? "The tool did not provide a safe outcome summary." :
        value.Trim().Length <= maximum ? value.Trim() : value.Trim()[..maximum];
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private sealed record Candidate(Guid CompanyId, Guid RunId, Guid StepId, string StepKey,
        string EvidenceHash, DateTime EvidenceObservedUtc, string PlanJson, string PlanHash,
        string PlanVersion, string BudgetHash, string PolicyVersion, string CatalogueVersion,
        Guid AgentId, Guid GrantId, Guid GrantVersionId, int GrantVersionNumber,
        string CapabilityId, string Trigger, string CorrelationId,
        Guid? WorkflowInstanceId, int AttemptCount, int MaximumAttempts, string ActionClass,
        string RequestedEffectHash, string BusinessIdempotencyKey, int ActionCount, DateTime UpdatedUtc);

    private sealed class MutableCounts
    {
        public int Considered, Claimed, Completed, AwaitingApproval, Retried, Reconciling, Blocked, DeadLettered;
        public FinanceAutonomyExecutorBatchResult ToResult() =>
            new(Considered, Claimed, Completed, AwaitingApproval, Retried, Reconciling, Blocked, DeadLettered);
    }

    private sealed class OutcomeFlags
    {
        public bool Missing;
        public bool Stale;
        public bool Exception;
    }

    private sealed class LeaseLostException : Exception;
}

internal static class FinanceAutonomyExecutorTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.FinanceAutonomy.Executor", "1.0.0");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("finance.autonomy.executor.outcomes");

    public static void Record(string outcome, string reasonCode) => Outcomes.Add(1,
        new KeyValuePair<string, object?>("outcome", outcome),
        new KeyValuePair<string, object?>("reason_code", reasonCode));
}
