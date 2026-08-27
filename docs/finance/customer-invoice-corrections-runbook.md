# Customer invoice corrections and refunds

## Operating boundary

Receivables corrections are explicit, company-scoped records. The original invoice, issued snapshot,
delivery evidence, payment allocations, journal, and VAT return are never edited to manufacture a
correction. Every proposal stores its amount, type, reason, evidence reference, source version, source
hash, approval, task, and stable request identity.

Supported actions are cancellation of an unpaid/unposted/undelivered native invoice, full or partial
credit, price/quantity/tax credit, refund, small-balance write-off, bad debt, and bad-debt recovery.
Drafts that have not been issued continue to use the existing discard action. Provider-authoritative
invoices return `customer_invoice_correction_provider_action_required` and must use the existing
provider action workflow.

## Approval and execution

The policy calculates completed incoming allocations, additive allocation-release adjustments,
existing correction reservations, remaining economic balance, period state, and locked VAT-return
state. A proposal reserves its amount so parallel proposals cannot over-credit, over-refund, or
over-write-off the invoice. Execution re-evaluates those facts and rejects a stale source hash or
approval.

Credit notes use the native customer-invoice draft and issue boundary. They retain the original
invoice and issued-document references, receive a separate statutory number, produce an immutable
snapshot, and post a journal linked to the original journal. Write-off and bad-debt journals post in
an explicitly selected current open period through `IAccountingPostingService`. Closed periods are
not reopened. If the original tax facts are part of a locked VAT return, execution creates and links
a correction-return draft before posting the correction.

## Refund operations

A refund proposal is not a payment. It requires beneficiary and payment evidence plus current
approval. If a configured `ICustomerRefundExecutionProvider` matches the requested provider key, the
refund is queued for bounded background execution with a stable business idempotency key. If no
provider is configured, the result is `manual_instruction`; this is an approved instruction and does
not claim that money moved.

Successful refund acknowledgement creates additive allocation-release records. Original allocation
records remain traceable. Timeout, connection loss, a provider-declared ambiguous result, an expired
execution lease, or a provider-success/local-persistence uncertainty results in
`reconciliation_required`; automatic retry stops. Confirm the provider outcome and record evidence:

- Confirmed succeeded: record the provider reference, release the corresponding allocation amount,
  and complete the correction.
- Confirmed absent: record evidence and return the durable execution to its bounded retry queue.
- Manual instruction succeeded: record the bank/payment reference and complete the allocation
  release.
- Manual instruction not executed: record the negative outcome; the proposal fails without claiming
  a payment.

Never retry an ambiguous refund before checking the provider or bank. Do not place account numbers,
provider payloads, credentials, or other sensitive payment data in logs or failure summaries.

## Reconciliation checks

For each completed correction, verify the company, original invoice, approval, source hash,
correcting invoice or journal, VAT correction return when applicable, and refund execution/allocation
adjustments. A correction remaining in `queued`, `failed`, `manual_instruction`, or
`reconciliation_required` is operator-visible unfinished work and must not be reported as completed.
