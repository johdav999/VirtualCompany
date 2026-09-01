namespace VirtualCompany.Domain.Entities;

public static class FinanceConversationRunStatuses
{
    public const string Planned = "planned";
    public const string AwaitingClarification = "awaiting_clarification";
    public const string Ready = "ready";
    public const string Executing = "executing";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Queued = "queued";
    public const string Reconciling = "reconciling";
    public const string Completed = "completed";
    public const string PartiallyCompleted = "partially_completed";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
    public const string Failed = "failed";

    public static IReadOnlySet<string> Terminal { get; } = new HashSet<string>(StringComparer.Ordinal)
    { Completed, PartiallyCompleted, Cancelled, Stale, Failed };
}

public static class FinanceConversationRunStepStatuses
{
    public const string Planned = "planned";
    public const string Ready = "ready";
    public const string Executing = "executing";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Queued = "queued";
    public const string Reconciling = "reconciling";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
    public const string Failed = "failed";

    public static IReadOnlySet<string> Terminal { get; } = new HashSet<string>(StringComparer.Ordinal)
    { Completed, Blocked, Cancelled, Stale, Failed };
}

public sealed class FinanceConversationRun : ICompanyOwnedEntity
{
    private FinanceConversationRun() { }

    public FinanceConversationRun(Guid id, Guid companyId, Guid agentId, Guid initiatingUserId,
        string idempotencyKey, string requestHash, string correlationId, string authorityVersion,
        string authorityHash, string planningContextVersion, string planningContextHash,
        DateTime createdUtc, DateTime retainUntilUtc, Guid? taskId = null, Guid? conversationId = null,
        Guid? workflowInstanceId = null, Guid? delegationAuthorityId = null)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || initiatingUserId == Guid.Empty)
            throw new ArgumentException("Company, agent, and initiating user are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        InitiatingUserId = initiatingUserId;
        IdempotencyKey = Required(idempotencyKey, 128);
        RequestHash = Required(requestHash, 64);
        CorrelationId = Required(correlationId, 128);
        EffectiveAuthorityVersion = Required(authorityVersion, 128);
        EffectiveAuthorityHash = Required(authorityHash, 64);
        PlanningContextVersion = Required(planningContextVersion, 64);
        PlanningContextHash = Required(planningContextHash, 64);
        TaskId = EmptyToNull(taskId);
        ConversationId = EmptyToNull(conversationId);
        WorkflowInstanceId = EmptyToNull(workflowInstanceId);
        DelegationAuthorityId = EmptyToNull(delegationAuthorityId);
        Status = FinanceConversationRunStatuses.Planned;
        SafeSummary = "A governed Finance conversation run was planned.";
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        RetainUntilUtc = Utc(retainUntilUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid InitiatingUserId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public Guid? DelegationAuthorityId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string EffectiveAuthorityVersion { get; private set; } = null!;
    public string EffectiveAuthorityHash { get; private set; } = null!;
    public string PlanningContextVersion { get; private set; } = null!;
    public string PlanningContextHash { get; private set; } = null!;
    public string SafeSummary { get; private set; } = null!;
    public string? FinalOutcomeCode { get; private set; }
    public Guid? SupersededByRunId { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = 5;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime RetainUntilUtc { get; private set; }
    public DateTime? RedactedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceConversationRunStep> Steps { get; } = new List<FinanceConversationRunStep>();
    public ICollection<FinanceConversationRunRevision> Revisions { get; } = new List<FinanceConversationRunRevision>();

    public void SetState(string status, string safeSummary, DateTime utcNow, string? outcomeCode = null)
    {
        if (FinanceConversationRunStatuses.Terminal.Contains(Status)) return;
        Status = Required(status, 32);
        SafeSummary = Required(safeSummary, 2000);
        FinalOutcomeCode = Optional(outcomeCode, 100);
        UpdatedUtc = Utc(utcNow);
        if (FinanceConversationRunStatuses.Terminal.Contains(Status))
        {
            CompletedUtc = UpdatedUtc;
            ClearLease();
        }
        Version++;
    }

    public bool TryClaim(string owner, DateTime utcNow, TimeSpan duration)
    {
        utcNow = Utc(utcNow);
        if (FinanceConversationRunStatuses.Terminal.Contains(Status) || CancelledUtc.HasValue ||
            NextAttemptUtc > utcNow || (LeaseExpiresUtc > utcNow && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)))
            return false;
        LeaseOwner = Required(owner, 128);
        LeaseExpiresUtc = utcNow.Add(duration);
        UpdatedUtc = utcNow;
        Version++;
        return true;
    }

    public void ReleaseLease(string owner, DateTime utcNow, DateTime? nextAttemptUtc = null)
    {
        if (!string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return;
        ClearLease();
        NextAttemptUtc = nextAttemptUtc.HasValue ? Utc(nextAttemptUtc.Value) : null;
        UpdatedUtc = Utc(utcNow);
        Version++;
    }

    public void ScheduleRetry(string owner, DateTime utcNow, DateTime nextAttemptUtc)
    {
        if (!string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return;
        AttemptCount++;
        ReleaseLease(owner, utcNow, nextAttemptUtc);
    }

    public void Cancel(Guid userId, string reason, DateTime utcNow)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Cancelling user is required.", nameof(userId));
        if (FinanceConversationRunStatuses.Terminal.Contains(Status)) return;
        CancelledByUserId = userId;
        CancellationReason = Required(reason, 1000);
        CancelledUtc = Utc(utcNow);
        Status = FinanceConversationRunStatuses.Cancelled;
        SafeSummary = "The Finance run was cancelled. Completed external effects, if any, were not undone.";
        CompletedUtc = CancelledUtc;
        UpdatedUtc = CancelledUtc.Value;
        ClearLease();
        Version++;
    }

    public void Supersede(Guid replacementRunId, Guid userId, DateTime utcNow)
    {
        if (replacementRunId == Guid.Empty || replacementRunId == Id) throw new ArgumentException("Replacement run is required.");
        SupersededByRunId = replacementRunId;
        Cancel(userId, "Superseded by a newer governed Finance run.", utcNow);
    }

    public void MarkRedacted(DateTime utcNow)
    {
        if (RedactedUtc.HasValue) return;
        RedactedUtc = Utc(utcNow);
        SafeSummary = "Retained Finance run metadata was redacted; audit and execution links remain available.";
        UpdatedUtc = RedactedUtc.Value;
        Version++;
    }

    private void ClearLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
    private static Guid? EmptyToNull(Guid? value) => value == Guid.Empty ? null : value;
    internal static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    internal static string Required(string value, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A required value is missing.")
        : value.Trim()[..Math.Min(value.Trim().Length, max)];
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class FinanceConversationRunStep : ICompanyOwnedEntity
{
    private FinanceConversationRunStep() { }

    public FinanceConversationRunStep(Guid id, Guid companyId, Guid runId, string stepKey, int sequence,
        string dependenciesJson, string toolName, string toolVersion, string actionType, string scope,
        string normalizedArgumentsJson, string normalizedArgumentsHash, string expectedEffect,
        string evidenceReferencesJson, string businessIdempotencyKey, string initialStatus, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || runId == Guid.Empty || sequence <= 0) throw new ArgumentException("Invalid run step identity.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunId = runId;
        StepKey = FinanceConversationRun.Required(stepKey, 64);
        Sequence = sequence;
        DependenciesJson = FinanceConversationRun.Required(dependenciesJson, 4000);
        ToolName = FinanceConversationRun.Required(toolName, 100);
        ToolVersion = FinanceConversationRun.Required(toolVersion, 32);
        ActionType = FinanceConversationRun.Required(actionType, 16);
        Scope = FinanceConversationRun.Required(scope, 100);
        NormalizedArgumentsJson = FinanceConversationRun.Required(normalizedArgumentsJson, 16000);
        NormalizedArgumentsHash = FinanceConversationRun.Required(normalizedArgumentsHash, 64);
        ExpectedEffect = FinanceConversationRun.Required(expectedEffect, 1000);
        EvidenceReferencesJson = FinanceConversationRun.Required(evidenceReferencesJson, 16000);
        BusinessIdempotencyKey = FinanceConversationRun.Required(businessIdempotencyKey, 200);
        Status = FinanceConversationRun.Required(initialStatus, 32);
        CreatedUtc = FinanceConversationRun.Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public string StepKey { get; private set; } = null!;
    public int Sequence { get; private set; }
    public string DependenciesJson { get; private set; } = null!;
    public string ToolName { get; private set; } = null!;
    public string ToolVersion { get; private set; } = null!;
    public string ActionType { get; private set; } = null!;
    public string Scope { get; private set; } = null!;
    public string NormalizedArgumentsJson { get; private set; } = null!;
    public string NormalizedArgumentsHash { get; private set; } = null!;
    public string ExpectedEffect { get; private set; } = null!;
    public string EvidenceReferencesJson { get; private set; } = null!;
    public string? ResultSummaryJson { get; private set; }
    public string? PolicyDecisionSummaryJson { get; private set; }
    public string BusinessIdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? ToolExecutionAttemptId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public string? ConfirmationPayloadHash { get; private set; }
    public string? ConfirmationTargetSnapshotHash { get; private set; }
    public string? ConfirmationAuthorityHash { get; private set; }
    public DateTime? ConfirmedUtc { get; private set; }
    public DateTime? ConfirmationExpiresUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = 3;
    public string? FailureCode { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? RedactedUtc { get; private set; }
    public long Version { get; private set; }
    public FinanceConversationRun Run { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceConversationRunAttempt> Attempts { get; } = new List<FinanceConversationRunAttempt>();

    public bool TryClaim(string owner, DateTime utcNow, TimeSpan duration)
    {
        utcNow = FinanceConversationRun.Utc(utcNow);
        if (FinanceConversationRunStepStatuses.Terminal.Contains(Status) || Status == FinanceConversationRunStepStatuses.AwaitingConfirmation ||
            Status == FinanceConversationRunStepStatuses.AwaitingApproval || NextAttemptUtc > utcNow ||
            AttemptCount >= MaxAttempts ||
            (LeaseExpiresUtc > utcNow && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))) return false;
        LeaseOwner = FinanceConversationRun.Required(owner, 128);
        LeaseExpiresUtc = utcNow.Add(duration);
        Status = FinanceConversationRunStepStatuses.Executing;
        AttemptCount++;
        UpdatedUtc = utcNow;
        Version++;
        return true;
    }

    public void Confirm(Guid userId, string payloadHash, string targetSnapshotHash, string authorityHash,
        DateTime confirmedUtc, DateTime expiresUtc)
    {
        if (Status != FinanceConversationRunStepStatuses.AwaitingConfirmation) throw new InvalidOperationException("Step is not awaiting confirmation.");
        if (userId == Guid.Empty || !string.Equals(payloadHash, NormalizedArgumentsHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Confirmation does not match the stored step.");
        ConfirmedByUserId = userId;
        ConfirmationPayloadHash = payloadHash;
        ConfirmationTargetSnapshotHash = FinanceConversationRun.Required(targetSnapshotHash, 64);
        ConfirmationAuthorityHash = FinanceConversationRun.Required(authorityHash, 64);
        ConfirmedUtc = FinanceConversationRun.Utc(confirmedUtc);
        ConfirmationExpiresUtc = FinanceConversationRun.Utc(expiresUtc);
        Status = FinanceConversationRunStepStatuses.Ready;
        UpdatedUtc = ConfirmedUtc.Value;
        Version++;
    }

    public void SetReady(DateTime utcNow) => SetState(FinanceConversationRunStepStatuses.Ready, utcNow);
    public void AwaitConfirmation(DateTime utcNow) => SetState(FinanceConversationRunStepStatuses.AwaitingConfirmation, utcNow);
    public void AwaitApproval(Guid executionId, Guid approvalId, string policyJson, DateTime utcNow)
    { ToolExecutionAttemptId = executionId; ApprovalRequestId = approvalId; PolicyDecisionSummaryJson = policyJson; SetState(FinanceConversationRunStepStatuses.AwaitingApproval, utcNow); }
    public void MarkQueued(Guid executionId, string resultJson, string policyJson, DateTime utcNow)
    { ToolExecutionAttemptId = executionId; ResultSummaryJson = resultJson; PolicyDecisionSummaryJson = policyJson; SetState(FinanceConversationRunStepStatuses.Queued, utcNow); }
    public void MarkReconciling(DateTime utcNow) => SetState(FinanceConversationRunStepStatuses.Reconciling, utcNow);
    public void Complete(Guid executionId, string resultJson, string policyJson, DateTime utcNow)
    { ToolExecutionAttemptId = executionId; ResultSummaryJson = resultJson; PolicyDecisionSummaryJson = policyJson; SetState(FinanceConversationRunStepStatuses.Completed, utcNow); CompletedUtc = UpdatedUtc; }
    public void Fail(string code, string summary, DateTime utcNow)
    { FailureCode = FinanceConversationRun.Required(code, 100); SafeFailureSummary = FinanceConversationRun.Required(summary, 2000); SetState(FinanceConversationRunStepStatuses.Failed, utcNow); CompletedUtc = UpdatedUtc; }
    public void Block(string code, string summary, DateTime utcNow)
    { FailureCode = FinanceConversationRun.Required(code, 100); SafeFailureSummary = FinanceConversationRun.Required(summary, 2000); SetState(FinanceConversationRunStepStatuses.Blocked, utcNow); CompletedUtc = UpdatedUtc; }
    public void MarkStale(string code, string summary, DateTime utcNow)
    { FailureCode = FinanceConversationRun.Required(code, 100); SafeFailureSummary = FinanceConversationRun.Required(summary, 2000); SetState(FinanceConversationRunStepStatuses.Stale, utcNow); CompletedUtc = UpdatedUtc; }
    public void Cancel(DateTime utcNow)
    { if (!FinanceConversationRunStepStatuses.Terminal.Contains(Status)) { SetState(FinanceConversationRunStepStatuses.Cancelled, utcNow); CompletedUtc = UpdatedUtc; } }
    public void ScheduleRetry(string code, string summary, DateTime nextUtc, DateTime utcNow)
    { FailureCode = FinanceConversationRun.Required(code, 100); SafeFailureSummary = FinanceConversationRun.Required(summary, 2000); NextAttemptUtc = FinanceConversationRun.Utc(nextUtc); SetState(FinanceConversationRunStepStatuses.Ready, utcNow); }

    public void Redact(DateTime utcNow)
    {
        if (RedactedUtc.HasValue) return;
        NormalizedArgumentsJson = "{}";
        EvidenceReferencesJson = "[]";
        ResultSummaryJson = null;
        PolicyDecisionSummaryJson = null;
        ExpectedEffect = "Retained step metadata was redacted.";
        RedactedUtc = FinanceConversationRun.Utc(utcNow);
        UpdatedUtc = RedactedUtc.Value;
        Version++;
    }

    private void SetState(string status, DateTime utcNow)
    {
        Status = status;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        UpdatedUtc = FinanceConversationRun.Utc(utcNow);
        Version++;
    }
}

public sealed class FinanceConversationRunRevision : ICompanyOwnedEntity
{
    private FinanceConversationRunRevision() { }
    public FinanceConversationRunRevision(Guid id, Guid companyId, Guid runId, int revision, Guid planId,
        string planState, string reasonCode, string planningContextHash, string evidenceReferencesJson, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || runId == Guid.Empty || planId == Guid.Empty || revision <= 0) throw new ArgumentException("Invalid plan revision.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; RunId = runId; Revision = revision; PlanId = planId;
        PlanState = FinanceConversationRun.Required(planState, 32); ReasonCode = FinanceConversationRun.Required(reasonCode, 100);
        PlanningContextHash = FinanceConversationRun.Required(planningContextHash, 64);
        EvidenceReferencesJson = FinanceConversationRun.Required(evidenceReferencesJson, 16000); CreatedUtc = FinanceConversationRun.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public int Revision { get; private set; } public Guid PlanId { get; private set; } public string PlanState { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!; public string PlanningContextHash { get; private set; } = null!;
    public string EvidenceReferencesJson { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public FinanceConversationRun Run { get; private set; } = null!; public Company Company { get; private set; } = null!;
}

public sealed class FinanceConversationRunAttempt : ICompanyOwnedEntity
{
    private FinanceConversationRunAttempt() { }
    public FinanceConversationRunAttempt(Guid id, Guid companyId, Guid runStepId, int attemptNumber,
        string leaseOwner, DateTime leaseExpiresUtc, DateTime startedUtc)
    {
        if (companyId == Guid.Empty || runStepId == Guid.Empty || attemptNumber <= 0) throw new ArgumentException("Invalid step attempt.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; RunStepId = runStepId; AttemptNumber = attemptNumber;
        LeaseOwner = FinanceConversationRun.Required(leaseOwner, 128); LeaseExpiresUtc = FinanceConversationRun.Utc(leaseExpiresUtc);
        Outcome = "executing"; StartedUtc = FinanceConversationRun.Utc(startedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunStepId { get; private set; }
    public int AttemptNumber { get; private set; } public string LeaseOwner { get; private set; } = null!;
    public DateTime LeaseExpiresUtc { get; private set; } public string Outcome { get; private set; } = null!;
    public Guid? ToolExecutionAttemptId { get; private set; } public string? FailureCode { get; private set; }
    public string? SafeSummary { get; private set; } public DateTime StartedUtc { get; private set; } public DateTime? CompletedUtc { get; private set; }
    public FinanceConversationRunStep Step { get; private set; } = null!; public Company Company { get; private set; } = null!;
    public void Complete(string outcome, DateTime utcNow, Guid? executionId = null, string? code = null, string? summary = null)
    { if (CompletedUtc.HasValue) return; Outcome = FinanceConversationRun.Required(outcome, 32); ToolExecutionAttemptId = executionId; FailureCode = FinanceConversationRun.Optional(code, 100); SafeSummary = FinanceConversationRun.Optional(summary, 2000); CompletedUtc = FinanceConversationRun.Utc(utcNow); }
}
