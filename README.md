# VirtualCompany

Initial .NET solution setup for the `VirtualCompany` application.

## Projects

- `VirtualCompany.Api` = ASP.NET Core backend
- `VirtualCompany.Web` = Blazor WebAssembly frontend
- `VirtualCompany.Mobile` = .NET MAUI mobile app
- `VirtualCompany.Shared` = shared DTOs, contracts, and enums
- `VirtualCompany.Domain` = core business and domain logic
- `VirtualCompany.Application` = application use cases and services
- `VirtualCompany.Infrastructure` = persistence and external integrations

## Structure

```text
VirtualCompany/
  VirtualCompany.sln
  src/
    VirtualCompany.Api/
    VirtualCompany.Web/
    VirtualCompany.Mobile/
    VirtualCompany.Shared/
    VirtualCompany.Domain/
    VirtualCompany.Application/
    VirtualCompany.Infrastructure/
  tests/
    VirtualCompany.Api.Tests/
```

## Local Database Setup (Docker SQL Server)

This solution includes a Docker Compose setup for a local SQL Server instance that stays compatible with Azure SQL development.

### Prerequisite: Docker Desktop on Windows

If Docker is not installed:

1. Install Docker Desktop for Windows from the official Docker installer.
2. Enable WSL 2 integration during setup if prompted.
3. Reboot Windows if the installer requests it.
4. Start Docker Desktop and wait until it shows that Docker is running.
5. Verify installation from a terminal:

```powershell
docker --version
docker compose version
docker info
```

### Start the database

From the solution root:

```powershell
docker compose up -d
```

### Stop the database

```powershell
docker compose down
```

### Check container logs

If startup fails:

```powershell
docker logs virtualcompany-sql
```

### Connect to the database

- Server: `localhost,1433`
- Database: `VirtualCompanyDb`
- User: `sa`
- Password: `YourStrongPassword123!`

### Sample connection string

```text
Server=localhost,1433;Database=VirtualCompanyDb;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True
```

## Local Development Authentication

The API uses a development-only header authentication scheme that preserves an auth-provider abstraction for future SSO/OIDC integration.

Send these headers with authenticated API requests:

- `X-Dev-Auth-Subject` = stable external identity subject
- `X-Dev-Auth-Email` = user email
- `X-Dev-Auth-DisplayName` = display name
- `X-Dev-Auth-Provider` = optional provider name, defaults to `dev-header`

On first authenticated request, the API provisions or updates the internal `User` row using the `(authProvider, authSubject)` identity pair.

## Company Context Resolution

Tenant-owned API requests resolve company context from:

1. Route value `companyId`
2. Header `X-Company-Id` when a route value is not present

If both are supplied and do not match, the API returns `400 Bad Request`.

## Tenant-Aware Endpoints

Authenticated endpoints:

- `GET /api/auth/me`
- `GET /api/auth/memberships`
- `POST /api/auth/select-company`

Company-scoped endpoints:

- `GET /api/companies/{companyId}/access`
- `GET /api/companies/{companyId}/access/admin`
- `GET /api/companies/{companyId}/notes/{noteId}`

## Authorization Behavior

- Active membership is required for company-scoped access.
- Role-gated endpoints use ASP.NET Core policy-based authorization against persisted membership roles.
- `403 Forbidden` is returned when the company context is known but the caller lacks an active membership or required role.
- `404 Not Found` is returned for company-owned resource fetches when the resource does not exist inside the resolved company context. This hides cross-tenant resource existence.

## Notes

- The SQL Server container uses the `mcr.microsoft.com/mssql/server:2022-latest` image.
- Data is persisted in the named Docker volume `sqlserver-data`.
- The API applies EF Core migrations at startup when using a relational provider.
