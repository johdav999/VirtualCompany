using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankConnectionService : IBankConnectionService
{
    private static readonly string[] DefaultCapabilities =
        [BankProviderCapabilities.Accounts, BankProviderCapabilities.AccountOwnership, BankProviderCapabilities.Transactions];
    private readonly VirtualCompanyDbContext _db;
    private readonly IBankConnectionProviderRegistry _providers;
    private readonly IBankConsentStateProtector _stateProtector;
    private readonly IProtectedBankCredentialStore _credentials;
    private readonly BankConnectionTelemetry _telemetry;
    private readonly TimeProvider _clock;

    public BankConnectionService(VirtualCompanyDbContext db, IBankConnectionProviderRegistry providers,
        IBankConsentStateProtector stateProtector, IProtectedBankCredentialStore credentials,
        BankConnectionTelemetry telemetry, TimeProvider? clock = null)
    { _db = db; _providers = providers; _stateProtector = stateProtector; _credentials = credentials; _telemetry = telemetry; _clock = clock ?? TimeProvider.System; }

    public async Task<BankConnectionStatusResult> GetStatusAsync(Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        var connections = await _db.BankConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.InstitutionName).Take(50).ToListAsync(cancellationToken);
        var connectionIds = connections.Select(x => x.Id).ToArray();
        var accounts = await _db.BankDiscoveredAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId)).OrderBy(x => x.DisplayName).Take(500).ToListAsync(cancellationToken);
        var accountIds = accounts.Select(x => x.Id).ToArray();
        var mappings = await _db.BankAccountMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsCurrent && accountIds.Contains(x.DiscoveredAccountId)).ToListAsync(cancellationToken);
        var mappedBankAccountIds = mappings.Select(x => x.CompanyBankAccountId).Distinct().ToArray();
        var mappedNames = await _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && mappedBankAccountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var consents = await _db.BankConsentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.ConnectionId)).OrderByDescending(x => x.Version).ToListAsync(cancellationToken);
        var currentConsentIds = consents.GroupBy(x => x.ConnectionId).ToDictionary(x => x.Key, x => x.First().Id);
        var consentIds = currentConsentIds.Values.ToArray();
        var grants = await _db.BankConnectionCapabilityGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && consentIds.Contains(x.ConsentVersionId)).ToListAsync(cancellationToken);
        var internalAccounts = await _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.IsActive).ThenBy(x => x.DisplayName).Take(500)
            .Select(x => new BankInternalAccountOption(x.Id, x.DisplayName, x.MaskedAccountNumber, x.Currency, x.IsActive)).ToListAsync(cancellationToken);

        var items = connections.Select(connection =>
        {
            var discovered = accounts.Where(x => x.ConnectionId == connection.Id).Select(account =>
            {
                var mapping = mappings.SingleOrDefault(x => x.DiscoveredAccountId == account.Id);
                return new BankDiscoveredAccountItem(account.Id, account.ProviderAccountId, account.DisplayName,
                    account.MaskedAccountNumber, account.Currency, account.OwnershipStatus, account.OwnershipSummary,
                    account.Version, mapping?.CompanyBankAccountId,
                    mapping is not null && mappedNames.TryGetValue(mapping.CompanyBankAccountId, out var name) ? name : null,
                    mapping?.Version);
            }).ToArray();
            var capabilities = currentConsentIds.TryGetValue(connection.Id, out var consentId)
                ? grants.Where(x => x.ConsentVersionId == consentId).Select(x => x.Capability).OrderBy(x => x).ToArray()
                : [];
            return new BankConnectionItem(connection.Id, connection.ProviderKey, connection.InstitutionId,
                connection.InstitutionName, EffectiveStatus(connection, consents.FirstOrDefault(x => x.ConnectionId == connection.Id)),
                connection.HealthStatus, EffectiveReasonCode(connection), EffectiveReasonSummary(connection),
                connection.ConsentExpiresUtc, connection.LastHealthCheckedUtc, connection.Version, capabilities, discovered);
        }).ToArray();
        return new BankConnectionStatusResult(_providers.GetProviders(), items, internalAccounts);
    }

    public Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(string providerKey, CancellationToken cancellationToken) =>
        _providers.GetRequired(providerKey).GetInstitutionsAsync(cancellationToken);

    public async Task<BankConsentSessionResult> StartAsync(StartBankConnectionCommand command, CancellationToken cancellationToken)
    {
        ValidateStart(command.CompanyId, command.ActorUserId, command.ProviderKey, command.InstitutionId, command.CallbackUri);
        var provider = _providers.GetRequired(command.ProviderKey);
        var institutions = await provider.GetInstitutionsAsync(cancellationToken);
        if (!institutions.Any(x => string.Equals(x.InstitutionId, command.InstitutionId, StringComparison.Ordinal)))
            throw new BankConnectionOperationException("institution_not_supported", "The selected institution is not supported by this provider.");
        var now = Now(); var sessionId = Guid.NewGuid(); var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expires = now.AddMinutes(10);
        var state = new BankConsentCallbackState(sessionId, command.CompanyId, command.ActorUserId,
            provider.Descriptor.ProviderKey, nonce, now, expires);
        var protectedState = _stateProtector.Protect(state);
        var session = new BankConsentSession(sessionId, command.CompanyId, null, provider.Descriptor.ProviderKey,
            command.InstitutionId, command.ActorUserId, Hash(protectedState), Hash(nonce), command.ReturnUri?.ToString(), false, expires, now);
        _db.BankConsentSessions.Add(session);
        var result = await ExecuteProviderAsync(command.CompanyId, null, "connect_started", command.CorrelationId,
            () => provider.StartConsentAsync(new BankProviderConsentStartRequest(command.CompanyId, sessionId,
                command.InstitutionId, protectedState, command.CallbackUri, false,
                NormalizeCapabilities(command.RequestedCapabilities)), cancellationToken));
        session.SetProviderSession(result.ProviderSessionReference);
        Audit(command.CompanyId, null, command.ActorUserId, "connect_started", "succeeded",
            "Bank consent authorization was started.", null, command.CorrelationId, null, BankConnectionStatuses.PendingConsent, now);
        await _db.SaveChangesAsync(cancellationToken);
        return new BankConsentSessionResult(session.Id, result.AuthorizationUri, Min(result.ExpiresUtc, expires));
    }

    public async Task<BankConsentCallbackResult> CompleteCallbackAsync(CompleteBankConsentCallbackCommand command, CancellationToken cancellationToken)
    {
        BankConsentCallbackState state;
        try { state = _stateProtector.Unprotect(command.ProtectedState); }
        catch (BankConnectionOperationException) { _telemetry.Operation(command.ExpectedCompanyId ?? Guid.Empty, null, "callback", "blocked", BankConnectionReasonCodes.CallbackStateInvalid, command.CorrelationId); throw; }
        if (state.ExpiresUtc <= Now() || !string.Equals(state.ProviderKey, command.ProviderKey, StringComparison.OrdinalIgnoreCase) ||
            command.ExpectedCompanyId.HasValue && command.ExpectedCompanyId.Value != state.CompanyId || state.UserId != command.ActorUserId)
        {
            _telemetry.Operation(command.ExpectedCompanyId ?? state.CompanyId, null, "callback", "blocked", BankConnectionReasonCodes.CallbackStateInvalid, command.CorrelationId);
            throw new BankConnectionOperationException(BankConnectionReasonCodes.CallbackStateInvalid, "Bank authorization state was invalid or expired.", true);
        }
        var session = await _db.BankConsentSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == state.CompanyId && x.Id == state.SessionId, cancellationToken)
            ?? throw new BankConnectionOperationException(BankConnectionReasonCodes.CallbackStateInvalid, "Bank authorization state was invalid or expired.", true);
        if (!FixedEquals(session.StateHash, Hash(command.ProtectedState)) || !FixedEquals(session.NonceHash, Hash(state.Nonce)))
            throw new BankConnectionOperationException(BankConnectionReasonCodes.CallbackStateInvalid, "Bank authorization state was invalid or expired.", true);
        if (session.ConsumedUtc.HasValue)
        {
            _telemetry.Operation(state.CompanyId, session.ConnectionId, "callback", "blocked", BankConnectionReasonCodes.CallbackReplay, command.CorrelationId);
            throw new BankConnectionOperationException(BankConnectionReasonCodes.CallbackReplay, "This bank authorization callback has already been used.", true);
        }
        var now = Now();
        try { session.Consume(now); }
        catch (InvalidOperationException) { throw new BankConnectionOperationException(BankConnectionReasonCodes.CallbackStateInvalid, "Bank authorization state was invalid or expired.", true); }
        await _db.SaveChangesAsync(cancellationToken); // One-time state is consumed before exchanging the provider code.

        var provider = _providers.GetRequired(state.ProviderKey);
        if (string.IsNullOrWhiteSpace(command.AuthorizationCode) && string.IsNullOrWhiteSpace(command.ProviderError))
            throw new BankConnectionOperationException("authorization_code_missing", "The bank did not return an authorization code.");
        var consent = await ExecuteProviderAsync(state.CompanyId, session.ConnectionId, "callback", command.CorrelationId,
            () => provider.CompleteConsentAsync(new BankProviderCallbackRequest(state.CompanyId, session.InstitutionId,
                command.AuthorizationCode ?? string.Empty, session.ProviderSessionReference, command.ProviderError), cancellationToken));
        var connection = session.ConnectionId.HasValue
            ? await LoadConnection(state.CompanyId, session.ConnectionId.Value, cancellationToken)
            : await _db.BankConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == state.CompanyId &&
                x.ProviderKey == state.ProviderKey && x.InstitutionId == session.InstitutionId, cancellationToken);
        if (connection is null)
        {
            connection = new BankConnection(Guid.NewGuid(), state.CompanyId, state.ProviderKey, session.InstitutionId,
                consent.InstitutionName, state.UserId, now);
            _db.BankConnections.Add(connection);
        }
        session.AttachConnection(connection.Id);
        var priorConsents = await _db.BankConsentVersions.IgnoreQueryFilters().Where(x => x.CompanyId == state.CompanyId && x.ConnectionId == connection.Id && x.Status == BankConsentStatuses.Active).ToListAsync(cancellationToken);
        foreach (var prior in priorConsents) prior.Supersede(now);
        var nextVersion = await _db.BankConsentVersions.IgnoreQueryFilters().Where(x => x.CompanyId == state.CompanyId && x.ConnectionId == connection.Id).Select(x => (int?)x.Version).MaxAsync(cancellationToken) is { } max ? max + 1 : 1;
        var consentVersion = new BankConsentVersion(Guid.NewGuid(), state.CompanyId, connection.Id, nextVersion,
            consent.ProviderConsentId, BankConsentStatuses.Active, now, consent.ConsentExpiresUtc, now);
        _db.BankConsentVersions.Add(consentVersion);
        foreach (var capability in NormalizeCapabilities(consent.GrantedCapabilities))
            _db.BankConnectionCapabilityGrants.Add(new BankConnectionCapabilityGrant(Guid.NewGuid(), state.CompanyId,
                connection.Id, consentVersion.Id, capability, now));
        await _credentials.StoreAsync(state.CompanyId, connection.Id, consent.Credentials, now, cancellationToken);

        IReadOnlyList<BankProviderDiscoveredAccount> discovered;
        try { discovered = await provider.DiscoverAccountsAsync(state.CompanyId, consent.ProviderConsentId, consent.Credentials, cancellationToken); }
        catch (BankProviderSafeException exception)
        {
            discovered = [];
            connection.MarkAttention(exception.ReasonCode, exception.SafeMessage, BankConnectionHealthStatuses.Outage, now);
        }
        await UpsertAccounts(state.CompanyId, connection.Id, discovered, now, cancellationToken);
        if (connection.Status != BankConnectionStatuses.AttentionRequired)
        {
            var mismatch = discovered.Any(x => x.OwnershipStatus == BankAccountOwnershipStatuses.Mismatch);
            if (mismatch) connection.MarkAttention(BankConnectionReasonCodes.OwnershipMismatch,
                "At least one discovered account did not pass the provider ownership check.", BankConnectionHealthStatuses.Healthy, now);
            else connection.Activate(consent.ConsentExpiresUtc, BankConnectionHealthStatuses.Healthy, now);
        }
        Audit(state.CompanyId, connection.Id, state.UserId, "consent_completed", "succeeded",
            "Bank consent was acknowledged and discovered accounts were retained for explicit mapping.", connection.ReasonCode,
            command.CorrelationId, BankConnectionStatuses.PendingConsent, connection.Status, now);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Operation(state.CompanyId, connection.Id, "callback", "succeeded", connection.ReasonCode, command.CorrelationId);
        return new BankConsentCallbackResult(state.CompanyId, connection.Id, connection.Status,
            string.IsNullOrWhiteSpace(session.ReturnUri) ? null : new Uri(session.ReturnUri, UriKind.Absolute));
    }

    public async Task<BankConsentSessionResult> RenewAsync(RenewBankConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await LoadConnection(command.CompanyId, command.ConnectionId, cancellationToken);
        EnsureVersion(connection, command.ExpectedVersion);
        if (connection.Status == BankConnectionStatuses.Disconnected)
            throw new BankConnectionOperationException(BankConnectionReasonCodes.Disconnected, "Reconnect this bank before renewing consent.");
        var provider = _providers.GetRequired(connection.ProviderKey); var now = Now(); var sessionId = Guid.NewGuid();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); var expires = now.AddMinutes(10);
        var protectedState = _stateProtector.Protect(new BankConsentCallbackState(sessionId, command.CompanyId,
            command.ActorUserId, connection.ProviderKey, nonce, now, expires));
        var session = new BankConsentSession(sessionId, command.CompanyId, connection.Id, connection.ProviderKey,
            connection.InstitutionId, command.ActorUserId, Hash(protectedState), Hash(nonce), command.ReturnUri?.ToString(), true, expires, now);
        _db.BankConsentSessions.Add(session);
        var capabilities = await CurrentCapabilities(command.CompanyId, connection.Id, cancellationToken);
        var result = await ExecuteProviderAsync(command.CompanyId, connection.Id, "renew_started", command.CorrelationId,
            () => provider.StartConsentAsync(new BankProviderConsentStartRequest(command.CompanyId, sessionId,
                connection.InstitutionId, protectedState, command.CallbackUri, true, capabilities), cancellationToken));
        session.SetProviderSession(result.ProviderSessionReference);
        Audit(command.CompanyId, connection.Id, command.ActorUserId, "renew_started", "succeeded", "Bank consent renewal was started.", null, command.CorrelationId, connection.Status, connection.Status, now);
        await _db.SaveChangesAsync(cancellationToken);
        return new BankConsentSessionResult(sessionId, result.AuthorizationUri, Min(result.ExpiresUtc, expires));
    }

    public async Task<BankAccountMappingResult> MapAccountAsync(MapDiscoveredBankAccountCommand command, CancellationToken cancellationToken)
    {
        var connection = await LoadConnection(command.CompanyId, command.ConnectionId, cancellationToken); EnsureVersion(connection, command.ExpectedConnectionVersion);
        var discovered = await _db.BankDiscoveredAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ConnectionId == connection.Id && x.Id == command.DiscoveredAccountId, cancellationToken)
            ?? throw new BankConnectionOperationException("discovered_account_not_found", "The discovered bank account was not found.");
        if (discovered.OwnershipStatus != BankAccountOwnershipStatuses.Verified)
            throw new BankConnectionOperationException(BankConnectionReasonCodes.OwnershipMismatch, "Account ownership must be verified before mapping.");
        var target = await _db.CompanyBankAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CompanyBankAccountId && x.IsActive, cancellationToken)
            ?? throw new BankConnectionOperationException("internal_account_not_found", "The selected internal bank account was not found or is inactive.");
        if (!string.Equals(discovered.Currency, target.Currency, StringComparison.OrdinalIgnoreCase))
            throw new BankConnectionOperationException("account_currency_mismatch", "The discovered and internal bank accounts must use the same currency.");
        var current = await _db.BankAccountMappings.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.DiscoveredAccountId == discovered.Id && x.IsCurrent).ToListAsync(cancellationToken);
        var before = current.SingleOrDefault(); foreach (var row in current) row.Supersede(Now());
        var next = await _db.BankAccountMappings.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.DiscoveredAccountId == discovered.Id).Select(x => (int?)x.Version).MaxAsync(cancellationToken) is { } max ? max + 1 : 1;
        var mapping = new BankAccountMapping(Guid.NewGuid(), command.CompanyId, discovered.Id, target.Id, next,
            command.ActorUserId, command.Reason, Now()); _db.BankAccountMappings.Add(mapping); connection.Touch(Now());
        Audit(command.CompanyId, connection.Id, command.ActorUserId, "account_mapped", "succeeded",
            "A discovered provider account was explicitly mapped to an internal bank account.", null, command.CorrelationId,
            before?.CompanyBankAccountId.ToString("D"), target.Id.ToString("D"), Now());
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(command.CompanyId, connection.Id, "map_account", "succeeded", null, command.CorrelationId);
        return new BankAccountMappingResult(mapping.Id, mapping.Version, connection.Version);
    }

    public async Task<BankConnectionStatusResult> RefreshAsync(RefreshBankConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await LoadConnection(command.CompanyId, command.ConnectionId, cancellationToken); EnsureVersion(connection, command.ExpectedVersion);
        var access = await EvaluateAccess(connection, cancellationToken);
        if (!access.Allowed)
        { _telemetry.Operation(command.CompanyId, connection.Id, "refresh", "blocked", access.ReasonCode, command.CorrelationId); throw new BankConnectionOperationException(access.ReasonCode!, access.Explanation); }
        var provider = _providers.GetRequired(connection.ProviderKey);
        var consent = await CurrentConsent(command.CompanyId, connection.Id, cancellationToken)
            ?? throw new BankConnectionOperationException(BankConnectionReasonCodes.MissingConsent, "Renew bank consent before refreshing accounts.");
        var credential = await _credentials.GetAsync(command.CompanyId, connection.Id, cancellationToken)
            ?? throw new BankConnectionOperationException(BankConnectionReasonCodes.MissingConsent, "Renew bank consent before refreshing accounts.");
        try
        {
            var health = await provider.GetHealthAsync(command.CompanyId, consent.ProviderConsentId, credential, cancellationToken);
            connection.RecordHealth(health.HealthStatus, health.ReasonCode, health.SafeSummary, Now());
            if (health.HealthStatus == BankConnectionHealthStatuses.Outage)
                throw new BankConnectionOperationException(BankConnectionReasonCodes.ProviderOutage, health.SafeSummary ?? "The bank provider is currently unavailable.");
            var accounts = await provider.DiscoverAccountsAsync(command.CompanyId, consent.ProviderConsentId, credential, cancellationToken);
            await UpsertAccounts(command.CompanyId, connection.Id, accounts, Now(), cancellationToken);
            if (accounts.Any(x => x.OwnershipStatus == BankAccountOwnershipStatuses.Mismatch))
                connection.MarkAttention(BankConnectionReasonCodes.OwnershipMismatch, "At least one discovered account did not pass the provider ownership check.", health.HealthStatus, Now());
            Audit(command.CompanyId, connection.Id, command.ActorUserId, "accounts_refreshed", "succeeded", "Bank connection health and discovered accounts were refreshed.", connection.ReasonCode, command.CorrelationId, null, connection.Status, Now());
            await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(command.CompanyId, connection.Id, "refresh", "succeeded", connection.ReasonCode, command.CorrelationId);
            return await GetStatusAsync(command.CompanyId, cancellationToken);
        }
        catch (BankProviderSafeException exception)
        {
            connection.MarkAttention(exception.ReasonCode, exception.SafeMessage, BankConnectionHealthStatuses.Outage, Now());
            Audit(command.CompanyId, connection.Id, command.ActorUserId, "accounts_refreshed", "failed", exception.SafeMessage, exception.ReasonCode, command.CorrelationId, null, connection.Status, Now());
            await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(command.CompanyId, connection.Id, "refresh", "failed", exception.ReasonCode, command.CorrelationId);
            throw new BankConnectionOperationException(exception.ReasonCode, exception.SafeMessage);
        }
    }

    public async Task SuspendAsync(ChangeBankConnectionStateCommand command, CancellationToken cancellationToken)
    {
        var connection = await LoadConnection(command.CompanyId, command.ConnectionId, cancellationToken); EnsureVersion(connection, command.ExpectedVersion); var before = connection.Status;
        connection.Suspend(string.IsNullOrWhiteSpace(command.Reason) ? "Bank access was suspended by a finance administrator." : command.Reason, Now());
        Audit(command.CompanyId, connection.Id, command.ActorUserId, "connection_suspended", "succeeded", connection.ReasonSummary!, connection.ReasonCode, command.CorrelationId, before, connection.Status, Now());
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(command.CompanyId, connection.Id, "suspend", "succeeded", connection.ReasonCode, command.CorrelationId);
    }

    public async Task DisconnectAsync(ChangeBankConnectionStateCommand command, CancellationToken cancellationToken)
    {
        var connection = await LoadConnection(command.CompanyId, command.ConnectionId, cancellationToken); EnsureVersion(connection, command.ExpectedVersion); var before = connection.Status; var now = Now();
        connection.Disconnect(string.IsNullOrWhiteSpace(command.Reason) ? "Bank connection was disconnected by a finance administrator." : command.Reason, now);
        var consent = await CurrentConsent(command.CompanyId, connection.Id, cancellationToken);
        if (consent is not null)
        {
            consent.Revoke(now);
            if (!await _db.BankConsentRevocationTasks.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == command.CompanyId && x.ConsentVersionId == consent.Id && x.Status != "completed", cancellationToken))
                _db.BankConsentRevocationTasks.Add(new BankConsentRevocationTask(Guid.NewGuid(), command.CompanyId, connection.Id, consent.Id, now));
        }
        Audit(command.CompanyId, connection.Id, command.ActorUserId, "connection_disconnected", "succeeded", connection.ReasonSummary!, connection.ReasonCode, command.CorrelationId, before, connection.Status, now);
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Operation(command.CompanyId, connection.Id, "disconnect", "succeeded", connection.ReasonCode, command.CorrelationId);
    }

    public async Task<BankSynchronizationAccessResult> GetSynchronizationAccessAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken) =>
        await EvaluateAccess(await LoadConnection(companyId, connectionId, cancellationToken), cancellationToken);

    private async Task<BankSynchronizationAccessResult> EvaluateAccess(BankConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Status == BankConnectionStatuses.Suspended) return Block(BankConnectionReasonCodes.Suspended, "Resume or renew this suspended bank connection before synchronization.", false);
        if (connection.Status == BankConnectionStatuses.Revoked) return Block(BankConnectionReasonCodes.Revoked, "Reconnect the bank because consent has been revoked.", true);
        if (connection.Status == BankConnectionStatuses.Disconnected) return Block(BankConnectionReasonCodes.Disconnected, "Reconnect the bank before synchronization.", true);
        var consent = await CurrentConsent(connection.CompanyId, connection.Id, cancellationToken);
        if (consent is null) return Block(BankConnectionReasonCodes.MissingConsent, "Connect the bank and grant consent before synchronization.", true);
        if (consent.ExpiresUtc.HasValue && consent.ExpiresUtc.Value <= Now()) return Block(BankConnectionReasonCodes.ExpiredConsent, "Bank consent expired. Renew consent before synchronization.", true);
        var capabilities = await CurrentCapabilities(connection.CompanyId, connection.Id, cancellationToken);
        if (!capabilities.Contains(BankProviderCapabilities.Accounts) || !capabilities.Contains(BankProviderCapabilities.Transactions))
            return Block(BankConnectionReasonCodes.ScopeLoss, "Bank consent no longer grants the account and transaction scopes required for synchronization.", true);
        if (await _db.BankDiscoveredAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == connection.CompanyId && x.ConnectionId == connection.Id && x.OwnershipStatus == BankAccountOwnershipStatuses.Mismatch, cancellationToken))
            return Block(BankConnectionReasonCodes.OwnershipMismatch, "Resolve the account ownership mismatch before synchronization.", false);
        return new BankSynchronizationAccessResult(true, null, "Bank consent is current and synchronization may contact the provider.", false);
    }

    private static BankSynchronizationAccessResult Block(string code, string explanation, bool renewal) => new(false, code, explanation, renewal);
    private async Task<BankConnection> LoadConnection(Guid companyId, Guid connectionId, CancellationToken cancellationToken) =>
        await _db.BankConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken)
        ?? throw new BankConnectionOperationException("bank_connection_not_found", "The bank connection was not found.");
    private Task<BankConsentVersion?> CurrentConsent(Guid companyId, Guid connectionId, CancellationToken cancellationToken) =>
        _db.BankConsentVersions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.Status == BankConsentStatuses.Active).OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
    private async Task<IReadOnlyList<string>> CurrentCapabilities(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        var consent = await CurrentConsent(companyId, connectionId, cancellationToken); if (consent is null) return [];
        return await _db.BankConnectionCapabilityGrants.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.ConsentVersionId == consent.Id).Select(x => x.Capability).ToListAsync(cancellationToken);
    }
    private async Task UpsertAccounts(Guid companyId, Guid connectionId, IReadOnlyList<BankProviderDiscoveredAccount> accounts, DateTime now, CancellationToken cancellationToken)
    {
        var existing = await _db.BankDiscoveredAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ConnectionId == connectionId).ToDictionaryAsync(x => x.ProviderAccountId, StringComparer.Ordinal, cancellationToken);
        foreach (var account in accounts)
        {
            if (existing.TryGetValue(account.ProviderAccountId, out var row)) row.Refresh(account.DisplayName, account.MaskedAccountNumber, account.Currency, account.OwnershipStatus, account.OwnershipSummary, now, account.ProviderAccessReference);
            else _db.BankDiscoveredAccounts.Add(new BankDiscoveredAccount(Guid.NewGuid(), companyId, connectionId, account.ProviderAccountId, account.DisplayName, account.MaskedAccountNumber, account.Currency, account.OwnershipStatus, account.OwnershipSummary, now, account.ProviderAccessReference));
        }
    }
    private void Audit(Guid companyId, Guid? connectionId, Guid actor, string type, string outcome, string summary,
        string? reason, string? correlation, string? before, string? after, DateTime now) =>
        _db.BankConnectionAuditEvents.Add(new BankConnectionAuditEvent(Guid.NewGuid(), companyId, connectionId, actor, type, outcome, summary, reason, correlation, before, after, now));
    private async Task<T> ExecuteProviderAsync<T>(Guid companyId, Guid? connectionId, string operation, string? correlationId, Func<Task<T>> action)
    {
        try { return await action(); }
        catch (BankProviderSafeException exception) { _telemetry.Operation(companyId, connectionId, operation, "failed", exception.ReasonCode, correlationId); throw new BankConnectionOperationException(exception.ReasonCode, exception.SafeMessage); }
    }
    private string EffectiveStatus(BankConnection connection, BankConsentVersion? consent) =>
        consent?.ExpiresUtc is { } expiry && expiry <= Now() && connection.Status == BankConnectionStatuses.Active ? BankConnectionStatuses.AttentionRequired : connection.Status;
    private string? EffectiveReasonCode(BankConnection connection) => connection.ConsentExpiresUtc is { } expiry && expiry <= Now() && connection.Status == BankConnectionStatuses.Active ? BankConnectionReasonCodes.ExpiredConsent : connection.ReasonCode;
    private string? EffectiveReasonSummary(BankConnection connection) => connection.ConsentExpiresUtc is { } expiry && expiry <= Now() && connection.Status == BankConnectionStatuses.Active ? "Bank consent expired. Renew consent before synchronization." : connection.ReasonSummary;
    private static IReadOnlyList<string> NormalizeCapabilities(IReadOnlyCollection<string> values) => (values.Count == 0 ? DefaultCapabilities : values).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();
    private static void ValidateStart(Guid companyId, Guid userId, string providerKey, string institutionId, Uri callbackUri) { Require(companyId, nameof(companyId)); Require(userId, nameof(userId)); if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(institutionId)) throw new ArgumentException("Provider and institution are required."); if (!callbackUri.IsAbsoluteUri || callbackUri.Scheme is not ("http" or "https")) throw new ArgumentException("A valid callback URI is required."); }
    private static void EnsureVersion(BankConnection connection, long expectedVersion) { try { connection.EnsureVersion(expectedVersion); } catch (InvalidOperationException) { throw new BankConnectionOperationException(BankConnectionReasonCodes.ConcurrencyConflict, "The bank connection changed. Reload it before continuing.", true); } }
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
    private static void Require(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
}
