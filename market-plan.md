# Strategy-grounded marketing plans and campaigns

## Outcome

Make the marketing plan the parent planning artifact for Marketing while keeping `SalesCampaign` as the canonical campaign entity. Maya, the Marketing agent, should be able to independently assess whether planning work is needed, create an internally governed plan draft, and populate that plan with campaign drafts grounded in an approved Marketing strategy and approved segment versions.

Creating internal drafts does not authorize Maya to approve budgets, activate plans, launch campaigns, publish content, spend money, enroll contacts, or send messages. Those actions remain behind the existing authority, readiness, approval, consent, workflow, and external-delivery boundaries.

```text
Approved Marketing strategy and exact strategy version
  └─ Approved target segment versions
      └─ Marketing plan
          ├─ Company Marketing objectives
          ├─ Plan budget and period
          ├─ Campaign A
          │   ├─ Target segment versions
          │   ├─ Audience preview/snapshot
          │   ├─ Activities
          │   └─ Content briefs
          └─ Campaign B
              ├─ Target segment versions
              ├─ Audience preview/snapshot
              ├─ Activities
              └─ Content briefs
```

In product terms: strategy determines direction, segmentation determines the audiences, the plan allocates objectives and resources, campaigns execute the plan, and the Calendar shows when the work occurs.

## Current implementation

The solution already contains most of the required parts, but their current relationship is campaign-first rather than plan-first:

- `MarketingPlan` stores a name, summary, period, budget, owner, status, and version. It links to Marketing objectives through `MarketingPlanObjective`, but it does not directly record its strategy basis or target segment versions.
- `MarketingStrategy` can link to approved Marketing segment versions through `MarketingStrategySegment`.
- `MarketingStrategyCampaignLink` currently combines a strategy, a plan, one Sales campaign, and one Marketing segment version.
- `PrepareMarketingDecompositionRequest` requires an existing `CampaignId`. Committing decomposition then creates a plan, links the objective, creates campaign activities and content briefs, and attaches everything to the existing campaign.
- `SalesCampaign` already owns the canonical campaign lifecycle, schedule, objectives, offers, activities, audiences, sequences, readiness, approvals, and launch behavior. A second Marketing campaign entity would duplicate that system of record.
- `MarketingOperatingLoopService` can create a generic 90-day Marketing plan through `marketing.prepare_plan`, but the created plan is not explicitly grounded in a strategy, its linked segment versions, or selected objectives.
- `RoleAgentCadenceBackgroundService` already invokes `MarketingOperatingLoopService` for active Marketing agents on the daily cadence. This existing cadence must be extended rather than adding another scheduler.
- The Marketing Calendar currently projects scheduled campaign launches and campaign activities. It does not include plan spans despite the UI language saying that dated plans are combined into the Calendar.

## Target domain model

### Marketing plan as the strategic parent

Extend `MarketingPlan` with the following queryable state:

- `MarketingStrategyId`
- `MarketingStrategyVersion`
- `PlanningRationale`
- `EvidenceReferencesJson`
- `MissingEvidenceJson`
- `ApprovalRequestId`
- A governed lifecycle such as `draft`, `in_review`, `approved`, `active`, `completed`, and `cancelled`

The plan must capture the exact strategy version used when it was proposed. A later strategy edit must not silently rewrite the plan's basis. A strategy change can instead make a plan require review.

Preserve `MarketingPlanObjective` for the plan-to-objective relationship.

### Plan segment versions

Add `MarketingPlanSegment` with:

- `Id`
- `CompanyId`
- `MarketingPlanId`
- `MarketingCustomerSegmentVersionId`
- `Role`, such as `primary`, `secondary`, or `excluded`
- `Priority`
- `Rationale`
- `ExpectedContributionJson`
- `CreatedUtc`

A plan may address multiple segment versions, but every included version must belong to the company, be approved or active, and be linked to the selected strategy. The exact version is retained for auditability and impact analysis.

### Plan campaigns

Add `MarketingPlanCampaign` with:

- `Id`
- `CompanyId`
- `MarketingPlanId`
- `SalesCampaignId`
- `Purpose`
- `AllocatedBudget`
- `BudgetCurrency`
- `Priority`
- `ExpectedContributionJson`
- `Status`
- `CreatedByAgentId`
- `IdempotencyKey`
- `CreatedUtc`
- `UpdatedUtc`

Use a one-plan-to-many-campaign relationship. A Sales campaign should have at most one owning Marketing plan so that budget ownership, performance attribution, and approval context remain understandable.

Add `MarketingPlanCampaignSegment` for campaigns that address one or more of the plan's target segment versions. It should retain the plan-campaign link, exact segment version, rationale, and expected audience contribution.

The selected strategy is inherited through the Marketing plan and should not need to be redundantly stored on every new plan-campaign link.

### Existing link migration

Migrate existing `MarketingStrategyCampaignLink` records into the new plan, plan-segment, plan-campaign, and campaign-segment relationships. Preserve existing IDs and relationships where practical, and preserve the current table while compatibility code is still required. Do not discard or reinterpret existing links silently.

All schema work must use an EF Core migration in `VirtualCompany.Persistence.Migrations`. Local SQL Server and Docker SQL Server must use the same migration history and retain a clear restore-and-run path.

## Application boundaries

### Marketing owns portfolio planning

Marketing application contracts should own:

- Preparing a strategy-grounded plan proposal
- Committing a plan draft idempotently
- Assessing plan readiness
- Preparing a campaign portfolio proposal for a plan
- Populating a plan with campaign drafts
- Assessing objective, segment, budget, channel, and schedule coverage
- Submitting a plan for review and activating it after approval

These operations belong in focused Marketing application contracts and capability-owned implementations under `VirtualCompany.Infrastructure.Sales/Marketing`. Controllers remain transport-only.

### Sales owns campaign invariants

Do not create a separate `MarketingCampaign`. Extend the Sales application boundary with a command for creating an incomplete campaign initiative draft without requiring contacts or a complete outbound sequence.

The draft command should accept enough planning context to create a real `SalesCampaign` in `draft` or `planning`, including:

- Name and purpose
- Campaign type
- Owner user and optional owner agent
- Campaign objective and target date
- Planning, launch, review, and end dates
- Time zone
- Proposed budget and currency
- Communication language and preferred channels where relevant
- Offer requirement or grounded offer reference
- Idempotency key

Marketing orchestration calls this Sales application contract. It must not write Sales aggregates directly. The Sales campaign remains incomplete until its normal audience, offer, activities, content, permissions, sequence, and readiness requirements are satisfied.

## Plan and campaign policies

Introduce a deterministic `MarketingPlanReadinessPolicy` that returns:

- Whether the transition is allowed
- A stable reason code
- A plain-English explanation
- Whether approval is required
- The evidence used

A plan cannot activate unless:

- Its strategy is approved or active.
- Its captured strategy version is still the approved basis or has received an explicit impact review.
- Every selected target segment version is approved or active and linked to the strategy.
- It has at least one active Marketing objective.
- Its dates fit within the strategy validity period.
- It has at least one linked campaign draft.
- Every campaign fits inside the plan period.
- Campaign budget allocations do not exceed the plan budget.
- Every campaign targets at least one included plan segment.
- Every objective has a declared campaign contribution or an explicit documented gap.
- Superseded segment versions have been explicitly reviewed.
- The exact plan version has received any required approval.

Campaign readiness remains governed by the Sales campaign policy. It must continue to check audience eligibility, consent, offer, activities, content, schedule, sequence, approval, and external execution requirements.

## Agent tools and authority

Separate read, recommend, and execute semantics explicitly.

### Read tools

- `marketing.read_active_strategy`
- `marketing.read_strategy_segments`
- `marketing.read_objectives`
- `marketing.read_existing_plans`
- `marketing.read_campaigns`
- `marketing.read_plan_coverage`
- `marketing.read_performance`
- Existing approved-knowledge, audience-evidence, segment, observation, and attribution tools

### Recommendation tools

- `marketing.prepare_plan`
- `marketing.prepare_campaign_portfolio`
- `marketing.assess_plan_coverage`
- `marketing.assess_segment_strategy_impact`

Recommendation tools create structured, reviewable proposals and do not mutate business records.

### Internal execution tools

- `marketing.create_plan_draft`
- `marketing.create_campaign_drafts`
- `marketing.populate_campaign_draft`
- `marketing.submit_plan_for_review`
- `marketing.submit_campaign_for_readiness`

Creating internal drafts may be permitted at `OperateInternally` authority. Approving budgets, activating plans, launching campaigns, publishing content, provider writes, enrolling contacts, and sending messages remain governed separately. Tool access must be company-scoped, permissioned, guarded, idempotent, audited, and limited by company and agent autonomy.

## Daily operating flow

The API-hosted role-agent cadence performs a daily pass as soon as the solution starts, in addition to its normal polling schedule. The startup pass is daily-only (it does not implicitly run weekly or monthly work), uses the same company/agent scope and idempotency keys as the scheduled pass, and remains safe to retry if startup occurs more than once on the same day. The existing `RoleAgentCadenceBackgroundService` remains the single scheduler; it runs Finance, Sales, Marketing, and Support daily work and continues to enforce the configured daily hour for later polling runs.

The existing daily role-agent cadence already triggers `MarketingOperatingLoopService`. Keep that scheduler as the single daily trigger and add a deterministic work-need assessment at the beginning of the Marketing operating run.

### Daily sequence

1. Resolve the company, active Maya agent, configured autonomy, operating assignment, company pause state, capacity, and budget.
2. Capture the authoritative company operating snapshot.
3. Run `MarketingWorkNeedAssessment` before any new planning model call or artifact creation.
4. Load approved or active strategies and their exact linked segment versions.
5. Load active Marketing objectives, current plans, plan-campaign links, campaign readiness, content deadlines, recent performance, attribution, and unresolved Marketing signals.
6. Produce deterministic, ranked need records with reason codes, evidence references, urgency, and an idempotency fingerprint.
7. If no Marketing work is required, complete the daily operating run as `no_work_required`, persist the evidence and rationale, and create no plan, campaign, task, or model cost.
8. If work is required, select the highest-value permitted need within capacity and budget.
9. Use AI analysis only where judgment or proposal generation is needed; deterministic policies remain authoritative.
10. Depending on authority, either record a recommendation, organize a task, or create internal plan/campaign drafts.
11. Recheck policies immediately before each state-changing command.
12. Persist artifacts, rationale, evidence versions, audit records, missing evidence, and safe failure/retry state.

### Work-need reason codes

At minimum, assess:

- `strategy_missing_or_expired`
- `approved_segments_missing`
- `objective_without_plan`
- `plan_missing_for_horizon`
- `plan_ending_soon`
- `plan_has_no_campaigns`
- `objective_without_campaign_coverage`
- `target_segment_without_campaign`
- `campaign_draft_incomplete`
- `campaign_readiness_due`
- `campaign_schedule_conflict`
- `plan_budget_overallocated`
- `segment_version_superseded`
- `performance_below_plan`
- `content_or_activity_overdue`
- `approval_waiting`

The assessment should distinguish actionable work from information-only conditions. For example, `approval_waiting` should not cause Maya to create a replacement plan or duplicate campaign.

### Daily idempotency and duplicate prevention

- Preserve the existing daily cadence key based on company, agent, cadence, and date.
- Give each detected need a stable fingerprint derived from company, need code, affected record IDs and versions, strategy version, selected segment versions, objective IDs, and planning horizon.
- Plan and campaign draft commands must use business idempotency keys derived from the need fingerprint, not random retry IDs.
- A retry or second poll in the same daily window must return the existing run and existing artifacts.
- A new daily run must not create another active plan for the same strategy, segment set, objectives, and overlapping horizon unless the prior plan is cancelled, superseded, rejected, or the need assessment documents a material change.
- A plan-population run must not recreate an equivalent campaign purpose for the same plan, segment set, objective contribution, and time window.

### Daily safe outcomes

The daily run must finish in an operator-visible state such as:

- No work required
- Recommendation ready
- Draft plan created
- Campaign drafts created
- Waiting for approval
- Blocked by missing evidence
- Blocked by policy or capacity
- Retry scheduled after a transient internal failure

Missing strategy or approved segmentation should normally produce a recommendation, internal task, or review signal rather than an invented plan based on assumptions.

## Agent planning flow

When the daily assessment or an on-demand request identifies a plan need, Maya should:

1. Select one approved or active Marketing strategy and record its exact version.
2. Load only approved or active segment versions linked to that strategy.
3. Load company objectives, existing overlapping plans, current campaigns, budget constraints, recent performance, and approved company knowledge.
4. Prepare a plan proposal containing strategy basis, selected segments, objectives, period, budget, rationale, evidence, assumptions, missing evidence, risks, and a proposed campaign portfolio.
5. Run deterministic proposal validation.
6. Create the plan as a draft when authority permits; otherwise preserve the proposal for review.
7. Prepare a campaign portfolio that declares, for each campaign, its purpose, objective contribution, target segment versions, allocated budget, dates, channels, offer basis, activities, content needs, and measurement approach.
8. Create Sales campaign drafts through the Sales application boundary.
9. Add plan-campaign and campaign-segment relationships transactionally and idempotently.
10. Create internal activities, content briefs, and company tasks where permitted.
11. Generate audience previews where permitted, but do not enroll or contact people.
12. Assess portfolio coverage and submit the exact plan version for review when ready.

## Coverage assessment

The portfolio assessment must identify:

- Objectives with no supporting campaign
- Target segments with no campaign
- Campaigns that target segments outside the plan
- Duplicate campaign purposes
- Budget over-allocation or currency mismatch
- Campaign dates outside the plan
- Channel or schedule conflicts
- Missing offers, activities, content briefs, audience evidence, or measurement plans
- Superseded strategy or segment versions
- Existing active campaigns that already satisfy the proposed need

Coverage results are read models and policy evidence. The UI and agent must not independently reproduce their rules.

## Calendar and user experience

Reshape the Marketing workspace around the hierarchy:

- **Plans:** show strategy, objectives, segments, period, budget allocation, campaign count, owner, readiness, and review state.
- **Plan detail:** show the campaign portfolio, coverage gaps, evidence, approvals, and an `Ask Maya to populate plan` action.
- **Campaign detail:** show inherited strategy and plan context, exact target segment versions, objective contribution, allocated budget, and readiness.
- **Calendar:** combine plan start/end, campaign planning/launch/review/end dates, campaign activities, and content due dates.
- **Daily review:** show whether Maya found work, what evidence was checked, what she created or recommended, and what needs human attention.

Use plain English and the existing design system. Do not expose raw status values, internal reason codes, tool IDs, or tenant terminology. Any significant new page or major redesign requires the repository's screenshot-first UI workflow before implementation.

## Delivery sequence

1. Add the strategy-grounded plan, plan-segment, plan-campaign, and campaign-segment data model with migration and existing-link backfill.
2. Add strategy-aware plan proposal, command, lifecycle, approval, readiness, and coverage behavior.
3. Add the Sales-owned campaign draft command and Marketing-owned plan population orchestration.
4. Add explicit Marketing read, recommend, and internal-execution tools.
5. Add deterministic daily work-need assessment to the existing Marketing cadence path.
6. Update Marketing analysis context and operating-loop execution to use the new plans and campaign portfolios.
7. Update dashboard, detail, Calendar, operating-run read models, APIs, and Web clients.
8. Implement and visually verify the plan portfolio, campaign context, Calendar, and daily-review UI.

## Verification expectations

Each delivery stage must include proportionate tests for:

- Tenant-isolated reads and writes
- Cross-company reference rejection
- Authorization and agent access
- Strategy and segment version validity
- Plan and campaign readiness policies
- Budget, date, objective, and segment coverage rules
- Optimistic concurrency and exact-version approval
- Command and daily-run idempotency
- Duplicate daily poll and retry behavior
- No-work daily outcomes
- Authority levels: recommend, organize, and operate internally
- Missing evidence and blocked states
- Sales campaign readiness and launch preservation
- EF migration and SQL Server model correctness
- Local SQL Server and Docker restore/run compatibility
- API and Web contracts
- Calendar projection correctness
- UI loading, empty, ready, waiting, and blocked states

The finished implementation must contain no mock production data, silent failures, parallel campaign system of record, direct LLM-provider calls, direct external side effects from request handlers, or deferred in-scope TODOs.
