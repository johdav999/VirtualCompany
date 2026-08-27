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

public sealed class CreateAccountingConfigurationApiRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public int FiscalYearStartMonth { get; set; } = 1;
    public int FiscalYearStartDay { get; set; } = 1;
    public string? PolicyPackKey { get; set; }
    public string? PolicyPackVersion { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public int RoundingPrecision { get; set; } = 2;
    public string? RoundingMode { get; set; }
    public Dictionary<string, Guid> AccountRoleAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PreviewAccountingPolicyPackApiRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public Dictionary<string, Guid> AccountRoleAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ApplyAccountingPolicyPackApiRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public long ExpectedVersion { get; set; }
    public Dictionary<string, Guid> AccountRoleAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AccountingSetupStatusResponse
{
    public Guid CompanyId { get; set; }
    public bool IsConfigured { get; set; }
    public bool CanUseInternalLedger { get; set; }
    public bool IsReady { get; set; }
    public bool IsCountrySpecificComplianceConfigured { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string SetupState { get; set; } = string.Empty;
    public AccountingConfigurationResponse? Configuration { get; set; }
    public List<AccountingConfigurationIssueResponse> Issues { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Warnings { get; set; } = [];
    public CompanyStatutoryProfileStatusResponse? StatutoryProfile { get; set; }
    public string PolicyPackValidationState { get; set; } = string.Empty;
    public List<string> MissingLegalFacts { get; set; } = [];
    public List<string> NextActions { get; set; } = [];
}

public sealed class SaveCompanyStatutoryProfileApiRequest
{
    public long? ExpectedVersion { get; set; }
    public string? LegalName { get; set; }
    public string? SwedishOrganisationNumber { get; set; }
    public string? VatRegistrationNumber { get; set; }
    public string VatRegistrationStatus { get; set; } = "not_registered";
    public StatutoryAddressResponse RegisteredAddress { get; set; } = new();
    public StatutoryAddressResponse? CorrespondenceAddress { get; set; }
    public string CountryCode { get; set; } = "SE";
    public string AccountingCurrency { get; set; } = "SEK";
    public string FiscalYearBasis { get; set; } = "calendar_year";
    public string BookkeepingMethod { get; set; } = "not_specified";
    public DateOnly? OrganisationRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveTo { get; set; }
    public bool IsUserAttested { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string SourceKind { get; set; } = "user_entry";
    public string? SourceReference { get; set; }
    public DateTime? SourceCapturedUtc { get; set; }
    public string? ExternalVerifier { get; set; }
    public DateTime? ExternallyVerifiedUtc { get; set; }
}

public sealed class StatutoryAddressResponse
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
}

public sealed class CompanyStatutoryProfileStatusResponse
{
    public Guid CompanyId { get; set; }
    public bool Exists { get; set; }
    public bool IsFormatComplete { get; set; }
    public bool IsUserAttested { get; set; }
    public bool IsExternallyVerified { get; set; }
    public bool IsCompleteForSelectedPolicyPack { get; set; }
    public string VerificationExplanation { get; set; } = string.Empty;
    public List<string> MissingFacts { get; set; } = [];
    public List<string> NextActions { get; set; } = [];
    public CompanyStatutoryProfileResponse? Profile { get; set; }
}

public sealed class CompanyStatutoryProfileResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? LegalName { get; set; }
    public string? SwedishOrganisationNumber { get; set; }
    public string? VatRegistrationNumber { get; set; }
    public string VatRegistrationStatus { get; set; } = string.Empty;
    public StatutoryAddressResponse RegisteredAddress { get; set; } = new();
    public StatutoryAddressResponse CorrespondenceAddress { get; set; } = new();
    public string CountryCode { get; set; } = string.Empty;
    public string AccountingCurrency { get; set; } = string.Empty;
    public string FiscalYearBasis { get; set; } = string.Empty;
    public string BookkeepingMethod { get; set; } = string.Empty;
    public DateOnly? OrganisationRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveTo { get; set; }
    public bool IsFormatComplete { get; set; }
    public bool IsUserAttested { get; set; }
    public Guid? AttestedByUserId { get; set; }
    public DateTime? AttestedUtc { get; set; }
    public string VerificationStatus { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public DateTime SourceCapturedUtc { get; set; }
    public string? ExternalVerifier { get; set; }
    public DateTime? ExternallyVerifiedUtc { get; set; }
    public long Version { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AccountingConfigurationResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public int FiscalYearStartMonth { get; set; }
    public int FiscalYearStartDay { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string SetupState { get; set; } = string.Empty;
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly PolicyPackEffectiveFrom { get; set; }
    public int RoundingPrecision { get; set; }
    public string RoundingMode { get; set; } = string.Empty;
    public long Version { get; set; }
    public bool IsCountryNeutral { get; set; }
    public bool IsStatutoryComplianceValidated { get; set; }
    public string ComplianceNotice { get; set; } = string.Empty;
    public List<AccountingAccountRoleReferenceResponse> AccountRoles { get; set; } = [];
    public List<AccountingPolicyPackSelectionResponse> PolicyPackHistory { get; set; } = [];
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AccountingConfigurationIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string? SubjectKey { get; set; }
    public bool IsBlocking { get; set; }
}

public sealed class AccountingAccountRoleReferenceResponse
{
    public string RoleKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsControlAccount { get; set; }
    public Guid? FinanceAccountId { get; set; }
    public string? FinanceAccountCode { get; set; }
    public string? FinanceAccountName { get; set; }
}

public sealed class AccountingPolicyPackSelectionResponse
{
    public Guid Id { get; set; }
    public string PackKey { get; set; } = string.Empty;
    public string PackVersion { get; set; } = string.Empty;
    public string DefinitionHash { get; set; } = string.Empty;
    public bool IsStatutoryComplianceValidated { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Guid SelectedByUserId { get; set; }
    public DateTime SelectedUtc { get; set; }
}

public sealed class AccountingPolicyPackImpactPreviewResponse
{
    public Guid CompanyId { get; set; }
    public string TargetPackKey { get; set; } = string.Empty;
    public string TargetPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public bool IsAllowed { get; set; }
    public bool IsUpgrade { get; set; }
    public List<string> AddedRequiredAccountRoles { get; set; } = [];
    public List<string> RemovedAccountRoles { get; set; } = [];
    public List<string> AddedTaxRules { get; set; } = [];
    public List<string> RemovedTaxRules { get; set; } = [];
    public List<string> AddedExports { get; set; } = [];
    public List<string> RemovedExports { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Issues { get; set; } = [];
    public List<AccountingConfigurationIssueResponse> Warnings { get; set; } = [];
}

public sealed class AccountingCapabilityDecisionResponse
{
    public Guid CompanyId { get; set; }
    public string CapabilityKey { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string? ReasonCode { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
}
