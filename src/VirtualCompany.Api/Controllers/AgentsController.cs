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
    private readonly IAgentStatusAggregationService _agentStatusAggregationService;
    private readonly IAgentToolExecutionService _agentToolExecutionService;
    private readonly IAgentScheduledTriggerService _agentScheduledTriggerService;

    public AgentsController(
        ICompanyAgentService agentService,
        IAgentStatusAggregationService agentStatusAggregationService,
        IAgentToolExecutionService agentToolExecutionService,
        IAgentScheduledTriggerService agentScheduledTriggerService)
    {
        _agentService = agentService;
        _agentStatusAggregationService = agentStatusAggregationService;
        _agentToolExecutionService = agentToolExecutionService;
        _agentScheduledTriggerService = agentScheduledTriggerService;
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

    [HttpGet("roster")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentRosterResponseDto>> GetRosterViewAsync(
        Guid companyId,
        [FromQuery] string? department,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = new AgentRosterFilterDto(department, status);
            var roster = await _agentService.GetRosterViewAsync(companyId, filter, cancellationToken);
            return Ok(roster);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("status")]
    [HttpGet("status-cards")]
    [Authorize(Policy = CompanyPolicies.CompanyMember)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AgentStatusCardsResponseDto>> GetStatusCardsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var statusCards = await _agentStatusAggregationService.GetStatusCardsAsync(companyId, cancellationToken);
            return Ok(statusCards);
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

    [HttpGet("{agentId:guid}/status-detail")]
    [Authorize(Policy = CompanyPolicies.CompanyMember)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AgentStatusDetailDto>> GetStatusDetailAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _agentStatusAggregationService.GetStatusDetailAsync(companyId, agentId, cancellationToken);
            return Ok(detail);
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

    [HttpGet("{agentId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyMember)]
    public async Task<ActionResult<AgentProfileViewDto>> GetProfileViewAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _agentService.GetProfileViewAsync(companyId, agentId, cancellationToken);
            return Ok(profile);
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

    [HttpGet("{agentId:guid}/profile")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentOperatingProfileDto>> GetOperatingProfileAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _agentService.GetOperatingProfileAsync(companyId, agentId, cancellationToken);
            return Ok(profile);
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

    [HttpPut("{agentId:guid}/profile")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentOperatingProfileDto>> UpdateOperatingProfileAsync(
        Guid companyId,
        Guid agentId,
        [FromBody] UpdateAgentOperatingProfileCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _agentService.UpdateOperatingProfileAsync(companyId, agentId, command, cancellationToken);
            return Ok(profile);
        }
        catch (AgentValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
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

    [HttpGet("{agentId:guid}/schedule-triggers")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<IReadOnlyList<AgentScheduledTriggerDto>>> ListScheduleTriggersAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var triggers = await _agentScheduledTriggerService.ListAsync(companyId, agentId, cancellationToken);
            return Ok(triggers);
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

    [HttpGet("{agentId:guid}/schedule-triggers/{triggerId:guid}", Name = nameof(GetScheduleTriggerAsync))]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentScheduledTriggerDto>> GetScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await _agentScheduledTriggerService.GetAsync(companyId, agentId, triggerId, cancellationToken);
            return Ok(trigger);
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

    [HttpPost("{agentId:guid}/schedule-triggers")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentScheduledTriggerDto>> CreateScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        [FromBody] CreateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await _agentScheduledTriggerService.CreateAsync(companyId, agentId, command, cancellationToken);
            return CreatedAtAction(
                nameof(GetScheduleTriggerAsync),
                new { companyId, agentId, triggerId = trigger.Id },
                trigger);
        }
        catch (AgentScheduledTriggerValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
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

    [HttpPut("{agentId:guid}/schedule-triggers/{triggerId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentScheduledTriggerDto>> UpdateScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        [FromBody] UpdateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await _agentScheduledTriggerService.UpdateAsync(companyId, agentId, triggerId, command, cancellationToken);
            return Ok(trigger);
        }
        catch (AgentScheduledTriggerValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
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

    [HttpPost("{agentId:guid}/schedule-triggers/{triggerId:guid}/enable")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentScheduledTriggerDto>> EnableScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await _agentScheduledTriggerService.EnableAsync(companyId, agentId, triggerId, cancellationToken);
            return Ok(trigger);
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

    [HttpPost("{agentId:guid}/schedule-triggers/{triggerId:guid}/disable")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<AgentScheduledTriggerDto>> DisableScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await _agentScheduledTriggerService.DisableAsync(companyId, agentId, triggerId, cancellationToken);
            return Ok(trigger);
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

    [HttpDelete("{agentId:guid}/schedule-triggers/{triggerId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> DeleteScheduleTriggerAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _agentScheduledTriggerService.DeleteAsync(companyId, agentId, triggerId, cancellationToken);
            return NoContent();
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

    [HttpPost("{agentId:guid}/executions")]
    public async Task<ActionResult<ExecuteAgentToolResultDto>> ExecuteToolAsync(
        Guid companyId,
        Guid agentId,
        [FromBody] ExecuteAgentToolCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _agentToolExecutionService.ExecuteAsync(companyId, agentId, command, cancellationToken);
            return Ok(result);
        }
        catch (AgentExecutionValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
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
