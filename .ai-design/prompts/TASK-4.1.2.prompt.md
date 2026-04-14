# Goal
Implement **TASK-4.1.2 — Add idempotency, retry, and failure tracking to briefing generation jobs** for **US-4.1 Build event-driven briefing generation pipeline** in the existing .NET modular monolith.

Deliver a production-ready change that ensures:
- supported business events create briefing update jobs within 30 seconds
- event-driven jobs persist tenant/company, event type, correlation ID, and idempotency key
- duplicate events with the same idempotency key do not create duplicate briefing jobs
- failed briefing generation jobs retry according to configured policy
- final failure state is persisted with error details
- existing scheduled daily/weekly briefing jobs continue to run through the same shared generation pipeline

Use the current architecture conventions:
- ASP.NET Core modular monolith
- PostgreSQL primary store
- background workers for async processing
- CQRS-lite application layer
- tenant-scoped processing
- reliable/idempotent job handling

# Scope
Implement only what is required for this task and acceptance criteria.

Include:
- domain/application/infrastructure support for a **briefing generation job** model with:
  - job type/source (`scheduled` vs `event-driven`)
  - tenant/company identifier
  - event type
  - correlation identifier
  - idempotency key
  - status
  - retry metadata
  - failure metadata
  - timestamps
- supported event types:
  - task status changes
  - workflow state changes
  - approval requests
  - approval decisions
  - escalations
  - agent-generated alerts
- event-to-job creation flow with idempotency enforcement
- retry execution behavior for transient failures
- final failure recording for exhausted retries
- refactor or adapt scheduled daily/weekly briefing generation so both scheduled and event-driven jobs use the same execution pipeline
- tests covering idempotency, retries, failure recording, and scheduled pipeline reuse

Do not include:
- unrelated UI work unless required to support existing execution paths
- broker adoption or distributed messaging changes beyond current architecture
- broad redesign of the scheduling subsystem
- email/mobile delivery enhancements
- unrelated audit/explainability features unless needed for persisted failure/error details

# Files to touch
Inspect the solution first and update the exact files that match existing patterns. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - briefing job entity/value objects/enums
  - domain constants for supported event types/statuses
- `src/VirtualCompany.Application/**`
  - commands/handlers for creating briefing jobs from events
  - commands/handlers for executing briefing jobs
  - retry policy abstraction/config
  - shared briefing generation pipeline service contract
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL persistence mappings
  - repositories
  - background worker/job runner
  - migration(s)
  - event ingestion/outbox dispatcher integration if present
- `src/VirtualCompany.Api/**`
  - DI/configuration wiring if needed
- `src/VirtualCompany.Web/**`
  - only if scheduled briefing triggers currently live here and must be redirected to shared pipeline
- `tests/**`
  - unit tests for idempotency and retry logic
  - integration tests for persistence and worker execution behavior

Also inspect for existing equivalents before creating new types:
- scheduled job infrastructure
- outbox/event dispatcher
- notification/briefing generation services
- workflow/task/approval domain events
- background execution abstractions
- existing retry/failure tracking patterns

# Implementation plan
1. **Discover existing briefing and job infrastructure**
   - Search for:
     - briefing generation services
     - daily/weekly summary jobs
     - scheduler/background worker implementations
     - outbox/event dispatcher
     - task/workflow/approval/escalation/alert events
   - Reuse existing abstractions where possible.
   - Identify whether there is already a generic job table or execution framework that can be extended instead of introducing a parallel system.

2. **Design the briefing job model**
   - Add or extend a persisted job record for briefing generation with fields covering:
     - `Id`
     - `CompanyId`
     - optional `TenantId` if distinct in current model; otherwise use company-scoped tenant context consistently
     - `JobSource` (`ScheduledDaily`, `ScheduledWeekly`, `EventDriven`)
     - `EventType` nullable for scheduled jobs
     - `CorrelationId`
     - `IdempotencyKey`
     - `Status` (`Pending`, `InProgress`, `Succeeded`, `RetryPending`, `Failed`)
     - `AttemptCount`
     - `MaxAttempts`
     - `NextAttemptAt`
     - `LastErrorCode` nullable
     - `LastErrorMessage` nullable
     - `LastErrorDetails` nullable/JSON/text
     - `CreatedAt`
     - `StartedAt`
     - `CompletedAt`
     - `UpdatedAt`
   - Add a unique constraint/index for idempotency, scoped appropriately:
     - likely `(CompanyId, IdempotencyKey)` for event-driven jobs
   - Ensure scheduled jobs can coexist without conflicting with event-driven idempotency.

3. **Add migration and persistence mapping**
   - Create EF Core configuration and migration for the briefing job table or table changes.
   - Add indexes for:
     - idempotency lookup
     - pending/retry execution lookup by `Status` + `NextAttemptAt`
     - company/time queries if useful
   - Keep schema aligned with PostgreSQL conventions already used in the repo.

4. **Implement supported event ingestion to create jobs**
   - Add an application service/command such as `CreateBriefingUpdateJobFromBusinessEvent`.
   - Accept:
     - company/tenant context
     - supported event type
     - correlation ID
     - idempotency key
     - source entity reference if useful
     - event timestamp/payload metadata if current patterns support it
   - Validate supported event types explicitly.
   - Persist a new pending job if no existing job with the same scoped idempotency key exists.
   - If duplicate:
     - do not create a second job
     - return existing job or a no-op result
   - Wire this into the existing internal event/outbox processing path for:
     - task status changes
     - workflow state changes
     - approval requests
     - approval decisions
     - escalations
     - agent-generated alerts

5. **Ensure “created within 30 seconds” behavior**
   - Use the current background dispatcher/outbox/event processing path.
   - Do not add unnecessary batching or delays.
   - If polling workers exist, ensure polling interval/config supports the SLA.
   - Add/update configuration defaults or comments if needed so supported events are processed within 30 seconds under normal operation.

6. **Unify scheduled and event-driven execution pipeline**
   - Extract or formalize a shared service, e.g.:
     - `IBriefingGenerationPipeline`
     - `GenerateBriefingForJob(...)`
   - Update daily and weekly scheduled briefing triggers so they create briefing jobs and execute through the same pipeline as event-driven jobs.
   - Preserve existing scheduled behavior while routing through the shared execution path.
   - Scheduled jobs should not require event metadata but should still use the same status/retry/failure handling model where appropriate.

7. **Implement job execution and retry behavior**
   - Add a worker/runner that:
     - selects pending/retryable jobs due for execution
     - marks a job `InProgress`
     - invokes the shared briefing generation pipeline
     - marks success on completion
   - On failure:
     - classify transient vs permanent if current architecture supports it
     - increment attempt count
     - if attempts remain, set `RetryPending` and `NextAttemptAt` according to configured retry policy
     - if attempts exhausted or failure is permanent, set final `Failed`
     - persist error details
   - Make execution idempotent and safe against duplicate worker pickup if current infra is concurrent:
     - use optimistic concurrency, row locking, or status transition guards consistent with existing patterns

8. **Add configurable retry policy**
   - Introduce options/config for briefing job retries, for example:
     - max attempts
     - backoff interval(s)
     - transient exception classification
   - Bind via existing .NET options pattern.
   - Keep defaults conservative and deterministic for tests.

9. **Persist failure details clearly**
   - Record enough detail for operational diagnosis without leaking sensitive internals:
     - exception type/code
     - safe message
     - truncated stack/details if current standards allow
     - timestamp of last failure
   - Ensure final failure state is queryable and durable.

10. **Testing**
   - Add unit tests for:
     - supported event type acceptance
     - duplicate idempotency key returns no duplicate job
     - retry scheduling after transient failure
     - final failure after max attempts
     - scheduled and event-driven jobs both call the same pipeline abstraction
   - Add integration tests for:
     - persistence of correlation ID, company ID, event type, idempotency key
     - unique constraint/idempotency behavior
     - worker execution status transitions
   - Prefer existing test patterns and fixtures in the repo.

11. **Documentation/comments**
   - Add concise code comments where behavior is non-obvious.
   - If the repo has architecture or module docs for background jobs, update minimally to note:
     - supported event-driven briefing triggers
     - idempotency behavior
     - retry/failure tracking

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal way.
   - If a migrations project/pattern exists, follow it exactly.
   - Confirm the new/updated table has:
     - unique idempotency constraint
     - retry/failure columns
     - indexes for due job lookup

4. Validate event-driven job creation:
   - Trigger or simulate each supported event type through the existing application/event path.
   - Confirm a briefing job is created with:
     - company/tenant context
     - event type
     - correlation ID
     - idempotency key
     - pending status
   - Confirm creation occurs within the expected processing window under current worker polling configuration.

5. Validate idempotency:
   - Emit the same supported event twice with the same idempotency key.
   - Confirm only one briefing job exists.

6. Validate retry behavior:
   - Force the shared briefing generation pipeline to fail with a transient error.
   - Confirm:
     - attempt count increments
     - next retry is scheduled per policy
     - job is not duplicated

7. Validate final failure recording:
   - Force repeated failure until retries are exhausted.
   - Confirm final status is `Failed` and error details are persisted.

8. Validate scheduled pipeline reuse:
   - Run daily and weekly scheduled briefing triggers.
   - Confirm they create/execute jobs through the same shared pipeline abstraction and still complete successfully.

9. Validate no regression in existing scheduled briefings:
   - Confirm previously working scheduled briefing flows still generate stored briefing outputs/messages/notifications as before.

# Risks and follow-ups
- **Existing job framework mismatch:** The repo may already have a generic background job model. Prefer extending it over adding a bespoke briefing job system to avoid duplication.
- **Event source inconsistency:** Supported business events may not yet be normalized. If event contracts differ by module, add a thin adapter layer rather than coupling briefing creation directly to many domain internals.
- **Concurrency hazards:** Multiple workers or dispatcher retries can create duplicate execution attempts. Use DB-enforced idempotency and safe status transitions.
- **Retry classification ambiguity:** If transient vs permanent failures are not already standardized, keep the first implementation simple and explicit, with a clear place to improve classification later.
- **Scheduled flow regression:** Refactoring scheduled daily/weekly briefings into the shared pipeline may break existing behavior if hidden assumptions exist. Preserve current output contracts and add regression tests.
- **Tenant terminology mismatch:** Architecture mentions tenant/company; the current codebase may use `CompanyId` as the tenant boundary. Follow existing conventions consistently.
- **Observability gap:** If there is no current operational view for failed jobs, this task should still persist failure state now; a later backlog item can expose failed briefing jobs in admin/ops UI.
- **30-second SLA dependency:** Meeting the creation window may depend on current outbox polling intervals and worker cadence. If config is too slow, adjust defaults or document required operational settings.