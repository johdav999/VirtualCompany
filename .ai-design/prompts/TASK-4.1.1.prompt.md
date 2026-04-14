# Goal
Implement **TASK-4.1.1 — briefing update job producer for supported domain events** in the existing .NET modular monolith so that supported business events enqueue a tenant-scoped briefing update job within 30 seconds, persist required identifiers, enforce idempotency, support retries/final failure recording, and keep scheduled daily/weekly briefing generation using the same downstream generation pipeline.

# Scope
Deliver the producer-side and job-model plumbing needed for **event-driven briefing generation**, aligned to **ST-404** and **ST-505**, without overreaching into a full new messaging platform.

Include:

- A durable **briefing update job** persistence model for event-driven and scheduled jobs.
- Support for these event types:
  - task status changes
  - workflow state changes
  - approval requests
  - approval decisions
  - escalations
  - agent-generated alerts
- A producer path that reacts to supported domain/integration events and creates a briefing update job containing:
  - tenant/company identifier
  - event type
  - correlation identifier
  - idempotency key
  - source event metadata/payload reference as appropriate
- Idempotency enforcement so duplicate events with the same idempotency key create only one job.
- Retry/failure state fields on the job record so the existing or upcoming worker can retry according to policy and record final failure details.
- Refactoring or wiring so **scheduled daily/weekly briefing jobs** create the same job type and flow through the same generation pipeline as event-driven jobs.

Do not include unless required by existing code patterns:

- Full briefing content generation logic rewrite
- New UI
- New external broker infrastructure
- Broad event bus redesign beyond what is needed to hook supported events into job creation

# Files to touch
Inspect the solution first and then update the exact files that match existing patterns. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - briefing/job aggregate or entity definitions
  - domain event contracts or supported event markers
  - enums/constants for briefing trigger source and job status
- `src/VirtualCompany.Application/`
  - command/service for creating briefing update jobs
  - event handlers / notification handlers for supported business events
  - scheduling use cases if daily/weekly jobs currently bypass the shared pipeline
  - retry policy abstractions or job orchestration contracts
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration
  - repository implementation
  - migrations for new briefing job table/indexes/constraints
  - background worker wiring if scheduled jobs or dispatcher live here
- `src/VirtualCompany.Api/`
  - DI registration if handlers/workers are registered here
- `tests/VirtualCompany.Api.Tests/` and/or other existing test projects
  - integration tests for event-to-job creation
  - idempotency tests
  - retry/failure persistence tests
  - scheduled job path tests

Also inspect:

- existing outbox/domain event handling
- current scheduled briefing implementation
- any existing briefing/message/notification entities
- correlation/idempotency conventions already used elsewhere

# Implementation plan
1. **Discover existing briefing and background job patterns**
   - Find how scheduled daily/weekly briefings currently run.
   - Find whether there is already:
     - a job table
     - outbox processing
     - MediatR/domain event notifications
     - retry metadata conventions
     - correlation ID propagation
   - Reuse existing patterns rather than inventing parallel infrastructure.

2. **Define the briefing update job model**
   Add or extend a durable entity/table for briefing generation jobs with fields covering at minimum:

   - `Id`
   - `CompanyId`
   - `TenantId` if distinct from company in current model; otherwise use the project’s actual tenant-scoping convention
   - `JobType` or `TriggerType` (`event_driven`, `daily`, `weekly`)
   - `EventType` nullable for scheduled jobs
   - `CorrelationId`
   - `IdempotencyKey`
   - `Status` (`pending`, `processing`, `retrying`, `completed`, `failed`)
   - `AttemptCount`
   - `NextAttemptAt`
   - `LastError`
   - `FinalFailedAt`
   - `CreatedAt`
   - `UpdatedAt`
   - optional source metadata JSON/payload reference

   Requirements:
   - Add a unique index/constraint on the idempotency key in the correct tenant/company scope.
   - Make scheduled jobs compatible with the same table/pipeline.

3. **Create a single application service/command for job creation**
   Implement one entry point, e.g. `CreateBriefingUpdateJob` / `EnqueueBriefingUpdateJob`, used by both:
   - supported event handlers
   - scheduled daily/weekly triggers

   Behavior:
   - validate required identifiers
   - map source trigger to normalized job record
   - persist job
   - enforce idempotency:
     - if same idempotency key already exists, do not create a duplicate
     - return existing job or a no-op result
   - ensure operation is safe under concurrent duplicate delivery

   Prefer DB-enforced uniqueness plus graceful handling of unique constraint violations.

4. **Wire supported business events to the producer**
   Identify existing domain events or notifications for:
   - task status changes
   - workflow state changes
   - approval requests
   - approval decisions
   - escalations
   - agent-generated alerts

   For each supported event:
   - add a handler that calls the shared enqueue service
   - derive a stable event type constant
   - propagate company/tenant context
   - propagate correlation ID if present; otherwise generate/derive according to existing conventions
   - derive a deterministic idempotency key from the source event’s unique identity

   If some events do not yet exist in code:
   - add the smallest missing domain/application event contract needed
   - do not redesign unrelated modules

5. **Unify scheduled daily/weekly jobs with the same pipeline**
   Refactor current scheduled briefing execution so the scheduler creates the same briefing update job record instead of bypassing it.

   Requirements:
   - daily and weekly schedules still run
   - they create jobs through the same enqueue service
   - downstream generation worker/pipeline consumes the same job type regardless of trigger source

   Use distinct idempotency keys for scheduled jobs, e.g. company + schedule type + scheduled period/window.

6. **Add retry/failure state support**
   Ensure the job model supports worker retry behavior even if the worker already exists elsewhere.

   At minimum:
   - attempt count increments on failure
   - transient failures can be rescheduled using configured retry policy
   - final failure state is persisted with error details
   - permanent/business failures can be marked failed without pointless retries if existing patterns support that distinction

   If a worker already processes briefing jobs:
   - adapt it to update the new/shared job record fields
   - do not fork a second processing path

   If no worker exists yet but acceptance requires failure state:
   - implement the minimal processing-state transitions needed for tests and future worker compatibility

7. **Persist migration and indexes**
   Add an EF Core migration for the new/changed schema.

   Include:
   - table creation or alteration
   - unique index for idempotency
   - indexes for worker polling (`status`, `next_attempt_at`, `company_id`, `created_at`)
   - any JSONB columns only if consistent with current PostgreSQL usage

8. **Observability and audit-friendly logging**
   Add structured logs around:
   - supported event received
   - briefing job created
   - duplicate ignored due to idempotency
   - retry scheduled
   - final failure recorded

   Include company/tenant and correlation identifiers in logs using existing logging conventions.

9. **Tests**
   Add automated tests covering at least:

   - supported event creates a briefing update job with:
     - company/tenant
     - event type
     - correlation ID
     - idempotency key
   - duplicate event with same idempotency key does not create a second job
   - scheduled daily job and weekly job create the same job type/pipeline records
   - failure updates retry metadata according to configured policy
   - final failure records error details
   - unsupported events do not create jobs

   Prefer integration tests against the real persistence layer if that is the project norm; otherwise use the highest-fidelity test style already present.

10. **Keep implementation aligned with backlog intent**
   Ensure the final design clearly supports the acceptance criterion that a job is created within 30 seconds of event emission.
   - In practice, this means the producer path should be synchronous with event handling or processed by an existing near-real-time internal dispatcher.
   - If current architecture uses outbox dispatch, ensure the event-to-job creation path is not delayed beyond normal worker cadence and document any timing assumptions in code comments/tests.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal way.

4. Manually validate the following flows in tests or a local run:
   - emit a supported task status change event → one briefing update job is persisted
   - emit the same event again with same idempotency key → still one job only
   - emit each supported event type → correct normalized `EventType` persisted
   - trigger daily and weekly scheduler paths → same briefing job table/pipeline used
   - simulate generation failure → retry metadata updated
   - exhaust retries → final failed state and error details persisted

5. Confirm persistence details:
   - unique idempotency index exists
   - correlation ID is stored
   - company/tenant scope is stored
   - worker polling indexes exist

6. Confirm no regression:
   - existing scheduled briefing behavior still functions
   - no duplicate jobs from concurrent inserts
   - no tenant scope leakage in queries or handlers

# Risks and follow-ups
- **Event availability mismatch:** some supported business events may not yet exist as explicit domain notifications. Add only the minimal missing contracts/handlers needed and note gaps.
- **Tenant vs company ambiguity:** architecture mentions tenant and company; current schema may only use `company_id`. Follow the existing codebase convention and persist both only if both truly exist.
- **Retry ownership ambiguity:** if retry execution belongs to another backlog item/worker, still persist the fields now and integrate minimally so acceptance is met without overbuilding.
- **Scheduled pipeline divergence:** current daily/weekly implementation may directly generate messages. Refactor carefully so it enqueues shared jobs without breaking delivery timing.
- **Idempotency race conditions:** rely on database uniqueness, not only in-memory checks.
- **30-second SLA dependency:** if current outbox/worker polling intervals are too slow, reduce polling interval/config or document the required operational setting as part of this task.
- **Follow-up suggestion:** after this task, add a dedicated briefing job processor/worker hardening task if not already present, including metrics, dead-letter visibility, and admin replay support.