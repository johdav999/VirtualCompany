namespace VirtualCompany.Domain.Enums;
public static class SalesMeetingConferencing
{
    public const string Browser = "browser", Teams = "teams", GoogleMeet = "google_meet", None = "none";
    public static string Resolve(string? value, bool online, ExternalAccountProvider provider)
    {
        var route = value ?? (online ? (provider == ExternalAccountProvider.Microsoft365 ? Teams : GoogleMeet) : None);
        if (route is not (Browser or Teams or GoogleMeet or None)) throw new ArgumentException("Choose a supported meeting type.");
        if (route == Teams && provider != ExternalAccountProvider.Microsoft365 || route == GoogleMeet && provider != ExternalAccountProvider.Google)
            throw new ArgumentException("The selected calendar does not support this online meeting type.");
        return route;
    }
    public static bool UsesCalendarConference(string route) => route is Teams or GoogleMeet;
}
