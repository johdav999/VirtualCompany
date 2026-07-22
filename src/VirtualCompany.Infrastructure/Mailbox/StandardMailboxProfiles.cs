using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class StandardMailboxConnectionProfileRegistry : IMailboxConnectionProfileRegistry
{
    public const string ZohoEuProfileKey = "zoho-eu";
    public const string CustomProfileKey = "custom";

    private static readonly IReadOnlyList<MailboxConnectionProfile> BuiltInProfiles =
    [
        new(
            ZohoEuProfileKey,
            "Zoho Mail (Europe)",
            "Europe",
            new MailboxEndpointSettings("imappro.zoho.eu", 993, MailboxTlsMode.ImplicitTls),
            new MailboxEndpointSettings("smtppro.zoho.eu", 465, MailboxTlsMode.ImplicitTls),
            new HashSet<MailboxAuthenticationType>
            {
                MailboxAuthenticationType.ApplicationPassword
            },
            MailboxCapability.ReadMessages |
            MailboxCapability.ReadAttachments |
            MailboxCapability.ListFolders |
            MailboxCapability.CreateDrafts |
            MailboxCapability.SendMessages |
            MailboxCapability.IncrementalSync),
        new(
            CustomProfileKey,
            "Other IMAP and SMTP provider",
            "Custom",
            new MailboxEndpointSettings(string.Empty, 993, MailboxTlsMode.ImplicitTls),
            new MailboxEndpointSettings(string.Empty, 465, MailboxTlsMode.ImplicitTls),
            new HashSet<MailboxAuthenticationType>
            {
                MailboxAuthenticationType.ApplicationPassword
            },
            MailboxCapability.ReadMessages |
            MailboxCapability.ReadAttachments |
            MailboxCapability.ListFolders |
            MailboxCapability.CreateDrafts |
            MailboxCapability.SendMessages |
            MailboxCapability.IncrementalSync,
            AllowsEndpointOverride: true)
    ];

    private readonly IReadOnlyList<MailboxConnectionProfile> _profiles;

    public StandardMailboxConnectionProfileRegistry()
        : this(Options.Create(new MailboxIntegrationOptions()))
    {
    }

    public StandardMailboxConnectionProfileRegistry(IOptions<MailboxIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuredProfiles = options.Value.StandardProfiles.Select(ToProfile).ToArray();
        var allProfiles = BuiltInProfiles.Concat(configuredProfiles).ToArray();
        var duplicate = allProfiles
            .GroupBy(profile => profile.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new OptionsValidationException(
                MailboxIntegrationOptions.SectionName,
                typeof(MailboxIntegrationOptions),
                [$"Mailbox profile key '{duplicate.Key}' is registered more than once."]);
        }

        _profiles = allProfiles;
    }

    public IReadOnlyList<MailboxConnectionProfile> List() => _profiles;

    public MailboxConnectionProfile Resolve(string profileKey) =>
        _profiles.FirstOrDefault(profile => string.Equals(profile.ProfileKey, profileKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("The selected email provider profile is not available.");

    private static MailboxConnectionProfile ToProfile(MailboxIntegrationOptions.StandardProfileOptions options)
    {
        var key = options.ProfileKey.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(key, "^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant))
        {
            throw InvalidProfile(key, "ProfileKey must contain 2 to 63 lowercase letters, numbers, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(options.DisplayName) || string.IsNullOrWhiteSpace(options.Region))
        {
            throw InvalidProfile(key, "DisplayName and Region are required.");
        }

        var imap = new MailboxEndpointSettings(options.ImapHost.Trim(), options.ImapPort, options.ImapTlsMode);
        var smtp = new MailboxEndpointSettings(options.SmtpHost.Trim(), options.SmtpPort, options.SmtpTlsMode);
        ValidateConfiguredEndpoint(key, "IMAP", imap);
        ValidateConfiguredEndpoint(key, "SMTP", smtp);

        return new MailboxConnectionProfile(
            key,
            options.DisplayName.Trim(),
            options.Region.Trim(),
            imap,
            smtp,
            new HashSet<MailboxAuthenticationType> { MailboxAuthenticationType.ApplicationPassword },
            MailboxCapability.ReadMessages |
            MailboxCapability.ReadAttachments |
            MailboxCapability.ListFolders |
            MailboxCapability.CreateDrafts |
            MailboxCapability.SendMessages |
            MailboxCapability.IncrementalSync);
    }

    private static void ValidateConfiguredEndpoint(string profileKey, string protocol, MailboxEndpointSettings endpoint)
    {
        if (Uri.CheckHostName(endpoint.Host) != UriHostNameType.Dns)
        {
            throw InvalidProfile(profileKey, $"{protocol} host must be a DNS host name.");
        }

        var valid = protocol == "IMAP"
            ? endpoint.Port == 993 && endpoint.TlsMode == MailboxTlsMode.ImplicitTls
            : (endpoint.Port == 465 && endpoint.TlsMode == MailboxTlsMode.ImplicitTls) ||
              (endpoint.Port == 587 && endpoint.TlsMode == MailboxTlsMode.StartTls);
        if (!valid)
        {
            throw InvalidProfile(profileKey, $"{protocol} must use a supported secure port and TLS mode.");
        }
    }

    private static OptionsValidationException InvalidProfile(string profileKey, string failure) =>
        new(
            MailboxIntegrationOptions.SectionName,
            typeof(MailboxIntegrationOptions),
            [$"Mailbox profile '{profileKey}' is invalid. {failure}"]);
}

public sealed class MailboxIntegrationOptionsValidator : IValidateOptions<MailboxIntegrationOptions>
{
    public ValidateOptionsResult Validate(string? name, MailboxIntegrationOptions options)
    {
        try
        {
            _ = new StandardMailboxConnectionProfileRegistry(Options.Create(options));
            return ValidateOptionsResult.Success;
        }
        catch (OptionsValidationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Failures);
        }
    }
}

public sealed class SecureMailboxEndpointPolicy : IMailboxEndpointPolicy
{
    private static readonly HashSet<int> AllowedPorts = [465, 587, 993];

    public async Task<MailboxEndpointPolicyDecision> EvaluateAsync(
        MailboxEndpointSettings endpoint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            return MailboxEndpointPolicyDecision.Deny("host_required", "Enter the mail server host name.");
        }

        if (!AllowedPorts.Contains(endpoint.Port))
        {
            return MailboxEndpointPolicyDecision.Deny(
                "port_not_allowed",
                "Use secure IMAP port 993 or secure SMTP port 465 or 587.");
        }

        if (endpoint.Port == 993 && endpoint.TlsMode != MailboxTlsMode.ImplicitTls)
        {
            return MailboxEndpointPolicyDecision.Deny("imap_tls_required", "IMAP port 993 must use TLS from the start.");
        }

        if (endpoint.Port == 465 && endpoint.TlsMode != MailboxTlsMode.ImplicitTls)
        {
            return MailboxEndpointPolicyDecision.Deny("smtp_tls_required", "SMTP port 465 must use TLS from the start.");
        }

        if (endpoint.Port == 587 && endpoint.TlsMode != MailboxTlsMode.StartTls)
        {
            return MailboxEndpointPolicyDecision.Deny("smtp_starttls_required", "SMTP port 587 must use STARTTLS.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(endpoint.Host.Trim(), cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return MailboxEndpointPolicyDecision.Deny("host_not_found", "The mail server host name could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            return MailboxEndpointPolicyDecision.Deny("host_not_found", "The mail server host name did not resolve to an address.");
        }

        if (addresses.Any(IsDisallowedAddress))
        {
            return MailboxEndpointPolicyDecision.Deny(
                "private_network_not_allowed",
                "Mail servers on local or private networks cannot be connected.");
        }

        return MailboxEndpointPolicyDecision.Permit(addresses);
    }

    private static bool IsDisallowedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 0
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
    }
}

public sealed class MailboxTransportRegistry : IMailboxTransportRegistry
{
    private readonly IReadOnlyDictionary<string, IMailboxTransport> _transports;

    public MailboxTransportRegistry(IEnumerable<IMailboxTransport> transports)
    {
        _transports = transports.ToDictionary(transport => transport.TransportKey, StringComparer.OrdinalIgnoreCase);
    }

    public IMailboxTransport Resolve(string transportKey) =>
        _transports.TryGetValue(transportKey, out var transport)
            ? transport
            : throw new KeyNotFoundException("The selected email transport is not available.");
}
