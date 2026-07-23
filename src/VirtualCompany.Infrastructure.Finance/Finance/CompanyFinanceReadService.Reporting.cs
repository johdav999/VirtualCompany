using System.Globalization;
using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;


public sealed partial class CompanyFinanceReadService
{
    public async Task<FinanceCashBalanceDto> GetCashBalanceAsync(
        GetFinanceCashBalanceQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;

        var accountBalances = await BuildAccountBalancesAsync(query.CompanyId, asOfUtc, cancellationToken);
        var cashAccounts = accountBalances
            .Where(x => IsCashAccount(x.AccountName, x.AccountCode, x.AccountType))
            .ToList();

        if (cashAccounts.Count == 0)
        {
            cashAccounts = accountBalances
                .Where(x => string.Equals(x.AccountType, "asset", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (cashAccounts.Count == 0)
        {
            cashAccounts = accountBalances;
        }

        var currency = cashAccounts.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? cashAccounts[0].Currency
            : "MIXED";

        return new FinanceCashBalanceDto(
            query.CompanyId,
            asOfUtc,
            cashAccounts.Sum(x => x.Amount),
            currency,
            cashAccounts
                .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public async Task<FinanceCashPositionDto> GetCashPositionAsync(
        GetFinanceCashPositionQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        var cashBalance = await GetCashBalanceAsync(
            new GetFinanceCashBalanceQuery(query.CompanyId, asOfUtc),
            cancellationToken);
        var averageMonthlyBurn = query.AverageMonthlyBurn ?? await CalculateAverageMonthlyBurnAsync(
            query.CompanyId,
            asOfUtc,
            query.BurnLookbackDays,
            cancellationToken);
        var policy = await LoadPolicyAsync(query.CompanyId, cancellationToken);

        var estimatedRunwayDays = averageMonthlyBurn <= 0m
            ? (int?)null
            : (int)Math.Floor(cashBalance.Amount / averageMonthlyBurn * 30m);
        if (estimatedRunwayDays < 0)
        {
            estimatedRunwayDays = 0;
        }

        var warningCashAmount = averageMonthlyBurn > 0m
            ? Math.Round(averageMonthlyBurn / 30m * policy.CashRunwayWarningThresholdDays, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var criticalCashAmount = averageMonthlyBurn > 0m
            ? Math.Round(averageMonthlyBurn / 30m * policy.CashRunwayCriticalThresholdDays, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        var riskLevel = ResolveCashRiskLevel(
            cashBalance.Amount,
            estimatedRunwayDays,
            policy,
            warningCashAmount,
            criticalCashAmount);
        var isLowCash = riskLevel is "critical" or "high" or "medium";
        var existingAlert = await LoadExistingLowCashAlertAsync(query.CompanyId, cancellationToken);
        var rationale = BuildCashPositionRationale(cashBalance, averageMonthlyBurn, estimatedRunwayDays, policy, warningCashAmount, criticalCashAmount, riskLevel);
        var workflowOutput = FinanceWorkflowOutputSchemas.Create(
            isLowCash ? "low_cash_position" : "cash_position_healthy",
            riskLevel,
            isLowCash ? "review_cash_plan" : "monitor",
            rationale,
            averageMonthlyBurn > 0m ? 0.86m : 0.72m,
            "cash_position_monitoring");

        return new FinanceCashPositionDto(
            query.CompanyId,
            asOfUtc,
            cashBalance.Amount,
            cashBalance.Currency,
            Math.Round(averageMonthlyBurn, 2, MidpointRounding.AwayFromZero),
            estimatedRunwayDays,
            new FinanceCashPositionThresholdsDto(
                policy.CashRunwayWarningThresholdDays,
                policy.CashRunwayCriticalThresholdDays,
                warningCashAmount,
                criticalCashAmount,
                cashBalance.Currency),
            new FinanceCashPositionAlertStateDto(
                isLowCash,
                riskLevel,
                false,
                false,
                existingAlert?.Id,
                existingAlert?.Status.ToStorageValue(),
                rationale),
            workflowOutput);
    }

    private async Task<decimal> CalculateAverageMonthlyBurnAsync(
        Guid companyId,
        DateTime asOfUtc,
        int burnLookbackDays,
        CancellationToken cancellationToken)
    {
        if (asOfUtc <= DateTime.MinValue)
        {
            return 0m;
        }

        var lookbackDays = Math.Clamp(burnLookbackDays <= 0 ? 90 : burnLookbackDays, 1, MaxBurnLookbackDays);
        var lookbackTicks = TimeSpan.FromDays(lookbackDays).Ticks;
        var startUtc = asOfUtc.Ticks <= lookbackTicks
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : asOfUtc.AddTicks(-lookbackTicks);
        var totalBurn = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc >= startUtc && x.TransactionUtc <= asOfUtc && x.Amount < 0)
            .SumAsync(x => (decimal?)Math.Abs(x.Amount), cancellationToken) ?? 0m;
        var months = Math.Max(1m, lookbackDays / 30m);
        return totalBurn / months;
    }

    public async Task<ProfitAndLossReportDto> GetProfitAndLossReportAsync(
        GetFinanceProfitAndLossReportQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var period = await LoadFiscalPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var snapshot = period.IsClosed
            ? await LoadLatestFinancialStatementSnapshotAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken)
            : null;
        if (snapshot is not null)
        {
            var snapshotLines = await LoadFinancialStatementSnapshotLinesAsync(snapshot.SnapshotId, cancellationToken);
            return BuildProfitAndLossReport(period, snapshotLines, true, MapSnapshotMetadata(snapshot));
        }

        var snapshotRows = period.IsClosed
            ? await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken)
            : [];
        var rows = snapshotRows.Count > 0
            ? snapshotRows
            : await LoadLedgerStatementRowsForPeriodAsync(query.CompanyId, period, FinancialStatementType.ProfitAndLoss, cancellationToken);
        var lines = rows
            .Select(MapStatementLine)
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildProfitAndLossReport(period, lines, snapshotRows.Count > 0, null);
    }

    public async Task<IReadOnlyList<FinancialStatementSnapshotSummaryDto>> ListFinancialStatementSnapshotsAsync(
        ListFinancialStatementSnapshotsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var snapshots = _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (query.FiscalPeriodId.HasValue)
        {
            snapshots = snapshots.Where(x => x.FiscalPeriodId == query.FiscalPeriodId.Value);
        }

        if (query.StatementType.HasValue)
        {
            snapshots = snapshots.Where(x => x.StatementType == query.StatementType.Value);
        }

        return await snapshots
            .OrderByDescending(x => x.GeneratedAtUtc)
            .ThenByDescending(x => x.VersionNumber)
            .Select(x => new FinancialStatementSnapshotSummaryDto(
                x.Id,
                x.CompanyId,
                x.FiscalPeriodId,
                x.FiscalPeriod.Name,
                x.StatementType.ToStorageValue(),
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency,
                x.Lines.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancialStatementSnapshotDetailDto?> GetFinancialStatementSnapshotAsync(
        GetFinancialStatementSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var summary = await _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.SnapshotId)
            .Select(x => new FinancialStatementSnapshotSummaryDto(
                x.Id,
                x.CompanyId,
                x.FiscalPeriodId,
                x.FiscalPeriod.Name,
                x.StatementType.ToStorageValue(),
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency,
                x.Lines.Count))
            .SingleOrDefaultAsync(cancellationToken);

        return summary is null
            ? null
            : new FinancialStatementSnapshotDetailDto(summary.SnapshotId, summary, await LoadFinancialStatementSnapshotLinesAsync(summary.SnapshotId, cancellationToken));
    }

    public async Task<FinancialStatementDrilldownDto> GetFinancialStatementDrilldownAsync(
        GetFinancialStatementDrilldownQuery query,
        CancellationToken cancellationToken)
    {
        if (query.SnapshotId.HasValue && query.SnapshotVersionNumber.HasValue)
        {
            throw new ArgumentException("Specify either snapshotId or snapshotVersionNumber when requesting statement drilldown.", nameof(query));
        }

        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var resolution = query.SnapshotId.HasValue || query.SnapshotVersionNumber.HasValue
            ? await ResolveSnapshotStatementLineAsync(query, cancellationToken)
            : await ResolveLiveStatementLineAsync(
                query,
                await LoadFiscalPeriodAsync(
                    query.CompanyId,
                    query.FiscalPeriodId ?? throw new ArgumentException("FiscalPeriodId is required for live statement drilldown.", nameof(query)),
                    cancellationToken),
                cancellationToken);
        var drilldownEntries = await LoadDrilldownEntriesAsync(
            query.CompanyId,
            resolution.Period,
            resolution.StatementType,
            resolution.ContributionRules,
            cancellationToken);
        var journalLineTotal = Math.Round(drilldownEntries.Sum(x => x.TotalContributionAmount), 2, MidpointRounding.AwayFromZero);
        var reconciliationTotal = Math.Round(resolution.OpeningBalanceAdjustment + journalLineTotal, 2, MidpointRounding.AwayFromZero);
        var reconciliationDelta = Math.Round(resolution.Amount - reconciliationTotal, 2, MidpointRounding.AwayFromZero);

        return new FinancialStatementDrilldownDto(
            query.CompanyId,
            resolution.Period.FiscalPeriodId,
            resolution.Period.Name,
            resolution.StatementType.ToStorageValue(),
            resolution.SourceMode,
            resolution.Snapshot,
            new FinancialStatementDrilldownLineDto(
                resolution.LineCode,
                resolution.LineName,
                resolution.ReportSection.ToStorageValue(),
                resolution.LineClassification.ToStorageValue(),
                resolution.Amount,
                resolution.Currency),
            resolution.OpeningBalanceAdjustment,
            journalLineTotal,
            reconciliationTotal,
            reconciliationDelta,
            drilldownEntries);
    }

    public async Task<BalanceSheetReportDto> GetBalanceSheetReportAsync(
        GetFinanceBalanceSheetReportQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var period = await LoadFiscalPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var snapshot = period.IsClosed
            ? await LoadLatestFinancialStatementSnapshotAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.BalanceSheet, cancellationToken)
            : null;
        if (snapshot is not null)
        {
            var snapshotLines = await LoadFinancialStatementSnapshotLinesAsync(snapshot.SnapshotId, cancellationToken);
            return BuildBalanceSheetReport(period, snapshotLines, true, MapSnapshotMetadata(snapshot));
        }

        var snapshotRows = period.IsClosed
            ? await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.BalanceSheet, cancellationToken)
            : [];
        var usedSnapshot = snapshotRows.Count > 0;
        var statementRows = usedSnapshot
            ? snapshotRows
            : await LoadBalanceSheetRowsAsync(query.CompanyId, period.EndUtc, cancellationToken);
        var currentEarnings = usedSnapshot
            ? CalculateProfitAndLossTotal(await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken))
            : await CalculateCurrentEarningsAsync(query.CompanyId, period.EndUtc, cancellationToken);

        var lines = BuildLiveBalanceSheetLines(statementRows, currentEarnings);
        return BuildBalanceSheetReport(period, lines, usedSnapshot, null);
    }

    private static FinancialStatementSnapshotMetadataDto MapSnapshotMetadata(FinancialStatementSnapshotHeaderRow row) =>
        new(row.SnapshotId, row.VersionNumber, row.BalancesChecksum, row.GeneratedAtUtc, row.SourcePeriodStartUtc, row.SourcePeriodEndUtc, row.Currency);

    public async Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
        GetFinanceMonthlyProfitAndLossQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Month must be between 1 and 12.");
        }

        var startUtc = new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddMonths(1);

        var invoices = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc >= startUtc && x.IssuedUtc < endUtc)
            .Select(x => new FinanceAmountRow(x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var expenseTransactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.TransactionUtc >= startUtc && x.TransactionUtc < endUtc && x.Amount < 0)
            .Select(x => new FinanceAmountRow(x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var revenue = invoices.Sum(x => x.Amount);
        var expenses = expenseTransactions.Sum(x => Math.Abs(x.Amount));
        var currency = ResolveCurrency(invoices.Concat(expenseTransactions));

        return new FinanceMonthlyProfitAndLossDto(
            query.CompanyId,
            query.Year,
            query.Month,
            startUtc,
            endUtc,
            revenue,
            expenses,
            revenue - expenses,
            currency);
    }

    public async Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
        GetFinanceExpenseBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var startUtc = NormalizeUtc(query.StartUtc) ?? throw new ArgumentException("Start date is required.", nameof(query));
        var endUtc = NormalizeUtc(query.EndUtc) ?? throw new ArgumentException("End date is required.", nameof(query));
        if (startUtc >= endUtc)
        {
            throw new ArgumentException("Start date must be before end date.", nameof(query));
        }

        var expenses = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.TransactionUtc >= startUtc && x.TransactionUtc < endUtc && x.Amount < 0)
            .Select(x => new FinanceExpenseRow(x.TransactionType, x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var categories = expenses
            .GroupBy(x => NormalizeCategory(x.TransactionType), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var currency = ResolveCurrency(group.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
                return new FinanceExpenseCategoryDto(
                    group.Key,
                    group.Sum(x => Math.Abs(x.Amount)),
                    currency);
            })
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FinanceExpenseBreakdownDto(
            query.CompanyId,
            startUtc,
            endUtc,
            categories.Sum(x => x.Amount),
            ResolveCurrency(expenses.Select(x => new FinanceAmountRow(x.Amount, x.Currency))),
            categories);
    }

}

