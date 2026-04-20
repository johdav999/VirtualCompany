# Goal

Implement backlog task **TASK-17.3.2 — Build invoices list and detail UI with approval context rendering** for story **US-17.3 ST-FUI-173 — Transactions and invoices workspace with detail drilldowns and linked documents**.

Deliver a tenant-aware Blazor web workspace experience that:

- Lists **finance transactions** with filters for:
  - date range
  - category
  - flagged state
- Opens a **transaction detail view** on selection showing:
  - category
  - flags
  - anomaly state
  - linked document metadata when available
- Lists **invoices** with:
  - approval or review status
  - amount
  - supplier
- Opens an **invoice detail view** on selection showing:
  - status
  - amount
  - supplier
  - linked workflow or approval context when available
- Renders linked documents in transaction and invoice detail views with navigation to the document view when the user has access
- Shows a clear, non-blocking status message when linked documents are missing or inaccessible

Use existing architecture and conventions in the repo. Prefer extending current application/query/UI patterns over inventing new ones.

# Scope

In scope:

- Blazor Web UI for transactions and invoices workspace pages/components
- Query/application layer support needed to populate the UI
- API endpoints only if the current web app architecture requires them
- Tenant-scoped filtering and authorization-aware rendering
- Linked document metadata rendering and conditional navigation
- Approval/workflow context rendering for invoice detail
- Empty/loading/error/non-blocking unavailable states
- Tests covering query/application behavior and any critical UI logic

Out of scope unless required by existing patterns:

- Mobile app changes
- New domain model redesigns unrelated to this task
- Full document viewer implementation
- New approval workflow creation/editing flows
- New transaction/invoice ingestion pipelines
- Broad refactors outside the finance workspace area

# Files to touch

Inspect the solution first and then update the actual matching files. Expected areas:

- `src/VirtualCompany.Web/**`
  - finance workspace pages
  - transactions list/detail components
  - invoices list/detail components
  - shared status/message components if needed
  - navigation links to documents/workflows/approvals
- `src/VirtualCompany.Application/**`
  - queries/read models for transactions and invoices
  - DTO/view model shaping for detail drilldowns
  - tenant-scoped query handlers
- `src/VirtualCompany.Api/**`
  - controller/endpoints only if the web app consumes API endpoints for these screens
- `src/VirtualCompany.Domain/**`
  - only minimal additions if existing domain/read contracts are missing required fields
- `src/VirtualCompany.Infrastructure/**`
  - query/repository/data access updates for transaction/invoice read models
- `src/VirtualCompany.Shared/**`
  - shared contracts if used between layers
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query integration tests if applicable
- Add tests in the appropriate test project(s) already used by the solution for:
  - application query behavior
  - authorization/access-state rendering logic
  - filter behavior

Before coding, locate any existing finance, transactions, invoices, approvals, workflow, and documents UI/query code and build on it.

# Implementation plan

1. **Discover existing patterns and map the feature entry points**
   - Search for:
     - transactions pages/components
     - invoices pages/components
     - finance workspace routes/navigation
     - document detail/view routes
     - approval/workflow detail routes
     - existing query handlers and DTOs
   - Identify whether the web app uses:
     - direct application service injection
     - API-backed calls
     - SSR + interactive components
   - Reuse the established pattern.

2. **Define/extend read models for the required UI**
   - Ensure transaction list items include:
     - id
     - date
     - category
     - amount if already standard in the workspace
     - flagged state
     - anomaly state summary if available
   - Ensure transaction detail includes:
     - category
     - flags
     - anomaly state
     - linked document metadata:
       - document id
       - title/file name
       - type
       - availability/access state
       - navigation target if accessible
   - Ensure invoice list items include:
     - id
     - invoice number/reference if available
     - approval/review status
     - amount
     - supplier
   - Ensure invoice detail includes:
     - status
     - amount
     - supplier
     - linked workflow/approval context:
       - workflow id/name/state when available
       - approval id/status/required role or assignee summary when available
     - linked document metadata with access state if invoices also support linked docs in current model
   - Model unavailable linked resources explicitly rather than relying on null-only behavior. Prefer a small status enum/string such as:
     - available
     - missing
     - inaccessible

3. **Implement or extend tenant-scoped queries**
   - Add/update application queries for:
     - transaction list with filters:
       - date from
       - date to
       - category
       - flagged state
     - transaction detail by id
     - invoice list
     - invoice detail by id
   - Enforce company/tenant scoping in all queries.
   - Keep authorization-aware shaping in the application layer where possible:
     - if user can access linked document, return navigation target
     - if not, return inaccessible status and no actionable link
   - For missing linked documents, return a non-error status payload so the UI can render a non-blocking message.

4. **Wire linked document access behavior**
   - Reuse existing authorization/policy checks for document access.
   - In transaction and invoice detail responses:
     - include document metadata when resolvable
     - include `CanNavigate`/`IsAccessible` style field
     - include a user-facing status message for missing/inaccessible cases if your existing DTO conventions support it
   - Do not fail the entire detail page because a linked document is unavailable.

5. **Build/update the transactions page UI**
   - Add or complete filter controls for:
     - date range
     - category
     - flagged state
   - Render a list/table of transactions.
   - On selection, open the detail view using the existing UX pattern:
     - split pane
     - side panel
     - nested route
     - detail section
   - Show:
     - category
     - flags
     - anomaly state
     - linked document metadata block
   - If linked document is accessible, render a navigation link/button to the document view.
   - If missing/inaccessible, render a clear informational message that does not block the rest of the detail content.

6. **Build/update the invoices page UI**
   - Render invoice list with:
     - approval/review status
     - amount
     - supplier
   - On selection, open invoice detail.
   - Show:
     - status
     - amount
     - supplier
     - linked workflow or approval context when available
   - Render approval/workflow context in a concise, readable section:
     - workflow name/state link if accessible and route exists
     - approval status and summary link if accessible and route exists
   - If linked document metadata exists for invoices in the current model, apply the same access-aware rendering pattern as transactions.

7. **Handle UX states cleanly**
   - Add/verify:
     - loading state
     - empty list state
     - no selection state
     - detail not found state
     - non-blocking linked resource unavailable state
   - Keep messages concise and user-friendly, e.g.:
     - “Linked document is no longer available.”
     - “You do not have access to the linked document.”
   - Avoid surfacing raw authorization or storage errors.

8. **Navigation integration**
   - Ensure linked document navigation points to the existing document view route.
   - Ensure workflow/approval links point to existing routes if available.
   - If a target route does not yet exist, render plain metadata/status without a broken link.

9. **Testing**
   - Add tests for application/query behavior:
     - transaction filters work as expected
     - invoice list/detail returns required fields
     - linked document states are shaped correctly:
       - available
       - missing
       - inaccessible
     - tenant scoping prevents cross-company access
   - Add UI/component tests if the repo already uses them; otherwise cover critical rendering logic at the application/API level.
   - Do not introduce a new testing framework unless already present.

10. **Keep implementation aligned with repo conventions**
    - Follow naming, folder structure, MediatR/CQRS-lite, DTO, and Blazor component patterns already in use.
    - Prefer incremental additions over broad abstractions.
    - Keep nullability and async patterns consistent.
    - Add minimal comments only where logic is non-obvious.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Open transactions workspace
   - Confirm filters exist for:
     - date range
     - category
     - flagged state
   - Confirm transaction selection opens detail and shows:
     - category
     - flags
     - anomaly state
     - linked document metadata when available
   - Confirm accessible linked document renders navigation to document view
   - Confirm missing/inaccessible linked document shows a clear non-blocking message

4. Manually verify invoices:
   - Open invoices workspace
   - Confirm list shows:
     - approval/review status
     - amount
     - supplier
   - Confirm invoice selection opens detail and shows:
     - status
     - amount
     - supplier
     - linked workflow or approval context when available

5. Authorization/tenant checks:
   - Verify cross-tenant data is not returned
   - Verify inaccessible linked documents do not expose sensitive metadata beyond allowed status messaging
   - Verify page still renders when linked resources are unavailable

6. Regression check:
   - Confirm existing finance/document/approval navigation still works
   - Confirm no broken routes or null-reference errors in empty-state scenarios

# Risks and follow-ups

- **Repo structure may differ from assumptions**
  - Mitigation: inspect existing finance/document/approval patterns first and adapt the plan to actual architecture.

- **Linked document access rules may already be centralized**
  - Mitigation: reuse existing authorization services/policies instead of duplicating checks in UI code.

- **Workflow/approval links may not yet have stable routes**
  - Mitigation: render context metadata without links when routes are unavailable.

- **Data model may not yet expose all required invoice/transaction fields**
  - Mitigation: add minimal read-model/query extensions only; avoid broad schema changes unless absolutely necessary.

- **Blazor interaction model may be SSR-first**
  - Mitigation: implement selection/detail behavior using the least complex pattern already used in the app.

- **Potential follow-up tasks**
  - richer invoice/transaction sorting and paging
  - shared linked-resource status component
  - deeper approval timeline rendering
  - document preview affordances in detail panels
  - audit trail integration for transaction/invoice drilldowns