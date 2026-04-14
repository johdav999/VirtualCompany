# Goal
Implement backlog task **TASK-1.2.1 — Create alert domain model, persistence schema, and CRUD APIs** for **US-1.2 ST-A202 — Alert generation framework** in the existing .NET modular monolith.

Deliver a production-ready first slice of the alerting foundation that:
- introduces an **Alert** domain model and supporting enums/value objects
- persists alerts in **PostgreSQL** with tenant-aware schema and indexes
- exposes **CRUD + query APIs** in ASP.NET Core
- supports **deduplication by fingerprint** so repeated detections do not create multiple open alerts
- supports **paginated filtering** by tenant, type, severity, status, and createdAt
- aligns with the architecture’s **shared-schema multi-tenancy**, **CQRS-lite**, and **clean module boundaries**

Acceptance criteria to satisfy:
- Alerts generated from agent detections include required fields: `type`, `severity`, `title`, `summary`, `evidence`, `status`, `tenantId`, `correlationId`
- Types include at least: `risk`, `anomaly`, `opportunity`
- Severities include at least: `low`, `medium`, `high`, `critical`
- Deduplication prevents multiple **open** alerts for the same fingerprint
- Query API supports filtering by tenant, type, severity, status, and createdAt with pagination

# Scope
Implement only what is needed for this task’s backend slice. Favor the project’s existing patterns if already present.

In scope:
- Domain entity/model for alerts
- Alert enums/constants for type and severity, plus status
- Persistence schema and migration
- EF Core configuration/repository or equivalent infrastructure persistence mapping
- Application commands/queries for:
  - create alert
  - get alert by id
  - update alert
  - delete alert
  - list/query alerts with pagination and filters
  - create-or-deduplicate alert from detection input
- API endpoints/controllers/minimal APIs for the above
- Request/response DTOs
- Validation
- Tenant scoping enforcement
- Tests for domain/application/API behavior, especially deduplication and filtering

Out of scope unless already trivial and idiomatic in this codebase:
- UI work in Blazor or MAUI
- notification fan-out
- inbox UX
- background workers
- full audit/eventing pipeline
- advanced RLS/database policies
- broker/outbox integration beyond leaving extension points
- detection engine implementation beyond accepting detection-derived input

If the codebase already has conventions for modules, result wrappers, pagination contracts, endpoint registration, or tenant resolution, reuse them instead of inventing new patterns.

# Files to touch
Touch only the files needed, but expect changes across these areas:

- `src/VirtualCompany.Domain/**`
  - add alert entity/aggregate
  - add enums/value objects for alert type, severity, status
- `src/VirtualCompany.Application/**`
  - commands, queries, handlers/services, DTOs, validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - repository implementation
  - migration(s)
  - DbContext updates
- `src/VirtualCompany.Api/**`
  - endpoints/controllers
  - request/response contracts if API layer owns them
  - DI registration
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- possibly shared/common files if the solution has existing abstractions for:
  - pagination
  - tenant context
  - result/error handling
  - correlation IDs

Also inspect before coding:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing `DbContext`, migrations approach, and API style in:
  - `src/VirtualCompany.Api`
  - `src/VirtualCompany.Application`
  - `src/VirtualCompany.Domain`
  - `src/VirtualCompany.Infrastructure`

# Implementation plan
1. **Inspect and align with existing architecture**
   - Determine whether the solution uses:
     - controllers or minimal APIs
     - MediatR/CQRS handlers or direct services
     - FluentValidation or data annotations
     - EF Core migrations and naming conventions
     - a tenant provider/current company context abstraction
   - Follow existing patterns exactly.

2. **Design the alert domain model**
   - Create an `Alert` entity in the domain layer with at minimum:
     - `Id`
     - `TenantId` or `CompanyId` depending on established naming in codebase  
       - Prefer existing tenant-owned naming conventions; if the codebase uses `company_id`, map domain/API naming carefully
     - `Type`
     - `Severity`
     - `Title`
     - `Summary`
     - `Evidence`
     - `Status`
     - `CorrelationId`
     - `Fingerprint`
     - `CreatedAt`
     - `UpdatedAt`
     - optional but useful: `SourceAgentId`, `ResolvedAt`, `ClosedAt`, `LastDetectedAt`, `OccurrenceCount`, `MetadataJson`
   - Add enums/constants:
     - `AlertType`: `Risk`, `Anomaly`, `Opportunity`
     - `AlertSeverity`: `Low`, `Medium`, `High`, `Critical`
     - `AlertStatus`: at least one “open” state plus closed/resolved states  
       - Example: `Open`, `Acknowledged`, `Resolved`, `Closed`
   - Add domain guards:
     - required fields cannot be empty
     - title/summary length sanity checks if project has conventions
     - dedup-related methods such as `RefreshFromDuplicateDetection(...)`
   - Keep the model simple and persistence-friendly.

3. **Define deduplication behavior**
   - Implement the rule: repeated detections with the same fingerprint must not create multiple **open** alerts.
   - Expected behavior for create-from-detection flow:
     - if an open alert exists for `(tenant, fingerprint)`:
       - do **not** create a new alert
       - update the existing alert’s `UpdatedAt` and optionally `LastDetectedAt`
       - optionally increment `OccurrenceCount`
       - optionally refresh evidence/summary if appropriate, but do not lose useful prior evidence unless intentionally merged
     - if no open alert exists:
       - create a new alert
   - Open statuses should be clearly defined in code.
   - Enforce dedup both:
     - in application logic
     - and, if feasible with PostgreSQL, with a partial unique index for open alerts by tenant + fingerprint

4. **Add persistence schema**
   - Create an `alerts` table in PostgreSQL.
   - Suggested columns:
     - `id uuid pk`
     - `company_id uuid` or `tenant_id uuid` matching existing conventions
     - `type text`
     - `severity text`
     - `title text`
     - `summary text`
     - `evidence jsonb` or `text`
     - `status text`
     - `correlation_id text`
     - `fingerprint text`
     - `source_agent_id uuid null`
     - `occurrence_count int`
     - `created_at timestamptz`
     - `updated_at timestamptz`
     - `last_detected_at timestamptz null`
     - `resolved_at timestamptz null`
     - `closed_at timestamptz null`
     - `metadata_json jsonb null`
   - Prefer `jsonb` for `evidence` if evidence is structured; otherwise use text plus a future migration path.
   - Add indexes for query acceptance criteria:
     - tenant/company + created_at desc
     - tenant/company + type
     - tenant/company + severity
     - tenant/company + status
     - tenant/company + fingerprint
     - composite index for common filtered pagination if useful
   - Add a partial unique index for deduplication of open alerts if supported by the project’s migration style, e.g. unique on `(company_id, fingerprint)` where status is in open statuses.
   - Update DbContext and entity configuration.

5. **Implement infrastructure mapping**
   - Add EF Core configuration for the alert entity:
     - table name
     - key
     - enum/string conversions
     - required fields
     - max lengths
     - JSONB mapping if used
     - indexes
   - Add repository/query methods or use DbContext directly per project conventions:
     - get by id scoped to tenant
     - list paginated with filters
     - find open by fingerprint scoped to tenant
     - add/update/delete

6. **Implement application layer**
   - Add commands/queries and handlers/services for:
     - `CreateAlert`
     - `CreateOrDeduplicateAlertFromDetection`
     - `GetAlertById`
     - `UpdateAlert`
     - `DeleteAlert`
     - `ListAlerts`
   - Add request DTOs with validation:
     - required fields
     - enum validation
     - pagination bounds
     - createdAt filter shape
   - For list/query, support:
     - tenant scoping from current context, not arbitrary cross-tenant access
     - optional filters: type, severity, status, createdAt from/to
     - pagination: page/pageSize or cursor, whichever the codebase already uses
     - deterministic ordering, default newest first
   - Return paginated results with total count if that is the project standard.

7. **Implement API endpoints**
   - Add endpoints under a consistent route, e.g. `/api/alerts`
   - Suggested endpoints:
     - `POST /api/alerts`
     - `POST /api/alerts/detections` or `/api/alerts/generate`
     - `GET /api/alerts/{id}`
     - `GET /api/alerts`
     - `PUT /api/alerts/{id}` or `PATCH /api/alerts/{id}`
     - `DELETE /api/alerts/{id}`
   - Ensure tenant/company context is resolved from the authenticated/request context, not trusted from raw client input alone.
   - If API contracts require `tenantId` in payload for acceptance criteria, validate it matches resolved tenant context or ignore client-supplied value in favor of server context.
   - Include `correlationId` handling:
     - accept from request if appropriate
     - otherwise populate from request correlation context if available
   - Use safe status codes:
     - `201` create
     - `200` get/update/list
     - `204` delete
     - `404` for not found in tenant scope
     - `400` validation errors
     - `409` only if needed for race conditions not absorbed by dedup logic

8. **Model evidence carefully**
   - Acceptance criteria require `evidence`.
   - Prefer a structured shape if no existing standard exists, e.g. array/object with source facts:
     - detection source
     - observed values
     - timestamps
     - references
   - If time is limited, store as JSONB string/object but keep API contract explicit and documented in code.
   - Do not over-engineer; just ensure evidence is required and persisted.

9. **Add tests**
   - Add/extend tests to cover:
     - create alert with required fields succeeds
     - invalid type/severity/status rejected
     - list alerts filtered by type
     - list alerts filtered by severity
     - list alerts filtered by status
     - list alerts filtered by createdAt range
     - pagination works and ordering is deterministic
     - tenant scoping prevents cross-tenant reads
     - duplicate detection with same fingerprint returns/updates existing open alert instead of creating another
     - same fingerprint can create a new alert after prior one is resolved/closed
   - Prefer integration tests hitting API + persistence if the test project supports it.

10. **Keep implementation extensible**
   - Leave clear extension points for future:
     - notification dispatch
     - audit events
     - workflow/escalation linkage
     - source agent linkage
   - But do not implement those unless already trivial.

11. **Document assumptions in code comments or PR-style notes**
   - Especially:
     - whether tenant is represented as `company_id`
     - what statuses count as “open”
     - evidence storage format
     - dedup update behavior on repeated detections

# Validation steps
Run the relevant local validation and ensure all pass.

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow:
   - generate/apply the migration using the repository’s established process
   - verify the `alerts` table, indexes, and any partial unique index are created correctly

4. Manually verify API behavior with tests or HTTP client:
   - create an alert
   - fetch by id
   - update status/details
   - list with pagination
   - list filtered by:
     - type
     - severity
     - status
     - createdAt range
   - delete alert
   - submit duplicate detection twice with same fingerprint and confirm only one open alert exists

5. Verify tenant isolation:
   - create alerts for tenant A and tenant B
   - ensure tenant A cannot read/query tenant B alerts

6. Verify dedup edge case:
   - create/open alert from fingerprint X
   - resolve/close it
   - submit fingerprint X again
   - confirm a new alert can be created

7. Confirm acceptance criteria mapping in final output/notes:
   - required fields present
   - categories/severities supported
   - dedup works
   - paginated filtered query works

# Risks and follow-ups
- **Tenant naming mismatch**: architecture text uses both tenant and company terminology. Reuse the codebase’s established convention and map API/domain names carefully.
- **Dedup race conditions**: application-level dedup alone may fail under concurrency. Prefer a PostgreSQL partial unique index for open alerts plus graceful retry/read-after-conflict handling.
- **Evidence shape ambiguity**: acceptance criteria require evidence but do not define schema. Choose a minimal structured JSON contract and keep it stable.
- **Status semantics**: define clearly which statuses are considered “open” for dedup. Document this in code and tests.
- **CRUD vs generation flow**: acceptance criteria emphasize generation from detections, not just generic CRUD. Ensure the detection-oriented create/dedup path is implemented, not only plain create.
- **Pagination contract consistency**: use existing shared pagination models if