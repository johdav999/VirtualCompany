# Sales Agent AI Implementation Prompts

## Purpose And Execution Order

This pack implements Alex's role-specific AI advice, analysis, and orchestration from `agents-ai.md`. Execute in order and extend the shared AI platform rather than creating a Sales-only model stack.

1. Shared-reasoning migration and Sales evidence
2. Lead and account intelligence
3. Next-best-action recommendations
4. Deal risk and close planning
5. Forecast scenarios and pipeline narrative
6. Campaign and message optimization
7. Governed proposal and commercial recommendations
8. Sales operating cadence and manager cockpit

## Instructions For Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `agents-ai.md`, `architecture-inst.md` when present, and `/docs/architecture-rules.md`. UI work must also follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements.
- Use the shared capability catalog, `IAgentReasoningGateway`, orchestration runs, grounded knowledge, plans, handoffs, memory candidates, quality events, tasks, workflows, approvals, audit, and background execution.
- CRM records, product catalog, prices, consent, commercial policy, pipeline stages, and provider outcomes remain authoritative. AI may explain and recommend but cannot invent customer facts, product capabilities, terms, or commitments.
- External outreach and provider writes require communication policy, approval where configured, durable idempotent execution, bounded retries, reconciliation, and visible failure.
- Company authorization and tenant isolation apply to every source, recommendation, task, metric, and handoff. Schema changes require EF migrations and equivalent local/Docker SQL Server compatibility.
- Implement production behavior without mock production data, direct feature-level provider calls, silent fallback, or deferred in-scope TODOs.

---

## Prompt 1: Migrate Sales AI To Shared Reasoning And Publish Sales Evidence

### 1. Title And Outcome

Move Sales AI interpretation onto the shared reasoning boundary and implement a versioned Sales evidence adapter so all later recommendations are grounded, validated, persisted, and measurable.

### 2. Current Context

- `OpenAiSalesEmailIntentExtractionService` calls OpenAI directly and is registered by `AddSalesModule`.
- Shared reasoning and durable runs exist, while Sales contracts cover email ingestion, sources, lead generation, outbound automation, campaigns, conversion analytics, and revenue forecasts.
- Sales persistence contains leads, contacts, customer companies, deals, stages, activities, signals, campaign/sequence execution, approvals, forecasts, and source attribution.
- There is no common Sales evidence envelope or Sales-specific output validator over the shared gateway.

### 3. Dependencies

- Shared AI implementation from `shared-ai.md`.

### 4. Implementation Requirements

- Define Sales evidence and recommendation contracts with source IDs, provenance, observed/as-of time, freshness, confidence, consent/communication state, and authoritative record version.
- Implement adapters for lead/contact/account/deal/activity/email/signal/campaign/forecast/product-policy sources through existing application/repository boundaries.
- Create a Sales reasoning facade over `IAgentReasoningGateway` with schemas for classification, recommendation, analysis, and plan outputs.
- Refactor email-intent extraction to the shared gateway while preserving `ISalesEmailIntentExtractionService` behavior, deterministic fallback, routes, and ingestion idempotency.
- Validate classifications, source citations, confidence, permitted action intents, stage values, and communication actions against Sales policy.
- Register stable Sales capability IDs and effective requirements in the shared catalog.
- Persist run/audit/quality evidence without raw emails, hidden prompts, credentials, or full provider responses.
- Add fake-provider tests and authorized run/detail access required by Sales UI.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not move Sales business rules into shared Infrastructure or controllers.
- AI cannot mutate CRM state or send messages in this prompt.
- Preserve current email ingestion deduplication and deterministic classifier fallback.

### 6. Acceptance Criteria

- Given a Sales email, when intent extraction succeeds, then the result comes through the shared gateway with validated sources and a durable run.
- Given unavailable AI, then the existing safe deterministic fallback is used and no confident unsupported result is recorded.
- Given an inaccessible source, then it is absent from context and output.
- Given an unsupported action intent, then validation blocks it before any Sales command executes.

### 7. Verification

- Unit-test evidence mapping, source/freshness validation, intent schemas, fallback, and action filtering.
- Add fake-provider success, malformed output, timeout, unsupported citation, and cancellation tests.
- Extend Sales email ingestion, shared AI, audit, authorization, and tenant-isolation tests.
- Build Application, Infrastructure, API, and Web.

### 8. Definition Of Done

Sales AI uses one shared, governed provider boundary and one authoritative evidence model. No direct Sales provider bypass, unsupported citations, or behavior regression remains.

---

## Prompt 2: Implement Lead And Account Intelligence Briefs

### 1. Title And Outcome

Implement source-backed lead and account intelligence that explains fit, intent, buying roles, timing, relevant products, missing evidence, and recommended research questions.

### 2. Current Context

- Lead generation supports ICP profiles, source policies, prospecting runs, provider provenance, scoring, research-brief JSON, review, and CRM adaptation.
- Sales sources and first-party/external provider boundaries already control provenance, geography, fields, budget, freshness, and retention.
- Current scoring is deterministic but research summaries and account context are not consistently grounded through shared AI.

### 3. Dependencies

- Prompt 1.
- Active ICP and source policy; missing configuration is an explicit unavailable state.

### 4. Implementation Requirements

- Build a deterministic lead/account intelligence snapshot from ICP criteria, source observations, contacts, engagement, activities, signals, product catalog, policies, and known data gaps.
- Keep fit/timing/role/data-confidence scoring authoritative; AI explains criteria and synthesizes a bounded brief.
- Return confirmed facts, hypotheses, unknowns, buyer-role map, pain hypotheses, approved product relevance, disqualifiers, research questions, source links, and freshness.
- Never treat scraped/external claims as verified first-party facts; preserve provider attribution and observation time.
- Add contradiction detection for company identity, size, geography, role, consent, and duplicate accounts.
- Allow explicit review to create follow-up research tasks or update reversible internal review state through existing commands.
- Add API/client and integrate briefs into prospect/account detail surfaces with source drill-down and safe empty states.
- Capture review corrections and accepted/rejected recommendations for quality metrics.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not bypass source policy, cost approval, retention, consent, or CRM adapters.
- AI cannot invent contact details, financials, technologies, needs, or product fit.
- Existing deterministic score remains unchanged unless separately changed by policy code and tests.

### 6. Acceptance Criteria

- Given conflicting provider observations, then the brief displays the conflict and does not select a fact without authoritative evidence.
- Given a disqualifying ICP rule, then AI cannot promote the prospect to qualified.
- Given stale external data, then freshness and reduced confidence are visible.
- Given another company's prospect ID, then no intelligence is disclosed.

### 7. Verification

- Unit-test source classification, contradiction/freshness rules, score preservation, product-policy grounding, and confidence thresholds.
- Add integration tests for sparse, rich, conflicting, stale, disqualified, and duplicate prospects plus tenant isolation.
- Extend lead generation, sales source, product knowledge, API client, and UI tests.
- Perform screenshot verification if the detail surface is substantially redesigned.

### 8. Definition Of Done

Alex can inspect a trustworthy lead/account brief whose claims are attributable, current, and clearly separated from hypotheses. Research tasks are reviewable and governed.

---

## Prompt 3: Implement Deterministic-First Next-Best Actions

### 1. Title And Outcome

Implement ranked next-best actions for leads, contacts, accounts, and deals using deterministic eligibility and urgency followed by AI explanation and personalization.

### 2. Current Context

- Sales operations, stages, activities, sequence execution, reply signals, automation policies, approvals, tasks, and shared work prioritization already exist.
- Current workflows create follow-ups but no unified recommendation contract explains impact, timing, dependencies, communication permission, and evidence.

### 3. Dependencies

- Prompts 1-2.

### 4. Implementation Requirements

- Define Sales action candidates for research, qualification, discovery, stakeholder mapping, follow-up, stage review, proposal preparation, handoff, and no-action/review.
- Determine eligibility, consent, quiet periods, sequence state, stale thresholds, stage requirements, approval, and due dates in deterministic policy.
- Rank by explicit score components such as urgency, deal impact, engagement, risk, due state, and confidence; AI may explain but not change the score or eligibility.
- Return action, owner, timing, rationale, evidence, dependencies, confidence, draft requirement, approval state, and safe alternative.
- Permit reviewed creation of existing tasks or sequence plans with state/version revalidation and stable idempotency.
- Suppress duplicate or conflicting actions already represented by open tasks, running sequences, approvals, or handoffs.
- Add API/client and prioritized action queue in Sales surfaces.
- Link human feedback and downstream outcomes without claiming AI causality.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- No outbound send or stage mutation occurs directly from recommendation output.
- Preserve Sales automation, consent, approval, task, and sequence systems of record.
- Do not use opaque model ranking as the authoritative priority.

### 6. Acceptance Criteria

- Given a contact without communication permission, then outreach is ineligible regardless of AI output.
- Given an existing open follow-up, then duplicate recommendation commit creates no second task.
- Given a stale recommendation after stage change, then commit requires refresh.
- Given equal deterministic scores, then stable tie-breaking produces repeatable order.

### 7. Verification

- Unit-test candidate generation, score components, consent, duplicate suppression, conflict checks, and stale commit.
- Add integration tests across leads, deals, sequences, tasks, approvals, and cross-tenant access.
- Add fake-provider tests proving score/action eligibility cannot be altered.
- Add Web tests for prioritization reasons, disabled actions, refresh conflicts, and empty/error states.

### 8. Definition Of Done

Alex has a repeatable, explainable Sales action queue whose reviewed actions use existing durable workflows and cannot bypass communication or commercial policy.

---

## Prompt 4: Implement Deal Risk, Strategy, And Mutual Action Plans

### 1. Title And Outcome

Implement evidence-backed deal-risk analysis and bounded close planning covering qualification, stakeholders, inactivity, competition, pricing friction, support blockers, and payment risk.

### 2. Current Context

- Deals, pipeline stages, activities, deal intelligence signals/sources, risk snapshots, forecast snapshots, approvals, tasks, and typed handoffs already exist.
- Existing signal services detect risk, but operators lack one validated analysis that separates facts from hypotheses and converts reviewed strategy into durable tasks.

### 3. Dependencies

- Prompts 1-3.
- Shared bounded planning and handoffs.

### 4. Implementation Requirements

- Define deterministic risk features and stage-entry/exit requirements using current stage, age, activity, contacts, stakeholders, amount, probability, source evidence, approvals, and linked Support/Finance handoffs.
- Keep risk score and stage eligibility deterministic. AI explains drivers, proposes discovery questions, mitigations, and stakeholder strategy.
- Return facts, risks, hypotheses, missing qualification, decision dependencies, confidence, and permitted next actions.
- Generate a bounded mutual action plan through shared planning with owners, outcomes, dates, dependencies, evidence, and approval markers.
- Commit reviewed plans to existing tasks only; do not automatically change stage, probability, amount, or terms.
- Use typed minimum-evidence handoffs to Finance for invoice/payment readiness and Support for customer blockers.
- Version analysis against deal/activity/signal state and mark it stale after material changes.
- Add API/client and deal-detail presentation with source drill-down and plan review.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not infer competitor presence, stakeholder authority, customer intent, or commitment without evidence.
- Handoffs grant no new access and cannot replace domain workflows.
- Preserve stage, risk, task, approval, and forecast authorities.

### 6. Acceptance Criteria

- Given missing required qualification, then risk is visible and stage advancement is not recommended as eligible.
- Given an unsupported competitor claim, then it is labeled unknown/hypothesis rather than fact.
- Given reviewed plan commit twice, then tasks are created once.
- Given material deal change, then previous analysis is stale and cannot drive execution.

### 7. Verification

- Unit-test risk features, stage rules, source labeling, plan bounds, stale detection, and handoff evidence minimization.
- Add integration tests for healthy/stale/blocked deals, missing stakeholders, payment risk, support blockers, duplicate plans, and tenant isolation.
- Extend deal signal, forecast, planning, task, handoff, API, and Web tests.
- Perform browser verification for significant detail changes.

### 8. Definition Of Done

Alex can explain deal risk and prepare a reviewable mutual action plan without inventing customer facts or mutating commercial records outside existing commands.

---

## Prompt 5: Implement Pipeline Forecast Scenarios And Narrative

### 1. Title And Outcome

Implement commit, best-case, and downside Sales forecast analysis with deterministic totals, uncertainty, slippage, coverage, and source-linked narrative.

### 2. Current Context

- `RevenueForecastService`, forecast snapshots, pipeline stages, deal probabilities, risk signals, conversion analytics, and Finance revenue context already exist.
- Forecast values exist, but scenario assumptions, changes, concentration, and uncertainty are not consistently explained through shared AI.

### 3. Dependencies

- Prompts 1 and 4.

### 4. Implementation Requirements

- Define deterministic scenario inclusion, amount, currency, period, probability, stage, slippage, coverage, and confidence-band calculations.
- Snapshot inputs and retain source deal/version IDs so forecasts are reproducible and stale changes detectable.
- Use AI to explain movements, concentration, risk drivers, assumptions, and management questions; model output cannot alter totals.
- Return commit/best/downside values, coverage, changes from prior snapshot, top contributions/risks, fact/inference/unknown claims, and source links.
- Integrate permitted Finance context such as payment or margin risk through scoped contracts/handoffs, not direct Finance queries.
- Add forecast-review feedback and carefully separated correlation/outcome metrics.
- Expose authorized API/client and pipeline/forecast UI with scenario comparison and source drill-down.
- Add scheduled snapshot/briefing contribution using idempotent background execution.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- AI never computes forecast totals or changes deal amount/probability/stage.
- Currency conversion must use authoritative configured data or stay separated by currency.
- Do not claim causal forecast accuracy from small samples.

### 6. Acceptance Criteria

- Given unchanged inputs, then repeated forecasts produce identical deterministic totals.
- Given a slipped deal, then scenario movement and source deal are visible.
- Given mixed currencies without conversion, then totals remain separated and are not falsely aggregated.
- Given a later deal update, then the prior snapshot remains reproducible and is marked historical/stale.

### 7. Verification

- Unit-test scenario formulas, currency handling, coverage, slippage, snapshot identity, and narrative source validation.
- Add integration tests for empty/sparse/healthy/concentrated pipelines, stage changes, mixed currency, and tenant isolation.
- Extend revenue forecast, conversion analytics, briefing, quality, API, and Web tests.
- Add UI tests for scenario comparison, small sample, stale snapshot, and safe failures.

### 8. Definition Of Done

Sales forecasts are deterministic, reproducible, and accompanied by an evidence-backed narrative that exposes assumptions and uncertainty without model-derived numbers.

---

## Prompt 6: Implement Campaign And Message Optimization Advice

### 1. Title And Outcome

Implement evidence-based campaign, sequence, channel, timing, and message recommendations from conversion analytics while preserving consent, attribution limits, and approval-backed delivery.

### 2. Current Context

- Campaigns, sequences, steps, executions, reply signals, outbound automation policy, message performance events, variants, attribution, and analytics dashboards already exist.
- Existing analytics expose funnels and rates; there is no governed AI interpretation with sample-size controls and reviewable experiments.

### 3. Dependencies

- Prompts 1 and 3.

### 4. Implementation Requirements

- Define deterministic experiment eligibility, minimum sample, confidence/uncertainty, consent, suppression, channel, spend, and approval policies.
- Build bounded campaign evidence with delivered/replied/qualified/conversion outcomes, attribution caveats, segment/variant/step performance, and time windows.
- Use AI to explain patterns and propose hypotheses, audience refinements, sequence changes, or A/B experiments; label correlation and avoid causal claims without evidence.
- Return recommendation, affected segment/step, expected measurement, sample caveat, policy state, source IDs, and review requirement.
- Permit reviewed creation of draft campaign/sequence versions through existing services; never edit active history or send automatically.
- Require existing approval and outbound execution paths for activation/delivery, with idempotency and provider reconciliation.
- Add API/client and optimization view inside campaign analytics, including accept/reject/correct feedback.
- Record experiment outcomes against exact recommendation/version.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Do not infer consent, fabricate performance, expose recipient content, or optimize toward prohibited/sensitive segments.
- Preserve immutable message-performance history and attribution semantics.
- Small samples must show insufficient evidence rather than a confident recommendation.

### 6. Acceptance Criteria

- Given insufficient sample, then no confident winner or autonomy recommendation is produced.
- Given an active campaign, then AI cannot directly alter or send it.
- Given a reviewed experiment draft, then its measurement and source recommendation are linked.
- Given duplicate activation delivery, then provider actions remain idempotent.

### 7. Verification

- Unit-test sample thresholds, attribution caveats, consent/suppression, experiment schemas, and draft versioning.
- Add integration tests across campaigns, sequences, approvals, delivery, reply signals, outcomes, and tenant isolation.
- Add fake-provider tests for unsupported conclusions and prohibited segment suggestions.
- Add Web tests for small-sample, hypothesis, draft-review, and outcome states.

### 8. Definition Of Done

Alex can propose measurable, policy-compliant campaign improvements from real evidence. Changes remain drafts until reviewed and existing outbound controls remain authoritative.

---

## Prompt 7: Implement Governed Product, Proposal, Pricing, And Terms Advice

### 1. Title And Outcome

Implement grounded product positioning and proposal recommendations that use approved catalog and policy content and route discounts or nonstandard terms through explicit approval.

### 2. Current Context

- Company knowledge documents include product, policy, FAQ, and agent briefs; Sales owns deals and approvals but lacks a dedicated governed proposal recommendation flow.
- Existing outbound drafting and shared grounded Q&A can retrieve knowledge, while product price and commercial commitments must remain authoritative.

### 3. Dependencies

- Prompts 1-4.
- Processed/indexed product catalog and commercial policy sources.

### 4. Implementation Requirements

- Define a proposal evidence contract combining deal facts, customer requirements explicitly on record, approved product catalog, current price/version, commercial policy, legal templates, and missing information.
- Generate source-backed product/package relevance, positioning, discovery questions, exclusions, draft scope, and assumptions.
- Validate every capability statement and price/term against authoritative sources; unsupported claims are removed and require review.
- Implement deterministic discount, term, currency, tax-context, validity-period, and approval-threshold policies.
- Produce a reviewable proposal/quote draft or commercial request, not a binding contract or provider write.
- Route nonstandard discounts/terms to existing approval workflow with exact requested values and policy reason; recheck approval before any later external action.
- Version drafts against catalog, policy, deal, and approval state and mark stale changes.
- Add API/client and deal proposal workspace with citations, assumptions, exclusions, approval, and revision feedback.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- AI cannot invent features, delivery dates, warranties, prices, discounts, tax treatment, or legal terms.
- Do not use general web knowledge as authoritative product evidence.
- No customer send occurs in this prompt; existing communication/outbox policy applies later.

### 6. Acceptance Criteria

- Given a requested unsupported capability, then the draft labels it unavailable/unknown and does not promise it.
- Given a discount above threshold, then approval is required and cannot be bypassed by draft editing.
- Given catalog or price change, then the prior draft is stale before send/acceptance.
- Given missing customer requirements, then assumptions are visible and review is required.

### 7. Verification

- Unit-test source coverage, capability/price validation, discount/term policy, stale versions, and approval mapping.
- Add integration tests for standard/nonstandard proposals, unsupported capabilities, catalog changes, approval outcomes, and tenant isolation.
- Add knowledge access, audit, API client, and Web tests.
- Perform screenshot-first/browser verification for a new proposal workspace.

### 8. Definition Of Done

Alex prepares grounded, reviewable commercial drafts and requests approvals for exceptions. Unsupported promises and unapproved commercial commitments cannot reach customers.

---

## Prompt 8: Implement Alex's Governed Operating Cadence And Sales Cockpit

### 1. Title And Outcome

Implement Alex's daily and weekly Sales cadence and consolidate priorities, deal risks, forecast changes, campaigns, approvals, handoffs, and quality evidence into the Sales manager cockpit.

### 2. Current Context

- Shared briefing scheduling, prioritization, planning, handoffs, memory, and quality exist.
- Sales has prospecting and sequence workers, source policies, campaigns, operations, forecasts, analytics dashboards, and lead/deal pages.
- Role-specific AI outputs are not yet coordinated through one idempotent cadence or actionable cockpit.

### 3. Dependencies

- Prompts 1-7 and shared AI prompts 4-10.

### 4. Implementation Requirements

- Define versioned daily/weekly cadence manifests with deterministic prerequisites, company batching, idempotency windows, and explicit outputs.
- Daily: new/high-priority leads, overdue follow-ups, reply signals, stale/at-risk deals, approvals, handoffs, and failed outbound/provider work.
- Weekly: pipeline/forecast scenarios, deal strategy reviews, campaign optimization, source quality/cost, win/loss learning, and cross-functional dependencies.
- Orchestrate existing services and prompt capabilities through background execution without duplicate tasks, insights, briefs, or handoffs.
- Implement typed won-deal Finance handoffs, Support blocker/retention handoffs, and Finance payment-risk intake using minimum evidence.
- Build/extend the Sales cockpit with current priorities, recommendations, source evidence, forecast, campaign experiments, approvals, handoffs, integration health, outcomes, and quality sample size.
- Surface configuration, stale data, policy blocks, provider failures, reconciliation ambiguity, and approval waits plainly.
- Allow feedback and autonomy-review recommendation only; never change autonomy automatically.
- Audit material outcomes and collect safe technical metrics.

### 5. Constraints And Preservation Rules

- Follow the pack preamble.
- Scheduled work cannot send outreach, make commitments, change deal commercial state, or write providers without existing checks.
- Register capability services/workers in `AddSalesModule` and avoid duplicate hosted service registrations.
- Use screenshot-first workflow for substantial cockpit changes.

### 6. Acceptance Criteria

- Given duplicate cadence delivery, then one logical output set exists.
- Given missing AI credentials, deterministic Sales processing continues and AI-dependent states are actionable.
- Given a won deal, then one scoped Finance handoff is created with required invoice-readiness facts.
- Given insufficient quality evidence, then no autonomy increase occurs.

### 7. Verification

- Unit-test cadence selection, idempotency, prerequisites, handoff schemas, and briefing mapping.
- Add retry/concurrency, approval preservation, outbound safety, audit, and cross-tenant integration tests.
- Add cockpit API projection and Web tests for loading, empty, stale, blocked, failure, and drill-down states.
- Run Sales, shared-AI, briefing, workflow, approval, support/finance handoff, API, and Web suites plus responsive browser checks.

### 8. Definition Of Done

Alex operates a durable Sales cadence and a trustworthy action cockpit. Recommendations are grounded and measurable, cross-agent work is scoped, and all external/commercial effects remain governed.
