# Compliance obligation calendar operations

## Supported launch boundary

The launch implementation generates Swedish VAT-return obligations only from an existing `VatFilingPeriod` that has an explicit `DueDate`. It does not calculate or infer a statutory deadline. Operators must obtain the date from an authoritative source and retain that source outside this workflow before generation.

No authority submission provider is configured. The supported lifecycle is export plus manually retained submission and acknowledgement evidence. The UI and API must continue to state that artifact generation, technical validation, manual-submission evidence, authority receipt, authority approval, and statutory compliance are distinct claims.

## Generation and ownership

1. Confirm the company statutory profile is Sweden (`SE`) and VAT status is `registered`.
2. Confirm the active accounting configuration resolves to the intended immutable policy-pack key, version, and definition hash.
3. Create or update the VAT filing period with an authority-sourced explicit due date.
4. Calculate the VAT return. The calendar reuses that return and links any existing VAT close task for the fiscal period; it does not create a duplicate close authority.
5. Generate obligations with a unique idempotency key. Reusing a key with a different payload is rejected.

Generation snapshots statutory-profile version, policy-pack identity and hash, filing-period boundaries and due date, VAT input/package hashes, owner, and close-task link into a deterministic source hash. A generated item can refresh its source only before preparation.

## Workflow controls

- `generated` → `prepared`: requires a linked calculated VAT return.
- `prepared` → `under_review`: performed by the preparer.
- `under_review` → `approved` or `rejected`: uses finance-approval authorization; the preparer cannot approve their own item.
- `approved` → `exported`: requires the linked VAT return to be locked with a checksum-verified final package.
- `exported` → `manual_submission_recorded`: requires a retained reference and content hash. This does not establish authority receipt.
- Submission evidence must be accepted by a different finance approver before authority evidence can advance the state.
- `manual_submission_recorded` → `authority_received`: requires retained receipt evidence.
- `authority_received` → `authority_approved`: requires separate retained approval evidence.
- Authority rejection is distinct and may create a correction linked in both directions to the original.

Every mutation is company-scoped, version checked, idempotent, audited, and added to the obligation history with the source hash. Reads require accounting-view permission; preparation/export/evidence actions require accounting administration; decisions and evidence review require finance approval.

## Reminders and escalation

The reminder generator creates durable, deduplicated reminder records for open obligations within 14 days. It records `upcoming`, `due_soon`, or `overdue`; overdue items escalate to level 1 and to level 2 after seven days. It does not send external messages. Operations should invoke the reminder endpoint from the approved company-scoped scheduler and monitor `compliance.reminders.generated`.

## Telemetry and incident response

Monitor:

- `compliance.obligations.generated`
- `compliance.obligations.transitions` by action
- `compliance.reminders.generated`
- audit actions beginning `compliance_obligation_`

If a state is disputed, stop further transitions, retain the submitted files, compare their hashes with the database records, inspect history and audit entries, and create a correction rather than altering the original evidence. If an authority response is ambiguous, leave the obligation at `manual_submission_recorded` or `authority_received`; never promote it speculatively.

## Retention, migration, and recovery

Obligations, source snapshots, history, evidence hashes, acknowledgements, reminders, and corrections are accounting evidence. There is no routine purge. Retain them under the active accounting retention policy and legal hold requirements; company deletion remains the only aggregate cascade boundary. A future retention job must be separately reviewed and must never delete evidence referenced by an open, received, approved, rejected, or corrected obligation.

Migrations `20260830210000_AddComplianceObligationCalendar` and `20260830211000_AddComplianceObligationDefinitions` add the nullable filing-period due date, definition snapshots, compliance evidence tables, and tenant indexes. Before rollout, back up the database, apply migrations in timestamp order, verify that existing filing periods remain valid with a null due date, and smoke-test a company with an explicit due date. Rollback drops Prompt 5 tables and the optional due-date column, so export any newly recorded evidence before rollback.

## Human review boundary

Technical verification supports human review only. It is not statutory approval, tax advice, confirmation of filing, or a signed professional opinion. The Swedish release remains `human_accountant_review_pending` until a qualified reviewer signs the exact frozen evidence pack.
