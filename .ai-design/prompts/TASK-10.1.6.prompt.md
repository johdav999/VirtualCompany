# Goal
Implement backlog task **TASK-10.1.6 — Assignment must reject paused/archived agents** for story **ST-401 Task lifecycle and assignment**.

Ensure task assignment logic rejects agents whose status is not eligible for assignment, specifically:
- `paused`
- `archived`

This should apply anywhere a task can be created or reassigned with an `assigned_agent_id`, while preserving tenant isolation and existing CQRS-lite boundaries.

# Scope
In scope:
- Identify the task create/update/reassign command flow(s) in the .NET backend.
- Add domain/application validation so tasks cannot be assigned to paused or archived agents.
- Enforce validation server-side, not just in UI.
- Return a clear validation/business error when assignment is rejected.
- Add or update automated tests covering allowed and rejected assignment cases.
- If applicable, update query/UI affordances so paused/archived agents are not offered as assignable options, but only as a secondary improvement if already straightforward and low-risk.

Out of scope:
- Broader agent lifecycle redesign.
- Changes to restricted-agent behavior unless the current codebase already defines assignment rules for `restricted`.
- New acceptance criteria beyond the backlog note.
- Mobile-specific work unless assignment UI is shared and trivial to update.

# Files to touch
Inspect and update only the files needed after confirming actual implementation locations. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Agent status enum/value object/constants
  - Task aggregate/entity assignment rules
  - Domain exceptions or business rule helpers

- `src/VirtualCompany.Application/**`
  - Task create command/handler
  - Task update/reassign command/handler
  - Validation layer (FluentValidation or equivalent)
  - Agent lookup/query service used during assignment
  - Application error mapping

- `src/VirtualCompany.Infrastructure/**`
  - Repository/query implementations if assignment validation requires agent status retrieval
  - EF Core configurations only if needed

- `src/VirtualCompany.Api/**`
  - Endpoint/controller error translation only if current behavior needs adjustment

- `src/VirtualCompany.Web/**`
  - Task create/edit UI assignable-agent picker, only if easy and already backed by an assignable-agent query

- Tests:
  - `tests/**` or corresponding test projects for Domain/Application/API layers
  - Add focused tests for create and reassignment scenarios

Do not invent new layers or large abstractions if the existing codebase already has a clear pattern.

# Implementation plan
1. **Discover current assignment flow**
   - Find all code paths where `assigned_agent_id` is set or changed.
   - Confirm whether assignment happens:
     - only on task creation,
     - on explicit reassignment,
     - during workflow/subtask creation,
     - via orchestration/internal services.
   - Identify the current source of truth for agent status and tenant-scoped agent lookup.

2. **Confirm status model**
   - Locate the agent status representation.
   - Verify exact values used in code for:
     - `active`
     - `paused`
     - `restricted`
     - `archived`
   - Reuse existing enum/constants rather than string literals.

3. **Implement server-side assignment eligibility rule**
   - Add a single authoritative rule in the most central place available:
     - Prefer domain rule if the task aggregate/entity already enforces assignment invariants and has access to agent eligibility input.
     - Otherwise enforce in the application command handler/service before persisting assignment.
   - Minimum required behavior:
     - reject assignment when agent status is `paused`
     - reject assignment when agent status is `archived`
   - Keep tenant scoping intact when loading the agent.
   - If agent is not found in the tenant, preserve existing not-found/forbidden semantics.

4. **Apply rule to all relevant commands**
   - Ensure the rule is used consistently for:
     - create task with assigned agent
     - reassign existing task
     - any internal subtask creation path that accepts an assigned agent
   - Avoid duplicating logic; extract a small reusable helper/service if multiple handlers need it.

5. **Return clear business error**
   - Use the project’s existing error/result/exception pattern.
   - Error message should be explicit and safe, e.g.:
     - `Agent cannot be assigned because it is paused.`
     - `Agent cannot be assigned because it is archived.`
   - If the codebase prefers a generic message, use one stable business code plus status-specific detail where appropriate.

6. **Optional low-risk UX alignment**
   - If there is already a query for assignable agents, filter out paused/archived agents there.
   - If the UI currently lists all agents, consider disabling or labeling paused/archived agents instead of a larger UI refactor.
   - Do not rely on UI filtering as the primary enforcement.

7. **Add tests**
   - Cover at least:
     - task creation succeeds with active agent
     - task creation fails with paused agent
     - task creation fails with archived agent
     - reassignment fails with paused agent
     - reassignment fails with archived agent
     - tenant-scoped lookup still prevents cross-tenant assignment
   - If `restricted` behavior is already defined elsewhere, add a regression test to preserve current behavior.

8. **Keep changes minimal and consistent**
   - Follow existing naming, architecture, and error-handling conventions.
   - Do not introduce speculative support for future statuses unless required by current patterns.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the relevant subset first, then full suite.

4. Manually verify behavior through API or handler tests:
   - Create a task assigned to an active agent → succeeds.
   - Create a task assigned to a paused agent → rejected with expected business/validation error.
   - Create a task assigned to an archived agent → rejected with expected business/validation error.
   - Reassign an existing task to a paused/archived agent → rejected.
   - Attempt assignment using an agent from another company → existing tenant-safe failure behavior remains unchanged.

5. If UI was updated:
   - Verify paused/archived agents are not selectable or are clearly disabled.
   - Verify server still rejects crafted requests that bypass UI.

# Risks and follow-ups
- **Multiple assignment paths:** Task assignment may occur in more than one handler/service; missing one would leave inconsistent enforcement.
- **Rule placement:** If implemented only in validators or UI, internal/background flows may bypass it. Prefer central business enforcement.
- **Status ambiguity:** The backlog explicitly mentions paused/archived only. Do not accidentally change `restricted` behavior unless the codebase already defines it.
- **Error contract changes:** If API clients depend on current error shapes, keep response formatting consistent.
- **Workflow-generated tasks:** Follow up if workflow/subtask creation uses a separate path not covered by standard task commands.
- **Future enhancement:** Consider introducing a formal “assignable agent” policy/query so roster, UI pickers, orchestration, and task APIs all share one eligibility definition.