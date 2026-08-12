namespace VirtualCompany.Web.Services;

public sealed partial class SalesApiClient
{
    public async Task<IReadOnlyList<CalendarConnectionSummaryResponse>> ListManagedCalendarConnectionsAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<CalendarConnectionSummaryResponse>>(
            companyId, $"api/companies/{companyId:D}/calendar-connections",
            allowNotFound: false, cancellationToken) ?? [];

    public Task<StartCalendarConnectionResponse> StartCalendarConnectionAsync(
        Guid companyId, string provider, string returnUri,
        CancellationToken cancellationToken = default) =>
        SendAsync<StartCalendarConnectionRequest, StartCalendarConnectionResponse>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/calendar-connections/{provider}/start",
            new StartCalendarConnectionRequest(returnUri), cancellationToken);

    public Task<CalendarConnectionSummaryResponse> DisconnectCalendarConnectionAsync(
        Guid companyId, Guid calendarConnectionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, CalendarConnectionSummaryResponse>(
            companyId, HttpMethod.Delete,
            $"api/companies/{companyId:D}/calendar-connections/{calendarConnectionId:D}",
            new { }, cancellationToken);
}

public sealed record StartCalendarConnectionRequest(string ReturnUri);
public sealed record StartCalendarConnectionResponse(string AuthorizationUrl);
public sealed record CalendarConnectionSummaryResponse(
    Guid Id, string Provider, string AccountEmail, string? DisplayName,
    string CalendarId, string? TimeZoneId, int Capabilities, string Status,
    bool HasRequiredPermissions, bool RequiresReconnect,
    DateTime? LastHealthCheckUtc, string? LastErrorSummary);
