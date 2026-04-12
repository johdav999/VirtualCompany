# Goal
Implement `TASK-12.3.7` for `ST-603 — Alerts, notifications, and approval inbox` by ensuring notification fan-out is removed from synchronous request handling and routed through a reliable database-backed outbox plus background dispatcher.

The coding agent should make notification creation and delivery initiation transactional with the business change, but defer actual fan-out and dispatch to background processing. The result should align with the architecture decision to use a database-backed outbox for reliable side effects in the modular monolith.

# Scope
In scope:

- Identify current request-path notification fan-out for alerts/approvals/inbox-related flows.
- Refactor those flows so HTTP handlers / application command handlers only:
  - persist the business state change,
  - persist notification records as needed,
  - enqueue outbox messages in the same transaction.
- Implement or extend background processing that:
  - reads pending outbox messages,
  - materializes or dispatches notification fan-out,
  - marks work complete idempotently,
  - retries safely on transient failures.
- Preserve tenant scoping, correlation IDs, and auditability.
- Add tests covering transactional enqueue, async dispatch, and idempotent retry behavior.

Out of scope unless required by existing code structure:

- Building the full notification inbox UX.
- Adding external brokers.
- Adding email/push delivery channels beyond existing in-app notification behavior.
- Broad redesign of unrelated outbox infrastructure.

# Files to touch
Likely areas to inspect and update:

- `src/VirtualCompany.Application`
  - notification-related commands/handlers
  - approval/escalation/workflow failure handlers that currently trigger notifications
  - abstractions for outbox publishing
- `src/VirtualCompany.Domain`
  - notification domain entities/value objects/events if present
  - outbox message contracts if domain-owned
- `src/VirtualCompany.Infrastructure`
  - EF Core persistence for notifications and outbox
  - background worker / hosted service for outbox dispatch
  - repositories
  - migrations if outbox or notification tables need schema changes
- `src/VirtualCompany.Api`
  - DI registration for dispatcher/worker if hosted there
  - request handlers only if they currently perform direct fan-out
- `tests/VirtualCompany.Api.Tests`
  - integration tests around approval/notification-triggering endpoints
- Potentially:
  - `README.md` or relevant docs if operational behavior needs documenting
  - `docs/postgresql-migrations-archive/README.md` only if migration conventions need to be followed

Before editing, inspect the solution for:
- existing outbox implementation,
- hosted/background services,
- notification entity/table,
- approval inbox APIs,
- direct notification service calls from controllers or command handlers.

# Implementation plan
1. **Discover current notification flow**
   - Search for notification-related types and usages:
     - `Notification`
     - `Approval`
     - `Inbox`
     - `Outbox`
     - `BackgroundService`
     - `IHostedService`
     - `Dispatch`
     - `Publish`
   - Identify all places where request handling directly fans out notifications or invokes delivery logic synchronously.
   - Map which business actions should emit notification work items:
     - approval created,
     - escalation created,
     - workflow failure/exception,
     - briefing availability if already implemented.

2. **Align with existing architecture**
   - If an outbox pattern already exists, extend it rather than introducing a parallel mechanism.
   - If none exists, implement a minimal database-backed outbox consistent with the architecture:
     - outbox table/entity with message id, tenant/company id, type, payload, status, attempts, timestamps, correlation id.
     - application-facing publisher abstraction.
     - background dispatcher that polls and processes pending messages.

3. **Refactor request-path fan-out**
   - Remove direct dispatch/fan-out from controllers and synchronous application handlers.
   - Ensure business transaction writes:
     - the triggering business entity change,
     - notification record(s) if notification records are part of the domain model,
     - one or more outbox entries representing deferred notification work.
   - Keep these writes atomic in the same DB transaction / unit of work.

4. **Define outbox message contract(s)**
   - Introduce explicit message types for notification fan-out, for example:
     - `ApprovalNotificationRequested`
     - `EscalationNotificationRequested`
     - `WorkflowFailureNotificationRequested`
   - Payload should include only the identifiers and metadata needed for background processing:
     - company/tenant id,
     - target entity id,
     - notification type,
     - recipient resolution inputs,
     - correlation id / causation id if available.
   - Avoid embedding large denormalized payloads unless necessary.

5. **Implement background dispatcher**
   - Add or extend a hosted worker in Infrastructure that:
     - fetches pending outbox messages in batches,
     - locks/claims them safely,
     - deserializes payload,
     - performs notification fan-out/materialization,
     - marks success/failure with retry metadata.
   - Ensure tenant-aware processing and structured logging with correlation IDs.
   - Use idempotent processing:
     - dedupe by outbox message id,
     - or enforce unique constraints on generated notification records,
     - or check whether target notifications already exist before insert.

6. **Implement notification fan-out logic off-request-path**
   - Move recipient resolution and per-user notification creation into the background processor or a dedicated async application service.
   - Prioritize approval and exception alerts if the model supports priority/sort fields.
   - Preserve notification state model such as unread/read/actioned if already present.
   - If notification records are already created synchronously and only delivery is deferred, keep that model; otherwise prefer creating recipient-specific notifications in the dispatcher if that is the actual fan-out concern. Match the existing design.

7. **Persistence and migration updates**
   - Add/update schema for outbox if missing.
   - Add indexes needed for polling and retry:
     - status + available timestamp,
     - created timestamp,
     - company id if useful.
   - If needed, add uniqueness constraints to support idempotency on notification materialization.
   - Follow repository migration conventions already used in the solution.

8. **Error handling and retries**
   - Distinguish transient failures from permanent payload/business failures.
   - Increment attempt count and schedule retry/backoff for transient issues.
   - Mark poison/permanent failures clearly for operator visibility.
   - Do not let one failed message block the batch.

9. **Tests**
   - Add tests that verify:
     - triggering endpoint/command no longer performs direct fan-out inline,
     - business action persists successfully with outbox entry created,
     - background dispatcher processes outbox and creates/distributes notifications,
     - duplicate processing does not create duplicate notifications,
     - transient failure retries work,
     - tenant scoping is preserved.
   - Prefer integration tests where practical, especially around transaction boundaries.

10. **Keep changes focused**
   - Do not redesign the whole notification subsystem.
   - Do not introduce external messaging infrastructure.
   - Keep naming and layering consistent with the existing solution.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify targeted tests for this task:
   - approval creation results in outbox entry within same transaction
   - no synchronous notification fan-out occurs in request path
   - background dispatcher processes pending outbox messages
   - retries are safe and idempotent
   - duplicate dispatch attempts do not duplicate notifications

4. Manual code validation:
   - Confirm controllers/application handlers do not directly call notification fan-out logic.
   - Confirm outbox writes happen in same unit of work as the triggering business change.
   - Confirm dispatcher is registered and runnable in the host.
   - Confirm logs include tenant/correlation context where available.

5. If migrations were added:
   - ensure migration compiles and is included correctly
   - verify schema names/table mappings match project conventions

# Risks and follow-ups
- **Existing architecture mismatch:** The repo may already have partial outbox infrastructure with different conventions. Reuse it instead of layering a second pattern.
- **Transaction boundary issues:** If notifications are currently emitted after `SaveChanges`, refactoring may require careful unit-of-work changes to guarantee atomicity.
- **Idempotency gaps:** Background retries can create duplicate notifications unless uniqueness or dedupe is explicit.
- **Recipient resolution complexity:** Approval/escalation recipients may depend on roles or dynamic membership queries; ensure async fan-out still uses correct tenant-scoped authorization data.
- **Operational visibility:** Failed outbox messages may need admin diagnostics later if not already present.
- **Follow-up candidates:**
  - operator dashboard for failed outbox messages,
  - configurable retry/backoff policy,
  - eventual broker abstraction if throughput grows,
  - metrics for outbox lag and notification dispatch latency.