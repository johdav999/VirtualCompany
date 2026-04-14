# Goal
Implement `TASK-13.4.2` for `ST-704 - Trigger evaluation engine and auditability` by adding a policy-aware orchestration entry point for trigger-initiated runs in the .NET modular monolith.

The implementation must ensure that background trigger evaluation can safely and idempotently start orchestration only after policy checks, while producing durable audit records for all execution outcomes, including blocked, retried, failed, and successful attempts.

# Scope
Deliver the minimum complete implementation needed to satisfy these acceptance criteria:

- A background worker evaluates:
  - due scheduled triggers
  - pending condition checks
  - on a recurring polling interval
- Each trigger execution attempt records an idempotency key and prevents duplicate orchestration starts
- Every trigger execution produces audit records containing:
  - trigger id
  - trigger type
  - agent id
  - tenant/company id
  - execution status
  - correlation id
- Policy checks run before orchestration start
- Blocked executions are recorded with denial reason
- Worker failures are retried according to configured retry policy
- Failed attempts are visible in audit records

Constrain the work to the existing architecture:
- ASP.NET Core modular monolith
- background workers
- PostgreSQL persistence
- shared orchestration subsystem
- tenant-scoped execution
- auditability as business persistence, not just logs

Do not build unrelated UI. Prefer application/domain/infrastructure changes plus tests.

# Files to touch
Target the existing solution structure and adjust exact paths based on what already exists.

Likely areas to modify:

- `src/VirtualCompany.Domain/`
  - trigger execution domain entities/value objects/enums
  - audit-related domain models if owned here
- `src/VirtualCompany.Application/`
  - trigger evaluation application services
  - orchestration entry point contract/command
  - policy-aware trigger execution coordinator
  - retry/idempotency abstractions
- `src/VirtualCompany.Infrastructure/`
  - EF Core/PostgreSQL persistence for trigger execution attempts and audit records
  - background worker implementation
  - repository implementations
  - distributed locking/idempotency persistence if applicable
- `src/VirtualCompany.Api/`
  - DI registration / hosted service wiring if hosted here
- `tests/VirtualCompany.Api.Tests/`
  - integration tests for worker polling, idempotency, audit persistence, retry behavior
- `tests/` adjacent application/infrastructure test projects if present
- migration location if this repo uses migrations in-source
  - check conventions from `README.md`
  - check `docs/postgresql-migrations-archive/README.md`

Also inspect and reuse existing code for:
- orchestration service interfaces
- policy engine interfaces
- audit event persistence
- workflow/task trigger models
- correlation ID propagation
- background worker patterns
- retry policy configuration

# Implementation plan
1. Inspect the current codebase before changing anything
   - Find existing models for workflows, triggers, scheduled jobs, approvals, policy checks, audit events, and orchestration
   - Identify whether trigger concepts already exist under workflow/task modules
   - Reuse existing naming and module boundaries
   - Confirm where hosted background workers are registered
   - Confirm whether EF Core migrations are active in repo or managed elsewhere

2. Introduce a dedicated trigger execution attempt model if missing
   - Add a persistence-backed record/table for trigger execution attempts, or extend an existing trigger run table
   - Required fields:
     - `Id`
     - `CompanyId` / tenant id
     - `TriggerId`
     - `TriggerType`
     - `AgentId`
     - `ExecutionStatus`
     - `CorrelationId`
     - `IdempotencyKey`
     - `DenialReason` nullable
     - `AttemptCount` or retry metadata
     - timestamps such as `CreatedAt`, `StartedAt`, `CompletedAt`, `LastError`
   - Add a unique constraint/index on `IdempotencyKey` to prevent duplicate orchestration starts
   - Model statuses clearly, e.g.:
     - `Pending`
     - `PolicyBlocked`
     - `Started`
     - `Completed`
     - `Failed`
     - `RetryScheduled`
     - `DuplicateIgnored`

3. Add a policy-aware orchestration entry point for trigger-initiated runs
   - Create or extend an application service such as:
     - `ITriggerInitiatedOrchestrationService`
     - `TriggerInitiatedOrchestrationService`
   - Responsibilities:
     - accept trigger execution context
     - compute/accept correlation id
     - compute deterministic idempotency key per execution attempt/window
     - persist attempt record before orchestration start
     - execute policy checks before invoking orchestration
     - if denied:
       - mark execution as blocked
       - persist denial reason
       - write audit record
       - do not start orchestration
     - if allowed:
       - invoke shared orchestration engine through existing application boundary
       - mark execution outcome
       - write audit record
   - Keep orchestration invocation behind existing contracts; do not couple worker directly to low-level orchestration internals

4. Implement recurring background polling worker
   - Add a hosted background service that runs on configured interval
   - On each poll:
     - resolve due scheduled triggers
     - resolve pending condition checks
     - process them tenant-safely
   - Prefer batching with bounded concurrency
   - If distributed coordination already exists, use it
   - If not, keep execution safe through idempotency and, if appropriate, a lightweight lock/claim update pattern
   - Ensure cancellation token support and structured logging

5. Add trigger claiming / due-item selection logic
   - Implement repository/query methods to fetch due triggers and pending condition checks
   - Ensure the same due item is not repeatedly started concurrently by multiple worker loops
   - Use one or more of:
     - row claim/update with status transition
     - `FOR UPDATE SKIP LOCKED` pattern if already used
     - unique idempotency key as final duplicate prevention
   - Keep all queries tenant-aware

6. Add audit persistence for all trigger executions
   - Reuse existing audit module/tables if present
   - Ensure every execution attempt writes a business audit record with at least:
     - trigger id
     - trigger type
     - agent id
     - tenant/company id
     - execution status
     - correlation id
   - Include denial reason for blocked executions
   - Include failure details in a safe operational form for failed attempts
   - Distinguish business audit from technical logs

7. Implement retry behavior for worker failures
   - Use configured retry policy for transient failures
   - Do not retry policy denials as transient worker failures
   - Persist failed attempts and retry scheduling/result in audit records
   - If a retry library or existing retry abstraction exists, reuse it
   - If not, implement a simple bounded retry strategy driven by configuration:
     - max attempts
     - delay/backoff
   - Make sure retries do not create duplicate orchestration starts because of idempotency enforcement

8. Wire configuration
   - Add strongly typed options for:
     - polling interval
     - batch size
     - max concurrency if needed
     - retry count/backoff
   - Register worker and services in DI
   - Keep defaults conservative

9. Add tests
   - Unit tests for:
     - idempotency key generation behavior
     - policy-denied path blocks orchestration and writes denial audit
     - duplicate attempt path does not start orchestration twice
     - failure path records failed audit
   - Integration tests for:
     - worker picks up due scheduled triggers on polling interval
     - worker picks up pending condition checks
     - successful trigger starts orchestration once
     - duplicate processing attempts are prevented by idempotency
     - blocked trigger writes audit with denial reason
     - transient failure retries according to policy and remains visible in audit
   - Prefer repository/database-backed tests where uniqueness and persistence behavior matter

10. Keep implementation aligned with backlog and architecture
   - Tenant-isolated data and execution context
   - policy checks before orchestration start
   - auditability as persisted business records
   - shared orchestration engine entry point, not bespoke trigger logic per agent

# Validation steps
1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. Verify database/migration correctness
   - Ensure any new schema for trigger execution attempts and indexes is applied according to repo conventions
   - Confirm unique idempotency constraint exists and is exercised by tests

4. Manually validate key scenarios through tests or local execution
   - due scheduled trigger is detected by worker and processed
   - pending condition check is detected by worker and processed
   - policy-denied trigger does not start orchestration
   - denied execution persists denial reason in audit
   - duplicate processing attempt does not start orchestration twice
   - transient failure retries and failed attempts remain auditable
   - correlation id is persisted through execution/audit path

5. Confirm non-functional expectations
   - cancellation token respected in worker loop
   - tenant/company id is present in persisted records
   - technical logs do not replace business audit records

# Risks and follow-ups
- Existing trigger model may be incomplete or named differently; adapt to current domain instead of forcing new abstractions
- If there is no current trigger persistence, you may need a minimal due-trigger query model to support scheduled and condition-based polling
- Retry semantics can become ambiguous if business failures and transient infrastructure failures are not clearly separated; keep status modeling explicit
- If multiple app instances run workers, idempotency alone may prevent duplicate starts but not duplicate work attempts; consider follow-up distributed claim/lock hardening if not already present
- Audit schema may already exist but may not include all required fields; extend carefully without breaking existing consumers
- Correlation ID propagation may be inconsistent across worker/orchestration/audit boundaries; standardize in this task where touched
- Follow-up work may be needed for:
  - richer trigger execution history views
  - operator dashboards for failed/blocked trigger runs
  - dead-letter handling for permanently failed trigger evaluations
  - metrics/observability around polling lag, retry counts, and duplicate suppression