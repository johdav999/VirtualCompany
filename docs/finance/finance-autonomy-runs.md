# Durable Finance autonomy runs

Autonomous Finance activity is persisted as a `FinanceAutonomyRun`. A run is a Finance-specific durable aggregate that links into the shared workflow, task, approval, tool-execution, orchestration, audit, and company-goal systems. It is not a parallel generic workflow engine.

## Immutable execution snapshot

Creation records the company, agent, capability, active grant and grant version, trigger window or authoritative event version, correlation and idempotency keys, evidence snapshot/hash, plan/version/hash, budget snapshot/hash, current policy/catalogue/authority versions, sources, and optional goal/task/workflow/orchestration links. Important searchable state is relational. Bounded snapshot JSON is retained only for metadata that must be reproduced after a restart.

Evidence snapshots accept normalized bounded summaries. Hidden reasoning, credentials, access tokens, and raw provider payloads are rejected. Source identity, version, and content hashes are stored separately so audit linkage survives later redaction.

## Lifecycle

Runs explicitly support `planned`, `validating`, `queued`, `running`, `awaiting_approval`, `reconciling`, `blocked`, `paused`, `completed`, `partially_completed`, `cancelled`, `failed`, `dead_lettered`, and `superseded`. Every transition appends immutable history with a stable reason code, safe summary, actor, correlation ID, and time.

Steps retain ordered dependencies, bounded attempts, lease owner/token/expiry/heartbeat, approval/task/tool-attempt links, policy and authority snapshots, requested-effect identity, actual-effect evidence, and safe outputs. Completed steps cannot be claimed, cancelled, or superseded retroactively.

## Idempotency and recovery

The logical run key is derived from company, active grant version, trigger identity, trigger window, and authoritative event/version. A unique company-scoped index coalesces concurrent duplicate creation attempts.

Workers claim only dependency-ready queued steps. A claim re-evaluates the active autonomy grant and current authority immediately before leasing. The grant version, policy version, authority version/hash, and evidence hash must still match the immutable step snapshot. A mismatch blocks the run before the step begins.

Leases are bounded to 30 minutes, support heartbeats, and are protected by both a changing 16-byte concurrency token and an integer version. An expired lease may be reclaimed only while no outcome has been recorded. Ambiguous effects must enter `reconciling`; they are never blindly repeated. Attempt records are immutable and store only the lease-token hash.

## Cancellation, supersession, replay, and retention

Cancellation and supersession stop incomplete work but never represent completed effects as rolled back. The run retains `HasCompletedEffects` and completed step effect evidence.

Operator replay creates a new run from a checkpoint explicitly marked replayable in the original plan. It re-evaluates the current grant and policy, copies only the checkpoint and subsequent steps, derives a new logical identity, and links the new run and steps to their sources. Redacted runs cannot be replayed because their execution inputs are no longer retained.

Retention redaction is permitted only for terminal runs. It removes evidence, plan, budget, and safe-label content while preserving hashes, statuses, transition history, source identities and versions, approval links, tool-attempt links, and audit records.

## Operator API

Authorized Finance readers can list and inspect runs under:

`/api/companies/{companyId}/finance/autonomy/runs`

Company managers can create/coalesce, transition, bind approval, cancel, supersede, redact, and replay runs. Claims, heartbeats, and attempt completion remain service-layer worker operations and are not exposed as public operator endpoints.
