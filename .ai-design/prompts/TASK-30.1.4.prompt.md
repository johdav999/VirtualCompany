# Goal
Implement **TASK-30.1.4: Add finance workspace UI for mailbox connection status and manual scan trigger** for story **US-30.1 Implement tenant-scoped mailbox connection and manual bill scan initiation**.

Deliver a Blazor web UI in the finance workspace that lets an authenticated tenant user:
- view mailbox connection status for their current tenant/user
- connect either **Gmail** or **Microsoft 365** via OAuth
- see provider and connection state
- trigger a manual **Scan inbox for bills** action
- view recent scan/audit status at a basic level if backend data is available

The implementation must align with the acceptance criteria and existing architecture:
- tenant-scoped and user-scoped mailbox connection handling
- no direct payment or approval proposal creation from connect/scan actions
- UI should call backend APIs/application services rather than embedding business logic in the page

# Scope
In scope:
- Finance workspace UI/page/component(s) for mailbox integration status
- Connect buttons/actions for Gmail and Microsoft 365
- Disconnect/reconnect affordance only if already supported by backend; otherwise status-only plus connect
- Manual scan trigger button and UX states
- Display of current connection status and provider
- Display of last scan summary if an endpoint/model already exists or is added as part of this task
- Tenant-aware authorization and current-user scoping in UI/API calls
- Basic success/error/loading states
- Tests for UI-facing application/API behavior where practical

Out of scope:
- Implementing full OAuth provider plumbing if not already present, except minimal wiring needed for UI integration
- Deep mailbox ingestion engine logic beyond what is needed to trigger existing command flow
- Creating payment actions, approval proposals, or downstream bill-processing actions
- Mobile UI
- Broad redesign of finance workspace navigation

Acceptance criteria to preserve explicitly:
- Users can connect either Gmail or Microsoft 365 mailboxes via OAuth with provider-specific minimal read scopes required for message and attachment retrieval.
- Mailbox connections are stored per tenant and per user in a MailboxConnection record with encrypted tokens and connection status.
- A user can trigger a manual "Scan inbox for bills" action that scans only the last 30 days of messages.
- The scan only evaluates configured folders/labels and bill-related keywords including invoice, bill, faktura, payment due, amount due, OCR, IBAN, bankgiro, and plusgiro.
- The system persists an EmailIngestionRun audit record containing start time, end time, provider, scanned message count, detected candidate count, and failure details.
- No payment action or approval proposal is created directly from mailbox connection or scan initiation alone.

# Files to touch
Inspect the solution first and update the exact files that fit the existing structure. Likely areas:

- `src/VirtualCompany.Web/**`
  - finance workspace pages/components
  - shared navigation/menu entries if needed
  - view models or client service wrappers
- `src/VirtualCompany.Api/**`
  - mailbox connection status/query endpoints
  - OAuth start/callback endpoints if web/API owns them
  - manual scan trigger endpoint
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for:
    - get mailbox connection status
    - start mailbox OAuth flow
    - trigger manual inbox scan
    - get recent ingestion run summary
- `src/VirtualCompany.Domain/**`
  - mailbox connection / ingestion run enums or DTO-facing domain types if missing
- `src/VirtualCompany.Infrastructure/**`
  - repository/query implementations
  - OAuth adapter wiring if needed
- `src/VirtualCompany.Shared/**`
  - contracts/DTOs shared between API and Web if this project is used that way
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/application tests for status and manual scan trigger
- potentially `README.md` or relevant docs if a small setup note is warranted

Before editing, locate:
- existing finance workspace route/page
- tenant context resolution pattern
- current API style and CQRS conventions
- any existing mailbox/integration models from prior tasks
- any existing audit/ingestion run entities and endpoints

# Implementation plan
1. **Discover existing mailbox/integration implementation**
   - Search for:
     - `MailboxConnection`
     - `EmailIngestionRun`
     - Gmail / Microsoft / OAuth integration code
     - finance workspace pages
     - tenant/user context helpers
   - Determine whether backend support already exists for:
     - current connection status query
     - OAuth initiation/callback
     - manual scan command
     - recent ingestion run query
   - Reuse existing patterns and naming; do not invent parallel architecture.

2. **Define the UI contract**
   - Ensure there is a clean query/response model for the finance mailbox panel/page, ideally including:
     - `isConnected`
     - `provider`
     - `connectionStatus`
     - `connectedAt` if available
     - `lastSuccessfulScanAt` if available
     - `lastRun` summary if available:
       - start time
       - end time
       - provider
       - scanned message count
       - detected candidate count
       - failure details
   - Ensure there is a command contract for manual scan trigger returning:
     - accepted/started state
     - run id if available
     - user-safe message

3. **Add or complete backend query/command endpoints**
   - Add tenant-scoped, authenticated endpoints for:
     - get current user mailbox connection status in current tenant
     - trigger manual scan for current user/current tenant
     - optionally get recent ingestion runs for current user/current tenant if needed by UI
   - Enforce that all operations are scoped by tenant and current user.
   - Manual scan endpoint must only initiate the scan flow; it must not create payment or approval artifacts.

4. **Wire OAuth connect actions into UI**
   - Add provider-specific connect buttons:
     - Connect Gmail
     - Connect Microsoft 365
   - If the app uses redirect-based OAuth:
     - button should navigate to backend OAuth initiation endpoint
   - If the app uses API-generated auth URLs:
     - fetch URL and redirect safely
   - Show current provider and status if already connected.
   - If both providers are not allowed simultaneously by domain rules, reflect that clearly in UI.

5. **Build finance workspace mailbox status UI**
   - Add a focused card/section/page in finance workspace with:
     - title and short explanatory text
     - current connection status badge
     - connected provider
     - connect/reconnect actions
     - manual scan button
     - loading and disabled states
     - success/error feedback
   - Keep UX simple and operational.
   - Do not expose internal tokens, raw failure traces, or chain-of-thought-like details.

6. **Add manual scan UX behavior**
   - Button label: **Scan inbox for bills**
   - On click:
     - call manual scan endpoint/command
     - disable while request is in flight
     - show confirmation that scan started or completed depending on backend behavior
   - If no mailbox is connected:
     - disable or block action with clear guidance
   - If backend returns last run info after trigger, refresh status panel.

7. **Show recent scan/audit summary**
   - If available, display the latest ingestion run summary:
     - started
     - ended
     - provider
     - scanned messages
     - detected candidates
     - failure details if any
   - Keep failure details concise and user-safe.
   - This is a user-facing operational summary, not raw logs.

8. **Preserve architecture boundaries**
   - UI should call typed services/endpoints.
   - Controllers/endpoints should delegate to application layer.
   - Application layer should use repositories/services and tenant-aware authorization patterns.
   - Keep business rules out of Razor components.

9. **Testing**
   - Add/extend tests for:
     - tenant/user-scoped status retrieval
     - manual scan trigger endpoint behavior
     - no mailbox connected => expected validation/error response
     - UI/API contract serialization if relevant
     - guarantee that scan initiation does not create payment/approval side effects, if testable at current layer
   - Prefer existing test patterns in the repo.

10. **Polish**
   - Ensure route/menu placement in finance workspace is discoverable.
   - Ensure empty states:
     - no connection yet
     - connected but no scans yet
     - failed last scan
   - Keep copy concise and business-friendly.

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Open finance workspace as an authenticated user with tenant context
   - Confirm mailbox section/page renders
   - Confirm disconnected state shows Gmail and Microsoft 365 connect actions
   - Confirm connected state shows provider and status
   - Confirm manual scan button is disabled or guarded when no connection exists
   - Confirm manual scan button triggers backend command when connected
   - Confirm latest scan summary updates or is retrievable after trigger

4. API verification:
   - Verify status endpoint returns only current tenant/current user mailbox connection
   - Verify manual scan endpoint requires auth and tenant context
   - Verify endpoint does not expose secrets/tokens
   - Verify no payment action or approval proposal is created by connect/scan initiation flow

5. Data/audit verification if local environment supports it:
   - Confirm `MailboxConnection` is used per tenant and per user
   - Confirm `EmailIngestionRun` is persisted with expected fields
   - Confirm scan initiation is limited to the intended command path and not broader side effects

# Risks and follow-ups
- **Backend dependencies may be incomplete**: if OAuth initiation/status/scan endpoints do not yet exist, this task may require small backend additions despite being UI-focused.
- **Route/UX placement ambiguity**: finance workspace structure may not yet have a dedicated mailbox integration area; choose the smallest consistent addition.
- **Async scan behavior**: if scan runs in background, UI may only be able to show “started” plus latest known run; avoid implying synchronous completion.
- **Provider capability mismatch**: Gmail and Microsoft 365 may have different callback or scope handling already in progress; reuse existing provider abstractions.
- **Authorization gaps**: ensure tenant and current-user scoping is enforced everywhere, especially for status and run history.
- **Do not overreach**: do not add payment proposal, approval creation, or invoice execution flows from this task.

Follow-up suggestions if not already covered elsewhere:
- add disconnect/revoke mailbox action
- add scan history list beyond latest run
- add folder/label configuration UI
- add clearer OAuth callback success/failure UX
- add polling or refresh after manual scan start if background execution is long-running