using VirtualCompany.Application.Agents;
namespace VirtualCompany.Application.Sales;
public sealed record TeamsPresenterChoice(Guid Id, string Name, string Department);
public sealed record TeamsMeetingPresenterDto(Guid? AgentId, long Version, IReadOnlyList<TeamsPresenterChoice> Choices);
public sealed record SelectTeamsMeetingPresenter(Guid AgentId, long ExpectedVersion);
public sealed record TeamsPresenterRuntime(Guid AgentId, string Instructions, IReadOnlyList<RealtimeAgentToolDefinition> Tools);
public interface ITeamsMeetingPresenterService
{
    Task<TeamsMeetingPresenterDto> GetAsync(Guid companyId, Guid userId, Guid meetingId, CancellationToken ct);
    Task<TeamsMeetingPresenterDto> SelectAsync(Guid companyId, Guid userId, Guid meetingId, SelectTeamsMeetingPresenter request, CancellationToken ct);
    Task<TeamsPresenterRuntime> ResolveAsync(Guid companyId, Guid meetingId, CancellationToken ct);
}
public sealed record FirstTeamsUatRequest(Guid OrganizerUserId, Guid MeetingSessionId, int DurationMinutes, string Reason, long ExpectedVersion);
public sealed record FirstTeamsUatDto(Guid? OrganizerUserId, Guid? MeetingSessionId, DateTime? ExpiresUtc, string? Reason, long Version);
public interface IFirstTeamsUatService
{
    Task<FirstTeamsUatDto> GetAsync(Guid companyId, CancellationToken ct);
    Task<FirstTeamsUatDto> AuthorizeAsync(Guid companyId, Guid administratorId, FirstTeamsUatRequest request, CancellationToken ct);
    Task<FirstTeamsUatDto> RevokeAsync(Guid companyId, Guid administratorId, long expectedVersion, CancellationToken ct);
}
