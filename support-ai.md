# Support Agent AI Implementation Prompts

## Purpose And Execution Order

This pack implements Ben's role-specific AI advice, analysis, and orchestration from `agents-ai.md`. Execute in order. Preserve the existing Support case, grounding, safety, SLA, refund, memory, and durable delivery authorities.

1. Shared-reasoning integration and Support evidence
2. Triage, context matching, and prioritization
3. Grounded reply advice and answerability
4. Risk, sentiment, SLA prediction, and escalation
5. Related-case clustering and root-cause analysis
6. Knowledge coverage and reviewed learning
7. Support operating cadence and manager cockpit

## Instructions For Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `agents-ai.md`, `architecture-inst.md` when present, and `/docs/architecture-rules.md`. UI work must follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements.
- Use the shared capability catalog, `IAgentReasoningGateway`, runs, grounded sources, handoffs, memory candidates, quality events, tasks, workflows, approvals, audit, and background execution.
- Preserve `ICompanyKnowledgeSearchService`, Support context/knowledge abstractions, `DeterministicSupportReplySafetyPolicy`, SLA policy, refund workflows, and `SupportReplyDeliveryDispatcher` as authoritative boundaries.
- AI must not invent product behavior, policy, customer/order/payment facts, commitments, refunds, legal advice, or resolution status.
- Every company/customer/case source and command is authorized and tenant-scoped. Schema changes require EF migrations and equivalent local/Docker SQL Server paths.
- Implement production behavior with no mock production data, autonomous unsafe sends, direct feature-level provider bypass, silent grounding fallback, or deferred in-scope TODOs.

---

## Prompt 1: Integrate Support With Shared Reasoning And Versioned Evidence

### 1. Title And Outcome

Adapt Support AI orchestration to the shared reasoning platform while retaining stricter Support grounding and reply-safety rules, and publish one versioned evidence contract for later Support analysis.

### 2. Current Context

- `ISupportAgentOrchestrationService`, `SupportAgentOrchestrationService`, case/context/triage/draft/safety/refund/SLA/memory services, and the operations worker already form a mature pipeline.
- Support contracts include source references, knowledge context, safety decisions, executions, cases, messages, drafts, gaps, refunds, analytics, and memory observations.
- Shared AI adds validated runs, capabilities, handoffs, memory candidates, and quality events; Support must adapt without replacing stricter domain behavior.

### 3. Dependencies

- Shared AI implementation from `shared-ai.md`.

### 4. Implementation Requirements

- Define a Support evidence envelope covering case/message/thread, resolved customer context, accessible knowledge sources, policy, SLA, related records, prior outcomes, sensitivity, freshness, and source IDs.
- Add a Support reasoning facade over `IAgentReasoningGateway` with schemas for triage explanation, reply advice, risk analysis, root cause, and knowledge recommendations.
- Integrate the shared run/correlation lifecycle into `SupportAgentOrchestrationService` while preserving current execution/idempotency records and public contracts.
- Ensure Support safety/grounding validation runs after AI output and before draft approval/delivery. Shared validation is additive, not a substitute.
- Register stable Support capability IDs and requirements in the shared catalog.
- Map material execution outcomes to quality events without copying raw customer messages or sensitive payloads.
- Keep deterministic fallback and explicit review for missing provider configuration, timeout, invalid output, refusal, or insufficient grounding.
- Add authorized run/detail evidence links needed by Support UI.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not create a second Support orchestrator or bypass `SupportReplyDeliveryDispatcher`.
- Only processed, indexed, accessible sources may support factual reply claims.
- Preserve inbound-message, case, execution, draft, and delivery idempotency.

### 6. Acceptance Criteria

- Given a Support execution, then AI reasoning is represented by a shared run correlated to the existing Support execution.
- Given insufficient grounding, then the output requires review/gap and cannot become an approved autonomous reply.
- Given invalid AI output, then no case/refund/send side effect occurs and the failure is visible.
- Given another company's case/source ID, then no data is disclosed.

### 7. Verification

- Unit-test evidence mapping, shared/Support validation order, fallback, correlation, and redaction.
- Add fake-provider success, malformed, timeout, unsupported-citation, and policy-conflict tests.
- Extend Support orchestration, grounding, safety, delivery, audit, authorization, and tenant-isolation tests.
- Build Application, Infrastructure, API, and Web.

### 8. Definition Of Done

Support uses shared AI infrastructure while retaining stricter Support safety and system-of-record behavior. No parallel provider path, grounding regression, duplicate execution, or unsafe side effect remains.

---

## Prompt 2: Implement AI-Supported Triage, Context Matching, And Queue Priority

### 1. Title And Outcome

Implement explainable case classification, customer/context matching, impact assessment, assignment advice, and queue prioritization with deterministic status, SLA, and access rules.

### 2. Current Context

- Mailbox ingestion/routing, context resolution, triage, case assignment, messages, SLA resolution, and deterministic Support priorities already exist.
- `SupportTriageResult` records category, priority, confidence, and rationale, but ambiguous matching and portfolio priority need stronger evidence and review behavior.

### 3. Dependencies

- Prompt 1.

### 4. Implementation Requirements

- Build deterministic candidate matching for customer, account, invoice, subscription/order where available, prior cases, and duplicate inbound messages.
- Use AI to interpret ambiguous intent, sentiment, and context only from bounded evidence; return ranked candidates and explicit ambiguity.
- Keep category, priority limits, SLA deadlines, assignment availability, and restricted-case access in backend policy.
- Produce triage with intent/category, customer impact, urgency factors, matched references, confidence, missing information, duplicate/related indicators, recommended assignee, and sources.
- Require human review below thresholds or for conflicting identity, legal/privacy/security, threats, high-value customers, or unsupported record matches.
- Implement deterministic queue score from SLA risk, severity, customer impact, wait time, dependencies, and confidence; AI only explains ranking.
- Persist review corrections and avoid duplicate triage/tasks across polling and retries.
- Add API/client and prioritized Support queue explanations.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- AI cannot merge customers/cases, assign restricted data, or change priority outside allowed values.
- Email addresses and model guesses are not sole identity proof.
- Preserve case/message/routing idempotency and SLA calculations.

### 6. Acceptance Criteria

- Given ambiguous customer matches, then no customer is silently selected and review is required.
- Given legal/privacy/security signals, then deterministic escalation policy overrides ordinary triage.
- Given duplicate mailbox polling, then one case/message/triage execution exists.
- Given two companies with matching emails, then records remain isolated.

### 7. Verification

- Unit-test matching, ambiguity thresholds, category/priority validation, escalation overrides, queue score, and dedupe identities.
- Add integration tests for known/unknown/ambiguous customers, duplicate messages, restricted cases, SLA risk, and tenant isolation.
- Extend mailbox, routing, triage, context, assignment, SLA, API, and Web tests.
- Add UI tests for evidence, ambiguity, manual correction, and empty/failure states.

### 8. Definition Of Done

Ben receives a deterministic-first Support queue and explainable triage. Ambiguous identity and sensitive risk are escalated rather than guessed.

---

## Prompt 3: Implement Grounded Answerability And Source-Backed Reply Advice

### 1. Title And Outcome

Implement robust answerability assessment and source-backed reply advice that distinguishes confirmed product/policy facts, customer-specific facts, inferences, and missing information before any draft can be approved or sent.

### 2. Current Context

- `SupportKnowledgeContextProvider`, knowledge search, reply drafting, safety decisions, knowledge gaps, draft review, and durable delivery already exist.
- Company product catalog, policies, and FAQ can be indexed through agent documents.
- Existing pipeline needs claim-level grounding, contradiction/staleness handling, and measurable answerability outcomes through shared AI.

### 3. Dependencies

- Prompts 1-2.
- Processed and indexed accessible Support knowledge.

### 4. Implementation Requirements

- Define deterministic answerability rules for source presence, processing/index state, scope, freshness, contradictions, customer-record availability, and claim coverage.
- Retrieve through existing Support knowledge/context abstractions only.
- Generate a structured answer plan and draft with claim type, source IDs, confidence, missing information, questions to ask, tone guidance, and review requirement.
- Validate every factual customer-facing claim against supplied sources. Unsupported claims are removed or converted to questions/unknowns.
- Preserve deterministic reply safety after drafting, including sensitive topics, promises, refunds, legal/privacy/security, and unusual compensation.
- Create/deduplicate Support knowledge gaps for material unanswered questions and link them to the exact case/draft/retrieval.
- Queue approved low-risk sends only through `support.reply.delivery_requested` and `SupportReplyDeliveryDispatcher`; recheck state and safety immediately before dispatch.
- Add source drill-down, claim labels, missing-information actions, and feedback in existing case/draft UI.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- No autonomous send without sufficient trusted grounding and deterministic safety approval.
- Do not expose inaccessible document chunks or raw internal policies to customers.
- Preserve draft editing, approval, outbox, retry, and reconciliation behavior.

### 6. Acceptance Criteria

- Given complete trusted evidence, then every factual draft claim cites an accessible supplied source.
- Given contradictory or stale policy documents, then review is required and no authoritative promise is sent.
- Given missing customer-specific facts, then the draft asks for information rather than inventing it.
- Given duplicate send requests, then one durable delivery occurs.

### 7. Verification

- Unit-test answerability, claim coverage, contradiction/freshness, safety ordering, and gap deduplication.
- Add integration tests for grounded, partially grounded, ungrounded, sensitive, stale, conflicting, and inaccessible sources.
- Extend draft, safety, knowledge gap, outbox/delivery, retry, audit, and tenant-isolation tests.
- Add Web tests for citations, missing-information flows, review, send state, and safe errors.

### 8. Definition Of Done

Ben produces useful replies only when evidence supports them, creates actionable gaps when it does not, and cannot bypass deterministic safety or durable delivery.

---

## Prompt 4: Implement Customer Risk, Sentiment, SLA Prediction, And Escalation

### 1. Title And Outcome

Implement evidence-backed detection of complaint severity, churn, legal/privacy/security/reputational risk, and likely SLA breach, with deterministic escalation and cross-agent handoffs.

### 2. Current Context

- SLA policy/calendar, monitor, breach/risk events, notifications, case priority/status, refunds, and handoffs already exist.
- Support can detect basic sentiment/priority but lacks one versioned risk assessment combining thread evidence, customer history, SLA state, recurrence, and business impact.

### 3. Dependencies

- Prompts 1-3.
- Shared typed handoffs.

### 4. Implementation Requirements

- Define deterministic severe-risk indicators and escalation classes for legal, privacy, security, threats, reputational exposure, safety, high-value churn, refund/credit, and SLA breach.
- Compute SLA deadlines and risk windows only through existing SLA services; AI explains signals and proposes operator steps.
- Return risk types, severity, confirmed evidence, hypotheses, confidence, time-to-deadline, required role, allowed actions, and source IDs.
- Require immediate deterministic escalation for configured severe classes regardless of model confidence.
- Create idempotent tasks/notifications through existing services and typed handoffs to Finance for refund/credit/invoice disputes and Sales for retention/commercial risk.
- Minimize handoff evidence and do not expose unrestricted Support threads.
- Prevent blind automated customer promises, compensation, refunds, or legal/security responses.
- Add API/client and escalation brief/SLA-risk UI integrated with case detail and Support dashboard.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- AI cannot redefine SLA, legal/privacy/security policy, customer tier, or refund eligibility.
- Handoffs cannot grant access or perform the requested financial/commercial action.
- Preserve existing notification, refund approval, and SLA event authorities.

### 6. Acceptance Criteria

- Given a security/privacy signal, then mandatory escalation occurs even if AI confidence is low.
- Given an approaching SLA deadline, then deterministic time remaining matches SLA services exactly.
- Given a refund request, then one scoped Finance handoff/workflow is created and no refund is issued directly.
- Given churn language without evidence, then it is a hypothesis, not a confirmed outcome.

### 7. Verification

- Unit-test severe indicators, escalation precedence, SLA delegation, handoff schemas, evidence minimization, and idempotency.
- Add integration tests for complaint, churn, legal, privacy, security, refund, SLA risk/breach, retries, and tenant isolation.
- Extend SLA monitor, notification, refund, handoff, audit, API, and Web tests.
- Add UI tests for urgency, source evidence, restricted details, and action links.

### 8. Definition Of Done

Support risk is detected early and escalated through authoritative policies and scoped workflows. AI adds interpretation without changing deadlines, eligibility, or commitments.

---

## Prompt 5: Implement Related-Case Clustering And Root-Cause Analysis

### 1. Title And Outcome

Implement explainable duplicate/related-case clustering and recurring-issue analysis that proposes root causes and operational improvements from reviewed case outcomes.

### 2. Current Context

- Support analytics exposes root-cause insights, categories, resolution outcomes, reopen/escalation metrics, and learning effectiveness.
- Cases, messages, events, resolutions, and knowledge gaps provide evidence, but recurring-topic analysis is limited and must not expose customer content across scopes.

### 3. Dependencies

- Prompts 1-4.

### 4. Implementation Requirements

- Define deterministic candidate grouping by normalized product/category/error/reference/time window and privacy-safe features before AI comparison.
- Use AI only to explain similarity, distinguish duplicate versus related, and propose root-cause hypotheses from bounded redacted summaries.
- Require thresholds and human review before linking/merging operational views; never merge case histories or customers automatically.
- Produce cluster ID/version, member references, shared confirmed facts, differences, hypotheses, recurrence trend, impact, evidence, and recommended investigation.
- Link product defects, documentation gaps, or process issues to idempotent tasks/handoffs with minimum evidence.
- Version clusters as cases change and preserve historical membership decisions/audit.
- Extend analytics projections with recurrence, reopen, escalation, resolution, and knowledge-gap outcomes without opaque vanity scores.
- Add API/client and recurring-issue/root-cause view with member drill-down subject to access.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not send raw customer messages to analytics solely for clustering or copy unrestricted content into generic JSON.
- Similarity is not proof of root cause; hypotheses must remain labeled.
- Preserve cases as separate systems of record.

### 6. Acceptance Criteria

- Given truly duplicate inbound messages, then deterministic ingestion dedupe remains authoritative.
- Given related but distinct customers/issues, then clustering does not merge records or disclose one customer's details to another.
- Given weak similarity, then no confident cluster is created.
- Given reviewed root-cause correction, then the cluster is versioned and quality feedback is recorded.

### 7. Verification

- Unit-test candidate features, thresholds, redaction, membership versioning, and hypothesis labels.
- Add integration tests for duplicate, related, unrelated, sensitive, multi-customer, reopened, and cross-tenant cases.
- Extend Support analytics, case access, tasks/handoffs, audit, API, and Web tests.
- Add bounded-query/performance tests for large case sets.

### 8. Definition Of Done

Ben identifies recurring problems and reviewable root-cause hypotheses without merging records, leaking customer data, or presenting model similarity as fact.

---

## Prompt 6: Implement Knowledge Coverage, Documentation Advice, And Reviewed Learning

### 1. Title And Outcome

Implement analysis of knowledge coverage and stale documentation plus a governed post-resolution learning flow for reusable answers, resolution steps, and customer preferences.

### 2. Current Context

- Support knowledge gaps, context retrieval, document processing/indexing, memory observations, review/update jobs, safety policy, and shared memory candidates already exist.
- Resolutions can include reusable answers and preference observations, but Support-specific and shared memory governance must have one clear activation path.

### 3. Dependencies

- Prompts 1, 3, and 5.
- Shared governed memory candidates.

### 4. Implementation Requirements

- Measure answerability, retrieval misses, stale/conflicting sources, gap frequency, repeated questions, and resolution evidence through bounded company-scoped projections.
- Generate source-backed documentation recommendations with affected topic/product, missing/outdated information, case/gap evidence, impact, and proposed owner.
- Create idempotent documentation-gap tasks/handoffs; do not auto-edit authoritative documents.
- Adapt Support memory observations to the shared candidate lifecycle or one explicit adapter, preserving stricter Support sensitivity/reuse rules and avoiding duplicate active memory.
- Propose memory only from reviewed/resolved work with evidence, bounded content, scope, sensitivity, retention, confidence, and contradiction checks.
- Require review before activation and ensure rejected/expired/superseded candidates never enter retrieval.
- Link knowledge-gap closure to processed/indexed replacement sources and re-evaluate affected answerability.
- Add API/client and focused knowledge/learning queues plus quality/outcome metrics.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Never store credentials, payment data, special-category personal data, unrestricted conversations, or hidden prompts as memory.
- Do not maintain conflicting Support and shared active-memory paths.
- Only processed/indexed accessible documents can close a grounding gap.

### 6. Acceptance Criteria

- Given repeated unanswerable cases, then one deduplicated documentation recommendation is linked to evidence.
- Given an approved Support memory candidate retried, then exactly one active memory item exists.
- Given rejected/expired memory, then retrieval never includes it.
- Given a gap marked resolved with an unprocessed document, then answerability remains incomplete.

### 7. Verification

- Unit-test coverage aggregation, stale/conflict rules, task dedupe, sensitivity, retention, contradiction, and activation idempotency.
- Add integration tests for gap creation/closure/re-evaluation, memory review/activation/expiry, Support policy preservation, and tenant isolation.
- Extend knowledge retrieval, document ingestion, memory, background job, audit, API, and Web tests.
- Add review-queue UI tests and screenshot verification if substantial.

### 8. Definition Of Done

Support learning is evidence-backed, reviewed, deduplicated, scoped, and expirable. Documentation improvements are actionable while unsafe model output never becomes knowledge or memory automatically.

---

## Prompt 7: Implement Ben's Governed Operating Cadence And Support Cockpit

### 1. Title And Outcome

Implement Ben's continuous/daily/weekly operating cadence and consolidate queue priorities, drafts, SLA risk, escalations, recurring issues, knowledge gaps, handoffs, and quality evidence into the Support manager cockpit.

### 2. Current Context

- `SupportOperationsBackgroundService` already polls routing, SLA, delivery, and memory work.
- Shared briefings, priorities, plans, handoffs, memory, and quality exist.
- Support dashboards/case surfaces expose operations and analytics but do not coordinate all role-specific AI outputs as one idempotent manager cadence.

### 3. Dependencies

- Prompts 1-6 and shared AI prompts 4-10.

### 4. Implementation Requirements

- Define versioned continuous/daily/weekly cadence manifests with prerequisites, bounded batches, idempotency windows, and outputs.
- Continuous: intake/routing, triage, answerability/draft/safety, delivery dispatch, SLA monitoring, and critical escalation.
- Daily: prioritized queue, drafts awaiting review, missing information, SLA risks, refunds/approvals, handoffs, failed delivery/provider work, and knowledge gaps.
- Weekly: recurring issues, root-cause hypotheses, SLA/FCR/reopen/escalation trends, knowledge coverage, documentation backlog, and reviewed-learning outcomes.
- Orchestrate existing Support services and Prompts 2-6 without duplicate cases, executions, drafts, sends, tasks, gaps, handoffs, or memory candidates.
- Build/extend the Support cockpit with operational priorities, evidence, drafts, risk, SLA, approvals, handoffs, integration health, outcomes, and quality sample size.
- Surface provider/configuration failures, stale knowledge, policy blocks, retry/reconciliation state, and human decisions plainly.
- Allow explicit feedback and autonomy-review recommendation only; never auto-raise autonomy or weaken reply safety.
- Audit material outcomes and emit safe technical metrics.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Scheduled work cannot autonomously send insufficiently grounded replies or perform refunds, credits, cancellations, compensation, or sensitive changes.
- Register workers/services once in `AddSupportModule` and use existing background execution patterns.
- Use screenshot-first workflow for substantial cockpit changes.

### 6. Acceptance Criteria

- Given repeated polling or duplicate events, then one logical case/execution/draft/send/task/gap output exists.
- Given missing AI credentials, deterministic routing, SLA, delivery, and safety continue where applicable and AI states fail visibly.
- Given severe risk, then the cockpit shows authoritative escalation and linked work.
- Given insufficient quality evidence, then autonomy remains unchanged.

### 7. Verification

- Unit-test cadence selection, prerequisites, idempotency, bounded batching, and briefing contribution mapping.
- Add background retry/concurrency, safety, delivery, approval, handoff, memory, audit, and tenant-isolation tests.
- Add cockpit API projection and Web tests for loading, empty, stale, blocked, approval, failure, and drill-down states.
- Run Support, shared-AI, briefing, workflow, approval, Finance/Sales handoff, API, and Web suites plus responsive browser checks.

### 8. Definition Of Done

Ben operates a durable Support cadence and a trustworthy action cockpit. Replies remain grounded and safe, cross-agent work is scoped, learning is reviewed, and reliability is measurable before autonomy changes.
