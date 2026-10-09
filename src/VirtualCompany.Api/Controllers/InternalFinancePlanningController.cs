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
public sealed class InternalFinancePlanningController : InternalFinanceControllerBase
{
    private readonly IFinanceCommandService _financeCommandService;

    private readonly IFinanceReadService _financeReadService;

    public InternalFinancePlanningController(
        IFinanceCommandService financeCommandService,
        IFinanceReadService financeReadService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinancePlanningController> logger)
        : base(initializationProblems, logger)
    {
        _financeCommandService = financeCommandService;
        _financeReadService = financeReadService;
    }
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
