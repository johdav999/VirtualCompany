namespace VirtualCompany.Application.Sales;

public sealed record TeamsOrganizerGuidanceDto(
    string Code, string Title, string Message, IReadOnlyList<string> Steps,
    bool RequiresNativeTeamsAction);

public sealed record TeamsOrganizerPresenterStateDto(
    Guid CompanyId, Guid MeetingSessionId, bool IsOrganizer,
    string ConsentStatus, DateTime? ConsentRecordedUtc, DateTime RetentionUntilUtc,
    string PresentationControlMode, Guid ControlModeUpdatedByUserId, DateTime ControlModeUpdatedUtc,
    string? MeetingJoinUrl, TeamsMeetingCallDto? Call,
    TeamsPresenterReadinessDto Readiness, TeamsPresenterRolloutDto Rollout,
    TeamsOrganizerGuidanceDto Guidance,
    string AttendeeDisclosure, string ConsentDisclosure);

public interface ITeamsOrganizerExperienceService
{
    Task<TeamsOrganizerPresenterStateDto> GetAsync(Guid companyId, Guid actorUserId,
        Guid meetingSessionId, Guid? requestEntraTenantId, CancellationToken cancellationToken);
}
