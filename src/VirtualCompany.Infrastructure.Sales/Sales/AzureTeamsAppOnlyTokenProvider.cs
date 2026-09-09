using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Infrastructure.Sales;

internal interface ITeamsCredentialTokenSource
{
    Task<AccessToken> AcquireAsync(Guid entraTenantId, CancellationToken cancellationToken);
}

internal sealed class AzureTeamsCredentialTokenSource(
    IOptions<TeamsPresenterOptions> configured,
    IPlatformSecretStore secretStore,
    IHostEnvironment environment) : ITeamsCredentialTokenSource
{
    private static readonly TokenRequestContext GraphTokenRequest = new(["https://graph.microsoft.com/.default"]);

    public async Task<AccessToken> AcquireAsync(Guid entraTenantId, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        var mode = TeamsPresenterConfigurationEvaluator.ResolveCredentialMode(options);
        var credential = mode switch
        {
            "managed_identity" => CreateManagedIdentity(options),
            "workload_identity" => CreateWorkloadIdentity(options, entraTenantId),
            "certificate" => await CreateCertificateAsync(options, entraTenantId, cancellationToken),
            "client_secret" => await CreateClientSecretAsync(options, entraTenantId, cancellationToken),
            _ => throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CredentialUnavailable,
                "A supported Teams application credential is not configured.")
        };

        try
        {
            return await credential.GetTokenAsync(GraphTokenRequest, cancellationToken);
        }
        catch (CredentialUnavailableException)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CredentialUnavailable,
                "The configured Teams application credential is unavailable.",
                retryable: false);
        }
        catch (AuthenticationFailedException)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.TokenAcquisitionFailed,
                "Microsoft Entra rejected Teams application authentication.",
                retryable: true);
        }
    }

    private static TokenCredential CreateManagedIdentity(TeamsPresenterOptions options) =>
        new ManagedIdentityCredential(string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? options.BotApplicationId
            : options.ManagedIdentityClientId.Trim());

    private static TokenCredential CreateWorkloadIdentity(TeamsPresenterOptions options, Guid tenantId) =>
        new WorkloadIdentityCredential(new WorkloadIdentityCredentialOptions
        {
            TenantId = tenantId.ToString("D"),
            ClientId = options.BotApplicationId,
            TokenFilePath = options.WorkloadIdentityTokenFile
        });

    private async Task<TokenCredential> CreateCertificateAsync(
        TeamsPresenterOptions options,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var certificateSecret = await GetRequiredSecretAsync(options.CertificateReference, cancellationToken);
        var password = string.IsNullOrWhiteSpace(options.CertificatePasswordReference)
            ? null
            : (await GetRequiredSecretAsync(options.CertificatePasswordReference, cancellationToken)).Value;
        try
        {
            var bytes = Convert.FromBase64String(certificateSecret.Value);
            var certificate = X509CertificateLoader.LoadPkcs12(
                bytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new CryptographicException("Certificate has no private key.");
            }

            return new ClientCertificateCredential(tenantId.ToString("D"), options.BotApplicationId, certificate);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CredentialUnavailable,
                "The Teams application certificate could not be loaded from the secret store.",
                retryable: false);
        }
    }

    private async Task<TokenCredential> CreateClientSecretAsync(
        TeamsPresenterOptions options,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CredentialUnavailable,
                "Client-secret Teams authentication is supported only in Development and Testing.");
        }

        var secret = await GetRequiredSecretAsync(options.ClientSecretReference, cancellationToken);
        return new ClientSecretCredential(tenantId.ToString("D"), options.BotApplicationId, secret.Value);
    }

    private async Task<PlatformSecretValue> GetRequiredSecretAsync(string reference, CancellationToken cancellationToken)
    {
        var name = ResolveSecretName(reference);
        var value = await secretStore.GetAsync(name, version: null, cancellationToken);
        return value ?? throw new TeamsIdentityException(
            TeamsIdentityFailureCodes.CredentialUnavailable,
            "The configured Teams application credential was not found in the server-side secret store.");
    }

    private static string ResolveSecretName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.CredentialUnavailable, "A Teams credential reference is required.");
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri)) return reference.Trim();
        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return !string.IsNullOrWhiteSpace(segment) ? segment : uri.Host;
    }
}

internal sealed class AzureTeamsAppOnlyTokenProvider(
    IOptions<TeamsPresenterOptions> configured,
    ITeamsCredentialTokenSource tokenSource,
    TimeProvider timeProvider,
    ILogger<AzureTeamsAppOnlyTokenProvider> logger) : ITeamsAppOnlyTokenProvider
{
    private const string GraphAudience = "00000003-0000-0000-c000-000000000000";
    private readonly ConcurrentDictionary<Guid, TeamsAppOnlyAccessToken> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<TeamsAppOnlyAccessToken> AcquireAsync(
        TeamsAppOnlyTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EntraTenantId == Guid.Empty) throw new ArgumentException("An Entra tenant ID is required.", nameof(request));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshSkew = TimeSpan.FromMinutes(configured.Value.TokenRefreshSkewMinutes);
        if (!request.ForceRefresh && cache.TryGetValue(request.EntraTenantId, out var cached) && cached.ExpiresUtc > now.Add(refreshSkew))
        {
            EnsureExactPermissions(cached.GrantedPermissions, request.RequiredPermissions);
            return cached;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow().UtcDateTime;
            if (!request.ForceRefresh && cache.TryGetValue(request.EntraTenantId, out cached) && cached.ExpiresUtc > now.Add(refreshSkew))
            {
                EnsureExactPermissions(cached.GrantedPermissions, request.RequiredPermissions);
                return cached;
            }

            var accessToken = await tokenSource.AcquireAsync(request.EntraTenantId, cancellationToken);
            var parsed = ParseTrustedGraphToken(accessToken, request.EntraTenantId);
            EnsureExactPermissions(parsed.GrantedPermissions, request.RequiredPermissions);
            cache[request.EntraTenantId] = parsed;
            logger.LogInformation(
                "Teams app-only Graph token acquired and permission claims verified; expiresUtc={ExpiresUtc}, permissionCount={PermissionCount}.",
                parsed.ExpiresUtc,
                parsed.GrantedPermissions.Count);
            return parsed;
        }
        catch (TeamsIdentityException)
        {
            cache.TryRemove(request.EntraTenantId, out _);
            throw;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static TeamsAppOnlyAccessToken ParseTrustedGraphToken(AccessToken token, Guid expectedTenantId)
    {
        try
        {
            var segments = token.Token.Split('.');
            if (segments.Length != 3) throw new JsonException();
            using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            var root = payload.RootElement;
            var tenant = Guid.Parse(root.GetProperty("tid").GetString()!);
            if (tenant != expectedTenantId)
            {
                throw new TeamsIdentityException(
                    TeamsIdentityFailureCodes.TokenTenantMismatch,
                    "The Teams application token belongs to a different Entra tenant.");
            }

            var audience = root.GetProperty("aud").GetString();
            if (!string.Equals(audience, GraphAudience, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(audience, "https://graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new TeamsIdentityException(TeamsIdentityFailureCodes.TokenInvalid, "The Teams application token has an invalid audience.");
            }

            var roles = root.TryGetProperty("roles", out var roleValues) && roleValues.ValueKind == JsonValueKind.Array
                ? roleValues.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : [];
            return new TeamsAppOnlyAccessToken(token.Token, token.ExpiresOn.UtcDateTime, tenant, roles);
        }
        catch (TeamsIdentityException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.TokenInvalid, "The Teams application token claims are invalid.");
        }
    }

    private static void EnsureExactPermissions(
        IReadOnlyCollection<string> granted,
        IReadOnlyCollection<string> required)
    {
        var missing = required.Except(granted, StringComparer.Ordinal).Any();
        if (missing)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.PermissionMissing, "Required Teams calling permissions have not been granted.");
        var excess = granted.Except(required, StringComparer.Ordinal).Any();
        if (excess)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.PermissionExcess, "The Teams application has unapproved additional Microsoft Graph permissions.");
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }
}
