# Goal
Implement **TASK-10.2.6 — Scheduler should use background workers with distributed locking** for **ST-402 Workflow definitions, instances, and triggers** in the existing .NET solution.

The coding agent should update the workflow scheduling/execution path so that:
- scheduled workflow triggering runs in **background workers**, not request/interactive paths
- execution is protected by **distributed locking** to avoid duplicate scheduler runs across multiple app instances
- the design fits the documented architecture:
  - ASP.NET Core modular monolith
  - PostgreSQL as source of truth
  - Redis for distributed locks / coordination
  - background workers for scheduled jobs and workflow progression
- the implementation is production-oriented, tenant-safe, and idempotent where practical

No explicit acceptance criteria were provided for this task, so derive behavior from:
- ST-402 notes: **“Scheduler should use background workers with distributed locking.”**
- ST-404 notes: resilient background execution, retries, tenant-scoped execution, Redis coordination
- architecture guidance around Redis locks and background workers

# Scope
Implement the minimum cohesive slice needed to support scheduler execution via hosted background services with distributed locking.

Include:
1. **Background worker infrastructure**
   - Add or extend an `IHostedService` / `BackgroundService` responsible for polling due scheduled workflow triggers.
   - Keep scheduling logic out of controllers/UI.

2. **Distributed lock abstraction**
   - Introduce an infrastructure abstraction for distributed locks.
   - Prefer Redis-backed locking if Redis is already configured in the solution.
   - If Redis integration is not yet present in runnable form, add the abstraction and a concrete implementation aligned with current infrastructure patterns.

3. **Scheduler execution flow**
   - On each polling interval:
     - acquire a global scheduler lock or partitioned lock
     - query due scheduled workflow definitions/triggers
     - start workflow instances safely
     - release/expire lock correctly
   - Ensure duplicate starts are prevented as much as possible through lock + persistence safeguards.

4. **Idempotency / duplicate protection**
   - Add a persistence-level safeguard where reasonable, such as:
     - deterministic trigger window handling
     - last-run watermark
     - unique key for a schedule occurrence
   - Do not rely on lock alone.

5. **Dependency registration and configuration**
   - Register worker(s), lock service, and scheduler options in DI.
   - Add configuration values for polling interval, lock TTL, batch size, etc.

6. **Observability**
   - Add structured logs around:
     - lock acquisition success/failure
     - scheduler polling
     - workflows started
     - duplicate/skip conditions
     - failures

7. **Tests**
   - Add focused tests for:
     - lock acquisition behavior at abstraction level where feasible
     - scheduler skipping when lock not acquired
     - due scheduled workflows being started once
     - duplicate prevention/idempotent behavior

Do not expand scope into:
- full workflow builder UX
- arbitrary cron designer unless already present
- message broker introduction
- unrelated approval or orchestration features

# Files to touch
Inspect the solution first and then modify the actual relevant files. Likely areas include:

- `src/VirtualCompany.Api/`
  - `Program.cs`
  - app configuration files if scheduler options are stored there

- `src/VirtualCompany.Application/`
  - workflow scheduling interfaces/services
  - commands for starting workflow instances
  - scheduler coordination abstractions

- `src/VirtualCompany.Domain/`
  - workflow scheduling domain concepts/value objects if needed
  - invariants for schedule occurrence deduplication if domain-owned

- `src/VirtualCompany.Infrastructure/`
  - background worker implementation
  - Redis/distributed lock implementation
  - persistence/repository updates
  - EF Core or SQL access for due scheduled workflows
  - options classes and DI registration

- `tests/VirtualCompany.Api.Tests/`
  - integration or host-level tests for worker registration / behavior

Also inspect for existing equivalents before creating new files, such as:
- workflow services
- hosted workers
- Redis cache/connection abstractions
- repository patterns
- options/config classes
- test fixtures for infrastructure or application services

Prefer extending existing patterns over inventing parallel ones.

# Implementation plan
1. **Discover current workflow and scheduling model**
   - Search for:
     - `workflow_definitions`
     - `workflow_instances`
     - `trigger_type`
     - scheduler/background worker classes
     - Redis usage
     - hosted services
   - Determine how scheduled workflows are currently represented:
     - JSON definition with schedule metadata
     - explicit schedule tables
     - placeholder/not yet implemented
   - Reuse the existing workflow start command/service if one exists.

2. **Define the scheduling contract**
   - Add or refine an application-layer contract such as:
     - `IWorkflowScheduler`
     - `IScheduledWorkflowTriggerService`
     - `IDistributedLockProvider`
   - Keep lock abstraction generic, e.g.:
     - acquire by key
     - TTL/lease duration
     - async disposable handle or explicit release
   - Example shape:
     - `Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)`

3. **Implement distributed locking**
   - If Redis is available, implement a Redis-backed lock in Infrastructure.
   - Use a safe lease-based pattern:
     - unique lock token/value
     - set-if-not-exists with expiry
     - release only if token matches
   - Avoid unsafe blind delete on release.
   - If the codebase already uses StackExchange.Redis, integrate with that.
   - Add clear logging for acquisition and release outcomes.

4. **Implement scheduler options**
   - Add options class, e.g. `WorkflowSchedulerOptions`, with:
     - `PollingInterval`
     - `LockKey`
     - `LockTtl`
     - `BatchSize`
     - optional `Enabled`
   - Bind from configuration and validate sensible defaults.

5. **Build the background worker**
   - Create a `BackgroundService` in Infrastructure or Api-hosted composition layer.
   - Loop until cancellation:
     - wait polling interval
     - attempt lock acquisition
     - if lock not acquired, log debug/info and skip cycle
     - if acquired:
       - fetch due scheduled workflow definitions/occurrences
       - start instances through application service/command
       - persist schedule progress/watermark
       - handle per-item failures without crashing the whole loop
   - Ensure cancellation token is respected.

6. **Add due-work discovery and duplicate prevention**
   - Based on the existing schema/model, implement one of these pragmatic approaches:
     - maintain `next_run_at` / `last_run_at` for scheduled definitions
     - compute due occurrences and persist a unique occurrence record
     - create workflow instances with a deterministic trigger reference per schedule window
   - Strongly prefer a persistence-level dedupe key such as:
     - `trigger_source = "schedule"`
     - `trigger_ref = "{workflowDefinitionId}:{scheduledOccurrenceUtc}"`
   - Then enforce uniqueness in persistence if feasible.
   - If schema changes are needed, add them in the project’s migration style.

7. **Start workflow instances through application logic**
   - Do not instantiate workflow instances directly in the worker if an application command/service already exists.
   - Reuse or add a command like:
     - `StartWorkflowInstanceFromSchedule`
   - Ensure tenant/company context is preserved.
   - Persist:
     - `company_id`
     - `workflow_definition_id`
     - `trigger_source = schedule`
     - deterministic `trigger_ref`
     - initial state/current step

8. **Handle failures safely**
   - Catch and log exceptions per polling cycle.
   - Catch and log exceptions per scheduled item so one bad workflow does not block others.
   - Distinguish:
     - lock contention
     - transient infra failures
     - duplicate occurrence conflicts
     - invalid workflow definition/schedule config
   - Skip duplicates gracefully rather than failing the cycle.

9. **Register services**
   - Update DI registration in the host startup.
   - Ensure worker registration is environment/config aware if needed.
   - Ensure Redis/distributed lock dependencies are registered once and consistently.

10. **Add tests**
   - Unit tests for scheduler service:
     - when lock not acquired, no workflows are started
     - when due items exist and lock acquired, start is invoked
     - duplicate occurrence is ignored/handled safely
   - Integration-style tests if practical:
     - host starts with worker registered
     - scheduler options bind correctly
   - If Redis cannot be used in tests, mock `IDistributedLockProvider`.

11. **Document assumptions in code comments**
   - If schedule parsing/model is incomplete in the current codebase, implement the smallest viable path and leave concise TODOs.
   - Keep comments focused on operational reasoning, especially around lock safety and dedupe.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify worker registration and startup logs:
   - confirm the scheduler background worker starts without DI/config errors

4. Validate lock behavior:
   - run the app with scheduler enabled
   - confirm logs show:
     - polling cycle started
     - lock acquired or skipped due to contention
   - if feasible, simulate two host instances and verify only one performs scheduling work per interval

5. Validate scheduled workflow triggering:
   - seed or configure a due scheduled workflow definition
   - confirm a workflow instance is created by the worker
   - confirm `trigger_source`/`trigger_ref` or equivalent dedupe fields are populated

6. Validate duplicate prevention:
   - trigger the same scheduler cycle/occurrence twice
   - confirm only one workflow instance is created for the same scheduled occurrence

7. Validate failure handling:
   - simulate an invalid scheduled workflow definition or transient failure
   - confirm the worker logs the error and continues future polling cycles

8. If schema changes were added:
   - apply migrations using the repository’s existing migration workflow
   - verify the app still starts and tests pass

# Risks and follow-ups
- **Current schedule model may be incomplete**
  - If workflow schedules are not yet fully modeled, implement the worker/lock foundation and the smallest viable due-item query path without overdesigning a scheduler engine.

- **Redis may not yet be fully wired**
  - If Redis infrastructure is absent, still add the abstraction cleanly and integrate with existing cache/connection patterns.
  - Prefer not to introduce a brittle fake distributed lock in production code.

- **Locking alone is insufficient**
  - Distributed locks reduce contention but do not guarantee exactly-once execution.
  - Persistence-level dedupe is required and should be part of this task.

- **Polling interval vs lock TTL**
  - Misconfigured TTL can cause overlapping execution or excessive idle time.
  - Choose conservative defaults and document them.

- **Multi-tenant fairness**
  - A single global lock is acceptable for the first implementation, but future scaling may require partitioned locks or tenant-sharded scheduling.

- **Follow-up candidates**
  - add metrics for scheduler cycles, lock contention, due items processed, duplicates skipped, failures
  - add retry/backoff policy for transient infrastructure failures
  - evolve from global lock to partitioned scheduling if throughput grows
  - formalize schedule occurrence persistence if recurring workflows become more complex