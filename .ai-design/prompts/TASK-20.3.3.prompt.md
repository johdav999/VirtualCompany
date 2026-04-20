# Goal
Implement backlog task **TASK-20.3.3 — Persist per-company backfill execution records and aggregate job metrics** for story **US-20.3 ST-FUI-411 — Background backfill job for retroactive seeding of existing companies**.

The coding agent should add a production-ready background backfill capability that:

- scans all companies for seed eligibility
- targets companies in `not_seeded` and eligible `partially_seeded` states
- processes companies in batches
- supports configurable rate limiting and concurrency
- is safe and idempotent on reruns
- persists a per-company execution record for every attempt
- exposes aggregate metrics for operators:
  - scanned
  - queued
  - succeeded
  - skipped
  - failed
- includes automated tests for rerun safety, rate limiting behavior, and partial failure handling

Follow existing solution conventions and keep changes aligned with the modular monolith / clean architecture style.

# Scope
In scope:

- Add or extend domain/application/infrastructure support for a **backfill job run** and **per-company backfill execution records**
- Persist, at minimum, for each company attempt:
  - company identifier
  - status
  - start time
  - end time
  - error details when applicable
- Add aggregate metrics for a job run:
  - scanned
  - queued
  - succeeded
  - skipped
  - failed
- Implement batch processing with configuration-driven:
  - batch size
  - concurrency
  - rate limiting / pacing
- Ensure rerun safety and idempotent behavior for controlled seed datasets
- Add tests covering:
  - rerun safety
  - rate limiting behavior
  - partial failure handling

Out of scope unless required by existing code patterns:

- full operator UI
- mobile changes
- unrelated refactors
- introducing new infrastructure beyond what the solution already uses
- broad redesign of existing seeding architecture

If there is already a seeding/backfill implementation, extend it rather than replacing it.

# Files to touch
Start by locating the existing seed/backfill implementation and then update the most relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - add entities/value objects/enums for backfill job runs and per-company execution records if not already present
- `src/VirtualCompany.Application/**`
  - commands/services/handlers for starting and executing the backfill
  - query/service for aggregate metrics
  - configuration options for batch size, concurrency, and rate limiting
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories
  - background worker/job implementation
  - migration support for new tables/columns
  - any locking / coordination / scheduling integration already used
- `src/VirtualCompany.Api/**`
  - only if there is an operator/admin endpoint or hosted service registration to wire up
- `tests/VirtualCompany.Api.Tests/**`
  - integration and/or application-level tests for acceptance criteria

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to follow repository migration and execution conventions.

# Implementation plan
1. **Discover existing implementation**
   - Search for:
     - seeding services
     - company seed state
     - background jobs / hosted services
     - existing job execution or audit tables
     - any `not_seeded` / `partially_seeded` state handling
   - Reuse existing abstractions and naming where possible.

2. **Model backfill run and per-company execution persistence**
   - Add a durable model for a backfill job run, likely something like:
     - `BackfillJobRun`
       - `Id`
       - `StartedAt`
       - `CompletedAt`
       - `Status`
       - aggregate counters
       - configuration snapshot if useful
     - `CompanyBackfillExecution`
       - `Id`
       - `BackfillJobRunId`
       - `CompanyId`
       - `SeedStateBefore`
       - `Status` (`queued`, `started`, `succeeded`, `skipped`, `failed`)
       - `StartedAt`
       - `CompletedAt`
       - `ErrorCode` / `ErrorMessage` / structured error details
   - If the codebase already has a generic job execution model, extend it instead of duplicating concepts.

3. **Add database persistence**
   - Create/update PostgreSQL schema and mappings for:
     - backfill job runs
     - per-company execution records
   - Add indexes appropriate for:
     - querying by run id
     - querying by company id
     - querying by status
   - Ensure migration files follow repo conventions.

4. **Implement company scanning and eligibility**
   - Add a scanner/service that enumerates companies and identifies those in:
     - `not_seeded`
     - eligible `partially_seeded`
   - Make eligibility rules explicit and testable.
   - Increment `scanned` for every company evaluated.
   - Increment `queued` only for companies selected for processing.
   - Record `skipped` for ineligible companies or companies already safely complete.

5. **Implement batch processing with configuration**
   - Add strongly typed options, e.g.:
     - batch size
     - max concurrency
     - delay / rate limit interval
     - optional max companies per run
   - Process queued companies in batches.
   - Respect concurrency limits.
   - Respect pacing/rate limiting between batches or dispatches.
   - Keep implementation deterministic enough to test.

6. **Ensure idempotent rerun-safe seeding**
   - Reuse existing seed-state checks and controlled dataset protections.
   - Before seeding a company, re-check current state to avoid stale queue decisions.
   - Prevent duplicate controlled seed data creation on rerun.
   - Treat already-complete or no-op cases as `skipped` or safe success according to existing semantics, but keep metrics consistent.
   - If there is an existing idempotency key or seed version mechanism, integrate with it.

7. **Persist per-company attempt lifecycle**
   - For each selected company:
     - create execution record when queued or started
     - set `StartedAt` when processing begins
     - set `CompletedAt` when processing ends
     - persist final status
     - persist error details on failure
   - Failures for one company must not abort the entire run unless existing policy explicitly requires it.
   - Aggregate counters should reflect final outcomes.

8. **Add aggregate metrics access**
   - Provide a query/service that returns run-level metrics:
     - scanned
     - queued
     - succeeded
     - skipped
     - failed
   - Prefer deriving from persisted records or maintaining counters transactionally in a reliable way.
   - If there is an admin/operator endpoint pattern already present, expose metrics through it; otherwise keep it application-service accessible and testable.

9. **Wire background execution**
   - Register the job/worker using existing background processing patterns.
   - If scheduling already exists, integrate there.
   - If distributed coordination/locking exists, use it to avoid overlapping runs.
   - Keep tenant/company processing tenant-safe.

10. **Add automated tests**
   - Add tests for:
     - **rerun safety**
       - running the backfill twice does not duplicate controlled seed datasets
       - already seeded companies are skipped or safely no-op
     - **rate limiting behavior**
       - configured pacing/concurrency is honored
       - use fake clock/test doubles if available
     - **partial failure handling**
       - one company failure does not prevent others from processing
       - failed company records include error details
       - aggregate metrics reflect mixed outcomes correctly
   - Prefer integration tests where persistence behavior matters.

11. **Keep observability aligned**
   - Add structured logs around:
     - run start/end
     - batch start/end
     - company attempt start/end/failure
   - Do not replace business persistence with logs; logs are supplemental.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migrations are included and consistent with repo conventions.

4. Manually validate behavior through tests or a local execution path:
   - create companies covering:
     - `not_seeded`
     - eligible `partially_seeded`
     - already seeded / ineligible
   - run the backfill job
   - confirm persisted records exist for each attempted company
   - confirm each record has:
     - status
     - start time
     - end time
     - error details when failed
   - confirm aggregate metrics match expected counts

5. Validate rerun safety:
   - run the same backfill again
   - confirm no duplicate controlled seed data is created
   - confirm metrics and statuses remain sensible

6. Validate partial failure:
   - force one company to fail during seeding
   - confirm:
     - other companies continue
     - failed record is persisted with error details
     - aggregate counts reflect the failure

7. Validate rate limiting/concurrency:
   - run with small batch/concurrency settings
   - confirm execution behavior follows configured limits
   - confirm tests do not rely on flaky timing where avoidable

# Risks and follow-ups
- **Unknown existing seed architecture**
  - Risk: duplicating concepts already present.
  - Mitigation: inspect current seeding/backfill code first and extend it.

- **Ambiguous `partially_seeded` eligibility**
  - Risk: incorrect companies are retried or skipped.
  - Mitigation: centralize eligibility rules and cover them with tests.

- **Counter drift**
  - Risk: aggregate metrics become inconsistent if maintained separately from execution records.
  - Mitigation: prefer deriving metrics from persisted records or updating counters transactionally.

- **Concurrency overlap**
  - Risk: overlapping job runs may process the same company twice.
  - Mitigation: use existing distributed lock / single-run coordination if available, and re-check company state at execution time.

- **Timing-based test flakiness**
  - Risk: rate limiting tests become unstable.
  - Mitigation: use fake time abstractions or observable dispatch sequencing where possible.

- **Migration mismatch**
  - Risk: schema changes do not follow repository conventions.
  - Mitigation: inspect migration guidance in `docs/postgresql-migrations-archive/README.md` before generating changes.

Potential follow-ups after this task:
- operator/admin API or UI for browsing backfill runs and company attempt history
- richer error categorization and retry policies
- dashboard surfacing of backfill metrics and exceptions