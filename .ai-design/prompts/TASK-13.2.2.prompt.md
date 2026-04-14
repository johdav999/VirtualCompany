# Goal

Implement **TASK-13.2.2 — Integrate trigger matching into platform event bus consumers** for **ST-702 Event-driven triggers**.

The coding agent should extend the existing .NET modular monolith so that supported platform events emitted on the internal event bus are consumed by trigger-aware handlers, matched against enabled triggers within the same tenant, and converted into downstream trigger evaluations/executions in an idempotent way.

This task must satisfy these outcomes:

- Triggers can be registered only for supported platform event types:
  - `task.created`
  - `task.updated`
  - `document.uploaded`
  - `workflow.state_changed`
- When one of those events is emitted, enabled triggers for the **same tenant/company** are discovered and evaluated.
- Trigger payloads passed downstream include:
  - event metadata
  - source entity identifiers
  - tenant/company identifier
  - correlation identifier
- Duplicate delivery of the same event must **not** create duplicate agent executions for the same trigger + event identifier.
- Unsupported event types must be rejected during trigger creation.

# Scope

In scope:

- Inspect the current trigger model, event bus abstractions, outbox/inbox/event consumer patterns, and workflow/task trigger execution flow.
- Add or extend a canonical supported platform event type list.
- Enforce supported event type validation at trigger creation/update boundaries.
- Integrate trigger matching into platform event consumers for the supported event types.
- Ensure tenant-scoped trigger lookup and evaluation.
- Add idempotency protection so duplicate event delivery does not create duplicate executions.
- Ensure trigger evaluation payloads include required metadata and identifiers.
- Add/adjust tests at application/integration level.

Out of scope unless already partially implemented and required to complete this task cleanly:

- New UI for trigger management.
- New external broker infrastructure.
- Broad redesign of the event bus.
- Support for additional event types beyond the four listed above.
- Full workflow builder changes unrelated to event-trigger matching.

# Files to touch

Likely areas to inspect and update first:

- `src/VirtualCompany.Domain/**`
  - Trigger aggregate/entity/value objects
  - Event type enums/constants
  - Validation rules/specifications
  - Domain events if trigger registration emits them
- `src/VirtualCompany.Application/**`
  - Trigger create/update command handlers
  - Event consumer handlers / notification handlers
  - Trigger matching service / orchestration service
  - Idempotency interfaces and application services
  - DTOs for trigger payloads
- `src/VirtualCompany.Infrastructure/**`
  - Persistence mappings
  - Repositories for trigger lookup by tenant + event type + enabled state
  - Event bus consumer wiring
  - Idempotency persistence store / unique constraints / migrations
  - Outbox/inbox support if used for internal event delivery
- `src/VirtualCompany.Api/**`
  - DI registration if consumers/services are wired here
  - API validation surface if trigger creation endpoints live here
- `tests/VirtualCompany.Api.Tests/**`
  - End-to-end or integration tests around trigger creation and event handling

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If migrations are needed, place them in the project’s active migration location, not the archive docs folder.

# Implementation plan

1. **Discover the existing trigger and event architecture**
   - Find where triggers are modeled and persisted.
   - Find how internal platform events are represented and consumed.
   - Identify whether the codebase uses:
     - MediatR notifications
     - integration event handlers
     - outbox-dispatched internal events
     - background inbox processors
   - Reuse existing patterns rather than inventing a parallel mechanism.

2. **Define the supported platform event contract**
   - Introduce or centralize a supported event type definition, preferably as constants or a value object in a shared domain/application location.
   - Supported values must map exactly to the accepted platform events:
     - `task.created`
     - `task.updated`
     - `document.uploaded`
     - `workflow.state_changed`
   - If the system already has typed domain events, create a mapping layer from typed events to canonical trigger event type names.

3. **Reject unsupported event types during trigger creation**
   - Update trigger create/update validation so unsupported event types fail fast with a clear validation error.
   - Ensure this validation is enforced server-side in the application/domain layer, not only at API request binding.
   - If there is both create and update/edit flow, validate both.
   - Preserve existing validation style and error result conventions.

4. **Add tenant-scoped trigger matching**
   - In the consumer path for each supported event, resolve:
     - company/tenant id
     - event id
     - correlation id
     - source entity type/id
     - event metadata
   - Query only enabled triggers for:
     - the same tenant/company
     - matching event type
   - Do not evaluate triggers across tenant boundaries.
   - If trigger conditions/filters already exist, invoke the existing evaluator rather than duplicating logic.

5. **Build the trigger payload shape**
   - Ensure the payload passed into trigger evaluation/execution includes:
     - canonical event type
     - event id
     - correlation id
     - tenant/company id
     - occurred timestamp if available
     - source entity identifiers
     - event metadata/body
   - Reuse existing DTOs if present; otherwise add a dedicated application contract for event-trigger payloads.
   - Keep payload serializable and audit-friendly.

6. **Implement idempotency for duplicate event delivery**
   - Add a durable deduplication mechanism keyed by at least:
     - trigger id
     - event id
   - If the architecture already has inbox/idempotency tables, integrate with that pattern.
   - Otherwise add a persistence-backed record such as `trigger_event_deliveries` / `trigger_execution_receipts` with a unique constraint on `(company_id, trigger_id, event_id)`.
   - The consumer flow should:
     - check/insert idempotency receipt atomically
     - skip downstream execution if already processed
   - Make sure retries remain safe under concurrent delivery.

7. **Wire supported event consumers**
   - For each supported platform event source, integrate the trigger matching call in the existing consumer/handler:
     - task created
     - task updated
     - document uploaded
     - workflow state changed
   - Prefer a shared service like `IPlatformEventTriggerMatcher` or similar to avoid duplicated logic across handlers.
   - Keep handlers thin:
     - translate event -> canonical trigger payload
     - invoke matcher
     - log outcome with tenant and correlation context

8. **Persist and log useful operational details**
   - Add structured logs for:
     - event received
     - trigger count matched
     - duplicate skipped
     - unsupported/malformed event ignored if applicable
   - Include correlation id and tenant/company id where available.
   - Do not confuse technical logs with business audit events unless the codebase already records trigger evaluations as audit records.

9. **Add/adjust tests**
   - Add tests covering:
     - trigger creation rejects unsupported event type
     - supported event delivery matches only enabled triggers in same tenant
     - payload contains tenant id, correlation id, source entity ids, and metadata
     - duplicate event delivery does not create duplicate execution for same trigger + event id
     - triggers in another tenant are not evaluated
   - Prefer integration tests that exercise the real persistence and consumer pipeline where feasible.

10. **Keep implementation aligned with existing architecture**
   - Respect modular monolith boundaries:
     - domain rules in Domain
     - orchestration/use cases in Application
     - persistence/event wiring in Infrastructure
   - Avoid direct DB access from consumers if repositories/services already exist.
   - Keep contracts typed and explicit.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests to prove acceptance criteria:
   - Creating a trigger with `task.created` succeeds.
   - Creating a trigger with an unsupported event type fails with validation error.
   - Emitting a supported event evaluates only enabled triggers for the same tenant.
   - Trigger payload includes:
     - event metadata
     - source entity identifiers
     - tenant/company identifier
     - correlation identifier
   - Re-delivering the same event id does not create a second execution for the same trigger.
   - A trigger in tenant B is not matched for an event from tenant A.

4. If migrations are introduced:
   - Generate/apply them using the repository’s established migration workflow.
   - Verify unique constraint/index for idempotency works as intended.

5. Manually inspect logs or test assertions for:
   - correlation id propagation
   - duplicate skip behavior
   - matched trigger counts

# Risks and follow-ups

- **Unknown existing trigger model:** The repository may already have partial trigger/event support under different naming. Reuse and extend rather than duplicating concepts.
- **Event identity ambiguity:** If current platform events do not expose a stable event id, you may need to add one or derive one from the outbox/inbox message identity. Prefer a true stable event identifier.
- **Correlation id gaps:** Some event producers may not currently propagate correlation ids. If missing, thread through existing request/job correlation infrastructure.
- **Concurrency/idempotency race conditions:** Dedup must be atomic and persistence-backed; in-memory checks are insufficient.
- **Migration impact:** If adding a new dedup table or unique index, ensure it is tenant-aware and compatible with current schema conventions.
- **Consumer duplication:** Multiple handlers may currently react to the same event. Keep trigger matching centralized in one shared service to avoid inconsistent behavior.
- **Follow-up likely needed:** If this task only integrates consumers, a later task may be needed for richer trigger condition filtering, audit surfacing, or UI support for supported event type selection.