using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingChangeRequest : ICompanyOwnedEntity
{
    private SalesMeetingChangeRequest() { }

    public SalesMeetingChangeRequest(
        Guid id, Guid companyId, Guid invitationId, SalesMeetingChangeOperation operation,
        Guid requestedByUserId, DateTime? startsUtc = null, DateTime? endsUtc = null,
        string? timeZoneId = null, string? title = null, string? description = null,
        string? location = null, bool? createOnlineMeeting = null, DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (invitationId == Guid.Empty) throw new ArgumentException("InvitationId is required.", nameof(invitationId));
        if (requestedByUserId == Guid.Empty) throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId));
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        InvitationId = invitationId;
        Operation = operation;
        RequestedByUserId = requestedByUserId;
        if (operation == SalesMeetingChangeOperation.Reschedule)
        {
            StartsUtc = NormalizeUtc(startsUtc ?? default, nameof(startsUtc));
            EndsUtc = NormalizeUtc(endsUtc ?? default, nameof(endsUtc));
            if (EndsUtc <= StartsUtc) throw new ArgumentException("The meeting end must be after its start.", nameof(endsUtc));
            TimeZoneId = Required(timeZoneId, nameof(timeZoneId), 100);
            Title = Required(title, nameof(title), 200);
            Description = Required(description, nameof(description), 4000);
            Location = Optional(location, nameof(location), 500);
            CreateOnlineMeeting = createOnlineMeeting ?? true;
        }

        Status = SalesMeetingChangeRequestStatus.Draft;
        IdempotencyKey = $"sales-meeting-change:{companyId:N}:{Id:N}:v1";
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InvitationId { get; private set; }
    public SalesMeetingChangeOperation Operation { get; private set; }
    public DateTime? StartsUtc { get; private set; }
    public DateTime? EndsUtc { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string? Location { get; private set; }
    public bool? CreateOnlineMeeting { get; private set; }
    public SalesMeetingChangeRequestStatus Status { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public int ExecutionAttemptCount { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public SalesMeetingInvitation Invitation { get; private set; } = null!;
    public Company Company { get; private set; } = null!;

    public void SubmitForApproval(Guid approvalRequestId)
    {
        if (Status != SalesMeetingChangeRequestStatus.Draft) throw new InvalidOperationException("Only a draft meeting change can be submitted for approval.");
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        ApprovalRequestId = approvalRequestId;
        Status = SalesMeetingChangeRequestStatus.WaitingForApproval;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkApproved(Guid? approvedByUserId, DateTime approvedUtc)
    {
        if (Status != SalesMeetingChangeRequestStatus.WaitingForApproval) throw new InvalidOperationException("Only a pending meeting change can be approved.");
        ApprovedByUserId = approvedByUserId;
        ApprovedUtc = NormalizeUtc(approvedUtc, nameof(approvedUtc));
        Status = SalesMeetingChangeRequestStatus.Queued;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void MarkRejected(DateTime decidedUtc)
    {
        if (Status != SalesMeetingChangeRequestStatus.WaitingForApproval) throw new InvalidOperationException("Only a pending meeting change can be rejected.");
        Status = SalesMeetingChangeRequestStatus.Rejected;
        UpdatedUtc = NormalizeUtc(decidedUtc, nameof(decidedUtc));
    }

    public void BeginExecution()
    {
        if (Status is not (SalesMeetingChangeRequestStatus.Queued or SalesMeetingChangeRequestStatus.Failed))
            throw new InvalidOperationException("This meeting change is not ready to execute.");
        ExecutionAttemptCount++;
        Status = SalesMeetingChangeRequestStatus.Executing;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkCompleted(DateTime completedUtc)
    {
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        Status = SalesMeetingChangeRequestStatus.Completed;
        LastErrorCode = null;
        LastErrorSummary = null;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkFailed(string code, string summary) => MarkProblem(code, summary, SalesMeetingChangeRequestStatus.Failed);
    public void MarkReconciliationRequired(string code, string summary) => MarkProblem(code, summary, SalesMeetingChangeRequestStatus.ReconciliationRequired);

    private void MarkProblem(string code, string summary, SalesMeetingChangeRequestStatus status)
    {
        LastErrorCode = Required(code, nameof(code), 120);
        LastErrorSummary = Required(summary, nameof(summary), 1000);
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static DateTime NormalizeUtc(DateTime value, string name) => value == default
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return trimmed;
    }

    private static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);
}
