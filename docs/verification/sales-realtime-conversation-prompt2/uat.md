# Sales Realtime conversation adapter — Prompt 2 verification

2026-09-27. Scope: shared server-side OpenAI Realtime session adapter and flag-gated browser-room input/context wiring. No database, meeting, consent, provider-admin or microphone state was changed.

| Scenario | Deterministic evidence | Result |
|---|---|---|
| Two successive confirmed turns | State test supplies two distinct turns, records only a played approved response, checks bounded explicit context and response/audio correlation. | Pass |
| Tool call and result | Completed registered call with JSON arguments and stable call ID; unknown, uncorrelated, malformed, oversized and duplicate calls/results rejected; one explicit continuation. Argument deltas are not executable. | Pass |
| Cancellation and late output | Identified response/item and received PCM bound truncation; out-of-band responses do not enter history; cancelled or completed response audio is dropped; a new session has no old turn/context. | Pass |
| Profile compatibility | Legacy explicit-commit PCM, automatic-turn PCM, WebRTC, selected voice, event/usage normalization and new conversation profile covered. Profile uses application commit and far-field noise reduction. | Pass |
| Room safety | Existing answer playback, floor, consent, host takeover, lease and quota regressions run with the adapter changes. Confirmed retained input only; provider audio still blocked from the room publisher until Prompt 3 release checks. | Pass |

Focused command: `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiRealtimeConversationStateTests|FullyQualifiedName~OpenAiRealtimeAgentSessionGatewayTests|FullyQualifiedName~SalesRoomFloorTests|FullyQualifiedName~SalesRoomPlaybackWorkerTests" -v:q -p:WarningLevel=0` — 88 passed, 0 failed, 0 skipped. API build passed with zero errors. Existing unrelated compiler warnings were suppressed in this verification command, not fixed.

Live synthetic-audio provider smoke and browser-device UAT remain unverified. The environment has an OpenAI key but no configured synthetic speech fixture (`VC_ROOM_TRANSCRIPTION_WAV`); no microphone was started and no external provider call was made. Before enabling the flag for a user, run a bounded consented fixture through the persistent session, verify two turns, a correlated response and usage, then repeat with a real room while checking host stops, audio output release and provider expiry. Prompt 2 does not itself publish generated conversational audio or add follow-up/resume behavior; those are later prompt stages.
