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

## Local Database Setup (Docker PostgreSQL + pgvector)

This solution includes a Docker Compose setup for a local PostgreSQL instance for application development. Semantic retrieval stores document chunk embeddings in PostgreSQL `knowledge_chunks` using the `pgvector` extension.

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
Host=localhost;Port=5432;Database=virtualcompany;Username=postgres;Password=postgres;Include Error Detail=true
```

### Start the API against local Docker SQL Server

Once the container is running, start the API from the solution root:

```powershell
dotnet run --project src/VirtualCompany.Api
```

The API enables the `vector` extension on PostgreSQL startup and ensures the current schema exists for local development.
Baseline `agent_templates` catalog records are seeded through EF Core model seeding and migrations, not API startup writes.

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


## Executive Briefings

TASK-ST-505 adds a deterministic v1 briefing pipeline. The `BriefingScheduler` hosted service scans tenant companies and generates one daily briefing and one weekly executive summary per company-period, using company timezone windows and an idempotent `(company_id, briefing_type, period_start_at, period_end_at)` key.

Briefings aggregate pending approvals, task status highlights, open workflow exceptions, and recent agent tool execution activity. Generated output is stored in `company_briefings`, projected to the executive briefing conversation when an active user exists, and fan-out notification rows are written to `company_notifications`.

Users can read and update in-app/mobile delivery preferences at `api/companies/{companyId}/briefings/preferences`. In-app delivery defaults on, mobile defaults off, and daily/weekly cadence preferences default on. Push provider and email dispatch are intentionally out of scope for this slice.
- The API accepts `X-Correlation-ID` on incoming requests.
- When absent, the API generates a correlation ID and returns it in the response header.
- Technical logs and safe error responses include the same correlation ID for support workflows.

## Mobile Companion Scope

The product direction is web-first, mobile-companion. The Blazor web app remains the primary command center for setup, administration, dashboard work, and deep operations.
Responsive web may cover some early mobile access, but it is an interim bridge rather than the final mobile strategy.
`VirtualCompany.Mobile` is the intended long-term .NET MAUI companion experience for sign-in, company selection, alerts, approvals, daily briefing, direct agent chat, and quick company status/task follow-up summaries.
Full setup and administration remain web-first: company onboarding, agent hiring/configuration, workflow definition/administration, deep cockpit analytics, and broad system management are not mobile parity goals.
Web and MAUI clients should reuse the same backend APIs and shared contracts. Mobile-specific business workflows or mobile-only endpoints should only be added when a later story explicitly requires them.
The mobile app centralizes this boundary in `MobileCompanionScope` so navigation, route guards, and product copy stay aligned with the supported companion surface.


## Workflow v1

Workflow v1 intentionally supports only predefined, versioned workflow templates from the curated catalog. Admins can review the catalog and start manual workflows, while schedule and internal event starts use the same predefined definitions. Arbitrary workflow graph authoring, builder UX, and custom node/edge editing are intentionally deferred.
Configuration:

- `Observability:Redis:ConnectionString` enables the Redis readiness check.
- `Observability:ObjectStorage:*` enables an object-storage probe without coupling the API to a specific provider yet.
- `Observability:RateLimiting:*` configures the early named policies for future chat/task style endpoints.
- `RedisExecutionCoordination:*` configures Redis key prefix and default TTLs for distributed locks and ephemeral tenant execution state used by schedulers, workflow progression, retry workers, and long-running task heartbeats.

Logging boundary:
- Technical operational logs stay in the application logging pipeline for diagnostics, retries, health checks, dependency failures, and exception handling.
- Business audit events are persisted separately in the `audit_events` store through the dedicated `IAuditEventWriter` application abstraction.
- Do not treat `ILogger` output as business history or compliance evidence.
- Reliability-sensitive side effects are persisted to `company_outbox_messages` in the same EF Core transaction as business state changes.
- `CompanyOutboxDispatcherBackgroundService` dispatches pending outbox work outside the request path with retry metadata, idempotency keys, and correlation-aware logging.
- Dispatcher behavior is configured through `CompanyOutboxDispatcher:*` in API configuration.
- Outbox delivery is at-least-once; handlers must be idempotent and terminal failures remain in the outbox for future escalation/ops workflows.
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

## Company Document Ingestion

Company knowledge documents are uploaded through `POST /api/companies/{companyId}/documents` using `multipart/form-data`.

Form fields:

- `title`
- `document_type`
- `access_scope` as a JSON object string with required `visibility`; the server persists the resolved tenant `company_id` and rejects cross-tenant scope references
- `metadata` as an optional JSON object string
- `file`

Supported upload formats for the initial rollout are `.txt`, `.md`, `.pdf`, `.doc`, and `.docx`.
Local document storage defaults to `App_Data/object-storage` under the API content root. Configure `CompanyDocuments:MaxUploadBytes`, `CompanyDocuments:Storage:RootPath`, and optionally `CompanyDocuments:Storage:BaseUri` in API settings as needed.
Successful uploads persist the blob to object storage, save tenant-scoped metadata in PostgreSQL, and pass through an explicit virus-scan gate before downstream processing. The current infrastructure registers a placeholder scanner implementation so new documents move through `uploaded`, `pending_scan`, and usually `scan_clean` without requiring a real antivirus product yet. Future processing workers must only continue from `scan_clean`. The API exposes `ingestion_status`, `failure_code`, `failure_message`, `failure_action`, `can_retry`, and `failed_utc` for tenant-scoped troubleshooting, and scan metadata is stored in the document metadata payload to keep the pipeline extension point explicit.

## Knowledge Chunking And Semantic Retrieval

When a knowledge document reaches `scan_clean` or `processed`, the background indexing worker can:

- load extracted text from persisted document metadata or the original plain-text object
- split the document into deterministic overlapping chunks
- generate embeddings for each chunk
- persist active chunk rows into `knowledge_chunks` scoped by `company_id`
- deactivate the prior chunk set on re-index so retries and re-ingestion do not duplicate active chunks

Configuration lives in API settings:

- `KnowledgeChunking:*` controls chunk size, overlap, max chunk count, and strategy version metadata
- `KnowledgeEmbeddings:*` controls the embedding provider, base URL, API key, model, optional model version, and vector dimensions
- `KnowledgeIndexing:*` controls the background indexing worker

Semantic search is available at `GET /api/companies/{companyId}/documents/semantic-search?q=...&top=5` and returns chunk content, similarity score, chunk index, source reference, and the source document title/id for the resolved tenant only.

## Authorization Behavior

- Active membership is required for company-scoped access.
- Role-gated endpoints use ASP.NET Core policy-based authorization against persisted membership roles.
- `403 Forbidden` is returned when the company context is known but the caller lacks an active membership or required role.
- `404 Not Found` is returned for company-owned resource fetches when the caller cannot access the company or the resource does not exist inside the resolved company context. This hides cross-tenant resource existence.

## Notes

- The local database container uses PostgreSQL and the API expects the `pgvector` extension to be available.
- Data is persisted in the named Docker volume configured in `docker-compose.yml`.
- PostgreSQL startup can take a short time on first boot; if API startup fails immediately after `docker compose up -d`, check the container logs and retry once the server is ready.
- The API applies EF Core migrations at startup when using a relational provider.
- Baseline hiring templates in `agent_templates` are versioned in source and delivered through EF Core migrations.
- The web offline hiring/catalog fallback reads bundled template JSON from `src/VirtualCompany.Web/wwwroot/offline/agent-templates.json` instead of hardcoded role defaults in code.
