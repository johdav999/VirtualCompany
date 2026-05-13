using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/automation/outbound-policy")]
[Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
[RequireCompanyContext]
public sealed class OutboundAutomationController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IOutboundAutomationPolicyService _policies;

    public OutboundAutomationController(ICompanyContextAccessor companyContextAccessor, IOutboundAutomationPolicyService policies)
    {
        _companyContextAccessor = companyContextAccessor;
        _policies = policies;
    }

    [HttpGet]
    public Task<OutboundAutomationPolicyResponse> GetAsync(CancellationToken cancellationToken) =>
        _policies.GetPolicyAsync(CompanyId(), cancellationToken);

    [HttpPut]
    public async Task<ActionResult<OutboundAutomationPolicyResponse>> UpdateAsync([FromBody] UpdateOutboundAutomationPolicyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _policies.UpdatePolicyAsync(CompanyId(), UserId(), request, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "policy"] = [ex.Message] });
        }
    }

    private Guid CompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty ? companyId : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty ? userId : throw new UnauthorizedAccessException("A resolved user is required.");

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed."
        });
}
