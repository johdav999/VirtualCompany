# Goal
Implement `TASK-20.2.1` for **US-20.2 ST-FUI-410** by adding finance-entry route/controller logic that detects companies in `not_seeded` finance state and enqueues a **non-blocking asynchronous finance seed job** on first `/finance` access. Ensure duplicate jobs are not enqueued while one is already running, expose enough state for the finance UI to render initializing/error/retry behavior, and emit telemetry for request/start/complete/fail lifecycle events originating from finance entry.

# Scope
In scope:
- Add backend application/API logic invoked by finance page entry to:
  - inspect finance seed state for the current company
  - enqueue async seeding when state is `not_seeded`
  - avoid duplicate enqueue when a seed job is already queued/running
  - return a finance initialization/status payload suitable for UI rendering
- Add or extend background job orchestration for finance seeding lifecycle
- Add telemetry/audit hooks for:
  - seed job requested
  - seed job started
  - seed job completed
  - seed job failed
- Update finance web route/page flow so `/finance`:
  - does not block on seeding
  - shows initializing state while seeding is in progress
  - refreshes automatically or offers a clear reload action after completion
  - shows recoverable error state with retry guidance on failure
- Add tests for idempotent enqueue behavior and UI-facing state transitions

Out of scope unless required by existing architecture:
- Reworking the full finance domain seed implementation itself if a seed worker/service already exists
- Broad redesign of job infrastructure
- Mobile-specific behavior
- New unrelated finance dashboard features

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance route/page/component for `/finance`
  - any page model, SSR loader, or Blazor component backing logic
- `src/VirtualCompany.Api/**`
  - finance controller/endpoint or company-scoped finance status endpoint
- `src/VirtualCompany.Application/**`
  - finance entry command/query handlers
  - seed orchestration service interfaces and handlers
  - telemetry/event recording abstractions
- `src/VirtualCompany.Domain/**`
  - finance seed state enum/value object if missing
  - company finance initialization state model
- `src/VirtualCompany.Infrastructure/**`
  - background job enqueue implementation
  - persistence/repository updates
  - telemetry/audit event persistence
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/controller tests
- Potentially:
  - `tests/**Application**`
  - `tests/**Web**`
  - migration files if persistence shape must change

Before editing, locate:
- existing finance page/route
- existing company state model for finance setup/seed status
- any background job abstraction, outbox, worker, or scheduler
- telemetry/audit event patterns already used elsewhere

# Implementation plan
1. **Discover current finance entry flow**
   - Find how `/finance` is served in the Blazor web app.
   - Identify whether finance data is loaded via:
     - direct server-side component injection
     - API call from the page
     - page loader/query service
   - Reuse the existing pattern rather than inventing a new one.

2. **Identify or introduce a finance seed state contract**
   - Confirm whether the company already has a finance seed status such as:
     - `not_seeded`
     - `seeding`
     - `seeded`
     - `failed`
   - If missing, add a minimal explicit state model in domain/application.
   - Include enough metadata for UX:
     - current state
     - last error summary/code if failed
     - active job indicator or timestamp
     - completion timestamp if available
     - whether retry is allowed

3. **Add finance entry orchestration use case**
   - Create or extend an application service/handler such as `GetFinanceEntryState` or `EnsureFinanceSeedStartedOnEntry`.
   - Behavior:
     - resolve current company from tenant context
     - read finance seed state
     - if `not_seeded`, attempt idempotent enqueue of seed job
     - if already `seeding`, do not enqueue another job
     - if `failed`, return failed state and allow retry action path
     - if `seeded`, continue normal finance load
   - Keep this logic out of UI/controller code beyond orchestration call.

4. **Implement idempotent enqueue semantics**
   - Use the existing job coordination mechanism if present.
   - Ensure repeated `/finance` navigation does not create duplicate jobs for the same company.
   - Preferred approaches in order:
     1. existing unique job key/idempotency support
     2. persisted company/job state transition guarded transactionally
     3. distributed lock plus persisted state check
   - The enqueue path should atomically move `not_seeded -> seeding_requested` or equivalent before/with enqueue.
   - If a job is already queued/running, return existing in-progress state.

5. **Wire background worker lifecycle updates**
   - On worker start:
     - mark state as `seeding`
     - emit telemetry `requested` already happened at entry, now emit `started`
   - On success:
     - mark state as `seeded`
     - persist any seeded-at timestamp
     - emit `completed`
   - On failure:
     - mark state as `failed`
     - persist recoverable error summary
     - emit `failed`
   - Ensure retries can be initiated intentionally from UI if acceptance criteria require retry guidance.

6. **Expose a finance status/read model for the UI**
   - Add or extend endpoint/query response consumed by `/finance`.
   - Response should let the UI distinguish:
     - ready with seeded data
     - initializing/in progress
     - failed/retryable
   - Avoid returning a broken/empty finance payload when unseeded.

7. **Update `/finance` UI behavior**
   - When state is initializing:
     - render a clear initializing/loading panel, not a blank page
     - explain that finance data is being prepared
   - After completion:
     - either auto-refresh/poll status until `seeded`
     - or provide a clear reload CTA if polling is not already a pattern in the app
   - On failure:
     - render recoverable error state with retry guidance
     - if a retry action is implemented, route it through the same idempotent enqueue path
   - Prefer the simplest UX consistent with existing app patterns.

8. **Add telemetry/audit instrumentation**
   - Follow existing observability/business event conventions.
   - Record finance-entry-originated events with company context and correlation where possible:
     - `finance_seed_job_requested`
     - `finance_seed_job_started`
     - `finance_seed_job_completed`
     - `finance_seed_job_failed`
   - Include source/entrypoint metadata like `source = finance_entry`.
   - If the codebase distinguishes technical logs from business audit events, use the correct channel for each.

9. **Testing**
   - Add application/API tests covering:
     - first `/finance` access for `not_seeded` enqueues exactly one job and returns initializing state
     - repeated access while job is running does not enqueue duplicates
     - failed state returns recoverable error payload
     - seeded state returns normal finance-ready payload without enqueue
   - Add worker/orchestration tests for lifecycle state transitions and telemetry emission.
   - Add UI/component tests if the project already uses them; otherwise keep UI validation focused on integration-level behavior.

10. **Keep implementation aligned with architecture**
   - Respect tenant scoping on all reads/writes/jobs.
   - Keep controller/page thin and application layer responsible for orchestration.
   - Use background workers and reliable side-effect patterns already present in the solution.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate the happy path:
   - Create/use a company with finance state `not_seeded`
   - Navigate to `/finance`
   - Confirm request returns promptly and UI shows initializing state
   - Confirm exactly one seed job is queued/started
   - Confirm telemetry contains `requested` then `started`
   - After worker completion, confirm UI refreshes or reload action loads seeded data
   - Confirm telemetry contains `completed`

4. Validate duplicate protection:
   - While seeding is in progress, hit `/finance` multiple times or refresh repeatedly
   - Confirm no duplicate jobs are enqueued for the same company

5. Validate failure path:
   - Force seed worker failure in a test/dev scenario
   - Navigate to `/finance`
   - Confirm UI shows recoverable error state with retry guidance
   - Confirm telemetry contains `failed`
   - If retry is implemented, trigger retry and confirm idempotent re-enqueue behavior

6. Validate tenant safety:
   - Confirm all finance seed state reads/writes are company-scoped
   - Confirm no cross-company job/status leakage

# Risks and follow-ups
- **Unknown existing finance seed model**: The repo may already have partial seeding concepts under different names. Reuse them instead of creating parallel state.
- **Job idempotency gaps**: If current background infrastructure lacks unique job semantics, transactional state guarding may be required to prevent duplicates.
- **UI refresh strategy**: Auto-refresh may require polling or SignalR-like updates. If no existing real-time pattern exists, a clear reload CTA is acceptable per acceptance criteria.
- **Telemetry split**: The codebase may separate logs, metrics, and business audit events. Ensure required lifecycle events are recorded in the intended system, not only debug logs.
- **Migration need**: If finance seed status/error fields do not exist, add the smallest safe schema change and corresponding tests.
- **Follow-up candidate**: If this task only adds web behavior, consider a later task for mobile finance-entry parity if `/finance` concepts surface there.