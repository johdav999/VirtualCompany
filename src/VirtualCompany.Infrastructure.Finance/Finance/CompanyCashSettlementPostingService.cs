using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyCashSettlementPostingService : IFinanceCashSettlementPostingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public CompanyCashSettlementPostingService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null)
    {
    }

    public CompanyCashSettlementPostingService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<CashSettlementPostingResultDto> PostCashSettlementAsync(
        PostCashSettlementCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        if (command.PaymentId == Guid.Empty)
        {
            throw CreateValidationException(nameof(command.PaymentId), "Payment id is required.");
        }

        var normalizedSourceType = FinanceCashPostingSourceTypes.Normalize(command.SourceType);
        var normalizedSourceId = NormalizeSourceId(command.SourceId);
        var settledAmount = NormalizeMoney(command.SettledAmount);
        var postedAtUtc = NormalizeUtc(command.SettledAtUtc);

        if (string.Equals(normalizedSourceType, FinanceCashPostingSourceTypes.BankTransaction, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureMatchedBankTransactionSourceAsync(command.CompanyId, normalizedSourceId, cancellationToken);
        }

        var existingEntry = await FindExistingAsync(command.CompanyId, normalizedSourceType, normalizedSourceId, postedAtUtc, cancellationToken);
        if (existingEntry is not null)
        {
            await TryEnsurePaymentCashLedgerLinkAsync(
                command.CompanyId,
                command.PaymentId,
                existingEntry.Id,
                normalizedSourceType,
                normalizedSourceId,
                postedAtUtc,
                cancellationToken);
            return await MapExistingAsync(existingEntry, normalizedSourceType, normalizedSourceId, postedAtUtc, cancellationToken);
        }

        var paymentExists = await _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.PaymentId, cancellationToken);
        var payment = paymentExists
            ? await _dbContext.Payments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.CompanyId == command.CompanyId && x.Id == command.PaymentId,
                    cancellationToken)
            : null;

        if (payment is null)
        {
            throw new KeyNotFoundException("Finance payment was not found.");
        }

        if (!string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            throw CreateValidationException(nameof(command.PaymentId), "Only completed payments can be posted to the ledger.");
        }

        if (settledAmount > payment.Amount)
        {
            throw CreateValidationException(nameof(command.SettledAmount), "Settled amount cannot exceed the payment amount.");
        }

        var fiscalPeriod = await EnsureFiscalPeriodAsync(command.CompanyId, postedAtUtc, cancellationToken);
        var cashAccount = await ResolveCashAccountAsync(command.CompanyId, payment.Currency, cancellationToken);
        var settlementAccount = await ResolveSettlementAccountAsync(
            command.CompanyId,
            payment.PaymentType,
            cashAccount.Id,
            cancellationToken);

        var entry = new LedgerEntry(
            Guid.NewGuid(),
            command.CompanyId,
            fiscalPeriod.Id,
            BuildEntryNumber(normalizedSourceType, normalizedSourceId, postedAtUtc),
            postedAtUtc,
            LedgerEntryStatuses.Posted,
            BuildDescription(payment),
            normalizedSourceType,
            normalizedSourceId,
            postedAtUtc,
            postedAtUtc,
            postedAtUtc);

        _dbContext.LedgerEntries.Add(entry);
        _dbContext.LedgerEntryLines.AddRange(
            BuildLines(
                command.CompanyId,
                entry.Id,
                cashAccount.Id,
                settlementAccount.Id,
                payment.PaymentType,
                settledAmount,
                payment.Currency,
                entry.Description));

        _dbContext.LedgerEntrySourceMappings.Add(new LedgerEntrySourceMapping(
            entry.Id,
            command.CompanyId,
            entry.Id,
            normalizedSourceType,
            normalizedSourceId,
            postedAtUtc,
            postedAtUtc));

        _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(
            Guid.NewGuid(),
            command.CompanyId,
            payment.Id,
            entry.Id,
            normalizedSourceType,
            normalizedSourceId,
            postedAtUtc,
            postedAtUtc));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new CashSettlementPostingResultDto(
                command.CompanyId,
                entry.Id,
                normalizedSourceType,
                normalizedSourceId,
                settledAmount,
                postedAtUtc,
                true);
        }
        catch (DbUpdateException ex) when (IsDuplicatePostingViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();

            existingEntry = await FindExistingAsync(command.CompanyId, normalizedSourceType, normalizedSourceId, postedAtUtc, cancellationToken);
            if (existingEntry is not null)
            {
                return await MapExistingAsync(existingEntry, normalizedSourceType, normalizedSourceId, postedAtUtc, cancellationToken);
            }

            throw;
        }
    }

    private async Task<LedgerEntry?> FindExistingAsync(
        Guid companyId,
        string normalizedSourceType,
        string normalizedSourceId,
        DateTime postedAtUtc,
        CancellationToken cancellationToken) =>
        (await _dbContext.LedgerEntrySourceMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.LedgerEntry)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.SourceType == normalizedSourceType &&
                     x.SourceId == normalizedSourceId &&
                     x.PostedAtUtc == postedAtUtc,
                cancellationToken))?.LedgerEntry
        ?? await _dbContext.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.SourceType == normalizedSourceType &&
                     x.SourceId == normalizedSourceId &&
                     x.PostedAtUtc == postedAtUtc,
                cancellationToken);

    private async Task<CashSettlementPostingResultDto> MapExistingAsync(
        LedgerEntry entry,
        string normalizedSourceType,
        string normalizedSourceId,
        DateTime postedAtUtc,
        CancellationToken cancellationToken)
    {
        var mapping = await _dbContext.LedgerEntrySourceMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == entry.CompanyId &&
                     x.LedgerEntryId == entry.Id &&
                     x.SourceType == normalizedSourceType &&
                     x.SourceId == normalizedSourceId &&
                     x.PostedAtUtc == postedAtUtc,
                cancellationToken);
        var postedAmount = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == entry.CompanyId && x.LedgerEntryId == entry.Id)
            .SumAsync(x => (decimal?)x.DebitAmount, cancellationToken) ?? 0m;

        var sourceType = mapping?.SourceType ?? entry.SourceType ?? normalizedSourceType;
        var sourceId = mapping?.SourceId ?? entry.SourceId ?? normalizedSourceId;
        var effectivePostedAtUtc = mapping?.PostedAtUtc ?? entry.PostedAtUtc ?? postedAtUtc;
        return new CashSettlementPostingResultDto(
            entry.CompanyId,
            entry.Id,
            sourceType,
            sourceId,
            postedAmount,
            effectivePostedAtUtc,
            false);
    }

    private async Task<FiscalPeriod> EnsureFiscalPeriodAsync(
        Guid companyId,
        DateTime postingUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.StartUtc <= postingUtc &&
                     x.EndUtc > postingUtc,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var startUtc = new DateTime(postingUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddYears(1);
        var period = new FiscalPeriod(
            Guid.NewGuid(),
            companyId,
            $"FY {startUtc.Year}",
            startUtc,
            endUtc,
            false,
            null,
            false,
            null,
            null,
            null,
            null,
            postingUtc,
            postingUtc);

        _dbContext.FiscalPeriods.Add(period);
        return period;
    }

    private async Task<FinanceAccount> ResolveCashAccountAsync(
        Guid companyId,
        string currency,
        CancellationToken cancellationToken)
    {
        var linkedCashAccount = await _dbContext.CompanyBankAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.FinanceAccount)
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Currency == currency)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.CreatedUtc)
            .Select(x => x.FinanceAccount)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkedCashAccount is not null)
        {
            return linkedCashAccount;
        }

        var fallbackCashAccount = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Code == "1000")
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return fallbackCashAccount ?? throw new InvalidOperationException("Cash settlement posting requires a configured cash account.");
    }

    private async Task EnsureMatchedBankTransactionSourceAsync(
        Guid companyId,
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sourceId, out var bankTransactionId))
        {
            throw CreateValidationException(nameof(PostCashSettlementCommand.SourceId), "Bank transaction posting sources must use the bank transaction id.");
        }

        var state = await _dbContext.BankTransactionPostingStateRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId,
                cancellationToken);

        if (state is not null &&
            !BankTransactionMatchingStatuses.AllowsSettlementPosting(state.MatchingStatus))
        {
            throw CreateValidationException(nameof(PostCashSettlementCommand.SourceId), "Unmatched bank transactions cannot create AR/AP settlement journal entries.");
        }

        if (state is null)
        {
            var hasMatch = await _dbContext.BankTransactionPaymentLinks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId,
                    cancellationToken);

            if (!hasMatch)
            {
                throw CreateValidationException(nameof(PostCashSettlementCommand.SourceId), "Unmatched bank transactions cannot create AR/AP settlement journal entries.");
            }
        }
    }

    private async Task TryEnsurePaymentCashLedgerLinkAsync(
        Guid companyId,
        Guid paymentId,
        Guid ledgerEntryId,
        string sourceType,
        string sourceId,
        DateTime postedAtUtc,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.PaymentCashLedgerLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == companyId && x.PaymentId == paymentId && x.LedgerEntryId == ledgerEntryId,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(
            Guid.NewGuid(),
            companyId,
            paymentId,
            ledgerEntryId,
            sourceType,
            sourceId,
            postedAtUtc,
            postedAtUtc));
    }

    private async Task<FinanceAccount> ResolveSettlementAccountAsync(
        Guid companyId,
        string paymentType,
        Guid cashAccountId,
        CancellationToken cancellationToken)
    {
        var preferredCode = string.Equals(paymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? "1100"
            : "2000";
        var fallbackType = string.Equals(paymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? "asset"
            : "liability";

        var baseQuery = _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id != cashAccountId);

        var preferred = await baseQuery
            .Where(x => x.Code == preferredCode)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (preferred is not null)
        {
            return preferred;
        }

        var fallback = await baseQuery
            .Where(x => x.AccountType == fallbackType)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return fallback ?? throw new InvalidOperationException("Cash settlement posting requires a configured receivable or payable account.");
    }

    private static IReadOnlyList<LedgerEntryLine> BuildLines(
        Guid companyId,
        Guid ledgerEntryId,
        Guid cashAccountId,
        Guid settlementAccountId,
        string paymentType,
        decimal amount,
        string currency,
        string? description)
    {
        if (string.Equals(paymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, cashAccountId, amount, 0m, currency, null, description),
                new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, settlementAccountId, 0m, amount, currency, null, description)
            ];
        }

        return
        [
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, settlementAccountId, amount, 0m, currency, null, description),
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, cashAccountId, 0m, amount, currency, null, description)
        ];
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Cash settlement posting is scoped to the active company context.");
        }
    }

    private static string NormalizeSourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Source id is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Source id must be 128 characters or fewer.");
        }

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

    private static decimal NormalizeMoney(decimal value)
    {
        if (value <= 0m)
        {
            throw CreateValidationException(nameof(PostCashSettlementCommand.SettledAmount), "Settled amount must be greater than zero.");
        }

        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildEntryNumber(string sourceType, string sourceId, DateTime postedAtUtc)
    {
        var compactSourceId = sourceId.Replace("-", string.Empty, StringComparison.Ordinal);
        compactSourceId = compactSourceId[..Math.Min(12, compactSourceId.Length)];
        var compactSourceType = sourceType.ToUpperInvariant()[..Math.Min(8, sourceType.Length)];
        return $"CSP-{postedAtUtc:yyyyMMddHHmmssfffffff}-{compactSourceType}-{compactSourceId}";
    }

    private static string BuildDescription(Payment payment) =>
        string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? $"Customer cash settlement for {payment.CounterpartyReference}"
            : $"Supplier cash settlement for {payment.CounterpartyReference}";

    private static bool IsDuplicatePostingViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("IX_ledger_entries_company_id_source_type_source_id_posted_at", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique", StringComparison.OrdinalIgnoreCase) && message.Contains("ledger_entries", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("payment_cash_ledger_links", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_payment_cash_ledger_links_company_id_payment_id_ledger_entry_id", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique", StringComparison.OrdinalIgnoreCase) && message.Contains("ledger_entry_source_mappings", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static FinanceValidationException CreateValidationException(string field, string message) =>
        new(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [field] = [message]
            },
            message);
}
