namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<NativeReceivablesReadinessResponse?> GetNativeReceivablesReadinessAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<NativeReceivablesReadinessResponse>(companyId,
            $"api/companies/{companyId:D}/finance/receivables/readiness",
            allowNotFound: false,
            cancellationToken);
}
