using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/integrations/fortnox")]
[Authorize(Policy = CompanyPolicies.CompanyAdmin)]
[RequireCompanyContext]
public sealed class FortnoxConnectionsController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IFortnoxOAuthService _fortnoxOAuthService;
    private readonly IFortnoxSyncService _fortnoxSyncService;
    private readonly IWebHostEnvironment _hostEnvironment;

    public FortnoxConnectionsController(
        ICompanyContextAccessor companyContextAccessor,
        IFortnoxOAuthService fortnoxOAuthService,
        IFortnoxSyncService fortnoxSyncService,
        IWebHostEnvironment hostEnvironment)
    {
        _companyContextAccessor = companyContextAccessor;
        _fortnoxOAuthService = fortnoxOAuthService;
        _fortnoxSyncService = fortnoxSyncService;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("status")]
    public async Task<ActionResult<FortnoxConnectionStatusResult>> StatusAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var result = await _fortnoxOAuthService.GetStatusAsync(
            new GetFortnoxConnectionStatusQuery(companyId, ResolveUserId()),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("connect")]
    public async Task<ActionResult<StartFortnoxConnectionResponse>> ConnectAsync(
        Guid companyId,
        [FromBody] StartFortnoxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        FortnoxOAuthStartResult result;
        try
        {
            result = await _fortnoxOAuthService.BuildAuthorizationUrlAsync(
                new StartFortnoxOAuthConnectionCommand(
                    companyId,
                    ResolveUserId(),
                    BuildReturnUri(request.ReturnUri),
                    request.Reconnect),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsFortnoxDisabledException(ex))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Fortnox is not configured",
                Detail = "Add Fortnox client settings and enable the integration before connecting.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(new StartFortnoxConnectionResponse(result.AuthorizationUrl.ToString(), result.ExpiresUtc));
    }

    [HttpPost("sync")]
    public async Task<ActionResult<FortnoxSyncResult>> SyncNowAsync(
        Guid companyId,
        [FromBody] SyncFortnoxNowRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _fortnoxSyncService.SyncAsync(
                new RunFortnoxSyncCommand(companyId, request?.ConnectionId, HttpContext.TraceIdentifier),
                cancellationToken);
            return Ok(result);
        }
        catch (FortnoxApiException exception)
        {
            return StatusCode(exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : StatusCodes.Status502BadGateway, new { message = exception.SafeMessage });
        }
        catch (FortnoxApprovalRequiredException exception)
        {
            return Accepted(new { approvalId = exception.ApprovalId, message = exception.SafeMessage });
        }
    }

    [HttpGet("sync-history")]
    public async Task<ActionResult<FortnoxSyncHistoryResult>> SyncHistoryAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var result = await _fortnoxSyncService.GetHistoryAsync(
            new GetFortnoxSyncHistoryQuery(companyId, limit <= 0 ? 25 : Math.Min(limit, 100)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("disconnect")]
    public async Task<ActionResult<FortnoxConnectionDisconnectResult>> DisconnectAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var result = await _fortnoxOAuthService.DisconnectAsync(
            new DisconnectFortnoxConnectionCommand(companyId, ResolveUserId()),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("/finance/integrations/fortnox/connect")]
    public async Task<IActionResult> BrowserConnectAsync(
        [FromQuery] Guid companyId,
        [FromQuery] bool reconnect,
        [FromQuery] string? returnUri,
        CancellationToken cancellationToken)
    {
        FortnoxOAuthStartResult result;
        try
        {
            result = await _fortnoxOAuthService.BuildAuthorizationUrlAsync(
                new StartFortnoxOAuthConnectionCommand(companyId, ResolveUserId(), BuildReturnUri(returnUri), reconnect),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsFortnoxDisabledException(ex))
        {
            return RedirectToFortnoxStatus(
                "failed",
                "Add Fortnox client settings and enable the integration before connecting.",
                companyId,
                BuildReturnUri(returnUri));
        }

        return Redirect(result.AuthorizationUrl.ToString());
    }

    [HttpGet("/finance/integrations/fortnox/callback")]
    public async Task<IActionResult> BrowserCallbackAsync(
        [FromQuery] Guid companyId,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            return RedirectToFortnoxStatus("failed", "Fortnox authorization was invalid.", null, null);
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return RedirectToFortnoxStatus("failed", "Fortnox authorization state was missing.", companyId, null);
        }

        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(error))
        {
            return RedirectToFortnoxStatus("failed", "Fortnox did not return an authorization code.", companyId, null);
        }

        try
        {
            var result = await _fortnoxOAuthService.HandleCallbackAsync(
                new CompleteFortnoxOAuthConnectionCommand(
                    companyId,
                    ResolveUserId(),
                    state,
                    code ?? string.Empty,
                    nonce,
                    error),
                cancellationToken);

            return RedirectToFortnoxStatus("connected", null, result.CompanyId, result.ReturnUri);
        }
        catch (Exception ex) when (ex is FortnoxOAuthException or UnauthorizedAccessException or ArgumentException)
        {
            var safeMessage = ex is FortnoxOAuthException oauthException
                ? oauthException.SafeMessage
                : "Fortnox authorization could not be completed.";

            return RedirectToFortnoxStatus("failed", safeMessage, companyId, null);
        }
    }

    private Guid ResolveUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");

    private Uri? BuildReturnUri(string? explicitReturnUri)
    {
        if (string.IsNullOrWhiteSpace(explicitReturnUri))
        {
            return null;
        }

        if (!Uri.TryCreate(explicitReturnUri, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme is not ("http" or "https") ||
            !IsAllowedReturnHost(returnUri) ||
            !IsAllowedReturnPath(returnUri.AbsolutePath))
        {
            throw new ArgumentException("Fortnox return URI must be an absolute Fortnox integration URL.", nameof(explicitReturnUri));
        }

        return returnUri;
    }

    private static bool IsAllowedReturnPath(string path) =>
        path.StartsWith("/finance/integrations/fortnox", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/finance/settings/integrations/fortnox", StringComparison.OrdinalIgnoreCase);

    private static bool IsFortnoxDisabledException(InvalidOperationException exception) =>
        string.Equals(exception.Message, "Fortnox integration is disabled.", StringComparison.Ordinal);

    private bool IsAllowedReturnHost(Uri returnUri)
    {
        if (string.Equals(returnUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
            returnUri.Port == (Request.Host.Port ?? GetDefaultPort(returnUri.Scheme)))
        {
            return true;
        }

        return _hostEnvironment.IsDevelopment() &&
            string.Equals(returnUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult RedirectToFortnoxStatus(string status, string? message, Guid? companyId, Uri? returnUri)
    {
        var target = returnUri ?? new Uri($"{Request.Scheme}://{Request.Host}/finance/integrations/fortnox");
        var builder = new UriBuilder(target);
        var existing = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : $"{builder.Query.TrimStart('?')}&";
        builder.Query = $"{existing}fortnoxConnection={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.Query = $"{builder.Query.TrimStart('?')}&fortnoxMessage={Uri.EscapeDataString(message)}";
        }

        if (companyId.HasValue)
        {
            builder.Query = $"{builder.Query.TrimStart('?')}&companyId={Uri.EscapeDataString(companyId.Value.ToString("D"))}";
        }

        return Redirect(builder.Uri.ToString());
    }

    private static int GetDefaultPort(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

    public sealed record StartFortnoxConnectionRequest(string? ReturnUri, bool Reconnect = false);
    public sealed record SyncFortnoxNowRequest(Guid? ConnectionId = null);
    public sealed record StartFortnoxConnectionResponse(string AuthorizationUrl, DateTime ExpiresUtc);
}
