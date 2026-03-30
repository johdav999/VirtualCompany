# SQL Server Local Validation Runbook

This checklist verifies the Virtual Company migration to:

- local development on Docker SQL Server
- EF Core provider wiring on SQL Server
- production direction toward Azure SQL

Use this from the repository root:

`c:\Users\Johan\source\repos\Virtual Company`

## 1. Start Docker SQL Server

- Confirm Docker Desktop is running.
- Start the local database:

```powershell
docker compose up -d
```

- Check the container:

```powershell
docker ps --filter "name=virtualcompany-sqlserver"
```

- Expected result:
  - container name: `virtualcompany-sqlserver`
  - image: `mcr.microsoft.com/mssql/server:2022-latest`
  - port mapping includes `0.0.0.0:1433->1433/tcp`

## 2. Confirm expected container state

- Inspect logs:

```powershell
docker logs virtualcompany-sqlserver
```

- Expected signs:
  - SQL Server startup messages
  - no repeated password-policy failures
  - no immediate container exit

- If the container keeps restarting:
  - confirm `MSSQL_SA_PASSWORD` in [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml) still matches the appsettings connection string
  - ensure the password meets SQL Server complexity rules

## 3. Confirm connection string expectations

- The repo currently expects this local development connection string shape:

```text
Server=localhost,1433;Database=virtualcompany;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True
```

- Confirm it matches:
  - [appsettings.Development.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.Development.json)
  - [appsettings.json](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/appsettings.json)
  - [DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs)
  - [VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs)

## 4. Restore and build

- Restore packages:

```powershell
dotnet restore
```

- Build the full solution:

```powershell
dotnet build VirtualCompany.sln
```

- Expected result:
  - restore succeeds
  - build succeeds, or any remaining errors are unrelated to PostgreSQL/Npgsql wiring

## 5. Install EF tooling if needed

- Check whether `dotnet-ef` is available:

```powershell
dotnet ef --version
```

- If missing, install it:

```powershell
dotnet tool install --global dotnet-ef --version 9.0.14
```

## 6. Apply the fresh SQL Server baseline migration

- If the new baseline migration has not been created yet, scaffold it:

```powershell
dotnet ef migrations add InitialSqlServerBaseline --project src\VirtualCompany.Infrastructure\VirtualCompany.Infrastructure.csproj --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj --output-dir Persistence\Migrations
```

- Apply migrations:

```powershell
dotnet ef database update --project src\VirtualCompany.Infrastructure\VirtualCompany.Infrastructure.csproj --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj
```

- Expected result:
  - migration scaffolding emits SQL Server-oriented migration files under [Persistence/Migrations](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/Migrations)
  - generated migration should use SQL Server types and conventions, not `jsonb`, `uuid`, or PostgreSQL SQL functions

## 7. Run the API

- Start the API:

```powershell
dotnet run --project src\VirtualCompany.Api
```

- Current startup behavior:
  - Development environment: relational migrations are allowed on startup
  - Non-development: startup migrations are disabled unless explicitly enabled in config

- Confirm startup behavior in:
  - [Program.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Api/Program.cs)

## 8. Inspect logs and health endpoints

- While the API is running, inspect startup output for:
  - SQL Server connection success
  - migration success
  - no provider mismatch errors

- Check health endpoints:

```powershell
curl http://localhost:5301/health
curl http://localhost:5301/health/live
curl http://localhost:5301/health/ready
```

- Or if using HTTPS:

```powershell
curl https://localhost:7120/health --insecure
curl https://localhost:7120/health/live --insecure
curl https://localhost:7120/health/ready --insecure
```

- Expected readiness checks:
  - `application` on live
  - `database` on readiness
  - optional `redis` and `object-storage` depending on config

## 9. Connect with a DB tool and inspect tables

Use SQL Server Management Studio, Azure Data Studio, or another SQL Server-compatible tool.

- Host: `localhost`
- Port: `1433`
- Authentication: SQL Login
- User: `sa`
- Password: `YourStrong!Passw0rd`
- Database: `virtualcompany`
- Encryption/trust:
  - trust server certificate for local dev if prompted

- Expected tables after migration:
  - `users`
  - `companies`
  - `company_memberships`
  - `company_notes`
  - `company_invitations`
  - `company_outbox_messages`
  - `company_setup_templates`
  - `audit_events`

- Recommended quick inspection query:

```sql
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;
```

## 10. Validate seeded setup templates

- The API seeds templates at startup through [CompanySetupTemplateSeeder.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Companies/CompanySetupTemplateSeeder.cs).
- Verify template data exists:

```sql
SELECT TOP 20 TemplateId, Name, Category, IsActive
FROM company_setup_templates
ORDER BY SortOrder, Name;
```

- Expected result:
  - rows exist
  - onboarding templates are present

## 11. Run tests

- Run the API integration test project:

```powershell
dotnet test tests\VirtualCompany.Api.Tests\VirtualCompany.Api.Tests.csproj
```

- Current test setup note:
  - integration tests no longer use EF InMemory
  - they now use relational SQLite as an intermediate step
  - this is better than InMemory for SQL translation and relational behavior, but it is not yet full SQL Server-backed integration testing

- Relevant test host:
  - [TestWebApplicationFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/tests/VirtualCompany.Api.Tests/TestWebApplicationFactory.cs)

## 12. Common failure modes and fixes

### `docker compose up -d` succeeds but the container exits immediately

- Likely cause:
  - invalid `MSSQL_SA_PASSWORD`
- Fix:
  - update the password in [docker-compose.yml](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/docker-compose.yml)
  - keep appsettings and design-time connection strings in sync

### API fails to connect to SQL Server

- Likely causes:
  - SQL Server container not ready yet
  - wrong password
  - port 1433 already in use
- Fix:
  - inspect `docker logs virtualcompany-sqlserver`
  - wait for startup to finish
  - check `docker ps`
  - confirm port mapping and connection string

### `dotnet ef` is not recognized

- Likely cause:
  - EF CLI tool not installed
- Fix:

```powershell
dotnet tool install --global dotnet-ef --version 9.0.14
```

### Generated migration still contains PostgreSQL types or SQL

- Likely cause:
  - stale provider wiring
  - stale active migration artifacts
  - model still contains provider-specific mapping
- Fix:
  - re-check:
    - [VirtualCompany.Infrastructure.csproj](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj)
    - [DependencyInjection.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs)
    - [VirtualCompanyDbContextFactory.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContextFactory.cs)
    - [EntityConfigurations.cs](/abs/path/c:/Users/Johan/source/repos/Virtual%20Company/src/VirtualCompany.Infrastructure/Persistence/EntityConfigurations.cs)

### API starts but health readiness fails on `database`

- Likely cause:
  - migrations not applied
  - wrong connection string
  - SQL Server unavailable
- Fix:
  - run `dotnet ef database update ...`
  - check appsettings
  - check SQL Server container state

### Tests pass but SQL Server runtime still fails

- Likely cause:
  - tests currently use SQLite as a relational intermediate step, not SQL Server
- Fix:
  - treat test success as partial validation
  - complete SQL Server-backed test infrastructure in a follow-up step

## 13. Final validation checklist

- [ ] Docker SQL Server starts and stays healthy
- [ ] Local connection string matches Docker settings
- [ ] Solution restore succeeds
- [ ] Solution build succeeds
- [ ] `dotnet ef` can scaffold/apply the SQL Server baseline
- [ ] API starts successfully against local SQL Server
- [ ] `/health`, `/health/live`, and `/health/ready` respond successfully
- [ ] Expected tables exist in the database
- [ ] Seeded company setup templates exist
- [ ] API integration tests pass
