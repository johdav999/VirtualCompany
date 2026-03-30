# Goal
Implement **TASK-10.2.2** for **ST-402 — Workflow definitions, instances, and triggers** so that workflow instances can be started through all three supported trigger paths:

- **manual**
- **schedule**
- **internal event**

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and persist/query workflow instance state consistently.

# Scope
Implement the minimum end-to-end backend support required for this backlog task, aligned to the story and architecture:

- Add or complete domain/application/infrastructure support for:
  - workflow definitions with trigger metadata
  - workflow instances with persisted trigger source/ref
  - starting workflow instances manually
  - starting workflow instances from scheduled execution
  - starting workflow instances from internal domain/application events
- Ensure workflow instance creation is:
  - tenant-scoped
  - version-safe against workflow definitions
  - auditable/loggable at business level where existing patterns already exist
- Expose only the smallest necessary API/application entry points to support the three trigger modes.
- Add tests covering the three trigger paths and tenant scoping.

Out of scope unless already trivial and required by existing code structure:

- Full workflow builder UX
- Rich schedule authoring UI
- Full workflow step runner/orchestration engine
- External event/webhook trigger ingestion beyond internal app events
- Advanced retry/escalation behavior outside what is necessary to create instances
- Large refactors unrelated to workflow trigger support

# Files to touch
Inspect the solution first and adjust to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - workflow definition/entity/value object files
  - workflow instance/entity files
  - enums/constants for trigger type/source/state
  - domain events if used
- `src/VirtualCompany.Application/**`
  - commands/handlers for manual workflow start
  - services/interfaces for workflow triggering
  - scheduled trigger coordination service
  - internal event trigger handler/subscriber
  - DTOs/contracts for workflow start requests/results
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories/query implementations
  - migrations for workflow tables/columns if missing/incomplete
  - background worker or scheduler integration
  - internal event dispatch wiring
- `src/VirtualCompany.Api/**`
  - controller or endpoint for manual workflow start
  - DI registration if needed
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums only if this solution already centralizes them here
- Tests in corresponding test projects if present
  - unit tests for domain/application logic
  - integration tests for persistence/API paths

Also review:
- `README.md`
- existing workflow/task/approval modules
- existing multi-tenant patterns
- existing outbox/background worker/event handling patterns

# Implementation plan
1. **Inspect existing workflow implementation before changing anything**
   - Find current support for:
     - `workflow_definitions`
     - `workflow_instances`
     - trigger fields such as `trigger_type`, `trigger_source`, `trigger_ref`
     - background workers/scheduler
     - domain or integration event publishing
   - Reuse existing naming and module boundaries.
   - Do not introduce parallel abstractions if a workflow service already exists.

2. **Model trigger concepts explicitly**
   - Ensure workflow definitions support trigger type metadata consistent with architecture/backlog:
     - `manual`
     - `schedule`
     - `event`
   - Ensure workflow instances persist:
     - `company_id`
     - `workflow_definition_id`
     - `trigger_source`
     - `trigger_ref`
     - `state`
     - `current_step`
     - `context_json`
     - timestamps
   - If enums/value objects exist, extend them rather than using scattered string literals.
   - Keep version-awareness intact so instances start against a specific definition/version snapshot or current active version per existing design.

3. **Implement a single application service for starting workflow instances**
   - Create or extend a central service such as `IWorkflowInstanceStarter` / `WorkflowTriggerService`.
   - This service should accept:
     - company/tenant context
     - workflow definition identifier/code
     - trigger source
     - optional trigger reference
     - initial context payload
   - It should:
     - resolve the active workflow definition for the tenant
     - validate the requested trigger is allowed by the definition
     - create the workflow instance
     - initialize state/current step according to the definition structure already used in the codebase
     - persist the instance transactionally
   - Keep this service reusable by manual API, scheduler, and internal event handlers.

4. **Add manual start path**
   - Implement a command/handler and API endpoint for manual workflow start.
   - Require tenant/company scoping from the authenticated request context.
   - Validate:
     - workflow definition exists and is active
     - manual trigger is supported by that definition
     - caller is authorized according to existing authorization patterns
   - Return a minimal response containing at least:
     - workflow instance id
     - workflow definition id/code
     - state
     - trigger source

5. **Add scheduled start path**
   - Reuse existing background worker/scheduler infrastructure if present.
   - Implement a scheduler-facing service that:
     - finds workflow definitions configured for scheduled triggering
     - acquires any required distributed lock/idempotency guard
     - starts workflow instances through the shared starter service
   - If schedule metadata already exists in `definition_json`, use it rather than inventing a new schema.
   - If no scheduler exists yet, implement only the minimal internal hook/job class needed to demonstrate scheduled starts without overbuilding.
   - Prevent obvious duplicate starts for the same schedule tick if existing patterns support idempotency/correlation.

6. **Add internal event start path**
   - Identify the project’s internal event mechanism:
     - MediatR notifications
     - domain events
     - application events
     - outbox-dispatched internal messages
   - Implement a handler/subscriber that maps a qualifying internal event to workflow start.
   - Event-triggered start should:
     - resolve tenant/company context from the event
     - resolve matching workflow definitions configured for that event
     - create workflow instances through the shared starter service
   - Keep event matching simple and deterministic, likely by event name/type in trigger config or definition JSON.
   - Do not add external broker complexity.

7. **Persist and query instance state cleanly**
   - Ensure created instances are queryable with persisted:
     - current state
     - current step
     - trigger source/ref
   - If repository/query methods are missing, add minimal read support used by tests or endpoint responses.
   - Preserve tenant filtering in all queries.

8. **Database and EF Core updates**
   - Add/update EF configurations and migration(s) only if schema support is missing.
   - Match the architecture model for:
     - `workflow_definitions`
     - `workflow_instances`
   - Use PostgreSQL-friendly mappings for JSON/JSONB fields.
   - Keep migration changes narrowly scoped to this task.

9. **Testing**
   - Add unit tests for the shared workflow start service:
     - manual trigger allowed
     - schedule trigger allowed
     - event trigger allowed
     - disallowed trigger rejected
     - inactive/missing definition rejected
     - tenant mismatch rejected
   - Add integration tests where feasible for:
     - manual API start creates persisted instance
     - scheduled job path creates instance
     - internal event path creates instance
   - Verify persisted `trigger_source` and `trigger_ref` values.

10. **Keep implementation aligned with story intent**
   - This task is about **starting instances from three trigger sources**, not full execution progression.
   - Prefer small, composable additions over speculative workflow engine design.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify manual trigger path:
   - Start the API
   - Call the manual workflow start endpoint for a tenant-scoped workflow definition that supports manual triggering
   - Confirm a workflow instance record is created with:
     - correct `company_id`
     - correct `workflow_definition_id`
     - `trigger_source = manual`
     - expected initial `state` and `current_step`

4. Verify scheduled trigger path:
   - Execute the scheduler job/worker directly or through an existing hosted service test harness
   - Confirm a scheduled workflow definition produces a workflow instance with:
     - `trigger_source = schedule`
     - appropriate `trigger_ref`/correlation if implemented
   - Re-run the same schedule tick scenario and confirm duplicate protection if implemented

5. Verify internal event trigger path:
   - Publish/raise the relevant internal event in a test or integration harness
   - Confirm matching workflow definition(s) create instance(s) with:
     - `trigger_source = event`
     - `trigger_ref` populated from event identity/type if supported

6. Verify negative cases:
   - Attempt manual start for a workflow that does not allow manual triggering
   - Attempt start for inactive or wrong-tenant definition
   - Confirm safe validation/authorization failure and no instance creation

7. If migrations were added:
   - Apply migration locally
   - Confirm schema matches entity mappings and tests still pass

# Risks and follow-ups
- **Existing workflow model may be partial or inconsistent**
  - Inspect first and adapt rather than forcing the architecture doc literally into code.
- **Schedule metadata may not yet be formalized**
  - If absent, keep schedule support minimal and driven by existing `definition_json` conventions.
- **Internal event infrastructure may vary**
  - Reuse the project’s current event pattern; do not introduce a second event bus abstraction.
- **Idempotency for scheduled/event starts may need hardening**
  - Implement basic safeguards now if easy; otherwise note follow-up work for duplicate prevention.
- **Definition versioning may be underspecified in current code**
  - Preserve current behavior and document any gap if full version pinning is not yet implemented.
- **Audit/business events may not yet exist for workflow starts**
  - Hook into existing audit patterns if available; otherwise avoid inventing a large audit subsystem in this task.

Follow-up candidates after this task:
- richer schedule configuration and cron validation
- event-to-workflow mapping configuration UX
- workflow instance query endpoints/UI
- blocked/failed step exception surfacing
- stronger idempotency and correlation handling for scheduler/event triggers