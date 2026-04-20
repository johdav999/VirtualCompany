# Goal
Implement backlog task **TASK-20.3.2 — Add rate limiting, concurrency caps, and retry policy for retroactive seeding jobs** for story **US-20.3 ST-FUI-411 — Background backfill job for retroactive seeding of existing companies**.

Deliver a production-ready background backfill capability in the .NET solution that:

- scans all companies for retroactive seeding eligibility
- targets companies in `not_seeded` and eligible `partially_seeded` states
- processes seeding in batches
- enforces configurable rate limiting and concurrency caps
- applies a clear retry policy for transient failures
- remains idempotent and safe to rerun
- records per-company attempt lifecycle details
- exposes aggregate counts for operators
- includes automated tests for rerun safety, rate limiting/concurrency behavior, and partial failure handling

Use the existing architecture conventions:
- modular monolith
- ASP.NET Core + background workers
- PostgreSQL as source of truth
- Redis only if already used for coordination; prefer simplest fit to current codebase
- CQRS-lite and clean boundaries
- tenant-safe processing
- structured operational logging separate from business state

# Scope
In scope:

- Add or extend domain/application/infrastructure support for a **retroactive seeding backfill job**
- Add persistence for:
  - backfill job run metadata
  - per-company backfill attempt records
  - statuses, timestamps, error details, retry counts
  - aggregate counters or queryable summaries
- Implement a scanner that identifies:
  - `not_seeded`
  - eligible `partially_seeded`
- Implement batch execution with configuration-driven:
  - batch size
  - max concurrency
  - rate limit / pacing
  - retry count / retry delay / backoff
- Ensure idempotent seeding behavior:
  - no duplicate controlled seed datasets
  - safe reruns after partial completion or failure
- Add operator-facing query/service support for aggregate counts:
  - scanned
  - queued
  - succeeded
  - skipped
  - failed
- Add automated tests

Out of scope unless required by existing patterns:

- full UI implementation beyond minimal operator visibility hooks already expected in backend
- introducing a new external job framework if the solution already has a background execution pattern
- broad refactors unrelated to retroactive seeding
- changing seed dataset semantics beyond what is required for idempotency and rerun safety

# Files to touch
Inspect the solution first and then touch the minimum coherent set. Expected areas:

- `src/VirtualCompany.Domain/**`
  - entities/value objects/enums for backfill job runs and company attempt records
  - seeding status rules and idempotency semantics if domain-owned
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for:
    - scanning eligible companies
    - queuing/processing batches
    - retry policy decisions
    - aggregate status queries
  - options/config models for rate limiting and concurrency
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - background worker/job runner implementation
  - time provider / delay abstractions if needed for testability
  - migration(s)
- `src/VirtualCompany.Api/**`
  - DI registration
  - configuration binding
  - optional admin/operator endpoints if backend exposure belongs here
- `tests/VirtualCompany.Api.Tests/**`
  - integration and/or API tests
- potentially:
  - `README.md`
  - appsettings files
  - migration archive/docs if this repo tracks migrations there

If the codebase already has seeding-related types, prefer extending them rather than creating parallel concepts. Reuse existing naming and module boundaries.

# Implementation plan
1. **Discover existing seeding and background job patterns**
   - Find current company seeding model:
     - where company seed state is stored
     - what `not_seeded` / `partially_seeded` / fully seeded states look like
     - how controlled seed datasets are identified
   - Find existing worker/scheduler/retry patterns
   - Find existing operational status or audit patterns
   - Do not invent a second job orchestration style if one already exists

2. **Define the backfill domain model**
   Add a minimal but explicit model for tracking runs and attempts. Prefer relational persistence.

   Suggested concepts:
   - `RetroactiveSeedBackfillRun`
     - `Id`
     - `StartedAt`
     - `CompletedAt`
     - `Status`
     - config snapshot fields: batch size, max concurrency, rate limit, retry policy
     - aggregate counters: scanned, queued, succeeded, skipped, failed
   - `RetroactiveSeedBackfillAttempt`
     - `Id`
     - `RunId`
     - `CompanyId`
     - `Status` (`queued`, `in_progress`, `succeeded`, `skipped`, `failed`)
     - `StartedAt`
     - `EndedAt`
     - `RetryCount`
     - `ErrorCode`/`ErrorMessage`/`ErrorDetails`
     - optional idempotency marker / seed version snapshot

   If the existing schema already has a generic job execution table, integrate there instead of duplicating.

3. **Add eligibility scanning**
   Implement a scanner service that:
   - scans all companies tenant-safely
   - identifies companies in:
     - `not_seeded`
     - eligible `partially_seeded`
   - excludes companies already fully seeded or otherwise ineligible
   - applies deterministic eligibility rules for partial seeding
   - records `scanned` count and creates queued attempt records

   Important:
   - eligibility for `partially_seeded` must be explicit and testable
   - scanner must be rerunnable without creating duplicate queued work for the same run/company combination
   - if rerunning as a new run, idempotency must still prevent duplicate seed data creation

4. **Implement idempotent seeding execution**
   Wrap the actual company seeding operation in an idempotent application service:
   - detect already-applied controlled seed datasets
   - only create missing seed artifacts
   - avoid duplicate inserts on rerun
   - use transactions where appropriate
   - prefer natural keys / unique constraints / upsert semantics where needed

   If controlled seed datasets already have versioning, use that version as the idempotency boundary.

5. **Implement batching, concurrency caps, and rate limiting**
   Build the backfill processor to:
   - pull queued attempts in batches
   - process with configurable `MaxConcurrency`
   - enforce configurable rate limiting, such as:
     - max starts per interval, or
     - minimum delay between dispatches
   - remain testable by abstracting time/delay behavior

   Preferred implementation characteristics:
   - use `IOptions` for settings
   - use `SemaphoreSlim` or existing coordination abstraction for concurrency
   - use a simple token-bucket/leaky-bucket/paced dispatcher only if needed; otherwise a deterministic interval-based limiter is acceptable
   - avoid overengineering if acceptance criteria can be met with a simpler pacing mechanism

6. **Add retry policy**
   Implement retry behavior for transient failures only:
   - configurable max retry attempts
   - configurable base delay and optional backoff
   - classify failures into:
     - transient/infrastructure => retry
     - permanent/business/policy/idempotency-safe skip => no retry
   - record each attempt outcome and final status

   Requirements:
   - retries must not corrupt or duplicate seed data
   - final failure must preserve error details
   - partial failures across companies must not abort the entire run unless current architecture requires it

7. **Record per-company attempt lifecycle**
   Ensure each company backfill attempt records:
   - status
   - start time
   - end time
   - error details when applicable

   Also ensure aggregate counts can be derived or updated consistently:
   - scanned
   - queued
   - succeeded
   - skipped
   - failed

   Prefer deriving counts from persisted attempt records unless the codebase already uses denormalized counters with safe updates.

8. **Expose operator review capability**
   Add backend query support for operators to review:
   - run summary
   - aggregate counts
   - optionally recent failed companies with error details

   If the project already has admin endpoints or query handlers, add to that pattern.
   If not, at minimum provide application query services and tests proving the counts are available.

9. **Configuration**
   Add strongly typed options, e.g.:
   - `RetroactiveSeedingBackfillOptions`
     - `Enabled`
     - `BatchSize`
     - `MaxConcurrency`
     - `RateLimitCount`
     - `RateLimitWindow`
     - `MaxRetries`
     - `BaseRetryDelay`
     - `RetryBackoffMultiplier`

   Bind from configuration and validate on startup if the project already uses options validation.

10. **Persistence and migrations**
   Add EF Core mappings and migration(s) for any new tables/columns/indexes.
   Consider indexes on:
   - run status
   - attempt `(run_id, company_id)` unique
   - attempt status
   - company seed status
   - timestamps for operator review

   Add unique constraints needed to enforce idempotency where appropriate.

11. **Logging and observability**
   Add structured logs with:
   - run id
   - company id
   - retry count
   - batch number if applicable
   - outcome classification

   Keep technical logs separate from business state records.

12. **Tests**
   Add automated tests covering at least:
   - rerun safety:
     - rerunning the same logical backfill does not duplicate controlled seed datasets
     - already seeded companies are skipped safely
   - rate limiting / concurrency:
     - processing does not exceed configured concurrency
     - pacing/rate limit behavior is enforced in a deterministic test
   - partial failure handling:
     - one company failure does not prevent others from succeeding
     - transient failures retry up to configured limit
     - permanent failures are recorded and not retried indefinitely
   - aggregate counts:
     - scanned/queued/succeeded/skipped/failed are correct after mixed outcomes

   Prefer deterministic tests using fake time/delay abstractions rather than real sleeps.

13. **Keep implementation aligned with story intent**
   This is a **background backfill job for retroactive seeding of existing companies**, not a generic workflow engine rewrite. Keep the implementation focused, composable, and easy to operate.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the relevant test suite:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal pattern.

4. Manually validate logic through tests or a local execution path:
   - create companies in `not_seeded`
   - create companies in eligible `partially_seeded`
   - create companies already fully seeded
   - run backfill
   - confirm:
     - only eligible companies are queued
     - batches respect configured concurrency
     - rate limiting is applied
     - retries occur only for transient failures
     - per-company attempt records include status/start/end/error
     - aggregate counts are correct

5. Rerun the backfill and confirm:
   - no duplicate controlled seed datasets are created
   - already completed companies are skipped or treated idempotently
   - counts remain consistent with rerun semantics

6. Validate partial failure behavior:
   - force one transient failure and one permanent failure
   - confirm successful companies still complete
   - confirm failed companies retain error details
   - confirm retry counts and final statuses are correct

7. If API/admin endpoints are added, verify response payloads expose aggregate counts and relevant run metadata.

# Risks and follow-ups
- **Existing seeding model ambiguity**: the biggest risk is misunderstanding current seed-state semantics. Resolve this before coding.
- **Idempotency gaps in current seed writers**: if existing seed insertion paths are not idempotent, this task may require tightening unique constraints or upsert behavior.
- **Overlapping runs**: decide whether concurrent backfill runs are allowed. If not already handled, add a simple lock/guard to prevent overlapping execution.
- **Rate limiting semantics**: keep implementation simple and testable. Do not introduce a complex distributed limiter unless the codebase already needs cross-node coordination.
- **Retry classification**: be explicit about transient vs permanent failures to avoid noisy retries.
- **Operator visibility**: if no admin surface exists yet, backend query support may be sufficient now, with UI follow-up later.
- **Distributed deployment**: if workers can run on multiple instances, consider whether Redis/distributed locking is needed for run coordination in a follow-up if not already present.
- **Follow-up candidates**:
  - admin UI for backfill run history
  - cancellation support for active runs
  - dead-letter handling for repeatedly failing companies
  - metrics export for run duration, throughput, and failure rates