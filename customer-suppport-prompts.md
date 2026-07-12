# Customer Support Agent Implementation Prompts

These prompts are ordered by dependency within each area. When implementing the complete pack, use this area order: refund and finance handoff, support workspace UI, configurable SLA policies, resolution learning and memory, AI triage and replies, then mailbox threading and routing. Within an area, execute prompts 1 through 5 in order.

## Instructions shared by every prompt

The following instructions are part of every prompt in this document:

- Implement production-ready behavior, not scaffolding, mock production data, or a proof of concept.
- Read and follow `AGENTS.md` and `production-implementation.md`. For backend, data, workflow, agent, approval, integration, and orchestration changes, also read and follow `architecture-inst.md` and `/docs/architecture-rules.md`. For UI work, also read and follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements when applicable.
- Inspect the repository before editing and use existing module boundaries, application services, policies, outbox/workers, provider adapters, authorization, audit, observability, and UI patterns.
- Preserve tenant isolation and server-side authorization. Do not trust client-supplied company, user, agent, approval, mailbox, finance, or provider identifiers without tenant-scoped validation.
- Preserve local SQL Server and Docker SQL Server compatibility. Add an EF Core migration only when the persisted model changes, update the model snapshot, and document equivalent local and Docker migration/restore behavior.
- Keep external side effects idempotent and recoverable. Apply policy and approval checks before execution, use reliable background/outbox processing where appropriate, and expose safe failure states without leaking secrets or raw provider payloads.
- Add focused automated coverage and run relevant builds and tests. For UI work, verify the actual rendered experience at desktop and mobile sizes and compare significant new UI against its saved reference screenshot.
- Do not stop with in-scope TODOs, disconnected endpoints, unused persisted state, or UI controls backed by placeholder behavior. Preserve unrelated user changes in the worktree.

## Area 1: AI Triage and Replies

### Prompt 1.1: Structured AI support triage

**Title and outcome**

Replace keyword-only support triage with structured AI-assisted triage so Ben can classify cases consistently while retaining a dependable fallback.

**Current context**

`SupportTriageService` currently derives category, priority, sentiment, and risk from hard-coded keyword checks. Support cases, messages, context resolution, audit writing, named agents, and the shared AI orchestration subsystem already exist.

**Dependencies**

None.

**Implementation requirements**

- Route inference through the existing shared AI orchestration abstraction; do not call an LLM provider directly from the Support module.
- Resolve the company and configured support agent, enforce its data scopes and permissions, and provide only the current case, tenant-scoped message history, and resolved customer context.
- Require a versioned structured result containing category, priority, sentiment, VIP/churn/SLA risk, confidence, suggested next action, and concise rationale. Validate every value before applying it.
- Retain deterministic triage as an explicit fallback for provider unavailability, timeout, malformed output, or policy rejection, and record which path produced the result.
- Persist model/orchestration metadata, rationale, data sources, and an audit event without storing hidden reasoning or secrets. Add configuration and operational metrics for success, fallback, latency, and invalid output.

**Constraints and preservation rules**

Preserve existing API contracts where practical, existing case lifecycle rules, manual retriage, tenant filtering, and plain-English UI labels. AI output may recommend but must not execute sensitive actions or bypass approvals.

**Acceptance criteria**

- Given a tenant-scoped billing case, when triage succeeds, then a validated structured result updates only that tenant's case and identifies the AI path and sources used.
- Given malformed, unavailable, or disallowed AI output, when triage runs, then deterministic fallback completes safely and the failure is observable.
- Given an unsupported enum value or cross-tenant reference, when validation runs, then no case mutation occurs from that value.

**Verification**

Add unit tests for schema validation and fallback; integration tests for orchestration, audit, authorization, and tenant isolation; and build the affected projects.

**Definition of done**

Production triage uses shared orchestration with validated output, safe fallback, auditability, metrics, and no remaining direct keyword-only primary path.

### Prompt 1.2: Grounded AI reply generation

**Title and outcome**

Generate useful, source-backed customer reply drafts instead of fixed templates while keeping humans in control of sending.

**Current context**

`SupportReplyDraftService` currently builds a deterministic template from `SupportKnowledgeContextProvider`. Knowledge chunks, customer memory, similar resolved cases, linked case context, reply-draft persistence, approval, and mailbox sending already exist.

**Dependencies**

Prompt 1.1.

**Implementation requirements**

- Generate drafts through shared AI orchestration using Ben's configured role, tone, permissions, case context, approved knowledge, customer memory, and permitted linked business records.
- Require structured output containing subject suggestion, reply body, confidence, answerability, source-reference identifiers, claims requiring verification, and rationale summary.
- Resolve source identifiers server-side and reject citations to data not supplied in the scoped context. Persist source snapshots sufficient to explain the draft later.
- Preserve deterministic low-confidence fallback and automatic knowledge-gap creation when approved knowledge is insufficient.
- Record generation version, agent/model metadata, latency, outcome, and audit data. Never include secrets, hidden prompts, internal enum names, or unsupported financial assertions in customer text.

**Constraints and preservation rules**

Do not send automatically in this prompt. Preserve draft editing, approval, rejection, send idempotency, mailbox behavior, and existing case state transitions.

**Acceptance criteria**

- Given adequate approved knowledge, when a draft is generated, then every material factual claim is traceable to a persisted permitted source.
- Given insufficient grounding, when generation completes, then the draft is clearly review-required and a deduplicated knowledge gap is recorded.
- Given attempted cross-tenant or fabricated source references, then generation is rejected or sanitized without exposing data.

**Verification**

Test structured parsing, source validation, low-knowledge fallback, prompt-injection resistance, tenant isolation, audit persistence, and existing draft lifecycle behavior.

**Definition of done**

Draft generation is grounded, explainable, provider-failure tolerant, integrated with existing drafts, and contains no fixed-template-only primary implementation.

### Prompt 1.3: Reply safety and claim validation

**Title and outcome**

Add an enforceable support-reply safety gate that prevents unsupported promises, sensitive actions, and unsafe customer communication.

**Current context**

Drafts have confidence and answerability checks, but there is no comprehensive claim-level validation before approval or sending. Billing, refunds, access issues, and provider-backed replies can create financial, privacy, and trust risk.

**Dependencies**

Prompts 1.1 and 1.2.

**Implementation requirements**

- Introduce a Support application policy service that evaluates generated and edited drafts before approval and again before sending.
- Detect unsupported facts, refund or credit promises, payment assertions, legal commitments, credential requests, account-security instructions, abusive content, personal-data leakage, and prompt-injection residue.
- Return structured allow, require-review, or block decisions with stable reason codes and plain-English operator explanations.
- Enforce decisions server-side; do not rely on the UI or prompt instructions. Persist policy version, decision, evaluated source set, and audit event.
- Allow authorized human edits and reevaluation, but never permit an override to bypass hard security or tenant-isolation rules.

**Constraints and preservation rules**

Use existing policy and approval patterns. Do not expose internal policy text to customers, store hidden chain-of-thought, or silently remove material content without showing the operator what changed.

**Acceptance criteria**

- Unsupported refund promises and unverified billing claims cannot be approved or sent.
- Low-risk, fully grounded replies remain approvable without unnecessary blockers.
- Edited drafts are reevaluated, and stale prior safety decisions cannot authorize changed content.

**Verification**

Add policy unit tests, API integration tests for approve/send enforcement, adversarial prompt-injection cases, authorization tests, audit assertions, and regression tests for valid replies.

**Definition of done**

Every support reply passes a current server-side safety decision before approval and send, with actionable operator feedback and complete auditability.

### Prompt 1.4: End-to-end support agent orchestration

**Title and outcome**

Coordinate triage, context retrieval, drafting, policy evaluation, approval routing, and permitted tools as one bounded and observable Ben support workflow.

**Current context**

The repository has separate support services and a support tool action service, but the agent does not yet execute a cohesive shared-orchestration plan with explicit step and failure state.

**Dependencies**

Prompts 1.1 through 1.3, plus completed finance-handoff safeguards before enabling refund tools.

**Implementation requirements**

- Define an application-level support orchestration command and persisted execution record using existing orchestration/task/workflow conventions.
- Resolve Ben's configuration, autonomy, tool allowlist, company scope, and applicable approval thresholds before planning or executing steps.
- Support bounded steps for context resolution, triage, knowledge retrieval, drafting, policy evaluation, human-review routing, and low-risk tool recommendations.
- Enforce maximum steps, runtime, retries, cancellation, idempotency, and correlation across audit, tool execution, approval, and case events.
- Execute external or sensitive side effects only through existing application workflows and approval boundaries; expose partial failure and safe retry state.

**Constraints and preservation rules**

Do not create a support-specific AI stack, free-form agent loops, controller orchestration, or direct provider calls. Existing manual case actions must continue to work independently.

**Acceptance criteria**

- A new eligible case can progress to a policy-evaluated draft with each step and source visible.
- A blocked or failed step stops subsequent unsafe work and can be retried without duplicating effects.
- Ben cannot invoke tools outside configured permissions or act across companies.

**Verification**

Add orchestration integration tests for success, timeout, cancellation, retry, policy denial, approval routing, idempotency, and tenant isolation; verify operational telemetry and builds.

**Definition of done**

The support agent has one bounded, shared-orchestration workflow with persisted state, policy enforcement, recoverability, and no uncontrolled autonomous loop.

### Prompt 1.5: Support AI evaluation and release gates

**Title and outcome**

Create a repeatable evaluation suite and release thresholds that demonstrate support AI quality and safety before configuration changes are promoted.

**Current context**

Service tests cover basic deterministic support behavior, but there is no representative AI evaluation corpus or measurable quality gate for triage, grounding, and safety.

**Dependencies**

Prompts 1.1 through 1.4.

**Implementation requirements**

- Add a versioned, synthetic, non-secret evaluation corpus covering billing, refunds, account access, bugs, complaints, churn, ambiguous requests, missing knowledge, multilingual/poorly formatted input, and adversarial injection.
- Evaluate classification correctness, priority/risk recall, source-grounded claim precision, answerability calibration, unsafe-action blocking, review routing, and cross-tenant protection.
- Provide deterministic test doubles at unit/integration level and an opt-in live-provider evaluation runner that never runs by default in normal builds.
- Define documented pass thresholds and emit machine-readable and human-readable reports without storing customer data or provider secrets.
- Add regression cases for every material production incident discovered later.

**Constraints and preservation rules**

Do not make CI depend on external credentials or nondeterministic live calls. Evaluation must measure persisted structured outputs and enforced behavior, not hidden reasoning.

**Acceptance criteria**

- Deterministic evaluations run locally and in CI with stable results.
- A known unsafe or ungrounded implementation fails the relevant release threshold.
- Live evaluation is explicitly enabled, securely configured, and clearly separated from required CI tests.

**Verification**

Run the deterministic suite, validate report generation and threshold failures, and document commands, configuration, dataset ownership, and interpretation.

**Definition of done**

Support AI changes have objective, repeatable quality and safety gates with no dependency on production customer data.

## Area 2: Refund and Finance Handoff

### Prompt 2.1: Process refund approval outcomes

**Title and outcome**

Reliably reflect approval decisions in support refund requests and their linked work so approved requests can advance and rejected requests close cleanly.

**Current context**

`SupportRefundWorkflowService` creates a refund request, work task, and `support_refund_credit` approval, but no support-specific handler consumes approval outcomes or advances `SupportRefundRequest` state.

**Dependencies**

None.

**Implementation requirements**

- Add an idempotent application/event handler for approved, rejected, cancelled, and expired approval outcomes using existing approval and outbox conventions.
- Resolve all linked records by tenant and stable identifiers, validate approval target and policy type, and update refund request, work task, support case, and case event atomically where appropriate.
- Add explicit lifecycle methods and transition validation to the refund domain entity instead of setting status strings ad hoc.
- Persist actor, rationale, approval reference, correlation, before/after state, and audit events. Treat duplicate and out-of-order events safely.
- Surface terminal and recoverable inconsistencies for operators rather than silently ignoring them.

**Constraints and preservation rules**

This prompt must not execute a refund or call Fortnox. Preserve existing generic approval processing and avoid coupling the Approval module directly to Support infrastructure.

**Acceptance criteria**

- Approved requests become ready for finance execution exactly once.
- Rejected, cancelled, and expired requests reach the matching terminal state and cannot execute.
- Wrong-company, wrong-policy, duplicate, and out-of-order events cannot mutate unrelated requests.

**Verification**

Test every outcome, duplicate delivery, ordering, missing links, tenant isolation, audit fields, and transactional rollback; build affected projects.

**Definition of done**

Every support refund approval outcome produces a consistent, auditable support state without initiating external financial action.

### Prompt 2.2: Validate and create internal finance actions

**Title and outcome**

Convert approved support refund requests into valid internal finance actions while preventing over-refunds and duplicate credits.

**Current context**

Refund requests may reference invoices and payments, but approval currently has no downstream finance command. Existing finance services and records remain the system of record for financial state.

**Dependencies**

Prompt 2.1.

**Implementation requirements**

- Add a Support-to-Finance application command/handler that consumes only approved, execution-ready refund requests.
- Validate linked customer invoice/payment ownership, currency, original paid amount, prior refunds/credits, refundable balance, accounting period constraints, and provider/integration state.
- Create the appropriate internal credit-note or refund action using existing Finance application services; do not duplicate finance business logic inside Support.
- Link the resulting finance action/reference back to the refund request and case, with idempotency keys and full audit provenance.
- Return structured blocking reasons for missing links, mismatched currency, excessive amount, duplicate request, already-refunded documents, or unsupported transaction type.

**Constraints and preservation rules**

No provider call or bank movement in this prompt. Enforce approval and finance authorization server-side and keep finance records authoritative.

**Acceptance criteria**

- A valid approved request creates one linked internal finance action.
- Invalid or duplicate requests create no finance mutation and display an actionable reason.
- Reprocessing the same request returns the existing action rather than creating another.

**Verification**

Add integration tests for full, partial, duplicate, excessive, currency-mismatch, missing-record, already-credited, authorization, and tenant-isolation scenarios.

**Definition of done**

Approved support requests can safely enter the Finance domain exactly once with reconciliation-ready links and no copied finance rules.

### Prompt 2.3: Execute approved credits through Fortnox

**Title and outcome**

Execute eligible approved support credits through the established Fortnox integration with reliable retry and reconciliation behavior.

**Current context**

Fortnox provider adapters and finance integration boundaries already exist. The support refund workflow does not yet connect its internal finance action to provider execution.

**Dependencies**

Prompts 2.1 and 2.2, and an active Fortnox connection for live verification.

**Implementation requirements**

- Dispatch provider execution through the existing Finance/Integration command and outbox/background worker patterns, never directly from Support or an HTTP request.
- Map internal credit/refund data to Fortnox within the provider adapter, validate provider capability, and persist request correlation, provider identifiers, attempts, and normalized status.
- Implement idempotency, retry classification, backoff, token-refresh handling, safe error summaries, and reconciliation after uncertain outcomes.
- Update the internal finance action and linked support refund request from normalized provider results without leaking raw secrets or sensitive payloads.
- Keep a no-provider/local development path that exercises internal state without claiming external completion.

**Constraints and preservation rules**

Never initiate bank payment automatically unless an existing explicitly approved finance workflow already authorizes it. Preserve Fortnox as an adapter, not the internal system of record.

**Acceptance criteria**

- Eligible approved actions are submitted exactly once and reach completed or operator-visible failed/reconciliation state.
- Retryable failures retry without duplication; terminal failures stop and explain the next step.
- Unknown provider outcomes are reconciled before any resubmission.

**Verification**

Use provider fakes for deterministic integration tests, cover token refresh and uncertain outcomes, and document an opt-in sandbox verification procedure.

**Definition of done**

Fortnox execution is reliable, idempotent, observable, and linked end-to-end from support approval to normalized finance result.

### Prompt 2.4: Complete refund lifecycle and recovery

**Title and outcome**

Make the refund lifecycle explicit and recoverable from request through approval, finance execution, completion, rejection, cancellation, or failure.

**Current context**

Earlier prompts add approval and execution handlers, but operators still need one consistent lifecycle, transition policy, retry model, and repair path.

**Dependencies**

Prompts 2.1 through 2.3.

**Implementation requirements**

- Define canonical states and allowed transitions for pending approval, approved, queued, executing, reconciliation required, completed, rejected, cancelled, and failed.
- Centralize transition rules in the domain/application layer and map provider/approval outcomes into them.
- Add retry-safe operator commands for recoverable failures and reconciliation, including concurrency tokens or equivalent protection.
- Prevent edits, cancellation, or retries that conflict with completed or provider-accepted work. Detect and flag linked-record drift.
- Add lifecycle metrics, stale-work monitoring, notifications, audit events, and a runbook for common recovery cases.

**Constraints and preservation rules**

No status changes exclusively in UI code. Never allow retry to bypass approval, policy, current refundable balance, or provider reconciliation.

**Acceptance criteria**

- Every state exposes only valid next actions and has an operator-readable explanation.
- Concurrent or repeated commands cannot create double credits/refunds.
- Recoverable failures can complete after retry; terminal states reject mutation consistently.

**Verification**

Add transition-table tests, concurrency tests, stale-work monitoring tests, retry/reconciliation integration tests, and migration tests if persisted state changes.

**Definition of done**

Refund work has one enforced lifecycle with safe recovery, monitoring, documentation, and no ambiguous intermediate status.

### Prompt 2.5: Refund and credit visibility in support

**Title and outcome**

Give support users a clear, actionable view of refund approval and finance execution without exposing technical provider internals.

**Current context**

The case detail can create a refund request but does not present the complete approval, finance action, execution, reconciliation, or recovery state introduced by prompts 2.1 through 2.4.

**Dependencies**

Prompts 2.1 through 2.4.

**Implementation requirements**

- Extend tenant-scoped support read models and web API contracts with amount, currency, linked invoice/payment, refundable balance, approval state, finance action, provider outcome, blocking reasons, and allowed actions.
- Add a case-detail refund section following the mandatory screenshot-first workflow and existing support design patterns.
- Show plain-English status, timeline, rationale, data used, and direct links to approval and finance records where authorized.
- Add controls only for valid server-advertised actions such as cancel-before-execution, retry recoverable failure, or request reconciliation; enforce all actions server-side.
- Include loading, empty, disconnected-provider, stale-data, unauthorized, and failure states.

**Constraints and preservation rules**

Do not expose raw payloads, tokens, internal enums, or controls that merely mutate display state. Preserve existing support and finance navigation behavior.

**Acceptance criteria**

- Users can understand what happened, what is blocking progress, and what they may do next from the support case.
- Invalid or unauthorized actions are absent in the UI and rejected by the API.
- Refreshing or retrying does not duplicate finance/provider actions.

**Verification**

Add API/read-model tests, authorization tests, component tests, and browser verification at desktop/mobile widths against the saved reference image.

**Definition of done**

The support case presents the complete real refund lifecycle with correctly enforced actions and no placeholder provider state.

## Area 3: Support Workspace UI

### Prompt 3.1: Case assignment and ownership

**Title and outcome**

Let authorized support users assign and reassign cases to eligible humans or support agents with visible ownership history.

**Current context**

The backend exposes case assignment and stores assigned user/agent identifiers, but `SupportApiClient` and the support pages do not expose a complete assignment workflow.

**Dependencies**

None.

**Implementation requirements**

- Add tenant-scoped read APIs for eligible assignees using existing membership and agent status rules, plus web-client methods for assign/unassign.
- Validate that selected users belong to the company and selected agents are active, permitted support agents; reject assigning both when the domain does not support shared ownership.
- Follow the screenshot-first workflow for a significant case-control redesign. Show current owner, unassigned state, workload context where already available, reassignment reason, and assignment timeline.
- Persist assignment events and audit records with previous/new owner and actor. Handle deactivated owners and concurrent reassignment clearly.
- Add inbox filters for assignee and “unassigned” without breaking pagination or other filters.

**Constraints and preservation rules**

Keep business logic out of Razor components, enforce authorization server-side, and do not expose users or agents from another company.

**Acceptance criteria**

- Authorized users can assign, reassign, and unassign eligible cases and see the history.
- Invalid, inactive, unauthorized, or cross-tenant assignees are rejected without mutation.
- Assignment filters return correct tenant-scoped results.

**Verification**

Test eligibility, authorization, tenant isolation, concurrency, audit history, API mapping, and responsive browser behavior.

**Definition of done**

Assignment is a complete persisted workflow across API and UI, with no raw identifier entry or client-only enforcement.

### Prompt 3.2: Explicit case lifecycle actions

**Title and outcome**

Replace generic status editing with clear, valid case actions that capture why a case changed state.

**Current context**

The API supports resolve, reopen, and close operations, but the UI uses a general status dropdown and does not collect the resolution summary and outcome required by the domain.

**Dependencies**

Prompt 3.1.

**Implementation requirements**

- Expose web-client methods and UI actions for resolve, reopen, close, escalate, wait for customer, and wait internally using existing specialized endpoints where available.
- Introduce an application-provided allowed-actions read model so the UI does not duplicate lifecycle transition rules.
- Require resolution summary/outcome, escalation reason, reopening note, or waiting reason as appropriate, with server-side validation.
- Show confirmation only for consequential actions and update case detail, inbox status, timeline, SLA state, and memory trigger consistently.
- Return plain-English invalid-transition and stale-state errors.

**Constraints and preservation rules**

Do not remove API compatibility without need. Do not allow arbitrary status strings to bypass domain transitions or authorization.

**Acceptance criteria**

- Each case state displays only valid actions and required fields.
- Resolving records a structured resolution; reopening and closing follow domain rules.
- Concurrent stale actions fail safely and refresh to the authoritative state.

**Verification**

Add lifecycle and validation tests, API/client tests, component tests, and desktop/mobile browser checks.

**Definition of done**

Case lifecycle changes are explicit, explainable, domain-enforced, and fully available in the support workspace.

### Prompt 3.3: Reply editing and review workspace

**Title and outcome**

Provide a complete operator workflow to inspect sources, edit, approve, reject, regenerate, and send support replies safely.

**Current context**

The API supports editing and rejecting drafts, while the current page shows draft text and approve/send buttons but lacks editing, rejection, source inspection, and precise state-dependent controls.

**Dependencies**

Prompt 3.2 and Area 1 prompt 1.3 when AI safety decisions are available.

**Implementation requirements**

- Extend the web client for edit, reject, regenerate/force-review, and source/safety detail retrieval.
- Build an accessible draft editor following screenshot-first requirements, with tone, body, unsaved-change protection, confidence, answerability, source references, rationale, policy status, and send failure details.
- Display controls from server-provided allowed actions; require rejection reason and reevaluate edited content before approval/send.
- Prevent double send, stale-draft approval, and sending an unsaved body. Show pending, sent, rejected, superseded, and failed states clearly.
- Keep customer-facing body separate from internal rationale and source metadata.

**Constraints and preservation rules**

Do not expose hidden prompts or chain-of-thought. Preserve mailbox idempotency and enforce approval/policy in the API.

**Acceptance criteria**

- Operators can edit and review a draft with its permitted sources before approval.
- Invalid actions are unavailable and rejected server-side if called directly.
- A sent draft cannot be edited or sent again.

**Verification**

Test API contracts, draft transitions, stale updates, authorization, policy reevaluation, component behavior, accessibility, and responsive rendering.

**Definition of done**

The reply workspace supports the entire real review lifecycle without raw metadata, placeholder controls, or client-only safety.

### Prompt 3.4: Knowledge-gap operations workspace

**Title and outcome**

Make repeated missing support knowledge visible and actionable so the team can improve approved answers.

**Current context**

Knowledge gaps and documentation-task endpoints exist, and gaps can be linked to cases and drafts, but the web client has no complete list/detail workflow.

**Dependencies**

Prompt 3.3.

**Implementation requirements**

- Add web-client methods and tenant-scoped read models for filtered/paged knowledge gaps, affected cases, frequency, source-search summary, linked task, status, and permitted actions.
- Build a support knowledge-gap page and case-detail section using screenshot-first workflow and existing list/detail patterns.
- Allow authorized users to create/open a documentation task, mark resolved only when approved knowledge is linked, and reopen if the answer becomes unavailable.
- Link to permitted case, draft, task, and knowledge records. Deduplicate repeated requests and show frequency trends without exposing customer-sensitive text unnecessarily.
- Add navigation, empty/loading/error states, audit events, and plain-English explanations.

**Constraints and preservation rules**

Knowledge-gap status must remain backend-owned. Do not copy full customer conversations into documentation tasks or bypass knowledge access scopes.

**Acceptance criteria**

- Users can identify highest-impact gaps and create exactly one linked documentation task.
- Resolution requires an accessible approved knowledge source.
- All links and results remain company-scoped and role-authorized.

**Verification**

Test deduplication, task linking, resolution validation, authorization, tenant isolation, navigation, accessibility, and responsive UI.

**Definition of done**

Knowledge gaps have a usable operational workflow from detection through documented resolution.

### Prompt 3.5: Production support dashboard

**Title and outcome**

Turn the support landing page into an operational queue that tells users what needs attention and what to do next.

**Current context**

The inbox shows summary counts, filters, a case table, and one category insight. It lacks robust paging, saved operational views, ownership queues, trend context, and complete action prioritization.

**Dependencies**

Prompts 3.1 through 3.4 and Area 4 SLA read models when available.

**Implementation requirements**

- Define optimized support dashboard/read models for assigned to me, unassigned, SLA risk, breached, awaiting approval, waiting too long, failed replies, and recent resolutions.
- Add server-side pagination, sorting, complete filters, URL-preserved state, and saved views using existing user-preference patterns if present.
- Follow screenshot-first workflow and show Ben's presence, top priorities, concise trends, actionable insights, and direct navigation to the exact case/action.
- Avoid expensive per-row queries and define indexes/migration only if query evidence requires them.
- Include accessible loading, empty, partial-failure, stale-data, and mobile behavior.

**Constraints and preservation rules**

Use read services rather than composing transactional queries in Razor. Do not add passive decorative charts or expose internal states.

**Acceptance criteria**

- Every attention metric opens the matching filtered queue.
- Paging/filtering is stable, tenant-scoped, and performant at representative volume.
- The page clearly identifies the highest-priority next actions on desktop and mobile.

**Verification**

Add read-model correctness/performance tests, tenant and authorization tests, component tests, and Playwright/browser comparison with the saved reference screenshot.

**Definition of done**

The support dashboard is a production operational workspace, not a static summary, and all displayed actions lead to real workflows.

## Area 4: Configurable SLA Policies

### Prompt 4.1: Resolve SLA policies from persisted configuration

**Title and outcome**

Use company SLA policies to calculate support deadlines instead of hard-coded durations.

**Current context**

`SupportSlaPolicy` persistence exists, but `SupportSlaMonitor` currently uses fixed high/normal response and resolution hours and does not query policies.

**Dependencies**

None.

**Implementation requirements**

- Add a tenant-scoped SLA policy resolver with deterministic precedence across exact category/priority/customer tier and documented fallback levels.
- Apply resolved policy when a case is created, triaged, reprioritized, recategorized, or linked to a customer tier, with a persisted policy/version reference or calculation snapshot.
- Replace hard-coded monitor calculations while preserving existing deadlines where recalculation is not warranted.
- Define a safe default policy for companies without configuration and seed/migrate only when necessary.
- Audit deadline changes and expose calculation rationale without internal enum leakage.

**Constraints and preservation rules**

Keep policy lookup out of controllers/UI, maintain tenant isolation, and preserve already-breached history.

**Acceptance criteria**

- Exact policies win over fallbacks deterministically.
- Companies without policies receive documented default deadlines.
- Relevant case changes recalculate correctly, while unrelated updates do not move deadlines.

**Verification**

Test precedence, defaults, recalculation triggers, inactive policies, tenant isolation, audit, migration, and existing SLA monitoring.

**Definition of done**

All new SLA deadlines derive from an explainable persisted policy or explicit default, with no hard-coded primary calculation path.

### Prompt 4.2: SLA policy administration API

**Title and outcome**

Provide secure APIs for tenant administrators to manage SLA targets without database edits.

**Current context**

SLA policy entities exist but have no complete administration application service or controller surface.

**Dependencies**

Prompt 4.1.

**Implementation requirements**

- Add commands, queries, DTOs, and authenticated endpoints to list, create, update, activate, deactivate, and inspect policy usage.
- Restrict mutation to appropriate company-admin/support-manager roles using existing authorization policies.
- Validate positive durations, supported dimensions, uniqueness/overlap, precedence ambiguity, and optimistic concurrency.
- Prevent destructive deletion of referenced policies; use deactivation and retain historical calculation snapshots.
- Write audit events with before/after values and actor/correlation information.

**Constraints and preservation rules**

Do not expose EF entities directly or trust company IDs from request bodies. Keep read and write concerns separated according to repository conventions.

**Acceptance criteria**

- Authorized administrators can manage valid policies and inspect their precedence.
- Ambiguous duplicates, invalid durations, stale updates, and unauthorized calls fail without mutation.
- Historical cases remain explainable after policy changes.

**Verification**

Add validation, authorization, concurrency, tenant-isolation, audit, and API contract integration tests.

**Definition of done**

SLA configuration has a production API with complete validation, authorization, history, and no direct-database operational dependency.

### Prompt 4.3: SLA policy settings UI

**Title and outcome**

Let authorized users understand and manage support SLA policies through a clear settings experience.

**Current context**

Prompt 4.2 provides APIs, but support settings have no policy list/editor, conflict explanation, or case-policy preview.

**Dependencies**

Prompts 4.1 and 4.2.

**Implementation requirements**

- Use screenshot-first workflow to create a support SLA settings page using existing settings/navigation components.
- Show active/inactive policies, category, priority, customer tier, response target, resolution target, precedence, usage count, and updated metadata.
- Provide create/edit/deactivate flows with inline validation, unsaved-change handling, conflict warnings, concurrency refresh, and permission-aware controls.
- Add a preview tool that uses the backend resolver to show which policy applies to a selected example, without duplicating resolution logic in the client.
- Include empty, loading, error, unauthorized, and narrow-screen states in plain English.

**Constraints and preservation rules**

Do not expose raw enums or create a second SLA rule engine in JavaScript/Razor. Preserve global design and support navigation.

**Acceptance criteria**

- Admins can manage policies and see their effective precedence.
- Non-admins can view only what their authorization allows and cannot mutate via direct API calls.
- Preview results match actual resolver results.

**Verification**

Add client/component tests, authorization tests, concurrency/error cases, accessibility checks, and desktop/mobile browser comparison to the reference.

**Definition of done**

SLA policies are safely manageable in-product with accurate previews and no database/manual configuration requirement.

### Prompt 4.4: Business hours, holidays, and timezones

**Title and outcome**

Calculate SLA deadlines in real company working time rather than simple elapsed hours.

**Current context**

Policy durations are elapsed-time values. Company timezone and scheduling concepts exist elsewhere, but support SLAs do not account for working hours, weekends, holidays, or daylight saving.

**Dependencies**

Prompts 4.1 through 4.3.

**Implementation requirements**

- Add or reuse tenant-scoped business calendar configuration for timezone, weekly working intervals, holidays, and closure periods.
- Implement a deterministic application service that adds working minutes across intervals and DST transitions using an established timezone library already available in the stack.
- Allow SLA policies to choose elapsed or business time and persist enough calculation context to reproduce deadlines.
- Recalculate future deadlines safely when calendar/policy/case inputs change, with explicit rules for in-progress and already-breached cases.
- Add admin UI for calendar settings if an equivalent reusable company calendar does not already exist.

**Constraints and preservation rules**

Store timestamps in UTC, never infer timezone from the browser, and maintain local SQL/Docker migration compatibility.

**Acceptance criteria**

- Deadlines skip non-working periods and handle Stockholm DST boundaries correctly.
- Overnight, split-shift, holiday, and no-working-time configurations are validated deterministically.
- Historical deadline rationale remains reproducible after configuration changes.

**Verification**

Add exhaustive calendar unit tests, DST/timezone tests, recalculation integration tests, migration tests, and UI verification if settings are added.

**Definition of done**

Support SLA calculations correctly support configurable elapsed or business time with reproducible UTC deadlines.

### Prompt 4.5: SLA escalation, notification, and reporting

**Title and outcome**

Proactively alert the right people before breaches and provide reliable SLA performance reporting.

**Current context**

The background monitor marks risk/breach, but risk threshold is fixed, notifications are not clearly routed, and reporting does not explain policy performance.

**Dependencies**

Prompts 4.1 through 4.4.

**Implementation requirements**

- Add configurable risk thresholds and escalation recipients by policy/severity using existing notification and assignment systems.
- Emit deduplicated outbox notifications for entering risk, breach, recovery/recalculation, and unresolved repeated escalation.
- Track first-response and resolution compliance against the applied policy snapshot and expose tenant-scoped trend/read models.
- Add dashboard links from SLA metrics to matching cases and explanations of applied targets.
- Add worker locking/idempotency, stale-case monitoring, metrics, and an operator runbook.

**Constraints and preservation rules**

Do not send notifications directly in monitor transactions. Avoid repeated alerts when state has not changed and never notify users outside the company.

**Acceptance criteria**

- Risk and breach transitions notify configured recipients exactly once per meaningful transition.
- Repeated worker runs are idempotent and concurrent workers do not duplicate alerts.
- Reports use the policy snapshot that governed each case, not today's configuration.

**Verification**

Test notification routing/deduplication, worker concurrency, policy snapshot reporting, authorization, tenant isolation, and dashboard links.

**Definition of done**

SLA risk is proactive, correctly routed, measurable, and recoverable, with no notification spam or misleading historical metrics.

## Area 5: Resolution Learning and Memory

### Prompt 5.1: Trigger memory updates from resolved cases

**Title and outcome**

Reliably update permitted customer-support memory after a case is resolved so future support context can improve.

**Current context**

`ISupportMemoryUpdateService` is registered and implemented, but resolution does not invoke it. Case resolution, outbox/background infrastructure, customer memory profiles, and audit events already exist.

**Dependencies**

Area 3 prompt 3.2 so resolution captures valid structured data.

**Implementation requirements**

- Emit a tenant-scoped domain/outbox event after a successful case resolution and handle it asynchronously with existing worker patterns.
- Invoke the memory application service idempotently using company/case identifiers and a versioned event key.
- Persist processing state, retries, terminal failure details, correlation, and audit events without blocking the case-resolution transaction.
- Skip cases without an eligible contact or reusable resolution content and record a safe explainable outcome.
- Reprocess safely after transient failure without duplicating memory observations.

**Constraints and preservation rules**

Do not call memory updates directly from the controller, copy entire conversations into memory, or process data across tenants.

**Acceptance criteria**

- Resolving an eligible case queues one memory update and repeated delivery produces no duplicates.
- Resolution succeeds even when asynchronous memory processing later fails.
- Ineligible or missing contacts produce no customer memory and an observable skipped result.

**Verification**

Add outbox/handler integration tests for success, retry, duplicate, missing contact, failure, audit, and tenant isolation.

**Definition of done**

Resolved cases reliably trigger idempotent background memory processing with operational visibility.

### Prompt 5.2: Capture structured reusable resolutions

**Title and outcome**

Capture enough structured resolution information to support accurate reuse, analytics, and memory decisions.

**Current context**

Resolution currently stores summary and outcome, while future learning needs root cause, action taken, reusable answer, linked records, and explicit reuse eligibility.

**Dependencies**

Prompt 5.1 and Area 3 prompt 3.2.

**Implementation requirements**

- Extend the resolution domain model and contracts with root-cause category, action taken, outcome, reusable answer, customer preference observations, relevant links, and a reuse/knowledge eligibility decision.
- Validate length, allowed values, record ownership, and required fields by case category; keep important queryable state relational rather than JSON-only.
- Add an EF migration and snapshot updates if persistence changes, preserving existing resolution rows with safe defaults.
- Update resolve API/UI to collect plain-English fields efficiently and show the resulting resolution in case history.
- Include structured resolution data in permitted knowledge retrieval and analytics only after eligibility checks.

**Constraints and preservation rules**

Do not force speculative customer preferences, overwrite historical resolution evidence, or break old cases and API consumers unnecessarily.

**Acceptance criteria**

- New resolutions persist validated structured fields and remain readable after restart.
- Existing resolutions migrate without data loss and remain displayable.
- Cross-tenant linked records and invalid reuse flags are rejected.

**Verification**

Test domain validation, API compatibility, migration up/down behavior where supported, local/Docker SQL compatibility, tenant isolation, and UI flows.

**Definition of done**

Resolutions contain production-quality structured learning inputs with backward-compatible persistence and complete UI/API support.

### Prompt 5.3: Enforce customer-memory safety policy

**Title and outcome**

Prevent sensitive, speculative, or short-lived case details from becoming durable customer memory.

**Current context**

The memory updater can store support observations, but it lacks a dedicated policy that distinguishes safe reusable preferences from secrets, payment data, and unsupported inferences.

**Dependencies**

Prompts 5.1 and 5.2.

**Implementation requirements**

- Add a memory-candidate extraction and policy service using existing shared AI orchestration only if inference is needed, with deterministic validation afterward.
- Classify candidates as allow, review, reject, or time-limited; block credentials, authentication data, full payment/bank details, protected/sensitive attributes, transient emotions, and unsupported personality conclusions.
- Require source case/resolution, evidence excerpt or structured field, confidence, observed date, validity/expiration, and policy version.
- Deduplicate equivalent observations, handle contradictions without silent overwrite, and audit every accepted/rejected candidate without retaining prohibited values in logs.
- Apply redaction before any AI call and before persistence.

**Constraints and preservation rules**

Do not store hidden reasoning, raw conversations, secrets, or sensitive rejected candidate content. Human override cannot bypass hard security/privacy rules.

**Acceptance criteria**

- Safe explicit preferences can be stored with provenance and expiry.
- Credentials, financial identifiers, and speculative traits are rejected and absent from storage/logs.
- Contradictory observations are surfaced for review rather than overwritten.

**Verification**

Add policy unit tests, adversarial/privacy cases, redaction tests, contradiction/deduplication tests, audit assertions, and tenant isolation tests.

**Definition of done**

No support-derived customer memory is persisted without deterministic safety validation, provenance, and lifecycle metadata.

### Prompt 5.4: Customer-memory review controls

**Title and outcome**

Let authorized users inspect, correct, expire, or delete support-derived customer memories and understand where they came from.

**Current context**

Customer memory can influence support context but support users cannot see which memories were used or review newly proposed observations from case resolution.

**Dependencies**

Prompts 5.1 through 5.3.

**Implementation requirements**

- Add tenant-scoped read and command APIs for support-memory candidates and active observations, including source case, confidence, validity, use history, and allowed actions.
- Follow screenshot-first workflow for a case/customer memory review surface integrated with existing support and memory UI patterns.
- Support approve when review is required, correct through superseding version, expire, and delete according to existing privacy/audit rules.
- Show memories used for a draft and why they were relevant without exposing unrelated customer data or hidden model instructions.
- Enforce role authorization, optimistic concurrency, and audit before/after metadata.

**Constraints and preservation rules**

Do not edit immutable provenance, expose deleted sensitive values in audit UI, or let support users browse memory outside their company and permissions.

**Acceptance criteria**

- Authorized reviewers can trace each observation to its source and safely correct or remove it.
- Unauthorized and stale modifications are rejected.
- Removed/expired memory is no longer retrieved for new drafts while historical usage remains explainable.

**Verification**

Test commands, authorization, tenant isolation, concurrency, retrieval exclusion, audit redaction, accessibility, and responsive browser behavior.

**Definition of done**

Support-derived memory is transparent and governable across API and UI, including deletion and historical explainability.

### Prompt 5.5: Measure learning effectiveness

**Title and outcome**

Measure whether support memory and resolved-case reuse improve outcomes without degrading safety or accuracy.

**Current context**

Support analytics count cases/categories, but do not attribute draft quality, repeat contacts, corrections, or resolution speed to memory and knowledge usage.

**Dependencies**

Prompts 5.1 through 5.4 and Area 1 prompt 1.5 for AI evaluation metrics.

**Implementation requirements**

- Define tenant-scoped outcome events/read models for memory used, knowledge used, answerability, human edit magnitude, approval/rejection, correction, reopen/repeat contact, and resolution time.
- Persist only necessary identifiers and aggregates; avoid duplicating customer message bodies or sensitive memory values.
- Add analytics comparing supported cohorts and trend periods with clear caveats rather than claiming causation from correlation.
- Surface actionable insights such as frequently corrected memory or knowledge that improves answerability, with drill-down authorization.
- Add retention, recalculation, and backfill strategy where needed using background processing.

**Constraints and preservation rules**

Do not expose cross-tenant benchmarks or sensitive content, and do not let analytics mutate operational state.

**Acceptance criteria**

- Metrics can identify whether memory was used and whether the resulting draft was edited, rejected, or associated with a reopened case.
- Aggregate calculations are reproducible and tenant-scoped.
- Missing historical data is labeled rather than silently treated as zero.

**Verification**

Add metric-definition tests, seeded multi-tenant integration tests, backfill/idempotency tests, query performance checks, and dashboard authorization tests.

**Definition of done**

The product can evaluate support learning with explainable, privacy-conscious metrics and actionable drill-downs.

## Area 6: Mailbox Threading and Routing

### Prompt 6.1: Preserve complete message-thread metadata

**Title and outcome**

Carry the provider metadata needed to associate inbound and outbound messages with the correct support conversation.

**Current context**

The support mailbox router passes provider message ID but currently supplies no provider thread ID from email snapshots. Message/case entities already contain some provider fields, and mailbox/provider abstractions exist.

**Dependencies**

None.

**Implementation requirements**

- Inventory metadata available from each mailbox provider and snapshot model: provider thread ID, provider message ID, internet message ID, in-reply-to, references, mailbox connection, recipient aliases, and normalized subject.
- Extend normalized mailbox snapshot and support ingestion contracts/persistence only where required, with an EF migration and backward-compatible null handling.
- Populate metadata in inbound polling/webhook paths and preserve it through support messages, cases, outbound replies, audit, and read models.
- Normalize and validate identifier length/format without treating provider identifiers as globally unique across providers/mailboxes.
- Document provider capability differences and local/Docker migration/restore steps.

**Constraints and preservation rules**

Keep provider-specific parsing in Integration adapters and normalized identifiers in core contracts. Do not store access tokens or unnecessary raw headers.

**Acceptance criteria**

- Available thread/reply metadata survives provider ingestion through persisted support messages.
- Existing snapshots/messages remain readable after migration.
- Identical provider IDs in different mailbox/provider scopes do not collide.

**Verification**

Test each provider adapter, null/legacy data, normalization, uniqueness scope, migration, local/Docker SQL, tenant isolation, and outbound preservation.

**Definition of done**

Support ingestion retains all safe normalized metadata required for deterministic threading across supported mailbox providers.

### Prompt 6.2: Deterministic conversation matching

**Title and outcome**

Associate inbound messages with the correct support case using auditable ranked evidence instead of a broad recent-case fallback.

**Current context**

Mailbox ingestion can deduplicate by provider message and otherwise search recent open cases, which risks joining unrelated messages. Prompt 6.1 provides richer thread metadata.

**Dependencies**

Prompt 6.1.

**Implementation requirements**

- Implement a tenant/mailbox-scoped matcher with precedence for exact provider thread, in-reply-to/references, known internet message, and then constrained sender/recipient/normalized-subject/recent-case evidence.
- Define confidence thresholds for auto-link, create-new, and ambiguous/manual-review outcomes.
- Persist match strategy, confidence, candidate case identifiers, and concise rationale without storing unnecessary raw headers.
- Never link to closed/old cases except through strong explicit reply evidence and documented reopening rules.
- Add an ambiguous-message queue rather than guessing when top candidates are too close.

**Constraints and preservation rules**

Matching must be deterministic, tenant-scoped, and independent of UI state. Do not use free-form AI as the authority for case linkage.

**Acceptance criteria**

- Exact provider/reply evidence links to the expected case.
- Weak or conflicting evidence creates a new case or manual-review item according to thresholds.
- Unrelated same-sender messages are not merged solely because they are recent.

**Verification**

Add a matching matrix covering new thread, normal reply, changed subject, forwarding, reused subject, closed case, ambiguity, provider/mailbox collision, and tenant isolation.

**Definition of done**

Every inbound message has an explainable deterministic linkage decision with no unsafe broad fallback.

### Prompt 6.3: Support mailbox routing policies

**Title and outcome**

Route only intended support mail into cases and keep finance, system, spam, and unrelated mailbox traffic out.

**Current context**

The worker scans recent email snapshots broadly. Mailbox connections exist, but there is no complete support-specific enablement and routing-policy layer.

**Dependencies**

Prompts 6.1 and 6.2.

**Implementation requirements**

- Add tenant-scoped support mailbox configuration that explicitly enables connections and defines accepted recipients/aliases, folders, sender rules, automated-message handling, and exclusions.
- Resolve routing policy before ingestion and persist a normalized route, ignore, or review decision with policy version and rationale.
- Add authenticated admin APIs and, if no reusable mailbox settings surface exists, a screenshot-first UI for support routing configuration and test-message preview.
- Handle disconnected/revoked connections and configuration drift with operator-visible state and no silent message loss.
- Default safely: existing mailboxes must not become support sources merely because the worker is enabled.

**Constraints and preservation rules**

Keep provider folder/header mapping in adapters, enforce configuration server-side, and never route across companies or expose mailbox secrets.

**Acceptance criteria**

- Only explicitly enabled mailbox traffic matching policy becomes or updates support cases.
- Excluded/system/finance messages are ignored or reviewed according to policy with explainable outcomes.
- Unauthorized users cannot change routing configuration.

**Verification**

Test policy combinations, safe defaults, disconnected state, authorization, tenant isolation, provider mapping, API, and any added UI.

**Definition of done**

Support mailbox ingestion is explicitly configured, safe by default, and operationally explainable.

### Prompt 6.4: Duplicate and concurrency protection

**Title and outcome**

Guarantee that polling overlap, webhooks, retries, and out-of-order delivery do not create duplicate messages or cases.

**Current context**

The ingestion service checks for existing provider messages, but read-before-write checks alone can race under concurrent workers or duplicate provider delivery.

**Dependencies**

Prompts 6.1 through 6.3.

**Implementation requirements**

- Define database-enforced idempotency keys scoped by company, mailbox connection, provider, and provider/internet message identity.
- Add unique constraints/indexes and an EF migration where required, with safe handling/backfill for existing duplicates.
- Make ingestion and match/link creation transactional and handle unique-constraint races by returning the authoritative existing result.
- Add worker claim/lease behavior consistent with repository patterns and safe handling for out-of-order parent/reply delivery.
- Persist attempt/outcome telemetry and avoid retrying terminal malformed input indefinitely.

**Constraints and preservation rules**

Do not rely on in-memory locks, a single process, or provider delivery guarantees. Preserve valid historical messages during cleanup/backfill.

**Acceptance criteria**

- Concurrent ingestion of the same provider message results in one support message and one linkage decision.
- Polling and webhook delivery of the same message converge on the same record.
- Out-of-order delivery can reconcile threading without duplicating cases or outbound effects.

**Verification**

Add real relational concurrency tests, unique-race tests, worker overlap tests, retry tests, migration/backfill tests, and local/Docker SQL verification.

**Definition of done**

Mailbox ingestion is database-idempotent and concurrency-safe across processes and delivery mechanisms.

### Prompt 6.5: Mailbox operations and diagnostics

**Title and outcome**

Give operators a safe way to understand and recover routing failures, ambiguity, disconnected mailboxes, and dead-lettered messages.

**Current context**

Routing runs in a background worker and logs failures, but support users lack an operational queue for unmatched/ambiguous/failed messages and safe recovery actions.

**Dependencies**

Prompts 6.1 through 6.4.

**Implementation requirements**

- Add persisted operational states for pending, routed, ignored, ambiguous, retrying, failed, and dead-lettered ingestion attempts using existing worker/outbox conventions where possible.
- Add tenant-scoped APIs and a screenshot-first operations UI showing safe message metadata, connection health, decision rationale, attempts, next retry, and candidate cases.
- Provide authorized actions to retry, link to an existing case, create a new case, or mark ignored, all idempotent and fully audited.
- Add health metrics and alerts for backlog age, failure rate, disconnected support mailboxes, repeated provider errors, and dead-letter growth.
- Write a runbook covering credential reconnect, provider outage, poison message, ambiguity, and replay procedures.

**Constraints and preservation rules**

Never display tokens, raw sensitive headers, or unrestricted message bodies. Manual recovery must rerun current routing, authorization, and duplicate policies.

**Acceptance criteria**

- Operators can identify why a message was not routed and take only valid recovery actions.
- Retry/reassignment cannot create duplicate messages or bypass tenant/routing policy.
- Disconnected mailboxes and growing backlogs become visible before messages silently age out.

**Verification**

Test each operational state and action, authorization, idempotency, redaction, alerts, tenant isolation, accessibility, and responsive browser behavior.

**Definition of done**

Mailbox routing has complete operational visibility and safe recovery, with documented procedures and no log-only failure mode.
