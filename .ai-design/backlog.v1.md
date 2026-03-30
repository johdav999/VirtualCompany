# Epics

## EP-1 Multi-tenant foundation and company setup
Goal: Establish the secure SaaS foundation for tenant isolation, authentication, company workspace creation, memberships, and initial onboarding.

## EP-2 AI workforce and policy configuration
Goal: Enable companies to hire, configure, and govern named AI agents with scoped permissions, autonomy levels, thresholds, and role-specific settings.

## EP-3 Knowledge, memory, and retrieval
Goal: Provide a company data layer for documents, memory, semantic retrieval, and grounded context access for agents.

## EP-4 Task, workflow, and approval orchestration
Goal: Implement explicit business task execution with workflow state, approvals, escalations, recurring triggers, and reliable background processing.

## EP-5 Agent communication and orchestration engine
Goal: Deliver direct agent chat, structured task delegation, shared orchestration, tool execution, and manager-worker multi-agent coordination.

## EP-6 Executive cockpit, auditability, and mobile companion
Goal: Surface dashboards, alerts, approvals, rationale summaries, audit trails, and a focused mobile experience for executive actions.

# User stories

## EP-1 Multi-tenant foundation and company setup

### ST-101 — Tenant-aware authentication and membership
**User story:** As a company user, I want to sign in and access only my company workspace so that company data stays isolated and secure.  
**Acceptance criteria:**
- Users can authenticate and resolve one or more company memberships.
- Every tenant-owned API request is scoped by company context.
- Unauthorized access to another company’s data returns forbidden/not found.
- Membership roles are persisted and available to authorization checks.
**Notes:**
- Use shared-schema multi-tenancy with `company_id` enforcement.
- Keep auth provider abstraction for future SSO.
- Prefer policy-based authorization in ASP.NET Core.

### ST-102 — Company workspace creation and onboarding wizard
**User story:** As a founder, I want to create a company workspace and complete guided setup so that I can start using the platform quickly.  
**Acceptance criteria:**
- User can create a company with name, industry, business type, timezone, currency, language, and compliance region.
- Setup wizard persists progress and supports resume.
- Industry/business templates can prefill recommended defaults.
- Successful completion lands the user in the web dashboard with starter guidance.
**Notes:**
- Blazor web is the primary onboarding surface.
- Store branding/settings in JSONB where flexibility is needed.
- Keep template model extensible without code changes.

### ST-103 — Human user invitation and role assignment
**User story:** As an owner/admin, I want to invite teammates and assign roles so that humans can collaborate with agents safely.  
**Acceptance criteria:**
- Owner/admin can invite users by email to a company.
- Invited users receive a pending membership until accepted.
- Roles such as owner, admin, manager, employee, finance approver, and support supervisor are assignable.
- Role changes take effect on subsequent authorization checks.
**Notes:**
- Invitation delivery can use outbox + background dispatcher.
- Support re-invite and revoke flows.
- Do not couple human roles to agent permissions.

### ST-104 — Baseline platform observability and operational safeguards
**User story:** As an operator, I want core platform logging, health checks, and error handling so that the SaaS foundation is supportable in production.  
**Acceptance criteria:**
- API exposes health endpoints for app, database, Redis, and object storage dependencies.
- Structured logs include correlation IDs and tenant context where applicable.
- Unhandled exceptions are captured and mapped to safe API responses.
- Background job failures are logged and retryable.
**Notes:**
- Separate technical logs from business audit events.
- Add rate limiting hooks early for chat/task endpoints.
- Use outbox for reliable side effects.

---

## EP-2 AI workforce and policy configuration

### ST-201 — Agent template catalog and hiring flow
**User story:** As a founder, I want to hire named agents from templates so that I can stand up a virtual company quickly.  
**Acceptance criteria:**
- System provides seed templates for at least finance, sales, marketing, support, operations, and executive assistant roles.
- User can create an agent from a template and customize name, avatar, department, role, personality, and seniority.
- Hired agents appear in the company roster with active status by default.
- Template defaults are copied into company-owned agent configuration records.
**Notes:**
- Seed data should be versioned/migrated.
- Avatar can initially be URL/file reference only.
- Avoid hardcoding role behavior outside config where possible.

### ST-202 — Agent operating profile management
**User story:** As an admin, I want to configure each agent’s objectives, KPIs, tools, scopes, and escalation rules so that the agent behaves within its business role.  
**Acceptance criteria:**
- Agent profile supports editing objectives, KPIs, role brief, tool permissions, data scopes, approval thresholds, escalation rules, trigger logic, and working hours.
- Changes are persisted with updated timestamps and reflected in subsequent orchestration runs.
- Invalid configurations are rejected with field-level validation.
- Agent status can be changed to active, paused, restricted, or archived.
**Notes:**
- Store flexible policy/config fields in JSONB with server-side validation.
- Restrict archived agents from new task assignment.
- Keep config history auditable later via audit events.

### ST-203 — Autonomy levels and policy guardrails
**User story:** As an owner, I want autonomy levels and guardrails enforced before actions execute so that agents cannot exceed trust boundaries.  
**Acceptance criteria:**
- Agents support autonomy levels 0-3 with conservative defaults at 0 or 1.
- Policy engine evaluates read/recommend/execute scope, thresholds, and approval requirements before tool execution.
- Actions outside scope are blocked and logged with a reason.
- Sensitive actions above threshold create approval requests instead of executing directly.
**Notes:**
- Guardrails must run pre-execution, not only post-response.
- Keep policy decisions structured for auditability.
- Default-deny when policy config is missing or ambiguous.

### ST-204 — Agent roster and profile views
**User story:** As a manager, I want to see all agents and inspect their profiles so that I understand responsibilities, status, and boundaries.  
**Acceptance criteria:**
- Web roster lists agents with name, role, department, status, autonomy level, and workload/health summary.
- Agent detail view shows identity, objectives, permissions, thresholds, and recent activity.
- Users can filter roster by department and status.
- Restricted fields/actions are hidden or disabled based on human role.
**Notes:**
- Blazor SSR first; add interactivity only where needed.
- Health summary can initially be derived from task state and last activity.
- Keep profile page as the anchor for future analytics.

---

## EP-3 Knowledge, memory, and retrieval

### ST-301 — Company document ingestion and storage
**User story:** As a company user, I want to upload SOPs, FAQs, playbooks, and files so that agents can use company knowledge.  
**Acceptance criteria:**
- Users can upload documents with title, type, and access scope metadata.
- Files are stored in object storage and document metadata in PostgreSQL.
- Ingestion status is tracked from uploaded to processed/failed.
- Unsupported or failed files surface actionable error states.
**Notes:**
- Start with common text/PDF/doc formats only.
- Virus scanning hook should be left in the pipeline design.
- Access scope metadata must be tenant-aware.

### ST-302 — Chunking, embeddings, and semantic retrieval
**User story:** As the system, I want uploaded knowledge converted into searchable chunks so that agents can retrieve relevant context.  
**Acceptance criteria:**
- Processed documents are chunked and embedded into `knowledge_chunks` using pgvector.
- Semantic search can return top relevant chunks scoped to company and access policy.
- Retrieval results include source document references for explainability.
- Re-ingesting a document replaces or versions prior chunks safely.
**Notes:**
- Background worker should handle embedding generation asynchronously.
- Keep embedding model/version metadata for future reindexing.
- Retrieval must enforce company and scope filters before similarity ranking.

### ST-303 — Agent and company memory persistence
**User story:** As an agent-enabled system, I want to store durable memory summaries and preferences so that agents retain useful context over time.  
**Acceptance criteria:**
- Memory items can be stored as company-wide or agent-specific records.
- Memory supports types such as preference, decision pattern, summary, role memory, and company memory.
- Memory retrieval can filter by agent, recency, salience, and semantic relevance.
- Users can delete or expire memory items according to policy controls.
**Notes:**
- Store summaries, not raw chain-of-thought.
- Include validity windows for stale memory handling.
- Deletion must respect tenant boundaries and future privacy controls.

### ST-304 — Grounded context retrieval service
**User story:** As the orchestration engine, I want a single context retrieval service so that prompts are grounded in scoped company data, memory, and recent history.  
**Acceptance criteria:**
- Retrieval service composes context from documents, memory, recent tasks, and relevant records.
- Retrieval respects agent data scopes and human/company permissions.
- Returned context is normalized into structured prompt-ready sections.
- Retrieval source references are persisted for downstream audit/explanation.
**Notes:**
- Keep retrieval deterministic and testable.
- Avoid direct prompt assembly in UI or controllers.
- Cache low-risk repeated retrievals in Redis where appropriate.

---

## EP-4 Task, workflow, and approval orchestration

### ST-401 — Task lifecycle and assignment
**User story:** As a user, I want to create and assign tasks to agents so that work is tracked explicitly rather than hidden in chat.  
**Acceptance criteria:**
- Users can create tasks with type, title, description, priority, due date, and assigned agent.
- Tasks support statuses: new, in_progress, blocked, awaiting_approval, completed, failed.
- Agent-created subtasks can reference a parent task.
- Task detail stores input payload, output payload, rationale summary, and confidence score where available.
**Notes:**
- Task entity is the backbone for orchestration and audit.
- Assignment must reject paused/archived agents.
- Keep task APIs CQRS-lite.

### ST-402 — Workflow definitions, instances, and triggers
**User story:** As an admin, I want predefined workflows triggered by user action, schedule, or event so that recurring business processes can run consistently.  
**Acceptance criteria:**
- System supports workflow templates/definitions with versioned JSON definitions.
- Workflow instances can be started manually, by schedule, or by internal event.
- Instance state and current step are persisted and queryable.
- Failed or blocked workflow steps surface exceptions for review.
**Notes:**
- Start with opinionated predefined workflows, not arbitrary builder UX.
- Scheduler should use background workers with distributed locking.
- Version workflow definitions to avoid breaking in-flight instances.

### ST-403 — Approval requests and decision chains
**User story:** As an approver, I want to review and decide on sensitive agent actions so that autonomy remains controlled.  
**Acceptance criteria:**
- Approval requests can be created for tasks, workflows, or actions with threshold context.
- Approval can target a required role, specific user, or ordered multi-step chain.
- Approve/reject decisions update the linked entity state and are auditable.
- Expired/cancelled approvals are handled explicitly and do not execute actions.
**Notes:**
- v1 can support single-step and ordered multi-step chains.
- Approval UX should show concise rationale and affected data.
- Rejection should support comments for agent/user feedback.

### ST-404 — Escalations, retries, and long-running background execution
**User story:** As the system, I want resilient workflow execution so that scheduled and long-running agent work completes reliably.  
**Acceptance criteria:**
- Background workers execute scheduled jobs, workflow progression, retries, and long-running tasks.
- Retry policy distinguishes transient failures from permanent policy/business failures.
- Blocked or failed executions create visible exceptions/escalations.
- Outbox-backed side effects are dispatched reliably without duplication.
**Notes:**
- Use idempotency keys/correlation IDs for retries.
- Redis can coordinate locks and ephemeral execution state.
- Keep worker execution tenant-scoped.

---

## EP-5 Agent communication and orchestration engine

### ST-501 — Direct chat with named agents
**User story:** As a founder, I want to chat with a specific named agent so that I can delegate or ask questions within that role’s context.  
**Acceptance criteria:**
- Users can open a direct conversation with an agent from roster or dashboard.
- Messages are stored with sender type, sender ID, conversation type, and timestamps.
- Agent responses use the selected agent’s persona, role brief, and scoped context.
- Chat can create or link to tasks when the interaction becomes actionable.
**Notes:**
- Keep conversation history tenant-scoped and paginated.
- Do not expose raw reasoning; return concise rationale summaries where relevant.
- Support direct-agent channel type first.

### ST-502 — Shared orchestration pipeline for single-agent tasks
**User story:** As the platform, I want one orchestration engine for all agents so that behavior is maintainable and configurable.  
**Acceptance criteria:**
- Orchestration resolves target agent, intent/task type, and runtime context.
- Prompt builder composes role instructions, company context, memory, policies, and tool schemas.
- Tool executor returns structured results only and records execution metadata.
- Final response includes user-facing output plus structured task/audit artifacts.
**Notes:**
- Shared engine, distinct agent configs; no bespoke stacks per role.
- Keep orchestration service separate from HTTP/UI concerns.
- Persist correlation IDs across prompt, tool, task, and audit records.

### ST-503 — Policy-enforced tool execution
**User story:** As a security-conscious owner, I want every agent tool call checked against policy so that actions remain within scope.  
**Acceptance criteria:**
- Tool execution requests are evaluated for tenant scope, action type, autonomy level, thresholds, and approval requirements.
- Allowed executions are persisted in `tool_executions` with request/response and policy decision metadata.
- Denied executions return a safe user-facing explanation and create audit records.
- Internal tools can call domain modules through typed contracts rather than direct DB access.
**Notes:**
- Separate read/recommend/execute action types.
- Never let the model call external systems directly.
- Start with internal tool abstractions and a small connector set.

### ST-504 — Manager-worker multi-agent collaboration
**User story:** As a founder, I want cross-functional requests coordinated across agents so that I receive one consolidated recommendation.  
**Acceptance criteria:**
- A coordinator flow can decompose a request into explicit subtasks for multiple agents.
- Subtasks are assigned, tracked, and linked to a parent task/workflow.
- Final output consolidates sub-results into one response with source attribution by agent.
- Collaboration is bounded to explicit plans; uncontrolled agent loops are prevented.
**Notes:**
- Use manager-worker pattern only, not free-form chatter.
- Put limits on fan-out, depth, and runtime.
- Consolidation should preserve rationale summaries per contributing agent.

### ST-505 — Daily briefings and executive summaries
**User story:** As an executive user, I want proactive daily and weekly summaries so that I can monitor the company without prompting each agent manually.  
**Acceptance criteria:**
- System can generate scheduled daily briefings and weekly summaries per company.
- Briefings aggregate alerts, approvals, KPI highlights, anomalies, and notable agent updates.
- Generated summaries are stored as messages/notifications and visible in the dashboard.
- Users can configure delivery preferences for in-app and mobile notifications.
**Notes:**
- Start with scheduled in-app generation; email delivery can follow.
- Summaries should reference underlying tasks/approvals where possible.
- Use cached dashboard aggregates to reduce generation cost.

---

## EP-6 Executive cockpit, auditability, and mobile companion

### ST-601 — Executive cockpit dashboard
**User story:** As a founder, I want a company-wide dashboard so that I can see alerts, approvals, KPIs, and agent activity in one place.  
**Acceptance criteria:**
- Dashboard shows daily briefing, pending approvals, alerts, department KPI cards, and recent activity feed.
- Users can drill into agents, tasks, workflows, and approvals from dashboard widgets.
- Dashboard queries are tenant-scoped and performant for interactive use.
- Empty states guide setup when no agents, workflows, or knowledge exist yet.
**Notes:**
- Web app is the primary command center.
- Start with summary KPIs and simple trend indicators.
- Use Redis caching for expensive aggregate queries.

### ST-602 — Audit trail and explainability views
**User story:** As a manager, I want to inspect what agents did and why so that I can trust, review, and override decisions.  
**Acceptance criteria:**
- Important actions create business audit events with actor, action, target, outcome, rationale summary, and data sources used.
- Users can view audit history filtered by agent, task, workflow, and date range.
- Action detail shows linked approvals, tool executions, and affected entities where available.
- Explanations are concise and operational, without exposing raw chain-of-thought.
**Notes:**
- Auditability is a domain feature, not just app logging.
- Keep data source references human-readable.
- Ensure audit views respect role-based access.

### ST-603 — Alerts, notifications, and approval inbox
**User story:** As a busy operator, I want a unified inbox for alerts and approvals so that I can act quickly on exceptions and sensitive actions.  
**Acceptance criteria:**
- System generates notifications for approvals, escalations, workflow failures, and briefing availability.
- Users can view and act on pending approvals from a dedicated inbox.
- Notification state supports unread/read and actioned statuses.
- Background dispatcher delivers in-app notifications reliably.
**Notes:**
- Model notifications separately from messages if needed for UX.
- Prioritize approval and exception alerts in sorting.
- Keep notification fan-out out of request path via outbox.

### ST-604 — Mobile companion for approvals, alerts, and quick chat
**User story:** As a founder on the go, I want a mobile companion app so that I can review alerts, approve actions, and ask agents quick questions.  
**Acceptance criteria:**
- .NET MAUI app supports sign-in, company selection, alert list, approval actions, daily briefing view, and direct agent chat.
- Mobile can display quick company status and task follow-up summaries.
- Approval decisions made on mobile update the same backend approval state as web.
- Mobile scope is intentionally limited and does not require full admin/workflow parity.
**Notes:**
- Responsive web may cover some early mobile needs, but MAUI app remains the target companion.
- Reuse backend APIs; no mobile-specific business logic.
- Optimize for concise payloads and intermittent connectivity.

# Milestones

## M1 — SaaS foundation and onboarding
Stories: ST-101, ST-102, ST-103, ST-104  
Release focus: secure multi-tenant base, company creation, memberships, operational readiness.

## M2 — Agent setup and governance
Stories: ST-201, ST-202, ST-203, ST-204  
Release focus: hire/configure agents, enforce autonomy and policy boundaries, roster/profile UX.

## M3 — Knowledge and workflow backbone
Stories: ST-301, ST-302, ST-303, ST-304, ST-401, ST-402, ST-403, ST-404  
Release focus: document ingestion, memory/retrieval, task lifecycle, workflows, approvals, resilient workers.

## M4 — Core virtual company experience
