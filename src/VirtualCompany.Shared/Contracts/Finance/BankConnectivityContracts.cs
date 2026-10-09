
namespace VirtualCompany.Application.Finance;

public sealed record BankConnectionStatusResult(
    IReadOnlyList<BankProviderDescriptor> Providers,
    IReadOnlyList<BankConnectionItem> Connections,
    IReadOnlyList<BankInternalAccountOption> InternalAccounts);


public sealed record BankProviderDescriptor(
    string ProviderKey,
    string DisplayName,
    IReadOnlyCollection<string> Capabilities,
    bool IsConfigured);


public sealed record BankInternalAccountOption(Guid Id, string DisplayName, string MaskedAccountNumber, string Currency, bool IsActive);


public sealed record BankConnectionItem(Guid Id, string ProviderKey, string InstitutionId, string InstitutionName,
    string Status, string HealthStatus, string? ReasonCode, string? ReasonSummary, DateTime? ConsentExpiresUtc,
    DateTime? LastHealthCheckedUtc, long Version, IReadOnlyList<string> Capabilities,
    IReadOnlyList<BankDiscoveredAccountItem> Accounts);


public sealed record BankDiscoveredAccountItem(Guid Id, string ProviderAccountId, string DisplayName,
    string MaskedAccountNumber, string Currency, string OwnershipStatus, string? OwnershipSummary,
    int Version, Guid? MappedCompanyBankAccountId, string? MappedCompanyBankAccountName, int? MappingVersion);

public sealed record BankSynchronizationAccessResult(bool Allowed, string? ReasonCode, string Explanation, bool RenewalRequired);

public sealed record BankAccountMappingResult(Guid MappingId, int MappingVersion, long ConnectionVersion);


public sealed record BankConsentSessionResult(Guid SessionId, Uri AuthorizationUri, DateTime ExpiresUtc);


public sealed record BankInstitutionDescriptor(
    string InstitutionId,
    string DisplayName,
    string? CountryCode,
    IReadOnlyCollection<string> Capabilities);
