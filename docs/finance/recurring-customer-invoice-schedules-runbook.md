# Recurring customer invoice schedules

Recurring schedules create native invoice drafts only. They never allocate a document number, post a journal, or deliver a document directly.

Operators create a schedule with a named customer, a typed monthly, quarterly, or yearly cadence, an IANA/Windows timezone supported by the host, billing day, business-day convention, due-date offset, tax-backed line template, and evidence references. A billing day beyond the length of a month resolves to that month’s final day; a 29th-day annual schedule resolves to February 28 in non-leap years. Following and preceding conventions only adjust weekends; public-holiday calendars are not implied. A preceding adjustment is always advanced from the nominal billing month, so it cannot repeat the same adjusted date. Due eligibility is evaluated against the schedule's local calendar date, while leases and retries use UTC.

`daily` proration applies only to the first partial billing period. It charges calendar days from the effective start date up to the first nominal billing boundary, divided by the calendar days in that billing period; subsequent occurrences use the full template amount. `none` always uses the full amount. The preview uses the same factor and explains it.

Every template has a SHA-256 hash and monotonic template version. Submission creates an approval request bound to that exact pair; editing the template cancels a pending request and invalidates every earlier approval. Activation and resume require the current template approval. The approval covers generation of reviewable drafts only and does not approve invoice issue, numbering, posting, or delivery.

The worker claims at most the configured batch size and one due occurrence per schedule on each pass. Its occurrence identity is `(company, schedule, occurrence date)` and the draft idempotency key includes the retained template version and hash. A reclaimed lease therefore finds the same draft rather than creating a duplicate. The occurrence advances only when the lease owner durably marks the occurrence generated in the same save as the schedule advance. A long pause does not create an unbounded catch-up batch.

Before creating a draft, the worker runs the same authoritative calculation plus current customer, evidence, tax, currency, statutory, accounting-policy, credit, date, and delivery checks used by native invoicing. A permanent failure therefore creates no draft: the occurrence becomes `blocked`, the schedule pauses, and a high-priority Finance task is created when task creation is available. A defensive post-create readiness check covers facts that change during generation; if it finds a new permanent blocker, the still-unissued draft is discarded and linked to the blocked occurrence for investigation.

Operators correct the schedule or current customer facts, then explicitly resume with `RetryBlockedOccurrence`. Resume normally skips past occurrences in the schedule’s timezone; generating a past occurrence requires the accounting administrator to explicitly set `AllowBackdatedGeneration`. Transient infrastructure failures set a durable `NextAttemptUtc` with bounded exponential delay and retry only up to `MaximumAttempts`; exhaustion becomes a visible blocker. Replays by an expired or different lease owner cannot complete the occurrence.

Preview is read-only and allocates neither occurrence records nor invoice numbers. It evaluates up to 24 future dates through the current authoritative policy and returns server-calculated net, tax, and gross totals, warnings, blockers, and a cadence/proration explanation.

Auto-issue is retained as an explicit schedule policy fact, but the current scheduler intentionally remains draft-only. Any future auto-issue implementation must pass the generated draft through the existing approval and issue boundary with a current company policy, authority, approval, number series, period, and delivery recheck.

## Operations

- `CustomerInvoiceScheduleGenerationWorker:ClaimBatchSize` bounds work per pass. `LeaseSeconds` bounds ownership; `MaximumAttempts`, `BaseRetryDelaySeconds`, and `MaximumRetryDelaySeconds` bound transient recovery.
- Monitor `VirtualCompany.Finance.CustomerInvoiceSchedules` operation and occurrence counters, especially `blocked`, `retry_scheduled`, and `lease_lost` outcomes. Review the schedule's recent occurrences and linked Finance task before retrying.
- Pausing or disabling the worker is safe. Do not delete occurrence rows or generated drafts. Restore service and let expired leases replay through the stable occurrence and draft keys.
- During deployment, apply `CompleteRecurringCustomerInvoiceSchedulesR2` before enabling the worker. Upgraded legacy schedule templates receive migration evidence values and must be edited, resubmitted, and approved before generation resumes.
- Roll back application behavior by disabling the worker and forward-fixing. Preserve additive schedule, occurrence, approval, task, audit, and draft evidence.
