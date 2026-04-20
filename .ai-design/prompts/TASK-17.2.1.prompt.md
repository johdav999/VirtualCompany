# Goal
Implement backlog task **TASK-17.2.1** for story **US-17.2 ST-FUI-172** by adding tenant-aware finance summary page components in the **Blazor Web App** for:

- Cash position
- Balances
- Monthly summary
- Anomalies

The implementation must:

- Load data from existing finance backend endpoints
- Render **loading**, **empty**, **success**, and **error** states for each screen
- Ensure all displayed values are scoped to the **active tenant**
- Show **expense breakdown by category** on the monthly summary screen when category aggregate data is returned
- Support navigation between summary screens **without full page reload**
- Include integration coverage proving UI values match backend responses for the active tenant

# Scope
In scope:

- Blazor Web finance summary UI pages/components
- Client-side/service-layer integration with existing finance API endpoints
- Shared state handling for loading/empty/error/success
- Tenant-aware data retrieval using existing active tenant context
- Monthly summary category breakdown rendering when data exists
- In-app navigation between finance summary screens
- Integration tests covering tenant scoping and backend/UI value consistency

Out of scope unless required by existing patterns:

- Creating new backend finance endpoints
- Changing finance domain calculations on the server
- Redesigning global navigation beyond what is needed for these screens
- Mobile app work
- New persistence or schema changes unless already required by existing frontend contracts

# Files to touch
Inspect the solution first and update the exact files that align with existing conventions. Likely areas:

- `src/VirtualCompany.Web/**`
  - Finance pages/routes
  - Shared summary components
  - API client/service classes
  - Navigation/menu components if finance tabs/links are defined there
  - DTO/view model mappings if the web app has its own presentation models
- `src/VirtualCompany.Shared/**`
  - Shared finance DTOs/contracts if the web and API already share them
- `src/VirtualCompany.Api/**`
  - Only if minimal endpoint contract exposure or route alignment is needed, but do **not** invent new backend behavior
- `tests/VirtualCompany.Api.Tests/**`
  - Integration tests for tenant-scoped finance summary responses and response/UI contract consistency
- Potentially existing web test projects if present in the repo; use them if available rather than creating a new test stack unnecessarily

Before coding, identify:

- Existing finance endpoints and DTOs
- Existing tenant resolution pattern in web and API
- Existing Blazor route/layout/navigation conventions
- Existing loading/empty/error component patterns
- Existing integration test setup and seeded tenant data

# Implementation plan
1. **Discover existing finance architecture**
   - Find current finance-related endpoints, services, DTOs, and any existing finance pages.
   - Confirm route names and payload shapes for:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Confirm how the active tenant/company context is resolved in the web app and passed to API requests.
   - Reuse existing abstractions for API access, auth, and tenant scoping.

2. **Define or reuse presentation models**
   - Reuse shared DTOs if already suitable for UI binding.
   - If the UI needs view-specific shaping, add thin presentation models in the web layer only.
   - Do not duplicate backend logic in the UI.
   - Ensure nullable/optional fields are handled safely, especially for:
     - empty datasets
     - missing category aggregates
     - partial anomaly details

3. **Create finance summary page structure**
   - Add or complete Blazor pages/components for:
     - Cash position
     - Balances
     - Monthly summary
     - Anomalies
   - Use routable pages under the existing finance area conventions.
   - Add a shared local navigation/tab component so users can move between summary screens without full page reload.
   - Navigation should use Blazor routing and normal in-app links, not hard browser reload behavior.

4. **Implement data loading from existing backend endpoints**
   - For each screen, call the corresponding existing finance endpoint through the project’s standard API client/service layer.
   - Ensure requests include or derive the active tenant context exactly as existing app patterns require.
   - Avoid direct `HttpClient` usage in pages if the app already uses typed services/clients.

5. **Implement screen states consistently**
   - Each summary screen must explicitly support:
     - **Loading**: visible while request is in progress
     - **Empty**: visible when request succeeds but no meaningful data is returned
     - **Success**: render summary values from backend response
     - **Error**: visible when request fails, with safe retry affordance if consistent with app patterns
   - Prefer a shared state wrapper/component if the app already has one.
   - Keep state logic deterministic and easy to test.

6. **Render monthly summary category breakdown**
   - On the monthly summary screen, detect whether category aggregate data is present.
   - If present, render an expense breakdown by category.
   - If absent, do not fail the page; render the rest of the monthly summary normally.
   - Follow existing formatting conventions for currency, percentages, dates, and labels.

7. **Preserve tenant isolation in UI behavior**
   - Ensure all displayed values come only from the active tenant’s backend response.
   - Do not cache or reuse finance data across tenant switches unless the app already has safe tenant-keyed caching.
   - If tenant switching exists in the UI, ensure page reload/requery behavior follows current app conventions and does not show stale values from another tenant.

8. **Add integration tests**
   - Extend existing integration tests to verify:
     - each finance summary endpoint returns tenant-scoped data
     - values shown in the UI contract match backend responses for the active tenant
     - monthly summary includes category breakdown when category aggregate data exists
     - empty and error scenarios are handled as expected where test infrastructure supports them
   - Prefer existing test fixtures, seeded tenants, and API test patterns.
   - If web UI integration tests do not exist, at minimum add API integration tests that lock down the response contracts consumed by the UI and document the UI binding assumptions in code comments/tests.

9. **Polish and align with app conventions**
   - Match existing styling/component patterns in `VirtualCompany.Web`.
   - Keep accessibility basics intact:
     - headings
     - labels
     - semantic lists/tables where appropriate
     - readable empty/error messages
   - Avoid introducing a new UI framework or inconsistent component style.

# Validation steps
Run and verify at minimum:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification in web app**
   - Open each finance summary screen:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Confirm each loads data from the existing backend
   - Confirm loading state appears before data resolves
   - Confirm empty state appears for no-data scenarios if seed/test data supports it
   - Confirm error state appears for failed requests if there is an existing safe way to simulate failures
   - Confirm monthly summary shows category breakdown when category aggregate data is returned
   - Confirm navigation between summary screens happens within the Blazor app without full page reload
   - Confirm switching active tenant shows only that tenant’s finance values and no stale cross-tenant data

4. **Integration verification**
   - Validate test assertions prove backend response values for the active tenant match what the UI is expected to render
   - Validate tenant A cannot receive tenant B finance summary data

# Risks and follow-ups
- **Unknown existing endpoint contracts**: The exact finance endpoint shapes may differ from the task wording. Inspect first and adapt UI to real contracts rather than inventing new ones.
- **Tenant context propagation**: If tenant resolution is implicit in auth/session rather than explicit in route/query/header, follow the established pattern exactly to avoid cross-tenant leakage.
- **State duplication risk**: Avoid copy-pasting loading/error/empty logic across four pages if a shared component/pattern already exists.
- **Formatting mismatches**: Currency/date/number formatting may cause apparent UI/backend mismatches; use existing formatting helpers and test expected rendered values carefully.
- **UI test coverage limitations**: If the repo lacks browser-level UI tests, add the strongest integration coverage possible around API contracts and component/service behavior, and note any remaining UI automation gap in your final summary.
- **Potential follow-up**:
  - add reusable finance summary card/table/chart components if duplication remains
  - add browser-based component/page tests if the workspace already supports them
  - add tenant-keyed caching only if needed and safe under current architecture