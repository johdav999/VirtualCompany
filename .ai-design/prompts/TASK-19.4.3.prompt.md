# Goal

Implement the UI and supporting application/API plumbing for **TASK-19.4.3 — Create tool execution history and execution detail components** in the finance transparency/admin area, aligned to **US-19.4 ST-FUI-304 — Finance events and tool execution transparency**.

Deliver a vertical slice that lets authorized users:

- view a **finance domain event stream**
- open an **event detail view**
- view **finance tool manifests**
- view **finance tool execution history**
- open **execution detail** with links to related approval requests and originating finance actions when identifiers exist

The implementation must fit the existing **.NET modular monolith + Blazor Web App + ASP.NET Core** architecture, preserve tenant scoping, and avoid exposing raw chain-of-thought or unsafe internal data.

# Scope

In scope:

- Add/extend backend query models and endpoints for:
  - finance event stream list
  - finance event detail
  - finance tool manifest list
  - finance tool execution history list
  - finance tool execution detail
- Add/extend application-layer query handlers and DTOs for the above
- Add Blazor admin UI components/pages for:
  - event stream table/list
  - event detail panel/page
  - tool manifest list
  - tool execution history table/list
  - tool execution detail panel/page
- Show the required fields from acceptance criteria:
  - event type, timestamp, correlation identifier, affected entity reference
  - payload summary and trigger consumption tracing when backend data exists
  - tool name, version metadata, contract/schema summary, provider adapter identity
  - lifecycle state, request summary, response summary, execution timestamp
  - links to approval requests and originating finance actions when related identifiers are available
- Ensure all queries are tenant-scoped and finance-admin appropriate
- Add tests for application/query behavior and any API contract coverage already consistent with repo patterns

Out of scope unless required by existing patterns:

- creating a brand new finance domain model if one already exists elsewhere
- redesigning navigation or global layout
- implementing write/edit flows
- exposing raw payload blobs without summarization/safe formatting
- mobile app work
- broad audit platform refactors outside what is needed for this task

If backend persistence for some fields does not yet exist, implement graceful UI fallbacks:
- hide optional sections when data is absent
- show “Not available” or equivalent for missing summaries/links
- do not block the rest of the experience

# Files to touch

Inspect the solution first and then touch the minimal set of files consistent with existing conventions. Likely areas:

- `src/VirtualCompany.Application/**`
  - finance/admin query DTOs
  - query handlers
  - interfaces for read models/services
- `src/VirtualCompany.Api/**`
  - finance/admin controller or minimal API endpoints
  - request/response mapping if API layer uses separate contracts
- `src/VirtualCompany.Infrastructure/**`
  - query repositories / EF / Dapper read models
  - tenant-scoped SQL or data access implementations
- `src/VirtualCompany.Web/**`
  - finance admin pages/components
  - route registration/navigation updates if needed
  - typed API client/service calls
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this repo uses shared DTOs between API/Web
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests if patterns exist
- other test projects if present for application-layer tests

Before coding, locate existing equivalents for:
- audit/event stream pages
- approval detail links
- task/workflow detail links
- manifest/catalog list components
- execution history/detail components in other domains

Prefer reusing existing table/detail component patterns over inventing new ones.

# Implementation plan

1. **Discover existing finance transparency surface**
   - Search for finance admin pages, audit views, tool execution models, approval detail pages, and event/audit APIs.
   - Identify whether finance domain events already map to:
     - `audit_events`
     - workflow/event inbox tables
     - integration/webhook event records
     - dedicated finance event read models
   - Identify whether tool manifests already exist in code/config and how provider adapter identity/version metadata are stored.

2. **Define read models around acceptance criteria**
   - Create or extend query DTOs for:
     - `FinanceEventListItem`
     - `FinanceEventDetail`
     - `FinanceToolManifestListItem`
     - `FinanceToolExecutionListItem`
     - `FinanceToolExecutionDetail`
   - Include only user-facing, safe fields.
   - Suggested fields:
     - Event list item:
       - `Id`
       - `EventType`
       - `OccurredAt`/`Timestamp`
       - `CorrelationId`
       - `AffectedEntityType`
       - `AffectedEntityId`
       - `AffectedEntityDisplay`
     - Event detail:
       - above core fields
       - `PayloadSummary`
       - `TriggerConsumptionTrace`
       - `RawPayloadAvailable` flag only if useful
     - Tool manifest:
       - `ToolName`
       - `Version`
       - `VersionMetadata`
       - `ContractSummary`
       - `SchemaSummary`
       - `ProviderAdapterIdentity`
     - Tool execution list item:
       - `ExecutionId`
       - `ToolName`
       - `LifecycleState`
       - `RequestSummary`
       - `ResponseSummary`
       - `ExecutedAt`
       - `CorrelationId`
     - Tool execution detail:
       - above plus
       - `ApprovalRequestId`
       - `ApprovalRequestDisplay`
       - `OriginatingFinanceActionId`
       - `OriginatingFinanceActionDisplay`
       - any related task/workflow identifiers if already available
   - Keep optional fields nullable and render conditionally.

3. **Implement application queries**
   - Add CQRS-lite query objects and handlers for:
     - list finance events
     - get finance event detail
     - list finance tool manifests
     - list finance tool executions
     - get finance tool execution detail
   - Ensure handlers enforce:
     - company/tenant scoping
     - finance/admin authorization assumptions consistent with existing app patterns
     - deterministic ordering:
       - events: newest first
       - executions: newest first
   - If summaries are not stored directly, derive concise summaries from JSON safely:
     - truncate long content
     - avoid dumping entire request/response payloads
     - prefer key fields over raw JSON

4. **Implement infrastructure read access**
   - Use existing data access style in the repo.
   - Map from current persistence sources:
     - `tool_executions`
     - `audit_events`
     - approvals/tasks/workflows/finance action tables if present
     - manifest/config tables or in-memory registry if manifests are configuration-backed
   - If no dedicated finance event table exists, derive finance event stream from the closest existing business audit/event source, but keep naming honest in code/comments.
   - Preserve tenant filters in every query.
   - Add lightweight projection SQL/EF includes only for fields needed by UI.

5. **Expose API endpoints**
   - Add finance admin read endpoints under the existing route structure.
   - Keep routes predictable, e.g. analogous to:
     - `/api/admin/finance/events`
     - `/api/admin/finance/events/{id}`
     - `/api/admin/finance/tools/manifests`
     - `/api/admin/finance/tools/executions`
     - `/api/admin/finance/tools/executions/{id}`
   - Reuse existing pagination/filter conventions if present.
   - Return 404 for cross-tenant or missing resources without leaking existence.

6. **Build Blazor UI components**
   - Add or extend finance admin pages/components:
     - **Finance event stream**
       - table/list with event type, timestamp, correlation ID, affected entity reference
       - row click or detail link
     - **Event detail**
       - payload summary section
       - trigger consumption tracing section only when available
     - **Tool manifests**
       - list/cards/table with tool name, version metadata, contract/schema summary, provider adapter identity
     - **Tool execution history**
       - table/list with lifecycle state, request summary, response summary, execution timestamp
       - detail link
     - **Execution detail**
       - related approval request link when available
       - originating finance action link when available
   - Reuse existing shared components for:
     - loading/empty/error states
     - definition lists / metadata panels
     - badges for lifecycle state
     - timestamp formatting
     - entity links
   - Keep the UI SSR-friendly first, with minimal interactivity.

7. **Link related records**
   - For execution detail, resolve related identifiers from existing fields such as:
     - approval entity references
     - task/workflow IDs
     - correlation IDs
     - target/action references in audit records
   - If direct links are not possible, show the identifier as plain text rather than inventing broken routes.
   - Only render links when the destination route exists.

8. **Handle optional backend data gracefully**
   - Event detail:
     - if no payload summary exists, show a neutral empty state
     - if no trigger consumption trace exists, omit that section or show “No trigger trace available”
   - Execution detail:
     - if no approval/originating action identifiers exist, omit related links section
   - Tool manifests:
     - if schema/contract summary is partial, show available metadata only

9. **Testing**
   - Add application tests for:
     - tenant scoping
     - newest-first ordering
     - optional field handling
     - summary derivation behavior if implemented in handlers/services
   - Add API tests for:
     - successful list/detail retrieval
     - 404/not found for wrong tenant or missing record
   - Add component tests only if the repo already uses them; otherwise keep UI validation through integration/manual verification.

10. **Polish**
   - Ensure naming consistently uses “finance” and “tool execution transparency”
   - Avoid exposing internal-only fields, raw reasoning, or oversized payloads
   - Keep code comments concise and only where needed to explain non-obvious mapping logic

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Navigate to the finance admin transparency area
   - Confirm event stream renders with:
     - event type
     - timestamp
     - correlation identifier
     - affected entity reference
   - Open an event detail and confirm:
     - payload summary is shown when data exists
     - trigger consumption tracing is shown when backend data exists
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
   - Open execution detail and confirm:
     - approval request link appears when related identifier exists
     - originating finance action link appears when related identifier exists
     - missing related identifiers do not break the page

4. Tenant/authorization verification:
   - Confirm data is scoped to the active company
   - Confirm cross-tenant IDs are not retrievable
   - Confirm unauthorized users do not see restricted finance transparency views if authorization already exists in this area

5. Regression check:
   - Verify existing audit/approval/task detail routes still work
   - Verify no broken navigation links or route conflicts

# Risks and follow-ups

- **Risk: finance event persistence may not yet be explicit**
  - You may need to project from existing audit/event sources.
  - If so, keep the implementation modular so a future dedicated finance event store can replace the projection cleanly.

- **Risk: tool manifest metadata may be configuration-driven rather than persisted**
  - If manifests come from a registry/config source, expose a read-model adapter instead of forcing persistence changes.

- **Risk: related approval/originating action links may be inconsistently modeled**
  - Prefer nullable relationships and conditional rendering.
  - Do not fabricate associations from weak heuristics unless correlation rules already exist in code.

- **Risk: request/response payloads may be too large or sensitive**
  - Summarize and truncate.
  - Avoid raw JSON dumps unless there is an established safe viewer pattern.

- **Risk: route targets for approval or finance action detail may not exist yet**
  - Render plain identifiers for now and note follow-up work.

Follow-ups to note in code comments or task notes if encountered:
- add pagination/filtering if event/execution volume grows
- add richer trigger trace visualization if backend tracing matures
- add dedicated finance action detail page if only identifiers exist today
- unify transparency components across domains if similar patterns already exist or emerge later