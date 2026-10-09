

namespace VirtualCompany.Application.Finance;


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


public sealed record FinanceAutonomyGrantDto(
    Guid Id, Guid CompanyId, Guid AgentId, string CapabilityId, Guid? ActiveVersionId,
    int LatestVersionNumber, int Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<FinanceAutonomyGrantVersionDto> Versions);


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
