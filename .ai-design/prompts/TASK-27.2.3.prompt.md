# Goal
Implement backlog task **TASK-27.2.3 — Build baseline planning seed service for current and newly onboarded companies** for story **US-27.2 Add budget and forecast planning models with seeded baseline data**.

Deliver a production-ready vertical slice in the existing .NET modular monolith that:
- adds persistent **budget** and **forecast** planning tables in PostgreSQL,
- exposes tenant-scoped APIs for budget CRUD/list/version retrieval and forecast querying,
- introduces **idempotent baseline seed/backfill logic** for both newly created and already onboarded companies,
- ensures all planning rows reference valid tenant chart-of-accounts accounts,
- prevents duplicates across the uniqueness dimensions:
  - `companyId`
  - `period`
  - `accountId`
  - `version`
  - optional `costCenterId`

Keep the implementation aligned with the architecture:
- shared-schema multi-tenancy with `company_id` enforcement,
- ASP.NET Core + application layer + infrastructure persistence,
- CQRS-lite,
- background-safe/idempotent seed behavior,
- no direct DB access from controllers.

# Scope
In scope:
- PostgreSQL migration(s) for planning tables and indexes/constraints.
- Domain/application/infrastructure support for:
  - budget records,
  - forecast records,
  - baseline planning seed service,
  - backfill logic for existing companies.
- Budget API endpoints:
  - create,
  - update,
  - list by company and period,
  - version-filtered retrieval.
- Forecast API endpoint(s):
  - return month-level forecast values by account and version for a company and period range.
- Validation that planning rows only use accounts that exist for the same tenant.
- Idempotent insert/upsert behavior for seed and backfill execution.
- Automated tests covering migration assumptions, API behavior, tenant scoping, and idempotency.

Out of scope unless required by existing patterns:
- UI work in Blazor or MAUI.
- Advanced forecasting algorithms.
- Cost center management feature build-out beyond nullable FK/reference handling if a cost center table already exists.
- Workflow/orchestration integration beyond invoking seed logic from company onboarding and a backfill entry point/job.
- Bulk import/export UX.

# Files to touch
Inspect the solution structure first and adapt to existing conventions. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add planning entities/value objects/enums if the domain layer owns them.
- `src/VirtualCompany.Application/**`
  - commands/queries/DTOs/validators/handlers for budgets and forecasts,
  - interfaces for planning repositories/services,
  - seed/backfill orchestration service contracts.
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations or repository implementations,
  - migration files / SQL scripts,
  - seed service implementation,
  - onboarding hook / background job implementation if infrastructure owns it.
- `src/VirtualCompany.Api/**`
  - controllers or minimal API endpoints for budget and forecast APIs,
  - request/response contracts if API-local,
  - DI registration.
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for budget and forecast endpoints,
  - seed/backfill idempotency tests,
  - tenant isolation tests.
- `docs/postgresql-migrations-archive/**`
  - only if this repo tracks migration documentation/archive updates.
- `README.md`
  - only if API or migration usage docs need a brief update.

Also inspect for existing modules related to:
- companies/onboarding,
- chart of accounts/accounts,
- accounting/finance,
- background jobs,
- tenant resolution,
- migrations.

Prefer extending existing finance/accounting modules over creating parallel structures.

# Implementation plan
1. **Discover existing finance/accounting and persistence patterns**
   - Find:
     - how entities are modeled,
     - whether EF Core or raw SQL migrations are used,
     - how tenant scoping is enforced,
     - where company onboarding hooks live,
     - whether chart-of-accounts and cost center tables already exist.
   - Reuse naming, folder layout, and endpoint style already present in the repo.
   - Do not invent a new architectural pattern if one already exists.

2. **Design the planning data model**
   Create two month-grain planning tables, likely:
   - `budget_records`
   - `forecast_records`

   Include fields consistent with existing conventions, but ensure at minimum:
   - `id`
   - `company_id`
   - `period` or equivalent month identifier
   - `account_id`
   - `version`
   - `cost_center_id` nullable
   - numeric amount/value field
   - timestamps (`created_at`, `updated_at`)
   - optional metadata if the codebase conventionally includes it

   Requirements:
   - month-level granularity only,
   - tenant-owned rows,
   - FK/reference to valid tenant account,
   - nullable cost center support,
   - uniqueness across company/period/account/version/cost center.

   Important uniqueness detail:
   - PostgreSQL unique constraints treat `NULL` values as distinct.
   - To satisfy “optional costCenterId” uniqueness without duplicates when null, implement one of:
     - a unique index using `coalesce(cost_center_id, '00000000-0000-0000-0000-000000000000'::uuid)` if UUIDs are used and safe by convention, or
     - two partial unique indexes:
       - one for rows where `cost_center_id is null` on `(company_id, period, account_id, version)`
       - one for rows where `cost_center_id is not null` on `(company_id, period, account_id, version, cost_center_id)`
   - Prefer the partial unique index approach unless the repo already standardizes on expression indexes.

3. **Create the database migration**
   Add migration(s) that:
   - create budget and forecast tables,
   - add FK constraints to company and account tables,
   - optionally add FK to cost center table if it exists and is appropriate,
   - add indexes for common query paths:
     - by `company_id`,
     - by `company_id + period`,
     - by `company_id + version`,
     - by `company_id + period range`,
     - by `company_id + account_id`.
   - add uniqueness enforcement with null-safe handling for optional cost center.
   - ensure rollback/down migration is valid if the project supports it.

   If account tenancy cannot be enforced by FK alone, enforce in application logic too:
   - when creating/updating rows, verify the account belongs to the same company.

4. **Add domain/application models**
   Introduce planning models and contracts:
   - budget entity/model,
   - forecast entity/model,
   - DTOs for API responses,
   - commands/queries for:
     - create budget record,
     - update budget record,
     - list budget records by company and period,
     - get budget records by version,
     - get forecast records by company and period range, grouped or filterable by account/version.

   Include validation for:
   - required company context,
   - valid month period format/type,
   - non-empty version,
   - valid account id,
   - amount/value numeric constraints,
   - optional cost center id,
   - tenant ownership of referenced account.

5. **Implement repositories/query services**
   Add persistence methods needed for:
   - create budget record,
   - update budget record,
   - list budget records by company and period,
   - retrieve budget records by version,
   - retrieve forecast records by company and period range,
   - existence checks for idempotent seed/backfill,
   - bulk insert/upsert where appropriate.

   Prefer efficient batch operations for seed/backfill.
   If EF Core is used, be careful with large loops and N+1 queries.

6. **Implement Budget APIs**
   Add tenant-scoped endpoints matching existing API style:
   - `POST` create budget record
   - `PUT`/`PATCH` update budget record
   - `GET` list budget records by company and period
   - `GET` retrieve budget records filtered by version

   Requirements:
   - company context must come from tenant resolution / route / auth pattern already used in the app,
   - reject cross-tenant account references,
   - return safe validation errors for uniqueness conflicts,
   - preserve month-level semantics.

   If the codebase prefers a single list endpoint with query params, support:
   - `period`
   - `version`
   - optional `accountId`
   - optional `costCenterId`

   That is acceptable as long as acceptance criteria are met.

7. **Implement Forecast APIs**
   Add tenant-scoped query endpoint(s) that return:
   - month-level forecast values,
   - by account and version,
   - for a requested company and period range.

   Support query parameters such as:
   - `startPeriod`
   - `endPeriod`
   - optional `version`
   - optional `accountId`

   Ensure results are deterministic and sorted, ideally by:
   - period ascending,
   - account,
   - version.

8. **Implement baseline planning seed logic**
   Build a dedicated service, e.g. `BaselinePlanningSeedService`, that:
   - for a given company:
     - loads valid chart-of-accounts accounts for that tenant,
     - determines which planning rows are missing,
     - creates baseline budget and forecast rows,
     - does not duplicate existing rows on repeated execution.

   Seed behavior expectations:
   - baseline data should be generated for both budgets and forecasts,
   - only for valid tenant accounts already present,
   - should work for:
     - newly created companies,
     - already onboarded companies missing planning data.

   Use a pragmatic baseline strategy unless the repo already defines one. For example:
   - create rows for a default planning horizon such as current fiscal year / next 12 months,
   - use a default version like `"baseline"` or repo-standard equivalent,
   - initialize values to `0` if no better baseline source exists.

   If there is existing accounting/history data that can reasonably seed values, only use it if already available and low-risk. Do not over-engineer forecasting logic for this task.

9. **Make seed/backfill idempotent**
   This is critical.
   Repeated execution must not create duplicates.

   Implement idempotency through both:
   - database uniqueness constraints/indexes,
   - application logic that inserts only missing rows or uses upsert semantics.

   Preferred approaches:
   - PostgreSQL `INSERT ... ON CONFLICT DO NOTHING` if using SQL/raw commands and conflict target can match the unique index strategy,
   - or precompute missing keys and bulk insert only absent rows,
   - or use partial-index-aware logic if `ON CONFLICT` is awkward with nullable cost center uniqueness.

   Ensure concurrent execution is also safe enough:
   - duplicate attempts should fail harmlessly or no-op,
   - service should be retry-safe.

10. **Hook seed logic into new company onboarding**
   Find where company creation/onboarding completes and invoke the seed service there.
   Requirements:
   - seed runs after company and chart-of-accounts setup prerequisites are satisfied,
   - failures should be logged and handled according to existing patterns,
   - avoid blocking onboarding longer than necessary if the app already uses background jobs/outbox for setup tasks.

   If onboarding already has a post-create pipeline/event, plug into that.
   If not, add the smallest clean integration point.

11. **Add backfill path for existing companies**
   Provide a way to backfill already onboarded companies.
   Prefer one of:
   - application service callable from a background job/startup task/admin command,
   - hosted service/job that scans companies and seeds missing planning data safely,
   - internal endpoint only if the repo already uses admin/internal endpoints.

   Keep it safe and idempotent.
   Avoid expensive full rewrites if only missing rows need to be inserted.

12. **Add tests**
   Cover at minimum:
   - migration creates required tables/indexes/constraints,
   - budget create succeeds for valid tenant account,
   - budget update works,
   - budget list by company and period works,
   - budget version-filtered retrieval works,
   - forecast query by company and period range works,
   - cross-tenant account reference is rejected,
   - duplicate budget/forecast rows for same uniqueness key are prevented,
   - seed for new company creates baseline rows,
   - backfill for existing company inserts only missing rows,
   - repeated seed/backfill execution is idempotent.

   If integration tests are expensive, prioritize API-level tests plus service-level tests for seed logic.

13. **Keep implementation clean**
   - Use existing naming and coding conventions.
   - Keep controllers thin.
   - Put business rules in application/domain services.
   - Add structured logging around seed/backfill execution with company context.
   - Do not expose internal seed mechanics in public APIs unless already standard in the repo.

# Validation steps
1. Inspect and restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are code-generated in this repo, verify migration compiles and applies cleanly against PostgreSQL.

4. Manually validate acceptance criteria through tests or local API calls:
   - create a budget row for a valid tenant account,
   - update it,
   - list by company and period,
   - retrieve by version,
   - query forecasts by period range,
   - attempt duplicate insert for same company/period/account/version/cost center and confirm no duplicate row is created,
   - attempt create/update using another tenant’s account and confirm rejection,
   - run seed for a new company and confirm baseline budget + forecast rows exist,
   - run backfill twice for an existing company and confirm row counts do not increase after the first complete pass.

5. Validate null cost center uniqueness specifically:
   - insert one row with `cost_center_id = null`,
   - attempt same uniqueness key again with `cost_center_id = null`,
   - confirm duplicate is blocked/no-op.

6. Validate tenant scoping:
   - ensure APIs cannot read or mutate another company’s planning rows.

# Risks and follow-ups
- **Null handling in unique constraints on PostgreSQL** is the main correctness risk. Use partial unique indexes or another null-safe strategy deliberately.
- **Onboarding order dependency** may matter if chart-of-accounts seeding happens after company creation. Ensure planning seed runs only after accounts exist.
- **Backfill performance** could become expensive for many companies/accounts/months. Use batching and insert-only-missing logic.
- **Cost center FK ambiguity** may