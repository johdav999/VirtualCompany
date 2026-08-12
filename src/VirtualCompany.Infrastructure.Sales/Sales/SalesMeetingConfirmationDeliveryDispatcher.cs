using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingConfirmationDeliveryDispatcher : ISalesMeetingConfirmationDeliveryDispatcher
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly IMailboxProviderRegistry _mailboxProviderRegistry;

    public SalesMeetingConfirmationDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        IMailboxOAuthAccessTokenLeaseService tokenLeaseService,
        IMailboxProviderRegistry mailboxProviderRegistry)
    {
        _dbContext = dbContext;
        _tokenLeaseService = tokenLeaseService;
        _mailboxProviderRegistry = mailboxProviderRegistry;
    }

    public async Task DispatchAsync(
        SalesMeetingConfirmationDeliveryRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.SalesMeetingInvitations
            .SingleOrDefaultAsync(
                x => x.CompanyId == message.CompanyId && x.Id == message.InvitationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Meeting confirmation target was not found.");

        if (!string.Equals(invitation.ConfirmationIdempotencyKey, message.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Meeting confirmation idempotency key does not match.");
        if (invitation.ConfirmationStatus == SalesMeetingConfirmationStatus.Sent) return;
        if (invitation.Status != SalesMeetingInvitationStatus.Scheduled)
        {
            await MarkUnavailableAsync(
                invitation, message.CorrelationId,
                "The meeting is no longer scheduled, so no confirmation reply was sent.",
                cancellationToken);
            return;
        }

        var source = await ResolveSourceAsync(invitation, cancellationToken);
        if (source is null)
        {
            await MarkUnavailableAsync(
                invitation, message.CorrelationId,
                "No originating sales email thread is available for this meeting.",
                cancellationToken);
            return;
        }

        try
        {
            var mailboxProvider = _mailboxProviderRegistry.Resolve(source.Connection.Provider);
            var lease = await _tokenLeaseService.AcquireAsync(
                invitation.CompanyId,
                source.Connection.Id,
                mailboxProvider.ReplyRequiredScopes,
                cancellationToken);
            invitation.BeginConfirmationDelivery();
            await _dbContext.SaveChangesAsync(cancellationToken);

            var original = await mailboxProvider.GetMessageAsync(
                lease.AccessToken,
                new MailboxMessageFetchRequest(source.Link.ExternalMessageId),
                cancellationToken);
            var result = await mailboxProvider.SendReplyAsync(
                lease.AccessToken,
                new MailboxReplyExecutionRequest(
                    invitation.CompanyId,
                    source.Connection.Id,
                    source.Connection.Provider.ToStorageValue(),
                    source.Link.ExternalMessageId,
                    source.Link.ExternalThreadId,
                    source.Link.InternetMessageId ?? original.InternetMessageId,
                    invitation.AttendeeEmail,
                    invitation.AttendeeName,
                    original.Subject ?? invitation.Title,
                    BuildBody(invitation),
                    invitation.ConfirmationIdempotencyKey),
                cancellationToken);

            invitation.MarkConfirmationSent(
                source.Connection.Id,
                result.ProviderMessageId,
                result.ProviderThreadId ?? source.Link.ExternalThreadId,
                mailboxProvider.ReplyThreadingMode,
                DateTime.UtcNow);
            _dbContext.SalesActivities.Add(new SalesActivity(
                Guid.NewGuid(), invitation.CompanyId, "meeting confirmation",
                $"Meeting confirmation sent to {invitation.AttendeeEmail} in the sales email thread.",
                DateTime.UtcNow, invitation.LeadId, invitation.DealId, invitation.ContactId));
            AddAudit(
                invitation, message.CorrelationId,
                "sales.meeting_confirmation.sent", AuditEventOutcomes.Succeeded,
                "The approved meeting was confirmed in the originating sales email thread.");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (MailboxProviderExecutionException ex)
        {
            invitation.MarkConfirmationFailed(ex.Code, ex.Message);
            AddAudit(
                invitation, message.CorrelationId,
                "sales.meeting_confirmation.delivery_failed", AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ex.IsRetryable) throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            invitation.MarkConfirmationReconciliationRequired(
                "confirmation_outcome_unknown",
                "The mailbox provider response was interrupted. Check the sales thread before retrying.");
            AddAudit(
                invitation, message.CorrelationId,
                "sales.meeting_confirmation.reconciliation_required", AuditEventOutcomes.Blocked,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            invitation.MarkConfirmationFailed("confirmation_mailbox_unavailable", ex.Message);
            AddAudit(
                invitation, message.CorrelationId,
                "sales.meeting_confirmation.delivery_failed", AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ConfirmationSource?> ResolveSourceAsync(
        SalesMeetingInvitation invitation,
        CancellationToken cancellationToken)
    {
        var link = await _dbContext.SalesEmailLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == invitation.CompanyId &&
                x.LeadId == invitation.LeadId && !x.IsDeleted &&
                x.LinkKind == SalesEmailLinkKinds.Message &&
                x.MailboxConnectionId != null && x.Provider != null)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (link?.MailboxConnectionId is not Guid connectionId) return null;

        var connection = await _dbContext.MailboxConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == invitation.CompanyId && x.Id == connectionId &&
                x.Purpose == MailboxPurpose.Sales &&
                x.Status == MailboxConnectionStatus.Active &&
                x.CapabilityFlags.HasFlag(MailboxCapability.SendMessages) &&
                x.CapabilityFlags.HasFlag(MailboxCapability.ThreadCorrelation),
                cancellationToken);
        return connection is null ? null : new ConfirmationSource(link, connection);
    }

    private async Task MarkUnavailableAsync(
        SalesMeetingInvitation invitation, string? correlationId,
        string summary, CancellationToken cancellationToken)
    {
        invitation.MarkConfirmationUnavailable(summary);
        AddAudit(
            invitation, correlationId,
            "sales.meeting_confirmation.unavailable", AuditEventOutcomes.Blocked,
            summary);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(
        SalesMeetingInvitation invitation, string? correlationId,
        string action, string outcome, string rationale)
    {
        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), invitation.CompanyId, AuditActorTypes.System, actorId: null,
            action, "sales_meeting_invitation", invitation.Id.ToString("D"), outcome,
            rationale, ["calendar provider", "sales email thread"],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["approvalRequestId"] = invitation.ApprovalRequestId?.ToString("D"),
                ["calendarEventId"] = invitation.ExternalEventId,
                ["confirmationStatus"] = invitation.ConfirmationStatus.ToStorageValue(),
                ["confirmationMessageId"] = invitation.ConfirmationProviderMessageId,
                ["confirmationThreadingMode"] = invitation.ConfirmationThreadingMode.ToStorageValue(),
                ["confirmationErrorCode"] = invitation.ConfirmationErrorCode
            },
            correlationId));
    }

    private static string BuildBody(SalesMeetingInvitation invitation)
    {
        var (starts, ends) = LocalTimes(invitation);
        var builder = new StringBuilder();
        builder.Append("Hi ").Append(invitation.AttendeeName ?? "there").AppendLine(",").AppendLine();
        builder.Append("Your meeting is confirmed for ")
            .Append(starts.ToString("dddd, MMMM d, yyyy 'at' HH:mm"))
            .Append('-').Append(ends.ToString("HH:mm"))
            .Append(" (").Append(invitation.TimeZoneId).AppendLine(").");
        builder.AppendLine().AppendLine(invitation.Title);
        if (!string.IsNullOrWhiteSpace(invitation.OnlineMeetingUrl))
            builder.Append("Join online: ").AppendLine(invitation.OnlineMeetingUrl);
        else if (!string.IsNullOrWhiteSpace(invitation.Location))
            builder.Append("Location: ").AppendLine(invitation.Location);
        builder.AppendLine().AppendLine("A calendar invitation has also been sent.");
        builder.Append("Best regards,").AppendLine().Append(invitation.OrganizerEmail);
        return builder.ToString();
    }

    private static (DateTime Starts, DateTime Ends) LocalTimes(SalesMeetingInvitation invitation)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(invitation.TimeZoneId);
            return (
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(invitation.StartsUtc, DateTimeKind.Utc), zone),
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(invitation.EndsUtc, DateTimeKind.Utc), zone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return (invitation.StartsUtc, invitation.EndsUtc);
        }
    }

    private sealed record ConfirmationSource(SalesEmailLink Link, MailboxConnection Connection);
}
