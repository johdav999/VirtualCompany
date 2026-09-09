# Sales meeting realtime voice pilot

## Scope and operating posture

The realtime voice path is an optional extension to the Sales meeting workflow. It is disabled by default and has no effect on slides, typed and host-mediated questions, capture, closing, review, approvals, or delivery. Voice may read meeting state and request a grounded answer; it cannot mutate canonical Sales data, make a customer commitment, send a message, or bypass proposal, approval, and outbox enforcement.

The first adapter is `browser_webrtc`. It creates a server-mediated OpenAI WebRTC call and returns only the provider's SDP answer. The long-lived OpenAI API key remains on the API server. The shared provider contract also supports an ephemeral client secret for adapters that need one, but this adapter does not return a client credential.

The browser route is not a Teams media bot. The separately gated Teams route is an application-hosted raw-media implementation and therefore has the specialist Windows, Azure, networking, certificate, media-processing, and scaling requirements described in [Microsoft's real-time media concepts](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/real-time-media-concepts) and [application-hosted media requirements](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots).

## Approval and permissions prerequisites

Before enabling the pilot in any environment:

1. Obtain written product-owner authorization for the pilot and explicit legal/privacy approval for consent wording, transcript handling, and the existing meeting retention policy.
2. Approve the media route for that environment. `browser_webrtc` is intended for a controlled browser-hosted pilot. A Teams deployment must select the separately implemented `teams_application_hosted` route (or a future certified provider); setting flags alone never turns the browser adapter into a Teams media bot.
3. Confirm that the existing company membership policy is appropriate for the pilot population and restrict feature configuration to that population at the deployment layer.
4. Provision the provider credential only in server-side secret configuration. Never put it in `appsettings.json`, a Teams manifest, browser storage, JavaScript, logs, or support exports.
5. Apply the `AddSalesMeetingRealtimeVoicePilot` database migration and verify `/health/ready` before enabling users.

## Configuration

Set the API server's `OPENAI_API_KEY` secret, then explicitly enable both independent capability gates:

```json
{
  "SharedRealtimeAgent": {
    "Enabled": true,
    "BaseUrl": "https://api.openai.com/v1/",
    "Model": "gpt-realtime-2.1-mini",
    "Voice": "marin",
    "TranscriptionModel": "gpt-realtime-whisper",
    "TimeoutSeconds": 20
  },
  "SalesMeetingVoice": {
    "Enabled": true,
    "PilotApproved": true,
    "MediaRoute": "browser_webrtc",
    "MaximumSessionMinutes": 30,
    "MaximumReconnects": 2,
    "MaximumAudioSeconds": 1800,
    "MaximumInputTokens": 50000,
    "MaximumOutputTokens": 10000
  }
}
```

The browser route uses the server-mediated `POST /v1/realtime/calls` WebRTC flow. The Teams route uses the server-side Realtime WebSocket transport, with PCM16/24 kHz at the provider boundary and deterministic conversion at the Teams bridge. Consult the current [Realtime API reference](https://platform.openai.com/docs/api-reference/realtime) and [realtime model documentation](https://developers.openai.com/api/docs/models/gpt-realtime) before changing the transport or model. Provider credentials remain server-side for both routes. If a future adapter connects from an untrusted client, use short-lived credentials as described by the [Realtime client-secret API](https://developers.openai.com/api/reference/python/resources/realtime/subresources/client_secrets/methods/create).

## Runtime contract

- `GET /api/sales/meeting-sessions/{sessionId}/voice/status` always exposes a typed fallback status, including when the feature is disabled.
- Starting voice requires an active company membership, a company-owned Sales agent, explicit `Granted` meeting consent, an unexpired meeting retention window, both enabled gates, and healthy provider/media adapters.
- Events are normalized behind `IRealtimeAgentSessionGateway` and `IMeetingMediaAdapter`. Durable event receipts enforce provider-event idempotency and sequence ordering.
- Participant speech persists the exact slide and talking-point marker before the meeting enters `Interrupted`. A completed participant question uses the existing grounded Q&A service and then persists `Answering`, `Resuming`, and the restored `Presenting` state.
- The approved realtime registry exposes `presentation.get_current_slide` and `presentation.search_slides` as reads, the five versioned presentation mutations as execute tools, and `ask_grounded_question` as a recommendation tool. Execute tools remain blocked unless organizer-approved autonomous mode is active; invented JavaScript names such as `nextSlide()` are rejected and audited.
- Barge-in uses provider voice-activity detection plus the explicit response-cancel endpoint. A cancelled response does not create a second question or command.
- Session duration, audio time, token use, reconnects, provider retry, event/body size, tool schemas, and returned tool data are bounded.
- No raw audio is persisted. Questions and approved transcripts continue to use the meeting's retention boundary. Revoking consent terminates the provider call, blocks new media, records audit evidence, and leaves already-retained evidence governed by `RetentionUntilUtc`.

## Costs and limits

Realtime pricing and model limits can change. Do not encode a monetary estimate in the product. Before each pilot phase, record the provider's current input-audio, output-audio, transcription, and token prices in the rollout decision, then set conservative limits for the expected participant count. Monitor session starts/failures, processed/deduplicated events, rejected tools, fallbacks, provider rate-limit responses, duration, and token counters. The application allows one active voice session per user and meeting and performs only one bounded retry for a short provider rate limit.

## Failure and risk controls

- Provider outage, invalid response, timeout, quota, disconnect, or usage exhaustion changes only the voice state. The API returns a safe summary and typed workflows continue.
- Provider payloads are bounded and normalized. Provider secrets, SDP, raw audio, participant questions, and detailed provider errors are not written to application logs or audit metadata.
- An authenticated meeting client can submit provider data-channel events, so every reachable tool remains read/recommend only and is independently company-scoped by the backend.
- Realtime answers inherit the existing evidence boundary. Unsupported factual claims become a private follow-up rather than customer-visible truth.
- The browser media route must be assessed for browser permissions, device selection, echo cancellation, accessibility, consent indication, and supported-network behavior during UAT.

## Rollback and emergency disable

Set either `SalesMeetingVoice:Enabled=false` or `SharedRealtimeAgent:Enabled=false` and restart the API. This immediately prevents new calls while status endpoints report the typed fallback. For an incident, terminate active provider calls, then disable both flags. Do not roll back Prompts 1–8, remove their endpoints, or delete meeting evidence. Keep voice audit records and event receipts until the approved meeting retention process permits removal.

The migration is additive. A schema rollback is only appropriate after all active calls have ended and the retained audit requirements have been reviewed; normal feature rollback is configuration-only.

## Teams application-hosted audio route

The Teams route is separate from `browser_webrtc`. It uses `Microsoft.Graph.Communications.Calls.Media` and `Microsoft.Skype.Bots.Media` on the same Windows media-host instance that creates the call. The Graph join/answer payload uses `appHostedMediaConfig`; enabling the Teams flags never sends browser SDP to Teams.

Required `TeamsPresenter` settings are `Enabled`, `CallControlEnabled`, `AudioEnabled`, `MediaRoute=teams_application_hosted`, `MediaRouteApproved`, a non-placeholder `CallControlHostId`, `BotApplicationId`, `MediaHostPublicIp`, `MediaHostServiceFqdn`, `MediaHostInternalPort`, `MediaHostPublicPort`, and `MediaCertificateThumbprint`. The certificate private key remains in the Windows certificate store. Microsoft requires a directly reachable per-instance public IP/port and a supported Azure Windows Server guest; Azure Web Apps are not supported.

Incoming and outgoing audio is PCM S16LE, 16 kHz, mono, exactly 20 ms/640 bytes per frame. SDK callbacks copy the unmanaged frame immediately into a bounded channel and dispose the SDK buffer. Raw audio, SDP, access tokens, media configuration blobs, and detailed provider payloads are never persisted or logged. Unmixed speaker identity is disabled; an optional provider media-source id is retained only when exactly one active source is supplied, with no voice-based identity inference.

When the durable Graph callback reaches `connected`, the pinned-host coordinator creates one server-side realtime PCM session, activates the durable voice session, attaches the application-hosted socket, and starts the participant/audio and agent/audio pumps. Provider output is converted from PCM16/24 kHz into exact Teams frames. Normalized speech, transcript, tool, usage, cancellation, and error events re-enter `SalesMeetingRealtimeService`, so durable receipts, quotas, consent, interruption markers, grounded answers, and presentation-tool authorization are shared with the browser route. A participant speech-start event cancels the provider response and Teams output once before the persisted interruption workflow continues. Terminal call callbacks stop both resources.

Every attach, input frame, and output frame revalidates the durable company/session/call/voice/agent/consent/host binding. Consent revocation, call termination, host mismatch, malformed frames, expiry, buffer pressure, or reconnect exhaustion stops or degrades audio while typed questions and manual slide controls remain available. Emergency rollback is: stop admitting Alex, set `AudioEnabled=false`, terminate active calls through the existing forced-termination path, drain the pinned host, and then set `MediaRouteApproved=false`.

Presentation mode defaults to `manual`. Only the meeting organizer may select `assisted` or `autonomous`, and changing mode preempts narration immediately. Assisted mode returns recommendations for confirmation. Autonomous mode issues only the registered `presentation.*` commands against the active deck and optimistic version, then waits for the matching stage render acknowledgement before narration. Until Prompt 5 connects the Blazor stage-presence contract, autonomous narration fails closed with `stage_disconnected`.

The SDK package must be upgraded at least every three months, with a new build and approved two-participant audio test. Validate frame drops, setup time, time to first audio, jitter/backpressure, barge-in cancellation, provider failures, usage and forced termination without recording raw media. Microsoft’s current hosting and SDK constraints are documented in [application-hosted media requirements](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots).

## Side-panel design gate

The typed Web client is registered and ready for the private side panel, but controls are intentionally not rendered until the existing Sales meeting stage and side-panel reference images receive explicit user approval as required by `docs/design.md`. After approval, the side panel should show consent, connecting/active/reconnecting/degraded/stopped state, start/stop, mute/cancel, usage/limit messaging, and an always-visible typed fallback. No voice state or private status belongs on the customer stage.
