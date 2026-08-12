using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/calendar-connections")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CalendarConnectionsController : ControllerBase
{
    private readonly ICalendarConnectionService _service;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly IWebHostEnvironment _environment;

    public CalendarConnectionsController(
        ICalendarConnectionService service,
        ICompanyContextAccessor companyContext,
        IWebHostEnvironment environment)
    {
        _service = service;
        _companyContext = companyContext;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IReadOnlyList<CalendarConnectionResponse>> ListAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        (await _service.ListAsync(companyId, UserId(), cancellationToken))
            .Select(Map)
            .ToArray();

    [HttpPost("{provider}/start")]
    public async Task<ActionResult<StartCalendarConnectionResponse>> StartAsync(
        Guid companyId, string provider,
        [FromBody] StartCalendarConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var parsed = ExternalAccountProviderValues.Parse(provider);
        var callback = BuildCallbackUri(parsed);
        var result = await _service.StartOAuthConnectionAsync(
            new StartCalendarOAuthConnectionCommand(
                companyId, UserId(), parsed, callback,
                BuildReturnUri(request.ReturnUri)), cancellationToken);
        return Ok(new StartCalendarConnectionResponse(result.AuthorizationUrl.ToString()));
    }

    [HttpDelete("{calendarConnectionId:guid}")]
    public async Task<CalendarConnectionResponse> DisconnectAsync(
        Guid companyId, Guid calendarConnectionId, CancellationToken cancellationToken) =>
        Map(await _service.DisconnectAsync(
            companyId, UserId(), calendarConnectionId, cancellationToken));

    private static CalendarConnectionResponse Map(CalendarConnectionSummary connection) =>
        new(
            connection.Id,
            connection.Provider.ToStorageValue(),
            connection.AccountEmail,
            connection.DisplayName,
            connection.CalendarId,
            connection.TimeZoneId,
            (int)connection.Capabilities,
            connection.Status.ToStorageValue(),
            connection.HasRequiredPermissions,
            connection.RequiresReconnect,
            connection.LastHealthCheckUtc,
            connection.LastErrorSummary);

    private Uri BuildCallbackUri(ExternalAccountProvider provider)
    {
        var builder = new UriBuilder(Request.Scheme, Request.Host.Host)
        {
            Path = $"/api/calendar-connections/{provider.ToStorageValue()}/callback"
        };
        if (Request.Host.Port.HasValue) builder.Port = Request.Host.Port.Value;
        return builder.Uri;
    }

    private Uri? BuildReturnUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !AllowedPath(uri.AbsolutePath) ||
            !(string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
              _environment.IsDevelopment() && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Calendar return URI must be a local calendar settings or sales URL.", nameof(value));
        return uri;
    }

    private static bool AllowedPath(string path) =>
        path.StartsWith("/settings/calendar-connections", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/app/sales/leads/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/app/sales/deals/", StringComparison.OrdinalIgnoreCase);

    private Guid UserId() => _companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}

public sealed record StartCalendarConnectionRequest(string? ReturnUri);
public sealed record StartCalendarConnectionResponse(string AuthorizationUrl);

public sealed record CalendarConnectionResponse(Guid Id, string Provider, string AccountEmail, string? DisplayName, string CalendarId, string? TimeZoneId, int Capabilities, string Status, bool HasRequiredPermissions, bool RequiresReconnect, DateTime? LastHealthCheckUtc, string? LastErrorSummary);
