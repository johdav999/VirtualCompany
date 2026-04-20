# Goal
Implement backlog task **TASK-18.3.1 — Finance anomaly list page with filters, pagination, and persisted URL state** for story **US-18.3 ST-FUI-203 — Finance anomaly workbench and follow-up actions**.

Deliver a production-ready **finance anomaly workbench** in the Blazor web app that:
- exposes a dedicated route for anomaly browsing
- loads anomalies from existing backend anomaly APIs
- supports filterable, paginated browsing for larger datasets
- persists filter and paging state in the URL so users can navigate away and return without losing context
- provides anomaly detail navigation with links to related follow-up tasks and underlying finance records
- conditionally renders deduplication metadata only when present

Use the existing architecture and conventions in this repository. Prefer extending current query/API/UI patterns over inventing new ones.

# Scope
In scope:
- Add or complete a dedicated finance anomaly workbench route in `VirtualCompany.Web`
- Integrate with existing anomaly APIs in `VirtualCompany.Api` / application layer if already present
- If the API surface is incomplete, add the minimal query endpoint(s) needed to support:
  - list retrieval
  - filtering by:
    - anomaly type
    - status
    - confidence range
    - supplier
    - date window
  - pagination for datasets over 50 items
- Add anomaly detail page/navigation
- Persist current filter and paging state in URL query parameters
- Ensure returning from anomaly detail or linked follow-up task preserves workbench state
- Render anomaly rows with all required fields
- Render deduplication metadata section only when backend data exists
- Add/update tests for query binding, filtering, pagination, and conditional rendering

Out of scope:
- Creating new anomaly detection logic
- Reworking unrelated finance pages
- Mobile implementation
- Major redesign of backend anomaly domain contracts unless required to satisfy acceptance criteria
- Infinite scrolling if standard pagination is simpler and consistent with current app patterns

Implementation preference:
- **Use pagination rather than infinite loading unless the codebase already has a reusable infinite-load pattern**
- **Use URL query string state as the source of truth for filters/page**
- **Preserve tenant scoping and authorization patterns already used by the app**

# Files to touch
Inspect the repo first, then update the exact files that fit existing conventions. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance/anomaly list page component
  - finance/anomaly detail page component
  - shared filter/pagination components if needed
  - navigation helpers / query-string state helpers
- `src/VirtualCompany.Api/**`
  - anomaly controller/endpoints if list/detail query endpoints are missing or incomplete
- `src/VirtualCompany.Application/**`
  - anomaly list/detail query handlers
  - DTOs/view models for list rows and detail payloads
- `src/VirtualCompany.Domain/**`
  - only if existing contracts need small additions for deduplication metadata or follow-up references
- `src/VirtualCompany.Shared/**`
  - shared contracts if web and API already communicate through shared DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - API/query tests for filtering, pagination, and tenant-safe access
- Add web/UI tests if the solution already contains a pattern for Blazor component or integration tests

Before coding, identify the actual existing anomaly-related files and follow those naming and placement conventions instead of creating parallel structures.

# Implementation plan
1. **Discover existing anomaly implementation**
   - Search the solution for:
     - `Anomaly`
     - finance workbench pages
     - follow-up task links
     - invoice/transaction detail links
   - Determine:
     - existing anomaly API endpoints
     - current DTOs/contracts
     - whether there is already a finance area/route structure
     - whether pagination/filter helpers already exist in the web app

2. **Define/confirm list and detail contracts**
   - Ensure the list payload supports:
     - anomaly id
     - anomaly type
     - affected record reference
     - explanation summary
     - confidence
     - recommended action
     - deduplication key
     - detection window
     - current follow-up status
   - Ensure detail payload supports:
     - anomaly type
     - affected record
     - explanation
     - confidence
     - recommended action
     - related follow-up tasks
     - links to underlying invoice or transaction record
     - optional deduplication metadata
   - Keep contracts additive and backward-compatible where possible

3. **Implement/complete backend list query**
   - Add or extend a query endpoint for anomaly workbench listing
   - Support filters:
     - anomaly type
     - status
     - confidence min/max
     - supplier
     - date from/to
   - Support pagination with explicit page/pageSize or cursor pattern
   - Return total count or enough metadata for pager rendering
   - Enforce tenant scoping
   - Reuse CQRS-lite query patterns already present in the application layer

4. **Implement/complete backend detail query**
   - Add or extend anomaly detail endpoint by anomaly id
   - Include related follow-up tasks and underlying record links
   - Ensure missing deduplication metadata is represented as absent/null data, not placeholder strings

5. **Build the anomaly workbench page**
   - Add a dedicated route, likely under the finance area, e.g. `/finance/anomalies` if consistent with current routing
   - Render:
     - filter controls
     - results table/list
     - pagination controls
   - Each row must show:
     - anomaly type
     - affected record reference
     - explanation summary
     - confidence
     - recommended action
     - deduplication key or window when available
     - current follow-up status
   - Make each row selectable/clickable to open detail page

6. **Persist filter and paging state in URL**
   - Bind filter state to query string parameters
   - On filter change:
     - update URL
     - reset page to first page where appropriate
     - reload data
   - On initial page load:
     - hydrate UI state from URL
   - On returning from detail page:
     - restore state from URL without relying on in-memory component state only
   - Prefer a small reusable helper if the app lacks one

7. **Implement anomaly detail page**
   - Add route for anomaly detail, likely `/finance/anomalies/{id}`
   - Render:
     - anomaly type
     - affected record
     - explanation
     - confidence
     - recommended action
     - related follow-up tasks
     - links to invoice/transaction record
   - Include a “Back to workbench” action that preserves prior query string state
   - If navigation to detail originates from the list, pass the current workbench URL or query string as a return target

8. **Support follow-up task navigation without losing workbench context**
   - From anomaly detail, users must be able to open linked follow-up tasks
   - Preserve a return path back to anomaly detail and/or workbench
   - At minimum, ensure the workbench URL with filters remains available and functional after navigating back
   - Use query parameters like `returnUrl` only if consistent with existing navigation/security patterns; avoid open redirect issues by restricting to local app-relative URLs

9. **Conditional deduplication metadata rendering**
   - In list rows and detail page:
     - show deduplication key and detection window only when present
     - hide the section entirely when absent
   - Do not render placeholder labels, empty boxes, or error text for missing metadata

10. **UX and resilience**
   - Add loading, empty, and error states
   - Empty state should clearly indicate no anomalies match current filters
   - Keep filter UX responsive and accessible
   - Format confidence and dates consistently with existing app conventions

11. **Testing**
   - Add/extend backend tests for:
     - filter combinations
     - pagination behavior
     - tenant isolation
     - detail retrieval
   - Add UI/component/integration tests if supported for:
     - query-string hydration
     - URL updates on filter changes
     - back navigation preserving state
     - deduplication section hidden when absent

12. **Keep implementation aligned with repository conventions**
   - Follow existing naming, folder structure, MediatR/query patterns, DTO style, and Blazor component patterns
   - Avoid introducing a new frontend state library or custom infrastructure unless already used

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Manual verification in the web app:
   - Navigate to the finance anomaly workbench route
   - Confirm anomalies load from existing APIs
   - Apply each filter individually:
     - anomaly type
     - status
     - confidence range
     - supplier
     - date window
   - Apply multiple filters together and verify results update correctly
   - Verify pagination works when result set exceeds 50 items
   - Refresh the page and confirm filters/page are restored from URL
   - Copy/paste the filtered URL into a new tab and confirm the same state loads
   - Open an anomaly detail page and verify all required fields render
   - Open linked follow-up task(s), then navigate back and confirm workbench filters remain intact
   - Verify invoice/transaction links are present when available
   - Verify deduplication metadata:
     - present data renders correctly
     - absent data hides the section cleanly with no placeholder errors

3. API verification:
   - Confirm list endpoint returns paginated, tenant-scoped results
   - Confirm detail endpoint returns related follow-up tasks and underlying record links
   - Confirm null/absent deduplication metadata does not break serialization or UI rendering

4. Regression checks:
   - Ensure no unauthorized cross-tenant anomaly access
   - Ensure existing finance pages/routes still function
   - Ensure browser back/forward navigation behaves correctly with query-string state

# Risks and follow-ups
- **Unknown existing anomaly API shape**
  - Risk: current endpoints may not expose all fields needed by acceptance criteria
  - Mitigation: extend contracts minimally and keep changes additive

- **Return navigation complexity**
  - Risk: preserving workbench state across anomaly detail and follow-up task pages can become brittle
  - Mitigation: use URL-based state and safe local `returnUrl` patterns only where necessary

- **Pagination contract mismatch**
  - Risk: existing APIs may use a different paging model than the web app expects
  - Mitigation: adapt UI to current standard if one exists; otherwise implement a simple page/pageSize model

- **Optional deduplication metadata**
  - Risk: backend may return partial metadata inconsistently
  - Mitigation: treat all deduplication fields as optional and render defensively

- **Supplier/date filtering semantics**
  - Risk: ambiguity around supplier identifier vs display name, and date field meaning
  - Mitigation: inspect existing anomaly domain/API docs and use the already-established finance record semantics

Follow-ups if not already covered in this task:
- Add saved views/presets for common anomaly filter combinations
- Add sortable columns if product needs triage optimization
- Add richer anomaly detail timeline/audit trail
- Add reusable query-string state helper for other workbench-style pages