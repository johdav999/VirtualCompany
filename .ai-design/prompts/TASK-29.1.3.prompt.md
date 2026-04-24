# Goal
Implement backlog task **TASK-29.1.3 — Persist workflow trigger execution logs and correlation identifiers for debugging** for story **US-29.1 Workflow-triggered financial evaluation pipeline**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:

- automatically reacts to emitted financial domain events for:
  - invoice
  - bill
  - payment
  - cash
  - simulation-day-advanced
- persists a **workflow trigger execution log** for every trigger run
- carries and persists a **correlation identifier** end-to-end for debugging and traceability
- enforces **idempotent reprocessing** so the same trigger event for the same entity version does not create duplicate active insights or duplicate tasks
- includes at least one **integration test** proving end-to-end flow from emitted domain event to persisted check result

Use the existing architecture and conventions already present in the repository. Prefer extending current workflow/event-processing patterns over introducing a new framework or parallel pipeline.

# Scope
In scope:

- Find the current financial evaluation workflow/event trigger pipeline and extend it
- Add a persistent business-level log entity/table for workflow trigger executions
- Ensure each execution log captures:
  - `tenantId`
  - `triggerType`
  - `sourceEntityId`
  - `executedChecks`
  - `startedAt`
  - `completedAt`
  - `outcome`
  - correlation identifier(s) needed for debugging
- Propagate correlation IDs from event emission/handling through workflow/check execution and persistence
- Add idempotency protections for reprocessing the same trigger event for the same entity version
- Persist enough metadata to diagnose retries, duplicates, and outcomes
- Add/update integration tests

Out of scope unless required by existing code structure:

- New UI screens
- Broad observability platform changes unrelated to this workflow
- Reworking the entire event bus/outbox architecture
- Large refactors outside the financial workflow module

# Files to touch
Inspect the solution first, then update the actual files that match the existing implementation. Expect to touch files in these areas:

- **Domain**
  - `src/VirtualCompany.Domain/...`
  - financial workflow/check entities, domain events, value objects, enums
- **Application**
  - `src/VirtualCompany.Application/...`
  - event handlers, workflow orchestration services, commands, DTOs, idempotency logic
- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/...`
  - EF Core configurations, repositories, persistence models, migrations, correlation/log persistence
- **API** if needed for wiring only
  - `src/VirtualCompany.Api/...`
- **Tests**
  - `tests/VirtualCompany.Api.Tests/...`
  - integration tests covering emitted event -> workflow execution -> persisted results/logs

Also check whether there is an existing migrations approach in:
- `docs/postgresql-migrations-archive/README.md`

If the repo uses EF Core migrations, add one. If it uses SQL scripts or another migration mechanism, follow the established pattern exactly.

# Implementation plan
1. **Discover the existing workflow trigger path**
   - Locate:
     - financial evaluation pipeline
     - domain event definitions for invoice/bill/payment/cash/simulation-day-advanced
     - event dispatch/inbox/outbox/background worker flow
     - current persistence for check results, insights, and tasks
   - Identify the current idempotency mechanism, if any, for workflow/event processing.
   - Identify how tenant/company context is represented. Use the existing tenant field naming consistently; if the codebase uses `CompanyId` instead of `TenantId`, preserve domain consistency while still satisfying the acceptance criteria semantically.

2. **Add a persistent trigger execution log model**
   - Introduce a business persistence model such as `WorkflowTriggerExecutionLog` or equivalent in the appropriate module.
   - Include fields for:
     - primary key
     - tenant/company id
     - trigger type
     - source entity id
     - source entity version if available
     - correlation id
     - causation/event id if available
     - executed checks
     - started at
     - completed at
     - outcome
     - optional error/failure summary
     - created/updated timestamps if consistent with project conventions
   - Prefer structured persistence for `executedChecks`:
     - JSON/JSONB list if the project already uses JSON columns
     - otherwise a normalized child table only if clearly aligned with existing patterns
   - Add indexes supporting:
     - tenant/company + trigger type + source entity id
     - correlation id
     - event id / entity version idempotency lookup

3. **Add persistence configuration**
   - Update EF Core entity configuration and DbContext mappings.
   - Add the required migration/schema change.
   - Ensure PostgreSQL-friendly types are used, especially for JSONB and timestamps.

4. **Propagate correlation identifiers**
   - Reuse any existing correlation ID abstraction already present in the codebase.
   - If none exists in the workflow path, introduce a minimal shared mechanism that:
     - reads correlation ID from the incoming event/envelope if present
     - generates one if absent
     - passes it through handler/orchestrator/check execution
     - persists it in the trigger execution log and any related persisted check result/audit records where appropriate
   - Do not create a second incompatible correlation system if one already exists for structured logs or request tracing.

5. **Instrument trigger execution lifecycle**
   - At the start of handling a supported financial trigger event:
     - create a trigger execution log row with `startedAt`
     - set outcome to an in-progress/pending state if the model supports it
   - After checks complete:
     - populate `executedChecks`
     - set `completedAt`
     - set final `outcome` such as success/no-op/failed/duplicate-skipped according to existing conventions
   - On failure:
     - persist failure outcome and a concise error summary
     - rethrow only if the existing retry semantics require it
   - Ensure logs are persisted even for duplicate-skipped or failed executions where feasible.

6. **Implement idempotent reprocessing**
   - Define the idempotency key around the acceptance criteria:
     - same trigger event
     - same entity
     - same entity version
   - Prefer using an existing event ID or versioned source reference if already available.
   - Ensure reprocessing does **not** create:
     - duplicate active insights
     - duplicate tasks
   - Implement this in the application/domain layer closest to insight/task creation, not only at the event ingress, so retries remain safe.
   - If needed, add:
     - unique constraints
     - existence checks
     - upsert semantics
     - duplicate-skipped outcomes in the execution log
   - Be careful not to block legitimate reprocessing for a newer entity version.

7. **Support all required trigger types**
   - Verify the pipeline executes automatically for:
     - invoice events
     - bill events
     - payment events
     - cash events
     - simulation-day-advanced events
   - If some event types are not yet wired, add the missing subscriptions/handlers using the existing event-driven pattern.
   - Keep the implementation centralized enough that all trigger types share the same logging/idempotency behavior.

8. **Persist executed check results consistently**
   - Ensure the trigger execution log references or records which checks actually ran.
   - If check results are already persisted elsewhere, do not duplicate full result payloads unnecessarily; store check identifiers/names in the execution log and rely on existing result tables for details.
   - If no persisted check result exists today for this path, add the minimal persistence needed to satisfy the end-to-end acceptance criterion.

9. **Add integration tests**
   - Add at least one integration test in `tests/VirtualCompany.Api.Tests` that:
     - arranges a tenant/company and relevant source entity
     - emits one supported domain event
     - runs the event-processing path
     - asserts persisted check result(s)
     - asserts a persisted trigger execution log with required fields
     - asserts correlation ID presence
   - Add a second test if practical for idempotency:
     - emit/reprocess the same event for the same entity version
     - assert no duplicate active insights/tasks
     - assert either one effective result or a duplicate-skipped log outcome according to implementation
   - Make tests deterministic and avoid timing flakiness.

10. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - domain rules in Domain
     - orchestration/use cases in Application
     - persistence in Infrastructure
   - Keep technical logs separate from business audit/trigger execution persistence.
   - Prefer concise, structured business records over free-form log text.

# Validation steps
Run and verify the following after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Targeted verification**
   - Confirm migration/schema changes are included and valid
   - Confirm a supported financial event causes automatic workflow/check execution
   - Confirm a trigger execution log row is persisted with:
     - tenant/company id
     - trigger type
     - source entity id
     - executed checks
     - started at
     - completed at
     - outcome
     - correlation id
   - Confirm reprocessing the same event/entity version does not create duplicate active insights or tasks
   - Confirm at least one integration test covers emitted event -> persisted result

4. **Code quality checks**
   - Ensure nullability and async patterns match repo conventions
   - Ensure tenant scoping is enforced in queries and persistence
   - Ensure failure paths still persist useful execution log state

# Risks and follow-ups
- **Naming mismatch risk:** the backlog says `tenantId`, but the codebase may use `CompanyId`. Follow existing domain naming and document the semantic mapping in code comments/tests if needed.
- **Event version availability risk:** if source entity version is not currently present on events, you may need to extend event payloads or derive a stable idempotency key from existing metadata.
- **Duplicate prevention risk:** application-level checks alone may be race-prone under retries/concurrency; add database constraints where appropriate if the current schema allows it.
- **Correlation propagation risk:** avoid introducing a new correlation abstraction that conflicts with existing request or background-job tracing.
- **Migration risk:** ensure the chosen persistence shape is PostgreSQL-compatible and consistent with the repo’s migration strategy.
- **Follow-up suggestion:** if not already present, consider later exposing trigger execution logs in audit/explainability views, but do not add UI in this task unless already partially implemented.