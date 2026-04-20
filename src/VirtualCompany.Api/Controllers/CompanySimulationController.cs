using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/simulation")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CompanySimulationController : ControllerBase
{
    private readonly ICompanySimulationStateService _simulationStateService;
    private readonly ILogger<CompanySimulationController> _logger;

    public CompanySimulationController(
        ICompanySimulationStateService simulationStateService,
        ILogger<CompanySimulationController> logger)
    {
        _simulationStateService = simulationStateService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<CompanySimulationStateDto>> GetStateAsync(
        Guid companyId,
        CancellationToken cancellationToken)
        => await ExecuteReadAsync(() => _simulationStateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), cancellationToken));

    [HttpPost("start")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanySimulationStateDto>> StartAsync(
        Guid companyId,
        [FromBody] StartCompanySimulationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Simulation start requested. CompanyId: {CompanyId}. StartSimulatedDateTime: {StartSimulatedDateTime}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}. DeterministicConfigurationJsonPresent: {HasDeterministicConfigurationJson}.",
                companyId,
                request.StartSimulatedDateTime,
                request.GenerationEnabled,
                request.Seed,
                !string.IsNullOrWhiteSpace(request.DeterministicConfigurationJson));

            var result = await _simulationStateService.StartAsync(
                new StartCompanySimulationCommand(
                    companyId,
                    request.StartSimulatedDateTime,
                    request.GenerationEnabled,
                    request.Seed,
                    request.DeterministicConfigurationJson),
                cancellationToken);

            _logger.LogInformation(
                "Simulation start completed. CompanyId: {CompanyId}. Status: {Status}. ActiveSessionId: {ActiveSessionId}. CurrentSimulatedDateTime: {CurrentSimulatedDateTime}. LastProgressionTimestamp: {LastProgressionTimestamp}. GenerationEnabled: {GenerationEnabled}.",
                companyId,
                result.Status,
                result.ActiveSessionId,
                result.CurrentSimulatedDateTime,
                result.LastProgressionTimestamp,
                result.GenerationEnabled);

            return CreatedAtAction(nameof(GetStateAsync), new { companyId }, result);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Simulation start forbidden. CompanyId: {CompanyId}.", companyId);
            return Forbid();
        }
        catch (SimulationBackendDisabledException ex)
        {
            _logger.LogWarning(ex, "Simulation start blocked because backend execution is disabled. CompanyId: {CompanyId}.", companyId);
            return Conflict(CreateSimulationExecutionDisabledProblemDetails(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Simulation start rejected because the state transition is invalid. CompanyId: {CompanyId}.", companyId);
            return Conflict(CreateProblemDetails(ex.Message, StatusCodes.Status409Conflict));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Simulation start rejected because the request is invalid. CompanyId: {CompanyId}.", companyId);
            return BadRequest(CreateProblemDetails(ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    [HttpPatch]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<CompanySimulationStateDto>> UpdateAsync(
        Guid companyId,
        [FromBody] UpdateCompanySimulationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.GenerationEnabled.HasValue && request.DeterministicConfigurationJson is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(UpdateCompanySimulationSettingsRequest.GenerationEnabled)] = ["Specify at least one mutable simulation setting."],
                [nameof(UpdateCompanySimulationSettingsRequest.DeterministicConfigurationJson)] = ["Specify at least one mutable simulation setting."]
            })
            {
                Title = "Simulation validation failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Update the simulation settings request and try again.",
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteMutationAsync(
            () => _simulationStateService.UpdateSettingsAsync(
                new UpdateCompanySimulationSettingsCommand(
                    companyId,
                    request.GenerationEnabled,
                    request.DeterministicConfigurationJson),
                cancellationToken));
    }

    [HttpPost("pause")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<ActionResult<CompanySimulationStateDto>> PauseAsync(Guid companyId, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(() => _simulationStateService.PauseAsync(new PauseCompanySimulationCommand(companyId), cancellationToken));

    [HttpPost("resume")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<ActionResult<CompanySimulationStateDto>> ResumeAsync(Guid companyId, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(() => _simulationStateService.ResumeAsync(new ResumeCompanySimulationCommand(companyId), cancellationToken));

    [HttpPost("step-forward")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<ActionResult<CompanySimulationStateDto>> StepForwardAsync(Guid companyId, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(() => _simulationStateService.StepForwardOneDayAsync(new StepForwardCompanySimulationOneDayCommand(companyId), cancellationToken));

    [HttpPost("stop")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<ActionResult<CompanySimulationStateDto>> StopAsync(Guid companyId, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(() => _simulationStateService.StopAsync(new StopCompanySimulationCommand(companyId), cancellationToken));

    private async Task<ActionResult<CompanySimulationStateDto>> ExecuteMutationAsync(
        Func<Task<CompanySimulationStateDto>> callback)
    {
        try
        {
            return Ok(await callback());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (SimulationBackendDisabledException ex)
        {
            return Conflict(CreateSimulationExecutionDisabledProblemDetails(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(CreateProblemDetails(ex.Message, StatusCodes.Status409Conflict));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    private async Task<ActionResult<CompanySimulationStateDto>> ExecuteReadAsync(
        Func<Task<CompanySimulationStateDto>> callback)
    {
        try
        {
            return Ok(await callback());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, StatusCodes.Status400BadRequest));
        }
    }


    private ProblemDetails CreateSimulationExecutionDisabledProblemDetails(string detail) =>
        new()
        {
            Title = "Simulation execution is disabled.",
            Detail = detail,
            Status = StatusCodes.Status409Conflict,
            Instance = HttpContext.Request.Path
        };
    private ProblemDetails CreateProblemDetails(string detail, int status) =>
        new()
        {
            Title = status switch
            {
                StatusCodes.Status404NotFound => "Simulation state was not found.",
                StatusCodes.Status409Conflict => "Simulation state transition is invalid.",
                _ => "Simulation request is invalid."
            },
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };
}

public sealed record StartCompanySimulationRequest(
    DateTime StartSimulatedDateTime,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null);

public sealed record UpdateCompanySimulationSettingsRequest(
    bool? GenerationEnabled = null,
    string? DeterministicConfigurationJson = null);
