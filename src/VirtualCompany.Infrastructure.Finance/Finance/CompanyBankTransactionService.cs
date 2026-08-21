using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyBankTransactionService : IBankTransactionReadService, IBankTransactionCommandService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IAccountingPostingService _postingService;
    private readonly IAccountingAccountRoleResolver _roleResolver;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public CompanyBankTransactionService(VirtualCompanyDbContext dbContext) : this(dbContext, null) { }

    public CompanyBankTransactionService(VirtualCompanyDbContext dbContext, ICompanyContextAccessor? companyContextAccessor)
        : this(
            dbContext,
            companyContextAccessor,
            new AccountingPostingService(dbContext, new AccountingJournalReadService(dbContext), new AuditEventWriter(dbContext), TimeProvider.System),
            new AccountingAccountRoleResolver(dbContext),
            new AuditEventWriter(dbContext),
            TimeProvider.System)
    {
    }

    public CompanyBankTransactionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        IAccountingPostingService postingService,
        IAccountingAccountRoleResolver roleResolver,
        IAuditEventWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _postingService = postingService;
        _roleResolver = roleResolver;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<BankTransactionDto>> ListAsync(ListBankTransactionsQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var status = NormalizeOptionalStatus(query.Status);
        var rows = _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId);
        if (query.BankAccountId is { } bankAccountId && bankAccountId != Guid.Empty) rows = rows.Where(x => x.BankAccountId == bankAccountId);
        if (query.BookingDateFromUtc is { } from) rows = rows.Where(x => x.BookingDate >= NormalizeUtc(from));
        if (query.BookingDateToUtc is { } to) rows = rows.Where(x => x.BookingDate <= NormalizeUtc(to));
        if (status is not null) rows = rows.Where(x => x.Status == status);
        if (query.MinAmount is { } min) rows = rows.Where(x => x.Amount >= min);
        if (query.MaxAmount is { } max) rows = rows.Where(x => x.Amount <= max);
        var transactions = await rows
            .Include(x => x.BankAccount)
            .ThenInclude(x => x.FinanceAccount)
            .OrderByDescending(x => x.BookingDate)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(NormalizeLimit(query.Limit))
            .ToListAsync(cancellationToken);
        return transactions.Select(x => MapList(x, x.BankAccount)).ToArray();
    }

    public async Task<BankTransactionDetailDto?> GetDetailAsync(GetBankTransactionDetailQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.BankTransactionId == Guid.Empty) throw new ArgumentException("Bank transaction id is required.", nameof(query));
        var transaction = await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.BankAccount).ThenInclude(x => x.FinanceAccount)
            .Include(x => x.CashLedgerLinks)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BankTransactionId, cancellationToken);
        if (transaction is null) return null;
        var payments = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BankTransactionId == query.BankTransactionId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new BankTransactionPaymentLinkDto(x.Id, x.PaymentId, x.Payment.PaymentType, x.Payment.PaymentDate,
                x.Payment.CounterpartyReference, x.AllocatedAmount, x.Currency, x.CreatedUtc))
            .ToListAsync(cancellationToken);
        return new BankTransactionDetailDto(
            transaction.Id, transaction.CompanyId, transaction.BankAccountId, transaction.BankAccount.DisplayName,
            transaction.BankAccount.BankName, transaction.BankAccount.MaskedAccountNumber, transaction.BookingDate,
            transaction.ValueDate, transaction.Amount, transaction.Currency, transaction.ReferenceText,
            transaction.Counterparty, transaction.Status, transaction.ReconciledAmount, transaction.ExternalReference,
            transaction.CashLedgerLinks.OrderByDescending(x => x.CreatedUtc).Select(x => (Guid?)x.LedgerEntryId).FirstOrDefault(),
            payments, MapBankAccount(transaction.BankAccount));
    }

    public async Task<BankReconciliationWorkspaceDto> ListReconciliationAsync(ListBankReconciliationItemsQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var rows = _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.BankAccount).Include(x => x.PaymentLinks).Include(x => x.CashLedgerLinks).Include(x => x.PostingStateRecord)
            .Where(x => x.CompanyId == query.CompanyId);
        if (query.FromUtc is { } from) rows = rows.Where(x => x.BookingDate >= NormalizeUtc(from));
        if (query.ToUtc is { } to) rows = rows.Where(x => x.BookingDate <= NormalizeUtc(to));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.Counterparty.Contains(search) || x.ReferenceText.Contains(search) || (x.ExternalReference != null && x.ExternalReference.Contains(search)));
        }
        var materialized = await rows.OrderByDescending(x => x.BookingDate).Take(NormalizeLimit(query.Limit)).ToListAsync(cancellationToken);
        var followUps = await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == BankReconciliationFollowUpStatuses.Open)
            .Select(x => x.BankTransactionId).ToHashSetAsync(cancellationToken);
        var items = materialized.Select(x => MapReconciliationItem(x, followUps.Contains(x.Id))).ToArray();
        if (!string.IsNullOrWhiteSpace(query.State))
            items = items.Where(x => string.Equals(x.State, NormalizeState(query.State), StringComparison.OrdinalIgnoreCase)).ToArray();
        var counts = materialized.Select(x => MapReconciliationItem(x, followUps.Contains(x.Id))).GroupBy(x => x.State)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        return new BankReconciliationWorkspaceDto(items, counts);
    }

    public async Task<BankReconciliationDetailDto?> GetReconciliationDetailAsync(GetBankReconciliationDetailQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var detail = await GetDetailAsync(new GetBankTransactionDetailQuery(query.CompanyId, query.BankTransactionId), cancellationToken);
        if (detail is null) return null;
        var transaction = await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.PostingStateRecord).Include(x => x.CashLedgerLinks)
            .SingleAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BankTransactionId, cancellationToken);
        var followUp = await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BankTransactionId == query.BankTransactionId)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        var state = ResolveState(transaction, followUp?.Status == BankReconciliationFollowUpStatuses.Open);
        var candidates = await LoadCandidatesAsync(transaction, cancellationToken);
        var ledgerIds = transaction.CashLedgerLinks.Select(x => x.LedgerEntryId)
            .Concat(new[] { transaction.PostingStateRecord?.SuspenseLedgerEntryId, transaction.PostingStateRecord?.ReclassifiedLedgerEntryId }.OfType<Guid>())
            .Distinct().ToArray();
        var journals = ledgerIds.Length == 0 ? [] : await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                (ledgerIds.Contains(x.Id) || (x.OriginalLedgerEntryId.HasValue && ledgerIds.Contains(x.OriginalLedgerEntryId.Value))))
            .OrderBy(x => x.PostingDate)
            .Select(x => new BankReconciliationJournalLinkDto(x.Id, x.EntryNumber, x.PostingType ?? LedgerPostingTypeValues.Bank,
                x.Status, x.PostingDate, x.Id == transaction.PostingStateRecord!.SuspenseLedgerEntryId,
                x.Id == transaction.PostingStateRecord.ReclassifiedLedgerEntryId || x.PostingType == LedgerPostingTypeValues.Reversal))
            .ToListAsync(cancellationToken);
        var suspenseRole = await _roleResolver.ResolveOptionalAsync(query.CompanyId, AccountingAccountRoleKeys.Suspense, cancellationToken);
        return new BankReconciliationDetailDto(
            detail,
            state,
            NormalizeMoney(transaction.AbsoluteAmount - transaction.ReconciledAmount),
            transaction.SourceVersion,
            transaction.PostingStateRecord?.HandlingMode,
            transaction.PostingStateRecord?.ReviewReason,
            candidates,
            journals,
            followUp is null ? null : new BankReconciliationFollowUpDto(followUp.Id, followUp.Status, followUp.Reason,
                followUp.LedgerEntryId, followUp.CreatedUtc, followUp.ResolvedUtc),
            suspenseRole is not null,
            followUp?.Status == BankReconciliationFollowUpStatuses.Open,
            transaction.PostingStateRecord?.ConflictDetails);
    }

    public Task<BankTransactionDetailDto> ReconcileAsync(ReconcileBankTransactionCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(() => ReconcileCoreAsync(command, cancellationToken), cancellationToken);

    internal Task<BankTransactionDetailDto> ReconcileWithinAmbientTransactionAsync(ReconcileBankTransactionCommand command, CancellationToken cancellationToken) =>
        ReconcileCoreAsync(command, cancellationToken);

    private async Task<BankTransactionDetailDto> ReconcileCoreAsync(ReconcileBankTransactionCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var mode = NormalizeHandlingMode(command.HandlingMode);
        var transaction = await _dbContext.BankTransactions.IgnoreQueryFilters()
            .Include(x => x.BankAccount).Include(x => x.PostingStateRecord)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BankTransactionId, cancellationToken)
            ?? throw new KeyNotFoundException("Bank transaction was not found.");
        try { transaction.EnsureSourceVersion(command.ExpectedSourceVersion); }
        catch (InvalidOperationException ex) { throw Validation(nameof(command.ExpectedSourceVersion), ex.Message); }

        var initialReconciledAmount = transaction.ReconciledAmount;
        var initialStateId = transaction.PostingStateRecord?.Id;
        var initialMatchingStatus = transaction.PostingStateRecord?.MatchingStatus;
        var initialPostingState = transaction.PostingStateRecord?.PostingState;
        var initialLinkedPaymentCount = transaction.PostingStateRecord?.LinkedPaymentCount;
        var initialUnmatchedReason = transaction.PostingStateRecord?.UnmatchedReason;
        var initialHandlingMode = transaction.PostingStateRecord?.HandlingMode;
        var initialReviewReason = transaction.PostingStateRecord?.ReviewReason;
        var initialSuspenseLedgerEntryId = transaction.PostingStateRecord?.SuspenseLedgerEntryId;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existingLinks = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.BankTransactionId == transaction.Id).ToListAsync(cancellationToken);
        if (mode == BankReconciliationHandlingModes.Payment)
            await ApplyPaymentMatchesAsync(command, transaction, existingLinks, now, cancellationToken);
        else if (mode == BankReconciliationHandlingModes.LeaveUnmatched)
        {
            EnsureReviewed(command);
            var state = await UpsertStateAsync(transaction, existingLinks.Count, false, "left_unmatched_after_review", cancellationToken);
            state.RecordReviewedHandling(mode, transaction.SourceVersion, command.ActorUserId, command.ReviewReason!,
                BankTransactionPostingStates.SkippedUnmatched, now);
            if (HasMaterialReconciliationChange(state))
                await AuditReconciliationAsync(command, transaction, mode, transaction.ReconciledAmount, null, now, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (await GetDetailAsync(new(command.CompanyId, transaction.Id), cancellationToken))!;
        }
        else
        {
            EnsureReviewed(command);
        }

        var allocated = NormalizeMoney(existingLinks.Sum(x => x.AllocatedAmount) + _dbContext.ChangeTracker.Entries<BankTransactionPaymentLink>()
            .Where(x => x.State == EntityState.Added && x.Entity.BankTransactionId == transaction.Id).Sum(x => x.Entity.AllocatedAmount));
        var adjustments = command.Adjustments ?? [];
        var adjustmentsBalancePayment = mode == BankReconciliationHandlingModes.Payment && adjustments.Count > 0 &&
            IsBalancedWithAdjustments(transaction, allocated, adjustments);
        if (mode == BankReconciliationHandlingModes.Payment && adjustments.Count > 0 && !adjustmentsBalancePayment)
            throw Validation(nameof(command.Adjustments), "The payment and explicit adjustment lines do not balance to the bank transaction amount.");
        var shouldPost = mode != BankReconciliationHandlingModes.Payment || allocated == transaction.AbsoluteAmount || adjustmentsBalancePayment;
        transaction.ApplyReconciliation(shouldPost ? transaction.AbsoluteAmount : allocated, now);
        Guid? ledgerEntryId = null;
        var ledgerEntryIds = new HashSet<Guid>();
        var newBankLedgerEntryIds = new HashSet<Guid>();
        var nativeAccountingEnabled = shouldPost &&
            await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        if (nativeAccountingEnabled)
        {
            if (!string.IsNullOrWhiteSpace(transaction.PostingStateRecord?.ConflictCode))
                throw Validation(nameof(command.BankTransactionId), transaction.PostingStateRecord.ConflictDetails ?? "Historical bank posting links need review before this transaction can be posted.");
            var existingCashLinks = await _dbContext.BankTransactionCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.BankTransactionId == transaction.Id)
                .OrderBy(x => x.CreatedUtc)
                .ToListAsync(cancellationToken);
            foreach (var existingCashLink in existingCashLinks)
            {
                ledgerEntryId = existingCashLink.LedgerEntryId;
                ledgerEntryIds.Add(existingCashLink.LedgerEntryId);
            }

            var desiredLines = await BuildPostingLinesAsync(command, transaction, allocated, cancellationToken);
            var period = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.StartUtc <= transaction.BookingDate && x.EndUtc > transaction.BookingDate, cancellationToken)
                ?? throw new AccountingPostingException(AccountingPostingReasonCodes.PeriodNotFound, "No accounting period covers the bank transaction date.");
            var sourceVersion = $"{transaction.SourceVersion}:{mode}";
            var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                ? $"bank-reconciliation:{command.CompanyId:N}:{transaction.Id:N}:{sourceVersion}"
                : command.IdempotencyKey.Trim();
            var actorType = command.ActorUserId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.User;
            var linesToPost = desiredLines;
            if (existingCashLinks.Count == 0 && mode == BankReconciliationHandlingModes.Payment)
            {
                var paymentIds = existingLinks.Select(x => x.PaymentId).Concat(command.Payments.Select(x => x.PaymentId)).Distinct().ToArray();
                var reuse = await ReuseExistingPaymentCashPostingsAsync(
                    transaction, paymentIds, allocated, desiredLines, cancellationToken);
                linesToPost = reuse.ResidualLines;
                foreach (var reusedLedgerEntryId in reuse.LedgerEntryIds)
                {
                    ledgerEntryIds.Add(reusedLedgerEntryId);
                    ledgerEntryId ??= reusedLedgerEntryId;
                    _dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
                        Guid.NewGuid(), command.CompanyId, transaction.Id, reusedLedgerEntryId,
                        BuildBankLedgerLinkIdentity(command.CompanyId, transaction.Id, reusedLedgerEntryId), now));
                }
            }

            if (existingCashLinks.Count == 0 && linesToPost.Count > 0)
            {
                var posted = await _postingService.PostAsync(new PostAccountingEntryCommand(new ProposedAccountingEntry(
                command.CompanyId,
                period.Id,
                "B",
                DateOnly.FromDateTime(transaction.ValueDate),
                DateOnly.FromDateTime(transaction.BookingDate),
                LedgerPostingTypeValues.Bank,
                $"Bank reconciliation for {transaction.ReferenceText}",
                FinanceCashPostingSourceTypes.BankTransaction,
                transaction.Id.ToString("D"),
                sourceVersion,
                idempotencyKey,
                linesToPost,
                command.ActorUserId,
                PolicyFacts: new Dictionary<string, string>
                {
                    ["handlingMode"] = mode,
                    ["bankTransactionAmount"] = transaction.AbsoluteAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["allocatedAmount"] = allocated.ToString("0.00", CultureInfo.InvariantCulture)
                },
                    ActorType: actorType,
                    EffectivePostedAtUtc: transaction.BookingDate), command.CorrelationId), cancellationToken);
                ledgerEntryId = posted.Journal.Id;
                ledgerEntryIds.Add(posted.Journal.Id);
                newBankLedgerEntryIds.Add(posted.Journal.Id);
                _dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
                    Guid.NewGuid(), command.CompanyId, transaction.Id, ledgerEntryId.Value,
                    BuildBankLedgerLinkIdentity(command.CompanyId, transaction.Id, ledgerEntryId.Value), now));
            }

            foreach (var paymentId in existingLinks.Select(x => x.PaymentId).Concat(command.Payments.Select(x => x.PaymentId)).Distinct())
            {
                foreach (var linkedLedgerEntryId in newBankLedgerEntryIds)
                {
                    if (!await _dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                            .AnyAsync(x => x.CompanyId == command.CompanyId && x.PaymentId == paymentId && x.LedgerEntryId == linkedLedgerEntryId, cancellationToken) &&
                        !_dbContext.ChangeTracker.Entries<PaymentCashLedgerLink>().Any(x => x.State == EntityState.Added &&
                            x.Entity.CompanyId == command.CompanyId && x.Entity.PaymentId == paymentId && x.Entity.LedgerEntryId == linkedLedgerEntryId))
                        _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(Guid.NewGuid(), command.CompanyId, paymentId,
                            linkedLedgerEntryId, FinanceCashPostingSourceTypes.BankTransaction, transaction.Id.ToString("D"), transaction.BookingDate, now));
                }
            }
        }

        var linkedPaymentCount = existingLinks.Select(x => x.PaymentId).Concat(command.Payments.Select(x => x.PaymentId)).Distinct().Count();
        var stateRecord = await UpsertStateAsync(transaction, linkedPaymentCount, ledgerEntryIds.Count > 0,
            allocated == 0 ? "no_payment_match" : allocated < transaction.AbsoluteAmount ? "partially_matched" : null, cancellationToken);
        if (mode is BankReconciliationHandlingModes.Categorization or BankReconciliationHandlingModes.Suspense)
        {
            stateRecord.RecordReviewedHandling(mode, transaction.SourceVersion, command.ActorUserId, command.ReviewReason!,
                mode == BankReconciliationHandlingModes.Suspense ? BankTransactionPostingStates.Suspense : BankTransactionPostingStates.Posted,
                now, mode == BankReconciliationHandlingModes.Suspense ? ledgerEntryId : null);
        }
        if (mode == BankReconciliationHandlingModes.Suspense && ledgerEntryId.HasValue &&
            !await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == command.CompanyId &&
                x.BankTransactionId == transaction.Id && x.Status == BankReconciliationFollowUpStatuses.Open, cancellationToken))
            _dbContext.BankReconciliationFollowUps.Add(new BankReconciliationFollowUp(Guid.NewGuid(), command.CompanyId,
                transaction.Id, ledgerEntryId.Value, command.ReviewReason!, command.ActorUserId, now));
        if (HasMaterialReconciliationChange(stateRecord))
            await AuditReconciliationAsync(command, transaction, mode, transaction.ReconciledAmount, ledgerEntryId, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetDetailAsync(new(command.CompanyId, transaction.Id), cancellationToken))!;

        bool HasMaterialReconciliationChange(BankTransactionPostingStateRecord state) =>
            initialReconciledAmount != transaction.ReconciledAmount ||
            initialStateId != state.Id ||
            !string.Equals(initialMatchingStatus, state.MatchingStatus, StringComparison.Ordinal) ||
            !string.Equals(initialPostingState, state.PostingState, StringComparison.Ordinal) ||
            initialLinkedPaymentCount != state.LinkedPaymentCount ||
            !string.Equals(initialUnmatchedReason, state.UnmatchedReason, StringComparison.Ordinal) ||
            !string.Equals(initialHandlingMode, state.HandlingMode, StringComparison.Ordinal) ||
            !string.Equals(initialReviewReason, state.ReviewReason, StringComparison.Ordinal) ||
            initialSuspenseLedgerEntryId != state.SuspenseLedgerEntryId ||
            _dbContext.ChangeTracker.Entries<BankTransactionPaymentLink>().Any(x => x.State == EntityState.Added && x.Entity.BankTransactionId == transaction.Id) ||
            _dbContext.ChangeTracker.Entries<BankTransactionCashLedgerLink>().Any(x => x.State == EntityState.Added && x.Entity.BankTransactionId == transaction.Id) ||
            _dbContext.ChangeTracker.Entries<PaymentCashLedgerLink>().Any(x => x.State == EntityState.Added) ||
            _dbContext.ChangeTracker.Entries<BankReconciliationFollowUp>().Any(x => x.State == EntityState.Added && x.Entity.BankTransactionId == transaction.Id);
    }

    public async Task<BankStatementImportResultDto> ImportStatementAsync(ImportBankStatementCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteInTransactionAsync(() => ImportStatementCoreAsync(command, cancellationToken), cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent import can win either the statement-identity or row-identity unique index.
            // Re-read once after the losing transaction has rolled back so the normal replay/overlap
            // rules produce a deterministic result instead of leaking a provider exception.
            _dbContext.ChangeTracker.Clear();
            return await ExecuteInTransactionAsync(() => ImportStatementCoreAsync(command, cancellationToken), cancellationToken);
        }
    }

    private async Task<BankStatementImportResultDto> ImportStatementCoreAsync(ImportBankStatementCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (command.BankAccountId == Guid.Empty || command.Rows is not { Count: > 0 })
            throw Validation(nameof(command.Rows), "A bank account and at least one statement row are required.");
        var sourceKey = Required(command.SourceKey, nameof(command.SourceKey), 64).ToLowerInvariant();
        var statementIdentity = Required(command.StatementIdentity, nameof(command.StatementIdentity), 128);
        var contentHash = RequiredHash(command.ContentHash, nameof(command.ContentHash));
        var existingImport = await _dbContext.BankStatementImports.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BankAccountId == command.BankAccountId &&
                x.SourceKey == sourceKey && x.StatementIdentity == statementIdentity, cancellationToken);
        if (existingImport is not null)
        {
            if (!string.Equals(existingImport.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(command.ContentHash), "This statement identity was already imported with different content.");
            var count = await _dbContext.BankStatementImportRows.IgnoreQueryFilters().CountAsync(x => x.CompanyId == command.CompanyId && x.BankStatementImportId == existingImport.Id, cancellationToken);
            return new(existingImport.Id, 0, count, 0, true, []);
        }
        var bankAccountExists = await _dbContext.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BankAccountId && x.IsActive, cancellationToken);
        if (!bankAccountExists) throw new KeyNotFoundException("The bank account was not found.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var import = new BankStatementImport(Guid.NewGuid(), command.CompanyId, command.BankAccountId, sourceKey,
            statementIdentity, contentHash, command.ActorUserId, now);
        _dbContext.BankStatementImports.Add(import);
        var imported = 0;
        var duplicates = 0;
        var conflicts = new List<string>();
        foreach (var group in command.Rows.GroupBy(x => Required(x.RowIdentity, nameof(x.RowIdentity), 128), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1) throw Validation(nameof(command.Rows), $"Row identity '{group.Key}' appears more than once in the statement.");
            var row = group.Single();
            var rowHash = ComputeRowHash(row);
            var existing = await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BankAccountId == command.BankAccountId &&
                    x.ImportSource == sourceKey && x.RowIdentity == group.Key, cancellationToken);
            BankTransaction transaction;
            if (existing is not null)
            {
                if (!string.Equals(existing.RowContentHash, rowHash, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(group.Key);
                    continue;
                }
                transaction = existing;
                duplicates++;
            }
            else
            {
                transaction = new BankTransaction(Guid.NewGuid(), command.CompanyId, command.BankAccountId, row.BookingDateUtc,
                    row.ValueDateUtc, row.Amount, row.Currency, row.ReferenceText, row.Counterparty, row.ExternalReference,
                    sourceKey, 0m, now, now, null, group.Key, rowHash, 1);
                _dbContext.BankTransactions.Add(transaction);
                imported++;
            }
            _dbContext.BankStatementImportRows.Add(new BankStatementImportRow(Guid.NewGuid(), command.CompanyId, import.Id,
                transaction.Id, group.Key, rowHash, now));
        }
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            AuditEventActions.AccountingBankStatementImported, "bank_statement_import", import.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "A bank statement was imported with stable statement and row identities.", ["bank_statement"],
            new Dictionary<string, string?> { ["imported"] = imported.ToString(), ["duplicates"] = duplicates.ToString(), ["conflicts"] = conflicts.Count.ToString() },
            command.CorrelationId, now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new(import.Id, imported, duplicates, conflicts.Count, false, conflicts);
    }

    public Task<BankReconciliationDetailDto> ReclassifySuspenseAsync(ReclassifyBankSuspenseCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(() => ReclassifySuspenseCoreAsync(command, cancellationToken), cancellationToken);

    private async Task<BankReconciliationDetailDto> ReclassifySuspenseCoreAsync(ReclassifyBankSuspenseCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var transaction = await _dbContext.BankTransactions.IgnoreQueryFilters().Include(x => x.BankAccount).Include(x => x.PostingStateRecord)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BankTransactionId, cancellationToken)
            ?? throw new KeyNotFoundException("Bank transaction was not found.");
        try { transaction.EnsureSourceVersion(command.ExpectedSourceVersion); }
        catch (InvalidOperationException ex) { throw Validation(nameof(command.ExpectedSourceVersion), ex.Message); }
        var followUp = await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BankTransactionId == transaction.Id && x.Status == BankReconciliationFollowUpStatuses.Open, cancellationToken)
            ?? throw Validation(nameof(command.BankTransactionId), "This bank transaction is not waiting for suspense reclassification.");
        if (transaction.PostingStateRecord?.ReclassifiedLedgerEntryId is not null)
            throw Validation(nameof(command.BankTransactionId), "This suspense transaction has already been reclassified.");
        if (string.IsNullOrWhiteSpace(command.Reason)) throw Validation(nameof(command.Reason), "A correction reason is required.");

        var reversed = await _postingService.ReverseAsync(new ReverseAccountingEntryCommand(command.CompanyId, followUp.LedgerEntryId,
            command.FiscalPeriodId, "CR", command.PostingDate, command.Reason, $"{transaction.SourceVersion}:suspense-reversal",
            $"{command.IdempotencyKey}:reverse", command.ActorUserId, CorrelationId: command.CorrelationId), cancellationToken);
        var bank = await _roleResolver.ResolveRequiredAsync(command.CompanyId, AccountingAccountRoleKeys.Bank, cancellationToken);
        if (bank.FinanceAccountId != transaction.BankAccount.FinanceAccountId)
            throw Validation(nameof(command.BankTransactionId), "The bank transaction account does not match the configured bank account role.");
        var target = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.TargetFinanceAccountId &&
                x.Id != bank.FinanceAccountId && x.IsPostingEnabled, cancellationToken)
            ?? throw new KeyNotFoundException("The reclassification account was not found.");
        var amount = transaction.AbsoluteAmount;
        IReadOnlyList<ProposedAccountingLine> lines = transaction.Amount > 0m
            ? [new(bank.FinanceAccountId, amount, 0m, transaction.Currency, command.Reason), new(target.Id, 0m, amount, transaction.Currency, command.Reason)]
            : [new(target.Id, amount, 0m, transaction.Currency, command.Reason), new(bank.FinanceAccountId, 0m, amount, transaction.Currency, command.Reason)];
        var replacement = await _postingService.PostAsync(new PostAccountingEntryCommand(new ProposedAccountingEntry(
            command.CompanyId, command.FiscalPeriodId, "CR", DateOnly.FromDateTime(transaction.ValueDate), command.PostingDate,
            LedgerPostingTypeValues.Bank, command.Reason, "bank_transaction_reclassification", transaction.Id.ToString("D"),
            $"{transaction.SourceVersion}:reclassified", $"{command.IdempotencyKey}:replacement", lines, command.ActorUserId,
            Action: "reclassify", OriginalLedgerEntryId: followUp.LedgerEntryId, CorrectionReason: command.Reason), command.CorrelationId), cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        followUp.Resolve(command.ActorUserId, replacement.Journal.Id, now);
        transaction.PostingStateRecord!.RecordReviewedHandling(BankReconciliationHandlingModes.Categorization,
            transaction.SourceVersion, command.ActorUserId, command.Reason, BankTransactionPostingStates.Corrected, now,
            followUp.LedgerEntryId, replacement.Journal.Id);
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            AuditEventActions.AccountingBankSuspenseReclassified, "bank_transaction", transaction.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "A suspense posting was corrected through linked reversal and replacement journals.", ["bank_transaction", "accounting_journal"],
            new Dictionary<string, string?>
            {
                ["originalLedgerEntryId"] = followUp.LedgerEntryId.ToString("N"),
                ["reversalLedgerEntryId"] = reversed.Journal.Id.ToString("N"),
                ["replacementLedgerEntryId"] = replacement.Journal.Id.ToString("N")
            }, command.CorrelationId, now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetReconciliationDetailAsync(new(command.CompanyId, transaction.Id), cancellationToken))!;
    }

    private async Task ApplyPaymentMatchesAsync(ReconcileBankTransactionCommand command, BankTransaction transaction,
        IReadOnlyCollection<BankTransactionPaymentLink> existingLinks, DateTime now, CancellationToken cancellationToken)
    {
        if (command.Payments is not { Count: > 0 } && existingLinks.Count == 0)
            throw Validation(nameof(command.Payments), "At least one payment match is required.");
        if (command.Payments.GroupBy(x => x.PaymentId).Any(x => x.Key == Guid.Empty || x.Count() > 1))
            throw Validation(nameof(command.Payments), "Each payment must be selected once.");
        var ids = command.Payments.Select(x => x.PaymentId).ToArray();
        var payments = await _dbContext.Payments.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (payments.Count != ids.Length) throw new KeyNotFoundException("One or more finance payments were not found.");
        var linkedByPayment = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && ids.Contains(x.PaymentId))
            .GroupBy(x => x.PaymentId).Select(x => new { PaymentId = x.Key, Amount = x.Sum(y => y.AllocatedAmount) })
            .ToDictionaryAsync(x => x.PaymentId, x => x.Amount, cancellationToken);
        var existingTotal = existingLinks.Sum(x => x.AllocatedAmount);
        var newTotal = 0m;
        foreach (var match in command.Payments)
        {
            var payment = payments[match.PaymentId];
            var amount = NormalizeMoney(match.AllocatedAmount);
            if (amount <= 0m) throw Validation(nameof(match.AllocatedAmount), "Allocated amount must be greater than zero.");
            if (!string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(match.PaymentId), "Only completed payments can be matched.");
            if (!string.Equals(payment.Currency, transaction.Currency, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(match.AllocatedAmount), "Payment currency must match the bank transaction currency.");
            var expectedType = transaction.Amount > 0 ? PaymentTypes.Incoming : PaymentTypes.Outgoing;
            if (!string.Equals(payment.PaymentType, expectedType, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(match.PaymentId), "Payment direction does not match the bank transaction.");
            var existing = existingLinks.SingleOrDefault(x => x.PaymentId == match.PaymentId);
            if (existing is not null)
            {
                if (existing.AllocatedAmount != amount) throw Validation(nameof(match.AllocatedAmount), "Existing payment matches are immutable.");
                continue;
            }
            var previouslyLinked = linkedByPayment.GetValueOrDefault(payment.Id);
            if (NormalizeMoney(previouslyLinked + amount) > payment.Amount)
                throw Validation(nameof(match.AllocatedAmount), "The match would exceed the payment amount.");
            newTotal += amount;
            if (NormalizeMoney(existingTotal + newTotal) > transaction.AbsoluteAmount)
                throw Validation(nameof(match.AllocatedAmount), "The match would exceed the bank transaction amount.");
            _dbContext.BankTransactionPaymentLinks.Add(new BankTransactionPaymentLink(Guid.NewGuid(), command.CompanyId,
                transaction.Id, payment.Id, amount, transaction.Currency, now));
        }
    }

    private async Task<IReadOnlyList<ProposedAccountingLine>> BuildPostingLinesAsync(ReconcileBankTransactionCommand command,
        BankTransaction transaction, decimal allocated, CancellationToken cancellationToken)
    {
        var bank = await _roleResolver.ResolveRequiredAsync(command.CompanyId, AccountingAccountRoleKeys.Bank, cancellationToken);
        if (bank.FinanceAccountId != transaction.BankAccount.FinanceAccountId)
            throw Validation(nameof(command.BankTransactionId), "The bank transaction account does not match the configured bank account role.");
        var mode = NormalizeHandlingMode(command.HandlingMode);
        Guid offsetId;
        decimal offsetAmount;
        if (mode == BankReconciliationHandlingModes.Payment)
        {
            var role = transaction.Amount > 0 ? AccountingAccountRoleKeys.AccountsReceivable : AccountingAccountRoleKeys.AccountsPayable;
            offsetId = (await _roleResolver.ResolveRequiredAsync(command.CompanyId, role, cancellationToken)).FinanceAccountId;
            offsetAmount = allocated;
        }
        else if (mode == BankReconciliationHandlingModes.Suspense)
        {
            offsetId = (await _roleResolver.ResolveRequiredAsync(command.CompanyId, AccountingAccountRoleKeys.Suspense, cancellationToken)).FinanceAccountId;
            offsetAmount = transaction.AbsoluteAmount;
        }
        else
        {
            if (!command.CategorizationFinanceAccountId.HasValue || command.CategorizationFinanceAccountId == Guid.Empty)
                throw Validation(nameof(command.CategorizationFinanceAccountId), "Select an accounting category before posting.");
            offsetId = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.Id == command.CategorizationFinanceAccountId.Value && x.Id != bank.FinanceAccountId)
                .Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
            if (offsetId == Guid.Empty) throw new KeyNotFoundException("The categorization account was not found.");
            offsetAmount = transaction.AbsoluteAmount;
        }
        var description = $"Bank reconciliation for {transaction.ReferenceText}";
        var lines = new List<ProposedAccountingLine>();
        if (transaction.Amount > 0)
        {
            lines.Add(new(bank.FinanceAccountId, transaction.AbsoluteAmount, 0m, transaction.Currency, description));
            lines.Add(new(offsetId, 0m, offsetAmount, transaction.Currency, description));
        }
        else
        {
            lines.Add(new(offsetId, offsetAmount, 0m, transaction.Currency, description));
            lines.Add(new(bank.FinanceAccountId, 0m, transaction.AbsoluteAmount, transaction.Currency, description));
        }
        foreach (var adjustment in command.Adjustments ?? [])
        {
            var kind = adjustment.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AccountingAccountRoleKeys.BankAdjustmentRoles.Contains(kind))
                throw Validation(nameof(command.Adjustments), "The selected bank adjustment type is not supported by accounting policy.");
            var debit = NormalizeAdjustmentAmount(adjustment.DebitAmount);
            var credit = NormalizeAdjustmentAmount(adjustment.CreditAmount);
            if ((debit == 0m) == (credit == 0m))
                throw Validation(nameof(command.Adjustments), "Each bank adjustment must contain either one positive debit or one positive credit.");
            var role = await _roleResolver.ResolveRequiredAsync(command.CompanyId, kind, cancellationToken);
            lines.Add(new(role.FinanceAccountId, debit, credit,
                transaction.Currency, adjustment.Explanation));
        }
        return lines;
    }

    private async Task<PaymentCashPostingReuse> ReuseExistingPaymentCashPostingsAsync(
        BankTransaction transaction,
        IReadOnlyCollection<Guid> paymentIds,
        decimal allocated,
        IReadOnlyList<ProposedAccountingLine> desiredLines,
        CancellationToken cancellationToken)
    {
        if (paymentIds.Count == 0) return new(desiredLines, []);

        var linkedJournals = await _dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == transaction.CompanyId && paymentIds.Contains(x.PaymentId) &&
                x.SourceType != FinanceCashPostingSourceTypes.BankTransaction)
            .Select(x => x.LedgerEntryId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (linkedJournals.Count == 0) return new(desiredLines, []);

        var controlRole = transaction.Amount > 0m
            ? AccountingAccountRoleKeys.AccountsReceivable
            : AccountingAccountRoleKeys.AccountsPayable;
        var controlAccount = await _roleResolver.ResolveRequiredAsync(transaction.CompanyId, controlRole, cancellationToken);
        var governedJournalIds = await _dbContext.LedgerPostingIdentities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == transaction.CompanyId && linkedJournals.Contains(x.LedgerEntryId))
            .Select(x => x.LedgerEntryId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var journals = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.CompanyId == transaction.CompanyId && governedJournalIds.Contains(x.Id) && x.Status == LedgerEntryStatuses.Posted)
            .ToListAsync(cancellationToken);

        var reusable = new List<LedgerEntry>();
        decimal coveredAmount = 0m;
        foreach (var journal in journals)
        {
            if (journal.Lines.Count < 2 || journal.Lines.Any(x =>
                    x.FinanceAccountId != transaction.BankAccount.FinanceAccountId &&
                    x.FinanceAccountId != controlAccount.FinanceAccountId) ||
                journal.Lines.Any(x => !string.Equals(x.Currency, transaction.Currency, StringComparison.OrdinalIgnoreCase)))
                continue;

            var bankNet = NormalizeMoney(journal.Lines.Where(x => x.FinanceAccountId == transaction.BankAccount.FinanceAccountId)
                .Sum(x => x.DebitAmount - x.CreditAmount));
            var controlNet = NormalizeMoney(journal.Lines.Where(x => x.FinanceAccountId == controlAccount.FinanceAccountId)
                .Sum(x => x.DebitAmount - x.CreditAmount));
            var expectedDirection = transaction.Amount > 0m
                ? bankNet > 0m && controlNet < 0m
                : bankNet < 0m && controlNet > 0m;
            if (!expectedDirection || NormalizeMoney(Math.Abs(bankNet)) != NormalizeMoney(Math.Abs(controlNet))) continue;

            coveredAmount = NormalizeMoney(coveredAmount + Math.Abs(controlNet));
            reusable.Add(journal);
        }

        if (reusable.Count == 0) return new(desiredLines, []);
        if (coveredAmount > allocated)
            throw Validation(nameof(ReconcileBankTransactionCommand.Payments),
                "The selected payment already has cash postings that exceed this bank match. Review the payment and existing journals before continuing.");

        var desiredNet = desiredLines
            .GroupBy(x => x.FinanceAccountId)
            .ToDictionary(x => x.Key, x => NormalizeMoney(x.Sum(y => y.DebitAmount - y.CreditAmount)));
        var reusedNet = reusable.SelectMany(x => x.Lines)
            .GroupBy(x => x.FinanceAccountId)
            .ToDictionary(x => x.Key, x => NormalizeMoney(x.Sum(y => y.DebitAmount - y.CreditAmount)));
        var accountIds = desiredNet.Keys.Concat(reusedNet.Keys).Distinct().ToArray();
        var residual = accountIds.Select(accountId =>
            {
                var net = NormalizeMoney(desiredNet.GetValueOrDefault(accountId) - reusedNet.GetValueOrDefault(accountId));
                return net == 0m
                    ? null
                    : new ProposedAccountingLine(
                        accountId,
                        net > 0m ? net : 0m,
                        net < 0m ? Math.Abs(net) : 0m,
                        transaction.Currency,
                        $"Remaining bank reconciliation for {transaction.ReferenceText}");
            })
            .OfType<ProposedAccountingLine>()
            .ToArray();

        if (NormalizeMoney(residual.Sum(x => x.DebitAmount)) != NormalizeMoney(residual.Sum(x => x.CreditAmount)))
            throw Validation(nameof(ReconcileBankTransactionCommand.Payments),
                "Existing payment cash postings do not reconcile cleanly to this bank transaction.");

        return new(residual, reusable.Select(x => x.Id).ToArray());
    }

    private async Task<BankTransactionPostingStateRecord> UpsertStateAsync(BankTransaction transaction, int linkedPaymentCount,
        bool hasLedger, string? unmatchedReason, CancellationToken cancellationToken)
    {
        var record = transaction.PostingStateRecord ?? await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == transaction.CompanyId && x.BankTransactionId == transaction.Id, cancellationToken);
        var matching = linkedPaymentCount > 0 ? BankTransactionMatchingStatuses.Matched : BankTransactionMatchingStatuses.Unmatched;
        var posting = BankTransactionPostingStates.Resolve(matching, hasLedger, false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (record is null)
        {
            record = new BankTransactionPostingStateRecord(Guid.NewGuid(), transaction.CompanyId, transaction.Id, matching,
                posting, linkedPaymentCount, now, unmatchedReason, sourceVersion: transaction.SourceVersion);
            _dbContext.BankTransactionPostingStateRecords.Add(record);
        }
        else record.SyncSnapshot(matching, posting, linkedPaymentCount, now, unmatchedReason);
        return record;
    }

    private async Task<IReadOnlyList<BankReconciliationCandidatePaymentDto>> LoadCandidatesAsync(BankTransaction transaction, CancellationToken cancellationToken)
    {
        var type = transaction.Amount > 0 ? PaymentTypes.Incoming : PaymentTypes.Outgoing;
        var payments = await _dbContext.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == transaction.CompanyId && x.Status == PaymentStatuses.Completed && x.PaymentType == type && x.Currency == transaction.Currency)
            .OrderByDescending(x => x.PaymentDate).Take(50).ToListAsync(cancellationToken);
        var ids = payments.Select(x => x.Id).ToArray();
        var linked = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == transaction.CompanyId && ids.Contains(x.PaymentId)).GroupBy(x => x.PaymentId)
            .Select(x => new { Id = x.Key, Amount = x.Sum(y => y.AllocatedAmount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken);
        var allocations = await _dbContext.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Invoice).Include(x => x.Bill).Where(x => x.CompanyId == transaction.CompanyId && ids.Contains(x.PaymentId))
            .ToListAsync(cancellationToken);
        return payments.Select(payment =>
        {
            var used = linked.GetValueOrDefault(payment.Id);
            var allocation = allocations.FirstOrDefault(x => x.PaymentId == payment.Id);
            return new BankReconciliationCandidatePaymentDto(payment.Id, payment.PaymentType, payment.Amount, used,
                Math.Max(0, payment.Amount - used), payment.Currency, payment.PaymentDate, payment.CounterpartyReference,
                allocation?.InvoiceId, allocation?.Invoice?.InvoiceNumber, allocation?.BillId, allocation?.Bill?.BillNumber);
        }).Where(x => x.AvailableAmount > 0 || transaction.PaymentLinks.Any(link => link.PaymentId == x.PaymentId)).ToArray();
    }

    private async Task EnsureActiveMemberAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || !await _dbContext.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.UserId == actorUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company member is required for this bank action.");
    }

    private async Task AuditReconciliationAsync(
        ReconcileBankTransactionCommand command,
        BankTransaction transaction,
        string mode,
        decimal reconciledAmount,
        Guid? ledgerEntryId,
        DateTime occurredUtc,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(
            command.CompanyId,
            AuditActorTypes.User,
            command.ActorUserId,
            AuditEventActions.AccountingBankReconciliationReviewed,
            "bank_transaction",
            transaction.Id.ToString("N"),
            AuditEventOutcomes.Succeeded,
            ledgerEntryId.HasValue
                ? "A reviewed bank reconciliation was saved with a governed journal link."
                : "A reviewed bank reconciliation was saved without posting an unresolved remainder.",
            ["bank_transaction", "payment", "accounting_journal"],
            new Dictionary<string, string?>
            {
                ["handlingMode"] = mode,
                ["reconciledAmount"] = reconciledAmount.ToString("0.00", CultureInfo.InvariantCulture),
                ["remainingAmount"] = Math.Max(0m, transaction.AbsoluteAmount - reconciledAmount).ToString("0.00", CultureInfo.InvariantCulture),
                ["ledgerEntryId"] = ledgerEntryId?.ToString("N"),
                ["reviewReason"] = command.ReviewReason
            },
            command.CorrelationId,
            occurredUtc), cancellationToken);
    }

    private static bool IsBalancedWithAdjustments(
        BankTransaction transaction,
        decimal allocated,
        IReadOnlyCollection<BankReconciliationAdjustmentDto> adjustments)
    {
        var adjustmentDebits = adjustments.Sum(x => NormalizeAdjustmentAmount(x.DebitAmount));
        var adjustmentCredits = adjustments.Sum(x => NormalizeAdjustmentAmount(x.CreditAmount));
        var debit = transaction.Amount > 0m
            ? transaction.AbsoluteAmount + adjustmentDebits
            : allocated + adjustmentDebits;
        var credit = transaction.Amount > 0m
            ? allocated + adjustmentCredits
            : transaction.AbsoluteAmount + adjustmentCredits;
        return NormalizeMoney(debit) == NormalizeMoney(credit);
    }

    private static void EnsureReviewed(ReconcileBankTransactionCommand command)
    {
        if (command.ActorUserId == Guid.Empty || string.IsNullOrWhiteSpace(command.ReviewReason))
            throw Validation(nameof(command.ReviewReason), "A reviewed handling choice and reason are required.");
    }

    private static BankTransactionDto MapList(BankTransaction x, CompanyBankAccount account) => new(x.Id, x.CompanyId,
        x.BankAccountId, account.DisplayName, account.BankName, account.MaskedAccountNumber, x.BookingDate, x.ValueDate,
        x.Amount, x.Currency, x.ReferenceText, x.Counterparty, x.Status, x.ReconciledAmount, x.ExternalReference, MapBankAccount(account));

    private static CompanyBankAccountDto MapBankAccount(CompanyBankAccount x) => new(x.Id, x.CompanyId, x.FinanceAccountId,
        x.FinanceAccount.Name, x.DisplayName, x.BankName, x.MaskedAccountNumber, x.Currency, x.ExternalCode, x.IsPrimary,
        x.IsActive, x.CreatedUtc, x.UpdatedUtc);

    private static BankReconciliationItemDto MapReconciliationItem(BankTransaction x, bool hasOpenFollowUp)
    {
        var ledgerId = x.CashLedgerLinks.OrderByDescending(y => y.CreatedUtc).Select(y => (Guid?)y.LedgerEntryId).FirstOrDefault();
        return new(x.Id, x.BookingDate, x.Amount, x.Currency, x.Counterparty, x.ReferenceText, x.BankAccount.DisplayName,
            ResolveState(x, hasOpenFollowUp), x.ReconciledAmount, Math.Max(0, x.AbsoluteAmount - x.ReconciledAmount),
            x.PaymentLinks.Count, x.SourceVersion, x.PostingStateRecord?.ConflictCode, x.PostingStateRecord?.ConflictDetails, ledgerId);
    }

    private static string ResolveState(BankTransaction x, bool hasOpenFollowUp) =>
        !string.IsNullOrWhiteSpace(x.PostingStateRecord?.ConflictCode) ? "conflict" :
        x.PostingStateRecord?.PostingState == BankTransactionPostingStates.Corrected ? "correction" :
        hasOpenFollowUp || x.PostingStateRecord?.PostingState == BankTransactionPostingStates.Suspense ? "suspense" :
        x.CashLedgerLinks.Count > 0 ? "posted" :
        x.ReconciledAmount >= x.AbsoluteAmount ? "matched" :
        x.ReconciledAmount > 0 ? "partial" : "unmatched";

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContextAccessor?.CompanyId is Guid current && current != companyId)
            throw new UnauthorizedAccessException("Bank transaction operations are scoped to the active company context.");
    }

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null) return await action();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private static string ComputeRowHash(ImportBankStatementRowDto row)
    {
        var canonical = string.Join("|", NormalizeUtc(row.BookingDateUtc).ToString("O"), NormalizeUtc(row.ValueDateUtc).ToString("O"),
            row.Amount.ToString("0.00", CultureInfo.InvariantCulture), row.Currency.Trim().ToUpperInvariant(), row.ReferenceText.Trim(),
            row.Counterparty.Trim(), row.ExternalReference?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string BuildBankLedgerLinkIdentity(Guid companyId, Guid bankTransactionId, Guid ledgerEntryId) =>
        $"bank-link:{companyId:N}:{bankTransactionId:N}:{ledgerEntryId:N}";

    private static string NormalizeHandlingMode(string value) => value?.Trim().ToLowerInvariant() switch
    {
        BankReconciliationHandlingModes.Payment => BankReconciliationHandlingModes.Payment,
        BankReconciliationHandlingModes.Categorization => BankReconciliationHandlingModes.Categorization,
        BankReconciliationHandlingModes.Suspense => BankReconciliationHandlingModes.Suspense,
        BankReconciliationHandlingModes.LeaveUnmatched => BankReconciliationHandlingModes.LeaveUnmatched,
        _ => throw Validation(nameof(value), "Select how this bank transaction should be handled.")
    };

    private static string NormalizeState(string value) => value.Trim().ToLowerInvariant() switch
    {
        "unmatched" or "partial" or "matched" or "posted" or "suspense" or "conflict" or "correction" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("Unsupported reconciliation state.", nameof(value))
    };

    private static string? NormalizeOptionalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var status = BankTransactionReconciliationStatuses.Normalize(value);
        return BankTransactionReconciliationStatuses.IsSupported(status) ? status : throw new ArgumentException("Unsupported bank transaction status.", nameof(value));
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    private static string RequiredHash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentException($"{name} must be a SHA-256 hex value.", name);
    }

    private static int NormalizeLimit(int value) => value <= 0 ? DefaultLimit : Math.Min(value, MaxLimit);
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static decimal NormalizeMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal NormalizeAdjustmentAmount(decimal value) => value >= 0m
        ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
        : throw Validation(nameof(BankReconciliationAdjustmentDto), "Bank adjustment amounts cannot be negative.");
    private static FinanceValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] }, message);

    private sealed record PaymentCashPostingReuse(
        IReadOnlyList<ProposedAccountingLine> ResidualLines,
        IReadOnlyList<Guid> LedgerEntryIds);
}
