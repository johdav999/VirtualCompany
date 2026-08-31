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

public sealed class AccountingPolicyPackOptionResponse
{
    public string PackKey { get; set; } = string.Empty;
    public string PackVersion { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryOrRegion { get; set; }
    public bool IsCountryNeutral { get; set; }
    public bool IsStatutoryComplianceValidated { get; set; }
    public string ComplianceNotice { get; set; } = string.Empty;
    public List<AccountingChartTemplateOptionResponse> ChartTemplates { get; set; } = [];
}

public sealed class AccountingChartTemplateOptionResponse
{
    public string TemplateKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int AccountCount { get; set; }
}

public class PreviewAccountingSetupApiRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public string ChartTemplateKey { get; set; } = string.Empty;
    public Dictionary<string, string> AccountRoleCodeAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompleteAccountingSetupApiRequest : PreviewAccountingSetupApiRequest
{
    public string? IdempotencyKey { get; set; }
}

public sealed class AccountingSetupPreviewResponse
{
    public Guid CompanyId { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public string PolicyPackName { get; set; } = string.Empty;
    public string ChartTemplateName { get; set; } = string.Empty;
    public bool IsCountryNeutral { get; set; }
    public bool IsStatutoryComplianceValidated { get; set; }
    public string ComplianceNotice { get; set; } = string.Empty;
    public string TaxBehavior { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public bool IsAlreadyConfigured { get; set; }
    public List<AccountingSetupAccountPreviewResponse> Accounts { get; set; } = [];
    public List<AccountingSetupTaxPreviewResponse> TaxRules { get; set; } = [];
    public List<AccountingSetupPeriodPreviewResponse> Periods { get; set; } = [];
    public List<AccountingVoucherSeriesPreviewResponse> VoucherSeries { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Issues { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Warnings { get; set; } = [];
    public CompanyStatutoryProfileStatusResponse? StatutoryProfile { get; set; }
    public string PolicyPackValidationState { get; set; } = string.Empty;
    public List<string> MissingLegalFacts { get; set; } = [];
    public List<string> NextActions { get; set; } = [];
}

public sealed class AccountingSetupAccountPreviewResponse
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public string? RoleName { get; set; }
    public bool IsControlAccount { get; set; }
    public string ReportingPlacement { get; set; } = string.Empty;
}

public sealed class AccountingSetupTaxPreviewResponse
{
    public string Name { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class AccountingSetupPeriodPreviewResponse
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public sealed class AccountingVoucherSeriesPreviewResponse
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NumberPrefix { get; set; } = string.Empty;
}

public sealed class AccountingSetupCompletionResponse
{
    public AccountingSetupStatusResponse SetupStatus { get; set; } = new();
    public int AccountCount { get; set; }
    public int PeriodCount { get; set; }
    public int VoucherSeriesCount { get; set; }
    public bool WasAlreadyApplied { get; set; }
}

public sealed class AccountingAccountListItemResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPostingEnabled { get; set; }
    public bool HasPostedHistory { get; set; }
    public bool IsProtected { get; set; }
    public string? ProtectedReason { get; set; }
    public string? RoleName { get; set; }
    public string? ReportingPlacement { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsReportable { get; set; }
    public string PostingRestriction { get; set; } = "none";
    public Guid? ReplacementAccountId { get; set; }
    public string LifecycleStatus { get; set; } = "active";
    public long LifecycleVersion { get; set; }
    public int DependencyCount { get; set; }
}

public sealed class AccountingAccountDetailResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPostingEnabled { get; set; }
    public bool RestrictsManualPosting { get; set; }
    public bool HasPostedHistory { get; set; }
    public bool IsProtected { get; set; }
    public string? ProtectedReason { get; set; }
    public string? RoleName { get; set; }
    public string? ReportingPlacement { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsReportable { get; set; }
    public string PostingRestriction { get; set; } = "none";
    public Guid? ReplacementAccountId { get; set; }
    public string? ReplacementAccountCode { get; set; }
    public string LifecycleStatus { get; set; } = "active";
    public long LifecycleVersion { get; set; }
    public List<AccountingAccountLifecycleHistoryResponse> LifecycleHistory { get; set; } = [];
}

public sealed class AccountingAccountLifecycleHistoryResponse
{
    public long Version { get; set; } public string ChangeType { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty; public string NormalBalance { get; set; } = string.Empty;
    public bool IsReportable { get; set; } public string PostingRestriction { get; set; } = "none";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public Guid? ReplacementAccountId { get; set; }
    public string Reason { get; set; } = string.Empty; public Guid? ActorUserId { get; set; } public DateTime RecordedUtc { get; set; }
}

public class PreviewAccountingAccountLifecycleApiRequest
{
    public string AccountClass { get; set; } = string.Empty; public string NormalBalance { get; set; } = string.Empty;
    public bool IsReportable { get; set; } = true; public string PostingRestriction { get; set; } = "none";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public Guid? ReplacementAccountId { get; set; }
}
public sealed class ApplyAccountingAccountLifecycleApiRequest : PreviewAccountingAccountLifecycleApiRequest
{
    public string Name { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; public long ExpectedLifecycleVersion { get; set; }
}
public sealed class AccountingAccountDependencyResponse { public string DependencyType { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public int Count { get; set; } public bool IsBlocking { get; set; } }
public sealed class AccountingAccountLifecyclePreviewResponse
{
    public Guid AccountId { get; set; } public string AccountCode { get; set; } = string.Empty; public bool HasPostedHistory { get; set; }
    public bool ReplacementRequired { get; set; } public bool CanApply { get; set; }
    public List<AccountingAccountDependencyResponse> Dependencies { get; set; } = []; public List<AccountingConfigurationIssueResponse> Issues { get; set; } = [];
}

public sealed class AccountingSeriesPolicyResponse
{
    public Guid Id { get; set; } public string SeriesKind { get; set; } = string.Empty; public Guid SeriesId { get; set; }
    public string SeriesCode { get; set; } = string.Empty; public string SeriesName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty; public string TransactionType { get; set; } = string.Empty;
    public int? FiscalYear { get; set; } public Guid? LocationDimensionMemberId { get; set; } public string? Jurisdiction { get; set; }
    public string PolicyPackKey { get; set; } = string.Empty; public string PolicyPackVersion { get; set; } = string.Empty;
    public string? ProviderKey { get; set; } public string? ProviderSeriesCode { get; set; } public bool IsActive { get; set; }
    public long Version { get; set; } public int UnexplainedGapCount { get; set; }
}
public sealed class SaveAccountingSeriesPolicyApiRequest
{
    public Guid? PolicyId { get; set; } public string SeriesKind { get; set; } = "voucher"; public Guid SeriesId { get; set; }
    public string SourceType { get; set; } = "*"; public string TransactionType { get; set; } = "*"; public int? FiscalYear { get; set; }
    public Guid? LocationDimensionMemberId { get; set; } public string? Jurisdiction { get; set; } public string? ProviderKey { get; set; }
    public string? ProviderSeriesCode { get; set; } public bool IsActive { get; set; } = true; public long? ExpectedVersion { get; set; }
}
public sealed class RecordAccountingVoucherGapApiRequest
{
    public int FiscalYear { get; set; }
    public long MissingNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}
public sealed class CommerceAccountingCapabilityResponse
{
    public string CapabilityState { get; set; } = string.Empty; public string ContractVersion { get; set; } = string.Empty;
    public bool SupportsInventoryQuantity { get; set; } public bool SupportsInventoryValuation { get; set; } public bool SupportsCogs { get; set; }
    public List<string> AcceptedEventTypes { get; set; } = []; public string Explanation { get; set; } = string.Empty;
}

public sealed class CreateAccountingAccountApiRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class AccountingChartCatalogPageResponse
{
    public string CatalogKey { get; set; } = string.Empty;
    public string CatalogVersion { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public int TotalAccountCount { get; set; }
    public int MatchedAccountCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<string> Limitations { get; set; } = [];
    public List<AccountingChartCatalogGroupResponse> Groups { get; set; } = [];
    public List<AccountingChartCatalogAccountResponse> Accounts { get; set; } = [];
}

public sealed class AccountingChartCatalogGroupResponse
{
    public string Code { get; set; } = string.Empty;
    public string NameSv { get; set; } = string.Empty;
}

public sealed class AccountingChartCatalogAccountResponse
{
    public string Code { get; set; } = string.Empty;
    public string NameSv { get; set; } = string.Empty;
    public List<string> NameVariantsSv { get; set; } = [];
    public bool RequiresNameSelection { get; set; }
    public bool IsK2Allowed { get; set; }
    public bool IsSubAccount { get; set; }
    public string? ParentAccountCode { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupNameSv { get; set; } = string.Empty;
    public string? SuggestedAccountClass { get; set; }
    public string? SuggestedNormalBalance { get; set; }
    public bool RequiresSemanticsConfirmation { get; set; }
    public bool RequiresCompanySuitabilityConfirmation { get; set; }
    public bool IsAlreadyAdded { get; set; }
}

public sealed class CreateAccountingAccountFromChartCatalogApiRequest
{
    public string CatalogKey { get; set; } = "bas-2026";
    public string CatalogVersion { get; set; } = "1.1";
    public string Code { get; set; } = string.Empty;
    public string? NameSv { get; set; }
    public string? AccountClass { get; set; }
    public string? NormalBalance { get; set; }
    public bool AccountingSemanticsConfirmed { get; set; }
    public bool CompanySuitabilityConfirmed { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class RenameAccountingAccountApiRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTime ExpectedUpdatedUtc { get; set; }
}

public sealed class DeactivateAccountingAccountApiRequest
{
    public DateOnly EffectiveTo { get; set; }
    public DateTime ExpectedUpdatedUtc { get; set; }
}

public sealed class AccountingPeriodResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public bool IsReportingLocked { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public DateTime? ReportingLockedUtc { get; set; }
    public DateTime? LastCloseValidatedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AccountingFiscalYearResponse
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int OpenPeriodCount { get; set; }
    public int ClosedPeriodCount { get; set; }
    public int ReportingLockedPeriodCount { get; set; }
    public List<AccountingPeriodResponse> Periods { get; set; } = [];
}

public sealed class PreviewAccountingFiscalYearApiRequest
{
    public DateOnly FiscalYearStart { get; set; }
}

public sealed class CreateAccountingFiscalYearApiRequest
{
    public DateOnly FiscalYearStart { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class AccountingFiscalYearPreviewResponse
{
    public Guid CompanyId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsValid { get; set; }
    public List<AccountingSetupPeriodPreviewResponse> Periods { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Issues { get; set; } = [];
}

public sealed class AccountingFiscalYearCreationResponse
{
    public AccountingFiscalYearResponse FiscalYear { get; set; } = new();
    public bool WasAlreadyPresent { get; set; }
}
