# Customer collections operations

This runbook covers the Release 2 customer aging, statement, reminder, dispute, promise-to-pay, and collection-task workflow.

## Operating boundary

- The ledger, posted customer-invoice accounting profiles, completed incoming payments, and effective payment allocations remain the systems of record.
- Aging and customer statements are bounded evidence projections. Statements retain immutable line evidence, a source-manifest hash, a statement checksum, and a content checksum.
- Reminder preparation does not contact a customer. Email is sent only by the company outbox dispatcher after a second live-evidence and approval check.
- Mailbox `accepted` means the provider accepted the submission. It does not prove recipient delivery.
- Ambiguous mailbox outcomes remain `reconciliation_required`. Do not resend until the finance mailbox Sent folder or provider evidence proves whether the original message was accepted.
- Statutory reminder fees and interest are disabled. Attempts to enable either setting are rejected until an authoritative policy pack implements them.
- Customer exceptions are explicit tenant-scoped policy records with a reason and optional inclusive end date. An active exception blocks preparation and the immediate pre-send recheck.

## Configuration

Configure one company-scoped collection policy through the accounting customer-collections policy API. Stages must use unique positive stage numbers and unique days-after-due thresholds. Email is the only supported reminder channel. Keep approval required unless the company has explicitly approved a conservative alternative policy.

`CustomerCollectionWorker` controls the preparation worker:

- `Enabled`: starts or stops scheduled preparation; default `true`.
- `PollIntervalMilliseconds`: 10,000–3,600,000; default 60,000.
- `BatchSize`: 1–200; default 100.
- `LeaseSeconds`: 30–900; default 120.
- `MaximumAttempts`: 1–20; default 5.
- `BaseRetryDelaySeconds` / `MaximumRetryDelaySeconds`: bounded exponential retry, default 30/1,800 seconds.

The worker never sends email. It prepares an idempotent reminder draft and creates one linked finance task for a due invoice. Repeated cycles reuse the stage/source evidence and do not duplicate the logical draft or task.

## Daily operation

1. Review aged receivables for the company cutoff date and timezone. Confirm the AR control difference is zero before relying on totals.
2. Review collection tasks ordered by due follow-up date.
3. Record disputes and promises immediately. An open dispute places the invoice on hold and blocks preparation and delivery.
4. Generate a customer statement when supporting evidence is needed. Downloaded CSV content is checked against its retained SHA-256 hash.
5. Review the prepared reminder, its cited invoice/payment evidence, current open amount, recipient, and approval.
6. Queue the approved send. Treat the resulting state as queued until the background dispatcher records a provider outcome.

## Stale and duplicate protection

Reminder source identity includes the invoice version, amount, effective allocations, open balance, hold/dispute/promise facts, recipient, stage, and optional statement checksum. Both the request service and dispatcher recompute it. A payment, credit/allocation release, recipient update, dispute, hold, promise change, or invoice change blocks the stale draft.

Business idempotency keys are company-scoped and unique for statements, case actions, drafts, sends, and outbox messages. Reusing a key with different business inputs returns a conflict. SQL unique indexes are the final concurrent-write guard.

## Failure and recovery

- `blocked`: current business evidence no longer permits contact. Correct the data and prepare a new reminder; do not retry the old draft.
- `failed`: a safe permanent or retryable mailbox failure was retained. Permanent failures require recipient correction or mailbox reconnection. Retryable failures are retried by the bounded outbox policy.
- `reconciliation_required`: external acceptance is uncertain. Inspect the Sent folder/provider reference and record operational evidence before any resend.
- Blocked worker lease: inspect `last_failure_code` and the safe summary, correct the cause, then run the company-scoped worker endpoint with `resetBlockedLease=true`. The reset is authorized as an accounting-admin action and does not send customer email.
- Missing finance mailbox: connect an active standard SMTP finance mailbox with send capability.
- AR control difference: investigate customer-invoice journals, credits, and payment allocations before producing collection communications.

## Deployment and migration

Apply `AddCustomerCollectionsR2` through the standard SQL Server EF migration path before enabling the worker. Local and Docker SQL Server use the same migration history. Rollback should disable the worker and reminder endpoints and forward-fix the additive schema; do not delete retained statements, actions, approvals, deliveries, or audit evidence.

## Privacy and observability

All queries, commands, worker candidates, statement artifacts, outbox messages, and dispatcher lookups apply explicit company scope. Audit records retain IDs, hashes, stages, outcomes, and safe summaries, not reminder bodies or unnecessary customer data. Technical logs must not contain customer email bodies, credentials, mailbox session material, or raw provider payloads.
