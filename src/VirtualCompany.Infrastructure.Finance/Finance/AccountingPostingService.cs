using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingPostingService : IAccountingPostingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingJournalReadService _readService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;
    private readonly IAccountingAuthorityPolicy? _authorityPolicy;

    public AccountingPostingService(
        VirtualCompanyDbContext dbContext,
        IAccountingJournalReadService readService,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider,
        IAccountingAuthorityPolicy? authorityPolicy = null)
    {
        _dbContext = dbContext;
        _readService = readService;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
        _authorityPolicy = authorityPolicy;
    }

    public async Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Entry);
        return await ValidateAsync(command.Entry, requireAuthority: true, cancellationToken);
    }

    public async Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(
        PreviewNonAuthoritativeAccountingCandidateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Entry);
        return await ValidateAsync(command.Entry, requireAuthority: false, cancellationToken);
    }

    public async Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Entry);
        try
        {
            return await ExecuteWithSqlRetryAsync(
                () => PostInternalAsync(command.Entry, command.CorrelationId, command.Entry.OriginalLedgerEntryId,
                    command.Entry.CorrectionReason, null, cancellationToken),
                cancellationToken);
        }
        catch (AccountingPostingException exception) when (exception.ReasonCode == AccountingPostingReasonCodes.AuthorityUnavailable)
        {
            await RecordFormerAuthorityPostingAttemptAsync(command, cancellationToken);
            throw;
        }
    }

    private async Task RecordFormerAuthorityPostingAttemptAsync(PostAccountingEntryCommand command,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var monitoringRun = await _dbContext.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.Entry.CompanyId &&
                (x.Status == AccountingProviderSwitchMonitoringStatuses.Active ||
                 x.Status == AccountingProviderSwitchMonitoringStatuses.AttentionRequired ||
                 x.Status == AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval))
            .OrderByDescending(x => x.StartedUtc).FirstOrDefaultAsync(cancellationToken);
        if (monitoringRun is null) return;
        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(command.Entry.CompanyId, command.Entry.ActorType,
            command.Entry.ActorUserId == Guid.Empty ? null : command.Entry.ActorUserId,
            AuditEventActions.AccountingFormerAuthorityPostingBlocked,
            AuditTargetTypes.AccountingProviderSwitchMonitoring, monitoringRun.Id.ToString("D"), AuditEventOutcomes.Rejected,
            "A posting attempt to the former accounting authority was blocked after activation.",
            ["accounting_authority", "accounting_provider_switch_monitoring"],
            new Dictionary<string, string?> { ["switchId"] = monitoringRun.SwitchId.ToString("D"),
                ["postingDate"] = command.Entry.PostingDate.ToString("O"), ["sourceType"] = command.Entry.SourceType,
                ["sourceId"] = command.Entry.SourceId }, command.CorrelationId,
            _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(
        MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken)
    {
        if (command.CompanyId == Guid.Empty || command.SwitchId == Guid.Empty || command.ExecutionId == Guid.Empty ||
            command.CandidateId == Guid.Empty || command.ActivationApprovalRequestId == Guid.Empty || command.ActorUserId == Guid.Empty)
            throw Error(AccountingPostingReasonCodes.InvalidSource, "The provider-switch journal binding is incomplete.");
        var candidate = await _dbContext.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                x.Id == command.CandidateId, cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.InvalidSource, "The approved native journal candidate was not found.");
        if (candidate.Status != AccountingProviderSwitchNativeCandidateStatuses.Valid ||
            candidate.CandidateKind is not (AccountingProviderSwitchNativeCandidateKinds.OpeningJournal or
                AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal))
            throw Error(AccountingPostingReasonCodes.InvalidSource, "Only a valid prepared journal candidate can be materialized.");
        var execution = await _dbContext.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                x.Id == command.ExecutionId && x.Status == AccountingProviderSwitchCutoverStatuses.Activating,
                cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.AuthorityUnavailable,
                "Native migration journals can be committed only inside atomic switch activation.");
        var snapshot = await _dbContext.AccountingProviderSwitchFinalSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                x.ExecutionId == command.ExecutionId, cancellationToken);
        if (!string.Equals(snapshot.FinalSourceSnapshotHash, command.FinalSnapshotHash, StringComparison.Ordinal))
            throw Error(AccountingPostingReasonCodes.IdempotencyConflict, "The final source snapshot changed before journal materialization.", true);
        var activation = await _dbContext.AccountingProviderSwitchActivationApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ExecutionId == command.ExecutionId &&
                x.ApprovalRequestId == command.ActivationApprovalRequestId && x.FinalSnapshotHash == command.FinalSnapshotHash,
                cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.ApprovalInvalid, "The activation approval is not bound to this final snapshot.");
        _ = activation;
        var authority = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == execution.AuthorityPeriodId &&
                x.Authority == AccountingAuthorityValues.Migration && x.TargetAuthority == AccountingAuthorityValues.InternalLedger,
                cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.AuthorityUnavailable,
                "The switch is not in the bounded migration authority state for an internal target.");
        _ = authority;
        var entry = BuildProviderSwitchEntry(candidate, command.ActorUserId, command.ActivationApprovalRequestId);
        return await ExecuteWithSqlRetryAsync(
            () => PostInternalAsync(entry, command.CorrelationId, null, null, null, cancellationToken, requireAuthority: false),
            cancellationToken);
    }

    public async Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty || command.OriginalLedgerEntryId == Guid.Empty)
        {
            throw Error(AccountingPostingReasonCodes.JournalNotFound, "The journal entry could not be found.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw Error(AccountingPostingReasonCodes.CorrectionReasonRequired, "A correction reason is required.");
        }

        var original = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleOrDefaultAsync(entry => entry.CompanyId == command.CompanyId && entry.Id == command.OriginalLedgerEntryId, cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.JournalNotFound, "The journal entry could not be found.");

        if (!LedgerEntryStatuses.IsPosted(original.Status))
        {
            throw Error(AccountingPostingReasonCodes.JournalNotPosted, "Only a posted journal entry can be reversed.");
        }

        if (await _dbContext.LedgerEntries.AsNoTracking().AnyAsync(
                entry => entry.CompanyId == command.CompanyId && entry.OriginalLedgerEntryId == original.Id && entry.PostingType == LedgerPostingTypeValues.Reversal,
                cancellationToken))
        {
            throw Error(AccountingPostingReasonCodes.AlreadyReversed, "This journal entry has already been reversed.", true);
        }

        var proposed = new ProposedAccountingEntry(
            command.CompanyId,
            command.FiscalPeriodId,
            command.VoucherSeriesCode,
            original.DocumentDate ?? command.PostingDate,
            command.PostingDate,
            LedgerPostingTypeValues.Reversal,
            command.Reason,
            "ledger_entry_reversal",
            original.Id.ToString("N"),
            command.SourceVersion,
            command.IdempotencyKey,
            original.Lines.Select(line => new ProposedAccountingLine(
                line.FinanceAccountId,
                line.CreditAmount,
                line.DebitAmount,
                original.BaseCurrency ?? line.Currency,
                line.Description,
                line.CostCenterId,
                ParseFacts(line.TaxFactsJson),
                ParseFacts(line.DimensionFactsJson))).ToArray(),
            command.ActorUserId,
            command.ApprovalRequestId,
            command.ApprovalRequestId.HasValue,
            ParseFacts(original.PolicyFactsJson),
            "reverse");

        return await ExecuteWithSqlRetryAsync(
            () => PostInternalAsync(
                proposed,
                command.CorrelationId,
                original.Id,
                command.Reason,
                (original.PolicyPackKey, original.PolicyPackVersion),
                cancellationToken),
            cancellationToken);
    }

    private async Task<PostedAccountingJournal> PostInternalAsync(
        ProposedAccountingEntry entry,
        string? correlationId,
        Guid? originalLedgerEntryId,
        string? correctionReason,
        (string? Key, string? Version)? policyOverride,
        CancellationToken cancellationToken,
        bool requireAuthority = true)
    {
        var payloadHash = ComputePayloadHash(entry, originalLedgerEntryId, correctionReason);
        await using var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var replay = await FindExistingIdentityAsync(entry, cancellationToken);
        if (replay is not null)
        {
            EnsureMatchingReplay(replay, entry, payloadHash);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
            return new PostedAccountingJournal(
                await _readService.GetAsync(new GetAccountingJournalQuery(entry.CompanyId, replay.LedgerEntryId), cancellationToken),
                true);
        }

        var preview = await ValidateAsync(entry, requireAuthority, cancellationToken);
        if (!preview.IsValid)
        {
            var first = preview.Issues[0];
            throw Error(first.ReasonCode, first.Explanation);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var effectivePostedAtUtc = entry.EffectivePostedAtUtc is { } requestedPostedAt
            ? NormalizeUtc(requestedPostedAt)
            : nowUtc;
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleAsync(item => item.CompanyId == entry.CompanyId, cancellationToken);
        var series = await _dbContext.VoucherSeries.AsNoTracking()
            .SingleAsync(item => item.CompanyId == entry.CompanyId && item.Code == NormalizeSeries(entry.VoucherSeriesCode), cancellationToken);
        var fiscalYear = ResolveFiscalYear(entry.PostingDate, configuration.FiscalYearStartMonth, configuration.FiscalYearStartDay);
        var sequenceNumber = await AllocateVoucherNumberAsync(entry.CompanyId, series.Id, fiscalYear, nowUtc, cancellationToken);
        var ledgerEntryId = Guid.NewGuid();
        var entryNumber = $"{series.NumberPrefix}-{fiscalYear:D4}-{sequenceNumber:D6}";
        var policyKey = policyOverride?.Key ?? configuration.PolicyPackKey;
        var policyVersion = policyOverride?.Version ?? configuration.PolicyPackVersion;
        var policyFactsJson = SerializeFacts(entry.PolicyFacts);

        var journal = new LedgerEntry(
            ledgerEntryId,
            entry.CompanyId,
            entry.FiscalPeriodId,
            entryNumber,
            entry.PostingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            entry.Description,
            NormalizeRequired(entry.SourceType, nameof(entry.SourceType), 64).ToLowerInvariant(),
            NormalizeRequired(entry.SourceId, nameof(entry.SourceId), 128),
            effectivePostedAtUtc,
            nowUtc,
            nowUtc,
            series.Id,
            sequenceNumber,
            fiscalYear,
            entry.DocumentDate,
            entry.PostingDate,
            configuration.BaseCurrency,
            entry.PostingType,
            entry.SourceVersion,
            entry.IdempotencyKey,
            policyKey,
            policyVersion,
            policyFactsJson,
            string.Equals(entry.ActorType, AuditActorTypes.User, StringComparison.OrdinalIgnoreCase) ? entry.ActorUserId : null,
            entry.ApprovalRequestId,
            originalLedgerEntryId,
            correctionReason);

        foreach (var proposedLine in entry.Lines)
        {
            journal.Lines.Add(new LedgerEntryLine(
                Guid.NewGuid(),
                entry.CompanyId,
                ledgerEntryId,
                proposedLine.FinanceAccountId,
                Round(proposedLine.DebitAmount, configuration.RoundingPrecision, configuration.RoundingMode),
                Round(proposedLine.CreditAmount, configuration.RoundingPrecision, configuration.RoundingMode),
                configuration.BaseCurrency,
                proposedLine.CostCenterId,
                proposedLine.Description,
                nowUtc,
                SerializeFacts(proposedLine.TaxFacts),
                SerializeFacts(proposedLine.DimensionFacts)));
        }

        journal.SourceMappings.Add(new LedgerEntrySourceMapping(
            Guid.NewGuid(), entry.CompanyId, ledgerEntryId, entry.SourceType, entry.SourceId, effectivePostedAtUtc, nowUtc));
        foreach (var evidence in entry.Evidence ?? [])
            journal.EvidenceLinks.Add(new LedgerEntryEvidenceLink(Guid.NewGuid(), entry.CompanyId, ledgerEntryId,
                evidence.DocumentId, evidence.ContentHash, evidence.Title, nowUtc));
        _dbContext.LedgerEntries.Add(journal);
        _dbContext.LedgerPostingIdentities.Add(new LedgerPostingIdentity(
            Guid.NewGuid(), entry.CompanyId, ledgerEntryId, entry.Action, entry.SourceType, entry.SourceId,
            entry.SourceVersion, entry.IdempotencyKey, payloadHash, nowUtc));
        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(
            entry.CompanyId,
            entry.ActorType,
            string.Equals(entry.ActorType, AuditActorTypes.User, StringComparison.OrdinalIgnoreCase) ? entry.ActorUserId : null,
            originalLedgerEntryId.HasValue ? AuditEventActions.AccountingJournalReversed : AuditEventActions.AccountingJournalPosted,
            AuditTargetTypes.AccountingJournal,
            ledgerEntryId.ToString("N"),
            AuditEventOutcomes.Succeeded,
            originalLedgerEntryId.HasValue ? "A posted journal was reversed through a linked correction." : "A balanced journal was posted to the internal ledger.",
            ["accounting_configuration", "fiscal_period", "finance_accounts"],
            new Dictionary<string, string?>
            {
                ["entryNumber"] = entryNumber,
                ["sourceType"] = entry.SourceType,
                ["sourceId"] = entry.SourceId,
                ["sourceVersion"] = entry.SourceVersion,
                ["policyPack"] = $"{policyKey}@{policyVersion}",
                ["approvalRequestId"] = entry.ApprovalRequestId?.ToString("N"),
                ["originalLedgerEntryId"] = originalLedgerEntryId?.ToString("N")
            },
            correlationId,
            nowUtc), cancellationToken);

        if (string.Equals(entry.SourceType, "manual_journal_draft", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(entry.SourceId, out var manualDraftId))
        {
            var draft = await _dbContext.ManualJournalDrafts.SingleOrDefaultAsync(x =>
                x.CompanyId == entry.CompanyId && x.Id == manualDraftId, cancellationToken)
                ?? throw Error(AccountingPostingReasonCodes.InvalidSource, "The manual journal draft could not be found.");
            if (!string.Equals(draft.Version.ToString(CultureInfo.InvariantCulture), entry.SourceVersion, StringComparison.Ordinal) ||
                !string.Equals(draft.PayloadHash, entry.ApprovalPayloadHash, StringComparison.OrdinalIgnoreCase))
                throw Error(AccountingPostingReasonCodes.ApprovalInvalid, "The manual journal changed after approval.", true);
            draft.MarkPosted(ledgerEntryId, entry.ActorUserId, nowUtc);
        }
        else if (string.Equals(entry.SourceType, "customer_invoice", StringComparison.OrdinalIgnoreCase) &&
                 Guid.TryParse(entry.SourceId, out var customerInvoiceId))
        {
            var profile = await _dbContext.CustomerInvoiceAccountingProfiles
                .Include(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == entry.CompanyId && x.InvoiceId == customerInvoiceId, cancellationToken)
                ?? throw Error(AccountingPostingReasonCodes.InvalidSource, "The customer invoice accounting profile could not be found.");
            if (!string.Equals(profile.Version.ToString(CultureInfo.InvariantCulture), entry.SourceVersion, StringComparison.Ordinal) ||
                !string.Equals(profile.PayloadHash, entry.ApprovalPayloadHash, StringComparison.OrdinalIgnoreCase) ||
                profile.ApprovalRequestId != entry.ApprovalRequestId ||
                profile.ApprovalRequest?.TargetEntityType != ApprovalTargetEntityType.CustomerInvoiceAccounting.ToStorageValue() ||
                profile.ApprovalRequest.TargetEntityId != profile.Id)
                throw Error(AccountingPostingReasonCodes.ApprovalInvalid, "The customer invoice changed after accounting approval.", true);
            profile.MarkPosted(ledgerEntryId, entry.ActorUserId, nowUtc);
        }
        else if (string.Equals(entry.SourceType, "supplier_bill", StringComparison.OrdinalIgnoreCase) &&
                 Guid.TryParse(entry.SourceId, out var supplierBillId))
        {
            var profile = await _dbContext.SupplierBillAccountingProfiles
                .Include(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == entry.CompanyId && x.BillId == supplierBillId, cancellationToken)
                ?? throw Error(AccountingPostingReasonCodes.InvalidSource, "The supplier bill accounting profile could not be found.");
            if (!string.Equals(profile.Version.ToString(CultureInfo.InvariantCulture), entry.SourceVersion, StringComparison.Ordinal) ||
                !string.Equals(profile.PayloadHash, entry.ApprovalPayloadHash, StringComparison.OrdinalIgnoreCase) ||
                profile.ApprovalRequestId != entry.ApprovalRequestId ||
                profile.ApprovalRequest?.TargetEntityType != ApprovalTargetEntityType.SupplierBillAccounting.ToStorageValue() ||
                profile.ApprovalRequest.TargetEntityId != profile.Id)
                throw Error(AccountingPostingReasonCodes.ApprovalInvalid, "The supplier bill changed after accounting approval.", true);
            profile.MarkPosted(ledgerEntryId, entry.ActorUserId, nowUtc);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException) when (ownedTransaction is not null)
        {
            await ownedTransaction.RollbackAsync(cancellationToken);
            DetachPendingChanges();
            var concurrent = await FindExistingIdentityAsync(entry, cancellationToken);
            if (concurrent is not null)
            {
                EnsureMatchingReplay(concurrent, entry, payloadHash);
                return new PostedAccountingJournal(
                    await _readService.GetAsync(new GetAccountingJournalQuery(entry.CompanyId, concurrent.LedgerEntryId), cancellationToken),
                    true);
            }
            throw;
        }

        return new PostedAccountingJournal(
            await _readService.GetAsync(new GetAccountingJournalQuery(entry.CompanyId, ledgerEntryId), cancellationToken),
            false);
    }

    private async Task<AccountingPostingPreview> ValidateAsync(
        ProposedAccountingEntry entry,
        bool requireAuthority,
        CancellationToken cancellationToken)
    {
        var issues = new List<AccountingPostingIssue>();
        if (entry.CompanyId == Guid.Empty)
        {
            issues.Add(new(AccountingPostingReasonCodes.ConfigurationMissing, "Accounting has not been configured for this company."));
            return BuildPreview(entry, null, issues);
        }

        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == entry.CompanyId, cancellationToken);
        if (configuration is null)
        {
            issues.Add(new(AccountingPostingReasonCodes.ConfigurationMissing, "Accounting has not been configured for this company."));
            return BuildPreview(entry, null, issues);
        }

        if (configuration.SetupState != AccountingSetupStateValues.Ready)
            issues.Add(new(AccountingPostingReasonCodes.ConfigurationIncomplete, "Accounting setup must be completed before journals can be posted."));
        if (requireAuthority && _authorityPolicy is null)
        {
            if (configuration.Authority != AccountingAuthorityValues.InternalLedger)
                issues.Add(new(AccountingPostingReasonCodes.AuthorityUnavailable, "The internal ledger is not the accounting authority for this company."));
        }
        else if (requireAuthority)
        {
            var authority = await _authorityPolicy.EvaluateAsync(
                new EvaluateAccountingAuthorityQuery(
                    entry.CompanyId,
                    entry.PostingDate,
                    AccountingAuthorityOperationValues.NativeAuthoritativePosting),
                cancellationToken);
            if (!authority.IsAllowed)
                issues.Add(new(AccountingPostingReasonCodes.AuthorityUnavailable, authority.Explanation));
        }
        if (configuration.PolicyPackEffectiveFrom > entry.PostingDate)
            issues.Add(new(AccountingPostingReasonCodes.AuthorityUnavailable, "The selected accounting policy is not effective on the posting date."));

        var period = await _dbContext.FiscalPeriods.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == entry.CompanyId && item.Id == entry.FiscalPeriodId, cancellationToken);
        if (period is null) issues.Add(new(AccountingPostingReasonCodes.PeriodNotFound, "The accounting period could not be found."));
        else
        {
            if (period.IsReportingLocked) issues.Add(new(AccountingPostingReasonCodes.PeriodLocked, "The accounting period is locked for reporting."));
            else if (period.IsClosed) issues.Add(new(AccountingPostingReasonCodes.PeriodClosed, "The accounting period is closed."));
            var start = DateOnly.FromDateTime(period.StartUtc);
            var endExclusive = DateOnly.FromDateTime(period.EndUtc);
            if (entry.PostingDate < start || entry.PostingDate >= endExclusive)
                issues.Add(new(AccountingPostingReasonCodes.PostingDateOutsidePeriod, "The posting date is outside the selected accounting period."));
        }

        VoucherSeries? series = null;
        if (!string.IsNullOrWhiteSpace(entry.VoucherSeriesCode))
        {
            series = await _dbContext.VoucherSeries.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == entry.CompanyId && item.Code == NormalizeSeries(entry.VoucherSeriesCode), cancellationToken);
        }
        if (series is null) issues.Add(new(AccountingPostingReasonCodes.VoucherSeriesNotFound, "The voucher series could not be found."));
        else if (!series.IsActive) issues.Add(new(AccountingPostingReasonCodes.VoucherSeriesInactive, "The voucher series is not active."));

        if (entry.Lines is null || entry.Lines.Count < 2)
            issues.Add(new(AccountingPostingReasonCodes.TooFewLines, "A journal entry must contain at least two non-zero lines."));
        else
        {
            var accountIds = entry.Lines.Select(line => line.FinanceAccountId).Where(id => id != Guid.Empty).Distinct().ToArray();
            var accounts = await _dbContext.FinanceAccounts.AsNoTracking()
                .Where(account => account.CompanyId == entry.CompanyId && accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, cancellationToken);
            foreach (var line in entry.Lines)
            {
                if (line.FinanceAccountId == Guid.Empty || !accounts.TryGetValue(line.FinanceAccountId, out var account))
                {
                    issues.Add(new(AccountingPostingReasonCodes.AccountNotFound, "A journal account could not be found.", line.FinanceAccountId));
                    continue;
                }
                if (account.AccountClass is null || account.NormalBalance is null)
                    issues.Add(new(AccountingPostingReasonCodes.AccountUnclassified, "The journal account needs an accounting class and normal balance before it can be used.", account.Id));
                if (!account.IsPostingEnabled)
                    issues.Add(new(AccountingPostingReasonCodes.AccountPostingDisabled, "The journal account is not enabled for posting.", account.Id));
                if (account.EffectiveFrom.HasValue && entry.PostingDate < account.EffectiveFrom.Value || account.EffectiveTo.HasValue && entry.PostingDate > account.EffectiveTo.Value)
                    issues.Add(new(AccountingPostingReasonCodes.AccountInactive, "The journal account is not active on the posting date.", account.Id));
                if (entry.PostingType == LedgerPostingTypeValues.Manual && account.RestrictManualPosting)
                    issues.Add(new(AccountingPostingReasonCodes.ManualPostingRestricted, "Manual posting is restricted for this control account.", account.Id));
                if (!string.Equals(line.Currency?.Trim(), configuration.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new(AccountingPostingReasonCodes.CurrencyMismatch, "Every journal line must use the company's base currency.", account.Id));
                if (line.DebitAmount < 0 || line.CreditAmount < 0 || line.DebitAmount == 0 && line.CreditAmount == 0 || line.DebitAmount > 0 && line.CreditAmount > 0)
                    issues.Add(new(AccountingPostingReasonCodes.InvalidLine, "Each journal line must contain either one positive debit or one positive credit.", account.Id));
                if (!HasSupportedPrecision(line.DebitAmount, configuration.RoundingPrecision) || !HasSupportedPrecision(line.CreditAmount, configuration.RoundingPrecision))
                    issues.Add(new(AccountingPostingReasonCodes.InvalidPrecision, $"Journal amounts support up to {configuration.RoundingPrecision} decimal places.", account.Id));
                if (!ValidFacts(line.TaxFacts) || !ValidFacts(line.DimensionFacts))
                    issues.Add(new(AccountingPostingReasonCodes.InvalidFacts, "Tax and dimension facts must use non-empty bounded keys and values.", account.Id));
            }
        }

        TryValidateRequired(entry.SourceType, nameof(entry.SourceType), 64, issues);
        TryValidateRequired(entry.SourceId, nameof(entry.SourceId), 128, issues);
        TryValidateRequired(entry.SourceVersion, nameof(entry.SourceVersion), 128, issues);
        TryValidateRequired(entry.IdempotencyKey, nameof(entry.IdempotencyKey), 200, issues);
        TryValidateRequired(entry.Action, nameof(entry.Action), 64, issues);
        try { _ = LedgerPostingTypeValues.Normalize(entry.PostingType); }
        catch (ArgumentException) { issues.Add(new(AccountingPostingReasonCodes.InvalidSource, "The posting type is not supported.")); }
        if (entry.PostingType is LedgerPostingTypeValues.Manual or LedgerPostingTypeValues.Adjustment &&
            !string.Equals(entry.SourceType, "manual_journal_draft", StringComparison.OrdinalIgnoreCase))
            issues.Add(new(AccountingPostingReasonCodes.InvalidSource,
                "Manual and adjusting journals must use the governed manual-journal draft workflow."));
        if (!ValidFacts(entry.PolicyFacts)) issues.Add(new(AccountingPostingReasonCodes.InvalidFacts, "Policy facts must use non-empty bounded keys and values."));
        if (entry.EffectivePostedAtUtc is { } effectivePostedAt &&
            DateOnly.FromDateTime(NormalizeUtc(effectivePostedAt)) != entry.PostingDate)
            issues.Add(new(AccountingPostingReasonCodes.PostingDateOutsidePeriod, "The effective posting timestamp must fall on the selected posting date."));

        var normalizedActorType = string.IsNullOrWhiteSpace(entry.ActorType) ? string.Empty : entry.ActorType.Trim().ToLowerInvariant();
        if (normalizedActorType == AuditActorTypes.User)
        {
            var actorIsMember = entry.ActorUserId != Guid.Empty && await _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(
                membership => membership.CompanyId == entry.CompanyId && membership.UserId == entry.ActorUserId && membership.Status == CompanyMembershipStatus.Active,
                cancellationToken);
            if (!actorIsMember) issues.Add(new(AccountingPostingReasonCodes.ActorInvalid, "The posting actor is not an active member of this company."));
        }
        else if (normalizedActorType != AuditActorTypes.System || entry.ActorUserId != Guid.Empty)
        {
            issues.Add(new(AccountingPostingReasonCodes.ActorInvalid, "The posting actor is not valid for this accounting action."));
        }

        if (entry.RequiresApproval && !entry.ApprovalRequestId.HasValue)
            issues.Add(new(AccountingPostingReasonCodes.ApprovalMissing, "An approved accounting request is required before posting."));
        if (entry.ApprovalRequestId.HasValue)
        {
            var approval = await _dbContext.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(
                item => item.CompanyId == entry.CompanyId && item.Id == entry.ApprovalRequestId,
                cancellationToken);
            var approvedVersion = approval?.ThresholdContext.TryGetValue("sourceVersion", out var versionNode) == true
                ? versionNode?.ToString()
                : null;
            var approvedPayloadHash = approval?.ThresholdContext.TryGetValue("payloadHash", out var hashNode) == true
                ? hashNode?.ToString()
                : null;
            var approvalTargetsSource = true;
            if (string.Equals(entry.SourceType, "manual_journal_draft", StringComparison.OrdinalIgnoreCase))
                approvalTargetsSource = Guid.TryParse(entry.SourceId, out var approvalDraftId) &&
                    approval?.TargetEntityType == ApprovalTargetEntityType.ManualJournalDraft.ToStorageValue() && approval.TargetEntityId == approvalDraftId;
            else if (string.Equals(entry.SourceType, "customer_invoice", StringComparison.OrdinalIgnoreCase))
            {
                var profileId = Guid.TryParse(entry.SourceId, out var approvalInvoiceId)
                    ? await _dbContext.CustomerInvoiceAccountingProfiles.AsNoTracking()
                        .Where(x => x.CompanyId == entry.CompanyId && x.InvoiceId == approvalInvoiceId)
                        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken)
                    : null;
                approvalTargetsSource = profileId.HasValue && approval?.TargetEntityType == ApprovalTargetEntityType.CustomerInvoiceAccounting.ToStorageValue() &&
                    approval.TargetEntityId == profileId.Value;
            }
            else if (string.Equals(entry.SourceType, "supplier_bill", StringComparison.OrdinalIgnoreCase))
            {
                var profileId = Guid.TryParse(entry.SourceId, out var approvalBillId)
                    ? await _dbContext.SupplierBillAccountingProfiles.AsNoTracking()
                        .Where(x => x.CompanyId == entry.CompanyId && x.BillId == approvalBillId)
                        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken)
                    : null;
                approvalTargetsSource = profileId.HasValue && approval?.TargetEntityType == ApprovalTargetEntityType.SupplierBillAccounting.ToStorageValue() &&
                    approval.TargetEntityId == profileId.Value;
            }
            if (approval?.Status != ApprovalRequestStatus.Approved ||
                entry.RequiresApproval && !string.Equals(approvedVersion, entry.SourceVersion?.Trim(), StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(entry.ApprovalPayloadHash) &&
                !string.Equals(approvedPayloadHash, entry.ApprovalPayloadHash.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !approvalTargetsSource)
            {
                issues.Add(new(AccountingPostingReasonCodes.ApprovalInvalid, "The accounting approval is missing, stale, or no longer approved."));
            }
        }

        if (entry.OriginalLedgerEntryId.HasValue)
        {
            var originalExists = await _dbContext.LedgerEntries.AsNoTracking().AnyAsync(x =>
                x.CompanyId == entry.CompanyId && x.Id == entry.OriginalLedgerEntryId.Value && x.Status == LedgerEntryStatuses.Posted,
                cancellationToken);
            if (!originalExists || string.IsNullOrWhiteSpace(entry.CorrectionReason))
                issues.Add(new(AccountingPostingReasonCodes.CorrectionReasonRequired,
                    "A correction must reference a posted journal and include an explicit reason."));
        }

        if ((entry.Evidence?.Count ?? 0) > 0)
        {
            var documentIds = entry.Evidence!.Select(x => x.DocumentId).Distinct().ToArray();
            var documents = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking()
                .Where(x => x.CompanyId == entry.CompanyId && documentIds.Contains(x.Id)).ToListAsync(cancellationToken);
            if (documents.Count != documentIds.Length)
                issues.Add(new(AccountingPostingReasonCodes.InvalidSource, "One or more evidence documents could not be found."));
            foreach (var evidence in entry.Evidence)
            {
                var document = documents.FirstOrDefault(x => x.Id == evidence.DocumentId);
                var checksum = document?.Metadata.TryGetValue("checksum_sha256", out var checksumNode) == true ? checksumNode?.ToString() : null;
                if (document is not null && !string.Equals(checksum, evidence.ContentHash, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new(AccountingPostingReasonCodes.InvalidSource, "An evidence document changed after the draft was prepared.", evidence.DocumentId));
            }
        }

        var preview = BuildPreview(entry, configuration, issues);
        if (preview.Difference != 0m)
            issues.Add(new(AccountingPostingReasonCodes.UnbalancedEntry, "Total debits must equal total credits."));
        return BuildPreview(entry, configuration, issues);
    }

    private async Task<long> AllocateVoucherNumberAsync(Guid companyId, Guid seriesId, int fiscalYear, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                MERGE accounting_voucher_sequences WITH (HOLDLOCK) AS target
                USING (SELECT @company_id AS company_id, @series_id AS voucher_series_id, @fiscal_year AS fiscal_year) AS source
                ON target.company_id = source.company_id AND target.voucher_series_id = source.voucher_series_id AND target.fiscal_year = source.fiscal_year
                WHEN MATCHED THEN UPDATE SET last_allocated_number = target.last_allocated_number + 1, updated_utc = @now_utc
                WHEN NOT MATCHED THEN INSERT (id, company_id, voucher_series_id, fiscal_year, last_allocated_number, created_utc, updated_utc)
                    VALUES (@id, @company_id, @series_id, @fiscal_year, 1, @now_utc, @now_utc)
                OUTPUT INSERTED.last_allocated_number;
                """;
            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@company_id", companyId);
            AddParameter(command, "@series_id", seriesId);
            AddParameter(command, "@fiscal_year", fiscalYear);
            AddParameter(command, "@now_utc", nowUtc);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        var sequence = await _dbContext.VoucherSequences
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.VoucherSeriesId == seriesId && item.FiscalYear == fiscalYear, cancellationToken);
        if (sequence is null)
        {
            sequence = new VoucherSequence(Guid.NewGuid(), companyId, seriesId, fiscalYear, 0, nowUtc);
            _dbContext.VoucherSequences.Add(sequence);
        }
        return sequence.Allocate(nowUtc);
    }

    private Task<LedgerPostingIdentity?> FindExistingIdentityAsync(ProposedAccountingEntry entry, CancellationToken cancellationToken)
    {
        var action = NormalizeOptional(entry.Action)?.ToLowerInvariant();
        var sourceType = NormalizeOptional(entry.SourceType)?.ToLowerInvariant();
        var sourceId = NormalizeOptional(entry.SourceId);
        var sourceVersion = NormalizeOptional(entry.SourceVersion);
        var idempotencyKey = NormalizeOptional(entry.IdempotencyKey);
        return _dbContext.LedgerPostingIdentities.AsNoTracking().SingleOrDefaultAsync(identity =>
            identity.CompanyId == entry.CompanyId &&
            ((identity.Action == action && identity.SourceType == sourceType && identity.SourceId == sourceId && identity.SourceVersion == sourceVersion) ||
             identity.IdempotencyKey == idempotencyKey), cancellationToken);
    }

    private static void EnsureMatchingReplay(LedgerPostingIdentity identity, ProposedAccountingEntry entry, string payloadHash)
    {
        if (!string.Equals(identity.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.IdempotencyKey, entry.IdempotencyKey?.Trim(), StringComparison.Ordinal))
        {
            throw Error(AccountingPostingReasonCodes.IdempotencyConflict, "This posting identity was already used with different journal content.", true);
        }
    }

    private void DetachPendingChanges()
    {
        foreach (var tracked in _dbContext.ChangeTracker.Entries().Where(item => item.State != EntityState.Unchanged).ToArray())
            tracked.State = EntityState.Detached;
    }

    private async Task<PostedAccountingJournal> ExecuteWithSqlRetryAsync(
        Func<Task<PostedAccountingJournal>> action,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                DetachPendingChanges();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private static AccountingPostingPreview BuildPreview(ProposedAccountingEntry entry, AccountingConfiguration? configuration, IReadOnlyList<AccountingPostingIssue> issues)
    {
        var precision = configuration?.RoundingPrecision ?? 2;
        var mode = configuration?.RoundingMode ?? AccountingRoundingModeValues.MidpointToEven;
        var lines = entry.Lines ?? [];
        var debit = Round(lines.Sum(line => line.DebitAmount), precision, mode);
        var credit = Round(lines.Sum(line => line.CreditAmount), precision, mode);
        var difference = Round(debit - credit, precision, mode);
        return new AccountingPostingPreview(issues.Count == 0 && difference == 0m, debit, credit, difference, configuration?.BaseCurrency ?? string.Empty, precision, issues.ToArray());
    }

    private static decimal Round(decimal amount, int precision, string mode) =>
        decimal.Round(amount, precision, mode == AccountingRoundingModeValues.AwayFromZero ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven);

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool HasSupportedPrecision(decimal amount, int precision) => amount == decimal.Round(amount, precision, MidpointRounding.ToEven);

    private static int ResolveFiscalYear(DateOnly postingDate, int startMonth, int startDay)
    {
        var boundaryDay = Math.Min(startDay, DateTime.DaysInMonth(postingDate.Year, startMonth));
        return postingDate < new DateOnly(postingDate.Year, startMonth, boundaryDay) ? postingDate.Year - 1 : postingDate.Year;
    }

    private static string ComputePayloadHash(ProposedAccountingEntry entry, Guid? originalId, string? correctionReason)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            entry.CompanyId, entry.FiscalPeriodId,
            VoucherSeriesCode = NormalizeOptional(entry.VoucherSeriesCode)?.ToUpperInvariant(),
            entry.DocumentDate, entry.PostingDate,
            PostingType = NormalizeOptional(entry.PostingType)?.ToLowerInvariant(),
            Description = NormalizeOptional(entry.Description),
            SourceType = NormalizeOptional(entry.SourceType)?.ToLowerInvariant(),
            SourceId = NormalizeOptional(entry.SourceId), SourceVersion = NormalizeOptional(entry.SourceVersion),
            IdempotencyKey = NormalizeOptional(entry.IdempotencyKey), Action = NormalizeOptional(entry.Action)?.ToLowerInvariant(),
            entry.ActorUserId, ActorType = NormalizeOptional(entry.ActorType)?.ToLowerInvariant(),
            EffectivePostedAtUtc = entry.EffectivePostedAtUtc.HasValue ? (DateTime?)NormalizeUtc(entry.EffectivePostedAtUtc.Value) : null,
            entry.ApprovalRequestId, entry.RequiresApproval, originalId,
            entry.ApprovalPayloadHash, entry.OriginalLedgerEntryId,
            CorrectionReason = NormalizeOptional(correctionReason),
            PolicyFacts = NormalizeFacts(entry.PolicyFacts),
            Evidence = (entry.Evidence ?? []).OrderBy(x => x.DocumentId).Select(x => new
            {
                x.DocumentId,
                ContentHash = NormalizeOptional(x.ContentHash)?.ToLowerInvariant(),
                Title = NormalizeOptional(x.Title)
            }),
            Lines = (entry.Lines ?? []).Select(line => new
            {
                line.FinanceAccountId, line.DebitAmount, line.CreditAmount,
                Currency = NormalizeOptional(line.Currency)?.ToUpperInvariant(),
                Description = NormalizeOptional(line.Description), line.CostCenterId,
                TaxFacts = NormalizeFacts(line.TaxFacts), DimensionFacts = NormalizeFacts(line.DimensionFacts)
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static ProposedAccountingEntry BuildProviderSwitchEntry(AccountingProviderSwitchNativeCandidate candidate,
        Guid actorUserId, Guid activationApprovalRequestId)
    {
        if (!candidate.FiscalPeriodId.HasValue || !candidate.PostingDate.HasValue || !candidate.DocumentDate.HasValue)
            throw Error(AccountingPostingReasonCodes.InvalidSource, "The prepared migration journal is missing its period or accounting dates.");
        using var document = JsonDocument.Parse(candidate.PayloadJson);
        var root = document.RootElement;
        var series = String(root, "voucherSeriesCode") ?? throw Error(AccountingPostingReasonCodes.VoucherSeriesNotFound,
            "The prepared migration journal has no approved voucher series.");
        if (!root.TryGetProperty("lines", out var lineArray) || lineArray.ValueKind != JsonValueKind.Array)
            throw Error(AccountingPostingReasonCodes.InvalidLine, "The prepared migration journal has no normalized lines.");
        var lines = lineArray.EnumerateArray().Select(line => new ProposedAccountingLine(
            Guid.TryParse(String(line, "financeAccountId"), out var accountId) ? accountId : Guid.Empty,
            Decimal(line, "debitAmount") ?? Decimal(line, "debit") ?? 0m,
            Decimal(line, "creditAmount") ?? Decimal(line, "credit") ?? 0m,
            String(line, "currency") ?? candidate.Currency ?? string.Empty,
            String(line, "description"),
            Guid.TryParse(String(line, "costCenterId"), out var costCenterId) ? costCenterId : null,
            StringDictionary(line, "taxFacts"), StringDictionary(line, "dimensionFacts"))).ToArray();
        return new ProposedAccountingEntry(candidate.CompanyId, candidate.FiscalPeriodId.Value, series,
            candidate.DocumentDate.Value, candidate.PostingDate.Value,
            candidate.CandidateKind == AccountingProviderSwitchNativeCandidateKinds.OpeningJournal
                ? LedgerPostingTypeValues.SourceDocument
                : String(root, "postingType") ?? LedgerPostingTypeValues.SourceDocument,
            String(root, "description") ?? "Approved accounting provider migration journal",
            "provider_switch_candidate", candidate.Id.ToString("N"), candidate.SourceVersion,
            candidate.IdempotencyKey, lines, actorUserId, activationApprovalRequestId,
            RequiresApproval: false,
            PolicyFacts: new Dictionary<string, string>
            {
                ["providerSwitchId"] = candidate.SwitchId.ToString("D"),
                ["candidateId"] = candidate.Id.ToString("D"),
                ["sourceHash"] = candidate.SourceHash,
                ["activationApprovalId"] = activationApprovalRequestId.ToString("D")
            });

        static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        static decimal? Decimal(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number) ? number : null;
        static IReadOnlyDictionary<string, string> StringDictionary(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string>();
            return value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ToString(), StringComparer.Ordinal);
        }
    }

    internal static IReadOnlyDictionary<string, string> ParseFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    private static string? SerializeFacts(IReadOnlyDictionary<string, string>? facts) =>
        facts is null || facts.Count == 0 ? null : JsonSerializer.Serialize(NormalizeFacts(facts));

    private static SortedDictionary<string, string> NormalizeFacts(IReadOnlyDictionary<string, string>? facts) =>
        facts is null
            ? new SortedDictionary<string, string>(StringComparer.Ordinal)
            : new SortedDictionary<string, string>(facts.ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim()), StringComparer.Ordinal);

    private static bool ValidFacts(IReadOnlyDictionary<string, string>? facts) =>
        facts is null || facts.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Trim().Length <= 100 && !string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Trim().Length <= 1000);

    private static void TryValidateRequired(string? value, string name, int maxLength, ICollection<AccountingPostingIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            issues.Add(new(AccountingPostingReasonCodes.InvalidSource, $"{name} is required and must be {maxLength} characters or fewer."));
    }

    private static string NormalizeSeries(string value) => NormalizeRequired(value, nameof(value), 32).ToUpperInvariant();
    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Error(AccountingPostingReasonCodes.InvalidSource, $"{name} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw Error(AccountingPostingReasonCodes.InvalidSource, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AccountingPostingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
