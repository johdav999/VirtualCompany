using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/calendar-connections")]
public sealed class CalendarConnectionCallbacksController : ControllerBase
{
    private readonly ICalendarConnectionService _service;
    private readonly ICalendarOAuthStateProtector _stateProtector;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly TimeProvider _timeProvider;
    private readonly IWebHostEnvironment _environment;

    public CalendarConnectionCallbacksController(
        ICalendarConnectionService service,
        ICalendarOAuthStateProtector stateProtector,
        ICompanyContextAccessor companyContext,
        TimeProvider timeProvider,
        IWebHostEnvironment environment)
    {
        _service = service;
        _stateProtector = stateProtector;
        _companyContext = companyContext;
        _timeProvider = timeProvider;
        _environment = environment;
    }

    [HttpGet("google/callback")]
    public Task<IActionResult> GoogleAsync(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken cancellationToken) =>
        CompleteAsync(ExternalAccountProvider.Google, code, state, error, cancellationToken);

    [HttpGet("microsoft365/callback")]
    public Task<IActionResult> MicrosoftAsync(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken cancellationToken) =>
        CompleteAsync(ExternalAccountProvider.Microsoft365, code, state, error, cancellationToken);

    private async Task<IActionResult> CompleteAsync(
        ExternalAccountProvider expectedProvider, string? code,
        string? protectedState, string? error, CancellationToken cancellationToken)
    {
        if (!TryState(protectedState, out var state))
            return Problem(title: "Calendar authorization failed.", detail: "Calendar OAuth state was invalid.", statusCode: 401);
        if (state.Provider != expectedProvider)
            return Problem(title: "Calendar authorization failed.", detail: "Calendar OAuth provider did not match the callback.", statusCode: 401);
        _companyContext.SetCompanyId(null);
        if (!string.IsNullOrWhiteSpace(error)) return RedirectResult(state, "denied", error);
        if (string.IsNullOrWhiteSpace(code)) return RedirectResult(state, "failed", "calendar_oauth_callback_missing_code");
        var callback = BuildCallbackUri(expectedProvider);
        try
        {
            var result = await _service.CompleteOAuthConnectionAsync(
                new CompleteCalendarOAuthConnectionCommand(
                    protectedState!, code, callback, expectedProvider), cancellationToken);
            return RedirectResult(state, "connected", null, result.CalendarConnectionId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or HttpRequestException)
        {
            return RedirectResult(state, "failed", ex.Message);
        }
    }

    private bool TryState(string? protectedState, out CalendarOAuthState state)
    {
        state = default!;
        if (string.IsNullOrWhiteSpace(protectedState)) return false;
        try
        {
            state = _stateProtector.Unprotect(protectedState);
            return state.CompanyId != Guid.Empty && state.UserId != Guid.Empty &&
                state.ExpiresUtc > _timeProvider.GetUtcNow().UtcDateTime;
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private Uri BuildCallbackUri(ExternalAccountProvider provider)
    {
        var builder = new UriBuilder(Request.Scheme, Request.Host.Host)
        {
            Path = $"/api/calendar-connections/{provider.ToStorageValue()}/callback"
        };
        if (Request.Host.Port.HasValue) builder.Port = Request.Host.Port.Value;
        return builder.Uri;
    }

    private IActionResult RedirectResult(
        CalendarOAuthState state, string status, string? detail, Guid? connectionId = null)
    {
        var target = state.ReturnUri is not null && AllowedReturnUri(state.ReturnUri)
            ? state.ReturnUri
            : new UriBuilder(Request.Scheme, Request.Host.Host)
            {
                Port = Request.Host.Port ?? -1,
                Path = "/settings/calendar-connections",
                Query = $"companyId={state.CompanyId:D}"
            }.Uri;
        var builder = new UriBuilder(target);
        var query = builder.Query.TrimStart('?');
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) values.Add(query);
        values.Add($"calendarConnection={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(detail)) values.Add($"calendarMessage={Uri.EscapeDataString(detail)}");
        if (connectionId.HasValue) values.Add($"calendarConnectionId={connectionId:D}");
        builder.Query = string.Join('&', values);
        return Redirect(builder.Uri.ToString());
    }

    private bool AllowedReturnUri(Uri uri) =>
        uri.Scheme is "http" or "https" &&
        (uri.AbsolutePath.StartsWith("/settings/calendar-connections", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.StartsWith("/app/sales/leads/", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.StartsWith("/app/sales/deals/", StringComparison.OrdinalIgnoreCase)) &&
        (string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
         _environment.IsDevelopment() && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
}
