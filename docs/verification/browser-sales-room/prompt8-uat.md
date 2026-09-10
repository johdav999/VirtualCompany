# Prompt 8 floor control, interruption and takeover UAT

Product: Virtual Company browser Sales room. Role: organizer with two admitted fictional participants.
Environment: domain/service fixtures, compiled Blazor component, Node browser lifecycle harness, and local headless Chrome.
Reference: [generated design](../../design/references/browser-sales-room-floor-control-reference.png), [written prompt](../../design/references/browser-sales-room-floor-control-reference-prompt.md).

## Implemented flow

The Sales-owned durable floor records the host and optional preauthorized co-host, current owner, pending addressed turn, overlap, control mode, room turn, response generation, presentation version, slide, talking point and resume offset. Manual mode holds an addressed question for invocation, assisted mode holds it for confirmation, and autonomous mode releases only a completed evidence-backed answer. Ordinary human conversation produces no agent turn; overlap stays in a waiting state.

Local 20-ms PCM speech-start detection cancels the active response token, flushes the room media output, preempts presentation narration, advances the durable generation and asks every connected client to detach the agent track before transcription completes. Stop receipts are persisted per participant generation and exposed as acknowledged or timed out. A later playback-start generation cannot be reopened by an older event.

Takeover is authorized for the organizer or a preauthorized admitted company co-host. It wins without depending on a stale UI version, returns presentation control to manual, fences queued/processing speech and slide mutations, flushes provider/adapter/client playback, and persists the exact slide/talking point for reconnect. Resume revalidates controller authority, consent, presentation version and audience render state, then reopens the approved narration from the saved PCM offset. Autonomous narration advances through the existing bounded conductor, waits for exact new-slide render readiness, rechecks consent/floor generations, and pauses visibly when readiness or an approved narration segment is missing.

The host UI shows floor owner/status, mode, resume/takeover, pending-turn approval, render readiness and client-stop receipts. Guests have an explicit typed “Ask Alex” action after admission and consent. Chrome captures of the real component and compiled scoped CSS passed semantic and overflow assertions at [1440 px](prompt8-host-floor-1440.png) and [390 px](prompt8-host-floor-390.png); machine-readable results are in [prompt8-browser-checks.json](prompt8-browser-checks.json).

## Verification

- Floor, address policy, lease/VAD and conductor suite: 23 passed.
- Teams interruption/conductor/shared-media regression suite: 37 passed.
- Web component suite: 11 passed; JavaScript media lifecycle suite: 11 passed.
- API and Web builds pass. EF reports no pending model changes.
- Migration `20260910150531_AddSalesRoomFloorControl` is additive. It adds floor state, stop acknowledgements and speech response generation.
- SQL Server fresh/upgrade tests were discovered but skipped because `VC_SQLSERVER_TEST_CONNECTION` was not configured.

## Limits

`LIVEKIT_URL`, `LIVEKIT_API_KEY`, and `LIVEKIT_API_SECRET` were absent. A real three-human call in English and Swedish, physical-device echo/background noise, live organizer loss, and customer-audible p95 takeover-to-silence/answer latency remain unrun. The automated evidence covers deterministic silence/noise/short-trigger VAD behavior, overlap provenance, immediate production cancellation calls, generation fencing, browser track detachment and responsive controls. It does not convert the local 80-ms detector threshold into an end-to-end audible latency claim.

No Teams resource was enabled, invoked or changed. Existing Teams source, routes, configuration, migration history, deployment assets and stored semantics remain present.
