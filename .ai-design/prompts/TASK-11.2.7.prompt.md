# Goal
Implement `TASK-11.2.7` for `ST-502`: persist and propagate a shared correlation ID across the single-agent orchestration pipeline so the same execution can be traced through prompt-related persistence, tool executions, task records, and audit events.

This work should make correlation IDs a first-class orchestration concept in the .NET backend, aligned with the architecture note: “Persist correlation IDs across prompt, tool, task, and audit records.”

# Scope
In scope:
- Add a durable orchestration correlation ID model for single-agent task execution.
- Ensure a correlation ID is created or propagated at orchestration start.
- Persist that correlation ID on all relevant business records involved in the orchestration flow:
  - prompt/prompt-run related persistence if such a table/entity already exists
  - `tool_executions`
  - `tasks`
  - `audit_events`
- Thread the correlation ID through application/domain/infrastructure layers used by the shared orchestration pipeline.
- Update mappings, EF configurations, repositories, and migrations as needed.
- Ensure correlation ID is included in structured logs where the orchestration pipeline already emits logs.
- Add/adjust tests to verify persistence and propagation behavior.

Out of scope:
- Building a new distributed tracing system.
- Refactoring unrelated orchestration behavior.
- Adding multi-agent correlation hierarchies beyond what is needed for single-agent orchestration.
- Large UI work unless an existing API contract must expose correlation IDs for diagnostics.

Implementation expectations:
- Prefer a single correlation ID per orchestration run, represented as a stable string or GUID-based value.
- Reuse an incoming correlation ID if one is already present in the request/execution context; otherwise generate one at orchestration entry.
- Keep the design compatible with retries/idempotency and future multi-agent expansion.

# Files to touch
Inspect the solution first and then update the actual files that implement orchestration, persistence, and auditing. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - task, tool execution, audit, and orchestration-related entities/value objects
- `src/VirtualCompany.Application/**`
  - orchestration services/handlers
  - task creation/update flows
  - audit event creation
  - DTOs/commands used by the shared orchestration pipeline
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations
  - repositories
  - logging/correlation helpers if present
- `src/VirtualCompany.Api/**`
  - request correlation extraction/propagation if orchestration starts from API endpoints
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially other test projects if present under `tests/**`

Also inspect:
- existing migration conventions
- any current correlation/request ID middleware
- any existing prompt persistence model/table not listed in the architecture excerpt

# Implementation plan
1. **Discover the current orchestration and persistence model**
   - Find the shared single-agent orchestration entry point for `ST-502`.
   - Identify where:
     - prompts are built and possibly persisted
     - tasks are created/updated
     - tool executions are persisted
     - audit events are persisted
   - Identify whether correlation/request IDs already exist in:
     - HTTP middleware
     - logging scopes
     - background worker execution context
     - domain entities or base auditable entities

2. **Define the correlation ID contract**
   - Introduce a consistent orchestration correlation ID concept used across the pipeline.
   - Prefer one of:
     - `Guid`/UUID stored as text or native UUID depending on current conventions
     - a dedicated string field if existing logging/request correlation uses strings
   - Name consistently, e.g. `CorrelationId`.
   - Decide nullability:
     - for newly created records in the orchestration path it should be required
     - for legacy rows, migration may need nullable column + backfill strategy or nullable schema with required app behavior going forward
   - If there is an orchestration context object, add `CorrelationId` there.

3. **Extend domain/application models**
   - Add `CorrelationId` to relevant entities/models:
     - `Task`
     - `ToolExecution`
     - `AuditEvent`
     - prompt persistence entity if one exists
   - Update constructors/factories so new records created by orchestration require or receive the correlation ID.
   - Avoid leaking HTTP-specific concerns into domain entities; treat correlation ID as orchestration metadata.

4. **Propagate correlation ID through orchestration entry**
   - At orchestration start:
     - read an existing correlation ID from current execution context if available
     - otherwise generate a new one
   - Ensure the same value is passed through:
     - prompt build/persist step
     - task creation/update
     - tool execution persistence
     - audit event creation
   - If orchestration can continue in background workers, ensure the correlation ID is carried in the command/job payload.

5. **Update persistence layer**
   - Add EF Core properties/configuration for new `CorrelationId` columns.
   - Add indexes where useful for traceability queries, likely on:
     - `tasks(company_id, correlation_id)` or equivalent
     - `tool_executions(company_id, correlation_id)`
     - `audit_events(company_id, correlation_id)`
     - prompt table equivalent
   - Generate a migration following repository conventions.
   - Keep tenant scoping intact.

6. **Update audit and tool execution creation paths**
   - Ensure all orchestration-generated audit events include the same correlation ID.
   - Ensure all tool execution records include the same correlation ID as the parent orchestration run.
   - If task updates create multiple audit events, all should share the same correlation ID.

7. **Update prompt persistence**
   - If prompt runs/prompts are already persisted, add and populate `CorrelationId`.
   - If prompts are not currently persisted anywhere, do not invent a large new subsystem unless there is already a lightweight persistence model intended for this. Instead:
     - document the finding in code comments and task notes
     - still complete task/tool/audit persistence
     - only add prompt persistence if there is an obvious existing record/table for prompt artifacts in this codebase

8. **Logging alignment**
   - Where orchestration logs already exist, include `CorrelationId` in log scopes/structured properties.
   - Do not replace business persistence with logs; logs are supplemental.

9. **Testing**
   - Add unit/integration tests covering:
     - new orchestration run generates a correlation ID when absent
     - existing correlation ID is reused when supplied
     - created task persists the correlation ID
     - persisted tool execution uses the same correlation ID
     - persisted audit event uses the same correlation ID
     - prompt persistence uses the same correlation ID if applicable
   - Prefer integration tests around the orchestration flow over isolated mocks where feasible.

10. **Document assumptions in code**
   - If the codebase lacks prompt persistence, note that explicitly in comments/PR-ready notes and keep the implementation extensible for future prompt record support.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration output and model alignment:
   - ensure new migration exists and applies cleanly
   - confirm EF model snapshot updates if used

4. Manually verify code paths:
   - Start from the single-agent orchestration entry point and confirm one correlation ID instance is passed through all relevant persistence calls.
   - Confirm no new record in the orchestration path is created without correlation ID unless legacy/non-orchestration behavior explicitly requires null support.

5. Validate persistence behavior in tests or local run:
   - trigger a single-agent orchestration/task flow
   - inspect persisted records for the same correlation ID across:
     - task row
     - tool execution row
     - audit event row
     - prompt row if applicable

6. Validate logging:
   - confirm orchestration logs include the correlation ID in structured form where logging already exists.

# Risks and follow-ups
- **Prompt persistence may not exist yet**  
  The backlog note requires correlation IDs across prompt records, but the architecture excerpt does not show a prompt table. If no prompt persistence exists in the codebase, do not create a broad new prompt-history subsystem unless it is already partially implemented. Complete the correlation plumbing so prompt persistence can adopt it immediately later.

- **Schema compatibility for existing data**  
  Adding non-null columns to existing tables may require a phased migration. If needed, use nullable columns for legacy rows and enforce non-null in application behavior for new orchestration-created records.

- **Multiple correlation concepts may already exist**  
  There may be request IDs, trace IDs, job IDs, or idempotency keys already present. Avoid duplicating semantics unnecessarily. Reuse an existing correlation concept if it already matches the business need and can be persisted consistently.

- **Background worker propagation gaps**  
  If orchestration spans API and worker boundaries, correlation ID can be lost unless explicitly serialized into commands/messages/jobs. Check these transitions carefully.

- **Future multi-agent expansion**  
  Keep this implementation compatible with parent/child execution tracing later. A single `CorrelationId` now is fine, but avoid designs that would block adding `ParentCorrelationId` or execution/span concepts later.