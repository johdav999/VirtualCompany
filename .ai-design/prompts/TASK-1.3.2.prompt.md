# Goal
Implement `TASK-1.3.2` for **US-1.3 ST-A203 — Escalation policies** by adding durable escalation state tracking so the system does **not repeatedly trigger the same escalation level for the same source entity** unless that entity is resolved and later re-opened.

The implementation prompt should direct the coding agent to:
- persist escalation records with `policyId`, `sourceEntityId`, `escalationLevel`, `reason`, and `triggeredAt`
- enforce one-time execution per policy level per source entity lifecycle
- support reset behavior when the source entity is resolved and then re-opened
- write policy evaluation results and escalation actions to the audit log with `correlationId`
- fit the existing modular monolith / clean architecture / PostgreSQL design

# Scope
In scope:
- Domain model changes for escalation state tracking
- Persistence changes and migration(s) for escalation records
- Application/service logic updates to check prior escalation state before triggering
- Reset/re-open behavior for source entities such as alerts/tasks
- Audit event emission for both evaluation results and actual escalation actions
- Correlation ID propagation through evaluation and escalation execution paths
- Automated tests covering idempotency and re-open behavior

Out of scope unless already present and trivial to wire:
- New UI screens
- Broad workflow engine redesign
- Notification delivery implementation beyond existing hooks
- New external integrations
- Refactoring unrelated policy engine behavior

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- Domain entities / value objects for escalation policy evaluation and escalation records
- Application commands/handlers/services for escalation evaluation
- Repository interfaces and implementations
- EF Core entity configurations / DbContext mappings
- PostgreSQL migration files
- Audit logging abstractions and implementations
- Event handlers or workflow/task state transition handlers for resolved/re-opened reset behavior
- Tests for escalation execution and audit traceability

If the repository already has similarly named concepts, prefer extending existing files over creating parallel abstractions.

# Implementation plan
1. **Discover existing escalation and audit architecture**
   - Search for:
     - escalation policy models/services
     - task/alert lifecycle state transitions
     - audit event persistence
     - correlation ID handling
   - Identify the current source entities involved in escalation evaluation, especially tasks and alerts.
   - Determine whether escalation policy evaluation already exists and where duplicate-trigger prevention belongs.

2. **Add a durable escalation record model**
   - Introduce or extend a persistence-backed entity/table for escalation executions.
   - Minimum fields:
     - `Id`
     - `CompanyId` / tenant key if applicable
     - `PolicyId`
     - `SourceEntityType` if needed by current design
     - `SourceEntityId`
     - `EscalationLevel`
     - `Reason`
     - `TriggeredAt`
     - `CorrelationId`
     - lifecycle discriminator if needed to support re-open semantics cleanly
   - If the current model lacks lifecycle awareness, add a field such as:
     - `SourceLifecycleId`, or
     - `ReopenSequence`, or
     - equivalent version counter tied to the source entity lifecycle
   - Prefer an explicit lifecycle/version approach over deleting old escalation history.

3. **Model one-time-per-level behavior**
   - Update escalation evaluation logic so that before triggering level `N`, it checks whether an escalation record already exists for:
     - same tenant
     - same policy
     - same source entity
     - same escalation level
     - same lifecycle/version of the source entity
   - If a matching record exists, do not execute the escalation again.
   - Ensure this is enforced in both:
     - application logic
     - database constraints/indexes where practical
   - Add a unique index if possible on the natural idempotency key, e.g.:
     - `(company_id, policy_id, source_entity_id, escalation_level, lifecycle_id)`
     - include `source_entity_type` if required

4. **Support resolved and re-opened reset semantics**
   - Identify how tasks/alerts represent:
     - active/open
     - resolved/completed
     - re-opened
   - Implement lifecycle reset behavior so that once an entity is resolved and later re-opened, escalation levels may trigger again for the new lifecycle.
   - Preferred approach:
     - maintain a lifecycle/revision counter on the source entity and increment it on re-open
     - use that counter in escalation uniqueness checks
   - If source entities already have status history/versioning, reuse it rather than inventing a second mechanism.

5. **Persist escalation actions**
   - When an escalation condition is met and not previously triggered for the current lifecycle:
     - create the escalation record
     - persist it transactionally with any related state changes
   - Include the required fields from acceptance criteria exactly:
     - `policyId`
     - `sourceEntityId`
     - `escalationLevel`
     - `reason`
     - `triggeredAt`

6. **Audit evaluation results and actions**
   - For every policy evaluation, write an audit event capturing:
     - policy evaluated
     - source entity
     - evaluation outcome (matched / not matched / suppressed due to prior trigger)
     - correlation ID
   - For every actual escalation action, write an audit event capturing:
     - escalation created
     - level
     - reason
     - source entity
     - policy ID
     - correlation ID
   - Keep audit entries concise and operational; do not log chain-of-thought.
   - Reuse existing audit module patterns if present.

7. **Propagate correlation ID**
   - Ensure the evaluation entry point accepts or resolves a `correlationId`.
   - Pass it through:
     - policy evaluation
     - escalation creation
     - audit logging
   - If the app already uses request-scoped correlation IDs, reuse that mechanism.
   - For background workers, generate or forward a correlation ID consistently.

8. **Add migration and mappings**
   - Add/update EF Core configuration and PostgreSQL migration(s).
   - Include:
     - table definition
     - indexes
     - uniqueness constraint for idempotent escalation level execution
   - Keep migration naming aligned with repository conventions.

9. **Add tests**
   - Add focused tests for:
     - escalation record created when condition first matches
     - same policy level does not trigger twice for same source entity lifecycle
     - different escalation levels can still trigger independently
     - after resolve + re-open, the same level can trigger again
     - audit events are written for evaluation and action
     - correlation ID is persisted/traceable
   - Prefer integration tests where persistence behavior and uniqueness constraints matter.

10. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - domain rules in domain/application layers
     - persistence in infrastructure
     - API only for wiring
   - Do not add direct DB access from controllers or tools.
   - Keep tenant scoping enforced in all queries and writes.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal migration flow.

4. Manually validate logic through tests or a small harness:
   - evaluate a policy for a task/alert that meets level 1 threshold
   - confirm one escalation record is created
   - evaluate again with same inputs
   - confirm no second level 1 escalation record is created
   - resolve the entity, then re-open it
   - evaluate again
   - confirm level 1 can trigger again for the new lifecycle

5. Verify audit traceability:
   - confirm evaluation result audit event exists
   - confirm escalation action audit event exists when triggered
   - confirm both include the same `correlationId`

6. Verify tenant safety:
   - ensure duplicate checks and queries are tenant-scoped

# Risks and follow-ups
- **Lifecycle ambiguity risk:** if tasks/alerts do not currently model re-open lifecycle explicitly, adding correct reset semantics may require a small schema extension on source entities.
- **Concurrency risk:** repeated evaluations from workers/webhooks could race; mitigate with a DB uniqueness constraint plus graceful handling of duplicate insert attempts.
- **Audit consistency risk:** if audit logging is currently best-effort only, ensure escalation creation and audit emission are at least reliably persisted or outboxed according to existing patterns.
- **Cross-entity variation risk:** alerts and tasks may have different resolution/re-open semantics; normalize carefully without over-generalizing.
- **Follow-up suggestion:** consider a dedicated escalation evaluation result model or richer audit taxonomy later if product needs reporting on suppressed vs triggered escalations.
- **Follow-up suggestion:** if not already present, add a reusable idempotency helper/pattern for policy-triggered actions beyond escalations.