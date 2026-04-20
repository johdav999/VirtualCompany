# Goal
Implement backlog task **TASK-19.3.1 — Build anomaly injection form and registry list components** for story **US-19.3 ST-FUI-303 — Anomaly injection and simulation time controls** in the existing .NET solution.

Deliver the admin-facing web UI needed to:
- submit anomaly injection requests by selecting a scenario profile
- list anomaly registry entries
- open anomaly details
- control simulation time advancement and progression runs
- display progression run status, history, generated-record counts, and backend warning/failure messages

Assume this task is primarily a **Blazor Web** implementation integrated with existing or in-progress backend APIs/contracts. Reuse established app patterns, tenant-aware admin routing, shared DTOs, and existing styling/components where possible.

# Scope
In scope:
- Admin page UI components for anomaly injection and simulation controls
- Anomaly scenario selection and submit flow
- Registry list/table for anomalies
- Detail panel/view for selected anomaly
- Simulation time controls:
  - advance by increment
  - start progression run
- Progression run status/history presentation
- Display of generated-record counts and warning/failure messages from backend
- Loading, empty, success, and error states
- Basic validation and disabled states during submission
- Wiring to existing application/API endpoints or adding minimal client-side service abstractions if needed

Out of scope unless strictly required to complete the UI:
- New backend domain logic for anomaly generation/simulation engine
- Major API redesign
- New persistence schema unless already missing and absolutely necessary
- Mobile app work
- Broad dashboard redesign outside the relevant admin page
- Nonessential refactors unrelated to this task

# Files to touch
Inspect the solution first and then update only the minimum necessary files. Likely areas include:

- `src/VirtualCompany.Web/**`
  - admin page(s) for finance/simulation/anomaly controls
  - reusable Blazor components for:
    - anomaly injection form
    - anomaly registry list
    - anomaly detail view
    - simulation time controls
    - progression run history/status panel
  - client-side service classes for calling API endpoints
  - view models / UI state models
- `src/VirtualCompany.Shared/**`
  - shared request/response DTOs for anomaly registry, detail, injection, simulation time, progression runs
- `src/VirtualCompany.Api/**`
  - only if endpoint surface or DTO mapping is missing for the UI contract
- `tests/**`
  - component/service/API tests aligned to the implementation approach

Before editing, identify the actual existing files for:
- admin finance pages
- simulation/anomaly-related DTOs
- API clients/services
- shared UI patterns
- test conventions

# Implementation plan
1. **Discover existing architecture and patterns**
   - Search for:
     - admin pages in `VirtualCompany.Web`
     - finance record views
     - existing simulation/anomaly/progression endpoints
     - shared DTOs in `VirtualCompany.Shared`
     - API client abstractions already used by Blazor pages
   - Follow existing conventions for:
     - routing
     - authorization
     - tenant/company context
     - form handling
     - tables/lists
     - status badges
     - error messaging

2. **Identify or define the UI contract**
   - Confirm whether backend/shared contracts already exist for:
     - available anomaly scenario profiles
     - create anomaly injection request
     - anomaly registry list
     - anomaly detail
     - simulation time advance request/result
     - progression run start request/result
     - progression run history/status
   - If missing, add minimal DTOs in `VirtualCompany.Shared` and wire them through the existing API/client pattern.
   - Keep contracts explicit and UI-friendly, including fields for acceptance criteria:
     - anomaly: `Id`, `Type`, `Status`, `AffectedRecordReference`
     - detail metadata: scenario profile/code/name, created/submitted timestamps, parameters, backend messages
     - finance record link/reference when available
     - progression run: status, started/completed timestamps, generated-record counts, warnings, failures

3. **Build the anomaly injection form component**
   - Create or update a Blazor component that:
     - loads available anomaly scenario profiles
     - allows selecting a profile
     - optionally captures any required parameters already supported by backend
     - submits an anomaly injection request
   - Include:
     - validation for required selection
     - loading/submitting state
     - success/error feedback
     - refresh of registry after successful submission
   - Ensure the form is admin-page appropriate and tenant-scoped.

4. **Build the anomaly registry list component**
   - Render a list/table showing:
     - anomaly ID
     - type
     - status
     - affected record reference
   - Add:
     - loading state
     - empty state
     - row selection behavior
     - refresh support after injection or progression actions
   - Use existing table/list styling patterns if present.

5. **Build the anomaly detail view**
   - When a registry item is selected:
     - load or display anomaly detail
     - show scenario metadata
     - show related finance record link when available
   - Include:
     - clear handling when no item is selected
     - graceful display when related record is unavailable
     - warning/failure/backend message sections if returned

6. **Build simulation time controls**
   - Add UI controls to:
     - advance simulation time by a specified increment
     - start a progression run
   - Include:
     - increment input with validation
     - disabled state while request is in progress
     - success/error messaging
   - After actions complete, refresh:
     - current run status
     - run history
     - generated-record counts
     - anomaly registry if impacted

7. **Build progression run status/history UI**
   - Add a panel/component showing:
     - current/latest run status
     - run history
     - generated-record counts
     - warning/failure messages returned by backend
   - Prefer a compact summary + history list/table.
   - Make statuses visually distinct using existing badge/alert patterns.

8. **Compose the admin page**
   - Integrate the components into the relevant admin page with a coherent layout:
     - anomaly injection form
     - simulation controls
     - registry list
     - detail view
     - progression status/history
   - Keep the page responsive and readable.
   - Avoid overengineering; prioritize acceptance criteria coverage.

9. **Add tests**
   - Add tests appropriate to the project’s current test setup:
     - DTO/service tests for request/response handling
     - component tests if the repo already uses Blazor component testing
     - API tests only if minimal endpoint additions were required
   - At minimum, cover:
     - required validation for scenario selection/increment input
     - mapping/display of anomaly registry fields
     - detail view finance record link behavior
     - progression warning/failure message rendering

10. **Keep implementation safe and incremental**
   - Do not break unrelated admin pages.
   - Preserve existing authorization and tenant scoping.
   - Prefer additive changes and minimal API surface changes.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Navigate to the relevant admin page
   - Confirm anomaly scenario profiles load
   - Submit an anomaly injection request successfully
   - Confirm the registry updates and shows:
     - ID
     - type
     - status
     - affected record reference
   - Select a registry item and confirm detail view shows:
     - scenario metadata
     - related finance record link when available
   - Advance simulation time with a valid increment
   - Start a progression run
   - Confirm UI shows:
     - run status
     - run history
     - generated-record counts
     - warning/failure messages from backend
   - Verify loading, empty, and error states
   - Verify controls are disabled appropriately during in-flight requests

4. If API changes were required:
   - Verify endpoints remain tenant-scoped and consistent with existing auth patterns
   - Verify serialization/deserialization of shared DTOs

# Risks and follow-ups
- **Backend contract gaps:** The UI may depend on endpoints/DTOs not yet implemented. If so, add only the minimal contract surface needed and document any backend follow-up.
- **Unclear admin page location:** The exact page/module may differ from assumptions; inspect the repo before creating new routes.
- **Finance record linking:** Related finance record URLs may require existing route conventions; reuse current finance detail navigation patterns.
- **Long-running progression runs:** If runs are asynchronous, the UI may need polling/refresh behavior. Implement the simplest pattern consistent with existing app behavior and note any enhancement follow-up.
- **Message shape variability:** Backend warnings/failures may come in different formats; normalize display defensively.
- **Testing constraints:** If the repo lacks component test infrastructure, add focused service/unit tests instead of introducing a large new test framework.
- **Follow-up candidates:** filtering/sorting for anomaly registry, auto-refresh for run status, richer scenario parameter inputs, pagination/history retention UX.