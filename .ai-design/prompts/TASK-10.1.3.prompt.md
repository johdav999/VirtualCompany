# Goal
Implement backlog task **TASK-10.1.3 — Agent-created subtasks can reference a parent task** for story **ST-401 Task lifecycle and assignment** in the existing .NET solution.

This change should ensure that when an agent creates a subtask, the new task can persist and expose a valid `parent_task_id` relationship to an existing task, aligned with the architecture and data model where `tasks.parent_task_id` is nullable and used to model task hierarchies.

# Scope
Include only the work required to support parent-task references for agent-created subtasks within the task lifecycle/application stack.

In scope:
- Domain model support for parent task linkage if not already present.
- Application command/request DTO updates for task creation to optionally accept a parent task reference.
- Validation rules for parent task usage.
- Persistence/configuration updates in Infrastructure/EF Core if needed.
- API endpoint contract updates for task creation if exposed through the API.
- Query/read model updates so task details can return parent task information or at minimum `parentTaskId`.
- Tests covering valid and invalid parent-child task creation scenarios.

Constraints and expectations:
- Respect **multi-tenant isolation**: parent and child tasks must belong to the same `company_id`.
- Preserve existing task creation behavior for non-subtasks.
- Do not implement full task tree browsing UI unless already trivially supported.
- Do not expand into workflow-parent linkage beyond task-to-task parent references.
- Keep CQRS-lite boundaries intact.
- Assignment rules from ST-401 still apply: paused/archived agents must not receive assignment.
- Prefer minimal, production-ready changes over speculative abstractions.

If the repository already contains partial task support, extend the existing patterns rather than introducing parallel implementations.

# Files to touch
Inspect and update the relevant existing files under these projects as needed:

- `src/VirtualCompany.Domain`
  - Task aggregate/entity/value objects/enums
  - Domain validation or factory methods
- `src/VirtualCompany.Application`
  - Task create command/handler
  - Task DTOs/contracts
  - Validators
  - Task query models/handlers
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration/mappings
  - Migrations
  - Repository/query implementations
- `src/VirtualCompany.Api`
  - Task creation endpoints/controllers
  - Request/response contracts if API-specific
- Potential shared contracts if used:
  - `src/VirtualCompany.Shared`

Also review:
- Existing tests in corresponding test projects, and add/update tests wherever task creation and retrieval are already covered.

# Implementation plan
1. **Discover the current task implementation**
   - Find the existing task entity/aggregate, create-task command, API endpoint, and persistence mapping.
   - Confirm whether `parent_task_id` already exists in the database model, EF configuration, and domain object.
   - Identify how actor type is represented and whether agent-created tasks are already distinguished from human/system-created tasks.

2. **Add/complete domain support for parent task references**
   - Ensure the task domain model includes an optional `ParentTaskId`.
   - If the domain uses constructors/factory methods, update them to accept an optional parent task ID.
   - Keep the relationship nullable so normal top-level tasks remain unchanged.
   - If there is domain validation, enforce sensible invariants:
     - a task cannot reference itself as parent
     - parent-child linkage must be same-company
     - optional: only allow parent reference to an existing task

3. **Update task creation command/contracts**
   - Extend the create-task command/request DTO with an optional `ParentTaskId`.
   - Ensure this field is available for agent-created subtasks without making it mandatory for all tasks.
   - If there is a dedicated agent task creation path, update that path specifically; otherwise update the shared create-task flow.

4. **Implement application-layer validation**
   - In the command handler or validator:
     - load the parent task when `ParentTaskId` is provided
     - verify the parent exists
     - verify the parent belongs to the same company/tenant
     - reject self-parenting if applicable
   - Preserve existing assignment validation:
     - if assigning to an agent, reject paused/archived agents
   - Return existing error/result patterns used by the codebase.

5. **Update persistence mapping**
   - If EF/entity mapping is incomplete, configure the nullable self-reference on tasks.
   - Add or update migration only if the schema is not already present.
   - Prefer a foreign key from `tasks.parent_task_id -> tasks.id` with delete behavior that does not accidentally cascade-delete child tasks unless the codebase already has a clear convention.
   - Ensure indexes exist or are added if task hierarchy queries are expected and the project already manages indexes via migrations.

6. **Update read models and API responses**
   - Ensure task detail/query responses include `ParentTaskId`.
   - If the existing detail model supports related summaries, optionally include a lightweight parent task summary (`id`, `title`, `status`) only if consistent with current patterns.
   - Keep response changes backward-compatible where possible.

7. **Support agent-created subtasks explicitly**
   - Verify the agent orchestration/task creation path can pass `ParentTaskId` when creating delegated subtasks.
   - If there is a manager-worker or orchestration service already creating tasks, update it to populate the parent reference when a subtask is spawned from a parent task.
   - Do not build full multi-agent planning here; only wire the parent reference through the existing creation path.

8. **Add tests**
   - Add unit and/or integration tests for:
     - creating a task without `ParentTaskId` still succeeds
     - creating a subtask with a valid parent in the same company succeeds
     - creating a subtask with a nonexistent parent fails
     - creating a subtask with a parent from another company fails
     - assigning a subtask to a paused/archived agent fails if that rule already exists in the same flow
     - task retrieval returns `ParentTaskId`
   - If API integration tests exist, add one request/response test covering the new field.

9. **Keep implementation aligned with architecture**
   - Maintain tenant-scoped access and repository filtering.
   - Keep business logic in Application/Domain, not controllers.
   - Avoid direct DB access from orchestration code; use existing application/domain contracts.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used and a schema change was required:
   - Generate/apply the migration using the repository’s existing EF workflow.
   - Verify the `tasks` table includes nullable `parent_task_id` with the expected FK.

4. Manually verify the main scenarios through tests or local API calls:
   - Create a normal task with no parent.
   - Create an agent-created subtask with a valid `parentTaskId`.
   - Attempt to create a subtask with an invalid/nonexistent parent.
   - Attempt to create a subtask referencing a task from another tenant/company.
   - Fetch task details and confirm `parentTaskId` is returned.

5. Confirm no regressions:
   - Existing task creation flows still work.
   - Existing assignment validation still blocks paused/archived agents.
   - No tenant isolation violations are introduced.

# Risks and follow-ups
- **Schema mismatch risk:** The architecture already includes `parent_task_id` in the conceptual schema, but the actual codebase may not. If missing, migration work may be required.
- **Partial task stack risk:** The repository may have incomplete ST-401 implementation, so this task may require touching adjacent create/query code to make the feature usable end-to-end.
- **Tenant validation risk:** Self-referencing task relationships can accidentally bypass tenant isolation if parent lookup is not company-scoped.
- **Delete behavior risk:** Self-referencing FK cascade rules can create unintended deletions; prefer restrictive/no-action behavior unless the project already defines otherwise.
- **API contract drift:** Adding `ParentTaskId` to request/response models may require updates to clients or tests that assume a fixed payload shape.
- **Orchestration gap:** If agent-created tasks are not yet implemented, complete the foundational parent-reference support now and leave orchestration wiring as a small follow-up.

Suggested follow-ups after this task:
- Add child-task listing on task detail queries.
- Add parent/child task summaries in web UI.
- Add audit events for subtask creation and parent linkage.
- Add safeguards for task hierarchy depth if manager-worker orchestration expands later.