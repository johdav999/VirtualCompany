# Goal
Implement backlog task **TASK-1.2.3 — Add alert query filters and pagination to alert retrieval endpoints** for **US-1.2 ST-A202 — Alert generation framework**.

The coding agent should extend the existing alert retrieval API so users can query alerts by:

- tenant/company
- type
- severity
- status
- createdAt

and receive **paginated results**.

This work must preserve the alert domain expectations from the acceptance criteria:

- alerts include required fields: `type`, `severity`, `title`, `summary`, `evidence`, `status`, `tenantId/companyId`, and `correlationId`
- supported categories include at least:
  - types: `risk`, `anomaly`, `opportunity`
  - severities: `low`, `medium`, `high`, `critical`
- retrieval is tenant-scoped
- pagination is stable and deterministic

# Scope
In scope:

- Add or update alert query request/response contracts
- Add filtering support for:
  - company/tenant scope
  - type
  - severity
  - status
  - createdAt range or equivalent createdAt filtering supported by current API conventions
- Add pagination support:
  - page/pageSize or cursor-based if the codebase already has a standard; prefer existing project conventions
  - total count metadata if the project’s API patterns support it
- Ensure sorting is deterministic, preferably:
  - `createdAt desc`, then `id desc`
- Implement query handling in application/infrastructure layers
- Update endpoint/controller/minimal API wiring
- Add tests covering filtering, tenant isolation, and pagination behavior

Out of scope unless required by existing code to make this work:

- building the full alert generation pipeline
- changing deduplication behavior
- adding new UI screens
- mobile changes
- unrelated refactors

If alerts are not yet fully implemented, do the minimum necessary to support retrieval contracts and filtering without expanding beyond this task.

# Files to touch
Inspect the solution first and then modify the actual alert-related files you find. Likely areas:

- `src/VirtualCompany.Api/**`
  - alert endpoints/controllers/minimal API registrations
  - request/response DTOs
- `src/VirtualCompany.Application/**`
  - alert query models
  - query handlers/services
  - pagination abstractions if already present
- `src/VirtualCompany.Domain/**`
  - alert entity/value objects/enums if filtering relies on domain types
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query/repository implementations
  - entity configurations
  - SQL/index-related changes if needed
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests for filters and pagination

Also inspect:

- existing shared result/paging abstractions
- tenant/company resolution patterns
- existing enum serialization conventions
- existing date filtering conventions in other endpoints

Do not invent new architectural patterns if the repo already has established ones.

# Implementation plan
1. **Discover existing alert implementation**
   - Search for:
     - `Alert`
     - `Alerts`
     - `GetAlerts`
     - `AlertStatus`
     - `AlertSeverity`
     - `AlertType`
   - Identify:
     - current endpoint shape
     - current persistence model
     - how tenant/company scoping is enforced
     - whether paging abstractions already exist

2. **Align with existing API conventions**
   - Reuse existing patterns for:
     - query parameter binding
     - paginated responses
     - validation
     - problem details / error responses
   - If no standard exists, implement a minimal consistent pattern:
     - query params:
       - `type`
       - `severity`
       - `status`
       - `createdFrom`
       - `createdTo`
       - `page`
       - `pageSize`
     - response:
       - items
       - page
       - pageSize
       - totalCount
       - totalPages if already used elsewhere

3. **Add application-layer query contract**
   - Create or update an alert retrieval query object with:
     - `CompanyId`/tenant context from authenticated request, not arbitrary caller input unless admin-only patterns already exist
     - optional `Type`
     - optional `Severity`
     - optional `Status`
     - optional `CreatedFrom`
     - optional `CreatedTo`
     - pagination fields
   - Validate:
     - page >= 1
     - pageSize within sane bounds
     - createdFrom <= createdTo
     - enum/string values map safely to supported alert categories

4. **Implement filtered query logic**
   - In infrastructure/query handler:
     - start from tenant-scoped alerts only
     - apply optional filters only when provided
     - apply deterministic ordering:
       - `CreatedAt DESC`
       - then stable tie-breaker such as `Id DESC`
     - apply pagination after filtering and ordering
   - Return paged results and metadata

5. **Preserve tenant isolation**
   - Do not trust a raw tenant/company query parameter unless the codebase explicitly supports privileged cross-tenant admin APIs
   - Resolve tenant/company from the authenticated context and enforce it in the query
   - Ensure tests verify one tenant cannot retrieve another tenant’s alerts

6. **Handle enum/string filtering carefully**
   - If domain uses enums, support API values matching existing serialization conventions
   - Ensure at least these values are supported if not already present:
     - types: `risk`, `anomaly`, `opportunity`
     - severities: `low`, `medium`, `high`, `critical`
   - Avoid breaking existing stored values or API contracts

7. **Add/adjust indexes if needed**
   - If alerts are persisted in PostgreSQL via EF Core and migrations are in active use, consider adding an index to support common retrieval:
     - `(company_id, created_at desc)`
     - and possibly composite/filter-friendly indexes involving status/type/severity depending on current schema
   - Only add migration/index work if this repo’s current workflow expects it for backlog tasks of this size

8. **Update API surface**
   - Expose filters and pagination on the alert retrieval endpoint
   - Keep endpoint naming and route structure consistent with the repo
   - Ensure response DTO includes required alert fields already expected by acceptance criteria:
     - type
     - severity
     - title
     - summary
     - evidence
     - status
     - tenant/company id
     - correlationId
     - createdAt

9. **Add tests**
   - Add API/integration tests for:
     - returns paginated alerts for current tenant only
     - filters by type
     - filters by severity
     - filters by status
     - filters by createdAt range
     - combines multiple filters
     - stable ordering across pages
     - invalid pagination values rejected
     - invalid date range rejected
     - tenant isolation enforced
   - Prefer existing test patterns and fixtures

10. **Keep changes minimal and production-safe**
   - Avoid broad refactors
   - Keep query logic readable and composable
   - Use async EF/query APIs
   - Ensure cancellation tokens flow through handlers where already standard

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Manually verify endpoint behavior using the project’s existing API style. Confirm:
   - no filters returns first page of tenant-scoped alerts
   - `type=risk`
   - `severity=high`
   - `status=open` or current supported status values
   - `createdFrom` / `createdTo`
   - `page=1&pageSize=20`
   - page 2 returns the next deterministic slice
   - response metadata matches actual result counts

5. Confirm acceptance criteria mapping:
   - alerts returned expose required fields
   - supported type/severity categories are queryable
   - users can query by tenant-scoped context, type, severity, status, and createdAt
   - results are paginated

6. If migrations are added:
   - ensure migration files compile
   - ensure local database update path matches repo conventions
   - document any migration command needed in code comments or PR notes if appropriate

# Risks and follow-ups
- **Alert model may not yet exist or may be partial**
  - If so, implement only the retrieval/filtering slice needed for this task and avoid inventing the full alert subsystem.

- **Tenant naming mismatch**
  - Architecture mentions both `tenantId` and `company_id`. Follow the repo’s actual naming and keep external contracts consistent.

- **Pagination convention uncertainty**
  - Reuse existing paging abstractions if present. Do not introduce a second pagination style.

- **Date filtering ambiguity**
  - If `createdAt` filtering convention is not defined, use `createdFrom` and `createdTo` and document it in code comments or endpoint metadata.

- **Enum/string compatibility**
  - Be careful not to break existing clients if current API uses different casing or serialization.

- **Performance**
  - Filtering plus pagination should be executed in the database, not in memory.
  - Consider indexes as a follow-up if not appropriate to add now.

- **Follow-up suggestions**
  - Add OpenAPI documentation/examples for alert filters
  - Add cursor pagination later if alert volume grows
  - Add richer filters such as correlationId, assigned agent, or dedup/open-only views if future backlog requires it