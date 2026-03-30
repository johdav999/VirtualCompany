# Goal
Implement backlog task **TASK-ST-401 — Task lifecycle and assignment** for story **ST-401 Task lifecycle and assignment** in the existing .NET solution.

Deliver a vertical slice for tenant-scoped task management that allows users to create, assign, update, and query tasks for agents, with support for:
- task creation with required business fields
- assignment to valid agents only
- lifecycle status management
- parent/child subtask linkage
- storage of orchestration-related task detail fields

This work should align with the architecture and backlog notes:
- modular monolith
- CQRS-lite application layer
- PostgreSQL-backed transactional model
- tenant isolation via `company_id`
- task as a core orchestration backbone entity
- reject assignment to paused or archived agents

Because no explicit acceptance criteria were provided on the task record, implement against the story acceptance criteria for **ST-401**.

# Scope
In scope:
- Add or complete the **Task** domain model and related enums/value constraints
- Add persistence mapping for `tasks`
- Add application commands/queries for:
  - create task
  - update task status
  - reassign task
  - get task by id
  - list tasks with basic filtering
  - create subtask linked to parent task
- Enforce tenant scoping on all task operations
- Enforce assignment validation:
  - assigned agent must belong to same company
  - assigned agent must not be paused or archived
- Support task fields:
  - `type`
  - `title`
  - `description`
  - `priority`
  - `status`
  - `due_at`
  - `assigned_agent_id`
  - `parent_task_id`
  - `input_payload`
  - `output_payload`
  - `rationale_summary`
  - `confidence_score`
- Expose API endpoints for the above operations
- Add basic validation and tests

Out of scope unless already scaffolded and trivial to wire:
- full workflow engine integration
- approval engine integration beyond allowing `awaiting_approval` status
- background workers
- UI-heavy Blazor task management screens
- audit/event fan-out beyond minimal hooks if patterns already exist
- notifications/outbox side effects unless already required by existing architecture patterns

If the repository already contains partial task functionality, extend/refactor it instead of duplicating it.

# Files to touch
Inspect the solution first and then touch the minimum necessary files in the relevant layers. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - task aggregate/entity
  - task status enum
  - task priority enum
  - domain validation/guard logic
- `src/VirtualCompany.Application/**`
  - commands
  - command handlers
  - queries
  - query handlers
  - DTOs/view models
  - validators
  - interfaces/repositories
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext
  - entity configurations
  - migrations
  - repository implementations
- `src/VirtualCompany.Api/**`
  - task endpoints/controllers
  - request/response contracts if API-specific
  - authorization/tenant resolution wiring
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared DTOs/enums across projects
- `src/VirtualCompany.Web/**`
  - only if there is already a task page or API client that must compile after contract changes
- `README.md`
  - only if there is a concise API or feature note worth documenting

Also inspect for existing patterns in:
- tenant-scoped repositories/services
- MediatR or equivalent CQRS setup
- FluentValidation or equivalent validation
- EF Core configuration conventions
- minimal APIs vs controllers
- existing agent entity/status definitions from ST-201/ST-202 work

# Implementation plan
1. **Inspect current architecture and reuse existing patterns**
   - Review the solution structure and identify:
     - how tenant context is resolved
     - how commands/queries are organized
     - how entities are mapped in EF Core
     - whether agents already exist and how agent status is represented
   - Do not invent a new pattern if the solution already has one.

2. **Model the task domain**
   - Add or complete a `Task` domain entity in the Domain project.
   - Avoid naming conflicts with `System.Threading.Tasks.Task`; use a clear name such as `WorkTask` if needed by the codebase.
   - Include fields consistent with the architecture:
     - `Id`
     - `CompanyId`
     - `AssignedAgentId`
     - `CreatedByActorType`
     - `CreatedByActorId`
     - `Type`
     - `Title`
     - `Description`
     - `Priority`
     - `Status`
     - `DueAt`
     - `InputPayload`
     - `OutputPayload`
     - `RationaleSummary`
     - `ConfidenceScore`
     - `ParentTaskId`
     - `WorkflowInstanceId`
     - `CreatedAt`
     - `UpdatedAt`
     - `CompletedAt`
   - Add status support for:
     - `new`
     - `in_progress`
     - `blocked`
     - `awaiting_approval`
     - `completed`
     - `failed`
   - Add priority support if not already present, using a simple constrained set.
   - Add domain methods/guards for:
     - assignment
     - status transition updates
     - completion timestamp behavior
     - parent task linkage
   - Keep rules pragmatic; do not over-engineer a full state machine unless the codebase already uses one.

3. **Enforce assignment rules**
   - When assigning an agent, validate:
     - agent exists
     - agent belongs to the same `company_id`
     - agent status is not `paused`
     - agent status is not `archived`
   - If story ST-202 introduced `restricted`, allow assignment unless the existing domain rules say otherwise. The story note only explicitly requires rejection for paused/archived.
   - Return clear validation/business errors.

4. **Add persistence mapping**
   - Add EF Core configuration for the task table.
   - Map to the `tasks` schema shape described in the architecture.
   - Ensure:
     - required columns are required
     - JSON payload fields map appropriately for PostgreSQL/JSONB if the project already uses JSONB mapping
     - string lengths and indexes are reasonable
     - foreign keys are configured for:
       - assigned agent
       - parent task
       - workflow instance if present in model
   - Add indexes likely needed for tenant-scoped queries:
     - `(company_id, status)`
     - `(company_id, assigned_agent_id)`
     - `(company_id, due_at)`
     - `(company_id, parent_task_id)` if subtasks are queried
   - Create a migration.

5. **Implement application commands**
   - Add command + handler for **CreateTask**:
     - input: type, title, description, priority, due date, assigned agent, input payload, parent task optional
     - set initial status to `new` unless existing conventions dictate otherwise
     - validate parent task belongs to same company if provided
     - validate assigned agent rules
   - Add command + handler for **CreateSubtask**:
     - same as create task, but requires valid parent task in same company
   - Add command + handler for **UpdateTaskStatus**:
     - allow setting one of the supported statuses
     - set `CompletedAt` when status becomes `completed`
     - clear or preserve `CompletedAt` consistently if moved away from completed; prefer preserving only when completed and null otherwise unless existing conventions differ
     - allow optional updates to `output_payload`, `rationale_summary`, `confidence_score`
   - Add command + handler for **ReassignTask**:
     - validate target agent
     - update `AssignedAgentId`
   - If the codebase prefers a single update command, keep it cohesive but do not create a bloated god-command.

6. **Implement application queries**
   - Add query + handler for **GetTaskById**:
     - tenant scoped
     - return all relevant task detail fields
     - include parent task summary and assigned agent summary if easy within existing patterns
   - Add query + handler for **ListTasks**:
     - tenant scoped
     - support basic filters:
       - status
       - assigned agent
       - parent task
       - due before/after
     - support simple paging if the project already has a paging abstraction
   - Keep query DTOs read-optimized and separate from domain entities.

7. **Add validation**
   - Add request/command validation for:
     - required title
     - required type
     - valid priority
     - valid status
     - due date sanity if applicable
     - confidence score range if provided
   - Validate payload sizes only if there is an existing convention/helper.
   - Ensure field-level validation errors are returned in the project’s standard format.

8. **Expose API endpoints**
   - Add tenant-scoped endpoints in the API project for:
     - `POST /api/tasks`
     - `POST /api/tasks/{id}/subtasks`
     - `GET /api/tasks/{id}`
     - `GET /api/tasks`
     - `PUT` or `PATCH /api/tasks/{id}/status`
     - `PUT` or `PATCH /api/tasks/{id}/assignment`
   - Reuse existing authorization and company context resolution.
   - Do not allow cross-tenant access.
   - Return appropriate HTTP responses:
     - `201` for create
     - `200` for reads/updates
     - `400` for validation errors
     - `403`/`404` according to existing tenant access conventions

9. **Preserve CQRS-lite boundaries**
   - Keep writes in commands and reads in queries.
   - Do not let controllers/endpoints talk directly to EF Core if the application layer pattern already exists.
   - Keep domain logic out of API contracts.

10. **Add tests**
   - Add unit and/or integration tests based on existing test strategy.
   - Cover at minimum:
     - create task succeeds with valid data
     - create task rejects paused agent
     - create task rejects archived agent
     - create subtask links to parent task
     - get task is tenant scoped
     - list tasks filters by status/agent
     - update status to completed sets completion timestamp
     - reassign task to valid agent succeeds
     - cross-company parent task or agent assignment is rejected
   - If API integration tests exist, add at least one happy-path and one tenant-isolation case.

11. **Keep implementation pragmatic**
   - Since no explicit task-level acceptance criteria were provided, do not expand into workflow/approval/audit features beyond what is necessary to satisfy ST-401 and keep the codebase coherent.
   - Leave clear TODOs only where they naturally connect to ST-402/ST-403/ST-404.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, verify the new migration is present and applies cleanly.

4. Manually verify API behavior using the project’s preferred approach (integration tests preferred; otherwise Swagger/Postman if available):
   - create a task with valid assigned agent
   - create a task without assigned agent if supported by the model
   - create a subtask with a valid parent task
   - attempt assignment to paused agent and confirm rejection
   - attempt assignment to archived agent and confirm rejection
   - update task through statuses:
     - `new`
     - `in_progress`
     - `blocked`
     - `awaiting_approval`
     - `completed`
     - `failed`
   - confirm `completed_at` behavior when completed
   - fetch task by id and confirm detail fields are returned
   - list tasks with filters and confirm tenant scoping

5. Confirm database mapping:
   - task records include `company_id`
   - JSON payload fields persist correctly
   - parent task linkage persists correctly
   - indexes/migration compile for PostgreSQL provider

6. Confirm no naming collisions or compile issues caused by `Task` vs `System.Threading.Tasks.Task`.

# Risks and follow-ups
- **Naming collision risk:** A domain entity named `Task` may conflict with `System.Threading.Tasks.Task`. Prefer a domain name that matches existing conventions or use explicit namespaces carefully.
- **Existing partial implementation risk:** The repository may already contain task/workflow scaffolding. Extend it rather than duplicating concepts.
- **Tenant enforcement risk:** Missing `company_id` filters in any query/update path would be a serious security issue. Review all handlers/endpoints carefully.
- **Agent status interpretation risk:** Story notes explicitly reject paused/archived assignment. If the current domain also blocks `restricted`, follow existing domain rules and document the behavior.
- **Status transition ambiguity:** Acceptance criteria define supported statuses but not strict transition rules. Keep transitions permissive unless the codebase already models a stricter lifecycle.
- **JSON payload mapping risk:** PostgreSQL JSONB mapping may vary by project conventions; reuse existing serializers/converters.
- **Follow-up for ST-402/ST-403:** Task records should remain compatible with future workflow and approval linkage, especially `workflow_instance_id`, `awaiting_approval`, and parent/child task relationships.
- **Follow-up for auditability:** If there is an existing audit pattern, consider lightweight task lifecycle audit hooks later, but do not block this task on full audit implementation.