using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/tasks")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TasksController : ControllerBase
{
    private readonly ICompanyTaskCommandService _taskCommands;
    private readonly ICompanyTaskQueryService _taskQueries;
    private readonly ISingleAgentOrchestrationService _orchestrationService;
    private readonly IMultiAgentCoordinator _multiAgentCoordinator;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public TasksController(
        ICompanyTaskCommandService taskCommands,
        ICompanyTaskQueryService taskQueries,
        ISingleAgentOrchestrationService orchestrationService,
        IMultiAgentCoordinator multiAgentCoordinator,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _taskCommands = taskCommands;
        _taskQueries = taskQueries;
        _orchestrationService = orchestrationService;
        _multiAgentCoordinator = multiAgentCoordinator;
        _correlationContextAccessor = correlationContextAccessor;
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

    [HttpPost("{taskId:guid}/execute")]
    public async Task<ActionResult<OrchestrationResult>> ExecuteAsync(
        Guid companyId,
        Guid taskId,
        [FromBody] ExecuteSingleAgentTaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _orchestrationService.ExecuteAsync(
                new SingleAgentOrchestrationRequest(
                    companyId,
                    taskId,
                    command.AgentId,
                    command.InitiatingActorId,
                    command.InitiatingActorType,
                    ResolveCorrelationId(command.CorrelationId),
                    command.Intent,
                    command.ToolInvocations),
                cancellationToken));
        }
        catch (OrchestrationValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (AgentExecutionValidationException ex)
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

    [HttpPost("manager-worker-collaborations")]
    public async Task<ActionResult<MultiAgentCollaborationResultDto>> StartManagerWorkerCollaborationAsync(
        Guid companyId,
        [FromBody] StartMultiAgentCollaborationCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _multiAgentCoordinator.ExecuteAsync(
                command with
                {
                    CompanyId = companyId,
                    CorrelationId = ResolveCorrelationId(command.CorrelationId)
                },
                cancellationToken);

            return CreatedAtAction(nameof(GetByIdAsync), new { companyId, taskId = result.ParentTaskId }, result);
        }
        catch (MultiAgentCollaborationValidationException ex)
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

    private string ResolveCorrelationId(string? requestedCorrelationId) =>
        string.IsNullOrWhiteSpace(requestedCorrelationId)
            ? _correlationContextAccessor.CorrelationId ?? HttpContext.TraceIdentifier
            : requestedCorrelationId.Trim();
}

public sealed record ExecuteSingleAgentTaskCommand(
    Guid? AgentId = null,
    Guid? InitiatingActorId = null,
    string? InitiatingActorType = null,
    string? CorrelationId = null,
    string? Intent = null,
    IReadOnlyList<ToolInvocationRequest>? ToolInvocations = null);
