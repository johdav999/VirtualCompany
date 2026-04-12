# Goal
Implement backlog task **TASK-12.2.1** for story **ST-602 Audit trail and explainability views** by ensuring that **important business actions create persisted audit events** containing:

- `actor`
- `action`
- `target`
- `outcome`
- `rationale summary`
- `data sources used`

The implementation must treat auditability as a **business/domain feature**, not technical logging, and fit the existing **.NET modular monolith** architecture with **tenant-scoped PostgreSQL persistence**.

# Scope
Focus only on the backend/domain/application/infrastructure work needed to persist business audit events for important actions.

In scope:

- Define or complete the **audit event domain model** and persistence mapping.
- Add any missing database migration/schema needed for `audit_events`.
- Introduce an **application-facing audit writer/service** for creating business audit events.
- Integrate audit event creation into the most relevant existing important action paths, prioritizing:
  - policy-enforced tool execution outcomes
  - approval-related action requests/decisions if already implemented
  - task/workflow actions only where there is already a clear action completion point
- Ensure audit events capture:
  - tenant/company
  - actor type/id
  - action
  - target type/id
  - outcome
  - rationale summary
  - data sources used
  - timestamps / correlation metadata if the codebase already supports it
- Keep explanations concise and operational; do **not** store raw chain-of-thought.

Out of scope unless required by existing code structure:

- Full audit history UI/filtering pages
- Mobile changes
- Broad refactors across unrelated modules
- Technical logging/observability changes
- Implementing every possible audit-producing action in the system if the underlying feature does not yet exist

If the codebase already contains partial audit/explainability structures, extend them rather than duplicating them.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - audit domain entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - audit service interface
  - command handlers / orchestration handlers / approval handlers / task handlers where important actions complete
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository / audit service implementation
  - migrations or SQL scripts if migrations are managed here
- `src/VirtualCompany.Api/**`
  - DI registration if needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- `tests/**` or other test projects
  - unit tests for audit creation behavior

Also inspect:

- existing persistence conventions
- existing migration approach
- any current `audit_events`, `tool_executions`, approvals, tasks, or workflow persistence code
- any existing correlation/request context abstractions
- any existing retrieval/source reference models that can be reused for `data sources used`

# Implementation plan
1. **Discover existing audit and persistence structures**
   - Search for:
     - `audit`
     - `AuditEvent`
     - `tool_executions`
     - `approval`
     - `rationale`
     - `data source`
     - `source reference`
     - `correlation`
   - Determine:
     - whether `audit_events` already exists in code or only in docs
     - how EF Core entities/configurations are organized
     - how tenant scoping is enforced
     - where important actions currently complete

2. **Define the business audit event model**
   - Add or complete a domain entity for `AuditEvent` aligned to architecture/backlog intent.
   - Minimum fields:
     - `Id`
     - `CompanyId`
     - `ActorType`
     - `ActorId`
     - `Action`
     - `TargetType`
     - `TargetId`
     - `Outcome`
     - `RationaleSummary`
     - `DataSourcesUsed`
     - `CreatedAt`
   - Prefer strongly typed enums/value objects where consistent with project conventions; otherwise use constrained strings.
   - `DataSourcesUsed` should be stored in a structured form that remains human-readable and query-safe:
     - preferred: JSON/JSONB collection of source references
     - acceptable fallback: serialized JSON string if that matches current persistence patterns

3. **Add persistence mapping and migration**
   - Create/update EF configuration and database schema for `audit_events`.
   - Use PostgreSQL-friendly types:
     - UUIDs for ids
     - `jsonb` for structured source references if supported by current stack
     - `timestamptz` for timestamps
   - Ensure `company_id` is required for tenant-owned records.
   - Add useful indexes if consistent with current migration style, likely:
     - `(company_id, created_at desc)`
     - `(company_id, actor_type, actor_id)`
     - `(company_id, target_type, target_id)`

4. **Introduce an audit writing abstraction**
   - Add an application-level interface such as `IAuditEventWriter` or equivalent.
   - Add a method that accepts a structured request model, e.g.:
     - company id
     - actor
     - action
     - target
     - outcome
     - rationale summary
     - data sources used
   - Keep it simple and synchronous from the caller perspective unless the codebase already uses domain events/outbox for this pattern.
   - The service should normalize null/empty values safely and reject invalid required fields.

5. **Define source reference structure**
   - Add a small DTO/value object for audit data sources, for example:
     - `SourceType` (document, memory, task, workflow, approval, integration_record, message, retrieval_chunk, etc.)
     - `SourceId`
     - `DisplayName` or `Reference`
   - Keep it concise and human-readable.
   - Do not store raw prompts or hidden reasoning.

6. **Integrate audit creation into important action paths**
   - Prioritize existing action paths that clearly represent “important actions”:
     1. **Tool execution decisions/results**
        - When a tool execution is allowed and completed, create an audit event.
        - When a tool execution is denied by policy, create an audit event with blocked/denied outcome.
     2. **Approval actions**
        - If approval request creation or approval decision flows already exist, create audit events there.
     3. **Task/workflow state-changing actions**
        - Add only where there is already a stable command/handler boundary.
   - Reuse existing rationale summaries and retrieval/source references if already produced by orchestration.
   - If source references are not yet available everywhere, pass an empty collection rather than inventing fake data.

7. **Map outcomes consistently**
   - Standardize outcome values across integrations, e.g.:
     - `succeeded`
     - `failed`
     - `denied`
     - `requested`
     - `approved`
     - `rejected`
   - Keep naming consistent with existing domain language if already established.

8. **Preserve tenant isolation**
   - Ensure every audit event is written with the correct `CompanyId`.
   - Ensure any repository/query path remains tenant-scoped.
   - Do not allow cross-tenant source references or target ids to be written from unrelated contexts.

9. **Add tests**
   - Unit tests for audit writer validation/mapping.
   - Integration tests for at least the highest-value path:
     - denied tool execution creates audit event with actor/action/target/outcome/rationale/data sources
     - successful important action creates audit event
   - Verify persisted values, not just method invocation.
   - If API/integration tests are expensive, prefer application/infrastructure integration tests that hit the real persistence layer used by the solution’s test conventions.

10. **Keep implementation incremental and documented in code**
   - Add concise comments only where behavior is non-obvious.
   - Do not over-engineer a full audit framework if the current task only requires event persistence for important actions.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration/schema:
   - Confirm `audit_events` table exists with required columns.
   - Confirm structured `data sources used` persistence works as expected.

4. Verify behavior in tests or local execution:
   - Trigger an important action path already present in the codebase.
   - Confirm an audit event is persisted with:
     - actor
     - action
     - target
     - outcome
     - rationale summary
     - data sources used
     - company/tenant id

5. Verify negative path:
   - Confirm denied/blocked policy action also creates an audit event.

6. Verify no raw reasoning leakage:
   - Ensure stored rationale is concise summary text only.
   - Ensure no chain-of-thought, full prompt, or hidden reasoning is persisted.

# Risks and follow-ups
- **Risk: incomplete existing action flows**
  - Some “important actions” may not yet exist in code. In that case, wire audit creation into the most mature implemented paths and leave clear TODOs for remaining story coverage.

- **Risk: unclear schema ownership**
  - Migration patterns may differ across projects. Follow the repository’s established migration approach rather than introducing a new one.

- **Risk: source references not yet standardized**
  - Retrieval/data-source metadata may not be consistently available. Use a minimal structured source reference model now and align later with ST-304/ST-602 explainability views.

- **Risk: duplication with technical logging**
  - Do not reuse app logs as business audit records. Keep business audit persistence separate.

- **Risk: enum/string drift**
  - If using strings for actor/outcome/action types, centralize constants or value objects to avoid inconsistent values.

Follow-ups after this task:
- Add query/read models and endpoints for audit history filtering by agent/task/workflow/date range.
- Link audit events to approvals, tool executions, and affected entities in detail views.
- Standardize explainability/source-reference generation across orchestration and retrieval flows.
- Consider outbox/event fan-out if audit events later need notifications or compliance exports.