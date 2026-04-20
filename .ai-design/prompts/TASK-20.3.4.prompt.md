# Goal
Implement integration tests for **TASK-20.3.4 — Create integration tests for backfill reruns, skips, and failure recovery** for story **US-20.3 ST-FUI-411 — Background backfill job for retroactive seeding of existing companies**.

The coding agent should add or extend integration tests that verify the background backfill job behavior end-to-end enough to prove:

- companies in `not_seeded` and eligible `partially_seeded` states are scanned and selected
- batch processing honors configurable rate limiting and concurrency settings
- reruns are idempotent and do not duplicate or corrupt controlled seed datasets
- each company attempt records status, start/end timestamps, and error details when failures occur
- aggregate operator-visible counts are correct for scanned, queued, succeeded, skipped, and failed
- partial failures can be recovered safely on rerun

Prefer exercising the real application/infrastructure composition used by existing integration tests rather than isolated unit tests.

# Scope
In scope:

- Add integration tests in the existing test project(s), most likely under `tests/VirtualCompany.Api.Tests`
- Reuse existing test host, DI, database, worker, and persistence setup if present
- Seed test data for companies in different backfill states
- Execute the backfill job through the most production-like entry point available:
  - application service
  - background job handler
  - command handler
  - hosted worker orchestration entry point
- Assert persisted outcomes in the database and any exposed aggregate/reporting model already implemented
- Cover rerun safety, skip behavior, failure recording, and recovery on rerun
- Cover configurable batching/rate limiting/concurrency behavior to the extent observable and stable in integration tests

Out of scope unless required to make tests possible:

- redesigning the backfill architecture
- adding broad new production features not needed for testability
- changing unrelated modules
- introducing flaky timing-based tests that depend on wall-clock delays when deterministic hooks/configuration can be used instead

If production code must be adjusted for testability, keep changes minimal, targeted, and backward-compatible.

# Files to touch
Likely files to inspect and update:

- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
- integration test folders under `tests/VirtualCompany.Api.Tests/**`
- shared test infrastructure under `tests/VirtualCompany.Api.Tests/**` such as:
  - test web application factory
  - database fixture
  - service override helpers
  - seeded data builders
- production files only if necessary for testability, likely in:
  - `src/VirtualCompany.Application/**`
  - `src/VirtualCompany.Infrastructure/**`
  - `src/VirtualCompany.Api/**`

Also inspect for existing backfill-related code by searching for terms like:

- `backfill`
- `seeded`
- `not_seeded`
- `partially_seeded`
- `Seed`
- `BackgroundJob`
- `HostedService`
- `rate limit`
- `concurrency`
- `batch`

Do not invent file names prematurely; first discover the actual implementation and align tests to the existing structure and naming.

# Implementation plan
1. **Discover the existing backfill implementation**
   - Search the solution for the backfill job, related entities, statuses, and reporting models.
   - Identify:
     - the company seed/backfill state model
     - the job entry point
     - how attempts are recorded
     - where aggregate counts are stored or returned
     - how batching, rate limiting, and concurrency are configured
   - Identify the most integration-testable seam already used in the codebase.

2. **Inspect existing integration test patterns**
   - Review `tests/VirtualCompany.Api.Tests` for:
     - database-backed integration tests
     - background worker tests
     - command/query integration tests
     - service replacement patterns for deterministic behavior
   - Follow established conventions for fixtures, naming, and assertions.

3. **Design a deterministic test matrix**
   Create integration tests that cover at least these scenarios:

   - **Scan and eligibility**
     - companies in `not_seeded` are included
     - eligible `partially_seeded` companies are included
     - already completed/ineligible companies are skipped
     - aggregate scanned/queued/skipped counts reflect the scan result

   - **Rerun idempotency**
     - run the backfill once successfully
     - rerun the same job
     - assert no duplicate controlled seed records are created
     - assert already seeded companies are skipped or otherwise safely ignored
     - assert aggregate counts on rerun reflect safe behavior

   - **Partial failure and recovery**
     - configure one or more companies to fail during seeding using a deterministic test double/fault injection hook if available
     - assert failed attempt records include:
       - failed status
       - start time
       - end time
       - error details
     - assert unaffected companies still succeed
     - rerun after removing the injected failure
     - assert previously failed companies recover successfully without duplicating prior successful seed data

   - **Batching / rate limiting / concurrency**
     - configure small batch size and constrained concurrency
     - assert processing occurs in expected batch groupings or queue counts if observable
     - prefer asserting persisted metadata / invocation counts / max concurrent executions via deterministic instrumentation rather than elapsed time
     - if the implementation exposes options objects, override them in the test host with small values

4. **Add test helpers/builders as needed**
   - Add concise builders or fixtures for creating companies in specific seed states.
   - Add helpers to inspect:
     - seed datasets created per company
     - backfill attempt records
     - aggregate counts/report rows/results
   - If needed, add a test-only fake or decorator around the seeding executor to:
     - force failures for selected company IDs
     - track max observed concurrency
     - avoid brittle time-based assertions

5. **Keep production changes minimal**
   If current code is hard to test deterministically, make only small improvements such as:
   - exposing configuration through options already intended for DI
   - introducing an interface/decorator around the per-company seeding executor
   - ensuring attempt/error metadata is persisted in a queryable way
   - making aggregate result objects accessible from the job execution path

   Do not weaken production behavior just to satisfy tests.

6. **Implement the tests**
   Use clear names, for example:
   - `BackfillJob_scans_not_seeded_and_eligible_partially_seeded_companies`
   - `BackfillJob_rerun_is_idempotent_and_skips_already_seeded_companies`
   - `BackfillJob_records_failure_details_and_recovers_on_rerun`
   - `BackfillJob_honors_configured_batching_and_concurrency_limits`

   For each test:
   - arrange realistic company states
   - execute the job through the real application path
   - assert persisted state and aggregates
   - assert no duplicate seed records after rerun

7. **Assert acceptance-criteria-level outcomes**
   Ensure the final test suite explicitly proves:
   - scan eligibility logic
   - batch/rate-limit/concurrency behavior
   - idempotent rerun safety
   - per-company attempt audit fields
   - aggregate operator counts
   - partial failure handling and recovery

8. **Document any unavoidable gaps**
   - If true rate limiting cannot be asserted without flaky timing, document the deterministic proxy used, such as max concurrent executor invocations or batch partition counts.
   - If aggregate counts are not yet exposed by production code, note whether tests validate the underlying persisted counts instead.

# Validation steps
1. Restore/build/test locally:
   - `dotnet build`
   - `dotnet test`

2. Run the specific integration test subset if supported:
   - `dotnet test --filter Backfill`
   - or the relevant fully qualified test names

3. Verify tests are deterministic:
   - run the new tests multiple times
   - ensure no dependence on arbitrary sleeps or race-prone timing

4. Confirm assertions cover:
   - scanned/queued/skipped/succeeded/failed counts
   - attempt status and timestamps
   - error details on failure
   - no duplicate seed data after rerun
   - successful recovery after rerun

5. If production code was changed for testability:
   - verify no public behavior regressed
   - ensure DI registrations and default options still work in non-test environments

# Risks and follow-ups
- **Risk: flaky timing assertions**
  - Avoid asserting elapsed duration for rate limiting.
  - Prefer deterministic instrumentation, invocation tracking, or batch metadata assertions.

- **Risk: background workers may be hard to drive in tests**
  - Use the most direct real execution path available, such as a command/job handler resolved from DI, instead of relying on hosted-service startup timing.

- **Risk: hidden duplication rules in seed datasets**
  - Inspect the controlled seed dataset model carefully and assert on the actual persisted uniqueness boundaries.

- **Risk: aggregate counts may be computed indirectly**
  - If no direct operator-facing query exists yet, assert the persisted source-of-truth records and note the gap.

- **Risk: partial failure simulation may require seams**
  - If needed, add a minimal injectable executor/fault hook rather than mocking large parts of the system.

Follow-ups to note if discovered during implementation:
- add dedicated query/integration coverage for operator reporting endpoints if they exist separately
- add explicit observability assertions if the story also persists audit events for backfill runs
- add performance/load tests later for large-company scans; keep this task focused on correctness and recovery behavior