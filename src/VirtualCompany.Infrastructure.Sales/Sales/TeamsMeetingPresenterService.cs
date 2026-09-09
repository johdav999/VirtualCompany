using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsMeetingPresenterService(VirtualCompanyDbContext db, IAgentCapabilityCatalog catalog,
    IAgentCommunicationProfileResolver profiles, IAuditEventWriter audit, TimeProvider clock) : ITeamsMeetingPresenterService
{
    public async Task<TeamsMeetingPresenterDto> GetAsync(Guid companyId, Guid userId, Guid meetingId, CancellationToken ct)
    {
        var meeting = await OrganizerAsync(companyId, userId, meetingId, ct);
        var candidates = await db.Agents.AsNoTracking().Where(a => a.CompanyId == companyId &&
            a.Status == AgentStatus.Active && (a.Department == "Sales" || a.Department == "Marketing"))
            .OrderBy(a => a.DisplayName).ToListAsync(ct);
        var choices = new List<TeamsPresenterChoice>();
        foreach (var agent in candidates)
            if ((await PermittedToolsAsync(companyId, agent.Id, ct)).Count > 0)
                choices.Add(new(agent.Id, agent.DisplayName, agent.Department));
        return new(meeting.PresenterAgentId, meeting.ConcurrencyVersion, choices);
    }
    public async Task<TeamsMeetingPresenterDto> SelectAsync(Guid companyId, Guid userId, Guid meetingId,
        SelectTeamsMeetingPresenter request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var meeting = await OrganizerAsync(companyId, userId, meetingId, ct);
        if (meeting.ConcurrencyVersion != request.ExpectedVersion) throw Conflict("The meeting changed. Refresh before selecting its presenter.");
        if (await db.TeamsMeetingCalls.AnyAsync(c => c.CompanyId == companyId && c.MeetingSessionId == meetingId &&
            c.State != TeamsMeetingCallStates.Ended && c.State != TeamsMeetingCallStates.Failed && c.State != TeamsMeetingCallStates.Rejected, ct))
            throw Conflict("End the current Teams call before changing its presenter.");
        await RequireAgentAsync(companyId, request.AgentId, ct);
        if ((await PermittedToolsAsync(companyId, request.AgentId, ct)).Count == 0) throw Conflict("This assistant has no permitted meeting tools.");
        try { meeting.SelectPresenter(request.AgentId, userId, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw Conflict(e.Message); }
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId,
            "teams.presenter.selected", "sales_meeting_session", meetingId.ToString("D"), AuditEventOutcomes.Succeeded,
            "The organizer selected the meeting presenter.", Metadata: new Dictionary<string,string?> { ["agentId"] = request.AgentId.ToString("D") }), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(companyId, userId, meetingId, ct);
    }
    public async Task<TeamsPresenterRuntime> ResolveAsync(Guid companyId, Guid meetingId, CancellationToken ct)
    {
        var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == companyId && m.Id == meetingId, ct);
        if (!meeting.PresenterAgentId.HasValue) throw Conflict("Select a meeting presenter before requesting a Teams call.");
        var agent = await RequireAgentAsync(companyId, meeting.PresenterAgentId.Value, ct);
        var tools = await PermittedToolsAsync(companyId, agent.Id, ct);
        if (tools.Count == 0) throw Conflict("The presenter no longer has permission to use meeting tools.");
        var profile = profiles.Resolve(agent.CommunicationProfile, new(companyId, agent.Id, "teams_meeting", null));
        var instructions = $"You are {agent.DisplayName}, the company's {agent.RoleName} in {agent.Department}. Identify yourself as an AI assistant.\n" +
            $"Role brief: {agent.RoleBrief}\nAgent identity profile\nTone: {profile.Tone}\nPersona: {profile.Persona}\n" +
            string.Join("\n", profile.StyleDirectives.Concat(profile.CommunicationRules)) +
            "\nAvoid: " + string.Join(", ", profile.ForbiddenToneRules) +
            "\nPresent only the approved meeting deck and supplied evidence. Use only the provided tools. Never invent facts, commitments, or tool calls. " +
            "Stop speaking immediately when interrupted. Wait for stage render acknowledgement before narrating. Human control and approval always prevail. " +
            "If a question cannot be answered from the visible slide and permitted evidence tools, say that it needs organizer follow-up.";
        if (instructions.Length > 16000) throw Conflict("The presenter communication profile is too large for a meeting session.");
        return new(agent.Id, instructions, tools);
    }
    private async Task<Agent> RequireAgentAsync(Guid companyId, Guid agentId, CancellationToken ct) =>
        await db.Agents.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId && a.Id == agentId &&
            a.Status == AgentStatus.Active && (a.Department == "Sales" || a.Department == "Marketing"), ct)
        ?? throw Conflict("Select an active, permitted Sales or Marketing assistant in this company.");
    private async Task<IReadOnlyList<RealtimeAgentToolDefinition>> PermittedToolsAsync(Guid companyId, Guid agentId, CancellationToken ct)
    {
        var effective = await catalog.GetEffectiveCatalogAsync(companyId, agentId, ct);
        var allowed = effective.EffectiveTools.Where(t => t.State == AgentCapabilityStates.Available).Select(t => t.ToolName).ToHashSet(StringComparer.Ordinal);
        if (effective.Capabilities.Any(c => c.Id == AgentCapabilityIds.SalesMeetingQuestionAnswering && c.State == AgentCapabilityStates.Available))
            allowed.Add(SalesMeetingRealtimeToolNames.AskGroundedQuestion);
        return SalesMeetingRealtimeService.Tools().Where(t => allowed.Contains(t.Name)).ToArray();
    }
    private async Task<SalesMeetingSession> OrganizerAsync(Guid companyId, Guid userId, Guid meetingId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(m => m.CompanyId == companyId && m.UserId == userId &&
            m.Status == CompanyMembershipStatus.Active, ct)) throw new UnauthorizedAccessException("Active company membership is required.");
        var meeting = await db.SalesMeetingSessions.SingleOrDefaultAsync(m => m.CompanyId == companyId && m.Id == meetingId, ct)
            ?? throw new KeyNotFoundException("Meeting not found.");
        var invitation = await db.SalesMeetingInvitations.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.Id == meeting.InvitationId, ct);
        if (meeting.CreatedByUserId != userId || invitation.CreatedByUserId != userId) throw new UnauthorizedAccessException("Only the meeting organizer can select the presenter.");
        return meeting;
    }
    private static TeamsCallControlException Conflict(string message) => new(TeamsCallControlProblemCodes.NotReady, message);
}
