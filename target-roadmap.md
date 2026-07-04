# Virtual Company Target Roadmap

Source: `.ai-design/backlog.v1.md`

This roadmap describes the target product direction for the Virtual Company application. It is a planning document, not a statement that all functionality is currently complete.

## Product Target

Virtual Company should become a secure, multi-tenant business operations platform where a company can configure a governed AI workforce, connect company knowledge and operational data, delegate work through tasks and workflows, review sensitive actions through approvals, and monitor the business through dashboards, briefings, audit trails, and a focused mobile companion.

## Roadmap Principles

- Web-first: setup, administration, workflow management, dashboards, and deep review happen in the web app.
- Mobile-companion: mobile focuses on alerts, approvals, briefings, quick status, and direct agent chat.
- Tenant-safe by default: every company-owned record, query, workflow, task, memory item, and integration action must be scoped by company.
- Governed autonomy: agents should not execute sensitive work unless policy, permissions, thresholds, and approvals allow it.
- Explicit work tracking: important agent work should become tasks, workflow steps, approvals, audit events, or notifications rather than disappearing inside chat.
- Explainable operations: user-facing rationale, source references, approval context, and audit history are core product features.
- Production implementation: features should be real, tenant-scoped, authenticated, persisted, observable, and supported by migrations where data changes are required.

## Phase 1: SaaS Foundation And Onboarding

Backlog stories: ST-101, ST-102, ST-103, ST-104

Target outcome: a company can securely create and operate a tenant workspace with human members, roles, onboarding progress, and baseline production safeguards.

Key capabilities:
- Tenant-aware authentication and company membership resolution.
- Company workspace creation with name, industry, business type, timezone, currency, language, and compliance region.
- Guided onboarding wizard with resumable progress and recommended defaults.
- Human invitations, pending memberships, role assignment, re-invite, and revoke flows.
- Role-aware authorization for owner, admin, manager, employee, finance approver, and support supervisor style roles.
- Health checks for application and infrastructure dependencies.
- Structured logs with correlation IDs and tenant context.
- Safe exception responses.
- Reliable outbox-backed side effects.
- Retryable background job failure handling.

Exit criteria:
- A user can sign in, create/select a company, invite collaborators, and land in a usable company dashboard.
- Company-scoped APIs reject cross-tenant access.
- Operational logs and health endpoints are adequate for local and production troubleshooting.

Dependencies:
- Authentication provider abstraction.
- Shared company context model.
- Membership and role model.
- Outbox/background dispatcher foundation.

## Phase 2: AI Workforce Setup And Governance

Backlog stories: ST-201, ST-202, ST-203, ST-204

Target outcome: a company can hire named agents from templates, configure their responsibilities, and enforce clear operating boundaries before any agent action executes.

Key capabilities:
- Agent template catalog for finance, sales, marketing, support, operations, and executive assistant roles.
- Agent hiring flow from templates.
- Customizable agent identity: name, avatar, department, role, personality, and seniority.
- Company-owned agent configuration copied from template defaults.
- Agent profile management for objectives, KPIs, role brief, tool permissions, data scopes, approval thresholds, escalation rules, trigger logic, and working hours.
- Agent status lifecycle: active, paused, restricted, archived.
- Autonomy levels 0-3 with conservative defaults.
- Policy engine for read, recommend, and execute scopes.
- Pre-execution guardrails for tenant scope, tool scope, thresholds, and approval requirements.
- Agent roster and detail views with status, autonomy level, workload, health summary, boundaries, and recent activity.

Exit criteria:
- Admins can create and configure agents without code changes.
- Paused or archived agents cannot receive new work.
- Sensitive or out-of-scope actions are blocked or routed to approval before execution.
- Users can inspect what each agent is responsible for and what it is allowed to do.

Dependencies:
- Phase 1 tenant and authorization foundation.
- Agent template seed data.
- Policy decision persistence.
- Initial audit event model.

## Phase 3: Knowledge, Memory, And Grounded Context

Backlog stories: ST-301, ST-302, ST-303, ST-304

Target outcome: agents can answer and act from scoped company knowledge, document content, memory summaries, recent work, and relevant business records.

Key capabilities:
- Company document upload with title, type, and access scope metadata.
- Object storage for files and database metadata for documents.
- Ingestion status tracking from uploaded through processed or failed.
- Actionable error states for unsupported or failed files.
- Virus scanning extension point.
- Document chunking and embedding generation.
- Semantic retrieval scoped by company and access policy.
- Retrieval results with source document references.
- Safe re-ingestion/versioning of document chunks.
- Company-wide and agent-specific memory records.
- Memory types such as preference, decision pattern, summary, role memory, and company memory.
- Memory retrieval by agent, recency, salience, and semantic relevance.
- User controls to delete or expire memory items.
- Central grounded context retrieval service composing documents, memory, recent tasks, and relevant records.
- Persisted source references for downstream explanation and audit.

Exit criteria:
- Uploaded company knowledge can be retrieved in agent context with source attribution.
- Retrieval respects company, human role, and agent data scopes.
- Agents can use durable memory without storing raw chain-of-thought.
- Context assembly is centralized and testable, not duplicated in UI or controllers.

Dependencies:
- Document storage.
- Background indexing worker.
- Embedding provider abstraction.
- Agent data scopes.
- Memory retention/deletion policy.

## Phase 4: Tasks, Workflows, Approvals, And Reliable Execution

Backlog stories: ST-401, ST-402, ST-403, ST-404

Target outcome: agent and human work is tracked explicitly through tasks, workflows, approvals, exceptions, escalations, and background execution.

Key capabilities:
- Task creation with type, title, description, priority, due date, and assigned agent.
- Task statuses: new, in progress, blocked, awaiting approval, completed, failed.
- Parent-child task relationships for agent-created subtasks.
- Task detail with input payload, output payload, rationale summary, and confidence where available.
- Versioned workflow templates and definitions.
- Workflow instances started manually, by schedule, or by internal event.
- Persisted workflow instance state, current step, and queryable progress.
- Failed or blocked workflow steps surfaced as visible exceptions.
- Approval requests linked to tasks, workflows, or tool/action attempts.
- Approval targeting by role, specific user, or ordered multi-step chain.
- Approval decisions that update linked entity state and create audit history.
- Explicit handling for expired and cancelled approvals.
- Background workers for scheduled jobs, workflow progression, retries, and long-running work.
- Retry policy that separates transient failures from permanent policy/business failures.
- Escalations for blocked or failed execution.
- Idempotency keys and correlation IDs for safe retries.

Exit criteria:
- Work can be delegated to agents and tracked outside chat.
- Recurring or event-driven business processes can run safely from predefined workflows.
- Sensitive work waits for human approval when required.
- Failed work is visible, reviewable, and retryable where appropriate.

Dependencies:
- Phase 2 policy guardrails.
- Phase 3 context retrieval for task execution.
- Outbox and background worker infrastructure.
- Notification and audit primitives.

## Phase 5: Agent Communication And Orchestration

Backlog stories: ST-501, ST-502, ST-503, ST-504, ST-505

Target outcome: users can communicate with named agents, delegate work through a shared orchestration engine, coordinate multi-agent tasks, and receive proactive briefings.

Key capabilities:
- Direct chat with named agents from roster or dashboard.
- Tenant-scoped conversation and message storage.
- Agent responses based on role brief, persona, policy, memory, company context, and allowed tools.
- Chat-to-task linking when a conversation becomes actionable.
- Shared orchestration pipeline for all agents.
- Target agent, intent, task type, and runtime context resolution.
- Prompt/context construction through approved application services.
- Typed internal tool executor with structured results.
- Tool execution records with request, response, policy decision, and metadata.
- Denied tool executions with safe explanations and audit records.
- Manager-worker coordination for cross-functional requests.
- Explicit subtask fan-out, bounded depth, bounded runtime, and consolidated final response.
- Daily briefings and weekly executive summaries per company.
- Briefing aggregation of alerts, approvals, KPI highlights, anomalies, and notable agent updates.
- Briefing delivery through dashboard, notifications, and configured in-app/mobile preferences.

Exit criteria:
- Users can chat with role-specific agents and get grounded, scoped answers.
- Agent work uses one maintainable orchestration pipeline instead of bespoke role-specific stacks.
- Tools cannot bypass policy or call external systems directly.
- Multi-agent collaboration is structured and bounded.
- Executives receive proactive summaries without manually prompting every agent.

Dependencies:
- Phases 2-4.
- Grounded context retrieval.
- Tool registry/execution framework.
- Audit and notification systems.
- Dashboard aggregate data.

## Phase 6: Executive Cockpit, Auditability, Alerts, And Mobile Companion

Backlog stories: ST-601, ST-602, ST-603, ST-604

Target outcome: operators and executives can monitor company health, review agent work, approve sensitive actions, and act from web or mobile.

Key capabilities:
- Executive cockpit dashboard with daily briefing, pending approvals, alerts, KPI cards, and recent activity.
- Drilldown from dashboard widgets into agents, tasks, workflows, approvals, and relevant records.
- Tenant-scoped dashboard queries with interactive performance.
- Empty states that guide setup when agents, workflows, or knowledge are missing.
- Business audit events for important actions with actor, action, target, outcome, rationale summary, and data sources.
- Audit history filtered by agent, task, workflow, and date range.
- Action detail views showing linked approvals, tool executions, and affected entities.
- Concise explanations without exposing raw chain-of-thought.
- Unified inbox for approvals, escalations, workflow failures, briefing availability, and other notifications.
- Notification state for unread, read, and actioned.
- Outbox-backed notification fan-out.
- .NET MAUI mobile companion for sign-in, company selection, alert list, approval actions, daily briefing, direct agent chat, quick company status, and task follow-up.
- Mobile decisions update the same backend approval and notification state as web.

Exit criteria:
- The dashboard becomes the primary command center for company status and exceptions.
- Users can inspect what agents did, why, and which data was used.
- Approval and exception work is actionable from a unified inbox.
- Mobile supports focused action flows without attempting full web parity.

Dependencies:
- Phases 1-5.
- Activity feed, audit, alerts, notifications.
- Dashboard read models/caching.
- Mobile authentication and company context.

## Cross-Cutting Roadmap Workstreams

Security and tenancy:
- Enforce company scope on all tenant-owned data.
- Prefer forbidden/not found for unauthorized cross-tenant access.
- Keep human roles separate from agent permissions.
- Default-deny missing or ambiguous policy.

Observability:
- Correlation IDs across requests, tasks, tool executions, workflows, approvals, and audit records.
- Separate operator logs from business audit events.
- Health checks for runtime dependencies.
- Visible execution exceptions for business users where action is needed.

Data and persistence:
- Use migrations for schema changes.
- Persist structured records for tasks, workflows, approvals, tool execution, memory, retrieval sources, audit events, and notifications.
- Keep flexible config fields validated server-side.
- Preserve source references for explainability and drilldown.

AI and orchestration:
- Use approved abstractions for model calls.
- Do not let models call external systems directly.
- Store rationale summaries and source references, not raw chain-of-thought.
- Keep orchestration separate from HTTP/UI concerns.

Integrations:
- Start with a small, typed connector/tool set.
- External writes must pass policy checks and approval checks when required.
- Integration side effects should be idempotent and auditable.

## Suggested Release Sequence

| Release | Name | Primary value | Included phases |
|---|---|---|---|
| R1 | Secure company workspace | Users can create and operate isolated company workspaces. | Phase 1 |
| R2 | Governed agent roster | Companies can hire and configure agents with enforced boundaries. | Phase 2 |
| R3 | Grounded operating memory | Agents can use scoped company knowledge and memory. | Phase 3 |
| R4 | Work execution backbone | Tasks, workflows, approvals, retries, and exceptions become reliable. | Phase 4 |
| R5 | Core virtual company experience | Chat, orchestration, tool execution, multi-agent coordination, and briefings work together. | Phase 5 |
| R6 | Executive operating layer | Dashboard, audit, inbox, alerts, and mobile companion make the system operationally usable. | Phase 6 |

## Definition Of Done For Target Features

A roadmap item should not be considered complete unless:
- It is tenant-scoped and role-aware.
- It has real persisted state where the workflow requires durability.
- It has authenticated API endpoints or internal application contracts as appropriate.
- It has validation for invalid or unsafe inputs.
- It produces audit events for important business actions.
- It exposes user-facing errors or exceptions where users need to act.
- It is observable through logs, correlation IDs, and relevant health/status information.
- It avoids mock data and scaffolding in production paths.
- It has tests proportional to risk and blast radius.
