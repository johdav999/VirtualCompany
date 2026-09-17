# Sales room false interruption — 2026-09-16

## Profile and evidence
- Product: Virtual Company, local ASP.NET Core API + Blazor browser room.
- Role: organizer. Room: d998f346-a6bc-4c40-8182-d19251f3d5ea.
- Report: agent stopped and assigned the floor to Organizer although nobody spoke; user screenshot shows paused agent and acknowledged playback stop.
- Runtime: tracked API port 5301, Web port 5062. Launcher: run-api.ps1.
- Source path: local energy detector -> segmenter SpeechStarted -> immediate cancellation and HumanStarted, before transcription.
- Original defaults: 80 ms onset; raw RMS/peak included DC bias. Pending onset had no timestamp or sequence continuity check.
- Historical stored room records do not retain an acoustic sample of the reported event; the precise noise or echo source cannot be determined. Do not describe the screenshot as proof of audible human speech.

## Issue ledger
| ID | Severity | Expected / observed | Acceptance | Status |
|---|---|---|---|---|
| VAD-001 | P1 | Short noise and discontinuous bursts must not take the floor; original detector could report speech for both. | DC plus quiet variation, brief energetic burst, separated bursts, duplicate frames do not trigger; sustained varying input still triggers. | Verified by deterministic PCM and frame replay tests. |

## Changes
- Measure AC energy after subtracting each frame's DC mean.
- Calibrate each microphone for 300 ms and require onset energy at least 2.5 times its ambient RMS; adapt the noise floor outside active speech and retain calibration between utterances.
- Default onset confirmation 240 ms instead of 80 ms.
- Reset unconfirmed onset and pre-roll on >100 ms transport gap or missing sequence; ignore duplicate/out-of-order frames.
- Keep enough pre-roll to cover confirmation even when configured pre-roll is shorter.
- Preserve existing synchronization, bounded utterances and cancellation/floor behavior.

## Verification
- Focused API test run: SalesRoomVoiceActivitySegmenterTests, 13 passed, 0 failed.
- Existing tests retain an explicit 80 ms custom configuration; new real-detector tests exercise the new production default.
- No raw microphone recording was available. Deterministic replay is the strongest safe substitute; physical speaker echo and room noise still require a live user retest.
- This remains an energy detector, not a semantic speech classifier. Abrupt new loud noise or residual echo can still qualify; no claim of complete acoustic noise rejection.
