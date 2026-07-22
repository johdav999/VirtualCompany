using System.Security.Cryptography;
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

public sealed class CompanyMailboxConnectionService : IMailboxConnectionService
{
    private static readonly TimeSpan OAuthStateTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ManualScanWindow = TimeSpan.FromDays(30);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IMailboxOAuthStateProtector _stateProtector;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly IManualInboxBillScanJobScheduler _scanJobScheduler;
    private readonly IConnectedMailboxInboxScanJobScheduler _connectedMailboxScanJobScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyMailboxConnectionService> _logger;
    private readonly IMailboxOAuthReplayGuard? _replayGuard;
    private readonly IMailboxTransportRegistry? _transportRegistry;
    private readonly IAuditEventWriter? _audit;

    public CompanyMailboxConnectionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        IMailboxOAuthStateProtector stateProtector,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        IManualInboxBillScanJobScheduler scanJobScheduler,
        IConnectedMailboxInboxScanJobScheduler connectedMailboxScanJobScheduler,
        TimeProvider timeProvider,
        ILogger<CompanyMailboxConnectionService> logger,
        IMailboxOAuthReplayGuard? replayGuard = null,
        IMailboxTransportRegistry? transportRegistry = null,
        IAuditEventWriter? audit = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _stateProtector = stateProtector;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _scanJobScheduler = scanJobScheduler;
        _connectedMailboxScanJobScheduler = connectedMailboxScanJobScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
        _replayGuard = replayGuard;
        _transportRegistry = transportRegistry;
        _audit = audit;
    }

    public CompanyMailboxConnectionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        IMailboxOAuthStateProtector stateProtector,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        IManualInboxBillScanJobScheduler scanJobScheduler,
        TimeProvider timeProvider,
        ILogger<CompanyMailboxConnectionService> logger)
        : this(
            dbContext,
            companyContextAccessor,
            stateProtector,
            providerRegistry,
            fieldEncryption,
            scanJobScheduler,
            NoOpConnectedMailboxInboxScanJobScheduler.Instance,
            timeProvider,
            logger)
    {
    }

    public async Task<MailboxOAuthStartResult> StartOAuthConnectionAsync(
        StartMailboxOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        MailboxPurposeValues.EnsureSupported(command.Purpose, nameof(command.Purpose));
        var provider = _providerRegistry.Resolve(command.Provider);
        var now = UtcNow();
        var configuredFolders = NormalizeFolders(command.ConfiguredFolders, command.Provider);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (command.Provider == MailboxProvider.StandardEmail &&
            (string.IsNullOrWhiteSpace(command.ProfileKey) ||
             string.IsNullOrWhiteSpace(command.EmailAddress) ||
             string.IsNullOrWhiteSpace(command.Username) ||
             command.Imap is null || command.Smtp is null))
        {
            throw new ArgumentException("Hosted mailbox OAuth requires a profile, mailbox address, username, and secure endpoints.", nameof(command));
        }

        var state = _stateProtector.Protect(new MailboxOAuthState(
            CompanyId: command.CompanyId,
            UserId: command.UserId,
            Provider: command.Provider,
            ConfiguredFolders: configuredFolders,
            ExpiresUtc: now.Add(OAuthStateTtl),
            ReturnUri: command.ReturnUri,
            Purpose: command.Purpose,
            ProfileKey: command.ProfileKey,
            EmailAddress: command.EmailAddress,
            Username: command.Username,
            Imap: command.Imap,
            Smtp: command.Smtp,
            Nonce: nonce));
        if (_replayGuard is not null)
        {
            await _replayGuard.RegisterAsync(
                command.CompanyId,
                command.UserId,
                command.Purpose,
                command.Provider,
                nonce,
                now.Add(OAuthStateTtl),
                cancellationToken);
        }

        var authorizationUrl = provider.BuildAuthorizationUrl(new MailboxAuthorizationRequest(
            command.CompanyId,
            command.UserId,
            command.CallbackUri,
            state,
            command.ProfileKey));

        _logger.LogInformation(
            "Mailbox OAuth start built. CompanyId: {CompanyId}. UserId: {UserId}. Purpose: {Purpose}. Provider: {Provider}.",
            command.CompanyId,
            command.UserId,
            command.Purpose,
            command.Provider);

        return new MailboxOAuthStartResult(command.Provider, authorizationUrl);
    }

    public async Task<MailboxConnectionStatusResult> GetStatusAsync(
        GetMailboxConnectionStatusQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(query.CompanyId, query.UserId);
        MailboxPurposeValues.EnsureSupported(query.Purpose, nameof(query.Purpose));

        var connection = await _dbContext.MailboxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Purpose == query.Purpose)
            .OrderByDescending(x => x.UserId == query.UserId)
            .ThenByDescending(x => x.Status == MailboxConnectionStatus.Active)
            .ThenByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            return new MailboxConnectionStatusResult(
                IsConnected: false,
                MailboxConnectionId: null,
                Provider: null,
                ConnectionStatus: null,
                EmailAddress: null,
                DisplayName: null,
                ConnectedAtUtc: null,
                LastSuccessfulScanAtUtc: null,
                LastErrorSummary: null,
                ConfiguredFolders: [],
                LastRun: null,
                Purpose: query.Purpose.ToStorageValue());
        }

        var lastRun = await _dbContext.EmailIngestionRuns
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                x.MailboxConnectionId == connection.Id)
            .OrderByDescending(x => x.StartedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new MailboxConnectionStatusResult(
            IsConnected: connection.Status == MailboxConnectionStatus.Active,
            MailboxConnectionId: connection.Id,
            Provider: connection.Provider.ToStorageValue(),
            ConnectionStatus: connection.Status.ToStorageValue(),
            EmailAddress: connection.EmailAddress,
            DisplayName: connection.DisplayName,
            ConnectedAtUtc: connection.CreatedUtc,
            LastSuccessfulScanAtUtc: connection.LastSuccessfulScanUtc,
            LastErrorSummary: connection.LastErrorSummary,
            ConfiguredFolders: connection.ConfiguredFolders.Select(x => new MailboxFolderSelectionSummary(x.ProviderFolderId, x.DisplayName)).ToArray(),
            LastRun: lastRun is null ? null : new EmailIngestionRunSummary(
                lastRun.Id, lastRun.StartedUtc, lastRun.CompletedUtc, lastRun.Provider.ToStorageValue(),
                lastRun.ScanFromUtc, lastRun.ScanToUtc,
                lastRun.ScannedMessageCount,
                lastRun.DetectedCandidateCount,
                lastRun.NonCandidateMessageCount,
                lastRun.CandidateAttachmentSnapshotCount,
                lastRun.DeduplicatedAttachmentCount,
                lastRun.FailureDetails),
            Purpose: connection.Purpose.ToStorageValue());
    }

    public async Task<IReadOnlyList<MailboxScannedMessageSummary>> GetScannedMessagesAsync(
        GetMailboxScannedMessagesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(query.CompanyId, query.UserId);
        MailboxPurposeValues.EnsureSupported(query.Purpose, nameof(query.Purpose));

        var limit = Math.Clamp(query.Limit, 1, 100);
        var connectionIds = await _dbContext.MailboxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Purpose == query.Purpose)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (connectionIds.Length == 0)
        {
            return [];
        }

        var snapshots = await _dbContext.EmailMessageSnapshots
            .AsNoTracking()
            .Include(x => x.Attachments)
            .Where(x => x.CompanyId == query.CompanyId && connectionIds.Contains(x.MailboxConnectionId))
            .OrderByDescending(x => x.ReceivedUtc ?? x.CreatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        var snapshotSourceIds = snapshots.Select(snapshot => snapshot.Id.ToString("D")).ToArray();
        var detectedBillIdsBySourceEmailId = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                x.SourceEmailId != null &&
                snapshotSourceIds.Contains(x.SourceEmailId))
            .GroupBy(x => x.SourceEmailId!)
            .Select(x => new { SourceEmailId = x.Key, BillId = x.OrderByDescending(bill => bill.UpdatedUtc).Select(bill => bill.Id).First() })
            .ToDictionaryAsync(x => x.SourceEmailId, x => x.BillId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return snapshots
            .Select(snapshot => new MailboxScannedMessageSummary(
                snapshot.Id,
                snapshot.EmailIngestionRunId,
                snapshot.ExternalMessageId,
                snapshot.FromAddress,
                snapshot.FromDisplayName,
                snapshot.Subject,
                snapshot.ReceivedUtc,
                snapshot.FolderId,
                snapshot.FolderDisplayName,
                snapshot.SourceType.ToStorageValue(),
                snapshot.CandidateDecision.ToStorageValue(),
                snapshot.MatchedRules.Select(rule => rule.ToStorageValue()).ToArray(),
                snapshot.ReasonSummary,
                snapshot.UntrustedBodyText,
                snapshot.Attachments
                    .OrderBy(attachment => attachment.FileName)
                    .Select(attachment => new MailboxScannedAttachmentSummary(
                        attachment.FileName,
                        attachment.MimeType,
                        attachment.SizeBytes,
                        attachment.SourceType.ToStorageValue(),
                        attachment.IsDuplicateByHash))
                    .ToArray(),
                detectedBillIdsBySourceEmailId.TryGetValue(snapshot.Id.ToString("D"), out var detectedBillId) ? detectedBillId : null,
                snapshot.CreatedUtc))
            .ToArray();
    }

    public async Task<MailboxOAuthCompletionResult> CompleteOAuthConnectionAsync(
        CompleteMailboxOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var state = _stateProtector.Unprotect(command.State);
        if (state.ExpiresUtc <= UtcNow())
        {
            throw new InvalidOperationException("Mailbox OAuth state has expired.");
        }

        if (state.CompanyId == Guid.Empty || state.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Mailbox OAuth state was invalid.");
        }

        MailboxProviderValues.EnsureSupported(state.Provider, nameof(state.Provider));
        MailboxPurposeValues.EnsureSupported(state.Purpose, nameof(state.Purpose));

        if (!string.IsNullOrWhiteSpace(state.Nonce) &&
            _replayGuard is not null &&
            !await _replayGuard.TryConsumeAsync(
                state.CompanyId,
                state.UserId,
                state.Purpose,
                state.Provider,
                state.Nonce,
                UtcNow(),
                cancellationToken))
        {
            throw new UnauthorizedAccessException("Mailbox OAuth state was already used or has expired.");
        }

        if (command.ExpectedProvider.HasValue && state.Provider != command.ExpectedProvider.Value)
        {
            throw new UnauthorizedAccessException("Mailbox OAuth state provider did not match the callback endpoint.");
        }

        ResolveCompletionTenantUserFromState(state);
        var provider = _providerRegistry.Resolve(state.Provider);
        var tokenResult = await provider.ExchangeCodeAsync(
            new MailboxTokenExchangeRequest(command.Code, command.CallbackUri, state.ProfileKey),
            cancellationToken);
        var profile = state.Provider == MailboxProvider.StandardEmail
            ? new MailboxAccountProfile(
                state.EmailAddress ?? throw new InvalidOperationException("Hosted mailbox OAuth state did not include an email address."),
                null,
                state.EmailAddress)
            : await provider.GetAccountProfileAsync(tokenResult.AccessToken, cancellationToken);
        MailboxTransportHealthResult? standardHealth = null;
        if (state.Provider == MailboxProvider.StandardEmail)
        {
            if (_transportRegistry is null)
            {
                throw new InvalidOperationException("The hosted mailbox transport is not available.");
            }

            standardHealth = await _transportRegistry.Resolve(MailKitMailboxTransport.Key).TestAsync(
                new MailboxTransportContext(
                    state.CompanyId,
                    Guid.NewGuid(),
                    state.EmailAddress!,
                    new MailboxTransportSettings(state.Imap!, state.Smtp!),
                    new MailboxCredentialLease(MailboxAuthenticationType.OAuth2, state.Username!, tokenResult.AccessToken, tokenResult.AccessTokenExpiresUtc)),
                cancellationToken);
            if (!standardHealth.ImapSucceeded || !standardHealth.SmtpSucceeded)
            {
                throw new InvalidOperationException(standardHealth.SafeFailureMessage ?? "The hosted mailbox could not be authenticated.");
            }
        }
        var normalizedEmail = profile.EmailAddress.Trim().ToLowerInvariant();
        var existing = await _dbContext.MailboxConnections
            .SingleOrDefaultAsync(
                x => x.CompanyId == state.CompanyId &&
                    x.Purpose == state.Purpose &&
                    x.Provider == state.Provider &&
                    x.EmailAddress == normalizedEmail,
                cancellationToken);

        var now = UtcNow();
        var connection = existing ?? new MailboxConnection(
            Guid.NewGuid(),
            state.CompanyId,
            state.UserId,
            state.Provider,
            normalizedEmail,
            profile.DisplayName,
            now,
            purpose: state.Purpose);

        var otherActiveConnections = await _dbContext.MailboxConnections
            .Where(x => x.CompanyId == state.CompanyId &&
                x.Purpose == state.Purpose &&
                x.Id != connection.Id &&
                x.Status == MailboxConnectionStatus.Active)
            .ToArrayAsync(cancellationToken);
        foreach (var otherConnection in otherActiveConnections)
        {
            otherConnection.SetStatus(MailboxConnectionStatus.Disconnected);
        }

        connection.UpdateMailboxProfile(normalizedEmail, profile.DisplayName, profile.ProviderAccountId);
        if (state.Provider == MailboxProvider.StandardEmail)
        {
            var imap = state.Imap ?? throw new InvalidOperationException("Hosted mailbox OAuth state did not include the incoming endpoint.");
            var smtp = state.Smtp ?? throw new InvalidOperationException("Hosted mailbox OAuth state did not include the sending endpoint.");
            connection.ConfigureStandardConnection(
                state.ProfileKey ?? throw new InvalidOperationException("Hosted mailbox OAuth state did not include a profile."),
                MailboxAuthenticationType.OAuth2,
                state.Username ?? normalizedEmail,
                imap.Host,
                imap.Port,
                imap.TlsMode,
                smtp.Host,
                smtp.Port,
                smtp.TlsMode,
                standardHealth!.Capabilities);
        }
        connection.ConfigureFolders(state.ConfiguredFolders.Count > 0
            ? state.ConfiguredFolders
            : standardHealth?.Folders.Where(folder => folder.IsInbox && folder.CanRead)
                .Select(folder => new MailboxFolderSelection(folder.FolderId, folder.DisplayName))
                .ToArray());
        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(
                state.CompanyId,
                state.Provider == MailboxProvider.StandardEmail
                    ? StandardMailboxCredentialPurposes.AccessToken(connection.Id)
                    : BuildTokenPurpose(state.Provider, "access_token"),
                tokenResult.AccessToken),
            string.IsNullOrEmpty(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(
                    state.CompanyId,
                    state.Provider == MailboxProvider.StandardEmail
                        ? StandardMailboxCredentialPurposes.RefreshToken(connection.Id)
                        : BuildTokenPurpose(state.Provider, "refresh_token"),
                    tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes);
        connection.SetStatus(MailboxConnectionStatus.Active);

        if (existing is null)
        {
            _dbContext.MailboxConnections.Add(connection);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteMailboxAuditAsync(
            state.CompanyId,
            state.UserId,
            existing is null ? "mailbox.connection.connected" : "mailbox.credential.replaced",
            connection.Id,
            AuditEventOutcomes.Succeeded,
            existing is null ? "Mailbox connected through OAuth." : "Mailbox OAuth credential replaced.",
            connection,
            cancellationToken);

        _logger.LogInformation(
            "Mailbox OAuth connection completed. CompanyId: {CompanyId}. UserId: {UserId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
            state.CompanyId,
            state.UserId,
            state.Purpose,
            state.Provider,
            connection.Id);

        await EnqueueConnectedMailboxScanAsync(connection, cancellationToken);

        return new MailboxOAuthCompletionResult(
            MailboxConnectionId: connection.Id,
            CompanyId: state.CompanyId,
            UserId: state.UserId,
            Provider: connection.Provider,
            EmailAddress: connection.EmailAddress,
            Status: connection.Status.ToStorageValue(),
            ReturnUri: state.ReturnUri,
            Purpose: connection.Purpose);
    }

    public async Task<ManualMailboxScanResult> TriggerManualScanAsync(
        TriggerManualMailboxScanCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        if (command.Purpose != MailboxPurpose.Finance)
        {
            throw new InvalidOperationException("Manual supplier bill scanning is available only for the finance mailbox.");
        }
        var query = _dbContext.MailboxConnections
            .Where(
                x => x.CompanyId == command.CompanyId &&
                    x.Purpose == command.Purpose);

        if (command.MailboxConnectionId.HasValue)
        {
            query = query.Where(x => x.Id == command.MailboxConnectionId.Value);
        }

        var connection = await query
            .OrderByDescending(x => x.UserId == command.UserId)
            .ThenByDescending(x => x.Status == MailboxConnectionStatus.Active)
            .ThenByDescending(x => x.Status == MailboxConnectionStatus.Failed)
            .ThenByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            throw new KeyNotFoundException("Mailbox connection was not found.");
        }

        if (!CanRunManualScan(connection.Status))
        {
            throw new InvalidOperationException("Mailbox connection is not active.");
        }

        var now = UtcNow();
        var scanFromUtc = now.Subtract(ManualScanWindow);
        var scanToUtc = now;
        var run = new EmailIngestionRun(
            Guid.NewGuid(),
            command.CompanyId,
            connection.Id,
            command.UserId,
            connection.Provider,
            now,
            scanFromUtc,
            scanToUtc);
        _dbContext.EmailIngestionRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _scanJobScheduler.EnqueueManualScanAsync(
            new ManualInboxBillScanJob(command.CompanyId, command.UserId, connection.Id, run.Id, scanFromUtc, scanToUtc),
            cancellationToken);

        await _dbContext.Entry(run).ReloadAsync(cancellationToken);

        return new ManualMailboxScanResult(
            run.Id,
            connection.Id,
            scanFromUtc,
            scanToUtc,
            run.ScannedMessageCount,
            run.DetectedCandidateCount,
            run.NonCandidateMessageCount,
            run.CandidateAttachmentSnapshotCount,
            run.DeduplicatedAttachmentCount,
            run.FailureDetails,
            run.CompletedUtc.HasValue ? "completed" : "started");
    }

    private void EnsureCurrentTenantUser(Guid companyId, Guid userId)
    {
        if (!_companyContextAccessor.IsResolved ||
            _companyContextAccessor.CompanyId != companyId ||
            _companyContextAccessor.UserId != userId)
        {
            throw new UnauthorizedAccessException("Mailbox connections are scoped to the current tenant and user.");
        }
    }

    private static bool CanRunManualScan(MailboxConnectionStatus status) =>
        status is MailboxConnectionStatus.Active or MailboxConnectionStatus.Failed;

    private void ResolveCompletionTenantUserFromState(MailboxOAuthState state)
    {
        if (_companyContextAccessor.IsResolved)
        {
            EnsureCurrentTenantUser(state.CompanyId, state.UserId);
            return;
        }

        if (_companyContextAccessor.CompanyId.HasValue &&
            _companyContextAccessor.CompanyId.Value != state.CompanyId)
        {
            throw new UnauthorizedAccessException("Mailbox connections are scoped to the current tenant and user.");
        }

        // Provider callbacks are authenticated by protected OAuth state instead of an ambient user session.
        _companyContextAccessor.SetCompanyContext(new ResolvedCompanyMembershipContext(
            Guid.Empty,
            state.CompanyId,
            state.UserId,
            string.Empty,
            CompanyMembershipRole.Employee,
            CompanyMembershipStatus.Active));
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task EnqueueConnectedMailboxScanAsync(MailboxConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Purpose != MailboxPurpose.Finance)
        {
            _logger.LogInformation(
                "Mailbox connected without a finance bill scan. CompanyId: {CompanyId}. Purpose: {Purpose}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.Purpose,
                connection.Id);
            return;
        }

        try
        {
            await _connectedMailboxScanJobScheduler.EnqueueConnectedMailboxScanAsync(
                new ConnectedMailboxInboxScanJob(
                    connection.CompanyId,
                    connection.UserId,
                    connection.Id,
                    connection.Provider,
                    connection.Purpose),
                cancellationToken);

            _logger.LogInformation(
                "Laura mailbox scan task queued after mailbox connection. CompanyId: {CompanyId}. UserId: {UserId}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.UserId,
                connection.Provider,
                connection.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Mailbox connected, but Laura's automatic inbox scan could not be queued. CompanyId: {CompanyId}. UserId: {UserId}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.UserId,
                connection.Provider,
                connection.Id);
        }
    }

    public static IReadOnlyCollection<MailboxFolderSelection> NormalizeFolders(
        IReadOnlyCollection<MailboxFolderSelection>? folders,
        MailboxProvider provider)
    {
        var normalized = folders?.Select(x => x.Normalize()).Where(x => !string.IsNullOrWhiteSpace(x.ProviderFolderId)).ToArray();
        if (normalized is { Length: > 0 })
        {
            return normalized;
        }

        // Until folder configuration UI exists, default to inbox only instead of scanning all mail.
        return [new MailboxFolderSelection(provider == MailboxProvider.Gmail ? "INBOX" : "inbox", "Inbox")];
    }

    public static string BuildTokenPurpose(MailboxProvider provider, string tokenKind) =>
        $"mailbox:{provider.ToStorageValue()}:{tokenKind}";

    public async Task<MailboxConnectionStatusResult> DisconnectAsync(
        DisconnectMailboxConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        MailboxPurposeValues.EnsureSupported(command.Purpose, nameof(command.Purpose));

        var connections = await _dbContext.MailboxConnections
            .Where(x => x.CompanyId == command.CompanyId &&
                x.Purpose == command.Purpose &&
                x.Status != MailboxConnectionStatus.Disconnected)
            .ToArrayAsync(cancellationToken);

        foreach (var connection in connections)
        {
            var revocationOutcome = "not_supported";
            if (connection.Provider == MailboxProvider.Gmail)
            {
                try
                {
                    var encryptedToken = connection.EncryptedRefreshToken ?? connection.EncryptedAccessToken;
                    if (!string.IsNullOrWhiteSpace(encryptedToken))
                    {
                        var tokenKind = connection.EncryptedRefreshToken is null ? "access_token" : "refresh_token";
                        var token = _fieldEncryption.Decrypt(
                            connection.CompanyId,
                            BuildTokenPurpose(connection.Provider, tokenKind),
                            encryptedToken);
                        var result = await _providerRegistry.Resolve(connection.Provider).RevokeCredentialAsync(
                            new MailboxCredentialRevocationRequest(token, connection.ProfileKey),
                            cancellationToken);
                        revocationOutcome = result.Supported
                            ? result.Succeeded ? "revoked" : "provider_rejected"
                            : "not_supported";
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    revocationOutcome = "provider_unavailable";
                    _logger.LogWarning(
                        exception,
                        "Remote mailbox credential revocation failed; local erasure will continue. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                        connection.CompanyId,
                        connection.Id);
                }
            }

            connection.EraseCredentialMaterial(UtcNow());
            connection.SetStatus(MailboxConnectionStatus.Disconnected);
            connection.ProviderMetadata["lastCredentialRevocationOutcome"] = revocationOutcome;
        }

        if (connections.Length > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            foreach (var connection in connections)
            {
                await WriteMailboxAuditAsync(
                    command.CompanyId,
                    command.UserId,
                    "mailbox.connection.disconnected",
                    connection.Id,
                    AuditEventOutcomes.Succeeded,
                    "Mailbox disconnected and stored credential material erased.",
                    connection,
                    cancellationToken);
            }
            _logger.LogInformation(
                "Mailbox disconnected. CompanyId: {CompanyId}. UserId: {UserId}. Purpose: {Purpose}. ConnectionCount: {ConnectionCount}.",
                command.CompanyId,
                command.UserId,
                command.Purpose,
                connections.Length);
        }

        return await GetStatusAsync(
            new GetMailboxConnectionStatusQuery(command.CompanyId, command.UserId, command.Purpose),
            cancellationToken);
    }

    private Task WriteMailboxAuditAsync(
        Guid companyId,
        Guid userId,
        string action,
        Guid connectionId,
        string outcome,
        string summary,
        MailboxConnection connection,
        CancellationToken cancellationToken) =>
        _audit?.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.Human,
                userId,
                action,
                "mailbox_connection",
                connectionId.ToString("D"),
                outcome,
                summary,
                ["mailbox"],
                new Dictionary<string, string?>
                {
                    ["purpose"] = connection.Purpose.ToStorageValue(),
                    ["provider"] = connection.Provider.ToStorageValue(),
                    ["authenticationType"] = connection.AuthenticationType?.ToStorageValue(),
                    ["credentialRevocationOutcome"] = connection.ProviderMetadata.TryGetPropertyValue(
                        "lastCredentialRevocationOutcome",
                        out var revocationOutcome)
                            ? revocationOutcome?.GetValue<string>()
                            : null
                }),
            cancellationToken) ?? Task.CompletedTask;

    private sealed class NoOpConnectedMailboxInboxScanJobScheduler : IConnectedMailboxInboxScanJobScheduler
    {
        public static readonly NoOpConnectedMailboxInboxScanJobScheduler Instance = new();

        private NoOpConnectedMailboxInboxScanJobScheduler()
        {
        }

        public Task EnqueueConnectedMailboxScanAsync(ConnectedMailboxInboxScanJob job, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
