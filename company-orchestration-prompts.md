# Company Orchestration Implementation Prompts

> Implementation status (2026-08-12): The production baseline for Prompts 1–12 is implemented. This pack remains the acceptance and maintenance specification; “implemented” does not mean that every fault-injection, provider-level concurrency, multi-host recovery, or live-model failure criterion has been exhaustively verified. See the status matrix below and `company-orchestration.md`.

## Current status matrix

| Prompt | Status | Repository evidence | Remaining acceptance work |
|---|---|---|---|
| 1. Goals and configuration | Implemented | Durable goals/configuration, coordinator, cadence, autonomy, budgets, pause and emergency stop APIs/UI | Broader authorization and SQL migration-path regression coverage |
| 2. Durable operating model | Implemented | Cycles, snapshots, plans, initiatives, dependencies, decisions, reviews, events, requests, leases, dispatches and migration | Persistence index/cascade and pagination matrix tests |
| 3. State snapshot and signals | Implemented | Core snapshot plus Finance, Sales, Marketing and Support contributors; explicit gaps/truncation/failures; focused failure, cancellation and tenant-isolation tests | Correlation, truncation, duplicate normalization and representative populated department integration tests |
| 4. Deterministic validation | Implemented | Tenant, goal, duplicate, dependency, owner, capability, capacity, date, evidence, budget, action and autonomy checks | Full rule-by-rule integration matrix and stale-config race tests |
| 5. Recommendation cycle | Implemented | Bounded guarded reasoning, immutable proposal, source snapshot, validation and safe failure lifecycle | Malformed/oversized/contradictory/ungrounded live-gateway tests |
| 6. Review workspace | Implemented | Professional company-operation workspace, review/commit state, controls, usage, automatic activity, dispatch and snapshot visibility | Runtime visual/accessibility regression after restarting the user-owned host |
| 7. Approval and atomic commit | Implemented | Shared authoritative approval, request changes, serializable transaction, idempotent tasks and dispatch links | SQL Server raced-commit and forced mid-transaction rollback tests |
| 8. Agent dispatch and collaboration | Implemented | Durable leased dispatcher, autonomy recheck, single-agent/multi-agent paths, retries, block/dead-letter states | End-to-end orchestration and concurrent worker integration tests |
| 9. Recurring/event cycles | Implemented | Daily timezone scheduler, task/workflow/approval/Marketing/background events, coalescing, per-company lease and retries | Multi-host restart, missed-window and expired-lease recovery integration tests |
| 10. Evidence review and replan | Implemented baseline | Evidence-version review, missing-evidence handling, escalation, validated immutable revisions | Authoritative before/after KPI and confirmed goal-impact comparison |
| 11. Governed autonomy and budgets | Implemented | Company plus agent authority, current validation/config checks, daily task/model/tool/money limits and kill switches | Boundary-race and accumulated-usage stress tests |
| 12. Controlled actions | Implemented narrow allowlist | `operator_notification` readiness registry, decision approval, active-recipient check and idempotent outbox | Provider-dispatch/reconciliation integration test; additional actions require their own complete readiness contracts |

Focused verification on 2026-08-12: 23 orchestration API/domain tests and 2 Web surface tests pass. Affected Operations, Finance, Sales, Support, API dependency graph, and Web projects build from isolated artifact paths. Existing unrelated compiler/analyzer warnings remain outside this prompt pack.

## How to use this prompt pack

Execute these prompts in order. Each prompt delivers one production-capable outcome and states its prerequisites. Do not stop the overall implementation sequence at intermediate build or test checkpoints; continue through the requested prompt or ordered prompt set until complete or genuinely blocked.

For every prompt:

- Read and follow `production-implementation.md`, `company-orchestration.md`, and `docs/architecture-rules.md` before changing code.
- `architecture-inst.md` is required by repository instructions for architecture-sensitive work, but it does not currently exist. If it is added before execution, read and follow it. Do not invent its contents or silently ignore a newly added version.
- Existing repository behavior is authoritative when it conflicts with older planning documents.
- Implement real production behavior: no scaffolding, fake production data, placeholder services, silent fallback success, unhandled intermediate states, or deferred in-scope TODOs.
- Preserve the modular-monolith boundaries, CQRS-lite separation, tenant isolation, authorization, approval enforcement, audit evidence, and existing shared AI orchestration stack.
- Use EF Core migrations as the only schema authority. Keep local SQL Server and Docker SQL Server restore and run paths equivalent.
- Place tests in the narrowest suitable project. Do not weaken or remove valid tests.
- Do not call an LLM provider directly from a capability module. Use the existing shared orchestration and `IAgentReasoningGateway` boundaries.
- Treat model output as a proposal. Backend validation and policy are authoritative.
- Use durable background execution and the outbox for long-running work and important external side effects.

For prompts containing UI work, also read and follow `ui-instructions.md` and `docs/design.md`. Complete their mandatory screenshot-first workflow before implementing a new page, modal, dashboard, major component, or significant redesign. Save the reference under `docs/design/references/`, then compare the implemented UI with the reference and refine it. Do not ship the screenshot as a UI asset.

---

## Prompt 1 — Establish company goals and executive operating configuration

### 1. Title and outcome

Implement durable company-wide goals and executive operating configuration so each company has authoritative outcomes, constraints, and a designated coordinator agent. This gives later operating cycles a real source of business intent instead of relying on ad hoc prompts or agent JSON alone.

### 2. Current context

The solution has company-scoped `Agent`, `WorkTask`, workflow, approval, briefing, audit, and orchestration entities in `VirtualCompany.Domain`. Agent configuration contains flexible objectives, and Marketing has capability-specific objectives, but there is no authoritative cross-company `CompanyGoal`. Agents are managed through `AgentsController`, company membership uses `CompanyPolicies.CompanyMember` and `CompanyPolicies.CompanyManager`, and tenant-owned persistence is centralized in `VirtualCompanyDbContext` with configuration classes discovered by convention.

Relevant current files include:

- `src/VirtualCompany.Domain/Entities/AgentEntities.cs`
- `src/VirtualCompany.Domain/Entities/TenantEntities.cs`
- `src/VirtualCompany.Persistence/Persistence/VirtualCompanyDbContext.cs`
- `src/VirtualCompany.Application/Companies/CompanyContracts.cs`
- `src/VirtualCompany.Api/Controllers/AgentsController.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/OperationsModuleRegistration.cs`

### 3. Dependencies

None.

### 4. Implementation requirements

- Add focused domain entities and typed statuses for `CompanyGoal` and `CompanyOperatingConfiguration`; do not create a catch-all orchestration entity file.
- Store goal name, plain-English outcome, metric key and unit when measurable, baseline and target values when applicable, start and target dates, priority, owner user or agent, status, constraints, creation/update metadata, and version/concurrency data.
- Store the designated coordinator agent, default autonomy level, operating-cycle timezone and cadence settings, cycle budgets and limits, pause state, and update metadata in company-scoped operating configuration. Important queryable state must use relational columns; JSON is permitted only for bounded flexible thresholds or metadata.
- Enforce deterministic domain transitions for draft, active, paused, completed, and cancelled goals. Reject invalid periods, empty outcomes, unsupported target definitions, and cross-company owners.
- Add Application command/query contracts and separate command/query services for creating, updating, activating, pausing, completing, cancelling, retrieving, and listing goals, plus reading/updating operating configuration.
- Add transport-only, company-scoped API endpoints. Company members may read; company managers may mutate goals and operating configuration. Reapply server-side membership and company ownership checks rather than trusting route IDs.
- Validate that a configured coordinator is an active, assignable agent in the same company. Default new or missing configuration to recommendation-only behavior.
- Write business audit events for goal lifecycle changes, coordinator changes, autonomy changes, budget changes, pause/resume, and rejected configuration changes.
- Add `DbSet` properties, focused EF configurations, appropriate indexes, query filters/conventions, foreign keys, and an EF Core SQL Server migration with an updated model snapshot.
- Preserve existing agent objective JSON and Marketing objectives; do not migrate or reinterpret them as company goals without an explicit, safe mapping.
- Add concise operator/developer documentation for configuration defaults and Docker/local SQL Server migration behavior.

### 5. Constraints and preservation rules

- Follow `production-implementation.md`, `company-orchestration.md`, and `docs/architecture-rules.md`; read `architecture-inst.md` if it exists at execution time.
- All records and relationships must be company-scoped and resistant to cross-company reads and writes.
- Do not put goal policies or EF queries in controllers or UI code.
- Do not introduce a generic policy engine, event sourcing, a second DbContext, startup DDL, or a new infrastructure facade owner.
- Persist typed storage values consistently with existing enum conversion patterns and show only plain-English values in wire contracts.
- Increasing autonomy or unpausing company operation must be explicit, authorized, and audited.

### 6. Acceptance criteria

- Given a company manager and a valid measurable or qualitative goal, when the goal is created and activated, then it is persisted for that company and returned through the company-scoped query API.
- Given a user from another company, when they attempt to read or mutate the goal or operating configuration, then access is denied or the record is not found without revealing its existence.
- Given an inactive, paused, archived, missing, or foreign-company agent, when it is selected as coordinator, then the command is rejected with a stable reason code and plain-English explanation.
- Given a newly configured company, when its operating settings are read, then its effective autonomy is recommendation-only and operation is not silently enabled.
- Given invalid target dates, unsupported numeric targets, or an invalid lifecycle transition, when submitted, then no partial mutation occurs and validation is actionable.
- Given any goal or operating-configuration mutation, when it succeeds, then a company-scoped audit record identifies actor, action, target, outcome, correlation ID, and useful before/after evidence.

### 7. Verification

- Add domain and policy unit tests for validation and lifecycle transitions.
- Add API integration tests for manager/member authorization, cross-company isolation, concurrency conflicts, and validation responses.
- Add migration tests or SQL Server migration inspection for tables, indexes, constraints, conversions, and query filters.
- Run `dotnet ef migrations has-pending-model-changes` with `VirtualCompany.Persistence.Migrations` and `VirtualCompany.Api`; it must report clean after the migration.
- Verify local SQL Server migration and the equivalent Docker restore/run path.
- Build affected Domain, Application, Persistence, Migrations, Operations, API, and test projects.

### 8. Definition of done

Company goals and operating configuration are production-ready, authorized, tenant-isolated, audited, migrated, documented, and tested. No scaffolding, mock data, startup schema manipulation, silent fallbacks, or in-scope TODOs remain.

---

## Prompt 2 — Add durable operating cycles, plans, initiatives, decisions, and reviews

### 1. Title and outcome

Implement the durable operating record model so every management cycle, proposed plan, initiative, decision, dependency, source, validation result, and review has an explainable lifecycle and can be queried independently of chat history.

### 2. Current context

`WorkTask`, `WorkflowInstance`, `ApprovalRequest`, `AgentOrchestrationRun`, `AgentHandoff`, `AuditEvent`, `BackgroundExecution`, and `CompanyOutboxMessage` already persist execution state. Multi-agent collaboration currently stores parent and worker tasks but not a reusable company operating plan. Chat and briefing records are not suitable systems of record for company management state.

Relevant current files include:

- `src/VirtualCompany.Domain/Entities/TaskEntities.cs`
- `src/VirtualCompany.Domain/Entities/WorkflowEntities.cs`
- `src/VirtualCompany.Domain/Entities/AgentAiEntities.cs`
- `src/VirtualCompany.Domain/Entities/AgentExecutionEntities.cs`
- `src/VirtualCompany.Domain/Entities/AuditEvent.cs`
- `src/VirtualCompany.Persistence/Persistence/VirtualCompanyDbContext.cs`

### 3. Dependencies

Prompt 1 and its database migration.

### 4. Implementation requirements

- Add focused company-owned aggregates for `OperatingCycle`, `OperatingPlan`, `OperatingInitiative`, `OperatingDecision`, `OperatingReview`, plan items/dependencies, decision evidence/source references, and validation results.
- Model explicit statuses and legal transitions for requested, observing, planning, awaiting review, approved, committing, active, blocked, completed, failed, cancelled, rejected, and superseded states as appropriate to each aggregate.
- Relate cycles to their trigger, coordinator, goals, snapshot identity, plan version, correlation ID, budget consumption, start/completion timestamps, failure classification, and safe failure summary.
- Make plans immutable by version after review begins. Revisions must create a new version linked to the superseded plan.
- Represent initiatives as bounded outcomes linked to one or more goals, an owner, target date, expected evidence, expected metric impact, budget, current state, and optional task/workflow identifiers after dispatch.
- Persist plan dependencies relationally and enforce company consistency. Add deterministic cycle detection before commit.
- Persist each decision's action class, target, proposed owner, rationale summary, confidence, risk, autonomy classification, approval requirement, policy result, and stable business idempotency key.
- Preserve bounded flexible proposal payloads and model output in JSON only as supplementary evidence; authoritative status, ownership, risk, approval, and idempotency fields must be relational.
- Add Application read contracts and query services for cycle detail/history, plan versions, initiative status, decisions, reviews, evidence, and correlation links. Do not add mutation endpoints beyond the lifecycle operations required to persist and safely fail records at this stage.
- Add transport-only read APIs with pagination and company membership enforcement.
- Add EF configurations, indexes for company/status/due work and idempotency, unique constraints where needed, an EF migration, and updated snapshot.
- Integrate business audit events for lifecycle transitions and plan-version changes.

### 5. Constraints and preservation rules

- Follow all mandatory files named in this prompt pack; read `architecture-inst.md` if it exists.
- Do not replace existing tasks, workflows, approvals, background executions, orchestration runs, or audit events. Link to them through stable references.
- Do not use chat messages as the operating system of record.
- Never make a plan mutation through a read/query service.
- Ensure idempotency uniqueness is company-scoped and derives from business identity, not random retry IDs.
- Preserve local and Docker SQL Server compatibility.

### 6. Acceptance criteria

- Given a new cycle and plan, when they are persisted, then their trigger, goals, coordinator, version, correlation ID, and lifecycle state are queryable.
- Given a plan under review, when its content is changed, then the existing version remains immutable and a linked revision is created.
- Given dependencies containing a cycle or a foreign-company item, when validation runs, then persistence or transition is rejected without partial state.
- Given the same company and business idempotency key, when the same decision is recorded twice, then only one authoritative decision exists.
- Given a plan, when queried, then its initiatives, decisions, validation results, evidence, linked work, and review history are returned without relying on chat history.
- Given another company's identity, when operating records are queried, then no foreign state or existence information is disclosed.

### 7. Verification

- Add domain tests for state transitions, immutability, versioning, dependencies, and idempotency.
- Add persistence/integration tests for mappings, indexes, uniqueness, cascade behavior, and tenant isolation.
- Add API read tests for pagination, authorization, not-found semantics, and correlation projections.
- Inspect and apply the migration against SQL Server; verify Docker restore/migrate compatibility and no pending model changes.
- Build affected projects and run the narrowest relevant test suites.

### 8. Definition of done

Operating records are durable, versioned, tenant-safe, explainable, queryable, audited, and migration-backed. They augment rather than duplicate existing execution records, and all intermediate and failure states are represented.

---

## Prompt 3 — Build the authoritative company-state snapshot and business-signal layer

### 1. Title and outcome

Implement a bounded company-state snapshot that collects authoritative goals, workload, agent capacity, approvals, workflow health, and cross-department business signals for planning without exposing unrestricted database state to the model.

### 2. Current context

The solution already has cockpit and KPI query contracts, business signals, briefings, focus projections, task queries, agent staff overview, approvals, workflow exceptions, Finance insights, Sales pipeline data, Marketing metrics, and Support SLA/knowledge-gap data. `BriefingInsightAggregationService` and executive cockpit services demonstrate aggregation patterns. Generic condition metric/entity resolution is not yet a complete company observation layer.

Relevant current areas include:

- `src/VirtualCompany.Application/Cockpit`
- `src/VirtualCompany.Application/Briefings`
- `src/VirtualCompany.Application/Focus`
- `src/VirtualCompany.Application/Tasks`
- `src/VirtualCompany.Application/Approvals`
- `src/VirtualCompany.Infrastructure.Operations/Companies`
- capability-owned Finance, Sales, Marketing, and Support Application contracts

### 3. Dependencies

Prompts 1 and 2.

### 4. Implementation requirements

- Define Application contracts for `ICompanyOperatingSnapshotService` and capability-specific signal contributors. Contributors return normalized, bounded, company-scoped signals through Application contracts; Operations must not reference sibling infrastructure implementations.
- Create a normalized signal contract containing stable signal type, source type/id, title, plain-English summary, observed time, severity/materiality, freshness, metric/value/unit when applicable, affected goal references, and safe source evidence.
- Aggregate active goals, open initiatives, task ownership and due risk, agent capabilities/status/workload, open approvals, blocked or failed workflows, background execution exceptions, and recent relevant decisions.
- Add contributors for authoritative existing Finance, Sales, Marketing, and Support projections. Reuse existing query/policy services rather than duplicating calculations or querying provider schemas.
- Apply deterministic bounding, ordering, freshness, materiality, and deduplication rules. Record why signals were included or excluded.
- Persist a bounded snapshot record or immutable serialized snapshot linked to an `OperatingCycle`, plus normalized source references and a schema version. Do not persist credentials, raw provider payloads, hidden prompts, or unrestricted documents.
- Ensure snapshot creation is read-only with respect to business modules. It may persist only its own snapshot/evidence records and audit metadata.
- Support partial availability: missing integrations or a failed contributor must be represented as an explicit data gap with safe failure classification. Do not fabricate zero values or declare the company healthy.
- Add correlation, duration, contributor outcome, item counts, truncation, and freshness observability without logging sensitive content.
- Expose a company-scoped diagnostic/read API for authorized users to inspect the snapshot and its data gaps in plain English.

### 5. Constraints and preservation rules

- Follow the mandatory production and architecture documents; read `architecture-inst.md` if present.
- Dashboard/cockpit projections remain read models; do not move their business rules into Operations or UI.
- Capability implementations communicate through Application contracts, not sibling infrastructure references.
- Snapshot creation must never execute tools, create tasks, advance workflows, send messages, or mutate provider state.
- All source queries must be explicitly company-scoped, including background use of `IgnoreQueryFilters`.
- Keep context bounded and schema-versioned so model prompts cannot grow without limit.

### 6. Acceptance criteria

- Given active goals and current work, when a snapshot is created, then it contains relevant goal, task, agent, approval, workflow, and department signals with source references and timestamps.
- Given duplicate signals from multiple projections, when aggregation completes, then deterministic rules produce one normalized result while preserving source evidence.
- Given a stale or unavailable source, when aggregation runs, then the snapshot contains an actionable data gap rather than fabricated state or silent omission.
- Given two companies, when snapshots run concurrently, then no source, signal, count, or error from one appears in the other.
- Given an oversized source set, when bounding occurs, then limits and truncation are recorded and ordering is stable.
- Given snapshot creation, when it completes, then no business task, workflow, approval, provider, or external side effect has been mutated.

### 7. Verification

- Add unit tests for normalization, materiality, freshness, deduplication, bounding, and data-gap behavior.
- Add contributor contract tests using authoritative existing services.
- Add integration tests for tenant isolation, partial contributor failure, cancellation, correlation, and persistence.
- Add authorization tests for snapshot diagnostics.
- Run focused cockpit, briefing, task, workflow, and capability regression tests plus affected builds.

### 8. Definition of done

The company has one bounded, explainable, company-scoped observation snapshot suitable for planning. It uses existing authoritative projections, handles gaps safely, produces no business side effects, and is fully tested and observable.

---

## Prompt 4 — Implement deterministic operating-plan validation and governance

### 1. Title and outcome

Implement backend plan validation that decides whether a proposed initiative or assignment is valid, requires review or approval, or must be denied before any plan can create work.

### 2. Current context

The repository already has `IAgentAssignmentGuard`, `BoundedCollaborationPolicy`, tool execution guardrails, responsibility policies, approval services, autonomy profiles, `AgentTaskCreationDedupeRecord`, workflow validation, and capability-specific high-impact policies. These controls operate at execution boundaries but there is no unified operating-plan validation pipeline.

Relevant current files include:

- `src/VirtualCompany.Application/Orchestration/BoundedCollaborationPolicy.cs`
- `src/VirtualCompany.Application/Orchestration/ResponsibilityPolicyContracts.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentGovernanceServices.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/ResponsibilityPolicyEvaluator.cs`
- `src/VirtualCompany.Domain/Entities/AgentTaskCreationDedupeRecord.cs`
- existing approval and agent assignment services

### 3. Dependencies

Prompts 1 through 3.

### 4. Implementation requirements

- Define focused Application policy contracts and composable validators for goal relevance, tenant ownership, duplicate work, agent capability/scope/status, workload/capacity, dependency validity, cycle limits, time and monetary budgets, action classification, autonomy, approval requirements, and completion evidence.
- Produce a structured decision per plan item containing allowed/denied/review-required outcome, stable reason code, plain-English explanation, approval requirement, authoritative evidence, validator version, and evaluated time.
- Implement deterministic duplicate-work identity using company, goal, normalized outcome/action, target, and version. Check proposed, approved, active, and recently completed work without treating random correlation IDs as business identity.
- Reuse `IAgentAssignmentGuard`, agent runtime profile resolution, capability catalog, responsibility policy, and bounded collaboration limits. Do not duplicate their authoritative rules.
- Validate agent workload with deterministic configured thresholds and current active assignments; distinguish temporary capacity blocks from permanent capability denials.
- Validate the dependency graph, completion evidence, target dates, and budget totals across the full plan, not only per item.
- Classify action types as read, recommend, internal mutation, or external execute using the existing tool/action metadata conventions. Unknown action types are denied safely.
- Persist validation results against the immutable plan version from Prompt 2 and invalidate prior validation when a revision is created or relevant configuration version changes.
- Ensure validation itself creates no tasks, approvals, workflows, or external effects.
- Add audit evidence for final validation outcome and high-impact denials without recording hidden prompts or sensitive payloads.

### 5. Constraints and preservation rules

- Follow all mandatory architecture and production instructions.
- Do not introduce one generic policy engine that replaces domain-specific authoritative policies. The operating validator coordinates them and preserves their decisions.
- UI or model output cannot override backend policy.
- Review-required is not equivalent to allowed execution.
- Unknown capability, tool, scope, action, owner, evidence, or policy state must fail safely with an operator-visible reason.
- Preserve current task assignment, tool guardrail, approval, and collaboration behavior.

### 6. Acceptance criteria

- Given a valid recommendation-only plan with qualified agents and evidence, when validation runs, then it is recorded as eligible for human review with complete policy evidence.
- Given duplicate active work, when an equivalent item is proposed, then it is denied or merged according to an explicit deterministic rule and no new task is created.
- Given an agent without required capability, scope, active status, or capacity, when assigned, then validation returns a stable actionable reason.
- Given a dependency cycle, exceeded cycle budget, or unknown execute action, when validated, then the full plan cannot be committed.
- Given a sensitive or external action, when validation runs, then it requires the applicable authoritative policy and approval regardless of model confidence or autonomy configuration.
- Given a plan revision or changed operating configuration version, when prior validation is inspected, then it is not treated as current authorization.

### 7. Verification

- Add focused policy unit tests for every validator and combined decision precedence.
- Add duplicate identity, normalization, time-window, concurrency, and race-condition tests.
- Add tenant-isolation and foreign-reference tests.
- Add regression tests proving existing assignment, collaboration, approval, and tool policies remain authoritative.
- Add persistence tests for validation versions and invalidation.
- Build Application, Operations, API, and affected test projects.

### 8. Definition of done

Every operating-plan item receives a deterministic, persisted, explainable governance result before mutation. Unknown and high-risk states fail safely, existing domain policies remain authoritative, and no validation path can execute work.

---

## Prompt 5 — Deliver a recommendation-only executive operating cycle

### 1. Title and outcome

Implement a manually requested, recommendation-only operating cycle that observes company state, asks the designated coordinator agent for a structured plan, validates it, and saves it for human review without creating tasks or executing tools.

### 2. Current context

`SharedAgentReasoningGateway` provides the shared OpenAI-backed reasoning path and persists `AgentOrchestrationRun`. `StructuredPromptBuilder`, grounded context, named agent profiles, source references, and audit services already exist. Current `AgentPlanningService` turns a caller-supplied objective into a single-agent draft plan, while `RoleAgentCadenceBackgroundService` produces hard-coded department briefs. No service currently combines company goals and an authoritative snapshot into a company-wide operating plan.

Relevant current files include:

- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentReasoningGateway.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentAiCapabilityServices.cs`
- `src/VirtualCompany.Application/Orchestration/StructuredPromptBuilder.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/RoleAgentCadenceBackgroundService.cs`
- artifacts implemented by Prompts 1 through 4

### 3. Dependencies

Prompts 1 through 4.

### 4. Implementation requirements

- Define `ICompanyOperatingCycleService` and focused request/result contracts for a manual recommendation cycle.
- Require an authorized company manager, non-paused operating configuration, an active coordinator agent, and at least one active goal.
- Create and persist the cycle before observation. Advance explicit states through observing, planning, validating, awaiting review, completed, or safely failed.
- Build the snapshot using Prompt 3 and construct bounded coordinator instructions containing only relevant goals, constraints, signals, recent decisions, agent roster/capabilities, policies, and source schemas.
- Add a schema-versioned structured planning response containing proposed initiatives, assignments, collaborators, dependencies, expected evidence, expected metric impact, risk, uncertainty, and requested action class.
- Use the existing shared reasoning gateway. Extend its structured response support in a reusable way if needed; do not add a direct provider call or a second orchestration stack.
- Strictly validate model JSON/schema, limits, referenced IDs, and source references before translating it into Prompt 2 records. Malformed, contradictory, oversized, or ungrounded output must create a safe failed/review state, not partial plan records.
- Run Prompt 4 validation and persist the immutable proposal, evidence, validation results, rationale summary, uncertainty, coordinator run ID, prompt/schema version, and correlation chain.
- Enforce recommendation-only semantics: this service must not create tasks, subtasks, workflows, approvals, tool invocations, outbox messages, handoffs, or provider effects.
- Add a transport-only endpoint to request a manual cycle and endpoints to read its result. Use an idempotency key to prevent duplicate manual submissions.
- Record audit events and technical telemetry for request, snapshot, model outcome, validation, safe failure, and completion.
- If AI is not configured or is unavailable, return and persist a clear operator-visible unavailable/failed state. Do not generate a fake plan.

### 5. Constraints and preservation rules

- Follow the mandatory production and architecture files; read `architecture-inst.md` if present.
- The coordinator is an existing named agent configuration, not a new special LLM runtime.
- Do not modify `RoleAgentCadenceBackgroundService` into the company orchestrator in this prompt.
- Model confidence cannot authorize mutation.
- A successful cycle ends at `awaiting review`; it does not commit work.
- Preserve bounded prompt context and do not expose hidden instructions or inaccessible company data.

### 6. Acceptance criteria

- Given an authorized manager, active goals, valid configuration, and available AI, when a manual cycle is requested, then a snapshot, structured plan, source evidence, and policy validation are persisted and returned in `awaiting review` state.
- Given the same idempotency key, when the manual request is repeated, then the existing cycle is returned and the model is not invoked again.
- Given invalid or oversized model output, when processing completes, then the cycle records a safe failure or review-required reason and creates no partial business work.
- Given missing goals, paused operation, an invalid coordinator, or unavailable AI configuration, when requested, then the result explains what must be fixed and no plan is fabricated.
- Given a completed recommendation cycle, when the task, workflow, approval, outbox, and tool-execution stores are inspected, then none were created by that cycle.
- Given another company, when it attempts to access the cycle, snapshot, or plan, then access is denied without information leakage.

### 7. Verification

- Add unit tests for prompt/request construction, schema parsing, bounding, reference validation, and translation.
- Add integration tests with a controlled reasoning gateway double for success, malformed output, cancellation, timeout, unavailable provider, and idempotent replay.
- Add API authorization and tenant-isolation tests.
- Add negative assertions proving recommendation cycles create no tasks, workflows, approvals, tools, outbox messages, or external effects.
- Verify audit/correlation evidence and safe logging.
- Build and run affected Application, Operations, API, and test projects.

### 8. Definition of done

A manager can request a real, grounded, company-wide operating recommendation that is durably recorded and validated for review. Failure states are explicit, no work executes, and there are no fake plans or direct LLM bypasses.

---

## Prompt 6 — Add the executive operating-plan review workspace

### 1. Title and outcome

Implement a production Blazor workspace where managers can see company goals, request a recommendation cycle, understand the proposed plan and evidence, and identify policy issues before any work is approved.

### 2. Current context

The production information architecture uses Overview, Agent team, department modules, Work, History, and Settings. Executive cockpit, Today Focus, agent cards, approvals, workflows, activity, and list/detail patterns already exist. Web API clients are registered through `AddVirtualCompanyApiClients` and tenant-owned requests use `ICompanyApiTransport`. No company operating-plan review page exists.

Relevant current files and areas include:

- `src/VirtualCompany.Web/Pages`
- `src/VirtualCompany.Web/Components`
- `src/VirtualCompany.Web/Services/CompanyApiTransport.cs`
- `src/VirtualCompany.Web/Services/ExecutiveCockpitApiClient.cs`
- `src/VirtualCompany.Web/Services/WebApiClientRegistration.cs`
- `docs/ui-route-inventory.md`
- APIs and read models from Prompts 1 through 5

### 3. Dependencies

Prompts 1 through 5.

### 4. Implementation requirements

- Read and follow `ui-instructions.md` and `docs/design.md` in addition to all mandatory architecture files.
- Before UI implementation, write an explicit reference prompt, generate an approved reference screenshot, and save it as `docs/design/references/company-operating-plan-reference.png`. Compare the rendered page with it and refine the implementation.
- Inspect `docs/ui-route-inventory.md` and current navigation before selecting the route. Add the workspace to the appropriate existing daily-work or executive surface; do not restore retired primary navigation or create a technical admin destination.
- Create a typed `CompanyOrchestrationApiClient` or equally focused client registered through `AddVirtualCompanyApiClients` and using `ICompanyApiTransport`.
- Show active company goals, operating status, coordinator identity, current/recent cycle, proposal state, data gaps, and the requested manual-cycle action.
- Present plan initiatives in priority order with owner agent, desired outcome, target time, collaboration, dependencies, expected evidence, risk, and validation status.
- Include a detail panel with “What this means,” “Why this was proposed,” “Data used,” uncertainty, policy/approval explanation, and related records.
- Clearly distinguish recommendation, needs review, denied, failed, blocked, and unavailable states in plain English. Never display enum tokens, schema names, policy object names, internal trigger terms, raw IDs, hidden prompts, or provider errors.
- Show safe empty/loading/error/retry states and prevent duplicate request submission while an idempotent request is in flight.
- Respect member versus manager authorization in visible actions, while relying on backend authorization as authoritative.
- Do not include plan approval or commit actions in this prompt; the page is review-only until Prompt 7.
- Add localization resources following existing patterns where the surrounding surface is localized.

### 5. Constraints and preservation rules

- The screenshot-first workflow is mandatory and must occur before implementation.
- Reuse existing design tokens, cards, badges, page headers, agent identity, list/detail behavior, responsive patterns, and plain-English conventions.
- Do not assemble cross-module transactional data in Razor components; consume query read models.
- Do not add mock data or silent offline substitutes.
- Do not expose controls that imply work has been approved or started.
- Preserve current app shell, route compatibility, authentication forwarding, cancellation, and company headers.

### 6. Acceptance criteria

- Given active goals and a completed recommendation cycle, when a manager opens the workspace, then they can understand what changed, what is proposed, who would own it, why it matters, and what evidence and policy decisions support it.
- Given a company member without manager rights, when they open the page, then they can view authorized information but cannot request a new cycle.
- Given data gaps or AI unavailability, when the page loads, then it shows actionable plain-English guidance rather than fabricated healthy state or raw provider errors.
- Given an invalid or denied plan item, when selected, then the detail panel explains the reason and evidence without exposing internal identifiers.
- Given no goals or cycles, when the page loads, then it shows a purposeful empty state and authorized next action.
- Given desktop and narrow viewport sizes, when rendered, then the page remains usable and visually close to the approved reference.

### 7. Verification

- Add typed API-client tests for routes, company context, correlation, cancellation, validation problems, unauthorized, not-found, and safe error mapping.
- Add Blazor component/page tests for authorization states, cycle submission, loading, empty, failed, data-gap, and plan-detail rendering.
- Run the mandatory visual comparison against the generated reference and refine discrepancies.
- Perform focused browser verification only if required; follow the repository's bounded Web startup rules.
- Build `VirtualCompany.Web`, run `VirtualCompany.Web.Tests`, and run relevant Web contract tests.

### 8. Definition of done

Managers have a polished, responsive, review-only operating workspace grounded in real APIs and current design patterns. The reference screenshot is saved, visual QA is complete, all states are handled, and no approval or execution authority is implied prematurely.

---

## Prompt 7 — Approve and atomically commit operating plans into durable work

### 1. Title and outcome

Implement reviewed plan approval and atomic commit so an authorized, currently valid plan can create durable initiatives and assigned tasks exactly once, while stale, rejected, or policy-invalid plans cannot mutate work.

### 2. Current context

The solution has `ICompanyTaskService`, task assignment guards, approval request/decision chains, approval tasks, workflow instances, audit, correlation, and task-creation dedupe records. `AgentPlanningService.CommitAsync` currently creates sequential tasks from one agent's reviewed claims, but company plans require cross-agent assignments, dependencies, stronger validation, and atomic idempotency.

Relevant current files include:

- `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyTaskService.cs`
- existing company approval services under `VirtualCompany.Infrastructure.Operations/Companies`
- `src/VirtualCompany.Domain/Entities/AgentTaskCreationDedupeRecord.cs`
- `src/VirtualCompany.Api/Controllers/ApprovalsController.cs`
- `src/VirtualCompany.Api/Controllers/TasksController.cs`
- artifacts and validators from Prompts 1 through 5

### 3. Dependencies

Prompts 1 through 6.

### 4. Implementation requirements

- Add a focused Application command boundary for submitting a plan for approval, approving/rejecting/requesting changes, and committing an approved plan.
- Reuse the existing approval subsystem. Do not invent an operating-plan-only approval table when existing approval requests/steps can represent the decision and link to the plan.
- Require company-manager authorization and any configured approval chain. Persist request, decision, expiration, rejection, cancellation, and requested-change states.
- Immediately before commit, recheck plan version, goal state, operating configuration version, validation freshness, agent assignment eligibility, duplicate identity, budgets, and approval validity.
- Commit initiatives, dependencies, and `WorkTask` records in a clear transaction. Use existing task services/command boundaries or a correctly owned application service; do not bypass authoritative assignment validation.
- Map each plan item to a stable task type, plain-English title/description, owner, priority, due date, parent/dependency linkage, expected evidence, plan/initiative/cycle references, rationale, confidence, and correlation ID.
- Use stable company/business idempotency keys so repeated approval callbacks, API retries, or concurrent commits return the same committed result without duplicate initiatives or tasks.
- A commit may create internal work only. It must not execute agents, start external effects, or bypass workflows in this prompt.
- Record audit events linking requester, approvers, plan version, validation evidence, committed initiatives/tasks, outcome, and correlation ID.
- Extend the Prompt 6 UI with approve, reject, and ask-for-changes actions. Complete screenshot-first work if this constitutes a significant redesign or modal; otherwise document why the existing reference and component pattern remain sufficient and visually verify the change.

### 5. Constraints and preservation rules

- Follow all mandatory architecture, production, and UI instructions.
- Approval in the UI is not authoritative; backend validation and approval state are authoritative.
- Never commit a superseded, expired, rejected, cancelled, unvalidated, or materially stale plan.
- Transaction failure must leave no partial initiatives, dependencies, tasks, or false success audit state.
- Do not execute tool calls or external side effects during the request transaction.
- Preserve existing task routes and behavior.

### 6. Acceptance criteria

- Given a current valid plan and completed approval chain, when commit is requested, then initiatives and assigned tasks are created once and linked to the plan, cycle, goals, evidence, and correlation chain.
- Given the same commit request repeated or raced concurrently, when processing completes, then all callers observe one committed result and no duplicate tasks exist.
- Given a changed goal, coordinator configuration, agent status, budget, or plan revision after approval, when commit is attempted, then it is blocked with an actionable stale-validation reason.
- Given rejection, cancellation, expiration, or requested changes, when commit is attempted, then no work is created.
- Given a transaction or validation failure, when stores are inspected, then no partial initiative/task graph or successful audit record exists.
- Given an approved commit, when inspected, then no agent execution, workflow advancement, outbox message, or external effect was started by the commit itself.

### 7. Verification

- Add approval-chain tests for approve, reject, changes, cancellation, expiration, and authorization.
- Add transaction, idempotency, duplicate, stale-validation, concurrency, and rollback tests.
- Add tenant-isolation tests for foreign goals, plans, agents, tasks, and approvals.
- Add API and Web tests for actions, conflict/error presentation, and repeated submission.
- Run existing task, approval, assignment, audit, and Web regression suites and affected builds.

### 8. Definition of done

An authorized approved plan becomes a durable, correctly assigned work graph exactly once. Stale or invalid approval cannot mutate state, partial commits are impossible, the UI explains outcomes clearly, and execution remains a separate controlled step.

---

## Prompt 8 — Dispatch individual and bounded multi-agent work

### 1. Title and outcome

Implement controlled dispatch of committed initiatives so eligible tasks run through the existing single-agent orchestrator and validated collaboration items form bounded manager-worker teams without requiring a user to manually specify every worker.

### 2. Current context

`ISingleAgentOrchestrationService` executes assigned work with grounded context, tools, policy, audit, and task artifacts. `IMultiAgentCoordinator` creates parent/worker tasks and consolidates contributions, but currently requires an explicit `Workers` plan and disallows recursive delegation. `GenerateWorkerPlanAsync` exists but is unused and currently selects agents too simplistically. `AgentHandoff` exists as a durable concept. Trigger dispatch already demonstrates background-safe invocation of single-agent orchestration.

Relevant current files include:

- `src/VirtualCompany.Infrastructure.Operations/Companies/SingleAgentOrchestrationService.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/MultiAgentCoordinator.cs`
- `src/VirtualCompany.Application/Orchestration/MultiAgentCollaborationContracts.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/TriggerExecutionInfrastructure.cs`
- `src/VirtualCompany.Domain/Entities/AgentAiEntities.cs`

### 3. Dependencies

Prompts 1 through 7.

### 4. Implementation requirements

- Add a durable operating-work dispatcher that claims committed, eligible initiatives/tasks in bounded batches through background execution. Request handlers may enqueue dispatch but must not synchronously run long-lived agent work.
- Dispatch single-owner work through `ISingleAgentOrchestrationService` using persisted task, plan, initiative, actor, intent, expected evidence, and correlation context.
- Translate an already validated collaboration proposal into the explicit `StartMultiAgentCollaborationCommand`. Automatic team formation must occur before `IMultiAgentCoordinator.ExecuteAsync`, based on the approved plan, agent capabilities, scope, capacity, role, and policy—not by choosing the first agents alphabetically.
- Replace, remove, or correctly integrate the unused simplistic `GenerateWorkerPlanAsync` path so there is one intentional team-planning behavior.
- Keep existing bounds on workers, depth, steps, runtime, self-assignment, duplicate workers, and recursive delegation. Worker agents cannot expand the team.
- Support explicit collaboration roles and patterns: owner, contributor, reviewer/challenger, parallel contribution, and sequential handoff. Persist each expected artifact and source task.
- Use durable idempotency/claim records so duplicate queue delivery or concurrent workers cannot execute the same initiative/task twice.
- Classify retryable, permanent, blocked, policy-denied, approval-required, and ambiguous outcomes. Persist attempts, next retry, safe failure, and operator-visible recovery state.
- Do not auto-run external execute actions. If a task requires approval or external effect, dispatch must stop at the appropriate workflow/approval boundary.
- Update initiative/task progress from orchestration results and link contributions, rationale, confidence, source references, tool executions, and audit evidence.
- Add query/read projections for the collaboration graph and dispatch state.

### 5. Constraints and preservation rules

- Follow mandatory production and architecture instructions.
- Use the shared orchestration subsystem; do not create department-specific LLM runtimes.
- Background workers must create scopes, reapply company context, use bounded batches, and be safe under duplicates and concurrency.
- Do not let the model assign agents outside validated capability, scope, status, capacity, or company boundaries.
- Preserve current direct API manager-worker collaboration behavior for compatibility unless a deliberate versioned change is required.
- External side effects remain behind policy, approval, workflow, and outbox boundaries.

### 6. Acceptance criteria

- Given a committed eligible single-agent task, when dispatch runs, then it executes once through the shared single-agent orchestration and records linked results.
- Given an approved collaboration plan, when dispatch runs, then the validated workers receive bounded subtasks and the coordinator consolidates their contributions.
- Given a worker without current capability, scope, capacity, or active status, when dispatch rechecks eligibility, then execution is blocked and operator-visible rather than reassigned silently.
- Given duplicate delivery or concurrent dispatchers, when they process the same work, then only one authoritative execution occurs.
- Given a worker attempt to create unplanned recursive collaboration, when evaluated, then it is denied by existing collaboration boundaries.
- Given an external or sensitive action, when internal dispatch reaches it, then execution stops at the required approval/workflow boundary.

### 7. Verification

- Add single-agent and multi-agent dispatch integration tests, including worker selection, role translation, consolidation, sequential handoff, and reviewer patterns.
- Add idempotency, concurrency, lease/claim, retry, cancellation, timeout, and permanent-failure tests.
- Add tenant-isolation and eligibility recheck tests.
- Run existing orchestration, assignment, trigger, task, approval, and audit tests.
- Verify no external effect is emitted from an unapproved internal dispatch.
- Build Operations, API, and relevant test projects.

### 8. Definition of done

Committed internal work is dispatched durably and exactly once through existing orchestration. Multi-agent teams are formed from approved structured plans, collaboration remains bounded, outcomes are explainable, and sensitive execution boundaries cannot be bypassed.

---

## Prompt 9 — Add scheduled and event-driven company operating cycles

### 1. Title and outcome

Implement reliable automatic operating-cycle requests from configured schedules and material company events, with leases, cooldowns, deduplication, bounded budgets, and an emergency pause so the company can operate proactively without entering uncontrolled AI loops.

### 2. Current context

The solution already has hosted background services for role cadence, scheduled agent triggers, trigger evaluation, workflow scheduling/progression, briefings, knowledge indexing, outbox dispatch, and memory expiry. Background execution records, company execution scopes, retry classification, idempotent trigger windows, and observability patterns exist. The current `RoleAgentCadenceBackgroundService` hard-codes departments and manager-brief instructions rather than requesting a company operating cycle.

Relevant current files include:

- `src/VirtualCompany.Infrastructure.Operations/Companies/RoleAgentCadenceBackgroundService.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/AgentScheduledTriggerServices.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/TriggerExecutionInfrastructure.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/WorkflowSchedulerInfrastructure.cs`
- `src/VirtualCompany.Infrastructure.Platform` background execution and tenancy services

### 3. Dependencies

Prompts 1 through 8.

### 4. Implementation requirements

- Add a durable `OperatingEvent` and cycle-request queue/outbox contract with source, source identity/version, observed time, materiality, affected goals, safe payload, correlation ID, and stable deduplication identity.
- Add a company operating scheduler using company timezone/cadence configuration from Prompt 1. Persist scheduling windows so restarts and multiple hosts do not duplicate cycles.
- Add event producers at authoritative completion or exception boundaries for material task, workflow, approval, business-signal, and integration outcomes. Producers enqueue durable events; they must not invoke the planner inline.
- Implement deterministic event materiality, coalescing, cooldown, and self-event suppression. Administrative writes produced by a cycle must not immediately cause a recursive cycle unless an actual material outcome changes.
- Implement one renewable lease per company cycle, bounded claim batches, lease expiry/recovery, and safe multi-host concurrency.
- Enforce configured minimum interval, maximum cycles per window, model/tool/time budget, and company pause/emergency stop before requesting reasoning.
- Route automatic requests through the same Prompt 5 cycle service and Prompt 4 policies. Do not create a separate automatic planning path.
- Keep automatic behavior in recommendation-only mode initially unless Prompt 11 later authorizes low-risk commit/dispatch.
- Reconcile `RoleAgentCadenceBackgroundService`: either have it contribute bounded department signals/briefs to the company cycle or retain it as a separate compatible feature with documented non-duplication. Remove hard-coded duplication only when behavior and tests are preserved.
- Persist technical and business audit evidence for request, suppression, deduplication, lease, budget denial, retry, dead-letter, and completion.
- Provide operator-visible read models for next scheduled cycle, recent automatic requests, suppressed events, leases, budgets, and failures.

### 5. Constraints and preservation rules

- Follow all mandatory production and architecture documents.
- Long-running work belongs in background services registered once through Operations module registration.
- Hosted services create scopes for scoped work and explicitly establish company execution context.
- Unchanged state is expected and must not generate repeated plans.
- Never use a random retry ID as event or cycle business identity.
- Pause and emergency stop must be authoritative in backend execution, not only UI state.

### 6. Acceptance criteria

- Given a configured daily cadence, when its due window arrives across two hosts, then one company cycle request is created.
- Given repeated copies of the same material event, when processed, then they coalesce into one eligible request and retain source evidence.
- Given only cycle-generated administrative changes, when events are evaluated, then no recursive cycle is started.
- Given an active lease, cooldown, exhausted budget, paused company, or emergency stop, when a request is evaluated, then it is safely deferred or denied with an operator-visible reason.
- Given a crashed worker with an expired lease, when another worker resumes, then processing can recover without duplicating a completed cycle.
- Given automatic mode before Prompt 11 autonomy is enabled, when a cycle completes, then it produces only a recommendation for review.

### 7. Verification

- Add deterministic time-provider tests for timezone schedules, daylight-saving transitions, windows, cooldowns, and maximum cycles.
- Add multi-host/concurrency tests for leases, duplicate events, expired claims, and idempotent recovery.
- Add materiality, coalescing, self-suppression, pause, emergency stop, and budget tests.
- Add background retry/dead-letter and tenant-context isolation tests.
- Run existing scheduled-trigger, role-cadence, workflow, outbox, and background-execution regression tests.
- Build Operations, Platform, API, and affected test projects.

### 8. Definition of done

Companies can request recommendation cycles automatically from schedules and material events without duplicates, recursion, budget runaway, or cross-company leakage. Operations can pause, inspect, and recover the system safely.

---

## Prompt 10 — Implement outcome review and evidence-driven replanning

### 1. Title and outcome

Implement initiative outcome review so completed, failed, or blocked work is compared with expected evidence and goal impact, producing a durable close, continue, revise, reassign, pause, or escalate decision instead of allowing plans to drift indefinitely.

### 2. Current context

Tasks, workflows, orchestration results, tool executions, approvals, audit events, business signals, and capability records contain completion and failure evidence. Operating initiatives from Prompt 2 define expected evidence and metric impact. Prompt 9 can enqueue material operating events, but no company-level service currently evaluates actual outcomes against a plan and decides whether replanning is warranted.

Relevant current areas include:

- task and workflow query services
- orchestration task artifacts and source references
- approval and execution exception records
- business signals and snapshots from Prompt 3
- operating records and events from Prompts 2 and 9

### 3. Dependencies

Prompts 1 through 9.

### 4. Implementation requirements

- Define a deterministic review-readiness policy using task/workflow terminal state, required artifacts, approvals, target dates, retry state, and expected evidence.
- Assemble a bounded review packet from authoritative linked results, source references, before/after signals, and unresolved gaps.
- Compare expected completion evidence and metric direction with actual evidence. Deterministic facts and policy outcomes remain authoritative; AI may summarize ambiguity and propose the next bounded action.
- Use the shared reasoning gateway only when interpretation is needed. Schema-version the review response and validate it strictly.
- Persist an immutable `OperatingReview` linked to plan version, initiative, evidence, reviewer/coordinator run, confidence, uncertainty, and one allowed outcome: close successful, continue, revise, reassign, request evidence, escalate, pause, or stop.
- Closing or continuing may update initiative state through explicit commands. Revision must create a new plan version; it must not mutate the approved plan in place.
- A proposed revision returns to Prompt 4 validation and the normal review/approval path. It cannot automatically bypass approval or execute external effects.
- Emit deduplicated operating events for material reviews and suppress no-op loops.
- Capture bounded learning in existing memory mechanisms only when it meets memory policy; do not store unlimited raw task transcripts.
- Add read APIs and extend the operating workspace to show expected versus actual outcome, evidence, review decision, and next required action. Follow screenshot-first instructions if the change is a major page/component redesign.

### 5. Constraints and preservation rules

- Follow all mandatory architecture, production, and applicable UI instructions.
- Do not declare success from model prose when required authoritative evidence is missing.
- Missing evidence must become `request evidence` or review-required state, not fabricated completion.
- Plan revisions are immutable new versions and re-enter validation.
- Do not retry permanent failures or ambiguous external outcomes blindly.
- Review events must be company-scoped, idempotent, and safe under repeated task/workflow notifications.

### 6. Acceptance criteria

- Given completed work with all required evidence and confirmed goal impact, when reviewed, then the initiative can close with linked before/after evidence.
- Given completed task status but missing required evidence, when reviewed, then success is not declared and an actionable evidence request is recorded.
- Given a failed or blocked dependency, when reviewed, then the outcome distinguishes retryable, permanent, approval-blocked, and external-unavailable states.
- Given a proposed revision, when accepted for further consideration, then a new plan version is created and must pass validation and approval normally.
- Given duplicate terminal events, when review runs, then only one authoritative review exists for the same initiative/evidence version.
- Given another company, when review evidence is assembled or queried, then no foreign data is accessible.

### 7. Verification

- Add review-readiness and outcome policy tests for every allowed outcome.
- Add evidence completeness, before/after comparison, missing-data, malformed AI response, and deterministic-precedence tests.
- Add idempotency, duplicate terminal event, concurrency, revision-versioning, and tenant-isolation tests.
- Add UI/API tests for expected-versus-actual presentation and next actions.
- Run task, workflow, orchestration, memory, audit, event, and operating-plan regressions.

### 8. Definition of done

Every material initiative reaches an explainable reviewed state based on authoritative evidence. Missing evidence and failure remain visible, revisions follow normal governance, and no model-generated summary can silently mark business outcomes successful.

---

## Prompt 11 — Enable governed low-risk autonomous organization and internal operation

### 1. Title and outcome

Implement graduated autonomy so explicitly configured companies can automatically commit and optionally execute validated low-risk internal work, while recommendation-only remains the default and every higher level has enforceable budgets, approvals, pause controls, and audit evidence.

### 2. Current context

Prompt 1 stores operating autonomy and budgets. Prompt 4 classifies actions and validates plans. Prompt 7 commits reviewed plans, Prompt 8 dispatches internal work, and Prompt 9 requests automatic cycles. Existing agent profiles also contain autonomy levels, tool permissions, data scopes, responsibility rules, and guardrails. No company-level policy currently combines these dimensions to authorize automatic plan commit or dispatch.

Relevant current files include:

- `src/VirtualCompany.Domain/Entities/AgentEntities.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/PersistedAgentRuntimeProfileResolver.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentGovernanceServices.cs`
- operating configuration and policy artifacts from earlier prompts

### 3. Dependencies

Prompts 1 through 10.

### 4. Implementation requirements

- Implement typed company autonomy levels: Recommend, Organize, OperateInternally, and ControlledExecution. This prompt enables only the first three; ControlledExecution remains denied until Prompt 12.
- Add an authoritative backend autonomy policy that combines company level, goal overrides, agent autonomy, capability/tool action class, plan validation, budgets, current pause/stop state, approval requirements, and configuration version.
- `Recommend` saves proposals only. `Organize` may automatically commit eligible internal tasks but cannot dispatch them. `OperateInternally` may commit and dispatch eligible read/recommend/low-risk internal actions.
- Unknown, internal mutation with unclear impact, sensitive, monetary, customer communication, provider write, or external execute actions must not auto-run under `OperateInternally`.
- Recheck autonomy and all relevant policies immediately before automatic commit and again before dispatch.
- Add per-cycle and rolling-window limits for tasks, initiatives, collaborations, model calls, tool calls, runtime, and configured monetary planning budget. Persist consumption atomically.
- Add automatic approval creation or review queues when an otherwise valid plan exceeds autonomy rather than silently dropping it.
- Add company manager APIs to change autonomy, limits, pause/resume, and emergency stop with optimistic concurrency and audit evidence.
- Extend the operating workspace/settings UI with plain-English autonomy levels, exact allowed behavior, current budget use, pending review, pause/resume, and emergency stop. Complete the screenshot-first workflow before any significant new settings page or modal.
- Add prominent confirmation and impact explanation when increasing autonomy. Do not use fear-based or technical language.

### 5. Constraints and preservation rules

- Follow all mandatory production, architecture, and UI documents.
- Recommendation-only is the default for new companies, missing configurations, new capabilities, unknown actions, and migration upgrades.
- Company autonomy cannot exceed the assigned agent's permissions or bypass domain policy and approval.
- `OperateInternally` must never send customer messages, move money, write to providers, or perform another external side effect.
- Pause and emergency stop are checked in backend workers and dispatchers, not only reflected in UI.
- Budget exhaustion creates an operator-visible state and must not be reset by retrying with a new correlation ID.

### 6. Acceptance criteria

- Given default or Recommend autonomy, when an automatic cycle produces a valid plan, then it waits for review and creates no tasks.
- Given Organize autonomy and an eligible internal plan, when validation completes, then tasks are committed exactly once but are not executed.
- Given OperateInternally autonomy and eligible read/recommend work, when validation completes, then it is committed and dispatched within limits.
- Given any external, sensitive, monetary, customer-communication, provider-write, unknown, or approval-required action, when automatic operation evaluates it, then it waits for review/approval and does not execute.
- Given exhausted budget, pause, emergency stop, stale configuration, or reduced agent autonomy, when commit or dispatch rechecks policy, then automatic work stops safely.
- Given an autonomy increase, when saved, then authorization, concurrency, explicit confirmation, before/after audit evidence, and plain-English effective behavior are present.

### 7. Verification

- Add policy matrix tests covering every company level, agent level, action class, approval state, budget state, and pause state.
- Add automatic commit/dispatch integration tests and negative external-effect assertions.
- Add budget atomicity, rolling-window, concurrency, replay, and configuration-version tests.
- Add authorization and tenant-isolation tests for autonomy and emergency controls.
- Add UI tests and visual QA for settings, confirmation, budget, pause, and stop states.
- Run all operating-cycle, validation, commit, dispatch, approval, tool-guardrail, and background-worker regressions.

### 8. Definition of done

Companies can explicitly opt into bounded automatic organization and low-risk internal operation. Defaults remain conservative, budgets and emergency controls are authoritative, external actions cannot run, and every autonomous decision is explainable and audited.

---

## Prompt 12 — Add controlled execution for already-supported external actions

### 1. Title and outcome

Enable the `ControlledExecution` autonomy level for explicitly allowlisted external actions that already have production provider adapters, authoritative domain policies, approval support, durable outbox dispatch, idempotency, retries, and reconciliation. Unsupported or incompletely governed effects remain denied.

### 2. Current context

The repository already has capability-owned external execution paths such as approved Support reply delivery and provider-backed Finance, Sales, mailbox, and calendar actions. Architecture rules require important external effects to use durable outbox/background dispatch, stable business idempotency, approval rechecks, bounded retries, and reconciliation for ambiguous outcomes. The shared tool execution system classifies actions and applies guardrails, but company-level operating plans do not yet have a controlled external execution allowlist and readiness contract.

Relevant current areas include:

- shared tool execution and policy services in Operations
- `CompanyOutboxInfrastructure.cs`
- Support reply delivery dispatcher and safety/approval services
- capability-owned Finance, Sales, Mailbox, and Support provider adapters and workers
- autonomy policy implemented by Prompt 11

### 3. Dependencies

Prompts 1 through 11.

### 4. Implementation requirements

- Define an Application-level external-action readiness contract. A capability can register an action only if it supplies stable action identity, target/version identity, action classification, authoritative eligibility policy, required roles/approval policy, outbox message contract, dispatcher ownership, retry classification, reconciliation behavior, and safe status query.
- Build an explicit allowlist from registered ready actions. Do not infer readiness from a tool name, prompt, or model request. Unknown and partially implemented actions remain denied.
- Extend the autonomy policy so ControlledExecution can request allowlisted actions only when company, goal, agent, tool, policy, budget, approval, and provider readiness all allow it.
- Recheck authorization, approval, policy, current target version, pause/stop, and idempotency immediately before enqueue and again where the existing dispatcher requires it.
- Enqueue through the owning capability's durable outbox/background boundary. Never call a provider directly from the operating orchestrator, controller, or request transaction.
- Derive idempotency from company, business action, target, and version. Persist attempt, provider reference, safe error, retry, reconciliation, and final outcome through existing capability-owned records.
- Route ambiguous provider outcomes to reconciliation and block blind retries. Permanent validation, authorization, or provider failures stop with operator-visible recovery guidance.
- Start with only actions that demonstrably satisfy the readiness contract in the existing production implementation. Do not create fake adapters or claim generic external automation support.
- Link external request and outcome back to goal, cycle, plan, initiative, decision, approval, task, orchestration/tool execution, outbox message, provider reference, review, audit, and correlation ID.
- Extend operating views to show what the agent wants to do, why approval is or was needed, current delivery/provider state, retry/reconciliation state, and safe operator actions. Use screenshot-first workflow for significant UI changes.
- Document each enabled action, its policy owner, approval behavior, outbox type, reconciliation path, and operational recovery procedure.

### 5. Constraints and preservation rules

- Follow all mandatory production, architecture, and UI instructions.
- Capability modules own provider adapters, external policies, dispatchers, and reconciliation. Operations coordinates contracts and must not absorb sibling capability implementation.
- Existing business-specific policies remain authoritative; do not replace them with a generic orchestration decision.
- Approval-backed execution rechecks approval immediately before effect.
- Never log credentials, tokens, sensitive provider payloads, or hidden prompts.
- ControlledExecution is opt-in and cannot enable an action that is absent from the readiness allowlist.
- Do not weaken existing manual or capability-specific external execution protections.

### 6. Acceptance criteria

- Given ControlledExecution and a fully registered, allowlisted, approved action, when the plan reaches execution, then one durable outbox request is created and the owning dispatcher handles it.
- Given Recommend, Organize, or OperateInternally autonomy, when the same action is proposed, then it waits for review/approval and no outbox request is emitted automatically.
- Given an unknown, incomplete, unapproved, stale, policy-denied, provider-unready, paused, or budget-exceeded action, when evaluated, then no provider call occurs and the reason is operator-visible.
- Given duplicate queue delivery or API retry, when execution completes, then the provider effect is not duplicated.
- Given an ambiguous provider result, when handled, then the action enters reconciliation and is neither marked successful nor blindly retried.
- Given final provider outcome, when reviewed, then the complete goal-to-provider correlation and safe audit evidence are available.

### 7. Verification

- Add readiness-contract tests proving incomplete action registrations cannot enter the allowlist.
- Add end-to-end tests for at least one existing fully governed action from plan decision through approval, outbox, dispatcher, provider test adapter, final state, and review.
- Add negative tests for every lower autonomy level, missing/expired approval, stale target, duplicate delivery, pause/stop, provider unavailability, permanent failure, retryable failure, and ambiguous outcome.
- Add tenant-isolation, authorization, idempotency, concurrency, reconciliation, and audit/correlation tests.
- Run capability-specific external action regression suites so existing manual paths remain unchanged.
- Complete applicable UI visual QA and build all affected projects.

### 8. Definition of done

ControlledExecution safely orchestrates only external actions that already possess complete production governance and provider infrastructure. All other actions remain denied, side effects are durable and idempotent, ambiguous outcomes reconcile safely, and operators can understand and control every state.

---

## Completion state after all prompts

After Prompt 12, Virtual Company should have a governed operating loop that:

1. Stores authoritative company goals and operating limits.
2. Observes bounded, source-backed company state.
3. Produces structured plans through the existing coordinator-agent runtime.
4. Validates every plan deterministically.
5. Supports human review and approval.
6. Commits durable initiatives and work exactly once.
7. Dispatches individual and bounded collaborative agent work.
8. Reacts safely to schedules and material business events.
9. Reviews actual outcomes and replans through versioned governance.
10. Supports explicitly configured low-risk autonomy.
11. Executes only allowlisted, fully governed external actions through existing approval, outbox, provider, and reconciliation boundaries.

The completed system must remain a multi-tenant modular monolith using the existing shared AI orchestration subsystem. Hard-coded domain policies and provider capabilities remain authoritative tools; the company orchestrator decides what deserves attention, which outcome to pursue, who should own the work, how collaboration should be structured, and when results require review, replanning, escalation, pause, or closure.
