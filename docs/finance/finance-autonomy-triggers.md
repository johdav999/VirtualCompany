# Scheduled and event-driven Finance autonomy triggers

Finance autonomy triggers turn reviewed schedules and a narrow set of authoritative business events into durable `FinanceAutonomyRun` records. They are an intake layer between active autonomy grants and the run engine; they do not execute provider operations directly and do not bypass grant, policy, authority, approval, or run-step checks.

## Reviewed trigger policy

Each active grant version must explicitly allow `schedule`, `business_event`, or both. Scheduled grants retain a validated cron expression, company timezone, local operating window, and a bounded `skip` or `latest` catch-up policy. Event-driven grants must name one or more of the following normalized event types:

- `new_uncategorized_transaction`
- `overdue_receivable`
- `stale_cash_evidence`
- `close_task_blocker_changed`
- `reconciliation_failed`
- `import_failed`
- `compliance_obligation_expiring`
- `background_work_completed`

The grant also fixes the minimum interval, maximum runs per company-local window, debounce duration, maximum catch-up windows, and late-event tolerance. These values are reviewed and versioned with the grant; operators cannot widen them through the trigger API.

## Schedule behavior

The worker evaluates cron schedules in the grant's IANA or Windows timezone. Company-local windows are converted to UTC with explicit handling for invalid and ambiguous local times, so spring-forward gaps do not invent work and fall-back overlaps resolve deterministically. The persistent cursor stores the last considered occurrence and quota window. Restarts therefore do not recreate already-considered windows.

`skip` begins with the next occurrence after the worker resumes. `latest` considers only the latest missed occurrence, subject to the reviewed catch-up-window bound. A pause, expired/revoked grant, operating-policy denial, or disabled `FinanceAutonomyTriggers` worker starts no new Finance work. The existing role cadence routes Finance schedules through this same durable service and observes the worker's disabled setting.

## Event intake, deduplication, and coalescing

Owning Finance adapters emit normalized signals through the company outbox. Receipts contain authoritative event identity and version, source entity identity, observed time, correlation ID, a SHA-256 content hash, and an optional safe label. Raw provider payloads, credentials, and access tokens are not accepted.

A company/grant/event/coalescing-key cursor is claimed with an optimistic concurrency token and a two-minute lease. A unique receipt key deduplicates authoritative event versions across hosts. Events inside the fixed debounce/minimum-interval boundary coalesce into the existing durable run; their source references are appended to that run up to the bounded retention cap. Coalescing does not slide the eligibility boundary, so a continuous event burst cannot postpone the next bounded window forever.

Late or future-dated events outside tolerance are dead-lettered as evidence and require a new authoritative version. Window quotas and minimum intervals are evaluated before a new run is created. Each accepted trigger creates only a conservative `validate_and_prepare` step; current grant and evidence checks still occur when the run worker claims it.

## Failure and recovery

Lease conflicts are safe no-ops and allow the owning host to finish. Processing failures use a five-minute retry delay and dead-letter the cursor after three consecutive failed claims. A successful or policy-suppressed receipt resets the consecutive-failure counter. Error summaries are bounded and exclude provider payloads.

Finance readers can inspect cursors and safe event receipts at:

`GET /api/companies/{companyId}/finance/autonomy/triggers`

A company manager can release a dead-lettered cursor at:

`POST /api/companies/{companyId}/finance/autonomy/triggers/{cursorId}/retry?expectedVersion={version}`

Recovery uses optimistic version checking, clears the cursor failure state, resets the latest processing-dead-letter receipt to `received`, and writes an audit event. The next authoritative/outbox delivery then performs one bounded retry under the current active grant. Late-event dead letters are not reset because stale evidence must not become current eligibility.

## Operations

`FinanceAutonomyTriggers:Enabled` is the maintenance switch. `PollIntervalSeconds` is clamped to 5–3600 seconds and `BatchSize` to 1–100. Trigger state is tenant-scoped, while the worker deliberately uses explicit company identity when evaluating active grants. Useful operational fields include status, cursor/event version, company-local window, runs in window, next eligibility, lease expiry, attempt count, failure code, last run, and updated time.

Migration `20260901161417_ImplementScheduledAndEventDrivenFinanceTriggers` is additive. It adds the reviewed grant limits plus the trigger cursor and event tables, their tenant-scoped foreign keys, unique deduplication keys, query indexes, and fixed binary concurrency token. Rollback removes only this trigger layer and its added grant columns; existing grants and durable runs remain governed by their earlier migrations.
