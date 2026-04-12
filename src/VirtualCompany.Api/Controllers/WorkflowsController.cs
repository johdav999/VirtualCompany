using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/workflows")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class WorkflowsController : ControllerBase
{
    private readonly ICompanyWorkflowService _workflows;

    public WorkflowsController(ICompanyWorkflowService workflows)
    {
        _workflows = workflows;
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyList<WorkflowCatalogItemDto>>> ListCatalogAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.ListCatalogAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // v1 keeps this path constrained to known predefined catalog codes and schemas.
    [HttpPost("definitions")]
    public async Task<ActionResult<WorkflowDefinitionDto>> CreateDefinitionAsync(
        Guid companyId,
        [FromBody] CreateWorkflowDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.CreateDefinitionAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetDefinitionAsync), new { companyId, definitionId = result.Id }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("definitions/{definitionId:guid}/versions")]
    public async Task<ActionResult<WorkflowDefinitionDto>> CreateDefinitionVersionAsync(
        Guid companyId,
        Guid definitionId,
        [FromBody] CreateWorkflowDefinitionVersionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.CreateDefinitionVersionAsync(companyId, definitionId, command, cancellationToken);
            return CreatedAtAction(nameof(GetDefinitionAsync), new { companyId, definitionId = result.Id }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpGet("definitions")]
    public async Task<ActionResult<IReadOnlyList<WorkflowDefinitionDto>>> ListDefinitionsAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] bool activeOnly = false,
        [FromQuery] bool latestOnly = false,
        [FromQuery] bool includeSystem = true)
    {
        try
        {
            return Ok(await _workflows.ListDefinitionsAsync(companyId, activeOnly, latestOnly, includeSystem, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("definitions/{definitionId:guid}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetDefinitionAsync(
        Guid companyId,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.GetDefinitionAsync(companyId, definitionId, cancellationToken));
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

    [HttpPost("definitions/{definitionId:guid}/triggers")]
    public async Task<ActionResult<WorkflowTriggerDto>> CreateTriggerAsync(
        Guid companyId,
        Guid definitionId,
        [FromBody] CreateWorkflowTriggerCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.CreateTriggerAsync(companyId, definitionId, command, cancellationToken);
            return CreatedAtAction(nameof(GetDefinitionAsync), new { companyId, definitionId }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpPost("definitions/{definitionId:guid}/start")]
    public async Task<ActionResult<WorkflowInstanceDto>> StartManualInstanceAsync(
        Guid companyId,
        Guid definitionId,
        [FromBody] StartManualWorkflowInstanceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.StartManualInstanceAsync(
                companyId,
                command with { DefinitionId = definitionId },
                cancellationToken);
            return CreatedAtAction(nameof(GetInstanceAsync), new { companyId, instanceId = result.Id }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpPost("definitions/by-code/{code}/start")]
    public async Task<ActionResult<WorkflowInstanceDto>> StartManualInstanceByCodeAsync(
        Guid companyId,
        string code,
        [FromBody] StartManualWorkflowByCodeCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.StartManualInstanceByCodeAsync(
                companyId,
                command with { Code = code },
                cancellationToken);
            return CreatedAtAction(nameof(GetInstanceAsync), new { companyId, instanceId = result.Id }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpPost("instances")]
    public async Task<ActionResult<WorkflowInstanceDto>> StartInstanceAsync(
        Guid companyId,
        [FromBody] StartWorkflowInstanceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflows.StartInstanceAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetInstanceAsync), new { companyId, instanceId = result.Id }, result);
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpGet("instances")]
    public async Task<ActionResult<IReadOnlyList<WorkflowInstanceDto>>> ListInstancesAsync(
        Guid companyId,
        [FromQuery] Guid? definitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.ListInstancesAsync(companyId, definitionId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("instances/{instanceId:guid}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetInstanceAsync(
        Guid companyId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.GetInstanceAsync(companyId, instanceId, cancellationToken));
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

    [HttpPatch("instances/{instanceId:guid}/state")]
    public async Task<ActionResult<WorkflowInstanceDto>> UpdateInstanceStateAsync(
        Guid companyId,
        Guid instanceId,
        [FromBody] UpdateWorkflowInstanceStateCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.UpdateInstanceStateAsync(companyId, instanceId, command, cancellationToken));
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
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

    [HttpGet("exceptions")]
    public async Task<ActionResult<IReadOnlyList<WorkflowExceptionDto>>> ListExceptionsAsync(
        Guid companyId,
        [FromQuery] string? status,
        [FromQuery] Guid? workflowInstanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.ListExceptionsAsync(companyId, status, workflowInstanceId, cancellationToken));
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("exceptions/{exceptionId:guid}")]
    public async Task<ActionResult<WorkflowExceptionDto>> GetExceptionAsync(
        Guid companyId,
        Guid exceptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.GetExceptionAsync(companyId, exceptionId, cancellationToken));
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

    [HttpPost("exceptions/{exceptionId:guid}/review")]
    public async Task<ActionResult<WorkflowExceptionDto>> ReviewExceptionAsync(
        Guid companyId,
        Guid exceptionId,
        [FromBody] ReviewWorkflowExceptionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _workflows.ReviewExceptionAsync(companyId, exceptionId, command, cancellationToken));
        }
        catch (WorkflowValidationException ex)
        {
            return WorkflowValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException)
        {
            return Conflict();
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

    private ActionResult WorkflowValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}
