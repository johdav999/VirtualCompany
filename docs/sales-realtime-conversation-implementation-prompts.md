# Hybrid Realtime conversations for sales meetings — implementation prompt pack

Created 2026-09-27. Execute prompts 1–6 in order. These are implementation prompts, not a request to execute them while creating this document.

## Shared instructions — mandatory for every prompt

Follow `/production-implementation.md`, `/docs/architecture-rules.md`, repository and applicable scoped `AGENTS.md`. For UI work follow `/docs/design.md`; `/ui-instructions.md` is a companion, not an override. Use the polish/UAT skill for user-flow implementation and verification. Read these sources before editing. Preserve existing uncommitted changes.

Implement real production behavior and focused tests in each prompt. Do not stop at interfaces, mock production adapters, an inventory, or a plan. When executing the whole pack, continue through the sequence unless genuinely blocked or the user requests a pause. Record verified outcomes and live-verification limitations separately. Do not reset meetings, consent, usage, or database records to make tests pass.

### Outcome and scope

Alex can pause narration for a grounded question, give a brief answer, generate a natural follow-up if useful, listen to the reply, and either continue the conversation or resume from the saved presentation checkpoint. Dialogue wording and interpretation are model-generated, not a scripted question/response tree. Backend state transitions, permissions, tool schemas, and failure codes remain deterministic.

Deliver the browser sales room end to end first. Shared provider/orchestration additions must be reusable by other agents, without agent-name conditionals or a second AI stack. Do not silently enable this behavior for Teams, guided work, or other voice products; preserve their existing contracts and test compatibility.

Mode policy:

- Autonomous: eligible grounded answers, including partial answers with limitations, can be released without per-answer host approval. Natural follow-ups and explicitly requested continuation can run automatically while the session remains authorized.
- Assisted/manual: substantive answers still require the existing exact-version host approval and speech action. Model intent never bypasses it. Automatic conversational speech and model-initiated presentation resume are disabled in these modes; retain the existing host controls and mode-specific presentation behavior.
- Changing mode takes effect on in-flight and queued work, not only the next question. Downgrading autonomy must invalidate unreleased automatic speech and pending automatic actions.
- No response, background noise, an ambiguous answer, or a backchannel alone is permission to resume. Human Stop, takeover, consent withdrawal, limits, and room expiry override conversation intent.

### Current implementation map — inspect again before modifying

- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesRoomAgentCoordinator.cs`: browser-room agent and playback workers, confirmed transcript routing, grounded question creation, automatic answer release, worker ownership, and autonomous deck continuation. Question routing currently uses question/addressing heuristics; contextual acknowledgements must not be lost at this gate.
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesRoomAgentService.cs`: host commands, typed questions, pending-turn authorization, speech queuing, and explicit approval.
- `src/VirtualCompany.Domain/Entities/SalesRoomFloor.cs`, `SalesBrowserRoom.cs`, `SalesMeetingSession.cs`, `SalesMeetingQuestion.cs`: floor generations, modes, room lifecycle, presentation position, and versioned answer release.
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesMeetingQuestionAnsweringService.cs` and `SalesMeetingAnswerGrounding.cs`: company/agent-scoped retrieval, factual claims with source IDs, partial answers with limitations, concise answer composition. Current sales target is 2–3 sentences / roughly 40–60 words with deterministic whole-claim bounds.
- `src/VirtualCompany.Application/Agents/RealtimeAgentSessionContracts.cs` and `src/VirtualCompany.Infrastructure.Operations/Companies/OpenAiRealtimeAgentSessionGateway.cs`: shared WebRTC/PCM contracts, normalized events, provider configuration. PCM manual-commit and other session profiles differ; do not change their defaults globally.
- `src/VirtualCompany.Application/Agents/ApprovedSpeechContracts.cs` and `src/VirtualCompany.Infrastructure.Operations/Companies/ApprovedSpeechGateway.cs`: approved text-to-audio validation. Preserve this path for factual answers and approved narration.
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesRoomVoiceActivitySegmenter.cs`, shared speech-interruption contracts, room media adapters: noise handling, bounded capture, interruption and playback ownership. Current browser flow confirms a transcript before interruption; do not regress to volume-only cancellation.
- `src/VirtualCompany.Web/Components/Sales/SalesHumanRoom.razor`: meeting host controls, private evidence, mode, consent, and spoken-answer UI.
- Regression owners: `tests/VirtualCompany.Api.Tests/SalesRoomPlaybackWorkerTests.cs`, `SalesRoomFloorTests.cs`, `SalesMeetingCaptureServiceTests.cs`, `OpenAiRealtimeAgentSessionGatewayTests.cs`, existing room lifecycle/lease/consent tests; `tests/VirtualCompany.Web.Tests/SalesHumanRoomTests.cs`.
- `docs/verification/speech-aware-interruption/uat.md` contains prior acceptance evidence. Older implementation prompts describe older code and must not override the current checkout.

### Provider references and important distinction

Use the official OpenAI Docs skill and recheck the current API schema before implementation. Reference: [Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations), particularly stateful sessions, function calling, and interruption/truncation. That documentation was consulted when creating this pack; model availability and schemas must be revalidated at execution time. Use the existing configured compatible model unless an explicit migration is required and documented.

Realtime supports conversational audio and function calls, but it does not itself authorize document access or presentation actions. Semantic end-of-turn detection is not proof of noise rejection or participant intent. Direct audio already played cannot be retrospectively withheld by checking its final transcript.

This pack therefore requires a hybrid release boundary: factual content stays on the grounded, validated speech path; generated conversational bridges have a separate bounded release policy. Do not feed unrestricted model audio straight to the room and claim that prompt instructions alone enforce factual grounding. If a generated bridge cannot be validated safely before playback, withhold it and remain listening; do not fall back to a hardcoded dialogue script. Document the latency cost of any buffering. Never weaken the approved-answer equality check just to permit generated follow-ups.

## Prompt 1 — Implement the conversation state and authorization policy

### 1. Title and outcome

Implement a reusable, versioned conversation controller with a sales-room profile so a question, answer, follow-up, reply, and presentation continuation have one authoritative owner.

### 2. Current context

Use the shared instructions and implementation map above. Room floor, presentation, answer visibility, and worker generations already exist. Extend their contracts rather than creating a competing floor controller.

### 3. Dependencies

None beyond the current repository. No live provider credential is needed for deterministic tests.

### 4. Implementation requirements

- Define explicit transitions covering presenting, interpreting a confirmed turn, retrieving an answer, awaiting host approval, speaking an answer, speaking a conversational bridge, waiting for a reply, and paused/stopped. Map these to existing lifecycle state rather than duplicating it blindly.
- Bind conversation turns and pending actions to company, agent, room/session, authorized participant identity/generation, room turn/response generation, mode/policy version, and presentation checkpoint.
- Separate model proposals from backend authorization. Implement one policy used by spoken input, typed input, tool dispatch, and playback release.
- Model pending conversational context: what was actually said/heard, whether a reply is awaited, what action is being proposed, and which response/checkpoint it concerns. Make timeout/uncertainty a safe listening or paused outcome, not implied consent.
- Wire this policy into existing worker/service entry points behind a default-off rollout flag. Existing disabled behavior remains unchanged; new policy must execute in tests, not exist as unused scaffolding.
- Keep transient audio ephemeral. If resumable business state or deduplication requires persistence, identify relational fields, add EF migrations, and verify SQL Server upgrade compatibility per the Database and EF Core rules. Do not assume in-memory state survives worker replacement.

### 5. Constraints and preservation rules

Apply the shared instructions and mode matrix. Keep usage history, limits, consent semantics, and approved narration. No new direct database maintenance workflow or automatic microphone activation.

### 6. Acceptance criteria

- Given a pending continuation proposal, when the host takes over or changes mode, then the proposal cannot resume narration.
- Given a partial grounded answer in autonomous mode, then policy permits qualified release; in assisted/manual it requires host approval.
- Given silence, expired reply context, unknown mode, or stale generation, then no continuation is authorized.

### 7. Verification

Add state-transition, mode-change, unauthorized participant, cross-company, stale-version, duplicate-event, and restart/recovery tests. Run the narrow backend suites and build affected projects. Verify migrations if introduced.

### 8. Definition of done

The policy is integrated, tested, documented, and executable under the feature flag; no alternate unguarded path or deferred in-scope TODO remains. Live conversational output is delivered by later prompts, not claimed here.

## Prompt 2 — Implement the shared Realtime conversational session adapter

### 1. Title and outcome

Deliver an actual persistent Realtime conversational connection that can interpret successive turns, propose tools, and produce bounded conversational audio through shared Application contracts.

### 2. Current context

Extend `RealtimeAgentSessionContracts.cs` and the existing Operations-owned gateway. Reuse the established room media transport; do not bypass the meeting audio publisher with a second browser audio connection. Inspect existing tool invocation normalization and PCM response ownership before adding methods.

### 3. Dependencies

Prompt 1. Current official provider documentation. Existing secure provider configuration for a bounded live smoke test; absent credentials block only that test, not real adapter implementation and deterministic verification.

### 4. Implementation requirements

- Implement supported operations for bounded conversation context, response requests, completed function-call arguments/results, correlated audio events, cancellation, and truncation based on audio actually played. Do not invent provider fields.
- Add an explicit conversational profile; preserve legacy transcription/manual-commit/guided profiles. Decide and document whether the application or provider commits turns and creates responses; never let both independently answer the same input.
- Keep the current speech-confirmation gate authoritative during presentation. Ensure short contextual replies reach the conversation controller while it awaits a reply; do not discard them merely because they lack a question mark, agent name, or minimum multiword length.
- Configure noise processing and end-of-turn handling deliberately. Handle echo, fan noise, overlapping participants, short replies, and silence without claiming semantic VAD guarantees intent detection.
- Keep conversation history bounded and aligned with played approved answers/narration. Store only permitted retained context; respect separate processing/retention consent. Old provider state must not leak between companies or meetings.
- Normalize completed tool calls with stable call IDs; reject malformed/oversized arguments, unknown tools, and uncorrelated responses. Never execute streamed argument fragments.
- Add backpressure, bounded audio buffers, cancellation, transport failure cleanup, and safe session rollover. Reconnect must not reset room usage caps, re-execute tools, or replay old speech.
- Count conversational input/output in the existing billing and audio budget system. Renew valid leases during provider waits without overriding host stops.

### 5. Constraints and preservation rules

Follow shared instructions. Provider SDK/schema ownership stays in Operations, sales ownership in Sales, and contracts in Application. Do not expose long-lived credentials or globally enable automatic provider interruption.

### 6. Acceptance criteria

- Two consecutive user turns retain permitted context and produce correctly correlated outputs.
- A duplicate tool event or late audio after cancellation cannot cause a duplicate action/playback.
- A reconnect after host Stop remains stopped; a provider outage yields actionable degradation and manual controls.

### 7. Verification

Extend provider-adapter protocol tests for audio, tool results, ordering, malformed JSON, cancellation/truncation, profile compatibility, and rollover. Test lease and quota behavior. If authorized credentials are available, run a small synthetic-audio smoke test with declared cost bounds; never start the user's microphone automatically.

### 8. Definition of done

Real provider transport and normalized contracts work behind the flag, with compatibility tests passing and any unperformed live verification explicitly recorded.

## Prompt 3 — Integrate grounded answers and generated conversational follow-ups

### 1. Title and outcome

After a short factual answer, Alex can generate an appropriate follow-up, listen, and understand a contextual reply without scripted dialogue.

### 2. Current context

Use the existing question-answering service, claim composer, approved speech gateway, and private evidence panel. Current heuristic question routing is insufficient for acknowledgements and continuation requests. Current exact-text speech validation must not reject a separately authorized conversational bridge or accidentally allow it to modify an approved answer.

### 3. Dependencies

Prompts 1–2. Existing company/agent-scoped knowledge retrieval and approved narration remain the source of factual evidence.

### 4. Implementation requirements

- Build bounded model context from the actual latest user turn, released answer and limitations, played presentation position, current mode, and available actions. Treat user/document/tool-result content as data, not authority to override policy.
- Use model reasoning for semantic intent and wording: follow-up question, clarification, continuation request, stop/wait, or unrelated speech. Do not implement production phrase tables for yes/no/continue or a fixed follow-up question. Deterministic safety controls remain allowed and required.
- Route factual questions to the existing grounded answer service. Preserve source permissions, citations, concise answer limits, missing-detail disclosure, and mode-dependent release. Provider/retrieval failures must not masquerade as “no sources.”
- Implement separate output lanes: immutable grounded answer speech and model-generated conversational bridge. Bridges must not inject product facts, pricing, commitments, or claims that an action succeeded without an authoritative tool result.
- Implement the pre-playback release boundary described in the shared instructions, including content-policy validation and version/generation fences. If additional semantic validation is needed, use shared orchestration, not a Sales-owned provider call. Prompt-only restrictions or a model's self-declared `safe` flag are insufficient enforcement.
- Preserve natural generated wording; normally at most one short follow-up question, and do not ask after every answer mechanically. Keep listening after bridge playback. Feed only what was actually played back into conversation context.
- Do not begin a second answer or duplicate narration while a prior output owns playback. A new substantive question replaces a pending continuation proposal rather than authorizing it.
- No-evidence answers can disclose the limitation through an explicit safe policy, but cannot invent facts. Keep a distinction between grounded content, a limitation-only conversational response, and provider failure.

### 5. Constraints and preservation rules

Apply shared mode policy. Assisted/manual factual answers remain private until the host approves that exact version. Retain the existing verified speech content checks. Never include raw credentials, hidden prompts, or inaccessible source text in conversational context or diagnostics.

### 6. Acceptance criteria

- Given an onboarding question with partial evidence, autonomous mode speaks a concise supported answer with its caveat, may generate a relevant follow-up, and waits.
- Given a new question instead of a continuation reply, the system retrieves and answers it without resuming slides.
- Given an attempted factual claim in a bridge, it is withheld or routed through grounding before playback.
- Given assisted/manual mode, no unapproved model-generated answer or bridge is played.

### 7. Verification

Test multi-turn context, one-word replies, ambiguous replies, follow-up suppression, source injection, evidence denial, output-lane separation, double speech, stale approved text, cross-company access, and mode downgrade during synthesis. Use deterministic model fixtures for policy tests and a separate bounded live evaluation for language quality; do not assert one exact generated follow-up string.

### 8. Definition of done

A complete question → grounded answer → generated follow-up → listening flow works through the real room worker behind the flag, without scripted dialogue or weakening factual-release checks.

## Prompt 4 — Implement authorized conversational presentation tools

### 1. Title and outcome

Alex can resume the presentation from the correct checkpoint when the participant clearly requests it, or stay in conversation for another question.

### 2. Current context

Use existing room floor, presentation runtime, resume commands, approved narration assets, audience readiness, and playback acknowledgements. Historic interrupted-state and inconsistent talking-point bugs must remain covered.

### 3. Dependencies

Prompts 1–3. A valid deck, approved narration, and current checkpoint are required for successful continuation, not fabricated if absent.

### 4. Implementation requirements

- Expose narrowly scoped tools for grounded question handling, requesting continuation, and requesting pause/wait using existing command services. Names are implementation choices; never expose arbitrary database, slide-index, or code-execution tools.
- Bind tool authority to trusted session/participant context, not model-supplied company, user, room, or approval identifiers. Treat all model tool arguments as untrusted proposals.
- A continuation proposal must reference the current completed conversational turn and authoritative checkpoint. Validate mode, participant control rights, clear current-turn intent, no newer question, room/lease/consent/budget state, floor ownership, presentation version, and narration/audience readiness immediately before execution.
- An ambiguous acknowledgement after multiple questions does not suffice. Generate a single clear clarification or remain waiting. Do not infer consent from elapsed time.
- Persist deduplication for successful control actions using existing durable command/outbox infrastructure where appropriate. Retries, reconnects, concurrent tool calls, and worker replacement must not resume twice or skip slides. Document any schema effect and apply EF migration rules if needed.
- Finish bridge playback before narration takes the floor. Save and resume the authoritative slide/talking point/offset; do not derive progress from a model's textual recollection.
- Return actual execution success/failure to the model. Do not announce continuation as completed before the backend accepts it. Failures leave a recoverable waiting/paused state with existing host controls.
- Recheck mode when executing and playing. Assisted/manual requests remain host-controlled, not automatically executed by speech intent.

### 5. Constraints and preservation rules

Follow Workflow and Approval and External Side Effects and Outbox sections of `/docs/architecture-rules.md`. Side effects are room playback and presentation movement, not external calendar edits. Preserve approved assets, manual navigation, Stop, human calling, and usage history.

### 6. Acceptance criteria

- After a generated continuation offer, a clear contextual affirmative in autonomous mode resumes the saved position exactly once.
- A reply containing a new question does not also resume narration.
- An older affirmative cannot resume after host takeover, a newer turn, a mode change, room expiry, or worker replacement.
- Missing narration/readiness produces an actionable pause, not skipped content or unapproved speech.

### 7. Verification

Add relational integration tests for tool authorization, foreign company/session, duplicate/replayed call IDs, stale checkpoint, overlapping speakers, stop/resume races, mode changes, and correct continuation across slide boundaries. Re-run playback, floor, presentation runtime, and lease regression suites.

### 8. Definition of done

Natural conversational intent drives existing presentation commands safely and idempotently; no hardcoded response matching, stale-state bypass, or alternate slide controller remains.

## Prompt 5 — Deliver understandable meeting controls and recovery

### 1. Title and outcome

Make the conversation state and mode behavior clear to the organizer without requiring routine clicks in autonomous mode.

### 2. Current context

Update `SalesHumanRoom.razor`, typed clients/contracts where needed, and existing agent status projections. Preserve the private evidence panel and host controls.

### 3. Dependencies

Prompts 1–4. Backend policy and status are authoritative; UI must not duplicate permission decisions.

### 4. Implementation requirements

- Show plain-language states such as listening for your reply, checking sources, answering, awaiting approval, resuming presentation, and paused with a reason.
- Explain autonomous conversational behavior and approval requirements in other modes. Keep manual approval available where required, and manual recovery available when automation is withheld.
- Preserve microphone device selection, mute, enable sound, consent/retention controls, Stop, takeover, and presentation controls. Do not auto-unmute or automatically grant consent.
- Clear stale errors and pending actions after successful recovery, while preserving accurate failure reasons. Expose provider outage, unsupported conversational configuration, quota, and expired room distinctly.
- Avoid significant layout redesign. `/docs/design.md`'s mandatory reference-image workflow applies if a new screen or major component redesign becomes necessary; existing-component text/state additions should preserve current structure. Use the polish/UAT skill and its evidence ledger.
- Provide operator rollout configuration and a default-off flag; enabled-but-unavailable must be visible, not silently described as Realtime conversation. Disabling the flag must safely cancel new conversational work and preserve the existing approved-answer/manual workflow.

### 5. Constraints and preservation rules

Apply the shared instructions. Public participants must not see private evidence, host-only controls, model/tool internals, or credentials. No optimistic UI claim of successful resume before backend confirmation.

### 6. Acceptance criteria

- Autonomous question/answer/follow-up needs no approval click, and the organizer sees when Alex is waiting.
- Assisted/manual clearly requests host approval and cannot play a private answer through UI or direct API calls.
- Failure and rollback preserve human calling and manual slide control.

### 7. Verification

Add component and API-client tests for each new state/mode, privacy, disabled actions, errors, and recovery. Build API and Web. Run a safe browser check of the original flow; if live audio cannot be exercised, explicitly record that limitation and use the strongest automated substitute.

### 8. Definition of done

The real meeting surface accurately reflects backend conversation state, mode and recovery. Rollout and rollback are documented and tested; no mock production UI or hidden required step remains.

## Prompt 6 — Verify the complete conversation and operational readiness

### 1. Title and outcome

Close the end-to-end loop with evidence that natural conversation, factual grounding, and presentation continuation coexist safely under real timing and failures.

### 2. Current context

Use the implementation from prompts 1–5 and extend the existing UAT evidence. This prompt includes fixing discovered in-scope defects, not merely writing a test plan.

### 3. Dependencies

Prompts 1–5. Deterministic tests need no external service. Live verification requires an authorized active test meeting, approved sources/narration, configured provider credentials, participant consent and an agreed bounded spend. Do not alter the user's current meeting schedule or exhausted quotas without explicit authorization.

### 4. Implementation requirements

- Execute: start autonomous deck → interrupt with onboarding question → hear concise grounded answer → hear a generated follow-up → give a clear continuation reply → resume the exact checkpoint → interrupt again with a different question.
- Test paraphrases and contextual replies, including an affirmative that also introduces a question, “not yet,” uncertain acknowledgement, a request to repeat, and silence. These are evaluation inputs, not a production phrase dictionary.
- Repeat in assisted/manual modes; verify answer approval and host presentation control cannot be bypassed. Test mode changes while retrieval, audio generation, bridge playback, or resume is pending.
- Exercise fan noise, speaker echo, short intentional speech, two participants, unrelated background speech, mute, consent withdrawal, host takeover, network failure, lease expiry, provider session rollover, duplicate tool delivery, room expiry, and spend/audio caps.
- Verify generated bridges cannot leak unsupported facts and source/tool prompt injection cannot elevate authority. Confirm actual tool results, not model claims, drive state.
- Capture bounded telemetry: confirmed input to first audible answer, answer completion to listening, continuation request to accepted resume, cancellation to stopped playback, tool duplicate counts, audio/token usage, and failure reasons. Set numeric latency/cost acceptance budgets in the evidence before the live run and report measured values; do not invent results or claim noise accuracy without labeled evidence.
- Add a concise operational runbook for configuration, source permissions, model/profile compatibility, safe restart/reconnect, rollout/rollback and diagnosing stale generations versus provider/grounding failures. Record prompt/profile versions, no sensitive content or credentials.
- Fix in-scope failures and rerun affected checks. Do not repeatedly run unchanged broad suites.

### 5. Constraints and preservation rules

Apply all shared instructions. Do not remove approvals in nonautonomous modes, disable source checks, reset history, raise limits, or bypass release validation to pass acceptance. Live output is observable side effect; use only the authorized test audience.

### 6. Acceptance criteria

- The full scenario completes without hardcoded dialogue, duplicate speech, lost questions, incorrect slide position, or manual approval in autonomous mode.
- Uncertain continuation stays waiting; no stale/unauthorized action executes.
- Factual answers remain grounded and concise; unavailable evidence and provider errors are distinguished.
- Controls, privacy, budgets and failure recovery hold in the adversarial scenarios above.
- Rollback restores the existing working flow without schema/data loss or quota resets.

### 7. Verification

Run focused domain, provider, relational worker/service and Web tests first, then one appropriate broader build/regression pass. Include tenant isolation, approval, idempotency and migration checks where changed. Publish a UAT ledger separating deterministic passes, live passes, measured latency/cost, and blocked cases. Real microphone/playback acceptance cannot be inferred solely from mocks.

### 8. Definition of done

All in-scope implementation and automated acceptance is complete, API/Web build, and operational documentation and evidence exist. Report live readiness honestly: if credentials, consent or a test audience are missing, name that exact blocker and do not claim live audio verified or enable the feature silently.
