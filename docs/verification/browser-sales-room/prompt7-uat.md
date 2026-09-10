# Prompt 7 consent-aware room agent UAT

Product: Virtual Company browser Sales room. Role: organizer, with two admitted fictional participants.
Environment: isolated SQLite/component fixtures, local SQL Server Express disposable databases, and local headless Chrome.
Reference: [generated design](../../design/references/browser-sales-room-agent-reference.png), [written prompt](../../design/references/browser-sales-room-agent-reference-prompt.md).
The ImageGen built-in generated the reference before the room-agent UI was implemented.

## Flows and evidence

1. Consent and start authority: lifecycle tests admit two humans, record purpose-specific consent, and verify that a non-organizer cannot start the agent. The room domain permits one live lease owner and fences a stale generation after stop/restart.
2. Local speech detection: deterministic 20-ms PCM fixtures cover silence, a one-frame false trigger, a quiet below-threshold utterance, 240-ms bounded pre-roll behavior, 600-ms trailing silence, clipped-start preservation, overlap provenance, and participant buffer clearing. The configured detector confirms speech after 80 ms and calls output cancellation directly on that worker path. This is an application-boundary latency result; customer-audible stop latency was not measured.
3. Consent withdrawal: an admitted participant withdraws transient AI processing while retaining their separate transcript choice. The room immediately clears its lease, advances the turn fence, queues provider removal, and leaves human media/manual slides available.
4. Publication gate: approved narration is reopened through the Prompt 6 release service after conductor readiness; answers require the existing grounded question, evidence, selected agent, and customer-visible release before synthesis and again before publication. Both use the room's single agent media connection and turn generation. Existing shared-output tests reject late frames and drain only the current turn.
5. Host UI: Chrome renders the actual component and compiled scoped CSS at 1440 and 390 pixels. Both sizes have no horizontal overflow and expose consent count, independent voice health, stop control, typed fallback, private evidence, received/detected/forwarded/provider-billed totals, and explicit billing-evidence wording.

Screenshots: [desktop](prompt7-host-agent-1440.png), [mobile](prompt7-host-agent-390.png). Machine-readable DOM checks: [browser results](prompt7-browser-checks.json).

## Issue ledger

| ID | Severity | Finding | Fix / regression | Status |
| --- | --- | --- | --- | --- |
| AGENT-01 | P1 | A single-row queue read failed when an organizer queued more than one valid speech item. | Process queued speech in durable creation order. | Verified by build and review |
| AGENT-02 | P1 | A full runtime-event channel could discard barge-in or unexpected-audio events. | Use bounded backpressure and awaited writes for speech-start/provider-audio events; cancel reader tasks on exit. | Verified by build and shared cancellation tests |
| AGENT-03 | P1 | Approved PCM with a final partial 20-ms frame would be withheld at its tail. | Zero-pad only the final frame while recording the real sample duration. | Verified by build and shared output tests |
| AGENT-04 | P1 | The handcrafted upgrade fixture lacked pre-existing agent/question parent tables required by the additive foreign keys. | Add the minimal historical parents; rerun the preserved-Teams upgrade test. | Verified on SQL Server Express |
| AGENT-05 | P2 | Disabled room AI could appear as an ordinary waiting state. | Map disabled configuration to unavailable voice/state in the organizer status. | Verified by component contract |

## Configuration and operations

`SalesRoomAgent` is disabled by default. Production enablement also requires the existing `SalesBrowserRoom`, `SharedRealtimeAgent`, and approved-speech/narration configuration. Supported controls are:

| Setting | Default | Valid range / meaning |
| --- | ---: | --- |
| `Enabled` | `false` | Explicit environment opt-in |
| `LeaseSeconds` / `RenewalSeconds` | `30` / `10` | 15–120 / 5–60; renewal must be shorter |
| `PreRollMilliseconds` | `240` | 200–300 |
| `TrailingSilenceMilliseconds` | `600` | 500–800 |
| `MinimumSpeechMilliseconds` | `80` | 40–300 |
| `MaximumUtteranceSeconds` | `30` | 5–60 |
| `MaximumInputAudioSeconds` / `MaximumOutputAudioSeconds` | `1800` / `1800` | 60–7200 per room-agent session |
| `MaximumSessionMinutes` | `60` | 1–120 |
| `SpeechRmsThreshold` / `SpeechPeakThreshold` | `320` / `700` | Local detector thresholds |
| estimated input/output cost per minute | `0` | UI estimate only; configure dated rates |

Provider-reported billed duration is stored separately from received, locally detected, and forwarded duration. When a provider does not return duration evidence, the UI says “Not reported” and makes no savings claim. Tokens remain reported separately. No raw microphone PCM is persisted.

## Verification and limits

- Focused backend suite: 23 passed after the final consent/lease/VAD/provider changes.
- Browser component suite: 11 passed. Chrome desktop/mobile checks passed with visual inspection and no overflow.
- Teams/shared media preservation suite: 61 passed. No Teams-specific source, test, script, or historical migration changed.
- SQL Server migration evidence: the complete fresh chain passed; the final upgrade path passed after the fixture correction and retained its seeded Teams call row and conferencing conversions. EF reports no pending model changes.
- API and Web builds pass with existing repository warnings.

No `LIVEKIT_URL`, `LIVEKIT_API_KEY`, or `LIVEKIT_API_SECRET` was configured. A real multi-human LiveKit call, physical microphone behavior, provider reconnect billing, and customer-audible stop/answer latency therefore remain unrun. The strongest safe substitute uses the production VAD, consent lifecycle, release checks, output-generation primitive, compiled component, real Chrome, and local SQL Server. This record does not relabel the 80-ms detector threshold as end-to-end audible latency or claim live rollout readiness.

Reproduce the local checks with the focused `SalesRoomVoiceActivitySegmenterTests`, `SalesRoomAgentLeaseTests`, `SalesBrowserRoomLifecycleTests`, `SalesRoomMediaTransportTests`, `OpenAiRealtimeAgentSessionGatewayTests`, and `SalesHumanRoomTests`. Set `VC_BROWSER_UAT_DIRECTORY` to an absolute `artifacts/browser-human-room-uat` path, then run `node tests/scripts/Capture-BrowserRoomAgent.cjs` with Playwright available in `NODE_PATH`.
