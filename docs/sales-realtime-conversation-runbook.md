# Sales realtime conversation operations

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
- Server WebSocket PCM: signed 16-bit little-endian, mono, 24 kHz. Input is explicitly
  committed after the local candidate detector; provider final transcription confirms the
  words. `turn_detection=null`; far-field reduction is configured for this profile.
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

An accepted wait/uncertain reply preserves the remaining original 45-second reply window;
it does not renew that window. A new question supersedes continuation context. After timeout,
use a fresh explicit question or host presentation control rather than assuming an old “yes”
can resume the deck.

A completed answer opens contextual listening even when a generated follow-up is skipped.
Within that window, contextual intent interpretation precedes the legacy question heuristic:
an explicit request to continue is not a factual source query merely because it starts with
“Can you”. Clear thanks can receive a bounded, independently validated generated follow-up;
it never authorizes continuation. Each played social reply is correlated to its own confirmed
input turn and opens a fresh reply window. Only autonomous mode permits automatic bridges
and voice-requested continuation. Changing the feature flag requires a fresh worker/provider
session in either direction; the old socket cannot acquire a different profile mid-session.

## Diagnostic order

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
