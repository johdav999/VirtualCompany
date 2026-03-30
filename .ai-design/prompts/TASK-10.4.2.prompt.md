# Goal
Implement `TASK-10.4.2` for `ST-404` so the background execution and retry pipeline clearly distinguishes **transient/technical failures** from **permanent policy/business failures**, and only retries when retrying is appropriate.

The coding agent should make this behavior explicit in code, testable, and observable.

Expected outcome:
- Transient failures are classified and retried with bounded policy.
- Permanent failures are not retried and are marked/escalated immediately.
- Blocked/policy/business failures become visible exceptions/escalations rather than silent retry loops.
- The implementation fits the existing modular monolith and .NET background worker architecture.
- Tenant scope, idempotency/correlation, and outbox reliability are preserved.

# Scope
Focus only on the retry-classification and execution behavior needed for `ST-404`.

Include:
- A reusable failure classification model for background/workflow execution.
- Retry policy updates in worker/job execution paths.
- Explicit handling for:
  - transient infrastructure failures
  - timeout/rate-limit/network failures
  - concurrency/lock contention where retry is valid
  - permanent policy denials
  - permanent approval-required/business-rule failures
  - invalid configuration/input failures
- Persistence or surfacing of execution outcome so blocked/failed work is visible for review/escalation.
- Unit/integration tests for classification and retry decisions.
- Structured logging around retry decisions.

Do not expand scope into:
- Full notification UX
- New workflow designer features
- Broad refactors unrelated to background execution
- New external broker adoption
- Mobile/web UI work unless a minimal API/domain hook is required for visibility

If the codebase already has a job runner, workflow runner, outbox dispatcher, or background execution abstraction, extend it rather than introducing a parallel mechanism.

# Files to touch
Inspect the solution first and then touch only the minimum necessary files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - Add domain concepts/enums/value objects for execution failure classification and outcome if missing.
- `src/VirtualCompany.Application/**`
  - Add application-layer retry decision logic / execution result contracts.
  - Update workflow/background command handlers or services.
- `src/VirtualCompany.Infrastructure/**`
  - Update background worker implementations, outbox dispatcher, scheduler, workflow runner, persistence mappings, and logging.
- `src/VirtualCompany.Api/**`
  - Only if health/diagnostic or exception-surfacing endpoints already exist and need wiring.
- `src/VirtualCompany.Shared/**`
  - Only if shared contracts are already used for execution status models.
- Test projects under `tests/**` or existing `*.Tests` projects
  - Add/extend tests for retry classification and worker behavior.

Before editing, identify the concrete files that currently implement:
- background workers
- workflow progression
- retry loops/policies
- outbox dispatch
- task/workflow failure state transitions
- audit/escalation/exception recording

Prefer modifying existing files over creating many new ones.

# Implementation plan
1. **Inspect current execution pipeline**
   - Find the existing abstractions for:
     - background jobs / hosted services
     - workflow runner / scheduler
     - outbox dispatcher
     - task/workflow state transitions
     - policy/business-rule exceptions
   - Map where retries currently happen and whether they are implicit (e.g., catch-all retry) or explicit.

2. **Introduce explicit failure classification**
   - Add a small, central model such as:
     - `ExecutionFailureCategory` or similar enum:
       - `Transient`
       - `PermanentBusiness`
       - `PermanentPolicy`
       - `PermanentConfiguration`
       - `ConcurrencyTransient` or fold into `Transient`
       - `Unknown`
     - optional `ExecutionDisposition`:
       - `Retry`
       - `Fail`
       - `Block/Escalate`
   - Keep naming aligned with existing conventions in the repo.
   - Ensure the model is usable across workflow runner and outbox/background execution.

3. **Standardize exception-to-classification mapping**
   - Implement a classifier service or helper in application/infrastructure that maps known exceptions to categories.
   - Treat as **transient** where appropriate:
     - network failures
     - HTTP 429 / provider throttling
     - timeouts
     - temporary DB connectivity issues
     - Redis unavailable
     - deadlock/lock timeout/concurrency contention that is safe to retry
   - Treat as **permanent non-retryable** where appropriate:
     - policy denied / guardrail denied
     - approval required
     - business rule violation
     - invalid state transition
     - missing required configuration
     - malformed payload / validation failure
     - tenant scope violation / authorization-like invariant failures in worker context
   - For unknown exceptions, prefer a conservative default that avoids infinite retries:
     - bounded retries if current architecture expects this
     - then fail/escalate visibly
   - Do not swallow stack traces; preserve original exception details in technical logs.

4. **Apply classification to retry policy**
   - Update the worker/job execution path so retry decisions are based on classification, not generic exception catch-all behavior.
   - Ensure:
     - transient failures increment attempt count and schedule next retry with backoff
     - permanent failures stop retrying immediately
     - blocked/policy/business failures are marked distinctly from transient exhaustion
   - If there is already a retry metadata model, extend it rather than replacing it.
   - Use bounded retry counts and backoff already present in the system; if absent, add a simple bounded exponential or stepped backoff.

5. **Persist visible execution outcomes**
   - Ensure failed/blocked executions create a visible record for review using existing domain constructs where possible:
     - task status `blocked` or `failed`
     - workflow instance exception/failure state
     - audit event
     - escalation/notification/outbox event if such plumbing already exists
   - Distinguish at least:
     - retry scheduled
     - permanently failed
     - blocked due to policy/approval/business rule
   - Preserve tenant scope and correlation/idempotency identifiers in these records.

6. **Protect outbox-backed side effects**
   - Review outbox dispatcher behavior so transient dispatch failures retry, but permanent payload/configuration failures do not loop forever.
   - Ensure duplicate dispatch is still prevented by existing idempotency/outbox semantics.
   - If the dispatcher currently retries all exceptions forever, fix that.

7. **Add structured logging and diagnostics**
   - Log at decision points with structured fields such as:
     - `CompanyId` / tenant context if available
     - execution/job/workflow/task ID
     - correlation ID
     - attempt number
     - failure category
     - retry decision
     - next retry time if applicable
   - Keep business audit separate from technical logs.

8. **Add tests**
   - Add unit tests for the classifier:
     - transient exception => retry
     - policy/business/config exception => no retry
     - unknown exception => expected bounded behavior
   - Add tests for worker/executor behavior:
     - transient failure schedules retry
     - permanent failure marks failed/blocked and does not retry
     - approval/policy failure surfaces as blocked/escalated
     - outbox dispatch transient vs permanent behavior
   - Prefer existing test patterns/frameworks in the repo.

9. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - domain/application define intent and state transitions
     - infrastructure handles concrete exception mapping for provider/DB/Redis/HTTP exceptions
   - Do not put retry/business classification logic in controllers or UI.

10. **Document assumptions in code comments only where needed**
   - Add concise comments where classification is non-obvious, especially around provider-specific transient detection.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run those first for faster iteration, then full test suite.

4. Verify behavior through tests or existing harnesses for these scenarios:
   - transient infrastructure failure retries with backoff
   - policy denial does not retry
   - approval-required/business-rule failure does not retry and is marked blocked/escalated
   - invalid configuration does not retry
   - unknown exception does not create infinite retry loops
   - outbox transient dispatch retries safely
   - outbox permanent dispatch failure is surfaced and stops retrying

5. Review logs/output to confirm structured retry decision data is emitted.

6. Confirm no tenant-scoping regressions in worker execution paths.

7. If persistence models changed, ensure migrations are added only if truly required by the existing persistence approach.

# Risks and follow-ups
- **Risk: exception taxonomy is currently inconsistent**
  - You may need a thin normalization layer first rather than trying to classify dozens of raw exceptions everywhere.

- **Risk: retries may be implemented in multiple places**
  - Search for all retry loops, Polly policies, hosted services, and dispatcher code to avoid partial fixes.

- **Risk: unknown exceptions could still loop**
  - Ensure there is always a max-attempt guardrail even when classification is uncertain.

- **Risk: business vs policy vs configuration failures may not yet have dedicated exception types**
  - If needed, introduce minimal explicit exception types or result codes, but avoid broad refactors.

- **Risk: visibility/escalation path may be incomplete**
  - Reuse existing task/workflow failure recording if possible; if no exception record exists, add the smallest viable mechanism and note follow-up work.

Follow-ups to note in your final implementation summary if not completed in this task:
- unify exception types across workflow, orchestration, and integration modules
- add richer operator-facing exception dashboards/inbox integration
- add metrics for retry counts, permanent failures, and retry exhaustion
- review all external integration adapters for consistent transient/permanent mapping