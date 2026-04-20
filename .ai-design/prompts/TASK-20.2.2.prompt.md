# Goal
Implement backlog task **TASK-20.2.2** for story **US-20.2 ST-FUI-410 — Auto-seed finance data on first finance access for existing companies**.

Deliver the frontend and supporting integration behavior for `/finance` so that when a company’s finance area is first accessed and the company is in a `not_seeded` state:

- the page request does **not** block on seeding
- an async seed job is requested once
- the UI shows clear **initializing / in-progress / success / failure** states
- duplicate navigation while a job is already running does **not** enqueue duplicate jobs
- users can recover from failures with retry guidance
- telemetry is emitted for requested, started, completed, and failed events originating from finance entry

Use the existing .NET solution conventions and keep changes aligned with the modular monolith architecture, tenant scoping, CQRS-lite, and background-job/idempotency patterns.

# Scope
In scope:

- Finance entry flow for `/finance`
- Frontend state handling for:
  - `not_seeded`
  - `seeding_requested` / `in_progress`
  - `seeded` / success-ready
  - `failed`
- Automatic trigger of seed request from finance entry when appropriate
- Polling or refresh/reload UX after completion
- Retry UX and recoverable error presentation
- Backend/API support needed by the frontend to:
  - read current finance seed status
  - request seed job idempotently
  - avoid duplicate enqueue while job already exists/runs
- Telemetry instrumentation for finance-entry-triggered seed lifecycle events

Out of scope unless required to complete this task safely:

- Reworking the full finance domain model
- Large redesign of background job infrastructure
- Mobile app changes
- Broad analytics dashboards beyond the required telemetry
- Non-finance onboarding flows

Assumptions to validate in the codebase before implementation:

- There is already some finance/company status model or a place to add one
- There is an existing background job mechanism or worker pattern to hook into
- There is an existing telemetry/logging abstraction; prefer that over introducing a new one
- `/finance` is implemented in Blazor Web and can be extended with stateful UI

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance page/component for `/finance`
  - related view models, services, or API clients
  - shared UI components for loading/error/empty states if applicable
- `src/VirtualCompany.Api/**`
  - finance controller/endpoints for status/query/request
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for:
    - get finance seed status
    - request finance seed
    - retry finance seed
  - telemetry/event recording hooks
- `src/VirtualCompany.Domain/**`
  - finance seed status enum/value object/entity fields if missing
  - idempotency/job state rules
- `src/VirtualCompany.Infrastructure/**`
  - background job enqueueing
  - persistence for seed job/status
  - telemetry implementation
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for request/status/retry/idempotency
- Add web/component/unit/integration tests in the appropriate existing test projects if present

If migrations are required, add them in the project’s current migration location/pattern rather than inventing a new one.

# Implementation plan
1. **Discover existing finance entry and seed infrastructure**
   - Find the `/finance` page/component and current data-loading path.
   - Find any existing finance seeding logic, company finance status fields, background worker/job queue, and telemetry abstractions.
   - Confirm how tenant/company context is resolved in web and API layers.
   - Identify whether there is already a status endpoint or if one must be added.

2. **Define/normalize finance seed states**
   - Ensure there is a single backend source of truth for finance initialization state, with enough detail for the UI.
   - Prefer a compact state model such as:
     - `not_seeded`
     - `queued` or `requested`
     - `in_progress`
     - `seeded`
     - `failed`
   - Include optional metadata useful to the UI:
     - last error summary
     - last updated timestamp
     - active job id/correlation id if available
     - whether retry is allowed
   - Keep naming consistent across domain, API DTOs, and frontend.

3. **Add idempotent seed request behavior**
   - On finance entry, if status is `not_seeded`, request seeding asynchronously.
   - Implement request handling so repeated requests for the same company while a seed job is already queued/running do not enqueue duplicates.
   - Use existing locking/idempotency patterns if present; otherwise implement a minimal safe guard at the application/infrastructure layer.
   - Return the current effective state after request:
     - newly requested
     - already queued/running
     - already seeded
     - failed
   - Emit telemetry for **seed job requested** from finance entry.

4. **Ensure background worker lifecycle updates state**
   - When the worker actually begins processing, transition to `in_progress` and emit **started** telemetry.
   - On successful completion, transition to `seeded` and emit **completed** telemetry.
   - On failure, transition to `failed`, persist a safe user-facing error summary, and emit **failed** telemetry.
   - Preserve tenant/company scoping and correlation IDs.

5. **Expose status/query API for the frontend**
   - Add or extend an endpoint/query the finance page can call to get current seed status.
   - If needed, add a request endpoint for:
     - initial auto-request
     - explicit retry
   - Keep contracts simple and UI-oriented, e.g.:
     - `status`
     - `message`
     - `canRetry`
     - `lastUpdatedAt`
   - Ensure authorization and company scoping are enforced.

6. **Implement `/finance` frontend state machine**
   - Update the Blazor finance page so it:
     - loads current finance seed status on entry
     - if `not_seeded`, triggers the async request without blocking the page
     - renders an initializing/in-progress state immediately
   - Render states clearly:
     - **Initializing/In progress:** explain that finance data is being prepared
     - **Success:** automatically refresh data or show a clear reload/load action
     - **Failure:** show recoverable error state with retry guidance
   - Avoid blank, broken, or misleading empty states during seeding/failure.

7. **Add refresh/polling behavior**
   - Implement a lightweight polling loop or equivalent refresh mechanism while status is `queued`/`in_progress`.
   - Stop polling when status becomes `seeded` or `failed`.
   - On `seeded`, either:
     - automatically reload finance data and transition into the normal finance view, or
     - present a clear reload CTA if automatic refresh is not reliable in the current page architecture
   - Prefer the simplest robust option already idiomatic in the app.

8. **Implement retry UX**
   - In `failed` state, show:
     - concise explanation
     - retry button/action
     - guidance that the user can retry safely
   - Retry should call the same idempotent request path or a dedicated retry command if the domain requires it.
   - After retry, return to initializing/in-progress UI and resume polling.

9. **Instrument telemetry**
   - Record telemetry for:
     - requested
     - started
     - completed
     - failed
   - Include dimensions/properties where supported:
     - company id
     - finance entry source (`/finance`)
     - correlation/job id
     - outcome/error category
   - Do not leak sensitive details in user-facing messages or telemetry payloads beyond existing standards.

10. **Add tests**
   - API/application tests:
     - requesting seed from `not_seeded` enqueues once
     - repeated request while queued/running does not duplicate
     - failed state can be retried
     - status endpoint returns expected state transitions
   - Web/component tests if test infrastructure exists:
     - initializing state renders
     - failure state renders retry guidance
     - success path reload action/auto-refresh is shown
   - If UI tests are not practical in current setup, cover state mapping logic in unit tests and document any gaps.

11. **Keep implementation production-safe**
   - Use safe async patterns in Blazor; avoid fire-and-forget without error handling.
   - Ensure page remains responsive even if request/polling fails transiently.
   - Log technical failures separately from business telemetry.
   - Keep changes minimal and cohesive.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app for a company in `not_seeded` state:
   - Navigate to `/finance`
   - Confirm page loads immediately
   - Confirm seed request is triggered asynchronously
   - Confirm initializing/in-progress UI is shown

4. Duplicate request verification:
   - Refresh/navigate repeatedly to `/finance` while seeding is active
   - Confirm only one job is queued/running for the company
   - Confirm UI continues to show in-progress state without errors

5. Success verification:
   - Complete seeding
   - Confirm UI auto-refreshes into seeded finance data or presents a clear reload action
   - Confirm seeded data loads successfully

6. Failure verification:
   - Simulate or force a seed failure
   - Confirm recoverable error UI appears instead of empty/broken page
   - Confirm retry guidance is visible
   - Trigger retry and confirm state returns to initializing/in-progress

7. Telemetry verification:
   - Confirm requested, started, completed, and failed events are emitted for finance entry flow
   - Confirm event properties include company/correlation context as supported

8. Regression check:
   - Verify already-seeded companies still load `/finance` normally without triggering seed requests
   - Verify authorization/tenant scoping still holds

# Risks and follow-ups
- **Unknown existing finance seed model:** If there is already a partially implemented status/job model, align with it rather than creating a parallel one.
- **Duplicate job prevention:** This must be enforced server-side, not only in the UI. If current worker infrastructure lacks idempotency, implement the smallest reliable guard now and note any deeper hardening follow-up.
- **Polling complexity in Blazor:** Be careful with component disposal/cancellation to avoid orphaned polling tasks.
- **Telemetry fragmentation:** Prefer existing observability abstractions so events are queryable alongside current platform telemetry.
- **Migration risk:** If new persistence fields/tables are needed, keep schema changes minimal and backward-compatible.

Potential follow-ups to note in implementation comments or PR notes if not completed here:
- richer progress percentages/messages for seeding
- admin/operator diagnostics for failed finance seed jobs
- notification/inbox integration when seeding completes or fails
- broader audit trail exposure for finance initialization events