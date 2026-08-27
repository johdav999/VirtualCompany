using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerReminderDeliveryService(
    VirtualCompanyDbContext db,
    CustomerCollectionsService collections,
    IMailboxTransportRegistry transports,
    IFieldEncryptionService encryption,
    IAuditEventWriter audit,
    ILogger<CustomerReminderDeliveryService> logger,
    CustomerCollectionsTelemetry? telemetry = null) : ICustomerReminderDeliveryDispatcher
{
    public async Task DeliverAsync(Guid companyId, Guid deliveryId, CancellationToken ct)
    {
        var delivery = await db.CustomerReminderDeliveries.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == deliveryId, ct)
            ?? throw new PermanentBackgroundJobException("The customer reminder delivery request no longer exists.");
        if (delivery.Status is "accepted" or "reconciliation_required" or "blocked") return;
        var draft = await db.CustomerReminderDrafts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == delivery.ReminderDraftId, ct)
            ?? throw new PermanentBackgroundJobException("The customer reminder draft no longer exists.");
        try
        {
            await collections.EnsureDraftSendableAsync(draft, ct);
        }
        catch (CustomerCollectionException ex)
        {
            delivery.Block(ex.ReasonCode, ex.Message, DateTime.UtcNow); draft.Block(DateTime.UtcNow);
            await audit.WriteAsync(new(companyId, AuditActorTypes.System, null, "finance.customer_reminder.send_blocked",
                "customer_reminder", draft.Id.ToString("N"), AuditEventOutcomes.Failed,
                "Customer contact was blocked because current receivable evidence no longer allowed the reminder.",
                ["finance", "receivables", "reminder", "outbox"],
                new Dictionary<string, string?> { ["deliveryId"] = delivery.Id.ToString("N"), ["reasonCode"] = ex.ReasonCode }), ct);
            await db.SaveChangesAsync(ct); telemetry?.Delivery("blocked"); throw new PermanentBackgroundJobException(ex.Message);
        }

        delivery.Start(DateTime.UtcNow); await db.SaveChangesAsync(ct);
        try
        {
            var connection = await db.MailboxConnections.IgnoreQueryFilters().Where(x => x.CompanyId == companyId &&
                    x.Purpose == MailboxPurpose.Finance && x.Status == MailboxConnectionStatus.Active &&
                    x.CapabilityFlags.HasFlag(MailboxCapability.SendMessages))
                .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(ct)
                ?? throw new CustomerCollectionException(CustomerCollectionReasonCodes.RecipientMissing,
                    "Connect a finance mailbox before sending customer reminders.");
            if (connection.Provider != MailboxProvider.StandardEmail)
                throw new CustomerCollectionException(CustomerCollectionReasonCodes.RecipientMissing,
                    "The selected finance mailbox cannot send customer reminders. Connect a standard SMTP mailbox.");
            var context = StandardMailboxSessionCodec.Decode(StandardMailboxSessionCodec.Create(connection, encryption));
            var message = new MailboxOutboundMessage($"<reminder-{delivery.Id:N}@virtualcompany.local>", connection.EmailAddress,
                [draft.RecipientEmail], [], [], draft.Subject, draft.Body, null, null, [], []);
            var result = await transports.Resolve("mailkit").SendAsync(context, message, ct);
            if (result.Outcome == MailboxSubmissionOutcome.Accepted)
            {
                delivery.Accept(result.ProviderReference, DateTime.UtcNow); draft.Accept(DateTime.UtcNow);
                await audit.WriteAsync(new(companyId, AuditActorTypes.System, null, "finance.customer_reminder.email_accepted",
                    "customer_reminder", draft.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                    "The mailbox provider accepted the reminder email. Recipient delivery is not asserted.",
                    ["finance", "receivables", "reminder", "mailbox"], new Dictionary<string, string?>
                    { ["deliveryId"] = delivery.Id.ToString("N"), ["providerReference"] = result.ProviderReference, ["sourceHash"] = draft.SourceHash }), ct);
                await db.SaveChangesAsync(ct); telemetry?.Delivery("accepted"); return;
            }
            if (result.Outcome == MailboxSubmissionOutcome.Ambiguous)
            {
                delivery.Fail(result.SafeFailureCode ?? CustomerCollectionReasonCodes.DeliveryAmbiguous,
                    "The mailbox outcome is ambiguous. Reconcile the Sent folder before any resend.", true, DateTime.UtcNow);
                draft.Fail(true, DateTime.UtcNow);
                await audit.WriteAsync(new(companyId, AuditActorTypes.System, null, "finance.customer_reminder.email_reconciliation_required",
                    "customer_reminder", draft.Id.ToString("N"), AuditEventOutcomes.Failed,
                    "The reminder mailbox outcome is ambiguous and requires reconciliation.",
                    ["finance", "receivables", "reminder", "mailbox"], new Dictionary<string, string?>
                    { ["deliveryId"] = delivery.Id.ToString("N"), ["failureCode"] = result.SafeFailureCode }), ct);
                await db.SaveChangesAsync(ct); telemetry?.Delivery("reconciliation_required"); return;
            }
            delivery.Fail(result.SafeFailureCode ?? "customer_reminder_delivery_failed",
                result.SafeFailureMessage ?? "The customer reminder email could not be sent.", false, DateTime.UtcNow);
            draft.Fail(false, DateTime.UtcNow); await db.SaveChangesAsync(ct); telemetry?.Delivery("failed");
            if (result.Outcome is MailboxSubmissionOutcome.PermanentFailure or MailboxSubmissionOutcome.AuthenticationRequired)
                throw new PermanentBackgroundJobException("Customer reminder delivery failed permanently. Correct the recipient or reconnect the finance mailbox.");
            throw new InvalidOperationException("Customer reminder delivery failed and will be retried.");
        }
        catch (CustomerCollectionException ex)
        {
            delivery.Block(ex.ReasonCode, ex.Message, DateTime.UtcNow); draft.Block(DateTime.UtcNow); await db.SaveChangesAsync(ct); telemetry?.Delivery("blocked");
            throw new PermanentBackgroundJobException(ex.Message);
        }
        catch (PermanentBackgroundJobException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Customer reminder delivery {DeliveryId} failed for company {CompanyId}.", deliveryId, companyId);
            throw;
        }
    }
}
