using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingInvitation : ICompanyOwnedEntity
{
    private SalesMeetingInvitation() { }

    public SalesMeetingInvitation(
        Guid id, Guid companyId, Guid leadId, Guid? dealId, Guid? contactId,
        Guid calendarConnectionId, ExternalAccountProvider provider, string organizerEmail,
        string attendeeEmail, string? attendeeName, string title, string description,
        DateTime startsUtc, DateTime endsUtc, string timeZoneId, string? location,
        bool createOnlineMeeting, Guid createdByUserId, DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (leadId == Guid.Empty) throw new ArgumentException("LeadId is required.", nameof(leadId));
        if (calendarConnectionId == Guid.Empty) throw new ArgumentException("CalendarConnectionId is required.", nameof(calendarConnectionId));
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        if (dealId == Guid.Empty) throw new ArgumentException("DealId cannot be empty.", nameof(dealId));
        if (contactId == Guid.Empty) throw new ArgumentException("ContactId cannot be empty.", nameof(contactId));
        _ = provider.ToStorageValue();
        var start = NormalizeUtc(startsUtc, nameof(startsUtc));
        var end = NormalizeUtc(endsUtc, nameof(endsUtc));
        if (end <= start) throw new ArgumentException("The meeting end must be after its start.", nameof(endsUtc));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LeadId = leadId;
        DealId = dealId;
        ContactId = contactId;
        CalendarConnectionId = calendarConnectionId;
        Provider = provider;
        CalendarId = "primary";
        OrganizerEmail = NormalizeEmail(organizerEmail, nameof(organizerEmail));
        AttendeeEmail = NormalizeEmail(attendeeEmail, nameof(attendeeEmail));
        AttendeeName = NormalizeOptional(attendeeName, nameof(attendeeName), 160);
        Title = NormalizeRequired(title, nameof(title), 200);
        Description = NormalizeRequired(description, nameof(description), 4000);
        StartsUtc = start;
        EndsUtc = end;
        TimeZoneId = NormalizeRequired(timeZoneId, nameof(timeZoneId), 100);
        Location = NormalizeOptional(location, nameof(location), 500);
        CreateOnlineMeeting = createOnlineMeeting;
        CreatedByUserId = createdByUserId;
        IdempotencyKey = $"sales-meeting:{companyId:N}:{Id:N}:v1";
        ConfirmationIdempotencyKey = $"{IdempotencyKey}:confirmation:v1";
        Status = SalesMeetingInvitationStatus.Draft;
        ConfirmationStatus = SalesMeetingConfirmationStatus.NotQueued;
        ConfirmationThreadingMode = MailboxReplyThreadingMode.Unknown;
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid CalendarConnectionId { get; private set; }
    public ExternalAccountProvider Provider { get; private set; }
    public string CalendarId { get; private set; } = "primary";
    public string OrganizerEmail { get; private set; } = null!;
    public string AttendeeEmail { get; private set; } = null!;
    public string? AttendeeName { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public DateTime StartsUtc { get; private set; }
    public DateTime EndsUtc { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public string? Location { get; private set; }
    public bool CreateOnlineMeeting { get; private set; }
    public SalesMeetingInvitationStatus Status { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string? ExternalEventId { get; private set; }
    public string? ExternalICalUid { get; private set; }
    public string? ProviderWebUrl { get; private set; }
    public string? OnlineMeetingUrl { get; private set; }
    public SalesMeetingConfirmationStatus ConfirmationStatus { get; private set; }
    public string ConfirmationIdempotencyKey { get; private set; } = null!;
    public Guid? ConfirmationMailboxConnectionId { get; private set; }
    public string? ConfirmationProviderMessageId { get; private set; }
    public string? ConfirmationProviderThreadId { get; private set; }
    public MailboxReplyThreadingMode ConfirmationThreadingMode { get; private set; }
    public int ConfirmationAttemptCount { get; private set; }
    public string? ConfirmationErrorCode { get; private set; }
    public string? ConfirmationErrorSummary { get; private set; }
    public DateTime? ConfirmationSentUtc { get; private set; }
    public int ExecutionAttemptCount { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ScheduledUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Lead Lead { get; private set; } = null!;
    public Deal? Deal { get; private set; }
    public Contact? Contact { get; private set; }
    public CalendarConnection CalendarConnection { get; private set; } = null!;
    public MailboxConnection? ConfirmationMailboxConnection { get; private set; }

    public void SubmitForApproval(Guid approvalRequestId)
    {
        if (Status != SalesMeetingInvitationStatus.Draft) throw new InvalidOperationException("Only a draft meeting can be submitted for approval.");
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        ApprovalRequestId = approvalRequestId;
        Status = SalesMeetingInvitationStatus.WaitingForApproval;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkApproved(Guid? approvedByUserId, DateTime approvedUtc)
    {
        if (Status != SalesMeetingInvitationStatus.WaitingForApproval) throw new InvalidOperationException("Only a pending meeting invitation can be approved.");
        ApprovedByUserId = approvedByUserId;
        ApprovedUtc = NormalizeUtc(approvedUtc, nameof(approvedUtc));
        Status = SalesMeetingInvitationStatus.Queued;
        LastErrorCode = null;
        LastErrorSummary = null;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void MarkRejected(DateTime decidedUtc)
    {
        if (Status != SalesMeetingInvitationStatus.WaitingForApproval) throw new InvalidOperationException("Only a pending meeting invitation can be rejected.");
        Status = SalesMeetingInvitationStatus.Rejected;
        UpdatedUtc = NormalizeUtc(decidedUtc, nameof(decidedUtc));
    }

    public void BeginScheduling()
    {
        if (Status is not (SalesMeetingInvitationStatus.Queued or SalesMeetingInvitationStatus.Failed))
            throw new InvalidOperationException("This meeting invitation is not ready to schedule.");
        ExecutionAttemptCount++;
        Status = SalesMeetingInvitationStatus.Scheduling;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkScheduled(string externalEventId, string? externalICalUid, string? providerWebUrl, string? onlineMeetingUrl, DateTime scheduledUtc)
    {
        ExternalEventId = NormalizeRequired(externalEventId, nameof(externalEventId), 512);
        ExternalICalUid = NormalizeOptional(externalICalUid, nameof(externalICalUid), 512);
        ProviderWebUrl = NormalizeOptional(providerWebUrl, nameof(providerWebUrl), 2000);
        OnlineMeetingUrl = NormalizeOptional(onlineMeetingUrl, nameof(onlineMeetingUrl), 2000);
        ScheduledUtc = NormalizeUtc(scheduledUtc, nameof(scheduledUtc));
        Status = SalesMeetingInvitationStatus.Scheduled;
        LastErrorCode = null;
        LastErrorSummary = null;
        UpdatedUtc = ScheduledUtc.Value;
    }

    public void MarkFailed(string code, string summary)
    {
        LastErrorCode = NormalizeRequired(code, nameof(code), 120);
        LastErrorSummary = NormalizeRequired(summary, nameof(summary), 1000);
        Status = SalesMeetingInvitationStatus.Failed;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkReconciliationRequired(string code, string summary)
    {
        LastErrorCode = NormalizeRequired(code, nameof(code), 120);
        LastErrorSummary = NormalizeRequired(summary, nameof(summary), 1000);
        Status = SalesMeetingInvitationStatus.ReconciliationRequired;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void QueueConfirmation()
    {
        if (Status != SalesMeetingInvitationStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled meeting can queue a confirmation.");
        if (ConfirmationStatus == SalesMeetingConfirmationStatus.Sent) return;
        if (ConfirmationStatus != SalesMeetingConfirmationStatus.NotQueued)
            throw new InvalidOperationException("The meeting confirmation has already been queued.");
        ConfirmationStatus = SalesMeetingConfirmationStatus.Queued;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void BeginConfirmationDelivery()
    {
        if (ConfirmationStatus is not (SalesMeetingConfirmationStatus.Queued or SalesMeetingConfirmationStatus.Failed))
            throw new InvalidOperationException("This meeting confirmation is not ready to send.");
        ConfirmationAttemptCount++;
        ConfirmationStatus = SalesMeetingConfirmationStatus.Sending;
        ConfirmationErrorCode = null;
        ConfirmationErrorSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkConfirmationSent(
        Guid mailboxConnectionId, string providerMessageId,
        string? providerThreadId, MailboxReplyThreadingMode threadingMode,
        DateTime sentUtc)
    {
        if (ConfirmationStatus != SalesMeetingConfirmationStatus.Sending)
            throw new InvalidOperationException("The meeting confirmation is not being sent.");
        if (mailboxConnectionId == Guid.Empty)
            throw new ArgumentException("MailboxConnectionId is required.", nameof(mailboxConnectionId));
        ConfirmationMailboxConnectionId = mailboxConnectionId;
        ConfirmationProviderMessageId = NormalizeRequired(providerMessageId, nameof(providerMessageId), 512);
        ConfirmationProviderThreadId = NormalizeOptional(providerThreadId, nameof(providerThreadId), 512);
        ConfirmationThreadingMode = threadingMode;
        ConfirmationSentUtc = NormalizeUtc(sentUtc, nameof(sentUtc));
        ConfirmationStatus = SalesMeetingConfirmationStatus.Sent;
        ConfirmationErrorCode = null;
        ConfirmationErrorSummary = null;
        UpdatedUtc = ConfirmationSentUtc.Value;
    }

    public void MarkConfirmationFailed(string code, string summary) =>
        MarkConfirmationProblem(code, summary, SalesMeetingConfirmationStatus.Failed);

    public void MarkConfirmationReconciliationRequired(string code, string summary) =>
        MarkConfirmationProblem(code, summary, SalesMeetingConfirmationStatus.ReconciliationRequired);

    public void MarkConfirmationUnavailable(string summary)
    {
        ConfirmationStatus = SalesMeetingConfirmationStatus.Unavailable;
        ConfirmationErrorCode = "sales_thread_unavailable";
        ConfirmationErrorSummary = NormalizeRequired(summary, nameof(summary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    private void MarkConfirmationProblem(
        string code, string summary, SalesMeetingConfirmationStatus status)
    {
        ConfirmationErrorCode = NormalizeRequired(code, nameof(code), 120);
        ConfirmationErrorSummary = NormalizeRequired(summary, nameof(summary), 1000);
        ConfirmationStatus = status;
        UpdatedUtc = DateTime.UtcNow;
    }
    public void ApplyReschedule(
        string title, string description, DateTime startsUtc, DateTime endsUtc,
        string timeZoneId, string? location, bool createOnlineMeeting,
        string? providerWebUrl, string? onlineMeetingUrl, DateTime updatedUtc)
    {
        if (Status != SalesMeetingInvitationStatus.Scheduled) throw new InvalidOperationException("Only a scheduled meeting can be rescheduled.");
        var start = NormalizeUtc(startsUtc, nameof(startsUtc));
        var end = NormalizeUtc(endsUtc, nameof(endsUtc));
        if (end <= start) throw new ArgumentException("The meeting end must be after its start.", nameof(endsUtc));
        Title = NormalizeRequired(title, nameof(title), 200);
        Description = NormalizeRequired(description, nameof(description), 4000);
        StartsUtc = start;
        EndsUtc = end;
        TimeZoneId = NormalizeRequired(timeZoneId, nameof(timeZoneId), 100);
        Location = NormalizeOptional(location, nameof(location), 500);
        CreateOnlineMeeting = createOnlineMeeting;
        ProviderWebUrl = NormalizeOptional(providerWebUrl, nameof(providerWebUrl), 2000) ?? ProviderWebUrl;
        OnlineMeetingUrl = NormalizeOptional(onlineMeetingUrl, nameof(onlineMeetingUrl), 2000) ?? OnlineMeetingUrl;
        UpdatedUtc = NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void MarkCancelled(DateTime cancelledUtc)
    {
        if (Status != SalesMeetingInvitationStatus.Scheduled) throw new InvalidOperationException("Only a scheduled meeting can be cancelled.");
        Status = SalesMeetingInvitationStatus.Cancelled;
        UpdatedUtc = NormalizeUtc(cancelledUtc, nameof(cancelledUtc));
    }
    private static DateTime NormalizeUtc(DateTime value, string name) => value == default
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeEmail(string value, string name)
    {
        var normalized = NormalizeRequired(value, name, 256).ToLowerInvariant();
        if (!normalized.Contains('@', StringComparison.Ordinal)) throw new ArgumentException($"{name} must be a valid email address.", name);
        return normalized;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, name, maxLength);
}
