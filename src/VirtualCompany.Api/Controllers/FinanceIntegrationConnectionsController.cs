using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
public sealed class FinanceIntegrationConnectionsController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IFortnoxOAuthSessionStore _fortnoxOAuthSessionStore;
    private readonly IFinanceIntegrationProviderRegistry _providerRegistry;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<FinanceIntegrationConnectionsController> _logger;

    public FinanceIntegrationConnectionsController(
        ICompanyContextAccessor companyContextAccessor,
        IFortnoxOAuthSessionStore fortnoxOAuthSessionStore,
        IFinanceIntegrationProviderRegistry providerRegistry,
        IWebHostEnvironment hostEnvironment,
        ILogger<FinanceIntegrationConnectionsController> logger)
    {
        _companyContextAccessor = companyContextAccessor;
        _fortnoxOAuthSessionStore = fortnoxOAuthSessionStore;
        _providerRegistry = providerRegistry;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations")]
    public async Task<ActionResult<IReadOnlyList<FinanceIntegrationProviderMetadataResponse>>> ProvidersAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var providers = new List<FinanceIntegrationProviderMetadataResponse>();

        foreach (var provider in _providerRegistry.Providers.OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var status = await provider.OAuth.GetStatusAsync(
                new GetFinanceIntegrationConnectionStatusQuery(provider.ProviderKey, companyId, userId),
                cancellationToken);

            providers.Add(new FinanceIntegrationProviderMetadataResponse(
                provider.ProviderKey,
                provider.DisplayName,
                provider.Capabilities,
                status));
        }

        return Ok(providers);
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/{providerKey}/status")]
    public async Task<ActionResult<FinanceIntegrationConnectionStatusResult>> StatusAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        var result = await provider.OAuth.GetStatusAsync(
            new GetFinanceIntegrationConnectionStatusQuery(provider.ProviderKey, companyId, ResolveUserId()),
            cancellationToken);

        return Ok(result);
    }

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/fortnox/oauth/start")]
    public async Task<IActionResult> StartFortnoxOAuthAsync(
        Guid companyId,
        [FromQuery] string? returnUri,
        [FromQuery] bool reconnect,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(FinanceIntegrationProviderKeys.Fortnox);
        if (provider is null)
        {
            return UnknownProvider(FinanceIntegrationProviderKeys.Fortnox);
        }

        FinanceIntegrationOAuthResult result;
        try
        {
            result = await provider.OAuth.BuildAuthorizationUrlAsync(
                new StartFinanceIntegrationOAuthConnectionCommand(
                    provider.ProviderKey,
                    companyId,
                    ResolveUserId(),
                    BuildReturnUri(returnUri, provider.ProviderKey),
                    reconnect),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsProviderDisabledException(provider.ProviderKey, ex))
        {
            return BadRequest(CreateProviderNotConfiguredProblem(provider.ProviderKey));
        }

        return Redirect(result.AuthorizationUrl.ToString());
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/fortnox/status")]
    public Task<ActionResult<FinanceIntegrationConnectionStatusResult>> FortnoxStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        StatusAsync(companyId, FinanceIntegrationProviderKeys.Fortnox, cancellationToken);

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/connect")]
    public async Task<ActionResult<StartFinanceIntegrationConnectionResponse>> ConnectAsync(
        Guid companyId,
        string providerKey,
        [FromBody] StartFinanceIntegrationConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        FinanceIntegrationOAuthResult result;
        try
        {
            result = await provider.OAuth.BuildAuthorizationUrlAsync(
                new StartFinanceIntegrationOAuthConnectionCommand(
                    provider.ProviderKey,
                    companyId,
                    ResolveUserId(),
                    BuildReturnUri(request.ReturnUri, provider.ProviderKey),
                    request.Reconnect),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsProviderDisabledException(provider.ProviderKey, ex))
        {
            return BadRequest(CreateProviderNotConfiguredProblem(provider.ProviderKey));
        }

        return Ok(new StartFinanceIntegrationConnectionResponse(result.AuthorizationUrl.ToString(), result.ExpiresUtc));
    }

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/reconnect")]
    public Task<ActionResult<StartFinanceIntegrationConnectionResponse>> ReconnectAsync(
        Guid companyId,
        string providerKey,
        [FromBody] StartFinanceIntegrationConnectionRequest request,
        CancellationToken cancellationToken) =>
        ConnectAsync(
            companyId,
            providerKey,
            request with
            {
                Reconnect = true
            },
            cancellationToken);

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/sync")]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/sync/now")]
    public async Task<ActionResult<FinanceIntegrationSyncResult>> SyncNowAsync(
        Guid companyId,
        string providerKey,
        [FromBody] SyncFinanceIntegrationNowRequest? request,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        try
        {
            var result = await provider.Sync.SyncAsync(
                new RunFinanceIntegrationSyncCommand(
                    provider.ProviderKey,
                    companyId,
                    request?.ConnectionId,
                    HttpContext.TraceIdentifier,
                    ResolveUserId(),
                    request?.FullSync ?? false),
                cancellationToken);
            return Ok(result);
        }
        catch (FortnoxApiException exception) when (provider.ProviderKey == FinanceIntegrationProviderKeys.Fortnox)
        {
            return StatusCode(exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : StatusCodes.Status502BadGateway, new { message = exception.SafeMessage });
        }
        catch (FortnoxApprovalRequiredException exception) when (provider.ProviderKey == FinanceIntegrationProviderKeys.Fortnox)
        {
            return Accepted(new { approvalId = exception.ApprovalId, message = exception.SafeMessage });
        }
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/{providerKey}/sync-history")]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/{providerKey}/sync/history")]
    public async Task<ActionResult<FinanceIntegrationSyncHistoryResult>> SyncHistoryAsync(
        Guid companyId,
        string providerKey,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        var result = await provider.Sync.GetHistoryAsync(
            new GetFinanceIntegrationSyncHistoryQuery(
                provider.ProviderKey,
                companyId,
                limit <= 0 ? 25 : Math.Min(limit, 100)),
            cancellationToken);

        return Ok(result);
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/{providerKey}/sync/history/{syncId:guid}")]
    public async Task<ActionResult<FinanceIntegrationSyncHistoryItem>> SyncHistoryDetailAsync(
        Guid companyId,
        string providerKey,
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        var result = await provider.Sync.GetHistoryAsync(
            new GetFinanceIntegrationSyncHistoryQuery(
                provider.ProviderKey,
                companyId,
                100),
            cancellationToken);

        var item = result.Items.FirstOrDefault(x => x.Id == syncId);
        if (item is null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/disconnect")]
    public async Task<ActionResult<FinanceIntegrationConnectionDisconnectResult>> DisconnectAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        var result = await provider.OAuth.DisconnectAsync(
            new DisconnectFinanceIntegrationConnectionCommand(provider.ProviderKey, companyId, ResolveUserId()),
            cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpDelete("api/companies/{companyId:guid}/finance/integrations/fortnox")]
    public Task<ActionResult<FinanceIntegrationConnectionDisconnectResult>> DisconnectFortnoxAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        DisconnectAsync(companyId, FinanceIntegrationProviderKeys.Fortnox, cancellationToken);

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpGet("api/companies/{companyId:guid}/finance/integrations/fortnox/oauth/callback")]
    public Task<ActionResult<FinanceIntegrationOAuthCompletionResult>> FortnoxCallbackAsync(
        Guid companyId,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery] string? error,
        CancellationToken cancellationToken) =>
        CallbackAsync(
            companyId,
            FinanceIntegrationProviderKeys.Fortnox,
            new CompleteFinanceIntegrationConnectionRequest(
                code,
                state ?? string.Empty,
                nonce,
                error),
            cancellationToken);

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/write-command")]
    public async Task<ActionResult<FinanceIntegrationWriteResult>> RequestWriteCommandApprovalAsync(
        Guid companyId,
        string providerKey,
        [FromBody] FinanceIntegrationWriteCommandRequest request,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        var result = await provider.WriteCommands.RequestApprovalAsync(
            new FinanceIntegrationWriteCommand(
                provider.ProviderKey,
                companyId,
                request.ConnectionId,
                request.ActorUserId,
                FinanceIntegrationWriteCommandTypes.Normalize(request.CommandType),
                request.HttpMethod,
                request.Path,
                request.TargetCompany,
                request.PayloadSummary,
                request.PayloadHash,
                new FinanceIntegrationWritePayload(request.SanitizedPayloadJson, request.ProviderPayloadType),
                request.WriteRequestId,
                request.CorrelationId,
                request.ApprovedApprovalId),
            cancellationToken);

        return result.CanExecute ? Ok(result) : Accepted(result);
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpGet("/finance/integrations/{providerKey}/connect")]
    public async Task<IActionResult> BrowserConnectAsync(
        string providerKey,
        [FromQuery] Guid companyId,
        [FromQuery] bool reconnect,
        [FromQuery] string? returnUri,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return RedirectToIntegrationStatus(providerKey, "failed", $"Finance integration provider '{providerKey}' is not registered.", companyId, null);
        }

        FinanceIntegrationOAuthResult result;
        try
        {
            result = await provider.OAuth.BuildAuthorizationUrlAsync(
                new StartFinanceIntegrationOAuthConnectionCommand(
                    provider.ProviderKey,
                    companyId,
                    ResolveUserId(),
                    BuildReturnUri(returnUri, provider.ProviderKey),
                    reconnect),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsProviderDisabledException(provider.ProviderKey, ex))
        {
            return RedirectToIntegrationStatus(
                provider.ProviderKey,
                "failed",
                CreateProviderNotConfiguredDetail(provider.ProviderKey),
                companyId,
                BuildReturnUri(returnUri, provider.ProviderKey));
        }

        return Redirect(result.AuthorizationUrl.ToString());
    }

    [Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
    [HttpGet("/finance/integrations/{providerKey}/callback")]
    public async Task<IActionResult> BrowserCallbackAsync(
        string providerKey,
        [FromQuery] Guid companyId,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return RedirectToIntegrationStatus(providerKey, "failed", $"Finance integration provider '{providerKey}' is not registered.", companyId == Guid.Empty ? null : companyId, null);
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return RedirectToIntegrationStatus(provider.ProviderKey, "failed", "Finance integration authorization state was missing.", companyId, null);
        }

        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(error))
        {
            return RedirectToIntegrationStatus(provider.ProviderKey, "failed", "Finance integration did not return an authorization code.", companyId, null);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning(
                "Finance integration provider returned OAuth error. ProviderKey: {ProviderKey}. Error: {ProviderError}. Description: {ProviderErrorDescription}.",
                provider.ProviderKey,
                error,
                errorDescription);
        }

        try
        {
            var result = await provider.OAuth.HandleCallbackAsync(
                new CompleteFinanceIntegrationOAuthConnectionCommand(
                    provider.ProviderKey,
                    companyId,
                    ResolveCallbackUserId(),
                    state,
                    code ?? string.Empty,
                    nonce,
                    string.IsNullOrWhiteSpace(errorDescription) ? error : $"{error}: {errorDescription}"),
                cancellationToken);

            return RedirectToIntegrationStatus(provider.ProviderKey, "connected", null, result.CompanyId, result.ReturnUri);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var safeMessage = ex is FortnoxOAuthException oauthException
                ? oauthException.SafeMessage
                : "Finance integration authorization could not be completed.";
            _logger.LogWarning(
                ex,
                "Finance integration browser callback failed. ProviderKey: {ProviderKey}. CompanyId: {CompanyId}.",
                provider.ProviderKey,
                companyId);
            var callbackState = await TryResolveCallbackStateAsync(provider.ProviderKey, state, cancellationToken);

            return RedirectToIntegrationStatus(
                provider.ProviderKey,
                "failed",
                safeMessage,
                callbackState.CompanyId ?? (companyId == Guid.Empty ? null : companyId),
                callbackState.ReturnUri);
        }
    }

    [Authorize(Policy = CompanyPolicies.FinanceView)]
    [RequireCompanyContext]
    [HttpPost("api/companies/{companyId:guid}/finance/integrations/{providerKey}/callback")]
    public async Task<ActionResult<FinanceIntegrationOAuthCompletionResult>> CallbackAsync(
        Guid companyId,
        string providerKey,
        [FromBody] CompleteFinanceIntegrationConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerKey);
        if (provider is null)
        {
            return UnknownProvider(providerKey);
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Finance integration authorization was invalid",
                Detail = "Finance integration authorization state was missing.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.Code) && string.IsNullOrWhiteSpace(request.ProviderError))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Finance integration authorization was invalid",
                Detail = "Finance integration did not return an authorization code.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await provider.OAuth.HandleCallbackAsync(
                new CompleteFinanceIntegrationOAuthConnectionCommand(
                    provider.ProviderKey,
                    companyId,
                    ResolveUserId(),
                    request.State,
                    request.Code ?? string.Empty,
                    request.Nonce,
                    request.ProviderError),
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex) when (ex is FortnoxOAuthException or UnauthorizedAccessException or ArgumentException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Finance integration authorization could not be completed",
                Detail = ex is FortnoxOAuthException oauthException ? oauthException.SafeMessage : "Finance integration authorization was invalid.",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    private IFinanceIntegrationProvider? ResolveProvider(string providerKey)
    {
        try
        {
            return _providerRegistry.GetRequired(providerKey);
        }
        catch (FinanceIntegrationProviderNotFoundException)
        {
            return null;
        }
    }

    private Guid ResolveUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");

    private Guid ResolveCallbackUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : Guid.Empty;

    private Uri? BuildReturnUri(string? explicitReturnUri, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(explicitReturnUri))
        {
            return null;
        }

        if (!Uri.TryCreate(explicitReturnUri, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme is not ("http" or "https") ||
            !IsAllowedReturnHost(returnUri) ||
            !IsAllowedReturnPath(returnUri.AbsolutePath, providerKey))
        {
            throw new ArgumentException("Finance integration return URI must be an absolute integration URL.", nameof(explicitReturnUri));
        }

        return returnUri;
    }

    private static bool IsAllowedReturnPath(string path, string providerKey) =>
        IsProviderPath(path, "/finance/integrations/", providerKey) ||
        IsProviderPath(path, "/finance/settings/integrations/", providerKey);

    private static bool IsProviderPath(string path, string prefix, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return false;
        }

        var normalizedProviderKey = providerKey.Trim();
        var normalizedPrefix = prefix.EndsWith('/') ? prefix : $"{prefix}/";
        return path.StartsWith($"{normalizedPrefix}{normalizedProviderKey}", StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool IsProviderDisabledException(string providerKey, InvalidOperationException exception) =>
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(exception.Message, "Fortnox integration is disabled.", StringComparison.Ordinal);

    private static ProblemDetails CreateProviderNotConfiguredProblem(string providerKey) =>
        new()
        {
            Title = string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase)
                ? "Fortnox is not configured"
                : "Finance integration is not configured",
            Detail = CreateProviderNotConfiguredDetail(providerKey),
            Status = StatusCodes.Status400BadRequest
        };

    private static string CreateProviderNotConfiguredDetail(string providerKey) =>
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase)
            ? "Add Fortnox client settings and enable the integration before connecting."
            : "Add provider client settings and enable the integration before connecting.";

    private NotFoundObjectResult UnknownProvider(string providerKey) =>
        NotFound(new ProblemDetails
        {
            Title = "Finance integration provider is not registered",
            Detail = $"Finance integration provider '{providerKey}' is not registered.",
            Status = StatusCodes.Status404NotFound
        });

    private IActionResult RedirectToIntegrationStatus(string providerKey, string status, string? message, Guid? companyId, Uri? returnUri)
    {
        var target = returnUri ?? new Uri($"{Request.Scheme}://{Request.Host}/finance/integrations/{Uri.EscapeDataString(providerKey)}");
        var builder = new UriBuilder(target);
        var query = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : $"{builder.Query.TrimStart('?')}&";
        var statusKey = string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase)
            ? "fortnoxConnection"
            : "integrationConnection";
        var messageKey = string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase)
            ? "fortnoxMessage"
            : "integrationMessage";

        query += $"{statusKey}={Uri.EscapeDataString(status)}&integrationProvider={Uri.EscapeDataString(providerKey)}";
        if (!string.IsNullOrWhiteSpace(message))
        {
            query += $"&{messageKey}={Uri.EscapeDataString(message)}";
        }

        if (companyId.HasValue)
        {
            query += $"&companyId={Uri.EscapeDataString(companyId.Value.ToString("D"))}";
        }

        builder.Query = query;
        return Redirect(builder.Uri.ToString());
    }

    private async Task<(Guid? CompanyId, Uri? ReturnUri)> TryResolveCallbackStateAsync(
        string providerKey,
        string? state,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state))
        {
            return (null, null);
        }

        try
        {
            var callbackState = await _fortnoxOAuthSessionStore.GetRedirectStateAsync(state, cancellationToken);
            if (callbackState is null)
            {
                return (null, null);
            }

            return (callbackState.CompanyId, callbackState.ReturnUri);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Finance integration callback redirect state could not be resolved. ProviderKey: {ProviderKey}.",
                providerKey);
            return (null, null);
        }
    }

    private static int GetDefaultPort(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

    public sealed record StartFinanceIntegrationConnectionRequest(string? ReturnUri, bool Reconnect = false);
    public sealed record SyncFinanceIntegrationNowRequest(Guid? ConnectionId = null, bool FullSync = false);
    public sealed record CompleteFinanceIntegrationConnectionRequest(string? Code, string State, string? Nonce = null, string? ProviderError = null);
    public sealed record StartFinanceIntegrationConnectionResponse(string AuthorizationUrl, DateTime ExpiresUtc);
    public sealed record FinanceIntegrationProviderMetadataResponse(
        string ProviderKey,
        string DisplayName,
        IReadOnlyCollection<string> Capabilities,
        FinanceIntegrationConnectionStatusResult Status);

    public sealed record FinanceIntegrationWriteCommandRequest(
        Guid? ConnectionId,
        Guid? ActorUserId,
        string? CommandType,
        string HttpMethod,
        string Path,
        string TargetCompany,
        string PayloadSummary,
        string PayloadHash,
        string SanitizedPayloadJson,
        Guid WriteRequestId,
        string? ProviderPayloadType = null,
        string? CorrelationId = null,
        Guid? ApprovedApprovalId = null);
}
