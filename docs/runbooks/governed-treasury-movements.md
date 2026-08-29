# Governed treasury movements

## Purpose

Use treasury sources for internal account transfers, bank fees or interest, card settlements, and provider payout batches. These sources preserve imported bank rows and provider evidence while keeping `IAccountingPostingService` as the only final journal boundary.

The reconciliation page remains the operator surface. This feature does not create a second treasury ledger.

## Supported source types

| Source type | Required accounting evidence | Posting shape |
| --- | --- | --- |
| `account_transfer` | Outbound and inbound bank rows | Debit destination cash, credit source cash; post a separately identified fee only when configured |
| `bank_adjustment` | One booked bank row | Bank fee/interest expense to cash, or cash to interest income |
| `card_settlement` | Provider or merchant settlement evidence plus a matching booked bank row | Debit net cash and fees, credit gross card receivable |
| `payout_settlement` | Provider batch evidence plus a matching booked bank row | Debit net cash and fees, credit gross payout clearing |

Cross-currency sources are rejected with `treasury_cross_currency_transfer_blocked` until governed currency accounting is delivered.

## Lifecycle and controls

- A transfer with no legs is `needs_review`; a transfer with one leg is `in_transit`. Neither state can post.
- A card or payout settlement is `awaiting_bank_evidence` until booked bank evidence arrives. Provider acceptance alone never means cash settlement.
- A bank amount mismatch remains `needs_review` with the imported row visible. The service never invents a counterpart.
- Material sources move to `awaiting_approval` only after required evidence agrees. Bind only an approved `treasury_source` approval for the same company, source ID, and `sourceType` threshold fact. Its `sourceVersion` threshold fact must authorize the resulting bound version (`expectedVersion + 1`). Bound evidence is immutable; use a correction when evidence changes.
- `ready_to_post` sources expose a deterministic preview. Final posting and reversal always call the accounting posting boundary.
- Evidence changes, approval binding, posting, and reversal use source versions for optimistic concurrency and write source history plus the global audit stream.
- Posted evidence is immutable. Correct it with a linked correction source or a linked journal reversal.

## API operations

All routes are company scoped under:

`/internal/companies/{companyId}/finance/treasury-sources`

- `GET /` and `GET /{sourceType}/{sourceId}` require Finance view access.
- Create routes, bank-evidence linking, and preview require Finance edit access.
- Approval binding, post, and reverse require Finance approval access.

Creation identities and final posting idempotency keys are stable per company, source type, source ID, source version, and action. Replaying a completed post does not create another journal.

## Operator response

1. Open the imported bank row in Accounting reconciliation.
2. Review treasury status, gross/fee/net amounts, every linked bank row, and provider evidence.
3. For `in_transit`, wait for and link the missing bank leg. Do not categorize it as income or expense.
4. For `needs_review`, compare the provider batch with the booked bank amount. Create a correction when the source facts were wrong.
5. For a material source, complete and bind the source-specific approval.
6. Review the debit/credit preview and post only when the control check passes.
7. If a posted result must be corrected, enter an operator reason and create the linked reversal. Confirm the immutable history contains both posting and reversal events.

## Monitoring and incident signals

Telemetry counters use the `VirtualCompany.Finance.Treasury` meter:

- `finance.treasury.sources.created`
- `finance.treasury.sources.posted`
- `finance.treasury.sources.reversed`
- `finance.treasury.actions.blocked`

Investigate sustained growth in `in_transit`, `treasury_bank_amount_mismatch`, or `treasury_bank_transaction_already_linked`. Preserve source evidence and audit records during investigation; do not edit posted journals directly.
