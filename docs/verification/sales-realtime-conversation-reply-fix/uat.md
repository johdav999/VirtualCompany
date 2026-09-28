# Sales conversation reply-routing repair — 2026-09-27

## Scope and acceptance

User-reported flow: autonomous presentation -> spoken question -> grounded answer ->
no conversational follow-up; thanks ignored; an explicit request to continue sent to
source lookup. Repair the existing hybrid conversation, not unrestricted factual audio.
Preserve approvals in other modes, source release checks, consent, meeting timing,
usage history, spending/audio limits and the authoritative saved presentation cursor.

## Evidence and issue ledger

| ID | Root cause / evidence | Repair and acceptance |
|---|---|---|
| RC-01 | Live `/agent` status returned `phase=legacy`, `availability=off`. Local development configuration did not enable `HybridConversationEnabled`. | Enable only local Development pilot; restart with a new provider profile. General deployment remains gated. |
| RC-02 | Lexical question detection ran before contextual routing, excluding “Can you continue presenting?” from intent interpretation. | Route fresh contextual replies first; model-classify new facts versus explicit resume, then enforce backend authority. |
| RC-03 | Continuation required a spoken bridge, although the model can legitimately skip one. | A completed released answer also establishes the bounded reply window and supports explicit continuation. |
| RC-04 | Thanks was unknown/wait with no speech. | Classify clear social acknowledgment; generate an independently validated short conversational follow-up, without new factual lookup or deck movement. Model may safely decline unsafe/unnecessary speech. |
| RC-05 | Only one follow-up per original answer may enter provider context. Reusing that original turn for a social reply would reject its playback record. | Bind social replies to their own confirmed input turn, retaining the original one-follow-up guard. |
| RC-06 | Hot enabling could leave an already-running legacy socket under conversation code. | Either flag direction requires worker restart; never change socket profile in place. |

## Verification

- Focused relational/playback, floor, tool, status, reasoner and provider-context suite:
  **178 passed, 0 failed**, TRX `tests/VirtualCompany.Api.Tests/TestResults/conversation-reply-fix.trx`.
- Final reasoner prompt change: **20 passed, 0 failed**, TRX
  `tests/VirtualCompany.Api.Tests/TestResults/conversation-reasoner-final.trx`.
- Coverage includes explicit resume without a generated bridge, correct 20 ms checkpoint,
  thanks without new question/resume, duplicate social tool idempotency, actual fake-transport
  playback completion, social reply provider correlation, stale expiry, cross-company scope,
  manual/assisted mode, withdrawn consent and mode changes during generation.
- Reasoner tests use structured gateway doubles; they verify routing/schema/release behavior,
  not live model quality. Playback tests use the repository transport double, not a speaker.
- Local API is rebuilt/restarted via the existing `run-api.ps1` launcher. Runtime verification
  is recorded below after startup. No database data or quota reset is part of this repair.

### Deployment verification

- LocalRun API build succeeded (129 seconds; existing compiler warnings, no errors).
- New API process started at `2026-09-27T14:22:30Z` from runtime snapshot
  `.codex-build/api-runs/20260927142018022`.
- `/health/live`: Healthy.
- Authorized room `/agent`: `state=stopped`, `conversation.phase=not_started`,
  `conversation.availability=available`, with explicit start/consent instruction.
  This replaces the observed pre-fix `legacy/off` status. It verifies local configuration
  and deployment, not a provider session handshake or audible follow-up.
- `git diff --check`: passed. Existing unrelated worktree changes were preserved.

## Remaining live acceptance

Refresh the room and explicitly start the agent in autonomous mode. With the user's chosen
microphone and sound enabled, ask a grounded question, listen to the answer/follow-up, say
thanks, then explicitly request continuation. Confirm the saved presentation point resumes.
Also ask a new factual question and verify grounded retrieval rather than automatic resume.
No microphone, recording consent, or agent speech was activated automatically during repair.
Physical microphone-to-speaker quality/latency and live model intent quality remain unverified;
the test evidence above is the strongest deterministic substitute, not a claimed live pass.

The polish-uat-loop skill guided the issue ledger, scoped repair, regression tests and explicit
separation of transport-test evidence from live audibility. OpenAI's official Realtime
conversations guide was checked for tool/result and response lifecycle semantics; this repair
does not change the provider wire schema or model selection.

## Follow-up repair — captured question and tool-result failure

User clarified that the intended question concerned onboarding. Retained input instead
contained a short social affirmative; a later explicit presentation-continuation request
was captured correctly. No microphone recording was retained, so the cause of the lost
words cannot be established from the saved transcription alone.

| ID | Evidence | Repair |
|---|---|---|
| RC-07 | Single-speaker interruption heuristic admitted a social affirmative as a factual question and retrieved generic company material. | Model-classify initial confirmed input before interrupting or invoking factual answering. Unknown fragments and acknowledgements do not create factual questions. No missing topic is invented. |
| RC-08 | Actual gateway synthetic dialogue reproduced OpenAI `invalid_tool_call_id`: custom-input response calls were absent from the default conversation receiving the tool output. | Keep requests out-of-band; explicitly pair the accepted function call with its backend result in the continuation input. Preserve call/turn validation, deduplication and backend authority. |
| RC-09 | Lease cleanup replaced the prior paused error with a lease-expired message. | Preserve the original paused error unless the operator explicitly disables/drains AI; log provider completion status/reason/code without transcript or raw response. |
| RC-10 | A synthetic continuation occasionally exhausted the 128-token completion bound. | Give the receipt-only continuation explicit brief instructions and a bounded 512-token ceiling. This internal text is not published as speech. |

Live synthetic adapter verification: confirmed input -> continuation tool -> accepted backend
result -> response completion succeeded without provider error after repair. No microphone,
meeting content, consent changes or room commands were used by the probe. This verifies
provider protocol compatibility, not real microphone capture or audible meeting behavior.
Official reference: [Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations).

Initial focused regression run: 192 passed, 0 failed. Final focused test/build and deployment
results are recorded below. The polish-uat-loop skill guided this evidence-led repair;
the openai-docs skill guided protocol verification. No quota, usage or meeting-time reset.

Final verification: **198 passed, 0 failed** in
`tests/VirtualCompany.Api.Tests/TestResults/conversation-followup-repair-final.trx`.
LocalRun API build succeeded; process 44352 started at `2026-09-27T15:34:06Z`
from `.codex-build/api-runs/20260927153343342`. Health is Healthy. Authorized
`/api/sales/browser-rooms/{room}/agent` reports stopped / not_started with conversation
available. The pre-existing saved lease-expired error remains until an explicit new start;
this repair does not rewrite historical room errors or activate AI automatically.
Actual RØDE microphone recognition and audible follow-up/resume still require user testing.

## Split-sentence repair — 2026-09-27 evening

Same local host/profile/room. Reported flow: announce a question, pause, finish the onboarding
question; later ask onboarding again after a bridge. Read-only retained evidence showed
`question how` became a partially-supported generic company answer. The later complete
question reached intent reasoning at 16:54:54 UTC, but no question record followed.
The worker remained ready, floor host, with no provider failure; this was a dropped turn,
not a confirmed worker crash. Prior logging cannot identify which proposal predicate rejected it.

| ID | Severity | Evidence / cause | Repair / acceptance |
|---|---|---|---|
| RC-11 | P1 | Local silence commits were treated as whole turns; intent classification alone accepted an unfinished interrogative. | Separate structured model completeness verdict before routing. Hold incomplete consented text; combine only same room/participant/consent/microphone/owner/turn continuation. Expiry discards, never answers. |
| RC-12 | P1 | The previous acknowledgement fix did not independently validate sentence completeness. | Incomplete verdict blocks factual lookup even if the intent model says Question. No phrase table or word-count completion rule. |
| RC-13 | P1 | Contextual questions passed through redundant intent and realtime tool-selection decisions with silent rejection. Exact historic rejecting predicate was not logged. | Complete model-classified questions directly invoke existing grounded workflow and its current authority/release checks. Control tools are pinned to the classified intent; rejected proposals log safe diagnostics. |

This uses semantic **transcript assembly**, not OpenAI audio `semantic_vad`: the existing
per-participant manually committed capture/correlation contract remains intact. OpenAI
[semantic VAD documentation](https://developers.openai.com/api/docs/guides/realtime-vad)
confirms why silence alone cannot establish thought completion. A future streaming-VAD
change must preserve those participant/consent boundaries rather than just flipping a setting.
The 12-second/2000-character limits bound ephemeral retention, not language judgment.

Actual production completeness prompt evaluated through an isolated synthetic HTTP harness
with the configured `gpt-4.1-mini`: **6/6 expected verdicts**. Both unfinished lead-ins were
incomplete; combined onboarding, mixed-language onboarding, thanks and explicit continuation
were complete. This evaluates the real prompt/model without claiming microphone end-to-end
coverage. Automated tests additionally cover consent/tenant/track isolation, expiry, bounds,
missing verdict fail-closed, and incomplete-question retrieval prevention.

Verification: **145/145 focused tests passed** (`semantic-turn-assembly.trx`), including
the new buffer/completeness/required-tool cases. Web build passed without warnings/errors.
The six live synthetic cases also passed with the exact production shared-gateway
system/user message builders and Chat Completions settings (JSON mode, temperature 0.1).
API LocalRun build succeeded in 134 seconds; process 55676 started at 17:11:49 UTC
from `.codex-build/api-runs/20260927170931093`. Health Healthy; authorized room status
ready, conversation available, no current error. No timing, usage, limit or consent reset.
Original physical microphone pause/resume flow remains pending user acceptance; the
historic contextual rejection predicate cannot be reconstructed from the previous logs.

## RC-14 — Complete continuation stranded behind microphone activity (2026-09-27)

P1, same local organizer/room flow: after an answer and bridge, say “Can you
continue the presentation”. Expected: resume via the authorized continuation tool.
Observed: retained transcript “You continue the presentation instead.” at
17:16:03–17:16:06 UTC, followed by `Room turn held for more speech ... Complete=True`
in `.codex-build/api-runs/20260927171407109/api.stdout.log`. No continuation followed.
The complete-turn branch shared the fragment buffer but could only drain on a new
nonempty transcript. Empty noise completions or an idle microphone could not release it.

Repair: release only semantically complete buffered text once local capture,
transcription and queued events drain; re-enter consent, participant, track, room,
owner and semantic validation without duplicate transcript retention. Incomplete,
expired, superseded and cleared turns are never released by silence. Log the two
transport fences separately for future diagnosis.

Separate stop: database state records `quota_exceeded` at 17:16:31 UTC, with the
cumulative 60-minute input allowance exhausted. Preserve the requested 60-minute
limit and usage history. A new authorized meeting is required for physical-microphone
acceptance testing; do not claim that this repair resets or bypasses the allowance.
Regression coverage: AgentSpeechTurnBufferTests complete-idle release, exactly-once,
incomplete, expiry, replacement and clear; existing continuation authorization tests.
Final focused verification: **139/139 passed**, `continuation-drain-final.trx`.
The replay runs after usage persistence and before selecting new playback; accounting
and the 60-minute limit remain unchanged. Physical microphone acceptance is blocked
on a new authorized meeting, not marked passed by these component/service tests.
Local API build succeeded in 130.4 seconds; deployed process 58156 at
17:27:44 UTC from `.codex-build/api-runs/20260927172531371`.

## RC-15 — Assembled question rejected before autonomous audio (2026-09-28)

P1, same organizer/room in autonomous mode. User asked “How is onboarding done?”;
latest retained text is “How does onboarding work?” at 05:54:35 UTC. Immediately
preceding retained fragment is “ま”, with matching participant consent version 53,
agent generation 14 and microphone track generation 1. The semantic buffer joins
matching fragments across pauses, but browser-question validation only accepted a
single segment whose content equalled the whole assembled question. It threw
SalesMeetingCaptureValidationException and the worker stopped with agent_worker_failed.
No new question/answer was persisted; the UI displayed yesterday's “is onboarding done?”
answer. Runtime evidence: `.codex-build/api-runs/20260928052346106/api.stdout.log`.

Repair: validate an exact concatenation of retained browser fragments anchored at
the final segment, with company/session/room/speaker/consent/participant generation/
owner generation/track boundaries, overlap rejection and a bounded pause window.
Every raw fragment must match its linked meeting transcript. No invented words,
paraphrases or foreign evidence qualify. Single-segment behavior is preserved.
Existing autonomous release/playback policy already permits supported partial
answers without a host approval click; this failure occurred before that policy.

Acceptance: real retained split turns pass evidence validation; fabricated text,
expired pauses, changed provenance and cross-company/session turns fail. Existing
autonomous partial answer and playback tests must pass. Live microphone acceptance
remains pending after deployment; no historical answer is replayed automatically.
Verification: 126 existing playback/conversation checks passed in
`assembled-question-evidence.trx`; all 24 capture checks passed in
`assembled-question-evidence-final.trx` after correcting the test fixture company
context and supplying an actual foreign participant for its foreign-key test.
Read-only SQL Server production-query replay using the actual retained IDs passed:
single=True, assembled=True, fabricated=False, foreign=False. No question, speech,
usage or consent records were changed by this probe. API build passed in 17.6 seconds;
process 24932 deployed from `.codex-build/api-runs/20260928060159214`.

## RC16 — rejected question terminates meeting presence

P1. The RC15 capture-validation exception escaped the per-question operation into
the worker-level safe shutdown. Media disposal removed Alex's participant track;
lease reconciliation later marked the agent stopped while leaving an agent-owned
presentation floor behind. This was a session-lifetime failure, not an approval requirement.

Repair: isolate capture-validation/conflict failures at the answering boundary.
Recheck company/room ownership, live room and consent before retaining the ready
agent; fence failed speech, preserve the narration checkpoint and media lease,
and permit a new question. Cancellation and unknown infrastructure failures still
propagate. Real policy stops, failed workers and expired-owner reconciliation now
pause the stale presentation floor. No usage, consent or meeting timing is reset.

Verification: relational regression covers both rejection types, a subsequent
autonomous answer and cancellation propagation. Physical-microphone/participant-tile
acceptance remains pending; tests must not be represented as a live voice test.
Focused playback/lease/status suite: 99 passed. Final recovery/cancellation/status
recheck after checkpoint cleanup: 11 passed. `git diff --check` passed (existing
line-ending warnings only). Read-only local API confirmed the reported stopped
generation 14 with `agent_worker_failed` before deployment.
Deployment build passed in 135.2 seconds; API process 32696 launched from
`.codex-build/api-runs/20260928061610707`.

## RC17 — obsolete narration pauses the newly queued answer

P1, autonomous presentation interrupted by a question. Local API evidence shows
agent generation 15, healthy voice, and a `turn_fenced` pause. Database sequence:
narration turn 69/response 126 was withheld, then an approved answer queued for
turn 70/response 128 was also withheld. The room advanced to turn 72. The failure
handler for old narration paused the room without checking that the failed item
still owned the current turn, invalidating the new answer; its handler paused again.

Repair: withheld speech may update its own failure record, but may pause the room
or cancel buffered shared-track output only while its agent, turn and response
generations still match the live room and floor. Stale narration and old answers
cannot mutate a newer response. Existing current-turn safety failures remain pauses.

Acceptance: reproduce an old narration failure after a new autonomous answer is
queued; the room/floor generations remain unchanged and the answer plays. Also
cover response changes within the same turn and a newer turn during publication.
Live physical microphone acceptance remains pending after deployment.
Verification: 88 playback/reply checks passed against the final production change.
The additional in-flight shared-track check passed separately after correcting its
fixture to claim a new narration turn and compare cancellation counts at handoff
(initial track-generation alignment also invokes cancellation). Manual-mode
downgrade continues to discard same-turn audio returned to the host. Evidence:
`obsolete-speech-isolation-final.trx` and `obsolete-track-generation-final.trx`.
Deployment: API build passed in 9.6 seconds; process 14640 launched from
`.codex-build/api-runs/20260928071041606`.
