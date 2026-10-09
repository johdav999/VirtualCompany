namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<IReadOnlyList<AccountingPolicyPackOptionResponse>> GetAccountingPolicyPacksAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<AccountingPolicyPackOptionResponse>>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/policy-packs",
            allowNotFound: false,
            cancellationToken) ?? [];

    public Task<AccountingSetupPreviewResponse> PreviewAccountingSetupAsync(
        Guid companyId,
        PreviewAccountingSetupApiRequest request,
        CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PreviewAccountingSetupApiRequest, AccountingSetupPreviewResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/setup/preview",
            request,
            cancellationToken);

    public Task<AccountingSetupCompletionResponse> CompleteAccountingSetupAsync(
        Guid companyId,
        CompleteAccountingSetupApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CompleteAccountingSetupApiRequest, AccountingSetupCompletionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/setup/complete",
            request,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingAccountListItemResponse>> GetAccountingAccountsAsync(
        Guid companyId,
        string? search = null,
        string? accountClass = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "search", search);
        AddQuery(query, "accountClass", accountClass);
        AddQuery(query, "status", status);
        var suffix = query.Count == 0 ? string.Empty : $"?{string.Join("&", query)}";
        return await GetAsync<List<AccountingAccountListItemResponse>>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/accounts{suffix}",
            allowNotFound: false,
            cancellationToken) ?? [];
    }

    public Task<AccountingAccountDetailResponse?> GetAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingAccountDetailResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}",
            allowNotFound: true,
            cancellationToken);

    public Task<AccountingAccountDetailResponse> CreateAccountingAccountAsync(
        Guid companyId,
        CreateAccountingAccountApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateAccountingAccountApiRequest, AccountingAccountDetailResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/accounts",
            request,
            cancellationToken);
    }

    public async Task<AccountingChartCatalogPageResponse> GetAccountingChartCatalogAsync(
        Guid companyId,
        string catalogKey = "bas-2026",
        string catalogVersion = "1.1",
        string? search = null,
        string? groupCode = null,
        bool k2Only = false,
        bool excludeExisting = false,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "search", search);
        AddQuery(query, "groupCode", groupCode);
        query.Add($"k2Only={k2Only.ToString().ToLowerInvariant()}");
        query.Add($"excludeExisting={excludeExisting.ToString().ToLowerInvariant()}");
        query.Add($"skip={skip}");
        query.Add($"take={take}");
        return await GetAsync<AccountingChartCatalogPageResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/chart-catalogs/{Uri.EscapeDataString(catalogKey)}/{Uri.EscapeDataString(catalogVersion)}/accounts?{string.Join("&", query)}",
            allowNotFound: false,
            cancellationToken) ?? new AccountingChartCatalogPageResponse();
    }

    public Task<AccountingAccountDetailResponse> CreateAccountingAccountFromChartCatalogAsync(
        Guid companyId,
        CreateAccountingAccountFromChartCatalogApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateAccountingAccountFromChartCatalogApiRequest, AccountingAccountDetailResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/accounts/from-chart-catalog",
            request,
            cancellationToken);
    }

    public Task<AccountingAccountDetailResponse> RenameAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        RenameAccountingAccountApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RenameAccountingAccountApiRequest, AccountingAccountDetailResponse>(
            companyId,
            HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/name",
            request,
            cancellationToken);
    }

    public Task<AccountingAccountDetailResponse> DeactivateAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        DeactivateAccountingAccountApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<DeactivateAccountingAccountApiRequest, AccountingAccountDetailResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/deactivate",
            request,
            cancellationToken);
    }

    public Task<AccountingAccountLifecyclePreviewResponse> PreviewAccountingAccountLifecycleAsync(Guid companyId,
        Guid accountId, PreviewAccountingAccountLifecycleApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PreviewAccountingAccountLifecycleApiRequest, AccountingAccountLifecyclePreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/lifecycle/preview", request, cancellationToken);

    public Task<AccountingAccountDetailResponse> ApplyAccountingAccountLifecycleAsync(Guid companyId,
        Guid accountId, ApplyAccountingAccountLifecycleApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ApplyAccountingAccountLifecycleApiRequest, AccountingAccountDetailResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/lifecycle", request, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingSeriesPolicyResponse>> GetAccountingSeriesPoliciesAsync(Guid companyId,
        CancellationToken cancellationToken = default) => await GetAsync<List<AccountingSeriesPolicyResponse>>(companyId,
            $"internal/companies/{companyId}/finance/accounting/series-policies", false, cancellationToken) ?? [];

    public Task<AccountingSeriesPolicyResponse> SaveAccountingSeriesPolicyAsync(Guid companyId,
        SaveAccountingSeriesPolicyApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveAccountingSeriesPolicyApiRequest, AccountingSeriesPolicyResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/series-policies", request, cancellationToken);
    }

    public Task<AccountingSeriesPolicyResponse> RecordAccountingVoucherGapAsync(Guid companyId, Guid seriesId,
        RecordAccountingVoucherGapApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordAccountingVoucherGapApiRequest, AccountingSeriesPolicyResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/voucher-series/{seriesId:D}/gaps", request, cancellationToken);
    }

    public async Task<CommerceAccountingCapabilityResponse> GetCommerceAccountingCapabilityAsync(Guid companyId,
        CancellationToken cancellationToken = default) => await GetAsync<CommerceAccountingCapabilityResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/commerce/capability", false, cancellationToken) ?? new();

    public async Task<IReadOnlyList<AccountingFiscalYearResponse>> GetAccountingFiscalYearsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<AccountingFiscalYearResponse>>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/fiscal-years",
            allowNotFound: false,
            cancellationToken) ?? [];

    public Task<AccountingPeriodResponse?> GetAccountingPeriodAsync(
        Guid companyId,
        Guid periodId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingPeriodResponse>(
            companyId,
            $"internal/companies/{companyId}/finance/accounting/periods/{periodId:D}",
            allowNotFound: true,
            cancellationToken);

    public Task<AccountingFiscalYearPreviewResponse> PreviewAccountingFiscalYearAsync(
        Guid companyId,
        PreviewAccountingFiscalYearApiRequest request,
        CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PreviewAccountingFiscalYearApiRequest, AccountingFiscalYearPreviewResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/fiscal-years/preview",
            request,
            cancellationToken);

    public Task<AccountingFiscalYearCreationResponse> CreateAccountingFiscalYearAsync(
        Guid companyId,
        CreateAccountingFiscalYearApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateAccountingFiscalYearApiRequest, AccountingFiscalYearCreationResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/fiscal-years",
            request,
            cancellationToken);
    }

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
public sealed class RecordAccountingVoucherGapApiRequest
{
    public int FiscalYear { get; set; }
    public long MissingNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}
