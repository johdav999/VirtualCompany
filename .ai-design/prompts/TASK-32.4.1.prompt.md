# Goal
Implement backlog task **TASK-32.4.1** for **US-32.4 Fortnox integration UI, approval-gated writes, and production readiness** in the existing .NET solution.

Deliver a production-ready Fortnox integration experience that includes:

- Finance Settings → Integrations Fortnox card UI
- Connection state handling and actions
- Sync history views
- Reconnect and disconnect flows
- Source labels for Fortnox-backed records
- Approval-gated Fortnox write operations
- Audit events for approved writes
- Guardrails preventing simulation overwrites of Fortnox-linked records
- Unit and opt-in integration test coverage

Follow the existing architecture: modular monolith, tenant-scoped application services, Blazor Web App frontend, ASP.NET Core backend, PostgreSQL persistence, and audit/approval domain patterns already present in the codebase.

# Scope
In scope:

1. **Fortnox integration settings UI**
   - Add/update Finance Settings → Integrations page to show a Fortnox card.
   - Support these states:
     - Not connected
     - Connecting
     - Connected
     - Syncing
     - Needs reconnect
     - Error
   - Support these actions:
     - Connect Fortnox
     - Sync now
     - Reconnect
     - Disconnect
     - View sync history

2. **Design reference generation**
   - If the current UI does not already support this cleanly, create/refactor UI and generate a reference screenshot using the OpenAI image.2 API.
   - Save the generated reference at:
     - `/docs/design/references/fortnox-integration-settings.png`
   - Ensure implemented UI follows:
     - `/docs/style.md`
     - `/docs/design.md`

3. **Approval-gated Fortnox writes**
   - Ensure all MVP Fortnox write operations create an approval item before any external API call.
   - Approval details must display:
     - target company
     - payload summary
   - External write executes only after explicit user approval.

4. **Audit events for approved writes**
   - On approved write execution, create audit events containing:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
   - Exclude:
     - access tokens
     - refresh tokens
     - client secrets
     - any sensitive Fortnox secrets

5. **Fortnox source labels and overwrite protection**
   - Clearly indicate records sourced from Fortnox in relevant UI surfaces.
   - Prevent simulation actions from overwriting Fortnox-linked records.

6. **Tests**
   - Unit tests for:
     - OAuth URL generation
     - token refresh
     - encryption
     - DTO mapping
     - duplicate prevention
     - sync cursor updates
     - approval creation
   - Opt-in integration tests may call real Fortnox API only when explicit environment variables are present.

Out of scope unless required to satisfy acceptance criteria:

- Broad redesign of unrelated settings pages
- Full mobile companion implementation
- New generic workflow builder
- Non-Fortnox integrations
- Large refactors outside the Fortnox, approval, audit, and affected record surfaces

# Files to touch
Inspect the repo first and then update the exact files that match existing patterns. Likely areas:

## Web / Blazor UI
- `src/VirtualCompany.Web/**`
  - Finance settings pages/components
  - Integrations settings page
  - Approval detail/list views if Fortnox write approvals need richer display
  - Shared badges/components for source labels
  - Record detail/list pages where Fortnox-backed records appear

## API
- `src/VirtualCompany.Api/**`
  - Controllers/endpoints for:
    - Fortnox connection status
    - OAuth connect/reconnect/disconnect
    - sync now
    - sync history
    - approval-triggered write execution callbacks or commands

## Application layer
- `src/VirtualCompany.Application/**`
  - Fortnox commands/queries
  - Approval creation handlers
  - Sync history queries
  - DTO mapping
  - duplicate prevention logic
  - sync cursor update logic
  - audit event creation services/handlers
  - simulation guardrails for Fortnox-linked records

## Domain layer
- `src/VirtualCompany.Domain/**`
  - Integration entities/value objects/enums
  - approval/audit domain models if extension is needed
  - source metadata for Fortnox-linked records
  - write direction/entity type metadata

## Infrastructure
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox API client
  - OAuth URL generation
  - token refresh
  - secret encryption/decryption
  - persistence for integration connections, sync runs/history, cursors
  - opt-in real API integration test support

## Shared contracts
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/enums for integration state, sync history, source labels if applicable

## Tests
- `tests/**`
  - Add or extend unit tests for all required acceptance criteria
  - Add opt-in integration tests guarded by explicit environment variables

## Docs / design assets
- `docs/design/references/fortnox-integration-settings.png`
- Potentially update:
  - `README.md`
  - integration-specific docs if present

Do not invent new top-level folders if equivalent project locations already exist.

# Implementation plan
1. **Discover existing Fortnox and integration patterns**
   - Search the solution for:
     - `Fortnox`
     - `Integration`
     - `Approval`
     - `Audit`
     - `Sync`
     - `Finance Settings`
   - Identify:
     - existing integration entities and status enums
     - current settings UI entry points
     - approval workflow patterns
     - audit event creation patterns
     - record models that can be Fortnox-backed
     - simulation flows that mutate those records

2. **Model Fortnox connection and sync states**
   - Introduce or extend a typed status model for the UI/backend to represent:
     - NotConnected
     - Connecting
     - Connected
     - Syncing
     - NeedsReconnect
     - Error
   - Ensure state derivation is deterministic from persisted connection/token/sync/job/error data.
   - Avoid stringly-typed status handling where possible.

3. **Implement Finance Settings → Integrations Fortnox card**
   - Add/update the integrations settings page in Blazor.
   - Render a Fortnox card with:
     - provider name
     - current state badge
     - concise status text
     - last sync info if available
     - action buttons based on state
   - Action visibility rules:
     - Not connected → Connect Fortnox
     - Connecting → disabled/in-progress state
     - Connected → Sync now, Disconnect, View sync history
     - Syncing → disabled Sync now or loading state, View sync history, Disconnect if safe
     - Needs reconnect → Reconnect, View sync history, Disconnect
     - Error → Reconnect or Connect depending on cause, View sync history, Disconnect if applicable
   - Keep styling aligned with `/docs/style.md` and `/docs/design.md`.

4. **Generate design reference if UI is new/refactored**
   - If the UI is materially new or significantly refactored, generate a reference screenshot using the OpenAI image.2 API.
   - Save it exactly to:
     - `/docs/design/references/fortnox-integration-settings.png`
   - If generation requires a script/tool, add a minimal repeatable script or note consistent with repo conventions.
   - Do not block implementation if the API is unavailable; document the exact follow-up in the final notes, but still implement the UI.

5. **Implement connect/reconnect/disconnect/sync/history flows**
   - Backend:
     - query current Fortnox connection status
     - generate OAuth connect URL
     - support reconnect flow
     - disconnect safely by revoking/clearing stored credentials and updating state
     - trigger sync now command
     - expose sync history query
   - UI:
     - wire buttons to commands/endpoints
     - show loading/error/success feedback
     - show sync history in a modal, drawer, or dedicated page depending on existing UX patterns

6. **Approval-gate all Fortnox write operations**
   - Identify every MVP Fortnox write path.
   - Refactor so that no external Fortnox write call happens directly from user action or automation.
   - Instead:
     - create an approval request first
     - persist enough metadata to display:
       - target company
       - payload summary
       - entity type / intended action
     - mark the action pending approval
   - Only after explicit approval:
     - execute the external Fortnox API call
   - Ensure rejected/expired/cancelled approvals do not execute writes.

7. **Create audit events for approved writes**
   - On successful approval-triggered execution, create business audit events with:
     - approver identity
     - entity type
     - direction
     - summary
     - payload hash
   - Ensure audit payloads are sanitized:
     - never store tokens or secrets
     - never include raw Fortnox credentials in summaries or metadata
   - Reuse existing audit infrastructure rather than ad hoc logging.

8. **Add source labels for Fortnox-backed records**
   - Extend affected record DTOs/view models with source metadata if not already present.
   - Show a clear source badge/label such as “Fortnox” or “Synced from Fortnox” in relevant list/detail views.
   - Keep the label visually consistent with existing badge patterns.

9. **Prevent simulation overwrites of Fortnox-linked records**
   - Find simulation actions that can mutate records also backed by Fortnox.
   - Add application-layer guardrails so simulation cannot overwrite Fortnox-linked records.
   - Surface a clear user-facing explanation in the UI/API response.
   - Prefer enforcing this in the application/domain layer, not only in the UI.

10. **Implement/extend sync history**
    - Persist or expose sync run history with fields such as:
      - started/finished timestamps
      - status
      - counts
      - cursor or checkpoint summary
      - error summary if failed
    - Show this in the UI via View sync history.
    - Keep tenant scoping enforced.

11. **Add unit tests**
    - Cover:
      - OAuth URL generation
      - token refresh
      - encryption/decryption or encryption service behavior
      - DTO mapping
      - duplicate prevention
      - sync cursor updates
      - approval creation before write execution
    - Add tests for:
      - no external write before approval
      - audit event sanitization
      - simulation overwrite prevention
      - state mapping for UI if practical

12. **Add opt-in real Fortnox integration tests**
    - Add integration tests that only run when explicit environment variables are set.
    - Guard them clearly, e.g. skip unless all required env vars exist.
    - Never require real credentials for default local/CI test runs.
    - Document required env vars in test comments or README if repo conventions allow.

13. **Validate end-to-end behavior**
    - Confirm:
      - settings card renders all states
      - actions work
      - sync history is visible
      - approvals are created before writes
      - approved writes create sanitized audit events
      - Fortnox source labels appear
      - simulation cannot overwrite Fortnox-linked records

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Verify UI behavior manually in the web app:
   - Navigate to Finance Settings → Integrations.
   - Confirm Fortnox card exists.
   - Confirm each supported state can be rendered or simulated:
     - Not connected
     - Connecting
     - Connected
     - Syncing
     - Needs reconnect
     - Error

3. Verify action availability by state:
   - Connect Fortnox
   - Sync now
   - Reconnect
   - Disconnect
   - View sync history

4. Verify approval gating:
   - Trigger an MVP Fortnox write operation.
   - Confirm an approval item is created before any external API call.
   - Confirm approval UI shows target company and payload summary.
   - Confirm no write occurs until explicit approval.
   - Confirm rejected/expired approvals do not execute.

5. Verify audit behavior:
   - Approve a Fortnox write.
   - Confirm an audit event is created with:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
   - Confirm tokens/secrets are absent from audit data.

6. Verify source labels and overwrite protection:
   - Open relevant Fortnox-backed records.
   - Confirm source label is visible.
   - Attempt simulation overwrite.
   - Confirm overwrite is blocked with a clear message.

7. Verify sync history:
   - Run or simulate sync.
   - Open View sync history.
   - Confirm history entries show status/timestamps and useful summaries.

8. Verify tests added for required areas:
   - OAuth URL generation
   - token refresh
   - encryption
   - DTO mapping
   - duplicate prevention
   - sync cursor updates
   - approval creation

9. Verify opt-in integration tests:
   - Ensure they are skipped by default.
   - If env vars are available, run them explicitly and confirm they can call the real Fortnox API.

10. Verify design asset:
   - If UI was new/refactored, confirm file exists:
     - `/docs/design/references/fortnox-integration-settings.png`

# Risks and follow-ups
- **Unknown existing Fortnox implementation state**
  - The repo may already contain partial Fortnox support. Reuse and extend rather than duplicating logic.

- **Approval flow coupling**
  - Existing write paths may call infrastructure clients directly. Refactor carefully so approval creation happens in the application layer before any side effect.

- **Audit data leakage**
  - Be strict about sanitization. Never serialize token-bearing DTOs directly into audit events.

- **UI state derivation drift**
  - If connection state is inferred in multiple places, centralize mapping to avoid inconsistent badges/actions.

- **Simulation guardrail gaps**
  - UI-only disabling is insufficient. Enforce overwrite prevention server-side