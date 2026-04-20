# Goal
Implement `TASK-17.1.2` for `US-17.1 ST-FUI-171` by adding a role-aware Finance module shell in the Blazor web app, registering Finance routes, showing Finance navigation only to authorized users, and enforcing route/API access so unauthorized users receive a forbidden state while authorized users only see tenant-scoped Finance data for the active tenant.

# Scope
In scope:
- Add a top-level Finance navigation item visible only to users with finance view permission.
- Add/register Finance routes in the web application router so browser refresh works.
- Implement a Finance landing page with links to:
  - cash position
  - transactions
  - invoices
  - balances
  - monthly summary
  - anomalies
- Enforce role/permission-based route guards for Finance pages.
- Ensure unauthorized users:
  - do not see Finance navigation
  - receive a forbidden state on direct Finance route access
- Ensure Finance data access remains scoped to the active tenant/company.
- Add/update tests covering navigation visibility, route authorization, and tenant scoping behavior.

Out of scope unless required by existing patterns:
- Building full Finance feature pages beyond shell/placeholder pages and landing links.
- Introducing a brand-new auth model if one already exists.
- Cross-tenant admin/superuser behavior not described in the task.
- Mobile app changes.

# Files to touch
Inspect the existing solution first and then update the actual matching files. Likely areas:

- `src/VirtualCompany.Web/`
  - App/router setup:
    - `App.razor`
    - router/layout/auth-related components
  - Navigation/menu components:
    - main nav, sidebar, header, shell layout components
  - Finance pages/components:
    - add `Pages/Finance/` or existing feature folder equivalent
    - landing page and placeholder child pages
  - authorization helpers/services used by UI
  - forbidden/access denied UI if already present

- `src/VirtualCompany.Api/`
  - Finance route endpoints/controllers if route/API authorization is enforced server-side here
  - authorization policy registration
  - tenant resolution usage in Finance endpoints

- `src/VirtualCompany.Application/`
  - Finance queries/services for landing/module shell if needed
  - permission abstractions or authorization handlers
  - tenant-scoped query logic

- `src/VirtualCompany.Infrastructure/`
  - permission/authorization implementations
  - tenant-aware repository/query enforcement if Finance data endpoints are added/updated

- `src/VirtualCompany.Shared/`
  - shared permission constants / DTOs / route constants if such patterns exist

- Tests:
  - `tests/VirtualCompany.Api.Tests/`
  - any web/component test project if present
  - add/update tests for:
    - nav visibility by permission
    - forbidden on unauthorized direct route access
    - tenant-scoped Finance data access
    - route registration / refresh-safe behavior where testable

Also review:
- `README.md`
- any architecture or auth docs
- existing permission constants, policies, and tenant access patterns before coding

# Implementation plan
1. **Discover existing auth, tenant, and navigation patterns**
   - Inspect how the app currently:
     - resolves active tenant/company
     - stores membership roles/permissions
     - hides/shows navigation items
     - protects pages/routes
     - returns forbidden vs redirect behavior
   - Reuse existing patterns rather than inventing new ones.
   - Identify whether permissions are role-based, claim-based, policy-based, or membership JSON-based.

2. **Define/reuse a Finance view permission**
   - Find the canonical place for permission names/constants.
   - Add or reuse a permission representing Finance read/view access, e.g. `finance.view`.
   - Map existing finance-capable roles to that permission if role-to-permission mapping exists.
   - Keep naming consistent with current conventions.

3. **Register Finance authorization policy**
   - Add a policy for Finance access in the ASP.NET Core authorization setup if not already present.
   - Policy should require:
     - authenticated user
     - active tenant/company context
     - finance view permission for that tenant membership
   - If the app uses custom authorization handlers, extend them instead of bypassing them.

4. **Add Finance routes and pages in the Blazor web app**
   - Create a Finance landing page route, likely `/finance`.
   - Create child routes/placeholders for:
     - `/finance/cash-position`
     - `/finance/transactions`
     - `/finance/invoices`
     - `/finance/balances`
     - `/finance/monthly-summary`
     - `/finance/anomalies`
   - Ensure these are routable components/pages so browser refresh resolves correctly.
   - Use the app’s existing layout and route conventions.

5. **Protect Finance pages with route guards**
   - Apply the existing authorization mechanism to all Finance pages.
   - If the app uses `[Authorize(Policy = ...)]` on Razor components/pages, use that.
   - If the app uses wrapper components or route-view authorization, integrate there.
   - Unauthorized direct access should render a forbidden/access denied state, not expose content.
   - Prefer a 403-style UX over redirect loops unless the app’s established pattern differs.

6. **Implement Finance landing page shell**
   - Build the landing page as a module shell with links/cards/list items to the six required destinations.
   - Keep the page lightweight and consistent with the current design system.
   - If child pages are not yet implemented, provide placeholder pages with titles and empty states rather than broken links.

7. **Add role-aware Finance navigation visibility**
   - Update the top-level navigation component to conditionally render Finance only when the current user has Finance view permission for the active tenant.
   - Do not render hidden-but-disabled items unless that is the established UX pattern.
   - Ensure nav visibility updates correctly when tenant context changes.

8. **Enforce tenant-scoped Finance data access**
   - For any Finance endpoints/queries used by the landing or child pages:
     - require active tenant context
     - filter by `company_id` / tenant identifier
     - never return data across tenants
   - If placeholder pages do not yet load real data, still ensure any backing query/service follows tenant-scoped patterns.
   - Reuse existing tenant-aware query abstractions and avoid ad hoc filtering in UI code.

9. **Handle forbidden state explicitly**
   - Reuse an existing forbidden page/component if available.
   - Otherwise add a simple reusable forbidden state component/page aligned with app UX.
   - Ensure unauthorized Finance route access lands in that state rather than a generic crash or blank page.

10. **Add tests**
   - Add/update tests to verify:
     - user with finance permission sees Finance nav
     - user without finance permission does not see Finance nav
     - unauthorized direct access to Finance routes returns forbidden/access denied
     - authorized access succeeds
     - Finance data queries/endpoints are tenant-scoped to active tenant only
     - Finance routes are registered and do not fail on refresh/navigation resolution
   - Prefer existing test styles:
     - API integration tests for authorization and tenant scoping
     - component/UI tests if present for nav visibility
   - If no UI test harness exists, cover the logic via authorization service tests and endpoint tests.

11. **Keep implementation minimal and aligned**
   - Do not over-engineer a full Finance subsystem.
   - Focus on shell, routing, authorization, and tenant safety.
   - Leave clear TODOs only where placeholders are intentional and acceptable.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual validation in the web app:
   - Sign in as a user with Finance view permission for tenant A.
   - Confirm top-level Finance nav is visible.
   - Open `/finance` and verify links to:
     - cash position
     - transactions
     - invoices
     - balances
     - monthly summary
     - anomalies
   - Refresh the browser on `/finance` and on at least one child route; confirm no routing/navigation error.
   - Switch tenant if multi-tenant switching exists; confirm visibility and data are based on the active tenant.

4. Unauthorized validation:
   - Sign in as a user without Finance access.
   - Confirm Finance nav is not visible.
   - Directly navigate to `/finance` and a child route.
   - Confirm forbidden/access denied state is shown.

5. Tenant isolation validation:
   - Using test data for at least two tenants, verify Finance endpoints/queries only return records for the active tenant.
   - Confirm attempts to access another tenant’s Finance data are forbidden or not found per existing platform conventions.

6. Code quality validation:
   - Ensure no Finance page bypasses policy checks.
   - Ensure no tenant-owned Finance query executes without tenant filter/context.
   - Ensure permission names/constants are centralized, not duplicated as string literals.

# Risks and follow-ups
- **Risk: unclear existing permission model**
  - The codebase may use roles, claims, membership JSON, or policies inconsistently.
  - Follow the dominant existing pattern; do not introduce a parallel auth path.

- **Risk: Blazor authorization behavior may default to redirect instead of forbidden**
  - If current routing sends unauthorized users to login/access denied automatically, adapt to that pattern while still satisfying the forbidden-state requirement.

- **Risk: tenant context may be resolved differently in web vs API**
  - Verify active tenant resolution is consistent across UI and backend.
  - Avoid trusting client-side tenant identifiers without server-side enforcement.

- **Risk: no existing Finance data endpoints yet**
  - If the shell is mostly placeholder UI, still wire authorization and tenant-safe query scaffolding so later pages inherit the correct pattern.

- **Risk: browser refresh support depends on hosting fallback config**
  - If refresh issues stem from server fallback routing rather than Blazor route registration, update the hosting/router configuration accordingly.

Follow-ups after completion:
- Flesh out each Finance child page with real tenant-scoped queries.
- Add finer-grained Finance permissions if needed later, e.g. `finance.cashposition.view`, `finance.invoices.view`.
- Add audit logging for forbidden Finance access attempts if the platform already tracks business/security audit events.
- Consider extracting route and permission constants for Finance into shared modules if repeated elsewhere.