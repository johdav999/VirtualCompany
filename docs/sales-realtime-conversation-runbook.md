# Sales realtime conversation operations

## Agentdialog prompt 5 verification and pilot

Use the existing opt-in semantic/autonomous configuration, retained-transcript permission and
authorized indexed documents. No deployment setting, source grant, meeting schedule or quota
was changed by this implementation. Rebuild/restart through the normal development procedure
before a live pilot; the implementation did not restart running hosts.

Ask a complete onboarding question, hear its supported steps and concise missing-detail statement,
then ask a reference-bearing follow-up or explicitly resume the deck. Verify the exact captured
question, fresh agent-scoped knowledge citations, approved answer version and completed speech
receipt. Repeat in assisted/manual mode: no automatic audio; use Speak approved answer.

Inspect `answer_source_changed`, `answer_changed`, `evidence_check_unavailable`,
`speech_provider_unavailable`, `question_rejected`, `question_authorization_changed` and
`provider_unavailable` separately from insufficient evidence. A safe no-evidence limitation contains
no factual claims. An old approved answer without the new source snapshot requires a fresh question.
Do not reset capture, grants or usage history to bypass these checks.

After a recoverable failure, Alex should still be listening with the same owner/input connection;
the failed row must be withheld, never replayed. An optional follow-up may be skipped on timeout
(`optional_follow_up_timeout`) without invalidating delivered answer audio. Changed consent/mode
or authority still requires the appropriate current authorization, not an automatic retry.

[Prompt 5 verification](verification/agentdialog-prompt5/uat.md) uses isolated SQL-backed indexed
retrieval and production release/playback paths with deterministic model and media seams.
It proves evidence/version enforcement and server playout accounting, not RØDE capture accuracy,
live model relevance, speaker audibility or real tenant Graph availability. Complete those pilot
checks before treating the new flow as production-accepted.

## Agentdialog prompt 4 verification and pilot

The semantic autonomous profile now offers spoken start/restart, pause, resume and state tools.
Keep the two opt-in flags and existing consent, approved deck, audience and budget prerequisites
described below. No deployment was enabled or restarted by this implementation.

With an authorized isolated audience: Start agent → greet Alex → ask to start presenting → ask
a substantive question → hear the grounded response → clearly ask to continue. Verify the durable
narration offset, one output at a time, continuing microphone input and no replay. Separately test
pause/restart, a guest command, mode downgrade, duplicate calls, ambiguous assent and withdrawal
of consent. A backchannel may select wait without pausing the presentation.

`presentation_queued` confirms persistence, not audible completion. Inspect the current-generation
speech row and media completion; allow the newly published slide to render. Missing audience
readiness must not be overridden implicitly. `presentation_queued_notification_pending` means the
queue is saved but notification failed; the worker polls durable speech and rechecks readiness.
The `playback_notification_failed` failure metric records that case. Do not retry with invented IDs.

The provider-only diagnostic now accepts `--live --playback` on the existing probe command.
It verifies actual provider greeting audio and start/pause/resume selection against synthetic
state, without executing meeting commands or using a microphone. See
[prompt-4 evidence and remaining live checks](verification/agentdialog-prompt4/uat.md).

## Agentdialog prompt 3 verification and pilot

The semantic profile now includes model-selected social/general/clarification proposals and
independently checked buffered Realtime speech. For an authorized isolated pilot, enable both
`SalesRoomAgent:HybridConversationEnabled` and `SalesRoomAgent:SemanticConversationInputEnabled`,
then start a new agent session in autonomous mode with AI-processing and transcript-retention consent.
This task does not enable flags, restart hosts, activate a microphone, or alter an existing room.

First welcome Alex before asking any factual question. Then ask a general educational question,
an ambiguous question and a company-specific question. Verify generated speech for permitted
dialogue, a clarification rather than a generic company overview, and the grounded path for facts.
Check that rejection leaves Alex connected and exposes a retry/clarification message. Repeat after
a long pause, then test mode/consent changes during generation and ensure no pending audio escapes.
Presentation tools are now included by agentdialog prompt 4 above; host controls remain available.

The audio path is `buffered_realtime`: generated PCM is ephemeral, correlated with its completed
transcript, independently validated, then released to room media. No automatic TTS/bridge fallback
is used. `conversation_audio_buffered_to_release` measures buffering plus validation latency.
Speech rows preserve outcome/class/validation references and existing room accounting preserves
reported token usage. Reconcile shared reasoning/provider usage as usual; this is not an invoice.

Optional bounded provider-only probe (uses the existing `OPENAI_API_KEY`, never prints it):

```powershell
dotnet run --project scripts/AgentDialogueProbe/AgentDialogueProbe.csproj -- --live
```

This billable synthetic greeting probe does not capture a microphone, access customer data, write
PCM, publish room audio or bypass release checks. It measures provider routing and candidate
generation only, not independent validation or browser audibility. See
[prompt-3 evidence](verification/agentdialog-prompt3/uat.md) for the measured result and remaining gate.

## Release gate and configuration

Keep `SalesRoomAgent:HybridConversationEnabled=false` in general deployments until the live acceptance gate in
[Prompt 6 evidence](verification/sales-realtime-conversation-prompt6/uat.md) passes. This is a
deployment flag, not tenant-specific isolation: pilot in an isolated test deployment. It
does not change consent, permissions, approved source selection, or room limits.

The local Development profile enables the flag for the user-authorized conversation pilot
as of 2026-09-27. This is not production rollout approval or a completed live-audio acceptance
gate. See [reply-routing fix evidence](verification/sales-realtime-conversation-reply-fix/uat.md).

Required configuration: `SalesRoomAgent:Enabled`, `SharedRealtimeAgent:Enabled`, server-side
`SharedRealtimeAgent:ApiKey` (or `OPENAI_API_KEY`), approved speech gateway, shared reasoning
gateway, media transport, and current provider rate card. Keep the existing call/monthly
spend limits, input/output audio limits and `MaximumSessionMinutes`. Never reset counters
or reopen an expired room to make a test pass. Health reports local configuration; it is
not a successful provider handshake or an audibility check. Do not log secret values.

The selected agent needs explicit access to the company's indexed documents and approved
meeting knowledge. Check indexing failures, selected folder scope, meeting source bindings,
and the active deck's approved narration revision and generated audio. Connecting OneDrive
alone is not approval for every agent or proof that all documents were indexed. A missing
fact must remain a limitation, never be supplied from unrestricted model knowledge.

## Protocol and version manifest

- Conversation policy: `AgentConversation.PolicyVersion=1`.
- Reasoner capability/schema: `1.0.0`; intent prompt `1.0.2`; bridge proposal prompt `1.0.1`;
  independent semantic validation prompt `1.0.0`. AI run records contain the prompt versions.
- Room profile: `sales_browser_room_conversation` with `ConversationProfile=true`. Record
  deployed Git revision and configured model identifiers alongside UAT results; room session
  instructions are code-versioned, not an external mutable prompt.
- Repository defaults: `SharedRealtimeAgent:Model=gpt-realtime-2.1-mini`,
  `TranscriptionModel=gpt-realtime-whisper`. Do not silently migrate models. Verify account
  entitlement and the deployed configuration with a bounded authorized handshake.
- Server WebSocket PCM: signed 16-bit little-endian, mono, 24 kHz. The default conversation
  profile uses explicit local commits and `turn_detection=null`. The opt-in semantic-input
  profile below uses provider-owned completion with no manual commits. Both use far-field
  reduction and application-controlled responses.
- Tool responses are text-only, correlated, complete `response.output_item.done` calls.
  Deltas never execute. `function_call_output` carries the real backend result; continuation
  waits for `response.done`. Tools have empty arguments: no model-supplied tenant, slide,
  approval, or room IDs. Generated provider audio cannot bypass the released speech lane.
- Approved factual answers and independently validated short bridges use exact-text speech
  generation and release checks. This is hybrid conversation, not unrestricted direct audio.

Protocol review on 2026-09-27 used the official
[Realtime conversations guide](https://developers.openai.com/api/docs/guides/realtime-conversations):
explicit response creation, completed function calls, tool results, and session lifecycle.
The documented session maximum is 60 minutes; PCM session lifetime is bounded to that and
the remaining room time. Socket expiry is a safe pause/restart, not seamless context migration.

## Safe restart, rollout and rollback

Agentdialog prompt 2 adds a separate default-off setting:
`SalesRoomAgent:SemanticConversationInputEnabled=true`, alongside `HybridConversationEnabled=true`.
Start testing with `SalesRoomAgent:SemanticVadEagerness=low`. No running or checked-in environment
was enabled by implementation. Keep the configured Realtime and transcription models unchanged.
Validate entitlement/compatibility in the authorized test deployment before rollout. Invalid settings
fail validation; a provider rejecting semantic VAD must not be reported as a working semantic session.

With this opt-in, confirm the session update contains semantic VAD, `create_response=false` and
`interrupt_response=false`. Input sends append/clear only, never `input_audio_buffer.commit`.
A new profile or eagerness requires a new agent session; a detected configuration change safely
stops the existing worker. To fall back, disable only the semantic flag and explicitly restart the
agent: this restores the documented local/manual-commit profile, not equivalent pause handling.

Require AI processing and transcript-retention permission for spoken durable questions. Retention
opt-out skips this participant's semantic forwarding; use the human call or typed questions instead.
Do not change consent to make a test pass. Failed transcription or a missing final turn is recoverable
without reconnecting; retry with a fresh utterance. A provenance change/overlap deliberately drops the
ambiguous turn. Timeouts default to 42 seconds and do not reset usage or authorize an answer.

Observe `sales.browser_room.semantic_input` stages `capture_started`, `turn_completed`, `routing`,
the `semantic_capture_to_final` and existing first-answer-frame latency stages, forwarded/billed audio,
and conversation failures `semantic_turn_timeout`, `semantic_capture_timeout`, `semantic_transcription_failed`.
Metrics contain no transcript/audio. Test the real RØDE microphone with a mid-sentence pause, fan/echo,
two speakers, retention withdrawal and a second question; deterministic replay is not live audibility proof.

The schema was rechecked against the [official VAD guide](https://developers.openai.com/api/docs/guides/realtime-vad)
and [speech-boundary server events](https://developers.openai.com/api/reference/resources/realtime/server-events#input_audio_buffer.speech_started).

1. Use a newly authorized test meeting with approved narration, a consented test audience,
   and the agreed spend ceiling. Confirm the chosen microphone, output device and Enable
   sound. Never activate another person's microphone or consent for them.
2. Enable the flag only in that test deployment. Start the agent explicitly; verify the
   autonomous mode and host-only conversation status. Assisted/manual still require approval.
3. On disconnect/lease expiry, stop and restart through host controls once access, consent,
   room time and quota are valid. New ownership generations invalidate old proposals. Do not
   replay queued callbacks, copy provider context, or patch generation fields in SQL.
4. For rollback set the flag false. Existing conversation workers stop and queued bridges
   or conversation-resume commands are fenced. Start the agent explicitly to establish the
   legacy approved-answer session. If configuration is not hot-reloaded, use the normal
   drain/restart procedure. Human calling/manual slides remain available.
5. Preserve speech/audit history, approved answers, usage and schema. There is no rollback
   migration or quota reset. Rollback does not undo an already accepted presentation action.

After Start agent in the semantic profile, fresh autonomous utterances enter conversation processing
without a prior answer or reply window. Playback completion or pause does not disconnect the agent.
Fresh authorized start/pause/resume tools and spoken social replies are integrated. Proposals still
expire after 15 seconds. The legacy local/manual-commit profile alone retains answer-bound
bridge/continuation and its 45-second window. A new question supersedes obsolete proposals.

A completed answer adds optional context even when a generated follow-up is skipped.
Fresh autonomous intent interpretation precedes the legacy question heuristic:
an explicit request to continue is not a factual source query merely because it starts with
“Can you”. Clear thanks can receive a bounded, independently validated generated follow-up;
it never authorizes continuation. Each played social reply is correlated to its own confirmed
input turn. The semantic profile stays listening; only the legacy profile opens a reply window. Only autonomous mode permits automatic bridges
and voice-requested continuation. Changing the feature flag requires a fresh worker/provider
session in either direction; the old socket cannot acquire a different profile mid-session.

## Diagnostic order

### Continuous dialogue recovery (agentdialog prompt 6)

Deploy the rebuilt API and Web together. Validate flags before admitting a pilot audience:

| `SalesRoomAgent` option | Default / allowed values |
|---|---|
| `HybridConversationEnabled` / `SemanticConversationInputEnabled` | Both default false; semantic true requires hybrid true |
| `SemanticVadEagerness` | `low`; `low`, `medium`, `high`, `auto` |
| `ProviderSessionMinutes` | 55; 1–55 (provider sessions are bounded to 60 minutes) |
| `MaximumProviderSessionRecoveries` | 2; 0–3 per worker lifetime; zero disables retry |
| `ProviderRecoveryBackoffSeconds` | 1; 1–10 |

Invalid combinations fail startup validation and block runtime admission. These are bounded
operating settings, not new room/audio/spend allowances. Preserve existing compatible models.
Check [official session documentation](https://developers.openai.com/api/docs/guides/realtime-conversations)
when changing the adapter; do not assume a socket can survive for the whole meeting.

On provider input-session expiry/loss, the old connection is closed and pending speech is fenced.
The host panel says reconnecting; human calling continues and the deck stays paused. Successful
replacement rechecks current authority/consent/presenter/mode/budget before listening returns.
Usage and the actual checkpoint remain. Ask again or explicitly request resume; do not expect
an unheard answer, old command or narration to replay. A genuine lost media participant disappears;
an idle connected participant stays visible even with no active audio track.

After denied/exhausted recovery, review participant consent, active organizer/presenter access,
autonomous mode, room time, media/provider health and preserved usage, then explicitly Start agent
if eligible. Codes `provider_reconnecting`, `provider_recovery_denied`,
`provider_recovery_exhausted` and audit `sales.browser_room.provider_recovery` distinguish attempts.
`sales.browser_room.agent.ownership` uses fixed `provider_session_recovery` result receipts (no transcript).
Never reopen an ended room, reset quota or auto-consent to recover. Manual/assisted speech still
requires current answer approval. Lease expiry or worker replacement is not an automatic retry grant.

Rollback both opt-ins to false on all instances (semantic false before/with hybrid false). A runtime
profile change fences the current worker; explicitly restart under the legacy approved-answer path
after eligibility checks. If options are not hot-reloaded, use normal drain/restart. Lower modes,
Teams and guided work retain existing profiles. Setting a retry count never changes release policy.

Conversational output is buffered Realtime PCM, independently validated before room publication,
not unchecked direct streaming. Separate `conversation_audio_buffered_to_release`, provider
generation/validation, and endpoint audible latency. A listening badge is not audio delivery proof.
See [prompt-6 UAT](verification/agentdialog-prompt6/uat.md) and the
[prompt-7 acceptance ledger](verification/agentdialog/uat.md). Prompt 7's provider and
relational checks passed; full live voice acceptance remains gated on an eligible meeting.
`conversation_audio_generation` measures candidate generation separately from
`conversation_audio_buffered_to_release` (candidate return through release checks).
`conversation_first_media_enqueue` measures the dialogue operation through its first
accepted server frame; it is not an endpoint audibility measurement.

| Symptom / safe code | Check / recovery |
|---|---|
| `conversation_stale`, `turn_fenced`, `conversation_changed` | Compare current worker/participant/turn/response/presentation generations and saved checkpoint. A takeover, mode change or second question should invalidate old work. Refresh, then require a fresh command; never force the stale proposal. |
| `transcription_unavailable`, `response_failed`, `transcription_timeout` | Provider connectivity/session/entitlement; distinguish a closed socket from silence. Typed questions and manual controls remain the fallback. |
| `voice_unavailable`, output mismatch | Approved speech provider and immutable text/version binding. Do not disable release validation. |
| No grounded answer | Inspect question status/evidence and indexing/agent source permissions. Provider failure is not missing evidence. Preserve supported claims and clearly identify gaps. |
| `consent_required`, transcript retention message | Respect each participant's current consent; a confirmed utterance alone grants no storage or release authority. |
| quota/spend/expiry | Inspect preserved usage and room end time; book a new meeting when appropriate. Do not raise/reset limits as a recovery shortcut. |
| Ready/listening but no audible speech | Server send is not loudspeaker output. Check browser sound permission, selected output, mute, transport and playback-stop receipts. |

## Metrics and evidence collection

### Detailed meeting trace logs

The API's existing console/debug logging now emits structured `MeetingTrace` events.
Filter by `RoomId` and then `TurnId` to follow a question through the worker:
`session_start` → `turn_completed` → `route_requested` →
`dialogue_generation_start` / `dialogue_validated` / `dialogue_playback_complete`
(optional acknowledgement) → `source_lookup_start` / `source_lookup_complete` →
`queued_speech_start` / `queued_speech_finished`. `tool_result` records backend
acceptance or rejection; `answer_awaiting_release`, `dialogue_withheld`, and
`session_policy_stop` explain withholding and shutdown. Generation and lookup
durations, speech status, content class, generation fences and usage counters
are included where relevant. Server playback completion does not prove browser
audibility.

Development configuration enables Debug for
`VirtualCompany.Infrastructure.Sales.SalesRoomAgentWorker`, adding normalized
`input_event` metadata. It does not log PCM frames, participant transcripts, grounded answer text,
document contents, credentials, or raw provider payloads. The development profile
temporarily sets `SalesRoomAgent:AcknowledgementTextDiagnosticRoomId` for one test room.
For that room only, `acknowledgement_candidate_received` records JSON-escaped generated
acknowledgement text after a fresh authority/consent check and before release validation,
including candidates later withheld. This is sensitive diagnostic content: restrict
log access and retention, remove that room ID after diagnosis, and leave the option
unset in production. Treat correlated IDs
as operational data and restrict access/retention for any collected logs.
No new file sink is installed: use the API terminal/debug output or the existing
deployment log collector. Restore the worker category to Information to reduce
verbosity after debugging. Restart the API after deploying worker changes and
check `session_start` reports `Hybrid=True LiveDialogue=True` for the new session.

The local development profile enables semantic live-dialogue input. Autonomous
grounded questions can generate a short, independently validated acknowledgement
before lookup. Its wording is generated, not a fixed recording; the optional
acknowledgement has an eight-second cancellation budget and must not occupy the
grounded answer's delivered-context slot. Other modes retain approval rules.

The existing Azure Monitor configuration registers `VirtualCompany.Sales.BrowserRoom` and
`VirtualCompany.Agents.RealtimeConversation`. Without an exporter, collect these meters with
an operator-authorized .NET metrics collector; do not invent a dashboard result.

`sales.browser_room.latency` uses a fixed `operation` label at the conversation call sites:

- `confirmed_input_to_first_answer_frame`: confirmed/routed input to first frame accepted by
  the server media transport. Worker-local correlation is bounded to 32 entries/two minutes;
  timeouts or worker replacement produce no successful latency sample.
- `answer_complete_to_listening`: transport answer completion, including bridge generation
  and playback, to confirmed provider context/reply-window opening.
- `continuation_request_to_accepted`: backend request to successful durable resume result.
- `cancellation_to_transport_stopped`: cancellation to media transport stop completion.

These are **server timings**, not microphone-to-ear latency. During live UAT, separately
record first audible output and actual stopped playback at the consenting test endpoint.
Use the same observer clock or explicitly record clock offset. Missing playback evidence is
unknown, not zero latency. Keep raw microphone audio off by default; record event timestamps,
scenario labels, outcome and authorized redacted screenshots, not customer document contents.

`agents.realtime.tool.duplicates` counts duplicate provider tool calls without identifying
tags. Speech failure codes remain in bounded durable room/speech evidence; aggregated
`sales.browser_room.conversation.failures` uses only fixed reason buckets. Existing metrics
separate received/detected/forwarded/billed/generated/played/cancelled audio and input/output
tokens. Generated bridge and limitation audio are now included. Reconcile room counters with
shared reasoning/approved-speech run usage and provider billing for total test cost; the room
estimate alone is not an invoice or complete end-to-end cost measurement.

Never tag metrics with tenant/room/user IDs, transcripts, provider call IDs, or credentials.
