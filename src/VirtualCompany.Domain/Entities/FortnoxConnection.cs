using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FortnoxConnection : ICompanyOwnedEntity
{
    private const int MaxEncryptedTokenLength = 65536;

    private FortnoxConnection()
    {
    }

    public FortnoxConnection(Guid id, Guid companyId, Guid connectedByUserId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (connectedByUserId == Guid.Empty)
        {
            throw new ArgumentException("ConnectedByUserId is required.", nameof(connectedByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ConnectedByUserId = connectedByUserId;
        Status = FortnoxConnectionStatus.Pending;
        GrantedScopes = [];
        ProviderMetadata = [];
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectedByUserId { get; private set; }
    public FortnoxConnectionStatus Status { get; private set; }
    public string? EncryptedAccessToken { get; private set; }
    public string? EncryptedRefreshToken { get; private set; }
    public string? TokenEncryptionKeyId { get; private set; }
    public string? TokenEncryptionAlgorithm { get; private set; }
    public DateTime? AccessTokenExpiresUtc { get; private set; }
    public DateTime? RefreshTokenExpiresUtc { get; private set; }
    public List<string> GrantedScopes { get; private set; } = [];
    public string? ProviderTenantId { get; private set; }
    public string? FortnoxCompanyName { get; private set; }
    public JsonObject ProviderMetadata { get; private set; } = [];
    public DateTime? ConnectedUtc { get; private set; }
    public DateTime? LastRefreshAttemptUtc { get; private set; }
    public DateTime? LastSuccessfulRefreshUtc { get; private set; }
    public DateTime? LastValidatedUtc { get; private set; }
    public DateTime? LastSyncUtc { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime? DisconnectedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User ConnectedByUser { get; private set; } = null!;

    public void StoreEncryptedTokens(
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime? accessTokenExpiresUtc,
        IReadOnlyCollection<string>? grantedScopes,
        string? providerTenantId,
        DateTime nowUtc,
        DateTime? refreshTokenExpiresUtc = null,
        string? tokenEncryptionKeyId = null,
        string? tokenEncryptionAlgorithm = null,
        string? fortnoxCompanyName = null)
    {
        EncryptedAccessToken = NormalizeRequired(encryptedAccessToken, nameof(encryptedAccessToken), MaxEncryptedTokenLength);
        EncryptedRefreshToken = NormalizeRequired(encryptedRefreshToken, nameof(encryptedRefreshToken), MaxEncryptedTokenLength);
        AccessTokenExpiresUtc = accessTokenExpiresUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(accessTokenExpiresUtc.Value, nameof(accessTokenExpiresUtc))
            : null;
        GrantedScopes = NormalizeStringList(grantedScopes, nameof(grantedScopes), 256);
        RefreshTokenExpiresUtc = refreshTokenExpiresUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(refreshTokenExpiresUtc.Value, nameof(refreshTokenExpiresUtc))
            : RefreshTokenExpiresUtc;
        TokenEncryptionKeyId = NormalizeOptional(tokenEncryptionKeyId, nameof(tokenEncryptionKeyId), 128) ?? TokenEncryptionKeyId;
        TokenEncryptionAlgorithm = NormalizeOptional(tokenEncryptionAlgorithm, nameof(tokenEncryptionAlgorithm), 64) ?? TokenEncryptionAlgorithm;
        ProviderTenantId = NormalizeOptional(providerTenantId, nameof(providerTenantId), 256);
        FortnoxCompanyName = NormalizeOptional(fortnoxCompanyName, nameof(fortnoxCompanyName), 256) ?? FortnoxCompanyName;
        Status = FortnoxConnectionStatus.Connected;
        DisconnectedUtc = null;
        ConnectedUtc ??= EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        LastValidatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        LastErrorSummary = null;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
    }

    public void StoreRefreshedTokens(
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime? accessTokenExpiresUtc,
        IReadOnlyCollection<string>? grantedScopes,
        DateTime nowUtc)
    {
        var previousRefreshTokenExpiry = RefreshTokenExpiresUtc;
        StoreEncryptedTokens(
            encryptedAccessToken,
            encryptedRefreshToken,
            accessTokenExpiresUtc,
            grantedScopes is null || grantedScopes.Count == 0 ? GrantedScopes : grantedScopes,
            ProviderTenantId,
            nowUtc);
        RefreshTokenExpiresUtc = previousRefreshTokenExpiry;
        LastRefreshAttemptUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        LastSuccessfulRefreshUtc = LastRefreshAttemptUtc;
    }

    public void RecordRefreshAttempt(DateTime nowUtc)
    {
        LastRefreshAttemptUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        UpdatedUtc = LastRefreshAttemptUtc.Value;
    }

    public void MarkValidated(DateTime nowUtc)
    {
        LastValidatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        UpdatedUtc = LastValidatedUtc.Value;
    }

    public void MarkSyncSucceeded(DateTime completedUtc)
    {
        LastSyncUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        LastErrorSummary = null;
        UpdatedUtc = LastSyncUtc.Value;
    }

    public void ClearTokenMaterial(DateTime nowUtc)
    {
        EncryptedAccessToken = null;
        EncryptedRefreshToken = null;
        AccessTokenExpiresUtc = null;
        RefreshTokenExpiresUtc = null;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
    }

    public void SetStatus(FortnoxConnectionStatus status, string? safeErrorSummary, DateTime nowUtc)
    {
        FortnoxConnectionStatusValues.EnsureSupported(status, nameof(status));
        Status = status;
        LastErrorSummary = NormalizeOptional(safeErrorSummary, nameof(safeErrorSummary), 1000);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));

        if (status == FortnoxConnectionStatus.Disconnected)
        {
            DisconnectedUtc = UpdatedUtc;
        }
    }

    private static List<string> NormalizeStringList(IReadOnlyCollection<string>? values, string name, int maxLength) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeRequired(value, name, maxLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}
