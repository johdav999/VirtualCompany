using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Cockpit;

public static class TodayWorkspaceLenses
{
    public const string Company = "company";
    public const string Finance = "finance";
    public const string Sales = "sales";
    public const string Marketing = "marketing";
    public const string Customers = "customers";

    public static IReadOnlyList<string> Ordered { get; } = [Company, Finance, Sales, Marketing, Customers];
    public static IReadOnlySet<string> All { get; } = Ordered.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant();

    public static string FromResponsibility(ResponsibilityArea area) => area switch
    {
        ResponsibilityArea.CompanyPerformance => Company,
        ResponsibilityArea.CashAndAccounting or ResponsibilityArea.Compliance => Finance,
        ResponsibilityArea.Sales => Sales,
        ResponsibilityArea.Marketing => Marketing,
        ResponsibilityArea.CustomerSupport => Customers,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unsupported responsibility area.")
    };

    public static string Label(string lens) => Normalize(lens) switch
    {
        Company => "Company",
        Finance => "Finance",
        Sales => "Sales",
        Marketing => "Marketing",
        Customers => "Customers",
        _ => throw new ArgumentOutOfRangeException(nameof(lens), lens, "Unsupported Today workspace lens.")
    };
}

public sealed record GetTodayWorkspaceQuery(Guid CompanyId, string? Lens = null);

public sealed record TodayWorkspaceDto(
    Guid CompanyId,
    TodayWorkspaceHeaderDto Header,
    string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensDto> AvailableLenses,
    TodayWorkspaceSituationSummaryDto SituationSummary,
    IReadOnlyList<TodayWorkspacePriorityDto> Priorities,
    IReadOnlyList<TodayWorkspaceMetricDto> Metrics,
    TodayWorkspaceFinanceSectionDto? Finance,
    TodayWorkspaceSalesSectionDto? Sales,
    TodayWorkspaceSupportSectionDto? Support,
    TodayWorkspaceMarketingSectionDto? Marketing,
    IReadOnlyList<TodayWorkspaceDecisionDto> Decisions,
    IReadOnlyList<TodayWorkspaceAgentUpdateDto> AgentUpdates,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticDto> Diagnostics,
    TodayWorkspaceResponsibilitySetupDto? ResponsibilitySetup = null,
    TodayWorkspaceManualReviewDto? ManualReview = null);

public sealed record TodayWorkspaceHeaderDto(string CompanyName, string Title, string Subtitle);
public sealed record TodayWorkspaceLensDto(string Value, string Label, bool IsDefault, string AvailabilityReason);
public sealed record TodayWorkspaceSituationSummaryDto(
    string Headline,
    string Summary,
    DateTime AsOfUtc,
    string Freshness,
    bool IsDeterministicFallback);

public sealed record TodayWorkspacePriorityDto(
    string Key,
    int Rank,
    string Lens,
    string WhatHappened,
    string WhyItMatters,
    string ResponsiblePerson,
    string? WorkingAgent,
    string RequiredHumanAction,
    DateTime ObservedAtUtc,
    string Freshness,
    string EvidenceSourceType,
    string? EvidenceSourceId,
    string DeepLink,
    bool DecisionRequired,
    DateTime? DueUtc,
    bool DirectlyOwned,
    decimal Confidence,
    string? VisibilityReason = null);

public sealed record TodayWorkspaceMetricDto(
    string Key,
    string Label,
    decimal? Value,
    string DisplayValue,
    string? Unit,
    string Status,
    DateTime ObservedAtUtc,
    string EvidenceSourceType,
    string DeepLink);

public sealed record TodayWorkspaceFeatureItemDto(
    string Key,
    string Title,
    string Summary,
    string Status,
    DateTime ObservedAtUtc,
    string DeepLink);

public sealed record TodayWorkspaceFinanceSectionDto(
    bool IsAvailable,
    string StatusMessage,
    DateTime ObservedAtUtc,
    decimal? CashBalance,
    string? Currency,
    int? RunwayDays,
    string FinancialHealth,
    int OpenInsightCount,
    IReadOnlyList<TodayWorkspaceFeatureItemDto> Items,
    string DeepLink);

public sealed record TodayWorkspaceSalesSectionDto(
    bool IsAvailable,
    string StatusMessage,
    DateTime ObservedAtUtc,
    decimal PipelineValue,
    string Currency,
    int NewLeads,
    int HotLeads,
    int DealsNeedingAttention,
    decimal ForecastRevenue,
    IReadOnlyList<TodayWorkspaceFeatureItemDto> Items,
    string DeepLink);

public sealed record TodayWorkspaceSupportSectionDto(
    bool IsAvailable,
    string StatusMessage,
    DateTime ObservedAtUtc,
    int OpenCases,
    int AwaitingApproval,
    int EscalatedCases,
    int SlaAtRisk,
    int SlaBreached,
    IReadOnlyList<TodayWorkspaceFeatureItemDto> Items,
    string DeepLink);

public sealed record TodayWorkspaceMarketingSectionDto(
    bool IsAvailable,
    string StatusMessage,
    DateTime ObservedAtUtc,
    int ActiveObjectives,
    int ActivePlans,
    int DueContentItems,
    int ActiveExperiments,
    IReadOnlyList<TodayWorkspaceFeatureItemDto> Items,
    string DeepLink);

public sealed record TodayWorkspaceDecisionDto(
    string Key,
    string Title,
    string Summary,
    DateTime ObservedAtUtc,
    string DeepLink,
    string? VisibilityReason = null,
    Guid? RelatedApprovalId = null);

public sealed record TodayWorkspaceAgentUpdateDto(
    string Key,
    string Title,
    string Summary,
    string? WorkingAgent,
    DateTime ObservedAtUtc,
    string EvidenceSourceType,
    string? DeepLink,
    string? AgentRole = null,
    string? AgentState = null,
    string? AvatarUrl = null,
    string? RationaleSummary = null,
    string? VisibilityReason = null,
    Guid? RelatedTaskId = null,
    Guid? RelatedWorkflowInstanceId = null,
    Guid? RelatedApprovalId = null,
    DateTime? UpdatedUtc = null);

public sealed record TodayWorkspaceDiagnosticDto(string Section, string Code, string Message);
public sealed record TodayWorkspaceResponsibilitySetupDto(
    bool IsConfigured,
    bool CanManage,
    string Message,
    string SettingsDeepLink);

public sealed record TodayWorkspaceManualReviewDto(
    bool CanRequest,
    string? UnavailableReasonCode,
    string? UnavailableReason,
    Guid? RequestId,
    Guid? OperatingCycleId,
    string State,
    string StatusMessage,
    DateTime? UpdatedUtc);

public static class TodayAgentStates
{
    public const string Monitoring = "monitoring";
    public const string Working = "working";
    public const string Recommended = "recommended";
    public const string NeedsUser = "needs_user";
    public const string Blocked = "blocked";
    public const string Completed = "completed";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    { Monitoring, Working, Recommended, NeedsUser, Blocked, Completed };
}

public static class TodayAgentStateMapper
{
    public static string FromTask(WorkTaskStatus status) => status switch
    {
        WorkTaskStatus.New => TodayAgentStates.Monitoring,
        WorkTaskStatus.InProgress => TodayAgentStates.Working,
        WorkTaskStatus.AwaitingApproval => TodayAgentStates.NeedsUser,
        WorkTaskStatus.Completed => TodayAgentStates.Completed,
        WorkTaskStatus.Blocked or WorkTaskStatus.Failed => TodayAgentStates.Blocked,
        _ => TodayAgentStates.Monitoring
    };

    public static string FromAgentRun(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "running" => TodayAgentStates.Working,
        "needs_review" => TodayAgentStates.Recommended,
        "completed" => TodayAgentStates.Completed,
        "blocked" or "failed" or "cancelled" => TodayAgentStates.Blocked,
        _ => TodayAgentStates.Monitoring
    };

    public static string FromApproval(ApprovalRequestStatus status) => status switch
    {
        ApprovalRequestStatus.Pending or ApprovalRequestStatus.ChangesRequested => TodayAgentStates.NeedsUser,
        ApprovalRequestStatus.Approved => TodayAgentStates.Completed,
        _ => TodayAgentStates.Blocked
    };
}

public sealed record TodayWorkspaceLensAccess(
    string Lens,
    string Label,
    string AvailabilityReason,
    bool IsPrimary,
    bool IsExecutiveOversight,
    Guid? ResponsibleMembershipId,
    string ResponsiblePerson,
    string? WorkingAgent,
    Guid? WorkingAgentId = null);

public sealed record TodayWorkspaceLensResolution(
    Guid CompanyId,
    Guid UserId,
    Guid MembershipId,
    CompanyMembershipRole MembershipRole,
    string CompanyName,
    string ActiveLens,
    string DefaultLens,
    string ResponsibilityRevision,
    IReadOnlyList<TodayWorkspaceLensAccess> AvailableLenses,
    bool ResponsibilitiesConfigured = true,
    bool CanManageResponsibilities = false,
    bool CanRequestReview = false);

public sealed record TodayWorkspaceContributorContext(
    Guid CompanyId,
    DateTime NowUtc,
    string ActiveLens,
    TodayWorkspaceLensAccess Access,
    ExecutiveCockpitDashboardDto? ExecutiveCockpit);

public sealed record TodayWorkspacePriorityCandidate(
    string Key,
    string DeduplicationKey,
    string Lens,
    string WhatHappened,
    string WhyItMatters,
    string ResponsiblePerson,
    string? WorkingAgent,
    string RequiredHumanAction,
    DateTime ObservedAtUtc,
    string EvidenceSourceType,
    string? EvidenceSourceId,
    string DeepLink,
    bool DecisionRequired = false,
    DateTime? DueUtc = null,
    int ProximityRank = 0,
    decimal Impact = 0,
    bool DirectlyOwned = false,
    bool Blocked = false,
    int SeverityRank = 0,
    decimal Confidence = 1m,
    string? VisibilityReason = null);

public sealed record TodayWorkspaceFeatureContribution(
    string Lens,
    IReadOnlyList<TodayWorkspacePriorityCandidate> PriorityCandidates,
    IReadOnlyList<TodayWorkspaceMetricDto> Metrics,
    IReadOnlyList<TodayWorkspaceAgentUpdateDto> AgentUpdates,
    TodayWorkspaceFinanceSectionDto? Finance = null,
    TodayWorkspaceSalesSectionDto? Sales = null,
    TodayWorkspaceSupportSectionDto? Support = null,
    TodayWorkspaceMarketingSectionDto? Marketing = null);

public interface ITodayWorkspaceContributor
{
    string Lens { get; }
    Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken);
}

public interface ITodayWorkspaceLensResolver
{
    Task<TodayWorkspaceLensResolution> ResolveAsync(
        Guid companyId,
        string? requestedLens,
        CancellationToken cancellationToken);
}

public interface ITodayWorkspaceQueryService
{
    Task<TodayWorkspaceDto> GetAsync(GetTodayWorkspaceQuery query, CancellationToken cancellationToken);
}

public interface ITodayAgentActivityQueryService
{
    Task<IReadOnlyList<TodayWorkspaceAgentUpdateDto>> GetAsync(
        TodayWorkspaceLensResolution resolution,
        CancellationToken cancellationToken);
}

public interface ICompanyManualReviewService
{
    Task<TodayWorkspaceManualReviewDto> GetStatusAsync(Guid companyId, bool canRequest, CancellationToken cancellationToken);
    Task<TodayWorkspaceManualReviewDto> RequestAsync(Guid companyId, CancellationToken cancellationToken);
}

public static class TodayWorkspacePriorityOrdering
{
    public static IReadOnlyList<TodayWorkspacePriorityCandidate> Select(
        IEnumerable<TodayWorkspacePriorityCandidate> candidates,
        DateTime nowUtc,
        int limit = 5)
    {
        var ordered = candidates
            .Where(IsValid)
            .OrderByDescending(x => x.DecisionRequired)
            .ThenByDescending(x => EffectiveProximity(x, nowUtc))
            .ThenByDescending(x => x.Impact)
            .ThenByDescending(x => x.DirectlyOwned)
            .ThenByDescending(x => x.Blocked)
            .ThenByDescending(x => x.SeverityRank)
            .ThenByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        var selected = new List<TodayWorkspacePriorityCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ordered)
        {
            if (seen.Add(candidate.DeduplicationKey))
            {
                selected.Add(candidate);
                if (selected.Count >= Math.Clamp(limit, 3, 5)) break;
            }
        }

        return selected;
    }

    private static bool IsValid(TodayWorkspacePriorityCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Key) &&
        !string.IsNullOrWhiteSpace(candidate.DeduplicationKey) &&
        !string.IsNullOrWhiteSpace(candidate.WhatHappened) &&
        !string.IsNullOrWhiteSpace(candidate.WhyItMatters) &&
        !string.IsNullOrWhiteSpace(candidate.DeepLink);

    private static int EffectiveProximity(TodayWorkspacePriorityCandidate candidate, DateTime nowUtc)
    {
        if (!candidate.DueUtc.HasValue) return candidate.ProximityRank;
        var remaining = candidate.DueUtc.Value - nowUtc;
        var dueRank = remaining switch
        {
            _ when remaining <= TimeSpan.Zero => 1000,
            _ when remaining <= TimeSpan.FromHours(4) => 900,
            _ when remaining <= TimeSpan.FromHours(24) => 800,
            _ when remaining <= TimeSpan.FromDays(3) => 600,
            _ => 100
        };
        return Math.Max(candidate.ProximityRank, dueRank);
    }
}
