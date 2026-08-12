using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class CalendarConnectionService : ICalendarConnectionService
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly ICalendarOAuthStateProtector _stateProtector;
    private readonly IMailboxOAuthReplayGuard _replayGuard;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _encryption;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CalendarConnectionService> _logger;

    public CalendarConnectionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContext,
        ICalendarOAuthStateProtector stateProtector,
        IMailboxOAuthReplayGuard replayGuard,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService encryption,
        TimeProvider timeProvider,
        ILogger<CalendarConnectionService> logger)
    {
        _dbContext = dbContext;
        _companyContext = companyContext;
        _stateProtector = stateProtector;
        _replayGuard = replayGuard;
        _providerRegistry = providerRegistry;
        _encryption = encryption;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CalendarOAuthStartResult> StartOAuthConnectionAsync(
        StartCalendarOAuthConnectionCommand command, CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        var mailboxProvider = ToMailboxProvider(command.Provider);
        IReadOnlyCollection<string> requestedScopes = CalendarOAuthScopes.For(command.Provider);
        var existingScopeSets = await _dbContext.ExternalAccountConnections
            .Where(x => x.CompanyId == command.CompanyId &&
                x.UserId == command.UserId &&
                x.Provider == command.Provider &&
                x.Status != ExternalConnectionStatus.Disconnected)
            .Select(x => x.GrantedScopes)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (existingScopeSets.Length == 1)
        {
            requestedScopes = requestedScopes
                .Concat(existingScopeSets[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        var now = UtcNow();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var state = _stateProtector.Protect(new CalendarOAuthState(
            command.CompanyId, command.UserId, command.Provider,
            now.Add(StateTtl), command.ReturnUri, nonce, requestedScopes));
        await _replayGuard.RegisterAsync(
            command.CompanyId, command.UserId, MailboxPurpose.Sales,
            mailboxProvider, nonce, now.Add(StateTtl), cancellationToken);
        var authorizationUrl = _providerRegistry.Resolve(mailboxProvider).BuildAuthorizationUrl(
            new MailboxAuthorizationRequest(
                command.CompanyId, command.UserId, command.CallbackUri,
                state, RequestedScopes: requestedScopes));
        return new CalendarOAuthStartResult(command.Provider, authorizationUrl);
    }

    public async Task<CalendarOAuthCompletionResult> CompleteOAuthConnectionAsync(
        CompleteCalendarOAuthConnectionCommand command, CancellationToken cancellationToken)
    {
        var state = _stateProtector.Unprotect(command.State);
        if (state.ExpiresUtc <= UtcNow()) throw new InvalidOperationException("Calendar OAuth state has expired.");
        if (command.ExpectedProvider.HasValue && command.ExpectedProvider.Value != state.Provider)
            throw new UnauthorizedAccessException("Calendar OAuth state provider did not match the callback endpoint.");
        ResolveCompletionTenantUser(state);
        var mailboxProvider = ToMailboxProvider(state.Provider);
        if (!await _replayGuard.TryConsumeAsync(
                state.CompanyId, state.UserId, MailboxPurpose.Sales,
                mailboxProvider, state.Nonce, UtcNow(), cancellationToken))
            throw new UnauthorizedAccessException("Calendar OAuth state was already used or has expired.");

        var provider = _providerRegistry.Resolve(mailboxProvider);
        var token = await provider.ExchangeCodeAsync(
            new MailboxTokenExchangeRequest(
                command.Code, command.CallbackUri,
                RequestedScopes: state.RequestedScopes), cancellationToken);
        var profile = await provider.GetExternalAccountProfileAsync(token.AccessToken, cancellationToken);
        var email = profile.EmailAddress.Trim().ToLowerInvariant();
        var external = await _dbContext.ExternalAccountConnections
            .SingleOrDefaultAsync(x => x.CompanyId == state.CompanyId &&
                x.Provider == state.Provider && x.AccountEmail == email, cancellationToken);
        if (external is null)
        {
            var id = Guid.NewGuid();
            external = new ExternalAccountConnection(
                id, state.CompanyId, state.UserId, state.Provider,
                email, profile.DisplayName, profile.ProviderAccountId,
                $"external-account:{id:N}", UtcNow());
            _dbContext.ExternalAccountConnections.Add(external);
        }
        external.UpdateProfile(email, profile.DisplayName, profile.ProviderAccountId);
        external.StoreEncryptedCredentials(
            _encryption.Encrypt(state.CompanyId, external.CredentialPurpose("access_token"), token.AccessToken),
            string.IsNullOrWhiteSpace(token.RefreshToken)
                ? external.EncryptedRefreshToken
                : _encryption.Encrypt(state.CompanyId, external.CredentialPurpose("refresh_token"), token.RefreshToken),
            token.AccessTokenExpiresUtc,
            token.GrantedScopes.Count == 0
                ? state.RequestedScopes
                : token.GrantedScopes.Concat(state.RequestedScopes)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        external.SetStatus(ExternalConnectionStatus.Active);

        var calendar = await _dbContext.CalendarConnections
            .SingleOrDefaultAsync(x => x.CompanyId == state.CompanyId &&
                x.ExternalAccountConnectionId == external.Id && x.CalendarId == "primary", cancellationToken);
        if (calendar is null)
        {
            calendar = new CalendarConnection(
                Guid.NewGuid(), state.CompanyId, state.UserId, external.Id,
                state.Provider, email, profile.DisplayName, createdUtc: UtcNow());
            _dbContext.CalendarConnections.Add(calendar);
        }
        calendar.UpdateProfile(email, profile.DisplayName, "primary", null);
        calendar.SetStatus(ExternalConnectionStatus.Active);
        AddAudit(state.CompanyId, state.UserId, "calendar.connection.connected", calendar.Id,
            "Calendar connection authorized independently from email inboxes.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Calendar connection completed. CompanyId: {CompanyId}. Provider: {Provider}. CalendarConnectionId: {ConnectionId}.",
            state.CompanyId, state.Provider, calendar.Id);
        return new CalendarOAuthCompletionResult(
            calendar.Id, state.CompanyId, state.UserId, state.Provider,
            calendar.AccountEmail, calendar.Status.ToStorageValue(), state.ReturnUri);
    }

    public async Task<IReadOnlyList<CalendarConnectionSummary>> ListAsync(
        Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(companyId, userId);
        var connections = await _dbContext.CalendarConnections.AsNoTracking()
            .Include(x => x.ExternalAccountConnection)
            .Where(x => x.CompanyId == companyId && x.UserId == userId)
            .OrderByDescending(x => x.Status == ExternalConnectionStatus.Active)
            .ThenBy(x => x.AccountEmail)
            .ToArrayAsync(cancellationToken);
        return connections.Select(Map).ToArray();
    }

    public async Task<CalendarConnectionSummary> DisconnectAsync(
        Guid companyId, Guid userId, Guid calendarConnectionId, CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(companyId, userId);
        var connection = await _dbContext.CalendarConnections
            .Include(x => x.ExternalAccountConnection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Id == calendarConnectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Calendar connection not found.");
        connection.SetStatus(ExternalConnectionStatus.Disconnected);
        AddAudit(companyId, userId, "calendar.connection.disconnected", connection.Id,
            "Calendar access was disconnected without changing any email inbox connection.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(connection);
    }

    private CalendarConnectionSummary Map(CalendarConnection connection)
    {
        var required = CalendarOAuthScopes.CalendarOnly(connection.Provider);
        var hasScopes = required.All(scope => connection.ExternalAccountConnection.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));
        return new CalendarConnectionSummary(
            connection.Id, connection.Provider, connection.AccountEmail,
            connection.DisplayName, connection.CalendarId, connection.TimeZoneId,
            connection.Capabilities, connection.Status, hasScopes,
            !hasScopes || connection.Status != ExternalConnectionStatus.Active ||
                connection.ExternalAccountConnection.Status != ExternalConnectionStatus.Active,
            connection.LastHealthCheckUtc, connection.LastErrorSummary);
    }

    private void ResolveCompletionTenantUser(CalendarOAuthState state)
    {
        if (_companyContext.IsResolved)
        {
            EnsureCurrentTenantUser(state.CompanyId, state.UserId);
            return;
        }
        if (_companyContext.CompanyId.HasValue && _companyContext.CompanyId.Value != state.CompanyId)
            throw new UnauthorizedAccessException("Calendar connections are scoped to the current company and user.");
        _companyContext.SetCompanyContext(new ResolvedCompanyMembershipContext(
            Guid.Empty, state.CompanyId, state.UserId, string.Empty,
            CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
    }

    private void EnsureCurrentTenantUser(Guid companyId, Guid userId)
    {
        if (!_companyContext.IsResolved || _companyContext.CompanyId != companyId || _companyContext.UserId != userId)
            throw new UnauthorizedAccessException("Calendar connections are scoped to the current company and user.");
    }

    private void AddAudit(Guid companyId, Guid userId, string action, Guid targetId, string rationale) =>
        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
            action, "calendar_connection", targetId.ToString("D"),
            AuditEventOutcomes.Succeeded, rationale, ["calendar connection"],
            new Dictionary<string, string?>(), targetId.ToString("D")));

    private static MailboxProvider ToMailboxProvider(ExternalAccountProvider provider) => provider switch
    {
        ExternalAccountProvider.Google => MailboxProvider.Gmail,
        ExternalAccountProvider.Microsoft365 => MailboxProvider.Microsoft365,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported calendar provider.")
    };

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
