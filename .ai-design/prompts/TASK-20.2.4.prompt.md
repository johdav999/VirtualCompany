# Goal
Implement **TASK-20.2.4 — Instrument analytics and logs for finance auto-seed lifecycle events** for **US-20.2 ST-FUI-410**.

Add end-to-end telemetry and structured logging for the finance auto-seed flow triggered from first access to `/finance` for existing companies in `not_seeded` state. Ensure instrumentation covers the lifecycle events:

- seed job **requested**
- seed job **started**
- seed job **completed**
- seed job **failed**

The implementation must support the existing acceptance criteria for async seeding, initializing UI state, duplicate-job prevention, recoverable failure UX, and telemetry emitted specifically from finance entry.

# Scope
In scope:

- Add structured application logs for finance auto-seed lifecycle transitions.
- Add analytics/telemetry event emission for finance-entry-triggered seeding.
- Include tenant/company context, correlation/job identifiers, and outcome metadata in logs/events.
- Instrument both:
  - request-path initiation from `/finance`
  - background worker/job execution lifecycle
- Ensure duplicate navigation while a job is already running does not emit misleading duplicate lifecycle events.
- Add/update tests validating telemetry/log behavior and idempotent instrumentation.
- Keep implementation aligned with modular monolith boundaries:
  - Web/UI for finance entry state
  - Application layer for orchestration/use cases
  - Infrastructure for telemetry/log sinks if abstractions already exist

Out of scope unless required by existing code structure:

- Rebuilding the finance auto-seed feature itself from scratch
- New analytics platform integration if a telemetry abstraction already exists
- Broad observability refactors outside finance auto-seed
- New audit/business event persistence unless already part of the established pattern for this feature

# Files to touch
Inspect the repo first and then update the exact files that own finance entry, seed orchestration, and observability. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance page/component for `/finance`
  - UI state handling for initializing / failed / reload states
- `src/VirtualCompany.Api/**`
  - finance endpoints/controllers if finance entry is API-driven
- `src/VirtualCompany.Application/**`
  - finance access query/command handlers
  - auto-seed orchestration services
  - background job request/start/complete/fail flow
  - telemetry abstraction usage
- `src/VirtualCompany.Infrastructure/**`
  - telemetry implementation
  - structured logging setup/helpers
  - background worker/job execution plumbing
  - idempotency/locking coordination if instrumentation depends on job state
- `src/VirtualCompany.Domain/**`
  - seed status enums/value objects if event naming depends on domain states
- `src/VirtualCompany.Shared/**`
  - shared contracts/DTOs for finance seed status if needed by UI/API
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for finance entry telemetry behavior
- Potentially additional test projects if present for Application/Web/Infrastructure

Before editing, identify the concrete implementation points for:
1. finance page entry
2. seed job enqueue/request logic
3. background seed job execution
4. existing telemetry/logging abstractions

# Implementation plan
1. **Discover existing finance auto-seed flow**
   - Find the `/finance` route implementation in Web/API.
   - Trace how company finance seed state is loaded (`not_seeded`, in-progress, completed, failed).
   - Identify where the async seed job is requested/enqueued.
   - Identify where duplicate-job prevention currently happens.
   - Identify the background worker/job handler that performs seeding.
   - Identify existing observability patterns:
     - `ILogger<T>`
     - telemetry client/service
     - analytics event publisher
     - correlation ID propagation

2. **Define canonical telemetry event names and payload**
   Use a consistent naming scheme, preferably under a finance namespace. If the repo has an existing convention, follow it. Otherwise use something like:

   - `finance_auto_seed_requested`
   - `finance_auto_seed_started`
   - `finance_auto_seed_completed`
   - `finance_auto_seed_failed`

   Include stable properties where available:
   - `company_id`
   - `user_id` for request-path events only if available and allowed
   - `job_id`
   - `correlation_id`
   - `trigger_source = finance_entry`
   - `seed_state_before`
   - `seed_state_after`
   - `was_duplicate_request` or `job_already_running`
   - `duration_ms` for completed/failed
   - `error_type`
   - `error_message_safe` or normalized failure code
   - tenant/company context fields already used by the app

   Do **not** log sensitive payloads or raw exception details beyond safe operational metadata.

3. **Instrument finance entry request path**
   In the application service/handler invoked when `/finance` is opened:
   - When company state is `not_seeded` and a new async seed job is actually requested:
     - emit structured log at `Information`
     - emit telemetry event `finance_auto_seed_requested`
   - If navigation occurs while a job is already running:
     - do not emit a second misleading `requested` event for a duplicate enqueue
     - optionally emit a lower-level log/telemetry event only if the codebase already tracks deduped attempts; otherwise keep it to logs only
   - Ensure the page request remains non-blocking.

4. **Instrument background job lifecycle**
   In the actual seed job executor/handler:
   - On execution start:
     - log `started`
     - emit `finance_auto_seed_started`
   - On successful completion:
     - log `completed`
     - emit `finance_auto_seed_completed`
     - include duration and resulting state
   - On failure:
     - log `failed` with safe structured metadata
     - emit `finance_auto_seed_failed`
     - include duration and normalized error classification
   - Ensure retries or re-entrancy do not create ambiguous duplicate lifecycle events beyond actual execution attempts. If retries are part of the worker model, include attempt number if available.

5. **Preserve idempotency semantics**
   - Confirm duplicate `/finance` visits while a seed job is already running do not enqueue another job.
   - Ensure telemetry reflects actual behavior:
     - one `requested` per actual enqueue
     - one `started` per actual execution attempt
     - one terminal event per execution attempt (`completed` or `failed`)
   - If locking or Redis/database coordination exists, instrument after the enqueue/lock decision, not before.

6. **Surface telemetry-aware UI state transitions if needed**
   If the UI currently lacks explicit hooks around initializing/error/reload states:
   - keep UI behavior intact
   - only add minimal instrumentation-related wiring needed to preserve correlation/context
   - do not overcomplicate the Blazor page

7. **Add or extend observability abstractions**
   If the repo already has a telemetry abstraction, use it.
   If not, add a minimal application-facing interface such as:
   - `IAnalyticsTelemetry`
   - `IProductTelemetry`
   - or existing equivalent

   Requirements:
   - callable from Application layer
   - implemented in Infrastructure
   - testable via fake/mock
   - no direct dependency from Domain on telemetry

8. **Add tests**
   Add focused tests for:
   - finance entry in `not_seeded` state emits `requested` when a job is newly enqueued
   - repeated finance entry while job already running does **not** emit duplicate `requested` for the same company/job
   - job execution emits `started` then `completed` on success
   - job execution emits `started` then `failed` on failure
   - logs/telemetry include required identifiers and `trigger_source = finance_entry`
   - if practical, verify safe error metadata rather than raw exception leakage

   Prefer existing test style in the repo:
   - integration tests for API/request behavior
   - application tests for handler/service instrumentation
   - avoid brittle assertions on full log message strings; assert structured properties or telemetry calls

9. **Keep naming and architecture consistent**
   - Follow existing namespaces, folder structure, and CQRS-lite patterns.
   - Keep business audit events separate from technical logs unless the feature already persists business audit records.
   - Do not introduce cross-layer leakage.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate the finance entry flow in code or local run:
   - Open `/finance` for a company in `not_seeded`
   - Confirm request path does not block on seeding
   - Confirm a single seed job is requested
   - Confirm telemetry/log entry for `requested`

4. Validate duplicate navigation behavior:
   - Open `/finance` again while seed is in progress
   - Confirm no duplicate enqueue
   - Confirm no duplicate `requested` event for the same active job

5. Validate successful execution path:
   - Confirm background worker emits:
     - `started`
     - `completed`
   - Confirm completion includes duration and identifiers
   - Confirm UI can refresh or reload into seeded data state

6. Validate failure path:
   - Simulate or test a seed failure
   - Confirm background worker emits:
     - `started`
     - `failed`
   - Confirm failure metadata is safe/sanitized
   - Confirm UI shows recoverable error guidance rather than empty/broken state

7. Review logs for structure:
   - company/tenant context present
   - correlation/job IDs present where available
   - no sensitive payload leakage
   - event names consistent and searchable

# Risks and follow-ups
- **Unknown existing telemetry abstraction**: the repo may not yet have a unified analytics service. If absent, add the smallest viable abstraction in Application with Infrastructure implementation.
- **Duplicate event inflation**: emitting `requested` before dedupe/lock confirmation will create false positives. Instrument only after actual enqueue decision.
- **Retry semantics**: background retries may produce multiple `started`/`failed` pairs. If retries exist, include attempt metadata to avoid confusion.
- **Correlation propagation gaps**: request correlation IDs may not automatically flow into background jobs. If missing, propagate a seed job correlation identifier explicitly.
- **UI/API split uncertainty**: finance entry may be SSR Blazor, API-backed, or mixed. Trace the real flow before changing files.
- **Log assertion brittleness**: prefer testing telemetry abstraction calls and structured state over exact log text.
- **Follow-up**: if product analytics dashboards exist, document the new event contract and add dashboard/alert wiring for finance auto-seed failure rates.