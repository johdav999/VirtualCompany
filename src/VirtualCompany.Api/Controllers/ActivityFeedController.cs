using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/activity-feed")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ActivityFeedController : ControllerBase
{
    private readonly IActivityEventStore _activityEvents;

    public ActivityFeedController(IActivityEventStore activityEvents)
    {
        _activityEvents = activityEvents;
    }

    [HttpGet]
    public async Task<ActionResult<ActivityFeedPageDto>> ListAsync(
        Guid companyId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? agentId,
        [FromQuery] string? department,
        [FromQuery] Guid? task,
        [FromQuery] Guid? taskId,
        [FromQuery] string? eventType,
        [FromQuery] string? status,
        [FromQuery] string? timeframe,
        [FromQuery(Name = "from")] DateTime? fromUtc,
        [FromQuery(Name = "to")] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _activityEvents.QueryFeedAsync(companyId, new ActivityFeedQuery(cursor, pageSize, agentId, department, taskId ?? task, eventType, status, fromUtc, toUtc, timeframe), cancellationToken));
        }
        catch (ActivityFeedValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("correlations/{correlationId}")]
    public async Task<ActionResult<ActivityCorrelationTimelineDto>> GetCorrelationTimelineAsync(
        Guid companyId,
        string correlationId,
        [FromQuery] Guid? selectedActivityEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _activityEvents.QueryCorrelationTimelineAsync(companyId, new ActivityCorrelationTimelineQuery(correlationId, selectedActivityEventId), cancellationToken));
        }
        catch (ActivityFeedValidationException ex)
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

    [HttpGet("{activityEventId:guid}/correlation")]
    public async Task<ActionResult<ActivityCorrelationTimelineDto>> GetCorrelationTimelineForActivityAsync(
        Guid companyId,
        Guid activityEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _activityEvents.QueryCorrelationTimelineForActivityAsync(companyId, activityEventId, cancellationToken));
        }
        catch (ActivityFeedValidationException ex)
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

    [HttpGet("{activityEventId:guid}/links")]
    public async Task<ActionResult<ActivityLinkedEntitiesDto>> GetLinkedEntitiesAsync(
        Guid companyId,
        Guid activityEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _activityEvents.QueryLinkedEntitiesAsync(companyId, activityEventId, cancellationToken));
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

    [HttpGet("/api/timeline/correlations/{correlationId}")]
    public Task<ActionResult<ActivityCorrelationTimelineDto>> GetCorrelationTimelineAliasAsync(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ModelState.AddModelError("companyId", "Company-scoped route /api/companies/{companyId}/activity-feed/correlations/{correlationId} is required.");
        return Task.FromResult<ActionResult<ActivityCorrelationTimelineDto>>(ValidationProblem(ModelState));
    }

    [HttpPost("/internal/companies/{companyId:guid}/activity-events")]
    public async Task<ActionResult<ActivityEventDto>> PersistAsync(
        Guid companyId,
        [FromBody] PersistActivityEventCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var activityEvent = await _activityEvents.PersistAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(ListAsync), new { companyId }, activityEvent);
        }
        catch (ActivityFeedValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("/api/tenants/{tenantId:guid}/activity-feed")]
    [HttpGet("/api/tenants/{tenantId:guid}/agent-activity")]
    public Task<ActionResult<ActivityFeedPageDto>> ListTenantAliasAsync(
        Guid tenantId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? agentId,
        [FromQuery] string? department,
        [FromQuery] Guid? task,
        [FromQuery] Guid? taskId,
        [FromQuery] string? eventType,
        [FromQuery] string? status,
        [FromQuery] string? timeframe,
        [FromQuery(Name = "from")] DateTime? fromUtc,
        [FromQuery(Name = "to")] DateTime? toUtc,
        CancellationToken cancellationToken) =>
        ListAsync(tenantId, cursor, pageSize, agentId, department, task, taskId, eventType, status, timeframe, fromUtc, toUtc, cancellationToken);

    [HttpGet("/api/companies/{companyId:guid}/agent-activity")]
    public Task<ActionResult<ActivityFeedPageDto>> ListAgentActivityAliasAsync(
        Guid companyId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? agentId,
        [FromQuery] string? department,
        [FromQuery] Guid? task,
        [FromQuery] Guid? taskId,
        [FromQuery] string? eventType,
        [FromQuery] string? status,
        [FromQuery] string? timeframe,
        [FromQuery(Name = "from")] DateTime? fromUtc,
        [FromQuery(Name = "to")] DateTime? toUtc,
        CancellationToken cancellationToken) =>
        ListAsync(companyId, cursor, pageSize, agentId, department, task, taskId, eventType, status, timeframe, fromUtc, toUtc, cancellationToken);

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}
