# Goal
Implement backlog task **TASK-17.3.1 — Build transactions list and filter UI with detail panel routing** for story **US-17.3 ST-FUI-173 — Transactions and invoices workspace with detail drilldowns and linked documents** in the existing **.NET / Blazor Web App** solution.

Deliver a finance workspace UI that:
- Lists **transactions** with filters for **date range**, **category**, and **flagged state**
- Opens a **transaction detail panel/view via routing** when a transaction is selected
- Lists **invoices** with **approval/review status**, **amount**, and **supplier**
- Opens an **invoice detail panel/view via routing** when an invoice is selected
- Shows linked document/workflow metadata when available
- Provides navigation to linked documents only when the user has access
- Shows a clear, non-blocking message when linked documents are missing or inaccessible

Use existing architectural patterns in the repo where possible:
- Blazor Web App for UI
- ASP.NET Core application/API layer for queries
- CQRS-lite query flow
- Tenant-scoped access
- Clean separation between Web, Application, Infrastructure, and Shared contracts

# Scope
In scope:
- Add or extend finance workspace pages/components in `VirtualCompany.Web`
- Add query models/endpoints/services needed to populate:
  - transactions list + filters
  - transaction detail
  - invoices list
  - invoice detail
- Add route-driven detail panel behavior for selected transaction/invoice
- Add linked document metadata rendering and guarded navigation behavior
- Add empty/loading/error states for list and detail views
- Add tests for query handlers and/or API endpoints and basic UI routing behavior if test patterns already exist

Out of scope unless already trivial and required by existing patterns:
- Full backend finance domain creation from scratch if no finance entities exist
- Editing transactions or invoices
- Approval action workflows
- Mobile implementation
- New authentication/authorization framework work
- Large visual redesign outside the finance workspace

Assume this task may require creating **read models / stubbed query sources** if the finance module is partially implemented, but prefer integrating with existing domain/data structures first.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Web/`
  - Finance workspace pages, components, layouts, nav, and route definitions
  - Shared UI components for filter bars, split views, detail panels, status badges, empty states
- `src/VirtualCompany.Api/`
  - Finance query endpoints/controllers/minimal APIs if the web app consumes API endpoints
- `src/VirtualCompany.Application/`
  - Query contracts, DTOs, handlers, validators
  - Tenant-scoped finance read services
- `src/VirtualCompany.Infrastructure/`
  - Query implementations / EF or SQL access for finance read models
- `src/VirtualCompany.Shared/`
  - Shared DTOs/contracts if used between API and Web
- `src/VirtualCompany.Domain/`
  - Only if small additions are needed for enums/value objects already implied by finance records
- `tests/VirtualCompany.Api.Tests/`
  - Endpoint/query handler tests
- Potentially other existing test projects if there are Web/Application test suites

Before coding, inspect:
- Existing finance-related pages/components/endpoints
- Existing patterns for:
  - list/detail routing
  - query handlers
  - tenant scoping
  - authorization checks
  - linked document navigation
  - status badges and filter controls

# Implementation plan
1. **Discover existing finance and document patterns**
   - Search the solution for:
     - `Transaction`, `Invoice`, `Finance`, `Document`, `Approval`, `Workflow`
     - existing workspace pages and split-panel routing patterns
   - Reuse established naming, folder structure, DTO style, and endpoint conventions
   - Identify whether the Blazor app calls the API over HTTP or uses direct application services

2. **Define read models for the workspace**
   Create or extend query DTOs for:
   - `TransactionListItem`
     - id
     - transaction date
     - description/reference
     - category
     - amount/currency if available
     - flagged state
     - anomaly state summary if available
   - `TransactionDetailDto`
     - id
     - category
     - flags
     - anomaly state
     - linked document metadata
     - document accessibility state
   - `InvoiceListItem`
     - id
     - invoice number/reference
     - status (approval/review)
     - amount
     - supplier
     - due date if available
   - `InvoiceDetailDto`
     - id
     - status
     - amount
     - supplier
     - linked workflow/approval context
     - linked document metadata
     - accessibility state
   - Filter DTO for transactions:
     - from date
     - to date
     - category
     - flagged state (`all`, `flagged`, `not_flagged`)

3. **Implement application-layer queries**
   Add CQRS-lite queries and handlers for:
   - get transactions list with filters
   - get transaction detail by id
   - get invoices list
   - get invoice detail by id
   Requirements:
   - enforce tenant scoping
   - return only records visible to the current company/user context
   - include linked document metadata only as allowed
   - expose a clear status for:
     - linked document available
     - linked document missing
     - linked document inaccessible

4. **Implement infrastructure data access**
   - Wire handlers to existing persistence models
   - If finance tables/entities already exist, query them directly
   - If linked document/workflow/approval data exists, join/project only the metadata needed for the UI
   - Do not leak inaccessible document identifiers/URLs beyond what current access rules allow
   - If access checks are already centralized, call that service rather than duplicating logic

5. **Expose API/query surface**
   Depending on repo pattern:
   - Add/extend API endpoints for:
     - `GET /finance/transactions`
     - `GET /finance/transactions/{id}`
     - `GET /finance/invoices`
     - `GET /finance/invoices/{id}`
   Or equivalent route structure already used in the project
   Ensure:
   - tenant context is enforced
   - invalid cross-tenant ids return forbidden/not found per existing conventions
   - filter query params are validated

6. **Build transactions list UI**
   In Blazor Web:
   - Create/update transactions workspace page
   - Add filter controls:
     - date range start/end
     - category select
     - flagged state select/toggle
   - Render list with key columns:
     - date
     - category
     - amount/reference if available
     - flagged indicator
   - Support loading, empty, and error states
   - Keep filter state reflected in URL query string if consistent with existing app patterns

7. **Add route-driven transaction detail panel**
   - Implement selection so clicking a transaction updates the route
   - Use nested route, query param, or split-view route pattern already present in the app
   - Detail panel/view must show:
     - category
     - flags
     - anomaly state
     - linked document metadata when available
   - If linked document is accessible, render navigation action
   - If missing/inaccessible, render a clear non-blocking status message instead of a broken link

8. **Build invoices list UI**
   - Create/update invoices workspace page
   - Render list with:
     - approval/review status
     - amount
     - supplier
   - Add loading, empty, and error states
   - Keep selection behavior consistent with transactions page

9. **Add route-driven invoice detail panel**
   - Clicking an invoice updates the route and opens detail panel/view
   - Show:
     - status
     - amount
     - supplier
     - linked workflow or approval context when available
     - linked document metadata when available
   - Guard linked document navigation by access
   - Show non-blocking status message for missing/inaccessible linked documents

10. **Document navigation behavior**
    - Reuse existing document route/navigation helper if present
    - Only render active navigation when access is confirmed
    - For inaccessible documents:
      - show a neutral message like “Linked document unavailable or you do not have access.”
      - do not block the rest of the detail view
    - For missing documents:
      - show a neutral message like “Linked document is no longer available.”

11. **Polish UX consistency**
    - Use existing badge/chip components for:
      - flagged state
      - anomaly state
      - invoice status
      - document availability
    - Ensure keyboard and screen-reader friendly selection/navigation
    - Preserve selected item state on refresh when route contains selected id
    - Ensure direct navigation to detail route loads both list and selected detail correctly

12. **Add tests**
    Add tests aligned to existing project patterns:
    - query handler or API tests for transaction filters
    - tenant isolation tests
    - detail query tests for linked document accessibility states
    - invoice detail tests for workflow/approval context projection
    - if web/component tests exist, add route-selection/detail rendering coverage

13. **Keep implementation incremental**
    Preferred delivery order:
    1. backend read models + endpoints
    2. transactions list + filters
    3. transaction detail routing
    4. invoices list
    5. invoice detail routing
    6. linked document guarded navigation
    7. tests and cleanup

# Validation steps
1. Inspect and build before changes:
   - `dotnet build`

2. After implementation, run:
   - `dotnet build`
   - `dotnet test`

3. Manually validate in the web app:
   - Open transactions page
   - Confirm transactions list renders
   - Apply date range filter and verify results change
   - Apply category filter and verify results change
   - Apply flagged state filter and verify results change
   - Select a transaction and confirm route updates and detail panel/view opens
   - Refresh on a selected transaction route and confirm detail still loads
   - Verify transaction detail shows category, flags, anomaly state, and linked document metadata when present
   - Verify accessible linked document renders a working navigation action
   - Verify missing/inaccessible linked document shows a clear non-blocking message

4. Validate invoices flow:
   - Open invoices page
   - Confirm invoices list renders status, amount, and supplier
   - Select an invoice and confirm route updates and detail panel/view opens
   - Refresh on a selected invoice route and confirm detail still loads
   - Verify invoice detail shows status, amount, supplier, and linked workflow/approval context when present
   - Verify linked document navigation/accessibility behavior matches acceptance criteria

5. Validate security/tenancy:
   - Confirm cross-tenant ids are not exposed
   - Confirm inaccessible linked documents do not expose sensitive navigation targets
   - Confirm unauthorized access follows existing forbidden/not found conventions

6. If URL query state is used for filters:
   - verify back/forward navigation preserves filter and selection state
   - verify sharable URLs restore the same workspace state

# Risks and follow-ups
- **Unknown existing finance model**: The repo may not yet have full transaction/invoice persistence. If so, implement read-side contracts cleanly and use the smallest viable backing source consistent with current architecture.
- **Routing pattern mismatch**: The app may already use a specific split-panel or nested-route convention. Follow that pattern rather than inventing a new one.
- **Authorization complexity for linked documents**: Access checks must be centralized and tenant-aware. Reuse existing document authorization logic if available.
- **Data shape gaps**: Anomaly state, flags, workflow context, or linked document metadata may not all exist yet. If missing, project nullable fields and render graceful empty states without blocking the task.
- **Filter UX consistency**: If the app already has a standard filter bar/query-string sync pattern, use it for consistency.
- **Potential follow-up tasks**:
  - add paging/sorting for large transaction and invoice lists
  - add saved filters
  - add richer approval/workflow drilldowns
  - add component tests for split-view routing
  - add loading skeletons and telemetry for finance workspace interactions