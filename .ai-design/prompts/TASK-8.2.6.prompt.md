# Goal
Implement backlog task **TASK-8.2.6 — Restrict archived agents from new task assignment** for story **ST-202 Agent operating profile management**.

Ensure the system prevents any **new task assignment** to agents whose status is `archived`, while preserving tenant isolation, clean architecture boundaries, and consistent validation behavior across application and UI layers.

This should align with related backlog notes:
- **ST-202:** “Restrict archived agents from new task assignment.”
- **ST-401:** “Assignment must reject paused/archived agents.”

# Scope
Include:
- Domain/application enforcement that rejects assigning a task to an archived agent.
- Enforcement for both:
  - creating a new task with `assigned_agent_id`
  - updating/reassigning an existing task to an archived agent
- Tenant-safe validation when resolving the target agent.
- Clear validation/error messaging suitable for API/UI consumption.
- Tests covering allowed and rejected cases.

Prefer enforcing this in the **application layer** where task commands are handled, with any supporting domain guard methods if the current codebase patterns support that.

If the current implementation already rejects `paused` agents, preserve that behavior and extend it cleanly to `archived`. If it does not, do **not** broaden scope unless the existing assignment validation is clearly centralized and trivial to update safely; the minimum required behavior is archived-agent rejection.

Out of scope:
- Historical task cleanup for already-assigned archived agents.
- Background migration/data repair.
- Changes to orchestration/runtime execution beyond task assignment entry points.
- New database schema changes unless absolutely required.
- Large UX redesigns.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas if they exist:

- **Domain**
  - `src/VirtualCompany.Domain/**/Agent*.cs`
  - `src/VirtualCompany.Domain/**/Task*.cs`
  - any enums/value objects for agent status

- **Application**
  - task create/update/reassign command handlers in:
    - `src/VirtualCompany.Application/**/Tasks/**`
    - `src/VirtualCompany.Application/**/Task*Handler.cs`
  - agent lookup/query/repository abstractions
  - shared validation/error result types

- **Infrastructure**
  - repository implementations only if needed for agent lookup with company scoping

- **API**
  - request/response mapping only if validation errors need explicit translation

- **Web**
  - task assignment UI validation/disabled state only if there is already a task form that lists assignable agents and the change is low-risk
  - do not build major UI work if backend validation is sufficient for this task

- **Tests**
  - `tests/**` or corresponding test projects for application/domain behavior
  - add/extend tests for task creation and reassignment validation

Use the actual project structure and naming conventions found in the repo rather than inventing new patterns.

# Implementation plan
1. **Inspect current task assignment flow**
   - Find where tasks are created and where `assigned_agent_id` is validated.
   - Find whether reassignment/update uses the same path or a separate command/handler.
   - Find the canonical representation of agent status (`active`, `paused`, `restricted`, `archived`).

2. **Identify the best enforcement point**
   - Prefer a single centralized guard in the application layer used by all task assignment paths.
   - If the domain model already contains assignment invariants, add a domain guard there and call it from handlers.
   - Avoid duplicating status checks in multiple controllers/pages if a shared command validation path exists.

3. **Implement archived-agent assignment restriction**
   - When a task is being assigned to an agent:
     - resolve the agent within the current `company_id`
     - if not found, return the existing not-found/forbidden behavior
     - if found and status is `archived`, reject the assignment
   - Use a clear error message such as:
     - `Archived agents cannot be assigned new tasks.`
   - If the codebase already has a standard validation/error code pattern, use that instead of ad hoc exceptions.

4. **Cover both create and update/reassign scenarios**
   - New task creation with an archived assigned agent must fail.
   - Reassigning an existing task to an archived agent must fail.
   - Updating unrelated task fields should remain allowed if assignment is unchanged, unless current architecture always revalidates assignment; in that case ensure behavior remains consistent and intentional.

5. **Keep tenant isolation intact**
   - Agent lookup must remain company-scoped.
   - Do not allow cross-tenant agent existence leakage through error details.

6. **Optionally improve UI affordance if already straightforward**
   - If the web task assignment form already loads assignable agents, filter out archived agents or disable them in the selector.
   - Backend validation remains mandatory even if UI is updated.

7. **Add tests**
   - Add/extend unit or application tests for:
     - creating a task assigned to an active agent succeeds
     - creating a task assigned to an archived agent fails
     - reassigning a task to an archived agent fails
     - tenant-scoped lookup still behaves correctly
   - Reuse existing test fixtures/builders.

8. **Keep implementation minimal and consistent**
   - No schema changes unless the current model is missing status support.
   - No broad refactors unless needed to centralize an obviously duplicated validation rule.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run those specifically as well.

4. Manually verify behavior through the relevant API/application path:
   - create a task with an active agent → succeeds
   - create a task with an archived agent → fails with expected validation/business error
   - reassign an existing task to an archived agent → fails
   - assign to a valid agent in another tenant → still blocked by existing tenant-scoped behavior

5. If web UI was updated:
   - confirm archived agents are not offered for new assignment or are visibly disabled
   - confirm backend still rejects crafted requests

# Risks and follow-ups
- **Risk: duplicated assignment logic**
  - Task creation and reassignment may be implemented in separate handlers, making it easy to miss one path.
  - Mitigation: search for all writes to `assigned_agent_id` and centralize validation where practical.

- **Risk: inconsistent status semantics**
  - The codebase may already reject `paused` and/or `restricted` agents in some places but not others.
  - Mitigation: preserve existing behavior and avoid accidental policy expansion unless clearly intended.

- **Risk: UI-only filtering without backend enforcement**
  - This would be insufficient and bypassable.
  - Mitigation: backend validation is required; UI changes are optional enhancement only.

- **Risk: cross-tenant information leakage**
  - Returning different errors for “agent exists but belongs to another tenant” vs “agent not found” may leak data.
  - Mitigation: follow existing tenant-safe not-found/forbidden conventions.

Follow-ups to note in code comments or task notes if relevant:
- Consider centralizing **assignable agent eligibility** rules for `active/paused/restricted/archived` into a reusable policy/service if multiple modules need it.
- Consider extending the same eligibility rule to workflow auto-assignment and orchestration-generated subtasks if those paths are separate from manual task commands.