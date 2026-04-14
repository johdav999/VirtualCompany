# Goal
Implement backlog task **TASK-1.1.1 — proactive task creation service with trigger-to-task mapping** for story **US-1.1 ST-A201 — Agent-initiated task creation** in the existing **.NET modular monolith**.

Build a production-ready application/domain/infrastructure slice that allows the system to create tasks **without user prompting** when a configured trigger or analysis result is received, while enforcing:
- tenant scoping
- required task fields
- trigger-to-task mapping
- deduplication within a configurable window
- audit logging for every agent-initiated task creation

The implementation must fit the current architecture and codebase conventions, prefer clean boundaries, and avoid speculative overengineering.

# Scope
Implement only what is necessary to satisfy the acceptance criteria for this task.

Include:
1. **Domain support** for agent-initiated task creation metadata:
   - originating agent
   - trigger source
   - creation reason
   - sourceType = `agent`
   - correlationId
2. **Trigger-to-task mapping service** that converts a configured trigger/analysis result into a task creation request.
3. **Proactive task creation application service/command handler** that:
   - validates required fields
   - persists the task
   - prevents duplicates for the same trigger event + correlationId within a configured deduplication window
4. **Audit event creation** for each successful agent-initiated task creation with:
   - tenantId/companyId
   - agentId
   - timestamp
   - payload diff
5. **Persistence changes** required to support the feature.
6. **Automated tests** covering happy path, validation, deduplication, and audit behavior.

Out of scope unless already trivially supported by existing code:
- UI screens
- mobile changes
- external integrations
- workflow engine expansion beyond what is needed for task creation
- broad refactors unrelated to this task
- full event bus/message broker work

If there is no existing trigger ingestion model, create the smallest internal contract needed for this feature.

# Files to touch
Inspect the solution first and then update the most appropriate files. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - task entity/value objects/specifications
  - audit event entity/contracts
  - any new enums/constants for source type / actor type / trigger source
- `src/VirtualCompany.Application/`
  - command + handler/service for proactive task creation
  - DTOs/contracts for trigger input and mapped task request
  - deduplication policy abstraction
  - validation
- `src/VirtualCompany.Infrastructure/`
  - EF Core persistence mappings/configurations
  - repositories
  - deduplication query implementation
  - audit persistence
  - configuration binding for deduplication window
- `src/VirtualCompany.Api/`
  - only if an internal/admin/test endpoint is needed to exercise the feature
  - DI registration/configuration
- `tests/VirtualCompany.Api.Tests/`
  - integration/API tests if API surface exists
- possibly:
  - `tests/...Application.Tests/` or equivalent if present
  - migration files or SQL scripts if this repo uses them
  - `README.md` or docs only if configuration/setup needs a brief note

Before coding, discover the actual project structure and use existing patterns rather than inventing new ones.

# Implementation plan
1. **Discover existing task and audit implementation**
   - Inspect current domain/application/infrastructure for:
     - task entity/model
     - audit event model
     - actor/source enums
     - correlation ID handling
     - repository and command patterns
     - EF migrations approach
   - Reuse existing abstractions where possible.
   - Identify whether the architecture uses MediatR, custom command handlers, repositories, unit of work, or direct DbContext patterns.

2. **Design the minimal domain changes**
   - Ensure tasks can persist all required fields from acceptance criteria:
     - `title`
     - `description`
     - `priority`
     - `status`
     - `assignee or queue`
     - `sourceType = 'agent'`
     - `correlationId`
   - Add metadata for:
     - originating agent ID
     - trigger source
     - creation reason
     - trigger event identifier if needed for deduplication
   - If the existing `tasks` table already has partial support (e.g. `created_by_actor_type`, `created_by_actor_id`), extend rather than duplicate.
   - Prefer explicit columns for queryable fields over burying everything in JSON.

3. **Add trigger input and mapping contracts**
   - Create a small internal contract such as:
     - `ProactiveTaskTrigger`
     - `MappedTaskCreationRequest`
   - The trigger contract should include enough data to support:
     - tenant/company ID
     - agent ID
     - trigger source/type
     - trigger event ID/reference
     - correlation ID
     - reason/analysis summary
     - optional payload
   - Implement a mapping service that transforms trigger input into a normalized task creation request.
   - Keep mapping deterministic and testable.

4. **Implement proactive task creation service**
   - Add an application service/command handler, e.g. `CreateAgentInitiatedTaskCommandHandler`.
   - Responsibilities:
     - validate tenant/company and agent IDs
     - validate required task fields
     - enforce `sourceType = agent`
     - set creator metadata (`created_by_actor_type = agent`, `created_by_actor_id = agentId`) if aligned with existing schema
     - persist trigger source and creation reason
     - persist correlation ID
   - If assignment supports either agent or queue and queue does not yet exist in the model:
     - use the existing assignment model if present
     - otherwise add the smallest viable queue/assignee representation needed to satisfy the acceptance criteria without broad workflow redesign

5. **Implement deduplication**
   - Add configurable deduplication window in app settings/options.
   - Prevent duplicate task creation for the same:
     - tenant/company
     - trigger event
     - correlationId
     - within the configured window
   - Prefer a robust implementation:
     - application-level existence check plus
     - database support if practical in current schema
   - If a unique index cannot express the time window cleanly, implement a repository query against creation timestamp and ensure tests cover race-adjacent behavior as much as feasible.
   - Return a safe idempotent result when a duplicate is detected:
     - either return the existing task or a structured duplicate outcome
   - Do not create duplicate audit events for blocked duplicates unless existing conventions require a separate audit outcome event.

6. **Implement audit logging**
   - For every successful agent-initiated task creation, create a business audit event.
   - Include:
     - tenant/company ID
     - actor type = `agent`
     - actor ID = agentId
     - timestamp
     - action indicating agent-initiated task creation
     - target type/id for the task
     - payload diff
   - If the audit model already supports rationale summary and structured payload/data sources, populate them appropriately.
   - For payload diff:
     - capture a concise structured diff between empty/origin state and created task payload, or use the project’s existing diff format if one exists.
   - Keep audit logging in business tables, not only technical logs.

7. **Persistence and migrations**
   - Update EF entities/configurations and create the required migration/script.
   - Add indexes for common lookups:
     - company/tenant + correlationId
     - company/tenant + trigger event reference
     - created_at if needed for deduplication window queries
   - Ensure all new tenant-owned records are company-scoped.

8. **Dependency injection and configuration**
   - Register the mapping service, proactive task creation service, and deduplication options.
   - Add configuration defaults for deduplication window with sensible conservative value.
   - Keep names aligned with existing configuration conventions.

9. **Testing**
   - Add unit tests for:
     - trigger-to-task mapping
     - required field validation
     - deduplication logic
   - Add integration tests for:
     - successful proactive task creation persists required fields and metadata
     - duplicate trigger event + correlationId inside window does not create a second task
     - audit event is written with required actor/tenant/timestamp/payload diff
   - Prefer integration tests against the real persistence layer pattern used by the repo.

10. **Implementation constraints**
   - Follow existing naming, folder structure, and architectural patterns.
   - Keep methods cohesive and avoid introducing unrelated abstractions.
   - Do not expose raw chain-of-thought or LLM internals.
   - Preserve tenant isolation in every query and write path.
   - If you encounter missing foundational pieces, implement the smallest compatible extension and document it in the final notes.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. Verify the implemented behavior through automated tests:
   - a configured trigger/analysis result creates a task without user prompting
   - persisted task includes:
     - title
     - description
     - priority
     - status
     - assignee or queue
     - sourceType = `agent`
     - correlationId
     - originating agent
     - trigger source
     - creation reason
   - duplicate creation is prevented for same trigger event + correlationId within dedup window
   - successful creation writes an audit event with:
     - tenant/company ID
     - agent ID
     - timestamp
     - payload diff
4. If an endpoint or executable path exists for manual verification, exercise it and confirm:
   - first request creates task
   - second identical request within window is deduplicated
5. Include in your final implementation summary:
   - files changed
   - schema changes/migration added
   - configuration keys added
   - any assumptions made due to current codebase gaps

# Risks and follow-ups
- **Schema mismatch risk:** the current `tasks` or `audit_events` schema may differ from the architecture doc. Adapt to the real codebase and note deviations.
- **Assignment model ambiguity:** “assignee or queue” may not yet exist. Prefer existing assignment semantics and implement the smallest extension needed.
- **Dedup race conditions:** application-level dedup checks may still allow rare concurrent duplicates. If the current stack supports it, strengthen with transactional safeguards or a supporting unique/idempotency record.
- **Audit diff format ambiguity:** if no payload diff standard exists, implement a simple structured diff and keep it isolated for future standardization.
- **Migration strategy uncertainty:** the repo may use archived/manual PostgreSQL migrations. Follow the repository’s actual migration workflow rather than assuming EF CLI-only migrations.
- **Follow-up candidates, not part of this task unless necessary:**
  - dedicated trigger event inbox table
  - stronger idempotency key store
  - queue entity/model
  - richer audit explainability views
  - outbox/event emission for downstream workflow progression