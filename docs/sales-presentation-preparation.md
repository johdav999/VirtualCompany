# Sales presentation preparation readiness

The Sales presentation preparation projection is the authoritative read model for deciding what a user can do before opening the browser presenter. It is a passive, company-scoped read and does not create sessions, mutate decks, contact a calendar provider, or write audit events.

## Endpoint

GET /api/sales/meeting-invitations/{invitationId}/presentation-preparation

The endpoint requires an authenticated company member and a resolved company context. A missing invitation and an invitation owned by another company both return the same 404 problem response with code resource.not_found.

The response contains safe invitation details, the existing meeting session, eligible Sales agents, safe deck processing summaries, the active deck ID and slide count, one readiness state, blocking explanations, and allowed actions. It intentionally excludes attendee addresses, provider payloads and tokens, storage keys and URLs, content hashes, and stage capabilities.

## Eligibility

A new meeting session can be created only when the invitation is scheduled, has a provider event, and at least one eligible presenter exists. An eligible presenter belongs to the resolved company, has status active, and belongs to the Sales department. This is the same eligibility enforced by deck import and presentation runtime services.

## Readiness states

- blocked: a prerequisite or non-recoverable deck state prevents presenter launch.
- session_required: prerequisites are satisfied and the session can be created.
- deck_required: a session exists and needs a deck.
- processing: an uploaded deck is pending scanning or processing.
- activation_required: a processed deck must be activated.
- ready: an active, processed deck is assigned to an eligible Sales agent.

## Blocking reason codes

- invitation_not_scheduled
- provider_event_missing
- eligible_sales_agent_missing
- session_missing
- deck_missing
- deck_processing
- deck_processing_failed
- deck_processing_blocked
- active_deck_missing
- active_deck_agent_ineligible

Each code is paired with a safe, actionable English explanation. Clients should use the code for behavior and the explanation for presentation.

## Allowed actions

- create_session: invitation, provider event, and agent prerequisites are satisfied.
- update_session: the existing session is still ready and its preparation can be edited.
- upload_deck: a session and eligible Sales presenter exist.
- retry_processing: at least one failed deck is retryable.
- activate_deck: at least one processed, inactive deck can be activated.
- open_presenter: the readiness state is ready.

Allowed actions are advisory projections of the owning mutation rules. Mutation endpoints remain responsible for authorization, concurrency, and final invariant enforcement.

## PowerPoint workflow

After a meeting session exists, the preparation workspace exposes Step 2 for the
real presentation lifecycle:

1. Choose a `.pptx` file, confirm or edit its title, and verify the selected
   eligible Sales presenter.
2. Upload the deck. The browser rejects unsupported extensions, empty files, and
   files above the server-advertised limit before sending; the API remains the
   authoritative validator.
3. The accepted deck appears in history immediately. The workspace polls the
   existing deck endpoint at a bounded interval until processing succeeds,
   fails, is blocked, is cancelled, or times out.
4. Retry is offered only when the deck response says `canRetry`. A processed
   deck with slides can be made active only through an explicit confirmation.
5. Every upload, retry, terminal processing result, and activation refreshes the
   readiness projection. Older deck versions remain visible and are not deleted
   or replaced by the browser.

Polling and upload work is cancelled when the component is disposed. Repeated
clicks are guarded locally, while conflicts trigger an authoritative reload.
Failure copy is limited to the API's safe summary; storage keys, slide contents,
and presentation titles are not emitted as telemetry dimensions.

## Manual development and UAT fixture

Use `artifacts/wellheld-presentation/output/wellheld-overview.pptx` as the normal
three-slide manual fixture. It is documentation-only: production behavior and
seed data do not refer to this path.

Recommended manual cases:

- upload the Wellheld fixture and verify the terminal result reports 3 slides;
- upload a small non-PowerPoint file and verify it is rejected before transport;
- lower the configured upload limit during development and use a small file over
  that substitute limit instead of committing a large binary;
- use supported test infrastructure to force a retryable processor failure,
  verify retry visibility, then let processing finish;
- activate the processed deck and verify it becomes the session's only active
  deck and presenter readiness refreshes.

The visual reference for this workflow is
`docs/design/references/sales-presentation-deck-workflow-reference.png`; its
generation prompt is stored beside it.

## Local UI journey

The normal in-product path does not require copied invitation or session IDs:

1. Start the API on `http://localhost:5301` and the Web app on
   `http://localhost:5062` using the repository launch scripts.
2. Open a Sales lead and find its meeting invitation.
3. Use **Prepare presentation** when no session exists, **Continue preparation**
   while the deck is missing or processing, or **Open presenter controls** when
   the authoritative preparation projection reports `ready`.
4. Configure and save the session, upload
   `artifacts/wellheld-presentation/output/wellheld-overview.pptx`, wait for
   processing to report 3 slides, and explicitly activate it.
5. Review the final readiness section. In Development,
   `TeamsMeetingUi:BrowserDiagnosticsEnabled` is `true`, so **Open browser
   presenter** opens the existing private side panel with exact company and
   session context.

The setting belongs under `TeamsMeetingUi` and must remain disabled outside an
intended development/test environment. When it is disabled, the preparation
page omits the browser action and directs the organizer to the installed Teams
meeting application. Direct non-Teams access to the side panel fails closed.

The browser journey verifies PowerPoint processing, private slide preview,
next/previous/goto/search, manual/assisted/autonomous mode changes, pause/take
control, and reconnect recovery. It does **not** verify that Alex joined Teams,
heard or spoke, passed the lobby, received presenter rights, shared the meeting
stage, or produced Teams audio. The browser preview must never be described as
a shared meeting stage.

Existing visual references remain authoritative for this integrated journey:

- `docs/design/references/sales-lead-detail-reference.png`
- `docs/design/references/sales-meeting-preparation-desktop-reference.png`
- `docs/design/references/sales-meeting-preparation-narrow-reference.png`
- `docs/design/references/sales-presentation-deck-workflow-reference.png`
- `docs/design/references/sales-meeting-side-panel-reference.png`

The Prompt 4 UAT session and current environment blockers are recorded under
`uat/sales-presentation-preparation-prompt4/`.
