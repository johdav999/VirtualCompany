# SQL Server Migration Implementation Plan

Branch: `feature/db-sqlserver-docker-migration`

## Scope

This plan converts the solution from the current PostgreSQL/Npgsql implementation to SQL Server for local development and Azure SQL for production. It is implementation planning only. No code or configuration changes are included in this document.

## Current baseline confirmed in repo

- Runtime provider is PostgreSQL via `UseNpgsql(...)` in [src/VirtualCompany.Infrastructure/DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs).
- EF design-time provider is PostgreSQL via `UseNpgsql(...)` in [src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs).
- PostgreSQL package is referenced in [src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj).
- SQL Server package is already referenced in [src/VirtualCompany.Api/VirtualCompany.Api.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/VirtualCompany.Api.csproj), but it is not the active provider.
- API appsettings use PostgreSQL connection strings in [src/VirtualCompany.Api/appsettings.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.json) and [src/VirtualCompany.Api/appsettings.Development.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.Development.json).
- Docker is PostgreSQL in [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml).
- JSON storage is PostgreSQL-specific via `jsonb` in [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs).
- Existing migrations in [src/VirtualCompany.Infrastructure/Persistence/Migrations](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations) are PostgreSQL-specific.
- API integration tests use EF InMemory in [tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs).

## Target state

- Local development database: SQL Server in Docker.
- Production database: Azure SQL.
- Single EF Core provider family: `Microsoft.EntityFrameworkCore.SqlServer`.
- JSON persisted in SQL Server-compatible storage, most likely `nvarchar(max)` with application-side serialization.
- Fresh SQL Server baseline migration replaces the active PostgreSQL migration chain.
- Test strategy evolves toward relational SQL Server-backed integration coverage.

## Phase 1: Provider and package standardization

### Goal

Make Infrastructure the single source of truth for the SQL Server provider and remove PostgreSQL package ownership.

### Files to modify

- [src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj)
- [src/VirtualCompany.Api/VirtualCompany.Api.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/VirtualCompany.Api.csproj)

### Changes

- Add `Microsoft.EntityFrameworkCore.SqlServer` to Infrastructure.
- Remove `Npgsql.EntityFrameworkCore.PostgreSQL` from Infrastructure.
- Decide whether to keep or remove `Microsoft.EntityFrameworkCore.SqlServer` from API:
  - Preferred: remove from API if Infrastructure owns all provider registration.
  - Keep `Microsoft.EntityFrameworkCore.Design` in API if `dotnet ef` commands continue to run from API or transitively require it.

### Risks

- Package ownership split can remain confusing if SQL Server stays referenced in both API and Infrastructure.
- EF tooling can break if provider/design package placement becomes inconsistent.

### Validation

- `dotnet restore`
- `dotnet build VirtualCompany.sln`
- Confirm `rg -n "Npgsql|UseNpgsql"` only returns intentionally preserved historical references before later cleanup.

### Suggested commit boundary

- `chore: standardize EF provider packages on SQL Server`

## Phase 2: Runtime and design-time provider switch

### Goal

Switch actual application and EF tooling behavior from PostgreSQL to SQL Server.

### Files to modify

- [src/VirtualCompany.Infrastructure/DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs)
- [src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs)

### Changes

- Replace `UseNpgsql(connectionString)` with `UseSqlServer(connectionString, ...)`.
- Remove PostgreSQL fallback connection string and replace it with SQL Server format.
- Add SQL Server-friendly options if desired:
  - `EnableRetryOnFailure(...)` for Azure SQL transient fault handling.
- Update the design-time factory to use `UseSqlServer(...)` with a SQL Server connection string consistent with local Docker.

### Risks

- Design-time/runtime drift if the app uses one connection string source and EF tooling uses another.
- Azure SQL-specific retry options can hide local issues if configured too loosely.

### Validation

- `dotnet build VirtualCompany.sln`
- `dotnet ef migrations list --project src/VirtualCompany.Infrastructure --startup-project src/VirtualCompany.Api`
- Confirm the design-time factory can instantiate the context.

### Suggested commit boundary

- `refactor: switch runtime and design-time EF provider to SQL Server`

## Phase 3: Configuration and environment alignment

### Goal

Replace PostgreSQL configuration with SQL Server configuration for local development, while shaping production settings for Azure SQL.

### Files to modify

- [src/VirtualCompany.Api/appsettings.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.json)
- [src/VirtualCompany.Api/appsettings.Development.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.Development.json)
- [src/VirtualCompany.Api/Properties/launchSettings.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/Properties/launchSettings.json) if dev UX needs updated environment behavior
- Possibly [README.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/README.md) later in the docs phase

### Changes

- Replace PostgreSQL connection strings with SQL Server connection strings.
- Local development string should target Docker SQL Server on `localhost,1433`.
- Production appsettings should avoid embedding real Azure SQL credentials; prefer placeholders and environment-driven configuration.
- Decide on local defaults:
  - likely `TrustServerCertificate=True`
  - likely `Encrypt=False` for local Docker only
- Document expected production behavior:
  - Azure SQL connection string with encryption enabled
  - secrets injected by environment or deployment platform

### Risks

- Local and production concerns can get mixed if dev-only flags like `TrustServerCertificate=True` leak into production settings.
- The trailing comma currently present in [src/VirtualCompany.Api/appsettings.Development.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.Development.json) should be corrected as part of config cleanup if still present when implementation starts.

### Validation

- `dotnet run --project src/VirtualCompany.Api`
- Verify the app starts with the SQL Server connection string resolved from configuration.
- Confirm config parsing succeeds in both Development and default environment.

### Suggested commit boundary

- `chore: align API configuration with SQL Server local and Azure SQL production`

## Phase 4: Docker local development replacement

### Goal

Make the checked-in local database workflow use SQL Server in Docker instead of PostgreSQL.

### Files to modify

- [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml)
- [README.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/README.md)

### Changes

- Replace PostgreSQL service definition with SQL Server 2022.
- Expose port `1433`.
- Add required SQL Server environment variables such as:
  - `ACCEPT_EULA=Y`
  - `MSSQL_SA_PASSWORD=...`
- Add persistent named volume for SQL Server data.
- Optionally add a healthcheck if helpful for developer workflow.
- Rewrite README local database instructions, logs, connection settings, and sample connection string.

### Risks

- SQL Server image startup is slower and more resource-heavy than PostgreSQL.
- Weak default SA passwords will fail container startup.
- Existing developers may still have PostgreSQL data volumes or muscle memory from prior workflow.

### Validation

- `docker compose up -d`
- Verify container health and port binding on `1433`
- Connect using the configured local connection string
- Start API and confirm it can create/migrate schema against the container

### Suggested commit boundary

- `chore: replace local docker database with SQL Server`

## Phase 5: JSON storage conversion strategy

### Goal

Replace PostgreSQL-specific `jsonb` mapping with SQL Server-compatible storage while preserving current object semantics.

### Files to modify

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)

### Changes

- Replace `HasJsonbConversion<T>()` with a new provider-appropriate helper, likely something like `HasJsonConversion<T>()`.
- Remove `.HasColumnType("jsonb")`.
- Replace PostgreSQL default SQL expressions:
  - `"'{}'::jsonb"`
  - `"'[]'::jsonb"`
  with SQL Server-compatible defaults or no SQL default if app-side initialization is sufficient.
- Decide storage type:
  - simplest path: `nvarchar(max)` with EF value converter and value comparer.
- Update all call sites:
  - `Company.Branding`
  - `Company.Settings`
  - `CompanySetupTemplate.Defaults`
  - `CompanySetupTemplate.Metadata`
  - `AuditEvent.DataSources`
  - `AuditEvent.Metadata`

### Risks

- JSON default semantics can change between database-side defaults and CLR defaults.
- Existing migrations contain JSON transformation SQL that cannot be reused as-is.
- String comparison/value comparer semantics must remain stable to avoid noisy update tracking.

### Validation

- `dotnet build VirtualCompany.sln`
- Generate a test migration and inspect resulting SQL types for JSON-backed columns
- Run relevant integration tests that read/write these fields

### Suggested commit boundary

- `refactor: replace PostgreSQL jsonb mappings with SQL Server-compatible JSON storage`

## Phase 6: Entity configuration and provider-specific cleanup

### Goal

Remove remaining PostgreSQL-specific assumptions from persistence and observability.

### Files to modify

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
- [src/VirtualCompany.Infrastructure/Observability/ServiceCollectionExtensions.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Observability/ServiceCollectionExtensions.cs)
- Any migration or SQL helper code discovered during implementation that still assumes PostgreSQL

### Changes

- Remove any remaining `jsonb` literals, PostgreSQL types, or provider-specific comments.
- Rename health check registration from `"postgresql"` to `"database"` or `"sqlserver"`.
- Review indexes/constraints for SQL Server compatibility:
  - filtered indexes should still work in EF Core, but verify SQL output
- Search the repo for:
  - `jsonb`
  - `::jsonb`
  - `to_jsonb`
  - `jsonb_`
  - `UseNpgsql`
  - `postgres`

### Risks

- Some provider-specific behavior may hide in migrations or docs rather than runtime code.
- Health check naming changes may require test updates.

### Validation

- `rg -n "jsonb|::jsonb|UseNpgsql|postgres"` should only return historical/archive references after cleanup
- `dotnet build VirtualCompany.sln`
- Health endpoint tests updated and passing

### Suggested commit boundary

- `refactor: remove PostgreSQL-specific persistence and observability assumptions`

## Phase 7: Migration reset and SQL Server baseline

### Goal

Replace the active PostgreSQL migration chain with a fresh SQL Server baseline.

### Files to modify

- [src/VirtualCompany.Infrastructure/Persistence/Migrations](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations)
- Potentially [src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs) if tooling inputs need adjustment before generating migration

### Changes

- Decide how to preserve old PostgreSQL migrations:
  - archive outside the active migration path, or
  - remove and rely on Git history
- Generate a new initial SQL Server baseline migration from the updated model.
- Do not attempt to adapt PostgreSQL migration files one by one.
- Inspect the generated baseline carefully for:
  - SQL column types
  - indexes
  - filtered indexes
  - concurrency tokens
  - default values

### Recommendation

- Prefer a fresh baseline migration with a clean SQL Server model snapshot.

### Risks

- If any environments contain real PostgreSQL data that must be preserved, a baseline reset is not enough and a separate data migration plan is required.
- Teams may incorrectly assume historical migrations remain runnable against SQL Server.

### Validation

- `dotnet ef migrations add <BaselineName> --project src/VirtualCompany.Infrastructure --startup-project src/VirtualCompany.Api`
- Create a clean SQL Server database and run API startup migration or `dotnet ef database update`
- Confirm schema creation succeeds from zero

### Suggested commit boundary

- `chore: replace PostgreSQL migrations with SQL Server baseline`

## Phase 8: Test infrastructure evolution

### Goal

Move provider-sensitive integration coverage away from EF InMemory and toward relational behavior that matches SQL Server.

### Files to modify

- [tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs](/abs/path/c:/Users/Johan/source/repos/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs)
- Potentially individual integration test files under [tests/VirtualCompany.Api.Tests](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests)
- CI config files if any are later introduced to spin up SQL Server for tests

### Changes

- Short term option:
  - keep existing `InMemoryDatabase` tests while provider migration lands
- Preferred end state:
  - introduce SQL Server-backed integration test path, ideally using Docker
- Revisit tests that depend on provider-agnostic behavior today:
  - filtered index behavior
  - migrations
  - uniqueness enforcement
  - query translation
  - concurrency behavior

### Risks

- SQL Server-backed tests will be slower and more operationally complex.
- Keeping only `InMemoryDatabase` after provider switch leaves significant parity gaps.

### Validation

- Integration suite passes under current strategy after provider switch
- At least a targeted smoke suite passes against SQL Server

### Suggested commit boundary

- `test: prepare integration tests for SQL Server-backed persistence`

## Phase 9: Program startup migration behavior

### Goal

Decide whether startup migration remains appropriate for local dev and Azure SQL production.

### Files to review or modify

- [src/VirtualCompany.Api/Program.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/Program.cs)

### Current fact

- The API currently auto-runs `Database.MigrateAsync()` at startup for relational providers.

### Recommendation

- Keep startup migration behavior for local development initially because it simplifies onboarding with Docker SQL Server.
- Reassess for production:
  - Preferred Azure SQL posture is often explicit migration execution in deployment rather than app startup.
- If one behavior must serve both environments, gate startup migration by environment or configuration.

### Risks

- Automatic production migration can create startup race conditions or deployment-time schema surprises.
- Turning it off too early can make local setup more fragile.

### Validation

- Local dev: API starts against empty Docker SQL Server and self-migrates successfully
- Production path: documented deployment plan exists even if not implemented yet

### Suggested commit boundary

- `chore: clarify startup migration behavior for local SQL Server and Azure SQL`

## Phase 10: Documentation and developer workflow

### Goal

Make the repo’s documented workflow reflect the new database reality.

### Files to modify

- [README.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/README.md)
- [docs/sqlserver-migration-plan.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docs/sqlserver-migration-plan.md) if implementation notes are later appended

### Changes

- Update local database startup instructions to SQL Server Docker.
- Document expected connection string format.
- Document first-run workflow:
  - start Docker
  - start API
  - let migrations run
- Add troubleshooting for:
  - container startup failures
  - SA password policy
  - certificate/trust settings
- Document Azure SQL production configuration expectations at a high level.

### Risks

- Docs can lag implementation if updated too early or too late.
- If the repo does not become the canonical source of SQL Server Docker setup, docs may stay ambiguous.

### Validation

- A new developer can follow README and boot the API against Docker SQL Server without tribal knowledge

### Suggested commit boundary

- `docs: update local and production database workflow for SQL Server`

## Recommended execution order

1. Phase 1: Provider and package standardization
2. Phase 2: Runtime and design-time provider switch
3. Phase 3: Configuration and environment alignment
4. Phase 4: Docker local development replacement
5. Phase 5: JSON storage conversion strategy
6. Phase 6: Entity configuration and provider-specific cleanup
7. Phase 7: Migration reset and SQL Server baseline
8. Phase 8: Test infrastructure evolution
9. Phase 9: Program startup migration behavior
10. Phase 10: Documentation and developer workflow

## Recommended commit strategy

1. `chore: standardize EF provider packages on SQL Server`
2. `refactor: switch runtime and design-time EF provider to SQL Server`
3. `chore: align API configuration with SQL Server local and Azure SQL production`
4. `chore: replace local docker database with SQL Server`
5. `refactor: replace PostgreSQL jsonb mappings with SQL Server-compatible JSON storage`
6. `refactor: remove PostgreSQL-specific persistence and observability assumptions`
7. `chore: replace PostgreSQL migrations with SQL Server baseline`
8. `test: prepare integration tests for SQL Server-backed persistence`
9. `chore: clarify startup migration behavior for local SQL Server and Azure SQL`
10. `docs: update local and production database workflow for SQL Server`

## Cross-phase validation checklist

- `dotnet restore`
- `dotnet build VirtualCompany.sln`
- `docker compose up -d`
- `dotnet run --project src/VirtualCompany.Api`
- `dotnet ef migrations add <name> --project src/VirtualCompany.Infrastructure --startup-project src/VirtualCompany.Api`
- `dotnet ef database update --project src/VirtualCompany.Infrastructure --startup-project src/VirtualCompany.Api`
- Run API integration tests after the persistence/test strategy is updated
- Manually verify health endpoints and initial application startup against empty SQL Server

## Decisions to confirm before implementation

- Whether old PostgreSQL migrations should be archived in-repo or removed and left in Git history
- Whether startup migrations remain enabled in production or become deployment-managed
- Whether SQL Server-backed integration tests are introduced immediately or in a follow-up PR
- Whether JSON should remain serialized text storage long-term or later be normalized/query-optimized for SQL Server
