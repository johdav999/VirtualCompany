# One-liner
A SaaS “virtual company” platform where founders and managers run business operations through persistent AI role-agents—each with defined responsibilities, permissions, memory, workflows, and KPIs—via a shared executive control room.

# Problem
Small businesses and lean teams often lack the budget, time, or operational maturity to staff every business function well across finance, sales, marketing, support, HR, and operations.

Current AI products mostly behave like generic assistants: they answer prompts, but they do not operate as a persistent company system with clear ownership, boundaries, memory, and accountability. This creates several gaps:

- Work is fragmented across tools and chats
- There is no durable “owner” for recurring business functions
- Users must repeatedly restate context and preferences
- Cross-functional coordination is manual
- Trust is low because actions, permissions, and rationale are unclear
- Full autonomy feels risky without approvals, logs, and guardrails

The opportunity is to turn AI from an ad hoc assistant into a structured operating model: a digital company staffed by named AI employees that continuously monitor, recommend, draft, and—within limits—execute business work.

# Target users & primary persona
## Target users
- Solo founders running early-stage companies
- Small business owners with limited headcount
- Startup operators managing multiple functions at once
- Agency owners or service business operators
- SMB leadership teams that want AI-assisted operations before hiring full departments

## Primary persona
**Founder-Operator of a small or growing business**

Characteristics:
- Owns or oversees multiple business functions
- Uses several SaaS tools but lacks integrated operational visibility
- Wants leverage without hiring a full team
- Needs help with recurring work, prioritization, and exception handling
- Is interested in AI, but only if it is trustworthy, auditable, and controllable

Primary goals:
- Run the business with fewer manual handoffs
- Get proactive alerts and recommendations
- Delegate routine work safely
- Maintain approval control over sensitive actions
- See company-wide health in one place

# Top use cases (5)
1. **Run the business from an executive cockpit**
   - View daily briefings, alerts, KPIs, pending approvals, and department summaries from AI agents such as accounting, sales, marketing, and support.

2. **Delegate function-specific work to named AI agents**
   - Ask Laura the Accountant to close books, Alex the Sales Rep to follow up leads, or Ben the Support Lead to summarize churn risks.

3. **Automate recurring workflows with approval guardrails**
   - Let agents draft reports, triage tickets, categorize invoices, schedule approved campaigns, and escalate exceptions based on autonomy level.

4. **Coordinate cross-functional planning through multi-agent collaboration**
   - Request a business plan or operational review that combines finance, sales, marketing, and operations inputs into one recommendation.

5. **Review auditable decisions and override when needed**
   - Inspect what an agent did, what data it used, why it made a recommendation, whether approvals were required, and intervene when necessary.

# Functional requirements
## Company workspace and setup
- Create and manage a company workspace
- Configure company profile, industry, branding, timezone, currency, language, and compliance region
- Invite human users and assign roles
- Support industry or business-type templates for faster onboarding

## AI workforce management
- Create or “hire” AI agents from role templates
- Configure agent identity:
  - name
  - avatar
  - department
  - role description
  - personality/tone
  - seniority
- Configure agent operating profile:
  - objectives
  - KPIs
  - allowed tools
  - allowed data scopes
  - approval thresholds
  - escalation rules
  - autonomy level
  - working hours or trigger logic
- Enable agent status management (active, paused, restricted)

## Agent interaction and communication
- Chat with individual agents
- Assign tasks directly to an agent
- Support agent-to-agent collaboration for cross-functional work
- Provide shared notifications, daily briefings, and weekly executive summaries
- Support a shared company inbox or communication queue for relevant workflows

## Task and workflow orchestration
- Create, assign, track, and complete tasks
- Trigger predefined workflows by user action, schedule, or event
- Support recurring business processes
- Route tasks to the correct agent based on role and permissions
- Manage exceptions, escalations, and blocked states
- Support approval checkpoints before sensitive actions

## Departmental capabilities
The product should support launch agents and workflows across:
- Accounting & finance
- Sales
- Marketing
- Customer support
- Operations
- Executive assistant / chief of staff

Examples:
- Finance: invoice coding, reconciliation support, monthly summaries, anomaly detection
- Sales: lead follow-up drafts, CRM updates, pipeline reviews
- Marketing: campaign planning, content drafts, performance summaries
- Support: ticket triage, response drafts, issue trend summaries
- Operations: bottleneck detection, vendor follow-up, task coordination
- Executive assistant: daily briefings, summaries, reminders, cross-agent coordination

## Knowledge and memory
- Store company documents, SOPs, FAQs, playbooks, and uploaded files
- Maintain company-wide knowledge accessible by permission
- Maintain agent-specific memory:
  - long-term role memory
  - company-specific memory
  - task history
  - learned user preferences
- Retrieve relevant historical context for tasks and recommendations

## Executive cockpit and analytics
- Show company-wide dashboard with:
  - alerts
  - pending approvals
  - department KPIs
  - activity feed
  - anomalies
  - forecasts
- Provide department scorecards
- Provide recommendation summaries and trend explanations
- Surface agent health and workload status

## Permissions, approvals, and guardrails
- Support human roles such as owner, admin, manager, employee, finance approver, support supervisor
- Enforce agent-specific scoped permissions:
  - read scope
  - recommend scope
  - execute scope
  - threshold limits
- Support autonomy levels:
  - Level 0: advisor
  - Level 1: draft mode
  - Level 2: guardrailed execution
  - Level 3: managed autonomy
- Require approvals for actions above configured thresholds
- Allow human override of agent decisions and workflow outcomes

## Auditability and explainability
- Log all important agent actions and workflow events
- Record:
  - actor
  - action taken
  - target entity
  - timestamp
  - approval status
  - outcome
  - short rationale summary
  - relevant data sources used
- Provide user-facing operational explanations without exposing raw chain-of-thought

## Integrations
- Connect to common business systems as available, such as:
  - email
  - calendar
  - CRM
  - accounting systems
  - ad platforms
  - support platforms
  - payment providers
  - document/file storage
- Sync relevant data into the company data layer with permission controls

## Platform surfaces
- Web app as the primary command center for setup, dashboards, workflows, analytics, and administration
- Mobile app focused on:
  - alerts
  - approvals
  - quick status
  - chat with agents
  - task follow-up
  - daily briefing

# Non-goals
- Replacing all human employees in medium or large enterprises
- Offering unrestricted autonomous execution from day one
- Acting as a general-purpose consumer chatbot
- Building deep ERP-grade functionality for every department in v1
- Supporting every industry-specific compliance regime at launch
- Providing raw model reasoning or chain-of-thought visibility
- Becoming a full custom workflow builder for any arbitrary business process in the initial release
- Serving as the system of record for all external business domains immediately; initially it should orchestrate and augment existing tools where possible

# Quality attributes (NFRs)
- **Trustworthy:** clear permissions, approval gates, and rationale summaries for important actions
- **Auditable:** complete operational logs for agent actions, approvals, and workflow outcomes
- **Secure:** least-privilege access, tenant isolation, encryption in transit and at rest
- **Reliable:** resilient workflow execution, retries for background jobs, graceful failure handling
- **Responsive:** fast dashboard loads and acceptable chat/task response times for interactive use
- **Scalable:** support multiple companies, multiple agents per company, and growing workflow volume
- **Configurable:** agents, autonomy levels, thresholds, and policies must be adjustable without code changes
- **Maintainable:** shared orchestration engine with configurable personas rather than bespoke stacks per agent
- **Explainable:** concise, user-friendly reasons for recommendations and escalations
- **Observable:** monitoring, logging, alerting, and agent/workflow health visibility
- **Privacy-aware:** scoped data access, redaction where needed, and controlled memory retention

# Constraints & assumptions
- Product is a multi-tenant SaaS platform
- Primary domain is SMB business operations and management
- Initial product value comes from persistent role-based AI agents, not generic chat
- Trust and staged autonomy are essential to adoption
- SQL is the transactional source of truth for core entities and workflows
- A vector retrieval layer is needed for semantic knowledge and memory access
- AI agents should share a common orchestration framework with per-agent configuration
- Web is the primary surface; mobile is a companion experience
- Integrations are important, but the product can launch with a limited set of high-value connectors
- Sensitive actions must be guardrailed by permissions and approval thresholds
- The product should support both AI-only teams and hybrid human + AI teams
- Initial launch should focus on a small set of high-value departments rather than broad enterprise coverage

# Success metrics
## Product adoption
- Workspace activation rate
- Percentage of new companies that configure at least 3 agents
- Time to first completed agent-assisted workflow
- Weekly active companies
- Weekly active users interacting with agents

## Engagement
- Average number of agent interactions per company per week
- Percentage of companies using more than one department agent
- Number of recurring workflows enabled per company
- Daily briefing open rate
- Approval task completion rate

## Operational value
- Tasks completed or drafted by agents per company
- Reduction in manual handling time for common workflows
- Percentage of low-risk tasks handled at Level 1 or Level 2 autonomy
- Escalation rate vs. successful autonomous completion rate
- Cross-agent workflow completion rate

## Trust and quality
- Approval override rate
- User-reported confidence/trust score
- Audit log access rate for reviewed actions
- Error rate in agent-executed workflows
- Hallucination or incorrect-action incident rate

## Commercial
- Conversion from trial to paid
- Expansion by number of agents or departments enabled
- Retention by company cohort
- ARPU by plan tier tied to agents, autonomy, and integrations

# Risks & mitigations
## 1. Low trust in autonomous AI actions
**Risk:** Users may hesitate to let agents act on business data or execute workflows.

**Mitigations:**
- Start with advisor and draft modes by default
- Introduce staged autonomy with explicit thresholds
- Require approvals for sensitive actions
- Provide concise rationale summaries and full audit trails
- Make override and rollback paths obvious

## 2. Generic assistant experience instead of “virtual company”
**Risk:** The product may feel like a thin chat wrapper rather than a persistent operating model.

**Mitigations:**
- Emphasize named agents with durable roles and memory
- Build dashboards, task ownership, and recurring workflows into the core UX
- Show proactive updates and department-specific KPIs
- Make agent identity and responsibility boundaries visible

## 3. Permission or data-scope failures
**Risk:** Agents may access or act on data beyond intended boundaries.

**Mitigations:**
- Enforce least-privilege permissions at tool and data layer
- Separate read, recommend, and execute scopes
- Add policy checks before tool execution
- Log all access and action events
- Test high-risk scenarios extensively

## 4. Hallucinations or poor business recommendations
**Risk:** Agents may produce incorrect summaries, classifications, or recommendations.

**Mitigations:**
- Ground outputs in connected systems and retrieved company knowledge
- Use structured workflows and tool calls instead of freeform generation where possible
- Keep humans in the loop for high-impact actions
- Provide confidence indicators or exception flags
- Continuously evaluate role-specific quality

## 5. Integration complexity slows delivery
**Risk:** Supporting many external systems can delay launch and increase maintenance burden.

**Mitigations:**
- Prioritize a narrow set of high-value integrations
- Use a modular integration layer
- Launch with manual upload/import fallback where needed
- Focus v1 on workflows that work with limited connectors

## 6. Multi-agent coordination becomes expensive or chaotic
**Risk:** Separate agent stacks or uncontrolled collaboration can increase cost and unpredictability.

**Mitigations:**
- Use one shared orchestration engine with configurable personas
- Apply manager-worker coordination patterns
- Limit collaboration to explicit task delegation flows
- Track token/cost usage and optimize retrieval/prompting

## 7. Overbuilding enterprise breadth too early
**Risk:** Trying to cover every department and industry at launch dilutes product quality.

**Mitigations:**
- Start with a focused launch set of agents and workflows
- Target SMB/general business use cases first
- Add vertical depth only after validating core operating model

# Open questions
1. Which business segment should v1 target first: startups, agencies, ecommerce brands, service businesses, or general SMBs?
2. Which 3–4 department agents should be included in the initial launch to maximize value and simplicity?
3. Which integrations are mandatory for v1 to make the product useful on day one?
4. Should the first release prioritize AI-only teams, or hybrid teams where humans and agents share workflows?
5. What actions are allowed at each autonomy level by default, and how much can customers customize them?
6. What approval model is needed: single approver, threshold-based, role-based, or multi-step chains?
7. How should agent memory retention and deletion work for privacy, compliance, and user control?
8. What level of explanation is sufficient for trust without overwhelming users?
9. Should the executive cockpit be centered on departments, agents, workflows, or business outcomes?
10. How opinionated should setup be—template-driven by industry, or highly customizable from the start?
11. What is the pricing axis for launch: number of agents, autonomy level, workflow volume, integrations, or seats?
12. What compliance and data residency requirements must be supported in the first target market?
