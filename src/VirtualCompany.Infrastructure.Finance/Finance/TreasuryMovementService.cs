using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class TreasuryMovementService : ITreasuryMovementReadService, ITreasuryMovementCommandService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPostingService _postingService;
    private readonly IAccountingAccountRoleResolver _accountRoleResolver;
    private readonly IAuditEventWriter _audit;
    private readonly ICompanyContextAccessor? _companyContext;
    private readonly TreasuryMovementTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;

    public TreasuryMovementService(VirtualCompanyDbContext dbContext, IAccountingPostingService postingService,
        IAccountingAccountRoleResolver accountRoleResolver, IAuditEventWriter audit,
        ICompanyContextAccessor? companyContext, TreasuryMovementTelemetry telemetry, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _postingService = postingService; _accountRoleResolver = accountRoleResolver;
        _audit = audit; _companyContext = companyContext; _telemetry = telemetry; _timeProvider = timeProvider;
    }

    public async Task<TreasurySourceListDto> ListAsync(ListTreasurySourcesQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 500);
        var status = NormalizeOptional(query.Status);
        var transfers = await _dbContext.TreasuryTransfers.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (status == null || x.Status == status) &&
                (!query.BankTransactionId.HasValue || x.OutboundBankTransactionId == query.BankTransactionId || x.InboundBankTransactionId == query.BankTransactionId))
            .OrderByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var adjustments = await _dbContext.BankAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (status == null || x.Status == status) &&
                (!query.BankTransactionId.HasValue || x.BankTransactionId == query.BankTransactionId))
            .OrderByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var cards = await _dbContext.CardSettlements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (status == null || x.Status == status) &&
                (!query.BankTransactionId.HasValue || x.BankTransactionId == query.BankTransactionId))
            .OrderByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var payouts = await _dbContext.PayoutSettlements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (status == null || x.Status == status) &&
                (!query.BankTransactionId.HasValue || x.BankTransactionId == query.BankTransactionId))
            .OrderByDescending(x => x.UpdatedUtc).Take(limit).ToListAsync(cancellationToken);
        var items = transfers.Select(MapSummary).Concat(adjustments.Select(MapSummary)).Concat(cards.Select(MapSummary))
            .Concat(payouts.Select(MapSummary)).OrderByDescending(x => x.UpdatedUtc).Take(limit).ToArray();
        return new(items,
            items.Count(x => x.Status is TreasuryMovementStatuses.NeedsReview or TreasuryMovementStatuses.AwaitingBankEvidence or TreasuryMovementStatuses.AwaitingApproval),
            items.Count(x => x.Status == TreasuryMovementStatuses.InTransit),
            items.Count(x => x.Status == TreasuryMovementStatuses.ReadyToPost),
            items.Count(x => x.Status == TreasuryMovementStatuses.Posted));
    }

    public async Task<TreasurySourceDetailDto?> GetAsync(GetTreasurySourceQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var state = await LoadStateAsync(query.CompanyId, NormalizeSourceType(query.SourceType), query.SourceId, false, cancellationToken);
        return state is null ? null : await MapDetailAsync(state, null, cancellationToken);
    }

    public Task<TreasurySourceDetailDto> CreateTransferAsync(CreateTreasuryTransferCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); await EnsureIdentityAvailableAsync(command.CompanyId, TreasurySourceTypes.AccountTransfer, command.SourceIdentity, cancellationToken);
            var from = await LoadBankAccountAsync(command.CompanyId, command.FromBankAccountId, cancellationToken);
            var to = await LoadBankAccountAsync(command.CompanyId, command.ToBankAccountId, cancellationToken);
            var currency = Currency(command.Currency);
            if (from.Currency != currency || to.Currency != currency)
                throw Block(TreasuryMovementReasonCodes.CrossCurrencyTransferBlocked, "Cross-currency transfers remain blocked until governed currency accounting is enabled.");
            if (command.CorrectionOfTransferId.HasValue)
                await EnsureCorrectionExistsAsync<TreasuryTransfer>(command.CompanyId, command.CorrectionOfTransferId.Value, cancellationToken);
            if (command.FeeAmount > 0m) await LoadPostingAccountAsync(command.CompanyId, command.FeeFinanceAccountId ?? Guid.Empty, currency, FinanceAccountClassValues.Expense, cancellationToken);
            var now = UtcNow(); var source = new TreasuryTransfer(Guid.NewGuid(), command.CompanyId, command.SourceIdentity,
                from.Id, to.Id, command.Amount, command.FeeAmount, currency, command.FeeFinanceAccountId,
                command.MaterialityThreshold, command.CorrectionOfTransferId, command.ActorUserId, now);
            _dbContext.TreasuryTransfers.Add(source);
            if (command.OutboundBankTransactionId.HasValue)
                await AttachTransferLegAsync(source, command.OutboundBankTransactionId.Value, TreasuryTransferLegRoles.Outbound, command.ActorUserId, now, cancellationToken);
            if (command.InboundBankTransactionId.HasValue)
                await AttachTransferLegAsync(source, command.InboundBankTransactionId.Value, TreasuryTransferLegRoles.Inbound, command.ActorUserId, now, cancellationToken);
            AddEvidence(command.CompanyId, TreasurySourceTypes.AccountTransfer, source.Id, command.Evidence, now);
            await RecordEventAndAuditAsync(command.CompanyId, TreasurySourceTypes.AccountTransfer, source.Id, "created", command.ActorUserId,
                null, "{}", Snapshot(source), AuditEventActions.AccountingTreasurySourceCreated, source.Status, command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken); _telemetry.Created(TreasurySourceTypes.AccountTransfer, source.Status);
            return await MapDetailAsync(State(source), null, cancellationToken);
        }, cancellationToken);

    public Task<TreasurySourceDetailDto> CreateBankAdjustmentAsync(CreateBankAdjustmentCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); await EnsureIdentityAvailableAsync(command.CompanyId, TreasurySourceTypes.BankAdjustment, command.SourceIdentity, cancellationToken);
            var kind = BankAdjustmentKinds.Normalize(command.AdjustmentKind);
            if (!BankAdjustmentKinds.IsSupported(kind)) throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, "The bank adjustment kind is not supported.");
            var bank = await LoadBankAccountAsync(command.CompanyId, command.BankAccountId, cancellationToken); var currency = Currency(command.Currency);
            if (bank.Currency != currency) throw CrossCurrency();
            var expectedClass = BankAdjustmentKinds.IsIncome(kind) ? FinanceAccountClassValues.Income : FinanceAccountClassValues.Expense;
            await LoadPostingAccountAsync(command.CompanyId, command.CounterpartFinanceAccountId, currency, expectedClass, cancellationToken);
            if (command.CorrectionOfAdjustmentId.HasValue) await EnsureCorrectionExistsAsync<BankAdjustment>(command.CompanyId, command.CorrectionOfAdjustmentId.Value, cancellationToken);
            var transaction = await LoadBankTransactionAsync(command.CompanyId, command.BankTransactionId, cancellationToken);
            ValidateBankAdjustmentEvidence(bank.Id, transaction, kind, command.Amount, currency);
            await EnsureBankTransactionAvailableAsync(command.CompanyId, command.BankTransactionId, null, cancellationToken);
            var now = UtcNow(); var source = new BankAdjustment(Guid.NewGuid(), command.CompanyId, command.SourceIdentity,
                kind, bank.Id, transaction.Id, command.CounterpartFinanceAccountId, command.Amount, currency,
                command.Description, command.MaterialityThreshold, command.CorrectionOfAdjustmentId, command.ActorUserId, now);
            _dbContext.BankAdjustments.Add(source); AddEvidence(command.CompanyId, TreasurySourceTypes.BankAdjustment, source.Id, command.Evidence, now);
            AddBankEvidence(command.CompanyId, TreasurySourceTypes.BankAdjustment, source.Id, transaction, TreasuryEvidenceTypes.BankTransaction, now);
            await RecordEventAndAuditAsync(command.CompanyId, TreasurySourceTypes.BankAdjustment, source.Id, "created", command.ActorUserId,
                source.ReasonCode, "{}", Snapshot(source), AuditEventActions.AccountingTreasurySourceCreated, source.Status, command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken); _telemetry.Created(TreasurySourceTypes.BankAdjustment, source.Status);
            return await MapDetailAsync(State(source), null, cancellationToken);
        }, cancellationToken);

    public Task<TreasurySourceDetailDto> CreateCardSettlementAsync(CreateCardSettlementCommand command, CancellationToken cancellationToken) =>
        CreateSettlementAsync(command.CompanyId, TreasurySourceTypes.CardSettlement, command.SourceIdentity,
            command.ProviderBatchReference, command.BankAccountId, command.ReceivableFinanceAccountId,
            command.GrossAmount, command.FeeAmount, command.NetAmount, command.Currency, command.MaterialityThreshold,
            command.CorrectionOfSettlementId, command.BankTransactionId, command.Evidence, command.ActorUserId,
            command.CorrelationId, cancellationToken);

    public Task<TreasurySourceDetailDto> CreatePayoutSettlementAsync(CreatePayoutSettlementCommand command, CancellationToken cancellationToken) =>
        CreateSettlementAsync(command.CompanyId, TreasurySourceTypes.PayoutSettlement, command.SourceIdentity,
            command.ProviderBatchReference, command.BankAccountId, command.PayoutClearingFinanceAccountId,
            command.GrossAmount, command.FeeAmount, command.NetAmount, command.Currency, command.MaterialityThreshold,
            command.CorrectionOfSettlementId, command.BankTransactionId, command.Evidence, command.ActorUserId,
            command.CorrelationId, cancellationToken);

    public Task<TreasurySourceDetailDto> LinkBankEvidenceAsync(LinkTreasuryBankEvidenceCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeSourceType(command.SourceType);
            var state = await LoadStateAsync(command.CompanyId, type, command.SourceId, true, cancellationToken)
                ?? throw new KeyNotFoundException("Treasury source was not found.");
            if (state.BankTransactions.Any(x => x.Id == command.BankTransactionId)) return await MapDetailAsync(state, null, cancellationToken);
            if (state.Version != command.ExpectedVersion) throw Conflict();
            if (state.ApprovalRequestId.HasValue)
                throw Block(TreasuryMovementReasonCodes.InvalidLifecycleState, "Approved treasury evidence is immutable. Create a correction source for changed evidence.");
            var transaction = await LoadBankTransactionAsync(command.CompanyId, command.BankTransactionId, cancellationToken);
            await EnsureBankTransactionAvailableAsync(command.CompanyId, transaction.Id, state.Id, cancellationToken);
            var before = Snapshot(state.Entity); var now = UtcNow();
            switch (state.Entity)
            {
                case TreasuryTransfer transfer:
                    await AttachTransferLegAsync(transfer, transaction.Id, command.TransferLegRole ?? string.Empty, command.ActorUserId, now, cancellationToken);
                    break;
                case CardSettlement card:
                    ValidateSettlementBankAccount(card.BankAccountId, card.Currency, transaction);
                    card.LinkBankEvidence(command.ExpectedVersion, transaction.Id, Money(Math.Abs(transaction.Amount)) == card.NetAmount, command.ActorUserId, now);
                    break;
                case PayoutSettlement payout:
                    ValidateSettlementBankAccount(payout.BankAccountId, payout.Currency, transaction);
                    payout.LinkBankEvidence(command.ExpectedVersion, transaction.Id, Money(Math.Abs(transaction.Amount)) == payout.NetAmount, command.ActorUserId, now);
                    break;
                default: throw Block(TreasuryMovementReasonCodes.InvalidLifecycleState, "This treasury source already owns fixed bank evidence.");
            }
            var refreshed = State(state.Entity); AddBankEvidence(command.CompanyId, type, state.Id, transaction,
                command.TransferLegRole ?? TreasuryEvidenceTypes.BankTransaction, now);
            await RecordEventAndAuditAsync(command.CompanyId, type, state.Id, "bank_evidence_linked", command.ActorUserId,
                refreshed.ReasonCode, before, Snapshot(state.Entity), AuditEventActions.AccountingTreasuryBankEvidenceLinked,
                refreshed.Status, command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await MapDetailAsync(refreshed, null, cancellationToken);
        }, cancellationToken);

    public Task<TreasurySourceDetailDto> BindApprovalAsync(BindTreasuryApprovalCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeSourceType(command.SourceType);
            var state = await LoadStateAsync(command.CompanyId, type, command.SourceId, true, cancellationToken)
                ?? throw new KeyNotFoundException("Treasury source was not found.");
            if (state.ApprovalRequestId == command.ApprovalRequestId) return await MapDetailAsync(state, null, cancellationToken);
            if (!state.RequiresApproval) throw Block(TreasuryMovementReasonCodes.InvalidLifecycleState, "This source does not require material approval.");
            if (state.Status != TreasuryMovementStatuses.AwaitingApproval)
                throw Block(TreasuryMovementReasonCodes.InvalidLifecycleState, "Complete and reconcile treasury evidence before binding approval.");
            await EnsureApprovedTreasuryRequestAsync(state, command.ApprovalRequestId, cancellationToken);
            var before = Snapshot(state.Entity); var now = UtcNow();
            switch (state.Entity)
            {
                case TreasuryTransfer x: x.BindApproval(command.ExpectedVersion, command.ApprovalRequestId, command.ActorUserId, now); break;
                case BankAdjustment x: x.BindApproval(command.ExpectedVersion, command.ApprovalRequestId, command.ActorUserId, now); break;
                case CardSettlement x: x.BindApproval(command.ExpectedVersion, command.ApprovalRequestId, command.ActorUserId, now); break;
                case PayoutSettlement x: x.BindApproval(command.ExpectedVersion, command.ApprovalRequestId, command.ActorUserId, now); break;
            }
            var refreshed = State(state.Entity);
            await RecordEventAndAuditAsync(command.CompanyId, type, state.Id, "approval_bound", command.ActorUserId,
                null, before, Snapshot(state.Entity), AuditEventActions.AccountingTreasuryApprovalBound, refreshed.Status,
                command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await MapDetailAsync(refreshed, null, cancellationToken);
        }, cancellationToken);

    public async Task<TreasuryPostingPreviewDto> PreviewAsync(PreviewTreasuryPostingCommand command, CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeSourceType(command.SourceType);
        var state = await LoadStateAsync(command.CompanyId, type, command.SourceId, false, cancellationToken)
            ?? throw new KeyNotFoundException("Treasury source was not found.");
        if (state.Status is not (TreasuryMovementStatuses.ReadyToPost or TreasuryMovementStatuses.AwaitingApproval))
            return BlockedPreview(state);
        var built = await BuildEntryAsync(state, command.FiscalPeriodId, command.PostingDate, command.ActorUserId, false, cancellationToken);
        var accounting = await _postingService.PreviewNonAuthoritativeCandidateAsync(new(built.Entry), cancellationToken);
        var approvalBlocked = state.RequiresApproval && !state.ApprovalRequestId.HasValue;
        return new(accounting.IsValid && !approvalBlocked,
            approvalBlocked ? TreasuryMovementReasonCodes.ApprovalRequired : accounting.Issues.FirstOrDefault()?.ReasonCode,
            approvalBlocked ? "Material treasury sources require an approved review before posting." : accounting.Issues.FirstOrDefault()?.Explanation,
            accounting, built.Lines);
    }

    public Task<TreasurySourceDetailDto> PostAsync(PostTreasurySourceCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeSourceType(command.SourceType);
            var state = await LoadStateAsync(command.CompanyId, type, command.SourceId, true, cancellationToken)
                ?? throw new KeyNotFoundException("Treasury source was not found.");
            if (state.Status == TreasuryMovementStatuses.Posted) return await MapDetailAsync(state, null, cancellationToken);
            if (state.Version != command.ExpectedVersion) throw Conflict();
            if (state.Status != TreasuryMovementStatuses.ReadyToPost)
            { _telemetry.Blocked(type, state.ReasonCode ?? TreasuryMovementReasonCodes.InvalidLifecycleState); throw Block(state.ReasonCode ?? TreasuryMovementReasonCodes.InvalidLifecycleState, Explain(state)); }
            var built = await BuildEntryAsync(state, command.FiscalPeriodId, command.PostingDate, command.ActorUserId, true, cancellationToken);
            var accountingPreview = await _postingService.PreviewAsync(new(built.Entry), cancellationToken);
            var posted = await _postingService.PostAsync(new(built.Entry, command.CorrelationId), cancellationToken);
            var now = UtcNow(); var before = Snapshot(state.Entity);
            MarkPosted(state.Entity, command.ExpectedVersion, command.ActorUserId, now);
            if (!await _dbContext.TreasurySourceLedgerLinks.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == command.CompanyId && x.SourceType == type && x.SourceId == state.Id && x.LinkRole == TreasuryLedgerLinkRoles.Posting, cancellationToken))
                _dbContext.TreasurySourceLedgerLinks.Add(new(Guid.NewGuid(), command.CompanyId, type, state.Id, posted.Journal.Id, TreasuryLedgerLinkRoles.Posting, now));
            await RecordEventAndAuditAsync(command.CompanyId, type, state.Id, "posted", command.ActorUserId, null,
                before, Snapshot(state.Entity), AuditEventActions.AccountingTreasurySourcePosted, TreasuryMovementStatuses.Posted,
                command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken); _telemetry.Posted(type);
            return await MapDetailAsync(State(state.Entity), new(true, null, null, accountingPreview, built.Lines), cancellationToken);
        }, cancellationToken);

    public Task<TreasurySourceDetailDto> ReverseAsync(ReverseTreasurySourceCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureCommand(command.CompanyId, command.ActorUserId); var type = NormalizeSourceType(command.SourceType);
            var state = await LoadStateAsync(command.CompanyId, type, command.SourceId, true, cancellationToken)
                ?? throw new KeyNotFoundException("Treasury source was not found.");
            if (state.Status == TreasuryMovementStatuses.Reversed) return await MapDetailAsync(state, null, cancellationToken);
            if (state.Version != command.ExpectedVersion) throw Conflict();
            var original = await _dbContext.TreasurySourceLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceType == type && x.SourceId == state.Id && x.LinkRole == TreasuryLedgerLinkRoles.Posting, cancellationToken)
                ?? throw Block(TreasuryMovementReasonCodes.InvalidLifecycleState, "The original treasury journal is missing.");
            var reversed = await _postingService.ReverseAsync(new(command.CompanyId, original.LedgerEntryId,
                command.FiscalPeriodId, "B", command.PostingDate, command.Reason,
                $"{state.Version}:reversal", $"treasury:{command.CompanyId:N}:{type}:{state.Id:N}:reverse:{state.Version}",
                command.ActorUserId, null, command.CorrelationId), cancellationToken);
            var before = Snapshot(state.Entity); var now = UtcNow(); MarkReversed(state.Entity, command.ExpectedVersion, command.ActorUserId, now);
            _dbContext.TreasurySourceLedgerLinks.Add(new(Guid.NewGuid(), command.CompanyId, type, state.Id,
                reversed.Journal.Id, TreasuryLedgerLinkRoles.Reversal, now));
            await RecordEventAndAuditAsync(command.CompanyId, type, state.Id, "reversed", command.ActorUserId, null,
                before, Snapshot(state.Entity), AuditEventActions.AccountingTreasurySourceReversed, TreasuryMovementStatuses.Reversed,
                command.CorrelationId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken); _telemetry.Reversed(type);
            return await MapDetailAsync(State(state.Entity), null, cancellationToken);
        }, cancellationToken);

    private Task<TreasurySourceDetailDto> CreateSettlementAsync(Guid companyId, string type, string sourceIdentity,
        string providerReference, Guid bankAccountId, Guid controlAccountId, decimal gross, decimal fee, decimal net,
        string currencyValue, decimal threshold, Guid? correctionId, Guid? bankTransactionId,
        IReadOnlyList<TreasuryEvidenceInputDto> evidence, Guid actorUserId, string? correlationId,
        CancellationToken cancellationToken) => ExecuteInTransactionAsync(async () =>
    {
        EnsureCommand(companyId, actorUserId); if (evidence.Count == 0) throw Block(TreasuryMovementReasonCodes.BankEvidenceMissing, "Provider or merchant settlement evidence is required.");
        await EnsureIdentityAvailableAsync(companyId, type, sourceIdentity, cancellationToken); var currency = Currency(currencyValue);
        var bank = await LoadBankAccountAsync(companyId, bankAccountId, cancellationToken); if (bank.Currency != currency) throw CrossCurrency();
        await LoadPostingAccountAsync(companyId, controlAccountId, currency, FinanceAccountClassValues.Asset, cancellationToken);
        if (correctionId.HasValue)
        {
            if (type == TreasurySourceTypes.CardSettlement) await EnsureCorrectionExistsAsync<CardSettlement>(companyId, correctionId.Value, cancellationToken);
            else await EnsureCorrectionExistsAsync<PayoutSettlement>(companyId, correctionId.Value, cancellationToken);
        }
        var now = UtcNow(); object entity = type == TreasurySourceTypes.CardSettlement
            ? new CardSettlement(Guid.NewGuid(), companyId, sourceIdentity, providerReference, bank.Id, controlAccountId,
                gross, fee, net, currency, threshold, correctionId, actorUserId, now)
            : new PayoutSettlement(Guid.NewGuid(), companyId, sourceIdentity, providerReference, bank.Id, controlAccountId,
                gross, fee, net, currency, threshold, correctionId, actorUserId, now);
        _dbContext.Add(entity); var state = State(entity); AddEvidence(companyId, type, state.Id, evidence, now);
        if (bankTransactionId.HasValue)
        {
            var transaction = await LoadBankTransactionAsync(companyId, bankTransactionId.Value, cancellationToken);
            await EnsureBankTransactionAvailableAsync(companyId, transaction.Id, null, cancellationToken); ValidateSettlementBankAccount(bank.Id, currency, transaction);
            var matches = Money(Math.Abs(transaction.Amount)) == Money(net);
            if (entity is CardSettlement card) card.LinkBankEvidence(card.Version, transaction.Id, matches, actorUserId, now);
            else ((PayoutSettlement)entity).LinkBankEvidence(((PayoutSettlement)entity).Version, transaction.Id, matches, actorUserId, now);
            AddBankEvidence(companyId, type, state.Id, transaction, TreasuryEvidenceTypes.BankTransaction, now);
        }
        state = State(entity);
        await RecordEventAndAuditAsync(companyId, type, state.Id, "created", actorUserId, state.ReasonCode,
            "{}", Snapshot(entity), AuditEventActions.AccountingTreasurySourceCreated, state.Status, correlationId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken); _telemetry.Created(type, state.Status);
        return await MapDetailAsync(state, null, cancellationToken);
    }, cancellationToken);

    private async Task AttachTransferLegAsync(TreasuryTransfer transfer, Guid transactionId, string legRole,
        Guid actor, DateTime now, CancellationToken cancellationToken)
    {
        var role = TreasuryTransferLegRoles.Normalize(legRole); var transaction = await LoadBankTransactionAsync(transfer.CompanyId, transactionId, cancellationToken);
        await EnsureBankTransactionAvailableAsync(transfer.CompanyId, transaction.Id, transfer.Id, cancellationToken);
        var expectedAccount = role == TreasuryTransferLegRoles.Outbound ? transfer.FromBankAccountId : transfer.ToBankAccountId;
        if (transaction.BankAccountId != expectedAccount || transaction.Currency != transfer.Currency)
            throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, "The transfer leg does not belong to the expected account and currency.");
        var expectedAmount = role == TreasuryTransferLegRoles.Outbound ? transfer.Amount + transfer.FeeAmount : transfer.Amount;
        var validDirection = role == TreasuryTransferLegRoles.Outbound ? transaction.Amount < 0m : transaction.Amount > 0m;
        if (!validDirection || Money(Math.Abs(transaction.Amount)) != Money(expectedAmount))
            throw Block(TreasuryMovementReasonCodes.BankAmountMismatch, "The bank leg direction or amount does not match the governed transfer.");
        transfer.AttachBankLeg(transfer.Version, role, transaction.Id, actor, now);
        AddBankEvidence(transfer.CompanyId, TreasurySourceTypes.AccountTransfer, transfer.Id, transaction, role, now);
    }

    private async Task<BuiltEntry> BuildEntryAsync(SourceState state, Guid fiscalPeriodId, DateOnly postingDate,
        Guid actorUserId, bool final, CancellationToken cancellationToken)
    {
        if (fiscalPeriodId == Guid.Empty) throw Validation(nameof(fiscalPeriodId), "An open accounting period is required.");
        var lines = new List<ProposedAccountingLine>(); var display = new List<TreasuryPostingLineDto>();
        async Task Add(Guid accountId, decimal debit, decimal credit, string description)
        {
            var account = await LoadPostingAccountAsync(state.CompanyId, accountId, state.Currency, null, cancellationToken);
            lines.Add(new(account.Id, debit, credit, state.Currency, description)); display.Add(new(account.Id, account.Code, account.Name, debit, credit, state.Currency, description));
        }
        switch (state.Entity)
        {
            case TreasuryTransfer x:
                var from = await LoadBankAccountAsync(state.CompanyId, x.FromBankAccountId, cancellationToken); var to = await LoadBankAccountAsync(state.CompanyId, x.ToBankAccountId, cancellationToken);
                await Add(to.FinanceAccountId, x.Amount, 0m, $"Internal transfer {x.SourceIdentity}");
                if (x.FeeAmount > 0m) await Add(x.FeeFinanceAccountId!.Value, x.FeeAmount, 0m, $"Transfer fee {x.SourceIdentity}");
                await Add(from.FinanceAccountId, 0m, x.Amount + x.FeeAmount, $"Internal transfer {x.SourceIdentity}"); break;
            case BankAdjustment x:
                var adjustmentBank = await LoadBankAccountAsync(state.CompanyId, x.BankAccountId, cancellationToken);
                if (BankAdjustmentKinds.IsIncome(x.AdjustmentKind)) { await Add(adjustmentBank.FinanceAccountId, x.Amount, 0m, x.Description); await Add(x.CounterpartFinanceAccountId, 0m, x.Amount, x.Description); }
                else { await Add(x.CounterpartFinanceAccountId, x.Amount, 0m, x.Description); await Add(adjustmentBank.FinanceAccountId, 0m, x.Amount, x.Description); } break;
            case CardSettlement x:
                await AddSettlementLinesAsync(x.BankAccountId, x.ReceivableFinanceAccountId, x.GrossAmount, x.FeeAmount, x.NetAmount, $"Card settlement {x.ProviderBatchReference}"); break;
            case PayoutSettlement x:
                await AddSettlementLinesAsync(x.BankAccountId, x.PayoutClearingFinanceAccountId, x.GrossAmount, x.FeeAmount, x.NetAmount, $"Payout settlement {x.ProviderBatchReference}"); break;
        }
        async Task AddSettlementLinesAsync(Guid bankAccountId, Guid controlAccountId, decimal gross, decimal fee, decimal net, string description)
        {
            var bank = await LoadBankAccountAsync(state.CompanyId, bankAccountId, cancellationToken); await Add(bank.FinanceAccountId, net, 0m, description);
            if (fee > 0m) { var feeAccount = await _accountRoleResolver.ResolveRequiredAsync(state.CompanyId, AccountingAccountRoleKeys.BankFee, cancellationToken); await Add(feeAccount.FinanceAccountId, fee, 0m, description); }
            await Add(controlAccountId, 0m, gross, description);
        }
        var descriptionText = state.DisplayName; var sourceVersion = state.Version.ToString(CultureInfo.InvariantCulture);
        var entry = new ProposedAccountingEntry(state.CompanyId, fiscalPeriodId, "B", postingDate, postingDate,
            state.Entity is BankAdjustment ? LedgerPostingTypeValues.Adjustment : LedgerPostingTypeValues.CashSettlement,
            descriptionText, state.Type, state.Id.ToString("N"), sourceVersion,
            $"treasury:{state.CompanyId:N}:{state.Type}:{state.Id:N}:post:{sourceVersion}", lines, actorUserId,
            final ? state.ApprovalRequestId : null, final && state.RequiresApproval,
            new Dictionary<string, string> { ["sourceIdentity"] = state.SourceIdentity, ["grossAmount"] = state.Gross.ToString("0.00", CultureInfo.InvariantCulture), ["feeAmount"] = state.Fee.ToString("0.00", CultureInfo.InvariantCulture), ["netAmount"] = state.Net.ToString("0.00", CultureInfo.InvariantCulture) },
            ApprovalPayloadHash: null);
        return new(entry, display);
    }

    private async Task<TreasurySourceDetailDto> MapDetailAsync(SourceState state, TreasuryPostingPreviewDto? preview,
        CancellationToken cancellationToken)
    {
        var transactionIds = state.BankTransactions.Select(x => x.Id).Distinct().ToArray();
        var transactionRows = transactionIds.Length == 0 ? [] : await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == state.CompanyId && transactionIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var bankEvidence = state.BankTransactions.Select(link =>
        {
            var row = transactionRows.Single(x => x.Id == link.Id);
            return new TreasuryBankEvidenceDto(row.Id, link.Role, row.BookingDate, row.Amount, row.Currency, row.ReferenceText, row.Counterparty);
        }).ToArray();
        var evidence = await _dbContext.TreasurySourceEvidence.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == state.CompanyId && x.SourceType == state.Type && x.SourceId == state.Id).OrderBy(x => x.CreatedUtc)
            .Select(x => new TreasuryEvidenceDto(x.Id, x.EvidenceType, x.Reference, x.ContentHash, x.Description, x.CreatedUtc)).ToListAsync(cancellationToken);
        var ledgers = await (from link in _dbContext.TreasurySourceLedgerLinks.IgnoreQueryFilters().AsNoTracking()
            join entry in _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking() on new { link.CompanyId, Id = link.LedgerEntryId } equals new { entry.CompanyId, entry.Id }
            where link.CompanyId == state.CompanyId && link.SourceType == state.Type && link.SourceId == state.Id
            orderby link.CreatedUtc select new TreasuryLedgerLinkDto(link.LedgerEntryId, entry.EntryNumber ?? "Pending journal", link.LinkRole, link.CreatedUtc)).ToListAsync(cancellationToken);
        var history = await _dbContext.TreasurySourceEvents.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == state.CompanyId && x.SourceType == state.Type && x.SourceId == state.Id).OrderBy(x => x.CreatedUtc)
            .Select(x => new TreasurySourceEventDto(x.Id, x.Action, x.ActorUserId, x.ReasonCode, x.BeforeJson, x.AfterJson, x.CreatedUtc)).ToListAsync(cancellationToken);
        return new(MapSummary(state), state.FromBankAccountId, state.ToBankAccountId, state.BankAccountId,
            state.CounterpartFinanceAccountId, state.CorrectionOfSourceId, bankEvidence, evidence, ledgers, history,
            Allowed(state), preview);
    }

    private async Task<SourceState?> LoadStateAsync(Guid companyId, string type, Guid id, bool tracked, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("Source id is required.", nameof(id));
        return type switch
        {
            TreasurySourceTypes.AccountTransfer => StateOrNull(await Query(_dbContext.TreasuryTransfers, tracked).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)),
            TreasurySourceTypes.BankAdjustment => StateOrNull(await Query(_dbContext.BankAdjustments, tracked).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)),
            TreasurySourceTypes.CardSettlement => StateOrNull(await Query(_dbContext.CardSettlements, tracked).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)),
            TreasurySourceTypes.PayoutSettlement => StateOrNull(await Query(_dbContext.PayoutSettlements, tracked).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)),
            _ => throw Block(TreasuryMovementReasonCodes.UnsupportedSourceType, "Treasury source type is not supported.")
        };
        static IQueryable<T> Query<T>(DbSet<T> set, bool tracked) where T : class => tracked ? set.IgnoreQueryFilters() : set.IgnoreQueryFilters().AsNoTracking();
        static SourceState? StateOrNull(object? value) => value is null ? null : State(value);
    }

    private static SourceState State(object entity) => entity switch
    {
        TreasuryTransfer x => new(x, x.Id, x.CompanyId, TreasurySourceTypes.AccountTransfer, x.SourceIdentity,
            $"Internal transfer · {x.SourceIdentity}", x.Status, x.ReasonCode, x.Currency, x.Amount, x.FeeAmount,
            x.Amount, x.RequiresApproval, x.ApprovalRequestId, x.Version, x.UpdatedUtc, x.FromBankAccountId,
            x.ToBankAccountId, null, x.FeeFinanceAccountId, x.CorrectionOfTransferId,
            BankLinks((x.OutboundBankTransactionId, TreasuryTransferLegRoles.Outbound), (x.InboundBankTransactionId, TreasuryTransferLegRoles.Inbound))),
        BankAdjustment x => new(x, x.Id, x.CompanyId, TreasurySourceTypes.BankAdjustment, x.SourceIdentity,
            $"{Friendly(x.AdjustmentKind)} · {x.Description}", x.Status, x.ReasonCode, x.Currency, x.Amount, 0m,
            x.Amount, x.RequiresApproval, x.ApprovalRequestId, x.Version, x.UpdatedUtc, null, null,
            x.BankAccountId, x.CounterpartFinanceAccountId, x.CorrectionOfAdjustmentId, [(x.BankTransactionId, TreasuryEvidenceTypes.BankTransaction)]),
        CardSettlement x => new(x, x.Id, x.CompanyId, TreasurySourceTypes.CardSettlement, x.SourceIdentity,
            $"Card settlement · {x.ProviderBatchReference}", x.Status, x.ReasonCode, x.Currency, x.GrossAmount,
            x.FeeAmount, x.NetAmount, x.RequiresApproval, x.ApprovalRequestId, x.Version, x.UpdatedUtc, null,
            null, x.BankAccountId, x.ReceivableFinanceAccountId, x.CorrectionOfSettlementId,
            BankLinks((x.BankTransactionId, TreasuryEvidenceTypes.BankTransaction))),
        PayoutSettlement x => new(x, x.Id, x.CompanyId, TreasurySourceTypes.PayoutSettlement, x.SourceIdentity,
            $"Payout settlement · {x.ProviderBatchReference}", x.Status, x.ReasonCode, x.Currency, x.GrossAmount,
            x.FeeAmount, x.NetAmount, x.RequiresApproval, x.ApprovalRequestId, x.Version, x.UpdatedUtc, null,
            null, x.BankAccountId, x.PayoutClearingFinanceAccountId, x.CorrectionOfSettlementId,
            BankLinks((x.BankTransactionId, TreasuryEvidenceTypes.BankTransaction))),
        _ => throw new ArgumentOutOfRangeException(nameof(entity))
    };

    private static (Guid Id, string Role)[] BankLinks(params (Guid? Id, string Role)[] values) =>
        values.Where(x => x.Id.HasValue).Select(x => (x.Id!.Value, x.Role)).ToArray();
    private static TreasurySourceSummaryDto MapSummary(SourceState x) => new(x.Id, x.Type, x.SourceIdentity,
        x.DisplayName, x.Status, x.ReasonCode, x.Currency, x.Gross, x.Fee, x.Net, x.RequiresApproval,
        x.ApprovalRequestId, x.Version, x.UpdatedUtc);
    private static TreasurySourceSummaryDto MapSummary(TreasuryTransfer x) => MapSummary(State(x));
    private static TreasurySourceSummaryDto MapSummary(BankAdjustment x) => MapSummary(State(x));
    private static TreasurySourceSummaryDto MapSummary(CardSettlement x) => MapSummary(State(x));
    private static TreasurySourceSummaryDto MapSummary(PayoutSettlement x) => MapSummary(State(x));
    private static TreasuryAllowedActionsDto Allowed(SourceState state)
    {
        var immutable = state.Status is TreasuryMovementStatuses.Posted or TreasuryMovementStatuses.Reversed;
        var canLink = !immutable && !state.ApprovalRequestId.HasValue && state.Entity is TreasuryTransfer or CardSettlement or PayoutSettlement;
        var canBind = state.Status == TreasuryMovementStatuses.AwaitingApproval && !state.ApprovalRequestId.HasValue;
        var canPreview = state.Status is TreasuryMovementStatuses.ReadyToPost or TreasuryMovementStatuses.AwaitingApproval;
        return new(canLink, canBind, canPreview, state.Status == TreasuryMovementStatuses.ReadyToPost,
            state.Status == TreasuryMovementStatuses.Posted, state.ReasonCode, Explain(state));
    }
    private static string Explain(SourceState state) => state.ReasonCode switch
    {
        TreasuryMovementReasonCodes.TransferLegMissing => "The counterpart bank leg has not arrived. The transfer remains in transit and no journal will be invented.",
        TreasuryMovementReasonCodes.TransferEvidenceMissing => "Link at least one bank leg before reviewing this transfer.",
        TreasuryMovementReasonCodes.BankEvidenceMissing => "Provider acceptance is not bank settlement. Link the booked bank transaction before posting.",
        TreasuryMovementReasonCodes.BankAmountMismatch => "The booked bank amount does not agree with the settlement net amount. Review the payout evidence.",
        TreasuryMovementReasonCodes.ApprovalRequired => "Material treasury sources require approved review before posting.",
        _ when state.Status == TreasuryMovementStatuses.ReadyToPost => "Evidence and accounting controls passed. The source is ready for governed posting.",
        _ when state.Status == TreasuryMovementStatuses.Posted => "The treasury source is linked to its governed journal.",
        _ when state.Status == TreasuryMovementStatuses.Reversed => "The original result is retained and a linked reversal was posted.",
        _ => "Review the treasury evidence before continuing."
    };
    private static TreasuryPostingPreviewDto BlockedPreview(SourceState state) => new(false,
        state.ReasonCode ?? TreasuryMovementReasonCodes.InvalidLifecycleState, Explain(state), null, []);

    private async Task<CompanyBankAccount> LoadBankAccountAsync(Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw Validation(nameof(id), "Bank account is required.");
        var account = await _dbContext.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Company bank account was not found.");
        if (!account.IsActive) throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, "The bank account is inactive.");
        return account;
    }
    private async Task<FinanceAccount> LoadPostingAccountAsync(Guid companyId, Guid id, string currency, string? expectedClass, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw Validation(nameof(id), "Accounting account is required.");
        var account = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting account was not found.");
        if (!account.IsPostingEnabled || account.EffectiveTo.HasValue) throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, "The accounting account is not open for posting.");
        if (!string.Equals(account.Currency, currency, StringComparison.OrdinalIgnoreCase)) throw CrossCurrency();
        if (expectedClass is not null && !string.Equals(account.AccountClass, expectedClass, StringComparison.OrdinalIgnoreCase))
            throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, $"The selected accounting account must be classified as {expectedClass}.");
        return account;
    }
    private async Task<BankTransaction> LoadBankTransactionAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.BankTransactions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Bank transaction was not found.");

    private async Task EnsureBankTransactionAvailableAsync(Guid companyId, Guid transactionId, Guid? currentSourceId, CancellationToken cancellationToken)
    {
        var linked = await _dbContext.TreasuryTransfers.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id != currentSourceId && (x.OutboundBankTransactionId == transactionId || x.InboundBankTransactionId == transactionId), cancellationToken)
            || await _dbContext.BankAdjustments.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id != currentSourceId && x.BankTransactionId == transactionId, cancellationToken)
            || await _dbContext.CardSettlements.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id != currentSourceId && x.BankTransactionId == transactionId, cancellationToken)
            || await _dbContext.PayoutSettlements.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id != currentSourceId && x.BankTransactionId == transactionId, cancellationToken);
        if (linked) throw Block(TreasuryMovementReasonCodes.BankTransactionAlreadyLinked, "The bank transaction is already governed by another treasury source.", true);
    }
    private async Task EnsureIdentityAvailableAsync(Guid companyId, string type, string identity, CancellationToken cancellationToken)
    {
        identity = identity?.Trim() ?? string.Empty; if (identity.Length == 0) throw Validation(nameof(identity), "Source identity is required.");
        var exists = type switch
        {
            TreasurySourceTypes.AccountTransfer => await _dbContext.TreasuryTransfers.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.SourceIdentity == identity, cancellationToken),
            TreasurySourceTypes.BankAdjustment => await _dbContext.BankAdjustments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.SourceIdentity == identity, cancellationToken),
            TreasurySourceTypes.CardSettlement => await _dbContext.CardSettlements.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.SourceIdentity == identity, cancellationToken),
            TreasurySourceTypes.PayoutSettlement => await _dbContext.PayoutSettlements.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.SourceIdentity == identity, cancellationToken),
            _ => true
        };
        if (exists) throw Block(TreasuryMovementReasonCodes.SourceIdentityConflict, "This treasury source identity already exists.", true);
    }
    private async Task EnsureApprovedTreasuryRequestAsync(SourceState state, Guid approvalRequestId, CancellationToken cancellationToken)
    {
        if (approvalRequestId == Guid.Empty) throw Validation(nameof(approvalRequestId), "Approval request is required.");
        var approval = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == state.CompanyId && x.Id == approvalRequestId, cancellationToken);
        var approvedVersion = approval?.ThresholdContext.TryGetValue("sourceVersion", out var value) == true
            ? value?.ToString()
            : null;
        var approvedSourceType = approval?.ThresholdContext.TryGetValue("sourceType", out var typeValue) == true
            ? typeValue?.ToString()
            : null;
        if (approval?.Status != ApprovalRequestStatus.Approved ||
            approval.TargetEntityType != ApprovalTargetEntityType.TreasurySource.ToStorageValue() ||
            approval.TargetEntityId != state.Id ||
            !string.Equals(approvedSourceType, state.Type, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(approvedVersion, (state.Version + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw Block(TreasuryMovementReasonCodes.ApprovalRequired, "The treasury approval is missing, stale, or does not target this source.");
    }
    private async Task EnsureCorrectionExistsAsync<T>(Guid companyId, Guid correctionId, CancellationToken cancellationToken) where T : class, ICompanyOwnedEntity
    { if (!await _dbContext.Set<T>().IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && EF.Property<Guid>(x, "Id") == correctionId, cancellationToken)) throw new KeyNotFoundException("The treasury source being corrected was not found."); }

    private static void ValidateBankAdjustmentEvidence(Guid bankAccountId, BankTransaction transaction, string kind, decimal amount, string currency)
    {
        var expectedPositive = BankAdjustmentKinds.IsIncome(kind);
        if (transaction.BankAccountId != bankAccountId || transaction.Currency != currency ||
            transaction.Amount > 0m != expectedPositive || Money(Math.Abs(transaction.Amount)) != Money(amount))
            throw Block(TreasuryMovementReasonCodes.BankAmountMismatch, "The booked bank row does not match the adjustment type, amount, account, and currency.");
    }
    private static void ValidateSettlementBankAccount(Guid bankAccountId, string currency, BankTransaction transaction)
    {
        if (transaction.BankAccountId != bankAccountId || transaction.Currency != currency || transaction.Amount <= 0m)
            throw Block(TreasuryMovementReasonCodes.InvalidAccountingPolicy, "Settlement evidence must be an incoming booked row on the configured bank account and currency.");
    }
    private void AddEvidence(Guid companyId, string type, Guid sourceId, IReadOnlyList<TreasuryEvidenceInputDto> evidence, DateTime now)
    { foreach (var item in evidence ?? []) _dbContext.TreasurySourceEvidence.Add(new(Guid.NewGuid(), companyId, type, sourceId, item.EvidenceType, item.Reference, item.ContentHash, item.Description, now)); }
    private void AddBankEvidence(Guid companyId, string type, Guid sourceId, BankTransaction transaction, string role, DateTime now)
    {
        var hashInput = $"{transaction.Id:N}|{transaction.SourceVersion}|{transaction.Amount:0.00}|{transaction.Currency}|{transaction.BookingDate:O}|{transaction.RowContentHash}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
        _dbContext.TreasurySourceEvidence.Add(new(Guid.NewGuid(), companyId, type, sourceId,
            TreasuryEvidenceTypes.BankTransaction, transaction.Id.ToString("D"), hash, $"{Friendly(role)} bank evidence", now));
    }

    private async Task RecordEventAndAuditAsync(Guid companyId, string type, Guid sourceId, string action,
        Guid actor, string? reasonCode, string before, string after, string auditAction, string status,
        string? correlationId, CancellationToken cancellationToken)
    {
        var now = UtcNow(); _dbContext.TreasurySourceEvents.Add(new(Guid.NewGuid(), companyId, type, sourceId,
            action, actor, reasonCode, before, after, now));
        await _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, auditAction, AuditTargetTypes.TreasurySource,
            $"{type}:{sourceId:N}", status is TreasuryMovementStatuses.Posted or TreasuryMovementStatuses.Reversed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
            ExplainStatus(status), [$"{type}:{sourceId:N}"], new Dictionary<string, string?> { ["sourceType"] = type, ["status"] = status, ["reasonCode"] = reasonCode },
            correlationId, now, PayloadDiffJson: JsonSerializer.Serialize(new { before, after })), cancellationToken);
    }
    private static string ExplainStatus(string status) => status switch
    { TreasuryMovementStatuses.InTransit => "Transfer retained in transit until both booked bank legs are present.", TreasuryMovementStatuses.NeedsReview => "Treasury source requires evidence review.", TreasuryMovementStatuses.AwaitingBankEvidence => "Treasury source is waiting for booked bank evidence.", TreasuryMovementStatuses.AwaitingApproval => "Treasury source is waiting for material approval.", TreasuryMovementStatuses.ReadyToPost => "Treasury source passed evidence controls and is ready to post.", TreasuryMovementStatuses.Posted => "Treasury source posted through the native accounting boundary.", TreasuryMovementStatuses.Reversed => "Treasury source was corrected with a linked reversal.", _ => "Treasury source lifecycle changed." };

    private static void MarkPosted(object entity, long version, Guid actor, DateTime now)
    { switch (entity) { case TreasuryTransfer x: x.MarkPosted(version, actor, now); break; case BankAdjustment x: x.MarkPosted(version, actor, now); break; case CardSettlement x: x.MarkPosted(version, actor, now); break; case PayoutSettlement x: x.MarkPosted(version, actor, now); break; } }
    private static void MarkReversed(object entity, long version, Guid actor, DateTime now)
    { switch (entity) { case TreasuryTransfer x: x.MarkReversed(version, actor, now); break; case BankAdjustment x: x.MarkReversed(version, actor, now); break; case CardSettlement x: x.MarkReversed(version, actor, now); break; case PayoutSettlement x: x.MarkReversed(version, actor, now); break; } }
    private static string Snapshot(object entity) { var state = State(entity); return JsonSerializer.Serialize(new { state.Id, state.Type, state.SourceIdentity, state.Status, state.ReasonCode, state.Currency, state.Gross, state.Fee, state.Net, state.RequiresApproval, state.ApprovalRequestId, state.Version, BankTransactions = state.BankTransactions.Select(x => new { x.Id, x.Role }) }); }

    private void EnsureTenant(Guid companyId)
    { if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId)); if (_companyContext?.CompanyId is Guid current && current != companyId) throw new UnauthorizedAccessException("Treasury sources are scoped to the active company context."); }
    private void EnsureCommand(Guid companyId, Guid actor) { EnsureTenant(companyId); if (actor == Guid.Empty) throw new UnauthorizedAccessException("A resolved company user is required."); }
    private static string NormalizeSourceType(string value) { var type = TreasurySourceTypes.Normalize(value); return TreasurySourceTypes.IsSupported(type) ? type : throw Block(TreasuryMovementReasonCodes.UnsupportedSourceType, "Treasury source type is not supported."); }
    private static string Currency(string value) { var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty; return normalized.Length == 3 && normalized.All(char.IsLetter) ? normalized : throw Validation(nameof(value), "Currency must be a three-letter ISO code."); }
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Friendly(string value) => value.Replace('_', ' ');
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static TreasuryMovementException CrossCurrency() => Block(TreasuryMovementReasonCodes.CrossCurrencyTransferBlocked, "Cross-currency treasury posting remains blocked until governed currency accounting is enabled.");
    private static TreasuryMovementException Conflict() => Block(TreasuryMovementReasonCodes.SourceVersionConflict, "The treasury source changed after it was reviewed.", true);
    private static TreasuryMovementException Block(string code, string message, bool conflict = false) => new(code, message, conflict);
    private static FinanceValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] }, message);

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null) return await action();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => { await using var tx = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken); var result = await action(); await tx.CommitAsync(cancellationToken); return result; });
    }

    private sealed record SourceState(object Entity, Guid Id, Guid CompanyId, string Type, string SourceIdentity,
        string DisplayName, string Status, string? ReasonCode, string Currency, decimal Gross, decimal Fee,
        decimal Net, bool RequiresApproval, Guid? ApprovalRequestId, long Version, DateTime UpdatedUtc,
        Guid? FromBankAccountId, Guid? ToBankAccountId, Guid? BankAccountId,
        Guid? CounterpartFinanceAccountId, Guid? CorrectionOfSourceId,
        IReadOnlyList<(Guid Id, string Role)> BankTransactions);
    private sealed record BuiltEntry(ProposedAccountingEntry Entry, IReadOnlyList<TreasuryPostingLineDto> Lines);
}
