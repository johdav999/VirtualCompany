# Goal
Implement backlog task **TASK-10.1.5 / ST-401 — Task lifecycle and assignment** by introducing the **Task entity as a first-class orchestration and audit backbone** in the existing .NET modular monolith.

The coding agent should deliver a vertical slice that supports:
- creating tasks,
- assigning tasks to eligible agents,
- persisting task lifecycle state,
- supporting parent/child task relationships for subtasks,
- storing orchestration/audit-relevant task fields,
- exposing CQRS-lite application APIs for command/query flows.

This work should align with the architecture and backlog, especially:
- multi-tenant shared-schema enforcement via `company_id`,
- modular monolith boundaries,
- CQRS-lite in the application layer,
- PostgreSQL persistence,
- auditability-oriented task fields,
- rejection of assignment to paused or archived agents.

# Scope
Implement only what is necessary to satisfy **ST-401** and establish a solid domain/application/persistence foundation for later workflow, approval, orchestration, and audit stories.

Include:
- Domain model for `Task`
- Task status model with supported statuses:
  - `new`
  - `in_progress`
  - `blocked`
  - `awaiting_approval`
  - `completed`
  - `failed`
- Parent task linkage for subtasks
- Assignment to agent with validation against agent status
- Multi-tenant persistence with `company_id`
- Core task fields:
  - type
  - title
  - description
  - priority
  - due date
  - assigned agent
  - input payload
  - output payload
  - rationale summary
  - confidence score
  - parent task id
- CQRS-lite commands/queries and API endpoints
- Basic task detail/read models

Do not overreach into:
- full workflow engine behavior
- approval execution logic
- orchestration engine integration
- notifications
- UI-heavy implementation unless a minimal API surface already expects it
- full audit event fan-out unless there is already an established pattern to hook into

If there is an existing architectural pattern in the repo for entities, commands, handlers, repositories, EF configurations, and controllers/endpoints, follow it exactly.

# Files to touch
Inspect the solution first and then update the appropriate files in the existing project structure. Expected areas:

- `src/VirtualCompany.Domain`
  - task aggregate/entity
  - task status/priority/value objects or enums
  - domain validation/business rules
- `src/VirtualCompany.Application`
  - commands for create/update status/assign if separated
  - queries for task detail/list
  - DTOs/read models
  - validators
  - interfaces for repositories/services
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration
  - repository implementation
  - DbContext updates
  - migration for `tasks` table if migrations are used in-repo
- `src/VirtualCompany.Api`
  - task endpoints/controllers
  - request/response contracts if API owns them
- Potentially `src/VirtualCompany.Shared`
  - shared contracts only if this is the established convention

Likely concrete additions/changes:
- Domain task model
- Agent status validation integration
- Persistence mapping for `tasks`
- Application command/query handlers
- API endpoints for:
  - create task
  - get task by id
  - optionally list tasks for company/agent if trivial and pattern-consistent

Also inspect whether these already exist and should be extended instead of recreated:
- base entity abstractions
- tenant-scoped repository/query patterns
- result/error abstractions
- authorization/company context accessors
- existing `Agent` entity and status enum
- existing migrations folder and naming conventions

# Implementation plan
1. **Inspect the current architecture in code**
   - Review `README.md`, solution structure, and each project’s conventions.
   - Identify:
     - domain entity patterns,
     - EF Core configuration style,
     - command/query handler style,
     - API endpoint style,
     - tenant context resolution,
     - whether MediatR/FluentValidation/Minimal APIs are already used.
   - Reuse existing patterns rather than inventing new ones.

2. **Model the Task domain entity**
   - Add a `Task` domain entity/aggregate in the appropriate module namespace.
   - Include fields aligned to the architecture/backlog:
     - `Id`
     - `CompanyId`
     - `AssignedAgentId` nullable
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
     - `WorkflowInstanceId` nullable for future compatibility
     - `CreatedAt`
     - `UpdatedAt`
     - `CompletedAt`
   - Prefer strong typing for status/priority/actor type if the codebase uses enums or value objects.
   - Add domain behavior methods where appropriate, such as:
     - create
     - assign agent
     - start / mark in progress
     - mark blocked
     - mark awaiting approval
     - complete
     - fail
     - update outputs/rationale/confidence
   - Enforce valid transitions if practical without overengineering. At minimum:
     - `CompletedAt` is set when completed
     - assignment to invalid agent states is blocked
     - required fields are validated

3. **Define task lifecycle rules**
   - Support the required statuses exactly as the story specifies.
   - Implement minimal but sensible transition guards, for example:
     - cannot complete an already failed/completed task unless existing patterns suggest otherwise
     - cannot assign paused/archived agents
   - If agent statuses are modeled as `active`, `paused`, `restricted`, `archived`, then:
     - reject `paused`
     - reject `archived`
     - allow `active`
     - treat `restricted` according to existing business rules; if no rule exists, do not reject unless explicitly required by current story

4. **Add persistence mapping**
   - Update the EF Core DbContext and entity configuration.
   - Map the `tasks` table according to the architecture schema.
   - Ensure:
     - tenant-owned row includes `company_id`
     - parent task self-reference is nullable
     - assigned agent FK is nullable
     - JSON payload fields are mapped appropriately for PostgreSQL/JSONB if that pattern exists
     - timestamps are stored consistently
   - Add indexes that are likely useful and low-risk:
     - `(company_id, status)`
     - `(company_id, assigned_agent_id)`
     - `(company_id, parent_task_id)`
     - maybe `(company_id, created_at)` if consistent with conventions
   - Add migration if the repo uses committed migrations.

5. **Implement repository/query access**
   - Add or extend repository abstractions for task persistence.
   - Ensure all reads/writes are tenant-scoped by `company_id`.
   - Add methods such as:
     - create/add task
     - get task by id + company id
     - list/filter tasks if needed
     - verify parent task belongs to same company
     - verify assigned agent belongs to same company and is assignable

6. **Implement application commands**
   - Add a command for task creation, e.g. `CreateTaskCommand`.
   - The command should accept:
     - type
     - title
     - description
     - priority
     - due date
     - assigned agent id
     - input payload
     - parent task id
   - In the handler:
     - resolve current company context
     - resolve current actor context if available
     - validate assigned agent exists in same company
     - reject paused/archived agents
     - validate parent task exists in same company if provided
     - create and persist task
   - If the codebase already separates assignment/status updates into separate commands, follow that pattern. If not, keep scope minimal and creation-focused.

7. **Implement application queries**
   - Add a query for task detail by id.
   - Return a read model including all story-relevant fields:
     - identity
     - assignment
     - lifecycle status
     - parent task reference
     - payload/rationale/confidence fields
     - timestamps
   - Optionally add a simple list query if the existing API pattern expects it and it is low effort.

8. **Expose API endpoints**
   - Add endpoints/controller actions for:
     - `POST /tasks`
     - `GET /tasks/{id}`
   - If conventions support listing:
     - `GET /tasks`
   - Ensure endpoints are tenant-scoped through existing auth/company context mechanisms.
   - Return appropriate status codes:
     - `201 Created` for successful creation
     - `400 Bad Request` for validation failures
     - `404 Not Found` when task not found in tenant scope
     - `403/404` according to existing tenant isolation conventions
   - Do not leak cross-tenant existence information.

9. **Validation**
   - Add request/application validation for:
     - required title/type/priority/status inputs as applicable
     - title length and sensible field constraints based on existing conventions
     - confidence score range if writable
     - due date format
   - Keep validation centralized in the application layer if that is the project convention.

10. **Tests**
   - Add or update tests in the existing test project structure.
   - Cover at least:
     - create task successfully
     - create task with parent task successfully
     - reject assignment to paused agent
     - reject assignment to archived agent
     - reject parent task from another company
     - get task detail only within tenant scope
     - completed task sets `CompletedAt` if lifecycle methods are implemented/testable
   - Prefer unit tests for domain/application rules and integration tests for persistence/API if the repo already has those patterns.

11. **Keep future stories unblocked**
   - Design the task model so later stories can attach:
     - workflow instance linkage,
     - approvals,
     - tool executions,
     - audit events,
     - orchestration outputs.
   - Avoid premature implementation of those features, but leave the schema and naming compatible with the architecture document.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used:
   - generate/apply migration using the repo’s established process
   - verify the `tasks` table schema matches the intended fields and relationships

4. Manually verify API behavior with the existing local setup:
   - create a task for an active agent
   - fetch the created task
   - create a subtask with `parentTaskId`
   - attempt to assign a task to a paused agent and confirm validation failure
   - attempt cross-tenant access and confirm tenant isolation behavior

5. Confirm persistence details:
   - `company_id` is always populated
   - JSON payload fields serialize/deserialize correctly
   - `completed_at` remains null until completion
   - parent-child linkage persists correctly

6. Confirm code quality:
   - no tenant filtering bypasses
   - no direct infrastructure leakage into API/UI layers
   - naming and namespaces match existing module conventions

# Risks and follow-ups
- **Naming collision risk:** `Task` can conflict with `System.Threading.Tasks.Task`. If needed, use a more explicit domain name internally such as `WorkTask` while preserving API/resource naming as `tasks`, or use namespace aliasing consistently.
- **Agent status ambiguity:** The story explicitly rejects paused/archived assignment, but not restricted. Follow existing domain rules if present; otherwise document the chosen behavior.
- **Tenant enforcement gaps:** This story is highly sensitive to cross-tenant leakage. Reuse existing tenant-scoping infrastructure rather than ad hoc filters.
- **Migration drift:** If the repo already has partial task/workflow schema work, extend it carefully instead of creating duplicate concepts.
- **API contract uncertainty:** If there is already a task contract or generated client pattern, update that instead of introducing parallel DTOs.
- **Future follow-ups likely needed after this task:**
  - explicit task status update commands/endpoints,
  - task list/filter endpoints,
  - workflow instance linkage,
  - approval state transitions,
  - audit event creation on task lifecycle changes,
  - orchestration engine integration for agent-created tasks/subtasks.