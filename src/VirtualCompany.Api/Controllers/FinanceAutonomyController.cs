using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/autonomy")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAutonomyController : ControllerBase
{
    [HttpGet("grants")]
    public Task<IReadOnlyList<FinanceAutonomyGrantDto>> ListAsync(
        Guid companyId, [FromQuery] Guid? agentId,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken) =>
        service.ListAsync(companyId, agentId, cancellationToken);

    [HttpGet("grants/{grantId:guid}")]
    public async Task<ActionResult<FinanceAutonomyGrantDto>> GetAsync(
        Guid companyId, Guid grantId,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetAsync(companyId, grantId, cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("effective")]
    public async Task<ActionResult<FinanceAutonomyPolicySnapshotDto>> GetEffectiveAsync(
        Guid companyId, [FromQuery] Guid agentId, [FromQuery] string capabilityId,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetEffectivePolicyAsync(companyId, agentId, capabilityId, cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<FinanceAutonomyDecisionDto>> EvaluateAsync(
        Guid companyId, [FromBody] FinanceAutonomyEvaluationRequest request,
        [FromServices] IFinanceAutonomyPolicyEvaluator policy, CancellationToken cancellationToken)
    {
        if (request.CompanyId != companyId)
            return Validation(new FinanceAutonomyValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.CompanyId)] = ["The request company must match the route."]
            }));
        return Ok(await policy.EvaluateAsync(request, cancellationToken));
    }

    [HttpPost("grants")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyGrantDto>> CreateAsync(
        Guid companyId, [FromBody] CreateFinanceAutonomyGrantCommand command,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetAsync), new { companyId, grantId = result.Id }, result);
        }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("grants/{grantId:guid}/versions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyGrantDto>> CreateVersionAsync(
        Guid companyId, Guid grantId, [FromBody] CreateFinanceAutonomyGrantVersionCommand command,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.CreateVersionAsync(companyId, grantId, command, cancellationToken)); }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
        catch (FinanceAutonomyConcurrencyException ex) { return ConflictProblem(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("grants/{grantId:guid}/versions/{versionId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyGrantDto>> ActivateAsync(
        Guid companyId, Guid grantId, Guid versionId,
        [FromBody] ActivateFinanceAutonomyGrantVersionCommand command,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ActivateAsync(companyId, grantId, versionId, command, cancellationToken)); }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
        catch (FinanceAutonomyConcurrencyException ex) { return ConflictProblem(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("grants/{grantId:guid}/revoke")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyGrantDto>> RevokeAsync(
        Guid companyId, Guid grantId, [FromBody] RevokeFinanceAutonomyGrantCommand command,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.RevokeAsync(companyId, grantId, command, cancellationToken)); }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
        catch (FinanceAutonomyConcurrencyException ex) { return ConflictProblem(ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPut("control")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyControlDto>> SetControlAsync(
        Guid companyId, [FromBody] SetFinanceAutonomyControlCommand command,
        [FromServices] IFinanceAutonomyGrantService service, CancellationToken cancellationToken)
    {
        try { return Ok(await service.SetControlAsync(companyId, command, cancellationToken)); }
        catch (FinanceAutonomyValidationException ex) { return Validation(ex); }
        catch (FinanceAutonomyConcurrencyException ex) { return ConflictProblem(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private ActionResult Validation(FinanceAutonomyValidationException ex) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Finance autonomy policy validation failed",
            Status = StatusCodes.Status400BadRequest
        });

    private ActionResult ConflictProblem(string message) => Conflict(new ProblemDetails
    {
        Title = "Finance autonomy policy changed",
        Detail = message,
        Status = StatusCodes.Status409Conflict
    });
}
