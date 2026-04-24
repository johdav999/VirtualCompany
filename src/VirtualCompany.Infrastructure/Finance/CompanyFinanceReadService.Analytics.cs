using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    public async Task<FinanceAnalyticsDto> GetAnalyticsAsync(
        GetFinanceAnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var parameters = NormalizeInsightsQuery(
            query.CompanyId,
            query.AsOfUtc,
            query.ExpenseWindowDays,
            query.TrendWindowDays,
            query.PayableWindowDays,
            FinanceInsightSnapshotKeys.Default);
        var computed = await ComputeOperationalAnalyticsAsync(parameters, cancellationToken);

        var insights = query.RefreshInsightsSnapshot
            ? (await RefreshInsightsSnapshotAsync(
                new RefreshFinanceInsightsSnapshotCommand(
                    query.CompanyId,
                    query.AsOfUtc,
                    query.ExpenseWindowDays,
                    query.TrendWindowDays,
                    query.PayableWindowDays,
                    FinanceInsightSnapshotKeys.Default),
                cancellationToken)).Insights!
            : await GetInsightsAsync(
                new GetFinanceInsightsQuery(
                    query.CompanyId,
                    query.AsOfUtc,
                    query.ExpenseWindowDays,
                    query.TrendWindowDays,
                    query.PayableWindowDays,
                    IncludeResolved: true,
                    PreferSnapshot: true),
                cancellationToken);

        var summaryQueryService = new CompanyFinanceSummaryQueryService(
            _dbContext,
            _timeProvider ?? TimeProvider.System,
            new FinanceSummaryConsistencyChecker(_dbContext));
        var summary = await summaryQueryService.GetAsync(
            new GetFinanceSummaryQuery(
                query.CompanyId,
                parameters.AsOfUtc,
                query.RecentAssetPurchaseLimit,
                query.IncludeConsistencyCheck),
            cancellationToken);
        var planning = await BuildPlanningAnalyticsAsync(query.CompanyId, parameters.AsOfUtc, cancellationToken);
        var statements = await BuildStatementAnalyticsAsync(query.CompanyId, cancellationToken);

        return new FinanceAnalyticsDto(
            query.CompanyId,
            parameters.AsOfUtc,
            insights,
            computed.CashPosition,
            summary,
            new FinanceAnalyticsNarrativeDto(
                computed.Headline,
                computed.Summary,
                computed.CoverageNote,
                computed.Highlights,
                computed.NarrativeHints,
                computed.TopExpenses,
                computed.RevenueTrend,
                computed.BurnRate,
                computed.OverdueCustomerRisk,
                computed.PayablePressure),
            planning,
            statements);
    }

    private async Task<ComputedFinanceAnalytics> ComputeOperationalAnalyticsAsync(
        FinanceInsightQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var expenseWindowStartUtc = parameters.AsOfUtc.Date.AddDays(-(parameters.ExpenseWindowDays - 1));
        var currentTrendStartUtc = parameters.AsOfUtc.Date.AddDays(-(parameters.TrendWindowDays - 1));
        var previousTrendStartUtc = currentTrendStartUtc.AddDays(-parameters.TrendWindowDays);
        var payableWindowEndUtc = parameters.AsOfUtc.Date.AddDays(parameters.PayableWindowDays + 1);

        var expenseRows = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.TransactionUtc >= expenseWindowStartUtc &&
                x.TransactionUtc <= parameters.AsOfUtc &&
                x.Amount < 0m)
            .Select(x => new InsightExpenseRow(
                x.Id,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Counterparty != null ? x.Counterparty.Name : null))
            .ToListAsync(cancellationToken);

        var revenueRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.IssuedUtc >= previousTrendStartUtc &&
                x.IssuedUtc <= parameters.AsOfUtc)
            .Select(x => new InsightRevenueRow(
                x.Id,
                x.IssuedUtc,
                x.Amount,
                x.Currency))
            .ToListAsync(cancellationToken);

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == parameters.CompanyId && x.IssuedUtc <= parameters.AsOfUtc)
            .Select(x => new InsightInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == parameters.CompanyId && x.ReceivedUtc <= parameters.AsOfUtc)
            .Select(x => new InsightBillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var incomingAllocations = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= parameters.AsOfUtc)
            .Select(x => new InsightAllocationRow(x.InvoiceId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var outgoingAllocations = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= parameters.AsOfUtc)
            .Select(x => new InsightAllocationRow(x.BillId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = incomingAllocations
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Math.Round(x.Sum(y => y.Amount), 2, MidpointRounding.AwayFromZero));
        var completedOutgoingByBill = outgoingAllocations
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Math.Round(x.Sum(y => y.Amount), 2, MidpointRounding.AwayFromZero));

        var cashPosition = await GetCashPositionAsync(
            new GetFinanceCashPositionQuery(parameters.CompanyId, parameters.AsOfUtc),
            cancellationToken);
        var currency = ResolveCurrency(
            expenseRows.Select(x => new FinanceAmountRow(Math.Abs(x.Amount), x.Currency))
                .Concat(revenueRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency)))
                .Concat(invoiceRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency)))
                .Concat(billRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency)))
                .Concat([new FinanceAmountRow(cashPosition.AvailableBalance, cashPosition.Currency)]));

        var topExpenses = BuildTopExpensesInsight(expenseRows, expenseWindowStartUtc, parameters.AsOfUtc, currency);
        var revenueTrend = BuildRevenueTrendInsight(
            revenueRows,
            previousTrendStartUtc,
            currentTrendStartUtc,
            parameters.AsOfUtc,
            currency,
            parameters.TrendWindowDays);
        var burnRate = BuildBurnRateInsight(
            expenseRows,
            revenueRows,
            parameters.ExpenseWindowDays,
            cashPosition.AvailableBalance,
            currency,
            currentTrendStartUtc,
            parameters.AsOfUtc);
        var overdueCustomerRisk = BuildOverdueCustomerRiskInsight(
            invoiceRows,
            completedIncomingByInvoice,
            parameters.AsOfUtc,
            currency);
        var payablePressure = BuildPayablePressureInsight(
            billRows,
            completedOutgoingByBill,
            parameters.AsOfUtc,
            payableWindowEndUtc,
            parameters.PayableWindowDays,
            cashPosition.AvailableBalance,
            currency);

        return new ComputedFinanceAnalytics(
            cashPosition,
            topExpenses,
            revenueTrend,
            burnRate,
            overdueCustomerRisk,
            payablePressure,
            BuildHeadline(revenueTrend, burnRate, overdueCustomerRisk, payablePressure),
            BuildSummary(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure),
            BuildCoverageNote(expenseRows.Count(), revenueRows.Count(), invoiceRows.Count(), billRows.Count()),
            BuildHighlights(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure),
            BuildNarrativeHints(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure));
    }

    private async Task<IReadOnlyList<FinancialCheckResult>> BuildDerivedOperationalCheckResultsAsync(
        FinanceInsightQueryParameters parameters,
        ComputedFinanceAnalytics computed,
        CancellationToken cancellationToken)
    {
        var companyEntity = new FinanceInsightEntityReferenceDto(
            "company",
            parameters.CompanyId.ToString("D"),
            IsPrimary: true);
        var results = new List<FinancialCheckResult>();

        var topExpenseLeader = computed.TopExpenses.Items.FirstOrDefault();
        if (topExpenseLeader is not null && string.Equals(computed.TopExpenses.TrendLabel, "concentrated", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.TopExpenseConcentration,
                $"top_expense_concentration:{NormalizeCategory(topExpenseLeader.Label)}",
                "company",
                parameters.CompanyId.ToString("D"),
                topExpenseLeader.ShareOfExpenses >= 0.6m ? FinancialCheckSeverity.High : FinancialCheckSeverity.Medium,
                computed.TopExpenses.Summary,
                $"Review spend concentration in {topExpenseLeader.Label} and confirm supplier/category dependence is acceptable.",
                topExpenseLeader.ShareOfExpenses >= 0.6m ? 0.91m : 0.82m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    topExpenseLeader.Label,
                    topExpenseLeader.Amount,
                    topExpenseLeader.ShareOfExpenses
                })));
        }

        if (string.Equals(computed.RevenueTrend.DirectionLabel, "down", StringComparison.OrdinalIgnoreCase))
        {
            var severity = computed.RevenueTrend.DeltaPercent <= -0.2m ? FinancialCheckSeverity.High : FinancialCheckSeverity.Medium;
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.RevenueTrend,
                "revenue_trend:declining",
                "company",
                parameters.CompanyId.ToString("D"),
                severity,
                computed.RevenueTrend.Summary,
                "Investigate invoice volume, conversion, and collections to stabilize short-term revenue.",
                severity == FinancialCheckSeverity.High ? 0.88m : 0.79m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    computed.RevenueTrend.CurrentRevenue,
                    computed.RevenueTrend.PreviousRevenue,
                    computed.RevenueTrend.DeltaAmount,
                    computed.RevenueTrend.DeltaPercent
                })));
        }

        if (computed.BurnRate.RiskLabel is "watch" or "critical")
        {
            var severity = string.Equals(computed.BurnRate.RiskLabel, "critical", StringComparison.OrdinalIgnoreCase)
                ? FinancialCheckSeverity.Critical
                : FinancialCheckSeverity.High;
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.BurnRunwayRisk,
                $"burn_runway_risk:{computed.BurnRate.RiskLabel}",
                "company",
                parameters.CompanyId.ToString("D"),
                severity,
                computed.BurnRate.Summary,
                "Revisit burn drivers, collections timing, and near-term payables to preserve runway.",
                severity == FinancialCheckSeverity.Critical ? 0.94m : 0.87m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    computed.BurnRate.AverageMonthlyBurn,
                    computed.BurnRate.NetMonthlyBurn,
                    computed.BurnRate.EstimatedRunwayDays
                })));
        }

        var overdueCustomer = computed.OverdueCustomerRisk.Customers.FirstOrDefault();
        if (overdueCustomer is not null && computed.OverdueCustomerRisk.RiskLabel is "medium" or "high")
        {
            var customerEntity = new FinanceInsightEntityReferenceDto(
                "counterparty",
                overdueCustomer.CounterpartyId.ToString("D"),
                overdueCustomer.CustomerName,
                true);
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.OverdueCustomerConcentration,
                $"overdue_customer_concentration:{overdueCustomer.CounterpartyId:D}",
                "counterparty",
                overdueCustomer.CounterpartyId.ToString("D"),
                string.Equals(computed.OverdueCustomerRisk.RiskLabel, "high", StringComparison.OrdinalIgnoreCase)
                    ? FinancialCheckSeverity.High
                    : FinancialCheckSeverity.Medium,
                computed.OverdueCustomerRisk.Summary,
                $"Prioritize collections outreach for {overdueCustomer.CustomerName} and reduce customer concentration in overdue balances.",
                0.86m,
                customerEntity,
                [customerEntity, companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    overdueCustomer.OverdueAmount,
                    overdueCustomer.OverdueInvoiceCount,
                    overdueCustomer.MaxDaysOverdue,
                    overdueCustomer.ConcentrationRatio
                })));
        }

        if (computed.PayablePressure.RiskLabel is "medium" or "high")
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.NearTermLiquidityPressure,
                $"near_term_liquidity_pressure:{computed.PayablePressure.RiskLabel}",
                "company",
                parameters.CompanyId.ToString("D"),
                string.Equals(computed.PayablePressure.RiskLabel, "high", StringComparison.OrdinalIgnoreCase)
                    ? FinancialCheckSeverity.High
                    : FinancialCheckSeverity.Medium,
                computed.PayablePressure.Summary,
                "Sequence due-soon payables against cash availability and confirm which obligations need escalation or rescheduling.",
                0.85m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    computed.PayablePressure.OverdueAmount,
                    computed.PayablePressure.DueSoonAmount,
                    computed.PayablePressure.UpcomingBurdenRatioOfCash
                })));
        }

        if (!string.IsNullOrWhiteSpace(computed.CoverageNote))
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.SparseDataCoverage,
                "sparse_data_coverage:observed",
                "company",
                parameters.CompanyId.ToString("D"),
                FinancialCheckSeverity.Low,
                computed.CoverageNote!,
                "Treat trend-oriented analytics as directional until finance history becomes denser.",
                0.72m,
                companyEntity,
                [companyEntity]));
        }

        var policy = await LoadPolicyAsync(parameters.CompanyId, cancellationToken);
        var pendingApprovalTasks = await _dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.Status != ApprovalTaskStatus.Approved &&
                x.Status != ApprovalTaskStatus.Rejected)
            .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);
        if (pendingApprovalTasks.Count > 0)
        {
            var earliestTask = pendingApprovalTasks[0];
            var taskEntity = new FinanceInsightEntityReferenceDto(
                "approval_task",
                earliestTask.Id.ToString("D"),
                $"{earliestTask.TargetType.ToStorageValue()} approval",
                true);
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.ApprovalNeededFinanceEvents,
                $"approval_needed_finance_events:{earliestTask.TargetType.ToStorageValue()}",
                "approval_task",
                earliestTask.Id.ToString("D"),
                pendingApprovalTasks.Count >= 3 ? FinancialCheckSeverity.High : FinancialCheckSeverity.Medium,
                $"{pendingApprovalTasks.Count} finance approval task(s) are pending or escalated.",
                "Clear the oldest approval queue first to avoid policy-breaching payments or bills aging unnoticed.",
                0.84m,
                taskEntity,
                [taskEntity, companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    count = pendingApprovalTasks.Count,
                    earliestTask.TargetType,
                    earliestTask.DueDate,
                    earliestTask.Status
                })));
        }

        var thresholdWindowStartUtc = parameters.AsOfUtc.AddDays(-30);
        var thresholdBreaches = new List<string>();
        thresholdBreaches.AddRange(await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.IssuedUtc >= thresholdWindowStartUtc &&
                x.IssuedUtc <= parameters.AsOfUtc &&
                x.Currency == policy.ApprovalCurrency &&
                x.Amount >= policy.InvoiceApprovalThreshold)
            .Select(x => $"invoice:{x.Id:D}")
            .ToListAsync(cancellationToken));
        thresholdBreaches.AddRange(await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.ReceivedUtc >= thresholdWindowStartUtc &&
                x.ReceivedUtc <= parameters.AsOfUtc &&
                x.Currency == policy.ApprovalCurrency &&
                x.Amount >= policy.BillApprovalThreshold)
            .Select(x => $"bill:{x.Id:D}")
            .ToListAsync(cancellationToken));
        thresholdBreaches.AddRange(await _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.PaymentDate >= thresholdWindowStartUtc &&
                x.PaymentDate <= parameters.AsOfUtc &&
                x.Currency == policy.ApprovalCurrency &&
                x.Amount >= policy.BillApprovalThreshold)
            .Select(x => $"payment:{x.Id:D}")
            .ToListAsync(cancellationToken));
        if (thresholdBreaches.Count > 0)
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.ThresholdBreachFinanceEvents,
                "threshold_breach_finance_events:recent",
                "company",
                parameters.CompanyId.ToString("D"),
                thresholdBreaches.Count >= 3 ? FinancialCheckSeverity.High : FinancialCheckSeverity.Medium,
                $"{thresholdBreaches.Count} finance event(s) crossed approval thresholds in the last 30 days.",
                "Verify the recent threshold breaches have the right approval coverage and supporting audit trail.",
                0.81m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    count = thresholdBreaches.Count,
                    thresholdBreaches,
                    policy.InvoiceApprovalThreshold,
                    policy.BillApprovalThreshold,
                    policy.ApprovalCurrency
                })));
        }

        var planning = await BuildPlanningAnalyticsAsync(parameters.CompanyId, parameters.AsOfUtc, cancellationToken);
        if (!planning.HasForecasts)
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.ForecastGap,
                "forecast_gap:missing_current_period",
                "company",
                parameters.CompanyId.ToString("D"),
                FinancialCheckSeverity.Low,
                "No forecast entries were found for the current planning period.",
                "Add at least one forecast version so actual-vs-forecast variance and forward cash planning stay actionable.",
                0.77m,
                companyEntity,
                [companyEntity]));
        }

        if (!planning.HasBudgets)
        {
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.BudgetGap,
                "budget_gap:missing_current_period",
                "company",
                parameters.CompanyId.ToString("D"),
                FinancialCheckSeverity.Low,
                "No budget entries were found for the current planning period.",
                "Create a budget baseline for the current period so variance and approval context are anchored to targets.",
                0.77m,
                companyEntity,
                [companyEntity]));
        }

        var summaryQueryService = new CompanyFinanceSummaryQueryService(
            _dbContext,
            _timeProvider ?? TimeProvider.System,
            new FinanceSummaryConsistencyChecker(_dbContext));
        var summary = await summaryQueryService.GetAsync(
            new GetFinanceSummaryQuery(parameters.CompanyId, parameters.AsOfUtc, 5, IncludeConsistencyCheck: true),
            cancellationToken);
        if (summary.ConsistencyCheck is { IsConsistent: false } consistency)
        {
            var mismatchCount = consistency.Metrics.Count(x => !x.IsMatch);
            results.Add(new FinancialCheckResult(
                FinancialCheckDefinitions.SummaryConsistencyAnomaly,
                "summary_consistency_anomaly:detected",
                "company",
                parameters.CompanyId.ToString("D"),
                mismatchCount >= 3 ? FinancialCheckSeverity.High : FinancialCheckSeverity.Medium,
                $"{mismatchCount} finance summary metric(s) do not reconcile with source records.",
                "Inspect statement mappings, cash deltas, and open-balance calculations before using summary metrics for downstream decisions.",
                0.9m,
                companyEntity,
                [companyEntity],
                MetadataJson: JsonSerializer.Serialize(new
                {
                    mismatchCount,
                    metrics = consistency.Metrics.Where(x => !x.IsMatch)
                })));
        }

        return results;
    }

    private async Task<FinancePlanningAnalyticsDto> BuildPlanningAnalyticsAsync(
        Guid companyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var periodStartUtc = new DateTime(asOfUtc.Year, asOfUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddMonths(1).AddTicks(-1);
        var budgets = await GetBudgetsAsync(
            new GetFinanceBudgetsQuery(companyId, periodStartUtc, periodEndUtc),
            cancellationToken);
        var forecasts = await GetForecastsAsync(
            new GetFinanceForecastsQuery(companyId, periodStartUtc, periodEndUtc),
            cancellationToken);
        var actualVsBudget = budgets.Count > 0
            ? await GetVarianceAsync(
                new GetFinanceVarianceQuery(companyId, periodStartUtc, FinanceVarianceComparisonTypes.ActualVsBudget, periodEndUtc),
                cancellationToken)
            : null;
        var actualVsForecast = forecasts.Count > 0
            ? await GetVarianceAsync(
                new GetFinanceVarianceQuery(companyId, periodStartUtc, FinanceVarianceComparisonTypes.ActualVsForecast, periodEndUtc),
                cancellationToken)
            : null;

        return new FinancePlanningAnalyticsDto(
            periodStartUtc,
            periodEndUtc,
            budgets.Count > 0,
            budgets.Count,
            budgets.Select(x => x.Version).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            forecasts.Count > 0,
            forecasts.Count,
            forecasts.Select(x => x.Version).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            actualVsBudget,
            actualVsForecast);
    }

    private async Task<FinanceStatementAnalyticsDto> BuildStatementAnalyticsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
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
            .Take(20)
            .ToListAsync(cancellationToken);

        return new FinanceStatementAnalyticsDto(
            snapshots,
            snapshots.FirstOrDefault(x => string.Equals(x.StatementType, FinancialStatementType.BalanceSheet.ToStorageValue(), StringComparison.OrdinalIgnoreCase)),
            snapshots.FirstOrDefault(x => string.Equals(x.StatementType, FinancialStatementType.ProfitAndLoss.ToStorageValue(), StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record ComputedFinanceAnalytics(
        FinanceCashPositionDto CashPosition,
        FinanceTopExpensesInsightDto TopExpenses,
        FinanceRevenueTrendInsightDto RevenueTrend,
        FinanceBurnRateInsightDto BurnRate,
        FinanceOverdueCustomerRiskInsightDto OverdueCustomerRisk,
        FinancePayablePressureInsightDto PayablePressure,
        string Headline,
        string Summary,
        string? CoverageNote,
        IReadOnlyList<string> Highlights,
        IReadOnlyList<FinanceNarrativeHintDto> NarrativeHints);
}
