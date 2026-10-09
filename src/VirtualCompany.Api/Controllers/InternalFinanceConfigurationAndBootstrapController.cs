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
public sealed class InternalFinanceConfigurationAndBootstrapController : InternalFinanceControllerBase
{
    private readonly IFinanceBootstrapRerunService _financeBootstrapRerunService;
    private readonly IFinancePolicyConfigurationService _financePolicyConfigurationService;
    private readonly IFinanceSeedBootstrapService _financeSeedBootstrapService;

    public InternalFinanceConfigurationAndBootstrapController(
        IFinanceBootstrapRerunService financeBootstrapRerunService,
        IFinancePolicyConfigurationService financePolicyConfigurationService,
        IFinanceSeedBootstrapService financeSeedBootstrapService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceConfigurationAndBootstrapController> logger)
        : base(initializationProblems, logger)
    {
        _financeBootstrapRerunService = financeBootstrapRerunService;
        _financePolicyConfigurationService = financePolicyConfigurationService;
        _financeSeedBootstrapService = financeSeedBootstrapService;
    }
    [HttpGet("policy-configuration")]
    public async Task<ActionResult<FinancePolicyConfigurationDto>> GetPolicyConfigurationAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financePolicyConfigurationService.GetPolicyConfigurationAsync(
                new GetFinancePolicyConfigurationQuery(companyId),
                cancellationToken));

    [HttpPut("policy-configuration")]
    public async Task<ActionResult<FinancePolicyConfigurationDto>> UpsertPolicyConfigurationAsync(
        Guid companyId,
        [FromBody] FinancePolicyConfigurationDto configuration,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePolicyConfigurationService.UpsertPolicyConfigurationAsync(
                new UpsertFinancePolicyConfigurationCommand(companyId, configuration),
                cancellationToken));

    [HttpPost("bootstrap/seed")]
    public async Task<ActionResult<FinanceSeedBootstrapResultDto>> BootstrapSeedAsync(
        Guid companyId,
        [FromBody] BootstrapFinanceSeedRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeSeedBootstrapService.GenerateAsync(
                new FinanceSeedBootstrapCommand(
                    companyId,
                    request.SeedValue,
                    request.SeedAnchorUtc,
                    request.ReplaceExisting,
                    request.InjectAnomalies,
                    request.AnomalyScenarioProfile),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("bootstrap/rerun")]
    public async Task<ActionResult<FinanceBootstrapRerunResultDto>> RerunBootstrapAsync(
        Guid companyId,
        [FromBody] RerunFinanceBootstrapRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request is not null && request.BatchSize <= 0)
        {
            errors[nameof(RerunFinanceBootstrapRequest.BatchSize)] = ["Batch size must be greater than zero."];
        }

        if (request is not null && !request.RerunPlanningBackfill && !request.RerunApprovalBackfill)
        {
            errors[nameof(RerunFinanceBootstrapRequest.RerunPlanningBackfill)] = ["Enable at least one bootstrap rerun operation."];
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors)
            {
                Title = "Finance validation failed",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(() => _financeBootstrapRerunService.RerunAsync(new RerunFinanceBootstrapCommand(companyId, request?.RerunPlanningBackfill ?? true, request?.RerunApprovalBackfill ?? true, request?.BatchSize ?? 250, request?.CorrelationId), cancellationToken));
    }

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("insights/refresh")]
    public async Task<ActionResult<FinanceInsightsSnapshotRefreshResultDto>> RefreshInsightsSnapshotAsync(
        Guid companyId,
        [FromBody] RefreshFinanceInsightsSnapshotRequest? request,
        [FromServices] IFinanceInsightRefreshService insightRefreshService,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () =>
            {
                var snapshotKey = FinanceInsightSnapshotKeys.Normalize(request?.SnapshotKey);
                if (request?.RunInBackground == true)
                {
                    return insightRefreshService.QueueInsightsSnapshotRefreshAsync(
                        new QueueFinanceInsightsSnapshotRefreshCommand(
                            companyId,
                            request.AsOfUtc,
                            request.ExpenseWindowDays,
                            request.TrendWindowDays,
                            request.PayableWindowDays,
                            snapshotKey,
                            request.RetentionMinutes,
                            request.ResetAttempts,
                            request.CorrelationId),
                        cancellationToken);
                }

                return insightRefreshService.RefreshInsightsSnapshotAsync(
                    new RefreshFinanceInsightsSnapshotCommand(
                        companyId,
                        request?.AsOfUtc,
                        request?.ExpenseWindowDays ?? 90,
                        request?.TrendWindowDays ?? 30,
                        request?.PayableWindowDays ?? 14,
                        snapshotKey,
                        TimeSpan.FromMinutes(request?.RetentionMinutes ?? 360)),
                    cancellationToken);
            });

    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [HttpPost("sandbox-admin/seed-generation")]
    public async Task<ActionResult<FinanceSandboxSeedGenerationResponse>> GenerateSandboxSeedDatasetAsync(
        Guid companyId,
        [FromBody] FinanceSandboxSeedGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateSandboxSeedGenerationRequest(companyId, request);
        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors)
            {
                Title = "Finance validation failed",
                Detail = "Update the seed generation request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var normalizedMode = FinanceSandboxSeedGenerationModes.Normalize(request.GenerationMode);
        var command = normalizedMode switch
        {
            FinanceSandboxSeedGenerationModes.Refresh => new FinanceSeedBootstrapCommand(
                companyId,
                request.SeedValue,
                request.AnchorDateUtc,
                ReplaceExisting: true,
                InjectAnomalies: false),
            FinanceSandboxSeedGenerationModes.RefreshWithAnomalies => new FinanceSeedBootstrapCommand(
                companyId,
                request.SeedValue,
                request.AnchorDateUtc,
                ReplaceExisting: true,
                InjectAnomalies: true,
                AnomalyScenarioProfile: "baseline"),
            _ => throw new InvalidOperationException("Unsupported sandbox seed generation mode.")
        };

        return await ExecuteWriteAsync(async () => BuildSandboxSeedGenerationResponse(await _financeSeedBootstrapService.GenerateAsync(command, cancellationToken), normalizedMode));
    }

    private static FinanceSandboxSeedGenerationResponse BuildSandboxSeedGenerationResponse(
        FinanceSeedBootstrapResultDto result,
        string generationMode)
    {
        var referentialIntegrityErrors = result.ValidationErrors
            .Where(x => IsReferentialIntegrityCode(x.Code))
            .Select(MapSeedGenerationIssue)
            .ToArray();
        var validationErrors = result.ValidationErrors
            .Where(x => !IsReferentialIntegrityCode(x.Code))
            .Select(MapSeedGenerationIssue)
            .ToArray();
        var warnings = result.Anomalies
            .Select(MapSeedGenerationWarning)
            .ToArray();
        var succeeded = result.ValidationErrors.Count == 0;

        return new FinanceSandboxSeedGenerationResponse
        {
            CompanyId = result.CompanyId,
            SeedValue = result.SeedValue,
            AnchorDateUtc = result.WindowEndUtc,
            GenerationMode = generationMode,
            Succeeded = succeeded,
            CreatedCount = succeeded ? CountCreatedSeedRecords(result) : 0,
            UpdatedCount = 0,
            Message = succeeded
                ? "Seed dataset generated successfully. Review the summary and validation results below."
                : "Seed dataset generation returned validation issues. Resolve the reported problems and retry the request.",
            Errors = validationErrors,
            Warnings = warnings,
            ReferentialIntegrityErrors = referentialIntegrityErrors
        };
    }

    private static Dictionary<string, string[]> ValidateSandboxSeedGenerationRequest(
        Guid companyId,
        FinanceSandboxSeedGenerationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.CompanyId == Guid.Empty)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.CompanyId)] = ["Select a company before generating a seed dataset."];
        }
        else if (request.CompanyId != companyId)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.CompanyId)] = ["The selected company does not match the active company context."];
        }

        if (request.SeedValue <= 0)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.SeedValue)] = ["Enter a positive seed value."];
        }

        if (request.AnchorDateUtc == default)
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.AnchorDateUtc)] = ["Select an anchor date for the generated dataset."];
        }

        if (!FinanceSandboxSeedGenerationModes.IsSupported(request.GenerationMode))
        {
            errors[nameof(FinanceSandboxSeedGenerationRequest.GenerationMode)] = ["Select a supported generation mode."];
        }

        return errors;
    }

    private static int CountCreatedSeedRecords(FinanceSeedBootstrapResultDto result) =>
        result.AccountCount +
        result.CounterpartyCount +
        result.InvoiceCount +
        result.BillCount +
        result.RecurringExpenseCount +
        result.TransactionCount +
        result.BalanceCount +
        result.PaymentCount +
        result.DocumentCount +
        result.Anomalies.Count +
        1;

    private static FinanceSandboxSeedGenerationIssueResponse MapSeedGenerationIssue(FinanceSeedValidationErrorDto error) =>
        new()
        {
            Code = error.Code,
            Message = error.Message
        };

    private static FinanceSandboxSeedGenerationIssueResponse MapSeedGenerationWarning(FinanceSeedAnomalyDto anomaly) =>
        new()
        {
            Code = $"anomaly.{anomaly.AnomalyType}",
            Message = $"Injected validation scenario '{HumanizeReviewToken(anomaly.AnomalyType)}' affecting {anomaly.AffectedRecordIds.Count} record(s)."
        };

    private static bool IsReferentialIntegrityCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return code.StartsWith("accounts.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("counterparties.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("documents.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoices.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoices.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("bills.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("bills.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("recurring.supplier", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("recurring.category", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.account", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.counterparty", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.invoice", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.bill", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("transactions.document", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("balances.account", StringComparison.OrdinalIgnoreCase);
    }

}
