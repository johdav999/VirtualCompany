# Goal
Implement backlog task **TASK-27.3.1 — Implement variance aggregation service for actual vs budget and actual vs forecast comparisons** for story **US-27.3 Deliver actual vs budget and actual vs forecast variance services**.

Deliver a tenant-safe .NET backend implementation that:
- Aggregates **actuals from ledger data** for a requested **company** and **period**
- Compares actuals against **budget** and **forecast** values
- Returns **variance amount** and **variance percentage**
- Groups results by **period** and **account**
- Includes **cost center grouping** when cost centers are enabled for the tenant
- Returns **empty results** when no budget or forecast exists for the requested slice
- Excludes all data from other tenants
- Includes automated **integration tests** against a fixed seeded dataset for at least one monthly period
- If summary/materialized tables are introduced, includes a **documented, idempotent refresh job**

Produce production-quality code aligned with the existing modular monolith and CQRS-lite patterns in this repository.

# Scope
In scope:
- Add or extend domain/application/infrastructure/API components needed for variance aggregation
- Support both comparison modes:
  - **actual vs budget**
  - **actual vs forecast**
- Implement grouping:
  - always by **period** and **account**
  - additionally by **cost center** when tenant/company settings enable cost centers
- Ensure tenant scoping via `company_id` on all queries
- Handle missing comparison data by returning empty result sets, not errors
- Add integration tests with deterministic seeded data and expected values
- Add refresh job + docs only if you introduce summary/pre-aggregated tables

Out of scope unless required by existing patterns:
- New UI pages
- Mobile changes
- Broad analytics/dashboard work beyond the API/service contract
- Unrelated refactors
- Premature optimization unless query shape clearly requires summary tables

Implementation expectations:
- Prefer extending existing finance/reporting/query modules if present
- Keep orchestration/UI concerns out of the aggregation logic
- Use typed DTOs/contracts, not ad hoc dynamic payloads
- Keep behavior deterministic and testable

# Files to touch
Inspect the repo first and update the exact files that fit existing conventions. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - domain models/value objects/enums for variance comparison types or grouping options if needed
- `src/VirtualCompany.Application/**`
  - query/handler/service interfaces
  - DTOs for variance request/response
  - validation
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/db access/query implementation
  - SQL projections or repository/query services
  - optional background refresh job if summary tables are added
- `src/VirtualCompany.Api/**`
  - endpoint/controller/minimal API wiring for variance API
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests
  - seeded test data updates
- `README.md` and/or `docs/**`
  - only if API usage or refresh job documentation needs to be added
- migration files
  - only if schema changes are necessary
- `docs/postgresql-migrations-archive/README.md`
  - only if this repo’s migration process requires documentation updates

Before coding, identify the actual existing finance/accounting/reporting files and follow local naming and folder conventions rather than inventing a parallel structure.

# Implementation plan
1. **Discover existing architecture and finance/reporting patterns**
   - Inspect current API, application, and infrastructure layers for:
     - tenant resolution and authorization patterns
     - CQRS query handlers
     - ledger/budget/forecast entities and tables
     - company settings / tenant feature flags for cost centers
     - integration test seeding approach
   - Reuse existing abstractions wherever possible.

2. **Define the variance contract**
   - Add request/response models for a variance query, likely including:
     - `companyId` or resolved tenant/company context
     - comparison type: `Budget` or `Forecast`
     - requested period/month or period range if already supported
     - optional account filters
     - optional cost center filters
   - Response should include, per row:
     - period
     - account identifier/code/name
     - optional cost center identifier/code/name when enabled
     - actual amount
     - comparison amount
     - variance amount
     - variance percentage
   - Define and document the variance percentage formula in code comments/tests. Use one consistent formula, typically:
     - `varianceAmount = actual - comparison`
     - `variancePercentage = comparison == 0 ? null/0 per existing API conventions : varianceAmount / comparison * 100`
   - Match existing API nullability conventions. If no convention exists, prefer `null` for percentage when denominator is zero.

3. **Implement application-layer query/service**
   - Add a query + handler or application service for variance retrieval.
   - Keep it read-only and deterministic.
   - Validate:
     - valid comparison type
     - valid period input
     - tenant/company context present
   - If no budget/forecast exists for the requested slice, return an empty collection and success response.

4. **Implement infrastructure aggregation**
   - Build the query against ledger actuals and budget/forecast data with strict `company_id` filtering.
   - Aggregate actuals from ledger data for the requested period.
   - Join/align actuals with budget or forecast by:
     - company
     - period
     - account
     - cost center when enabled/applicable
   - Ensure grouping behavior:
     - when cost centers disabled: aggregate by period + account only
     - when enabled: aggregate by period + account + cost center
   - Be careful with sparse data:
     - if actual exists but no comparison row for the slice, acceptance criteria says empty results when no budget/forecast exists for requested slice; implement behavior at request-slice level, not per-row erroring
     - avoid leaking partial cross-tenant or unrelated rows
   - Prefer a single efficient SQL query/projection if practical.

5. **Handle cost center enablement correctly**
   - Determine from existing tenant/company settings how cost center support is enabled.
   - Only include cost center grouping in the response when enabled for that tenant.
   - If enabled, ensure joins and grouping use cost center consistently.
   - If disabled, do not accidentally split rows by cost center from ledger/comparison tables.

6. **Expose API endpoint**
   - Add or extend an endpoint under the existing finance/reporting route conventions.
   - Ensure tenant/company authorization/scoping follows existing API patterns.
   - Return:
     - `200 OK` with rows when data exists
     - `200 OK` with empty rows when no budget/forecast exists for the requested slice
   - Do not return errors for missing comparison data unless the request itself is invalid.

7. **Add integration tests with seeded data**
   - Seed at least:
     - one tenant/company with ledger actuals
     - matching budget rows
     - matching forecast rows
     - at least one monthly period
     - account data
     - cost center data if feature-enabled scenario exists
     - another tenant/company with overlapping-looking data to prove isolation
   - Add tests covering:
     - actual vs budget expected values for one monthly period
     - actual vs forecast expected values for one monthly period
     - variance amount and percentage exact expected values
     - grouping by period and account
     - grouping by cost center when enabled
     - empty results when no budget exists for requested slice
     - empty results when no forecast exists for requested slice
     - exclusion of other tenant data
   - Keep expected values explicit in assertions, not recomputed by helper logic that could hide bugs.

8. **Only introduce summary tables if justified**
   - First prefer direct query implementation.
   - If performance or schema shape strongly suggests summary tables/materialized data:
     - add schema/migration
     - implement a refresh job that populates from ledger or snapshot data
     - make the refresh rerunnable and idempotent
     - document how to run it
     - add tests for refresh behavior if feasible
   - Do not add summary tables without a clear need.

9. **Document behavior**
   - Add concise documentation/comments for:
     - variance formula
     - zero-denominator percentage behavior
     - cost center grouping behavior
     - refresh job usage if introduced

10. **Keep code quality high**
   - Follow existing naming, dependency injection, and test conventions.
   - Avoid leaking persistence concerns into API contracts.
   - Keep tenant isolation explicit in every query path.

# Validation steps
1. Inspect and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically verify integration coverage for:
   - actual vs budget monthly variance values
   - actual vs forecast monthly variance values
   - tenant isolation
   - empty results for missing budget/forecast
   - cost center grouping behavior when enabled

4. If migrations were added:
   - ensure they apply cleanly using the repo’s existing migration workflow
   - verify schema changes are minimal and justified

5. If a refresh job was added:
   - run it twice and confirm rerun safety/idempotency
   - verify refreshed data matches expected API results

6. Manually sanity check API payload shape:
   - period/account grouping present
   - cost center fields present only when appropriate
   - variance amount and percentage values match seeded expectations

# Risks and follow-ups
- **Unknown existing finance schema:** ledger, budget, forecast, account, and cost center tables may already exist with naming that differs from assumptions. Adapt to actual schema rather than forcing new abstractions.
- **Tenant scoping risk:** missing `company_id` filters in any join can leak data. Double-check every query path and add tests with overlapping data across tenants.
- **Cost center ambiguity:** if some source rows have null cost centers, define grouping behavior carefully and align actuals/comparison joins consistently.
- **Percentage edge cases:** denominator zero behavior must be explicit and tested.
- **Sparse comparison data:** acceptance criteria require empty results when no budget/forecast exists for the requested slice; ensure this is implemented intentionally, not as an accidental side effect.
- **Performance:** direct aggregation may be sufficient now. Only add summary tables if needed, and if added, include a documented idempotent refresh process.
- **Follow-up candidates:** pagination/filtering for larger result sets, period-range support, caching, and dashboard reuse once the core service is correct.