using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOAuthService : IFortnoxOAuthService
{
    private static readonly TimeSpan OAuthStateTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshLockTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshLockWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshLockPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IFortnoxOAuthSessionStore _sessionStore;
    private readonly IFortnoxTokenStore _tokenStore;
    private readonly FortnoxOAuthClient _client;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FortnoxOAuthService> _logger;
    private readonly IFortnoxIntegrationDiagnostics? _diagnostics;
    private readonly IAuditEventWriter _auditEventWriter;

    public FortnoxOAuthService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IFortnoxOAuthStateProtector stateProtector,
        IFortnoxOAuthSessionStore sessionStore,
        IFortnoxTokenStore tokenStore,
        FortnoxOAuthClient client,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider,
        ILogger<FortnoxOAuthService> logger,
        IAuditEventWriter auditEventWriter,
        IFortnoxIntegrationDiagnostics? diagnostics = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _sessionStore = sessionStore;
        _tokenStore = tokenStore;
        _client = client;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider;
        _logger = logger;
        _auditEventWriter = auditEventWriter;
        _diagnostics = diagnostics;
    }

    public async Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        var now = UtcNow();
        var nonce = CreateNonce();
        var state = new FortnoxOAuthState(
            command.CompanyId,
            command.UserId,
            nonce,
            now,
            now.Add(OAuthStateTtl),
            command.Reconnect,
            command.ReturnUri);
        var stateHandle = await _sessionStore.CreateAsync(state, OAuthStateTtl, cancellationToken);

        var authorizationUrl = await _client.BuildAuthorizationUrlAsync(
            stateHandle,
            nonce,
            cancellationToken);
        _logger.LogInformation(
            "Fortnox OAuth authorization URL created. CompanyId: {CompanyId}. UserId: {UserId}. Reconnect: {Reconnect}.",
            command.CompanyId,
            command.UserId,
            command.Reconnect);

        return new FortnoxOAuthStartResult(authorizationUrl, now.Add(OAuthStateTtl));
    }

    public async Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(
        CompleteFortnoxOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        FortnoxOAuthState? state = null;
        var receivedUtc = UtcNow();
        try
        {
            if (string.IsNullOrWhiteSpace(command.State))
            {
                throw new FortnoxOAuthException("Fortnox authorization state was missing.");
            }

            state = command.CompanyId == Guid.Empty
                ? await _sessionStore.GetAsync(command.State, cancellationToken)
                : await _sessionStore.GetAsync(command.CompanyId, command.State, cancellationToken);
            ValidateCallbackState(command, state);
            await ResolveCallbackCompanyContextAsync(command, state, cancellationToken);
            EnsureCallbackTenantUser(command, state);

            if (!string.IsNullOrWhiteSpace(command.ProviderError))
            {
                throw new FortnoxOAuthException(FormatProviderError(command.ProviderError));
            }

            if (string.IsNullOrWhiteSpace(command.Code))
            {
                throw new FortnoxOAuthException("Fortnox did not return an authorization code.");
            }

            FortnoxOAuthTokenResult tokenResult;
            try
            {
                tokenResult = await _client.ExchangeCodeAsync(command.Code, cancellationToken);
            }
            catch (FortnoxOAuthException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new FortnoxOAuthException("Fortnox authorization is temporarily unavailable. Try again later.", isTransient: true);
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            var connection = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _sessionStore.MarkConsumedAsync(state.CompanyId, command.State, null, receivedUtc, cancellationToken);
                var persistedConnection = await _tokenStore.UpsertConnectedAsync(state.CompanyId, state.UserId, tokenResult, UtcNow(), cancellationToken);
                await _sessionStore.AttachConnectionAsync(state.CompanyId, command.State, persistedConnection.ConnectionId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return persistedConnection;
            });

            _logger.LogInformation(
                "Fortnox OAuth connection completed. CompanyId: {CompanyId}. UserId: {UserId}. ConnectionId: {ConnectionId}.",
                state.CompanyId,
                state.UserId,
                connection.ConnectionId);

            return new FortnoxOAuthCompletionResult(
                connection.ConnectionId,
                state.CompanyId,
                connection.Status,
                state.ReturnUri);
        }
        catch (Exception ex) when (ex is FortnoxOAuthException or UnauthorizedAccessException or ArgumentException)
        {
            if (!string.IsNullOrWhiteSpace(command.State))
            {
                var failureCompanyId = state?.CompanyId ?? command.CompanyId;
                if (failureCompanyId != Guid.Empty)
                {
                    await SafeMarkCallbackFailedAsync(failureCompanyId, command.State, ToSafeCallbackFailureReason(ex), receivedUtc, cancellationToken);
                }
            }

            throw;
        }
    }

    public async Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(
        RefreshFortnoxAccessTokenCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _tokenStore.GetAsync(command.CompanyId, command.ConnectionId, cancellationToken);
        var usable = command.ForceRefresh ? null : TryUseExistingToken(existing);
        if (usable is not null)
        {
            return usable;
        }

        if (existing is null)
        {
            return FortnoxAccessTokenResult.ReconnectRequired("Fortnox is not connected.");
        }

        if (IsReconnectStatus(existing.Status))
        {
            return FortnoxAccessTokenResult.ReconnectRequired("Fortnox needs to be reconnected.");
        }

        var lockKey = $"fortnox-refresh:{command.CompanyId:N}:{existing.ConnectionId:N}";
        await using var handle = await _lockProvider.TryAcquireAsync(lockKey, RefreshLockTtl, cancellationToken);
        if (handle is null)
        {
            return await WaitForConcurrentRefreshAsync(command, cancellationToken);
        }

        var afterLock = await _tokenStore.GetAsync(command.CompanyId, existing.ConnectionId, cancellationToken);
        usable = command.ForceRefresh ? null : TryUseExistingToken(afterLock);
        if (usable is not null)
        {
            return usable;
        }

        return afterLock is null
            ? FortnoxAccessTokenResult.ReconnectRequired("Fortnox is not connected.")
            : await RefreshAsync(afterLock, cancellationToken);
    }

    private FortnoxAccessTokenResult? TryUseExistingToken(FortnoxTokenSnapshot? connection)
    {
        if (connection is null)
        {
            return null;
        }

        if (IsReconnectStatus(connection.Status))
        {
            return FortnoxAccessTokenResult.ReconnectRequired("Fortnox needs to be reconnected.");
        }

        var now = UtcNow();
        if (!string.IsNullOrWhiteSpace(connection.AccessToken) &&
            (!connection.AccessTokenExpiresUtc.HasValue || connection.AccessTokenExpiresUtc.Value > now.Add(RefreshSkew)))
        {
            return FortnoxAccessTokenResult.Success(connection.AccessToken, connection.AccessTokenExpiresUtc);
        }

        return null;
    }

    private async Task<FortnoxAccessTokenResult> RefreshAsync(FortnoxTokenSnapshot connection, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var started = Stopwatch.GetTimestamp();
        _diagnostics?.TokenRefreshStarted(connection.CompanyId, connection.ConnectionId);
        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
        {
            await _tokenStore.MarkAsync(connection.CompanyId, connection.ConnectionId, FortnoxConnectionStatusValues.NeedsReconnect, "Fortnox needs to be reconnected.", now, cancellationToken);
            return FortnoxAccessTokenResult.ReconnectRequired("Fortnox needs to be reconnected.");
        }

        try
        {
            var tokenResult = await _client.RefreshTokenAsync(connection.RefreshToken, cancellationToken);
            await _tokenStore.StoreRefreshResultAsync(connection.CompanyId, connection.ConnectionId, tokenResult, now, cancellationToken);

            _diagnostics?.TokenRefreshCompleted(connection.CompanyId, connection.ConnectionId, succeeded: true, needsReconnect: false, Stopwatch.GetElapsedTime(started));
            return FortnoxAccessTokenResult.Success(tokenResult.AccessToken, tokenResult.AccessTokenExpiresUtc);
        }
        catch (FortnoxOAuthException ex) when (ex.RequiresReconnect)
        {
            await _tokenStore.MarkAsync(connection.CompanyId, connection.ConnectionId, FortnoxConnectionStatusValues.NeedsReconnect, ex.SafeMessage, now, cancellationToken);
            _logger.LogWarning(
                "Fortnox refresh token rejected. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.ConnectionId);
            _diagnostics?.TokenRefreshFailed(connection.CompanyId, connection.ConnectionId, ex.SafeMessage, needsReconnect: true, Stopwatch.GetElapsedTime(started));
            return FortnoxAccessTokenResult.ReconnectRequired(ex.SafeMessage);
        }
        catch (Exception ex) when (ex is FortnoxOAuthException or HttpRequestException or TaskCanceledException)
        {
            await _tokenStore.MarkAsync(connection.CompanyId, connection.ConnectionId, FortnoxConnectionStatusValues.NeedsReconnect, "Fortnox token refresh failed. Reconnect Fortnox to continue.", now, cancellationToken);
            _logger.LogWarning(
                "Fortnox token refresh failed without exposing token material. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.ConnectionId);
            _diagnostics?.TokenRefreshFailed(connection.CompanyId, connection.ConnectionId, "Fortnox token refresh failed. The job will retry later.", needsReconnect: false, Stopwatch.GetElapsedTime(started));
            return FortnoxAccessTokenResult.TransientFailure("Fortnox token refresh failed. The job will retry later.");
        }
    }

    private async Task<FortnoxAccessTokenResult> WaitForConcurrentRefreshAsync(
        RefreshFortnoxAccessTokenCommand command,
        CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow().Add(RefreshLockWait);
        while (_timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(RefreshLockPoll, cancellationToken);
            var refreshed = await _tokenStore.GetAsync(command.CompanyId, command.ConnectionId, cancellationToken);
            var usable = TryUseExistingToken(refreshed);
            if (usable is not null)
            {
                return usable;
            }
        }

        _logger.LogWarning(
            "Fortnox token refresh coordination timed out. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
            command.CompanyId,
            command.ConnectionId);
        return FortnoxAccessTokenResult.TransientFailure("Fortnox token refresh is already in progress. The job will retry later.");
    }

    public async Task<FortnoxConnectionStatusResult> GetStatusAsync(
        GetFortnoxConnectionStatusQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(query.CompanyId, query.UserId);
        var connection = await _tokenStore.GetStatusAsync(query.CompanyId, null, cancellationToken);

        if (connection is null)
        {
            return new FortnoxConnectionStatusResult(false, null, null, null, null, null, null, null);
        }

        return new FortnoxConnectionStatusResult(
            connection.Status == FortnoxConnectionStatusValues.Connected,
            connection.ConnectionId,
            connection.Status,
            connection.ConnectedUtc,
            connection.AccessTokenExpiresUtc,
            connection.LastRefreshAttemptUtc,
            connection.LastErrorSummary,
            connection.LastSuccessfulSyncUtc);
    }

    public async Task MarkNeedsReconnectAsync(
        Guid companyId,
        Guid connectionId,
        string safeReason,
        CancellationToken cancellationToken)
    {
        await _tokenStore.MarkAsync(companyId, connectionId, FortnoxConnectionStatusValues.NeedsReconnect, safeReason, UtcNow(), cancellationToken);
    }

    public async Task<FortnoxConnectionDisconnectResult> DisconnectAsync(
        DisconnectFortnoxConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCurrentTenantUser(command.CompanyId, command.UserId);
        var now = UtcNow();
        var disconnected = await _tokenStore.DisconnectAsync(command.CompanyId, now, cancellationToken);

        _logger.LogInformation(
            "Fortnox connection disconnected. CompanyId: {CompanyId}. UserId: {UserId}. ConnectionId: {ConnectionId}.",
            command.CompanyId,
            command.UserId,
            disconnected?.ConnectionId);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                command.CompanyId,
                AuditActorTypes.User,
                command.UserId,
                AuditEventActions.IntegrationConnectionDisconnected,
                AuditTargetTypes.IntegrationConnection,
                disconnected?.ConnectionId.ToString("D") ?? "fortnox",
                AuditEventOutcomes.Succeeded,
                "Fortnox was disconnected by a company administrator.",
                DataSources: ["Fortnox connection settings"],
                Metadata: new Dictionary<string, string?>
                {
                    ["provider"] = "Fortnox",
                    ["connectionId"] = disconnected?.ConnectionId.ToString("D"),
                    ["status"] = FortnoxConnectionStatusValues.Disconnected
                },
                OccurredUtc: now),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FortnoxConnectionDisconnectResult(
            command.CompanyId,
            disconnected?.ConnectionId,
            FortnoxConnectionStatusValues.Disconnected,
            now,
            "Fortnox has been disconnected.");
    }

    private static bool IsReconnectStatus(string status) =>
        status is FortnoxConnectionStatusValues.NeedsReconnect or FortnoxConnectionStatusValues.Revoked or FortnoxConnectionStatusValues.Disconnected;

    private async Task SafeMarkCallbackFailedAsync(
        Guid companyId,
        string stateHandle,
        string safeReason,
        DateTime receivedUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sessionStore.MarkFailedAsync(companyId, stateHandle, safeReason, receivedUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or DbUpdateException)
        {
            _logger.LogWarning(
                "Fortnox OAuth callback failure could not be recorded. CompanyId: {CompanyId}. Reason: {Reason}.",
                companyId,
                safeReason);
        }
    }

    private static string ToSafeCallbackFailureReason(Exception exception) =>
        exception is FortnoxOAuthException oauthException ? oauthException.SafeMessage : "Fortnox authorization was invalid.";

    private static string FormatProviderError(string providerError)
    {
        if (providerError.Contains("error_missing_license", StringComparison.OrdinalIgnoreCase) ||
            providerError.Contains("not licensed", StringComparison.OrdinalIgnoreCase))
        {
            return "The Fortnox company is not licensed for one or more requested permissions. Remove unlicensed scopes from the Fortnox configuration, or enable the matching Fortnox license before reconnecting.";
        }

        if (providerError.Contains("invalid_scope", StringComparison.OrdinalIgnoreCase) ||
            providerError.Contains("unsupported scope", StringComparison.OrdinalIgnoreCase))
        {
            return "Fortnox rejected the requested permissions. Enable the same scopes in the Fortnox Developer Portal, or remove unsupported scopes from the local Fortnox configuration.";
        }

        return "Fortnox authorization was cancelled or denied.";
    }

    private void ValidateCallbackState(CompleteFortnoxOAuthConnectionCommand command, FortnoxOAuthState state)
    {
        var now = UtcNow();
        if (state.ExpiresUtc <= now)
        {
            throw new FortnoxOAuthException("Fortnox authorization has expired. Start the connection again.");
        }

        if ((command.CompanyId != Guid.Empty && state.CompanyId != command.CompanyId) ||
            (command.UserId != Guid.Empty && state.UserId != command.UserId))
        {
            throw new UnauthorizedAccessException("Fortnox authorization did not match the current company and user.");
        }

        if (!string.IsNullOrWhiteSpace(command.Nonce) &&
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(state.Nonce),
                System.Text.Encoding.UTF8.GetBytes(command.Nonce)))
        {
            throw new UnauthorizedAccessException("Fortnox authorization nonce was invalid.");
        }
    }

    private async Task ResolveCallbackCompanyContextAsync(
        CompleteFortnoxOAuthConnectionCommand command,
        FortnoxOAuthState state,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId != Guid.Empty || _companyContextAccessor.IsResolved)
        {
            return;
        }

        _companyContextAccessor.SetCompanyId(state.CompanyId);
        var membership = await _companyMembershipContextResolver.ResolveAsync(state.CompanyId, cancellationToken);
        if (membership is null && command.UserId != Guid.Empty)
        {
            throw new UnauthorizedAccessException("Fortnox authorization did not match an active company membership.");
        }
    }

    private void EnsureCallbackTenantUser(CompleteFortnoxOAuthConnectionCommand command, FortnoxOAuthState state)
    {
        if (_companyContextAccessor.CompanyId != state.CompanyId)
        {
            throw new UnauthorizedAccessException("Fortnox connections are scoped to the current tenant and user.");
        }

        if (command.UserId != Guid.Empty &&
            _companyContextAccessor.UserId.HasValue &&
            _companyContextAccessor.UserId.Value != state.UserId)
        {
            throw new UnauthorizedAccessException("Fortnox authorization did not match the current company and user.");
        }
    }

    private void EnsureCurrentTenantUser(Guid companyId, Guid userId)
    {
        if (!_companyContextAccessor.IsResolved ||
            _companyContextAccessor.CompanyId != companyId ||
            _companyContextAccessor.UserId != userId)
        {
            throw new UnauthorizedAccessException("Fortnox connections are scoped to the current tenant and user.");
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string CreateNonce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
