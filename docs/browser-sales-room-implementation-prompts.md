# Browser sales room implementation prompts

Implement these eleven prompts in order. They implement `/docs/browser-sales-room-architecture.md` and preserve Teams for future reactivation. This is a prompt pack; creating or opening it does not deploy or start implementation.

## Mandatory instructions for every prompt

Each numbered prompt includes this section by reference. When running a prompt independently, read this section and the selected prompt in full.

1. Read `/AGENTS.md`, applicable scoped `AGENTS.md` files, `/production-implementation.md` and `/docs/architecture-rules.md`. Read `/docs/design.md` for UI work and follow its mandatory reference-image workflow before creating/significantly redesigning UI. `/ui-instructions.md` is only a companion. Invoke the installed `polish-uat-loop` skill for hands-on UI/UAT as required by `/src/AGENTS.md`.
2. Read `/docs/browser-sales-room-architecture.md` and `/docs/teams-preservation-and-reactivation.md`. Current implementation and canonical rules win over older planning assumptions. Inspect relevant current files before editing; proposed class names are not assertions that those classes already exist.
3. Preserve ALL current Teams work, including modified and untracked files. Keep source, supported builds, routes, contracts, settings, packages, migration history, stored records, scripts, infrastructure, tests, admin/UAT surfaces and runbooks in the active tree. No reset, clean, discard, unrelated overwrite, or restoration of HEAD over current work. Git history is not a substitute for retention.
4. Browser rooms are added alongside Teams and existing single-user browser voice. Use an explicit distinct route. Missing optional provider configuration must not block the other route. Preserve existing wire/storage values and invitation URLs. Do not change live Teams flags/resources, reactivate Teams, or send real invitations merely to validate the implementation.
5. Keep the modular monolith, Blazor, ASP.NET Core, SQL Server and shared AI orchestration. No second agent backend, application stack or microservice. Browser media adapters belong to Sales behind Application contracts; shared reasoning/speech stays behind shared orchestration. No sibling Infrastructure implementation references.
6. Schema changes follow the Database and EF Core section of `/docs/architecture-rules.md`: compatible additive migrations in the existing migrations project, updated snapshot, fresh/upgrade SQL Server checks and preservation of Teams migration history/data. Reuse existing entities where their semantics fit.
7. External effects follow the Workflow and Approval and External Side Effects and Outbox sections of `/docs/architecture-rules.md`. Provisioning, calendar writes and delivery require durable execution, stable operation identities, bounded retries and reconciliation. Immediate media cancellation must not wait for an outbox. Never store PCM frames in SQL/outbox.
8. Authorize every command and projection server-side. Guests receive room-scoped access, never company membership. Public DTOs/assets/captions must exclude private fields rather than merely hide them in the UI. Ship real loading, unavailable, permission, reconnecting and failure states.
9. Run focused tests in the narrowest existing projects before broader validation. Shared changes require affected Teams regressions as well as browser tests. Preserve valid tests; record pre-existing failures instead of weakening assertions. Never report an unrun provider/browser/SQL Server check as passed.
10. Continue through the requested sequence without stopping at routine checkpoints. If required credentials or an essential decision blocks a dependent step, finish independent work and report the precise blocker. Do not quietly substitute mocked production behavior or another stack.
11. Every definition of done below requires production implementation: real contracts/endpoints/provider behavior where in scope; no scaffolding, mock production data, placeholder success, silent failures, unhandled intermediate states or deferred in-scope TODOs. Report files changed, checks run, limitations and Teams preservation evidence. Maintain a sanitized progress/evidence index at `/docs/verification/browser-sales-room-implementation.md`.

## Delivery map and execution

| Prompt | Independently delivered outcome |
| --- | --- |
| 1 | Real LiveKit .NET transport and preserved Teams baseline |
| 2 | Durable rooms, invitations, guest admission and revocation |
| 3 | Browser scheduling alongside Teams |
| 4 | Human audio/video meetings and screen sharing |
| 5 | Synchronized approved slides and private host controls |
| 6 | Approved, reusable narration with revision-based invalidation |
| 7 | One room agent, speech detection before external processing, grounded answers |
| 8 | Multi-human dialogue, interruptions and immediate host takeover |
| 9 | Consent-aware capture, minutes and reviewed follow-up |
| 10 | Full-call cost and Azure concurrency benchmark |
| 11 | Operational hardening, rollout evidence and Teams reactivation checks |

To run one stage, use: **Implement Prompt N from `/docs/browser-sales-room-implementation-prompts.md`, including its mandatory instructions. Verify prerequisites against the current tree and complete its implementation and checks.** To run the full sequence, explicitly request Prompts 1–11; continue across normal checkpoints under repository persistence rules.

Scope is a browser meeting with synchronized slide demonstrations, human screen sharing and one speaking Sales agent. Agent-operated interactive product demos and animated avatars remain outside this pack. Initial product limits follow the architecture; package prices and monthly call allowances are configurable business decisions, not hard-coded provider limits.

## Prompt 1 — Add the media adapter and preserve both routes

### Title and outcome

Implement a real .NET LiveKit adapter and independently selected readiness, proving multi-human media without displacing Teams.

### Current context

Inspect `src/VirtualCompany.Infrastructure.Sales/Sales/TeamsApplicationHostedMediaAdapter.cs`, `TeamsRealtimeAudioBridge.cs`, `TeamsMeetingMediaCoordinator.cs`, `TeamsPresenterOptions.cs`, owning DI registration and `src/VirtualCompany.Application/Agents/RealtimeAgentSessionContracts.cs`. `IMeetingMediaAdapter` currently normalizes events/exposes health; it is not a full room transport contract. Teams has its own binding/audio format. LiveKit's .NET media SDK is community-maintained; its server-management SDK alone cannot carry PCM.

### Dependencies

None in this pack. Live feasibility acceptance needs a LiveKit test project/credentials, permitted participants, connectivity and the intended host runtime. Verify current maintainer/provider documentation before pinning dependencies.

### Implementation requirements

Before editing, capture the actual Teams working-tree path/status/content-hash inventory required by the preservation guide, including untracked implementation. Record baseline test/build state and existing blockers without secrets. Complete the preservation map.

Add a distinct browser-room route/options, Sales-owned room transport contracts, provider error normalization and independent health/DI selection. Implement actual room management, narrow token generation, per-participant track subscription/publication, cancellation/buffer flush, removal and cleanup. Expose stable operation/reconciliation inputs for later lifecycle orchestration. Exercise through an explicitly external test harness, not an unauthenticated production diagnostic endpoint.

Pin/review the .NET media package and native dependencies. Implement bounded buffers, track identity/timestamps, format negotiation, resampling, disposal and safe telemetry. Verify native loading on the intended runtime. No domain schema or product UI is expected here.

### Constraints and preservation rules

Apply mandatory instructions. Keep Teams dependencies and its supported build; isolate platform loading rather than deleting packages. Disabled/missing Teams configuration must not block browser startup, and vice versa. Preserve existing `browser_webrtc`. Test resources are isolated, cleaned up, and contain no customer content or logged tokens/PCM.

### Acceptance criteria

- Two real browser microphones produce separate identifiable input tracks; one server-published audio track is heard by both humans.
- Cancellation clears pending playback and rejects frames from a cancelled generation.
- Independent enabled/disabled/configured combinations select the correct adapter and readiness without changing another route.
- A 60-minute real call soak on the intended runtime records reconnect, memory, frame/backpressure and cleanup outcomes.

### Verification

Run focused adapter/routing/DI/native-load/cancellation tests, affected Teams adapter/readiness tests, API and supported Teams builds. Record dependency/license/native-runtime evidence and live call/soak outcomes. A failed media feasibility gate blocks dependent LiveKit work; report it explicitly rather than accepting a stub.

### Definition of done

The mandatory production definition applies. A working real adapter, independent readiness and verified Teams baseline exist, with an evidence-backed feasibility decision or clearly identified external blocker.

## Prompt 2 — Implement durable rooms and guest admission

### Title and outcome

Deliver real authorized room creation, scoped guest access, admission, revocation and termination APIs.

### Current context

Reuse `SalesMeetingSession`, `SalesMeetingSessionService`, `SalesMeetingSessionsController`, company authorization and outbox/background patterns. Existing `SalesPresentationStageAccessService` grants presentation access, not general guest admission. Existing voice sessions are not the room aggregate.

### Dependencies

Prompt 1. Existing SQL Server migration tooling and server secret configuration; provider credentials for external lifecycle verification.

### Implementation requirements

Implement room bindings, invitation grants, participants and purpose-specific consent records only where existing entities do not fit. Include CompanyId, queryable relational state, expiry, concurrency, indexes and unique active-room constraints. Add migrations/snapshot. Preserve meeting/call data and separate room from agent health.

Create room create/status, invitation redemption, lobby, admit/deny/remove, media-token issue/renew, consent and end-room endpoints. Add durable provisioning/termination, signed/deduplicated webhook intake, reconciliation, bounded retries and expiry cleanup. Queries cannot provision resources. Use opaque provider names and server-issued participant identities.

Exchange expiring high-entropy invitation capabilities for scoped guest sessions; hash secrets, limit redemption, remove URL/referrer/log leakage and define replay policy. Admission and any invitee verification are backend decisions. No media tokens before admission. Revocation actively removes provider participants and denies API/hub/asset/token access. Audit the actual actor, not a fabricated organizer identity. Add company room/participant/concurrency limits.

### Constraints and preservation rules

Apply mandatory instructions, including schema and external-effect rules. Guests cannot use room credentials for company APIs. Preserve Teams migrations, IDs and status meanings. No UI is expected in this prompt.

### Acceptance criteria

- Duplicate authorized creation/end commands produce one room lifecycle.
- A valid guest stays without media in the lobby until admitted; expired/revoked/wrong-room access is rejected.
- Removal disconnects an existing media session, not just future token requests.
- Ambiguous/duplicate provider events reconcile safely; stale callbacks cannot reopen finished rooms.

### Verification

Add API/integration tests for roles, tenant/room isolation, replay, expiry, consent races, concurrency and failures. Validate fresh/upgrade SQL Server migrations with existing Teams records intact. Run affected session/Teams migration tests and API build. Document new routes and operations.

### Definition of done

The mandatory production definition applies. Durable admission/lifecycle APIs work against real provider integration; no success response masks a pending or failed provision operation.

## Prompt 3 — Schedule browser meetings alongside Teams

### Title and outcome

Allow Browser meeting selection from the Sales workflow and deliver our join link through existing calendar/invitation workflows, preserving Teams selection.

### Current context

Inspect `SalesMeetingSchedulingService.cs`, `SalesMeetingSchedulingContracts.cs`, `SalesMeetingInvitation`, calendar registry/adapters, invitation/change dispatchers and existing lead/deal scheduling/preparation UI. Calendar and conferencing selection must be separate. Existing approval and canonical change workflows must be reused.

### Dependencies

Prompt 2. Connected calendar/mailbox and explicitly authorized test recipients for actual sends; otherwise use isolated provider contract tests and mark live delivery unrun.

### Implementation requirements

Add explicit conferencing selection in contracts, persistence/read models and existing scheduling UI with compatible legacy defaults. Never infer route by parsing a URL. Browser scheduling idempotently provisions/reuses its room, inserts the join URL and applicable AI disclosure, and requests no Teams online meeting. Preserve organizer, attendees, timezone, selected calendar and approval/outbox behavior.

Implement copy-link, readiness/failure states, reschedule and cancellation through existing change workflows. Define link preservation/revocation/versioning and reconcile ambiguous calendar outcomes. Changing conferencing after sending is an explicit reviewed change, not a hidden rewrite. Add migrations if required and document legacy mapping.

### Constraints and preservation rules

Apply mandatory instructions. The design reference workflow applies to changed scheduling/preparation UI. Keep Teams links, selection, presenter controls and first-UAT behavior. Browser choice never overwrites Teams configuration. Calendar writes and email remain approved durable external effects.

### Acceptance criteria

- The same calendar connection can schedule a browser call using our URL without a Teams create request, or a Teams meeting using its original path.
- Retries do not create duplicate rooms/events/email deliveries.
- Cancelled meetings revoke room entry; rescheduling follows documented link policy.
- Existing Teams invitations retain their type, URL and behavior after upgrade.

### Verification

Extend scheduling/invitation/change-delivery and Web/client tests for authorization, route mapping, retries and cancellation. Run affected Teams scheduling regressions, migrations if applicable, API/Web builds and real reference-based UI verification.

### Definition of done

The mandatory production definition applies. Browser scheduling is fully integrated through existing delivery boundaries; Teams remains selectable under its original readiness gates.

## Prompt 4 — Build the human browser meeting experience

### Title and outcome

Deliver host/guest browser rooms with prejoin, admission, human audio/video and screen sharing.

### Current context

Use Blazor, typed clients/`CompanyApiTransport`, Prompt 2 room APIs, Prompt 3 entry points and the media adapter/browser SDK. Teams pages/layout/context and `teams-meeting.js` remain separate. Guest access requires a room credential, not Teams SSO/company membership.

### Dependencies

Prompts 1–3. Runnable permitted test environment and provider credentials for actual WebRTC verification.

### Implementation requirements

Complete the mandatory design reference workflow. Implement device preview/selection, explicit audio unlock, AI/capture notices, display name, lobby/admit/deny, participant tiles, mic/camera controls, screen share, removal, leave/end and reconnect. Guest pages must not expose internal navigation or private workspace. Prepare a separate private host surface for later controls.

Use a bounded JS interop module for media connection/attachment/disposal; no PCM/video through SignalR or Blazor circuits. Preserve existing single-user realtime JavaScript. Support audio-only, camera-off, keyboard, responsive layout and permission denial. Handle control/circuit loss, token renewal, expired admission and provider outage distinctly. Rejoin must not duplicate identities, tiles or audio.

### Constraints and preservation rules

Apply mandatory instructions and UI workflow. Publish only after admission; leave stops local tracks; host end terminates the room. Do not repurpose Teams pages or show internal provider errors/secrets.

### Acceptance criteria

- Host plus two guests in separate browsers can hear/see each other and share a screen.
- Microphone denial has actionable UI; denied admission yields no media.
- Removal immediately revokes room media/control access; valid reconnect restores one participant.
- Joining works with Teams credentials absent without changing Teams selection/startup.

### Verification

Add Web/API contract and JS lifecycle tests. Exercise Chrome/Edge and Safari/iOS, autoplay, corporate-network TURN, keyboard, mobile layouts and device errors with the required UAT skill. Retain sanitized visual evidence. Run affected Teams Web regressions and Web build.

### Definition of done

The mandatory production definition applies. A real human-only room is usable from Sales and visually verified, independently of agent availability.

## Prompt 5 — Share synchronized slides with private host controls

### Title and outcome

Show every guest the same approved slide while keeping private notes confidential and slide control authoritative.

### Current context

Reuse deck services/processor/renderers, `SalesPresentationRuntimeService`, `SalesMeetingHub`, stage access/presence contracts and `SalesMeetingPresentationConductor` in `SalesPresentationConductor.cs`. Public/private DTOs exist. The conductor uses Teams options and stage presence has process-local state.

### Dependencies

Prompt 4 and processed meeting decks. No agent audio required yet.

### Implementation requirements

Follow the design workflow for stage and private controls. Integrate existing deck readiness/selection, assets, next/previous/goto/pause and manual/assisted/autonomous modes. Serve room-scoped public snapshots/assets; private notes/plan artifacts never enter guest payloads.

Extend hub authorization for admission and active revocation while preserving Teams stage grants and member-only private access. Version commands with command ID, expected version, deck/version and actor/generation. Reconnect reads current state. Capture the required audience at each transition, wait a bounded time for matching render acknowledgements and expose slow/disconnected state with explicit host override.

Extract route-neutral conductor options with tested compatibility for explicit Teams settings. Implement distributed coordination or deliberately enforced durable single ownership; a SignalR backplane alone does not distribute presence dictionaries. Preserve Teams stage acknowledgement behavior.

### Constraints and preservation rules

Apply mandatory instructions. Guests cannot join private groups or execute host methods. Backend resolves actor/company. Keep Teams stage/side-panel routes and package behavior. Any schema changes are additive and verified.

### Acceptance criteria

- Two guests render the same committed deck revision; private notes are absent from guest HTTP/hub/assets.
- Concurrent/stale commands cannot overwrite host changes; old acknowledgements cannot release a newer narration plan.
- Slow clients pause automatic readiness visibly; reconnect restores the current revision.
- Existing Teams option values and stage/conductor behavior remain compatible after extraction.

### Verification

Extend runtime/conductor/stage-access/hub and Web tests, including revoked and wrong-room/deck access. Exercise real multi-browser rendering/reconnect and multi-instance coordination. Run Teams stage/conductor/Web regressions, API/Web builds and relevant migrations.

### Definition of done

The mandatory production definition applies. Authoritative synchronized slides, bounded audience readiness and confidential host controls work with verified Teams compatibility.

## Prompt 6 — Generate and reuse approved presentation narration

### Title and outcome

Generate narration once per approved presentation revision and reuse it across eligible calls, reducing repeated speech-generation cost while retaining human control.

### Current context

Reuse the existing deck processing, versioned presentation runtime and conductor from Prompt 5. Read `/docs/sales-narration-benchmark.md`: its short synthetic provider benchmark and local cache reads do not implement production narration or establish a fixed generation price. Reuse shared AI orchestration and existing document/object storage conventions; inspect their contracts before extending them.

### Dependencies

Prompts 1–5, configured shared speech provider and authorized test content. Playback through the shared agent voice track is integrated in Prompt 7; this stage delivers real generation, review, persistence and authorized preview.

### Implementation requirements

Implement a company-scoped narration preparation workflow: prepare a customer-safe script from approved sources, review/version it, generate speech through shared orchestration, and expose authorized playback preview and explicit readiness. Preserve source evidence and text-to-audio correspondence. Generated speech must pass content validation before becoming playable; explain rejection and retry states.

Segment by slide and talking point. Key assets by company, deck/content revision, script hash, language, voice, speech-model/configuration version and customer-specific context where applicable. Store manifests, hashes, approval references, duration, media format and generation usage in relational metadata with audio in approved object storage. Add necessary migrations and snapshot under canonical database rules. Never share private narration across companies or reuse customer-specific content for a different audience.

Use durable bounded background generation with idempotent segment requests, concurrent-claim protection, cost limits and explicit ambiguous-provider outcomes. Retry costs remain visible. Regenerate only invalidated segments; immutable approved versions stay auditable. Recheck release status and audience at playback time. Unapproved, revoked, failed or missing assets cannot silently fall back to unrestricted live speech. Show preparation/preview/revision controls through the mandatory design workflow.

Expose cancellation-aware playback contracts carrying asset, slide, segment, offset and turn generation for Prompt 7. Track cache hits, generated/reused minutes, bytes and attributable costs without logging content. Define retention and deletion that preserve active authorized references and invalidate revoked assets. A revision means a new approved content version, not every replay or technical retry; expose metering without hard-coding commercial allowances.

### Constraints and preservation rules

Apply mandatory instructions, including AI orchestration, database and external-effect boundaries. Synthetic approved narration assets are distinct from recordings of human calls. Preserve Teams live narration and shared gateway compatibility. Reuse does not eliminate storage, transport or Azure playback costs. The earlier approximately USD 0.448 for 18 minutes is a projection, not a contractual or hard-coded rate.

### Acceptance criteria

- Preparing the same approved revision twice reuses completed segments rather than generating duplicate billable work.
- Updating one slide invalidates its affected segments; changing language, voice or customer context cannot return an incompatible cache entry.
- Wrong-company, wrong-audience and revoked-approval previews/playback are denied server-side.
- A failed segment produces partial/not-ready status; the presentation is never reported fully ready with missing audio.
- Authorized preview plays real generated audio and reports generation usage separately from reuse.

### Verification

Test key isolation, revision invalidation, approval revocation, concurrent jobs, retry accounting, storage authorization and retention. Validate SQL Server migrations and affected deck/shared-speech/Teams tests. Run an authorized real generation and preview in English and Swedish; compare script and audio content. Perform reference-based UI verification and affected API/Web builds.

### Definition of done

The mandatory production definition applies. Approved narration can be prepared, reviewed, previewed and reused as versioned assets with measurable generation usage; room playback is explicitly delivered by Prompt 7.

## Prompt 7 — Add one consent-aware speaking agent

### Title and outcome

Let the host explicitly invoke approved narration or a grounded answer, heard identically by everyone in the room.

### Current context

Reuse `SalesMeetingRealtimeService`, `IRealtimeAgentPcmSessionGateway`, `IRealtimeAgentSessionGateway`, `SalesMeetingQuestionAnsweringService` and the conductor. Existing per-user SDP startup does not establish one room agent; PCM streaming alone does not establish speaker attribution or release-before-speech.

### Dependencies

Prompts 1–6; configured shared reasoning/speech providers and approved agent authority. Observe existing pilot consent/retention prerequisites without automatically enabling an environment.

### Implementation requirements

Consume Prompt 6’s approved narration assets through the same single published voice track used for live released answers. Recheck asset approval/audience/version and current turn generation at playback; observe slide readiness, bounded buffers, pause/resume markers and cache usage. A missing asset leaves an actionable paused state unless policy explicitly authorizes a released-text live fallback.

Add one room-owned agent lifecycle with durable renewable lease, fencing generation, unique active ownership, scoped background execution and cleanup. Add authorized start/stop/invoke controls, independent voice health, duration/spend limits and any necessary migrations. Keep the organizer accountable while preserving the true question actor.

Implement speech-detected input as the initial browser policy, following the architecture's Initial audio input policy section. Run per-track local VAD in the .NET worker before sending audio to external AI/transcription. Preserve human call audio through LiveKit. Use bounded in-memory pre-roll/trailing-silence buffers, keep timestamp/track/consent provenance, and clear buffers on revocation/removal/end. Detector failure pauses AI with typed fallback; it must not silently enable continuous external processing. Validate provider-supported segmented/streaming lifecycle and actual billed duration, including minimums and reconnect costs. Provider-side VAD alone does not satisfy upstream filtering.

Implement per-participant transcription through shared speech contracts, with track identity/timestamps, consent generation and overlap metadata. Exclude agent output/system/screen-share audio from microphone input. Suspend AI while any admitted human lacks consent; withdrawal stops input/output. Transient processing and retained transcript consent are separate.

Route substantive answers through existing grounding and customer-release policy. Implement approved-text speech behind shared orchestration as needed. Publish only released answer text/approved narration; never validate after unchecked audio has streamed. Enforce customer-audience context filtering and evidence. Start with host-invoked/manual speech and exactly one agent track. Preserve typed questions. Add AI state/host stop/private evidence UI through the design workflow.

### Constraints and preservation rules

Apply mandatory instructions. No direct provider reasoning calls from Sales. Do not reinterpret Teams/per-user voice records or reuse format constants blindly. No raw audio retention. Shared gateway extensions remain compatible with Teams PCM paths.

### Acceptance criteria

- Silence beyond configured padding is not forwarded for external transcription; clipped word starts, missed quiet speech and false triggers are tested. Report received/detected/forwarded/billed durations separately, and do not claim savings without billing evidence.
- Concurrent starts create one agent owner/voice track; fenced workers cannot publish.
- All participants hear the same approved narration/answer with retained permitted evidence.
- Unreleased/unsupported substantive answers are withheld or use an approved clarification, never leaked in speech.
- Withdrawal, quota exhaustion or AI outage stops AI while human calls/manual slides continue.

### Verification

Test leases/generations, consent races, authority/quotas, audience filtering and the audio publication gate. Run real multi-human speech/stop tests and affected Teams bridge/realtime regressions. Validate migrations and API/Web builds; measure actual latency.

### Definition of done

The mandatory production definition applies. One real host-controlled room agent speaks with verified consent, grounding, customer filtering and release enforcement; Teams voice remains preserved.

## Prompt 8 — Implement dialogue, interruptions and human takeover

### Title and outcome

Let the agent lead an approved deck, answer addressed questions and immediately yield to the salesperson.

### Current context

Build on Prompt 7's single controlled agent and labelled input, Prompt 5's presentation authority/render acknowledgements, existing modes/preemption and question policies. Mode names alone do not implement multi-human floor control.

### Dependencies

Prompts 5–7 and verified speech release enforcement.

### Implementation requirements

Implement a Sales-owned floor controller with owner, pending turn, overlap, turn/response generation, presentation version and resume marker. Manual mode requires host invocation; assisted mode requires confirmation; autonomous mode permits the approved deck and eligible released answers. Address detection proposes turns; backend floor/mode policy authorizes speech. Human-to-human dialogue does not trigger responses by default.

Local speech-start detection pauses cached or live narration immediately through the floor controller, without waiting for utterance completion or transcription. Test echo and background-noise false starts, and do not treat VAD as intent detection. Human speech pauses narration. Overlap waits for a clear floor or a short approved clarification. Take over wins concurrent claims, cancels generation, flushes provider/adapter/client playback where supported, rejects late speech/tools and restores manual mode. Track client stop acknowledgements/timeout; cancelling generation alone is not proof of silence. Resume uses the accepted current slide/talking point without replaying abandoned audio.

Require matching render readiness before new-slide narration. Recheck mode, authority, consent and generation before execution/publication. Bound transitions/responses. Organizer loss pauses AI; only a preauthorized co-host may resume. Implement host/customer controls and states through the design workflow.

### Constraints and preservation rules

Apply mandatory instructions. Sales/contract/discount/email actions retain existing authorization/approval. Shared preemption changes preserve Teams takeover/modes. Do not create a conversational agent per human.

### Acceptance criteria

- Handoff starts presentation only after slide readiness; interruption pauses speech, a permitted addressed question receives an answer, then host-approved resume uses current state.
- Human-to-human and overlapping speech produce no competing unsolicited agent answers.
- Takeover during speech/tool execution prevents obsolete audio and slide mutations and persists across reconnect.
- Missing audience readiness pauses autonomous narration visibly with explicit host override.

### Verification

Test races among speech, tools, slide/mode changes and consent, including late output. Run real three-human English/Swedish dialogue, overlap, echo/device and organizer-loss tests. Measure p95 takeover-to-silence/answer latency against architecture targets. Run affected Teams interruption/conductor tests and API/Web builds.

### Definition of done

The mandatory production definition applies. The core multi-person sales scenario and human takeover work in real calls, with measured limitations and preserved Teams behavior.

## Prompt 9 — Complete capture, closing and reviewed follow-up

### Title and outcome

Produce consent-aware evidence, reviewable minutes and approved follow-up from browser calls through existing business workflows.

### Current context

Reuse `SalesMeetingCaptureService`, `SalesMeetingClosingService`, transcript/provenance/question entities, internal intelligence, customer minutes, action items and change/minutes delivery dispatchers. Graph transcript ingestion retains its own provenance and behavior.

### Dependencies

Prompt 8 and existing retention/approval policies. Authorized test mailbox/recipients are needed only for live send verification.

### Implementation requirements

Persist only permitted transcript segments with participant/track generation, timestamps, stable IDs and deduplication. Without capture consent, transient transcription must not leak text into storage/logs/traces/retry payloads. Add missing provenance/consent semantics through compatible migrations. Raw audio/video recording remains off.

End stops processing and resolves pending capture idempotently. Expose incomplete capture honestly. Generate minutes/actions through existing reasoning/closing services, preserve internal/customer separation, evidence, review and canonical Sales approval/outbox delivery. Ending a call never automatically authorizes email or CRM changes.

Implement closing/review UI through the design workflow. Apply retention cleanup to all new content and document future-processing revocation versus already retained evidence. Keep browser provenance separate from Graph reconciliation.

### Constraints and preservation rules

Apply mandatory instructions, especially schema, approval and external delivery. Do not fabricate missing transcript sections. Existing Teams capture/minutes remain readable and governed by their original policies.

### Acceptance criteria

- Duplicate events produce one segment/action candidate; Graph records retain source/identity.
- Without capture consent, audio and transcript content are absent from persistence/logs; minimal permitted lifecycle audit remains.
- Ending twice produces one closing result; only approved delivery sends a message.
- Retention cleanup removes eligible browser content without deleting unrelated Teams records or required retained evidence.

### Verification

Test capture/provenance, consent, retention, end races, private-data exclusion and approval/delivery. Validate SQL Server upgrade with existing Teams transcripts/minutes. Run relevant capture/closing/Teams regressions, API/Web builds and real review UAT; distinguish unrun sends.

### Definition of done

The mandatory production definition applies. Permitted browser evidence and reviewed follow-up work through existing workflows, preserving Teams records and provenance.

## Prompt 10 — Measure complete-call cost and Azure capacity

### Title and outcome

Deliver a reproducible benchmark comparing reusable narration with live-generated narration, reporting cost per completed 30-minute call and Azure resources per concurrent call.

### Current context

Read `/docs/sales-narration-benchmark.md` and its existing harness/results. The recorded component test generated about 97 seconds of synthetic audio; it did not exercise browser calls, local speech detection or Azure concurrency. Prompts 1–9 provide the production paths to measure. Extend test tooling without introducing a second production agent stack.

### Dependencies

Prompts 1–9, a named isolated Azure target using the intended Sweden-region deployment/runtime, LiveKit and shared speech credentials, permitted test participants/fixtures, and an explicit load/spend envelope. Do not infer that an existing production App Service is an authorized load target. Complete harness and offline validation if live prerequisites are missing; leave measured fields unreported until run.

### Implementation requirements

Implement and document the full-call protocol from the benchmark document. Use 30-minute fixtures with 18 minutes of approved presentation, six minutes of human questions/discussion, three minutes of live answers and three minutes of host activity/pauses. Compare A: the same approved narration generated live per segment, and B: cached narration with identical Q&A, voice, model, sources and release policy. Add a separately labelled fully live-composed narration arm so differences in reasoning/content are not confused with the isolated caching effect. Every arm retains customer-release controls.

Measure cold construction, warm reuse and partial revision separately, amortizing at 1/10/40/80 uses. Compare continuous-input and local speech-detection policies on controlled synthetic fixtures at 10/30/60 percent speech duty cycle; continuous input is a benchmark-only explicit mode, not a silent production fallback. Include quiet/overlapping English and Swedish speech, short words, noise and output echo.

Implement sufficient instrumentation and disposable deployment assets for the benchmark within existing Azure conventions. Measure idle baseline, then bounded concurrency steps starting at 1 and increasing to 5/10/25/50 only within the authorized envelope. Keep load generators separate. Record CPU-seconds, working set/allocated memory, network/storage, queue growth, dropped frames, restarts, instances and scale events. Report sustainable concurrency on the actual instance size and incremental resources per call, including all required application components.

Capture provider usage by modality, received/detected/forwarded/billed audio, generated/played/cancelled output, cache use, retries and failed calls. Separate OpenAI or Azure-hosted AI charges, LiveKit, Azure application hosting/storage/telemetry and existing fixed hosting; do not double-count model requests across vendors. Verify current rate cards at execution and preserve rate date/currency/region and billing assumptions. Report all attempt costs divided by quality-passing completed calls, plus cold/amortized/warm views. Reconcile available billed usage; label unresolved estimates.

Measure customer-audible latency and quality against architecture targets; report sample size and misses. Produce sanitized machine-readable results and a human-readable report linked from the implementation evidence index. Document recommended concurrency/budget defaults from evidence; do not silently change paid plans or production quotas.

### Constraints and preservation rules

Apply mandatory instructions. Use synthetic fixtures, no customer recordings or logged secrets. No load, resource provisioning or sends outside the authorized isolated scope. Missing resources are a blocker for measurement, never permission to fabricate numbers. Teams remains untouched by benchmark traffic.

### Acceptance criteria

- Repeated runs use versioned fixtures/configuration and distinguish live-generated, cached and live-composed narration.
- Report includes total cost per completed 30-minute call, incremental Azure cost, fixed-cost allocation assumptions, CPU/memory per concurrent call and highest quality-passing tested concurrency.
- Cache reuse produces no narration-generation request on a valid warm hit; live Q&A and transport are still counted.
- Speech-detection savings are based on returned metered usage, with speech-loss/latency results alongside cost.
- Failure/retry/cancellation costs are included; unavailable measurements remain explicitly blocked/not run.

### Verification

Test accounting arithmetic, unit conversion, duplicate usage events, cache amortization and report completeness. Run paired low-concurrency trials before escalating load; stop at resource/spend/quality thresholds. Verify no growing queues and complete drain/cleanup. Reference genuine provider/platform evidence and the exact application revision.

### Definition of done

The mandatory production definition applies. A reproducible full-call benchmark and honest evidence support capacity and cost decisions, or the exact external blocker is recorded with the runnable harness complete. Microbenchmark projections are never relabelled as full-call measurements.

## Prompt 11 — Harden operations and verify Teams reactivation readiness

### Title and outcome

Complete deployment support, resilience and operator controls, with evidence for browser rollout and a preserved executable Teams recovery path.

### Current context

Prompts 1–9 supply the feature; Prompt 10 supplies measured cost and capacity evidence. Use existing Azure hosting/background execution/SQL Server/health/audit infrastructure. Read the Teams preservation guide and existing production, identity, media and UAT runbooks plus Prompt 1's inventory. Retained source and old UAT are not proof of current live readiness.

### Dependencies

Prompts 1–10, authorized isolated browser test resources, provider region/data terms, runtime, secrets and pilot approvals. Actual production enablement or live Teams reactivation requires explicit authorization beyond this prompt's default scope.

### Implementation requirements

Implement browser deployment templates/configuration within existing conventions, secret references, readiness, capacity, drain, lease recovery/reconciliation, revocation propagation and emergency disable that stops active AI. Prove coordination/fencing under the chosen multi-instance topology. Preserve Teams infrastructure/environment settings.

Add useful health/metrics for joins, lifecycle ambiguity, voice health, latency, frame drops, ownership, quotas and costs. Enforce company concurrency/duration/spend limits from measured usage and current provider rates. Provide operating/rollback instructions: configuration rollback disables browser admission/agents and preserves schema/Teams settings; no routine database downgrade.

Run evidence-led UAT and fix in-scope defects. Cover real decks, English/Swedish, browser/device/network matrix, long calls, app/provider loss, worker crash, stale-owner cleanup, deployment drain and consent races. Record sampling and measured p95 targets/misses.

Reconcile the Teams baseline inventory with the final tree. Account for every changed/moved/missing Teams-relevant path and restore accidental removal. Validate supported Teams build, package generation, settings bindings, routes/permissions, migrations/data and affected regressions. Update `/docs/teams-preservation-and-reactivation.md` with exact changed paths/mappings, SDK freshness and unresolved blockers. Keep admin/first-UAT surfaces available. Do not make a live Teams call solely to complete this prompt.

### Constraints and preservation rules

Apply mandatory instructions and required UAT/design workflows for fixes. Never weaken release gates or tests. Static validation does not establish production approval or live Teams readiness. External deployment, sends and resource changes require their proper authorization.

### Acceptance criteria

- Browser-only configuration works without Teams provisioning; Teams-only configuration still binds/builds without browser provisioning.
- Failures yield documented fallback; emergency disable stops active AI; worker replacement cannot duplicate an agent or replay abandoned speech.
- Fresh/upgrade schema preserves Teams data and the supported Teams build/package/test path remains executable.
- A permitted full browser scenario completes: invite, admit, consent, present, interrupt, answer, takeover, close, review and approved follow-up. Claimed live checks have evidence.

### Verification

Run focused fix tests, one appropriate broader API/Web validation, SQL Server upgrade/pending-model checks, supported Teams build/package checks and browser load/soak/UAT. Compare sanitized baseline inventory. Follow repository process-lifecycle rules. Classify evidence as passed, failed, not run or blocked with exact remediation.

### Definition of done

The mandatory production definition applies. Deliver completed implementation, reviewable rollout assets, measured evidence, updated operations/route docs and an accurate Teams reactivation guide. Implementation completion, production enablement and live Teams readiness remain distinct; report missing prerequisites precisely.
