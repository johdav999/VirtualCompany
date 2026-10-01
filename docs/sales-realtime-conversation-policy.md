# Sales Realtime conversation policy — continuous dialogue

Updated 2026-09-28. The original conversation pack provides backend authorization, bounded provider context, validated conversational bridges, and authorized presentation-continuation tools. Prompts 1–6 of `agentdialog-prompts.md` add persistent input, opt-in semantic completion, model-selected release-checked conversational audio, fresh authorized playback tools, grounded concise answers, truthful status and bounded recovery. Synthetic provider routing/audio has been verified; live microphone/browser voice acceptance remains unverified.

## Agentdialog prompt 6: status, presence and bounded recovery

The semantic profile projects listening and paused-presentation availability throughout a current
authorized lease, without an earlier answer or 45-second reply gate. Checking sources, answering,
awaiting approval, queued narration, presenting, reconnecting, stopped, quota and room expiry remain
distinct. Output is called presenting only when current speech/health support it, not merely because
an old floor row names the agent. A recoverable rejected answer is a listening problem, not a departure.
The organizer sees microphone-muted guidance and safe recovery actions; guests receive no host
evidence or private diagnostics. Manual/assisted answer approval and manual slide controls remain.

Participant tiles derive from actual media participants, not API labels. An idle/muted agent remains
visible while connected; a real media disconnection removes the tile. Reconnection does not render
a substitute tile or claim speech was played. UI availability is observational, never authorization.

The provider's documented maximum session duration is 60 minutes. The new profile requests at most
55 minutes (or the remaining room duration), allowing bounded replacement independently of meeting
and accumulated-audio limits. Defaults permit two recovery attempts per worker lifetime and a
one-second backoff. Expiry or input-provider loss closes the old transport/session before retry.
Fresh authority checks cover tenant/owner lease, active organizer membership and configured presenter,
agent, admitted organizer, all AI consent, autonomous mode, room time, deployment state and budgets;
checks repeat after backoff and provider connection. Failed checks or exhausted retries require
operator recovery; they never silently grant permission or reset accounting.

Recovery increments room-turn and floor-response fences, discards pending speech, clears pending
floor turns and retains the narration checkpoint in a paused deck. Unrecorded received/forwarded
input and reported provider usage are saved before replacement; historical counters are retained.
Old provider context, proposals and buffered output are not replayed. Fresh listening resumes only
after a successful authorized connection. A fresh explicit resume/start request is still necessary.
Host stop, takeover, consent withdrawal, expired ownership or rollback wins over reconnect.

Configuration validates the semantic/hybrid dependency, documented eagerness values and bounded
session/retry/backoff settings at startup and through the runtime admission policy. Both opt-ins
remain default-off; Teams and other profiles are not migrated. The legacy local/manual-commit
profile retains its answer-bound reply window; it does not advertise continuous semantic dialogue.
No schema, consent, calendar or customer-permission change accompanies this implementation.

See [OpenAI session bounds](https://developers.openai.com/api/docs/guides/realtime-conversations)
and [prompt-6 verification](verification/agentdialog-prompt6/uat.md). Prompt 7 remains the full
live microphone/meeting acceptance gate. Buffered speech release/latency still follows prompt 3
below; recovery does not weaken independent content validation or lower-mode approval.

## Agentdialog prompt 5: concise grounded answers inside dialogue

The full confirmed utterance remains the stored question and the start of the search query.
Reference resolution adds at most three prior delivered turns and 3,000 characters from the
same room, agent-owner generation, participant generation and current retention-consent version.
Only completed answer/follow-up/conversational speech receipts are history; queued, withheld,
interrupted or other participants' speech is excluded. Context retains input/question/speech IDs
but is explicitly untrusted and non-citable. It must not substitute a guessed company topic.

The existing shared reasoning gateway composes source-cited claims, normally 40–60 words.
An independent structured review accepts only relevant claims supported by their cited evidence
with essential qualifications. The backend keeps whole accepted claims and one short qualification;
it never publishes the model summary or cuts qualifiers to meet a word count. Independent rejection
can yield a partial answer or the existing safe no-evidence limitation, not a company overview.
Company-specific onboarding, capabilities, pricing and customer facts retain the verified-source
path. Social and explicitly general education retain prompt 3's independently checked policy.

The private evidence panel retains citation IDs/titles; identifiers are not added to spoken text.
Eligible complete/partial answers release automatically only in autonomous mode; manual and
assisted modes retain versioned review plus the explicit speech action. Exact text matching,
answer version, floor, consent and owner fences still apply during playback. Source content and
knowledge content versions are fingerprinted in an audit snapshot and checked through current
agent-scoped retrieval before synthesis and again before publication. Removed/changed sources,
changed answers or unavailable checks withhold audio. Legacy answers without that snapshot must
be asked again; approval is not a bypass. There is no schema change or permission expansion.

Actual completion is recorded before an optional follow-up can become spoken history. Its semantic
proposal/validation has a three-second optional deadline: failure or timeout must not rewrite the
already delivered answer as interrupted, and no bridge or 45-second timer grants dialogue authority.
A new question or fresh, explicit presentation command remains eligible under prompts 1–4.
Recoverable retrieval/provider, source-check and speech-matching failures terminate only that
speech item, leave the healthy semantic session listening, and never replay an old answer.
Consent withdrawal, ended meetings, invalid authority, quota and transport loss remain fenced.
Expired meeting capture purges independent claim-review runs as well as answer runs.

Provider function calls remain proposals executed by the application's authorization/release path,
consistent with [OpenAI Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations#function-calling).

## Agentdialog prompt 4: playback inside persistent dialogue

For the opt-in semantic profile this section supersedes the legacy answer/bridge-gated
continuation rules below. Realtime can select `start_presentation`, `pause_presentation`,
`resume_presentation` and `read_presentation_state`. Empty arguments only: identities, generations,
assets and positions always come from the backend. Per-turn tool lists restrict commands to the
current controller, autonomous mode, playable approved narration and audience readiness. Guests
retain eligible questions but gain no playback authority. Offers, silence, thanks and ambiguous
multi-option assent are not continuation permission; the model must clarify or wait.

A fresh confirmed command needs no preceding answer or 45-second reply window. Start resets
slide/point to 1/1 and offset zero using canonical presentation navigation; resume uses the durable
floor checkpoint through the same domain restoration as host resume. Pause stops narration, not
the listening connection. Serializable operations, unique turn-derived command IDs, audit events
and the existing durable speech queue prevent replay. Execution and publication recheck authority,
consent, mode, generations, limits, audience and approved assets. Tool results say queued, not played.
After stage publication, a new dialogue command waits up to five seconds for real render receipts;
missing readiness still fails closed. Notification failure cannot roll back a committed queue item.

Narration, grounded answers and dialogue share one asynchronous output ownership gate. Old cleanup
must finish before a new output can publish. Confirmed interruptions flush once, preserve the
narration checkpoint and hand off ownership; answer audio never replaces that checkpoint. LiveKit
delivery receipts subtract queued duration before flush and remain generation-bound afterward.
Unknown transport delivery conservatively saves zero additional progress, not invented playback.
These are server playout receipts, not proof of sound from a participant's loudspeaker.

The model receives current slide/point/state plus bounded completed narration receipts. Text is
included only for fully played segments starting at zero; partial playback is represented by offsets,
not guessed transcript fragments. Generated replies use an isolated output-only provider conversation
so their exact item can be truncated at measured delivered milliseconds on interruption. That session
is discarded afterward; unheard words never enter persistent listening context. Tool proposal output
is bounded to 512 tokens; incomplete proposals cannot execute an action.

See [prompt-4 verification](verification/agentdialog-prompt4/uat.md). No schema, deployment flags,
meeting schedule, participant consent or quota changed. Assisted/manual and the legacy profile retain
their approval and host controls.

## Agentdialog prompt 3: permitted dialogue

This section supersedes the earlier staged limitations below for the opt-in semantic profile.
With both conversation flags enabled and autonomous mode selected, Realtime automatically selects
`respond_social`, `explain_general`, `clarify_input`, `wait_for_participant`, or `ask_grounded_question`.
The input-completeness safety check remains, but no separate intent classifier pins the tool.
Arguments must be empty. Trusted current input, company, participant/retention consent, room, owner,
mode and floor generations bind execution. A proposal expires after 15 seconds; duplicate/stale
calls cannot act again. Results distinguish accepted processing from completed speech.

Social, explicitly general educational, and clarification replies require no previous question row.
The shared adapter creates a short-lived output-only Realtime session using the configured model,
bounded confirmed/actually-played context and current input. Complete audio and transcript must
match turn/response/item and finish successfully. PCM is held in memory (at most 20 seconds), with
a 30-second generation deadline. An independent shared reasoning call validates the actual words,
their relevance, content class and absence of company/customer facts or commitments. A model label
alone cannot authorize release. Company questions use the existing grounded answer and approval path.

Only after those checks does the durable `conversation` speech row claim the current floor and
publish through room media. Consent, mode, lease, tenant, retention, generations and limits are
rechecked before and during output. The unique existing company/room/command index deduplicates
execution. Checkpoints and usage are preserved; no schema change is needed. Manual/assisted
approval behavior and the legacy profile remain unchanged.

Rejected/incomplete output is not spoken or added as heard context. The room stays ready/listening,
with an actionable clarification/typed-question status. Genuine consent, meeting, ownership,
transport and quota stops still take precedence. This path is **buffered Realtime audio**, not
unrestricted direct streaming and not a silent fallback to the old bridge/TTS path. The metric
`conversation_audio_buffered_to_release` includes generation plus independent validation latency.
New presentation actions belong to prompt 4, not this change. See
[prompt-3 evidence](verification/agentdialog-prompt3/uat.md).

## Rollout

`SalesRoomAgent:HybridConversationEnabled` defaults to `false`. No checked-in or running configuration was enabled. Enabling it applies the new policy to browser-room confirmed spoken questions, typed questions, answer authorization, playback and provider tool proposals. It does not activate microphones or change Teams/guided-work profiles.

With the flag disabled, existing entry points retain their behavior. With it enabled, changing presentation mode fences queued/in-flight responses and clears pending floor turns. Turning the flag off during an already protected playback fails that playback closed; subsequent operations use the legacy path. Usage, consent, meeting schedules and approval records are not reset.

## Authority and ownership

`AgentConversation` is the reusable provider-neutral domain controller. It models presenting, interpreting, retrieval, approval, answer/bridge speech, waiting, paused and stopped phases. Versioned transitions distinguish proposals from authorization and consumption. It stores reference IDs for heard turns and actually played responses, not transcripts or audio.

`AgentConversationSession` holds the established session identity independently of presentation/output turns. Each fresh input creates an interpreting controller from current authority; no preceding answer or reply timer is required. It retains at most 128 heard and 128 played reference IDs, while the shared provider adapter bounds the actual conversation context. Replacement ownership starts empty. Old action controllers still fail when their original binding changes. Existing relational room/floor records own leases, presence, output and checkpoints; no schema change is needed.

`SalesRoomConversationPolicy` is the sales profile. It loads company-scoped room, floor and participant records from relational persistence. Bindings include company, agent, room/session, participant generation, worker owner/generation, room turn, floor response generation, mode/policy version and presentation checkpoint. Fresh checks enforce admission, consent, live ownership, expiry, emergency stop and room budgets. Existing worker/company-budget and approved-speech checks remain mandatory in addition to this policy.

The existing room/floor/session rows remain the authoritative business state. The controller is operation-local and must not be used as a second persisted floor. Input binds before retrieval and is rechecked after the provider wait. Playback binds before generation and rechecks before output, every 200 ms of submitted PCM, and before completion. Those checks add database work; 200 ms of submitted PCM is not a measured wall-clock cancellation guarantee, and already delivered audio cannot be recalled.

## Mode and continuation rules

- Autonomous permits supported complete or partial answers; partial answers retain their limitations and the existing exact-text speech validation.
- Assisted/manual require the existing current host authorization and released answer. They never allow automatic bridges or model-initiated resume.
- Semantic-profile continuation requires a fresh, unambiguous resume proposal and current controller rights/checkpoint, not a preceding answer. Silence, unknown intent/mode, expiry, consent withdrawal, stale generations and takeover cannot authorize it.
- Fresh autonomous semantic input remains available throughout the authorized session. Individual proposals expire after 15 seconds; listening does not. The legacy local/manual-commit profile alone retains answer/bridge-bound continuation and its 45-second window.
- Semantic tools include authorized playback and bounded dialogue as described above. Legacy tools remain grounded-question, answer-bound continuation and wait. Neither surface accepts model-supplied authority or slide positions.

## Recovery and persistence

No EF model/schema change or migration is required. Existing durable command, speech and floor generations retain business history and deduplication. New pending conversational proposals are deliberately ephemeral: worker replacement discards them, and a recovered controller has no played-response/reply context. It cannot infer permission to resume from the old database floor. A fresh conversational exchange is required.

Normalized turns and completed playback are now connected to the controller. Successful continuation uses the existing durable operation and speech queue. Pending proposals are not reconstructed from persistence or replayed after restart.

## Prompt 2: persistent provider adapter

The hybrid flag selects the server-side PCM conversation profile for browser rooms. By default the application owns local segmentation and explicit audio-buffer commit. The additional semantic-input opt-in described below replaces that commit path, not the response/release policy. Far-field noise reduction is requested but is not a speech/intent guarantee. The existing room media transport remains the only publication path. Neither input profile publishes unchecked conversational provider audio.

### Agentdialog prompt 2: semantic input completion

`SalesRoomAgent:SemanticConversationInputEnabled` defaults to false and requires `HybridConversationEnabled`.
The shared PCM request opts in using `SemanticVadEagerness`; valid values are `low` (Sales default),
`medium`, `high`, and `auto`. It requires `ConversationProfile=true` and `ManualInputCommit=false`.
The provider receives `audio.input.turn_detection={type:semantic_vad,eagerness:low,create_response:false,interrupt_response:false}`.
The adapter rejects manual commits for that session. Legacy browser, Teams and guided-work profiles are unchanged.

The existing per-track speech classifier and nonstationary-onset safeguard open a forwarding window
with bounded pre-roll (500–600 ms with supported settings). Once open, PCM continues through pauses;
600 ms of local silence no longer ends or commits a turn. DTX gaps receive bounded synthetic silence,
counted as forwarded input, not speech. Only one participant/track window is forwarded at a time.
Overlap, sequence loss, track replacement, consent/generation changes invalidate the affected pending
window and clear uncommitted provider input. Late completions cannot borrow another participant's identity.

Normalized provider item IDs and session-relative audio offsets correlate speech-start, speech-stop,
commit and final transcription, in any arrival order. All four are required, exactly once, within a
valid provenance window. Raw PCM remains ephemeral. Pending items, reference history and pre-roll are
bounded; a missing completion times out after `MaximumUtteranceSeconds + 12` (42 seconds by default).
Timeout/failed transcription discards that turn but leaves the healthy connection listening. Transport,
ownership, consent, room end and operating-limit failures retain their existing terminal behavior.

Provider completion still passes model-based completeness and current authority checks. Incomplete
input waits in the existing bounded speech-turn buffer; it must not become a generic company answer.
Provider speech-start alone never stops narration. Response creation, tool authorization and approved
audio publication remain application-controlled; free conversational replies belong to later prompts.

This durable sales-question flow requires transcript retention. When a participant declines it, their
microphone is not forwarded through the semantic question profile and no transcript is stored. Human
calling, manual slides and explicitly submitted typed questions remain the supported path. This is
not an implicit retention grant or a claim that non-retained live question answering is supported.

See [semantic-input verification](verification/agentdialog-prompt2/uat.md) for replay and worker evidence.

The shared Application interface accepts confirmed turns, bounded response requests, completed function-call results and one continuation per submitted result, played-response context, and cancellation/truncation. The Operations gateway owns the persistent WebSocket and provider JSON. Responses default to out-of-band (`conversation: none`), so unheard text/audio does not enter provider history. A tool proposal may use the default provider conversation only as a text-only response; completed tool results are correlated by call ID before a single explicit continuation. A caller that deliberately uses the default provider conversation must correlate the exact audio output item and measured played PCM position before truncation. Only a completed, registered, correlated function call with bounded JSON arguments can be submitted; argument deltas are not executable. No Sales room tools are registered in this stage.

Room context includes only final-confirmed, retained participant transcripts after fresh company/participant/track/lease/floor checks, plus answers persisted as spoken after the existing speech publisher completes. Fresh autonomous input from the sole connected human, or explicitly addressed input without overlap, can enter context immediately and throughout the session. Current played answers/follow-ups provide optional bounded interpretation context. With no preceding answer, social/control turns are interpreted without inventing factual questions; initial generated replies and new presentation tools are delivered by later prompts. Non-retained utterances, interrupted or withheld answers, cross-room state, and old provider sessions do not enter this context. Each provider session bounds context, event IDs, tool calls, response/audio bytes and duration. Expiry/transport loss pauses the AI lane with typed/manual controls; a host restart creates empty provider context without clearing durable room usage, consent, or ownership state. There is no automatic replay or quota reset.

Current Realtime sessions are capped at 60 minutes. The separate room audio/spend limits continue to apply and valid worker leases continue to renew while provider and answer operations wait. This adapter is not a second browser audio connection, and enabling it alone will not make Alex hold a free-form spoken conversation. That user-visible behavior requires the grounded-answer/bridge release work in later prompts.

The worker retains its media connection during idle listening and after narration completes or pauses. Long provider waits recheck consent, room expiry, ownership and operating limits at lease renewal; terminal failures cancel pending work. Recoverable turn rejection preserves the session. The room loop also checks the actual provider-session expiry. Transport connection/disposal receipts must be distinguished from inferred UI status and from audible output. See [persistent lifetime verification](verification/agentdialog-prompt1/uat.md).

## Verification

Deterministic state tests cover mode/approval, evidence denial, played context, duplicate/stale versions, expiry, restart, cross-company/session/participant/owner/checkpoint changes, and tool registration. Relational playback tests exercise the production worker with the flag on/off, partial approved answers, changed answers, mode changes during generation/streaming, revoked participants and takeover. See `verification/speech-aware-interruption/uat.md` for executed results and live-test limitations.

## Prompt 4: authorized presentation tools

The room worker records the actual completed bridge and authoritative floor binding. The shared reasoner interprets the next confirmed, retained utterance against the question, released answer and **actually played** follow-up. A contextual affirmative after an ambiguous or multi-part offer is not sufficient: remain waiting. No phrase table, timed consent or model-supplied checkpoint drives continuation.

A text-only Realtime request proposes one of `ask_grounded_question`, `request_presentation_continuation`, or `wait_for_participant`. Each tool accepts an empty JSON object. The backend checks the proposal against independently interpreted current intent and the trusted participant/session binding. A newer confirmed utterance discards the pending proposal; overlapping/pending transcription, active playback and stale floor versions prevent execution. Question requests reuse grounded question handling; wait leaves the already paused presentation unchanged. Neither can also resume narration.

Continuation uses the existing `SalesRoomAgentService` resume command and shared conversation policy. A serializable transaction, SQL Server execution strategy, concurrency versions and existing unique command keys atomically persist the checkpoint, `conversation_resume` operation, audit event and narration queue item. The command ID derives from company, room, worker generation and confirmed input turn, not provider call ID. Repeated calls/reconnects cannot create another action; changed call IDs do not bypass deduplication. No schema or migration is added.

Execution rechecks controller rights, both floor and presentation mode, participant generation, consent, ownership/lease, room/session expiry, audio/spend limits, pending/newer questions, completed bridge, checkpoint version, audience readiness and approved playable narration. Actual playback rechecks mode/floor/consent and the approved asset through the existing worker. The saved narration offset survives answers, bridges and their interruptions; answer-audio duration never becomes the narration offset. Narration completion still advances through the existing conductor.

Missing narration or audience readiness leaves an actionable pause in existing host controls. Tool results describe actual acceptance/failure, not claimed playback completion. Results go back as `function_call_output`; a text-only, tools-disabled follow-up waits until the tool response ends. Its text is not broadcast. Only accepted approved narration may then own room audio. The adapter ignores argument-completion fragments and dispatches only validated complete function-call items.

See [Prompt 4 verification](verification/sales-realtime-conversation-prompt4/uat.md) for executed coverage and deployment limitations. The flag remains default-off; no meeting, calendar, microphone, usage history, or customer permission was changed.

## Legacy conversation pack: organizer status, rollout and recovery

The host-only agent endpoint projects state from the authoritative room, floor, speech queue, question and provider configuration. In the legacy local/manual-commit profile only, a completed follow-up is shown as a reply window while the worker/turn/response match and 45 seconds have not elapsed. The semantic profile uses continuous availability as described above. Neither status grants speech permission. The guest endpoint carries no private conversation status or evidence.

Roll out per environment, not per customer account: leave `SalesRoomAgent:HybridConversationEnabled=false` until the browser-room agent, `SharedRealtimeAgent` provider profile/credentials, approved narration, grounding sources, participant consent and existing room budgets have been validated with an authorized test audience. First enable the feature in a test environment; check the organizer panel says the conversation is available before trying an autonomous question. If enabled but the shared provider is disabled or credentials are missing, the panel explicitly says Realtime conversation is unavailable and retains approved-answer/manual controls. A configured provider outage, quota, room expiry and a paused agent are separate statuses. Do not use a model/tool response or UI text as evidence that playback or continuation completed; check the current floor and speech receipts.

For rollback, set `SalesRoomAgent:HybridConversationEnabled=false` on all instances. A worker that started with the conversation profile detects the change, cancels current speech, fences queued conversational follow-ups and stops its agent lease; the human meeting and manual slide controls continue. The organizer can restart the agent under the legacy approved-answer profile after checking consent and budgets. Changing the flag does not grant consent, unmute a device, clear spending, replay speech, change a meeting schedule or remove approval requirements. For a broader AI incident, use `SalesRoomAgent:EmergencyDisabled=true` as documented in [browser room operations](browser-sales-room-operations.md). Restart/reconnect never reconstructs an old conversational reply window; ask a fresh question after recovery. See [Prompt 5 UAT](verification/sales-realtime-conversation-prompt5/uat.md).
