# Goal
Implement backlog task **TASK-27.3.2 — Expose variance API endpoints with grouping by period, account, category, and optional cost center** for story **US-27.3 Deliver actual vs budget and actual vs forecast variance services**.

Deliver a tenant-scoped ASP.NET Core API and supporting application/infrastructure logic that:
- Aggregates **actuals from ledger data**
- Compares actuals against **budget** and **forecast**
- Returns **variance amount** and **variance percentage**
- Groups results by:
  - **period**
  - **account**
  - **category**
  - and **cost center when enabled for the tenant**
- Returns **empty results instead of errors** when no budget or forecast exists for the requested slice
- Enforces **multi-tenant isolation**
- Includes **automated integration tests** using a fixed seeded dataset for at least one monthly period
- If you introduce any summary/materialized tables, include a **documented refresh job** that is **safe to rerun**

# Scope
In scope:
- Add or extend domain/application query models for variance reporting
- Add API endpoint(s) under the existing reporting/analytics/finance API area consistent with project conventions
- Implement tenant-aware query handling against PostgreSQL-backed data
- Support both:
  - actual vs budget
  - actual vs forecast
- Support grouping dimensions:
  - period
  - account
  - category
  - optional cost center
- Detect whether cost centers are enabled for the tenant and include grouping only when enabled
- Return deterministic response DTOs including:
  - grouping keys
  - actual amount
  - comparison amount
  - variance amount
  - variance percentage
- Add integration tests covering:
  - expected monthly seeded values
  - tenant isolation
  - empty result behavior when comparison data is absent
  - cost center behavior when enabled/disabled
- Add documentation for any refresh job if summary tables are introduced

Out of scope unless required by existing patterns:
- UI/Blazor work
- Mobile work
- New forecasting or budgeting authoring flows
- Broad analytics redesign
- Unrelated refactors

Implementation constraints:
- Follow existing modular monolith and CQRS-lite patterns
- Keep tenant scoping explicit through request handling and data access
- Prefer typed application queries over controller-level SQL
- Do not leak cross-tenant data
- Do not throw errors for missing budget/forecast slices; return empty collections or equivalent successful empty payloads
- If variance percentage denominator is zero, handle safely and consistently; document behavior in code/tests

# Files to touch
Inspect the solution and update the actual files that match existing conventions. Likely areas include:

- `src/VirtualCompany.Api/...`
  - Controllers/endpoints for finance/reporting analytics
  - Request/response contracts if API-specific models are used
  - DI registration if endpoint wiring is manual

- `src/VirtualCompany.Application/...`
  - Query/handler for variance reporting
  - DTOs/view models for grouped variance rows
  - Validation for request parameters
  - Service interfaces if application layer uses abstractions

- `src/VirtualCompany.Domain/...`
  - Domain enums/value objects only if needed for report grouping/comparison types
  - Avoid unnecessary domain churn for a read-model feature

- `src/VirtualCompany.Infrastructure/...`
  - EF Core query implementation, repositories, or SQL-based read service
  - Tenant-scoped data access
  - Optional summary table refresh job implementation
  - Migrations if schema changes are required
  - Seed/test data support if infrastructure owns it

- `tests/VirtualCompany.Api.Tests/...`
  - Integration tests for endpoint behavior and seeded expected values
  - Test fixtures/seeding updates
  - Assertions for tenant isolation and empty results

- `README.md` or relevant docs folder
  - Document refresh job behavior if summary tables/materialized views are introduced

- `docs/...`
  - Migration or operational notes if needed

Only touch files necessary for this task. Reuse existing finance/reporting structures if present.

# Implementation plan
1. **Discover existing finance/reporting patterns**
   - Inspect current API route conventions, tenant resolution, auth/policy patterns, MediatR/CQRS usage, and test fixture setup.
   - Find existing ledger, budget, forecast, account, category, period, and cost center entities/tables.
   - Identify how tenant/company context is passed through requests.
   - Identify whether cost center enablement already exists in company settings or tenant configuration.

2. **Define request/response contract**
   - Add a variance query request model with fields similar to:
     - `companyId` or tenant-scoped company context per existing conventions
     - comparison type: `budget` or `forecast`
     - period range or fixed period/month
     - optional account/category filters
     - optional cost center filter
     - grouping options if the API supports configurable grouping; otherwise hardcode required grouping in response
   - Add response DTO(s) with:
     - period key
     - account id/code/name as available
     - category id/code/name as available
     - optional cost center id/code/name
     - actual amount
     - comparison amount
     - variance amount
     - variance percentage
   - Keep response shape stable and explicit.

3. **Implement application query**
   - Create a query + handler or equivalent application service for variance reporting.
   - Validate:
     - supported comparison type
     - valid period inputs
     - tenant/company access
   - Resolve whether cost centers are enabled for the tenant.
   - Delegate data retrieval to infrastructure read model/query service.

4. **Implement infrastructure aggregation**
   - Build a tenant-scoped query that:
     - aggregates actuals from ledger data for the requested company and period
     - aggregates matching budget or forecast values for the same slice
     - aligns/group joins on:
       - period
       - account
       - category
       - cost center when enabled
   - Ensure behavior:
     - if no budget/forecast exists for requested slice, return empty results rather than error
     - exclude all rows from other tenants
   - Be careful with joins:
     - avoid accidental fan-out
     - normalize null cost center handling
     - ensure category comes from the correct source of truth
   - Compute:
     - `varianceAmount = actualAmount - comparisonAmount`
     - `variancePercentage` using a consistent denominator, typically comparison amount
   - For zero comparison amount:
     - use project-consistent safe behavior, e.g. `0`, `null`, or a documented sentinel
     - cover with tests

5. **Cost center conditional grouping**
   - If tenant cost centers are enabled:
     - include cost center in grouping and response
   - If disabled:
     - aggregate without cost center dimension
     - do not split rows by cost center
   - If the API contract always includes a cost center field, return `null` when disabled.
   - Add tests for both enabled and disabled cases.

6. **Endpoint wiring**
   - Expose endpoint(s) in API layer, for example under a route like:
     - `/api/companies/{companyId}/finance/variance/budget`
     - `/api/companies/{companyId}/finance/variance/forecast`
     - or a single endpoint with comparison type parameter, depending on existing conventions
   - Return `200 OK` with payload for successful queries, including empty result sets.
   - Apply existing authorization and tenant membership checks.

7. **Optional summary table path**
   - Prefer direct query first unless performance or existing architecture strongly suggests summary tables.
   - If introducing summary/materialized tables:
     - add schema/migration
     - implement a refresh job/service that populates from ledger or snapshot data
     - make refresh idempotent and safe to rerun
     - document invocation and operational expectations
     - add at least one test or verification path for refresh correctness if feasible

8. **Integration tests**
   - Extend seeded dataset or add test fixture data for:
     - one monthly period with known actual, budget, and forecast values
     - at least two tenants/companies to verify isolation
     - cost center enabled tenant
     - cost center disabled tenant
     - missing budget slice
     - missing forecast slice
   - Assert exact expected values for:
     - actual amount
     - comparison amount
     - variance amount
     - variance percentage
   - Assert empty results for missing comparison data.
   - Assert no cross-tenant leakage.

9. **Documentation and cleanup**
   - Document any non-obvious calculation rules in code comments/tests.
   - If summary refresh exists, document:
     - source tables
     - rerun safety
     - how to execute
   - Keep implementation focused and consistent with existing naming and layering.

# Validation steps
Run and verify at minimum:

1. Build:
   - `dotnet build`

2. Automated tests:
   - `dotnet test`

3. Specifically verify integration coverage for:
   - actual vs budget monthly variance returns expected seeded values
   - actual vs forecast monthly variance returns expected seeded values
   - grouping includes period, account, category
   - grouping includes cost center when enabled
   - grouping excludes/suppresses cost center split when disabled
   - missing budget returns empty results with success status
   - missing forecast returns empty results with success status
   - tenant A cannot see tenant B data

4. Manual/API verification if practical:
   - Call the endpoint for a seeded company/month and inspect payload shape
   - Call same route for another tenant and confirm isolation
   - Call a slice with no budget/forecast and confirm empty array/result set

5. If summary tables were added:
   - run refresh job twice
   - confirm no duplication/corruption
   - confirm endpoint results remain correct after rerun

# Risks and follow-ups
- **Data model ambiguity:** Ledger, budget, and forecast schemas may not align cleanly on account/category/cost center. Resolve mapping carefully before coding.
- **Category source ambiguity:** Category may belong to account master data rather than transaction rows. Use the canonical source and keep grouping consistent.
- **Cost center enablement location:** Tenant setting may be stored in company settings JSON or another config table; verify before implementing.
- **Variance percentage edge cases:** Division by zero and negative comparison values can produce surprising results. Keep behavior explicit and test-covered.
- **Join fan-out risk:** If budgets/forecasts are stored at different granularity than ledger actuals, naive joins can duplicate amounts. Aggregate each side first, then join on normalized keys.
- **Tenant isolation risk:** Ensure every query filters by tenant/company before aggregation, not after.
- **Performance risk:** Large ledger scans may be expensive. Start with correctness; if needed, follow up with indexed summaries/materialized views.
- **Follow-up candidate:** Add pagination, totals, or rollups if product later needs dashboard-scale reporting.
- **Follow-up candidate:** Add caching or pre-aggregation if endpoint latency is high on realistic datasets.
- **Follow-up candidate:** Add explicit API docs/OpenAPI examples for finance consumers.