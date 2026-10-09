namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingDimensionWorkspaceResponse?> GetAccountingDimensionWorkspaceAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<AccountingDimensionWorkspaceResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/dimensions/workspace", false, cancellationToken);

    public Task<AccountingDimensionTypeResponse> SaveAccountingDimensionTypeAsync(Guid companyId,
        SaveAccountingDimensionTypeApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionTypeApiRequest, AccountingDimensionTypeResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/types", request, cancellationToken); }

    public Task<AccountingDimensionMemberResponse> SaveAccountingDimensionMemberAsync(Guid companyId,
        SaveAccountingDimensionMemberApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionMemberApiRequest, AccountingDimensionMemberResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/members", request, cancellationToken); }

    public Task<AccountingDimensionAccountPolicyResponse> SaveAccountingDimensionAccountPolicyAsync(Guid companyId,
        SaveAccountingDimensionAccountPolicyApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionAccountPolicyApiRequest, AccountingDimensionAccountPolicyResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/account-policies", request, cancellationToken); }

    public Task<AccountingDimensionExternalMappingResponse> SaveAccountingDimensionExternalMappingAsync(Guid companyId,
        SaveAccountingDimensionExternalMappingApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionExternalMappingApiRequest, AccountingDimensionExternalMappingResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/external-mappings", request, cancellationToken); }

    public Task<AccountingDimensionCombinationRuleResponse> SaveAccountingDimensionCombinationRuleAsync(Guid companyId,
        SaveAccountingDimensionCombinationRuleApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionCombinationRuleApiRequest, AccountingDimensionCombinationRuleResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/combination-rules", request, cancellationToken); }

    public Task<AccountingAllocationTemplateResponse> SaveAccountingAllocationTemplateAsync(Guid companyId,
        SaveAccountingAllocationTemplateApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingAllocationTemplateApiRequest, AccountingAllocationTemplateResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocation-templates", request, cancellationToken); }

    public Task<AccountingAllocationPreviewResponse> PreviewAccountingAllocationAsync(Guid companyId,
        PreviewAccountingAllocationApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PreviewAccountingAllocationApiRequest, AccountingAllocationPreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocations/preview", request, cancellationToken);

    public Task<AccountingAllocationApplicationResponse> ApplyAccountingAllocationAsync(Guid companyId,
        ApplyAccountingAllocationApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<ApplyAccountingAllocationApiRequest, AccountingAllocationApplicationResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocations", request, cancellationToken); }

    public Task<AccountingDimensionReportResponse?> GetAccountingDimensionReportAsync(Guid companyId, Guid memberId,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingDimensionReportResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/dimensions/members/{memberId}/report" + BuildQuery(
                ("from", from?.ToString("yyyy-MM-dd")), ("to", to?.ToString("yyyy-MM-dd"))), false, cancellationToken);
}
