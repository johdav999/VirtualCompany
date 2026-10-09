namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingOperationsResponse?> GetAccountingOperationsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingOperationsResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/operations",
            allowNotFound: false, cancellationToken);

    public Task<AccountingMigrationRunResponse> StartAccountingMigrationAsync(
        Guid companyId,
        StartAccountingMigrationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingMigrationApiRequest, AccountingMigrationRunResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/operations/migrations",
            request, cancellationToken);
    }

    public Task<AccountingMigrationRunResponse> ResolveAccountingMigrationConflictAsync(
        Guid companyId,
        Guid conflictId,
        ResolveAccountingMigrationConflictApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ResolveAccountingMigrationConflictApiRequest, AccountingMigrationRunResponse>(
            companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/operations/migration-conflicts/{conflictId}/resolve",
            request, cancellationToken);
    }

    public Task<AccountingRecoveryVerificationResponse> VerifyAccountingRecoveryAsync(
        Guid companyId,
        VerifyAccountingRecoveryApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<VerifyAccountingRecoveryApiRequest, AccountingRecoveryVerificationResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/operations/recovery-verification",
            request, cancellationToken);
    }
}
