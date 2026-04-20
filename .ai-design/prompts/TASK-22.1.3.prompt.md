# Goal
Implement `TodayFocusPanel.razor` in the Blazor web app to display the Today’s Focus decision panel for story **US-22.1**. The component must render **3–5 prioritized focus items** returned by `GET /api/dashboard/focus` as action cards showing **title**, **short description**, and a **CTA button**, and clicking the CTA must navigate to the exact `NavigationTarget` returned by the API.

# Scope
In scope:
- Add or complete a reusable Blazor component named `TodayFocusPanel.razor`.
- Load focus items from the existing dashboard focus API endpoint.
- Render returned items in descending priority order as cards.
- Show title, description, and CTA/action affordance per card.
- Wire CTA click behavior to `NavigationManager.NavigateTo(item.NavigationTarget)`.
- Handle loading, empty, and error states appropriately for dashboard UX.
- Keep implementation tenant/user aware through the existing authenticated API flow rather than hardcoding IDs.
- Add/update tests for component rendering and navigation behavior if the current test setup supports Blazor component tests.

Out of scope:
- Building or changing the backend aggregation logic beyond what is strictly required for the UI contract.
- Redesigning the dashboard layout outside the panel.
- Adding new business rules for prioritization.
- Mobile implementation.
- Broad styling refactors unrelated to this panel.

# Files to touch
Prefer the smallest set of changes necessary. Likely locations to inspect and update:

- `src/VirtualCompany.Web/.../TodayFocusPanel.razor`
- `src/VirtualCompany.Web/.../TodayFocusPanel.razor.cs` if code-behind pattern is used
- `src/VirtualCompany.Web/.../Pages/...` or dashboard host page where the panel is placed
- `src/VirtualCompany.Web/.../Services/...` or existing API client used by dashboard widgets
- `src/VirtualCompany.Shared/...` for shared DTOs if the web app consumes shared contracts
- `src/VirtualCompany.Api/...` only if the focus endpoint contract is missing from generated/manual client usage
- `tests/VirtualCompany.Api.Tests/...` only if endpoint contract tests need alignment
- Any existing web test project for Blazor component tests, if present

Before editing, search for:
- `TodayFocusPanel`
- `/api/dashboard/focus`
- `FocusItem`
- `NavigationTarget`
- dashboard page/component composition
- existing card components or dashboard widget patterns

# Implementation plan
1. **Inspect existing contracts and usage**
   - Find the `FocusItem` DTO/model already used by the API.
   - Confirm fields available to the web app: `Id`, `Title`, `Description`, `ActionType`, `PriorityScore`, `NavigationTarget`.
   - Confirm whether the web app already has a typed client/service for dashboard data. Reuse it if available.

2. **Locate or create the panel component**
   - If `TodayFocusPanel.razor` exists, extend it rather than replacing patterns already used in the web project.
   - If it does not exist, create it in the same folder/namespace convention as other dashboard widgets.

3. **Implement data loading**
   - Use the existing dashboard API client/service to call `GET /api/dashboard/focus`.
   - Load data in the appropriate lifecycle method (`OnInitializedAsync` or `OnParametersSetAsync` depending on page composition).
   - Do not hardcode company/user IDs; rely on the existing authenticated request pipeline and current tenant context.

4. **Render focus cards**
   - Render each returned item as a card in API order.
   - Display:
     - `Title`
     - a short description using `Description`
     - CTA button text derived from `ActionType` if no explicit CTA label exists in the contract
   - Keep the markup accessible and consistent with existing dashboard card styling/components.
   - If more than 5 items are ever returned unexpectedly, render only the first 5 in the panel as a defensive UI measure, but do not reorder them.
   - If fewer than 3 are returned, render what the API returned and do not fabricate items.

5. **Implement CTA navigation**
   - Inject `NavigationManager`.
   - On CTA click, navigate to `item.NavigationTarget` exactly as returned.
   - Do not transform, append, or reinterpret the target unless existing app conventions require safe handling of relative URLs.
   - If the app has a centralized navigation helper, use it.

6. **Handle UX states**
   - Loading state: show a lightweight placeholder/skeleton/spinner consistent with existing dashboard widgets.
   - Empty state: show a concise message if no focus items are returned.
   - Error state: show a non-breaking error message and log if the app has an existing logger pattern.
   - Avoid throwing unhandled exceptions from the component.

7. **Styling and layout**
   - Reuse existing CSS utility classes/components where possible.
   - Keep the panel visually suitable for 3–5 cards.
   - Avoid introducing a new styling system; prefer co-located CSS only if the project already uses `.razor.css`.

8. **Testing**
   - Add/update tests to verify:
     - cards render for returned focus items
     - title and description are shown
     - CTA exists for each item
     - clicking CTA navigates to the exact `NavigationTarget`
   - If component tests are not currently set up, add at least targeted API/client or page-level tests only if consistent with the repo’s testing approach.
   - Do not introduce a large new test framework unless already present.

9. **Integration check**
   - Ensure the dashboard page actually includes `TodayFocusPanel`.
   - Verify no namespace/import issues.
   - Verify the component builds cleanly with the solution.

# Validation steps
1. Search the solution for existing focus API contracts and dashboard widget patterns.
2. Build the solution:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Manually verify in the web app:
   - Navigate to the dashboard page containing `TodayFocusPanel`.
   - Confirm 3–5 focus items render as cards when the API returns data.
   - Confirm cards display title and description.
   - Confirm CTA is visible on each card.
   - Click each CTA and verify navigation goes to the exact `NavigationTarget`.
5. If local API mocking or seeded data exists, validate with mixed domain data so the panel can display items sourced from approvals, tasks, anomalies, and finance alerts when available.
6. Confirm graceful behavior for:
   - loading
   - empty response
   - API failure

# Risks and follow-ups
- The web app may not yet have a typed client for `/api/dashboard/focus`; if missing, add the thinnest possible client abstraction rather than calling `HttpClient` ad hoc from markup.
- The API contract may exist only in backend code and not yet be shared with the web app; align DTO usage carefully to avoid duplicate models.
- `NavigationTarget` may contain relative or absolute paths; preserve exact behavior expected by the app and avoid unsafe assumptions.
- If the API currently returns more than 5 items or unsorted data, that is a backend contract issue and should be raised separately rather than silently “fixing” business behavior in the UI.
- If no component test infrastructure exists for Blazor, note that navigation behavior may need a follow-up task for proper UI automation coverage.
- Follow-up task if needed: add dashboard-level telemetry/audit for focus CTA clicks, but do not block this task on analytics instrumentation.