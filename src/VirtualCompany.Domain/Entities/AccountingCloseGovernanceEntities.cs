namespace VirtualCompany.Domain.Entities;

public static class AccountingCloseReadinessStatuses
{
    public const string Prepared = "prepared";
    public const string InReview = "in_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
    public const string Locked = "locked";
    public const string Failed = "failed";
}

public static class AccountingCloseWaiverStatuses
{
    public const string Proposed = "proposed";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}

public static class AccountingCloseReopenStatuses
{
    public const string PendingReview = "pending_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Executed = "executed";
    public const string Cancelled = "cancelled";
}

public sealed class CompanyAccountingClosePolicy : ICompanyOwnedEntity
{
    private CompanyAccountingClosePolicy() { }

    public CompanyAccountingClosePolicy(Guid id, Guid companyId, decimal materialityThreshold,
        string currency, int waiverValidityHours, Guid actorUserId, DateTime now)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        Apply(materialityThreshold, currency, waiverValidityHours, actorUserId, now);
        CreatedUtc = UpdatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public decimal MaterialityThreshold { get; private set; }
    public string Currency { get; private set; } = null!;
    public int WaiverValidityHours { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public void Update(decimal materialityThreshold, string currency, int waiverValidityHours,
        Guid actorUserId, DateTime now)
    {
        Apply(materialityThreshold, currency, waiverValidityHours, actorUserId, now);
        Version++;
    }

    private void Apply(decimal materialityThreshold, string currency, int waiverValidityHours,
        Guid actorUserId, DateTime now)
    {
        if (materialityThreshold < 0m) throw new ArgumentOutOfRangeException(nameof(materialityThreshold));
        if (waiverValidityHours is < 1 or > 2160) throw new ArgumentOutOfRangeException(nameof(waiverValidityHours));
        MaterialityThreshold = materialityThreshold;
        Currency = CloseGovernanceValue.Required(currency, nameof(currency), 3).ToUpperInvariant();
        WaiverValidityHours = waiverValidityHours;
        UpdatedByUserId = CloseGovernanceValue.RequiredId(actorUserId, nameof(actorUserId));
        UpdatedUtc = CloseGovernanceValue.Utc(now);
    }
}

public sealed class AccountingCloseReadinessSnapshot : ICompanyOwnedEntity
{
    private AccountingCloseReadinessSnapshot() { }

    public AccountingCloseReadinessSnapshot(Guid id, Guid companyId, Guid closeInstanceId,
        Guid fiscalPeriodId, int snapshotNumber, string evidenceHash, string trialBalanceChecksum,
        bool isReady, Guid preparedByUserId, DateTime preparedUtc)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseGovernanceValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        FiscalPeriodId = CloseGovernanceValue.RequiredId(fiscalPeriodId, nameof(fiscalPeriodId));
        if (snapshotNumber < 1) throw new ArgumentOutOfRangeException(nameof(snapshotNumber));
        SnapshotNumber = snapshotNumber;
        EvidenceHash = CloseGovernanceValue.RequiredHash(evidenceHash, nameof(evidenceHash));
        TrialBalanceChecksum = CloseGovernanceValue.Required(trialBalanceChecksum, nameof(trialBalanceChecksum), 128);
        IsReady = isReady;
        PreparedByUserId = CloseGovernanceValue.RequiredId(preparedByUserId, nameof(preparedByUserId));
        PreparedUtc = UpdatedUtc = CloseGovernanceValue.Utc(preparedUtc);
        Status = AccountingCloseReadinessStatuses.Prepared;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public int SnapshotNumber { get; private set; }
    public string Status { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public string TrialBalanceChecksum { get; private set; } = null!;
    public bool IsReady { get; private set; }
    public Guid PreparedByUserId { get; private set; }
    public DateTime PreparedUtc { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public string? ReviewReason { get; private set; }
    public Guid? LockedByUserId { get; private set; }
    public DateTime? LockedUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public AccountingCloseInstance CloseInstance { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ICollection<AccountingCloseReadinessCheck> Checks { get; } = new List<AccountingCloseReadinessCheck>();

    public void Submit(Guid actorUserId, DateTime now)
    {
        RequireStatus(AccountingCloseReadinessStatuses.Prepared);
        if (!IsReady) throw new InvalidOperationException("A blocked readiness snapshot cannot be submitted.");
        SubmittedByUserId = CloseGovernanceValue.RequiredId(actorUserId, nameof(actorUserId));
        SubmittedUtc = CloseGovernanceValue.Utc(now);
        Status = AccountingCloseReadinessStatuses.InReview;
        Touch(now);
    }

    public void Approve(Guid reviewerUserId, DateTime now)
    {
        RequireStatus(AccountingCloseReadinessStatuses.InReview);
        reviewerUserId = CloseGovernanceValue.RequiredId(reviewerUserId, nameof(reviewerUserId));
        if (reviewerUserId == PreparedByUserId || reviewerUserId == SubmittedByUserId)
            throw new InvalidOperationException("The preparer or submitter cannot approve the same close readiness snapshot.");
        ReviewedByUserId = reviewerUserId;
        ReviewedUtc = CloseGovernanceValue.Utc(now);
        ReviewReason = null;
        Status = AccountingCloseReadinessStatuses.Approved;
        Touch(now);
    }

    public void Reject(Guid reviewerUserId, string reason, DateTime now)
    {
        RequireStatus(AccountingCloseReadinessStatuses.InReview);
        reviewerUserId = CloseGovernanceValue.RequiredId(reviewerUserId, nameof(reviewerUserId));
        if (reviewerUserId == PreparedByUserId || reviewerUserId == SubmittedByUserId)
            throw new InvalidOperationException("The preparer or submitter cannot review the same close readiness snapshot.");
        ReviewedByUserId = reviewerUserId;
        ReviewedUtc = CloseGovernanceValue.Utc(now);
        ReviewReason = CloseGovernanceValue.Required(reason, nameof(reason), 1000);
        Status = AccountingCloseReadinessStatuses.Rejected;
        Touch(now);
    }

    public void Cancel(string reason, DateTime now)
    {
        if (Status is AccountingCloseReadinessStatuses.Locked or AccountingCloseReadinessStatuses.Cancelled)
            throw new InvalidOperationException("A locked or already cancelled readiness snapshot cannot be cancelled.");
        ReviewReason = CloseGovernanceValue.Required(reason, nameof(reason), 1000);
        Status = AccountingCloseReadinessStatuses.Cancelled;
        Touch(now);
    }

    public void MarkStale(string reason, DateTime now)
    {
        if (Status is AccountingCloseReadinessStatuses.Locked or AccountingCloseReadinessStatuses.Cancelled)
            throw new InvalidOperationException("A locked or cancelled snapshot is immutable.");
        Status = AccountingCloseReadinessStatuses.Stale;
        FailureCode = "readiness_evidence_changed";
        FailureSummary = CloseGovernanceValue.Required(reason, nameof(reason), 2000);
        Touch(now);
    }

    public void MarkLocked(Guid actorUserId, string expectedHash, DateTime now)
    {
        RequireStatus(AccountingCloseReadinessStatuses.Approved);
        if (!string.Equals(EvidenceHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The approved readiness evidence hash changed.");
        LockedByUserId = CloseGovernanceValue.RequiredId(actorUserId, nameof(actorUserId));
        LockedUtc = CloseGovernanceValue.Utc(now);
        Status = AccountingCloseReadinessStatuses.Locked;
        Touch(now);
    }

    public void MarkFailed(string code, string safeSummary, DateTime now)
    {
        if (Status is AccountingCloseReadinessStatuses.Locked or AccountingCloseReadinessStatuses.Cancelled)
            throw new InvalidOperationException("A locked or cancelled snapshot is immutable.");
        Status = AccountingCloseReadinessStatuses.Failed;
        FailureCode = CloseGovernanceValue.Required(code, nameof(code), 100);
        FailureSummary = CloseGovernanceValue.Required(safeSummary, nameof(safeSummary), 2000);
        Touch(now);
    }

    private void RequireStatus(string status)
    {
        if (Status != status) throw new InvalidOperationException($"Readiness snapshot must be '{status}'.");
    }

    private void Touch(DateTime now) { UpdatedUtc = CloseGovernanceValue.Utc(now); Version++; }
}

public sealed class AccountingCloseReadinessCheck : ICompanyOwnedEntity
{
    private AccountingCloseReadinessCheck() { }

    public AccountingCloseReadinessCheck(Guid id, Guid companyId, Guid snapshotId, string category,
        string code, string message, bool isBlocking, bool isWaivable, decimal? amount, string? currency,
        int itemCount, string evidenceJson, string evidenceHash, DateTime observedUtc)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        SnapshotId = CloseGovernanceValue.RequiredId(snapshotId, nameof(snapshotId));
        Category = CloseGovernanceValue.Required(category, nameof(category), 64).ToLowerInvariant();
        Code = CloseGovernanceValue.Required(code, nameof(code), 100).ToLowerInvariant();
        Message = CloseGovernanceValue.Required(message, nameof(message), 2000);
        IsBlocking = isBlocking;
        IsWaivable = isWaivable;
        if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        Amount = amount;
        Currency = CloseGovernanceValue.Optional(currency, nameof(currency), 3)?.ToUpperInvariant();
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        ItemCount = itemCount;
        EvidenceJson = CloseGovernanceValue.Required(evidenceJson, nameof(evidenceJson), 16000);
        EvidenceHash = CloseGovernanceValue.RequiredHash(evidenceHash, nameof(evidenceHash));
        ObservedUtc = CloseGovernanceValue.Utc(observedUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public bool IsBlocking { get; private set; }
    public bool IsWaivable { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public int ItemCount { get; private set; }
    public string EvidenceJson { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public DateTime ObservedUtc { get; private set; }
    public AccountingCloseReadinessSnapshot Snapshot { get; private set; } = null!;
}

public sealed class AccountingCloseWaiver : ICompanyOwnedEntity
{
    private AccountingCloseWaiver() { }

    public AccountingCloseWaiver(Guid id, Guid companyId, Guid closeInstanceId, Guid snapshotId,
        string checkCode, string checkEvidenceHash, string reason, decimal? amount, Guid evidenceDocumentId,
        string evidenceDocumentHash, Guid approvalRequestId, Guid proposedByUserId, DateTime expiresUtc, DateTime now)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseGovernanceValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        SnapshotId = CloseGovernanceValue.RequiredId(snapshotId, nameof(snapshotId));
        CheckCode = CloseGovernanceValue.Required(checkCode, nameof(checkCode), 100).ToLowerInvariant();
        CheckEvidenceHash = CloseGovernanceValue.RequiredHash(checkEvidenceHash, nameof(checkEvidenceHash));
        Reason = CloseGovernanceValue.Required(reason, nameof(reason), 2000);
        if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        Amount = amount;
        EvidenceDocumentId = CloseGovernanceValue.RequiredId(evidenceDocumentId, nameof(evidenceDocumentId));
        EvidenceDocumentHash = CloseGovernanceValue.RequiredHash(evidenceDocumentHash, nameof(evidenceDocumentHash));
        ApprovalRequestId = CloseGovernanceValue.RequiredId(approvalRequestId, nameof(approvalRequestId));
        ProposedByUserId = CloseGovernanceValue.RequiredId(proposedByUserId, nameof(proposedByUserId));
        CreatedUtc = CloseGovernanceValue.Utc(now);
        ExpiresUtc = CloseGovernanceValue.Utc(expiresUtc);
        if (ExpiresUtc <= CreatedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
        Status = AccountingCloseWaiverStatuses.Proposed;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public string CheckCode { get; private set; } = null!;
    public string CheckEvidenceHash { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public decimal? Amount { get; private set; }
    public Guid EvidenceDocumentId { get; private set; }
    public string EvidenceDocumentHash { get; private set; } = null!;
    public Guid ApprovalRequestId { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid ProposedByUserId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public AccountingCloseReadinessSnapshot Snapshot { get; private set; } = null!;
    public CompanyKnowledgeDocument EvidenceDocument { get; private set; } = null!;
    public ApprovalRequest ApprovalRequest { get; private set; } = null!;

    public bool AppliesTo(string checkCode, string evidenceHash, DateTime now) =>
        Status == AccountingCloseWaiverStatuses.Approved && ExpiresUtc > CloseGovernanceValue.Utc(now) &&
        string.Equals(CheckCode, checkCode, StringComparison.Ordinal) &&
        string.Equals(CheckEvidenceHash, evidenceHash, StringComparison.Ordinal);

    public void Approve(Guid reviewerUserId, DateTime now)
    {
        reviewerUserId = CloseGovernanceValue.RequiredId(reviewerUserId, nameof(reviewerUserId));
        if (reviewerUserId == ProposedByUserId) throw new InvalidOperationException("A waiver proposer cannot approve their own waiver.");
        if (ExpiresUtc <= CloseGovernanceValue.Utc(now)) { Status = AccountingCloseWaiverStatuses.Expired; return; }
        Status = AccountingCloseWaiverStatuses.Approved; ReviewedByUserId = reviewerUserId; ReviewedUtc = CloseGovernanceValue.Utc(now);
    }

    public void Reject(Guid reviewerUserId, DateTime now)
    {
        reviewerUserId = CloseGovernanceValue.RequiredId(reviewerUserId, nameof(reviewerUserId));
        if (reviewerUserId == ProposedByUserId) throw new InvalidOperationException("A waiver proposer cannot review their own waiver.");
        Status = AccountingCloseWaiverStatuses.Rejected; ReviewedByUserId = reviewerUserId; ReviewedUtc = CloseGovernanceValue.Utc(now);
    }
}

public sealed class AccountingCloseReopenRequest : ICompanyOwnedEntity
{
    private AccountingCloseReopenRequest() { }

    public AccountingCloseReopenRequest(Guid id, Guid companyId, Guid closeInstanceId, Guid priorSnapshotId,
        string priorSnapshotHash, string reason, string scope, string correctionPath, Guid requestedByUserId,
        DateTime expiresUtc, DateTime now)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseGovernanceValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        PriorSnapshotId = CloseGovernanceValue.RequiredId(priorSnapshotId, nameof(priorSnapshotId));
        PriorSnapshotHash = CloseGovernanceValue.RequiredHash(priorSnapshotHash, nameof(priorSnapshotHash));
        Reason = CloseGovernanceValue.Required(reason, nameof(reason), 2000);
        Scope = CloseGovernanceValue.Required(scope, nameof(scope), 1000);
        CorrectionPath = CloseGovernanceValue.Required(correctionPath, nameof(correctionPath), 1000);
        RequestedByUserId = CloseGovernanceValue.RequiredId(requestedByUserId, nameof(requestedByUserId));
        RequestedUtc = CloseGovernanceValue.Utc(now);
        ExpiresUtc = CloseGovernanceValue.Utc(expiresUtc);
        if (ExpiresUtc <= RequestedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
        Status = AccountingCloseReopenStatuses.PendingReview;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid PriorSnapshotId { get; private set; }
    public string PriorSnapshotHash { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public string Scope { get; private set; } = null!;
    public string CorrectionPath { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public Guid? ExecutedByUserId { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public long Version { get; private set; }
    public AccountingCloseReadinessSnapshot PriorSnapshot { get; private set; } = null!;

    public void Review(Guid reviewerUserId, bool approve, DateTime now)
    {
        if (Status != AccountingCloseReopenStatuses.PendingReview) throw new InvalidOperationException("The reopen request is not awaiting review.");
        reviewerUserId = CloseGovernanceValue.RequiredId(reviewerUserId, nameof(reviewerUserId));
        if (reviewerUserId == RequestedByUserId) throw new InvalidOperationException("The reopen requester cannot review their own request.");
        if (ExpiresUtc <= CloseGovernanceValue.Utc(now)) throw new InvalidOperationException("The reopen request has expired.");
        ReviewedByUserId = reviewerUserId; ReviewedUtc = CloseGovernanceValue.Utc(now);
        Status = approve ? AccountingCloseReopenStatuses.Approved : AccountingCloseReopenStatuses.Rejected;
        Version++;
    }

    public void MarkExecuted(Guid actorUserId, DateTime now)
    {
        if (Status != AccountingCloseReopenStatuses.Approved) throw new InvalidOperationException("The reopen request is not approved.");
        if (ExpiresUtc <= CloseGovernanceValue.Utc(now)) throw new InvalidOperationException("The reopen approval has expired.");
        ExecutedByUserId = CloseGovernanceValue.RequiredId(actorUserId, nameof(actorUserId));
        ExecutedUtc = CloseGovernanceValue.Utc(now); Status = AccountingCloseReopenStatuses.Executed; Version++;
    }
}

public sealed class AccountingCloseSignOff : ICompanyOwnedEntity
{
    private AccountingCloseSignOff() { }
    public AccountingCloseSignOff(Guid id, Guid companyId, Guid closeInstanceId, Guid? snapshotId,
        Guid? reopenRequestId, string action, string evidenceHash, Guid actorUserId, string actorRole,
        string? reason, DateTime occurredUtc)
    {
        Id = CloseGovernanceValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseGovernanceValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseGovernanceValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        SnapshotId = snapshotId; ReopenRequestId = reopenRequestId;
        Action = CloseGovernanceValue.Required(action, nameof(action), 32).ToLowerInvariant();
        EvidenceHash = CloseGovernanceValue.RequiredHash(evidenceHash, nameof(evidenceHash));
        ActorUserId = CloseGovernanceValue.RequiredId(actorUserId, nameof(actorUserId));
        ActorRole = CloseGovernanceValue.Required(actorRole, nameof(actorRole), 64).ToLowerInvariant();
        Reason = CloseGovernanceValue.Optional(reason, nameof(reason), 2000);
        OccurredUtc = CloseGovernanceValue.Utc(occurredUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public Guid? ReopenRequestId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public string ActorRole { get; private set; } = null!;
    public string? Reason { get; private set; }
    public DateTime OccurredUtc { get; private set; }
}

internal static class CloseGovernanceValue
{
    public static Guid RequiredId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max) { var normalized = value?.Trim(); if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{name} is required.", name); if (normalized.Length > max) throw new ArgumentOutOfRangeException(name); return normalized; }
    public static string RequiredHash(string? value, string name) { var normalized = Required(value, name, 128).ToUpperInvariant(); if (normalized.Length != 64 || normalized.Any(x => !Uri.IsHexDigit(x))) throw new ArgumentException($"{name} must be a SHA-256 hash.", name); return normalized; }
    public static string? Optional(string? value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var normalized = value.Trim(); if (normalized.Length > max) throw new ArgumentOutOfRangeException(name); return normalized; }
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
