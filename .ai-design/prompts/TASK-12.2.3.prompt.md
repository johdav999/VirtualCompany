# Goal
Implement backlog task **TASK-12.2.3** for **ST-602 Audit trail and explainability views** so that an **action detail view** shows **linked approvals, tool executions, and affected entities where available**, while preserving tenant isolation, role-based access, and concise explainability.

# Scope
Deliver the minimum complete vertical slice needed for this task in the existing .NET solution:

- Add or extend backend query support for **action detail** in the Audit & Explainability area.
- Ensure the action detail response can include:
  - core audit/action metadata
  - linked approvals
  - linked tool executions
  - affected entities
- Wire the web UI to render these sections only when data exists.
- Keep explanations operational and concise; do **not** expose raw chain-of-thought.
- Enforce **company scoping** and existing authorization patterns.
- Add tests covering query behavior, tenant isolation, and conditional rendering behavior where practical.

Out of scope unless required by existing code structure:

- Large redesign of the audit domain
- New mobile UI
- Broad notification/inbox changes
- Reworking unrelated audit list/filter pages
- Inventing a full generic graph model if a pragmatic targeted implementation is sufficient

If the codebase already has partial audit detail support, extend it rather than duplicating patterns.

# Files to touch
Inspect the solution first and then update the most relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - audit event entities/value objects
  - any entity-link or explainability models
- `src/VirtualCompany.Application/**`
  - audit detail query/handler
  - DTO/view model for action detail
  - authorization/query services/interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories/query projections
  - SQL/query composition for linked approvals, tool executions, affected entities
- `src/VirtualCompany.Api/**`
  - audit detail endpoint if not already exposed
  - response contracts/mapping
- `src/VirtualCompany.Web/**`
  - audit/action detail page/component
  - sections for approvals, tool executions, affected entities
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query integration tests
- Potentially:
  - migration files if schema changes are truly necessary
  - shared contracts in `src/VirtualCompany.Shared/**`

Prefer touching existing audit/explainability files over creating parallel structures.

# Implementation plan
1. **Discover existing audit detail flow**
   - Find current implementation for ST-602-related audit history and detail pages/endpoints.
   - Identify:
     - audit event entity/schema
     - current detail DTO/view model
     - how approvals and tool executions are currently linked, if at all
     - whether “affected entities” already exist in schema or are derivable
   - Reuse established CQRS-lite patterns and naming conventions.

2. **Define the action detail contract**
   - Extend the application/API response model for action detail to include:
     - `Approvals` collection
     - `ToolExecutions` collection
     - `AffectedEntities` collection
   - Include only concise, user-facing fields. Suggested shape:
     - approval: id, type, status, required role/user, created/decided timestamps, decision summary
     - tool execution: id, tool name, action type, status, started/completed timestamps, summarized request/response or safe metadata
     - affected entity: entity type, entity id, display label/summary, relationship/impact if available
   - Keep fields nullable/optional and omit or render empty sections gracefully.

3. **Implement backend query composition**
   - Update the audit action detail query handler/repository to load linked data.
   - Link approvals using the most reliable existing relationship available, likely one of:
     - audit target/entity references
     - approval `entity_type/entity_id`
     - task/workflow/action foreign references
   - Link tool executions using existing references such as:
     - task id
     - workflow instance id
     - agent id plus correlation/action context if present
   - Populate affected entities from existing audit metadata if available. If not directly modeled:
     - derive from audit target + related linked entities
     - use structured metadata/JSON fields already present
   - Do not fabricate data; only return affected entities “where available.”

4. **Handle affected entities pragmatically**
   - First preference: use an existing persisted relation/JSON field for affected entities.
   - Second preference: derive a small set of obvious affected entities from linked records:
     - target entity itself
     - approval-linked entity
     - tool execution target references if safely available
   - If the schema lacks a dedicated structure and derivation is impossible, add a minimal extensible representation only if necessary and justified by current architecture.
   - Avoid introducing a heavy polymorphic framework unless the codebase already uses one.

5. **Preserve security and tenancy**
   - Ensure all queries are scoped by `company_id`.
   - Respect role-based access already used by audit views.
   - Return not found/forbidden consistently for cross-tenant access.
   - Do not expose sensitive request/response payload details from tool executions if existing patterns redact them.

6. **Update the web action detail UI**
   - Extend the action detail page/component to render:
     - linked approvals section
     - tool executions section
     - affected entities section
   - Show sections only when data exists.
   - Keep the UI concise and operational:
     - status badges
     - timestamps
     - short summaries
     - links to related detail pages where routes already exist
   - Do not expose raw reasoning or internal-only payloads.

7. **Add tests**
   - Backend tests should cover:
     - action detail returns linked approvals when present
     - action detail returns linked tool executions when present
     - action detail returns affected entities when present
     - action detail omits/returns empty collections when absent
     - tenant isolation prevents cross-company access
   - UI/component tests if the project already uses them; otherwise keep UI validation lightweight and focus on API/application tests.

8. **Schema changes only if unavoidable**
   - Before adding migrations, verify whether current tables already support this task:
     - `approvals`
     - `tool_executions`
     - `audit_events`
   - If a schema change is required, keep it minimal and aligned with the architecture’s auditability guidance.
   - Document why the change was necessary.

9. **Keep implementation explainability-focused**
   - Ensure the final detail view supports trust/review/override workflows by surfacing linked operational artifacts.
   - Favor human-readable labels and summaries over raw JSON blobs.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Open an audit/action detail record with linked approval(s) and confirm the approvals section appears with correct status and timestamps.
   - Open an audit/action detail record with linked tool execution(s) and confirm tool name, action type, and status render correctly.
   - Open a record with affected entities and confirm they are shown with readable labels.
   - Open a record without linked data and confirm sections are hidden or empty-state behavior is clean.
   - Verify links navigate correctly if related detail pages already exist.

4. Security verification:
   - Attempt to access an action detail from another tenant/company context and confirm access is denied or not found per existing conventions.
   - Verify no raw chain-of-thought or unsafe tool payload details are exposed.

5. Data correctness verification:
   - Confirm linked approvals are the correct ones for the action, not merely same-agent or same-day records.
   - Confirm tool executions are linked through the intended entity/task/workflow relationship.
   - Confirm affected entities are only shown when confidently known.

# Risks and follow-ups
- **Ambiguous linkage risk:** The current schema may not provide a single canonical relation between audit events, approvals, and tool executions. Prefer the strongest existing linkage and document assumptions in code comments.
- **Affected entities modeling gap:** “Affected entities” may not yet be explicitly stored. If derivation is weak, implement a conservative partial result now and note a follow-up for richer persisted entity links.
- **Payload sensitivity risk:** Tool execution request/response data may contain sensitive details. Reuse any existing redaction/safe-summary logic instead of exposing raw JSON.
- **UI route dependency:** Related detail links for approvals/tasks/workflows may not all exist. Only add links where routes already exist or can be added trivially.
- **Test fixture complexity:** It may take extra setup to create realistic linked audit/approval/tool execution records. Favor focused integration tests over brittle end-to-end coverage.

Potential follow-ups after this task:
- Persist first-class affected-entity references on audit events for stronger explainability.
- Add richer human-readable source attribution and impact summaries.
- Add filtering/drill-down from action detail into approval and tool execution detail pages if not already present.