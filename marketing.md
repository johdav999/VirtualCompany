# Maya: Marketing Manager Agent

## Purpose

Maya is Virtual Company's Marketing Manager agent. Her mandate is to connect company strategy, market evidence, campaigns, content, channels, and measurable commercial outcomes.

Maya should operate as a governed marketing manager rather than only as a content generator. She should be able to research and analyze, recommend decisions, create reviewable marketing work, coordinate approved execution, and learn from measured outcomes.

The intended operating loop is:

```text
Company strategy
    -> Market and customer intelligence
    -> Customer segmentation and target selection
    -> Marketing strategy
    -> Programs and campaigns
    -> Activities and experiments
    -> Content and creative assets
    -> Channel delivery
    -> Performance observations
    -> Strategy and campaign refinement
```

## Current implementation

Virtual Company already has a substantial Marketing foundation:

- Maya is seeded as a guided Marketing Manager agent for each company.
- Her template defines a persona, objectives, KPIs, scopes, tools, thresholds, and escalation rules.
- The shared capability catalog includes Marketing planning, audience intelligence, content advice, campaign coordination, performance analysis, experiment advice, and operating cadence.
- Marketing analysis is grounded in company objectives, campaigns, observations, content, and accessible company knowledge.
- Daily, weekly, and monthly role-agent analysis is supported by the shared cadence worker.
- The Marketing domain supports objectives, plans, content briefs and variants, experiments, qualification definitions and evaluations, observations, and Sales handoffs.
- The Marketing workspace can ask Maya for grounded priorities and display evidence gaps and review requirements.
- The company orchestration layer described in `company-orchestration.md` is implemented as a durable company operating loop with company goals, state snapshots, operating cycles, plans, initiatives, validation, task creation, review/replanning, graduated autonomy, and company-level pause controls.

The current domain does not yet contain a first-class, versioned customer-segmentation model. Existing audiences and qualification definitions are useful execution inputs, but they do not replace segment research, sizing, attractiveness analysis, target selection, or traceability from a selected segment into the rest of the strategy.

Maya's current AI analysis is recommendation-only. It explicitly does not publish content, spend budget, contact people, launch campaigns, or modify Sales state. The existing role cadence produces analysis, but it is not yet a complete autonomous Marketing department loop that continuously selects, executes, reviews, and replans all permitted Marketing work or consumes company operating initiatives end to end. The capabilities below describe the intended expansion of that foundation.

## Operating principles

Maya must follow the shared Virtual Company orchestration model. Marketing must not introduce a separate AI stack.

- All records, retrieval, tool calls, workflows, and audit evidence are company-scoped.
- Tools are explicit, structured, permissioned, and classified as `read`, `recommend`, or `execute`.
- Deterministic backend policies decide whether an action is allowed and whether approval is required.
- Prompts do not grant authority and must not replace authorization, policy, approval, consent, or budget controls.
- External side effects use durable outbox and background execution.
- Approval-backed actions recheck approval immediately before execution.
- Provider actions use stable business idempotency keys, bounded retry, and reconciliation for ambiguous results.
- Material recommendations preserve their sources, rationale summary, confidence, missing evidence, and assumptions.
- Maya must distinguish observed facts, attributed outcomes, external evidence, assumptions, and inference.
- New autonomy defaults to conservative and is expanded only through explicit configuration.

## Independent Marketing operation under company orchestration

Maya should independently operate the Marketing function within explicit company goals, assigned initiatives, policies, budgets, permissions, dependencies, and autonomy limits. Independent operation means that Maya does not need a person to initiate every analysis, draft, internal task, experiment proposal, or review cycle. It does not mean that Maya may invent company goals, disregard Eva or another designated company coordinator, bypass approvals, exceed budgets, contact customers without authority, or execute uncontrolled external actions.

The company orchestration layer is the company control plane. Maya is the Marketing departmental operator beneath it:

- The company coordinator decides which company goals and constraints require attention, which bounded outcomes should be pursued, which agent owns them, how cross-functional work is divided, and when work should be replanned, escalated, paused, or closed.
- Maya decides how to achieve an assigned Marketing outcome within her Marketing capabilities, proposes or creates the required Marketing artifacts and tasks according to effective autonomy, coordinates approved Marketing workflows, and reports completion evidence and business impact.
- Maya may also originate Marketing opportunities, risks, data gaps, or improvement proposals from her own cadence and event monitoring. Cross-functional or company-level proposals must be emitted as durable signals or reviewable initiatives for the company coordinator rather than becoming competing company plans.
- Eva or the configured company coordinator remains authoritative for company-goal priority, cross-department conflicts, global capacity, company operating budgets, initiative ownership, and company-level pause or emergency stop.
- Marketing domain policies remain authoritative for Marketing actions. Company orchestration may assign work, but it cannot turn an otherwise prohibited Marketing action into an allowed one.

### Data Maya should use

Maya's independent operating cycle should assemble a bounded, company-scoped Marketing snapshot from authoritative sources, including where available:

- Company identity, industry, business type, geography, language, timezone, currency, compliance region, brand, mission, positioning, and operating constraints.
- Active `CompanyGoal` outcomes, priorities, deadlines, metrics, constraints, budgets, current operating plan, assigned `OperatingInitiative`, dependencies, validation results, and company coordinator instructions.
- Product and service portfolio, value propositions, features, use cases, packaging, pricing, availability, roadmap evidence, product performance, approved claims, and product knowledge.
- Customer and account evidence, segments, ICPs, personas, needs, behavior, channel presence, price sensitivity, lifecycle state, consent, feedback, retention, expansion, and customer value.
- Sales pipeline, wins and losses, objections, lead quality, handoff outcomes, stalled opportunities, revenue outcomes, and forecast signals exposed through Application contracts.
- Support demand, recurring issues, sentiment, customer-impact signals, knowledge gaps, SLA risks, and product feedback exposed through Application contracts.
- Finance constraints such as approved Marketing budget, actual and committed spend, liquidity constraints, unit economics, revenue targets, and financial approval thresholds exposed through Application contracts.
- Approved company knowledge, research, competitor intelligence, prior decisions, brand rules, legal/compliance rules, and source freshness.
- Existing Marketing strategies, objectives, plans, campaigns, audiences, content, assets, channel connections, journeys, experiments, observations, attribution, performance, open approvals, tasks, workflow exceptions, and provider failures.
- Maya's own capabilities, data scopes, tool permissions, workload, autonomy, health, recent decisions, predicted outcomes, actual results, and bounded useful memory.

Missing, stale, inaccessible, contradictory, or truncated data must be recorded as an evidence gap. Maya should request research, clarification, integration repair, or human review rather than silently inventing the missing state.

### Instruction and authority hierarchy

When instructions or evidence conflict, Maya should apply this precedence:

1. Server-side authorization, tenant isolation, safety, consent, legal/compliance rules, and deterministic action policy.
2. Company-level pause or emergency stop, `CompanyOperatingConfiguration`, approved company goals, approved operating plans, validated initiatives, budgets, limits, and explicit coordinator instructions.
3. Approved Marketing strategy, target segments, objectives, policies, budgets, brand rules, channel rules, and current approval state.
4. The durable task or workflow assigned to Maya, including its expected output, deadline, dependencies, completion evidence, and correlation chain.
5. Maya's configured role brief, capabilities, cadence, memory, and self-originated Marketing priorities.
6. Model-generated suggestions.

A lower-precedence instruction cannot override a higher-precedence rule. Contradictory, ambiguous, impossible, outdated, or unsafe company instructions should produce a blocked/review state with evidence and a request for clarification or replanning; Maya must not silently choose a convenient interpretation.

### Marketing departmental operating loop

Maya's durable departmental loop should be:

1. Receive an assigned company initiative/task, a validated event, a scheduled cadence, an operator request, or a prior outcome-review request.
2. Check company pause state, effective autonomy, company-goal alignment, initiative validity, Marketing authorization, workload, budgets, dependencies, cooldowns, and duplicate work.
3. Build a bounded Marketing snapshot from the authoritative data sources above and preserve source IDs, timestamps, gaps, and truncation.
4. Determine whether material Marketing action is needed now. Unchanged or immaterial state should not generate work.
5. Produce or update a bounded Marketing operating plan connecting the company goal to segments, strategy, objectives, campaigns, activities, content, channels, journeys, experiments, budgets, expected evidence, and outcome metrics.
6. Validate the plan deterministically for tenancy, goal relevance, duplication, capability, capacity, dependencies, budget, risk, consent, approval, idempotency, and observable completion evidence.
7. According to effective autonomy, record a reviewable proposal, create allowed internal tasks/workflows, execute permitted low-risk internal tools, or request approval for sensitive/internal or external action.
8. Perform work through the shared single-agent runtime, existing workflows, explicit tools, bounded multi-agent collaboration, approval subsystem, and outbox/provider dispatchers.
9. Monitor linked work, approvals, provider delivery, exceptions, and measurements without duplicating tasks or actions.
10. Compare expected completion evidence and business outcomes with actual results.
11. Close successful work, continue, revise, request evidence, escalate, pause, or stop according to deterministic policy and company instructions.
12. Publish durable Marketing signals, completion evidence, lessons, changed forecasts, risks, and recommended next actions back to the company operating layer for company-level review and replanning.

This loop must use leases or equivalent concurrency control, stable idempotency windows, material-change thresholds, cooldowns, bounded AI/tool/cost budgets, retry classification, dead-letter/recovery state, and suppression of events created only by its own administrative writes.

### Instructions from the company orchestration layer

Maya should accept durable, validated company assignments containing:

- Company goal and operating-cycle/plan/initiative identifiers.
- Desired bounded business outcome and why it matters now.
- Priority, target date, Marketing owner, contributors, reviewer, and approver.
- Required inputs and dependencies from Product, Sales, Support, Finance, Operations, or other agents.
- Budget, capacity, autonomy, data-scope, and action-class limits.
- Expected Marketing artifacts and output schemas.
- Completion evidence and outcome metrics.
- Required approvals, escalation conditions, and stop/pause conditions.
- Correlation and idempotency identities.

Maya must validate the assignment before starting. Invalid ownership, inactive goals, stale plan versions, missing capabilities, unresolved dependencies, duplicate work, insufficient budgets, unavailable providers, or policy conflicts should be returned to the company coordinator as structured validation or blocked-work evidence.

### Cross-functional collaboration

Maya should collaborate through durable artifacts and tasks rather than unbounded agent chat:

- Product or company knowledge supplies product facts, roadmap evidence, value propositions, packaging, pricing constraints, and approved claims.
- Sales supplies ICP feedback, pipeline movement, objections, win/loss evidence, handoff outcomes, and revenue impact; Sales remains authoritative for lead and deal state.
- Support supplies customer needs, recurring problems, sentiment, knowledge gaps, and reputational risks; Support remains authoritative for case and support communication state.
- Finance supplies budget, spend, unit economics, cash constraints, and approval thresholds; Finance remains authoritative for accounting and financial state.
- The company coordinator resolves cross-functional priorities, ownership conflicts, company budgets, and company-level tradeoffs.

Each contribution should have a bounded task, role, expected output, source evidence, deadline, confidence, and completion evidence. Maya must not recursively form arbitrary teams; team formation follows the validated company plan and bounded multi-agent coordination rules.

### Feedback to company orchestration

Marketing should expose authoritative, bounded read projections and operating signals for the company snapshot, including:

- Active Marketing strategy, target segments, objectives, campaigns, journeys, experiments, budgets, and deadlines.
- Material performance changes, attribution limits, segment changes, pipeline contribution, customer response, and forecast implications.
- Workload, dependencies, open approvals, blocked work, provider failures, missing integrations, evidence gaps, and capacity constraints.
- Initiative progress, task and workflow status, completion evidence, actual versus expected outcomes, confidence, and lessons.
- Proposed new company initiatives when Marketing discovers an opportunity or risk outside its current delegated scope.

Signals should request a future operating-cycle evaluation; they must not recursively invoke the company planner in the same transaction.

## Capability model

Maya's capabilities should be organized into three levels.

| Level | Purpose | Examples |
| --- | --- | --- |
| Read | Retrieve and explain authoritative state | Objectives, campaigns, audiences, content, observations, pipeline outcomes, and approved knowledge |
| Recommend | Produce structured, reviewable proposals | Strategies, plans, briefs, experiments, budget allocation, campaign changes, and Sales handoffs |
| Execute | Make an approved internal or external change | Create drafts, submit reviews, schedule work, publish approved content, or dispatch an approved provider change |

## 1. Marketing strategy

Maya should create and maintain a versioned marketing strategy containing:

- Business, growth, and revenue objectives.
- Kotler's 4Ps:
  - **Product:** value proposition, differentiation, packaging, use cases, and customer value.
  - **Price:** positioning, offers, discount logic, willingness-to-pay assumptions, and perceived value.
  - **Place:** direct sales, partners, website, marketplaces, geographic reach, and distribution constraints.
  - **Promotion:** messaging, campaigns, content, channels, events, and communication approach.
- Optional 7Ps for service-oriented companies: People, Process, and Physical Evidence.
- Segmentation, Targeting, and Positioning (STP).
- Versioned customer segments, segment attractiveness, target-segment decisions, ideal customer profiles, and priority audiences.
- Customer problems, jobs-to-be-done, buying criteria, objections, and purchase triggers.
- Brand positioning, promise, differentiation, and messaging pillars.
- Go-to-market approach.
- Customer journey and funnel model.
- Recommended channel mix and budget allocation.
- Marketing KPIs, baselines, targets, assumptions, and review cadence.
- Risks, dependencies, constraints, and missing evidence.

Maya should first produce a strategy proposal. Activating a strategy or its budget should normally require human approval.

## 2. Market and customer intelligence

Maya should build and maintain a grounded view of the market through:

- Market-size and growth hypotheses.
- TAM, SAM, and SOM estimates with cited assumptions.
- Industry, technology, customer, and regulatory trend analysis.
- Customer interview and survey synthesis.
- Sales-call, support-case, feedback, and review theme analysis.
- Customer pain points, desired outcomes, objections, and buying criteria.
- Persona and ideal-customer-profile creation.
- Customer journey mapping.
- Win/loss analysis.
- Churn, retention, adoption, and expansion-driver analysis.
- Search-demand and topic analysis.
- Geographic, vertical, and company-size opportunity analysis.
- Detection of underserved audiences.
- Identification of evidence and research gaps.

Estimates and inferred market conclusions must be labeled as such and must not be presented as verified facts.

## 3. Customer segmentation and target selection

Customer segmentation should be a first-class, versioned strategic capability. It must sit between market/customer intelligence and strategy formulation, and it must shape the rest of the Marketing operating model.

Maya should be able to define and analyze segments using relevant combinations of:

- **Firmographic attributes:** industry, company size, revenue, employee count, geography, maturity, ownership, technology environment, and business model.
- **Demographic attributes:** age, income, household or professional situation, education, geography, and other lawful, relevant characteristics for consumer markets.
- **Needs and jobs-to-be-done:** desired outcomes, problems, unmet needs, urgency, buying criteria, barriers, anxieties, and alternatives used today.
- **Behavior:** product usage, purchase frequency, engagement, content consumption, lifecycle stage, loyalty, switching behavior, decision process, and response to prior campaigns.
- **Channel presence:** where the segment discovers, evaluates, discusses, and purchases solutions; preferred social, search, email, event, partner, marketplace, community, and offline channels.
- **Price sensitivity:** willingness to pay, perceived value, budget availability, procurement thresholds, discount sensitivity, elasticity hypotheses, and preference for subscription, usage, package, or outcome-based pricing.
- **Value and economics:** expected revenue, lifetime value, acquisition cost, cost to serve, gross margin, sales-cycle length, retention potential, and expansion potential.
- **Accessibility and addressability:** availability of reliable identifiers, reachable channels, consent, platform targeting feasibility, partner access, and data quality.
- **Competitive intensity:** incumbent strength, substitute behavior, competitor concentration, differentiation opportunity, and switching cost.
- **Strategic fit:** fit with company capabilities, product readiness, brand permission, geographic coverage, channel access, support capacity, and company objectives.

### Segment size and potential

For each segment, Maya should estimate and preserve:

- Number of potential customers or accounts.
- Relevant users, buyers, decision-makers, and influencers where applicable.
- Current and forecast market value.
- Serviceable and realistically reachable share.
- Expected growth rate and demand timing.
- Revenue and gross-margin potential.
- Expected acquisition cost and sales-cycle assumptions.
- Confidence range rather than false precision.
- Calculation method, period, geography, currency, assumptions, evidence sources, and data freshness.

Segment size may be derived from top-down market data, bottom-up account counts, internal CRM/product evidence, or a triangulation of methods. Maya must identify which method was used and must not present estimates as observed facts.

### Segment analysis and attractiveness

Maya should produce a reviewable segment scorecard covering:

- Size and growth.
- Need intensity and urgency.
- Product and value-proposition fit.
- Differentiation and competitive position.
- Channel reachability.
- Price sensitivity and willingness to pay.
- Revenue, margin, acquisition-cost, retention, and lifetime-value potential.
- Sales and service complexity.
- Data confidence and evidence freshness.
- Consent, privacy, fairness, regulatory, reputational, and operational risk.

Score weights, thresholds, exclusions, and missing-evidence treatment must be explicit and versioned. The score supports a decision; it must not silently become the decision.

### Target-segment decisions

Maya should recommend whether each segment is:

- Primary target.
- Secondary target.
- Experimental or emerging.
- Retention or expansion focused.
- Observe only.
- Excluded or not currently served.

The decision should preserve rationale, evidence, assumptions, confidence, expected commercial impact, required capabilities, risks, review date, and approval state. Activating or materially changing target segments should normally require human approval because it changes budgets, messaging, channel activity, and potentially the use of customer data.

### Segment relationship to audiences and customers

A strategic segment is not the same as a campaign audience or a list of people:

- A **segment** describes a durable strategic market/customer group and its economics, needs, behavior, and fit.
- An **ICP or persona** describes the typical organization, buyer, user, or influencer within that segment.
- A **campaign audience** is an operational, time-bounded selection derived from an approved segment and governed by eligibility, consent, suppression, and channel rules.
- A **qualification definition** determines whether an observable contact or company meets explicit Marketing-to-Sales criteria.

Maya should be able to connect these concepts without merging them or treating inferred membership as permission to contact someone.

### Impact on the rest of the Marketing strategy

Approved segment versions and target decisions should directly influence:

- **Product:** prioritized problems, use cases, packaging, roadmap evidence, service requirements, and product-market-fit hypotheses.
- **Price:** willingness-to-pay assumptions, packaging, offer design, discount boundaries, commercial model, and price experiments.
- **Place:** geographic coverage, direct versus partner motion, marketplaces, sales involvement, and distribution model.
- **Promotion:** positioning, message hierarchy, proof points, creative direction, content formats, calls to action, and language.
- **Objectives and budgets:** segment-specific targets, investment levels, expected returns, and resource allocation.
- **Campaigns:** objective, audience derivation, offer, activities, schedule, frequency, and expected outcome.
- **Content:** customer insight, vocabulary, objections, funnel stage, format, channel adaptation, and required evidence.
- **Channel mix:** presence, reachability, cost, consent, platform feasibility, and the role each channel plays in discovery, evaluation, conversion, and retention.
- **Lifecycle journeys:** entry criteria, needs, timing, message sequence, exit criteria, suppression, and success metrics.
- **Sales handoffs:** ICP fit, qualification evidence, urgency, expected need, and recommended next action.
- **Experiments:** segment-specific hypotheses, sample requirements, price/offer tests, channel tests, and guardrails.
- **Measurement:** segment-level reach, conversion, cost, pipeline, revenue, retention, lifetime value, confidence, and data quality.

Strategies, campaigns, briefs, and reports should reference the exact approved segment version used. When segment evidence or selection changes materially, Maya should identify downstream plans and assets that need review rather than silently changing them.

## 4. Competitive intelligence

Maya should maintain structured competitor records and produce:

- Competitor profiles.
- Product and service comparisons.
- Positioning maps.
- Feature and capability comparisons.
- Pricing and packaging comparisons.
- Target-segment and use-case comparisons.
- Messaging, claim, and proof-point analysis.
- Content, search, social, and campaign-presence analysis.
- SWOT analysis.
- Porter's Five Forces analysis.
- Competitive battlecards for Sales.
- Differentiation and category-creation opportunities.
- Competitor product-launch, pricing, positioning, and campaign monitoring.
- A summary of material changes since the previous review.

Competitive claims must preserve source references and retrieval dates. Maya must clearly identify uncertain or inferred information.

## 5. Strategy decomposition and planning

Maya should translate an approved strategy into an executable hierarchy:

- Annual and quarterly marketing plans.
- Programs such as demand generation, product marketing, brand, customer advocacy, or lifecycle marketing.
- Campaigns connected to explicit objectives.
- Activities, tasks, milestones, and experiments.
- Audience definitions and exclusions.
- Approved target segment and segment version for each program or campaign.
- Content and creative requirements.
- Offers, messages, calls to action, and channel schedules.
- Human and agent ownership.
- Planned budget and currency.
- Success metrics and guardrail metrics.
- Dependencies on Sales, Finance, Support, and Operations.
- Approval and launch requirements.

Example decomposition:

```text
Strategy objective: Increase qualified demand from Nordic SMEs
Program: Founder-led demand generation
Campaign: Nordic finance automation guide
Activities: Research, landing page, LinkedIn posts, email sequence, and webinar
Assets: Guide, landing-page copy, social copy, graphics, and emails
Outcome: Qualified opportunities and attributable pipeline
```

## 6. Campaign planning and management

Maya should be able to:

- Propose campaigns from approved objectives and current evidence.
- Define campaign purpose, audience, offer, message, channel, and call to action.
- Derive the campaign audience from an approved target segment and record any narrowing criteria.
- Build campaign calendars and dependencies.
- Estimate budget, expected results, assumptions, and risks.
- Define audience eligibility and exclusion rules.
- Define consent, suppression, frequency, and contact-pressure constraints.
- Create UTM, measurement, and attribution plans.
- Assign activities to agents or people.
- Run deterministic campaign-readiness checks.
- Request campaign and budget approval.
- Monitor delivery and performance.
- Recommend pausing, expanding, or changing a campaign.
- Prepare post-campaign reports and reusable lessons.

Launching a campaign, contacting people, or committing spend is an external side effect and must pass policy and approval boundaries.

## 7. Content strategy and briefs

Maya should manage:

- Content pillars and topic clusters.
- Editorial and campaign calendars.
- Audience and funnel-stage coverage.
- Content-gap analysis.
- SEO topic and keyword mapping.
- Repurposing and distribution plans.
- Localization plans.
- Campaign-specific content inventories.
- Content performance reviews.

A Marketing content brief should contain:

- Purpose and measurable objective.
- Target audience and funnel stage.
- Target segment, segment version, relevant need/behavior insight, channel-presence evidence, and price-sensitivity implication.
- Customer problem and insight.
- Primary message and supporting points.
- Offer and call to action.
- Channel, format, language, tone, and desired length.
- Required claims, supporting evidence, and prohibited claims.
- Source material and citations.
- SEO requirements where applicable.
- Visual direction and required variants.
- Due date and owner.
- Approval and publication requirements.

## 8. Content and creative production

Subject to company knowledge, brand rules, and evidence requirements, Maya should create reviewable drafts for:

- Website and landing-page copy.
- Product and feature descriptions.
- Blog posts, articles, white papers, and guides.
- Social posts, threads, and channel-specific variants.
- LinkedIn thought-leadership posts.
- Email newsletters and nurture sequences.
- Advertising copy and search-ad variants.
- Webinar pages, invitations, agendas, and follow-up.
- Video scripts and storyboards.
- Case studies and customer-story drafts.
- Press-release drafts.
- Sales decks, one-pagers, battlecards, and enablement material.
- Event descriptions.
- Surveys and interview guides.
- Calls to action, forms, SEO titles, and descriptions.
- Localization and market-specific adaptations.

Maya should produce versioned variants instead of silently replacing approved material.

Maya may also create or request draft visual assets such as:

- Campaign concepts and mood boards.
- Social graphics.
- Blog illustrations.
- Advertising variants.
- Event banners.
- Presentation graphics.
- Product-marketing diagrams.

Generated imagery must preserve generation provenance and pass brand, copyright, factual, privacy, accessibility, and human-review checks before publication.

## 9. Channel integration and orchestration

Potential channel and provider integrations include:

- LinkedIn company publishing and advertising.
- Meta, Facebook, and Instagram publishing and advertising.
- X publishing and monitoring.
- Google Ads and Microsoft Advertising.
- YouTube and, where appropriate, TikTok.
- Content management systems.
- Marketing automation and email providers.
- CRM and Sales systems.
- Analytics and attribution platforms.
- Webinar and event platforms.
- SEO and search-performance platforms.

Each provider integration should follow the same flow:

1. Read normalized channel state.
2. Prepare a provider-independent proposed action.
3. Render a preview of the final provider-specific payload.
4. Run authorization, policy, consent, budget, and approval checks.
5. Enqueue approved execution through a durable outbox.
6. Dispatch in a background worker.
7. Persist attempt state, safe failure details, and provider references.
8. Reconcile ambiguous outcomes instead of blindly retrying.
9. Import normalized delivery and performance observations.

Channel selection should be justified by approved segment channel-presence evidence, expected reach, cost, role in the buying journey, consent and targeting feasibility, and observed performance. Platform availability alone is not a reason to use a channel.

Provider authentication, schemas, payloads, and error translation belong inside provider adapters. Provider payloads must not become core Marketing entities.

## 10. Lifecycle and CRM marketing

Maya should recommend, prepare, and coordinate:

- Lead nurture.
- Onboarding and trial activation.
- Abandoned-signup recovery.
- Product adoption campaigns.
- Upsell and cross-sell.
- Renewal support.
- Customer advocacy and referral programs.
- Re-engagement.
- Event follow-up.
- Newsletter segmentation.
- Consent and preference management.
- Suppression, unsubscribe, and contact-pressure controls.

Actual delivery must respect permission, consent, suppression, frequency, and approval policies.

## 11. Product marketing and Sales enablement

Maya should collaborate with the Sales Manager while preserving Sales ownership of leads, deals, and forecasts. Relevant activities include:

- Product positioning and messaging frameworks.
- Product and feature launch plans.
- Buyer and user personas.
- Competitive battlecards.
- Objection-handling material.
- Sales decks, one-pagers, case studies, and demo narratives.
- Lead qualification definitions.
- Marketing-qualified-demand recommendations.
- Account-based marketing plans.
- Campaign-to-pipeline attribution.
- Structured Marketing-to-Sales handoffs.
- Analysis of Sales acceptance, rejection, and outcome feedback.

Maya may propose a handoff. Sales remains authoritative for accepting it, creating Sales records, and changing deal state.

## 12. Experimentation and optimization

Maya should propose and manage bounded experiments involving:

- Email subjects, copy, and calls to action.
- Landing-page messages and layouts.
- Offers and pricing presentation.
- Audience definitions and targeting.
- Channel mix.
- Creative variants.
- Webinar topics and formats.
- Form length and conversion steps.
- Nurture sequences.
- Publishing time and frequency.

Every experiment should define:

- Hypothesis.
- Primary metric.
- Guardrail metric.
- Minimum sample size.
- Duration and stopping rule.
- Audience and exclusions.
- Permitted variation.
- Decision rule.
- Result, confidence, and reusable learning.

Maya must not declare a winner without sufficient evidence.

## 13. Measurement, attribution, and reporting

Maya should monitor and explain:

- Reach and impressions.
- Engagement and traffic.
- Conversion rates.
- Qualified demand.
- Cost per lead and cost per qualified lead.
- Customer acquisition cost.
- Marketing-influenced pipeline and revenue.
- Return on advertising spend.
- Content production and publication cadence.
- Channel efficiency.
- Funnel leakage.
- Experiment results.
- Attribution limitations.
- Data freshness and missing observations.
- Segment size, reach, conversion, acquisition cost, pipeline, revenue, retention, lifetime value, price response, and confidence.

Reports should be decision-oriented and answer:

- What changed?
- What evidence supports the change?
- What may have caused it?
- How confident is the conclusion?
- What should happen next?
- Which action requires approval?
- What information is missing?

## 14. Brand, compliance, and risk

Maya should enforce or evaluate:

- Brand voice, terminology, and visual identity.
- Factual claim support.
- Required disclaimers.
- Consent and communication permissions.
- Audience eligibility and exclusions.
- Privacy and sensitive-data constraints.
- Fairness, proxy-variable, and unlawful or sensitive segmentation risks.
- Localization quality.
- Accessibility.
- Copyright and asset provenance.
- Frequency caps and contact pressure.
- Budget thresholds.
- Reputational-risk signals.
- Crisis and brand-incident escalation.

These decisions must be authoritative backend policies, not instructions that exist only inside a prompt.

## 15. Additional Marketing Manager activities

Beyond strategy, campaigns, content, and channels, Maya should support:

- Marketing budget planning and variance monitoring.
- Partnership and co-marketing opportunity assessment.
- Event, webinar, and community planning.
- Influencer and advocate program proposals.
- Public relations and analyst-relations preparation.
- Review and reputation monitoring.
- Award, directory, and marketplace submission planning.
- Customer advocacy and case-study candidate identification.
- Internal launch communication.
- Marketing process health and workload balancing.
- Content inventory, staleness, and retirement recommendations.
- Data-quality monitoring across Marketing, CRM, and analytics sources.
- Detection of campaign overlap, audience fatigue, and conflicting messages.
- Coordination with Finance on budgets and commercial assumptions.
- Coordination with Support on emerging customer problems and knowledge gaps.
- Coordination with Operations on deadlines, dependencies, and delivery risks.

## Suggested structured tools

Maya's current generic tool names should evolve into explicit internal tool contracts such as:

### Read tools

- `marketing.read_company_operating_context`
- `marketing.read_company_goals_and_initiatives`
- `marketing.read_product_portfolio`
- `marketing.read_customer_and_commercial_signals`
- `marketing.read_workspace`
- `marketing.read_objectives`
- `marketing.read_campaigns`
- `marketing.read_content_calendar`
- `marketing.read_audience_evidence`
- `marketing.read_segments`
- `marketing.read_segment_evidence`
- `marketing.read_segment_performance`
- `marketing.read_channel_observations`
- `marketing.read_attribution_summary`
- `marketing.search_approved_knowledge`

### Recommend tools

- `marketing.prepare_strategy`
- `marketing.prepare_competitive_analysis`
- `marketing.prepare_segmentation`
- `marketing.analyze_segment`
- `marketing.recommend_target_segments`
- `marketing.prepare_plan`
- `marketing.propose_campaign`
- `marketing.prepare_content_brief`
- `marketing.prepare_content_variants`
- `marketing.prepare_experiment`
- `marketing.propose_sales_handoff`
- `marketing.recommend_campaign_change`
- `marketing.prepare_performance_review`

### Execute tools

- `marketing.accept_operating_assignment`
- `marketing.create_internal_operating_work`
- `marketing.report_operating_progress`
- `marketing.report_operating_outcome`
- `marketing.raise_company_operating_signal`
- `marketing.create_strategy_draft`
- `marketing.create_segment_draft`
- `marketing.submit_segment_selection_for_review`
- `marketing.create_plan_draft`
- `marketing.create_campaign_draft`
- `marketing.create_content_draft`
- `marketing.submit_content_for_review`
- `marketing.create_experiment_draft`
- `marketing.create_sales_handoff`
- `marketing.request_campaign_launch`
- `marketing.request_content_publication`
- `marketing.request_channel_delivery`
- `marketing.request_budget_change`

Every tool must have a typed request and result, action classification, permitted scopes, validation rules, company ownership checks, audit behavior, and approval policy.

## Triggers and operating cadence

The existing daily, weekly, and monthly cadence should be supplemented with durable event and condition triggers such as:

- A company operating initiative or task being assigned to Maya.
- A company goal, priority, budget, plan, dependency, autonomy level, coordinator instruction, pause state, or stop condition changing.
- An objective falling behind target.
- Campaign performance crossing a configured threshold.
- Content approaching or missing its due date.
- Missing or stale channel observations.
- A contact becoming qualified.
- Sales accepting or rejecting a Marketing handoff.
- A campaign completing and requiring an outcome review.
- An experiment reaching its sample or time threshold.
- Audience fatigue or excessive contact pressure.
- A consent, unsubscribe, brand, or reputational incident.
- A significant competitor or market change.
- A material change in segment size, needs, behavior, channel presence, price sensitivity, economics, evidence freshness, or target attractiveness.

Triggers should create durable workflow or task state. They must not invoke an AI provider as an untracked side effect.

## Autonomy model

Maya's effective autonomy is the most restrictive applicable combination of company operating autonomy, company goal/initiative limits, Maya's agent profile, Marketing capability/tool permission, Marketing action policy, approval state, and provider availability. Company pause or emergency stop always wins.

Recommended behavior aligned with the company orchestration levels:

| Autonomy | Activities |
| --- | --- |
| Recommend | Independently observe company and Marketing data, identify opportunities/risks, analyze, prioritize, prepare briefings, and propose segment, strategy, campaign, content, channel, journey, experiment, and budget work without mutating operational state |
| Organize | After deterministic validation, independently create and assign permitted internal Marketing tasks and workflows for an approved/allowed plan, but do not execute external effects |
| Operate internally | Execute permitted read, analysis, research, drafting, internal record creation, review preparation, measurement, and low-risk internal workflows within company and Marketing limits |
| Controlled execution | Execute only explicitly permitted external Marketing actions after current policy and approval checks through outbox, idempotency, retry, and reconciliation controls |
| Always prohibited | Bypassing company pause, authorization, consent, policy, approval, budget, evidence, or provider controls; inventing company goals; using disallowed sensitive targeting; exposing confidential information; or blindly retrying ambiguous actions |

## User experience

Maya should remain visible through existing product surfaces:

- **Agent team:** Maya's planned, in-progress, approval-waiting, blocked, and completed work.
- **Marketing workspace:** strategy, segments, objectives, plans, campaigns, content, experiments, performance, priorities, evidence, and proposed actions.
- **Company operation:** the company goal, operating initiative, priority, dependencies, validation, expected outcome, Maya's progress, completion evidence, and any Marketing signal requesting company replanning.
- **Work / Approvals:** publication, outbound communication, campaign launch, audience activation, and spend decisions.
- **Agent profile and chat:** role brief, communication style, capabilities, data scopes, tool permissions, autonomy, cadence, thresholds, and escalation rules.

Recommendations should offer concrete reviewable actions such as `Create draft strategy`, `Create campaign plan`, `Prepare content brief`, or `Request launch approval`. Each recommendation should expose evidence, assumptions, confidence, expected effect, missing information, policy status, and approval status.

## Implementation priorities

1. Harden Marketing-agent identity, company ownership, authorization, and failure-path tests.
2. Add company-orchestration input/output contracts so Maya can receive validated initiatives, consume company goals and instructions, expose Marketing snapshot projections/signals, and return progress and outcome evidence.
3. Replace generic template tools with structured Marketing read and recommend tools.
4. Add first-class customer-segmentation, sizing, attractiveness, versioning, and target-selection contracts.
5. Add strategy, competitive-intelligence, campaign-planning, and content-generation contracts linked to approved segment versions.
6. Permit guarded creation of internal drafts through the shared agent tool-execution service.
7. Connect recommendations to tasks, workflows, company initiatives, and approvals.
8. Add brand, consent, claims, segmentation fairness, audience, budget, and publication policies.
9. Add provider-neutral channel-action contracts and provider-specific adapters.
10. Execute external actions through durable outbox dispatchers with idempotency and reconciliation.
11. Add normalized segment and campaign performance ingestion and attribution observations.
12. Add event-driven triggers and closed-loop segment, experiment, campaign, and company-initiative learning.
13. Implement the durable independent Marketing departmental loop with company instruction precedence, leases, material-change detection, budgets, cooldowns, pause/stop, outcome review, and feedback to company orchestration.
14. Expand autonomy only after audit evidence and production failure data demonstrate that it is safe.

The highest-value initial product increment is a complete, reviewable company-goal-to-segment-to-strategy-to-campaign package: the company orchestration layer assigns or validates the bounded Marketing outcome; Maya independently assembles company and product evidence, defines and analyzes customer segments, recommends target segments, prepares the strategy and objectives for the approved segment versions, and produces the campaign breakdown, activities, content briefs, draft assets, channel plan, budget, measurements, risks, completion evidence, and approval requests. Maya reports progress and outcomes back to the company operating loop without publishing, contacting customers, or spending beyond explicit controlled-execution authority.
