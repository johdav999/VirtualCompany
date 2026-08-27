# Customer invoice Peppol-to-email fallback

## Outcome

Native customer invoices prefer Peppol when a production provider is registered. The fallback uses the existing durable Finance email outbox and the exact immutable invoice PDF. It does not send email from the API request and does not change invoice issue or accounting state.

B2Brouter is the selected production provider for Peppol BIS Billing 3. A preferred-delivery request uses the company-scoped B2Brouter connection when configured. It records `customer_invoice_delivery_peppol_credentials_missing` and may queue email only when no external transmission occurred, the issued snapshot contains a valid invoice email address, and the caller permits fallback.

## Delivery decision

| Peppol outcome | Automatic email fallback | Reason |
| --- | --- | --- |
| Provider unavailable before submission | Yes | No external transmission occurred. |
| Recipient unsupported | Yes, only when the provider marks the outcome safe | No Peppol route exists for the retained participant. |
| Local validation failed | Yes, only when the provider marks the outcome safe | Nothing was submitted externally. |
| Definitively rejected | Yes, only when the provider marks the outcome safe | Provider evidence proves Peppol did not deliver. |
| Queued, accepted, or delivered | No | Peppol owns the delivery path. |
| Retryable failure | No | The durable Peppol workflow retains retry authority. |
| Timeout, ambiguity, or reconciliation required | No | Email could duplicate a possibly accepted invoice. |

Provider claims never override the hard safety rule: queued, accepted, delivered, retryable, and ambiguous outcomes cannot trigger fallback.

## Durable flow

1. The authorized API command validates company scope, native invoice identity, immutable issued snapshot, rendered artifact hash, recipient snapshot, and idempotency key.
2. The registered B2Brouter `ICustomerInvoiceElectronicDeliveryProvider` receives the stable business key and resolves the provider account for the company. Missing company credentials are reported truthfully; no provider is simulated.
3. `CustomerInvoiceDeliveryFallbackPolicy` selects Peppol, email, or a blocked state.
4. Email fallback creates one `CustomerInvoiceEmailDelivery` with source `peppol_fallback`, a typed fallback reason, optional provider key, and a derived idempotency key.
5. The existing company outbox dispatches through the configured Finance mailbox. Provider acceptance is retained as acceptance, not recipient delivery.
6. Ambiguous email outcomes enter reconciliation-required and cannot be resent blindly.

## API

`POST /internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId}/preferred-delivery`

The request supplies the immutable PDF artifact, optional retained recipient override, fallback permission, operator reason, and idempotency key. The response identifies the preferred and selected channel, safe reason code, provider/profile references when available, and the queued email delivery when fallback was used.

Direct email and resend endpoints remain available and retain their existing behavior. Peppol provider credentials, payloads, and raw errors never enter this contract.

## Operational requirements

- Configure one active Finance-purpose standard SMTP mailbox with attachment sending capability.
- Reconcile any Peppol or email ambiguous outcome before manual delivery or resend.
- Treat a disabled or unconfigured company connection as an unavailable capability, not successful e-invoice delivery.
- Operate B2Brouter submission and reconciliation according to [the B2Brouter Peppol delivery runbook](b2brouter-peppol-delivery-runbook.md).
