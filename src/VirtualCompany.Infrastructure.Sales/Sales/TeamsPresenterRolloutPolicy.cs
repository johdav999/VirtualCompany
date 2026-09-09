using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsPresenterRolloutPolicy(
    VirtualCompanyDbContext db,
    IDemoTenantExternalSideEffectPolicy demoPolicy,
    IOptions<TeamsPresenterOptions> configured, TimeProvider? clock = null) : ITeamsPresenterRolloutPolicy
{
    private static readonly string[] ActiveStates =
    [
        TeamsMeetingCallStates.Requested, TeamsMeetingCallStates.Joining,
        TeamsMeetingCallStates.WaitingInLobby, TeamsMeetingCallStates.Admitted,
        TeamsMeetingCallStates.Connected, TeamsMeetingCallStates.LeaveRequested,
        TeamsMeetingCallStates.Ending, TeamsMeetingCallStates.ReconciliationRequired
    ];

    public async Task<TeamsPresenterRolloutDto> EvaluateAsync(
        Guid companyId, Guid? actorUserId, Guid? requestEntraTenantId, CancellationToken ct, Guid? meetingSessionId = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var options = configured.Value;
        var registration = await db.TeamsTenantRegistrations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, ct);
        var demo = await demoPolicy.EvaluateAsync(companyId, "teams_presenter.join", ct);
        var companyAllowed = Contains(options.PilotCompanyIds, companyId) || options.ProductionEnabled;
        var userAllowed = !actorUserId.HasValue ||
            Contains(options.PilotUserIds, actorUserId.Value) || options.ProductionEnabled;
        var tenantAllowed = registration is not null &&
            Contains(options.AllowedTenantIds, registration.EntraTenantId) &&
            (!requestEntraTenantId.HasValue || requestEntraTenantId == registration.EntraTenantId);
        var packageCompatible = IsCompatible(options.InstalledPackageVersion, options.MinimumPackageVersion, options.PackageVersion);
        var activeCalls = await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking().CountAsync(call => ActiveStates.Contains(call.State) && (!meetingSessionId.HasValue || call.CompanyId != companyId || call.MeetingSessionId != meetingSessionId), ct);
        var costWithinLimit = options.MonthlyCostLimit > 0 && options.MonthlyCostUsed < options.MonthlyCostLimit;

        var testCall = meetingSessionId.HasValue ? await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.MeetingSessionId == meetingSessionId && ActiveStates.Contains(c.State), ct) : null;
        var testGrantCurrent = testCall?.FirstUatAuthorizedUtc is null;
        if (testCall?.FirstUatAuthorizedUtc is { } grantTime)
            testGrantCurrent = options.FirstUatEnabled && !options.ProductionEnabled && registration?.FirstUatAuthorizedUtc == grantTime &&
                registration.AllowsFirstUat(meetingSessionId, actorUserId, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        var firstUat = options.FirstUatEnabled && !options.ProductionEnabled &&
            registration?.AllowsFirstUat(meetingSessionId, actorUserId, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime) == true;
        var (allowed, code, message) = Decide(options, registration, demo, companyAllowed,
            userAllowed, tenantAllowed, packageCompatible, costWithinLimit, activeCalls, firstUat, testGrantCurrent);
        return new TeamsPresenterRolloutDto(
            allowed, code, message, options.ProductionEnabled, options.PilotEnabled,
            options.EmergencyDisabled, companyAllowed, userAllowed, tenantAllowed,
            packageCompatible, options.AppInstallationAttested, options.AutomatedEvidenceApproved,
            options.LiveUatApproved, options.PackageVersion, options.MinimumPackageVersion,
            Empty(options.InstalledPackageVersion), activeCalls, options.MaxActiveCallsGlobal,
            options.MonthlyCostUsed, options.MonthlyCostLimit,
            string.IsNullOrWhiteSpace(options.CostCurrency) ? "USD" : options.CostCurrency.Trim(),
            "typed_browser_hosted", Empty(options.LiveUatOwner), options.LiveUatCompletedUtc,
            Empty(options.LiveUatEvidenceReference), options.MediaCertificateExpiresUtc, Empty(options.InstallUrl));
    }

    private static (bool Allowed, string Code, string Message) Decide(
        TeamsPresenterOptions options, TeamsTenantRegistration? registration,
        DemoExternalSideEffectDecision demo, bool companyAllowed,
        bool userAllowed, bool tenantAllowed, bool packageCompatible, bool costWithinLimit, int activeCalls, bool firstUat, bool testGrantCurrent)
    {
        if (!options.Enabled) return Block(TeamsPresenterReadinessReasonCodes.Disabled,
            "Alex for Teams is disabled. Use the typed meeting workflow until an administrator enables it.");
        if (options.EmergencyDisabled) return Block(TeamsPresenterReadinessReasonCodes.EmergencyDisabled,
            "The emergency stop is active. Alex cannot join or use meeting media; typed controls remain available.");
        if (!demo.Allowed) return Block(TeamsPresenterReadinessReasonCodes.DemoTenantBlocked,
            "Teams side effects are blocked for synthetic demo companies.");
        if (registration?.Status != TeamsTenantRegistrationStates.Ready)
            return Block(TeamsPresenterReadinessReasonCodes.TenantApprovalNotVerified,
                "Tenant consent, exact permissions, and Teams policy evidence must all be verified.");
        if (!tenantAllowed) return Block(TeamsPresenterReadinessReasonCodes.TenantMismatch,
            "The signed-in Teams tenant does not match this company's approved tenant or tenant allowlist.");
        if (!options.ProductionEnabled && !options.PilotEnabled)
            return Block(TeamsPresenterReadinessReasonCodes.PilotDisabled,
                "The controlled Teams presenter rollout is disabled. Typed and browser-hosted workflows remain available.");
        if (!companyAllowed) return Block(TeamsPresenterReadinessReasonCodes.CompanyNotAllowed,
            "This company is not included in the approved Teams presenter pilot.");
        if (!userAllowed) return Block(TeamsPresenterReadinessReasonCodes.UserNotAllowed,
            "This organizer is not included in the approved Teams presenter pilot.");
        if (!options.AppInstallationAttested) return Block(TeamsPresenterReadinessReasonCodes.PackageInvalid,
            "A tenant administrator must install the approved Teams app package and record its version.");
        if (!packageCompatible) return Block(TeamsPresenterReadinessReasonCodes.PackageIncompatible,
            "The installed Teams app package is missing or outside the supported version range.");
        if (!options.AutomatedEvidenceApproved) return Block(TeamsPresenterReadinessReasonCodes.AutomatedEvidenceRequired,
            "Automated release evidence has not been approved for this package.");
        if (!testGrantCurrent) return Block(TeamsPresenterReadinessReasonCodes.LiveUatRequired, "The first-test authorization expired, was revoked, or changed. End this test call.");
        if (!options.LiveUatApproved && !firstUat) return Block(TeamsPresenterReadinessReasonCodes.LiveUatRequired,
            "Live restrictive-policy Teams UAT has not been approved for this tenant.");
        if (!costWithinLimit) return Block(TeamsPresenterReadinessReasonCodes.CostLimitReached,
            "The monthly Teams presenter cost limit is missing or exhausted. Update verified billing usage or use the typed fallback.");
        if (activeCalls >= options.MaxActiveCallsGlobal)
            return Block(TeamsPresenterReadinessReasonCodes.GlobalCapacityReached,
                "The global Teams meeting limit has been reached. Retry after another meeting ends.");
        if (firstUat && !options.LiveUatApproved && activeCalls > 0)
            return Block(TeamsPresenterReadinessReasonCodes.GlobalCapacityReached, "The first live test permits one active meeting only.");
        return firstUat && !options.LiveUatApproved
            ? (true, "teams_presenter.first_uat_allowed", "Time-bounded first live test authorized for this meeting; production approval is unchanged.") : Ready();
    }

    private static (bool, string, string) Ready() =>
        (true, TeamsPresenterReadinessReasonCodes.Ready, "All controlled-rollout gates are satisfied for this organizer and tenant.");
    private static (bool, string, string) Block(string code, string message) => (false, code, message);
    private static bool Contains(IEnumerable<string>? values, Guid id) =>
        (values ?? []).Any(value => Guid.TryParse(value, out var parsed) && parsed == id);
    private static bool IsCompatible(string? installed, string? minimum, string? package)
    {
        if (!Version.TryParse(installed, out var installedVersion) ||
            !Version.TryParse(minimum, out var minimumVersion) ||
            !Version.TryParse(package, out var packageVersion)) return false;
        return installedVersion >= minimumVersion && installedVersion <= packageVersion;
    }
    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
