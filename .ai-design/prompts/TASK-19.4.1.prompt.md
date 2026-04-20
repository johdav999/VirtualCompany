# Goal
Implement backlog task **TASK-19.4.1 — Finance domain event stream list and detail drawer** for story **US-19.4 ST-FUI-304 — Finance events and tool execution transparency**.

Deliver an admin-facing finance transparency UI in the Blazor web app that:
- Lists finance domain events with:
  - event type
  - timestamp
  - correlation identifier
  - affected entity reference
- Opens an event detail drawer/view showing:
  - payload summary
  - trigger consumption tracing when backend data exists
- Lists finance tool manifests with:
  - tool name
  - version metadata
  - contract/schema summary
  - provider adapter identity
- Shows finance tool execution history with:
  - lifecycle state
  - request summary
  - response summary
  - execution timestamp
- Links execution detail to:
  - approval requests
  - originating finance actions
  - when related identifiers are available

Use existing architecture and conventions in this repository. Prefer incremental, production-shaped implementation over placeholder-only UI.

# Scope
In scope:
- Add or extend backend query endpoints/application services needed by the web admin UI
- Add DTO/view model/query models for finance event stream, event detail, tool manifests, and tool execution history/detail
- Implement Blazor admin pages/components for:
  - finance event stream list
  - event detail drawer/panel
  - finance tool manifest list
  - finance tool execution history list
  - execution detail linking
- Ensure tenant/company scoping is preserved
- Surface correlation IDs and related entity references consistently
- Gracefully handle missing optional backend data for tracing/links
- Add tests for query/application logic and any API contract behavior that is introduced

Out of scope unless already trivially supported by existing code:
- New finance orchestration engine behavior
- New persistence model redesigns
- Full event sourcing
- Mobile implementation
- Deep schema registry infrastructure
- Large refactors unrelated to finance transparency views

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely backend:
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Api/**`

Likely web UI:
- `src/VirtualCompany.Web/**`

Likely tests:
- `tests/VirtualCompany.Api.Tests/**`
- any existing application/web test projects if present

Potential file categories to add/update:
- Finance transparency queries/handlers
- API endpoints/controllers/minimal API mappings
- Repository/query service interfaces and implementations
- Shared contracts/DTOs
- Blazor pages under admin/finance/audit/transparency areas
- Reusable drawer/detail/list components
- Navigation/menu entries if appropriate
- Test fixtures and integration/unit tests

Before coding, locate:
- Existing admin area routing/layout patterns
- Existing CQRS-lite query patterns
- Existing audit/tool execution/approval DTOs and endpoints
- Existing tenant/company context resolution
- Existing drawer/modal component patterns in Blazor
- Existing finance module naming conventions

# Implementation plan
1. **Discover existing finance transparency building blocks**
   - Search for:
     - `audit_events`
     - `tool_executions`
     - approvals
     - finance actions
     - correlation ID usage
     - event stream or activity feed UI
   - Determine whether finance domain events already exist as persisted records, projections, or API contracts.
   - Reuse existing entities/tables/contracts where possible instead of inventing parallel models.

2. **Define the read models**
   Create focused query DTOs/view models for:
   - `FinanceDomainEventListItem`
     - id
     - event type
     - occurred/created timestamp
     - correlation id
     - affected entity type/id or display reference
   - `FinanceDomainEventDetail`
     - id
     - event type
     - timestamp
     - correlation id
     - entity reference
     - payload summary
     - trigger consumption trace collection (optional)
   - `FinanceToolManifestListItem`
     - tool name
     - version
     - schema/contract summary
     - provider adapter identity
   - `FinanceToolExecutionListItem`
     - execution id
     - tool name
     - lifecycle state/status
     - request summary
     - response summary
     - execution timestamp
     - correlation id if available
   - `FinanceToolExecutionDetail`
     - above fields plus
     - approval request id/link target if available
     - originating finance action id/reference if available
     - related task/workflow/entity refs if available

3. **Map backend data sources pragmatically**
   Based on what exists in code/schema:
   - Use persisted audit/business event records for finance domain events
   - Use `tool_executions` for execution history
   - Use approval entities for approval linkage
   - Use any finance action/task/workflow identifiers already stored in audit or execution metadata
   - If payloads are JSON-heavy, generate concise summaries server-side rather than dumping raw JSON into the list UI
   - For trigger consumption tracing:
     - expose a collection only when backend trace data exists
     - otherwise return empty/null and show a “not available” state in UI

4. **Implement application queries**
   Add CQRS-lite query handlers/services for:
   - finance event stream list
   - finance event detail
   - finance tool manifest list
   - finance tool execution history
   - finance tool execution detail
   Requirements:
   - tenant/company scoped
   - paginated/sorted where appropriate
   - filterable at least by recent-first ordering
   - resilient to missing optional relationships
   - no direct UI formatting leakage beyond concise summaries

5. **Implement API surface**
   Add or extend API endpoints for the above queries.
   Suggested shape, adapt to existing conventions:
   - `GET /api/admin/finance/events`
   - `GET /api/admin/finance/events/{id}`
   - `GET /api/admin/finance/tools/manifests`
   - `GET /api/admin/finance/tools/executions`
   - `GET /api/admin/finance/tools/executions/{id}`
   Keep response contracts stable and explicit.
   Enforce authorization and company scoping consistently with existing admin APIs.

6. **Implement Blazor admin UI**
   Add a finance transparency page or section in the admin area with clear subsections/tabs/cards:
   - **Event stream**
     - table/list with event type, timestamp, correlation ID, entity reference
     - row action to open detail drawer
   - **Event detail drawer**
     - payload summary
     - trigger consumption tracing section
     - correlation/entity metadata
   - **Tool manifests**
     - list/table with tool name, version, schema summary, provider adapter identity
   - **Tool execution history**
     - table/list with lifecycle state, request summary, response summary, timestamp
     - row action to open detail or navigate to detail panel
   - **Execution detail**
     - related approval request link if available
     - originating finance action link/reference if available
   Follow existing Blazor patterns for:
   - SSR-first rendering
   - progressive interactivity only where needed
   - loading/empty/error states
   - reusable table/detail components if already present

7. **Add summary formatting helpers**
   If raw request/response/payload JSON exists, add server-side summarization helpers that:
   - extract key fields
   - truncate safely
   - avoid exposing excessive internals
   - produce concise operational summaries
   Keep these deterministic and testable.

8. **Handle optional tracing and linking**
   Acceptance criteria explicitly say “when backend data exists” and “when related identifiers are available”.
   Therefore:
   - do not fabricate links
   - show conditional sections only when data exists
   - provide clear fallback text such as:
     - “No trigger consumption trace available”
     - “No related approval request”
     - “Originating finance action unavailable”

9. **Wire navigation**
   If there is an admin finance menu/section, add entry points there.
   If not, place under the most appropriate existing admin/audit/finance route without broad navigation redesign.

10. **Test thoroughly**
   Add tests for:
   - tenant scoping
   - event list/detail mapping
   - execution detail relationship mapping
   - optional tracing/link behavior
   - API success/not-found/forbidden behavior as applicable
   - summary formatting edge cases

11. **Keep implementation repository-native**
   Match:
   - naming conventions
   - folder structure
   - dependency injection registration style
   - endpoint style
   - Blazor component style
   - test style
   Avoid introducing a new architectural pattern for this task.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full validation:
   - `dotnet build`
   - `dotnet test`

4. Manually verify the admin UI:
   - Navigate to the new finance transparency/admin page
   - Confirm finance event stream renders with:
     - event type
     - timestamp
     - correlation identifier
     - affected entity reference
   - Open an event detail drawer and confirm:
     - payload summary is shown
     - trigger consumption tracing appears only when available
   - Confirm tool manifest list renders with:
     - tool name
     - version metadata
     - contract/schema summary
     - provider adapter identity
   - Confirm tool execution history renders with:
     - lifecycle state
     - request summary
     - response summary
     - execution timestamp
   - Open execution detail and confirm related approval/originating finance action links appear when identifiers exist

5. Validate edge cases:
   - no events
   - no manifests
   - no executions
   - missing optional trace data
   - missing related approval/action IDs
   - cross-tenant access is blocked or not found per existing conventions

6. If seed/dev data is needed, add minimal non-invasive fixtures or local sample data paths consistent with existing patterns, then re-run:
   - `dotnet build`
   - `dotnet test`

# Risks and follow-ups
- **Risk: finance domain events may not yet exist as a first-class persisted model**
  - Mitigation: derive the stream from existing audit/business event records and clearly scope to finance-related actions using existing metadata.
- **Risk: tool manifests may not yet have a dedicated registry table**
  - Mitigation: project from existing tool registration/configuration sources; if necessary, create a lightweight read model without overengineering a registry subsystem.
- **Risk: correlation IDs and related identifiers may be inconsistently populated**
  - Mitigation: surface fields conditionally and avoid broken links; add TODOs where upstream producers should enrich metadata.
- **Risk: raw JSON payloads may be too verbose or sensitive**
  - Mitigation: summarize server-side and avoid dumping full internals into list views.
- **Risk: UI drawer patterns may differ across the app**
  - Mitigation: reuse existing modal/drawer/detail-shell components rather than inventing a new interaction model.
- **Risk: acceptance criteria depend on backend data existence**
  - Mitigation: implement graceful empty/absent states and document any upstream data gaps in the final notes.

Follow-ups to note in code comments or final handoff if encountered:
- standardize finance event metadata/correlation propagation across producers
- enrich tool execution records with stronger approval/action foreign keys
- add filtering by event type/tool/state/date if not included now
- consider dedicated audit/transparency shared components if similar views already exist or are upcoming