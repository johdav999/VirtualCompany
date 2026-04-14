# Goal
Implement backlog task **TASK-3.1.4 — Add authorization checks for department and widget visibility** for story **US-3.1 Department-based executive dashboard composition**.

Ensure the executive dashboard API returns **server-driven department sections** in a **deterministic order** and filters both **department sections** and **widgets** by the requesting user’s **role** and **tenant scope**. Update the frontend to render from the returned configuration without hardcoded department-specific layouts, and add automated tests for ordering, authorization, and empty/fallback behavior.

# Scope
In scope:
- Add/extend dashboard composition logic to return department sections for at least:
  - finance
  - sales
  - support
  - operations
- Enforce authorization at two levels:
  - section visibility
  - widget visibility
- Ensure tenant scoping is preserved on all dashboard queries and visibility decisions.
- Return section payloads with:
  - deterministic display order
  - configured widgets
  - summary counts
  - navigation metadata needed by frontend
  - fallback/empty-state metadata when a department has no data
- Update Blazor frontend to render sections from API response/configuration rather than department-specific hardcoded layout branches.
- Add automated tests covering:
  - deterministic ordering
  - role-based visibility
  - tenant isolation/scope behavior
  - fallback behavior for empty departments

Out of scope:
- New department types beyond the required four unless needed by existing abstractions.
- Major redesign of the dashboard visual system.
- New identity model or broad authorization framework rewrite.
- Mobile-specific dashboard work unless shared DTOs/components require minor updates.

# Files to touch
Inspect the solution first and adjust to actual project structure/names, but expect to touch files in these areas:

- `src/VirtualCompany.Application`
  - Dashboard query/handler/service for executive cockpit data
  - DTOs/view models for dashboard sections/widgets
  - Authorization/visibility policy interfaces or helpers
- `src/VirtualCompany.Domain`
  - Department/widget configuration models or enums if they belong in domain/shared contracts
  - Role/permission constants if centralized here
- `src/VirtualCompany.Infrastructure`
  - Repository/query implementations for dashboard aggregates
  - Tenant-scoped data access used by dashboard widgets
  - Seed/config provider if dashboard section definitions are stored/configured here
- `src/VirtualCompany.Api`
  - Dashboard endpoint/controller/minimal API mapping
  - Authorization wiring if endpoint-level policies need adjustment
- `src/VirtualCompany.Web`
  - Dashboard page/components to render server-driven sections
  - Remove hardcoded department layout assumptions
- `src/VirtualCompany.Shared`
  - Shared contracts/enums if dashboard DTOs are shared between API/Web
- `tests/VirtualCompany.Api.Tests`
  - API/integration tests for dashboard response ordering, visibility, and fallback behavior

Also review:
- `README.md`
- any architecture or conventions docs in the repo
- existing auth/tenant resolution code from ST-101/ST-103-related implementation
- any existing dashboard code from ST-601 or related cockpit work

# Implementation plan
1. **Discover existing dashboard and auth patterns**
   - Find the current executive dashboard API, query handlers, DTOs, and frontend rendering path.
   - Identify how tenant context is resolved and how user role/membership is exposed to application services.
   - Reuse existing policy-based authorization and tenant-scoped query patterns; do not invent a parallel mechanism.

2. **Define a server-driven dashboard section contract**
   - Introduce or refine DTOs so the API returns a collection like `DepartmentSections`.
   - Each section should include at minimum:
     - `DepartmentKey`
     - `DisplayName`
     - `DisplayOrder`
     - `IsVisible` only if useful internally; avoid returning hidden sections
     - `SummaryCounts`
     - `Widgets`
     - `Navigation`
     - `EmptyState` or fallback metadata
   - Each widget should include enough metadata for frontend rendering and navigation without hardcoded department logic.
   - Keep contracts deterministic and stable.

3. **Centralize department configuration**
   - Create a single source of truth for required department ordering and baseline widget configuration.
   - Required deterministic order should explicitly place:
     1. finance
     2. sales
     3. support
     4. operations
   - Avoid relying on dictionary iteration, DB natural order, or reflection order.
   - If there is already a config/provider pattern, extend it rather than hardcoding in multiple places.

4. **Add authorization checks for section and widget visibility**
   - Implement a visibility evaluator/service that takes:
     - current tenant/company context
     - current user membership/role/permissions
     - department definition
     - widget definition
   - Enforce:
     - user only sees sections permitted by role
     - user only sees widgets permitted by role
     - tenant scope is always applied to underlying data queries
   - Prefer default-deny if visibility config is missing or ambiguous.
   - Keep authorization logic composable and testable outside controllers/UI.

5. **Compose dashboard sections with filtered widgets**
   - Build the dashboard response by iterating through configured departments in deterministic order.
   - For each department:
     - check section visibility
     - gather tenant-scoped summary counts
     - build widget models
     - filter unauthorized widgets
     - include navigation metadata required by frontend
   - If a department is visible but has no data:
     - still return the section if acceptance criteria and UX require it
     - include fallback/empty-state metadata
     - ensure summary counts are zeroed and widgets behave predictably
   - If a department has no authorized widgets after filtering, decide based on existing UX conventions:
     - either omit the section entirely
     - or return the section with empty-state/fallback metadata
   - Make this behavior explicit and covered by tests.

6. **Preserve tenant isolation in all widget data sources**
   - Audit every query used by department widgets and summary counts.
   - Ensure all repositories/specifications/EF queries filter by `company_id`/tenant context.
   - Do not trust frontend filtering for security.
   - If any widget currently aggregates across tenants or ignores membership scope, fix it in the application/infrastructure layer.

7. **Update API endpoint behavior**
   - Ensure the dashboard endpoint uses the authenticated user and resolved tenant/company membership.
   - Return only authorized sections/widgets in the response.
   - Keep response shape frontend-friendly and avoid leaking unauthorized metadata.

8. **Refactor frontend to render server-driven sections**
   - Update Blazor dashboard page/components to iterate over returned department sections.
   - Remove hardcoded branches like “if finance render X, if sales render Y” where possible.
   - Use widget type/component mapping only for generic rendering concerns, not department-specific layout assumptions.
   - Render:
     - section title
     - summary counts
     - widgets
     - navigation links/actions
     - empty/fallback state when no data exists
   - Keep the frontend resilient to missing/empty sections.

9. **Add automated tests**
   - Add API/integration tests for:
     - deterministic section order for the required departments
     - role-based section visibility
     - role-based widget visibility within a visible section
     - tenant isolation: user from tenant A cannot receive tenant B data/sections
     - fallback behavior when a visible department has no data
   - Add unit tests for the visibility evaluator if there is a suitable application-layer test project or existing pattern.
   - If frontend component tests exist, add a rendering test to verify server-driven section rendering without hardcoded department assumptions.

10. **Keep implementation aligned with architecture**
   - Place business rules in application/domain layers, not controllers or Razor pages.
   - Keep CQRS-lite boundaries intact.
   - Avoid direct DB access from UI.
   - Keep authorization decisions auditable/loggable if existing patterns support it.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify API behavior for multiple roles/tenants:
   - Call the dashboard endpoint as a user with broad access and confirm sections are returned in this exact order:
     - finance
     - sales
     - support
     - operations
   - Call as a restricted role and confirm unauthorized sections/widgets are absent.
   - Call under a different tenant and confirm no cross-tenant data appears.

4. Manually verify empty/fallback behavior:
   - Use a tenant or seeded scenario where one visible department has no data.
   - Confirm the API still returns the expected fallback metadata and the frontend renders an appropriate empty state.

5. Manually verify frontend rendering:
   - Open the Blazor dashboard.
   - Confirm department sections are rendered from API response data.
   - Confirm there are no hardcoded department-specific layout dependencies for the required sections.

6. Before finishing, include in your summary:
   - files changed
   - authorization approach used
   - how deterministic ordering is enforced
   - test coverage added
   - any assumptions or follow-up gaps

# Risks and follow-ups
- **Risk: hidden hardcoded frontend assumptions**
  - Existing Blazor components may still assume specific departments or widget shapes. Remove or isolate these assumptions.

- **Risk: authorization split across layers**
  - If some visibility logic exists in API and some in UI, consolidate to server-side enforcement as the source of truth.

- **Risk: tenant leakage in aggregate queries**
  - Dashboard widgets often use custom aggregate SQL/EF queries; verify every one is tenant-scoped.

- **Risk: ambiguous empty-section behavior**
  - Be explicit whether visible-but-empty departments are returned with fallback metadata or omitted. Match acceptance criteria and document the chosen rule in tests.

- **Risk: unstable ordering from config or persistence**
  - Do not rely on insertion order from JSON, dictionaries, or database retrieval. Use an explicit numeric display order.

Follow-ups if needed:
- Introduce a reusable dashboard section/widget policy model if current visibility rules are too ad hoc.
- Add audit logging for denied dashboard widget visibility decisions if product wants explainability for UI-level authorization.
- Consider caching authorized dashboard composition per user/tenant if performance becomes an issue, but only after correctness is established.