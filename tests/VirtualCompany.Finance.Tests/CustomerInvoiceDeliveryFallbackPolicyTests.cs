using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerInvoiceDeliveryFallbackPolicyTests
{
    [Fact]
    public void Unavailable_peppol_queues_email_when_the_recipient_and_fallback_are_available()
    {
        var electronic = new CustomerInvoiceElectronicDeliveryResult(
            CustomerInvoiceElectronicDeliveryOutcomes.Unavailable,
            CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable,
            "No production Peppol provider is configured.", true);

        var decision = CustomerInvoiceDeliveryFallbackPolicy.Decide(electronic, true, true);

        Assert.True(decision.QueueEmail);
        Assert.Equal(CustomerInvoiceDeliveryChannels.Email, decision.SelectedChannel);
        Assert.Equal(CustomerInvoiceDeliveryStatuses.Pending, decision.Status);
        Assert.Equal(CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable, decision.ReasonCode);
    }

    [Theory]
    [InlineData(CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired)]
    [InlineData(CustomerInvoiceElectronicDeliveryOutcomes.RetryableFailure)]
    public void Uncertain_or_retryable_peppol_outcome_never_triggers_email_fallback(string outcome)
    {
        var electronic = new CustomerInvoiceElectronicDeliveryResult(outcome,
            "provider_outcome", "Check the provider.", true, "provider");

        var decision = CustomerInvoiceDeliveryFallbackPolicy.Decide(electronic, true, true);

        Assert.False(decision.QueueEmail);
        Assert.Equal(CustomerInvoiceDeliveryChannels.Peppol, decision.SelectedChannel);
        Assert.Equal(CustomerInvoiceDeliveryReasonCodes.PeppolOutcomePending, decision.ReasonCode);
    }

    [Fact]
    public void Fallback_requires_both_policy_permission_and_a_retained_email_recipient()
    {
        var electronic = new CustomerInvoiceElectronicDeliveryResult(
            CustomerInvoiceElectronicDeliveryOutcomes.RecipientUnsupported,
            CustomerInvoiceDeliveryReasonCodes.PeppolRecipientUnsupported,
            "The participant is not registered.", true, "provider");

        var disabled = CustomerInvoiceDeliveryFallbackPolicy.Decide(electronic, false, true);
        var missingRecipient = CustomerInvoiceDeliveryFallbackPolicy.Decide(electronic, true, false);

        Assert.Equal(CustomerInvoiceDeliveryReasonCodes.EmailFallbackDisabled, disabled.ReasonCode);
        Assert.Equal(CustomerInvoiceDeliveryChannels.None, disabled.SelectedChannel);
        Assert.Equal(CustomerInvoiceDeliveryReasonCodes.DeliveryAddressMissing, missingRecipient.ReasonCode);
        Assert.Equal(CustomerInvoiceDeliveryChannels.None, missingRecipient.SelectedChannel);
    }

    [Fact]
    public void Email_delivery_retains_typed_peppol_fallback_evidence()
    {
        var delivery = new CustomerInvoiceEmailDelivery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new string('a', 64), "billing@example.test", new string('b', 64),
            "Invoice 100", "Automatic fallback", "key", Guid.NewGuid(), DateTime.UtcNow,
            CustomerInvoiceEmailRequestSources.PeppolFallback,
            CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable, "provider");

        Assert.Equal(CustomerInvoiceEmailRequestSources.PeppolFallback, delivery.RequestSource);
        Assert.Equal(CustomerInvoiceDeliveryReasonCodes.PeppolProviderUnavailable, delivery.FallbackReasonCode);
        Assert.Equal("provider", delivery.FallbackProviderKey);
    }
}
