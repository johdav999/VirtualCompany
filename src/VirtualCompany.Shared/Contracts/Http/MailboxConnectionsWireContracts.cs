using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Shared.Contracts.MailboxConnections;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record StartMailboxConnectionRequest(string? ReturnUri, IReadOnlyCollection<MailboxFolderSelectionRequest>? ConfiguredFolders)
{
    public string? ReturnUri { get; set; } = ReturnUri;
    public IReadOnlyCollection<MailboxFolderSelectionRequest>? ConfiguredFolders { get; set; } = ConfiguredFolders;

    public StartMailboxConnectionRequest() : this(default !, default !)
    {
    }
}

public sealed record MailboxProviderAvailabilityResponse(MailboxProviderAvailability Gmail, MailboxProviderAvailability Microsoft365, MailboxProviderAvailability HostedEmail)
{
    public MailboxProviderAvailability Gmail { get; set; } = Gmail;
    public MailboxProviderAvailability Microsoft365 { get; set; } = Microsoft365;
    public MailboxProviderAvailability HostedEmail { get; set; } = HostedEmail;

    public MailboxProviderAvailabilityResponse() : this(new() { Provider = "gmail", DisplayName = "Gmail" }, new() { Provider = "microsoft365", DisplayName = "Microsoft 365" }, new() { Provider = "standard_email", DisplayName = "Hosted email", IsConfigured = true })
    {
    }
}

public sealed record MailboxProviderAvailability(string Provider, string DisplayName, bool IsConfigured, string? UnavailableReason)
{
    public string Provider { get; set; } = Provider;
    public string DisplayName { get; set; } = DisplayName;
    public bool IsConfigured { get; set; } = IsConfigured;
    public string? UnavailableReason { get; set; } = UnavailableReason;

    public MailboxProviderAvailability() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record MailboxFolderSelectionRequest(string ProviderFolderId, string? DisplayName)
{
    public string ProviderFolderId { get; set; } = ProviderFolderId;
    public string? DisplayName { get; set; } = DisplayName;

    public MailboxFolderSelectionRequest() : this(string.Empty, default !)
    {
    }
}

public sealed record StartMailboxConnectionResponse(string AuthorizationUrl)
{
    public string AuthorizationUrl { get; set; } = AuthorizationUrl;

    public StartMailboxConnectionResponse() : this(string.Empty)
    {
    }
}

public sealed record MailboxEndpointRequest(string Host, int Port, string TlsMode)
{
    public string Host { get; set; } = Host;
    public int Port { get; set; } = Port;
    public string TlsMode { get; set; } = TlsMode;
}

public sealed record MailboxEndpointResponse(string Host, int Port, string TlsMode)
{
    public string Host { get; set; } = Host;
    public int Port { get; set; } = Port;
    public string TlsMode { get; set; } = TlsMode;

    public MailboxEndpointResponse() : this(string.Empty, default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record StandardMailboxConnectionRequest(string ProfileKey, string EmailAddress, string Username, string AuthenticationType, string? Credential, MailboxEndpointRequest? Imap, MailboxEndpointRequest? Smtp, IReadOnlyCollection<string>? SelectedFolderIds, string? TestTarget = null, string? ReturnUri = null)
{
    public string ProfileKey { get; set; } = ProfileKey;
    public string EmailAddress { get; set; } = EmailAddress;
    public string Username { get; set; } = Username;
    public string AuthenticationType { get; set; } = AuthenticationType;
    public string? Credential { get; set; } = Credential;
    public MailboxEndpointRequest? Imap { get; set; } = Imap;
    public MailboxEndpointRequest? Smtp { get; set; } = Smtp;
    public IReadOnlyCollection<string>? SelectedFolderIds { get; set; } = SelectedFolderIds;
    public string? TestTarget { get; set; } = TestTarget;
    public string? ReturnUri { get; set; } = ReturnUri;

    public StandardMailboxConnectionRequest() : this("zoho-eu", string.Empty, string.Empty, "application_password", default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record StandardMailboxProfileResponse(string ProfileKey, string DisplayName, string Region, MailboxEndpointResponse Imap, MailboxEndpointResponse Smtp, IReadOnlyList<string> AuthenticationTypes, bool AllowsEndpointOverride)
{
    public string ProfileKey { get; set; } = ProfileKey;
    public string DisplayName { get; set; } = DisplayName;
    public string Region { get; set; } = Region;
    public MailboxEndpointResponse Imap { get; set; } = Imap;
    public MailboxEndpointResponse Smtp { get; set; } = Smtp;
    public IReadOnlyList<string> AuthenticationTypes { get; set; } = AuthenticationTypes;
    public bool AllowsEndpointOverride { get; set; } = AllowsEndpointOverride;

    public StandardMailboxProfileResponse() : this(string.Empty, string.Empty, string.Empty, new(), new(), [], default !)
    {
    }
}

public sealed record StandardMailboxConnectionResponse(Guid? ConnectionId, bool IncomingSucceeded, bool SendingSucceeded, string EmailAddress, int Capabilities, IReadOnlyList<MailboxTransportFolder> Folders, string? FailureCode, string? FailureMessage, DateTime CheckedUtc)
{
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public bool IncomingSucceeded { get; set; } = IncomingSucceeded;
    public bool SendingSucceeded { get; set; } = SendingSucceeded;
    public string EmailAddress { get; set; } = EmailAddress;
    public int Capabilities { get; set; } = Capabilities;
    public IReadOnlyList<MailboxTransportFolder> Folders { get; set; } = Folders;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureMessage { get; set; } = FailureMessage;
    public DateTime CheckedUtc { get; set; } = CheckedUtc;

    public StandardMailboxConnectionResponse() : this(default !, default !, default !, string.Empty, default !, [], default !, default !, default !)
    {
    }
}
