using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyDashboardFinanceSnapshotService : IDashboardFinanceSnapshotService
{
    private const int DefaultUpcomingWindowDays = 30;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public CompanyDashboardFinanceSnapshotService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<DashboardFinanceSnapshotDto> GetAsync(
        Guid companyId,
        DateTime? asOfUtc = null,
        int upcomingWindowDays = DefaultUpcomingWindowDays,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(companyId);

        var effectiveAsOfUtc = NormalizeUtc(asOfUtc) ?? _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedUpcomingWindowDays = Math.Clamp(
            upcomingWindowDays <= 0 ? DefaultUpcomingWindowDays : upcomingWindowDays,
            1,
            90);
        var upcomingWindowEndUtc = effectiveAsOfUtc.Date.AddDays(normalizedUpcomingWindowDays + 1);
        var expenseWindowStartUtc = effectiveAsOfUtc.Date.AddDays(-29);

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new FinanceAccountSnapshotRow(
                x.Id,
                x.Code,
                x.Name,
                x.AccountType,
                x.Currency))
            .ToListAsync(cancellationToken);

        var cashAccounts = accounts
            .Where(IsCashAccount)
            .ToList();
        var cashAccountIds = cashAccounts
            .Select(x => x.Id)
            .ToArray();

        var currentCashBalance = cashAccountIds.Length == 0
            ? 0m
            : Math.Round(
                await _dbContext.LedgerEntryLines
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.CompanyId == companyId &&
                        cashAccountIds.Contains(x.FinanceAccountId) &&
                        x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                        (x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) <= effectiveAsOfUtc)
                    .SumAsync(x => (decimal?)(x.DebitAmount - x.CreditAmount), cancellationToken) ?? 0m,
                2,
                MidpointRounding.AwayFromZero);

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new ReceivableSnapshotRow(
                x.Id,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new PayableSnapshotRow(
                x.Id,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var completedIncomingByInvoice = await LoadInvoiceAllocationLookupAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Incoming,
            paymentDateFromUtc: null,
            paymentDateToExclusiveUtc: effectiveAsOfUtc.AddTicks(1),
            cancellationToken);
        var scheduledIncomingByInvoice = await LoadInvoiceAllocationLookupAsync(
            companyId,
            PaymentStatuses.Pending,
            PaymentTypes.Incoming,
            paymentDateFromUtc: effectiveAsOfUtc,
            paymentDateToExclusiveUtc: upcomingWindowEndUtc,
            cancellationToken);
        var completedOutgoingByBill = await LoadBillAllocationLookupAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Outgoing,
            paymentDateFromUtc: null,
            paymentDateToExclusiveUtc: effectiveAsOfUtc.AddTicks(1),
            cancellationToken);
        var scheduledOutgoingByBill = await LoadBillAllocationLookupAsync(
            companyId,
            PaymentStatuses.Pending,
            PaymentTypes.Outgoing,
            paymentDateFromUtc: effectiveAsOfUtc,
            paymentDateToExclusiveUtc: upcomingWindowEndUtc,
            cancellationToken);

        var openReceivables = invoiceRows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus))
            .Select(x => new OpenDocumentSnapshotRow(
                x.Id,
                x.DueUtc,
                CalculateRemainingBalance(x.Amount, completedIncomingByInvoice.GetValueOrDefault(x.Id)),
                x.Currency))
            .Where(x => x.RemainingBalance > 0m)
            .ToList();

        var openPayables = billRows
            .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
            .Select(x => new OpenDocumentSnapshotRow(
                x.Id,
                x.DueUtc,
                CalculateRemainingBalance(x.Amount, completedOutgoingByBill.GetValueOrDefault(x.Id)),
                x.Currency))
            .Where(x => x.RemainingBalance > 0m)
            .ToList();

        // Query rules:
        // - completed allocations reduce the open document balance as of the selected date.
        // - pending scheduled allocations inside the upcoming window replace the unscheduled balance for expected cash metrics.
        // - when no schedule exists, the remaining open balance is forecast only when the due date falls inside the same window.
        // This prevents double counting a pending installment and the full document remainder at the same time.
        var expectedIncomingCash = Math.Round(openReceivables.Sum(receivable =>
        {
            var scheduledAmount = Math.Min(
                receivable.RemainingBalance,
                scheduledIncomingByInvoice.GetValueOrDefault(receivable.DocumentId));

            if (scheduledAmount > 0m)
            {
                return scheduledAmount;
            }

            return receivable.DueUtc < upcomingWindowEndUtc
                ? receivable.RemainingBalance
                : 0m;
        }), 2, MidpointRounding.AwayFromZero);

        var expectedOutgoingCash = Math.Round(openPayables.Sum(payable =>
        {
            var scheduledAmount = Math.Min(
                payable.RemainingBalance,
                scheduledOutgoingByBill.GetValueOrDefault(payable.DocumentId));

            if (scheduledAmount > 0m)
            {
                return scheduledAmount;
            }

            return payable.DueUtc < upcomingWindowEndUtc
                ? payable.RemainingBalance
                : 0m;
        }), 2, MidpointRounding.AwayFromZero);

        var overdueReceivables = Math.Round(
            openReceivables
                .Where(x => x.DueUtc < effectiveAsOfUtc)
                .Sum(x => x.RemainingBalance),
            2,
            MidpointRounding.AwayFromZero);

        var upcomingPayables = Math.Round(
            openPayables
                .Where(x => x.DueUtc >= effectiveAsOfUtc && x.DueUtc < upcomingWindowEndUtc)
                .Sum(x => x.RemainingBalance),
            2,
            MidpointRounding.AwayFromZero);

        var hasFinanceData =
            cashAccounts.Count > 0 ||
            openReceivables.Count > 0 ||
            openPayables.Count > 0;
        var currency = ResolveCurrency(cashAccounts, openReceivables, openPayables);

        var expenseAmounts = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc >= expenseWindowStartUtc && x.TransactionUtc <= effectiveAsOfUtc && x.Amount < 0)
            .Select(x => Math.Abs(x.Amount))
            .ToListAsync(cancellationToken);

        var totalExpenses = expenseAmounts.Sum();
        var burnRate = Math.Round(totalExpenses / 30m, 2, MidpointRounding.AwayFromZero);
        var runwayDays = burnRate > 0m
            ? (int?)Math.Max(0, (int)Math.Floor(currentCashBalance / burnRate))
            : null;
        var riskLevel = !hasFinanceData
            ? "missing"
            : runwayDays <= 30 ? "critical"
            : runwayDays <= 90 ? "warning"
            : "healthy";

        return new DashboardFinanceSnapshotDto(
            companyId,
            currentCashBalance,
            expectedIncomingCash,
            expectedOutgoingCash,
            overdueReceivables,
            upcomingPayables,
            currency,
            effectiveAsOfUtc,
            normalizedUpcomingWindowDays,
            currentCashBalance,
            burnRate,
            runwayDays,
            riskLevel,
            hasFinanceData);
    }

    private async Task<Dictionary<Guid, decimal>> LoadInvoiceAllocationLookupAsync(
        Guid companyId,
        string paymentStatus,
        string paymentType,
        DateTime? paymentDateFromUtc,
        DateTime? paymentDateToExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var allocations = _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType);

        if (paymentDateFromUtc.HasValue)
        {
            allocations = allocations.Where(x => x.Payment.PaymentDate >= paymentDateFromUtc.Value);
        }

        if (paymentDateToExclusiveUtc.HasValue)
        {
            allocations = allocations.Where(x => x.Payment.PaymentDate < paymentDateToExclusiveUtc.Value);
        }

        return await allocations
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(group => new AllocationLookupRow(group.Key, group.Sum(x => x.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);
    }

    private async Task<Dictionary<Guid, decimal>> LoadBillAllocationLookupAsync(
        Guid companyId,
        string paymentStatus,
        string paymentType,
        DateTime? paymentDateFromUtc,
        DateTime? paymentDateToExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var allocations = _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType);

        if (paymentDateFromUtc.HasValue)
        {
            allocations = allocations.Where(x => x.Payment.PaymentDate >= paymentDateFromUtc.Value);
        }

        if (paymentDateToExclusiveUtc.HasValue)
        {
            allocations = allocations.Where(x => x.Payment.PaymentDate < paymentDateToExclusiveUtc.Value);
        }

        return await allocations
            .GroupBy(x => x.BillId!.Value)
            .Select(group => new AllocationLookupRow(group.Key, group.Sum(x => x.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid scopedCompanyId &&
            scopedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance dashboard metrics are scoped to the active company context.");
        }
    }

    private static string ResolveCurrency(
        IReadOnlyCollection<FinanceAccountSnapshotRow> cashAccounts,
        IReadOnlyCollection<OpenDocumentSnapshotRow> receivables,
        IReadOnlyCollection<OpenDocumentSnapshotRow> payables)
    {
        var currencies = cashAccounts
            .Select(x => x.Currency)
            .Concat(receivables.Select(x => x.Currency))
            .Concat(payables.Select(x => x.Currency))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return currencies.Count switch
        {
            0 => "USD",
            1 => currencies[0],
            _ => "MIXED"
        };
    }

    private static bool IsCashAccount(FinanceAccountSnapshotRow account) =>
        account.AccountType.Equals("cash", StringComparison.OrdinalIgnoreCase) ||
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

    private static decimal CalculateRemainingBalance(decimal amount, decimal completedAllocatedAmount) =>
        Math.Round(Math.Max(0m, amount - completedAllocatedAmount), 2, MidpointRounding.AwayFromZero);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

    private static string NormalizeStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    private sealed record FinanceAccountSnapshotRow(
        Guid Id,
        string Code,
        string Name,
        string AccountType,
        string Currency);

    private sealed record ReceivableSnapshotRow(
        Guid Id,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record PayableSnapshotRow(
        Guid Id,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record OpenDocumentSnapshotRow(
        Guid DocumentId,
        DateTime DueUtc,
        decimal RemainingBalance,
        string Currency);

    private sealed record AllocationLookupRow(
        Guid DocumentId,
        decimal Amount);
}