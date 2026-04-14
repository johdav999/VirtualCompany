# Goal
Implement TASK-3.1.3: render the executive cockpit department layout from server-driven configuration so the Blazor web UI does not hardcode department-specific sections.

Deliver an end-to-end slice across backend and frontend for ST-601 / US-3.1 that:
- returns department dashboard sections in deterministic order for at least finance, sales, support, and operations
- includes widgets, summary counts, and navigation metadata per section
- filters sections/widgets by tenant and user role
- renders the UI from API-provided configuration
- covers ordering, visibility, and no-data fallback with automated tests

# Scope
In scope:
- Executive cockpit dashboard query/API contract updates
- Application-layer composition of department sections
- Role- and tenant-scoped filtering of department sections/widgets
- Deterministic ordering logic
- Frontend refactor to render sections/widgets dynamically from response data
- Empty/fallback rendering when a department has no data
- Automated tests at appropriate layers

Out of scope unless required by existing code patterns:
- New mobile UI work
- Broad redesign of dashboard visuals
- New department types beyond the required seeded/configured set
- Full admin UI for editing dashboard layout configuration
- Non-dashboard analytics redesign

Implementation constraints:
- Follow existing modular monolith boundaries
- Keep tenant scoping enforced in query/application layers
- Prefer CQRS-lite query flow for dashboard reads
- Avoid hardcoded finance/sales/support/operations layout branches in Blazor components
- Keep API contracts frontend-friendly and deterministic

# Files to touch
Inspect the solution first and update the exact files that fit existing patterns. Likely areas:

Backend:
- `src/VirtualCompany.Api/**` for dashboard endpoint/controller/minimal API wiring
- `src/VirtualCompany.Application/**` for dashboard query, DTOs, handlers, authorization filtering, composition services
- `src/VirtualCompany.Domain/**` if a domain enum/value object/config model for departments or widget metadata is appropriate
- `src/VirtualCompany.Infrastructure/**` for repository/query implementations and any seeded/config-backed dashboard layout source

Frontend:
- `src/VirtualCompany.Web/**` for executive cockpit page/components/view models
- Shared DTOs/contracts if the solution uses:
  - `src/VirtualCompany.Shared/**`

Tests:
- `tests/VirtualCompany.Api.Tests/**`
- any existing application/web test projects if present

Also review:
- `README.md`
- any architecture or conventions docs in `docs/**`

# Implementation plan
1. **Discover current dashboard flow**
   - Find the executive cockpit endpoint, query handler, DTOs, and Blazor page/components.
   - Identify how tenant context and user role are currently resolved.
   - Identify whether dashboard widgets are already modeled server-side or assembled ad hoc in UI.

2. **Define/extend the server-driven dashboard contract**
   - Add or extend response models to include:
     - top-level collection of department sections
     - deterministic `displayOrder`
     - stable department key/code
     - display title
     - summary counts
     - widget collection
     - navigation metadata for section and/or widgets
     - no-data/fallback metadata if needed by UI
   - Suggested shape, adapted to existing conventions:
     - `DashboardDepartmentSectionDto`
       - `DepartmentKey`
       - `Title`
       - `DisplayOrder`
       - `SummaryCounts`
       - `Widgets`
       - `Navigation`
       - `HasData`
       - `EmptyStateMessage`
     - `DashboardWidgetDto`
       - `WidgetKey`
       - `Title`
       - `WidgetType`
       - `DisplayOrder`
       - `Value/Series/Items` as appropriate
       - `Navigation`
       - `IsVisible`
       - `HasData`
   - Keep the contract deterministic and serializable without frontend inference.

3. **Implement server-side department layout composition**
   - Create or extend an application service/query handler that builds department sections from server-side configuration rather than UI assumptions.
   - Ensure at minimum these departments are returned when permitted:
     - finance
     - sales
     - support
     - operations
   - Centralize ordering in one place, e.g.:
     - static config object
     - seeded configuration
     - application-level layout provider
   - Do not scatter ordering logic across controller and UI.

4. **Apply role- and tenant-based filtering**
   - Filter department sections based on:
     - current tenant/company context
     - current user role/membership permissions
   - Filter widgets within visible sections independently if needed.
   - Default to deny/hide when permission mapping is missing or ambiguous.
   - Preserve deterministic ordering after filtering.

5. **Implement fallback/no-data behavior**
   - For departments with no underlying data:
     - still return the permitted section if that matches product intent
     - include `HasData = false`
     - include empty-state/fallback metadata/message
     - include zeroed summary counts where appropriate
   - Ensure frontend can render a consistent empty state without special-casing departments.

6. **Refactor Blazor executive cockpit rendering**
   - Replace any hardcoded department-specific layout blocks with iteration over API-provided sections.
   - Render section header, summary counts, widgets, and navigation links from the response model.
   - Keep widget rendering generic by widget type/component mapping if needed, but not by department.
   - Ensure finance/sales/support/operations are not explicitly laid out in Razor except perhaps in tests/fixtures.

7. **Preserve UX quality**
   - Keep existing dashboard content that is outside department sections intact unless it conflicts.
   - Ensure stable ordering in UI matches API order.
   - Render empty/fallback state for sections with no data.
   - Avoid null-reference issues when optional metadata is absent.

8. **Add automated tests**
   - Backend/API tests:
     - section ordering is deterministic
     - required departments appear in expected order when permitted
     - unauthorized roles do not receive forbidden sections/widgets
     - tenant scoping prevents cross-tenant leakage
     - no-data department returns fallback metadata/zero counts
   - Frontend/component tests if present in repo patterns; otherwise cover via API contract + rendered page tests where feasible.
   - If there are no existing UI tests, at minimum add robust API/application tests and keep UI logic simple.

9. **Keep implementation aligned with existing architecture**
   - Query-side only for dashboard reads
   - No direct DB access from UI
   - Reuse existing authorization abstractions
   - Keep DTOs in the layer already used by the solution

10. **Document assumptions in code comments only where necessary**
   - Especially around:
     - department ordering source
     - permission mapping
     - fallback behavior semantics

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API response for a permitted user:
   - dashboard response includes finance, sales, support, operations in deterministic order
   - each section includes widgets, summary counts, and navigation metadata

4. Manually verify authorization behavior:
   - use or add test fixtures for different roles
   - confirm restricted roles do not receive unauthorized sections/widgets

5. Manually verify no-data behavior:
   - simulate a department with no backing data
   - confirm API returns fallback metadata
   - confirm UI renders empty state without breaking layout

6. Manually verify frontend rendering:
   - executive cockpit page renders sections by iterating server response
   - no hardcoded department-specific layout branches remain
   - ordering matches API order

7. If snapshot/golden tests exist, update and verify them.

# Risks and follow-ups
- **Unknown existing dashboard architecture:** The current solution may already have partial dashboard DTOs/components; adapt rather than duplicate.
- **Authorization model ambiguity:** Role-to-department/widget visibility rules may not yet be explicit. If missing, implement the smallest clear mapping consistent with existing membership/permission patterns and note follow-up needs.
- **Widget polymorphism complexity:** If widgets vary significantly, keep the first pass to a small generic contract and avoid overengineering.
- **Configuration source choice:** If no persisted layout config exists yet, use a server-side provider with deterministic defaults now and leave persisted admin-configurable layout for a later task.
- **UI test coverage limitations:** If the repo lacks Blazor UI test infrastructure, prioritize application/API tests and keep UI rendering thin.
- **Follow-up candidates:**
  - persisted dashboard layout configuration per tenant
  - richer widget type registry
  - caching for dashboard composition
  - auditability for dashboard visibility decisions
  - expansion to more departments and personalized layouts