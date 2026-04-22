using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceSummaryConsistencyChecker
{
    private readonly VirtualCompanyDbContext _dbContext;

    public FinanceSummaryConsistencyChecker(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FinanceSummaryConsistencyResultDto> CheckAsync(
        Guid companyId,
        DateTime asOfUtc,
        int recentAssetPurchaseLimit,
        FinanceSummaryDto summary,
        CancellationToken cancellationToken)
    {
        var normalizedRecentAssetPurchaseLimit = Math.Clamp(recentAssetPurchaseLimit, 1, 20);
        var monthStartUtc = new DateTime(asOfUtc.Year, asOfUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEndExclusiveUtc = monthStartUtc.AddMonths(1);
        var monthCutoffExclusiveUtc = asOfUtc < monthEndExclusiveUtc
            ? asOfUtc.AddTicks(1)
            : monthEndExclusiveUtc;

        var cashDeltaRows = await _dbContext.SimulationCashDeltaRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SimulationDateUtc <= asOfUtc)
            .Select(x => new CashDeltaRow(x.SimulationDateUtc, x.CreatedUtc, x.CashAfter))
            .ToListAsync(cancellationToken);

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IssuedUtc <= asOfUtc)
            .Select(x => new InvoiceRow(
                x.Id,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ReceivedUtc <= asOfUtc)
            .Select(x => new BillRow(
                x.Id,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var assetRows = await _dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PurchasedUtc <= asOfUtc)
            .Select(x => new AssetRow(
                x.Id,
                x.ReferenceNumber,
                x.PurchasedUtc,
                x.Amount,
                x.FundingBehavior,
                x.FundingSettlementStatus,
                x.Status))
            .ToListAsync(cancellationToken);

        var incomingAllocationRows = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= asOfUtc)
            .Select(x => new AllocationRow(x.InvoiceId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var outgoingAllocationRows = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= asOfUtc)
            .Select(x => new AllocationRow(x.BillId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = incomingAllocationRows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Round(x.Sum(y => y.Amount)));
        var completedOutgoingByBill = outgoingAllocationRows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Round(x.Sum(y => y.Amount)));

        // When simulation cash deltas exist they are the most direct point-in-time source record.
        var expectedCurrentCash = cashDeltaRows.Count > 0
            ? Round(cashDeltaRows
                .OrderByDescending(x => x.SimulationDateUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .Select(x => x.CashAfter)
                .First())
            : await CalculateLedgerCashFallbackAsync(companyId, asOfUtc, cancellationToken);

        var expectedAccountsReceivable = Round(invoiceRows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus))
            .Sum(x => RemainingBalance(x.Amount, completedIncomingByInvoice.GetValueOrDefault(x.Id))));

        var expectedOverdueReceivables = Round(invoiceRows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus) && x.DueUtc < asOfUtc)
            .Sum(x => RemainingBalance(x.Amount, completedIncomingByInvoice.GetValueOrDefault(x.Id))));

        var payableAssets = assetRows
            .Where(IsOpenPayableAsset)
            .ToList();

        var expectedAccountsPayable = Round(
            billRows
                .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
                .Sum(x => RemainingBalance(x.Amount, completedOutgoingByBill.GetValueOrDefault(x.Id))) +
            payableAssets.Sum(x => x.Amount));

        var expectedOverduePayables = Round(
            billRows
                .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus) && x.DueUtc < asOfUtc)
                .Sum(x => RemainingBalance(x.Amount, completedOutgoingByBill.GetValueOrDefault(x.Id))) +
            payableAssets.Where(x => x.PurchasedUtc < asOfUtc).Sum(x => x.Amount));

        var expectedMonthlyRevenue = Round(invoiceRows
            .Where(x =>
                x.IssuedUtc >= monthStartUtc &&
                x.IssuedUtc < monthCutoffExclusiveUtc &&
                IsIncludedInMonthlyRevenue(x.Status))
            .Sum(x => x.Amount));

        // Month-to-date costs intentionally include active asset purchases so payable-funded
        // acquisitions reconcile against the same operational summary seen by dashboards.
        var expectedMonthlyCosts = Round(
            billRows
                .Where(x =>
                    x.ReceivedUtc >= monthStartUtc &&
                    x.ReceivedUtc < monthCutoffExclusiveUtc &&
                    IsIncludedInMonthlyCosts(x.Status))
                .Sum(x => x.Amount) +
            assetRows
                .Where(x =>
                    x.PurchasedUtc >= monthStartUtc &&
                    x.PurchasedUtc < monthCutoffExclusiveUtc &&
                    IsIncludedAssetCost(x.Status))
                .Sum(x => x.Amount));

        var recentAssetPurchases = assetRows
            .Where(x => IsIncludedAssetCost(x.Status))
            .OrderByDescending(x => x.PurchasedUtc)
            .ThenBy(x => x.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Take(normalizedRecentAssetPurchaseLimit)
            .ToList();

        var metrics = new[]
        {
            CreateMetric("current_cash", expectedCurrentCash, summary.CurrentCash),
            CreateMetric("accounts_receivable", expectedAccountsReceivable, summary.AccountsReceivable),
            CreateMetric("overdue_receivables", expectedOverdueReceivables, summary.OverdueReceivables),
            CreateMetric("accounts_payable", expectedAccountsPayable, summary.AccountsPayable),
            CreateMetric("overdue_payables", expectedOverduePayables, summary.OverduePayables),
            CreateMetric("monthly_revenue", expectedMonthlyRevenue, summary.MonthlyRevenue),
            CreateMetric("monthly_costs", expectedMonthlyCosts, summary.MonthlyCosts),
            CreateMetric("recent_asset_purchase_count", recentAssetPurchases.Count, summary.RecentAssetPurchaseCount),
            CreateMetric("recent_asset_purchase_total_amount", Round(recentAssetPurchases.Sum(x => x.Amount)), summary.RecentAssetPurchaseTotalAmount)
        };

        return new FinanceSummaryConsistencyResultDto(
            companyId,
            asOfUtc,
            metrics.All(x => x.IsMatch),
            invoiceRows.Count + billRows.Count + assetRows.Count + incomingAllocationRows.Count + outgoingAllocationRows.Count + cashDeltaRows.Count,
            metrics);
    }

    private async Task<decimal> CalculateLedgerCashFallbackAsync(
        Guid companyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var cashAccounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new CashAccountRow(x.Id, x.Code, x.Name, x.AccountType, x.OpeningBalance))
            .ToListAsync(cancellationToken);

        cashAccounts = cashAccounts.Where(IsCashAccount).ToList();

        if (cashAccounts.Count == 0)
        {
            return 0m;
        }

        var cashAccountIds = cashAccounts.Select(x => x.Id).ToArray();

        var balances = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && cashAccountIds.Contains(x.AccountId) && x.AsOfUtc <= asOfUtc)
            .Select(x => new BalanceRow(x.AccountId, x.AsOfUtc, x.Amount))
            .ToListAsync(cancellationToken);

        var transactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && cashAccountIds.Contains(x.AccountId) && x.TransactionUtc <= asOfUtc)
            .Select(x => new TransactionRow(x.AccountId, x.TransactionUtc, x.Amount))
            .ToListAsync(cancellationToken);

        var latestBalanceByAccount = balances
            .GroupBy(x => x.AccountId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AsOfUtc).First());
        var transactionsByAccount = transactions
            .GroupBy(x => x.AccountId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var expectedCash = cashAccounts.Sum(account =>
        {
            var accountTransactions = transactionsByAccount.GetValueOrDefault(account.Id) ?? [];
            if (latestBalanceByAccount.TryGetValue(account.Id, out var balance))
            {
                return balance.Amount + accountTransactions.Where(x => x.TransactionUtc > balance.AsOfUtc).Sum(x => x.Amount);
            }

            return account.OpeningBalance + accountTransactions.Sum(x => x.Amount);
        });

        return Round(expectedCash);
    }

    private static FinanceSummaryConsistencyMetricDto CreateMetric(string metricKey, decimal expectedValue, decimal actualValue) =>
        new(metricKey, Round(expectedValue), Round(actualValue), Round(expectedValue) == Round(actualValue));

    private static bool IsCashAccount(CashAccountRow account) =>
        string.Equals(account.AccountType, "cash", StringComparison.OrdinalIgnoreCase) ||
        account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        account.Code.StartsWith("10", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncludedReceivable(string status, string settlementStatus) =>
        !string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) &&
        NormalizeStatus(status) is not ("paid" or "cancelled" or "canceled" or "void" or "voided" or "written_off" or "rejected");

    private static bool IsIncludedPayable(string status, string settlementStatus) =>
        !string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) &&
        NormalizeStatus(status) is not ("paid" or "cancelled" or "canceled" or "void" or "voided");

    private static bool IsOpenPayableAsset(AssetRow row) =>
        string.Equals(FinanceAssetFundingBehaviors.Normalize(row.FundingBehavior), FinanceAssetFundingBehaviors.Payable, StringComparison.Ordinal) &&
        !string.Equals(FinanceSettlementStatuses.Normalize(row.FundingSettlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) &&
        IsIncludedAssetCost(row.Status);

    private static bool IsIncludedInMonthlyRevenue(string status) =>
        NormalizeStatus(status) is not ("cancelled" or "canceled" or "void" or "voided" or "rejected");

    private static bool IsIncludedInMonthlyCosts(string status) =>
        NormalizeStatus(status) is not ("cancelled" or "canceled" or "void" or "voided" or "rejected");

    private static bool IsIncludedAssetCost(string status) =>
        string.Equals(NormalizeStatus(status), NormalizeStatus(FinanceAssetStatuses.Active), StringComparison.Ordinal);

    private static decimal RemainingBalance(decimal amount, decimal allocatedAmount) =>
        Round(Math.Max(0m, amount - allocatedAmount));

    private static decimal Round(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private sealed record CashDeltaRow(DateTime SimulationDateUtc, DateTime CreatedUtc, decimal CashAfter);
    private sealed record CashAccountRow(Guid Id, string Code, string Name, string AccountType, decimal OpeningBalance);
    private sealed record BalanceRow(Guid AccountId, DateTime AsOfUtc, decimal Amount);
    private sealed record TransactionRow(Guid AccountId, DateTime TransactionUtc, decimal Amount);
    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
    private sealed record InvoiceRow(Guid Id, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Status, string SettlementStatus);
    private sealed record BillRow(Guid Id, DateTime ReceivedUtc, DateTime DueUtc, decimal Amount, string Status, string SettlementStatus);
    private sealed record AssetRow(Guid Id, string ReferenceNumber, DateTime PurchasedUtc, decimal Amount, string FundingBehavior, string FundingSettlementStatus, string Status);
}
