using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceSummaryQueryService : IFinanceSummaryQueryService
{
    private const int DefaultRecentAssetPurchaseLimit = 5;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly FinanceSummaryConsistencyChecker _consistencyChecker;

    public CompanyFinanceSummaryQueryService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        FinanceSummaryConsistencyChecker consistencyChecker,
        ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _consistencyChecker = consistencyChecker;
    }

    public async Task<FinanceSummaryDto> GetAsync(
        GetFinanceSummaryQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var effectiveAsOfUtc = await ResolveAsOfUtcAsync(query.CompanyId, query.AsOfUtc, cancellationToken);
        var recentAssetPurchaseLimit = Math.Clamp(
            query.RecentAssetPurchaseLimit <= 0 ? DefaultRecentAssetPurchaseLimit : query.RecentAssetPurchaseLimit,
            1,
            20);

        var monthStartUtc = new DateTime(
            effectiveAsOfUtc.Year,
            effectiveAsOfUtc.Month,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var monthEndExclusiveUtc = monthStartUtc.AddMonths(1);
        var monthCutoffExclusiveUtc = effectiveAsOfUtc < monthEndExclusiveUtc
            ? effectiveAsOfUtc.AddTicks(1)
            : monthEndExclusiveUtc;

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new FinanceAccountRow(
                x.Id,
                x.Code,
                x.Name,
                x.AccountType,
                x.OpeningBalance,
                x.Currency))
            .ToListAsync(cancellationToken);

        var cashAccounts = accounts
            .Where(IsCashAccount)
            .ToList();

        var currentCash = await CalculateCurrentCashAsync(
            query.CompanyId,
            effectiveAsOfUtc,
            cashAccounts,
            cancellationToken);

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc <= effectiveAsOfUtc)
            .Select(x => new InvoiceRow(
                x.Id,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.ReceivedUtc <= effectiveAsOfUtc)
            .Select(x => new BillRow(
                x.Id,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = await LoadInvoiceAllocationLookupAsync(
            query.CompanyId,
            effectiveAsOfUtc,
            cancellationToken);
        var completedOutgoingByBill = await LoadBillAllocationLookupAsync(
            query.CompanyId,
            effectiveAsOfUtc,
            cancellationToken);

        var accountsReceivable = Round(invoiceRows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus))
            .Sum(x => CalculateRemainingBalance(x.Amount, completedIncomingByInvoice.GetValueOrDefault(x.Id))));

        var overdueReceivables = Round(invoiceRows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus) && x.DueUtc < effectiveAsOfUtc)
            .Sum(x => CalculateRemainingBalance(x.Amount, completedIncomingByInvoice.GetValueOrDefault(x.Id))));

        var billPayables = Round(billRows
            .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
            .Sum(x => CalculateRemainingBalance(x.Amount, completedOutgoingByBill.GetValueOrDefault(x.Id))));

        var overdueBillPayables = Round(billRows
            .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus) && x.DueUtc < effectiveAsOfUtc)
            .Sum(x => CalculateRemainingBalance(x.Amount, completedOutgoingByBill.GetValueOrDefault(x.Id))));

        var assetRows = await _dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PurchasedUtc <= effectiveAsOfUtc)
            .Select(x => new AssetRow(
                x.Id,
                x.ReferenceNumber,
                x.Name,
                x.Category,
                x.PurchasedUtc,
                x.Amount,
                x.Currency,
                x.FundingBehavior,
                x.FundingSettlementStatus,
                x.Status))
            .ToListAsync(cancellationToken);

        var openPayableAssets = assetRows
            .Where(IsOpenPayableAsset)
            .ToList();

        var accountsPayable = Round(billPayables + openPayableAssets.Sum(x => x.Amount));
        var overduePayables = Round(
            overdueBillPayables +
            openPayableAssets.Where(x => x.PurchasedUtc < effectiveAsOfUtc).Sum(x => x.Amount));

        var monthlyRevenue = Round(invoiceRows
            .Where(x =>
                x.IssuedUtc >= monthStartUtc &&
                x.IssuedUtc < monthCutoffExclusiveUtc &&
                IsIncludedInMonthlyRevenue(x.Status))
            .Sum(x => x.Amount));

        var monthlyBillCosts = billRows
            .Where(x =>
                x.ReceivedUtc >= monthStartUtc &&
                x.ReceivedUtc < monthCutoffExclusiveUtc &&
                IsIncludedInMonthlyCosts(x.Status))
            .Sum(x => x.Amount);

        // Operational monthly costs include month-to-date asset acquisitions so payable-funded
        // purchases remain visible even when the simulation does not create a matching bill row.
        var monthlyAssetCosts = assetRows
            .Where(x =>
                x.PurchasedUtc >= monthStartUtc &&
                x.PurchasedUtc < monthCutoffExclusiveUtc &&
                IsIncludedAssetCost(x.Status))
            .Sum(x => x.Amount);

        var monthlyCosts = Round(monthlyBillCosts + monthlyAssetCosts);

        var recentAssetPurchases = assetRows
            .Where(x => IsIncludedAssetCost(x.Status))
            .OrderByDescending(x => x.PurchasedUtc)
            .ThenBy(x => x.ReferenceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Take(recentAssetPurchaseLimit)
            .Select(x => new FinanceSummaryAssetPurchaseDto(
                x.Id,
                query.CompanyId,
                x.ReferenceNumber,
                x.Name,
                x.Category,
                x.PurchasedUtc,
                x.Amount,
                x.Currency,
                x.FundingBehavior,
                x.FundingSettlementStatus))
            .ToList();

        var recentAssetPurchaseCount = recentAssetPurchases.Count;
        var recentAssetPurchaseTotalAmount = Round(recentAssetPurchases.Sum(x => x.Amount));

        var currency = ResolveCurrency(
            cashAccounts.Select(x => x.Currency),
            invoiceRows.Select(x => x.Currency),
            billRows.Select(x => x.Currency),
            assetRows.Select(x => x.Currency));

        var hasFinanceData =
            cashAccounts.Count > 0 ||
            invoiceRows.Count > 0 ||
            billRows.Count > 0 ||
            assetRows.Count > 0;
        var summary = new FinanceSummaryDto(
            query.CompanyId,
            effectiveAsOfUtc,
            currentCash,
            accountsReceivable,
            overdueReceivables,
            accountsPayable,
            overduePayables,
            monthlyRevenue,
            monthlyCosts,
            currency,
            hasFinanceData,
            recentAssetPurchaseCount,
            recentAssetPurchaseTotalAmount,
            recentAssetPurchases);

        if (!query.IncludeConsistencyCheck)
        {
            return summary;
        }

        var consistencyCheck = await _consistencyChecker.CheckAsync(
            query.CompanyId,
            effectiveAsOfUtc,
            recentAssetPurchaseLimit,
            summary,
            cancellationToken);
        return summary with { ConsistencyCheck = consistencyCheck };
    }

    private async Task<decimal> CalculateCurrentCashAsync(
        Guid companyId,
        DateTime asOfUtc,
        IReadOnlyList<FinanceAccountRow> cashAccounts,
        CancellationToken cancellationToken)
    {
        if (cashAccounts.Count == 0)
        {
            return 0m;
        }

        var cashAccountIds = cashAccounts
            .Select(x => x.Id)
            .ToArray();

        var balances = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                cashAccountIds.Contains(x.AccountId) &&
                x.AsOfUtc <= asOfUtc)
            .Select(x => new BalanceRow(x.AccountId, x.AsOfUtc, x.Amount))
            .ToListAsync(cancellationToken);

        var latestBalanceByAccount = balances
            .GroupBy(x => x.AccountId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(balance => balance.AsOfUtc).First());

        var transactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                cashAccountIds.Contains(x.AccountId) &&
                x.TransactionUtc <= asOfUtc)
            .Select(x => new TransactionRow(x.AccountId, x.TransactionUtc, x.Amount))
            .ToListAsync(cancellationToken);

        var transactionsByAccount = transactions
            .GroupBy(x => x.AccountId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var currentCash = cashAccounts.Sum(account =>
        {
            var accountTransactions = transactionsByAccount.GetValueOrDefault(account.Id) ?? [];
            if (latestBalanceByAccount.TryGetValue(account.Id, out var snapshot))
            {
                var postedSinceSnapshot = accountTransactions
                    .Where(x => x.TransactionUtc > snapshot.AsOfUtc)
                    .Sum(x => x.Amount);
                return snapshot.Amount + postedSinceSnapshot;
            }

            return account.OpeningBalance + accountTransactions.Sum(x => x.Amount);
        });

        return Round(currentCash);
    }

    private async Task<Dictionary<Guid, decimal>> LoadInvoiceAllocationLookupAsync(
        Guid companyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= asOfUtc)
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(group => new AllocationRow(group.Key, group.Sum(x => x.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

    private async Task<Dictionary<Guid, decimal>> LoadBillAllocationLookupAsync(
        Guid companyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= asOfUtc)
            .GroupBy(x => x.BillId!.Value)
            .Select(group => new AllocationRow(group.Key, group.Sum(x => x.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

    private async Task<DateTime> ResolveAsOfUtcAsync(
        Guid companyId,
        DateTime? asOfUtc,
        CancellationToken cancellationToken)
    {
        var normalizedAsOfUtc = NormalizeUtc(asOfUtc);
        if (normalizedAsOfUtc.HasValue)
        {
            return normalizedAsOfUtc.Value;
        }

        var simulatedUtc = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => (DateTime?)x.CurrentSimulatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return simulatedUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId &&
            currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance summary reads are scoped to the active company context.");
        }
    }

    private static bool IsCashAccount(FinanceAccountRow account) =>
        string.Equals(account.AccountType, "cash", StringComparison.OrdinalIgnoreCase) ||
        account.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        account.Code.StartsWith("10", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncludedReceivable(string status, string settlementStatus)
    {
        if (string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("paid" or "cancelled" or "canceled" or "void" or "voided" or "written_off" or "rejected");
    }

    private static bool IsIncludedPayable(string status, string settlementStatus)
    {
        if (string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("paid" or "cancelled" or "canceled" or "void" or "voided");
    }

    private static bool IsOpenPayableAsset(AssetRow row) =>
        string.Equals(FinanceAssetFundingBehaviors.Normalize(row.FundingBehavior), FinanceAssetFundingBehaviors.Payable, StringComparison.Ordinal) &&
        !string.Equals(FinanceSettlementStatuses.Normalize(row.FundingSettlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) &&
        IsIncludedAssetCost(row.Status);

    private static bool IsIncludedInMonthlyRevenue(string status)
    {
        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("cancelled" or "canceled" or "void" or "voided" or "rejected");
    }

    private static bool IsIncludedInMonthlyCosts(string status)
    {
        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("cancelled" or "canceled" or "void" or "voided" or "rejected");
    }

    private static bool IsIncludedAssetCost(string status) =>
        string.Equals(NormalizeStatus(status), NormalizeStatus(FinanceAssetStatuses.Active), StringComparison.Ordinal);

    private static decimal CalculateRemainingBalance(decimal amount, decimal completedAllocatedAmount) =>
        Round(Math.Max(0m, amount - completedAllocatedAmount));

    private static decimal Round(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private static DateTime? NormalizeUtc(DateTime? value) =>
        !value.HasValue
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

    private static string ResolveCurrency(params IEnumerable<string>[] currencySets)
    {
        var currencies = currencySets
            .SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return currencies.Length switch
        {
            0 => "USD",
            1 => currencies[0],
            _ => "MIXED"
        };
    }

    private sealed record FinanceAccountRow(
        Guid Id,
        string Code,
        string Name,
        string AccountType,
        decimal OpeningBalance,
        string Currency);

    private sealed record BalanceRow(Guid AccountId, DateTime AsOfUtc, decimal Amount);
    private sealed record TransactionRow(Guid AccountId, DateTime TransactionUtc, decimal Amount);
    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
    private sealed record InvoiceRow(Guid Id, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, string SettlementStatus);
    private sealed record BillRow(Guid Id, DateTime ReceivedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, string SettlementStatus);
    private sealed record AssetRow(Guid Id, string ReferenceNumber, string Name, string Category, DateTime PurchasedUtc, decimal Amount, string Currency, string FundingBehavior, string FundingSettlementStatus, string Status);
}