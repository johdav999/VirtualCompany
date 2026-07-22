# AI-Supported Agent Capabilities

## Purpose

This assessment describes useful AI-supported advice, analysis, orchestration, and automation for Virtual Company's three core agents:

- Laura, Finance Manager
- Alex, Sales Manager
- Ben, Support Manager

The goal is not to make each agent an unrestricted chatbot. Each agent should operate as a governed business role that combines authoritative company data, deterministic calculations and policies, AI-supported interpretation, explicit tools, durable workflows, approvals, and audit evidence.

## Recommended Operating Model

Use one shared AI orchestration subsystem for all agents. Agent differences should come from configuration: role brief, company brief, objectives, tools, data scopes, autonomy, approval thresholds, communication profile, memory, and escalation rules.

For every AI-supported action, the system should:

1. Resolve the company, user, and addressed agent.
2. Load only permitted company and agent context.
3. Retrieve relevant records, indexed documents, policies, and bounded memory.
4. Run authoritative deterministic calculations and eligibility policies first.
5. Ask the model for a structured interpretation, recommendation, or plan where judgment adds value.
6. Validate model output against schemas, policy, permissions, and current record state.
7. Execute only explicit tools allowed for that agent and autonomy level.
8. Route sensitive or high-impact actions through approval.
9. Persist source references, confidence, rationale summary, tool attempts, workflow state, and audit evidence.
10. Surface uncertainty, missing evidence, failures, and required human decisions plainly.

Models must not become the source of truth for money, ledger balances, tax treatment, customer commitments, contract terms, inventory, payment state, or workflow eligibility. They may explain and synthesize authoritative results, but they must not invent them.

## Shared AI Capabilities

### Grounded question answering

All agents should answer company-specific questions using current records and approved indexed documents. Answers should include source references and distinguish confirmed facts, inferences, and missing information. Insufficient grounding should create a review requirement or knowledge gap instead of a confident answer.

### Briefing and summarization

Agents should produce role-specific daily, weekly, and event-driven briefings. Briefings should explain what changed, why it matters, recommended actions, decisions required, and source records. Users should be able to drill from every material statement to the underlying record.

### Prioritization

AI can rank work using deterministic risk signals plus business context. The result should include urgency, impact, confidence, due date, dependencies, and a concise reason. Policy-defined deadlines, SLAs, approval status, and monetary thresholds remain authoritative.

### Planning and task decomposition

Agents can turn an objective into a bounded plan of tasks, handoffs, approvals, and follow-ups. Plans must use known tool schemas and workflow definitions, not free-form autonomous loops. Every task should have an owner, expected outcome, evidence, and completion state.

### Exception interpretation

AI is useful for explaining anomalies, contradictory records, ambiguous emails, low-confidence matches, and failed workflows. It should propose likely causes and next diagnostic steps while clearly marking hypotheses.

### Cross-agent orchestration

Typed handoffs should allow agents to request work from one another without sharing unrestricted context. A handoff should contain the business objective, linked records, permitted evidence, requested outcome, due date, and escalation path.

### Learning and memory

Agents can propose bounded memory candidates from completed work, such as customer preferences, supplier coding patterns, successful resolution steps, or sales objections. Deterministic policy should validate scope, sensitivity, duplication, retention, and whether human review is required before memory becomes active.

## Laura: Finance Manager

### Advice and recommendations

- Recommend which supplier bills to pay, defer, dispute, or negotiate, with cash impact and due-date evidence.
- Recommend collection priorities and reminder strategies for overdue customers.
- Suggest ledger accounts, VAT handling, dimensions, accruals, prepayments, and corrections, subject to deterministic accounting validation and approval.
- Explain cash risks, working-capital pressure, runway changes, budget variance, and forecast uncertainty.
- Recommend approval decisions for payment proposals, expenses, refunds, credits, and finance-related handoffs.
- Propose month-end actions, missing documents, reconciliations, and close sequencing.

### Analysis

- Cash-position and short-term liquidity analysis.
- Rolling cash forecast with scenarios and confidence ranges.
- Accounts-payable aging, concentration, duplicate-payment, and payment-timing analysis.
- Accounts-receivable aging, collection probability, dispute, and bad-debt risk analysis.
- Actual-versus-budget and actual-versus-forecast variance explanation.
- P&L, balance-sheet, and account-movement narrative based on deterministic statement data.
- Transaction, supplier bill, invoice, and payment anomaly interpretation.
- Supplier price, frequency, coding, and payment-pattern analysis.
- Customer profitability, margin, payment behavior, and revenue-quality analysis.
- Month-end close readiness and unresolved-control assessment.

### Orchestration

- Daily cash, payables, receivables, and anomaly review.
- Weekly payment-plan, collections-plan, and forecast review.
- Monthly reconciliation, close-readiness, statement review, and management briefing.
- Supplier invoice intake, extraction, duplicate checks, enrichment, approval, Fortnox synchronization, and expense posting.
- Payment proposal creation, approval, export/provider execution, retry, and reconciliation.
- Overdue customer reminder sequences with Sales escalation for strategic accounts.
- Refund, credit, and disputed-invoice workflows with Support and Sales.
- Won-deal handoff from Sales through invoice readiness and approved accounting-system draft creation.

### Safe automation

Laura may automatically classify, summarize, reconcile low-risk records, create tasks, request missing documents, and prepare drafts. Money movement, accounting-system writes, posting, unusual credits, refunds, write-offs, and policy exceptions require deterministic eligibility checks and configured approval. External writes must use durable, idempotent execution with retry and reconciliation.

### High-value outputs

- Today's finance priorities
- Recommended payment run
- Collections plan
- Cash-risk and runway briefing
- Budget and forecast variance report
- Month-end close checklist and readiness score
- CEO finance decision brief

## Alex: Sales Manager

### Advice and recommendations

- Recommend next-best actions for leads, contacts, accounts, and deals.
- Suggest qualification questions, discovery agendas, follow-up timing, and stakeholder strategy.
- Recommend deal-stage changes, probability adjustments, close plans, and risk mitigations.
- Suggest relevant products, packages, positioning, and approved commercial language from the product catalog and policies.
- Recommend campaign audiences, channel mixes, outreach sequences, and experiment designs.
- Advise when to involve Finance, Support, delivery, or management.
- Propose discount or commercial-term requests while respecting approval thresholds.

### Analysis

- Lead fit, intent, engagement, and buying-role analysis.
- Account research summaries built from permitted company and external-source data.
- Pipeline health, stage conversion, velocity, slippage, and coverage analysis.
- Forecast analysis with commit, best-case, downside, and uncertainty explanations.
- Deal-risk analysis covering inactivity, missing stakeholders, weak qualification, pricing friction, competition, and payment risk.
- Win/loss, objection, competitor, and message-effectiveness analysis.
- Campaign conversion, source quality, cost, and attribution analysis.
- Customer expansion, cross-sell, renewal, and churn-opportunity signals.
- Revenue quality analysis with Finance-provided payment and margin context.

### Orchestration

- New lead ingestion, deduplication, enrichment, scoring, routing, and follow-up task creation.
- Inbound email and web-enquiry classification into contacts, leads, opportunities, or service requests.
- Account research and structured sales-brief generation.
- Outreach plan preparation, approval, scheduled delivery, reply monitoring, and follow-up.
- Opportunity progression with stage requirements, stale-deal alerts, and close-plan tasks.
- Proposal and quote preparation using approved products, pricing rules, and terms.
- Won-deal handoff to Laura with customer, invoice, tax, currency, payment-term, and supporting-document context.
- At-risk-customer handoff to Ben or Laura when support issues or payment friction affect a deal.

### Safe automation

Alex may enrich records, score and prioritize leads, summarize interactions, create follow-ups, and prepare outreach drafts. External prospecting, bulk email, pricing commitments, discounts, contract terms, and CRM/provider writes must follow consent, communication, commercial, approval, idempotency, and audit rules. AI must never invent customer facts or claim unsupported product capabilities.

### High-value outputs

- Today's sales priorities
- Lead and account research brief
- Deal strategy and mutual action plan
- Pipeline-risk report
- Forecast narrative and scenarios
- Campaign optimization recommendations
- Won/lost deal learning summary

## Ben: Support Manager

### Advice and recommendations

- Recommend issue classification, priority, assignment, response, and next action.
- Suggest source-backed answers using approved product, policy, FAQ, and procedure documents.
- Recommend whether a case can be resolved, needs more information, should be escalated, or requires approval.
- Suggest customer-sensitive tone based on sentiment, history, severity, and company communication policy.
- Recommend refunds, credits, replacements, compensation, or retention actions within explicit policy boundaries.
- Identify documentation gaps, product defects, recurring confusion, and operational improvements.

### Analysis

- Intent, category, urgency, sentiment, and customer-impact analysis.
- Customer, account, order, invoice, subscription, and previous-case matching.
- Answerability and grounding-confidence assessment.
- SLA-risk and breach prediction.
- Complaint, churn, legal, privacy, security, and reputational-risk detection.
- Duplicate and related-case clustering.
- Root-cause and recurring-topic analysis across resolved cases.
- Reply-quality, first-contact-resolution, reopen, escalation, and customer-effort analysis.
- Knowledge coverage and stale-document analysis.
- Customer-memory candidate extraction from reviewed outcomes.

### Orchestration

- Connected mailbox and web-enquiry intake, routing, deduplication, and case creation.
- Customer/context resolution and case triage.
- Knowledge retrieval, answerability checks, reply drafting, safety evaluation, and review.
- Low-risk approved reply delivery through the durable outbound dispatcher.
- Missing-information requests and customer follow-up scheduling.
- SLA monitoring, breach alerts, and escalation workflows.
- Refund, credit, cancellation, and invoice-dispute handoff to Laura through approval-backed workflows.
- Product defect, documentation gap, and sales-retention handoffs with linked evidence.
- Resolution follow-up, memory proposal, and knowledge-gap creation.

### Safe automation

Ben may classify cases, summarize threads, retrieve knowledge, draft replies, request routine missing information, create internal tasks, and update low-risk case state. Refunds, credits, cancellations, compensation, sensitive-data changes, legal/privacy/security matters, unusual promises, and high-impact outbound messages require policy checks and usually human approval. A reply must not be sent autonomously without sufficient trusted grounding and deterministic safety approval.

### High-value outputs

- Prioritized support queue
- Source-backed reply draft
- Escalation brief
- SLA-risk report
- Customer-risk and churn alert
- Recurring-issue and root-cause report
- Knowledge-gap and documentation-improvement backlog

## Cross-Agent Scenarios

### Sales to Finance

Alex submits a won-deal handoff. Laura validates invoice readiness, payment terms, tax and currency context, customer payment risk, and approval requirements before an accounting-system draft is created.

### Support to Finance

Ben identifies a refund, credit, duplicate charge, or invoice dispute. Laura verifies financial records and policy eligibility, creates or requests the appropriate approval, and returns the confirmed outcome to Ben for customer communication.

### Finance to Sales

Laura identifies overdue payment or deteriorating payment behavior on a strategic account. Alex receives a scoped handoff with the financial facts, relationship context requested, and recommended commercial follow-up.

### Support to Sales

Ben detects churn language, repeated dissatisfaction, expansion interest, or a commercially important blocker. Alex receives a source-linked retention or opportunity handoff without unrestricted access to the support record.

### Joint executive briefing

Laura, Alex, and Ben contribute structured sections to one executive briefing: financial risk, revenue outlook, customer health, decisions required, cross-functional dependencies, and actions already completed. Conflicts and uncertainty must remain visible rather than being merged into false certainty.

## Autonomy Model

| Level | Agent behavior |
|---|---|
| Assist | Summarize, analyze, recommend, and draft. A person performs all business actions. |
| Low-risk automation | Classify, enrich, prioritize, create tasks, request routine information, and update reversible internal state. |
| Standard execution | Execute approved routine tools within policy, confidence, and value thresholds. |
| Controlled execution | Perform bounded external actions with durable execution, reconciliation, and approval where configured. |
| High autonomy | Run the role's operating cadence independently while escalating exceptions, sensitive actions, low-confidence cases, and threshold breaches. |

Autonomy should be configured per tool and action, not as one unrestricted agent-wide switch.

## Recommended Implementation Order

1. Complete trusted agent briefing and indexed document grounding for all three agents.
2. Publish a capability registry showing which behaviors are deterministic, AI-assisted, approval-gated, or unavailable.
3. Standardize structured AI output, confidence, source references, validation, audit, and safe fallback in the shared orchestration layer.
4. Implement role-specific read-only analysis and explainability before adding more execute tools.
5. Add daily and weekly scheduled operating cadences that create durable tasks, insights, and briefings.
6. Implement typed cross-agent handoffs for won deals, payment risk, refunds, disputes, churn risk, and knowledge gaps.
7. Expand policy-gated tools and approval-backed external actions incrementally.
8. Add role-specific manager cockpits showing priorities, recommendations, approvals, exceptions, handoffs, integration health, and agent activity.
9. Measure recommendation acceptance, correction rate, grounding quality, action outcomes, and business impact.
10. Increase autonomy only where production evidence shows reliable, explainable, recoverable behavior.

## Success Measures

- Percentage of recommendations with valid source evidence
- Human acceptance and correction rates by recommendation type
- False-positive and unsupported-claim rates
- Time saved per finance, sales, and support workflow
- Workflow completion, retry, reconciliation, and exception rates
- Approval turnaround and prevented-policy-bypass counts
- Forecast accuracy, collection improvement, and close-cycle reduction
- Pipeline conversion, velocity, forecast quality, and follow-up completion
- First-contact resolution, SLA attainment, reopen rate, and support backlog reduction
- Knowledge-gap closure and repeated-issue reduction
- Cross-agent handoff completion and escalation quality
- Tenant-isolation, authorization, audit-completeness, and external-action idempotency test coverage

## Bottom Line

Virtual Company already has substantial deterministic finance, sales, support, workflow, approval, knowledge, integration, and audit foundations. The highest-value AI investment is not a set of independent chatbots. It is a shared, governed reasoning and orchestration layer that helps Laura, Alex, and Ben interpret authoritative data, recommend next actions, coordinate durable work, and explain decisions while deterministic policies and approved tools retain control of business state and external effects.
