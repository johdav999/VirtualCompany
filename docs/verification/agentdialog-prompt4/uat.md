# Agentdialog prompt 4 — authorized presentation tools

Date: 2026-09-28. Base revision: `6c5532d7`, with the existing uncommitted prompts 1–3
preserved. Scope: prompt 4 only. Product: Virtual Company browser-room AI workflow.
Environment: local builds, isolated SQLite fixtures, deterministic media/provider replays,
and an explicitly bounded synthetic OpenAI probe. Roles: organizer/controller and guest fixtures.
No production database, room, calendar, device, consent, quota or deployment flag was changed.

## Delivered flow

Persistent semantic dialogue now offers start/restart, pause, resume and state tools alongside
social/general/clarification/grounded-question tools. Model proposals have empty arguments and
per-response capability lists. Backend execution uses fresh server bindings, controller rights,
autonomous mode, current consent, approved assets and audience readiness. A prior answer/bridge
and the old 45-second reply window are not prerequisites for a fresh presentation command.

Start uses canonical slide navigation and resets slide/point/offset; resume uses the durable
narration checkpoint. Existing serializable transactions, operation/audit records and the speech
queue provide durable deduplication without a schema change. The host controls and legacy path
remain intact. Committed queue operations are not described as completed playback.

One output ownership gate serializes narration, grounded answers and generated conversation,
including cleanup and stale completions. Actual server playout receipts exclude queued audio
before a flush. Interrupted dialogue hands off once and preserves the narration checkpoint.
Isolated Realtime output items are truncated through the provider adapter at the delivered offset;
only fully played text enters persistent context. Partial narration context uses positions, not
invented transcript fragments or the full unplayed script.

## Evidence ledger

| ID | Priority | Flow / evidence | Result |
|---|---|---|---|
| AD4-01 | P1 | Fresh start/resume/pause without question history; relational operation and queue assertions, duplicate and conflicting-action replay. | Automated |
| AD4-02 | P0 | Changed tenant/session/worker/checkpoint/mode/consent/retention/budget; pending speech, missing audience and revoked/unplayable narration. | Automated rejection checks |
| AD4-03 | P0 | Guest retains question tool but cannot see/execute playback commands; provider rejects registered-but-not-offered tools and authority arguments. | Automated |
| AD4-04 | P1 | Production worker dispatches correlated start/resume/state calls into real service/queue, keeps input/media session connected. | Relational worker + deterministic transport |
| AD4-05 | P0 | Output ownership waits for old cleanup; stale completion cannot release newer output; interrupted conversation flushes once and hands off its generation. | Automated |
| AD4-06 | P1 | Media queue receipt subtracts unplayed buffer, survives flush and a new generation; truncation excludes unheard audio after generation completes. | Adapter/media replay |
| AD4-07 | P1 | Restart from later interrupted/answering slide resets point 8 to slide 1 / point 1 / offset 0. | Relational regression |
| AD4-08 | P1 | Real OpenAI greeting audio plus start/pause/resume tool selection with restricted capability lists and synthetic playback state. | Passed, no meeting publication |
| AD4-09 | P1 | RØDE microphone → greeting → narration → grounded question → audible answer → saved-position resume in a live room. | Not exercised; authorized pilot deployment/audience required |

## Provider evidence and diagnosis

Command: `dotnet run --project scripts/AgentDialogueProbe/AgentDialogueProbe.csproj --no-restore -- --live --playback`.
The sandbox denied the OpenAI network socket. After a network escalation, the probe used the
existing environment credential without printing it. No customer content or microphone was sent.

Two smaller tool budgets, 128 and 256 tokens, ended the resume proposal with provider status
`incomplete`, reason `max_output_tokens`. The bounded proposal budget is now 512 tokens.
The successful final run selected `start_presentation`, `pause_presentation`, then
`resume_presentation`. Each result explicitly reported `executed=false`: these are compatibility
checks, not real meeting actions. Greeting routing was 1,635 ms; routing plus completed candidate
generation was 3,456 ms, with 4,100 ms of generated PCM, 72 input and 157 output tokens.
The candidate was never published; a zero-delivery truncation request was sent and its output
session discarded. The probe does not independently assert a provider truncation acknowledgement.
Independent content validation and microphone-to-loudspeaker latency were not measured.

## Validation

- Broad affected backend selection: **316 passed**, including playback, semantic input,
  conversation policy, floor, shared adapter/state and buffered speech.
- Final playback/restart selection after the final media/checkpoint changes: **18 passed**;
  two restart cases are additional to the broad selection.
- Final running-worker dispatch/retention selection: **8 passed**; one retention-revocation
  case is additional to the broad selection. Total distinct selected backend cases: **319**.
- Host UI component suite: **35 passed**. Total distinct selected cases: **354**.
- Backend and Web test builds succeeded. Existing repository warnings remain; no new build
  error remains. `git diff --check` passed (repository line-ending warnings only).
- Initial test failures identified fixture state assumptions (manual/slide-zero session versus
  autonomous floor, and startup media fences versus interruption fences) and the intentionally
  obsolete prompt-3 assertion that start is not registered. Fixtures/assertions were updated;
  the tests retain production authority checks rather than relaxing them.

No persistence shape changed, so no SQL Server migration, DDL or database reset was needed.

## Live acceptance and rollout boundary

Use the [runbook](../../sales-realtime-conversation-runbook.md) with both opt-in conversation
flags, a newly started autonomous agent, approved playable narration, rendered audience, current
consent and sufficient existing budgets. Do not force stale bindings, fabricate browser render
receipts, or alter limits to make a test pass. The implementation did not enable flags or restart
hosts. A real consenting audience and microphone/browser playout test remains required before
claiming customer-facing acceptance.

The OpenAI Docs skill informed per-response tool availability and measured interruption/truncation,
using [Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations).
The polish/UAT skill separated relational, provider and audible-user evidence and retained the
live-flow limitation rather than treating synthetic receipts as a successful meeting.
