# Native payment batches

## Purpose and boundary

Native payment batches turn eligible supplier payment proposals and approved customer refunds into reviewed, versioned payment instructions. They are internal preparation evidence only.

An `approved` batch has completed Virtual Company approval for its exact instruction set and source hashes. Approval does not contact a bank, create a bank transaction, mark a supplier proposal as exported, mark an invoice or refund as paid, or record provider acknowledgement. Bank submission and provider acknowledgement belong to a later delivery boundary.

## Configuration

The `Finance:PaymentBatches` configuration section controls the deterministic policy:

```json
{
  "Finance": {
    "PaymentBatches": {
      "CutOffHourEuropeStockholm": 14,
      "DualApprovalThreshold": 1000000,
      "ApprovalRole": "owner",
      "SupportedCurrencies": ["SEK", "EUR"],
      "HolidayDates": ["2026-01-01", "2026-01-06", "2026-04-03"]
    }
  }
}
```

Maintain `HolidayDates` as ISO `yyyy-MM-dd` bank-calendar dates before each calendar year begins. Weekends are always excluded. The cutoff is interpreted in `Europe/Stockholm`, including daylight-saving time. A request after cutoff moves the earliest execution date to the next configured business day.

Supported rails are Bankgiro and Plusgiro for SEK, SEPA credit transfer for enabled currencies, and original-method refund instructions sourced from approved native refund evidence. Supplier destinations must be registered through the beneficiary verification endpoint. Each update creates a new verified profile version; prior versions are retained.

## Lifecycle and controls

1. Create a draft with a planned execution date and idempotency key.
2. Add only server-evaluated obligations. Eligibility blocks holds, disputes, settled or duplicate obligations, stale sources, missing or changed beneficiary evidence, unsupported rail/currency combinations, insufficient cash, and invalid business dates.
3. Preview aggregate totals and validation issues without changing state.
4. Validate to create a new immutable instruction-set version, source-set hash, beneficiary snapshots, and an internal JSON manifest artifact.
5. Submit the current validated version for internal approval.
6. Approve or reject under the finance-approval policy. The creator and submitter cannot approve. Amounts at or above `DualApprovalThreshold` require two different approvers.
7. Cancel any batch before a future bank-submission boundary. Cancellation retains all evidence.

Every mutation carries the expected numeric batch version. A stale version returns `payment_batch_version_conflict` and the current version. Every operation carries an idempotency key. Replaying the same key and payload returns the prior batch; using the key with different input returns `payment_batch_idempotency_conflict`.

Changing an obligation or verified beneficiary after validation invalidates the approval binding, current manifest, and current instructions. A submit or approval attempt detects the change, persists the stale state, returns `payment_batch_approval_stale`, and requires regeneration and fresh approval.

## Recovery

- For a validation failure, inspect stable reason codes, correct the source evidence, payment details, calendar, or cash position, and validate again with the latest batch version.
- For a version conflict, reload the batch before retrying. Do not replace the idempotency key unless the business request itself changed.
- For stale approval, use regenerate. This creates a new instruction-set version and supersedes prior artifacts; then submit a new approval request.
- For duplicate-obligation errors, locate the other active batch and remove or cancel that link before retrying.
- Never repair approved evidence with direct database updates. Cancel the batch and prepare a new one.

## Audit and telemetry

Business audit events use target type `payment_batch` and record create, content change, validation, approval request, approval, rejection, cancellation, and blocked stale-evidence decisions. Correlation IDs come from the HTTP request. Metadata includes instruction versions, source identifiers, reason codes, and approval identifiers without exposing full payment destinations.

Runtime metrics are emitted from meter `VirtualCompany.Finance.PaymentBatches`:

- `finance.payment_batches.operations`, tagged by operation and resulting status.
- `finance.payment_batches.blocked`, tagged by operation and stable reason code.
- `finance.payment_batches.obligation_count`, tagged by validation result.

Alert on repeated version or idempotency conflicts, stale-approval blocks, sustained insufficient-cash blocks, and validation failure spikes. Audit records are the business trace; application logs and metrics remain the operational diagnostics.
