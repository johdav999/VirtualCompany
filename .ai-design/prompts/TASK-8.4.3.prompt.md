# Goal
Implement backlog task **TASK-8.4.3 — Users can filter roster by department and status** for story **ST-204 Agent roster and profile views** in the existing **.NET / Blazor Web App** solution.

Deliver a tenant-safe roster filtering experience that allows users to narrow the agent roster by:
- **Department**
- **Status**

The implementation should fit the current architecture:
- Blazor Web App frontend
- ASP.NET Core application/backend
- PostgreSQL-backed tenant-scoped data access
- CQRS-lite query flow
- Role-aware UI behavior where applicable

Because no task-specific acceptance criteria were provided beyond the story, treat the story criteria as the source of truth for this task, especially:
- “Users can filter roster by department and status.”
- Preserve tenant isolation and existing role restrictions.

# Scope
In scope:
- Add or extend roster query/filter models to support filtering by `department` and `status`
- Update backend query handling/repository access so filters are applied server-side
- Update the web roster UI to expose department and status filters
- Ensure filters are tenant-scoped and do not leak cross-company data
- Preserve existing roster listing behavior when no filters are selected
- Support combinations:
  - no filters
  - department only
  - status only
  - department + status
- Keep implementation aligned with Blazor SSR-first guidance, adding only minimal interactivity if needed

Out of scope:
- New agent profile features
- New authorization model changes
- Mobile app changes
- Advanced search, sorting, pagination redesign, or analytics
- Adding new agent statuses beyond the existing domain values
- Large UI redesign of the roster page

# Files to touch
Inspect the solution first and then update the actual files that own roster querying and rendering. Likely areas include:

- `src/VirtualCompany.Web/**`
  - Roster page/component
  - Any view models or page models used for agent roster display
  - Shared UI components for filters/forms if applicable

- `src/VirtualCompany.Application/**`
  - Agent roster query contract/DTO
  - Query handler/service for listing agents
  - Any request models for filtering

- `src/VirtualCompany.Infrastructure/**`
  - EF Core or repository query implementation for agents
  - Tenant-scoped data access logic

- `src/VirtualCompany.Domain/**`
  - Only if needed for existing status constants/enums/value objects
  - Do not introduce domain churn unless necessary

Potential file patterns to search for:
- `*Roster*`
- `*AgentList*`
- `*GetAgents*`
- `*AgentsQuery*`
- `*AgentSummary*`
- `*AgentStatus*`
- `Pages/Agents/*`
- `Components/Agents/*`

If there is already a roster query and page, prefer extending those rather than creating parallel paths.

# Implementation plan
1. **Discover the current roster flow**
   - Find the existing agent roster page in `VirtualCompany.Web`.
   - Trace how the page gets data:
     - direct application query
     - API endpoint
     - mediator/CQRS request
     - repository/DbContext query
   - Identify the current roster DTO fields and whether department/status are already present in the returned data.

2. **Confirm existing domain/status representations**
   - Locate how agent status is modeled:
     - enum
     - string constants
     - plain string persisted in DB
   - Reuse the existing allowed values from the `agents.status` model described in the architecture (`active`, `paused`, `restricted`, `archived`) unless the codebase already uses a different canonical representation.
   - Avoid introducing duplicate status definitions.

3. **Extend the roster query contract**
   - Add optional filter inputs to the roster query/request model:
     - `Department` or `DepartmentFilter`
     - `Status` or `StatusFilter`
   - Keep them nullable/optional so the default roster remains unfiltered.
   - If the codebase uses a dedicated filter object, extend that instead of adding loose parameters.

4. **Apply filters in the application/infrastructure query path**
   - Update the query handler/repository so filtering is performed in the database query, not in-memory after loading all agents.
   - Ensure the query remains tenant-scoped first, then applies optional filters.
   - Filtering rules:
     - If department is provided, match agents in that department
     - If status is provided, match agents in that status
     - If both are provided, apply both
   - Be consistent with existing string comparison conventions in the codebase:
     - if normalized values are already used, follow that
     - avoid ad hoc case-insensitive logic unless needed by existing patterns

5. **Provide filter option data to the UI**
   - For **status**, use a stable predefined list from the existing domain/app model.
   - For **department**, prefer one of these approaches based on existing architecture:
     - derive distinct departments from the current tenant’s agents via query
     - or use an existing department catalog if one already exists
   - Do not hardcode a new department taxonomy if the system already stores free-form department values.

6. **Update the Blazor roster UI**
   - Add filter controls to the roster page:
     - department selector
     - status selector
   - Include an “All”/empty option for each filter.
   - Bind the selected values into the roster query flow.
   - Keep the UX simple and SSR-friendly:
     - query-string backed filters are preferred if the page already uses route/query parameters
     - otherwise use the project’s established page interaction pattern
   - Ensure the roster refreshes correctly when filters change.
   - Preserve existing role-based hiding/disabling behavior on roster actions/fields.

7. **Handle empty and combined states**
   - If filters return no agents, show a clear empty state such as “No agents match the selected filters.”
   - Ensure clearing filters restores the full roster.
   - If one filter is selected and the other is empty, results should still work correctly.

8. **Keep tenant isolation intact**
   - Verify all filter option generation and roster results are scoped by current company/tenant.
   - Do not expose departments or agents from other tenants.
   - If there is a shared query helper, ensure `company_id` scoping remains mandatory.

9. **Add or update tests**
   - Add application/infrastructure tests for roster filtering behavior.
   - Prefer tests that verify:
     - unfiltered returns all tenant agents
     - department filter narrows correctly
     - status filter narrows correctly
     - combined filters narrow correctly
     - other-tenant agents are never returned
   - Add UI/component tests only if the repo already has a pattern for them; otherwise prioritize query-level coverage.

10. **Keep implementation minimal and consistent**
   - Do not refactor unrelated roster/profile code.
   - Do not introduce new abstractions unless the existing code clearly requires them.
   - Follow existing naming, mediator, DTO, and page conventions in the repository.

# Validation steps
1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Manual verification in the web app**
   - Open the agent roster page for a tenant with multiple agents across multiple departments and statuses.
   - Verify:
     - default page shows all tenant agents
     - selecting a department filters correctly
     - selecting a status filters correctly
     - selecting both filters filters correctly
     - clearing filters restores all agents
     - empty result state is shown when no agents match

3. **Tenant isolation verification**
   - If test fixtures or local data support multiple companies, confirm:
     - roster only shows current tenant’s agents
     - department options are derived only from current tenant data
     - filtering cannot reveal another tenant’s agents

4. **Regression checks**
   - Confirm roster still displays expected columns/details already implemented for ST-204
   - Confirm profile navigation from roster still works
   - Confirm restricted fields/actions remain hidden or disabled per existing role logic

5. **Code quality checks**
   - Ensure filtering is applied in the query layer/database path, not after materializing all records
   - Ensure no new magic strings are duplicated if status constants/enums already exist
   - Ensure nullable filters do not break existing callers

# Risks and follow-ups
- **Risk: status representation mismatch**
  - The DB architecture shows string statuses, but the code may use enums or constants. Reconcile with existing implementation rather than inventing a new mapping.

- **Risk: department values may be free-form**
  - If departments are not normalized, filter options may need to be derived from distinct tenant agent values. Be careful with null/empty departments and display normalization.

- **Risk: roster page interaction style may vary**
  - The app may use SSR pages, components, mediator calls, or API-backed fetches. Follow the existing pattern instead of forcing a new one.

- **Risk: missing test infrastructure for UI**
  - If UI tests are not established, focus on application/infrastructure tests plus manual verification.

Follow-up suggestions after this task:
- Add query-string persistence for shareable filtered roster URLs if not already present
- Add sorting and pagination if roster size grows
- Normalize department taxonomy later if product requirements demand consistent reporting
- Add richer roster filters later (autonomy level, workload/health, role) under future backlog items