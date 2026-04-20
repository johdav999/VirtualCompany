# Goal
Implement backlog task **TASK-17.3.3 — Integrate linked document metadata and document drilldown actions into finance detail views** for story **US-17.3 ST-FUI-173**.

Deliver finance workspace enhancements in the **Blazor web app** and supporting **ASP.NET Core application/API layers** so that:

- Transactions page lists finance transactions with filters for:
  - date range
  - category
  - flagged state
- Selecting a transaction opens a detail view showing:
  - category
  - flags
  - anomaly state
  - linked document metadata when available
- Invoices page lists invoices with:
  - approval/review status
  - amount
  - supplier
- Selecting an invoice opens a detail view showing:
  - status
  - amount
  - supplier
  - linked workflow or approval context when available
- Linked documents in transaction and invoice detail views provide navigation to the document view when the user has access
- Missing or inaccessible linked documents render a clear, non-blocking status message

Use the existing project structure and patterns already present in the repository. Prefer extending current finance/detail/document patterns over inventing new ones.

# Scope
In scope:

- Find the existing finance transactions/invoices workspace implementation and extend it
- Add or complete query models/view models needed to surface:
  - linked document metadata
  - document accessibility state
  - document navigation target
  - linked workflow/approval context for invoices
- Add server-side query/application logic to populate finance detail views from tenant-scoped data
- Enforce tenant-aware and permission-aware handling for linked documents
- Update Blazor pages/components for:
  - transactions list filters
  - transaction detail drilldown
  - invoice list
  - invoice detail drilldown
  - linked document action rendering
  - non-blocking missing/inaccessible document messaging
- Add or update tests covering application/query behavior and any practical UI/component logic already tested in the solution

Out of scope unless required by existing code structure:

- Large redesign of finance domain models
- New document management subsystem
- Mobile app changes
- Broad workflow engine changes beyond exposing existing linked context
- New authorization framework if a suitable one already exists

# Files to touch
Inspect first, then modify only the necessary files. Likely areas include:

- `src/VirtualCompany.Web/**`
  - finance workspace pages/components
  - detail drawer/detail panel components
  - shared drilldown/navigation components
- `src/VirtualCompany.Api/**`
  - finance endpoints/controllers if the web app calls API endpoints
- `src/VirtualCompany.Application/**`
  - finance queries/handlers
  - DTOs/read models
  - authorization/access checks for linked documents
- `src/VirtualCompany.Domain/**`
  - finance/document linkage value objects or entities only if truly needed
- `src/VirtualCompany.Infrastructure/**`
  - query/repository implementations
  - EF Core projections/config if applicable
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query integration tests
- Other relevant test projects if present for application/web layers

Before editing, identify the concrete files that currently implement:

- transactions list/detail
- invoices list/detail
- document detail/view navigation
- approval/workflow context display
- tenant/authorization checks

# Implementation plan
1. **Discover existing implementation**
   - Search for finance workspace, transactions, invoices, document detail, approval context, and drilldown components.
   - Identify whether the app uses:
     - direct Blazor server-side application services
     - API + DTOs
     - MediatR/CQRS handlers
     - EF Core projections
   - Reuse existing naming and architectural conventions.

2. **Map current data contracts**
   - Locate current transaction and invoice list/detail DTOs/view models.
   - Determine where linked document references already exist, if at all.
   - Determine how workflow/approval context is represented for invoices today.
   - Determine how document access is checked elsewhere in the app.

3. **Extend transaction list filtering if incomplete**
   - Ensure transactions page supports:
     - date range
     - category
     - flagged state
   - If filters already exist partially, complete them without breaking current behavior.
   - Keep filtering tenant-scoped and query-efficient.

4. **Extend transaction detail read model**
   - Add fields needed for acceptance criteria:
     - category
     - flags
     - anomaly state
     - linked document metadata
   - For linked document metadata, include only safe display fields such as:
     - document id/reference
     - title/name
     - type
     - source or created date if already available
     - access state
     - status message when unavailable
   - Avoid exposing storage URLs or sensitive internals directly.

5. **Extend invoice list and detail read model**
   - Ensure invoice list shows:
     - approval/review status
     - amount
     - supplier
   - Ensure invoice detail shows:
     - status
     - amount
     - supplier
     - linked workflow or approval context when available
   - Reuse existing approval/workflow summary DTOs if present.

6. **Implement linked document access behavior**
   - For transaction and invoice detail views, compute one of:
     - accessible linked document with navigation target
     - missing linked document
     - inaccessible linked document
     - no linked document
   - Navigation action should only render when the user has access.
   - Missing/inaccessible states must render a clear non-blocking message, not an error page or broken link.
   - Keep authorization tenant-aware and consistent with existing document access rules.

7. **Wire application/query layer**
   - Update handlers/services/repositories to populate the new detail models.
   - Prefer projection-based queries over loading large aggregates if the codebase already follows CQRS-lite read models.
   - Ensure all queries are scoped by `company_id` / tenant context.
   - If document access depends on membership/role, use existing policy/authorization services rather than duplicating logic.

8. **Update Blazor UI**
   - Transactions page:
     - verify/add filter controls
     - ensure selecting a row opens detail view
   - Transaction detail:
     - show category, flags, anomaly state
     - show linked document metadata section when available
     - show “Open document” action only when accessible
     - show non-blocking status text when missing/inaccessible
   - Invoices page:
     - verify list columns for status, amount, supplier
   - Invoice detail:
     - show status, amount, supplier
     - show linked workflow/approval context when available
     - show linked document action/status if invoice detail supports linked documents in current model
   - Match existing styling/component patterns; do not introduce a parallel UI pattern.

9. **Handle empty and edge states**
   - No linked document
   - Linked document record deleted
   - Linked document exists but user lacks access
   - Linked workflow/approval context absent
   - Null/unknown anomaly state or flags
   - Ensure UI remains usable and informative.

10. **Add tests**
   - Application/API tests for:
     - transaction detail includes linked document metadata when accessible
     - transaction detail returns inaccessible/missing status safely
     - invoice detail includes workflow/approval context when available
     - tenant isolation for linked document lookups
     - filters for transactions behave correctly
   - UI/component tests if the repo already uses them:
     - action button shown only when accessible
     - status message shown for missing/inaccessible document

11. **Keep changes minimal and coherent**
   - Do not refactor unrelated modules.
   - If you must introduce a shared helper for linked entity status, keep it small and local to finance/document read models unless clearly reusable.

# Validation steps
Run the smallest relevant checks first, then broader ones:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects or filters for touched areas, run those first as well.

4. Manually validate in the web app if feasible:
   - Open transactions page
   - Verify filters for date range, category, flagged state
   - Select a transaction and confirm detail view shows:
     - category
     - flags
     - anomaly state
     - linked document metadata or clear status message
   - Verify document navigation action appears only when access is allowed
   - Open invoices page
   - Verify list shows status, amount, supplier
   - Select an invoice and confirm detail view shows:
     - status
     - amount
     - supplier
     - linked workflow/approval context when available
   - Verify missing/inaccessible linked documents do not block the detail view

5. Confirm no tenant leakage:
   - Review queries/endpoints for tenant scoping
   - Ensure inaccessible documents do not expose sensitive metadata beyond a safe status message

# Risks and follow-ups
- **Risk: existing finance models may be incomplete or stubbed**
  - If so, extend read models conservatively and avoid overbuilding domain logic.

- **Risk: document access rules may not yet be centralized**
  - Reuse existing authorization patterns if possible.
  - If a small helper/service is needed, keep it narrowly scoped and note it for future consolidation.

- **Risk: linked document relationship may differ between transactions and invoices**
  - Support each path according to current schema rather than forcing a single abstraction prematurely.

- **Risk: workflow/approval context may be partially implemented**
  - Surface available context cleanly now and document any missing deeper drilldown as follow-up.

- **Follow-up candidates**
  - Shared linked-resource status component for finance and audit views
  - Stronger document summary DTO reused across modules
  - Additional integration tests for permission edge cases
  - Mobile parity later if finance drilldowns are needed in MAUI