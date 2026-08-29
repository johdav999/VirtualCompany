namespace VirtualCompany.Application.Finance;

public static class BankProviderCapabilities
{
    public const string Accounts = "accounts";
    public const string AccountOwnership = "account_ownership";
    public const string Balances = "balances";
    public const string Transactions = "transactions";
    public const string PaymentInitiation = "payment_initiation";
}

public sealed record BankProviderDescriptor(
    string ProviderKey,
    string DisplayName,
    IReadOnlyCollection<string> Capabilities,
    bool IsConfigured);

public sealed record BankInstitutionDescriptor(
    string InstitutionId,
    string DisplayName,
    string? CountryCode,
    IReadOnlyCollection<string> Capabilities);

public sealed record BankProviderConsentStartRequest(
    Guid CompanyId,
    Guid SessionId,
    string InstitutionId,
    string ProtectedState,
    Uri CallbackUri,
    bool IsRenewal,
    IReadOnlyCollection<string> RequestedCapabilities);

public sealed record BankProviderConsentStartResult(
    Uri AuthorizationUri,
    string? ProviderSessionReference,
    DateTime ExpiresUtc);

public sealed record BankProviderCallbackRequest(
    Guid CompanyId,
    string InstitutionId,
    string AuthorizationCode,
    string? ProviderSessionReference,
    string? ProviderError);

public sealed record BankProviderCredentialBundle(
    string AccessToken,
    string? RefreshToken,
    string? ProviderCredential,
    DateTime? ExpiresUtc);

public sealed record BankProviderConsentResult(
    string ProviderConsentId,
    string InstitutionName,
    DateTime? ConsentExpiresUtc,
    IReadOnlyCollection<string> GrantedCapabilities,
    BankProviderCredentialBundle Credentials);

public sealed record BankProviderDiscoveredAccount(
    string ProviderAccountId,
    string DisplayName,
    string MaskedAccountNumber,
    string Currency,
    string OwnershipStatus,
    string? OwnershipSummary,
    string? ProviderAccessReference = null);

public sealed record BankProviderHealthResult(
    string HealthStatus,
    string? ReasonCode,
    string? SafeSummary);

public interface IBankConnectionProvider
{
    BankProviderDescriptor Descriptor { get; }
    Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(CancellationToken cancellationToken);
    Task<BankProviderConsentStartResult> StartConsentAsync(BankProviderConsentStartRequest request, CancellationToken cancellationToken);
    Task<BankProviderConsentResult> CompleteConsentAsync(BankProviderCallbackRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankProviderDiscoveredAccount>> DiscoverAccountsAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, CancellationToken cancellationToken);
    Task<BankProviderHealthResult> GetHealthAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, CancellationToken cancellationToken);
    Task RevokeConsentAsync(Guid companyId, string providerConsentId, BankProviderCredentialBundle credentials,
        CancellationToken cancellationToken);
}

public interface IBankConnectionProviderRegistry
{
    IReadOnlyList<BankProviderDescriptor> GetProviders();
    IBankConnectionProvider GetRequired(string providerKey);
}

public sealed record BankConsentCallbackState(
    Guid SessionId,
    Guid CompanyId,
    Guid UserId,
    string ProviderKey,
    string Nonce,
    DateTime IssuedUtc,
    DateTime ExpiresUtc);

public interface IBankConsentStateProtector
{
    string Protect(BankConsentCallbackState state);
    BankConsentCallbackState Unprotect(string protectedState);
}

public interface IProtectedBankCredentialStore
{
    Task StoreAsync(Guid companyId, Guid connectionId, BankProviderCredentialBundle credentials, DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<BankProviderCredentialBundle?> GetAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken);
    Task ClearAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken);
}

public sealed record StartBankConnectionCommand(Guid CompanyId, Guid ActorUserId, string ProviderKey,
    string InstitutionId, Uri CallbackUri, Uri? ReturnUri, IReadOnlyCollection<string> RequestedCapabilities,
    string? CorrelationId = null);
public sealed record CompleteBankConsentCallbackCommand(Guid? ExpectedCompanyId, Guid ActorUserId, string ProviderKey,
    string ProtectedState, string? AuthorizationCode, string? ProviderError, string? CorrelationId = null);
public sealed record RenewBankConnectionCommand(Guid CompanyId, Guid ConnectionId, Guid ActorUserId, long ExpectedVersion,
    Uri CallbackUri, Uri? ReturnUri, string? CorrelationId = null);
public sealed record MapDiscoveredBankAccountCommand(Guid CompanyId, Guid ConnectionId, Guid DiscoveredAccountId,
    Guid CompanyBankAccountId, Guid ActorUserId, long ExpectedConnectionVersion, string Reason, string? CorrelationId = null);
public sealed record ChangeBankConnectionStateCommand(Guid CompanyId, Guid ConnectionId, Guid ActorUserId,
    long ExpectedVersion, string? Reason, string? CorrelationId = null);
public sealed record RefreshBankConnectionCommand(Guid CompanyId, Guid ConnectionId, Guid ActorUserId,
    long ExpectedVersion, string? CorrelationId = null);

public sealed record BankConsentSessionResult(Guid SessionId, Uri AuthorizationUri, DateTime ExpiresUtc);
public sealed record BankConsentCallbackResult(Guid CompanyId, Guid ConnectionId, string Status, Uri? ReturnUri);
public sealed record BankSynchronizationAccessResult(bool Allowed, string? ReasonCode, string Explanation, bool RenewalRequired);
public sealed record BankAccountMappingResult(Guid MappingId, int MappingVersion, long ConnectionVersion);

public sealed record BankConnectionStatusResult(
    IReadOnlyList<BankProviderDescriptor> Providers,
    IReadOnlyList<BankConnectionItem> Connections,
    IReadOnlyList<BankInternalAccountOption> InternalAccounts);

public sealed record BankConnectionItem(Guid Id, string ProviderKey, string InstitutionId, string InstitutionName,
    string Status, string HealthStatus, string? ReasonCode, string? ReasonSummary, DateTime? ConsentExpiresUtc,
    DateTime? LastHealthCheckedUtc, long Version, IReadOnlyList<string> Capabilities,
    IReadOnlyList<BankDiscoveredAccountItem> Accounts);

public sealed record BankDiscoveredAccountItem(Guid Id, string ProviderAccountId, string DisplayName,
    string MaskedAccountNumber, string Currency, string OwnershipStatus, string? OwnershipSummary,
    int Version, Guid? MappedCompanyBankAccountId, string? MappedCompanyBankAccountName, int? MappingVersion);

public sealed record BankInternalAccountOption(Guid Id, string DisplayName, string MaskedAccountNumber, string Currency, bool IsActive);

public interface IBankConnectionService
{
    Task<BankConnectionStatusResult> GetStatusAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(string providerKey, CancellationToken cancellationToken);
    Task<BankConsentSessionResult> StartAsync(StartBankConnectionCommand command, CancellationToken cancellationToken);
    Task<BankConsentCallbackResult> CompleteCallbackAsync(CompleteBankConsentCallbackCommand command, CancellationToken cancellationToken);
    Task<BankConsentSessionResult> RenewAsync(RenewBankConnectionCommand command, CancellationToken cancellationToken);
    Task<BankAccountMappingResult> MapAccountAsync(MapDiscoveredBankAccountCommand command, CancellationToken cancellationToken);
    Task<BankConnectionStatusResult> RefreshAsync(RefreshBankConnectionCommand command, CancellationToken cancellationToken);
    Task SuspendAsync(ChangeBankConnectionStateCommand command, CancellationToken cancellationToken);
    Task DisconnectAsync(ChangeBankConnectionStateCommand command, CancellationToken cancellationToken);
    Task<BankSynchronizationAccessResult> GetSynchronizationAccessAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken);
}

public sealed class BankConnectionOperationException : Exception
{
    public BankConnectionOperationException(string reasonCode, string safeMessage, bool isConflict = false)
        : base(safeMessage) { ReasonCode = reasonCode; SafeMessage = safeMessage; IsConflict = isConflict; }
    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsConflict { get; }
}

public sealed class BankProviderSafeException : Exception
{
    public BankProviderSafeException(string reasonCode, string safeMessage, bool isTransient,
        Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(safeMessage, innerException)
    {
        ReasonCode = reasonCode;
        SafeMessage = safeMessage;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }
    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }
}
