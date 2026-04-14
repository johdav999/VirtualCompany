# Goal
Implement backlog task **TASK-1.2.2 — Implement alert classification and fingerprint-based deduplication pipeline** for **US-1.2 ST-A202 — Alert generation framework** in the existing .NET solution.

Deliver a production-ready vertical slice that:
- generates alerts from agent detections,
- classifies alerts by required **type** and **severity**,
- deduplicates repeated detections using a stable **fingerprint** so only one open alert exists per fingerprint per tenant,
- exposes a tenant-scoped, paginated alerts query API with filtering by:
  - tenant,
  - type,
  - severity,
  - status,
  - createdAt.

The implementation must align with the modular monolith / clean architecture approach already described for the platform:
- Domain entities and rules in `VirtualCompany.Domain`
- Commands/queries/handlers in `VirtualCompany.Application`
- Persistence and API wiring in `VirtualCompany.Infrastructure` / `VirtualCompany.Api`
- Tests in `tests/VirtualCompany.Api.Tests`

# Scope
In scope:
- Add an **Alert** domain model with required fields:
  - `type`
  - `severity`
  - `title`
  - `summary`
  - `evidence`
  - `status`
  - `tenantId` or `companyId` consistent with existing naming conventions
  - `correlationId`
- Add classification enums/value objects for at least:
  - types: `risk`, `anomaly`, `opportunity`
  - severities: `low`, `medium`, `high`, `critical`
- Add a **fingerprint** field and deduplication rule:
  - repeated detections with the same fingerprint must not create multiple **open** alerts
- Support alert creation from agent detections through an application service/command
- Add persistence schema + EF Core mapping + migration
- Add paginated query API for alerts with filters:
  - tenant/company
  - type
  - severity
  - status
  - createdAt range or sort/filter semantics as supported by existing patterns
- Return API DTOs with stable contract and pagination metadata
- Add tests covering classification, deduplication, tenant scoping, and filtering

Out of scope unless required by existing code patterns:
- UI work in Blazor or MAUI
- notification fan-out
- inbox UX
- background dispatcher integration beyond what is necessary to persist/query alerts
- advanced correlation/grouping beyond fingerprint deduplication
- full audit/explainability screens

If the codebase already has adjacent concepts like detections, notifications, or audit events, integrate cleanly rather than duplicating patterns.

# Files to touch
Touch only the files needed for this task. Expect to add/update files in these areas:

- `src/VirtualCompany.Domain/`
  - alert entity/aggregate
  - enums/value objects for alert type, severity, status
  - domain rules for deduplication invariants where appropriate

- `src/VirtualCompany.Application/`
  - commands for creating/generating alerts from detections
  - query models and handlers for paginated alert retrieval
  - DTOs/contracts for alert responses
  - validation logic

- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration
  - repository/query implementation
  - migration(s)
  - indexes/constraints for deduplication and filtering

- `src/VirtualCompany.Api/`
  - alerts controller or minimal API endpoints
  - request/response contracts if API layer owns them
  - DI registration if needed

- `tests/VirtualCompany.Api.Tests/`
  - integration tests for create/generate + query API
  - deduplication behavior tests
  - tenant isolation/filtering tests

Also inspect before coding:
- existing entity naming conventions: `company_id` vs `tenantId`
- existing pagination abstractions
- existing CQRS/MediatR patterns
- existing API route conventions
- existing migration workflow and DbContext organization

# Implementation plan
1. **Inspect current architecture and conventions**
   - Review solution structure and existing patterns in:
     - `VirtualCompany.Domain`
     - `VirtualCompany.Application`
     - `VirtualCompany.Infrastructure`
     - `VirtualCompany.Api`
   - Determine:
     - whether tenant is modeled as `CompanyId` or `TenantId`
     - whether MediatR/FluentValidation/result wrappers are already used
     - how pagination/filtering is implemented elsewhere
     - whether there is already a detection/event model to attach alert generation to
   - Reuse existing conventions exactly.

2. **Design the alert domain model**
   - Add an `Alert` entity with fields at minimum:
     - `Id`
     - `CompanyId`/`TenantId`
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
     - optional resolution/closed timestamps if consistent with current style
   - Add enums/value objects:
     - `AlertType`: `Risk`, `Anomaly`, `Opportunity`
     - `AlertSeverity`: `Low`, `Medium`, `High`, `Critical`
     - `AlertStatus`: include at least an open state plus any needed lifecycle states such as `Open`, `Acknowledged`, `Resolved`, `Closed`
   - Keep evidence structured:
     - prefer JSON-backed structure or serialized object if the architecture already uses JSONB for flexible payloads
     - ensure API returns evidence in a usable shape
   - Add factory/constructor validation so required fields cannot be empty.

3. **Define deduplication behavior**
   - Implement fingerprint-based deduplication with this rule:
     - if an incoming detection generates the same fingerprint for the same tenant/company and there is already an **open** alert, do not create a second open alert
   - Preferred behavior:
     - return the existing open alert
     - optionally update `UpdatedAt`, occurrence count, last seen timestamp, or evidence append if that fits current patterns
   - If adding recurrence metadata is low-cost and clean, include:
     - `FirstSeenAt`
     - `LastSeenAt`
     - `OccurrenceCount`
   - Do **not** create duplicate open rows for the same `(tenant/company, fingerprint)` combination.
   - Enforce dedup both:
     - in application logic
     - and, where feasible, with a database-level unique partial index on open alerts
   - For PostgreSQL, a partial unique index like below is acceptable if EF/migration tooling supports it:
     - unique on `(company_id, fingerprint)` where status is open
   - If partial unique index support is awkward in current stack, document and implement the strongest safe alternative.

4. **Add persistence**
   - Add `Alerts` table mapping in infrastructure.
   - Use PostgreSQL-friendly types:
     - UUID PK
     - text/varchar for enums if that is the project convention
     - JSONB for evidence if appropriate
     - timestamptz for timestamps
   - Add indexes for query performance:
     - `(company_id, created_at desc)`
     - `(company_id, type, created_at desc)`
     - `(company_id, severity, created_at desc)`
     - `(company_id, status, created_at desc)`
     - fingerprint dedup index
   - Add migration with clear naming.
   - Ensure DbContext includes the new set and configuration.

5. **Implement alert generation command/service**
   - Create an application command such as `GenerateAlertCommand` or `CreateAlertFromDetectionCommand`.
   - Input should support:
     - tenant/company id
     - correlation id
     - title
     - summary
     - evidence
     - fingerprint
     - type
     - severity
     - optionally source detection metadata
   - If detections already exist in the codebase, wire from that model instead of inventing a parallel one.
   - Handler behavior:
     - validate required fields
     - normalize fingerprint
     - check for existing open alert by `(tenant/company, fingerprint)`
     - if found, return existing alert / update recurrence metadata
     - if not found, create a new open alert
   - Keep the handler idempotent for repeated submissions with same fingerprint.

6. **Implement classification support**
   - Ensure the system can classify alerts into the required categories and severities.
   - If the backlog task expects explicit classification logic from detections:
     - add a small classifier abstraction, e.g. `IAlertClassifier`
     - map detection inputs to `AlertType` and `AlertSeverity`
   - If classification is already supplied by upstream detection logic:
     - validate and persist it rather than re-deriving it
   - Minimum acceptable outcome:
     - the pipeline supports and enforces the required categories and severities
     - invalid values are rejected.

7. **Implement paginated query API**
   - Add a query endpoint under existing API conventions, e.g.:
     - `GET /api/alerts`
   - Support filters:
     - tenant/company scope from auth/context and/or explicit route/query if current architecture uses that
     - `type`
     - `severity`
     - `status`
     - `createdAt` filtering/sorting
   - Support pagination:
     - page/pageSize or cursor, whichever existing APIs use
   - Return:
     - list of alert DTOs
     - pagination metadata
   - Ensure tenant isolation is enforced in query layer and API layer.
   - Default sort should be newest first unless existing conventions differ.

8. **API contract details**
   - Response DTO should include at least:
     - `id`
     - `type`
     - `severity`
     - `title`
     - `summary`
     - `evidence`
     - `status`
     - `tenantId`/`companyId`
     - `correlationId`
     - `fingerprint`
     - `createdAt`
     - `updatedAt`
   - Query parameters should be validated and safely parsed.
   - Return proper status codes:
     - `200` for query
     - `201` or `200` for create/generate depending on whether a new alert was created or dedup returned existing
   - If dedup returns an existing alert, keep behavior explicit and deterministic.

9. **Testing**
   - Add integration tests covering:
     - creating a new alert with all required fields
     - accepted types: risk/anomaly/opportunity
     - accepted severities: low/medium/high/critical
     - duplicate generation with same fingerprint does not create multiple open alerts
     - same fingerprint in different tenants creates separate alerts
     - same fingerprint after prior alert is closed/resolved can create a new open alert if lifecycle allows
     - query by tenant returns only tenant-owned alerts
     - query filters by type, severity, status
     - query pagination works and is stable
     - query sorting/filtering by createdAt behaves correctly
   - If there is an existing test fixture for API + database, use it.

10. **Quality and consistency**
   - Keep code aligned with clean architecture boundaries.
   - Avoid leaking EF entities into API contracts.
   - Avoid adding unnecessary abstractions.
   - Add concise XML/docs/comments only where existing style uses them.
   - If you discover missing prerequisite infrastructure, implement the smallest necessary supporting pieces and note them in the final summary.

# Validation steps
Run the relevant validation locally and ensure all pass.

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow:
   - generate/apply the migration as required by the repo conventions
   - verify the schema includes:
     - alerts table
     - filtering indexes
     - deduplication index/constraint

4. Manually verify behavior through tests or API calls:
   - create/generate an alert with:
     - type = risk
     - severity = high
     - required fields populated
   - submit the same fingerprint again for same tenant
     - confirm only one open alert exists
   - submit same fingerprint for different tenant
     - confirm separate alert exists
   - query alerts with combinations of:
     - type
     - severity
     - status
     - createdAt
     - pagination
   - confirm results are tenant-scoped and paginated

5. Confirm acceptance criteria mapping:
   - required fields present on generated alerts
   - categories include risk/anomaly/opportunity
   - severities include low/medium/high/critical
   - dedup prevents multiple open alerts for same fingerprint
   - API supports paginated querying by tenant/type/severity/status/createdAt

# Risks and follow-ups
- **Tenant naming mismatch risk**: the task says `tenantId`, but the architecture/backlog often uses `company_id`. Use the repository’s existing canonical naming and keep API contracts consistent.
- **Detection model ambiguity**: if no detection entity exists yet, implement a minimal alert-generation command that can be called by future detection pipelines without overbuilding.
- **Dedup race conditions**: application-level checks alone are insufficient under concurrency. Prefer a PostgreSQL unique partial index for open alerts by `(company_id, fingerprint)` and handle conflict gracefully.
- **Status lifecycle ambiguity**: acceptance criteria only require deduplication of repeated detections against open alerts. Keep lifecycle minimal but ensure “open” is clearly represented.
- **Evidence shape drift**: use a structured JSON shape rather than opaque text if possible, but do not introduce a complex schema unless the codebase already has one.
- **Pagination consistency**: match existing API pagination conventions exactly; do not invent a new envelope if one already exists.
- **Follow-up candidates after this task**:
  - alert resolution/acknowledgement commands
  - notification/inbox integration
  - recurrence counters and last-seen timestamps
  - audit event emission for alert creation/dedup hits
  - dashboard widgets and mobile alert surfaces