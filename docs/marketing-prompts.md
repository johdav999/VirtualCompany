# Marketing Agent Implementation Prompts

## Purpose And Execution Order

This prompt pack implements the Marketing Agent described in `/docs/marketing.md`. Execute the prompts in order. Each prompt must deliver a production-usable increment and reuse existing agent, campaign, task, approval, document, integration, and AI orchestration boundaries.

1. Marketing Agent onboarding, governance, and access
2. Marketing objectives, plans, and operating calendar
3. Brand-governed content briefs, assets, variants, and approvals
4. Audience intelligence and qualified-demand criteria
5. Campaign collaboration and marketing-to-sales handoffs
6. Channel observations, performance, attribution, costs, and experiments
7. Maya's governed marketing intelligence
8. Production Marketing workspace and team integration

## Shared Instructions For Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `/docs/marketing.md`, `/docs/campaign.md`, `/docs/shared-agent-ai.md`, `/docs/architecture-rules.md`, and `/docs/architecture-overview.md` as background.
- Follow `architecture-inst.md` if it exists. Do not treat its absence as permission to ignore `/docs/architecture-rules.md`.
- For UI work, also read and follow `ui-instructions.md` and `/docs/design.md`, including the mandatory screenshot-first workflow.
- Inspect the current repository before editing. Existing implementation wins over older planning documents.
- Extend the existing `marketing` agent template, company-agent model, capability catalog, role briefs, company knowledge, trusted tool registry, tasks, workflows, approvals, campaign initiative model, Sales records, Finance summaries, Support signals, and shared AI orchestration. Do not create parallel subsystems.
- Preserve company authorization, explicit `CompanyId` filtering, tenant isolation, consent, suppression, communication-language resolution, approval, audit, idempotency, retry, reconciliation, optimistic concurrency, and operator-visible failures.
- Marketing recommendations and AI drafts are non-authoritative. Deterministic policies and owning modules remain authoritative for audience eligibility, consent, pipeline state, support state, prices, booked finance values, claims, attribution evidence, and external side effects.
- External side effects must use approved tools/providers and durable background execution. Never invoke a provider directly from a controller, Blazor component, or model response.
- Schema changes require an EF migration in `VirtualCompany.Persistence.Migrations` and equivalent local SQL Server and Docker SQL Server restore/run compatibility.
- Do not add mock production data, silent fallback, hidden credentials, unhandled intermediate states, or deferred in-scope TODOs.

---

## Prompt 1: Onboard And Govern The Marketing Agent

### 1. Title And Outcome

Activate the existing Marketing Manager template as a governed company agent with role briefs, capabilities, access scopes, configuration status, and safe default autonomy. This makes Maya a real member of the agent team without granting unconfigured provider access.

### 2. Current Context

- `AgentTemplate` already contains an active `marketing` template for a Marketing Manager with goals, KPIs, tool categories, data scopes, budget policy, and escalation metadata.
- `CoreCompanyAgentSeeder` currently adds Laura, Alex, and Ben, but does not add the Marketing template.
- Agent Management already supports active company agents, role briefs, uploaded documents, data scopes, trusted tools, mailbox access, autonomy, capability availability, and configuration guidance.
- The Agent Team board currently renders Finance, Sales, and Support work.
- Shared Agent AI capability registration is code-owned and availability is derived from effective tools, data scopes, autonomy, and configuration.
- There is no implemented Marketing role decision service, Marketing workspace, or Marketing-specific capability manifest.

### 3. Dependencies

None.

### 4. Implementation Requirements

- Extend the core company-agent onboarding/backfill flow to add the existing `marketing` template idempotently for new and existing companies.
- Use a configurable default display name, with `Maya` as the product default and `Marketing Manager` as the role. Do not hard-code identity in domain logic.
- Define Marketing role metadata, default Guided autonomy, primary goals, escalation rules, and plain-language capability descriptions.
- Add Marketing brief categories for company, products and services, policies, brand and positioning, audiences, and other marketing instructions using the existing briefing and indexed-document model.
- Add recommended Marketing data scopes:
  - approved company and product knowledge;
  - campaigns, audiences, activities, and performance;
  - permitted Sales summaries;
  - permitted Support suppression and trend signals;
  - Finance-approved budget and campaign-cost summaries;
  - marketing mailbox and web enquiries.
- Register initial Marketing capabilities as unavailable, restricted, approval-required, or available based on real implementation and configuration. Do not mark provider execution available before a trusted tool exists.
- Add a `marketing` mailbox purpose only if the current mailbox-purpose model can support it without duplicating transport code. Otherwise, implement the typed purpose through the existing generalized mailbox connection model.
- Add exact configuration links for missing mailbox, document, data-scope, analytics, CMS, or publishing access.
- Ensure credentials remain in provider settings or the secret store and never enter agent profiles or briefs.
- Add auditable company backfill and idempotency behavior. Preserve custom agent edits when a Marketing agent already exists.
- Update architecture and operator documentation where the new role changes the core-agent operating model.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not change the behavior or identity of Laura, Alex, or Ben.
- Do not seed demo tasks, campaigns, content, KPI values, or provider connections.
- Marketing must not inherit unrestricted Sales, Finance, or Support data.
- Backfill must be repeatable and company isolated.
- Capability messages must use plain language and route to the exact configuration surface.

### 6. Acceptance Criteria

- Given a new company, when core agents are onboarded, then exactly one active Marketing Manager is created from the existing `marketing` template.
- Given an existing company without Marketing, when backfill runs twice, then exactly one Marketing agent exists and no other agent is duplicated.
- Given an existing customized Marketing agent, when backfill runs, then its name, autonomy, access, and brief are not overwritten.
- Given a Marketing capability requiring a mailbox or provider, when it is not configured, then the UI states what is missing and links to the relevant configuration screen.
- Given a user from another company, when Marketing profile or access data is requested, then no data is disclosed.

### 7. Verification

- Add domain/service tests for idempotent onboarding and preservation of custom settings.
- Add integration tests for new-company creation, existing-company backfill, authorization, and tenant isolation.
- Add capability-catalog tests for each Marketing capability state and configuration link.
- Add Agent Management and Agent Team presentation tests, including English and Swedish localization.
- Build Operations Infrastructure, Persistence, API, Web, and affected test projects.

### 8. Definition Of Done

Marketing is an onboarded, configurable, tenant-isolated agent with safe default autonomy, real role briefs and access controls, accurate capability states, and no fabricated provider availability or production data.

---

## Prompt 2: Implement Marketing Objectives, Plans, And Calendar

### 1. Title And Outcome

Give Maya and human marketers a governed operating plan that turns business objectives into dated, owned, measurable marketing work using existing campaigns, activities, tasks, and approvals.

### 2. Current Context

- Campaign initiatives now store objectives, ownership, schedules, budgets, offers, audience snapshots, activities, lifecycle, and KPI definitions.
- Campaign activities support manual work, governed handoffs, and provider-backed execution.
- Agent Team displays durable work by lifecycle stage.
- Shared AI planning produces proposals but only authorized commit creates durable tasks.
- No Marketing-specific operating plan connects annual or quarterly marketing objectives to campaigns, content work, channel activity, and review cadence.

### 3. Dependencies

- Prompt 1.

### 4. Implementation Requirements

- Add company-owned Marketing objectives with:
  - name, description, type, target, unit, period, baseline, owner, status, and source evidence;
  - links to related campaign objectives and KPI definitions;
  - optimistic concurrency and lifecycle history.
- Add a Marketing plan for a bounded period with priorities, planned budget references, assumptions, risks, review cadence, and objective links.
- Reuse campaign activities and company tasks for executable work. Add only the minimum normalized entities needed for plan grouping and calendar views.
- Implement a unified Marketing calendar read model containing:
  - campaign activities;
  - content preparation and review tasks;
  - launches and milestones;
  - marketing-to-sales handoffs;
  - recurring review work;
  - manual tracked events.
- Keep source timezone and UTC values explicit. Apply company quiet hours and provider windows only through existing policy boundaries.
- Allow Maya to prepare a plan proposal with sources, missing evidence, alternatives, and requested actions. Committing the plan requires authorization and creates idempotent durable work.
- Add APIs and typed Web client methods for objectives, plans, calendar periods, plan proposal, and commit.
- Emit audit and Agent Team events for plan creation, material change, blocked work, and approval.
- Add migration and indexes for company/period/status queries if new persistence is required.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not duplicate campaign lifecycle or activity scheduling.
- A Marketing objective cannot overwrite a Sales campaign objective or Finance budget.
- Date changes that affect approved or scheduled work require version checking and approval according to policy.
- A missing data source must be shown as unavailable, not zero.
- Plan commit creates internal durable records only; it does not launch or publish anything.

### 6. Acceptance Criteria

- Given a business objective and permitted evidence, when Maya prepares a plan, then the proposal contains objectives, activities, owners, dates, dependencies, KPIs, risks, and missing evidence.
- Given an uncommitted proposal, when it is viewed, then no tasks, campaign activities, or provider actions have been created.
- Given an authorized user committing the same proposal twice, then durable work is created exactly once.
- Given a Marketing calendar period, when it is requested, then related campaign activities and tasks appear once with correct owner, status, timezone, and source link.
- Given a stale plan version, when an update is submitted, then it fails with a refresh-required conflict.

### 7. Verification

- Unit-test objective and plan invariants, period handling, lifecycle, and concurrency.
- Integration-test proposal/commit idempotency, calendar aggregation, audit, authorization, and tenant isolation.
- Test Agent Team projection for planned, ongoing, approval, and completed Marketing work.
- Add migration and local/Docker SQL Server compatibility checks where schema changes occur.
- Build Domain, Application, Operations/Sales Infrastructure, API, Web, and tests.

### 8. Definition Of Done

Marketing objectives and plans are measurable, period-bound, auditable, and connected to existing campaign and task execution. The calendar reflects real durable work without triggering external actions.

---

## Prompt 3: Build Brand-Governed Content Operations

### 1. Title And Outcome

Implement a source-backed content workflow for briefs, drafts, variants, review, approval, and outcome tracking so Maya can accelerate content without inventing product claims or publishing unreviewed material.

### 2. Current Context

- Company briefing supports typed text and indexed documents.
- Company knowledge retrieval can ground AI output in approved product, policy, FAQ, and uploaded sources.
- Campaign offers and activities can reference content or provider requirements.
- Existing support and sales drafting flows model review-required content and approved delivery.
- Marketing has no typed content brief, asset, variant, claim review, or content approval aggregate.

### 3. Dependencies

- Prompt 1.
- Prompt 2 for calendar linkage.

### 4. Implementation Requirements

- Add company-owned content briefs with:
  - purpose, audience, campaign, channel, language, tone, call to action, owner, due date, required sources, constraints, and approval policy.
- Add content assets and immutable or versioned variants with:
  - asset type;
  - draft body or provider asset reference;
  - source IDs and claim annotations;
  - AI/human authorship metadata;
  - review state, reviewer, decision, reason, and timestamps;
  - campaign/activity and calendar links.
- Implement deterministic preflight checks for missing product evidence, unapproved prices, unsupported claims, missing consent context, forbidden language, missing disclosure, and stale sources.
- Use `IAgentReasoningGateway` only for bounded draft generation, rewriting, summarization, and explanation. Validate structured output and source references.
- Add approval requests for brand, product, legal/compliance, commercial, and public-publish review as required by policy.
- Model publishing as a separate provider-backed action. Initial implementation may track manual publishing, but must not pretend a CMS/social/ads provider is connected.
- Add asset outcome observations such as published, sent, viewed, clicked, responded, converted, rejected, or retired without overwriting provider evidence.
- Add APIs, typed Web client, audit events, activity-feed events, and task/Agent Team projection.
- Add migration and indexes for company/campaign/status/due-date queries.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Never store credentials, raw provider tokens, hidden prompts, or chain-of-thought in content records.
- AI-generated text is always visibly identified until human or policy approval.
- Public publishing, bulk sends, paid promotion, and commercial claims require the applicable approval and trusted tool.
- Preserve source language and communication-language policy; do not silently translate regulated or legal text.
- Asset versions used in an approved or executed activity must remain auditable.

### 6. Acceptance Criteria

- Given an approved product source and content brief, when a draft is generated, then every factual claim has a source reference or is marked unsupported.
- Given an unsupported price or product capability, when preflight runs, then approval is blocked with a plain-language explanation.
- Given an approved variant linked to a scheduled activity, when the source is later edited, then the executed version and its evidence remain unchanged.
- Given no configured publishing provider, when a user chooses publish, then the UI offers a manual tracked task or configuration route and does not report a successful publish.
- Given a cross-company asset identifier, when requested or linked, then access is denied without disclosure.

### 7. Verification

- Unit-test content lifecycle, versioning, preflight policy, claims, localization, and approval requirements.
- Integration-test drafting, source validation, approval, manual publishing tasks, audit, authorization, and tenant isolation.
- Add adversarial tests for prompt injection in uploaded documents and unsupported claims.
- Add migration and local/Docker SQL Server checks.
- Add focused UI tests for long content, source display, approvals, errors, responsive layout, English, and Swedish.

### 8. Definition Of Done

Maya can create useful, source-backed content drafts and variants through a complete review lifecycle. Claims, versions, approvals, provider state, and outcomes are inspectable and no unapproved external action occurs.

---

## Prompt 4: Add Audience Intelligence And Qualified-Demand Criteria

### 1. Title And Outcome

Enable Maya to explain audiences, recommend safe segment improvements, and identify marketing-qualified demand while deterministic rules remain authoritative for membership, consent, suppression, and Sales acceptance.

### 2. Current Context

- Campaign segmentation supports typed B2B, B2C, and explicit-list criteria.
- Segment previews and audience snapshots retain inclusion, exclusion, consent, language, and source evidence.
- Sales owns prospects, contacts, companies, engagement, consent-aware next actions, deals, and pipeline.
- Support owns cases, complaints, risk, and suppression-relevant evidence.
- The repository does not have a Marketing-qualified-demand definition, scoring evidence model, or acceptance feedback loop.

### 3. Dependencies

- Prompt 1.
- Prompt 2.

### 4. Implementation Requirements

- Add company-configurable qualified-demand definitions for B2B and B2C with:
  - required deterministic conditions;
  - optional weighted evidence;
  - freshness windows;
  - exclusions and suppression;
  - threshold;
  - version and effective dates;
  - owner and approval state.
- Reuse existing segment evaluator and Sales read boundaries. Do not create a second contact or audience store.
- Implement an audience-insight service that explains:
  - size and change;
  - inclusion and exclusion reasons;
  - consent and language;
  - account/customer fit;
  - engagement;
  - Sales state;
  - Support risk;
  - missing or stale data.
- Implement deterministic qualified-demand evaluation with stable evidence IDs and reason codes.
- Allow Maya to recommend definition or segment changes, but require review and explicit version activation.
- Create candidate handoff records only through the typed handoff boundary introduced in Prompt 5; until then expose qualified candidates as reviewable read results.
- Capture Sales acceptance, rejection, duplicate, bad-fit, and timing feedback for later quality analysis without allowing it to rewrite historical evidence.
- Add APIs, typed clients, audit, authorization, tenant isolation, and migration where required.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- AI must not determine consent, suppression, eligibility, or final qualification.
- Do not use protected or sensitive personal characteristics for targeting or scoring.
- Do not expose private Support message bodies or unrestricted Sales communications to Marketing.
- A score must be explainable from current structured evidence and its definition version.
- Historical evaluations retain the definition version and source timestamps used.

### 6. Acceptance Criteria

- Given a qualified-demand definition, when a contact is evaluated, then the result includes status, score where applicable, reason codes, source IDs, freshness, and missing evidence.
- Given a suppressed or unconsented contact, when evaluation runs, then it cannot produce an outreach-eligible qualified candidate.
- Given a critical Support issue covered by policy, when audience preview or qualification runs, then the record is excluded or requires review with an explainable reason.
- Given a proposed definition change, when Maya recommends it, then the active definition remains unchanged until an authorized user approves a new version.
- Given Sales feedback, when later reports are generated, then acceptance metrics use immutable feedback events and do not rewrite the original evaluation.

### 7. Verification

- Unit-test B2B/B2C definitions, thresholds, freshness, exclusions, sensitive-data restrictions, and reason codes.
- Integration-test audience insights, Sales and Support read boundaries, feedback events, authorization, and tenant isolation.
- Add tests proving AI recommendations cannot alter active definitions directly.
- Add migration and local/Docker SQL Server checks where required.
- Build affected projects and run existing campaign segmentation regressions.

### 8. Definition Of Done

Audience and qualified-demand analysis is deterministic, explainable, consent-aware, versioned, and connected to existing campaign and Sales evidence. Maya can recommend improvements without controlling eligibility or Sales state.

---

## Prompt 5: Orchestrate Campaign Collaboration And Sales Handoffs

### 1. Title And Outcome

Connect Marketing work to the implemented campaign lifecycle and create reliable, typed marketing-to-sales handoffs so qualified demand reaches Alex or a human Sales owner with evidence and clear ownership.

### 2. Current Context

- Campaign initiatives support objectives, owners, offers, audiences, activities, schedules, costs, performance, and governed lifecycle.
- Campaign activity scheduling can create manual tasks and cross-agent handoffs.
- Shared Agent AI provides task-backed, typed handoff concepts.
- Sales owns leads, contacts, deals, pipeline, next actions, and outbound email execution.
- Marketing currently has no accepted contract for transferring qualified demand or requesting Sales work.

### 3. Dependencies

- Prompt 1.
- Prompt 2.
- Prompt 4.

### 4. Implementation Requirements

- Define a typed marketing-to-sales handoff aggregate/contract containing:
  - company, campaign, audience snapshot, contact/account/customer identifiers;
  - qualification-definition version and evidence;
  - engagement summary and timestamps;
  - consent and communication language;
  - suggested next action, urgency, expiry, and owner;
  - status, Sales decision, reason, and linked lead/deal/task.
- Implement legal lifecycle transitions: proposed, waiting for review, accepted, returned, rejected, expired, converted, and closed.
- Create handoffs idempotently from stable campaign/subject/qualification evidence.
- Route accepted handoffs through existing Sales application services to link or create the appropriate prospect/lead/task without duplicating contacts or changing pipeline state implicitly.
- Allow Sales to return a handoff for missing evidence and Marketing to resubmit a new version while preserving history.
- Add cross-agent task ownership so Maya's preparation, Alex's review, and human approval appear in the correct Agent Team columns.
- Add campaign-level collaboration:
  - Marketing prepares objectives, audience, content, and activity recommendations;
  - Alex reviews Sales execution and commercial follow-up;
  - Laura reviews material budget/cost requests;
  - Ben supplies suppression and customer-risk signals.
- Ensure campaign launch and provider execution continue through existing campaign approval and execution services.
- Add APIs, typed clients, audit, activity feed, notifications, retries for background routing, and operator-visible failure/reconciliation.
- Add migration and indexes for company/status/owner/expiry queries.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Marketing cannot create a qualified Sales opportunity, change stage, or send Sales outreach directly.
- Acceptance must be explicit unless a company-approved deterministic policy permits automatic task creation; even then it must not auto-qualify pipeline state.
- Idempotency must prevent duplicate leads, tasks, or handoffs after retries.
- Cross-company identifiers must never resolve.
- Handoff expiry and rejection must stop pending Marketing follow-up according to policy without deleting evidence.

### 6. Acceptance Criteria

- Given a qualified candidate with evidence, when Maya proposes a handoff twice, then one active handoff exists.
- Given an accepted handoff matching an existing contact and lead, when routed, then existing records are linked rather than duplicated.
- Given missing consent or expired evidence, when a handoff is proposed or accepted, then the operation is blocked or returned with an explainable reason.
- Given a handoff waiting for Sales, when viewed on Agent Team, then Maya's work is not shown as ongoing and Alex's review is shown in the correct waiting/review state.
- Given a transient routing failure, when retry succeeds, then the same handoff is reconciled without duplicate side effects.

### 7. Verification

- Unit-test lifecycle, expiry, versioning, and idempotency.
- Integration-test existing-record matching, Sales routing, campaign linkage, task projection, audit, authorization, retry, and tenant isolation.
- Add concurrency tests for simultaneous accept/reject decisions.
- Add migration and local/Docker SQL Server checks.
- Run existing Sales lead, campaign, task, approval, and Agent Team regression tests.

### 8. Definition Of Done

Marketing and Sales collaborate through a durable, evidence-backed handoff with one clear owner at each stage. Campaign execution remains governed, Sales records are not duplicated, and failures are visible and reconcilable.

---

## Prompt 6: Implement Marketing Performance, Costs, Attribution, And Experiments

### 1. Title And Outcome

Provide trustworthy Marketing performance and experimentation using normalized channel observations, Finance-authoritative costs, Sales outcomes, explicit attribution, and guardrails against false certainty.

### 2. Current Context

- Campaign performance already combines delivery, engagement, opportunity/revenue association, costs, KPI definitions, and versioned KPI snapshots.
- Finance is authoritative for booked campaign costs and revenue.
- Sales stores source attribution, campaign association, and pipeline outcomes.
- Provider-backed and manually tracked campaign activities retain result evidence.
- There is no general Marketing channel-observation model, Marketing overview read model, or governed experiment aggregate.

### 3. Dependencies

- Prompts 2 through 5.

### 4. Implementation Requirements

- Extend existing campaign performance rather than creating a parallel reporting engine.
- Add normalized, immutable channel observations for permitted providers/manual imports with:
  - provider/tool, external reference, campaign/activity/content/variant;
  - metric code, value, unit, currency where applicable;
  - observed period, retrieved time, source freshness, and raw-evidence reference;
  - idempotency key and reconciliation status.
- Add Marketing performance read models for objective progress, audience, reach, engagement, qualified demand, Sales acceptance, pipeline association, directly attributable revenue, cost, and guardrail metrics.
- Explicitly distinguish:
  - zero;
  - unavailable;
  - stale;
  - partial;
  - estimated;
  - authoritative.
- Reuse Finance read boundaries for booked/approved costs and Sales boundaries for pipeline and won-revenue evidence.
- Retain attribution model, window, confidence, and supporting events. Label influence separately from direct attribution.
- Add governed experiment entities with hypothesis, variants, audience allocation, primary metric, guardrails, evidence threshold, period, stop rules, status, and decision.
- Implement deterministic allocation and result calculations where supported. Maya may explain outcomes but cannot declare a winner below the evidence threshold.
- Add alerts/tasks for material negative guardrails such as complaint, bounce, unsubscribe, spend, or Support risk.
- Add APIs, typed clients, KPI snapshots, audit, authorization, tenant isolation, migration, and retention policy.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Do not add monetary values across currencies without an explicit conversion source and as-of time.
- Provider observations never overwrite booked Finance values or original provider references.
- Correlation and influence must not be presented as causal attribution.
- Missing provider access or stale data must be visible.
- Experiments cannot bypass consent, suppression, approved content, frequency, budget, or provider policies.

### 6. Acceptance Criteria

- Given repeated import of the same provider observation, when reconciliation runs, then one normalized observation exists.
- Given missing cost evidence, when cost-per-qualified-demand is displayed, then it is unavailable rather than zero.
- Given influenced pipeline without direct conversion evidence, when performance is shown, then it is labeled influenced and not directly attributable.
- Given an experiment below its evidence threshold, when Maya analyzes it, then no winning variant is declared.
- Given a guardrail breach, when detected, then an operator-visible task or approval is created idempotently with source evidence.

### 7. Verification

- Unit-test normalization, idempotency, metric states, currency separation, attribution labels, experiment allocation, thresholds, and guardrails.
- Integration-test Finance/Sales read boundaries, KPI snapshots, provider reconciliation, retry, audit, authorization, and tenant isolation.
- Add tests for stale, partial, unavailable, and conflicting evidence.
- Add migration and local/Docker SQL Server checks.
- Run campaign performance and Finance/Sales attribution regressions.

### 8. Definition Of Done

Marketing performance is source-backed, period-aware, currency-safe, and explicit about availability and attribution confidence. Experiments are governed and statistically bounded, and provider failures or negative signals are actionable.

---

## Prompt 7: Implement Maya's Governed Marketing Intelligence

### 1. Title And Outcome

Implement Marketing-specific AI advice for briefings, planning, content, prioritization, performance interpretation, experiments, and post-campaign review while deterministic services and approvals remain authoritative.

### 2. Current Context

- `IAgentReasoningGateway`, orchestration runs, source validation, capability catalog, role briefings, planning, prioritization, exception interpretation, handoffs, and memory proposals already exist.
- Finance, Sales, and Support have role-specific decision services that retrieve authoritative evidence before model reasoning.
- Campaign intelligence is currently associated with Alex and focuses on Sales outcomes.
- Prompts 1 through 6 provide Marketing role access, objectives, plans, content, audiences, handoffs, and performance evidence.

### 3. Dependencies

- Prompts 1 through 6.

### 4. Implementation Requirements

- Add stable Marketing capability IDs and manifests for:
  - grounded marketing questions;
  - daily/weekly/monthly briefing;
  - plan advice;
  - audience insight;
  - content brief and draft advice;
  - campaign readiness;
  - work prioritization;
  - performance interpretation;
  - experiment advice;
  - post-campaign review;
  - marketing-to-sales handoff recommendation.
- Implement Application contracts and Infrastructure decision services. Controllers and UI must transport structured requests/results only.
- Retrieve Marketing evidence through owning module boundaries with explicit company filtering.
- Return structured results with:
  - summary and recommendations;
  - claims and source IDs;
  - confidence;
  - data freshness;
  - assumptions and unknowns;
  - risks and guardrails;
  - requested actions;
  - approval requirement.
- Add Marketing cadence to existing idempotent role-analysis scheduling. Do not create duplicate completed runs for the same agent, period, and prompt version.
- Make degradation safe:
  - deterministic facts remain visible if AI is unavailable;
  - no provider action is executed;
  - timeout, malformed response, unsupported source, and configuration failures are operator visible.
- Implement evidence-backed memory proposals for reusable marketing observations, requiring review before activation.
- Add prompt-injection resistance for uploaded documents, mail, and provider text.
- Add quality events for factual accuracy, source coverage, brand correction, approval outcome, handoff acceptance, and operator usefulness without automatically increasing autonomy.
- Add authorized APIs, typed clients, audit, observability, and plain-language failure messages.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Model output cannot change audience eligibility, active qualification definitions, campaign lifecycle, content approval, budget, pipeline state, Support state, or provider execution.
- Do not store hidden prompts, chain-of-thought, credentials, or unvalidated raw provider output.
- Unsupported claims must be omitted or explicitly marked unknown.
- Deterministic ranking, calculations, policy results, and source facts must not be replaced by model estimates.

### 6. Acceptance Criteria

- Given current Marketing evidence, when a briefing runs twice for the same cadence window and prompt version, then one completed run is reused.
- Given a recommendation, when displayed, then its material claims link to permitted source IDs and show confidence and missing evidence.
- Given malicious instructions in an uploaded source, when reasoning runs, then those instructions are treated as untrusted content and cannot alter tools, policy, or output contract.
- Given AI provider failure, when the Marketing workspace loads, then deterministic work, campaign, and KPI data remain available with a clear AI status.
- Given a recommendation requiring external action, when accepted, then it creates the correct task, handoff, or approval request and does not call the provider directly.

### 7. Verification

- Unit-test structured-output validation, unsupported claims, confidence/review rules, prompt-injection handling, and action mapping.
- Integration-test evidence retrieval, company filtering, cadence idempotency, capability availability, provider failure, audit, and tenant isolation.
- Add tests proving no model path mutates authoritative Marketing, Sales, Finance, Support, or provider state.
- Add quality-event and memory-proposal lifecycle tests.
- Build Operations Infrastructure, affected role infrastructure, API, Web, and tests.

### 8. Definition Of Done

Maya provides useful, grounded Marketing advice with inspectable evidence and safe failure behavior. Accepted advice enters existing governed workflows, and model output never becomes an authoritative business decision or external side effect.

---

## Prompt 8: Build The Production Marketing Workspace

### 1. Title And Outcome

Build a professional, localized Marketing workspace that helps an SME operator understand objectives, work, campaigns, audiences, content, qualified demand, performance, and Maya's recommendations without duplicating existing Campaigns or Agent Management screens.

### 2. Current Context

- The consolidated application shell provides Overview, Agent Team, Finance, Sales, Support, Work, settings, history, and restricted tools.
- Agent Team has a kanban-style operating view and card-details panel.
- Agent Management owns agent profile, briefs, capabilities, access, mailbox, tools, and provider configuration.
- The Campaigns workspace provides overview, audience, activity plan, performance, and Alex review.
- `/docs/design.md` defines the production UI system and `ui-instructions.md` requires screenshot-first substantial UI implementation.
- Prompts 1 through 7 provide Marketing APIs and real data. There is no Marketing navigation entry or workspace.

### 3. Dependencies

- Prompts 1 through 7.

### 4. Implementation Requirements

- Follow the screenshot-first workflow:
  - inspect current rendered shell and design references;
  - create or update a Marketing workspace reference image;
  - document the intended information hierarchy and responsive behavior before implementation.
- Add Marketing to the main navigation using the existing icon system, responsive shell, selected-state handling, company context, and localization.
- Build a Marketing workspace with focused tabs or routes:
  - Overview;
  - Plan and calendar;
  - Campaigns;
  - Audiences;
  - Content;
  - Performance;
  - Handoffs;
  - Maya recommendations.
- Reuse the existing Campaigns editor through links or embedded read summaries; do not create a second campaign editor.
- Overview should show:
  - objective progress;
  - active work and approvals;
  - qualified demand created;
  - cost per qualified demand when evidence exists;
  - campaigns at risk;
  - content due;
  - handoffs awaiting Sales;
  - material guardrails and provider/data freshness.
- Use compact operational layouts, stable responsive grids, accessible tables/lists, icons, badges, tooltips, and plain-language empty/error/configuration states.
- Integrate Maya into:
  - Agent Team row and work cards;
  - Agent Team details panel;
  - contextual Marketing sidebar or header where consistent with the shell;
  - Agent Management configuration links.
- Add loading, stale, unavailable, partial, empty, unauthorized, provider-failure, AI-failure, and approval-required states without mock data.
- Localize all user-facing text in English and Swedish. Format dates, numbers, percentages, and currencies through existing localization services.
- Add route/navigation regression protection so Marketing links do not capture unrelated menu clicks.
- Add telemetry for page/API failures and user-triggered Marketing actions without logging sensitive content.

### 5. Constraints And Preservation Rules

- Follow the shared instructions.
- Follow `/docs/design.md`; do not introduce a separate visual style.
- Do not use oversized marketing-site heroes, decorative card nesting, gradients, or invented product metrics.
- Keep campaign editing, agent access, and provider configuration on their owning screens with clear deep links.
- Never show unavailable as zero.
- Do not expose source records or actions beyond the current user's company and permissions.

### 6. Acceptance Criteria

- Given a company with Marketing configured, when the workspace opens, then it shows real objective, work, campaign, content, handoff, and performance data from typed APIs.
- Given no Marketing data, when the workspace opens, then it presents actionable empty states without seeded examples.
- Given missing mailbox or provider access, when the user chooses configure, then the exact Agent Management or provider screen opens with preserved company and agent context.
- Given an Agent Team Marketing card, when selected, then its details appear in the existing right-hand details panel.
- Given Swedish UI preference, when each Marketing route is opened, then all labels, states, errors, dates, numbers, and currency formatting use Swedish localization.
- Given desktop and mobile viewports, when the workspace is rendered, then controls remain readable, no text overlaps, and primary workflows remain reachable.

### 7. Verification

- Add component/page tests for every view, real API mapping, empty/unavailable/stale/error states, authorization, and navigation.
- Add English and Swedish localization completeness tests.
- Run screenshot/browser verification at representative desktop and mobile viewports according to `ui-instructions.md`.
- Verify text fit, keyboard navigation, focus states, accessible names, selected navigation, and no overlap.
- Run API and tenant-isolation tests for all Marketing read/action routes.
- Build Web and API and run affected Agent Team, Agent Management, Campaigns, navigation, and localization regressions.

### 8. Definition Of Done

Marketing is a production-grade operational workspace using real governed data and the existing design system. Operators can understand and act on Marketing work, evidence, approvals, and configuration without duplicate screens, mock values, inaccessible controls, or hidden failures.

