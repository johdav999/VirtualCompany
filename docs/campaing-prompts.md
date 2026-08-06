# Campaign Implementation Prompts

## Purpose And Execution Order

This prompt pack implements the campaign model described in `/docs/campaign.md`. Execute the prompts in order. Each stage must preserve the existing governed outbound email campaign behavior while delivering an independently usable increment.

1. Campaign initiative foundation
2. B2B and B2C audience segmentation
3. Cross-channel campaign activity planning
4. Governed scheduling and execution
5. Sales lifecycle integration and attribution
6. Campaign financials and performance
7. Alex campaign intelligence
8. Production Campaigns experience

## Shared Instructions For Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `/docs/campaign.md`, `/docs/architecture-rules.md`, and `/docs/architecture-overview.md` as background. Follow `architecture-inst.md` if it exists.
- For UI work, also read and follow `ui-instructions.md` and `/docs/design.md`, including the mandatory screenshot-first workflow.
- Existing repository behavior wins over older planning documents.
- Extend the existing `SalesCampaign`, `SalesCampaignContact`, `SalesSequence`, `IOutboundCampaignService`, `ISequenceExecutionService`, `SalesCampaignsController`, Sales API client, and Campaigns page rather than creating a parallel campaign subsystem.
- Preserve company authorization, tenant isolation, communication consent, suppression, approval, audit, idempotency, retry, reconciliation, and safe operator-visible failure behavior.
- External side effects must use approved tools/providers and durable background execution. Never invoke a provider directly from a controller or UI component.
- AI may explain and recommend but cannot become authoritative for eligibility, consent, prices, product claims, campaign state, accounting values, or attribution evidence.
- Schema changes require an EF migration in `VirtualCompany.Persistence.Migrations` and equivalent compatibility for local SQL Server and Docker SQL Server restore/run flows.
- Do not add mock production data, silent fallback, unhandled intermediate states, or deferred in-scope TODOs.

---

## Prompt 1: Establish Campaign Initiatives, Objectives, And Lifecycle

### 1. Title And Outcome

Extend the existing outbound campaign into a governed commercial initiative with a required objective, ownership, products, schedule, budget plan, and explicit lifecycle. This gives operators a measurable reason and bounded operating period for every campaign.

### 2. Current Context

- `SalesCampaign` is a company-owned domain entity linked one-to-one with `SalesSequence`.
- It currently stores name, audience type, communication language, outbound policy, approval state, status, and lifecycle timestamps.
- `CreateOutboundCampaignRequest` currently contains name, description, audience type, contact IDs, outbound policy, email sequence steps, and communication language.
- `OutboundCampaignService` validates active company-owned contacts, creates and activates a sequence, enrolls contacts, and handles launch, pause, and stop.
- `SalesCampaignsController` exposes list, audience options, create, launch, pause, stop, draft, delivery, bounce, reply, and deal-created operations.
- Existing campaign status is optimized for email sequence execution and does not represent planning, scheduled launch, review, objectives, products, owners, or budget.

### 3. Dependencies

None.

### 4. Implementation Requirements

- Extend the domain model with:
  - campaign type;
  - description if not already persisted through the sequence;
  - primary human owner and Alex agent ownership reference where appropriate;
  - primary objective type, target value, unit, and target date;
  - optional secondary objectives using a normalized child entity rather than opaque JSON;
  - planning start, launch, end, timezone, and optional review date;
  - planned budget and currency;
  - linked product/service offer references;
  - lifecycle states for draft, planning, waiting for approval, scheduled, running, paused, completed, reviewed, stopped, and cancelled.
- Define domain transitions and invariants. A campaign cannot become ready for approval without an objective, owner, audience basis, offer or documented no-offer purpose, dates, and at least one activity plan.
- Preserve compatibility with current campaigns through a deterministic legacy classification and migration defaults. Do not fabricate business objectives for historical rows; show them as requiring setup.
- Keep sequence execution status separate from campaign lifecycle where necessary. Do not overload one raw string with conflicting meanings.
- Extend application contracts, service mappings, API endpoints, API client, audit events, and authorization checks.
- Add optimistic concurrency or authoritative version checking for updates and transitions.
- Emit durable Agent Team tasks for material campaign preparation and approval states through existing task/workflow boundaries.
- Add an EF migration, model snapshot updates, indexes for company/lifecycle/date queries, and local/Docker SQL Server migration verification.
- Document lifecycle transition rules and legacy behavior in relevant architecture or feature documentation.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Existing email campaign launch, pause, stop, reply, bounce, delivery, and deal-created behavior must remain functional.
- Do not silently launch legacy or newly scheduled campaigns.
- Campaign objective progress is derived from authoritative evidence; it is not manually asserted as achieved without an audited override.
- Product links must resolve to approved company-owned product catalog entries.
- Store currency-qualified amounts; never add amounts across currencies without an explicit conversion source and as-of time.

### 6. Acceptance Criteria

- Given a new campaign without a primary objective, when readiness is requested, then the request is rejected with plain-language missing requirements.
- Given a complete approved campaign with a future launch time, when approval completes, then it enters Scheduled rather than Running.
- Given a legacy outbound campaign, when it is read, then its existing execution remains available and its missing initiative fields are visibly reported without invented values.
- Given a campaign owned by another company, when accessed or linked to an objective, owner, or product, then no data is disclosed or changed.
- Given two users editing the same campaign version, when the stale update is submitted, then it fails with a refresh-required conflict.

### 7. Verification

- Unit-test domain invariants and every allowed and rejected lifecycle transition.
- Add migration tests covering existing campaigns, indexes, rollback constraints where supported, and local/Docker SQL Server compatibility.
- Add service and API tests for create, update, readiness, scheduling, ownership, product links, authorization, audit, concurrency, and tenant isolation.
- Extend current outbound campaign and sequence execution regression tests.
- Build Domain, Application, Sales Infrastructure, API, Web, and migration projects.

### 8. Definition Of Done

Campaigns have measurable objectives, bounded ownership and timing, governed lifecycle transitions, and backward-compatible email execution. All production persistence, APIs, authorization, audit, migration, and failure states are complete.

---

## Prompt 2: Implement Consent-Aware B2B And B2C Audiences

### 1. Title And Outcome

Implement reusable audience segments and auditable launch snapshots that support both account-based B2B targeting and customer/product-oriented B2C targeting.

### 2. Current Context

- Campaign creation accepts explicit contact IDs and an `AudienceType`.
- `GetAudienceOptionsAsync` exposes active contacts and simple source labels for existing contacts, past customers, and imported contacts.
- `SalesCampaignContact` records enrollment, current sequence step, and completion or cancellation timestamps.
- Prospects, contacts, customer companies, deals, activities, email links, source policy, preferred language, and consent-related communication controls already exist in Sales and Mailbox modules.
- Audience inclusion currently lacks reusable rule definitions, account/buying-role criteria, transaction-oriented B2C criteria, exclusions, preview explanations, and snapshot provenance.

### 3. Dependencies

- Prompt 1.

### 4. Implementation Requirements

- Add company-owned saved audience segments with a typed segment kind: B2B account, B2B contact, B2C customer, or explicit list.
- Model supported criteria through structured entities/contracts:
  - B2B account attributes, industry, geography, company size, account state, deal state, product fit, engagement, and buying role;
  - B2C customer lifecycle, product interest, purchase or subscription facts available in authoritative records, geography, language, and engagement;
  - explicit inclusion, exclusion, suppression, and freshness rules.
- Implement a deterministic segment evaluator over approved repository/application boundaries. Do not use AI as the inclusion engine.
- Add audience preview with estimated count, inclusion reason, exclusion reason, missing data, consent eligibility, language, and duplicate resolution.
- Snapshot membership and rule version when a campaign becomes ready for approval or launches, according to an explicit policy. Retain source IDs and evaluation evidence.
- Extend enrollment with account, prospect, customer, contact, and deal links where authoritative relationships exist.
- Re-evaluate eligibility immediately before each external communication. Stop or suppress pending steps after opt-out, complaint, bounce policy, invalid address, open critical support issue where configured, or other authoritative disqualification.
- Add deduplication by stable company-owned identities and normalized communication address. Ambiguities must require review rather than merging automatically.
- Add APIs and API client operations for segment CRUD, preview, audience snapshot, exclusions, and enrollment evidence.
- Add audit and operator-visible counts for eligible, excluded, suppressed, ambiguous, and missing-data records.
- Add EF migrations, indexes, and local/Docker SQL Server verification.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Consent and suppression are authoritative and cannot be overridden by AI or a segment rule.
- Every evaluated record and snapshot is company-scoped.
- Do not copy unnecessary personal data into snapshots; retain stable references and only the execution evidence required for audit.
- Preserve explicit-contact campaigns and migrate them to an explicit-list audience representation.
- B2C criteria must use data Virtual Company actually owns or imports through approved sources; do not assume an ecommerce platform exists.

### 6. Acceptance Criteria

- Given a B2B segment for target accounts and buying roles, when previewed, then each included contact shows the account and rule evidence that qualified it.
- Given a B2C segment with no authoritative purchase source configured, when purchase criteria are requested, then the segment is unavailable with a clear configuration message.
- Given an opted-out contact who otherwise matches, when previewed or executed, then the contact is suppressed.
- Given a contact who becomes ineligible after snapshot creation, when a step becomes due, then the step is cancelled or held according to policy and the reason is visible.
- Given duplicate or ambiguous identities, when evaluated, then no duplicate message is scheduled and the ambiguity is surfaced for review.

### 7. Verification

- Unit-test every supported criterion, inclusion/exclusion precedence, consent, language, deduplication, freshness, and snapshot behavior.
- Add integration tests for B2B, B2C, explicit lists, missing providers, opt-outs, critical support suppressions, and changed eligibility.
- Add authorization, tenant-isolation, retention, migration, and query-performance tests.
- Extend current audience option and sequence stop-condition tests.

### 8. Definition Of Done

Operators can define, preview, explain, approve, and snapshot B2B or B2C audiences without bypassing consent or creating duplicate outreach. No opaque or AI-decided audience eligibility remains.

---

## Prompt 3: Add Cross-Channel Campaign Activity Planning

### 1. Title And Outcome

Add an activity plan that coordinates executable email with manually tracked calls, meetings, content, events, web, social, advertising, approvals, and handoffs.

### 2. Current Context

- Campaign work is represented mainly by ordered `SalesSequenceStep` email steps with delay days, subject, body, and optional AI personalization.
- `ISequenceExecutionService` schedules and processes email steps.
- Virtual Company already has durable tasks, workflows, approvals, agent handoffs, activities, mailbox connections, and company tools.
- There is no campaign-level activity entity that can represent channel, owner, dates, dependencies, execution mode, tool requirement, content reference, or manual completion.

### 3. Dependencies

- Prompts 1-2.

### 4. Implementation Requirements

- Add company-owned campaign milestones and campaign activities.
- Support initial activity types: email, call, meeting/demo, manual social outreach, web/landing-page work, content publication, paid advertising, event/webinar, direct mail, survey, internal preparation, approval, and cross-agent handoff.
- Store channel, owner/assignee, planned start, due time, timezone, dependency, milestone, audience subset, content/asset reference, execution mode, required tool capability, status, result, evidence, and failure reason.
- Distinguish:
  - executable activities performed through an approved provider;
  - manual tracked activities represented by durable tasks;
  - approval activities represented by the approval subsystem;
  - handoffs represented by the shared agent handoff subsystem.
- Adapt current email sequence steps into campaign email activities without duplicating execution. The existing sequence remains the email execution authority until a later deliberate migration.
- Validate dependency graphs and reject cycles, impossible dates, terminal-dependency violations, and activities outside the campaign window unless explicitly approved.
- Generate stable durable tasks for manual activities and map their state back to the campaign plan idempotently.
- Allow operators to reschedule future activities with version checks and a visible impact preview.
- Add APIs, API client contracts, audit, authorization, observability, and EF migrations with required indexes.
- Document which channels are executable and which are tracked only.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not introduce vendor-specific advertising, social, or event integrations in this prompt.
- Creating a tracked activity must not imply that Virtual Company performed the external action.
- Existing email delays and drafts must preserve their behavior and identifiers.
- Human approval activities must appear in Waiting for human approval, not Ongoing.
- Agent work should appear in the correct Agent Team row and authoritative stage.

### 6. Acceptance Criteria

- Given a campaign with email, call, meeting, and approval activities, when the plan loads, then all activities appear in dependency and time order with their execution mode.
- Given a cyclic dependency, when saved, then validation rejects it with the involved activities identified.
- Given a manual call becoming due, when scheduled, then exactly one durable task is created for the assigned owner.
- Given an approval activity, when it becomes due, then it creates or links one approval and appears in Waiting for human approval.
- Given a legacy email campaign, when opened, then its sequence is represented in the activity plan without duplicate sends.

### 7. Verification

- Unit-test activity types, dependency graph validation, dates, execution modes, and state mapping.
- Add integration tests for task creation, approvals, handoffs, email adaptation, rescheduling, idempotency, and tenant isolation.
- Test Agent Team stage projection for planned, ongoing, waiting for approval, and completed activities.
- Add migration and local/Docker SQL Server checks.

### 8. Definition Of Done

Campaigns have a reliable cross-channel plan. Executable, manual, approval, and handoff work is clearly distinguished and backed by existing durable subsystems, with no duplicate email execution or false completion claims.

---

## Prompt 4: Implement Durable Campaign Scheduling And Governed Execution

### 1. Title And Outcome

Execute due campaign activities safely across restarts, approvals, pauses, provider failures, and eligibility changes while exposing operational state to users.

### 2. Current Context

- `ISequenceExecutionService` schedules campaign email executions and processes due steps.
- `SequenceExecutionBackgroundService` provides background processing.
- Email sending uses `IOutboundEmailSender`, idempotency keys, connected mailboxes, delivery state, bounces, replies, and stop conditions.
- Shared background execution, tasks, workflows, approvals, audit, and tool registry capabilities exist.
- Non-email campaign activities and campaign start/end scheduling are not yet coordinated by one durable campaign scheduler.

### 3. Dependencies

- Prompts 1-3.

### 4. Implementation Requirements

- Implement a campaign scheduling coordinator that selects due company-owned campaigns and activities in bounded indexed batches.
- Use durable claims/leases or equivalent concurrency control so multiple workers cannot execute the same activity.
- Revalidate campaign lifecycle, approval, audience eligibility, consent, provider/tool availability, dependencies, and authoritative record versions immediately before external execution.
- Preserve existing email execution by delegating to `ISequenceExecutionService`; do not build a second sender.
- Create tasks, approvals, and handoffs through existing application boundaries for non-email activity modes.
- Add stable idempotency keys per campaign/activity/audience execution and persist attempt/result state.
- Implement bounded retry, backoff, terminal failure classification, cancellation, pause/resume, stop, reconciliation, and dead-letter/operator review behavior.
- Scheduled campaigns should start only after approval and their due launch time. End-date processing should stop new work and complete only when policy-defined pending work is resolved.
- Expose last scheduler run, next due action, blocked reason, attempts, and safe recovery action.
- Add metrics and structured logs without message content, credentials, or unnecessary personal data.
- Add recovery behavior for application restart and partial provider success.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Never hold database transactions open during external provider calls.
- A paused, stopped, cancelled, unapproved, or ineligible campaign cannot produce a new external side effect.
- Retry must reuse the original idempotency key.
- Operator-visible success must reflect confirmed local/provider state, not merely a queued attempt.
- Do not allow one company's slow or failing campaign to block another company.

### 6. Acceptance Criteria

- Given two scheduler workers selecting the same due activity, when they run concurrently, then only one side effect occurs.
- Given an application restart after a provider accepted a send but before local completion, when reconciliation runs, then no duplicate send occurs.
- Given a campaign paused before a due activity executes, then the activity remains held and no provider call is made.
- Given consent revoked after scheduling, when execution begins, then the activity is suppressed with evidence.
- Given repeated terminal provider failure, then the activity enters an operator-visible failed state with a safe retry or cancel action.

### 7. Verification

- Unit-test due selection, revalidation, retry decisions, state transitions, and completion rules.
- Add concurrency, restart, partial-failure, provider-timeout, pause, stop, approval, and eligibility integration tests.
- Use fake providers to verify idempotency and reconciliation.
- Add scheduler query-plan/index checks where supported and ensure cancellation tokens bound shutdown.
- Extend email sequence background service regression tests.

### 8. Definition Of Done

Campaign execution is durable, restart-safe, idempotent, governed, observable, and isolated by company. Existing email behavior remains authoritative and all failure states have safe operator actions.

---

## Prompt 5: Integrate Campaigns With Prospects, Accounts, Deals, Tasks, And Support

### 1. Title And Outcome

Connect campaign engagement and outcomes to the complete Sales lifecycle so campaigns create useful follow-up work and attribution without duplicating CRM records or overstating causation.

### 2. Current Context

- Sales contains prospects, contacts, customer companies, deals, pipeline stages, activities, signals, mailbox links, reply detection, tasks, and forecasts.
- Current sequence processing handles reply, bounce, delivery, and deal-created stop conditions.
- Campaign membership does not yet provide a complete timeline or typed relationships to accounts, prospects, deals, purchases, meetings, and support suppressions.
- Support cases and customer context exist through separate application boundaries.

### 3. Dependencies

- Prompts 1-4.

### 4. Implementation Requirements

- Define typed campaign events for enrollment, activity scheduled, sent, delivered, engaged where evidence exists, replied, meeting booked, prospect qualified, opportunity created, stage changed, purchase recorded, won/lost, opt-out, complaint, bounce, and support suppression.
- Consume authoritative existing events or commands instead of inferring state from text.
- Link campaign events to contact, account, prospect, customer, deal, task, mailbox message/thread, and product where valid.
- Show campaign membership and significant activity on contact, account, prospect, and deal timelines.
- Add deterministic rules for:
  - stopping generic outreach after reply, meeting, opportunity, purchase, opt-out, or complaint;
  - creating reviewed next-action tasks;
  - linking or enriching existing prospects and deals;
  - avoiding duplicate leads, contacts, deals, or tasks.
- Add configurable support suppression using open critical cases, complaint state, or other approved support signals without exposing unrelated support content to Sales.
- Feed linked opportunity evidence into existing forecast analysis without changing forecast authority.
- Implement attribution evidence:
  - original source;
  - campaign membership;
  - first and last campaign interaction;
  - opportunity creation association;
  - won revenue or purchase association;
  - model, window, confidence, and source event IDs.
- Provide first-touch, last-touch, and influenced attribution views. Label association separately from direct stable-ID attribution.
- Add APIs, read models, audit, authorization, retention, and tenant-isolation enforcement.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not auto-merge ambiguous identities.
- Do not auto-create a deal solely from AI interpretation.
- Support data access must remain scoped to the minimum suppression signal needed by Sales.
- Attribution must never be presented as proven causation unless a deterministic direct campaign identifier establishes it.
- Existing CRM, pipeline, task, mailbox, and support systems remain authoritative.

### 6. Acceptance Criteria

- Given a campaign reply linked to an existing contact and deal, when processed, then the timeline is updated, generic pending outreach stops, and no duplicate contact or deal is created.
- Given an ambiguous sender identity, when a reply arrives, then it is held for review without cross-linking records.
- Given a critical support suppression, when outreach becomes due, then it is held or stopped according to policy without exposing case content.
- Given a won deal with campaign interactions, when attribution is shown, then the model, window, evidence, and confidence are visible.
- Given another company's campaign or CRM identifier, when queried, then no relationship or evidence is disclosed.

### 7. Verification

- Unit-test event mapping, stop rules, duplicate prevention, support suppression, and attribution calculations.
- Add integration tests across campaign, mailbox, prospect, contact, account, deal, task, forecast, and support boundaries.
- Add ambiguity, stale event, duplicate delivery, out-of-order event, authorization, tenant-isolation, and retention tests.
- Verify timeline read models remain bounded and indexed.

### 8. Definition Of Done

Campaign activity produces coherent, non-duplicated Sales follow-up and inspectable attribution evidence. Cross-module integration uses authoritative boundaries and preserves privacy and tenant isolation.

---

## Prompt 6: Add Campaign Budget, Cost, Revenue, And KPI Measurement

### 1. Title And Outcome

Measure campaign progress and commercial results using currency-safe costs, authoritative revenue associations, objective targets, and transparent KPI definitions.

### 2. Current Context

- Prompt 1 adds planned budget and objectives.
- Sales analytics already reports campaign send, reply, conversion, and forecast information.
- Finance owns booked financial facts and supports company currency-aware reporting.
- Campaign execution can produce provider outcomes, but channel costs, committed cost, actual cost, objective progress, and return metrics are not modeled comprehensively.

### 3. Dependencies

- Prompts 1-5.

### 4. Implementation Requirements

- Add normalized campaign KPI definitions with numerator, denominator, unit, baseline, target, attribution window, and data source.
- Provide standard metrics appropriate to:
  - B2B: engaged accounts, qualified contacts, meetings, opportunities, pipeline, wins, sales-cycle duration;
  - B2C: delivery, engagement, conversion, units, revenue, average order value, acquisition cost, repeat purchase, unsubscribe, complaint, and bounce.
- Model planned, committed, and actual campaign/channel costs with currency, source, observed time, and finance linkage.
- Treat Finance records as authoritative for booked costs and revenue. Keep projections and associations visibly separate.
- Calculate cost per lead, meeting, opportunity, acquisition, or purchase only when denominator and currency are valid.
- Calculate return on campaign spend without combining currencies unless an approved exchange-rate source and as-of time are present.
- Implement objective progress snapshots from authoritative campaign events and linked Sales/Finance records.
- Add period comparison only where comparable baselines exist; otherwise display no comparison.
- Add read models/APIs for overview, trend, channel, audience, objective, and attribution performance.
- Add export-ready, audit-friendly metric evidence without exposing unnecessary personal data.
- Add indexes/materialization or cached projections only after measuring query needs and preserving correct invalidation.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not manufacture revenue, cost, conversion, exchange rates, or historical baselines.
- Never sum unlike currencies.
- Distinguish projected, influenced, directly attributed, and booked values.
- Metric definitions and versions must be visible and stable for a completed campaign review.
- Analytics queries must remain company-scoped and bounded.

### 6. Acceptance Criteria

- Given booked Finance cost linked to a campaign, when performance loads, then actual cost shows the Finance source and observation time.
- Given campaign values in multiple currencies without conversion data, then totals remain separated by currency.
- Given zero eligible denominator, then a rate is shown as unavailable rather than zero percent.
- Given a directly tracked campaign purchase and an influenced deal, then the UI distinguishes direct attribution from influence.
- Given a completed campaign, when KPI definitions later change, then its review retains the version used at completion.

### 7. Verification

- Unit-test every metric formula, zero denominator, currency separation, attribution class, target progress, and snapshot versioning.
- Add Finance/Sales integration tests for booked cost, linked revenue, delayed records, corrections, and missing exchange rates.
- Add query performance, cache invalidation where used, authorization, and tenant-isolation tests.
- Verify no demo data or fabricated comparison appears in empty states.

### 8. Definition Of Done

Campaign results are commercially useful, currency-safe, attributable to inspectable evidence, and explicit about uncertainty. Finance authority and Sales projections remain clearly separated.

---

## Prompt 7: Implement Alex's Governed Campaign Intelligence

### 1. Title And Outcome

Give Alex source-backed campaign planning, quality checks, optimization recommendations, and post-campaign analysis without allowing AI to override policy or perform unapproved side effects.

### 2. Current Context

- Shared agent reasoning, grounded knowledge, durable runs, quality events, tasks, plans, handoffs, and approvals exist.
- Sales AI contracts include campaign optimization and campaign experiment results.
- `sales-ai.md` defines campaign and message optimization expectations over the shared reasoning gateway.
- Campaign objectives, audiences, activities, execution events, attribution, costs, and KPIs are delivered by Prompts 1-6.
- AI is not authoritative for consent, product facts, prices, audience eligibility, campaign state, accounting values, or attribution.

### 3. Dependencies

- Prompts 1-6.
- Shared AI capabilities described in `agents-ai.md` and implemented through the shared reasoning boundary.

### 4. Implementation Requirements

- Build a versioned campaign evidence envelope from objectives, audience quality summaries, approved product/policy sources, activity plan, execution results, attribution, costs, KPIs, and prior comparable campaigns.
- Implement Alex capabilities for:
  - objective and target-range suggestions;
  - audience quality and missing-evidence review;
  - activity and timing recommendations;
  - source-backed message drafting;
  - experiment design and interpretation;
  - delivery, reply, conversion, and cost anomaly explanation;
  - pause, continue, stop, or adjust recommendations;
  - post-campaign review and bounded reusable-memory proposals.
- Keep objective progress, eligibility, metric calculation, and recommendation ranking inputs deterministic. AI may explain but not alter authoritative values.
- Require structured output with source references, confidence, missing evidence, expected impact, risk, dependencies, and review requirement.
- Validate product claims, prices, language, communication action, and recommendation intents before persistence or task creation.
- Convert accepted recommendations into reviewed campaign updates, tasks, plans, approvals, or handoffs through existing commands with version revalidation and idempotency.
- Persist durable reasoning runs, feedback, accepted/rejected recommendations, quality events, and safe audit summaries.
- Add plain-language unavailable states for insufficient evidence, missing products, missing provider, stale data, or policy restrictions.
- Add APIs/API client for analysis request, run status, recommendation detail, evidence drill-down, feedback, and accepted action.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- No direct OpenAI/provider call outside the shared reasoning gateway.
- AI cannot add a person to an audience, grant consent, launch, send, change price, claim product capability, book accounting values, or assert causal attribution.
- Raw emails, credentials, hidden prompts, and unnecessary personal data must not enter logs or audit summaries.
- Recommendations must become stale when authoritative campaign data changes.

### 6. Acceptance Criteria

- Given an audience containing suppressed contacts, when Alex analyzes it, then the deterministic suppression remains unchanged and the issue is explained.
- Given an unsupported product claim in model output, when validated, then it is rejected before a draft or task is created.
- Given insufficient comparable history, when target advice is requested, then Alex reports the evidence gap rather than inventing a benchmark.
- Given an accepted recommendation after the campaign version changed, then commit requires refreshed analysis.
- Given a campaign result with correlation-only evidence, then Alex cannot describe it as proven causation.

### 7. Verification

- Unit-test evidence mapping, freshness, source validation, structured output, policy filtering, and stale commit.
- Add fake-provider tests for success, malformed output, unsupported citations, invented values, timeout, cancellation, and unavailable AI.
- Add integration tests for recommendation-to-task/plan/approval/handoff flows, authorization, audit, quality events, and tenant isolation.
- Extend Sales AI campaign optimization tests and verify deterministic metrics cannot be changed by model output.

### 8. Definition Of Done

Alex provides useful, reviewable campaign intelligence grounded in authoritative campaign and company evidence. Every accepted action uses governed application boundaries, and unsupported or stale output cannot affect production state.

---

## Prompt 8: Deliver The Production Campaigns Experience

### 1. Title And Outcome

Redesign the Sales Campaigns experience into a production-grade operational surface for planning, approving, running, and reviewing B2B and B2C campaigns.

### 2. Current Context

- `/app/sales/campaigns` currently lists outbound campaigns, shows high-level email execution counts, supports campaign lifecycle actions, and includes a sequence-centric creation form.
- Sales navigation is organized around Overview, Prospects, Pipeline, and Campaigns.
- The current campaign page uses the existing Sales design system but does not expose the complete initiative, objective, schedule, activity, audience, budget, attribution, or review model delivered by Prompts 1-7.
- `/docs/design.md` defines a calm operational product, and `ui-instructions.md` requires screenshot-first design for this significant redesign.
- Alex must remain visible as the Sales agent responsible for recommendations and campaign work.

### 3. Dependencies

- Prompts 1-7.
- Real campaign data and APIs from those prompts.

### 4. Implementation Requirements

- Before implementation, write an explicit screenshot prompt and generate `/docs/design/references/sales-campaigns-reference.png` using the approved image generation workflow. Implement against it; do not ship the image.
- Build a responsive Campaigns experience with:
  - lifecycle and attention filters;
  - objective progress, owner, schedule, audience, channel, budget, and status in the list;
  - a list/details interaction that preserves company and campaign URL state;
  - plain-language empty, loading, unavailable, restricted, failed, and stale states.
- Provide campaign detail sections or tabs for:
  - Overview;
  - Objective and offer;
  - Audience and exclusions;
  - Activity plan;
  - Content and approvals;
  - Performance and attribution;
  - Alex's recommendations;
  - Post-campaign review.
- Implement a guided create/edit flow:
  - purpose and objective;
  - B2B/B2C audience and preview;
  - products/offer;
  - dates and budget;
  - activities and channels;
  - governance review;
  - approval and scheduling.
- Make missing access or configuration explicit in plain language and link to the actual relevant configuration screen.
- Clearly distinguish executable channels from tracked-only activities.
- Show approval work as Waiting for human approval and Alex's active preparation as Ongoing in linked Agent Team tasks.
- Require impact previews and confirmations for launch, pause, stop, audience refresh, reschedule, and other material changes.
- Add evidence drill-down for attribution, KPI, AI recommendations, exclusions, and execution failures.
- Use real API data only; do not add demo campaign rows or placeholder metrics.
- Maintain localization through existing resource/catalog patterns. Do not hard-code raw enums or internal identifiers.
- Preserve keyboard navigation, focus states, semantic labels, responsive layouts, stable dimensions, and non-overlapping text.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Follow `/docs/design.md`; do not introduce a separate marketing visual style.
- Do not put cards inside cards or turn the page into a passive chart dashboard.
- Do not expose provider secrets, raw prompts, technical execution names, or unsupported channels as connected.
- Lifecycle buttons must reflect backend authorization and effective policy, not only client-side state.
- The existing Sales Overview, Prospects, Pipeline, deal, and contact routes must remain coherent and retain company context.

### 6. Acceptance Criteria

- Given a draft B2B campaign, when opened, then the operator can see its objective, target accounts, buying roles, planned activities, dates, budget, and missing readiness items.
- Given a running B2C campaign, when opened, then the operator can see consent-aware audience counts, next due activity, delivery and conversion results, cost, and objective progress.
- Given a campaign waiting for approval, then the exact decision and its impact are visible and linked to the approval surface.
- Given a tracked-only social activity, then the UI shows an assigned task and never claims it was published automatically.
- Given missing or unavailable data, then no fabricated zero, comparison, or result is shown.
- Given desktop and mobile viewports, then controls, text, tables, timelines, and detail panels remain readable without overlap.

### 7. Verification

- Add component tests for list/detail URL state, guided creation, audience preview, readiness, lifecycle actions, approvals, evidence, localization, and every non-happy state.
- Add API-client contract tests for all new campaign endpoints.
- Run authorization and tenant-isolation UI integration tests.
- Use Playwright to verify the Campaigns list, B2B draft, B2C running campaign, approval state, failure state, and empty state at desktop and mobile viewports.
- Capture and inspect screenshots against the saved reference, verify no overlap, and check browser console/network errors.
- Build and test Web without launching an untracked detached host.

### 8. Definition Of Done

Campaigns is a complete operational surface for governed B2B and B2C planning, execution, attention, and review. It uses production APIs and real data, matches the established design system, and leaves no sequence-only UI, mock production data, inaccessible actions, or unhandled state in scope.

