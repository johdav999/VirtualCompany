# Goal

Implement **TASK-3.1.1 — department dashboard configuration schema and persistence** for **US-3.1 Department-based executive dashboard composition** in the existing .NET modular monolith.

Deliver a server-driven dashboard configuration foundation that:
- persists department dashboard section definitions in PostgreSQL,
- returns department sections in a deterministic display order,
- includes configured widgets, summary counts, and frontend navigation metadata,
- enforces tenant and role-based visibility for sections/widgets,
- supports fallback behavior when a department has no data,
- is test-covered for ordering, visibility, and fallback behavior.

The implementation must align with the architecture:
- ASP.NET Core backend
- PostgreSQL primary store
- shared-schema multi-tenancy with `company_id`
- CQRS-lite application layer
- no frontend hardcoded department layouts

At minimum, support department sections for:
- finance
- sales
- support
- operations

# Scope

In scope:
- Add persistent schema/entities for dashboard department configuration and widget configuration.
- Add migrations for PostgreSQL.
- Add domain/application/infrastructure support to load dashboard configuration by tenant.
- Add API/query response shape that returns department sections in deterministic order.
- Include per-section:
  - department identity
  - display order
  - configured widgets
  - summary counts
  - navigation metadata
  - empty/fallback metadata when no data exists
- Enforce role/membership visibility and tenant scoping when returning sections and widgets.
- Seed or bootstrap default department dashboard configuration for finance, sales, support, and operations.
- Add automated tests covering:
  - deterministic ordering
  - role-based visibility
  - fallback behavior for empty departments

Out of scope unless required by existing code structure:
- Large frontend redesign
- Hardcoded department-specific rendering logic
- New analytics engines for KPI computation beyond lightweight summary counts needed for this task
- Mobile-specific work
- Full dashboard caching strategy unless already trivial to wire in
- Broad audit/eventing expansion unrelated to this task

# Files to touch

Inspect the solution first and adapt names/locations to the existing conventions. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - add dashboard configuration domain entities/value objects/enums
- `src/VirtualCompany.Application/**`
  - add query/handler/contracts for department dashboard configuration retrieval
  - add authorization/visibility filtering logic
  - add DTOs for section/widget/navigation payloads
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories/query services
  - migration files
  - seed/bootstrap logic for default department configs
- `src/VirtualCompany.Api/**`
  - dashboard endpoint/controller/minimal API wiring
  - request company context integration if not already present
- `src/VirtualCompany.Web/**`
  - only minimal changes if needed so frontend consumes server-driven sections without hardcoded department layouts
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for ordering, visibility, fallback
- optionally:
  - `README.md` or relevant docs if the repo documents migrations/seeding behavior
  - `docs/**` if there is an established architecture or migration note location

If the repository already has dashboard/cockpit code, extend existing types instead of creating parallel abstractions.

# Implementation plan

1. **Discover existing dashboard and tenancy patterns**
   - Inspect current modules for:
     - dashboard/cockpit endpoints
     - tenant/company resolution
     - membership role authorization
     - EF Core DbContext and migrations
     - seeding conventions
   - Reuse existing naming and layering patterns.
   - Do not introduce a new architectural style if one already exists.

2. **Design persistence model for server-driven department dashboard configuration**
   Create a normalized but pragmatic schema, likely with:
   - `dashboard_department_configs`
     - `id`
     - `company_id`
     - `department`
     - `display_name`
     - `display_order`
     - `is_enabled`
     - `icon` or similar optional UI metadata
     - `navigation_json` or explicit navigation fields
     - `visibility_roles_json` or equivalent normalized visibility model
     - timestamps
   - `dashboard_widget_configs`
     - `id`
     - `department_config_id`
     - `company_id`
     - `widget_key`
     - `title`
     - `widget_type`
     - `display_order`
     - `is_enabled`
     - `summary_binding` / `data_source_key` / `query_key`
     - `navigation_json`
     - `visibility_roles_json`
     - `empty_state_json`
     - timestamps

   Prefer explicit columns for stable query/filter/order fields and JSONB only for flexible UI metadata.

   Requirements:
   - shared-schema multi-tenancy via `company_id`
   - deterministic ordering by section `display_order`, then stable tie-breaker
   - deterministic widget ordering by widget `display_order`, then stable tie-breaker
   - support default configs for finance, sales, support, operations

3. **Add domain models and enums**
   Introduce domain concepts for:
   - department type/code
   - dashboard department config
   - dashboard widget config
   - navigation metadata
   - empty/fallback metadata
   - visibility rules

   Keep them simple and persistence-friendly.
   Avoid embedding frontend-specific layout logic beyond metadata needed for rendering.

4. **Add EF Core mappings and migration**
   - Add entity configurations.
   - Add indexes supporting:
     - `company_id`
     - `(company_id, department)`
     - `(company_id, display_order)`
     - widget lookup by `(company_id, department_config_id, display_order)`
   - Add unique constraints where appropriate, e.g. one active config per company+department.
   - Generate migration with clear names.
   - Ensure migration is idempotent and consistent with existing migration strategy.

5. **Seed/bootstrap default department configs**
   Implement default configuration creation for at least:
   - finance
   - sales
   - support
   - operations

   Include representative widgets per department and navigation metadata required by frontend.
   Example widget categories can be lightweight and config-driven:
   - finance: approvals, spend anomalies, invoice queue
   - sales: pipeline, open deals, follow-ups
   - support: open tickets, SLA risk, escalations
   - operations: workflow exceptions, task backlog, throughput

   Seed in the established repo pattern:
   - migration seed data, or
   - startup/bootstrap seeder, or
   - test fixture seeding if production seeding is handled elsewhere

   Ensure deterministic display order across these departments.

6. **Implement application query for department dashboard composition**
   Add a query/handler/service that:
   - resolves current company/tenant
   - resolves current user membership/role
   - loads configured department sections and widgets for the company
   - filters sections/widgets by role visibility
   - computes summary counts for each section/widget using existing task/workflow/approval data where available
   - returns fallback/empty-state metadata when a department has no data
   - returns sections in deterministic order

   The response contract should include, per section:
   - department key
   - title/display name
   - display order
   - summary counts
   - navigation metadata
   - widgets[]
   - empty state / hasData indicator

   And per widget:
   - widget key
   - title
   - type
   - display order
   - summary count/value if applicable
   - navigation metadata
   - visibility outcome if needed internally only
   - empty state metadata

7. **Enforce role and tenant scope**
   - Use existing membership role model and authorization patterns.
   - Users must only receive:
     - sections permitted by their role
     - widgets permitted by their role
     - data for their current tenant/company
   - Default deny if visibility config is missing/ambiguous.
   - Never leak hidden sections/widgets in payload shape if acceptance criteria require only permitted items.
   - Ensure all queries are company-scoped before aggregation.

8. **Support fallback behavior for empty departments**
   Implement behavior so that when a department has no underlying data:
   - the section still renders from configuration if the user is allowed to see it,
   - summary counts resolve safely to zero,
   - widgets expose empty-state/fallback metadata,
   - navigation metadata remains present if configured,
   - no exceptions occur from missing aggregates.

   This is important for frontend server-driven rendering and setup/empty-state UX.

9. **Wire API endpoint**
   Extend or add the dashboard API endpoint so it returns the department sections payload.
   Requirements:
   - tenant-aware
   - authenticated
   - deterministic response ordering
   - no hardcoded department-specific layout assembly in controller
   - controller/endpoint should delegate to application layer

   If an existing executive cockpit endpoint exists, extend it rather than creating a duplicate unless separation is cleaner and already consistent.

10. **Ensure frontend can render from server-driven config**
   If web code already consumes dashboard payloads:
   - update it minimally to iterate over returned department sections and widgets
   - remove/avoid department-specific hardcoded layout branching for these sections
   - preserve existing UX where possible

   Keep frontend changes minimal and configuration-driven.

11. **Add automated tests**
   Add integration-focused tests in `tests/VirtualCompany.Api.Tests` covering:
   - **section ordering**
     - response contains finance, sales, support, operations in expected deterministic order
   - **role-based visibility**
     - a restricted role does not receive unauthorized sections/widgets
     - an authorized role does receive expected sections/widgets
   - **tenant isolation**
     - one company’s config/data is not visible to another company
   - **fallback behavior**
     - when a department has no data, section still returns with zero/empty summaries and fallback metadata
   - **widget ordering**
     - widgets are returned in deterministic order within a section

   Prefer API/integration tests over only unit tests for acceptance coverage.

12. **Keep implementation clean**
   - Follow existing project conventions.
   - Avoid overengineering.
   - Keep DTOs explicit.
   - Keep business logic out of controllers.
   - Use cancellation tokens.
   - Keep null handling safe and deterministic.

# Validation steps

Run and verify at minimum:

1. Build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Validate migration generation/application flow according to repo conventions.
   If local migration execution is supported, verify:
   - new tables exist
   - indexes/constraints exist
   - seed/default config is present for finance, sales, support, operations

4. Manually verify API behavior if feasible:
   - authenticated request returns department sections in deterministic order
   - payload includes widgets, summary counts, and navigation metadata
   - restricted user role receives filtered sections/widgets only
   - empty department returns fallback metadata and zero counts

5. Confirm no frontend hardcoded department layout dependency remains for these sections, if web changes are in scope.

# Risks and follow-ups

- **Unknown existing dashboard model**
  - There may already be cockpit/dashboard entities or DTOs. Extend them instead of duplicating.
- **Role model ambiguity**
  - Membership roles may be coarse-grained. If visibility rules need finer granularity, keep implementation compatible with current roles and note follow-up work.
- **Summary count source complexity**
  - Existing domain data may not yet support rich counts for every widget. Use safe, minimal aggregates now and document richer KPI follow-up separately.
- **Seeding strategy differences**
  - The repo may prefer migration-based seed data or runtime bootstrap. Follow the established pattern only.
- **Frontend coupling**
  - If the web app currently hardcodes department layouts, keep changes minimal but ensure server-driven rendering is possible for this task.
- **Future extensibility**
  - Consider follow-up tasks for:
    - admin editing of dashboard configuration
    - per-role overrides beyond static role lists
    - Redis caching for dashboard aggregates
    - audit events for dashboard config changes
    - support for additional departments like marketing and executive assistant