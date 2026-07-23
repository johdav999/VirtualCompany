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
    public async Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        return await BuildAccountBalancesAsync(query.CompanyId, asOfUtc, cancellationToken);
    }

    public async Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
        GetFinanceAgentQueryQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        if (!FinanceAgentQueryRouting.TryResolveIntent(query.QueryText, out var intent))
        {
            throw new ArgumentException(
                $"Unsupported finance agent query '{query.QueryText}'. Supported queries: {string.Join(", ", FinanceAgentQueryRouting.SupportedPhrases)}.",
                nameof(query));
        }

        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        var timeZone = await ResolveCompanyTimeZoneAsync(query.CompanyId, cancellationToken);

        return intent switch
        {
            var value when string.Equals(value, FinanceAgentQueryIntents.WhatShouldIPayThisWeek, StringComparison.Ordinal) =>
                await ResolveWhatShouldIPayThisWeekAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken),
            var value when string.Equals(value, FinanceAgentQueryIntents.WhichCustomersAreOverdue, StringComparison.Ordinal) =>
                await ResolveWhichCustomersAreOverdueAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken),
            _ => await ResolveWhyIsCashDownThisMonthAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken)
        };
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhatShouldIPayThisWeekAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var weekWindow = ResolveCurrentWeekWindow(asOfUtc, timeZone);
        var completedAllocations = await LoadBillAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Outgoing,
            null,
            asOfUtc.AddTicks(1),
            cancellationToken);
        var scheduledAllocations = await LoadBillAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Pending,
            PaymentTypes.Outgoing,
            weekWindow.WindowStartUtc,
            weekWindow.WindowEndUtc,
            cancellationToken);

        var rows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AgentBillQueryRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.BillNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var localAsOfDate = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, timeZone).Date;
        var items = rows
            .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
            .Select(row =>
            {
                var completed = completedAllocations.GetValueOrDefault(row.Id);
                var scheduled = scheduledAllocations.GetValueOrDefault(row.Id);
                var remaining = CalculateRemainingBalance(row.Amount, completed.Amount);
                if (remaining <= 0m || row.DueUtc >= weekWindow.WindowEndUtc)
                {
                    return null;
                }

                var localDueDate = TimeZoneInfo.ConvertTimeFromUtc(row.DueUtc, timeZone).Date;
                var daysOverdue = row.DueUtc < asOfUtc
                    ? Math.Max(0, (localAsOfDate - localDueDate).Days)
                    : (int?)null;
                var sourceRecordIds = DistinctIds([row.Id, .. completed.SourceRecordIds, .. scheduled.SourceRecordIds]);
                return new FinanceAgentQueryItemDto(
                    row.Id,
                    "bill",
                    row.CounterpartyId,
                    row.CounterpartyName,
                    row.BillNumber,
                    row.DueUtc,
                    remaining,
                    row.Currency,
                    daysOverdue.HasValue
                        ? $"Overdue by {daysOverdue.Value} day(s)."
                        : "Due within the current company week.",
                    0,
                    daysOverdue,
                    null,
                    sourceRecordIds,
                    [
                        new FinanceAgentMetricComponentDto("original_amount", "Original amount", row.Amount, null, row.Amount, row.Currency, [row.Id]),
                        new FinanceAgentMetricComponentDto("completed_outgoing_allocations", "Completed outgoing allocations", completed.Amount, null, completed.Amount, row.Currency, completed.SourceRecordIds),
                        new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", remaining, null, remaining, row.Currency, [row.Id, .. completed.SourceRecordIds]),
                        new FinanceAgentMetricComponentDto("scheduled_outgoing_this_week", "Scheduled outgoing this week", scheduled.Amount, null, scheduled.Amount, row.Currency, scheduled.SourceRecordIds)
                    ]);
            })
            .Where(x => x is not null)
            .OrderBy(x => x!.DaysOverdue.HasValue ? 0 : 1)
            .ThenBy(x => x!.DueUtc)
            .ThenByDescending(x => x!.Amount)
            .ThenBy(x => x!.RecordId)
            .Select((item, index) => item! with { SortOrder = index + 1 })
            .ToArray();

        var currency = ResolveCurrency(items.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
        var sourceRecordIds = DistinctIds(items.SelectMany(x => x.SourceRecordIds));
        var totalAmount = items.Sum(x => x.Amount);
        var overdueCount = items.Count(x => x.DaysOverdue.HasValue);

        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhatShouldIPayThisWeek,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            $"Selected {items.Length} payable item(s) totaling {totalAmount:0.00} {currency} for the current company week; {overdueCount} item(s) are already overdue.",
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(
                asOfUtc,
                weekWindow.WindowStartUtc,
                weekWindow.WindowEndUtc,
                null,
                null,
                timeZone.Id),
            items,
            [
                new FinanceAgentMetricComponentDto("recommended_payables_total", "Recommended payables total", totalAmount, null, totalAmount, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("recommended_payables_count", "Recommended payables count", items.Length, null, items.Length, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("overdue_payables_count", "Overdue payables count", overdueCount, null, overdueCount, currency, DistinctIds(items.Where(x => x.DaysOverdue.HasValue).SelectMany(x => x.SourceRecordIds)))
            ],
            sourceRecordIds);
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhichCustomersAreOverdueAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var completedAllocations = await LoadInvoiceAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Incoming,
            null,
            asOfUtc.AddTicks(1),
            cancellationToken);

        var rows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AgentInvoiceQueryRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.InvoiceNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var localAsOfDate = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, timeZone).Date;
        var items = rows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus) && x.DueUtc < asOfUtc)
            .Select(row =>
            {
                var completed = completedAllocations.GetValueOrDefault(row.Id);
                var remaining = CalculateRemainingBalance(row.Amount, completed.Amount);
                if (remaining <= 0m)
                {
                    return null;
                }

                var daysOverdue = Math.Max(0, (localAsOfDate - TimeZoneInfo.ConvertTimeFromUtc(row.DueUtc, timeZone).Date).Days);
                var agingBucket = ResolveAgingBucket(daysOverdue);
                var sourceRecordIds = DistinctIds([row.Id, .. completed.SourceRecordIds]);
                return new FinanceAgentQueryItemDto(
                    row.Id,
                    "invoice",
                    row.CounterpartyId,
                    row.CounterpartyName,
                    row.InvoiceNumber,
                    row.DueUtc,
                    remaining,
                    row.Currency,
                    $"{agingBucket} overdue.",
                    0,
                    daysOverdue,
                    agingBucket,
                    sourceRecordIds,
                    [
                        new FinanceAgentMetricComponentDto("original_amount", "Original amount", row.Amount, null, row.Amount, row.Currency, [row.Id]),
                        new FinanceAgentMetricComponentDto("completed_incoming_allocations", "Completed incoming allocations", completed.Amount, null, completed.Amount, row.Currency, completed.SourceRecordIds),
                        new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", remaining, null, remaining, row.Currency, [row.Id, .. completed.SourceRecordIds])
                    ]);
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.DaysOverdue)
            .ThenByDescending(x => x!.Amount)
            .ThenBy(x => x!.RecordId)
            .Select((item, index) => item! with { SortOrder = index + 1 })
            .ToArray();

        var currency = ResolveCurrency(items.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
        var sourceRecordIds = DistinctIds(items.SelectMany(x => x.SourceRecordIds));
        var totalOutstanding = items.Sum(x => x.Amount);

        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhichCustomersAreOverdue,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            $"Selected {items.Length} overdue receivable item(s) totaling {totalOutstanding:0.00} {currency}.",
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(asOfUtc, null, asOfUtc, null, null, timeZone.Id),
            items,
            [
                new FinanceAgentMetricComponentDto("overdue_receivables_total", "Overdue receivables total", totalOutstanding, null, totalOutstanding, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("overdue_receivables_count", "Overdue receivables count", items.Length, null, items.Length, currency, sourceRecordIds)
            ],
            sourceRecordIds);
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhyIsCashDownThisMonthAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var monthWindow = ResolveMonthToDateWindow(asOfUtc, timeZone);
        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AccountRow(
                x.Id,
                x.Code,
                x.Name,
                x.AccountType,
                x.OpeningBalance,
                x.Currency))
            .ToListAsync(cancellationToken);

        var cashAccountIds = accounts
            .Where(x => IsCashAccount(x.Name, x.Code, x.AccountType) || string.Equals(x.AccountType, "asset", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToArray();

        var currentRows = await LoadCashMovementRowsAsync(companyId, cashAccountIds, monthWindow.WindowStartUtc, monthWindow.WindowEndUtc, cancellationToken);
        var comparisonRows = await LoadCashMovementRowsAsync(companyId, cashAccountIds, monthWindow.ComparisonStartUtc!.Value, monthWindow.ComparisonEndUtc!.Value, cancellationToken);

        var currency = ResolveCurrency(
            currentRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))
                .Concat(comparisonRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))));

        var netCurrent = Math.Round(currentRows.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var netPrevious = Math.Round(comparisonRows.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var inflowsCurrent = Math.Round(currentRows.Where(x => x.Amount > 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var inflowsPrevious = Math.Round(comparisonRows.Where(x => x.Amount > 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var outflowsCurrent = Math.Round(-currentRows.Where(x => x.Amount < 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var outflowsPrevious = Math.Round(-comparisonRows.Where(x => x.Amount < 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);

        var categoryComponents = BuildCashMovementCategoryComponents(currentRows, comparisonRows, currency)
            .OrderBy(x => x.Delta)
            .ThenBy(x => x.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        var items = categoryComponents
            .Where(x => x.Delta < 0m)
            .Select((component, index) => new FinanceAgentQueryItemDto(
                null,
                "cash_movement_category",
                null,
                null,
                component.ComponentKey,
                null,
                component.Delta,
                component.Currency,
                BuildCashMovementReason(component),
                index + 1,
                null,
                null,
                component.SourceRecordIds,
                [component]))
            .ToArray();

        var headlineDrivers = categoryComponents
            .Where(x => x.Delta < 0m)
            .Take(2)
            .Select(x => $"{x.Label} ({x.Delta:0.00} {x.Currency})")
            .ToArray();
        var netDelta = Math.Round(netCurrent - netPrevious, 2, MidpointRounding.AwayFromZero);
        var summary = headlineDrivers.Length == 0
            ? $"Net cash movement changed by {netDelta:0.00} {currency} month-to-date versus the same number of days in the prior month."
            : $"Net cash movement is down by {Math.Abs(Math.Min(netDelta, 0m)):0.00} {currency} month-to-date versus the same number of days in the prior month. Largest drivers: {string.Join(" and ", headlineDrivers)}.";

        var sourceRecordIds = DistinctIds(currentRows.Select(x => x.Id).Concat(comparisonRows.Select(x => x.Id)));
        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhyIsCashDownThisMonth,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            summary,
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(
                asOfUtc,
                monthWindow.WindowStartUtc,
                monthWindow.WindowEndUtc,
                monthWindow.ComparisonStartUtc,
                monthWindow.ComparisonEndUtc,
                timeZone.Id),
            items,
            new[]
            {
                new FinanceAgentMetricComponentDto("net_cash_movement", "Net cash movement", netCurrent, netPrevious, netDelta, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("cash_inflows", "Cash inflows", inflowsCurrent, inflowsPrevious, inflowsCurrent - inflowsPrevious, currency, DistinctIds(currentRows.Where(x => x.Amount > 0m).Select(x => x.Id).Concat(comparisonRows.Where(x => x.Amount > 0m).Select(x => x.Id)))),
                new FinanceAgentMetricComponentDto("cash_outflows", "Cash outflows", -outflowsCurrent, -outflowsPrevious, outflowsPrevious - outflowsCurrent, currency, DistinctIds(currentRows.Where(x => x.Amount < 0m).Select(x => x.Id).Concat(comparisonRows.Where(x => x.Amount < 0m).Select(x => x.Id))))
            }.Concat(categoryComponents).ToArray(),
            sourceRecordIds);
    }

}

