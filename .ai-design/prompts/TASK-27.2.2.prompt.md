# Goal

Implement backlog task **TASK-27.2.2 — Implement budget CRUD APIs and forecast retrieval APIs with tenant and period filters** for story **US-27.2 Add budget and forecast planning models with seeded baseline data**.

Deliver a complete vertical slice in the existing **.NET modular monolith** that adds tenant-scoped planning persistence, APIs, and idempotent seed/backfill behavior for **budget** and **forecast** month-level records.

The implementation must satisfy these acceptance criteria:

- Add a **database migration** creating budget and forecast tables with uniqueness constraints covering:
  - `companyId`
  - `period`
  - `accountId`
  - `version`
  - optional `costCenterId`
- Implement **Budget APIs** for:
  - create
  - update
  - list
  - version-filtered retrieval
  - all scoped by company and period
- Implement **Forecast APIs** for:
  - retrieval of month-level forecast values
  - filtered by company
  - period range
  - account
  - version
- Implement **seed/backfill logic** that:
  - creates baseline planning data for newly created companies
  - backfills missing planning data for already onboarded companies
  - is **idempotent** and does not create duplicates
- Enforce that budget and forecast rows reference valid tenant-owned chart-of-accounts accounts
- Preserve strict **tenant isolation** in all queries and writes

Use existing project conventions and architecture. Prefer minimal, cohesive changes over speculative abstractions.

# Scope

In scope:

- Domain/entity additions for budget and forecast planning records
- EF Core configuration and migration for new planning tables
- Uniqueness/index strategy that correctly handles nullable `costCenterId`
- Application commands/queries/services for budget CRUD and forecast retrieval
- API endpoints/controllers for budget and forecast operations
- Tenant/company scoping and validation against tenant-owned accounts
- Seed/backfill service for baseline planning data
- Automated tests covering:
  - migration behavior where practical
  - API behavior
  - tenant scoping
  - uniqueness/idempotency
  - account validation
  - period/version filters

Out of scope unless required by existing patterns:

- UI work in Blazor or mobile
- Full cost center domain implementation beyond nullable foreign key/reference handling
- Advanced forecasting generation logic beyond baseline seeded values and retrieval
- New auth model changes
- New reporting/dashboard features

# Files to touch

Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

## Domain
- Planning entities/value objects, e.g.:
  - `BudgetRecord`
  - `ForecastRecord`
- Shared enums/constants for planning versions if the codebase uses them
- Optional domain validation helpers for period/month semantics

## Infrastructure
- `DbContext` / EF Core model registration
- Entity type configuration classes
- New EF migration files
- Seed/backfill implementation
- Repository/query implementations if infrastructure owns them

## Application
- Commands:
  - create budget record(s)
  - update budget record(s)
- Queries:
  - list budgets by company/period/version
  - retrieve forecasts by company/period range/account/version
- DTOs/contracts
- Validators
- Service interfaces and handlers

## API
- Budget controller/endpoints
- Forecast controller/endpoints
- Request/response contracts if API-specific
- DI registration if needed

## Tests
- API integration tests
- Application/service tests
- Seed/backfill idempotency tests
- Tenant/account validation tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to align with migration and local workflow conventions.

# Implementation plan

1. **Discover existing patterns before coding**
   - Inspect how the solution currently models:
     - tenant-owned entities
     - chart of accounts / accounts
     - period/date fields
     - EF migrations
     - seeding/backfill on company creation
     - API controllers and test style
   - Reuse naming, folder structure, MediatR/CQRS style, validation style, and endpoint conventions already present.
   - Identify whether “period” is represented as:
     - first day of month `DateOnly`/`DateTime`
     - `YYYY-MM`
     - year/month ints
   - Follow the existing representation consistently.

2. **Design the planning persistence model**
   - Add two tenant-owned tables/entities:
     - `budget_records`
     - `forecast_records`
   - Each row should represent a **month-level planning value**.
   - Include at minimum:
     - `id`
     - `company_id`
     - `period`
     - `account_id`
     - `version`
     - `cost_center_id` nullable
     - numeric amount/value field
     - timestamps
   - If the codebase already uses audit columns or concurrency tokens, include them.
   - Ensure account references are to the tenant’s chart-of-accounts records.

3. **Implement uniqueness constraints correctly**
   - Acceptance criteria require uniqueness across:
     - company
     - period
     - account
     - version
     - optional cost center
   - Because nullable columns can behave differently in PostgreSQL unique indexes, implement this carefully.
   - Preferred approach:
     - either use two unique indexes:
       - one for rows where `cost_center_id is null`
       - one for rows where `cost_center_id is not null`
     - or another PostgreSQL-safe equivalent already used in the codebase
   - Apply the same uniqueness strategy to both budget and forecast tables.
   - Add supporting non-unique indexes for common filters:
     - `company_id + period`
     - `company_id + version`
     - `company_id + period range`
     - `company_id + account_id`

4. **Add EF Core configuration and migration**
   - Register entities in the DbContext.
   - Add entity type configurations with:
     - table names
     - key mappings
     - precision for numeric values
     - foreign keys
     - indexes
     - required/optional fields
   - Create a migration that:
     - creates both tables
     - creates indexes/constraints
     - preserves referential integrity to accounts
   - Keep migration names descriptive and aligned with repo conventions.

5. **Implement domain/application validation**
   - Validate:
     - company context is present and enforced
     - account exists and belongs to the same company
     - period is valid month-level input
     - version is required
     - duplicate create attempts are rejected or safely handled according to endpoint semantics
   - For updates, ensure the target row belongs to the requesting company.
   - If batch create/update is more natural in the codebase, support it only if it does not expand scope too much.

6. **Implement Budget APIs**
   - Add endpoints for:
     - **create** budget record(s)
     - **update** budget record(s)
     - **list** budget records by company and period
     - **version-filtered retrieval** by company, period, and version
   - Keep endpoints tenant-scoped using the existing company resolution mechanism.
   - Suggested behavior:
     - create: inserts new month-level budget rows
     - update: updates existing rows by id or natural key, depending on existing API style
     - list: returns month-level rows for a company and period or period range
     - version filter: allows retrieval for a specific version
   - Return stable DTOs with account/version/period/value/costCenter metadata.
   - Use pagination only if existing list endpoints require it.

7. **Implement Forecast retrieval APIs**
   - Add read endpoints that return month-level forecast values filtered by:
     - company
     - period range
     - optional account
     - optional version
   - Ensure retrieval supports “by account and version” as required.
   - Keep forecast APIs read-only unless existing story decomposition clearly expects write endpoints here. This task only requires retrieval APIs.

8. **Implement seed and backfill logic**
   - Find the existing company creation flow and seed hooks.
   - Add baseline planning seed generation for newly created companies.
   - Add a backfill path for already onboarded companies that inserts missing planning rows only.
   - Seed data should:
     - use valid tenant accounts already present
     - create baseline budget and forecast rows for expected periods/versions
     - avoid duplicates on repeated execution
   - Prefer idempotent upsert-like behavior or existence checks backed by DB uniqueness.
   - If there is an existing hosted service, migration runner, or admin/backfill command pattern, plug into that rather than inventing a new mechanism.

9. **Handle idempotency explicitly**
   - Repeated seed/backfill execution must not create duplicate rows.
   - Implement this with both:
     - database uniqueness constraints
     - application logic that only inserts missing rows
   - If using bulk insert patterns, ensure duplicate conflicts are ignored or merged safely in PostgreSQL-compatible ways.
   - Add tests proving reruns are safe.

10. **Enforce tenant-owned account references**
    - On create/update/seed/backfill:
      - verify the referenced account exists
      - verify it belongs to the same company
    - Reject cross-tenant account references.
    - Add tests for forbidden/not found/validation behavior based on existing API conventions.

11. **Add tests**
    - Cover at least:
      - budget create success
      - budget update success
      - budget list by company and period
      - budget retrieval filtered by version
      - forecast retrieval by company and period range
      - forecast retrieval filtered by account and version
      - invalid account reference rejected
      - cross-tenant access blocked
      - duplicate natural key prevented
      - seed/backfill rerun does not duplicate rows
    - Prefer integration tests through the API where the test suite already supports them.

12. **Keep implementation aligned with architecture**
    - Respect modular monolith boundaries:
      - API thin
      - application owns use cases
      - infrastructure owns persistence
      - domain owns core entities/rules where appropriate
    - Do not bypass application layer from controllers.
    - Do not access DB directly from API endpoints.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If the repo uses EF migrations via CLI, verify migration generation/application flow according to repo conventions.

4. Manually validate these scenarios through tests or local API execution:
   - Create budget row for valid tenant account
   - Update existing budget row
   - List budget rows for a company and period
   - Retrieve budget rows filtered by version
   - Retrieve forecast rows for a company over a period range
   - Retrieve forecast rows filtered by account and version
   - Attempt create with account from another company and confirm rejection
   - Re-run seed/backfill and confirm row counts do not increase unexpectedly
   - Confirm duplicate natural key insert is prevented by DB/app behavior

5. Verify migration shape:
   - both planning tables exist
   - foreign keys exist
   - uniqueness constraints/indexes correctly handle nullable `cost_center_id`
   - common filter indexes are present

6. Verify tenant isolation:
   - requests scoped to one company cannot read or mutate another company’s planning rows

# Risks and follow-ups

- **Nullable `costCenterId` uniqueness in PostgreSQL** is the biggest correctness risk. Use partial unique indexes or an equivalent PostgreSQL-safe strategy.
- The existing codebase may not yet have a **cost center** table/domain. If absent, keep `costCenterId` nullable and avoid overbuilding; only add FK if a real table already exists or the task explicitly requires it.
- “Period” representation may already be standardized elsewhere. Do not introduce a conflicting format.
- Seed/backfill scope may be ambiguous:
  - if no baseline planning horizon/version is defined in code, choose a conservative default and document it in code comments/tests
  - prefer configuration/constants over magic values
- If chart-of-accounts seeding happens asynchronously relative to company creation, ensure planning seed runs only after accounts exist or safely retries later.
- Follow-up backlog likely needed for:
  - forecast write APIs
  - cost center management
  - planning version management endpoints
  - richer baseline generation rules
  - UI surfaces for planning management