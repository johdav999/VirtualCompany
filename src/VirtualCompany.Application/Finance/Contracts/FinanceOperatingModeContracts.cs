namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceOperatingModeQuery(Guid CompanyId, DateOnly? AsOfDate = null);

public interface IFinanceOperatingModeService
{
    Task<FinanceOperatingModeDecisionDto> GetAsync(
        GetFinanceOperatingModeQuery query,
        CancellationToken cancellationToken);
}
