# Goal
Implement `TASK-17.2.2` for `US-17.2 ST-FUI-172` by wiring the finance summary UI screens to existing finance aggregate backend APIs with active tenant scoping, robust UI state handling, and client-side navigation between summary views.

This task must ensure that:
- Cash position, balances, monthly summary, and anomalies screens fetch live data from existing finance endpoints.
- Each screen supports loading, empty, success, and error states.
- All requests are scoped to the active tenant/company context.
- Monthly summary shows expense breakdown by category when category aggregate data is present.
- Navigation between summary screens happens without full page reload.
- Integration tests verify UI/backend response alignment under tenant context.

# Scope
In scope:
- Inspect existing finance aggregate APIs and current web summary screens/components.
- Connect the Blazor web finance summary views to the existing backend endpoints.
- Ensure tenant/company context is included/resolved consistently in all finance summary requests.
- Add or refine typed DTOs/view models as needed in shared/application/web layers.
- Implement loading, empty, success, and error UI states for:
  - cash position
  - balances
  - monthly summary
  - anomalies
- Render monthly category expense breakdown when returned by the API.
- Ensure navigation between summary screens uses Blazor routing/navigation, not full reloads.
- Add/update integration tests covering tenant scoping and response rendering expectations.

Out of scope:
- Creating new finance aggregate endpoints unless absolutely required to match an already intended contract.
- Redesigning the finance UI beyond what is needed for state handling and data binding.
- Mobile app changes unless shared contracts force a compile fix.
- Broad refactors unrelated to finance summary integration.

# Files to touch
Likely areas to inspect and update first:

- `src/VirtualCompany.Web/**/*Finance*`
- `src/VirtualCompany.Web/Pages/**/*`
- `src/VirtualCompany.Web/Components/**/*`
- `src/VirtualCompany.Web/Services/**/*`
- `src/VirtualCompany.Shared/**/*Finance*`
- `src/VirtualCompany.Api/**/*Finance*`
- `src/VirtualCompany.Application/**/*Finance*`
- `src/VirtualCompany.Infrastructure/**/*Finance*`
- `tests/VirtualCompany.Api.Tests/**/*Finance*`
- `tests/**/*Web*`
- `README.md` only if a small note is needed, otherwise avoid

Also inspect:
- tenant/company context resolution in web and API layers
- existing typed HTTP client registrations
- route definitions and nav components for finance summary pages
- existing test fixtures for multi-tenant API coverage

# Implementation plan
1. **Discover existing contracts and routes**
   - Find the existing finance aggregate endpoints for:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Document their request/response shapes from code, not assumptions.
   - Identify how active tenant/company context is currently resolved in the web app and API.
   - Identify the current finance summary pages/components and whether they already use placeholders/mock data.

2. **Align shared contracts**
   - If DTOs already exist in `Shared`, reuse them.
   - If the web layer currently uses ad hoc models, replace or align them with shared typed contracts.
   - Keep naming consistent with backend responses.
   - Do not introduce duplicate DTOs unless there is a clear UI projection need.

3. **Implement/adjust web finance API client**
   - Add or update a typed service in `VirtualCompany.Web` for finance summary aggregate calls.
   - Ensure every request carries active tenant/company context using the project’s established pattern.
   - Centralize error handling so screens can distinguish:
     - loading
     - empty/no data
     - recoverable error
     - success
   - Prefer cancellation-aware async calls for page navigation.

4. **Wire each summary screen to live data**
   - Update the four summary screens to load from the finance API client:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Remove mock/static/demo bindings where present.
   - Ensure each screen renders:
     - loading skeleton/spinner/message
     - empty state when API returns no meaningful data
     - success state with formatted values
     - error state with retry option if consistent with existing UX patterns

5. **Monthly summary category breakdown**
   - Detect category aggregate data in the monthly summary response.
   - Render expense breakdown by category only when data is returned.
   - Hide the section cleanly when category data is absent or empty.
   - Preserve formatting consistency with the rest of the finance UI.

6. **Tenant scoping verification**
   - Trace the request path from UI to API and confirm active tenant/company is used end-to-end.
   - If the API already derives tenant from auth/session/context, ensure the web client uses the correct authenticated path.
   - If a tenant header/route/query value is required by current architecture, apply it consistently.
   - Avoid any fallback that could accidentally show cross-tenant data.

7. **Client-side navigation**
   - Ensure navigation between finance summary screens uses Blazor `NavLink`/`NavigationManager` routing.
   - Remove any anchors or patterns that trigger full page reloads.
   - Preserve active tab/section highlighting if such UI exists.

8. **Integration and rendering tests**
   - Add/update tests to verify:
     - each finance summary endpoint is called and returns tenant-scoped data
     - another tenant’s data is not shown
     - monthly summary category breakdown appears when category aggregates exist
     - empty and error states are handled appropriately where test infrastructure supports it
     - values rendered in UI/integration flow match backend responses
   - Prefer existing test conventions and fixtures over inventing new harnesses.

9. **Polish and consistency**
   - Keep formatting for currency/percent/date aligned with existing finance UI conventions.
   - Ensure null handling is defensive.
   - Keep changes minimal and cohesive.

# Validation steps
Run these after implementation:

1. Build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in web app:
   - Navigate to each finance summary screen.
   - Confirm data loads from backend, not placeholders.
   - Confirm loading state appears before data resolves.
   - Confirm empty state appears for no-data scenarios if test data supports it.
   - Confirm error state appears for failed requests if there is a safe local way to simulate failure.
   - Confirm monthly summary shows category expense breakdown when category data exists.
   - Confirm switching between summary screens does not trigger full page reload.

4. Verify tenant behavior:
   - Use at least two tenant/company contexts if test fixtures/dev data allow.
   - Confirm values differ appropriately by tenant and no cross-tenant leakage occurs.

5. Review code quality:
   - No duplicated finance DTOs without reason.
   - No hardcoded tenant IDs.
   - No synchronous blocking calls in UI data loading.
   - No broad unrelated refactors.

# Risks and follow-ups
- Existing finance endpoints may not perfectly match current UI expectations; prefer adapting UI bindings before changing backend contracts.
- Tenant context handling may be implicit and easy to break; verify carefully before and after changes.
- Empty-state semantics may differ by endpoint, so define “empty” based on actual response contracts rather than guesswork.
- If there are no existing web integration/component tests for these screens, add the smallest viable coverage now and note broader UI test expansion as follow-up.
- If category aggregates are returned in a shape not suitable for direct rendering, add a thin UI projection model rather than changing backend contracts.
- If navigation currently mixes SSR and interactive behavior, keep the fix aligned with the app’s existing Blazor hosting model instead of introducing a new pattern.