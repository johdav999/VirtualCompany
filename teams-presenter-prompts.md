# Alex Microsoft Teams Presenter — implementation prompts

## Purpose

This prompt pack extends the completed Sales meeting work so Alex can become a real Microsoft Teams meeting participant, speak through an approved Teams media route, and present the existing synchronized Sales presentation in a Teams meeting.

The target experience uses two coordinated Teams capabilities:

1. A Teams meeting application supplies the customer-visible shared stage and salesperson-private side panel.
2. A calling and meeting bot supplies Alex's participant identity and approved real-time audio. Application-hosted video or video-based screen sharing (VBSS) is optional and must remain behind a separate feasibility and rollout gate; the shared meeting stage is the primary visual presentation route.

Concretely, the primary visual route is a dedicated Blazor page in `VirtualCompany.Web`, hosted by Virtual Company and loaded by Teams in the meeting-stage iframe. The page displays the uploaded PowerPoint as the existing authorized, rendered slide images; it does not embed desktop PowerPoint or capture a user's screen. A separate private Blazor side-panel page contains notes and controls. Alex changes the presentation only through the existing server-authorized `presentation.*` commands, and `SalesMeetingHub` keeps the Teams stage, private cockpit, backend meeting state, and Alex's narration synchronized.

The required automated narration sequence is: Alex reads the authoritative slide and talking-point state, selects or advances the slide through a permitted presentation command, waits for the accepted authoritative version and bounded stage-render acknowledgement, and only then speaks the corresponding slide narration. Human commands always preempt Alex, and the organizer can pause or disable Alex's slide-control autonomy immediately.

This separation is intentional. It keeps slide presentation usable when raw media is unavailable and avoids treating raw-media access as the only way to present content.

## Current repository baseline

- `/sales-assistant-prompts.md` Prompts 1–10 have been implemented, including persistent `SalesMeetingSession` state, deck processing, synchronized presentation commands, grounded Q&A/capture, closing, change review, transcript reconciliation, the optional realtime voice pilot, and controlled demo scenarios.
- `/src/VirtualCompany.Application/Sales/SalesMeetingRealtimeContracts.cs` exposes `IMeetingMediaAdapter`, but its current contract only reports health and normalizes already-produced realtime events.
- `ConfiguredMeetingMediaAdapter` only approves the `browser_webrtc` route. It is not a Teams calling bot and has no Teams join, leave, audio, video, lobby, or participant lifecycle behavior.
- `SalesMeetingRealtimeService` and the shared realtime agent gateway already enforce consent, retention, company membership, Sales-agent ownership, bounded tools, usage limits, event idempotency, and typed fallback behavior. Preserve those protections.
- Microsoft 365 calendar integration can create Teams-enabled invitations. Microsoft Graph transcript reconciliation is post-meeting evidence ingestion and does not provide live meeting media.
- `SalesMeetingHub` and the presentation runtime provide authoritative, company-scoped synchronization and separate stage-safe state from private state.
- Reference assets exist at `/docs/design/references/sales-meeting-stage-reference.png` and `/docs/design/references/sales-meeting-side-panel-reference.png`. Their existence is not proof of approval; verify approval before using them as implementation references.
- There is no production Teams calling-bot registration/package, Graph call-control adapter, Teams media adapter, media-host deployment, or end-to-end organizer control for admitting Alex.
- Controlled demo tenants deny external Teams, calendar, email, and other provider side effects. A real Teams integration must be tested in an explicitly authorized non-demo tenant.

## Mandatory instructions for every prompt

Each prompt is an implementation prompt, not a planning exercise.

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and all applicable `AGENTS.md` files.
- For UI work, follow `/docs/design.md`; its mandatory reference-image and approval workflow applies. Use `/ui-instructions.md` only as a companion where it does not conflict.
- Preserve the modular-monolith boundaries. Keep provider-neutral contracts in Application, Teams provider details in `VirtualCompany.Infrastructure.Sales`, HTTP/webhook transport in API, and TeamsJS/Blazor behavior in Web.
- Validate all Microsoft Teams, Microsoft Graph, TeamsJS, app-manifest, permission, media SDK, hosting, and policy assumptions against current official Microsoft documentation at implementation time. These APIs and preview limitations can change.
- Microsoft currently states that application-hosted real-time media bots are specialist infrastructure and are not the recommended default for AI-agent meeting scenarios. Do not enable such a route without explicit product-owner, tenant-administrator, security, privacy, legal, and operations approval.
- Do not substitute delegated calendar tokens for application calling permissions. Do not store client secrets, certificates, access tokens, raw SDP, or raw media in source control, manifests, relational business records, logs, audit metadata, browser storage, or support exports.
- All tenant-owned state and operations must be company-scoped and server-authorized. A Teams tenant, meeting ID, join URL, user-supplied company ID, or client-visible session ID is never sufficient authorization by itself.
- Treat joining, inviting, speaking, starting a presentation, sending captions/notifications, and changing meeting state as external side effects. Enforce backend policy, explicit organizer intent, stable idempotency, durable execution where ambiguity/retry matters, safe reconciliation, and audit evidence.
- Alex must control slides through the existing provider-neutral commands—`presentation.get_current_slide`, `presentation.search_slides`, `presentation.next`, `presentation.previous`, `presentation.goto`, `presentation.pause`, and `presentation.resume`—not by directly invoking JavaScript, manipulating the DOM, simulating clicks, or calling Teams UI automation.
- Slide narration and slide state must be causally synchronized. Alex may not announce or explain slide N until the backend has accepted slide N as authoritative and the active stage has acknowledged rendering that version, subject to an explicit bounded degraded-mode policy.
- Human presentation commands and stop/pause controls always override Alex. Autonomous slide control must be explicitly enabled for the meeting, scoped to the approved active deck, and independently revocable without ending the meeting.
- Preserve typed/host-mediated presentation and the `browser_webrtc` pilot as safe fallbacks. A Teams outage or disabled Teams route must not break deck processing, presentation commands, meeting capture, closing, review, or delivery.
- Never claim that Alex can bypass a Teams lobby, meeting policy, organizer decision, tenant app policy, participant role, consent boundary, or platform limitation.
- No production mocks, fake provider success, permissive development authentication in production, silent fallback to a different tenant, or in-scope TODOs are acceptable.

## Execution order and gates

Run the prompts in order. Prompts 1–2 establish the app identity, permissions, and fail-closed tenant readiness boundary. Prompt 3 adds durable call control. Prompt 4 adds approved live audio and the policy-governed presentation tool loop. Prompt 5 adds the Virtual Company-hosted Blazor Teams stage, render acknowledgements, automated narration synchronization, and optional supported visual-media route. Prompt 6 establishes the required Azure runtime. Prompt 7 adds organizer controls and completes live tenant UAT.

Do not enable application-hosted media in a production tenant until Prompt 6 is deployed and Prompt 7's security, policy, privacy, and live-call acceptance criteria pass. If Microsoft or the tenant does not approve application-hosted media for this use case, implement the same provider-neutral contracts through an approved certified media provider and leave the unsupported route disabled.

---

## Prompt 1 — Deliver the Teams application, calling-bot identity, and fail-closed readiness gate

### 1. Title and outcome

Create a production-valid Microsoft Teams application package and server-side configuration/readiness boundary for Alex. Administrators can register and install one correctly described app without enabling live media prematurely, and operators can see exactly which prerequisites remain incomplete.

### 2. Current context

The repository has Teams-enabled calendar invitations, a synchronized meeting hub, stage/side-panel reference assets, and browser-hosted realtime voice. It does not contain a production Teams app manifest or calling-bot identity. `SalesMeetingVoiceOptions.MediaRoute` defaults to `browser_webrtc`, and `ConfiguredMeetingMediaAdapter` rejects other routes.

### 3. Dependencies

The implemented `/sales-assistant-prompts.md` sequence. External Microsoft 365 developer tenant access is needed only for live installation verification; package generation and validation must work without credentials.

### 4. Implementation requirements

- Add a versioned Teams application package owned by the Sales integration. Include manifest, color icon, outline icon, meeting side-panel/stage configuration, valid domains, web application information, bot registration reference, and the current manifest-schema fields required for calling and video capabilities.
- Set calling/video declarations only when they truthfully match the selected route. Do not advertise video support merely because the existing browser adapter can exchange WebRTC audio with OpenAI.
- Add environment-aware, strongly typed `TeamsPresenter` configuration for Teams app ID, bot application ID, Microsoft Entra tenant mode/allowlist, public API origin, bot notification/calling callback URLs, Web content URLs, selected media route, certificate reference, package version, and independent feature gates for call control, audio, shared stage, and optional video/VBSS.
- Validate configuration at startup. Production configuration must require HTTPS, exact allowed hosts, non-placeholder IDs, an approved route, and a certificate/managed credential reference. Development configuration may remain disabled and explain missing prerequisites.
- Implement an authorized readiness query/API that reports safe typed checks for package validity, selected route, app/bot IDs, callback reachability configuration, credential presence, tenant approval state, required permission state, media-host compatibility, and feature gates. Never return secret values or detailed token errors.
- Add a deterministic package/build command that injects only non-secret environment values, validates the manifest against the current schema, verifies icons and domain consistency, and produces a reproducible ZIP artifact outside source files.
- Document manual Entra/Azure Bot/Teams Developer Portal registration steps, exact callback/content URL mapping, development versus production credentials, package generation, and rollback. Clearly label values that must be supplied externally.
- Add audit/telemetry for configuration/readiness evaluation changes without logging credentials.

### 5. Constraints and preservation rules

- Follow the integration, security, Web, and configuration boundaries in `/docs/architecture-rules.md`.
- A readiness check must never create Azure resources, grant consent, install the app, join a call, or mutate meeting state.
- Do not commit tenant-specific IDs, generated secrets, certificates, or a production-ready ZIP containing private tenant values.
- Keep the package compatible with the existing stage/private contract boundary. Private Sales intelligence must never be exposed to the stage.

### 6. Acceptance criteria

- Given valid non-secret inputs, when the package command runs, then it produces a schema-valid Teams application ZIP with matching app/bot IDs, content URLs, callback domains, contexts, icons, and package version.
- Given missing, placeholder, HTTP production, mismatched-domain, or unapproved-route configuration, when the application starts, then Teams presenter capabilities remain disabled and readiness returns an actionable stable reason code.
- Given an authorized operator, when readiness is queried, then the result distinguishes app package, identity, permissions, callbacks, media host, and rollout gates without revealing secrets.
- Given an ordinary company member or wrong-company request, when readiness details are requested, then administrative configuration is not disclosed.
- Given the Teams route is disabled, then existing scheduling, typed presentation, transcript reconciliation, and `browser_webrtc` fallback behavior remain unchanged.

### 7. Verification

Add configuration-validation, authorization, package-generation, schema, icon, URL, placeholder, and secret-redaction tests. Run the affected API/Web builds and validate the generated ZIP with the current Teams manifest tooling. Complete one manual upload into a development tenant when external credentials are supplied; report it as unverified rather than faking success when they are absent.

### 8. Definition of done

The versioned Teams package, typed configuration, fail-closed gates, authorized readiness surface, deterministic packaging, documentation, audit/telemetry, and tests are complete. Live calling remains disabled until the later permission and deployment gates pass.

---

## Prompt 2 — Implement application authentication, Graph calling permissions, and tenant administrator consent

### 1. Title and outcome

Implement the secure Microsoft identity and tenant-registration boundary required for Alex to act as a Teams calling bot. A tenant administrator can grant the least privileges required for the approved route, and Virtual Company can prove which company and tenant a callback or call belongs to before any action occurs.

### 2. Current context

Microsoft 365 calendar and transcript flows use organizer-delegated connections. Teams call control and application-hosted media require a separate application identity and application permissions. The repository has company authorization, OAuth state protection, token-protection patterns, audit infrastructure, health checks, and safe problem responses, but no Teams calling-bot application credential or company-to-Entra-tenant registration.

### 3. Dependencies

Prompt 1, an Entra/Azure Bot application ID, an approved credential strategy, and access to a development Microsoft 365 tenant. Tenant administrator consent is required for live verification.

### 4. Implementation requirements

- Revalidate the least Microsoft Graph application permissions for the selected behavior. At the time of writing, joining a scheduled group call normally requires `Calls.JoinGroupCall.All`, and application-hosted media additionally requires `Calls.AccessMedia.All`; request no broader permission unless a concrete implemented operation requires it.
- Use certificate-based or managed/federated application authentication in production. Permit a client secret only in local/test configuration, mark it unsupported for production, and load it through server-side secret configuration.
- Add provider-neutral Application contracts and a Teams implementation for acquiring and caching app-only Graph tokens with bounded refresh, clock-skew handling, cancellation, safe error translation, and no token logging.
- Add relational, company-owned Teams tenant-registration/readiness state as needed: company ID, Entra tenant ID, app/bot identity references, consent/readiness statuses, approved media route, policy/approval timestamps and actors, safe failure code, version, and audit timestamps. Do not persist credentials or access tokens. Add EF configuration, SQL Server migration, and model snapshot updates.
- Require an explicit platform-administrator action to associate a Virtual Company company with an Entra tenant. Prevent one Entra tenant from being silently rebound to another company, and define the supported one-to-many/many-to-one rule explicitly.
- Implement admin-consent initiation/completion or a documented admin-controlled consent verification flow, protected by correlation/state, exact redirect URIs, replay protection, expiry, and server-side administrator authorization.
- Verify granted permissions from trusted Microsoft identity/Graph evidence where supported. Do not mark consent complete merely because a user returned to a callback URL.
- Implement secure validation and tenant resolution for Teams/Bot Framework/Graph call notifications using the current supported SDK/protocol. Resolve the company from persisted tenant/app/call state; never accept a company ID from the provider payload as authority.
- Extend readiness and health reporting with credential, token, tenant-consent, permission, policy, and callback-authentication states. Use safe stable error codes and bounded retry guidance.
- Document app registration, API permissions, admin consent, tenant policies, credential rotation, removal, and incident revocation.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md` for tenant isolation, identity, database migrations, audit, integrations, and sensitive data.
- Keep calendar delegated permissions and bot application permissions separate in code, configuration, storage, consent UX, and documentation.
- App-only permission is not authorization for a user action. Every company operation still requires company policy and explicit organizer intent.
- Never allow a development header identity, test credential, or fake JWT validator in production.

### 6. Acceptance criteria

- Given a registered development tenant with the exact required grants, when consent verification runs, then the company registration becomes ready with evidence of the tenant and granted permission set but no credential/token persistence.
- Given missing or extra-unapproved permissions, revoked consent, wrong issuer/audience/tenant, expired callback state, replayed callback, or an unassociated tenant, then the integration remains disabled and returns a safe actionable state.
- Given company A is associated with tenant A, when a notification or admin flow from tenant B is received, then no company A state is disclosed or changed.
- Given the application credential rotates, then new tokens use the new credential without deleting Sales meeting records or requiring a database reset.
- Given consent is revoked or the tenant registration is disabled, then new calls are blocked immediately and active-call handling follows an explicit safe termination policy.

### 7. Verification

Add token/authentication contract tests using signed deterministic test tokens or supported SDK test facilities; authorization, callback replay, issuer/audience, tenant-association, cross-company, permission-diff, credential-rotation, migration, and secret-redaction tests; then run affected builds. Perform a live development-tenant consent/token check when credentials are supplied and explicitly report any unverified external step.

### 8. Definition of done

Application authentication, least-permission verification, tenant registration, admin consent, secure callback identity, health/readiness, migration, documentation, and tests are production-ready. No call can start solely because an application token exists.

---

## Prompt 3 — Implement durable Teams call control and meeting participation lifecycle

### 1. Title and outcome

Allow an authorized organizer to invite or command Alex to join a scheduled Teams meeting as a real bot participant, observe lobby/call state, recover from duplicate or ambiguous callbacks, and leave cleanly without yet depending on live audio.

### 2. Current context

`SalesMeetingSession` links to a scheduled invitation and provider meeting data. The Microsoft 365 adapter stores the Teams join link, and transcript reconciliation can resolve an online meeting afterward. The current realtime service starts an OpenAI WebRTC call from browser SDP; it does not create or control a Microsoft Graph communications call. Existing workflow/outbox, idempotency, audit, and background-worker infrastructure must be reused.

### 3. Dependencies

Prompts 1–2, an authorized non-demo company, a Teams invitation created through Microsoft 365, a ready tenant registration, public authenticated bot callback URLs, and tenant policy allowing the bot/app.

### 4. Implementation requirements

- Add provider-neutral call-control contracts separate from `IMeetingMediaAdapter`, with commands/queries for readiness, request join, accept an invitation where supported, observe admission/lobby state, leave, terminate after consent revocation, reconcile, and retrieve a safe authoritative snapshot.
- Implement the Microsoft Graph/Bot Framework Teams adapter for joining the scheduled meeting using the persisted provider meeting identity or join information. Normalize Graph/SDK DTOs before Application or Domain code sees them.
- Add company-owned persistent call state linked to `SalesMeetingSession`: provider call ID, tenant/meeting identity references, requested/admitted/connected/ending/ended/failed/reconciliation states, organizer command and policy evidence, idempotency key, callback sequence/version, media-host instance affinity, safe provider reference, retry/reconciliation state, timestamps, and optimistic concurrency. Add EF configuration, migration, and snapshot updates.
- Model the call lifecycle as an authoritative backend state machine. Handle lobby wait, admitted, rejected, meeting ended, bot removed, organizer disconnected, duplicate notifications, delayed/out-of-order notifications, provider timeout, callback loss, service restart, and ambiguous join/leave outcomes.
- Enqueue join and leave external actions durably. Recheck tenant readiness, meeting consent, retention, organizer/company authorization, feature gates, and active-call limits immediately before provider execution. Use stable idempotency derived from company/session/action/version.
- Authenticate and acknowledge provider callbacks within platform time limits, persist receipts before asynchronous processing, and deduplicate by provider event/resource/version. Do not log provider payloads containing sensitive participant data.
- Permit at most one active Alex Teams call per meeting and enforce configured per-company and per-host concurrency limits.
- Keep human admission authoritative. Surface `waiting_in_lobby` and instructions; never loop or attempt to bypass rejection/policy denial.
- Add authorized API commands and typed Web client methods for join, leave, status, and reconciliation. Do not add final UI controls until Prompt 7.
- Add call-control health, latency, callback, failure, retry, reconciliation, active-call, and forced-termination telemetry.

### 5. Constraints and preservation rules

- Follow the workflow/outbox, integration, tenant, audit, and database sections of `/docs/architecture-rules.md`.
- Do not reuse the transcript subscription as a live-call subscription or infer live-call authorization from a join URL.
- Do not start OpenAI realtime or media sockets in this prompt. Call control must be independently testable and safe.
- Demo tenants must continue to block provider calls at the external side-effect boundary.

### 6. Acceptance criteria

- Given an authorized organizer, ready tenant, granted meeting consent, and valid scheduled Teams meeting, when join is requested, then exactly one durable provider call is created and status progresses through authoritative lifecycle states.
- Given Alex is waiting in the lobby, then Virtual Company reports that state and does not claim Alex is connected or retry around the organizer.
- Given duplicated join commands or provider callbacks, then no duplicate call or state transition is produced.
- Given an ambiguous Graph response or callback outage, then the call enters reconciliation rather than being blindly recreated.
- Given consent revocation, meeting end, organizer stop, tenant disable, or retention expiry, then active participation is terminated according to policy and audit evidence is preserved.
- Given a wrong-company user, unassociated tenant, demo tenant, unauthorized role, or manipulated meeting ID, then no call is created and no provider state is disclosed.

### 7. Verification

Add domain state-machine tests, adapter contract tests, API authorization and tenant-isolation tests, outbox/idempotency/concurrency tests, callback authentication/replay/order tests, restart/reconciliation tests, migration tests, and deterministic simulated-provider tests. Run affected builds and complete one real development-tenant join/lobby/admit/leave test when external prerequisites exist.

### 8. Definition of done

Alex can reliably join and leave as a real Teams bot participant through durable, authorized, idempotent call control. Lifecycle state, callbacks, failure recovery, health, telemetry, migration, documentation, and tests are complete; no fake connected state or direct request-handler provider call remains.

---

## Prompt 4 — Connect approved Teams audio to Alex's realtime meeting intelligence

### 1. Title and outcome

Enable an admitted Alex participant to receive meeting audio through the approved Teams media route, send Alex's synthesized speech back to Teams, and operate the authorized presentation commands in lockstep with its narration while preserving consent, interruption handling, human override, privacy, and typed fallback behavior.

### 2. Current context

The repository already has `IRealtimeAgentSessionGateway`, `SalesMeetingRealtimeService`, persistent voice sessions/event receipts, grounded Q&A, exact interruption/resume markers, usage limits, a browser WebRTC pilot, and authoritative presentation tools in `SalesPresentationToolNames`. The current realtime allowlist exposes only meeting-specific read/Q&A tools and does not authorize Alex to mutate presentation state. `IMeetingMediaAdapter` is currently too small for a Teams media session and `ConfiguredMeetingMediaAdapter` only validates/normalizes the browser route. Prompt 3 adds real Teams call control and host affinity.

### 3. Dependencies

Prompts 1–3; explicit written approval for the selected Teams media route; a compatible deployed or development Windows media host; active meeting consent and retention; provider credentials; and an admitted Teams call.

### 4. Implementation requirements

- Revalidate whether the approved route is Microsoft application-hosted media or a certified provider. Implement it behind provider-neutral Application contracts. Do not emulate a Teams media connection with the existing browser SDP flow.
- Evolve `IMeetingMediaAdapter` or introduce narrowly separated media-session contracts for create/attach, receive audio, send audio, mute/suspend, cancel response, reconnect where supported, usage/status, and terminate. Preserve a compatible browser adapter rather than adding route conditionals throughout `SalesMeetingRealtimeService`.
- For application-hosted media, use the current supported `Microsoft.Graph.Communications.Calls.Media` .NET SDK and supported PCM/audio formats. Keep SDK types inside Infrastructure.Sales.
- Bind each media session to the durable call, company, meeting session, consent version, agent, and pinned media-host instance. Reject media/events whose binding cannot be proved.
- Build a bounded audio pipeline with format validation, frame timing, jitter/backpressure handling, silence/voice activity handling, cancellation, reconnect policy, and explicit degraded states. Do not persist raw audio.
- Bridge normalized participant speech and Alex audio with the shared realtime agent gateway. Preserve existing evidence boundaries, exact slide/talking-point interruption marker, barge-in cancellation, and resume semantics.
- Expose the existing presentation tools to Alex through the shared agent tool registry with explicit classifications and schemas: `presentation.get_current_slide`, `presentation.search_slides`, `presentation.next`, `presentation.previous`, `presentation.goto`, `presentation.pause`, and `presentation.resume`. Keep read/search separate from mutating commands, require the active company/session/deck context, and reject invented tool names such as direct `nextSlide()` JavaScript.
- Add a backend-owned meeting presentation-control mode with stable values such as `manual`, `assisted`, and `autonomous`. Default to `manual`; only an authorized organizer may enable `assisted` or `autonomous`, and the organizer may return to `manual` immediately. In `assisted`, Alex recommends a transition for human confirmation. In `autonomous`, Alex may execute approved presentation mutations within the active deck and configured limits.
- Implement an agent presentation conductor that composes the existing per-slide objective, talking points, expected timing, transition text, current slide, talking-point index, resume marker, and authoritative presentation version. It must not duplicate the presentation state machine or keep an independent in-memory slide number.
- Define the provider-neutral stage-presence/render-acknowledgement contract needed by the conductor, including session, deck/version, slide, presentation sequence/version, active connection, and bounded wait result. Prompt 5 must connect this contract to the real Blazor stage. Until a verified stage is connected, autonomous narration remains fail-closed while manual/assisted tools continue to work.
- Enforce the narration synchronization sequence: obtain the current authoritative snapshot; decide the next talking point or target slide; execute the appropriate versioned `presentation.*` command; await the accepted authoritative snapshot; await the bounded stage-render acknowledgement; then begin narration tied to that exact deck version, slide number, talking-point marker, and presentation version.
- If the stage is disconnected or does not acknowledge the expected slide within the configured bound, do not silently narrate a different slide. Pause, retry only according to policy, or announce a neutral degraded state to the organizer while typed/manual controls remain available.
- Interpret clear participant navigation requests such as “go back one slide” or “show the implementation timeline” through `presentation.previous` or `presentation.search_slides` followed by `presentation.goto`. Ask for confirmation when multiple search results or an ambiguous request could move to materially different content.
- Treat a human presentation command as a preemption event. Cancel or pause narration that was bound to an older presentation version, reload the authoritative snapshot, and continue only from the human-selected slide/marker. Never immediately counteract a human override by advancing again.
- Bound autonomous behavior by maximum consecutive transitions, minimum/maximum dwell time, active deck range, current meeting phase, command rate, and optimistic concurrency. Reaching the last slide must move to the configured closing/discussion behavior rather than wrapping to slide one.
- Map active/dominant speaker identity only to the minimum participant attribution required by the meeting workflow. Respect unidentified/guest speakers and never infer identity from voice characteristics.
- Require granted consent before attaching media, while processing every frame, and before sending Alex audio. Consent revocation must synchronously stop output and asynchronously terminate provider resources.
- Enforce duration, audio, token, reconnect, frame-size, event-size, simultaneous-call, and provider-rate limits. Apply backpressure instead of unbounded buffering.
- Keep browser and Teams voice sessions distinguishable in persistence, health, audit, support diagnostics, and cost/usage telemetry.
- Add safe observability for media setup time, frame drops, jitter/backpressure, time-to-first-audio, barge-in latency, provider failures, usage, and forced termination. Exclude raw audio, transcripts, SDP, tokens, and detailed provider payloads.
- Update `/docs/sales-meeting-realtime-voice-pilot.md` or add a dedicated Teams media runbook covering configuration, consent, failure modes, emergency disable, and rollback.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md` for AI orchestration, tool governance, privacy, integration, background execution, and audit.
- Application-hosted media is currently specialist/preview infrastructure and is not a general browser feature. Fail closed on unsupported OS, SDK age, networking, certificate, host affinity, or route approval.
- Alex cannot make commitments, update canonical Sales records, or send external messages through voice tools.
- Presentation mutation tools are allowed only while organizer-approved slide autonomy is active. They cannot upload/replace a deck, expose speaker notes to the stage, grant Teams roles, or change canonical Sales records.
- Alex never manipulates the Blazor page or Teams client directly. The backend presentation runtime remains authoritative and publishes accepted state through `SalesMeetingHub`.
- Typed and host-mediated meeting controls must remain available when Teams audio is unavailable.

### 6. Acceptance criteria

- Given an admitted call, approved media host, granted consent, and enabled route, when media starts, then participant speech reaches the normalized realtime pipeline and Alex audio is sent back through Teams without exposing server credentials.
- Given autonomous presentation mode and a connected stage, when Alex completes a slide's final talking point, then it issues one versioned `presentation.next`, waits for the accepted/rendered new slide version, and only then narrates that slide's first talking point.
- Given Alex is asked to show a uniquely matching topic, when search resolves one slide, then Alex uses `presentation.goto`, waits for authoritative/rendered confirmation, and narrates content from that slide rather than maintaining a private slide counter.
- Given a human selects another slide while Alex is speaking, then stale narration is cancelled or paused, the human command wins, and Alex reloads the authoritative slide before continuing.
- Given autonomous mode is disabled or paused, then Alex performs no presentation mutation and may only recommend the next action or respond through the permitted non-mutating tools.
- Given participant barge-in while Alex is speaking, then the active response is cancelled once, the exact presentation marker is persisted, the grounded answer flow runs once, and presentation resumes from the stored marker.
- Given revoked consent, mute/stop, quota exhaustion, malformed frames, provider failure, route disable, or media-host drain, then output stops promptly, state becomes explicitly degraded/stopped, and typed fallback remains available.
- Given duplicate or reordered media/provider events, then durable receipts prevent duplicate questions, tool calls, or state transitions.
- Given a participant whose identity cannot be established, then the segment remains safely unattributed rather than being assigned to another attendee.
- Given raw audio or sensitive provider data, then it is absent from database records, logs, audit metadata, and support exports.

### 7. Verification

Add media-format/frame validation, bounded-buffer, cancellation, barge-in, consent, quota, event-order, host-affinity, tenant-isolation, tool-authorization/rejection, presentation-mode, narration-command ordering, render-timeout, search/goto ambiguity, last-slide, optimistic-concurrency, human-preemption, fallback, and secret-redaction tests. Use deterministic in-memory media frames only in tests, not as a production adapter. Run affected builds and load tests, then perform an approved live two-participant Teams audio test on compatible infrastructure.

### 8. Definition of done

The selected Teams media route provides production-controlled two-way audio integrated with the existing realtime Sales meeting workflow, and Alex can use only the approved backend presentation commands under organizer-controlled autonomy. Narration is version-bound to the authoritative rendered slide, human override is immediate, and consent, limits, interruption/resume, failure states, fallback, telemetry, documentation, and tests are complete; raw audio is not retained.

---

## Prompt 5 — Host PowerPoint in the Virtual Company Blazor stage and synchronize Alex's slide automation

### 1. Title and outcome

Host the uploaded PowerPoint presentation in a dedicated Virtual Company Blazor experience that Teams loads into its shared meeting stage. Keep the salesperson cockpit private, render the authoritative slide for all attendees, and synchronize every Alex `next`, `previous`, `goto`, `pause`, or `resume` command with what Alex is currently narrating. Where explicitly approved and currently supported, optionally let the media bot send a slide/video or VBSS stream without making that route mandatory.

### 2. Current context

The repository already ingests PPTX files, stores deck/slide metadata, renders each slide, exposes authoritative `presentation.*` commands, and publishes stage-safe/private snapshots through `SalesMeetingHub`. It also has stage and side-panel reference images. It does not currently contain a verified production Teams meeting-stage package or production Blazor routes that Teams can load. Prompt 1 creates the base package, Prompt 3 supplies the bot participant, and Prompt 4 supplies bot audio plus the policy-governed presentation conductor.

### 3. Dependencies

Prompts 1–4. Explicit user approval of the applicable stage, side-panel, and organizer-control reference images is mandatory before UI implementation. A development Teams tenant is required for live shared-stage verification.

### 4. Implementation requirements

- Execute the mandatory `/docs/design.md` workflow. Inspect existing Sales surfaces and existing meeting reference assets; confirm whether those assets were explicitly approved. Create or revise references for any new Teams states and controls, present them for approval, and implement only after approval.
- Implement production Blazor routes in `VirtualCompany.Web` for the private side panel and customer-visible stage, using a stable shape such as `/teams/meetings/{meetingSessionId}/side-panel` and `/teams/meetings/{meetingSessionId}/stage`. Teams must load these Virtual Company-hosted pages as meeting-app iframes. Authenticate through supported Teams context/SSO and server-side company/meeting authorization. Retain a secure browser-hosted diagnostic mode only where already allowed.
- The PowerPoint presentation experience is hosted by Virtual Company, but the original PPTX and rendered slide files must remain behind the repository's authorized document/object-storage boundary. Do not copy customer decks into public `wwwroot` files. The Blazor stage retrieves slide content through authorized APIs or short-lived scoped asset URLs.
- Display the server-rendered slide image rather than embedding Microsoft PowerPoint, Office desktop, a remote desktop, or a screen capture. Preserve the stored exact pixel dimensions and aspect ratio, letterbox when necessary, and never crop slide content. Preload only a bounded set such as the current/next slide without leaking inaccessible deck assets.
- Use current TeamsJS meeting APIs and manifest contexts for side panel and stage. Implement capability detection and the standard user-initiated **Share to meeting** action, including the currently required resource-specific consent such as `MeetingStage.Write.Chat` only if official documentation still requires it.
- Treat Teams stage sharing as a participant/organizer-authorized action. Do not simulate a successful share or silently fall back to screen capture when Teams rejects it.
- Render only stage-safe slide/state data on the shared stage. Keep notes, confidence, internal intelligence, suggested responses, consent controls, call diagnostics, and administrator data in the private cockpit and private API contracts.
- Implement a server-verified stage presence and render-acknowledgement protocol. After the stage has successfully decoded and displayed a slide, it reports the company-scoped session ID, connection ID, deck ID/version, slide number, presentation sequence/version, and render timestamp. Accept acknowledgements only from an authorized active stage connection and make duplicates idempotent.
- Synchronize stage, cockpit, bot speech, and server presentation state using the existing authoritative version/sequence rules. The concrete agent flow is `presentation.get_current_slide` → optional `presentation.search_slides` → one versioned mutation such as `presentation.next`, `presentation.previous`, or `presentation.goto` → authoritative command result → hub broadcast → Blazor stage renders → stage acknowledges that exact version → Alex narrates the corresponding slide/talking point. On reconnect, recover the server snapshot before accepting more commands.
- Ensure `presentation.pause` immediately suspends autonomous transitions and active narration at the stored talking-point marker. Ensure `presentation.resume` reloads the authoritative snapshot, restores that marker, confirms the rendered slide, and only then continues speaking.
- When Alex reaches a slide transition in the stored presentation plan, let the conductor advance automatically only in organizer-approved `autonomous` mode. In `assisted` mode, show the proposed `next`, `previous`, or `goto` action for human confirmation. In `manual` mode, Alex must not mutate presentation state.
- Add accessible private controls for start/stop sharing, autonomy mode, next/previous/goto/search, pause/resume, Alex speaking state, audio mute/stop, connection/render acknowledgement state, and typed fallback. Human controls use the same backend commands and always preempt an in-flight Alex command or narration bound to an older version. Use duplicate-click protection and truthful loading, lobby, denied, degraded, reconnecting, and ended states.
- Surface a concise private status such as “Slide 4 displayed · Alex presenting talking point 2 of 3”. Never expose internal talking points or autonomy diagnostics on the customer stage.
- Handle stage-render failure explicitly. If the authoritative slide asset is missing, unauthorized, fails to decode, or does not acknowledge within the bound, keep the previous safe slide or show an approved neutral paused state, stop Alex from narrating the unconfirmed slide, and give the organizer retry/manual fallback controls.
- Implement optional bot video/VBSS only after a documented feasibility check confirms the selected SDK, tenant, deployment, formats, licensing/policy, and use case support it. Keep it behind an independent gate such as `TeamsPresenter:VisualMediaEnabled`.
- If optional bot visual media is enabled, convert the existing exact-aspect slide render into supported bounded frames, preserve deterministic slide/version mapping, rate-limit changes, send an explicit paused/answering frame, and stop cleanly. Do not capture the user's desktop or arbitrary windows.
- If the platform requires a human participant to initiate shared-stage presentation or grant a role, state that limitation in the UI and runbook. Do not claim Alex independently acquired presenter rights.
- Add deep links from the authorized lead/invitation/session experience into the installed Teams app and shared-stage flow without exposing private identifiers in an unsafe URL.

### 5. Constraints and preservation rules

- `/docs/design.md` applies in full. Do not implement unapproved new UI from text alone.
- TeamsJS and media SDK types remain at integration/UI boundaries; the server's presentation state stays provider-neutral.
- CSS hiding is never a privacy boundary. Stage endpoints and hub messages must be structurally unable to return private data.
- Alex never calls Blazor component methods, JavaScript functions such as `nextSlide()`, or Teams UI automation. Alex invokes the existing backend `presentation.*` tools; the stage reacts only to the resulting authoritative server state.
- The original PPTX and rendered customer slides are tenant-protected content, even though the presentation page is hosted in the front end. Do not expose durable anonymous asset URLs.
- The shared stage is the primary visual route. Optional raw visual media must not become a prerequisite for presentation, Q&A, capture, or closing.

### 6. Acceptance criteria

- Given an installed app and authorized organizer, when **Share to meeting** is selected and Teams grants capability, then all participants see the synchronized stage at the authoritative slide/version while the organizer retains private controls.
- Given a processed PowerPoint deck, when the Teams stage opens, then the Virtual Company Blazor page displays the authorized rendered slide at its exact aspect ratio without requiring desktop PowerPoint or screen sharing.
- Given autonomous mode and an active stage, when Alex finishes the final talking point on slide N, then exactly one authorized `presentation.next` is accepted, the stage renders and acknowledges slide N+1, and Alex narrates only content bound to slide N+1.
- Given Alex or the organizer requests `presentation.goto` for slide N, then every connected stage/cockpit converges on the same authoritative deck/slide/version before Alex describes slide N.
- Given the organizer presses Previous, Goto, or Pause while Alex is operating, then the human command wins, stale narration is cancelled/paused, and Alex does not counteract the override.
- Given a stage client or manipulated stage URL, then no private notes, intelligence, confidence, consent details, tokens, or cross-company/session data are returned.
- Given Teams denies stage sharing, a required RSC permission is absent, or the user is not allowed to present, then the UI shows the actual limitation and does not report success.
- Given a disconnected/reloaded stage or side panel, then it returns to the authoritative state without replaying stale commands.
- Given the new slide is not rendered and acknowledged within the configured bound, then Alex does not narrate it and the organizer receives an actionable retry/manual fallback state.
- Given an unauthorized or expired slide asset URL, then no deck content is returned and no cross-company metadata is disclosed.
- Given optional visual media is disabled or unsupported, then shared-stage presentation still works and bot audio remains independent.
- Given optional visual media is enabled, when slides change or presentation pauses, then the exact supported frames are sent in order within configured rate/resource bounds and stop on consent revocation or meeting end.
- Given keyboard-only or assistive-technology use, then all organizer controls and changing states are operable and announced.

### 7. Verification

Add Blazor component/route, authorized slide-asset delivery, exact-aspect rendering, Teams context/SSO, public-private contract, hub reconnect/order, stage presence/render acknowledgement, narration-command-render ordering, autonomy-mode, human-preemption, missing/slow slide, authorization, RSC capability, deep-link, accessibility, and visual regression tests against approved references. If visual media is implemented, add format/frame/rate/termination tests. Run affected builds, validate the package, and perform browser UAT plus a real Teams desktop/web meeting test at documented stage sizes.

### 8. Definition of done

The approved Virtual Company Blazor side panel and shared stage are production-ready, securely display processed PowerPoint slides inside Teams, and synchronize authoritative slide commands, render acknowledgements, Alex narration, and human override. Optional visual media is either implemented behind its verified gate or truthfully documented as unsupported and disabled; no desktop PowerPoint dependency, direct DOM control, placeholder share success, durable public slide URL, or private-data leakage remains.

---

## Prompt 6 — Deploy the Teams media runtime on supported Azure infrastructure

### 1. Title and outcome

Provide reproducible, secure Azure infrastructure and operations for the selected Teams media route so callbacks and pinned real-time media sessions survive normal deployment, scaling, monitoring, certificate rotation, and incident response.

### 2. Current context

Virtual Company is a .NET modular monolith. Application-hosted Teams media cannot be deployed as an ordinary Azure Web App and has OS, public endpoint, instance affinity, networking, SDK freshness, compute, and drain requirements. Prompt 3 persists host affinity and call state; Prompt 4 implements the approved media route. Existing deployment conventions, health checks, secret configuration, SQL Server, audit, and observability must be reused.

### 3. Dependencies

Prompts 1–5; an approved Azure subscription/region/network design; DNS; certificates; Key Vault or equivalent; monitoring workspace; and explicit architecture/operations approval for the selected Microsoft-supported hosting topology.

### 4. Implementation requirements

- Revalidate the current Microsoft application-hosted media requirements. Select a supported Windows Server Azure topology—such as VM scale sets, supported AKS Windows nodes, or another currently documented option—and record the choice and rejected alternatives in an implementation decision tied to deployable infrastructure.
- Keep the repository's .NET modular-monolith architecture. If a separate deployable media host is genuinely required for SDK/platform isolation, obtain explicit architecture approval, keep it in the same solution and stack, share only Application contracts, and do not duplicate Sales business rules, persistence ownership, or AI orchestration.
- Add production Infrastructure as Code for compute, public instance-level addressing/ports where required, load balancer/NAT rules, DNS, TLS certificates, network security, outbound dependencies, managed identity, Key Vault references, health probes, diagnostics, autoscaling bounds, and SQL/Redis connectivity used by existing coordination.
- Ensure each active media call remains pinned to the instance that created/accepted it. Route subsequent media/callback work to that instance or use a supported coordination pattern; never move raw media through the database or generic message bus.
- Implement graceful drain: reject new calls, expose draining readiness, preserve active calls until their bounded deadline, terminate safely when required, and deploy without silently abandoning sessions. Document the limits of scale-in and emergency shutdown.
- Enforce media SDK freshness at build/deployment time according to Microsoft's current deprecation window. Fail deployment/readiness on unsupported OS/architecture, expired certificate, missing public reachability, incompatible SDK, or invalid port mapping.
- Store credentials/certificates in managed secret infrastructure. Implement rotation and rollback without committing private material or requiring deletion of business data.
- Add dashboards/alerts for active calls per instance, admission/join failures, callback authentication failures, media setup time, packet/frame health, provider/Graph throttling, dropped frames, realtime latency, forced terminations, certificate expiry, CPU/network saturation, and cost/usage ceilings.
- Add disaster recovery, region outage, database outage, provider outage, certificate failure, SDK emergency upgrade, and tenant-disable runbooks. Define what is safely recoverable and what requires the organizer to re-invite Alex.
- Keep the existing API/Web deployment functional when Teams media is disabled. Feature flags and tenant allowlists must support immediate emergency disable without schema rollback.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md` and repository deployment conventions. Do not introduce a second language or duplicate application stack.
- Do not deploy application-hosted media to Azure Web App if Microsoft still disallows it.
- Infrastructure changes must be reproducible and reviewable; no production setup may depend on undocumented portal clicks.
- Never expose SQL, Redis, management endpoints, metrics with tenant content, or raw media ports beyond the minimum required network boundary.

### 6. Acceptance criteria

- Given a clean approved Azure environment, when Infrastructure as Code is deployed, then the configured public callbacks, credentials, ports, health probes, persistence, and monitoring are created without embedding secrets.
- Given an admitted live call, when unrelated instances scale or deploy, then the call remains on its owning instance and new work is not routed to a draining instance.
- Given certificate rotation, then new sessions use the rotated certificate and existing data/audit history remains intact.
- Given incompatible OS/SDK, missing public reachability, expired certificate, or invalid network rules, then readiness fails closed before accepting a call.
- Given an emergency disable, then new Teams calls/media stop immediately, active sessions follow the documented policy, and typed meeting workflows remain usable.
- Given a resource/usage threshold is exceeded, then bounded admission control prevents overload and operators receive actionable alerts.

### 7. Verification

Validate IaC syntax and policy, run security/static scans, deploy to an isolated development environment, test callback reachability and authentication, certificate rotation, health/readiness, instance pinning, scale-out, drain/upgrade, forced termination, resource ceilings, and rollback. Run a bounded concurrent-call load test appropriate to the approved capacity. Do not describe an undeployed template as production-verified.

### 8. Definition of done

Supported Azure infrastructure, secure secrets/certificates, networking, instance affinity, deployment/drain behavior, monitoring/alerts, cost limits, recovery/incident runbooks, validation, and development-environment evidence are complete. Production rollout remains gated by Prompt 7.

---

## Prompt 7 — Add organizer controls, tenant installation, restrictive-policy UAT, and controlled rollout

### 1. Title and outcome

Complete the end-to-end experience so an authorized organizer can install Alex, invite or admit Alex, understand required Teams roles/policies, start and stop presentation/audio, and safely run a customer meeting under a controlled tenant rollout.

### 2. Current context

Prompts 1–6 provide package/readiness, application identity and consent, durable call control, approved audio, shared-stage presentation, and supported Azure hosting. The existing lead page schedules Teams invitations and can link a `SalesMeetingSession`, but it has no complete production organizer experience for Alex as a real participant. Controlled demo tenants cannot be used for live external integration tests.

### 3. Dependencies

Prompts 1–6; approved UI references; deployed development infrastructure; a Microsoft 365 tenant administrator; Teams app/calling/meeting policies; licensed organizer and test users; legal/privacy approval; support/on-call ownership; and a non-demo test company.

### 4. Implementation requirements

- Execute `/docs/design.md` for every new or changed organizer/admin surface and obtain explicit reference approval before implementation.
- Add a platform-admin Teams integration surface that shows tenant association, app installation/package version, admin consent, exact required versus granted permissions, calling policy, callback/media health, certificate expiry, allowed-company rollout, and safe remediation links. Never show secrets.
- Add organizer controls to the authorized Sales meeting experience for: check readiness, open/install the Teams app, invite/request Alex join, display lobby state, explain how a human organizer admits Alex, open the Teams meeting, share Alex to stage, start/stop/mute Alex audio, stop sharing, remove/leave Alex, revoke consent, and recover/reconcile an ambiguous state.
- Add explicit `manual`, `assisted`, and `autonomous` slide-control modes. Explain each mode in plain language, default every meeting to `manual`, show who enabled autonomy and when, and provide an always-visible **Pause Alex**/**Take control** action that immediately preempts Alex without ending the meeting.
- In autonomous mode, show the authoritative current slide, rendered/acknowledged state, current talking point, and next planned transition privately. In assisted mode, let the organizer approve or reject Alex's proposed `next`, `previous`, or `goto` command. All controls must call the same server-authorized `presentation.*` commands used by Alex.
- Make every control consume authoritative backend policy and state. Show stable reason codes as plain-language guidance for missing admin consent, app policy, lobby, presenter role, meeting option, unsupported client, disabled feature, tenant mismatch, quota, provider outage, or expired retention.
- Determine from current Teams APIs which actions can be performed programmatically. When Teams requires the organizer to use native participant controls to admit Alex or change who can present, provide exact in-context instructions and wait for observed provider state; do not automate the Teams UI or report success early.
- Require explicit meeting consent before media starts and show a persistent, accessible indicator while Alex can hear or speak. Support immediate revocation and explain transcript/retention consequences using approved wording.
- Add invitation/meeting guidance that identifies Alex as an AI assistant, explains what it can access/do, and avoids misleading attendees about human identity or autonomous commitments.
- Implement a per-tenant/company/user rollout model with disabled-by-default production gates, pilot allowlists, concurrency/cost limits, emergency disable, package-version compatibility, and rollback to typed/browser-hosted workflows.
- Create a live UAT matrix covering Teams desktop and web clients, organizer/presenter/attendee roles, lobby on/off, restrictive app setup policies, guest/federated participants, anonymous attendees where supported, meeting lock, organizer disconnect/rejoin, bot removal, stage sharing denial, media/callback/network failure, consent revocation, meeting end, transcript policy, accessibility, localization, and tenant isolation.
- Record UAT evidence without customer data or raw audio. Distinguish deterministic automated coverage from externally verified tenant/client behavior.
- Update operator, administrator, salesperson, privacy, support, incident, install/uninstall, permission revocation, credential rotation, and rollback documentation. Include a precise end-user walkthrough for scheduling a Teams meeting and bringing Alex into it.

### 5. Constraints and preservation rules

- `/docs/design.md` applies in full.
- The UI cannot grant permissions, roles, admission, or consent that the backend/provider has not verified.
- No general-purpose browser or desktop UI automation may be used to bypass native Teams controls or tenant policy.
- Pilot rollout must use authorized non-demo companies; demo tenants continue to block external side effects.
- External customer meetings require named accountable human ownership and an immediate way to stop Alex.

### 6. Acceptance criteria

- Given an installed/approved tenant and authorized organizer, when a qualified Teams meeting is opened, then the organizer can request Alex, see the true lobby/admission state, start approved audio, share the stage, present synchronized slides, and stop/remove Alex.
- Given the organizer enables autonomous slide control, then Alex can advance or jump only within the active deck, waits for the Blazor stage to render each accepted version before narrating it, and exposes its current/next presentation state privately.
- Given the organizer selects **Take control**, then autonomous transitions stop immediately, any stale narration is cancelled/paused, and subsequent human presentation commands remain authoritative until autonomy is explicitly re-enabled.
- Given Teams requires native admission or presenter-role changes, then the UI gives accurate steps and updates only after the provider confirms the resulting state.
- Given attendees have not granted the approved consent state, then Alex cannot receive or send meeting media.
- Given restrictive tenant/app/meeting policy, missing permission, unsupported client, wrong tenant, demo company, quota limit, or disabled rollout, then the action is blocked with an actionable reason and no provider side effect.
- Given an emergency disable or provider outage during a meeting, then Alex stops according to policy, the salesperson sees a clear fallback, and typed presentation/capture can continue.
- Given two companies/tenants and manipulated identifiers or deep links, then neither call state nor meeting/private data crosses the isolation boundary.
- Given the approved UAT matrix, then every required scenario has dated evidence, an owner, pass/fail status, and no unresolved release-blocking defect before production enablement.

### 7. Verification

Add admin/organizer component tests, authorization and tenant-isolation tests, policy/reason-code tests, autonomy-mode and human-preemption tests, narration/render synchronization tests, duplicate-click/idempotency tests, consent/revocation tests, package-version and rollout-gate tests, accessibility/localization checks, and browser UAT against approved references. Execute the documented live Teams UAT matrix in an isolated tenant on deployed Prompt 6 infrastructure. Run focused tests and the full relevant solution build.

### 8. Definition of done

An authorized organizer can install, invite/admit, operate, stop, and remove Alex in a real Teams meeting with truthful state, explicit consent, approved presentation/audio, restrictive-policy handling, production monitoring, complete runbooks, and recorded live UAT. Production remains disabled for any tenant that has not passed the defined approval and readiness gates.

---

## Official references to revalidate during implementation

- [Register calls and meetings bots for Microsoft Teams](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/registering-calling-bot)
- [Real-time media calls and meetings](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/real-time-media-concepts)
- [Application-hosted media bot requirements and considerations](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots)
- [Microsoft Graph call: answer permissions](https://learn.microsoft.com/en-us/graph/api/call-answer?view=graph-rest-1.0)
- [Teams meeting app APIs](https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/meeting-apps-apis)
- [Build tabs for Teams meetings](https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/build-tabs-for-meeting)
- [Design apps for Teams meetings](https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/design/designing-apps-in-meetings)
- [Share app content to the meeting stage](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/build-and-test/share-in-meeting)
