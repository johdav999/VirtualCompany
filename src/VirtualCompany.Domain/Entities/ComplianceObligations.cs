namespace VirtualCompany.Domain.Entities;

public static class ComplianceObligationStatuses
{
    public const string Generated = "generated";
    public const string Prepared = "prepared";
    public const string UnderReview = "under_review";
    public const string Approved = "approved";
    public const string Exported = "exported";
    public const string ManualSubmissionRecorded = "manual_submission_recorded";
    public const string AuthorityReceived = "authority_received";
    public const string AuthorityApproved = "authority_approved";
    public const string Rejected = "rejected";
    public const string Corrected = "corrected";
}

public static class ComplianceSubmissionModes
{
    public const string ExportAndManualEvidence = "export_and_manual_evidence";
}

public sealed class ComplianceObligationDefinition : ICompanyOwnedEntity
{
    private ComplianceObligationDefinition() { }
    public ComplianceObligationDefinition(Guid id,Guid companyId,string key,string title,string jurisdiction,string policyPackKey,string policyPackVersion,string policyPackDefinitionHash,string dueDateRule,string requiredReport,string requiredEvidence,string submissionMode,DateTime createdUtc)
    { Id=id;CompanyId=companyId;Key=key;Title=title;Jurisdiction=jurisdiction;PolicyPackKey=policyPackKey;PolicyPackVersion=policyPackVersion;PolicyPackDefinitionHash=policyPackDefinitionHash;DueDateRule=dueDateRule;RequiredReport=requiredReport;RequiredEvidence=requiredEvidence;SubmissionMode=submissionMode;RequiresApproval=true;CreatedUtc=createdUtc; }
    public Guid Id{get;private set;} public Guid CompanyId{get;private set;} public string Key{get;private set;}=null!; public string Title{get;private set;}=null!; public string Jurisdiction{get;private set;}=null!; public string PolicyPackKey{get;private set;}=null!; public string PolicyPackVersion{get;private set;}=null!; public string PolicyPackDefinitionHash{get;private set;}=null!; public string DueDateRule{get;private set;}=null!; public string RequiredReport{get;private set;}=null!; public string RequiredEvidence{get;private set;}=null!; public bool RequiresApproval{get;private set;} public string SubmissionMode{get;private set;}=null!; public DateTime CreatedUtc{get;private set;}
}

public sealed class ComplianceObligationInstance : ICompanyOwnedEntity
{
    private ComplianceObligationInstance() { }

    public ComplianceObligationInstance(Guid id, Guid companyId, string definitionKey, string title,
        string jurisdiction, string policyPackKey, string policyPackVersion, string policyPackDefinitionHash,
        string dueDateRule, DateOnly dueDate, Guid ownerUserId, Guid vatFilingPeriodId, Guid? vatReturnId,
        Guid? accountingCloseTaskId, string sourceHash, Guid generatedByUserId, DateTime now)
    {
        Id = Required(id, nameof(id)); CompanyId = Required(companyId, nameof(companyId));
        DefinitionKey = Text(definitionKey, nameof(definitionKey), 100).ToLowerInvariant();
        Title = Text(title, nameof(title), 240); Jurisdiction = Text(jurisdiction, nameof(jurisdiction), 64);
        PolicyPackKey = Text(policyPackKey, nameof(policyPackKey), 100);
        PolicyPackVersion = Text(policyPackVersion, nameof(policyPackVersion), 64);
        PolicyPackDefinitionHash = Text(policyPackDefinitionHash, nameof(policyPackDefinitionHash), 128);
        DueDateRule = Text(dueDateRule, nameof(dueDateRule), 160); DueDate = dueDate;
        OwnerUserId = Required(ownerUserId, nameof(ownerUserId)); VatFilingPeriodId = Required(vatFilingPeriodId, nameof(vatFilingPeriodId));
        VatReturnId = vatReturnId; AccountingCloseTaskId = accountingCloseTaskId;
        SourceHash = Text(sourceHash, nameof(sourceHash), 128); GeneratedByUserId = Required(generatedByUserId, nameof(generatedByUserId));
        Status = ComplianceObligationStatuses.Generated; SubmissionMode = ComplianceSubmissionModes.ExportAndManualEvidence;
        CreatedUtc = UpdatedUtc = Utc(now); Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string DefinitionKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Jurisdiction { get; private set; } = null!;
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string PolicyPackDefinitionHash { get; private set; } = null!;
    public string DueDateRule { get; private set; } = null!;
    public DateOnly DueDate { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Status { get; private set; } = null!;
    public string SubmissionMode { get; private set; } = null!;
    public Guid VatFilingPeriodId { get; private set; }
    public Guid? VatReturnId { get; private set; }
    public Guid? AccountingCloseTaskId { get; private set; }
    public Guid? CorrectionOfInstanceId { get; private set; }
    public Guid? CorrectedByInstanceId { get; private set; }
    public string SourceHash { get; private set; } = null!;
    public string? ExportReference { get; private set; }
    public string? ExportChecksum { get; private set; }
    public Guid GeneratedByUserId { get; private set; }
    public Guid? PreparedByUserId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public ICollection<ComplianceObligationHistory> History { get; } = new List<ComplianceObligationHistory>();
    public ICollection<ComplianceSubmissionEvidence> SubmissionEvidence { get; } = new List<ComplianceSubmissionEvidence>();
    public ICollection<ComplianceAuthorityAcknowledgement> Acknowledgements { get; } = new List<ComplianceAuthorityAcknowledgement>();
    public ICollection<ComplianceReminder> Reminders { get; } = new List<ComplianceReminder>();

    public void RefreshSource(Guid? vatReturnId, Guid? closeTaskId, DateOnly dueDate, string sourceHash, DateTime now)
    { if (Status != ComplianceObligationStatuses.Generated) return; VatReturnId = vatReturnId; AccountingCloseTaskId = closeTaskId; DueDate=dueDate; SourceHash = Text(sourceHash, nameof(sourceHash), 128); Touch(now); }
    public void Prepare(Guid actor, DateTime now) { RequireStatus(ComplianceObligationStatuses.Generated); PreparedByUserId = Required(actor, nameof(actor)); SetStatus(ComplianceObligationStatuses.Prepared, now); }
    public void SubmitForReview(Guid actor, DateTime now) { RequireStatus(ComplianceObligationStatuses.Prepared); if (actor != PreparedByUserId) throw new InvalidOperationException("The preparer must submit the obligation for review."); ReviewedByUserId = actor; SetStatus(ComplianceObligationStatuses.UnderReview, now); }
    public void Decide(Guid actor, bool approved, DateTime now) { RequireStatus(ComplianceObligationStatuses.UnderReview); if (actor == PreparedByUserId) throw new InvalidOperationException("The preparer cannot approve their own obligation."); ApprovedByUserId = approved ? Required(actor, nameof(actor)) : null; SetStatus(approved ? ComplianceObligationStatuses.Approved : ComplianceObligationStatuses.Rejected, now); }
    public void RecordExport(string reference, string checksum, DateTime now) { RequireStatus(ComplianceObligationStatuses.Approved); ExportReference = Text(reference, nameof(reference), 500); ExportChecksum = Text(checksum, nameof(checksum), 128); SetStatus(ComplianceObligationStatuses.Exported, now); }
    public void RecordManualSubmission(DateTime now) { RequireStatus(ComplianceObligationStatuses.Exported); SetStatus(ComplianceObligationStatuses.ManualSubmissionRecorded, now); }
    public void RecordAcknowledgement(string kind, DateTime now) { if (Status is not ComplianceObligationStatuses.ManualSubmissionRecorded and not ComplianceObligationStatuses.AuthorityReceived) throw new InvalidOperationException("Authority evidence can only follow a recorded manual submission."); SetStatus(kind switch { "received" => ComplianceObligationStatuses.AuthorityReceived, "approved" when Status == ComplianceObligationStatuses.AuthorityReceived => ComplianceObligationStatuses.AuthorityApproved, "rejected" => ComplianceObligationStatuses.Rejected, _ => throw new InvalidOperationException("The acknowledgement transition is invalid.") }, now); }
    public void LinkCorrection(Guid correctionId, DateTime now) { CorrectedByInstanceId = Required(correctionId, nameof(correctionId)); SetStatus(ComplianceObligationStatuses.Corrected, now); }
    public void SetCorrectionOf(Guid originalId, DateTime now) { CorrectionOfInstanceId = Required(originalId, nameof(originalId)); Touch(now); }
    private void SetStatus(string value, DateTime now) { Status = value; Touch(now); }
    private void RequireStatus(string expected) { if (Status != expected) throw new InvalidOperationException($"The obligation must be {expected} for this action."); }
    private void Touch(DateTime now) { UpdatedUtc = Utc(now); Version++; }
    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentOutOfRangeException(name) : value;
    private static string Text(string? value, string name, int max) { var result = value?.Trim(); if (string.IsNullOrWhiteSpace(result) || result.Length > max) throw new ArgumentException($"{name} is required and limited to {max} characters.", name); return result; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class ComplianceObligationHistory : ICompanyOwnedEntity
{
    private ComplianceObligationHistory() { }
    public ComplianceObligationHistory(Guid id, Guid companyId, Guid instanceId, string action, string fromStatus, string toStatus, Guid actorUserId, string sourceHash, string? reason, DateTime occurredUtc)
    { Id=id; CompanyId=companyId; InstanceId=instanceId; Action=action; FromStatus=fromStatus; ToStatus=toStatus; ActorUserId=actorUserId; SourceHash=sourceHash; Reason=reason; OccurredUtc=occurredUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InstanceId { get; private set; }
    public string Action { get; private set; } = null!; public string FromStatus { get; private set; } = null!; public string ToStatus { get; private set; } = null!;
    public Guid ActorUserId { get; private set; } public string SourceHash { get; private set; } = null!; public string? Reason { get; private set; } public DateTime OccurredUtc { get; private set; }
    public ComplianceObligationInstance Instance { get; private set; } = null!;
}

public sealed class ComplianceSubmissionEvidence : ICompanyOwnedEntity
{
    private ComplianceSubmissionEvidence() { }
    public ComplianceSubmissionEvidence(Guid id, Guid companyId, Guid instanceId, string reference, string contentHash, Guid actorUserId, DateTime submittedUtc)
    { Id=id; CompanyId=companyId; InstanceId=instanceId; Reference=reference.Trim(); ContentHash=contentHash.Trim(); ActorUserId=actorUserId; SubmittedUtc=submittedUtc; ReviewStatus="pending"; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InstanceId { get; private set; }
    public string Reference { get; private set; } = null!; public string ContentHash { get; private set; } = null!; public Guid ActorUserId { get; private set; } public DateTime SubmittedUtc { get; private set; }
    public string ReviewStatus { get; private set; } = null!; public Guid? ReviewedByUserId { get; private set; } public DateTime? ReviewedUtc { get; private set; }
    public ComplianceObligationInstance Instance { get; private set; } = null!;
    public void Review(Guid actor, bool accepted, DateTime now) { if (actor == ActorUserId) throw new InvalidOperationException("Submission evidence requires independent review."); ReviewStatus = accepted ? "accepted" : "rejected"; ReviewedByUserId=actor; ReviewedUtc=now; }
}

public sealed class ComplianceAuthorityAcknowledgement : ICompanyOwnedEntity
{
    private ComplianceAuthorityAcknowledgement() { }
    public ComplianceAuthorityAcknowledgement(Guid id, Guid companyId, Guid instanceId, string kind, string reference, string contentHash, Guid actorUserId, DateTime occurredUtc)
    { Id=id; CompanyId=companyId; InstanceId=instanceId; Kind=kind.Trim().ToLowerInvariant(); Reference=reference.Trim(); ContentHash=contentHash.Trim(); ActorUserId=actorUserId; OccurredUtc=occurredUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InstanceId { get; private set; }
    public string Kind { get; private set; } = null!; public string Reference { get; private set; } = null!; public string ContentHash { get; private set; } = null!; public Guid ActorUserId { get; private set; } public DateTime OccurredUtc { get; private set; }
    public ComplianceObligationInstance Instance { get; private set; } = null!;
}

public sealed class ComplianceReminder : ICompanyOwnedEntity
{
    private ComplianceReminder() { }
    public ComplianceReminder(Guid id, Guid companyId, Guid instanceId, string kind, int escalationLevel, DateTime createdUtc)
    { Id=id; CompanyId=companyId; InstanceId=instanceId; Kind=kind; EscalationLevel=escalationLevel; CreatedUtc=createdUtc; Status="open"; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InstanceId { get; private set; }
    public string Kind { get; private set; } = null!; public int EscalationLevel { get; private set; } public string Status { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public ComplianceObligationInstance Instance { get; private set; } = null!;
}

public sealed class ComplianceCommandReceipt : ICompanyOwnedEntity
{
    private ComplianceCommandReceipt() { }
    public ComplianceCommandReceipt(Guid id, Guid companyId, string idempotencyKey, string payloadHash, Guid instanceId, DateTime createdUtc)
    { Id=id; CompanyId=companyId; IdempotencyKey=idempotencyKey.Trim(); PayloadHash=payloadHash; InstanceId=instanceId; CreatedUtc=createdUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string IdempotencyKey { get; private set; } = null!; public string PayloadHash { get; private set; } = null!; public Guid InstanceId { get; private set; } public DateTime CreatedUtc { get; private set; }
}
