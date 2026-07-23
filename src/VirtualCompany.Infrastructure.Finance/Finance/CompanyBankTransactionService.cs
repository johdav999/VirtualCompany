using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyBankTransactionService : IBankTransactionReadService, IBankTransactionCommandService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public CompanyBankTransactionService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null)
    {
    }

    public CompanyBankTransactionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<IReadOnlyList<BankTransactionDto>> ListAsync(
        ListBankTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var normalizedStatus = NormalizeOptionalStatus(query.Status);
        var limit = NormalizeLimit(query.Limit);
        var rows = _dbContext.BankTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (query.BankAccountId.HasValue && query.BankAccountId.Value != Guid.Empty)
        {
            rows = rows.Where(x => x.BankAccountId == query.BankAccountId.Value);
        }

        if (query.BookingDateFromUtc.HasValue)
        {
            var fromUtc = NormalizeUtc(query.BookingDateFromUtc.Value);
            rows = rows.Where(x => x.BookingDate >= fromUtc);
        }

        if (query.BookingDateToUtc.HasValue)
        {
            var toUtc = NormalizeUtc(query.BookingDateToUtc.Value);
            rows = rows.Where(x => x.BookingDate <= toUtc);
        }

        if (normalizedStatus is not null)
        {
            rows = rows.Where(x => x.Status == normalizedStatus);
        }

        if (query.MinAmount.HasValue)
        {
            rows = rows.Where(x => x.Amount >= query.MinAmount.Value);
        }

        if (query.MaxAmount.HasValue)
        {
            rows = rows.Where(x => x.Amount <= query.MaxAmount.Value);
        }

        return await rows
            .OrderByDescending(x => x.BookingDate)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(limit)
            .Select(x => new BankTransactionDto(
                x.Id,
                x.CompanyId,
                x.BankAccountId,
                x.BankAccount.DisplayName,
                x.BankAccount.BankName,
                x.BankAccount.MaskedAccountNumber,
                x.BookingDate,
                x.ValueDate,
                x.Amount,
                x.Currency,
                x.ReferenceText,
                x.Counterparty,
                x.Status,
                x.ReconciledAmount,
                x.ExternalReference,
                MapBankAccount(x.BankAccount)))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankTransactionDetailDto?> GetDetailAsync(
        GetBankTransactionDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.BankTransactionId == Guid.Empty)
        {
            throw new ArgumentException("Bank transaction id is required.", nameof(query));
        }

        var transaction = await _dbContext.BankTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.BankAccount)
            .ThenInclude(x => x.FinanceAccount)
            .Include(x => x.CashLedgerLinks)
            .SingleOrDefaultAsync(
                x => x.CompanyId == query.CompanyId && x.Id == query.BankTransactionId,
                cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        var linkedPayments = await _dbContext.BankTransactionPaymentLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BankTransactionId == query.BankTransactionId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Select(x => new BankTransactionPaymentLinkDto(
                x.Id,
                x.PaymentId,
                x.Payment.PaymentType,
                x.Payment.PaymentDate,
                x.Payment.CounterpartyReference,
                x.AllocatedAmount,
                x.Currency,
                x.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new BankTransactionDetailDto(
            transaction.Id,
            transaction.CompanyId,
            transaction.BankAccountId,
            transaction.BankAccount.DisplayName,
            transaction.BankAccount.BankName,
            transaction.BankAccount.MaskedAccountNumber,
            transaction.BookingDate,
            transaction.ValueDate,
            transaction.Amount,
            transaction.Currency,
            transaction.ReferenceText,
            transaction.Counterparty,
            transaction.Status,
            transaction.ReconciledAmount,
            transaction.ExternalReference,
            transaction.CashLedgerLinks
                .OrderByDescending(link => link.CreatedUtc)
                .Select(link => (Guid?)link.LedgerEntryId)
                .FirstOrDefault(),
            linkedPayments,
            MapBankAccount(transaction.BankAccount));
    }

    public Task<BankTransactionDetailDto> ReconcileAsync(
        ReconcileBankTransactionCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(
            () => ReconcileWithinAmbientTransactionAsync(command, cancellationToken),
            cancellationToken);

    internal Task<BankTransactionDetailDto> ReconcileWithinAmbientTransactionAsync(
        ReconcileBankTransactionCommand command,
        CancellationToken cancellationToken) =>
        ReconcileCoreAsync(command, cancellationToken);

    private async Task<BankTransactionDetailDto> ReconcileCoreAsync(
        ReconcileBankTransactionCommand command,
        CancellationToken cancellationToken)
    {
            EnsureTenant(command.CompanyId);

            if (command.BankTransactionId == Guid.Empty)
            {
                throw CreateValidationException(nameof(command.BankTransactionId), "Bank transaction id is required.");
            }

            if (command.Payments is not { Count: > 0 })
            {
                throw CreateValidationException(nameof(command.Payments), "At least one payment match is required.");
            }

            var duplicatePaymentId = command.Payments
                .GroupBy(x => x.PaymentId)
                .Where(x => x.Key != Guid.Empty && x.Count() > 1)
                .Select(x => x.Key)
                .FirstOrDefault();
            if (duplicatePaymentId != Guid.Empty)
            {
                throw CreateValidationException(nameof(command.Payments), "Each payment can only appear once in a reconciliation request.");
            }

            var transaction = await _dbContext.BankTransactions
                .IgnoreQueryFilters()
                .Include(x => x.BankAccount)
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BankTransactionId, cancellationToken);
            if (transaction is null)
            {
                throw new KeyNotFoundException("Bank transaction was not found.");
            }

            var existingLinks = await _dbContext.BankTransactionPaymentLinks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId && x.BankTransactionId == command.BankTransactionId)
                .ToListAsync(cancellationToken);

            var requestedPaymentIds = command.Payments.Select(x => x.PaymentId).Distinct().ToArray();
            var payments = await _dbContext.Payments
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId && requestedPaymentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            if (payments.Count != requestedPaymentIds.Length)
            {
                throw new KeyNotFoundException("One or more finance payments were not found.");
            }

            var existingTotal = existingLinks.Sum(x => x.AllocatedAmount);
            var newTotal = 0m;

            foreach (var match in command.Payments)
            {
                if (match.PaymentId == Guid.Empty)
                {
                    throw CreateValidationException(nameof(match.PaymentId), "Payment id is required.");
                }

                if (!payments.TryGetValue(match.PaymentId, out var payment))
                {
                    throw new KeyNotFoundException("One or more finance payments were not found.");
                }

                var normalizedAllocatedAmount = NormalizeMoney(match.AllocatedAmount);
                if (normalizedAllocatedAmount <= 0m)
                {
                    throw CreateValidationException(nameof(match.AllocatedAmount), "Allocated amount must be greater than zero.");
                }

                if (normalizedAllocatedAmount > payment.Amount)
                {
                    throw CreateValidationException(nameof(match.AllocatedAmount), "Allocated amount cannot exceed the payment amount.");
                }

                if (!string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateValidationException(nameof(match.PaymentId), "Only completed payments can be linked during bank reconciliation.");
                }

                if (!string.Equals(payment.Currency, transaction.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateValidationException(nameof(match.AllocatedAmount), "Payment currency must match the bank transaction currency.");
                }

                var expectedPaymentType = transaction.Amount > 0m ? PaymentTypes.Incoming : PaymentTypes.Outgoing;
                if (!string.Equals(payment.PaymentType, expectedPaymentType, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateValidationException(nameof(match.PaymentId), $"Payment type '{payment.PaymentType}' does not match the bank transaction direction.");
                }

                var existingLink = existingLinks.SingleOrDefault(x => x.PaymentId == match.PaymentId);
                if (existingLink is not null)
                {
                    if (existingLink.AllocatedAmount != normalizedAllocatedAmount)
                    {
                        throw CreateValidationException(nameof(match.AllocatedAmount), "Existing reconciled payment links are immutable. Repeat the same allocation amount to retry idempotently.");
                    }

                    continue;
                }

                newTotal += normalizedAllocatedAmount;
                if (NormalizeMoney(existingTotal + newTotal) > transaction.AbsoluteAmount)
                {
                    throw CreateValidationException(nameof(match.AllocatedAmount), "Bank transaction reconciliation cannot exceed the transaction amount.");
                }

                _dbContext.BankTransactionPaymentLinks.Add(new BankTransactionPaymentLink(
                    Guid.NewGuid(),
                    command.CompanyId,
                    transaction.Id,
                    payment.Id,
                    normalizedAllocatedAmount,
                    transaction.Currency,
                    transaction.BookingDate));
            }

            transaction.ApplyReconciliation(existingTotal + newTotal, DateTime.UtcNow);

            var idempotencyKey = BuildIdempotencyKey(command.CompanyId, transaction.Id);
            var existingLedgerLink = await _dbContext.BankTransactionCashLedgerLinks
                .IgnoreQueryFilters()
                .AsTracking()
                .SingleOrDefaultAsync(
                    x => x.CompanyId == command.CompanyId &&
                         (x.BankTransactionId == transaction.Id || x.IdempotencyKey == idempotencyKey),
                    cancellationToken);

            // Ledger posting is created on the first successful reconciliation and reused on retries or later partial-to-full updates.
            Guid? ledgerEntryId = existingLedgerLink?.LedgerEntryId;
            if (transaction.ReconciledAmount > 0m && existingLedgerLink is null)
            {
                var fiscalPeriod = await EnsureFiscalPeriodAsync(command.CompanyId, transaction.BookingDate, cancellationToken);
                var offsetAccount = await ResolveOffsetFinanceAccountAsync(
                    command.CompanyId,
                    transaction.BankAccount.FinanceAccountId,
                    transaction.Amount,
                    cancellationToken);
                var ledgerEntry = CreateLedgerEntry(transaction, fiscalPeriod.Id);
                _dbContext.LedgerEntries.Add(ledgerEntry);
                _dbContext.LedgerEntryLines.AddRange(
                    BuildLedgerLines(
                        command.CompanyId,
                        ledgerEntry.Id,
                        transaction.BankAccount.FinanceAccountId,
                        offsetAccount.Id,
                        transaction.Amount,
                        transaction.Currency,
                        transaction.ReferenceText));

                _dbContext.LedgerEntrySourceMappings.Add(new LedgerEntrySourceMapping(
                    ledgerEntry.Id,
                    command.CompanyId,
                    ledgerEntry.Id,
                    FinanceCashPostingSourceTypes.BankTransaction,
                    transaction.Id.ToString("D"),
                    transaction.BookingDate,
                    transaction.BookingDate));

                _dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
                    Guid.NewGuid(),
                    command.CompanyId,
                    transaction.Id,
                    ledgerEntry.Id,
                    idempotencyKey,
                    transaction.BookingDate));

                ledgerEntryId = ledgerEntry.Id;
            }

            var effectivePaymentIds = existingLinks
                .Select(x => x.PaymentId)
                .Concat(command.Payments.Select(x => x.PaymentId))
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            await UpsertPostingStateAsync(
                command.CompanyId,
                transaction.Id,
                linkedPaymentCount: existingLinks.Select(x => x.PaymentId).Concat(effectivePaymentIds).Distinct().Count(),
                hasLedgerEntry: ledgerEntryId.HasValue,
                unmatchedReason: null,
                conflictCode: null,
                conflictDetails: null,
                cancellationToken);

            await EnsurePaymentCashLedgerLinksAsync(command.CompanyId, transaction.Id, effectivePaymentIds, ledgerEntryId, transaction.BookingDate, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsReconciliationDuplicateViolation(ex))
            {
                // The database uniqueness constraints are the final exact-once guard for
                // payment links and the cash ledger side effect. If a retry or concurrent
                // request already committed the same reconciliation, reload that state.
                var duplicateResult = await TryLoadDuplicateReconciliationResultAsync(command, cancellationToken);
                if (duplicateResult is null)
                {
                    throw;
                }

                return duplicateResult;
            }

            return (await GetDetailAsync(
                new GetBankTransactionDetailQuery(command.CompanyId, command.BankTransactionId),
                cancellationToken))!;
    }

    private async Task<BankTransactionDetailDto?> TryLoadDuplicateReconciliationResultAsync(
        ReconcileBankTransactionCommand command,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var requestedAllocations = command.Payments
            .Where(x => x.PaymentId != Guid.Empty)
            .GroupBy(x => x.PaymentId)
            .ToDictionary(
                x => x.Key,
                x => NormalizeMoney(x.Single().AllocatedAmount));

        if (requestedAllocations.Count == 0)
        {
            return null;
        }

        var persistedLinks = await _dbContext.BankTransactionPaymentLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId &&
                        x.BankTransactionId == command.BankTransactionId &&
                        requestedAllocations.Keys.Contains(x.PaymentId))
            .ToListAsync(cancellationToken);

        if (persistedLinks.Count != requestedAllocations.Count)
        {
            return null;
        }

        foreach (var link in persistedLinks)
        {
            if (!requestedAllocations.TryGetValue(link.PaymentId, out var requestedAmount) ||
                NormalizeMoney(link.AllocatedAmount) != requestedAmount)
            {
                return null;
            }
        }

        var hasLedgerLink = await _dbContext.BankTransactionCashLedgerLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == command.CompanyId &&
                     (x.BankTransactionId == command.BankTransactionId ||
                      x.IdempotencyKey == BuildIdempotencyKey(command.CompanyId, command.BankTransactionId)),
                cancellationToken);

        if (!hasLedgerLink)
        {
            return null;
        }

        return await GetDetailAsync(new GetBankTransactionDetailQuery(command.CompanyId, command.BankTransactionId), cancellationToken);
    }

    private async Task<FiscalPeriod> EnsureFiscalPeriodAsync(
        Guid companyId,
        DateTime bookingDateUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.StartUtc <= bookingDateUtc &&
                     x.EndUtc > bookingDateUtc,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var startUtc = new DateTime(bookingDateUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
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
            startUtc,
            bookingDateUtc);

        _dbContext.FiscalPeriods.Add(period);
        return period;
    }

    private async Task<FinanceAccount> ResolveOffsetFinanceAccountAsync(
        Guid companyId,
        Guid bankFinanceAccountId,
        decimal transactionAmount,
        CancellationToken cancellationToken)
    {
        var preferredCode = transactionAmount > 0m ? "1100" : "2000";

        var preferred = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id != bankFinanceAccountId && x.Code == preferredCode)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);
        if (preferred is not null)
        {
            return preferred;
        }

        var fallback = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id != bankFinanceAccountId)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return fallback ?? throw new InvalidOperationException("Reconciliation requires a non-bank finance account to post the cash ledger entry.");
    }

    private async Task UpsertPostingStateAsync(
        Guid companyId,
        Guid bankTransactionId,
        int linkedPaymentCount,
        bool hasLedgerEntry,
        string? unmatchedReason,
        string? conflictCode,
        string? conflictDetails,
        CancellationToken cancellationToken)
    {
        var hasPaymentLinks = linkedPaymentCount > 0;
        var evaluatedAtUtc = DateTime.UtcNow;
        var matchingStatus = hasPaymentLinks
            ? BankTransactionMatchingStatuses.Matched
            : BankTransactionMatchingStatuses.Unmatched;
        var postingState = BankTransactionPostingStates.Resolve(matchingStatus, hasLedgerEntry, !string.IsNullOrWhiteSpace(conflictCode));

        var record = await _dbContext.BankTransactionPostingStateRecords
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId,
                cancellationToken);

        if (record is null)
        {
            _dbContext.BankTransactionPostingStateRecords.Add(new BankTransactionPostingStateRecord(
                Guid.NewGuid(),
                companyId,
                bankTransactionId,
                matchingStatus,
                postingState,
                linkedPaymentCount,
                evaluatedAtUtc,
                unmatchedReason,
                conflictCode,
                conflictDetails,
                evaluatedAtUtc,
                evaluatedAtUtc));
            return;
        }

        record.SyncSnapshot(
            matchingStatus,
            postingState,
            linkedPaymentCount,
            evaluatedAtUtc,
            unmatchedReason,
            conflictCode,
            conflictDetails);
    }

    private async Task EnsurePaymentCashLedgerLinksAsync(
        Guid companyId,
        Guid bankTransactionId,
        IReadOnlyCollection<Guid> paymentIds,
        Guid? ledgerEntryId,
        DateTime postedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!ledgerEntryId.HasValue || paymentIds.Count == 0)
        {
            return;
        }

        var existingPaymentIds = await _dbContext.PaymentCashLedgerLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntryId == ledgerEntryId.Value && paymentIds.Contains(x.PaymentId))
            .Select(x => x.PaymentId)
            .ToListAsync(cancellationToken);

        foreach (var paymentId in paymentIds.Where(x => !existingPaymentIds.Contains(x)))
        {
            _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(Guid.NewGuid(), companyId, paymentId, ledgerEntryId.Value, FinanceCashPostingSourceTypes.BankTransaction, bankTransactionId.ToString("D"), postedAtUtc, postedAtUtc));
        }
    }

    private static LedgerEntry CreateLedgerEntry(BankTransaction transaction, Guid fiscalPeriodId) =>
        new(
            Guid.NewGuid(),
            transaction.CompanyId,
            fiscalPeriodId,
            $"BTX-{transaction.BookingDate:yyyyMMdd}-{transaction.Id.ToString("N")[..8]}",
            transaction.BookingDate,
            LedgerEntryStatuses.Posted,
            $"Bank reconciliation for {transaction.ReferenceText}",
            FinanceCashPostingSourceTypes.BankTransaction,
            transaction.Id.ToString("D"),
            transaction.BookingDate,
            transaction.BookingDate,
            transaction.BookingDate);

    private static IReadOnlyList<LedgerEntryLine> BuildLedgerLines(
        Guid companyId,
        Guid ledgerEntryId,
        Guid bankFinanceAccountId,
        Guid offsetFinanceAccountId,
        decimal amount,
        string currency,
        string description)
    {
        var absoluteAmount = NormalizeMoney(Math.Abs(amount));
        if (amount > 0m)
        {
            return
            [
                new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, bankFinanceAccountId, absoluteAmount, 0m, currency, null, description),
                new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, offsetFinanceAccountId, 0m, absoluteAmount, currency, null, description)
            ];
        }

        return
        [
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, offsetFinanceAccountId, absoluteAmount, 0m, currency, null, description),
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, bankFinanceAccountId, 0m, absoluteAmount, currency, null, description)
        ];
    }

    private static CompanyBankAccountDto MapBankAccount(CompanyBankAccount bankAccount) =>
        new(
            bankAccount.Id,
            bankAccount.CompanyId,
            bankAccount.FinanceAccountId,
            bankAccount.FinanceAccount.Name,
            bankAccount.DisplayName,
            bankAccount.BankName,
            bankAccount.MaskedAccountNumber,
            bankAccount.Currency,
            bankAccount.ExternalCode,
            bankAccount.IsPrimary,
            bankAccount.IsActive,
            bankAccount.CreatedUtc,
            bankAccount.UpdatedUtc);

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Bank transaction operations are scoped to the active company context.");
        }
    }

    private static string? NormalizeOptionalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = BankTransactionReconciliationStatuses.Normalize(value);
        if (!BankTransactionReconciliationStatuses.IsSupported(normalized))
        {
            throw new ArgumentException($"Unsupported bank transaction status '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static int NormalizeLimit(int limit) =>
        limit <= 0
            ? DefaultLimit
            : Math.Min(limit, MaxLimit);

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string BuildIdempotencyKey(Guid companyId, Guid bankTransactionId) =>
        $"bank-transaction-ledger:{companyId:N}:{bankTransactionId:N}";

    private static bool IsReconciliationDuplicateViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            var referencesReconciliationLink =
                message.Contains("bank_transaction_payment_links", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("bank_transaction_cash_ledger_links", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_bank_transaction_payment_links_company_id_bank_transaction_id_payment_id", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("ledger_entry_source_mappings", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("payment_cash_ledger_links", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("bank_transaction_posting_states", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IX_bank_transaction_cash_ledger_links_company_id_idempotency_key", StringComparison.OrdinalIgnoreCase);

            if (!referencesReconciliationLink)
            {
                continue;
            }

            if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase))
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

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await action();
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
