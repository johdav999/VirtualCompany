# Agentdialog prompt 5 — grounded concise conversational answers

Date: 2026-09-28. Scope: prompt 5 only, on the existing uncommitted prompts 1–4.
Product profile: Virtual Company browser meeting AI workflow. Isolated SQLite fixtures (and
focused InMemory service tests); organizer, autonomous/manual/assisted, foreign-tenant and
revoked-authority cases. Evidence: production retrieval/answer service/worker, relational
citations and audit fingerprints, deterministic model review/TTS and media completion receipts.
No running host, customer database, calendar, source permission, consent or quota was modified.

## Flows and issue ledger

| ID | Severity | Flow and regression evidence | Acceptance / result |
|---|---|---|---|
| AD5-01 | P1 | Exact complete utterance plus same-room delivered references; `SalesMeetingDialogueEvidenceTests` | Actual question retained; queued/withheld/foreign participant or owner/withdrawn-retention context excluded. General conversational text is reference context, never a citable source. Automated substitute verified. |
| AD5-02 | P0 | Independent relevant/source-supported claim review; partial composition tests | Accepted whole claims and essential approval qualification remain; rejected generic overview and unsupported summaries cannot play. One bounded missing-detail statement. Automated substitute verified. |
| AD5-03 | P0 | Indexed onboarding policy → actual knowledge search/access policy → persisted partial evidence → approved speech → 100 ms media receipt; `SalesRoomGroundedDialogueTests` | Autonomous publishes exact released text. Manual/assisted remain private and silent until explicit approval/speech. Automated substitute verified. |
| AD5-04 | P0 | Same-ID source content changes, removed sources, changed answer/version and foreign company | Source/answer fingerprint recheck fails closed. Current retrieval uses the agent's original access boundary. No source grants added. Automated substitute verified. |
| AD5-05 | P1 | Mismatching speech, provider failure, source loss before/after generation → fresh valid question | Zero failed-turn frames; healthy owner/session stays available; old failed item cannot replay. Provider failure is not classified as insufficient evidence. Automated substitute verified. |
| AD5-06 | P1 | Completed answer without mandatory follow-up, >45-second reference interval, mode downgrade/consent and obsolete-turn tests | Fresh input remains available; actual completion owns the context. Optional follow-up has a bounded deadline and does not authorize resume. Existing controller/receipt regression coverage retained. Automated substitute verified. |
| AD5-07 | P0 | Capture expiry including independently reviewed claim runs; `SalesRoomCaptureTests` | No new retained validation text survives expiry. Automated substitute verified. |

## Reproduction packet

Entry point: the production question handler behind `ask_grounded_question` plus the existing
speech worker, exercised in a fresh isolated room rather than the user's accumulated test room.

1. Start the isolated owner, enable its already-defined semantic/autonomous profile, establish
   explicit test capture consent, and index a policy chunk using the real knowledge model.
2. Retain “How is onboarding done?” as one exact confirmed browser utterance.
3. Execute the real handler, retrieval/access filter, structured answer composition and independent
   claim-review boundary. The deterministic provider returns supported setup/administrator steps
   plus a missing rollout timeline.
4. Observe partial evidence, automatic stage release only for autonomous mode, exact answer audio
   matching and the real worker's completed receipt. The media seam measures 100 ms; this is not
   an assertion about a physical loudspeaker.
5. Repeat assisted/manual: approval alone remains silent; the explicit speech command can publish.
6. Repeat with a mismatching transcript, provider exception or revoked indexed chunk during
   synthesis: no audio frames publish; ask a new valid question and verify only its new answer plays.

Expected and observed: exact question/provenance retained; concise supported partial answer;
source IDs private; fresh dialogue/continuation eligibility preserved; failed answers never replay.

## Verification commands

Focused tests cover the new service and SQL-backed document-to-audio path. The broader affected
matrix covers playback, conversation lifetime/tools/recovery, semantic input, shared Realtime
adapter, output receipts, floor/mode/consent fences, capture expiry and knowledge access policy.
Web build verifies the updated shared contract composes with existing host controls.

Final results: **381 API tests and 35 Web tests passed** (416 total, zero failures/skips).
The API matrix was rerun after updating the retention assertion to include both answer and
independent claim-review runs, and includes the latest delivered conversational-context case.

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -v:q -clp:ErrorsOnly --filter "FullyQualifiedName~SalesRoomPlaybackWorkerTests|FullyQualifiedName~SalesMeetingCaptureServiceTests|FullyQualifiedName~SalesRoomCaptureTests|FullyQualifiedName~OpenAiRealtime|FullyQualifiedName~SalesRoomAudioOutputTests|FullyQualifiedName~SalesRoomFloorTests|FullyQualifiedName~RealtimeBufferedSpeechTests|FullyQualifiedName~SalesRoomConversation|FullyQualifiedName~SalesRoomSemantic|FullyQualifiedName~KnowledgeAccessPolicyEvaluatorTests"
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -v:q -clp:ErrorsOnly --filter "FullyQualifiedName~SalesHumanRoomTests"
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -v:q -clp:ErrorsOnly
git diff --check
```

Web build succeeded with zero errors; API test builds compiled the changed backend/shared
contract. `git diff --check` passed. No standalone full-solution or live-host test was claimed.

## Remaining pilot boundary

No live microphone, browser speaker, external tenant Graph or new real-model question-answering
probe was run. Semantic relevance is model judgment plus backend source/claim constraints, not a
proof that a model will understand every utterance. Use the existing pilot runbook with the RØDE
microphone and approved indexed OneDrive source to verify capture, relevance, concise wording,
follow-up usefulness, latency, physical audibility and same-generation resume. This remains
explicitly unverified; no stale room or quotas were reset to manufacture a successful pilot.

The Polish & UAT skill influenced the isolated end-to-end receipt test and this evidence ledger.
Official OpenAI documentation was checked for function-call/application-authority separation;
the configured models and provider transport were not changed in this prompt.
