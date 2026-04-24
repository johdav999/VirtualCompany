\# Virtual Company Architecture Rules for Codex



\## Core architecture



\- Build the product as a multi-tenant SaaS.

\- Use a web-first architecture.

\- Use Blazor Web App for the main application.

\- Use .NET MAUI only for mobile companion flows such as alerts, approvals, chat, briefings, and task follow-up.

\- Use ASP.NET Core as the backend.

\- Use a modular monolith for v1/v2.

\- Do not introduce microservices unless explicitly requested.

\- Keep clear internal module boundaries.

\- Use background workers for long-running workflows, scheduled jobs, inbox processing, integration sync, retries, and notifications.



\## Current implementation note



The original architecture mentioned PostgreSQL and pgvector. If the repository currently uses SQL Server, follow the existing repository implementation instead.



\- Do not switch database provider unless explicitly requested.

\- Do not add PostgreSQL-specific features to a SQL Server implementation.

\- Prefer EF Core-compatible relational design.

\- Keep vector/retrieval functionality abstracted behind interfaces so the storage implementation can change later.



\## Module boundary rules



When implementing a feature, place it in the most relevant module:



\- Identity \& Tenant Access

\- Company Setup

\- Agent Management

\- Task \& Workflow

\- Approval

\- Communication

\- Knowledge \& Memory

\- Analytics \& Cockpit

\- Integration

\- Audit \& Explainability

\- AI Orchestration

\- Background Workers



Rules:



\- Do not place business logic directly in UI components.

\- Do not place orchestration logic directly in controllers.

\- Do not create direct cross-module dependencies when an application service, interface, event, or command can be used.

\- Prefer existing module patterns before creating new abstractions.

\- If a feature crosses modules, keep coordination in an application service or workflow handler.



\## Multi-tenancy rules



\- Every tenant-owned entity must include `CompanyId` or equivalent tenant scope.

\- Every query for tenant-owned data must be tenant-scoped.

\- Never return data across company boundaries.

\- Never trust client-provided company IDs without authorization checks.

\- Tenant resolution and access checks belong in application/backend services, not only in the UI.



\## Data and persistence rules



\- Use the existing database provider and existing EF Core conventions in the repository.

\- Use relational tables for core business concepts.

\- Use JSON columns only for flexible configuration, metadata, thresholds, settings, payload snapshots, or provider-specific data.

\- Do not store important queryable business state only inside JSON.

\- Use migrations for schema changes.

\- Keep schema changes backward-compatible when possible.

\- Update seed data and tests when adding required fields.



Core business entities should map to these concepts where relevant:



\- Company

\- User

\- CompanyMembership

\- AgentTemplate

\- Agent

\- Task

\- WorkflowDefinition

\- WorkflowInstance

\- Approval

\- ApprovalStep

\- Conversation

\- Message

\- KnowledgeDocument

\- KnowledgeChunk

\- MemoryItem

\- ToolExecution

\- AuditEvent

\- Integration



\## CQRS-lite rules



\- Use commands for state-changing operations.

\- Use queries/read models for dashboards, views, and summaries.

\- Do not implement full event sourcing unless explicitly requested.

\- Keep dashboard queries optimized and separate from transactional write logic.



\## Workflow rules



\- Treat workflow as a first-class subsystem.

\- Model recurring processes, triggers, escalations, retries, blocked states, and exceptions explicitly.

\- Do not implement workflow behavior as ad hoc UI logic.

\- Workflows may be triggered by:

&#x20; - manual user action

&#x20; - schedule

&#x20; - domain event

&#x20; - integration/webhook event



Workflow instances must store enough context to explain:



\- what triggered the workflow

\- current step

\- current state

\- related task or approval

\- failure/retry status where applicable



\## Outbox and side-effect rules



\- Use the outbox pattern for reliable side effects.

\- Do not directly perform external side effects inside request handlers when reliability matters.

\- Use background dispatchers/workers for:

&#x20; - notifications

&#x20; - integration sync

&#x20; - workflow progression

&#x20; - audit fan-out

&#x20; - long-running AI work

\- Failed side effects must be retryable or explicitly marked failed.



\## Agent architecture rules



\- Use one shared AI orchestration subsystem.

\- Do not create separate orchestration stacks per agent.

\- Named agents are configuration + memory + tools + permissions + role/persona.

\- Agent behavior should be driven by agent templates and company-specific configuration.

\- Avoid duplicated prompt-building, tool-calling, or policy logic.



An agent should have:



\- display name

\- role name

\- department

\- role brief

\- objectives

\- KPIs

\- autonomy level

\- tool permissions

\- data scopes

\- approval thresholds

\- escalation rules

\- working hours or availability if relevant



\## AI orchestration rules



All AI interactions must go through the AI Orchestration subsystem.



The orchestration flow should be:



1\. Resolve tenant and user context.

2\. Resolve addressed agent or coordinator.

3\. Load agent configuration and permissions.

4\. Retrieve scoped context and memory.

5\. Build prompt/runtime instructions.

6\. Select available tools.

7\. Apply policy checks before tool execution.

8\. Execute approved tools only.

9\. Return structured result.

10\. Store rationale summary, tool execution record, and audit event where applicable.



Do not call LLM APIs directly from feature modules unless the codebase already has an approved abstraction for that path.



\## Prompt builder rules



Prompt construction should include only relevant scoped context:



\- agent role brief

\- company context

\- current task or workflow context

\- applicable policies

\- memory snippets

\- relevant data records

\- available tool schemas



Rules:



\- Prefer structured output formats.

\- Avoid fragile string parsing.

\- Keep prompts deterministic where possible.

\- Do not include data outside the agent’s permitted scope.

\- Do not expose hidden policy or system implementation details to end users.



\## Context and memory rules



\- Context retrieval must be tenant-scoped.

\- Agent memory must be scoped by company and optionally by agent.

\- Company memory and role memory should be separate concepts.

\- Store summaries, not unlimited raw conversation history.

\- Use salience/importance and validity periods where appropriate.

\- Retrieval should prefer relevant business records, task history, documents, KPIs, and prior decisions.



\## Tool execution rules



\- Tools must be explicit and permissioned.

\- Every tool execution must include:

&#x20; - company/tenant scope

&#x20; - agent identity

&#x20; - action type: read, recommend, or execute

&#x20; - request payload

&#x20; - response payload or failure

&#x20; - policy decision

&#x20; - timestamps



Before executing a tool, check:



\- tenant scope

\- agent permission scope

\- autonomy level

\- threshold limits

\- approval requirements

\- compliance or region rules

\- redaction requirements



Tools should return structured results only.



\## Policy and guardrail rules



\- Guardrails must run before tool execution, not only after LLM generation.

\- Sensitive or high-impact actions must require approval.

\- Default autonomy must be conservative.

\- New agents should start at autonomy level 0 or 1.

\- Autonomy level 2 or 3 requires explicit configuration.

\- Policy failures should produce clear user-facing explanations and internal audit events.



\## Approval rules



Use the Approval module for actions requiring human review.



Approval logic must support:



\- threshold checks

\- required role or required user

\- approval chains

\- approval status

\- decision summary

\- rejection handling

\- cancellation/expiration where relevant



Do not bury approval requirements inside prompts only. Approval must be enforced in backend policy/application logic.



\## Audit and explainability rules



Auditability is a product feature, not only technical logging.



Persist business audit events for:



\- agent actions

\- tool executions

\- approvals

\- important workflow transitions

\- integration-triggered changes

\- sensitive recommendations or actions



Audit records should capture:



\- actor type: human, agent, or system

\- actor id where available

\- action

\- target type and id

\- outcome

\- rationale summary

\- data sources used

\- before/after snapshots where useful

\- correlation id

\- timestamp



Technical logs are separate from user-facing audit history.



\## Integration rules



\- Integrations are adapters, not systems of record.

\- Normalize imported/synced data into internal company data contracts.

\- Keep provider-specific auth and payload details inside the Integration module.

\- Use background sync jobs for recurring imports.

\- Use webhook ingestion where available.

\- Never let external provider schemas leak directly into core domain models unless intentionally mapped.



Supported integration categories may include:



\- email

\- calendar

\- CRM

\- accounting

\- ads

\- support

\- storage



\## Knowledge and document rules



The Knowledge \& Memory module owns:



\- document ingestion

\- uploaded files

\- SOPs

\- FAQs

\- playbooks

\- generated documents

\- chunks

\- embeddings

\- semantic search

\- company and agent memory



Rules:



\- Store documents separately from chunks.

\- Keep chunk metadata.

\- Track ingestion status.

\- Respect access scope during retrieval.

\- Do not retrieve knowledge across company boundaries.



\## Communication rules



Use Communication module for:



\- agent chat

\- shared company inbox

\- notifications

\- daily briefings

\- weekly summaries

\- task/workflow conversation threads

\- approval request messages



Do not use chat messages as the only source of truth for workflow/task state.



\## Analytics and cockpit rules



Use Analytics \& Cockpit module for:



\- executive dashboard

\- department scorecards

\- KPI aggregation

\- alerts

\- anomalies

\- forecasts

\- agent health

\- workload



Dashboard UI should use query/read models instead of assembling complex transactional data directly in components.



\## Background worker rules



Use background workers for:



\- scheduled briefings

\- workflow progression

\- retries

\- inbox and webhook processing

\- embedding generation

\- integration sync

\- notification dispatch

\- long-running AI tasks



Rules:



\- Workers must be idempotent where practical.

\- Workers must be tenant-aware.

\- Failed jobs must be logged and recoverable.

\- Long-running work must not block user-facing requests.



\## Manager-worker multi-agent rules



\- Cross-functional work should be coordinated by a manager/coordinator agent or orchestration plan.

\- Avoid free-form agent-to-agent chatter loops.

\- Multi-agent work should create explicit tasks, subtasks, outputs, and final consolidation.

\- Store delegated tasks and rationale summaries.



\## UI-facing language rules



\- Do not expose internal enum values or workflow codes directly to users.

\- Translate internal names into plain English.

\- Examples:

&#x20; - `finance\_approver` → “Finance approver”

&#x20; - `invoice\_review` → “Invoice review”

&#x20; - `multiple\_payments` → “Multiple payments”

\- User-facing explanations should be concise, specific, and action-oriented.



\## Security and trust rules



\- Never rely only on frontend checks for authorization.

\- Sensitive actions must be checked server-side.

\- Do not expose credentials, tokens, provider secrets, or raw encrypted auth config.

\- External integration credentials must be stored securely.

\- User-facing rationale should be explainable without leaking private prompt or policy internals.



\## Implementation checklist



Before completing a task, verify:



\- Correct module placement

\- Tenant isolation

\- Authorization checks

\- Policy checks before tool execution

\- Approval flow where required

\- Audit event where required

\- Outbox/background processing for reliable side effects

\- No direct LLM calls outside approved orchestration abstractions

\- No internal enum names exposed in UI

\- Database migration included when schema changes

\- Tests or seed data updated when relevant

