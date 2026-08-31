using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class YearEndRolloverService : IYearEndRolloverService
{
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.YearEndRollover");
    private static readonly Meter Meter = new("VirtualCompany.YearEndRollover");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("year_end.operations");
    private static readonly Counter<long> Blockers = Meter.CreateCounter<long>("year_end.blockers");
    private static readonly Histogram<double> EvaluationDuration = Meter.CreateHistogram<double>("year_end.evaluation.duration", "ms");
    private static readonly CompanyMembershipRole[] PrepareRoles = [CompanyMembershipRole.Owner, CompanyMembershipRole.Admin, CompanyMembershipRole.Manager];
    private static readonly CompanyMembershipRole[] ReviewRoles = [CompanyMembershipRole.Owner, CompanyMembershipRole.Admin, CompanyMembershipRole.Manager, CompanyMembershipRole.FinanceApprover];

    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IAccountingPostingService _posting;
    private readonly IKnowledgeAccessPolicyEvaluator _knowledgeAccess;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;

    public YearEndRolloverService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        IAccountingPostingService posting, IKnowledgeAccessPolicyEvaluator knowledgeAccess,
        IAuditEventWriter audit, TimeProvider clock)
    {
        _db = db; _memberships = memberships; _posting = posting; _knowledgeAccess = knowledgeAccess;
        _audit = audit; _clock = clock;
    }

    public async Task<IReadOnlyList<YearEndRunSummaryDto>> ListAsync(ListYearEndRunsQuery query, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, ReviewRoles, cancellationToken);
        var take = Math.Clamp(query.Take, 1, 100);
        var runs = await _db.YearEndRuns.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId).OrderByDescending(x => x.FiscalYearStart).Take(take).ToListAsync(cancellationToken);
        var ids = runs.Select(x => x.Id).ToArray();
        var snapshots = await _db.YearEndReadinessSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId && ids.Contains(x.RunId))
            .GroupBy(x => x.RunId).Select(x => x.OrderByDescending(y => y.SnapshotNumber).First()).ToListAsync(cancellationToken);
        var proposals = await _db.YearEndRetainedEarningsProposals.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.CompanyId == query.CompanyId && ids.Contains(x.RunId))
            .GroupBy(x => x.RunId).Select(x => x.OrderByDescending(y => y.PreparedUtc).First()).ToListAsync(cancellationToken);
        return runs.Select(run => new YearEndRunSummaryDto(run.Id, run.FiscalYearStart, run.FiscalYearEnd, run.Status,
            snapshots.FirstOrDefault(x => x.RunId == run.Id)?.BlockerCount ?? 0,
            proposals.FirstOrDefault(x => x.RunId == run.Id)?.NetIncome ?? 0m,
            proposals.FirstOrDefault(x => x.RunId == run.Id)?.Currency ?? string.Empty, run.UpdatedUtc, run.Version)).ToArray();
    }

    public async Task<YearEndRunDto> GetAsync(GetYearEndRunQuery query, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, ReviewRoles, cancellationToken);
        return await LoadDtoAsync(query.CompanyId, query.RunId, cancellationToken);
    }

    public async Task<YearEndRunDto> PrepareAsync(PrepareYearEndRunCommand command, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("year_end.prepare");
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var requestHash = Hash(new { command.FiscalYearStart, command.TargetFiscalPeriodId,
            command.RetainedEarningsAccountId, command.OpeningBalanceClearingAccountId, command.VoucherSeriesCode });
        var existingOperation = await _db.YearEndOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existingOperation is not null)
        {
            EnsureReplay(existingOperation, requestHash);
            return await LoadDtoAsync(command.CompanyId, existingOperation.RunId, cancellationToken);
        }
        var existingRun = await _db.YearEndRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.FiscalYearStart == command.FiscalYearStart, cancellationToken);
        if (existingRun is not null)
            throw Error(YearEndReasonCodes.IdempotencyConflict, "A different year-end run already exists for this fiscal year.", true, existingRun.Version);

        var run = new YearEndRun(Guid.NewGuid(), command.CompanyId, command.FiscalYearStart,
            command.FiscalYearStart.AddYears(1).AddDays(-1), command.TargetFiscalPeriodId,
            command.RetainedEarningsAccountId, command.OpeningBalanceClearingAccountId,
            command.VoucherSeriesCode, member.UserId, Now());
        _db.YearEndRuns.Add(run);
        var evaluation = await EvaluateAsync(run, cancellationToken);
        ApplyEvaluation(run, evaluation, member, 1);
        AddOperation(run, "prepare", command.IdempotencyKey, requestHash);
        AddHistory(run, "prepared", YearEndRunStatuses.Draft, run.Status, member.UserId,
            evaluation.EvidenceHash, evaluation.IsReady ? "Year-end readiness prepared." : "Year-end readiness prepared with blockers.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndPrepared,
            evaluation.IsReady ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            "Prepared a versioned year-end readiness snapshot and opening-balance candidates.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        Operations.Add(1, Key("operation", "prepare")); Blockers.Add(evaluation.Checks.Count(x => !x.Passed && x.Blocking));
        return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> RefreshReadinessAsync(RefreshYearEndReadinessCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken);
        EnsureVersion(run.Version, command.ExpectedVersion); var requestHash = Hash(command);
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken))
            return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        var before = run.Status; var next = await _db.YearEndReadinessSnapshots.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == command.CompanyId && x.RunId == run.Id, cancellationToken) + 1;
        foreach (var old in await _db.YearEndReadinessSnapshots.IgnoreQueryFilters().Where(x =>
            x.CompanyId == command.CompanyId && x.RunId == run.Id && x.Status != YearEndReadinessStatuses.Stale).ToListAsync(cancellationToken)) old.MarkStale();
        var previousCandidates = await _db.YearEndOpeningBalanceCandidates.IgnoreQueryFilters().Where(x =>
            x.CompanyId == command.CompanyId && x.RunId == run.Id).ToListAsync(cancellationToken);
        _db.YearEndOpeningBalanceCandidates.RemoveRange(previousCandidates);
        var evaluation = await EvaluateAsync(run, cancellationToken); ApplyEvaluation(run, evaluation, member, next);
        AddOperation(run, "refresh", command.IdempotencyKey, requestHash); AddHistory(run, "readiness_refreshed", before,
            run.Status, member.UserId, evaluation.EvidenceHash, "Year-end readiness evidence was refreshed from authoritative sources.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndPrepared,
            evaluation.IsReady ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            "Refreshed year-end readiness and invalidated prior approval evidence.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> SubmitAsync(SubmitYearEndRunCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        var snapshot = await CurrentSnapshotAsync(run, cancellationToken); EnsureHash(snapshot.EvidenceHash, command.ExpectedEvidenceHash);
        if (snapshot.Status != YearEndReadinessStatuses.Ready) throw Error(YearEndReasonCodes.NotReady, "Resolve every year-end blocker before submission.");
        var requestHash = Hash(command); if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        var proposal = await CurrentProposalAsync(run, snapshot.EvidenceHash, cancellationToken); var before = run.Status;
        try { run.Submit(member.UserId, snapshot.EvidenceHash, Now()); proposal.Submit(Now()); }
        catch (InvalidOperationException exception) { throw State(exception, run.Version); }
        AddSignOff(run, "submitted", YearEndApprovalDecisions.Pending, snapshot.EvidenceHash, member, null);
        AddOperation(run, "submit", command.IdempotencyKey, requestHash); AddHistory(run, "submitted", before, run.Status,
            member.UserId, snapshot.EvidenceHash, "Year-end run submitted for independent approval.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndSubmitted, AuditEventOutcomes.Pending,
            "Submitted the exact year-end evidence hash for independent approval.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> ReviewAsync(ReviewYearEndRunCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, ReviewRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        EnsureHash(run.ApprovedEvidenceHash, command.ExpectedEvidenceHash); var requestHash = Hash(command);
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        var proposal = await CurrentProposalAsync(run, command.ExpectedEvidenceHash, cancellationToken); var before = run.Status;
        try { run.Review(member.UserId, command.Approve, command.ExpectedEvidenceHash, Now()); proposal.Review(member.UserId, command.Approve, Now()); }
        catch (InvalidOperationException exception) { throw exception.Message.Contains("preparer", StringComparison.OrdinalIgnoreCase)
            ? Error(YearEndReasonCodes.SelfReview, exception.Message, true, run.Version) : State(exception, run.Version); }
        AddSignOff(run, "reviewed", command.Approve ? YearEndApprovalDecisions.Approved : YearEndApprovalDecisions.Rejected,
            command.ExpectedEvidenceHash, member, command.Reason); AddOperation(run, "review", command.IdempotencyKey, requestHash);
        AddHistory(run, command.Approve ? "approved" : "rejected", before, run.Status, member.UserId,
            command.ExpectedEvidenceHash, command.Approve ? "Independent year-end approval recorded." : "Year-end approval rejected.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndReviewed,
            command.Approve ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected,
            command.Approve ? "Approved the exact year-end evidence snapshot." : "Rejected the year-end proposal.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> ExecuteAsync(ExecuteYearEndRunCommand command, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("year_end.execute");
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var requestHash = Hash(command); ValidateIdempotency(command.IdempotencyKey);
        var replay = await _db.YearEndOperations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (replay is not null) { EnsureReplay(replay, requestHash); return await LoadDtoAsync(command.CompanyId, replay.RunId, cancellationToken); }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
            EnsureHash(run.ApprovedEvidenceHash, command.ExpectedEvidenceHash);
            var live = await EvaluateAsync(run, cancellationToken);
            if (!live.IsReady || !string.Equals(live.EvidenceHash, command.ExpectedEvidenceHash, StringComparison.Ordinal))
                throw Error(YearEndReasonCodes.EvidenceStale, "Authoritative year-end evidence changed after approval. Refresh and review again.", true, run.Version);
            var proposal = await CurrentProposalAsync(run, command.ExpectedEvidenceHash, cancellationToken);
            var candidates = await _db.YearEndOpeningBalanceCandidates.IgnoreQueryFilters().Where(x =>
                x.CompanyId == command.CompanyId && x.RunId == run.Id).OrderBy(x => x.AccountCode).ThenBy(x => x.DimensionKey).ToListAsync(cancellationToken);
            if (candidates.Count == 0) throw Error(YearEndReasonCodes.NotReady, "No retained closing balances are available for rollover.");
            run.BeginExecution(member.UserId, command.ExpectedEvidenceHash, Now());
            var target = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == run.TargetFiscalPeriodId, cancellationToken);
            var config = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
            var postingDate = DateOnly.FromDateTime(target.StartUtc);
            var currentResult = candidates.SingleOrDefault(x => x.DimensionKey.EndsWith("current_result", StringComparison.Ordinal));
            var openingCandidates = candidates.Where(x => x != currentResult && x.ClosingFunctionalBalance != 0m).ToArray();
            var openingLines = openingCandidates.Select(x => CandidateLine(x, config.BaseCurrency)).ToList();
            var openingSigned = openingCandidates.Sum(x => x.ClosingFunctionalBalance);
            if (openingSigned != 0m) openingLines.Add(SignedLine(run.OpeningBalanceClearingAccountId, -openingSigned,
                config.BaseCurrency, "Opening balance clearing", new Dictionary<string, string> { ["year_end_scope"] = "opening_balance_clearing" }));
            if (openingLines.Count < 2) throw Error(YearEndReasonCodes.NotReady, "The opening-balance journal requires at least two balanced lines.");
            var opening = await _posting.PostAsync(new PostAccountingEntryCommand(new ProposedAccountingEntry(
                run.CompanyId, run.TargetFiscalPeriodId, run.VoucherSeriesCode, postingDate, postingDate,
                LedgerPostingTypeValues.YearEnd, $"Opening balances for fiscal year {run.FiscalYearStart:yyyy}",
                "year_end_opening_balance", run.Id.ToString("N"), command.ExpectedEvidenceHash,
                $"year-end:{run.Id:N}:opening:{command.ExpectedEvidenceHash}", openingLines, member.UserId,
                PolicyFacts: new Dictionary<string, string> { ["yearEndRunId"] = run.Id.ToString("N"), ["evidenceHash"] = command.ExpectedEvidenceHash }),
                command.CorrelationId), cancellationToken);

            Guid? retainedId = null;
            if (currentResult is not null && proposal.NetIncome != 0m)
            {
                var retainedLines = new[]
                {
                    SignedLine(run.OpeningBalanceClearingAccountId, proposal.NetIncome, config.BaseCurrency,
                        "Clear current-year result", new Dictionary<string, string> { ["year_end_scope"] = "opening_balance_clearing" }),
                    CandidateLine(currentResult, config.BaseCurrency)
                };
                var retained = await _posting.PostAsync(new PostAccountingEntryCommand(new ProposedAccountingEntry(
                    run.CompanyId, run.TargetFiscalPeriodId, run.VoucherSeriesCode, postingDate, postingDate,
                    LedgerPostingTypeValues.YearEnd, $"Transfer {run.FiscalYearStart:yyyy} result to retained earnings",
                    "year_end_retained_earnings", run.Id.ToString("N"), command.ExpectedEvidenceHash,
                    $"year-end:{run.Id:N}:retained:{command.ExpectedEvidenceHash}", retainedLines, member.UserId,
                    PolicyFacts: new Dictionary<string, string> { ["yearEndRunId"] = run.Id.ToString("N"), ["evidenceHash"] = command.ExpectedEvidenceHash }),
                    command.CorrelationId), cancellationToken);
                retainedId = retained.Journal.Id;
            }
            foreach (var candidate in openingCandidates) candidate.MarkPosted(opening.Journal.Id);
            currentResult?.MarkPosted(retainedId ?? opening.Journal.Id);
            proposal.MarkExecuted(); run.MarkExecuted(retainedId, opening.Journal.Id, Now());
            AddSignOff(run, "executed", YearEndApprovalDecisions.Approved, command.ExpectedEvidenceHash, member, null);
            AddOperation(run, "execute", command.IdempotencyKey, requestHash); AddHistory(run, "executed",
                YearEndRunStatuses.Approved, run.Status, member.UserId, command.ExpectedEvidenceHash,
                "Retained-earnings and opening-balance journals committed as one transaction.");
            await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndExecuted, AuditEventOutcomes.Succeeded,
                "Committed the approved year-end journal chain atomically.", command.CorrelationId, cancellationToken);
            await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken); _db.ChangeTracker.Clear();
            if (exception is YearEndRolloverException) throw;
            var failed = await _db.YearEndRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.RunId, cancellationToken);
            if (failed is not null)
            {
                failed.Fail(YearEndReasonCodes.PostingFailed, "The year-end journal chain was rolled back; no partial journal remains.", Now());
                AddHistory(failed, "execution_failed", YearEndRunStatuses.Approved, failed.Status, member.UserId,
                    command.ExpectedEvidenceHash, "Year-end execution failed and the transaction was rolled back.");
                await WriteAuditAsync(failed, member.UserId, AuditEventActions.AccountingYearEndFailed, AuditEventOutcomes.Failed,
                    "Year-end execution failed; the journal chain was rolled back.", command.CorrelationId, cancellationToken);
                await SaveAsync(cancellationToken);
            }
            throw Error(YearEndReasonCodes.PostingFailed, "Year-end posting failed. Neither journal was committed.", true, failed?.Version);
        }
        Operations.Add(1, Key("operation", "execute")); return await LoadDtoAsync(command.CompanyId, command.RunId, cancellationToken);
    }

    public async Task<YearEndRunDto> ReconcileAsync(ReconcileYearEndRunCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        EnsureHash(run.ApprovedEvidenceHash, command.ExpectedEvidenceHash); var requestHash = Hash(command);
        if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        if (!run.OpeningBalanceLedgerEntryId.HasValue) throw Error(YearEndReasonCodes.InvalidState, "The opening-balance journal has not been posted.");
        var journalIds = new[] { run.OpeningBalanceLedgerEntryId.Value, run.RetainedEarningsLedgerEntryId ?? Guid.Empty }.Where(x => x != Guid.Empty).ToArray();
        var lines = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == command.CompanyId && journalIds.Contains(x.LedgerEntryId)).ToListAsync(cancellationToken);
        var actual = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var facts = ParseFacts(line.DimensionFactsJson);
            if (facts.TryGetValue("year_end_candidate_id", out var candidateText) && Guid.TryParse(candidateText, out var candidateId))
                actual[candidateId] = actual.GetValueOrDefault(candidateId) + line.SignedAmount;
        }
        var candidates = await _db.YearEndOpeningBalanceCandidates.IgnoreQueryFilters().Where(x =>
            x.CompanyId == command.CompanyId && x.RunId == run.Id).OrderBy(x => x.AccountCode).ThenBy(x => x.DimensionKey).ToListAsync(cancellationToken);
        foreach (var candidate in candidates) candidate.Reconcile(actual.GetValueOrDefault(candidate.Id));
        var checksum = Hash(candidates.Select(x => new { x.Id, x.ClosingFunctionalBalance, Actual = actual.GetValueOrDefault(x.Id) }).ToArray());
        var matched = candidates.All(x => x.Status == YearEndCandidateStatuses.Matched); var before = run.Status;
        run.Reconcile(member.UserId, checksum, matched, Now()); AddOperation(run, "reconcile", command.IdempotencyKey, requestHash);
        AddHistory(run, "reconciled", before, run.Status, member.UserId, command.ExpectedEvidenceHash,
            matched ? "Opening balances match prior-year closing balances by account, currency, and dimension." : "Opening-balance reconciliation found differences.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndReconciled,
            matched ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            matched ? "Verified opening balances against retained closing candidates." : "Opening-balance differences block year-end finalization.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        if (!matched) throw Error(YearEndReasonCodes.ReconciliationFailed, "Opening balances differ from the retained closing candidates.", true, run.Version);
        return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> FinalizeAsync(FinalizeYearEndRunCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        var requestHash = Hash(command); if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        var before = run.Status; try { run.Complete(member.UserId, Now()); } catch (InvalidOperationException exception) { throw State(exception, run.Version); }
        AddOperation(run, "finalize", command.IdempotencyKey, requestHash); AddHistory(run, "finalized", before, run.Status,
            member.UserId, run.OpeningBalanceChecksum ?? run.ApprovedEvidenceHash!, "Fiscal-year rollover finalized with reconciled opening balances.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndFinalized, AuditEventOutcomes.Succeeded,
            "Finalized the reconciled fiscal-year rollover without changing prior-year journals or snapshots.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public async Task<YearEndRunDto> RecordSubsequentEventAsync(RecordYearEndSubsequentEventCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, PrepareRoles, cancellationToken);
        var run = await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken);
        if (run.Status is not (YearEndRunStatuses.Reconciled or YearEndRunStatuses.Completed)) throw Error(YearEndReasonCodes.InvalidState, "Subsequent events can be recorded after rollover reconciliation.");
        if (command.EventDate <= run.FiscalYearEnd) throw Error(YearEndReasonCodes.InvalidState, "A subsequent event must occur after the closed fiscal year.");
        await RequireCompanyMemberReferenceAsync(command.CompanyId, command.OwnerUserId, cancellationToken);
        if (command.EvidenceDocumentId.HasValue) await RequireAccessibleDocumentAsync(command.CompanyId, command.EvidenceDocumentId.Value, member, cancellationToken);
        var requestHash = Hash(command); if (await ReplayAsync(command.CompanyId, command.IdempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
        var item = new YearEndSubsequentEvent(Guid.NewGuid(), command.CompanyId, run.Id, command.EventDate,
            command.Title, command.Description, command.EstimatedAmount, command.Currency, command.Decision,
            command.OwnerUserId, command.EvidenceDocumentId, member.UserId, Now());
        _db.YearEndSubsequentEvents.Add(item); AddOperation(run, "subsequent_event_record", command.IdempotencyKey, requestHash);
        AddHistory(run, "subsequent_event_recorded", run.Status, run.Status, member.UserId,
            run.OpeningBalanceChecksum ?? run.ApprovedEvidenceHash!, "Subsequent event recorded without changing prior-year accounting.");
        await WriteAuditAsync(run, member.UserId, AuditEventActions.AccountingYearEndSubsequentEventRecorded, AuditEventOutcomes.Succeeded,
            "Recorded a subsequent event and its proposed disclosure or correction path.", command.CorrelationId, cancellationToken, AuditTargetTypes.YearEndSubsequentEvent, item.Id);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(command.CompanyId, run.Id, cancellationToken);
    }

    public Task<YearEndRunDto> SubmitSubsequentEventAsync(SubmitYearEndSubsequentEventCommand command, CancellationToken cancellationToken) =>
        MutateEventAsync(command.CompanyId, command.RunId, command.EventId, command.ExpectedVersion, command.IdempotencyKey,
            Hash(command), command.ActorUserId, PrepareRoles, "subsequent_event_submit", command.CorrelationId,
            (item, member) => { item.Submit(member.UserId, Now()); return Task.CompletedTask; }, cancellationToken);

    public Task<YearEndRunDto> ReviewSubsequentEventAsync(ReviewYearEndSubsequentEventCommand command, CancellationToken cancellationToken) =>
        MutateEventAsync(command.CompanyId, command.RunId, command.EventId, command.ExpectedVersion, command.IdempotencyKey,
            Hash(command), command.ActorUserId, ReviewRoles, "subsequent_event_review", command.CorrelationId,
            (item, member) => { item.Review(member.UserId, command.Approve, Now()); return Task.CompletedTask; }, cancellationToken);

    public async Task<YearEndRunDto> LinkCorrectionAsync(LinkYearEndCorrectionCommand command, CancellationToken cancellationToken)
    {
        return await MutateEventAsync(command.CompanyId, command.RunId, command.EventId, command.ExpectedVersion,
            command.IdempotencyKey, Hash(command), command.ActorUserId, PrepareRoles, "correction_link", command.CorrelationId,
            async (item, member) =>
            {
                if (item.Decision == SubsequentEventDecisions.PostForward)
                {
                    var journal = command.CorrectionLedgerEntryId.HasValue ? await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                        .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CorrectionLedgerEntryId &&
                            x.Status == LedgerEntryStatuses.Posted, cancellationToken) : null;
                    if (journal?.PostingDate is null || journal.PostingDate <= (await LoadRunAsync(command.CompanyId, command.RunId, cancellationToken)).FiscalYearEnd)
                        throw Error(YearEndReasonCodes.CrossCompanyReference, "The forward correction must be a posted journal in a later period.");
                }
                if (item.Decision == SubsequentEventDecisions.RequestReopen)
                {
                    var reopen = command.ReopenRequestId.HasValue ? await _db.AccountingCloseReopenRequests.IgnoreQueryFilters().AsNoTracking()
                        .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ReopenRequestId &&
                            (x.Status == AccountingCloseReopenStatuses.Approved || x.Status == AccountingCloseReopenStatuses.Executed), cancellationToken) : null;
                    if (reopen is null) throw Error(YearEndReasonCodes.CrossCompanyReference, "The correction requires an approved company-scoped reopen request.");
                }
                item.LinkResolution(command.CorrectionLedgerEntryId, command.ReopenRequestId, Now());
                _db.YearEndCorrectionRecords.Add(new YearEndCorrectionRecord(Guid.NewGuid(), command.CompanyId,
                    command.RunId, item.Id, item.Decision, command.CorrectionLedgerEntryId, command.ReopenRequestId,
                    command.Reason, member.UserId, Now()));
            }, cancellationToken);
    }

    private async Task<YearEndRunDto> MutateEventAsync(Guid companyId, Guid runId, Guid eventId, long expectedVersion,
        string idempotencyKey, string requestHash, Guid actorUserId, IReadOnlyCollection<CompanyMembershipRole> roles,
        string operation, string? correlationId, Func<YearEndSubsequentEvent, ResolvedCompanyMembershipContext, Task> mutation,
        CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, actorUserId, roles, cancellationToken); ValidateIdempotency(idempotencyKey);
        if (await ReplayAsync(companyId, idempotencyKey, requestHash, cancellationToken)) return await LoadDtoAsync(companyId, runId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, cancellationToken);
        var item = await _db.YearEndSubsequentEvents.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.RunId == runId && x.Id == eventId, cancellationToken)
            ?? throw Error(YearEndReasonCodes.NotFound, "The subsequent event was not found.");
        EnsureVersion(item.Version, expectedVersion); var before = item.Status;
        try { await mutation(item, member); }
        catch (InvalidOperationException exception) { throw exception.Message.Contains("own", StringComparison.OrdinalIgnoreCase)
            ? Error(YearEndReasonCodes.SelfReview, exception.Message, true, item.Version) : State(exception, item.Version); }
        AddOperation(run, operation, idempotencyKey, requestHash); AddHistory(run, operation, run.Status, run.Status,
            member.UserId, run.OpeningBalanceChecksum ?? run.ApprovedEvidenceHash!, $"Subsequent event changed from {before} to {item.Status}.");
        var auditAction = operation == "correction_link" ? AuditEventActions.AccountingYearEndCorrectionLinked : AuditEventActions.AccountingYearEndSubsequentEventReviewed;
        await WriteAuditAsync(run, member.UserId, auditAction, AuditEventOutcomes.Succeeded,
            "Updated the governed subsequent-event lifecycle.", correlationId, cancellationToken, AuditTargetTypes.YearEndSubsequentEvent, item.Id);
        await SaveAsync(cancellationToken); return await LoadDtoAsync(companyId, runId, cancellationToken);
    }

    private async Task<Evaluation> EvaluateAsync(YearEndRun run, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp(); var now = Now(); var checks = new List<YearEndReadinessCheckDto>();
        var startUtc = run.FiscalYearStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endExclusive = run.FiscalYearEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periods = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            x.StartUtc < endExclusive && x.EndUtc > startUtc).OrderBy(x => x.StartUtc).ToListAsync(cancellationToken);
        var exactPeriods = periods.Count == 12 && periods.FirstOrDefault()?.StartUtc == startUtc && periods.LastOrDefault()?.EndUtc == endExclusive &&
            periods.Zip(periods.Skip(1), (left, right) => left.EndUtc == right.StartUtc).All(x => x);
        AddCheck(checks, "fiscal_periods", "Fiscal year periods", exactPeriods,
            exactPeriods ? 0 : Math.Max(1, Math.Abs(12 - periods.Count)), "The run requires twelve exact, contiguous, non-overlapping fiscal periods.", "fiscal_period", null, now);
        AddCheck(checks, "period_locks", "Period close and reporting locks", periods.Count == 12 && periods.All(x => x.IsClosed && x.IsReportingLocked),
            periods.Count(x => !x.IsClosed || !x.IsReportingLocked), "Every source period must be closed and reporting-locked.", "fiscal_period", periods.FirstOrDefault(x => !x.IsClosed || !x.IsReportingLocked)?.Id, now);
        var target = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId && x.Id == run.TargetFiscalPeriodId, cancellationToken);
        AddCheck(checks, "target_period", "Next-year opening period", target is not null && target.StartUtc == endExclusive && !target.IsClosed && !target.IsReportingLocked,
            target is null ? 1 : 0, "The target must be the first open, unlocked period immediately after the fiscal year.", "fiscal_period", target?.Id, now);
        var accounts = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            (x.Id == run.RetainedEarningsAccountId || x.Id == run.OpeningBalanceClearingAccountId)).ToListAsync(cancellationToken);
        var accountReady = accounts.Count == 2 && accounts.All(x => x.AccountClass == FinanceAccountClassValues.Equity && x.IsPostingEnabled && x.PostingRestriction != FinanceAccountPostingRestrictionValues.All);
        AddCheck(checks, "year_end_accounts", "Retained earnings mapping", accountReady, accountReady ? 0 : 1,
            "Retained earnings and opening clearing require distinct active equity accounts.", "finance_account", accounts.FirstOrDefault(x => x.AccountClass != FinanceAccountClassValues.Equity || !x.IsPostingEnabled)?.Id, now);
        var finalPeriod = periods.LastOrDefault();
        var closeSnapshot = finalPeriod is null ? null : await _db.AccountingCloseReadinessSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.Status == AccountingCloseReadinessStatuses.Locked && x.IsReady &&
                _db.AccountingCloseInstances.IgnoreQueryFilters().Any(c => c.Id == x.CloseInstanceId && c.FiscalPeriodId == finalPeriod.Id))
            .OrderByDescending(x => x.SnapshotNumber).FirstOrDefaultAsync(cancellationToken);
        var closeEvidence = closeSnapshot is not null;
        AddCheck(checks, "close_readiness", "Subledgers and control accounts", closeEvidence, closeEvidence ? 0 : 1,
            "The final period needs a locked governed close snapshot covering subledgers, controls, tasks, and sign-offs.", "fiscal_period", finalPeriod?.Id, now);
        var statements = finalPeriod is null ? [] : await _db.FinancialStatementSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.FiscalPeriodId == finalPeriod.Id)
            .Select(x => new { x.Id, x.StatementType, x.VersionNumber, x.BalancesChecksum, x.GeneratedAtUtc }).ToArrayAsync(cancellationToken);
        var suiteReports = finalPeriod is null ? [] : await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.FiscalPeriodId == finalPeriod.Id)
            .Select(x => new { x.Id, x.ReportKind, x.Checksum, x.ReportDefinitionHash, x.CreatedUtc }).ToArrayAsync(cancellationToken);
        var reportsReady = statements.Length >= 2 && suiteReports.Any(x => x.ReportKind == FinancialReportKinds.CashFlow) && suiteReports.Any(x => x.ReportKind == FinancialReportKinds.EquityChanges);
        AddCheck(checks, "reports", "Financial reports", reportsReady, reportsReady ? 0 : 1,
            "Balance sheet, profit and loss, cash flow, and equity changes must be current for the final period.", "fiscal_period", finalPeriod?.Id, now);
        var dueCompliance = await _db.ComplianceObligationInstances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId && x.DueDate <= DateOnly.FromDateTime(now))
            .Select(x => new { x.Id, x.Status, x.SourceHash, x.Version, x.UpdatedUtc }).ToArrayAsync(cancellationToken);
        var overdueCompliance = dueCompliance.Where(x => x.Status != ComplianceObligationStatuses.AuthorityApproved &&
            x.Status != ComplianceObligationStatuses.AuthorityReceived && x.Status != ComplianceObligationStatuses.ManualSubmissionRecorded).ToArray();
        AddCheck(checks, "compliance", "Tax and compliance", overdueCompliance.Length == 0, overdueCompliance.Length,
            "No due tax or compliance obligation may remain without retained submission or authority evidence.", "compliance_obligation", null, now);
        var failedSyncs = await _db.FinanceIntegrationSyncStates.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            (x.Status == FinanceIntegrationSyncStatuses.Failed || x.Status == FinanceIntegrationSyncStatuses.Partial))
            .Select(x => new { x.Id, x.Status, x.ProviderKey, x.EntityType, x.UpdatedUtc }).ToArrayAsync(cancellationToken);
        var providerSwitches = await _db.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            x.Status != AccountingProviderSwitchStatuses.Completed && x.Status != AccountingProviderSwitchStatuses.Cancelled)
            .Select(x => new { x.Id, x.Status, x.Version, x.UpdatedUtc }).ToArrayAsync(cancellationToken);
        var migrationAuthorities = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            x.Authority == AccountingAuthorityValues.Migration)
            .Select(x => new { x.Id, x.Authority, x.Version, x.UpdatedUtc }).ToArrayAsync(cancellationToken);
        var providerBlockerCount = failedSyncs.Length + providerSwitches.Length + migrationAuthorities.Length;
        AddCheck(checks, "provider_work", "Accounting authority and integrations", providerBlockerCount == 0, providerBlockerCount,
            "Failed/partial synchronization and active provider or accounting-authority changes must be resolved before rollover.", "finance_integration", null, now);
        var auditPackage = finalPeriod is null ? null : await _db.AuditPackages.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            x.FiscalPeriodId == finalPeriod.Id && x.Status == AuditPackageStatuses.Final && x.IsFinal)
            .OrderByDescending(x => x.FinalizedUtc).Select(x => new { x.Id, x.ScopeHash, x.ManifestChecksum, x.PackageChecksum, x.Version, x.FinalizedUtc }).FirstOrDefaultAsync(cancellationToken);
        var packageReady = auditPackage is not null;
        AddCheck(checks, "audit_package", "Audit package", packageReady, packageReady ? 0 : 1,
            "A final checksum-verifiable audit package is required for the closing period.", "audit_package", null, now);
        var sourcePeriodIds = periods.Select(x => x.Id).ToArray();
        var ledgerCount = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId &&
            sourcePeriodIds.Contains(x.FiscalPeriodId) && x.Status == LedgerEntryStatuses.Posted, cancellationToken);
        AddCheck(checks, "ledger", "Posted accounting source", ledgerCount > 0, ledgerCount > 0 ? 0 : 1,
            "The fiscal year needs posted journals before opening balances can be generated.", "ledger_entry", null, now);
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId, cancellationToken);
        AddCheck(checks, "configuration", "Accounting configuration", configuration is not null, configuration is null ? 1 : 0,
            "Accounting configuration and base currency are required.", "accounting_configuration", configuration?.Id, now);
        var journalHash = await JournalCutoffHashAsync(run.CompanyId, sourcePeriodIds, cancellationToken);
        var candidateSeeds = configuration is null
            ? new CandidateSeedResult([], 0m)
            : await BuildCandidateSeedsAsync(run, sourcePeriodIds, configuration.BaseCurrency, cancellationToken);
        var netIncome = candidateSeeds.NetIncome;
        var stableEvidence = new { run.FiscalYearStart, run.FiscalYearEnd, run.TargetFiscalPeriodId,
            run.RetainedEarningsAccountId, run.OpeningBalanceClearingAccountId, JournalCutoffHash = journalHash,
            Checks = checks.Select(x => new { x.Code, x.Passed, x.Blocking, x.Count, x.TargetType, x.TargetId }).ToArray(),
            Close = closeSnapshot is null ? null : new { closeSnapshot.Id, closeSnapshot.EvidenceHash, closeSnapshot.TrialBalanceChecksum, closeSnapshot.Version },
            Statements = statements.OrderBy(x => x.StatementType).ThenBy(x => x.Id).ToArray(),
            SuiteReports = suiteReports.OrderBy(x => x.ReportKind).ThenBy(x => x.Id).ToArray(),
            Compliance = dueCompliance.OrderBy(x => x.Id).ToArray(),
            FailedSyncs = failedSyncs.OrderBy(x => x.Id).ToArray(),
            ProviderSwitches = providerSwitches.OrderBy(x => x.Id).ToArray(),
            MigrationAuthorities = migrationAuthorities.OrderBy(x => x.Id).ToArray(),
            AuditPackage = auditPackage,
            Configuration = configuration is null ? null : new { configuration.Id, configuration.BaseCurrency, configuration.Version, configuration.UpdatedUtc },
            Candidates = candidateSeeds.Items.Select(x => new { x.FinanceAccountId, x.SourceCurrency, x.DimensionKey,
                x.ClosingFunctionalBalance, x.ClosingDocumentBalance }).ToArray(), netIncome };
        var evidenceHash = Hash(stableEvidence); EvaluationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new(checks.All(x => !x.Blocking || x.Passed), evidenceHash, journalHash, checks, periods.Count(x => x.IsClosed),
            candidateSeeds.Items, netIncome, configuration?.BaseCurrency ?? string.Empty);
    }

    private async Task<CandidateSeedResult> BuildCandidateSeedsAsync(YearEndRun run, Guid[] periodIds, string baseCurrency, CancellationToken cancellationToken)
    {
        var rows = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId &&
            periodIds.Contains(x.LedgerEntry.FiscalPeriodId) && x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Select(x => new { x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name, x.FinanceAccount.AccountClass,
                x.DocumentCurrency, x.DimensionFactsJson, Functional = x.DebitAmount - x.CreditAmount,
                Document = x.DocumentDebitAmount - x.DocumentCreditAmount }).ToListAsync(cancellationToken);
        var netIncome = -rows.Where(x => x.AccountClass is FinanceAccountClassValues.Income or FinanceAccountClassValues.Expense).Sum(x => x.Functional);
        var items = rows.Where(x => x.AccountClass is FinanceAccountClassValues.Asset or FinanceAccountClassValues.Liability or FinanceAccountClassValues.Equity)
            .GroupBy(x => new { x.FinanceAccountId, x.Code, x.Name, x.AccountClass, x.DocumentCurrency, Facts = x.DimensionFactsJson ?? "{}" })
            .Select(group => new CandidateSeed(group.Key.FinanceAccountId, group.Key.Code, group.Key.Name, group.Key.AccountClass!,
                group.Key.DocumentCurrency ?? baseCurrency, DimensionKey(group.Key.DocumentCurrency ?? baseCurrency, group.Key.Facts),
                AddSourceCurrency(group.Key.Facts, group.Key.DocumentCurrency ?? baseCurrency), group.Sum(x => x.Functional), group.Sum(x => x.Document)))
            .Where(x => x.ClosingFunctionalBalance != 0m || x.ClosingDocumentBalance != 0m).OrderBy(x => x.AccountCode).ThenBy(x => x.DimensionKey).ToList();
        if (netIncome != 0m)
        {
            var facts = JsonSerializer.Serialize(new Dictionary<string, string> { ["source_currency"] = baseCurrency, ["year_end_scope"] = "current_result" });
            var account = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId && x.Id == run.RetainedEarningsAccountId, cancellationToken);
            if (account is not null) items.Add(new CandidateSeed(account.Id, account.Code, account.Name, FinanceAccountClassValues.Equity,
                baseCurrency, $"{baseCurrency} / current_result", facts, -netIncome, -netIncome));
        }
        return new(items, netIncome);
    }

    private void ApplyEvaluation(YearEndRun run, Evaluation evaluation, ResolvedCompanyMembershipContext member, int snapshotNumber)
    {
        var snapshot = new YearEndReadinessSnapshot(Guid.NewGuid(), run.CompanyId, run.Id, snapshotNumber,
            evaluation.IsReady ? YearEndReadinessStatuses.Ready : YearEndReadinessStatuses.Blocked,
            evaluation.EvidenceHash, evaluation.JournalCutoffHash, JsonSerializer.Serialize(evaluation.Checks),
            evaluation.Checks.Count(x => x.Blocking && !x.Passed), evaluation.ClosedPeriodCount, member.UserId, Now());
        _db.YearEndReadinessSnapshots.Add(snapshot);
        var oldCandidates = _db.YearEndOpeningBalanceCandidates.Local.Where(x => x.RunId == run.Id).ToArray();
        if (oldCandidates.Length > 0) _db.YearEndOpeningBalanceCandidates.RemoveRange(oldCandidates);
        foreach (var seed in evaluation.Candidates)
            _db.YearEndOpeningBalanceCandidates.Add(new YearEndOpeningBalanceCandidate(Guid.NewGuid(), run.CompanyId,
                run.Id, seed.FinanceAccountId, seed.AccountCode, seed.AccountName, seed.AccountClass, seed.SourceCurrency,
                seed.DimensionKey, seed.DimensionFactsJson, seed.ClosingFunctionalBalance, seed.ClosingDocumentBalance, Now()));
        _db.YearEndRetainedEarningsProposals.Add(new YearEndRetainedEarningsProposal(Guid.NewGuid(), run.CompanyId,
            run.Id, run.RetainedEarningsAccountId, run.OpeningBalanceClearingAccountId, evaluation.NetIncome,
            evaluation.BaseCurrency.Length == 3 ? evaluation.BaseCurrency : "SEK", evaluation.EvidenceHash, member.UserId, Now()));
        run.ApplyReadiness(snapshot.Id, evaluation.IsReady, Now());
    }

    private async Task<YearEndRunDto> LoadDtoAsync(Guid companyId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await _db.YearEndRuns.IgnoreQueryFilters().AsNoTracking().Include(x => x.TargetFiscalPeriod)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId, cancellationToken)
            ?? throw Error(YearEndReasonCodes.NotFound, "The year-end run was not found.");
        var snapshots = await _db.YearEndReadinessSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderByDescending(x => x.SnapshotNumber).ToListAsync(cancellationToken);
        var current = run.CurrentReadinessSnapshotId.HasValue ? snapshots.FirstOrDefault(x => x.Id == run.CurrentReadinessSnapshotId) : snapshots.FirstOrDefault();
        var proposals = await _db.YearEndRetainedEarningsProposals.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderByDescending(x => x.PreparedUtc).ToListAsync(cancellationToken);
        var proposal = current is null ? proposals.FirstOrDefault() : proposals.FirstOrDefault(x => x.EvidenceHash == current.EvidenceHash) ?? proposals.FirstOrDefault();
        var candidates = await _db.YearEndOpeningBalanceCandidates.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderBy(x => x.AccountCode).ThenBy(x => x.DimensionKey).ToListAsync(cancellationToken);
        var signoffs = await _db.YearEndApprovalSignOffs.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderBy(x => x.OccurredUtc).ToListAsync(cancellationToken);
        var events = await _db.YearEndSubsequentEvents.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderByDescending(x => x.EventDate).ToListAsync(cancellationToken);
        var history = await _db.YearEndHistory.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.RunId == runId).OrderByDescending(x => x.OccurredUtc).ToListAsync(cancellationToken);
        var accountIds = new[] { run.RetainedEarningsAccountId, run.OpeningBalanceClearingAccountId };
        var codes = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var companyName = await _db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.Name).SingleAsync(cancellationToken);
        return new(run.Id, run.CompanyId, companyName, run.FiscalYearStart, run.FiscalYearEnd, run.TargetFiscalPeriodId,
            run.TargetFiscalPeriod.Name, run.VoucherSeriesCode, run.Status, run.PreparedByUserId, run.ApprovedByUserId,
            run.ExecutedByUserId, run.ReconciledByUserId, run.CompletedByUserId, run.ApprovedEvidenceHash,
            run.RetainedEarningsLedgerEntryId, run.OpeningBalanceLedgerEntryId, run.OpeningBalanceChecksum,
            run.FailureCode, run.FailureSummary, run.CreatedUtc, run.UpdatedUtc, run.ApprovedUtc, run.ExecutedUtc,
            run.ReconciledUtc, run.CompletedUtc, run.Version, current is null ? null : MapSnapshot(current),
            proposal is null ? null : new YearEndRetainedEarningsProposalDto(proposal.Id, proposal.RetainedEarningsAccountId,
                codes.GetValueOrDefault(proposal.RetainedEarningsAccountId, "—"), proposal.OpeningBalanceClearingAccountId,
                codes.GetValueOrDefault(proposal.OpeningBalanceClearingAccountId, "—"), proposal.NetIncome, proposal.Currency,
                proposal.EvidenceHash, proposal.Status, proposal.PreparedByUserId, proposal.ReviewedByUserId,
                proposal.PreparedUtc, proposal.ReviewedUtc, proposal.Version),
            candidates.Select(x => new YearEndOpeningBalanceCandidateDto(x.Id, x.FinanceAccountId, x.AccountCode,
                x.AccountName, x.AccountClass, x.SourceCurrency, x.DimensionKey, x.ClosingFunctionalBalance,
                x.ClosingDocumentBalance, x.OpeningFunctionalBalance, x.OpeningDocumentBalance, x.Difference,
                x.Status, x.OpeningLedgerEntryId)).ToArray(),
            signoffs.Select(x => new YearEndSignOffDto(x.Id, x.Action, x.Decision, x.EvidenceHash, x.ActorUserId,
                x.ActorRole, x.Reason, x.OccurredUtc)).ToArray(),
            events.Select(x => new YearEndSubsequentEventDto(x.Id, x.EventDate, x.Title, x.Description,
                x.EstimatedAmount, x.Currency, x.Decision, x.OwnerUserId, x.EvidenceDocumentId, x.Status,
                x.RecordedByUserId, x.ReviewedByUserId, x.CorrectionLedgerEntryId, x.ReopenRequestId,
                x.RecordedUtc, x.UpdatedUtc, x.ResolvedUtc, x.Version)).ToArray(),
            history.Select(x => new YearEndHistoryDto(x.Id, x.Action, x.FromStatus, x.ToStatus, x.ActorUserId,
                x.EvidenceHash, x.Summary, x.OccurredUtc)).ToArray(), AllowedActions(run, current));
    }

    private static YearEndReadinessSnapshotDto MapSnapshot(YearEndReadinessSnapshot x) => new(x.Id, x.SnapshotNumber,
        x.Status, x.EvidenceHash, x.JournalCutoffHash, x.BlockerCount, x.ClosedPeriodCount, x.PreparedByUserId,
        x.PreparedUtc, x.Version, JsonSerializer.Deserialize<YearEndReadinessCheckDto[]>(x.EvidenceJson) ?? []);

    private static IReadOnlyList<string> AllowedActions(YearEndRun run, YearEndReadinessSnapshot? snapshot)
    {
        var actions = new List<string> { "refresh" };
        if (run.Status is YearEndRunStatuses.Reconciled or YearEndRunStatuses.Completed)
            actions.Add("record_subsequent_event");
        if (run.Status == YearEndRunStatuses.Ready && snapshot?.Status == YearEndReadinessStatuses.Ready) actions.Add("submit");
        if (run.Status == YearEndRunStatuses.PendingApproval) actions.Add("review");
        if (run.Status == YearEndRunStatuses.Approved) actions.Add("execute");
        if (run.Status == YearEndRunStatuses.Executed) actions.Add("reconcile");
        if (run.Status == YearEndRunStatuses.Reconciled) actions.Add("finalize");
        return actions;
    }

    private async Task<YearEndRun> LoadRunAsync(Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        await _db.YearEndRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId, cancellationToken)
        ?? throw Error(YearEndReasonCodes.NotFound, "The year-end run was not found.");

    private async Task<YearEndReadinessSnapshot> CurrentSnapshotAsync(YearEndRun run, CancellationToken cancellationToken) =>
        run.CurrentReadinessSnapshotId.HasValue
            ? await _db.YearEndReadinessSnapshots.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == run.CompanyId && x.RunId == run.Id && x.Id == run.CurrentReadinessSnapshotId, cancellationToken)
            : throw Error(YearEndReasonCodes.NotReady, "Prepare year-end readiness first.");

    private async Task<YearEndRetainedEarningsProposal> CurrentProposalAsync(YearEndRun run, string evidenceHash, CancellationToken cancellationToken) =>
        await _db.YearEndRetainedEarningsProposals.IgnoreQueryFilters().OrderByDescending(x => x.PreparedUtc).FirstOrDefaultAsync(x =>
            x.CompanyId == run.CompanyId && x.RunId == run.Id && x.EvidenceHash == evidenceHash, cancellationToken)
        ?? throw Error(YearEndReasonCodes.NotReady, "The retained-earnings proposal does not match current evidence.");

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, Guid? actorUserId,
        IReadOnlyCollection<CompanyMembershipRole> roles, CancellationToken cancellationToken)
    {
        var member = await _memberships.ResolveAsync(companyId, cancellationToken);
        if (member is null || !roles.Contains(member.MembershipRole)) throw new UnauthorizedAccessException("Year-end access is denied.");
        if (actorUserId.HasValue && actorUserId.Value != member.UserId) throw new UnauthorizedAccessException("The actor must match the authenticated user.");
        return member;
    }

    private async Task RequireCompanyMemberReferenceAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw Error(YearEndReasonCodes.CrossCompanyReference, "The referenced owner is not an active member of this company.");
    }

    private async Task RequireAccessibleDocumentAsync(Guid companyId, Guid documentId,
        ResolvedCompanyMembershipContext member, CancellationToken cancellationToken)
    {
        var document = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken)
            ?? throw Error(YearEndReasonCodes.DocumentAccessDenied, "The evidence document is unavailable.");
        var context = new CompanyKnowledgeAccessContext(companyId, member.MembershipId, member.UserId,
            member.MembershipRole.ToStorageValue(), Array.Empty<string>());
        if (!_knowledgeAccess.CanAccess(context, document)) throw Error(YearEndReasonCodes.DocumentAccessDenied, "The evidence document is inaccessible.");
    }

    private async Task<string> JournalCutoffHashAsync(Guid companyId, Guid[] periodIds, CancellationToken cancellationToken)
    {
        var entries = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && periodIds.Contains(x.FiscalPeriodId) && x.Status == LedgerEntryStatuses.Posted)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.FiscalPeriodId, x.UpdatedUtc, Debit = x.Lines.Sum(l => l.DebitAmount), Credit = x.Lines.Sum(l => l.CreditAmount) }).ToArrayAsync(cancellationToken);
        return Hash(entries);
    }

    private static ProposedAccountingLine CandidateLine(YearEndOpeningBalanceCandidate candidate, string baseCurrency)
    {
        var facts = ParseFacts(candidate.DimensionFactsJson); facts["year_end_candidate_id"] = candidate.Id.ToString("N");
        return SignedLine(candidate.FinanceAccountId, candidate.ClosingFunctionalBalance, baseCurrency,
            $"Opening balance {candidate.AccountCode}", facts);
    }

    private static ProposedAccountingLine SignedLine(Guid accountId, decimal signed, string currency, string description,
        IReadOnlyDictionary<string, string> facts) => signed >= 0m
        ? new(accountId, signed, 0m, currency, description, DimensionFacts: facts)
        : new(accountId, 0m, -signed, currency, description, DimensionFacts: facts);

    private static Dictionary<string, string> ParseFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static string AddSourceCurrency(string json, string currency)
    {
        var facts = ParseFacts(json); facts["source_currency"] = currency.ToUpperInvariant(); return JsonSerializer.Serialize(facts.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value));
    }

    private static string DimensionKey(string currency, string json)
    {
        var facts = ParseFacts(json); return facts.Count == 0 ? $"{currency.ToUpperInvariant()} / company" :
            $"{currency.ToUpperInvariant()} / {string.Join(", ", facts.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))}";
    }

    private static void AddCheck(ICollection<YearEndReadinessCheckDto> checks, string code, string label,
        bool passed, int count, string explanation, string? targetType, Guid? targetId, DateTime observedUtc) =>
        checks.Add(new(code, label, passed, true, Math.Max(0, count), explanation, targetType, targetId, observedUtc));

    private void AddSignOff(YearEndRun run, string action, string decision, string evidenceHash,
        ResolvedCompanyMembershipContext member, string? reason) => _db.YearEndApprovalSignOffs.Add(new YearEndApprovalSignOff(
            Guid.NewGuid(), run.CompanyId, run.Id, action, decision, evidenceHash, member.UserId,
            member.MembershipRole.ToStorageValue(), reason, Now()));

    private void AddHistory(YearEndRun run, string action, string from, string to, Guid actor,
        string evidenceHash, string summary) => _db.YearEndHistory.Add(new YearEndHistory(Guid.NewGuid(), run.CompanyId,
            run.Id, action, from, to, actor, evidenceHash, summary, Now()));

    private void AddOperation(YearEndRun run, string operation, string key, string requestHash) =>
        _db.YearEndOperations.Add(new YearEndOperation(Guid.NewGuid(), run.CompanyId, run.Id, operation, key, requestHash, run.Version, Now()));

    private async Task<bool> ReplayAsync(Guid companyId, string key, string requestHash, CancellationToken cancellationToken)
    {
        ValidateIdempotency(key); var operation = await _db.YearEndOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, cancellationToken);
        if (operation is null) return false; EnsureReplay(operation, requestHash); return true;
    }

    private static void EnsureReplay(YearEndOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw Error(YearEndReasonCodes.IdempotencyConflict, "The idempotency key was already used for a different year-end request.", true, operation.ResultVersion);
    }

    private async Task WriteAuditAsync(YearEndRun run, Guid actor, string action, string outcome, string summary,
        string? correlationId, CancellationToken cancellationToken, string targetType = AuditTargetTypes.YearEndRun, Guid? targetId = null) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.User, actor, action,
            targetType, (targetId ?? run.Id).ToString("N"), outcome, summary,
            ["fiscal_periods", "ledger_entries", "close_readiness", "financial_reports", "audit_packages"],
            new Dictionary<string, string?> { ["fiscalYearStart"] = run.FiscalYearStart.ToString("yyyy-MM-dd"),
                ["fiscalYearEnd"] = run.FiscalYearEnd.ToString("yyyy-MM-dd"), ["status"] = run.Status,
                ["evidenceHash"] = run.ApprovedEvidenceHash }, correlationId), cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Error(YearEndReasonCodes.ConcurrencyConflict, "The year-end run changed after it was loaded.", true); }
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static void EnsureVersion(long current, long expected) { if (expected != current) throw Error(YearEndReasonCodes.ConcurrencyConflict, "The year-end run changed after it was loaded.", true, current); }
    private static void EnsureHash(string? current, string expected) { if (string.IsNullOrWhiteSpace(expected) || !string.Equals(current, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal)) throw Error(YearEndReasonCodes.EvidenceStale, "The retained year-end evidence hash no longer matches.", true); }
    private static void ValidateIdempotency(string key) { if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200) throw new ArgumentException("A bounded idempotency key is required.", nameof(key)); }
    private static YearEndRolloverException State(InvalidOperationException exception, long version) => Error(YearEndReasonCodes.InvalidState, exception.Message, true, version);
    private static YearEndRolloverException Error(string code, string message, bool conflict = false, long? version = null) => new(code, message, conflict, version);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static KeyValuePair<string, object?> Key(string key, object? value) => new(key, value);

    private sealed record CandidateSeed(Guid FinanceAccountId, string AccountCode, string AccountName,
        string AccountClass, string SourceCurrency, string DimensionKey, string DimensionFactsJson,
        decimal ClosingFunctionalBalance, decimal ClosingDocumentBalance);
    private sealed record CandidateSeedResult(IReadOnlyList<CandidateSeed> Items, decimal NetIncome);
    private sealed record Evaluation(bool IsReady, string EvidenceHash, string JournalCutoffHash,
        IReadOnlyList<YearEndReadinessCheckDto> Checks, int ClosedPeriodCount,
        IReadOnlyList<CandidateSeed> Candidates, decimal NetIncome, string BaseCurrency);
}
