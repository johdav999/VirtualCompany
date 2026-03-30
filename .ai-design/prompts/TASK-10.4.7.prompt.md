# Goal
Implement `TASK-10.4.7` for `ST-404 — Escalations, retries, and long-running background execution` by ensuring all background worker execution remains strictly tenant-scoped.

The coding agent should update the worker/orchestration pipeline so that:
- every queued/scheduled/background execution carries an explicit tenant/company context,
- worker handlers resolve and enforce that tenant context before reading or mutating tenant-owned data,
- cross-tenant execution is prevented by design and validated defensively,
- logs/correlation metadata include tenant context where applicable,
- retries and long-running execution preserve the original tenant scope.

No explicit acceptance criteria were provided for this task, so derive implementation behavior from:
- ST-404 notes: “Keep worker execution tenant-scoped.”
- architecture guidance: “Tenant-isolated data and agent execution context”
- shared-schema multi-tenancy with `company_id` enforcement.

# Scope
In scope:
- Inspect current background job, scheduler, workflow runner, retry, and long-running execution code paths.
- Identify the execution envelope/message/job model used by workers.
- Add or standardize explicit tenant/company context on all worker-dispatched jobs/events/commands that operate on tenant-owned data.
- Ensure worker entry points establish tenant context before invoking application/domain logic.
- Add guard clauses so handlers fail safely if tenant context is missing, invalid, or mismatched.
- Ensure repository/query access used by workers filters by `company_id` and does not rely on ambient non-worker HTTP context.
- Preserve tenant context across retries, delayed jobs, and resumed long-running work.
- Add/update tests covering tenant-scoped worker behavior.

Out of scope unless required by existing design:
- Large-scale redesign of the job system.
- Introducing a new message broker.
- Full row-level security implementation.
- Refactoring unrelated HTTP request tenant resolution.
- Broad observability work beyond tenant-aware worker logging/correlation needed for this task.

# Files to touch
Start by inspecting these projects and likely touch files in them:

- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`

Likely candidate areas:
- Background worker abstractions and hosted services
- Scheduler / workflow runner / retry handlers
- Outbox dispatcher and outbox message contracts
- Job payload/envelope models
- Tenant context abstractions/services
- Repository/query handlers used by workers
- Logging/correlation helpers
- Integration tests / unit tests

Potential file patterns to inspect:
- `**/*Worker*.cs`
- `**/*Background*.cs`
- `**/*Scheduler*.cs`
- `**/*Outbox*.cs`
- `**/*Job*.cs`
- `**/*Message*.cs`
- `**/*Tenant*.cs`
- `**/*Workflow*.cs`
- `**/*Retry*.cs`
- `**/*Execution*.cs`

If present, prioritize these kinds of files:
- worker hosted services in Infrastructure
- application commands/handlers invoked by workers
- tenant provider/accessor interfaces
- outbox dispatch pipeline
- workflow progression services
- tests around multi-tenancy and background execution

# Implementation plan
1. **Discover the current worker execution model**
   - Find how background work is represented:
     - hosted services,
     - queued jobs,
     - outbox messages,
     - scheduled commands,
     - workflow progression records,
     - retry records.
   - Map all entry points where non-HTTP execution starts.
   - Determine whether tenant context is currently:
     - embedded in payloads,
     - inferred from entity lookup,
     - read from ambient request context,
     - or missing entirely.

2. **Define a consistent tenant-scoped execution contract**
   - Standardize on explicit tenant/company identity in every tenant-owned background execution payload.
   - Prefer a single execution envelope or interface pattern, for example:
     - `CompanyId` / `TenantId`
     - `CorrelationId`
     - optional `InitiatedByActorType` / `InitiatedByActorId`
   - Do not rely on HTTP-only tenant resolution in workers.
   - If multiple job/message types exist, update each to carry tenant context explicitly.

3. **Establish tenant context at worker boundaries**
   - At each worker entry point, resolve the tenant/company from the job envelope and set the worker/application tenant context using the project’s existing abstraction if available.
   - If no abstraction exists, add a minimal one suitable for non-request execution, such as:
     - `ITenantContextAccessor`
     - `ITenantExecutionScopeFactory`
     - or equivalent scoped context service.
   - Ensure the context is scoped per job execution and cleared/disposed afterward.
   - Avoid static/global mutable tenant state.

4. **Enforce defensive validation**
   - Fail fast when a tenant-owned job arrives without tenant context.
   - Where a job references a tenant-owned entity (`Task`, `WorkflowInstance`, `Approval`, `Agent`, etc.), validate that:
     - the entity exists,
     - the entity belongs to the same `company_id` as the job context.
   - If mismatched:
     - do not process the job,
     - log a structured warning/error with correlation and tenant identifiers,
     - mark the execution as failed/permanent where appropriate to avoid unsafe retries.

5. **Update worker-invoked application logic to be tenant-aware**
   - Review commands/queries used by workers and ensure they accept tenant context explicitly or consume it from a worker-safe scoped accessor.
   - Ensure repositories and queries filter by `company_id`.
   - Remove any hidden dependency on `HttpContext`, request claims, or web-only middleware for tenant resolution.
   - For workflow progression and retries, ensure all reads/writes remain constrained to the active tenant.

6. **Preserve tenant scope across retries and long-running execution**
   - Ensure retry records or re-enqueued jobs retain the original `CompanyId`.
   - Ensure delayed/scheduled jobs serialize tenant context durably.
   - For long-running workflows resumed from persisted state, ensure the resumption path restores tenant context before processing.
   - If idempotency keys exist, confirm they are tenant-safe; if needed, scope them by tenant to avoid collisions across companies.

7. **Update outbox-backed dispatch if applicable**
   - If outbox messages trigger tenant-owned work, ensure outbox payloads/metadata include `CompanyId`.
   - Ensure dispatcher/consumer sets tenant context before invoking downstream handlers.
   - Prevent duplicate or replayed messages from executing against the wrong tenant.

8. **Add structured logging and diagnostics**
   - Include tenant/company identifier in worker logs where applicable.
   - Preserve correlation IDs across enqueue, execution, retry, and completion/failure.
   - Keep business audit concerns separate unless the existing implementation already records business events here.

9. **Add tests**
   - Unit tests:
     - worker/job handler rejects missing tenant context,
     - worker/job handler rejects tenant/entity mismatch,
     - retry preserves tenant context,
     - tenant context is established before application logic runs.
   - Integration tests where feasible:
     - create data for two companies,
     - enqueue/process work for company A,
     - verify only company A data is read/updated,
     - verify company B data is untouched,
     - verify mismatched payload/entity combinations fail safely.
   - Prefer adding tests near existing worker/multi-tenant test suites rather than inventing a new test style.

10. **Keep changes minimal and aligned with current architecture**
   - Follow existing project conventions, naming, DI patterns, and layering.
   - Prefer incremental hardening over speculative framework creation.
   - If you introduce a reusable tenant execution helper, keep it small and focused on worker scenarios.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted tests for:
   - worker/background execution,
   - workflow progression,
   - outbox dispatch,
   - tenant/multi-tenant behavior.

4. Run full test suite:
   - `dotnet test`

5. Manually verify in code review that:
   - every tenant-owned background payload includes `CompanyId`/tenant identifier,
   - worker entry points establish tenant context explicitly,
   - no worker path depends on `HttpContext` or request claims,
   - repository/query access from workers is tenant-filtered,
   - retries/resumptions preserve tenant context,
   - logs include tenant and correlation metadata where appropriate.

6. If there are integration or local execution hooks for workers, exercise one end-to-end path such as:
   - create two companies,
   - create a workflow/task for company A,
   - trigger background processing,
   - confirm only company A records change,
   - simulate a mismatched tenant payload and confirm safe failure.

# Risks and follow-ups
- **Risk: hidden ambient tenant resolution**
  - Some application services may implicitly depend on request-scoped tenant resolution. Worker execution can bypass this and accidentally run unscoped.
  - Mitigation: audit worker-invoked services and make tenant dependency explicit.

- **Risk: partial coverage**
  - Some background paths may be less obvious, especially retries, delayed jobs, or outbox consumers.
  - Mitigation: inventory all hosted services and dispatchers before changing code.

- **Risk: breaking existing job payload compatibility**
  - Adding required tenant fields may affect persisted jobs/messages.
  - Mitigation: if persistence already exists, add backward-compatible handling where safe, but default to fail-closed for tenant-owned work lacking tenant context.

- **Risk: duplicate or cross-tenant idempotency collisions**
  - Retry/idempotency keys not scoped by tenant can create subtle bugs.
  - Mitigation: include tenant in idempotency semantics where applicable.

- **Risk: overreaching refactor**
  - This task is about tenant-scoped worker execution, not redesigning the whole background processing subsystem.
  - Mitigation: keep changes focused on execution envelopes, worker boundaries, validation, and tests.

Follow-ups to note in comments or implementation summary if discovered:
- remaining worker paths that still need tenant hardening,
- opportunities to centralize tenant-scoped execution helpers,
- whether row-level security or stronger DB-level enforcement should be considered later,
- whether outbox/event schemas should formally standardize tenant metadata across the platform.