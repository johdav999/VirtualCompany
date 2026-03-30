# Architecture overview

Target platform inferred from the idea: **multi-tenant SaaS**, with a **web-first application**, **mobile companion**, **.NET backend**, **SQL transactional store**, **vector retrieval layer**, and **LLM-based shared orchestration engine**. The idea explicitly points to **Blazor Web App + ASP.NET Core + SQL + OpenAI**, with **.NET MAUI** as the natural mobile option.

Recommended architecture:

- **Frontend**
  - **Blazor Web App** for the executive cockpit, setup, dashboards, workflows, approvals, analytics, and agent management
  - **.NET MAUI mobile app** for alerts, approvals, quick chat, briefings, and task follow-up
- **Backend**
  - **ASP.NET Core modular monolith** for v1/v2, with clear internal module boundaries
  - **Background workers** for long-running workflows, inbox processing, scheduled jobs, and retries
  - **Shared AI orchestration subsystem** that instantiates many named agents from configuration rather than separate stacks
- **Data**
  - **PostgreSQL** as primary transactional source of truth
  - **pgvector** for semantic retrieval in the same database for v1 simplicity
  - **Object storage** for uploaded files and generated artifacts
  - **Redis** for caching, rate limiting, ephemeral conversation state, and job coordination
- **Messaging**
  - Start with **database-backed outbox + background dispatcher**
  - Add **message broker** later if workflow volume requires it
- **Hosting**
  - Containerized deployment on a cloud platform
  - Good fit: **Azure App Service / Container Apps + Azure Database for PostgreSQL**, or equivalent on AWS/GCP

Architectural style:

- **Modular monolith with DDD-lite**
- **Clean architecture boundaries**
- **Event-driven internal workflows**
- **Policy-enforced tool execution**
- **Human-in-the-loop approvals for sensitive actions**
- **Tenant-isolated data and agent execution context**

Core product flow:

1. User creates a company workspace
2. User hires/configures named agents
3. Integrations and documents populate the company data layer
4. Agents monitor triggers, receive tasks, and retrieve scoped memory/context
5. Orchestrator composes prompts, invokes tools, and enforces policies
6. Actions above thresholds create approval requests
7. Results, rationale summaries, and audit events are stored
8. Executive cockpit surfaces status, KPIs, alerts, and exceptions

---

# Key decisions

1. **Use a modular monolith first, not microservices**
   - The domain is broad but still early-stage.
   - Strong module boundaries are needed, but operational complexity of microservices would slow delivery.
   - Internal modules can later be extracted if needed.

2. **Use one shared orchestration engine with configurable agent personas**
   - Each agent is configuration + memory + permissions + tools.
   - This avoids duplicated prompt/tool logic and keeps cost and behavior manageable.

3. **Use PostgreSQL as both transactional store and initial vector store**
   - Simplifies operations and consistency.
   - pgvector is sufficient for v1/v2 semantic retrieval.
   - Can later split to dedicated retrieval/search infrastructure if scale demands it.

4. **Web-first, mobile-companion**
   - The product’s primary value is dashboards, workflows, approvals, and administration.
   - Mobile should optimize for executive actions, not full parity.

5. **Guardrails before tool execution, not only after generation**
   - Every action request must pass policy checks:
     - tenant scope
     - agent permission scope
     - autonomy level
     - threshold rules
     - approval requirements
   - This is essential for trust.

6. **Workflow engine is a first-class subsystem**
   - The product is not just chat.
   - Recurring processes, escalations, approvals, and exceptions must be modeled explicitly.

7. **Auditability is a domain feature, not just logging**
   - Operational logs, rationale summaries, data sources used, approvals, and outcomes are persisted in business tables.
   - Technical logs remain separate.

8. **Integrations are adapters, not systems of record**
   - v1 should orchestrate existing tools.
   - Imported/synced data is normalized into the company data layer for retrieval and analytics.

9. **Manager-worker multi-agent pattern**
   - Cross-functional tasks should be coordinated explicitly by an executive/coordinator agent or orchestration plan.
   - Avoid free-form agent chatter loops.

10. **Default autonomy is conservative**
   - New agents start at Level 0 or Level 1.
   - Level 2/3 requires explicit enablement and policy configuration.

11. **CQRS-lite for application layer**
   - Commands for state changes, queries for dashboards and views.
   - No need for full event sourcing in v1.

12. **Outbox pattern for reliable side effects**
   - Needed for notifications, integration sync, workflow progression, and audit fan-out.

---

# Components (diagram in text + responsibilities)

## High-level diagram

```text
[ Blazor Web App ] --------\
                             \ 
[ .NET MAUI Mobile App ] -----> [ API Gateway / ASP.NET Core App ]
                                      |
                                      +-- Identity & Tenant Access
                                      +-- Company Setup Module
                                      +-- Agent Management Module
                                      +-- Task & Workflow Module
                                      +-- Approval Module
                                      +-- Communication Module
                                      +-- Knowledge & Memory Module
                                      +-- Analytics & Cockpit Module
                                      +-- Integration Module
                                      +-- Audit & Explainability Module
                                      |
                                      +-- AI Orchestration Subsystem
                                      |      +-- Agent Registry
                                      |      +-- Prompt Builder
                                      |      +-- Context Retriever
                                      |      +-- Tool Executor
                                      |      +-- Policy Guardrail Engine
                                      |      +-- Multi-Agent Coordinator
                                      |
                                      +-- Background Workers
                                      |      +-- Scheduler
                                      |      +-- Workflow Runner
                                      |      +-- Inbox Processor
                                      |      +-- Integration Sync Jobs
                                      |      +-- Notification Dispatcher
                                      |
                                      +-- Persistence
                                             +-- PostgreSQL
                                             +-- pgvector
                                             +-- Redis
                                             +-- Object Storage
                                             +-- External LLM Provider
                                             +-- External SaaS Integrations
```

## Module responsibilities

### 1. Identity & Tenant Access
Responsibilities:
- Authentication
- Human user roles and permissions
- Tenant resolution
- Session and API token handling
- SSO later

Key concepts:
- User
- Company
- Membership
- Human role
- Tenant-scoped authorization

### 2. Company Setup Module
Responsibilities:
- Create workspace/company
- Configure timezone, currency, language, region
- Branding and templates
- Invite users
- Initial setup wizard

### 3. Agent Management Module
Responsibilities:
- Hire agent from template
- Configure identity, role, personality, seniority
- Configure objectives, KPIs, tools, data scopes, thresholds, escalation rules
- Pause/restrict/archive agent
- Agent roster and profile views

### 4. Task & Workflow Module
Responsibilities:
- Task creation, assignment, status tracking
- Workflow definitions and instances
- Trigger handling: user, schedule, event
- Escalations, blocked states, retries
- Recurring workflows

### 5. Approval Module
Responsibilities:
- Approval policies
- Approval requests and chains
- Threshold checks
- Human review actions
- Override and rejection handling

### 6. Communication Module
Responsibilities:
- Agent chat
- Shared company inbox
- Notifications
- Daily briefings and weekly summaries
- Agent-to-agent task delegation messages

### 7. Knowledge & Memory Module
Responsibilities:
- Document ingestion
- SOPs, FAQs, playbooks, uploaded files
- Agent memory storage and retrieval
- Company memory and task history
- Embedding generation and semantic search

### 8. Analytics & Cockpit Module
Responsibilities:
- Executive dashboard
- Department scorecards
- KPI aggregation
- Alerts, anomalies, forecasts
- Agent health and workload

### 9. Integration Module
Responsibilities:
- Connectors for email, calendar, CRM, accounting, ads, support, storage
- OAuth/token management
- Sync jobs
- Normalization into internal data contracts
- Webhook ingestion

### 10. Audit & Explainability Module
Responsibilities:
- Persist operational audit events
- Store rationale summaries
- Track data sources used
- Expose user-facing action history
- Support compliance review

### 11. AI Orchestration Subsystem
Responsibilities:
- Resolve addressed agent
- Build runtime context
- Retrieve scoped memory
- Select tools
- Enforce policy checks
- Execute tool calls
- Coordinate multi-agent plans
- Produce structured outputs and rationale summaries

Subcomponents:

#### Agent Registry
- Stores agent definitions and templates
- Resolves capabilities and permissions

#### Prompt Builder
- Builds system/runtime prompts from:
  - role brief
  - company context
  - current task
  - policies
  - memory snippets
  - tool schemas

#### Context Retriever
- Pulls:
  - recent task history
  - company docs
  - role memory
  - relevant records
  - KPI context

#### Tool Executor
- Executes approved tools against internal modules or external integrations
- Returns structured results only

#### Policy Guardrail Engine
- Checks:
  - read/recommend/execute scope
  - autonomy level
  - threshold limits
  - compliance region rules
  - approval requirements
  - redaction rules

#### Multi-Agent Coordinator
- Creates task plans
- Delegates subtasks
- Collects outputs
- Consolidates final response

### 12. Background Workers
Responsibilities:
- Scheduled briefings
- Workflow progression
- Retry failed jobs
- Process inbox/webhooks
- Generate embeddings
- Sync integrations
- Long-running AI tasks

---

# Data model & storage (include persistence choice and schemas)

## Persistence choices

### Primary transactional store: PostgreSQL
Why:
- Strong relational modeling for workflows, approvals, tasks, users, agents
- Good support for JSONB for flexible configs
- pgvector support for semantic retrieval
- Mature multi-tenant SaaS fit

### Vector retrieval: pgvector in PostgreSQL
Why:
- Keeps v1 architecture simple
- Supports semantic search over documents, memory, and summaries
- Avoids operating a separate vector DB initially

### Cache and ephemeral state: Redis
Use for:
- Session/cache
- dashboard query cache
- rate limiting
- short-lived orchestration state
- distributed locks for scheduled jobs

### File storage: object storage
Use for:
- uploaded documents
- generated reports
- exported audit bundles
- avatars/branding assets

## Multi-tenancy strategy

Recommended:
- **Shared database, shared schema, tenant_id on all tenant-owned tables**
- Enforce tenant isolation in:
  - repository/query layer
  - application services
  - row-level security if desired
- For enterprise/data residency later:
  - support per-region database clusters

## Core schemas

Below is a pragmatic relational model.

### companies
```sql
companies (
  id uuid pk,
  name text,
  industry text,
  business_type text,
  timezone text,
  currency text,
  language text,
  compliance_region text,
  plan text,
  status text,
  branding_json jsonb,
  settings_json jsonb,
  created_at timestamptz,
  updated_at timestamptz
)
```

### users
```sql
users (
  id uuid pk,
  email citext unique,
  display_name text,
  auth_provider text,
  auth_subject text,
  created_at timestamptz,
  updated_at timestamptz
)
```

### company_memberships
```sql
company_memberships (
  id uuid pk,
  company_id uuid fk,
  user_id uuid fk,
  role text,
  permissions_json jsonb,
  status text,
  created_at timestamptz,
  updated_at timestamptz
)
```

### agent_templates
```sql
agent_templates (
  id uuid pk,
  role_name text,
  department text,
  default_persona_json jsonb,
  default_objectives_json jsonb,
  default_kpis_json jsonb,
  default_tools_json jsonb,
  default_scopes_json jsonb,
  default_thresholds_json jsonb,
  default_escalation_rules_json jsonb,
  created_at timestamptz
)
```

### agents
```sql
agents (
  id uuid pk,
  company_id uuid fk,
  template_id uuid fk null,
  display_name text,
  role_name text,
  department text,
  avatar_url text,
  seniority text,
  status text, -- active, paused, restricted, archived
  autonomy_level int,
  personality_json jsonb,
  role_brief text,
  objectives_json jsonb,
  kpis_json jsonb,
  tool_permissions_json jsonb,
  data_scopes_json jsonb,
  approval_thresholds_json jsonb,
  escalation_rules_json jsonb,
  trigger_logic_json jsonb,
  working_hours_json jsonb,
  created_at timestamptz,
  updated_at timestamptz
)
```

### tasks
```sql
tasks (
  id uuid pk,
  company_id uuid fk,
  assigned_agent_id uuid fk null,
  created_by_actor_type text, -- human, agent, system
  created_by_actor_id uuid null,
  type text,
  title text,
  description text,
  priority text,
  status text, -- new, in_progress, blocked, awaiting_approval, completed, failed
  due_at timestamptz null,
  input_payload jsonb,
  output_payload jsonb,
  rationale_summary text,
  confidence_score numeric(5,2) null,
  parent_task_id uuid null,
  workflow_instance_id uuid null,
  created_at timestamptz,
  updated_at timestamptz,
  completed_at timestamptz null
)
```

### workflow_definitions
```sql
workflow_definitions (
  id uuid pk,
  company_id uuid fk null, -- null for system templates
  code text,
  name text,
  department text,
  version int,
  trigger_type text, -- manual, schedule, event
  definition_json jsonb,
  active boolean,
  created_at timestamptz,
  updated_at timestamptz
)
```

### workflow_instances
```sql
workflow_instances (
  id uuid pk,
  company_id uuid fk,
  workflow_definition_id uuid fk,
  trigger_source text,
  trigger_ref text null,
  state text,
  current_step text,
  context_json jsonb,
  started_at timestamptz,
  updated_at timestamptz,
  completed_at timestamptz null
)
```

### approvals
```sql
approvals (
  id uuid pk,
  company_id uuid fk,
  entity_type text, -- task, workflow, action
  entity_id uuid,
  requested_by_actor_type text,
  requested_by_actor_id uuid null,
  approval_type text,
  threshold_context_json jsonb,
  required_role text null,
  required_user_id uuid null,
  status text, -- pending, approved, rejected, expired, cancelled
  decision_summary text null,
  created_at timestamptz,
  decided_at timestamptz null
)
```

### approval_steps
```sql
approval_steps (
  id uuid pk,
  approval_id uuid fk,
  sequence_no int,
  approver_type text, -- role, user
  approver_ref text,
  status text,
  decided_by_user_id uuid null,
  decided_at timestamptz null,
  comment text null
)
```

### conversations
```sql
conversations (
  id uuid pk,
  company_id uuid fk,
  channel_type text, -- direct_agent, task_thread, workflow_thread, inbox
  subject text,
  created_by_user_id uuid null,
  created_at timestamptz,
  updated_at timestamptz
)
```

### messages
```sql
messages (
  id uuid pk,
  company_id uuid fk,
  conversation_id uuid fk,
  sender_type text, -- human, agent, system
  sender_id uuid null,
  message_type text, -- text, summary, alert, approval_request
  body text,
  structured_payload jsonb null,
  created_at timestamptz
)
```

### knowledge_documents
```sql
knowledge_documents (
  id uuid pk,
  company_id uuid fk,
  title text,
  document_type text, -- sop, faq, playbook, upload, note
  source_type text, -- upload, integration, generated
  source_ref text null,
  storage_url text,
  metadata_json jsonb,
  access_scope_json jsonb,
  ingestion_status text,
  created_at timestamptz,
  updated_at timestamptz
)
```

### knowledge_chunks
```sql
knowledge_chunks (
  id uuid pk,
  company_id uuid fk,
  document_id uuid fk,
  chunk_index int,
  content text,
  embedding vector(1536), -- size depends on model
  metadata_json jsonb,
  created_at timestamptz
)
```

### memory_items
```sql
memory_items (
  id uuid pk,
  company_id uuid fk,
  agent_id uuid fk null,
  memory_type text, -- preference, decision_pattern, summary, role_memory, company_memory
  summary text,
  source_entity_type text,
  source_entity_id uuid null,
  salience numeric(5,2),
  valid_from timestamptz,
  valid_to timestamptz null,
  embedding vector(1536),
  metadata_json jsonb,
  created_at timestamptz
)
```

### tool_executions
```sql
tool_executions (
  id uuid pk,
  company_id uuid fk,
  task_id uuid fk null,
  workflow_instance_id uuid fk null,
  agent_id uuid fk,
  tool_name text,
  action_type text, -- read, recommend, execute
  request_json jsonb,
  response_json jsonb,
  status text,
  policy_decision_json jsonb,
  started_at timestamptz,
  completed_at timestamptz null
)
```

### audit_events
```sql
audit_events (
  id uuid pk,
  company_id uuid fk,
  actor_type text, -- human, agent, system
  actor_id uuid null,
  action text,
  target_type text,
  target_id uuid null,
  outcome text,
  rationale_summary