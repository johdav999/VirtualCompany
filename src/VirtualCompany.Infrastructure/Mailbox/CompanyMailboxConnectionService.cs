using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyMailboxConnectionService> _logger;

    public CompanyMailboxConnectionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        IMailboxOAuthStateProtector stateProtector,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        IManualInboxBillScanJobScheduler scanJobScheduler,
        TimeProvider timeProvider,
        ILogger<CompanyMailboxConnectionService> logger)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _stateProtector = stateProtector;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _scanJobScheduler = scanJobScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<MailboxOAuthStartResult> StartOAuthConnectionAsync(
        StartMailboxOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        var provider = _providerRegistry.Resolve(command.Provider);
        var now = UtcNow();
        var configuredFolders = NormalizeFolders(command.ConfiguredFolders, command.Provider);
        var state = _stateProtector.Protect(new MailboxOAuthState(
            command.CompanyId,
            command.UserId,
            command.Provider,
            configuredFolders,
            now.Add(OAuthStateTtl),
            command.ReturnUri));

        var authorizationUrl = provider.BuildAuthorizationUrl(new MailboxAuthorizationRequest(
            command.CompanyId,
            command.UserId,
            command.CallbackUri,
            state));

        _logger.LogInformation(
            "Mailbox OAuth start built. CompanyId: {CompanyId}. UserId: {UserId}. Provider: {Provider}.",
            command.CompanyId,
            command.UserId,
            command.Provider);

        return Task.FromResult(new MailboxOAuthStartResult(command.Provider, authorizationUrl));
    }

    public async Task<MailboxConnectionStatusResult> GetStatusAsync(
        GetMailboxConnectionStatusQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(query.CompanyId, query.UserId);

        var connection = await _dbContext.MailboxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.UserId == query.UserId)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            return new MailboxConnectionStatusResult(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                null);
        }

        var lastRun = await _dbContext.EmailIngestionRuns
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                x.TriggeredByUserId == query.UserId &&
                x.MailboxConnectionId == connection.Id)
            .OrderByDescending(x => x.StartedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new MailboxConnectionStatusResult(
            connection.Status == MailboxConnectionStatus.Active,
            connection.Id,
            connection.Provider.ToStorageValue(),
            connection.Status.ToStorageValue(),
            connection.EmailAddress,
            connection.DisplayName,
            connection.CreatedUtc,
            connection.LastSuccessfulScanUtc,
            connection.LastErrorSummary,
            connection.ConfiguredFolders.Select(x => new MailboxFolderSelectionSummary(x.ProviderFolderId, x.DisplayName)).ToArray(),
            lastRun is null ? null : new EmailIngestionRunSummary(
                lastRun.Id, lastRun.StartedUtc, lastRun.CompletedUtc, lastRun.Provider.ToStorageValue(),
                lastRun.ScanFromUtc, lastRun.ScanToUtc,
                lastRun.ScannedMessageCount,
                lastRun.DetectedCandidateCount,
                lastRun.NonCandidateMessageCount,
                lastRun.CandidateAttachmentSnapshotCount,
                lastRun.DeduplicatedAttachmentCount,
                lastRun.FailureDetails));
    }

    public async Task<IReadOnlyList<MailboxScannedMessageSummary>> GetScannedMessagesAsync(
        GetMailboxScannedMessagesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(query.CompanyId, query.UserId);

        var limit = Math.Clamp(query.Limit, 1, 100);
        var connectionIds = await _dbContext.MailboxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.UserId == query.UserId)
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

        if (command.ExpectedProvider.HasValue && state.Provider != command.ExpectedProvider.Value)
        {
            throw new UnauthorizedAccessException("Mailbox OAuth state provider did not match the callback endpoint.");
        }

        ResolveCompletionTenantUserFromState(state);
        var provider = _providerRegistry.Resolve(state.Provider);
        var tokenResult = await provider.ExchangeCodeAsync(
            new MailboxTokenExchangeRequest(command.Code, command.CallbackUri),
            cancellationToken);
        var profile = await provider.GetAccountProfileAsync(tokenResult.AccessToken, cancellationToken);
        var normalizedEmail = profile.EmailAddress.Trim().ToLowerInvariant();
        var existing = await _dbContext.MailboxConnections
            .SingleOrDefaultAsync(
                x => x.CompanyId == state.CompanyId &&
                    x.UserId == state.UserId &&
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
            now);

        connection.UpdateMailboxProfile(normalizedEmail, profile.DisplayName, profile.ProviderAccountId);
        connection.ConfigureFolders(state.ConfiguredFolders);
        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(state.CompanyId, BuildTokenPurpose(state.Provider, "access_token"), tokenResult.AccessToken),
            string.IsNullOrEmpty(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(state.CompanyId, BuildTokenPurpose(state.Provider, "refresh_token"), tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes);
        connection.SetStatus(MailboxConnectionStatus.Active);

        if (existing is null)
        {
            _dbContext.MailboxConnections.Add(connection);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Mailbox OAuth connection completed. CompanyId: {CompanyId}. UserId: {UserId}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
            state.CompanyId,
            state.UserId,
            state.Provider,
            connection.Id);

        return new MailboxOAuthCompletionResult(
            connection.Id,
            state.CompanyId,
            state.UserId,
            connection.Provider,
            connection.EmailAddress,
            connection.Status.ToStorageValue(),
            state.ReturnUri);
    }

    public async Task<ManualMailboxScanResult> TriggerManualScanAsync(
        TriggerManualMailboxScanCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        var query = _dbContext.MailboxConnections
            .Where(
                x => x.CompanyId == command.CompanyId &&
                    x.UserId == command.UserId);

        if (command.MailboxConnectionId.HasValue)
        {
            query = query.Where(x => x.Id == command.MailboxConnectionId.Value);
        }

        var connection = await query
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            throw new KeyNotFoundException("Mailbox connection was not found.");
        }

        if (connection.Status != MailboxConnectionStatus.Active)
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

        var completedRun = await _dbContext.EmailIngestionRuns
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == run.Id, cancellationToken);

        return new ManualMailboxScanResult(
            completedRun.Id,
            connection.Id,
            scanFromUtc,
            scanToUtc,
            completedRun.ScannedMessageCount,
            completedRun.DetectedCandidateCount,
            completedRun.NonCandidateMessageCount,
            completedRun.CandidateAttachmentSnapshotCount,
            completedRun.DeduplicatedAttachmentCount,
            completedRun.FailureDetails,
            completedRun.CompletedUtc.HasValue ? "completed" : "started");
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
}
