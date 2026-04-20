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
    decimal Cash,
    decimal BurnRate,
    int? RunwayDays,
    string RiskLevel,
    bool HasFinanceData,
    string Currency,
    DateTime AsOfUtc);

public interface IDashboardFinanceSnapshotService
{
    Task<DashboardFinanceSnapshotDto> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}