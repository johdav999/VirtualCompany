# Goal
Implement **TASK-ST-404 — Escalations, retries, and long-running background execution** in the existing .NET modular monolith so that workflow/task processing is resilient, tenant-scoped, retry-aware, and safe for long-running execution.

This task should establish the backend foundation for:
- background worker execution of scheduled jobs, workflow progression, retries, and long-running tasks
- explicit distinction between transient failures vs permanent business/policy failures
- visible exception/escalation records for blocked or failed executions
- reliable outbox-backed side-effect dispatch without duplication
- idempotent retry behavior using correlation IDs / idempotency keys
- tenant-scoped execution and coordination, with Redis used where appropriate for locks or ephemeral execution state

No explicit acceptance criteria were provided beyond the backlog story notes, so derive implementation details from:
- ST-404 backlog entry
- EP-4 architecture guidance
- modular monolith / CQRS-lite / outbox pattern
- PostgreSQL + Redis + ASP.NET Core + background workers stack

# Scope
In scope:
- Add or extend domain/application/infrastructure support for resilient background execution
- Introduce execution state concepts needed for retries, escalations, and long-running processing
- Implement worker orchestration for:
  - scheduled jobs
  - workflow progression
  - retry processing
  - long-running task execution
  - outbox dispatch
- Add failure classification logic:
  - transient/infrastructure failures => retryable
  - permanent business/policy failures => non-retryable, escalated/visible
- Ensure idempotent processing and duplicate-safe outbox dispatch
- Persist enough execution metadata for observability and future UI surfacing
- Add tests covering retry behavior, idempotency, escalation creation, and tenant scoping

Out of scope unless already partially scaffolded:
- Full end-user UI for escalations/exceptions
- Full notification center UX
- Message broker introduction
- Arbitrary workflow builder UX
- Mobile changes
- Large refactors unrelated to ST-404

If the codebase already contains partial implementations for jobs, outbox, workflows, or approvals, extend them rather than replacing them.

# Files to touch
Inspect first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- Domain entities / value objects / enums for:
  - workflow execution state
  - job execution state
  - escalation / exception records
  - retry classification
- Application layer:
  - commands/handlers for progressing workflows and tasks
  - services/interfaces for background execution, retry policy, escalation creation, outbox dispatch
  - tenant-scoped execution contracts
- Infrastructure:
  - EF Core configurations
  - repositories
  - background services / hosted services
  - Redis lock coordination
  - outbox dispatcher
  - clock / correlation / idempotency helpers
- API/bootstrap:
  - DI registrations
  - hosted service registration
  - health/observability wiring if needed
- Tests:
  - unit tests for retry classification and idempotency
  - integration tests for worker processing and duplicate-safe dispatch

Also inspect for existing equivalents before creating new files:
- outbox-related types
- workflow runner / scheduler / hosted services
- audit/event persistence
- approval/escalation/notification models
- correlation ID middleware/utilities

# Implementation plan
1. **Inspect the current architecture and reuse existing patterns**
   - Review solution structure and existing implementations for:
     - background workers / `IHostedService` / `BackgroundService`
     - outbox tables and dispatchers
     - workflow/task entities and handlers
     - audit events
     - tenant context resolution
     - Redis usage
   - Follow existing naming, layering, and persistence conventions.
   - Do not introduce a parallel job framework if a project-native pattern already exists.

2. **Define the execution model for resilient background work**
   - Introduce or extend a persistent execution record for background processing if missing.
   - Minimum metadata should support:
     - execution ID
     - company/tenant ID
     - execution type
     - related entity type/id (`task`, `workflow_instance`, `outbox_message`, etc.)
     - correlation ID
     - idempotency key
     - status
     - attempt count
     - next retry time
     - started/completed timestamps
     - failure category
     - failure code/message
     - escalation/exception linkage
   - Prefer a simple, explicit relational model over a generic opaque blob.

3. **Implement failure classification**
   - Add a classifier/service that maps exceptions or outcomes into categories such as:
     - transient infrastructure failure
     - concurrency/lock contention
     - external dependency timeout/unavailable
     - permanent business rule failure
     - permanent policy/approval failure
     - validation/configuration failure
   - Retry only transient categories.
   - Permanent failures should mark execution as failed and create an exception/escalation record.
   - Keep classification deterministic and testable.

4. **Implement retry policy**
   - Add configurable retry behavior for background executions:
     - max attempts
     - backoff strategy
     - optional jitter
   - Persist retry scheduling in the database.
   - Ensure retries are safe via idempotency keys and correlation IDs.
   - Prevent infinite retry loops.
   - If an execution exceeds max attempts, mark it failed and escalate.

5. **Implement escalation/exception persistence**
   - Add a domain/application concept for blocked or failed executions that need visibility.
   - This can be an explicit exception/escalation entity or a well-structured audit/event record if the codebase already uses one.
   - Persist:
     - tenant/company ID
     - source execution/entity
     - severity
     - reason/category
     - human-readable summary
     - current status (`open`, `acknowledged`, `resolved`, etc. if appropriate)
   - Ensure blocked workflow steps and permanent failures create visible records.

6. **Implement long-running execution handling**
   - Add worker logic that can pick up and process long-running tasks/workflow steps outside the request path.
   - Ensure work is resumable and stateful:
     - mark in-progress
     - heartbeat/update timestamps if useful
     - recover stale/incomplete executions after restart
   - Avoid holding request-scoped state in memory only.
   - Keep execution tenant-scoped at all times.

7. **Implement scheduler / workflow progression worker**
   - Add or extend a background worker that:
     - polls due scheduled jobs
     - progresses workflow instances
     - enqueues or executes retryable work
   - Use Redis distributed locking or equivalent coordination to avoid duplicate processing across instances.
   - Ensure lock scope includes tenant/entity identity where appropriate.
   - Keep polling intervals and batch sizes configurable.

8. **Harden outbox-backed side effects**
   - Review existing outbox implementation.
   - Ensure dispatcher behavior is:
     - idempotent
     - duplicate-safe
     - retry-aware
     - status-tracked
   - Add message dispatch state transitions such as:
     - pending
     - in_progress
     - dispatched
     - failed
     - retry_scheduled
   - Ensure duplicate dispatch is prevented even if the worker crashes mid-flight.
   - Preserve correlation IDs from originating task/workflow into outbox records where possible.

9. **Wire tenant-scoped execution**
   - Ensure every worker operation resolves and enforces `company_id`.
   - Queries for due work must be tenant-safe.
   - Any created audit/escalation/outbox records must carry tenant context.
   - Avoid any cross-tenant batching logic that could leak data or context.

10. **Add observability hooks**
   - Add structured logs for:
     - execution start/end
     - retry scheduled
     - retry exhausted
     - escalation created
     - outbox dispatch success/failure
   - Include:
     - correlation ID
     - execution ID
     - company ID
     - related entity type/id
   - Keep technical logs separate from business audit records.

11. **Register services and configuration**
   - Update DI and app startup to register:
     - retry policy service
     - failure classifier
     - background workers
     - distributed lock abstraction
     - outbox dispatcher
   - Add configuration options for:
     - polling intervals
     - retry limits
     - backoff
     - stale execution timeout
     - batch sizes

12. **Testing**
   - Add unit tests for:
     - failure classification
     - retry scheduling/backoff
     - idempotency behavior
   - Add integration tests for:
     - transient failure => retry scheduled
     - permanent failure => escalation created, no retry
     - max retries exceeded => escalation created
     - duplicate outbox dispatch prevented
     - tenant-scoped processing
   - Prefer deterministic tests with fake clock / fake dispatcher / fake lock where possible.

13. **Document assumptions in code comments or README notes if needed**
   - If ST-404 requires introducing new persistence objects or worker conventions, add concise documentation near the implementation or in project docs.
   - Keep docs minimal and implementation-focused.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify worker behavior through tests or existing integration harness:
   - transient execution failures are retried
   - permanent business/policy failures are not retried
   - blocked/failed executions create escalation/exception records
   - outbox messages are dispatched exactly-once from the application perspective, or at minimum duplicate-safe/idempotent
   - retries preserve correlation IDs and idempotency keys
   - tenant-scoped records are never processed across tenants

4. If there are migrations in this repo’s current pattern, generate/apply them using the project’s established approach.
   - Do not use the archived migration docs as the active source of truth unless the repo clearly indicates that pattern.
   - Ensure schema changes compile and tests pass.

5. Manually review logs/assertions for:
   - execution lifecycle logging
   - retry exhaustion logging
   - escalation creation logging
   - outbox dispatch logging

6. Confirm no request-path regression:
   - long-running work should execute in background services, not block API requests
   - task/workflow APIs should remain responsive

# Risks and follow-ups
- **Existing infrastructure mismatch:** The repo may already have partial worker/outbox patterns. Reuse them to avoid fragmentation.
- **Schema uncertainty:** ST-404 may require new tables/entities for execution tracking or escalations. Keep schema minimal and aligned with existing domain language.
- **Exactly-once semantics:** True exactly-once delivery is difficult; implement idempotent, duplicate-safe dispatch and document the guarantee level in code/tests.
- **Distributed coordination complexity:** Redis locking must avoid deadlocks and stale locks; prefer short leases and safe renewal only if needed.
- **Long-running recovery:** Crash recovery for in-progress executions can be subtle. At minimum, detect stale executions and make them retryable or escalated.
- **UI gap:** This task should persist visible exceptions/escalations even if the UI story lands later.
- **Future follow-up likely needed:** dedicated exception inbox/dashboard surfacing, richer retry policies per workflow type, and broker-based scaling if throughput grows.