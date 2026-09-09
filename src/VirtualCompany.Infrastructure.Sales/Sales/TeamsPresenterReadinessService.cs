using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsPresenterReadinessService(
    IOptions<TeamsPresenterOptions> configured,
    ITeamsPresenterPackageBuilder packageBuilder,
    ITeamsTenantRegistrationService registrations,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    ILogger<TeamsPresenterReadinessService> logger,
    ITeamsPresenterRolloutPolicy rolloutPolicy,
    ITeamsMeetingMediaAdapter? teamsMedia = null,
    ITeamsMediaHostRuntime? mediaHost = null) : ITeamsPresenterReadinessService
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.TeamsPresenter", "2.0.0");
    private static readonly Counter<long> Evaluations = Meter.CreateCounter<long>("sales.teams_presenter.readiness.evaluations");
    private static readonly Counter<long> StateChanges = Meter.CreateCounter<long>("sales.teams_presenter.readiness.state_changes");
    private static string? lastFingerprint;

    public async Task<TeamsPresenterReadinessDto> GetReadinessAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = configured.Value;
        var configurationIssues = TeamsPresenterConfigurationEvaluator.Evaluate(options);
        var packageIssues = packageBuilder.Validate();
        var packageReady = packageIssues.Count == 0;
        var credentialMode = TeamsPresenterConfigurationEvaluator.ResolveCredentialMode(options);
        var credentialConfigured = credentialMode is "managed_identity" or "workload_identity" ||
                                   credentialMode == "certificate" && IsConfiguredReference(options.CertificateReference) ||
                                   credentialMode == "client_secret" && IsConfiguredReference(options.ClientSecretReference);
        if (credentialMode == "client_secret" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            credentialConfigured = false;
        }
        var requestsCalling = options.CallControlEnabled || options.AudioEnabled || options.VisualMediaEnabled;
        var approvedRoute = options.MediaRouteApproved &&
                            options.MediaRoute is "teams_application_hosted" or "certified_provider";
        var registration = companyId.HasValue
            ? await registrations.GetAsync(companyId.Value, cancellationToken)
            : null;
        var rollout = companyId.HasValue
            ? await rolloutPolicy.EvaluateAsync(companyId.Value, null, registration?.EntraTenantId, cancellationToken)
            : null;
        var consentReady = registration?.ConsentStatus == TeamsTenantVerificationStates.Verified;
        var permissionsReady = registration?.PermissionStatus == TeamsTenantVerificationStates.Verified;
        var policyReady = registration?.PolicyStatus == TeamsTenantVerificationStates.Attested;
        var callbackReady = registration?.Status == TeamsTenantRegistrationStates.Ready;
        var callControlReady = options.Enabled && options.CallControlEnabled && callbackReady &&
                               configurationIssues.Count == 0 && packageReady && credentialConfigured;
        var mediaHealth = teamsMedia is null
            ? new TeamsMeetingMediaHealth(options.AudioEnabled, false, false, options.MediaRoute, "degraded",
                TeamsPresenterReadinessReasonCodes.MediaHostNotVerified)
            : await teamsMedia.GetHealthAsync(cancellationToken);
        var audioReady = callControlReady && mediaHealth.Available;
        var mediaHostStatus = mediaHost?.GetStatus();
        var mediaHostReady = !options.AudioEnabled || mediaHostStatus?.AcceptingNewCalls == true;
        var sharedStageReady = options.Enabled && options.SharedStageEnabled && configurationIssues.Count == 0 &&
                               packageReady && callbackReady;

        var checks = new List<TeamsPresenterReadinessCheckDto>
        {
            Check(TeamsPresenterReadinessCheckNames.Configuration, configurationIssues.Count == 0,
                TeamsPresenterReadinessReasonCodes.ConfigurationInvalid,
                configurationIssues.Count == 0 ? "Teams presenter configuration passed fail-closed validation." : SafeIssueSummary(configurationIssues)),
            Check(TeamsPresenterReadinessCheckNames.Identity,
                !configurationIssues.Any(issue => issue.Code.Contains("_id_", StringComparison.Ordinal)),
                TeamsPresenterReadinessReasonCodes.IdentityInvalid,
                "Teams, bot, and web application IDs must be non-placeholder GUIDs."),
            Check(TeamsPresenterReadinessCheckNames.Urls,
                !configurationIssues.Any(issue => issue.Code.Contains("url", StringComparison.Ordinal) ||
                                                  issue.Code.Contains("origin_invalid", StringComparison.Ordinal) ||
                                                  issue.Code == "web_application_resource_invalid"),
                TeamsPresenterReadinessReasonCodes.UrlInvalid,
                "All Teams presenter content, consent, metadata, and callback URLs must be exact HTTPS URLs."),
            Check(TeamsPresenterReadinessCheckNames.Domains,
                !configurationIssues.Any(issue => issue.Code.Contains("origin_mismatch", StringComparison.Ordinal)),
                TeamsPresenterReadinessReasonCodes.DomainMismatch,
                "Configured callback and content URLs must match their declared origins."),
            Check(TeamsPresenterReadinessCheckNames.MediaRoute, !requestsCalling || approvedRoute,
                TeamsPresenterReadinessReasonCodes.MediaRouteNotApproved,
                requestsCalling
                    ? "Calling capabilities require an explicitly approved Teams application-hosted or certified-provider route."
                    : "No Teams calling or media capability is requested."),
            Check(TeamsPresenterReadinessCheckNames.Credential, credentialConfigured,
                TeamsPresenterReadinessReasonCodes.CredentialMissing,
                "Configure managed identity, workload identity, a certificate reference, or a development-only client-secret reference."),
            Check(TeamsPresenterReadinessCheckNames.Package, packageReady,
                TeamsPresenterReadinessReasonCodes.PackageInvalid,
                packageReady
                    ? $"Teams manifest {TeamsPresenterPackageBuilder.ManifestVersion}, icons, domains, and package inputs are valid."
                    : SafePackageIssueSummary(packageIssues)),
            Check(TeamsPresenterReadinessCheckNames.TenantApproval, consentReady,
                registration?.FailureCode ?? TeamsPresenterReadinessReasonCodes.TenantApprovalNotVerified,
                registration is null
                    ? "Select a company with an explicit Entra tenant association."
                    : consentReady ? "Tenant administrator consent was verified." : "Tenant administrator consent is pending, rejected, or revoked."),
            Check(TeamsPresenterReadinessCheckNames.Token, permissionsReady && registration?.PermissionsVerifiedUtc is not null,
                registration?.FailureCode ?? TeamsPresenterReadinessReasonCodes.TokenNotVerified,
                permissionsReady ? "An app-only Graph token was acquired for the associated tenant." : "No current app-only token permission evidence is recorded.",
                permissionsReady ? null : 300),
            Check(TeamsPresenterReadinessCheckNames.Permissions, permissionsReady,
                registration?.FailureCode ?? TeamsPresenterReadinessReasonCodes.PermissionsNotVerified,
                permissionsReady
                    ? "The token contains exactly the approved Teams application permissions."
                    : "The required Teams application permissions are missing, excessive, revoked, or not verified."),
            Check(TeamsPresenterReadinessCheckNames.TenantPolicy, policyReady,
                TeamsPresenterReadinessReasonCodes.TenantPolicyNotVerified,
                policyReady
                    ? "A platform administrator attested the required tenant Teams policy configuration."
                    : "Tenant Teams policy configuration has not been attested or was revoked."),
            Check(TeamsPresenterReadinessCheckNames.CallbackAuthentication, callbackReady,
                TeamsPresenterReadinessReasonCodes.CallbackAuthenticationNotReady,
                callbackReady
                    ? "Signed callback identity can resolve to this enabled company registration."
                    : "Callback identity remains blocked until tenant consent, exact permissions, and policy attestation are ready."),
            Check(TeamsPresenterReadinessCheckNames.CallControl, callControlReady,
                callControlReady ? TeamsPresenterReadinessReasonCodes.Ready : TeamsPresenterReadinessReasonCodes.CallControlNotImplemented,
                callControlReady
                    ? "Durable Graph call control, authenticated callbacks, and reconciliation are ready for this tenant."
                    : "Call control requires valid configuration and a ready tenant registration."),
            Check(TeamsPresenterReadinessCheckNames.SharedStage, sharedStageReady,
                TeamsPresenterReadinessReasonCodes.SharedStageNotImplemented,
                sharedStageReady
                    ? "The Teams-hosted Blazor stage and private side panel are enabled for the verified tenant."
                    : "The shared stage requires its feature gate, package, configuration, and tenant callback identity."),
            Check(TeamsPresenterReadinessCheckNames.MediaHost, mediaHostReady,
                mediaHostStatus?.State == TeamsMediaHostStates.Draining
                    ? TeamsPresenterReadinessReasonCodes.MediaHostDraining
                    : mediaHostStatus?.Checks.FirstOrDefault(check => !check.Ready)?.ReasonCode ??
                      mediaHealth.ReasonCode ?? TeamsPresenterReadinessReasonCodes.MediaHostNotVerified,
                mediaHostReady
                    ? $"The approved Azure media host is accepting calls with SDK {mediaHostStatus?.MediaSdkVersion ?? mediaHealth.SdkVersion ?? "unknown"}."
                    : "The Azure media host is disabled, draining, at capacity, or failed a runtime prerequisite."),
            Check(TeamsPresenterReadinessCheckNames.Rollout, rollout?.Allowed == true,
                rollout?.ReasonCode ?? TeamsPresenterReadinessReasonCodes.ProductionDisabled,
                rollout?.Message ?? "Select a company to evaluate its controlled rollout gates.")
        };

        var configuredCapabilities = new TeamsPresenterCapabilityStateDto(
            options.CallControlEnabled, options.AudioEnabled, options.SharedStageEnabled, options.VisualMediaEnabled);
        var liveCallingReady = callControlReady && (!options.AudioEnabled || audioReady && mediaHostReady) &&
                               (rollout?.Allowed ?? false);
        var result = new TeamsPresenterReadinessDto(
            timeProvider.GetUtcNow().UtcDateTime,
            options.Enabled,
            packageReady,
            LiveCallingReady: liveCallingReady,
            Normalize(options.MediaRoute, "disabled"),
            Normalize(options.PackageVersion, "invalid"),
            Normalize(options.TenantMode, "invalid"),
            configuredCapabilities,
            new TeamsPresenterCapabilityStateDto(callControlReady, audioReady && mediaHostReady, sharedStageReady,
                options.VisualMediaEnabled && audioReady && mediaHostReady),
            checks,
            registration,
            rollout);

        Evaluations.Add(1, new KeyValuePair<string, object?>("enabled", options.Enabled));
        RecordStateChange(result);
        return result;
    }

    private void RecordStateChange(TeamsPresenterReadinessDto readiness)
    {
        var fingerprint = string.Join('|', readiness.Enabled, readiness.PackageReady, readiness.LiveCallingReady,
            readiness.MediaRoute, readiness.TenantRegistration?.Status ?? "none",
            string.Join(',', readiness.Checks.Where(check => !check.Ready).Select(check => check.ReasonCode)));
        var previous = Interlocked.Exchange(ref lastFingerprint, fingerprint);
        if (string.Equals(previous, fingerprint, StringComparison.Ordinal)) return;
        StateChanges.Add(1,
            new KeyValuePair<string, object?>("enabled", readiness.Enabled),
            new KeyValuePair<string, object?>("package_ready", readiness.PackageReady),
            new KeyValuePair<string, object?>("live_calling_ready", readiness.LiveCallingReady));
        logger.LogInformation(
            "Teams presenter readiness changed: enabled={Enabled}, packageReady={PackageReady}, liveCallingReady={LiveCallingReady}, route={MediaRoute}, registrationState={RegistrationState}, blockingChecks={BlockingChecks}.",
            readiness.Enabled, readiness.PackageReady, readiness.LiveCallingReady, readiness.MediaRoute,
            readiness.TenantRegistration?.Status ?? "none",
            string.Join(',', readiness.Checks.Where(check => !check.Ready).Select(check => check.Name)));
    }

    private static TeamsPresenterReadinessCheckDto Check(
        string name, bool ready, string failureCode, string message, int? retryAfterSeconds = null) =>
        new(name, ready, ready ? "ready" : "blocked", ready ? TeamsPresenterReadinessReasonCodes.Ready : failureCode, message,
            ready ? null : retryAfterSeconds);

    private static TeamsPresenterReadinessCheckDto Pending(string name, string reasonCode, string message) =>
        new(name, false, "not_verified", reasonCode, message);

    private static string SafeIssueSummary(IReadOnlyList<TeamsPresenterConfigurationIssue> issues) =>
        string.Join(' ', issues.Take(3).Select(issue => issue.Message));

    private static string SafePackageIssueSummary(IReadOnlyList<TeamsPresenterPackageValidationIssue> issues) =>
        string.Join(' ', issues.Take(3).Select(issue => issue.Message));

    private static bool IsConfiguredReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('<') && !value.Contains('>') &&
        !value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
