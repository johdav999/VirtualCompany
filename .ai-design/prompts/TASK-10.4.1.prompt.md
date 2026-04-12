# Goal

Implement `TASK-10.4.1` for `ST-404` by adding resilient background execution infrastructure in the .NET modular monolith so the system can reliably run scheduled jobs, progress workflows, retry transient failures, surface failed/blocked executions as visible exceptions/escalations, and dispatch outbox-backed side effects without duplication.

This task should align with the architecture and backlog expectations for:
- background workers as a first-class subsystem
- tenant-scoped execution
- PostgreSQL as source of truth
- Redis for coordination/locking where appropriate
- outbox pattern for reliable side effects
- workflow/task/approval domain boundaries
- structured observability and idempotent retries

There are no explicit story acceptance criteria beyond the backlog notes, so implement to satisfy the story and implied architecture constraints.

# Scope

Include:
- Background worker hosting and registration in the API host or appropriate worker host within the existing solution.
- A scheduler worker that discovers due scheduled workflow triggers/jobs and enqueues or executes them safely.
- A workflow progression worker that advances workflow instances/steps outside the request path.
- Retry handling that distinguishes:
  - transient/technical failures => retry with backoff
  - permanent business/policy failures => mark failed/blocked without retry storm
- Long-running task execution support with persisted execution state and correlation/idempotency support.
- Reliable outbox dispatch with deduplication/idempotent processing semantics.
- Visible exception/escalation creation or persistence for blocked/failed executions using existing domain concepts where available; if no dedicated exception entity exists yet, implement the minimal durable representation needed and wire it so it is queryable later.
- Tenant-scoped execution and locking.
- Structured logging/telemetry for worker runs, retries, failures, and dispatch outcomes.
- Tests for core retry classification, idempotency, and worker behavior.

Do not include:
- Full UI for exception management unless minimal plumbing is required.
- A new external message broker.
- Arbitrary workflow builder UX.
- Broad refactors unrelated to background execution.
- Mobile changes.

If the codebase already contains partial worker/outbox/workflow infrastructure, extend and standardize it rather than replacing it.

# Files to touch

Inspect first, then update the most relevant files under these projects:

- `src/VirtualCompany.Api/`
  - `Program.cs`
  - hosting/DI/configuration files for worker registration
  - appsettings files if worker options need configuration
- `src/VirtualCompany.Application/`
  - workflow orchestration services
  - task/workflow commands and handlers
  - retry policy abstractions
  - outbox dispatch application services
  - execution result/error classification models
- `src/VirtualCompany.Domain/`
  - workflow/task/outbox/escalation domain models
  - enums/value objects for execution status, failure type, retry state
  - domain events if needed
- `src/VirtualCompany.Infrastructure/`
  - EF Core persistence for worker-owned entities
  - repositories
  - background worker implementations
  - Redis locking/coordinator implementation if Redis is already wired
  - outbox dispatcher implementation
  - clock/idempotency/correlation helpers
  - migrations or persistence mappings if schema changes are required
- `tests/VirtualCompany.Api.Tests/`
  - integration tests for hosted worker behavior where feasible
- potentially add or update tests in:
  - `tests/VirtualCompany.Application.Tests/` if present
  - `tests/VirtualCompany.Infrastructure.Tests/` if present

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If migrations are needed, place them in the project’s current migration location/pattern rather than inventing a new one.

# Implementation plan

1. **Inspect existing architecture and conventions**
   - Review solution structure, DI setup, EF Core DbContext(s), existing workflow/task/approval/outbox models, and any current hosted services.
   - Identify whether there is already:
     - an outbox table/entity
     - workflow instance persistence
     - approval linkage
     - notification dispatch plumbing
     - Redis integration
     - correlation/idempotency helpers
   - Follow existing naming, layering, and module boundaries.

2. **Define the execution model**
   - Introduce or complete a durable background execution model sufficient for:
     - scheduled job discovery
     - workflow progression attempts
     - retry metadata
     - long-running execution tracking
     - failure classification
   - Prefer extending existing workflow/task entities first.
   - Add minimal supporting types such as:
     - `ExecutionStatus`
     - `FailureCategory` or equivalent (`Transient`, `PermanentBusiness`, `PermanentPolicy`, `Unknown`)
     - retry counters / next-attempt timestamps
     - correlation ID / idempotency key
   - Ensure all tenant-owned records include `company_id`.

3. **Implement retry classification and policy**
   - Add an application/infrastructure abstraction to classify exceptions and execution outcomes.
   - Rules should distinguish:
     - transient infrastructure issues (timeouts, network, deadlocks, temporary provider failures) => retry
     - policy denials / invalid workflow definitions / missing required approvals / business rule violations => no retry
     - blocked states => persist as blocked/escalated, not endlessly retried
   - Add bounded retry policy with backoff.
   - Persist retry metadata so retries survive process restarts.

4. **Implement scheduler worker**
   - Add a hosted background service that periodically scans for due scheduled workflow triggers/jobs.
   - Use distributed locking if Redis locking exists; otherwise use a safe DB-based claim/update pattern.
   - Ensure scheduler execution is idempotent:
     - same due trigger should not create duplicate workflow instances
     - use correlation/idempotency keys
   - Scope all discovered work by tenant/company.

5. **Implement workflow progression worker**
   - Add a worker that claims runnable workflow instances/tasks and advances them step-by-step.
   - Persist state transitions and attempt metadata.
   - Respect blocked states such as:
     - awaiting approval
     - missing dependency
     - policy restriction
   - On blocked/failed execution, create a durable exception/escalation record or equivalent auditable marker if no dedicated table exists yet.
   - Keep progression outside HTTP request flow.

6. **Implement long-running task handling**
   - Support execution that may span multiple polling cycles.
   - Persist enough state to resume safely after restart.
   - Ensure a claimed execution cannot be processed concurrently by multiple workers.
   - If there is already a task/workflow runner abstraction, extend it rather than duplicating orchestration logic.

7. **Implement or harden outbox dispatcher**
   - Ensure outbox-backed side effects are dispatched by a dedicated worker.
   - Add deduplication/idempotent dispatch semantics:
     - mark claimed/processed safely
     - avoid duplicate sends on retry/restart
   - Persist dispatch attempts and failure metadata.
   - Retry transient dispatch failures only.
   - Keep side effects out of request path.

8. **Add visible exception/escalation persistence**
   - If the domain already has an exception/escalation/alert/notification concept, use it.
   - Otherwise add the smallest durable model needed to represent:
     - company
     - source entity type/id
     - failure/block reason
     - severity/status
     - created/resolved timestamps
     - correlation ID
   - Make sure this is suitable for later inbox/dashboard surfacing.

9. **Wire DI and configuration**
   - Register hosted services and supporting services in the host project.
   - Add options for polling intervals, batch sizes, retry limits, and lock durations.
   - Keep defaults conservative and test-friendly.

10. **Add persistence mappings and migrations**
   - Update EF Core configurations and create migration(s) if schema changes are required.
   - Preserve tenant scoping and indexes for:
     - due work lookup
     - retry lookup
     - outbox dispatch lookup
     - correlation/idempotency uniqueness where appropriate

11. **Add observability**
   - Structured logs must include:
     - worker name
     - company/tenant context where applicable
     - workflow/task/execution IDs
     - correlation ID
     - attempt number
     - failure category
   - Emit clear logs for claim/start/success/retry/fail/block/dispatch duplicate-skipped paths.

12. **Add tests**
   - Unit tests for retry classification and backoff behavior.
   - Unit tests for idempotency/deduplication logic.
   - Integration tests for:
     - due scheduled work creates one workflow instance only
     - transient failure retries and eventually succeeds/fails after max attempts
     - permanent policy/business failure does not retry
     - blocked execution creates visible exception/escalation marker
     - outbox dispatcher does not duplicate side effects when re-run

13. **Document assumptions in code comments or brief README updates**
   - If any acceptance ambiguity exists, document the chosen behavior near the implementation and keep it minimal.

# Validation steps

Run and verify at minimum:

1. Restore/build/tests
   - `dotnet build`
   - `dotnet test`

2. If migrations were added, verify they apply cleanly using the project’s existing migration workflow.

3. Manually validate behavior through tests or local execution:
   - a scheduled workflow trigger is picked up by the scheduler
   - only one workflow instance is created for a given due trigger/idempotency key
   - workflow progression advances persisted state
   - transient failure increments attempt count and schedules retry
   - permanent business/policy failure marks execution failed/blocked without retry loop
   - blocked/failed execution persists a visible exception/escalation record
   - outbox dispatcher processes pending messages and does not duplicate already-processed side effects
   - logs include correlation and tenant context

4. Confirm no layering violations:
   - no UI/controller-owned orchestration logic
   - no direct DB access from tool/worker logic bypassing application/infrastructure boundaries
   - tenant scoping enforced in queries and claims

5. Summarize in the final implementation notes:
   - files changed
   - schema changes
   - retry classification rules
   - idempotency approach
   - any follow-up gaps

# Risks and follow-ups

- **Existing partial infrastructure may conflict with new abstractions**  
  Mitigate by extending current patterns instead of introducing parallel worker systems.

- **No explicit exception/escalation entity may exist yet**  
  If needed, add a minimal durable record designed for later dashboard/inbox integration.

- **Distributed locking may not yet be wired**  
  Prefer existing Redis integration if present; otherwise use a DB claim pattern with row/version safety.

- **Acceptance criteria are broad and story-level**  
  Favor a thin, production-sensible implementation over a large framework.

- **Long-running execution semantics may overlap with future orchestration work**  
  Keep contracts generic and reusable for later stories like daily briefings, inbox processing, and integration sync.

- **Outbox consumers may require endpoint-specific idempotency**  
  Implement infrastructure-level dedupe now and note any downstream consumer gaps.

- **Potential follow-up tasks**
  - dashboard/inbox UI for surfaced execution exceptions
  - richer execution history views
  - metrics/health endpoints for worker lag and queue depth
  - broker extraction if throughput grows
  - more granular workflow step state machine and dead-letter handling