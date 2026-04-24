# Goal
Implement backlog task **TASK-29.1.1** by adding a **FinanceWorkflowTriggerService** that automatically reacts to emitted finance-related domain events and runs the financial evaluation pipeline for:
- invoice events
- bill events
- payment events
- cash events
- simulation-day-advanced / simulation tick events

The implementation must satisfy these outcomes:
- financial checks run automatically when supported events are emitted
- each trigger execution persists a traceable log entry with:
  - `tenantId`
  - `triggerType`
  - `sourceEntityId`
  - `executedChecks`
  - `startedAt`
  - `completedAt`
  - `outcome`
- reprocessing the same trigger event for the same entity version is idempotent and does **not** create duplicate active insights or duplicate tasks
- at least one integration test verifies end-to-end flow from emitted domain event to persisted check result

Work within the existing **.NET modular monolith** structure and preserve clean boundaries between Domain, Application, Infrastructure, and tests.

# Scope
In scope:
- discover the existing finance evaluation/check pipeline and integrate trigger-based execution into it
- add a new application service named `FinanceWorkflowTriggerService`
- support handlers for these trigger categories:
  - invoice
  - bill
  - payment
  - cash
  - simulation tick / simulation day advanced
- subscribe the service to the project’s existing internal domain event mechanism, notification pipeline, outbox/inbox pattern, or background execution path already used in the codebase
- persist a dedicated trigger execution log/audit record suitable for traceability and idempotency
- enforce idempotent processing for the same entity version / same trigger identity
- ensure duplicate active insights and duplicate tasks are not created on replay
- add at least one integration test covering emitted event -> trigger service -> check execution -> persisted result/log

Out of scope unless required by existing patterns:
- redesigning the finance domain model
- introducing a new external message broker
- broad UI work
- changing unrelated workflow infrastructure
- implementing a generic workflow engine if a finance-specific trigger service is sufficient
- adding speculative abstractions not justified by current code patterns

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Application/**`
  - add `FinanceWorkflowTriggerService`
  - add trigger DTOs/commands/interfaces if needed
  - add event handlers/notification handlers for finance events
  - add idempotency coordination logic if application-layer owned
- `src/VirtualCompany.Domain/**`
  - finance domain events, value objects, enums, or contracts if missing
  - trigger log entity or domain model if business-audit persistence belongs here
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence mappings/configurations
  - repositories for trigger logs / idempotency records
  - event dispatch wiring / background worker registration
- `src/VirtualCompany.Api/**`
  - DI registration if composition root lives here
- `tests/VirtualCompany.Api.Tests/**`
  - integration test(s) for end-to-end event emission and persisted results

Also inspect:
- existing migrations strategy and whether new EF migration files are expected in this repo
- existing finance check result, insight, and task persistence paths
- existing audit/log tables and whether this should reuse them or add a dedicated finance trigger execution table

# Implementation plan
1. **Discover existing finance pipeline and event infrastructure**
   - Find current finance evaluation services, check runners, insight creation logic, and task creation logic.
   - Identify existing domain events for invoice, bill, payment, cash, and simulation-day advancement. If exact events do not exist, locate the nearest existing emitted events and align to current naming conventions.
   - Identify how internal events are handled today:
     - MediatR notifications
     - domain event dispatcher
     - outbox-backed integration events
     - background jobs
   - Reuse the established pattern rather than inventing a parallel mechanism.

2. **Define the trigger service contract**
   - Add `FinanceWorkflowTriggerService` in the Application layer.
   - Give it a focused API that accepts a normalized trigger request, e.g. conceptually:
     - tenant/company id
     - trigger type
     - source entity id
     - source entity version or event id
     - occurred at
     - optional metadata
   - Keep the service orchestration-focused: normalize trigger input, enforce idempotency, invoke finance checks, persist execution log, and return outcome.

3. **Normalize supported trigger types**
   - Introduce a small enum or strongly typed discriminator for:
     - Invoice
     - Bill
     - Payment
     - Cash
     - SimulationTick / SimulationDayAdvanced
   - Map each emitted domain event handler to one of these trigger types.
   - Preserve tenant isolation by always carrying `company_id` / `tenantId`.

4. **Implement event handlers**
   - Add handlers for the exact supported event categories using the project’s existing event handling mechanism.
   - Each handler should:
     - extract tenant/company id
     - extract source entity id
     - extract entity version, revision, sequence, or event id for idempotency
     - call `FinanceWorkflowTriggerService`
   - If simulation-day-advanced is a scheduled/system event, wire it through the same service with a synthetic source entity id or simulation context id consistent with existing patterns.

5. **Add idempotent trigger execution**
   - Ensure replay of the same trigger for the same entity version does not re-create active insights or tasks.
   - Prefer a durable idempotency key such as:
     - `tenantId + triggerType + sourceEntityId + sourceEntityVersion`
     - or `tenantId + eventId` if event ids are stable and version-aware
   - Persist this in a dedicated execution log / trigger record with a uniqueness constraint if possible.
   - On duplicate processing attempt:
     - short-circuit safely
     - record outcome appropriately if current patterns require it
     - do not create duplicate active insights or duplicate tasks
   - Also review downstream insight/task creation logic for existing dedupe hooks and reuse them if available.

6. **Persist traceable execution logs**
   - Add or reuse a persistence model for finance trigger executions.
   - Required fields:
     - `tenantId`
     - `triggerType`
     - `sourceEntityId`
     - `executedChecks`
     - `startedAt`
     - `completedAt`
     - `outcome`
   - Recommended additional fields if aligned with current conventions:
     - entity version / event id
     - correlation id
     - error details / failure reason
     - created insight ids / task ids
   - If the architecture already distinguishes technical logs from business audit records, persist this as a business-domain trace record, not only structured app logging.

7. **Invoke the finance evaluation/check pipeline**
   - Reuse the existing finance check runner rather than duplicating logic.
   - Capture which checks were executed and include them in the persisted log.
   - Ensure the service can handle different trigger types, potentially selecting different check sets depending on event type.
   - If simulation tick should run broader periodic checks, keep that branching inside the service or a dedicated strategy component.

8. **Protect against duplicate active insights and tasks**
   - Inspect how insights/tasks are currently created.
   - Add dedupe guards based on business identity, for example:
     - same tenant
     - same check type
     - same source entity
     - same entity version or same active condition
   - Prefer repository-level existence checks plus database uniqueness where practical.
   - If active insights are stateful, ensure replay updates or no-ops instead of inserting duplicates.

9. **Persistence and configuration**
   - Add EF Core entity configuration and repository support in Infrastructure.
   - Add migration if this repo uses active EF migrations for schema evolution.
   - Register the new service and handlers in DI.
   - Keep all queries tenant-scoped.

10. **Integration test**
   - Add at least one integration test in `tests/VirtualCompany.Api.Tests`.
   - Test scenario should cover:
     - seed tenant/company and required finance data
     - emit a supported domain event
     - allow handler/service execution
     - assert persisted check result exists
     - assert persisted trigger execution log contains required fields
     - replay the same event / same entity version
     - assert no duplicate active insights and no duplicate tasks are created
   - Prefer the most realistic event path available in the test harness rather than directly unit-calling the service.

11. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep business audit persistence separate from technical logging.
   - Preserve tenant isolation in all repositories and handlers.
   - Use CQRS-lite and outbox/event-driven patterns already present in the solution.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the relevant tests:
   - `dotnet test`

3. Verify the implementation manually through tests/code review against acceptance criteria:
   - supported invoice, bill, payment, cash, and simulation-day-advanced events all route into `FinanceWorkflowTriggerService`
   - each execution persists a log with:
     - `tenantId`
     - `triggerType`
     - `sourceEntityId`
     - `executedChecks`
     - `startedAt`
     - `completedAt`
     - `outcome`
   - replaying the same trigger for the same entity version is idempotent
   - no duplicate active insights are created
   - no duplicate tasks are created
   - at least one integration test proves end-to-end event-to-persistence behavior

4. If migrations are added, verify schema consistency using the repo’s established migration workflow.

# Risks and follow-ups
- **Event model mismatch:** the exact finance domain events may not yet exist for all trigger types. If missing, add the smallest compatible event handlers or adapters rather than inventing a broad new eventing subsystem.
- **Idempotency ambiguity:** if entity version is not currently modeled, use the most stable replay-safe identifier available and document the limitation clearly in code comments or task notes.
- **Duplicate prevention may need downstream fixes:** even with trigger-level idempotency, insight/task creation paths may still need their own dedupe protections.
- **Audit vs log placement:** prefer persisted business trace records over app logs only; if an existing audit table already fits, reuse it instead of creating a redundant table.
- **Simulation tick semantics:** clarify whether “simulation tick” and “simulation-day-advanced” are the same event in this codebase and map consistently.
- **Follow-up candidate:** after this task, consider extracting a reusable trigger execution/idempotency pattern for other workflow-triggered evaluation pipelines if similar behavior appears in non-finance modules.