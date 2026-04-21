# Goal
Implement backlog task **TASK-27.2.1 — Create Budget and Forecast entities with EF Core mappings, constraints, and migrations** for story **US-27.2 Add budget and forecast planning models with seeded baseline data**.

Deliver a complete vertical slice for persistence and seed/backfill support of planning data in the .NET modular monolith using **EF Core + PostgreSQL**, ensuring:

- New **Budget** and **Forecast** entities exist in the domain and infrastructure layers.
- EF Core mappings enforce required relationships, indexes, uniqueness, and tenant-safe constraints.
- A migration creates the underlying tables and indexes.
- Seed/backfill logic creates baseline planning rows for new and existing companies without duplicates.
- Records reference valid tenant chart-of-accounts accounts.
- The implementation is idempotent and safe to rerun.

This task should focus primarily on **data model, persistence, migration, and seed/backfill plumbing**, while adding only the minimum API/application surface needed to satisfy the acceptance criteria if those endpoints are already scaffolded or expected by the current architecture.

# Scope
In scope:

- Add domain entities/value objects/enums as needed for:
  - `Budget`
  - `Forecast`
- Add EF Core configurations and `DbSet<>` registrations.
- Add PostgreSQL migration for:
  - `budgets` table
  - `forecasts` table
  - foreign keys to `companies`, chart-of-accounts/accounts table, and optional cost center table if present
  - uniqueness constraints/indexes covering:
    - `company_id`
    - `period`
    - `account_id`
    - `version`
    - optional `cost_center_id`
- Ensure nullable `cost_center_id` uniqueness works correctly in PostgreSQL. Prefer a robust approach such as:
  - unique index on `(company_id, period, account_id, version, cost_center_id)` plus
  - a second partial unique index for rows where `cost_center_id IS NULL`
  - or an equivalent PostgreSQL-safe strategy.
- Add repository/query support needed for:
  - create/update/list budget records by company and period
  - version-filtered budget retrieval
  - forecast retrieval by company, period range, account, and version
- Add seed/backfill service logic for:
  - newly created companies
  - already onboarded companies missing planning data
- Make seed/backfill idempotent and duplicate-safe.
- Add tests covering mappings, uniqueness behavior, and seed idempotency where practical.

Out of scope unless required by existing code patterns:

- Full UI work in Blazor or MAUI.
- Rich forecasting algorithms.
- Non-baseline planning workflows.
- Broad refactors unrelated to planning models.
- New cost center module if none exists already. If cost centers do not exist, keep `cost_center_id` nullable and only wire FK if a table/entity already exists.

# Files to touch
Inspect the solution first and adapt to actual project structure, but expect to touch files in these areas.

## Domain
Likely under `src/VirtualCompany.Domain/...`

- Add:
  - `Entities/Budget.cs`
  - `Entities/Forecast.cs`
- Possibly add/update:
  - shared base entity/tenant-owned abstractions
  - planning enums/constants such as version/source/status if the domain uses them
  - account reference navigation types if needed

## Application
Likely under `src/VirtualCompany.Application/...`

- Add/update:
  - commands/queries/DTOs for budget create/update/list and forecast retrieval if not already present
  - interfaces for planning repositories/services
  - seed/backfill orchestration service contracts
- If APIs already exist, wire handlers to persistence.

## Infrastructure
Likely under `src/VirtualCompany.Infrastructure/...`

- Update `DbContext`
- Add EF configurations:
  - `Persistence/Configurations/BudgetConfiguration.cs`
  - `Persistence/Configurations/ForecastConfiguration.cs`
- Add repositories/query services
- Add seed/backfill implementation
- Add migration files under EF migrations folder
- If there is existing company bootstrap logic, update it to invoke planning seed creation after company creation

## API
Likely under `src/VirtualCompany.Api/...`

- Add/update controllers/endpoints for:
  - budget create
  - budget update
  - budget list by company + period
  - budget retrieval filtered by version
  - forecast retrieval by company + period range + account/version filters
- Register services in DI

## Tests
Likely under `tests/VirtualCompany.Api.Tests/...` and/or other test projects

- Add/update:
  - persistence tests for uniqueness/index behavior where feasible
  - API tests for budget/forecast retrieval semantics
  - seed/backfill idempotency tests
  - tenant/account validation tests

## Documentation / migration notes
- If the repo tracks migration notes, update any relevant docs or README snippets.

# Implementation plan
1. **Discover existing accounting and tenant model**
   - Inspect current entities and schema for:
     - `Company`
     - chart-of-accounts / account entity name
     - optional `CostCenter` entity/table
     - existing seeding/backfill patterns
     - existing migration conventions
     - naming conventions for PostgreSQL tables/columns
   - Reuse existing tenant-owned base classes and audit fields.
   - Do not invent parallel accounting entities if account models already exist.

2. **Design the planning entity shape**
   - Model month-level records, one row per unique planning grain.
   - Recommended minimum fields for both `Budget` and `Forecast`:
     - `Id`
     - `CompanyId`
     - `Period` or `PeriodStart` representing the month
     - `AccountId`
     - `Version`
     - `CostCenterId` nullable
     - numeric amount/value field
     - created/updated timestamps
   - If the codebase already uses year/month split or a date-only month representation, align with that instead of introducing a conflicting pattern.
   - Keep the model simple and deterministic for baseline seeding.

3. **Add domain entities**
   - Create `Budget` and `Forecast` entities in the domain project.
   - Include navigation properties to:
     - `Company`
     - account/chart-of-accounts entity
     - optional cost center entity if available
   - Add guard clauses or factory/update methods if the domain style prefers encapsulation over public setters.
   - Ensure records cannot exist without valid `CompanyId`, `AccountId`, `Version`, and month period.

4. **Add EF Core mappings**
   - Register `DbSet<Budget>` and `DbSet<Forecast>` in the infrastructure `DbContext`.
   - Create explicit entity configurations with:
     - table names `budgets` and `forecasts` unless conventions differ
     - PK on `id`
     - required columns for tenant, period, account, version, amount
     - FK to company
     - FK to account
     - FK to cost center only if supported by existing schema
     - indexes for common query paths:
       - `(company_id, period)`
       - `(company_id, period, version)`
       - `(company_id, account_id, version, period)` or equivalent for forecast retrieval
     - uniqueness enforcement for the planning grain
   - For nullable `cost_center_id`, implement PostgreSQL-safe uniqueness:
     - unique index on `(company_id, period, account_id, version, cost_center_id)` for non-null cost centers
     - partial unique index on `(company_id, period, account_id, version)` where `cost_center_id IS NULL`
   - Use explicit column types for money/amount values consistent with the rest of the accounting model, likely `numeric(18,2)` unless the repo uses another standard.

5. **Create migration**
   - Generate an EF Core migration after mappings are in place.
   - Review the generated migration manually and adjust if needed for:
     - partial unique indexes
     - PostgreSQL-specific SQL
     - FK delete behavior
   - Ensure migration is deterministic and reversible.
   - Name the migration clearly, e.g. `AddBudgetAndForecastPlanning`.

6. **Implement account validation**
   - Ensure budget/forecast records can only reference accounts belonging to the same tenant/company.
   - Prefer enforcing this in application/service logic before insert/update.
   - If the account table includes `company_id`, validate:
     - account exists
     - account belongs to the target company
   - If cost center exists, validate tenant ownership there too.
   - Return safe validation errors rather than raw DB exceptions where possible.

7. **Implement persistence/query services**
   - Add repository or query service methods for:
     - create budget row
     - update budget row
     - list budget rows by company and period
     - retrieve budget rows by company, period, and version
     - retrieve forecast rows by company, period range, account, and version
   - Keep all queries tenant-scoped.
   - Use CQRS-lite patterns already present in the solution.

8. **Implement seed and backfill logic**
   - Find existing company creation/onboarding flow and hook in baseline planning seed creation.
   - Add a backfill service for already onboarded companies:
     - scan companies
     - determine whether baseline budget/forecast data is missing
     - insert only missing rows
   - Seed only against valid tenant accounts already present in chart of accounts.
   - Baseline data should be deterministic. Prefer a simple baseline strategy such as:
     - one default version like `baseline`
     - month-level rows for a defined planning horizon
     - zero or template-derived values
   - Use idempotent insert logic:
     - pre-check existing keys in memory for batch inserts, and/or
     - rely on unique indexes with conflict-safe behavior if the repo already uses raw SQL/upsert patterns
   - Repeated execution must not create duplicates.

9. **Wire minimal API/application endpoints**
   - If budget/forecast endpoints already exist partially, complete them.
   - Otherwise add minimal endpoints matching existing API conventions:
     - `POST` budget create
     - `PUT/PATCH` budget update
     - `GET` budget list by company + period
     - `GET` budget list filtered by version
     - `GET` forecast list by company + period range + optional account/version filters
   - Ensure authorization and company scoping follow existing tenant patterns.
   - Keep request/response DTOs lean and aligned to month-level planning records.

10. **Add tests**
   - Add tests for:
     - migration/model creates expected uniqueness behavior
     - duplicate insert for same `(company, period, account, version, costCenter)` fails or is ignored according to service behavior
     - null `cost_center_id` duplicate prevention works
     - seed/backfill reruns are idempotent
     - invalid cross-tenant account references are rejected
     - budget list/version filter and forecast range retrieval return correct rows
   - Prefer integration tests against the real persistence provider if the test setup supports PostgreSQL; otherwise cover service logic and document any provider limitations around partial indexes.

11. **Keep implementation aligned with architecture**
   - Maintain modular monolith boundaries:
     - domain entities in Domain
     - orchestration/use cases in Application
     - EF/migrations in Infrastructure
     - HTTP concerns in Api
   - Do not let controllers access DbContext directly if the codebase uses handlers/services.

# Validation steps
Run these steps after implementation and include any required migration commands in your work log.

1. **Restore/build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Verify migration**
   - Ensure the new migration is present in the infrastructure startup assembly used for EF.
   - Apply the migration to a local/dev database.
   - Confirm tables and indexes exist:
     - `budgets`
     - `forecasts`
     - unique indexes including null-cost-center handling

4. **Manual persistence checks**
   - Insert a budget row for a valid company/account/version/period.
   - Attempt duplicate insert with same key and confirm duplicate prevention.
   - Attempt same key with different version and confirm allowed.
   - Attempt same key with null `cost_center_id` twice and confirm duplicate prevention.
   - Attempt insert using an account from another company and confirm rejection.

5. **Seed/backfill checks**
   - Run company creation flow and confirm baseline budget/forecast rows are created.
   - Run seed/backfill a second time and confirm row counts do not increase unexpectedly.
   - Run backfill for an existing company missing some planning rows and confirm only missing rows are inserted.

6. **API checks**
   - Verify budget create/update/list endpoints work for tenant-scoped requests.
   - Verify version-filtered budget retrieval returns only matching version rows.
   - Verify forecast retrieval returns month-level values for requested company and period range, with account/version filters applied.

7. **Code quality checks**
   - Ensure no tenant-unscoped queries were introduced.
   - Ensure migration code is reviewed for PostgreSQL correctness, especially partial unique indexes.
   - Ensure nullable FK handling and delete behaviors are explicit.

# Risks and follow-ups
- **Nullable uniqueness in PostgreSQL**: standard unique indexes allow multiple nulls, so this must be handled explicitly with a partial unique index or equivalent. Review generated migration carefully.
- **Unknown existing accounting schema**: the actual account entity/table name may differ from assumptions. Adapt to the existing chart-of-accounts model rather than creating a duplicate abstraction.
- **Cost center availability**: if no cost center entity exists yet, keep the column nullable and avoid introducing a premature module. If acceptance requires the column now, add it only as a nullable FK-ready field unless a real table already exists.
- **Seed volume/horizon ambiguity**: the story requires baseline data but does not define horizon or values. Use the smallest deterministic baseline compatible with current business rules and document assumptions in code comments or task notes.
- **Cross-tenant FK safety**: relational FKs alone may not guarantee same-company account usage unless the schema is modeled that way. Application validation is required.
- **API scope creep**: if full budget/forecast APIs are not already in place, implement only the minimal endpoints needed by acceptance criteria and avoid broad planning UX work.
- **Testing provider gaps**: EF in-memory/SQLite may not faithfully validate PostgreSQL partial indexes. Prefer PostgreSQL-backed integration tests if available; otherwise document the limitation and cover service-level idempotency thoroughly.
- **Follow-up suggestion**: after this task, consider a dedicated