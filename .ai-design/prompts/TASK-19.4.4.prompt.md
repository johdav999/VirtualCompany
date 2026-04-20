# Goal
Implement backlog task **TASK-19.4.4 — Wire deep links between events, executions, approvals, and finance actions** for **US-19.4 ST-FUI-304 — Finance events and tool execution transparency**.

Deliver an admin-facing finance transparency experience in the Blazor web app that lets users:

- browse finance domain events
- open event details
- inspect finance tool manifests
- review finance tool execution history
- navigate from execution details to related approval requests
- navigate from execution details to originating finance actions
- navigate across related records using correlation and entity references when available

The implementation must align with the modular monolith architecture, CQRS-lite application layer, tenant-scoped access, and auditability-first design.

# Scope
In scope:

- Add or complete tenant-scoped query models and handlers for:
  - finance event stream
  - finance event detail
  - finance tool manifests
  - finance tool execution history
  - finance tool execution detail with related links
- Add UI pages/components in the admin area for:
  - finance event stream list
  - finance event detail
  - finance tool manifest list
  - finance execution history list
  - finance execution detail
- Wire deep links between:
  - event → related execution(s), approval(s), finance action(s) where identifiers exist
  - execution → approval request where related identifier exists
  - execution → originating finance action where related identifier exists
  - execution/event → related entity detail via affected entity reference/correlation identifier where supported
- Show payload/request/response summaries and trigger consumption tracing only when backend data exists
- Preserve role/tenant scoping and safe empty states

Out of scope unless required by existing code patterns:

- creating new finance domain workflows
- changing orchestration behavior
- introducing new external integrations
- redesigning unrelated admin navigation
- exposing raw chain-of-thought or unsafe internal payloads
- broad schema redesigns beyond minimal additions needed to support links/queryability

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas:

- `src/VirtualCompany.Web/**`
  - admin finance pages/components
  - shared navigation/link components
  - route definitions
- `src/VirtualCompany.Application/**`
  - finance transparency queries/DTOs/handlers
  - approval/action/execution linking query services
- `src/VirtualCompany.Domain/**`
  - read model contracts or domain enums/value objects if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF/query implementations
  - repository/query service wiring
  - projections or SQL for finance event/execution reads
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if web app consumes API endpoints rather than direct application services
- `src/VirtualCompany.Shared/**`
  - shared DTOs/view models if used across layers
- `tests/VirtualCompany.Api.Tests/**`
  - API/query integration tests
- any relevant web test project if present
- migration files only if absolutely necessary to expose missing link identifiers

Also inspect for existing concepts matching these names before adding anything new:

- finance events / domain events
- tool manifests
- tool executions
- approvals
- finance actions
- audit/explainability views
- correlation id / correlation identifier
- affected entity reference
- trigger consumption tracing

# Implementation plan
1. **Discover existing implementation and map current data sources**
   - Search the codebase for:
     - finance admin pages
     - event stream/detail pages
     - tool manifest pages
     - tool execution history/detail pages
     - approval detail pages
     - finance action detail pages
     - correlation IDs and related identifiers in persistence models
   - Identify whether the web app uses:
     - direct application-layer MediatR/CQRS calls
     - API endpoints
     - server-side services
   - Reuse existing patterns for admin list/detail pages and deep-link rendering.

2. **Define the read model contract for transparency views**
   - Ensure there are query DTOs/view models for:
     - `FinanceEventListItem`
       - event type
       - timestamp
       - correlation identifier
       - affected entity reference
       - optional related execution/approval/action ids
     - `FinanceEventDetail`
       - summary payload
       - trigger consumption tracing
       - related links collection
     - `FinanceToolManifestListItem`
       - tool name
       - version metadata
       - contract/schema summary
       - provider adapter identity
     - `FinanceToolExecutionListItem`
       - lifecycle state
       - request summary
       - response summary
       - execution timestamp
       - optional approval id
       - optional finance action id
     - `FinanceToolExecutionDetail`
       - all above plus related links and correlation/entity references
   - Keep summaries concise and safe for UI display.

3. **Implement or complete application queries**
   - Add/extend tenant-scoped query handlers for:
     - finance event stream
     - finance event detail
     - finance tool manifests
     - finance tool execution history
     - finance tool execution detail
   - Query handlers must:
     - filter by company/tenant
     - return only available related identifiers
     - avoid failing when optional relationships are absent
     - normalize payload/request/response into summary fields
     - include trigger consumption tracing only when present in backend data
   - If there is no direct finance action table but actions are represented as tasks/workflows/audit targets, map to the existing canonical action detail route rather than inventing a new concept.

4. **Resolve deep-link relationships**
   - Implement relationship resolution using existing identifiers in this priority order:
     - explicit foreign keys/related ids
     - correlation identifier joins
     - affected entity reference / target reference
     - audit linkage if already modeled
   - For execution detail, expose:
     - approval request link when related approval identifier exists
     - originating finance action link when related action identifier exists
   - For event detail, expose:
     - related execution(s)
     - related approval(s)
     - related finance action(s)
     - affected entity link if the entity type/route is known
   - Do not fabricate links when identifiers are missing or ambiguous.

5. **Build/update Blazor admin pages**
   - Add or complete pages for:
     - finance event stream list
     - finance event detail
     - finance tool manifests
     - finance execution history list
     - finance execution detail
   - Each page should:
     - show loading, empty, error states
     - render concise summaries
     - display related links as clickable deep links only when available
     - preserve current admin layout/navigation patterns
   - Event stream list must display:
     - event type
     - timestamp
     - correlation identifier
     - affected entity reference
   - Event detail must display:
     - payload summary
     - trigger consumption tracing when available
   - Tool manifest list must display:
     - tool name
     - version metadata
     - contract/schema summary
     - provider adapter identity
   - Execution history must display:
     - lifecycle state
     - request summary
     - response summary
     - execution timestamp

6. **Add reusable related-link UI helpers if appropriate**
   - If the codebase already has chips/badges/link rows for related entities, reuse them.
   - Otherwise add a small reusable component for “Related records” that can render:
     - approvals
     - finance actions
     - executions
     - affected entities
     - correlation identifier
   - Keep styling consistent with existing admin UI.

7. **Handle payload and tracing summaries safely**
   - Summaries should be human-readable and bounded in size.
   - Prefer:
     - selected fields
     - compact JSON preview
     - schema/contract summary text
   - Never expose raw chain-of-thought or unsafe secrets.
   - If payloads are null/empty, show a clear “No payload summary available” style message.

8. **Add routing and navigation wiring**
   - Ensure all deep links resolve to valid routes.
   - If approval/action detail pages already exist, link to them directly.
   - If a target detail page does not exist but a list page does, link to the best available destination with query/filter context if that is an established pattern.
   - Preserve tenant/admin authorization checks.

9. **Test coverage**
   - Add tests for query handlers and/or API endpoints covering:
     - event stream fields
     - event detail payload summary and optional tracing
     - tool manifest fields
     - execution history fields
     - execution detail related approval/action links
     - missing optional identifiers
     - tenant isolation
   - Add UI/component tests if the project already uses them; otherwise keep to application/API tests plus manual validation.

10. **Keep implementation minimal and idiomatic**
   - Prefer extending existing read models and pages over introducing parallel abstractions.
   - Avoid speculative architecture.
   - Follow existing naming, folder structure, and dependency direction.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate in the web admin area:
   - Open finance event stream
   - Confirm each row shows:
     - event type
     - timestamp
     - correlation identifier
     - affected entity reference
   - Open an event detail page
   - Confirm payload summary is shown
   - Confirm trigger consumption tracing appears only when backend data exists
   - Open finance tool manifests
   - Confirm each item shows:
     - tool name
     - version metadata
     - contract/schema summary
     - provider adapter identity
   - Open finance execution history
   - Confirm each row shows:
     - lifecycle state
     - request summary
     - response summary
     - execution timestamp
   - Open an execution detail page with related identifiers
   - Confirm deep links to:
     - approval request
     - originating finance action
   - Confirm links are hidden or replaced with a non-breaking empty state when identifiers are unavailable

4. Validate tenant isolation:
   - Confirm records from another company are not visible or linkable
   - Confirm direct navigation to another tenant’s record is forbidden/not found per existing conventions

5. Validate route integrity:
   - Click through all related links from event and execution detail pages
   - Ensure no broken routes or null-reference rendering errors

# Risks and follow-ups
- **Data model mismatch risk:** Existing persistence may not store explicit finance action or approval identifiers on events/executions. If so, use correlation/entity-reference-based linking where reliable, and document any remaining gaps.
- **Ambiguous “finance action” concept:** The codebase may represent finance actions as tasks, workflows, audit targets, or another entity. Reuse the existing canonical detail route instead of inventing a new model.
- **Tracing availability risk:** Trigger consumption tracing may be partially implemented or stored in heterogeneous payloads. Render conditionally and avoid brittle parsing.
- **UI fragmentation risk:** There may already be audit/explainability pages with overlapping functionality. Prefer integrating with those patterns rather than duplicating screens.
- **Schema change risk:** If minimal schema additions are required to support stable deep links, keep them narrowly scoped and backward-compatible.
- **Follow-up suggestion:** If relationship resolution relies heavily on correlation IDs, consider a later task to formalize explicit linkage fields/projections for event ↔ execution ↔ approval ↔ action navigation.