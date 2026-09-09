namespace VirtualCompany.Application.Sales;

public static class SalesMeetingSessionProblemCodes
{
    public const string Conflict = "sales.meeting_session.conflict";
    public const string InvalidTransition = "sales.meeting_session.invalid_transition";
    public const string InvalidRelationship = "sales.meeting_session.invalid_relationship";
}

public sealed class SalesMeetingSessionConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record CreateOrUpdateSalesMeetingSessionRequest(
    string MeetingGoal,
    string IntendedAudience,
    int PlannedDurationMinutes,
    string? DemoScenario,
    string ConsentStatus,
    string RetentionPolicy,
    int RetentionDays,
    long? ExpectedVersion = null);

public sealed record TransitionSalesMeetingSessionRequest(
    string TargetStatus,
    long ExpectedVersion,
    int? CurrentSlideIndex = null,
    int? CurrentTalkingPointIndex = null,
    string? ResumeMarker = null,
    string? Reason = null);

public sealed record SalesMeetingSessionResponse(
    Guid Id,
    Guid CompanyId,
    Guid InvitationId,
    Guid LeadId,
    Guid? DealId,
    Guid? ContactId,
    Guid CustomerCompanyId,
    string MeetingGoal,
    string IntendedAudience,
    int PlannedDurationMinutes,
    string? DemoScenario,
    string ProviderMeetingId,
    string Status,
    int CurrentSlideIndex,
    int CurrentTalkingPointIndex,
    string? ResumeMarker,
    string ConsentStatus,
    DateTime? ConsentRecordedUtc,
    Guid? ConsentRecordedByUserId,
    string RetentionPolicy,
    int RetentionDays,
    DateTime RetentionStartsUtc,
    DateTime RetentionUntilUtc,
    string? StatusReason,
    DateTime? EndedUtc,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long ConcurrencyVersion);

public interface ISalesMeetingSessionService
{
    Task<SalesMeetingSessionResponse?> GetByInvitationAsync(
        Guid companyId, Guid invitationId, CancellationToken cancellationToken);

    Task<SalesMeetingSessionResponse?> GetAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken);

    Task<SalesMeetingSessionResponse> CreateOrUpdateAsync(
        Guid companyId,
        Guid actorUserId,
        Guid invitationId,
        CreateOrUpdateSalesMeetingSessionRequest request,
        string? correlationId,
        CancellationToken cancellationToken);

    Task<SalesMeetingSessionResponse?> TransitionAsync(
        Guid companyId,
        Guid actorUserId,
        Guid sessionId,
        TransitionSalesMeetingSessionRequest request,
        string? correlationId,
        CancellationToken cancellationToken);
}
