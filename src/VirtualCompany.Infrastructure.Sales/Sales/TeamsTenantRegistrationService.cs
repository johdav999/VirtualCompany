using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsTenantRegistrationService(
    VirtualCompanyDbContext db,
    IOptions<TeamsPresenterOptions> configured,
    ITeamsAppOnlyTokenProvider tokenProvider,
    ICompanyOutboxEnqueuer outbox,
    IAuditEventWriter audit,
    TimeProvider timeProvider,
    ILogger<TeamsTenantRegistrationService> logger) : ITeamsTenantRegistrationService
{
    public async Task<TeamsTenantRegistrationDto?> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        var registration = await db.TeamsTenantRegistrations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        return registration is null ? null : Map(registration);
    }

    public async Task<TeamsTenantRegistrationDto> AssociateAsync(
        AssociateTeamsTenantCommand command,
        CancellationToken cancellationToken)
    {
        EnsureId(command.CompanyId, nameof(command.CompanyId));
        EnsureId(command.EntraTenantId, nameof(command.EntraTenantId));
        EnsureId(command.ActorUserId, nameof(command.ActorUserId));
        var options = configured.Value;
        EnsureTenantAllowed(command.EntraTenantId, options);
        if (!options.MediaRouteApproved || options.MediaRoute == "disabled" ||
            !string.Equals(command.ApprovedMediaRoute, options.MediaRoute, StringComparison.Ordinal))
        {
            throw new TeamsIdentityException(
                TeamsPresenterReadinessReasonCodes.MediaRouteNotApproved,
                "The tenant can only be associated with the explicitly approved configured media route.");
        }

        var company = await db.Companies.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.CompanyId, cancellationToken)
            ?? throw new KeyNotFoundException("The company was not found.");
        if (company.IsDemoTenant)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.TenantDisabled, "External Teams integration is disabled for demo companies.");

        var existingCompanyRegistration = await db.TeamsTenantRegistrations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.CompanyId == command.CompanyId, cancellationToken);
        if (existingCompanyRegistration is not null)
        {
            if (existingCompanyRegistration.EntraTenantId != command.EntraTenantId)
                throw new TeamsIdentityException(
                    TeamsIdentityFailureCodes.TenantAlreadyAssociated,
                    "The company already has a different Entra tenant association. It cannot be silently rebound.");
            return Map(existingCompanyRegistration);
        }

        var existingTenantRegistration = await db.TeamsTenantRegistrations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.EntraTenantId == command.EntraTenantId, cancellationToken);
        if (existingTenantRegistration is not null)
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.TenantAlreadyAssociated,
                "The Entra tenant is already associated with another company.");

        if (!Guid.TryParse(options.TeamsAppId, out var teamsAppId) || !Guid.TryParse(options.BotApplicationId, out var botApplicationId))
            throw new TeamsIdentityException(TeamsPresenterReadinessReasonCodes.IdentityInvalid, "The Teams application identity is invalid.");

        var requiredPermissions = RequiredPermissions(options);
        if (requiredPermissions.Count == 0)
            throw new TeamsIdentityException(TeamsPresenterReadinessReasonCodes.PermissionsNotVerified, "Call control must be configured before tenant consent is requested.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var registration = new TeamsTenantRegistration(
            Guid.NewGuid(), command.CompanyId, command.EntraTenantId, teamsAppId, botApplicationId,
            options.MediaRoute, SerializePermissions(requiredPermissions), command.ActorUserId, now);
        db.TeamsTenantRegistrations.Add(registration);
        await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_tenant.associated", AuditEventOutcomes.Succeeded,
            "A platform administrator associated the company with an allowed Entra tenant for Teams consent.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(registration);
    }

    public async Task<TeamsAdminConsentStartResult> StartAdminConsentAsync(
        StartTeamsAdminConsentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureId(command.CompanyId, nameof(command.CompanyId));
        EnsureId(command.ActorUserId, nameof(command.ActorUserId));
        var registration = await RequiredRegistrationAsync(command.CompanyId, cancellationToken);
        if (registration.Status is TeamsTenantRegistrationStates.Disabled or TeamsTenantRegistrationStates.Revoked)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.TenantDisabled, "The Teams tenant registration is disabled.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(configured.Value.ConsentStateLifetimeMinutes);
        var state = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var session = new TeamsAdminConsentSession(
            Guid.NewGuid(), registration.CompanyId, registration.Id, command.ActorUserId, Hash(state), expires, now);
        db.TeamsAdminConsentSessions.Add(session);
        await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_admin_consent.started", AuditEventOutcomes.Started,
            "A platform administrator started tenant-wide Teams application consent.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = configured.Value.BotApplicationId,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["redirect_uri"] = configured.Value.AdminConsentRedirectUrl,
            ["state"] = state
        };
        var url = QueryHelpers.AddQueryString(
            $"https://login.microsoftonline.com/{registration.EntraTenantId:D}/v2.0/adminconsent",
            query);
        return new TeamsAdminConsentStartResult(new Uri(url), expires);
    }

    public async Task<TeamsTenantRegistrationDto> CompleteAdminConsentAsync(
        CompleteTeamsAdminConsentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureId(command.ActorUserId, nameof(command.ActorUserId));
        if (string.IsNullOrWhiteSpace(command.State))
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentStateInvalid, "The consent state is invalid.");

        var stateHash = Hash(command.State);
        var session = await db.TeamsAdminConsentSessions.IgnoreQueryFilters()
            .Include(item => item.Registration)
            .SingleOrDefaultAsync(item => item.StateHash == stateHash, cancellationToken)
            ?? throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentStateInvalid, "The consent state is invalid.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (session.ConsumedUtc.HasValue)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentStateReplayed, "The consent state has already been used.");
        if (session.ExpiresUtc <= now)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentStateExpired, "The consent state has expired.");
        if (session.ActorUserId != command.ActorUserId)
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentStateInvalid, "The consent state belongs to a different administrator.");

        session.Consume(now);
        await SaveAsync(cancellationToken);
        var registration = session.Registration;

        if (!string.IsNullOrWhiteSpace(command.Error) || !string.Equals(command.AdminConsent, "true", StringComparison.OrdinalIgnoreCase))
        {
            registration.RecordConsentFailure(TeamsIdentityFailureCodes.ConsentDenied, TeamsTenantVerificationStates.Pending, now);
            await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_admin_consent.denied", AuditEventOutcomes.Blocked,
                "Tenant-wide Teams application consent was not granted.", command.CorrelationId, cancellationToken);
            await SaveAsync(cancellationToken);
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentDenied, "Tenant administrator consent was not granted.");
        }

        if (!Guid.TryParse(command.Tenant, out var returnedTenant) || returnedTenant != registration.EntraTenantId)
        {
            await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_admin_consent.tenant_mismatch", AuditEventOutcomes.Blocked,
                "The admin-consent callback tenant did not match the registered tenant.", command.CorrelationId, cancellationToken);
            await SaveAsync(cancellationToken);
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.ConsentTenantMismatch, "The consent callback tenant did not match the registered tenant.");
        }

        try
        {
            var required = ParsePermissions(registration.RequiredPermissions);
            var token = await tokenProvider.AcquireAsync(
                new TeamsAppOnlyTokenRequest(registration.EntraTenantId, required, ForceRefresh: true),
                cancellationToken);
            registration.RecordConsentVerification(
                TeamsTenantVerificationStates.Verified,
                SerializePermissions(token.GrantedPermissions),
                failureCode: null,
                command.ActorUserId,
                now);
            await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_admin_consent.verified", AuditEventOutcomes.Succeeded,
                "Tenant consent and the exact approved Teams application permission set were verified from a Graph app-only token.", command.CorrelationId, cancellationToken);
            await SaveAsync(cancellationToken);
            return Map(registration);
        }
        catch (TeamsIdentityException exception)
        {
            var permissionStatus = exception.Code == TeamsIdentityFailureCodes.PermissionExcess
                ? TeamsTenantVerificationStates.Excess
                : exception.Code == TeamsIdentityFailureCodes.PermissionMissing
                    ? TeamsTenantVerificationStates.Missing
                    : TeamsTenantVerificationStates.Pending;
            registration.RecordConsentFailure(exception.Code, permissionStatus, now);
            await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_admin_consent.verification_failed", AuditEventOutcomes.Blocked,
                "Tenant consent could not be verified from the configured application identity.", command.CorrelationId, cancellationToken);
            await SaveAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<TeamsTenantRegistrationDto> AttestPolicyAsync(
        AttestTeamsTenantPolicyCommand command,
        CancellationToken cancellationToken)
    {
        EnsureId(command.CompanyId, nameof(command.CompanyId));
        EnsureId(command.ActorUserId, nameof(command.ActorUserId));
        var registration = await RequiredRegistrationAsync(command.CompanyId, cancellationToken);
        registration.AttestPolicy(command.Approved, command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
        await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_tenant_policy.attested",
            command.Approved ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            command.Approved
                ? "A platform administrator attested that the required tenant Teams policies are configured."
                : "A platform administrator revoked the Teams tenant policy attestation.",
            command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(registration);
    }

    public async Task<TeamsTenantRegistrationDto> DisableAsync(
        DisableTeamsTenantCommand command,
        CancellationToken cancellationToken)
    {
        EnsureId(command.CompanyId, nameof(command.CompanyId));
        EnsureId(command.ActorUserId, nameof(command.ActorUserId));
        var registration = await RequiredRegistrationAsync(command.CompanyId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        registration.Disable(command.ConsentRevoked, command.ActorUserId, now);
        var activeCalls = await db.TeamsMeetingCalls.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.RegistrationId == registration.Id &&
                x.State != TeamsMeetingCallStates.Ended && x.State != TeamsMeetingCallStates.Rejected && x.State != TeamsMeetingCallStates.Failed)
            .ToListAsync(cancellationToken);
        foreach (var call in activeCalls)
        {
            var key = call.RequestLeave(now, forced: true);
            outbox.Enqueue(call.CompanyId, CompanyOutboxTopics.TeamsCallControlRequested,
                new TeamsCallControlWorkItem(call.CompanyId, call.Id, call.ActionVersion, "terminate", command.CorrelationId),
                command.CorrelationId, idempotencyKey: key, messageType: nameof(TeamsCallControlWorkItem));
        }
        await WriteAuditAsync(registration, command.ActorUserId, "sales.teams_tenant.disabled", AuditEventOutcomes.Succeeded,
            command.ConsentRevoked
                ? "A platform administrator recorded tenant consent as revoked and disabled new Teams calls."
                : "A platform administrator disabled new Teams calls for the company.",
            command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(registration);
    }

    private async Task<TeamsTenantRegistration> RequiredRegistrationAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.TeamsTenantRegistrations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken)
        ?? throw new KeyNotFoundException("The Teams tenant registration was not found.");

    private async Task WriteAuditAsync(
        TeamsTenantRegistration registration,
        Guid actorUserId,
        string action,
        string outcome,
        string summary,
        string? correlationId,
        CancellationToken cancellationToken) =>
        await audit.WriteAsync(new AuditEventWriteRequest(
            registration.CompanyId,
            AuditActorTypes.Human,
            actorUserId,
            action,
            "teams_tenant_registration",
            registration.Id.ToString("D"),
            outcome,
            summary,
            Metadata: new Dictionary<string, string?>
            {
                ["status"] = registration.Status,
                ["permissionStatus"] = registration.PermissionStatus,
                ["policyStatus"] = registration.PolicyStatus
            },
            CorrelationId: correlationId,
            OccurredUtc: timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new TeamsIdentityException(
                "teams_identity.registration_conflict",
                "The Teams tenant registration changed. Reload it before retrying.",
                false,
                exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is not null)
        {
            logger.LogWarning("Teams tenant registration persistence rejected a conflicting association.");
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.TenantAlreadyAssociated,
                "The company or Entra tenant already has a Teams association.",
                false,
                exception);
        }
    }

    private static IReadOnlyList<string> RequiredPermissions(TeamsPresenterOptions options) =>
        TeamsPresenterApplicationPermissions.RequiredFor(
            options.MediaRoute,
            options.CallControlEnabled,
            options.AudioEnabled,
            options.VisualMediaEnabled);

    private static void EnsureTenantAllowed(Guid tenantId, TeamsPresenterOptions options)
    {
        if (!(options.AllowedTenantIds ?? []).Any(value => Guid.TryParse(value, out var allowed) && allowed == tenantId))
            throw new TeamsIdentityException(TeamsIdentityFailureCodes.TenantNotAssociated, "The Entra tenant is not in the configured allowlist.");
    }

    private static TeamsTenantRegistrationDto Map(TeamsTenantRegistration registration) => new(
        registration.Id,
        registration.CompanyId,
        registration.Status,
        registration.ConsentStatus,
        registration.PermissionStatus,
        registration.PolicyStatus,
        registration.ApprovedMediaRoute,
        ParsePermissions(registration.RequiredPermissions),
        ParsePermissions(registration.GrantedPermissions),
        registration.FailureCode,
        registration.ConsentVerifiedUtc,
        registration.PermissionsVerifiedUtc,
        registration.PolicyApprovedUtc,
        registration.UpdatedUtc,
        registration.ConcurrencyVersion,
        registration.EntraTenantId,
        registration.TeamsAppId,
        registration.BotApplicationId);

    private static string SerializePermissions(IEnumerable<string> permissions) =>
        string.Join(';', permissions.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));

    private static IReadOnlyList<string> ParsePermissions(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static void EnsureId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }
}
