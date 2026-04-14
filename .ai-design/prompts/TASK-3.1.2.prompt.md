# Goal
Implement backlog task **TASK-3.1.2 — Build dashboard composition API for department sections and widgets** for story **US-3.1 Department-based executive dashboard composition**.

Deliver a server-driven dashboard composition capability in the **.NET backend** that returns department sections in a deterministic order, includes widget configuration and navigation metadata, and enforces **tenant scope** plus **role-based visibility**. Ensure the frontend can render department sections from API configuration without hardcoded department-specific layouts. Add automated tests covering ordering, visibility, and fallback behavior when a department has no data.

# Scope
In scope:
- Add/extend a dashboard composition query/API in the **Analytics & Cockpit** area.
- Return department sections for at least:
  - finance
  - sales
  - support
  - operations
- Enforce deterministic display order from server-side configuration.
- Include per-section:
  - department identity metadata
  - display order
  - configured widgets
  - summary counts
  - navigation metadata needed by frontend
  - empty/fallback state metadata when no data exists
- Enforce filtering by:
  - tenant/company scope
  - user membership role/permissions
- Update frontend consumption so department sections are rendered from API response/config rather than hardcoded layouts.
- Add automated tests for:
  - section ordering
  - role-based visibility
  - fallback behavior for empty departments

Out of scope unless required by existing code patterns:
- Building all final KPI calculations for every widget in depth
- New persistence schema unless clearly necessary
- Mobile-specific UI work
- Broad redesign of the executive dashboard beyond department composition
- Introducing a generic CMS/config editor for dashboard layout

# Files to touch
Inspect the solution first and then touch the minimal correct set. Likely areas:

Backend:
- `src/VirtualCompany.Api/...`
  - dashboard/cockpit controller or endpoint definitions
  - DI registration if new services/handlers are added
- `src/VirtualCompany.Application/...`
  - dashboard query/handler
  - DTOs/view models for dashboard composition
  - authorization/visibility policy helpers
- `src/VirtualCompany.Domain/...`
  - department/widget enums or value objects if missing
- `src/VirtualCompany.Infrastructure/...`
  - repositories/query services for dashboard aggregates
  - tenant-scoped data access
  - caching only if already used by this feature area

Frontend:
- `src/VirtualCompany.Web/...`
  - dashboard page/component consuming composition API
  - rendering loop for server-driven department sections/widgets
  - removal of hardcoded department-specific layout assumptions

Tests:
- `tests/VirtualCompany.Api.Tests/...`
  - API/integration tests for response shape, ordering, visibility, and empty fallback
- Add application-layer tests too if the repo already has a pattern for them

Also review:
- `README.md`
- any existing architecture/docs in `docs/`
- existing dashboard, analytics, tenant resolution, and authorization code paths before implementing

# Implementation plan
1. **Discover existing dashboard architecture**
   - Find current executive dashboard endpoints, handlers, DTOs, and Blazor page/components.
   - Identify how tenant context is resolved and how user membership roles are accessed.
   - Reuse existing CQRS/query patterns, naming conventions, and response contracts where possible.

2. **Define a server-driven dashboard composition contract**
   - Create or extend response DTOs to represent:
     - dashboard composition root
     - department sections
     - widgets
     - summary counts
     - navigation metadata
     - empty state/fallback metadata
   - Suggested shape:
     - `DashboardCompositionDto`
     - `DepartmentSectionDto`
     - `DashboardWidgetDto`
     - `NavigationMetadataDto`
     - `EmptyStateDto`
   - Each department section should include fields similar to:
     - `DepartmentKey`
     - `DisplayName`
     - `DisplayOrder`
     - `IsVisible`
     - `SummaryCounts`
     - `Widgets`
     - `Navigation`
     - `IsEmpty`
     - `EmptyState`
   - Keep the contract frontend-friendly and deterministic.

3. **Implement deterministic department configuration**
   - Define a server-side configuration/source of truth for supported departments and order.
   - Minimum required order must include:
     1. finance
     2. sales
     3. support
     4. operations
   - Prefer a centralized configuration class or static mapping in application layer over scattered literals.
   - Do not rely on DB row ordering or dictionary iteration order.

4. **Implement role- and tenant-aware composition service/query**
   - Build a query/handler/service that:
     - resolves current company/tenant
     - resolves current user membership/role
     - loads aggregate data for each supported department
     - filters sections/widgets not permitted for the user
     - returns only allowed sections/widgets
   - Ensure all data access is company-scoped.
   - If widget visibility differs by role, apply filtering at widget level too.
   - Default-deny if role visibility is ambiguous.

5. **Populate section summary counts and widget metadata**
   - For each department section, include:
     - configured widgets
     - summary counts relevant to that department
     - navigation targets required by frontend drill-down
   - Reuse existing aggregate queries if available.
   - If no real data exists for a department, still return the section when permitted, with:
     - empty summary counts
     - empty widgets or placeholder widget states per design
     - explicit fallback/empty-state metadata

6. **Add fallback behavior for no-data departments**
   - Implement deterministic empty-state behavior.
   - Acceptance requires automated verification when a department has no data.
   - Recommended behavior:
     - permitted department section still appears
     - `IsEmpty = true`
     - summary counts default to zero
     - widgets either:
       - return configured widgets with empty data state, or
       - return no widgets plus an empty-state payload
   - Keep behavior consistent across departments.

7. **Expose/update API endpoint**
   - Add or update the dashboard endpoint to return the new composition response.
   - Ensure endpoint authorization uses existing authenticated tenant-aware patterns.
   - Keep route naming consistent with current API conventions.

8. **Update Blazor frontend to render from server-driven composition**
   - Replace hardcoded department-specific rendering branches with iteration over returned sections/widgets.
   - Render section headers, summary counts, widgets, and navigation links from API metadata.
   - Preserve styling/layout conventions, but remove department-specific layout assumptions from code.
   - If some widget components are type-based, map by generic widget type/key rather than department name.

9. **Add automated tests**
   - API/integration tests should verify:
     - deterministic order of finance, sales, support, operations
     - unauthorized roles do not receive forbidden sections/widgets
     - tenant isolation is preserved
     - empty department fallback behavior is returned consistently
   - If practical, add frontend/component tests or at least ensure the frontend binds to generic section iteration rather than hardcoded department checks.

10. **Keep implementation incremental and aligned with repo patterns**
   - Avoid speculative abstractions.
   - Prefer small composable services over embedding logic in controllers/pages.
   - Keep DTOs and query handlers explicit and testable.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify API behavior:
   - Call the dashboard composition endpoint as a user with broad access.
   - Confirm returned section order is exactly deterministic and includes:
     - finance
     - sales
     - support
     - operations
   - Confirm each visible section includes widgets, summary counts, and navigation metadata.

4. Verify role-based filtering:
   - Execute tests or manual requests for users with different roles.
   - Confirm users only receive permitted sections/widgets.
   - Confirm no cross-tenant data appears.

5. Verify empty/fallback behavior:
   - Use seeded or test data where one permitted department has no records.
   - Confirm the section still renders with expected empty-state/fallback metadata and stable ordering.

6. Verify frontend behavior:
   - Load the dashboard in the web app.
   - Confirm department sections render from API response order.
   - Confirm there are no hardcoded department-specific layout dependencies remaining for these sections.

7. If formatting/analyzers are configured in the repo, run them and fix issues before finishing.

# Risks and follow-ups
- **Unknown existing dashboard shape:** The repo may already have partial dashboard DTOs/endpoints; extend carefully to avoid breaking consumers.
- **Authorization complexity:** Human role permissions may be coarse or inconsistently modeled; document any assumptions and keep filtering centralized.
- **Widget contract drift:** Frontend and backend may currently rely on implicit widget assumptions; align on explicit widget keys/types and navigation metadata.
- **Empty-state ambiguity:** If current UX does not define whether empty departments return placeholder widgets or empty widget lists, choose one consistent approach and document it in code/tests.
- **Performance:** Aggregating multiple department sections may create N+1 queries; prefer batched tenant-scoped queries where feasible.
- **Caching:** If dashboard caching exists, ensure cache keys include tenant and role/permission context.
- **Follow-up candidates:**
  - externalize dashboard section/widget configuration
  - add richer per-role widget policies
  - add more departments beyond the required four
  - add contract tests between API and Blazor dashboard rendering