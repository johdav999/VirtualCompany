using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ExternalAccountConnection : ICompanyOwnedEntity
{
    private ExternalAccountConnection() { }

    public ExternalAccountConnection(
        Guid id, Guid companyId, Guid userId, ExternalAccountProvider provider,
        string accountEmail, string? displayName, string? externalAccountId,
        string credentialPurposePrefix, DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        Provider = provider;
        AccountEmail = NormalizeEmail(accountEmail);
        DisplayName = NormalizeOptional(displayName, 200);
        ExternalAccountId = NormalizeOptional(externalAccountId, 256);
        CredentialPurposePrefix = NormalizeRequired(credentialPurposePrefix, 160);
        Status = ExternalConnectionStatus.Pending;
        GrantedScopes = [];
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public ExternalAccountProvider Provider { get; private set; }
    public string AccountEmail { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public string? ExternalAccountId { get; private set; }
    public string CredentialPurposePrefix { get; private set; } = null!;
    public ExternalConnectionStatus Status { get; private set; }
    public string? EncryptedAccessToken { get; private set; }
    public string? EncryptedRefreshToken { get; private set; }
    public DateTime? AccessTokenExpiresUtc { get; private set; }
    public List<string> GrantedScopes { get; private set; } = [];
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;
    public ICollection<MailboxConnection> MailboxConnections { get; } = new List<MailboxConnection>();
    public ICollection<CalendarConnection> CalendarConnections { get; } = new List<CalendarConnection>();

    public void UpdateProfile(string email, string? displayName, string? externalAccountId)
    {
        AccountEmail = NormalizeEmail(email);
        DisplayName = NormalizeOptional(displayName, 200);
        ExternalAccountId = NormalizeOptional(externalAccountId, 256);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void StoreEncryptedCredentials(
        string encryptedAccessToken, string? encryptedRefreshToken,
        DateTime? expiresUtc, IReadOnlyCollection<string> grantedScopes)
    {
        EncryptedAccessToken = NormalizeRequired(encryptedAccessToken, 8192);
        EncryptedRefreshToken = NormalizeOptional(encryptedRefreshToken, 8192);
        AccessTokenExpiresUtc = expiresUtc.HasValue ? NormalizeUtc(expiresUtc.Value) : null;
        GrantedScopes = grantedScopes.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(ExternalConnectionStatus status, string? errorCode = null, string? errorSummary = null)
    {
        Status = status;
        LastErrorCode = NormalizeOptional(errorCode, 120);
        LastErrorSummary = NormalizeOptional(errorSummary, 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public string CredentialPurpose(string tokenKind) => $"{CredentialPurposePrefix}:{NormalizeRequired(tokenKind, 40)}";

    private static string NormalizeEmail(string value) =>
        NormalizeRequired(value, 256).ToLowerInvariant();
    private static string NormalizeRequired(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.") : value.Trim().Length > max ? throw new ArgumentException($"Value cannot exceed {max} characters.") : value.Trim();
    private static string? NormalizeOptional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, max);
    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
