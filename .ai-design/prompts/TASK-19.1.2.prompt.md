# Goal
Implement `TASK-19.1.2` for `US-19.1 ST-FUI-301` by adding a finance sandbox administration route and a role-gated access guard for sandbox admin views in the Blazor web app, ensuring unauthorized users see an access denied state and no admin data is loaded.

# Scope
Include:
- A dedicated finance sandbox admin page reachable from finance area navigation.
- Role-gated access for authorized finance sandbox admin/tester users only.
- An access denied UI state for unauthorized users.
- Prevention of admin backing-data requests when access is denied.
- Page composition with these independently loading sections:
  - dataset generation
  - anomaly injection
  - simulation controls
  - tool execution visibility
  - domain events
- Per-section loading, empty, and error states.
- Tests covering authorization behavior and independent section loading behavior where practical.

Do not include:
- New backend domain capabilities for sandbox operations unless already stubbed or required only to support page rendering contracts.
- Broad RBAC redesign beyond what is needed for this route/page.
- Mobile app changes.
- Non-finance sandbox admin pages.

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance area navigation/menu component(s)
  - router/route registration if applicable
  - finance sandbox admin page/component
  - shared authorization/access denied components
  - section components for each admin area
  - page-level or service-level data access clients
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts for sandbox admin section responses if needed
- `src/VirtualCompany.Api/**`
  - finance sandbox admin endpoints or query endpoints if they do not already exist
  - authorization policy wiring if enforced server-side
- `src/VirtualCompany.Application/**`
  - query handlers/interfaces for section data if needed
- `src/VirtualCompany.Infrastructure/**`
  - implementations for any new query services if needed
- `tests/VirtualCompany.Api.Tests/**`
  - API authorization tests for sandbox admin endpoints
- Add web/UI tests if a test project already exists for Blazor components; otherwise add focused API tests and keep UI validation manual

Before editing, locate:
- existing finance navigation patterns
- existing role/policy authorization patterns
- existing access denied/forbidden UI patterns
- existing section/card/loading-state component patterns
- existing CQRS query and endpoint conventions

# Implementation plan
1. **Discover existing patterns**
   - Search for:
     - finance area pages and nav entries
     - `[Authorize]`, policy names, role checks, and membership-based authorization
     - existing “access denied”, “forbidden”, or unauthorized state components
     - dashboard/admin pages with independently loaded widgets/sections
     - API endpoint grouping conventions for feature areas
   - Reuse existing naming and architecture rather than inventing new patterns.

2. **Define the authorization rule**
   - Identify the current human role model from tenant membership/authorization code.
   - Implement a dedicated policy for finance sandbox admin access if one does not already exist.
   - The policy should allow only the intended authorized admin/tester roles for this feature.
   - Prefer policy-based authorization over ad hoc UI-only checks.
   - Ensure the same rule is enforced both:
     - in the web app for page rendering/access denied state
     - in the API for backing data endpoints

3. **Add the finance sandbox admin route**
   - Create a dedicated finance sandbox admin page in the finance area.
   - Add a navigation entry from the finance area navigation.
   - Ensure the nav item visibility follows the intended UX:
     - if the app convention is to hide unauthorized admin links, do so
     - if the app convention is to show and then deny on entry, follow that convention consistently
   - Use clear route naming aligned with existing finance routes.

4. **Implement guarded page behavior**
   - On page initialization, evaluate authorization before triggering any admin data loads.
   - If unauthorized:
     - render access denied state
     - do not invoke any section data requests
   - If authorized:
     - render the admin page shell and load each section independently

5. **Build the page sections**
   - Render these sections as separate components or clearly separated page modules:
     - dataset generation
     - anomaly injection
     - simulation controls
     - tool execution visibility
     - domain events
   - Each section must load its own backing data independently.
   - Each section must support:
     - loading state
     - empty state
     - error state
     - success/content state
   - Avoid one failed section blocking the others.

6. **Implement data contracts and endpoints**
   - If backend endpoints do not exist, add minimal read/query endpoints for each section.
   - Keep them tenant-scoped and authorization-protected.
   - Prefer one endpoint per section or equivalent independent query path so failures remain isolated.
   - Return stable DTOs with enough information to render loading/empty/error/content states.
   - Do not load sensitive admin data in unauthorized cases.

7. **Prevent unauthorized data loading**
   - Ensure the web page does not call section APIs when authorization fails.
   - Ensure direct API access is still blocked server-side with forbidden/unauthorized responses as appropriate.
   - Verify no prefetching or parent-level aggregate call leaks admin data before authorization is resolved.

8. **Wire independent loading**
   - Use separate async calls/state containers per section.
   - Do not gate all sections behind a single combined request.
   - Handle partial success:
     - one section can show content
     - another can show empty
     - another can show error
   - Keep UX consistent with existing app patterns.

9. **Testing**
   - Add/extend tests for:
     - authorized role can access route/endpoints
     - unauthorized role receives denied/forbidden behavior
     - unauthorized page path does not trigger admin data loading where testable
     - section endpoints enforce authorization independently
   - If component tests exist, add tests for:
     - access denied rendering
     - independent section state rendering
   - If component tests do not exist, keep UI tests manual and cover server-side behavior with automated tests.

10. **Documentation/comments**
   - Add concise comments only where logic is non-obvious.
   - Keep naming explicit around “finance sandbox admin”.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual web validation:
   - Sign in as an authorized finance sandbox admin/tester user.
   - Confirm finance navigation includes the sandbox admin route.
   - Open the page and verify all five sections render.
   - Verify each section shows appropriate loading behavior before data resolves.
   - Verify empty state for sections with no data.
   - Verify error state by simulating a failing section request if feasible.
   - Verify one failing section does not prevent other sections from loading.

4. Unauthorized validation:
   - Sign in as a user without authorized admin/tester role.
   - Attempt to access the route directly.
   - Confirm access denied state is shown.
   - Confirm no admin data appears.
   - Confirm no section API calls are made from the page when unauthorized.
   - Confirm direct calls to sandbox admin endpoints are rejected by the API.

5. Regression validation:
   - Confirm finance navigation still works for existing finance pages.
   - Confirm no unrelated authorization policies were broken.
   - Confirm tenant scoping remains intact for any new endpoints.

# Risks and follow-ups
- The exact authorized roles may not yet exist as named roles/policies in code; align with existing membership role constants and avoid inventing incompatible role names.
- If the current app lacks a reusable access denied component, create a minimal one consistent with existing styling rather than introducing a large new pattern.
- If there is no existing component test setup for Blazor, avoid overbuilding test infrastructure in this task; prioritize API authorization tests and manual UI verification.
- If backend sandbox admin data sources are not implemented yet, use minimal safe placeholder/query contracts that still support loading/empty/error states without fabricating domain behavior.
- Follow-up task may be needed to refine nav visibility rules if product wants hidden-vs-visible denied-link behavior standardized across admin surfaces.
- Follow-up task may be needed to consolidate repeated section-state UI into reusable async section components if this page introduces a useful pattern.