# Goal
Implement backlog task **TASK-12.2.2** for **ST-602 Audit trail and explainability views** so users can **view audit history filtered by agent, task, workflow, and date range** in the .NET solution.

This task should deliver the minimum complete vertical slice needed to support filtered audit history in the web app, aligned with the architecture and story intent:
- tenant-scoped audit history query support
- backend API/query layer for filtering
- application/domain/infrastructure wiring
- Blazor UI for filter inputs and results display
- role-aware and tenant-safe access
- tests covering filtering behavior and isolation

Use existing project patterns and naming conventions in the repository. Prefer incremental, production-ready implementation over speculative abstraction.

# Scope
In scope:
- Add or complete the **audit history read model/query path** for filtering by:
  - agent
  - task
  - workflow
  - date range
- Ensure all queries are **company/tenant scoped**
- Expose a backend endpoint or query handler used by the web UI
- Add/update Blazor page/components to:
  - render audit history
  - allow filter selection
  - show empty/loading/error states
- Return audit event fields appropriate for history listing, such as:
  - timestamp
  - actor
  - action
  - target
  - outcome
  - rationale summary
- Keep explainability concise; do **not** expose raw chain-of-thought
- Add tests for filtering and tenant isolation

Out of scope unless already partially implemented and required to complete this task:
- full audit event creation pipeline for all modules
- export/download of audit bundles
- advanced pagination/sorting beyond what is necessary
- action detail drill-down page for approvals/tool executions/affected entities
- mobile UI
- broad redesign of audit schema

If the codebase already contains partial audit infrastructure, extend it rather than replacing it.

# Files to touch
Inspect first, then modify only the necessary files across these likely areas:

- `src/VirtualCompany.Domain/**`
  - audit event entity/value objects/specifications if needed
- `src/VirtualCompany.Application/**`
  - audit queries, DTOs, handlers, validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations, repositories, query implementations
- `src/VirtualCompany.Api/**`
  - controller/endpoints for audit history retrieval
- `src/VirtualCompany.Web/**`
  - Blazor pages/components/view models for audit history filters and results
- `tests/VirtualCompany.Api.Tests/**`
  - API/query integration tests for filter behavior and tenant isolation

Also inspect:
- existing auth/tenant resolution patterns
- existing CQRS/query conventions
- existing list/filter UI patterns in web project
- any existing audit-related migrations/configurations

Do **not** create new projects. Avoid touching mobile unless required by shared contracts.

# Implementation plan
1. **Discover existing audit implementation**
   - Search for:
     - `Audit`
     - `AuditEvent`
     - explainability views
     - existing dashboard/activity feed queries
   - Identify:
     - current audit entity/schema
     - whether `audit_events` already exists in EF/migrations
     - current tenant scoping approach
     - current API + Blazor data flow conventions

2. **Confirm/complete audit event read model**
   - Ensure there is a queryable audit event model with fields needed for listing.
   - Based on architecture/backlog, support at minimum:
     - `company_id`
     - `actor_type`
     - `actor_id`
     - `action`
     - `target_type`
     - `target_id`
     - `outcome`
     - `rationale_summary`
     - timestamp (`created_at` or equivalent)
   - If schema already includes references for agent/task/workflow, use them directly.
   - If not, derive filtering from existing fields in the least invasive way, e.g.:
     - agent filter via actor agent id and/or target metadata if already stored
     - task/workflow filter via target type/id or related reference columns
   - Do not invent a large new schema unless absolutely necessary.

3. **Add application query contract**
   - Create or extend a query such as `GetAuditHistoryQuery` with:
     - optional `AgentId`
     - optional `TaskId`
     - optional `WorkflowInstanceId` or `WorkflowId` based on existing model
     - optional `FromUtc`
     - optional `ToUtc`
     - optional paging if repository conventions require it
   - Add response DTOs for audit history items.
   - Validate:
     - date range is sensible
     - `FromUtc <= ToUtc`
     - optional IDs are well-formed
   - Keep query read-only and CQRS-lite.

4. **Implement tenant-scoped filtering**
   - In the handler/repository/query service:
     - always scope by current company
     - apply optional filters only when provided
   - Filtering rules:
     - **agent**: return events associated with the selected agent
     - **task**: return events associated with the selected task
     - **workflow**: return events associated with the selected workflow instance/definition per existing model
     - **date range**: inclusive lower bound, sensible upper bound handling
   - Sort newest first unless existing UX conventions differ.

5. **Expose API endpoint**
   - Add or extend an endpoint under existing audit routes, e.g.:
     - `GET /api/audit/history`
   - Accept query string parameters for the filters.
   - Use existing authorization and tenant context resolution.
   - Return safe, user-facing DTOs only.
   - Ensure forbidden/not-found behavior follows existing tenant-aware API patterns.

6. **Build/update Blazor audit history UI**
   - Add or update the audit history page in `src/VirtualCompany.Web`.
   - Include filter controls for:
     - agent selector
     - task selector/input
     - workflow selector/input
     - from date
     - to date
   - If selector data sources do not yet exist, use the simplest consistent UX:
     - IDs or existing dropdown sources if available
     - avoid building unrelated lookup systems unless necessary
   - Render results in a clear list/table with:
     - timestamp
     - actor
     - action
     - target
     - outcome
     - rationale summary
   - Include:
     - loading state
     - empty state
     - error state
     - clear filters action
   - Respect role-based access patterns already used in the app.

7. **Keep explainability safe**
   - Ensure UI/API only surfaces concise operational summaries.
   - Do not expose raw prompts, hidden reasoning, or chain-of-thought.
   - If data source references already exist in DTOs, they may be omitted from this task unless needed for the list view.

8. **Testing**
   - Add tests covering:
     - returns only current tenant/company audit events
     - filter by agent
     - filter by task
     - filter by workflow
     - filter by date range
     - combined filters narrow results correctly
     - invalid date range returns validation error or safe failure per project conventions
   - Prefer existing test style in `VirtualCompany.Api.Tests`.

9. **Keep implementation minimal and aligned**
   - Reuse existing abstractions.
   - Avoid introducing MediatR/FluentValidation/etc. if the repo does not already use them.
   - Avoid speculative generic filtering frameworks.
   - Keep naming explicit and domain-oriented.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API behavior:
   - query audit history with no filters
   - query with `agentId`
   - query with `taskId`
   - query with `workflowId` or `workflowInstanceId`
   - query with `from`/`to`
   - query with combined filters
   - verify cross-tenant data is never returned

4. Manually verify web UI:
   - open audit history page
   - apply each filter independently
   - apply combined filters
   - clear filters
   - confirm empty state when no matches
   - confirm invalid date range handling
   - confirm results are ordered sensibly and display concise summaries

5. If migrations/schema changes were required:
   - ensure app starts successfully
   - verify EF mapping and query translation work against PostgreSQL-compatible patterns

# Risks and follow-ups
- **Schema ambiguity risk:** The architecture excerpt for `audit_events` is truncated. First inspect the actual entity/schema before changing anything. Prefer adapting to the existing model.
- **Filter semantics risk:** “workflow” may mean workflow definition or workflow instance. Match the existing domain model and naming already present in code.
- **Agent association risk:** Some audit events may not have a direct `agent_id`. If so, use the most reliable existing association and document the limitation in code comments or task notes.
- **Lookup UX risk:** Agent/task/workflow selectors may require supporting queries not yet present. Keep this lightweight and reuse existing list endpoints if available.
- **Authorization risk:** Audit views must respect role-based access and tenant boundaries. Do not bypass existing authorization patterns.
- **Performance follow-up:** If audit volume is high, a later task may need pagination, indexes, and cached lookups.
- **Story follow-up:** ST-602 also mentions action detail with linked approvals, tool executions, and affected entities; that is a likely subsequent task beyond this filter-focused backlog item.