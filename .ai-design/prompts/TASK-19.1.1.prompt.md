# Goal
Implement backlog task **TASK-19.1.1 — Create finance sandbox admin page route and navigation entry** for story **US-19.1 ST-FUI-301 — Finance sandbox administration page**.

Deliver a Blazor web admin page in the finance area that:
- adds a dedicated **finance sandbox admin route**
- adds a **navigation entry** from the finance area
- renders these sections on the page:
  - dataset generation
  - anomaly injection
  - simulation controls
  - tool execution visibility
  - domain events
- enforces **role-based access** so unauthorized users see an **access denied** state and **no admin data is loaded**
- ensures **each section loads independently** and supports **loading, empty, and error states**

Use the existing project architecture and conventions. Prefer minimal, composable changes that fit the current Blazor Web App structure.

# Scope
In scope:
- Web route/page creation in `src/VirtualCompany.Web`
- Finance-area navigation update to expose the new page
- Role/authorization gating for admin/tester access
- Page composition for the five required sections
- Independent data-loading model per section
- Loading/empty/error UI states per section
- Preventing admin data fetches for unauthorized users
- Basic supporting view models/services if needed

Out of scope unless required by existing patterns:
- Full backend implementation of sandbox admin operations
- Real mutation workflows for dataset generation/anomaly injection/simulation execution
- New database schema or migrations
- Mobile app changes
- Broad redesign of finance navigation or authorization framework
- Deep styling refactors unrelated to this page

If backend endpoints/query handlers for these sections do not yet exist, create the smallest safe abstraction needed for the page to compile and render, using placeholders or stubbed query services consistent with current architecture. Do not invent large new subsystems.

# Files to touch
Inspect the repo first and update the exact files that match existing conventions. Likely areas include:

- `src/VirtualCompany.Web/**`
  - finance page route component/page
  - finance navigation/menu component
  - shared authorization/access denied UI if already present
  - page-specific view models/components for section cards/panels
  - section query/data services or adapters
- Potentially:
  - `src/VirtualCompany.Application/**` for query contracts/DTOs if the web app consumes application-layer queries
  - `src/VirtualCompany.Api/**` only if the web app requires API endpoints and the solution already uses API-backed page data
  - `tests/**` for route/nav/authorization/component tests if test coverage exists for web UI or application queries

Before editing, identify:
- where finance-area routes live
- how navigation entries are declared
- how roles are represented
- how access denied states are rendered
- how async loading states are typically modeled in the web app

# Implementation plan
1. **Discover existing patterns**
   - Inspect `src/VirtualCompany.Web` for:
     - finance area pages/components
     - nav menu/sidebar definitions
     - role-based rendering/authorization usage
     - access denied components/pages
     - existing dashboard/admin pages with multiple independently loading sections
   - Inspect application/API layers only if needed to understand current data flow.

2. **Add the finance sandbox admin route**
   - Create a dedicated finance sandbox admin page under the finance area using the project’s existing routing conventions.
   - Use a clear route name aligned with the task, e.g. finance sandbox admin semantics, but match existing URL naming patterns in the repo.
   - Set page title/heading appropriately.

3. **Add finance navigation entry**
   - Update the finance area navigation so users can reach the new route.
   - Place the entry in the most logical admin/sandbox location within finance navigation.
   - If navigation visibility is role-aware in the current app, hide or disable the entry for unauthorized users according to existing conventions. If not, route-level protection is still required.

4. **Implement authorization gating**
   - Restrict access to authorized **admin/tester** roles using the app’s current authorization model.
   - Do not hardcode a brand-new auth framework if one already exists.
   - On unauthorized access:
     - render an access denied state
     - do not trigger section data loading
   - If the app uses `AuthorizeView`, policies, or role checks, follow that pattern consistently.
   - If “tester” is not yet a first-class role constant, add the smallest safe shared constant/enum update needed without breaking existing role handling.

5. **Build the page shell with five sections**
   - Render distinct sections for:
     - dataset generation
     - anomaly injection
     - simulation controls
     - tool execution visibility
     - domain events
   - Each section should be visually and structurally separate, ideally as reusable panels/cards/components if that matches current style.
   - Keep labels user-friendly and aligned with acceptance criteria.

6. **Implement independent section loading**
   - Each section must load its backing data independently.
   - Do not use one monolithic page load that blocks all sections.
   - Preferred approaches:
     - separate child components, each with its own async load lifecycle
     - or separate per-section state containers invoked independently
   - Each section must support:
     - loading state
     - empty state
     - error state
     - success/content state
   - Failures in one section must not prevent other sections from loading/rendering.

7. **Add minimal data contracts/services**
   - For each section, wire a backing data source using existing architecture:
     - application query service
     - API client
     - or in-process service used by the web app
   - If real data is unavailable, return safe placeholder DTOs through a clearly named service abstraction so the UI behavior is complete and future work can replace internals.
   - Keep contracts small and section-specific where practical.

8. **Prevent unauthorized data access**
   - Ensure no admin data requests are made when the user lacks the required role.
   - This must be true both in UI flow and in any backing endpoint/query if touched.
   - If API/application handlers are added or updated, enforce authorization there too, not only in the UI.

9. **Polish UX states**
   - Add concise copy for:
     - access denied
     - loading
     - empty
     - error
   - Keep the page useful even when all sections are empty.
   - Avoid fake interactivity beyond what the task requires.

10. **Add/adjust tests where feasible**
   - Add tests consistent with the repo’s current testing style.
   - Prioritize:
     - route availability
     - navigation entry presence
     - unauthorized access denied behavior
     - independent section state behavior if component tests exist
   - If UI tests are not present in the repo, add focused application/service tests for any new query services and keep UI logic simple.

11. **Keep implementation incremental**
   - Do not over-engineer.
   - Prefer small reusable components and DTOs.
   - Leave clear TODOs only where future backend integration is genuinely pending.

# Validation steps
1. Inspect and restore/build:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. Manually verify in the web app:
   - finance area shows a navigation entry for the sandbox admin page
   - clicking the entry opens the dedicated route
   - authorized admin/tester user sees the page shell with all five sections
   - each section shows a loading state before data resolves
   - each section can independently show empty/error/content without blocking others
   - unauthorized user sees access denied
   - unauthorized user does not trigger admin data loading
4. If applicable, verify direct URL access:
   - authorized user can access the route
   - unauthorized user is denied even when navigating directly
5. Confirm no unrelated pages/regressions in finance navigation.

# Risks and follow-ups
- **Role ambiguity:** “admin/tester” may not map cleanly to existing role names. Reuse current role constants/policies where possible and document any assumption.
- **Missing backend support:** The page may need placeholder section services if real sandbox data sources are not implemented yet. Keep these abstractions minimal and clearly replaceable.
- **Authorization duplication:** UI gating alone is insufficient if new endpoints/queries are introduced. Enforce authorization at the backend/application boundary too.
- **Navigation conventions:** Finance navigation may be centralized or componentized; update only the canonical source to avoid duplicate entries.
- **Testing limitations:** If the repo lacks Blazor component/UI tests, avoid introducing a heavy new test framework just for this task; add the smallest meaningful coverage possible.
- **Follow-up work likely needed:** actual sandbox actions, richer telemetry, real domain event feeds, and operational controls can be implemented in later tasks once the route and shell are in place.