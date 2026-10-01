# Agent dialogue prompt 2 — semantic input completion

Date: 2026-09-28. Baseline: `6c5532d7` plus the preserved prompt-1 working tree.
Scope: prompt 2 of `docs/agentdialog-prompts.md`; no later prompt is claimed complete.

## Profile and evidence

Product: browser sales meeting; admitted organizer, AI processing and transcript-retention
permission, valid room/worker authority. Flow: Start agent → local speech candidate → continuous
authorized PCM including a mid-sentence pause → provider-completed item → completeness/authority
checks → existing grounded-question/conversation processing. Presentation output is not controlled
by local volume or a provider speech-start event.

The OpenAI Docs skill was used to verify the current semantic VAD and server-event schema.
The polish/UAT workflow uses explicit deterministic substitutes: production worker, SQLite
relational authority/capture, fake media/provider transport receipts, real event normalization,
replayed provider events, and controlled classifier/completeness verdicts. These tests do not
measure the model's speech recognition quality or real microphone audibility.

| ID | Severity | Change / acceptance evidence | Status |
|---|---|---|---|
| AD2-01 | P1 | Semantic profile uses configurable eagerness (initial `low`), provider-owned commits, automatic response/interruption disabled. Schema tests cover all four eagerness values, invalid combinations and legacy profiles. | Automated |
| AD2-02 | P1 | A 1.2-second pause between question fragments stays in one 2-second audio stream. Production worker replay reaches confirmed conversation once, with no manual commit and one retained transcript. | Deterministic transport substitute |
| AD2-03 | P1 | Reordered/duplicate speech boundary, commit and transcript events bind by item ID and session audio offsets. Track, consent, participant generation, room turn, sequence loss and overlapping speakers invalidate old windows. | Automated replay |
| AD2-04 | P1 | Local noise/provider speech-start alone cannot release a turn. A model-incomplete turn is retained only with permission, waits without source lookup, and leaves the connection alive. Pending newer speech holds routing. | Automated classifier/completeness substitute |
| AD2-05 | P1 | Retention declined: production worker forwards no microphone PCM, stores no transcript and does not route a voice question. Human call/manual slides/typed questions are the documented supported path. | Relational worker substitute |
| AD2-06 | P1 | DTX silence is bounded and included in forwarded usage. Missing/failed turns expire or are discarded without reconnecting; subsequent items can complete. Consent revocation prevents late release. | Automated replay and existing lifecycle coverage |

## Validation

- Sales backend build succeeded; affected Operations/Application/API projects compiled during tests.
- Broad API regression selection: 292 passed, no skipped tests. Includes playback/lifetime,
  semantic input, local segmentation, conversation routing/tools/status, lease, capture, floor,
  provider schema/context and speech-turn buffer tests.
- Shared conversation domain suite: 22 passed.
- The first broad run found a timing-sensitive incomplete-turn test assertion. Replaced its fixed
  processing assumption with a completion signal; the corrected broad run passed.
- Final focused run: 40 passed, covering the additional pending-newer-speech regression and semantic
  worker/schema. Across the overlapping selections, 315 distinct tests passed.
- `git diff --check` passed. No EF schema changes; no migration or SQL Server DDL required.

## Operating boundary and remaining live acceptance

The new behavior is opt-in, not enabled in any checked-in/running environment by this task.
Enable both `SalesRoomAgent:HybridConversationEnabled` and `SemanticConversationInputEnabled`
with `SemanticVadEagerness=low` for an authorized test deployment, then start a new agent session.
See the [runbook](../../sales-realtime-conversation-runbook.md) for rollback and telemetry.
Provider models, meeting schedules, counters, budgets, consent, permissions and calendars were not changed.

Input still passes current tenant/participant/track/consent/generation checks and the existing
model completeness gate. Semantic VAD is not a guarantee of perfect recognition, intent or noise
rejection. Application-controlled response creation and approved audio release remain enforced.
Immediate generated greetings and model-selected presentation actions belong to later prompts.

No live RØDE USB microphone/audience session or OpenAI handshake was exercised. Real pause
sensitivity, fan/echo behavior, end-to-end provider latency and browser audibility remain unverified.
No process was restarted and no active room/database state was changed. The replay timing values
are test inputs, not production latency measurements.
