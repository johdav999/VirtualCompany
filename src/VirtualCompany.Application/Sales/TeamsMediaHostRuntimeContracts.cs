namespace VirtualCompany.Application.Sales;

public static class TeamsMediaHostStates
{
    public const string Disabled = "disabled";
    public const string Starting = "starting";
    public const string Accepting = "accepting";
    public const string Draining = "draining";
    public const string Stopped = "stopped";
}

public static class TeamsMediaHostProblemCodes
{
    public const string Disabled = "teams_media_host.disabled";
    public const string DeploymentNotApproved = "teams_media_host.deployment_not_approved";
    public const string UnsupportedOperatingSystem = "teams_media_host.unsupported_operating_system";
    public const string UnsupportedArchitecture = "teams_media_host.unsupported_architecture";
    public const string SdkOutdated = "teams_media_host.sdk_outdated";
    public const string CertificateMissing = "teams_media_host.certificate_missing";
    public const string CertificateExpired = "teams_media_host.certificate_expired";
    public const string CertificatePrivateKeyMissing = "teams_media_host.certificate_private_key_missing";
    public const string PublicEndpointInvalid = "teams_media_host.public_endpoint_invalid";
    public const string PortMappingInvalid = "teams_media_host.port_mapping_invalid";
    public const string InstanceIdentityInvalid = "teams_media_host.instance_identity_invalid";
    public const string Draining = "teams_media_host.draining";
    public const string CapacityReached = "teams_media_host.capacity_reached";
    public const string ScaleInProtectionFailed = "teams_media_host.scale_in_protection_failed";
    public const string Ready = "teams_media_host.ready";
}

public sealed record TeamsMediaHostValidationCheck(
    string Name,
    bool Ready,
    string ReasonCode,
    string Message);

public sealed record TeamsMediaHostRuntimeStatus(
    string HostInstanceId,
    string State,
    bool AcceptingNewCalls,
    int ActiveCalls,
    int MaximumActiveCalls,
    DateTime StartedUtc,
    DateTime? DrainStartedUtc,
    DateTime? DrainDeadlineUtc,
    string MediaSdkVersion,
    DateTime MediaSdkPublishedUtc,
    IReadOnlyList<TeamsMediaHostValidationCheck> Checks);

public sealed record TeamsMediaHostDrainRequest(int? DeadlineMinutes = null, string? Reason = null);
public sealed record TeamsMediaHostDrainResult(TeamsMediaHostRuntimeStatus Status, bool AlreadyDraining);

public interface ITeamsMediaHostRuntime
{
    TeamsMediaHostRuntimeStatus GetStatus();
    Task ReserveCallAsync(Guid callId, CancellationToken cancellationToken);
    Task ReleaseCallAsync(Guid callId, CancellationToken cancellationToken);
    Task<TeamsMediaHostDrainResult> BeginDrainAsync(
        TimeSpan? deadline,
        string reason,
        CancellationToken cancellationToken);
}

public interface ITeamsMediaHostDrainExecutor
{
    Task ForceTerminateOwnedCallsAsync(string reason, CancellationToken cancellationToken);
    Task ForceTerminateCallAsync(Guid companyId, Guid callId, string reason, CancellationToken cancellationToken);
}
