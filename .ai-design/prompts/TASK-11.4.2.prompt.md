# Goal
Implement backlog task **TASK-11.4.2** for story **ST-504 Manager-worker multi-agent collaboration**: ensure **subtasks are assigned, tracked, and linked to a parent task/workflow** within the existing .NET modular monolith.

This work should make manager-worker collaboration explicit and auditable by:
- allowing a parent task to spawn child subtasks,
- assigning each subtask to a worker agent,
- preserving links to the parent task and optional workflow instance,
- exposing query/read models so orchestration and UI/API layers can inspect collaboration state,
- enforcing tenant and agent validity rules consistent with the architecture.

No explicit acceptance criteria were provided for the task beyond the story context, so implement the smallest complete vertical slice that satisfies the story requirement and fits the existing architecture.

# Scope
In scope:
- Domain and application support for creating subtasks under a parent task.
- Validation that subtasks:
  - belong to the same company as the parent task,
  - can optionally inherit or explicitly set `workflow_instance_id`,
  - are assigned only to valid active/restricted-allowed agents per current business rules,
  - persist `parent_task_id`.
- Read/query support to retrieve a parent task with its subtasks.
- Minimal API surface or internal application service surface needed for orchestration engine integration.
- Persistence updates in PostgreSQL/EF Core mappings/repositories.
- Tests covering creation, linkage, and validation.

Out of scope unless already trivial in the codebase:
- Full manager-worker planner implementation.
- Final consolidated response generation.
- UI-heavy collaboration views.
- Background execution fan-out logic beyond what is necessary to persist and query subtasks.
- New workflow engine semantics beyond linking to existing `workflow_instance_id`.

Assume this task is a foundational backend capability for ST-504, not the entire story.

# Files to touch
Inspect the solution first and adapt to actual project structure/naming, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - Task aggregate/entity
  - Task status/value objects/enums if present
  - Domain validation/business rules for parent-child task relationships
- `src/VirtualCompany.Application/`
  - Commands/handlers for creating subtasks
  - Queries/handlers for retrieving task hierarchy
  - DTOs/contracts for task details and subtask summaries
  - Authorization/tenant scoping behaviors if applicable
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration/mappings
  - Repository/query implementations
  - Migration(s) if schema changes are needed
- `src/VirtualCompany.Api/`
  - Endpoint/controller for subtask creation and parent-task retrieval, if API layer exists for tasks
- Potentially `src/VirtualCompany.Shared/`
  - Shared contracts if task DTOs are shared across web/mobile
- Tests:
  - `tests/...` or corresponding test projects for domain/application/integration coverage

Likely concrete artifacts to add or update:
- Task entity configuration for self-referencing parent/children relationship
- `CreateSubtaskCommand` + handler
- `GetTaskById` / `GetTaskDetails` query to include child tasks
- Request/response DTOs for subtask creation
- EF migration if indexes/constraints are missing for `parent_task_id` and `workflow_instance_id`

# Implementation plan
1. **Inspect existing task/workflow implementation**
   - Find current task entity, repository, command/query handlers, and API endpoints.
   - Confirm whether `tasks.parent_task_id` and `tasks.workflow_instance_id` already exist in code and database.
   - Confirm current assignment rules from ST-401, especially paused/archived agent rejection.
   - Reuse existing CQRS-lite patterns, validation approach, and tenant scoping conventions.

2. **Model parent-child task relationship explicitly**
   - If not already modeled, update the domain task entity to include:
     - `ParentTaskId`
     - optional navigation/reference to parent
     - child task collection if the project uses navigations
   - Preserve existing task lifecycle fields.
   - Add domain guard methods or constructor/factory logic for creating a subtask from a parent task.
   - Enforce invariants:
     - parent task must exist,
     - child company must match parent company,
     - child cannot parent itself,
     - optional depth guard only if there is already a collaboration limit concept in code; otherwise do not invent broad new policy here.

3. **Add/create application command for subtask creation**
   - Implement a command such as `CreateSubtaskCommand` with fields like:
     - `CompanyId`
     - `ParentTaskId`
     - `AssignedAgentId`
     - `Title`
     - `Description`
     - `Type`
     - `Priority`
     - `DueAt`
     - `InputPayload`
     - optional `WorkflowInstanceId`
   - Handler behavior:
     - load parent task by `CompanyId` + `ParentTaskId`,
     - validate assigned agent exists in same company,
     - reject paused/archived agents per ST-401,
     - set `parent_task_id` to parent task id,
     - set `workflow_instance_id` to explicit value if valid, otherwise inherit from parent when appropriate,
     - initialize status to `new`,
     - persist and return created subtask details.

4. **Ensure workflow linkage behavior is consistent**
   - If parent task has a `workflow_instance_id`, child subtasks should default to the same workflow instance unless explicitly overridden with a valid same-company workflow instance.
   - Validate any provided workflow instance belongs to the same company.
   - Do not create new workflow semantics; just maintain correct linkage.

5. **Update persistence mappings and schema**
   - In EF Core configuration:
     - configure self-referencing relationship for tasks,
     - ensure delete behavior is safe (likely restrict/no action rather than cascade),
     - ensure indexes exist on:
       - `company_id`
       - `parent_task_id`
       - `workflow_instance_id`
       - possibly composite indexes used by common queries.
   - If the schema is incomplete, add a migration.
   - Keep tenant isolation explicit in repository/query filters.

6. **Add query support for tracking subtasks**
   - Extend existing task detail query or add a new query to return:
     - parent task core fields,
     - linked workflow instance id,
     - subtask list with assignment, status, priority, due date, created/completed timestamps.
   - Keep response shape simple and orchestration-friendly.
   - If there is already a task detail DTO, add a `Subtasks` collection rather than creating a parallel model unnecessarily.

7. **Expose minimal API/application surface**
   - If task APIs already exist, add:
     - `POST /api/tasks/{parentTaskId}/subtasks`
     - `GET /api/tasks/{taskId}` or equivalent enhancement to include subtasks
   - Follow existing authorization and company resolution patterns.
   - If API endpoints are not yet the established pattern, keep this internal to application services and wire only what the current architecture uses.

8. **Audit/observability alignment**
   - If task creation already emits audit events or domain events, ensure subtask creation uses the same mechanism.
   - Include enough metadata to identify:
     - parent task,
     - assigned worker agent,
     - workflow instance linkage.
   - Do not introduce a brand-new audit subsystem if one is not yet present; integrate with existing patterns only.

9. **Testing**
   - Add domain/application tests for:
     - creating a subtask under a valid parent,
     - inheriting workflow instance from parent,
     - rejecting cross-company parent/agent/workflow linkage,
     - rejecting assignment to paused/archived agents,
     - retrieving parent task with child subtasks.
   - Add integration tests for persistence/query behavior if the repo already has them.

10. **Keep implementation bounded to ST-504 foundation**
   - Avoid implementing free-form agent messaging or planner logic here.
   - The deliverable is reliable subtask persistence and retrieval that the multi-agent coordinator can use.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo:
   - generate/apply migration as appropriate for local validation,
   - verify the `tasks` table supports self-reference and indexes.

4. Manually validate the main flow using API/integration tests or local endpoints:
   - create or identify a company, active parent task, and active worker agent,
   - create a subtask under the parent,
   - verify persisted fields:
     - `company_id`
     - `assigned_agent_id`
     - `parent_task_id`
     - `workflow_instance_id`
     - `status = new`
   - fetch parent task details and confirm the subtask appears in the returned hierarchy/list.

5. Validate negative cases:
   - attempt subtask creation with agent from another company,
   - attempt subtask creation for paused/archived agent,
   - attempt explicit workflow linkage to another company’s workflow instance,
   - confirm safe validation errors are returned.

6. Confirm no regressions in existing task lifecycle behavior:
   - standard task creation still works,
   - existing task detail queries still return expected fields,
   - no accidental cascade delete or broken foreign key behavior.

# Risks and follow-ups
- **Schema drift risk:** The architecture already describes `parent_task_id` and `workflow_instance_id`; the codebase may partially implement them. Prefer extending existing structures over duplicating concepts.
- **Assignment rule ambiguity:** ST-401 explicitly rejects paused/archived agents. If current code also restricts `restricted` agents, follow existing business rules rather than inventing new ones.
- **Hierarchy depth/fan-out limits:** ST-504 notes bounded collaboration, but this task should not overreach into planner policy unless a clear existing place for those limits already exists.
- **Query performance:** Parent-with-subtasks retrieval may need indexes and projection-based queries to avoid N+1 issues.
- **Workflow consistency:** Be careful not to allow cross-company or mismatched workflow linkage when inheriting/overriding `workflow_instance_id`.
- **Future follow-up:** Subsequent tasks will likely need:
  - coordinator plan records,
  - subtask dependency tracking,
  - consolidated result aggregation with per-agent attribution,
  - bounded fan-out/depth/runtime enforcement,
  - richer UI for collaboration trees.