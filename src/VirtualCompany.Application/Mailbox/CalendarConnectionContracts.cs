using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public sealed record StartCalendarOAuthConnectionCommand(
    Guid CompanyId, Guid UserId, ExternalAccountProvider Provider,
    Uri CallbackUri, Uri? ReturnUri = null);

public sealed record CompleteCalendarOAuthConnectionCommand(
    string State, string Code, Uri CallbackUri,
    ExternalAccountProvider? ExpectedProvider = null);

public sealed record CalendarOAuthState(
    Guid CompanyId, Guid UserId, ExternalAccountProvider Provider,
    DateTime ExpiresUtc, Uri? ReturnUri, string Nonce,
    IReadOnlyCollection<string> RequestedScopes);

public sealed record CalendarOAuthStartResult(
    ExternalAccountProvider Provider, Uri AuthorizationUrl);

public sealed record CalendarOAuthCompletionResult(
    Guid CalendarConnectionId, Guid CompanyId, Guid UserId,
    ExternalAccountProvider Provider, string AccountEmail,
    string Status, Uri? ReturnUri = null);

public sealed record CalendarConnectionSummary(
    Guid Id, ExternalAccountProvider Provider, string AccountEmail,
    string? DisplayName, string CalendarId, string? TimeZoneId,
    CalendarCapability Capabilities, ExternalConnectionStatus Status,
    bool HasRequiredPermissions, bool RequiresReconnect,
    DateTime? LastHealthCheckUtc, string? LastErrorSummary);

public interface ICalendarConnectionService
{
    Task<CalendarOAuthStartResult> StartOAuthConnectionAsync(
        StartCalendarOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<CalendarOAuthCompletionResult> CompleteOAuthConnectionAsync(
        CompleteCalendarOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarConnectionSummary>> ListAsync(
        Guid companyId, Guid userId, CancellationToken cancellationToken);
    Task<CalendarConnectionSummary> DisconnectAsync(
        Guid companyId, Guid userId, Guid calendarConnectionId, CancellationToken cancellationToken);
}

public interface ICalendarOAuthStateProtector
{
    string Protect(CalendarOAuthState state);
    CalendarOAuthState Unprotect(string protectedState);
}

public sealed record CalendarOAuthAccessTokenLease(
    Guid CalendarConnectionId, Guid ExternalAccountConnectionId,
    Guid CompanyId, ExternalAccountProvider Provider,
    string AccountEmail, string AccessToken, DateTime? ExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes, string CalendarId);

public interface ICalendarOAuthAccessTokenLeaseService
{
    Task<CalendarOAuthAccessTokenLease> AcquireAsync(
        Guid companyId, Guid calendarConnectionId,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken);
}

public static class CalendarOAuthScopes
{
    public static IReadOnlyCollection<string> For(ExternalAccountProvider provider) => provider switch
    {
        ExternalAccountProvider.Google =>
        [
            "openid",
            "email",
            "profile",
            "https://www.googleapis.com/auth/calendar.events",
            "https://www.googleapis.com/auth/calendar.events.freebusy"
        ],
        ExternalAccountProvider.Microsoft365 =>
            ["offline_access", "User.Read", "Calendars.ReadWrite"],
        _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported calendar provider.")
    };

    public static IReadOnlyCollection<string> CalendarOnly(ExternalAccountProvider provider) => provider switch
    {
        ExternalAccountProvider.Google =>
        [
            "https://www.googleapis.com/auth/calendar.events",
            "https://www.googleapis.com/auth/calendar.events.freebusy"
        ],
        ExternalAccountProvider.Microsoft365 => ["Calendars.ReadWrite"],
        _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported calendar provider.")
    };
}
