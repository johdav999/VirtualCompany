# Goal
Implement TASK-33.3.2 by wiring the Finance settings Fortnox UI in the Blazor web app to existing backend endpoints so the page shows live integration state and supports connect, reconnect, manual sync, and disconnect actions with optimistic refresh, permission-aware rendering, and safe error handling.

# Scope
In scope:
- Finance settings page Fortnox section UI/data binding
- Backend API client/service calls from web app to Fortnox settings endpoints
- Loading, success, and failure states for:
  - initial status load
  - connect
  - reconnect
  - manual sync
  - disconnect
- Optimistic state refresh after actions without full page reload
- Sync history rendering with safe user-facing formatting
- Permission-gated visibility and action enablement
- Mapping backend DTOs/status values into user-friendly labels
- Preventing display of internal enums, raw payloads, token values, or unsafe error details
- Tests for UI/service behavior where practical in current solution patterns

Out of scope:
- Creating new Fortnox backend business logic if endpoints already exist
- OAuth/provider backend redesign
- Database schema changes unless absolutely required for an already-missing read model contract
- Mobile app changes
- Non-Fortnox finance settings unrelated to this task

# Files to touch
Inspect first, then update only the files needed. Likely areas:

- `src/VirtualCompany.Web/**`
  - Finance settings page/component(s)
  - Shared settings components
  - Web API client/service abstractions
  - Permission/authorization-aware UI helpers
  - DTO/view-model mapping code
- `src/VirtualCompany.Shared/**`
  - Shared contracts/DTOs for Fortnox settings and sync history, if shared between API and Web
- `src/VirtualCompany.Api/**`
  - Only if a missing endpoint surface or response contract adjustment is required for UI consumption
- `src/VirtualCompany.Application/**`
  - Only if query/command contracts need minor additions already implied by acceptance criteria
- `tests/VirtualCompany.Api.Tests/**`
  - If API contract tests need updates
- Add or update web/UI tests if a web test project exists; otherwise add focused unit tests in existing test structure where feasible

Before coding, locate:
- `docs/design.md`
- Existing finance settings page
- Existing Fortnox endpoints/controllers
- Existing permission model for tenant/company admin or finance settings access
- Existing patterns for Blazor data loading, action buttons, toast/alert messaging, and API error handling

# Implementation plan
1. **Discover existing contracts and UI structure**
   - Find the Finance settings page and confirm how it currently renders Fortnox settings.
   - Find backend endpoints for:
     - current Fortnox connection status/details
     - connect start
     - reconnect start
     - manual sync
     - disconnect
     - sync history
   - Identify whether connect/reconnect returns a redirect URL, pending state, or command acknowledgment.
   - Confirm permission names/policies already used for finance settings and tenant admin actions.
   - Review `docs/design.md` and align labels/layout/states to it.

2. **Define a safe UI-facing Fortnox view model**
   - Create or refine a web-facing model that contains only:
     - connection status label
     - last successful sync timestamp
     - last failure summary
     - available actions
     - sync history items with timestamp, status label, imported counts, duration, safe error summary
   - Add mapping from backend DTOs/enums to plain-English labels.
   - Ensure no raw enum names are rendered directly.
   - Ensure no token/provider payload fields are exposed in UI models.

3. **Wire initial page load**
   - On Finance settings page load, fetch Fortnox status/details and sync history from backend APIs.
   - Render:
     - current connection status
     - last successful sync time
     - last failure summary
     - action buttons based on state and permissions
     - sync history list/table
   - Add loading skeleton/spinner and empty states as appropriate.

4. **Implement permission-aware rendering**
   - Hide the Fortnox action controls entirely for users lacking required tenant permissions.
   - If the whole Fortnox section should be hidden per existing policy, follow that pattern.
   - Also guard execution in the client by disabling/not rendering action triggers when permission is absent.
   - Do not rely on UI-only security; preserve backend authorization behavior.

5. **Implement action handlers**
   - Add handlers for:
     - connect
     - reconnect
     - manual sync
     - disconnect
   - Use backend APIs only.
   - For each action:
     - set local in-flight state
     - disable duplicate submissions
     - call API
     - show success/failure feedback
     - refresh Fortnox status and sync history after completion without page reload
   - If connect/reconnect returns a URL for provider auth, navigate using the returned URL while still handling pre-navigation failures safely.

6. **Add optimistic refresh behavior**
   - Immediately reflect action-in-progress state in the UI:
     - e.g. syncing indicator, disabling buttons, temporary status text
   - After action completion, re-query status + history from backend and reconcile UI.
   - If action fails, roll back optimistic indicators and show safe error messaging.
   - Avoid stale state by centralizing refresh logic in one method used after every action.

7. **Implement safe error handling**
   - Normalize API failures into plain-English messages.
   - Never surface:
     - internal enum names
     - stack traces
     - raw provider payloads
     - token values
     - opaque backend exception text unless already sanitized
   - Prefer a fallback such as:
     - “Couldn’t start Fortnox connection. Please try again.”
     - “Sync failed. Review the latest sync history entry for a safe summary.”
   - Render last failure summary only if already sanitized; otherwise map to a generic safe summary.

8. **Render sync history correctly**
   - Show for each history row:
     - timestamp
     - status label
     - imported entity counts
     - duration
     - safe error summary
   - Format timestamps and durations consistently with existing app conventions.
   - If counts are per entity type, render human-readable labels.
   - If no history exists, show a clear empty state.

9. **Polish UX states**
   - Ensure buttons reflect valid actions by current state:
     - disconnected: connect
     - connected: manual sync, disconnect, maybe reconnect
     - failed/expired: reconnect, disconnect as appropriate
   - Prevent multiple concurrent clicks.
   - Preserve responsiveness without full page refresh.
   - Use existing toast/banner/inline alert components rather than inventing a new pattern.

10. **Testing**
   - Add tests for mapping/sanitization logic.
   - Add tests for permission-based visibility if current test setup supports component/unit tests.
   - Add tests for action handler state transitions where practical.
   - If API contracts changed, update/add API tests accordingly.

11. **Keep implementation aligned with architecture**
   - Maintain clean boundaries:
     - Web app calls typed API/application contracts
     - no direct DB access
     - no provider-specific secrets in UI
   - Reuse CQRS-lite query/command endpoints already present.

# Validation steps
1. Locate and review `docs/design.md`; verify the Finance settings Fortnox section matches intended layout/content.
2. Build solution:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Manual verification in web app:
   - Open Finance settings as authorized company admin
   - Confirm current Fortnox status loads from backend
   - Confirm last successful sync time displays when available
   - Confirm last failure summary displays safely when available
   - Confirm sync history shows timestamp, status, counts, duration, and safe error summary
5. Action verification as authorized user:
   - Trigger connect and verify backend API is called and UI updates without full page refresh
   - Trigger reconnect and verify same
   - Trigger manual sync and verify in-flight state, completion refresh, and history update
   - Trigger disconnect and verify state refresh
6. Permission verification:
   - Sign in as user without required tenant permissions
   - Confirm Fortnox actions are hidden or unavailable
   - Confirm no action can be executed from UI
7. Safety verification:
   - Inspect rendered UI and network-bound models for absence of:
     - raw enum names
     - raw provider payloads
     - token values
     - unsafe exception details
8. Failure-path verification:
   - Simulate or use a failing backend response for each action
   - Confirm safe error message is shown
   - Confirm optimistic/in-flight state is cleared or rolled back
   - Confirm page does not require manual refresh to recover

# Risks and follow-ups
- **Endpoint mismatch risk:** Backend endpoints may not yet expose exactly the data needed for status/history/action availability. If so, make the smallest contract adjustment necessary and keep it sanitized.
- **Permission ambiguity:** Required permission names may not be obvious. Reuse existing finance/admin policy definitions rather than inventing new ones unless clearly missing.
- **Connect/reconnect flow uncertainty:** If Fortnox connect requires redirect-based OAuth, confirm expected UX before coding. Handle redirect URL responses carefully.
- **Unsafe backend messages:** If backend currently returns raw provider/internal errors, sanitize in web mapping and note a backend follow-up to return safe summaries by contract.
- **State race conditions:** Manual sync and refresh may overlap. Centralize refresh logic and guard against duplicate in-flight actions.
- **Testing limitations:** If there is no existing Blazor component test setup, prioritize unit tests for mapping/sanitization and add a follow-up for richer UI interaction tests.
- **Follow-up suggestion:** If not already present, add a dedicated shared formatter/helper for integration status labels and safe error summaries so future accounting integrations follow the same pattern.