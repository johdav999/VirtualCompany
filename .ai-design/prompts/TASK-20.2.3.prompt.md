# Goal

Implement backlog task **TASK-20.2.3 — Add duplicate-job suppression and company-level seeding lock for finance access flow** for story **US-20.2 ST-FUI-410 — Auto-seed finance data on first finance access for existing companies**.

Deliver a production-ready change in the existing .NET solution so that opening **`/finance`** for a company in **`not_seeded`** state:

- triggers an **asynchronous** finance seed job without blocking the page request,
- shows an **initializing** UI state while seeding is running,
- avoids **duplicate job enqueueing** for the same company while a seed is already in progress,
- uses a **company-level lock/idempotency mechanism** so concurrent requests cannot start multiple seeds,
- surfaces a **recoverable error state** if seeding fails,
- records telemetry for **requested**, **started**, **completed**, and **failed** finance-entry seed events.

Preserve tenant isolation and align with the modular monolith architecture, CQRS-lite patterns, background worker model, and Redis/job coordination guidance where already available in the codebase.

# Scope

In scope:

- Identify the current finance access flow for the `/finance` route in web + API/application layers.
- Add or extend a finance seeding state model sufficient to represent:
  - `not_seeded`
  - `seeding`
  - `seeded`
  - `failed`
- Add a **non-blocking trigger path** from finance entry that requests seeding asynchronously.
- Add **duplicate suppression** so repeated navigation while seeding is active does not enqueue another job.
- Add a **company-level lock** or equivalent atomic guard around seed job creation/start.
- Ensure the UI renders:
  - initializing/in-progress state,
  - success transition with refresh/reload behavior,
  - recoverable failure state with retry guidance/action.
- Add telemetry/audit-style operational events for finance-entry seeding lifecycle.
- Add/update automated tests covering concurrency/idempotency and UI/API behavior.

Out of scope unless required by existing implementation shape:

- Reworking the entire finance domain model.
- Introducing a new external message broker.
- Broad redesign of background job infrastructure.
- Mobile-specific finance UX unless the same shared contracts require minor updates.
- Large observability platform changes beyond the required telemetry hooks.

# Files to touch

Inspect the solution first and then update the actual files that own this flow. Likely areas include:

- `src/VirtualCompany.Web/**`
  - finance page/component for `/finance`
  - any page model/view model/query client used to load finance state
  - UI components for loading/error/retry/reload behavior
- `src/VirtualCompany.Api/**`
  - finance controller/endpoints if finance entry is API-driven
  - telemetry/logging hooks if API owns request-side orchestration
- `src/VirtualCompany.Application/**`
  - finance access command/query handlers
  - seed request orchestration service
  - idempotency/locking abstraction
  - DTOs/contracts for finance initialization state
- `src/VirtualCompany.Domain/**`
  - finance/company seeding state enums/value objects/entities
  - domain rules for seed lifecycle transitions
- `src/VirtualCompany.Infrastructure/**`
  - background job enqueueing/execution
  - Redis/distributed lock implementation if present
  - persistence updates for seed status / job correlation / timestamps / failure reason
  - telemetry event emission plumbing
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for finance entry and duplicate suppression
- Potentially:
  - migration files if persistence schema changes are needed
  - shared contracts in `src/VirtualCompany.Shared/**`
  - README/docs only if there is an established pattern for operational behavior documentation

Do not assume file names. Discover the existing finance seeding implementation and extend it in-place.

# Implementation plan

1. **Discover the current finance access and seeding flow**
   - Find the `/finance` route/component in the Blazor web app.
   - Trace how finance data is loaded and how the company’s seed state is currently determined.
   - Find any existing finance seeding service, background worker, job table, outbox, or Redis lock abstraction.
   - Identify whether telemetry already exists and follow the established pattern.

2. **Define/confirm the state model**
   - Ensure there is a canonical finance seed status for a company, at minimum:
     - `not_seeded`
     - `seeding`
     - `seeded`
     - `failed`
   - Persist enough metadata to support UX and operations, such as:
     - `seed_requested_at`
     - `seed_started_at`
     - `seed_completed_at`
     - `seed_failed_at`
     - `seed_failure_reason` or safe error summary
     - current job/correlation id if useful
   - Keep transitions explicit and safe:
     - `not_seeded -> seeding`
     - `failed -> seeding` on retry
     - `seeding -> seeded`
     - `seeding -> failed`

3. **Add finance-entry asynchronous seed request behavior**
   - On finance entry for a company in `not_seeded` state, request seeding asynchronously and return the page/API response immediately.
   - Do not block the request waiting for seed completion.
   - Emit telemetry for **seed job requested** from finance entry.
   - Return/query a view model that tells the UI seeding is in progress.

4. **Implement duplicate-job suppression**
   - Add an application service or command dedicated to “ensure finance seed in progress for company”.
   - This operation must be idempotent:
     - if state is already `seeding`, do not enqueue another job,
     - if already `seeded`, do nothing,
     - if `failed`, allow explicit retry path,
     - if `not_seeded`, transition and enqueue exactly once.
   - Prefer a single entry point so all callers share the same suppression logic.

5. **Add company-level locking / atomic guard**
   - Use the existing distributed coordination mechanism if available, preferably Redis per architecture guidance.
   - If Redis lock infrastructure does not exist, use the strongest existing atomic persistence pattern available in the codebase, such as:
     - transactional compare-and-set update,
     - unique constraint on active seed job per company,
     - row-level lock in PostgreSQL.
   - The critical section should cover:
     - checking current seed state,
     - marking state as `seeding` / active,
     - creating/enqueueing the background job.
   - Ensure concurrent requests for the same company cannot create duplicate active jobs.

6. **Update background job execution**
   - When the worker actually starts processing, emit telemetry for **seed job started**.
   - On success:
     - persist `seeded` state and completion metadata,
     - emit **completed** telemetry.
   - On failure:
     - persist `failed` state and safe failure details,
     - emit **failed** telemetry,
     - leave the system recoverable for retry.
   - Make the worker idempotent where practical in case of retries or partial failures.

7. **Update finance UI states**
   - When finance state is `seeding`, show a clear initializing state instead of empty/broken content.
   - Include either:
     - automatic polling/refresh until seeded, or
     - a clear reload action,
     - or both if consistent with current UX patterns.
   - When state becomes `seeded`, load and display seeded finance data successfully.
   - When state is `failed`, show a recoverable error state with retry guidance and a retry action if supported by current architecture.
   - Avoid flashing broken/empty states during transitions.

8. **Add retry behavior for failed seeding**
   - Provide a user-triggered retry path from the failed state.
   - Retry must reuse the same duplicate-safe “ensure seed in progress” logic.
   - Emit the same requested telemetry on retry from finance entry/retry action if that matches existing telemetry conventions.

9. **Telemetry and logging**
   - Record telemetry events for:
     - requested
     - started
     - completed
     - failed
   - Include safe dimensions where appropriate:
     - company id / tenant context
     - trigger source = finance_entry
     - correlation/job id
     - outcome / failure category
   - Do not log sensitive payloads.
   - Follow existing structured logging and correlation-id patterns.

10. **Testing**
   - Add tests for:
     - first `/finance` access in `not_seeded` triggers async seed request and returns non-blocking response,
     - repeated navigation while `seeding` does not enqueue duplicate jobs,
     - concurrent requests for the same company still result in only one active/enqueued seed job,
     - failed seed shows recoverable state,
     - retry from failed state re-requests seeding,
     - telemetry events are emitted for requested/started/completed/failed.
   - Prefer integration tests where they validate real application behavior; use unit tests for domain transition logic and lock/idempotency service behavior.

11. **Keep implementation aligned with architecture**
   - Maintain tenant scoping on all reads/writes.
   - Keep HTTP/UI concerns separate from application orchestration.
   - Use typed contracts and existing background execution patterns.
   - Avoid direct DB access from UI or controller layers.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests before and after changes:
   - `dotnet test`

3. Manually validate the finance flow for a company in `not_seeded` state:
   - Open `/finance`
   - Confirm the request returns promptly
   - Confirm an initializing state is shown
   - Confirm only one seed job is created

4. Validate duplicate suppression:
   - Refresh `/finance` repeatedly while seeding is active
   - If possible, hit the finance entry endpoint concurrently
   - Confirm no duplicate jobs are enqueued for the same company

5. Validate completion UX:
   - After seed completion, confirm the UI auto-refreshes or reload action successfully loads seeded data

6. Validate failure UX:
   - Simulate or force a seed failure in a safe test path
   - Confirm the UI shows a recoverable error state with retry guidance
   - Trigger retry and confirm seeding can proceed again

7. Validate telemetry/logging:
   - Confirm requested, started, completed, and failed events are emitted with expected correlation and tenant context

8. If schema changes were added:
   - Apply migrations using the project’s established migration workflow
   - Re-run build/tests after migration generation

# Risks and follow-ups

- **Concurrency risk:** duplicate suppression can still fail if the lock/transaction boundary is too weak. Prefer a truly atomic guard, not just an in-memory check.
- **State drift risk:** job enqueue may fail after state is marked `seeding`. Handle this carefully, either by transactional job persistence, compensating state update, or a recoverable timeout/reconciliation approach consistent with existing infrastructure.
- **Polling UX risk:** aggressive polling can create unnecessary load. Reuse existing refresh patterns and keep intervals conservative.
- **Telemetry duplication risk:** ensure requested vs started semantics are distinct and not emitted multiple times for the same logical attempt unless intentionally modeled.
- **Retry semantics:** define whether retry is allowed only from `failed` or also after stale `seeding` timeouts. Implement only what is necessary for this task, but note stale-job recovery as a follow-up if not already handled.
- **Follow-up suggestion:** if no generic distributed lock/idempotent job abstraction exists, consider extracting this implementation into a reusable company-scoped background job guard for other modules later.