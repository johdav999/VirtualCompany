# Sales room conversation — Prompt 4 verification

2026-09-27. Base revision `66e29aeb` plus existing Prompt 1–3 changes and Prompt 4 work. Product: Virtual Company browser sales meeting (AI workflow). Role: consenting organizer/authorized co-host. Environment: production command/playback code with deterministic SQLite persistence, approved synthetic narration and fake media/provider boundaries. No customer meeting or microphone was started. Flag: `SalesRoomAgent:HybridConversationEnabled`, default-off.

The OpenAI-docs workflow verified [Realtime function results](https://developers.openai.com/api/docs/guides/realtime-conversations#provide-the-results-of-a-function-call-to-the-model). The polish/UAT workflow separates relational regression evidence from live language and microphone acceptance.

| Flow | Expected / evidence | Result |
|---|---|---|
| Clear contextual continuation | Real answer and validated bridge complete playback. Existing resume command restores floor point 1 despite session point 0, preserves offset, queues approved narration, and existing conductor advances across the next slide. | Relational regression |
| Duplicate/concurrent calls | Two independent database connections race the same command. One durable operation, one speech item, one room-turn increment. A new scoped service replays without repeating; another call ID with stale checkpoint cannot advance again. | Relational regression |
| Foreign/stale authority | Foreign company/session/participant, worker generation, changed checkpoint, expired reply, takeover, stopped room, withdrawn consent and exhausted budget reject continuation. | Relational regression |
| New question/ambiguity/overlap | Tool must match current interpreted intent. Question/unknown intent cannot execute continuation; later question sequence invalidates the bridge even with identical timestamps. Overlap/pending turns deny control. | Relational + deterministic policy regression; live interpretation unverified |
| Mode changes | Manual/assisted cannot automatically continue. Downgrade, Stop or flag disable after queueing prevents narration playback. | Relational regression |
| Missing resources | Revoked/missing narration, unready audio and missing audience readiness queue nothing and persist an actionable pause; checkpoint remains available to host controls. | Relational regression |
| Provider boundaries | Completed registered tools only, bounded empty arguments, response/turn correlation, duplicate suppression, actual results, tools-disabled result continuation. Generated tool-result text is never published as audio. | Adapter/state tests and code review; live WebSocket ordering unverified |

## Executed checks

- API room/realtime/question/capture/presentation-runtime regression filter: 242 passed, 1 existing skip before the final provider event-ordering refinements.
- Final focused playback/floor/tool/conductor run: 121 passed before the final provider refinements.
- Shared `AgentConversationTests`: 20 passed.
- Final combined regression (including room/lease/floor/playback, tools, Realtime, question/capture, runtime and conductor): **247 passed, 1 existing skip, 0 failed**. Command: `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SalesRoom|FullyQualifiedName~SalesMeetingQuestion|FullyQualifiedName~SalesMeetingCapture|FullyQualifiedName~OpenAiRealtime|FullyQualifiedName~SalesPresentationRuntime|FullyQualifiedName~SalesBrowserPresentation|FullyQualifiedName~SalesMeetingPresentationConductorTests" -clp:ErrorsOnly -v:q --logger "trx;LogFileName=prompt4-verified.trx"`. Local results: `tests/VirtualCompany.Api.Tests/TestResults/prompt4-verified.trx` (ignored build artifact).
- `git diff --check`: no whitespace errors.
- `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -clp:ErrorsOnly -v:q`: passed, 0 warnings/errors in the final incremental build.

## Issue ledger / release limitations

| ID | Severity | Summary | Acceptance / status |
|---|---|---|---|
| UAT-04-01 | P2 | Real microphone wording, ambiguous acknowledgements, interruption latency and provider WebSocket timing have not been exercised in an authorized live room. | Before customer rollout, run a bounded consented question → grounded answer → generated continuation offer → affirmative → resume flow; repeat with a new question, ambiguity and takeover. Open live acceptance. |
| UAT-04-02 | P2 | Concurrency uses isolated file-backed SQLite; production SQL Server range-lock/deadlock behavior has not been exercised against an integration database. | Run the same simultaneous continuation/Stop scenarios in SQL Server staging. Serializable transaction, provider retry strategy, existing unique indexes and concurrency tokens are implemented; no migration is needed. Open provider-specific acceptance. |

No schema, external calendar write, permission expansion or quota reset. Pending proposals disappear on worker replacement. Successful commands persist through the existing operation/speech queue. Failure remains recoverable using the existing host controls. The earlier unrelated full-solution .NET 9/10 test-project assembly mismatch remains outside this prompt; the API project is the build target for these backend changes.
