# Year-end rollover and recovery runbook

## Purpose and invariants

This runbook covers the governed fiscal-year rollover. The source fiscal year is evidence, never an editable staging area. Execution creates journals in the first period of the next fiscal year and retains exact links to the approved readiness hash. Operators must never update or delete prior-year journals, final report snapshots, close snapshots, audit packages, sign-offs, or year-end history to resolve an incident.

The non-negotiable invariants are:

- all source periods are closed and reporting locked;
- the final close-readiness snapshot, report suite, compliance state, audit package, configuration, and journal cutoff are current;
- preparer and approver are different people;
- the opening and retained-earnings journals use stable source identities and commit in one serializable database transaction;
- opening balances match retained candidates by account, source currency, and dimension facts before finalization;
- later discoveries use either a linked post-forward journal or the approved close-reopen workflow.

## Normal operation

1. Open **Accounting → Year-end** and prepare the source fiscal year. Confirm the target is the immediate, open first period of the next fiscal year and that retained-earnings and clearing accounts are distinct active equity accounts.
2. Resolve every blocking check. Refresh readiness after changing any source. A refresh creates a new evidence hash and invalidates earlier approval authority.
3. The preparer submits the exact snapshot. A different authorized finance reviewer approves that same hash.
4. Execute once. The service re-evaluates authoritative evidence inside a serializable transaction, then posts the opening and retained-earnings journal chain through the central accounting posting service.
5. Reconcile. The service reads posted lines by retained candidate ID and compares account, source currency, dimension key, and signed functional amount.
6. Finalize only when the difference is zero. Retain the checksum and linked journal IDs with the run.

## Expected telemetry and audit evidence

- Activity source and meter: `VirtualCompany.YearEndRollover`.
- Counters: `year_end.operations`, tagged by operation; `year_end.blockers`.
- Histogram: `year_end.evaluation.duration` in milliseconds.
- Audit targets: `year_end_run` and `year_end_subsequent_event`.
- Audit actions cover prepare, submit, review, execute, reconcile, finalize, failure, event review, and correction linkage.
- Database recovery receipts are in `year_end_operations`; approval and historical evidence are in the corresponding sign-off and history tables.

Alert on repeated posting failures, persistent non-zero reconciliation differences, unusually slow evaluation, a rising blocker count near the reporting deadline, or multiple idempotency conflicts for one company/run.

## Execution failure or timeout

1. Do not create a manual replacement journal until the run and journal source identities have been inspected.
2. Read the run state, failure code, history, and operation receipt using the company-scoped API or workspace.
3. Look up journals by source type and run source ID:
   - `year_end_opening_balance`;
   - `year_end_retained_earnings`.
4. If neither journal exists and the run reports `year_end_posting_failed`, the transaction rolled back. Refresh readiness and repeat approval if the authoritative hash changed; otherwise retry with the same request and idempotency key when the original outcome is uncertain.
5. If a journal exists, do not retry with a new key. Investigate the retained operation receipt and posting idempotency result. A partial journal chain indicates a severe transaction-boundary defect and must be escalated before any accounting correction.

## Reconciliation mismatch

1. Keep the run failed; activation/finalization must remain blocked.
2. Compare `year_end_opening_balance_candidates` with posted lines carrying the `year_end_candidate_id` fact. Check account ID, functional amount, source currency, and the complete dimension-facts JSON.
3. Confirm no source journal, dimension assignment, currency fact, or account lifecycle configuration changed after approval. Such a change requires a refreshed readiness snapshot and new independent approval, not editing retained evidence.
4. If the generated journal is wrong, use the normal immutable correction/reversal mechanisms in the next open period and link the correction through a subsequent-event record. Do not alter the posted journal.

## Later evidence and reopening

- **Disclose only:** record, submit, independently review, and resolve the event without a journal or reopen link.
- **Post forward:** create and post the correction through the normal accounting posting flow in a later open period, then link that same-company journal to the approved event.
- **Request reopen:** use the accounting-close governance workflow. The reopen request must be independently approved/executed and match the same company before it can resolve the event. Prepare replacement close/year-end evidence after corrections.

Document access remains governed by the source document’s own access policy. A year-end reference never broadens access.

## Database deployment and rollback

Migration `20260830240000_AddFormalYearEndRollover` is additive and creates nine `year_end_*` tables. Before deployment, generate and review SQL from the last deployed migration, verify the migration appears after `20260830230000_AddExternalAccountantCollaboration`, and confirm `dotnet ef migrations has-pending-model-changes` reports no model drift after a rebuild.

Application rollback is safe before any year-end execution because the new schema is additive. After execution, keep the schema and data even if the application is rolled back; do not run the migration `Down` path in production because it would remove retained statutory/audit evidence. Restore service by forward-fixing the application.
