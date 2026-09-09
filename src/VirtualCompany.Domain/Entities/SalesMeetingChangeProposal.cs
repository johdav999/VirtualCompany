using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingChangeProposal : ICompanyOwnedEntity
{
    private SalesMeetingChangeProposal() { }

    public SalesMeetingChangeProposal(Guid id, Guid companyId, Guid sessionId, Guid evidenceArtifactId,
        SalesMeetingChangeTargetType targetType, Guid targetId, SalesMeetingChangeAction action,
        SalesMeetingChangeField field, SalesMeetingChangeValueKind valueKind, string proposedValueJson,
        string beforeValueJson, string targetVersion, decimal confidence, string rationale,
        string sourceIdsJson, string evidenceVersionHash, SalesMeetingChangeRiskClass riskClass,
        bool requiresApproval, string policyVersion, Guid createdByUserId, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, evidenceArtifactId, targetId, createdByUserId);
        if (confidence is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(confidence));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SessionId = sessionId;
        EvidenceArtifactId = evidenceArtifactId; TargetType = targetType; TargetId = targetId; Action = action; Field = field;
        ValueKind = valueKind; ProposedValueJson = Required(proposedValueJson, nameof(proposedValueJson), 8000);
        BeforeValueJson = Required(beforeValueJson, nameof(beforeValueJson), 8000); TargetVersion = Required(targetVersion, nameof(targetVersion), 128);
        Confidence = confidence; Rationale = Required(rationale, nameof(rationale), 2000); SourceIdsJson = Required(sourceIdsJson, nameof(sourceIdsJson), 8000);
        EvidenceVersionHash = Required(evidenceVersionHash, nameof(evidenceVersionHash), 128); RiskClass = riskClass;
        RequiresApproval = requiresApproval; PolicyVersion = Required(policyVersion, nameof(policyVersion), 64);
        CreatedByUserId = createdByUserId; CreatedUtc = Utc(nowUtc); UpdatedUtc = CreatedUtc;
        Status = SalesMeetingChangeProposalStatus.Draft; ConcurrencyVersion = 1;
        var binding = $"{companyId:N}|{sessionId:N}|{targetType.ToStorageValue()}|{targetId:N}|{action.ToStorageValue()}|{field.ToStorageValue()}|{proposedValueJson}|{targetVersion}|{evidenceVersionHash}";
        IdempotencyKey = $"sales-meeting-proposal:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(binding))).ToLowerInvariant()}";
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid EvidenceArtifactId { get; private set; }
    public SalesMeetingChangeTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public SalesMeetingChangeAction Action { get; private set; }
    public SalesMeetingChangeField Field { get; private set; }
    public SalesMeetingChangeValueKind ValueKind { get; private set; }
    public string ProposedValueJson { get; private set; } = null!;
    public string BeforeValueJson { get; private set; } = null!;
    public string TargetVersion { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string Rationale { get; private set; } = null!;
    public string SourceIdsJson { get; private set; } = null!;
    public string EvidenceVersionHash { get; private set; } = null!;
    public SalesMeetingChangeRiskClass RiskClass { get; private set; }
    public SalesMeetingChangeProposalStatus Status { get; private set; }
    public bool RequiresApproval { get; private set; }
    public string PolicyVersion { get; private set; } = null!;
    public Guid? ApprovalRequestId { get; private set; }
    public string? ApprovalBindingHash { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public int ExecutionAttemptCount { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string? ExecutedBeforeValueJson { get; private set; }
    public string? ExecutedAfterValueJson { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesMeetingArtifact EvidenceArtifact { get; private set; } = null!;

    public void Edit(long expectedVersion, SalesMeetingChangeValueKind kind, string proposedJson, decimal confidence,
        string rationale, string sourceIdsJson, string evidenceHash, SalesMeetingChangeRiskClass risk, bool requiresApproval,
        string policyVersion, Guid reviewer, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion); if (Status is SalesMeetingChangeProposalStatus.Executing or SalesMeetingChangeProposalStatus.Executed) throw new InvalidOperationException("An executing or executed proposal cannot be edited.");
        ValueKind = kind; ProposedValueJson = Required(proposedJson, nameof(proposedJson), 8000); Confidence = confidence is >= 0m and <= 1m ? confidence : throw new ArgumentOutOfRangeException(nameof(confidence));
        Rationale = Required(rationale, nameof(rationale), 2000); SourceIdsJson = Required(sourceIdsJson, nameof(sourceIdsJson), 8000); EvidenceVersionHash = Required(evidenceHash, nameof(evidenceHash), 128);
        RiskClass = risk; RequiresApproval = requiresApproval; PolicyVersion = Required(policyVersion, nameof(policyVersion), 64);
        ApprovalRequestId = null; ApprovalBindingHash = null; ApprovedUtc = null; RejectedUtc = null; LastErrorCode = null; LastErrorSummary = null;
        Status = SalesMeetingChangeProposalStatus.Draft; ReviewedByUserId = reviewer; ReviewedUtc = Utc(nowUtc); Touch(nowUtc);
    }
    public void RequestApproval(long expectedVersion, Guid requestId, string bindingHash, Guid reviewer, DateTime nowUtc) { EnsureVersion(expectedVersion); SalesMeetingTranscriptSegment.EnsureIds(requestId, reviewer); ApprovalRequestId = requestId; ApprovalBindingHash = Required(bindingHash, nameof(bindingHash), 128); Status = SalesMeetingChangeProposalStatus.WaitingForApproval; ReviewedByUserId = reviewer; ReviewedUtc = Utc(nowUtc); Touch(nowUtc); }
    public void Approve(long expectedVersion, string bindingHash, Guid reviewer, DateTime nowUtc) { EnsureVersion(expectedVersion); SalesMeetingTranscriptSegment.EnsureIds(reviewer); ApprovalBindingHash = Required(bindingHash, nameof(bindingHash), 128); Status = SalesMeetingChangeProposalStatus.Approved; ReviewedByUserId = reviewer; ReviewedUtc = Utc(nowUtc); ApprovedUtc = UpdatedUtc = Utc(nowUtc); ConcurrencyVersion++; }
    public void Reject(long expectedVersion, Guid reviewer, string? reason, DateTime nowUtc) { EnsureVersion(expectedVersion); SalesMeetingTranscriptSegment.EnsureIds(reviewer); Status = SalesMeetingChangeProposalStatus.Rejected; ReviewedByUserId = reviewer; ReviewedUtc = RejectedUtc = Utc(nowUtc); LastErrorSummary = Optional(reason, 1000); Touch(nowUtc); }
    public void BeginExecution() { if (Status != SalesMeetingChangeProposalStatus.Approved) throw new InvalidOperationException("Only an approved proposal can execute."); Status = SalesMeetingChangeProposalStatus.Executing; ExecutionAttemptCount++; Touch(DateTime.UtcNow); }
    public void MarkQueued(string beforeJson, string afterJson, DateTime nowUtc) { ExecutedBeforeValueJson = Required(beforeJson, nameof(beforeJson), 8000); ExecutedAfterValueJson = Required(afterJson, nameof(afterJson), 8000); Status = SalesMeetingChangeProposalStatus.Queued; Touch(nowUtc); }
    public void BindProviderReference(string providerReference, DateTime nowUtc) { ProviderReference = Required(providerReference, nameof(providerReference), 1000); Touch(nowUtc); }
    public void MarkExecuted(string beforeJson, string afterJson, string? providerReference, DateTime nowUtc) { ExecutedBeforeValueJson = Required(beforeJson, nameof(beforeJson), 8000); ExecutedAfterValueJson = Required(afterJson, nameof(afterJson), 8000); ProviderReference = Optional(providerReference, 1000); Status = SalesMeetingChangeProposalStatus.Executed; ExecutedUtc = Utc(nowUtc); LastErrorCode = LastErrorSummary = null; Touch(nowUtc); }
    public void MarkConflict(string code, string summary) => MarkProblem(SalesMeetingChangeProposalStatus.Conflict, code, summary);
    public void MarkFailed(string code, string summary) => MarkProblem(SalesMeetingChangeProposalStatus.Failed, code, summary);
    public void MarkReconciliationRequired(string code, string summary) => MarkProblem(SalesMeetingChangeProposalStatus.ReconciliationRequired, code, summary);
    private void MarkProblem(SalesMeetingChangeProposalStatus status, string code, string summary) { Status = status; LastErrorCode = Required(code, nameof(code), 120); LastErrorSummary = Required(summary, nameof(summary), 1000); Touch(DateTime.UtcNow); }
    private void EnsureVersion(long expected) { if (expected != ConcurrencyVersion) throw new InvalidOperationException("The proposal changed after it was opened."); }
    private void Touch(DateTime utc) { UpdatedUtc = Utc(utc); ConcurrencyVersion++; }
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
