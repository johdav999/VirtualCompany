namespace VirtualCompany.Application.Mailbox;

public sealed class OAuthReconnectRequiredException() : InvalidOperationException("This account authorization has expired or been revoked. Reconnect the account to continue.");

public sealed class CalendarReconnectRequiredException() : InvalidOperationException("Your calendar connection has expired or needs permission. Reconnect your calendar, then return to the lead and try again.")
{
    public const string Code = "calendar.reconnect_required";
}
