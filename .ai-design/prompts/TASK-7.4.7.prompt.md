# Goal
Implement `TASK-7.4.7 — Use outbox for reliable side effects` for `ST-104 Baseline platform observability and operational safeguards` in the existing .NET solution.

The coding agent should introduce a database-backed outbox pattern and background dispatcher so side effects are persisted transactionally and dispatched reliably outside the request path. This should align with the architecture decision to use **PostgreSQL + outbox + background workers** and support operational safeguards for production readiness.

Because no explicit acceptance criteria were provided for this task, derive completion from the story notes and architecture:
- side effects are not executed inline when reliability matters
- side effects are persisted in an outbox table in the same transaction as domain/application state changes
- a background worker dispatches pending outbox messages with retry handling
- dispatch is idempotent or duplication-safe
- failures are logged with correlation context and are retryable
- implementation fits the modular monolith and clean architecture boundaries

# Scope
In scope:
- Add an outbox persistence model in Infrastructure, backed by SQL/PostgreSQL via the app’s existing data access approach.
- Add a reusable application/infrastructure abstraction for writing outbox messages.
- Ensure outbox records can be created atomically with business state changes.
- Add a background hosted service/worker that polls and dispatches pending outbox messages.
- Add retry metadata and safe failure handling.
- Add structured logging around enqueue, dispatch, retry, and terminal failure behavior.
- Add at least one concrete side-effect path to prove the pattern end-to-end, preferably one already implied by the backlog/story notes such as:
  - invitation delivery
  - notification fan-out
  - workflow/event progression
  - audit fan-out
- Add tests for persistence and dispatcher behavior.

Out of scope unless already trivial in the codebase:
- Introducing an external message broker
- Refactoring every existing side effect in the system to use the outbox
- Building a full generic event bus across all modules
- Large UI changes
- Full notification product implementation if not already present

If the repository already contains partial outbox/event infrastructure, extend and standardize it rather than duplicating it.

# Files to touch
Inspect first, then update the most relevant files under these projects:

- `src/VirtualCompany.Domain`
  - any domain event or integration event abstractions if present
- `src/VirtualCompany.Application`
  - command handlers / application services where side effects are initiated
  - interfaces for outbox writing and message dispatch contracts
- `src/VirtualCompany.Infrastructure`
  - DbContext / persistence configuration
  - EF Core entity mappings
  - migrations
  - outbox repository/store implementation
  - background worker / hosted service
  - dispatcher implementations
  - logging and retry behavior
- `src/VirtualCompany.Api`
  - DI registration
  - hosted service registration
  - health/readiness integration if applicable
- `README.md`
  - brief operational note if the repo documents background workers or local setup

Likely concrete file categories:
- `*DbContext*.cs`
- `*ServiceCollection*.cs` or DI bootstrap files
- `*HostedService*.cs` / `*BackgroundService*.cs`
- `*Repository*.cs`
- `*EntityTypeConfiguration*.cs`
- EF migration files
- test files in existing test projects if present

Do not invent new top-level architectural layers if the solution already has established patterns.

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how persistence is implemented:
     - EF Core DbContext?
     - repositories/unit of work?
     - transaction boundaries?
   - Inspect whether the solution already has:
     - domain events
     - integration events
     - background workers
     - notification/invitation services
     - correlation ID logging
   - Reuse existing conventions for namespaces, DI, logging, and migrations.

2. **Design the outbox model**
   Add an outbox table/entity with enough metadata for reliable dispatch. Prefer fields like:
   - `Id`
   - `Type`
   - `Payload`
   - `OccurredAtUtc`
   - `AvailableAtUtc`
   - `Status` or nullable processed fields
   - `AttemptCount`
   - `LastAttemptAtUtc`
   - `ProcessedAtUtc`
   - `Error`
   - `CorrelationId`
   - `CompanyId` if tenant context applies
   - optional `IdempotencyKey`

   Keep payload serialized JSON and type metadata explicit. Use a simple, durable schema over premature abstraction.

3. **Add application/infrastructure abstractions**
   Introduce minimal interfaces such as:
   - outbox writer/store for enqueueing messages
   - outbox dispatcher/handler resolution for processing messages
   - optional serializer abstraction if the codebase already uses one

   Keep the Application layer dependent on abstractions only. Infrastructure should own persistence and worker execution.

4. **Persist outbox messages atomically**
   Ensure outbox messages are saved in the same transaction as the originating state change.
   Preferred approaches:
   - command handler writes business entity changes and outbox message before `SaveChanges`
   - or DbContext intercepts domain events and materializes outbox rows during save

   Choose the simpler option that matches the current codebase. Do not introduce a complex domain-event pipeline if none exists.

5. **Implement a dispatcher worker**
   Add a hosted background service that:
   - polls pending outbox rows in batches
   - acquires them safely to avoid duplicate concurrent processing
   - dispatches each message to a registered handler
   - marks success/failure with timestamps and attempt counts
   - applies retry/backoff for transient failures
   - logs structured details including message id, type, correlation id, and tenant/company context where available

   Keep polling interval and batch size configurable.

6. **Add message handling**
   Implement at least one concrete outbox message and handler. Pick the most natural existing side effect in the repo, for example:
   - send invitation email
   - create notification record/fan-out
   - emit audit side effect
   - continue workflow processing

   The goal is to prove the pattern is used for a real reliable side effect, not just create dead infrastructure.

7. **Handle idempotency and duplication safety**
   Since outbox dispatch can retry, make handlers safe:
   - use natural idempotency checks where possible
   - avoid unsafe duplicate sends without a guard
   - if exact-once is not feasible, implement at-least-once with duplication-safe consumers

   Document assumptions in code comments where needed.

8. **Add migration**
   Create and verify an EF Core migration for the outbox table and any indexes needed, such as:
   - pending/available lookup
   - processed status
   - occurred/available timestamps
   - optional correlation/company filters

9. **Register everything**
   Update DI/bootstrap so:
   - outbox store is registered
   - handlers are registered
   - dispatcher worker is registered
   - configuration options are bound from settings if the project uses options

10. **Add tests**
   Add focused tests for:
   - enqueue persists outbox message with expected metadata
   - dispatcher processes pending messages
   - failed dispatch increments attempts and preserves error details
   - retryable messages remain available for later processing
   - concrete side-effect handler is invoked once in the happy path
   - idempotency/duplicate safety where practical

11. **Keep observability aligned with ST-104**
   Ensure logs are structured and operationally useful:
   - enqueue event
   - dispatch started
   - dispatch succeeded
   - dispatch failed
   - retry scheduled / next availability
   - poison/terminal failure if implemented

   If the app already has correlation ID middleware or tenant context enrichment, flow that data into outbox records and logs.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and is included correctly.

4. Manually validate the end-to-end path for the chosen side effect:
   - trigger the originating command/action
   - confirm business data is committed
   - confirm an outbox row is created in pending state
   - run/observe the background worker
   - confirm the side effect is dispatched
   - confirm the outbox row is marked processed

5. Validate failure behavior:
   - simulate handler failure
   - confirm attempt count increments
   - confirm error details are stored/logged
   - confirm message remains retryable or is rescheduled

6. Validate duplication safety:
   - simulate worker restart or repeated processing attempt
   - confirm the side effect does not produce unsafe duplicate outcomes

7. Check logs for:
   - correlation id presence where available
   - tenant/company context where applicable
   - clear operational messages for retries/failures

# Risks and follow-ups
- **Existing architecture mismatch:** If the repo lacks background worker infrastructure or uses a non-EF persistence pattern, adapt the implementation to local conventions rather than forcing a new pattern.
- **Transaction boundary ambiguity:** Be careful that outbox writes occur in the same transaction as business state changes; otherwise reliability is not achieved.
- **Concurrent dispatch races:** Multiple app instances can process the same rows unless row claiming/locking is implemented correctly.
- **Handler duplication risk:** Side-effect handlers must be idempotent or duplication-safe because retries are expected.
- **Scope creep:** Do not refactor all side effects in one task unless the codebase is tiny and the change is low risk.
- **Operational tuning:** Poll interval, batch size, retry backoff, and poison-message handling may need follow-up tuning.
- **Future follow-up:** Later stories should migrate more side effects to the outbox pattern, add richer dead-letter handling, and potentially evolve to a broker if throughput demands it.