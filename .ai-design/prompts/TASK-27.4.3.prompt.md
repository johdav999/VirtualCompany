# Goal
Implement backlog task **TASK-27.4.3 — Create migration verification tests and startup pending-migration guard for dev and test** for story **US-27.4 Add finance insight services and migration-safe rollout verification**.

Deliver a coding change that:

- adds **automated migration verification tests**
- adds a **startup guard** that fails fast in **Development** and **Test** when EF Core migrations are pending
- aligns with the acceptance criteria around migration safety for finance insight rollout
- preserves safe local/dev workflows and does not affect production startup behavior unless already explicitly configured

The implementation must be production-quality, minimal, and consistent with the existing .NET modular monolith architecture.

# Scope
In scope:

1. **Migration verification tests**
   - Cover:
     - clean database migration from zero
     - migration from the current mock finance schema
     - migration of a partially seeded company
   - Verify schema reaches the latest expected state without migration failures
   - Verify seeded/backfilled/bootstrap-sensitive data does not duplicate when rerun where applicable
   - If finance insight cache/materialization tables exist or are introduced, verify migration and refresh behavior do not create duplicate rows for the same tenant + snapshot key

2. **Startup pending-migration guard**
   - On app startup in **Development** and **Test** environments, detect pending EF Core migrations
   - Fail fast with a clear error message if any pending migrations exist
   - Do not enable this fail-fast behavior for Production unless the codebase already has an explicit pattern for it

3. **Supportive plumbing**
   - Add any small infrastructure abstractions/helpers needed for:
     - migration checking
     - test database setup/teardown
     - schema verification
     - deterministic bootstrap/backfill rerun validation

Out of scope unless required by existing code coupling:

- redesigning finance insight services
- broad refactors of startup composition
- changing production deployment strategy
- introducing a new migration framework
- adding unrelated seed data changes

# Files to touch
Inspect the repo first and then update the most relevant files. Expected likely touch points include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Infrastructure/...` files related to:
  - EF Core `DbContext`
  - migrations
  - seeding/bootstrap/backfill services
  - finance insight persistence/materialization if present
- `tests/VirtualCompany.Api.Tests/...` for integration/migration tests
- possibly shared test infrastructure files under:
  - `tests/VirtualCompany.Api.Tests/`
  - any existing test host / WebApplicationFactory / database fixture helpers
- if needed, add new test-only helpers for PostgreSQL-backed migration verification

Before coding, locate:
- the primary application `DbContext`
- current EF Core migrations assembly
- any existing startup migration/apply logic
- any bootstrap/backfill/admin rerun logic for planning/approval seeding
- any finance mock schema artifacts referenced by prior migrations or archived docs
- any existing integration test conventions for database-backed tests

# Implementation plan
1. **Discover current migration architecture**
   - Find the main EF Core `DbContext` and migrations assembly.
   - Identify whether startup currently:
     - applies migrations automatically
     - validates connectivity only
     - does nothing migration-related
   - Identify how environments are determined in `Program.cs` or startup extensions.
   - Search for:
     - `Database.Migrate`
     - `GetPendingMigrations`
     - `EnsureCreated`
     - seed/bootstrap/backfill services
     - finance-related migrations, tables, and mock schema references

2. **Design the startup guard**
   - Add a startup validation step in API startup that runs only when environment is:
     - `Development`
     - `Test` (or equivalent project convention if a custom environment name is used)
   - The guard should:
     - create a scoped service provider
     - resolve the main `DbContext`
     - call `Database.GetPendingMigrationsAsync(...)`
     - if any pending migrations exist, throw an exception before the app begins serving requests
   - Error message should clearly state:
     - pending migrations were detected
     - current environment
     - migration names
     - expected remediation, e.g. apply migrations before startup
   - Keep implementation isolated in an extension/helper if that matches repo style.

3. **Avoid production behavior regressions**
   - Ensure the guard is not active in Production by default.
   - If the app already auto-applies migrations in some environments, preserve intended behavior and only add fail-fast validation where required.
   - Do not silently apply migrations as part of this task unless the existing codebase already does so and tests depend on it.

4. **Implement migration verification test infrastructure**
   - Prefer real PostgreSQL integration tests if the repo already supports them.
   - Do not use EF InMemory provider for migration verification.
   - Create reusable helpers to:
     - create isolated test databases
     - apply migrations
     - inspect schema/data
     - optionally execute raw SQL to simulate legacy/mock schema state
   - Keep tests deterministic and self-cleaning.

5. **Add clean database migration test**
   - Create a test that:
     - provisions an empty database
     - applies all migrations from baseline to latest
     - verifies success
     - verifies expected finance insight-related tables/indexes exist if applicable
   - Also verify the database reports no pending migrations after migration completes.

6. **Add current mock finance schema migration test**
   - Reconstruct the “current mock finance schema” using one of these approaches, preferring the most accurate available:
     - apply migrations up to the migration immediately before the finance rollout
     - or execute SQL fixture/setup script that mirrors the current mock finance schema
   - Then migrate to latest and verify:
     - migration succeeds
     - legacy/mock finance data remains compatible
     - expected transformed/new structures exist
     - no duplicate rows are introduced in any cache/materialization/bootstrap tables relevant to finance insights

7. **Add partially seeded company migration test**
   - Create a database state representing a company that is only partially seeded/backfilled.
   - Then run migration and any relevant bootstrap/backfill rerun path.
   - Verify:
     - migration succeeds
     - rerun is safe
     - no duplicate seeded records are created
     - planning/approval backfills remain idempotent for the existing company
   - If there is an admin bootstrap command/service already, invoke that service in the test rather than duplicating logic.

8. **Validate finance insight snapshot/materialization uniqueness if applicable**
   - If the implementation already contains or this task branch introduces:
     - insight cache tables
     - materialized snapshot tables
     - refresh jobs
   - Then add assertions for uniqueness by tenant and snapshot key.
   - Prefer verifying:
     - unique index/constraint exists
     - rerunning refresh job updates/replaces safely or no-ops
     - duplicate rows are not produced
   - If such tables do not exist yet, do not invent unnecessary persistence just for this task; keep tests aligned to actual code.

9. **Keep tests aligned with acceptance criteria**
   - Ensure automated tests explicitly cover:
     - clean migration
     - migration from mock finance schema
     - partially seeded company migration
     - startup fail-fast behavior in dev/test for pending migrations
   - For startup guard behavior, add either:
     - focused unit/integration tests around the guard helper, or
     - API host tests that assert startup failure under pending migrations in test environment

10. **Document assumptions in code comments only where necessary**
   - Keep comments concise.
   - If reconstructing legacy schema from archived docs or migration history, encode the rationale in test naming and helper naming rather than verbose comments.

11. **Implementation quality bar**
   - Follow existing project conventions.
   - Use async APIs.
   - Pass cancellation tokens where patterns already exist.
   - Keep new public surface area minimal.
   - Do not add dead code or speculative abstractions.

# Validation steps
Run and report the results of the relevant commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run targeted tests first if helpful, then full tests:
   - `dotnet test`

3. Specifically verify:
   - migration verification tests pass
   - startup guard tests pass
   - no existing tests regress

4. Manual code verification checklist:
   - In `Development` and `Test`, startup path checks pending migrations before serving requests
   - In non-dev/test environments, the new guard does not unexpectedly fail startup
   - Clean database migrates to latest successfully
   - Legacy/mock finance schema migrates successfully
   - Partially seeded company migration/bootstrap rerun is idempotent
   - If insight snapshot/cache tables exist, duplicate rows for same tenant + snapshot key are prevented

If any environment-specific integration tests require local PostgreSQL and cannot run in the current environment, still implement them cleanly and note exactly what is required.

# Risks and follow-ups
- **Legacy schema ambiguity:** “current mock finance schema” may not be directly encoded in migrations. If unclear, derive it from the latest pre-rollout migration or archived migration docs rather than guessing.
- **Test environment setup:** migration verification is only trustworthy against relational/PostgreSQL behavior; avoid provider shortcuts that hide migration issues.
- **Startup test fragility:** if host startup is hard to test directly, isolate the guard into a small service/extension and test that behavior explicitly.
- **Bootstrap idempotency gaps:** tests may expose existing duplicate-seeding issues in planning/approval backfills. Fix only what is necessary for this task and keep changes narrowly scoped.
- **Materialization/cache uncertainty:** only add uniqueness assertions and refresh-job coverage if those tables/jobs actually exist in the current branch or are required by adjacent finance insight work.
- **Follow-up suggestion:** if not already present, consider a CI step dedicated to migration verification against PostgreSQL so pending or broken migrations are caught before merge.