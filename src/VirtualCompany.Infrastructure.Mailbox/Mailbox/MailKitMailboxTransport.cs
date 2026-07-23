using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics.Metrics;
using SslProtocols = System.Security.Authentication.SslProtocols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class MailKitMailboxTransport : IMailboxTransport
{
    public const string Key = "mailkit";
    private static readonly Meter Meter = new("VirtualCompany.Mailbox.Transport");
    private static readonly Counter<long> ConnectionAttempts = Meter.CreateCounter<long>("mailbox_connection_attempts");
    private static readonly Counter<long> SubmissionOutcomes = Meter.CreateCounter<long>("mailbox_submission_outcomes");
    private static readonly Counter<long> CursorResets = Meter.CreateCounter<long>("mailbox_cursor_resets");
    private readonly IMailboxEndpointPolicy _endpointPolicy;
    private readonly IMailboxOperationConcurrencyGate? _operationGate;
    private readonly ILogger<MailKitMailboxTransport> _logger;

    public MailKitMailboxTransport(IMailboxEndpointPolicy endpointPolicy)
        : this(endpointPolicy, null, NullLogger<MailKitMailboxTransport>.Instance)
    {
    }

    public MailKitMailboxTransport(
        IMailboxEndpointPolicy endpointPolicy,
        IMailboxOperationConcurrencyGate? operationGate,
        ILogger<MailKitMailboxTransport>? logger = null)
    {
        _endpointPolicy = endpointPolicy;
        _operationGate = operationGate;
        _logger = logger ?? NullLogger<MailKitMailboxTransport>.Instance;
    }

    public string TransportKey => Key;

    public async Task<MailboxTransportHealthResult> TestAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            LogHealthTestStarted(context, "both");
            using var imap = await ConnectImapAsync(context, cancellationToken);
            var folders = await ReadFoldersAsync(imap.Client, cancellationToken);

            using var smtp = await ConnectSmtpAsync(context, cancellationToken);
            var capabilities = MailboxCapability.ReadMessages |
                MailboxCapability.ReadAttachments |
                MailboxCapability.ListFolders |
                MailboxCapability.SendMessages |
                MailboxCapability.IncrementalSync;

            if (folders.Any(folder => folder.IsDrafts && folder.CanAppend))
            {
                capabilities |= MailboxCapability.CreateDrafts;
            }

            _logger.LogInformation(
                "Mailbox health test succeeded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Target: {Target}. FolderCount: {FolderCount}. Capabilities: {Capabilities}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                "both",
                folders.Count,
                capabilities);
            return new MailboxTransportHealthResult(true, true, context.EmailAddress, capabilities, folders);
        }
        catch (AuthenticationException exception)
        {
            LogAuthenticationFailure(context, "both", exception);
            return new MailboxTransportHealthResult(
                false,
                false,
                context.EmailAddress,
                MailboxCapability.None,
                [],
                "authentication_failed",
                "The mail server rejected the username or credential.");
        }
        catch (MailboxEndpointPolicyException exception)
        {
            LogPolicyFailure(context, "both", exception.Code, exception.Message);
            return new MailboxTransportHealthResult(false, false, context.EmailAddress, MailboxCapability.None, [], exception.Code, exception.Message);
        }
        catch (SslHandshakeException exception)
        {
            LogTransportFailure(context, "both", "invalid_certificate", exception);
            return new MailboxTransportHealthResult(
                false,
                false,
                context.EmailAddress,
                MailboxCapability.None,
                [],
                "invalid_certificate",
                "The mail server TLS certificate could not be trusted or did not match its host name.");
        }
        catch (Exception exception) when (exception is IOException or ServiceNotConnectedException or ServiceNotAuthenticatedException or TimeoutException)
        {
            LogTransportFailure(context, "both", "mail_server_unavailable", exception);
            return new MailboxTransportHealthResult(
                false,
                false,
                context.EmailAddress,
                MailboxCapability.None,
                [],
                "mail_server_unavailable",
                "The secure mail server could not be reached or did not complete the request.");
        }
    }

    public async Task<MailboxTransportHealthResult> TestIncomingAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            LogHealthTestStarted(context, "incoming");
            using var imap = await ConnectImapAsync(context, cancellationToken);
            var folders = await ReadFoldersAsync(imap.Client, cancellationToken);
            var capabilities = MailboxCapability.ReadMessages |
                MailboxCapability.ReadAttachments |
                MailboxCapability.ListFolders |
                MailboxCapability.IncrementalSync;
            if (folders.Any(folder => folder.IsDrafts && folder.CanAppend))
            {
                capabilities |= MailboxCapability.CreateDrafts;
            }

            _logger.LogInformation(
                "Mailbox incoming test succeeded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. FolderCount: {FolderCount}. Capabilities: {Capabilities}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                folders.Count,
                capabilities);
            return new MailboxTransportHealthResult(true, false, context.EmailAddress, capabilities, folders);
        }
        catch (AuthenticationException exception)
        {
            LogAuthenticationFailure(context, "incoming", exception);
            return Failure(context, "authentication_failed", "The incoming mail server rejected the username or credential.");
        }
        catch (MailboxEndpointPolicyException exception)
        {
            LogPolicyFailure(context, "incoming", exception.Code, exception.Message);
            return Failure(context, exception.Code, exception.Message);
        }
        catch (SslHandshakeException exception)
        {
            LogTransportFailure(context, "incoming", "invalid_certificate", exception);
            return Failure(context, "invalid_certificate", "The incoming mail server TLS certificate could not be trusted or did not match its host name.");
        }
        catch (Exception exception) when (exception is IOException or ServiceNotConnectedException or ServiceNotAuthenticatedException or TimeoutException)
        {
            LogTransportFailure(context, "incoming", "mail_server_unavailable", exception);
            return Failure(context, "mail_server_unavailable", "The secure incoming mail server could not be reached or did not complete the request.");
        }
    }

    public async Task<MailboxTransportHealthResult> TestSendingAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            LogHealthTestStarted(context, "sending");
            using var smtp = await ConnectSmtpAsync(context, cancellationToken);
            await smtp.Client.DisconnectAsync(true, cancellationToken);
            _logger.LogInformation(
                "Mailbox sending test succeeded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Capabilities: {Capabilities}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                MailboxCapability.SendMessages);
            return new MailboxTransportHealthResult(false, true, context.EmailAddress, MailboxCapability.SendMessages, []);
        }
        catch (AuthenticationException exception)
        {
            LogAuthenticationFailure(context, "sending", exception);
            return Failure(context, "authentication_failed", "The sending mail server rejected the username or credential.");
        }
        catch (MailboxEndpointPolicyException exception)
        {
            LogPolicyFailure(context, "sending", exception.Code, exception.Message);
            return Failure(context, exception.Code, exception.Message);
        }
        catch (SslHandshakeException exception)
        {
            LogTransportFailure(context, "sending", "invalid_certificate", exception);
            return Failure(context, "invalid_certificate", "The sending mail server TLS certificate could not be trusted or did not match its host name.");
        }
        catch (Exception exception) when (exception is IOException or ServiceNotConnectedException or ServiceNotAuthenticatedException or TimeoutException)
        {
            LogTransportFailure(context, "sending", "mail_server_unavailable", exception);
            return Failure(context, "mail_server_unavailable", "The secure sending mail server could not be reached or did not complete the request.");
        }
    }

    private static MailboxTransportHealthResult Failure(
        MailboxTransportContext context,
        string code,
        string message) =>
        new(false, false, context.EmailAddress, MailboxCapability.None, [], code, message);

    public async Task<IReadOnlyList<MailboxTransportFolder>> ListFoldersAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectImapAsync(context, cancellationToken);
        return await ReadFoldersAsync(client.Client, cancellationToken);
    }

    public async Task<MailboxIncrementalPage> ReadIncrementalAsync(
        MailboxTransportContext context,
        MailboxIncrementalQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The incremental page size must be between 1 and 500.");
        }

        using var client = await ConnectImapAsync(context, cancellationToken);
        var folder = await client.Client.GetFolderAsync(query.FolderId, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uidValidity = folder.UidValidity;
        if (query.ExpectedUidValidity.HasValue && query.ExpectedUidValidity.Value != uidValidity)
        {
            CursorResets.Add(1);
            return new MailboxIncrementalPage(query.FolderId, uidValidity, 0, ReadHighestModSequence(folder), [], true);
        }

        var search = SearchQuery.All;
        if (query.FromUtc.HasValue)
        {
            search = search.And(SearchQuery.DeliveredAfter(query.FromUtc.Value.Date.AddDays(-1)));
        }
        if (query.ToUtc.HasValue)
        {
            search = search.And(SearchQuery.DeliveredBefore(query.ToUtc.Value.Date.AddDays(1)));
        }

        var allUids = await folder.SearchAsync(search, cancellationToken);
        var candidateUids = allUids
            .Where(uid => uid.Id > query.LastProcessedUid)
            .OrderBy(uid => uid.Id)
            .Take(query.Limit + 1)
            .ToArray();
        var hasMore = candidateUids.Length > query.Limit;
        var pageUids = candidateUids.Take(query.Limit).ToArray();
        if (pageUids.Length == 0)
        {
            return new MailboxIncrementalPage(query.FolderId, uidValidity, query.LastProcessedUid, ReadHighestModSequence(folder), [], false);
        }

        var summaries = await folder.FetchAsync(
            pageUids,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate |
            MessageSummaryItems.BodyStructure | MessageSummaryItems.Size,
            cancellationToken);
        var messages = summaries.Select(summary => ToSummary(query.FolderId, folder.Name, summary)).ToArray();

        return new MailboxIncrementalPage(
            query.FolderId,
            uidValidity,
            pageUids.Max(uid => (long)uid.Id),
            ReadHighestModSequence(folder),
            messages,
            hasMore);
    }

    public async Task<MailboxInboundMessage> GetMessageAsync(
        MailboxTransportContext context,
        MailboxMessageFetchRequest request,
        CancellationToken cancellationToken)
    {
        var locator = MessageLocator.Parse(request.MessageId);
        using var client = await ConnectImapAsync(context, cancellationToken);
        var folder = await client.Client.GetFolderAsync(locator.FolderId, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var message = await folder.GetMessageAsync(locator.UniqueId, cancellationToken);
        EnforceMessageSize(message, context.Settings.MaxMessageBytes);

        return new MailboxInboundMessage(
            request.MessageId,
            request.MessageId,
            message.MessageId,
            message.Subject,
            message.TextBody,
            message.HtmlBody,
            ToAddress(message.From.Mailboxes.FirstOrDefault()),
            message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(ToAddress).ToArray(),
            message.Date == default ? null : message.Date.UtcDateTime,
            message.Headers
                .GroupBy(header => header.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(header => header.Value)), StringComparer.OrdinalIgnoreCase));
    }

    public async Task<MailboxAttachmentContent?> GetAttachmentAsync(
        MailboxTransportContext context,
        MailboxAttachmentFetchRequest request,
        CancellationToken cancellationToken)
    {
        var locator = MessageLocator.Parse(request.MessageId);
        using var client = await ConnectImapAsync(context, cancellationToken);
        var folder = await client.Client.GetFolderAsync(locator.FolderId, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var message = await folder.GetMessageAsync(locator.UniqueId, cancellationToken);
        var attachment = message.Attachments
            .Select((entity, index) => new { Entity = entity, Index = index })
            .FirstOrDefault(item => string.Equals(item.Index.ToString(), request.AttachmentId, StringComparison.Ordinal) ||
                string.Equals(ReadFileName(item.Entity), request.FileName, StringComparison.OrdinalIgnoreCase));
        if (attachment is null)
        {
            return null;
        }

        await using var output = new MemoryStream();
        if (attachment.Entity is MimePart { Content: not null } part)
        {
            await part.Content.DecodeToAsync(output, cancellationToken);
        }
        else
        {
            await attachment.Entity.WriteToAsync(output, cancellationToken);
        }

        if (output.Length > context.Settings.MaxAttachmentBytes)
        {
            throw new InvalidDataException("The attachment exceeds the configured size limit.");
        }

        return new MailboxAttachmentContent(
            attachment.Index.ToString(),
            ReadFileName(attachment.Entity),
            attachment.Entity.ContentType.MimeType,
            output.ToArray());
    }

    public async Task<MailboxSubmissionResult> CreateDraftAsync(
        MailboxTransportContext context,
        MailboxOutboundMessage message,
        CancellationToken cancellationToken)
    {
        ValidateOutboundMessage(context, message);
        try
        {
            using var client = await ConnectImapAsync(context, cancellationToken);
            var folder = client.Client.GetFolder(SpecialFolder.Drafts)
                ?? throw new NotSupportedException("The mailbox does not expose a drafts folder.");
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            var mimeMessage = ToMimeMessage(message);
            var uid = await folder.AppendAsync(mimeMessage, MessageFlags.Draft, cancellationToken);
            return new MailboxSubmissionResult(MailboxSubmissionOutcome.Accepted, message.MessageId, uid?.Id.ToString(), null, null);
        }
        catch (AuthenticationException)
        {
            return AuthenticationRequired(message.MessageId);
        }
        catch (Exception exception) when (exception is IOException or ServiceNotConnectedException or TimeoutException)
        {
            return Retryable(message.MessageId);
        }
    }

    public async Task<MailboxSubmissionResult> SendAsync(
        MailboxTransportContext context,
        MailboxOutboundMessage message,
        CancellationToken cancellationToken)
    {
        ValidateOutboundMessage(context, message);
        var submissionStarted = false;
        SmtpSubmissionProtocolState? protocolState = null;
        try
        {
            protocolState = new SmtpSubmissionProtocolState();
            using var client = await ConnectSmtpAsync(context, cancellationToken, protocolState);
            var mimeMessage = ToMimeMessage(message);
            submissionStarted = true;
            var providerReference = await client.Client.SendAsync(mimeMessage, cancellationToken);
            await client.Client.DisconnectAsync(true, cancellationToken);
            SubmissionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "accepted"));
            return new MailboxSubmissionResult(MailboxSubmissionOutcome.Accepted, message.MessageId, providerReference, null, null);
        }
        catch (AuthenticationException)
        {
            SubmissionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "authentication_required"));
            return AuthenticationRequired(message.MessageId);
        }
        catch (SmtpCommandException exception) when ((int)exception.StatusCode >= 500)
        {
            SubmissionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "permanent_failure"));
            return new MailboxSubmissionResult(
                MailboxSubmissionOutcome.PermanentFailure,
                message.MessageId,
                null,
                "smtp_rejected",
                "The receiving mail server rejected the message.");
        }
        catch (Exception exception) when (submissionStarted &&
            protocolState?.MessageBodyAccepted == true &&
            exception is IOException or ServiceNotConnectedException or TimeoutException or SmtpProtocolException)
        {
            SubmissionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "ambiguous"));
            return new MailboxSubmissionResult(
                MailboxSubmissionOutcome.Ambiguous,
                message.MessageId,
                null,
                "smtp_delivery_ambiguous",
                "The mail server connection ended while the message was being submitted. Check the Sent folder before trying again.");
        }
        catch (Exception exception) when (exception is IOException or ServiceNotConnectedException or TimeoutException or SmtpCommandException)
        {
            SubmissionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "retryable_failure"));
            return Retryable(message.MessageId);
        }
    }

    public async Task<string?> FindSentMessageAsync(
        MailboxTransportContext context,
        string messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Contains('\r') || messageId.Contains('\n'))
        {
            throw new InvalidDataException("The message identifier is invalid.");
        }
        using var client = await ConnectImapAsync(context, cancellationToken);
        var sent = client.Client.GetFolder(SpecialFolder.Sent);
        if (sent is null)
        {
            return null;
        }

        await sent.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var matches = await sent.SearchAsync(SearchQuery.HeaderContains("Message-ID", messageId), cancellationToken);
        var match = matches.OrderByDescending(uid => uid.Id).FirstOrDefault();
        return match.IsValid ? MessageLocator.Format(sent.FullName, match) : null;
    }

    private async Task<MailboxClientLease<ImapClient>> ConnectImapAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken)
    {
        var approvedConnection = await ConnectApprovedSocketAsync(
            context.CompanyId,
            context.ConnectionId,
            context.Settings.Imap,
            context.Settings.ConnectionTimeoutSeconds,
            cancellationToken);
        var socket = approvedConnection.Socket;
        var client = new ImapClient
        {
            Timeout = checked(context.Settings.CommandTimeoutSeconds * 1000),
            SslProtocols = SslProtocols.None
        };
        try
        {
            _logger.LogInformation(
                "Connecting to IMAP endpoint. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}. Host: {Host}. Port: {Port}. TlsMode: {TlsMode}. AuthType: {AuthenticationType}. UsernameMatchesEmail: {UsernameMatchesEmail}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                MaskUsername(context.Credential.Username),
                context.Settings.Imap.Host,
                context.Settings.Imap.Port,
                context.Settings.Imap.TlsMode,
                context.Credential.AuthenticationType,
                string.Equals(context.Credential.Username.Trim(), context.EmailAddress.Trim(), StringComparison.OrdinalIgnoreCase));
            await client.ConnectAsync(
                socket,
                context.Settings.Imap.Host,
                context.Settings.Imap.Port,
                ToSocketOptions(context.Settings.Imap.TlsMode),
                cancellationToken);
            EnsureModernTls(client.SslProtocol);
            _logger.LogInformation(
                "IMAP TLS connection established. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Host: {Host}. Port: {Port}. TlsProtocol: {TlsProtocol}.",
                context.CompanyId,
                context.ConnectionId,
                context.Settings.Imap.Host,
                context.Settings.Imap.Port,
                client.SslProtocol);
            LogAuthenticationAttempt(client, context, "IMAP");
            await AuthenticateAsync(client, context.Credential, cancellationToken);
            _logger.LogInformation(
                "IMAP authentication succeeded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                MaskUsername(context.Credential.Username));
            return new MailboxClientLease<ImapClient>(client, approvedConnection.OperationLease);
        }
        catch
        {
            socket.Dispose();
            client.Dispose();
            if (approvedConnection.OperationLease is not null)
            {
                await approvedConnection.OperationLease.DisposeAsync();
            }
            throw;
        }
    }

    private async Task<MailboxClientLease<SmtpClient>> ConnectSmtpAsync(
        MailboxTransportContext context,
        CancellationToken cancellationToken,
        SmtpSubmissionProtocolState? protocolState = null)
    {
        var approvedConnection = await ConnectApprovedSocketAsync(
            context.CompanyId,
            context.ConnectionId,
            context.Settings.Smtp,
            context.Settings.ConnectionTimeoutSeconds,
            cancellationToken);
        var socket = approvedConnection.Socket;
        var client = protocolState is null ? new SmtpClient() : new SmtpClient(protocolState);
        client.Timeout = checked(context.Settings.CommandTimeoutSeconds * 1000);
        client.SslProtocols = SslProtocols.None;
        try
        {
            _logger.LogInformation(
                "Connecting to SMTP endpoint. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}. Host: {Host}. Port: {Port}. TlsMode: {TlsMode}. AuthType: {AuthenticationType}. UsernameMatchesEmail: {UsernameMatchesEmail}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                MaskUsername(context.Credential.Username),
                context.Settings.Smtp.Host,
                context.Settings.Smtp.Port,
                context.Settings.Smtp.TlsMode,
                context.Credential.AuthenticationType,
                string.Equals(context.Credential.Username.Trim(), context.EmailAddress.Trim(), StringComparison.OrdinalIgnoreCase));
            await client.ConnectAsync(
                socket,
                context.Settings.Smtp.Host,
                context.Settings.Smtp.Port,
                ToSocketOptions(context.Settings.Smtp.TlsMode),
                cancellationToken);
            EnsureModernTls(client.SslProtocol);
            _logger.LogInformation(
                "SMTP TLS connection established. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Host: {Host}. Port: {Port}. TlsProtocol: {TlsProtocol}.",
                context.CompanyId,
                context.ConnectionId,
                context.Settings.Smtp.Host,
                context.Settings.Smtp.Port,
                client.SslProtocol);
            LogAuthenticationAttempt(client, context, "SMTP");
            await AuthenticateAsync(client, context.Credential, cancellationToken);
            _logger.LogInformation(
                "SMTP authentication succeeded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}.",
                context.CompanyId,
                context.ConnectionId,
                MaskEmail(context.EmailAddress),
                MaskUsername(context.Credential.Username));
            return new MailboxClientLease<SmtpClient>(client, approvedConnection.OperationLease);
        }
        catch
        {
            socket.Dispose();
            client.Dispose();
            if (approvedConnection.OperationLease is not null)
            {
                await approvedConnection.OperationLease.DisposeAsync();
            }
            throw;
        }
    }

    private async Task<ApprovedSocketConnection> ConnectApprovedSocketAsync(
        Guid companyId,
        Guid connectionId,
        MailboxEndpointSettings endpoint,
        int connectionTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var protocol = endpoint.Port == 993 ? "imap" : "smtp";
        ConnectionAttempts.Add(1, new KeyValuePair<string, object?>("protocol", protocol));
        _logger.LogInformation(
            "Evaluating mailbox endpoint policy. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}. Port: {Port}. TlsMode: {TlsMode}.",
            companyId,
            connectionId,
            protocol,
            endpoint.Host,
            endpoint.Port,
            endpoint.TlsMode);
        var decision = await _endpointPolicy.EvaluateAsync(endpoint, cancellationToken);
        if (!decision.Allowed)
        {
            ConnectionAttempts.Add(
                1,
                new KeyValuePair<string, object?>("protocol", protocol),
                new KeyValuePair<string, object?>("outcome", "blocked"));
            _logger.LogWarning(
                "Mailbox endpoint policy blocked connection. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}. Port: {Port}. ReasonCode: {ReasonCode}. Explanation: {Explanation}.",
                companyId,
                connectionId,
                protocol,
                endpoint.Host,
                endpoint.Port,
                decision.ReasonCode,
                decision.Explanation);
            throw new MailboxEndpointPolicyException(decision.ReasonCode, decision.Explanation);
        }

        var operationLease = _operationGate is null
            ? null
            : await _operationGate.TryAcquireAsync(companyId, connectionId, endpoint.Host, cancellationToken);
        if (_operationGate is not null && operationLease is null)
        {
            _logger.LogWarning(
                "Mailbox connection capacity was limited. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}.",
                companyId,
                connectionId,
                protocol,
                endpoint.Host);
            throw new MailboxEndpointPolicyException(
                "mailbox_capacity_limited",
                "This mail connection is busy. Wait a moment and try again.");
        }

        _logger.LogInformation(
            "Mailbox endpoint policy allowed connection. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}. Port: {Port}. ResolvedAddressCount: {ResolvedAddressCount}.",
            companyId,
            connectionId,
            protocol,
            endpoint.Host,
            endpoint.Port,
            decision.ResolvedAddresses.Count);
        Exception? lastFailure = null;
        foreach (var address in decision.ResolvedAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(Math.Clamp(connectionTimeoutSeconds, 1, 120)), cancellationToken);
                _logger.LogInformation(
                    "Mailbox TCP connection established. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}. Port: {Port}. AddressFamily: {AddressFamily}.",
                    companyId,
                    connectionId,
                    protocol,
                    endpoint.Host,
                    endpoint.Port,
                    address.AddressFamily);
                return new ApprovedSocketConnection(socket, operationLease);
            }
            catch (Exception exception) when (exception is SocketException or IOException or TimeoutException)
            {
                lastFailure = exception;
                _logger.LogWarning(
                    exception,
                    "Mailbox TCP connection failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. Host: {Host}. Port: {Port}. AddressFamily: {AddressFamily}.",
                    companyId,
                    connectionId,
                    protocol,
                    endpoint.Host,
                    endpoint.Port,
                    address.AddressFamily);
                socket.Dispose();
            }
        }

        if (operationLease is not null)
        {
            await operationLease.DisposeAsync();
        }

        throw new IOException("The approved mail server addresses could not be reached.", lastFailure);
    }

    private void LogHealthTestStarted(MailboxTransportContext context, string target)
    {
        _logger.LogInformation(
            "Mailbox health test started. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}. Target: {Target}. ImapHost: {ImapHost}. ImapPort: {ImapPort}. ImapTlsMode: {ImapTlsMode}. SmtpHost: {SmtpHost}. SmtpPort: {SmtpPort}. SmtpTlsMode: {SmtpTlsMode}. AuthType: {AuthenticationType}. UsernameMatchesEmail: {UsernameMatchesEmail}.",
            context.CompanyId,
            context.ConnectionId,
            MaskEmail(context.EmailAddress),
            MaskUsername(context.Credential.Username),
            target,
            context.Settings.Imap.Host,
            context.Settings.Imap.Port,
            context.Settings.Imap.TlsMode,
            context.Settings.Smtp.Host,
            context.Settings.Smtp.Port,
            context.Settings.Smtp.TlsMode,
            context.Credential.AuthenticationType,
            string.Equals(context.Credential.Username.Trim(), context.EmailAddress.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void LogAuthenticationFailure(MailboxTransportContext context, string target, Exception exception)
    {
        _logger.LogWarning(
            exception,
            "Mailbox authentication failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Username: {Username}. Target: {Target}. AuthType: {AuthenticationType}. UsernameMatchesEmail: {UsernameMatchesEmail}. ImapHost: {ImapHost}. ImapPort: {ImapPort}. SmtpHost: {SmtpHost}. SmtpPort: {SmtpPort}. FailureCode: {FailureCode}. ProviderMessage: {ProviderMessage}.",
            context.CompanyId,
            context.ConnectionId,
            MaskEmail(context.EmailAddress),
            MaskUsername(context.Credential.Username),
            target,
            context.Credential.AuthenticationType,
            string.Equals(context.Credential.Username.Trim(), context.EmailAddress.Trim(), StringComparison.OrdinalIgnoreCase),
            context.Settings.Imap.Host,
            context.Settings.Imap.Port,
            context.Settings.Smtp.Host,
            context.Settings.Smtp.Port,
            "authentication_failed",
            SanitizeProviderMessage(exception.Message));
    }

    private void LogAuthenticationAttempt(
        MailService client,
        MailboxTransportContext context,
        string protocol)
    {
        _logger.LogInformation(
            "Mailbox authentication starting. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Protocol: {Protocol}. AuthType: {AuthenticationType}. Username: {Username}. AdvertisedMechanisms: {AdvertisedMechanisms}.",
            context.CompanyId,
            context.ConnectionId,
            protocol,
            context.Credential.AuthenticationType,
            MaskUsername(context.Credential.Username),
            string.Join(",", client.AuthenticationMechanisms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
    }

    private void LogPolicyFailure(MailboxTransportContext context, string target, string code, string message)
    {
        _logger.LogWarning(
            "Mailbox endpoint policy failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Target: {Target}. FailureCode: {FailureCode}. FailureMessage: {FailureMessage}.",
            context.CompanyId,
            context.ConnectionId,
            MaskEmail(context.EmailAddress),
            target,
            code,
            message);
    }

    private void LogTransportFailure(MailboxTransportContext context, string target, string code, Exception exception)
    {
        _logger.LogWarning(
            exception,
            "Mailbox transport test failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Email: {EmailAddress}. Target: {Target}. FailureCode: {FailureCode}.",
            context.CompanyId,
            context.ConnectionId,
            MaskEmail(context.EmailAddress),
            target,
            code);
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

    private static string MaskUsername(string value)
    {
        value = value.Trim();
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return MaskEmail(value);
        }

        return value.Length <= 2 ? "***" : $"{value[0]}***{value[^1]}";
    }

    private sealed record ApprovedSocketConnection(Socket Socket, IAsyncDisposable? OperationLease);

    internal sealed class SmtpSubmissionProtocolState : IProtocolLogger
    {
        private readonly StringBuilder _serverLine = new();
        private bool _dataCommandSent;

        public bool MessageBodyAccepted { get; private set; }

        public IAuthenticationSecretDetector? AuthenticationSecretDetector { get; set; }

        public void LogConnect(Uri uri)
        {
        }

        public void LogClient(byte[] buffer, int offset, int count)
        {
            if (_dataCommandSent || count <= 0)
            {
                return;
            }

            var command = Encoding.ASCII.GetString(buffer, offset, Math.Min(count, 16));
            _dataCommandSent = command.StartsWith("DATA", StringComparison.OrdinalIgnoreCase);
        }

        public void LogServer(byte[] buffer, int offset, int count)
        {
            if (!_dataCommandSent || MessageBodyAccepted)
            {
                return;
            }

            for (var index = offset; index < offset + count; index++)
            {
                var character = (char)buffer[index];
                if (character == '\n')
                {
                    MessageBodyAccepted = _serverLine.ToString().TrimStart().StartsWith("354", StringComparison.Ordinal);
                    _serverLine.Clear();
                    if (MessageBodyAccepted)
                    {
                        return;
                    }
                }
                else if (character != '\r' && _serverLine.Length < 32)
                {
                    _serverLine.Append(character);
                }
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class MailboxClientLease<TClient>(TClient client, IAsyncDisposable? operationLease) : IDisposable
        where TClient : MailService
    {
        private int _disposed;

        public TClient Client { get; } = client;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Client.Dispose();
            operationLease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task AuthenticateAsync(MailService client, MailboxCredentialLease credential, CancellationToken cancellationToken)
    {
        if (credential.AuthenticationType == MailboxAuthenticationType.OAuth2)
        {
            await client.AuthenticateAsync(new SaslMechanismOAuth2(credential.Username, credential.Secret), cancellationToken);
            return;
        }

        // MailKit otherwise considers every advertised mechanism. An application
        // password must never be submitted as an OAuth bearer token.
        ConfigureApplicationPasswordAuthentication(client.AuthenticationMechanisms);
        await client.AuthenticateAsync(credential.Username, credential.Secret, cancellationToken);
    }

    internal static void ConfigureApplicationPasswordAuthentication(ISet<string> authenticationMechanisms)
    {
        authenticationMechanisms.Remove("XOAUTH2");
        authenticationMechanisms.Remove("OAUTHBEARER");
    }

    private static string SanitizeProviderMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "not provided";
        }

        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 500 ? singleLine : singleLine[..500];
    }

    private static async Task<IReadOnlyList<MailboxTransportFolder>> ReadFoldersAsync(ImapClient client, CancellationToken cancellationToken)
    {
        var folders = new List<IMailFolder> { client.Inbox };
        foreach (var mailboxNamespace in client.PersonalNamespaces)
        {
            folders.AddRange(await client.GetFoldersAsync(mailboxNamespace, StatusItems.None, false, cancellationToken));
        }

        return folders
            .DistinctBy(folder => folder.FullName, StringComparer.OrdinalIgnoreCase)
            .Where(folder => !folder.Attributes.HasFlag(FolderAttributes.NonExistent))
            .Select(folder => new MailboxTransportFolder(
                folder.FullName,
                folder.Name,
                !folder.Attributes.HasFlag(FolderAttributes.NoSelect),
                !folder.Attributes.HasFlag(FolderAttributes.NoSelect),
                folder.Attributes.HasFlag(FolderAttributes.Inbox) || ReferenceEquals(folder, client.Inbox),
                folder.Attributes.HasFlag(FolderAttributes.Drafts),
                folder.Attributes.HasFlag(FolderAttributes.Sent)))
            .OrderByDescending(folder => folder.IsInbox)
            .ThenBy(folder => folder.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MailboxMessageSummary ToSummary(string folderId, string folderName, IMessageSummary summary)
    {
        var attachments = summary.BodyParts
            .Where(part => part.IsAttachment)
            .Select((part, index) => new MailboxAttachmentSummary(
                index.ToString(),
                part.FileName,
                part.ContentType.MimeType,
                part.Octets))
            .ToArray();
        var from = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
        var providerMessageId = MessageLocator.Format(folderId, summary.UniqueId);

        return new MailboxMessageSummary(
            providerMessageId,
            summary.Envelope?.Subject,
            null,
            null,
            attachments.Select(attachment => attachment.FileName).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray(),
            from?.Address,
            from?.Name,
            summary.InternalDate?.UtcDateTime ?? summary.Envelope?.Date?.UtcDateTime,
            folderId,
            folderName,
            providerMessageId,
            attachments);
    }

    private static MimeMessage ToMimeMessage(MailboxOutboundMessage source)
    {
        var message = new MimeMessage { Subject = source.Subject, MessageId = source.MessageId };
        message.From.Add(MimeKit.MailboxAddress.Parse(source.FromAddress));
        message.To.AddRange(source.To.Select(MimeKit.MailboxAddress.Parse));
        message.Cc.AddRange(source.Cc.Select(MimeKit.MailboxAddress.Parse));
        message.Bcc.AddRange(source.Bcc.Select(MimeKit.MailboxAddress.Parse));
        if (!string.IsNullOrWhiteSpace(source.InReplyTo))
        {
            message.InReplyTo = source.InReplyTo;
        }

        foreach (var reference in source.References.Where(reference => !string.IsNullOrWhiteSpace(reference)))
        {
            message.References.Add(reference);
        }

        var builder = new BodyBuilder { TextBody = source.PlainTextBody, HtmlBody = source.HtmlBody };
        foreach (var attachment in source.Attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.MimeType));
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static VirtualCompany.Application.Mailbox.MailboxAddress ToAddress(MimeKit.MailboxAddress? address) =>
        new(address?.Address, address?.Name);

    private static string? ReadFileName(MimeEntity entity) =>
        entity is MimePart part ? part.FileName : entity.ContentDisposition?.FileName;

    private static SecureSocketOptions ToSocketOptions(MailboxTlsMode tlsMode) => tlsMode switch
    {
        MailboxTlsMode.ImplicitTls => SecureSocketOptions.SslOnConnect,
        MailboxTlsMode.StartTls => SecureSocketOptions.StartTls,
        _ => throw new ArgumentOutOfRangeException(nameof(tlsMode), "Unsupported TLS mode.")
    };

    private static void EnsureModernTls(SslProtocols negotiatedProtocol)
    {
        if (negotiatedProtocol is not SslProtocols.Tls12 and not SslProtocols.Tls13)
        {
            throw new MailboxEndpointPolicyException(
                "tls_version_not_supported",
                "The mail server did not negotiate TLS 1.2 or newer.");
        }
    }

    private static long? ReadHighestModSequence(IMailFolder folder) =>
        folder.Supports(FolderFeature.ModSequences) ? checked((long)folder.HighestModSeq) : null;

    private static void EnforceMessageSize(MimeMessage message, long maxBytes)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        if (stream.Length > maxBytes)
        {
            throw new InvalidDataException("The message exceeds the configured size limit.");
        }
    }

    private static void ValidateOutboundMessage(MailboxTransportContext context, MailboxOutboundMessage message)
    {
        if (!string.Equals(context.EmailAddress.Trim(), message.FromAddress.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The From address must match the connected mailbox.");
        }

        if (message.Attachments.Count > context.Settings.MaxAttachments)
        {
            throw new InvalidDataException("The message contains too many attachments.");
        }

        if (message.Attachments.Any(attachment => attachment.Content.LongLength > context.Settings.MaxAttachmentBytes))
        {
            throw new InvalidDataException("An attachment exceeds the configured size limit.");
        }

        if (new[] { message.Subject, message.InReplyTo }
            .Concat(message.References)
            .Any(value => value?.Contains('\r') == true || value?.Contains('\n') == true))
        {
            throw new InvalidDataException("Message headers cannot contain line breaks.");
        }
    }

    private static MailboxSubmissionResult AuthenticationRequired(string messageId) =>
        new(MailboxSubmissionOutcome.AuthenticationRequired, messageId, null, "authentication_required", "Reconnect this mailbox before sending.");

    private static MailboxSubmissionResult Retryable(string messageId) =>
        new(MailboxSubmissionOutcome.RetryableFailure, messageId, null, "mail_server_unavailable", "The mail server is temporarily unavailable. The message can be retried.");

    private sealed record MessageLocator(string FolderId, UniqueId UniqueId)
    {
        public static string Format(string folderId, UniqueId uniqueId) =>
            $"{Base64UrlEncode(folderId)}.{uniqueId.Id}";

        public static MessageLocator Parse(string value)
        {
            value = StandardMailboxMessageReference.WithoutUidValidity(value);
            var separator = value.LastIndexOf('.');
            if (separator <= 0 || !uint.TryParse(value[(separator + 1)..], out var uid) || uid == 0)
            {
                throw new ArgumentException("The mailbox message reference is invalid.", nameof(value));
            }

            return new MessageLocator(Base64UrlDecode(value[..separator]), new UniqueId(uid));
        }

        private static string Base64UrlEncode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
    }

    private sealed class MailboxEndpointPolicyException : Exception
    {
        public MailboxEndpointPolicyException(string code, string message) : base(message) => Code = code;
        public string Code { get; }
    }

}
