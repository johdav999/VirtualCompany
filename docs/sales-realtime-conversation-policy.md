# Sales Realtime conversation policy — Prompts 1–4

Updated 2026-09-27. Prompts 1–4 provide backend authorization, bounded provider context, validated conversational bridges, and authorized presentation-continuation tools. The provider-adapter section below records the Prompt 2 boundary; the Prompt 4 section describes the current implementation. Live microphone/provider acceptance remains unverified.

## Rollout

`SalesRoomAgent:HybridConversationEnabled` defaults to `false`. No checked-in or running configuration was enabled. Enabling it applies the new policy to browser-room confirmed spoken questions, typed questions, answer authorization, playback and provider tool proposals. It does not activate microphones or change Teams/guided-work profiles.

With the flag disabled, existing entry points retain their behavior. With it enabled, changing presentation mode fences queued/in-flight responses and clears pending floor turns. Turning the flag off during an already protected playback fails that playback closed; subsequent operations use the legacy path. Usage, consent, meeting schedules and approval records are not reset.

## Authority and ownership

`AgentConversation` is the reusable provider-neutral domain controller. It models presenting, interpreting, retrieval, approval, answer/bridge speech, waiting, paused and stopped phases. Versioned transitions distinguish proposals from authorization and consumption. It stores reference IDs for heard turns and actually played responses, not transcripts or audio.

`SalesRoomConversationPolicy` is the sales profile. It loads company-scoped room, floor and participant records from relational persistence. Bindings include company, agent, room/session, participant generation, worker owner/generation, room turn, floor response generation, mode/policy version and presentation checkpoint. Fresh checks enforce admission, consent, live ownership, expiry, emergency stop and room budgets. Existing worker/company-budget and approved-speech checks remain mandatory in addition to this policy.

The existing room/floor/session rows remain the authoritative business state. The controller is operation-local and must not be used as a second persisted floor. Input binds before retrieval and is rechecked after the provider wait. Playback binds before generation and rechecks before output, every 200 ms of submitted PCM, and before completion. Those checks add database work; 200 ms of submitted PCM is not a measured wall-clock cancellation guarantee, and already delivered audio cannot be recalled.

## Mode and continuation rules

- Autonomous permits supported complete or partial answers; partial answers retain their limitations and the existing exact-text speech validation.
- Assisted/manual require the existing current host authorization and released answer. They never allow automatic bridges or model-initiated resume.
- A continuation requires a fresh, unambiguous continue proposal tied to a played response and the current checkpoint, plus current controller rights. Silence, unknown intent/mode, a new question, expiry, consent withdrawal, stale generations and takeover cannot authorize it.
- Reply context expires after at most 45 seconds; proposals expire after 15 seconds. Rechecking at consumption prevents using a previously permitted proposal after authority changes.
- Browser conversation sessions expose only grounded-question, continuation, and wait requests. Arguments contain no authority or slide position. Continuation additionally requires the current completed bridge, a fresh confirmed reply and authorized host/co-host control.

## Recovery and persistence

No EF model/schema change or migration is required. Existing durable command, speech and floor generations retain business history and deduplication. New pending conversational proposals are deliberately ephemeral: worker replacement discards them, and a recovered controller has no played-response/reply context. It cannot infer permission to resume from the old database floor. A fresh conversational exchange is required.

Normalized turns and completed playback are now connected to the controller. Successful continuation uses the existing durable operation and speech queue. Pending proposals are not reconstructed from persistence or replayed after restart.

## Prompt 2: persistent provider adapter

The flag now selects the server-side PCM conversation profile for browser rooms. The application still owns local speech detection, explicit audio-buffer commit, final-transcript confirmation, and every `response.create`; provider VAD cannot independently interrupt playback or create an answer. Far-field noise reduction is requested but is not a semantic speech/intent guarantee. The existing room media transport remains the only publication path. Prompt 2 does **not** yet publish conversational provider audio or implement generated follow-ups; that release boundary belongs to Prompt 3.

The shared Application interface accepts confirmed turns, bounded response requests, completed function-call results and one continuation per submitted result, played-response context, and cancellation/truncation. The Operations gateway owns the persistent WebSocket and provider JSON. Responses default to out-of-band (`conversation: none`), so unheard text/audio does not enter provider history. A tool proposal may use the default provider conversation only as a text-only response; completed tool results are correlated by call ID before a single explicit continuation. A caller that deliberately uses the default provider conversation must correlate the exact audio output item and measured played PCM position before truncation. Only a completed, registered, correlated function call with bounded JSON arguments can be submitted; argument deltas are not executable. No Sales room tools are registered in this stage.

Room context includes only final-confirmed, retained participant transcripts after fresh company/participant/track/lease/floor checks, plus answers persisted as spoken after the existing speech publisher completes. A short reply from the sole connected human can enter that context for 45 seconds after a played answer, even without a question mark or agent name; this stage does not interpret the reply or resume a deck. Non-retained utterances, interrupted or withheld answers, cross-room state, and old provider sessions do not enter the new context. Each provider session bounds context, event IDs, tool calls, response/audio bytes and duration. Expiry/transport loss pauses the AI lane with typed/manual controls; a host restart creates empty provider context without clearing durable room usage, consent, or ownership state. There is no automatic replay or quota reset.

Current Realtime sessions are capped at 60 minutes. The separate room audio/spend limits continue to apply and valid worker leases continue to renew while provider and answer operations wait. This adapter is not a second browser audio connection, and enabling it alone will not make Alex hold a free-form spoken conversation. That user-visible behavior requires the grounded-answer/bridge release work in later prompts.

## Verification

Deterministic state tests cover mode/approval, evidence denial, played context, duplicate/stale versions, expiry, restart, cross-company/session/participant/owner/checkpoint changes, and tool registration. Relational playback tests exercise the production worker with the flag on/off, partial approved answers, changed answers, mode changes during generation/streaming, revoked participants and takeover. See `verification/speech-aware-interruption/uat.md` for executed results and live-test limitations.

## Prompt 4: authorized presentation tools

The room worker records the actual completed bridge and authoritative floor binding. The shared reasoner interprets the next confirmed, retained utterance against the question, released answer and **actually played** follow-up. A contextual affirmative after an ambiguous or multi-part offer is not sufficient: remain waiting. No phrase table, timed consent or model-supplied checkpoint drives continuation.

A text-only Realtime request proposes one of `ask_grounded_question`, `request_presentation_continuation`, or `wait_for_participant`. Each tool accepts an empty JSON object. The backend checks the proposal against independently interpreted current intent and the trusted participant/session binding. A newer confirmed utterance discards the pending proposal; overlapping/pending transcription, active playback and stale floor versions prevent execution. Question requests reuse grounded question handling; wait leaves the already paused presentation unchanged. Neither can also resume narration.

Continuation uses the existing `SalesRoomAgentService` resume command and shared conversation policy. A serializable transaction, SQL Server execution strategy, concurrency versions and existing unique command keys atomically persist the checkpoint, `conversation_resume` operation, audit event and narration queue item. The command ID derives from company, room, worker generation and confirmed input turn, not provider call ID. Repeated calls/reconnects cannot create another action; changed call IDs do not bypass deduplication. No schema or migration is added.

Execution rechecks controller rights, both floor and presentation mode, participant generation, consent, ownership/lease, room/session expiry, audio/spend limits, pending/newer questions, completed bridge, checkpoint version, audience readiness and approved playable narration. Actual playback rechecks mode/floor/consent and the approved asset through the existing worker. The saved narration offset survives answers, bridges and their interruptions; answer-audio duration never becomes the narration offset. Narration completion still advances through the existing conductor.

Missing narration or audience readiness leaves an actionable pause in existing host controls. Tool results describe actual acceptance/failure, not claimed playback completion. Results go back as `function_call_output`; a text-only, tools-disabled follow-up waits until the tool response ends. Its text is not broadcast. Only accepted approved narration may then own room audio. The adapter ignores argument-completion fragments and dispatches only validated complete function-call items.

See [Prompt 4 verification](verification/sales-realtime-conversation-prompt4/uat.md) for executed coverage and deployment limitations. The flag remains default-off; no meeting, calendar, microphone, usage history, or customer permission was changed.

## Prompt 5: organizer status, rollout and recovery

The host-only agent endpoint now projects a conversation phase from the authoritative room, floor, current-generation speech queue, latest question and provider configuration. The meeting UI shows listening for a reply, checking approved sources, answering, awaiting host approval, queued presentation continuation and paused/expired states in plain language. A completed follow-up is shown as a listening window only while the current worker lease, turn and response generation match and 45 seconds have not elapsed. This status is observational; it is not permission to speak or resume. The guest endpoint carries no conversation status or private evidence.

Roll out per environment, not per customer account: leave `SalesRoomAgent:HybridConversationEnabled=false` until the browser-room agent, `SharedRealtimeAgent` provider profile/credentials, approved narration, grounding sources, participant consent and existing room budgets have been validated with an authorized test audience. First enable the feature in a test environment; check the organizer panel says the conversation is available before trying an autonomous question. If enabled but the shared provider is disabled or credentials are missing, the panel explicitly says Realtime conversation is unavailable and retains approved-answer/manual controls. A configured provider outage, quota, room expiry and a paused agent are separate statuses. Do not use a model/tool response or UI text as evidence that playback or continuation completed; check the current floor and speech receipts.

For rollback, set `SalesRoomAgent:HybridConversationEnabled=false` on all instances. A worker that started with the conversation profile detects the change, cancels current speech, fences queued conversational follow-ups and stops its agent lease; the human meeting and manual slide controls continue. The organizer can restart the agent under the legacy approved-answer profile after checking consent and budgets. Changing the flag does not grant consent, unmute a device, clear spending, replay speech, change a meeting schedule or remove approval requirements. For a broader AI incident, use `SalesRoomAgent:EmergencyDisabled=true` as documented in [browser room operations](browser-sales-room-operations.md). Restart/reconnect never reconstructs an old conversational reply window; ask a fresh question after recovery. See [Prompt 5 UAT](verification/sales-realtime-conversation-prompt5/uat.md).
