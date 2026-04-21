using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("budgets")]
    public async Task<ActionResult<IReadOnlyList<FinanceBudgetDto>>> GetBudgetsAsync(
        Guid companyId,
        [FromQuery] DateTime periodStartUtc,
        [FromQuery] DateTime? periodEndUtc,
        [FromQuery] Guid? financeAccountId,
        [FromQuery] Guid? costCenterId,
        [FromQuery] string? version,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBudgetsAsync(
                new GetFinanceBudgetsQuery(companyId, periodStartUtc, periodEndUtc, version, financeAccountId, costCenterId),
                cancellationToken));

    [HttpPost("budgets")]
    public async Task<ActionResult<FinanceBudgetDto>> CreateBudgetAsync(
        Guid companyId,
        [FromBody] UpsertFinanceBudgetRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.CreateBudgetAsync(
                new CreateFinanceBudgetCommand(companyId, request.ToDto()),
                cancellationToken));

    [HttpPut("budgets/{budgetId:guid}")]
    public async Task<ActionResult<FinanceBudgetDto>> UpdateBudgetAsync(
        Guid companyId,
        Guid budgetId,
        [FromBody] UpsertFinanceBudgetRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateBudgetAsync(
                new UpdateFinanceBudgetCommand(companyId, budgetId, request.ToDto()),
                cancellationToken));

    [HttpGet("variance")]
    public async Task<ActionResult<FinanceVarianceResultDto>> GetVarianceAsync(
        Guid companyId,
        [FromQuery] DateTime periodStartUtc,
        [FromQuery] string comparisonType,
        [FromQuery] DateTime? periodEndUtc,
        [FromQuery] Guid? financeAccountId,
        [FromQuery] Guid? costCenterId,
        [FromQuery] string? version,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetVarianceAsync(
                new GetFinanceVarianceQuery(companyId, periodStartUtc, comparisonType, periodEndUtc, version, financeAccountId, costCenterId),
                cancellationToken));

    [HttpGet("forecasts")]
    public async Task<ActionResult<IReadOnlyList<FinanceForecastDto>>> GetForecastsAsync(
        Guid companyId,
        [FromQuery] DateTime periodStartUtc,
        [FromQuery] DateTime periodEndUtc,
        [FromQuery] Guid? financeAccountId,
        [FromQuery] string? version,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetForecastsAsync(
                new GetFinanceForecastsQuery(companyId, periodStartUtc, periodEndUtc, financeAccountId, version),
                cancellationToken));
}

public sealed record UpsertFinanceBudgetRequest(
    Guid FinanceAccountId,
    DateTime PeriodStartUtc,
    string Version,
    decimal Amount,
    string? Currency = null,
    Guid? CostCenterId = null)
{
    public FinancePlanningEntryUpsertDto ToDto() =>
        new(
            FinanceAccountId,
            PeriodStartUtc,
            Version,
            Amount,
            Currency,
            CostCenterId);
}