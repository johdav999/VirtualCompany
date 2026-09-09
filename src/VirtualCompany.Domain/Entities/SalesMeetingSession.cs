using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingSession : ICompanyOwnedEntity
{
    private static readonly IReadOnlyDictionary<SalesMeetingSessionStatus, IReadOnlySet<SalesMeetingSessionStatus>> AllowedTransitions =
        new Dictionary<SalesMeetingSessionStatus, IReadOnlySet<SalesMeetingSessionStatus>>
        {
            [SalesMeetingSessionStatus.Ready] = Set(SalesMeetingSessionStatus.Presenting, SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Presenting] = Set(SalesMeetingSessionStatus.Interrupted, SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Interrupted] = Set(SalesMeetingSessionStatus.Answering, SalesMeetingSessionStatus.Resuming, SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Answering] = Set(SalesMeetingSessionStatus.Resuming, SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Resuming] = Set(SalesMeetingSessionStatus.Presenting, SalesMeetingSessionStatus.Interrupted, SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Discussion] = Set(SalesMeetingSessionStatus.Presenting, SalesMeetingSessionStatus.Closing, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Closing] = Set(SalesMeetingSessionStatus.Discussion, SalesMeetingSessionStatus.Completed, SalesMeetingSessionStatus.Cancelled, SalesMeetingSessionStatus.Failed),
            [SalesMeetingSessionStatus.Completed] = Set(),
            [SalesMeetingSessionStatus.Cancelled] = Set(),
            [SalesMeetingSessionStatus.Failed] = Set()
        };

    private SalesMeetingSession() { }

    public SalesMeetingSession(
        Guid id,
        Guid companyId,
        Guid invitationId,
        Guid leadId,
        Guid? dealId,
        Guid? contactId,
        Guid customerCompanyId,
        string meetingGoal,
        string intendedAudience,
        int plannedDurationMinutes,
        string? demoScenario,
        string providerMeetingId,
        SalesMeetingConsentStatus consentStatus,
        SalesMeetingRetentionPolicy retentionPolicy,
        int retentionDays,
        DateTime retentionStartsUtc,
        Guid createdByUserId,
        DateTime createdUtc)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(invitationId, nameof(invitationId));
        EnsureId(leadId, nameof(leadId));
        EnsureOptionalId(dealId, nameof(dealId));
        EnsureOptionalId(contactId, nameof(contactId));
        EnsureId(customerCompanyId, nameof(customerCompanyId));
        EnsureId(createdByUserId, nameof(createdByUserId));
        ValidatePreparation(plannedDurationMinutes, retentionPolicy, retentionDays);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        InvitationId = invitationId;
        LeadId = leadId;
        DealId = dealId;
        ContactId = contactId;
        CustomerCompanyId = customerCompanyId;
        MeetingGoal = Required(meetingGoal, nameof(meetingGoal), 1000);
        IntendedAudience = Required(intendedAudience, nameof(intendedAudience), 1000);
        PlannedDurationMinutes = plannedDurationMinutes;
        DemoScenario = Optional(demoScenario, nameof(demoScenario), 4000);
        ProviderMeetingId = Required(providerMeetingId, nameof(providerMeetingId), 512);
        Status = SalesMeetingSessionStatus.Ready;
        CurrentSlideIndex = 0;
        CurrentTalkingPointIndex = 0;
        ConsentStatus = consentStatus;
        RetentionPolicy = retentionPolicy;
        RetentionDays = retentionDays;
        RetentionStartsUtc = Utc(retentionStartsUtc, nameof(retentionStartsUtc));
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedUtc = Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        ConsentRecordedUtc = consentStatus == SalesMeetingConsentStatus.NotRequested ? null : CreatedUtc;
        ConsentRecordedByUserId = ConsentRecordedUtc.HasValue ? createdByUserId : null;
        RetentionUntilUtc = RetentionStartsUtc.AddDays(retentionDays);
        ConcurrencyVersion = 1;
        LastPresentationSequence = 0;
        PresentationControlMode = "manual";
        PresentationControlUpdatedByUserId = createdByUserId;
        PresentationControlUpdatedUtc = CreatedUtc;
        CaptureVersion = 0;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InvitationId { get; private set; }
    public Guid? PresenterAgentId { get; private set; }

    public void SelectPresenter(Guid agentId, Guid actorId, DateTime nowUtc)
    {
        EnsureId(agentId, nameof(agentId));
        if (actorId != CreatedByUserId) throw new UnauthorizedAccessException("Only the meeting organizer can select its presenter.");
        if (Status != SalesMeetingSessionStatus.Ready) throw new InvalidOperationException("Select a presenter before the meeting starts.");
        if (PresenterAgentId == agentId) return;
        PresenterAgentId = agentId;
        UpdatedByUserId = actorId;
        UpdatedUtc = Utc(nowUtc, nameof(nowUtc));
        ConcurrencyVersion++;
    }
    public Guid LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid CustomerCompanyId { get; private set; }
    public string MeetingGoal { get; private set; } = null!;
    public string IntendedAudience { get; private set; } = null!;
    public int PlannedDurationMinutes { get; private set; }
    public string? DemoScenario { get; private set; }
    public string ProviderMeetingId { get; private set; } = null!;
    public SalesMeetingSessionStatus Status { get; private set; }
    public int CurrentSlideIndex { get; private set; }
    public int CurrentTalkingPointIndex { get; private set; }
    public string? ResumeMarker { get; private set; }
    public SalesMeetingConsentStatus ConsentStatus { get; private set; }
    public DateTime? ConsentRecordedUtc { get; private set; }
    public Guid? ConsentRecordedByUserId { get; private set; }
    public SalesMeetingRetentionPolicy RetentionPolicy { get; private set; }
    public int RetentionDays { get; private set; }
    public DateTime RetentionStartsUtc { get; private set; }
    public DateTime RetentionUntilUtc { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime? EndedUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public long LastPresentationSequence { get; private set; }
    public Guid? LastPresentationCommandId { get; private set; }
    public string PresentationControlMode { get; private set; } = "manual";
    public Guid PresentationControlUpdatedByUserId { get; private set; }
    public DateTime PresentationControlUpdatedUtc { get; private set; }
    public long CaptureVersion { get; private set; }
    public Guid? LastCaptureBatchId { get; private set; }
    public long TranscriptReconciliationVersion { get; private set; }
    public Guid? LastTranscriptReconciliationId { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesMeetingInvitation Invitation { get; private set; } = null!;
    public Lead Lead { get; private set; } = null!;
    public Deal? Deal { get; private set; }
    public Contact? Contact { get; private set; }
    public CustomerCompany CustomerCompany { get; private set; } = null!;

    public bool HasPreparation(
        string meetingGoal,
        string intendedAudience,
        int plannedDurationMinutes,
        string? demoScenario,
        SalesMeetingConsentStatus consentStatus,
        SalesMeetingRetentionPolicy retentionPolicy,
        int retentionDays) =>
        string.Equals(MeetingGoal, meetingGoal?.Trim(), StringComparison.Ordinal) &&
        string.Equals(IntendedAudience, intendedAudience?.Trim(), StringComparison.Ordinal) &&
        PlannedDurationMinutes == plannedDurationMinutes &&
        string.Equals(DemoScenario, NormalizeOptional(demoScenario), StringComparison.Ordinal) &&
        ConsentStatus == consentStatus &&
        RetentionPolicy == retentionPolicy &&
        RetentionDays == retentionDays;

    public void UpdatePreparation(
        string meetingGoal,
        string intendedAudience,
        int plannedDurationMinutes,
        string? demoScenario,
        SalesMeetingConsentStatus consentStatus,
        SalesMeetingRetentionPolicy retentionPolicy,
        int retentionDays,
        Guid actorUserId,
        DateTime updatedUtc)
    {
        if (Status != SalesMeetingSessionStatus.Ready)
        {
            throw new InvalidOperationException("Meeting preparation can only be changed while the session is ready.");
        }

        EnsureId(actorUserId, nameof(actorUserId));
        ValidatePreparation(plannedDurationMinutes, retentionPolicy, retentionDays);
        var now = Utc(updatedUtc, nameof(updatedUtc));
        var normalizedConsent = consentStatus;

        MeetingGoal = Required(meetingGoal, nameof(meetingGoal), 1000);
        IntendedAudience = Required(intendedAudience, nameof(intendedAudience), 1000);
        PlannedDurationMinutes = plannedDurationMinutes;
        DemoScenario = Optional(demoScenario, nameof(demoScenario), 4000);
        if (ConsentStatus != normalizedConsent)
        {
            ConsentStatus = normalizedConsent;
            ConsentRecordedUtc = normalizedConsent == SalesMeetingConsentStatus.NotRequested ? null : now;
            ConsentRecordedByUserId = ConsentRecordedUtc.HasValue ? actorUserId : null;
        }

        RetentionPolicy = retentionPolicy;
        RetentionDays = retentionDays;
        RetentionUntilUtc = RetentionStartsUtc.AddDays(retentionDays);
        UpdatedByUserId = actorUserId;
        UpdatedUtc = now;
        ConcurrencyVersion++;
    }

    public void TransitionTo(
        SalesMeetingSessionStatus targetStatus,
        int? currentSlideIndex,
        int? currentTalkingPointIndex,
        string? resumeMarker,
        string? reason,
        Guid actorUserId,
        DateTime transitionedUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        _ = targetStatus.ToStorageValue();
        if (!AllowedTransitions[Status].Contains(targetStatus))
        {
            throw new InvalidOperationException($"A sales meeting session cannot transition from {Status.ToStorageValue()} to {targetStatus.ToStorageValue()}.");
        }

        var slide = currentSlideIndex ?? CurrentSlideIndex;
        var talkingPoint = currentTalkingPointIndex ?? CurrentTalkingPointIndex;
        if (slide < 0) throw new ArgumentOutOfRangeException(nameof(currentSlideIndex), "Current slide index cannot be negative.");
        if (talkingPoint < 0) throw new ArgumentOutOfRangeException(nameof(currentTalkingPointIndex), "Current talking-point index cannot be negative.");

        var marker = Optional(resumeMarker, nameof(resumeMarker), 1000) ?? ResumeMarker;
        if (targetStatus == SalesMeetingSessionStatus.Interrupted && string.IsNullOrWhiteSpace(marker))
        {
            throw new InvalidOperationException("An interrupted meeting must preserve a resume marker.");
        }

        if (targetStatus == SalesMeetingSessionStatus.Resuming && string.IsNullOrWhiteSpace(marker))
        {
            throw new InvalidOperationException("A meeting cannot resume without a saved resume marker.");
        }

        var now = Utc(transitionedUtc, nameof(transitionedUtc));
        Status = targetStatus;
        CurrentSlideIndex = slide;
        CurrentTalkingPointIndex = talkingPoint;
        ResumeMarker = marker;
        StatusReason = targetStatus is SalesMeetingSessionStatus.Cancelled or SalesMeetingSessionStatus.Failed
            ? Required(reason, nameof(reason), 1000)
            : Optional(reason, nameof(reason), 1000);
        EndedUtc = targetStatus is SalesMeetingSessionStatus.Completed or SalesMeetingSessionStatus.Cancelled or SalesMeetingSessionStatus.Failed
            ? now
            : null;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = now;
        ConcurrencyVersion++;
    }

    public void ApplyPresentationCommand(
        SalesPresentationCommandType commandType,
        Guid commandId,
        long sequence,
        long expectedVersion,
        int? targetSlideIndex,
        int? targetTalkingPointIndex,
        string? resumeMarker,
        Guid actorUserId,
        DateTime occurredUtc)
    {
        EnsureId(commandId, nameof(commandId));
        EnsureId(actorUserId, nameof(actorUserId));
        if (expectedVersion != ConcurrencyVersion)
            throw new InvalidOperationException("The presentation state changed after it was opened.");
        if (sequence != LastPresentationSequence + 1)
            throw new InvalidOperationException("The presentation command sequence is out of order.");

        var now = Utc(occurredUtc, nameof(occurredUtc));
        switch (commandType)
        {
            case SalesPresentationCommandType.Next:
            case SalesPresentationCommandType.Previous:
            case SalesPresentationCommandType.Goto:
                if (Status is not (SalesMeetingSessionStatus.Ready or SalesMeetingSessionStatus.Presenting))
                    throw new InvalidOperationException("Slides can only be changed while the meeting is ready or presenting.");
                if (targetSlideIndex is null or < 1)
                    throw new ArgumentOutOfRangeException(nameof(targetSlideIndex), "A valid target slide is required.");
                if (targetTalkingPointIndex is < 0)
                    throw new ArgumentOutOfRangeException(nameof(targetTalkingPointIndex));
                CurrentSlideIndex = targetSlideIndex.Value;
                CurrentTalkingPointIndex = targetTalkingPointIndex ?? 0;
                Status = SalesMeetingSessionStatus.Presenting;
                break;
            case SalesPresentationCommandType.Pause:
                if (Status != SalesMeetingSessionStatus.Presenting)
                    throw new InvalidOperationException("Only a presenting meeting can be paused.");
                var pausedSlide = targetSlideIndex ?? CurrentSlideIndex;
                var pausedTalkingPoint = targetTalkingPointIndex ?? CurrentTalkingPointIndex;
                if (pausedSlide < 1)
                    throw new InvalidOperationException("A current slide is required before pausing.");
                if (pausedTalkingPoint < 0)
                    throw new ArgumentOutOfRangeException(nameof(targetTalkingPointIndex));
                var marker = Optional(resumeMarker, nameof(resumeMarker), 1000);
                if (string.IsNullOrWhiteSpace(marker))
                    throw new InvalidOperationException("Pausing must preserve an exact resume marker.");
                CurrentSlideIndex = pausedSlide;
                CurrentTalkingPointIndex = pausedTalkingPoint;
                ResumeMarker = marker;
                Status = SalesMeetingSessionStatus.Interrupted;
                break;
            case SalesPresentationCommandType.Resume:
                if (Status is not (SalesMeetingSessionStatus.Interrupted or SalesMeetingSessionStatus.Answering))
                    throw new InvalidOperationException("Only an interrupted or answering meeting can resume.");
                if (string.IsNullOrWhiteSpace(ResumeMarker))
                    throw new InvalidOperationException("The presentation has no saved resume marker.");
                // Resuming is an explicit lifecycle checkpoint even though the single atomic
                // command restores the persisted presentation position before returning.
                Status = SalesMeetingSessionStatus.Resuming;
                Status = SalesMeetingSessionStatus.Presenting;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(commandType));
        }

        LastPresentationSequence = sequence;
        LastPresentationCommandId = commandId;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = now;
        StatusReason = null;
        EndedUtc = null;
        ConcurrencyVersion++;
    }

    public void SetPresentationControlMode(string mode, long expectedVersion, Guid actorUserId, DateTime occurredUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        if (actorUserId != CreatedByUserId)
            throw new UnauthorizedAccessException("Only the meeting organizer can change presentation control mode.");
        if (expectedVersion != ConcurrencyVersion)
            throw new InvalidOperationException("The presentation state changed after it was opened.");
        var normalized = string.IsNullOrWhiteSpace(mode) ? string.Empty : mode.Trim().ToLowerInvariant();
        if (normalized is not ("manual" or "assisted" or "autonomous"))
            throw new ArgumentOutOfRangeException(nameof(mode), "Presentation control mode must be manual, assisted, or autonomous.");
        var now = Utc(occurredUtc, nameof(occurredUtc));
        PresentationControlMode = normalized;
        PresentationControlUpdatedByUserId = actorUserId;
        PresentationControlUpdatedUtc = now;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = now;
        ConcurrencyVersion++;
    }

    public void ApplyCaptureBatch(Guid batchId, long expectedCaptureVersion, Guid actorUserId, DateTime occurredUtc)
    {
        EnsureId(batchId, nameof(batchId));
        EnsureId(actorUserId, nameof(actorUserId));
        if (expectedCaptureVersion != CaptureVersion)
            throw new InvalidOperationException("The meeting capture changed after it was opened.");
        LastCaptureBatchId = batchId;
        CaptureVersion++;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = Utc(occurredUtc, nameof(occurredUtc));
    }

    public void RevokeMediaConsent(Guid actorUserId, DateTime occurredUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        var now = Utc(occurredUtc, nameof(occurredUtc));
        ConsentStatus = SalesMeetingConsentStatus.Revoked;
        ConsentRecordedUtc = now;
        ConsentRecordedByUserId = actorUserId;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = now;
        ConcurrencyVersion++;
    }

    public void ApplyTranscriptReconciliation(Guid reconciliationId, DateTime occurredUtc)
    {
        EnsureId(reconciliationId, nameof(reconciliationId));
        LastTranscriptReconciliationId = reconciliationId;
        TranscriptReconciliationVersion++;
        CaptureVersion++;
        UpdatedUtc = Utc(occurredUtc, nameof(occurredUtc));
        ConcurrencyVersion++;
    }

    private static HashSet<SalesMeetingSessionStatus> Set(params SalesMeetingSessionStatus[] statuses) => [.. statuses];

    private static void ValidatePreparation(int plannedDurationMinutes, SalesMeetingRetentionPolicy policy, int retentionDays)
    {
        _ = policy.ToStorageValue();
        if (plannedDurationMinutes is < 5 or > 480)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedDurationMinutes), "Planned duration must be between 5 and 480 minutes.");
        }

        if (retentionDays is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention must be between 1 and 3650 days.");
        }

        if (policy == SalesMeetingRetentionPolicy.Standard && retentionDays != 365)
        {
            throw new ArgumentException("The standard meeting retention policy is 365 days.", nameof(retentionDays));
        }
    }

    private static void EnsureId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }

    private static void EnsureOptionalId(Guid? value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} cannot be empty.", name);
    }

    private static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime Utc(DateTime value, string name) => value == default
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
