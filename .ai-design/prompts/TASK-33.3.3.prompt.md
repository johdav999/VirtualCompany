# Goal
Implement TASK-33.3.3 for the Finance settings UI so the Fortnox connection management experience is permission-aware, follows `docs/design.md`, and uses safe plain-English messaging throughout.

This task must ensure:
- the Finance settings page renders current Fortnox connection status and sync information from backend APIs,
- only authorized tenant users can see or trigger Fortnox actions,
- all action flows (`connect`, `reconnect`, `manual sync`, `disconnect`) handle loading/success/failure inline without page refresh,
- sync history is human-readable and safe,
- the UI never exposes internal enum names, raw provider payloads, token values, or other implementation details.

# Scope
In scope:
- Review `docs/design.md` and align the Finance settings page structure, labels, states, and action presentation to it.
- Update Blazor UI/components/pages for the Finance settings Fortnox section.
- Add or refine frontend view models / DTO mapping needed to convert backend data into safe display text.
- Apply tenant permission-aware rendering so unauthorized users cannot see or invoke Fortnox actions.
- Ensure action buttons and result messaging are driven only by backend APIs.
- Render sync history with:
  - timestamp
  - status
  - imported entity counts
  - duration
  - safe plain-English error summaries
- Add loading, success, and failure states for connect, reconnect, sync, and disconnect without full page reload.
- Add/adjust tests for rendering, permission gating, and safe messaging.

Out of scope unless required to complete acceptance criteria:
- New Fortnox backend business workflows beyond what existing APIs already support.
- Any direct provider SDK/browser-side integration.
- Displaying raw diagnostic payloads, stack traces, token metadata, or internal enum values.
- Mobile app changes unless the web project shares components that must be updated.

# Files to touch
Inspect and update only the files needed after confirming actual project structure. Likely areas:

- `docs/design.md`
- `src/VirtualCompany.Web/**`
  - Finance settings page(s)
  - Shared settings components
  - Authorization/permission-aware UI helpers
  - API client/service classes used by the page
  - View models / display mappers / formatting helpers
- `src/VirtualCompany.Shared/**`
  - Shared DTOs or contracts if the web app consumes shared response models
- `src/VirtualCompany.Api/**`
  - Only if a small contract-safe response adjustment is required for UI consumption and still uses backend APIs only
- `src/VirtualCompany.Application/**`
  - Only if a query/DTO mapping change is required to provide safe display-ready fields
- `tests/VirtualCompany.Api.Tests/**`
  - If API contract tests need updates
- Web/UI test projects if present in solution
- Any existing test files covering Finance settings, Fortnox integration UI, or authorization rendering

Before editing, locate:
- the Finance settings route/page,
- Fortnox-related API client/service usage,
- tenant permission model and authorization helpers,
- any existing enum-to-display-text mapping utilities,
- any sync history DTOs and formatting code.

# Implementation plan
1. Review current implementation and design contract
   - Open `docs/design.md` and extract the exact intended Finance settings layout, labels, sections, and action behavior.
   - Find the current Finance settings page and identify:
     - how Fortnox status is loaded,
     - how actions are triggered,
     - whether page refresh is currently required,
     - where raw backend values may leak into UI,
     - how permissions are currently checked.

2. Identify the backend contract already available
   - Confirm the existing backend APIs for:
     - current connection status,
     - connect,
     - reconnect,
     - manual sync,
     - disconnect,
     - sync history.
   - Do not introduce browser-side provider logic.
   - If backend responses contain internal enums or raw payload fields, keep them internal and map them to safe UI text before rendering.

3. Implement safe display mapping
   - Add or refine a dedicated mapping layer in the web app for Fortnox UI display.
   - Convert backend/internal values into plain-English labels, for example:
     - connection status text,
     - sync status text,
     - action result messages,
     - failure summaries.
   - Ensure the UI never renders:
     - enum identifiers,
     - raw JSON/provider payloads,
     - token values,
     - stack traces,
     - internal exception messages not intended for users.
   - Prefer centralized mapping helpers over ad hoc string handling in Razor markup.

4. Apply permission-aware rendering
   - Determine the required tenant permission(s) for Fortnox connection management.
   - Gate action visibility and enabled state based on those permissions.
   - Unauthorized users must not see actionable controls for connect/reconnect/sync/disconnect.
   - If the page includes read-only status visibility for lower-permission users, ensure that matches `docs/design.md`; otherwise hide the Fortnox management section as required.
   - Do not rely on UI gating alone; continue calling secured backend APIs, but this task focuses on rendering and UX behavior.

5. Update Finance settings UI to match design
   - Render the required data points:
     - current Fortnox connection status,
     - last successful sync time,
     - last failure summary,
     - available actions.
   - Render sync history list with:
     - timestamp,
     - status,
     - imported entity counts,
     - duration,
     - safe plain-English error summary.
   - Ensure empty states are intentional and human-readable, e.g.:
     - never connected,
     - no syncs yet,
     - no failures recorded.

6. Implement action state handling without page refresh
   - For each action (`connect`, `reconnect`, `manual sync`, `disconnect`):
     - show loading state while request is in flight,
     - disable duplicate submissions,
     - show success/failure feedback inline,
     - refresh the displayed status/history data after completion without full page reload.
   - Use component state updates and API re-fetching as needed.
   - Preserve a responsive UX if one action is running; avoid inconsistent button states.

7. Handle connect/reconnect flow carefully
   - If connect/reconnect returns a redirect URL or backend-driven next step, present it in the intended UX from `docs/design.md`.
   - Keep all initiation through backend APIs only.
   - Do not expose provider internals in the UI.

8. Improve failure messaging
   - Replace technical/provider-specific error text with safe summaries.
   - If backend already returns safe summaries, use them.
   - If not, map known failure categories to user-friendly text and fall back to a generic safe message.
   - Example style:
     - “The last sync could not complete because Fortnox access needs to be reconnected.”
     - not raw exception/provider response text.

9. Add or update tests
   - Add tests for:
     - authorized user sees allowed actions,
     - unauthorized user does not see action controls,
     - sync history renders safe display fields,
     - internal enum/raw payload values are not rendered,
     - action loading/success/failure states update without full page refresh.
   - If component tests are not available, add the closest practical coverage in the existing test style used by the repo.

10. Keep implementation aligned with architecture
   - Respect modular boundaries:
     - UI concerns in Web,
     - application/query shaping in Application if needed,
     - API contracts in Api/Shared only when necessary.
   - Avoid embedding business rules directly in Razor when a service/mapper/helper is more appropriate.
   - Keep tenant-scoped authorization and backend API usage intact.

# Validation steps
1. Build and test
   - Run:
     - `dotnet build`
     - `dotnet test`

2. Manual verification in the web app
   - Navigate to the Finance settings page.
   - Confirm the page follows `docs/design.md`.
   - Verify it displays:
     - current Fortnox connection status,
     - last successful sync time,
     - last failure summary,
     - available actions.
   - Verify sync history shows:
     - timestamp,
     - status,
     - imported entity counts,
     - duration,
     - safe plain-English error summaries.

3. Permission verification
   - Test with a user/company membership that has required tenant permissions:
     - connect visible and usable when disconnected,
     - reconnect visible and usable when connection needs renewal,
     - manual sync visible and usable when connected,
     - disconnect visible and usable when connected.
   - Test with a user lacking required permissions:
     - Fortnox action controls are not visible,
     - no action can be triggered from the UI.

4. State handling verification
   - Trigger each action and confirm:
     - loading indicator appears,
     - duplicate clicks are prevented,
     - success/failure feedback appears inline,
     - displayed status/history updates without full page refresh.

5. Safe rendering verification
   - Inspect the UI and any rendered messages to confirm it does not display:
     - internal enum names,
     - raw provider payloads,
     - token values,
     - stack traces,
     - unsafe exception text.
   - If practical, add/assert this in tests using representative sample data.

# Risks and follow-ups
- `docs/design.md` may specify behavior that differs from the current API contract; if so, prefer safe UI mapping first and make only minimal contract changes needed.
- Permission names/policies may not yet be consistently modeled in the web layer; you may need to reuse existing authorization helpers rather than invent new ones.
- Backend responses may currently expose technical fields; if the UI cannot safely map them, a follow-up backend task may be needed to provide explicit user-facing summary fields.
- Connect/reconnect flows may involve redirect semantics that need careful handling in Blazor; keep the initiation backend-driven and avoid leaking provider details.
- If there is no existing component/UI test harness, add the smallest maintainable test coverage possible and note any gaps.
- If acceptance criteria reveal missing backend support for safe failure summaries or sync history fields, document that clearly as a follow-up rather than exposing unsafe data.