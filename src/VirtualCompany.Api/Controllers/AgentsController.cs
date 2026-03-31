using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agents")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class AgentsController : ControllerBase
{
    private readonly ICompanyAgentService _agentService;

    public AgentsController(ICompanyAgentService agentService)
    {
        _agentService = agentService;
    }

    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<AgentTemplateCatalogItemDto>>> GetTemplatesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var templates = await _agentService.GetTemplatesAsync(companyId, cancellationToken);
            return Ok(templates);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyAgentSummaryDto>>> GetRosterAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var roster = await _agentService.GetRosterAsync(companyId, cancellationToken);
            return Ok(roster);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("from-template")]
    public async Task<ActionResult<CreateAgentFromTemplateResultDto>> CreateFromTemplateAsync(
        Guid companyId,
        [FromBody] CreateAgentFromTemplateCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _agentService.CreateFromTemplateAsync(companyId, command, cancellationToken);
            return Ok(result);
        }
        catch (AgentValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (AgentTemplateNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    public Task<ActionResult<CreateAgentFromTemplateResultDto>> HireAsync(
        Guid companyId,
        [FromBody] CreateAgentFromTemplateCommand command,
        CancellationToken cancellationToken)
    {
        return CreateFromTemplateAsync(companyId, command, cancellationToken);
    }
}
