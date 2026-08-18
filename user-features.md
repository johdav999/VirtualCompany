# Virtual Company — Detailed User Feature List

This inventory is grounded in the implemented Blazor pages, API controllers, shared contracts, and product documentation in this repository. “Available surface” means there is a corresponding route or API capability in the current solution. Some documented marketing and campaign capabilities are architectural/product targets and may still require additional UI or provider work.

## 1. Company access and workspace context

- Sign in through the configured authentication abstraction; local development supports authenticated development headers.
- Work within a selected company context.
- Carry the active company through navigation and API requests.
- Prevent cross-company access through membership, authorization policy, and tenant-scoped queries.
- Support company members, managers, and restricted system administrators with different capabilities.
- Invite and manage company members through the company administration flow.
- Complete company onboarding and setup workshops.
- Maintain company profile, operating context, policies, documents, and business knowledge used by agents.
- Configure user profile and communication preferences.
- Select communication language/localization preferences where supported.

## 2. Executive overview and daily control

- Open an executive dashboard at `/dashboard` (with the home route resolving into the existing home flow).
- See an executive cockpit covering company health, priorities, work, approvals, alerts, agent status, finance, sales, marketing, and support signals.
- View dashboard KPIs and refresh individual widgets without reloading the entire cockpit.
- Review current focus items, exceptions, blocked work, and decisions requiring attention.
- Read daily and periodic executive briefings.
- Configure briefing preferences.
- See the source, freshness, and explanation behind important metrics and recommendations where exposed by the relevant module.
- Navigate from a summary card to the owning module or record.

## 3. AI agent team

- View the agent roster and staff overview at `/agents/staff` and `/agents`.
- Work with specialised agents such as executive, finance, sales, marketing, operations, HR, support, and governance roles when onboarded for the company.
- Open an agent profile with role, objectives, capabilities, current work, recent activity, alerts, and operating status.
- Start a direct conversation with an agent at `/agents/{agentId}/chat`.
- Send messages, review the agent’s rationale summary, and use a chat message as the source for a new task.
- Ask an agent for recommendations, analysis, priorities, next actions, or a grounded briefing.
- Review agent-generated outputs, evidence, confidence, missing information, and requests for human review.
- Configure an agent’s operating brief by category, including role responsibilities, company context, products, policies, audiences, and marketing or department instructions.
- Edit and save an agent brief with unsaved-change protection.
- Generate a draft section of an agent brief and then review or edit it before saving.
- Upload documents to an agent brief, track indexing/processing status, and share selected brief documents with the team.
- Configure agent data scopes, trusted tools, mailbox access, capabilities, autonomy, and approval requirements.
- Hire/create an agent from an available template.
- Connect or disconnect purpose-specific Gmail or Microsoft 365 mailboxes for an agent or team mailbox.
- Review agent memory proposals and approve or reject them through a governed review queue.
- View capability availability and follow links to the exact access or integration configuration needed.

## 4. Guided work and workshops

- Open the company operation workspace at `/company-operation`.
- Run guided workshops for strategy, planning, onboarding, market work, hiring, and operational improvement where the corresponding artifact type is enabled.
- Select a workshop type at `/agents/{agentId}/workshops` and resume a specific work session at `/agents/{agentId}/workshops/{ArtifactType}`.
- Capture structured answers as a durable company artifact instead of leaving them only in chat.
- Continue a workshop over multiple sessions and retain status, history, questions, decisions, and outstanding work.
- Ask an agent to research a topic during a guided session.
- Review research findings, source citations, freshness, and proposed changes before committing them to company knowledge.
- Use real-time guided dialogue endpoints where enabled.

## 5. Work management and operating flow

- Open the unified Work center at `/work`.
- View planned, ongoing, waiting-for-human-approval, blocked, and completed work.
- Use compatibility/detail routes for tasks, approvals, inbox, queue, and outbound review work.
- Create tasks from the Work center, an agent chat, an agent staff view, or a workflow.
- Assign work to an agent or human owner.
- Set task title, description, due date, priority, status, and supporting context.
- Open task details and see why the work exists, its owner, related records, dependencies, and next actions.
- Filter work by state, owner, department/module, or other supported context.
- Run predefined workflows and inspect workflow state and results.
- On solution startup, trigger the daily role-agent operating pass before the normal cadence polling continues.
- Review queued work and background execution exceptions.
- Receive proactive messages and alerts for material events, failures, or decisions.
- Review approvals and approve, reject, or return governed work with a reason.
- Track work transitions on the Agent Team board.
- Preserve an auditable record of task, workflow, approval, and automated actions.

## 6. History, audit, and transparency

- Review the activity history at `/history` (compatibility alias `/activity-feed`).
- Search or inspect company activity and material changes.
- Open audit records at `/audit` and `/audit/{AuditEventId}`.
- Inspect who or what performed an action, when it happened, the company context, and the outcome.
- For restricted administrators, inspect transparency events and tool executions.
- View the registered trusted-tool catalog and individual execution details.
- Investigate execution exceptions and integration failures without silently losing the failed work.

## 7. Marketing workspace

The marketing workspace is available at `/marketing`. It connects business objectives to plans, campaign work, content, qualified demand, and Sales handoff.

### Overview and planning

- View marketing KPIs, explanations, objectives, plans, calendar items, priorities, and missing evidence.
- Refresh the workspace and ask the Marketing Agent (Maya when configured) for grounded priorities.
- Create a measurable marketing objective with name, measure, target, unit, and period.
- Activate a draft objective.
- Create a bounded marketing plan with name, summary, dates, and optional budget/currency.
- Activate a draft plan.
- Review plans and objectives before campaign work is prioritised.

### Calendar and campaigns

- View a combined marketing calendar of dated plans, campaigns, and campaign activities.
- See start date, end date, type, and status for scheduled work.
- Open the existing Sales campaign workspace from the marketing calendar.
- Use the existing campaign foundation for audience, offer, owner, schedule, activities, costs, performance, lifecycle, consent, and approval controls.
- Support B2B and B2C campaign planning concepts, including account/contact audiences, customer segments, products, purchases, retention, and transaction outcomes.
- Distinguish campaign initiatives from ongoing trigger-based automations.

### Content workflow

- Create a content brief with title, purpose, audience, channel, language, measurable objective, funnel stage, key message, offer, and call to action.
- Add content variants and versioned content.
- Generate source-backed content variants when the relevant agent capability is enabled.
- See source references and prompt/version metadata for generated variants.
- Run a content preflight/readiness check.
- Submit a content brief for review.
- Approve or reject submitted content.
- Retire content variants.
- Keep approval state and factual/source evidence attached to the content record.

### Audience qualification and Sales handoff

- Create a B2B or B2C qualification definition with channel, minimum score, freshness window, and optional linked-company requirement.
- Activate a qualification definition.
- Evaluate a contact against an active definition.
- Review recent qualification evaluations and scores.
- Record feedback on qualification decisions.
- Create a durable marketing-to-Sales handoff with evidence, urgency, suggested action, and expiry.
- Accept or decline a proposed handoff.
- Open the Sales prospects surface to continue accepted demand.

### Performance and experimentation

- Record and review marketing observations for a time window.
- Create, activate, and complete marketing experiments.
- Record a hypothesis, audience, success metrics, guardrails, duration, and result where the experiment contract supports it.
- Review marketing performance, missing evidence, negative signals, and confidence rather than treating unavailable data as zero.
- Ask the Marketing Agent to rank priorities using objectives, campaigns, observations, and approved company knowledge.

### Strategy, intelligence, and segmentation

- List and open marketing strategies.
- Prepare a strategy proposal and commit it after review.
- Update, submit, activate, or cancel a strategy subject to policy.
- Preview and commit strategy decomposition into actionable marketing work.
- Create, update, review, archive, and inspect marketing intelligence records.
- Maintain a freshness queue and review history for intelligence.
- List marketing segments.
- Prepare a segment proposal and commit a segment version.
- Create segment versions, submit them, and activate a target selection.
- Inspect segment impact, dimensions, decision data, target recommendations, and artifact mappings.
- Use deterministic segment evaluation, consent, exclusions, suppression, language, and source evidence when audience membership is calculated.

### Operating loop and channels

- Request a governed marketing operating run for a marketing agent.
- Inspect operating-run actions and retry eligible failed actions.
- Review channel connections and OAuth/provider status where configured.
- Use provider-backed execution only through approved trusted tools and policy boundaries.
- Keep manual or unsupported channels as tracked activities/tasks rather than implying that the application executed an external side effect.

## 8. Sales and CRM

- Open the Sales overview at `/app/sales`.
- View sales KPIs, priorities, pipeline, prospects, leads, contacts, deals, and follow-up work.
- Manage prospects and inbound leads; `/app/sales/prospecting` is a compatibility route for the prospects surface.
- Open lead detail and contact profiles.
- View and manage the sales pipeline.
- Open deal detail with current stage, value, contacts, activities, and next actions.
- Create and manage sales campaigns at `/app/sales/campaigns`.
- Build outbound email sequences with audience selection, sequence steps, timing, language, policy, approval state, and lifecycle controls.
- Enrol company-owned contacts in campaigns and track sequence progress.
- Schedule due sequence steps and observe drafts, delivery outcomes, replies, bounces, cancellations, and deal-created stop conditions.
- Pause, launch, or stop campaigns subject to approval and policy.
- Generate or review lead-generation suggestions and source information.
- Ask the Sales Agent for campaign analysis, next-best actions, and pipeline recommendations.
- Schedule sales meetings where calendar integration is configured.
- Preserve consent, opt-out, tenant isolation, and audit controls around outbound activity.

## 9. Finance

- Open the finance overview at `/finance` when the user has finance access.
- Review cash position, balances, monthly summaries, financial KPIs, forecasts, priorities, and low-cash alerts.
- Review customer invoices and invoice detail.
- Review supplier bills, bill inbox items, staged bill review, and supplier-bill detail.
- Inspect invoice/bill anomalies and issue detail.
- Review, approve, reject, or return finance work according to finance policy.
- Review transactions, transaction detail, payments, payment detail, and counterparties.
- Review payment allocations and payment readiness where enabled.
- Manage supplier subscriptions and subscription discovery.
- Connect and configure finance providers, including provider-specific settings and email settings.
- Review synchronisation state, provider errors, reconciliation suggestions, and maintenance actions.
- Use finance-agent analysis for supported cash questions such as what to pay, overdue customers, and why cash changed.
- Use the finance simulation lab for restricted scenario/seeding administration.
- Inspect finance transparency events, tool registry, and tool executions as an administrator.
- Keep booked finance data, payment authority, accounting evidence, and spending approvals inside finance ownership boundaries.

## 10. Customer support

- Open the Support workspace at `/support` or `/support/cases`.
- View the support inbox and customer cases.
- Open case detail and manage case status, ownership, priority, and follow-up.
- Draft and govern support replies.
- Route mailbox messages into support work.
- Monitor SLA status and configure SLA policy at `/support/settings/sla`.
- Track refund requests through a governed support/finance approval lifecycle.
- Review support knowledge and knowledge gaps at `/support/knowledge` (compatibility alias `/support/knowledge-gaps`).
- Review governed support memory at `/support/memory`.
- Ask the Support Agent for triage, reply, knowledge-gap, and next-action analysis.
- Keep complaint, escalation, customer-risk, and service-commitment decisions in the Support boundary.

## 11. Mailbox, calendar, and communication integrations

- Connect team or agent mailboxes through Gmail or Microsoft 365 OAuth flows.
- Track mailbox connection state and reconnect/disconnect a provider.
- Configure calendar connections at `/settings/calendar-connections`.
- Use mailbox ingestion, routing, outbound delivery, and reply tracking where a provider is connected.
- Configure finance mailbox and email-provider settings separately from general agent mailbox access.
- Configure outbound automation policy and review queues.
- Keep OAuth state, credentials, and provider secrets outside agent prompts and user-visible records.

## 12. Settings and administration

- Use the settings hub at `/settings`.
- Edit personal profile and user preferences at `/settings/profile`.
- Configure agent settings and manage the roster at `/settings/agents` and `/agents/manage`.
- Configure agent autonomy, data scopes, tools, capabilities, briefs, documents, and mailboxes.
- Configure briefing preferences.
- Configure calendar connections.
- Configure finance providers, finance email settings, and provider integrations.
- Configure support SLA policy.
- Review restricted simulation and system administration tools when authorized.
- Inspect transparency events, tool registry, and tool execution history when authorized.

## 13. Mobile companion

The .NET MAUI companion app provides a reduced operational view for users away from the main web workspace:

- Sign in and select a company.
- View company status KPIs.
- Review alerts and pending approvals.
- Read the daily briefing.
- Open direct agent chat.
- Trigger quick follow-up actions supported by the mobile contracts.

## 14. Public and external-facing surfaces

- View the public company page at `/company`.
- Submit a public inquiry through `/contact`.
- Receive website lead-form submissions through the website lead-form API and route them into the appropriate company workflow.
- Keep public routes separate from authenticated company data and internal navigation.

## 15. Cross-cutting user guarantees

- Company isolation and authorization apply to every company-scoped page and API operation.
- Approval-gated actions expose a reviewable state instead of silently executing.
- External side effects are routed through provider/tool boundaries with observable success and failure states.
- Background work is represented by durable tasks, workflow state, retries, or operator-visible exceptions.
- Idempotent operations prevent duplicate sends, duplicate handoffs, duplicate approvals, and duplicate scheduled work where the relevant service supports them.
- Activity, audit, transparency, and correlation information is retained for material actions.
- Agent outputs distinguish facts, assumptions, recommendations, missing evidence, confidence, and freshness where the contract provides those fields.
- Existing module ownership is preserved: Sales owns pipeline and sales execution, Finance owns booked financial truth and payment authority, Support owns cases and customer-risk handling, and Marketing coordinates plans/content/demand without bypassing those boundaries.

## 16. Main user-facing route index

| Area | Primary routes |
| --- | --- |
| Overview | `/`, `/dashboard` |
| Agents | `/agents/staff`, `/agents`, `/agents/{AgentId}`, `/agents/{AgentId}/chat`, `/settings/agents` |
| Workshops | `/agents/{AgentId}/workshops`, `/agents/{AgentId}/workshops/{ArtifactType}` |
| Work | `/work`, `/tasks`, `/approvals`, `/inbox`, `/queue`, `/workflows`, `/outbound-review-queue` |
| History | `/history`, `/activity-feed`, `/audit` |
| Marketing | `/marketing` |
| Sales | `/app/sales`, `/app/sales/prospects`, `/app/sales/pipeline`, `/app/sales/campaigns` |
| Finance | `/finance` plus the cash, invoice, bill, transaction, payment, anomaly, subscription, and settings routes |
| Support | `/support`, `/support/cases`, `/support/knowledge`, `/support/memory` |
| Settings | `/settings`, `/settings/profile`, `/settings/calendar-connections`, `/briefing-preferences` |
| Public | `/company`, `/contact` |
