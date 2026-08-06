using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed record FinanceIntegrationRuntimeSettings(
    string ProviderKey,
    bool Enabled,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    IReadOnlyCollection<string> Scopes);

public interface IFinanceIntegrationRuntimeSettingsProvider
{
    Task<FinanceIntegrationRuntimeSettings> GetRequiredAsync(
        string providerKey,
        CancellationToken cancellationToken);
}

public sealed class FinanceIntegrationApplicationUnavailableException : InvalidOperationException
{
    public FinanceIntegrationApplicationUnavailableException(string providerKey)
        : base($"{providerKey} connections are temporarily unavailable. A Virtual Company administrator needs to finish the integration setup.")
    {
        ProviderKey = providerKey;
    }

    public string ProviderKey { get; }
}

public sealed class FinanceIntegrationApplicationConfigurationException : InvalidOperationException
{
    public FinanceIntegrationApplicationConfigurationException(string message, bool isConflict = false)
        : base(message)
    {
        IsConflict = isConflict;
    }

    public bool IsConflict { get; }
}

internal sealed class FinanceIntegrationApplicationManagementService :
    IFinanceIntegrationApplicationManagementService,
    IFinanceIntegrationRuntimeSettingsProvider
{
    private const string CredentialSecretSuffix = "oauth-credentials";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IPlatformSecretStore _secretStore;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<string, FinanceIntegrationApplicationDefinition> _definitions;

    public FinanceIntegrationApplicationManagementService(
        VirtualCompanyDbContext dbContext,
        IPlatformSecretStore secretStore,
        IConfiguration configuration,
        TimeProvider timeProvider,
        IEnumerable<IFinanceIntegrationApplicationDefinition> definitions)
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _definitions = definitions
            .Select(item => item.Definition)
            .ToDictionary(
                item => NormalizeProviderKey(item.ProviderKey),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FinanceIntegrationApplicationConfigurationList> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var providers = new List<FinanceIntegrationApplicationConfigurationDto>();
        foreach (var definition in _definitions.Values.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            providers.Add(await GetAsync(definition.ProviderKey, cancellationToken));
        }

        return new FinanceIntegrationApplicationConfigurationList(providers);
    }

    public async Task<FinanceIntegrationApplicationConfigurationDto> GetAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var definition = GetDefinition(providerKey);
        var effective = await LoadEffectiveAsync(definition, cancellationToken);
        return Map(definition, effective);
    }

    public async Task<FinanceIntegrationApplicationConfigurationDto> SaveAsync(
        SaveFinanceIntegrationApplicationConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved platform administrator is required.");
        }

        var definition = GetDefinition(command.ProviderKey);
        var providerKey = NormalizeProviderKey(definition.ProviderKey);
        var existing = await _dbContext.FinanceIntegrationProviderConfigurations
            .SingleOrDefaultAsync(item => item.ProviderKey == providerKey, cancellationToken);
        var current = await LoadEffectiveAsync(definition, cancellationToken);
        var clientId = string.IsNullOrWhiteSpace(command.ClientId)
            ? current.ClientId
            : command.ClientId.Trim();
        var clientSecret = string.IsNullOrWhiteSpace(command.ClientSecret)
            ? current.ClientSecret
            : command.ClientSecret.Trim();
        var redirectUri = NormalizeRedirectUri(command.RedirectUri);
        var scopes = NormalizeScopes(definition, command.Scopes);

        if (command.Enabled && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
        {
            throw new ArgumentException(
                $"{definition.DisplayName} requires both a client ID and client secret before it can be enabled.");
        }

        var changedFields = DetermineChangedFields(current, command.Enabled, clientId, command.ClientSecret, redirectUri, scopes);
        var secretName = existing?.CredentialSecretName ?? BuildCredentialSecretName(providerKey);
        var secretVersion = existing?.CredentialSecretVersion;
        if (!string.Equals(clientId, current.ClientId, StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(command.ClientSecret) ||
            string.IsNullOrWhiteSpace(secretVersion))
        {
            if (!_secretStore.SupportsWrites)
            {
                throw new FinanceIntegrationApplicationConfigurationException(
                    "The configured production secret store is read-only or unavailable.");
            }

            var payload = JsonSerializer.Serialize(new OAuthApplicationCredentials(clientId, clientSecret));
            secretVersion = (await _secretStore.SetAsync(secretName, payload, cancellationToken)).Version;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            existing = new FinanceIntegrationProviderConfiguration(
                Guid.NewGuid(),
                providerKey,
                redirectUri,
                scopes,
                command.Enabled,
                secretName,
                secretVersion,
                command.ActorUserId,
                now);
            _dbContext.FinanceIntegrationProviderConfigurations.Add(existing);
        }
        else
        {
            existing.Apply(
                redirectUri,
                scopes,
                command.Enabled,
                secretName,
                secretVersion,
                command.ActorUserId,
                now);
        }

        _dbContext.FinanceIntegrationProviderConfigurationAudits.Add(
            new FinanceIntegrationProviderConfigurationAudit(
                Guid.NewGuid(),
                providerKey,
                command.ActorUserId,
                "configuration_saved",
                "succeeded",
                $"{definition.DisplayName} application configuration was saved securely.",
                changedFields,
                command.CorrelationId,
                now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FinanceIntegrationApplicationConfigurationException(
                "The provider configuration changed while it was being saved. Reload it and try again.",
                isConflict: true);
        }
        return await GetAsync(providerKey, cancellationToken);
    }

    public async Task<FinanceIntegrationApplicationValidationResult> ValidateAsync(
        ValidateFinanceIntegrationApplicationConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved platform administrator is required.");
        }

        var definition = GetDefinition(command.ProviderKey);
        var providerKey = NormalizeProviderKey(definition.ProviderKey);
        var effective = await LoadEffectiveAsync(definition, cancellationToken);
        var checks = BuildValidationChecks(definition, effective);
        var succeeded = checks.All(check => check.Succeeded);
        var summary = succeeded
            ? $"{definition.DisplayName} is ready for company administrators to connect."
            : $"{definition.DisplayName} setup needs attention before companies can connect.";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var configuration = await _dbContext.FinanceIntegrationProviderConfigurations
            .SingleOrDefaultAsync(item => item.ProviderKey == providerKey, cancellationToken);
        if (configuration is not null)
        {
            configuration.RecordValidation(
                succeeded
                    ? FinanceIntegrationApplicationValidationStatuses.Valid
                    : FinanceIntegrationApplicationValidationStatuses.Invalid,
                summary,
                command.ActorUserId,
                now);
        }

        _dbContext.FinanceIntegrationProviderConfigurationAudits.Add(
            new FinanceIntegrationProviderConfigurationAudit(
                Guid.NewGuid(),
                providerKey,
                command.ActorUserId,
                "configuration_validated",
                succeeded ? "succeeded" : "failed",
                summary,
                [],
                command.CorrelationId,
                now));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FinanceIntegrationApplicationConfigurationException(
                "The provider configuration changed while it was being validated. Reload it and try again.",
                isConflict: true);
        }

        return new FinanceIntegrationApplicationValidationResult(
            providerKey,
            succeeded,
            summary,
            now,
            checks);
    }

    public async Task<FinanceIntegrationApplicationAuditHistory> GetAuditHistoryAsync(
        string providerKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var definition = GetDefinition(providerKey);
        var normalized = NormalizeProviderKey(definition.ProviderKey);
        var items = await _dbContext.FinanceIntegrationProviderConfigurationAudits
            .AsNoTracking()
            .Where(item => item.ProviderKey == normalized)
            .OrderByDescending(item => item.OccurredUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        return new FinanceIntegrationApplicationAuditHistory(
            normalized,
            items.Select(item => new FinanceIntegrationApplicationAuditItem(
                    item.Id,
                    item.ProviderKey,
                    item.ActorUserId,
                    item.Action,
                    item.Outcome,
                    item.Summary,
                    item.GetChangedFields(),
                    item.OccurredUtc,
                    item.CorrelationId))
                .ToList());
    }

    public async Task<FinanceIntegrationRuntimeSettings> GetRequiredAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        var definition = GetDefinition(providerKey);
        var effective = await LoadEffectiveAsync(definition, cancellationToken);
        var supportedScopes = definition.SupportedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!effective.Enabled ||
            string.IsNullOrWhiteSpace(effective.ClientId) ||
            string.IsNullOrWhiteSpace(effective.ClientSecret) ||
            !IsRedirectValid(definition, effective.RedirectUri) ||
            effective.Scopes.Count == 0 ||
            !effective.Scopes.All(supportedScopes.Contains))
        {
            throw new FinanceIntegrationApplicationUnavailableException(definition.DisplayName);
        }

        return new FinanceIntegrationRuntimeSettings(
            NormalizeProviderKey(definition.ProviderKey),
            effective.Enabled,
            effective.ClientId,
            effective.ClientSecret,
            effective.RedirectUri,
            effective.Scopes);
    }

    private async Task<EffectiveConfiguration> LoadEffectiveAsync(
        FinanceIntegrationApplicationDefinition definition,
        CancellationToken cancellationToken)
    {
        var providerKey = NormalizeProviderKey(definition.ProviderKey);
        var stored = await _dbContext.FinanceIntegrationProviderConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProviderKey == providerKey, cancellationToken);
        var section = _configuration.GetSection(definition.ConfigurationSectionName);

        if (stored is null)
        {
            return new EffectiveConfiguration(
                section.GetValue<bool>("Enabled"),
                section["ClientId"]?.Trim() ?? string.Empty,
                section["ClientSecret"]?.Trim() ?? string.Empty,
                section["RedirectUri"]?.Trim() ?? string.Empty,
                ResolveBootstrapScopes(section, definition),
                null,
                null,
                FinanceIntegrationApplicationValidationStatuses.NotChecked,
                null,
                null,
                null);
        }

        var credentialValue = string.IsNullOrWhiteSpace(stored.CredentialSecretName)
            ? null
            : await _secretStore.GetAsync(
                stored.CredentialSecretName,
                stored.CredentialSecretVersion,
                cancellationToken);
        var credentials = DeserializeCredentials(credentialValue?.Value);

        return new EffectiveConfiguration(
            stored.Enabled,
            credentials?.ClientId ?? string.Empty,
            credentials?.ClientSecret ?? string.Empty,
            stored.RedirectUri,
            stored.GetScopes(),
            stored.CredentialSecretName,
            stored.CredentialSecretVersion,
            stored.ValidationStatus,
            stored.ValidationSummary,
            stored.LastValidatedUtc,
            stored.UpdatedUtc);
    }

    private FinanceIntegrationApplicationConfigurationDto Map(
        FinanceIntegrationApplicationDefinition definition,
        EffectiveConfiguration effective)
    {
        var clientIdConfigured = !string.IsNullOrWhiteSpace(effective.ClientId);
        var clientSecretConfigured = !string.IsNullOrWhiteSpace(effective.ClientSecret);
        var redirectValid = IsRedirectValid(definition, effective.RedirectUri);
        var supportedScopes = definition.SupportedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopesValid = effective.Scopes.Count > 0 && effective.Scopes.All(supportedScopes.Contains);
        var complete = clientIdConfigured && clientSecretConfigured && redirectValid && scopesValid;
        var status = !effective.Enabled
            ? FinanceIntegrationApplicationConfigurationStatuses.Disabled
            : string.Equals(
                effective.ValidationStatus,
                FinanceIntegrationApplicationValidationStatuses.Invalid,
                StringComparison.OrdinalIgnoreCase)
                ? FinanceIntegrationApplicationConfigurationStatuses.Invalid
            : complete
                ? FinanceIntegrationApplicationConfigurationStatuses.Ready
                : FinanceIntegrationApplicationConfigurationStatuses.Incomplete;
        var message = status switch
        {
            FinanceIntegrationApplicationConfigurationStatuses.Ready =>
                $"{definition.DisplayName} is available for company administrators.",
            FinanceIntegrationApplicationConfigurationStatuses.Disabled =>
                $"{definition.DisplayName} is not currently available to companies.",
            FinanceIntegrationApplicationConfigurationStatuses.Invalid =>
                $"{definition.DisplayName} validation found a setup issue that must be corrected.",
            _ =>
                $"{definition.DisplayName} needs application credentials, a callback URL, and scopes before it can be enabled."
        };

        return new FinanceIntegrationApplicationConfigurationDto(
            NormalizeProviderKey(definition.ProviderKey),
            definition.DisplayName,
            effective.Enabled,
            status,
            message,
            effective.RedirectUri,
            effective.Scopes,
            definition.SupportedScopes,
            clientIdConfigured,
            CreateClientIdHint(effective.ClientId),
            clientSecretConfigured,
            _secretStore.BackendName,
            _secretStore.SupportsWrites,
            definition.CallbackPath,
            effective.LastValidatedUtc,
            effective.ValidationStatus,
            effective.ValidationSummary,
            effective.UpdatedUtc);
    }

    private static IReadOnlyList<FinanceIntegrationApplicationValidationCheck> BuildValidationChecks(
        FinanceIntegrationApplicationDefinition definition,
        EffectiveConfiguration effective)
    {
        var redirectValid = IsRedirectValid(definition, effective.RedirectUri);
        var supportedScopes = definition.SupportedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopesValid = effective.Scopes.Count > 0 && effective.Scopes.All(supportedScopes.Contains);

        return
        [
            new(
                "client_id",
                "Client ID",
                !string.IsNullOrWhiteSpace(effective.ClientId),
                !string.IsNullOrWhiteSpace(effective.ClientId)
                    ? "Client ID is configured."
                    : "Add the provider application's client ID."),
            new(
                "client_secret",
                "Client secret",
                !string.IsNullOrWhiteSpace(effective.ClientSecret),
                !string.IsNullOrWhiteSpace(effective.ClientSecret)
                    ? "Client secret is stored securely."
                    : "Add the provider application's client secret."),
            new(
                "callback",
                "Callback URL",
                redirectValid,
                redirectValid
                    ? "Callback URL is valid and uses the expected callback path."
                    : $"Use an absolute HTTP or HTTPS callback URL ending in {definition.CallbackPath}."),
            new(
                "scopes",
                "Permissions",
                scopesValid,
                scopesValid
                    ? "Selected permissions are supported by this provider adapter."
                    : "Select at least one supported permission.")
        ];
    }

    private FinanceIntegrationApplicationDefinition GetDefinition(string providerKey)
    {
        var normalized = NormalizeProviderKey(providerKey);
        return _definitions.TryGetValue(normalized, out var definition)
            ? definition
            : throw new FinanceIntegrationProviderNotFoundException(providerKey);
    }

    private static string BuildCredentialSecretName(string providerKey) =>
        $"finance-provider-{providerKey.Replace('_', '-')}-{CredentialSecretSuffix}";

    private static bool IsRedirectValid(
        FinanceIntegrationApplicationDefinition definition,
        string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) &&
        redirect.Scheme is "http" or "https" &&
        redirect.AbsolutePath.Equals(definition.CallbackPath, StringComparison.OrdinalIgnoreCase);

    private static OAuthApplicationCredentials? DeserializeCredentials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OAuthApplicationCredentials>(json);
        }
        catch (JsonException)
        {
            throw new FinanceIntegrationApplicationConfigurationException(
                "The stored finance-provider credential payload is invalid. Rotate the provider credentials.");
        }
    }

    private static string NormalizeRedirectUri(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Redirect URI must be an absolute HTTP or HTTPS URL.", nameof(value));
        }

        return uri.AbsoluteUri;
    }

    private static IReadOnlyCollection<string> NormalizeScopes(
        FinanceIntegrationApplicationDefinition definition,
        IReadOnlyCollection<string>? requestedScopes)
    {
        var supported = definition.SupportedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = requestedScopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Select at least one provider permission.", nameof(requestedScopes));
        }

        var unsupported = normalized.Where(scope => !supported.Contains(scope)).ToArray();
        if (unsupported.Length > 0)
        {
            throw new ArgumentException(
                $"Unsupported provider permissions: {string.Join(", ", unsupported)}.",
                nameof(requestedScopes));
        }

        return normalized;
    }

    private static IReadOnlyCollection<string> ResolveBootstrapScopes(
        IConfigurationSection section,
        FinanceIntegrationApplicationDefinition definition)
    {
        var configured = section.GetSection("Scopes").Get<string[]>();
        return configured is { Length: > 0 }
            ? configured.Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => scope.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : definition.DefaultScopes;
    }

    private static IReadOnlyCollection<string> DetermineChangedFields(
        EffectiveConfiguration current,
        bool enabled,
        string clientId,
        string? clientSecret,
        string redirectUri,
        IReadOnlyCollection<string> scopes)
    {
        var changed = new List<string>();
        if (current.Enabled != enabled) changed.Add("Availability");
        if (!string.Equals(current.ClientId, clientId, StringComparison.Ordinal)) changed.Add("Client ID");
        if (!string.IsNullOrWhiteSpace(clientSecret)) changed.Add("Client secret");
        if (!string.Equals(current.RedirectUri, redirectUri, StringComparison.OrdinalIgnoreCase)) changed.Add("Callback URL");
        if (!current.Scopes.OrderBy(value => value).SequenceEqual(scopes.OrderBy(value => value), StringComparer.OrdinalIgnoreCase))
        {
            changed.Add("Permissions");
        }

        return changed;
    }

    private static string? CreateClientIdHint(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        return clientId.Length <= 8
            ? clientId
            : $"{clientId[..4]}…{clientId[^4..]}";
    }

    private static string NormalizeProviderKey(string providerKey) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? throw new ArgumentException("Provider key is required.", nameof(providerKey))
            : providerKey.Trim().Replace('-', '_').ToLowerInvariant();

    private sealed record OAuthApplicationCredentials(string ClientId, string ClientSecret);

    private sealed record EffectiveConfiguration(
        bool Enabled,
        string ClientId,
        string ClientSecret,
        string RedirectUri,
        IReadOnlyCollection<string> Scopes,
        string? SecretName,
        string? SecretVersion,
        string ValidationStatus,
        string? ValidationSummary,
        DateTime? LastValidatedUtc,
        DateTime? UpdatedUtc);
}
