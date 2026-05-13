# Goal
Implement backlog task **TASK-33.3.1** for story **US-33.3 Deliver Finance settings UI for Fortnox connection management and sync review** by adding a **Finance settings Fortnox section** in the Blazor web app that:

- Shows current Fortnox connection status
- Shows last successful sync time
- Shows last failure summary
- Shows available actions: connect, reconnect, manual sync, disconnect
- Shows sync history with timestamp, status, imported entity counts, duration, and safe error summaries
- Enforces tenant permission-based visibility and action availability
- Uses **backend APIs only**
- Handles loading, success, and failure states **without page refresh**
- Never exposes internal enums, raw provider payloads, or token values

Follow existing architecture and coding conventions in the repo. Prefer minimal, cohesive changes over broad refactors.

# Scope
In scope:

- Update the Finance settings page in `VirtualCompany.Web`
- Add/extend UI components for:
  - Fortnox connection status summary
  - Action controls
  - Sync history list/table
- Add/extend web-facing service/client models needed to call existing backend APIs
- Add permission-aware rendering and action enablement
- Add safe UI mapping from backend/domain values to plain-English labels/messages
- Add loading/success/error UX for connect, reconnect, sync, and disconnect actions
- Add/extend tests for component rendering and permission behavior where practical

Out of scope unless strictly required to complete the UI:

- New backend business logic
- New persistence schema
- Provider token handling changes
- Displaying raw integration payloads
- Mobile app changes
- Broad redesign outside `docs/design.md`

If backend endpoints or contracts are missing, first inspect whether equivalent APIs already exist. Only add the thinnest necessary API surface if absolutely required, and keep it aligned with the story and acceptance criteria.

# Files to touch
Inspect first, then update only the relevant files. Likely areas:

- `src/VirtualCompany.Web/**`
  - Finance settings page/component
  - Shared settings components
  - Web API client/service classes
  - View models / DTOs used by the UI
  - Authorization/permission helpers
  - Styling files if the page uses scoped CSS
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/constants if the solution already centralizes API contracts here
- `src/VirtualCompany.Api/**`
  - Only if a thin endpoint/contract adjustment is required for the UI and no existing endpoint supports the acceptance criteria
- `tests/VirtualCompany.Api.Tests/**`
  - Only if API contract changes are necessary
- Any web test project if present for Blazor/UI tests

Before editing, locate:
- Finance settings route/page
- Existing Fortnox/integration-related DTOs, endpoints, and services
- Existing permission model for tenant/company admin actions
- Existing design patterns for async action buttons, alerts, and tables/lists
- `docs/design.md`

# Implementation plan
1. **Discover existing implementation surface**
   - Read `docs/design.md` and align layout/components with it.
   - Search for:
     - Finance settings page
     - Fortnox integration code
     - Integration settings components
     - Permission checks for tenant/company admin
     - Existing API clients in web/shared layers
   - Identify existing backend endpoints for:
     - Get Fortnox connection/settings status
     - Start connect
     - Reconnect
     - Manual sync
     - Disconnect
     - Fetch sync history

2. **Define/confirm UI data contract**
   - Ensure the UI can obtain, at minimum:
     - Connection status
     - Last successful sync timestamp
     - Last failure summary
     - Available actions
     - Sync history entries with timestamp, status, imported counts, duration, safe error summary
     - Permission flags or enough auth context to derive visibility
   - If backend returns internal enums or unsafe/raw fields, add a UI mapping layer so the page only renders safe, human-readable values.
   - Do not bind raw provider payloads or token-related fields into any component.

3. **Implement Fortnox settings section**
   - Add a dedicated section/component on the Finance settings page for Fortnox.
   - Include:
     - Status card/summary
     - Last successful sync
     - Last failure summary
     - Action controls
     - Sync history list/table
   - Keep the component composable if the page already uses section components.

4. **Implement permission-aware rendering**
   - Hide action controls entirely for users lacking required tenant permissions.
   - Ensure unauthorized users also cannot trigger actions through bound events.
   - If the page should still show read-only status/history for some users, preserve that behavior only if consistent with existing permission patterns and acceptance criteria.
   - Company admin should be able to perform all required actions.

5. **Implement action flows**
   - Wire buttons/actions to backend APIs only:
     - Connect
     - Reconnect
     - Manual sync
     - Disconnect
   - For each action:
     - Show in-progress state
     - Disable duplicate submissions while pending
     - Show success/failure feedback inline
     - Refresh the section data after completion without full page reload
   - For connect/reconnect:
     - If backend returns a redirect URL or launch instruction, use the established app pattern for external auth handoff.
   - For disconnect:
     - Confirm destructive action if the app has an existing confirmation pattern.

6. **Implement sync history presentation**
   - Render a list/table with:
     - Timestamp
     - Status as plain English
     - Imported entity counts in readable labels
     - Duration in readable format
     - Safe error summary
   - Never display:
     - Internal enum names
     - Raw JSON/provider payloads
     - Token values
     - Stack traces or internal exception text
   - Add empty state and loading state.

7. **Handle state transitions without refresh**
   - After any successful action, re-query the Fortnox section data and update the UI reactively.
   - Handle failure states gracefully with safe user-facing messages.
   - Ensure stale action state is cleared appropriately between operations.

8. **Testing**
   - Add/update tests for:
     - Authorized admin sees action controls
     - Unauthorized user does not see action controls
     - Safe rendering of status/history labels
     - Loading and failure states for action execution
   - If UI tests are not available, add focused service/component tests where the repo patterns allow.

9. **Polish**
   - Keep naming user-friendly and finance-domain appropriate.
   - Reuse existing design system components and alert patterns.
   - Avoid introducing provider-specific leakage into generic shared UI where not needed.

# Validation steps
1. Read and verify alignment with `docs/design.md`.
2. Build solution:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Manually verify in the web app:
   - Finance settings page loads Fortnox section
   - Connected/disconnected states render correctly
   - Last successful sync and last failure summary display safely
   - Sync history renders expected columns/fields
   - No internal enum names or raw payloads appear anywhere
   - Admin can trigger connect, reconnect, manual sync, disconnect
   - Non-permitted user cannot see or execute actions
   - Loading/success/failure states work without page refresh
5. If API changes were required, verify:
   - Endpoints remain tenant-scoped
   - Unauthorized requests are rejected server-side
   - No token values/raw provider payloads are serialized to the UI contract

# Risks and follow-ups
- **Missing backend endpoints/contracts**: If the backend does not yet expose the required Fortnox settings/status/history shape, add only the smallest compatible API extension and document it in the PR.
- **Permission ambiguity**: Reuse existing tenant/company admin authorization rules rather than inventing new ones. If permission names are unclear, inspect current policy usage before implementing.
- **Unsafe backend data exposure**: Backend DTOs may include internal enums or provider details. Add explicit mapping/sanitization in the web layer or API contract.
- **Connect/reconnect flow complexity**: OAuth/external auth initiation may require redirect handling. Follow existing integration auth patterns in the repo.
- **State consistency after actions**: Manual sync/disconnect may be asynchronous. If backend returns accepted/pending semantics, reflect that clearly and refresh status/history accordingly.
- **Follow-up opportunity**: If the implementation reveals repeated integration-settings patterns, consider a later refactor into reusable integration status/action/history components, but do not block this task on that refactor.