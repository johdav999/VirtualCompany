# Goal
Implement backlog task **TASK-29.1.2 — Add trigger-to-check registration and execution orchestration with tenant-scoped dispatch** for story **US-29.1 Workflow-triggered financial evaluation pipeline**.

The coding agent should add the application, domain, infrastructure, and test support needed so that financial checks are automatically executed when supported domain events are emitted, with strict tenant scoping, traceable execution logging, and idempotent reprocessing behavior.

The implementation must satisfy these acceptance criteria:

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

Use the existing modular monolith patterns already present in the solution. Prefer extending current eventing, outbox/inbox, workflow/background execution, and persistence conventions rather than inventing a parallel mechanism.

# Scope
In scope:

- Discover the existing financial evaluation/check pipeline and how checks are currently invoked.
- Add a **trigger registration model** mapping supported trigger types to one or more financial checks.
- Add **tenant-scoped dispatch/orchestration** that reacts to emitted domain events and invokes the correct checks for the correct tenant/company only.
- Add a **trigger execution log** persisted in business storage, not just technical logs.
- Add **idempotency protections** so replaying the same trigger for the same entity version does not create duplicate active insights/tasks/check side effects.
- Add or extend integration tests covering end-to-end flow from event emission to persisted result/log.
- Keep implementation aligned with architecture:
  - shared-schema multi-tenancy
  - background/event-driven internal workflows
  - auditability as persisted domain data
  - outbox/reliable side effects where applicable

Out of scope unless required by existing design:

- New UI screens
- New external integrations
- Broad refactors unrelated to trigger orchestration
- Replacing existing event bus/outbox abstractions
- Large redesign of financial check result models

# Files to touch
Inspect first, then update the most relevant existing files. Expect changes across these projects:

- `src/VirtualCompany.Domain`
  - financial check trigger domain types/enums/value objects
  - trigger execution log entity
  - idempotency key or processed-trigger model if domain-owned
- `src/VirtualCompany.Application`
  - event handlers / notification handlers
  - orchestration service for trigger-to-check dispatch
  - tenant-scoped command/service interfaces
  - DTOs for execution logging
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configurations
  - repositories
  - event dispatch wiring
  - persistence/migrations for trigger execution logs and any idempotency table/indexes
- `src/VirtualCompany.Api`
  - DI registration if handlers/services are wired here
- `tests/VirtualCompany.Api.Tests`
  - integration test(s) for event -> dispatch -> persisted check result/log
- Potentially:
  - `README.md` or relevant docs if there is a local convention for migrations/testing
  - `docs/postgresql-migrations-archive/README.md` if migration process requires documentation alignment

Before coding, search for existing implementations or adjacent concepts using terms like:

- `FinancialCheck`
- `Insight`
- `Trigger`
- `DomainEvent`
- `Outbox`
- `Workflow`
- `Tenant`
- `CompanyId`
- `Invoice`
- `Bill`
- `Payment`
- `Cash`
- `Simulation`
- `Idempot`
- `Audit`
- `ExecutionLog`

# Implementation plan
1. **Inspect the current architecture in code**
   - Identify:
     - how domain events are defined and emitted
     - how internal event handlers are registered
     - where financial checks currently live
     - how insights/tasks/check results are persisted
     - how tenant/company scoping is enforced in repositories/services
     - whether there is already an audit/execution log pattern to reuse
   - Do not create duplicate abstractions if a workflow runner, background job, or notification handler already exists.

2. **Define supported trigger types**
   - Introduce or extend a strongly typed trigger representation for:
     - invoice emitted/changed
     - bill emitted/changed
     - payment emitted/changed
     - cash emitted/changed
     - simulation-day-advanced
   - Match naming to existing domain event conventions.
   - If entity versioning already exists, include version in the trigger context.
   - If not, derive a stable idempotency identity from event metadata plus entity/version fields already available.

3. **Add trigger-to-check registration**
   - Implement a registration mechanism that maps trigger types to one or more financial checks.
   - Prefer a simple, testable registry abstraction such as:
     - static configuration in application layer
     - DI-registered handlers per trigger type
     - or a registry service returning applicable checks
   - The registry must be easy to extend for future triggers/checks.
   - Keep it deterministic and tenant-safe.

4. **Implement tenant-scoped dispatch orchestration**
   - Add an application service/handler that receives supported domain events and:
     - resolves tenant/company context from the event
     - resolves trigger type and source entity id/version
     - finds applicable financial checks from the registry
     - executes them through the existing financial evaluation pipeline
     - persists a trigger execution log
   - Ensure dispatch never crosses tenant boundaries.
   - If current architecture uses background workers/outbox for internal events, integrate with that path rather than synchronous controller logic.

5. **Persist trigger execution logs**
   - Add a business-level persistence model for trigger execution logs with at least:
     - `tenantId`
     - `triggerType`
     - `sourceEntityId`
     - `executedChecks`
     - `startedAt`
     - `completedAt`
     - `outcome`
   - Include additional useful fields if consistent with current patterns, such as:
     - source entity version
     - correlation/event id
     - failure reason/error summary
   - Keep technical exception details out of user-facing fields if there is a business audit convention.
   - Add EF configuration and migration if needed.

6. **Add idempotency / replay safety**
   - Ensure reprocessing the same trigger event for the same entity version does not create duplicate active insights or duplicate tasks.
   - Prefer one or both of:
     - a persisted processed-trigger/idempotency record keyed by tenant + trigger type + source entity id + source version + check
     - enforcing uniqueness at the insight/task creation boundary using natural/business keys
   - The implementation must be safe under retries and repeated event delivery.
   - If there is already an outbox/inbox deduplication mechanism, reuse it and add domain-level protection where necessary.

7. **Handle outcomes cleanly**
   - Record success/failure/partial outcome in the trigger execution log.
   - If one check fails and others succeed, preserve traceability of what executed.
   - Follow existing retry/error-handling conventions.
   - Avoid swallowing exceptions silently; either:
     - mark outcome appropriately and rethrow if infrastructure expects retries, or
     - handle as a business failure if that matches current worker semantics.

8. **Wire supported domain events**
   - Subscribe the orchestration to the exact supported event families:
     - invoice
     - bill
     - payment
     - cash
     - simulation-day-advanced
   - If multiple concrete event types exist per family, wire all relevant ones.
   - Keep handlers thin; orchestration logic should live in a reusable application service.

9. **Add integration test coverage**
   - Add at least one end-to-end integration test that:
     - arranges tenant/company data
     - emits a supported domain event
     - runs the relevant processing path
     - verifies a financial check result was persisted
     - verifies a trigger execution log was persisted with required fields
   - Add replay/idempotency coverage if feasible:
     - emit/process the same event twice for the same entity version
     - assert no duplicate active insights/tasks
   - Use existing test infrastructure and patterns in `tests/VirtualCompany.Api.Tests`.

10. **Keep code quality high**
   - Follow existing naming, DI, MediatR/eventing, repository, and EF conventions.
   - Keep methods small and composable.
   - Add comments only where behavior is non-obvious.
   - Do not introduce speculative abstractions beyond what this task needs.

# Validation steps
Run these after implementation:

1. **Build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Targeted verification**
   - Confirm supported trigger events are wired and compile cleanly.
   - Confirm persistence schema updates are included and tests initialize correctly.
   - Confirm trigger execution logs persist required fields:
     - `tenantId`
     - `triggerType`
     - `sourceEntityId`
     - `executedChecks`
     - `startedAt`
     - `completedAt`
     - `outcome`
   - Confirm replaying the same trigger for the same entity version does not create:
     - duplicate active insights
     - duplicate tasks

4. **Integration test expectations**
   - At least one integration test must explicitly prove:
     - emitted domain event
     - orchestration dispatch
     - persisted check result
     - persisted trigger execution log

5. **Code review checklist**
   - Tenant/company scoping enforced on every query/write
   - No direct DB access from handlers if typed domain/application abstractions already exist
   - No duplicate orchestration path introduced
   - Idempotency is enforced at a durable boundary, not only in-memory
   - Logging is business-traceable and not only technical

# Risks and follow-ups
- **Risk: unclear existing event model**
  - The codebase may use MediatR notifications, domain event collections, outbox messages, or custom background dispatch. Reuse the existing mechanism instead of guessing.

- **Risk: entity version may not exist uniformly**
  - If invoice/bill/payment/cash entities do not all expose a version field, define a consistent replay identity using existing event metadata and document the choice in code/tests.

- **Risk: duplicate prevention may need multiple layers**
  - Event deduplication alone may not prevent duplicate insights/tasks if downstream creation is not idempotent. Add durable uniqueness or upsert-style protection where side effects are created.

- **Risk: migration friction**
  - If adding new tables/indexes, ensure migration generation matches repository conventions and does not break test setup.

- **Risk: partial failure semantics**
  - If multiple checks run per trigger, decide and encode whether one failure should fail the whole trigger or produce partial outcome. Keep behavior explicit and testable.

Suggested follow-ups after this task, only if naturally adjacent and not required now:

- Add richer trigger execution detail records per check, not just aggregate log rows.
- Add query endpoints or admin views for trigger execution history.
- Add more replay/idempotency integration tests across multiple trigger families.
- Add correlation IDs linking trigger logs, check results, insights, tasks, and audit events.