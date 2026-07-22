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
    [HttpGet("profit-and-loss/monthly")]
    public async Task<ActionResult<FinanceMonthlyProfitAndLossDto>> GetMonthlyProfitAndLossAsync(
        Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetMonthlyProfitAndLossAsync(
                new GetFinanceMonthlyProfitAndLossQuery(companyId, year, month),
                cancellationToken));

    [HttpGet("reports/profit-loss")]
    public async Task<ActionResult<ProfitAndLossReportDto>> GetProfitAndLossReportAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetProfitAndLossReportAsync(
                new GetFinanceProfitAndLossReportQuery(companyId, fiscalPeriodId),
                cancellationToken));

    [HttpGet("reports/balance-sheet")]
    public async Task<ActionResult<BalanceSheetReportDto>> GetBalanceSheetReportAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetBalanceSheetReportAsync(
                new GetFinanceBalanceSheetReportQuery(companyId, fiscalPeriodId),
                cancellationToken));

    [HttpGet("reports/drilldown")]
    public async Task<ActionResult<FinancialStatementDrilldownDto>> GetFinancialStatementDrilldownAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        [FromQuery] string statementType,
        [FromQuery] string lineCode,
        [FromQuery] int? snapshotVersionNumber,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetFinancialStatementDrilldownAsync(
                new GetFinancialStatementDrilldownQuery(
                    companyId,
                    fiscalPeriodId,
                    FinancialStatementTypeValues.Parse(statementType),
                    lineCode,
                    snapshotVersionNumber),
                cancellationToken));

    [HttpGet("expense-breakdown")]
    public async Task<ActionResult<FinanceExpenseBreakdownDto>> GetExpenseBreakdownAsync(
        Guid companyId,
        [FromQuery] DateTime startUtc,
        [FromQuery] DateTime endUtc,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetExpenseBreakdownAsync(
                new GetFinanceExpenseBreakdownQuery(companyId, startUtc, endUtc),
                cancellationToken));

}

