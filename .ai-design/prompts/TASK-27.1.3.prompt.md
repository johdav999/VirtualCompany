# Goal
Implement backlog task **TASK-27.1.3 — Expose pending approvals API and approval action endpoints for approve, reject, and escalate** for story **US-27.1 Implement finance approval workflow state and pending approval APIs**.

Deliver a tenant-scoped finance approval workflow foundation in the existing .NET modular monolith, including:

- database migration for approval workflow persistence
- domain/application/infrastructure support for finance approval tasks
- automatic creation of pending approval tasks from threshold-based rules for bills and payments
- pending approvals query API
- approval action APIs for approve, reject, and escalate
- idempotent backfill job for existing mock bills
- tests covering migration compatibility, tenant isolation, filtering, and duplicate prevention

The implementation must satisfy these acceptance criteria exactly:

- A database migration creates approval workflow tables and fields with indexes on `companyId`, `assigneeId`, `status`, and `dueDate`.
- The system supports approval targets of `bill`, `payment`, and `exception` with workflow states `pending`, `approved`, `rejected`, and `escalated`.
- Threshold-based approval rules create pending approval tasks automatically when a bill or payment exceeds configured limits.
- `GET /api/finance/approvals/pending` returns only tenant-scoped pending and escalated approval tasks with target type, target id, assignee, due date, and status.
- Existing bill records remain valid after migration even when no approval task exists for the bill.
- A backfill job creates approval tasks for existing mock bills that match approval rule criteria without creating duplicates on repeated runs.

# Scope
In scope:

- Add approval workflow persistence model for finance approvals.
- Add nullable linkage from approval task to finance targets as needed without breaking existing bill data.
- Add indexes optimized for pending approval inbox queries.
- Add domain enums/value objects/entities for:
  - target types: `bill`, `payment`, `exception`
  - statuses: `pending`, `approved`, `rejected`, `escalated`
- Add application commands/queries for:
  - listing pending approvals
  - approving an approval task
  - rejecting an approval task
  - escalating an approval task
- Add API endpoints:
  - `GET /api/finance/approvals/pending`
  - `POST /api/finance/approvals/{id}/approve`
  - `POST /api/finance/approvals/{id}/reject`
  - `POST /api/finance/approvals/{id}/escalate`
- Enforce tenant scoping on all reads/writes.
- Implement threshold evaluation for bill/payment creation or update flow where finance records already exist.
- Implement idempotent backfill job for existing mock bills.
- Add tests.

Out of scope unless required by existing patterns:

- Full approval chain UX
- notifications/mobile work
- generalized workflow engine redesign
- non-finance approval inboxes
- arbitrary approval policy builder UI
- broker/outbox expansion beyond what is necessary for this task

If the codebase already has adjacent approval abstractions, extend them rather than creating a parallel model. Prefer the smallest implementation that matches current architecture and naming conventions.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

1. **Domain**
- finance entities for bill/payment if present
- approval workflow entity/entities
- enums for approval target type and status
- domain services or rule evaluators for threshold checks

2. **Application**
- query handler for pending approvals
- command handlers for approve/reject/escalate
- DTOs/contracts for API responses and action requests
- backfill job command/service
- validation classes if FluentValidation or equivalent is used

3. **Infrastructure**
- EF Core DbContext mappings/configurations
- repository/query implementations
- migration(s)
- hosted service/background job registration if backfill is implemented as a worker
- seed/mock data integration if needed for existing mock bills

4. **API**
- finance approvals controller or minimal API endpoint mapping
- request/response contracts if API layer owns them
- authorization/tenant resolution wiring

5. **Tests**
- API integration tests for tenant-scoped pending list
- action endpoint tests
- migration compatibility test for existing bills
- backfill idempotency test
- threshold-trigger creation test

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Use existing repository conventions for migrations, endpoint registration, MediatR/CQRS patterns, and test fixtures.

# Implementation plan
1. **Discover existing finance and approval structures**
   - Search for:
     - bill/payment entities and APIs
     - approval-related entities/tables/endpoints
     - tenant resolution patterns
     - EF Core migration conventions
     - background job/hosted service patterns
   - Reuse existing abstractions where possible.
   - Identify whether finance mock bills already exist and where they are seeded.

2. **Design the persistence model**
   - Add or extend a finance approval task table/entity with fields covering:
     - `Id`
     - `CompanyId`
     - `TargetType` (`bill`, `payment`, `exception`)
     - `TargetId`
     - `AssigneeId` or assignee reference consistent with current user model
     - `Status` (`pending`, `approved`, `rejected`, `escalated`)
     - `DueDate`
     - threshold/rule context if needed
     - created/updated timestamps
     - acted-on metadata for approve/reject/escalate if useful
   - Ensure existing bills remain valid:
     - do not require every bill to have an approval task
     - use nullable foreign keys or no FK from bill to approval task if that is safer
   - Add indexes on:
     - `CompanyId`
     - `AssigneeId`
     - `Status`
     - `DueDate`
   - Add a uniqueness strategy to prevent duplicate approval tasks for the same active target/rule combination during backfill and repeated threshold evaluation. Prefer a DB-level unique index if feasible, e.g. on active task identity dimensions, or enforce idempotency in code plus a supporting index.

3. **Create the migration**
   - Generate/update EF migration for PostgreSQL.
   - Verify migration is additive and backward-compatible.
   - Ensure no existing bill rows become invalid due to non-null constraints or required relationships.
   - If there are existing finance tables, avoid destructive changes.

4. **Implement domain logic for approval workflow**
   - Add enums/constants for target types and statuses.
   - Add entity behavior methods:
     - `Approve(...)`
     - `Reject(...)`
     - `Escalate(...)`
   - Guard invalid transitions:
     - only `pending` or `escalated` should be actionable unless current business rules say otherwise
     - prevent approving/rejecting already terminal tasks
   - If escalation changes assignee or due date, model that explicitly and minimally.

5. **Implement threshold-based creation**
   - Find the bill/payment creation/update path.
   - Add a threshold evaluator that checks configured limits.
   - When a bill or payment exceeds configured limits:
     - create a pending approval task automatically
     - do not create duplicates if one already exists for the same target in a non-terminal actionable state
   - Keep this logic tenant-scoped and deterministic.
   - If approval rule configuration does not yet exist, use the existing configuration source in the repo; if absent, add the smallest configurable mechanism already aligned with finance settings patterns.

6. **Implement pending approvals query**
   - Add application query and handler for `GET /api/finance/approvals/pending`.
   - Return only records for the current tenant/company.
   - Filter to statuses:
     - `pending`
     - `escalated`
   - Response items must include:
     - target type
     - target id
     - assignee
     - due date
     - status
   - Keep response shape concise and stable.
   - Order by due date ascending, then created date if no existing convention exists.

7. **Implement action endpoints**
   - Add:
     - `POST /api/finance/approvals/{id}/approve`
     - `POST /api/finance/approvals/{id}/reject`
     - `POST /api/finance/approvals/{id}/escalate`
   - Each endpoint must:
     - resolve tenant context
     - load only approval tasks belonging to that tenant
     - apply transition rules
     - persist status changes
   - For reject/escalate, support a minimal request body with optional comment/reason if consistent with current API style.
   - Return appropriate status codes:
     - `200`/`204` on success per project convention
     - `404` for missing or cross-tenant records
     - `400`/`409` for invalid state transitions per existing API error style

8. **Implement backfill job**
   - Add an idempotent backfill process for existing mock bills.
   - It should:
     - scan existing bills in tenant scope or all tenants depending on current worker conventions
     - evaluate threshold criteria
     - create missing approval tasks only
     - skip bills that already have matching approval tasks
   - Repeated runs must not create duplicates.
   - Implement as:
     - application service callable from tests, and
     - optionally a hosted/background job registration if the codebase already supports this pattern
   - Prefer explicit idempotency over timing-based assumptions.

9. **Wire API and DI**
   - Register handlers/services/repositories.
   - Map endpoints/controllers.
   - Ensure authorization and tenant resolution are applied consistently with existing APIs.

10. **Add tests**
   - Integration tests for:
     - pending endpoint returns only current tenant records
     - pending endpoint includes only `pending` and `escalated`
     - approve endpoint updates status
     - reject endpoint updates status
     - escalate endpoint updates status
     - cross-tenant action/read returns not found/forbidden per existing convention
     - threshold-based creation on qualifying bill/payment
     - migration compatibility: existing bills without approval tasks still load and remain valid
     - backfill creates tasks for qualifying mock bills
     - backfill is idempotent on repeated runs
   - If repository/unit tests are common in the solution, add focused tests for transition guards and duplicate prevention.

11. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries:
     - API -> Application -> Domain -> Infrastructure
   - Keep CQRS-lite:
     - queries for inbox listing
     - commands for state changes
   - Do not bypass tenant scoping in repositories or handlers.
   - Avoid direct DB access from controllers.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, verify the new migration is present and compiles.

4. Manually validate API behavior with existing test host or integration tests:
   - create or seed two tenants
   - seed approval tasks across both tenants
   - call `GET /api/finance/approvals/pending`
   - confirm only current tenant + statuses `pending`/`escalated` are returned

5. Validate action transitions:
   - approve a pending task
   - reject a pending task
   - escalate a pending task
   - verify persisted status changes
   - verify terminal tasks cannot be actioned again if that rule is implemented

6. Validate threshold creation:
   - create/update a bill above threshold
   - confirm a pending approval task is created automatically
   - repeat the triggering operation if applicable and confirm no duplicate actionable task is created

7. Validate migration compatibility:
   - seed or use existing bill rows without approval tasks
   - apply migration
   - confirm bills remain queryable and valid

8. Validate backfill idempotency:
   - run backfill once and record created task count
   - run backfill again
   - confirm no additional duplicates are created

9. Include in the final implementation summary:
   - files changed
   - migration name
   - endpoint contracts
   - any assumptions made about threshold configuration source

# Risks and follow-ups
- **Unknown existing finance model shape**: bill/payment entities or approval abstractions may already exist under different names. Mitigation: inspect first and extend existing patterns.
- **Threshold configuration ambiguity**: acceptance criteria require configured limits, but the exact config source may not yet exist. Follow existing finance/agent policy config if present; otherwise implement the smallest internal configuration path and document assumptions.
- **Duplicate prevention complexity**: if multiple approval tasks per target are allowed historically, use a uniqueness rule only for active/actionable tasks and preserve terminal history.
- **Escalation semantics may be underspecified**: if no assignee reassignment rules exist, implement status transition to `escalated` with optional reason and preserve current assignee unless existing business rules dictate otherwise.
- **Migration safety**: avoid making approval linkage mandatory on bills/payments.
- **Tenant isolation**: ensure every query and action path filters by `CompanyId`; do not rely solely on controller-level checks.
- **Backfill execution model**: if no worker framework exists yet, implement a callable application service plus tests now, and leave scheduling/hosted execution as a follow-up.
- **Follow-up suggestions after completion**:
  - add approval audit events
  - add notification fan-out for new/escalated approvals
  - add pagination/filtering for pending approvals inbox
  - add ordered multi-step approval chains
  - add explicit approval rule management APIs/UI