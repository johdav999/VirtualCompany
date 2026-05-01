using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOAuthService : IFortnoxOAuthService
{
    private static readonly TimeSpan OAuthStateTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshLockTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshLockWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshLockPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IFortnoxOAuthSessionStore _sessionStore;
    private readonly IFortnoxTokenStore _tokenStore;
    private readonly FortnoxOAuthClient _client;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FortnoxOAuthService> _logger;
    private readonly IFortnoxIntegrationDiagnostics? _diagnostics;

    public FortnoxOAuthService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        IFortnoxOAuthStateProtector stateProtector,
        IFortnoxOAuthSessionStore sessionStore,
        IFortnoxTokenStore tokenStore,
        FortnoxOAuthClient client,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider,
        ILogger<FortnoxOAuthService> logger,
        IFortnoxIntegrationDiagnostics? diagnostics = null)
    {
        _companyContextAccessor = companyContextAccessor;
        _sessionStore = sessionStore;
        _tokenStore = tokenStore;
        _client = client;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider;
        _logger = logger;
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

        var authorizationUrl = _client.BuildAuthorizationUrl(stateHandle, nonce);
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
        if (!string.IsNullOrWhiteSpace(command.ProviderError))
        {
            throw new FortnoxOAuthException("Fortnox authorization was cancelled or denied.");
        }

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            throw new FortnoxOAuthException("Fortnox did not return an authorization code.");
        }

        var state = await _sessionStore.ConsumeAsync(command.CompanyId, command.State, cancellationToken);
        ValidateCallbackState(command, state);
        EnsureCurrentTenantUser(state.CompanyId, state.UserId);

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

        var connection = await _tokenStore.UpsertConnectedAsync(state.CompanyId, state.UserId, tokenResult, UtcNow(), cancellationToken);

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

    public async Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(
        RefreshFortnoxAccessTokenCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _tokenStore.GetAsync(command.CompanyId, command.ConnectionId, cancellationToken);
        var usable = TryUseExistingToken(existing);
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
        usable = TryUseExistingToken(afterLock);
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
            await _tokenStore.MarkAsync(connection.CompanyId, connection.ConnectionId, FortnoxConnectionStatusValues.Error, "Fortnox token refresh failed. The job will retry later.", now, cancellationToken);
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
        var connection = await _tokenStore.GetAsync(query.CompanyId, null, cancellationToken);

        if (connection is null)
        {
            return new FortnoxConnectionStatusResult(false, null, null, null, null, null, null);
        }

        return new FortnoxConnectionStatusResult(
            connection.Status == FortnoxConnectionStatusValues.Connected,
            connection.ConnectionId,
            connection.Status,
            null,
            connection.AccessTokenExpiresUtc,
            null,
            null);
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

        return new FortnoxConnectionDisconnectResult(
            command.CompanyId,
            disconnected?.ConnectionId,
            FortnoxConnectionStatusValues.Disconnected,
            now,
            "Fortnox has been disconnected.");
    }

    private static bool IsReconnectStatus(string status) =>
        status is FortnoxConnectionStatusValues.NeedsReconnect or FortnoxConnectionStatusValues.Revoked or FortnoxConnectionStatusValues.Disconnected;

    private void ValidateCallbackState(CompleteFortnoxOAuthConnectionCommand command, FortnoxOAuthState state)
    {
        var now = UtcNow();
        if (state.ExpiresUtc <= now)
        {
            throw new FortnoxOAuthException("Fortnox authorization has expired. Start the connection again.");
        }

        if (state.CompanyId != command.CompanyId || state.UserId != command.UserId)
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
