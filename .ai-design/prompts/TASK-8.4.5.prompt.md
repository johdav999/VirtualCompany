# Goal
Implement backlog task **TASK-8.4.5 — Blazor SSR first; add interactivity only where needed** for **ST-204 Agent roster and profile views**.

The coding agent should update the web experience so the **agent roster and agent profile pages are rendered with Blazor SSR by default**, and only use interactive Blazor features where there is a clear UX need. The result should align with the architecture and story intent: a web-first Blazor app, tenant-aware, role-aware, and optimized for maintainability and performance.

# Scope
Focus only on the **web UI and supporting application/query plumbing** needed to ensure the roster and profile views follow an **SSR-first** approach.

In scope:
- Agent roster page rendering via SSR.
- Agent profile/detail page rendering via SSR.
- Department/status filtering implemented in an SSR-friendly way first.
- Role-based hiding/disabling of restricted fields/actions in the rendered UI.
- Minimal supporting query/view model changes if needed to supply roster/profile data to SSR pages.
- Add interactivity only for isolated elements that truly benefit from it, and keep those boundaries explicit and small.

Out of scope unless already partially present and required to complete this task:
- Reworking broader agent domain behavior.
- New backend domain rules unrelated to roster/profile display.
- Full SPA-style client interactivity.
- Mobile app changes.
- New analytics beyond what is already needed for workload/health summary display.
- Large design-system refactors.

Implementation intent:
- Prefer **static SSR pages and standard form/query-string navigation**.
- If interactive components are necessary, use them only for narrow UX enhancements and document why.
- Preserve tenant scoping and authorization expectations from the broader architecture and backlog.

# Files to touch
Inspect and update only the files needed after confirming actual project structure. Likely areas include:

- `src/VirtualCompany.Web/**`
  - Agent roster page/component
  - Agent profile/detail page/component
  - Shared layout/navigation/components used by those pages
  - Blazor render mode configuration if currently forcing interactivity
  - Any route components or page models related to agents
- `src/VirtualCompany.Application/**`
  - Query handlers / DTOs / view models for roster and profile data
- `src/VirtualCompany.Api/**` or web host startup files if render mode/service registration needs adjustment
- Possibly:
  - authorization helpers/policies already used by the web app
  - existing agent management query contracts
  - tests covering web rendering, application queries, or authorization behavior

Before editing, identify:
- Where the Blazor Web App config sets render modes.
- Whether roster/profile pages are currently interactive by default.
- Whether filters currently depend on client-side event handling.
- Whether role-based UI gating is already available in the web layer.

# Implementation plan
1. **Inspect current Blazor render mode setup**
   - Find how the web app configures Blazor components and render modes.
   - Determine whether the app or the agent pages are currently using global interactivity such as `InteractiveServer`, `InteractiveAuto`, or similar.
   - Identify the smallest change needed so roster/profile pages are SSR-first without breaking unrelated pages.

2. **Locate the ST-204 agent roster and profile UI**
   - Find the current roster page and profile/detail page.
   - Review how data is loaded:
     - direct service injection in components
     - API calls from client-side interactive components
     - query-string or route parameter handling
   - Review how filtering is implemented today.

3. **Convert roster page to SSR-first**
   - Ensure the roster page renders server-side on initial request.
   - Prefer route/query-string driven filtering for:
     - department
     - status
   - Use standard GET form submission or link-based filtering rather than client event-driven filtering where possible.
   - Ensure the page can render fully from server-provided data on first load.
   - Keep workload/health summary display server-rendered.

4. **Convert agent profile page to SSR-first**
   - Ensure the profile page renders server-side using route parameter agent ID/slug as applicable.
   - Render identity, objectives, permissions, thresholds, and recent activity from server-side query results.
   - Avoid unnecessary interactive wrappers around the full page.

5. **Apply “interactivity only where needed”**
   - If there are small UX elements that genuinely need interactivity, isolate them into narrowly scoped interactive components.
   - Examples only if justified by existing UX:
     - collapsible detail panel
     - copy-to-clipboard button
     - lightweight tab persistence
   - Do not make the whole page interactive just to support a minor enhancement.
   - If no such need exists, keep the pages fully SSR.

6. **Preserve role-based UI restrictions**
   - Ensure restricted fields/actions are hidden or disabled based on the current human role.
   - Reuse existing authorization/policy helpers where available.
   - Do not rely solely on client-side hiding; server-rendered output should already reflect allowed visibility.

7. **Support tenant-aware, query-based data loading**
   - Confirm roster/profile queries remain tenant-scoped.
   - If needed, add or refine application-layer query DTOs/view models to support SSR rendering cleanly.
   - Keep CQRS-lite boundaries intact: queries for display, no domain mutations unless already required.

8. **Keep implementation simple and explicit**
   - Prefer plain Razor/Blazor SSR patterns over introducing JS or richer client state.
   - Use query-string parameters for filters so pages are linkable, refresh-safe, and SSR-friendly.
   - Keep component boundaries understandable for future analytics/profile expansion.

9. **Add or update tests where practical**
   - Add focused tests for any application query/view model logic introduced.
   - Add web/component tests if the solution already has a pattern for them.
   - At minimum, validate that filtering and role-based visibility logic are covered somewhere testable.

10. **Document assumptions in code comments only where necessary**
   - If an interactive island remains, add a concise comment explaining why SSR alone was insufficient.
   - Avoid noisy comments elsewhere.

# Validation steps
Run and verify the following after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification in the web app**
   - Open the agent roster page.
   - Confirm the page loads and renders correctly with SSR.
   - Confirm filtering by department works.
   - Confirm filtering by status works.
   - Confirm filters work via URL/query-string or standard GET form behavior.
   - Refresh the filtered page and verify state is preserved from the URL.
   - Open an agent profile page directly by URL and confirm it renders correctly server-side.
   - Verify identity, objectives, permissions, thresholds, and recent activity are shown as expected.
   - Verify restricted fields/actions are hidden or disabled for lower-privilege roles.
   - Verify authorized roles still see the intended controls.
   - If any interactive island remains, verify it works without making the whole page interactive.

4. **Regression checks**
   - Confirm no unrelated pages lost required interactivity.
   - Confirm tenant-scoped access still behaves correctly for roster/profile data.
   - Confirm unauthorized or cross-tenant access remains blocked by existing app behavior.

# Risks and follow-ups
- **Risk: global render mode coupling**
  - The app may currently assume interactive render modes broadly. Narrowing roster/profile to SSR may expose hidden dependencies on client-side lifecycle or injected browser-only services.

- **Risk: filter UX currently depends on client state**
  - Existing roster filters may be implemented with bound events and in-memory lists. Converting to SSR may require reshaping the page around query-string-driven requests.

- **Risk: authorization logic may be UI-fragmented**
  - Restricted field visibility may currently be scattered across components. Consolidate carefully without changing security semantics.

- **Risk: data loading may be API/client oriented**
  - If pages currently fetch via client-side API calls, SSR conversion may require introducing or reusing server-side query services in the web layer.

Follow-ups to note if not completed in this task:
- Add explicit component/integration tests for SSR-rendered roster/profile pages if the repo lacks current coverage.
- Review other ST-204 pages/components for accidental overuse of interactivity.
- Consider documenting a team convention: **default to SSR, justify interactive islands explicitly**.
- Future enhancements like richer profile analytics should preserve SSR-first rendering and only isolate truly interactive widgets.