# Campaign Functionality

## Purpose

Campaigns in Virtual Company should be time-bound commercial initiatives that coordinate an audience, an offer, sales and marketing activities, governance, and measurable business objectives.

The campaign surface should support both:

- Business-to-business campaigns focused on accounts, buying groups, meetings, opportunities, and pipeline.
- Business-to-consumer campaigns focused on customer segments, products, purchases, retention, and transaction volume.

A campaign is not the same as an automation. A campaign has an owner, objective, audience, planning period, and measured result. An automation is an ongoing trigger-based rule, such as welcoming every new lead or reminding every abandoned cart. Automations may enroll people in campaigns, but permanently running automations should not be presented as campaigns.

## Current Repository Foundation

Virtual Company already has a governed outbound campaign foundation:

- `SalesCampaign` is company-owned and linked to a `SalesSequence`.
- Campaigns have an audience type, status, communication language, outbound policy, approval state, and lifecycle timestamps.
- `SalesCampaignContact` records contact enrollment and sequence progress.
- `OutboundCampaignService` creates campaigns, validates company-owned active contacts, creates email sequence steps, and manages launch, pause, and stop actions.
- `ISequenceExecutionService` schedules due steps and handles drafts, delivery results, replies, bounces, cancellations, and deal-created stop conditions.
- Campaign APIs are company-authorized through `SalesCampaignsController`.
- The Sales campaign page supports audience selection, sequence design, policy controls, lifecycle actions, and execution visibility.
- Sales analytics and Alex's AI capability contracts already include campaign analysis and optimization.

The existing implementation is primarily an outbound email sequence. It should be extended into a broader campaign initiative without weakening consent, approval, idempotency, tenant isolation, or delivery controls.

## Recommended Campaign Definition

Every campaign should define the following.

| Area | Required information |
| --- | --- |
| Purpose | Campaign type, business reason, description, owner |
| Objective | Primary objective, target value, unit, deadline, optional secondary objectives |
| Audience | Segment rules, selected accounts or contacts, exclusions, consent and suppression state |
| Offer | Products, services, approved prices, promotion, content, or event |
| Schedule | Planning dates, launch date, end date, timezone, milestones |
| Channels | Email, calls, meetings, web, content, events, social, advertising, or other tracked channels |
| Activities | Scheduled communication, manual work, content, approvals, follow-ups, and handoffs |
| Budget | Planned budget, actual cost, currency, approval threshold |
| Ownership | Campaign owner, agent owner, activity assignees, human approvers |
| Targets | KPI definitions, baseline, target, attribution window |
| Results | Delivery, engagement, leads, opportunities, purchases, revenue, cost, and lessons |
| Governance | Consent, privacy, claims, pricing, approval, frequency, and suppression policy |

The primary objective should normally be required. Without an objective and target, Virtual Company cannot determine whether a campaign is successful or give trustworthy optimization advice.

Example objectives:

- Generate 30 qualified leads.
- Book 10 sales meetings.
- Create SEK 500,000 of qualified pipeline.
- Win SEK 150,000 in campaign-attributed revenue.
- Sell 300 units of a consumer product.
- Renew 80 percent of targeted subscriptions.
- Register 100 attendees for an event.

## Campaign Types

Initial campaign types should be deliberately small and extensible:

- Lead generation
- Account-based sales
- Product launch
- Promotion
- Nurture
- Re-engagement
- Cross-sell or upsell
- Renewal
- Event or webinar
- Customer education

The campaign type can supply recommended objectives, activities, metrics, and audience rules, but must not hide the underlying data from the operator.

## Lifecycle

Campaign lifecycle should be explicit and policy-driven:

1. **Draft**
   Define the purpose, objective, audience, offer, owner, budget, and dates.

2. **Planning**
   Build activities, channels, content, responsibilities, sequence timing, and measurement rules.

3. **Ready for approval**
   Validate audience consent, exclusions, budget, product claims, pricing, communication content, and planned side effects.

4. **Scheduled**
   The campaign is approved and waiting for its start date or an allowed business trigger.

5. **Running**
   Execute due activities and react to replies, purchases, meetings, bounces, opt-outs, and deal changes.

6. **Paused**
   Stop future executable activities without losing state. Manual tasks remain visible and can be explicitly cancelled or retained.

7. **Completed**
   The campaign reached its end date, objective completion rule, or an authorized manual completion.

8. **Reviewed**
   Record results, attribution confidence, lessons, accepted recommendations, and follow-up work.

Stopped and cancelled should remain terminal exception states with a reason and audit trail.

Campaign preparation, execution, approvals, and review should appear as durable tasks on the Agent Team board. Work actively performed by Alex belongs in **Ongoing**; decisions awaiting a person belong in **Waiting for human approval**.

## B2B Campaigns

B2B campaigns are usually account and relationship oriented.

### Typical B2B data

- Target accounts, industries, geography, and company size
- Account fit and intent signals
- Known contacts and buying roles
- Decision makers, champions, users, finance, and procurement stakeholders
- Existing relationships, activities, support history, and open issues
- Current opportunities, stages, values, products, and expected close dates
- Communication consent and preferred language
- Product fit, approved claims, pricing, and commercial policy

### Typical B2B activities

- Account research
- Buying-group and stakeholder mapping
- Personalized email sequences
- Sales calls and manual social outreach tasks
- Discovery meetings
- Webinars, events, and demonstrations
- Proposal preparation
- Commercial and legal review
- Stakeholder follow-up
- Sales, finance, support, and customer-success handoffs

### Important B2B KPIs

- Engaged accounts
- Qualified contacts
- Meetings booked
- Marketing-qualified and sales-qualified leads
- Opportunities created
- Pipeline value
- Opportunity conversion rate
- Win rate
- Sales-cycle duration
- Campaign-influenced and campaign-attributed revenue

When a contact replies, requests a meeting, or becomes a qualified opportunity, generic outreach should stop according to policy. Virtual Company should update the contact timeline, create or enrich the opportunity, assign the next action, and retain campaign attribution.

## B2C Campaigns

B2C campaigns are usually customer, product, and transaction oriented.

### Typical B2C data

- Customer segment and lifecycle state
- Product interests and purchase history
- Geography, language, and communication preferences
- Engagement and transaction history
- Cart, subscription, renewal, or loyalty status
- Consent, suppression, unsubscribe, and complaint state
- Offer eligibility and promotion limits

### Typical B2C activities

- Product launch communication
- Promotions and discount offers
- Newsletters and educational content
- Abandoned-cart reminders
- Re-engagement
- Cross-sell and upsell
- Seasonal campaigns
- Renewal reminders
- Surveys and feedback requests

### Important B2C KPIs

- Reach and delivery rate
- Click and engagement rate
- Conversion rate
- Units sold
- Revenue and average order value
- Cost per acquisition
- Return on campaign spend
- Repeat purchase or renewal rate
- Unsubscribe, complaint, and bounce rate

B2C audiences can be substantially larger than B2B audiences. Frequency limits, consent, suppression, batching, provider quotas, and observable delivery failures are therefore mandatory.

## Audience And Segmentation

Campaign audiences should support:

- Explicit selected contacts or accounts
- Saved dynamic segments
- Snapshot-at-launch membership for auditability
- B2B account and buying-role criteria
- B2C customer and transaction criteria
- Inclusion and exclusion rules
- Suppression lists
- Consent and channel eligibility
- Preferred communication language
- Estimated audience size before launch

The system must show why a person or account is included. Dynamic segments should be previewed before approval and snapshotted when execution begins so later source-data changes do not make the audit trail ambiguous.

Audience records must retain:

- Source and selection rule
- Enrollment time
- consent decision and evidence
- language resolution
- exclusion or stop reason
- current campaign state
- linked account, contact, prospect, customer, and deal identifiers when available

## Activities And Channels

Campaigns should contain ordered or scheduled activities rather than only email steps.

Useful activity types:

- Email
- Sales call
- Meeting or demonstration
- Manual social outreach
- Web form or landing page
- Content publication
- Paid advertisement
- Webinar or physical event
- Direct mail
- Survey
- Internal preparation
- Approval
- Cross-agent handoff

Channels should be separated into:

1. **Executable channels** that Virtual Company can perform through an approved connected provider, initially the existing email implementation.
2. **Tracked activities** for which Virtual Company creates a task, schedule, asset reference, and result but does not perform the external action.

This allows broad campaign planning without requiring a vendor-specific implementation for every advertising, social, event, or content platform.

Activities should store:

- Activity type and channel
- Owner and assignee
- planned start, due time, and timezone
- dependencies and milestone
- target audience or account subset
- content or asset reference
- execution mode: automatic, approval required, or manual
- provider/tool requirement
- status, result, evidence, and failure reason

## Time Planning

Campaigns should normally be time planned.

Required scheduling concepts:

- Planning start
- Launch date
- Campaign end date or explicit evergreen designation
- Company timezone
- Activity timing and dependencies
- Milestones
- Quiet hours and allowed send windows
- Recurrence for tracked activities where justified
- Objective measurement and attribution window

The scheduler should create durable due work rather than relying on an in-memory timer. Execution must be idempotent, retryable, observable, and safe across restarts.

Evergreen campaigns should still have review dates, limits, and an explicit stop condition. Ongoing trigger-based journeys should eventually be represented as automations rather than campaigns.

## Sales Integration

Campaigns should integrate directly with the existing Sales lifecycle:

- **Prospects:** engagement creates or enriches prospects without duplicating existing records.
- **Contacts and accounts:** membership, messages, activities, and outcomes appear on timelines.
- **Pipeline:** qualified responses can create or link opportunities with review and attribution.
- **Deals:** campaigns can target stalled, renewal, cross-sell, or expansion opportunities.
- **Activities:** calls, meetings, and manual follow-ups become assigned sales tasks.
- **Inbox:** replies are associated with the campaign, contact, account, and deal.
- **Agent Team:** Alex's campaign work and approvals are visible as durable tasks.
- **Forecast:** linked opportunities contribute to forecast scenarios without overstating certainty.
- **Products:** campaign offers reference approved product catalog and pricing data.
- **Finance:** campaign cost and won revenue support ROI analysis.
- **Support:** open critical issues, complaints, or negative sentiment can suppress unsuitable outreach.

The campaign should stop or change its next activities when authoritative events occur, including reply, meeting booked, opportunity created, purchase, opt-out, bounce, complaint, or disqualifying support issue.

## Attribution

Attribution should be evidence-backed and should not claim causation where only association is known.

Minimum attribution data:

- Original acquisition source
- Campaign membership
- First campaign interaction
- Most recent campaign interaction
- Opportunity creation influence
- Purchase or won-deal association
- Attribution model and window
- Confidence and supporting events

Initial reporting should provide:

- First-touch association
- Last-touch association
- Campaign-influenced pipeline and revenue
- Directly attributable conversion where a stable campaign identifier exists

Multi-touch attribution can be added later, but raw evidence must remain inspectable.

## Budget And Results

Campaign planning should support:

- Planned budget by campaign and optional channel
- Currency
- Approval threshold
- Committed and actual cost
- External provider cost references
- Revenue associated with purchases or won opportunities
- Cost per lead, meeting, opportunity, acquisition, or purchase
- Return on campaign spend

Finance remains authoritative for booked cost and revenue. Sales may display campaign-specific projections and associations but should not manufacture accounting values.

## Alex's Role

Alex can support campaigns by:

- Suggesting objectives and realistic target ranges from available history
- Detecting weak or contradictory audience rules
- Recommending activity plans and timing
- Drafting source-backed communication
- Identifying missing consent, product, pricing, or customer evidence
- Ranking follow-up work
- Explaining delivery, engagement, and conversion changes
- Recommending pause, continue, stop, or experiment decisions
- Preparing a post-campaign review

Alex must not:

- Invent product capabilities, customer facts, consent, prices, or results
- Change deterministic audience eligibility
- Launch a campaign without required approval
- Send through an unregistered or unauthorized provider
- Change prices, commitments, or commercial terms without policy
- Present correlation as proven causation

AI recommendations should contain source references, confidence, missing evidence, expected impact, risks, and a human-review requirement where appropriate.

## Campaign User Experience

The Campaigns surface should answer:

- What is running now?
- What objective is each campaign pursuing?
- What needs attention or approval?
- What activity happens next?
- Which audiences, products, and channels are involved?
- Is the campaign on track?
- What has it contributed to leads, pipeline, purchases, revenue, and cost?

Recommended views:

- Campaign list grouped by lifecycle and attention state
- Campaign details with objective, progress, owner, dates, audience, and budget
- Activity timeline or plan
- Audience preview and exclusions
- Content and approval queue
- Performance and attribution
- Alex's recommendations and evidence
- Post-campaign review

The implementation should follow `/docs/design.md` and `ui-instructions.md`, including the required screenshot-first workflow for the substantial Campaigns redesign.

## Recommended Delivery Order

1. Extend the campaign domain with objectives, products, ownership, dates, budget, and lifecycle.
2. Add B2B and B2C segmentation with consent-aware audience snapshots.
3. Add campaign activities and scheduling while retaining existing email sequences.
4. Add governed executable and manually tracked activity processing.
5. Connect campaign events to prospects, accounts, contacts, deals, tasks, inboxes, and support suppressions.
6. Add attribution, campaign cost, revenue association, and KPI reporting.
7. Add Alex's governed planning, optimization, and post-campaign analysis.
8. Redesign the Campaigns surface around operational planning, attention, execution, and results.

## Implemented Campaign Operating Model

The campaign implementation extends the existing sales campaign and sequence model rather than introducing a parallel subsystem:

- Campaigns have an explicit lifecycle, owner, objective, operating dates, timezone, budget, offers, milestones, and optimistic concurrency version.
- Reusable B2B and B2C audience segments produce immutable, versioned audience snapshots. Snapshot members retain eligibility, consent, language, and inclusion evidence.
- Campaign activities support manual work, governed cross-agent handoffs, and approved provider-backed execution. Claims and provider delegation are idempotent.
- Existing email sequence steps remain authoritative for email delivery and appear in the campaign activity plan as read-only linked activity.
- Campaign performance combines delivery, engagement, deal attribution, revenue evidence, costs, KPI definitions, and versioned KPI snapshots without treating correlation as proven causation.
- Alex receives campaign-scoped evidence and can recommend actions, experiments, or reviews. Deterministic eligibility, consent, approval, and provider policy remain authoritative.
- The Campaigns workspace exposes overview, audience, activity plan, performance, and Alex review surfaces backed by company-scoped API endpoints.

The persistence change is delivered through the `AddCampaignInitiativeManagement` EF migration. It uses provider-neutral EF SQL Server operations and remains compatible with both local SQL Server and the repository's Docker SQL Server restore and run path.
