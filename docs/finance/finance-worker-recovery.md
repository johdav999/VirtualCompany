# Finance worker execution and recovery contract

This document is the Release 0 operational inventory for Finance background work. It describes the durable business state owned by each capability; it does not replace those states with an opaque generic queue.

The operator surface is available at `/system/admin/finance-work?companyId={companyId}`. Its API is `GET /api/companies/{companyId}/finance/worker-operations`. Reads require Finance view permission. Retry, stop, and acknowledgement actions require Accounting admin permission, an expected execution version, and a plain-English operator reason. Every action is company-scoped and written to business audit history.

## Shared failure contract

- Validation, authorization, policy, poison payload, and permanent business-rule failures do not retry automatically.
- Concurrency, persistence, object-storage availability, provider rate limiting, definite transport timeout, and transient dependency failures use bounded retry and configured backoff.
- Host cancellation stops the worker loop without converting shutdown into a business failure.
- A timeout after possible provider acceptance is an ambiguous external result. It enters the capability's reconciliation state and is never replayed automatically.
- SQL claims are atomic, include a non-empty company id, retain the lease owner and expiry, and increment an optimistic version. Expired attempts are retained as `lease_expired` before a new attempt resumes.
- Operator stop is allowed only while work is queued and only where no posted, issued, externally successful, or checkpointed fact can be undone. Manual retry resets the bounded attempt budget only after a recorded operator decision; old attempts remain immutable evidence.
- Logs and metrics carry company, worker, execution, correlation, attempt, status, duration, backlog, and retry facts. They must not carry credentials, provider payloads, or source-document content.

## Worker inventory

| Worker | Durable unit and company scope | Trigger / atomic claim / bound | Idempotency and checkpoints | Retry, cancellation, terminal state | Operator path |
|---|---|---|---|---|---|
| Finance setup backfill | Backfill run, company attempt, and Finance seed execution | Scheduled scan; distributed scan lock; bounded pages, enqueues, and company count | Run + company + seed version; deterministic dataset checks | Bounded exponential retry; scan can stop; claimed companies resume checkpoints | Retry eligible seed execution; acknowledge permanent failure |
| Financial report regeneration | Background execution linked to company fiscal period | Close/reopen request; atomic SQL lease; configured claim batch | Company + fiscal period + snapshot version | Transient retry; queued work may stop; immutable snapshots remain | Finance work recovery page |
| Accounting file export | Export job and immutable artifact | Authorized request; capability-owned job claim and bounded due batch | Company + period + format + source checksum | Storage/persistence retry; validation terminal; completed artifact immutable | Accounting operations/export views |
| Historical accounting migration | Migration run, phase, conflicts, and cutover reports | Authorized request; company SQL lease; configured claim/phase batch | Company + target version; phase checkpoint | Expired lease resumes phase; conflicts or exhaustion are visible terminal states | Accounting operations; resolve conflict or rerun |
| Provider switch assessment | Assessment and dataset capability evidence | Switch request; company SQL lease; bounded claim/page | Switch + source evidence version | Transient provider reads retry; invalid evidence terminal | Provider-switch assessment actions |
| Provider switch rehearsal | Rehearsal inputs, dataset results, and reconciliation checks | Approved mapping state; company SQL lease; bounded claim | Switch + plan + evidence hashes | Stale/invalid evidence blocks; safe replay after remediation | Provider-switch rehearsal actions |
| Provider switch preparation | Preparation, readiness checks, candidates, archive dependencies | Approved plan; company SQL lease; bounded claim/save batch | Switch + plan + candidate source identity | Invalid readiness blocks; rejected candidate may replay | Provider-switch preparation actions |
| Provider target transfer | Transfer batch, item, attempt, write request, acknowledgement | Prepared package; company lease and per-item tracker | Stable target identity + operation mode + version | Rate limit/definite transient retry; possible success requires lookup/reconciliation | Replay batch or reconcile item; never blind retry |
| Provider switch cutover | Cutover execution and immutable final checkpoints | Approved schedule; company SQL lease; bounded claim | Switch + plan + boundary; current step persists | Resume safe blocks; cancellation only before irreversible activity; ambiguity reconciles | Resume, recover, cancel, or corrective cutover |
| Provider switch monitoring | Monitoring run, checks, incidents, closure approval | Activated switch and schedule; company SQL lease; bounded claim | Switch + monitoring sequence | Consecutive failures exhaust; closure requires evidence/approval | Run now, retry, accept exception, request closure |
| Approval task backfill | Existing approval/task identities | Scheduled bounded company scan | Company + target record + policy version | Next scan recovers transient failure; created approvals remain governed | Disable worker or run authorized bounded backfill |
| Finance insights snapshot | Background execution and normalized descriptor | Refresh request; atomic company SQL lease; bounded claim | Company + normalized descriptor | Bounded transient retry; queued work may stop | Finance work recovery page |
| Analytics startup refresh | Per-company durable insight executions | Host startup; bounded company enqueue | Company + startup descriptor | Shutdown stops enumeration; queued items retain retry state | Finance work recovery page |
| Integration startup sync | Connection sync state and provider cursor | Host startup; company/connection lock; configured timeout | Company + connection + cursor | Definite transient retry; cursor persists | Connection health/reconnect/sync action |
| Supplier bill registration reconciliation | Provider write request and bill registration state | Startup reconciliation scan; bounded candidate batch | Company + bill source/version + provider command | Provider lookup completes possible success; no blind replay | Supplier bill/provider reconciliation |
| Finance setup execution | Background execution plus deterministic data checks | Company setup request; atomic company SQL lease; bounded claim | Company + seed dataset version | Bounded retry; setup cannot stop after checkpoints begin | Retry eligible execution or acknowledge failure |
| Simulation progression | Simulation run, transitions, day logs | Explicit Simulation Lab run; company run claim; bounded batch | Company + run + virtual day | Resume last completed virtual day; production Finance remains isolated | Pause/stop in Simulation Lab |

## Readiness and telemetry

The `finance-workers` readiness check verifies that every registered Finance worker has an explicit production configuration section. Per-company worker health additionally reports queued count, oldest queued time, active and expired leases, exhausted failures, poison work, and provider reconciliation-required outcomes. A business backlog is operator-visible but is not hidden by successful host liveness.

Metrics use the `VirtualCompany.Finance.Workers` meter:

- `finance.worker.operator_actions`
- `finance.worker.backlog`
- `finance.worker.failures`
- `finance.worker.attempts`
- `finance.worker.attempt.duration`
- `finance.worker.retry.delay`

The metrics use company, worker, outcome, and failure-class tags. Structured attempt logs add the execution and correlation identities, attempt/max-attempt values, lease expiry, duration, and next retry time. High-cardinality execution and correlation identities remain in logs rather than metric dimensions. Neither surface records credentials, provider payloads, or source-document content.

The normal rollout checks are:

1. Apply the SQL Server EF migration through the standard local or Docker startup path.
2. Confirm `/health/ready` reports the `finance-workers` check as ready.
3. Open the Finance work recovery page as a Finance viewer and confirm the queue is company-scoped.
4. Use an Accounting admin account to retry a transient test failure, stop a queued safe refresh, and acknowledge a permanent test failure.
5. Confirm the previous attempts and the three operator audit actions remain visible.
6. Confirm a cross-company execution id returns not found and an operator without Accounting admin permission receives forbidden.
