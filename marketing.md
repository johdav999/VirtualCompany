# Maya: Marketing Manager Agent

## Purpose

Maya is Virtual Company's Marketing Manager agent. Her mandate is to connect company strategy, market evidence, campaigns, content, channels, and measurable commercial outcomes.

Maya should operate as a governed marketing manager rather than only as a content generator. She should be able to research and analyze, recommend decisions, create reviewable marketing work, coordinate approved execution, and learn from measured outcomes.

The intended operating loop is:

```text
Company strategy
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

Maya's current AI analysis is recommendation-only. It explicitly does not publish content, spend budget, contact people, launch campaigns, or modify Sales state. The capabilities below describe the intended expansion of that foundation.

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
- Ideal customer profiles and priority audiences.
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

## 3. Competitive intelligence

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

## 4. Strategy decomposition and planning

Maya should translate an approved strategy into an executable hierarchy:

- Annual and quarterly marketing plans.
- Programs such as demand generation, product marketing, brand, customer advocacy, or lifecycle marketing.
- Campaigns connected to explicit objectives.
- Activities, tasks, milestones, and experiments.
- Audience definitions and exclusions.
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

## 5. Campaign planning and management

Maya should be able to:

- Propose campaigns from approved objectives and current evidence.
- Define campaign purpose, audience, offer, message, channel, and call to action.
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

## 6. Content strategy and briefs

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

## 7. Content and creative production

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

## 8. Channel integration and orchestration

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

Provider authentication, schemas, payloads, and error translation belong inside provider adapters. Provider payloads must not become core Marketing entities.

## 9. Lifecycle and CRM marketing

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

## 10. Product marketing and Sales enablement

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

## 11. Experimentation and optimization

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

## 12. Measurement, attribution, and reporting

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

Reports should be decision-oriented and answer:

- What changed?
- What evidence supports the change?
- What may have caused it?
- How confident is the conclusion?
- What should happen next?
- Which action requires approval?
- What information is missing?

## 13. Brand, compliance, and risk

Maya should enforce or evaluate:

- Brand voice, terminology, and visual identity.
- Factual claim support.
- Required disclaimers.
- Consent and communication permissions.
- Audience eligibility and exclusions.
- Privacy and sensitive-data constraints.
- Localization quality.
- Accessibility.
- Copyright and asset provenance.
- Frequency caps and contact pressure.
- Budget thresholds.
- Reputational-risk signals.
- Crisis and brand-incident escalation.

These decisions must be authoritative backend policies, not instructions that exist only inside a prompt.

## 14. Additional Marketing Manager activities

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

- `marketing.read_workspace`
- `marketing.read_objectives`
- `marketing.read_campaigns`
- `marketing.read_content_calendar`
- `marketing.read_audience_evidence`
- `marketing.read_channel_observations`
- `marketing.read_attribution_summary`
- `marketing.search_approved_knowledge`

### Recommend tools

- `marketing.prepare_strategy`
- `marketing.prepare_competitive_analysis`
- `marketing.prepare_plan`
- `marketing.propose_campaign`
- `marketing.prepare_content_brief`
- `marketing.prepare_content_variants`
- `marketing.prepare_experiment`
- `marketing.propose_sales_handoff`
- `marketing.recommend_campaign_change`
- `marketing.prepare_performance_review`

### Execute tools

- `marketing.create_strategy_draft`
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

Triggers should create durable workflow or task state. They must not invoke an AI provider as an untracked side effect.

## Autonomy model

Recommended initial autonomy:

| Autonomy | Activities |
| --- | --- |
| Autonomous | Analysis, monitoring, prioritization, internal briefings, evidence-gap detection, draft briefs, draft copy, draft visuals, proposed experiments, and proposed plans |
| Guided | Creating Marketing records, changing calendars, submitting content, creating Sales handoffs, and preparing channel actions |
| Approval required | Publication, outbound communication, campaign launch, audience activation, paid spend, budget changes, tracking changes, and destructive changes |
| Prohibited | Bypassing consent, using unsupported claims, targeting with disallowed sensitive data, exposing confidential information, or blindly retrying ambiguous external actions |

## User experience

Maya should remain visible through existing product surfaces:

- **Agent team:** Maya's planned, in-progress, approval-waiting, blocked, and completed work.
- **Marketing workspace:** objectives, plans, campaigns, content, experiments, performance, priorities, evidence, and proposed actions.
- **Work / Approvals:** publication, outbound communication, campaign launch, audience activation, and spend decisions.
- **Agent profile and chat:** role brief, communication style, capabilities, data scopes, tool permissions, autonomy, cadence, thresholds, and escalation rules.

Recommendations should offer concrete reviewable actions such as `Create draft strategy`, `Create campaign plan`, `Prepare content brief`, or `Request launch approval`. Each recommendation should expose evidence, assumptions, confidence, expected effect, missing information, policy status, and approval status.

## Implementation priorities

1. Harden Marketing-agent identity, company ownership, authorization, and failure-path tests.
2. Replace generic template tools with structured Marketing read and recommend tools.
3. Add strategy, competitive-intelligence, campaign-planning, and content-generation contracts.
4. Permit guarded creation of internal drafts through the shared agent tool-execution service.
5. Connect recommendations to tasks, workflows, and approvals.
6. Add brand, consent, claims, audience, budget, and publication policies.
7. Add provider-neutral channel-action contracts and provider-specific adapters.
8. Execute external actions through durable outbox dispatchers with idempotency and reconciliation.
9. Add normalized performance ingestion and attribution observations.
10. Add event-driven triggers and closed-loop experiment and campaign learning.
11. Expand autonomy only after audit evidence and production failure data demonstrate that it is safe.

The highest-value initial product increment is a complete, reviewable strategy-to-campaign package: Maya prepares the strategy, objectives, campaign breakdown, activities, content briefs, draft assets, channel plan, budget, measurements, risks, and approval requests without publishing or spending autonomously.
