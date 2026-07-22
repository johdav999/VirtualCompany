# Shared Agent AI Implementation Prompts

## Purpose And Execution Order

These prompts implement the shared AI capabilities described in `agents-ai.md` for Laura, Alex, Ben, and future company agents. Execute them in the numbered order because later prompts depend on contracts, evidence, and governance introduced earlier.

1. Shared capability catalog and effective availability
2. Validated shared reasoning and persisted orchestration runs
3. Grounded company question answering
4. Role-specific and joint AI-supported briefings
5. Deterministic-first shared work prioritization
6. Bounded planning and task decomposition
7. Evidence-backed exception interpretation
8. Typed cross-agent handoffs
9. Governed shared memory candidates
10. AI quality feedback, evaluation, and operational metrics

## Instructions That Apply To Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `agents-ai.md`, and `/docs/architecture-rules.md` before implementation. Use `/docs/architecture-overview.md` only as background.
- `architecture-inst.md` and `ui-instructions.md` were not present when this prompt pack was generated. If either exists when a prompt is executed, read and follow it. Do not fabricate missing instruction files and do not treat their current absence as permission to ignore `/docs/architecture-rules.md` or `/docs/design.md`.
- Implement production behavior, not scaffolding, placeholder responses, mock production data, or disconnected demonstrations.
- Preserve the modular-monolith boundaries and use the existing shared orchestration, knowledge, task, workflow, approval, outbox, audit, and background-execution subsystems. Do not create a second orchestration stack for a department or named agent.
- Existing repository implementation wins over older planning text. Inspect the current branch immediately before editing because this repository is under active development.
- Keep deterministic policies authoritative for eligibility, money, accounting, SLA deadlines, commercial commitments, permissions, approvals, and external side effects. AI may interpret and explain those results but may not replace them.
- Every tenant-owned read, write, event, task, workflow, metric, and AI execution must be company-scoped and covered by cross-tenant tests.
- Never expose hidden prompts, credentials, tokens, raw sensitive provider payloads, unrestricted support records, or out-of-scope company data.
- Any schema change requires an EF Core migration and model snapshot update. Verify both local SQL Server and Docker SQL Server migration/restore compatibility, including `restore-local-sql-db.ps1` and `restore-virtualcompany-db.ps1` where relevant.
- Any reliable external effect must use existing approval, outbox/background execution, stable idempotency, bounded retry, reconciliation, and operator-visible failure patterns.
- UI work must follow `/docs/design.md`, reuse the existing design system, and use the mandatory screenshot-first workflow for a new or substantially redesigned surface.
- Put tests in the narrowest appropriate test project and run focused tests plus affected API and Web builds. Do not weaken existing tests.

---

## Prompt 1: Publish A Shared Agent Capability Catalog

### 1. Title And Outcome

Implement a shared, authoritative agent capability catalog that explains what each company agent can currently read, recommend, or execute, and why a capability is available, approval-gated, unavailable, or misconfigured. This gives operators a truthful view of agent abilities and provides later orchestration prompts with one capability-resolution boundary.

### 2. Current Context

- `ICompanyToolRegistry`, `TrustedToolRegistration`, and `ToolDefinitionManifest` in `VirtualCompany.Application/Agents/InternalToolContracts.cs` describe trusted tools and schemas.
- `StaticCompanyToolRegistry` currently registers task, approval, knowledge, Finance, and Sales tools.
- `CompanyAgentToolExecutionService`, `PolicyGuardrailEngine`, persisted agent operating profiles, data scopes, tool permissions, autonomy, approval thresholds, and integration configuration already determine whether execution is allowed.
- `AgentsController` exposes agent profiles and executions, and the Blazor Agents workspace displays operating-profile and source-access configuration.
- There is no single read model that combines declared capability, tool registration, effective permissions, data scope, autonomy, approval requirement, integration readiness, and operator-facing unavailability reasons.

### 3. Dependencies

None. This is the first prompt.

### 4. Implementation Requirements

- Add an application-level `IAgentCapabilityCatalog` query boundary under the existing Agents/orchestration ownership. Do not overload `ICompanyToolRegistry` with tenant-specific authorization queries.
- Define typed capability manifests with stable IDs, version, category, supported action class (`read`, `recommend`, `execute`), required tools, required data scopes, required integration/configuration signals, minimum autonomy, approval behavior, and user-facing description.
- Seed shared capability manifests for grounded question answering, briefing, prioritization, planning, exception interpretation, cross-agent handoff, and memory proposal. Map existing tool definitions into the same effective read model without duplicating tool schemas.
- Resolve effective availability for a specific company and agent from the current agent profile, registered tools, data scopes, status, autonomy, required integrations, and policy configuration. Return stable reason codes and plain-English explanations.
- Distinguish `available`, `approval_required`, `configuration_required`, `permission_denied`, `integration_unavailable`, and `not_implemented`. Do not claim a capability is available merely because a prompt or UI label exists.
- Add authorized API queries under the existing company/agent route family for one agent and, if useful to the current Agents page, the company roster. Controllers must remain transport-only.
- Add typed Web client methods through the existing API-client registration and company transport patterns.
- Add a compact capability section to the existing Agents workspace showing action class, effective state, and reason. This is an extension of the existing workspace, not a separate marketing-style page.
- Audit changes to persisted capability configuration only if this prompt introduces any. Read-only catalog requests need technical observability but should not generate noisy business audit records.
- Document how a future capability is registered and how a tool or integration changes effective availability.

### 5. Constraints And Preservation Rules

- The catalog is descriptive and must not itself authorize execution. `PolicyGuardrailEngine`, tool permissions, approval policy, and authoritative feature policies remain enforcement boundaries.
- Do not infer availability from agent display names such as Laura, Alex, or Ben. Resolve by stable role/department configuration and explicit manifest requirements.
- Do not expose tool input/output schemas, hidden policy metadata, secrets, or internal denial diagnostics in the operator UI.
- Prefer code-owned manifests for platform capabilities. Do not add a database table merely to mirror static definitions unless current repository configuration patterns demonstrably require persisted overrides.
- Preserve all existing tool names, routes, profile payloads, and execution behavior.

### 6. Acceptance Criteria

- Given an active agent with `knowledge.search` and the `knowledge` read scope, when its catalog is queried, then grounded question answering is returned as available or approval-gated according to policy.
- Given an agent without a required data scope, when its catalog is queried, then the capability is unavailable with a stable permission reason and no data is leaked.
- Given a capability that requires an unconfigured provider integration, when queried, then it is reported as configuration required rather than available.
- Given two companies with different profiles and integrations, when the same agent role is queried, then each receives only its own effective capability state.
- Given a user without agent-management/read authorization, when the endpoint is called, then access is denied server-side.

### 7. Verification

- Unit-test manifest validation, requirement evaluation, reason precedence, and stable action classification.
- Add API authorization and cross-tenant integration tests.
- Add Web client contract tests and focused Blazor presentation tests for available, approval-required, unavailable, loading, and safe-error states.
- Run the affected API and Web test projects and build both hosts.
- Verify existing tool execution and agent profile tests remain unchanged and passing.

### 8. Definition Of Done

The capability catalog is production-ready, truthful, authorized, company-scoped, documented, and visible in the existing Agents workspace. There are no hardcoded named-agent exceptions, fake availability states, disconnected manifests, silent configuration failures, or in-scope TODOs.

---

## Prompt 2: Standardize Validated Shared AI Reasoning And Persisted Runs

### 1. Title And Outcome

Implement one validated AI reasoning boundary and a durable orchestration-run record for all shared agent capabilities. Every AI-supported interpretation must return a schema-validated result with confidence, evidence references, uncertainty, and safe failure state, enabling reliable downstream capabilities and auditability.

### 2. Current Context

- `ISingleAgentOrchestrationService`, `SingleAgentOrchestrationService`, `StructuredPromptBuilder`, `OrchestrationResult`, `OrchestrationCompositeFinalResult`, source references, tool references, and audit artifacts already define much of the shared orchestration flow.
- `SingleAgentOrchestrationResolver` resolves company, agent, task, conversation, intent, and policy context.
- Grounded context, memory snippets, tool schemas, policy instructions, communication-style checks, guardrails, and tool execution are already present.
- `OpenAiAgentBriefDraftService` and potentially other feature services contain provider-facing generation behavior that must not evolve into parallel orchestration stacks.
- Tool attempts, context retrieval, tasks, and audit events are persisted, but there is no clearly authoritative generic persisted record for the full lifecycle and validated structured output of every shared orchestration run.

### 3. Dependencies

- Prompt 1: shared capability IDs and action classification.
- A configured AI provider is required for live integration tests, but deterministic/fake provider contract tests must run without external credentials.

### 4. Implementation Requirements

- Add an application-owned `IAgentReasoningGateway` or equivalently narrow shared contract used by orchestration capabilities. It must accept a capability ID, resolved runtime context, bounded prompt/context payload, required output schema/version, and cancellation token.
- Implement provider access in Infrastructure behind the shared gateway. Feature modules must not call an LLM provider directly. Refactor existing direct generation paths to use the shared boundary where behavior is equivalent, preserving public contracts.
- Require strict structured output for shared capabilities: result version, summary, findings/claims, fact-versus-inference classification, confidence, uncertainty, missing evidence, source-reference IDs, recommended next actions, and requested tool/plan intents where allowed.
- Validate provider output against a versioned schema and semantic rules. Reject unknown source IDs, invalid confidence ranges, unsupported action types, malformed payloads, and tool requests outside the resolved capability manifest.
- Add deterministic fallback behavior for provider timeout, unavailable credentials, invalid output, content refusal, rate limit, and cancellation. Never convert a provider failure into a confident business answer.
- Add a company-owned `AgentOrchestrationRun` aggregate if the current persistence model cannot represent the full run lifecycle. Persist stable capability ID/version, company, agent, initiating actor, task/conversation links, status, model/provider identifiers safe for operators, prompt/schema version, confidence, rationale summary, source IDs, safe failure code/message, correlation ID, timestamps, and token/latency metadata where available. Do not persist secrets or unredacted hidden prompts.
- Model explicit statuses such as queued/running/completed/needs_review/blocked/failed/cancelled with legal transitions and concurrency protection.
- Write business audit evidence for material completions, denials, review requirements, and failures. Add structured technical logs and metrics without sensitive content.
- Add authorized read endpoints for run status/detail needed by later UI and workflows; do not expose raw system prompts or sensitive context payloads.
- Register services once in the existing shared infrastructure composition.

### 5. Constraints And Preservation Rules

- Keep `SingleAgentOrchestrationService` as the shared orchestration entry point unless repository inspection proves a narrower refactor is needed. Do not create department-specific orchestrators.
- AI output cannot directly mutate records or call providers. Validated action intents must continue through registered tools, guardrails, approvals, and durable execution.
- Maintain current task, audit, context retrieval, and tool-attempt relationships and correlation behavior.
- If a schema change is required, add an EF migration and verify SQL Server local/Docker compatibility and a clean pending-model-change check.
- Provider configuration must use the existing secret/configuration approach. Never log API keys, authorization headers, hidden prompts, or full sensitive provider payloads.

### 6. Acceptance Criteria

- Given valid grounded context and a schema-compliant provider response, when reasoning completes, then a completed run persists validated output, confidence, source IDs, model/schema versions, timestamps, and correlation ID.
- Given a response citing a source that was not supplied, when validation runs, then the result is rejected or marked for review and the unsupported citation is not shown as evidence.
- Given a timeout or invalid JSON/schema response, when the run ends, then it has an actionable safe failure state and no tool is executed.
- Given an execute intent outside the agent's capability or permission, when validated, then it is blocked before tool execution and audited.
- Given a cross-company run ID, when another company requests it, then the API returns no tenant data.

### 7. Verification

- Unit-test output schema validation, semantic source validation, state transitions, safe failure mapping, and redaction.
- Add orchestration integration tests using a deterministic fake reasoning provider for success, malformed output, unsupported citations, timeout, cancellation, and policy denial.
- Add authorization and tenant-isolation API tests.
- If persistence changes, test migration discovery, model snapshot, SQL Server migration application, and `dotnet ef migrations has-pending-model-changes`.
- Build Application, Infrastructure, API, and Web; run existing single-agent orchestration, prompt builder, guardrail, tool execution, direct chat, and audit tests.

### 8. Definition Of Done

All shared AI reasoning uses one production provider boundary, produces validated versioned output, persists a complete safe run lifecycle, and fails visibly without side effects. No production contract stubs, no direct feature-level provider bypass, no silent parse fallback, and no deferred in-scope TODOs remain.

---

## Prompt 3: Implement Grounded Company Question Answering For Every Agent

### 1. Title And Outcome

Implement a reusable grounded question-answering capability that lets any permitted agent answer company-specific questions from current records, indexed documents, policies, and bounded memory while clearly distinguishing confirmed facts, inferences, and missing information.

### 2. Current Context

- `ICompanyKnowledgeSearchService`, knowledge documents/chunks, access-scope evaluation, indexing, grounded-context retrieval, `GroundedContextPromptReadyMapper`, context retrieval persistence, and source-reference DTOs already exist.
- `StructuredPromptBuilder` already includes grounded context, memory, policy instructions, source IDs, and tool schemas.
- Direct chat already exercises scoped knowledge and memory retrieval.
- Support has stricter grounding and knowledge-gap behavior that must remain authoritative for outbound customer replies.
- The Agents workspace can upload and brief agents with company documents, but there is no shared, explicit question-answering contract with claim-level evidence and insufficient-grounding behavior for all agents.

### 3. Dependencies

- Prompt 1: capability availability.
- Prompt 2: validated reasoning gateway and persisted orchestration run.
- Documents must be processed and indexed; missing indexing is a handled prerequisite state, not a reason to fabricate an answer.

### 4. Implementation Requirements

- Add a shared application query/service contract for agent question answering. Inputs must include company, agent, question, optional conversation/task context, and requested domain; callers must not submit arbitrary source IDs to bypass access evaluation.
- Resolve agent profile, permissions, data scopes, relevant company records, indexed documents, policy sources, and bounded memory through existing context and retrieval abstractions.
- Build a versioned structured answer containing a concise answer, claim list, claim type (`confirmed_fact`, `inference`, `unknown`), confidence, source references, missing information, review requirement, and safe next actions.
- Require every confirmed factual claim to cite one or more source IDs actually included in the resolved context. Inferences must name supporting evidence and be visibly labeled.
- Define deterministic grounding thresholds and rules outside prompts. Missing, stale, inaccessible, contradictory, or low-confidence evidence must produce `needs_review` or `insufficient_grounding`, not an authoritative answer.
- Reuse existing Support knowledge-gap creation for Support-owned cases. For other agents, create a durable company task or a narrowly owned shared knowledge-gap record only if repository inspection shows tasks cannot support review, deduplication, and closure. Avoid duplicate gap systems.
- Add an authorized API endpoint under the agent route family and integrate the shared capability into direct chat without breaking existing conversation/task behavior.
- Return source links only after server-side company and access-scope validation. A source visible to one agent must not automatically be visible to another.
- Add operator-visible answer states and citations to existing chat/agent surfaces using current components and plain English.
- Audit answer completion, review requirement, and knowledge-gap creation with correlation to the orchestration run and context retrieval.

### 5. Constraints And Preservation Rules

- Only processed and indexed accessible documents may ground answers.
- Do not bypass `ICompanyKnowledgeSearchService`, grounded-context services, agent data scopes, or Support grounding/safety abstractions.
- Question answering is read/recommend behavior. It must not mutate business records except for explicitly defined review/gap tasks and audit evidence.
- Do not expose raw chunk embeddings, storage keys, hidden prompts, inaccessible snippets, or another company's records.
- Preserve Support's deterministic reply-safety and approval requirements; a grounded answer is not permission to send a customer reply.

### 6. Acceptance Criteria

- Given indexed permitted documents and current records, when an agent asks a supported question, then the answer contains claim-level valid citations and a persisted source-linked run.
- Given only weak or contradictory evidence, when answering, then the response labels uncertainty and requires review rather than selecting an unsupported fact.
- Given an inaccessible document from the same company or another company, when answering, then its content and identifying metadata are absent.
- Given a document still queued for indexing, when answering, then the user sees that the source is not ready and no unsupported answer is generated.
- Given the same question retried with the same conversation request identity, when gap/task creation is required, then duplicate review work is not created.

### 7. Verification

- Unit-test claim/source validation, confidence thresholds, contradictory evidence, stale sources, and gap deduplication.
- Add integration tests for document grounding, record grounding, bounded memory, agent-scope isolation, cross-tenant isolation, and insufficient evidence.
- Extend direct-chat tests and add API authorization tests.
- Add Web tests for cited facts, inference labels, missing information, needs-review, loading, and failure states.
- Run Support grounding/safety tests to prove no reply-sending boundary was weakened.

### 8. Definition Of Done

All agents can answer permitted company questions through one grounded capability with valid citations, explicit uncertainty, safe review behavior, and tenant isolation. No uncited confirmed claims, unrestricted retrieval, fabricated answers, or UI-only authorization remain.

---

## Prompt 4: Add AI-Supported Role Briefings On The Existing Durable Briefing Pipeline

### 1. Title And Outcome

Extend the existing durable briefing subsystem so Laura, Alex, and Ben can produce role-specific daily, weekly, and event-driven briefings, while a joint executive briefing preserves source evidence, conflicting assessments, uncertainty, decisions required, and cross-agent dependencies.

### 2. Current Context

- `CompanyBriefingService`, `CompanyBriefingGenerationPipeline`, briefing entities/sections/contributions, priorities, source references, preferences, scheduler, update-job producer/runner, and dashboard briefing summaries already provide durable deterministic briefings.
- Existing briefing tests cover aggregation, preferences, scheduling, retries, links, and tenant isolation.
- `MultiAgentCoordinator` supports bounded manager-worker collaboration and contributor rationale summaries.
- Current briefings already aggregate tasks, approvals, alerts, workflow exceptions, bills, agent updates, and cash-position information, but shared AI interpretation and role-owned contribution contracts are not consistently standardized.

### 3. Dependencies

- Prompt 2: validated reasoning runs.
- Prompt 3: grounded evidence and citation rules.
- Existing briefing scheduler and update jobs must be operational.

### 4. Implementation Requirements

- Extend existing briefing application contracts with role-owned contribution providers for Finance, Sales, and Support. Each provider must supply deterministic facts and source references before AI interpretation.
- Use the shared reasoning gateway to synthesize `what changed`, `why it matters`, `recommended actions`, `decisions required`, `dependencies`, and `uncertainty` from those authoritative contributions.
- Preserve deterministic priority and deadline calculations. AI may explain ranking but may not alter SLA, due-date, approval, cash, or workflow truth.
- Persist AI-supported narrative as versioned briefing contribution/section data linked to orchestration runs and source records. Reuse existing briefing entities where they fit; do not create a parallel briefing store.
- Implement daily and weekly role briefing generation through existing scheduled jobs, with stable idempotency per company, agent role, cadence, period, and version.
- Implement event-driven refresh for material supported events using existing briefing update jobs/outbox patterns. Coalesce bursts and prevent duplicate briefings.
- Implement a joint executive briefing that consolidates role sections but retains contributor identity, conflicting recommendations, confidence, unresolved decisions, and links. Do not merge disagreements into false certainty.
- Enforce per-user briefing preferences and agent/company data scopes when material is retrieved and when links are rendered.
- Extend existing dashboard/agent briefing surfaces to filter by role/cadence and drill into source records, using current design patterns.
- Add observable retry/failure state and safe operator actions. A failed AI narrative must not discard deterministic briefing facts.

### 5. Constraints And Preservation Rules

- Do not replace `CompanyBriefingService`, the scheduler, or update-job worker with a new generic background loop.
- Do not let the model invent metrics, due dates, status, monetary values, or source links.
- Keep role providers inside their owning Finance, Sales, and Support modules; shared aggregation stays in the briefing/orchestration subsystem.
- Preserve existing routes, preferences, dashboard contracts, source-link access checks, retry semantics, and briefing tests unless an additive versioned contract change is necessary.
- Any outbound briefing delivery remains subject to existing communication preferences and durable delivery patterns.

### 6. Acceptance Criteria

- Given current Finance, Sales, and Support facts, when daily role briefings run, then each contains only role-permitted evidence, source links, recommendations, and decisions required.
- Given conflicting role recommendations, when the executive briefing is produced, then both positions and confidence/evidence remain visible.
- Given the reasoning provider is unavailable, when generation runs, then deterministic sections remain available and the narrative failure is visible and retryable.
- Given duplicate schedule/event delivery, when jobs execute, then only one briefing per idempotency window/version is persisted.
- Given a cross-company linked record, when a briefing is rendered, then it is unavailable and no foreign details are exposed.

### 7. Verification

- Extend briefing aggregation, preference, scheduler, update-job, retry, and cross-tenant tests.
- Add deterministic fake-reasoning tests for each role and joint consolidation, including conflicting and low-confidence inputs.
- Add idempotency/concurrency tests for schedule and event-driven generation.
- Add Web/API tests for role filters, source drill-down, inaccessible links, partial narrative failure, and executive conflict presentation.
- Build API and Web and run existing dashboard briefing tests.

### 8. Definition Of Done

Role and executive briefings run through the existing durable pipeline, preserve authoritative facts and contributor disagreements, expose evidence and failures, and remain idempotent and company-scoped. No duplicate briefing stack, fabricated metrics, or silent narrative loss remains.

---

## Prompt 5: Implement Deterministic-First Shared Work Prioritization

### 1. Title And Outcome

Implement one shared work-prioritization capability that ranks tasks, approvals, alerts, cases, deals, bills, and other supported work using deterministic urgency and impact signals, then uses AI only to explain context, dependencies, and recommended sequencing.

### 2. Current Context

- `CompanyActionInsightService`, briefing priority rules, task priorities, approval states, workflow exceptions, Finance heuristics, Sales recommendation/risk data, and Support SLA/priority logic already produce domain-specific signals.
- Work tasks already store owner, priority, due date, rationale, confidence, workflow and source context.
- Existing briefing aggregation orders sections by deterministic priority scores.
- There is no common cross-domain prioritization contract exposing urgency, impact, confidence, due date, dependencies, policy reason, and AI explanation without duplicating domain policy.

### 3. Dependencies

- Prompt 2: validated reasoning output.
- Prompt 3: evidence and source-reference validation.
- Prompt 1 capability catalog for effective read/recommend permission.

### 4. Implementation Requirements

- Add a shared application query contract for prioritizing a bounded set of company work items for an agent, role, or executive view.
- Define a normalized candidate contract containing stable source type/ID, authoritative status, deterministic urgency signals, deterministic impact signals, due date/SLA, approval state, monetary exposure if permitted, owner, dependencies, and source references.
- Keep candidate adapters in Finance, Sales, Support, Task/Workflow, and Approval ownership. Each adapter must call existing authoritative policies and calculations rather than reimplementing them.
- Implement deterministic score/category calculation with versioned rules and reason codes. Make rule contributions inspectable.
- Use the shared reasoning gateway only after deterministic scoring to explain ties, dependencies, sequencing, uncertainty, and recommended next action. AI must not lower a policy-defined critical deadline or elevate an unsupported item above deterministic critical work.
- Return a structured ranked result with urgency, impact, confidence, due date, dependencies, deterministic score/reasons, AI rationale, and source links.
- Persist material prioritization runs through the orchestration-run record and link accepted priority changes or created tasks through audit evidence. Read-only ranking must not silently rewrite source priorities.
- Expose authorized API queries and integrate results into existing agent/dashboard priority surfaces rather than creating an isolated dashboard.
- Add stale-data handling: include calculation timestamp and source version/update times; do not present stale rank as current after material source changes.

### 5. Constraints And Preservation Rules

- Support SLA, approval status, Finance due dates/amounts, workflow blocked states, and domain policy remain authoritative.
- Do not create one generic policy engine that replaces domain policies.
- Do not load all company records into memory; use bounded, server-side candidate queries and documented limits.
- Do not expose financial impact to agents/users lacking Finance scope.
- Ranking is read/recommend behavior unless a separate approved command explicitly updates a task or creates follow-up work.

### 6. Acceptance Criteria

- Given a breached SLA, a blocked critical workflow, and routine work, when ranking occurs, then deterministic critical items remain above routine items regardless of AI wording.
- Given two similarly scored items with different dependencies, when AI sequencing succeeds, then the explanation cites those dependencies and valid sources.
- Given AI failure, when ranking completes, then deterministic order and reasons remain available with a visible interpretation failure.
- Given an agent without Finance scope, when ranking is requested, then restricted financial candidates and values are absent.
- Given source data changes after a cached/ranked result, when requested again, then stale results are invalidated or clearly marked and recalculated.

### 7. Verification

- Unit-test deterministic scoring, reason ordering, ties, deadlines, missing values, versioning, and bounded candidate limits.
- Add adapter tests proving each domain reuses authoritative policies.
- Add tenant-isolation, authorization, restricted-field, AI-failure, stale-data, and no-mutation integration tests.
- Add Web presentation tests for ranked items, deterministic reasons, AI explanation, missing scope, loading, and partial failure.
- Run existing action insight, briefing priority, Finance, Sales risk, Support SLA, and task tests.

### 8. Definition Of Done

Shared prioritization produces explainable, bounded, current, company-scoped rankings without replacing domain truth or mutating work implicitly. Deterministic results survive AI failure, restricted data stays hidden, and all material claims are source-linked.

---

## Prompt 6: Implement Bounded Planning And Durable Task Decomposition

### 1. Title And Outcome

Allow an agent to turn a business objective into a bounded, reviewable plan of durable tasks, dependencies, handoffs, approvals, and expected evidence, using registered capabilities and workflow definitions rather than open-ended autonomous loops.

### 2. Current Context

- `WorkTask`, task command/query services, workflows, approvals, scheduled triggers, proactive task creation deduplication, and `MultiAgentCoordinator` already support durable work and bounded collaboration.
- `OrchestrationAction`, tool schemas, capability permissions, and guardrails constrain executable behavior.
- Multi-agent collaboration currently accepts explicit worker subtasks and enforces worker/depth/runtime/step limits.
- There is no common objective-to-plan contract that validates task ownership, dependencies, evidence, approvals, and completion criteria before durable creation.

### 3. Dependencies

- Prompt 1: capability catalog.
- Prompt 2: validated reasoning.
- Prompt 5: shared prioritization inputs for proposed sequencing.
- Existing task, workflow, and approval services.

### 4. Implementation Requirements

- Add a shared planning application service with separate `GeneratePlan` query/recommend behavior and `CommitPlan` command behavior.
- Define a versioned plan schema containing objective, assumptions, constraints, ordered steps, owner agent/user, capability/tool intent, source records, expected outcome, completion evidence, due date, dependencies, approval requirement, handoff requirement, and escalation path.
- Generate plan drafts through the shared reasoning gateway using only effective capabilities and known workflow/tool schemas.
- Deterministically validate every proposed step: company/agent ownership, supported capability, tool action class, permissions, dependency graph, cycles, step/agent/runtime limits, due-date consistency, approval need, and required evidence.
- Return validation errors and review requirements without creating tasks when a plan is invalid.
- On explicit authorized commit, create a durable parent `WorkTask` and child tasks using existing task services, preserving the plan ID/version, source references, dependency metadata, and correlation ID. Reuse workflow instances where an existing workflow definition owns the process.
- Require approval before committing plans that create sensitive execution tasks or cross configured thresholds. Committing a plan does not itself perform external side effects.
- Make commit idempotent using company, objective/source identity, plan version, and request identity. Concurrent retries must not duplicate task trees.
- Expose authorized draft, validate, commit, status, and cancel endpoints as appropriate, with transport-only controllers and typed Web clients.
- Add a review UI in the existing agent/task workflow surfaces showing assumptions, invalid steps, dependencies, approvals, owners, evidence, and commit status.
- Audit generation, validation failure, commit, cancellation, and material plan changes.

### 5. Constraints And Preservation Rules

- No recursive autonomous loop, arbitrary code execution, unregistered tool, or model-created workflow definition is allowed.
- Use existing tasks/workflows as systems of record. Do not store critical plan state only as opaque JSON if it must be queried or transitioned.
- A plan cannot bypass domain eligibility or approval by labeling an action as a task.
- Preserve `MultiAgentCoordinator` limits and prohibit worker agents from recursively delegating unless a future explicit policy changes that rule.
- Any schema change requires an EF migration and local/Docker SQL Server verification.

### 6. Acceptance Criteria

- Given a bounded objective and available capabilities, when a plan is generated, then every step has an owner, expected outcome, evidence, dependency state, and capability/tool classification.
- Given a proposed unsupported or unauthorized step, when validation runs, then commit is blocked with a stable reason and no tasks are created.
- Given a cyclic dependency graph, when validated, then it is rejected deterministically before persistence.
- Given two identical commit retries, when processed, then one durable task tree exists.
- Given a sensitive action step, when committed, then it is marked approval-dependent and no external action occurs before authoritative approval and execution policy checks.

### 7. Verification

- Unit-test plan schema validation, graph cycles, limit enforcement, permission checks, approval classification, and idempotency-key construction.
- Add integration tests for draft/commit separation, durable task tree creation, workflow reuse, cancellation, retries, concurrency, authorization, and cross-tenant isolation.
- Add guardrail tests proving commit cannot execute an external action.
- Add Web tests and screenshot verification for plan review if the UI is a substantial new surface.
- Build API and Web and run task, workflow, approval, proactive-task, and multi-agent collaboration tests.

### 8. Definition Of Done

Agents can generate and explicitly commit bounded production plans into durable governed work. Invalid plans create nothing, retries are idempotent, sensitive steps remain approval-gated, and no autonomous-loop scaffolding or hidden task state remains.

---

## Prompt 7: Implement Evidence-Backed Exception Interpretation

### 1. Title And Outcome

Implement a shared exception-interpretation capability that explains anomalies, contradictory records, low-confidence matches, ambiguous messages, blocked workflows, and failed executions, while clearly separating deterministic facts from AI-generated hypotheses and diagnostic suggestions.

### 2. Current Context

- `ExecutionExceptionRecord`, workflow exceptions, background execution records, tool attempts, reconciliation warnings/suggestions, Finance anomaly services, Support triage/safety outcomes, and Sales risk signals already capture domain-specific failures and exceptions.
- Existing dashboards and briefings surface some blocked workflows, alerts, and anomalies.
- Audit events and correlation IDs provide evidence trails.
- There is no shared interpretation envelope or diagnostic workflow that can consume typed exceptions from multiple modules without replacing their authoritative state.

### 3. Dependencies

- Prompt 2: validated reasoning and persisted runs.
- Prompt 3: evidence/source validation.
- Prompt 5: prioritization for exception urgency.

### 4. Implementation Requirements

- Define a shared typed `AgentExceptionContext` application contract with stable exception kind, source type/ID, authoritative status, safe diagnostic facts, correlation IDs, attempts, policy decisions, related records, and permitted source references.
- Implement adapters in Background Execution, Workflow, Finance, Sales, and Support that map existing exception records into the shared context without changing domain ownership.
- Add deterministic classification for permanent versus retryable, approval blocked, configuration missing, dependency unavailable, ambiguous provider outcome, data contradiction, and human decision required.
- Use shared reasoning to produce hypotheses, evidence for/against each hypothesis, confidence, next diagnostic steps, operator decision required, and prohibited unsafe actions.
- Label all model-suggested causes as hypotheses unless directly established by authoritative evidence. Never rewrite the underlying exception status from model output.
- Link interpretation runs to the source exception, related tasks/workflows/tool attempts, and audit events. Deduplicate repeated interpretations for the same exception version unless explicitly refreshed.
- Provide authorized read/recommend endpoints and integrate interpretation into existing exception/anomaly detail surfaces with source drill-down and retry/approval links driven by authoritative allowed-action policies.
- Any retry, reconciliation, approval, or corrective execution remains a separate existing command with fresh policy/state checks.
- Record safe technical observability while redacting provider payloads, customer secrets, credentials, and inaccessible data.

### 5. Constraints And Preservation Rules

- Do not create a new generic exception state machine that supersedes workflow, background execution, integration reconciliation, or domain exception records.
- AI cannot classify an ambiguous external provider outcome as success or authorize blind retry.
- Do not expose raw stack traces or technical details to normal users; retain them only in authorized technical logs where appropriate.
- Do not allow an interpretation endpoint to mutate or retry the source operation.
- Preserve existing allowed-action and reconciliation policies.

### 6. Acceptance Criteria

- Given a blocked workflow with authoritative policy evidence, when interpreted, then facts and policy reason are confirmed while proposed causes are clearly labeled.
- Given an ambiguous provider outcome, when interpreted, then reconciliation is recommended and blind retry is explicitly prohibited.
- Given a permanent validation failure, when interpreted, then it is not described as retryable.
- Given repeated requests for an unchanged exception, when processed, then the existing interpretation is reused or versioned without duplicate tasks/audit noise.
- Given a user lacking access to the source record, when requesting interpretation, then neither the interpretation nor source metadata is disclosed.

### 7. Verification

- Unit-test exception adapters, deterministic classification, hypothesis labeling, version/deduplication, and redaction.
- Add integration tests for workflow, background execution, Finance anomaly/reconciliation, Sales risk, and Support exception examples.
- Add authorization and cross-tenant tests and prove read endpoints do not mutate or retry.
- Add Web tests for facts, hypotheses, confidence, source links, unavailable technical details, and authoritative allowed actions.
- Run existing workflow exception, reconciliation, integration failure, tool execution, and audit tests.

### 8. Definition Of Done

Operators receive evidence-backed, source-linked exception explanations and safe next steps without AI mutating truth, hiding uncertainty, or bypassing retry/reconciliation/approval policy. All supported exception adapters are production implementations with visible failure behavior.

---

## Prompt 8: Implement Typed Cross-Agent Handoffs On Existing Collaboration And Workflow Foundations

### 1. Title And Outcome

Implement durable typed handoffs between Finance, Sales, Support, and future agents so one agent can request a bounded outcome from another with only permitted evidence, explicit ownership, due date, status, escalation, and auditable completion.

### 2. Current Context

- `MultiAgentCoordinator` and its contracts already support bounded coordinator/worker tasks, limits, contributions, rationale summaries, and safe termination.
- `WorkTask`, workflow, approval, notifications, audit, and agent assignment guards already provide durable execution foundations.
- `SalesFinanceHandoff` exists as a domain-specific handoff and Support/Finance refund-dispute flows already coordinate through domain workflows.
- There is no shared typed handoff lifecycle covering the scenarios in `agents-ai.md` without over-sharing source-module context.

### 3. Dependencies

- Prompts 1-3: capability, reasoning, and evidence boundaries.
- Prompt 6: durable plan/task decomposition.
- Existing `MultiAgentCoordinator`, task, workflow, approval, notification, and audit systems.

### 4. Implementation Requirements

- Add shared application contracts and a domain aggregate for `AgentHandoff` if existing tasks cannot represent typed handoff lifecycle and query needs. Include company, type/version, requesting/receiving agent, objective, requested outcome, status, due date, escalation path, source links, permitted evidence snapshot, related task/workflow/approval IDs, completion summary, confidence, failure reason, correlation, and timestamps.
- Define stable handoff types for won-deal invoice readiness, customer payment risk, refund/credit/invoice dispute, churn/retention risk, product or documentation gap, and a generic reviewed internal request. Keep payload schemas versioned and typed.
- Implement deterministic handoff validators per type in the owning modules. Required records, scopes, evidence, approval state, and receiving capability must be checked before creation.
- Resolve the minimum permitted evidence for the receiving agent. Do not copy unrestricted source records into generic JSON. Store references and safe bounded snapshots where history requires them.
- Create receiving work through existing task/workflow services and use `MultiAgentCoordinator` only where bounded collaboration is needed; do not replace it.
- Implement lifecycle transitions: proposed, accepted, in progress, awaiting information/approval, completed, rejected, cancelled, failed, and escalated, with legal transition checks and optimistic concurrency.
- Make creation and completion idempotent using company, handoff type, source business identity, requested outcome version, and receiving agent.
- Trigger notifications, due-date escalation, and briefing updates through existing outbox/background patterns.
- Adapt `SalesFinanceHandoff` and existing Support/Finance flows to publish or link the shared handoff without breaking current contracts or migrating history unsafely. Avoid two active systems of record for the same handoff.
- Add authorized endpoints, typed Web clients, and handoff views in existing agent/task surfaces showing objective, owner, due state, evidence, approval, and outcome.
- Audit every transition and evidence-scope decision.

### 5. Constraints And Preservation Rules

- A handoff does not grant new data access. Receiving agents see only references and snapshots permitted by their profile and the handoff policy.
- Do not use handoffs to bypass approval, domain eligibility, or external-action execution paths.
- Do not let worker agents recursively delegate outside existing bounded-collaboration policy.
- Preserve existing Sales/Finance and Support/Finance business workflows during migration/adaptation.
- Add an EF migration and model snapshot for new persistence, with local and Docker SQL Server verification.

### 6. Acceptance Criteria

- Given a won deal with complete permitted evidence, when Alex creates an invoice-readiness handoff, then Laura receives one durable task/handoff linked to the deal and permitted source records.
- Given a Support refund request, when Ben hands off to Laura, then Finance policy/approval remains authoritative and Support does not gain unrestricted Finance data.
- Given a receiving agent lacking the required capability or status, when creation is attempted, then it is rejected before persistence with an actionable reason.
- Given duplicate event or command delivery, when creation runs, then only one active handoff exists for the business identity/version.
- Given an overdue handoff, when escalation runs, then one idempotent notification/task update is produced and the escalation is audited.

### 7. Verification

- Unit-test type schemas, validators, lifecycle transitions, evidence minimization, idempotency keys, and escalation rules.
- Add SQL-backed integration tests where concurrency or migration behavior matters.
- Add cross-tenant, cross-agent-scope, authorization, duplicate-delivery, cancellation, and approval-preservation tests.
- Extend `MultiAgentCoordinator`, task, workflow, notification, briefing, SalesFinanceHandoff, and Support refund/dispute tests.
- Add Web tests and screenshot verification for any substantial handoff surface.
- Verify migration application and restore compatibility for local and Docker SQL Server.

### 8. Definition Of Done

Cross-agent work is exchanged through typed, durable, minimal-evidence handoffs with clear ownership, status, escalation, idempotency, and audit. Existing domain workflows remain authoritative, and no handoff grants implicit permissions or performs unapproved side effects.

---

## Prompt 9: Implement A Governed Shared Memory-Candidate Lifecycle

### 1. Title And Outcome

Implement a shared memory-candidate lifecycle that allows agents to propose bounded reusable observations from completed work while deterministic policy controls sensitivity, scope, duplication, retention, review, activation, correction, and expiry.

### 2. Current Context

- `MemoryItem`, context retrieval, memory snippets, customer-memory profile entities, Support memory observations/update jobs/safety policy, and domain-specific learning flows already exist.
- Grounded prompts can include scoped memory, and tests already prove some memory isolation in direct chat.
- Support has a more developed reviewed memory workflow, but Finance and Sales do not share one generic candidate contract.
- Directly writing model output into active `MemoryItem` records would bypass review, sensitivity, and retention controls.

### 3. Dependencies

- Prompt 2: validated reasoning runs.
- Prompt 3: source evidence.
- Prompt 8: completed handoff outcomes as optional memory sources.

### 4. Implementation Requirements

- Add a shared `IAgentMemoryCandidateService` application boundary and, if required, a company-owned `AgentMemoryCandidate` aggregate distinct from active `MemoryItem`.
- Define candidate fields: company, proposing agent, memory type, proposed scope, subject/customer/supplier/source links, bounded content, evidence references, confidence, sensitivity classification, retention suggestion, dedupe fingerprint, review requirement/reason, status, reviewer, activation/expiry/rejection details, orchestration run, and timestamps.
- Allow candidate proposal only from completed/reviewed source work and validated reasoning output. The model may propose content/type; deterministic policy decides whether it can be accepted.
- Implement deterministic policies for allowed memory types, maximum size, prohibited secrets/sensitive categories, company/agent/customer scope, duplicate/contradiction detection, retention, source accessibility, and mandatory human review.
- Reuse or adapt Support memory safety/observation behavior rather than maintaining conflicting shared and Support policies. Preserve stricter Support rules.
- Implement explicit statuses such as proposed, needs review, approved, activated, rejected, superseded, expired, and failed with legal transitions.
- Activate approved candidates into existing `MemoryItem` or the appropriate domain-owned memory profile through one idempotent service. Keep the candidate-to-active-memory link and never activate twice.
- Exclude unapproved, expired, superseded, or inaccessible candidates from prompt retrieval.
- Add authorized review/list/detail/approve/reject/correct endpoints and integrate a focused review queue into existing agent/knowledge administration UI.
- Add scheduled expiry/retention handling through existing background execution patterns and audit every review/activation/expiry transition.

### 5. Constraints And Preservation Rules

- Model output never becomes active memory automatically unless a deterministic policy explicitly permits a narrowly defined low-risk class.
- Do not store credentials, payment details, special-category personal data, hidden prompts, or unrestricted raw conversations as memory.
- Memory access remains company-scoped and, when configured, agent/customer/supplier scoped.
- Do not replace customer memory profiles or Support memory observations blindly; add adapters and one authoritative activation path.
- If adding persistence, include an EF migration and local/Docker SQL Server verification.

### 6. Acceptance Criteria

- Given a completed source task with evidence, when a low-risk candidate is proposed, then it is deduplicated, policy-evaluated, and persisted with source links and review state.
- Given a candidate containing prohibited sensitive data, when evaluated, then activation is blocked and the safe reason is visible without retaining unnecessary secret content.
- Given an approved candidate retried for activation, when processed, then exactly one active memory record is linked.
- Given an expired or rejected candidate, when grounded context is retrieved, then it is never included.
- Given a candidate from another company or outside the agent's scope, when queried or reviewed, then it is not disclosed or activated.

### 7. Verification

- Unit-test sensitivity rules, scope policy, dedupe/contradiction fingerprints, retention, transitions, and activation idempotency.
- Add integration tests for proposal from completed work, review, correction, activation, retrieval inclusion, expiry, and Support policy preservation.
- Add authorization and tenant/agent/customer-scope isolation tests.
- Add background expiry retry/concurrency tests and audit assertions.
- Add Web review-queue tests and screenshot verification if the surface is substantial.
- Verify migration/model snapshot and local/Docker SQL Server compatibility.

### 8. Definition Of Done

Shared agent learning occurs only through bounded evidence-backed candidates and deterministic governance. Active memory is deduplicated, scoped, reviewable, expirable, and auditable; unsafe or unapproved model output never enters retrieval.

---

## Prompt 10: Measure AI Recommendation Quality, Human Feedback, And Business Outcomes

### 1. Title And Outcome

Implement shared AI quality measurement so operators can see grounding quality, recommendation acceptance/correction, policy blocks, execution outcomes, and capability reliability by agent and capability, enabling evidence-based autonomy increases rather than subjective trust.

### 2. Current Context

- Orchestration results, context retrieval sources, tool attempts, tasks, approvals, audit events, briefings, recommendations, support executions, and domain outcomes already hold parts of the required evidence.
- Prompt 2 adds durable shared orchestration runs; later prompts add standardized capability outputs, handoffs, and memory candidates.
- Existing dashboards and agent status surfaces show activity but do not provide a shared quality model connecting recommendation, human decision, correction, execution, and business outcome.

### 3. Dependencies

- Prompts 1-9 completed.
- Existing analytics/cockpit read-model patterns and authorized agent-management UI.

### 4. Implementation Requirements

- Define a shared application analytics contract and stable event taxonomy for recommendation produced, viewed, accepted, rejected, corrected, expired, approval requested/approved/rejected, tool blocked/executed/failed/reconciled, handoff completed, knowledge gap created/closed, and memory candidate approved/rejected.
- Link events to company, agent, capability/version, orchestration run, source business record, task/workflow/approval/tool attempt, model/schema version, confidence band, and correlation ID without copying sensitive content.
- Capture explicit human feedback with reason codes and optional bounded comments. Do not infer acceptance merely from page views.
- Derive quality measures including valid-source coverage, unsupported-claim/validation failure, review rate, acceptance, correction, policy-block rate, execution success/failure/reconciliation, handoff completion, memory approval, and latency/cost where safely available.
- Add capability-specific outcome adapters for Finance, Sales, and Support only where causality can be stated carefully. Separate correlation from attribution and do not claim AI caused a business outcome without evidence.
- Build company-scoped read projections using existing analytics/cockpit patterns. Use bounded aggregation and appropriate indexes; do not calculate large metrics in Blazor components.
- Add authorized API queries with time range, agent, capability, action class, and outcome filters.
- Add an agent AI quality section to the existing management/cockpit experience showing coverage, acceptance/correction, blocked actions, failures, and drill-down evidence. Use plain English and avoid vanity scores without components.
- Add configurable reliability thresholds that can recommend an autonomy review but cannot automatically raise autonomy. Any autonomy change remains an explicit authorized profile command with audit.
- Add retention, redaction, and safe deletion behavior consistent with audit and privacy requirements.
- Add technical metrics for provider latency, invalid output, retries, and failures without leaking prompts or customer content.

### 5. Constraints And Preservation Rules

- Metrics are company-scoped and must not reveal another tenant's volumes, prompts, costs, or model behavior.
- Do not store full prompts or raw sensitive outputs solely for analytics.
- Do not collapse all capabilities into one opaque quality score. Show component measures, sample size, time window, and uncertainty.
- Quality thresholds may recommend but never automatically increase autonomy or bypass approvals.
- Reuse existing audit/outcome records where possible; avoid duplicate event writes and double counting through stable event identities.
- Add an EF migration for new feedback/events/projections only when existing records cannot meet query and retention requirements; verify local/Docker SQL Server compatibility.

### 6. Acceptance Criteria

- Given a recommendation that a user accepts, when feedback is recorded, then one idempotent accepted event is linked to the exact run/capability and reflected in metrics.
- Given a corrected recommendation, when feedback is submitted, then correction is counted separately from rejection and the bounded reason is auditable.
- Given an unsupported citation rejected by Prompt 2 validation, when metrics are queried, then it contributes to validation/grounding failure and never to accepted recommendation counts.
- Given a small sample, when quality is displayed, then the sample size and insufficient-evidence state are visible and autonomy is not automatically changed.
- Given another company requests metrics or drill-down records, then no data is returned.

### 7. Verification

- Unit-test event identities, feedback validation, aggregation formulas, confidence/sample-size handling, and double-count prevention.
- Add integration tests spanning recommendation, feedback, approval/tool outcome, handoff, and memory events.
- Add tenant-isolation, authorization, retention/redaction, idempotency, and large bounded-query tests.
- Add Web client and component tests for filters, empty state, insufficient sample, metric drill-down, and safe failures; perform screenshot-first and responsive browser verification for a substantial new quality surface.
- Run affected analytics, cockpit, orchestration, audit, approval, tool execution, and agent-management suites and build API/Web.
- If persistence changes, verify migration discovery, pending-model changes, indexes, and local/Docker SQL Server migration/restore paths.

### 8. Definition Of Done

The system measures shared AI behavior from grounded recommendation through human feedback and governed outcome with idempotent, company-scoped evidence. Operators can identify reliable and weak capabilities without hidden formulas, data leakage, automatic autonomy escalation, mock metrics, or deferred in-scope TODOs.
