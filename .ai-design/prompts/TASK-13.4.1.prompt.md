# Goal
Implement backlog task **TASK-13.4.1 — Implement background worker for trigger evaluation and execution dispatch** for story **ST-704 - Trigger evaluation engine and auditability** in the existing **.NET modular monolith**.

Deliver a production-ready background worker that:
- polls on a recurring interval for:
  - due scheduled triggers
  - pending condition checks
- evaluates trigger eligibility in a tenant-safe way
- performs policy checks before orchestration start
- dispatches orchestration starts only once using idempotency protection
- writes business audit records for every execution attempt and outcome
- retries worker failures according to configured retry policy
- records failed and blocked attempts visibly in audit data

Use the existing architecture conventions:
- ASP.NET Core backend
- PostgreSQL as source of truth
- background workers for recurring execution
- tenant-scoped processing
- auditability as domain data, not just logs
- correlation IDs and idempotency keys across execution flow

# Scope
Implement only what is necessary to satisfy this task and its acceptance criteria. Prefer extending existing workflow/trigger/audit infrastructure over inventing parallel patterns.

In scope:
- Background polling worker for trigger evaluation
- Trigger evaluation application service/domain flow
- Idempotency key generation and duplicate prevention for orchestration start
- Policy check invocation before orchestration dispatch
- Audit record creation for:
  - started
  - blocked
  - duplicate skipped
  - failed
  - retry attempts
- Retry handling for transient worker failures
- Configuration for polling interval and retry policy
- Tests covering core behavior

Out of scope unless already partially present and required to wire this task:
- New UI screens
- Full workflow builder changes
- New mobile functionality
- Large schema redesigns unrelated to trigger execution
- Message broker introduction
- Replacing existing orchestration engine patterns

If the codebase already contains partial trigger/workflow/audit abstractions, integrate with them rather than duplicating them.

# Files to touch
Inspect the solution first and adjust file choices to actual project structure. Expected areas:

- `src/VirtualCompany.Application/...`
  - add trigger evaluation service/command handlers/interfaces
  - add policy pre-check orchestration dispatch flow
  - add DTOs/models for execution attempt and audit payloads

- `src/VirtualCompany.Domain/...`
  - add or extend domain entities/value objects for:
    - trigger execution attempt
    - idempotency key
    - execution status
    - denial reason
    - correlation id handling
  - add enums/constants if needed

- `src/VirtualCompany.Infrastructure/...`
  - implement repositories/query services for due triggers and pending condition checks
  - implement background worker / hosted service
  - implement persistence for execution attempts and audit records
  - implement duplicate prevention using DB uniqueness and/or transactional check
  - wire retry policy and configuration
  - register DI

- `src/VirtualCompany.Api/...`
  - configuration binding for worker options
  - startup/Program registration if hosted service lives here or is wired here

- `tests/VirtualCompany.Api.Tests/...` and/or relevant test projects
  - unit tests for evaluation logic
  - integration tests for worker behavior, idempotency, policy blocking, and audit persistence

- `README.md` or relevant docs if there is an established place for operational configuration notes

Also inspect migrations. If persistence changes are required, add a migration in the project’s existing migration location/pattern rather than inventing a new one.

# Implementation plan
1. **Inspect existing trigger/workflow/audit infrastructure**
   - Search for:
     - workflow definitions/instances
     - trigger entities or trigger tables
     - audit event persistence
     - orchestration start entry points
     - policy engine interfaces
     - background worker patterns
     - outbox/retry utilities
   - Reuse existing naming and module boundaries.
   - Identify whether there is already a table for trigger executions or if one must be added.

2. **Define the execution model**
   Implement a clear execution-attempt model for trigger processing. Each attempt should capture at minimum:
   - trigger id
   - trigger type
   - agent id
   - tenant/company id
   - correlation id
   - idempotency key
   - execution status
   - denial reason nullable
   - retry attempt count
   - timestamps
   - failure details nullable

   Suggested statuses:
   - `Pending`
   - `Dispatched`
   - `Blocked`
   - `DuplicateSkipped`
   - `Failed`
   - `Retried`

   If the codebase already has audit/outcome enums, align to them instead of introducing conflicting values.

3. **Add persistence support for idempotent execution attempts**
   Ensure duplicate orchestration starts are prevented even under concurrent polling or retries.

   Preferred approach:
   - persist a trigger execution attempt record before dispatch
   - generate a deterministic idempotency key from stable execution identity, for example:
     - tenant/company id
     - trigger id
     - due occurrence timestamp / scheduled fire timestamp / condition evaluation window
     - agent id
   - enforce uniqueness at the database level on the idempotency key
   - if insert conflicts, treat as duplicate and do not start orchestration again
   - still write an audit record indicating duplicate prevention/skipped dispatch if feasible

   Important:
   - duplicate prevention must not rely only on in-memory locking
   - use transactional boundaries where appropriate

4. **Implement trigger polling query flow**
   Add repository/query methods to fetch:
   - due scheduled triggers
   - pending condition checks

   Requirements:
   - tenant-safe filtering
   - only active/eligible triggers
   - bounded batch size to avoid runaway processing
   - deterministic ordering for stable polling
   - mark/claim records safely if the existing model supports it

   Add worker options such as:
   - polling interval
   - batch size
   - max retries
   - retry backoff

5. **Implement application service for evaluation and dispatch**
   Create an application service, e.g. `TriggerEvaluationService`, responsible for:
   - loading due work
   - evaluating whether a trigger should execute now
   - generating correlation id and idempotency key
   - recording execution attempt
   - invoking policy checks before orchestration start
   - dispatching orchestration if allowed
   - writing audit records for all outcomes
   - updating retry/failure state

   Keep orchestration dispatch behind an interface, e.g. existing orchestration starter abstraction if present.

6. **Run policy checks before orchestration start**
   Integrate with the existing policy/guardrail engine.

   Required behavior:
   - policy checks happen before orchestration start
   - blocked executions do not start orchestration
   - blocked executions create audit records with denial reason
   - denial reason should be structured where possible and human-readable in audit output

   If no suitable policy interface exists, add a minimal application-facing abstraction rather than coupling directly to infrastructure.

7. **Create audit records for every execution**
   Every trigger execution attempt must produce business audit data containing:
   - trigger id
   - trigger type
   - agent id
   - tenant/company id
   - execution status
   - correlation id

   Also include when available:
   - idempotency key
   - denial reason
   - retry attempt
   - failure summary
   - target orchestration/workflow/task reference if dispatch succeeds

   Reuse the existing `audit_events` model if present. If extending it, keep it aligned with the architecture note that auditability is a domain feature.

8. **Implement the background worker**
   Add a hosted background service using the project’s existing worker pattern.

   Worker responsibilities:
   - run on configured polling interval
   - invoke evaluation service
   - handle cancellation correctly
   - log technical failures with correlation/tenant context where available
   - apply retry policy for transient failures
   - ensure failed attempts are reflected in audit records

   Prefer:
   - one polling loop
   - bounded execution per cycle
   - no unbounded fan-out
   - safe shutdown behavior

   If distributed coordination already exists, use it. If not, rely on DB idempotency and keep implementation simple.

9. **Retry behavior**
   Implement retry handling according to configured retry policy.

   Requirements:
   - transient worker failures are retried
   - permanent business/policy failures are not retried as orchestration starts
   - failed attempts are visible in audit records
   - retry count/backoff should be configurable

   Distinguish:
   - policy denial => blocked, not retried as success path
   - duplicate => skipped, not retried
   - transient infra failure => retryable
   - permanent invalid trigger/configuration => failed, likely non-retryable unless existing conventions say otherwise

10. **Configuration and DI**
   Add strongly typed options, e.g.:
   - `TriggerWorkerOptions`
     - `PollingIntervalSeconds`
     - `BatchSize`
     - `MaxRetryAttempts`
     - `RetryBackoffSeconds`

   Register:
   - worker
   - evaluation service
   - repositories
   - policy checker abstraction
   - orchestration dispatcher abstraction

11. **Database changes**
   If needed, add schema support for execution attempts and uniqueness.

   Possible additions:
   - `trigger_execution_attempts` table or equivalent
   - unique index on `idempotency_key`
   - columns for:
     - trigger_id
     - trigger_type
     - company_id
     - agent_id
     - correlation_id
     - execution_status
     - denial_reason
     - retry_count
     - failure_details
     - created_at / updated_at

   If the existing `audit_events` table is the only required persistence, still ensure there is a durable uniqueness mechanism for idempotency.

12. **Testing**
   Add tests for:
   - due scheduled triggers are picked up on polling
   - pending condition checks are picked up on polling
   - policy denial prevents orchestration start
   - denial reason is audited
   - successful execution records audit data
   - duplicate execution attempts do not start orchestration twice
   - retryable failure increments retry behavior and is audited
   - failed attempts remain visible in audit records
   - tenant scoping is preserved

   Prefer unit tests for evaluation logic and integration tests for persistence/idempotency behavior.

Implementation notes:
- Keep code async end-to-end.
- Use UTC timestamps.
- Preserve correlation IDs through logs, audit, and orchestration dispatch.
- Do not expose raw chain-of-thought anywhere.
- Follow existing clean architecture boundaries.
- Avoid direct DB access from orchestration/policy code; use typed contracts.

# Validation steps
1. Restore and inspect solution structure:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation:
   - `dotnet build`
   - `dotnet test`

4. Verify behavior with tests or integration harness:
   - a due scheduled trigger is evaluated and dispatched once
   - a pending condition check is evaluated on polling
   - the same trigger occurrence processed twice results in one orchestration start only
   - blocked policy execution creates audit record with denial reason
   - transient failure causes retry behavior and audit visibility
   - failed execution attempt is visible in persisted audit data

5. If migrations were added:
   - ensure migration applies cleanly using the repo’s existing migration workflow
   - verify unique constraint/index for idempotency key exists

6. Confirm configuration wiring:
   - worker starts with app
   - polling interval and retry settings bind from configuration
   - cancellation/shutdown is graceful

# Risks and follow-ups
- **Unknown existing trigger model**: the repo may already have trigger entities with different naming. Adapt to existing structures instead of forcing a new model.
- **Concurrency across instances**: if multiple app instances run workers, DB-level idempotency is mandatory. Optional future enhancement: distributed lock/claiming via Redis.
- **Audit schema mismatch**: existing audit tables may not yet support all required fields. Extend carefully without breaking current consumers.
- **Retry classification**: transient vs permanent failures may need a shared exception taxonomy. If absent, add a minimal one and document assumptions.
- **Condition check semantics**: “pending condition checks” may need a clearer domain definition if not already modeled. Implement the narrowest viable interpretation consistent with existing backlog/story language.
- **Operational visibility**: consider a follow-up task for admin observability endpoints or dashboard surfacing of worker health and trigger backlog.
- **Outbox integration**: if orchestration dispatch should be outbox-backed for stronger reliability, note this as a follow-up if not feasible within this task.
- **Metrics**: consider follow-up instrumentation for poll cycles, evaluated triggers, blocked executions, duplicates prevented, retries, and failures.