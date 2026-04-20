# Goal
Implement backlog task **TASK-20.3.1** for **US-20.3 ST-FUI-411** by adding a scheduled background backfill worker that scans all companies, identifies those needing finance seeding, and enqueues finance seed jobs in controlled batches.

The implementation must satisfy these outcomes:

- Scan companies in `not_seeded` and eligible `partially_seeded` states.
- Enqueue finance seed work in batches with configurable rate limiting and concurrency.
- Be safe to rerun without duplicating or corrupting controlled seed datasets.
- Persist per-company backfill attempt status, timing, and error details.
- Expose aggregate operational counts for scanned, queued, succeeded, skipped, and failed.
- Include automated tests for rerun safety, rate limiting, and partial failure handling.

Assume the existing solution follows a modular monolith structure with ASP.NET Core, application/domain/infrastructure layers, PostgreSQL, Redis, and background workers.

# Scope
In scope:

- Add or extend domain/application/infrastructure support for a **finance seeding backfill campaign**.
- Implement a **scheduled worker** that:
  - acquires a distributed lock,
  - scans companies in pages/batches,
  - determines eligibility,
  - records backfill attempts,
  - enqueues finance seed jobs.
- Add **configurable options** for:
  - batch size,
  - scan page size,
  - max queued per run,
  - concurrency,
  - rate limiting / delay between enqueue batches,
  - schedule enable/disable.
- Ensure **idempotent rerun behavior**:
  - do not enqueue duplicate active work for the same company/run window,
  - do not reseed already-complete controlled datasets,
  - safely skip ineligible companies.
- Add persistence for:
  - backfill run summary,
  - per-company backfill attempt records,
  - statuses, timestamps, and error details.
- Add a query/service/logging path for aggregate counts operators can review.
- Add tests covering acceptance criteria.

Out of scope unless required by existing patterns:

- New UI screens beyond minimal operator-facing query endpoints or internal service methods.
- Reworking the entire finance seeding pipeline.
- Introducing a new external message broker if the app already uses DB-backed or in-process background execution.
- Broad refactors unrelated to backfill scheduling and idempotent enqueueing.

# Files to touch
Inspect the repository first and update the exact files that match existing conventions. Likely areas:

- `src/VirtualCompany.Domain/**`
  - finance seeding state enums/value objects/entities
  - backfill run / backfill attempt entities if domain-owned
- `src/VirtualCompany.Application/**`
  - commands/queries/services for scanning and enqueueing
  - eligibility evaluator
  - aggregate reporting DTOs
  - options classes
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories
  - background worker implementation
  - distributed lock / Redis coordination integration
  - persistence for run and attempt records
- `src/VirtualCompany.Api/**`
  - DI registration
  - hosted service registration
  - configuration binding
  - optional operator endpoint if the project already exposes admin/ops endpoints
- `tests/VirtualCompany.Api.Tests/**`
  - integration or API tests
- potentially:
  - `src/VirtualCompany.Web/**` only if there is already an operator/admin page pattern and a minimal view is necessary
  - appsettings files for worker configuration

If migrations are used in-repo, add the appropriate migration in the project that owns persistence.

# Implementation plan
1. **Discover existing finance seeding model and worker patterns**
   - Find current finance seed job implementation, seed state storage, and any existing background scheduler abstractions.
   - Identify how company seed state is represented:
     - `not_seeded`
     - `partially_seeded`
     - `seeded`
     - any in-progress or failed variants
   - Reuse existing job queueing, outbox, or background execution patterns instead of inventing a parallel mechanism.
   - Find existing distributed lock and rate limiting patterns, especially Redis-backed ones.

2. **Define backfill domain model**
   Add a minimal, explicit model for operational tracking. Prefer names aligned with existing conventions. Include:
   - **Backfill run** record:
     - id
     - started_at
     - completed_at
     - status
     - scanned_count
     - queued_count
     - succeeded_count
     - skipped_count
     - failed_count
     - configuration snapshot / metadata JSON if useful
   - **Backfill attempt** record per company:
     - id
     - run_id
     - company_id
     - status
     - started_at
     - completed_at
     - error_details
     - skip_reason / eligibility_reason if useful
     - seed_state_before / after if useful
     - idempotency key or unique constraint fields if needed
   Suggested statuses:
   - run: `running`, `completed`, `completed_with_errors`, `failed`
   - attempt: `queued`, `skipped`, `in_progress`, `succeeded`, `failed`
   Keep the model simple and auditable.

3. **Add persistence and constraints**
   - Create EF entities/configurations and migration.
   - Add indexes for:
     - run lookup by created/start time
     - attempt lookup by run_id
     - attempt lookup by company_id
     - uniqueness to prevent duplicate active enqueue records where appropriate
   - If needed, add a uniqueness or guard mechanism such as:
     - one active backfill attempt per company for this backfill type while status in queued/in_progress
     - or one attempt per company per run
   - Preserve tenant/company isolation conventions.

4. **Implement eligibility evaluation**
   Create an application service such as `IFinanceSeedBackfillEligibilityService` that:
   - scans companies,
   - reads current finance seed state,
   - returns:
     - eligible `not_seeded`
     - eligible `partially_seeded`
     - skipped with reason for all others.
   Eligibility rules should be explicit and testable. For `partially_seeded`, only include companies that are safe to resume/reseed according to existing finance seeding semantics. Do not guess; inspect current seed logic and encode the same safety rules.

5. **Implement idempotent enqueue orchestration**
   Create a service such as `FinanceSeedBackfillOrchestrator` that:
   - starts a run record,
   - scans companies in pages,
   - evaluates eligibility,
   - records scanned count,
   - for each eligible company:
     - checks whether a seed job is already active or already completed safely,
     - creates/updates a backfill attempt,
     - enqueues the finance seed job with an idempotency key,
     - marks attempt as `queued`,
     - increments queued count.
   For ineligible companies:
   - create/update attempt as `skipped` with reason,
   - increment skipped count.
   For enqueue failures:
   - capture exception/error details on the attempt,
   - increment failed count,
   - continue processing remaining companies.
   The run must tolerate partial failures and complete with aggregate counts.

6. **Add batching, concurrency, and rate limiting**
   Implement configurable controls via options, for example:
   - `Enabled`
   - `Cron` or interval-based schedule depending on existing scheduler pattern
   - `ScanPageSize`
   - `EnqueueBatchSize`
   - `MaxCompaniesPerRun`
   - `MaxConcurrentEnqueues`
   - `DelayBetweenBatchesMs`
   - `DistributedLockTtl`
   - `EligiblePartialSeedStates` if configuration-driven
   Behavior:
   - scan in pages,
   - process enqueue work in batches,
   - cap concurrent enqueue operations,
   - wait configured delay between batches,
   - stop when `MaxCompaniesPerRun` is reached.
   Prefer deterministic implementation that is easy to test, e.g. inject a clock/delay abstraction if the codebase already supports it.

7. **Implement scheduled worker**
   Add a hosted/background worker that:
   - runs on schedule,
   - acquires a distributed lock so only one instance performs the scan at a time,
   - invokes the orchestrator,
   - logs run summary with correlation/run id,
   - exits cleanly if disabled or lock not acquired.
   Reuse existing scheduler infrastructure if present. If there is already a generic scheduled job framework, plug into it rather than creating a bespoke loop.

8. **Integrate with existing finance seed job**
   - Ensure the actual finance seed job accepts or propagates:
     - company id
     - correlation/run id
     - idempotency key
     - backfill attempt id if useful
   - On completion/failure of the seed job, update the corresponding backfill attempt:
     - `succeeded` with end time
     - `failed` with end time and error details
   - Update aggregate run counters either:
     - transactionally during completion handling, or
     - by recomputing from attempts when queried/finalized.
   Prefer correctness over premature optimization.

9. **Expose aggregate review capability**
   Add a query/service and, if consistent with the codebase, an API/admin endpoint to retrieve:
   - latest runs
   - per-run counts:
     - scanned
     - queued
     - succeeded
     - skipped
     - failed
   - optional attempt drill-down for troubleshooting
   If no endpoint pattern exists, at minimum implement an application query service used by tests and logs.

10. **Logging and observability**
   Add structured logs with:
   - run id
   - company id
   - batch number
   - counts
   - skip reasons
   - error details
   Keep business tracking in persistence tables and technical diagnostics in logs.

11. **Testing**
   Add automated tests that verify:
   - **rerun safety**
     - rerunning the backfill does not duplicate controlled seed datasets
     - already-seeded or already-queued companies are skipped safely
   - **rate limiting / batching**
     - configured batch size and delay behavior are honored
     - concurrency cap is respected
   - **partial failure handling**
     - one company failing does not abort the whole run
     - failed attempts record error details
     - aggregate counts remain correct
   - **eligibility**
     - `not_seeded` included
     - only eligible `partially_seeded` included
     - ineligible states skipped with reason
   - **distributed lock behavior** if testable in current setup
   Prefer integration tests around the application/infrastructure boundary over overly mocked unit tests.

12. **Definition of done checks**
   Before finishing:
   - migration applies cleanly
   - worker is registered and configuration-bound
   - no duplicate enqueue path exists
   - counts reconcile with attempt records
   - `dotnet build` and `dotnet test` pass

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the full test suite:
   - `dotnet test`

3. If migrations are part of the normal workflow:
   - generate/apply the migration for new backfill tables and indexes
   - verify the app starts successfully

4. Manually validate the backfill flow in a test environment:
   - seed companies across states:
     - `not_seeded`
     - eligible `partially_seeded`
     - ineligible `partially_seeded`
     - fully seeded
   - trigger the scheduled worker or orchestrator manually
   - verify:
     - only eligible companies are queued
     - attempts are recorded with timestamps/status
     - aggregate counts are correct

5. Validate rerun safety:
   - run the backfill twice against the same dataset
   - confirm no duplicate controlled seed data is created
   - confirm already processed companies are skipped or deduplicated correctly

6. Validate partial failure handling:
   - force one company’s finance seed job to fail
   - confirm:
     - that attempt is marked failed with error details
     - other companies continue processing
     - run summary reflects failed and succeeded counts correctly

7. Validate rate limiting and concurrency:
   - configure small batch size and low concurrency
   - confirm enqueue behavior follows configured caps
   - confirm delay between batches is observable/testable

# Risks and follow-ups
- **Unknown existing finance seed semantics**
  - Risk: `partially_seeded` eligibility may be implemented incorrectly if current seed invariants are not reused.
  - Follow-up: align backfill eligibility strictly with existing finance seeding rules and controlled dataset protections.

- **Duplicate work across scheduler instances**
  - Risk: multiple app instances may run the same scan.
  - Mitigation: use distributed locking and idempotency keys.
  - Follow-up: add stronger DB uniqueness if current queueing is weak.

- **Aggregate count drift**
  - Risk: run summary counters can become inconsistent if updated in multiple places.
  - Mitigation: prefer deriving counts from attempt records or centralize updates.
  - Follow-up: add reconciliation logic if needed.

- **Long-running scans on large company counts**
  - Risk: full-table scans may become expensive.
  - Mitigation: page through companies with indexed predicates and cap per-run volume.
  - Follow-up: consider incremental cursor-based scanning or partitioned scheduling if scale grows.

- **Testing timing-sensitive rate limiting**
  - Risk: flaky tests if real delays are used.
  - Mitigation: use injectable clock/delay abstractions where possible.
  - Follow-up: refactor worker timing behind interfaces if not already present.

- **Operator visibility**
  - Risk: acceptance requires reviewable aggregate counts, but no admin surface may exist yet.
  - Follow-up: if no UI is currently appropriate, expose an application query/API endpoint now and add a lightweight operator page later.