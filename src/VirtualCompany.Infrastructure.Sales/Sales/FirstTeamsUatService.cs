using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Infrastructure.Sales;
// Only exposed by the platform-administrator controller; never an organizer self-service grant.
internal sealed class FirstTeamsUatService(VirtualCompanyDbContext db, IOptions<TeamsPresenterOptions> configured,
    IDemoTenantExternalSideEffectPolicy demo, IAuditEventWriter audit, TimeProvider clock,
    ITeamsMediaHostDrainExecutor drain, ICurrentUserAccessor currentUser) : IFirstTeamsUatService
{
    public async Task<FirstTeamsUatDto> GetAsync(Guid companyId, CancellationToken ct) => Map(await RegistrationAsync(companyId, ct));
    public async Task<FirstTeamsUatDto> AuthorizeAsync(Guid companyId, Guid administratorId, FirstTeamsUatRequest request, CancellationToken ct)
    {
        RequireAdministrator(administratorId);
        var o = configured.Value;
        if (!o.FirstUatEnabled || o.ProductionEnabled || o.EmergencyDisabled) throw Denied("First live testing is disabled or production is enabled.");
        if (!(await demo.EvaluateAsync(companyId, "teams_presenter.join", ct)).Allowed) throw Denied("Demo companies cannot make live calls.");
        var r = await RegistrationAsync(companyId, ct);
        if (r.ConcurrencyVersion != request.ExpectedVersion) throw Denied("Registration changed. Refresh before authorizing a test.");
        if (!o.AllowedTenantIds.Contains(r.EntraTenantId.ToString("D"), StringComparer.OrdinalIgnoreCase) ||
            !o.PilotCompanyIds.Contains(companyId.ToString("D"), StringComparer.OrdinalIgnoreCase) ||
            !o.PilotUserIds.Contains(request.OrganizerUserId.ToString("D"), StringComparer.OrdinalIgnoreCase)) throw Denied("The tenant, company and organizer must be allowlisted.");
        var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleOrDefaultAsync(m =>
            m.CompanyId == companyId && m.Id == request.MeetingSessionId && m.CreatedByUserId == request.OrganizerUserId, ct);
        if (meeting is null || !await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(m =>
            m.CompanyId == companyId && m.UserId == request.OrganizerUserId && m.Status == CompanyMembershipStatus.Active, ct))
            throw Denied("Choose an active company organizer and their meeting.");
        if (await db.TeamsMeetingCalls.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == companyId &&
            c.State != TeamsMeetingCallStates.Ended && c.State != TeamsMeetingCallStates.Failed && c.State != TeamsMeetingCallStates.Rejected, ct))
            throw Denied("End existing company calls before authorizing the first test.");
        try { r.AuthorizeFirstUat(request.MeetingSessionId, request.OrganizerUserId, administratorId, clock.GetUtcNow().UtcDateTime, request.DurationMinutes, request.Reason); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { throw Denied(e.Message); }
        await WriteAsync(r, administratorId, "teams.first_uat.authorized", ct);
        await db.SaveChangesAsync(ct);
        return Map(r);
    }
    public async Task<FirstTeamsUatDto> RevokeAsync(Guid companyId, Guid administratorId, long expectedVersion, CancellationToken ct)
    {
        RequireAdministrator(administratorId);
        var r = await RegistrationAsync(companyId, ct);
        if (r.ConcurrencyVersion != expectedVersion) throw Denied("Registration changed. Refresh before revoking the test.");
        r.RevokeFirstUat(clock.GetUtcNow().UtcDateTime);
        await WriteAsync(r, administratorId, "teams.first_uat.revoked", ct);
        await db.SaveChangesAsync(ct);
        if (r.FirstUatMeetingId.HasValue)
        {
            var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.CompanyId == companyId && c.MeetingSessionId == r.FirstUatMeetingId, ct);
            if (call is not null) await drain.ForceTerminateCallAsync(companyId, call.Id, "first_uat_revoked", ct);
        }
        return Map(r);
    }
    private async Task<TeamsTenantRegistration> RegistrationAsync(Guid companyId, CancellationToken ct) =>
        await db.TeamsTenantRegistrations.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.CompanyId == companyId, ct)
        ?? throw new KeyNotFoundException("Teams registration not found.");
    private Task WriteAsync(TeamsTenantRegistration r, Guid actor, string action, CancellationToken ct) =>
        audit.WriteAsync(new AuditEventWriteRequest(r.CompanyId, AuditActorTypes.Human, actor, action,
            "teams_tenant_registration", r.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            "First live test authorization changed; release approval was not changed.",
            Metadata: new Dictionary<string,string?> { ["meetingId"] = r.FirstUatMeetingId?.ToString("D"),
                ["organizerId"] = r.FirstUatOrganizerId?.ToString("D"), ["expiresUtc"] = r.FirstUatExpiresUtc?.ToString("O"), ["reason"] = r.FirstUatReason }), ct);
    private void RequireAdministrator(Guid actorId)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId != actorId ||
            !currentUser.Principal.HasClaim(CurrentUserClaimTypes.PlatformRole, "platform_admin"))
            throw new UnauthorizedAccessException("Platform administrator access is required.");
    }
    private static FirstTeamsUatDto Map(TeamsTenantRegistration r) => new(r.FirstUatOrganizerId, r.FirstUatMeetingId, r.FirstUatExpiresUtc, r.FirstUatReason, r.ConcurrencyVersion);
    private static TeamsCallControlException Denied(string message) => new(TeamsPresenterReadinessReasonCodes.LiveUatRequired, message);
}
