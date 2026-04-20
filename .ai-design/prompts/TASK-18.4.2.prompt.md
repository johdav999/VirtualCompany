# Goal
Implement backlog task **TASK-18.4.2 — Finance alert detail panel and deep links from low-cash alert surfaces** for story **US-18.4 ST-FUI-204 — Cash position cockpit widgets and finance action entry points**.

Deliver a vertical slice across web UI, application/query layer, authorization/policy checks, and backend endpoint integration so that:

- The executive cockpit shows a **finance cash position widget** with:
  - current value
  - trend indicator
  - last refreshed timestamp
- The cockpit shows a **runway visualization** with:
  - current runway estimate
  - threshold-based styling for healthy / warning / critical
- **Low-cash alerts** from finance workflow surfaces open a **finance-specific detail panel or page**
- Finance widgets and alert surfaces **deep-link** to:
  - finance workspace
  - anomaly workbench
  - cash detail page
  - finance summary
- Explicit finance actions are available:
  - Review invoice
  - Inspect anomaly
  - View cash position
  - Open finance summary
- Finance action entry points are shown only when the current user passes **role + policy checks**
- Action triggers call **existing backend orchestration endpoints**, not new bespoke business logic

Keep the implementation aligned with the existing modular monolith, CQRS-lite patterns, tenant scoping, and policy-enforced action model.

# Scope
In scope:

- Add or extend executive cockpit finance widget UI in the Blazor web app
- Add runway status visualization and threshold styling
- Add finance alert detail panel/page for low-cash alerts
- Add deep-link routing/navigation from:
  - cockpit finance widgets
  - low-cash alert surfaces
  - finance action buttons
- Add/extend application queries/view models needed to populate the widget and detail panel
- Add role/policy-gated rendering for finance action entry points
- Wire finance actions to existing backend orchestration/API endpoints
- Add tests for:
  - query/view model mapping
  - authorization/policy gating behavior
  - route/deep-link generation
  - endpoint invocation contracts where practical

Out of scope unless required by existing patterns:

- Building a new finance domain model from scratch
- Creating new orchestration business workflows if equivalent endpoints already exist
- Mobile-specific implementation
- Broad redesign of cockpit layout
- New persistence schema unless absolutely necessary for existing alert/detail projection support

If data is partially unavailable in the current codebase, prefer:
1. extending existing dashboard/alert projections,
2. using placeholder-safe UI states,
3. adding minimal query contracts,
4. avoiding speculative schema expansion.

# Files to touch
Inspect the solution first and then update the exact files that match existing conventions. Likely areas:

- `src/VirtualCompany.Web/`
  - Executive cockpit/dashboard pages and components
  - Alert list/detail components
  - Shared navigation/link helpers
  - Authorization-aware UI helpers/components
- `src/VirtualCompany.Api/`
  - Existing dashboard/alerts/controllers or minimal API endpoints
  - Finance action trigger endpoints if already exposed here
- `src/VirtualCompany.Application/`
  - Dashboard/analytics/alerts queries
  - Finance widget DTOs/view models
  - Policy/authorization query services
  - Action orchestration request contracts
- `src/VirtualCompany.Domain/`
  - Only if existing enums/value objects need extension for finance alert types/statuses
- `src/VirtualCompany.Infrastructure/`
  - Query handlers/repositories for finance dashboard projections
  - Existing orchestration endpoint adapters if needed
- `src/VirtualCompany.Shared/`
  - Shared DTOs/contracts for finance widget, alert detail, and action links
- `tests/VirtualCompany.Api.Tests/`
  - API tests for finance widget/detail/action endpoints
- Add corresponding web/component/application tests in existing test projects if present

Before editing, locate:
- executive cockpit page/component
- alert inbox/list/detail implementation
- existing finance-related DTOs/endpoints
- authorization/policy-check patterns
- existing orchestration endpoint client/service abstractions

# Implementation plan
1. **Discover existing patterns before coding**
   - Find the executive cockpit dashboard implementation in `VirtualCompany.Web`
   - Find current alert rendering and any detail drawer/panel/page pattern
   - Find existing finance-related routes/pages such as workspace, anomaly workbench, cash detail, or summary
   - Find how role checks and policy checks are currently performed in UI and API
   - Find existing orchestration endpoints for finance actions
   - Reuse naming, route conventions, and DTO patterns already in the repo

2. **Define/extend finance cockpit view models**
   - Add or extend a dashboard query result model to include:
     - cash position amount
     - trend direction and/or delta
     - last refreshed timestamp
     - runway estimate
     - runway status enum/string: healthy, warning, critical
     - deep-link targets for relevant destinations
     - available finance actions with authorization flags
   - Keep the model tenant-scoped and presentation-ready
   - If the app already separates raw query DTOs from UI models, follow that pattern

3. **Implement or extend application query handlers**
   - Add a query/handler or extend an existing cockpit/dashboard query to return finance widget data
   - Add a query for low-cash alert detail that returns:
     - alert summary
     - contributing factors
     - related finance links
     - available actions
   - Ensure all queries enforce company/tenant scope
   - Prefer composition from existing analytics/alerts services over duplicating logic

4. **Add runway status logic**
   - Implement deterministic threshold mapping for runway status:
     - healthy
     - warning
     - critical
   - Use existing business thresholds if already defined
   - If thresholds are not yet centralized, keep them in one application-layer mapper/service rather than embedding them in Razor markup
   - Expose status in a way the UI can style consistently

5. **Implement finance widget UI**
   - Update the executive cockpit to render:
     - cash position value
     - trend indicator
     - last refreshed timestamp
     - runway visualization
     - status styling
   - Add click targets/deep links to the appropriate finance destinations
   - Ensure empty/loading/error states are handled gracefully
   - Keep accessibility in mind:
     - semantic labels
     - status text not color-only
     - keyboard-accessible links/buttons

6. **Implement low-cash alert detail panel/page**
   - Reuse the app’s existing detail drawer/panel/page pattern
   - When a low-cash alert is opened, show:
     - alert summary
     - contributing factors
     - links to detailed finance views
     - explicit finance actions
   - Support deep linking directly to this detail surface via route/query parameter if consistent with current routing patterns
   - If both panel and page patterns exist, choose the one already used for alert drill-in

7. **Implement deep-link generation**
   - Add a centralized helper or route builder if the app already uses one
   - Ensure links resolve to the correct destinations:
     - finance workspace
     - anomaly workbench
     - cash detail page
     - finance summary
   - Preserve tenant/company context in navigation
   - Avoid hardcoding URLs in multiple components

8. **Add finance action entry points**
   - Render the following actions where appropriate:
     - Review invoice
     - Inspect anomaly
     - View cash position
     - Open finance summary
   - Map each action to the existing backend orchestration/API endpoint
   - Do not implement direct business-side effects in the UI
   - Use existing command/request patterns for action invocation

9. **Enforce role and policy checks**
   - Gate action visibility using existing authorization services/policies
   - Ensure UI visibility is based on both:
     - user role/membership
     - policy eligibility for the action
   - Also enforce authorization server-side on action endpoints
   - If current UI only supports role checks, add a minimal policy eligibility query/flag from the backend rather than duplicating policy logic in the client

10. **Wire backend action triggers**
    - Connect action buttons to existing orchestration endpoints
    - Pass required identifiers such as:
      - alert id
      - anomaly id
      - invoice id
      - company context
    - Handle success/failure states with existing UX patterns
    - Ensure correlation/request IDs and audit-friendly invocation metadata flow through existing infrastructure where supported

11. **Testing**
    - Add unit tests for:
      - runway status mapping
      - finance widget view model mapping
      - action availability gating logic
      - deep-link generation
    - Add API/integration tests for:
      - tenant-scoped finance widget query
      - low-cash alert detail retrieval
      - unauthorized action trigger rejection
      - authorized action trigger success path or contract validation
    - Add component/UI tests if the repo already uses them for Blazor components

12. **Keep implementation minimal and consistent**
    - Prefer extending existing dashboard and alert infrastructure
    - Avoid introducing parallel finance-specific frameworks
    - Keep new types focused and named clearly around cockpit finance widgets and low-cash alert detail

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before and after changes:
   - `dotnet test`

3. Manually verify in the web app:
   - Executive cockpit displays finance cash widget with:
     - value
     - trend indicator
     - last refreshed timestamp
   - Runway visualization appears with healthy/warning/critical styling
   - Clicking finance widget navigates to the expected finance destination
   - Opening a low-cash alert shows a finance detail panel/page with:
     - summary
     - contributing factors
     - detailed finance links
   - Finance actions appear only for users with valid role/policy access
   - Finance actions invoke existing backend orchestration endpoints successfully
   - Unauthorized users do not see restricted actions, and direct endpoint calls are rejected server-side

4. Validate tenant isolation:
   - Confirm finance widget and alert detail data are scoped to the active company
   - Confirm deep links preserve company context
   - Confirm action endpoints reject cross-tenant access

5. Validate UX/accessibility basics:
   - Keyboard navigation works for widget links and action buttons
   - Status is understandable without relying only on color
   - Loading/empty/error states do not break the cockpit layout

# Risks and follow-ups
- **Risk: finance routes/endpoints may not yet exist**
  - Mitigation: discover existing routes first; if missing, add minimal placeholders only where necessary and note follow-up work clearly

- **Risk: policy logic may currently live only server-side**
  - Mitigation: expose backend-computed action availability flags to the UI instead of reimplementing policy rules in Razor

- **Risk: dashboard data source may not yet provide runway/cash trend**
  - Mitigation: extend existing query projections minimally and use safe fallback states when data is unavailable

- **Risk: alert detail UX pattern may be inconsistent across the app**
  - Mitigation: reuse the dominant existing pattern rather than inventing a new drawer/page system

- **Risk: action labels may map ambiguously to orchestration endpoints**
  - Mitigation: document the mapping in code comments/tests and keep endpoint invocation behind a typed service abstraction

Follow-ups to note in your final implementation summary if not completed in this task:
- mobile companion parity for finance alert deep links
- richer finance threshold configuration from policy/admin settings
- audit/explainability enrichment for finance action invocations
- caching/performance optimization for cockpit finance aggregates