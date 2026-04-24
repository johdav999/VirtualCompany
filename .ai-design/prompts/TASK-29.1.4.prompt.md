# Goal
Implement backlog task **TASK-29.1.4 — Add idempotency guards keyed by trigger type, entity reference, entity version, and check type** for **US-29.1 Workflow-triggered financial evaluation pipeline**.

The coding agent should add durable idempotency protection so that workflow-triggered financial checks can safely reprocess duplicate domain events without creating duplicate active insights or duplicate tasks, while preserving traceability and tenant isolation.

This work must satisfy these acceptance criteria:

- Financial checks execute automatically when **invoice**, **bill**, **payment**, **cash**, and **simulation-day-advanced** events are emitted.
- Each trigger execution writes a traceable log entry containing:
  - `tenantId`
  - `triggerType`
  - `sourceEntityId`
  - `executedChecks`
  - `startedAt`
  - `completedAt`
  - `outcome`
- Reprocessing the same trigger event for the same entity version does **not** create duplicate active insights or duplicate tasks.
- At least one integration test verifies end-to-end execution from emitted domain event to persisted check result.

Use the existing architecture and code conventions in this repository. Prefer minimal, cohesive changes that fit the modular monolith, CQRS-lite, PostgreSQL-backed persistence, and background/event-driven workflow style already described.

# Scope
In scope:

- Identify the existing financial evaluation trigger pipeline and domain event handling flow.
- Add an idempotency mechanism keyed by:
  - tenant/company identifier
  - trigger type
  - source entity reference/id
  - source entity version
  - check type
- Persist idempotency state in a durable store suitable for retries and duplicate event delivery.
- Ensure duplicate processing of the same entity version/check combination is skipped or treated as already handled.
- Ensure trigger execution logging is persisted with the required fields.
- Ensure automatic execution wiring exists for the required trigger event types:
  - invoice
  - bill
  - payment
  - cash
  - simulation-day-advanced
- Prevent duplicate downstream artifacts, especially:
  - duplicate active insights
  - duplicate tasks
- Add or update integration test coverage for end-to-end event emission to persisted result.

Out of scope unless required by existing design:

- Re-architecting the event bus or outbox system.
- Broad refactors unrelated to financial evaluation triggers.
- UI work.
- Mobile work.
- Introducing a new external broker or infrastructure dependency.

# Files to touch
Start by inspecting and then modify the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - financial check domain models
  - trigger/event contracts
  - insight/task uniqueness rules if enforced in domain
- `src/VirtualCompany.Application/**`
  - event handlers / workflow trigger handlers
  - financial evaluation orchestration services
  - commands for creating insights/tasks/check results
  - logging/audit application services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence configuration
  - repositories
  - migrations or schema mappings
  - outbox/inbox/idempotency persistence
- `src/VirtualCompany.Api/**`
  - only if event wiring or DI registration lives here
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for emitted event → handler → persisted check result
- `README.md` or migration docs only if repository conventions require documentation updates

Also inspect for existing equivalents before creating anything new, especially:

- event inbox/outbox tables
- execution log tables
- workflow instance logs
- audit event persistence
- existing idempotency key abstractions
- unique indexes for tasks/insights/check results

# Implementation plan
1. **Discover the current financial trigger pipeline**
   - Find the existing implementation for workflow-triggered financial evaluation.
   - Identify:
     - domain events for invoice, bill, payment, cash, and simulation-day-advanced
     - where those events are emitted
     - where they are consumed
     - how financial checks are selected and executed
     - how insights/tasks/check results are persisted
   - Reuse existing abstractions if there is already an inbox processor, event dispatcher, or execution log model.

2. **Define the idempotency contract**
   - Introduce or extend a durable idempotency record for financial trigger executions.
   - The effective uniqueness key must include:
     - `tenantId` / `companyId`
     - `triggerType`
     - `sourceEntityId` or equivalent entity reference
     - `sourceEntityVersion`
     - `checkType`
   - If the pipeline executes multiple checks per trigger, idempotency should be enforced per check type, not only per event envelope.
   - Prefer a database-enforced uniqueness constraint/index to guarantee correctness under concurrency.

3. **Add persistence model and schema**
   - Create a persistence entity/table if one does not already exist, such as a financial check execution/idempotency table.
   - Include enough fields to support:
     - uniqueness key
     - execution status
     - timestamps
     - correlation/traceability
     - optional event metadata
   - Add EF configuration and migration if this repo uses migrations in-source.
   - Prefer explicit unique index over in-memory locking.

4. **Implement execution flow with idempotency guard**
   - In the event handler or orchestration service:
     - resolve tenant
     - resolve trigger type
     - resolve source entity id/reference
     - resolve source entity version
     - determine applicable check types
   - Before executing each check:
     - attempt to create/reserve the idempotency record
     - if a duplicate exists for the same key, skip execution and record a duplicate/no-op outcome as appropriate
   - Ensure behavior is safe under concurrent duplicate deliveries.
   - If a check previously completed successfully for the same key, do not recreate insights/tasks.
   - If there is a partial failure model already in place, align with it rather than inventing a conflicting retry semantic.

5. **Prevent duplicate active insights and duplicate tasks**
   - Review how insights and tasks are created from financial checks.
   - Add guards at the creation boundary as needed:
     - either by checking existing active records using the same business identity
     - or by adding unique constraints/business keys
   - Prefer business-level uniqueness tied to the same trigger/check identity if the current model allows it.
   - Ensure duplicate event replay for the same entity version cannot create another active insight/task even if the handler is retried.

6. **Add trigger execution logging**
   - Persist a traceable log entry for each trigger execution containing:
     - `tenantId`
     - `triggerType`
     - `sourceEntityId`
     - `executedChecks`
     - `startedAt`
     - `completedAt`
     - `outcome`
   - If an existing audit/execution log table already fits, extend and reuse it.
   - If not, add a dedicated trigger execution log entity/table.
   - Make sure logs are business-traceable, not only technical logger output.
   - Define `outcome` clearly, e.g. `Succeeded`, `PartiallySucceeded`, `SkippedAsDuplicate`, `Failed`.

7. **Wire required trigger types**
   - Ensure the automatic execution path is active for:
     - invoice emitted events
     - bill emitted events
     - payment emitted events
     - cash emitted events
     - simulation-day-advanced emitted events
   - If some are already wired, preserve existing behavior and only fill gaps.
   - Keep tenant scoping enforced throughout.

8. **Add integration test coverage**
   - Add at least one integration test in `tests/VirtualCompany.Api.Tests` that verifies:
     - a relevant domain event is emitted or simulated
     - the financial evaluation pipeline runs
     - a check result is persisted
     - a trigger execution log is persisted
     - replaying the same event for the same entity version does not create duplicates
   - Prefer a test that exercises the real application wiring rather than mocking the entire pipeline.
   - If practical, assert:
     - one persisted check result
     - one active insight/task
     - duplicate replay does not increase counts
     - log outcome reflects duplicate handling or single execution semantics

9. **Keep implementation aligned with repository patterns**
   - Follow existing naming, DI registration, MediatR/handler patterns, EF configuration style, and test conventions.
   - Avoid introducing parallel abstractions if the repo already has a standard for:
     - domain events
     - background processing
     - audit logging
     - idempotency/inbox processing

10. **Document assumptions in code comments only where necessary**
   - Add concise comments only for non-obvious concurrency/idempotency decisions.
   - Do not add broad speculative documentation.

# Validation steps
1. Inspect and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically verify the new/updated integration test covers:
   - event-driven financial check execution
   - persisted check result
   - persisted trigger execution log with required fields
   - duplicate replay for same entity version does not create duplicate active insights/tasks

4. If migrations are added, ensure:
   - migration compiles
   - schema mappings are valid
   - unique index/constraint exists for the idempotency key

5. Manually review for correctness:
   - tenant/company scoping is included in all queries and uniqueness keys
   - entity version is part of the idempotency key
   - check type is part of the idempotency key
   - duplicate handling is concurrency-safe
   - no duplicate downstream artifacts are created on replay

6. In the final implementation summary, report:
   - files changed
   - idempotency key shape
   - where duplicate prevention is enforced
   - what integration test was added
   - any assumptions made about existing event contracts or entity version fields

# Risks and follow-ups
- **Risk: entity version may not exist consistently across all trigger sources.**
  - If missing, first locate the canonical version field already used by those entities/events.
  - If some events lack version data, use the closest existing canonical revision field and clearly note the assumption in the final summary.

- **Risk: duplicate prevention only at handler level may still allow duplicate tasks/insights under race conditions.**
  - Prefer DB uniqueness or business-key enforcement at persistence boundaries in addition to handler checks.

- **Risk: existing event processing may already have inbox-level idempotency.**
  - Do not assume inbox dedupe is sufficient; this task requires business idempotency by trigger/entity-version/check-type.

- **Risk: logging may currently exist only in technical logs.**
  - Acceptance requires persisted, traceable business execution logging.

- **Risk: simulation-day-advanced may be modeled differently from entity-backed events.**
  - Use a stable source entity reference for that trigger, such as simulation/workspace/day key, while still including tenant and version semantics if available.

Follow-ups to note if not fully addressed by this task:
- broader end-to-end coverage for all five trigger types
- explicit business-key uniqueness for insights/tasks if current schema still allows semantic duplicates from non-trigger paths
- operational dashboards or audit views for trigger execution logs