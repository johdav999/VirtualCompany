# Goal
Implement backlog task **TASK-7.4.4 — Background job failures are logged and retryable** for story **ST-104 Baseline platform observability and operational safeguards**.

The coding agent should add a production-ready baseline for background job execution resilience in the .NET solution so that:
- background job failures are captured with structured technical logs,
- retries are supported for transient failures,
- permanent/business failures are not retried indefinitely,
- retry attempts preserve correlation and tenant context where applicable,
- the implementation fits the existing modular monolith architecture and can be reused by future workers (scheduler, outbox dispatcher, workflow runner, ingestion jobs, etc.).

There are no story-specific acceptance criteria beyond the story-level requirement, so the implementation should define a pragmatic, minimal, extensible baseline.

# Scope
In scope:
- Inspect the existing solution for any current background worker infrastructure:
  - `BackgroundService`, hosted services, job runners, outbox dispatchers, schedulers, queue consumers, or retry helpers.
- Introduce or refine a shared background job execution pattern in the backend.
- Ensure failures are logged using structured logging with enough context to support operations:
  - job name/type,
  - attempt number,
  - correlation ID if available,
  - tenant/company ID if available,
  - exception type/message.
- Add retry behavior for transient failures with bounded attempts and backoff.
- Distinguish retryable/transient failures from non-retryable/permanent failures.
- Ensure final failure is clearly logged after retries are exhausted.
- Add or update tests for retry behavior and failure classification where practical.
- Keep implementation aligned with ST-104 technical logging, not business audit logging.

Out of scope unless already partially implemented and trivial to complete:
- Full observability platform integration (App Insights, OpenTelemetry exporters, dashboards).
- New UI for viewing job failures.
- Full dead-letter queue infrastructure.
- Large workflow-engine redesign.
- Business audit event persistence for technical worker failures.
- Introducing an external message broker if none exists.

If the repository already uses a job library (e.g. Hangfire, Quartz, custom queue, Channels, MediatR-based dispatcher), extend that existing pattern rather than replacing it.

# Files to touch
Start by inspecting these likely locations and then update the actual files you find relevant:

- `src/VirtualCompany.Api/**`
  - hosted service registration
  - logging setup
  - dependency injection composition root
- `src/VirtualCompany.Application/**`
  - background job abstractions
  - retry policies
  - execution services
  - command handlers triggered by workers
- `src/VirtualCompany.Infrastructure/**`
  - concrete worker implementations
  - outbox/background dispatcher
  - persistence-backed job coordination if present
  - Redis coordination if present
- `src/VirtualCompany.Domain/**`
  - only if a small shared exception classification concept belongs here
- `README.md`
  - only if there is already an operational/setup section that should mention retry behavior
- Test projects anywhere in the solution
  - add unit/integration tests around retry and logging behavior where the repo conventions support it

Prefer touching the smallest coherent set of files. Do not create parallel infrastructure if an existing worker pattern already exists.

# Implementation plan
1. **Discover the current background processing model**
   - Search the solution for:
     - `BackgroundService`
     - `IHostedService`
     - `Channel`
     - `PeriodicTimer`
     - `Hangfire`
     - `Quartz`
     - `Outbox`
     - `Dispatcher`
     - `Retry`
     - `Polly`
   - Identify the primary execution path for background jobs and where failures currently surface.
   - Determine whether there is already:
     - correlation ID propagation,
     - tenant/company context propagation,
     - structured logging conventions,
     - exception middleware or shared logging helpers.

2. **Define a minimal shared retryable execution abstraction**
   - If no shared abstraction exists, introduce one in Application or Infrastructure, such as:
     - `IBackgroundJobExecutor`
     - `BackgroundJobExecutionOptions`
     - `ITransientFailureDetector`
     - `BackgroundJobExecutionContext`
   - Keep it lightweight and reusable.
   - The abstraction should support:
     - job name,
     - async execution delegate,
     - max attempts,
     - backoff strategy,
     - cancellation token,
     - optional tenant/company ID,
     - optional correlation ID.

3. **Implement failure classification**
   - Add a simple mechanism to distinguish transient vs permanent failures.
   - Prefer one of these approaches, depending on repo style:
     - marker exceptions like `TransientBackgroundJobException`,
     - a classifier service that inspects exception types,
     - explicit wrapping by job implementations.
   - Treat obvious operational exceptions as retryable where reasonable:
     - timeout/network/temporary dependency failures.
   - Treat business/policy/validation failures as non-retryable.
   - Keep the classifier conservative and easy to extend.

4. **Implement bounded retry with backoff**
   - Add retry logic around job execution:
     - log each failed attempt at warning/error level,
     - retry only when classified as transient,
     - stop after configured max attempts,
     - log terminal failure clearly when retries are exhausted,
     - do not swallow cancellation exceptions.
   - Use existing retry libraries if already present; otherwise implement a small internal retry loop.
   - Prefer deterministic backoff such as small exponential or incremental delays.
   - Ensure cancellation token is honored during delays.

5. **Add structured logging**
   - Ensure logs include structured properties, not interpolated-only strings.
   - Include as many of these as available:
     - `JobName`
     - `Attempt`
     - `MaxAttempts`
     - `CompanyId` or `TenantId`
     - `CorrelationId`
     - `ExceptionType`
   - Log events for:
     - job start (optional if consistent with current logging volume),
     - retryable failure,
     - retry scheduled,
     - permanent failure,
     - retries exhausted,
     - eventual success after retry.
   - Reuse existing logging scopes if the solution already uses them.

6. **Integrate with existing workers**
   - Update the actual background worker(s) to execute through the shared retryable executor.
   - Prioritize the most central worker path, such as:
     - outbox dispatcher,
     - scheduled job runner,
     - workflow progression worker,
     - inbox processor.
   - If multiple workers exist, apply the pattern to the shared base class or helper rather than duplicating logic.

7. **Configuration**
   - If the app already uses options/configuration for workers, add retry settings there.
   - Otherwise add a minimal options class with sensible defaults, for example:
     - max attempts: 3
     - base delay: short and safe for tests/ops
   - Wire options through DI.
   - Avoid overengineering per-job configuration unless the repo already supports it.

8. **Testing**
   - Add tests for the shared execution behavior:
     - transient failure retries and eventually succeeds,
     - permanent failure does not retry,
     - transient failure stops after max attempts,
     - cancellation is not retried,
     - logging-relevant context is passed through where testable.
   - If there are existing worker tests, extend them rather than creating isolated artificial tests.
   - Keep tests stable and fast.

9. **Documentation/comments**
   - Add concise XML comments or inline comments only where needed to clarify retry classification or executor behavior.
   - If there is an operations section in `README.md`, add a brief note about background retry behavior only if appropriate.

10. **Implementation constraints**
   - Preserve clean architecture boundaries.
   - Keep technical logs separate from domain/business audit events.
   - Do not add direct database writes solely for technical failure tracking unless the repo already has a technical job state store.
   - Prefer composable services over static helpers.
   - Avoid introducing a new third-party dependency unless clearly justified by existing project patterns.

# Validation steps
Run and report the relevant results:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for Application/Infrastructure, run those specifically as well.

4. Manually validate code paths by reviewing:
   - a retryable exception path logs attempt failures and retries,
   - a non-retryable exception path logs once and stops,
   - retries exhausted path logs terminal failure,
   - cancellation path exits cleanly without misleading failure logs.

5. In the final implementation notes, summarize:
   - which worker(s) now use the shared retryable execution path,
   - how transient vs permanent failures are determined,
   - what configuration knobs were added,
   - any gaps left because the repository lacked a concrete worker implementation.

# Risks and follow-ups
- The repository may not yet contain a real background job runner; if so, implement the shared retryable execution foundation and wire it into the nearest existing hosted service or dispatcher without inventing a full job system.
- Tenant/company context may not currently flow into background workers; include it where available, and note any propagation gaps.
- Correlation IDs may be HTTP-request-centric today; background jobs may need generated correlation IDs when none exist.
- Without a durable job state store, retries may only be in-process for now; note this limitation clearly if applicable.
- If outbox dispatching already exists, that is the best place to anchor this work for immediate value.
- Future follow-up likely belongs under ST-404 for richer execution state, escalations, dead-letter handling, and idempotent durable retries.