using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Escalations;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/escalations")]
[Authorize(Policy = CompanyPolicies.AuditReview)]
[RequireCompanyContext]
public sealed class EscalationsController : ControllerBase
{
    private readonly IEscalationQueryService _escalationQueries;

    public EscalationsController(IEscalationQueryService escalationQueries)
    {
        _escalationQueries = escalationQueries;
    }

    [HttpGet]
    public async Task<ActionResult<EscalationRecordListResult>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] Guid? sourceEntityId = null,
        [FromQuery] string? sourceEntityType = null,
        [FromQuery] Guid? policyId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null)
    {
        try
        {
            return Ok(await _escalationQueries.ListEscalationsAsync(
                companyId,
                new EscalationRecordFilter(
                    sourceEntityId,
                    sourceEntityType,
                    policyId,
                    correlationId,
                    fromUtc,
                    toUtc,
                    skip,
                    take),
                cancellationToken));
        }
        catch (ArgumentException ex) when (ex.ParamName == "filter")
        {
            ModelState.AddModelError("filter", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{escalationId:guid}")]
    public async Task<ActionResult<EscalationRecordDto>> GetAsync(
        Guid companyId,
        Guid escalationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _escalationQueries.GetEscalationAsync(companyId, escalationId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<PolicyEvaluationHistoryResult>> ListPolicyEvaluationHistoryAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] Guid? sourceEntityId = null,
        [FromQuery] string? sourceEntityType = null,
        [FromQuery] Guid? policyId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null)
    {
        try
        {
            return Ok(await _escalationQueries.ListPolicyEvaluationHistoryAsync(
                companyId,
                new PolicyEvaluationHistoryFilter(sourceEntityId, sourceEntityType, policyId, correlationId, fromUtc, toUtc, skip, take),
                cancellationToken));
        }
        catch (ArgumentException ex) when (ex.ParamName == "filter")
        {
            ModelState.AddModelError("filter", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
