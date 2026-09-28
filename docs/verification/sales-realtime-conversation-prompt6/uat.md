# Prompt 6 — conversation acceptance and operational readiness

Date: 2026-09-27. Product: Virtual Company, browser sales meeting (web/AI workflow).
Revision: `66e29aeb` plus uncommitted Prompts 1–6. Role: organizer; guest privacy is checked
separately. Environment: .NET 9 deterministic domain, provider-contract, SQLite worker/service
and Blazor component tests. No live meeting, calendar, microphone, consent, secret, limit or
rollout setting was changed. See [operational runbook](../../sales-realtime-conversation-runbook.md).

## Predeclared live acceptance budgets

These are proposed release gates, set before any live run, not measured results or permission
to spend. Obtain the test owner's agreement before activating the provider or audience.
Run at least 20 labeled question/reply cycles; record p50/p95/max and failed/censored samples.

| Measurement | Gate | Measured live |
|---|---|---|
| Confirmed words to first audible grounded answer | p95 <= 8 seconds | Not measured |
| Audible answer end to follow-up end/listening | p95 <= 8 seconds | Not measured |
| Continuation request to backend acceptance | p95 <= 2 seconds | Not measured |
| Cancellation to observed stopped playback | p95 <= 750 ms | Not measured |
| Duplicate tool delivery | zero additional state changes or speech | Not measured |
| Cost | <= USD 2 total for one <= 10-minute run, or lower existing cap; stop on either bound | Not measured; no calls made |
| Noise/echo | zero unintended interruptions in 20 labeled noise-only trials; separately report intentional speech success | Not measured |

The server transport histograms are proxies, never substituted for audible measurements.
Budgets failing in a real run block rollout; do not relax guards, increase caps, or report a
mock timing as production latency. Only approved non-sensitive fixture facts may be spoken.

## Evidence packets

### FLOW-01 — Two-question autonomous conversation

Preconditions: active consented room, approved deck/source, autonomous mode, valid lease/budget.
Steps: start deck; interrupt with onboarding question; play grounded answer then generated
follow-up; continue at the checkpoint; interrupt again with a finance question.
Expected: exact slide/point/offset, one release per question, no approval click or duplicate.
Observed automated substitute: `Answer_bridge_exact_resume_then_second_question_uses_new_release_and_never_replays_first_answer`
uses production playback, relational resume and confirmed-question handling; checks 20 ms
saved offset, cancels resumed narration, releases the second answer and refuses the old turn.
Retrieval, wording and media are deterministic seams. Separate existing narration tests check
full-deck progression. This is not a single live microphone-to-speaker test.

### FLOW-02 — Contextual replies and injection

Inputs: “yes” after an unambiguous continuation invitation; “Please carry on with the slides”;
“Yes, but who approves the setup?”; “not yet”; “I suppose so”; “Could you say that again?”;
silence; affirmative after a two-part/ambiguous follow-up; source instruction requesting an
unapproved promise or tool action. Expected: question beats continuation; uncertainty/silence
wait; no factual bridge or permission expansion. Observed: reasoner contract fixtures, bridge
validation, empty-argument tool matching, domain version/authority and provider duplicate
tests. These verify enforcement and model-output handling, **not live classification accuracy**.
Live paraphrase/repeat quality remains gated. Inputs are test data, not a production phrase table.

### FLOW-03 — Approval and changing controls

Repeat question in manual/assisted; switch modes during retrieval, generation, bridge playback
and queued resume; withdraw consent/take over. Expected: no stale automatic speech or resume.
Observed: new relational retrieval/bridge-stream tests plus existing generation/resume/floor
tests. Nonautonomous answers remain private and no speech is queued; buffered bridge output
is cancelled on downgrade/withdrawal. Host/guest component tests preserve private controls.

### FLOW-04 — Audio and failure boundaries

Expected: local noise candidates alone never interrupt; current confirmed participant input
is required; overlap, disconnected/replaced tracks, mute/revocation, lease replacement,
expiry, provider errors and caps fail safely. Existing segmenter tests include deterministic
fan-like noise, clicks/tones and labeled short speech fixtures. Correlator/floor/provider tests
cover empty transcript, out-of-order input, multiple participants, cancelled/late output,
network/session reset and ownership fencing. These are not measured physical fan/echo or
background-conversation accuracy; real RØDE microphone/speaker and two-human trials remain open.

### FLOW-05 — Operations, telemetry, rollback

Expected: fixed-label metrics, no sensitive content, no history loss, default-off rollback
fences bridge/resume and preserves manual controls. Observed: timing/duplicate/failure/audio
metrics tests, existing rollback playback and UI tests. No schema/configuration change.
SQL Server simultaneous Stop/resume race and live exporter/reconnect remain staging checks;
SQLite concurrency is not proof of SQL Server locking behavior.

## Issue ledger

| ID | Severity | Flow | Finding | Acceptance / evidence | Status |
|---|---|---|---|---|---|
| UAT-P6-01 | P1 | 01/03 | Prompt 5 health dependency missing from Prompt 4 continuation fixture caused six regression failures | Supply a real fixture health implementation; keep production dependency mandatory; rerun continuation/approval tests | Fixed; focused pass |
| UAT-P6-02 | P2 | 05 | Conversation timing and duplicate-tool metrics absent; generated bridge/limitation audio omitted | Bounded worker correlation, fixed failure buckets, duplicate counter and exporter registration; metric tests | Fixed; final focused pass |
| UAT-P6-03 | P2 | 02 | Reply classification instructions did not explicitly cover repeat requests or untrusted context | Versioned intent prompt 1.0.1; repeat classified as question rather than continuation; fixture coverage | Fixed; deterministic contract pass; live semantics unverified |
| UAT-P6-04 | P1 | 01–05 | Physical audio, language quality and operational provider run unverified | Complete authorized live matrix within budgets | Blocked: no running local API/Web, active consented audience or agreed test spend |
| UAT-P6-05 | P1 | 05 | Production SQL Server race validation not executed | Staging concurrent Stop/resume with existing unique keys, transaction/concurrency checks | Open staging prerequisite |
| UAT-P6-06 | P1 | 02 | Accepted wait/uncertain reply discarded the remaining reply window, potentially losing the next short response | Preserve the original deadline only for an accepted wait; never extend it or retain stale/failed/resume/question proposals | Fixed; final focused regression below |

## Verification results

Initial combined backend check: 233 passed, 6 failed (UAT-P6-01), 1 explicit live-transcription
skip. Domain: 20 passed. Web room/client: 36 passed. After the fixture and workflow additions:
90 focused backend cases passed. Artifacts are local `tests/*/TestResults/prompt6-*.trx`.
Broader backend regression: **283 passed, 1 opt-in live-transcription skip, 0 failed**
(`prompt6-regression.trx`). Includes tenant isolation, provider contracts, question/capture,
presentation runtime and migration metadata. Persistence/migration files are unchanged by
Prompt 6; no new columns, conversions or schema migration are required.

API and Web explicit `dotnet build --no-restore -clp:ErrorsOnly -v quiet`: both passed,
zero warnings/errors on the final incremental builds. Earlier recompilation emitted existing
nullable/analyzer warnings; this is not a claim of a warning-free clean solution build.
`git diff --check` passed. The final focused run after the bounded-wait fix and telemetry
exporter refactor passed **95 tests, 0 failures/skips** (`prompt6-final-focused.trx`). It covers
`SalesRoomPlaybackWorkerTests`, `SalesRoomConversationTelemetryTests`,
`OpenAiRealtimeConversationStateTests`, and `SalesRoomConversationReasonerTests`.

Reproduce the final focused check:

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SalesRoomPlaybackWorkerTests|FullyQualifiedName~SalesRoomConversationTelemetryTests|FullyQualifiedName~OpenAiRealtimeConversationStateTests|FullyQualifiedName~SalesRoomConversationReasonerTests" --logger "trx;LogFileName=prompt6-final-focused.trx"
```

Counts from the broad and focused passes overlap; do not sum them as unique tests.

## Live blocker and handoff

Read-only listener check found neither port 5062 nor 5301 listening. No test audience/active
meeting with current consent and agreed spend was established. Credentials were not exposed
or assumed missing; live provider entitlement/handshake was not checked. The external
transcription test remains explicitly skipped without its opt-in fixture configuration.
The polish/UAT workflow therefore uses the documented deterministic substitutes above and
does not certify live readiness. Start an authorized test deployment and new test meeting,
confirm device/consent/spend, then execute FLOW-01–05 and retain redacted outcome/timing
evidence. Do not reuse exhausted rooms or silently enable the flag.
