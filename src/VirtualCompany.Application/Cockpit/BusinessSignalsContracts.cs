using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Signals;

namespace VirtualCompany.Application.Cockpit;

public interface ISignalEngine
{
    Task<IReadOnlyList<BusinessSignal>> GenerateSignals(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

public sealed record FinancialHealthSummaryDto(
    string Status,
    int? Score,
    string Trend,
    string Summary,
    int ActiveInsightCount,
    int CriticalInsightCount,
    int HighInsightCount,
    decimal CurrentCashBalance,
    decimal ExpectedIncomingCash,
    decimal ExpectedOutgoingCash,
    decimal OverdueReceivables,
    decimal UpcomingPayables,
    string Currency);

public sealed record FinanceActionDto(
    string Title,
    string Description,
    string Priority,
    string? TargetEntityType,
    string? TargetEntityId,
    string ActionLabel,
    string? NavigationTarget);

public sealed record GroupedFinanceInsightDto(
    string GroupKey,
    string Title,
    string Summary,
    string Recommendation,
    string Severity,
    string Category,
    DateTime LatestOccurredUtc,
    int OccurrenceCount,
    FinanceInsightEntityReferenceDto? PrimaryEntity,
    IReadOnlyList<FinanceInsightEntityReferenceDto> RelatedEntities);

public sealed record DashboardFinanceSnapshotDto(
    Guid CompanyId,
    decimal CurrentCashBalance,
    decimal ExpectedIncomingCash,
    decimal ExpectedOutgoingCash,
    decimal OverdueReceivables,
    decimal UpcomingPayables,
    string Currency,
    DateTime AsOfUtc,
    int UpcomingWindowDays,
    decimal Cash,
    decimal BurnRate,
    int? RunwayDays,
    string RiskLevel,
    bool HasFinanceData,
    FinancialHealthSummaryDto FinancialHealth,
    IReadOnlyList<FinanceActionDto> TopFinanceActions,
    IReadOnlyList<GroupedFinanceInsightDto> InsightFeed);

public sealed record FinanceDashboardMetricDto(
    string MetricKey,
    decimal Amount,
    string Currency,
    DateTime AsOfUtc,
    int UpcomingWindowDays);

public interface IDashboardFinanceSnapshotService
{
    Task<DashboardFinanceSnapshotDto> GetAsync(
        Guid companyId,
        DateTime? asOfUtc = null,
        int upcomingWindowDays = 30,
        CancellationToken cancellationToken = default);
}