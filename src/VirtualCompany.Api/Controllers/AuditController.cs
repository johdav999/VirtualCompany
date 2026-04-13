using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/audit")]
[Authorize(Policy = CompanyPolicies.AuditReview)]
[RequireCompanyContext]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditQueryService _auditQueryService;

    public AuditController(IAuditQueryService auditQueryService)
    {
        _auditQueryService = auditQueryService;
    }

    [HttpGet]
    public async Task<ActionResult<AuditHistoryResult>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? taskId = null,
        [FromQuery] Guid? workflowInstanceId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null)
    {
        try
        {
            return Ok(await _auditQueryService.ListAsync(
                companyId,
                new AuditHistoryFilter(agentId, taskId, workflowInstanceId, fromUtc, toUtc, skip, take),
                cancellationToken));
        }
        catch (ArgumentException ex) when (ex.ParamName == "filter")
        {
            ModelState.AddModelError("dateRange", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{auditEventId:guid}")]
    public async Task<ActionResult<AuditDetailDto>> GetAsync(
        Guid companyId,
        Guid auditEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _auditQueryService.GetAsync(companyId, auditEventId, cancellationToken));
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
}