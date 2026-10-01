# Persistent agent dialogue — prompt 7 acceptance

Date: 2026-09-28. Profile: local Virtual Company browser-room workflow; organizer and
guest fixtures; autonomous, assisted and manual modes. Existing uncommitted prompts
1–6 were preserved. No room, calendar, source access, consent, quota, configured model
or deployment flag was changed. No persistence shape changed; no migration is needed.

## Release decision

**Not yet live-voice accepted.** The real browser room
`af4dc01c-d832-4be9-a87c-4a41134a8196` displays “This meeting has ended. Book a new
meeting to continue.” Start agent is disabled. Its displayed historical usage is
11:08 output and $0.1953 estimated spend. The old running UI also shows a stale Alex
floor label; this is not proof that the new source build was deployed.

The browser check was read-only. It cannot exercise join, Start agent, RØDE microphone
capture or audible output in an ended room. Do not reuse historical consent to start
a new test, reopen this room, reset usage or report fixture audio as live playback.
Rollout remains gated on an eligible test meeting, deployed build, current consenting
test audience, approved playable deck, indexed accessible sources and remaining budget.

## Changes and evidence ledger

| ID | Priority | Finding / acceptance | Evidence and status |
|---|---|---|---|
| AD7-01 | P1 | Greeting, grounded answer and a late resume must coexist in one room without expired reply-window gating. | New `Dialogue_journey_preserves_checkpoint_through_greeting_grounded_answer_and_late_resume`: real SQL-backed retrieval, answer release, media receipts, >45-second fresh input and durable/idempotent resume; passed. Provider/media seams are deterministic. |
| AD7-02 | P1 | Provider routing must choose among competing tools, not only the expected tool. | Strengthened opt-in probe: greeting, start, pause, resume, company onboarding lookup and thanks; final real-provider run passed. No action executed or audio published. |
| AD7-03 | P1 | Old unanswered context must not displace the latest request. | First expanded probe chose social for start. Shared playback-context instruction now explicitly selects the last user message; regression asserts retained history and latest input. Subsequent probe exposed an inappropriate pause option while already paused; probe now offers state-appropriate competing tools and complete content-policy instructions. These observations do not isolate a single model-internal cause or guarantee every paraphrase. |
| AD7-04 | P2 | Separate generation from release checking and transport enqueue latency. | Previously buffered-to-release included generation. New generation and first-media-enqueue measurements; buffered-to-release now begins after the candidate returns. Journey asserts all three meters emit. No claim of endpoint audibility. |
| AD7-05 | P0 | Preserve authorization and release gates across failure/races. | Existing affected regression selection covers stale generations, takeover, consent, mode downgrade, source revocation, output matching, tenant isolation, duplicate/out-of-order events, provider recovery/expiry, quotas and rollback. |
| AD7-06 | P1 | Shared lifetime must not depend on Alex's name. | New separate finance-agent binding test uses the same lifecycle, rejects sales authority/history, permits late fresh input, respects stop. Contract-level reuse only, not a deployed finance voice product. Existing shared gateway profile tests retain legacy behavior. |
| AD7-07 | P1 | Full spoken journey, including internal pause, fan noise, echo, overlap and audible follow-up/resume. | **Blocked live**: ended room and no eligible active test audience. Existing semantic/transport fixtures are useful regressions, not acoustic acceptance. |
| AD7-08 | P1 | Start agent must accept valid microphone provenance and remain connected. | 2026-09-29: live room persisted `semantic_provenance_invalid`; API run `20260929060407115` confirms failure in `SalesRoomSemanticAudioInput.Push`. Pinned LiveKit 0.1.4 creates remote audio tracks without assigning `Track.Sid`. Adapter now keys subscription, frames and unsubscribe by `Publication.Sid`, rejecting missing publication identity. SDK-object/semantic-input and media regressions: 37 passed. Live microphone recheck remains required. |

## Real provider measurements

Command: `dotnet run --project scripts/AgentDialogueProbe/AgentDialogueProbe.csproj --no-restore -- --live --playback`.
Sandbox network initially returned `provider_unavailable`; the bounded network-enabled
probe then ran against the existing `gpt-realtime-2.1-mini` configuration and credential.
Only synthetic test phrases were sent. No raw microphone audio or customer records.

Final successful run:

| Measurement | Observed |
|---|---|
| Connect plus greeting route | 1,240 ms |
| Connect/route plus completed buffered generation | 3,067 ms |
| Incremental candidate generation path | 1,827 ms (difference, includes request overhead) |
| Generated PCM duration | 3,450 ms |
| Candidate input/output tokens | 72 / 123 |
| Published/delivered audio | None; candidate discarded, zero delivery reported |
| Tool selections | start → pause → resume → approved-source lookup → social |

The token counts above cover the candidate output session, **not total probe cost**
or all routing calls. Earlier failed probes were also billable. Completion-of-human-turn,
retrieval latency, release-validation latency, first audible output, actual playback stop
and audible resume remain **unmeasured live**, not zero. No latency SLA is claimed.

## Repeatable regression

Final verification: **473 backend passed, 1 skipped**, **23 shared-lifecycle passed**,
**40 Web component passed**, **13 browser-media JavaScript passed** (549 passed total).
API and Web builds succeeded with zero errors/warnings in the final incremental builds.
`git diff --check` passed, with repository line-ending warnings only. The PowerShell
runner passed parser validation. No SQL Server schema change or migration was involved.

The initial broad backend run had 472 passes and one failure in the incomplete-turn
semantic worker case. All three semantic worker cases then passed in isolation; an
unchanged full-selection rerun passed all 473 with a saved report at
`tests/VirtualCompany.Api.Tests/TestResults/agentdialog.trx`. The initial failure did not
reproduce; its exact cause was not established. Do not call it a fixed production bug.
The one skipped case requires explicit synthetic WAV/provider opt-in and exercises the
legacy segmented transcription path; it is not replaced by the text/tool probe above.

From repository root, after normal dependency restore:

```powershell
./tests/scripts/Test-AgentDialogue.ps1
# Optional, billable, synthetic provider compatibility (not voice acceptance):
./tests/scripts/Test-AgentDialogue.ps1 -LiveProviderProbe
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -v:q -clp:ErrorsOnly
```

The combined journey uses real production services and SQLite state with deterministic
embeddings, semantic judgments and media. Separate running-worker tests stream synthetic
PCM and replay normalized provider events. Neither tests a real microphone, real source
indexing from OneDrive, acoustic echo cancellation or loudspeaker delivery.

## Live acceptance procedure and rollout gate

Use the [runbook](../../sales-realtime-conversation-runbook.md) and the existing authorized
local pilot only. Deploy the built API/Web through the normal operator workflow; verify
the two opt-in flags and eagerness, media service, provider health, source access and
approved narration. Do not broaden flags to other customers or products. If readiness
fails, retain the gate and record the exact failure.

1. Join an eligible meeting using the actual RØDE input and intended output device.
   Each participant supplies current consent; Start agent. Say a welcome and verify
   a generated audible reply with no presentation started.
2. Ask to start presenting. Verify approved prerecorded audio, one output speaker,
   and a listening agent that remains present.
3. Ask “A question, how…”; pause; finish the onboarding question. Verify the complete
   question, approved-source lookup, concise qualified answer, audible automatic
   release in autonomous mode, natural follow-up and continued listening.
4. Reply with another question; then say thanks. Verify a relevant answer/social
   response, not automatic resume. Explicitly request continuation; verify measured
   saved narration offset and no repeated earlier speech. Repeat after >45 seconds.
5. Test initial question before presentation, general education, ambiguous assent,
   fan noise, speaker echo and overlapping speakers. Incomplete speech must wait or
   clarify rather than fabricate a company overview. Record false/missed interruptions.
6. Repeat answer release in assisted/manual: require host approval/speech. Exercise
   takeover and consent withdrawal, then source denial and provider disconnection in
   the authorized test environment. Verify no unreleased audio and no stale replay.
   Use isolated regression for quota exhaustion/worker races rather than changing
   real limits or terminating unrelated services.

For every live turn record one observer's timestamps for last intended speech, confirmed
turn, tool route, retrieval completion, generation completion, release validation,
first audible sound, actual stopped playback and resumed sound; record missing values
as unknown. Capture provider usage and delivered receipts separately. Use registered
meters without user text/IDs as metric labels; do not retain raw microphone audio.
`conversation_first_media_enqueue` is server enqueue, not first audible output.

Rollback: disable semantic input before/with hybrid conversation on the pilot, drain
through the normal host workflow, and explicitly restart only when eligible. Preserve
usage, permissions, consent and checkpoints; do not replay pending speech. Human/manual
controls and the legacy approved-answer path remain available within room eligibility.

The polish-UAT-loop skill determined the evidence tiers and blocked-live release gate.

### 2026-09-29 acknowledgement and debugging follow-up

Local configuration had hybrid conversation enabled without the semantic live-dialogue
flag. The semantic grounded-question dispatch also performed lookup without a spoken
acknowledgement. Development now enables that flag, and autonomous grounded questions
attempt a short generated, independently validated acknowledgement before retrieval.
Acknowledgement speech has a separate deterministic command key from its answer, while
trace events retain the input turn ID. It does not fill the answer's provider-context slot.

`MeetingTrace` logs correlate normalized input, completed turns, model tool routing,
lookup, validation, playback and policy/failure outcomes without logging customer text
or audio. See the runbook for filtering and verbosity. The worker regression exercises
acknowledgement-before-lookup ordering, distinct durable command IDs and trace privacy.
These are deterministic server-side checks, not a claim of microphone-to-speaker UAT.
Verification: the focused `SalesRoomPlaybackWorkerTests` and
`SalesRoomConversationReasonerTests` suite passed all 216 tests. API build passed
with zero warnings/errors; `git diff --check` reported no whitespace errors.
The API must be restarted with the rebuilt code; live audibility and acknowledgement
wording still require a consenting participant's next meeting test.

### 2026-09-29 acknowledgement budget and voice resume handoff

- Evidence: the live `checking_sources` response ended with `incomplete / max_output_tokens`;
  the buffered audio request used a 256-token cap. The request now uses 1024 tokens,
  retaining the 20-second PCM cap, transcript validation, generation deadline and release checks.
- Evidence: `resume_presentation` was accepted, then narration failed `render_timeout`
  for presentation version 51 with no audience rows. The fresh command DI scope had
  no company execution context. Its tenant-filtered runtime read returned no snapshot,
  silently skipping publication to the browser. Both voice continuation paths now enter
  the command scope's company execution context; missing snapshots are no longer reported
  as ordinary successful publication. Render acknowledgement remains mandatory.
- Regression coverage uses an initially empty scoped tenant accessor, relational persistence,
  real presentation runtime and captured stage publication. Start/resume must publish the
  new committed version, without treating old audience receipts as confirmation. Audio tests
  check the actual provider response payload and one-response-per-turn enforcement.
- The rebuilt API was restarted. Database, shared Realtime and sales-room readiness checks
  are healthy; aggregate health is degraded by the unrelated B2Brouter check.
- Verification: 190 focused buffered-speech/playback-worker tests passed, including the
  running semantic start/resume cases with fresh scoped tenant accessors. Results:
  `tests/VirtualCompany.Api.Tests/TestResults/dialogue-handoff.trx`. API LocalRun build
  succeeded; `git diff --check` passed. No browser/client changes were required.
- Live acceptance remains pending: consenting participant asks a grounded question, hears
  an acknowledgement and answer, then asks to continue and hears narration from its checkpoint.
  Automated checks do not establish microphone-to-speaker audibility.

### 2026-09-29 incomplete microphone question (open)

Latest user input: “How is onboarding done?” Browser inspection confirms RØDE NT-USB
is selected, microphone is enabled, consent is active and Alex remains listening.
Owner generation 26 saved only “How is” and other 2–4-character fragments. Logs show
the completeness gate holding those fragments; no grounded-question route is requested.
Some completed provider transcripts also fail correlation. The existing evidence does
not establish whether the lost words originate in capture, transport or recognition.

Added bounded `MeetingTrace input_health` diagnostics for received/dropped frames,
maximum processing age, capture discontinuities and input-duration counters. Input events
now include transcript length, provider offsets and correlation rejection reason without
transcript content or audio. These diagnostics preserve all consent and attribution gates.
The 18 semantic-input tests pass. Live reproduction with the updated API and the same
spoken question is required before selecting a capture/recognition fix; this defect is open.

The OpenAI Docs skill verified [semantic VAD behavior](https://developers.openai.com/api/docs/guides/realtime-vad):
low eagerness helps with pauses but does not prove complete speech or pause external
recordings automatically. No API/model migration was made.

### 2026-09-29 acknowledgement and held resume command

Scope: existing local organizer/autonomous meeting, consented microphone input,
grounded onboarding question followed by presentation continuation.

| Issue | Evidence | Change | Verification |
|---|---|---|---|
| ACK-02 (P1) | `dialogue_generation_start` then `dialogue_withheld`, `RealtimeAgentEventException`, no audio release; lookup 6318 ms | Output-only speech instructions no longer inherit router tool-selection/speech-prohibition instructions. Safe exception/collector reason codes are now logged. Acknowledgement and retrieval run concurrently in separate tenant-scoped contexts, with answer publication ordered after acknowledgement. | Actual production-prompt synthetic Realtime probe generated acknowledgement PCM; independent release validation retained. Original exception subtype was not logged, so the historical provider failure is not retrospectively proven. Live microphone-to-speaker acceptance remains pending. |
| RESUME-02 (P1) | Retained “Continue the presentation.” was semantically complete but held; no route request | Local capture windows no longer count as pending provider speech. Raw-frame queue occupancy no longer blocks complete-turn release. Completeness receives presentation context. Complete commands have a separate bounded lifetime and explicit expiry diagnostics. | Semantic-input/buffer/reasoner tests; real provider selected resume in the synthetic tool probe. |
| INPUT-02 (P1) | 437 dropped frames, maximum frame age 9535 ms during lookup | Grounded work leaves the listening loop free; queued input fragments preserve order in a bounded queue. Each input is revalidated before routing. | Running worker test blocks retrieval, observes acknowledgement playback, then verifies additional microphone frames reach the provider before lookup is released. |

Verification: targeted suite passed 92/92; broader suite passed 259/260 with one
SQLite shared-connection lock in the idle host-stop fixture. That lifecycle case
passed in the targeted rerun. Live synthetic probe used `gpt-realtime-2.1-mini`,
generated a 3550 ms source-check acknowledgement, and selected start, pause, resume,
grounded question and social tools correctly. It published no audio and performed
no presentation actions. Evidence: `TestResults/dialogue-targeted-final.trx` and
the opt-in `scripts/AgentDialogueProbe --live --checking --playback` probe.

Remaining live acceptance: consenting participant asks an onboarding question,
hears acknowledgement then grounded answer, asks to continue, and hears narration
resume from the saved checkpoint. Do not treat synthetic provider success as
proof of browser audibility. Consent, evidence, owner/generation, approval modes,
room expiry and usage ceilings remain enforced.

Final worker recheck: 10/10 passed, including the delayed-lookup case and explicit
assertion that answer speech is queued/processing/spoken after acknowledgement.
API LocalRun build succeeded (45 existing warnings, zero errors) and restarted as
PID 40300, run `20260929103523256`. Database, speech interruption, shared Realtime
and browser Sales-room readiness are healthy. Aggregate readiness is degraded by
B2Brouter and the separate optional Sales meeting voice pilot; this does not verify
browser microphone-to-speaker delivery.

### 2026-09-29 confirmed question versus advancing narration

| Issue | Baseline evidence | Change | Acceptance status |
|---|---|---|---|
| TURN-03 (P1) | API run `20260929181640220`: the confirmed participant turn routed to `ask_grounded_question`, but narration advanced during tool selection; backend returned `conversation_stale` and no lookup began. | A complete, attributed turn now reserves the agent-owned presentation floor, cancels active/queued narration, requests playback stop and captures the new durable generation/checkpoint **before** requesting a Realtime tool. The already-authenticated input alone carries across this worker-owned preemption; the eventual tool still rechecks consent, participant, owner, mode, room lifetime and exact binding. Presentation-version mismatch is checked before mutating agent health. No volume-only or incomplete turn can reserve the floor. | Deterministic worker tests cover a gap between narration segments and active narration cancellation, then verify grounded lookup acceptance and acknowledgement/answer queuing. Live RØDE microphone-to-speaker acceptance with the rebuilt API remains pending. |

Focused run: 15/16 passed; the unrelated incomplete-turn fixture hit a transient
SQLite shared-connection lock. Its isolated rerun passed 3/3. Test reports:
`tests/VirtualCompany.Api.Tests/TestResults/turn-reservation-final.trx` and
`turn-reservation-semantic-rerun.trx` in the same directory. The two new
presentation-reservation cases passed in the focused run. No running API or meeting
state was changed as part of this check.
