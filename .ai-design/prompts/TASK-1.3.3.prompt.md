# Goal
Implement `TASK-1.3.3 — Expose escalation records and policy evaluation history via API` for story `US-1.3 ST-A203 — Escalation policies`.

Deliver tenant-scoped ASP.NET Core API endpoints that expose:

- escalation records for a source entity and/or policy
- policy evaluation history related to escalation processing
- audit-log traceability via `correlationId`

The implementation must align with the existing modular monolith structure and preserve clean boundaries across API, Application, Domain, and Infrastructure.

This task is specifically about **read/exposure via API**, but the exposed data must reflect the acceptance criteria already established by the escalation engine:

- escalation records include `policyId`, `sourceEntityId`, `escalationLevel`, `reason`, and `triggeredAt`
- escalations are only executed once per policy level per source entity unless resolved/re-opened
- policy evaluation results and escalation actions are audit logged and traceable by `correlationId`

# Scope
In scope:

- Add or complete domain/application query models for escalation records and policy evaluation history
- Add persistence/query support in Infrastructure for retrieving escalation and audit/evaluation history from PostgreSQL
- Add tenant-aware API endpoints in `VirtualCompany.Api`
- Support filtering by:
  - source entity
  - policy
  - correlationId
  - date range and/or paging if project conventions already exist
- Return API DTOs that include enough data for auditability and UI consumption
- Ensure authorization and tenant scoping are enforced
- Add tests for API/query behavior

Out of scope unless required by existing code gaps:

- Building or changing the escalation evaluation engine itself
- Creating new escalation triggering behavior beyond what is necessary to expose persisted records
- UI/Blazor pages
- Mobile changes
- Large refactors unrelated to escalation read APIs

If the codebase does not yet persist policy evaluation history in a queryable form, implement the **minimum necessary persistence/query path** to satisfy the API exposure requirement without broad redesign.

# Files to touch
Inspect the solution first and then update the appropriate files in these areas.

Likely targets:

- `src/VirtualCompany.Api/**`
  - endpoint/controller files for escalation APIs
  - request/response contracts
  - DI registration if needed
- `src/VirtualCompany.Application/**`
  - queries, handlers, DTOs/view models
  - interfaces for repositories/read services
- `src/VirtualCompany.Domain/**`
  - escalation entities/value objects if missing fields are needed
  - audit/policy evaluation domain models only if necessary
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/mappings
  - repositories/read models/SQL queries
  - migrations only if persistence gaps must be closed
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - authorization/tenant-scope tests
  - correlationId/history retrieval tests

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing patterns for:
  - CQRS-lite queries
  - tenant resolution
  - pagination
  - audit event exposure
  - correlation ID propagation

# Implementation plan
1. **Discover existing escalation and audit structures**
   - Search the solution for:
     - `Escalation`
     - `PolicyEvaluation`
     - `AuditEvent`
     - `correlationId`
     - `sourceEntityId`
     - `policyId`
   - Identify whether escalation records already exist as:
     - domain entities
     - EF entities/tables
     - audit events only
   - Identify current API style:
     - controllers vs minimal APIs
     - MediatR/CQRS handlers or custom services
   - Reuse existing conventions rather than inventing new patterns.

2. **Confirm or add read model shape**
   Define application-layer DTOs/view models for:
   - `EscalationRecordDto`
     - `id`
     - `policyId`
     - `sourceEntityId`
     - `sourceEntityType` if available
     - `escalationLevel`
     - `reason`
     - `triggeredAt`
     - `correlationId`
     - `status` if available
     - `resolvedAt` / `reopenedAt` if available
   - `PolicyEvaluationHistoryItemDto`
     - `policyId`
     - `sourceEntityId`
     - `evaluationResult` or `outcome`
     - `matchedConditions` / summary
     - `reason`
     - `evaluatedAt`
     - `correlationId`
     - linked `escalationRecordId` if applicable
     - audit event reference/id if applicable

   Prefer exposing a stable API contract even if some fields are nullable.

3. **Implement application queries**
   Add query use cases such as:
   - `GetEscalationsQuery`
   - `GetEscalationByIdQuery`
   - `GetPolicyEvaluationHistoryQuery`

   Support tenant-aware filters:
   - company/tenant context from authenticated request
   - `sourceEntityId`
   - `policyId`
   - `correlationId`
   - paging/sorting if conventions exist

   Keep commands out of this task unless absolutely required.

4. **Implement infrastructure read access**
   Add repository/read-service implementations that query the underlying persistence.

   Preferred order:
   - use existing EF Core entities and DbContext mappings if already present
   - if policy evaluation history is represented through `audit_events`, project from audit records
   - if escalation records are stored separately, query them directly
   - if both exist, join/project them by `correlationId` and related target/entity references

   Ensure:
   - strict tenant filtering on every query
   - deterministic ordering, typically newest first
   - no cross-tenant leakage through correlationId lookups

5. **Expose API endpoints**
   Add endpoints following existing API conventions. Prefer routes like:
   - `GET /api/escalations`
   - `GET /api/escalations/{id}`
   - `GET /api/escalations/history`
   - or nested routes if the codebase already uses entity-centric routing, e.g.
     - `GET /api/tasks/{sourceEntityId}/escalations`
     - `GET /api/escalation-policies/{policyId}/evaluations`

   Minimum capability should include:
   - list escalation records by source entity and/or policy
   - retrieve policy evaluation history
   - filter by `correlationId`

   Response payloads should clearly surface traceability fields.

6. **Map audit log traceability**
   The acceptance criteria require policy evaluation results and escalation actions to be traceable by `correlationId`.

   Ensure the API response includes `correlationId` and, where possible:
   - audit action name
   - audit outcome
   - target type/id
   - timestamps

   If the audit module already has DTOs/contracts, reuse them instead of duplicating concepts.

7. **Handle once-per-policy-level semantics in read model**
   This task is read-focused, but the API should make the execution semantics observable.

   If data exists, expose fields that help consumers understand deduplication behavior:
   - escalation level
   - current status
   - resolution/reopen markers
   - whether a record is superseded or historical

   Do not re-implement the escalation engine unless necessary, but if you discover missing persistence preventing the API from reflecting this rule, add the smallest safe persistence enhancement and document it.

8. **Add tests**
   Add API/integration tests covering:
   - tenant-scoped retrieval returns only current tenant data
   - filtering by `sourceEntityId`
   - filtering by `policyId`
   - filtering by `correlationId`
   - escalation payload includes required fields
   - policy evaluation history is returned and linked to audit/correlation data
   - unauthorized/forbidden access behavior
   - not found behavior for cross-tenant IDs if that is the project convention

   If there are existing test fixtures/factories, use them.

9. **Keep implementation aligned with architecture**
   Follow:
   - modular monolith boundaries
   - CQRS-lite for reads
   - auditability as a domain feature
   - PostgreSQL as source of truth
   - tenant isolation in repository/query layer and application services

10. **Document assumptions in code comments or PR notes**
   If persistence for policy evaluation history is incomplete and you must infer it from audit events, make that explicit in concise comments and structure the code so a future dedicated table can replace the projection cleanly.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API behavior using the project’s existing test approach or HTTP client tooling:
   - list escalations for a tenant
   - filter by `sourceEntityId`
   - filter by `policyId`
   - retrieve evaluation history by `correlationId`

4. Confirm response contracts include:
   - `policyId`
   - `sourceEntityId`
   - `escalationLevel`
   - `reason`
   - `triggeredAt`
   - `correlationId` on escalation and/or linked history items

5. Confirm tenant isolation:
   - same `correlationId` or entity identifier from another tenant must not leak data

6. Confirm audit traceability:
   - policy evaluation history and escalation actions can be followed through `correlationId`
   - ordering is sensible and timestamps are populated

7. If migrations were added, verify:
   - migration applies cleanly
   - queries work against migrated schema
   - no destructive schema changes were introduced without necessity

# Risks and follow-ups
- **Risk: persistence gaps**
  - The codebase may not yet have first-class tables for escalation records or policy evaluation history.
  - Mitigation: project from existing audit events where possible and keep abstractions clean.

- **Risk: ambiguous routing conventions**
  - The API may already use a specific controller/minimal API style.
  - Mitigation: inspect and conform to existing patterns before adding endpoints.

- **Risk: tenant leakage**
  - Correlation-based lookups can accidentally bypass tenant scoping.
  - Mitigation: enforce tenant filter in every repository/query path, not only at controller level.

- **Risk: incomplete correlation propagation**
  - Some historical records may not have `correlationId`.
  - Mitigation: expose nullable values where needed and avoid fabricating IDs; note follow-up work if propagation is inconsistent.

- **Risk: acceptance criteria imply more than read APIs**
  - The task title is API exposure, but criteria reference evaluation, record creation, deduplication, and audit logging.
  - Mitigation: do not broaden scope unless missing persistence blocks the API; if discovered, implement only the minimal supporting changes and clearly document them.

Follow-up items to note if not completed here:
- dedicated UI views for escalation history
- richer pagination/filtering/sorting contracts
- dedicated persistence model for policy evaluation history if currently audit-derived
- explicit resolved/re-opened lifecycle exposure if not yet modeled