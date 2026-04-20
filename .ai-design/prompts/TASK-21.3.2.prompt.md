# Goal
Implement backlog task **TASK-21.3.2** for story **US-21.3 Expose finance page simulation controls and live simulation status** by adding finance-page UI and supporting application/API wiring so the finance page can render and update:

- live simulation controls: **Start**, **Pause**, **Stop**
- simulation status: **stopped / running / paused**
- current simulated date/time
- last progression timestamp/metadata
- whether generation is enabled
- toggle for **Generate financial data during simulation**
- optional **Step forward 1 day** action if backend support already exists
- optional **Refresh simulation state** action if backend support already exists

The implementation must ensure generated records appear in the **existing finance lists and detail views**, not in a separate simulation-only UI.

# Scope
In scope:

- Discover the existing finance page, simulation domain/application/API contracts, and feature-flag pattern.
- Add or extend finance page top-panel UI to show simulation controls and live status when the feature is enabled.
- Bind UI to existing backend endpoints/services where available; add minimal missing query/command plumbing if required.
- Ensure control actions update visible status within **2 seconds** using one or more of:
  - optimistic UI update
  - short polling / refresh loop
  - explicit refresh after command completion
- Render:
  - simulation status
  - simulated date/time
  - last progression timestamp
  - generation enabled state
- Allow toggling generation **before start** and **while paused**, persisting the selected value to simulation state.
- If step-forward support exists, surface it and ensure it advances exactly one day and only generates one day when generation is On.
- If refresh support exists, surface it and reload latest backend state without full page refresh.
- Refresh or invalidate finance data views after progression so generated records become visible in existing finance screens.

Out of scope unless required for compilation or acceptance:

- Building an entirely new simulation engine.
- Adding mobile support.
- Creating a separate simulation dashboard.
- Large refactors unrelated to finance simulation.
- Implementing optional step/refresh backend behavior from scratch if the architecture already clearly treats them as not yet available; in that case, gate/hide the UI cleanly.

# Files to touch
Inspect first, then update only the necessary files in these likely areas:

- `src/VirtualCompany.Web/**`
  - finance page/component(s)
  - shared top-panel/status components
  - feature-flag checks
  - client-side service(s) for simulation state/actions
- `src/VirtualCompany.Api/**`
  - finance simulation controller/endpoints if UI-facing endpoints are missing or incomplete
- `src/VirtualCompany.Application/**`
  - finance simulation queries/commands/DTOs
  - handlers/services for reading and updating simulation state
- `src/VirtualCompany.Domain/**`
  - simulation state/value objects/enums only if needed
- `src/VirtualCompany.Shared/**`
  - shared contracts/view models if the solution uses shared DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for simulation state/query/action behavior
- Any existing web/UI test project if present
- `README.md` or nearby docs only if there is an established convention to document feature flags or finance simulation behavior

Before editing, identify the exact files that currently implement:

- finance page rendering
- simulation state retrieval
- simulation control actions
- finance list/detail refresh behavior
- feature enablement for this story

# Implementation plan
1. **Discover current implementation**
   - Locate the finance page and top panel component.
   - Find existing simulation-related models, endpoints, commands, and feature flags.
   - Determine whether **Step forward 1 day** and **Refresh simulation state** are already implemented.
   - Determine how finance lists/detail views currently load data and how to trigger refresh/invalidation.

2. **Define/extend the UI-facing simulation state contract**
   - Ensure the finance page can retrieve a single state payload containing at least:
     - active company identifier/context
     - status enum/string: `Stopped`, `Running`, `Paused`
     - simulated date/time
     - last progression timestamp
     - generation enabled boolean
     - optional capability flags such as:
       - `CanStart`
       - `CanPause`
       - `CanStop`
       - `CanToggleGeneration`
       - `SupportsStepForwardOneDay`
       - `SupportsRefresh`
   - If the codebase already has a DTO, extend it rather than creating a parallel contract.

3. **Implement or wire finance page top panel**
   - Add a simulation section to the finance page top panel, shown only when the feature is enabled.
   - Render:
     - Start button
     - Pause button
     - Stop button
     - status badge/indicator
     - simulated date/time
     - last progression timestamp
     - generation enabled state/toggle
   - If supported, render:
     - Step forward 1 day button
     - Refresh simulation state button
   - Disable controls appropriately based on current state and capability flags.
   - Prevent generation toggle changes while running if acceptance requires only before start and while paused.

4. **Wire control actions**
   - Connect Start/Pause/Stop to existing commands/endpoints.
   - Connect generation toggle persistence to existing update-state command/endpoint or add a minimal one if missing.
   - If supported, connect Step forward 1 day and Refresh.
   - After each action:
     - update local UI state immediately where safe
     - fetch latest backend state
     - ensure visible status updates within 2 seconds

5. **Implement live status refresh**
   - Add a lightweight refresh strategy for the finance page while it is open:
     - preferred: short polling every ~2 seconds while simulation is running
     - also trigger immediate refresh after control actions
   - Ensure polling is disposed correctly when the component unmounts/disposes.
   - Avoid excessive requests; only poll when feature is enabled and simulation panel is visible, and preferably only while running or during a short post-action window.

6. **Persist generation-enabled state**
   - Ensure the toggle writes to simulation state and survives subsequent refreshes.
   - Enforce allowed transitions:
     - editable before start
     - editable while paused
     - not editable while running unless existing product rules explicitly allow it
   - Keep backend validation aligned with UI behavior.

7. **Handle optional step-forward behavior**
   - Only surface the button if support exists.
   - Ensure invocation advances simulated date by exactly one day once.
   - Ensure one day of generation occurs only when generation is On.
   - Re-query state after stepping and refresh finance data views.

8. **Handle optional refresh behavior**
   - Only surface the button if support exists.
   - Reload latest backend state without full page refresh.
   - Use the same refresh path as post-action state reload where possible.

9. **Refresh finance data views**
   - After progression-related changes, ensure existing finance lists/detail views reload or invalidate cached data so generated records appear naturally in the existing UI.
   - Reuse existing page/query refresh mechanisms instead of adding simulation-specific lists.

10. **Add tests**
   - Add/extend API/application tests for:
     - state query returns required metadata
     - generation-enabled toggle persists
     - status transitions for start/pause/stop
     - optional step-forward behavior if implemented
   - Add UI/component tests if the project already uses them; otherwise keep UI logic simple and covered by lower-level tests where possible.

11. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - Web UI calls API/application contracts
     - no direct DB access from UI
     - commands for state changes, queries for reads
   - Keep tenant/company scoping intact for all simulation operations.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in the web app:
   - Open the finance page with the feature flag disabled:
     - confirm simulation panel/controls are hidden
   - Open the finance page with the feature flag enabled:
     - confirm Start, Pause, Stop controls are visible
     - confirm status indicator is visible
     - confirm simulated date/time, last progression timestamp, and generation enabled state are visible

4. Control behavior:
   - Click **Start**
     - confirm status changes to `running` within 2 seconds
   - Click **Pause**
     - confirm status changes to `paused` within 2 seconds
   - Click **Stop**
     - confirm status changes to `stopped` within 2 seconds

5. Generation toggle:
   - While stopped or before starting, toggle **Generate financial data during simulation**
     - refresh state
     - confirm selected value persists
   - While paused, toggle it again
     - confirm selected value persists
   - While running
     - confirm toggle is disabled or otherwise prevented if that is the intended rule

6. Optional actions:
   - If **Step forward 1 day** is available:
     - invoke once
     - confirm simulated date advances by exactly 1 day
     - confirm generation occurs only when generation is On
   - If **Refresh simulation state** is available:
     - invoke it
     - confirm latest backend state reloads without full page refresh

7. Finance data visibility:
   - After progression/generation, confirm newly generated records appear in the existing finance lists and detail views without navigating to a separate simulation UI.

8. Regression checks:
   - Confirm no tenant/company scoping regressions
   - Confirm finance page still loads when simulation feature is disabled
   - Confirm no runaway polling or disposal errors in browser logs/server logs

# Risks and follow-ups
- **Unknown existing contracts:** Simulation endpoints/models may already exist under different names; prefer extending them over duplicating.
- **Feature-flag ambiguity:** If no clear feature-flag mechanism exists, follow the project’s established configuration pattern rather than inventing a new one.
- **Polling load:** A 2-second refresh loop can create unnecessary traffic; scope it narrowly to active/running states and dispose cleanly.
- **State race conditions:** Start/Pause/Stop and toggle actions may overlap; guard against double-clicks and stale UI state.
- **Optional capability uncertainty:** Do not fake Step/Refresh support. Detect and render only when actually supported.
- **Finance data refresh coupling:** Existing finance lists may cache aggressively; use the current invalidation/reload mechanism so generated records appear consistently.
- **Timezone/display correctness:** Simulated date/time and last progression timestamps should use the app’s established formatting and company timezone rules.
- **Follow-up candidate:** If live updates are expected to expand, consider a future push-based mechanism (SignalR/server-sent events) instead of polling, but do not introduce that unless it already exists or is clearly warranted by current patterns.