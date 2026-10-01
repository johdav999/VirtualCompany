# Agent dialogue prompt 3 — model routing and permitted speech

Date: 2026-09-28. Scope: prompt 3 of `docs/agentdialog-prompts.md`, preserving the
uncommitted prompt-1/2 implementation. Prompt 4 presentation-tool expansion is not included.

## Outcome and authority

Autonomous semantic-profile input now uses automatic Realtime tool selection, without a prior
question requirement or an intent-classifier-pinned tool. Social, explicitly general and clarification
replies use real buffered Realtime audio. The backend independently checks the completed words and
current authority before any PCM is published. Restricted facts retain the grounded-answer path.
Manual/assisted approvals are preserved. No runtime flags, schedules, consent, quotas or data were changed.

The OpenAI Docs skill informed the out-of-band response/function-call/audio implementation using
[Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations).
The polish/UAT skill kept provider compatibility, deterministic media receipts and real browser
audibility as separate evidence levels; there is no claim of live meeting acceptance.

## Acceptance evidence

| ID | Priority | Evidence | Result |
|---|---|---|---|
| AD3-01 | P1 | Production worker with relational authority, replayed semantic input and complete provider tool event; automatic tool choice, no intent-classifier invocation, initial social output through media. | Passed with deterministic provider/media substitutes |
| AD3-02 | P0 | Wrong-turn, duplicate and authority-injecting proposals; shared completed-call correlation and once-only result continuation. | Automated regression passed |
| AD3-03 | P0 | Collector withholds bytes until matching audio/transcript/response completion; wrong turn/item, failed response, mismatched text and missing completion rejected. | Automated regression passed |
| AD3-04 | P0 | Independent actual-content verdict overrides general label; restricted output publishes zero frames. Consent, retention, mode, end and generation changes during generation prevent release. Cross-company request denied. | Automated regression passed |
| AD3-05 | P1 | Initial greeting needs no question row; durable command deduplicates; checkpoint preserved; rejected candidate leaves room ready and a later greeting can speak. | Relational/media substitute passed |
| AD3-06 | P1 | Failed proposal keeps input/media connected; actionable withheld reason projects ready/available and renders in host UI. | Worker and component tests passed |
| AD3-07 | P1 | Real OpenAI synthetic welcome → model-selected social tool → correlated completed generated audio/transcript. | Provider probe passed; not published into a meeting |
| AD3-08 | P1 | RØDE capture, audience playback, perceived pause/fan behavior and real independent-validator latency. | Not exercised; live pilot still required |

## Tests and builds

- Broader affected backend selection: **284 passed** (conversation, playback, floor, semantic input,
  shared Realtime adapter/state and buffered speech tests). Its build compiled backend dependencies.
- Web component selection: **35 passed**; Web and test projects built successfully.
- Final routing/correlation selection after the duplicate-result fence change: **19 passed**,
  overlapping the broader selection.
- Final authority/release selection after usage-accounting adjustment: **8 passed** (overlapping).
- Provider-neutral conversation/controller selection: **22 passed**. Across the three test projects,
  **341 distinct selected tests passed**. `git diff --check` passed.
- Early fixture failures were corrected: a wrong retention-purpose name and synthetic constant
  audio that intentionally failed the local speech-like onset gate. Corrected relational and complete
  worker replay tests passed. One earlier timing-limited semantic test passed on focused and broad reruns.
- No persistence shape changed. `conversation` fits the existing string discriminator and existing
  unique company/room/command index; no migration or SQL Server DDL is required.

## Real provider probe

Command: `dotnet run --project scripts/AgentDialogueProbe/AgentDialogueProbe.csproj -- --live`.
Existing environment credential was used without printing or persisting it. The sandbox blocked
NuGet/network access; the bounded probe ran after the network escalation. The first probe stopped on
an unhandled non-content session event; handling unsupported events correctly produced the result below.

- Configured default model: `gpt-realtime-2.1-mini` (not changed).
- Synthetic input: “Welcome to the meeting, Alex!”
- Selected tool: `respond_social`, complete correlated call.
- Generated words: “Hi, thank you for the warm welcome! Happy to be here.”
- Routing: **1,412 ms**. Routing plus candidate generation: **3,561 ms**.
- Completed PCM: **3,100 ms** of audio; candidate usage reported 72 input / 119 output tokens.
- Path: **buffered_realtime_candidate_only**. `published=false`.

This was a billable provider compatibility check, not an audible browser exchange. It did not
capture a microphone, access company sources, store PCM, invoke the real independent release
reasoner, or change an active meeting. Production release adds independent validation latency;
the measured 3.6 seconds must not be described as end-to-end meeting response latency.

## Operating boundary

Pilot instructions and rollback remain in the [runbook](../../sales-realtime-conversation-runbook.md).
Both HybridConversationEnabled and SemanticConversationInputEnabled must be enabled in an
authorized test deployment, with a fresh started agent and autonomous mode. No flags were enabled
and no hosts were restarted here. Candidate output is capped at 20 seconds and generation at
30 seconds; output remains buffered until release, with no silent TTS fallback. Consent withdrawal,
host stop, room end, transport failure and budget exhaustion still stop the applicable AI path.
