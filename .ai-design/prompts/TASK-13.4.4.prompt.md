# Goal
Implement backlog task **TASK-13.4.4 — Add retry and dead-letter handling for failed trigger execution jobs** for story **ST-704 - Trigger evaluation engine and auditability** in the existing .NET modular monolith.

The implementation must add resilient background processing for trigger execution jobs so that:

- a recurring background worker polls for due scheduled triggers and pending condition checks
- each trigger execution attempt uses an idempotency key to prevent duplicate orchestration starts
- every execution attempt writes business audit records with:
  - trigger id
  - trigger type
  - agent id
  - tenant/company id
  - execution status
  - correlation id
- policy checks run before orchestration start
- blocked executions are recorded with denial reason
- worker failures are retried using configured retry policy
- exhausted failures are dead-lettered or otherwise marked terminal and visible in audit records

Assume the codebase follows clean architecture boundaries across `Domain`, `Application`, `Infrastructure`, and `Api`, and preserve tenant isolation throughout.

# Scope
In scope:

- Add or extend a background worker that runs on a recurring polling interval
- Evaluate:
  - due scheduled triggers
  - pending condition checks
- Introduce retry handling for failed trigger execution jobs
- Introduce dead-letter / terminal failure handling after retry exhaustion
- Add idempotency protection per trigger execution attempt so orchestration is not started twice
- Persist audit records for all outcomes:
  - started
  - succeeded
  - blocked by policy
  - retry scheduled
  - failed
  - dead-lettered / terminal failure
- Ensure policy checks happen before orchestration start
- Record denial reason for blocked executions
- Surface retry attempt count and failure visibility in audit data
- Add tests covering retry, idempotency, auditability, and dead-letter behavior

Out of scope unless required by existing design:

- New UI screens
- Mobile changes
- Full message broker introduction
- Re-architecting the worker framework
- Broad workflow engine redesign unrelated to trigger execution reliability

# Files to touch
Inspect the solution first and then touch the minimum necessary files in the relevant layers. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - trigger execution entities/value objects/enums
  - audit domain models
  - retry/dead-letter status concepts
- `src/VirtualCompany.Application/**`
  - trigger evaluation services/handlers
  - orchestration start command/service
  - policy check integration
  - idempotency coordination
  - audit record creation
  - retry policy abstractions/options
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence mappings/repositories
  - background worker implementation
  - polling scheduler
  - distributed locking / coordination if already present
  - retry persistence and dead-letter persistence
  - migrations or schema updates if this repo uses active migrations here
- `src/VirtualCompany.Api/**`
  - DI registration / hosted service wiring
  - configuration binding for polling interval and retry policy
- `tests/VirtualCompany.Api.Tests/**`
  - integration and/or application-level tests for worker polling, retries, audit records, and idempotency

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- any existing trigger/workflow/audit/outbox/worker-related files discovered during repo exploration

If schema changes are needed, add them in the project’s established migration pattern rather than inventing a new one.

# Implementation plan
1. **Explore the existing implementation before changing anything**
   - Find current models and services for:
     - workflows
     - triggers
     - background workers
     - audit events
     - orchestration start
     - policy checks
     - correlation IDs
     - idempotency or outbox patterns
   - Identify whether scheduled triggers and condition checks already exist partially under different names.
   - Reuse existing abstractions and naming conventions.

2. **Define the trigger execution lifecycle**
   Add or extend a domain/application model for trigger execution attempts with explicit statuses, for example:
   - `Pending`
   - `InProgress`
   - `Blocked`
   - `Succeeded`
   - `RetryScheduled`
   - `Failed`
   - `DeadLettered`

   Each attempt should capture at minimum:
   - execution attempt id
   - trigger id
   - trigger type
   - company/tenant id
   - agent id
   - correlation id
   - idempotency key
   - attempt number
   - status
   - denial reason if blocked
   - failure reason / exception summary if failed
   - timestamps

   If the architecture already uses `audit_events` as the primary business audit store, ensure these details are persisted there and add a dedicated execution table only if necessary for reliable retry tracking.

3. **Implement recurring polling worker**
   Create or extend a hosted background service that:
   - runs on a configurable polling interval
   - acquires a distributed lock if the app may run on multiple instances
   - fetches due scheduled triggers and pending condition checks in a tenant-safe way
   - claims work items atomically to avoid duplicate processing across worker instances
   - processes a bounded batch size per poll if appropriate

   Configuration should be added via options, e.g.:
   - polling interval
   - batch size
   - max retry attempts
   - retry backoff settings

4. **Add idempotency protection**
   Before starting orchestration:
   - generate or resolve a deterministic idempotency key per trigger execution attempt
   - check whether an orchestration has already been started for that key
   - if already started, do not start another one
   - still write an audit record indicating duplicate prevention / already processed outcome if appropriate

   Prefer a persistence-enforced uniqueness guarantee where possible, such as:
   - unique index on idempotency key for orchestration start records
   - transactional check-and-insert pattern

   Do not rely only on in-memory checks.

5. **Run policy checks before orchestration**
   Integrate the existing policy guardrail engine before orchestration start:
   - evaluate tenant scope, agent permissions, autonomy level, thresholds, and approval requirements as already defined in the architecture/story
   - if denied or blocked:
     - do not start orchestration
     - mark execution as blocked/permanent non-retryable
     - record denial reason in audit data
   - classify policy/business denials as non-transient so they do not consume retry attempts unnecessarily

6. **Classify failures for retry behavior**
   Introduce a clear distinction between:
   - transient failures:
     - database timeout
     - network hiccup
     - temporary infrastructure dependency issue
     - lock contention / temporary coordination issue
   - permanent failures:
     - policy denial
     - invalid trigger configuration
     - missing required agent/tenant data
     - duplicate already-started orchestration where no further action is needed

   Retry only transient failures according to configured policy.

7. **Implement retry scheduling**
   For transient failures:
   - increment attempt count
   - compute next retry time using configured policy
   - persist retry state durably
   - write audit records for the failed attempt and scheduled retry

   Support a simple backoff strategy if no existing framework exists, such as:
   - fixed delay or exponential backoff with cap

   Ensure retries survive process restarts.

8. **Implement dead-letter / terminal failure handling**
   When max retry attempts are exhausted:
   - mark the execution as dead-lettered or terminally failed
   - persist final failure details
   - write audit records showing:
     - final status
     - attempt count
     - correlation id
     - failure summary

   If the codebase already has an exceptions/escalations concept from workflow execution, integrate with it rather than creating a parallel mechanism.

9. **Write business audit records for every outcome**
   Ensure all trigger executions produce audit records containing the required fields:
   - trigger id
   - trigger type
   - agent id
   - tenant/company id
   - execution status
   - correlation id

   Also include where possible:
   - attempt number
   - idempotency key
   - denial reason
   - failure summary
   - retry scheduled at
   - dead-letter flag/final failure marker

   Keep audit records business-facing and concise; do not store raw chain-of-thought or overly verbose exception dumps.

10. **Persist schema changes**
    If needed, add schema support for:
    - trigger execution attempts
    - retry metadata
    - dead-letter status
    - unique idempotency key constraint
    - audit event field extensions

    Follow the repository’s existing PostgreSQL migration approach.

11. **Register services and configuration**
    Update DI and configuration binding for:
    - hosted worker
    - retry policy options
    - polling interval options
    - repositories/services added
    - any lock provider or clock abstraction used

12. **Add tests**
    Add automated tests covering at least:
    - worker polls and picks up due scheduled triggers
    - worker polls and picks up pending condition checks
    - policy denial prevents orchestration start and records denial reason
    - duplicate processing does not start orchestration twice for the same idempotency key
    - transient failure schedules retry and writes audit records
    - retry exhaustion results in dead-letter/terminal failure status
    - audit records contain required fields
    - permanent failures are not retried
    - retry state survives subsequent poll cycles

13. **Keep implementation production-safe**
    - Use cancellation tokens correctly in background services
    - Avoid unbounded loops or unbounded batch processing
    - Ensure tenant/company scoping on all queries
    - Prefer transactional updates for claim/process/audit state transitions
    - Log technical failures separately from business audit events

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests to verify:
   - recurring polling behavior
   - due trigger selection
   - pending condition check selection
   - idempotency duplicate prevention
   - policy-blocked execution audit trail
   - transient retry scheduling
   - dead-letter after retry exhaustion
   - required audit fields present on all outcomes

4. If configuration-driven, verify default options are bound correctly for:
   - polling interval
   - max retry attempts
   - retry delay/backoff

5. If migrations are added, verify they are consistent with the project’s migration workflow and that the application still starts cleanly.

6. Manually validate code paths by tracing one example of each:
   - successful trigger execution
   - blocked by policy
   - transient failure then retry success
   - transient failure then dead-letter
   - duplicate attempt prevented by idempotency key

# Risks and follow-ups
- **Unknown existing trigger model:** The repo may already have partial trigger/workflow infrastructure under different names. Reuse it instead of duplicating concepts.
- **Migration approach ambiguity:** The workspace references archived PostgreSQL migration docs; confirm the active migration mechanism before adding schema changes.
- **Distributed execution risk:** If multiple app instances run workers, duplicate processing can occur without atomic claim logic or distributed locks.
- **Audit schema mismatch:** Existing `audit_events` may not yet contain all required fields; extend carefully without breaking existing consumers.
- **Retry classification drift:** Be explicit about transient vs permanent failures so policy denials and invalid configs do not retry forever.
- **Idempotency race conditions:** Enforce uniqueness at the database level where possible; application-only checks are insufficient.
- **Dead-letter UX visibility:** This task should at least persist dead-letter visibility in audit records, but a later follow-up may be needed for operator-facing dashboards/inbox surfacing.
- **Follow-up suggestion:** If not already present, add a dedicated operator query/API for failed/dead-lettered trigger executions and escalations in a future task.