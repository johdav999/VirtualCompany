using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/tasks")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TasksController : ControllerBase
{
    private readonly ICompanyTaskCommandService _taskCommands;
    private readonly ICompanyTaskQueryService _taskQueries;

    public TasksController(
        ICompanyTaskCommandService taskCommands,
        ICompanyTaskQueryService taskQueries)
    {
        _taskCommands = taskCommands;
        _taskQueries = taskQueries;
    }

    [HttpPost]
    public async Task<ActionResult<TaskCommandResultDto>> CreateAsync(
        Guid companyId,
        [FromBody] CreateTaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _taskCommands.CreateTaskAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetByIdAsync), new { companyId, taskId = result.Id }, result);
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (AgentAssignmentValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpPost("{taskId:guid}/subtasks")]
    public async Task<ActionResult<TaskCommandResultDto>> CreateSubtaskAsync(
        Guid companyId,
        Guid taskId,
        [FromBody] CreateSubtaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _taskCommands.CreateSubtaskAsync(companyId, taskId, command, cancellationToken);
            return CreatedAtAction(nameof(GetByIdAsync), new { companyId, taskId = result.Id }, result);
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (AgentAssignmentValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<TaskDetailDto>> GetByIdAsync(
        Guid companyId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _taskQueries.GetByIdAsync(companyId, new GetTaskByIdQuery(taskId), cancellationToken));
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

    [HttpGet]
    public async Task<ActionResult<TaskListResultDto>> ListAsync(
        Guid companyId,
        [FromQuery] string? status,
        [FromQuery] Guid? assignedAgentId,
        [FromQuery] Guid? parentTaskId,
        [FromQuery] DateTime? dueBefore,
        [FromQuery] DateTime? dueAfter,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new ListTasksQuery(status, assignedAgentId, parentTaskId, dueBefore, dueAfter, skip, take);
            return Ok(await _taskQueries.ListAsync(companyId, query, cancellationToken));
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPatch("{taskId:guid}/status")]
    public async Task<ActionResult<TaskCommandResultDto>> UpdateStatusAsync(
        Guid companyId,
        Guid taskId,
        [FromBody] UpdateTaskStatusCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _taskCommands.UpdateStatusAsync(companyId, taskId, command, cancellationToken));
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpPatch("{taskId:guid}/assignment")]
    public async Task<ActionResult<TaskCommandResultDto>> ReassignAsync(
        Guid companyId,
        Guid taskId,
        [FromBody] ReassignTaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _taskCommands.ReassignAsync(companyId, taskId, command, cancellationToken));
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (AgentAssignmentValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}
