using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public sealed record CustomerInvoiceDeliveryFallbackDecision(
    bool QueueEmail,
    string SelectedChannel,
    string Status,
    string ReasonCode);

public static class CustomerInvoiceDeliveryFallbackPolicy
{
    public static CustomerInvoiceDeliveryFallbackDecision Decide(
        CustomerInvoiceElectronicDeliveryResult electronic,
        bool allowEmailFallback,
        bool hasEmailRecipient)
    {
        ArgumentNullException.ThrowIfNull(electronic);

        if (electronic.Outcome is CustomerInvoiceElectronicDeliveryOutcomes.Queued
            or CustomerInvoiceElectronicDeliveryOutcomes.Accepted
            or CustomerInvoiceElectronicDeliveryOutcomes.Delivered)
        {
            return new(false, CustomerInvoiceDeliveryChannels.Peppol, electronic.Outcome, electronic.ReasonCode);
        }

        if (electronic.Outcome is CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired
            or CustomerInvoiceElectronicDeliveryOutcomes.RetryableFailure)
        {
            return new(false, CustomerInvoiceDeliveryChannels.Peppol, electronic.Outcome,
                CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending);
        }

        if (!electronic.IsSafeToFallback)
        {
            return new(false, CustomerInvoiceDeliveryChannels.Peppol,
                CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired,
                CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending);
        }

        if (!allowEmailFallback)
        {
            return new(false, CustomerInvoiceDeliveryChannels.None, "blocked",
                CustomerInvoiceDeliveryReasonCodes.EmailFallbackDisabled);
        }

        if (!hasEmailRecipient)
        {
            return new(false, CustomerInvoiceDeliveryChannels.None, "blocked",
                CustomerInvoiceDeliveryReasonCodes.DeliveryAddressMissing);
        }

        return new(true, CustomerInvoiceDeliveryChannels.Email, CustomerInvoiceDeliveryStatuses.Pending,
            electronic.ReasonCode);
    }
}
