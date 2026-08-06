using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/platform/finance-integration-applications")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class FinanceIntegrationApplicationManagementController : ControllerBase
{
    private readonly IFinanceIntegrationApplicationManagementService _service;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public FinanceIntegrationApplicationManagementController(
        IFinanceIntegrationApplicationManagementService service,
        ICurrentUserAccessor currentUserAccessor)
    {
        _service = service;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public Task<FinanceIntegrationApplicationConfigurationList> GetAllAsync(
        CancellationToken cancellationToken) =>
        _service.GetAllAsync(cancellationToken);

    [HttpGet("{providerKey}")]
    public Task<FinanceIntegrationApplicationConfigurationDto> GetAsync(
        string providerKey,
        CancellationToken cancellationToken) =>
        _service.GetAsync(providerKey, cancellationToken);

    [HttpPut("{providerKey}")]
    public async Task<ActionResult<FinanceIntegrationApplicationConfigurationDto>> SaveAsync(
        string providerKey,
        [FromBody] SaveFinanceIntegrationApplicationConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SaveAsync(
                new SaveFinanceIntegrationApplicationConfigurationCommand(
                    providerKey,
                    request.Enabled,
                    request.ClientId ?? string.Empty,
                    request.ClientSecret,
                    request.RedirectUri ?? string.Empty,
                    request.Scopes ?? [],
                    ResolveUserId(),
                    ResolveCorrelationId()),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem(
                title: "The provider configuration is invalid.",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
        catch (FinanceIntegrationApplicationConfigurationException exception)
        {
            var status = exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status503ServiceUnavailable;
            return Problem(
                title: exception.IsConflict
                    ? "The provider configuration changed."
                    : "The provider configuration could not be saved.",
                detail: exception.Message,
                statusCode: status);
        }
    }

    [HttpPost("{providerKey}/validate")]
    public async Task<ActionResult<FinanceIntegrationApplicationValidationResult>> ValidateAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ValidateAsync(
                new ValidateFinanceIntegrationApplicationConfigurationCommand(
                    providerKey,
                    ResolveUserId(),
                    ResolveCorrelationId()),
                cancellationToken));
        }
        catch (FinanceIntegrationApplicationConfigurationException exception)
        {
            var status = exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status503ServiceUnavailable;
            return Problem(
                title: "The provider configuration could not be validated.",
                detail: exception.Message,
                statusCode: status);
        }
    }

    [HttpGet("{providerKey}/audit-history")]
    public Task<FinanceIntegrationApplicationAuditHistory> GetAuditHistoryAsync(
        string providerKey,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        _service.GetAuditHistoryAsync(providerKey, limit <= 0 ? 25 : limit, cancellationToken);

    private Guid ResolveUserId() =>
        _currentUserAccessor.UserId
        ?? throw new UnauthorizedAccessException("A resolved platform administrator is required.");

    private string? ResolveCorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId) &&
        !string.IsNullOrWhiteSpace(correlationId)
            ? correlationId.ToString()
            : HttpContext.TraceIdentifier;
}

public sealed record SaveFinanceIntegrationApplicationConfigurationRequest(
    bool Enabled,
    string? ClientId,
    string? ClientSecret,
    string? RedirectUri,
    IReadOnlyCollection<string>? Scopes);
