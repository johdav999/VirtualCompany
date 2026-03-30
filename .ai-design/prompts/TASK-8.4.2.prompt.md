# Goal
Implement backlog task **TASK-8.4.2** for **ST-204 Agent roster and profile views** by adding or completing the **agent detail view** so it clearly shows:

- agent identity
- objectives
- permissions
- thresholds
- recent activity

The implementation must fit the existing **.NET modular monolith** and **Blazor Web App** architecture, remain **tenant-scoped**, and respect **human-role-based visibility** for restricted fields/actions.

# Scope
In scope for this task:

- Add or complete a tenant-scoped **agent detail query** in the application/backend layer.
- Render an **agent detail page/view** in the Blazor web app.
- Show the following sections on the detail view:
  - **Identity**: display name, role, department, seniority, status, autonomy level, avatar if available, role brief summary if already present.
  - **Objectives**: configured objectives from `objectives_json`.
  - **Permissions**: tool permissions and data scopes from `tool_permissions_json` and optionally `data_scopes_json` if already available in the profile contract.
  - **Thresholds**: approval/autonomy thresholds from `approval_thresholds_json`.
  - **Recent activity**: recent tasks and/or recent audit/tool activity, depending on what is already available and easiest to query safely.
- Ensure the page is **company/tenant isolated**.
- Ensure restricted fields/actions are **hidden or disabled** based on the current human user role/policies.
- Keep the UI **SSR-first** and simple; only add interactivity if already consistent with the current web patterns.

Out of scope unless required by existing code patterns:

- Editing the agent profile.
- New analytics beyond a simple recent activity list.
- Mobile implementation.
- New orchestration behavior.
- Deep audit/explainability drilldowns beyond links/placeholders.
- Large schema redesigns.

If the codebase already has partial roster/profile support, extend it rather than duplicating patterns.

# Files to touch
Touch only the files needed by the existing architecture and project structure. Likely areas:

- **Web**
  - `src/VirtualCompany.Web/...` agent detail page/component
  - shared UI components for agent profile sections if appropriate
  - navigation/route registration if needed
- **Application**
  - query/handler for getting agent detail view model
  - DTO/view model for agent detail
  - authorization/visibility shaping logic if application-layer projection is used
- **Infrastructure**
  - repository/query implementation if application queries are backed here
  - EF Core projection/query composition if applicable
- **Domain**
  - only if existing domain types/value objects need small additions for safe projection/parsing
- **Shared**
  - shared contracts only if the solution already uses shared query/view contracts

Possible concrete targets based on common structure, but confirm before editing:

- `src/VirtualCompany.Web/Pages/Agents/...`
- `src/VirtualCompany.Web/Components/...`
- `src/VirtualCompany.Application/Agents/Queries/...`
- `src/VirtualCompany.Infrastructure/Persistence/...`
- `src/VirtualCompany.Domain/.../Agents/...`

Also inspect:
- existing roster page implementation
- existing tenant context resolution
- existing authorization helpers/policies
- existing task/audit query patterns

# Implementation plan
1. **Inspect current agent roster/profile implementation**
   - Find any existing ST-204 work for:
     - roster page
     - agent list DTOs
     - agent details route
     - authorization patterns
   - Reuse naming, folder structure, MediatR/CQRS conventions, and UI composition already present in the solution.

2. **Define the agent detail read model**
   - Create or extend an application-layer query DTO/view model with fields for:
     - `Id`
     - `DisplayName`
     - `RoleName`
     - `Department`
     - `AvatarUrl`
     - `Seniority`
     - `Status`
     - `AutonomyLevel`
     - `RoleBrief`
     - `Objectives`
     - `ToolPermissions`
     - `DataScopes` if available
     - `ApprovalThresholds`
     - `RecentActivity`
   - Keep JSON-backed config fields normalized into UI-friendly structures where possible.
   - Prefer defensive parsing of JSON/JSONB-backed fields; null/empty config should render gracefully.

3. **Implement tenant-scoped backend query**
   - Add a query/handler that:
     - resolves current company/tenant context
     - fetches the requested agent by `id`
     - enforces `company_id`
     - returns not found/forbidden-safe behavior for cross-tenant access
   - Include recent activity using the simplest reliable source already in the system:
     - preferred: recent `tasks` for the agent ordered by `updated_at`/`created_at`
     - optional: recent `audit_events` and/or `tool_executions` if already easy to join and display
   - Limit recent activity to a small number, e.g. 5–10 items.

4. **Apply role-based visibility shaping**
   - Use existing authorization/policy patterns to determine whether the current human user can view:
     - permissions
     - thresholds
     - any restricted actions
   - If the app already uses policy-based authorization in ASP.NET Core, follow that pattern.
   - Prefer shaping the returned view model or page rendering so restricted sections are hidden/disabled rather than leaking data to the client.
   - At minimum, ensure sensitive sections are not shown to unauthorized roles.

5. **Build the Blazor agent detail page**
   - Add or complete a page such as `/agents/{id}` using existing routing conventions.
   - Render clear sections:
     - **Identity**
     - **Objectives**
     - **Permissions**
     - **Thresholds**
     - **Recent activity**
   - Use simple cards/definition lists/tables consistent with current UI.
   - Handle empty states:
     - no objectives configured
     - no permissions configured
     - no thresholds configured
     - no recent activity yet
   - Handle missing avatar with fallback initials or placeholder if the app already has a pattern.

6. **Render recent activity in a useful but lightweight way**
   - Show a concise list with fields such as:
     - activity type
     - title/summary
     - status/outcome
     - timestamp
     - optional link to task/audit detail if routes already exist
   - If multiple activity sources are combined, normalize them into one simple display model.

7. **Preserve SSR-first behavior**
   - Avoid unnecessary client-side complexity.
   - Keep data loading server-side if that matches current Blazor Web App patterns.
   - Only add interactive enhancements if already standard in the project.

8. **Add tests where the solution already expects them**
   - Application/query tests for:
     - tenant isolation
     - not found for wrong tenant
     - correct projection of identity/objectives/permissions/thresholds
     - recent activity ordering/limit
   - If web/component tests exist, add minimal coverage for section rendering and empty states.
   - Do not introduce a new testing style if the repo does not already use it.

9. **Keep implementation incremental and non-breaking**
   - Do not redesign agent config storage.
   - Do not block on perfect JSON normalization; render readable structured output using existing config shape.
   - Favor pragmatic projection over broad refactors.

# Validation steps
1. **Build**
   - Run:
     - `dotnet build`

2. **Tests**
   - Run:
     - `dotnet test`

3. **Manual verification in web app**
   - Open the agent roster and navigate to an agent detail page.
   - Confirm the page shows:
     - identity
     - objectives
     - permissions
     - thresholds
     - recent activity
   - Confirm empty states render cleanly when config/activity is missing.
   - Confirm recent activity is tenant-scoped and relevant to the selected agent.

4. **Authorization verification**
   - Test with at least two human roles if available, e.g. admin/manager vs lower-privilege user.
   - Confirm restricted sections/controls are hidden or disabled appropriately.
   - Confirm cross-tenant agent IDs do not expose data.

5. **Data correctness checks**
   - Verify displayed values map correctly from:
     - `agents.objectives_json`
     - `agents.tool_permissions_json`
     - `agents.approval_thresholds_json`
     - recent `tasks` and/or other activity sources
   - Verify timestamps/statuses are sensible and ordered descending by recency.

6. **Regression check**
   - Confirm roster page still works.
   - Confirm no existing agent management routes or queries are broken.

# Risks and follow-ups
- **JSON config shape variability**: `objectives_json`, `tool_permissions_json`, and `approval_thresholds_json` may not have a fully standardized structure yet. Use tolerant parsing and readable fallback rendering.
- **Authorization ambiguity**: acceptance criteria mention restricted fields/actions by human role, but no story-specific detail defines exactly which fields are restricted. Follow existing policy conventions and document any assumptions in code comments or PR notes.
- **Recent activity source may be incomplete**: if audit/tool execution views are not yet mature, use recent tasks first as the baseline activity feed.
- **UI contract evolution**: this page is the anchor for future analytics, so keep the view model extensible without overengineering now.
- **Potential follow-up tasks**:
  - richer health/workload summary on detail page
  - links to audit/explainability drilldowns
  - editable profile sections
  - standardized typed config objects for agent JSON fields
  - reusable authorization matrix for field-level visibility