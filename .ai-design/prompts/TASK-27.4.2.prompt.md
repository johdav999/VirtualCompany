# Goal
Implement backlog task **TASK-27.4.2 — Add insight API endpoints and optional snapshot refresh job with retention handling** for story **US-27.4 Add finance insight services and migration-safe rollout verification**.

Deliver a migration-safe, tenant-scoped finance insight capability in the existing .NET modular monolith that:
- computes finance insights from current finance data,
- exposes them through API endpoints for agent/system consumption,
- optionally persists/cache snapshots with idempotent refresh behavior and retention cleanup,
- adds migration and startup validation safeguards,
- and supports a safe admin bootstrap rerun for existing companies without duplicate seeded records.

# Scope
In scope:
- Add application/domain services to compute these tenant-scoped finance insights:
  - top expenses
  - revenue trend
  - burn rate
  - overdue customer risk
  - payable pressure
- Add structured API endpoint(s) returning:
  - machine-friendly metrics
  - narrative-ready fields
  - `generatedAt` timestamp
- If using snapshot/materialization/cache tables:
  - add EF Core migrations
  - ensure refresh job is idempotent
  - prevent duplicate rows for same tenant + snapshot key
  - add retention handling
- Add automated migration tests covering:
  - clean database migration
  - migration from current mock finance schema
  - migration of partially seeded company
- Add startup validation in dev/test to fail fast on pending migrations
- Add safe admin-triggered bootstrap/backfill rerun for an existing company without duplicating seeded records
- Add/adjust tests for service logic, API behavior, migration safety, and bootstrap idempotency

Out of scope:
- UI/dashboard work unless required only for wiring/admin trigger exposure
- mobile changes
- unrelated finance schema redesign
- introducing external schedulers/brokers if existing background job patterns already exist

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- Finance domain/application services
  - query/services for finance metrics
  - DTOs/contracts for insight responses
- API endpoints/controllers/minimal API mappings
  - finance insight endpoint(s)
  - admin bootstrap endpoint/handler if not already present
- Infrastructure persistence
  - EF Core entity configurations
  - DbContext
  - migrations
  - repositories/query services
- Background jobs
  - snapshot refresh worker/job
  - retention cleanup logic
- Startup/program wiring
  - migration validation on startup in dev/test
  - DI registration
- Tests
  - unit tests for insight calculations
  - integration/API tests
  - migration tests
  - bootstrap idempotency tests

Potential concrete files, depending on current structure:
- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Infrastructure/*DbContext*`
- `src/VirtualCompany.Infrastructure/Migrations/*`
- `src/VirtualCompany.Application/**/*Finance*`
- `src/VirtualCompany.Api/**/*Finance*`
- `src/VirtualCompany.Infrastructure/**/*Bootstrap*`
- `tests/VirtualCompany.Api.Tests/**/*`

Do not assume exact paths/classes exist; discover and align with existing conventions before editing.

# Implementation plan
1. **Discover existing finance and migration patterns**
   - Inspect current finance-related entities, mock finance schema, seed/bootstrap flows, and any existing background job infrastructure.
   - Identify:
     - how tenant/company context is resolved,
     - where finance data currently lives,
     - how migrations are applied,
     - whether there is already a bootstrap/backfill admin operation,
     - whether there is an existing scheduler/hosted service pattern.

2. **Design the insight contract**
   - Add a structured response model for finance insights with:
     - tenant/company scope implied by auth/context
     - `generatedAt`
     - per-insight metric payloads
     - narrative-ready summary fields suitable for agent consumption
   - Keep the contract stable and explicit, e.g. sections like:
     - `topExpenses`
     - `revenueTrend`
     - `burnRate`
     - `overdueCustomerRisk`
     - `payablePressure`
     - optional `highlights` / `narrativeHints`
   - Prefer typed DTOs over loose dictionaries.

3. **Implement finance insight services**
   - Add application services/queries that compute each required insight from current finance data.
   - Ensure all queries are tenant-scoped.
   - Handle sparse/mock/partially seeded data safely:
     - return empty collections or null-safe metrics where appropriate
     - avoid divide-by-zero and invalid trend calculations
   - Keep calculation logic testable and separated from HTTP concerns.

4. **Add optional snapshot/materialization support**
   - Only if justified by current architecture/performance patterns, add a snapshot table for cached/materialized insight results.
   - If added:
     - create a table keyed to prevent duplicates for same tenant + snapshot key (+ snapshot period if applicable)
     - store generated payload/metrics and timestamps
     - add unique constraint/index enforcing idempotency
     - define retention policy fields and cleanup behavior
   - If not added, still satisfy API/service requirements and document why snapshotting was unnecessary.

5. **Implement refresh job with retention handling**
   - If snapshotting exists, add a background refresh job/hosted service/command that:
     - refreshes snapshots idempotently
     - upserts instead of blindly inserting
     - avoids duplicate rows for same tenant and snapshot key
     - cleans up expired/old snapshots according to retention policy
   - Use existing job coordination patterns if present.
   - Ensure failures are logged and do not corrupt tenant data.

6. **Expose insight API endpoint(s)**
   - Add authenticated tenant-scoped endpoint(s), likely under existing finance routes.
   - Return structured insight payload with `generatedAt`.
   - If snapshotting is enabled, endpoint may read latest valid snapshot or compute on demand per existing conventions.
   - Ensure response shape is agent-friendly and deterministic.

7. **Add migration-safe schema changes**
   - Create EF Core migration(s) for any new tables/indexes/constraints.
   - Pay special attention to compatibility with:
     - clean database creation
     - current mock finance schema
     - partially seeded company data
   - Avoid destructive assumptions about existing seed data.

8. **Add startup migration validation**
   - In dev and test environments, fail fast during startup if pending migrations exist.
   - Reuse existing environment/config patterns.
   - Do not force this behavior in production unless already established by project convention.
   - Make the failure message actionable.

9. **Implement safe admin bootstrap rerun**
   - Add or update an admin-triggered bootstrap/backfill operation so it can rerun planning/approval backfills for an existing company.
   - Make it idempotent:
     - detect existing seeded/backfilled records
     - update/reconcile where appropriate
     - never duplicate seeded records
   - Scope it safely to the target company and admin authorization.

10. **Add comprehensive tests**
    - Unit tests:
      - insight calculations for normal and sparse data cases
      - duplicate prevention/idempotent snapshot refresh logic
      - retention cleanup behavior
      - bootstrap rerun idempotency
    - Integration/API tests:
      - insight endpoint returns expected structure and `generatedAt`
      - tenant isolation
      - admin bootstrap endpoint/handler behavior
    - Migration tests:
      - clean database migration
      - migration from current mock finance schema
      - migration of partially seeded company
    - Startup validation tests:
      - pending migrations in dev/test cause startup failure

11. **Keep implementation aligned with architecture**
    - Respect modular monolith boundaries:
      - domain/application logic outside controllers
      - infrastructure handles persistence/migrations/jobs
      - API only wires endpoints and auth
    - Use typed contracts and CQRS-lite patterns already present.
    - Preserve tenant isolation in every query and write path.

# Validation steps
1. Restore/build:
   - `dotnet build`
2. Run full tests:
   - `dotnet test`
3. Specifically verify:
   - finance insight service tests pass
   - API tests for insight endpoint pass
   - migration tests pass for:
     - clean DB
     - current mock finance schema
     - partially seeded company
   - bootstrap rerun tests confirm no duplicate seeded records
   - startup validation tests fail fast when migrations are pending in dev/test
4. If migrations were added:
   - verify generated migration is deterministic and checked in
   - verify unique constraints/indexes enforce no duplicate snapshot rows
5. Manual verification if practical:
   - run API locally against seeded/mock finance data
   - call insight endpoint for a tenant and confirm:
     - structured metrics present
     - narrative-ready fields present
     - `generatedAt` present
   - trigger snapshot refresh/bootstrap rerun twice and confirm idempotent results

# Risks and follow-ups
- **Unknown current finance schema shape**: inspect before coding; do not hardcode assumptions from backlog text alone.
- **Snapshot table may be unnecessary**: only add it if needed; acceptance criteria says “if insight cache or materialization tables are added”.
- **Migration fragility**: mock schema and partially seeded states may contain edge cases; write explicit migration tests before finalizing schema changes.
- **Bootstrap duplication risk**: use natural keys/unique constraints/existence checks to guarantee idempotency.
- **Startup validation impact**: ensure fail-fast behavior is limited to dev/test and does not break production startup unexpectedly.
- **Tenant isolation**: every insight query, snapshot refresh, and bootstrap rerun must be company-scoped.
- **Narrative fields**: keep them deterministic/template-based from computed metrics, not LLM-generated.
- **Follow-up suggestion**: if snapshotting is introduced, consider adding observability around refresh duration, row counts, retention deletions, and per-tenant failures.