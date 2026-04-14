# Goal
Implement **TASK-13.2.1 — Define event trigger schema and supported event type registry** for **ST-702 Event-driven triggers** in the existing .NET modular monolith.

Deliver the foundational backend support for **tenant-scoped event-driven triggers** by introducing:

- a **supported event type registry**
- a **trigger schema/model** for event-bound triggers
- **validation** that rejects unsupported event types at trigger creation time
- an **event payload contract** containing:
  - event metadata
  - source entity identifiers
  - tenant identifier
  - correlation identifier
- **same-tenant trigger matching/evaluation**
- **idempotency protection** so duplicate delivery of the same event does not create duplicate agent executions for the same trigger and event identifier

This task should focus on the **domain/application/infrastructure foundation**, not full end-user UX unless minimal API/controller wiring already exists and is the natural integration point.

# Scope
In scope:

- Define canonical supported platform event types:
  - `task.created`
  - `task.updated`
  - `document.uploaded`
  - `workflow.state_changed`
- Add a registry/abstraction for supported event types so the list is centralized and testable.
- Define trigger schema/entity/value objects for event triggers, including enabled/disabled state and tenant ownership.
- Define event envelope/payload contract with required metadata fields.
- Add trigger creation validation to reject unsupported event types.
- Add event matching/evaluation entry point that only considers enabled triggers within the same tenant.
- Add idempotency persistence/checking for `(tenant, trigger, eventId)` to prevent duplicate execution creation.
- Add tests covering acceptance criteria.

Out of scope unless required by existing architecture:

- Full workflow builder UI
- Rich filtering DSL beyond what is needed for this task
- Actual agent orchestration execution details beyond creating or invoking a stable application-layer execution request
- External broker integration
- Cross-tenant event fan-out

Assumptions to preserve:

- Shared-schema multi-tenancy with `company_id` / tenant scoping
- PostgreSQL persistence
- Modular monolith boundaries
- CQRS-lite application layer
- Outbox/background processing patterns may exist, but this task should remain compatible with same-process internal event handling

# Files to touch
Inspect the solution first and adapt paths/names to the existing conventions. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - trigger domain models/entities/value objects
  - event type registry abstractions/constants
  - event envelope contract if domain-owned
- `src/VirtualCompany.Application/`
  - commands/handlers for trigger creation
  - validation logic
  - event dispatch/evaluation service interfaces
  - idempotency coordination logic
- `src/VirtualCompany.Infrastructure/`
  - EF Core configurations/repositories
  - PostgreSQL persistence for triggers and processed trigger events
  - registry implementation if infrastructure-backed
- `src/VirtualCompany.Api/`
  - DI registration
  - API endpoint/controller updates if trigger creation or event ingestion endpoints already exist
- `tests/VirtualCompany.Api.Tests/`
  - integration/API tests
- potentially other test projects if present for:
  - domain tests
  - application tests
  - infrastructure tests

Likely new artifacts:

- Trigger entity/table
- Processed trigger event/idempotency table
- Supported event type registry class
- Event envelope DTO/record
- Trigger evaluation service
- Migration for new tables

If the repository already has partial trigger/workflow/event infrastructure, extend it rather than duplicating concepts.

# Implementation plan
1. **Inspect existing architecture and naming**
   - Search for existing concepts:
     - triggers
     - workflows
     - tasks
     - domain events / integration events
     - tenant/company scoping
     - idempotency
     - outbox/inbox
   - Reuse established patterns for:
     - entity base classes
     - repository interfaces
     - command handlers
     - validation
     - EF configurations
     - migrations

2. **Define supported event type registry**
   - Create a centralized registry with the canonical supported event types:
     - `task.created`
     - `task.updated`
     - `document.uploaded`
     - `workflow.state_changed`
   - Expose:
     - enumeration/list of supported types
     - `IsSupported(string eventType)`
     - optional metadata lookup if useful
   - Keep it deterministic and code-defined for now unless the codebase already uses seeded reference data.

3. **Define event trigger schema**
   - Introduce a trigger model/entity for event-driven triggers with at minimum:
     - `Id`
     - `CompanyId` / tenant id
     - `Name` or identifier if consistent with existing patterns
     - `TriggerType` = event
     - `EventType`
     - `Enabled`
     - target execution reference (agent/workflow/action target) according to existing orchestration model
     - optional filter/config JSON if the system already supports flexible trigger criteria
     - timestamps
   - Ensure the model is tenant-owned and queryable by tenant + event type + enabled state.
   - If there is already a generic trigger table/model, extend it instead of creating a parallel one.

4. **Define event payload/envelope contract**
   - Add a stable event envelope record/class containing:
     - `EventId`
     - `EventType`
     - `OccurredAt`
     - `CompanyId` / tenant identifier
     - `CorrelationId`
     - source entity identifiers:
       - `SourceEntityType`
       - `SourceEntityId`
     - metadata payload / event metadata object
   - Keep the contract serializable and suitable for internal event handling and future outbox dispatch.
   - Ensure naming aligns with existing event/message conventions.

5. **Add trigger creation validation**
   - In trigger creation/update command validation:
     - reject unsupported event types
     - reject missing required event trigger fields
   - Return field-level validation errors consistent with the project’s API conventions.
   - Add tests proving unsupported event types are rejected.

6. **Add persistence**
   - Add EF Core entity configurations and migration(s) for:
     - trigger storage if not already present
     - processed trigger events / idempotency records
   - Recommended idempotency table shape:
     - `id`
     - `company_id`
     - `trigger_id`
     - `event_id`
     - `created_execution_id` or equivalent nullable reference
     - `processed_at`
   - Add a unique constraint/index on:
     - `(company_id, trigger_id, event_id)`
   - Add indexes for trigger lookup by:
     - `(company_id, event_type, enabled)`

7. **Implement tenant-scoped trigger evaluation**
   - Create an application service such as `IEventTriggerEvaluator` / `IEventTriggerDispatcher`.
   - On receiving a supported event:
     - verify event type is supported
     - load enabled triggers matching:
       - same `company_id`
       - same `event_type`
       - enabled = true
     - evaluate each matching trigger
   - Keep evaluation within the same tenant boundary only.
   - If filtering logic exists, apply it after tenant + event type matching.

8. **Implement idempotency protection**
   - Before creating an agent execution / workflow instance / task execution for a trigger-event pair:
     - attempt to persist an idempotency record for `(company_id, trigger_id, event_id)`
     - if duplicate exists, do not create another execution
   - Prefer database-enforced uniqueness over in-memory checks.
   - Handle race conditions safely:
     - catch unique constraint violations and treat them as duplicate delivery
   - Record or return whether the event was newly processed vs already processed.

9. **Connect to execution path**
   - Integrate with the existing execution mechanism in the least invasive way:
     - create a task
     - enqueue a workflow instance
     - invoke an orchestration command
   - The key requirement is that duplicate event delivery must not create duplicate executions for the same trigger and event id.
   - If full execution plumbing is not yet present, create a clear application-layer seam and test against that seam.

10. **Add tests**
   - Add unit and/or integration tests for:
     - supported event registry contains required event types
     - unsupported event type rejected during trigger creation
     - supported event matches only enabled triggers
     - matching is tenant-scoped
     - event payload includes required metadata fields
     - duplicate delivery does not create duplicate execution for same trigger + event id
   - Prefer integration tests around persistence and uniqueness behavior.

11. **Document minimally**
   - Add concise code comments where needed.
   - If the repo has architecture docs or README sections for workflows/triggers, update them briefly with:
     - supported event types
     - event envelope fields
     - idempotency behavior

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify trigger creation validation:
   - create a trigger with `task.created` → succeeds
   - create a trigger with unsupported type like `task.deleted` → fails with validation error

4. Verify tenant-scoped matching:
   - create same event type triggers in two tenants
   - emit event for tenant A
   - confirm only tenant A enabled triggers are evaluated

5. Verify enabled-only behavior:
   - create one enabled and one disabled trigger for same tenant/event type
   - emit supported event
   - confirm only enabled trigger is evaluated

6. Verify event envelope contract:
   - confirm emitted/handled event includes:
     - event metadata
     - source entity identifiers
     - tenant identifier
     - correlation identifier

7. Verify idempotency:
   - deliver the same event twice with same `EventId`
   - confirm only one execution is created per trigger
   - if possible, add a concurrency-oriented test to validate unique constraint behavior

8. Verify migration correctness:
   - ensure new tables/indexes/constraints are created as expected
   - confirm unique constraint exists for `(company_id, trigger_id, event_id)`

# Risks and follow-ups
- **Existing trigger model may already exist**  
  Avoid creating duplicate abstractions. Extend current workflow/trigger infrastructure if present.

- **Execution target ambiguity**  
  The backlog item is about schema/registry/idempotency, not final orchestration semantics. If execution target design is incomplete, keep the integration seam explicit and minimal.

- **Event contract ownership**  
  Be careful not to mix domain events, integration events, and trigger evaluation envelopes if the codebase already distinguishes them. Reuse existing event abstractions where possible.

- **Idempotency race conditions**  
  In-memory dedupe is insufficient. Use database uniqueness and handle conflicts gracefully.

- **Tenant naming inconsistency**  
  Architecture uses both company and tenant terminology. Follow the codebase’s established naming, but preserve tenant-boundary semantics.

- **Future follow-ups**
  - richer event filters
  - event trigger management APIs/UI
  - outbox/inbox integration for internal event delivery
  - audit records for trigger evaluation outcomes
  - observability metrics for trigger match rate, duplicate suppression, and execution latency