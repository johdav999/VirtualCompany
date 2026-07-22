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
    [HttpGet("cash-balance")]
    public async Task<ActionResult<FinanceCashBalanceDto>> GetCashBalanceAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCashBalanceAsync(
                new GetFinanceCashBalanceQuery(companyId, asOfUtc),
                cancellationToken));

    [HttpGet("cash-position")]
    public async Task<ActionResult<FinanceCashPositionDto>> GetCashPositionAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] decimal? averageMonthlyBurn,
        [FromQuery] int burnLookbackDays,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCashPositionAsync(
                new GetFinanceCashPositionQuery(companyId, asOfUtc, averageMonthlyBurn, burnLookbackDays),
                cancellationToken));

    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<FinanceAccountBalanceDto>>> GetBalancesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBalancesAsync(
                new GetFinanceBalancesQuery(companyId, asOfUtc),
                cancellationToken));

    [HttpPost("cash-position/evaluation")]
    public async Task<ActionResult<FinanceCashPositionDto>> EvaluateCashPositionAsync(
        Guid companyId,
        [FromBody] EvaluateFinanceCashPositionRequest? request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _cashPositionWorkflowService.EvaluateAsync(
                new EvaluateFinanceCashPositionWorkflowCommand(
                    companyId,
                    request?.WorkflowInstanceId,
                    request?.AgentId),
                cancellationToken));

    [HttpGet("dashboard/cash-metrics")]
    public async Task<ActionResult<DashboardFinanceSnapshotDto>> GetDashboardCashMetricsAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        await ExecuteReadAsync(
            () => _dashboardFinanceSnapshotService.GetAsync(
                companyId,
                asOfUtc,
                upcomingWindowDays,
                cancellationToken));

    [HttpGet("dashboard/current-cash-balance")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetCurrentCashBalanceAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("current_cash_balance", snapshot.CurrentCashBalance, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/expected-incoming-cash")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetExpectedIncomingCashAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("expected_incoming_cash", snapshot.ExpectedIncomingCash, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/expected-outgoing-cash")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetExpectedOutgoingCashAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("expected_outgoing_cash", snapshot.ExpectedOutgoingCash, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/overdue-receivables")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetOverdueReceivablesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("overdue_receivables", snapshot.OverdueReceivables, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("dashboard/upcoming-payables")]
    public Task<ActionResult<FinanceDashboardMetricDto>> GetUpcomingPayablesAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30) =>
        GetDashboardMetricAsync(companyId, asOfUtc, upcomingWindowDays, snapshot =>
            new FinanceDashboardMetricDto("upcoming_payables", snapshot.UpcomingPayables, snapshot.Currency, snapshot.AsOfUtc, snapshot.UpcomingWindowDays), cancellationToken);

    [HttpGet("seeding-state")]
    public async Task<ActionResult<FinanceSeedingStateResponse>> GetSeedingStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(companyId, cancellationToken);
            return new FinanceSeedingStateResponse
            {
                CompanyId = result.CompanyId,
                SeedingState = result.State.ToStorageValue(),
                DerivedFrom = result.DerivedFrom,
                CheckedAtUtc = result.CheckedAtUtc,
                Diagnostics = new FinanceSeedingStateDiagnosticsResponse
                {
                    PersistedState = result.Diagnostics.PersistedState?.ToStorageValue(),
                    MetadataState = result.Diagnostics.MetadataState?.ToStorageValue(),
                    MetadataPresent = result.Diagnostics.MetadataPresent,
                    MetadataIndicatesComplete = result.Diagnostics.MetadataIndicatesComplete,
                    UsedFastPath = result.Diagnostics.UsedFastPath,
                    Reason = result.Diagnostics.Reason
                }.WithRecordChecks(result.Diagnostics)
            };
        });

    [HttpGet("entry-state")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> GetEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _financeEntryService.GetEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("entry-state/request")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RequestEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("entry-state/retry")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RetryEntryStateAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(companyId, RetryOnFailure: true, Source: FinanceEntrySources.FinanceEntryRetry),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });

    [HttpPost("manual-seed")]
    public async Task<ActionResult<FinanceEntryInitializationResponse>> RequestManualSeedAsync(
        Guid companyId,
        [FromBody] FinanceManualSeedRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedMode = FinanceManualSeedModes.Normalize(request?.Mode);
        if (!FinanceManualSeedModes.IsSupported(normalizedMode))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(FinanceManualSeedRequest.Mode)] = ["Select a supported finance seed mode."]
            })
            {
                Title = "Finance validation failed",
                Detail = "Update the finance seed request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        if (!string.Equals(normalizedMode, FinanceManualSeedModes.Replace, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(FinanceManualSeedRequest.Mode)] = ["Append mode is not available for manual finance seeding."]
            })
            {
                Title = "Finance validation failed",
                Detail = "Only replace mode is currently supported for manual finance seeding.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        return await ExecuteWriteAsync(async () =>
        {
            var result = await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(
                    companyId,
                    ForceSeed: true,
                    Source: FinanceEntrySources.ManualSeed,
                    SeedMode: normalizedMode,
                    ConfirmReplace: request?.ConfirmReplace ?? false),
                cancellationToken);
            return InternalFinanceControllerMappings.MapFinanceEntryState(result);
        });
    }

}

