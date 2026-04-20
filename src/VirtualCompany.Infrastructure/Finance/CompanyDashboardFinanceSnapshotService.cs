using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyDashboardFinanceSnapshotService : IDashboardFinanceSnapshotService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public CompanyDashboardFinanceSnapshotService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardFinanceSnapshotDto> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        var asOfUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expenseWindowStartUtc = asOfUtc.Date.AddDays(-29);

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.AccountType,
                x.Currency,
                x.OpeningBalance
            })
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return Missing(companyId, asOfUtc);
        }

        var transactionSums = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc <= asOfUtc)
            .GroupBy(x => x.AccountId)
            .Select(group => new { AccountId = group.Key, Amount = group.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Amount, cancellationToken);

        var latestBalances = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AsOfUtc <= asOfUtc)
            .GroupBy(x => x.AccountId)
            .Select(group => group
                .OrderByDescending(x => x.AsOfUtc)
                .Select(x => new
                {
                    x.AccountId,
                    x.Amount,
                    x.Currency
                })
                .First())
            .ToDictionaryAsync(x => x.AccountId, x => x.Amount, cancellationToken);

        var cashAccounts = accounts
            .Where(x =>
                x.AccountType.Equals("cash", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
                x.Code.StartsWith("10", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cashAccounts.Count == 0)
        {
            cashAccounts = accounts.Where(x => x.AccountType.Equals("asset", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var cash = cashAccounts.Sum(x =>
            latestBalances.TryGetValue(x.Id, out var latestBalance)
                ? latestBalance
                : x.OpeningBalance + transactionSums.GetValueOrDefault(x.Id));
        var currency = cashAccounts.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? cashAccounts[0].Currency
            : "USD";

        var expenseAmounts = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc >= expenseWindowStartUtc && x.TransactionUtc <= asOfUtc && x.Amount < 0)
            .Select(x => Math.Abs(x.Amount))
            .ToListAsync(cancellationToken);

        var totalExpenses = expenseAmounts.Sum();
        var hasExpenseData = expenseAmounts.Count > 0;
        var burnRate = Math.Round(totalExpenses / 30m, 2, MidpointRounding.AwayFromZero);
        var runwayDays = burnRate > 0m ? (int?)Math.Max(0, (int)Math.Floor(cash / burnRate)) : null;
        var hasCashData = cashAccounts.Count > 0;
        var hasFinanceData = hasCashData && hasExpenseData;
        var riskLevel = !hasFinanceData
            ? "missing"
            : runwayDays <= 30 ? "critical"
            : runwayDays <= 90 ? "warning"
            : "healthy";

        return new DashboardFinanceSnapshotDto(companyId, Math.Round(cash, 2, MidpointRounding.AwayFromZero), burnRate, runwayDays, riskLevel, hasFinanceData, currency, asOfUtc);
    }

    private static DashboardFinanceSnapshotDto Missing(Guid companyId, DateTime asOfUtc) =>
        new(companyId, 0m, 0m, null, "missing", false, "USD", asOfUtc);
}