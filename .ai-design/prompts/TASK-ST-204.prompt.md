# Goal
Implement **TASK-ST-204 — Agent roster and profile views** in the existing .NET solution, delivering a **Blazor Web App** experience for managers/admins to:
- view a tenant-scoped roster of agents
- filter by **department** and **status**
- inspect an agent profile with:
  - identity
  - objectives
  - permissions
  - thresholds
  - recent activity
  - autonomy level
  - workload/health summary
- hide or disable restricted fields/actions based on the signed-in human user’s role

This task should align with the architecture and backlog for **EP-2 / M2**, fit the modular monolith structure, and use **CQRS-lite** patterns with tenant-aware queries.

# Scope
In scope:
- Add backend query support for:
  - agent roster listing
  - roster filtering by department and status
  - agent profile/detail retrieval
  - lightweight recent activity summary for an agent
  - workload/health summary derived from task state and last activity
- Add/extend web UI pages/components for:
  - roster page
  - profile/detail page
- Enforce tenant scoping on all queries
- Enforce role-based visibility/edit affordances in the UI and/or application layer
- Keep implementation SSR-first, with only minimal interactivity if needed for filters
- Use existing domain entities where possible, especially `agents` and `tasks`

Out of scope:
- Full analytics beyond a simple health/workload summary
- Editing agent profiles beyond linking to existing edit flows if already present
- Mobile app changes
- New orchestration behavior
- Deep audit/explainability views beyond a recent activity summary
- New persistence model unless required for missing fields already implied by stories

Assumptions to honor:
- Health summary can initially be derived from:
  - task counts by status
  - last task/activity timestamp
  - agent status
- Recent activity can initially come from:
  - recent tasks assigned to the agent
  - optionally recent tool executions or audit events only if already easy to query
- Restricted fields/actions should be hidden or disabled based on human role, not agent permissions

# Files to touch
Inspect the solution first, then update only the relevant files. Expected areas:

- `src/VirtualCompany.Web/...`
  - roster page
  - agent detail/profile page
  - shared components for agent cards/table/filter UI
  - authorization-aware UI helpers if present
- `src/VirtualCompany.Application/...`
  - query contracts / handlers for roster and profile views
  - DTO/view models for roster rows, filters, profile details, recent activity, health summary
- `src/VirtualCompany.Domain/...`
  - only if small domain additions are required for enums/value objects already missing
- `src/VirtualCompany.Infrastructure/...`
  - EF Core query implementations / repositories / projections
  - tenant-scoped data access
- `src/VirtualCompany.Api/...`
  - only if the web app consumes API endpoints rather than direct application services
- `README.md`
  - only if there is an established feature documentation section worth updating

Likely concrete artifacts to add or modify:
- Agent roster query + response DTO
- Agent profile query + response DTO
- Task-derived summary query/projection
- Blazor page for `/agents`
- Blazor page for `/agents/{id}`
- Filter model for department/status
- Role-based UI gating logic

# Implementation plan
1. **Inspect the current solution structure before coding**
   - Identify:
     - how tenant context is resolved
     - how authorization/roles are represented
     - whether the web app calls application services directly or via API
     - whether agent management pages already exist from ST-201/ST-202
     - how tasks are stored and queried
   - Reuse existing patterns for:
     - MediatR/CQRS handlers if present
     - Result/error handling
     - authorization policies
     - page routing and layout

2. **Define the application-layer read models**
   Create query DTOs/view models for:
   - `AgentRosterItem`
     - `Id`
     - `DisplayName`
     - `RoleName`
     - `Department`
     - `Status`
     - `AutonomyLevel`
     - `OpenTaskCount` or task summary
     - `LastActivityAt`
     - `HealthSummary` / `HealthStatus`
   - `AgentRosterResponse`
     - collection of roster items
     - available filter values if useful
   - `GetAgentRosterQuery`
     - `CompanyId` or tenant context
     - optional `Department`
     - optional `Status`
   - `AgentProfileView`
     - identity fields
     - role brief
     - seniority
     - status
     - autonomy level
     - objectives
     - KPI summary if already available
     - tool permissions
     - data scopes
     - approval thresholds
     - escalation rules if easy to surface
     - working hours if already modeled
     - workload/health summary
     - recent activity list
     - permissions flags for UI actions/visibility
   - `GetAgentProfileQuery`
     - tenant-scoped agent id

3. **Implement tenant-scoped roster query**
   - Query `agents` filtered by current tenant/company
   - Support optional filters:
     - department
     - status
   - Join/aggregate from `tasks` to derive:
     - active/open workload counts
     - blocked/awaiting approval counts if useful
     - last activity timestamp from most recent assigned task update or agent update fallback
   - Derive a simple health summary, for example:
     - `Healthy` = active and no blocked overload and recent activity exists
     - `Attention` = paused/restricted, many blocked tasks, or stale activity
     - keep logic simple and deterministic
   - Ensure archived/restricted agents still appear if filters allow, since this is a management view

4. **Implement tenant-scoped profile query**
   - Load the target agent by `id` and `company_id`
   - Return not found if agent is outside tenant scope
   - Project profile fields from the `agents` table JSON/config fields
   - Add recent activity summary from recent tasks assigned to the agent:
     - title
     - status
     - updated/completed timestamp
     - maybe priority/type if already available
   - Add workload summary:
     - counts by task status
     - last activity timestamp
   - If there is an existing audit/task history abstraction, prefer reusing it over duplicating logic

5. **Add role-based visibility model**
   - Determine which human roles can see which sections/actions
   - At minimum:
     - managers/admins/owners can view roster and profile
     - restricted config/action affordances are hidden or disabled for lower roles
   - Add explicit UI flags in the profile response or compute them in the web layer, such as:
     - `CanViewPermissions`
     - `CanViewThresholds`
     - `CanEditAgent`
     - `CanPauseOrRestrictAgent`
   - Do not rely on UI hiding alone if sensitive data should be protected; enforce in the application layer where appropriate

6. **Build the Blazor roster page**
   - Route likely `/agents`
   - Render SSR-first
   - Include:
     - page title
     - filter controls for department and status
     - list/table of agents
   - Each row/card should show:
     - name
     - role
     - department
     - status
     - autonomy level
     - workload/health summary
     - link to profile
   - Handle empty states:
     - no agents at all
     - no agents matching filters
   - Keep interactivity minimal:
     - GET-based filter query string preferred if consistent with app patterns

7. **Build the Blazor agent profile page**
   - Route likely `/agents/{id}`
   - Show:
     - identity header
     - role/department/seniority/status/autonomy
     - objectives
     - permissions/scopes
     - thresholds
     - recent activity
     - workload/health summary
   - Hide or disable restricted sections/actions based on role
   - If edit functionality already exists from ST-202, add a conditional “Edit profile” action
   - Keep layout modular so this page can become the anchor for future analytics

8. **Use consistent formatting for JSON-backed config**
   - Many agent fields are JSONB-backed
   - Do not dump raw JSON unless no better option exists
   - Render readable summaries/lists for:
     - objectives
     - tool permissions
     - scopes
     - thresholds
   - If the current codebase already has typed config objects, use them
   - If not, add lightweight typed projections for display only

9. **Add authorization checks**
   - Ensure only authorized human roles can access roster/profile pages
   - If the app uses policy-based authorization, add or reuse a policy for agent management visibility
   - Ensure tenant mismatch returns forbidden/not found according to existing conventions

10. **Keep implementation incremental and low-risk**
   - Prefer read-only queries and UI composition
   - Avoid schema changes unless absolutely necessary
   - Reuse existing entities and task data for summaries
   - Keep health logic simple and documented in code comments

11. **Add tests**
   Add focused tests for:
   - roster query tenant isolation
   - roster filtering by department/status
   - profile query returns correct agent only within tenant
   - restricted role visibility behavior if testable at application/web level
   - health/workload summary derivation logic
   Prefer existing test patterns in the repo.

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Verify roster behavior manually:
   - navigate to the agent roster page
   - confirm agents are tenant-scoped
   - confirm displayed columns include:
     - name
     - role
     - department
     - status
     - autonomy level
     - workload/health summary
   - confirm filtering by department and status works
   - confirm empty states render cleanly

3. Verify profile behavior manually:
   - open an agent detail page from the roster
   - confirm it shows:
     - identity
     - objectives
     - permissions
     - thresholds
     - recent activity
   - confirm workload/health summary is present
   - confirm non-tenant agent ids are inaccessible

4. Verify role-based restrictions:
   - test with at least two human roles if possible, e.g. manager vs employee/admin
   - confirm restricted fields/actions are hidden or disabled appropriately
   - confirm unauthorized access follows existing app conventions

5. Verify summary derivation:
   - create or inspect agents with different task states
   - confirm workload/health changes sensibly based on:
     - active tasks
     - blocked tasks
     - last activity
     - agent status

6. If the web app uses query-string filters:
   - verify filters are bookmarkable/shareable
   - verify page reload preserves filter state

# Risks and follow-ups
- **Role ambiguity risk:** backlog says restricted fields/actions depend on human role, but exact role matrix is not fully specified. Reuse existing authorization rules and document any assumptions in code comments or PR notes.
- **Health summary ambiguity:** no strict acceptance criteria define health scoring. Keep it simple, deterministic, and derived from task state + last activity as noted in the story.
- **JSON config display risk:** agent config may currently be stored as flexible JSON without typed display models. Avoid exposing raw JSON if possible; add small typed display projections where needed.
- **Data availability risk:** recent activity may be sparse if tasks are not yet widely used in local/dev data. Fall back gracefully to agent `updated_at` and empty activity states.
- **UI duplication risk:** if ST-201/ST-202 already introduced agent pages/components, extend them rather than creating parallel views.
- **Authorization leakage risk:** do not rely solely on hidden UI for sensitive profile sections if those sections should not be returned to lower-privilege users.

Suggested follow-ups after this task:
- richer agent health analytics and trend indicators
- profile-linked audit trail and tool execution history
- direct chat launch from roster/profile
- profile edit/version history integration
- pagination/sorting/search for larger rosters