using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingCustomerMinutesDeliveryDispatcher(VirtualCompanyDbContext db, IOutboundEmailSender sender)
    : ISalesMeetingCustomerMinutesDeliveryDispatcher
{
    public async Task DispatchAsync(SalesMeetingCustomerMinutesDeliveryRequestedMessage message, CancellationToken ct)
    {
        var x = await db.SalesMeetingChangeProposals.SingleOrDefaultAsync(p => p.CompanyId == message.CompanyId && p.Id == message.ProposalId, ct)
            ?? throw new InvalidOperationException("Customer-minutes proposal was not found.");
        if (!string.Equals(x.IdempotencyKey, message.IdempotencyKey, StringComparison.Ordinal)) throw new InvalidOperationException("Customer-minutes delivery idempotency key does not match.");
        if (x.Status == SalesMeetingChangeProposalStatus.Executed) { SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "deduplicated"); return; }
        if (x.Status == SalesMeetingChangeProposalStatus.ReconciliationRequired) { SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "reconciliation_pending"); return; }
        if (x.Status is not (SalesMeetingChangeProposalStatus.Queued or SalesMeetingChangeProposalStatus.Failed)) throw new InvalidOperationException("Customer minutes are not queued for delivery.");
        var minutes = await db.SalesMeetingMinutes.AsNoTracking().Include(m => m.Items).SingleAsync(m => m.CompanyId == message.CompanyId && m.SessionId == x.SessionId && m.Id == x.TargetId, ct);
        if (minutes.Status != SalesMeetingClosingArtifactStatus.Approved || minutes.ConcurrencyVersion.ToString() != x.TargetVersion)
        { x.MarkConflict(SalesMeetingChangeProposalProblemCodes.TargetChanged, "The approved customer minutes changed before delivery. Review and approve the current version."); await db.SaveChangesAsync(ct); SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "conflict"); return; }
        var proposed = System.Text.Json.JsonSerializer.Deserialize<string>(x.ProposedValueJson) ?? throw new InvalidOperationException("Customer-minutes recipient is missing.");
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(s => s.CompanyId == message.CompanyId && s.Id == x.SessionId, ct);
        try
        {
            var result = await sender.SendSequenceEmailAsync(new OutboundEmailSendRequest(message.CompanyId, x.SessionId, x.Id, x.Id,
                session.ContactId ?? x.Id, proposed, null, "Meeting notes and next steps", BuildBody(minutes), x.IdempotencyKey), ct);
            x.MarkExecuted(x.BeforeValueJson, System.Text.Json.JsonSerializer.Serialize("sent"), result.ProviderMessageId, DateTime.UtcNow); await db.SaveChangesAsync(ct); SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "succeeded");
        }
        catch (MailboxProviderExecutionException ex)
        {
            x.MarkFailed(ex.Code, ex.Message); await db.SaveChangesAsync(ct); SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", ex.IsRetryable ? "retryable_failure" : "permanent_failure"); if (ex.IsRetryable) throw;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            x.MarkReconciliationRequired("minutes_delivery_outcome_unknown", "The mailbox response was interrupted. Reconcile the sent folder before retrying."); await db.SaveChangesAsync(ct); SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "ambiguous");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            x.MarkFailed("minutes_delivery_authentication_required", ex.Message); await db.SaveChangesAsync(ct); SalesMeetingChangeTelemetry.RecordDelivery("customer_minutes", "authentication_required");
        }
    }

    private static string BuildBody(Domain.Entities.SalesMeetingMinutes minutes)
    {
        var b = new StringBuilder("Thank you for the meeting. Here are the agreed notes and next steps:\n\n");
        foreach (var item in minutes.Items.OrderBy(i => i.Order))
        { b.Append("• ").Append(item.Content); if (!string.IsNullOrWhiteSpace(item.OwnerLabel)) b.Append(" — ").Append(item.OwnerLabel); if (item.DueUtc.HasValue) b.Append(" (due ").Append(item.DueUtc.Value.ToString("yyyy-MM-dd")).Append(')'); b.AppendLine(); }
        return b.AppendLine().Append("Best regards").ToString();
    }
}
