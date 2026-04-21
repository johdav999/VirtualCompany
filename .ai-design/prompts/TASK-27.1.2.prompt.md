# Goal
Implement backlog task **TASK-27.1.2** for story **US-27.1 Implement finance approval workflow state and pending approval APIs** in the existing .NET modular monolith.

Deliver:
- approval workflow persistence for finance approvals
- automatic approval task creation for **bill**, **payment**, and **exception** targets
- tenant-scoped pending approvals API
- migration-safe behavior for existing bills with no approval task
- idempotent backfill job for existing mock bills

All work must align with the architecture and backlog, especially:
- shared-schema multi-tenancy via `company_id`
- CQRS-lite application structure
- approval/workflow domain under EP-4
- background-job-safe idempotent processing
- PostgreSQL-first persistence with indexed query paths

# Scope
In scope:
- Add/extend database schema for finance approval workflows and approval tasks
- Add domain model/enums for approval target types and workflow states
- Add threshold-based approval rule evaluation for bills and payments
- Support exception approval target type in schema/domain/query shape even if automatic creation is not yet triggered by a broad exception engine
- Create pending/escalated approval tasks automatically when thresholds are exceeded
- Implement `GET /api/finance/approvals/pending`
- Ensure API returns only tenant-scoped tasks with:
  - target type
  - target id
  - assignee
  - due date
  - status
- Preserve compatibility for existing bill records after migration
- Add idempotent backfill job for existing mock bills matching approval criteria
- Add tests covering migration-safe behavior, tenant scoping, duplicate prevention, and API filtering

Out of scope unless required by existing patterns:
- full approval decision endpoints
- UI changes
- mobile changes
- generalized notification fan-out
- multi-step approval chains beyond what is needed for this task
- broad exception detection engine beyond supporting exception as a valid target type/state model

# Files to touch
Inspect the solution first and then touch the minimum necessary files in the established patterns. Likely areas:

- `src/VirtualCompany.Domain/**`
  - finance approval entities/value objects/enums
  - bill/payment domain hooks if needed
- `src/VirtualCompany.Application/**`
  - commands/services for approval rule evaluation
  - query handler for pending approvals API
  - DTOs/contracts
  - background job/backfill orchestration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - migrations
  - repositories/query implementations
  - background job registration
- `src/VirtualCompany.Api/**`
  - finance approvals controller/endpoint mapping
  - DI wiring if needed
- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests
  - tenant scoping tests
  - backfill idempotency tests
- `README.md` or relevant docs only if the repo already documents migrations/jobs/API endpoints

Also inspect:
- existing bill/payment/exception models
- existing tenant resolution/auth patterns
- existing migration approach
- existing background worker/job framework
- existing API route conventions under `/api/finance/**`

# Implementation plan
1. **Discover existing patterns before coding**
   - Find current finance domain models for bills, payments, and any exception records.
   - Find how `company_id` is enforced in repositories/controllers.
   - Find whether EF Core migrations are active and where they live.
   - Find existing background job mechanism, hosted services, scheduler, or command runner.
   - Find existing API style: controllers vs minimal APIs, MediatR/CQRS patterns, DTO naming, pagination conventions.
   - Find whether there is already an approvals module/table that should be extended instead of creating parallel concepts.

2. **Design the approval workflow data model**
   - Prefer extending an existing approval/task model if one already exists and fits.
   - Otherwise add a finance-focused approval task table with explicit tenant ownership.
   - Required fields should support acceptance criteria and query efficiency:
     - `id`
     - `company_id`
     - `target_type` with values `bill`, `payment`, `exception`
     - `target_id`
     - `assignee_id` nullable if assignment is role/rule based, but acceptance requires assignee in response so ensure query can resolve one
     - `status` with values `pending`, `approved`, `rejected`, `escalated`
     - `due_date`
     - threshold/rule context fields as needed
     - created/updated timestamps
     - optional source rule id / source hash / created_by
   - Add indexes on:
     - `company_id`
     - `assignee_id`
     - `status`
     - `due_date`
   - Add a uniqueness strategy to prevent duplicates, e.g. one active approval task per `(company_id, target_type, target_id, rule_key)` or equivalent.
   - Ensure existing bill rows do not require a non-null approval FK.

3. **Create migration**
   - Add migration for new tables/columns/indexes.
   - If adding fields to bills/payments, make them nullable or default-safe.
   - Do not invalidate existing mock or seeded bill data.
   - Include unique index/constraint for idempotent backfill and repeated rule evaluation.
   - Verify PostgreSQL naming consistency and enum/string storage conventions used by the repo.

4. **Implement domain/application rule evaluation**
   - Add approval rule evaluation service for threshold-based checks.
   - Inputs should include:
     - company/tenant context
     - target type
     - target id
     - monetary amount and currency if relevant
     - configured threshold/rule source
   - For bills and payments:
     - if amount exceeds configured limit, create or upsert a pending approval task
     - if not, do nothing
   - Support exception target type in the model and query layer even if no automatic trigger is added beyond current task needs.
   - Keep logic idempotent and deterministic.
   - If there is existing policy/threshold config on agents/company settings, reuse it rather than inventing a new config store unless necessary.

5. **Hook automatic creation into bill/payment flows**
   - Identify where bills and payments are created/imported/updated.
   - Trigger approval evaluation after persistence at the appropriate application layer boundary.
   - Avoid duplicate task creation on retries or repeated saves.
   - If there is an outbox/domain event pattern, prefer that over controller-level logic.

6. **Implement pending approvals query**
   - Add query/handler/repository for `GET /api/finance/approvals/pending`.
   - Return only records for the current tenant/company.
   - Filter statuses to `pending` and `escalated` only.
   - Response must include:
     - target type
     - target id
     - assignee
     - due date
     - status
   - Ensure no approved/rejected items leak into the response.
   - Ensure cross-tenant data cannot be returned even with direct IDs or manipulated query params.

7. **Implement API endpoint**
   - Add endpoint at exact route: `GET /api/finance/approvals/pending`
   - Use existing auth and tenant resolution.
   - Return the established API response shape consistent with repo conventions.
   - If there is an existing finance approvals controller, extend it there.

8. **Implement backfill job**
   - Add a background job/command to scan existing mock bills and create approval tasks for those matching current rule criteria.
   - Make it safe for repeated runs:
     - use unique constraint and/or existence check
     - no duplicate approval tasks
   - Scope by tenant/company.
   - If mock bills are identifiable by seed/source flags, use that; otherwise backfill all existing bills that match criteria.
   - Keep the job resumable and log useful counts:
     - scanned
     - matched
     - created
     - skipped-existing

9. **Testing**
   - Add migration/schema tests if the repo supports them.
   - Add API integration tests for:
     - pending endpoint returns only tenant-scoped records
     - endpoint returns only `pending` and `escalated`
     - response includes required fields
   - Add application/integration tests for:
     - bill over threshold creates pending approval task
     - payment over threshold creates pending approval task
     - below-threshold items do not create tasks
     - existing bills remain valid with no approval task
     - backfill creates tasks for matching existing mock bills
     - repeated backfill does not create duplicates
   - Add tests for uniqueness/idempotency under repeated evaluation if feasible.

10. **Keep implementation clean**
   - Follow existing naming, namespaces, and folder structure.
   - Do not introduce speculative abstractions.
   - Prefer small focused services and query handlers.
   - Add concise comments only where behavior is non-obvious.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration generation/application flow matches repo conventions.
   - Confirm new migration exists and is included correctly.
   - If local DB workflow exists, apply migration and verify schema.

4. Manually validate database expectations:
   - approval workflow table(s) created
   - indexes exist on `company_id`, `assignee_id`, `status`, `due_date`
   - uniqueness/idempotency constraint exists for duplicate prevention
   - existing bill rows still load without approval task rows

5. Manually validate API behavior:
   - create or seed approval tasks across multiple tenants
   - call `GET /api/finance/approvals/pending`
   - confirm only current tenant’s `pending` and `escalated` tasks are returned
   - confirm payload includes target type, target id, assignee, due date, status

6. Manually validate automatic creation:
   - create/update a bill above threshold → pending approval task created
   - create/update a payment above threshold → pending approval task created
   - create/update below threshold → no task created

7. Manually validate backfill:
   - run backfill once → matching existing mock bills get tasks
   - run backfill again → no duplicates created

# Risks and follow-ups
- There may already be a generic approvals model in the repo; duplicating it would create debt. Prefer extending existing approval infrastructure if present.
- Threshold configuration source may be ambiguous. Reuse existing company/agent policy config if available; otherwise implement the smallest viable finance approval rule source and note follow-up work.
- Assignee resolution may not yet exist for finance approvers. If no robust assignment model exists, implement a minimal deterministic assignee strategy consistent with current membership/role data and document limitations.
- Exception target support may be schema/query-only in this task if no exception producer exists yet; ensure this is explicit in code/tests.
- Backfill jobs can race with live creation flows. Use idempotent writes plus unique constraints.
- If the repo lacks a mature background job framework, implement backfill in the simplest existing hosted-service/command pattern and note future scheduler integration.
- If API response contracts are already versioned/shared, update shared DTOs carefully to avoid breaking consumers.