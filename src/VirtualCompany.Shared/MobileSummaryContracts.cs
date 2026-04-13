namespace VirtualCompany.Shared.Mobile;

public sealed record MobileHomeSummaryResponse(
    MobileCompanyStatusSummaryDto CompanyStatus,
    IReadOnlyList<MobileTaskFollowUpSummaryDto> TaskFollowUps,
    bool HasTaskFollowUps);

public sealed record MobileCompanyStatusSummaryDto(
    Guid CompanyId,
    string CompanyName,
    DateTime GeneratedAtUtc,
    DateTime? LastActivityUtc,
    string Headline,
    string? Subtitle,
    int PendingApprovalCount,
    int ActiveAlertCount,
    int OpenTaskCount,
    int BlockedTaskCount,
    int OverdueTaskCount,
    IReadOnlyList<MobileCompanyStatusMetricDto> Metrics);

public sealed record MobileCompanyStatusMetricDto(
    string Key,
    string Label,
    int Value,
    string StatusHint);

public sealed record MobileTaskFollowUpSummaryDto(
    Guid TaskId,
    string Title,
    string Status,
    string Priority,
    string? AssignedAgentDisplayName,
    string? Summary,
    DateTime UpdatedAtUtc,
    DateTime? DueAtUtc,
    bool IsOverdue);
