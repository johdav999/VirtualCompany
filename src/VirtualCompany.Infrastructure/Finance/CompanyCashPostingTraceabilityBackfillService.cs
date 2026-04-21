using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyCashPostingTraceabilityBackfillService : ICashPostingTraceabilityBackfillService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<CompanyCashPostingTraceabilityBackfillService> _logger;

    public CompanyCashPostingTraceabilityBackfillService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        ILogger<CompanyCashPostingTraceabilityBackfillService> logger)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _logger = logger;
    }

    public async Task<CashPostingTraceabilityBackfillResultDto> BackfillAsync(
        BackfillCashPostingTraceabilityCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : command.CorrelationId.Trim();
        var batchSize = command.BatchSize <= 0 ? 250 : Math.Min(command.BatchSize, 1000);
        var migrated = 0;
        var backfilled = 0;
        var skipped = 0;
        var conflicts = 0;
        var page = 0;

        while (true)
        {
            var transactions = await _dbContext.BankTransactions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId)
                .OrderBy(x => x.BookingDate)
                .ThenBy(x => x.Id)
                .Skip(page * batchSize)
                .Take(batchSize)
                .Select(x => new { x.Id, x.CompanyId, x.BookingDate })
                .ToListAsync(cancellationToken);

            if (transactions.Count == 0)
            {
                break;
            }

            page++;
            var transactionIds = transactions.Select(x => x.Id).ToArray();
            var transactionSourceIds = transactionIds.ToDictionary(x => x, x => x.ToString("D"));
            var paymentLinks = await _dbContext.BankTransactionPaymentLinks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && transactionIds.Contains(x.BankTransactionId))
                .ToListAsync(cancellationToken);
            var cashLinks = await _dbContext.BankTransactionCashLedgerLinks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && transactionIds.Contains(x.BankTransactionId))
                .ToListAsync(cancellationToken);
            var postingStates = await _dbContext.BankTransactionPostingStateRecords
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId && transactionIds.Contains(x.BankTransactionId))
                .ToDictionaryAsync(x => x.BankTransactionId, cancellationToken);
            var inferredMappings = await _dbContext.LedgerEntrySourceMappings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId &&
                            x.SourceType == FinanceCashPostingSourceTypes.BankTransaction &&
                            transactionSourceIds.Values.Contains(x.SourceId))
                .ToListAsync(cancellationToken);

            var paymentIds = paymentLinks.Select(x => x.PaymentId).Distinct().ToArray();
            var existingPaymentLedgerLinks = paymentIds.Length == 0
                ? []
                : await _dbContext.PaymentCashLedgerLinks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.CompanyId == command.CompanyId && paymentIds.Contains(x.PaymentId))
                    .ToListAsync(cancellationToken);
            var existingPaymentLedgerPairs = existingPaymentLedgerLinks
                .Select(x => (x.PaymentId, x.LedgerEntryId))
                .ToHashSet();

            foreach (var transaction in transactions)
            {
                var linksForTransaction = paymentLinks.Where(x => x.BankTransactionId == transaction.Id).ToList();
                var cashLinksForTransaction = cashLinks.Where(x => x.BankTransactionId == transaction.Id).ToList();
                var mappingsForTransaction = inferredMappings
                    .Where(x => string.Equals(x.SourceId, transactionSourceIds[transaction.Id], StringComparison.OrdinalIgnoreCase))
                    .ToList();

                string? conflictCode = null;
                string? conflictDetails = null;
                Guid? ledgerEntryId = null;
                var effectivePostedAtUtc = transaction.BookingDate;

                if (cashLinksForTransaction.Count > 1)
                {
                    conflictCode = "duplicate_cash_posting_links";
                    conflictDetails = "More than one cash ledger link exists for the same bank transaction.";
                }
                else if (cashLinksForTransaction.Count == 1)
                {
                    ledgerEntryId = cashLinksForTransaction[0].LedgerEntryId;
                    effectivePostedAtUtc = cashLinksForTransaction[0].CreatedUtc;
                }

                if (!ledgerEntryId.HasValue)
                {
                    if (mappingsForTransaction.Count == 1)
                    {
                        ledgerEntryId = mappingsForTransaction[0].LedgerEntryId;
                        effectivePostedAtUtc = mappingsForTransaction[0].PostedAtUtc;
                        _dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
                            Guid.NewGuid(),
                            command.CompanyId,
                            transaction.Id,
                            ledgerEntryId.Value,
                            BuildIdempotencyKey(command.CompanyId, transaction.Id),
                            effectivePostedAtUtc));
                        migrated++;
                    }
                    else if (mappingsForTransaction.Count > 1)
                    {
                        conflictCode = "ambiguous_legacy_cash_posting";
                        conflictDetails = "More than one legacy journal entry mapping was found for the bank transaction.";
                    }
                }
                else if (mappingsForTransaction.Count == 1 && mappingsForTransaction[0].LedgerEntryId != ledgerEntryId.Value)
                {
                    conflictCode = "conflicting_cash_posting_traceability";
                    conflictDetails = "Existing cash posting link does not match the inferred legacy ledger entry mapping.";
                }

                if (linksForTransaction.Count == 0 && ledgerEntryId.HasValue)
                {
                    conflictCode ??= "unmatched_transaction_has_posting";
                    conflictDetails ??= "An unmatched bank transaction already has a cash posting journal entry.";
                }

                if (!postingStates.TryGetValue(transaction.Id, out var postingState))
                {
                    _dbContext.BankTransactionPostingStateRecords.Add(CreatePostingStateRecord(
                        command.CompanyId,
                        transaction.Id,
                        linksForTransaction.Count,
                        ledgerEntryId.HasValue,
                        transaction.BookingDate,
                        conflictCode,
                        conflictDetails));
                    backfilled++;
                }
                else
                {
                    var matchingStatus = linksForTransaction.Count > 0
                        ? BankTransactionMatchingStatuses.Matched
                        : string.Equals(postingState.MatchingStatus, BankTransactionMatchingStatuses.ManuallyClassified, StringComparison.OrdinalIgnoreCase)
                            ? BankTransactionMatchingStatuses.ManuallyClassified
                            : BankTransactionMatchingStatuses.Unmatched;
                    var desiredPostingState = BankTransactionPostingStates.Resolve(
                        matchingStatus,
                        ledgerEntryId.HasValue,
                        !string.IsNullOrWhiteSpace(conflictCode));
                    var desiredUnmatchedReason = linksForTransaction.Count > 0 ? null : "no_payment_match";

                    if (postingState.MatchingStatus != matchingStatus ||
                        postingState.PostingState != desiredPostingState ||
                        postingState.LinkedPaymentCount != linksForTransaction.Count ||
                        !string.Equals(postingState.UnmatchedReason, desiredUnmatchedReason, StringComparison.Ordinal) ||
                        !string.Equals(postingState.ConflictCode, conflictCode, StringComparison.Ordinal) ||
                        !string.Equals(postingState.ConflictDetails, conflictDetails, StringComparison.Ordinal))
                    {
                        postingState.SyncSnapshot(
                            matchingStatus,
                            desiredPostingState,
                            linksForTransaction.Count,
                            transaction.BookingDate,
                            desiredUnmatchedReason,
                            conflictCode,
                            conflictDetails);
                        backfilled++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                if (ledgerEntryId.HasValue && linksForTransaction.Count > 0 && string.IsNullOrWhiteSpace(conflictCode))
                {
                    foreach (var paymentLink in linksForTransaction)
                    {
                        var key = (paymentLink.PaymentId, ledgerEntryId.Value);
                        if (existingPaymentLedgerPairs.Add(key))
                        {
                            _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(
                                Guid.NewGuid(),
                                command.CompanyId,
                                paymentLink.PaymentId,
                                ledgerEntryId.Value,
                                FinanceCashPostingSourceTypes.BankTransaction,
                                transaction.Id.ToString("D"),
                                effectivePostedAtUtc,
                                effectivePostedAtUtc));
                            backfilled++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(conflictCode))
                {
                    conflicts++;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(correlationId, command.CompanyId));
        _logger.LogInformation(
            "Completed cash posting traceability backfill for company {CompanyId}. Migrated={MigratedRecordCount} Backfilled={BackfilledRecordCount} Skipped={SkippedRecordCount} Conflicts={ConflictCount}.",
            command.CompanyId,
            migrated,
            backfilled,
            skipped,
            conflicts);

        return new CashPostingTraceabilityBackfillResultDto(
            command.CompanyId,
            correlationId,
            migrated,
            backfilled,
            skipped,
            conflicts);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Cash posting backfill is scoped to the active company context.");
        }
    }

    private static string BuildIdempotencyKey(Guid companyId, Guid bankTransactionId) =>
        $"bank-transaction-ledger:{companyId:N}:{bankTransactionId:N}";

    private static BankTransactionPostingStateRecord CreatePostingStateRecord(
        Guid companyId,
        Guid bankTransactionId,
        int linkedPaymentCount,
        bool hasLedgerEntry,
        DateTime evaluatedAtUtc,
        string? conflictCode,
        string? conflictDetails) =>
        new(
            Guid.NewGuid(),
            companyId,
            bankTransactionId,
            linkedPaymentCount > 0 ? BankTransactionMatchingStatuses.Matched : BankTransactionMatchingStatuses.Unmatched,
            BankTransactionPostingStates.Resolve(linkedPaymentCount > 0, hasLedgerEntry, !string.IsNullOrWhiteSpace(conflictCode)),
            linkedPaymentCount,
            evaluatedAtUtc,
            linkedPaymentCount > 0 ? null : "no_payment_match",
            conflictCode,
            conflictDetails,
            evaluatedAtUtc,
            evaluatedAtUtc);
}