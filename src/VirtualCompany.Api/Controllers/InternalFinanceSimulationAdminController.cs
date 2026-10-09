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
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceSimulationAdminController : InternalFinanceControllerBase
{
    private readonly ICompanySimulationService _companySimulationService;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceToolProvider _financeToolProvider;
    private readonly ICompanyToolRegistry _toolRegistry;

    public InternalFinanceSimulationAdminController(
        ICompanySimulationService companySimulationService,
        VirtualCompanyDbContext dbContext,
        IFinanceToolProvider financeToolProvider,
        ICompanyToolRegistry toolRegistry,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceSimulationAdminController> logger)
        : base(initializationProblems, logger)
    {
        _companySimulationService = companySimulationService;
        _dbContext = dbContext;
        _financeToolProvider = financeToolProvider;
        _toolRegistry = toolRegistry;
    }
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

    private async Task<FinanceSandboxDatasetGenerationResponse?> BuildSandboxDatasetGenerationAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var transactionCount = await _dbContext.FinanceTransactions
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var invoiceCount = await _dbContext.FinanceInvoices
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var billCount = await _dbContext.FinanceBills
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var balanceCount = await _dbContext.FinanceBalances
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);
        var anomalyCount = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId)
            .CountAsync(cancellationToken);

        var lastGeneratedUtc = await _dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId && x.Action == "finance.sandbox.dataset.generated")
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => (DateTime?)x.OccurredUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceTransactions
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => (DateTime?)x.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken)
            ?? DateTime.UtcNow;

        return new FinanceSandboxDatasetGenerationResponse
        {
            ProfileName = "Tenant sandbox dataset",
            LastGeneratedUtc = lastGeneratedUtc,
            CoverageSummary = $"{transactionCount} transactions, {invoiceCount} invoices, {billCount} bills, {balanceCount} balances, and {anomalyCount} anomalies are available for sandbox validation.",
            AvailableProfiles =
            [
                "Tenant sandbox dataset",
                "Tenant sandbox dataset with anomaly validation"
            ]
        };
    }

    private async Task<FinanceSandboxAnomalyInjectionResponse?> BuildSandboxAnomalyInjectionAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var registryEntries = new List<FinanceSandboxAnomalyRegistryItemResponse>(anomalies.Count);
        foreach (var anomaly in anomalies)
        {
            registryEntries.Add(await BuildSandboxAnomalyRegistryItemAsync(anomaly, cancellationToken));
        }

        var lastInjectedUtc = anomalies.FirstOrDefault()?.CreatedUtc
            ?? await _dbContext.AuditEvents
                .Where(x => x.CompanyId == companyId && x.Action == "finance.sandbox.dataset.generated")
                .OrderByDescending(x => x.OccurredUtc)
                .Select(x => (DateTime?)x.OccurredUtc)
                .FirstOrDefaultAsync(cancellationToken)
            ?? DateTime.UtcNow;

        return new FinanceSandboxAnomalyInjectionResponse
        {
            Mode = "Seed anomaly scenarios",
            LastInjectedUtc = lastInjectedUtc,
            Observation = anomalies.Count == 0
                ? "No anomaly injections have been registered yet. Inject a scenario profile to populate the registry."
                : $"Tracking {anomalies.Count} sandbox anomaly registration(s) for the active company.",
            ActiveScenarios = anomalies
                .Select(x => ResolveSandboxScenarioProfile(x.ScenarioProfile).Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AvailableScenarioProfiles = SandboxScenarioProfiles,
            RegistryEntries = registryEntries
        };
    }

    private async Task<FinanceSandboxAnomalyDetailResponse?> BuildSandboxAnomalyDetailAsync(
        Guid companyId,
        Guid anomalyId,
        CancellationToken cancellationToken)
    {
        var anomaly = await _dbContext.FinanceSeedAnomalies
            .Where(x => x.CompanyId == companyId && x.Id == anomalyId)
            .SingleOrDefaultAsync(cancellationToken);

        return anomaly is null
            ? null
            : await BuildSandboxAnomalyDetailAsync(anomaly, cancellationToken);
    }

    private async Task<FinanceSandboxAnomalyDetailResponse> BuildSandboxAnomalyDetailAsync(
        FinanceSeedAnomaly anomaly,
        CancellationToken cancellationToken)
    {
        var profile = ResolveSandboxScenarioProfile(anomaly.ScenarioProfile);
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(anomaly.CompanyId, anomaly.GetAffectedRecordIds(), cancellationToken);

        return new FinanceSandboxAnomalyDetailResponse
        {
            Id = anomaly.Id,
            Type = anomaly.AnomalyType,
            Status = "registered",
            ScenarioProfileCode = profile.Code,
            ScenarioProfileName = profile.Name,
            AffectedRecordType = affectedRecord?.RecordType ?? "unknown",
            AffectedRecordId = affectedRecord?.RecordId,
            AffectedRecordReference = affectedRecord?.Reference ?? "Unavailable",
            CreatedUtc = anomaly.CreatedUtc,
            ExpectedDetectionMetadataJson = anomaly.ExpectedDetectionMetadataJson,
            Messages = BuildSandboxAnomalyMessages(profile, affectedRecord)
        };
    }

    private async Task<FinanceSandboxAnomalyRegistryItemResponse> BuildSandboxAnomalyRegistryItemAsync(
        FinanceSeedAnomaly anomaly,
        CancellationToken cancellationToken)
    {
        var profile = ResolveSandboxScenarioProfile(anomaly.ScenarioProfile);
        var affectedRecord = await ResolveSandboxFinanceRecordAsync(anomaly.CompanyId, anomaly.GetAffectedRecordIds(), cancellationToken);

        return new FinanceSandboxAnomalyRegistryItemResponse
        {
            Id = anomaly.Id,
            Type = anomaly.AnomalyType,
            Status = "registered",
            ScenarioProfileCode = profile.Code,
            ScenarioProfileName = profile.Name,
            AffectedRecordType = affectedRecord?.RecordType ?? "unknown",
            AffectedRecordId = affectedRecord?.RecordId,
            AffectedRecordReference = affectedRecord?.Reference ?? "Unavailable",
            CreatedUtc = anomaly.CreatedUtc,
            Messages = BuildSandboxAnomalyMessages(profile, affectedRecord)
        };
    }

    private static FinanceSandboxAnomalyScenarioProfileResponse ResolveSandboxScenarioProfile(string? code) =>
        SandboxScenarioProfiles.FirstOrDefault(x => string.Equals(x.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? new FinanceSandboxAnomalyScenarioProfileResponse
        {
            Code = NormalizeReviewToken(code) ?? "custom",
            Name = HumanizeReviewToken(code),
            Description = "Registers a sandbox anomaly scenario against an existing finance record."
        };

    private static IReadOnlyList<FinanceSandboxBackendMessageResponse> BuildSandboxAnomalyMessages(
        FinanceSandboxAnomalyScenarioProfileResponse profile,
        SandboxFinanceRecordCandidate? affectedRecord)
    {
        var messages = new List<FinanceSandboxBackendMessageResponse>
        {
            new()
            {
                Severity = "info",
                Code = "sandbox.anomaly.registered",
                Message = $"Scenario '{profile.Name}' is registered for sandbox review."
            }
        };

        if (affectedRecord is null)
        {
            messages.Add(new FinanceSandboxBackendMessageResponse
            {
                Severity = "warning",
                Code = "sandbox.anomaly.record_unresolved",
                Message = "The related finance record could not be resolved from the sandbox registry."
            });
        }

        return messages;
    }

    private async Task<FinanceSandboxSimulationControlsResponse?> BuildSandboxSimulationControlsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var clock = await _companySimulationService.GetClockAsync(
            new GetCompanySimulationClockQuery(companyId),
            cancellationToken);
        var runHistory = await BuildSandboxRunHistoryAsync(companyId, cancellationToken);
        var currentRun = runHistory.FirstOrDefault();

        return new FinanceSandboxSimulationControlsResponse
        {
            ClockMode = clock.Enabled ? "Simulated clock enabled" : "Live clock fallback",
            ReferenceUtc = clock.CurrentUtc,
            CheckpointLabel = currentRun is null
                ? "No simulation run has been recorded for the current sandbox."
                : $"{HumanizeReviewToken(currentRun.RunType)} completed {currentRun.AdvancedHours}h with {currentRun.Steps.Count} step(s).",
            Observation = currentRun is null
                ? "Advance simulation time or start a progression run to populate backend status and history."
                : currentRun.Messages.FirstOrDefault()?.Message
                    ?? "The latest simulation run completed without backend messages.",
            CurrentRun = currentRun,
            RunHistory = runHistory
        };
    }

    private async Task<IReadOnlyList<FinanceSandboxProgressionRunSummaryResponse>> BuildSandboxRunHistoryAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.FinanceSimulationStepLogs
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SandboxSimulationRunStepRow(
                x.RunId,
                x.StepNumber,
                x.WindowStartUtc,
                x.WindowEndUtc,
                x.ExecutionStepHours,
                x.TotalHoursProcessed,
                x.IsAccelerated,
                x.TransactionsGenerated,
                x.InvoicesGenerated,
                x.BillsGenerated,
                x.RecurringExpenseInstancesGenerated,
                x.EventsEmitted,
                x.CreatedUtc))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.RunId)
            .OrderByDescending(group => group.Max(step => step.CreatedUtc))
            .Take(10)
            .Select(BuildSandboxProgressionRunSummary)
            .ToArray();
    }

    private static FinanceSandboxProgressionRunSummaryResponse BuildSandboxProgressionRunSummary(IGrouping<Guid, SandboxSimulationRunStepRow> run)
    {
        var orderedSteps = run.OrderBy(step => step.StepNumber).ToArray();
        var firstStep = orderedSteps[0];
        var lastStep = orderedSteps[^1];
        var transactionsGenerated = orderedSteps.Sum(step => step.TransactionsGenerated);
        var invoicesGenerated = orderedSteps.Sum(step => step.InvoicesGenerated);
        var billsGenerated = orderedSteps.Sum(step => step.BillsGenerated);
        var recurringExpenseInstancesGenerated = orderedSteps.Sum(step => step.RecurringExpenseInstancesGenerated);
        var eventsEmitted = orderedSteps.Sum(step => step.EventsEmitted);
        var generatedRecordCount = transactionsGenerated + invoicesGenerated + billsGenerated + recurringExpenseInstancesGenerated;

        return new FinanceSandboxProgressionRunSummaryResponse
        {
            RunType = firstStep.IsAccelerated ? "progression_run" : "advance",
            Status = "completed",
            StartedUtc = firstStep.WindowStartUtc,
            CompletedUtc = lastStep.WindowEndUtc,
            AdvancedHours = firstStep.TotalHoursProcessed,
            ExecutionStepHours = firstStep.ExecutionStepHours,
            TransactionsGenerated = transactionsGenerated,
            InvoicesGenerated = invoicesGenerated,
            BillsGenerated = billsGenerated,
            RecurringExpenseInstancesGenerated = recurringExpenseInstancesGenerated,
            EventsEmitted = eventsEmitted,
            Messages = generatedRecordCount == 0
                ? [new FinanceSandboxBackendMessageResponse { Severity = "warning", Code = "sandbox.progression.no_output", Message = "The simulation run completed without generating new finance records." }]
                : [new FinanceSandboxBackendMessageResponse { Severity = "info", Code = "sandbox.progression.completed", Message = $"The simulation run completed and generated {generatedRecordCount} finance record(s)." }],
            Steps = orderedSteps.Select(step => new FinanceSandboxProgressionRunStepResponse
            {
                WindowStartUtc = step.WindowStartUtc,
                WindowEndUtc = step.WindowEndUtc,
                TransactionsGenerated = step.TransactionsGenerated,
                InvoicesGenerated = step.InvoicesGenerated,
                BillsGenerated = step.BillsGenerated,
                RecurringExpenseInstancesGenerated = step.RecurringExpenseInstancesGenerated,
                EventsEmitted = step.EventsEmitted
            }).ToArray()
        };
    }

    private static FinanceSandboxProgressionRunSummaryResponse BuildSandboxProgressionRunSummary(string runType, AdvanceCompanySimulationTimeResultDto result)
    {
        var generatedRecordCount = result.TransactionsGenerated + result.InvoicesGenerated + result.BillsGenerated + result.RecurringExpenseInstancesGenerated;
        return new FinanceSandboxProgressionRunSummaryResponse
        {
            RunType = runType,
            Status = "completed",
            StartedUtc = result.PreviousUtc,
            CompletedUtc = result.CurrentUtc,
            AdvancedHours = result.TotalHoursProcessed,
            ExecutionStepHours = result.ExecutionStepHours,
            TransactionsGenerated = result.TransactionsGenerated,
            InvoicesGenerated = result.InvoicesGenerated,
            BillsGenerated = result.BillsGenerated,
            RecurringExpenseInstancesGenerated = result.RecurringExpenseInstancesGenerated,
            EventsEmitted = result.EventsEmitted,
            Messages = generatedRecordCount == 0
                ? [new FinanceSandboxBackendMessageResponse { Severity = "warning", Code = "sandbox.progression.no_output", Message = "The progression run completed without generating new finance records." }]
                : [new FinanceSandboxBackendMessageResponse { Severity = "info", Code = "sandbox.progression.completed", Message = $"The progression run completed and generated {generatedRecordCount} finance record(s)." }],
            Steps = result.Logs.Select(log => new FinanceSandboxProgressionRunStepResponse
            {
                WindowStartUtc = log.WindowStartUtc,
                WindowEndUtc = log.WindowEndUtc,
                TransactionsGenerated = log.TransactionsGenerated,
                InvoicesGenerated = log.InvoicesGenerated,
                BillsGenerated = log.BillsGenerated,
                RecurringExpenseInstancesGenerated = log.RecurringExpenseInstancesGenerated,
                EventsEmitted = log.EventsEmitted
            }).ToArray()
        };
    }

    private sealed record SandboxSimulationRunStepRow(
        Guid RunId,
        int StepNumber,
        DateTime WindowStartUtc,
        DateTime WindowEndUtc,
        int ExecutionStepHours,
        int TotalHoursProcessed,
        bool IsAccelerated,
        int TransactionsGenerated,
        int InvoicesGenerated,
        int BillsGenerated,
        int RecurringExpenseInstancesGenerated,
        int EventsEmitted,
        DateTime CreatedUtc);

    private async Task<SandboxFinanceRecordCandidate?> ResolveSandboxFinanceRecordAsync(
        Guid companyId,
        IReadOnlyList<Guid> preferredIds,
        CancellationToken cancellationToken)
    {
        foreach (var recordId in preferredIds.Where(x => x != Guid.Empty))
        {
            var transaction = await _dbContext.FinanceTransactions
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "transaction", string.IsNullOrWhiteSpace(x.ExternalReference) ? $"Transaction {x.Id:D}" : x.ExternalReference))
                .FirstOrDefaultAsync(cancellationToken);
            if (transaction is not null) return transaction;

            var invoice = await _dbContext.FinanceInvoices
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "invoice", x.InvoiceNumber))
                .FirstOrDefaultAsync(cancellationToken);
            if (invoice is not null) return invoice;

            var bill = await _dbContext.FinanceBills
                .Where(x => x.CompanyId == companyId && x.Id == recordId)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "bill", x.BillNumber))
                .FirstOrDefaultAsync(cancellationToken);
            if (bill is not null) return bill;
        }

        return await _dbContext.FinanceTransactions
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SandboxFinanceRecordCandidate(x.Id, "transaction", string.IsNullOrWhiteSpace(x.ExternalReference) ? $"Transaction {x.Id:D}" : x.ExternalReference))
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceInvoices
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "invoice", x.InvoiceNumber))
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.FinanceBills
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new SandboxFinanceRecordCandidate(x.Id, "bill", x.BillNumber))
                .FirstOrDefaultAsync(cancellationToken);
    }

    private static string MapScenarioProfileToAnomalyType(string code) =>
        code.Trim().ToLowerInvariant() switch
        {
            "missing_receipt" => "missing_receipt",
            "duplicate_vendor_charge" => "duplicate_vendor_charge",
            "historical_baseline_deviation" => "historical_baseline_deviation",
            _ => "threshold_breach"
        };

    private static string BuildExpectedDetectionMetadataJson(FinanceSandboxAnomalyScenarioProfileResponse profile, SandboxFinanceRecordCandidate affectedRecord) =>
        new JsonObject
        {
            ["scenarioProfileCode"] = profile.Code,
            ["scenarioProfileName"] = profile.Name,
            ["affectedRecordType"] = affectedRecord.RecordType,
            ["affectedRecordReference"] = affectedRecord.Reference
        }.ToJsonString();

    private async Task<FinanceSandboxToolExecutionVisibilityResponse?> BuildSandboxToolExecutionVisibilityAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var attempts = await _dbContext.ToolExecutionAttempts
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CompletedUtc ?? x.StartedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (attempts.Count == 0)
        {
            return null;
        }

        return new FinanceSandboxToolExecutionVisibilityResponse
        {
            Summary = $"Observed {attempts.Count} recent sandbox tool execution(s) for the active company.",
            Items = attempts
                .Select(x => new FinanceSandboxToolExecutionItemResponse
                {
                    Name = x.ToolName,
                    Visibility = x.ResultPayload.Count > 0 || x.PolicyDecision.Count > 0
                        ? "Visible in admin timeline"
                        : "Visible with minimal telemetry",
                    LastStatus = x.Status.ToString()
                })
                .ToArray()
        };
    }

    private async Task<FinanceSandboxDomainEventsResponse?> BuildSandboxDomainEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var events = await _dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (events.Count == 0)
        {
            return null;
        }

        return new FinanceSandboxDomainEventsResponse
        {
            Summary = $"Showing the {events.Count} most recent sandbox-relevant audit event(s) for the active company.",
            Items = events
                .Select(x => new FinanceSandboxDomainEventItemResponse
                {
                    EventType = x.Action,
                    Status = x.Outcome,
                    OccurredAtUtc = x.OccurredUtc
                })
                .ToArray()
        };
    }

    private Task<FinanceTransparencyToolManifestListResponse?> BuildFinanceTransparencyToolManifestsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var financeRegistrations = _toolRegistry.ListTools()
            .Where(registration => registration.Scopes.Contains("finance"))
            .ToDictionary(registration => registration.ToolName, StringComparer.OrdinalIgnoreCase);

        var definitions = _toolRegistry.ListToolDefinitions()
            .Where(definition => financeRegistrations.ContainsKey(definition.ToolName))
            .OrderBy(definition => definition.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (definitions.Length == 0)
        {
            return Task.FromResult<FinanceTransparencyToolManifestListResponse?>(null);
        }

        var providerAdapterIdentity = _financeToolProvider.GetType().Name;
        return Task.FromResult<FinanceTransparencyToolManifestListResponse?>(new FinanceTransparencyToolManifestListResponse
        {
            Summary = $"Registered {definitions.Length} finance tool manifest(s) for provider adapter {providerAdapterIdentity}.",
            Items = definitions
                .Select(definition =>
                {
                    var contractSummary = BuildContractSummary(definition.InputSchema, definition.OutputSchema);
                    var schemaSummary = BuildSchemaSummary(definition.InputSchema, definition.OutputSchema);
                    return new FinanceTransparencyToolManifestItemResponse
                    {
                        ToolName = definition.ToolName,
                        Version = definition.Version,
                        VersionMetadata = BuildManifestVersionMetadata(definition.Version),
                        ContractSummary = contractSummary,
                        SchemaSummary = schemaSummary,
                        ManifestSource = "runtime_registry",
                        ProviderAdapterId = providerAdapterIdentity,
                        ProviderAdapterName = providerAdapterIdentity,
                        ProviderAdapterIdentity = providerAdapterIdentity
                    };
                })
                .ToArray()
        });
    }

    private async Task<FinanceTransparencyToolExecutionHistoryResponse?> BuildFinanceTransparencyToolExecutionsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var financeToolNames = BuildFinanceToolNameSet();
        var attempts = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.ExecutedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var items = attempts
            .Where(attempt => IsFinanceToolExecution(attempt, financeToolNames))
            .Take(25)
            .Select(BuildToolExecutionListItem)
            .ToArray();

        if (items.Length == 0)
        {
            return null;
        }

        return new FinanceTransparencyToolExecutionHistoryResponse
        {
            Summary = $"Showing {items.Length} recent finance tool execution(s) for the active company.",
            Items = items
        };
    }

    private async Task<FinanceTransparencyToolExecutionDetailResponse?> BuildFinanceTransparencyToolExecutionDetailAsync(
        Guid companyId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var financeToolNames = BuildFinanceToolNameSet();
        var attempt = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == executionId, cancellationToken);

        if (attempt is null || !IsFinanceToolExecution(attempt, financeToolNames))
        {
            return null;
        }

        var relatedRecords = await BuildExecutionRelatedRecordsAsync(companyId, attempt, cancellationToken);

        var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
        return new FinanceTransparencyToolExecutionDetailResponse
        {
            ExecutionId = attempt.Id,
            ToolName = attempt.ToolName,
            ToolVersion = attempt.ToolVersion,
            LifecycleState = attempt.Status.ToString().ToLowerInvariant(),
            RequestSummary = BuildToolExecutionRequestSummary(attempt),
            ResponseSummary = BuildToolExecutionResponseSummary(attempt),
            ExecutionTimestampUtc = attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc,
            CorrelationId = attempt.CorrelationId ?? string.Empty,
            ApprovalRequestDisplay = BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
            ApprovalRequestId = attempt.ApprovalRequestId,
            OriginatingEntityType = originatingEntityType,
            OriginatingFinanceActionDisplay = BuildOriginatingFinanceActionDisplay(originatingEntityReference),
            OriginatingEntityId = originatingEntityId,
            OriginatingEntityReference = originatingEntityReference,
            TaskId = attempt.TaskId,
            WorkflowInstanceId = attempt.WorkflowInstanceId,
            RelatedRecords = relatedRecords
        };
    }

    private async Task<FinanceTransparencyEventStreamResponse?> BuildFinanceTransparencyEventsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var auditEvents = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var items = auditEvents
            .Where(IsFinanceTransparencyEvent)
            .Take(25)
            .Select(auditEvent =>
            {
                var triggerTrace = BuildTriggerConsumptionTrace(auditEvent);
                return new FinanceTransparencyEventListItemResponse
                {
                    Id = auditEvent.Id,
                    EventType = auditEvent.Action,
                    OccurredAtUtc = auditEvent.OccurredUtc,
                    CorrelationId = auditEvent.CorrelationId ?? string.Empty,
                    AffectedEntityType = auditEvent.TargetType,
                    AffectedEntityId = auditEvent.TargetId,
                    EntityReference = BuildAuditEntityReference(auditEvent),
                    PayloadSummary = BuildAuditPayloadSummary(auditEvent),
                    HasTriggerTrace = triggerTrace.Count > 0
                };
            })
            .ToArray();

        if (items.Length == 0)
        {
            return null;
        }

        return new FinanceTransparencyEventStreamResponse
        {
            Summary = $"Showing {items.Length} recent finance event(s) for the active company.",
            Items = items
        };
    }

    private async Task<FinanceTransparencyEventDetailResponse?> BuildFinanceTransparencyEventDetailAsync(
        Guid companyId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var auditEvent = await _dbContext.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == eventId, cancellationToken);

        if (auditEvent is null || !IsFinanceTransparencyEvent(auditEvent))
        {
            return null;
        }

        var relatedRecords = await BuildEventRelatedRecordsAsync(companyId, auditEvent, cancellationToken);

        return new FinanceTransparencyEventDetailResponse
        {
            Id = auditEvent.Id,
            EventType = auditEvent.Action,
            OccurredAtUtc = auditEvent.OccurredUtc,
            CorrelationId = auditEvent.CorrelationId ?? string.Empty,
            EntityType = auditEvent.TargetType,
            EntityId = auditEvent.TargetId,
            EntityReference = BuildAuditEntityReference(auditEvent),
            PayloadSummary = BuildAuditPayloadSummary(auditEvent),
            RelatedRecords = relatedRecords,
            TriggerConsumptionTrace = BuildTriggerConsumptionTrace(auditEvent)
        };
    }

    private HashSet<string> BuildFinanceToolNameSet() =>
        _toolRegistry.ListTools()
            .Where(registration => registration.Scopes.Contains("finance"))
            .Select(registration => registration.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<FinanceTransparencyRelatedRecordResponse>> BuildExecutionRelatedRecordsAsync(
        Guid companyId,
        ToolExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var records = new List<FinanceTransparencyRelatedRecordResponse>();

        AddRelatedRecord(
            records,
            "approval_request",
            "approval_request",
            attempt.ApprovalRequestId?.ToString("D"),
            BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
            attempt.ApprovalRequestId is Guid approvalRequestId ? $"Approval request {approvalRequestId:D}" : string.Empty,
            "explicit_link");

        if (attempt.TaskId is Guid taskId)
        {
            AddRelatedRecord(
                records,
                "task",
                "work_task",
                taskId.ToString("D"),
                $"Task {taskId:D}",
                "Execution task context",
                "explicit_link");
        }

        if (attempt.WorkflowInstanceId is Guid workflowInstanceId)
        {
            AddRelatedRecord(
                records,
                "workflow",
                "workflow_instance",
                workflowInstanceId.ToString("D"),
                $"Workflow {workflowInstanceId:D}",
                "Workflow context",
                "explicit_link");
        }

        var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
        AddRelatedRecord(
            records,
            "finance_action",
            originatingEntityType,
            originatingEntityId?.ToString("D"),
            BuildOriginatingFinanceActionDisplay(originatingEntityReference),
            originatingEntityReference,
            "payload_reference");

        var relatedEvents = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.RelatedToolExecutionAttemptId == attempt.Id ||
                 (!string.IsNullOrWhiteSpace(attempt.CorrelationId) &&
                  x.CorrelationId != null &&
                  x.CorrelationId == attempt.CorrelationId)))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var auditEvent in relatedEvents)
        {
            AddRelatedRecord(
                records,
                "event",
                "audit_event",
                auditEvent.Id.ToString("D"),
                auditEvent.Action,
                BuildAuditEntityReference(auditEvent),
                auditEvent.RelatedToolExecutionAttemptId == attempt.Id ? "audit_link" : "correlation");

            AddRelatedRecord(
                records,
                "approval_request",
                "approval_request",
                auditEvent.RelatedApprovalRequestId?.ToString("D"),
                BuildApprovalRequestDisplay(auditEvent.RelatedApprovalRequestId),
                "Approval observed in related event",
                "audit_link");

            AddRelatedRecord(
                records,
                "finance_action",
                auditEvent.TargetType,
                auditEvent.TargetId,
                BuildAuditEntityReference(auditEvent),
                BuildAuditEntityReference(auditEvent),
                string.IsNullOrWhiteSpace(auditEvent.CorrelationId) ? "audit_target" : "correlation");
        }

        return OrderRelatedRecords(records);
    }

    private async Task<IReadOnlyList<FinanceTransparencyRelatedRecordResponse>> BuildEventRelatedRecordsAsync(
        Guid companyId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var records = new List<FinanceTransparencyRelatedRecordResponse>();

        AddRelatedRecord(
            records,
            "affected_entity",
            auditEvent.TargetType,
            auditEvent.TargetId,
            BuildAuditEntityReference(auditEvent),
            BuildAuditEntityReference(auditEvent),
            "audit_target");

        AddRelatedRecord(
            records,
            "approval_request",
            "approval_request",
            auditEvent.RelatedApprovalRequestId?.ToString("D"),
            BuildApprovalRequestDisplay(auditEvent.RelatedApprovalRequestId),
            "Approval recorded directly on the event",
            "explicit_link");

        if (auditEvent.RelatedTaskId is Guid taskId)
        {
            AddRelatedRecord(
                records,
                "task",
                "work_task",
                taskId.ToString("D"),
                $"Task {taskId:D}",
                "Task context recorded on the event",
                "explicit_link");
        }

        if (auditEvent.RelatedWorkflowInstanceId is Guid workflowInstanceId)
        {
            AddRelatedRecord(
                records,
                "workflow",
                "workflow_instance",
                workflowInstanceId.ToString("D"),
                $"Workflow {workflowInstanceId:D}",
                "Workflow context recorded on the event",
                "explicit_link");
        }

        var relatedAttempts = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Id == auditEvent.RelatedToolExecutionAttemptId ||
                 (!string.IsNullOrWhiteSpace(auditEvent.CorrelationId) &&
                  x.CorrelationId != null &&
                  x.CorrelationId == auditEvent.CorrelationId)))
            .OrderByDescending(x => x.ExecutedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        foreach (var attempt in relatedAttempts)
        {
            AddRelatedRecord(
                records,
                "tool_execution",
                "tool_execution",
                attempt.Id.ToString("D"),
                BuildToolExecutionDisplay(attempt),
                BuildToolExecutionReference(attempt),
                auditEvent.RelatedToolExecutionAttemptId == attempt.Id ? "explicit_link" : "correlation");

            AddRelatedRecord(
                records,
                "approval_request",
                "approval_request",
                attempt.ApprovalRequestId?.ToString("D"),
                BuildApprovalRequestDisplay(attempt.ApprovalRequestId),
                "Approval attached to related tool execution",
                "execution_link");

            var (originatingEntityType, originatingEntityId, originatingEntityReference) = ResolveOriginatingEntity(attempt);
            AddRelatedRecord(
                records,
                "finance_action",
                originatingEntityType,
                originatingEntityId?.ToString("D"),
                BuildOriginatingFinanceActionDisplay(originatingEntityReference),
                originatingEntityReference,
                "execution_payload");
        }

        return OrderRelatedRecords(records);
    }

    private static void AddRelatedRecord(
        ICollection<FinanceTransparencyRelatedRecordResponse> records,
        string relationshipType,
        string? targetType,
        string? targetId,
        string? displayText,
        string? reference,
        string resolutionSource)
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId))
        {
            return;
        }

        var normalizedTargetType = targetType.Trim();
        var normalizedTargetId = targetId.Trim();
        if (records.Any(x =>
                string.Equals(x.RelationshipType, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TargetType, normalizedTargetType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TargetId, normalizedTargetId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        records.Add(new FinanceTransparencyRelatedRecordResponse
        {
            RelationshipType = relationshipType,
            TargetType = normalizedTargetType,
            TargetId = normalizedTargetId,
            DisplayText = string.IsNullOrWhiteSpace(displayText) ? $"{NormalizeTransparencyToken(normalizedTargetType)} {normalizedTargetId}" : displayText.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? string.Empty : TruncateSummary(reference.Trim(), 180),
            ResolutionSource = resolutionSource
        });
    }

    private static bool IsFinanceToolExecution(ToolExecutionAttempt attempt, HashSet<string> financeToolNames) =>
        string.Equals(attempt.Scope, "finance", StringComparison.OrdinalIgnoreCase) ||
        financeToolNames.Contains(attempt.ToolName);

    private static bool IsFinanceTransparencyEvent(AuditEvent auditEvent)
    {
        if (auditEvent.Action.StartsWith("finance", StringComparison.OrdinalIgnoreCase) ||
            auditEvent.TargetType.StartsWith("finance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return auditEvent.Metadata.TryGetValue("requestedDomain", out var requestedDomain) &&
               string.Equals(requestedDomain, "finance", StringComparison.OrdinalIgnoreCase) ||
               auditEvent.Metadata.TryGetValue("responsibilityDomain", out var responsibilityDomain) &&
               string.Equals(responsibilityDomain, "finance", StringComparison.OrdinalIgnoreCase);
    }

    private static FinanceTransparencyToolExecutionListItemResponse BuildToolExecutionListItem(ToolExecutionAttempt attempt) =>
        new()
        {
            ExecutionId = attempt.Id,
            ToolName = attempt.ToolName,
            ToolVersion = attempt.ToolVersion,
            LifecycleState = attempt.Status.ToString().ToLowerInvariant(),
            RequestSummary = BuildToolExecutionRequestSummary(attempt),
            ResponseSummary = BuildToolExecutionResponseSummary(attempt),
            ExecutionTimestampUtc = attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc,
            CorrelationId = attempt.CorrelationId ?? string.Empty
        };

    private static string BuildContractSummary(JsonObject inputSchema, JsonObject outputSchema) =>
        $"Requests {DescribeSchema(inputSchema)}; returns {DescribeSchema(outputSchema)}.";

    private static string BuildSchemaSummary(JsonObject inputSchema, JsonObject outputSchema) =>
        $"Input schema exposes {CountSchemaProperties(inputSchema)} field(s); output schema exposes {CountSchemaProperties(outputSchema)} field(s).";

    private static int CountSchemaProperties(JsonObject schema) =>
        (schema["properties"] as JsonObject)?.Count ?? 0;

    private static string BuildManifestVersionMetadata(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "Version metadata not available." : $"Manifest version {version.Trim()} from the active runtime registry.";

    private static string DescribeSchema(JsonObject schema)
    {
        var properties = schema["properties"] as JsonObject;
        if (properties is null || properties.Count == 0)
        {
            return "has no declared fields";
        }

        var required = (schema["required"] as JsonArray)?
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray() ?? [];

        if (required.Length > 0)
        {
            return $"requires {string.Join(", ", required.Take(3))}";
        }

        return $"declares {string.Join(", ", properties.Select(pair => pair.Key).Take(3))}";
    }

    private static string BuildToolExecutionRequestSummary(ToolExecutionAttempt attempt) =>
        SummarizePayload(
            attempt.RequestPayload,
            ["transactionId", "invoiceId", "category", "status", "candidateCategory", "candidateStatus", "limit", "asOfUtc"]);

    private static string BuildToolExecutionResponseSummary(ToolExecutionAttempt attempt)
    {
        var safeSummary = TryReadSummaryScalar(attempt.ResultPayload, "userSafeSummary");
        if (!string.IsNullOrWhiteSpace(safeSummary))
        {
            return safeSummary;
        }

        if (!string.IsNullOrWhiteSpace(attempt.DenialReason))
        {
            return attempt.DenialReason;
        }

        return SummarizePayload(
            attempt.ResultPayload,
            ["status", "errorCode", "toolName", "actionType", "success", "recommendedCategory", "recommendedStatus", "confidence"]);
    }

    private static string BuildApprovalRequestDisplay(Guid? approvalRequestId) =>
        approvalRequestId is Guid resolvedApprovalRequestId ? $"Approval request {resolvedApprovalRequestId:D}" : "Not available";

    private static string BuildOriginatingFinanceActionDisplay(string? originatingEntityReference) =>
        string.IsNullOrWhiteSpace(originatingEntityReference) ? "Not available" : originatingEntityReference.Trim();

    private static string BuildToolExecutionDisplay(ToolExecutionAttempt attempt)
    {
        var version = string.IsNullOrWhiteSpace(attempt.ToolVersion) ? "unversioned" : attempt.ToolVersion;
        return $"{attempt.ToolName} ({version})";
    }

    private static string BuildToolExecutionReference(ToolExecutionAttempt attempt) =>
        TruncateSummary(
            $"{NormalizeTransparencyToken(attempt.Status.ToString())} at {(attempt.ExecutedUtc ?? attempt.CompletedUtc ?? attempt.StartedUtc):u}",
            120);

    private static IReadOnlyList<FinanceTransparencyRelatedRecordResponse> OrderRelatedRecords(
        IEnumerable<FinanceTransparencyRelatedRecordResponse> records) =>
        records
            .OrderBy(record => record.RelationshipType switch
            {
                "affected_entity" => 0,
                "finance_action" => 1,
                "approval_request" => 2,
                "tool_execution" => 3,
                "event" => 4,
                "workflow" => 5,
                "task" => 6,
                _ => 99
            })
            .ThenBy(record => record.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string SummarizePayload(IReadOnlyDictionary<string, JsonNode?> payload, IReadOnlyList<string> preferredKeys)
    {
        if (payload.Count == 0)
        {
            return "No payload recorded.";
        }

        var parts = new List<string>();
        foreach (var key in preferredKeys)
        {
            var value = TryReadSummaryScalar(payload, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parts.Add($"{key}={value}");
            if (parts.Count == 3)
            {
                break;
            }
        }

        if (parts.Count == 0 && payload.TryGetValue("data", out var dataNode) && dataNode is JsonObject dataObject)
        {
            foreach (var pair in dataObject)
            {
                var value = NodeToSummaryText(pair.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add($"{pair.Key}={value}");
                if (parts.Count == 3)
                {
                    break;
                }
            }
        }

        if (parts.Count == 0)
        {
            foreach (var pair in payload)
            {
                var value = NodeToSummaryText(pair.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add($"{pair.Key}={value}");
                if (parts.Count == 3)
                {
                    break;
                }
            }
        }

        return parts.Count == 0
            ? "Payload captured without scalar summary."
            : TruncateSummary(string.Join(" | ", parts), 240);
    }

    private static (string EntityType, Guid? EntityId, string EntityReference) ResolveOriginatingEntity(ToolExecutionAttempt attempt)
    {
        var transactionId = TryReadGuid(attempt.RequestPayload, "transactionId") ?? TryReadGuid(attempt.ResultPayload, "transactionId");
        if (transactionId is Guid resolvedTransactionId)
        {
            return ("finance_transaction", resolvedTransactionId, $"Finance transaction {resolvedTransactionId:D}");
        }

        var invoiceId = TryReadGuid(attempt.RequestPayload, "invoiceId") ?? TryReadGuid(attempt.ResultPayload, "invoiceId");
        if (invoiceId is Guid resolvedInvoiceId)
        {
            return ("finance_invoice", resolvedInvoiceId, $"Finance invoice {resolvedInvoiceId:D}");
        }

        return (string.Empty, null, string.Empty);
    }

    private static string BuildAuditEntityReference(AuditEvent auditEvent)
    {
        if (auditEvent.Metadata.TryGetValue("recordReference", out var recordReference) && !string.IsNullOrWhiteSpace(recordReference))
        {
            return recordReference;
        }

        if (auditEvent.Metadata.TryGetValue("affectedRecordReference", out var affectedReference) && !string.IsNullOrWhiteSpace(affectedReference))
        {
            return affectedReference;
        }

        return string.IsNullOrWhiteSpace(auditEvent.TargetId)
            ? NormalizeTransparencyToken(auditEvent.TargetType)
            : $"{NormalizeTransparencyToken(auditEvent.TargetType)} {auditEvent.TargetId}";
    }

    private static string BuildAuditPayloadSummary(AuditEvent auditEvent)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(auditEvent.RationaleSummary))
        {
            parts.Add(auditEvent.RationaleSummary);
        }

        var metadataSummary = auditEvent.Metadata
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Take(3)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (metadataSummary.Length > 0)
        {
            parts.Add(string.Join(" | ", metadataSummary));
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(auditEvent.PayloadDiffJson))
        {
            parts.Add(auditEvent.PayloadDiffJson);
        }

        return parts.Count == 0
            ? "No payload summary recorded."
            : TruncateSummary(string.Join(" ", parts), 320);
    }

    private static IReadOnlyList<FinanceTransparencyTriggerTraceItemResponse> BuildTriggerConsumptionTrace(AuditEvent auditEvent)
    {
        var items = auditEvent.DataSourcesUsed
            .Select(dataSource => new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = dataSource.SourceType,
                SourceId = dataSource.SourceId ?? string.Empty,
                DisplayName = dataSource.DisplayName ?? dataSource.SourceType,
                Reference = dataSource.Reference ?? string.Empty
            })
            .ToList();

        if (auditEvent.RelatedWorkflowInstanceId is Guid workflowInstanceId)
        {
            items.Add(new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = "workflow_instance",
                SourceId = workflowInstanceId.ToString("D"),
                DisplayName = "Workflow instance",
                Reference = workflowInstanceId.ToString("D")
            });
        }

        if (auditEvent.RelatedToolExecutionAttemptId is Guid toolExecutionAttemptId)
        {
            items.Add(new FinanceTransparencyTriggerTraceItemResponse
            {
                SourceType = "tool_execution",
                SourceId = toolExecutionAttemptId.ToString("D"),
                DisplayName = "Tool execution",
                Reference = toolExecutionAttemptId.ToString("D")
            });
        }

        return items;
    }

    private static Guid? TryReadGuid(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return node is JsonValue stringValue &&
               stringValue.TryGetValue<string>(out var text) &&
               Guid.TryParse(text, out guid) &&
               guid != Guid.Empty
            ? guid
            : null;
    }

    private static string? TryReadSummaryScalar(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (payload.TryGetValue(key, out var node))
        {
            return NodeToSummaryText(node);
        }

        if (payload.TryGetValue("data", out var dataNode) && dataNode is JsonObject dataObject)
        {
            return NodeToSummaryText(dataObject[key]);
        }

        return null;
    }

    private static string? NodeToSummaryText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return TruncateSummary(text, 96);
            }

            if (value.TryGetValue<Guid>(out var guid))
            {
                return guid.ToString("D");
            }

            if (value.TryGetValue<DateTime>(out var dateTime))
            {
                return dateTime.ToString("u");
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }
        }

        return TruncateSummary(node.ToJsonString(), 96);
    }

    private static string NormalizeTransparencyToken(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Replace("_", " ", StringComparison.Ordinal);

    private static string TruncateSummary(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..Math.Max(0, maxLength - 3)]}...";
    }

}
