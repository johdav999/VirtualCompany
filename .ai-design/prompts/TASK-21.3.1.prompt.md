# Goal
Implement backlog task **TASK-21.3.1 — Add finance top-panel simulation controls and generation toggle bound to company simulation APIs** for story **US-21.3 Expose finance page simulation controls and live simulation status**.

Deliver a production-ready vertical slice in the existing .NET solution so that the finance page can display and control the active company simulation through backend APIs, with state updates reflected in the UI within 2 seconds of user actions.

# Scope
Implement only what is required to satisfy the acceptance criteria and fit the current architecture.

In scope:
- Add finance page top-panel simulation UI controls:
  - Start
  - Pause
  - Stop
  - Generation On/Off toggle
  - Status indicator
  - Current simulated date/time
  - Last progression timestamp
- Bind controls to company simulation APIs in the backend.
- Persist generation-enabled state to simulation state.
- Ensure UI refreshes after control actions and reflects backend state within 2 seconds.
- If backend/application support already exists or is straightforward to add, support:
  - Step forward 1 day
  - Refresh simulation state
- Ensure generated finance records appear in existing finance lists/detail views via normal data refresh/query paths, not a separate simulation-only UI.
- Respect tenant/company scoping and existing authorization patterns.
- Add or update tests for API/application/UI behavior where practical in the current test setup.

Out of scope unless already partially implemented and needed to complete the flow:
- New simulation engine design
- New finance list/detail screens
- Mobile support
- Broad refactors unrelated to finance simulation
- Polling/websocket infrastructure beyond a simple page-level refresh/polling mechanism sufficient for the 2-second update requirement

# Files to touch
Inspect the solution first and then update the most relevant files. Likely areas:

- `src/VirtualCompany.Web/**`
  - Finance page/component
  - Top panel/shared finance UI components
  - Client/service classes used by the finance page to call APIs
  - DTO/view-model mapping for simulation state
- `src/VirtualCompany.Api/**`
  - Finance or simulation controller/endpoints
  - Request/response contracts if API surface is missing/incomplete
- `src/VirtualCompany.Application/**`
  - Commands/queries/handlers for:
    - get simulation state
    - start simulation
    - pause simulation
    - stop simulation
    - set generation enabled
    - optional step forward one day
  - Validation and authorization hooks
- `src/VirtualCompany.Domain/**`
  - Simulation state model/enums/value objects if missing
- `src/VirtualCompany.Infrastructure/**`
  - Persistence/repositories for simulation state
  - Any background progression integration needed for timestamps/state persistence
- `src/VirtualCompany.Shared/**`
  - Shared contracts/enums if this solution uses shared DTOs between API/Web
- `tests/VirtualCompany.Api.Tests/**`
  - Endpoint and application behavior tests
- Any existing web/component test project if present

Before editing, locate:
- Existing finance page route/component
- Existing company simulation domain/API code
- Existing feature flag mechanism
- Existing tenant/company context resolution
- Existing finance data generation/progression code

# Implementation plan
1. **Discover existing simulation implementation**
   - Search for:
     - `Simulation`
     - `Finance`
     - `Generate financial data`
     - `Start`, `Pause`, `Stop`
     - `company_id`
     - feature flags/configuration
   - Identify whether simulation state already exists and what fields are available.
   - Confirm whether there is already an active company simulation concept and whether generation-enabled is already persisted.

2. **Define/complete the backend simulation state contract**
   - Ensure there is a single queryable simulation state for the active company containing at least:
     - status: `stopped | running | paused`
     - current simulated date/time
     - last progression timestamp
     - generation enabled boolean
   - If missing, add application/API DTOs and mappings.
   - Keep naming consistent across domain, API, and UI.

3. **Implement/complete backend control endpoints**
   - Ensure tenant-scoped endpoints or handlers exist for:
     - get current simulation state
     - start simulation
     - pause simulation
     - stop simulation
     - set generation enabled
   - If already present but incomplete, align them to acceptance criteria.
   - Enforce valid transitions:
     - Start allowed when stopped or paused as appropriate by current design
     - Pause allowed only when running
     - Stop allowed when running or paused
     - Generation toggle allowed before starting and while paused
     - Reject generation toggle while running if acceptance criteria disallow it
   - Persist generation-enabled selection to simulation state.

4. **Implement optional actions only if supported by current design**
   - If step-forward support exists or can be added cleanly:
     - Add endpoint/handler/UI action for “Step forward 1 day”
     - Advance simulated date by exactly 1 day once
     - Trigger one day of generation only when generation is On
   - If refresh support exists or can be added cleanly:
     - Add explicit refresh action in UI or service method to reload latest state without full page refresh

5. **Wire finance page top panel UI**
   - Add a top-panel section on the finance page gated by the relevant feature flag.
   - Display:
     - Start button
     - Pause button
     - Stop button
     - Status indicator
     - Current simulated date/time
     - Last progression timestamp
     - Generate financial data toggle
   - Disable/hide controls based on current state and allowed transitions.
   - Keep UI simple and consistent with existing design system/components.

6. **Implement state refresh behavior**
   - On page load, fetch current simulation state.
   - After any control action:
     - call the API
     - immediately re-fetch state
     - continue lightweight polling until the updated state is observed or for a short bounded window sufficient to satisfy the 2-second requirement
   - If the page already has a polling pattern, reuse it.
   - Avoid full page reloads.

7. **Ensure finance data visibility after progression**
   - Verify existing finance lists/detail views query normal persisted finance records.
   - After progression/step, trigger the existing data reload path so newly generated records appear in the current finance UI.
   - Do not create a separate simulation-only display.

8. **Feature flag integration**
   - Use the existing feature flag/configuration mechanism.
   - When disabled, the simulation controls should not render.
   - Do not break the finance page when the feature is off.

9. **Testing**
   - Add/update tests for:
     - simulation state query returns required fields
     - start/pause/stop transitions
     - generation toggle persistence and allowed-state rules
     - optional step-forward exact 1-day behavior if implemented
     - finance page/API integration behavior where feasible
   - Prefer focused tests over broad snapshot-style tests.

10. **Keep implementation aligned with architecture**
   - Maintain clean boundaries:
     - Web UI calls API/service layer
     - API delegates to application commands/queries
     - application/domain own transition rules
     - infrastructure persists state
   - Preserve tenant isolation and authorization checks.

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Manual verification in the web app:
   - Open the finance page for a company with the feature enabled.
   - Confirm the top panel shows:
     - Start
     - Pause
     - Stop
     - status
     - simulated date/time
     - last progression timestamp
     - generation toggle
   - Start simulation and verify status updates to `running` within 2 seconds.
   - Pause simulation and verify status updates to `paused` within 2 seconds.
   - Stop simulation and verify status updates to `stopped` within 2 seconds.
   - While stopped or paused, toggle generation On/Off and verify the selected value persists after refresh/reload.
   - If step-forward is implemented:
     - invoke it once
     - verify simulated date advances by exactly 1 day
     - verify generation occurs only when generation is On
   - If refresh is implemented:
     - invoke refresh
     - verify latest backend state loads without full page refresh
   - After progression/generation, verify generated records appear in existing finance lists/detail views.

3. Negative/edge validation:
   - With feature flag disabled, confirm controls are hidden.
   - Attempt invalid transitions and confirm safe handling.
   - Confirm company/tenant scoping prevents cross-company access.

# Risks and follow-ups
- The exact simulation domain model and API surface may already exist under different names; avoid duplicating concepts.
- If there is no existing polling/state refresh pattern in Blazor, implement the smallest reliable approach that meets the 2-second requirement.
- Be careful with timezone handling for simulated date/time and last progression timestamp; use the company timezone if the app already standardizes on it, otherwise preserve current conventions consistently.
- If generation toggle semantics are ambiguous while running, follow the acceptance criteria strictly: allow changes before starting and while paused.
- If step-forward or refresh are not already implemented and would require disproportionate new infrastructure, keep them out unless they can be added cleanly and safely.
- If generated records are not appearing automatically, prefer reusing existing finance data reload/query mechanisms rather than adding simulation-specific rendering.
- Document any assumptions in code comments or a short implementation note if the existing simulation subsystem is incomplete.