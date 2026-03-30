# Goal
Implement backlog task **TASK-10.2.3 — Instance state and current step are persisted and queryable** for story **ST-402 Workflow definitions, instances, and triggers** in the existing .NET solution.

The coding agent should add or complete the workflow instance persistence/query path so that:

- workflow instances store **state**
- workflow instances store **current step**
- both values are updated durably in PostgreSQL
- both values are exposed through application queries/API endpoints already aligned with the project structure
- all reads and writes remain **tenant-scoped**
- the implementation fits the project’s **modular monolith + CQRS-lite** architecture

Because the story acceptance criteria are sparse, treat the backlog and architecture as the source of truth. Prefer minimal, production-appropriate implementation over speculative workflow-engine breadth.

# Scope
In scope:

- Persist `workflow_instances.state`
- Persist `workflow_instances.current_step`
- Ensure workflow instance creation initializes these fields consistently
- Ensure workflow progression/update logic can modify these fields
- Add query support to retrieve workflow instances including `state` and `current_step`
- Add or update API/application contracts/DTOs so these fields are visible to callers
- Add/update EF Core mappings, migrations, repositories, command/query handlers, and tests as needed
- Enforce `company_id` scoping on all workflow instance reads/writes

Out of scope unless required by existing code paths:

- Full workflow designer/builder UX
- Arbitrary workflow execution engine
- Scheduler implementation
- Event-trigger infrastructure beyond what is needed to persist/query instance state
- Rich exception management UI
- New cross-module abstractions not needed for this task

Implementation constraints:

- Follow existing project conventions and naming
- Keep changes localized to workflow/task orchestration areas
- Do not introduce direct DB access from controllers/UI
- Use application-layer commands/queries for state changes and reads
- Prefer additive changes that do not break in-flight or existing code

# Files to touch
Inspect the solution first, then modify the relevant files you actually find. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - workflow instance entity/aggregate
  - enums/value objects for workflow state
- `src/VirtualCompany.Application/**`
  - workflow commands
  - workflow queries
  - DTOs/view models
  - validators
  - interfaces for repositories/services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - repository implementations
  - migrations
- `src/VirtualCompany.Api/**`
  - workflow endpoints/controllers/minimal APIs
  - request/response contracts if API-specific
- `src/VirtualCompany.Web/**`
  - only if an existing page already displays workflow instance details and needs the new fields surfaced
- tests in any existing test projects
  - application tests
  - infrastructure persistence tests
  - API tests

At minimum, look for files related to:

- `WorkflowDefinition`
- `WorkflowInstance`
- `Task & Workflow`
- `DbContext`
- workflow endpoints/handlers
- tenant/company scoping patterns

# Implementation plan
1. **Discover the current workflow implementation**
   - Find existing workflow domain models, EF mappings, commands, queries, and API endpoints.
   - Determine whether `workflow_instances` already exists in code and whether `state` / `current_step` are missing, partially implemented, or not surfaced.
   - Identify the project’s tenant scoping pattern and reuse it consistently.

2. **Align the domain model with the architecture**
   - Ensure the workflow instance model includes:
     - `CompanyId`
     - `WorkflowDefinitionId`
     - `State`
     - `CurrentStep`
     - timestamps such as `StartedAt`, `UpdatedAt`, `CompletedAt`
     - optional context payload if already present
   - If the project uses enums/value objects for state, prefer that over raw strings; otherwise use constrained string values consistent with backlog language.
   - Keep state values pragmatic and compatible with story expectations, e.g. `running`, `blocked`, `failed`, `completed`, or whatever convention the codebase already uses.

3. **Persist the fields in Infrastructure**
   - Update EF Core configuration for workflow instances so `state` and `current_step` are mapped correctly.
   - Apply sensible constraints:
     - required `state`
     - nullable or required `current_step` based on lifecycle semantics already present
     - max lengths if the codebase uses them
   - If schema changes are needed, add an EF migration.
   - Ensure migration is safe for existing rows:
     - backfill a default `state` if necessary
     - set `current_step` to null or a known initial step where appropriate

4. **Initialize values on instance creation**
   - Update the workflow instance creation/start command path so new instances persist:
     - initial `state`
     - initial `current_step`
   - If the workflow definition JSON contains steps, derive the initial step from the first executable step only if that pattern already exists or is straightforward.
   - If no step parsing exists yet, use a minimal deterministic initialization strategy consistent with current architecture and document assumptions in code comments where needed.

5. **Support state/step updates during progression**
   - Find the workflow progression/update path and ensure it updates:
     - `State`
     - `CurrentStep`
     - `UpdatedAt`
     - `CompletedAt` when terminal
   - If no explicit progression command exists, add a focused application command for updating workflow instance status/step used by internal workflow execution paths.
   - Keep this internal and tenant-scoped; do not create an overly broad public mutation surface unless the existing API pattern requires it.

6. **Make workflow instances queryable**
   - Add or update application queries to fetch:
     - workflow instance by id
     - optionally list workflow instances for a company/definition if such query already exists
   - Ensure returned DTOs include:
     - instance id
     - workflow definition id
     - state
     - current step
     - timestamps
     - trigger metadata if already part of the contract
   - Enforce company scoping in query handlers/repositories.

7. **Expose through API**
   - Update existing workflow instance endpoints so responses include `state` and `current_step`.
   - If no endpoint exists but workflow APIs are already present, add a minimal read endpoint consistent with current routing conventions, for example:
     - get workflow instance by id
     - optionally list workflow instances for the current company
   - Do not bypass application layer abstractions.

8. **Handle blocked/failed visibility if already modeled**
   - Since ST-402 mentions failed or blocked steps surfacing exceptions for review, ensure at least the persisted `state` can represent blocked/failed conditions.
   - If there is already an exception field/table, wire it through only if necessary.
   - Do not expand into a full exception subsystem for this task.

9. **Add tests**
   - Add/adjust tests covering:
     - workflow instance creation persists initial `state` and `current_step`
     - workflow instance updates persist changed `state` and `current_step`
     - queries return these fields
     - tenant scoping prevents cross-company access
   - Prefer the project’s existing testing style.
   - If integration tests exist for EF/API, include at least one end-to-end persistence/query test.

10. **Keep implementation clean and incremental**
   - Reuse existing abstractions
   - Avoid speculative generalization
   - Add concise comments only where behavior is non-obvious
   - Ensure build/test passes before finishing

Suggested behavioral expectations if the codebase does not already define them:

- New workflow instance:
  - `State = Running` or equivalent existing initial state
  - `CurrentStep = first step identifier` if derivable, otherwise a deterministic placeholder/null per existing conventions
- On progression:
  - update `CurrentStep` to the active step
  - set `State = Blocked` / `Failed` / `Completed` as appropriate
- On completion:
  - set terminal state
  - set `CompletedAt`

# Validation steps
1. **Codebase inspection**
   - Confirm where workflow entities, handlers, and endpoints live.
   - Confirm tenant scoping pattern before changing anything.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Migration validation**
   - If a migration is added, verify it:
     - creates or alters the workflow instance columns correctly
     - preserves existing data safely
     - uses appropriate nullability/defaults

5. **Functional verification**
   - Start or simulate creation of a workflow instance and verify persisted values for:
     - `state`
     - `current_step`
   - Progress/update an instance and verify values change in storage.
   - Query the instance through the application/API and verify both fields are returned.

6. **Tenant isolation verification**
   - Confirm a workflow instance from company A cannot be queried or updated from company B context.
   - Match existing app behavior for forbidden/not found semantics.

7. **Regression check**
   - Ensure existing task/workflow flows still compile and function.
   - Ensure no controller/UI code now depends on infrastructure directly.

# Risks and follow-ups
- **Risk: workflow model is only partially scaffolded**
  - If workflow definitions/instances are not yet implemented, keep this task focused on the minimum vertical slice needed for persistence and querying rather than inventing the full engine.

- **Risk: unclear state vocabulary**
  - Reuse existing enums/constants if present. If absent, introduce a minimal state set and keep it easy to evolve.

- **Risk: deriving initial/current step from JSON definitions may be ambiguous**
  - If definition parsing is not mature, use a simple deterministic approach and avoid overengineering. Note assumptions in code.

- **Risk: tenant scoping gaps**
  - Be strict: every repository/query/update path must include `company_id` filtering.

- **Risk: migration compatibility**
  - Existing rows may need defaults/backfill. Make migration safe and explicit.

Follow-ups after this task, if not already covered elsewhere:

- richer workflow progression engine
- blocked/failed step exception records and review UX
- schedule/event trigger execution paths
- workflow instance history/audit trail
- list/filter endpoints for workflow monitoring by state/current step