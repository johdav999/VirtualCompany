# Shared speech-aware interruption detection for agent conversations

## 1. Title and outcome

Implement one reusable, production-ready speech-aware interruption capability for all existing user-facing agent voice conversation paths in Virtual Company. Human speech should interrupt promptly, while background noise should not stop an agent's narration or answer. Named agents must share the implementation; conversation modes may supply explicit policy profiles.

Implement and verify the feature end to end, not just a design or scaffolding. Follow `/production-implementation.md`, `/docs/architecture-rules.md`, repository and scoped `AGENTS.md` instructions. For UI changes, follow `/docs/design.md` and its mandatory workflow, using `/ui-instructions.md` only as its implementation companion.

## 2. Current context

Re-inspect the current checkout and preserve unrelated changes. Known entry points:

- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesRoomVoiceActivitySegmenter.cs`: `EnergyLocalVoiceActivityDetector` uses RMS/peak thresholds. The segmenter adds per-track noise-floor calibration and sustained-onset timing. These are energy heuristics, not a trained speech classifier.
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesRoomAgentCoordinator.cs`: `ReadInputAsync` cancels the response, stops playback, and preempts the presentation as soon as local `SpeechStarted` fires, before a transcript exists.
- `src/VirtualCompany.Infrastructure.Operations/Companies/OpenAiRealtimeAgentSessionGateway.cs`: shared WebRTC/PCM provider configuration; manual-commit PCM sessions disable provider turn detection, while other paths configure server VAD. Do not break manual commit by blindly enabling automatic provider turns.
- `src/VirtualCompany.Infrastructure.Operations/Companies/GuidedRealtimeSessionConfiguration.cs`: guided conversations already configure noise reduction and semantic/server turn detection, with automatic response creation and interruption disabled. Preserve guided orchestration ownership.
- `src/VirtualCompany.Application/Agents/RealtimeAgentSessionContracts.cs`, Sales room media contracts, `LiveKitSalesRoomMediaConnection.cs`, `SalesMeetingRealtimeService.cs`, and `TeamsRealtimeAudioBridge.cs` contain transport/lifecycle boundaries to inspect.
- Existing tests include `SalesRoomVoiceActivitySegmenterTests`, `SalesRoomAgentLeaseTests`, `OpenAiRealtimeAgentSessionGatewayTests`, `GuidedRealtimeSessionConfigurationTests`, and `SalesMeetingRealtimeServiceTests` under `tests/VirtualCompany.Api.Tests`.

Observed failure: sustained microphone noise can stop Alex presenting because a local energy decision cancels playback before actual speech is established. Changing only a provider setting cannot fix that local cancellation path.

Inventory every existing voice entry point, including browser meetings, Teams audio, guided work, and any other realtime agent conversation. Record its capture, detection, cancellation, playback, and turn-completion owners. Do not claim a text-only agent has a voice channel or create new voice products just to satisfy coverage.

## 3. Dependencies

No prerequisite implementation prompt. Existing voice transports, consent, authorization, and shared orchestration are prerequisites to reuse, not replace. Live provider verification requires the existing configured credentials; never embed them in code or evidence.

Select and document a maintained speech/non-speech detector compatible with deployment platforms and redistribution requirements. Verify actual package/model availability, license, integrity checks, runtime/native dependencies, and startup behavior before adopting it. Deliver the real adapter and model packaging, not an interface with only a fake implementation. Consult current official OpenAI documentation before changing provider configuration; distinguish speech onset detection from semantic end-of-turn detection.

## 4. Implementation requirements

### Shared contracts and ownership

- Define provider-neutral interruption contracts in Application and deterministic policy/state rules in their appropriate inward layer. Put shared implementation in the existing shared agent/AI orchestration owner, with DI owned by that module. Sales and other capabilities consume Application interfaces, never sibling capability implementations.
- Separate audio preprocessing, speech classification, interruption policy, turn completion, and transport playback control. Keep one authoritative interruption policy per session even when local and provider events coexist.
- Isolate state by company, agent, conversation/session, participant, track and generation. Use bounded buffers, backpressure, cancellation and deterministic cleanup. Validate sample rates and frame shapes; explicitly resample when required.

### Speech-aware onset and noise handling

- Use a genuine speech/non-speech classifier with confidence/probability where supported. Energy may be a cheap prefilter, but must not be the sole reason to cancel output. Do not relabel RMS/peak heuristics as semantic VAD.
- Apply or request echo cancellation/noise suppression at the appropriate supported capture or provider boundary. Inspect existing settings first; avoid competing processing chains. Never assume a browser constraint was honored or that all transports provide identical audio processing.
- Support bounded onset confirmation, hysteresis, minimum duration, pre-roll and trailing silence. Preserve quiet speech and short intentional interruptions such as “stop” and “Alex”; do not wait for a complete transcript before stopping on confidently detected speech.
- Distinguish likely human speech from interruption intent. Presentation and conversational profiles may treat short backchannels differently. Document policy for overlapping participants and unrelated background speech; speech classification alone cannot prove someone addressed the agent.

### Cancellation and recovery

- Model candidate onset, confirmed speech, interruption, turn completion, and recovery explicitly. A noise candidate alone must not advance slides, discard narration, or commit a user turn.
- On confirmed interruption, cancel model generation and queued playback once; fence late audio/events with generation IDs. Reconcile provider conversation history with audio actually played, using supported truncation/cancellation behavior.
- Keep narration recovery anchored to the existing presentation checkpoint; generic conversation recovery must not re-execute tools or repeat completed business actions.
- If a candidate or interrupted segment is positively classified as non-speech, recover automatically only while the original session/generation still owns the floor and the user has not paused, taken over, disconnected, or ended the conversation. Empty/failed transcription alone is not proof of noise; real but unintelligible speech should prompt clarification or explicit resume rather than being ignored.
- Manual Stop, mute, host takeover, consent revocation, and session termination remain immediate and independent of speech confidence. Never auto-resume across these actions.
- Detector/model/provider failures must expose a clear degraded mode and retain manual interruption. Do not silently fall back to volume-only automatic cancellation or claim speech-aware operation when the classifier is unavailable.

### Integrations, configuration and observability

- Integrate every discovered existing voice path with the shared policy. Keep transport adapters thin and preserve each path's response ownership, consent semantics and tool authorization.
- Configure speech-onset policy independently from end-of-turn behavior. Where supported, use semantic turn completion to avoid cutting off hesitant users; do not present it as a noise classifier. Prevent provider auto-interruption from bypassing the shared decision when the application owns cancellation.
- Provide validated, documented deployment defaults and explicit conversation profiles, not per-agent copies or hard-coded Alex branches. Include enablement, safe rollout/rollback and dependency readiness checks. Unsupported transports must be explicitly reported, not silently left on old behavior.
- Preserve existing UI controls; show actionable degraded/manual-mode feedback where needed. Avoid exposing tuning knobs to ordinary users unless necessary.
- Emit bounded structured diagnostics: detection confidence, decision/reason, policy/model version, candidate/confirmed events, rejected candidates, cancellation/acknowledgement latency, recovery and failure state. Do not label a rejected candidate a measured false positive without ground truth. Do not log raw audio, transcripts, credentials or sensitive provider payloads by default.
- Audio processing and retention remain separate consent decisions. Do not broaden forwarding, recording or retention; release temporary audio buffers on session end or consent revocation.
- Prefer ephemeral detection state. No schema changes are presumed necessary. If persistent configuration/state is genuinely needed, use migrations and the database verification rules in `/docs/architecture-rules.md`; never introduce direct database repair scripts as the feature.

## 5. Constraints and preservation rules

Preserve tenant isolation, participant authorization, tool policies, approval/outbox boundaries, grounding, presentation state, lease ownership and cost controls. This feature changes conversational interruption behavior, not authority to send messages, change sales records or execute external actions. No new orchestration stack, raw-audio archival, or blanket permission grants. Keep working text/manual fallbacks. Do not enable paid services or contact real meeting participants without explicit authorization.

## 6. Acceptance criteria

1. Given an agent is speaking, fan noise, keyboard clicks, bumps, breathing and non-speech bursts in the committed test corpus do not trigger confirmed interruption or slide advancement.
2. Given real human speech, including quiet speech and short English/Swedish interruptions, the shared policy interrupts promptly and preserves the start of the utterance. For controlled replay, target p95 onset-to-cancel-command latency at or below 300 ms; measure transport playback-stop acknowledgement separately. Document any failure against this target rather than silently relaxing it.
3. Given agent playback leakage, echo alone does not create a user question or self-interruption in the echo test corpus.
4. Given concurrent or duplicated local/provider events, output is cancelled at most once for the same response generation; late audio cannot restart playback.
5. Given positively rejected non-speech and unchanged ownership, recovery continues from the safe checkpoint without duplicate tools or skipped presentation content. Given manual pause, takeover, ended session or revoked consent, automatic resume never occurs.
6. Given missing/failed transcription after confirmed speech, the agent does not presume noise and speak over the user.
7. Given two companies/sessions/participants, their detector state, audio and interruption decisions cannot cross boundaries.
8. Given an unavailable classifier, the UI/status clearly reports degraded/manual behavior; automatic volume-only interruption is not silently restored.
9. Every existing voice path has a verified adapter integration and declared capability/profile. A second non-Sales conversation uses the same shared implementation without importing Sales infrastructure.

## 7. Verification

- Add deterministic state-machine and streaming tests for onset, hysteresis, silence, packet loss, duplicate/out-of-order frames, track replacement, overlapping speakers, cancellation races and generation fencing.
- Test the real detector against a small legally usable, versioned speech/noise/echo fixture corpus; mocks alone cannot verify noise discrimination. Include English/Swedish speech, soft voices, short interruptions and changing noise floors. Do not use private customer recordings.
- Add integration tests for browser-room cancellation/recovery, shared realtime configuration, guided response ownership, and Teams/adapters that exist. Include negative authorization, cross-company access, consent revocation and detector/provider failure cases.
- Run focused tests first and build all affected hosts. Verify migrations only if schema changes occur. Follow the polish-uat-loop skill for hands-on voice-flow validation and record evidence using its required ledger.
- Replay Alex's original noise-interruption scenario and at least one non-Sales voice flow. Capture timing and decisions without retaining private audio. Live device/provider checks require an authorized test environment; clearly distinguish deterministic replay, integration tests and real microphone results. Do not mark an unavailable live verification as passed.
- Deliver an integration matrix and before/after measurements for false triggers on the labelled corpus, missed speech and interruption latency, plus operational dependency and rollback instructions.

## 8. Definition of done

The shared implementation and real detector run in the supported deployment environment; all existing voice entry points use the shared decision policy; focused regressions and affected builds pass. No placeholders, production mocks, hidden energy-only fallback, in-scope TODOs, or unexplained intermediate states remain. Document the chosen detector, supported profiles, known limitations and exact verification evidence. Continue through implementation and verification rather than stopping after an architecture proposal. If live verification is blocked, report that specific boundary and do not claim the full feature verified.
