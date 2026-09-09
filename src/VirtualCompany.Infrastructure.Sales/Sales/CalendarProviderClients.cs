using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class CalendarProviderRegistry : ICalendarProviderRegistry
{
    private readonly IReadOnlyDictionary<ExternalAccountProvider, ICalendarProviderClient> _clients;

    public CalendarProviderRegistry(IEnumerable<ICalendarProviderClient> clients) =>
        _clients = clients.ToDictionary(x => x.Provider);

    public ICalendarProviderClient Resolve(ExternalAccountProvider provider) =>
        _clients.TryGetValue(provider, out var client)
            ? client
            : throw new InvalidOperationException("This connected account does not support calendar scheduling.");
}

public sealed class GoogleCalendarProviderClient : ICalendarProviderClient
{
    public const string ClientName = "google-calendar";
    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleCalendarProviderClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public ExternalAccountProvider Provider => ExternalAccountProvider.Google;
    public IReadOnlyCollection<string> RequiredScopes { get; } =
    [
        "https://www.googleapis.com/auth/calendar.events",
        "https://www.googleapis.com/auth/calendar.events.freebusy"
    ];

    public async Task<IReadOnlyList<CalendarBusyWindow>> GetBusyWindowsAsync(
        CalendarProviderContext context, DateTime fromUtc, DateTime toUtc,
        string timeZoneId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/freeBusy", context.AccessToken);
        request.Content = JsonContent.Create(new
        {
            timeMin = FormatUtc(fromUtc),
            timeMax = FormatUtc(toUtc),
            timeZone = timeZoneId,
            items = new[] { new { id = context.CalendarId } }
        });
        using var response = await SendAsync(request, creatingExternalEvent: false, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("calendars", out var calendars) ||
            !calendars.TryGetProperty(context.CalendarId, out var calendar) ||
            !calendar.TryGetProperty("busy", out var busy))
            return [];

        return busy.EnumerateArray()
            .Select(x => new CalendarBusyWindow(
                DateTime.Parse(x.GetProperty("start").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                DateTime.Parse(x.GetProperty("end").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)))
            .ToArray();
    }

    public async Task<CalendarMeetingCreateResult> CreateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingCreateRequest meeting,
        CancellationToken cancellationToken)
    {
        var calendarId = Uri.EscapeDataString(context.CalendarId);
        using var request = Authorized(
            HttpMethod.Post,
            $"https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events?sendUpdates=all&conferenceDataVersion=1",
            context.AccessToken);
        request.Content = JsonContent.Create(new
        {
            id = meeting.InvitationId.ToString("N"),
            summary = meeting.Title,
            description = meeting.Description,
            location = meeting.Location,
            start = new { dateTime = FormatUtc(meeting.StartsUtc), timeZone = meeting.TimeZoneId },
            end = new { dateTime = FormatUtc(meeting.EndsUtc), timeZone = meeting.TimeZoneId },
            attendees = new[] { new { email = meeting.AttendeeEmail, displayName = meeting.AttendeeName } },
            conferenceData = meeting.CreateOnlineMeeting
                ? new { createRequest = new { requestId = meeting.InvitationId.ToString("N"), conferenceSolutionKey = new { type = "hangoutsMeet" } } }
                : null,
            extendedProperties = new Dictionary<string, object> { ["private"] = new Dictionary<string, string> { ["virtualCompanyInvitationId"] = meeting.InvitationId.ToString("D") } }
        });
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var eventId = ReadRequired(root, "id", "Google Calendar did not return an event identifier.");
        var joinUrl = ReadOptional(root, "hangoutLink") ?? ReadConferenceJoinUrl(root);
        return new CalendarMeetingCreateResult(
            eventId,
            ReadOptional(root, "iCalUID"),
            ReadOptional(root, "htmlLink"),
            joinUrl);
    }

    public async Task<CalendarMeetingCreateResult> UpdateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingUpdateRequest meeting,
        CancellationToken cancellationToken)
    {
        var calendarId = Uri.EscapeDataString(context.CalendarId);
        var eventId = Uri.EscapeDataString(meeting.ExternalEventId);
        using var request = Authorized(
            HttpMethod.Patch,
            $"https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events/{eventId}?sendUpdates=all&conferenceDataVersion=1",
            context.AccessToken);
        request.Content = JsonContent.Create(new
        {
            summary = meeting.Title,
            description = meeting.Description,
            location = meeting.Location,
            start = new { dateTime = FormatUtc(meeting.StartsUtc), timeZone = meeting.TimeZoneId },
            end = new { dateTime = FormatUtc(meeting.EndsUtc), timeZone = meeting.TimeZoneId },
            attendees = new[] { new { email = meeting.AttendeeEmail, displayName = meeting.AttendeeName } },
            conferenceData = meeting.CreateOnlineMeeting
                ? new { createRequest = new { requestId = meeting.ChangeRequestId.ToString("N"), conferenceSolutionKey = new { type = "hangoutsMeet" } } }
                : null,
            extendedProperties = new Dictionary<string, object> { ["private"] = new Dictionary<string, string> { ["virtualCompanyChangeRequestId"] = meeting.ChangeRequestId.ToString("D") } }
        });
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        return new CalendarMeetingCreateResult(
            ReadRequired(root, "id", "Google Calendar did not return the updated event identifier."),
            ReadOptional(root, "iCalUID"),
            ReadOptional(root, "htmlLink"),
            ReadOptional(root, "hangoutLink") ?? ReadConferenceJoinUrl(root));
    }

    public async Task CancelMeetingAsync(
        CalendarProviderContext context, string externalEventId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var calendarId = Uri.EscapeDataString(context.CalendarId);
        var eventId = Uri.EscapeDataString(externalEventId);
        using var request = Authorized(
            HttpMethod.Delete,
            $"https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events/{eventId}?sendUpdates=all",
            context.AccessToken);
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool creatingExternalEvent, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw ProviderFailure(status, responseBody);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CalendarProviderException) { throw; }
        catch (OperationCanceledException ex)
        {
            throw new CalendarProviderException(
                "google_calendar_timeout",
                creatingExternalEvent
                    ? "Google Calendar did not confirm whether the calendar operation completed. Review it before retrying."
                    : "Google Calendar timed out while checking availability.",
                creatingExternalEvent ? CalendarProviderFailureKind.Ambiguous : CalendarProviderFailureKind.Retryable,
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CalendarProviderException(
                "google_calendar_transport",
                creatingExternalEvent
                    ? "Google Calendar did not confirm whether the calendar operation completed. Review it before retrying."
                    : "Google Calendar is temporarily unavailable.",
                creatingExternalEvent ? CalendarProviderFailureKind.Ambiguous : CalendarProviderFailureKind.Retryable,
                ex);
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static CalendarProviderException ProviderFailure(HttpStatusCode status, string? responseBody)
    {
        var googleReason = ReadGoogleErrorReason(responseBody);
        if (status == HttpStatusCode.Unauthorized ||
            status == HttpStatusCode.Forbidden && googleReason is "insufficientPermissions" or "authError")
            return new("calendar_authorization_required", "Reconnect Google Calendar and grant calendar access.", CalendarProviderFailureKind.AuthenticationRequired);

        if (status == HttpStatusCode.Forbidden && googleReason is "accessNotConfigured" or "SERVICE_DISABLED")
            return new(
                "google_calendar_api_disabled",
                "Enable the Google Calendar API in the Google Cloud project used by this OAuth client, then retry delivery. Reconnecting is not required.",
                CalendarProviderFailureKind.Permanent);

        if (status == HttpStatusCode.Forbidden && googleReason is "rateLimitExceeded" or "userRateLimitExceeded")
            return new("calendar_temporarily_unavailable", "Google Calendar is temporarily unavailable.", CalendarProviderFailureKind.Retryable);

        if (status == HttpStatusCode.Forbidden)
            return new(
                "google_calendar_permission_denied",
                "Google Calendar denied the request. Confirm this account may create calendar events and Google Meet links, then retry delivery.",
                CalendarProviderFailureKind.Permanent);

        return status switch
        {
            HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                new("calendar_temporarily_unavailable", "Google Calendar is temporarily unavailable.", CalendarProviderFailureKind.Retryable),
            _ => new("calendar_request_rejected", "Google Calendar rejected the meeting invitation.", CalendarProviderFailureKind.Permanent)
        };
    }

    private static string? ReadGoogleErrorReason(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            if (!json.RootElement.TryGetProperty("error", out var error)) return null;
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var item in errors.EnumerateArray())
                    if (item.TryGetProperty("reason", out var reason) && !string.IsNullOrWhiteSpace(reason.GetString()))
                        return reason.GetString();
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var item in details.EnumerateArray())
                    if (item.TryGetProperty("reason", out var reason) && !string.IsNullOrWhiteSpace(reason.GetString()))
                        return reason.GetString();
            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatUtc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string ReadRequired(JsonElement root, string name, string message) => ReadOptional(root, name) ?? throw new CalendarProviderException("calendar_response_invalid", message, CalendarProviderFailureKind.Ambiguous);
    private static string? ReadOptional(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static string? ReadConferenceJoinUrl(JsonElement root)
    {
        if (!root.TryGetProperty("conferenceData", out var conference) || !conference.TryGetProperty("entryPoints", out var entries)) return null;
        foreach (var entry in entries.EnumerateArray())
            if (entry.TryGetProperty("entryPointType", out var type) && type.GetString() == "video")
                return ReadOptional(entry, "uri");
        return null;
    }
}

public sealed class Microsoft365CalendarProviderClient : ICalendarProviderClient
{
    public const string ClientName = "microsoft365-calendar";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Microsoft365CalendarProviderClient> _logger;

    public Microsoft365CalendarProviderClient(
        IHttpClientFactory httpClientFactory,
        ILogger<Microsoft365CalendarProviderClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ExternalAccountProvider Provider => ExternalAccountProvider.Microsoft365;
    public IReadOnlyCollection<string> RequiredScopes { get; } = ["Calendars.ReadWrite"];

    public async Task<IReadOnlyList<CalendarBusyWindow>> GetBusyWindowsAsync(
        CalendarProviderContext context, DateTime fromUtc, DateTime toUtc,
        string timeZoneId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/calendar/getSchedule", context.AccessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
        request.Content = JsonContent.Create(new
        {
            schedules = new[] { context.OrganizerEmail },
            startTime = new { dateTime = FormatGraphUtc(fromUtc), timeZone = "UTC" },
            endTime = new { dateTime = FormatGraphUtc(toUtc), timeZone = "UTC" },
            availabilityViewInterval = 30
        });
        using var response = await SendAsync(request, creatingExternalEvent: false, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("value", out var values) || values.GetArrayLength() == 0 ||
            !values[0].TryGetProperty("scheduleItems", out var items))
            return [];

        return items.EnumerateArray()
            .Select(x => new CalendarBusyWindow(
                ParseGraphUtc(x.GetProperty("start").GetProperty("dateTime").GetString()!),
                ParseGraphUtc(x.GetProperty("end").GetProperty("dateTime").GetString()!)))
            .ToArray();
    }

    public async Task<CalendarMeetingCreateResult> CreateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingCreateRequest meeting,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating Microsoft 365 calendar event. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. InvitationId: {InvitationId}. CreateOnlineMeeting: {CreateOnlineMeeting}.",
            context.CompanyId, context.ConnectionId, meeting.InvitationId, meeting.CreateOnlineMeeting);
        using var request = Authorized(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/events", context.AccessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
        request.Content = JsonContent.Create(new
        {
            subject = meeting.Title,
            body = new { contentType = "text", content = meeting.Description },
            start = new { dateTime = FormatGraphUtc(meeting.StartsUtc), timeZone = "UTC" },
            end = new { dateTime = FormatGraphUtc(meeting.EndsUtc), timeZone = "UTC" },
            location = string.IsNullOrWhiteSpace(meeting.Location) ? null : new { displayName = meeting.Location },
            attendees = new[]
            {
                new
                {
                    emailAddress = new { address = meeting.AttendeeEmail, name = meeting.AttendeeName ?? meeting.AttendeeEmail },
                    type = "required"
                }
            },
            isOnlineMeeting = meeting.CreateOnlineMeeting,
            onlineMeetingProvider = meeting.CreateOnlineMeeting ? "teamsForBusiness" : null,
            allowNewTimeProposals = true,
            responseRequested = true,
            transactionId = meeting.InvitationId.ToString("D")
        });
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var result = ReadMeetingResult(root, "Microsoft 365 did not return an event identifier.");
        LogEventResponse("create", meeting.InvitationId, root, result);
        return meeting.CreateOnlineMeeting && string.IsNullOrWhiteSpace(result.OnlineMeetingUrl)
            ? await TryEnableTeamsMeetingAsync(context, meeting.InvitationId, result, cancellationToken)
            : result;
    }

    public async Task<CalendarMeetingCreateResult> UpdateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingUpdateRequest meeting,
        CancellationToken cancellationToken)
    {
        var eventId = Uri.EscapeDataString(meeting.ExternalEventId);
        using var request = Authorized(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/me/events/{eventId}", context.AccessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
        request.Content = JsonContent.Create(new
        {
            subject = meeting.Title,
            body = new { contentType = "text", content = meeting.Description },
            start = new { dateTime = FormatGraphUtc(meeting.StartsUtc), timeZone = "UTC" },
            end = new { dateTime = FormatGraphUtc(meeting.EndsUtc), timeZone = "UTC" },
            location = string.IsNullOrWhiteSpace(meeting.Location) ? null : new { displayName = meeting.Location },
            attendees = new[]
            {
                new
                {
                    emailAddress = new { address = meeting.AttendeeEmail, name = meeting.AttendeeName ?? meeting.AttendeeEmail },
                    type = "required"
                }
            },
            isOnlineMeeting = meeting.CreateOnlineMeeting,
            onlineMeetingProvider = meeting.CreateOnlineMeeting ? "teamsForBusiness" : null,
            allowNewTimeProposals = true,
            responseRequested = true
        });
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var result = ReadMeetingResult(root, "Microsoft 365 did not return the updated event identifier.");
        LogEventResponse("update", meeting.ChangeRequestId, root, result);
        return meeting.CreateOnlineMeeting && string.IsNullOrWhiteSpace(result.OnlineMeetingUrl)
            ? await TryEnableTeamsMeetingAsync(context, meeting.ChangeRequestId, result, cancellationToken)
            : result;
    }

    public async Task CancelMeetingAsync(
        CalendarProviderContext context, string externalEventId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var eventId = Uri.EscapeDataString(externalEventId);
        using var request = Authorized(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/me/events/{eventId}", context.AccessToken);
        using var response = await SendAsync(request, creatingExternalEvent: true, cancellationToken);
    }

    private async Task<CalendarMeetingCreateResult> TryEnableTeamsMeetingAsync(
        CalendarProviderContext context,
        Guid operationId,
        CalendarMeetingCreateResult existingEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            using var calendarRequest = Authorized(
                HttpMethod.Get,
                "https://graph.microsoft.com/v1.0/me/calendar?$select=allowedOnlineMeetingProviders,defaultOnlineMeetingProvider",
                context.AccessToken);
            using var calendarResponse = await SendAsync(calendarRequest, creatingExternalEvent: false, cancellationToken);
            using var calendarJson = await JsonDocument.ParseAsync(
                await calendarResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var calendar = calendarJson.RootElement;
            var allowedProviders = ReadStringArray(calendar, "allowedOnlineMeetingProviders");
            var defaultProvider = ReadOptional(calendar, "defaultOnlineMeetingProvider");

            _logger.LogDebug(
                "Microsoft 365 calendar online-meeting capabilities inspected. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. OperationId: {OperationId}. AllowedProviders: {AllowedProviders}. DefaultProvider: {DefaultProvider}.",
                context.CompanyId, context.ConnectionId, operationId,
                allowedProviders.Count == 0 ? "(none)" : string.Join(",", allowedProviders),
                defaultProvider ?? "(none)");

            if (!allowedProviders.Contains("teamsForBusiness", StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Microsoft 365 did not enable the requested Teams meeting because the connected default calendar does not advertise teamsForBusiness. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. OperationId: {OperationId}.",
                    context.CompanyId, context.ConnectionId, operationId);
                return existingEvent;
            }

            var eventId = Uri.EscapeDataString(existingEvent.ExternalEventId);
            using var patchRequest = Authorized(
                HttpMethod.Patch,
                $"https://graph.microsoft.com/v1.0/me/events/{eventId}",
                context.AccessToken);
            patchRequest.Content = JsonContent.Create(new
            {
                isOnlineMeeting = true,
                onlineMeetingProvider = "teamsForBusiness"
            });
            using var patchResponse = await SendAsync(patchRequest, creatingExternalEvent: true, cancellationToken);
            using var patchJson = await JsonDocument.ParseAsync(
                await patchResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var patchedRoot = patchJson.RootElement;
            var patchedEvent = ReadMeetingResult(
                patchedRoot,
                "Microsoft 365 did not return the updated event identifier.",
                existingEvent);
            LogEventResponse("online-meeting-recovery", operationId, patchedRoot, patchedEvent);

            if (string.IsNullOrWhiteSpace(patchedEvent.OnlineMeetingUrl))
                _logger.LogWarning(
                    "Microsoft 365 accepted the Teams recovery update but still returned no join URL. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. OperationId: {OperationId}. ExternalEventId: {ExternalEventId}.",
                    context.CompanyId, context.ConnectionId, operationId, existingEvent.ExternalEventId);

            return patchedEvent;
        }
        catch (CalendarProviderException ex)
        {
            _logger.LogWarning(
                "Microsoft 365 Teams recovery could not be completed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. OperationId: {OperationId}. ErrorCode: {ErrorCode}. FailureKind: {FailureKind}.",
                context.CompanyId, context.ConnectionId, operationId, ex.Code, ex.Kind);
            return existingEvent;
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "Microsoft 365 returned an invalid calendar capability or recovery response. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. OperationId: {OperationId}.",
                context.CompanyId, context.ConnectionId, operationId);
            return existingEvent;
        }
    }

    private void LogEventResponse(
        string operation,
        Guid operationId,
        JsonElement root,
        CalendarMeetingCreateResult result) =>
        _logger.LogDebug(
            "Microsoft 365 calendar event response parsed. Operation: {Operation}. OperationId: {OperationId}. ExternalEventId: {ExternalEventId}. IsOnlineMeeting: {IsOnlineMeeting}. OnlineMeetingProvider: {OnlineMeetingProvider}. HasOnlineMeetingObject: {HasOnlineMeetingObject}. HasJoinUrl: {HasJoinUrl}.",
            operation,
            operationId,
            result.ExternalEventId,
            ReadOptionalBoolean(root, "isOnlineMeeting"),
            ReadOptional(root, "onlineMeetingProvider") ?? "(none)",
            root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("onlineMeeting", out var onlineMeeting) &&
                onlineMeeting.ValueKind == JsonValueKind.Object,
            !string.IsNullOrWhiteSpace(result.OnlineMeetingUrl));

    private static CalendarMeetingCreateResult ReadMeetingResult(
        JsonElement root,
        string missingEventIdMessage,
        CalendarMeetingCreateResult? fallback = null)
    {
        var onlineMeeting = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("onlineMeeting", out var value) &&
            value.ValueKind == JsonValueKind.Object
                ? value
                : default;
        return new CalendarMeetingCreateResult(
            ReadOptional(root, "id") ?? fallback?.ExternalEventId ??
                throw new CalendarProviderException(
                    "calendar_response_invalid",
                    missingEventIdMessage,
                    CalendarProviderFailureKind.Ambiguous),
            ReadOptional(root, "iCalUId") ?? fallback?.ExternalICalUid,
            ReadOptional(root, "webLink") ?? fallback?.ProviderWebUrl,
            onlineMeeting.ValueKind == JsonValueKind.Object
                ? ReadOptional(onlineMeeting, "joinUrl")
                : fallback?.OnlineMeetingUrl);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static bool? ReadOptionalBoolean(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool creatingExternalEvent, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            throw status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new CalendarProviderException("calendar_authorization_required", "Reconnect Microsoft 365 and grant calendar access.", CalendarProviderFailureKind.AuthenticationRequired),
                HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                    new CalendarProviderException("calendar_temporarily_unavailable", "Microsoft 365 Calendar is temporarily unavailable.", CalendarProviderFailureKind.Retryable),
                _ => new CalendarProviderException("calendar_request_rejected", "Microsoft 365 rejected the meeting invitation.", CalendarProviderFailureKind.Permanent)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CalendarProviderException) { throw; }
        catch (OperationCanceledException ex)
        {
            throw new CalendarProviderException(
                "microsoft_calendar_timeout",
                creatingExternalEvent
                    ? "Microsoft 365 did not confirm whether the calendar operation completed. Review it before retrying."
                    : "Microsoft 365 Calendar timed out while checking availability.",
                creatingExternalEvent ? CalendarProviderFailureKind.Ambiguous : CalendarProviderFailureKind.Retryable,
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CalendarProviderException(
                "microsoft_calendar_transport",
                creatingExternalEvent
                    ? "Microsoft 365 did not confirm whether the calendar operation completed. Review it before retrying."
                    : "Microsoft 365 Calendar is temporarily unavailable.",
                creatingExternalEvent ? CalendarProviderFailureKind.Ambiguous : CalendarProviderFailureKind.Retryable,
                ex);
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string FormatGraphUtc(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    private static DateTime ParseGraphUtc(string value) => DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
    private static string ReadRequired(JsonElement root, string name, string message) => ReadOptional(root, name) ?? throw new CalendarProviderException("calendar_response_invalid", message, CalendarProviderFailureKind.Ambiguous);
    private static string? ReadOptional(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
