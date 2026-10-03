using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceOperationalReportService(VirtualCompanyDbContext db, IFinanceReadService finance,
    TimeProvider time, ILogger<FinanceOperationalReportService> logger, ICompanyContextAccessor context) : IFinanceOperationalReportService
{
    public async Task<FinanceOperationalReportDto> GetAsync(GetFinanceOperationalReportQuery query, CancellationToken token)
    {
        if (query.CompanyId == Guid.Empty || context.CompanyId != query.CompanyId) throw new UnauthorizedAccessException();
        if (query.HorizonDays is < 1 or > 30) throw new ArgumentException("Choose a forecast horizon from 1 to 30 days.");
        var source = string.IsNullOrWhiteSpace(query.SourceFilter) ? "operational" : query.SourceFilter.Trim().ToLowerInvariant();
        if (source is not ("operational" or "fortnox")) throw new ArgumentException("Choose operational or Fortnox obligation evidence.");
        var sourcePolicy = new FinanceRecordSourcePolicy(db);
        var currency = string.IsNullOrWhiteSpace(query.Currency) ? null : query.Currency.Trim().ToUpperInvariant();
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetter))) throw new ArgumentException("Choose a three-letter currency.");
        var buckets = new[] { "current", "past_due_1_30", "past_due_31_60", "past_due_61_90", "past_due_over_90", "overdue" };
        var bucket = string.IsNullOrWhiteSpace(query.Bucket) ? null : query.Bucket;
        if (bucket is not null && !buckets.Contains(bucket)) throw new ArgumentException("Choose a supported aging bucket.");
        var exclusive = query.AsOfDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var through = query.AsOfDate.AddDays(query.HorizonDays);
        var invoices = await sourcePolicy.ApplyFilter(db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty), query.CompanyId, source, "invoice")
            .Where(x => x.CompanyId == query.CompanyId && x.Counterparty.CompanyId == query.CompanyId && x.IssuedUtc < exclusive &&
                x.SourceSimulationEventRecordId == null && x.Amount > 0 && x.PostingStatus == FinanceDocumentPostingStatuses.Booked &&
                x.DocumentKind == FinanceDocumentKinds.Invoice && x.SettlementStatus != FinanceSettlementStatuses.Paid &&
                x.Status != "cancelled" && (currency == null || x.Currency == currency))
            .OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(5001).ToListAsync(token);
        var bills = await sourcePolicy.ApplyFilter(db.FinanceBills.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty), query.CompanyId, source, "supplier_invoice")
            .Where(x => x.CompanyId == query.CompanyId && x.Counterparty.CompanyId == query.CompanyId && x.ReceivedUtc < exclusive &&
                x.SourceSimulationEventRecordId == null && x.Amount > 0 && x.PostingStatus == FinanceDocumentPostingStatuses.Booked &&
                x.DocumentKind == FinanceDocumentKinds.SupplierInvoice && x.SettlementStatus != FinanceSettlementStatuses.Paid && x.Status != "cancelled" &&
                (currency == null || x.Currency == currency))
            .OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(5001).ToListAsync(token);
        if (invoices.Count > 5000 || bills.Count > 5000) throw new ArgumentException("Too many obligations. Filter by currency before retrying.");
        FinanceObligationRowDto Row(Guid id, string kind, string number, string counterparty, DateTime due, decimal amount,
            decimal paid, string code, string status, DateTime updated)
        {
            var age = FinancialReportSuiteCalculator.AgingBucket(DateOnly.FromDateTime(due), query.AsOfDate);
            return new(id, kind, number, counterparty, DateTime.SpecifyKind(due, DateTimeKind.Utc), age.DaysPastDue, age.Bucket,
                FinancialReportSuiteCalculator.Round(Math.Max(0, amount - paid)), code, status, DateTime.SpecifyKind(updated, DateTimeKind.Utc),
                $"/finance/{(kind == "invoice" ? "invoices" : "supplier-bills")}/{id:D}?companyId={query.CompanyId:D}&financeSource={source}");
        }
        bool Include(FinanceObligationRowDto row) => row.RemainingAmount > 0 && (bucket is null || row.Bucket == bucket || bucket == "overdue" && row.DaysOverdue > 0);
        var receivables = invoices.Select(x => Row(x.Id, "invoice", x.InvoiceNumber, x.Counterparty.Name, x.DueUtc, x.Amount, x.PaidAmount, x.Currency, x.Status, x.UpdatedUtc)).Where(Include).ToArray();
        var payables = bills.Select(x => Row(x.Id, "bill", x.BillNumber, x.Counterparty.Name, x.DueUtc, x.Amount, x.PaidAmount, x.Currency, x.Status, x.UpdatedUtc)).Where(Include).ToArray();
        var gaps = new List<string>();
        var evidence = new List<FinanceCashEvidenceDto>();
        try
        {
            var cash = await finance.GetCashBalanceAsync(new(query.CompanyId, exclusive.AddTicks(-1)), token);
            var accountIds = cash.Accounts.Select(x => x.AccountId).ToArray();
            var accounts = await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId &&
                accountIds.Contains(x.Id) && x.OpenedUtc < exclusive).ToDictionaryAsync(x => x.Id, token);
            var snapshots = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId &&
                accountIds.Contains(x.AccountId) && x.AsOfUtc < exclusive).Select(x => new { x.AccountId, x.AsOfUtc }).ToListAsync(token);
            foreach (var row in cash.Accounts.Where(x => accounts.ContainsKey(x.AccountId) && (currency is null || x.Currency == currency)))
            {
                var observation = snapshots.Where(x => x.AccountId == row.AccountId).Select(x => (DateTime?)x.AsOfUtc).Max();
                evidence.Add(new(row.AccountId, row.AccountName, row.Amount, row.Currency,
                    DateTime.SpecifyKind(observation ?? accounts[row.AccountId].OpenedUtc, DateTimeKind.Utc),
                    observation.HasValue ? "Retained account balance plus posted cash movements through cutoff." : "Recorded opening balance plus posted cash movements through cutoff; no retained balance snapshot."));
                if (!observation.HasValue) gaps.Add($"{row.AccountName}: no retained balance snapshot; opening evidence is used.");
                else if (observation.Value < exclusive.AddDays(-1)) gaps.Add($"{row.AccountName}: retained balance is older than the selected cutoff day; this is not live bank confirmation.");
            }
            if (evidence.Count == 0) gaps.Add("No retained cash account evidence is available for this scope.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
        {
            logger.LogWarning("Operational cash evidence unavailable for {CompanyId}: {ErrorType}", query.CompanyId, ex.GetType().Name);
            gaps.Add("Cash evidence is unavailable. Obligation balances remain visible; retry or review cash sources.");
        }
        var codes = receivables.Select(x => x.Currency).Concat(payables.Select(x => x.Currency)).Concat(evidence.Select(x => x.Currency))
            .Concat(currency is null ? [] : new[] { currency }).Distinct().Order().ToArray();
        var totals = codes.Select(code =>
        {
            var incoming = receivables.Where(x => x.Currency == code).ToArray(); var outgoing = payables.Where(x => x.Currency == code).ToArray();
            var cashRows = evidence.Where(x => x.Currency == code).ToArray(); decimal? cash = cashRows.Length == 0 ? null : cashRows.Sum(x => x.Amount);
            // Overdue balances are assumed collected/paid within the horizon; dates beyond the horizon are excluded.
            var inflow = incoming.Where(x => DateOnly.FromDateTime(x.DueUtc) <= through).Sum(x => x.RemainingAmount);
            var outflow = outgoing.Where(x => DateOnly.FromDateTime(x.DueUtc) <= through).Sum(x => x.RemainingAmount);
            return new FinanceOperationalCurrencyDto(code, incoming.Sum(x => x.RemainingAmount), incoming.Where(x => x.DaysOverdue > 0).Sum(x => x.RemainingAmount),
                outgoing.Sum(x => x.RemainingAmount), outgoing.Where(x => x.DaysOverdue > 0).Sum(x => x.RemainingAmount), cash, inflow, outflow, cash + inflow - outflow);
        }).ToArray();
        return new(query.CompanyId, query.AsOfDate, time.GetUtcNow().UtcDateTime, through, "UTC", "finance-operational/1.0",
            $"Obligation source: {source}. Current recorded positive booked operational obligations issued/received before the end of the UTC cutoff day, less current retained paid amounts. Excludes simulation, paid, cancelled and credit documents. Not a reconstructed historical ledger; later settlements can change past-cutoff results.",
            "Starting cash is the owning Finance cash account evidence at cutoff. All included open balances due on or before the horizon, including overdue balances, are assumed collected or paid in full. No probability, new sales, recurring unrecorded costs, payment requests or FX conversion. Bucket and currency filters also constrain forecast flows.",
            currency, bucket, receivables, payables, totals, evidence, gaps,
            await db.BankTransactions.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == query.CompanyId &&
                x.BookingDate < exclusive && x.Status != BankTransactionReconciliationStatuses.Reconciled, token));
    }
}
