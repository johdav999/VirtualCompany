# Goal
Implement automated integration tests for **TASK-27.3.3 — Add integration tests for seeded monthly variance calculations and tenant isolation** under **US-27.3 Deliver actual vs budget and actual vs forecast variance services**.

The coding agent should add or update integration tests that prove, against a fixed seeded dataset, that the variance API/services:

- aggregate actuals from ledger data for a requested company and monthly period
- compare actuals against budget and forecast values
- return variance amount and variance percentage grouped by period and account
- include cost center grouping when cost centers are enabled for the tenant
- exclude data from other tenants
- return empty results, not errors, when no budget or forecast exists for the requested slice
- remain compatible with any summary-table approach, including safe rerunnable refresh behavior if such tables already exist in the codebase

The implementation should prefer exercising the real API surface and persistence stack used by existing integration tests in this repository.

# Scope
In scope:

- Inspect the existing variance API/service implementation and current test infrastructure
- Identify or add deterministic seeded integration-test data for:
  - at least two tenants/companies
  - ledger actuals
  - budget values
  - forecast values
  - at least one monthly period with known expected results
  - cost center enabled and disabled scenarios if supported by current domain model
- Add integration tests covering:
  - actual vs budget monthly variance correctness
  - actual vs forecast monthly variance correctness
  - grouping by period and account
  - cost center grouping when enabled
  - tenant isolation
  - empty result behavior when budget/forecast data is absent
- If the implementation uses summary/materialized tables, add assertions or test setup steps validating the documented refresh path is safe to rerun

Out of scope unless required to make tests pass:

- redesigning the variance domain
- broad refactors unrelated to testability
- changing API contracts except where current behavior clearly violates acceptance criteria
- adding new product features beyond what is necessary for test coverage

# Files to touch
Likely files to inspect and update:

- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
- existing integration test files under `tests/VirtualCompany.Api.Tests/`
- test fixtures, web application factory, database reset/seed helpers under `tests/VirtualCompany.Api.Tests/`
- variance-related API/controller/query/service files under:
  - `src/VirtualCompany.Api/`
  - `src/VirtualCompany.Application/`
  - `src/VirtualCompany.Infrastructure/`
  - possibly `src/VirtualCompany.Domain/`
- seed/test data helpers and SQL seed scripts if present
- documentation if summary-table refresh behavior exists and is undocumented:
  - `README.md`
  - module docs near variance/reporting code
  - `docs/postgresql-migrations-archive/README.md` only if relevant to refresh-job documentation

Only touch files actually needed after repo inspection. Reuse existing test conventions and helpers.

# Implementation plan
1. **Discover current variance implementation and test patterns**
   - Search for variance-related endpoints, handlers, services, DTOs, and existing tests.
   - Identify:
     - API routes for actual vs budget and actual vs forecast
     - request parameters for company/tenant, period, account, and cost center
     - current integration test infrastructure and seeding approach
     - whether summary tables/materialized views are already used

2. **Map the real data model used by variance calculations**
   - Determine which tables/entities represent:
     - ledger actuals
     - budgets
     - forecasts
     - accounts
     - periods/months
     - cost centers
     - tenant/company ownership
   - Confirm how tenant scoping is enforced:
     - `company_id`
     - tenant context in auth/request pipeline
     - repository/query filters

3. **Create deterministic seeded test data**
   - Add or extend test seed helpers so integration tests can load a fixed dataset with explicit expected values.
   - Seed at minimum:
     - **Tenant A** with monthly ledger actuals, budget, and forecast for one known month
     - **Tenant B** with overlapping-looking data to prove isolation
   - Include simple arithmetic that makes expected variance obvious, for example:
     - actual = 1200, budget = 1000, variance amount = 200, variance % = 20%
     - actual = 1200, forecast = 1500, variance amount = -300, variance % = -20%
   - If cost centers are supported:
     - seed at least one tenant with cost centers enabled
     - seed multiple cost centers under the same account/month so grouping can be asserted
   - Keep values small and unambiguous.

4. **Add integration tests for monthly variance correctness**
   - Add tests that call the real API endpoint and assert:
     - HTTP success status
     - response shape
     - one monthly period returned
     - expected grouping by account
     - exact variance amount and variance percentage values
   - Cover both:
     - actual vs budget
     - actual vs forecast
   - Use exact decimal assertions where possible; if rounding rules exist, assert according to the API contract.

5. **Add integration tests for cost center grouping**
   - If tenant settings/domain support cost centers:
     - call the variance API for a tenant with cost centers enabled
     - assert results include cost center grouping keys and expected values per cost center
   - Also verify behavior for a tenant without cost centers enabled if applicable:
     - either no cost center grouping is returned
     - or grouping remains null/omitted per current contract

6. **Add tenant isolation coverage**
   - Seed Tenant B with data that would alter totals if leakage occurred.
   - Query Tenant A’s variance endpoint and assert only Tenant A data is included.
   - If the API requires authenticated tenant context, ensure the test uses the proper tenant-scoped identity/request setup.
   - Prefer assertions that would fail clearly if cross-tenant rows were included.

7. **Add empty-result behavior tests**
   - Query a valid tenant/period/account slice where:
     - ledger actuals may exist but no budget exists, and/or
     - no forecast exists
   - Assert:
     - success response, not 4xx/5xx
     - empty result collection per acceptance criteria
   - Match the existing API contract exactly if it returns an envelope object.

8. **Handle summary-table refresh validation if applicable**
   - If repo inspection shows summary tables/materialized views are part of variance calculations:
     - identify the refresh job/command/process
     - ensure it is documented if not already
     - add an integration test or setup step that runs refresh more than once and confirms safe rerun behavior
     - verify refreshed data produces the expected variance results
   - If no summary tables exist, do not introduce them just for this task.

9. **Keep tests maintainable**
   - Centralize seed creation in helper/builders rather than inline repetitive setup.
   - Name tests around business behavior, e.g.:
     - `GetActualVsBudgetVariance_ForSeededMonthlyData_ReturnsExpectedVarianceByAccount`
     - `GetActualVsForecastVariance_ExcludesOtherTenantData`
     - `GetVariance_WhenNoBudgetExists_ReturnsEmptyResults`
   - Follow existing repository naming and assertion style.

10. **Make minimal production fixes only if tests expose gaps**
   - If integration tests reveal missing tenant filters, incorrect grouping, or wrong empty-result behavior, fix production code narrowly.
   - Do not broaden scope beyond what is needed for acceptance criteria.

# Validation steps
1. Inspect and run the relevant tests:
   - `dotnet test`

2. If needed, run targeted tests for the API test project:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Verify the new integration tests cover:
   - actual vs budget monthly variance expected values
   - actual vs forecast monthly variance expected values
   - grouping by period and account
   - cost center grouping when enabled
   - tenant isolation
   - empty results when budget/forecast is missing

4. If summary refresh exists:
   - run the refresh path twice in test setup or dedicated test
   - confirm no duplicate/broken results
   - confirm documentation was updated if previously missing

5. Ensure the full solution still builds if production code changed:
   - `dotnet build`

# Risks and follow-ups
- The repo may not yet have stable integration-test seeding utilities for finance/reporting data; if so, add the smallest reusable helper set needed.
- Variance percentage rules may be ambiguous when baseline values are zero; avoid inventing behavior unless already defined in code/tests. If encountered, document the observed contract and add a follow-up backlog note.
- Cost center enablement may be represented via tenant settings, feature flags, or schema shape; align tests with the real implementation rather than assumptions.
- If tenant scoping is enforced indirectly through middleware/auth context, tests must use the same path or they may produce false confidence.
- If summary tables/materialized views exist but are not test-friendly, add a follow-up to improve refresh-job observability and test hooks after this task.
- If current API behavior returns partial rows instead of empty results when budget/forecast is absent, confirm acceptance criteria and adjust implementation to match before finalizing tests.