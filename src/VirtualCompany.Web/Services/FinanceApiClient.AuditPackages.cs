namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AuditPackageWorkspaceResponse?> GetAuditPackagesAsync(Guid companyId, Guid? fiscalPeriodId = null,
        CancellationToken cancellationToken = default) => GetAsync<AuditPackageWorkspaceResponse>(companyId,
        $"internal/companies/{companyId:D}/finance/accounting/audit-packages{(fiscalPeriodId.HasValue ? $"?fiscalPeriodId={fiscalPeriodId:D}" : string.Empty)}",
        false, cancellationToken);

    public Task<AuditPackageResponse> RequestAuditPackageAsync(Guid companyId, Guid fiscalPeriodId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages",
            new { fiscalPeriodId, idempotencyKey, scopeKey = "period_close", scopeVersion = "audit-package-v1" }, cancellationToken);
    }

    public Task<AuditPackageResponse> ApproveAuditPackageAsync(Guid companyId, Guid packageId,
        long expectedVersion, string? reason = null, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/approve",
            new { expectedVersion, reason }, cancellationToken);
    }

    public Task<AuditPackageResponse> CancelAuditPackageAsync(Guid companyId, Guid packageId,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/cancel",
            new { expectedVersion }, cancellationToken);
    }

    public Task<AuditPackageVerificationResponse> VerifyAuditPackageAsync(Guid companyId, Guid packageId,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageVerificationResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/verify",
            new { }, cancellationToken);
    }

    public Task<AuditPackageDownloadAuthorizationResponse> AuthorizeAuditPackageDownloadAsync(Guid companyId,
        Guid packageId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageDownloadAuthorizationResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/download-authorizations",
            new { }, cancellationToken);
    }
}
