# Virtual Company Gap Analysis

Sources:
- `target.md`
- `current.md`

Assessment basis: this compares the target roadmap against the saved current implementation assessment. It is not a runtime verification, test result, or production-readiness certification.

## Executive Summary

Virtual Company already has a broad platform foundation: multi-tenant company records, agents, tasks, workflows, approvals, finance, Fortnox integration, mailbox ingestion, sales, knowledge documents, memory, audit, notifications, briefings, executive dashboard, and mobile companion endpoints.

The largest gaps are therefore not basic platform existence. The largest gaps are:
- clear end-to-end completeness for each workflow
- production verification
- support-specific case management
- deeper AI-backed agent execution
- explicit customer support autonomy workflows
- stronger distinction between backend capability and complete user-facing UX
- complete integration readiness, credentials, scopes, and operational setup

## Gap Status By Roadmap Phase

| Roadmap area | Current evidence | Gap status | Main gaps |
|---|---|---|---|
| Phase 1: SaaS foundation and onboarding | Companies, memberships, invitations, selected company context, onboarding, dashboard, notes, role-aware access, observability basics. | Mostly present | Runtime validation, full production auth readiness, health dependency completeness, and confirmation that every tenant query is consistently scoped. |
| Phase 2: AI workforce setup and governance | Agent roster, templates, profiles, scheduling, direct chat, task links, tool permissions, approval thresholds, escalation rules, working hours, autonomy settings. | Strong foundation | Need verify policy is enforced on every action path, not only modeled. Need clearer support/department templates and full UX for configuration/history. |
| Phase 3: Knowledge, memory, and grounded context | Document upload, local storage, text extraction, chunking, embeddings/indexing, semantic search, grounded context retrieval, company/customer memory. | Strong foundation | Need verify source attribution end-to-end, access-scope enforcement, re-ingestion/versioning behavior, memory retention controls, and production object storage/scanning. |
| Phase 4: Tasks, workflows, approvals, reliable execution | Work tasks, subtasks, status updates, execution, workflow definitions/versions/triggers/instances/exceptions, approvals, background workers. | Strong foundation | Need prove complete execution lifecycle, retry/idempotency behavior, approval expiry/cancellation handling, and visible exception UX. |
| Phase 5: Agent communication and orchestration | Direct chat, conversations/messages, task links, tool execution records, manager-worker collaboration, briefings, orchestration service. | Partial to strong | Agent behavior appears partly deterministic. Need verify LLM-backed orchestration where intended, tool-call policy coverage, bounded multi-agent execution, and consistent chat-to-task conversion. |
| Phase 6: Executive cockpit, auditability, alerts, mobile | Dashboard focus, finance snapshot, briefing summary, executive cockpit widgets, audit list/detail, notifications, activity feed, mobile summary/briefing/notification/chat endpoints. | Strong foundation | Need confirm dashboard drilldowns, audit source/rationale completeness, mobile sign-in/company selection parity, and production-ready notification delivery. |
| Customer support agent | Some enabling pieces exist: mailbox ingestion, sales contacts/customer companies, customer memory, tasks, workflows, approvals, knowledge, notifications, audit, finance records. | Major product gap | No clear support-specific ticket/case domain, support inbox, SLA model, support classifications, support workflows, autonomous customer replies, refund support flow, support escalation routing, or support dashboard. |

## What Already Matches The Target Well

| Target capability | Current implementation evidence |
|---|---|
| Multi-tenant business platform | Company, membership, invitation, selected company context, role-aware access. |
| Agent management | Agent templates, roster, profiles, scheduling, direct chat, autonomy settings, permissions, thresholds. |
| Work tracking | Tasks, subtasks, task status, task execution, task links from chat. |
| Workflow orchestration | Definitions, versions, triggers, instances, exceptions, review handling, scheduled/condition-triggered execution. |
| Approval control | General approvals, finance approvals, approval tasks, notification-linked decisions, auditability. |
| Finance operations and analysis | Invoices, bills, supplier bills, payments, counterparties, transactions, ledger entries/lines, balances, budgets, forecasts, financial statement snapshots, anomalies, insights. |
| Fortnox integration | OAuth, token storage, sync history, external references, sync state, outbound write-command tracking, approval-oriented write flow. |
| Mailbox integration | Gmail/Microsoft 365 connection, OAuth callbacks, mailbox scanning, message/attachment snapshots, ingestion runs. |
| Knowledge and memory | Company documents, chunking, embeddings, semantic search, grounded context, company/customer memory profiles. |
| Briefings and dashboard | Daily/weekly briefing records, preferences, update jobs, dashboard focus, executive cockpit widgets, KPIs. |
| Audit/activity/notifications | Audit events, activity feed, SignalR hub, notification inbox, unread/actioned states, proactive messages. |
| Mobile companion foundation | Mobile summary, mobile briefing, mobile notification inbox, mobile chat-related endpoints. |

## Major Functional Gaps

### 1. Customer Support Agent Domain Is Not Explicit Enough

Target expectation:
- A support agent should monitor inbound customer messages, create support cases, classify issues, prioritize, respond, escalate, track SLAs, and learn from outcomes.

Current evidence:
- The platform has mailbox ingestion, sales/customer records, memory, tasks, workflows, approvals, notifications, audit, and finance records.
- These are strong building blocks, but they are not the same as a support product.

Gap:
- No clearly identified support ticket/case aggregate.
- No explicit support queue/inbox.
- No SLA model.
- No support category/status/priority model.
- No customer support lifecycle: new, triaged, waiting for customer, waiting internal, escalated, resolved, reopened.
- No support-specific escalation routing.
- No support dashboard or support agent workspace.
- No support-specific automation policies.

Recommended target work:
- Add a `SupportCase` domain with tenant scope.
- Add `SupportMessage`, `SupportCaseEvent`, `SupportCaseAssignment`, `SupportSla`, and `SupportCaseResolution` concepts.
- Add support inbox UI and APIs.
- Add mailbox-to-support-case ingestion.
- Add support case classification and routing.

### 2. Autonomous Customer Replies Are Not Proven

Target expectation:
- The support agent can draft and, when policy allows, send low-risk customer replies.

Current evidence:
- Sales email processing exists.
- Mailbox provider delivery appears to exist for sales/outbound flows.
- Agent orchestration exists.

Gap:
- No clear support reply draft/send workflow.
- No explicit confidence threshold for support replies.
- No support-specific approval queue for customer-facing messages.
- No policy distinction between internal note, draft reply, and external customer reply.

Recommended target work:
- Add support response draft records.
- Add response approval/review state.
- Add send-via-mailbox action behind policy.
- Add support-specific tone and knowledge-source requirements.
- Add audit trail for customer-facing responses.

### 3. Support Actions Are Not Yet Tool-Modeled

Target expectation:
- Support agent can safely execute support actions: ask for missing info, update status, resend invoices, share tracking links, create internal tasks, request refunds/credits, escalate.

Current evidence:
- The platform has tasks, approvals, finance records, invoices, notifications, and workflows.

Gap:
- These actions are not represented as a coherent support toolset.
- No clear support tool registry actions such as `CreateSupportCase`, `ClassifySupportCase`, `DraftSupportReply`, `SendSupportReply`, `EscalateSupportCase`, `RequestRefundApproval`, `CreateBugReportTask`, `MarkCaseResolved`.

Recommended target work:
- Define typed support tools.
- Enforce policy per support action.
- Persist tool execution metadata and linked support case events.

### 4. Support Knowledge Gaps Are Not First-Class

Target expectation:
- When the support agent cannot answer confidently, it should flag missing or unclear help content.

Current evidence:
- Knowledge documents and semantic retrieval exist.

Gap:
- No explicit `KnowledgeGap` or support knowledge improvement workflow.
- No loop from unresolved support cases to documentation tasks.

Recommended target work:
- Add knowledge-gap records linked to support cases and failed retrievals.
- Add workflow to create documentation tasks.
- Track repeated unresolved categories.

### 5. SLA And Follow-Up Management Are Missing

Target expectation:
- Support agent tracks pending customer responses, promised callbacks, missed SLAs, unresolved cases, and overdue internal handoffs.

Current evidence:
- Tasks and notifications exist.

Gap:
- No support SLA policy model.
- No case due timers or breach detection specific to support.
- No customer-waiting/internal-waiting state automation.

Recommended target work:
- Add SLA rules per company/customer tier/category.
- Add scheduled worker for SLA risk and breach detection.
- Add notifications/escalations for overdue support cases.

### 6. Refund/Credit/Compensation Flow Needs Support-Specific Integration

Target expectation:
- Support agent can validate refund policy, recommend refund/credit, and request approval.

Current evidence:
- Finance approvals, invoices, bills, payments, Fortnox write flows, and approval infrastructure exist.

Gap:
- No support-originated refund/credit request workflow is identified.
- No support policy thresholds for refund autonomy.
- No direct link from customer complaint/support case to finance approval and Fortnox action.

Recommended target work:
- Add `SupportRefundRequest` or use approval request with support-specific payload.
- Link support case, customer, invoice/payment, approval, and external finance action.
- Add thresholds by amount, customer tier, reason code, and confidence.

### 7. AI Usage Is Partial And Needs Clarification

Target expectation:
- Agents should use grounded context, model reasoning/output generation, policies, tools, and source-backed explanations.

Current evidence:
- AI is clearly used for sales email intent extraction, finance PDF/image OCR, embeddings, semantic retrieval, and grounded context.
- Many finance insight and agent behaviors appear deterministic/rule/workflow-based.

Gap:
- Not all agent responses are proven to be LLM-backed.
- Need clear boundary for which agent tasks use AI, deterministic logic, or both.
- Need policy that support replies must be source-backed and confidence-scored before autonomous send.

Recommended target work:
- Document AI usage per agent capability.
- Add support-specific classification, sentiment, summarization, and response drafting through approved AI abstractions.
- Persist confidence, source references, and action rationale.

### 8. Backend Capability May Exceed Complete UX

Target expectation:
- Roadmap features should be operationally usable from web/mobile surfaces.

Current evidence:
- Many backend APIs/entities/services exist.
- Current assessment notes some features may be backend-capable but not complete in frontend UX.

Gap:
- Need feature-by-feature verification of UI completeness.
- Need walkthroughs for onboarding, agent configuration, workflows, approvals, finance, support, and mobile.

Recommended target work:
- Create a route/API/UX readiness matrix.
- Mark each feature as backend-only, partial UI, complete UI, or verified end-to-end.

## Customer Support Agent Gap Detail

| Target support capability | Current enabling pieces | Gap |
|---|---|---|
| Inbox monitoring | Mailbox ingestion exists. | Need support-specific inbox, routing, and case creation from messages. |
| Ticket/case creation | Tasks exist. | Need support case model distinct from generic task. |
| Customer identification | Sales contacts/customer companies and customer memory exist. | Need matching support messages to customers, invoices, orders/subscriptions, and prior cases. |
| Triage/classification | Sales email intent extraction exists. | Need support issue taxonomy and support classifier. |
| Prioritization | Alerts/tasks exist. | Need sentiment, VIP, SLA, revenue impact, compliance risk scoring for support. |
| Knowledge retrieval | Company docs, semantic search, grounded context exist. | Need support-specific retrieval sources, confidence rules, and answerability checks. |
| Response drafting | Agent/chat infrastructure exists. | Need support reply draft entity and support reply generation flow. |
| Autonomous replies | Outbound email exists in sales context. | Need support send policy, approval gating, mailbox send action, and audit trail. |
| Action execution | Tasks, workflows, approvals, finance records exist. | Need typed support tools and support-specific workflow actions. |
| Approval-required actions | Approval framework exists. | Need support-specific approval payloads for refund, credit, cancellation, compensation, data changes. |
| Escalation | Escalations/notifications exist. | Need routing rules to support, finance, sales, technical, operations, management. |
| Follow-up management | Tasks/notifications exist. | Need support pending states, callback promises, SLA timers, and breach workers. |
| Sentiment/churn detection | Sales/customer memory exists. | Need support sentiment/churn classifier and retention workflow. |
| Root-cause analysis | Analytics/insights infrastructure exists. | Need recurring support issue clustering and trend reporting. |
| Customer memory | Customer memory profile exists. | Need support case outcomes and preferences written back to memory. |
| Audit trail | Audit exists. | Need support-specific audit events for reads, replies, status changes, escalations, approvals. |

## Suggested Implementation Roadmap For Closing Gaps

### Gap Release 1: Support Case Foundation

Goal: create the support domain backbone.

Deliverables:
- Support case aggregate.
- Support message records.
- Support case events.
- Support statuses, priority, category, source, and assignment.
- Tenant-scoped support APIs.
- Support inbox page.
- Mailbox message to support case ingestion.

### Gap Release 2: Support Agent Triage

Goal: let the support agent classify and prioritize cases.

Deliverables:
- Support issue taxonomy.
- AI/deterministic triage service.
- Customer/contact matching.
- Invoice/customer/sales context lookup.
- Sentiment, urgency, VIP, and SLA-risk scoring.
- Audit events for classification.

### Gap Release 3: Knowledge-Grounded Reply Drafting

Goal: let the support agent produce source-backed reply drafts.

Deliverables:
- Support reply draft entity.
- Knowledge retrieval tuned for support.
- Confidence and answerability checks.
- Source references attached to drafts.
- Human review UI for replies.
- Knowledge gap creation when confidence is low.

### Gap Release 4: Governed Autonomous Support Actions

Goal: allow safe support work to execute without human intervention.

Deliverables:
- Typed support tool actions.
- Policy checks per support action.
- Autonomous low-risk replies.
- Case status updates.
- Missing-information requests.
- Internal task creation for bug/product/operations handoff.
- Escalation routing.

### Gap Release 5: Refund, Credit, And Finance Handoff

Goal: connect support cases to finance approval and Fortnox-backed actions.

Deliverables:
- Support refund/credit request workflow.
- Approval thresholds by amount, reason, customer tier, and policy.
- Links between support case, invoice/payment, approval, and finance action.
- Finance handoff and audit trail.

### Gap Release 6: SLA, Follow-Up, And Support Analytics

Goal: make support operationally reliable.

Deliverables:
- SLA policy model.
- Follow-up timers.
- Breach detection worker.
- Overdue case notifications.
- Repeated issue clustering.
- Root-cause dashboards.
- Support memory updates from resolved cases.

## Risks And Open Questions

- Runtime completeness is unknown because the current assessment did not run the app or tests.
- Some target items may already exist deeper in code but were not proven in the saved current assessment.
- AI-enabled autonomy must be carefully limited until source attribution, confidence, and policy checks are reliable.
- Fortnox/customer finance actions depend on external scopes, licenses, and tenant configuration.
- Support requires a clear data model decision: generic tasks only, or dedicated support cases. A dedicated support case model is recommended.

## Practical Next Step

Create a feature readiness matrix for the support agent before implementation:

| Feature | Existing entity/API/UI | Reusable platform pieces | New work required | Risk |
|---|---|---|---|---|
| Support case inbox | TBD | Mailbox, tasks, notifications | Support case model and UI | High |
| Triage/classification | TBD | Sales email classifier pattern, AI abstraction | Support classifier and taxonomy | Medium |
| Reply drafting | TBD | Knowledge retrieval, agent orchestration | Draft entity, support prompts, review UI | Medium |
| Autonomous reply send | TBD | Mailbox send, policy, audit | Send action and approval policy | High |
| Refund request | TBD | Finance approvals, invoices, Fortnox | Support-to-finance workflow | High |
| SLA breach detection | TBD | Workers, notifications | SLA model and scheduled checks | Medium |

