# Goal

Implement backlog task **TASK-17.1.1 — Add finance route group and page shell components to the web router** for story **US-17.1 ST-FUI-171 — Finance module shell, routing, and role-aware navigation**.

Deliver a minimal but production-aligned Finance module shell in the **Blazor Web App** that:

- registers Finance routes in the app router
- adds a top-level Finance navigation item only for authorized users
- provides a Finance landing page shell with links to:
  - cash position
  - transactions
  - invoices
  - balances
  - monthly summary
  - anomalies
- enforces finance access checks so unauthorized users:
  - do not see Finance navigation
  - receive a forbidden state on finance routes
- preserves tenant-aware behavior by ensuring finance pages resolve only within the active tenant context
- supports direct URL navigation and browser refresh without routing errors

Keep this task focused on **routing, shell pages, authorization gating, and tenant-aware page composition**, not full finance feature implementation.

# Scope

In scope:

- Web router registration for Finance route group/pages
- Finance landing page shell
- Placeholder/page shell components for finance subroutes
- Role/permission-aware navigation visibility
- Forbidden state rendering for unauthorized access
- Tenant-context-aware page initialization patterns
- Basic tests covering route registration, nav visibility logic, and forbidden behavior

Out of scope:

- Real finance data grids, charts, or backend finance APIs
- New database schema or migrations
- Full finance domain services
- Complex policy engine changes
- Mobile app changes
- Non-finance navigation redesign

Implementation should align with existing app patterns for:

- routing
- layout/navigation composition
- authorization/policy checks
- tenant resolution
- empty/forbidden states

If the codebase already has reusable primitives for authorization, tenant context, nav registration, or forbidden pages, reuse them rather than inventing parallel patterns.

# Files to touch

Inspect the existing structure first, then update the most relevant files in `src/VirtualCompany.Web` and tests. Likely candidates include:

- `src/VirtualCompany.Web/App.razor`
- `src/VirtualCompany.Web/Components/**`
- `src/VirtualCompany.Web/Layout/**`
- `src/VirtualCompany.Web/Pages/**`
- `src/VirtualCompany.Web/Program.cs`
- `src/VirtualCompany.Web/_Imports.razor`
- `src/VirtualCompany.Web/wwwroot/**` only if needed for nav icons/styles
- `tests/VirtualCompany.Api.Tests/**` only if web routing/authorization integration is covered there
- any existing web test project if present for Blazor/component tests

Create new files as needed, likely under a finance-specific area, for example:

- `src/VirtualCompany.Web/Pages/Finance/FinancePage.razor`
- `src/VirtualCompany.Web/Pages/Finance/CashPositionPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/TransactionsPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/InvoicesPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/BalancesPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/MonthlySummaryPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/AnomaliesPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/FinanceShell.razor` or shared shell component if appropriate
- `src/VirtualCompany.Web/Components/Authorization/ForbiddenState.razor` only if no reusable forbidden component exists

Do not assume these exact paths exist; adapt to the repository’s actual conventions.

# Implementation plan

1. **Inspect existing web app patterns before coding**
   - Find how routes are currently declared in the Blazor app.
   - Find how top-level navigation is built.
   - Find how tenant context is resolved in the web layer.
   - Find whether authorization is based on:
     - roles
     - claims
     - policies
     - permission helper services
   - Find whether a reusable forbidden/unauthorized state already exists.
   - Find whether there are existing module shells for other areas to mirror.

2. **Define or reuse a finance-view authorization check**
   - Reuse an existing finance permission/policy if already present.
   - If none exists in the web layer, add the smallest viable policy/check abstraction consistent with current architecture.
   - Prefer policy-based authorization or existing permission service usage over hardcoded role checks.
   - The check must support:
     - nav visibility decisions
     - route/page access decisions
   - Avoid introducing backend-wide auth redesign in this task.

3. **Register Finance routes in the Blazor router**
   - Ensure the Finance landing page and subpages are routable via direct URL.
   - Use stable route patterns, for example:
     - `/finance`
     - `/finance/cash-position`
     - `/finance/transactions`
     - `/finance/invoices`
     - `/finance/balances`
     - `/finance/monthly-summary`
     - `/finance/anomalies`
   - Ensure browser refresh on these routes resolves correctly through the app router.
   - Follow existing route conventions if the app uses tenant-prefixed or area-based URLs.

4. **Create a Finance landing page shell**
   - Build a Finance landing page that clearly lists links/cards to:
     - cash position
     - transactions
     - invoices
     - balances
     - monthly summary
     - anomalies
   - Keep the page intentionally shell-level:
     - title/header
     - brief description
     - navigation links/cards
     - optional placeholder summary text
   - Do not add fake business metrics unless the app already uses standard placeholders.

5. **Create finance subpage shells**
   - Add lightweight placeholder pages for each finance route.
   - Each page should:
     - render a clear heading
     - indicate it is tenant-scoped
     - provide a consistent shell layout
     - optionally link back to Finance home
   - If there is a reusable module shell component, use it.
   - If not, create a small shared Finance shell component to avoid duplication.

6. **Enforce authorization on finance routes**
   - Authorized users should see the Finance page content.
   - Unauthorized users should receive a forbidden state, not a broken page and not silent redirect unless that is the app standard.
   - Apply the same access rule consistently across:
     - `/finance`
     - all finance child routes
   - Prefer declarative authorization where possible; if page-level checks are required, centralize them in a shared shell/base pattern.

7. **Hide Finance navigation for unauthorized users**
   - Update the top-level navigation composition so Finance appears only when the current user has finance view access.
   - Ensure unauthorized users do not see Finance nav items at all.
   - If navigation is generated from a model/list, add Finance there with a visibility predicate.
   - If navigation is hardcoded in layout markup, refactor minimally to keep logic maintainable.

8. **Preserve tenant-scoped behavior**
   - Ensure finance pages resolve against the active tenant context already used by the app.
   - Do not introduce any cross-tenant fallback behavior.
   - If pages call any query/service during initialization, pass or validate the active tenant context.
   - If no data is loaded yet, still structure the page so future finance queries will be tenant-scoped by default.
   - Add comments only where necessary to clarify tenant assumptions.

9. **Add or reuse forbidden state UI**
   - Reuse an existing forbidden component/page if available.
   - Otherwise add a minimal, consistent forbidden state component with:
     - title
     - short explanation
     - optional link back to a safe page
   - Keep wording generic and security-safe.

10. **Add tests**
   - Add focused tests for the implemented behavior using the project’s existing test style.
   - Cover at least:
     - Finance routes are registered and render without router errors
     - Finance landing page contains the required links
     - unauthorized users do not see Finance navigation
     - unauthorized access to finance routes yields forbidden state
     - authorized access renders finance shell
   - If tenant context is testable in the web layer, verify finance page initialization respects active tenant context rather than global/unscoped access.

11. **Keep implementation minimal and coherent**
   - Avoid speculative abstractions.
   - Avoid adding backend APIs unless absolutely required.
   - Keep naming aligned with story/task language: Finance, cash position, transactions, invoices, balances, monthly summary, anomalies.

# Validation steps

1. **Restore/build**
   - Run:
     - `dotnet build`
   - Fix any compile issues across the solution.

2. **Run tests**
   - Run:
     - `dotnet test`
   - Ensure all existing tests still pass.
   - Ensure new tests pass.

3. **Manual web verification**
   - Start the web app using the repository’s normal local run flow.
   - Verify as a user with finance access:
     - Finance appears in top-level navigation
     - `/finance` loads successfully
     - landing page shows links to all six required destinations
     - each finance subroute loads successfully
     - browser refresh on each finance route works
   - Verify as a user without finance access:
     - Finance nav item is absent
     - direct navigation to `/finance` and child routes shows forbidden state

4. **Tenant-awareness verification**
   - Using a user with multiple tenant memberships if supported:
     - switch active tenant
     - verify finance pages resolve within the active tenant context
     - confirm no cross-tenant leakage in any displayed tenant identifiers or loaded shell data

5. **Regression check**
   - Confirm no existing non-finance routes/navigation are broken.
   - Confirm layout and router still behave correctly for anonymous/authenticated flows already present.

# Risks and follow-ups

- **Unknown auth pattern risk:** The repo may not yet have a finalized permission model for finance view access. If so, implement the smallest web-layer-compatible check and note where it should later align with centralized policy definitions.
- **Unknown nav composition risk:** If navigation is duplicated across layouts/components, update only the canonical source and avoid partial inconsistencies.
- **Unknown tenant context risk:** If tenant resolution is incomplete in the web layer, structure finance pages to consume the existing active tenant provider/service and document any gap rather than inventing a second tenant mechanism.
- **Testing gap risk:** There may be limited existing Blazor component test infrastructure. If so, add the smallest viable tests in the current framework and document recommended follow-up coverage.
- **Follow-up tasks likely needed after this one:**
  - finance-specific query services and tenant-scoped data retrieval
  - richer finance dashboard widgets/content
  - centralized finance permission constants/policies if not already present
  - breadcrumb/secondary navigation improvements within the Finance module
  - audit logging for finance page access if required by future compliance stories