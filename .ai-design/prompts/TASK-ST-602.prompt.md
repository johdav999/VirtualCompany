# Goal
Implement **TASK-ST-602 — Audit trail and explainability views** for the existing .NET solution so managers can inspect what agents did and why, through tenant-scoped, role-aware audit APIs and web views.

This task should deliver the core vertical slice for:
- persisting and querying business audit history
- exposing concise explainability details without raw chain-of-thought
- filtering audit history by agent, task, workflow, and date range
- showing linked approvals, tool executions, and affected entities where available
- enforcing tenant isolation and role-based access in all audit queries and UI

# Scope
Implement the story in a way that fits the current modular monolith and CQRS-lite architecture.

Include:
- Domain/application/infrastructure/web support for **business audit events**
- Read models and query handlers for:
  - audit history list
  - audit detail view
- Blazor web pages/components for:
  - audit history index with filters
  - audit detail page
- Human-readable explainability presentation using:
  - rationale summary
  - data source references
  - linked approvals
  - linked tool executions
  - linked target/affected entities where available
- Tenant-aware authorization and filtering
- Tests for query behavior, authorization boundaries, and UI/API integration where the repo patterns support it

Do not include:
- raw chain-of-thought storage or display
- mobile implementation
- broad observability/technical logging changes
- unrelated notification/inbox work from ST-603
- speculative microservice/event-bus refactors

If the repository already contains partial audit entities or migrations, extend them rather than duplicating concepts.

# Files to touch
Inspect the solution first and adapt to actual conventions. Likely areas:

- `src/VirtualCompany.Domain/**`
  - audit domain entity/value objects/enums if missing
- `src/VirtualCompany.Application/**`
  - audit queries, DTOs, handlers, authorization policies/interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - repositories/query services
  - migrations or migration registration if this repo uses active migrations
- `src/VirtualCompany.Api/**`
  - audit endpoints/controllers/minimal APIs if the web app does not call application services directly
  - authorization wiring
- `src/VirtualCompany.Web/**`
  - Blazor pages/components for audit list and detail views
  - navigation entry points from relevant pages if appropriate
- `src/VirtualCompany.Shared/**`
  - shared contracts/view models if this solution uses them
- `tests/VirtualCompany.Api.Tests/**`
  - integration/endpoint tests
- Other test projects if present in the repo and more appropriate

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing architecture and audit support**
   - Inspect solution structure, naming conventions, and module boundaries.
   - Find whether these already exist:
     - `audit_events` persistence
     - task/approval/tool execution entities
     - tenant context abstraction
     - authorization policies/roles
     - Blazor routing/layout patterns
     - CQRS query handler conventions
   - Reuse existing patterns exactly.

2. **Model the audit event shape**
   - Ensure there is a business audit entity/table aligned with the architecture intent.
   - Minimum fields needed for this story:
     - id
     - company/tenant id
     - actor type
     - actor id
     - action
     - target type
     - target id
     - outcome
     - rationale summary
     - data sources used / source references
     - created at
     - correlation id if the system already uses one
     - metadata JSON for extensibility if consistent with repo patterns
   - If the schema is incomplete, add only the fields needed for ST-602.
   - Keep explanations concise and operational; do not add any field intended for raw reasoning traces.

3. **Define read-side contracts**
   - Add application-layer DTOs/view models for:
     - `AuditHistoryListItem`
     - `AuditHistoryFilter`
     - `AuditDetailDto`
     - nested linked records for approvals, tool executions, and affected entities
   - Include human-readable source references, not opaque internal-only payloads.

4. **Implement audit history query**
   - Add a query/handler or repository method that returns paged/sorted audit events filtered by:
     - agent
     - task
     - workflow
     - date range
   - Also support default sorting by newest first.
   - Enforce:
     - tenant scoping
     - role-based access
     - safe handling of missing/foreign-tenant IDs
   - If task/workflow linkage is indirect, derive it from target type/target id and related records where feasible.

5. **Implement audit detail query**
   - Add a query/handler for a single audit event detail page.
   - Include:
     - core audit event fields
     - rationale summary
     - data sources used
     - linked approval(s), if any
     - linked tool execution(s), if any
     - affected entity references, if any
   - Prefer concise summaries over dumping raw JSON.
   - If structured JSON exists, map it into readable sections.

6. **Link related entities**
   - Join or compose related data from existing modules:
     - approvals
     - tool executions
     - tasks/workflows/agents where relevant
   - Show references and statuses, not full internal payloads unless already safe and user-facing.
   - If exact linkage is not consistently available in current schema, implement best-effort linking and document gaps in follow-ups.

7. **Add authorization**
   - Ensure only authorized human users can access audit views.
   - Respect tenant isolation on every query.
   - Respect role-based access for audit pages/endpoints.
   - If no dedicated policy exists, add a focused policy for audit review using existing role conventions from backlog notes.

8. **Build Blazor audit history page**
   - Add a page under the existing navigation structure, likely something like:
     - `/audit`
   - Include:
     - filter controls for agent, task, workflow, date range
     - results table/list
     - outcome/status badges
     - actor/action/target summary
     - rationale summary preview
     - link to detail page
   - Handle:
     - empty state
     - loading state
     - invalid filters
     - unauthorized state if applicable

9. **Build Blazor audit detail page**
   - Add a page likely like:
     - `/audit/{id}`
   - Show:
     - who acted
     - what action occurred
     - target and outcome
     - when it happened
     - concise explanation/rationale summary
     - data sources used
     - linked approvals
     - linked tool executions
     - affected entities
   - Make source references human-readable.
   - Do not expose chain-of-thought, hidden prompts, or unsafe internal reasoning artifacts.

10. **Add navigation and contextual entry points**
    - If low effort and consistent with current UI, add links into audit detail from:
      - task detail
      - workflow detail
      - agent profile/recent activity
    - Keep this limited to obvious existing surfaces; do not create broad UX churn.

11. **Persist audit events where needed**
    - If the system already creates audit events for important actions, reuse them.
    - If not enough events exist to make the views useful, add audit writes for the most relevant existing flows already in codebase:
      - tool execution allow/deny
      - approval creation/decision
      - task state changes tied to agent actions
    - Keep this incremental and focused on ST-602, not a full audit overhaul.

12. **Testing**
    - Add tests for:
      - tenant isolation in audit queries
      - filter behavior
      - detail query composition
      - authorization enforcement
      - no raw reasoning leakage in returned DTOs/UI models
    - Prefer integration tests where data joins matter.

13. **Documentation**
    - Add brief notes in code comments or README-adjacent docs if needed:
      - what counts as a business audit event
      - explainability constraints
      - known linkage limitations

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, verify:
   - audit history page loads for an authorized user
   - filters work for:
     - agent
     - task
     - workflow
     - date range
   - audit detail page shows:
     - actor
     - action
     - target
     - outcome
     - rationale summary
     - data sources used
     - linked approvals/tool executions when present
   - unauthorized or cross-tenant access is denied or not found per existing conventions
   - no raw chain-of-thought or hidden prompt content is returned

4. Run full automated validation:
   - `dotnet test`

5. If migrations are added, verify migration approach matches repo conventions from:
   - `docs/postgresql-migrations-archive/README.md`

# Risks and follow-ups
- **Schema uncertainty:** The architecture excerpt shows `audit_events`, but the actual repo may not yet have it fully implemented. If missing, add the smallest viable schema and document assumptions.
- **Linkage gaps:** Existing approvals/tool executions/tasks may not have consistent correlation IDs or foreign-key relationships. Implement best-effort joins now and note follow-up work for stronger linkage.
- **Authorization ambiguity:** Human role names may exist without a dedicated audit-review policy. Reuse current role patterns and add a minimal policy if needed.
- **UI consistency:** Blazor app patterns may favor SSR, components, or API-backed pages. Match existing style rather than introducing a new pattern.
- **Migration strategy:** The repo may archive migrations or use a specific workflow. Follow repository conventions exactly.
- **Future follow-up candidates:**
  - exportable audit bundles
  - richer affected-entity diffs
  - correlation-id drill-through across task/workflow/tool execution
  - compliance review workflows
  - mobile audit summaries, if later requested

Use this task as a **focused, production-quality vertical slice** for auditability and explainability, not as a broad platform rewrite.