# Goal
Implement **TASK-10.4.4** for **ST-404 — Escalations, retries, and long-running background execution** by making **outbox-backed side effects dispatch reliably without duplication** in the existing .NET modular monolith.

The coding agent should deliver a production-appropriate first version of a **database-backed outbox + background dispatcher** that:

- persists side effects transactionally with business state changes
- dispatches them asynchronously from background workers
- prevents duplicate delivery through idempotent claiming/marking and handler safeguards
- supports retries for transient failures
- distinguishes permanent failures from transient ones
- preserves tenant and correlation context
- is testable end-to-end

This task is specifically about the **reliable dispatch mechanism**, not about implementing every downstream notification/integration feature in the backlog.

# Scope
In scope:

- Add or complete an **OutboxMessage** persistence model in PostgreSQL-backed infrastructure.
- Add application/infrastructure abstractions for writing outbox messages within the same transaction as domain/application state changes.
- Add a **background dispatcher worker** that polls pending outbox messages, claims them safely, dispatches them, and updates status.
- Ensure **at-least-once dispatch with duplicate prevention controls**:
  - safe concurrent claiming
  - idempotent processing contract
  - no repeated dispatch of already-completed messages
- Add retry metadata and policy:
  - attempt count
  - next attempt time
  - last error
  - terminal failure/dead-letter style state for permanent failures or retry exhaustion
- Preserve:
  - `company_id` / tenant context where applicable
  - correlation/causation identifiers
  - event type / payload metadata
- Add tests covering:
  - message persistence
  - single dispatch
  - retry behavior
  - duplicate/concurrency protection
  - handler idempotency expectations where feasible

Out of scope unless already partially present and needed to complete this task:

- introducing an external message broker
- implementing all notification channels
- broad workflow engine redesign
- full inbox/notification UX
- large-scale refactors unrelated to outbox reliability

If the repo already contains partial outbox infrastructure, extend and align it rather than replacing it unnecessarily.

# Files to touch
Inspect first, then modify the minimal coherent set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - only if a domain event or outbox contract belongs here
- `src/VirtualCompany.Application/**`
  - outbox abstractions/interfaces
  - integration event or side-effect contracts
  - dispatcher contracts
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL entity mapping for outbox table
  - repository/store implementation
  - background worker implementation
  - transaction/save-changes integration
  - retry policy and dispatcher logic
- `src/VirtualCompany.Api/**`
  - DI registration / hosted service wiring
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests
- `README.md` or relevant docs
  - brief operational notes if needed
- migration location used by this repo
  - add a migration for the outbox table/schema changes if not already present

Likely concrete file additions if absent:

- `src/VirtualCompany.Application/.../IOutboxStore.cs`
- `src/VirtualCompany.Application/.../IOutboxDispatcher.cs`
- `src/VirtualCompany.Infrastructure/Persistence/.../OutboxMessageEntity.cs`
- `src/VirtualCompany.Infrastructure/BackgroundJobs/.../OutboxDispatcherWorker.cs`
- `src/VirtualCompany.Infrastructure/.../OutboxMessageProcessor.cs`
- `tests/VirtualCompany.Api.Tests/.../OutboxDispatchTests.cs`

Use actual project conventions and namespaces already present in the solution.

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Review solution structure, persistence approach, background worker patterns, and any existing domain event/outbox code.
   - Search for:
     - `Outbox`
     - `BackgroundService`
     - `IHostedService`
     - `DbContext`
     - transaction pipeline / unit of work
     - domain events / integration events
     - notification dispatchers
   - Reuse existing patterns for logging, correlation IDs, tenant context, and retries.

2. **Define the outbox data model**
   Implement or align an outbox table/entity with fields similar to:

   - `Id` (GUID/UUID)
   - `CompanyId` nullable if some system events are global, otherwise tenant-scoped
   - `Type`
   - `PayloadJson`
   - `HeadersJson` or metadata JSON
   - `CorrelationId`
   - `CausationId`
   - `IdempotencyKey` if useful
   - `Status` (`Pending`, `Processing`, `Succeeded`, `Failed`, `DeadLettered`)
   - `AttemptCount`
   - `AvailableAtUtc` / `NextAttemptAtUtc`
   - `ClaimedAtUtc`
   - `ClaimedBy`
   - `ProcessedAtUtc`
   - `LastError`
   - `CreatedAtUtc`

   Add indexes for efficient polling, e.g. on:
   - status + next attempt time
   - created time
   - company id if tenant-scoped querying is needed

3. **Persist outbox messages transactionally**
   Ensure side effects are written to the outbox in the same database transaction as the business state change that caused them.

   Preferred approaches:
   - if domain events already exist, translate them to outbox messages during `SaveChanges`
   - otherwise provide an application service/store used by command handlers before commit

   Requirements:
   - no side effect should be dispatched directly in the request path if it should be reliable
   - the outbox write must succeed/fail atomically with the business transaction

4. **Create dispatcher abstractions**
   Add clear interfaces for:
   - fetching/claiming dispatchable messages
   - dispatching a single message to a registered handler
   - marking success/failure/retry
   - resolving handlers by message type

   Keep contracts simple and testable.

5. **Implement safe polling and claiming**
   The dispatcher worker should:
   - poll in batches
   - only select messages eligible for dispatch (`Pending`/retryable and `NextAttemptAtUtc <= now`)
   - claim messages atomically to avoid multiple workers processing the same row

   For PostgreSQL, prefer a safe claim strategy such as:
   - `FOR UPDATE SKIP LOCKED`, or
   - atomic update with status transition and row count check

   The key requirement is that concurrent workers do not process the same message simultaneously under normal operation.

6. **Implement dispatch execution**
   For each claimed message:
   - deserialize payload by message type
   - restore correlation/tenant context where possible
   - invoke the appropriate handler
   - on success:
     - mark `Succeeded`
     - set `ProcessedAtUtc`
   - on transient failure:
     - increment attempts
     - compute next retry time with bounded backoff
     - return to retryable state
   - on permanent failure:
     - mark terminal failure / dead-letter state
     - preserve error details for operations

   Use structured logging with message id, type, company id, correlation id, attempt count.

7. **Define transient vs permanent failure behavior**
   Add a pragmatic classification strategy:
   - transient:
     - network timeouts
     - temporary dependency unavailability
     - retryable infrastructure exceptions
   - permanent:
     - payload deserialization failure
     - unknown message type
     - validation/business rule failure that will not succeed on retry

   If the codebase already has exception taxonomy, use it.
   Otherwise add a small internal classifier rather than overengineering.

8. **Add idempotency safeguards**
   Because outbox dispatch is at-least-once, handlers must tolerate duplicates.

   Implement one or both:
   - a processed-message/idempotency record for side-effect handlers, or
   - handler-level idempotency using a stable message id / idempotency key

   Minimum acceptable outcome:
   - the dispatcher never intentionally reprocesses succeeded messages
   - retries of the same message do not create duplicate side effects when a handler is invoked more than once

   If there is already a notification/integration dispatch layer, thread the outbox message id or idempotency key through it.

9. **Wire up hosted background execution**
   Register the dispatcher as a hosted service in the API host or the designated worker host already used by the solution.
   Ensure:
   - configurable polling interval
   - configurable batch size
   - graceful shutdown via cancellation token
   - no busy looping when queue is empty

10. **Add tests**
   Add focused automated tests, preferring integration tests where persistence and worker behavior matter.

   Cover at least:
   - outbox message is persisted with business transaction
   - eligible pending message is dispatched once and marked succeeded
   - transient failure causes retry metadata update
   - permanent failure causes terminal state
   - concurrent dispatchers do not both process the same message
   - duplicate invocation does not duplicate downstream side effect, if a handler/idempotency test seam exists

   If full hosted-service testing is cumbersome, test the processor/claiming service directly and keep one smoke test for DI wiring.

11. **Document operational assumptions**
   Add a short note in code comments or README covering:
   - outbox is at-least-once, not exactly-once
   - handlers must be idempotent
   - retry/backoff behavior
   - how terminal failures are surfaced for future escalation/ops work

# Validation steps
Run and report the results of the relevant commands:

```bash
dotnet build
dotnet test
```

Also validate manually in code/tests that:

1. A representative command or transaction writes an outbox row.
2. The dispatcher picks up the row and marks it succeeded.
3. Re-running the dispatcher does not redispatch already-succeeded rows.
4. A simulated transient exception increments attempts and reschedules the message.
5. A simulated permanent exception marks the message as terminally failed/dead-lettered.
6. Concurrent processing path is protected against duplicate claims.

If migrations are used in this repo, ensure the migration is included and the test environment applies it successfully.

# Risks and follow-ups
- **Existing partial implementation risk:** The repo may already have domain events, background jobs, or notification dispatchers. Avoid duplicating concepts; integrate with current patterns.
- **Exactly-once misconception:** Database outbox provides reliable at-least-once delivery, not true exactly-once. Idempotent handlers are required.
- **Concurrency edge cases:** Claiming logic must be atomic and PostgreSQL-safe. Be careful with race conditions around status transitions.
- **Retry storms:** Use bounded backoff and max attempts to avoid hot-looping poison messages.
- **Tenant leakage:** Preserve `company_id` and never dispatch tenant-owned side effects without tenant context.
- **Serialization/versioning:** Message payloads should include stable type/version metadata to avoid future deserialization breaks.
- **Operational visibility:** This task should leave enough metadata for future escalations/exception dashboards under ST-404 and ST-603.
- **Follow-up candidates:** dead-letter inspection UI, escalation records for terminal failures, metrics/health checks for outbox backlog, and broader adoption of outbox for all side-effecting modules.