# Customer Support Agent Implementation Prompts

Source: `gap.md`

Purpose: this document defines the ordered implementation prompts needed to close the customer support agent gaps. Use these prompts one at a time, in order. Each prompt should be treated as production implementation work, not prototype work.

Global instructions for every prompt:
- Read and follow `AGENTS.md`.
- Read and follow `docs/architecture-rules.MD`.
- Read and follow `production-implementation.md`.
- For UI work, also read and follow `ui-instructions.md` and `docs/design.md`.
- Use the current repository implementation as the source of truth.
- Keep the implementation modular and tenant-scoped.
- Use the existing database provider and EF Core conventions.
- Add migrations for schema changes.
- Add tests proportional to risk and blast radius.
- Do not add mock-only production paths.
- Do not call LLM APIs directly from feature modules. Use existing AI/orchestration abstractions or add approved abstractions in the appropriate module.
- Use backend policy checks for sensitive actions. Do not rely on prompts or UI checks for guardrails.
- Record audit events for important support actions.
- Use outbox/background workers for reliable side effects where applicable.

## Implementation Order

| Order | Prompt | Goal |
|---|---|---|
| 1 | Support domain foundation | Add support case, message, event, assignment, SLA policy, and resolution data model. |
| 2 | Support APIs and read models | Add tenant-scoped support case commands, queries, and controllers. |
| 3 | Support inbox UI | Add the web support inbox and case detail workspace. |
| 4 | Mailbox to support case ingestion | Convert inbound support mailbox messages into support cases/messages. |
| 5 | Customer and context matching | Link support cases to customers, contacts, finance records, sales records, and prior context. |
| 6 | Support triage service | Classify category, urgency, sentiment, SLA risk, and routing. |
| 7 | Knowledge-grounded reply drafting | Generate source-backed support reply drafts with confidence and answerability checks. |
| 8 | Reply review and send workflow | Add reply approval/review state and governed mailbox send action. |
| 9 | Support tool actions | Add typed support tools for case updates, missing-info requests, escalation, internal tasks, and resolution. |
| 10 | Refund and credit handoff | Connect support cases to finance approvals and Fortnox-capable finance actions. |
| 11 | SLA and follow-up workers | Add SLA breach/risk detection, follow-up timers, and overdue notifications. |
| 12 | Knowledge gap workflow | Track missing support knowledge and create documentation tasks. |
| 13 | Support analytics and root cause | Add trends, repeated issue detection, and operational dashboard metrics. |
| 14 | Support memory updates | Persist customer support preferences, outcomes, and promises into customer memory. |
| 15 | End-to-end hardening | Verify policy, audit, UX, tests, and runtime flow across the full support lifecycle. |
| 16 | Support mailbox transport mapping | Add explicit support mailbox send metadata and connection selection for real provider sends. |
| 17 | Support provider reply send execution | Send approved support replies through connected Gmail/Microsoft mailbox provider APIs with audit and failure state. |`n| 18 | Support knowledge and AI-orchestrated reply drafting hardening | Ground support drafts in tenant knowledge/context through approved orchestration boundaries. |`n| 19 | Support approval workflow integration for risky actions | Connect refund/credit and risky replies to approval/workflow records. |`n| 20 | Support background reliability for mailbox routing and SLA | Add recoverable background workers for support mailbox routing and SLA checks. |`n| 21 | Support tool policy integration hardening | Add explicit policy decisions and default-deny behavior for sensitive support tools. |`n| 22 | Support outcomes memory and knowledge gap automation | Automatically update memory and knowledge gaps from support outcomes. |

## Prompt 1: Support Domain Foundation

```text
Implement the customer support domain foundation.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Goal:
Create the persistent support case model needed for a governed customer support agent.

Scope:
- Add tenant-scoped support entities using existing Domain/Infrastructure/Application patterns.
- Add EF Core mappings and migration.
- Do not build UI in this prompt.
- Do not implement AI classification yet.

Required domain concepts:
- SupportCase
- SupportMessage
- SupportCaseEvent
- SupportCaseAssignment
- SupportSlaPolicy
- SupportCaseResolution

Required support case fields:
- CompanyId
- CaseNumber or human-readable reference
- Subject
- Summary
- Description
- Status
- Priority
- Category
- Source
- Customer/contact linkage fields where current data model supports it
- AssignedAgentId optional
- AssignedUserId optional
- CreatedUtc
- UpdatedUtc
- FirstResponseDueUtc optional
- ResolutionDueUtc optional
- LastCustomerMessageUtc optional
- LastInternalActivityUtc optional
- ClosedUtc optional

Required statuses:
- New
- Triaged
- WaitingForCustomer
- WaitingInternal
- Escalated
- AwaitingApproval
- Resolved
- Reopened
- Closed

Required priorities:
- Low
- Normal
- High
- Urgent

Required categories:
- GeneralQuestion
- Billing
- Refund
- TechnicalIssue
- AccountAccess
- Delivery
- Complaint
- FeatureRequest
- BugReport
- ChurnRisk

Required events:
- Created
- MessageReceived
- Triaged
- Assigned
- StatusChanged
- PriorityChanged
- ReplyDrafted
- ReplySent
- Escalated
- ApprovalRequested
- ApprovalResolved
- InternalTaskCreated
- Resolved
- Reopened
- Closed

Implementation requirements:
- All support entities must include CompanyId or equivalent tenant scope.
- Queryable state must be relational columns, not only JSON.
- Flexible metadata/payload snapshots may use JSON if consistent with repo conventions.
- Add indexes for CompanyId, status, priority, category, assigned agent/user, due dates, and created date.
- Add audit event creation for case creation and major lifecycle events if the repo has an established audit pattern at this layer. If audit is handled in later application services, document that in code/tests.

Verification:
- Add/extend unit tests for entity creation/status transitions where patterns exist.
- Add migration.
- Build the affected projects if practical.

Deliverable:
- Domain/application/infrastructure support foundation with migration and tests.
```

## Prompt 2: Support APIs And Read Models

```text
Implement tenant-scoped support case APIs and read models.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 1 completed.

Goal:
Expose production-ready backend support case operations for the web UI and future support agent tools.

Scope:
- Add application contracts, commands, queries, services, and API controllers for support cases.
- Do not implement mailbox ingestion or AI triage yet.
- Do not build UI in this prompt.

Required API capabilities:
- List support cases with filters: status, priority, category, assigned agent/user, customer/contact, search, created range, SLA risk.
- Get support case detail including messages, events, assignment, SLA fields, linked records, and resolution.
- Create support case manually.
- Add internal note/message.
- Change status.
- Change priority.
- Assign to agent.
- Assign to user.
- Add or update category.
- Reopen case.
- Resolve case.
- Close case.

Required safeguards:
- Every route must be company-scoped.
- Authorization must verify the user has access to the company.
- State transitions must be validated server-side.
- Invalid transitions must return safe validation errors.
- User-facing response models must use plain English labels where appropriate and avoid leaking internal enum names.
- Important actions must write business audit events.

Read model requirements:
- SupportCaseListItem
- SupportCaseDetail
- SupportCaseMessageDto
- SupportCaseEventDto
- SupportCaseSummaryCounts

Verification:
- Add API/application tests for tenant scoping, create, status change, assignment, resolve, and reopen.
- Verify unauthorized company access is forbidden/not found according to existing repo patterns.

Deliverable:
- Support backend API surface ready for UI and automation.
```

## Prompt 3: Support Inbox UI

```text
Implement the web support inbox and support case detail workspace.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md
- ui-instructions.md
- docs/design.md

Depends on:
- Prompt 2 completed.

Goal:
Make support cases operationally usable from the Blazor web app.

Scope:
- Add navigation entry for Support if consistent with current navigation patterns.
- Add support inbox page.
- Add support case detail page.
- Reuse existing design tokens, layout, cards, tables, badges, spacing, and interaction patterns.
- Do not introduce a new visual system.

Required pages:
- /support
- /support/cases
- /support/cases/{caseId}

Inbox requirements:
- Filter by status, priority, category, assigned to me/agent, SLA risk, and search text.
- Show case number, subject, customer/contact if known, category, priority, status, age, due indicator, assigned owner, and last activity.
- Show empty states for no cases and no matching filters.
- Provide actions for create case, assign, change priority, and open detail.

Detail requirements:
- Header with case number, subject, status, priority, category, assigned owner, and SLA due status.
- Customer/context panel.
- Message timeline.
- Internal events/audit-style activity.
- Internal note composer.
- Status/priority/category controls.
- Assign controls.
- Resolve/reopen/close actions.
- Placeholders for later reply drafts, approvals, and linked tasks without fake data.

UX rules:
- Use plain English.
- Do not expose internal enum names.
- Avoid cards inside cards.
- Ensure responsive layout for desktop and mobile web.

Verification:
- Run relevant frontend build if practical.
- Manually inspect routes if a local server is already supported by the repo workflow.
- Add component tests if the repo has an established pattern.

Deliverable:
- Support inbox and case detail UI wired to real APIs.
```

## Prompt 4: Mailbox To Support Case Ingestion

```text
Implement mailbox-to-support-case ingestion.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 2 completed.

Goal:
Turn inbound support mailbox messages into support cases and support messages.

Scope:
- Reuse existing mailbox ingestion infrastructure.
- Add support-specific routing rules.
- Do not implement AI triage yet.
- Do not send customer replies in this prompt.

Required behavior:
- Detect inbound mailbox messages that should create or update support cases.
- Create a new SupportCase when no matching open case exists.
- Add SupportMessage to an existing case when the inbound message matches a thread/case reference.
- Preserve mailbox message snapshot/source reference.
- Avoid duplicate case/message creation using provider message IDs and idempotency keys.
- Set source to Email.
- Set initial status to New.
- Set initial priority/category to default values until triage runs.
- Create SupportCaseEvent records.
- Create audit events for new case and received message.

Matching rules:
- Prefer explicit case reference in subject/body if present.
- Fall back to provider thread/conversation ID when available.
- Fall back to sender plus recent open cases if safe.
- Otherwise create a new case.

Operational requirements:
- Use background worker/outbox patterns where the repo already uses them.
- Do not block mailbox ingestion on long-running AI work.
- Log technical failures with correlation IDs and tenant context.

Verification:
- Add tests for new case, existing case update, duplicate message suppression, and cross-tenant isolation.

Deliverable:
- Connected mailbox messages can create/update support cases.
```

## Prompt 5: Customer And Context Matching

```text
Implement support customer and business-context matching.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 4 completed.

Goal:
Link support cases to known customers, contacts, finance records, sales records, and prior context.

Scope:
- Add application service for support context resolution.
- Reuse existing sales contacts/customer companies, finance invoices/payments/counterparties, and customer memory where available.
- Do not implement response drafting yet.

Required behavior:
- Match inbound sender email to contact/customer records.
- Link support case to customer/contact where confident.
- Find related invoices/payments for billing/refund categories when possible.
- Find related deals/customer memory profile if applicable.
- Find prior support cases for the same customer after Prompt 1/2 model exists.
- Store context links or references in a queryable/supportable way.
- Record confidence and reason for matches.
- Avoid unsafe automatic linking when confidence is low.

Required output:
- SupportCaseContextSummary read model.
- Linked customer/contact records.
- Related finance records.
- Related sales records.
- Related prior cases.
- Relevant customer memory references.

Verification:
- Tests for exact email match, ambiguous match, no match, finance linkage, and tenant isolation.

Deliverable:
- Support cases can show customer/business context and use it in later triage/reply flows.
```

## Prompt 6: Support Triage Service

```text
Implement support case triage and prioritization.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 5 completed.

Goal:
Classify support cases by category, urgency, sentiment, SLA risk, routing, and required next action.

Scope:
- Add a support triage application service.
- Use deterministic rules first where sufficient.
- Use approved AI abstractions only if needed and already available or added through the AI orchestration boundary.
- Do not call LLM APIs directly from support feature modules.

Required triage outputs:
- Category
- Priority
- Sentiment
- Urgency
- Churn risk flag
- VIP/revenue impact flag when data supports it
- SLA risk
- Suggested assignment route
- Suggested next action
- Confidence
- Rationale summary
- Source references

Required behavior:
- Run triage when a new support case is created from mailbox.
- Allow manual retriage from API.
- Update support case category/priority/status to Triaged when confident.
- Leave low-confidence cases as New or needs review.
- Create SupportCaseEvent and AuditEvent for triage.
- Create notification/escalation for urgent/high-risk cases if existing patterns support it.

Verification:
- Tests for billing, refund, complaint, technical issue, bug report, churn risk, low-confidence unknown, and tenant isolation.
- Tests must verify audit/event creation.

Deliverable:
- Support cases are automatically classified and prioritized in a governed way.
```

## Prompt 7: Knowledge-Grounded Reply Drafting

```text
Implement knowledge-grounded support reply drafting.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 6 completed.

Goal:
Let the support agent produce source-backed reply drafts without sending them automatically.

Scope:
- Add support reply draft entity/service/API.
- Use existing knowledge retrieval and grounded context services.
- Use approved AI/orchestration abstractions for draft generation.
- Do not send emails in this prompt.

Required data model:
- SupportReplyDraft
- CompanyId
- SupportCaseId
- DraftBody
- Tone
- Status
- Confidence
- Answerability
- RationaleSummary
- SourceReferences
- CreatedByAgentId optional
- CreatedByUserId optional
- CreatedUtc
- UpdatedUtc

Required statuses:
- Draft
- NeedsReview
- Approved
- Rejected
- Superseded

Required behavior:
- Generate draft from support case, messages, customer context, support category, and knowledge retrieval.
- Attach source references used to produce the answer.
- Compute or persist confidence and answerability.
- If knowledge is missing or confidence is low, mark draft NeedsReview and create/queue a knowledge gap in a later prompt if not implemented yet.
- Never include raw chain-of-thought.
- Use company tone/support policy where available.

API requirements:
- Generate draft.
- List drafts for case.
- Get draft.
- Edit draft.
- Approve draft.
- Reject draft.

Verification:
- Tests for source-backed draft, low-confidence draft, no-answer condition, edit/approve/reject flow, and tenant isolation.

Deliverable:
- Support agents/users can create reviewable, source-backed customer reply drafts.
```

## Prompt 8: Reply Review And Governed Send Workflow

```text
Implement support reply review and governed send workflow.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 7 completed.

Goal:
Allow approved support reply drafts to be sent through connected mailbox providers under policy control.

Scope:
- Reuse existing mailbox send/provider patterns where available.
- Add policy checks for customer-facing support replies.
- Add support reply sent records/events.
- Do not implement broad autonomous sending for all cases yet. Start with explicit approved send and safe low-risk policy path.

Required behavior:
- Send an approved SupportReplyDraft to the customer through the connected mailbox.
- Require approval/review for drafts that are low confidence, high risk, refund/legal/sensitive, or outside autonomy threshold.
- Allow autonomous send only when all conditions pass:
  - low-risk category
  - high confidence
  - answerable from approved sources
  - no sensitive finance/legal/customer-risk action
  - agent autonomy and policy allow it
- Add SupportMessage for sent reply.
- Add SupportCaseEvent ReplySent.
- Add AuditEvent with actor, target, outcome, rationale summary, sources, and correlation ID.
- Update case status to WaitingForCustomer or Resolved according to selected send action.
- Use outbox/background dispatch if mailbox send reliability requires it.

API requirements:
- Send approved draft.
- Send edited draft as human.
- Request approval for draft.
- Mark sent draft as failed/retryable if provider send fails.

Verification:
- Tests for approved send, blocked unapproved send, low-risk autonomous send, high-risk blocked send, provider failure, audit/event creation, and tenant isolation.

Deliverable:
- Support replies can be sent safely and audibly through connected mailbox infrastructure.
```

## Prompt 9: Support Tool Actions

```text
Implement typed support tool actions for the customer support agent.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 8 completed.

Goal:
Expose explicit, permissioned support tools that the support agent can use through the shared orchestration/tool execution system.

Scope:
- Add support tools to the existing tool registry/execution framework.
- Do not create a separate orchestration stack.
- Enforce policy before every execute action.

Required tools:
- CreateSupportCase
- ClassifySupportCase
- DraftSupportReply
- SendSupportReply
- AddSupportInternalNote
- UpdateSupportCaseStatus
- AssignSupportCase
- EscalateSupportCase
- RequestMissingInformation
- CreateBugReportTask
- CreateOperationsFollowUpTask
- MarkSupportCaseResolved
- ReopenSupportCase

Tool execution requirements:
- Company scope.
- Agent identity.
- Action type: read, recommend, or execute.
- Request payload.
- Response payload or failure.
- Policy decision.
- Timestamps.
- Correlation ID.
- Linked SupportCaseEvent where relevant.
- AuditEvent for important actions.

Policy requirements:
- Sending replies, resolving cases, escalation, and task creation must be permissioned.
- Default deny when agent policy is missing or ambiguous.
- Sensitive customer-facing actions require approval if thresholds or categories demand it.

Verification:
- Tests for allowed/denied tool execution, policy metadata persistence, audit creation, and tenant isolation.

Deliverable:
- Customer support agent can perform support work through explicit governed tools.
```

## Prompt 10: Refund, Credit, And Finance Handoff

```text
Implement support-originated refund, credit, and finance handoff workflow.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 9 completed.

Goal:
Connect refund/credit support cases to finance approvals and finance/Fortnox-capable actions.

Scope:
- Reuse existing finance approval and Fortnox write-command patterns.
- Do not bypass finance policy or Fortnox approval flow.
- Do not directly call Fortnox from support feature code.

Required capabilities:
- Create support refund/credit request from a support case.
- Link request to customer/contact, invoice/payment when available, support case, approval request, and finance action/write command when created.
- Validate refund/credit reason.
- Validate amount thresholds by company policy, customer tier, and agent autonomy.
- Create approval request when required.
- Update support case status to AwaitingApproval where appropriate.
- Notify finance approver using existing notification patterns.
- Record support case event and audit event.
- When finance approval is resolved, update linked support case and notify support/customer as appropriate.

Required data:
- SupportRefundRequest or equivalent support-specific approval payload.
- Amount
- Currency
- Reason code
- Explanation
- Linked invoice/payment
- RequestedByAgentId/UserId
- ApprovalRequestId
- FinanceActionReference optional
- Status

Verification:
- Tests for below-threshold recommendation, approval-required refund, rejected refund, approved refund handoff, missing invoice, tenant isolation, and audit creation.

Deliverable:
- Support can initiate refund/credit workflows safely through finance governance.
```

## Prompt 11: SLA And Follow-Up Workers

```text
Implement support SLA policy, follow-up timers, and breach detection workers.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 3 and Prompt 6 completed.

Goal:
Make support operationally reliable by tracking response/resolution due dates and escalating overdue cases.

Scope:
- Add support SLA policy configuration if not fully implemented in Prompt 1.
- Add background worker for SLA risk and breach checks.
- Add notifications/escalations for overdue support cases.

Required capabilities:
- Configure SLA by company, category, priority, customer tier, and business hours if current model supports it.
- Compute first response due date.
- Compute resolution due date.
- Detect nearing breach.
- Detect breached cases.
- Create SupportCaseEvent for SLA risk/breach.
- Create notification/escalation for assigned owner/agent/manager.
- Update support case read model fields for due status.
- Track promised follow-up time and customer waiting/internal waiting states.

Worker requirements:
- Tenant-aware.
- Idempotent.
- Uses correlation IDs.
- Does not duplicate notifications for the same breach state.
- Logs technical failures safely.

Verification:
- Tests for due date calculation, near-breach, breach, duplicate prevention, notification creation, and tenant isolation.

Deliverable:
- Support cases have reliable SLA and follow-up monitoring.
```

## Prompt 12: Knowledge Gap Workflow

```text
Implement support knowledge gap tracking and documentation task workflow.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 7 completed.

Goal:
When the support agent cannot answer confidently, create an actionable knowledge improvement loop.

Scope:
- Add support knowledge gap records.
- Link gaps to support cases, failed/low-confidence drafts, categories, and retrieval attempts where available.
- Create documentation tasks when gaps are repeated or manually promoted.

Required data:
- SupportKnowledgeGap
- CompanyId
- SupportCaseId optional
- SupportReplyDraftId optional
- Category
- QuestionSummary
- MissingInformationSummary
- RetrievalSourceSummary
- FrequencyCount
- Status
- CreatedUtc
- ResolvedUtc optional
- LinkedTaskId optional

Required statuses:
- Open
- LinkedToTask
- Resolved
- Ignored

Required behavior:
- Create a gap when draft generation has low answerability due to missing knowledge.
- Increment or group repeated gaps by similar category/question when safe.
- Allow user/agent to create documentation task from gap.
- Show gaps in support/admin UI or expose API for later dashboard.
- Record audit event when task is created from gap.

Verification:
- Tests for gap creation, repeated gap grouping, task creation, status changes, and tenant isolation.

Deliverable:
- Support unresolved questions feed back into company knowledge improvement.
```

## Prompt 13: Support Analytics And Root Cause

```text
Implement support analytics and root-cause insights.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md
- ui-instructions.md
- docs/design.md

Depends on:
- Prompts 1 through 12 completed.

Goal:
Provide operational visibility into support workload, recurring issues, SLA risk, and customer impact.

Scope:
- Add backend analytics/read models.
- Add support dashboard section or page using existing design system.
- Do not assemble expensive dashboard queries directly in UI components.

Required metrics:
- Open cases by status.
- Cases by category.
- Cases by priority.
- SLA at risk.
- SLA breached.
- Average first response time.
- Average resolution time.
- Reopened cases.
- Top recurring issue categories.
- High-risk customers.
- Refund/credit requests from support.
- Knowledge gaps by category.

Required insights:
- Repeated issue detection.
- Root-cause candidates by category/source/customer segment.
- Suggested documentation improvements.
- Suggested workflow improvements.

UI requirements:
- Add support dashboard widgets or a support analytics page.
- Use plain English labels.
- Provide drilldown to filtered case lists.
- Respect role-based access.

Verification:
- Tests for read model queries and tenant isolation.
- Build frontend/backend affected projects where practical.

Deliverable:
- Managers can monitor support health and recurring issue trends.
```

## Prompt 14: Support Memory Updates

```text
Implement customer support memory updates from resolved support cases.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 10 and Prompt 12 completed.

Goal:
Persist useful customer support context so future support, sales, and finance interactions avoid repeating mistakes.

Scope:
- Reuse existing customer memory profile and memory item patterns.
- Store summaries and preferences, not raw chain-of-thought.
- Respect privacy/deletion/retention behavior already present in the repo.

Required behavior:
- On support case resolution, identify memory-worthy facts:
  - customer communication preference
  - repeated issue
  - active promise
  - refund/credit sensitivity
  - support tone sensitivity
  - product/service constraint
  - unresolved risk
- Create or update customer memory records with validity and source references.
- Link memory back to support case/resolution.
- Avoid creating memory for sensitive information unless policy allows it.
- Allow manual opt-out/deletion through existing memory controls where applicable.

Verification:
- Tests for memory creation on resolution, no memory for sensitive disallowed data, update existing memory, source linkage, and tenant isolation.

Deliverable:
- Resolved support cases improve future customer context safely.
```

## Prompt 15: End-To-End Support Agent Hardening

```text
Perform end-to-end hardening for the customer support agent.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md
- ui-instructions.md
- docs/design.md

Depends on:
- Prompts 1 through 14 completed.

Goal:
Verify and harden the full support lifecycle from inbound email to resolved case, reply, approval, escalation, SLA tracking, audit, and memory update.

Scope:
- Do not add new large features unless required to close broken end-to-end flows.
- Focus on integration correctness, policy enforcement, user experience, observability, and tests.

Required end-to-end scenarios:
- Inbound email creates support case.
- Support case is matched to customer context.
- Triage classifies and prioritizes case.
- Reply draft is generated from knowledge with source references.
- Human approves/sends reply.
- Low-risk autonomous reply is sent only when policy allows.
- High-risk reply is blocked or routed to approval.
- Refund request creates finance approval and links back to case.
- SLA risk creates notification/escalation.
- Knowledge gap creates documentation task.
- Resolved case writes customer memory when allowed.
- Audit trail shows key actions, actor, target, outcome, rationale, and sources.

Hardening requirements:
- Verify tenant isolation for every support route/query/tool/worker.
- Verify policy checks before every execute action.
- Verify no raw chain-of-thought is stored or displayed.
- Verify user-facing labels are plain English.
- Verify failed external side effects are retryable or explicitly failed.
- Verify logs include correlation IDs and tenant context where applicable.
- Verify support UI handles empty, loading, error, and permission states.

Testing requirements:
- Add integration tests for the main lifecycle.
- Add targeted tests for policy denial and approval-required paths.
- Run affected test projects.
- Run build for affected backend/frontend projects where practical.

Deliverable:
- Customer support agent implementation is ready for product review as an end-to-end capability.
```

## Prompt 16: Support Mailbox Transport Mapping

```text
Implement support mailbox transport mapping for real outbound support replies.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 8 completed.
- Existing mailbox OAuth/provider abstractions available.

Goal:
Support reply sending must know which connected mailbox to use and which customer/thread metadata to reply to.

Scope:
- Reuse the existing mailbox connection/provider model.
- Do not create a separate email provider stack.
- Keep all data tenant-scoped.

Required behavior:
- Add support send request fields for optional MailboxConnectionId, ToEmail, ToDisplayName, Subject, OriginalMessageId, ProviderThreadId, and InternetMessageId.
- Resolve missing values from the support case and latest inbound support message where possible.
- Validate that the selected mailbox connection belongs to the company and is active.
- If no mailbox connection is explicitly selected, use the latest active company mailbox connection with encrypted credentials.
- Fail safely if recipient/thread metadata is missing.
- Keep approval/autonomous guardrails unchanged.

Verification:
- Build affected projects.
- Add/extend tests where the test project is buildable.

Deliverable:
- Support send commands carry enough transport metadata to perform a real provider reply.
```

## Prompt 17: Support Provider Reply Send Execution

```text
Implement real support reply send execution through connected mailbox providers.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 16 completed.

Goal:
When a support reply is approved and sent, dispatch it through the existing Gmail/Microsoft provider client instead of only recording a local outbound message.

Scope:
- Add an application abstraction for support outbound email sending.
- Implement the abstraction in infrastructure using IMailboxProviderRegistry and mailbox credential encryption.
- Reuse provider SendReplyAsync.
- Refresh mailbox access tokens when needed.
- Persist provider message/thread IDs on the support outbound message and support case.
- Record audit events for success, blocked sends, and provider failures.
- Preserve retryable failure details on the draft.
- Do not log or store mailbox tokens.

Required behavior:
- Approved human sends call the provider immediately through the service path.
- Low-risk autonomous sends may call the provider only when existing confidence/answerability guardrails pass.
- Provider failures must mark the draft failed with a safe summary and must not mark the case as waiting/resolved.
- Successful sends mark the draft sent, add an outbound support message, update first response sent timestamp, and transition status.

Verification:
- Build API and Web projects.
- Run support tests if the test project compiles; otherwise report unrelated compile blockers.

Deliverable:
- Support replies can be sent through real connected Gmail/Microsoft mailbox providers.
```

## Prompt 18: Support Knowledge And AI-Orchestrated Reply Drafting Hardening

```text
Implement real knowledge retrieval and AI-orchestration-safe support reply drafting.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 7 completed.

Goal:
Support reply drafts must use tenant-scoped knowledge, customer context, and an approved orchestration boundary instead of static templates only.

Scope:
- Reuse existing knowledge/memory/context retrieval abstractions where available.
- Do not call LLM APIs directly from support feature code.
- If no AI provider is configured, produce a deterministic grounded draft from retrieved context and mark confidence honestly.

Required behavior:
- Retrieve tenant-scoped support knowledge, customer memory, prior case context, and linked business records before drafting.
- Include source references in the draft read model.
- Compute answerability from retrieved evidence and message completeness.
- Create or increment a support knowledge gap when answerability is too low.
- Audit draft generation with data sources used.
- Preserve no-chain-of-thought behavior.

Verification:
- Build affected projects.
- Add tests for grounded source references and low-answerability knowledge gap creation when practical.
```

## Prompt 19: Support Approval Workflow Integration For Risky Actions

```text
Implement proper approval workflow integration for support-originated refund/credit and risky support actions.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompts 8, 10, and 17 completed.

Goal:
Risky support actions should create real approval records/tasks using existing approval/workflow patterns rather than only changing support status.

Scope:
- Reuse existing approval request/task entities and services where available.
- Do not directly execute Fortnox or finance side effects from support code.
- Keep support case links and audit trail.

Required behavior:
- Refund/credit requests create an approval-linked work item or approval request.
- Risky reply sends are blocked or routed to approval before provider dispatch.
- Approval references are persisted on support refund requests when possible.
- Support case status moves to Awaiting approval while approval is pending.
- Audit events explain why approval is required.

Verification:
- Build affected projects.
- Add tests for approval-required refund/risky reply when practical.
```

## Prompt 20: Support Background Reliability For Mailbox Routing And SLA

```text
Implement background reliability for inbound support mailbox routing and SLA monitoring.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompts 4 and 11 completed.

Goal:
Inbound support routing and SLA checks should run through recoverable background services instead of only manual API calls.

Scope:
- Reuse existing hosted service and scoped worker patterns.
- Keep workers tenant-aware and idempotent.
- Do not duplicate support messages or SLA breach events.

Required behavior:
- Add a hosted support SLA monitor worker with configurable interval.
- Add a support mailbox routing service that can scan recent inbound mailbox snapshots and enqueue/create support cases idempotently.
- Log failures with company/correlation context.
- Avoid blocking user requests on long-running scans.

Verification:
- Build affected projects.
- Add tests for idempotent worker/service behavior when practical.
```

## Prompt 21: Support Tool Policy Integration Hardening

```text
Implement full shared-policy-style guardrails for support tool execution.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompt 9 completed.

Goal:
Support tool execution must have explicit policy decisions, denied states, action classifications, and audit metadata before executing sensitive tools.

Scope:
- Reuse existing audit/tool execution concepts where available.
- Do not add a separate orchestration stack.
- Keep default-deny behavior for ambiguous sensitive actions.

Required behavior:
- Classify support tools as read, recommend, or execute.
- Deny sensitive execute tools when autonomous execution is requested without explicit policy allowance.
- Route risky reply/refund/resolve/escalation/task actions to approval or denial according to support policy.
- Persist policy decision metadata in audit events.
- Return structured denied/approval-required results.

Verification:
- Build affected projects.
- Add tests for denied autonomous sensitive tools and allowed safe tools when practical.
```

## Prompt 22: Support Outcomes Memory And Knowledge Gap Automation

```text
Implement automatic support memory updates and knowledge-gap creation from real outcomes.

Read and follow:
- AGENTS.md
- docs/architecture-rules.MD
- production-implementation.md

Depends on:
- Prompts 12 and 14 completed.

Goal:
Resolved cases and low-answerability drafts should automatically improve customer memory and support knowledge operations.

Scope:
- Reuse customer memory and work task patterns.
- Store concise summaries, not raw chain-of-thought.
- Respect tenant scope and sensitive categories.

Required behavior:
- When a case is resolved, update customer memory if a linked contact memory profile exists.
- When a draft has low answerability, create or increment a support knowledge gap automatically.
- When repeated knowledge gaps exceed a threshold, create or link a documentation task.
- Audit memory and task creation outcomes.

Verification:
- Build affected projects.
- Add tests for resolved-case memory update and low-answerability gap creation when practical.
```
## Notes For Future Prompt Refinement

- If a prompt discovers that a required concept already exists under another name, reuse the existing implementation and adapt the prompt scope instead of duplicating the model.
- If the support domain becomes too large for one implementation pass, split the prompt by backend, UI, and tests, but keep the same order.
- If Fortnox or mailbox provider credentials are unavailable locally, implement and test against existing provider abstractions/fakes used elsewhere in the repo.
- If AI provider configuration is unavailable, implement deterministic fallback behavior and tests, but keep the AI abstraction path ready for configured environments.
- Do not move to autonomous customer replies until source attribution, confidence, policy, approval, audit, and send failure handling are all implemented.
