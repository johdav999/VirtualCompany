namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<TreasuryWorkspaceResponse?> GetTreasuryWorkspaceAsync(
        Guid companyId,
        int horizonDays = 14,
        int exceptionLimit = 12,
        int taskLimit = 8,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<TreasuryWorkspaceResponse?>(null);
        }

        var uri = $"api/companies/{companyId:D}/finance/treasury-workspace" +
                  $"?horizonDays={Math.Clamp(horizonDays, 1, 30)}" +
                  $"&exceptionLimit={Math.Clamp(exceptionLimit, 1, 50)}" +
                  $"&taskLimit={Math.Clamp(taskLimit, 1, 25)}";
        return GetAsync<TreasuryWorkspaceResponse>(companyId, uri, allowNotFound: false, cancellationToken);
    }
}
