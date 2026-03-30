# Goal
Implement **TASK-10.1.7 — Keep task APIs CQRS-lite** for **ST-401 Task lifecycle and assignment** in the existing .NET modular monolith.

The objective is to ensure the **task application/API surface cleanly separates commands from queries** without introducing full CQRS complexity or event sourcing. State-changing operations must flow through explicit command handlers/services, while read operations must use dedicated query handlers/services/read models. Keep the design pragmatic, tenant-scoped, and aligned with the architecture note: **“CQRS-lite for application layer — Commands for state changes, queries for dashboards and views.”**

# Scope
In scope:

- Review the current task-related implementation across API, Application, Domain, and Infrastructure layers.
- Refactor or implement task endpoints so they are clearly split into:
  - **Commands** for create/update/assign/status transitions/subtask creation
  - **Queries** for get-by-id/list/detail views
- Ensure command paths do not return rich read models beyond what is necessary.
- Ensure query paths do not mutate state.
- Keep tenant scoping enforced on all task operations.
- Preserve ST-401 behavior:
  - create tasks
  - assign tasks to agents
  - support statuses
  - support parent task references
  - store task detail fields like input/output payload, rationale summary, confidence score
- Keep implementation lightweight and idiomatic for the current codebase; do **not** introduce MediatR or a large framework unless already present and clearly established in the repo.

Out of scope:

- Full event sourcing
- Separate read database
- Message broker changes
- Workflow/approval redesign beyond what is necessary for task API separation
- UI redesign unless compile errors require small contract updates
- Mobile-specific work

# Files to touch
Inspect first, then update only what is necessary. Likely areas:

- `src/VirtualCompany.Api/**`
  - Task endpoints/controllers/minimal API mappings
  - Request/response contracts if API contracts need CQRS-lite separation
- `src/VirtualCompany.Application/**`
  - Task command handlers/services
  - Task query handlers/services
  - DTOs/read models
  - Validation logic
- `src/VirtualCompany.Domain/**`
  - Task aggregate/entity methods if command-side invariants belong in domain
  - Status/assignment rules
- `src/VirtualCompany.Infrastructure/**`
  - Task repositories
  - Query repositories/read projections
  - EF Core mappings if needed
- `src/VirtualCompany.Shared/**`
  - Shared contracts only if this solution already uses shared DTOs across projects
- Tests in any existing test projects
  - Add or update tests covering command/query separation and behavior

Also review:

- `README.md`
- `VirtualCompany.sln`

# Implementation plan
1. **Inspect the existing task implementation**
   - Find all task-related endpoints, services, repositories, DTOs, and tests.
   - Determine the current architectural style already used in the solution:
     - controllers vs minimal APIs
     - service classes vs handlers
     - repository patterns
     - validation approach
   - Identify where task reads and writes are currently mixed.

2. **Define the CQRS-lite shape consistent with the repo**
   - Introduce or align to a simple split such as:
     - `Commands/Tasks/...`
     - `Queries/Tasks/...`
   - Keep naming explicit and boring:
     - `CreateTaskCommand`
     - `AssignTaskCommand`
     - `UpdateTaskStatusCommand`
     - `CreateSubtaskCommand`
     - `GetTaskByIdQuery`
     - `ListTasksQuery`
   - If the repo already uses application services instead of handlers, preserve that style but still separate write and read concerns into distinct classes/interfaces.

3. **Separate write operations into command-side logic**
   - Ensure all state-changing task operations go through command-side application logic.
   - Commands should enforce business rules such as:
     - tenant scoping
     - paused/archived agents cannot be assigned tasks
     - valid status transitions if such rules already exist or are appropriate to add
     - parent task references remain tenant-safe
   - Command results should be minimal:
     - created/updated identifiers
     - success/failure
     - maybe version/timestamp if the codebase already uses it
   - Avoid returning full hydrated detail views from command handlers.

4. **Separate read operations into query-side logic**
   - Implement dedicated query-side retrieval for:
     - task detail by id
     - task list/search/filter if present
   - Query models may be optimized for API/view needs and can include related display data if appropriate.
   - Ensure query handlers/services are read-only and do not trigger side effects or state changes.

5. **Refactor API endpoints to reflect command/query intent**
   - Keep REST shape pragmatic, but map endpoints internally to command/query paths.
   - Example intent:
     - `POST /tasks` -> create command
     - `POST /tasks/{id}/assign` or equivalent -> assign command
     - `POST /tasks/{id}/status` or equivalent -> status transition command
     - `GET /tasks/{id}` -> detail query
     - `GET /tasks` -> list query
   - If existing routes differ, preserve compatibility where reasonable and only change routes if necessary.
   - Make request/response types clearly command-oriented vs query-oriented.

6. **Keep domain invariants on the write side**
   - If the domain layer already contains task behavior, move/retain invariants there rather than duplicating them in controllers.
   - Ensure assignment and lifecycle rules are enforced in one place.
   - Do not push query concerns into domain entities.

7. **Keep persistence pragmatic**
   - Reuse the same transactional database and EF Core context/repositories.
   - It is acceptable for command and query paths to share the same DbContext while using different classes/interfaces.
   - If useful, create a dedicated query repository/read service for task projections instead of reusing write repositories for everything.

8. **Add or update tests**
   - Cover at minimum:
     - creating a task through command path
     - assigning a task rejects paused/archived agents
     - creating a subtask with parent reference
     - querying task detail does not mutate state
     - command handlers do not expose rich read-model coupling
     - tenant scoping on both command and query paths
   - Prefer existing test conventions in the repo.

9. **Document with light touch**
   - Add concise comments only where structure is not obvious.
   - If helpful, add a short note in the relevant application/API area clarifying the CQRS-lite convention for tasks.

10. **Keep changes minimal and coherent**
   - Do not over-engineer.
   - Do not introduce a new framework or abstraction layer unless the repository already uses it.
   - Favor consistency with the existing solution over idealized architecture.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify task API behavior in code or existing API tests:
   - create task uses command path
   - assign task uses command path
   - status update uses command path
   - get task/detail uses query path
   - list tasks uses query path

4. Confirm architectural intent in the code:
   - no mixed read/write “god service” for tasks if avoidable
   - no query endpoint calling mutation logic
   - no command endpoint assembling large dashboard/detail projections unnecessarily

5. Confirm ST-401 compatibility:
   - task create fields supported: type, title, description, priority, due date, assigned agent
   - statuses supported: `new`, `in_progress`, `blocked`, `awaiting_approval`, `completed`, `failed`
   - parent task/subtask support remains intact
   - detail fields remain available where applicable: input payload, output payload, rationale summary, confidence score

6. Confirm tenant safety:
   - all task reads and writes are company-scoped
   - cross-tenant task or agent access is rejected/not found per existing conventions

# Risks and follow-ups
- **Risk: existing code may already mix reads/writes deeply**
  - Refactor incrementally; do not attempt broad architectural cleanup outside task APIs.

- **Risk: route/contracts may be consumed by Web or Mobile**
  - Preserve existing external contracts where possible; prefer internal refactoring over breaking API changes.

- **Risk: over-abstracting CQRS-lite into full CQRS**
  - Avoid buses, event sourcing, separate databases, or excessive ceremony.

- **Risk: duplicated mapping logic between command and query models**
  - Accept small duplication if it keeps responsibilities clear.

- **Risk: tenant scoping gaps**
  - Verify every repository/query path includes company scoping, especially parent task and assigned agent lookups.

Suggested follow-ups after this task, only if clearly needed:
- Apply the same CQRS-lite pattern consistently to workflows and approvals.
- Introduce shared conventions for command/query foldering and naming across modules.
- Add API/integration tests for tenant-scoped task lifecycle flows.