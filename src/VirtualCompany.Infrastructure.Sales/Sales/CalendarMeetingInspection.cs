using System.Globalization;
using System.Net;
using System.Text.Json;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Infrastructure.Sales;
public sealed partial class GoogleCalendarProviderClient
{
    public async Task<CalendarMeetingObservation?> InspectMeetingAsync(CalendarProviderContext context, Guid invitationId, string? externalEventId, DateTime startsUtc, DateTime endsUtc, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(context.CalendarId)}/events/{Uri.EscapeDataString(externalEventId ?? invitationId.ToString("N"))}", context.AccessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone) return null;
        if (!response.IsSuccessStatusCode) throw ProviderFailure(response.StatusCode, null);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); var root = json.RootElement;
        var result = new CalendarMeetingCreateResult(ReadRequired(root, "id", "Calendar event identifier missing."), ReadOptional(root, "iCalUID"), ReadOptional(root, "htmlLink"), ReadOptional(root, "hangoutLink") ?? ReadConferenceJoinUrl(root));
        var cancelled = ReadOptional(root, "status") == "cancelled";
        DateTime Time(string name) => root.TryGetProperty(name, out var field) && field.TryGetProperty("dateTime", out var time) ? DateTime.Parse(time.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal) : default;
        return new(result, Time("start"), Time("end"), ReadOptional(root, "summary") ?? "", cancelled);
    }
}
public sealed partial class Microsoft365CalendarProviderClient
{
    public async Task<CalendarMeetingObservation?> InspectMeetingAsync(CalendarProviderContext context, Guid invitationId, string? externalEventId, DateTime startsUtc, DateTime endsUtc, CancellationToken ct)
    {
        var calendar = context.CalendarId == "primary" ? "calendar" : $"calendars/{Uri.EscapeDataString(context.CalendarId)}";
        var uri = externalEventId != null ? $"https://graph.microsoft.com/v1.0/me/events/{Uri.EscapeDataString(externalEventId)}" :
            $"https://graph.microsoft.com/v1.0/me/{calendar}/calendarView?startDateTime={Uri.EscapeDataString(DateTime.SpecifyKind(startsUtc, DateTimeKind.Utc).AddDays(-1).ToString("O"))}&endDateTime={Uri.EscapeDataString(DateTime.SpecifyKind(endsUtc, DateTimeKind.Utc).AddDays(1).ToString("O"))}&$top=100&$select=id,iCalUId,webLink,onlineMeeting,transactionId,start,end,subject,isCancelled";
        for (var page = 0; page < 5; page++)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var target) || target.Scheme != "https" || target.Host != "graph.microsoft.com") throw new CalendarProviderException("calendar_inspection_invalid", "Calendar inspection returned an invalid continuation.", CalendarProviderFailureKind.Ambiguous);
            using var request = Authorized(HttpMethod.Get, uri, context.AccessToken); request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
            using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new CalendarProviderException("calendar_inspection_unavailable", "Calendar inspection could not complete. Reconnect or retry inspection.", CalendarProviderFailureKind.Retryable);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); var root = json.RootElement;
            JsonElement? found = externalEventId != null ? root : null;
            if (externalEventId == null)
            {
                var matches = root.GetProperty("value").EnumerateArray().Where(x => x.TryGetProperty("transactionId", out var transaction) && transaction.GetString() == invitationId.ToString("D")).ToArray();
                if (matches.Length > 1) throw new CalendarProviderException("calendar_multiple_matches", "Multiple calendar events need manual review.", CalendarProviderFailureKind.Ambiguous);
                if (matches.Length == 1) found = matches[0];
            }
            if (found is { } item)
            {
                DateTime Time(string name) => DateTime.Parse(item.GetProperty(name).GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                return new(ReadMeetingResult(item, "Calendar event identifier missing."), Time("start"), Time("end"), item.GetProperty("subject").GetString() ?? "", item.TryGetProperty("isCancelled", out var cancelled) && cancelled.GetBoolean());
            }
            if (!root.TryGetProperty("@odata.nextLink", out var next)) return null; uri = next.GetString()!;
        }
        throw new CalendarProviderException("calendar_inspection_limit", "Calendar inspection reached its limit. Review the calendar before retrying.", CalendarProviderFailureKind.Ambiguous);
    }
}
