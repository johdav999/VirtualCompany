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
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () =>
            {
                var snapshotKey = FinanceInsightSnapshotKeys.Normalize(request?.SnapshotKey);
                if (request?.RunInBackground == true)
                {
                    return _financeReadService.QueueInsightsSnapshotRefreshAsync(
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

                return _financeReadService.RefreshInsightsSnapshotAsync(
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

}

