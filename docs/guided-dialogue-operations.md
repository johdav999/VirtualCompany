# Guided dialogue and Realtime voice operations

## Capability and trust boundary

Guided work sessions are company-owned drafts. They reuse the existing direct-agent conversation for finalized transcript messages and store queryable session, field, provenance, readiness, review, idempotency, and voice-binding state in relational tables. A model response can propose draft changes; it cannot write an authoritative Marketing, Finance, Sales, Support, or agent artifact. The user must prepare an expiring review and confirm it in the authenticated application.

The browser uses WebRTC for audio. It sends SDP to the authenticated Virtual Company API, never to an endpoint containing a standard provider key. The API combines the SDP with a bounded Realtime session configuration and calls `POST /v1/realtime/calls`. The provider `Location` call ID is retained in a bounded durable binding and used by the server to open a sideband WebSocket. Tool definitions, arguments, results, and business logic remain server-side. Finalized browser transcription is submitted through the normal guided turn endpoint, so text and voice use the same structured checkpoint, validation, idempotency, provenance, concurrency, and audit path.

## Configuration

Configure the `GuidedDialogue` section through normal secret/configuration providers:

```json
{
  "GuidedDialogue": {
    "Enabled": true,
    "ApiKey": "",
    "BaseUrl": "https://api.openai.com/v1/",
    "Model": "gpt-4.1-mini",
    "TimeoutSeconds": 45,
    "MaxOutputTokens": 3000,
    "ReviewTokenMinutes": 15,
    "RetentionDays": 90,
    "MaxTurnCharacters": 12000,
    "RealtimeEnabled": true,
    "RealtimeModel": "gpt-realtime-2.1-mini",
    "RealtimeVoice": "marin",
    "RealtimeTranscriptionModel": "gpt-4o-mini-transcribe",
    "RealtimeTurnDetection": "semantic_vad",
    "RealtimeVadThreshold": 0.15,
    "RealtimeVadSilenceDurationMs": 900,
    "RealtimeTurnEagerness": "low",
    "RealtimeNoiseReduction": "near_field",
    "MaxVoiceMinutes": 30,
    "MaxVoiceReconnects": 2
  }
}
```

`OPENAI_API_KEY` is used when `GuidedDialogue:ApiKey` is empty. Never put the key in Web configuration, JavaScript, source control, logs, diagnostics, or browser responses. Disable `Enabled` to stop model checkpoints and `RealtimeEnabled` to stop new voice calls. Existing text sessions remain readable and directly editable when provider features are disabled.

## Listening-first turn control

The browser keeps Realtime voice activity detection enabled while the server configures `create_response=false` and `interrupt_response=false`. The shared `conversation-turn-controller.mjs` module decides when a normal response may be created. It is provider-neutral and must be reused by future voice conversation surfaces instead of duplicating guided-work turn logic.

User speech immediately cancels the active response and clears buffered WebRTC output before the transcript is classified. A bounded out-of-band text response, tagged with `purpose=turn_intent` and excluded from the default conversation, classifies `pause`, `stop`, `continue`, `incomplete_turn`, or `complete_turn`. Classifier output is never spoken, shown, persisted, or routed to tools. A small deterministic command set is only a fast safety path; semantic classification handles natural variants. Low-confidence, malformed, and timed-out classifications temporarily retain the buffered fragments and favor silence. If no further speech arrives during the bounded grace period, the already-transcribed utterance is accepted so one conservative classification cannot make detected speech disappear. Explicit pause remains latched until the user speaks again.

Pause intent enters a latched visual `Listening — take your time` state with no spoken acknowledgement and no automatic expiry. Incomplete fragments remain bounded and are joined in their original wording when a later fragment completes the thought. Every speech start advances a turn epoch. Browser response gating and the sideband epoch check suppress audio, transcripts, and tool continuations from older turns.

`RealtimeTurnDetection=semantic_vad` with low eagerness is the recommended conversational default. `server_vad` remains available as a fallback; `RealtimeVadSilenceDurationMs` is bounded from 500 to 3000 ms. Response creation and interruption remain application-managed in either VAD mode so there is only one authoritative turn-control path.

Each checkpoint receives the current user turn, the current structured draft, the safe session summary, and up to 20 recent workshop messages. Recent dialogue is bounded to 2,000 characters per message and 12,000 characters in total, ordered oldest to newest. The checkpoint merges supported details into durable field documentation instead of replacing existing material with a terse summary. Narrative fields should retain decisions, rationale, qualifiers, examples, constraints, evidence references, and uncertainty; the separate safe session summary remains concise.

## Data and migrations

Migration `AddGuidedDialogueAndRealtimeVoice` creates:

- `guided_work_sessions`
- `guided_draft_fields`
- `guided_session_operations`
- `guided_voice_bindings`

All session relationships are company-scoped where the principal is company-owned. SQL Server migrations use ordinary `uniqueidentifier`, `nvarchar`, `datetime2`, and `bit` types and do not introduce startup DDL. Apply the same migrations after restoring either a local SQL Server backup or the Docker SQL Server backup. The existing Docker restore path remains unchanged: restore first, run the migration project/API migration procedure second, and then start the application. Never recreate or drop a database to roll this feature back.

Rollback is application-level: disable guided dialogue/voice, hide entry points if required, retain the additive tables, and deploy the prior application. Immutable migration history need not be reversed. The additive tables can be retained until the normal retention period removes terminal sessions.

## Retention and privacy

The daily retention worker deletes terminal sessions older than `RetentionDays` in bounded batches. Cascade relationships delete fields, retry records, and voice bindings. Guided transcript messages are removed only when they are older than the cutoff and have no task links; ordinary direct-chat messages are not selected. Review retry payloads are encrypted with ASP.NET Core Data Protection and review tokens are stored on sessions only as SHA-256 hashes. Provider event payloads, audio, raw SDP, API keys, hidden reasoning, and raw model traces are not persisted.

Users should be told before microphone capture starts. The browser stops every local media track, closes its data channel and peer connection, removes its audio element, and asks the API to end the binding when the user stops voice or leaves the page. Provider calls have bounded duration and reconnect counts.
Audio is transmitted directly over the provider WebRTC connection and remains subject to the configured OpenAI project data controls and retention terms; validate those controls against company policy before enabling voice. Virtual Company does not write audio bytes to its database or logs.

## Authorization and incident response

Every public route requires an authenticated company member and resolved company context. Session reads and mutations additionally require the creating user. Artifact definitions check the selected agent's current effective capability. Sideband tools bind to a durable call, company, session, and user; they recheck active membership, session state, field schema, expected version, size bounds, and provider call idempotency. Cross-company IDs return forbidden or not found without revealing the foreign object.

If suspicious behavior occurs:

1. Set `GuidedDialogue:RealtimeEnabled` to `false` and redeploy/reload configuration to stop new calls.
2. Rotate the provider key if exposure is suspected; the browser never needs a replacement.
3. Inspect audit actions beginning with `guided_session.` and metrics below, using company/session correlation rather than transcript contents.
4. End active provider calls and allow durable voice bindings to expire.
5. Preserve audit events and relevant database backups under the applicable incident policy before changing retention.

## Observability and alerts

The `VirtualCompany.GuidedWork` meter emits:

- `guided_work.sessions.started`
- `guided_work.turns.completed`
- `guided_work.fields.changed`
- `guided_work.reviews.prepared`
- `guided_work.artifacts.committed`
- `guided_work.voice.calls.started`
- `guided_work.voice.tools.rejected`
- `guided_work.voice.stale_continuations_suppressed`
- `guided_work.checkpoint.duration`

Dimensions are bounded to artifact type or tool name. Do not add user text, field values, SDP, provider output, or identifiers as metric labels. Alert on sustained checkpoint failures/latency, sideband reconnect exhaustion, rejected-tool spikes, commit conflicts, and retention-worker failures. Audit summaries are intentionally safe and contain no chain of thought.

## Supported user flow

Start from an eligible agent profile or deep-link to `/agents/{agentId}/workshops/{artifactType}?companyId={companyId}`. The supported artifacts are agent operating brief, Marketing strategy, Marketing segment, Finance budget line, Sales campaign plan, and Support service-level policy. A Sales campaign workshop requires an existing campaign target because the current planning contract configures rather than creates that aggregate.

Operators should verify keyboard navigation, visible focus, screen-reader labels, mobile stacking, microphone denial, provider-disabled behavior, reconnect/stop behavior, stale-version conflicts, incomplete review, expired review, and successful commit before enabling the feature for a tenant.

For the live turn-control smoke test, start agent speech and speak over it, verify immediate cutoff, say a pause request and remain silent, verify no automatic acknowledgement or resumed audio, continue with two unfinished fragments, and verify exactly one accepted user turn and one audible response. Interrupt once while a tool is running and confirm that its older continuation is suppressed. Also repeat microphone change, mute, reconnect, explicit interrupt, stop, and text fallback checks. Browser diagnostics may include bounded event names and timing, but must not include transcript text or audio.

Run the package-free deterministic turn-controller suite with `node --test tests/VirtualCompany.Web.Tests/js/conversation-turn-controller.test.mjs` alongside the focused API and Web test projects.
# Asynchronous public research in workshops

Text workshop research is handled as a durable company-outbox continuation. The initial workshop turn records the user request and the agent acknowledgement, then queues one `guided_work.research_continuation_requested` message. The user may continue the workshop immediately.

The outbox dispatcher leases and retries the continuation using its normal bounded retry policy. On completion, the continuation records a separate agent follow-up and only proposed, cited draft fields. A cancelled or completed workshop is safely ignored; no late research result may alter its draft.

## Operator checks

- Find the outbox item by company ID, session ID, correlation ID, or idempotency key beginning `guided-research:`.
- Check `guided_session.research_queued` and `guided_session.text_research` audit events for queue and completion outcome. Audit metadata intentionally includes only artifact type, source count, and failure code.
- Use the outbox status, attempt count, available time, and last safe error to diagnose retry or terminal failure. Do not retry by creating a new random request ID.
- A terminal provider failure produces a safe workshop follow-up; it never substitutes model knowledge or fabricated citations.

## Recovery and reconciliation

Queued work survives a process restart because it is stored in the company outbox. A stale lease is reclaimed by the normal dispatcher. Duplicate delivery is suppressed by the stable outbox idempotency key and the guided-work `research_followup` operation record. If a field changed after the originating user message, the continuation preserves that newer field rather than overwriting it.
