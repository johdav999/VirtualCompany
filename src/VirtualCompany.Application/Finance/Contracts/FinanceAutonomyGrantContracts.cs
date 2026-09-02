namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyPolicyVersions
{
    public const string V1 = "finance-autonomy-policy-v1";
}

public static class FinanceAutonomyLevels
{
    public const string ReadMonitor = "read_monitor";
    public const string RecommendDraft = "recommend_draft";
    public const string SupervisedInternalExecute = "supervised_internal_execute";
    public const string ScheduledBoundedExecute = "scheduled_bounded_execute";
}

public static class FinanceAutonomyTriggers
{
    public const string ManualReview = "manual_review";
    public const string Schedule = "schedule";
    public const string BusinessEvent = "business_event";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ManualReview, Schedule, BusinessEvent
    };
}

public static class FinanceAutonomyConfirmationBehaviors
{
    public const string NoConfirmation = "no_confirmation";
    public const string ExplicitConfirmation = "explicit_confirmation";
    public const string ApprovalRequired = "approval_required";
    public const string PolicyDriven = "policy_driven";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        NoConfirmation, ExplicitConfirmation, ApprovalRequired, PolicyDriven
    };
}

public static class FinanceAutonomyDecisionReasonCodes
{
    public const string Allowed = "finance_autonomy_allowed";
    public const string GrantMissing = "finance_autonomy_grant_missing";
    public const string GrantInactive = "finance_autonomy_grant_inactive";
    public const string GrantExpired = "finance_autonomy_grant_expired";
    public const string GrantNotYetEffective = "finance_autonomy_grant_not_yet_effective";
    public const string CompanyPaused = "finance_autonomy_company_paused";
    public const string EmergencyStopped = "finance_autonomy_emergency_stopped";
    public const string Paused = "finance_autonomy_paused";
    public const string TriggerDenied = "finance_autonomy_trigger_denied";
    public const string ActionDenied = "finance_autonomy_action_denied";
    public const string ToolDenied = "finance_autonomy_tool_denied";
    public const string LimitExceeded = "finance_autonomy_limit_exceeded";
    public const string EvidenceStale = "finance_autonomy_evidence_stale";
    public const string PolicyStale = "finance_autonomy_policy_stale";
    public const string AuthorityStale = "finance_autonomy_authority_stale";
    public const string HumanOnly = "finance_autonomy_human_only";
}

public sealed record FinanceAutonomyGrantDefinition(
    Guid AgentId,
    string CapabilityId,
    string Level,
    IReadOnlyList<string> AllowedTriggers,
    IReadOnlyList<string> AllowedActionClasses,
    IReadOnlyList<string> AllowedTools,
    int MaximumRecordsPerRun,
    decimal? MaximumAmountPerRun,
    int MaximumActionsPerRun,
    string? ScheduleExpression,
    string Timezone,
    string WindowStartLocal,
    string WindowEndLocal,
    int EvidenceFreshnessMinutes,
    string ConfirmationBehavior,
    string EscalationRoute,
    DateTime? EffectiveFromUtc,
    DateTime? ExpiresUtc,
    IReadOnlyList<string>? AllowedEventTypes = null,
    int MinimumIntervalMinutes = 60,
    int MaximumRunsPerWindow = 1,
    int DebounceMinutes = 5,
    string CatchUpBehavior = "latest",
    int MaximumCatchUpWindows = 1,
    int LateEventToleranceMinutes = 1440);

public sealed record CreateFinanceAutonomyGrantCommand(FinanceAutonomyGrantDefinition Definition, string? Rationale = null);
public sealed record CreateFinanceAutonomyGrantVersionCommand(FinanceAutonomyGrantDefinition Definition, int ExpectedGrantVersion, string? Rationale = null);
public sealed record ActivateFinanceAutonomyGrantVersionCommand(int ExpectedGrantVersion, string? ReviewReason = null);
public sealed record RevokeFinanceAutonomyGrantCommand(int ExpectedGrantVersion, string Reason);

public sealed record SetFinanceAutonomyControlCommand(
    string Scope,
    Guid? AgentId,
    string? CapabilityId,
    string State,
    string Reason,
    int ExpectedVersion = 0);

public sealed record FinanceAutonomyGrantVersionDto(
    Guid Id, int VersionNumber, string Level, string Status,
    IReadOnlyList<string> AllowedTriggers, IReadOnlyList<string> AllowedActionClasses, IReadOnlyList<string> AllowedTools,
    int MaximumRecordsPerRun, decimal? MaximumAmountPerRun, int MaximumActionsPerRun,
    string? ScheduleExpression, string Timezone, string WindowStartLocal, string WindowEndLocal,
    int EvidenceFreshnessMinutes, string ConfirmationBehavior, string EscalationRoute,
    DateTime EffectiveFromUtc, DateTime? ExpiresUtc, string CatalogueVersion, string CapabilityPolicyHash,
    string AuthorityVersion, string AuthorityHash, Guid CreatedByUserId, DateTime CreatedUtc,
    Guid? ReviewedByUserId, string? ReviewReason, DateTime? ReviewedUtc, DateTime? ActivatedUtc,
    Guid? RevokedByUserId, string? RevocationReason, DateTime? RevokedUtc,
    IReadOnlyList<string> AllowedEventTypes, int MinimumIntervalMinutes, int MaximumRunsPerWindow,
    int DebounceMinutes, string CatchUpBehavior, int MaximumCatchUpWindows, int LateEventToleranceMinutes);

public sealed record FinanceAutonomyGrantDto(
    Guid Id, Guid CompanyId, Guid AgentId, string CapabilityId, Guid? ActiveVersionId,
    int LatestVersionNumber, int Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<FinanceAutonomyGrantVersionDto> Versions);

public sealed record FinanceAutonomyControlDto(
    Guid Id, Guid CompanyId, string Scope, string ScopeKey, Guid? AgentId, string? CapabilityId,
    string State, string? Reason, Guid? ChangedByUserId, DateTime UpdatedUtc, int Version);

public sealed record FinanceAutonomyPolicySnapshotDto(
    Guid CompanyId, Guid AgentId, string CapabilityId, string CompatibilityLevel,
    FinanceAutonomyGrantDto? Grant, IReadOnlyList<FinanceAutonomyControlDto> Controls,
    FinanceAutonomyDecisionDto EffectiveDecision, DateTime GeneratedUtc);

public sealed record FinanceAutonomyEvaluationRequest(
    Guid CompanyId, Guid AgentId, string CapabilityId, string Trigger, string ActionClass, string ToolName,
    int RecordCount = 0, decimal? Amount = null, DateTime? EvidenceObservedUtc = null, int ActionCount = 1);

public sealed record FinanceAutonomyDecisionDto(
    bool IsAllowed, string ReasonCode, string Explanation, Guid? GrantId, Guid? GrantVersionId,
    int? GrantVersionNumber, string? Level, bool RequiresConfirmation, bool RequiresApproval,
    int RemainingRecordCapacity, int RemainingActionCapacity, decimal? RemainingAmountCapacity,
    string PolicyVersion, string? CatalogueVersion, string? AuthorityVersion, string? AuthorityHash,
    DateTime EvaluatedUtc);

public interface IFinanceAutonomyGrantService
{
    Task<IReadOnlyList<FinanceAutonomyGrantDto>> ListAsync(Guid companyId, Guid? agentId, CancellationToken cancellationToken);
    Task<FinanceAutonomyGrantDto> GetAsync(Guid companyId, Guid grantId, CancellationToken cancellationToken);
    Task<FinanceAutonomyGrantDto> CreateAsync(Guid companyId, CreateFinanceAutonomyGrantCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyGrantDto> CreateVersionAsync(Guid companyId, Guid grantId, CreateFinanceAutonomyGrantVersionCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyGrantDto> ActivateAsync(Guid companyId, Guid grantId, Guid versionId, ActivateFinanceAutonomyGrantVersionCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyGrantDto> RevokeAsync(Guid companyId, Guid grantId, RevokeFinanceAutonomyGrantCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyControlDto> SetControlAsync(Guid companyId, SetFinanceAutonomyControlCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyPolicySnapshotDto> GetEffectivePolicyAsync(Guid companyId, Guid agentId, string capabilityId, CancellationToken cancellationToken);
}

public interface IFinanceAutonomyPolicyEvaluator
{
    Task<FinanceAutonomyDecisionDto> EvaluateAsync(FinanceAutonomyEvaluationRequest request, CancellationToken cancellationToken);
}

public sealed class FinanceAutonomyValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("Finance autonomy policy validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class FinanceAutonomyConcurrencyException(string message) : Exception(message);
