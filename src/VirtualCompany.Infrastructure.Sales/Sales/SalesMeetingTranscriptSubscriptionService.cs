using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingTranscriptSubscriptionService(
    VirtualCompanyDbContext db,
    ICalendarOAuthAccessTokenLeaseService tokenLeases,
    IMeetingTranscriptProviderAdapter provider,
    IOptions<SalesMeetingTranscriptOptions> options,
    ICompanyExecutionScopeFactory executionScopes,
    TimeProvider timeProvider) : ISalesMeetingTranscriptSubscriptionService
{
    public async Task<SalesMeetingTranscriptSubscriptionDto?> EnsureAsync(Guid companyId, Guid userId, Guid sessionId,
        CreateSalesMeetingTranscriptSubscriptionRequest request, string? correlationId,
        CancellationToken cancellationToken)
    {
        EnsureIds(companyId, userId, sessionId);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        EnsureConsentAndRetention(session);
        var invitation = await db.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == session.InvitationId, cancellationToken);
        EnsureMicrosoftTeams(invitation);
        var connectionId = request.CalendarConnectionId ?? invitation.CalendarConnectionId;
        if (connectionId != invitation.CalendarConnectionId)
            throw Policy(SalesMeetingTranscriptProblemCodes.Conflict, "The transcript connection must match the meeting invitation connection.");
        var existing = await db.SalesMeetingTranscriptSubscriptions
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId &&
                        x.Status == SalesMeetingTranscriptSubscriptionStatus.Active && x.ExpiresUtc > UtcNow())
            .OrderByDescending(x => x.ExpiresUtc).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return ToDto(existing);

        var settings = ValidateSettings();
        var lease = await AcquireAsync(companyId, connectionId, cancellationToken);
        var context = new MeetingTranscriptProviderContext(lease.AccessToken, invitation.OrganizerEmail, invitation.StartsUtc);
        var meeting = await provider.ResolveMeetingAsync(context, invitation.OnlineMeetingUrl!, cancellationToken);
        var expires = SubscriptionExpiry(session, settings);
        var created = await provider.CreateSubscriptionAsync(context, meeting.OnlineMeetingId,
            new Uri(settings.NotificationUrl), new Uri(settings.LifecycleNotificationUrl), settings.ClientState,
            expires, cancellationToken);
        var now = UtcNow();
        var subscription = new SalesMeetingTranscriptSubscription(Guid.NewGuid(), companyId, sessionId,
            connectionId, session.ProviderMeetingId, meeting.OnlineMeetingId, created.SubscriptionId,
            created.Resource, Sha256(settings.ClientState), created.ExpiresUtc, session.RetentionUntilUtc, userId, now);
        db.SalesMeetingTranscriptSubscriptions.Add(subscription);
        AddAudit(companyId, userId, AuditEventActions.SalesMeetingTranscriptSubscriptionCreated, subscription.Id,
            "A Microsoft Graph transcript notification subscription was created after consent and retention checks.", correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(subscription);
    }

    public async Task<SalesMeetingTranscriptSubscriptionDto?> RenewAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid subscriptionId, string? correlationId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, userId, sessionId, subscriptionId);
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var subscription = await db.SalesMeetingTranscriptSubscriptions.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == subscriptionId, cancellationToken);
        if (subscription is null) return null;
        await RenewOneAsync(subscription, userId, correlationId, true, cancellationToken);
        return ToDto(subscription);
    }

    public async Task<int> RenewDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return 0;
        var now = UtcNow();
        await PurgeExpiredAsync(now, cancellationToken);
        var due = await db.SalesMeetingTranscriptSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == SalesMeetingTranscriptSubscriptionStatus.Active ||
                         x.Status == SalesMeetingTranscriptSubscriptionStatus.RenewalRequired) &&
                        x.RetentionUntilUtc > now && x.ExpiresUtc <= now.AddMinutes(Math.Max(30, settings.RenewalLeadMinutes)))
            .OrderBy(x => x.ExpiresUtc).Select(x => new { x.CompanyId, x.Id }).Take(50).ToListAsync(cancellationToken);
        var renewed = 0;
        foreach (var candidate in due)
        {
            using var tenantScope = executionScopes.BeginScope(candidate.CompanyId);
            var subscription = await db.SalesMeetingTranscriptSubscriptions.SingleOrDefaultAsync(
                x => x.CompanyId == candidate.CompanyId && x.Id == candidate.Id, cancellationToken);
            if (subscription is null) continue;
            if (await RenewOneAsync(subscription, subscription.CreatedByUserId, null, false, cancellationToken)) renewed++;
        }
        return renewed;
    }

    private async Task<bool> RenewOneAsync(SalesMeetingTranscriptSubscription subscription, Guid actorUserId,
        string? correlationId, bool throwOnFailure, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var session = await db.SalesMeetingSessions.SingleAsync(x => x.CompanyId == subscription.CompanyId && x.Id == subscription.SessionId, cancellationToken);
        try
        {
            EnsureConsentAndRetention(session);
            if (subscription.ExpiresUtc <= now) subscription.MarkExpired(now);
            var invitation = await db.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == subscription.CompanyId && x.Id == session.InvitationId, cancellationToken);
            var lease = await AcquireAsync(subscription.CompanyId, subscription.CalendarConnectionId, cancellationToken);
            var context = new MeetingTranscriptProviderContext(lease.AccessToken, invitation.OrganizerEmail, invitation.StartsUtc);
            var renewed = await provider.RenewSubscriptionAsync(context, subscription.ProviderSubscriptionId,
                subscription.ProviderResource, SubscriptionExpiry(session, ValidateSettings()), cancellationToken);
            subscription.Renew(renewed.SubscriptionId, renewed.ExpiresUtc, now);
            AddAudit(subscription.CompanyId, actorUserId, AuditEventActions.SalesMeetingTranscriptSubscriptionRenewed,
                subscription.Id, "The Microsoft Graph transcript subscription was renewed.", correlationId);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (MeetingTranscriptProviderException e)
        {
            if (e.Kind == MeetingTranscriptProviderFailureKind.AuthenticationRequired)
                subscription.MarkPermissionRequired(e.Code, e.Message, now);
            else if (e.Kind == MeetingTranscriptProviderFailureKind.Retryable)
                subscription.MarkRenewalRequired(e.Code, e.Message, now);
            else subscription.MarkFailed(e.Code, e.Message, now);
            await db.SaveChangesAsync(cancellationToken);
            if (throwOnFailure) throw Policy(
                e.Kind == MeetingTranscriptProviderFailureKind.AuthenticationRequired
                    ? SalesMeetingTranscriptProblemCodes.ProviderPermissionRequired
                    : SalesMeetingTranscriptProblemCodes.Conflict, e.Message);
            return false;
        }
        catch (SalesMeetingTranscriptPolicyException)
        {
            subscription.Disable("Consent or retention policy no longer permits transcript retrieval.", now);
            await db.SaveChangesAsync(cancellationToken);
            if (throwOnFailure) throw;
            return false;
        }
    }

    private async Task PurgeExpiredAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expiredSessionIds = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RetentionUntilUtc <= now || x.ConsentStatus == SalesMeetingConsentStatus.Revoked || x.ConsentStatus == SalesMeetingConsentStatus.Denied)
            .Select(x => new { x.CompanyId, x.Id }).Take(50).ToListAsync(cancellationToken);
        foreach (var sessionKey in expiredSessionIds)
        {
            var provenance = await db.SalesMeetingTranscriptProvenance.IgnoreQueryFilters()
                .Where(x => x.CompanyId == sessionKey.CompanyId && x.SessionId == sessionKey.Id).ToListAsync(cancellationToken);
            var providerSegmentIds = provenance.Select(x => x.TranscriptSegmentId).Distinct().ToArray();
            db.SalesMeetingTranscriptProvenance.RemoveRange(provenance);
            db.SalesMeetingProviderTranscripts.RemoveRange(await db.SalesMeetingProviderTranscripts.IgnoreQueryFilters().Where(x => x.CompanyId == sessionKey.CompanyId && x.SessionId == sessionKey.Id).ToListAsync(cancellationToken));
            db.SalesMeetingTranscriptIngestions.RemoveRange(await db.SalesMeetingTranscriptIngestions.IgnoreQueryFilters().Where(x => x.CompanyId == sessionKey.CompanyId && x.SessionId == sessionKey.Id).ToListAsync(cancellationToken));
            db.SalesMeetingTranscriptSubscriptions.RemoveRange(await db.SalesMeetingTranscriptSubscriptions.IgnoreQueryFilters().Where(x => x.CompanyId == sessionKey.CompanyId && x.SessionId == sessionKey.Id).ToListAsync(cancellationToken));
            if (providerSegmentIds.Length > 0)
                db.SalesMeetingTranscriptSegments.RemoveRange(await db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().Where(x => x.CompanyId == sessionKey.CompanyId && providerSegmentIds.Contains(x.Id) && x.InputSource == SalesMeetingInputSource.TranscriptAdapter).ToListAsync(cancellationToken));
        }
        if (expiredSessionIds.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CalendarOAuthAccessTokenLease> AcquireAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        try { return await tokenLeases.AcquireAsync(companyId, connectionId, provider.RequiredScopes, cancellationToken); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException)
        {
            throw new MeetingTranscriptProviderException("graph_transcript_permission_required",
                "Reconnect Microsoft 365 and grant online-meeting transcript permissions.",
                MeetingTranscriptProviderFailureKind.AuthenticationRequired, innerException: e);
        }
    }

    private SalesMeetingTranscriptOptions ValidateSettings()
    {
        var value = options.Value;
        if (!value.Enabled) throw Policy(SalesMeetingTranscriptProblemCodes.ProviderUnsupported, "Microsoft Graph transcript integration is disabled.");
        if (!Uri.TryCreate(value.NotificationUrl, UriKind.Absolute, out var notification) || notification.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(value.LifecycleNotificationUrl, UriKind.Absolute, out var lifecycle) || lifecycle.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(value.ClientState) || value.ClientState.Length is < 32 or > 128)
            throw Policy(SalesMeetingTranscriptProblemCodes.ProviderUnsupported, "Microsoft Graph transcript webhook configuration is incomplete.");
        return value;
    }

    private static void EnsureMicrosoftTeams(SalesMeetingInvitation invitation)
    {
        if (invitation.Provider != ExternalAccountProvider.Microsoft365 || !invitation.CreateOnlineMeeting || string.IsNullOrWhiteSpace(invitation.OnlineMeetingUrl))
            throw Policy(SalesMeetingTranscriptProblemCodes.ProviderUnsupported, "Transcript reconciliation requires a Microsoft Teams meeting created through Microsoft 365.");
    }

    private void EnsureConsentAndRetention(SalesMeetingSession session)
    {
        if (session.ConsentStatus != SalesMeetingConsentStatus.Granted)
            throw Policy(SalesMeetingTranscriptProblemCodes.ConsentRequired, "Recorded meeting consent is required before transcript subscription or retrieval.");
        if (session.RetentionUntilUtc <= UtcNow())
            throw Policy(SalesMeetingTranscriptProblemCodes.RetentionExpired, "The meeting retention period has expired.");
    }

    private DateTime SubscriptionExpiry(SalesMeetingSession session, SalesMeetingTranscriptOptions settings)
    {
        var proposed = UtcNow().AddHours(Math.Clamp(settings.SubscriptionLifetimeHours, 1, 71));
        var expiry = proposed < session.RetentionUntilUtc ? proposed : session.RetentionUntilUtc;
        if (expiry <= UtcNow().AddMinutes(45)) throw Policy(SalesMeetingTranscriptProblemCodes.RetentionExpired, "The remaining retention period is too short to create a transcript subscription.");
        return expiry;
    }

    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company membership is required.");
    }

    private void AddAudit(Guid companyId, Guid actorUserId, string action, Guid subjectId, string rationale, string? correlationId) =>
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, actorUserId, action,
            "sales_meeting_transcript_subscription", subjectId.ToString("D"), AuditEventOutcomes.Succeeded,
            rationale, ["microsoft graph", "meeting transcript"], new Dictionary<string, string?>(),
            string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim()[..Math.Min(128, correlationId.Trim().Length)], UtcNow()));

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
    private static SalesMeetingTranscriptPolicyException Policy(string code, string message) => new(code, message);
    internal static SalesMeetingTranscriptSubscriptionDto ToDto(SalesMeetingTranscriptSubscription x) =>
        new(x.Id, x.SessionId, x.CalendarConnectionId, "microsoft_graph", x.Status.ToStorageValue(), x.ExpiresUtc,
            x.RetentionUntilUtc, x.LastNotificationUtc, x.LastRenewedUtc, x.RenewalAttemptCount,
            x.AuthenticityFailureCount, x.LastErrorCode, x.LastErrorSummary, x.ConcurrencyVersion);
}

public sealed class SalesMeetingTranscriptSubscriptionBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<SalesMeetingTranscriptOptions> options,
    ILogger<SalesMeetingTranscriptSubscriptionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISalesMeetingTranscriptSubscriptionService>().RenewDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Microsoft Graph transcript subscription maintenance failed."); }
            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(options.Value.RenewalPollMinutes, 5, 180)), stoppingToken);
        }
    }
}
