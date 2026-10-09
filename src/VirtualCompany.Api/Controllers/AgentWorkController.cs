using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agent-work")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class AgentWorkController(IAgentWorkQueryService queries, VirtualCompany.Application.Orchestration.ICollaborationEvidenceQueryService collaboration, IBusinessWorkEvidenceQueryService business) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AgentWorkBoardDto>> List(Guid companyId, [FromQuery] string? responsibility,
        [FromQuery] Guid? agentId, [FromQuery] string? objective, [FromQuery] string? state,
        [FromQuery] int skip = 0, [FromQuery] int take = 24, [FromQuery] bool perState = false, CancellationToken cancellationToken = default)
    {
        try { return Ok(await queries.ListAsync(new(companyId, responsibility, agentId, objective, state, skip, take, perState), cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Invalid work filters", Detail = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
    [HttpGet("business/{kind}/{id:guid}")]
    public async Task<ActionResult<BusinessWorkEvidenceDto>> Business(Guid companyId, string kind, Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await business.GetAsync(companyId, kind, id, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Invalid business identity", Detail = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
    [HttpGet("{kind}/{id:guid}")]
    public async Task<ActionResult<AgentWorkItemDto>> Detail(Guid companyId, string kind, Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await queries.GetAsync(companyId, kind, id, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Invalid work identity", Detail = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
    [HttpGet("{kind}/{id:guid}/collaboration")]
    public async Task<ActionResult<VirtualCompany.Application.Orchestration.CollaborationEvidenceDto>> Collaboration(
        Guid companyId, string kind, Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await collaboration.GetAsync(companyId, kind, id, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Invalid work identity", Detail = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
