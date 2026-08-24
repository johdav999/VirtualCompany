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
        string sourceFilter,
        CancellationToken cancellationToken)
    {
        var normalizedSourceFilter = FinanceDataSources.NormalizeOperationalRead(sourceFilter);
        var sourcePolicy = new FinanceRecordSourcePolicy(_dbContext);
        var normalizedRecentAssetPurchaseLimit = Math.Clamp(recentAssetPurchaseLimit, 1, 20);
        var monthStartUtc = new DateTime(asOfUtc.Year, asOfUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEndExclusiveUtc = monthStartUtc.AddMonths(1);
        var monthCutoffExclusiveUtc = asOfUtc < monthEndExclusiveUtc
            ? asOfUtc.AddTicks(1)
            : monthEndExclusiveUtc;

        var cashDeltaRows = normalizedSourceFilter == FinanceDataSources.Simulation
            ? await _dbContext.SimulationCashDeltaRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SimulationDateUtc <= asOfUtc)
                .Select(x => new CashDeltaRow(x.SimulationDateUtc, x.CreatedUtc, x.CashAfter))
                .ToListAsync(cancellationToken)
            : [];

        var invoiceRows = await sourcePolicy.ApplyFilter(_dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IssuedUtc <= asOfUtc), companyId, normalizedSourceFilter, "invoice")
            .Select(x => new InvoiceRow(
                x.Id,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await sourcePolicy.ApplyFilter(_dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ReceivedUtc <= asOfUtc), companyId, normalizedSourceFilter, "supplier_invoice", "bill")
            .Select(x => new BillRow(
                x.Id,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var assetRows = await sourcePolicy.ApplyFilter(_dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PurchasedUtc <= asOfUtc), companyId, normalizedSourceFilter, "asset")
            .Select(x => new AssetRow(
                x.Id,
                x.ReferenceNumber,
                x.PurchasedUtc,
                x.Amount,
                x.FundingBehavior,
                x.FundingSettlementStatus,
                x.Status))
            .ToListAsync(cancellationToken);

        var incomingAllocationRows = await sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= asOfUtc), companyId, normalizedSourceFilter)
            .Select(x => new AllocationRow(x.InvoiceId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var outgoingAllocationRows = await sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= asOfUtc), companyId, normalizedSourceFilter)
            .Select(x => new AllocationRow(x.BillId!.Value, x.AllocatedAmount))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = incomingAllocationRows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Round(x.Sum(y => y.Amount)));
        var completedOutgoingByBill = outgoingAllocationRows
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => Round(x.Sum(y => y.Amount)));

        // The finance ledger and balance snapshots are authoritative for reporting. Simulation
        // cash deltas remain audit evidence, but can represent an intermediate event within a day.
        var expectedCurrentCash = await CalculateLedgerCashFallbackAsync(companyId, asOfUtc, normalizedSourceFilter, cancellationToken);

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
        string sourceFilter,
        CancellationToken cancellationToken)
    {
        var sourcePolicy = new FinanceRecordSourcePolicy(_dbContext);
        var cashAccounts = await sourcePolicy.ApplyFilter(_dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId), companyId, sourceFilter, "account")
            .Select(x => new CashAccountRow(x.Id, x.Code, x.Name, x.AccountType, x.OpeningBalance))
            .ToListAsync(cancellationToken);

        cashAccounts = cashAccounts.Where(IsCashAccount).ToList();

        if (cashAccounts.Count == 0)
        {
            return 0m;
        }

        var cashAccountIds = cashAccounts.Select(x => x.Id).ToArray();

        var balances = await sourcePolicy.ApplyFilter(_dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && cashAccountIds.Contains(x.AccountId) && x.AsOfUtc <= asOfUtc), companyId, sourceFilter, "balance")
            .Select(x => new BalanceRow(x.AccountId, x.AsOfUtc, x.Amount))
            .ToListAsync(cancellationToken);

        var transactions = await sourcePolicy.ApplyFilter(_dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && cashAccountIds.Contains(x.AccountId) && x.TransactionUtc <= asOfUtc), companyId, sourceFilter, "voucher", "payment", "transaction")
            .Select(x => new TransactionRow(
                x.AccountId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Description,
                x.ExternalReference))
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
                return balance.Amount + accountTransactions
                    .Where(x => x.TransactionUtc > balance.AsOfUtc)
                    .Where(IsCashMovementTransaction)
                    .Sum(x => x.Amount);
            }

            return account.OpeningBalance + accountTransactions.Where(IsCashMovementTransaction).Sum(x => x.Amount);
        });

        return Round(expectedCash);
    }

    private static FinanceSummaryConsistencyMetricDto CreateMetric(string metricKey, decimal expectedValue, decimal actualValue) =>
        new(metricKey, Round(expectedValue), Round(actualValue), Round(expectedValue) == Round(actualValue));

    private static bool IsCashAccount(CashAccountRow account) =>
        (string.Equals(account.AccountType, "cash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(account.AccountType, "asset", StringComparison.OrdinalIgnoreCase)) &&
        (account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
            account.Name.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
            account.Name.Contains("kassa", StringComparison.OrdinalIgnoreCase) ||
            account.Name.Contains("plusgiro", StringComparison.OrdinalIgnoreCase) ||
            account.Code.StartsWith("19", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(account.Code, "1000", StringComparison.OrdinalIgnoreCase));

    private static bool IsCashMovementTransaction(TransactionRow transaction) =>
        !string.Equals(transaction.TransactionType, "voucher", StringComparison.OrdinalIgnoreCase) ||
        IsExplicitBankPaymentVoucher(transaction.Description, transaction.ExternalReference);

    private static bool IsExplicitBankPaymentVoucher(string description, string externalReference)
    {
        var text = string.Concat(description, " ", externalReference);
        var mentionsBank =
            text.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("bankgiro", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("plusgiro", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("kassa", StringComparison.OrdinalIgnoreCase);
        var mentionsPayment =
            text.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("betal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("inbetal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("utbetal", StringComparison.OrdinalIgnoreCase);

        return mentionsBank && mentionsPayment;
    }

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
    private sealed record TransactionRow(
        Guid AccountId,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string Description,
        string ExternalReference);
    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
    private sealed record InvoiceRow(Guid Id, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Status, string SettlementStatus);
    private sealed record BillRow(Guid Id, DateTime ReceivedUtc, DateTime DueUtc, decimal Amount, string Status, string SettlementStatus);
    private sealed record AssetRow(Guid Id, string ReferenceNumber, DateTime PurchasedUtc, decimal Amount, string FundingBehavior, string FundingSettlementStatus, string Status);
}
