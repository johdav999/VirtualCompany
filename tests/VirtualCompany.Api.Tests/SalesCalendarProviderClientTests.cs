using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesCalendarProviderClientTests
{
    [Fact]
    public async Task Google_create_uses_deterministic_event_id_and_sends_updates()
    {
        var invitationId = Guid.Parse("efd8b4d2-da9d-438e-990e-d2ac719bf608");
        var handler = new CapturingHandler("""
            {
              "id": "efd8b4d2da9d438e990ed2ac719bf608",
              "iCalUID": "event@example.google.com",
              "htmlLink": "https://calendar.google.com/event",
              "hangoutLink": "https://meet.google.com/abc-defg-hij"
            }
            """);
        var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));

        var result = await client.CreateMeetingAsync(
            Context(ExternalAccountProvider.Google),
            Meeting(invitationId),
            CancellationToken.None);

        Assert.Contains("sendUpdates=all", handler.RequestUri!.Query);
        Assert.Contains("conferenceDataVersion=1", handler.RequestUri.Query);
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(invitationId.ToString("N"), payload.RootElement.GetProperty("id").GetString());
        Assert.Equal("customer@example.com", payload.RootElement.GetProperty("attendees")[0].GetProperty("email").GetString());
        Assert.Equal(invitationId.ToString("D"), payload.RootElement.GetProperty("extendedProperties").GetProperty("private").GetProperty("virtualCompanyInvitationId").GetString());
        Assert.Equal("https://meet.google.com/abc-defg-hij", result.OnlineMeetingUrl);
    }

    [Fact]
    public async Task Microsoft_create_uses_transaction_id_and_requests_teams_meeting()
    {
        var invitationId = Guid.Parse("efd8b4d2-da9d-438e-990e-d2ac719bf608");
        var handler = new CapturingHandler("""
            {
              "id": "AAMk-event",
              "iCalUId": "ical-id",
              "webLink": "https://outlook.office.com/calendar/item",
              "onlineMeeting": { "joinUrl": "https://teams.microsoft.com/l/meetup-join/example" }
            }
            """);
        var client = new Microsoft365CalendarProviderClient(new SingleClientFactory(handler));

        var result = await client.CreateMeetingAsync(
            Context(ExternalAccountProvider.Microsoft365),
            Meeting(invitationId),
            CancellationToken.None);

        Assert.Equal("https://graph.microsoft.com/v1.0/me/events", handler.RequestUri!.ToString());
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(invitationId.ToString("D"), payload.RootElement.GetProperty("transactionId").GetString());
        Assert.True(payload.RootElement.GetProperty("isOnlineMeeting").GetBoolean());
        Assert.Equal("teamsForBusiness", payload.RootElement.GetProperty("onlineMeetingProvider").GetString());
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/example", result.OnlineMeetingUrl);
    }

    [Fact]
    public async Task Google_update_patches_the_existing_event_and_sends_updates()
    {
        var handler = new CapturingHandler("""{"id":"existing-event","htmlLink":"https://calendar.google.com/event"}""");
        var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));

        var result = await client.UpdateMeetingAsync(
            Context(ExternalAccountProvider.Google),
            UpdatedMeeting("existing-event"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.RequestMethod);
        Assert.Contains("/calendars/primary/events/existing-event", handler.RequestUri!.AbsolutePath);
        Assert.Contains("sendUpdates=all", handler.RequestUri.Query);
        Assert.Equal("existing-event", result.ExternalEventId);
    }

    [Fact]
    public async Task Google_cancel_deletes_the_existing_event_and_sends_updates()
    {
        var handler = new CapturingHandler("{}");
        var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));

        await client.CancelMeetingAsync(
            Context(ExternalAccountProvider.Google), "existing-event", "change-key", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.RequestMethod);
        Assert.Contains("/calendars/primary/events/existing-event", handler.RequestUri!.AbsolutePath);
        Assert.Contains("sendUpdates=all", handler.RequestUri.Query);
    }

    [Fact]
    public async Task Microsoft_update_patches_the_existing_event()
    {
        var handler = new CapturingHandler("""{"id":"existing-event","webLink":"https://outlook.office.com/calendar/item"}""");
        var client = new Microsoft365CalendarProviderClient(new SingleClientFactory(handler));

        var result = await client.UpdateMeetingAsync(
            Context(ExternalAccountProvider.Microsoft365),
            UpdatedMeeting("existing-event"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.RequestMethod);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/events/existing-event", handler.RequestUri!.ToString());
        Assert.Equal("existing-event", result.ExternalEventId);
    }

    [Fact]
    public async Task Microsoft_cancel_deletes_the_existing_event()
    {
        var handler = new CapturingHandler("{}");
        var client = new Microsoft365CalendarProviderClient(new SingleClientFactory(handler));

        await client.CancelMeetingAsync(
            Context(ExternalAccountProvider.Microsoft365), "existing-event", "change-key", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.RequestMethod);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/events/existing-event", handler.RequestUri!.ToString());
    }
    [Fact]
    public async Task Google_free_busy_is_normalized_to_utc_windows()
    {
        var handler = new CapturingHandler("""
            {
              "calendars": {
                "primary": {
                  "busy": [
                    { "start": "2026-08-10T08:00:00Z", "end": "2026-08-10T08:30:00Z" }
                  ]
                }
              }
            }
            """);
        var client = new GoogleCalendarProviderClient(new SingleClientFactory(handler));

        var result = await client.GetBusyWindowsAsync(
            Context(ExternalAccountProvider.Google),
            new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc),
            "Europe/Stockholm",
            CancellationToken.None);

        var window = Assert.Single(result);
        Assert.Equal(DateTimeKind.Utc, window.StartsUtc.Kind);
        Assert.Equal(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), window.StartsUtc);
    }

    [Fact]
    public void Provider_clients_declare_explicit_calendar_permissions()
    {
        var google = new GoogleCalendarProviderClient(new SingleClientFactory(new CapturingHandler("{}")));
        var microsoft = new Microsoft365CalendarProviderClient(new SingleClientFactory(new CapturingHandler("{}")));

        Assert.Contains("https://www.googleapis.com/auth/calendar.events", google.RequiredScopes);
        Assert.Contains("https://www.googleapis.com/auth/calendar.events.freebusy", google.RequiredScopes);
        Assert.Equal(["Calendars.ReadWrite"], microsoft.RequiredScopes);
    }

    private static CalendarProviderContext Context(ExternalAccountProvider provider) =>
        new(Guid.NewGuid(), Guid.NewGuid(), provider, "sales@example.com", "secret-token", "primary");

    private static CalendarMeetingCreateRequest Meeting(Guid invitationId) =>
        new(
            invitationId,
            $"sales-meeting:{invitationId:N}",
            "Virtual Company demo",
            "Product overview and next steps.",
            new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 10, 8, 30, 0, DateTimeKind.Utc),
            "Europe/Stockholm",
            null,
            "customer@example.com",
            "Customer",
            true);

    private static CalendarMeetingUpdateRequest UpdatedMeeting(string externalEventId) =>
        new(
            Guid.NewGuid(),
            "change-key",
            externalEventId,
            "Updated demo",
            "Updated agenda.",
            new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 11, 9, 45, 0, DateTimeKind.Utc),
            "Europe/Stockholm",
            null,
            "customer@example.com",
            "Customer",
            true);
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestMethod = request.Method;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
