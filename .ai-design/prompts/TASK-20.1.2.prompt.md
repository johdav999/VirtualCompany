# Goal
Implement backlog task **TASK-20.1.2 — Add company-level finance seed status fields and timestamps to persistence model** for story **US-20.1 ST-FUI-409 — Detect finance seeding state for new and existing companies**.

The coding agent should update the persistence model so the system can store company-level finance seeding metadata that supports fast request-time detection of finance seed state.

Target outcome:
- The `companies` persistence model exposes a finance seeding state with values:
  - `not_seeded`
  - `partially_seeded`
  - `fully_seeded`
- The model also stores timestamps needed to support detection and lifecycle tracking.
- Database schema, domain model, EF Core mappings/configuration, and tests are updated consistently.
- This task is persistence-focused, but the design must support a later shared detection service/endpoint without requiring full dataset scans.

# Scope
In scope:
- Add company-level finance seeding status field(s) to the `companies` table/entity.
- Add timestamp field(s) needed to track finance seeding lifecycle.
- Add any lightweight supporting metadata fields that make fast-path detection practical.
- Add/update EF Core entity configuration and migrations.
- Add/update domain model and any shared constants/enums/value objects used by persistence.
- Add automated tests covering persistence mapping and migration expectations.
- Preserve multi-tenant/shared-schema conventions already used in the solution.

Out of scope unless required by existing architecture patterns:
- Building the full finance seed detection service.
- Adding UI changes.
- Adding API endpoints.
- Implementing background job orchestration logic.
- Performing expensive backfills beyond what is necessary for safe migration defaults.

Design constraints:
- Prefer explicit columns over opaque JSON for queryable status/timestamps.
- Keep the model compatible with request-time detection via metadata and lightweight existence checks.
- Do not introduce a design that requires scanning all finance records to determine state.
- If the codebase already has conventions for status enums as strings, follow them.
- If there is an existing migration approach in the repo, use it rather than inventing a new one.

# Files to touch
Inspect the solution first, then update the relevant files in the established project structure. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Company aggregate/entity
  - Any domain constants/enums/value objects for finance seed state
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations folder / migration classes
- `src/VirtualCompany.Application/**`
  - Only if shared contracts or mapping layers require the new fields
- `src/VirtualCompany.Shared/**`
  - Only if status values are shared across layers by convention
- `tests/**`
  - Persistence/migration tests
  - Domain mapping tests
  - Any integration tests that validate schema behavior

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover current company persistence shape**
   - Inspect the `Company` domain/entity model and EF configuration.
   - Confirm whether status-like fields are represented as strings, enums, smart enums, or value objects.
   - Confirm migration workflow and naming conventions.
   - Check whether there are existing finance-related tables/entities that will later participate in lightweight existence checks.

2. **Design the persistence fields**
   Add company-level fields that support fast-path finance seed detection. Prefer a minimal but future-proof set such as:
   - `finance_seed_status` / `FinanceSeedStatus`
   - `finance_seed_status_updated_at` / `FinanceSeedStatusUpdatedAt`
   - `finance_seeded_at` / `FinanceSeededAt` for completed/full seed timestamp
   Optionally add a metadata timestamp if justified by existing patterns, but do not over-model.

   Recommended semantics:
   - `not_seeded`: no finance seed metadata indicating completion and no known seeded finance footprint
   - `partially_seeded`: some finance records or partial metadata exist, but not enough to classify as fully seeded
   - `fully_seeded`: finance seed process completed according to metadata/rules

   Persistence guidance:
   - Store status as a constrained string if that matches project conventions.
   - Timestamps should be `timestamptz`/UTC-compatible nullable columns where appropriate.
   - Ensure defaults are safe for existing companies.

3. **Update the domain model**
   - Add the new properties to the company entity/aggregate.
   - If the project uses domain enums/constants, introduce a finance seed state type with the exact accepted values:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
   - Keep naming aligned with existing domain conventions.
   - Avoid embedding detection logic here unless the codebase already places simple invariants on entities.

4. **Update EF Core mapping/configuration**
   - Map the new columns explicitly.
   - Apply length/nullability/default constraints consistent with existing style.
   - If the project uses check constraints for status fields, add one for the allowed values.
   - Ensure the mapping is PostgreSQL-friendly.

5. **Create the database migration**
   - Add the new columns to `companies`.
   - Set a safe default for existing rows, likely `not_seeded`, unless the existing migration strategy prefers nullable + backfill.
   - Add indexes only if justified for expected query patterns; avoid speculative indexing.
   - If using a check constraint, include it in the migration.

   Migration should be safe for existing tenants and existing company rows.

6. **Handle existing data carefully**
   - For pre-existing companies, choose a migration strategy that does not incorrectly mark them as fully seeded.
   - Recommended default:
     - status = `not_seeded`
     - status updated timestamp = migration execution time or nullable, depending on conventions
     - seeded-at = null
   - Do not attempt a heavy data backfill scan in this task.
   - Leave room for a later reconciliation/detection task to classify legacy companies using metadata + lightweight existence checks.

7. **Add tests**
   Add automated tests that fit the repo’s current testing style:
   - Domain/entity tests for allowed values or property behavior if applicable.
   - Persistence mapping tests validating the new columns are mapped.
   - Migration/integration tests validating:
     - schema contains the new fields
     - default behavior for newly inserted companies
     - invalid status values are rejected if a check constraint exists

   Since the acceptance criteria mention inconsistent metadata/data combinations, this task should at least prepare for that by ensuring the persistence model can represent those states. If there are already finance-related persistence tests, extend them minimally.

8. **Document assumptions in code comments if needed**
   - Keep comments brief.
   - Clarify that these fields are the canonical metadata fast path for future shared detection logic.

9. **Do not overreach**
   - Do not implement the full detection service unless required to make the build pass.
   - Keep this task focused on persistence model changes that unblock shared detection logic.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run relevant tests:
   - `dotnet test`

3. If the repo supports migration generation/verification, ensure the migration is present and consistent with the model snapshot.

4. Manually verify:
   - `Company` model includes finance seed status and timestamps.
   - EF configuration maps them correctly.
   - Migration updates the `companies` table.
   - Allowed values are exactly:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`

5. Confirm the design supports acceptance criteria indirectly:
   - metadata exists for fast-path request-time detection
   - timestamps support lifecycle tracking
   - no full dataset scan is required just to read current metadata

# Risks and follow-ups
- **Risk: wrong default for legacy companies**
  - Defaulting all existing companies to `not_seeded` may temporarily under-classify companies with existing finance data.
  - This is acceptable for this task if a later reconciliation/detection task corrects classification using lightweight checks.

- **Risk: enum/string mismatch across layers**
  - If some layers use enums and others use raw strings, keep the persistence representation consistent and avoid accidental casing/value drift.

- **Risk: future detection rules need more metadata**
  - If later tasks require richer provenance, a follow-up may add fields like last detection timestamp or seed source/version.
  - Do not add speculative columns now unless clearly justified by existing patterns.

- **Follow-up expected after this task**
  - Implement shared finance seed detection service/endpoint used by UI, onboarding, and background jobs.
  - Add lightweight existence-check strategy against finance tables.
  - Add reconciliation logic for inconsistent metadata vs actual finance records.
  - Expand tests to cover all three states and inconsistent metadata/data combinations end-to-end.