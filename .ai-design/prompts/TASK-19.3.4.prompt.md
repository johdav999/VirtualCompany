# Goal

Implement `TASK-19.3.4` for **US-19.3 ST-FUI-303 — Anomaly injection and simulation time controls** by adding **polling and/or refresh logic** so the admin UI keeps progression run status current after a run is started.

The result should ensure the UI reliably surfaces backend updates for:
- current progression run status
- run history
- generated-record counts
- warning messages
- failure messages

Prefer a pragmatic implementation that fits the existing .NET/Blazor architecture and current code patterns.

# Scope

In scope:
- Find the existing admin page, components, services, DTOs, and API endpoints related to:
  - anomaly scenario profile selection and submission
  - anomaly registry and anomaly detail view
  - simulation time advancement
  - progression run start/status/history display
- Add client-side refresh behavior for progression runs, using polling and/or explicit refresh actions.
- Ensure the UI updates after a progression run is started and while it remains in a non-terminal state.
- Show terminal outcomes clearly:
  - succeeded/completed
  - warning/completed-with-warnings if supported
  - failed
- Ensure generated-record counts and backend warning/failure messages are refreshed and rendered.
- Add or update tests for the new behavior where practical.

Out of scope unless required to support the task:
- redesigning the admin page
- changing unrelated anomaly injection flows
- introducing SignalR/websocket infrastructure
- broad backend workflow refactors
- changing domain semantics for progression runs beyond what is needed for refresh/polling

Implementation preference:
- Start with **simple Blazor polling** driven by component lifecycle and cancellation/disposal.
- If there is already a query/service abstraction for progression run status, reuse it.
- If no single endpoint returns the needed status/history payload, add the smallest safe backend/API enhancement necessary.

# Files to touch

Inspect and update only the files needed after discovery. Likely areas include:

- `src/VirtualCompany.Web/**`
  - admin page(s) for anomaly injection / simulation controls
  - related Razor components
  - code-behind files
  - typed API client/service classes
- `src/VirtualCompany.Api/**`
  - controllers/endpoints for progression run status/history if needed
- `src/VirtualCompany.Application/**`
  - queries/handlers/DTOs for progression run status/history
- `src/VirtualCompany.Shared/**`
  - shared contracts/view models if used between API and Web
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for any endpoint changes
- any existing web/UI test project if present

Before editing, identify the concrete files that currently implement:
- admin anomaly page
- progression run start action
- progression run status/history rendering
- API contract used by the page

# Implementation plan

1. **Discover the current implementation**
   - Search the solution for terms like:
     - `anomaly`
     - `simulation`
     - `progression`
     - `run history`
     - `advance time`
     - `admin`
   - Map the flow end-to-end:
     - UI page/component
     - web client/service
     - API endpoint
     - application query/handler
   - Determine:
     - how a progression run is started
     - how current run status is fetched
     - whether run history and generated-record counts come from the same or separate endpoints
     - what statuses exist and which are terminal vs non-terminal

2. **Design the refresh behavior**
   - Implement a polling strategy that is simple and safe for Blazor:
     - start polling after a progression run is initiated
     - continue polling while the latest run is in a non-terminal state
     - stop polling when the run reaches a terminal state
     - stop polling when the component is disposed or navigated away from
   - Suggested defaults:
     - interval around 2–5 seconds
     - immediate refresh right after starting a run
     - optional manual refresh button if useful
   - Avoid overlapping requests:
     - guard against re-entrant polling
     - use `CancellationTokenSource` and component disposal patterns
     - ensure `StateHasChanged` is invoked safely

3. **Update the UI state model**
   - Ensure the page/component has explicit state for:
     - latest progression run
     - run history collection
     - loading/refreshing state
     - polling active flag
     - last refresh timestamp if helpful
     - error state for failed refresh attempts
   - If the backend returns warnings/failures/messages, bind them into the UI so they refresh with each poll.
   - Ensure generated-record counts are refreshed from the latest payload.

4. **Add or refine API/query support if needed**
   - If the current API does not expose enough data in one call, add a focused query/endpoint that returns:
     - latest run status
     - run history
     - generated-record counts
     - warning/failure messages
   - Keep this CQRS-lite and tenant-scoped.
   - Do not introduce unnecessary new abstractions if an existing query can be extended safely.

5. **Implement robust terminal-state handling**
   - Define terminal statuses based on existing domain values, e.g.:
     - completed/succeeded
     - failed
     - cancelled if supported
   - Poll only for non-terminal statuses such as:
     - queued
     - pending
     - running
     - in_progress
   - If the backend supports partial completion with warnings, treat it as terminal and display warnings.

6. **Preserve UX clarity**
   - After the user starts a progression run:
     - disable duplicate start actions if appropriate while a run is active
     - show that status is refreshing
     - update the visible status without requiring a full page reload
   - Ensure the UI clearly shows:
     - current status badge/text
     - run history entries
     - generated-record counts
     - warning/failure messages from backend
   - If refresh fails transiently, show a non-blocking message and allow retry.

7. **Testing**
   - Add/update tests for any backend endpoint/query changes.
   - Add component/service tests if the project already has a pattern for them.
   - At minimum, verify:
     - starting a run triggers a refresh path
     - non-terminal status keeps polling
     - terminal status stops polling
     - warning/failure messages are surfaced
     - generated-record counts update from refreshed data

8. **Keep implementation aligned with architecture**
   - Respect tenant scoping and existing authorization patterns.
   - Keep UI concerns in Web, orchestration/query concerns in Application/API.
   - Do not bypass typed contracts or access the database directly from UI code.

# Validation steps

1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Manual verification in the admin UI**
   - Navigate to the admin page for anomaly injection / simulation controls.
   - Verify existing flows still work:
     - select anomaly scenario profile
     - submit anomaly injection request
     - view anomaly registry with ID, type, status, affected record reference
     - open anomaly detail view with scenario metadata and finance record link when available
     - advance simulation time by a specified increment
   - Start a progression run from the UI.
   - Confirm:
     - the page refreshes status automatically without manual reload
     - current run status changes as backend processing advances
     - run history updates
     - generated-record counts update
     - warning/failure messages appear when returned by backend
     - polling stops once the run reaches a terminal state

3. **Edge-case verification**
   - Refresh/navigate away during polling and confirm no unhandled exceptions occur.
   - Start a run and confirm duplicate/overlapping polling requests do not accumulate.
   - If backend returns an error during refresh, confirm the UI handles it gracefully.
   - If there is no active run, confirm the page remains stable and does not poll unnecessarily.

4. **Code quality checks**
   - Ensure component disposal cancels polling.
   - Ensure no fire-and-forget loops without cancellation.
   - Ensure status mapping is consistent with backend/domain values.

# Risks and follow-ups

- **Unknown existing status model**: progression run statuses may already have domain-specific names; reuse them rather than inventing new ones.
- **Fragmented data sources**: if status, history, and counts come from separate endpoints, polling may require coordinating multiple calls. Prefer consolidating into one read model if feasible.
- **Blazor lifecycle pitfalls**: careless polling can cause overlapping requests, memory leaks, or `ObjectDisposedException`. Be strict about cancellation and disposal.
- **Backend latency**: polling interval should be conservative enough to avoid unnecessary load.
- **Future enhancement**: if real-time updates become important, a later task can replace polling with SignalR/server push. Do not implement that now unless already present.
- **Potential follow-up task**: add a visible “Last updated” timestamp and manual refresh control if not already present.
- **Potential follow-up task**: disable or gate “Start progression run” when an active run is already in progress, if the backend enforces single active run semantics.