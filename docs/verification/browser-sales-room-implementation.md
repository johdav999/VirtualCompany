# Browser sales-room implementation evidence

## Prompt 1 — LiveKit transport and Teams preservation

Status: implementation delivered; live-media feasibility gate remains **BLOCKED / NOT PROVEN**. No browser-room product UI, guest admission API, database schema, deployment or live Teams changes were made. Prompts 2–11 have not been implemented by this task.

### Implemented boundaries

- Application contracts: `src/VirtualCompany.Application/Sales/SalesRoomMediaContracts.cs`.
- Independent optional configuration/registration: `SalesRoomMediaOptions.cs`, called by `SalesModuleRegistration.AddSalesInfrastructure`.
- Real provider management and token adapter: `LiveKitSalesRoomMediaTransport.cs`.
- Native per-track input and one agent output connection: `LiveKitSalesRoomMediaConnection.cs`.
- Serialized, cancellable output and native resampling: `SalesRoomAudioOutput.cs`.
- External executable: `tests/benchmarks/livekit-media/LiveKitMediaHarness.csproj`.
- Focused tests: `tests/VirtualCompany.Api.Tests/SalesRoomMediaTransportTests.cs`.

The new route is `browser_livekit_room`. Existing `browser_webrtc` and Teams routes keep their original meanings and registrations. Configuring or resolving the optional browser transport does not load native code or fail application startup. Readiness distinguishes disabled, invalid configuration, configured/unverified, and locally native-ready/live-unverified. A successful local native probe is not a provider connectivity or Azure approval signal.

The adapter derives opaque provider room names from company plus room identity. Provider create operations carry a stable caller-supplied operation ID in metadata; retries inspect the existing room and reject mismatched binding/limits. Management timeouts and uncertain writes report reconciliation requirements without automatic retries. The SDK's management requests do not expose CancellationToken; a timed-out dispatched write can still finish. Prompt 2 must persist receipts/outbox lifecycle and reconcile these outcomes.

Tokens are short-lived, restricted to one room and server-derived participant/generation identity, and cannot publish data or administer the room. Agent publication is limited to microphone audio. Token record string formatting redacts credentials. These are INTERNAL provider contracts: the future room use case must authorize membership/admission/consent and persist the true actor before calling them. No HTTP diagnostic/token endpoint was introduced.

Native input subscribes only to explicitly supplied consented human microphone identities, excluding agent output and screen-share audio. SDK streams and the application queue are bounded. Input emits participant ID, track ID/generation, sequence, arrival timestamp and mono PCM16 at 24 kHz. The arrival timestamp is not claimed as the original capture clock. Revocation unsubscribes, cancels input and clears buffered input before provider removal. The allowed input set is fixed for a connection; new admissions/consent require a rebuilt connection until the room coordinator supplies a governed update lifecycle in later prompts.

One local connection owns one published agent track. Output accepts exact 20-ms mono PCM16 frames at 16/24/48 kHz, resampling to 48 kHz. Concurrent producers receive backpressure instead of accumulating tasks. Cancellation advances generation, cancels in-flight writes and clears the native queue/resampler; late generations are rejected. `CompleteSpeechAsync` flushes the resampler tail and waits for playout. Client/network buffers and customer-audible stop latency still require live evidence.

Reconnect pauses output and advances the turn generation; it does not replay abandoned narration or automatically resume. Dispose/recreate the connection under the owning room policy to resume. Session expiry and application shutdown release streams, tracks, sources and rooms; failed cleanup is visible. Provider removal/deletion revokes the local stream first. The in-process owner/capacity check is not a distributed lease: durable fencing across hosts remains Prompt 7 work.

### Verification

- Pre-edit Teams application-hosted media baseline: 3 passed.
- Final focused tests and Teams compatibility suite: see `browser-sales-room/check-results.json` for the final count. Includes route configuration through Sales DI, scoped/narrow tokens, provider binding/retry/failure, output backpressure/cancellation/drain, Teams readiness, Teams media, Teams package builder, runtime and deployment prerequisites.
- Windows x64 native probe: passed for SDK 0.1.4, native AudioSource creation/clear/disposal and 24-to-48-kHz resampling with real output frames. No LiveKit network request or AI request was made.
- API and Web Release builds: passed on Windows x64 with existing compiler warnings. This retains the Teams dependency/build path; it is not Windows Server/Azure runtime validation.
- NuGet vulnerability audit, including transitive Sales dependencies: no vulnerable packages reported by the configured feeds at execution time. This is feed-based dependency evidence, not a comprehensive security review.
- No schema changes; no SQL migration was generated or required.

### Exact remaining feasibility checks

No `LIVEKIT_*` environment variable was present during inspection. No isolated Azure target/runtime or permitted real browser participants were identified. The user has been asked to identify the test project/runtime; credentials must be configured in a secret store/environment rather than conversation or logs.

Therefore the following remain NOT RUN: real room management, two actual browser microphone tracks, common customer-audible agent output, browser/network cancellation, removal/reconnect behavior against LiveKit, 60-minute call soak on the intended Azure runtime, and native packaging on that runtime. The external synthetic harness has compiled; its live mode has not run. These are mandatory Prompt 1 feasibility evidence, so this document does not mark the entire prompt accepted or authorize dependent provider rollout.

### Reproduction and configuration

Bind `SalesBrowserRoom` through existing server-side configuration:

| Setting | Default / requirement |
| --- | --- |
| Enabled | false |
| Route | browser_livekit_room |
| Url | wss origin, no credentials/query/path |
| ApiKey / ApiSecret | server secret references; never browser configuration |
| TokenLifetimeSeconds | 120; allowed 30–300 |
| RequestTimeoutSeconds | 15; allowed 2–60 |
| MaximumBufferedFrames | 25; allowed 5–100 |
| MaximumSessionMinutes | 60 maximum |
| MaximumLocalRooms | 50; local safety ceiling, not measured capacity |

For ASP.NET environment configuration use `SalesBrowserRoom__Url`, `SalesBrowserRoom__ApiKey`, etc. The external harness separately reads `LIVEKIT_URL`, `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`.

```powershell
dotnet run --project tests/benchmarks/livekit-media/LiveKitMediaHarness.csproj -- --probe
# Only against an explicitly isolated authorized project; creates and deletes a disposable room:
dotnet run --project tests/benchmarks/livekit-media/LiveKitMediaHarness.csproj -- --live 1
# After a short live run passes, execute the 60-minute synthetic transport soak on the intended runtime:
dotnet run --project tests/benchmarks/livekit-media/LiveKitMediaHarness.csproj -- --live 60
```

The live harness uses two synthetic native participants plus the actual production room adapter. It reports distinct input identities, agent-track frames at both recipients, late-frame rejection, memory and cleanup. It never prints tokens or raw PCM. This is transport evidence, not proof of real browser microphone or audible takeover behavior. Additional controlled real-browser evidence must record devices, network, track identities, reconnect/removal, stop-to-silence, memory/frame behavior and cleanup, without storing human audio or tokens. Do not claim these checks from the synthetic harness.

Application counters report frames observed/dropped at the application boundary. The SDK can also drop frames inside its bounded input queue; those drops are not exposed by its current API. Provider/client diagnostics are required during soak to characterize total loss and reconnect quality. Review that limitation before accepting capacity targets.

### SDK provenance and preservation

Pinned management package: `Livekit.Server.Sdk.Dotnet` 1.2.3. Pinned media package: `Livekit.Rtc.Dotnet` 0.1.4. Both package manifests declare Apache-2.0. The media package uses native Rust FFI binaries and Google.Protobuf/System.Threading.Channels dependencies. Exact source commits and all six bundled native assets/hashes are in [dependency manifest](browser-sales-room/dependencies.json). Native/platform compatibility and licensing notices must be checked for the actual deployment artifact; local loading alone does not prove every RID works.

Primary maintainer references: [SDK repository](https://github.com/pabloFuente/livekit-server-sdk-dotnet), [RTC package](https://www.nuget.org/packages/Livekit.Rtc.Dotnet/0.1.4), [management package](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet/1.2.3).

The sanitized baseline covers 746 existing paths, including modified/untracked Teams implementation, relevant shared Sales files, project references, migration history and deployment/configuration assets. See [baseline](browser-sales-room/teams-baseline.json) and [comparison](browser-sales-room/preservation-comparison.json). The baseline stores hashes/status, not secret values or local secret files. Only the Sales project gained two package references and its registration gained the additive browser registration call. The Teams preservation guide also gains this task's evidence note. No Teams implementation, package, infrastructure, migration, config or test was removed, disabled or reactivated.


## Prompt 2 — Durable rooms and guest admission (2026-09-09)

The backend implementation adds six relational tables through `20260909152622_AddBrowserSalesRooms`: rooms, invitation grants, participants, purpose-specific consent history, operation receipts, and normalized provider events. It binds to the existing meeting session through its existing company/ID alternate key. Migration Up only creates new tables and indexes; historical migrations and Teams data are preserved. One durable room binding per meeting is enforced for the entire meeting lifetime, including after termination. A subsequent meeting uses a new meeting session.

### API and authorization contract

All organizer routes start at `/api/sales/browser-rooms`, require company authentication and `X-Company-Id`, and verify active membership plus the meeting's actual creator. A guest capability never authenticates company APIs.

| Method and suffix | Purpose |
| --- | --- |
| POST `meetings/{meetingId}` | Create durable provisioning work; body `commandId`, UTC `expiresUtc` |
| GET `{roomId}` or `{roomId}/lobby` | Read state, versions, participants and pending provider operations; no provisioning side effects |
| POST `{roomId}/invitations` | Issue a one-use secret; body `commandId`, `expectedVersion`, UTC `expiresUtc` |
| POST `{roomId}/invitations/{invitationId}/revoke` | Revoke invitation and any redeemed guest |
| POST `{roomId}/participants/{participantId}/{admit\|deny\|remove}` | Organizer admission decision or active removal |
| POST `{roomId}/media-token` | Issue or renew organizer media access |
| POST `{roomId}/consent` | Update the organizer participant's purpose-specific consent |
| POST `{roomId}/end` | Deny access immediately and enqueue confirmed provider termination |
| POST `{roomId}/operations/{operationId}/retry` | Organizer-controlled recovery after five unsuccessful provider attempts |

Versioned room commands carry `commandId` and `expectedVersion`. Exact create/end/decision/retry replays return current authoritative state; reusing the command ID with a different payload is rejected. Invitation issuance deliberately returns `invitation_already_issued` on replay: its plaintext cannot be recovered. Revoke the grant and issue another if the response was lost.

Guest routes start at `/api/sales/browser-room-guests`. POST `redeem` accepts `secret` and `displayName`, then returns a scoped guest credential and lobby state. GET `{roomId}`, POST `{roomId}/media-token`, POST `{roomId}/consent`, and POST `{roomId}/leave` require the single `X-Sales-Room-Session` header. They resolve a hashed credential to persisted company/room/participant identity. Callers cannot supply an identity or choose a company. A redeemed guest receives no media token until admitted. Revocation clears the capability immediately; removal remains visibly pending until the provider confirms absence.

Invitations contain 256 random bits, guest credentials 512 random bits; only SHA-256 hashes are stored. Invitation redemption is single use and is not replayable after the secret is cleared. Do not place either secret in a query string or path. The later browser implementation must read the invitation from a URL fragment, immediately remove that fragment with `history.replaceState`, and exchange it by POST before enabling analytics. This prompt intentionally supplies no browser UI or emailed link. Responses use `Cache-Control: no-store` and `Referrer-Policy: no-referrer`; secrets are absent from audit records, outbox payloads and normalized webhook storage. Configure ingress/APM to exclude request/response bodies and `X-Sales-Room-Session`/Authorization headers from logging. Display names are self-asserted, not verified email identities.

Consent bodies contain `commandId`, participant `expectedVersion`, `purpose` (`ai_processing` or `retained_transcript`), `granted`, and the exact configured `noticeVersion`. Consent records are append-only and audited as the real member or guest. Withdrawal also schedules removal of agent participants. Prompt 7 must enforce the consent state before subscribing to each human microphone; this prompt starts no agent and captures no transcript.

### Provider work and operating configuration

Enable both `SalesBrowserRoom:Enabled` and `SalesBrowserRoom:Lifecycle:Enabled` only after the live release checks. Existing media credentials remain server-only. Lifecycle defaults are 20 non-ended rooms per company, 2 live rooms per company, 6 human participants including the organizer, 30 invitations per room, and notice version `browser-room-v1`. Invitation/room expiry is at most seven days; first media issuance shortens the live room to the configured media session limit (maximum 60 minutes). Issued join tokens are bounded by both participant and room expiry. Provider token refresh during an established connection makes active server-side revocation necessary even after the initial token's expiry.

The first guest release requires a `*.livekit.cloud` endpoint. The adapter uses Cloud's explicit token-revocation cutoff when removing a participant, including disconnected identities. Self-hosted deployments need equivalent revocation enforcement before this gate can be relaxed. See [LiveKit participant management](https://docs.livekit.io/intro/basics/rooms-participants-tracks/participants/).

Provisioning, expiry, removal, consent withdrawal and ending go through the existing company outbox. SQL serializable commands, unique binding/command indexes and optimistic versions protect room and admission decisions. Provider work has a three-minute receipt lease, a 90-second attempt deadline, a durable lease-expiry recovery message, and at most five automatic attempts with bounded backoff. Read-before-write reconciliation uses the original provision operation metadata and opaque room names. Exhausted work is returned as `needs_review` with a safe problem code. Correct provider configuration and POST its retry endpoint using a fresh command ID and current room version; termination retries are allowed even if new-room creation is disabled. Ending/ended rooms cannot be reopened by delayed provisioning or stale callbacks. Cleanup of a late orphan provider room remains safe after termination.

Configure LiveKit's webhook URL as POST `/api/sales/browser-room-provider/livekit`. The exact raw body and Authorization token are verified through the SDK signature, issuer and SHA-256 checksum checks. Only normalized bounded fields are stored, event IDs deduplicate deliveries, and timestamps order participant observations. Provider observations never admit a participant. A room-finished observation schedules inspection rather than blindly reopening or terminating local state. No webhook secret or raw signed body is persisted.

### Validation and remaining release gate

Fresh SQL Server migration history and a twice-applied representative upgrade passed on local SQL Server Express 16.0.1000.6. The upgrade preserves a pre-existing Teams call ID and evidence, enforces company/meeting foreign keys and unique room bindings, and rejects stale consent updates. Tests create isolated GUID-named databases and validate the exact test-name prefix before deleting them; no application database was migrated.

The preservation comparison covers 561 pre-existing files: none missing, no Teams implementation or historical migration changed. The seven changed baseline paths are the additive shared outbox/context/snapshot integrations and Prompt 1 media contracts, registration, adapter and tests. See [Prompt 2 comparison](browser-sales-room/prompt2-preservation-comparison.json).

Live Cloud room creation, a connected guest's actual disconnection and rejection of its previously issued token, webhook delivery, restart recovery, and Azure runtime checks remain **NOT RUN**: no authorized LiveKit credentials/project or Azure execution target are available in this task. Test doubles establish local state-machine behavior, not live media disconnection. Keep both route flags disabled until this provider verification and Prompt 1's remaining media/runtime gate pass. Later browser, scheduling and agent prompts are not part of this implementation.


Final local validation: the expanded affected suite passed **59/59** with no skips, including SQL/Teams migration and outbox regressions. After the final UTC deadline, credential redaction and delayed-expiry changes, the focused Release suite passed **28/28**. API Release and Web Release builds passed (API reports 34 existing warnings; Web reports none). The fresh database check also reports no pending EF model changes. See [sanitized check results](browser-sales-room/prompt2-check-results.json) for log locations and counts. Runs overlap; these are not 87 distinct tests.

The local compiler intermittently crashed in a migration-history project before test execution. Retrying with `DOTNET_PROCESSOR_COUNT=2`, `-m:1` and `-p:UseSharedCompilation=false` succeeded without changing historical source or disabling analyzers. For SQL tests set `VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION` in the test process to an authorized local test server; the tests create their own disposable databases. The final focused suite needs no LiveKit credentials and does not contact LiveKit.

## Prompt 3 — Browser scheduling alongside Teams (2026-09-09)

The Sales scheduling form now selects conferencing independently of the connected calendar. Browser invitations use the selected calendar without asking Microsoft 365 for a Teams conference or Google for a Meet conference. Existing calendar-online selection and Teams preparation remain available. Approval, canonical calendar/change outboxes and threaded confirmation delivery remain the external-side-effect boundaries.

### Compatibility and persistence

Migration `20260909161316_AddBrowserMeetingScheduling` adds explicit conferencing, browser-room binding and an encrypted invitation URL. Existing online Microsoft 365 invitations map to `teams`, online Google invitations to `google_meet`, and offline invitations to `none`; existing URLs and external IDs are preserved. Requests omitting the new conferencing field retain the legacy provider/online-flag mapping. No URL parsing selects a route. An optional `CommandId` makes repeated creation of the same command return the existing invitation; reuse with a changed payload is rejected.

A room can be bound to its approved invitation before a calendar event exists. Its meeting-session binding is nullable until ordinary presentation/session preparation attaches the real session. The migration refuses a downgrade while these pending bindings exist. Fresh SQL Server and twice-applied upgrade tests cover this schema and legacy Teams/Meet preservation. No application database was migrated.

### Link, change and recovery policy

Provisioning reuses one invitation-bound room. The original full URL is protected with ASP.NET Data Protection; its one-use guest secret is otherwise stored only as a hash. Plaintext links are excluded from outbox and audit payloads. The calendar body and approved confirmation receive the decrypted URL at delivery time. The stored public meeting URL excludes the fragment credential. Copy-link access is scoped to the active company and organizer, and requires the approved scheduled invitation. The human entry/exchange page is delivered by Prompt 4.

Rescheduling retains the original URL and extends eligible room, invitation and participant expiry through the canonical approved change. Redeemed or revoked grants are never restored; existing valid guest sessions retain their identity. A started or ended room cannot be rescheduled. Cancellation immediately closes room admission and queues provider termination before calendar cancellation. If calendar cancellation is ambiguous, the event can remain visible until reconciled, but room access stays denied. Changing conferencing after sending requires an approved cancellation and a new reviewed invitation.

Unknown calendar outcomes use an explicit read-only inspection through the existing outbox, not an automatic resend. Microsoft Graph inspection matches the stable transaction ID in a bounded selected-calendar window; Google inspection uses the deterministic event ID. A verified matching event can complete local delivery. Failure to verify an ambiguous create does not prove that no external effect occurred. Known failed approved changes can be retried by the organizer; retry delivery inspects the provider before repeating a write. The existing Prompt 2 room-operation recovery remains available for exhausted provisioning operations.

Provider references: [Graph calendar view](https://learn.microsoft.com/en-us/graph/api/calendar-list-calendarview?view=graph-rest-1.0), [Graph event transaction ID](https://learn.microsoft.com/en-us/graph/api/resources/event?view=graph-rest-1.0), and [Google event lookup](https://developers.google.com/workspace/calendar/api/v3/reference/events/get).

The company-authorized scheduling API is rooted at `/api/sales/browser-meeting-scheduling`: `GET readiness`, `GET invitations/{id}/link`, `POST invitations/{id}/reconcile?change={optionalId}`, and `POST invitations/{id}/changes/{changeId}/retry`. Recovery only queues canonical work with the actual approval and actor; it does not perform direct calendar writes from the controller.

### Configuration and release gate

Keep `SalesBrowserRoom:Lifecycle:Enabled` and `SalesBrowserRoom:Enabled` disabled until Prompt 4 supplies the human room experience and the outstanding LiveKit/runtime checks pass. Configure `SalesBrowserRoom:Lifecycle:PublicOrigin` to the application's HTTPS origin without a path, query or credentials. Readiness also requires valid LiveKit Cloud media configuration. All replicas must share a durable Data Protection key ring so links remain decryptable after restarts and deployments.

The current bounded lifecycle permits scheduling more than five minutes ahead, up to the seven-day credential horizon including one hour after the meeting, with the configured call-duration limit (default 60 minutes). Media admission opens 15 minutes before the scheduled start. These are enforced constraints, not a claim of arbitrary future booking support.

The scheduling/preparation UI follows the [generated reference](../design/references/browser-meeting-scheduling-reference.png). See the [UAT record](browser-sales-room/prompt3-uat.md), [preservation comparison](browser-sales-room/prompt3-preservation-comparison.json), and [check results](browser-sales-room/prompt3-check-results.json). Teams-specific baseline files and historical migrations are unchanged; shared scheduling contracts and services have additive route handling.

Live connected-calendar writes, customer email delivery, authenticated end-to-end browser operation, LiveKit media and Azure runtime checks were **NOT RUN**. No connected test account, authorized recipient or provider runtime target was supplied. Isolated provider tests and rendered-component screenshots do not establish live delivery or production readiness.

Final Prompt 3 verification passed **95/95 API tests** and **34/34 Web tests**, with no skips, in Release. These runs compile the latest API/Web changes and include affected Teams regressions and actual local SQL Server migration tests. Existing compiler/analyzer warnings remain. The preservation baseline contains 256 files: 233 unchanged, 23 shared scheduling/test/model files changed, none missing, and no Teams-specific file changed. Patch whitespace validation passed.

## Prompt 4 — Human browser meeting experience (2026-09-09)

Implemented separate guest `/sales/rooms/{roomId}` and organizer `/app/sales/rooms/{roomId}?companyId={companyId}` pages using the minimal public layout. Sales lead and meeting-preparation entry points now open the organizer room. The host surface manages the lobby, admit/deny/remove, individual one-use guest links and end-for-everyone through the existing versioned durable room APIs. Guests never receive company navigation or the organizer projection.

The prejoin flow supports display names, local microphone/camera preview and selection, speaker testing, microphone/camera-off participation and explicit sound unlock. The room uses the real pinned LiveKit browser SDK through a bounded JS module for direct WebRTC, screen sharing, participant tiles and cleanup. No audio/video is passed through Blazor or SignalR, and no room agent, AI processing or retained capture is started. Permission failure, missing devices, unavailable media, reconnecting, expired/removed access and lost controls have separate safe UI messages.

### Access and lifecycle

The invitation fragment is removed before framework/application scripts, both on initial navigation and same-page links. A successful exchange saves only the scoped credential in per-tab session storage; reload reuses the server-issued identity. Explicit leave clears it and requests durable revocation after stopping local tracks; leave requires a new guest link to return. A network reconnect or page reload retains valid admission. Browsers that reject session storage receive explicit guidance to keep the tab open. A new link can recover an expired saved session.

Host requests use `CompanyApiTransport`; guest requests use a separate HttpClient without company/authentication forwarding. Admitted guest status includes only admitted participant IDs, safe display names and media identities; lobby guests receive no audience. Member IDs, room operations, provider references and private workspace content remain absent. Server names are rendered as text; SDK metadata does not establish authority.

Successful status reads occur every three seconds. Unauthorized/expired status immediately stops local media on receipt. An independent JS watchdog stops preview/publication after 15 seconds without a successful control heartbeat, including circuit loss. Provider removal/end still uses Prompt 2's durable operation path and visibly retains pending/recovery state. Immediate provider disconnection is not claimed from local tests. Read-only status has a separate 1,200-request/minute IP budget so polling does not consume the existing 120/minute mutation/redemption budget. Both remain bounded; tune aggregate Web-host egress capacity only with the later load evidence.

Fresh authorized media tokens are obtained for join and explicit reconnect. Established connections use the SDK's provider token-refresh/reconnect behavior while application admission continues to be rechecked. No internal SDK engine mutation is used. Web Locks, where available, prevent two same-origin tabs from connecting the same identity simultaneously; the provider identity remains stable. Disposal fences late connection/device completions, removes listeners/elements, stops local tracks and releases the tab lock.

### Dependencies, verification and preservation

Vendored `livekit-client` **2.22.3**, Apache-2.0, under `src/VirtualCompany.Web/wwwroot/lib/livekit/`, with its license. Bundle SHA-256: `23E6B0966C20CCABA8D39343035FCC49E64AA46C93E772CA0A3F1B5A6D30B573`. The production page makes no floating CDN request. Sources checked during implementation: [package/version](https://www.npmjs.com/package/livekit-client/v/2.22.3), [Room API](https://docs.livekit.io/reference/client-sdk-js/classes/Room.html), [room events](https://docs.livekit.io/reference/client-sdk-js/enums/RoomEvent.html).

See the [UAT record](browser-sales-room/prompt4-uat.md), [generated reference](../design/references/browser-human-room-reference.png), [check results](browser-sales-room/prompt4-check-results.json), [preservation baseline](browser-sales-room/prompt4-baseline.json) and [comparison](browser-sales-room/prompt4-preservation-comparison.json). The Web build and focused tests compile the API/Web projects on Windows. Existing compiler/analyzer warnings remain. No schema change or migration was needed; all historical migrations and the snapshot are retained.

Chrome and Edge passed actual local guest-page checks with synthetic devices. Component fixtures cover organizer/admission and connected layouts; JS lifecycle tests cover cleanup/races. These are not live LiveKit acceptance. Host-plus-two-guests, actual screen sharing, remote revocation, Safari/iOS, TURN/corporate networks, Azure runtime and the preceding media soak remain NOT RUN. Browser/Teams flags, infrastructure, real invitations and mailbox delivery were not changed. Keep existing browser release gates in force until those external checks pass.

## Prompt 5 — Synchronized slides and private host controls (2026-09-09)

Browser rooms now reuse the approved presentation deck, runtime, SignalR groups and conductor contracts. Every admitted participant receives a room-scoped public snapshot and the exact current slide asset. The guest contract contains no private snapshot type, speaker notes, objective, transition plan or workspace storage key. The organizer receives a separate private snapshot with notes, objective, expected duration, transition cue, deck-preparation link, slide navigation, pause/resume and manual/assisted/autonomous modes.

Host presentation commands carry command ID, expected presentation version, sequence, deck ID/version and the persisted organizer participant ID/generation. The backend resolves company, user, room, meeting session and participant authority. Stale actor generations, stale versions and wrong decks fail before mutation. Guest hub connections validate the room-scoped capability on connect and again through durable acknowledgement work; wrong-room, removed and revoked sessions cannot join. Existing Teams stage grants and member-only private groups keep their original authorization paths.

Migration `20260909182543_AddBrowserRoomPresentationAudience` adds one company/room/participant row for each exact committed presentation version. It records deck/version, slide, sequence, participant generation, deadline, render, disconnect and audited override state. Capturing the admitted audience at the transition prevents late joins or a SignalR backplane from redefining which acknowledgements release that revision. Separate request/hub scopes read the same persisted state, providing replica-safe coordination without treating the process-local presence dictionary as distributed state. Exact browser acknowledgements refresh route-neutral in-memory presence after a live deck change so existing connections continue without a forced reconnect.

Audience readiness exposes pending, slow, disconnected, rendered, revoked and overridden states. The host sees counts and can explicitly continue after review; the override is persisted and audited. Reconnect rejoins the current authoritative revision. The render deadline and autonomous transition limits moved to validated `SalesPresentationConductor` options. When the neutral section is absent, explicit Teams timeout, transition and dwell values are retained through a tested compatibility fallback.

See the [Prompt 5 UAT record](browser-sales-room/prompt5-uat.md), [generated design reference](../design/references/browser-sales-room-prompt-5-reference.png) and [check results](browser-sales-room/prompt5-check-results.json). The final affected suites passed 34/34 API tests and 26/26 Web tests, including Teams stage/conductor/Web regressions. Chrome and Edge passed two-context desktop/mobile rendering checks. API and Web Release builds passed, and EF reports no pending model changes. SQL Server-only tests were skipped because no authorized test connection was configured; no application database was migrated. Live provider/Azure and remote-device acceptance remains subject to the existing browser-room release gates.
## Prompt 6 — Approved reusable narration (2026-09-10)

Implemented a company- and organizer-authorized narration workflow in meeting preparation. Draft scripts are extracted only from customer-visible slide text; private speaker notes, objectives and internal artifacts are excluded. The organizer reviews the source evidence and exact saved script, approves disclosure to the meeting's customer, and queues generation. Editing creates another immutable manifest rather than overwriting an approved script. English and Swedish are supported; the shared speech configuration supplies the model and voice.

### Persistence, reuse and release

Migration `20260910121327_AddApprovedSalesNarration` adds `sales_narration_revisions`, `sales_narration_segments`, `sales_narration_assets` and `sales_narration_attempts`. It preserves existing schema/history. The manifest retains the deck/version, source slide IDs/hashes/text, script/hash, audience, language, voice, model/configuration version and approval/revocation actors/timestamps. Audio is WAV PCM16 mono 24 kHz in the existing `ICompanyDocumentStorage` boundary; no audio frames enter SQL.

Assets use a company-scoped content key combining the source slide content revision, approved script hash, language, voice, speech configuration and audience-context hash. Deck IDs and versions remain in the manifest; unchanged content segments can be reused across new deck/meeting manifests for the same eligible audience without regenerating other slides. The audience hash includes customer, lead, contact and intended audience. Different companies, audiences, languages, voices or configurations cannot collide.

Only active company members who organized the meeting can prepare, decide or preview. Release checks validate the active processed deck, its source content, current audience, approval, revocation and expiry. Preview rechecks release after object I/O, verifies object size/hash, and refuses missing, corrupt or unapproved audio. Readiness also checks object existence/size without changing business state. Script/audio validation preserves word boundaries and numeric distinctions; mismatched or incomplete speech remains rejected. There is no live-speech fallback.

The cancellation-aware playback contract returns asset, slide, talking point, offset and turn generation with bounded PCM. Prompt 7 must connect it to the room-owned voice track and enforce live consent, audience acknowledgements and cancellation throughout publication. This prompt does not start a room agent.

### Durable work and configuration

Generation uses SQL-backed pending assets and attempt receipts as the durable work queue. Serializable claim/budget transactions and optimistic concurrency protect duplicate/concurrent claims. A receipt and reservation commit before the provider call. Each segment has at most three attempts; synthesis is bounded to 100 seconds, 1,200 output tokens and two minutes of PCM. Draft preparation is bounded to 100 talking points of 3,000 characters each. Provider/session work stays behind shared `IApprovedSpeechGateway` and the existing PCM gateway; Teams session behavior is unchanged.

No uncertain call is automatically repeated. Provider/worker ambiguity becomes `needs_review`; known usage remains attributed and unknown usage retains its conservative reservation. An explicit organizer retry requires acknowledgement of another possible generation charge. Retries add receipts instead of replacing them. Invalid sources/approvals are removed from active queue consideration; unapproved drafts cannot starve approved work.

Configure these server settings before generation:

- Existing `SharedRealtimeAgent` enablement, credentials, model and voice.
- `SalesNarration:Enabled` (default false).
- `SalesNarration:InputUsdPerMillion`, `OutputUsdPerMillion`: dated conservative maximum rates across relevant token modalities, not package prices.
- `SalesNarration:RateVersion`: rate date, currency/provider/model/region assumptions.
- `SalesNarration:MaximumCompanyDailyUsd`: configurable daily generation budget, default 5 USD.
- Existing private `CompanyDocuments:Storage` configuration. Production replicas must share durable private storage; no public storage URL is returned.

Budget admission reserves 20,000 input and 1,200 output tokens at configured maximum rates, including unresolved attempts. Returned token totals produce conservative generation estimates; retained modality usage supports later exact pricing/billing reconciliation. These estimates are deliberately labelled and must not be presented as invoices. A missing rate configuration disables generation with actionable feedback. Storage, transport and hosting costs are not included in generation estimates.

Synthetic audio releases expire 90 days after preparation. Cleanup deletes old unreferenced objects, including attempt paths, while keeping unexpired authorized references. Revocation immediately denies new previews; already downloaded audio cannot be recalled. Cleanup serializes reference checks and fences new preparation while deletion is in progress. Relational script/approval/usage manifests remain audit evidence under company data lifecycle controls; synthetic narration is distinct from recorded human calls.

### Routes and UI

Company-authorized API base: `/api/sales/narration`.

- `GET sessions/{sessionId}`: readiness and private revision history.
- `POST sessions/{sessionId}/prepare`: language plus optional reviewed slide/talking-point scripts.
- `POST revisions/{revisionId}/{approve|revoke|retry}`: expected version and retry-cost acknowledgement.
- `GET sessions/{sessionId}/revisions/{revisionId}/segments/{segmentId}/preview?audienceId=...`: authorized binary preview.

The Web audio proxy forwards authenticated company context; audio travels over HTTP, not a Blazor circuit. Responses are no-store/no-referrer and do not expose object keys, provider sessions or credentials. The existing meeting preparation page contains source/script review, save-as-new-revision, explicit approval, preview, retry/revoke, budget/failure feedback and separate generation/preview-delivery metering. Preview minutes measure bytes served, not time actually heard.

### Verification and preservation

- API build: passed; Web build: passed. Existing compiler/analyzer warnings remain.
- Initial affected API suite: **29 passed**, including narration, shared speech comparison, deck/runtime/conductor and fresh/upgrade SQL Server tests.
- Final focused backend suite after readiness/queue/retention fixes: **17 passed** (overlaps the previous suite).
- Existing human-room/Teams Web suite: **15 passed**; existing preparation-page/client suite: **15 passed**; final new narration component suite: **4 passed**.
- Four real shared-speech generations: **passed**, two English and two Swedish segments, no retry, no microphone/customer data. Both language manifests reached ready; repeated preparation and authorized preview added no provider requests.
- English generated 5.55 seconds; Swedish generated 7.10 seconds. Conservative generation estimates from returned usage total **0.01018 USD**, not invoice-reconciled or a full-call cost. Current model/rate documentation: [Realtime model](https://developers.openai.com/api/docs/models/gpt-realtime-2.1-mini), [pricing](https://developers.openai.com/api/docs/pricing), [conversation events](https://developers.openai.com/api/docs/guides/realtime-conversations).
- Chrome desktop/mobile captures pass overflow and script-visibility checks and decode/play real generated audio. Interaction assertions run against the actual Blazor component with isolated HTTP responses. Full authenticated deployed-app UAT and independent human listening assessment are not claimed.

See [UAT record](browser-sales-room/prompt6-uat.md), [live generation](browser-sales-room/prompt6-live-generation.json), [browser checks](browser-sales-room/prompt6-browser-checks.json), [preservation baseline](browser-sales-room/prompt6-baseline.json) and [comparison](browser-sales-room/prompt6-preservation-comparison.json). All 787 baseline paths remain; 784 have identical hashes. Three baseline paths have additive integrations: meeting preparation, Sales registration and the EF snapshot. No Teams-specific implementation or historical migration was changed. Additional shared integrations are Operations gateway registration, DbContext tenant sets/filters, Web client/proxy registration and isolated test wiring.

No application database, live Teams flag/resource, invitation or customer delivery was changed. Local SQL tests create and delete validated GUID-named disposable databases on SQL Server Express; the same migration path applies to Docker SQL Server. Existing LiveKit/Azure/device/soak release gates remain in force. Implementation of narration preparation does not assert room rollout readiness.

Final checks: the existing browser-room upgrade regression also passed with the expanded migration chain, retaining all original Teams/tenant/concurrency assertions. EF reports no pending model changes. The upgrade fixture gained only the parent tables needed by the additive narration foreign keys. No application database was migrated.

## Prompt 7 — One consent-aware speaking agent (2026-09-10)

The browser room now has one durable, room-owned agent lifecycle. Organizer-only commands start and stop a renewable lease, invoke an approved narration segment, ask a typed grounded question, and release an approved answer to speech. Serializable start and command receipts prevent duplicate ownership; lease owner, agent generation, and turn generation fence stale workers and late output. Restart recovery stops old sessions and requires an explicit organizer restart.

The scoped room worker creates one LiveKit agent media connection and one manually committed shared Realtime transcription session. Per-track .NET speech detection runs before external forwarding with bounded 240-ms pre-roll, 600-ms trailing silence, track/generation/timestamp provenance and overlap metadata. Only admitted human microphone tracks enter the allowed set; agent, system, and screen-share audio remain excluded by the existing transport. Raw audio stays in bounded memory and is cleared on revocation, removal, end, failure, or worker shutdown.

All admitted humans must grant transient AI-processing consent. A new admission or withdrawal stops and fences AI while the human call and manual presentation remain available. Transcript retention is a separate participant choice; a completed transcript is stored only when that participant's retained-transcript consent and consent version still match. Typed questions remain available during AI fallback.

Both output paths use the same published track. Narration reopens Prompt 6's approved asset and rechecks revision, audience, customer, current slide/talking point, render acknowledgements and turn. Answers use the existing question-answering service and selected Sales presenter, require evidence and explicit customer-stage release before synthesis, then recheck release and turn before the first output frame. Unexpected provider audio is blocked. Human speech cancels output at local detection and advances the durable turn fence.

Migration `20260910132423_AddConsentAwareSalesRoomAgent` adds the room lease/health/usage fields plus `sales_room_agent_speech` and consent-versioned `sales_room_agent_transcripts`. It is additive, tenant-keyed, and has no raw-audio column. Existing rooms receive `not_connected` voice health and turn generation 1.

The host room UI follows the [generated reference](../design/references/browser-sales-room-agent-reference.png): it shows consent readiness, independent voice health, duration and estimated spend, explicit start/present/stop actions, typed questions, private evidence, paused fallback, and received/detected/forwarded/provider-billed metrics. Provider duration is shown only when reported; the UI explicitly withholds savings claims without billing evidence.

Verification: focused backend tests passed (23), browser component tests passed (11), Teams/shared media regressions passed (61), SQL Server fresh/upgrade migration checks passed, EF reports no pending model changes, and API/Web builds pass with existing warnings. Chrome desktop/mobile captures pass semantic assertions and overflow checks. See the [Prompt 7 UAT record](browser-sales-room/prompt7-uat.md), [browser checks](browser-sales-room/prompt7-browser-checks.json), [desktop capture](browser-sales-room/prompt7-host-agent-1440.png), and [mobile capture](browser-sales-room/prompt7-host-agent-390.png).

No LiveKit credentials were available, so a real multi-human call and customer-audible end-to-end latency remain unrun and are not claimed. The preserved live-room gate from Prompt 1 still applies. No Teams-specific source, test, deployment script, setting, or historical migration changed; the focused Teams/shared media suite passed without enabling or invoking Teams.

## Prompt 8 — Dialogue, interruption and human takeover (2026-09-10)

Implemented a durable Sales-owned floor controller with host/preauthorized-co-host authority, human/agent/pending/overlap states, manual/assisted/autonomous policy, turn and response generations, exact presentation binding, resume offset, and per-client playback-stop receipts. Address detection proposes a turn; the backend authorizes it. Human-to-human speech remains human-owned, overlap waits, and only evidence-backed released answers can enter the shared agent track.

Speech start now cancels live generation and approved playback directly in the input loop before utterance completion or transcription. It also flushes adapter output, preempts narration, persists the new generation and publishes a client-detach request. Takeover restores manual presentation control, fences queued and late work, and persists across reconnect. Resume rechecks controller authority, consent, presentation version and required render acknowledgements, then continues the current approved narration from its saved PCM offset. Autonomous mode advances only through the bounded existing conductor and pauses on missing new-slide readiness or release.

Migration `20260910150531_AddSalesRoomFloorControl` is additive and EF reports no pending model changes. Focused floor/VAD/lease/conductor tests passed 23/23; affected Teams interruption/conductor/shared-media tests passed 37/37; Web component tests passed 11/11; browser JavaScript tests passed 11/11. API and Web builds pass. Chrome desktop/mobile semantic and overflow checks passed. See the [UAT record](browser-sales-room/prompt8-uat.md), [check results](browser-sales-room/prompt8-check-results.json), [browser checks](browser-sales-room/prompt8-browser-checks.json), [desktop capture](browser-sales-room/prompt8-host-floor-1440.png), and [mobile capture](browser-sales-room/prompt8-host-floor-390.png).

No LiveKit credentials or SQL Server test connection were configured. The real three-human English/Swedish call, physical echo/device and organizer-loss scenarios, customer-audible p95 latency, and SQL Server fresh/upgrade execution remain unrun and are not claimed. No application database or live Teams resource was changed.


## Prompt 9 — consented capture and reviewed closing

Implemented browser provenance, consent/lease-fenced capture, idempotent end/closing, reviewed reasoning, customer/private review UI, canonical approval delivery revalidation and retention cleanup. See [implementation, UAT and live limits](browser-sales-room/prompt9-uat.md), [browser checks](browser-sales-room/prompt9-browser-checks.json) and [Teams preservation comparison](browser-sales-room/prompt9-preservation-comparison.json). Backend regressions: 67 passed including both SQL Server tests; Teams/expanded retention: 57 passed; Web: 16 passed. Live full-call/provider and actual mailbox send verification remain unrun.

## Prompt 10 — complete-call cost and Azure capacity benchmark

Implemented the frozen 30-minute three-arm benchmark, speech-policy matrix, fail-closed load/spend gates, cost/resource analyzer, production OpenTelemetry measurements and disposable Sweden Central App Service template. The analyzer covers all-attempt provider/LiveKit cost, incremental and fixed Azure cost, 1/10/40/80 cache views, billed-audio VAD savings, p95 quality targets, baseline-subtracted CPU/memory and bounded 1/5/10/25/50 capacity with stop/drain rules.

The live run is blocked because no named authorized isolated Azure target/separate load generator, LiveKit credentials, permitted synthetic participant credentials, or explicit spend/concurrency envelope was supplied. All full-call cost, resource, latency and capacity fields remain null; no microbenchmark estimate was relabelled. See the [Prompt 10 benchmark report](browser-sales-room/prompt10-benchmark.md) and [machine-readable result](../../artifacts/sales-room-full-call-benchmark/2026-09-10-blocked-v3/report.json).

Seven offline accounting/protocol tests pass, the Bicep template compiles, and the focused API/agent/narration/VAD/telemetry plus shared Teams selection passes 63 tests with one credential-gated live narration test skipped. No deployment, provider call, load, send, database change or Teams traffic occurred. No production concurrency, quota or budget default changed because measured evidence is unavailable.

## Prompt 11 — operations hardening and Teams reactivation evidence

Added a disabled-by-default production browser deployment template with a dedicated two-or-more-instance App Service topology, managed identity, versionless Key Vault secret references, `/health/ready`, Application Insights, Log Analytics, diagnostics and an availability check. Capacity, duration, current provider rates and spend budgets are required deployment inputs because Prompt 10 has no authorized measured load result. The companion script compiles and runs Azure what-if before an explicitly requested apply.

Browser lifecycle and agent options now support live drain and emergency disable. Admission rejects disabled/draining work. Active workers cancel media, stop the durable lease and mark queued/processing speech interrupted. Per-call duration/audio/spend, company monthly spend and company/global active-agent limits use SQL state and provider-reported billed audio/tokens. Rate evidence must include a current UTC review date and reference; stale or absent evidence fails enabled agent readiness.

The coordinator no longer stops all active agents at process startup. It preserves unexpired leases owned by other instances, reaps only expired leases during periodic reconciliation, handles SQL concurrency losers as fenced, and releases locally owned work during graceful shutdown. Generation/owner/turn checks remain on renewal, transcript handling and output publication, so abandoned speech is never replayed after replacement.

The `sales-browser-room` readiness check reports admission/drain, media/provider/speech health, lifecycle ambiguity, active/stale ownership, unhealthy voice, limits and rate evidence without tenant identifiers. New low-cardinality meter series cover admissions, lifecycle, latency, ownership, quota and estimated spend; existing series cover audio stages, tokens, frame drops, reconnects, failures, sessions and queue depth.

Verification results:

- API build passed with existing warnings. Focused Prompt 11 policy/telemetry/fencing/configuration tests passed **23/23**.
- Broader browser, narration, closing, presentation and Teams API regression selection passed **184**, with six environment-gated tests skipped in that run. The same five SQL fresh/upgrade/data-preservation tests then passed against disposable LocalDB databases: browser room **2/2**, narration/Teams **3/3**.
- EF reported no pending model changes. Browser Bicep and all three preserved Teams Bicep templates compiled.
- Teams SDK `Microsoft.Graph.Communications.Calls.Media` **1.2.0.17950** passed its reviewed 92-day gate at 70 days. Disabled package generation passed with synthetic identifiers and produced a three-entry local artifact with SHA-256 `97453722844DFB9516E017C880ED350A9ADFCDB3B3D3950401BCC318A8259094`.
- The focused browser/Teams/localization Web selection passed **57/57**. The initial broad Web run passed 648/650 and exposed a missing Swedish presenter-count placeholder plus an unrelated Dashboard fixture failure. After fixing and verifying the Teams-facing localization defect, the broad rerun passed **649/650**; `DashboardPageTests.Dashboard_renders_required_section_order_for_action_first_layout` remains failed because its fixture does not register `IMonthlyWorkspaceApiClient`, outside the browser/Teams path.

See the [operations runbook](../browser-sales-room-operations.md), [UAT record](browser-sales-room/prompt11-uat.md), [machine-readable checks](browser-sales-room/prompt11-check-results.json), [baseline](browser-sales-room/prompt11-baseline.json) and [comparison](browser-sales-room/prompt11-preservation-comparison.json). The authorized full browser journey, real decks/devices/networks, provider/app loss, long call, load/soak and measured p95 remain blocked by the exact prerequisites in the UAT record. No deployment, live provider/Teams call, external invitation/follow-up or application database change occurred.
