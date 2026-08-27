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
}

public sealed class CreateAccountingAccountApiRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
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
