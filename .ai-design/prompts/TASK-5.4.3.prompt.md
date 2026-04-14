# Goal
Implement backlog task **TASK-5.4.3 — Add activity detail drawer with audit deep-link handling** for story **US-5.4 Filtering, drill-down, and audit deep linking** in the existing .NET solution.

Deliver a tenant-safe activity feed experience in the **Blazor Web App** that:
- supports filtering by **agent, department, task, event type, status, and timeframe**
- persists filter state in the **URL query string**
- opens an **activity detail drawer** when an item is clicked
- shows **raw payload, summary, correlation links, and audit deep link**
- deep-links to the correct **audit detail page for the same tenant** when an audit record exists
- includes **automated integration coverage** for top filter combinations and audit deep-link flow

Follow existing architecture boundaries:
- UI in `src/VirtualCompany.Web`
- application/query logic in `src/VirtualCompany.Application`
- persistence/query implementation in `src/VirtualCompany.Infrastructure`
- domain contracts/entities only if needed in `src/VirtualCompany.Domain`
- tests in `tests/VirtualCompany.Api.Tests` and/or existing web/integration test projects if present

# Scope
In scope:
- Extend the activity feed query model and backend query handling to support all required filters
- Ensure filter semantics are **AND across all selected filters**
- Add URL query-string synchronization for applying and clearing filters
- Add an activity detail drawer/panel UX in Blazor
- Populate detail view with:
  - raw payload
  - summary
  - correlation links
  - audit deep link when available
- Implement tenant-safe audit deep-link generation/resolution
- Add automated integration tests for:
  - at least 5 representative filter combinations
  - audit deep-link flow
- Keep behavior aligned with ST-602 auditability expectations and EP-6 tenant isolation rules

Out of scope:
- Reworking the entire audit module UX
- Mobile implementation
- New cross-module redesigns
- Broad refactors unrelated to activity feed filtering/detail behavior
- Exposing chain-of-thought or non-user-facing internal reasoning

# Files to touch
Likely files/modules to inspect and update first:

- `src/VirtualCompany.Web`
  - activity feed page/component
  - dashboard/recent activity components if feed lives there
  - routing/query-string helpers
  - detail drawer component
  - audit link navigation helpers
- `src/VirtualCompany.Application`
  - activity feed query DTOs/view models
  - query handlers/services for activity retrieval
  - filter contracts
  - detail DTO for activity item
- `src/VirtualCompany.Infrastructure`
  - EF/query repository implementations
  - SQL/linq filtering logic
  - tenant-scoped joins to audit records
- `src/VirtualCompany.Domain`
  - only if a missing enum/value object/contract is required for event type/status/filter modeling
- `tests/VirtualCompany.Api.Tests`
  - integration tests for filter combinations
  - audit deep-link flow tests
- Also inspect:
  - `README.md`
  - any existing activity/audit pages, route constants, query models, and test fixtures
  - any shared DTOs in `src/VirtualCompany.Shared`

If the exact activity feed implementation already exists elsewhere, modify the existing files rather than creating parallel components.

# Implementation plan
1. **Discover current implementation**
   - Find the existing activity feed page/component, backing API/query handler, and audit detail route.
   - Identify current activity item shape and whether audit correlation already exists in data.
   - Confirm how tenant context is resolved in web routes and backend queries.

2. **Define/extend filter contract**
   - Add or extend an activity feed filter request model with:
     - `AgentId`
     - `Department`
     - `TaskId`
     - `EventType`
     - `Status`
     - `FromUtc` / `ToUtc` or equivalent timeframe representation
   - Ensure null/empty values mean “not filtered”.
   - Keep filter semantics explicit: all provided filters must be applied cumulatively.

3. **Implement backend filtering**
   - Update application query handler and infrastructure query/repository logic to apply all selected filters with AND semantics.
   - Preserve tenant scoping on every query.
   - If department is derived through agent/task relationships, join safely and efficiently.
   - If timeframe is preset-based in UI, normalize it to concrete date bounds before querying or in the handler.

4. **Add activity detail query shape**
   - Extend the activity item/detail DTO to include:
     - summary
     - raw payload
     - correlation references/links
     - audit record identifier or audit route info when available
   - Prefer a dedicated detail DTO if list items should remain lightweight.

5. **Resolve audit deep-link metadata**
   - Determine how an activity event maps to an audit record:
     - direct `audit_event_id`
     - correlation ID
     - target/action/entity linkage
   - Implement a tenant-safe lookup that only returns an audit link for records in the current tenant.
   - Return enough route metadata for the UI to navigate to the correct audit detail page.
   - Do not generate links for missing or cross-tenant audit records.

6. **Build URL query-string synchronization**
   - On filter apply:
     - update the browser URL query string with current filter state
   - On filter clear:
     - remove/reset relevant query parameters
   - On initial page load:
     - hydrate filter UI state from query string
     - load feed using those values
   - Use stable, readable parameter names and avoid losing unrelated route context.

7. **Implement detail drawer UX**
   - Add click behavior on activity items to open a drawer/side panel/modal-style detail view.
   - Show:
     - summary
     - raw payload in readable formatted form
     - correlation links
     - audit deep link when available
   - Keep the drawer non-destructive to current feed state and filters.
   - If appropriate, support selected item state in URL or component state; prefer minimal change unless existing patterns dictate otherwise.

8. **Add audit deep-link navigation**
   - Render the audit link only when available.
   - Ensure navigation targets the existing audit detail page route for the same tenant/workspace context.
   - Verify route parameters and tenant context are preserved.
   - If the app uses route helpers/constants, centralize the link generation there.

9. **Integration tests**
   - Add automated tests covering at least 5 top filter combinations, for example:
     1. agent only
     2. department + status
     3. task + event type
     4. agent + department + timeframe
     5. event type + status + timeframe
   - Assert returned results match all selected filters, not any single filter.
   - Add audit deep-link flow test:
     - seed activity event with linked audit record in same tenant
     - verify detail payload includes working audit link metadata
     - verify cross-tenant audit records are not linked
   - If web UI integration tests exist, include URL query-string persistence and drawer interaction there; otherwise cover backend/API behavior robustly and add component tests if available.

10. **Polish and align**
   - Keep naming consistent with existing modules.
   - Avoid introducing duplicate DTOs/routes if equivalents already exist.
   - Ensure raw payload rendering is safe and escaped.
   - Keep explanations concise and operational.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Open activity feed page
   - Apply each filter individually and in combinations
   - Confirm results satisfy all selected filters
   - Refresh page and confirm filter state is restored from URL
   - Copy/share URL and confirm same filtered view loads
   - Click an activity item and confirm detail drawer opens
   - Verify drawer shows summary, raw payload, and correlation links
   - Verify audit deep link appears only when an audit record exists
   - Click audit deep link and confirm it opens the correct audit detail page for the same tenant

4. Tenant-safety verification:
   - Seed or simulate same identifiers across different tenants
   - Confirm activity queries never leak cross-tenant data
   - Confirm audit deep links are not produced for records outside current tenant

5. Regression checks:
   - Existing dashboard/activity pages still load
   - Clearing filters returns expected default feed
   - No broken navigation or malformed query strings

# Risks and follow-ups
- **Unknown existing route structure:** audit detail route naming may differ; inspect before implementing helpers.
- **Data model ambiguity:** activity-to-audit correlation may not be direct; choose the narrowest reliable mapping already supported by the schema.
- **Performance risk:** combining multiple filters plus joins may degrade feed performance; prefer indexed fields and efficient query composition.
- **UI state complexity:** query-string sync can conflict with existing component lifecycle/navigation patterns in Blazor; keep hydration/apply logic deterministic.
- **Test project limitations:** if current test setup is API-focused only, UI drawer/query-string behavior may need component tests or follow-up browser tests.
- **Follow-up candidates:**
  - add saved filter presets
  - support selected activity item in URL
  - add pagination/sorting assertions for filtered feeds
  - add explicit audit-link contract shared between activity and audit modules