# Goal
Implement backlog task **TASK-18.4.3 — Add role-gated finance action entry points and connect them to orchestration triggers** for story **US-18.4 ST-FUI-204 — Cash position cockpit widgets and finance action entry points**.

Deliver the UI and application wiring needed so that executive cockpit finance widgets and finance alerts expose explicit finance actions, those actions are only visible/enabled for authorized users, and invoking them calls existing backend orchestration endpoints or workflow triggers.

This task must satisfy these outcomes:

- Executive cockpit shows finance action entry points associated with:
  - current cash position widget
  - runway visualization
  - low-cash alerts / finance alert detail surface
- Supported explicit actions:
  - **Review invoice**
  - **Inspect anomaly**
  - **View cash position**
  - **Open finance summary**
- Finance action entry points are:
  - deep-linked appropriately to finance workspace, anomaly workbench, or cash detail page
  - shown only when current user passes role/policy checks
  - connected to existing backend orchestration endpoints/triggers
- Low-cash alerts open a finance-specific detail panel/page with:
  - alert summary
  - contributing factors
  - links to detailed finance views

Do not redesign unrelated cockpit areas. Reuse existing authorization, orchestration, navigation, and API patterns already present in the solution.

# Scope
In scope:

- Add or extend finance-specific cockpit UI components/pages/panels for action entry points
- Add role/policy-gated rendering for finance actions in the web UI
- Wire finance action buttons/menus to existing backend orchestration endpoints
- Add deep links from finance widgets/alerts to the correct finance destinations
- Add/extend alert detail UI for low-cash alerts
- Add application/API contracts only where needed to support the UI wiring and authorization metadata
- Add tests covering:
  - visibility rules
  - action trigger invocation
  - deep-link behavior
  - alert detail rendering contract if applicable

Out of scope unless strictly required for compilation/integration:

- Building new finance calculation engines for cash position or runway
- Creating brand-new orchestration backend business flows if equivalent endpoints already exist
- Reworking mobile app behavior
- Broad cockpit redesign
- New persistence schema unless absolutely necessary for existing alert/action metadata
- Replacing existing authorization model with a new one

Implementation constraints:

- Follow modular monolith boundaries
- Keep tenant scoping intact
- Use policy-based authorization and existing membership/role infrastructure
- Prefer CQRS-lite patterns already used in the app
- Keep UI authorization conservative: hidden or disabled only when backed by server-side enforcement
- Server-side checks must remain authoritative

# Files to touch
Inspect the repository first and then update the exact files that match existing patterns. Likely areas include:

- `src/VirtualCompany.Web/**`
  - executive cockpit pages/components
  - finance widget components
  - alert list/detail components
  - navigation/deep-link helpers
  - authorization-aware UI helpers
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for cockpit finance actions if a thin API surface is needed
  - existing orchestration trigger endpoints if route exposure or request models need extension
- `src/VirtualCompany.Application/**`
  - queries/view models for cockpit finance widgets and alert detail
  - commands for finance action trigger requests
  - authorization/policy evaluation services used by UI/API
- `src/VirtualCompany.Domain/**`
  - enums/value objects/constants for finance action types or alert types, only if missing
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts for finance widget actions, alert detail payloads, or deep-link metadata
- `src/VirtualCompany.Infrastructure/**`
  - implementations for orchestration trigger adapters if application layer already expects abstractions
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for authorization and trigger invocation
- Add corresponding web/application tests if test projects already exist for those layers

Before editing, identify the actual existing files for:

- executive cockpit dashboard
- finance/cash widgets
- alert detail page/panel
- authorization policy checks
- orchestration trigger endpoints/services

Prefer extending existing files over introducing parallel patterns.

# Implementation plan
1. **Discover existing implementation seams**
   - Locate the executive cockpit page and current widget composition.
   - Locate any existing finance widget, KPI card, alert card, or detail drawer/page components.
   - Locate current role/membership authorization patterns in web and API.
   - Locate existing orchestration endpoints/services for finance workflows, anomaly inspection, invoice review, cash position, and finance summary.
   - Identify whether deep-link routing already exists for:
     - finance workspace
     - anomaly workbench
     - cash detail page
     - finance summary page/panel

2. **Define finance action model**
   - Introduce or reuse a typed representation for finance action entry points, e.g.:
     - action key/type
     - label
     - destination kind: deep link vs orchestration trigger
     - route/url if navigational
     - trigger request payload if orchestration-backed
     - visibility/enabled state
     - authorization reason if hidden/disabled messaging is already supported
   - Keep this model shared between widget rendering and alert detail rendering if possible.

3. **Implement role/policy gating**
   - Reuse existing membership role and policy infrastructure.
   - Add a focused finance action authorization evaluator if needed, e.g. mapping actions to required roles/policies such as:
     - finance approver
     - owner/admin
     - manager with finance policy
   - Ensure checks consider:
     - authenticated user
     - company/tenant context
     - membership role
     - any existing policy/permission JSON if already modeled
   - Enforce on both:
     - UI visibility/enabled state
     - API/action trigger endpoint authorization
   - Default deny when role/policy context is missing or ambiguous.

4. **Extend cockpit finance widget view models**
   - Ensure current cash position widget view model includes:
     - value
     - trend indicator
     - last refreshed timestamp
     - available actions/deep links
   - Ensure runway widget view model includes:
     - current runway estimate
     - threshold-based status styling: healthy, warning, critical
     - available actions/deep links
   - Do not recalculate finance metrics here unless already part of the query path; only surface action metadata and links.

5. **Add low-cash alert detail surface**
   - Implement or extend a finance-specific detail panel/page for low-cash alerts.
   - It must show:
     - alert summary
     - contributing factors
     - links to detailed finance views
     - explicit finance actions where authorized
   - If the app already uses drawers/modals/detail pages, follow that pattern exactly.
   - Ensure alert detail can be opened from the cockpit alert card/list.

6. **Wire deep links**
   - Add or reuse route generation for:
     - finance workspace
     - anomaly workbench
     - cash detail page
     - finance summary
   - Ensure widget cards and alert detail links navigate correctly.
   - Preserve tenant/company context in route/query parameters according to existing app conventions.

7. **Connect explicit actions to orchestration triggers**
   - For each explicit action, wire the UI to the existing backend orchestration endpoint/service:
     - Review invoice
     - Inspect anomaly
     - View cash position
     - Open finance summary
   - Prefer a thin application/API command that delegates to existing orchestration trigger services rather than embedding orchestration logic in UI or controller code.
   - Include correlation/context payload where appropriate, such as:
     - source = executive cockpit / finance alert
     - alert id if launched from alert
     - entity reference if launched from anomaly/invoice context
   - Return a user-safe result:
     - success state
     - navigation target if applicable
     - created task/workflow reference if existing backend returns one

8. **Preserve auditability and safe behavior**
   - If existing orchestration trigger flows already emit audit events, reuse them.
   - If not, add minimal audit/event logging consistent with current patterns for user-initiated finance action triggers.
   - Do not expose raw reasoning.
   - Ensure denied actions return safe forbidden behavior and are not triggerable by crafted requests.

9. **Update UI states**
   - Add loading, success, and failure states for action invocation.
   - Keep interactions concise:
     - button click triggers orchestration
     - optional toast/status message
     - navigate to returned destination or detail page if existing UX supports it
   - If actions are hidden rather than disabled in current UX, follow that convention consistently.

10. **Testing**
   - Add/extend tests for:
     - authorized user sees finance actions
     - unauthorized user does not see them / receives forbidden on API
     - action trigger endpoint delegates to orchestration service
     - low-cash alert opens finance detail surface with expected fields
     - widget deep links resolve correctly
     - runway status styling maps correctly for healthy/warning/critical if this mapping is in scope of touched code

11. **Keep implementation minimal and aligned**
   - Avoid introducing a generic action framework unless one already exists.
   - Prefer small, composable additions that fit current architecture and naming.
   - Document any assumptions in code comments only where necessary.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Sign in as a user with finance-authorized role/policy.
   - Open executive cockpit.
   - Confirm finance widget shows:
     - cash position value
     - trend indicator
     - last refreshed timestamp
   - Confirm runway widget shows:
     - runway estimate
     - healthy/warning/critical styling
   - Confirm explicit finance actions are visible where expected.
   - Click each action and verify it:
     - calls the expected backend trigger
     - navigates/deep-links appropriately
     - does not error for authorized users

4. Verify low-cash alert behavior:
   - Open a low-cash alert from cockpit/inbox.
   - Confirm finance detail panel/page shows:
     - alert summary
     - contributing factors
     - links to detailed finance views
     - authorized actions only

5. Verify authorization:
   - Sign in as a user without finance role/policy.
   - Confirm finance action entry points are hidden or disabled per existing UX convention.
   - Attempt direct API invocation if applicable and confirm forbidden/denied behavior.

6. Verify tenant safety:
   - Confirm all queries and action triggers remain company-scoped.
   - Ensure deep links do not leak cross-tenant identifiers.

7. If new routes/endpoints were added, verify:
   - route registration works
   - no broken navigation from cockpit widgets
   - no duplicate or conflicting endpoint mappings

# Risks and follow-ups
- **Risk: orchestration endpoints may not yet exist for all four actions.**
  - If missing, implement only the thinnest adapter/command surface needed and clearly note which backend flow was absent.

- **Risk: authorization may currently be role-only while acceptance mentions role and policy checks.**
  - Reuse any existing permission JSON/policy evaluator if present; if not, add a narrow finance action policy layer without broad auth refactoring.

- **Risk: cockpit widget data contracts may not currently carry action metadata.**
  - Extend DTOs carefully to avoid breaking existing consumers.

- **Risk: alert detail UX pattern may be inconsistent across the app.**
  - Follow the dominant existing pattern: page, drawer, or modal. Do not invent a new one.

- **Risk: deep-link destinations may be partially implemented or named differently.**
  - Map to the closest existing finance destinations and document any naming mismatch in the PR/task notes.

Follow-ups to note if encountered:
- unify action-entry-point metadata across cockpit widgets beyond finance
- add richer audit trail visibility for user-triggered orchestration launches
- add mobile parity later if finance actions should appear in MAUI companion
- expand policy granularity from role-based gating to permission-claim/action-scope matrices if current model is too coarse