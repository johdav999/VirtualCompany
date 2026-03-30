# Goal
Implement backlog task **TASK-10.4.3 — Blocked or failed executions create visible exceptions/escalations** for story **ST-404 Escalations, retries, and long-running background execution**.

The coding agent should add the minimum complete vertical slice needed so that when background workflow/task execution becomes **blocked** or **failed**, the system persists a **visible exception/escalation record** and exposes it for downstream UI/inbox/dashboard consumption. This should fit the existing modular monolith architecture, remain tenant-scoped, and align with workflow/task/audit/notification patterns already implied by the backlog.

Because no explicit acceptance criteria were provided for this task beyond the story-level statement, treat the following as the implementation target:

- A blocked or failed background execution results in a durable, queryable exception/escalation record.
- The record is tenant-scoped and linked to the relevant task/workflow/execution context.
- The system distinguishes at least:
  - **Blocked** business/policy/manual-intervention conditions
  - **Failed** execution conditions
- Creation of the exception/escalation is idempotent enough to avoid duplicate spam for the same execution incident.
- The exception/escalation is visible through an application query/API surface suitable for future dashboard/inbox integration.
- Audit/structured logging should capture the event without exposing unsafe internals.

# Scope
Implement only what is necessary for this task inside the current .NET solution.

Include:

1. **Domain model support**
   - Add a domain concept for execution exceptions/escalations, or extend an existing one if already present.
   - Include status/severity/type fields appropriate for blocked vs failed execution visibility.
   - Include tenant ownership and links to task/workflow/background execution context.

2. **Persistence**
   - Add EF Core entity/configuration/migration for the new table(s), or extend existing persistence if a suitable table already exists.
   - Ensure indexes support tenant-scoped listing by status/created date.

3. **Application layer**
   - Add command/service logic used by background workers to record blocked/failed execution exceptions.
   - Add query/read model to list visible exceptions/escalations for a company.
   - Keep CQRS-lite style consistent with the codebase.

4. **Background execution integration**
   - Update the relevant workflow/background execution path so that blocked or failed execution states create exception/escalation records.
   - Respect retry semantics:
     - transient retryable failures should not necessarily create a final visible exception on every attempt unless the current architecture already expects that.
     - terminal failure or blocked/manual intervention should create the visible record.
   - If there is already a retry/failure abstraction, integrate there rather than duplicating logic.

5. **API surface**
   - Expose a tenant-scoped endpoint or handler to retrieve visible exceptions/escalations.
   - If the project uses minimal APIs/controllers/MediatR/etc., follow the existing pattern.

6. **Tests**
   - Add focused unit/integration tests for:
     - blocked execution creates visible exception/escalation
     - terminal failed execution creates visible exception/escalation
     - duplicate creation is prevented or controlled for the same incident
     - tenant scoping on queries

Out of scope unless already trivial and clearly supported by existing patterns:

- Full Blazor UI implementation
- Mobile UI
- Rich notification fan-out UX
- Full retry engine redesign
- Broad observability overhaul
- New broker infrastructure

# Files to touch
Inspect the solution first and then update the most relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - task/workflow/background execution domain models
  - new exception/escalation entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - commands/services/handlers for recording execution exceptions
  - queries/DTOs for listing visible exceptions
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entities/configurations
  - DbContext updates
  - migrations
  - repository implementations
  - background worker integration points
- `src/VirtualCompany.Api/**`
  - endpoint/controller for querying exceptions/escalations
- `src/VirtualCompany.Shared/**`
  - shared contracts if the API pattern uses shared DTOs
- Tests in the corresponding test projects if present in the repo

Before coding, identify the actual existing patterns for:

- domain entities and aggregate boundaries
- command/query handling
- background worker implementation
- tenant context resolution
- migrations
- testing conventions

Prefer modifying existing workflow/task/background execution components over introducing parallel abstractions.

# Implementation plan
1. **Discover existing architecture in code**
   - Inspect `README.md`, solution structure, and the main projects to determine:
     - whether MediatR or another handler pattern is used
     - how tenant context is passed
     - how tasks/workflows/background jobs are modeled
     - whether notifications/audit/outbox entities already exist
   - Search for existing concepts such as:
     - `WorkflowInstance`
     - `Task`
     - `Approval`
     - `AuditEvent`
     - `Notification`
     - `Outbox`
     - `Background`
     - `Retry`
     - `Escalation`
     - `Exception`

2. **Choose the smallest correct persistence model**
   - If no suitable model exists, add a new table/entity such as `execution_exceptions` or `workflow_exceptions`.
   - Recommended fields:
     - `id`
     - `company_id`
     - `type` or `exception_type` (`blocked`, `failed`)
     - `severity` (`warning`, `error`, `critical`) if useful
     - `status` (`open`, `acknowledged`, `resolved`) or at minimum `open`
     - `title`
     - `summary`
     - `source_type` (`task`, `workflow_instance`, `background_job`, `tool_execution`)
     - `source_id`
     - `task_id` nullable
     - `workflow_instance_id` nullable
     - `agent_id` nullable
     - `correlation_id` / `idempotency_key` / `incident_key`
     - `failure_code` / `reason_code`
     - `details_json`
     - `created_at`
     - `updated_at`
     - `resolved_at` nullable
   - Add a uniqueness constraint or dedupe strategy around the incident key if appropriate.

3. **Model blocked vs failed explicitly**
   - Add enums/value objects for:
     - exception kind
     - exception status
     - source type
   - Keep names aligned with existing code conventions.
   - Ensure blocked/manual-intervention conditions are not conflated with transient retry attempts.

4. **Implement application service/command**
   - Add a command or service like `RecordExecutionExceptionCommand` / `IExecutionExceptionService`.
   - Inputs should include:
     - company/tenant id
     - source identifiers
     - blocked vs failed classification
     - human-readable summary
     - machine-readable reason/failure code
     - correlation/incident key
   - Logic should:
     - validate tenant/source linkage where practical
     - upsert or dedupe repeated incidents
     - persist the exception/escalation
     - optionally emit an audit event if that pattern already exists

5. **Integrate with background execution path**
   - Find the workflow runner / scheduler / background worker path used for ST-404-related execution.
   - Update it so that:
     - when execution enters a **blocked** state, it records a visible exception/escalation
     - when execution reaches a **terminal failed** state, it records a visible exception/escalation
     - transient retryable failures only create a visible exception if they become terminal or if the existing design explicitly wants attempt-level visibility
   - Preserve existing retry behavior.
   - Do not swallow exceptions silently; record and rethrow/handle according to existing worker conventions.

6. **Expose query/read endpoint**
   - Add a query such as `GetOpenExecutionExceptionsQuery`.
   - Return a concise DTO with fields suitable for inbox/dashboard use:
     - id
     - type
     - status
     - title
     - summary
     - source type/id
     - task/workflow references
     - created at
     - severity
   - Add API endpoint under an existing tenant-scoped route pattern, for example:
     - `GET /api/exceptions`
     - or `GET /api/operations/exceptions`
   - Follow the project’s authorization and tenant resolution conventions.

7. **Add tests**
   - Unit test domain/application logic:
     - blocked incident creates open exception
     - terminal failure creates open exception
     - same incident key does not create duplicate open records
   - Integration test persistence/query behavior if test infrastructure exists:
     - query returns only current tenant’s exceptions
   - If background worker tests exist, add one proving worker integration creates the record on blocked/final failure.

8. **Migration and compatibility**
   - Add EF migration for schema changes.
   - Ensure the app still builds cleanly even if no UI consumes the new endpoint yet.
   - Keep DTOs and naming future-friendly for dashboard/inbox integration.

9. **Document assumptions in code comments or PR notes**
   - If the repo lacks a formal execution incident model, document that this implementation introduces one as the visibility mechanism for ST-404/TASK-10.4.3.
   - Note any follow-up needed for notification fan-out or UI surfacing.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal workflow.
   - If migrations are generated in-repo, ensure the new migration is included and builds.

4. Manually validate the main scenarios using the existing execution path or a focused test harness:
   - Trigger or simulate a **blocked** workflow/task/background execution.
   - Confirm:
     - source entity reflects blocked state if applicable
     - a visible exception/escalation record is persisted
     - the query/API returns it for the correct tenant
   - Trigger or simulate a **terminal failed** execution.
   - Confirm the same visibility behavior.
   - Repeat the same incident with the same correlation/incident key.
   - Confirm duplicates are prevented or intentionally coalesced.

5. Validate tenant isolation:
   - Query exceptions for tenant A and confirm tenant B incidents are not returned.

6. Validate logs/audit behavior:
   - Ensure structured logs include correlation/tenant context where existing infrastructure supports it.
   - Ensure no sensitive raw exception internals are exposed in user-facing DTOs.

# Risks and follow-ups
- **Repo pattern mismatch risk:** The actual codebase may already have notification, audit, or exception concepts. Reuse them if present instead of creating a competing model.
- **Retry semantics ambiguity:** Be careful not to create noisy exception records for every transient retry attempt. Prefer visible records for blocked/manual-intervention and terminal failure states.
- **Idempotency risk:** Without an incident key/correlation strategy, repeated worker retries may create duplicate visible exceptions.
- **Tenant enforcement risk:** All reads/writes must be company-scoped.
- **UI gap:** This task should at least expose a query/API surface, but full dashboard/inbox rendering may remain for a later task.
- **Notification follow-up:** If no notification fan-out is wired yet, a later task should connect these visible exceptions/escalations into the approval/alert inbox and executive cockpit.
- **Resolution workflow follow-up:** This task can create open exceptions; later work may need acknowledge/resolve actions and richer escalation routing.
- **Audit follow-up:** If business audit events are not yet integrated, add them only if the existing architecture already supports them cleanly; otherwise leave a clear extension point.