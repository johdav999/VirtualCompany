namespace VirtualCompany.Application.Finance;

public static class ComplianceObligationActions
{
    public const string Prepare = "prepare"; public const string SubmitReview = "submit_review";
    public const string Approve = "approve"; public const string Reject = "reject"; public const string Export = "export";
    public const string MarkManualSubmitted = "mark_manual_submitted"; public const string RecordAcknowledgement = "record_acknowledgement";
    public const string Correct = "correct";
}

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
