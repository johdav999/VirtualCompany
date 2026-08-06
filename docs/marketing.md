# Marketing Agent Concept

## Purpose

The Marketing Agent should help a small or medium-sized company plan, prepare, coordinate, measure, and improve demand-generation and customer-marketing work. It should operate as a governed member of the agent team, not as an autonomous advertising platform or a second sales system.

The agent should turn company strategy, product knowledge, audience evidence, campaign results, and approved brand policy into:

- measurable marketing plans;
- campaign briefs and activity plans;
- source-backed content drafts;
- audience and channel recommendations;
- coordinated work for Sales, Support, Finance, and human owners;
- performance analysis and next-step recommendations.

The default Marketing Agent may be named **Maya**, with the role **Marketing Manager**. The display name must remain configurable. The existing `marketing` agent template is the foundation and should be onboarded through the same governed company-agent flow as Laura, Alex, and Ben.

## Current Repository Foundation

Virtual Company already contains much of the required foundation:

- `AgentTemplate` includes an active `marketing` template for a Marketing Manager.
- `CoreCompanyAgentSeeder` currently onboards Finance, Sales, and Support, but not Marketing.
- Agent Management already governs role briefs, documents, data scopes, trusted tools, autonomy, approvals, mailbox access, and capability availability.
- Shared Agent AI provides grounded questions, role briefings, prioritization, planning, exception interpretation, handoffs, memory proposals, and quality events.
- The campaign implementation supports campaign objectives, ownership, schedules, offers, B2B and B2C audiences, consent-aware snapshots, activities, costs, performance, and lifecycle control.
- Sales owns prospects, contacts, companies, deals, pipeline, outbound email execution, replies, and commercial follow-up.
- Finance owns booked revenue, costs, budgets, accounting evidence, and payment authority.
- Support owns cases, customer issues, reply safety, knowledge gaps, and complaint evidence.
- The Agent Team board exposes planned, ongoing, waiting-for-human-approval, and completed work.

The Marketing Agent should extend these boundaries. It must not introduce parallel campaign, contact, product, task, approval, document, integration, or AI orchestration stores.

## Role And Business Value

The Marketing Agent is responsible for improving qualified demand and customer engagement while preserving brand, consent, privacy, budget, and product accuracy.

Its primary business outcomes are:

- increase qualified demand;
- improve campaign efficiency;
- maintain consistent, approved messaging;
- connect marketing activity to pipeline, customers, and revenue evidence;
- reduce missed follow-ups and uncoordinated campaign work;
- make marketing performance and tradeoffs understandable to an SME owner.

The agent should be useful before advanced integrations exist. It can plan and track manual activities, analyze repository data, and create reviewable drafts. Connected providers may later execute approved activities through trusted tools.

## Responsibility Boundaries

### Marketing Agent Owns

- marketing objectives and operating plans;
- campaign briefs and marketing work plans;
- audience hypotheses and segment recommendations;
- content briefs, variants, and review-ready copy;
- content and channel calendars;
- brand and claim checks against approved knowledge;
- marketing-qualified demand criteria and evidence;
- campaign performance interpretation;
- experiment design and post-campaign reviews;
- marketing-to-sales handoff preparation;
- requests for budget, product, legal, or brand approval.

### Alex And Sales Own

- lead and opportunity qualification;
- contact-level sales outreach and follow-up;
- pipeline stages and deal state;
- proposals, prices, terms, and sales forecasts;
- sales sequences and provider-backed commercial email execution;
- accepting, rejecting, or returning marketing-qualified demand;
- opportunity and won-revenue attribution evidence.

Marketing can recommend a handoff or prepare an approved campaign activity. It cannot change pipeline state, qualify an opportunity, promise a price, or send sales outreach outside the existing Sales execution boundary.

### Laura And Finance Own

- booked costs and revenue;
- accounting treatment;
- payment and spending authority;
- budget policy and financial approval;
- authoritative ROI amounts where accounting evidence is required.

Marketing may plan a budget and associate provider cost evidence. It cannot book costs, approve spend, or manufacture revenue.

### Ben And Support Own

- support cases and customer-risk evidence;
- complaint and escalation handling;
- support replies and service commitments;
- knowledge-gap resolution workflow.

Marketing must suppress or review audiences affected by complaints, critical support issues, or unsuitable customer circumstances according to deterministic policy.

### Human Owners Own

- strategy and final business priorities;
- brand approval;
- sensitive or regulated claims;
- material budget decisions;
- public publishing where policy requires review;
- high-impact audience, channel, or offer changes;
- crisis communication.

## Marketing Operating Model

### 1. Understand

The agent reads permitted company sources:

- company description, positioning, and strategy;
- product catalog, prices, target customers, and approved claims;
- company and brand policies;
- approved FAQ and support knowledge;
- campaign history and audience results;
- sales pipeline and attribution summaries;
- customer and support patterns;
- finance-approved budget and cost summaries;
- connected analytics, CMS, advertising, web, and mailbox data when configured.

It identifies missing, stale, conflicting, or unapproved knowledge before making recommendations.

### 2. Plan

The agent prepares a bounded marketing plan with:

- business objective and marketing objective;
- target audience and exclusions;
- offer or no-offer purpose;
- positioning and approved proof;
- campaign or initiative;
- channels and activities;
- owner, dates, milestones, and dependencies;
- planned budget and approval needs;
- KPI definitions, baseline, target, and measurement window;
- risks, assumptions, and missing evidence.

Plans remain drafts until an authorized user commits them as durable campaign activities, tasks, or approval requests.

### 3. Prepare

The agent creates review-ready assets:

- campaign brief;
- content brief;
- email or newsletter copy;
- landing-page copy;
- social post drafts;
- webinar or event outline;
- advertisement copy variants;
- sales enablement summary;
- FAQ or support handoff notes.

Every factual claim should be traceable to approved company knowledge. Unknown product behavior, customer facts, prices, legal claims, and performance values must be identified rather than invented.

### 4. Coordinate

The agent coordinates work through existing durable boundaries:

- campaign activities for marketing work;
- tasks for manual activity;
- approvals for content, budget, audience, and launch;
- handoffs to Alex for lead follow-up;
- handoffs to Ben for support-sensitive content or emerging issues;
- handoffs to Laura for budget or cost review;
- Agent Team board updates for planned, ongoing, waiting, and completed work.

### 5. Observe

The agent monitors:

- activity completion;
- audience eligibility and suppression;
- delivery and engagement;
- web enquiries and conversions;
- marketing-qualified demand;
- pipeline association;
- campaign costs;
- unsubscribe, bounce, complaint, and support-risk signals;
- data freshness and provider failures.

It must distinguish unavailable data from a true zero.

### 6. Improve

The agent explains performance, proposes experiments, and prepares post-campaign reviews. It may recommend continuing, pausing, changing, or stopping work, but deterministic policy and authorized users control lifecycle transitions and external side effects.

## Core Capabilities

### Grounded Marketing Questions

Answer questions about products, audiences, prior campaigns, approved positioning, content, and performance using company-scoped sources with citations and freshness information.

### Marketing Briefings

Prepare daily, weekly, monthly, and event-driven briefings:

- work due or blocked;
- campaigns at risk;
- approvals waiting;
- significant performance changes;
- new demand and sales handoffs;
- budget or provider issues;
- content and knowledge gaps.

### Marketing Plan Builder

Turn a business goal into a bounded draft plan. The plan should show assumptions, alternatives, dependencies, expected evidence, and the actions requiring approval.

### Campaign Strategy

Use the existing campaign initiative model to recommend:

- campaign type;
- objective and target;
- audience;
- offer;
- channels;
- activities and timing;
- budget allocation;
- KPI and attribution plan.

### Audience Intelligence

Explain segment size, eligibility, exclusions, consent, language, fit, engagement, sales state, and support risk. AI may explain and suggest criteria, but the deterministic segment evaluator decides membership.

### Content Studio

Generate source-backed drafts against a content brief, brand profile, channel constraints, communication language, and required approval state. Keep variants linked to their brief, campaign, evidence, reviewer, and outcome.

### Channel And Calendar Planning

Create a unified calendar of executable and manually tracked work. Initial implementation should reuse campaign activities and existing task scheduling. New channel providers should be registered as trusted tools rather than embedded directly in the Marketing Agent.

### Performance Analysis

Explain reach, engagement, conversion, pipeline association, revenue evidence, cost, and negative signals. Report metric definitions, source timestamps, attribution model, confidence, and missing data.

### Experimentation

Propose controlled variants with:

- one primary hypothesis;
- bounded audience allocation;
- success and guardrail metrics;
- minimum evidence threshold;
- planned duration;
- stop conditions;
- review requirements.

The agent must not declare a winner from insufficient evidence.

### Marketing-to-Sales Handoff

Create a typed, durable handoff containing:

- contact or account identifiers;
- campaign and source evidence;
- engagement summary;
- consent and language state;
- qualification hypothesis;
- suggested next action;
- urgency and expiry;
- missing evidence.

Alex or a human Sales owner decides whether to accept and qualify it.

### Knowledge And Memory Proposals

Identify reusable findings such as effective audience language or recurring objections. Proposals require evidence and review before becoming active company memory or approved marketing guidance.

## B2B And B2C Support

### B2B

Marketing should support:

- account-based marketing;
- industry and company-size segments;
- buying-group roles;
- content and event programs;
- lead-generation and nurture campaigns;
- marketing-qualified account and lead handoffs;
- pipeline and opportunity influence.

Important B2B measures include engaged accounts, qualified contacts, meetings, opportunities, pipeline, win association, and sales-cycle changes.

### B2C

Marketing should support:

- product launches;
- promotions;
- lifecycle and retention communication;
- re-engagement;
- cross-sell and upsell;
- newsletters and educational content;
- surveys;
- purchase and renewal outcomes.

Important B2C measures include reach, engagement, conversion, units, revenue, average order value, acquisition cost, return on campaign spend, repeat purchase, unsubscribe, complaint, and bounce rates.

Large B2C audiences require strict consent, frequency, provider quota, suppression, batching, and reconciliation controls.

## Data And Source Model

Marketing reads data through owning application boundaries. It should not query another module's tables directly.

| Source | Owner | Marketing use |
| --- | --- | --- |
| Company brief and policies | Company/Agent Management | Positioning, brand, constraints |
| Product catalog and approved claims | Company knowledge | Offers, content, proof |
| Campaigns, segments, activities, KPI snapshots | Sales campaign boundary | Planning and performance |
| Prospects, contacts, accounts, deals | Sales | Demand, fit, pipeline association |
| Cases, complaints, knowledge gaps | Support | Suppression, messaging risk, insight |
| Budgets, costs, booked revenue | Finance | Spend governance and ROI evidence |
| Web enquiries and forms | Mailbox/Web ingestion | Demand and conversion evidence |
| Marketing mailbox | Mailbox | Requests, partner communication, campaign responses |
| Analytics, CMS, ads, social, events | Trusted tool registry | Provider-scoped activity and metrics |

Every derived insight should include source identifiers, source time, confidence, and missing evidence. Provider metrics must be normalized without discarding the original provider reference.

## Tool And Access Model

The Marketing Agent should have a dedicated access profile in Agent Management.

Recommended default scopes:

- read approved company, product, policy, and brand documents;
- read campaign, segment, activity, and performance data;
- read permitted sales summaries, not unrestricted private communications;
- read permitted support trends and suppression signals;
- read finance-approved budget and campaign cost summaries;
- read and draft against a marketing mailbox;
- create marketing plans, briefs, content drafts, tasks, handoffs, and approval requests.

Write or execute access should be explicit:

- CMS publishing;
- marketing-email delivery;
- paid advertising;
- social publishing;
- event-platform changes;
- budget commitments.

Credentials remain in provider configuration or the secret store, never in agent profiles, prompts, audit descriptions, or documents.

## Autonomy And Approval

Recommended default autonomy is **Guided**.

### May Run Without Separate Approval

- retrieve permitted evidence;
- calculate deterministic summaries;
- answer grounded internal questions;
- prepare plans and drafts;
- create internal review tasks;
- identify risks and missing information;
- recommend handoffs.

### Requires Approval

- public publishing;
- bulk or promotional sends;
- audience launch or material segment change;
- paid spend or budget commitment;
- new or changed product, price, legal, environmental, security, or comparative claim;
- discount or commercial offer;
- use of sensitive personal data;
- automated handoff rules with customer impact;
- campaign launch, pause, or stop when existing policy requires it.

### Never Allowed Directly From Model Output

- execute provider side effects;
- override consent or suppression;
- approve its own work;
- change booked finance data;
- qualify or close sales opportunities;
- resolve support complaints;
- store hidden chain-of-thought or credentials.

## AI Safety And Quality

Marketing AI output must:

- use approved company evidence;
- separate facts, assumptions, recommendations, and unknowns;
- cite source IDs for claims;
- expose confidence and data freshness;
- flag potentially regulated or high-risk claims;
- preserve communication language and localization;
- avoid discriminatory or sensitive-person targeting;
- avoid dark patterns, deceptive urgency, or unsupported scarcity;
- avoid presenting attribution association as proven causation;
- fail visibly when provider or evidence data is unavailable.

AI quality should be measured with reviewed outcomes such as factual corrections, brand corrections, approval rate, handoff acceptance, experiment validity, and operator usefulness. Quality metrics must not automatically increase autonomy.

## Operating Cadence

- **Continuous:** react to material campaign, provider, consent, complaint, or lead events.
- **Daily:** review due activities, blocked work, approvals, provider failures, and urgent demand.
- **Weekly:** prepare marketing performance and next-week priorities.
- **Monthly:** review objectives, budget, channel mix, attribution, knowledge gaps, and experiments.
- **Campaign lifecycle:** prepare launch readiness and post-campaign review.

Cadence runs must be idempotent and use the existing orchestration-run and task infrastructure.

## User Experience

### Agent Team

Maya appears as a Marketing Manager row. Her work moves through:

- Planned;
- Ongoing;
- Waiting for human approval;
- Completed.

Cards should represent real campaign, content, analysis, handoff, or review work. Selecting a card opens the existing details panel with sources, status, owner, due date, approvals, and action links.

### Marketing Workspace

The main Marketing workspace should answer:

- What marketing outcome are we pursuing?
- What is running, planned, blocked, or waiting for approval?
- Who are we trying to reach and why?
- What content and channel work is due?
- What qualified demand has been created?
- What is performing, failing, or missing evidence?
- What should Maya recommend next?

Recommended views:

- Overview;
- Plan and calendar;
- Campaigns;
- Audiences;
- Content;
- Performance;
- Handoffs;
- Maya recommendations.

Campaigns should link to the existing Campaigns workspace rather than duplicating its editor.

### Agent Management

The Marketing Agent requires:

- role brief categories for company, products, policies, brand, audiences, and marketing instructions;
- document upload and indexing;
- marketing mailbox and web-enquiry access;
- trusted analytics, CMS, advertising, social, and event tools when configured;
- readable capability status with links to the exact access or provider configuration screen.

## Recommended KPIs

The initial overview should avoid a large vanity-metric dashboard.

Primary KPI:

- **Qualified demand created:** accepted marketing-to-sales handoffs or an equivalent company-defined qualified-demand event.

Efficiency KPI:

- **Cost per qualified demand:** Finance-approved campaign cost divided by qualified demand, with unavailable shown when cost evidence is missing.

Supporting measures:

- active campaigns on track;
- audience reached and engaged;
- campaign conversion;
- influenced pipeline and directly attributable revenue;
- content due or awaiting approval;
- unsubscribe, bounce, complaint, and support-risk signals;
- budget used versus plan;
- handoff acceptance rate.

Metric definitions, period, source, currency, and attribution model must be visible.

## Recommended Delivery Order

1. Activate the existing Marketing Manager template and define role capabilities, access, briefs, and company onboarding.
2. Add marketing objectives, plans, calendars, and durable work using existing tasks and campaign activities.
3. Add approved brand, content brief, content asset, variant, and review workflows.
4. Add audience intelligence and marketing-qualified-demand criteria over existing campaign segments and Sales records.
5. Add governed campaign collaboration, marketing-to-sales handoffs, and cross-agent coordination.
6. Add normalized channel observations, costs, attribution evidence, KPI snapshots, and experiments.
7. Add Maya's grounded advice, briefings, prioritization, and post-campaign analysis.
8. Build the production Marketing workspace and integrate it with Agent Team and Agent Management.

## Definition Of Success

The Marketing Agent is successful when an SME can move from a business objective to a governed marketing plan, approved content and activities, measurable demand, a clear Sales handoff, and an evidence-backed review without losing control of consent, brand, spend, or external actions.

