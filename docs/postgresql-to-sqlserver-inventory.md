# PostgreSQL-Specific Inventory

This inventory captures PostgreSQL-specific implementation details in the repository that must be removed, replaced, or reworked as part of the SQL Server + Azure SQL migration.

It excludes the planning document in `docs/sqlserver-migration-plan.md`, since that file is intentionally historical/planning content rather than active implementation.

## 1. Packages

- [src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj)
  - Current state: references `Npgsql.EntityFrameworkCore.PostgreSQL`.
  - What must change: remove PostgreSQL provider package from Infrastructure.
  - Suggested replacement direction: add or standardize on `Microsoft.EntityFrameworkCore.SqlServer` in Infrastructure as the active EF provider package.

## 2. Runtime registration

- [src/VirtualCompany.Infrastructure/DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs)
  - Current state: registers `VirtualCompanyDbContext` with `options.UseNpgsql(connectionString)`.
  - What must change: replace runtime provider registration.
  - Suggested replacement direction: switch to `UseSqlServer(connectionString, ...)`, optionally with SQL Server retry configuration suitable for Azure SQL.

- [src/VirtualCompany.Infrastructure/DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs)
  - Current state: includes PostgreSQL fallback connection string `Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres`.
  - What must change: remove PostgreSQL default/fallback connection string.
  - Suggested replacement direction: use SQL Server connection string format for local Docker and require environment-specific configuration for production.

## 3. Design-time tooling

- [src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs)
  - Current state: EF design-time factory uses `UseNpgsql(...)`.
  - What must change: design-time provider must match runtime provider.
  - Suggested replacement direction: switch to `UseSqlServer(...)` using the local Docker SQL Server connection string or configuration-driven resolution.

- [src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs)
  - Current state: hardcoded PostgreSQL connection string.
  - What must change: remove PostgreSQL-specific design-time connection string.
  - Suggested replacement direction: use SQL Server local Docker connection string and prefer configuration/environment inputs where practical.

## 4. Configuration

- [src/VirtualCompany.Api/appsettings.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.json)
  - Current state: `ConnectionStrings:VirtualCompanyDb` is PostgreSQL.
  - What must change: replace PostgreSQL connection string.
  - Suggested replacement direction: SQL Server local/dev placeholder or non-secret default; production value supplied externally for Azure SQL.

- [src/VirtualCompany.Api/appsettings.Development.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.Development.json)
  - Current state: `ConnectionStrings:VirtualCompanyDb` is PostgreSQL.
  - What must change: replace PostgreSQL local development connection string.
  - Suggested replacement direction: point to Docker SQL Server on `localhost,1433`, likely with `TrustServerCertificate=True` for local dev.

## 5. Entity mappings

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: custom helper `HasJsonbConversion<T>()`.
  - What must change: helper name and behavior are PostgreSQL-specific.
  - Suggested replacement direction: replace with provider-neutral or SQL Server-oriented JSON string conversion helper, likely storing JSON as `nvarchar(max)`.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: helper sets `HasColumnType("jsonb")`.
  - What must change: remove PostgreSQL column type usage.
  - Suggested replacement direction: use SQL Server-compatible string storage type or let EF infer SQL Server text storage.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `Company.Branding` uses `.HasDefaultValueSql("'{}'::jsonb")`.
  - What must change: PostgreSQL default SQL.
  - Suggested replacement direction: use SQL Server-compatible string default or rely on CLR initialization.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `Company.Settings` uses `.HasDefaultValueSql("'{}'::jsonb")`.
  - What must change: PostgreSQL default SQL.
  - Suggested replacement direction: use SQL Server-compatible string default or rely on CLR initialization.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `CompanySetupTemplate.Defaults` uses `.HasDefaultValueSql("'{}'::jsonb")`.
  - What must change: PostgreSQL default SQL.
  - Suggested replacement direction: SQL Server-compatible JSON string default or CLR default.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `CompanySetupTemplate.Metadata` uses `.HasDefaultValueSql("'{}'::jsonb")`.
  - What must change: PostgreSQL default SQL.
  - Suggested replacement direction: SQL Server-compatible JSON string default or CLR default.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `AuditEvent.DataSources` uses `.HasDefaultValueSql("'[]'::jsonb")`.
  - What must change: PostgreSQL array-like JSON default SQL.
  - Suggested replacement direction: SQL Server-compatible string default or CLR default.

- [src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)
  - Current state: `AuditEvent.Metadata` uses `.HasDefaultValueSql("'{}'::jsonb")`.
  - What must change: PostgreSQL default SQL.
  - Suggested replacement direction: SQL Server-compatible string default or CLR default.

## 6. Migrations

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs)
  - Current state: migration name and implementation are explicitly PostgreSQL JSONB-specific.
  - What must change: cannot remain as active migration chain for SQL Server.
  - Suggested replacement direction: replace the active migration history with a fresh SQL Server baseline rather than adapting this file.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs)
  - Current state: uses `type: "jsonb"`.
  - What must change: PostgreSQL-specific storage type.
  - Suggested replacement direction: baseline migration should emit SQL Server-compatible text columns.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs)
  - Current state: uses `defaultValueSql: "'{}'::jsonb"`.
  - What must change: PostgreSQL-specific default SQL.
  - Suggested replacement direction: SQL Server default or application-side initialization.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300004_AddCompanyBrandingAndSettingsJsonb.cs)
  - Current state: raw SQL uses PostgreSQL-specific functions and casts:
    - `jsonb_strip_nulls`
    - `jsonb_build_object`
    - `("OnboardingStateJson")::jsonb`
    - `jsonb_set`
    - `to_jsonb`
  - What must change: provider-specific SQL cannot run on SQL Server.
  - Suggested replacement direction: drop this migration from the active chain and generate a new SQL Server baseline; if data transformation is still needed, rewrite for SQL Server or perform transformation outside historical migrations.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300005_AddCompanySetupTemplates.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300005_AddCompanySetupTemplates.cs)
  - Current state: defines `defaults_json` and `metadata_json` as `jsonb` with PostgreSQL default SQL.
  - What must change: PostgreSQL JSONB columns.
  - Suggested replacement direction: replace via fresh SQL Server baseline migration using SQL Server-compatible text columns.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300009_AddBusinessAuditEvents.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300009_AddBusinessAuditEvents.cs)
  - Current state: defines `DataSources` and `Metadata` as `jsonb` with PostgreSQL default SQL.
  - What must change: PostgreSQL JSONB columns.
  - Suggested replacement direction: replace via fresh SQL Server baseline migration.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300008_AddReliableInvitationDeliveryOutbox.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations/202603300008_AddReliableInvitationDeliveryOutbox.cs)
  - Current state: uses `defaultValueSql: "NOW()"`.
  - What must change: `NOW()` is PostgreSQL-specific SQL.
  - Suggested replacement direction: use SQL Server-compatible default or handle timestamps in application code/baseline migration.

- [src/VirtualCompany.Infrastructure/Persistence/Migrations](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations)
  - Current state: the active migration set was generated under PostgreSQL/Npgsql.
  - What must change: active migration lineage is provider-specific and should not remain primary for SQL Server.
  - Suggested replacement direction: archive/reference old migrations and create a fresh SQL Server baseline migration.

## 7. Docker/dev environment

- [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml)
  - Current state: defines a `postgres` service using `postgres:17`.
  - What must change: local dev database container does not match SQL Server target.
  - Suggested replacement direction: replace or rework compose file to run SQL Server 2022 on port `1433`.

- [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml)
  - Current state: service/container naming is PostgreSQL-specific:
    - `postgres`
    - `virtualcompany-postgres`
    - `postgres-data`
  - What must change: PostgreSQL-specific service, container, and volume names.
  - Suggested replacement direction: rename to SQL Server-oriented naming and storage.

- [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml)
  - Current state: PostgreSQL environment variables:
    - `POSTGRES_DB`
    - `POSTGRES_USER`
    - `POSTGRES_PASSWORD`
  - What must change: incompatible with SQL Server image/runtime.
  - Suggested replacement direction: use SQL Server container environment variables such as `ACCEPT_EULA` and `MSSQL_SA_PASSWORD`.

## 8. Health checks / observability

- [src/VirtualCompany.Infrastructure/Observability/ServiceCollectionExtensions.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Observability/ServiceCollectionExtensions.cs)
  - Current state: registers database readiness check under the name `"postgresql"`.
  - What must change: provider-specific readiness check naming.
  - Suggested replacement direction: rename to `"database"` or `"sqlserver"` to match the new provider and avoid stale operational naming.

## 9. Tests

- [tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs)
  - Current state: integration tests replace the real provider with `UseInMemoryDatabase(...)`.
  - What must change: current test infrastructure does not validate SQL Server relational behavior.
  - Suggested replacement direction: evolve to SQL Server-backed integration tests, ideally against Docker SQL Server, or keep InMemory only as a temporary intermediate step.

- [tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs)
  - Current state: calls `EnsureCreatedAsync()` on an in-memory provider.
  - What must change: schema creation path does not exercise relational migrations.
  - Suggested replacement direction: run tests against a relational provider and validate migrations/database update behavior.

- [tests/VirtualCompany.Api.Tests/HealthEndpointsIntegrationTests.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests/HealthEndpointsIntegrationTests.cs)
  - Current state: asserts on `"postgresql"` health check result naming.
  - What must change: tests encode PostgreSQL-specific observability naming.
  - Suggested replacement direction: update expectations to provider-neutral or SQL Server naming after health check change.

## 10. Docs

- [README.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/README.md)
  - Current state: local Docker instructions are PostgreSQL-specific:
    - `docker logs virtualcompany-postgres`
    - connection info for port `5432`
    - user `postgres`
    - password `postgres`
    - PostgreSQL connection string example
  - What must change: developer onboarding docs still describe PostgreSQL local setup.
  - Suggested replacement direction: rewrite for SQL Server Docker local workflow and SQL Server connection strings.

- [README.md](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/README.md)
  - Current state: Notes section explicitly says:
    - PostgreSQL container uses `postgres:17`
    - data persisted in `postgres-data`
  - What must change: stale provider-specific documentation.
  - Suggested replacement direction: update notes to SQL Server image, container expectations, and local persistence model.

## Additional notes

- [src/VirtualCompany.Api/Program.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/Program.cs)
  - Current state: includes `EnsureCreatedAsync()` fallback for non-relational providers.
  - Why it matters: not PostgreSQL-specific itself, but currently interacts with the InMemory test setup rather than validating real relational migrations.
  - Suggested replacement direction: keep under review during migration; local SQL Server can continue using `MigrateAsync()`, while production behavior should be explicitly decided for Azure SQL.
