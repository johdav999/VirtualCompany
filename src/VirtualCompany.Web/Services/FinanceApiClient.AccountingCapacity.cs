namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingCapacityApiResponse?> GetAccountingCapacityAsync(Guid companyId,
        string profile = "small", CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<AccountingCapacityApiResponse?>(null)
            : GetAsync<AccountingCapacityApiResponse>(companyId,
                $"api/companies/{companyId:D}/finance/accounting-capacity?profile={Uri.EscapeDataString(profile)}",
                allowNotFound: false, cancellationToken);

    public Task<AccountingRetentionPreviewApiResponse> PreviewAccountingRetentionAsync(Guid companyId,
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingRetentionPreviewApiRequest, AccountingRetentionPreviewApiResponse>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/accounting-capacity/retention/preview",
            new AccountingRetentionPreviewApiRequest(batchSize), cancellationToken);
    }

    public Task<AccountingRetentionCleanupApiResponse> RunAccountingRetentionCleanupAsync(Guid companyId,
        AccountingRetentionCleanupApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingRetentionCleanupApiRequest, AccountingRetentionCleanupApiResponse>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/accounting-capacity/retention/run",
            request, cancellationToken);
    }
}
