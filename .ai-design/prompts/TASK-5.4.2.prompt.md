# Goal
Implement backlog task **TASK-5.4.2 — Build filter controls and URL state synchronization in the timeline UI** for story **US-5.4 Filtering, drill-down, and audit deep linking**.

Deliver a tenant-aware timeline/activity feed experience in the **Blazor Web App** that:
- supports filtering by **agent, department, task, event type, status, and timeframe**
- keeps filter state synchronized with the **URL query string**
- supports reload/share/deep-link behavior without losing state
- opens an activity detail view with **raw payload, summary, correlation links, and audit deep link**
- includes a working **same-tenant audit detail deep link** when an audit record exists
- adds **automated integration tests** covering top filter combinations and the audit deep-link flow

Use existing architecture conventions:
- Blazor Web App frontend
- ASP.NET Core modular monolith backend
- CQRS-lite query flow
- strict tenant scoping
- auditability as a domain feature
- no raw chain-of-thought exposure

# Scope
In scope:
- Timeline UI filter controls
- Query-string read/write synchronization for filter state
- Timeline query contract updates if needed to support all required filters
- Detail panel/modal/page for activity item drill-down
- Correlation links from activity item to related entities where available
- Audit deep link generation and navigation
- Integration tests for filter behavior, URL state persistence, and audit deep-link flow

Out of scope unless required to satisfy acceptance criteria:
- Redesigning the entire activity feed visual system
- New audit domain model beyond what is needed for linking
- Mobile implementation
- Broad API refactors unrelated to timeline filtering/detail behavior
- New logging/telemetry systems

Implementation constraints:
- Preserve tenant isolation on all queries and links
- Returned results must match **all selected filters** (AND semantics)
- URL sync should avoid noisy navigation loops
- Detail view must show concise operational explanation only; do not expose hidden reasoning
- Prefer extending existing query/view models over introducing parallel duplicate models

# Files to touch
Inspect the solution first and then update the most relevant existing files. Likely touch points include:

Frontend:
- `src/VirtualCompany.Web/**`  
  Look for:
  - timeline/activity feed page/component
  - dashboard recent activity components
  - shared filter UI components
  - navigation/query-string helpers
  - detail drawer/modal/page components
  - audit detail route definitions

Backend/API/Application if needed:
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`

Tests:
- `tests/VirtualCompany.Api.Tests/**`
- any existing web/UI/integration test project if present under `tests/**`

Also inspect:
- `README.md`
- solution/project references in:
  - `src/VirtualCompany.Web/VirtualCompany.Web.csproj`
  - `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
  - `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

Before coding, identify the concrete files that already implement:
- activity/timeline queries
- audit detail routes/pages
- tenant-aware navigation
- integration test patterns

# Implementation plan
1. **Discover existing timeline and audit flow**
   - Find the current activity feed/timeline page and its data source.
   - Find the query/endpoint used to fetch activity items.
   - Find existing audit detail page/route and how tenant context is resolved.
   - Find whether activity items already carry related entity IDs, correlation IDs, task IDs, workflow IDs, or audit event IDs.

2. **Define a single filter state model**
   - Create or extend a strongly typed filter state object for:
     - `agent`
     - `department`
     - `task`
     - `eventType`
     - `status`
     - `timeframe`
   - Include parsing/serialization helpers for query-string values.
   - Normalize empty/default values so URL output is stable and minimal.
   - Ensure the model supports round-trip:
     - URL -> filter state
     - filter state -> URL

3. **Add/extend backend query support**
   - Update the timeline/activity feed query contract and handler so all required filters are supported with AND semantics.
   - Enforce tenant scoping first, then apply filters.
   - If `task` means task ID or task type, infer from existing UX/domain naming and keep it consistent across UI and API.
   - Add timeframe filtering using a clear bounded range or preset mapping.
   - Ensure result ordering remains deterministic, likely newest first.
   - Include enough detail in the response for drill-down:
     - summary
     - raw payload or structured payload source
     - correlation references
     - related entity IDs
     - audit event ID/linkable audit reference when available

4. **Build filter controls in Blazor**
   - Add UI controls to the timeline page/component for all required filters.
   - Prefer existing design system/components if present.
   - Populate filter options from existing data sources or enums where appropriate.
   - Make filter changes trigger data refresh.
   - Add clear/reset behavior.
   - Keep UX responsive and avoid full-page disruption if interactive patterns already exist.

5. **Implement URL query-string synchronization**
   - On initial load, hydrate filter state from the current URL query string.
   - On apply/clear, update the URL query string to reflect current state.
   - Prevent infinite loops between navigation updates and component state changes.
   - Preserve unrelated query-string values only if that matches existing app conventions; otherwise keep behavior explicit and consistent.
   - Support browser refresh/back/forward behavior.

6. **Implement activity detail drill-down**
   - Add click behavior on an activity item to open a detail view.
   - Reuse existing modal/drawer/page patterns in the web app.
   - Show:
     - raw payload
     - summary
     - correlation links
     - deep link to corresponding audit record when available
   - Correlation links should navigate to related task/workflow/approval/etc. only when valid routes exist.
   - If no audit record exists, omit or disable the audit link clearly.

7. **Implement same-tenant audit deep link**
   - Generate audit detail links using the existing route pattern.
   - Ensure the link resolves the correct audit detail page for the same tenant context.
   - If tenant context is route-based or query-based, follow the existing convention exactly.
   - Validate that clicking from an activity item with an audit record lands on the correct audit detail record.

8. **Add automated integration tests**
   - Add tests for at least the top 5 filter combinations, covering AND semantics. Example combinations:
     1. agent + timeframe
     2. department + status
     3. event type + timeframe
     4. agent + department + event type
     5. task + status + timeframe
   - Add tests for:
     - applying filters updates URL query string
     - clearing filters removes/reset query-string values
     - loading the page with query-string filters restores the same filtered view
     - clicking an activity item opens detail content
     - audit deep link appears when audit record exists
     - audit deep link navigates to the correct same-tenant audit detail page
   - Reuse existing test fixtures/builders and tenant-scoped test setup.

9. **Polish and align**
   - Keep naming aligned with backlog and domain language.
   - Avoid introducing duplicate DTOs/helpers if an existing one can be extended.
   - Add small comments only where behavior is non-obvious, especially around URL sync and filter parsing.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in the web app:
   - Open the timeline/activity feed page.
   - Apply each individual filter and confirm results change correctly.
   - Apply multi-filter combinations and confirm results satisfy all selected filters.
   - Refresh the page and confirm filter state persists from the URL.
   - Copy/share the URL and confirm the same filtered view loads.
   - Use browser back/forward and confirm filter state and results stay in sync.
   - Click an activity item and verify the detail view shows:
     - summary
     - raw payload
     - correlation links
     - audit deep link when available
   - Click the audit deep link and confirm it opens the correct audit detail page for the same tenant.

4. Code quality checks:
   - Confirm no tenant leakage in queries or links.
   - Confirm no raw chain-of-thought or sensitive internal reasoning is displayed.
   - Confirm null/missing audit references are handled gracefully.
   - Confirm query-string serialization is stable and does not produce unnecessary defaults.

# Risks and follow-ups
- **Ambiguity in `task` filter meaning**: it may refer to task ID, task type, or linked task. Inspect existing UX/domain contracts and choose the established meaning; document it in code if needed.
- **Existing timeline data may not expose audit linkage**: you may need to extend the query projection to include audit event identifiers or correlation metadata.
- **URL sync loops in Blazor**: take care to separate initial hydration from subsequent navigation updates.
- **Tenant-aware routing differences**: audit deep links must follow the app’s existing tenant resolution pattern exactly.
- **Integration test surface may be split**: if UI-level integration tests are not yet present, use the strongest existing test style available and add focused coverage without inventing a large new harness.
- **Timeframe semantics**: use explicit preset-to-range mapping and keep it consistent between UI labels and backend filtering.
- Follow-up candidate after completion:
  - save named filter presets
  - add pagination/infinite scroll state to URL
  - add richer correlation navigation across tasks/workflows/approvals