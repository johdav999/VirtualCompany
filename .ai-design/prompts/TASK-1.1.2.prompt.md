# Goal
Implement `TASK-1.1.2` for **US-1.1 ST-A201 — Agent-initiated task creation** by adding **idempotency and deduplication protections** for agent-created tasks in the .NET modular monolith.

The implementation must ensure that when an agent creates a task from a configured trigger or analysis result:

- the task is persisted with full origin metadata
- required fields are enforced
- duplicate creation is prevented for the same `trigger event + correlationId` within a configurable deduplication window
- an audit event is recorded for every successful agent-initiated task creation with the required audit fields

Use the existing architecture conventions:
- ASP.NET Core modular monolith
- PostgreSQL transactional store
- CQRS-lite application layer
- tenant-scoped data access
- auditability as a business feature, not just technical logging

# Scope
In scope:

- Add/extend domain and persistence support for agent-originated task metadata:
  - originating agent
  - trigger source
  - creation reason
  - `sourceType = 'agent'`
  - `correlationId`
- Add idempotency/deduplication logic for agent-created tasks based on:
  - `tenant/company`
  - `trigger event identity`
  - `correlationId`
  - configured deduplication window
- Ensure required task fields are validated for agent-created tasks:
  - `title`
  - `description`
  - `priority`
  - `status`
  - `assignee or queue`
  - `sourceType='agent'`
  - `correlationId`
- Persist audit log entries for successful agent-created task creation including:
  - `tenantId`
  - `agentId`
  - `timestamp`
  - `payload diff`
- Add tests covering:
  - successful creation
  - duplicate suppression within window
  - allowed recreation outside window if intended by design
  - tenant isolation
  - audit creation

Out of scope unless required to complete the task cleanly:

- UI changes in Blazor or MAUI
- broad workflow engine redesign
- message broker changes
- unrelated task lifecycle features
- full approval flow changes
- external integration changes

Assumptions to follow unless the codebase dictates otherwise:

- `company_id` is the tenant key in persistence, even if some code uses `tenantId` at the application boundary
- deduplication should be enforced server-side, ideally with both:
  - application-level guard
  - database-level uniqueness or conflict-safe insert strategy where feasible
- if no dedicated queue model exists yet, use the existing assignment model and validate that either an assigned agent or queue reference is present according to current domain conventions
- audit events should go into the business audit store/module, not only structured logs

# Files to touch
Inspect the solution first and then update the actual files that match these responsibilities. Expected areas:

- `src/VirtualCompany.Domain/**`
  - task aggregate/entity/value objects
  - audit event entity/value objects
  - any enums/constants for source type / actor type / status
- `src/VirtualCompany.Application/**`
  - command/handler for agent-initiated task creation
  - validation layer
  - deduplication/idempotency service or policy
  - audit event creation orchestration
  - configuration options for deduplication window
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository implementation
  - migrations support
  - PostgreSQL index/constraint strategy
  - audit persistence
- `src/VirtualCompany.Api/**`
  - DI registration / options binding if needed
  - endpoint wiring only if this flow is API-triggered
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- possibly:
  - `tests/**Application.Tests**` or `tests/**Infrastructure.Tests**` if such projects already exist
  - `README.md` or docs only if configuration needs documenting

Likely concrete artifacts to add or modify:

- task entity schema/config to include:
  - `source_type`
  - `correlation_id`
  - `trigger_source`
  - `creation_reason`
  - `trigger_event_key` or equivalent dedupe fingerprint
  - `created_by_actor_type = 'agent'`
  - `created_by_actor_id = agentId`
- audit event schema/config if payload diff fields are not already modeled
- a migration creating:
  - new columns
  - indexes for tenant-scoped dedupe lookup
  - possibly a partial unique index if the dedupe design supports it

Do not invent file names blindly in the final implementation; first align with the repository’s existing naming and module structure.

# Implementation plan
1. **Discover the current task creation flow**
   - Find the existing task domain model, create command/handler, repositories, and EF mappings.
   - Identify whether agent-created tasks already exist partially under ST-401/ST-502 patterns.
   - Find the audit module implementation and how business audit events are persisted.
   - Confirm whether the codebase uses `companyId` or `tenantId` naming in entities and commands.

2. **Model agent-originated task metadata**
   - Extend the task entity/model so agent-created tasks persist:
     - tenant/company id
     - assigned agent or queue
     - `created_by_actor_type = agent`
     - `created_by_actor_id = agentId`
     - `sourceType = agent`
     - `correlationId`
     - `triggerSource`
     - `creationReason`
     - `triggerEventKey` or equivalent normalized dedupe key
   - Prefer explicit columns for queryability over burying these in JSON payloads.
   - Keep naming consistent with existing schema conventions.

3. **Add validation for required fields**
   - In the application command validator or domain factory, enforce:
     - non-empty title
     - non-empty description
     - valid priority
     - valid status
     - assignee or queue present
     - `sourceType == agent`
     - non-empty `correlationId`
     - non-empty `agentId`
     - non-empty trigger source / reason if required by the acceptance criteria
   - Reuse existing enums/value objects where available.
   - Return deterministic validation errors.

4. **Design the deduplication strategy**
   - Implement dedupe based on:
     - tenant/company id
     - agent id if appropriate
     - trigger event identity
     - correlation id
     - created timestamp within configured window
   - Preferred approach:
     - compute a stable dedupe fingerprint such as `companyId + triggerEventKey + correlationId`
     - check for an existing agent-created task within the configured window
   - Add configuration:
     - e.g. `AgentTaskCreationOptions.DeduplicationWindow`
   - If the architecture supports retries/concurrency, make the operation safe under race conditions:
     - either use a DB unique constraint on a dedupe bucket/key
     - or use transaction + conflict handling
     - or use PostgreSQL upsert semantics where appropriate
   - If a pure time-window unique constraint is not practical, use:
     - application-level lookup for “existing within window”
     - plus a stable idempotency record table or lockable dedupe record if needed
   - Favor correctness under concurrent worker retries over a simplistic pre-check only.

5. **Implement idempotent create flow**
   - In the agent task creation handler/service:
     - resolve tenant/company scope
     - validate input
     - derive dedupe key / trigger event key
     - check for existing matching task within dedupe window
     - if duplicate exists:
       - do not create a new task
       - return the existing task or a duplicate-suppressed result according to current application conventions
     - if no duplicate exists:
       - create the task
       - persist origin metadata
       - persist audit event
   - Ensure the flow is transactional so task creation and audit creation succeed/fail together if that matches current unit-of-work patterns.

6. **Persist audit event**
   - Record a business audit event for each successful agent-initiated task creation.
   - Include:
     - tenant/company id
     - actor type = `agent`
     - actor id = `agentId`
     - action such as `task.created`
     - target type = `task`
     - target id = created task id
     - timestamp
     - payload diff
     - outcome
   - If the audit model already supports rationale/data sources, preserve compatibility.
   - For payload diff:
     - use the existing diff format if present
     - otherwise store a concise structured diff showing created fields/values, not raw chain-of-thought

7. **Add persistence support**
   - Update EF Core configurations and repositories.
   - Add PostgreSQL migration for new columns/indexes.
   - Recommended indexes:
     - `(company_id, source_type, correlation_id)`
     - `(company_id, trigger_event_key, correlation_id, created_at desc)`
     - any audit lookup indexes already used by the audit module
   - If adding a dedicated dedupe/idempotency table is cleaner, model it explicitly and index by:
     - `company_id`
     - `dedupe_key`
     - `expires_at`
   - Keep all queries tenant-scoped.

8. **Handle concurrency explicitly**
   - Ensure duplicate prevention works when:
     - background workers retry
     - the same trigger is processed twice in parallel
   - If using a dedupe table:
     - insert dedupe record first in the same transaction
     - rely on unique constraint conflict to suppress duplicates
   - If using task-table uniqueness:
     - catch unique violation and resolve by loading the existing task
   - Do not rely only on in-memory locking.

9. **Add tests**
   - Integration tests should cover:
     - creates a task with required fields and origin metadata
     - records audit event on successful create
     - suppresses duplicate creation for same trigger event + correlationId within dedupe window
     - allows distinct trigger event or distinct correlationId to create a new task
     - enforces tenant isolation so one tenant’s task does not block another’s
     - handles concurrent duplicate attempts safely if test infrastructure allows
   - Add validator/unit tests for required field enforcement.
   - Prefer integration tests against the real persistence path if the repo already supports that.

10. **Keep implementation aligned with architecture**
   - Use application services/commands, not controller-heavy logic.
   - Keep domain rules in domain/application layers.
   - Keep infrastructure concerns in EF/repositories/migrations.
   - Ensure audit is business-level persistence, separate from technical logs.
   - Preserve correlation IDs across task and audit records.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests to verify:
   - agent-created task persists:
     - title
     - description
     - priority
     - status
     - assignee/queue
     - `sourceType='agent'`
     - `correlationId`
     - originating agent
     - trigger source
     - creation reason
   - duplicate request with same trigger event + correlationId inside dedupe window does not create a second task
   - duplicate suppression returns deterministic behavior
   - audit event exists with:
     - tenant/company id
     - agent id
     - timestamp
     - payload diff
   - cross-tenant duplicate keys do not collide

4. Verify migration correctness:
   - ensure schema updates apply cleanly
   - ensure indexes/constraints are created as expected
   - ensure nullable/non-nullable choices match acceptance criteria

5. If there is an API endpoint for this flow, verify via integration test or manual request:
   - first request creates task
   - second identical request within window is deduped
   - response semantics are stable and documented in code/tests

6. Review logs and audit persistence:
   - confirm business audit event is written once per successful creation
   - confirm duplicate-suppressed attempts do not create misleading duplicate audit records unless the existing audit model explicitly requires suppression events

# Risks and follow-ups
- **Schema mismatch risk:** The current `tasks` schema in the architecture excerpt does not yet show all required fields (`sourceType`, `correlationId`, trigger metadata). Confirm the actual codebase before choosing column names.
- **Queue modeling ambiguity:** Acceptance criteria mention “assignee or queue,” but the current architecture excerpt emphasizes `assigned_agent_id`. If queue support is not yet modeled, align with existing domain patterns and avoid inventing a half-baked queue subsystem.
- **Time-window uniqueness complexity:** A pure DB unique constraint cannot directly express “within configurable deduplication window.” If needed, introduce a dedicated idempotency/dedupe record with expiry semantics rather than overloading the task table.
- **Concurrency risk:** A pre-insert existence check alone is insufficient under parallel processing. Use transactional conflict handling or a unique dedupe record.
- **Audit diff format ambiguity:** Reuse any existing audit payload/diff conventions. Do not invent a new incompatible audit shape if one already exists.
- **Naming drift:** The backlog uses `tenantId`, while the architecture uses `company_id`. Keep external contracts and internal persistence naming consistent with the repository.
- **Follow-up recommendation:** If this pattern will be reused for workflows, approvals, notifications, and outbox-driven retries, extract a shared idempotency service/policy after this task lands.
- **Follow-up recommendation:** Consider documenting the dedupe key contract for trigger producers so trigger event identity remains stable across retries and worker restarts.