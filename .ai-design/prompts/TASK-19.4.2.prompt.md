# Goal
Implement backlog task **TASK-19.4.2 — Build finance tool registry view with manifest and provider metadata** for story **US-19.4 ST-FUI-304 — Finance events and tool execution transparency**.

Deliver a finance-focused admin UI in the web app that exposes:
- a **finance domain event stream**
- **event detail** with payload summary and trigger consumption tracing when available
- a **finance tool manifest registry** with provider metadata
- a **finance tool execution history** list
- **execution detail linkage** to approval requests and originating finance actions when related identifiers exist

The implementation must fit the existing **.NET modular monolith + Blazor Web App + ASP.NET Core + PostgreSQL** architecture, remain **tenant-scoped**, and align with the product’s **auditability / explainability** principles.

# Scope
Implement only what is necessary to satisfy the acceptance criteria for the finance transparency/admin experience.

Include:
- Backend query models and endpoints for finance transparency views
- Application-layer queries/handlers for:
  - finance event stream
  - finance event detail
  - finance tool manifest registry
  - finance tool execution history
  - finance tool execution detail
- Infrastructure data access against existing tables and/or new read models as needed
- Blazor admin pages/components for:
  - finance event stream list
  - event detail drawer/page
  - finance tool registry list
  - finance tool execution history list
  - execution detail page/panel with related approval/action links
- Safe empty states when backend data does not exist
- Tenant-aware authorization and filtering
- Minimal payload/contract summaries for human-readable display

Do not include:
- New mobile work
- Broad redesign of audit subsystem
- Full generic cross-domain registry UX beyond finance
- New orchestration behavior unless required to expose existing metadata
- Raw chain-of-thought or unsafe internal debugging data

If required data is missing from current persistence, add the smallest viable schema/query support to expose:
- event type
- timestamp
- correlation identifier
- affected entity reference
- payload summary
- trigger consumption tracing
- tool manifest metadata
- provider adapter identity
- execution lifecycle state
- request/response summaries
- related approval/origin identifiers

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely web UI:
- `src/VirtualCompany.Web/**`
- Admin/finance pages, components, routes, nav, and view models
- Shared table/detail components if they already exist

Likely API:
- `src/VirtualCompany.Api/**`
- Finance transparency endpoints/controllers or minimal API mappings
- Authorization policies if needed

Likely application layer:
- `src/VirtualCompany.Application/**`
- Query DTOs
- Query handlers
- Read service interfaces
- Mapping logic for summaries and related-link models

Likely domain/infrastructure:
- `src/VirtualCompany.Infrastructure/**`
- EF Core query implementations / repositories / SQL projections
- Entity configuration if new read-side persistence is needed
- Migrations if schema changes are unavoidable

Potential shared contracts:
- `src/VirtualCompany.Shared/**`

Potential tests:
- `tests/VirtualCompany.Api.Tests/**`
- Add application/API tests for tenant scoping, response shape, and empty-state behavior

Also review:
- `README.md`
- existing finance, audit, approval, workflow, and tool execution code paths
- any existing migrations guidance under `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing finance transparency data sources**
   - Search for current implementations related to:
     - finance events
     - audit events
     - tool manifests
     - tool executions
     - approvals
     - workflow triggers / trigger consumption
     - correlation IDs
   - Identify whether finance-specific data already exists in:
     - `audit_events`
     - `tool_executions`
     - approval tables
     - workflow/task tables
     - integration/provider adapter metadata
   - Reuse existing models first; avoid duplicating business data.

2. **Define the read models needed for the UI**
   Create focused query DTOs/view models such as:
   - `FinanceEventListItem`
     - `Id`
     - `EventType`
     - `OccurredAt`
     - `CorrelationId`
     - `AffectedEntityType`
     - `AffectedEntityId`
     - `PayloadSummary`
     - optional `HasTriggerTrace`
   - `FinanceEventDetail`
     - core event metadata
     - payload summary/details
     - trigger consumption trace entries if available
   - `FinanceToolManifestListItem`
     - `ToolName`
     - `Version`
     - `ContractSummary`
     - `SchemaSummary`
     - `ProviderAdapterId` / `ProviderAdapterName`
     - `ManifestSource` if useful
   - `FinanceToolExecutionListItem`
     - `ExecutionId`
     - `ToolName`
     - `LifecycleState`
     - `RequestSummary`
     - `ResponseSummary`
     - `ExecutedAt`
     - `CorrelationId`
   - `FinanceToolExecutionDetail`
     - execution metadata
     - policy/approval linkage
     - originating finance action linkage
     - related task/workflow/entity references

3. **Establish finance-domain filtering rules**
   - Define a clear, deterministic rule for what counts as “finance” data.
   - Prefer existing domain/module markers, such as:
     - finance-prefixed event/action names
     - finance tool category/module metadata
     - finance workflow/task/entity types
   - Centralize this logic in the application/infrastructure query layer so UI does not infer domain membership itself.

4. **Implement backend queries**
   - Add application queries and handlers for:
     - event stream list
     - event detail
     - tool manifest registry
     - execution history list
     - execution detail
   - Ensure all queries are:
     - tenant-scoped by company context
     - paginated/sorted where appropriate
     - resilient to missing optional related data
   - For summaries:
     - generate concise human-readable summaries from JSON payloads
     - truncate safely for list views
     - avoid exposing sensitive/raw internals unnecessarily

5. **Add or extend API endpoints**
   Add tenant-aware endpoints under a finance/admin route pattern consistent with the existing API style, for example:
   - `GET /api/admin/finance/events`
   - `GET /api/admin/finance/events/{id}`
   - `GET /api/admin/finance/tools`
   - `GET /api/admin/finance/tool-executions`
   - `GET /api/admin/finance/tool-executions/{id}`
   Match the project’s existing routing conventions rather than forcing this exact shape.

6. **Support trigger consumption tracing**
   - If backend trace data already exists, surface it in event detail.
   - If trace data is stored indirectly via workflow/task/audit records, compose a lightweight trace projection.
   - If no trace exists for an event, return a clean empty collection / null section and show a “No trigger trace available” state in UI.
   - Do not invent fake trace data.

7. **Support tool manifest/provider metadata**
   - Locate current tool registration/manifest/provider adapter definitions.
   - Expose:
     - tool name
     - version metadata
     - contract/schema summary
     - provider adapter identity
   - If manifests are code-defined rather than persisted, create a read service that projects runtime registry metadata into API responses.
   - Keep this read-only.

8. **Support execution linkage**
   - For execution detail, resolve related identifiers when available:
     - approval request ID
     - originating finance action/event/task/workflow/entity
   - Render links only when identifiers exist.
   - If relationships are indirect, compose them through correlation ID, task/workflow references, or explicit foreign keys already present.
   - Do not block the feature if some records are unlinked.

9. **Build Blazor admin UI**
   Implement a finance transparency area in the admin web app with:
   - **Finance Events**
     - table columns: event type, timestamp, correlation identifier, affected entity reference
     - row action to open detail
   - **Event Detail**
     - payload summary
     - trigger consumption tracing section when available
   - **Finance Tool Registry**
     - table columns: tool name, version metadata, contract/schema summary, provider adapter identity
   - **Execution History**
     - table columns: lifecycle state, request summary, response summary, execution timestamp
     - row action to open detail
   - **Execution Detail**
     - related approval request link
     - originating finance action link
     - fallback text when related identifiers are absent
   Reuse existing admin layout, table, badge, and detail components where possible.

10. **Handle UX states**
   - Loading state
   - Empty state
   - Missing optional metadata
   - Access denied / not found
   - Long JSON-derived summaries should be formatted into concise readable blocks
   - Timestamps should use existing app formatting conventions

11. **Add tests**
   Add targeted tests for:
   - tenant isolation on all finance transparency endpoints
   - event stream response shape
   - event detail with and without trigger trace
   - tool registry manifest/provider metadata projection
   - execution history/detail linkage behavior when related IDs exist or are absent
   - UI/component rendering for empty states if the project already tests Blazor components

12. **Keep implementation aligned with architecture**
   - Use CQRS-lite query handlers
   - Keep UI free of data access logic
   - Keep domain/business audit concerns separate from technical logs
   - Prefer typed contracts over ad hoc JSON parsing in components
   - Preserve modular boundaries

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, verify:
   - finance event stream page loads for an authorized tenant
   - list shows:
     - event type
     - timestamp
     - correlation identifier
     - affected entity reference
   - event detail opens and shows:
     - payload summary
     - trigger consumption tracing when data exists
     - clean empty state when it does not
   - finance tool registry page shows:
     - tool name
     - version metadata
     - contract/schema summary
     - provider adapter identity
   - finance tool execution history shows:
     - lifecycle state
     - request summary
     - response summary
     - execution timestamp
   - execution detail shows links to:
     - approval requests when available
     - originating finance actions when available

4. Re-run automated validation:
   - `dotnet test`

5. If migrations were added:
   - generate/apply migration per repo conventions
   - verify app still builds and tests pass

6. Manually verify authorization behavior:
   - cross-tenant access returns forbidden/not found per existing conventions
   - unauthorized users do not see restricted admin finance views

# Risks and follow-ups
- **Data availability risk:** finance event/manifest/trace metadata may not yet exist in a normalized form. If so, implement the smallest read-side projection needed and document any gaps.
- **Domain classification risk:** “finance” may not be consistently tagged. Centralize classification logic and avoid scattering string checks across UI.
- **Manifest source risk:** tool manifests may be runtime-defined rather than persisted. A registry projection service may be required.
- **Traceability risk:** trigger consumption tracing may be partial or absent for older records. UI must degrade gracefully.
- **Linkage risk:** approval/action relationships may rely on correlation IDs rather than direct foreign keys. Prefer explicit links where available and fall back safely.
- **Performance risk:** event/execution history can grow quickly. Use pagination, sorting, and efficient projections.
- **Security risk:** payload/request/response summaries may contain sensitive data. Keep summaries concise and sanitized; do not dump raw secrets or full payloads by default.
- **Follow-up suggestion:** if this task reveals missing finance audit schema consistency, propose a later backlog item for standardized finance event envelopes and explicit execution-to-approval/action foreign-key linkage.