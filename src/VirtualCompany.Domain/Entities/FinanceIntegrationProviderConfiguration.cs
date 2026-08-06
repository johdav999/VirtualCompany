using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceIntegrationProviderConfiguration
{
    private FinanceIntegrationProviderConfiguration()
    {
    }

    public FinanceIntegrationProviderConfiguration(
        Guid id,
        string providerKey,
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        bool enabled,
        string? credentialSecretName,
        string? credentialSecretVersion,
        Guid actorUserId,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ProviderKey = NormalizeProviderKey(providerKey);
        Apply(
            redirectUri,
            scopes,
            enabled,
            credentialSecretName,
            credentialSecretVersion,
            actorUserId,
            createdUtc);
        CreatedUtc = UpdatedUtc;
    }

    public Guid Id { get; private set; }
    public string ProviderKey { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string RedirectUri { get; private set; } = string.Empty;
    public string ScopesJson { get; private set; } = "[]";
    public string? CredentialSecretName { get; private set; }
    public string? CredentialSecretVersion { get; private set; }
    public string ValidationStatus { get; private set; } = "not_checked";
    public string? ValidationSummary { get; private set; }
    public DateTime? LastValidatedUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public IReadOnlyCollection<string> GetScopes() =>
        JsonSerializer.Deserialize<string[]>(ScopesJson) ?? [];

    public void Apply(
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        bool enabled,
        string? credentialSecretName,
        string? credentialSecretVersion,
        Guid actorUserId,
        DateTime updatedUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        RedirectUri = NormalizeRequired(redirectUri, nameof(redirectUri), 2048);
        ScopesJson = JsonSerializer.Serialize(NormalizeScopes(scopes));
        Enabled = enabled;
        CredentialSecretName = NormalizeOptional(credentialSecretName, nameof(credentialSecretName), 256);
        CredentialSecretVersion = NormalizeOptional(credentialSecretVersion, nameof(credentialSecretVersion), 256);
        ValidationStatus = "not_checked";
        ValidationSummary = null;
        LastValidatedUtc = null;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void RecordValidation(string status, string summary, Guid actorUserId, DateTime validatedUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        ValidationStatus = NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        ValidationSummary = NormalizeRequired(summary, nameof(summary), 1000);
        LastValidatedUtc = EntityTimestampNormalizer.NormalizeUtc(validatedUtc, nameof(validatedUtc));
        UpdatedByUserId = actorUserId;
        UpdatedUtc = LastValidatedUtc.Value;
        Version++;
    }

    private static string NormalizeProviderKey(string value) =>
        NormalizeRequired(value, nameof(value), 64).Replace('-', '_').ToLowerInvariant();

    private static string[] NormalizeScopes(IReadOnlyCollection<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeRequired(value, nameof(values), 128).ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string NormalizeRequired(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, name, maxLength);
}
