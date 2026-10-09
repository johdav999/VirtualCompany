namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CompanyStatutoryProfileStatusResponse?> GetCompanyStatutoryProfileAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<CompanyStatutoryProfileStatusResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/statutory-profile",
            allowNotFound: false,
            cancellationToken);

    public Task<CompanyStatutoryProfileStatusResponse> CreateCompanyStatutoryProfileAsync(
        Guid companyId,
        SaveCompanyStatutoryProfileApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCompanyStatutoryProfileApiRequest, CompanyStatutoryProfileStatusResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/statutory-profile", request, cancellationToken);
    }

    public Task<CompanyStatutoryProfileStatusResponse> UpdateCompanyStatutoryProfileAsync(
        Guid companyId,
        SaveCompanyStatutoryProfileApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCompanyStatutoryProfileApiRequest, CompanyStatutoryProfileStatusResponse>(
            companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/statutory-profile", request, cancellationToken);
    }

    public Task<AccountingSetupStatusResponse?> GetAccountingSetupStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingSetupStatusResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/setup-status",
            allowNotFound: false,
            cancellationToken);

    public Task<AccountingSetupStatusResponse> CreateAccountingConfigurationAsync(
        Guid companyId,
        CreateAccountingConfigurationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateAccountingConfigurationApiRequest, AccountingSetupStatusResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/configuration",
            request,
            cancellationToken);
    }

    public Task<AccountingPolicyPackImpactPreviewResponse> PreviewAccountingPolicyPackAsync(
        Guid companyId,
        PreviewAccountingPolicyPackApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PreviewAccountingPolicyPackApiRequest, AccountingPolicyPackImpactPreviewResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/policy-pack/preview",
            request,
            cancellationToken);
    }

    public Task<AccountingSetupStatusResponse> ApplyAccountingPolicyPackAsync(
        Guid companyId,
        ApplyAccountingPolicyPackApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ApplyAccountingPolicyPackApiRequest, AccountingSetupStatusResponse>(
            companyId,
            HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/policy-pack",
            request,
            cancellationToken);
    }

    public Task<AccountingSetupStatusResponse?> ValidateAccountingConfigurationAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingSetupStatusResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/validation",
            allowNotFound: false,
            cancellationToken);

    public Task<AccountingCapabilityDecisionResponse?> GetAccountingCapabilityAsync(
        Guid companyId,
        string capabilityKey,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingCapabilityDecisionResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/capabilities/{Uri.EscapeDataString(capabilityKey)}",
            allowNotFound: false,
            cancellationToken);
}
