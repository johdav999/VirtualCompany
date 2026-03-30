# Goal
Implement backlog task **TASK-8.4.1** for story **ST-204 Agent roster and profile views** by adding the **web agent roster list** that displays, at minimum, each agent’s:

- name
- role
- department
- status
- autonomy level
- workload/health summary

The implementation should fit the existing **.NET modular monolith + Blazor Web App** architecture, respect **multi-tenant scoping**, and be designed so the roster can later support the full ST-204 experience (filters, profile drill-in, role-based field/action restrictions, richer health analytics).

# Scope
In scope for this task:

- Add or extend an **application query** for tenant-scoped agent roster data.
- Return a roster DTO/view model containing:
  - agent id
  - display name
  - role name
  - department
  - status
  - autonomy level
  - workload/health summary
  - optional profile/detail route target if the app uses one
- Derive an initial **workload/health summary** from currently available data, consistent with story notes:
  - task state counts and/or open task count
  - last activity if available
  - conservative fallback when activity/task data is missing
- Add or update the **Blazor web roster page/component** to render the list.
- Ensure all data access is **company/tenant scoped**.
- Keep implementation SSR-first and avoid unnecessary client-side interactivity.
- Add tests for query logic and any summary derivation logic that is introduced.

Out of scope unless already trivial in the existing codebase:

- Full agent detail/profile page
- Advanced filtering UI
- Editing agent settings from the roster
- New analytics pipelines
- Mobile changes
- Broad redesign of navigation/layout
- New persistence schema unless absolutely required

# Files to touch
Touch only the files needed for this task. Likely areas:

- `src/VirtualCompany.Application/...`
  - agent management queries
  - roster DTO/view model
  - query handler/service
- `src/VirtualCompany.Domain/...`
  - only if a small domain enum/value object/helper is needed for status/summary semantics
- `src/VirtualCompany.Infrastructure/...`
  - query/repository implementation
  - EF projections or SQL query wiring
- `src/VirtualCompany.Web/...`
  - Blazor page/component for agent roster
  - any page model/view model mapping
  - navigation link if the roster page exists but is not wired
- tests in the relevant test project(s)
  - application query tests
  - web/component tests if the solution already uses them

Before coding, inspect the repo for existing equivalents of:

- agent list page
- agent query handlers
- tenant context abstraction
- task repository/query services
- authorization helpers for web pages
- shared DTOs for agents

Prefer extending existing patterns over introducing new ones.

# Implementation plan
1. **Inspect current agent management and web structure**
   - Find existing modules/namespaces for:
     - Agent Management
     - task queries
     - tenant/company context
     - Blazor pages for agents
   - Reuse established CQRS-lite patterns, MediatR-style handlers, repository abstractions, and DTO conventions if present.

2. **Define the roster query contract**
   - Add a tenant-scoped application query such as “GetAgentRoster” if one does not already exist.
   - Query result should include only the fields needed by this task:
     - `Id`
     - `DisplayName`
     - `RoleName`
     - `Department`
     - `Status`
     - `AutonomyLevel`
     - `WorkloadHealthSummary`
     - optionally `LastActivityAt`, `OpenTaskCount`, or `ProfileUrl` if useful internally
   - Keep the DTO read-focused and separate from edit/profile models.

3. **Implement workload/health summary derivation**
   - Use currently available persisted data only.
   - Preferred derivation order:
     1. task counts by status for the agent
     2. recent activity timestamp if available from tasks/messages/audit records already in the model
     3. fallback summary when no activity exists
   - Keep the first version simple and deterministic. Example patterns:
     - “Idle · no open tasks”
     - “Healthy · 3 open tasks”
     - “Busy · 8 open tasks”
     - “Attention needed · 2 blocked, 1 awaiting approval”
     - “Unknown · no recent activity”
   - Do not invent unsupported metrics.
   - Encapsulate summary formatting in one place so it can be upgraded later.

4. **Implement infrastructure/query projection**
   - Build an efficient tenant-scoped query from `agents` and related task/activity sources.
   - Ensure:
     - `company_id` filtering is mandatory
     - archived/restricted/etc. agents are still shown if roster requirements expect all agents
     - N+1 queries are avoided
   - Prefer a single projection query or a small bounded number of queries.
   - If task aggregation is needed, group by `assigned_agent_id` and compute:
     - open/new/in-progress counts
     - blocked count
     - awaiting approval count
     - optional latest task update timestamp
   - If there is an existing read model/query service, extend it rather than bypassing it.

5. **Wire the query into the web roster page**
   - Update the Blazor page/component that represents the agent roster, or create it if missing.
   - Render a table or list with columns:
     - Name
     - Role
     - Department
     - Status
     - Autonomy
     - Workload/Health
   - Keep SSR-friendly markup and accessible semantics.
   - If profile navigation already exists or is easy to infer, make the agent name link to the detail page route.
   - Handle empty state gracefully:
     - no agents hired yet
     - no task/activity data yet

6. **Respect authorization and field visibility patterns**
   - Follow existing human-role authorization patterns.
   - For this task, do not expose restricted management actions from the roster unless already authorized.
   - If the app already has role-based UI helpers, use them.
   - At minimum, ensure unauthorized users cannot query another company’s roster.

7. **Add tests**
   - Application/query tests:
     - returns only agents for current company
     - includes required fields
     - computes workload/health summary correctly for representative task states
     - handles agents with no tasks/activity
   - If practical in current test setup:
     - web/component rendering test for expected columns and empty state
   - Avoid brittle snapshot tests unless the repo already uses them.

8. **Keep implementation incremental and clean**
   - Do not overbuild future ST-204 requirements.
   - Leave clear extension points for:
     - department/status filters
     - richer profile page
     - role-based action buttons
     - analytics-backed health scoring

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Navigate to the agent roster page.
   - Confirm the roster lists agents with:
     - name
     - role
     - department
     - status
     - autonomy level
     - workload/health summary
   - Confirm empty state is sensible when no agents exist.
   - Confirm summaries behave correctly for:
     - no tasks
     - active tasks
     - blocked tasks
     - awaiting approval tasks, if such data exists

4. Multi-tenant verification:
   - Confirm roster data is scoped to the active company only.
   - Confirm cross-tenant access is not possible through route/query manipulation.

5. Regression check:
   - Ensure existing agent management pages still build and render.
   - Ensure no unnecessary client-side interactivity was introduced.

# Risks and follow-ups
- **Risk: no reliable “last activity” source exists yet**
  - Mitigation: derive summary primarily from task state counts and use a neutral fallback.
- **Risk: task aggregation may become expensive**
  - Mitigation: use grouped projections and keep the query read-optimized; consider a dedicated read model later if needed.
- **Risk: roster/profile route conventions may already exist**
  - Mitigation: inspect and align with current navigation instead of creating parallel patterns.
- **Risk: acceptance criteria for ST-204 are broader than this task**
  - Mitigation: keep this task focused on the roster list only, but structure DTOs/components so filters and profile drill-in can be added next.

Suggested follow-ups after this task:
- add department/status filters
- implement agent detail/profile page
- add role-based hiding/disabling of restricted fields/actions
- replace heuristic workload/health summary with analytics-backed health indicators
- add recent activity/audit snippets to the profile view