using System.Text.RegularExpressions;

namespace VirtualCompany.Infrastructure.Sales;

internal static partial class TeamsPresenterConfigurationEvaluator
{
    private const string AdminConsentCallbackPath = "/api/platform/teams-presenter/admin-consent/callback";
    private const string TrustedCallbackMetadataUrl = "https://api.aps.skype.com/v1/.well-known/OpenIdConfiguration";
    private const string TrustedCallbackIssuer = "https://api.botframework.com";
    private static readonly HashSet<string> TenantModes = new(StringComparer.Ordinal)
    {
        "single_tenant",
        "multi_tenant"
    };

    private static readonly HashSet<string> MediaRoutes = new(StringComparer.Ordinal)
    {
        "disabled",
        "teams_application_hosted",
        "certified_provider"
    };

    private static readonly HashSet<string> CredentialModes = new(StringComparer.Ordinal)
    {
        "managed_identity",
        "workload_identity",
        "certificate",
        "client_secret"
    };

    public static IReadOnlyList<TeamsPresenterConfigurationIssue> Evaluate(TeamsPresenterOptions options)
    {
        var issues = new List<TeamsPresenterConfigurationIssue>();

        ValidateGuid(options.TeamsAppId, "teams_app_id_invalid", "TeamsAppId must be a non-placeholder GUID.", issues);
        ValidateGuid(options.BotApplicationId, "bot_application_id_invalid", "BotApplicationId must be a non-placeholder GUID.", issues);
        ValidateGuid(options.WebApplicationId, "web_application_id_invalid", "WebApplicationId must be a non-placeholder GUID.", issues);

        if (!PackageVersionRegex().IsMatch(options.PackageVersion ?? string.Empty))
        {
            issues.Add(new("package_version_invalid", "PackageVersion must contain exactly three numeric components, for example 1.0.0."));
        }

        if (!TenantModes.Contains(options.TenantMode ?? string.Empty))
        {
            issues.Add(new("tenant_mode_invalid", "TenantMode must be single_tenant or multi_tenant."));
        }

        var allowedTenantIds = options.AllowedTenantIds ?? [];
        if (allowedTenantIds.Any(value => !TryGuid(value)))
        {
            issues.Add(new("allowed_tenant_invalid", "Every AllowedTenantIds value must be a non-placeholder GUID."));
        }
        else if (string.Equals(options.TenantMode, "single_tenant", StringComparison.Ordinal) && allowedTenantIds.Length != 1)
        {
            issues.Add(new("single_tenant_allowlist_invalid", "Single-tenant configuration requires exactly one allowed tenant ID."));
        }

        var publicApiOrigin = ValidateOrigin(options.PublicApiOrigin, "public_api_origin_invalid", "PublicApiOrigin", issues);
        var webOrigin = ValidateOrigin(options.WebOrigin, "web_origin_invalid", "WebOrigin", issues);
        var notificationUrl = ValidateHttpsUrl(options.BotNotificationUrl, "bot_notification_url_invalid", "BotNotificationUrl", issues);
        var callingUrl = ValidateHttpsUrl(options.BotCallingCallbackUrl, "bot_calling_callback_url_invalid", "BotCallingCallbackUrl", issues);
        var consentRedirectUrl = ValidateHttpsUrl(options.AdminConsentRedirectUrl, "admin_consent_redirect_url_invalid", "AdminConsentRedirectUrl", issues);
        var configurationUrl = ValidateHttpsUrl(options.ConfigurationUrl, "configuration_url_invalid", "ConfigurationUrl", issues);
        var sidePanelUrl = ValidateHttpsUrl(options.SidePanelUrl, "side_panel_url_invalid", "SidePanelUrl", issues);
        var stageUrl = ValidateHttpsUrl(options.StageUrl, "stage_url_invalid", "StageUrl", issues);

        RequireSameOrigin(publicApiOrigin, notificationUrl, "bot_notification_origin_mismatch", "BotNotificationUrl must use the exact PublicApiOrigin.", issues);
        RequireSameOrigin(publicApiOrigin, callingUrl, "bot_calling_origin_mismatch", "BotCallingCallbackUrl must use the exact PublicApiOrigin.", issues);
        RequireSameOrigin(publicApiOrigin, consentRedirectUrl, "admin_consent_redirect_origin_mismatch", "AdminConsentRedirectUrl must use the exact PublicApiOrigin.", issues);
        RequireSameOrigin(webOrigin, configurationUrl, "configuration_origin_mismatch", "ConfigurationUrl must use the exact WebOrigin.", issues);
        RequireSameOrigin(webOrigin, sidePanelUrl, "side_panel_origin_mismatch", "SidePanelUrl must use the exact WebOrigin.", issues);
        RequireSameOrigin(webOrigin, stageUrl, "stage_origin_mismatch", "StageUrl must use the exact WebOrigin.", issues);

        if (consentRedirectUrl is not null &&
            (!string.Equals(consentRedirectUrl.AbsolutePath, AdminConsentCallbackPath, StringComparison.Ordinal) ||
             !string.IsNullOrEmpty(consentRedirectUrl.Query)))
        {
            issues.Add(new("admin_consent_redirect_path_invalid",
                $"AdminConsentRedirectUrl must use the exact {AdminConsentCallbackPath} callback path with no query."));
        }

        if (!Uri.TryCreate(options.WebApplicationResource, UriKind.Absolute, out var resource) ||
            resource.Scheme != "api" ||
            webOrigin is null ||
            !string.Equals(resource.Host, webOrigin.Host, StringComparison.OrdinalIgnoreCase) ||
            IsPlaceholder(options.WebApplicationResource))
        {
            issues.Add(new("web_application_resource_invalid", "WebApplicationResource must be an api:// URI on the configured WebOrigin host."));
        }

        if (!MediaRoutes.Contains(options.MediaRoute ?? string.Empty))
        {
            issues.Add(new("media_route_invalid", "MediaRoute must be disabled, teams_application_hosted, or certified_provider."));
        }

        var credentialMode = ResolveCredentialMode(options);
        if (options.Enabled && !CredentialModes.Contains(credentialMode))
        {
            issues.Add(new("credential_mode_invalid", "CredentialMode must be managed_identity, workload_identity, certificate, or client_secret."));
        }
        else if (options.Enabled && credentialMode == "certificate" && IsPlaceholder(options.CertificateReference))
        {
            issues.Add(new("certificate_reference_missing", "Certificate credential mode requires a server-side certificate secret reference."));
        }
        else if (options.Enabled && credentialMode == "client_secret" && IsPlaceholder(options.ClientSecretReference))
        {
            issues.Add(new("client_secret_reference_missing", "Client-secret credential mode requires a server-side secret reference."));
        }
        else if (options.Enabled && credentialMode == "workload_identity" && string.IsNullOrWhiteSpace(options.WorkloadIdentityTokenFile))
        {
            issues.Add(new("workload_identity_token_file_missing", "Workload-identity credential mode requires a federated token file path."));
        }

        if (options.ConsentStateLifetimeMinutes is < 5 or > 30)
        {
            issues.Add(new("consent_state_lifetime_invalid", "ConsentStateLifetimeMinutes must be between 5 and 30."));
        }

        if (options.TokenRefreshSkewMinutes is < 2 or > 15)
        {
            issues.Add(new("token_refresh_skew_invalid", "TokenRefreshSkewMinutes must be between 2 and 15."));
        }

        var callbackMetadata = ValidateHttpsUrl(options.CallbackOpenIdConfigurationUrl, "callback_openid_configuration_url_invalid", "CallbackOpenIdConfigurationUrl", issues);
        if (callbackMetadata is not null &&
            !string.Equals(callbackMetadata.AbsoluteUri.TrimEnd('/'), TrustedCallbackMetadataUrl, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("callback_openid_configuration_untrusted",
                "CallbackOpenIdConfigurationUrl must use the trusted Microsoft Teams calling metadata endpoint."));
        }

        if (!string.Equals(options.CallbackIssuer?.TrimEnd('/'), TrustedCallbackIssuer, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("callback_issuer_invalid", "CallbackIssuer must use the trusted Bot Framework issuer."));
        }

        var requestsCalling = options.CallControlEnabled || options.AudioEnabled || options.VisualMediaEnabled;
        if (requestsCalling && (!options.MediaRouteApproved || string.Equals(options.MediaRoute, "disabled", StringComparison.Ordinal)))
        {
            issues.Add(new("media_route_not_approved", "Calling, audio, and visual media require an explicitly approved Teams media route."));
        }

        if (options.AudioEnabled && !options.CallControlEnabled)
        {
            issues.Add(new("audio_requires_call_control", "Audio cannot be enabled without call control."));
        }

        if (options.VisualMediaEnabled && (!options.CallControlEnabled || !options.AudioEnabled))
        {
            issues.Add(new("visual_media_requires_audio", "Visual media cannot be enabled before call control and audio."));
        }

        return issues;
    }

    private static void ValidateGuid(string value, string code, string message, ICollection<TeamsPresenterConfigurationIssue> issues)
    {
        if (!TryGuid(value)) issues.Add(new(code, message));
    }

    private static bool TryGuid(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty && !IsPlaceholder(value);

    private static Uri? ValidateOrigin(string? value, string code, string label, ICollection<TeamsPresenterConfigurationIssue> issues)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            IsPlaceholder(value))
        {
            issues.Add(new(code, $"{label} must be a non-placeholder HTTPS origin with no path, query, or fragment."));
            return null;
        }

        return uri;
    }

    private static Uri? ValidateHttpsUrl(string? value, string code, string label, ICollection<TeamsPresenterConfigurationIssue> issues)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            IsPlaceholder(value))
        {
            issues.Add(new(code, $"{label} must be a non-placeholder absolute HTTPS URL with a path."));
            return null;
        }

        return uri;
    }

    private static void RequireSameOrigin(Uri? expected, Uri? actual, string code, string message, ICollection<TeamsPresenterConfigurationIssue> issues)
    {
        if (expected is null || actual is null) return;
        if (!string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase) ||
            expected.Port != actual.Port)
        {
            issues.Add(new(code, message));
        }
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains('<') ||
        value.Contains('>') ||
        value.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("example.com", StringComparison.OrdinalIgnoreCase);

    public static string ResolveCredentialMode(TeamsPresenterOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CredentialMode)) return options.CredentialMode.Trim().ToLowerInvariant();
        if (options.UseManagedIdentity) return "managed_identity";
        if (!string.IsNullOrWhiteSpace(options.CertificateReference)) return "certificate";
        return string.Empty;
    }

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersionRegex();
}

internal sealed record TeamsPresenterConfigurationIssue(string Code, string Message);
