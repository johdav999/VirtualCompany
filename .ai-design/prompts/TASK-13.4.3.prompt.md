# Goal
Implement backlog task **TASK-13.4.3 — Create audit event writer for trigger evaluations and executions** for story **ST-704 - Trigger evaluation engine and auditability** in the existing .NET modular monolith.

Deliver a production-ready implementation that ensures the trigger evaluation worker and orchestration start path emit **business audit records** for all trigger evaluation/execution outcomes, including retries, policy denials, failures, and successful starts, while preserving **tenant scope**, **correlation IDs**, and **idempotency**.

The implementation prompt should guide the coding agent to:
- add or extend the domain/application/infrastructure pieces needed to write audit events for trigger evaluations and executions
- integrate audit writing into the background worker flow
- ensure blocked, failed, retried, duplicate-prevented, and successful execution attempts are visible in audit records
- keep business auditability separate from technical logging

# Scope
In scope:
- Background worker path that evaluates:
  - due scheduled triggers
  - pending condition checks
- Audit event writing for:
  - evaluation started
  - evaluation skipped / not due / no-op if applicable
  - execution attempt created
  - duplicate/idempotent execution prevented
  - policy denied / blocked before orchestration start
  - orchestration start requested / started
  - transient failure and retry attempt
  - terminal failure
  - successful completion of the trigger execution handoff/start
- Idempotency key persistence and duplicate prevention integration where needed for auditability
- Correlation ID propagation through worker → evaluation → policy check → orchestration start → audit write
- Tenant-aware persistence
- Tests covering acceptance criteria

Out of scope unless required by existing code structure:
- New UI pages for audit viewing
- Reworking the entire scheduler architecture
- Replacing existing logging/telemetry
- Large schema redesigns unrelated to trigger execution auditability
- Mobile/web changes

Implementation constraints:
- Follow existing solution structure and conventions first
- Prefer extending existing audit/event abstractions if present rather than inventing parallel infrastructure
- Keep the design CQRS-lite and modular
- Do not expose chain-of-thought; store concise operational rationale/denial reasons only
- Use PostgreSQL persistence patterns already present in the repo
- Preserve multi-tenant isolation on all reads/writes

# Files to touch
Inspect the repository first and then update the relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - trigger execution domain entities/value objects if they exist
  - audit event domain models/enums
  - execution status / outcome enums
- `src/VirtualCompany.Application/**`
  - background worker orchestration services
  - trigger evaluation handlers/services
  - policy check integration points
  - audit writer interface/application service
  - idempotency coordination abstractions
- `src/VirtualCompany.Infrastructure/**`
  - audit event persistence implementation
  - repositories / EF Core configurations / Dapper queries
  - worker implementations
  - retry policy integration
  - correlation/idempotency persistence
  - migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - DI registration if application/infrastructure services need wiring
- `tests/VirtualCompany.Api.Tests/**`
  - integration or application-level tests for worker/audit behavior
- Potential docs if needed:
  - `README.md`
  - migration documentation if a new migration is added

If schema changes are needed, likely touch:
- audit event table mapping/configuration
- trigger execution attempt table or equivalent persistence
- migration files under the project’s existing migration location

Before coding, identify the actual concrete files/classes in the repo that correspond to:
- background workers
- trigger scheduling/evaluation
- audit event persistence
- orchestration start
- policy checks
- retry handling

# Implementation plan
1. **Discover the current implementation**
   - Search the solution for:
     - trigger evaluation / scheduler / recurring worker code
     - audit event entities and repositories
     - orchestration start services
     - policy guardrail services
     - correlation ID handling
     - retry policy handling
     - idempotency key or execution attempt concepts
   - Determine whether there is already:
     - an `audit_events` entity/table
     - a trigger execution record/table
     - a worker loop for scheduled triggers and condition checks
     - an orchestration command/service to start work

2. **Model the auditable trigger execution lifecycle**
   - Introduce or extend a clear lifecycle model for trigger execution attempts.
   - Ensure each attempt has:
     - `triggerId`
     - `triggerType`
     - `agentId`
     - `tenantId` or `companyId`
     - `executionStatus`
     - `correlationId`
     - `idempotencyKey`
     - optional `denialReason`
     - optional `failureReason`
     - retry metadata if available (`attemptNumber`, `nextRetryAt`, etc.)
   - Use enums/constants for statuses to avoid string drift. Suggested statuses:
     - `EvaluationStarted`
     - `EvaluationSkipped`
     - `ExecutionAttempted`
     - `DuplicatePrevented`
     - `PolicyDenied`
     - `OrchestrationStarted`
     - `RetryScheduled`
     - `ExecutionFailed`
   - If the existing audit model is generic, map these to existing `action`, `outcome`, and metadata fields rather than creating a separate audit system.

3. **Add or extend an audit writer abstraction**
   - Create or extend an application-facing service such as `ITriggerAuditEventWriter` or equivalent.
   - It should provide focused methods for the trigger lifecycle, for example:
     - write evaluation started
     - write execution attempt
     - write duplicate prevented
     - write policy denied
     - write orchestration started
     - write retry scheduled
     - write execution failed
   - The abstraction should accept a strongly typed context object rather than many primitive parameters.
   - Keep it business-audit oriented, not a wrapper around technical logs.

4. **Implement infrastructure persistence**
   - Persist audit records into the existing audit event store.
   - Ensure records include the acceptance-criteria-required fields:
     - trigger id
     - trigger type
     - agent id
     - tenant id
     - execution status
     - correlation id
   - Include structured metadata JSON if the existing schema supports it, for:
     - idempotency key
     - denial reason
     - retry attempt number
     - exception summary / failure category
     - worker name / source
   - If the current schema cannot represent these fields, add the minimum necessary schema changes and migration.
   - Keep the schema additive and backward-compatible.

5. **Integrate audit writing into the background worker**
   - In the recurring polling worker:
     - when due scheduled triggers are loaded, evaluate them under tenant scope
     - when pending condition checks are loaded, evaluate them under tenant scope
   - At the relevant points, write audit events:
     - before evaluation begins
     - when an execution attempt is created
     - when a duplicate is prevented by idempotency
     - after policy denial
     - after orchestration start succeeds
     - when a failure occurs
     - when retry is scheduled
   - Avoid duplicate audit spam for the same exact state transition unless retries are distinct attempts.

6. **Implement or complete idempotency handling**
   - Ensure an idempotency key is generated or derived per trigger execution attempt.
   - The key should be stable enough to prevent duplicate orchestration starts for the same logical attempt.
   - Before starting orchestration:
     - persist/check the idempotency key
     - prevent duplicate starts
     - emit an audit event indicating duplicate prevention when applicable
   - If an execution-attempt table already exists, use it as the source of truth.
   - If not, add a minimal persistence mechanism with a uniqueness constraint where appropriate.

7. **Run policy checks before orchestration start**
   - Ensure the worker path invokes policy checks before orchestration start.
   - If policy denies execution:
     - do not start orchestration
     - persist an audit event with blocked/denied status
     - include denial reason in a safe, concise form
   - Distinguish policy/business denials from transient technical failures so retry behavior remains correct.

8. **Integrate retry-aware audit behavior**
   - For worker failures:
     - classify transient vs permanent failures using existing retry policy conventions
     - on transient failure, emit an audit event showing retry visibility
     - on terminal failure, emit a failed audit event
   - Include attempt count and failure summary where possible.
   - Ensure retries do not create duplicate orchestration starts due to idempotency enforcement.

9. **Preserve correlation and tenant context**
   - Propagate correlation ID through:
     - worker polling cycle
     - trigger evaluation
     - policy check
     - orchestration start
     - audit persistence
   - Ensure all audit writes include tenant/company ID and do not cross tenant boundaries.
   - If correlation IDs are generated in the worker, reuse them downstream.

10. **Testing**
   - Add tests that prove:
     - due scheduled triggers are evaluated on polling interval path
     - pending condition checks are evaluated on polling interval path
     - idempotency key prevents duplicate orchestration starts
     - every trigger execution outcome writes an audit record with required fields
     - policy-denied executions are recorded with denial reason
     - transient worker failures are retried and visible in audit records
     - terminal failures are visible in audit records
   - Prefer integration-style tests where persistence behavior matters.

11. **Implementation quality bar**
   - Keep methods small and intention-revealing.
   - Use existing DI and options patterns.
   - Add comments only where behavior is non-obvious.
   - Do not introduce speculative abstractions beyond what this task needs.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly using the project’s existing migration workflow.

4. Manually verify in code/tests that the acceptance criteria are satisfied:
   - recurring worker evaluates due scheduled triggers and pending condition checks
   - idempotency key is recorded per trigger execution attempt
   - duplicate orchestration starts are prevented
   - audit records exist for all trigger executions and include:
     - trigger id
     - trigger type
     - agent id
     - tenant id
     - execution status
     - correlation id
   - policy checks happen before orchestration start
   - blocked executions are recorded with denial reason
   - worker failures follow retry policy
   - failed attempts are visible in audit records

5. If there is an integration test harness or test database setup, add assertions against persisted audit rows rather than only mocked calls.

6. Include in the final coding summary:
   - files changed
   - schema changes/migrations added
   - how idempotency is enforced
   - how retry visibility is represented in audit records
   - any assumptions made due to current repo structure

# Risks and follow-ups
- **Repo mismatch risk:** The exact trigger engine/audit infrastructure may not yet exist. If so, implement the smallest cohesive slice needed and clearly note assumptions.
- **Schema ambiguity:** The provided architecture snippet shows an incomplete `audit_events` schema. Reuse existing repo schema first; only add fields if truly necessary.
- **Duplicate event noise:** Writing too many audit records per poll cycle can reduce usefulness. Focus on meaningful state transitions.
- **Retry semantics:** Be careful not to retry policy denials or other permanent business failures.
- **Idempotency race conditions:** If multiple workers can evaluate the same trigger, enforce uniqueness at persistence level, not only in memory.
- **Correlation consistency:** If correlation IDs are inconsistently generated today, standardize within this flow without broad unrelated refactors.
- **Follow-up candidates:**
  - audit query/read model enhancements for UI consumption
  - richer trigger execution history entity if current model is too generic
  - metrics/dashboard counters for trigger success/denial/failure rates
  - distributed lock hardening for multi-instance worker execution