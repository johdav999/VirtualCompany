# Goal
Implement backlog task **TASK-10.1.1** for **ST-401 Task lifecycle and assignment** so users can create tasks with:
- type
- title
- description
- priority
- due date
- assigned agent

This should fit the existing **.NET modular monolith** architecture, follow **CQRS-lite**, enforce **tenant scoping**, and respect the story note that **assignment must reject paused/archived agents**.

Because no explicit acceptance criteria were provided for this task beyond the backlog story, treat the backlog story acceptance criteria for ST-401 as the source of truth for this implementation slice, with focus on **task creation only**.

# Scope
Implement the minimum vertical slice needed for **creating a task** from the web/API layer down to persistence.

Include:
- Domain support for task creation if missing
- Application command + validation for creating tasks
- Tenant-aware agent assignment validation
- Persistence mapping for tasks
- API endpoint for task creation
- Basic web UI entry point only if a task creation surface already exists or can be added with low friction
- Tests for command validation and assignment rules

Required behavior:
- Create a task with `type`, `title`, `description`, `priority`, `due date`, and `assigned agent`
- Persist `company_id`
- Persist creator metadata as human actor when invoked by an authenticated user
- Default new tasks to status `new`
- Reject assignment if the selected agent:
  - does not belong to the current company
  - is `paused`
  - is `archived`
- Allow unassigned tasks only if the current codebase/backlog slice already supports nullable `assigned_agent_id`; otherwise keep assignment required for this task and document the constraint

Out of scope unless already trivial in the existing codebase:
- Full task list/detail UI
- Task editing
- Status transitions beyond default `new`
- Parent/subtask creation UX
- Workflow linkage
- Approval integration
- Output payload / rationale / confidence population
- Mobile support

# Files to touch
Inspect the solution first and adjust to actual conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - Task aggregate/entity/value objects/enums if missing
  - Agent status enum or related domain rules if needed

- `src/VirtualCompany.Application/`
  - `Tasks/Commands/CreateTask/...`
  - DTO/request/response models
  - Validators
  - Interfaces for repositories/unit of work if needed
  - Authorization/tenant context abstractions if already present

- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration for `tasks`
  - Repository implementation
  - DbContext updates
  - Migration for `tasks` table if not already present
  - Query filters or tenant enforcement wiring if used in this project

- `src/VirtualCompany.Api/`
  - Task creation endpoint/controller/minimal API mapping
  - Request contract mapping to application command
  - Authenticated user/company context resolution

- `src/VirtualCompany.Web/`
  - Only if there is an existing tasks page/form pattern to extend
  - Add a simple create-task form if low effort and aligned with current app structure

- Tests project(s) if present
  - Application tests for command handler and validation
  - API/integration tests if test infrastructure already exists

Also review:
- `README.md`
- solution-wide patterns for MediatR, FluentValidation, Result types, exceptions, and EF migrations

# Implementation plan
1. **Inspect current architecture and conventions**
   - Confirm whether the solution already uses:
     - MediatR or similar request handlers
     - FluentValidation
     - EF Core
     - repository pattern vs direct DbContext
     - minimal APIs vs controllers
     - current tenant/user context abstraction
   - Reuse existing patterns exactly; do not introduce a new architectural style.

2. **Locate or add task domain model**
   - Check whether `Task`/`WorkTask` entity already exists.
   - If missing, add a domain entity aligned to the architecture/backlog schema:
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
   - For this task, only fully wire the fields needed for creation plus sensible defaults for the rest.
   - Prefer enums/value objects if the codebase already uses them; otherwise use constrained strings consistently.

3. **Define allowed creation values**
   - Add or reuse enums/constants for:
     - task status: default `new`
     - priority
     - actor type: `human`
   - Keep `type` flexible if the architecture expects free-form text, but validate non-empty and reasonable length.
   - Add validation rules for:
     - title required
     - type required
     - description optional unless existing conventions require it
     - priority required
     - due date optional or required based on the exact backlog wording and current UX conventions; since the story says users can create tasks with due date, support nullable due date unless existing product assumptions require mandatory entry
     - assigned agent required if implementing assigned creation only

4. **Implement application command**
   - Add `CreateTaskCommand` and handler in the Application layer.
   - Inputs:
     - `Type`
     - `Title`
     - `Description`
     - `Priority`
     - `DueAt`
     - `AssignedAgentId`
   - Resolve current:
     - `CompanyId`
     - `UserId`
   - Validate:
     - tenant context exists
     - assigned agent exists in same company
     - assigned agent status is not `paused` or `archived`
     - optionally reject `restricted` if current business rules already do so elsewhere; otherwise do not invent this rule
   - Create and persist task with:
     - `Status = new`
     - `CreatedByActorType = human`
     - `CreatedByActorId = current user id`
     - timestamps set
   - Return created task id and key summary fields.

5. **Persistence wiring**
   - Add/update EF Core configuration for the tasks table.
   - Ensure column names/types align with the architecture schema where practical.
   - If the table does not exist, create a migration.
   - Add indexes that are low-cost and useful now, such as:
     - `(company_id, created_at)`
     - `(company_id, assigned_agent_id, status)` if consistent with current migration style
   - Ensure `assigned_agent_id` FK is nullable or non-nullable based on the chosen implementation and current schema direction.
   - Preserve tenant isolation patterns already used in the solution.

6. **Agent assignment validation**
   - Query the assigned agent by `id + company_id`.
   - Reject if not found.
   - Reject if status is `paused` or `archived`.
   - Use existing application error/result conventions:
     - validation error
     - domain/business rule violation
     - not found
   - Do not leak cross-tenant existence information.

7. **Expose API endpoint**
   - Add a POST endpoint such as `/api/tasks` following existing route conventions.
   - Require authentication and tenant/company context.
   - Map request DTO to command.
   - Return:
     - `201 Created` with created resource id/details if that is the project convention
     - validation errors in the project’s standard format
   - If there is already a tasks endpoint group/controller, extend it rather than creating a parallel style.

8. **Optional web form**
   - Only if the web project already has task-related navigation or a conventional CRUD page pattern.
   - Add a simple create form with:
     - type
     - title
     - description
     - priority
     - due date
     - assigned agent selector
   - Populate agent selector with active company agents only, or all eligible agents depending on existing UX patterns.
   - Show validation messages.
   - If no suitable UI pattern exists, skip UI and keep this task API/application-complete.

9. **Testing**
   - Add unit tests for:
     - successful task creation
     - rejects paused agent
     - rejects archived agent
     - rejects agent from another company
     - defaults status to `new`
     - stores creator as human/current user
   - Add validator tests for required fields.
   - Add integration/API test if infrastructure exists:
     - authenticated tenant user can POST and create task
     - invalid assignment returns expected error

10. **Keep implementation narrow**
   - Do not implement status transitions, subtasks, workflow orchestration, or approval logic unless required to make the create flow compile.
   - Leave clear TODOs only where they map directly to later backlog items like ST-401 remainder, ST-402, or ST-403.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF Core migrations are used, verify migration generation/apply flow:
   - create migration if needed
   - ensure app starts cleanly against local DB

4. Manually verify API behavior:
   - authenticated request with valid company context can create a task
   - response includes created id and persisted fields
   - created task has status `new`
   - created task stores current user as creator with actor type `human`

5. Negative-path verification:
   - assign to paused agent => rejected
   - assign to archived agent => rejected
   - assign to agent from another company => rejected/not found per existing security convention
   - missing title/type/priority => validation errors

6. If web UI is added:
   - open create task page
   - submit valid form
   - confirm success path
   - confirm validation messages render correctly

# Risks and follow-ups
- **Naming conflict risk:** `Task` may conflict with `System.Threading.Tasks.Task`. If so, use an existing naming convention like `WorkTask` in domain/application code while mapping to the `tasks` table.
- **Tenant context dependency:** If tenant resolution is not fully implemented yet, this task may need to integrate with existing membership/company context infrastructure first.
- **Schema drift risk:** The architecture defines a broad `tasks` schema; implement only what is needed now, but avoid painting the model into a corner.
- **Assignment rule ambiguity:** Backlog explicitly rejects `paused/archived` agents. Do not additionally reject `restricted` unless existing domain rules already require it.
- **Due date ambiguity:** The story lists due date as a creatable field, but not whether it is mandatory. Prefer nullable unless current product conventions say otherwise.
- **UI scope creep:** Avoid building a full tasks experience in this task. API + application + persistence is the priority.
- **Follow-up backlog likely needed:**
  - task list/detail queries
  - task status transitions
  - parent/subtask support
  - workflow/task linkage
  - audit event creation for task creation
  - task detail payload fields population
  - role-based authorization for who can create/assign tasks