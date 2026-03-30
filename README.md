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

## Local Database Setup (Docker PostgreSQL)

This solution includes a Docker Compose setup for a local PostgreSQL instance that matches the production JSONB persistence model used by onboarding.

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
docker logs virtualcompany-postgres
```

### Connect to the database

- Host: `localhost`
- Port: `5432`
- Database: `virtualcompany`
- User: `postgres`
- Password: `postgres`

### Sample connection string

```text
Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres
```

## Local Development Authentication

The API uses a development-only header authentication scheme that preserves an auth-provider abstraction for future SSO/OIDC integration.

Send these headers with authenticated API requests:

- `X-Dev-Auth-Subject` = stable external identity subject
- `X-Dev-Auth-Email` = user email
- `X-Dev-Auth-DisplayName` = display name
- `X-Dev-Auth-Provider` = optional provider name, defaults to `dev-header`

On first authenticated request, the API provisions or updates the internal `User` row using the `(authProvider, authSubject)` identity pair. Tenant membership and authorization always resolve from that internal user record, and users are not rebound across providers by email alone.

## Company Context Resolution

Tenant-owned API requests resolve company context from:

1. Route value `companyId`
2. Header `X-Company-Id` when a route value is not present

If both are supplied and do not match, the API returns `400 Bad Request`.

## Observability

Health endpoints:

- `GET /health`
- `GET /health/live`
- `GET /health/ready`

Request correlation:

- The API accepts `X-Correlation-ID` on incoming requests.
- When absent, the API generates a correlation ID and returns it in the response header.
- Technical logs and safe error responses include the same correlation ID for support workflows.

Configuration:

- `Observability:Redis:ConnectionString` enables the Redis readiness check.
- `Observability:ObjectStorage:*` enables an object-storage probe without coupling the API to a specific provider yet.
- `Observability:RateLimiting:*` configures the early named policies for future chat/task style endpoints.

Logging boundary:
- Technical operational logs stay in the application logging pipeline for diagnostics, retries, health checks, dependency failures, and exception handling.
- Business audit events are persisted separately in the `audit_events` store through the dedicated `IAuditEventWriter` application abstraction.
- Do not treat `ILogger` output as business history or compliance evidence.
- When an operation needs both, write a technical log for operators and a business audit event for actor/action/target/outcome history.
- The current baseline records tenant-scoped membership administration audit events and leaves future audit query/UI work to later backlog items.

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
- `404 Not Found` is returned for company-owned resource fetches when the caller cannot access the company or the resource does not exist inside the resolved company context. This hides cross-tenant resource existence.

## Notes

- The PostgreSQL container uses the `postgres:17` image.
- Data is persisted in the named Docker volume `postgres-data`.
- The API applies EF Core migrations at startup when using a relational provider.
