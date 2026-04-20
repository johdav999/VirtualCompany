# Goal
Implement backlog task **TASK-17.1.3 — Build Finance landing page with links to all finance workspace screens** for story **US-17.1 ST-FUI-171 — Finance module shell, routing, and role-aware navigation**.

Deliver a Blazor web implementation that:
- Adds a top-level **Finance** navigation item for authorized users only.
- Registers finance routes in the app router.
- Adds a **Finance landing page** that links to:
  - cash position
  - transactions
  - invoices
  - balances
  - monthly summary
  - anomalies
- Enforces role/permission-aware visibility and forbidden behavior for unauthorized users.
- Preserves tenant-scoped behavior on direct route navigation and browser refresh.

Use existing project conventions and architecture. Prefer minimal, composable changes that fit the current modular monolith and Blazor Web App structure.

# Scope
In scope:
- Blazor web routing for finance module shell pages.
- Finance landing page UI.
- Top-level navigation entry visibility based on finance access.
- Route protection / forbidden state for unauthorized users.
- Tenant-aware page loading behavior for finance routes.
- Basic placeholder/shell pages for linked finance screens if they do not already exist.
- Tests covering navigation visibility, route registration, and authorization behavior where practical.

Out of scope:
- Full finance data grids, charts, or backend finance calculations.
- New finance domain models beyond what is required for route/page shells.
- Broad redesign of auth system or tenant resolution.
- Mobile app changes.
- Non-finance navigation refactors unless required to integrate cleanly.

Implementation expectations:
- Reuse existing authorization and tenant context patterns.
- Do not hardcode tenant data outside established tenant resolution mechanisms.
- If a finance permission constant/policy does not exist, add it in the smallest consistent way.
- If linked finance pages are not implemented yet, create shell pages with correct routes and headings so the landing page links are valid.

# Files to touch
Inspect the repo first and adjust to actual structure, but expect to touch files in these areas:

- `src/VirtualCompany.Web/**`
  - App router setup (`App.razor`, route config, or equivalent)
  - Shared navigation/menu components
  - Layout components
  - Finance pages under a feature folder such as:
    - `Pages/Finance/Index.razor`
    - `Pages/Finance/CashPosition.razor`
    - `Pages/Finance/Transactions.razor`
    - `Pages/Finance/Invoices.razor`
    - `Pages/Finance/Balances.razor`
    - `Pages/Finance/MonthlySummary.razor`
    - `Pages/Finance/Anomalies.razor`
  - Forbidden/access denied UI if route-level handling is centralized there

- `src/VirtualCompany.Application/**`
  - Permission/policy definitions if web authorization depends on application-layer constants
  - Finance query contracts only if needed for tenant-scoped shell data

- `src/VirtualCompany.Api/**`
  - Authorization policy registration only if policies are hosted there and consumed by the web app

- `src/VirtualCompany.Shared/**`
  - Shared permission names / route constants if that pattern exists

- `tests/**`
  - Web/UI/component tests if present
  - API/auth tests if route authorization is validated there
  - Integration tests for tenant-scoped finance route access if test infrastructure exists

Before editing, identify:
- Where nav items are defined
- How permissions are represented
- How tenant context is resolved
- How forbidden states are rendered
- How Blazor routes are organized

# Implementation plan
1. **Discover existing patterns**
   - Inspect `README.md`, web project structure, and auth/navigation setup.
   - Find:
     - main layout and nav menu component
     - router configuration
     - authorization policies/claims/permissions
     - tenant context service or active tenant accessor
     - existing forbidden/access denied page or component
   - Mirror existing conventions exactly.

2. **Define or reuse finance access policy**
   - Reuse an existing finance permission if already present.
   - If missing, add a permission/policy aligned with acceptance criteria, e.g. finance view access.
   - Keep naming consistent with current permission conventions.
   - Ensure unauthorized users:
     - do not see finance nav
     - receive forbidden on finance routes

3. **Register finance routes**
   - Add routeable Blazor pages for:
     - `/finance`
     - `/finance/cash-position`
     - `/finance/transactions`
     - `/finance/invoices`
     - `/finance/balances`
     - `/finance/monthly-summary`
     - `/finance/anomalies`
   - Use route attributes or page directives consistent with the app.
   - Ensure routes are discoverable by the Blazor router and work on browser refresh.

4. **Build the Finance landing page**
   - Create a finance landing page with:
     - page title/header
     - short module description if consistent with app style
     - link cards/list items to all required finance screens
   - Use existing design system/components if available.
   - Each link should navigate to the corresponding finance route.
   - Keep content simple and production-clean.

5. **Create shell pages for linked screens**
   - If the target pages do not exist, add lightweight shell pages with:
     - authorized route protection
     - page heading
     - tenant-aware placeholder content
   - These pages should be sufficient to validate routing and navigation now, while allowing future finance work to build on them.

6. **Add top-level Finance navigation**
   - Update the main navigation/menu to include a top-level **Finance** item.
   - Show it only when the current user has finance view permission.
   - Point it to `/finance`.
   - Preserve existing nav ordering and styling conventions.

7. **Enforce forbidden behavior on finance routes**
   - Apply route/page authorization using the existing pattern:
     - `[Authorize(Policy = ...)]`
     - `AuthorizeView`
     - route guards
     - or centralized access wrapper
   - Unauthorized direct navigation must render a forbidden state, not a broken page.
   - If the app distinguishes unauthenticated vs unauthorized, preserve that behavior.

8. **Preserve tenant-scoped behavior**
   - For any page initialization or data loading, use the active tenant context only.
   - Do not fetch or display cross-tenant data.
   - If shell pages need sample/placeholder data, derive it through existing tenant-scoped services or avoid data loading entirely.
   - If there is an existing finance query endpoint/service, ensure it is called with active tenant context only.

9. **Handle refresh-safe routing**
   - Verify the app’s router and hosting setup already support deep links.
   - If finance routes need inclusion in fallback handling or route assembly scanning, add it.
   - Do not introduce custom routing behavior unless necessary.

10. **Add tests**
   - Add or update tests for:
     - finance nav visible for authorized users
     - finance nav hidden for unauthorized users
     - finance landing page contains all required links
     - unauthorized access to finance routes returns/renders forbidden state
     - authorized direct navigation succeeds
     - tenant-scoped behavior is preserved on direct route access where testable
   - Prefer existing test style and infrastructure over introducing new frameworks.

11. **Keep implementation incremental**
   - Avoid broad abstractions unless repeated patterns already exist.
   - Add route constants only if the codebase already centralizes routes.
   - Keep shell pages lightweight and easy to extend in later finance tasks.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Sign in as a user with finance view permission.
   - Confirm a top-level **Finance** nav item is visible.
   - Open `/finance`.
   - Confirm links are present for:
     - cash position
     - transactions
     - invoices
     - balances
     - monthly summary
     - anomalies
   - Click each link and confirm the route loads without errors.
   - Refresh each finance route in the browser and confirm it still loads.

4. Unauthorized verification:
   - Sign in as a user without finance access.
   - Confirm **Finance** nav item is not visible.
   - Directly navigate to `/finance` and one or more child routes.
   - Confirm forbidden state is shown.

5. Tenant isolation verification:
   - Using a user with access to multiple tenants if supported, switch active tenant.
   - Navigate to finance routes and confirm any displayed tenant-bound content reflects only the active tenant.
   - Confirm no cross-tenant leakage in route-loaded data.

6. Code quality checks:
   - Ensure no duplicated permission strings if constants/policies exist.
   - Ensure route names and labels are consistent.
   - Ensure no dead links from the landing page.

# Risks and follow-ups
- **Unknown permission model**: The repo may use roles, claims, policies, or membership permissions JSON. Reuse the existing mechanism rather than inventing a new one.
- **Unknown Blazor hosting mode**: Router/auth patterns differ between SSR, interactive server, and hybrid setups. Follow current app conventions.
- **Forbidden UX may be centralized**: If the app already has a shared access denied flow, integrate with it instead of creating a finance-specific version.
- **Tenant context may be implicit**: Be careful not to bypass tenant scoping in page initialization or service calls.
- **Linked pages may already exist**: If so, do not replace them; wire the landing page to the existing routes and only fill gaps.
- **Refresh support may depend on hosting config**: If deep-link refresh issues appear, document whether the fix belongs in web host/server config rather than page code.

Follow-up candidates after this task:
- Add richer finance landing page summaries/cards backed by tenant-scoped finance queries.
- Add breadcrumbs and section navigation within finance module.
- Add automated component tests for nav authorization and landing page links if current coverage is thin.
- Add route constants/shared finance navigation metadata if more finance screens are coming soon.