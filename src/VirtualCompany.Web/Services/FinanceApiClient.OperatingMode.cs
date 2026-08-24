namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FinanceOperatingModeResponse?> GetOperatingModeAsync(
        Guid companyId,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceOperatingModeResponse?>(null)
            : GetAsync<FinanceOperatingModeResponse>(
                companyId,
                $"api/companies/{companyId}/finance/operating-mode{BuildQuery(("asOfDate", asOfDate?.ToString("O")))}",
                allowNotFound: false,
                cancellationToken);
}
