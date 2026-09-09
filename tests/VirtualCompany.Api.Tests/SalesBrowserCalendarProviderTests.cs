using System.Net;
using System.Text.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;
namespace VirtualCompany.Api.Tests;
public sealed partial class SalesCalendarProviderClientTests
{
    [Fact]
    public async Task Microsoft_browser_invitation_keeps_selected_calendar_and_never_requests_Teams()
    {
        var handler = new SequenceCapturingHandler("""{"id":"event","webLink":"https://outlook.example.test/event"}""");
        var request = Meeting(Guid.NewGuid()) with { CreateOnlineMeeting = false, Description = "Join browser meeting: https://rooms.example.test/room#invite=test" };
        await MicrosoftClient(handler).CreateMeetingAsync(Context(ExternalAccountProvider.Microsoft365) with { CalendarId = "chosen-calendar" }, request, default);
        var sent = Assert.Single(handler.Requests); Assert.Contains("/calendars/chosen-calendar/events", sent.Uri.AbsolutePath);
        using var json = JsonDocument.Parse(sent.Body!); Assert.False(json.RootElement.GetProperty("isOnlineMeeting").GetBoolean()); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("onlineMeetingProvider").ValueKind);
        Assert.Contains("#invite=test", json.RootElement.GetProperty("body").GetProperty("content").GetString());
    }
    [Fact]
    public async Task Google_browser_invitation_does_not_request_a_Google_Meet_conference()
    {
        var handler = new CapturingHandler("""{"id":"event"}"""); var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));
        await client.CreateMeetingAsync(Context(ExternalAccountProvider.Google), Meeting(Guid.NewGuid()) with { CreateOnlineMeeting = false }, default);
        using var json = JsonDocument.Parse(handler.RequestBody!); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("conferenceData").ValueKind);
    }
    [Fact]
    public async Task Microsoft_reconciliation_reads_stable_transaction_without_writing()
    {
        var id = Guid.NewGuid(); var handler = new CapturingHandler(JsonSerializer.Serialize(new { value = new[] { new { id = "event", transactionId = id.ToString("D"), subject = "Demo", start = new { dateTime = "2026-09-10T10:00:00", timeZone = "UTC" }, end = new { dateTime = "2026-09-10T10:30:00", timeZone = "UTC" } } } }));
        var found = await MicrosoftClient(handler).InspectMeetingAsync(Context(ExternalAccountProvider.Microsoft365), id, null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), default);
        Assert.Equal("event", found!.Event.ExternalEventId); Assert.Equal(HttpMethod.Get, handler.RequestMethod); Assert.Null(handler.RequestBody);
    }
    [Fact]
    public async Task Stored_UTC_calendar_times_do_not_shift_with_the_host_time_zone()
    {
        var handler = new CapturingHandler("""{"id":"event"}"""); var request = Meeting(Guid.NewGuid());
        request = request with { CreateOnlineMeeting = false, StartsUtc = DateTime.SpecifyKind(request.StartsUtc, DateTimeKind.Unspecified), EndsUtc = DateTime.SpecifyKind(request.EndsUtc, DateTimeKind.Unspecified) };
        await MicrosoftClient(handler).CreateMeetingAsync(Context(ExternalAccountProvider.Microsoft365), request, default);
        using var json = JsonDocument.Parse(handler.RequestBody!); Assert.Equal("2026-08-10T08:00:00", json.RootElement.GetProperty("start").GetProperty("dateTime").GetString());
    }
    [Fact]
    public async Task Google_missing_event_is_a_read_observation_not_permission_to_send()
    {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound); var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));
        Assert.Null(await client.InspectMeetingAsync(Context(ExternalAccountProvider.Google), Guid.NewGuid(), null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), default)); Assert.Equal(HttpMethod.Get, handler.RequestMethod);
    }
}
