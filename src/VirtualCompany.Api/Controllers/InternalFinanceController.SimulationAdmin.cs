using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("simulation/clock")]
    public async Task<ActionResult<CompanySimulationClockDto>> GetSimulationClockAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _companySimulationService.GetClockAsync(
                new GetCompanySimulationClockQuery(companyId),
                cancellationToken));

    [HttpPost("simulation/advance")]
    public Task<ActionResult<AdvanceCompanySimulationTimeResultDto>> AdvanceSimulationAsync(
        Guid companyId,
        [FromBody] AdvanceCompanySimulationTimeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            () => _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.TotalHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/dataset-generation")]
    public Task<ActionResult<FinanceSandboxDatasetGenerationResponse>> GetSandboxDatasetGenerationAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxDatasetGenerationAsync(companyId, cancellationToken),
            "Finance sandbox dataset generation data was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/anomaly-injection")]
    public Task<ActionResult<FinanceSandboxAnomalyInjectionResponse>> GetSandboxAnomalyInjectionAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxAnomalyInjectionAsync(companyId, cancellationToken),
            "Finance sandbox anomaly injection data was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/anomaly-injection/{anomalyId:guid}")]
    public Task<ActionResult<FinanceSandboxAnomalyDetailResponse>> GetSandboxAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxAnomalyDetailAsync(companyId, anomalyId, cancellationToken),
            "Finance sandbox anomaly detail was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/anomaly-injection")]
    public async Task<ActionResult<FinanceSandboxAnomalyDetailResponse>> InjectSandboxAnomalyAsync(
        Guid companyId,
        [FromBody] FinanceSandboxAnomalyInjectionRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxAnomalyInjectionRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the anomaly injection request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var profile = SandboxScenarioProfiles.First(x => string.Equals(x.Code, request.ScenarioProfileCode.Trim(), StringComparison.OrdinalIgnoreCase));
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(companyId, [], cancellationToken);
        if (affectedRecord is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["No finance records are available yet for anomaly injection. Generate a sandbox dataset first."] })
            {
                Title = "Finance validation failed",
                Detail = "Generate sandbox data before injecting anomalies.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var anomaly = new FinanceSeedAnomaly(Guid.NewGuid(), companyId, MapScenarioProfileToAnomalyType(profile.Code), profile.Code, [affectedRecord.RecordId], BuildExpectedDetectionMetadataJson(profile, affectedRecord));
        _dbContext.FinanceSeedAnomalies.Add(anomaly);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildSandboxAnomalyDetailAsync(companyId, anomaly.Id, cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/simulation-controls")]
    public Task<ActionResult<FinanceSandboxSimulationControlsResponse>> GetSandboxSimulationControlsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxSimulationControlsAsync(companyId, cancellationToken),
            "Finance sandbox simulation controls were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/simulation-controls/advance")]
    public async Task<ActionResult<FinanceSandboxProgressionRunSummaryResponse>> AdvanceSandboxSimulationAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSimulationAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSimulationAdvanceRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the simulation control request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.IncrementHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken);

            return BuildSandboxProgressionRunSummary("advance", result);
        });
    }

    private static readonly FinanceSandboxAnomalyScenarioProfileResponse[] SandboxScenarioProfiles =
    [
        new() { Code = "baseline", Name = "Baseline threshold breach", Description = "Registers a threshold-breach anomaly against an existing sandbox record." },
        new() { Code = "missing_receipt", Name = "Missing receipt", Description = "Registers a missing-receipt scenario for finance validation coverage." },
        new() { Code = "duplicate_vendor_charge", Name = "Duplicate vendor charge", Description = "Registers a duplicate-charge anomaly for accounts payable review flows." },
        new() { Code = "historical_baseline_deviation", Name = "Historical baseline deviation", Description = "Registers a historical-drift scenario against a representative sandbox record." }
    ];

    private sealed record SandboxFinanceRecordCandidate(
        Guid RecordId,
        string RecordType,
        string Reference);

    private static Dictionary<string, string[]> ValidateSandboxAnomalyInjectionRequest(Guid companyId, FinanceSandboxAnomalyInjectionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId != companyId) errors[nameof(FinanceSandboxAnomalyInjectionRequest.CompanyId)] = ["The request company does not match the active company context."];
        if (string.IsNullOrWhiteSpace(request.ScenarioProfileCode)) errors[nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["Select a scenario profile."];
        else if (!SandboxScenarioProfiles.Any(x => string.Equals(x.Code, request.ScenarioProfileCode.Trim(), StringComparison.OrdinalIgnoreCase))) errors[nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode)] = ["Select a supported scenario profile."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateSandboxSimulationAdvanceRequest(Guid companyId, FinanceSandboxSimulationAdvanceRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId != companyId) errors[nameof(FinanceSandboxSimulationAdvanceRequest.CompanyId)] = ["The request company does not match the active company context."];
        if (request.IncrementHours <= 0) errors[nameof(FinanceSandboxSimulationAdvanceRequest.IncrementHours)] = ["Enter a positive hour increment."];
        if (request.ExecutionStepHours is <= 0) errors[nameof(FinanceSandboxSimulationAdvanceRequest.ExecutionStepHours)] = ["Enter a positive execution step size."];
        return errors;
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpPost("sandbox-admin/simulation-controls/progression-run")]
    public async Task<ActionResult<FinanceSandboxProgressionRunSummaryResponse>> StartSandboxProgressionRunAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSimulationAdvanceRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSimulationAdvanceRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the simulation control request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _companySimulationService.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(
                    companyId,
                    request.IncrementHours,
                    request.ExecutionStepHours,
                    request.Accelerated),
                cancellationToken);
            return BuildSandboxProgressionRunSummary("progression_run", result);
        });
    }

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/tool-execution-visibility")]
    public Task<ActionResult<FinanceSandboxToolExecutionVisibilityResponse>> GetSandboxToolExecutionVisibilityAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxToolExecutionVisibilityAsync(companyId, cancellationToken),
            "Finance sandbox tool execution visibility was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/domain-events")]
    public Task<ActionResult<FinanceSandboxDomainEventsResponse>> GetSandboxDomainEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildSandboxDomainEventsAsync(companyId, cancellationToken),
            "Finance sandbox domain events were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-manifests")]
    public Task<ActionResult<FinanceTransparencyToolManifestListResponse>> GetTransparencyToolManifestsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolManifestsAsync(companyId, cancellationToken),
            "Finance transparency tool manifests were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-executions")]
    public Task<ActionResult<FinanceTransparencyToolExecutionHistoryResponse>> GetTransparencyToolExecutionsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolExecutionsAsync(companyId, cancellationToken),
            "Finance transparency tool executions were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/tool-executions/{executionId:guid}")]
    public Task<ActionResult<FinanceTransparencyToolExecutionDetailResponse>> GetTransparencyToolExecutionDetailAsync(
        Guid companyId,
        Guid executionId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyToolExecutionDetailAsync(companyId, executionId, cancellationToken),
            "Finance transparency tool execution detail was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/events")]
    public Task<ActionResult<FinanceTransparencyEventStreamResponse>> GetTransparencyEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyEventsAsync(companyId, cancellationToken),
            "Finance transparency events were not found.");

    [Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
    [HttpGet("sandbox-admin/transparency/events/{eventId:guid}")]
    public Task<ActionResult<FinanceTransparencyEventDetailResponse>> GetTransparencyEventDetailAsync(
        Guid companyId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        ExecuteReadOptionalAsync(
            () => BuildFinanceTransparencyEventDetailAsync(companyId, eventId, cancellationToken),
            "Finance transparency event detail was not found.");

}

