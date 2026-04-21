using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    private static readonly JsonSerializerOptions InsightSnapshotSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<FinanceInsightsDto> GetInsightsAsync(
        GetFinanceInsightsQuery query,
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

        if (query.PreferSnapshot)
        {
            var cached = await TryGetCachedInsightsAsync(parameters, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        return await BuildInsightsAsync(parameters, fromSnapshot: false, snapshotExpiresAtUtc: null, cancellationToken);
    }

    public async Task<FinanceInsightsSnapshotRefreshResultDto> RefreshInsightsSnapshotAsync(
        RefreshFinanceInsightsSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureFinanceInitializedAsync(command.CompanyId, cancellationToken);

        var parameters = NormalizeInsightsQuery(
            command.CompanyId,
            command.AsOfUtc,
            command.ExpenseWindowDays,
            command.TrendWindowDays,
            command.PayableWindowDays,
            command.SnapshotKey);

        var refreshed = await BuildInsightsAsync(parameters, fromSnapshot: false, snapshotExpiresAtUtc: null, cancellationToken);
        var retention = NormalizeSnapshotRetention(command.Retention);
        var expiresAtUtc = GetUtcNow().Add(retention);

        if (_insightSnapshotCache is not null)
        {
            var payload = JsonSerializer.Serialize(
                new FinanceInsightsSnapshotCacheEnvelope(
                    parameters.SnapshotKey,
                    parameters.CacheKey,
                    expiresAtUtc,
                    refreshed with { SnapshotExpiresAtUtc = expiresAtUtc }),
                InsightSnapshotSerializerOptions);

            await _insightSnapshotCache.SetStringAsync(
                parameters.CacheKey,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = retention
                },
                cancellationToken);
        }

        return new FinanceInsightsSnapshotRefreshResultDto(
            command.CompanyId,
            parameters.SnapshotKey,
            parameters.CacheKey,
            GetUtcNow(),
            $"finance-insights-refresh:{command.CompanyId:N}:{parameters.SnapshotKey}",
            Queued: false,
            Refreshed: true,
            expiresAtUtc,
            refreshed with { SnapshotExpiresAtUtc = expiresAtUtc });
    }

    public async Task<FinanceInsightsSnapshotRefreshResultDto> QueueInsightsSnapshotRefreshAsync(
        QueueFinanceInsightsSnapshotRefreshCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureFinanceInitializedAsync(command.CompanyId, cancellationToken);

        var parameters = NormalizeInsightsQuery(
            command.CompanyId,
            command.AsOfUtc,
            command.ExpenseWindowDays,
            command.TrendWindowDays,
            command.PayableWindowDays,
            command.SnapshotKey);

        var descriptor = new FinanceInsightSnapshotExecutionDescriptor(
            parameters.SnapshotKey,
            command.AsOfUtc?.Date,
            parameters.ExpenseWindowDays,
            parameters.TrendWindowDays,
            parameters.PayableWindowDays,
            Math.Clamp(command.RetentionMinutes, 15, 60 * 24 * 7));
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? $"finance-insights-refresh:{command.CompanyId:N}:{descriptor.ToStorageValue()}"
            : command.CorrelationId.Trim();
        var idempotencyKey = $"finance-insights:{command.CompanyId:N}:{descriptor.ToStorageValue()}";
        var utcNow = GetUtcNow();

        var execution = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId &&
                     x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh &&
                     x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (execution is null)
        {
            execution = new BackgroundExecution(
                Guid.NewGuid(),
                command.CompanyId,
                BackgroundExecutionType.FinanceInsightRefresh,
                BackgroundExecutionRelatedEntityTypes.FinanceInsightSnapshot,
                descriptor.ToStorageValue(),
                correlationId,
                idempotencyKey,
                maxAttempts: 3);
            _dbContext.BackgroundExecutions.Add(execution);
        }
        else if (command.ResetAttempts || execution.IsTerminal)
        {
            execution.Queue(utcNow, correlationId, resetAttempts: command.ResetAttempts);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceInsightsSnapshotRefreshResultDto(
            command.CompanyId,
            parameters.SnapshotKey,
            parameters.CacheKey,
            utcNow,
            correlationId,
            Queued: true,
            Refreshed: false,
            ExpiresAtUtc: null,
            Insights: null);
    }

    private async Task<FinanceInsightsDto> BuildInsightsAsync(
        FinanceInsightQueryParameters parameters,
        bool fromSnapshot,
        DateTime? snapshotExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var generatedAt = parameters.GeneratedAtUtc;
        var asOfUtc = parameters.AsOfUtc;
        var expenseWindowDays = parameters.ExpenseWindowDays;
        var trendWindowDays = parameters.TrendWindowDays;
        var payableWindowDays = parameters.PayableWindowDays;

        var cashBalance = await GetCashBalanceAsync(new GetFinanceCashBalanceQuery(parameters.CompanyId, asOfUtc), cancellationToken);
        var expenseWindowStartUtc = asOfUtc.Date.AddDays(-expenseWindowDays + 1);
        var trendWindowStartUtc = asOfUtc.Date.AddDays(-trendWindowDays + 1);
        var previousTrendWindowStartUtc = trendWindowStartUtc.AddDays(-trendWindowDays);
        var payableWindowEndUtc = asOfUtc.Date.AddDays(payableWindowDays + 1);

        var expenseRows = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.TransactionUtc >= expenseWindowStartUtc &&
                x.TransactionUtc <= asOfUtc &&
                x.Amount < 0m)
            .Select(x => new InsightExpenseRow(
                x.Id,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Counterparty == null ? null : x.Counterparty.Name))
            .ToListAsync(cancellationToken);

        var revenueRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.IssuedUtc >= previousTrendWindowStartUtc &&
                x.IssuedUtc < payableWindowEndUtc)
            .Select(x => new InsightRevenueRow(x.Id, x.IssuedUtc, x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == parameters.CompanyId)
            .Select(x => new InsightInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == parameters.CompanyId)
            .Select(x => new InsightBillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= asOfUtc)
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(x => new InsightAllocationRow(x.Key, x.Sum(allocation => allocation.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

        var completedOutgoingByBill = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == parameters.CompanyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= asOfUtc)
            .GroupBy(x => x.BillId!.Value)
            .Select(x => new InsightAllocationRow(x.Key, x.Sum(allocation => allocation.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

        var currency = ResolveCurrency(
            expenseRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))
                .Concat(revenueRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))));

        var topExpenses = BuildTopExpensesInsight(expenseRows, expenseWindowStartUtc, asOfUtc, currency);
        var revenueTrend = BuildRevenueTrendInsight(revenueRows, previousTrendWindowStartUtc, trendWindowStartUtc, asOfUtc, currency, trendWindowDays);
        var burnRate = BuildBurnRateInsight(expenseRows, revenueRows, expenseWindowDays, cashBalance.Amount, currency, trendWindowStartUtc, asOfUtc);
        var overdueCustomerRisk = BuildOverdueCustomerRiskInsight(invoiceRows, completedIncomingByInvoice, asOfUtc, currency);
        var payablePressure = BuildPayablePressureInsight(billRows, completedOutgoingByBill, asOfUtc, payableWindowEndUtc, payableWindowDays, cashBalance.Amount, currency);
        var dataCoverageNote = BuildCoverageNote(expenseRows.Count, revenueRows.Count, invoiceRows.Count, billRows.Count);
        var highlights = BuildHighlights(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure);
        var hints = BuildNarrativeHints(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure);

        return new FinanceInsightsDto(
            parameters.CompanyId,
            generatedAt,
            string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? cashBalance.Currency : currency,
            BuildHeadline(revenueTrend, burnRate, overdueCustomerRisk, payablePressure),
            BuildSummary(topExpenses, revenueTrend, burnRate, overdueCustomerRisk, payablePressure),
            dataCoverageNote,
            fromSnapshot,
            snapshotExpiresAtUtc,
            highlights,
            hints,
            topExpenses,
            revenueTrend,
            burnRate,
            overdueCustomerRisk,
            payablePressure);
    }

    private async Task<FinanceInsightsDto?> TryGetCachedInsightsAsync(
        FinanceInsightQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        if (_insightSnapshotCache is null)
        {
            return null;
        }

        var payload = await _insightSnapshotCache.GetStringAsync(parameters.CacheKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<FinanceInsightsSnapshotCacheEnvelope>(payload, InsightSnapshotSerializerOptions);
        if (snapshot is null || snapshot.ExpiresAtUtc <= GetUtcNow())
        {
            await _insightSnapshotCache.RemoveAsync(parameters.CacheKey, cancellationToken);
            return null;
        }

        return snapshot.Insights with
        {
            FromSnapshot = true,
            SnapshotExpiresAtUtc = snapshot.ExpiresAtUtc
        };
    }

    private FinanceInsightQueryParameters NormalizeInsightsQuery(
        Guid companyId,
        DateTime? asOfUtc,
        int expenseWindowDays,
        int trendWindowDays,
        int payableWindowDays,
        string snapshotKey)
    {
        var generatedAtUtc = GetUtcNow();
        var normalizedAsOfUtc = NormalizeUtc(asOfUtc) ?? generatedAtUtc;
        var normalizedExpenseWindowDays = Math.Clamp(expenseWindowDays <= 0 ? 90 : expenseWindowDays, 30, 365);
        var normalizedTrendWindowDays = Math.Clamp(trendWindowDays <= 0 ? 30 : trendWindowDays, 7, 90);
        var normalizedPayableWindowDays = Math.Clamp(payableWindowDays <= 0 ? 14 : payableWindowDays, 7, 60);
        var normalizedSnapshotKey = FinanceInsightSnapshotKeys.Normalize(snapshotKey);
        var cacheKey =
            $"finance:insights:{companyId:N}:{normalizedSnapshotKey}:{normalizedAsOfUtc:yyyyMMdd}:{normalizedExpenseWindowDays}:{normalizedTrendWindowDays}:{normalizedPayableWindowDays}";

        return new FinanceInsightQueryParameters(
            companyId,
            generatedAtUtc,
            normalizedAsOfUtc,
            normalizedExpenseWindowDays,
            normalizedTrendWindowDays,
            normalizedPayableWindowDays,
            normalizedSnapshotKey,
            cacheKey);
    }

    private static TimeSpan NormalizeSnapshotRetention(TimeSpan? retention)
    {
        var candidate = retention ?? TimeSpan.FromHours(6);
        if (candidate < TimeSpan.FromMinutes(15))
        {
            return TimeSpan.FromMinutes(15);
        }

        if (candidate > TimeSpan.FromDays(7))
        {
            return TimeSpan.FromDays(7);
        }

        return candidate;
    }

    private DateTime GetUtcNow() => _timeProvider?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow;

    private static IReadOnlyList<string> BuildHighlights(
        FinanceTopExpensesInsightDto topExpenses,
        FinanceRevenueTrendInsightDto revenueTrend,
        FinanceBurnRateInsightDto burnRate,
        FinanceOverdueCustomerRiskInsightDto overdueCustomerRisk,
        FinancePayablePressureInsightDto payablePressure)
    {
        var highlights = new List<string>(5)
        {
            revenueTrend.Summary,
            burnRate.Summary
        };

        if (topExpenses.Items.Count > 0)
        {
            highlights.Add(topExpenses.Summary);
        }

        if (!string.Equals(overdueCustomerRisk.RiskLabel, "clear", StringComparison.OrdinalIgnoreCase))
        {
            highlights.Add(overdueCustomerRisk.Summary);
        }

        if (!string.Equals(payablePressure.RiskLabel, "low", StringComparison.OrdinalIgnoreCase))
        {
            highlights.Add(payablePressure.Summary);
        }

        return highlights.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<FinanceNarrativeHintDto> BuildNarrativeHints(
        FinanceTopExpensesInsightDto topExpenses,
        FinanceRevenueTrendInsightDto revenueTrend,
        FinanceBurnRateInsightDto burnRate,
        FinanceOverdueCustomerRiskInsightDto overdueCustomerRisk,
        FinancePayablePressureInsightDto payablePressure) =>
        [
            new FinanceNarrativeHintDto("topExpenses", "matter_of_fact", topExpenses.Summary, "Explain the primary expense drivers and mention concentration."),
            new FinanceNarrativeHintDto("revenueTrend", "trend", revenueTrend.Summary, "Compare current revenue with the prior comparison window."),
            new FinanceNarrativeHintDto("burnRate", "risk", burnRate.Summary, "Translate cash burn and runway into concrete urgency."),
            new FinanceNarrativeHintDto("overdueCustomerRisk", "collections", overdueCustomerRisk.Summary, "Call out overdue customers and concentration risk."),
            new FinanceNarrativeHintDto("payablePressure", "cash_planning", payablePressure.Summary, "Describe overdue and due-soon payables affecting cash.")
        ];

    private static FinanceTopExpensesInsightDto BuildTopExpensesInsight(
        IReadOnlyList<InsightExpenseRow> expenseRows,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string currency)
    {
        var totalExpenses = Math.Round(expenseRows.Sum(x => Math.Abs(x.Amount)), 2, MidpointRounding.AwayFromZero);
        if (expenseRows.Count == 0 || totalExpenses <= 0m)
        {
            return new FinanceTopExpensesInsightDto(
                windowStartUtc,
                windowEndUtc,
                0m,
                currency,
                "insufficient_data",
                $"No expense outflows were posted between {windowStartUtc:yyyy-MM-dd} and {windowEndUtc:yyyy-MM-dd}.",
                []);
        }

        var items = expenseRows
            .GroupBy(x => NormalizeCategory(x.TransactionType), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var amount = Math.Round(group.Sum(x => Math.Abs(x.Amount)), 2, MidpointRounding.AwayFromZero);
                var share = Math.Round(amount / totalExpenses, 4, MidpointRounding.AwayFromZero);
                var topCounterparty = group
                    .Select(x => NormalizeOptionalText(x.CounterpartyName))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                var narrative = topCounterparty is null
                    ? $"{group.Key} contributed {amount:0.00} of the {totalExpenses:0.00} expense window."
                    : $"{group.Key} contributed {amount:0.00}; the most visible counterparty was {topCounterparty}.";
                return new FinanceTopExpenseItemDto(group.Key, amount, currency, group.Count(), share, narrative);
            })
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var leadingShare = items.Count == 0 ? 0m : items[0].ShareOfExpenses;
        var trendLabel = leadingShare >= 0.4m ? "concentrated" : "distributed";
        var summary = $"{items[0].Label} is the largest expense driver at {items[0].Amount:0.00} across the latest {Math.Max(1, (windowEndUtc.Date - windowStartUtc.Date).Days + 1)} day window.";
        return new FinanceTopExpensesInsightDto(windowStartUtc, windowEndUtc, totalExpenses, currency, trendLabel, summary, items);
    }

    private static FinanceRevenueTrendInsightDto BuildRevenueTrendInsight(
        IReadOnlyList<InsightRevenueRow> revenueRows,
        DateTime previousStartUtc,
        DateTime currentStartUtc,
        DateTime asOfUtc,
        string currency,
        int trendWindowDays)
    {
        var currentRevenue = Math.Round(
            revenueRows.Where(x => x.IssuedUtc >= currentStartUtc && x.IssuedUtc <= asOfUtc).Sum(x => x.Amount),
            2,
            MidpointRounding.AwayFromZero);
        var previousRevenue = Math.Round(
            revenueRows.Where(x => x.IssuedUtc >= previousStartUtc && x.IssuedUtc < currentStartUtc).Sum(x => x.Amount),
            2,
            MidpointRounding.AwayFromZero);
        var deltaAmount = Math.Round(currentRevenue - previousRevenue, 2, MidpointRounding.AwayFromZero);
        var deltaPercent = previousRevenue <= 0m
            ? (decimal?)null
            : Math.Round(deltaAmount / previousRevenue, 4, MidpointRounding.AwayFromZero);

        var directionLabel = deltaAmount switch
        {
            > 0m => "up",
            < 0m => "down",
            _ => currentRevenue == 0m && previousRevenue == 0m ? "insufficient_data" : "flat"
        };

        var summary = directionLabel switch
        {
            "up" => $"Revenue improved by {deltaAmount:0.00} versus the prior {trendWindowDays} day comparison window.",
            "down" => $"Revenue declined by {Math.Abs(deltaAmount):0.00} versus the prior {trendWindowDays} day comparison window.",
            "flat" => $"Revenue held flat across the last two {trendWindowDays} day windows.",
            _ => "Revenue trend is based on sparse invoice activity."
        };

        return new FinanceRevenueTrendInsightDto(
            currentStartUtc,
            asOfUtc,
            previousStartUtc,
            currentStartUtc.AddTicks(-1),
            currentRevenue,
            previousRevenue,
            deltaAmount,
            deltaPercent,
            directionLabel,
            summary);
    }

    private static FinanceBurnRateInsightDto BuildBurnRateInsight(
        IReadOnlyList<InsightExpenseRow> expenseRows,
        IReadOnlyList<InsightRevenueRow> revenueRows,
        int expenseWindowDays,
        decimal availableCash,
        string currency,
        DateTime netBurnRevenueWindowStartUtc,
        DateTime asOfUtc)
    {
        var grossBurn = Math.Round(expenseRows.Sum(x => Math.Abs(x.Amount)), 2, MidpointRounding.AwayFromZero);
        var averageDailyBurn = Math.Round(grossBurn / expenseWindowDays, 2, MidpointRounding.AwayFromZero);
        var averageMonthlyBurn = Math.Round(averageDailyBurn * 30m, 2, MidpointRounding.AwayFromZero);
        var recentRevenue = Math.Round(
            revenueRows.Where(x => x.IssuedUtc >= netBurnRevenueWindowStartUtc && x.IssuedUtc <= asOfUtc).Sum(x => x.Amount),
            2,
            MidpointRounding.AwayFromZero);

        // Net burn uses the same recent window as the latest revenue trend so the runway signal
        // stays tied to posted outflows instead of fabricating forecast revenue.
        var netMonthlyBurn = Math.Round(Math.Max(averageMonthlyBurn - recentRevenue, 0m), 2, MidpointRounding.AwayFromZero);
        var runwayDenominator = netMonthlyBurn > 0m ? netMonthlyBurn : averageMonthlyBurn;
        var estimatedRunwayDays = runwayDenominator <= 0m
            ? (int?)null
            : Math.Max(0, (int)Math.Floor(availableCash / runwayDenominator * 30m));

        var riskLabel = estimatedRunwayDays switch
        {
            null when averageMonthlyBurn == 0m => "insufficient_data",
            <= 30 => "critical",
            <= 90 => "watch",
            _ => "stable"
        };

        var summary = riskLabel switch
        {
            "critical" => $"Runway is approximately {estimatedRunwayDays} days at the current burn profile.",
            "watch" => $"Runway is approximately {estimatedRunwayDays} days and should be monitored.",
            "stable" => $"Runway is approximately {estimatedRunwayDays} days with current outflow levels.",
            _ => "Burn rate is based on limited expense history."
        };

        return new FinanceBurnRateInsightDto(
            expenseWindowDays,
            averageDailyBurn,
            averageMonthlyBurn,
            netMonthlyBurn,
            Math.Round(availableCash, 2, MidpointRounding.AwayFromZero),
            estimatedRunwayDays,
            riskLabel,
            summary);
    }

    private static FinanceOverdueCustomerRiskInsightDto BuildOverdueCustomerRiskInsight(
        IReadOnlyList<InsightInvoiceRow> invoiceRows,
        IReadOnlyDictionary<Guid, decimal> completedIncomingByInvoice,
        DateTime asOfUtc,
        string currency)
    {
        var overdueInvoices = invoiceRows
            .Where(x => IsOpenReceivable(x.Status, x.SettlementStatus))
            .Select(x => new
            {
                Invoice = x,
                RemainingAmount = Math.Round(Math.Max(x.Amount - completedIncomingByInvoice.GetValueOrDefault(x.Id), 0m), 2, MidpointRounding.AwayFromZero),
                DaysOverdue = Math.Max(0, (int)Math.Floor((asOfUtc.Date - x.DueUtc.Date).TotalDays))
            })
            .Where(x => x.RemainingAmount > 0m && x.Invoice.DueUtc < asOfUtc)
            .ToList();

        var totalOverdueAmount = Math.Round(overdueInvoices.Sum(x => x.RemainingAmount), 2, MidpointRounding.AwayFromZero);
        if (overdueInvoices.Count == 0 || totalOverdueAmount <= 0m)
        {
            return new FinanceOverdueCustomerRiskInsightDto(0, 0, 0m, 0, 0m, "clear", "No overdue customer balance is currently open.", []);
        }

        var customers = overdueInvoices
            .GroupBy(x => new { x.Invoice.CounterpartyId, x.Invoice.CounterpartyName })
            .Select(group =>
            {
                var overdueAmount = Math.Round(group.Sum(x => x.RemainingAmount), 2, MidpointRounding.AwayFromZero);
                var maxDaysOverdue = group.Max(x => x.DaysOverdue);
                var concentrationRatio = Math.Round(overdueAmount / totalOverdueAmount, 4, MidpointRounding.AwayFromZero);
                var riskLabel = maxDaysOverdue >= 60 || concentrationRatio >= 0.5m ? "high" : maxDaysOverdue >= 30 ? "medium" : "low";
                var summary = $"{group.Key.CounterpartyName} carries {overdueAmount:0.00} overdue across {group.Count()} invoice(s).";
                return new FinanceOverdueCustomerRiskItemDto(
                    group.Key.CounterpartyId,
                    group.Key.CounterpartyName,
                    overdueAmount,
                    group.Count(),
                    maxDaysOverdue,
                    concentrationRatio,
                    riskLabel,
                    summary);
            })
            .OrderByDescending(x => x.OverdueAmount)
            .ThenBy(x => x.CustomerName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var maxDays = customers.Max(x => x.MaxDaysOverdue);
        var largestConcentration = customers.Max(x => x.ConcentrationRatio);
        var aggregateRisk = maxDays >= 60 || largestConcentration >= 0.5m ? "high" : maxDays >= 30 ? "medium" : "low";
        var summaryText = $"{overdueInvoices.Count} overdue invoice(s) total {totalOverdueAmount:0.00}; the largest customer concentration is {largestConcentration:P0}.";

        return new FinanceOverdueCustomerRiskInsightDto(
            overdueInvoices.Count,
            customers.Count,
            totalOverdueAmount,
            maxDays,
            largestConcentration,
            aggregateRisk,
            summaryText,
            customers);
    }

    private static FinancePayablePressureInsightDto BuildPayablePressureInsight(
        IReadOnlyList<InsightBillRow> billRows,
        IReadOnlyDictionary<Guid, decimal> completedOutgoingByBill,
        DateTime asOfUtc,
        DateTime payableWindowEndUtc,
        int payableWindowDays,
        decimal availableCash,
        string currency)
    {
        var openBills = billRows
            .Where(x => IsOpenPayable(x.Status, x.SettlementStatus))
            .Select(x => new
            {
                Bill = x,
                RemainingAmount = Math.Round(Math.Max(x.Amount - completedOutgoingByBill.GetValueOrDefault(x.Id), 0m), 2, MidpointRounding.AwayFromZero),
                UrgencyDays = (int)Math.Floor((x.DueUtc.Date - asOfUtc.Date).TotalDays)
            })
            .Where(x => x.RemainingAmount > 0m)
            .ToList();

        var overdueBills = openBills.Where(x => x.Bill.DueUtc < asOfUtc).ToList();
        var dueSoonBills = openBills.Where(x => x.Bill.DueUtc >= asOfUtc && x.Bill.DueUtc < payableWindowEndUtc).ToList();
        var overdueAmount = Math.Round(overdueBills.Sum(x => x.RemainingAmount), 2, MidpointRounding.AwayFromZero);
        var dueSoonAmount = Math.Round(dueSoonBills.Sum(x => x.RemainingAmount), 2, MidpointRounding.AwayFromZero);
        var totalPressuredAmount = overdueAmount + dueSoonAmount;
        var burdenRatio = availableCash <= 0m
            ? totalPressuredAmount > 0m ? 1m : (decimal?)null
            : Math.Round(totalPressuredAmount / availableCash, 4, MidpointRounding.AwayFromZero);

        var supplierItems = openBills
            .Where(x => x.Bill.DueUtc < payableWindowEndUtc)
            .GroupBy(x => new { x.Bill.CounterpartyId, x.Bill.CounterpartyName })
            .Select(group =>
            {
                var amount = Math.Round(group.Sum(x => x.RemainingAmount), 2, MidpointRounding.AwayFromZero);
                var hasOverdue = group.Any(x => x.Bill.DueUtc < asOfUtc);
                var maxUrgencyDays = group.Min(x => x.UrgencyDays);
                var riskLabel = hasOverdue ? "high" : maxUrgencyDays <= 3 ? "medium" : "low";
                var summary = $"{group.Key.CounterpartyName} has {amount:0.00} due within the next {payableWindowDays} days.";
                return new FinancePayablePressureItemDto(
                    group.Key.CounterpartyId,
                    group.Key.CounterpartyName,
                    amount,
                    group.Count(),
                    hasOverdue,
                    maxUrgencyDays,
                    riskLabel,
                    summary);
            })
            .OrderByDescending(x => x.DueAmount)
            .ThenBy(x => x.SupplierName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var riskLabel = overdueAmount > 0m || burdenRatio >= 0.8m ? "high" : dueSoonAmount > 0m || burdenRatio >= 0.4m ? "medium" : "low";
        var summary = totalPressuredAmount <= 0m
            ? "No overdue or near-term payables are currently pressuring cash."
            : $"{totalPressuredAmount:0.00} is overdue or due inside the next {payableWindowDays} days.";

        return new FinancePayablePressureInsightDto(
            overdueAmount,
            dueSoonAmount,
            overdueBills.Count,
            dueSoonBills.Count,
            burdenRatio,
            riskLabel,
            summary,
            supplierItems);
    }

    private static string BuildHeadline(
        FinanceRevenueTrendInsightDto revenueTrend,
        FinanceBurnRateInsightDto burnRate,
        FinanceOverdueCustomerRiskInsightDto overdueCustomerRisk,
        FinancePayablePressureInsightDto payablePressure)
    {
        if (string.Equals(overdueCustomerRisk.RiskLabel, "high", StringComparison.OrdinalIgnoreCase))
        {
            return "Customer collections risk is elevated.";
        }

        if (string.Equals(payablePressure.RiskLabel, "high", StringComparison.OrdinalIgnoreCase))
        {
            return "Near-term payables are putting pressure on cash.";
        }

        if (string.Equals(burnRate.RiskLabel, "critical", StringComparison.OrdinalIgnoreCase))
        {
            return "Cash runway needs immediate attention.";
        }

        if (string.Equals(revenueTrend.DirectionLabel, "down", StringComparison.OrdinalIgnoreCase))
        {
            return "Revenue softened in the latest comparison window.";
        }

        return "Finance posture is stable based on the current dataset.";
    }

    private static string BuildSummary(
        FinanceTopExpensesInsightDto topExpenses,
        FinanceRevenueTrendInsightDto revenueTrend,
        FinanceBurnRateInsightDto burnRate,
        FinanceOverdueCustomerRiskInsightDto overdueCustomerRisk,
        FinancePayablePressureInsightDto payablePressure) =>
        $"{revenueTrend.Summary} {burnRate.Summary} {overdueCustomerRisk.Summary} {payablePressure.Summary}";

    private static string? BuildCoverageNote(int expenseCount, int revenueCount, int invoiceCount, int billCount)
    {
        var sparseSignals = new List<string>(4);
        if (expenseCount < 3)
        {
            sparseSignals.Add("expense history");
        }

        if (revenueCount < 2)
        {
            sparseSignals.Add("revenue history");
        }

        if (invoiceCount == 0)
        {
            sparseSignals.Add("receivables");
        }

        if (billCount == 0)
        {
            sparseSignals.Add("payables");
        }

        return sparseSignals.Count == 0
            ? null
            : $"Sparse coverage for {string.Join(", ", sparseSignals)} can reduce confidence in trend-oriented sections.";
    }

    private static bool IsOpenReceivable(string status, string settlementStatus)
    {
        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant() ?? string.Empty;
        var normalizedSettlement = NormalizeOptionalText(settlementStatus)?.ToLowerInvariant() ?? string.Empty;
        return normalizedSettlement != FinanceSettlementStatuses.Paid &&
               normalizedStatus is not ("paid" or "rejected" or "void");
    }

    private static bool IsOpenPayable(string status, string settlementStatus)
    {
        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant() ?? string.Empty;
        var normalizedSettlement = NormalizeOptionalText(settlementStatus)?.ToLowerInvariant() ?? string.Empty;
        return normalizedSettlement != FinanceSettlementStatuses.Paid &&
               normalizedStatus is not ("paid" or "void" or "cancelled");
    }

    private sealed record InsightExpenseRow(
        Guid Id,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string Currency,
        string? CounterpartyName);

    private sealed record InsightRevenueRow(
        Guid Id,
        DateTime IssuedUtc,
        decimal Amount,
        string Currency);

    private sealed record InsightInvoiceRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record InsightBillRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record InsightAllocationRow(Guid DocumentId, decimal Amount);

    private sealed record FinanceInsightQueryParameters(
        Guid CompanyId,
        DateTime GeneratedAtUtc,
        DateTime AsOfUtc,
        int ExpenseWindowDays,
        int TrendWindowDays,
        int PayableWindowDays,
        string SnapshotKey,
        string CacheKey);

    private sealed record FinanceInsightsSnapshotCacheEnvelope(
        string SnapshotKey,
        string CacheKey,
        DateTime ExpiresAtUtc,
        FinanceInsightsDto Insights);
}
