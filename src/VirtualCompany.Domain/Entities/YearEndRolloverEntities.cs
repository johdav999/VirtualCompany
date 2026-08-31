namespace VirtualCompany.Domain.Entities;

public static class YearEndRunStatuses
{
    public const string Draft = "draft";
    public const string Ready = "ready";
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Executing = "executing";
    public const string Executed = "executed";
    public const string Reconciled = "reconciled";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class YearEndReadinessStatuses
{
    public const string Ready = "ready";
    public const string Blocked = "blocked";
    public const string Stale = "stale";
}

public static class YearEndApprovalDecisions
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class YearEndCandidateStatuses
{
    public const string Proposed = "proposed";
    public const string Posted = "posted";
    public const string Matched = "matched";
    public const string Mismatch = "mismatch";
}

public static class SubsequentEventDecisions
{
    public const string DiscloseOnly = "disclose_only";
    public const string PostForward = "post_forward";
    public const string RequestReopen = "request_reopen";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        DiscloseOnly => DiscloseOnly,
        PostForward => PostForward,
        RequestReopen => RequestReopen,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported subsequent-event decision.")
    };
}

public static class SubsequentEventStatuses
{
    public const string Recorded = "recorded";
    public const string UnderReview = "under_review";
    public const string Approved = "approved";
    public const string Resolved = "resolved";
    public const string Rejected = "rejected";
}

public sealed class YearEndRun : ICompanyOwnedEntity
{
    private YearEndRun() { }

    public YearEndRun(Guid id, Guid companyId, DateOnly fiscalYearStart, DateOnly fiscalYearEnd,
        Guid targetFiscalPeriodId, Guid retainedEarningsAccountId, Guid openingBalanceClearingAccountId,
        string voucherSeriesCode, Guid preparedByUserId, DateTime utcNow)
    {
        if (fiscalYearEnd < fiscalYearStart || fiscalYearEnd != fiscalYearStart.AddYears(1).AddDays(-1))
            throw new ArgumentException("A year-end run must cover one exact fiscal year.");
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId));
        FiscalYearStart = fiscalYearStart; FiscalYearEnd = fiscalYearEnd;
        TargetFiscalPeriodId = YearEndValue.Id(targetFiscalPeriodId, nameof(targetFiscalPeriodId));
        RetainedEarningsAccountId = YearEndValue.Id(retainedEarningsAccountId, nameof(retainedEarningsAccountId));
        OpeningBalanceClearingAccountId = YearEndValue.Id(openingBalanceClearingAccountId, nameof(openingBalanceClearingAccountId));
        if (RetainedEarningsAccountId == OpeningBalanceClearingAccountId)
            throw new ArgumentException("Retained earnings and opening balance clearing must use different accounts.");
        VoucherSeriesCode = YearEndValue.Text(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        PreparedByUserId = YearEndValue.Id(preparedByUserId, nameof(preparedByUserId));
        Status = YearEndRunStatuses.Draft; CreatedUtc = UpdatedUtc = YearEndValue.Utc(utcNow); Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateOnly FiscalYearStart { get; private set; }
    public DateOnly FiscalYearEnd { get; private set; }
    public Guid TargetFiscalPeriodId { get; private set; }
    public Guid RetainedEarningsAccountId { get; private set; }
    public Guid OpeningBalanceClearingAccountId { get; private set; }
    public string VoucherSeriesCode { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid PreparedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? ExecutedByUserId { get; private set; }
    public Guid? ReconciledByUserId { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public Guid? CurrentReadinessSnapshotId { get; private set; }
    public string? ApprovedEvidenceHash { get; private set; }
    public Guid? RetainedEarningsLedgerEntryId { get; private set; }
    public Guid? OpeningBalanceLedgerEntryId { get; private set; }
    public string? OpeningBalanceChecksum { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public DateTime? ReconciledUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public FiscalPeriod TargetFiscalPeriod { get; private set; } = null!;
    public ICollection<YearEndReadinessSnapshot> ReadinessSnapshots { get; } = new List<YearEndReadinessSnapshot>();
    public ICollection<YearEndRetainedEarningsProposal> RetainedEarningsProposals { get; } = new List<YearEndRetainedEarningsProposal>();
    public ICollection<YearEndOpeningBalanceCandidate> OpeningBalanceCandidates { get; } = new List<YearEndOpeningBalanceCandidate>();
    public ICollection<YearEndApprovalSignOff> SignOffs { get; } = new List<YearEndApprovalSignOff>();
    public ICollection<YearEndSubsequentEvent> SubsequentEvents { get; } = new List<YearEndSubsequentEvent>();
    public ICollection<YearEndHistory> History { get; } = new List<YearEndHistory>();

    public void ApplyReadiness(Guid snapshotId, bool isReady, DateTime now)
    {
        if (Status is YearEndRunStatuses.Executing or YearEndRunStatuses.Executed or YearEndRunStatuses.Reconciled or YearEndRunStatuses.Completed)
            throw new InvalidOperationException("An executed year-end run cannot be refreshed.");
        CurrentReadinessSnapshotId = YearEndValue.Id(snapshotId, nameof(snapshotId));
        Status = isReady ? YearEndRunStatuses.Ready : YearEndRunStatuses.Draft;
        ApprovedByUserId = null; ApprovedUtc = null; ApprovedEvidenceHash = null;
        FailureCode = FailureSummary = null; Touch(now);
    }

    public void Submit(Guid actorUserId, string evidenceHash, DateTime now)
    {
        if (Status != YearEndRunStatuses.Ready || actorUserId != PreparedByUserId)
            throw new InvalidOperationException("Only the preparer can submit a ready year-end run.");
        ApprovedEvidenceHash = YearEndValue.Hash(evidenceHash, nameof(evidenceHash));
        Status = YearEndRunStatuses.PendingApproval; Touch(now);
    }

    public void Review(Guid actorUserId, bool approve, string evidenceHash, DateTime now)
    {
        if (Status != YearEndRunStatuses.PendingApproval) throw new InvalidOperationException("The year-end run is not awaiting approval.");
        if (actorUserId == PreparedByUserId) throw new InvalidOperationException("The preparer cannot approve their own year-end run.");
        if (!string.Equals(ApprovedEvidenceHash, YearEndValue.Hash(evidenceHash, nameof(evidenceHash)), StringComparison.Ordinal))
            throw new InvalidOperationException("Year-end evidence changed after submission.");
        ApprovedByUserId = approve ? YearEndValue.Id(actorUserId, nameof(actorUserId)) : null;
        ApprovedUtc = approve ? YearEndValue.Utc(now) : null;
        Status = approve ? YearEndRunStatuses.Approved : YearEndRunStatuses.Ready; Touch(now);
    }

    public void BeginExecution(Guid actorUserId, string evidenceHash, DateTime now)
    {
        if (Status != YearEndRunStatuses.Approved || !ApprovedByUserId.HasValue || ApprovedByUserId == PreparedByUserId)
            throw new InvalidOperationException("Independent approval is required before year-end execution.");
        if (!string.Equals(ApprovedEvidenceHash, YearEndValue.Hash(evidenceHash, nameof(evidenceHash)), StringComparison.Ordinal))
            throw new InvalidOperationException("The approved year-end evidence is stale.");
        ExecutedByUserId = YearEndValue.Id(actorUserId, nameof(actorUserId)); Status = YearEndRunStatuses.Executing;
        FailureCode = FailureSummary = null; Touch(now);
    }

    public void MarkExecuted(Guid? retainedEarningsLedgerEntryId, Guid openingBalanceLedgerEntryId, DateTime now)
    {
        if (Status != YearEndRunStatuses.Executing) throw new InvalidOperationException("The year-end run is not executing.");
        RetainedEarningsLedgerEntryId = retainedEarningsLedgerEntryId.HasValue
            ? YearEndValue.Id(retainedEarningsLedgerEntryId.Value, nameof(retainedEarningsLedgerEntryId)) : null;
        OpeningBalanceLedgerEntryId = YearEndValue.Id(openingBalanceLedgerEntryId, nameof(openingBalanceLedgerEntryId));
        ExecutedUtc = YearEndValue.Utc(now); Status = YearEndRunStatuses.Executed; Touch(now);
    }

    public void Reconcile(Guid actorUserId, string checksum, bool matched, DateTime now)
    {
        if (Status != YearEndRunStatuses.Executed) throw new InvalidOperationException("Only an executed year-end run can be reconciled.");
        ReconciledByUserId = YearEndValue.Id(actorUserId, nameof(actorUserId));
        OpeningBalanceChecksum = YearEndValue.Hash(checksum, nameof(checksum)); ReconciledUtc = YearEndValue.Utc(now);
        Status = matched ? YearEndRunStatuses.Reconciled : YearEndRunStatuses.Failed;
        FailureCode = matched ? null : "opening_balance_mismatch";
        FailureSummary = matched ? null : "Opening balances do not match the retained prior-year closing balances."; Touch(now);
    }

    public void Complete(Guid actorUserId, DateTime now)
    {
        if (Status != YearEndRunStatuses.Reconciled) throw new InvalidOperationException("Reconciliation must pass before finalization.");
        CompletedByUserId = YearEndValue.Id(actorUserId, nameof(actorUserId)); CompletedUtc = YearEndValue.Utc(now);
        Status = YearEndRunStatuses.Completed; Touch(now);
    }

    public void Fail(string code, string summary, DateTime now)
    {
        FailureCode = YearEndValue.Text(code, nameof(code), 100).ToLowerInvariant();
        FailureSummary = YearEndValue.Text(summary, nameof(summary), 1000); Status = YearEndRunStatuses.Failed; Touch(now);
    }

    private void Touch(DateTime now) { UpdatedUtc = YearEndValue.Utc(now); Version++; }
}

public sealed class YearEndRetainedEarningsProposal : ICompanyOwnedEntity
{
    private YearEndRetainedEarningsProposal() { }
    public YearEndRetainedEarningsProposal(Guid id, Guid companyId, Guid runId, Guid retainedEarningsAccountId,
        Guid openingBalanceClearingAccountId, decimal netIncome, string currency, string evidenceHash,
        Guid preparedByUserId, DateTime preparedUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        RetainedEarningsAccountId = YearEndValue.Id(retainedEarningsAccountId, nameof(retainedEarningsAccountId));
        OpeningBalanceClearingAccountId = YearEndValue.Id(openingBalanceClearingAccountId, nameof(openingBalanceClearingAccountId));
        NetIncome = netIncome; Currency = YearEndValue.Text(currency, nameof(currency), 3).ToUpperInvariant();
        EvidenceHash = YearEndValue.Hash(evidenceHash, nameof(evidenceHash)); PreparedByUserId = YearEndValue.Id(preparedByUserId, nameof(preparedByUserId));
        PreparedUtc = YearEndValue.Utc(preparedUtc); Status = YearEndRunStatuses.Ready; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public Guid RetainedEarningsAccountId { get; private set; } public Guid OpeningBalanceClearingAccountId { get; private set; }
    public decimal NetIncome { get; private set; } public string Currency { get; private set; } = null!; public string EvidenceHash { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid PreparedByUserId { get; private set; } public Guid? ReviewedByUserId { get; private set; }
    public DateTime PreparedUtc { get; private set; } public DateTime? ReviewedUtc { get; private set; } public long Version { get; private set; }
    public YearEndRun Run { get; private set; } = null!;
    public void Submit(DateTime now) { if (Status != YearEndRunStatuses.Ready) throw new InvalidOperationException("The retained-earnings proposal is not ready."); Status = YearEndRunStatuses.PendingApproval; Version++; }
    public void Review(Guid actor, bool approve, DateTime now) { if (Status != YearEndRunStatuses.PendingApproval) throw new InvalidOperationException("The retained-earnings proposal is not under review."); if (actor == PreparedByUserId) throw new InvalidOperationException("The preparer cannot review their own retained-earnings proposal."); ReviewedByUserId = actor; ReviewedUtc = YearEndValue.Utc(now); Status = approve ? YearEndRunStatuses.Approved : YearEndRunStatuses.Ready; Version++; }
    public void MarkExecuted() { if (Status != YearEndRunStatuses.Approved) throw new InvalidOperationException("The retained-earnings proposal is not approved."); Status = YearEndRunStatuses.Executed; Version++; }
}

public sealed class YearEndReadinessSnapshot : ICompanyOwnedEntity
{
    private YearEndReadinessSnapshot() { }
    public YearEndReadinessSnapshot(Guid id, Guid companyId, Guid runId, int snapshotNumber, string status,
        string evidenceHash, string journalCutoffHash, string evidenceJson, int blockerCount, int closedPeriodCount,
        Guid preparedByUserId, DateTime preparedUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId));
        RunId = YearEndValue.Id(runId, nameof(runId)); SnapshotNumber = snapshotNumber > 0 ? snapshotNumber : throw new ArgumentOutOfRangeException(nameof(snapshotNumber));
        Status = status is YearEndReadinessStatuses.Ready or YearEndReadinessStatuses.Blocked ? status : throw new ArgumentOutOfRangeException(nameof(status));
        EvidenceHash = YearEndValue.Hash(evidenceHash, nameof(evidenceHash)); JournalCutoffHash = YearEndValue.Hash(journalCutoffHash, nameof(journalCutoffHash));
        EvidenceJson = YearEndValue.Text(evidenceJson, nameof(evidenceJson), 64000); BlockerCount = Math.Max(0, blockerCount);
        ClosedPeriodCount = Math.Max(0, closedPeriodCount); PreparedByUserId = YearEndValue.Id(preparedByUserId, nameof(preparedByUserId));
        PreparedUtc = YearEndValue.Utc(preparedUtc); Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public int SnapshotNumber { get; private set; } public string Status { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!; public string JournalCutoffHash { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!; public int BlockerCount { get; private set; } public int ClosedPeriodCount { get; private set; }
    public Guid PreparedByUserId { get; private set; } public DateTime PreparedUtc { get; private set; } public long Version { get; private set; }
    public YearEndRun Run { get; private set; } = null!;
    public void MarkStale() { if (Status != YearEndReadinessStatuses.Stale) { Status = YearEndReadinessStatuses.Stale; Version++; } }
}

public sealed class YearEndOpeningBalanceCandidate : ICompanyOwnedEntity
{
    private YearEndOpeningBalanceCandidate() { }
    public YearEndOpeningBalanceCandidate(Guid id, Guid companyId, Guid runId, Guid financeAccountId,
        string accountCode, string accountName, string accountClass, string sourceCurrency, string dimensionKey,
        string dimensionFactsJson, decimal closingFunctionalBalance, decimal closingDocumentBalance, DateTime createdUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        FinanceAccountId = YearEndValue.Id(financeAccountId, nameof(financeAccountId)); AccountCode = YearEndValue.Text(accountCode, nameof(accountCode), 32);
        AccountName = YearEndValue.Text(accountName, nameof(accountName), 160); AccountClass = YearEndValue.Text(accountClass, nameof(accountClass), 32).ToLowerInvariant();
        SourceCurrency = YearEndValue.Text(sourceCurrency, nameof(sourceCurrency), 3).ToUpperInvariant(); DimensionKey = YearEndValue.Text(dimensionKey, nameof(dimensionKey), 1000);
        DimensionFactsJson = YearEndValue.Text(dimensionFactsJson, nameof(dimensionFactsJson), 8000);
        ClosingFunctionalBalance = OpeningFunctionalBalance = closingFunctionalBalance;
        ClosingDocumentBalance = OpeningDocumentBalance = closingDocumentBalance;
        Difference = 0m; Status = YearEndCandidateStatuses.Proposed; CreatedUtc = YearEndValue.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public Guid FinanceAccountId { get; private set; } public string AccountCode { get; private set; } = null!; public string AccountName { get; private set; } = null!;
    public string AccountClass { get; private set; } = null!; public string SourceCurrency { get; private set; } = null!; public string DimensionKey { get; private set; } = null!;
    public string DimensionFactsJson { get; private set; } = null!; public decimal ClosingFunctionalBalance { get; private set; } public decimal ClosingDocumentBalance { get; private set; }
    public decimal OpeningFunctionalBalance { get; private set; } public decimal OpeningDocumentBalance { get; private set; } public decimal Difference { get; private set; }
    public string Status { get; private set; } = null!; public Guid? OpeningLedgerEntryId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public YearEndRun Run { get; private set; } = null!; public FinanceAccount FinanceAccount { get; private set; } = null!;
    public void MarkPosted(Guid ledgerEntryId) { OpeningLedgerEntryId = YearEndValue.Id(ledgerEntryId, nameof(ledgerEntryId)); Status = YearEndCandidateStatuses.Posted; }
    public void Reconcile(decimal actualFunctionalBalance)
    {
        OpeningFunctionalBalance = actualFunctionalBalance; Difference = actualFunctionalBalance - ClosingFunctionalBalance;
        Status = Difference == 0m ? YearEndCandidateStatuses.Matched : YearEndCandidateStatuses.Mismatch;
    }
}

public sealed class YearEndApprovalSignOff : ICompanyOwnedEntity
{
    private YearEndApprovalSignOff() { }
    public YearEndApprovalSignOff(Guid id, Guid companyId, Guid runId, string action, string decision,
        string evidenceHash, Guid actorUserId, string actorRole, string? reason, DateTime occurredUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        Action = YearEndValue.Text(action, nameof(action), 64).ToLowerInvariant(); Decision = YearEndValue.Text(decision, nameof(decision), 32).ToLowerInvariant();
        EvidenceHash = YearEndValue.Hash(evidenceHash, nameof(evidenceHash)); ActorUserId = YearEndValue.Id(actorUserId, nameof(actorUserId));
        ActorRole = YearEndValue.Text(actorRole, nameof(actorRole), 64).ToLowerInvariant(); Reason = YearEndValue.Optional(reason, 2000); OccurredUtc = YearEndValue.Utc(occurredUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public string Action { get; private set; } = null!; public string Decision { get; private set; } = null!; public string EvidenceHash { get; private set; } = null!;
    public Guid ActorUserId { get; private set; } public string ActorRole { get; private set; } = null!; public string? Reason { get; private set; } public DateTime OccurredUtc { get; private set; }
    public YearEndRun Run { get; private set; } = null!;
}

public sealed class YearEndSubsequentEvent : ICompanyOwnedEntity
{
    private YearEndSubsequentEvent() { }
    public YearEndSubsequentEvent(Guid id, Guid companyId, Guid runId, DateOnly eventDate, string title,
        string description, decimal? estimatedAmount, string currency, string decision, Guid ownerUserId,
        Guid? evidenceDocumentId, Guid recordedByUserId, DateTime recordedUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        EventDate = eventDate; Title = YearEndValue.Text(title, nameof(title), 240); Description = YearEndValue.Text(description, nameof(description), 4000);
        EstimatedAmount = estimatedAmount; Currency = YearEndValue.Text(currency, nameof(currency), 3).ToUpperInvariant(); Decision = SubsequentEventDecisions.Normalize(decision);
        OwnerUserId = YearEndValue.Id(ownerUserId, nameof(ownerUserId)); EvidenceDocumentId = evidenceDocumentId;
        RecordedByUserId = YearEndValue.Id(recordedByUserId, nameof(recordedByUserId)); RecordedUtc = UpdatedUtc = YearEndValue.Utc(recordedUtc);
        Status = SubsequentEventStatuses.Recorded; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public DateOnly EventDate { get; private set; } public string Title { get; private set; } = null!; public string Description { get; private set; } = null!;
    public decimal? EstimatedAmount { get; private set; } public string Currency { get; private set; } = null!; public string Decision { get; private set; } = null!;
    public Guid OwnerUserId { get; private set; } public Guid? EvidenceDocumentId { get; private set; } public Guid RecordedByUserId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; } public Guid? CorrectionLedgerEntryId { get; private set; } public Guid? ReopenRequestId { get; private set; }
    public string Status { get; private set; } = null!; public DateTime RecordedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? ResolvedUtc { get; private set; } public long Version { get; private set; }
    public YearEndRun Run { get; private set; } = null!;
    public void Submit(Guid actor, DateTime now) { if (Status != SubsequentEventStatuses.Recorded || actor != RecordedByUserId) throw new InvalidOperationException("Only the recorder can submit this event."); Status = SubsequentEventStatuses.UnderReview; Touch(now); }
    public void Review(Guid actor, bool approve, DateTime now) { if (Status != SubsequentEventStatuses.UnderReview) throw new InvalidOperationException("The event is not under review."); if (actor == RecordedByUserId) throw new InvalidOperationException("The recorder cannot review their own event."); ReviewedByUserId = actor; Status = approve ? SubsequentEventStatuses.Approved : SubsequentEventStatuses.Rejected; Touch(now); }
    public void LinkResolution(Guid? correctionLedgerEntryId, Guid? reopenRequestId, DateTime now)
    {
        if (Status != SubsequentEventStatuses.Approved) throw new InvalidOperationException("Only an approved event can be resolved.");
        if (Decision == SubsequentEventDecisions.PostForward && !correctionLedgerEntryId.HasValue) throw new InvalidOperationException("A forward-posted event requires a linked correction journal.");
        if (Decision == SubsequentEventDecisions.RequestReopen && !reopenRequestId.HasValue) throw new InvalidOperationException("A reopen event requires a linked approved reopen request.");
        CorrectionLedgerEntryId = correctionLedgerEntryId; ReopenRequestId = reopenRequestId; Status = SubsequentEventStatuses.Resolved; ResolvedUtc = YearEndValue.Utc(now); Touch(now);
    }
    private void Touch(DateTime now) { UpdatedUtc = YearEndValue.Utc(now); Version++; }
}

public sealed class YearEndHistory : ICompanyOwnedEntity
{
    private YearEndHistory() { }
    public YearEndHistory(Guid id, Guid companyId, Guid runId, string action, string fromStatus, string toStatus,
        Guid actorUserId, string evidenceHash, string summary, DateTime occurredUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        Action = YearEndValue.Text(action, nameof(action), 100).ToLowerInvariant(); FromStatus = YearEndValue.Text(fromStatus, nameof(fromStatus), 32).ToLowerInvariant();
        ToStatus = YearEndValue.Text(toStatus, nameof(toStatus), 32).ToLowerInvariant(); ActorUserId = YearEndValue.Id(actorUserId, nameof(actorUserId));
        EvidenceHash = YearEndValue.Hash(evidenceHash, nameof(evidenceHash)); Summary = YearEndValue.Text(summary, nameof(summary), 2000); OccurredUtc = YearEndValue.Utc(occurredUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public string Action { get; private set; } = null!; public string FromStatus { get; private set; } = null!; public string ToStatus { get; private set; } = null!;
    public Guid ActorUserId { get; private set; } public string EvidenceHash { get; private set; } = null!; public string Summary { get; private set; } = null!; public DateTime OccurredUtc { get; private set; }
    public YearEndRun Run { get; private set; } = null!;
}

public sealed class YearEndCorrectionRecord : ICompanyOwnedEntity
{
    private YearEndCorrectionRecord() { }
    public YearEndCorrectionRecord(Guid id, Guid companyId, Guid runId, Guid subsequentEventId,
        string correctionMode, Guid? ledgerEntryId, Guid? reopenRequestId, string reason,
        Guid recordedByUserId, DateTime recordedUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        SubsequentEventId = YearEndValue.Id(subsequentEventId, nameof(subsequentEventId)); CorrectionMode = SubsequentEventDecisions.Normalize(correctionMode);
        if (CorrectionMode == SubsequentEventDecisions.PostForward && !ledgerEntryId.HasValue) throw new ArgumentException("A post-forward correction requires a ledger entry.");
        if (CorrectionMode == SubsequentEventDecisions.RequestReopen && !reopenRequestId.HasValue) throw new ArgumentException("A reopen correction requires a reopen request.");
        LedgerEntryId = ledgerEntryId; ReopenRequestId = reopenRequestId; Reason = YearEndValue.Text(reason, nameof(reason), 2000);
        RecordedByUserId = YearEndValue.Id(recordedByUserId, nameof(recordedByUserId)); RecordedUtc = YearEndValue.Utc(recordedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; } public Guid SubsequentEventId { get; private set; }
    public string CorrectionMode { get; private set; } = null!; public Guid? LedgerEntryId { get; private set; } public Guid? ReopenRequestId { get; private set; }
    public string Reason { get; private set; } = null!; public Guid RecordedByUserId { get; private set; } public DateTime RecordedUtc { get; private set; }
    public YearEndRun Run { get; private set; } = null!; public YearEndSubsequentEvent SubsequentEvent { get; private set; } = null!;
}

public sealed class YearEndOperation : ICompanyOwnedEntity
{
    private YearEndOperation() { }
    public YearEndOperation(Guid id, Guid companyId, Guid runId, string operation, string idempotencyKey,
        string requestHash, long resultVersion, DateTime createdUtc)
    {
        Id = YearEndValue.Id(id, nameof(id)); CompanyId = YearEndValue.Id(companyId, nameof(companyId)); RunId = YearEndValue.Id(runId, nameof(runId));
        Operation = YearEndValue.Text(operation, nameof(operation), 64).ToLowerInvariant(); IdempotencyKey = YearEndValue.Text(idempotencyKey, nameof(idempotencyKey), 200);
        RequestHash = YearEndValue.Hash(requestHash, nameof(requestHash)); ResultVersion = resultVersion; CreatedUtc = YearEndValue.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public string Operation { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!; public string RequestHash { get; private set; } = null!;
    public long ResultVersion { get; private set; } public DateTime CreatedUtc { get; private set; } public YearEndRun Run { get; private set; } = null!;
}

internal static class YearEndValue
{
    public static Guid Id(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    public static string Text(string? value, string name, int max) { var text = value?.Trim(); return string.IsNullOrWhiteSpace(text) || text.Length > max ? throw new ArgumentException($"{name} is required and limited to {max} characters.", name) : text; }
    public static string? Optional(string? value, int max) { var text = value?.Trim(); return string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : throw new ArgumentOutOfRangeException(nameof(value)); }
    public static string Hash(string value, string name) { var hash = Text(value, name, 64).ToLowerInvariant(); return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash : throw new ArgumentException($"{name} must be SHA-256.", name); }
}
