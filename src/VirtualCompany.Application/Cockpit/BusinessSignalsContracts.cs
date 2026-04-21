using VirtualCompany.Domain.Signals;

namespace VirtualCompany.Application.Cockpit;

public interface ISignalEngine
{
    Task<IReadOnlyList<BusinessSignal>> GenerateSignals(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

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
    bool HasFinanceData);

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