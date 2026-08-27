# B2Brouter Peppol delivery runbook

## Supported production contract

Virtual Company supports B2Brouter API version `2026-06-26` for Peppol BIS Billing 3.0 UBL invoices and credit notes. Submission uses B2Brouter's invoice import operation with `send_after_import=true`, Peppol transport, and the exact BIS 3 document type. Recipient discovery uses the B2Brouter directory, document validation uses the provider XSD/Schematron validator, and acknowledgements use invoice status polling plus the optional signed `issued_invoice.state_change` webhook.

Authoritative provider references:

- [Import invoice](https://developer.b2brouter.net/reference/import-invoice)
- [Peppol directory lookup](https://developer.b2brouter.net/reference/lookup-directory-by-scheme)
- [Validate document](https://developer.b2brouter.net/reference/validate-document)
- [Get invoice](https://developer.b2brouter.net/reference/get-invoice)
- [Invoice state webhook](https://developer.b2brouter.net/reference/getnewinvoicestatechange)
- [Invoice states](https://developer.b2brouter.net/reference/get-invoice-states)

The application owns provider-neutral commands and states. B2Brouter request/response schemas remain in Finance Infrastructure, and raw provider bodies, credentials, and invoice XML are not written to logs or audit summaries.

## Configuration and secrets

Configure `FinanceIntegrations:B2BRouter`:

| Key | Requirement |
| --- | --- |
| `Enabled` | Enable only after sandbox verification. |
| `Environment` | `sandbox` or `production`. |
| `ApiBaseUrl` | `https://api-staging.b2brouter.net/` for sandbox; the non-staging HTTPS host for production. Startup validation rejects an environment/host mismatch. |
| `ApiVersion` | Pinned to the verified date, currently `2026-06-26`. |
| `AccountId` | Single-company deployment fallback. Do not use it to share one provider account across unrelated companies. |
| `CompanyAccountIds:{company-guid}` | Preferred multi-company allowlist from a Virtual Company company id to its B2Brouter account id. When any mappings exist, `AccountId` is ignored and an unmapped company is unavailable. |
| `ApiKey` | Required secret. Store in .NET user-secrets locally or the deployment secret provider. Never commit it. |
| `PaymentAccountId` | Seller IBAN or account identifier required for invoice payment instructions. |
| `PaymentAccountName` / `PaymentServiceProviderId` | Optional payment account name and BIC. |
| `WebhooksEnabled` / `WebhookSecret` | Enable together after registering the callback. The secret belongs in secret storage. |
| `WebhookToleranceSeconds` | Replay window, 60–900 seconds; default 300. |
| `ReconciliationPollingSeconds` / `MaximumReconciliationAttempts` | Bounded acknowledgement polling controls. |
| `MaximumAttachmentBytes` | Maximum immutable PDF size embedded in UBL, default 2 MB. |

The configured API key is limited to B2Brouter invoice, directory, validation, and status operations. Rotate it in the secret provider, restart the API, and confirm the `b2brouter-peppol` readiness check. Capability status is company-scoped: a company without an account mapping is reported unavailable even when another company is configured.

## Delivery and acknowledgement states

The API request only queues work through the company outbox. Immediately before submission, the worker verifies the retained Peppol participant and validates deterministic UBL locally, then asks B2Brouter to run its XSD/Schematron validation. The delivery record and `submitting` ambiguity boundary are committed before the external write.

| Provider evidence | Virtual Company state | Automatic email fallback |
| --- | --- | --- |
| Directory not found or local/provider validation rejection before send | Validation failed or recipient unsupported | Allowed when explicitly requested. |
| `new`, `issued`, `sending` | Accepted for processing | Never. |
| `sent`, `accepted`, `registered`, `paid`, `closed` | Delivered | Never. |
| `refused`, `error`, `discarded` | Rejected | Allowed only after this definitive provider evidence. |
| Timeout after write or unknown response/state | Reconciliation required | Never until provider evidence proves no delivery. |
| 429 or retryable upstream failure before provider acceptance | Retryable failure | Never; bounded outbox retry owns the attempt. |

HTTP success is not final delivery. Every submission, polling, and webhook transition is retained as a company-scoped event. Provider reconciliation uses the provider reference; when it was not returned after an ambiguous import, it searches the configured company account for the exact immutable invoice number and never blindly resubmits.

## Webhook setup and response

Register:

`POST /api/integrations/b2brouter/webhooks/invoice-state`

The endpoint accepts only `issued_invoice.state_change`. It verifies B2Brouter's HMAC-SHA256 `X-B2Brouter-Signature`, enforces the timestamp window, derives an evidence hash, resolves the signed provider invoice reference to a persisted delivery, checks the payload account against that delivery's company connection, and deduplicates the provider event id. Forged, stale, unknown-reference, cross-account, and unsupported events are rejected. The endpoint is anonymous only because HMAC verification is the transport authentication boundary.

If webhook delivery is delayed or disabled, the durable reconciliation outbox continues polling. A replayed valid event returns success without applying a second transition.

## Operator actions

- Inspect company capability: `GET /internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-provider`.
- Inspect a delivery: `GET /internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-deliveries/{deliveryId}`.
- Retry only a pre-submission retryable failure: `POST .../electronic-deliveries/{deliveryId}/retry` with a reason.
- Reconcile accepted, ambiguous, or pending acknowledgement: `POST .../electronic-deliveries/{deliveryId}/reconcile` with a reason.

Both mutations require accounting-administrator authorization and produce safe audit events. Never manually retry a record in `submitting` or `reconciliation_required`; reconcile it first. Never use direct email to work around an ambiguous provider outcome because that can duplicate delivery.

## Launch limitations

- Swedish domestic, SEK native invoices and credit notes only.
- Peppol participant schemes `0007` (Swedish organisation number) and `0088` (GLN).
- Peppol BIS Billing 3.0 only; no generic “Peppol compatible” claim and no provider cancellation because the verified contract does not support it here.
- Positive supported VAT rates and retained statutory/tax facts are required. Buyer reference, seller legal/address/VAT facts, buyer address, payment account for invoices, and a rendered immutable PDF are mandatory.
- Supported units are each, hour, day, and kilogram. Unsupported profiles, identifiers, currencies, units, or missing immutable evidence stop before transmission with safe remediation.
- The PDF is embedded as a supporting attachment; no mutable or separately uploaded attachment is accepted.

## Verification and incident handling

Run the `B2BRouterPeppolDeliveryTests`, customer-invoice delivery API surface tests, and Finance API client delivery tests. The sandbox contract test is categorized `B2BRouterSandbox` and performs directory plus provider XSD/Schematron validation only when `B2BROUTER_INTEGRATION_TESTS_ENABLED=true`; supply `B2BROUTER_API_KEY`, `B2BROUTER_TEST_PARTICIPANT_SCHEME`, and `B2BROUTER_TEST_PARTICIPANT_ID` at runtime. It never imports or sends an invoice. Real-provider tests must stay opt-in, redact output, and use designated sandbox participants; they are not part of the secret-free default suite.

For an incident, check the `b2brouter-peppol` health result and the `VirtualCompany.Finance.B2BRouter` counters, then inspect the company-scoped delivery/event history. Rotate invalid credentials in secret storage. Reconcile ambiguous work after provider recovery. If automatic reconciliation reaches its bound, compare the immutable document number and retained provider reference in B2Brouter before choosing a terminal operator action. Do not copy provider payloads into tickets or logs.
