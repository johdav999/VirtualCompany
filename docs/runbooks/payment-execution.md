# Payment execution operations

## Purpose and safety boundary

Payment execution is the durable path from an internally approved, immutable payment-batch instruction version to provider status, native payment/allocation evidence, an exact booked bank-row match, and journal posting. Provider receipt, bank authorization, bank acceptance, remittance mailbox acceptance, and final bank settlement are separate facts and must not be presented as interchangeable.

The request path never calls the bank. It stores a company-scoped execution and a transactional outbox message. The worker performs the last authority check immediately before the provider write: current batch/approval version, active healthy connection, unexpired consent with payment-initiation capability, current account mapping, verified ownership, protected credentials, current beneficiary evidence, supported rail/currency, and sufficient cash on the selected debit account.

## Enable Banking configuration

Payment initiation is disabled by default. Enable it only for an Enable Banking application with PIS access in the selected environment.

Required settings under `EnableBanking`:

- `Enabled=true`, valid `ApplicationId`, and private signing key.
- `PaymentInitiationEnabled=true` only after sandbox or licensed production PIS access is confirmed.
- `Environment=SANDBOX` or the exact webhook JWT environment value for the deployment.
- Provider-agreed `SingleSepaPaymentType` and `BulkSepaPaymentType` values.

Required settings under `Finance:PaymentExecution`:

- Public HTTPS `RedirectUri` for PSU/bank authorization return.
- Public HTTPS `WebhookUri` ending in `/webhooks/finance/payment-initiation/enable-banking`.
- Bounded `PollIntervalSeconds` and `MaximumStatusPolls`.
- Bounded `MaximumProviderAttempts` for each provider operation and `AuthorizationExpiryMinutes` for unattended bank authorization.

This adapter intentionally supports verified IBAN beneficiaries through SEPA credit transfer only. Bankgiro and Plusgiro instructions fail closed until the selected provider exposes and the application validates an explicit compatible PIS contract.

## Normal operating flow

1. An authorized finance approver selects a healthy PIS connection and verified mapped debit account, then queues execution.
2. The company outbox worker rechecks authority and writes a retained submission attempt before calling `POST /payments`.
3. The operator opens the returned bank authorization link. This is not settlement evidence.
4. Signed webhooks and bounded `GET /payments/{payment_id}` polls retain acknowledgements and instruction-level provider status. Terminal create-payment responses are handled without inventing an authorization URL. Webhook payloads are hashed; raw sensitive bodies are not retained.
5. A supported final provider status creates deterministic, replay-safe native `Payment` and `PaymentAllocation` records plus remittance work.
6. An approver supplies the exact booked debit bank-row identity and source version. Account, direction, currency, and total must agree exactly. Existing reconciliation/accounting services then create the journal links and the execution becomes settled.

## Ambiguous submission recovery

Never retry an unknown provider-write result blindly. Enable Banking does not document a provider idempotency key for `POST /payments`; therefore a timeout, connection break, 5xx response, or worker restart after a started submission freezes the execution as `reconciliation_required`.

Recovery:

1. Search the Enable Banking application/provider portal and the debtor bank for the retained request time, amount, beneficiary set, and execution correlation ID.
2. If a provider payment exists, paste its exact `payment_id` into **Attach and reconcile**. The system queues only a safe status read; it does not resubmit.
   If an HTTP-success response contained the payment ID but malformed local evidence, the worker retains that ID automatically and queues the same safe status read.
3. If no provider payment can be proven, retain that external evidence and create a new approved batch/version through the normal governed workflow. Do not mutate or erase the ambiguous execution.
4. If statuses are partial, the debit account differs, polling reaches its limit, or webhook evidence conflicts, keep the execution in reconciliation and escalate to finance operations.

## Cancellation boundary

Queued work can be cancelled locally before any provider payment identity exists. The Enable Banking adapter does not issue `DELETE /payments/{id}` as a cancellation: that endpoint removes finished/failed records and is not documented as a safe cancellation of an active money movement. Once submitted, review/cancel at the bank and reconcile status in this application.

## Webhook and replay response

The webhook endpoint accepts at most 64 KiB, requires a bearer JWT, requires `RS256`, trusts certificate URLs only on HTTPS `enablebanking.com` hosts, verifies the signature, application subject, environment, and fixed-time SHA-256 body digest, then enforces a unique provider/webhook ID. A repeated ID with the same hash is acknowledged idempotently; the same ID with different evidence returns conflict and requires investigation.

## Remittance recovery

Remittance advice is queued after authoritative completed instruction status. `accepted` means the configured finance mailbox accepted the submission; it does not prove recipient delivery. For ambiguous mailbox outcomes, inspect the Sent folder before retrying. Missing recipient email or mailbox configuration is an operator setup issue and does not change payment settlement state.

## Alerts and telemetry

Monitor meter `VirtualCompany.Finance.PaymentExecution`:

- `finance_payment_execution_queued_total`
- `finance_payment_provider_operations_total` by provider, operation, and outcome
- `finance_payment_execution_ambiguous_total`
- `finance_payment_execution_settled_total`
- `finance_payment_remittance_attempts_total`

Alert on any ambiguous submission, webhook conflict, sustained provider failures, polling exhaustion, remittance ambiguity, or a growing queued/submitting backlog. Correlate with company outbox status, execution attempts, audit events, and provider request IDs; do not log credentials, account numbers, raw provider bodies, or webhook bodies.

## Sandbox verification checklist

Before production activation, retain evidence for one successful and one rejected sandbox payment: approved instruction hash/version, selected consent/account mapping, outbound request hash, provider payment/request IDs, authorization completion, signed webhook verification, polled final status, instruction-level result, created payment/allocation IDs, imported booked bank row, journal IDs, and remittance mailbox result. Also exercise timeout ambiguity, webhook replay, invalid signature, insufficient selected-account cash, stale approval, and unsupported rail.

No real-provider sandbox credentials are stored in this repository. Contract tests cover the provider payload/status/signature boundaries; the environment-specific sandbox checklist must be completed with deployment-owned credentials before `PaymentInitiationEnabled` is enabled.

The worker-restart transaction and database-enforced webhook replay scenario is available in `PaymentExecutionSqlServerIntegrationTests`. Set `VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION` to a dedicated disposable SQL Server instance to run that lane; it is intentionally skipped when the variable is absent.
