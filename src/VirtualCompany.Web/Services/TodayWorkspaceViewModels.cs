namespace VirtualCompany.Web.Services;

public static class TodayWorkspaceLensValues
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "company", "finance", "sales", "marketing", "customers"
    };

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant();
}

public sealed record TodayWorkspaceViewModel(
    Guid CompanyId,
    TodayWorkspaceHeaderViewModel Header,
    string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensViewModel> AvailableLenses,
    TodayWorkspaceSituationSummaryViewModel SituationSummary,
    IReadOnlyList<TodayWorkspacePriorityViewModel> Priorities,
    IReadOnlyList<TodayWorkspaceMetricViewModel> Metrics,
    TodayWorkspaceFinanceSectionViewModel? Finance,
    TodayWorkspaceSalesSectionViewModel? Sales,
    TodayWorkspaceSupportSectionViewModel? Support,
    TodayWorkspaceMarketingSectionViewModel? Marketing,
    IReadOnlyList<TodayWorkspaceDecisionViewModel> Decisions,
    IReadOnlyList<TodayWorkspaceAgentUpdateViewModel> AgentUpdates,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticViewModel> Diagnostics,
    TodayWorkspaceResponsibilitySetupViewModel? ResponsibilitySetup = null,
    TodayWorkspaceManualReviewViewModel? ManualReview = null);

public sealed record TodayWorkspaceHeaderViewModel(string CompanyName, string Title, string Subtitle);
public sealed record TodayWorkspaceLensViewModel(string Value, string Label, bool IsDefault, string AvailabilityReason);
public sealed record TodayWorkspaceSituationSummaryViewModel(
    string Headline, string Summary, DateTime AsOfUtc, string Freshness, bool IsDeterministicFallback);
public sealed record TodayWorkspacePriorityViewModel(
    string Key, int Rank, string Lens, string WhatHappened, string WhyItMatters, string ResponsiblePerson,
    string? WorkingAgent, string RequiredHumanAction, DateTime ObservedAtUtc, string Freshness,
    string EvidenceSourceType, string? EvidenceSourceId, string DeepLink, bool DecisionRequired,
    DateTime? DueUtc, bool DirectlyOwned, decimal Confidence, string? VisibilityReason = null);
public sealed record TodayWorkspaceMetricViewModel(
    string Key, string Label, decimal? Value, string DisplayValue, string? Unit, string Status,
    DateTime ObservedAtUtc, string EvidenceSourceType, string DeepLink);
public sealed record TodayWorkspaceFeatureItemViewModel(
    string Key, string Title, string Summary, string Status, DateTime ObservedAtUtc, string DeepLink);
public sealed record TodayWorkspaceFinanceSectionViewModel(
    bool IsAvailable, string StatusMessage, DateTime ObservedAtUtc, decimal? CashBalance, string? Currency,
    int? RunwayDays, string FinancialHealth, int OpenInsightCount,
    IReadOnlyList<TodayWorkspaceFeatureItemViewModel> Items, string DeepLink);
public sealed record TodayWorkspaceSalesSectionViewModel(
    bool IsAvailable, string StatusMessage, DateTime ObservedAtUtc, decimal PipelineValue, string Currency,
    int NewLeads, int HotLeads, int DealsNeedingAttention, decimal ForecastRevenue,
    IReadOnlyList<TodayWorkspaceFeatureItemViewModel> Items, string DeepLink);
public sealed record TodayWorkspaceSupportSectionViewModel(
    bool IsAvailable, string StatusMessage, DateTime ObservedAtUtc, int OpenCases, int AwaitingApproval,
    int EscalatedCases, int SlaAtRisk, int SlaBreached,
    IReadOnlyList<TodayWorkspaceFeatureItemViewModel> Items, string DeepLink);
public sealed record TodayWorkspaceMarketingSectionViewModel(
    bool IsAvailable, string StatusMessage, DateTime ObservedAtUtc, int ActiveObjectives, int ActivePlans,
    int DueContentItems, int ActiveExperiments, IReadOnlyList<TodayWorkspaceFeatureItemViewModel> Items,
    string DeepLink);
public sealed record TodayWorkspaceDecisionViewModel(
    string Key, string Title, string Summary, DateTime ObservedAtUtc, string DeepLink,
    string? VisibilityReason = null, Guid? RelatedApprovalId = null);
public sealed record TodayWorkspaceAgentUpdateViewModel(
    string Key, string Title, string Summary, string? WorkingAgent, DateTime ObservedAtUtc,
    string EvidenceSourceType, string? DeepLink, string? AgentRole = null, string? AgentState = null,
    string? AvatarUrl = null, string? RationaleSummary = null, string? VisibilityReason = null,
    Guid? RelatedTaskId = null, Guid? RelatedWorkflowInstanceId = null, Guid? RelatedApprovalId = null,
    DateTime? UpdatedUtc = null);
public sealed record TodayWorkspaceDiagnosticViewModel(string Section, string Code, string Message);
public sealed record TodayWorkspaceResponsibilitySetupViewModel(
    bool IsConfigured, bool CanManage, string Message, string SettingsDeepLink);
public sealed record TodayWorkspaceManualReviewViewModel(
    bool CanRequest, string? UnavailableReasonCode, string? UnavailableReason,
    Guid? RequestId, Guid? OperatingCycleId, string State, string StatusMessage, DateTime? UpdatedUtc);
