using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportReplyDeliveryDispatcher(
    VirtualCompanyDbContext dbContext,
    ISupportOutboundEmailSender outboundEmailSender,
    IAuditEventWriter audit,
    TimeProvider timeProvider) : ISupportReplyDeliveryDispatcher
{
    public async Task DispatchAsync(
        SupportReplyDeliveryRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var draft = await dbContext.SupportReplyDrafts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == message.CompanyId && x.Id == message.DraftId,
                cancellationToken)
            ?? throw new InvalidOperationException("The queued support reply draft no longer exists.");

        if (draft.SentUtc.HasValue)
        {
            return;
        }

        if (!message.Autonomous && draft.Status != SupportReplyDraftStatuses.Approved)
        {
            throw new InvalidOperationException("The queued support reply is no longer approved.");
        }

        var supportCase = await dbContext.SupportCases
            .IgnoreQueryFilters()
            .Include(x => x.Messages)
            .Include(x => x.Events)
            .SingleOrDefaultAsync(
                x => x.CompanyId == message.CompanyId && x.Id == message.SupportCaseId,
                cancellationToken)
            ?? throw new InvalidOperationException("The queued support case no longer exists.");

        try
        {
            var sendResult = await outboundEmailSender.SendReplyAsync(
                new SupportOutboundEmailSendRequest(
                    message.CompanyId,
                    message.SupportCaseId,
                    message.DraftId,
                    message.MailboxConnectionId,
                    message.ToEmail,
                    message.ToDisplayName,
                    message.Subject,
                    draft.DraftBody,
                    message.OriginalMessageId,
                    message.ProviderThreadId,
                    message.InternetMessageId,
                    message.IdempotencyKey),
                cancellationToken);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            draft.MarkSent(now);
            dbContext.SupportMessages.Add(new SupportMessage(
                Guid.NewGuid(),
                message.CompanyId,
                supportCase.Id,
                SupportMessageDirections.Outbound,
                "email",
                "support",
                message.ToEmail,
                draft.DraftBody,
                now,
                providerMessageId: sendResult.ProviderMessageId,
                providerThreadId: sendResult.ProviderThreadId,
                replyDraftId: draft.Id));
            supportCase.LinkProviderMessage(sendResult.ProviderThreadId, sendResult.ProviderMessageId);
            supportCase.MarkFirstResponseSent(now);
            supportCase.SetStatus(message.ResolveAfterSend ? SupportCaseStatuses.Resolved : SupportCaseStatuses.WaitingForCustomer);
            dbContext.SupportCaseEvents.Add(new SupportCaseEvent(
                Guid.NewGuid(),
                message.CompanyId,
                supportCase.Id,
                SupportCaseEventTypes.ReplySent,
                "Support reply sent.",
                message.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human,
                message.Autonomous ? null : message.RequestedByUserId,
                now));
            await audit.WriteAsync(new AuditEventWriteRequest(
                message.CompanyId,
                message.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human,
                message.Autonomous ? null : message.RequestedByUserId,
                "support.reply.sent",
                "support_case",
                supportCase.Id.ToString("D"),
                AuditEventOutcomes.Succeeded,
                "Support reply sent through the connected mailbox provider.",
                ["support", "mailbox", "outbox"],
                Metadata: new Dictionary<string, string?>
                {
                    ["provider"] = sendResult.Provider,
                    ["mailboxConnectionId"] = sendResult.MailboxConnectionId.ToString("D"),
                    ["providerMessageId"] = sendResult.ProviderMessageId,
                    ["providerThreadId"] = sendResult.ProviderThreadId,
                    ["idempotencyKey"] = message.IdempotencyKey
                }), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (MailboxProviderExecutionException ex) when (ex.Code == "smtp_delivery_ambiguous")
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            draft.MarkDeliveryReconciliationRequired(
                "Delivery could not be confirmed. Check the mailbox Sent folder before sending this reply again.",
                now);
            await audit.WriteAsync(new AuditEventWriteRequest(
                message.CompanyId,
                message.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human,
                message.Autonomous ? null : message.RequestedByUserId,
                "support.reply.reconciliation_required",
                "support_reply_draft",
                draft.Id.ToString("D"),
                AuditEventOutcomes.Failed,
                "SMTP delivery could not be confirmed and requires Sent-folder reconciliation.",
                ["support", "mailbox", "outbox", "reconciliation"],
                Metadata: new Dictionary<string, string?>
                {
                    ["failureCode"] = ex.Code,
                    ["idempotencyKey"] = message.IdempotencyKey
                }), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            draft.MarkSendFailed("Support reply could not be sent through the connected mailbox.");
            await audit.WriteAsync(new AuditEventWriteRequest(
                message.CompanyId,
                message.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human,
                message.Autonomous ? null : message.RequestedByUserId,
                "support.reply.send_failed",
                "support_reply_draft",
                draft.Id.ToString("D"),
                AuditEventOutcomes.Failed,
                "Support reply could not be sent through the connected mailbox.",
                ["support", "mailbox", "outbox"],
                Metadata: new Dictionary<string, string?>
                {
                    ["exceptionType"] = ex.GetType().Name,
                    ["idempotencyKey"] = message.IdempotencyKey
                }), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
