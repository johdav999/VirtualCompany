namespace VirtualCompany.Application.Finance;

public static class ComplianceObligationActions
{
    public const string Prepare = "prepare"; public const string SubmitReview = "submit_review";
    public const string Approve = "approve"; public const string Reject = "reject"; public const string Export = "export";
    public const string MarkManualSubmitted = "mark_manual_submitted"; public const string RecordAcknowledgement = "record_acknowledgement";
    public const string Correct = "correct";
}

public sealed record ComplianceRequirementDto(string Kind, string Label, bool IsSatisfied, string? EvidenceReference);
public sealed record ComplianceHistoryDto(Guid Id, string Action, string FromStatus, string ToStatus, Guid ActorUserId, string SourceHash, string? Reason, DateTime OccurredUtc);
public sealed record ComplianceEvidenceDto(Guid Id, string Reference, string ContentHash, Guid ActorUserId, DateTime SubmittedUtc, string ReviewStatus, Guid? ReviewedByUserId, DateTime? ReviewedUtc);
public sealed record ComplianceAcknowledgementDto(Guid Id, string Kind, string Reference, string ContentHash, Guid ActorUserId, DateTime OccurredUtc);
public sealed record ComplianceReminderDto(Guid Id, string Kind, int EscalationLevel, string Status, DateTime CreatedUtc);

public sealed record ComplianceObligationDto(
    Guid Id, Guid CompanyId, string DefinitionKey, string Title, string Jurisdiction,
    string PolicyPackKey, string PolicyPackVersion, string PolicyPackDefinitionHash,
    string DueDateRule, DateOnly DueDate, Guid OwnerUserId, string Status, string SubmissionMode,
    Guid VatFilingPeriodId, Guid? VatReturnId, Guid? AccountingCloseTaskId,
    Guid? CorrectionOfInstanceId, Guid? CorrectedByInstanceId, string SourceHash,
    string? ExportReference, string? ExportChecksum, long Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<ComplianceRequirementDto> Requirements,
    IReadOnlyList<ComplianceHistoryDto> History,
    IReadOnlyList<ComplianceEvidenceDto> SubmissionEvidence,
    IReadOnlyList<ComplianceAcknowledgementDto> Acknowledgements,
    IReadOnlyList<ComplianceReminderDto> Reminders,
    IReadOnlyList<string> AllowedActions,
    string ComplianceNotice = "Artifacts and recorded evidence do not by themselves prove filing, authority receipt, approval, or statutory compliance.");

public sealed record ComplianceCalendarDto(Guid CompanyId, DateOnly From, DateOnly To,
    int OpenCount, int DueSoonCount, int OverdueCount, int AwaitingAuthorityCount,
    IReadOnlyList<ComplianceObligationDto> Obligations,
    string SubmissionCapability = "export_and_manual_evidence_only");

public sealed record GenerateComplianceObligationsCommand(Guid CompanyId, Guid OwnerUserId, Guid ActorUserId, string IdempotencyKey);
public sealed record TransitionComplianceObligationCommand(Guid CompanyId, Guid InstanceId, string Action, Guid ActorUserId, string IdempotencyKey, long ExpectedVersion, string? Reason = null);
public sealed record RecordComplianceSubmissionCommand(Guid CompanyId, Guid InstanceId, string EvidenceReference, string EvidenceHash, Guid ActorUserId, string IdempotencyKey, long ExpectedVersion);
public sealed record RecordComplianceAcknowledgementCommand(Guid CompanyId, Guid InstanceId, string Kind, string Reference, string EvidenceHash, Guid ActorUserId, string IdempotencyKey, long ExpectedVersion);
public sealed record ReviewComplianceEvidenceCommand(Guid CompanyId, Guid InstanceId, Guid EvidenceId, bool Accepted, Guid ActorUserId, string IdempotencyKey, long ExpectedVersion);
public sealed record CorrectComplianceObligationCommand(Guid CompanyId, Guid InstanceId, string Reason, Guid ActorUserId, string IdempotencyKey, long ExpectedVersion);
public sealed record GetComplianceCalendarQuery(Guid CompanyId, DateOnly From, DateOnly To);

public interface IComplianceObligationService
{
    Task<ComplianceCalendarDto> GetCalendarAsync(GetComplianceCalendarQuery query, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> GetAsync(Guid companyId, Guid instanceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ComplianceObligationDto>> GenerateAsync(GenerateComplianceObligationsCommand command, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> TransitionAsync(TransitionComplianceObligationCommand command, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> RecordManualSubmissionAsync(RecordComplianceSubmissionCommand command, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> RecordAcknowledgementAsync(RecordComplianceAcknowledgementCommand command, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> ReviewEvidenceAsync(ReviewComplianceEvidenceCommand command, CancellationToken cancellationToken);
    Task<ComplianceObligationDto> CorrectAsync(CorrectComplianceObligationCommand command, CancellationToken cancellationToken);
    Task<int> GenerateRemindersAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class ComplianceObligationException : Exception
{
    public ComplianceObligationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
