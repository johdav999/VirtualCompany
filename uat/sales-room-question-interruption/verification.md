# Spoken question interruption — 2026-09-17

## Product profile

- Product: Virtual Company local ASP.NET Core API and Blazor browser meeting.
- Flow: Organizer starts autonomous presentation, speaks a question while narration is playing, and expects an answer or actionable feedback.
- Room: `d998f346-a6bc-4c40-8182-d19251f3d5ea`.
- Evidence: user browser report, SQL Server room state, current API logs, focused deterministic tests.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Status |
|---|---|---|---|---|---|---|
| ROOM-QA-001 | P1 | Spoken question during narration | Defect | Narration stopped, but stale durable presence reported zero connected humans, so the implicit question path returned silently. Transcript retention was also disabled. | The current microphone speaker counts as live even when durable presence is stale. A lone spoken question enters routing; without transcript retention the room persists `transcript_retention_required`; with retention it can enter answering. A genuinely connected second human still prevents implicit addressing. | Verified by focused deterministic substitute; live microphone retry pending. |
| ROOM-QA-002 | P1 | Spoken question during narration | Defect | After retention was enabled, the transcript was retained but lost its interruption attribution because response cancellation completed before the queued speech-start event read room state. | Record active-response interruption at the input frame and carry it into transcript correlation regardless of later room-state changes. Add a deterministic response-control race regression. | Verified by focused deterministic substitute; live microphone retry pending. |
| ROOM-QA-003 | P1 | Speech recognition degrades question grammar | Defect | The latest interruption was attributed correctly, but transcription rendered the spoken question as `Find us agent two`; the question-word heuristic silently rejected it. | A substantive, non-overlapped utterance from the only connected human that interrupts narration enters addressed-turn routing even when ASR loses interrogative grammar. Short noise and acknowledgements remain ignored. | Verified by focused deterministic substitute; live microphone retry pending. |

## Baseline evidence

- Room persisted `AgentLastErrorCode=human_speaking`, floor state `human`, and no pending turn.
- No `sales_meeting_questions` or answer speech row was created for the reported turn.
- Organizer participant: AI processing allowed, transcript retention disallowed, durable `Connected=false`.
- The current API log contains no exception or room-specific question entry. Historical narration exceptions are from 2026-09-16 and unrelated to this turn.
- The server received live microphone audio because it preempted narration. Therefore the current detected speaker is positive live-presence evidence even when the provider-presence projection is stale.

## Fix

- Count the active detected speaker as connected and use the media connection for other admitted participants.
- Keep multiple-human ambiguity protection: a second live human prevents implicit addressing unless the agent is named.
- Keep transcript consent enforcement. The fix does not retain or answer from speech without transcript-retention consent.
- Add a content-free technical log when a recognized question is withheld for missing transcript retention.
- Capture whether response cancellation interrupted active narration at the microphone speech-start frame and carry that immutable value through transcript correlation.
- Add a content-free routing-decision log when a retained transcript does not enter question routing.

## Verification

- `SalesRoomFloorTests`, `SalesRoomAgentLeaseTests`, and `SalesRoomVoiceActivitySegmenterTests`: 47 passed, 0 failed.
- Regressions cover stale durable presence for the active speaker, multiple-human ambiguity through the live-media predicate, and response-control interruption state surviving cancellation.
- API build completed with 0 errors and was deployed as PID 35656 from run `20260917175733419`.
- The restarted API is listening on port 5301 with empty stderr. `database`, guided dialogue, and shared realtime agent checks are healthy. Overall health remains 503 because the restarted process initially reports the prior room owner as stale and unrelated B2Brouter/media-probe checks remain unavailable.
- Live microphone replay remains pending because it requires the organizer to speak during narration in the refreshed browser room.

## Second live attempt

- Organizer granted transcript retention at 17:51:44 UTC and started agent generation 30.
- A non-overlapped 62-character transcript was retained for 17:52:07–17:52:14 UTC.
- Retained text was a clear English question and matched the existing question heuristic.
- Narration was interrupted, but no meeting-question or answer-speech record was created; floor returned to `human` with no pending turn.
- Root cause: `ReadInputAsync` cancelled the response before the main event loop processed `SpeechStarted`. The later handler inferred interruption from mutable room health, losing the true state at the input frame.
- Fix captures the return value from response cancellation and carries it on the runtime event. Later room-state changes can no longer erase interruption attribution.

## Third live attempt

- Agent generation 32 started at 18:03:30 UTC. Narration was interrupted at 18:04:01 UTC.
- The retained, non-overlapped transcript was `Find us agent two` and no question row was created.
- The deployed diagnostic marker recorded `InterruptedAgent=True`, `ExplicitlyAddressed=False`, and `ConnectedHumans=1`, proving that interruption attribution and live-presence counting both worked.
- Root cause: the speech recognizer damaged the question grammar, and routing still required a question mark or recognized interrogative word.
- Fix treats a substantive interruption by the room's only human as an addressed turn while continuing to ignore short noise and acknowledgement phrases.
- The transcription session now supplies the meeting's agent vocabulary and asks the provider to preserve interrogative wording, reducing the specific `finance agent` / `find us agent` substitution.
- The exact retained phrase plus short noise and acknowledgement regressions passed in the 50-test focused suite.
- API build completed with 0 errors and the final vocabulary-aware build was deployed as PID 51116 from run `20260917181431169`.
- The restarted process is listening on port 5301 with empty stderr and no failure log entries. Database, guided dialogue, shared realtime agent, and Sales browser-room health checks are healthy. Overall health remains 503 because unrelated configured integrations are unavailable.
