# Goal
Implement backlog task **TASK-26.2.1 — Create schema migration for cash posting linkage fields and traceability tables** for story **US-26.2 Handle unmatched bank transactions and complete migration and backfill support for cash posting**.

Deliver a production-ready database migration and backfill path in the .NET/PostgreSQL stack that:

- adds all required linkage fields and traceability tables to connect **payments**, **bank transactions**, and **journal entries**
- persists unmatched bank transactions with an explicit unmatched status
- prevents unmatched transactions from generating AR/AP settlement journal entries until matched or manually classified
- backfills existing onboarded tenant finance data safely, without foreign key violations or duplicate posting links
- emits operational logs per company for migrated, backfilled, skipped, and conflict counts

Use the existing modular monolith conventions and keep the implementation tenant-aware, idempotent, and safe for existing datasets.

# Scope
In scope:

- Inspect the current finance/accounting persistence model and existing migration approach in the repo
- Add PostgreSQL migration(s) for:
  - linkage fields on existing finance tables as needed
  - traceability/mapping tables for cash posting relationships
  - explicit unmatched-state persistence
  - uniqueness and foreign key constraints that prevent duplicate posting links
  - indexes needed for backfill and runtime lookup
- Implement a backfill job/service that initializes traceability records for existing tenant/company finance data
- Ensure backfill ordering avoids FK violations and is safe to rerun
- Add structured operational logging with per-company counts:
  - migrated records
  - backfilled records
  - skipped records
  - posting conflicts
- Add tests covering migration/backfill behavior and duplicate-link prevention where practical in the current test setup

Out of scope unless required by existing code paths:

- Full UI changes
- New end-user workflows beyond persistence/backfill support
- Broad refactors unrelated to cash posting linkage and unmatched transaction persistence
- Reworking unrelated accounting domain logic

# Files to touch
Start by locating the actual persistence and migration patterns before editing. Likely areas include:

- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Api/**`
- `tests/VirtualCompany.Api.Tests/**`
- `docs/postgresql-migrations-archive/README.md`

Expected file categories to touch:

- existing migration registration/configuration files
- new SQL migration file(s) or EF Core migration artifacts, depending on repo convention
- finance persistence entities/configurations
- domain/application models for unmatched status and posting traceability
- background job/backfill runner
- logging/telemetry wiring for migration-backfill execution
- automated tests for migration/backfill/idempotency

If the repo uses raw SQL migrations, prefer following that convention. If it uses EF Core migrations, use EF Core migrations consistently. Do not introduce a second migration mechanism.

# Implementation plan
1. **Discover current finance schema and migration mechanism**
   - Inspect the infrastructure project for:
     - DbContext/entity configurations
     - existing finance tables for payments, bank transactions, journal entries, settlements, postings, or reconciliation
     - migration execution pattern
   - Identify the canonical tenant key naming (`company_id` vs other) and audit column conventions.
   - Identify whether unmatched bank transactions already exist implicitly and where status should live.

2. **Design the minimal schema changes**
   Based on the existing model, add the smallest coherent set of changes needed to satisfy the task. The design must support:
   - explicit unmatched persistence state for bank transactions
   - traceability from bank transaction ↔ payment
   - traceability from bank transaction/payment ↔ journal entry
   - prevention of duplicate posting links
   - safe backfill for existing data

   Prefer a design with:
   - explicit status column on bank transaction or cash posting table, including an `unmatched` value
   - one or more mapping/traceability tables with:
     - primary key
     - `company_id`
     - FK references to the linked finance entities
     - source/match/classification metadata if needed
     - created/updated timestamps
   - unique constraints/indexes that enforce one canonical posting link where required

   If multiple designs are possible, choose the one most aligned with existing naming and accounting model already in the repo.

3. **Create the migration**
   Implement the migration with:
   - additive schema changes first
   - nullable columns where needed for safe rollout, tightening constraints only when safe
   - FK constraints added in an order compatible with existing data
   - unique indexes to prevent duplicate posting links
   - indexes on `company_id` and common lookup columns for backfill/runtime use

   Ensure the migration is safe against existing onboarded company datasets.

4. **Implement unmatched-state persistence rules**
   Update the relevant domain/application/infrastructure logic so that:
   - unmatched bank transactions are stored with explicit unmatched status
   - unmatched transactions do **not** create AR/AP settlement journal entries
   - only matched or manually classified transactions can proceed into settlement posting flows

   Keep changes focused and avoid altering unrelated posting behavior.

5. **Implement the backfill job**
   Add a backfill service/job that:
   - iterates per company/tenant
   - discovers existing finance records needing traceability initialization
   - inserts missing linkage/traceability rows in FK-safe order
   - initializes unmatched-state records where no valid match exists
   - detects and skips duplicates/conflicts rather than failing the whole company run
   - is idempotent and safe to rerun

   Backfill requirements:
   - no foreign key violations
   - no duplicate posting links
   - conflict detection logged per company
   - use batching if datasets may be large
   - use transactions at sensible boundaries, ideally per company or per batch

6. **Add operational logging**
   Emit structured logs with tenant/company context and counters for:
   - migrated records
   - backfilled records
   - skipped records
   - posting conflicts

   Logging should be:
   - per company
   - summary-oriented
   - suitable for operators diagnosing rollout issues

   If there is an existing business audit vs technical logging split, keep this in technical/operational logs unless the repo already persists migration job audit records.

7. **Add tests**
   Add or update tests to cover as much as the current test harness allows:
   - migration applies successfully
   - backfill initializes traceability for existing records
   - unmatched records are explicitly marked unmatched
   - unmatched records do not generate AR/AP settlement journal entries
   - rerunning backfill does not create duplicates
   - conflicts are counted/logged/skipped

   Prefer integration-style tests if the repo already supports database-backed tests; otherwise add focused unit tests around backfill logic and guards.

8. **Document assumptions in code comments or concise notes**
   If the existing schema lacks one of the named entities exactly as described, map the task terminology to the repo’s actual finance model and note that clearly in code comments or test names.

# Validation steps
1. Inspect and follow the repo’s migration convention.
2. Build the solution:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. If there is a migration application path in the repo, run or verify it locally against a PostgreSQL instance or test harness.
5. Validate on an existing-style seeded/onboarded dataset:
   - migration completes successfully
   - backfill completes successfully
   - no FK violations occur
   - no duplicate posting links are created
   - unmatched bank transactions persist with explicit unmatched status
   - unmatched transactions do not create AR/AP settlement journal entries
   - logs include per-company counts for migrated, backfilled, skipped, and conflicts
6. Verify idempotency by running the backfill twice and confirming:
   - no duplicate traceability rows
   - no duplicate posting links
   - only expected skipped/conflict counts increase or remain stable

# Risks and follow-ups
- **Schema ambiguity risk:** The repo may use different finance entity names than the backlog wording. Resolve by mapping to actual tables/entities rather than inventing parallel structures.
- **Migration mechanism risk:** Do not mix raw SQL migrations and EF migrations. Follow the existing project standard exactly.
- **Constraint rollout risk:** Adding non-null FKs too early can break existing onboarded datasets. Prefer phased/additive migration and backfill-safe constraints.
- **Duplicate-link risk:** Existing dirty data may already contain ambiguous relationships. Handle with conflict detection, skip logic, and logging rather than destructive auto-resolution.
- **Performance risk:** Backfill across all companies may be expensive. Batch by company and index lookup paths used by the job.
- **Behavioral risk:** Existing posting flows may currently assume implicit matching. Ensure unmatched-state gating is added where settlement journal entries are created.
- **Follow-up likely needed:** After this task, a separate task may be needed for operator tooling/reporting to review posting conflicts and manually classify unmatched transactions if not already present.