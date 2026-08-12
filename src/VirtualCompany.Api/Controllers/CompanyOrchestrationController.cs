using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/operating")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CompanyOrchestrationController : ControllerBase
{
    [HttpGet("goals")]
    public Task<IReadOnlyList<CompanyGoalDto>> ListGoals(Guid companyId, [FromQuery] string? status,
        [FromServices] ICompanyGoalQueryService service, CancellationToken ct) => service.ListAsync(companyId, status, ct);

    [HttpGet("goals/{goalId:guid}")]
    public Task<CompanyGoalDto> GetGoal(Guid companyId, Guid goalId,
        [FromServices] ICompanyGoalQueryService service, CancellationToken ct) => service.GetAsync(companyId, goalId, ct);

    [HttpPost("goals")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyGoalDto>> CreateGoal(Guid companyId, CreateCompanyGoalCommand command,
        [FromServices] ICompanyGoalCommandService service, CancellationToken ct)
    { try { return Ok(await service.CreateAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPut("goals/{goalId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyGoalDto>> UpdateGoal(Guid companyId, Guid goalId, UpdateCompanyGoalCommand command,
        [FromServices] ICompanyGoalCommandService service, CancellationToken ct)
    { try { return Ok(await service.UpdateAsync(companyId, goalId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } catch (CompanyOperatingConcurrencyException ex) { return Conflict(new ProblemDetails { Title = "The goal changed", Detail = ex.Message, Status = 409 }); } }

    [HttpPost("goals/{goalId:guid}/{transition:regex(^(activate|pause|complete|cancel)$)}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyGoalDto>> TransitionGoal(Guid companyId, Guid goalId, string transition,
        [FromQuery] int expectedVersion, [FromServices] ICompanyGoalCommandService service, CancellationToken ct)
    {
        try { return Ok(transition.ToLowerInvariant() switch { "activate" => await service.ActivateAsync(companyId, goalId, expectedVersion, null, ct), "pause" => await service.PauseAsync(companyId, goalId, expectedVersion, null, ct), "complete" => await service.CompleteAsync(companyId, goalId, expectedVersion, null, ct), _ => await service.CancelAsync(companyId, goalId, expectedVersion, null, ct) }); }
        catch (CompanyOperatingValidationException ex) { return Validation(ex); } catch (CompanyOperatingConcurrencyException ex) { return Conflict(new ProblemDetails { Title = "The goal changed", Detail = ex.Message, Status = 409 }); }
    }

    [HttpGet("configuration")]
    public Task<CompanyOperatingConfigurationDto> GetConfiguration(Guid companyId,
        [FromServices] ICompanyOperatingConfigurationService service, CancellationToken ct) => service.GetAsync(companyId, ct);

    [HttpPut("configuration")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyOperatingConfigurationDto>> UpdateConfiguration(Guid companyId, UpdateCompanyOperatingConfigurationCommand command,
        [FromServices] ICompanyOperatingConfigurationService service, CancellationToken ct)
    { try { return Ok(await service.UpdateAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } catch (CompanyOperatingConcurrencyException ex) { return Conflict(new ProblemDetails { Title = "Operating settings changed", Detail = ex.Message, Status = 409 }); } }

    [HttpPost("pause")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyOperatingConfigurationDto>> Pause(Guid companyId, PauseCompanyOperationCommand command,
        [FromServices] ICompanyOperatingConfigurationService service, CancellationToken ct)
    { try { return Ok(await service.PauseAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPost("resume")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyOperatingConfigurationDto>> Resume(Guid companyId, ResumeCompanyOperationCommand command,
        [FromServices] ICompanyOperatingConfigurationService service, CancellationToken ct) => Ok(await service.ResumeAsync(companyId, command, ct));

    [HttpPost("emergency-stop")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyOperatingConfigurationDto>> EmergencyStop(Guid companyId,
        EmergencyStopCompanyOperationCommand command, [FromServices] ICompanyOperatingConfigurationService service,
        CancellationToken ct)
    { try { return Ok(await service.EmergencyStopAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPost("emergency-stop/clear")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanyOperatingConfigurationDto>> ClearEmergencyStop(Guid companyId,
        ClearEmergencyStopCommand command, [FromServices] ICompanyOperatingConfigurationService service,
        CancellationToken ct)
    { try { return Ok(await service.ClearEmergencyStopAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpGet("cycles")]
    public Task<IReadOnlyList<OperatingCycleDto>> ListCycles(Guid companyId, [FromQuery] int take,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct) => service.ListAsync(companyId, take <= 0 ? 20 : take, ct);

    [HttpGet("cycles/{cycleId:guid}")]
    public Task<OperatingCycleDto> GetCycle(Guid companyId, Guid cycleId,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct) => service.GetAsync(companyId, cycleId, ct);

    [HttpGet("snapshots")]
    public Task<IReadOnlyList<OperatingSnapshotDto>> ListSnapshots(Guid companyId, [FromQuery] int take,
        [FromServices] ICompanyOperatingSnapshotQueryService service, CancellationToken ct) =>
        service.ListAsync(companyId, take <= 0 ? 20 : take, ct);

    [HttpGet("snapshots/{snapshotId:guid}")]
    public Task<OperatingSnapshotDto> GetSnapshot(Guid companyId, Guid snapshotId,
        [FromServices] ICompanyOperatingSnapshotQueryService service, CancellationToken ct) =>
        service.GetAsync(companyId, snapshotId, ct);

    [HttpPost("cycles/recommend")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<OperatingCycleDto>> RunRecommendationCycle(Guid companyId, RequestOperatingCycleCommand command,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct)
    { try { return Ok(await service.RunRecommendationCycleAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPost("plans/{planId:guid}/review")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<OperatingCycleDto>> ReviewPlan(Guid companyId, Guid planId, ReviewOperatingPlanCommand command,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct)
    { try { return Ok(await service.ReviewPlanAsync(companyId, planId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPost("plans/{planId:guid}/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<OperatingCycleDto> CommitPlan(Guid companyId, Guid planId,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct) => service.CommitPlanAsync(companyId, planId, ct);

    [HttpPost("reviews/run")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWork(Guid companyId,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct) => service.ReviewCommittedWorkAsync(companyId, ct);

    [HttpGet("dispatches")]
    public Task<IReadOnlyList<OperatingDispatchDto>> ListDispatches(Guid companyId, [FromQuery] int take,
        [FromServices] IOperatingDispatchQueryService service, CancellationToken ct) =>
        service.ListAsync(companyId, take <= 0 ? 50 : take, ct);

    [HttpGet("initiatives/{initiativeId:guid}/collaboration")]
    public Task<IReadOnlyList<OperatingCollaborationParticipantDto>> ListCollaboration(Guid companyId,
        Guid initiativeId, [FromServices] IOperatingDispatchQueryService service, CancellationToken ct) =>
        service.ListCollaborationAsync(companyId, initiativeId, ct);

    [HttpGet("events")]
    public Task<IReadOnlyList<OperatingEventDto>> ListEvents(Guid companyId, [FromQuery] int take,
        [FromServices] ICompanyOperatingEventService service, CancellationToken ct) =>
        service.ListEventsAsync(companyId, take <= 0 ? 50 : take, ct);

    [HttpGet("cycle-requests")]
    public Task<IReadOnlyList<OperatingCycleRequestDto>> ListCycleRequests(Guid companyId, [FromQuery] int take,
        [FromServices] ICompanyOperatingEventService service, CancellationToken ct) =>
        service.ListRequestsAsync(companyId, take <= 0 ? 50 : take, ct);

    [HttpPost("controlled-actions/notifications")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<OperatingDecisionDto>> ProposeControlledNotification(Guid companyId, ProposeControlledNotificationCommand command,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct)
    { try { return Ok(await service.ProposeControlledNotificationAsync(companyId, command, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    [HttpPost("controlled-actions/{decisionId:guid}/execute")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<OperatingDecisionDto>> ExecuteControlledAction(Guid companyId, Guid decisionId,
        [FromServices] ICompanyOperatingCycleService service, CancellationToken ct)
    { try { return Ok(await service.ExecuteControlledActionAsync(companyId, decisionId, ct)); } catch (CompanyOperatingValidationException ex) { return Validation(ex); } }

    private ActionResult Validation(CompanyOperatingValidationException ex) => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase)) { Title = "Validation failed", Status = 400 });
}
