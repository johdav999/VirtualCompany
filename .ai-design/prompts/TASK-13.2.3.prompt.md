# Goal
Implement backlog task **TASK-13.2.3 — Implement idempotency handling for event-triggered execution requests** for story **ST-702 — Event-driven triggers** in the existing .NET modular monolith.

The implementation must ensure that when supported internal platform events are emitted and matching enabled triggers are evaluated within the same tenant, **duplicate delivery of the same event does not create duplicate agent executions for the same trigger and event identifier**.

Preserve tenant isolation, align with the existing architecture, and keep the design production-safe for retries/background processing.

# Scope
Implement the minimum complete vertical slice needed to satisfy the acceptance criteria for event-triggered execution idempotency, including:

- Supported trigger event type validation at trigger creation time:
  - `task_created`
  - `task_updated`
  - `document_uploaded`
  - `workflow_state_changed`
- Event-trigger evaluation scoped to the same tenant/company boundary
- Trigger payload composition including:
  - event metadata
  - source entity identifiers
  - tenant/company identifier
  - correlation identifier
- Idempotent execution behavior so duplicate delivery of the same event does not create duplicate agent executions for the same trigger + event identifier
- Tests covering:
  - supported vs unsupported event types
  - tenant scoping
  - duplicate event delivery behavior
  - payload shape

Out of scope unless required by existing code paths:

- New UI beyond what is necessary for API contract compatibility
- New external broker infrastructure
- Broad workflow builder changes
- Refactoring unrelated trigger/scheduler/manual trigger code
- Full event bus redesign beyond what is needed for reliable idempotent event-trigger processing

# Files to touch
Inspect the solution first and then modify the relevant files in these likely areas:

- `src/VirtualCompany.Domain/**`
  - Trigger/event domain entities, enums, value objects, domain rules
- `src/VirtualCompany.Application/**`
  - Commands/handlers for trigger creation
  - Event-trigger evaluation services
  - Execution request creation logic
  - Idempotency coordination abstractions
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL persistence
  - Repository implementations
  - Migrations for new tables/indexes/constraints
  - Outbox/event dispatcher/background worker integration
- `src/VirtualCompany.Api/**`
  - API endpoints/contracts/validation if trigger creation is exposed here
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/contracts if used across layers
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- Potentially additional test projects if present for application/infrastructure/domain tests

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover the existing trigger and execution flow**
   - Find current implementations for:
     - trigger registration/creation
     - internal event publishing/dispatch
     - task/workflow/document event emission
     - agent execution request creation
     - outbox/background worker processing
   - Identify the canonical place where event-triggered executions are created. Idempotency must be enforced there, not only at the API edge.

2. **Define supported event types explicitly**
   - Add or update a strongly typed representation for supported platform event types, preferably an enum or constrained value object.
   - Supported values must include exactly:
     - `task_created`
     - `task_updated`
     - `document_uploaded`
     - `workflow_state_changed`
   - Reject unsupported event types during trigger creation with clear validation errors.
   - Avoid free-form strings if the codebase already uses typed contracts.

3. **Model a canonical event envelope**
   - Ensure event-trigger evaluation receives a normalized internal event envelope containing at minimum:
     - event identifier
     - event type
     - tenant/company identifier
     - correlation identifier
     - occurred timestamp
     - source entity type
     - source entity identifier
     - metadata payload
   - If an event envelope already exists, extend it rather than duplicating concepts.
   - Make sure correlation ID is propagated from the originating request/process where available, otherwise generated consistently.

4. **Add idempotency persistence for trigger-event execution requests**
   - Introduce a persistence model that records the fact that a specific trigger has already produced an execution request for a specific event within a tenant.
   - Recommended shape:
     - `id`
     - `company_id`
     - `trigger_id`
     - `event_id`
     - `execution_request_id` or linked task/workflow/execution identifier
     - `created_at`
     - optional status/diagnostic fields
   - Enforce uniqueness in PostgreSQL with a unique index/constraint on:
     - `(company_id, trigger_id, event_id)`
   - This database uniqueness is the primary guard against duplicate delivery and race conditions.

5. **Implement atomic idempotent execution creation**
   - In the application service/handler that processes a matching event for a trigger:
     - evaluate trigger eligibility within the same tenant boundary
     - build the execution payload
     - attempt to persist the idempotency record and create the execution request atomically in one transaction
   - Preferred behavior:
     - first delivery creates the execution request and idempotency record
     - duplicate delivery for the same `(company_id, trigger_id, event_id)` returns success/no-op and does not create another execution
   - Handle concurrent duplicate deliveries safely by relying on the unique constraint and translating duplicate-key exceptions into a no-op result.

6. **Ensure tenant-scoped trigger matching**
   - When an event is emitted, only evaluate enabled triggers belonging to the same tenant/company as the event.
   - Verify repository queries always filter by `company_id` and enabled status.
   - Add tests to prove cross-tenant triggers are not evaluated.

7. **Build the trigger payload contract**
   - Ensure the execution request payload for event-triggered runs includes:
     - event metadata
     - source entity identifiers
     - tenant/company identifier
     - correlation identifier
   - Keep the payload structured and stable, likely as a DTO or JSON object already used by orchestration/workflow execution.
   - If there is an existing execution request schema, extend it compatibly.

8. **Integrate with emitted platform events**
   - Wire supported event emission points if not already present:
     - task created
     - task updated
     - document uploaded
     - workflow state changed
   - Reuse existing outbox/internal event mechanisms where possible.
   - Do not introduce direct synchronous coupling if the architecture already uses outbox + background dispatch.

9. **Add migration**
   - Create an EF Core/PostgreSQL migration for the idempotency table and unique index/constraint.
   - Keep naming consistent with existing migration conventions.
   - If there is already a trigger execution log table, prefer extending it if it can safely enforce uniqueness for this use case.

10. **Add observability**
   - Log structured information for:
     - event received
     - trigger matched
     - execution created
     - duplicate event ignored due to idempotency
   - Include company/tenant ID, trigger ID, event ID, and correlation ID where applicable.
   - Keep technical logs separate from business audit events unless the codebase already records trigger execution audit entries.

11. **Test thoroughly**
   - Add tests for:
     - creating a trigger with a supported event type succeeds
     - creating a trigger with an unsupported event type fails
     - matching enabled triggers are evaluated only within the same tenant
     - payload contains metadata, source entity identifiers, tenant/company identifier, and correlation identifier
     - duplicate delivery of the same event for the same trigger creates only one execution
     - duplicate delivery under concurrency still creates only one execution
     - same event ID across different triggers can create one execution per trigger
     - same trigger and event ID across different tenants does not collide if tenant isolation allows same event IDs
   - Prefer integration tests against the real persistence layer where uniqueness/idempotency behavior matters.

12. **Keep implementation aligned with clean boundaries**
   - Domain:
     - supported event types and invariants
   - Application:
     - trigger creation validation
     - event evaluation orchestration
     - idempotent execution coordination
   - Infrastructure:
     - persistence, transaction handling, unique constraints, exception translation
   - API:
     - request validation and response mapping only

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the relevant automated tests:
   - `dotnet test`

3. Specifically verify these behaviors manually or via tests:
   - Trigger creation accepts:
     - `task_created`
     - `task_updated`
     - `document_uploaded`
     - `workflow_state_changed`
   - Trigger creation rejects unsupported event types
   - Emitting a supported event evaluates only enabled triggers in the same tenant
   - Execution payload includes:
     - event metadata
     - source entity identifiers
     - tenant/company identifier
     - correlation identifier
   - Delivering the same event twice for the same trigger results in exactly one execution request
   - Concurrent duplicate delivery also results in exactly one execution request
   - Different triggers for the same event can each create their own execution once
   - Cross-tenant duplicate identifiers do not break tenant isolation

4. If migrations are part of the normal workflow, apply and verify schema:
   - confirm the new idempotency table exists
   - confirm the unique index/constraint on `(company_id, trigger_id, event_id)` exists

5. Review logs/test output to confirm duplicate deliveries are treated as no-op rather than failures where appropriate.

# Risks and follow-ups
- **Race conditions**: In-memory deduplication is insufficient. The database unique constraint must be the source of truth.
- **Transaction boundaries**: If the idempotency record and execution request are not created atomically, duplicates may still occur.
- **Event identity quality**: If upstream event IDs are unstable or regenerated on retries, idempotency will fail. Reuse canonical event IDs from the emitter.
- **Correlation ID propagation**: Some existing flows may not consistently propagate correlation IDs; generate fallback values only when necessary.
- **Existing schema overlap**: There may already be execution/audit tables that partially cover this. Prefer extending existing structures over introducing redundant persistence.
- **Unsupported event validation drift**: Keep supported event types centralized so API validation, domain rules, and persistence stay consistent.
- **Outbox/retry semantics**: Ensure retries of outbox/background processing do not surface as errors when duplicates are ignored.
- **Future follow-up**:
  - add metrics for duplicate suppression counts
  - consider retention/cleanup policy for idempotency records
  - extend supported event catalog as new platform events are introduced
  - document the internal event envelope and idempotency contract in architecture/docs