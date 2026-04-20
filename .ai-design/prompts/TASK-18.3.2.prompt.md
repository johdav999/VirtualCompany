# Goal
Implement backlog task **TASK-18.3.2 — Build anomaly detail page with related record links and follow-up task references** for story **US-18.3 ST-FUI-203 — Finance anomaly workbench and follow-up actions** in the existing .NET solution.

Deliver a tenant-aware **finance anomaly workbench** in the **Blazor Web App** with:
- a dedicated route for listing anomalies from existing anomaly APIs
- filtering and paging/infinite loading for larger datasets
- row-level summary fields required by acceptance criteria
- a detail page for a selected anomaly
- related follow-up task references and navigation
- links to underlying invoice or transaction records
- preservation of current workbench filters when navigating to and from anomaly detail/task pages
- conditional rendering of deduplication metadata only when present

Use existing architecture patterns in the repo. Prefer CQRS-lite, typed application services, tenant-scoped API access, and Blazor SSR/interactivity only where needed.

# Scope
In scope:
- Add or extend web routes/pages/components for:
  - anomaly workbench list
  - anomaly detail page
- Integrate with existing anomaly APIs rather than inventing new backend anomaly generation logic
- Add/extend application/API contracts needed to support:
  - anomaly list query with filters
  - anomaly detail query
  - related follow-up task references
  - underlying record links/reference metadata
- Preserve list filter state across navigation
- Handle deduplication metadata presence/absence cleanly
- Add tests for query handling, API behavior, and key UI state behavior where practical

Out of scope unless required by existing code structure:
- Building new anomaly detection engines
- Redesigning task detail pages or invoice/transaction pages
- Mobile implementation
- New authorization model beyond existing tenant/role patterns
- Large visual redesign beyond fitting current web UI conventions

Acceptance criteria to satisfy explicitly:
1. Dedicated anomaly workbench route lists finance anomalies returned by existing anomaly APIs.
2. List supports filtering by:
   - anomaly type
   - status
   - confidence range
   - supplier
   - date window
   - and pagination or infinite loading for datasets over 50 items
3. Each row shows:
   - anomaly type
   - affected record reference
   - explanation summary
   - confidence
   - recommended action
   - deduplication key or window when available
   - current follow-up status
4. Selecting an anomaly opens detail page showing:
   - anomaly type
   - affected record
   - explanation
   - confidence
   - recommended action
   - related follow-up tasks
   - links to underlying invoice or transaction record
5. Users can open linked follow-up tasks from detail page and return to workbench without losing current filters.
6. When backend deduplication metadata is present, display anomaly key and detection window; when absent, hide the section without placeholder errors.

# Files to touch
Inspect first, then update the most relevant existing files in these areas.

Likely web/UI files:
- `src/VirtualCompany.Web/**`
  - routing/navigation setup
  - finance/anomaly pages
  - shared filter/paging components
  - task link/navigation helpers
  - any existing dashboard/finance workbench pages

Likely API files:
- `src/VirtualCompany.Api/**`
  - anomaly endpoints/controllers/minimal APIs
  - DTO mappings
  - tenant authorization wiring

Likely application layer files:
- `src/VirtualCompany.Application/**`
  - anomaly queries/handlers
  - task reference query models
  - record link DTOs
  - pagination/filter contracts

Likely infrastructure files:
- `src/VirtualCompany.Infrastructure/**`
  - repository/API client implementations for anomaly retrieval
  - data access for follow-up task references
  - mapping from persistence/integration models to application DTOs

Potential shared contracts:
- `src/VirtualCompany.Shared/**`
  - shared DTOs/view models if this repo uses shared contracts between API and Web

Tests:
- `tests/VirtualCompany.Api.Tests/**`
- any existing web/application test projects if present in solution

Also review:
- `README.md`
- solution/project conventions in:
  - `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
  - `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
  - `src/VirtualCompany.Web/VirtualCompany.Web.csproj`

# Implementation plan
1. **Discover existing anomaly/task/finance patterns**
   - Search the solution for:
     - `Anomaly`
     - `Finance`
     - `Task`
     - `Invoice`
     - `Transaction`
     - `Workbench`
   - Identify:
     - existing anomaly APIs and DTOs
     - current finance routes/pages
     - task detail route conventions
     - invoice/transaction detail route conventions
     - how tenant context is resolved
     - whether pagination patterns already exist
   - Reuse existing naming and folder structure rather than introducing a new pattern.

2. **Define/extend query contracts**
   - Add or extend application/API query models for anomaly list and detail.
   - List query should support:
     - `AnomalyType`
     - `Status`
     - `ConfidenceMin`
     - `ConfidenceMax`
     - `Supplier`
     - `DateFrom`
     - `DateTo`
     - paging params such as `PageNumber`/`PageSize` or cursor-based loading
   - Response item should include:
     - anomaly id
     - anomaly type
     - affected record reference
     - explanation summary
     - confidence
     - recommended action
     - follow-up status
     - optional deduplication key
     - optional detection window start/end
   - Detail response should include:
     - all key anomaly fields
     - full explanation
     - related follow-up tasks
     - underlying record links
     - optional deduplication metadata section

3. **Implement or extend application queries**
   - Create/extend query handlers in `VirtualCompany.Application` to:
     - fetch anomaly list from existing anomaly APIs/repositories
     - fetch anomaly detail by id
     - enrich detail with related follow-up tasks
     - include invoice/transaction link metadata
   - Keep tenant scoping enforced in all queries.
   - If anomalies come from an existing backend API client, adapt there instead of duplicating logic.
   - If follow-up tasks are linked by anomaly id, dedup key, record reference, or source metadata, use the existing domain relationship pattern and document assumptions in code comments only where necessary.

4. **Implement API endpoints**
   - Add or extend API endpoints in `VirtualCompany.Api` for:
     - `GET /api/finance/anomalies`
     - `GET /api/finance/anomalies/{id}`
     - or align to existing route conventions if different
   - Ensure:
     - tenant-aware authorization
     - filter binding/validation
     - safe handling of missing optional dedup metadata
     - paged response shape for >50 items
   - Return DTOs tailored for the web UI, or map application DTOs consistently with existing API style.

5. **Build the anomaly workbench page**
   - Add a dedicated Blazor route, likely under a finance area, for example:
     - `/finance/anomalies`
   - Implement:
     - filter UI for anomaly type, status, confidence range, supplier, date window
     - list/table rendering
     - paging or infinite loading
   - Each row must show:
     - anomaly type
     - affected record reference
     - explanation summary
     - confidence
     - recommended action
     - deduplication key or window when available
     - current follow-up status
   - Make row selection navigate to detail page.
   - Preserve filter state in URL query string whenever possible so browser back/forward and deep links work naturally.

6. **Build the anomaly detail page**
   - Add a route such as:
     - `/finance/anomalies/{anomalyId}`
   - Render:
     - anomaly type
     - affected record
     - explanation
     - confidence
     - recommended action
     - related follow-up tasks
     - links to underlying invoice or transaction record
   - Deduplication metadata:
     - show section only if key and/or detection window exists
     - do not render placeholder labels, empty boxes, or error text when absent
   - Include a clear “Back to workbench” action that restores prior filters from query string or navigation state.

7. **Implement navigation state preservation**
   - Preserve current workbench filters when navigating:
     - from workbench -> anomaly detail
     - from anomaly detail -> follow-up task
     - back to anomaly workbench
   - Preferred approach:
     - encode workbench filter state in query string
     - pass return URL/query string to detail/task links
   - Ensure users can open linked follow-up tasks and still return without losing filters.
   - Reuse any existing navigation helper/state container if the repo already has one.

8. **Link related follow-up tasks and source records**
   - On detail page, render related follow-up tasks as links using existing task route conventions.
   - Render underlying invoice/transaction links using existing record detail routes if available.
   - If only references exist and no detail route exists yet, render safe non-broken references and note the limitation in follow-ups.
   - Avoid hardcoding URLs if route helpers or named routes already exist.

9. **Handle empty/loading/error states**
   - Workbench:
     - loading state
     - empty state for no anomalies
     - safe error state for API failures
   - Detail:
     - loading state
     - not found state
     - safe handling when related tasks or record links are absent
   - Dedup metadata absence must not produce null reference errors or placeholder artifacts.

10. **Add tests**
   - Application/API tests:
     - anomaly list filtering binds and returns expected shape
     - anomaly detail returns related tasks and record links
     - dedup metadata omitted cleanly when absent
     - tenant scoping enforced
   - Web/component tests if test infrastructure exists:
     - filter query string round-trip
     - back navigation preserves filters
     - dedup section hidden when metadata absent
   - If web component tests are not already set up, prioritize API/application coverage and keep UI logic simple and deterministic.

11. **Keep implementation aligned with repo conventions**
   - Follow existing patterns for:
     - MediatR or query handlers if used
     - DTO naming
     - result wrappers
     - pagination models
     - authorization attributes/policies
     - Blazor component composition/styling
   - Do not introduce a new architectural pattern for this task.

# Validation steps
1. **Codebase inspection**
   - Confirm existing anomaly APIs and route conventions before coding.
   - Verify whether pagination model already exists and reuse it.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual web validation**
   - Start the app using the repo’s normal local run flow.
   - Navigate to the new anomaly workbench route.
   - Verify:
     - anomalies load from existing API
     - filters update results
     - datasets over 50 items can page/load more
     - each row shows all required fields
   - Open an anomaly detail page and verify:
     - all required detail fields render
     - related follow-up tasks are linked
     - invoice/transaction links are present when available
     - dedup section appears only when metadata exists
   - Navigate:
     - workbench -> detail -> follow-up task -> back
     - confirm original filters remain intact

5. **Edge-case validation**
   - No anomalies returned
   - anomaly without dedup metadata
   - anomaly without related follow-up tasks
   - anomaly with invoice link vs transaction link
   - invalid anomaly id / unauthorized tenant access

6. **Regression check**
   - Ensure no existing finance/task routes are broken.
   - Ensure tenant scoping and authorization still behave correctly.

# Risks and follow-ups
- **Risk: existing anomaly APIs may not expose all fields needed**
  - Mitigation: inspect current contracts first; extend API/application mapping only as needed.
- **Risk: follow-up task linkage may be indirect or inconsistent**
  - Mitigation: centralize linkage logic in application layer and document assumptions.
- **Risk: record detail routes may not yet exist for both invoice and transaction**
  - Mitigation: use existing routes where available; otherwise render safe references and note a follow-up.
- **Risk: filter preservation can become brittle if handled only in component state**
  - Mitigation: prefer URL query string as source of truth.
- **Risk: large datasets may cause poor UX if full list is loaded eagerly**
  - Mitigation: use existing pagination/infinite loading pattern and server-side filtering.

Suggested follow-ups if gaps are discovered:
- Add reusable finance workbench filter state helper for other finance pages.
- Standardize anomaly-to-task linkage metadata in backend contracts if currently ad hoc.
- Add richer anomaly status badges and supplier lookup/autocomplete if supplier lists are large.
- Add component/integration tests for Blazor navigation state if the repo later introduces UI test infrastructure.