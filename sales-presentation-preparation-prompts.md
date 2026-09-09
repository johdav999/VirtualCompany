# Sales presentation preparation implementation prompts

## Purpose

Implement a complete in-product workflow for preparing a Sales meeting presentation without using Swagger or manual PowerShell commands. An authorized user should be able to start from a scheduled Sales meeting invitation, configure the meeting session, choose Alex or another eligible Sales agent, upload a PowerPoint deck, follow processing status, activate the deck, and open the private browser-diagnostic presenter controls.

## Shared instructions for every prompt

- Follow `/production-implementation.md`.
- Follow `/docs/architecture-rules.md` for application boundaries, tenant isolation, authorization, persistence, workflow, audit, observability, background processing, and external integrations.
- Follow `/docs/design.md` for every UI change. The mandatory design and evidence workflow applies to these prompts.
- Before changing files under `/src` or `/tests`, read the applicable `AGENTS.md` files on those paths.
- Preserve the modular-monolith boundaries. Domain and Application must not depend on API or Web. Keep provider details inside Infrastructure.Sales.
- Reuse the existing meeting invitation, meeting session, deck processing, presentation runtime, typed API client, authorization, audit, and problem-response implementations. Do not create a parallel presentation-preparation subsystem.
- Every tenant-owned read and mutation must be company-scoped and authorized on the server. A route or header company ID is context, not proof of access.
- Keep the existing requirement that a new meeting session may be created only for a scheduled invitation with a provider event. Do not weaken that production invariant to make local testing easier.
- Do not send, reschedule, or cancel a calendar invitation as an implicit side effect of presentation preparation.
- Use stable wire values already owned by the domain, including `not_requested`, `standard`, and the existing presentation/deck status values. Standard retention is 365 days.
- UI polling must be bounded, cancellable, and stopped when the component is disposed or the deck reaches a terminal state.
- Do not expose provider payloads, access tokens, stage tokens, internal storage keys, sensitive attendee data, or exception details in the UI, logs, or telemetry.
- Add no database migration unless the implementation genuinely changes persistent state. If persistence changes, follow the SQL Server migration and snapshot rules in `/docs/architecture-rules.md`.
- Each prompt must leave the solution building and its affected tests passing. Do not defer in-scope work as TODOs.

---

## Prompt 1 — Add an authoritative presentation-preparation readiness API

### 1. Title and outcome

Implement one company-scoped preparation read model that tells the Web application whether a Sales meeting can be prepared, which eligible Sales agents may present it, what session and deck state already exists, and which next actions are allowed. Users receive precise blocking guidance instead of discovering requirements through failed mutations.

### 2. Current context

- `SalesController` exposes meeting invitations under `/api/sales`.
- `SalesMeetingSessionsController` already gets and creates or updates `SalesMeetingSession` records.
- `SalesPresentationDecksController` already imports, lists, retrieves, activates, retries, and regenerates meeting presentation artifacts.
- `SalesMeetingSessionService` intentionally permits initial session creation only when the invitation status is `scheduled` and `ExternalEventId` exists.
- `AgentsController` exposes company-scoped roster data, but the presentation-preparation UI has no narrow query that identifies agents eligible to present a Sales deck.
- The Blazor UI currently has no cohesive preparation/readiness contract. It would otherwise have to reproduce invitation and deck eligibility decisions across several components.

### 3. Dependencies

None.

### 4. Implementation requirements

- Add a narrowly owned Application query contract and implementation for Sales meeting presentation preparation. Return a read model containing:
  - company, invitation, lead, and optional meeting-session IDs
  - invitation status and safe summary fields needed by the page
  - whether a provider event exists
  - whether the invitation is eligible for session creation
  - the existing meeting session, when present
  - eligible active Sales agents with ID, display name, role/template identity, and safe status
  - all decks for the session, with processing status, failure summary, retry eligibility, slide count, version, and active state
  - one authoritative readiness state for opening presenter controls
  - stable blocking reason codes and plain-English explanations
  - allowed actions such as `create_session`, `update_session`, `upload_deck`, `retry_processing`, `activate_deck`, and `open_presenter`
- Derive eligibility from existing domain/application rules. Do not duplicate the session or deck mutation rules in the controller or Web project.
- Define clearly what makes an agent eligible. At minimum, it must belong to the exact company, be active, belong to Sales or carry the existing Sales meeting/presentation capability required by the current model, and be authorized for the selected session.
- Add a company-scoped endpoint such as `GET /api/sales/meeting-invitations/{invitationId}/presentation-preparation`. Keep the controller transport-only and preserve current endpoints.
- Map known missing, ineligible, and cross-company conditions to stable problem responses without leaking whether another tenant owns an identifier.
- Add safe audit or observability only where the existing read-model conventions require it. Do not generate an audit event for passive polling unless current repository policy explicitly does so.
- Add or extend the typed Web API client with a strongly typed method for this preparation projection. Use `ICompanyApiTransport` and existing error presentation.
- Document the endpoint and its reason/action values near the owning Sales meeting documentation.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md` and the shared instructions above.
- Do not create a generic readiness engine. Keep this projection owned by the Sales meeting presentation capability.
- Do not allow the query to create sessions, activate decks, repair data, or contact a calendar provider.
- Do not return raw `StorageKey`, provider tokens, stage capabilities, or unnecessary attendee data.
- Preserve all current public routes and behavior.

### 6. Acceptance criteria

- Given a scheduled invitation with a provider event and an eligible Sales agent, when preparation is queried, then `create_session` is allowed and its prerequisites are reported as satisfied.
- Given an invitation that is draft, awaiting approval, failed, cancelled, or missing its provider event, when preparation is queried, then session creation is blocked with a stable reason and actionable explanation.
- Given an existing session with no deck, then upload is allowed while activation and presenter launch are blocked.
- Given a processed active deck, then presenter launch is allowed and the read model identifies the active deck and slide count.
- Given a processing or retryable failed deck, then the returned actions match the authoritative deck state.
- Given a company-A request for a company-B invitation, agent, session, or deck, then no cross-company details are returned.

### 7. Verification

Add focused Application/service tests for each readiness state, eligible-agent filtering, stable reason codes, and allowed actions. Add API authorization and tenant-isolation tests, including wrong-company and unauthorized-role cases. Add typed Web client tests for route, company context, cancellation, success, and problem mapping. Run the affected project builds and focused test suites.

### 8. Definition of done

The Web layer can load one authoritative, company-scoped preparation model without reconstructing backend eligibility. The endpoint, client contract, authorization, error mapping, documentation, and tests are production-ready, with no duplicate business policy or placeholder state.

---

## Prompt 2 — Build the meeting preparation workspace and session setup flow

### 1. Title and outcome

Create a Blazor meeting-preparation workspace where an authorized Sales user can review the invitation prerequisites, choose an eligible presenter, configure the meeting session, and save it without leaving the product.

### 2. Current context

- Prompt 1 provides the authoritative preparation projection and eligible Sales-agent list.
- `SalesMeetingSessionApiClient` already supports get and create/update operations.
- `CreateOrUpdateSalesMeetingSessionViewModel` contains meeting goal, intended audience, duration, demo scenario, consent status, retention policy, retention days, and optional expected version.
- Initial session creation requires a scheduled invitation with a provider event.
- Standard retention requires exactly 365 days.
- `SalesLeadDetail.razor` shows invitations and can schedule a demo, but it does not provide a session-preparation workspace.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Add a routable Blazor page owned by the Sales area, preferably invitation-oriented so it works before a session exists, for example `/app/sales/meeting-invitations/{InvitationId}/prepare` with company context handled through the established presentation context.
- Apply the mandatory UI workflow in `/docs/design.md`, including desktop and narrow-width reference capture, implementation against the approved reference, and browser verification.
- Present one calm preparation workflow rather than a dense administrative dashboard. Show:
  - invitation title, scheduled time, provider state, and lead/customer context
  - a compact readiness checklist driven entirely by Prompt 1
  - eligible Sales-agent selection with Alex selected when he is the appropriate current choice
  - meeting goal
  - intended audience
  - planned duration constrained to 5–480 minutes
  - optional demo scenario
  - consent status with safe explanatory copy
  - retention policy and retention days, enforcing 365 days when `standard` is selected
  - save state, validation, and safe failure recovery
- Use existing form, validation, localization, loading, empty-state, and problem-presentation patterns. Do not add raw JSON or expose API terminology unnecessarily.
- On initial save, call the existing session creation endpoint. On subsequent saves, include `ExpectedVersion` and handle optimistic-concurrency conflicts by reloading current state and explaining what changed.
- Preserve the chosen eligible agent in page state for Prompt 3. If the current model has no durable session-to-presenter assignment, do not add one casually. Determine whether deck ownership is the authoritative selection and keep the page state until upload, or add a narrowly justified persistent assignment with a SQL Server migration and full tenant/audit coverage.
- Disable mutation controls when readiness says the invitation is ineligible. Show the precise next action, such as approving the invitation, waiting for provider scheduling, reconnecting the calendar, or selecting an eligible agent.
- After a successful save, update the route/state without forcing the user to find or copy a meeting-session GUID.
- Add a clear next section or continuation action for uploading the presentation, implemented in Prompt 3.

### 5. Constraints and preservation rules

- Follow `/docs/design.md`, `/docs/architecture-rules.md`, and the shared instructions.
- Do not weaken the scheduled-invitation/provider-event invariant.
- Do not silently grant consent. `not_requested` must remain explicit until an authorized user or meeting workflow records another state.
- Do not let client-side validation replace server validation or authorization.
- Preserve the existing scheduling and approval flow. Session setup must not send or modify calendar events.

### 6. Acceptance criteria

- Given an eligible scheduled invitation, when an authorized user completes valid fields and saves, then a ready meeting session is created and the user remains in the preparation workspace.
- Given an existing session, when its preparation is edited with the current version, then the changes persist and the refreshed view shows the authoritative state.
- Given a stale expected version, then the page does not overwrite newer state and offers a safe reload path.
- Given standard retention, then the UI submits 365 days and the backend accepts it.
- Given an ineligible invitation, missing provider event, missing Sales agent, invalid duration, or unauthorized user, then the page blocks the action and shows an actionable message.
- Given a narrow viewport, then the workflow remains usable without horizontal scrolling, clipped actions, or hidden validation.

### 7. Verification

Add component/page tests for loading, valid creation, editing, validation, standard-retention behavior, concurrency conflict, authorization presentation, and blocked readiness states. Add API client tests as needed. Run focused Web and API tests, affected builds, and browser checks at the required viewport sizes. Save the required UI evidence under the established design-reference location.

### 8. Definition of done

An authorized user can create or update the meeting session and choose an eligible Sales presenter through the Blazor application. The flow is accessible, responsive, localized according to repository conventions, version-safe, and fully connected to the real backend.

---

## Prompt 3 — Add PowerPoint upload, processing progress, retry, and activation

### 1. Title and outcome

Complete the preparation workspace with a production PowerPoint workflow. A user can upload a `.pptx`, see background processing progress and failures, retry when allowed, review the resulting slide count and version, and activate the processed deck for the meeting.

### 2. Current context

- `SalesPresentationDecksController` already supports multipart import, list/get, slide retrieval, activate, retry, brief retrieval, and brief regeneration.
- `SalesPresentationDeckApiClient` already exposes the corresponding typed methods.
- Import accepts `File`, `AgentId`, and optional `Title` and returns an accepted deck that background processing updates.
- The controller request limit is 28,311,552 bytes. The UI currently provides no general upload, status, retry, or activation surface.
- The presentation runtime requires one active processed deck before it can issue stage access or load presenter state.

### 3. Dependencies

Prompts 1–2 and an existing meeting session.

### 4. Implementation requirements

- Add the deck workflow to the preparation page or a focused child component owned by Sales.
- Implement an accessible `.pptx` file picker with clear format and size guidance before upload. Reject unsupported extensions, empty files, and files exceeding the backend limit before sending while retaining authoritative server validation.
- Upload through `SalesPresentationDeckApiClient.ImportAsync` using the selected eligible Sales agent and an editable deck title.
- Show meaningful upload progress when supported by the current Blazor transport. At minimum, show a deterministic busy state that prevents duplicate submission and supports cancellation where the transport supports it.
- After the API accepts the deck, poll the existing get endpoint with a bounded interval until a terminal status or configured timeout. Stop polling on disposal, navigation, cancellation, `processed`, or `failed`.
- Render the existing deck history in a restrained list showing title, file name, version, processing status, slide count, updated time, active state, safe failure summary, and available actions.
- For a retryable failure, expose `Retry processing` only when the authoritative response says retry is allowed. Do not invent retry eligibility in the UI.
- Allow activation only for a processed deck. Require a deliberate user action and present the consequence that this becomes the meeting's active presentation.
- Refresh the Prompt 1 readiness projection after upload, retry, processing completion, or activation.
- Prevent duplicate upload and activation behavior under double-click, reconnect, refresh, and repeated API responses. Surface conflict responses safely and reload authoritative state.
- Preserve previous processed versions and the current active deck according to the existing service behavior. Never delete or replace older decks implicitly.
- Show `slideCount` after processing and make a processed-but-zero-slide result visibly invalid if the backend can return such a state.
- Include the generated `wellheld-overview.pptx` only as a manual development/UAT fixture path in documentation. Do not copy it into production seed data or hard-code it in application behavior.
- Add safe telemetry for upload accepted, processing duration observed by the UI, processing failure category, retry requested, and activation outcome using established conventions. Do not record file contents, extracted slide text, storage keys, or sensitive titles in high-cardinality telemetry.

### 5. Constraints and preservation rules

- Follow `/docs/design.md`, `/docs/architecture-rules.md`, and the shared instructions.
- Keep PowerPoint parsing and rendering in the existing background processor. The Blazor request must not process slides synchronously.
- Do not expose storage implementation details or permit arbitrary server paths/URLs as upload sources.
- Do not activate a queued, processing, failed, or cross-company deck.
- Do not add client-only fake progress states that contradict the authoritative deck status.

### 6. Acceptance criteria

- Given a valid `.pptx` under the limit, when it is uploaded with an eligible Sales agent, then one deck is created, processing state appears, and polling reaches the authoritative terminal state.
- Given the three-slide Wellheld deck, when processing succeeds, then the workspace reports three slides and permits activation.
- Given a processed deck, when the user activates it, then it becomes the only active deck for the session and preparation readiness permits presenter launch.
- Given an invalid extension, oversized or empty file, duplicate submission, processing failure, retryable failure, non-retryable failure, stale state, or cross-company identifier, then the workflow remains safe and explains the next valid action.
- Given navigation away or component disposal, then no orphan polling loop or mutation continues.

### 7. Verification

Add component tests for file validation, upload state, polling, terminal states, cancellation/disposal, retry visibility, activation, conflicts, and empty/error states. Add API/integration coverage for multipart validation, authorization, tenant isolation, background processing transitions, retry, and activation if existing coverage is insufficient. Run focused tests and builds. Complete browser UAT with a small valid deck, the Wellheld deck, an invalid file, an oversized-file substitute that does not require committing a large binary, and a forced retryable failure using supported test infrastructure.

### 8. Definition of done

The user can complete the real PowerPoint lifecycle inside the product. Upload, asynchronous status, cancellation, error recovery, retry, deck history, activation, authorization, observability, and tests are complete with no Swagger or PowerShell requirement.

---

## Prompt 4 — Integrate preparation with Sales and complete the browser-presenter journey

### 1. Title and outcome

Connect the preparation workspace to the Sales lead and meeting invitation experience, then complete and verify the local browser journey from scheduled invitation to active deck and private presenter controls.

### 2. Current context

- `SalesLeadDetail.razor` displays meeting invitations and currently shows `Open private Teams presenter controls` only when a `meetingSessionId` query parameter is supplied.
- The side panel route is `/teams/meetings/{MeetingSessionId}/side-panel` and requires company context.
- `MeetingSidePanel.razor` permits non-Teams access only when `TeamsMeetingUi:BrowserDiagnosticsEnabled` is true.
- The side panel requires an active processed presentation. It already supports slide preview, next, previous, goto, search, manual/assisted/autonomous control modes, and `Pause Alex · Take control`.
- Browser diagnostics must not claim that stage sharing, Teams membership, lobby admission, or Teams audio is active.
- Prompts 1–3 implement the in-product preparation flow.

### 3. Dependencies

Prompts 1–3. Browser diagnostics must remain enabled only in the intended development/test configuration.

### 4. Implementation requirements

- Add invitation-row actions on the Sales lead detail page:
  - `Prepare presentation` before a session/deck is ready
  - `Continue preparation` while setup or processing is incomplete
  - `Open presenter controls` only when authoritative readiness permits it
- Resolve the meeting session through the preparation projection rather than requiring users to manually append `meetingSessionId` to the lead URL.
- Preserve deep-link compatibility for existing `meetingSessionId` URLs, but make query-string knowledge unnecessary for normal navigation.
- Show a compact status beside each invitation, such as session not configured, deck processing, deck failed, deck ready, or presentation active. Use authoritative reason codes and accessible text rather than colour alone.
- Add a final readiness review in the preparation workspace showing the scheduled provider event, selected Sales agent, saved session, processed active deck, slide count, and browser/Teams environment limitations.
- When browser diagnostics is enabled and the meeting is ready, provide `Open browser presenter`. Open the existing private side panel with exact company and session context.
- When running outside Teams, retain the explicit message that sharing requires Teams. Do not label the browser preview as a shared meeting stage.
- When browser diagnostics is disabled, omit the browser launch action and explain that the organizer surface must be opened through the installed Teams application.
- Preserve private/public separation: the preparation page and side panel may show notes and controls, while the shared stage remains token-bound and exposes only customer-safe slide content.
- Add safe navigation recovery for missing/deleted invitation, stale session, inactive deck, revoked company access, reconnecting realtime state, and a deck that becomes inactive after the page loads.
- Update local developer documentation with the UI-only workflow, the expected ports, the `TeamsMeetingUi:BrowserDiagnosticsEnabled` setting, the Wellheld deck fixture path, expected results, and the explicit list of Teams behavior that remains untested.
- Invoke and follow the installed `$polish-uat-loop` skill for final hands-on review. Exercise the real flow, maintain a prioritized issue ledger, implement in-scope fixes, and rerun the original journey after each material correction.

### 5. Constraints and preservation rules

- Follow `/docs/design.md`, `/docs/architecture-rules.md`, and the shared instructions.
- Browser diagnostics is a development/test capability, not a production authorization bypass.
- Never issue or expose a stage access token on the preparation page.
- Do not claim Alex joined, spoke, heard participants, received presenter rights, or shared the stage during browser-only testing.
- Do not make Teams readiness a prerequisite for testing slide processing and private controls in an explicitly enabled browser-diagnostic environment.

### 6. Acceptance criteria

- Given a scheduled invitation with no session, when the user selects `Prepare presentation`, then the invitation-oriented preparation page opens without manually copying an ID.
- Given a session with a processing deck, when the user revisits the lead, then the invitation shows processing state and `Continue preparation` returns to the correct workspace.
- Given a processed active deck, when the lead page loads, then `Open presenter controls` appears without a `meetingSessionId` query parameter.
- Given browser diagnostics enabled, when the presenter opens, then the first slide preview renders and next, previous, goto, search, mode switching, and `Pause Alex · Take control` operate against authoritative state.
- Given browser diagnostics disabled outside Teams, then direct browser access fails closed with clear guidance.
- Given a user from another company or without the required Sales/meeting permission, then preparation state and presenter controls are not disclosed.
- Given a narrow viewport and keyboard-only use, then the lead actions, preparation workflow, upload controls, status updates, and presenter launch remain usable and understandable.

### 7. Verification

Add lead-page and preparation-navigation component tests, browser-diagnostics gating tests, authorization and tenant-isolation tests, deep-link compatibility tests, and realtime state recovery tests. Run the focused unit/integration suites, affected builds, and the mandatory `$polish-uat-loop` browser workflow. Verify the complete path with `artifacts/wellheld-presentation/output/wellheld-overview.pptx`, confirm three rendered slides, and capture the required desktop and narrow-width evidence under `docs/design/references` using repository naming conventions.

### 8. Definition of done

An authorized user can move from a scheduled Sales meeting invitation to a configured session, selected Sales agent, uploaded and activated PowerPoint deck, and functioning private browser presenter entirely through the Virtual Company UI. The journey is secure, tenant-isolated, accessible, responsive, observable, documented, and verified without requiring Swagger, manual API calls, or copied GUIDs.
