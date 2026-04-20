# Goal
Implement backlog task **TASK-21.3.3 — Implement optional step-forward and refresh actions with optimistic UI state handling** for **US-21.3 Expose finance page simulation controls and live simulation status**.

Deliver a coding change that adds finance-page simulation controls and status display for the active company simulation, with **optimistic UI updates** for control actions and safe reconciliation with backend state.

The implementation must satisfy these outcomes:

- When the feature is enabled, the finance page top panel shows **Start**, **Pause**, and **Stop** controls for the active company simulation.
- The finance page shows a simulation status indicator with values **stopped**, **running**, or **paused**, and the displayed state updates within **2 seconds** of a control action.
- The finance page shows:
  - current simulated date/time
  - last progression timestamp
  - whether generation is enabled
- Users can toggle **Generate financial data during simulation** between **On** and **Off**:
  - before starting
  - while paused
  - persisted to simulation state
- If **Step forward 1 day** is implemented, it must:
  - advance simulated date by exactly 1 day once
  - trigger one day of generation only when generation is **On**
- If **Refresh simulation state** is implemented, it must reload latest backend state without full page refresh.
- Generated records must appear in existing finance lists/detail views after progression, without a separate simulation-only UI.

Use the existing .NET solution structure and preserve tenant/company scoping, CQRS-lite boundaries, and modular-monolith conventions.

# Scope
Implement only what is required for this task and its direct dependencies.

In scope:

- Finance page UI updates for simulation controls/status in the top panel
- Feature-flag-aware rendering of simulation controls
- Optimistic UI state handling for simulation actions
- Backend/API/application wiring needed to:
  - read current simulation state
  - start simulation
  - pause simulation
  - stop simulation
  - update generation-enabled flag
- Optional support for:
  - step-forward-by-1-day action
  - refresh simulation state action
- Polling or lightweight refresh/reconciliation so UI reflects backend-confirmed state within 2 seconds after actions
- Refreshing/rebinding existing finance data views after progression so generated records become visible in normal finance UI

Out of scope unless already partially present and required to complete this task:

- New simulation domain redesign
- Separate simulation dashboard
- Mobile app changes
- Broad refactors unrelated to finance simulation controls
- New persistence model unless required to support acceptance criteria
- Reworking unrelated finance list/detail components beyond what is needed to refresh data after progression

Assumptions to validate in code before implementing:

- There is already some simulation domain/service/API surface from prior tasks in US-21.3 or adjacent work.
- There is an existing finance page/component in `src/VirtualCompany.Web`.
- There may already be feature flag infrastructure and company-scoped simulation state models.
- Existing finance records/list/detail queries can be reloaded after simulation progression.

If existing step-forward or refresh endpoints/actions already exist, integrate them. If not, implement them only if they are small, consistent extensions of the current simulation API.

# Files to touch
Inspect the solution first, then update the minimal correct set of files. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance page/component
  - top panel/status UI
  - client/service used to call simulation endpoints
  - feature flag checks
  - optimistic state/view-model handling
- `src/VirtualCompany.Api/**`
  - simulation controller/endpoints for finance page actions
  - request/response DTO mapping if API-owned
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for simulation state and control actions
  - validation and company scoping
- `src/VirtualCompany.Domain/**`
  - simulation state/status/value objects if changes are needed
- `src/VirtualCompany.Infrastructure/**`
  - persistence/repository updates if simulation state persistence or refresh support needs wiring
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts/enums if used across API/Web
- `tests/VirtualCompany.Api.Tests/**`
  - API/application integration tests for control actions and state transitions
- Potential web test project if present
  - component/service tests for optimistic UI behavior

Before editing, identify the actual finance simulation files and use those exact paths.

# Implementation plan
1. **Discover existing simulation architecture**
   - Find current finance page, simulation models, endpoints, feature flags, and any prior US-21.3 work.
   - Identify:
     - simulation status enum/string values
     - current state query endpoint
     - existing start/pause/stop commands
     - generation-enabled persistence
     - whether step-forward and refresh already exist
     - how finance lists/detail views currently load data
   - Do not invent parallel patterns if an existing simulation stack already exists.

2. **Define/confirm the state contract**
   - Ensure there is a single company-scoped simulation state DTO/view model containing at least:
     - status: `stopped | running | paused`
     - current simulated date/time
     - last progression timestamp
     - generation enabled flag
     - any in-flight metadata useful for optimistic UI reconciliation
   - If needed, add optional capability flags such as:
     - `canStart`, `canPause`, `canStop`
     - `canToggleGeneration`
     - `supportsStepForward`
     - `supportsRefresh`
   - Keep naming consistent with existing codebase conventions.

3. **Implement/complete backend commands and query**
   - Ensure there is a query to fetch the latest simulation state for the active company.
   - Ensure commands exist and persist state correctly for:
     - start
     - pause
     - stop
     - set generation enabled
   - If step-forward is implemented:
     - enforce exactly +1 day progression
     - generation occurs only when generation is enabled
   - If refresh is implemented:
     - return latest persisted/backend-derived state without full page reload
   - Preserve tenant/company authorization and scoping on every request.

4. **Add API endpoints if missing**
   - Expose minimal endpoints for the finance page:
     - get simulation state
     - start
     - pause
     - stop
     - update generation enabled
     - optional step-forward
     - optional refresh
   - Return the updated simulation state in action responses where practical to simplify optimistic reconciliation.
   - Use safe, explicit request/response contracts.

5. **Implement optimistic UI state handling in Web**
   - On finance page load, fetch simulation state and render top-panel controls/status when feature is enabled.
   - For each action:
     - immediately update local UI state optimistically
     - disable conflicting controls while request is in flight
     - show pending/busy feedback if the page already has a pattern for this
     - reconcile with server response when it returns
     - if request fails, roll back optimistic state and surface a user-visible error
   - Ensure the visible status changes within 2 seconds of action initiation by combining:
     - immediate optimistic update
     - short follow-up refresh/poll to confirm backend state

6. **Render required finance page status details**
   - Add display of:
     - simulation status
     - current simulated date/time
     - last progression timestamp
     - generation enabled state
   - Keep UI compact and aligned with existing finance page styling.
   - Only show controls when the feature is enabled; if disabled, preserve current behavior.

7. **Generation toggle behavior**
   - Allow toggling generation:
     - before simulation starts
     - while paused
   - Prevent or disable toggle while running if acceptance criteria imply only before start and while paused.
   - Persist the selected value to simulation state and reflect it after refresh/reload.
   - Ensure optimistic toggle behavior also rolls back on failure.

8. **Optional step-forward support**
   - If the codebase already supports or can cleanly support it in this task, add a **Step forward 1 day** action.
   - Enforce:
     - exactly one day advancement
     - one day of generation only when generation is On
   - Update UI state optimistically if safe; otherwise use immediate busy state plus refresh/reconciliation.
   - Refresh finance data after successful progression so generated records appear in existing lists/detail views.

9. **Optional refresh support**
   - If implemented, add a **Refresh simulation state** action that re-fetches backend state without full page reload.
   - This should update:
     - status
     - simulated date/time
     - last progression timestamp
     - generation enabled
   - Reuse the same state-loading method used by initial page load and post-action reconciliation.

10. **Refresh existing finance data after progression**
    - After successful progression actions that may generate records:
      - trigger reload of existing finance list/detail data sources
      - do not create simulation-only views
    - Ensure generated records become visible through the normal finance UI pathways.

11. **Feature flag integration**
    - Respect existing feature flag/configuration mechanism.
    - Controls/status panel should only render when the feature is enabled.
    - Avoid leaking partially functional controls when disabled.

12. **Testing**
    - Add/extend tests for:
      - state query returns required fields
      - start/pause/stop transitions
      - generation-enabled persistence
      - optional step-forward exact +1 day behavior
      - generation only when enabled during step-forward
      - refresh returns latest state
      - company scoping/authorization
   - If web/component tests exist, add tests for:
      - optimistic status update
      - rollback on failed action
      - toggle enablement rules by status

13. **Keep implementation clean**
    - Prefer small targeted changes over broad refactors.
    - Reuse existing command/query and service abstractions.
    - Keep domain rules server-enforced even if UI disables invalid actions.

# Validation steps
Run discovery first, then validate with the smallest reliable set of checks.

1. **Build**
   - Run:
     - `dotnet build`

2. **Tests**
   - Run:
     - `dotnet test`

3. **Manual verification in web app**
   - Start the app using the project’s normal local run flow.
   - Navigate to the finance page for a company with the feature enabled.
   - Verify:
     - top panel shows Start, Pause, Stop
     - status indicator shows one of stopped/running/paused
     - current simulated date/time is visible
     - last progression timestamp is visible
     - generation enabled state is visible
   - Action checks:
     - click Start → UI updates immediately and reconciles with backend within 2 seconds
     - click Pause → UI updates immediately and reconciles within 2 seconds
     - click Stop → UI updates immediately and reconciles within 2 seconds
     - toggle generation before start and while paused → persisted after refresh/reload
   - If step-forward implemented:
     - invoke once and verify simulated date advances by exactly 1 day
     - verify generation occurs only when generation is On
   - If refresh implemented:
     - invoke refresh and verify latest backend state loads without full page refresh
   - Verify generated records appear in existing finance lists/detail views after progression.

4. **Failure-path verification**
   - Simulate or force an API failure for one control action if practical.
   - Verify optimistic UI rolls back and user sees an error message.
   - Verify controls are not left stuck in an invalid in-flight state.

5. **Tenant-scope verification**
   - Confirm simulation state/actions are company-scoped and do not leak across tenants.

# Risks and follow-ups
- **Risk: existing simulation model may be incomplete or inconsistent**
  - Mitigation: inspect current domain/API first and extend minimally rather than creating duplicate state models.

- **Risk: optimistic UI may drift from backend truth**
  - Mitigation: always reconcile with action response and/or short follow-up refresh/poll; roll back on failure.

- **Risk: unclear rules for generation toggle while running**
  - Acceptance criteria explicitly require toggling before starting and while paused. If running behavior is unsupported, disable it in UI and enforce on server.

- **Risk: step-forward may require background processing semantics**
  - If progression is asynchronous, ensure the UI still updates within 2 seconds via optimistic state plus refresh. If exact one-day progression cannot be guaranteed synchronously, document the current behavior and keep server rules explicit.

- **Risk: finance data views may cache stale data**
  - Ensure progression success triggers reload/invalidation of existing finance queries/components.

- **Risk: feature flag wiring may differ between API and Web**
  - Keep rendering and action availability aligned so disabled features do not expose dead controls.

Follow-ups to note in code comments or task notes if not completed here:
- Add richer live updates via SignalR/server push if polling is currently used.
- Add dedicated component tests for optimistic state transitions if web test coverage is limited.
- Add audit events for simulation control actions if not already present.
- Consider exposing capability flags from backend to simplify UI enable/disable logic.