using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyRunPolicyVersions
{
    public const string V1 = "finance-autonomy-run-policy-v1";
}

public static class FinanceAutonomyRunReasonCodes
{
    public const string Created = "finance_autonomy_run_created";
    public const string Coalesced = "finance_autonomy_run_coalesced";
    public const string Validated = "finance_autonomy_run_validated";
    public const string PolicyChanged = "finance_autonomy_run_policy_changed";
    public const string EvidenceChanged = "finance_autonomy_run_evidence_changed";
    public const string DependencyPending = "finance_autonomy_step_dependency_pending";
    public const string LeaseUnavailable = "finance_autonomy_step_lease_unavailable";
    public const string LeaseExpired = "finance_autonomy_step_lease_expired";
    public const string StepCompleted = "finance_autonomy_step_completed";
    public const string AwaitingApproval = "finance_autonomy_step_awaiting_approval";
    public const string ReconciliationRequired = "finance_autonomy_step_reconciliation_required";
    public const string LeaseRecovered = "finance_autonomy_step_lease_recovered";
    public const string ApprovalRequired = "finance_autonomy_step_approval_required";
    public const string ApprovalApproved = "finance_autonomy_step_approval_approved";
    public const string ApprovalRejected = "finance_autonomy_step_approval_rejected";
    public const string ApprovalChangesRequested = "finance_autonomy_step_approval_changes_requested";
    public const string ApprovalCancelled = "finance_autonomy_step_approval_cancelled";
    public const string ApprovalExpired = "finance_autonomy_step_approval_expired";
    public const string ApprovalRevoked = "finance_autonomy_step_approval_revoked";
    public const string ApprovalSuperseded = "finance_autonomy_step_approval_superseded";
    public const string ApprovalStale = "finance_autonomy_step_approval_stale";
    public const string PermanentFailure = "finance_autonomy_step_permanent_failure";
    public const string TransientFailure = "finance_autonomy_step_transient_failure";
    public const string AmbiguousOutcome = "finance_autonomy_step_ambiguous_outcome";
    public const string ArtifactMissing = "finance_autonomy_step_artifact_missing";
    public const string ArtifactCorrupt = "finance_autonomy_step_artifact_corrupt";
    public const string ReconciledApplied = "finance_autonomy_step_reconciled_applied";
    public const string ReconciledNoEffect = "finance_autonomy_step_reconciled_no_effect";
    public const string ReconciledNotApplied = "finance_autonomy_step_reconciled_not_applied";
    public const string Cancelled = "finance_autonomy_run_cancelled";
    public const string Superseded = "finance_autonomy_run_superseded";
    public const string Replayed = "finance_autonomy_run_replayed";
    public const string Redacted = "finance_autonomy_run_sensitive_content_redacted";
}

public sealed record FinanceAutonomyRunPlanStepDefinition(
    string StepKey,
    string ActionClass,
    string ToolName,
    IReadOnlyList<string> DependencyStepKeys,
    string RequestedEffectHash,
    string? RequestedEffectSummary,
    int MaximumAttempts = 3,
    bool ReplayPermitted = false,
    Guid? WorkTaskId = null,
    string? Scope = null,
    IReadOnlyDictionary<string, JsonNode?>? RequestPayload = null,
    string? ThresholdCategory = null,
    string? ThresholdKey = null,
    decimal? ThresholdValue = null,
    bool SensitiveAction = false,
    Guid? DelegationAuthorityId = null,
    string? BusinessIdempotencyKey = null);

public sealed record FinanceAutonomyRunSourceDefinition(
    string SourceType,
    string EntityType,
    string EntityId,
    string SourceVersion,
    string ContentHash,
    string? SafeLabel = null);

public sealed record CreateOrCoalesceFinanceAutonomyRunCommand(
    Guid AgentId,
    string CapabilityId,
    string Trigger,
    string TriggerKey,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    string? AuthoritativeEventId,
    string? AuthoritativeEventVersion,
    string IdempotencyKey,
    string CorrelationId,
    DateTime EvidenceObservedUtc,
    IReadOnlyDictionary<string, string?> EvidenceSnapshot,
    string PlanVersion,
    IReadOnlyList<FinanceAutonomyRunPlanStepDefinition> Steps,
    IReadOnlyDictionary<string, decimal> BudgetSnapshot,
    IReadOnlyList<FinanceAutonomyRunSourceDefinition> Sources,
    int RecordCount = 0,
    decimal? Amount = null,
    Guid? OriginatingGoalId = null,
    Guid? OriginatingTaskId = null,
    Guid? WorkflowInstanceId = null,
    Guid? OrchestrationRunId = null);

public sealed record FinanceAutonomyRunFilter(
    Guid? AgentId = null,
    Guid? GrantId = null,
    string? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Skip = 0,
    int Take = 50);

public sealed record ClaimFinanceAutonomyStepCommand(
    Guid RunId,
    Guid StepId,
    string WorkerId,
    string LeaseToken,
    int LeaseSeconds,
    string CurrentEvidenceHash,
    FinanceAutonomyUsageDefinition? PlannedUsage = null);

public sealed record HeartbeatFinanceAutonomyStepCommand(
    Guid RunId,
    Guid StepId,
    string LeaseToken,
    int LeaseSeconds);

public sealed record CompleteFinanceAutonomyStepCommand(
    Guid RunId,
    Guid StepId,
    string LeaseToken,
    Guid? ToolExecutionAttemptId,
    string ActualEffectHash,
    string ActualEffectStatus,
    string? ActualEffectSummary,
    FinanceAutonomyUsageDefinition? ActualUsage = null);

public sealed record ReleaseFinanceAutonomyStepCommand(
    Guid RunId,
    Guid StepId,
    string LeaseToken,
    string NextStatus,
    string ReasonCode,
    string? SafeSummary,
    Guid? ToolExecutionAttemptId = null,
    FinanceAutonomyUsageDefinition? ActualUsage = null,
    string? ReconciliationReference = null);

public sealed record AwaitFinanceAutonomyStepApprovalCommand(
    Guid RunId,
    Guid StepId,
    string LeaseToken,
    Guid ApprovalRequestId,
    Guid ToolExecutionAttemptId,
    string? SafeSummary,
    FinanceAutonomyUsageDefinition? ActualUsage = null);

public sealed record ResolveFinanceAutonomyApprovalCommand(
    Guid ApprovalRequestId,
    string ApprovalStatus,
    string ToolExecutionStatus,
    IReadOnlyDictionary<string, JsonNode?>? ToolResult,
    string? DenialReason,
    string? DecisionSummary);

public static class FinanceAutonomyReconciliationOutcomes
{
    public const string ConfirmedApplied = "confirmed_applied";
    public const string ConfirmedNoEffect = "confirmed_no_effect";
    public const string ConfirmedNotApplied = "confirmed_not_applied";
    public const string PermanentFailure = "permanent_failure";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ConfirmedApplied, ConfirmedNoEffect, ConfirmedNotApplied, PermanentFailure
    };
}

public sealed record ReconcileFinanceAutonomyStepCommand(
    string Outcome,
    string ActualEffectHash,
    string? ActualEffectSummary,
    string? ProviderReference,
    long ExpectedStepVersion);

public sealed record BindFinanceAutonomyStepApprovalCommand(Guid ApprovalRequestId);
public sealed record TransitionFinanceAutonomyRunCommand(string Status, string ReasonCode, string? SafeSummary, long ExpectedVersion);
public sealed record CancelFinanceAutonomyRunCommand(string Reason, long ExpectedVersion);
public sealed record SupersedeFinanceAutonomyRunCommand(string Reason, long ExpectedVersion);
public sealed record RedactFinanceAutonomyRunCommand(string Reason, long ExpectedVersion);
public sealed record ReplayFinanceAutonomyRunCommand(Guid CheckpointStepId, string IdempotencyKey, string CorrelationId, string Reason);
public sealed record NarrowFinanceAutonomyRunCommand(
    IReadOnlyList<string> RetainedStepKeys,
    string IdempotencyKey,
    string CorrelationId,
    string Reason,
    long ExpectedVersion);

public sealed record FinanceAutonomyRunSourceDto(
    Guid Id, string SourceType, string EntityType, string EntityId, string SourceVersion,
    string ContentHash, string? SafeLabel, DateTime CreatedUtc);

public sealed record FinanceAutonomyStepAttemptDto(
    Guid Id, int AttemptNumber, string LeaseOwner, string PolicyVersion, string AuthorityVersion,
    string AuthorityHash, string EvidenceHash, string Outcome, string? ReasonCode, string? SafeSummary,
    Guid? ToolExecutionAttemptId, DateTime StartedUtc, DateTime? CompletedUtc);

public sealed record FinanceAutonomyRunStepDto(
    Guid Id, int Sequence, string StepKey, string ActionClass, string ToolName,
    IReadOnlyList<string> DependencyStepKeys, string Status, int AttemptCount, int MaximumAttempts,
    string ToolPolicyVersion, string AuthorityVersion, string AuthorityHash, string EvidenceHash,
    string RequestedEffectHash, string? RequestedEffectSummary, string? ActualEffectHash,
    string? ActualEffectStatus, string? ActualEffectSummary, string BusinessIdempotencyKey,
    string? ReconciliationReference, Guid? ApprovalRequestId,
    Guid? WorkTaskId, Guid? ToolExecutionAttemptId, string? LeaseOwner, DateTime? LeaseExpiresUtc,
    DateTime? LastHeartbeatUtc, bool ReplayPermitted, Guid? ReplayOfStepId,
    string? ReasonCode, string? SafeSummary, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? StartedUtc, DateTime? CompletedUtc, long Version,
    IReadOnlyList<FinanceAutonomyStepAttemptDto> Attempts);

public sealed record FinanceAutonomyRunHistoryDto(
    Guid Id, string? FromStatus, string ToStatus, string ReasonCode, string? SafeSummary,
    string ActorType, Guid? ActorId, string CorrelationId, DateTime OccurredUtc);

public sealed record FinanceAutonomyRunDto(
    Guid Id, Guid CompanyId, Guid AgentId, string CapabilityId, Guid GrantId, Guid GrantVersionId,
    int GrantVersionNumber, string Trigger, string TriggerKey, DateTime WindowStartUtc, DateTime WindowEndUtc,
    string? AuthoritativeEventId, string? AuthoritativeEventVersion, string LogicalKey,
    string IdempotencyKey, string CorrelationId, string EvidenceHash, DateTime EvidenceObservedUtc,
    string PlanHash, string PlanVersion, string BudgetHash, string PolicyVersion, string CatalogueVersion,
    string AuthorityVersion, string AuthorityHash, Guid? OriginatingGoalId, Guid? OriginatingTaskId,
    Guid? WorkflowInstanceId, Guid? OrchestrationRunId, Guid? ReplayOfRunId, Guid? ReplayCheckpointStepId,
    string Status, string? ReasonCode, string? SafeSummary, bool HasCompletedEffects,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? StartedUtc, DateTime? TerminalUtc,
    DateTime? SensitiveContentRedactedUtc, long Version, IReadOnlyList<FinanceAutonomyRunStepDto> Steps,
    IReadOnlyList<FinanceAutonomyRunHistoryDto> History, IReadOnlyList<FinanceAutonomyRunSourceDto> Sources,
    Guid? RevisionOfRunId = null, int RevisionNumber = 1);

public sealed record FinanceAutonomyRunListItemDto(
    Guid Id, Guid AgentId, string CapabilityId, Guid GrantId, int GrantVersionNumber,
    string Trigger, string TriggerKey, string Status, string? ReasonCode, bool HasCompletedEffects,
    int CompletedSteps, int TotalSteps, DateTime CreatedUtc, DateTime UpdatedUtc, long Version);

public sealed record FinanceAutonomyRunListResult(
    IReadOnlyList<FinanceAutonomyRunListItemDto> Items, int TotalCount, int Skip, int Take);

public sealed record FinanceAutonomyStepLeaseDto(
    Guid RunId, Guid StepId, string LeaseToken, DateTime LeaseExpiresUtc, int AttemptNumber,
    string ToolName, string ActionClass, Guid GrantVersionId, string PolicyVersion,
    string AuthorityVersion, string AuthorityHash, string EvidenceHash, string RequestedEffectHash);

public interface IFinanceAutonomyRunService
{
    Task<FinanceAutonomyRunDto> CreateOrCoalesceAsync(Guid companyId, CreateOrCoalesceFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunListResult> ListAsync(Guid companyId, FinanceAutonomyRunFilter filter, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> GetAsync(Guid companyId, Guid runId, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> TransitionAsync(Guid companyId, Guid runId, TransitionFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> BindApprovalAsync(Guid companyId, Guid runId, Guid stepId, BindFinanceAutonomyStepApprovalCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> CancelAsync(Guid companyId, Guid runId, CancelFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> SupersedeAsync(Guid companyId, Guid runId, SupersedeFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> RedactAsync(Guid companyId, Guid runId, RedactFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> ReplayAsync(Guid companyId, Guid runId, ReplayFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> NarrowAsync(Guid companyId, Guid runId, NarrowFinanceAutonomyRunCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyStepLeaseDto?> ClaimStepAsync(Guid companyId, ClaimFinanceAutonomyStepCommand command, CancellationToken cancellationToken);
    Task<bool> HeartbeatStepAsync(Guid companyId, HeartbeatFinanceAutonomyStepCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> CompleteStepAsync(Guid companyId, CompleteFinanceAutonomyStepCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> ReleaseStepAsync(Guid companyId, ReleaseFinanceAutonomyStepCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> AwaitApprovalStepAsync(Guid companyId, AwaitFinanceAutonomyStepApprovalCommand command, CancellationToken cancellationToken);
    Task<bool> ResolveApprovalAsync(Guid companyId, ResolveFinanceAutonomyApprovalCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyRunDto> ReconcileStepAsync(Guid companyId, Guid runId, Guid stepId, ReconcileFinanceAutonomyStepCommand command, CancellationToken cancellationToken);
}

public sealed record FinanceAutonomyApprovalCoordinatorBatchResult(
    int Considered, int Pending, int Continued, int Blocked, int Escalated);

public interface IFinanceAutonomyApprovalCoordinator
{
    Task<FinanceAutonomyApprovalCoordinatorBatchResult> ProcessBatchAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken);
    Task ProcessApprovalAsync(Guid companyId, Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed class FinanceAutonomyRunValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Finance autonomy run validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class FinanceAutonomyRunConcurrencyException(string message) : Exception(message);
