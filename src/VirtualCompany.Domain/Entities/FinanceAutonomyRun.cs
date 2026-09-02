using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAutonomyRun : ICompanyOwnedEntity
{
    private FinanceAutonomyRun() { }

    public FinanceAutonomyRun(
        Guid id, Guid companyId, Guid agentId, string capabilityId, Guid grantId, Guid grantVersionId,
        int grantVersionNumber, string trigger, string triggerKey, DateTime windowStartUtc, DateTime windowEndUtc,
        string? authoritativeEventId, string? authoritativeEventVersion, string logicalKey, string idempotencyKey,
        string correlationId, string evidenceSnapshotJson, string evidenceHash, DateTime evidenceObservedUtc,
        string planJson, string planHash, string planVersion, string budgetSnapshotJson, string budgetHash,
        string policyVersion, string catalogueVersion, string authorityVersion, string authorityHash,
        Guid? originatingGoalId, Guid? originatingTaskId, Guid? workflowInstanceId, Guid? orchestrationRunId,
        Guid? replayOfRunId, Guid? replayCheckpointStepId, DateTime createdUtc,
        Guid? revisionOfRunId = null, int revisionNumber = 1)
    {
        Id = FinanceAutonomyRunValues.Id(id);
        CompanyId = FinanceAutonomyRunValues.Required(companyId, nameof(companyId));
        AgentId = FinanceAutonomyRunValues.Required(agentId, nameof(agentId));
        CapabilityId = FinanceAutonomyRunValues.Text(capabilityId, nameof(capabilityId), 160).ToLowerInvariant();
        GrantId = FinanceAutonomyRunValues.Required(grantId, nameof(grantId));
        GrantVersionId = FinanceAutonomyRunValues.Required(grantVersionId, nameof(grantVersionId));
        GrantVersionNumber = grantVersionNumber > 0 ? grantVersionNumber : throw new ArgumentOutOfRangeException(nameof(grantVersionNumber));
        Trigger = FinanceAutonomyRunValues.Text(trigger, nameof(trigger), 64).ToLowerInvariant();
        TriggerKey = FinanceAutonomyRunValues.Text(triggerKey, nameof(triggerKey), 200);
        WindowStartUtc = FinanceAutonomyRunValues.Utc(windowStartUtc, nameof(windowStartUtc));
        WindowEndUtc = FinanceAutonomyRunValues.Utc(windowEndUtc, nameof(windowEndUtc));
        if (WindowEndUtc <= WindowStartUtc) throw new ArgumentException("The trigger window must end after it starts.");
        AuthoritativeEventId = FinanceAutonomyRunValues.Optional(authoritativeEventId, 240);
        AuthoritativeEventVersion = FinanceAutonomyRunValues.Optional(authoritativeEventVersion, 100);
        LogicalKey = FinanceAutonomyRunValues.Text(logicalKey, nameof(logicalKey), 64);
        IdempotencyKey = FinanceAutonomyRunValues.Text(idempotencyKey, nameof(idempotencyKey), 200);
        CorrelationId = FinanceAutonomyRunValues.Text(correlationId, nameof(correlationId), 128);
        EvidenceSnapshotJson = FinanceAutonomyRunValues.Json(evidenceSnapshotJson, nameof(evidenceSnapshotJson));
        EvidenceHash = FinanceAutonomyRunValues.Hash(evidenceHash, nameof(evidenceHash));
        EvidenceObservedUtc = FinanceAutonomyRunValues.Utc(evidenceObservedUtc, nameof(evidenceObservedUtc));
        PlanJson = FinanceAutonomyRunValues.Json(planJson, nameof(planJson));
        PlanHash = FinanceAutonomyRunValues.Hash(planHash, nameof(planHash));
        PlanVersion = FinanceAutonomyRunValues.Text(planVersion, nameof(planVersion), 100);
        BudgetSnapshotJson = FinanceAutonomyRunValues.Json(budgetSnapshotJson, nameof(budgetSnapshotJson));
        BudgetHash = FinanceAutonomyRunValues.Hash(budgetHash, nameof(budgetHash));
        PolicyVersion = FinanceAutonomyRunValues.Text(policyVersion, nameof(policyVersion), 100);
        CatalogueVersion = FinanceAutonomyRunValues.Text(catalogueVersion, nameof(catalogueVersion), 100);
        AuthorityVersion = FinanceAutonomyRunValues.Text(authorityVersion, nameof(authorityVersion), 100);
        AuthorityHash = FinanceAutonomyRunValues.Hash(authorityHash, nameof(authorityHash));
        OriginatingGoalId = FinanceAutonomyRunValues.OptionalId(originatingGoalId, nameof(originatingGoalId));
        OriginatingTaskId = FinanceAutonomyRunValues.OptionalId(originatingTaskId, nameof(originatingTaskId));
        WorkflowInstanceId = FinanceAutonomyRunValues.OptionalId(workflowInstanceId, nameof(workflowInstanceId));
        OrchestrationRunId = FinanceAutonomyRunValues.OptionalId(orchestrationRunId, nameof(orchestrationRunId));
        ReplayOfRunId = FinanceAutonomyRunValues.OptionalId(replayOfRunId, nameof(replayOfRunId));
        ReplayCheckpointStepId = FinanceAutonomyRunValues.OptionalId(replayCheckpointStepId, nameof(replayCheckpointStepId));
        RevisionOfRunId = FinanceAutonomyRunValues.OptionalId(revisionOfRunId, nameof(revisionOfRunId));
        RevisionNumber = revisionNumber > 0 ? revisionNumber : throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        Status = FinanceAutonomyRunStatus.Planned;
        CreatedUtc = UpdatedUtc = FinanceAutonomyRunValues.Utc(createdUtc, nameof(createdUtc));
        Version = 1;
        RowVersion = FinanceAutonomyRunValues.ConcurrencyToken();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string CapabilityId { get; private set; } = null!;
    public Guid GrantId { get; private set; }
    public Guid GrantVersionId { get; private set; }
    public int GrantVersionNumber { get; private set; }
    public string Trigger { get; private set; } = null!;
    public string TriggerKey { get; private set; } = null!;
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public string? AuthoritativeEventId { get; private set; }
    public string? AuthoritativeEventVersion { get; private set; }
    public string LogicalKey { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string EvidenceSnapshotJson { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public DateTime EvidenceObservedUtc { get; private set; }
    public string PlanJson { get; private set; } = null!;
    public string PlanHash { get; private set; } = null!;
    public string PlanVersion { get; private set; } = null!;
    public string BudgetSnapshotJson { get; private set; } = null!;
    public string BudgetHash { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string CatalogueVersion { get; private set; } = null!;
    public string AuthorityVersion { get; private set; } = null!;
    public string AuthorityHash { get; private set; } = null!;
    public Guid? OriginatingGoalId { get; private set; }
    public Guid? OriginatingTaskId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public Guid? OrchestrationRunId { get; private set; }
    public Guid? ReplayOfRunId { get; private set; }
    public Guid? ReplayCheckpointStepId { get; private set; }
    public Guid? RevisionOfRunId { get; private set; }
    public int RevisionNumber { get; private set; }
    public FinanceAutonomyRunStatus Status { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public bool HasCompletedEffects { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? TerminalUtc { get; private set; }
    public DateTime? SensitiveContentRedactedUtc { get; private set; }
    public Guid? SensitiveContentRedactedByUserId { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public FinanceAutonomyGrant Grant { get; private set; } = null!;
    public FinanceAutonomyGrantVersion GrantVersion { get; private set; } = null!;
    public ICollection<FinanceAutonomyRunStep> Steps { get; } = new List<FinanceAutonomyRunStep>();
    public ICollection<FinanceAutonomyRunHistory> History { get; } = new List<FinanceAutonomyRunHistory>();
    public ICollection<FinanceAutonomyRunSourceReference> Sources { get; } = new List<FinanceAutonomyRunSourceReference>();

    public void Transition(FinanceAutonomyRunStatus next, string reasonCode, string? safeSummary, DateTime utcNow)
    {
        if (Status == next) return;
        if (!AllowedNext(Status).Contains(next))
            throw new InvalidOperationException($"Cannot move Finance autonomy run from {Status.ToStorageValue()} to {next.ToStorageValue()}.");
        Status = next;
        ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000);
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        StartedUtc ??= next is FinanceAutonomyRunStatus.Running or FinanceAutonomyRunStatus.Reconciling ? now : null;
        TerminalUtc = IsTerminal(next) ? now : null;
        Touch(now);
    }

    public void MarkCompletedEffect(DateTime utcNow)
    {
        if (HasCompletedEffects) return;
        HasCompletedEffects = true;
        Touch(utcNow);
    }

    public void RedactSensitiveContent(Guid actorUserId, DateTime utcNow)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorUserId));
        if (SensitiveContentRedactedUtc.HasValue) return;
        EvidenceSnapshotJson = "{}";
        PlanJson = "{}";
        BudgetSnapshotJson = "{}";
        SafeSummary = SafeSummary is null ? null : "Sensitive summary redacted by retention policy.";
        SensitiveContentRedactedByUserId = actorUserId;
        SensitiveContentRedactedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        Touch(SensitiveContentRedactedUtc.Value);
    }

    public static bool IsTerminal(FinanceAutonomyRunStatus status) => status is
        FinanceAutonomyRunStatus.Completed or FinanceAutonomyRunStatus.PartiallyCompleted or
        FinanceAutonomyRunStatus.Cancelled or FinanceAutonomyRunStatus.Failed or
        FinanceAutonomyRunStatus.DeadLettered or FinanceAutonomyRunStatus.Superseded;

    private static IReadOnlySet<FinanceAutonomyRunStatus> AllowedNext(FinanceAutonomyRunStatus state) => state switch
    {
        FinanceAutonomyRunStatus.Planned => Set(FinanceAutonomyRunStatus.Validating, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Validating => Set(FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.Running, FinanceAutonomyRunStatus.AwaitingApproval, FinanceAutonomyRunStatus.Blocked, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Failed, FinanceAutonomyRunStatus.DeadLettered, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Queued => Set(FinanceAutonomyRunStatus.Validating, FinanceAutonomyRunStatus.Running, FinanceAutonomyRunStatus.AwaitingApproval, FinanceAutonomyRunStatus.Blocked, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.DeadLettered, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Running => Set(FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.AwaitingApproval, FinanceAutonomyRunStatus.Reconciling, FinanceAutonomyRunStatus.Blocked, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Completed, FinanceAutonomyRunStatus.PartiallyCompleted, FinanceAutonomyRunStatus.Failed, FinanceAutonomyRunStatus.DeadLettered, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.AwaitingApproval => Set(FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.Blocked, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Failed, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Reconciling => Set(FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.Completed, FinanceAutonomyRunStatus.PartiallyCompleted, FinanceAutonomyRunStatus.Blocked, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Failed, FinanceAutonomyRunStatus.DeadLettered, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Blocked => Set(FinanceAutonomyRunStatus.Validating, FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.Paused, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Failed, FinanceAutonomyRunStatus.DeadLettered, FinanceAutonomyRunStatus.Superseded),
        FinanceAutonomyRunStatus.Paused => Set(FinanceAutonomyRunStatus.Validating, FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunStatus.Superseded),
        _ => new HashSet<FinanceAutonomyRunStatus>()
    };

    private static IReadOnlySet<FinanceAutonomyRunStatus> Set(params FinanceAutonomyRunStatus[] values) => values.ToHashSet();
    private void Touch(DateTime utcNow) { UpdatedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)); Version++; RowVersion = FinanceAutonomyRunValues.ConcurrencyToken(); }
}

public sealed class FinanceAutonomyRunStep : ICompanyOwnedEntity
{
    private FinanceAutonomyRunStep() { }

    public FinanceAutonomyRunStep(Guid id, Guid companyId, Guid runId, int sequence, string stepKey,
        string actionClass, string toolName, IEnumerable<string> dependencyStepKeys, int maximumAttempts,
        string toolPolicyVersion, string authorityVersion, string authorityHash, string evidenceHash,
        string requestedEffectHash, string? requestedEffectSummary, bool replayPermitted,
        Guid? workTaskId, DateTime createdUtc, Guid? replayOfStepId = null,
        string? businessIdempotencyKey = null)
    {
        Id = FinanceAutonomyRunValues.Id(id);
        CompanyId = FinanceAutonomyRunValues.Required(companyId, nameof(companyId));
        RunId = FinanceAutonomyRunValues.Required(runId, nameof(runId));
        Sequence = sequence > 0 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence));
        StepKey = FinanceAutonomyRunValues.Text(stepKey, nameof(stepKey), 160).ToLowerInvariant();
        ActionClass = FinanceAutonomyRunValues.Text(actionClass, nameof(actionClass), 64).ToLowerInvariant();
        ToolName = FinanceAutonomyRunValues.Text(toolName, nameof(toolName), 160).ToLowerInvariant();
        DependencyStepKeys = dependencyStepKeys.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        MaximumAttempts = maximumAttempts is > 0 and <= 20 ? maximumAttempts : throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        ToolPolicyVersion = FinanceAutonomyRunValues.Text(toolPolicyVersion, nameof(toolPolicyVersion), 100);
        AuthorityVersion = FinanceAutonomyRunValues.Text(authorityVersion, nameof(authorityVersion), 100);
        AuthorityHash = FinanceAutonomyRunValues.Hash(authorityHash, nameof(authorityHash));
        EvidenceHash = FinanceAutonomyRunValues.Hash(evidenceHash, nameof(evidenceHash));
        RequestedEffectHash = FinanceAutonomyRunValues.Hash(requestedEffectHash, nameof(requestedEffectHash));
        RequestedEffectSummary = FinanceAutonomyRunValues.Optional(requestedEffectSummary, 1000);
        BusinessIdempotencyKey = string.IsNullOrWhiteSpace(businessIdempotencyKey)
            ? $"finance-autonomy:{companyId:N}:{runId:N}:{id:N}"
            : FinanceAutonomyRunValues.Text(businessIdempotencyKey, nameof(businessIdempotencyKey), 200);
        ReplayPermitted = replayPermitted;
        WorkTaskId = FinanceAutonomyRunValues.OptionalId(workTaskId, nameof(workTaskId));
        ReplayOfStepId = FinanceAutonomyRunValues.OptionalId(replayOfStepId, nameof(replayOfStepId));
        Status = FinanceAutonomyStepStatus.Planned;
        CreatedUtc = UpdatedUtc = FinanceAutonomyRunValues.Utc(createdUtc, nameof(createdUtc));
        Version = 1;
        RowVersion = FinanceAutonomyRunValues.ConcurrencyToken();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public int Sequence { get; private set; }
    public string StepKey { get; private set; } = null!;
    public string ActionClass { get; private set; } = null!;
    public string ToolName { get; private set; } = null!;
    public List<string> DependencyStepKeys { get; private set; } = [];
    public FinanceAutonomyStepStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaximumAttempts { get; private set; }
    public string ToolPolicyVersion { get; private set; } = null!;
    public string AuthorityVersion { get; private set; } = null!;
    public string AuthorityHash { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public string RequestedEffectHash { get; private set; } = null!;
    public string? RequestedEffectSummary { get; private set; }
    public string? ActualEffectHash { get; private set; }
    public string? ActualEffectStatus { get; private set; }
    public string? ActualEffectSummary { get; private set; }
    public string BusinessIdempotencyKey { get; private set; } = null!;
    public string? ReconciliationReference { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? WorkTaskId { get; private set; }
    public Guid? ToolExecutionAttemptId { get; private set; }
    public string? LeaseOwner { get; private set; }
    public string? LeaseToken { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime? LastHeartbeatUtc { get; private set; }
    public bool ReplayPermitted { get; private set; }
    public Guid? ReplayOfStepId { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public FinanceAutonomyRun Run { get; private set; } = null!;
    public ICollection<FinanceAutonomyStepAttempt> Attempts { get; } = new List<FinanceAutonomyStepAttempt>();

    public void Queue(DateTime utcNow)
    {
        if (Status is not FinanceAutonomyStepStatus.Planned and not FinanceAutonomyStepStatus.Validating and not FinanceAutonomyStepStatus.Blocked and not FinanceAutonomyStepStatus.Paused)
            throw new InvalidOperationException("Only a pending Finance autonomy step can be queued.");
        Status = FinanceAutonomyStepStatus.Queued; ReasonCode = null; SafeSummary = null; Touch(utcNow);
    }

    public bool TryClaim(string leaseOwner, string leaseToken, DateTime utcNow, TimeSpan leaseDuration)
    {
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(30)) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (Status == FinanceAutonomyStepStatus.Running && LeaseExpiresUtc > now) return false;
        if (Status is not FinanceAutonomyStepStatus.Queued and not FinanceAutonomyStepStatus.Running) return false;
        if (AttemptCount >= MaximumAttempts) return false;
        if (Status == FinanceAutonomyStepStatus.Running && !string.IsNullOrWhiteSpace(ActualEffectStatus)) return false;
        LeaseOwner = FinanceAutonomyRunValues.Text(leaseOwner, nameof(leaseOwner), 160);
        LeaseToken = FinanceAutonomyRunValues.Text(leaseToken, nameof(leaseToken), 160);
        LeaseExpiresUtc = now.Add(leaseDuration);
        LastHeartbeatUtc = now;
        AttemptCount++;
        Status = FinanceAutonomyStepStatus.Running;
        StartedUtc ??= now;
        Touch(now);
        return true;
    }

    public void Heartbeat(string leaseToken, DateTime utcNow, TimeSpan extension)
    {
        EnsureLease(leaseToken, utcNow);
        if (extension <= TimeSpan.Zero || extension > TimeSpan.FromMinutes(30)) throw new ArgumentOutOfRangeException(nameof(extension));
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        LastHeartbeatUtc = now; LeaseExpiresUtc = now.Add(extension); Touch(now);
    }

    public void BindApproval(Guid approvalRequestId, Guid toolExecutionAttemptId, DateTime utcNow)
    {
        ApprovalRequestId = FinanceAutonomyRunValues.Required(approvalRequestId, nameof(approvalRequestId));
        ToolExecutionAttemptId = FinanceAutonomyRunValues.Required(toolExecutionAttemptId, nameof(toolExecutionAttemptId));
        Status = FinanceAutonomyStepStatus.AwaitingApproval; ClearLease(); Touch(utcNow);
    }

    public void AwaitApproval(string leaseToken, Guid approvalRequestId, Guid toolExecutionAttemptId,
        string? safeSummary, DateTime utcNow)
    {
        EnsureLease(leaseToken, utcNow);
        ApprovalRequestId = FinanceAutonomyRunValues.Required(approvalRequestId, nameof(approvalRequestId));
        ToolExecutionAttemptId = FinanceAutonomyRunValues.Required(toolExecutionAttemptId, nameof(toolExecutionAttemptId));
        Status = FinanceAutonomyStepStatus.AwaitingApproval;
        ReasonCode = "finance_autonomy_step_approval_required";
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000);
        ClearLease();
        Touch(utcNow);
    }

    public void ResolveApproval(string outcome, string reasonCode, string? actualEffectHash,
        string? actualEffectStatus, string? safeSummary, DateTime utcNow)
    {
        if (Status != FinanceAutonomyStepStatus.AwaitingApproval)
            throw new InvalidOperationException("Only a Finance autonomy step awaiting approval can be resolved.");
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000);
        switch (outcome)
        {
            case "executed":
                Status = FinanceAutonomyStepStatus.Completed;
                ActualEffectHash = FinanceAutonomyRunValues.Hash(actualEffectHash!, nameof(actualEffectHash));
                ActualEffectStatus = FinanceAutonomyRunValues.Text(actualEffectStatus!, nameof(actualEffectStatus), 40).ToLowerInvariant();
                ActualEffectSummary = SafeSummary;
                CompletedUtc = now;
                break;
            case "reconciliation_required":
                Status = FinanceAutonomyStepStatus.Reconciling;
                break;
            case "cancelled":
                Status = FinanceAutonomyStepStatus.Cancelled;
                CompletedUtc = now;
                break;
            case "superseded":
                Status = FinanceAutonomyStepStatus.Superseded;
                CompletedUtc = now;
                break;
            default:
                Status = FinanceAutonomyStepStatus.Blocked;
                break;
        }
        ClearLease();
        Touch(now);
    }

    public void RecoverExpiredLease(FinanceAutonomyStepStatus next, string reasonCode,
        string safeSummary, DateTime utcNow)
    {
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        if (Status != FinanceAutonomyStepStatus.Running || LeaseExpiresUtc > now)
            throw new InvalidOperationException("Only an expired Finance autonomy lease can be recovered.");
        if (next is not (FinanceAutonomyStepStatus.Queued or FinanceAutonomyStepStatus.Reconciling))
            throw new ArgumentException("Expired leases can only be queued or reconciled.", nameof(next));
        Status = next;
        ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Text(safeSummary, nameof(safeSummary), 1000);
        ClearLease();
        Touch(now);
    }

    public void ResolveReconciliation(string outcome, string actualEffectHash, string? actualEffectSummary,
        string? providerReference, DateTime utcNow)
    {
        if (Status != FinanceAutonomyStepStatus.Reconciling)
            throw new InvalidOperationException("Only a reconciling Finance autonomy step can be resolved.");
        var now = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
        ReconciliationReference = FinanceAutonomyRunValues.Optional(providerReference, 240);
        ActualEffectHash = FinanceAutonomyRunValues.Hash(actualEffectHash, nameof(actualEffectHash));
        ActualEffectSummary = FinanceAutonomyRunValues.Optional(actualEffectSummary, 1000);
        switch (outcome)
        {
            case "confirmed_applied":
                Status = FinanceAutonomyStepStatus.Completed;
                ActualEffectStatus = "reconciled_effect";
                ReasonCode = "finance_autonomy_step_reconciled_applied";
                CompletedUtc = now;
                break;
            case "confirmed_no_effect":
                Status = FinanceAutonomyStepStatus.Completed;
                ActualEffectStatus = "no_effect";
                ReasonCode = "finance_autonomy_step_reconciled_no_effect";
                CompletedUtc = now;
                break;
            case "confirmed_not_applied":
                Status = AttemptCount < MaximumAttempts ? FinanceAutonomyStepStatus.Queued : FinanceAutonomyStepStatus.DeadLettered;
                ActualEffectStatus = "confirmed_not_applied";
                ReasonCode = "finance_autonomy_step_reconciled_not_applied";
                CompletedUtc = Status == FinanceAutonomyStepStatus.DeadLettered ? now : null;
                break;
            case "permanent_failure":
                Status = FinanceAutonomyStepStatus.Failed;
                ActualEffectStatus = "permanent_failure";
                ReasonCode = "finance_autonomy_step_permanent_failure";
                CompletedUtc = now;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        SafeSummary = ActualEffectSummary;
        ClearLease();
        Touch(now);
    }

    public void Complete(string leaseToken, Guid? toolExecutionAttemptId, string actualEffectHash,
        string actualEffectStatus, string? actualEffectSummary, DateTime utcNow)
    {
        EnsureLease(leaseToken, utcNow);
        ToolExecutionAttemptId = FinanceAutonomyRunValues.OptionalId(toolExecutionAttemptId, nameof(toolExecutionAttemptId));
        ActualEffectHash = FinanceAutonomyRunValues.Hash(actualEffectHash, nameof(actualEffectHash));
        ActualEffectStatus = FinanceAutonomyRunValues.Text(actualEffectStatus, nameof(actualEffectStatus), 40).ToLowerInvariant();
        ActualEffectSummary = FinanceAutonomyRunValues.Optional(actualEffectSummary, 1000);
        Status = FinanceAutonomyStepStatus.Completed; ReasonCode = null; SafeSummary = null;
        CompletedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)); ClearLease(); Touch(CompletedUtc.Value);
    }

    public void Release(string leaseToken, FinanceAutonomyStepStatus next, string reasonCode, string? safeSummary,
        DateTime utcNow, string? reconciliationReference = null)
    {
        EnsureLease(leaseToken, utcNow, allowExpired: true);
        if (next is not (FinanceAutonomyStepStatus.Queued or FinanceAutonomyStepStatus.Reconciling or FinanceAutonomyStepStatus.Blocked or FinanceAutonomyStepStatus.Paused or FinanceAutonomyStepStatus.Failed or FinanceAutonomyStepStatus.DeadLettered))
            throw new ArgumentException("Invalid post-attempt state.", nameof(next));
        Status = next; ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000); ClearLease();
        ReconciliationReference = next == FinanceAutonomyStepStatus.Reconciling
            ? FinanceAutonomyRunValues.Optional(reconciliationReference, 240)
            : null;
        CompletedUtc = next is FinanceAutonomyStepStatus.Failed or FinanceAutonomyStepStatus.DeadLettered ? FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)) : null;
        Touch(utcNow);
    }

    public void Block(string reasonCode, string? safeSummary, DateTime utcNow)
    {
        if (Status == FinanceAutonomyStepStatus.Completed) throw new InvalidOperationException("A completed effect cannot be blocked retroactively.");
        Status = FinanceAutonomyStepStatus.Blocked; ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000); ClearLease(); Touch(utcNow);
    }

    public void CancelOrSupersede(bool supersede, DateTime utcNow)
    {
        if (Status == FinanceAutonomyStepStatus.Completed) return;
        Status = supersede ? FinanceAutonomyStepStatus.Superseded : FinanceAutonomyStepStatus.Cancelled;
        ClearLease(); CompletedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)); Touch(CompletedUtc.Value);
    }

    public void RedactSensitiveContent(DateTime utcNow)
    {
        RequestedEffectSummary = RequestedEffectSummary is null ? null : "Redacted";
        ActualEffectSummary = ActualEffectSummary is null ? null : "Redacted";
        ReconciliationReference = ReconciliationReference is null ? null : "Redacted";
        SafeSummary = SafeSummary is null ? null : "Redacted";
        Touch(utcNow);
    }

    private void EnsureLease(string leaseToken, DateTime utcNow, bool allowExpired = false)
    {
        if (Status != FinanceAutonomyStepStatus.Running || !string.Equals(LeaseToken, leaseToken, StringComparison.Ordinal))
            throw new InvalidOperationException("The Finance autonomy step lease is not owned by this worker.");
        if (!allowExpired && LeaseExpiresUtc <= FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)))
            throw new InvalidOperationException("The Finance autonomy step lease expired.");
    }

    private void ClearLease() { LeaseOwner = null; LeaseToken = null; LeaseExpiresUtc = null; LastHeartbeatUtc = null; }
    private void Touch(DateTime utcNow) { UpdatedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow)); Version++; RowVersion = FinanceAutonomyRunValues.ConcurrencyToken(); }
}

public sealed class FinanceAutonomyStepAttempt : ICompanyOwnedEntity
{
    private FinanceAutonomyStepAttempt() { }
    public FinanceAutonomyStepAttempt(Guid id, Guid companyId, Guid runId, Guid stepId, int attemptNumber,
        string leaseOwner, string leaseTokenHash, string policyVersion, string authorityVersion,
        string authorityHash, string evidenceHash, DateTime startedUtc)
    {
        Id = FinanceAutonomyRunValues.Id(id); CompanyId = FinanceAutonomyRunValues.Required(companyId, nameof(companyId));
        RunId = FinanceAutonomyRunValues.Required(runId, nameof(runId)); StepId = FinanceAutonomyRunValues.Required(stepId, nameof(stepId));
        AttemptNumber = attemptNumber > 0 ? attemptNumber : throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        LeaseOwner = FinanceAutonomyRunValues.Text(leaseOwner, nameof(leaseOwner), 160);
        LeaseTokenHash = FinanceAutonomyRunValues.Hash(leaseTokenHash, nameof(leaseTokenHash));
        PolicyVersion = FinanceAutonomyRunValues.Text(policyVersion, nameof(policyVersion), 100);
        AuthorityVersion = FinanceAutonomyRunValues.Text(authorityVersion, nameof(authorityVersion), 100);
        AuthorityHash = FinanceAutonomyRunValues.Hash(authorityHash, nameof(authorityHash));
        EvidenceHash = FinanceAutonomyRunValues.Hash(evidenceHash, nameof(evidenceHash));
        Outcome = "started"; StartedUtc = FinanceAutonomyRunValues.Utc(startedUtc, nameof(startedUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid StepId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string LeaseOwner { get; private set; } = null!;
    public string LeaseTokenHash { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string AuthorityVersion { get; private set; } = null!;
    public string AuthorityHash { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? ReasonCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public Guid? ToolExecutionAttemptId { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public FinanceAutonomyRunStep Step { get; private set; } = null!;
    public void Complete(string outcome, string? reasonCode, string? safeSummary, Guid? toolExecutionAttemptId, DateTime utcNow)
    {
        if (CompletedUtc.HasValue) return;
        Outcome = FinanceAutonomyRunValues.Text(outcome, nameof(outcome), 40).ToLowerInvariant();
        ReasonCode = FinanceAutonomyRunValues.Optional(reasonCode, 100); SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000);
        ToolExecutionAttemptId = FinanceAutonomyRunValues.OptionalId(toolExecutionAttemptId, nameof(toolExecutionAttemptId));
        CompletedUtc = FinanceAutonomyRunValues.Utc(utcNow, nameof(utcNow));
    }
}

public sealed class FinanceAutonomyRunHistory : ICompanyOwnedEntity
{
    private FinanceAutonomyRunHistory() { }
    public FinanceAutonomyRunHistory(Guid id, Guid companyId, Guid runId, string? fromStatus, string toStatus,
        string reasonCode, string? safeSummary, string actorType, Guid? actorId, string correlationId, DateTime occurredUtc)
    {
        Id = FinanceAutonomyRunValues.Id(id); CompanyId = FinanceAutonomyRunValues.Required(companyId, nameof(companyId));
        RunId = FinanceAutonomyRunValues.Required(runId, nameof(runId)); FromStatus = FinanceAutonomyRunValues.Optional(fromStatus, 40);
        ToStatus = FinanceAutonomyRunValues.Text(toStatus, nameof(toStatus), 40); ReasonCode = FinanceAutonomyRunValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = FinanceAutonomyRunValues.Optional(safeSummary, 1000); ActorType = FinanceAutonomyRunValues.Text(actorType, nameof(actorType), 32);
        ActorId = FinanceAutonomyRunValues.OptionalId(actorId, nameof(actorId)); CorrelationId = FinanceAutonomyRunValues.Text(correlationId, nameof(correlationId), 128);
        OccurredUtc = FinanceAutonomyRunValues.Utc(occurredUtc, nameof(occurredUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!;
    public string? SafeSummary { get; private set; }
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public FinanceAutonomyRun Run { get; private set; } = null!;
}

public sealed class FinanceAutonomyRunSourceReference : ICompanyOwnedEntity
{
    private FinanceAutonomyRunSourceReference() { }
    public FinanceAutonomyRunSourceReference(Guid id, Guid companyId, Guid runId, string sourceType,
        string entityType, string entityId, string sourceVersion, string contentHash, string? safeLabel, DateTime createdUtc)
    {
        Id = FinanceAutonomyRunValues.Id(id); CompanyId = FinanceAutonomyRunValues.Required(companyId, nameof(companyId));
        RunId = FinanceAutonomyRunValues.Required(runId, nameof(runId)); SourceType = FinanceAutonomyRunValues.Text(sourceType, nameof(sourceType), 64);
        EntityType = FinanceAutonomyRunValues.Text(entityType, nameof(entityType), 100); EntityId = FinanceAutonomyRunValues.Text(entityId, nameof(entityId), 240);
        SourceVersion = FinanceAutonomyRunValues.Text(sourceVersion, nameof(sourceVersion), 100); ContentHash = FinanceAutonomyRunValues.Hash(contentHash, nameof(contentHash));
        SafeLabel = FinanceAutonomyRunValues.Optional(safeLabel, 300); CreatedUtc = FinanceAutonomyRunValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public string? SafeLabel { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public FinanceAutonomyRun Run { get; private set; } = null!;
    public void RedactLabel() => SafeLabel = null;
}

internal static class FinanceAutonomyRunValues
{
    public static Guid Id(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static Guid? OptionalId(Guid? value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;
    public static string Text(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name, $"{name} must be {max} characters or fewer.") : value.Trim();
    public static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null
        : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(nameof(value), $"Value must be {max} characters or fewer.") : value.Trim();
    public static string Hash(string? value, string name)
    {
        var hash = Text(value, name, 64).ToLowerInvariant();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash : throw new ArgumentException($"{name} must be a SHA-256 hash.", name);
    }
    public static string Json(string? value, string name) => Text(value, name, 1_000_000);
    public static byte[] ConcurrencyToken() => Guid.NewGuid().ToByteArray();
    public static DateTime Utc(DateTime value, string name) => value == default ? throw new ArgumentException($"{name} is required.", name)
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
