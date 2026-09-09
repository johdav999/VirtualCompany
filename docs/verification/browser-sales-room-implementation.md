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
