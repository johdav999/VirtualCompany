using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal static class TeamsMediaSdkCompatibility
{
    public const string PackageVersion = "1.2.0.17950";
    public static readonly DateTime PublishedUtc = new(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
}

internal interface ITeamsVmssInstanceProtection
{
    Task SetAsync(bool protect, CancellationToken cancellationToken);
}

internal interface ITeamsMediaHostPrerequisiteProbe
{
    string MediaSdkVersion { get; }
    IReadOnlyList<TeamsMediaHostValidationCheck> Validate(TeamsPresenterOptions options, DateTime nowUtc);
}

internal sealed class TeamsMediaHostPrerequisiteProbe : ITeamsMediaHostPrerequisiteProbe
{
    public string MediaSdkVersion => TeamsMediaHostRuntime.DeployedMediaSdkVersion();
    public IReadOnlyList<TeamsMediaHostValidationCheck> Validate(TeamsPresenterOptions options, DateTime nowUtc) =>
        TeamsMediaHostRuntime.ValidateHost(options, nowUtc);
}

internal sealed class AzureTeamsVmssInstanceProtection(
    IHttpClientFactory clients,
    IOptions<TeamsPresenterOptions> configured) : ITeamsVmssInstanceProtection
{
    internal const string ClientName = "teams-media-vmss-management";

    public async Task SetAsync(bool protect, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (!options.VmssInstanceProtectionEnabled) return;
        if (!Guid.TryParse(options.VmssSubscriptionId, out _) ||
            string.IsNullOrWhiteSpace(options.VmssResourceGroup) ||
            string.IsNullOrWhiteSpace(options.VmssName) ||
            string.IsNullOrWhiteSpace(options.VmssInstanceId))
            throw new TeamsMeetingMediaException(TeamsMediaHostProblemCodes.ScaleInProtectionFailed,
                "The VM scale-set identity required for call protection is incomplete.");

        var credential = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = options.ManagedIdentityClientId
            });
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]), cancellationToken);
        var resourceGroup = Uri.EscapeDataString(options.VmssResourceGroup);
        var scaleSet = Uri.EscapeDataString(options.VmssName);
        var instance = Uri.EscapeDataString(options.VmssInstanceId);
        var uri = $"subscriptions/{options.VmssSubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachineScaleSets/{scaleSet}/virtualMachines/{instance}?api-version=2024-07-01";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            properties = new
            {
                protectionPolicy = new
                {
                    protectFromScaleIn = protect,
                    protectFromScaleSetActions = protect
                }
            }
        }), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TeamsMeetingMediaException(TeamsMediaHostProblemCodes.ScaleInProtectionFailed,
                "Azure could not update scale-in protection for the pinned media host.");
    }
}

internal sealed class TeamsMediaHostRuntime(
    IOptions<TeamsPresenterOptions> configured,
    ITeamsVmssInstanceProtection protection,
    ITeamsMediaHostPrerequisiteProbe prerequisites,
    TimeProvider timeProvider,
    ILogger<TeamsMediaHostRuntime> logger) : ITeamsMediaHostRuntime
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.TeamsMediaHost", "1.0.0");
    private static readonly Counter<long> Admissions = Meter.CreateCounter<long>("teams.media_host.admissions");
    private static readonly Counter<long> AdmissionRejections = Meter.CreateCounter<long>("teams.media_host.admission_rejections");
    private static readonly Counter<long> Drains = Meter.CreateCounter<long>("teams.media_host.drains");
    private static readonly UpDownCounter<long> ActiveCalls = Meter.CreateUpDownCounter<long>("teams.media_host.active_calls");
    private readonly ConcurrentDictionary<Guid, byte> activeCalls = new();
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly DateTime startedUtc = timeProvider.GetUtcNow().UtcDateTime;
    private volatile string state = TeamsMediaHostStates.Starting;
    private DateTime? drainStartedUtc;
    private DateTime? drainDeadlineUtc;
    private string? lastValidationFingerprint;

    public TeamsMediaHostRuntimeStatus GetStatus()
    {
        var options = configured.Value;
        var checks = prerequisites.Validate(options, timeProvider.GetUtcNow().UtcDateTime);
        var fingerprint = string.Join(',', checks.Where(check => !check.Ready).Select(check => check.ReasonCode));
        var previousFingerprint = Interlocked.Exchange(ref lastValidationFingerprint, fingerprint);
        if (!string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal) && fingerprint.Length > 0)
            logger.LogWarning("Teams media host runtime validation is fail-closed. Blocking reason codes: {BlockingReasons}.", fingerprint);
        var enabled = options.Enabled && options.AudioEnabled &&
                      string.Equals(options.MediaRoute, "teams_application_hosted", StringComparison.Ordinal);
        var ready = enabled && checks.All(check => check.Ready);
        var currentState = state;
        if (!enabled) currentState = TeamsMediaHostStates.Disabled;
        else if (currentState == TeamsMediaHostStates.Starting && ready) currentState = TeamsMediaHostStates.Accepting;
        var accepting = currentState == TeamsMediaHostStates.Accepting &&
                        activeCalls.Count < options.MaxActiveCallsPerHost;
        return new(options.CallControlHostId, currentState, accepting, activeCalls.Count,
            options.MaxActiveCallsPerHost, startedUtc, drainStartedUtc, drainDeadlineUtc,
            prerequisites.MediaSdkVersion,
            TeamsMediaSdkCompatibility.PublishedUtc, checks);
    }

    public async Task ReserveCallAsync(Guid callId, CancellationToken cancellationToken)
    {
        if (callId == Guid.Empty) throw new ArgumentException("A call identifier is required.", nameof(callId));
        await transition.WaitAsync(cancellationToken);
        try
        {
            if (activeCalls.ContainsKey(callId)) return;
            var status = GetStatus();
            if (!status.AcceptingNewCalls)
            {
                var reason = status.State == TeamsMediaHostStates.Draining
                    ? TeamsMediaHostProblemCodes.Draining
                    : status.ActiveCalls >= status.MaximumActiveCalls
                        ? TeamsMediaHostProblemCodes.CapacityReached
                        : status.Checks.FirstOrDefault(check => !check.Ready)?.ReasonCode ?? TeamsMediaHostProblemCodes.Disabled;
                AdmissionRejections.Add(1, new KeyValuePair<string, object?>("reason", reason));
                throw new TeamsMeetingMediaException(reason,
                    "This Teams media host is not accepting new calls; typed meeting controls remain available.");
            }
            if (activeCalls.IsEmpty) await protection.SetAsync(true, cancellationToken);
            if (!activeCalls.TryAdd(callId, 0)) return;
            state = TeamsMediaHostStates.Accepting;
            Admissions.Add(1);
            ActiveCalls.Add(1);
        }
        finally { transition.Release(); }
    }

    public async Task ReleaseCallAsync(Guid callId, CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            if (!activeCalls.TryRemove(callId, out _)) return;
            ActiveCalls.Add(-1);
            if (activeCalls.IsEmpty)
            {
                try { await protection.SetAsync(false, cancellationToken); }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to remove VM scale-set protection after the final media call ended.");
                }
            }
        }
        finally { transition.Release(); }
    }

    public async Task<TeamsMediaHostDrainResult> BeginDrainAsync(
        TimeSpan? deadline,
        string reason,
        CancellationToken cancellationToken)
    {
        await transition.WaitAsync(cancellationToken);
        try
        {
            var alreadyDraining = state == TeamsMediaHostStates.Draining;
            if (!alreadyDraining)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var configuredDeadline = deadline ?? TimeSpan.FromMinutes(configured.Value.MediaHostDrainMinutes);
                configuredDeadline = TimeSpan.FromSeconds(Math.Clamp(configuredDeadline.TotalSeconds, 30, 7200));
                drainStartedUtc = now;
                drainDeadlineUtc = now.Add(configuredDeadline);
                state = TeamsMediaHostStates.Draining;
                Drains.Add(1, new KeyValuePair<string, object?>("reason", SafeReason(reason)));
                logger.LogWarning("Teams media host {HostId} started draining {ActiveCalls} active calls until {DeadlineUtc}. Reason={Reason}.",
                    configured.Value.CallControlHostId, activeCalls.Count, drainDeadlineUtc, SafeReason(reason));
            }
            return new(GetStatus(), alreadyDraining);
        }
        finally { transition.Release(); }
    }

    internal static IReadOnlyList<TeamsMediaHostValidationCheck> ValidateHost(TeamsPresenterOptions options, DateTime nowUtc)
    {
        var checks = new List<TeamsMediaHostValidationCheck>();
        Add("deployment_approval", options.MediaHostDeploymentApproved,
            TeamsMediaHostProblemCodes.DeploymentNotApproved, "The approved Azure media-host deployment gate is required.", checks);
        Add("topology", string.Equals(options.MediaHostTopology, "azure_vmss_windows", StringComparison.Ordinal),
            TeamsMediaHostProblemCodes.DeploymentNotApproved, "The supported topology is an Azure Windows Server VM scale set.", checks);
        Add("operating_system", OperatingSystem.IsWindows(), TeamsMediaHostProblemCodes.UnsupportedOperatingSystem,
            "Application-hosted Teams media requires Windows Server.", checks);
        Add("architecture", Environment.Is64BitProcess, TeamsMediaHostProblemCodes.UnsupportedArchitecture,
            "Application-hosted Teams media requires a 64-bit process.", checks);
        Add("media_sdk_version", string.Equals(DeployedMediaSdkVersion(), TeamsMediaSdkCompatibility.PackageVersion,
                StringComparison.Ordinal),
            TeamsMediaHostProblemCodes.SdkOutdated, "The deployed media assembly must match the repository-pinned supported package.", checks);
        Add("media_sdk_age", nowUtc <= TeamsMediaSdkCompatibility.PublishedUtc.AddDays(options.MediaSdkMaximumAgeDays),
            TeamsMediaHostProblemCodes.SdkOutdated, "The Teams media SDK release is outside the supported freshness window.", checks);
        Add("instance_identity", !string.IsNullOrWhiteSpace(options.CallControlHostId) &&
             options.CallControlHostId is not ("unassigned" or "pending"), TeamsMediaHostProblemCodes.InstanceIdentityInvalid,
            "A unique VM scale-set instance identity is required.", checks);
        var publicIpReady = IPAddress.TryParse(options.MediaHostPublicIp, out var address) &&
                            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IsPrivate(address);
        Add("public_ip", publicIpReady, TeamsMediaHostProblemCodes.PublicEndpointInvalid,
            "A directly reachable public IPv4 address is required for each media instance.", checks);
        Add("public_reachability", options.MediaHostPublicReachabilityApproved,
            TeamsMediaHostProblemCodes.PublicEndpointInvalid,
            "External callback and instance media-port reachability must be verified before admission.", checks);
        Add("service_fqdn", Uri.CheckHostName(options.MediaHostServiceFqdn) == UriHostNameType.Dns &&
             options.MediaHostServiceFqdn.Contains('.'), TeamsMediaHostProblemCodes.PublicEndpointInvalid,
            "A public media service FQDN is required.", checks);
        Add("port_mapping", options.MediaHostInternalPort is >= 1 and <= 65535 &&
             options.MediaHostPublicPort is >= 1 and <= 65535, TeamsMediaHostProblemCodes.PortMappingInvalid,
            "The media instance requires valid public and internal port mappings.", checks);
        checks.Add(ValidateCertificate(options.MediaCertificateThumbprint, nowUtc));
        return checks;
    }

    private static TeamsMediaHostValidationCheck ValidateCertificate(string thumbprint, DateTime nowUtc)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(thumbprint))
            return new("certificate", false, TeamsMediaHostProblemCodes.CertificateMissing,
                "A LocalMachine certificate thumbprint is required.");
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint,
                thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal), validOnly: false);
            var certificate = certificates.OfType<X509Certificate2>().OrderByDescending(value => value.NotAfter).FirstOrDefault();
            if (certificate is null)
                return new("certificate", false, TeamsMediaHostProblemCodes.CertificateMissing,
                    "The configured media certificate is not installed in LocalMachine/My.");
            if (certificate.NotBefore.ToUniversalTime() > nowUtc || certificate.NotAfter.ToUniversalTime() <= nowUtc.AddDays(7))
                return new("certificate", false, TeamsMediaHostProblemCodes.CertificateExpired,
                    "The media certificate is not valid for at least seven more days.");
            return new("certificate", certificate.HasPrivateKey,
                certificate.HasPrivateKey ? TeamsMediaHostProblemCodes.Ready : TeamsMediaHostProblemCodes.CertificatePrivateKeyMissing,
                certificate.HasPrivateKey ? "The media certificate and private key are available." : "The media certificate private key is unavailable.");
        }
        catch (Exception)
        {
            return new("certificate", false, TeamsMediaHostProblemCodes.CertificateMissing,
                "The media certificate store could not be read.");
        }
    }

    private static bool IsPrivate(IPAddress value)
    {
        var bytes = value.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] >= 224;
    }

    internal static string DeployedMediaSdkVersion()
    {
        try
        {
            return Assembly.Load(new AssemblyName("Microsoft.Graph.Communications.Calls.Media"))
                .GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception) { return "unknown"; }
    }

    private static void Add(string name, bool ready, string reasonCode, string message,
        ICollection<TeamsMediaHostValidationCheck> checks) =>
        checks.Add(new(name, ready, ready ? TeamsMediaHostProblemCodes.Ready : reasonCode, message));

    private static string SafeReason(string value) => string.IsNullOrWhiteSpace(value)
        ? "operator_requested"
        : new string(value.Trim().Take(80).Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').ToArray());
}

internal sealed class TeamsMediaHostDrainExecutor(
    VirtualCompanyDbContext db,
    ICompanyOutboxEnqueuer outbox,
    IAuditEventWriter audit,
    IOptions<TeamsPresenterOptions> configured,
    TimeProvider timeProvider) : ITeamsMediaHostDrainExecutor
{
    public async Task ForceTerminateOwnedCallsAsync(string reason, CancellationToken cancellationToken)
    {
        var hostId = configured.Value.CallControlHostId;
        var calls = await db.TeamsMeetingCalls.IgnoreQueryFilters()
            .Where(call => call.MediaHostInstanceId == hostId &&
                           (call.State == TeamsMeetingCallStates.Requested ||
                            call.State == TeamsMeetingCallStates.Joining ||
                            call.State == TeamsMeetingCallStates.WaitingInLobby ||
                            call.State == TeamsMeetingCallStates.Admitted ||
                            call.State == TeamsMeetingCallStates.Connected ||
                            call.State == TeamsMeetingCallStates.LeaveRequested ||
                            call.State == TeamsMeetingCallStates.ReconciliationRequired))
            .ToListAsync(cancellationToken);
        foreach (var call in calls)
            await RequestTerminationAsync(call, reason, hostId, cancellationToken);
        if (calls.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ForceTerminateCallAsync(Guid companyId, Guid callId, string reason, CancellationToken cancellationToken)
    {
        var hostId = configured.Value.CallControlHostId;
        var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(value =>
            value.CompanyId == companyId && value.Id == callId && value.MediaHostInstanceId == hostId,
            cancellationToken);
        if (call is null || !TeamsMeetingCallStates.IsActive(call.State) || call.Action == "terminate") return;
        await RequestTerminationAsync(call, reason, hostId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RequestTerminationAsync(TeamsMeetingCall call, string reason, string hostId,
        CancellationToken cancellationToken)
    {
        if (call.Action == "terminate") return;
        var key = call.RequestLeave(timeProvider.GetUtcNow().UtcDateTime, forced: true);
        outbox.Enqueue(call.CompanyId, CompanyOutboxTopics.TeamsCallControlRequested,
            new TeamsCallControlWorkItem(call.CompanyId, call.Id, call.ActionVersion, "terminate", null),
            idempotencyKey: key, messageType: nameof(TeamsCallControlWorkItem));
        await audit.WriteAsync(new AuditEventWriteRequest(call.CompanyId, "system", null,
            "sales.teams_media_host.forced_termination_requested", "teams_meeting_call", call.Id.ToString("D"),
            AuditEventOutcomes.Started, "The media host requested safe provider termination.",
            Metadata: new Dictionary<string, string?> { ["reason"] = reason, ["host"] = hostId }), cancellationToken);
    }
}

internal sealed class TeamsMediaHostLifecycleService(
    ITeamsMediaHostRuntime runtime,
    ITeamsMeetingMediaCoordinator coordinator,
    IServiceScopeFactory scopes,
    IOptions<TeamsPresenterOptions> configured,
    TimeProvider timeProvider,
    IHttpClientFactory clients,
    ILogger<TeamsMediaHostLifecycleService> logger) : BackgroundService
{
    private bool deadlineHandled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = runtime.GetStatus();
                if (status.State != TeamsMediaHostStates.Disabled)
                {
                    await ObserveScheduledEventsAsync(stoppingToken);
                    await coordinator.ReconcileHostAsync(status.HostInstanceId, stoppingToken);
                    status = runtime.GetStatus();
                    if (!deadlineHandled && status.State == TeamsMediaHostStates.Draining &&
                        status.DrainDeadlineUtc <= timeProvider.GetUtcNow().UtcDateTime)
                    {
                        deadlineHandled = true;
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<ITeamsMediaHostDrainExecutor>()
                            .ForceTerminateOwnedCallsAsync("drain_deadline_elapsed", stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Teams media-host lifecycle reconciliation failed safely.");
            }
            await Task.Delay(TimeSpan.FromSeconds(configured.Value.MediaHostReconciliationSeconds), stoppingToken);
        }
    }

    private async Task ObserveScheduledEventsAsync(CancellationToken cancellationToken)
    {
        if (!configured.Value.AzureScheduledEventsEnabled ||
            runtime.GetStatus().State == TeamsMediaHostStates.Draining) return;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "metadata/scheduledevents?api-version=2020-07-01");
        request.Headers.Add("Metadata", "true");
        using var response = await clients.CreateClient("azure-instance-metadata").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("Events", out var events)) return;
        var eventType = events.EnumerateArray().Select(value =>
                value.TryGetProperty("EventType", out var type) ? type.GetString() : null)
            .FirstOrDefault(value => value is "Preempt" or "Terminate" or "Reboot" or "Redeploy" or "Freeze");
        if (eventType is not null)
            await runtime.BeginDrainAsync(null, $"azure_{eventType.ToLowerInvariant()}", cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var status = runtime.GetStatus();
        if (status.State is not (TeamsMediaHostStates.Disabled or TeamsMediaHostStates.Stopped))
        {
            await runtime.BeginDrainAsync(TimeSpan.FromSeconds(30), "application_stopping", cancellationToken);
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ITeamsMediaHostDrainExecutor>()
                .ForceTerminateOwnedCallsAsync("application_stopping", cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
