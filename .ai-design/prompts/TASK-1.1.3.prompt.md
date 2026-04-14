# Goal
Implement backlog task **TASK-1.1.3 — Persist task provenance and audit metadata for proactive actions** for story **US-1.1 ST-A201 — Agent-initiated task creation** in the existing .NET modular monolith.

The implementation must ensure that when the system creates a task proactively from an agent trigger or analysis result, it persists full provenance and audit metadata, enforces required fields, prevents duplicates within a deduplication window, and records a business audit event.

Deliver a production-ready vertical slice aligned with the architecture:
- ASP.NET Core modular monolith
- PostgreSQL transactional persistence
- tenant-scoped data access
- CQRS-lite application layer
- auditability as a domain feature

# Scope
Implement only what is required to satisfy this task and its acceptance criteria.

Functional requirements:
1. **Persist provenance for agent-initiated task creation**
   - When a task is created without user prompting, persist:
     - originating agent
     - trigger source
     - creation reason
   - Ensure the task is clearly marked as agent-created / proactive.

2. **Persist required task fields**
   - Created tasks must include:
     - `title`
     - `description`
     - `priority`
     - `status`
     - `assignee` or `queue`
     - `sourceType = 'agent'`
     - `correlationId`

3. **Prevent duplicate task creation**
   - Prevent duplicate creation for the same:
     - trigger event
     - correlationId
   - Respect a configured deduplication window.
   - Prefer deterministic idempotency at the application/service layer, backed by persistence constraints where practical.

4. **Write audit event for each agent-initiated task creation**
   - Persist an audit record containing at minimum:
     - tenant/company id
     - agent id
     - timestamp
     - payload diff
   - Keep this in business audit storage, not only technical logs.

Non-goals unless already required by existing code patterns:
- No UI work unless necessary for compilation.
- No broad workflow engine redesign.
- No unrelated refactors.
- No speculative support for all future trigger types beyond what is needed for a clean extensible model.

# Files to touch
Inspect the solution first and then update the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - task aggregate/entity/value objects
  - audit event entity/value objects
  - enums/constants for source type / actor type / task status
- `src/VirtualCompany.Application/**`
  - command/handler or service for proactive task creation
  - deduplication policy/config abstraction
  - audit event creation logic
  - validation rules
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations
  - persistence for audit events
  - configuration binding for deduplication window
- `src/VirtualCompany.Api/**`
  - DI registration / options wiring if needed
  - endpoint/controller only if this flow is API-triggered in current design
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for end-to-end persistence and deduplication
- `tests/**` or other existing test projects
  - unit tests for domain/application behavior

Also inspect:
- existing migrations strategy
- current task schema/entity
- current audit event schema/entity
- any existing correlation/idempotency patterns
- tenant scoping conventions (`company_id`, `tenantId`, etc.)

# Implementation plan
1. **Discover existing implementation patterns**
   - Inspect how tasks are currently modeled and created.
   - Inspect whether there is already:
     - a task creation command/handler
     - audit event persistence
     - correlation ID support
     - actor/source metadata on tasks
     - queue assignment support
   - Reuse existing conventions over inventing new ones.

2. **Extend the task model for provenance**
   - Add the minimum fields needed to satisfy acceptance criteria if they do not already exist.
   - Prefer explicit columns for queryable provenance rather than burying everything in JSON.
   - Target fields should cover:
     - `sourceType` with value `agent`
     - `correlationId`
     - originating `agentId`
     - `triggerSource`
     - `creationReason`
     - trigger event reference/id if needed for deduplication
     - assignee or queue representation
   - If the current schema uses `company_id`, keep naming consistent with the codebase and map acceptance language accordingly.

3. **Model deduplication**
   - Introduce a deduplication strategy for proactive task creation:
     - input key should include tenant/company + trigger event identity + correlationId
     - compare against tasks created within configured window
   - Prefer one of:
     - dedicated provenance/idempotency columns on `tasks`
     - or a dedicated table for proactive task creation receipts if that better matches the codebase
   - Add configuration option for deduplication window via options/config.
   - If feasible, add a supporting index/constraint to reduce race-condition risk.
   - Ensure behavior is safe under retries and background worker reprocessing.

4. **Implement application command/service**
   - Create or extend a command such as proactive/agent-initiated task creation.
   - Validate required fields:
     - title
     - description
     - priority
     - status
     - assignee or queue
     - sourceType fixed to `agent`
     - correlationId required
   - Persist provenance metadata.
   - Run deduplication check before insert.
   - Return existing task or a deterministic duplicate result if duplicate is detected, based on current application conventions.

5. **Persist audit metadata**
   - For every successful agent-initiated task creation, create a business audit event.
   - Include:
     - company/tenant id
     - actor type = agent
     - actor id = originating agent id
     - timestamp
     - action indicating proactive task creation
     - target type/id for the task
     - payload diff
   - If the audit model already has `payload_json`, `metadata_json`, `changes_json`, or similar, use it.
   - If “payload diff” is not yet modeled, add the smallest structured field that can represent created values, e.g. before/after or created snapshot diff.
   - Keep rationale concise and operational; do not store chain-of-thought.

6. **Database migration**
   - Add EF migration for any schema changes.
   - Include:
     - new columns
     - indexes for deduplication lookup
     - audit schema updates if needed
   - Keep migration names descriptive and aligned with repository conventions.

7. **Testing**
   - Add unit tests for:
     - required field validation
     - deduplication logic
     - source type forced to `agent`
   - Add integration tests for:
     - proactive task creation persists provenance fields
     - duplicate creation is prevented within window
     - audit event is written with required metadata
     - tenant scoping is respected
   - Cover at least one retry/idempotency scenario.

8. **Keep implementation aligned with architecture**
   - Tenant isolation must be enforced in all queries and writes.
   - Use application layer commands/services, not controller-heavy logic.
   - Persist business audit events separately from technical logs.
   - Keep side effects ready for outbox patterns if existing code already uses them.

# Validation steps
1. Inspect and restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. Verify migration output is present and coherent:
   - confirm new migration files exist
   - confirm schema changes match implementation
   - confirm indexes/constraints support deduplication lookup

5. Validate acceptance criteria explicitly through tests or reproducible checks:
   - **Provenance persisted**
     - create a proactive task from a configured trigger/analysis result
     - verify persisted task contains originating agent, trigger source, creation reason
   - **Required fields present**
     - verify title, description, priority, status, assignee or queue, `sourceType='agent'`, correlationId
   - **Deduplication enforced**
     - submit same trigger event + correlationId twice within window
     - verify only one task is created
   - **Audit event written**
     - verify audit record contains company/tenant id, agent id, timestamp, payload diff

6. If there are API/integration tests, ensure they assert persisted database state rather than only HTTP status codes.

# Risks and follow-ups
- **Schema mismatch risk:** The architecture excerpt is partially conceptual and may not match the current codebase exactly. Adapt to actual entities and naming conventions in the repo.
- **Deduplication race conditions:** A pure read-then-write check may allow duplicates under concurrency. Mitigate with indexes, transactional boundaries, or a dedicated idempotency record if needed.
- **Audit model gaps:** If the current audit schema does not support payload diffs, add the smallest extensible structure rather than overdesigning a full diff engine.
- **Assignment model ambiguity:** The acceptance criteria says “assignee or queue.” If queue support does not exist, implement whichever is already supported and add the minimal queue field only if necessary.
- **Terminology mismatch:** The task says `tenantId` while the architecture uses `company_id`. Use the repository’s canonical tenant identifier and map consistently.
- **Background worker retries:** Ensure duplicate prevention works for retried trigger processing, not just repeated API calls.

Follow-ups after this task, only if needed and not part of this implementation:
- expose provenance in task query/read models
- add audit/explainability UI surfaces
- standardize idempotency utilities across workflow/background processing
- add outbox fan-out for downstream notifications or analytics