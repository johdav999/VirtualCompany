using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class StandardMailboxConnectionService : IStandardMailboxConnectionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly IMailboxConnectionProfileRegistry _profiles;
    private readonly IMailboxTransportRegistry _transports;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly IConnectedMailboxInboxScanJobScheduler? _scanScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StandardMailboxConnectionService> _logger;
    private readonly IAuditEventWriter? _audit;

    public StandardMailboxConnectionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContext,
        IMailboxConnectionProfileRegistry profiles,
        IMailboxTransportRegistry transports,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider,
        ILogger<StandardMailboxConnectionService> logger,
        IConnectedMailboxInboxScanJobScheduler? scanScheduler = null,
        IAuditEventWriter? audit = null)
    {
        _dbContext = dbContext;
        _companyContext = companyContext;
        _profiles = profiles;
        _transports = transports;
        _fieldEncryption = fieldEncryption;
        _scanScheduler = scanScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
        _audit = audit;
    }

    public IReadOnlyList<MailboxConnectionProfile> ListProfiles() => _profiles.List();

    public Task<StandardMailboxConnectionResult> TestAsync(
        TestStandardMailboxConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId, command.UserId);
        var prepared = Prepare(command.CompanyId, Guid.NewGuid(), command.Connection);
        return TestAndAuditAsync(command, prepared, cancellationToken);
    }

    private async Task<StandardMailboxConnectionResult> TestAndAuditAsync(
        TestStandardMailboxConnectionCommand command,
        PreparedConnection prepared,
        CancellationToken cancellationToken)
    {
        var result = await TestCoreAsync(null, prepared, command.Target, cancellationToken);
        await WriteAuditAsync(
            command.CompanyId,
            command.UserId,
            "mailbox.connection.tested",
            command.CompanyId.ToString("D"),
            result.IncomingSucceeded || result.SendingSucceeded ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            result.IncomingSucceeded && result.SendingSucceeded
                ? "Hosted mailbox access was tested successfully."
                : "One or more hosted mailbox access tests did not succeed.",
            new Dictionary<string, string?>
            {
                ["purpose"] = command.Purpose.ToStorageValue(),
                ["profile"] = prepared.Profile.ProfileKey,
                ["target"] = command.Target.ToString().ToLowerInvariant(),
                ["incomingSucceeded"] = result.IncomingSucceeded.ToString(),
                ["sendingSucceeded"] = result.SendingSucceeded.ToString()
            },
            cancellationToken);
        return result;
    }

    public async Task<StandardMailboxConnectionResult> SaveAsync(
        SaveStandardMailboxConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId, command.UserId);
        MailboxPurposeValues.EnsureSupported(command.Purpose, nameof(command.Purpose));
        if (string.IsNullOrWhiteSpace(command.Connection.Credential))
        {
            throw new ArgumentException("Enter an application password before saving the mailbox.", nameof(command));
        }

        var existing = await _dbContext.MailboxConnections
            .Where(connection => connection.CompanyId == command.CompanyId &&
                connection.Purpose == command.Purpose &&
                connection.Provider == MailboxProvider.StandardEmail &&
                connection.EmailAddress == command.Connection.EmailAddress.Trim().ToLowerInvariant())
            .FirstOrDefaultAsync(cancellationToken);
        var connection = existing ?? new MailboxConnection(
            Guid.NewGuid(),
            command.CompanyId,
            command.UserId,
            MailboxProvider.StandardEmail,
            command.Connection.EmailAddress,
            purpose: command.Purpose);
        var prepared = Prepare(command.CompanyId, connection.Id, command.Connection);
        var testResult = await TestCoreAsync(connection.Id, prepared, StandardMailboxTestTarget.Both, cancellationToken);
        if (!testResult.IncomingSucceeded || !testResult.SendingSucceeded)
        {
            return testResult;
        }

        var otherPrimaryConnections = await _dbContext.MailboxConnections
            .Where(item => item.CompanyId == command.CompanyId &&
                item.Purpose == command.Purpose &&
                item.Id != connection.Id &&
                item.IsPrimaryInbound)
            .ToArrayAsync(cancellationToken);
        foreach (var otherConnection in otherPrimaryConnections)
        {
            otherConnection.SetPrimaryInbound(false);
        }

        connection.SetPrimaryInbound(true);
        connection.ConfigureStandardConnection(
            prepared.Profile.ProfileKey,
            prepared.Input.AuthenticationType,
            prepared.Input.Username,
            prepared.Settings.Imap.Host,
            prepared.Settings.Imap.Port,
            prepared.Settings.Imap.TlsMode,
            prepared.Settings.Smtp.Host,
            prepared.Settings.Smtp.Port,
            prepared.Settings.Smtp.TlsMode,
            testResult.Capabilities);
        connection.ConfigureFolders(testResult.Folders
            .Where(folder => folder.CanRead &&
                (command.SelectedFolderIds?.Contains(folder.FolderId, StringComparer.OrdinalIgnoreCase) ?? folder.IsInbox))
            .Select(folder => new MailboxFolderSelection(folder.FolderId, folder.DisplayName))
            .ToArray());
        connection.StoreEncryptedCredentials(
            null,
            null,
            null,
            [],
            _fieldEncryption.Encrypt(
                command.CompanyId,
                StandardMailboxCredentialPurposes.ApplicationPassword(connection.Id),
                command.Connection.Credential));
        connection.RecordHealthCheck(_timeProvider.GetUtcNow().UtcDateTime, true);
        connection.SetStatus(MailboxConnectionStatus.Active);

        if (existing is null)
        {
            _dbContext.MailboxConnections.Add(connection);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(
            command.CompanyId,
            command.UserId,
            existing is null ? "mailbox.connection.connected" : "mailbox.credential.replaced",
            connection.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            existing is null ? "Hosted mailbox connected." : "Hosted mailbox credential replaced.",
            new Dictionary<string, string?>
            {
                ["purpose"] = connection.Purpose.ToStorageValue(),
                ["provider"] = connection.Provider.ToStorageValue(),
                ["profile"] = connection.ProfileKey,
                ["authenticationType"] = connection.AuthenticationType?.ToStorageValue()
            },
            cancellationToken);
        if (_scanScheduler is not null)
        {
            await _scanScheduler.EnqueueConnectedMailboxScanAsync(
                new ConnectedMailboxInboxScanJob(
                    connection.CompanyId,
                    connection.UserId,
                    connection.Id,
                    connection.Provider,
                    connection.Purpose),
                cancellationToken);
        }

        _logger.LogInformation(
            "Standard mailbox connected. CompanyId: {CompanyId}. Purpose: {Purpose}. ConnectionId: {ConnectionId}. ProfileKey: {ProfileKey}.",
            command.CompanyId,
            command.Purpose,
            connection.Id,
            prepared.Profile.ProfileKey);
        return testResult with { ConnectionId = connection.Id };
    }

    private Task WriteAuditAsync(
        Guid companyId,
        Guid userId,
        string action,
        string targetId,
        string outcome,
        string summary,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _audit?.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.Human,
                userId,
                action,
                "mailbox_connection",
                targetId,
                outcome,
                summary,
                ["mailbox"],
                metadata),
            cancellationToken) ?? Task.CompletedTask;

    private async Task<StandardMailboxConnectionResult> TestCoreAsync(
        Guid? connectionId,
        PreparedConnection prepared,
        StandardMailboxTestTarget target,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Standard mailbox test requested. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProfileKey: {ProfileKey}. Email: {EmailAddress}. Username: {Username}. Target: {Target}. ImapHost: {ImapHost}. ImapPort: {ImapPort}. SmtpHost: {SmtpHost}. SmtpPort: {SmtpPort}.",
            prepared.Context.CompanyId,
            connectionId ?? prepared.Context.ConnectionId,
            prepared.Profile.ProfileKey,
            MaskEmail(prepared.Context.EmailAddress),
            MaskEmail(prepared.Input.Username),
            target,
            prepared.Settings.Imap.Host,
            prepared.Settings.Imap.Port,
            prepared.Settings.Smtp.Host,
            prepared.Settings.Smtp.Port);
        var transport = _transports.Resolve(MailKitMailboxTransport.Key);
        var health = target switch
        {
            StandardMailboxTestTarget.Incoming => await transport.TestIncomingAsync(prepared.Context, cancellationToken),
            StandardMailboxTestTarget.Sending => await transport.TestSendingAsync(prepared.Context, cancellationToken),
            StandardMailboxTestTarget.Both => await transport.TestAsync(prepared.Context, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        _logger.LogInformation(
            "Standard mailbox test completed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ProfileKey: {ProfileKey}. Email: {EmailAddress}. Target: {Target}. IncomingSucceeded: {IncomingSucceeded}. SendingSucceeded: {SendingSucceeded}. FailureCode: {FailureCode}. FailureMessage: {FailureMessage}. FolderCount: {FolderCount}.",
            prepared.Context.CompanyId,
            connectionId ?? prepared.Context.ConnectionId,
            prepared.Profile.ProfileKey,
            MaskEmail(prepared.Context.EmailAddress),
            target,
            health.ImapSucceeded,
            health.SmtpSucceeded,
            health.SafeFailureCode,
            health.SafeFailureMessage,
            health.Folders.Count);
        return new StandardMailboxConnectionResult(
            connectionId,
            health.ImapSucceeded,
            health.SmtpSucceeded,
            health.AuthenticatedEmailAddress,
            health.Capabilities,
            health.Folders,
            health.SafeFailureCode,
            health.SafeFailureMessage,
            _timeProvider.GetUtcNow().UtcDateTime);
    }

    private PreparedConnection Prepare(Guid companyId, Guid connectionId, StandardMailboxConnectionInput input)
    {
        if (input.AuthenticationType != MailboxAuthenticationType.ApplicationPassword)
        {
            throw new InvalidOperationException("Use the OAuth connection flow for OAuth mailboxes.");
        }

        if (string.IsNullOrWhiteSpace(input.Credential))
        {
            throw new ArgumentException("Enter an application password. Do not enter your normal account password.", nameof(input));
        }

        var profile = _profiles.Resolve(input.ProfileKey);
        if (!profile.AuthenticationTypes.Contains(input.AuthenticationType))
        {
            throw new InvalidOperationException("The selected authentication method is not available for this email provider.");
        }

        var imap = ResolveEndpoint(profile, input.ImapOverride, profile.Imap);
        var smtp = ResolveEndpoint(profile, input.SmtpOverride, profile.Smtp);
        var settings = new MailboxTransportSettings(imap, smtp);
        var normalizedSecret = NormalizeApplicationPassword(profile.ProfileKey, input.Credential);
        var removedWhitespace = input.Credential.Length - normalizedSecret.Length;
        if (removedWhitespace > 0)
        {
            _logger.LogInformation(
                "Normalized application password formatting required by the mailbox profile. CompanyId: {CompanyId}. ProfileKey: {ProfileKey}. RemovedWhitespaceCharacters: {RemovedWhitespaceCharacters}.",
                companyId,
                profile.ProfileKey,
                removedWhitespace);
        }

        var credential = new MailboxCredentialLease(input.AuthenticationType, input.Username.Trim(), normalizedSecret);
        var context = new MailboxTransportContext(companyId, connectionId, input.EmailAddress.Trim().ToLowerInvariant(), settings, credential);
        return new PreparedConnection(input, profile, settings, context);
    }

    internal static string NormalizeApplicationPassword(string profileKey, string credential)
    {
        if (!string.Equals(
                profileKey,
                StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return credential;
        }

        return string.Concat(credential.Where(character => !char.IsWhiteSpace(character)));
    }

    internal static MailboxEndpointSettings ResolveEndpoint(
        MailboxConnectionProfile profile,
        MailboxEndpointSettings? endpointOverride,
        MailboxEndpointSettings profileEndpoint)
    {
        if (endpointOverride is null)
        {
            if (string.IsNullOrWhiteSpace(profileEndpoint.Host))
            {
                throw new ArgumentException("Enter the secure incoming and outgoing mail server settings.");
            }

            return profileEndpoint;
        }

        if (!profile.AllowsEndpointOverride)
        {
            if (EndpointMatches(endpointOverride, profileEndpoint))
            {
                return profileEndpoint;
            }

            throw new ArgumentException("Server addresses cannot be changed for this trusted email provider profile.", nameof(endpointOverride));
        }

        return endpointOverride;
    }

    private static bool EndpointMatches(MailboxEndpointSettings candidate, MailboxEndpointSettings expected) =>
        candidate.Port == expected.Port &&
        candidate.TlsMode == expected.TlsMode &&
        string.Equals(candidate.Host.Trim(), expected.Host.Trim(), StringComparison.OrdinalIgnoreCase);

    private void EnsureTenant(Guid companyId, Guid userId)
    {
        if (!_companyContext.IsResolved || _companyContext.CompanyId != companyId || _companyContext.UserId != userId)
        {
            throw new UnauthorizedAccessException("Mailbox connections are scoped to the current company and user.");
        }
    }

    private static string MaskEmail(string value)
    {
        value = value.Trim();
        var at = value.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        return $"{value[0]}***{value[(at - 1)..]}";
    }

    private sealed record PreparedConnection(
        StandardMailboxConnectionInput Input,
        MailboxConnectionProfile Profile,
        MailboxTransportSettings Settings,
        MailboxTransportContext Context);
}
