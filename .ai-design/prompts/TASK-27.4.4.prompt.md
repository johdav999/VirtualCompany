# Goal
Implement backlog task **TASK-27.4.4 — Build admin bootstrap endpoint for idempotent finance planning and approval backfill reruns** in the existing **.NET modular monolith** so that an authorized admin can safely rerun finance planning and approval bootstrap/backfill logic for an existing company without duplicating seeded records, while preserving migration-safe rollout behavior and dev/test startup validation.

# Scope
Focus only on the work required for this task and its direct acceptance criteria dependencies:

- Add a **safe admin-triggered bootstrap endpoint** for rerunning finance planning and approval backfills for an existing company.
- Ensure the bootstrap/backfill operation is **idempotent**:
  - no duplicate planning seed records
  - no duplicate approval seed/backfill records
  - safe to rerun multiple times for the same company
- Reuse or refactor existing finance bootstrap/seeding/backfill services rather than duplicating logic.
- Ensure any finance insight cache/materialization refresh behavior introduced by related work remains **duplicate-safe** for the same tenant and snapshot key.
- Add/extend **migration tests** for:
  - clean database migration
  - migration from current mock finance schema
  - migration of a partially seeded company
- Enforce **fail-fast startup validation** in dev/test when pending migrations exist.
- Add API/integration tests covering authorization, idempotency, and rerun behavior.

Do not expand into unrelated UI work, mobile work, or broad refactors outside the finance bootstrap/migration/admin API path.

# Files to touch
Adjust exact paths to match the current solution structure after discovery, but expect to touch files in these areas:

- `src/VirtualCompany.Api/**`
  - admin/bootstrap endpoint/controller or minimal API registration
  - request/response contracts
  - authorization policy wiring
  - startup/program migration validation
- `src/VirtualCompany.Application/**`
  - admin bootstrap command/service
  - finance planning backfill orchestration
  - approval backfill orchestration
  - idempotency guards and result model
- `src/VirtualCompany.Domain/**`
  - any domain abstractions/constants for bootstrap keys, snapshot keys, seed markers, or backfill semantics
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/db access implementation
  - migration validation implementation
  - seed/backfill persistence logic
  - migrations if needed for bootstrap tracking, cache/materialization uniqueness, or seed markers
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint authorization and idempotency tests
  - startup validation tests if present at API level
- `tests/**` integration or infrastructure test projects
  - migration tests for clean/current-mock/partial-seed scenarios

Also inspect these likely entry points before coding:

- `src/VirtualCompany.Api/Program.cs`
- existing finance module services, seeders, bootstrap jobs, or hosted services
- existing approval backfill logic
- existing migration/test helpers
- any archived migration docs in `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing finance bootstrap and backfill flow**
   - Locate current finance planning seed/bootstrap logic and approval backfill logic.
   - Identify where company setup currently seeds finance data, planning artifacts, approval policies/requests, or related records.
   - Find existing admin/internal endpoints, command handlers, or background jobs that can be reused.
   - Document the current uniqueness assumptions for seeded finance records.

2. **Define the rerunnable bootstrap contract**
   - Add an admin-only API endpoint such as:
     - `POST /api/admin/companies/{companyId}/finance/bootstrap-rerun`
     - or align with existing admin route conventions.
   - Request should be minimal and explicit, e.g. optional flags:
     - `rerunPlanningBackfill`
     - `rerunApprovalBackfill`
     - `dryRun` if easy and already supported; otherwise omit
   - Response should summarize:
     - companyId
     - whether planning backfill ran
     - whether approval backfill ran
     - counts of created/updated/skipped records
     - timestamp
     - correlation or operation id if available

3. **Refactor bootstrap logic into an idempotent application service**
   - Create or extend an application-layer orchestrator, e.g. `AdminFinanceBootstrapService`.
   - Move endpoint logic out of controllers into a command/service.
   - Ensure the service:
     - validates company existence
     - validates admin authorization through existing policy mechanisms
     - invokes planning and approval backfills in a deterministic order
     - returns a structured result
   - Prefer composition over duplicating existing seed code.

4. **Make planning backfill idempotent**
   - Audit all planning-related inserts performed during bootstrap.
   - Replace “blind insert” behavior with one of:
     - lookup by stable natural/business key and update/skip
     - upsert semantics
     - insert guarded by unique index/constraint
   - Introduce stable keys where missing, for example:
     - `company_id + plan_code`
     - `company_id + template_key`
     - `company_id + seed_version + artifact_type`
   - If seed tracking is needed, add a bootstrap tracking table or seed marker columns/records.
   - Ensure reruns do not create duplicate planning entities for partially seeded companies.

5. **Make approval backfill idempotent**
   - Audit approval-related bootstrap/backfill records.
   - Define stable uniqueness for seeded approval artifacts, such as:
     - `company_id + approval_policy_key`
     - `company_id + workflow_key + approval_type`
     - `company_id + seeded_request_key` where applicable
   - Ensure reruns:
     - create missing records
     - update compatible existing seeded records if intended
     - skip already seeded records
     - do not duplicate approval chains/steps
   - Be careful not to mutate user-generated approval records unless explicitly marked as system-seeded/backfill-managed.

6. **Protect cache/materialization snapshot uniqueness if applicable**
   - If related finance insight work added cache/materialization tables, verify uniqueness on:
     - `company_id + snapshot_key`
     - and any other required partition columns
   - Add DB constraints/indexes and refresh logic that updates/replaces existing snapshots rather than duplicating them.
   - Ensure background refresh jobs are safe under retries and concurrent execution.

7. **Add migration-safe persistence changes**
   - Create EF Core migrations for any new:
     - bootstrap tracking table
     - seed marker columns
     - unique indexes/constraints
     - cache/materialization uniqueness constraints
   - Keep migrations compatible with:
     - clean database creation
     - current mock finance schema
     - partially seeded company state
   - Avoid destructive migration behavior unless already established and safe.

8. **Implement fail-fast pending migration validation in dev/test**
   - In API startup, add environment-gated validation for development and test environments.
   - On startup, detect pending EF Core migrations and fail application startup with a clear error.
   - Do not enable this fail-fast behavior in production unless already required.
   - Keep the implementation testable and isolated, e.g. extension method or hosted startup validator.

9. **Add automated tests**
   - **Admin endpoint tests**
     - authorized admin can trigger rerun
     - unauthorized/non-admin is rejected
     - invalid/missing company returns appropriate error
   - **Idempotency tests**
     - first run creates expected planning/approval artifacts
     - second run creates zero duplicates
     - partially seeded company gets only missing records
   - **Migration tests**
     - clean database migration succeeds
     - migration from current mock finance schema succeeds
     - migration from partially seeded company succeeds and bootstrap rerun remains safe
   - **Startup validation tests**
     - dev/test startup fails when pending migrations exist
     - startup passes when schema is current

10. **Preserve auditability and operational clarity**
   - Log bootstrap rerun attempts with company context and correlation id.
   - If the system has business audit events for admin operations, emit one for bootstrap reruns.
   - Keep logs concise and safe; do not expose sensitive internals in API responses.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the full automated test suite:
   - `dotnet test`

3. Specifically verify the new admin bootstrap behavior:
   - call the new admin endpoint for a company with existing finance data
   - confirm first run creates/fixes missing planning and approval seed artifacts
   - call it again for the same company
   - confirm no duplicate seeded records are created

4. Verify partial-seed recovery:
   - prepare a company with only some planning/approval bootstrap records present
   - run the endpoint
   - confirm only missing records are added and existing seeded records are not duplicated

5. Verify migration behavior:
   - run migration tests for:
     - clean DB
     - current mock finance schema
     - partially seeded company
   - confirm schema and seed/backfill logic remain valid

6. Verify startup fail-fast:
   - in dev/test configuration, simulate pending migrations
   - confirm application startup fails with a clear pending-migrations error
   - confirm startup succeeds once migrations are applied

7. If finance insight cache/materialization tables exist:
   - run refresh job twice for the same tenant/snapshot key
   - confirm only one logical snapshot row exists per uniqueness rule

# Risks and follow-ups
- **Risk: hidden duplicate paths**
  - Existing bootstrap logic may be spread across setup flows, hosted services, and migrations. Consolidate carefully to avoid leaving one non-idempotent path behind.

- **Risk: seeded vs user-managed records**
  - Approval/planning records created by users must not be overwritten by rerun logic. Use explicit seed markers, source fields, or stable system keys.

- **Risk: migration brittleness**
  - Adding uniqueness constraints may fail on existing duplicate data. If duplicates are possible, include a migration cleanup strategy before applying constraints.

- **Risk: environment detection**
  - Startup fail-fast must apply to dev/test only and not break production boot behavior unexpectedly.

- **Risk: concurrent reruns**
  - If multiple admin calls or jobs can trigger bootstrap simultaneously, consider transaction boundaries, advisory locks, or idempotency keys.

Follow-ups if not already covered elsewhere:
- Add a lightweight operation history/audit record for admin bootstrap reruns.
- Consider a dry-run mode for diagnostics if support workflows need it.
- Consider a reusable bootstrap tracking abstraction for other seeded modules beyond finance.