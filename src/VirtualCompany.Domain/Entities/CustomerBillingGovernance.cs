namespace VirtualCompany.Domain.Entities;

public static class CustomerDuplicateDecisionStatuses
{
    public const string Pending = "pending";
    public const string Merged = "merged";
    public const string KeptSeparate = "kept_separate";
}

public sealed class CustomerBillingProfileVersion : ICompanyOwnedEntity
{
    private CustomerBillingProfileVersion() { }
    public CustomerBillingProfileVersion(Guid id, Guid companyId, Guid profileId, Guid counterpartyId, long profileVersion,
        string sourceKind, string? sourceReference, string changedFields, string snapshotJson, string snapshotHash,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ProfileId = profileId;
        CounterpartyId = counterpartyId; ProfileVersion = profileVersion;
        SourceKind = CustomerBillingNormalization.Choice(sourceKind, nameof(sourceKind), CustomerBillingSourceKinds.All);
        SourceReference = CustomerBillingNormalization.Optional(sourceReference, nameof(sourceReference), 200);
        ChangedFields = CustomerBillingNormalization.Required(changedFields, nameof(changedFields), 2000);
        SnapshotJson = CustomerBillingNormalization.Required(snapshotJson, nameof(snapshotJson), 16000);
        SnapshotHash = CustomerBillingNormalization.Required(snapshotHash, nameof(snapshotHash), 64);
        ActorUserId = actorUserId; CreatedUtc = CustomerBillingNormalization.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProfileId { get; private set; }
    public Guid CounterpartyId { get; private set; } public long ProfileVersion { get; private set; }
    public string SourceKind { get; private set; } = null!; public string? SourceReference { get; private set; }
    public string ChangedFields { get; private set; } = null!; public string SnapshotJson { get; private set; } = null!;
    public string SnapshotHash { get; private set; } = null!; public Guid ActorUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class CustomerBillingSourceConflict : ICompanyOwnedEntity
{
    private CustomerBillingSourceConflict() { }
    public CustomerBillingSourceConflict(Guid id, Guid companyId, Guid profileId, Guid counterpartyId,
        long baseVersion, string existingSourceKind, string incomingSourceKind, string? incomingSourceReference,
        string changedFields, string incomingSnapshotJson, Guid detectedByUserId, DateTime detectedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ProfileId = profileId;
        CounterpartyId = counterpartyId; BaseVersion = baseVersion; ExistingSourceKind = existingSourceKind;
        IncomingSourceKind = incomingSourceKind; IncomingSourceReference = incomingSourceReference;
        ChangedFields = changedFields; IncomingSnapshotJson = incomingSnapshotJson; Status = "pending";
        DetectedByUserId = detectedByUserId; DetectedUtc = detectedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProfileId { get; private set; }
    public Guid CounterpartyId { get; private set; } public long BaseVersion { get; private set; }
    public string ExistingSourceKind { get; private set; } = null!; public string IncomingSourceKind { get; private set; } = null!;
    public string? IncomingSourceReference { get; private set; } public string ChangedFields { get; private set; } = null!;
    public string IncomingSnapshotJson { get; private set; } = null!; public string Status { get; private set; } = null!;
    public bool? UsedIncomingValues { get; private set; } public string? DecisionReason { get; private set; }
    public Guid DetectedByUserId { get; private set; } public DateTime DetectedUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; } public DateTime? DecidedUtc { get; private set; }
    public long Version { get; private set; }
    public void Resolve(bool useIncoming, string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != "pending") throw new InvalidOperationException("This source conflict is already resolved.");
        UsedIncomingValues = useIncoming; DecisionReason = CustomerBillingNormalization.Required(reason, nameof(reason), 500);
        DecidedByUserId = actorUserId; DecidedUtc = nowUtc; Status = "resolved"; Version++;
    }
}

public sealed class CustomerDuplicateCandidate : ICompanyOwnedEntity
{
    private CustomerDuplicateCandidate() { }
    public CustomerDuplicateCandidate(Guid id, Guid companyId, Guid firstCounterpartyId, Guid secondCounterpartyId,
        int score, string evidenceJson, DateTime detectedUtc)
    {
        if (firstCounterpartyId.CompareTo(secondCounterpartyId) >= 0) throw new ArgumentException("Duplicate pair must be stored in canonical order.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; FirstCounterpartyId = firstCounterpartyId;
        SecondCounterpartyId = secondCounterpartyId; Score = score; EvidenceJson = evidenceJson;
        Status = CustomerDuplicateDecisionStatuses.Pending; DetectedUtc = detectedUtc; UpdatedUtc = detectedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid FirstCounterpartyId { get; private set; } public Guid SecondCounterpartyId { get; private set; }
    public int Score { get; private set; } public string EvidenceJson { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid? MergeSourceCounterpartyId { get; private set; }
    public Guid? MergeTargetCounterpartyId { get; private set; } public string? DecisionReason { get; private set; }
    public Guid? DecidedByUserId { get; private set; } public DateTime? DecidedUtc { get; private set; }
    public DateTime DetectedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public long Version { get; private set; }
    public void Refresh(int score, string evidenceJson, DateTime nowUtc)
    {
        if (Status != CustomerDuplicateDecisionStatuses.Pending) return;
        Score = score; EvidenceJson = evidenceJson; UpdatedUtc = nowUtc; Version++;
    }
    public void KeepSeparate(string reason, Guid actorUserId, DateTime nowUtc)
    {
        Decide(CustomerDuplicateDecisionStatuses.KeptSeparate, null, null, reason, actorUserId, nowUtc);
    }
    public void MarkMerged(Guid sourceId, Guid targetId, string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!new[] { FirstCounterpartyId, SecondCounterpartyId }.Contains(sourceId) || !new[] { FirstCounterpartyId, SecondCounterpartyId }.Contains(targetId) || sourceId == targetId)
            throw new ArgumentException("Merge source and target must be the candidate pair.");
        Decide(CustomerDuplicateDecisionStatuses.Merged, sourceId, targetId, reason, actorUserId, nowUtc);
    }
    private void Decide(string status, Guid? sourceId, Guid? targetId, string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != CustomerDuplicateDecisionStatuses.Pending) throw new InvalidOperationException("This duplicate candidate already has a decision.");
        Status = status; MergeSourceCounterpartyId = sourceId; MergeTargetCounterpartyId = targetId;
        DecisionReason = CustomerBillingNormalization.Required(reason, nameof(reason), 500); DecidedByUserId = actorUserId;
        DecidedUtc = nowUtc; UpdatedUtc = nowUtc; Version++;
    }
}

public sealed class CustomerCounterpartyRedirect : ICompanyOwnedEntity
{
    private CustomerCounterpartyRedirect() { }
    public CustomerCounterpartyRedirect(Guid id, Guid companyId, Guid sourceCounterpartyId, Guid targetCounterpartyId,
        Guid duplicateCandidateId, Guid actorUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SourceCounterpartyId = sourceCounterpartyId;
        TargetCounterpartyId = targetCounterpartyId; DuplicateCandidateId = duplicateCandidateId;
        ActorUserId = actorUserId; CreatedUtc = createdUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid SourceCounterpartyId { get; private set; } public Guid TargetCounterpartyId { get; private set; }
    public Guid DuplicateCandidateId { get; private set; } public Guid ActorUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class CustomerInvoiceCustomerSnapshot : ICompanyOwnedEntity
{
    private CustomerInvoiceCustomerSnapshot() { }
    public CustomerInvoiceCustomerSnapshot(Guid id, Guid companyId, Guid invoiceId, Guid counterpartyId,
        long? billingProfileVersion, string sourceKind, string snapshotJson, string snapshotHash, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; InvoiceId = invoiceId;
        CounterpartyId = counterpartyId; BillingProfileVersion = billingProfileVersion; SourceKind = sourceKind;
        SnapshotJson = snapshotJson; SnapshotHash = snapshotHash; CreatedUtc = createdUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InvoiceId { get; private set; }
    public Guid CounterpartyId { get; private set; } public long? BillingProfileVersion { get; private set; }
    public string SourceKind { get; private set; } = null!; public string SnapshotJson { get; private set; } = null!;
    public string SnapshotHash { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}
