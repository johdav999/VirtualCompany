# Goal
Implement `TASK-4.1.3` by wiring scheduled daily and weekly briefing triggers into the same unified briefing generation pipeline used by event-driven briefing jobs, while preserving reliability, idempotency, retry/failure recording, tenant scoping, and persisted correlation metadata.

# Scope
Include only the work needed to satisfy this task and its acceptance criteria:

- Ensure scheduled daily and weekly briefing jobs enqueue briefing update jobs through the unified pipeline rather than using any separate generation path.
- Preserve or add persisted job metadata for:
  - tenant/company identifier
  - event/trigger type
  - correlation identifier
  - idempotency key where applicable
  - schedule context for daily/weekly triggers
- Confirm supported business event types are recognized by the same pipeline:
  - task status changes
  - workflow state changes
  - approval requests
  - approval decisions
  - escalations
  - agent-generated alerts
- Enforce duplicate suppression for event-driven jobs using idempotency key.
- Ensure retry policy is applied consistently and final failure state is recorded with error details.
- Keep implementation tenant-aware and aligned with modular monolith boundaries.

Out of scope unless required to complete the task:

- New UI for briefing configuration or monitoring
- New delivery channels beyond existing in-app/message flow
- Broker-based messaging redesign
- Large refactors outside briefing trigger/job orchestration
- Email/mobile notification enhancements

# Files to touch
Inspect the solution first and then update the minimum necessary files, likely across these areas:

- `src/VirtualCompany.Domain/**`
  - briefing job entities/value objects/enums
  - trigger type/event type definitions
  - retry/failure state modeling if domain-owned
- `src/VirtualCompany.Application/**`
  - commands/handlers for enqueueing briefing jobs
  - unified pipeline orchestration service interfaces
  - scheduled trigger application services
  - idempotency handling contracts
- `src/VirtualCompany.Infrastructure/**`
  - persistence for briefing jobs
  - scheduler/background worker implementations
  - outbox/event dispatcher consumers
  - retry execution and failure recording
  - repository/query updates
- `src/VirtualCompany.Api/**`
  - DI registration if handlers/workers/services are wired here
  - hosted service registration if applicable
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums only if already used for trigger/job types
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for scheduled + event-driven trigger behavior
  - idempotency tests
  - retry/failure persistence tests

Also inspect:
- existing migrations or persistence conventions
- any current briefing generation service
- any scheduler/worker infrastructure
- any outbox/event ingestion pipeline
- any task/workflow/approval/audit event publishers

Do not create speculative files if equivalent structures already exist.

# Implementation plan
1. **Discover the current briefing pipeline**
   - Find all existing code paths for:
     - daily briefing generation
     - weekly summary generation
     - event-driven briefing updates
     - background job scheduling/execution
   - Identify whether scheduled jobs currently call generation logic directly instead of enqueueing a unified job.
   - Identify the canonical job entity/table and retry mechanism already in use.

2. **Define/normalize unified trigger model**
   - Introduce or align on a single trigger abstraction for briefing generation, e.g.:
     - `TriggerSource`: `Event`, `Schedule`
     - `TriggerType`: `TaskStatusChanged`, `WorkflowStateChanged`, `ApprovalRequested`, `ApprovalDecided`, `EscalationRaised`, `AgentAlertGenerated`, `DailyScheduled`, `WeeklyScheduled`
   - Ensure persisted records include:
     - `TenantId`/`CompanyId`
     - `TriggerType`
     - `CorrelationId`
     - `IdempotencyKey` for event-driven triggers
     - timestamps/status/error details
   - Reuse existing naming conventions if already established.

3. **Route scheduled daily/weekly triggers through enqueue path**
   - Update scheduler/background worker so daily and weekly runs create briefing update jobs via the same application command/service used by event-driven triggers.
   - Avoid direct invocation of briefing generation logic from the scheduler.
   - Include deterministic correlation/idempotency behavior for scheduled jobs if needed by existing infrastructure, but do not over-constrain unless acceptance criteria require dedupe for schedules too.

4. **Ensure supported business events map into unified pipeline**
   - Verify or add mappings from emitted business events to briefing enqueue requests for:
     - task status changes
     - workflow state changes
     - approval requests
     - approval decisions
     - escalations
     - agent-generated alerts
   - Persist event type and correlation metadata on the created job.
   - If event handling already exists, only adjust it to use the normalized trigger model and shared enqueue service.

5. **Implement idempotent event-driven enqueueing**
   - Enforce uniqueness for duplicate event submissions with the same idempotency key.
   - Prefer persistence-backed idempotency:
     - unique index/constraint on relevant job table or dedupe table
     - repository logic that safely handles concurrent duplicate inserts
   - Make duplicate handling return success/no-op semantics rather than throwing avoidable failures.
   - Keep tenant/company scope part of uniqueness if appropriate to current domain model.

6. **Unify execution path**
   - Ensure both scheduled and event-created jobs are processed by the same generation worker/handler/service.
   - Shared execution path should:
     - load company/tenant context
     - build briefing generation request
     - invoke existing generation/orchestration logic
     - persist resulting message/notification artifacts
     - update job status consistently

7. **Retry and final failure recording**
   - Confirm failed briefing generation jobs use configured retry policy.
   - Distinguish transient execution failures from permanent validation/business failures if the infrastructure already supports that.
   - Persist final failure state with structured error details:
     - error message
     - exception type if conventionally stored
     - last attempted timestamp
     - retry count / exhausted state
   - Ensure scheduled jobs receive the same retry/failure behavior as event-driven jobs.

8. **Persistence updates**
   - Add/adjust schema only if required:
     - trigger type/source columns
     - correlation ID column
     - idempotency key column
     - error details/final failure columns
     - uniqueness constraint for event idempotency
   - Follow existing migration approach in the repo.
   - Keep migration minimal and backward-compatible where possible.

9. **Dependency injection and worker wiring**
   - Register any new/updated services, handlers, repositories, and hosted workers.
   - Ensure no duplicate scheduler registrations or double-processing paths remain.
   - Remove or deprecate any old direct scheduled generation path if it bypasses the unified pipeline.

10. **Add tests**
   - Add integration-focused tests covering:
     - supported event emits create a briefing job within expected flow
     - duplicate event with same idempotency key creates only one job
     - scheduled daily trigger creates a job and uses same execution pipeline
     - scheduled weekly trigger creates a job and uses same execution pipeline
     - failed generation retries and records final failure details after exhaustion
     - persisted job metadata includes tenant/company, trigger/event type, and correlation ID
   - Prefer existing test patterns and fixtures over introducing new frameworks.

11. **Document assumptions in code comments only where necessary**
   - Keep comments concise.
   - Do not add broad documentation unless the repo already maintains task-level docs nearby.

# Validation steps
Run the smallest relevant validation first, then full validation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects or filters for briefing/workflow/background jobs, run those first during iteration.

4. Manually verify in code/tests that:
   - daily scheduled briefing path enqueues a unified briefing job
   - weekly scheduled briefing path enqueues a unified briefing job
   - event-driven triggers enqueue the same job type/path
   - duplicate event idempotency key does not create a second job
   - retry policy is applied on failure
   - final failure state persists error details
   - persisted records include tenant/company, trigger/event type, and correlation ID

5. If migrations are added:
   - ensure migration compiles and is included in startup path per repo conventions
   - verify uniqueness constraint/index matches idempotency behavior

# Risks and follow-ups
- **Unknown existing implementation split:** scheduled briefings may already have a separate legacy path; removing/bypassing it without regression requires careful discovery.
- **Schema drift risk:** if job persistence is incomplete today, adding columns/constraints may require migration/backfill considerations.
- **Concurrency/idempotency risk:** duplicate suppression must be safe under concurrent event delivery; prefer DB-enforced uniqueness over in-memory checks.
- **Retry semantics risk:** existing worker infrastructure may not clearly separate transient vs permanent failures; align with current conventions rather than inventing a parallel retry system.
- **Timing acceptance criterion:** “within 30 seconds” is best validated through architecture/path efficiency and testable enqueue behavior; if no timing tests exist, avoid brittle wall-clock tests unless the repo already supports them.
- **Follow-up candidate:** add operational visibility for briefing job status, retries, and dead-letter/final-failure review if not already present.
- **Follow-up candidate:** standardize correlation/idempotency conventions across all workflow/background job types beyond briefings.