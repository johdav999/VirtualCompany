# Goal
Implement backlog task **TASK-12.3.4 — Background dispatcher delivers in-app notifications reliably** for story **ST-603 Alerts, notifications, and approval inbox** in the existing .NET modular monolith.

The coding agent should add or complete the backend support needed so that **in-app notifications are dispatched asynchronously and reliably via an outbox-backed background worker**, with tenant-safe processing, retry behavior, idempotency, and observable failure handling.

Because this task has no explicit acceptance criteria beyond the story context, treat the following as the operational definition of done:

- Notifications for key events can be persisted without sending them inline in the request path.
- A background dispatcher reads pending outbox work and creates in-app notification records reliably.
- Dispatch is **idempotent** and does not create duplicate notifications when retries occur.
- Processing is **tenant-aware**.
- Failures are retried for transient issues and surfaced/logged for operational review.
- The implementation fits the architecture decision: **database-backed outbox + background dispatcher**.

# Scope
Focus only on the backend implementation required for reliable in-app notification dispatch. Do **not** build full inbox UI unless minimal API/query support is required by existing patterns.

Include:

- Notification domain model and persistence if not already present.
- Outbox integration for notification fan-out.
- Background worker/hosted service that polls and dispatches pending notification outbox messages.
- Idempotency protections to avoid duplicate notification creation.
- Retry and failure state handling.
- Tenant scoping and correlation/logging support.
- Tests covering success, retry, and duplicate-delivery scenarios.

Prefer a minimal, production-shaped implementation over speculative abstractions.

Out of scope unless already partially implemented and necessary to complete the task:

- Mobile push notifications.
- Email/SMS delivery.
- Full approval inbox UX.
- Broad event bus refactors unrelated to notifications.
- Message broker integration.

# Files to touch
Inspect the solution first and then modify the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - notification entity/value objects/enums
  - outbox message abstractions if domain-owned
- `src/VirtualCompany.Application/**`
  - commands/handlers for creating notification intents
  - dispatcher interfaces
  - idempotency/retry policies at application layer
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - outbox persistence/processing
  - background worker implementation
  - migrations or migration scaffolding support
- `src/VirtualCompany.Api/**`
  - DI registration for hosted worker
  - any endpoints needed for smoke validation
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for notification DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests
- `README.md` or relevant docs only if there is an established pattern for operational notes

Also inspect for existing equivalents before adding new ones:

- background job infrastructure
- outbox tables/entities
- approval/escalation/workflow event publishing
- notification or inbox models
- correlation ID / tenant context plumbing
- retry helpers / distributed locking

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect the solution for:
     - existing outbox implementation
     - hosted/background services
     - EF Core DbContext and migration approach
     - tenant context resolution
     - approval/workflow failure/escalation events
     - notification/inbox entities or DTOs
   - Reuse existing conventions and naming. Do not introduce a second outbox pattern if one already exists.

2. **Define the notification persistence model**
   - If absent, add a notification aggregate/table suitable for in-app inbox use.
   - Keep the model minimal but sufficient for ST-603:
     - `id`
     - `company_id`
     - `user_id` or recipient reference
     - `type`
     - `title`
     - `body` or summary
     - `status` / state fields supporting unread/read/actioned
     - `priority`
     - `source_entity_type`
     - `source_entity_id`
     - `created_at`
     - `read_at`
     - `actioned_at`
     - optional `deduplication_key`
   - Ensure indexes support:
     - recipient inbox queries
     - unread filtering
     - deduplication/idempotency checks
     - tenant scoping

3. **Define or reuse an outbox message contract for notification dispatch**
   - Represent notification work as an outbox message, not direct inline insert fan-out from request handlers.
   - If an outbox table already exists, add a notification message type/payload.
   - Payload should include only what is needed to create the in-app notification deterministically:
     - company/tenant id
     - recipient id(s) or routing info
     - notification type
     - title/body/summary
     - source entity references
     - correlation/idempotency key
     - created timestamp
   - Prefer versionable JSON payloads if that matches current architecture.

4. **Publish notification intents from relevant application flows**
   - Wire notification intent creation into existing approval/escalation/workflow failure/briefing flows only where already available and easy to connect.
   - If upstream producers already exist, adapt them to enqueue outbox messages instead of writing notifications directly.
   - Keep fan-out out of the request path.
   - Ensure notification creation and outbox persistence happen in the same transaction where appropriate.

5. **Implement the background dispatcher**
   - Add or complete a hosted background service in Infrastructure/API registration that:
     - polls pending outbox notification messages in batches
     - claims work safely
     - deserializes payloads
     - creates notification records
     - marks outbox messages processed on success
   - Use existing locking/claiming conventions if present.
   - If no pattern exists, implement safe polling with one of:
     - row status transition with concurrency token
     - `FOR UPDATE SKIP LOCKED` style claim logic if supported by current data access approach
   - Keep processing batched and cancellation-token aware.

6. **Add idempotency protections**
   - Ensure retries do not create duplicate notifications.
   - Use a deterministic deduplication key, for example based on:
     - message id, or
     - `(company_id, recipient_id, notification_type, source_entity_type, source_entity_id, correlation_key)`
   - Enforce idempotency with:
     - unique index/constraint where practical, and/or
     - existence check before insert
   - Treat duplicate insert attempts as successful processing, not failures.

7. **Implement retry and failure handling**
   - Track outbox processing attempts, last error, and next eligible processing time if the outbox schema supports it.
   - Distinguish:
     - transient infrastructure failures => retry
     - permanent payload/validation failures => dead-letter or mark failed
   - Log structured errors with:
     - outbox message id
     - company id
     - correlation id
     - notification type
   - Do not let one bad message block the batch.

8. **Preserve tenant isolation**
   - Every notification record must carry `company_id`.
   - Dispatcher must never process or query notifications without tenant context in the data being handled.
   - Validate payload tenant data before insert.
   - Follow existing repository/query filters and conventions.

9. **Add observability**
   - Emit structured logs for:
     - batch start/end
     - claimed count
     - processed count
     - retry count
     - permanent failure count
   - Include correlation IDs if the project already supports them.
   - If health checks/metrics patterns exist, hook into them lightly rather than inventing a new telemetry subsystem.

10. **Database migration**
   - Add/update EF Core configuration and create the required migration for:
     - notifications table
     - indexes
     - unique dedupe constraint
     - any outbox schema additions needed for retries/status
   - Keep migration names clear and task-focused.

11. **Testing**
   - Add automated tests that prove:
     - a queued outbox notification message results in one notification record
     - reprocessing the same message does not create duplicates
     - transient failure leaves the message retryable
     - successful processing marks the outbox item complete
     - tenant/company id is persisted correctly
   - Prefer integration tests around the real persistence layer patterns used by the solution.

12. **Keep implementation aligned with story intent**
   - The result should support ST-603’s requirement that:
     - notifications are generated for approvals/escalations/workflow failures/briefings
     - notification state supports unread/read/actioned
     - background dispatcher delivers in-app notifications reliably
   - If upstream event producers are not yet fully implemented, at minimum provide the reliable dispatch path and one representative producer path to demonstrate end-to-end behavior.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the repo workflow, generate/apply or verify the migration compiles cleanly.

4. Manually validate the happy path in code/tests:
   - create or enqueue a notification outbox message
   - run the dispatcher
   - verify exactly one notification row is created
   - verify status defaults to unread
   - verify tenant/company id and source references are correct

5. Manually validate idempotency:
   - process the same outbox message twice or simulate retry after partial failure
   - verify no duplicate notification is created

6. Manually validate failure handling:
   - simulate a transient persistence/processing failure
   - verify the outbox item is not lost and remains retryable or is rescheduled according to existing conventions

7. Confirm no request-path fan-out:
   - inspect producer flow to ensure notification delivery is queued through outbox/background processing rather than synchronous inline dispatch

8. Summarize in the final implementation notes:
   - what existing patterns were reused
   - what schema changes were introduced
   - how idempotency is enforced
   - what scenarios are covered by tests

# Risks and follow-ups
- **Existing outbox may already exist with different semantics**  
  Reuse it rather than creating a parallel mechanism. If it is incomplete, extend it minimally.

- **Notification recipient model may be unclear**  
  If the product has not finalized role-based fan-out, implement direct user-recipient notifications first with extensibility for future routing.

- **Background worker concurrency issues**  
  Multiple app instances can cause duplicate processing if claim logic is weak. Use DB-safe claiming and unique dedupe constraints.

- **Retry classification may be underspecified**  
  Default to conservative behavior: retry infrastructure exceptions, fail fast on invalid payloads.

- **Migration workflow may differ in this repo**  
  Follow the project’s established EF Core migration pattern; do not invent a new one.

- **Upstream producers may not yet emit notification intents**  
  If so, implement one representative producer path now and document follow-up work to connect approvals, escalations, workflow failures, and briefings.

- **Future follow-ups likely needed**
  - inbox query/read/action APIs if not already present
  - notification prioritization and sorting rules
  - dead-letter inspection tooling
  - mobile push/email channels using the same outbox/event source
  - richer audit linkage between notifications and approvals/workflows