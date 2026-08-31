# Accounting schedule operations

Accounting schedules generate controlled journal occurrences for recurring fixed journals, date allocations, accruals, and prepayments. They use the same accounting authority, fiscal-period, account, dimension, approval, and evidence controls as interactive postings.

## Workflow

1. Create a draft under **Accounting → Reports → Accounting schedules**. Supply balanced posting lines, a cadence, amount basis, effective dates, voucher series, dimensions where applicable, and source evidence.
2. Preview the next occurrence. Preview resolves the current fiscal period and runs the proposed journal through the accounting posting policy without writing a ledger entry.
3. Submit the exact schedule version for approval. The approval binding stores both the version number and payload hash.
4. An authorized approver approves or rejects that binding. Any prospective edit creates a new version and invalidates the prior binding.
5. Activate the approved schedule. The bounded generation worker claims due occurrences and rechecks the current version, approval, account and dimension validity, period state, authority, and source evidence before posting through `IAccountingPostingService`.
6. Review occurrences, exceptions, linked journals, reversals, and reconciliation in the workspace. Pause, resume, end, or regenerate only when the server-provided allowed actions expose that transition.

Posted journals and their occurrence snapshots are immutable. Corrections use linked reversals or a new prospective schedule version; operators must never edit a posted occurrence in the database.

## Worker configuration

`AccountingScheduleWorker` in the API configuration controls the worker:

- `Enabled`: enables due-occurrence processing.
- `PollIntervalSeconds`: delay between bounded scans.
- `ClaimBatchSize`: maximum claims per scan.
- `LeaseSeconds`: claim lifetime. Expired leases can be safely recovered by another process.
- `MaximumAttempts`: retry limit before an occurrence becomes blocked and the schedule is paused.
- `BaseRetryDelaySeconds` and `MaximumRetryDelaySeconds`: bounded exponential retry delay after a transient failure.

Keep batches bounded. Scale with additional API workers only after monitoring database claim contention and posting latency. Stable occurrence source identities, ledger idempotency keys, unique constraints, and leases prevent duplicate journals across retries and process restarts.

## Monitoring and alerts

Monitor the `VirtualCompany.Finance.AccountingSchedules` meter for scan duration, claimed occurrences, posted occurrences, reversals, retries, and blocked occurrences. Alert when:

- blocked or failed occurrences are non-zero for more than one polling interval;
- a due occurrence remains unposted past the applicable period-close cutoff;
- worker scans stop while the worker is enabled;
- posting or reversal retry counts rise repeatedly;
- reconciliation reports released/reversed totals that do not match linked immutable journals.

Period close is blocked by due active schedules, blocked/failed occurrences, and unreconciled posted or reversed occurrences. The close issue links directly to the schedules workspace.

## Recovery

1. Open the affected occurrence and read its reason code, explanation, and safe next action.
2. Correct the underlying control failure: reopen or choose a valid period under the normal approval process, restore account/dimension validity, restore evidence, or obtain a fresh approval for the current version.
3. Use **Regenerate** for a blocked or failed occurrence. This clears only transient execution state; it does not erase audit history or mutate a linked journal.
4. Resume the schedule if it was paused. Choose missed-occurrence generation only when policy and period state permit it.
5. Verify that exactly one journal is linked, any required reversal is linked, and the reconciliation returns to a valid state.

If a process stops after the journal was committed but before the occurrence was finalized, restart normally. The worker recovers the journal by its stable source identity before attempting another post. Do not manually insert an occurrence or journal to unblock processing.

## Deployment

Apply migration `20260829145851_AddAccountingSchedules` before enabling the worker. Deploy application instances with the worker disabled, apply the migration, verify API reads and schedule previews, then enable processing on the intended instances. A rollback must disable the worker first; retain schedule and occurrence tables for audit and reconciliation even if the UI is rolled back.

This runbook supports technical operations and control evidence. It is not a statutory accounting opinion; organization-specific policy and legal conclusions require qualified human review.
