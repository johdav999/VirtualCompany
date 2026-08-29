using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/bank-connections")]
public sealed class BankConnectionsController : ControllerBase
{
    private readonly IBankConnectionService _service;
    private readonly ICompanyContextAccessor _companyContext;
    private readonly IWebHostEnvironment _environment;
    public BankConnectionsController(IBankConnectionService service, ICompanyContextAccessor companyContext, IWebHostEnvironment environment)
    { _service = service; _companyContext = companyContext; _environment = environment; }

    [HttpGet]
    public Task<BankConnectionStatusResult> GetAsync(Guid companyId, CancellationToken cancellationToken) =>
        _service.GetStatusAsync(companyId, cancellationToken);

    [HttpGet("providers/{providerKey}/institutions")]
    public Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(Guid companyId, string providerKey, CancellationToken cancellationToken) =>
        _service.GetInstitutionsAsync(providerKey, cancellationToken);

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("connect")]
    public async Task<ActionResult<BankConsentSessionResult>> ConnectAsync(Guid companyId, StartBankConnectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var returnUri = ParseReturnUri(request.ReturnUri);
            var callback = new Uri($"{Request.Scheme}://{Request.Host}/finance/bank-connections/{Uri.EscapeDataString(request.ProviderKey)}/callback");
            return Ok(await _service.StartAsync(new StartBankConnectionCommand(companyId, UserId(), request.ProviderKey,
                request.InstitutionId, callback, returnUri, request.RequestedCapabilities ?? [], CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{connectionId:guid}/renew")]
    public async Task<ActionResult<BankConsentSessionResult>> RenewAsync(Guid companyId, Guid connectionId, RenewBankConnectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var callback = new Uri($"{Request.Scheme}://{Request.Host}/finance/bank-connections/{Uri.EscapeDataString(request.ProviderKey)}/callback");
            return Ok(await _service.RenewAsync(new RenewBankConnectionCommand(companyId, connectionId, UserId(), request.ExpectedVersion,
                callback, ParseReturnUri(request.ReturnUri), CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{connectionId:guid}/accounts/{discoveredAccountId:guid}/mapping")]
    public async Task<ActionResult<BankAccountMappingResult>> MapAsync(Guid companyId, Guid connectionId, Guid discoveredAccountId,
        MapBankAccountRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await _service.MapAccountAsync(new MapDiscoveredBankAccountCommand(companyId, connectionId,
            discoveredAccountId, request.CompanyBankAccountId, UserId(), request.ExpectedConnectionVersion,
            request.Reason, CorrelationId()), cancellationToken)); }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{connectionId:guid}/refresh")]
    public async Task<ActionResult<BankConnectionStatusResult>> RefreshAsync(Guid companyId, Guid connectionId,
        BankConnectionVersionRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await _service.RefreshAsync(new RefreshBankConnectionCommand(companyId, connectionId, UserId(), request.ExpectedVersion, CorrelationId()), cancellationToken)); }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{connectionId:guid}/suspend")]
    public async Task<ActionResult<BankConnectionStatusResult>> SuspendAsync(Guid companyId, Guid connectionId, ChangeBankConnectionStateRequest request, CancellationToken cancellationToken)
    {
        try { await _service.SuspendAsync(new ChangeBankConnectionStateCommand(companyId, connectionId, UserId(), request.ExpectedVersion, request.Reason, CorrelationId()), cancellationToken); return Ok(await _service.GetStatusAsync(companyId, cancellationToken)); }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{connectionId:guid}/disconnect")]
    public async Task<ActionResult<BankConnectionStatusResult>> DisconnectAsync(Guid companyId, Guid connectionId, ChangeBankConnectionStateRequest request, CancellationToken cancellationToken)
    {
        try { await _service.DisconnectAsync(new ChangeBankConnectionStateCommand(companyId, connectionId, UserId(), request.ExpectedVersion, request.Reason, CorrelationId()), cancellationToken); return Ok(await _service.GetStatusAsync(companyId, cancellationToken)); }
        catch (Exception exception) when (exception is BankConnectionOperationException or ArgumentException) { return ProblemFor(exception); }
    }

    [HttpGet("{connectionId:guid}/synchronization-access")]
    public Task<BankSynchronizationAccessResult> SynchronizationAccessAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken) =>
        _service.GetSynchronizationAccessAsync(companyId, connectionId, cancellationToken);

    private Guid UserId() => _companyContext.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private string? CorrelationId() => Request.Headers.TryGetValue("X-Correlation-Id", out var value) ? value.ToString() : HttpContext.TraceIdentifier;
    private Uri? ParseReturnUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            (!string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) && !(_environment.IsDevelopment() && uri.IsLoopback)) ||
            !uri.AbsolutePath.StartsWith("/finance/settings/bank-connections", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Return URI must target the bank connections settings page.");
        return uri;
    }
    private ActionResult ProblemFor(Exception exception)
    {
        var operation = exception as BankConnectionOperationException;
        var code = operation?.ReasonCode ?? "invalid_request";
        var status = operation?.IsConflict == true ? StatusCodes.Status409Conflict :
            code == BankConnectionReasonCodes.ProviderOutage ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest;
        var details = new ProblemDetails { Title = "Bank connection action could not be completed", Detail = operation?.SafeMessage ?? exception.Message, Status = status };
        details.Extensions["reasonCode"] = code;
        return StatusCode(status, details);
    }
}

[ApiController]
public sealed class BankConnectionCallbacksController : ControllerBase
{
    private readonly IBankConnectionService _service;
    private readonly ICompanyContextAccessor _companyContext;
    public BankConnectionCallbacksController(IBankConnectionService service, ICompanyContextAccessor companyContext) { _service = service; _companyContext = companyContext; }

    [Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
    [HttpGet("/finance/bank-connections/{providerKey}/callback")]
    public async Task<IActionResult> CallbackAsync(string providerKey, [FromQuery] string? state, [FromQuery] string? code,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var fallback = "/finance/settings/bank-connections?bankConnection=failed";
        if (string.IsNullOrWhiteSpace(state)) return Redirect(fallback + "&reason=callback_state_invalid");
        try
        {
            var providerError = string.IsNullOrWhiteSpace(errorDescription) ? error : $"{error}: {errorDescription}";
            var result = await _service.CompleteCallbackAsync(new CompleteBankConsentCallbackCommand(null, UserId(), providerKey,
                state, code, providerError, HttpContext.TraceIdentifier), cancellationToken);
            var target = result.ReturnUri?.ToString() ?? "/finance/settings/bank-connections";
            return Redirect(Append(target, "bankConnection=connected"));
        }
        catch (BankConnectionOperationException exception)
        { return Redirect(fallback + $"&reason={Uri.EscapeDataString(exception.ReasonCode)}"); }
    }
    private Guid UserId() => _companyContext.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private static string Append(string uri, string query) => uri + (uri.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;
}

public sealed record StartBankConnectionRequest(string ProviderKey, string InstitutionId, string? ReturnUri,
    IReadOnlyCollection<string>? RequestedCapabilities);
public sealed record RenewBankConnectionRequest(string ProviderKey, long ExpectedVersion, string? ReturnUri);
public sealed record MapBankAccountRequest(Guid CompanyBankAccountId, long ExpectedConnectionVersion, string Reason);
public sealed record BankConnectionVersionRequest(long ExpectedVersion);
public sealed record ChangeBankConnectionStateRequest(long ExpectedVersion, string? Reason);
