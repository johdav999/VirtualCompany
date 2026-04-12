# Goal
Implement **TASK-12.2.5** for **ST-602 Audit trail and explainability views** by treating auditability as a **first-class domain capability**, not merely technical logging.

The coding agent should add or complete the domain/application/infrastructure/web support needed so that:
- important business actions can be persisted as **business audit events**
- audit records can include **actor, action, target, outcome, rationale summary, and data sources used**
- audit history can be queried in a **tenant-scoped, role-aware** way
- audit detail can surface links to related **approvals, tool executions, tasks, workflows, and affected entities**
- explanations remain **concise and operational**, explicitly avoiding raw chain-of-thought exposure

Because no explicit acceptance criteria were provided for the backlog task itself, align implementation to the architecture and the ST-602 story definition in the backlog.

# Scope
In scope:
- Add or complete the **Audit & Explainability** domain model and persistence for business audit events.
- Ensure audit events are modeled separately from technical/operational logs.
- Add application-layer commands/services for writing audit events from business flows.
- Add query-layer support for:
  - audit history list
  - filtering by agent, task, workflow, and date range
  - audit detail view with related entities
- Add minimal web/API surface needed to support ST-602.
- Enforce **tenant isolation** and **role-based access** on audit queries.
- Represent **data source references** in a human-readable way.
- Preserve clean architecture boundaries and CQRS-lite style.

Out of scope unless already trivial in the codebase:
- Full compliance export bundles
- Mobile-specific audit UI
- Reworking all existing modules to emit perfect audit coverage
- Technical logging/observability changes from ST-104
- Event sourcing
- Exposing raw model reasoning or chain-of-thought

If the repository already contains partial audit functionality, extend and normalize it rather than duplicating it.

# Files to touch
Inspect first, then modify the appropriate files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to create/update:
- Domain entities/value objects/enums for audit and explainability
- Application contracts, commands, queries, DTOs, handlers
- Infrastructure EF Core mappings/repositories/query implementations
- PostgreSQL migration(s)
- API endpoints/controllers or minimal API route registrations
- Blazor pages/components for audit list/detail views
- Authorization policies or guards
- Tests covering tenant scoping, filtering, and detail linking

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Use existing naming, folder structure, MediatR/CQRS patterns, EF conventions, and authorization patterns already present in the solution.

# Implementation plan
1. **Discover existing architecture and conventions**
   - Build a quick map of:
     - current module boundaries
     - persistence approach
     - whether EF Core migrations are active
     - current auth/tenant resolution approach
     - existing task/workflow/approval/tool execution entities
     - any existing audit or activity feed implementation
   - Reuse existing abstractions wherever possible.

2. **Define or complete the audit domain model**
   - Add a business audit entity aligned to the architecture/backlog intent.
   - Ensure it supports at minimum:
     - `company_id`
     - actor type/id
     - action
     - target type/id
     - outcome
     - rationale summary
     - created timestamp
   - Add structured support for explainability metadata, preferably including:
     - human-readable data sources used
     - related approval id(s)
     - related tool execution id(s)
     - related task id
     - related workflow instance id
     - affected entity references
     - correlation id if the platform already uses one
   - Keep chain-of-thought out of the model. Store only concise rationale/explanation summaries.

3. **Complete persistence mapping**
   - Add/update EF configuration and database schema.
   - If the architecture snippet’s `audit_events` table is incomplete in the current codebase, finish it pragmatically.
   - Prefer normalized columns for common filters and JSONB for flexible explainability metadata where appropriate.
   - Add indexes for likely query paths:
     - company + created_at
     - company + actor
     - company + target
     - company + task/workflow
   - Keep all tenant-owned audit data scoped by `company_id`.

4. **Add application-layer write capability**
   - Introduce an audit writer service or command pattern that business flows can call.
   - The write contract should be easy to use from orchestration/task/approval/tool execution flows.
   - Include safe defaults and validation:
     - require company id
     - require action
     - require actor type
     - cap or validate rationale summary length if conventions exist
   - If there are already relevant flows for approvals/tool executions/tasks, wire in at least the most obvious audit emission points without broad invasive refactors.

5. **Add query models for audit history**
   - Implement a query for paged/filterable audit history with filters for:
     - agent
     - task
     - workflow
     - date range
   - Include enough summary fields for list rendering:
     - timestamp
     - actor display
     - action
     - target display
     - outcome
     - short rationale summary
   - Ensure all queries are tenant-scoped before any other filtering.

6. **Add query model for audit detail**
   - Implement a detail query by audit event id.
   - Include:
     - core audit fields
     - rationale summary
     - human-readable data sources used
     - linked approvals
     - linked tool executions
     - linked task/workflow
     - affected entities
   - Resolve related entity display names where practical, but avoid N+1-heavy designs if the codebase lacks projection helpers.

7. **Enforce authorization**
   - Audit views must respect role-based access.
   - Reuse existing authorization policies if available; otherwise add a focused policy for audit viewing.
   - Ensure unauthorized cross-tenant access returns forbidden/not found according to existing API conventions.

8. **Expose API or server endpoints**
   - Add endpoints for:
     - audit history list
     - audit detail
   - Follow existing API style and response contracts.
   - Keep endpoints thin; business logic belongs in application layer.

9. **Add Blazor audit/explainability views**
   - Implement or complete web views for:
     - audit history page with filters
     - audit detail page
   - Show concise operational explanations only.
   - Make data source references human-readable.
   - Include links/navigation to related task, workflow, approval, or agent pages if routes already exist.
   - Respect role-based visibility in the UI, but do not rely on UI-only security.

10. **Seed or backfill only if necessary**
   - Do not invent large fake datasets.
   - If needed for local usability/tests, add minimal fixtures or test data builders.

11. **Add tests**
   - Cover:
     - audit event persistence
     - tenant scoping
     - filter behavior
     - detail retrieval with related links
     - authorization restrictions
     - no raw reasoning leakage in returned DTOs
   - Prefer integration tests where API/query behavior matters.

12. **Keep implementation incremental and safe**
   - If full end-to-end wiring across all modules is too large, prioritize:
     1. durable audit entity + migration
     2. query endpoints
     3. web views
     4. wiring from at least one or two core business flows such as approvals/tool executions/tasks

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. Validate schema/migrations:
   - confirm the audit table/mapping is created correctly
   - confirm indexes exist for expected filters
   - confirm JSONB fields, if used, serialize/deserialize correctly

5. Validate functional behavior manually or via tests:
   - create or seed audit events for a company
   - query audit history by:
     - agent
     - task
     - workflow
     - date range
   - open audit detail and verify related approvals/tool executions/entities appear when linked
   - verify rationale summaries are concise and no raw chain-of-thought fields are exposed

6. Validate security:
   - confirm tenant A cannot read tenant B audit events
   - confirm users without the proper role/policy cannot access audit views/endpoints
   - confirm API behavior matches existing forbidden/not-found conventions

7. Validate UI:
   - audit history renders with filters and sensible empty states
   - audit detail renders human-readable source references
   - related entity links work where routes exist

# Risks and follow-ups
- **Risk: partial existing audit model**
  - The repo may already have activity/history constructs. Avoid duplicating concepts; consolidate toward a single business audit model.

- **Risk: unclear migration workflow**
  - The workspace references archived PostgreSQL migration docs. Confirm the active migration process before generating schema changes.

- **Risk: over-coupling audit writes**
  - Do not scatter ad hoc persistence logic across controllers/pages. Centralize audit writing behind an application service or command.

- **Risk: leaking sensitive reasoning**
  - Be strict about returning only rationale summaries and explainability metadata, never raw prompts, hidden reasoning, or chain-of-thought.

- **Risk: authorization gaps**
  - UI hiding is insufficient. Enforce tenant and role checks in application/API layers.

- **Risk: performance**
  - Audit history can grow quickly. Add indexes and paging from the start.

Suggested follow-ups after this task:
- broaden audit emission coverage across all major business flows
- add exported audit bundles/compliance review support
- add richer affected-entity diff views where useful
- add notification hooks from critical audit outcomes
- add retention/archival policies for long-term audit storage