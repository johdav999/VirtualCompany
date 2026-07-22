# Shared Agent AI

The shared agent AI platform is owned by Agent Management and AI Orchestration. Finance, Sales, and Support supply authoritative facts and policies, but do not call the model provider directly for shared capabilities.

## Runtime flow

1. `IAgentCapabilityCatalog` resolves effective availability from the persisted agent profile, trusted tool registry, data scopes, autonomy, and required configuration.
2. A capability service retrieves company-scoped evidence through its owning read boundary.
3. `IAgentReasoningGateway` sends a bounded request and validates version, claims, source IDs, confidence, and requested actions.
4. `AgentOrchestrationRun` stores the safe lifecycle and validated result. Hidden prompts, credentials, and raw provider payloads are not stored.
5. Any mutation continues through tasks, workflows, approvals, tools, and integration outbox processing. Model output never performs an external side effect directly.

Provider settings use the `SharedAgentAi` configuration section. The API key is read from `SharedAgentAi:ApiKey` or `OPENAI_API_KEY`; it must not be committed to configuration files.

## Capabilities

- Grounded questions use indexed documents, scoped records, bounded memory, and claim-level source IDs.
- Role briefings interpret the existing durable briefing aggregate while deterministic facts remain available if AI fails.
- Work prioritization uses authoritative task status, priority, and due dates. AI only explains the deterministic ranking.
- Plans are drafts until an authorized user commits them as durable tasks. Commit never executes external actions.
- Exception interpretation classifies and explains existing exception records without retrying or mutating them.
- Handoffs create typed, task-backed work with legal lifecycle transitions and bounded evidence references.
- Memory proposals require evidence and human review before an active `MemoryItem` is created.
- Quality events are idempotent and report component measures and sample size; they never raise autonomy automatically.

## Role decision services

Role-specific interpretation stays behind Application contracts and Infrastructure services. Controllers and Blazor pages only transport requests and present structured results.

- Finance decisions cover cash scenarios, reserve-aware payment-run recommendations, collections plans, accounting-treatment candidates, and fiscal-period close readiness. Deterministic Finance read models and policies calculate amounts, eligibility, account exclusions, and close blockers. Only reviewed payment recommendations can be converted into existing payment proposals; analysis cannot post entries or close periods.
- Sales decisions cover lead/deal intelligence, permission-aware next actions, deal risk and review-only mutual action plans, currency-separated forecast scenarios, campaign experiments, and source-backed proposal checks. Analysis cannot send outreach, change pipeline state, launch campaigns, or approve pricing and terms.
- Support decisions cover queue priority, answerability, case risk, privacy-safe recurring-issue clusters, and knowledge coverage. Existing support SLA, reply-safety, draft approval, delivery, refund, and escalation services remain authoritative. Analysis cannot send replies, disclose untrusted content, or resolve a case.

All role decision reads use explicit `CompanyId` predicates, including hosted/background execution where request query filters are unavailable. Structured results carry stable source identifiers, missing evidence, confidence/review state, and deterministic policy outcomes. Detail-page analysis is user initiated so navigation does not silently create model runs.

## Operating cadence

`RoleAgentCadenceBackgroundService` creates idempotent daily and weekly role-analysis windows for active Finance, Sales, and Support agents, plus a monthly Finance window. It reuses `AgentOrchestrationRun` status and prompt-version windows to prevent duplicate completed runs and cap failed attempts. Existing Finance and Support operational workers remain responsible for authoritative refresh, SLA, delivery, and reconciliation work; the cadence service only prepares manager advice.

## Registering a capability

Add a stable ID and code-owned manifest in `AgentCapabilityCatalog`. Declare action type, required trusted tools, data scopes, configuration signals, minimum autonomy, and approval behavior. Implement an Application contract and Infrastructure service that uses `IAgentReasoningGateway` when model reasoning is needed. Add the authorized API and typed Web client, plus tenant-isolation, policy, failure, and presentation tests. Only mark `IsImplemented` true when the complete path exists.

## Database portability

Migration `AddSharedAgentAiCapabilities` uses SQL Server types supported by both local SQL Server and the Docker SQL Server image, including explicit numeric optimistic-concurrency versions that also work in integration tests. Both restore scripts continue to restore the same backup and apply EF migrations during API startup; no local-only database behavior was introduced.
