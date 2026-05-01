namespace VirtualCompany.Application.Finance;

public sealed record StartFortnoxOAuthConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    Uri? ReturnUri = null,
    bool Reconnect = false);

public sealed record CompleteFortnoxOAuthConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    string State,
    string Code,
    string? Nonce = null,
    string? ProviderError = null);

public sealed record RefreshFortnoxAccessTokenCommand(
    Guid CompanyId,
    Guid? ConnectionId = null);

public sealed record DisconnectFortnoxConnectionCommand(
    Guid CompanyId,
    Guid UserId);

public sealed record GetFortnoxConnectionStatusQuery(
    Guid CompanyId,
    Guid UserId);

public sealed record FortnoxOAuthStartResult(
    Uri AuthorizationUrl,
    DateTime ExpiresUtc);

public sealed record FortnoxOAuthCompletionResult(
    Guid ConnectionId,
    Guid CompanyId,
    string Status,
    Uri? ReturnUri);

public sealed record FortnoxConnectionStatusResult(
    bool IsConnected,
    Guid? ConnectionId,
    string? ConnectionStatus,
    DateTime? ConnectedAtUtc,
    DateTime? AccessTokenExpiresUtc,
    DateTime? LastRefreshAttemptUtc,
    string? LastErrorSummary);

public sealed record FortnoxAccessTokenResult(
    bool Succeeded,
    string? AccessToken,
    DateTime? ExpiresUtc,
    bool NeedsReconnect,
    string? SafeFailureMessage)
{
    public static FortnoxAccessTokenResult Success(string accessToken, DateTime? expiresUtc) =>
        new(true, accessToken, expiresUtc, false, null);

    public static FortnoxAccessTokenResult ReconnectRequired(string message) =>
        new(false, null, null, true, message);

    public static FortnoxAccessTokenResult TransientFailure(string message) =>
        new(false, null, null, false, message);
}

public sealed record FortnoxOAuthState(
    Guid CompanyId,
    Guid UserId,
    string Nonce,
    DateTime IssuedUtc,
    DateTime ExpiresUtc,
    bool Reconnect,
    Uri? ReturnUri = null);

public sealed record FortnoxOAuthTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime? AccessTokenExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes,
    string? ProviderTenantId = null);

public sealed record FortnoxTokenSnapshot(
    Guid ConnectionId,
    Guid CompanyId,
    string Status,
    string? AccessToken,
    string? RefreshToken,
    DateTime? AccessTokenExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes,
    string? ProviderTenantId);

public sealed record FortnoxConnectionDisconnectResult(
    Guid CompanyId,
    Guid? ConnectionId,
    string Status,
    DateTime DisconnectedUtc,
    string Message);

public interface IFortnoxOAuthSessionStore
{
    Task<string> CreateAsync(FortnoxOAuthState state, TimeSpan ttl, CancellationToken cancellationToken);
    Task<FortnoxOAuthState> ConsumeAsync(Guid companyId, string stateHandle, CancellationToken cancellationToken);
}

public interface IFortnoxTokenStore
{
    Task<FortnoxTokenSnapshot?> GetAsync(Guid companyId, Guid? connectionId, CancellationToken cancellationToken);
    Task<FortnoxTokenSnapshot> UpsertConnectedAsync(Guid companyId, Guid userId, FortnoxOAuthTokenResult tokenResult, DateTime nowUtc, CancellationToken cancellationToken);
    Task<FortnoxTokenSnapshot> StoreRefreshResultAsync(Guid companyId, Guid connectionId, FortnoxOAuthTokenResult tokenResult, DateTime nowUtc, CancellationToken cancellationToken);
    Task MarkAsync(Guid companyId, Guid connectionId, string status, string safeReason, DateTime nowUtc, CancellationToken cancellationToken);
    Task<FortnoxTokenSnapshot?> DisconnectAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class FortnoxOAuthException : Exception
{
    public FortnoxOAuthException(string safeMessage, bool requiresReconnect = false, bool isTransient = false)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
        RequiresReconnect = requiresReconnect;
        IsTransient = isTransient;
    }

    public string SafeMessage { get; }
    public bool RequiresReconnect { get; }
    public bool IsTransient { get; }
}

public interface IFortnoxOAuthService
{
    Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(CompleteFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(RefreshFortnoxAccessTokenCommand command, CancellationToken cancellationToken);
    Task<FortnoxConnectionStatusResult> GetStatusAsync(GetFortnoxConnectionStatusQuery query, CancellationToken cancellationToken);
    Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken);
    Task<FortnoxConnectionDisconnectResult> DisconnectAsync(DisconnectFortnoxConnectionCommand command, CancellationToken cancellationToken);
}
