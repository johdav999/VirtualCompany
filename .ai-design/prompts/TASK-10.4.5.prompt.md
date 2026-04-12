# Goal
Implement `TASK-10.4.5` for `ST-404 — Escalations, retries, and long-running background execution` by introducing and consistently propagating **idempotency keys** and **correlation IDs** across retryable background execution paths.

The implementation should ensure that:
- retryable background work can be safely re-attempted without duplicating side effects
- each execution flow can be traced end-to-end through logs, persistence, and dispatched side effects
- tenant-scoped background processing remains safe and observable
- the design fits the existing modular monolith, ASP.NET Core, PostgreSQL, Redis, and outbox-based architecture

No explicit acceptance criteria were provided for this task, so derive completion from the story notes and architecture:
- use idempotency keys/correlation IDs for retries
- preserve reliable outbox-backed side effects without duplication
- keep worker execution tenant-scoped
- distinguish transient retry behavior from permanent failures where possible

# Scope
In scope:
- add a reusable application/infrastructure pattern for generating, carrying, and persisting correlation IDs and idempotency keys for background execution
- update relevant background job/workflow execution entry points to require or create correlation IDs
- ensure retry attempts reuse the same idempotency key for the same logical operation
- ensure outbox dispatch and/or side-effect-producing worker paths can detect duplicate processing
- add structured logging enrichment with tenant context + correlation ID for worker execution
- add tests covering retry/idempotency behavior and correlation propagation

Out of scope unless already trivially adjacent in the codebase:
- broad redesign of the job system
- introducing a new external message broker
- full distributed tracing platform setup
- changing unrelated HTTP request idempotency behavior
- large schema redesign beyond what is minimally needed to support durable idempotency/correlation tracking

# Files to touch
Inspect the solution first and then update the actual files that match the existing implementation. Likely areas include:

- `src/VirtualCompany.Application/**`
  - background job/workflow command handlers
  - task/workflow orchestration services
  - outbox dispatch abstractions
  - execution context models/interfaces

- `src/VirtualCompany.Infrastructure/**`
  - background worker implementations
  - persistence for job execution / outbox / retry metadata
  - logging setup and correlation propagation
  - Redis coordination helpers if present
  - EF Core entity configurations and migrations if needed

- `src/VirtualCompany.Api/**`
  - shared correlation middleware or logging enrichment if worker/shared bootstrap lives here
  - DI registration for correlation/idempotency services

- `src/VirtualCompany.Domain/**`
  - value objects or domain models for execution identity if the domain layer already owns these concepts

- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for retry-safe execution and duplicate suppression

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If schema changes are required, place them in the project’s current migration mechanism rather than the archive folder unless the repository conventions explicitly say otherwise.

# Implementation plan
1. **Discover current execution and retry model**
   - Inspect how background jobs, workflow progression, scheduled tasks, and outbox dispatch currently run.
   - Identify:
     - where retries happen
     - where side effects are emitted
     - whether there is already an execution context, request context, or correlation abstraction
     - whether outbox records already have unique business identifiers or dispatch markers
   - Document the concrete execution paths you are changing in code comments or commit structure.

2. **Define a small shared execution identity model**
   - Introduce a reusable model/abstraction for:
     - `CorrelationId`: identifies the end-to-end logical execution flow
     - `IdempotencyKey`: identifies a retry-safe logical operation or side effect
     - `CompanyId`/tenant context association
   - Prefer simple strongly typed wrappers or a compact record/class if the codebase already uses primitives heavily.
   - Rules:
     - a new logical workflow/job gets a new correlation ID
     - retries of the same logical operation keep the same correlation ID
     - retries of the same side-effecting operation keep the same idempotency key
     - child operations may derive nested keys, but do not generate random new keys on every retry

3. **Add generation and propagation services**
   - Create a service/helper responsible for:
     - creating correlation IDs when absent
     - deriving deterministic idempotency keys for retryable operations
     - restoring them from persisted job/outbox/workflow state
   - Prefer deterministic composition from stable identifiers where possible, for example:
     - workflow instance ID + step name + action type
     - task ID + tool/action name + attempt-independent operation name
     - outbox message ID as the idempotency key for dispatch
   - Avoid using retry attempt count in the idempotency key.

4. **Persist execution identity where durability is required**
   - Update the relevant persistence model(s) so correlation IDs and/or idempotency keys survive process restarts and retries.
   - Likely candidates:
     - workflow instance execution metadata/context JSON
     - task execution metadata
     - outbox records
     - tool execution records
     - job lease/coordination records if they exist
   - If adding columns, keep them nullable only if needed for backward compatibility, then populate on write for all new records.
   - Add indexes/uniqueness constraints where duplicate suppression depends on the database.
   - Prefer database-enforced uniqueness for side-effect deduplication when practical.

5. **Make outbox/side effects retry-safe**
   - Ensure the outbox dispatcher or equivalent side-effect path uses a stable idempotency key.
   - If the dispatcher can re-run the same message, it must not create duplicate downstream effects in local state.
   - If there is a notification/integration dispatch table, consider storing:
     - correlation ID
     - idempotency key
     - first processed at
     - last attempted at
     - status/error
   - If the outbox record itself is the durable unit, use its ID as the idempotency anchor and make processing updates atomic.

6. **Update workflow/background execution paths**
   - For each relevant worker path:
     - load or create correlation ID at execution start
     - attach tenant context
     - derive stable idempotency keys for side-effecting sub-operations
     - log all retries with the same correlation ID
   - Ensure transient retries do not create duplicate business records, duplicate notifications, or duplicate workflow progression.
   - Ensure permanent business/policy failures are not endlessly retried and still retain correlation context for escalation/debugging.

7. **Enrich structured logging**
   - Add logging scopes/enrichment so worker logs include:
     - `CorrelationId`
     - `CompanyId` or tenant identifier
     - logical job/workflow/task identifiers
     - retry attempt if available
   - Keep technical logs separate from business audit events.
   - Do not add noisy logs everywhere; focus on execution boundaries, retries, duplicate suppression, and final outcomes.

8. **Thread correlation through application boundaries**
   - Where commands/events/messages are passed internally, include correlation ID in the message/command context if the architecture supports it.
   - Ensure downstream handlers reuse the incoming correlation ID rather than generating a new one.
   - For newly initiated background work from HTTP or UI-triggered flows, preserve the originating correlation ID if one already exists; otherwise create one.

9. **Add tests**
   - Add or update tests to cover at minimum:
     - retrying the same logical background operation does not duplicate the side effect
     - the same correlation ID is reused across retries
     - a new logical operation gets a different correlation ID
     - tenant context remains attached during worker execution
     - duplicate outbox dispatch attempts are safely ignored or treated idempotently
   - Prefer integration-style tests where persistence behavior matters.

10. **Keep implementation aligned with existing conventions**
   - Follow the repository’s current layering and naming patterns.
   - Do not invent a parallel job framework if one already exists.
   - Keep the implementation minimal but durable enough for M3 workflow reliability needs.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify automated coverage for:
   - duplicate retry suppression
   - correlation ID propagation across worker retries
   - stable idempotency key generation for the same logical operation
   - tenant-scoped execution context in worker paths

4. If schema changes were added:
   - verify migrations are generated/applied according to repo conventions
   - verify uniqueness/index constraints behave as expected under repeated execution attempts

5. Manually validate via targeted test or debug run:
   - trigger a retryable background operation
   - force a transient failure before completion
   - rerun/retry the operation
   - confirm:
     - logs show the same correlation ID across attempts
     - no duplicate side effects are created
     - final success/failure state is consistent
     - tenant context is present in logs and persistence where expected

6. Review structured logs/output to confirm correlation fields are actually emitted, not just stored in memory.

# Risks and follow-ups
- **Risk: duplicate suppression at the wrong layer**
  - If idempotency is only handled in memory or only in logs, retries after restart may still duplicate side effects.
  - Prefer durable persistence and database constraints where appropriate.

- **Risk: over-generating IDs**
  - Generating a fresh correlation ID or idempotency key on each retry defeats the purpose.
  - Reuse stable identifiers for the same logical operation.

- **Risk: conflating correlation and idempotency**
  - Correlation ID is for tracing a flow.
  - Idempotency key is for deduplicating a logical operation.
  - They may be related but should not be treated as interchangeable.

- **Risk: tenant leakage in shared workers**
  - Ensure every execution path carries `company_id`/tenant context explicitly.
  - Do not process deduplication globally if the logical uniqueness should be tenant-scoped.

- **Risk: partial side effects**
  - If a side effect occurs before status persistence, retries may replay it.
  - Use atomic state transitions, outbox semantics, or durable processed markers.

- **Follow-up suggestions**
  - standardize correlation propagation across HTTP, background workers, and internal commands/events
  - expose correlation IDs in operational diagnostics/admin tooling
  - add metrics for retry counts, duplicate suppression hits, and permanent failure rates
  - consider a dedicated execution history table if current models do not provide enough observability