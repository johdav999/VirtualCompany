# Maya Marketing Agent — Production Implementation Prompt Pack

## How to use this prompt pack

Execute the prompts in order. Each prompt delivers a bounded production capability and identifies its prerequisites. Do not stop at an intermediate checkpoint when executing the complete sequence: continue until the sequence is complete, genuinely blocked, or explicitly paused by the user.

The prompts were derived from the current repository and [marketing.md](./marketing.md). The existing implementation is authoritative when it conflicts with older planning material.

## Mandatory instructions for every prompt

Before making changes for any prompt:

1. Read and follow `AGENTS.md`, `production-implementation.md`, and `/docs/architecture-rules.md` completely.
2. Read `/docs/architecture-overview.md` as background only.
3. `architecture-inst.md` is referenced by the workspace instructions but was not present when this prompt pack was created. If it exists when executing a prompt, read and follow it completely. If it remains absent, do not invent its contents; use `/docs/architecture-rules.md` as the authoritative architecture source and mention the missing file in the handoff.
4. For every prompt that changes UI, UX, layout, components, navigation, or user-facing text, read and follow `ui-instructions.md` and `/docs/design.md`. Complete the mandatory screenshot-first workflow before implementation, save the reference under `/docs/design/references/`, and compare the completed UI with the reference.
5. Treat `production-implementation.md` as mandatory: deliver real production implementation, authentication and authorization, real persistence and APIs, and migrations where needed. Do not use scaffolding, mock production data, silent fallbacks, or deferred in-scope TODOs.
6. Keep Marketing and Sales implementation ownership in `VirtualCompany.Infrastructure.Sales`. Use Application contracts and the shared orchestration, workflow, approval, outbox, knowledge, audit, and background-execution subsystems. Do not create a second AI or integration stack.
7. Preserve company isolation and server-side authorization. A company ID supplied through a route, query, or header is context, not proof of access.
8. Use CQRS-lite boundaries. Controllers and Blazor components must not own business rules, provider behavior, EF queries, policy decisions, or cross-capability orchestration.
9. Use EF Core migrations as the only schema authority. Preserve local SQL Server and Docker SQL Server restore/run compatibility and inspect the migration and model snapshot.
10. Put important external side effects behind durable outbox/background execution with stable business idempotency, bounded retry, reconciliation of ambiguous outcomes, safe failures, and business audit evidence.
11. Put sensitive decisions in deterministic backend policies. Recheck approval immediately before any approved external action.
12. Add tests in the narrowest appropriate projects and preserve all existing valid behavior.

---

## Prompt 1 — Harden Maya's identity, authorization, and structured capability boundary

### 1. Title and outcome

**Harden the existing Marketing agent foundation.** Ensure only the current company's active Marketing agent can invoke Marketing AI capabilities, replace Maya's legacy generic tool declarations with explicit internal capability permissions, and use the shared company-aware Web transport. This creates a safe foundation for every later prompt.

### 2. Current context

- Maya is defined by template ID `marketing` in `src/VirtualCompany.Persistence/Persistence/SeedData/agent-templates.json` and created as a guided agent by `CoreCompanyAgentSeeder`.
- Seven recommendation-only Marketing capability manifests exist in `AgentCapabilityCatalog`.
- `MarketingAgentAnalysisService` validates non-empty IDs and analysis type but does not itself prove that the supplied agent belongs to the company, is active, and has the Marketing role.
- `MarketingController` is protected by `CompanyPolicies.CompanyMember`, but its commands do not yet expose granular Marketing authorization decisions.
- `MarketingApiClient` constructs its own company header instead of using `ICompanyApiTransport`/`CompanyApiTransport`.
- Existing tests cover Marketing domain behavior, tenant-filtered queries, core-agent seeding, and capability metadata.

### 3. Dependencies

- None.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add or reuse an Application-level guard that authoritatively resolves a company-scoped Marketing agent and rejects missing, archived, paused, cross-company, or non-Marketing agents.
- Apply the guard to all Marketing agent analysis entry points and scheduled Marketing cadence execution.
- Introduce stable Marketing tool IDs for the current read/recommend boundary; do not add execute permissions yet.
- Update Maya's canonical template to use explicit tool IDs, action classes, and data scopes consistent with the runtime capability catalog.
- Preserve guided autonomy for existing and newly created Maya agents. Do not silently elevate existing company agents.
- Add granular server-side Marketing authorization policies or command authorization checks for read versus mutation behavior using established authorization patterns.
- Migrate `MarketingApiClient` to the shared company-aware transport without changing existing route contracts or offline semantics.
- Persist and return safe, plain-English denial reasons; never expose raw policy types or internal enum values in the UI.
- Add business audit evidence for denied sensitive Marketing agent invocations where the existing audit subsystem expects it.

### 5. Constraints and preservation rules

- Preserve template ID `marketing`, Maya's existing company-agent records, routes, wire values, and capability IDs.
- Do not duplicate agent resolution, company context, transport header creation, or tool-policy logic.
- Do not add provider integrations, publishing, campaign launch, or spending in this prompt.
- Any use of `IgnoreQueryFilters` must explicitly reapply company scope.
- Preserve existing non-Marketing agent behavior and cadence execution.

### 6. Acceptance criteria

- **Given** a valid active Maya agent owned by the resolved company, **when** a permitted Marketing analysis is requested, **then** the existing grounded analysis succeeds.
- **Given** an agent from another company, **when** its ID is supplied to a Marketing endpoint, **then** the request is denied without revealing whether that agent exists.
- **Given** a Finance, Sales, Support, paused, archived, or assignment-disabled agent, **when** Marketing analysis is requested, **then** it is rejected with a stable safe reason.
- **Given** Maya's runtime profile, **when** capabilities are resolved, **then** only explicit Marketing read/recommend tools and scopes are available.
- **Given** a Web Marketing request, **when** it is sent, **then** company and correlation context are supplied by the shared transport implementation.

### 7. Verification

- Add focused agent-guard and authorization unit tests.
- Add API integration tests for valid, cross-company, wrong-role, paused, and unauthorized-user requests.
- Add template/capability catalog tests for stable tool IDs, action classes, and scopes.
- Add Web client transport tests proving company context, correlation behavior, not-found mapping, and cancellation.
- Run the relevant API, Web, and contract tests, then build `VirtualCompany.Api` and `VirtualCompany.Web`.

### 8. Definition of done

Production behavior is implemented end to end with no placeholder permissions, duplicated transport, mock authorization, silent fallback, or deferred security TODO. Existing company agents remain compatible and no autonomy is increased.

---

## Prompt 1A — Integrate Marketing with the company orchestration control plane

### 1. Title and outcome

**Create the governed boundary between Maya and company-level orchestration.** Eva or the configured company coordinator can assign validated Marketing initiatives to Maya with complete goal, priority, dependency, budget, autonomy, evidence, and correlation context; Marketing can expose authoritative snapshot projections, progress, outcomes, risks, and initiative proposals back to the company operating loop without creating a competing company planner.

### 2. Current context

- `company-orchestration.md` documents an implemented durable company operating loop.
- `CompanyGoal` and `CompanyOperatingConfiguration` define company outcomes, coordinator, cadence, pause, budgets, limits, and four autonomy levels.
- `OperatingCycle`, snapshots, plans, `OperatingInitiative`, validation results, decisions, and reviews preserve the company decision chain.
- `CompanyOperatingSnapshotService`, `CompanyOperatingCycleService`, `OperatingPlanValidationService`, and `CompanyOperatingCycleScheduler` live in `VirtualCompany.Infrastructure.Operations`.
- Committing an approved company plan creates ordinary company tasks and links them to operating initiatives; work review compares linked task evidence and updates initiative outcomes.
- `ISingleAgentOrchestrationService` and `IMultiAgentCoordinator` remain the shared execution runtimes.
- Marketing implementation belongs to `VirtualCompany.Infrastructure.Sales`, so Operations must not directly reference Marketing infrastructure. Cross-capability coordination must use Application contracts, durable tasks/workflows/events, or shared Domain entities.
- The current company snapshot and Marketing cadence do not yet provide a complete typed Marketing departmental projection, assignment-validation contract, effective-autonomy calculation, or structured Marketing result feedback loop.

### 3. Dependencies

- Prompt 1.
- Read and follow `company-orchestration.md` completely in addition to all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add focused Application contracts for a bounded Marketing contribution to the company operating snapshot. The projection should include current Marketing strategy/segment/objective/plan state where available, active campaigns and journeys, budget use/constraints, performance changes, attribution/data-quality limits, deadlines, workload, approvals, workflow/provider exceptions, and material risks/opportunities.
- Implement the Marketing snapshot contributor in `VirtualCompany.Infrastructure.Sales`. Integrate it into `CompanyOperatingSnapshotService` through an inward-pointing Application abstraction or established business-signal extension pattern; do not add an Operations-to-Sales infrastructure reference.
- Preserve source IDs, timestamps, freshness, data gaps, truncation, and safe summaries. Do not send raw unbounded Marketing tables or provider payloads into the company coordinator prompt.
- Add an Application query/validation boundary that resolves the company operating context for a Marketing task or initiative: company goal, operating cycle/plan/initiative and versions, desired outcome, priority, target date, owner/contributors/reviewer, dependencies, budget, completion evidence, autonomy/action limits, validation status, and correlation chain.
- Ensure Marketing assignments are accepted only when the goal and initiative are active/allowed, Maya is the intended eligible owner, linked records belong to the company, required dependencies are satisfied or explicitly waiting, the plan/version is current, equivalent work is not already active, and company pause is not set.
- Define a deterministic effective-authority decision as the most restrictive combination of company operating level, company pause, goal/initiative limits, Maya's agent autonomy/profile, tool/capability permission, Marketing action policy, approval state, provider availability, and budget/capacity. Return stable reason codes and plain-English explanations.
- Carry the complete correlation chain from company goal/cycle/plan/initiative to task, Marketing artifact, orchestration run, tool execution, workflow, approval, outbox message, provider result, observation, and review evidence.
- Add a structured Marketing progress/outcome contribution contract for status, completed artifacts, evidence version, actual versus expected result, confidence, data gaps, dependencies, blocked/failure reason, changed forecast, lessons, and requested next company action.
- Add a structured Marketing operating-signal contract for material Marketing opportunities, risks, product/customer evidence, budget needs, segment changes, provider failures, or cross-functional dependencies discovered outside the assigned scope. Signals must request a future company operating-cycle evaluation and must not recursively invoke planning in the same transaction.
- Reuse existing tasks, workflow events, company cycle automation, audit, and review mechanisms. Introduce new durable signal or linkage persistence only where the current implementation cannot provide idempotency, lifecycle, and recovery.
- Expose Marketing initiative context and progress in the existing `/company-operation` and Marketing/Work read models with links between the company initiative and Maya's work. If this is a significant UI change, write and generate `docs/design/references/company-operation-marketing-initiative-reference.png` before implementation.
- Add an EF migration only if new durable linkage/signal state is required; preserve local and Docker SQL Server compatibility.

### 5. Constraints and preservation rules

- Do not create a Marketing-owned company goal, company operating plan, executive coordinator, or second company orchestration stack.
- Maya must not change company goal priority, company operating budgets, initiative ownership, company autonomy, pause state, or cross-department plan decisions.
- Company instructions cannot bypass Marketing authorization, consent, legal/compliance policy, approval, provider, budget, or external-effect controls.
- Marketing self-originated signals are proposals/evidence, not automatically approved company initiatives.
- Do not use chat messages as the system of record for assignments, progress, outcomes, or signals.
- Do not create recursive synchronous loops between Marketing events and company planning.
- Preserve existing company operating-cycle behavior, task commit idempotency, reviews, pause semantics, and non-Marketing snapshot contributions.

### 6. Acceptance criteria

- **Given** an active validated company initiative assigned to Maya, **when** Marketing resolves its work context, **then** the exact goal, plan/initiative versions, desired outcome, priority, budget, dependencies, autonomy limits, completion evidence, and correlation IDs are available.
- **Given** company pause, a stale/rejected plan, inactive goal, wrong owner, cross-company reference, duplicate active work, or unsatisfied hard dependency, **when** Maya attempts to accept the assignment, **then** execution is blocked with stable evidence-backed reasons.
- **Given** company controlled-execution authority but Marketing policy allows only recommendation, **when** effective authority is evaluated, **then** recommendation is the maximum allowed action.
- **Given** material Marketing progress or completion, **when** it is reported, **then** the company initiative review can consume versioned evidence without scraping chat or provider payloads.
- **Given** Maya discovers a cross-functional opportunity outside her initiative, **when** she raises a signal, **then** one durable idempotent signal requests a future company cycle and no recursive planner call occurs.
- **Given** company orchestration builds a snapshot, **when** Marketing data is stale or unavailable, **then** the snapshot records the gap/freshness rather than inventing state or failing silently.

### 7. Verification

- Add contract and policy tests for assignment resolution, ownership, versions, dependencies, pause, effective authority, duplication, budget/capacity, and safe explanations.
- Add company snapshot tests for Marketing contribution, source IDs, freshness, gaps, truncation, and absence of infrastructure-layer coupling.
- Add tenant-isolation and authorization tests across goal, initiative, task, Marketing artifact, progress, outcome, and signal paths.
- Add idempotency/concurrency tests for repeated assignment acceptance, progress reporting, signals, company snapshot cycles, and review.
- Add integration tests proving Marketing results feed existing `CompanyOperatingCycleService` work review and that signals request later evaluation without recursion.
- Add UI/read-model tests for company initiative-to-Marketing work navigation, blocked state, progress, evidence, and outcome.
- If schema changes, create and inspect the EF migration and run pending-model-change validation.
- Build Application, Operations Infrastructure, Sales Infrastructure, API, and Web and run company orchestration, task, agent, Marketing, audit, and Web contract suites.

### 8. Definition of done

Marketing and company orchestration exchange real, company-scoped, versioned, bounded, idempotent assignments, snapshot data, progress, outcomes, and signals through correct architectural boundaries. Maya remains a departmental operator under the company control plane, and no duplicate planner, direct sibling-infrastructure dependency, recursive loop, authority escalation, or untracked instruction path remains.

---

## Prompt 2 — Add versioned Marketing strategy management with 4Ps, STP, and governance

### 1. Title and outcome

**Create a versioned Marketing strategy capability.** Users and Maya can prepare, review, approve, activate, supersede, and inspect a grounded strategy covering Kotler's 4Ps, optional 7Ps, STP, positioning, journey, channel mix, KPIs, assumptions, risks, evidence, and references to the approved customer-segment versions the strategy serves.

### 2. Current context

- Marketing currently persists objectives and plans, but it has no first-class strategy aggregate.
- `MarketingOperationsService` already implements company-scoped objectives, plans, content, observations, experiments, qualification, and handoffs.
- The Marketing dashboard has Overview and operational tabs and uses existing cards, forms, and plain-English states.
- Approval and workflow subsystems already exist and must remain authoritative for sensitive activation decisions.

### 3. Dependencies

- Prompts 1 and 1A.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a tenant-owned Marketing strategy aggregate with explicit lifecycle states such as draft, in review, approved, active, superseded, and cancelled using typed storage values consistent with repository conventions.
- Persist strategy title, summary, business context, Product, Price, Place, Promotion, optional People/Process/Physical Evidence, target-segment references, positioning, ICPs, customer jobs, journey, channel mix, KPIs, budget assumptions, risks, dependencies, evidence references, missing evidence, validity period, and version.
- Do not embed the full definition of a strategic customer segment inside the strategy. Prompt 3A introduces the first-class segment model; once it exists, strategy activation must reference exact approved segment versions and surface stale or superseded segment dependencies.
- Use relational columns for core queryable lifecycle, company, version, dates, owner, and status data. Use bounded JSON only for flexible structured sections where appropriate.
- Enforce one intentional active strategy per company and period according to a deterministic backend policy.
- Support prepare proposal, create draft, update draft with optimistic concurrency, submit for review, approve/reject through the established approval subsystem, activate, and supersede.
- Persist audit evidence for material lifecycle and content changes.
- Add Application commands, queries, DTOs, validation, API endpoints, safe problem mappings, and typed Web client methods.
- Extend the existing Marketing workspace with strategy summary and strategy detail/edit surfaces. Use plain language and show status, version, evidence, missing information, approval state, and next action.
- Before UI changes, explicitly write and execute a design prompt for `docs/design/references/marketing-strategy-reference.png` and use it as the implementation target.
- Add an EF migration in `VirtualCompany.Persistence.Migrations` and update the SQL Server model snapshot.

### 5. Constraints and preservation rules

- Do not turn `MarketingPlan` into the strategy aggregate or overload its current meaning.
- Strategy approval and activation decisions must not live in Blazor or controller code.
- Preserve existing objectives, plans, routes, dashboard behavior, and Marketing migrations.
- Do not store the entire strategy as the only JSON blob.
- Preserve local SQL Server and Docker SQL Server migration and restore compatibility.

### 6. Acceptance criteria

- **Given** a company with no strategy, **when** an authorized user creates a complete draft, **then** it is persisted at version 1 with evidence and missing-evidence fields.
- **Given** two users editing the same version, **when** the stale update is submitted, **then** it fails with an actionable concurrency response.
- **Given** a strategy awaiting approval, **when** an unauthorized user attempts activation, **then** activation is denied server-side.
- **Given** an approved strategy, **when** it is activated, **then** conflicting active strategy state is resolved according to policy and fully audited.
- **Given** another company's strategy ID, **when** it is read or mutated, **then** no data or existence information leaks.
- **Given** an empty company, **when** the strategy UI loads, **then** it shows a useful empty state and a clear creation action without mock data.

### 7. Verification

- Add domain lifecycle, validation, versioning, and deterministic-policy tests.
- Add tenant-isolation, authorization, concurrency, approval, and API integration tests.
- Create and inspect the EF migration; run pending-model-change verification using the migrations project and API startup project.
- Add Web presenter/component/client tests and perform screenshot comparison at desktop and narrow responsive widths.
- Build API and Web and run focused Marketing and approval test suites.

### 8. Definition of done

The strategy is a production aggregate with complete persistence, migration, API, authorization, approval, audit, UI, empty/loading/error states, and tests. No strategy section is a non-persisted mock form or unvalidated opaque payload.

---

## Prompt 3 — Add market, customer, and competitive intelligence records

### 1. Title and outcome

**Create a source-grounded Marketing intelligence workspace.** Maya and users can maintain market hypotheses, customer insights, competitor profiles, comparable claims, and dated evidence that can safely ground strategy, campaigns, content, and Sales battlecards.

### 2. Current context

- Company knowledge search exists behind `ICompanyKnowledgeSearchService` and is already used by `MarketingAgentAnalysisService`.
- Marketing observations store normalized metrics with provider and source references, but there is no first-class competitor or research-evidence model.
- Marketing analysis already separates sources and missing evidence, and Sales handoffs already preserve evidence references.
- Documents and chunks are the established knowledge model; this feature must reference them rather than duplicating document storage.

### 3. Dependencies

- Prompt 1.
- Prompt 2 is recommended so intelligence can link to a strategy, but the intelligence records must remain useful independently.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add tenant-owned records for market hypotheses, customer insights, competitors, competitor observations/claims, comparison dimensions, and intelligence review history.
- Support explicit evidence references, source type, retrieval/observation date, freshness, confidence, verified/inferred/estimated classification, and review status.
- Link to accessible knowledge documents/chunks where available without copying unlimited source content.
- Support structured SWOT, positioning, pricing/packaging, audience, feature/capability, messaging, and channel-presence comparisons.
- Add commands and queries for create, update, archive, review, list, detail, freshness queue, and change-since-last-review.
- Add deterministic validation preventing unsupported certainty, missing required attribution, invalid dates, and cross-company source linkage.
- Expose typed APIs and add a Marketing Intelligence workspace using a list/detail pattern with filters, freshness indicators, evidence, and plain-English uncertainty labels.
- Generate and save `docs/design/references/marketing-intelligence-reference.png` before implementing the new UI.
- Add an EF migration and model snapshot changes.

### 5. Constraints and preservation rules

- Do not implement uncontrolled public-web scraping or a provider connector in this prompt.
- Do not store copyrighted source pages or unlimited raw research when a bounded summary and reference is sufficient.
- Do not let inferred claims appear as verified facts.
- Respect knowledge access scope and document processing/indexing state.
- Avoid a generic catch-all research entity that hides important queryable fields.

### 6. Acceptance criteria

- **Given** a dated competitor claim with an accessible source, **when** it is recorded, **then** its source, classification, freshness, and reviewer state are queryable.
- **Given** an inferred market-size estimate, **when** it is displayed, **then** the UI and API identify it as an estimate with assumptions.
- **Given** stale intelligence, **when** the freshness queue is queried, **then** it appears with an actionable review reason.
- **Given** a source outside the company or the user's access scope, **when** a link is attempted, **then** the command is denied.
- **Given** two competitor reviews, **when** change history is requested, **then** material before/after evidence is available.

### 7. Verification

- Add domain and policy tests for evidence, classification, freshness, and lifecycle behavior.
- Add tenant-isolation, authorization, source-access, and API integration tests.
- Add migration and SQL Server model validation.
- Add Web component, presenter, filter, empty-state, and client tests.
- Verify the UI visually against the generated reference and build API and Web.

### 8. Definition of done

The intelligence workspace persists traceable, reviewable, source-grounded records and exposes production APIs and UI. It contains no mock research, silent source failures, or unsupported certainty.

---

## Prompt 3A — Add first-class customer segmentation, sizing, and target selection

### 1. Title and outcome

**Create the strategic customer-segmentation foundation.** Users can define, analyze, compare, approve, version, and select customer segments using needs, behaviors, channel presence, price sensitivity, size, economics, accessibility, competitive intensity, strategic fit, evidence, and risk. Approved segment versions become explicit inputs to Marketing strategy and execution.

### 2. Current context

- Marketing currently has objectives, plans, content briefs, observations, experiments, qualification definitions/evaluations, and Sales handoffs.
- Sales campaigns have audience segments and contacts for campaign execution, while Marketing qualification definitions assess observable contacts. Neither is a durable strategic customer-segment model.
- Prompt 2 adds versioned Marketing strategies and Prompt 3 adds source-grounded market, customer, and competitor intelligence.
- Existing knowledge, Sales, Support, observations, and company records can supply evidence through Application contracts and approved company-scoped retrieval.
- The Marketing workspace does not currently provide a segment-analysis or target-selection surface.

### 3. Dependencies

- Prompts 1–3.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add tenant-owned aggregates for customer segment identity and immutable/versioned segment definitions. Preserve stable segment identity while needs, criteria, estimates, evidence, scorecards, and target decisions evolve by version.
- Support relevant segmentation dimensions without assuming B2B or B2C exclusively:
  - Firmographic, demographic where lawful and relevant, geographic, technographic, lifecycle, behavioral, needs-based, value-based, and channel-based criteria.
  - Customer needs, jobs-to-be-done, desired outcomes, urgency, buying criteria, barriers, anxieties, alternatives, and unmet needs.
  - Usage, purchase, engagement, content-consumption, loyalty, switching, decision-process, and campaign-response behavior.
  - Channel presence across discovery, evaluation, purchase, onboarding, retention, and advocacy, including reachability, consent, targeting feasibility, expected cost, and evidence quality.
  - Price sensitivity, willingness-to-pay and elasticity hypotheses, budget/procurement constraints, perceived value, discount sensitivity, and commercial-model preference.
- Add versioned segment-size estimates with count/value/range, period, geography, currency, top-down/bottom-up/triangulated method, assumptions, evidence, confidence, and freshness.
- Add segment economics including revenue potential, gross-margin potential, expected acquisition cost, sales-cycle length, cost to serve, retention, lifetime-value, and expansion hypotheses with units and confidence.
- Add an explicit, versioned attractiveness scorecard covering size/growth, need intensity, product fit, differentiation, reachability, price/value fit, economics, operational complexity, evidence quality, and consent/privacy/fairness/regulatory/reputational risk.
- Make score dimensions, weights, thresholds, exclusions, and missing-evidence behavior explicit and queryable. A deterministic policy computes the score; the score must not silently select the target.
- Add target decisions such as primary, secondary, experimental/emerging, retention/expansion, observe only, and excluded/not served. Persist rationale, expected impact, confidence, risks, review date, approval status, and actor.
- Support draft, update with optimistic concurrency, compare, submit for review, approve/reject, activate selection, supersede, archive, and scheduled review/freshness behavior.
- Enforce backend policy and approval for activating or materially changing target-segment selection.
- Add links from strategies to exact approved segment versions. Add impact-state records or a queryable impact projection identifying strategies, objectives, plans, campaigns, audiences, briefs, journeys, experiments, budgets, and reports that reference a superseded or materially changed segment version.
- Keep strategic segments distinct from ICPs/personas, campaign audiences, contacts, and qualification definitions. Add explicit mapping/link contracts rather than merging these models.
- Add Application commands/queries/DTOs, validation, API endpoints, authorization, audit, safe problem mappings, and typed Web clients.
- Add a Segments area within the existing Marketing information architecture using comparison and list/detail patterns. Show needs, behavior, channel presence, price sensitivity, size/economics, scorecard, evidence, confidence, freshness, target state, downstream impact, and next action in plain English.
- Before UI implementation, explicitly write and generate `docs/design/references/marketing-customer-segments-reference.png`, then match it at desktop and narrow widths.
- Add an EF migration in `VirtualCompany.Persistence.Migrations`, update the snapshot, and preserve local and Docker SQL Server compatibility.

### 5. Constraints and preservation rules

- Do not repurpose `SalesCampaignAudienceSegment` or `MarketingQualificationDefinition` as the strategic segment aggregate.
- Do not store core segment criteria, lifecycle, target state, sizing, score, or version only in an opaque JSON document.
- Do not infer communication consent from segment membership, behavior, channel presence, or model output.
- Do not use protected or sensitive traits, proxy variables, or unlawful discriminatory logic for targeting. Deterministic policy must reject disallowed criteria and explain why safely.
- Do not present size, price sensitivity, economics, or channel presence estimates as observed facts; preserve method, assumptions, range, confidence, date, and source.
- Do not silently mutate downstream strategies or campaigns when a segment changes.
- Preserve existing campaign audiences, qualification behavior, routes, and storage values.

### 6. Acceptance criteria

- **Given** a segment with needs, behaviors, channels, price sensitivity, size, economics, and evidence, **when** a version is submitted, **then** all queryable strategic dimensions, methods, sources, confidence, and freshness are persisted and reviewable.
- **Given** a top-down size estimate and a bottom-up account estimate, **when** they are compared or triangulated, **then** the UI shows methods, ranges, assumptions, and confidence without false precision.
- **Given** incomplete or stale evidence, **when** attractiveness is calculated, **then** missing evidence affects the deterministic score according to the versioned rule and is shown explicitly.
- **Given** an attractiveness score, **when** a target decision is made, **then** the decision remains a separate approved human/authorized action with rationale.
- **Given** a disallowed sensitive criterion or proxy, **when** a segment is saved or activated, **then** the command is denied by backend policy with a safe reason.
- **Given** a segment version used by an active strategy, **when** a materially changed version is approved, **then** downstream dependencies are marked for review and are not silently rewritten.
- **Given** another company's segment, evidence, customer, or strategy ID, **when** it is read, linked, or mutated, **then** no data or existence information leaks.

### 7. Verification

- Add domain tests for stable identity, immutable versions, lifecycle, target decisions, size ranges/methods, economics, score calculation, freshness, and concurrency.
- Add deterministic policy tests for missing evidence, score weights, sensitive/proxy criteria, target activation, and downstream material-change detection.
- Add tenant-isolation, authorization, approval, audit, cross-source access, idempotency, and API integration tests.
- Add tests proving strategic segments remain distinct from campaign audiences, contacts, and qualification definitions while mappings remain company-scoped.
- Create and inspect the EF migration and run pending-model-change validation through the migrations project with the API startup project.
- Add Web client, presenter, filter, comparison, impact, empty/loading/error, and responsive component tests; visually compare with the generated reference.
- Build `VirtualCompany.Api` and `VirtualCompany.Web` and run focused Marketing, Sales campaign, approval, authorization, and Web contract tests.

### 8. Definition of done

Customer segmentation is a production, source-grounded, versioned, policy-governed capability with real persistence, sizing, analysis, target selection, strategy linkage, downstream impact visibility, API, UI, audit, migration, and tests. It contains no mock estimates, opaque core state, silent downstream mutation, inferred consent, discriminatory targeting shortcut, or deferred in-scope TODO.

---

## Prompt 3B — Let Maya analyze segments and propagate segment decisions through Marketing strategy

### 1. Title and outcome

**Enable governed AI segment analysis and strategic propagation.** Maya can propose segment definitions, synthesize needs and behaviors, estimate size and economics, analyze channel presence and price sensitivity, recommend target segments, and explain how approved segment choices should change the 4Ps, positioning, objectives, budgets, campaigns, content, channels, journeys, Sales handoffs, experiments, and measurement.

### 2. Current context

- `MarketingAgentAnalysisService` already uses the shared `IAgentReasoningGateway`, accessible company knowledge, Marketing objectives, Sales campaigns, content, and channel observations.
- `AgentCapabilityCatalog` already includes Marketing audience intelligence as a recommendation capability.
- Prompt 3A adds first-class segment definitions, versions, size/economic estimates, scorecards, target decisions, and downstream impact visibility.
- Prompt 2 adds versioned Marketing strategy and Prompt 3 adds evidence-backed market/customer/competitor intelligence.
- Maya currently cannot create or commit typed segment proposals or produce structured downstream strategy impact recommendations.

### 3. Dependencies

- Prompts 1–3 and 3A.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Extend the existing Marketing audience-intelligence capability rather than creating a separate AI stack. Add explicit read/recommend tools such as `marketing.read_segments`, `marketing.read_segment_evidence`, `marketing.prepare_segmentation`, `marketing.analyze_segment`, `marketing.recommend_target_segments`, and `marketing.assess_segment_strategy_impact`.
- Add structured reasoning contracts for:
  - Proposed segmentation basis and mutually understandable segment definitions.
  - Needs, jobs, barriers, buying criteria, behavior, channel presence, and price-sensitivity analysis.
  - Size, growth, reachability, economics, and confidence estimates with method and assumptions.
  - Attractiveness dimensions, risks, missing evidence, and alternative interpretations.
  - Target/non-target recommendations with rationale and expected impact.
  - Segment-specific Product, Price, Place, Promotion, positioning, objective, budget, campaign, content, lifecycle, Sales handoff, experiment, and measurement implications.
- Build bounded company-scoped context using approved segment evidence, market/customer intelligence, competitor evidence, company knowledge, Marketing observations, Sales outcomes, and Support themes through Application contracts. Do not reference sibling capability implementations.
- Require a source ID for factual claims and explicit `observed`, `estimated`, `inferred`, or `assumption` classification for material claims.
- Require estimates to include method, range, units, period, geography, currency where relevant, confidence, and missing evidence. Reject false precision and structurally invalid output.
- Keep deterministic attractiveness computation and eligibility/fairness policy outside the model. Maya may explain and recommend but must not override policy or approval.
- Support previewing segment proposals and comparisons, then committing an accepted proposal as a draft segment/version through a separate guarded idempotent command.
- Support an explicit target-selection recommendation followed by a distinct authorized approval command. Maya must not activate target segments.
- Add a strategy-impact assessment that compares an approved segment version with active downstream artifacts, identifies material conflicts or stale assumptions, and creates review proposals/tasks without rewriting those artifacts.
- Persist orchestration run, prompt/capability versions, evidence, claims, confidence, missing evidence, proposal, target recommendation, impact assessment, and commit audit evidence.
- Extend the Segments and Strategy UI with `Ask Maya`, proposal comparison, evidence inspection, target recommendation, impact preview, and `Create draft`/`Request review` actions. Reuse the Prompt 3A reference unless this is a significant redesign; if significant, generate `docs/design/references/marketing-segment-analysis-reference.png` first.

### 5. Constraints and preservation rules

- Use the shared reasoning gateway and agent tool guardrails; no direct model-provider calls.
- Do not infer or expose individual sensitive traits, create unlawful segments, infer consent, or automatically assign people to strategic segments.
- Do not treat model-generated size, channel, or price estimates as authoritative observations.
- Do not automatically activate target segments, change pricing, reallocate budgets, rewrite strategy, launch campaigns, contact customers, or modify Sales state.
- Do not silently propagate a changed segment definition into downstream records; use version references and explicit review/commit actions.
- Preserve current audience-intelligence analysis behavior and capability IDs where compatibility permits; version contracts and prompts explicitly when behavior changes.

### 6. Acceptance criteria

- **Given** sufficient grounded customer and market evidence, **when** Maya proposes segments, **then** each material claim has a source/classification and each estimate has method, range, period, and confidence.
- **Given** weak evidence for price sensitivity or channel presence, **when** Maya analyzes a segment, **then** she reports the gap and proposes research or experiments instead of presenting invented certainty.
- **Given** a high deterministic attractiveness score, **when** Maya recommends a primary target, **then** the recommendation remains separate from target activation and exposes risks and alternatives.
- **Given** an approved target-segment change, **when** impact assessment runs, **then** affected 4P choices, objectives, campaigns, content, channels, journeys, Sales handoffs, experiments, budgets, and reports are listed with review reasons.
- **Given** an accepted proposal and repeated idempotency key, **when** draft commit retries, **then** only one segment version is created.
- **Given** malformed, unsafe, discriminatory, unsupported, or insufficiently grounded model output, **when** validation runs, **then** no draft or target decision is committed and the result safely requires review.

### 7. Verification

- Add structured-output schema, prompt/context construction, range/unit/currency, source classification, and false-precision tests.
- Add grounding, missing-evidence, prompt-injection, sensitive/proxy-attribute, privacy, fairness, and content-safety tests.
- Add idempotent proposal commit, target approval separation, downstream impact, audit, authorization, and tenant-isolation integration tests.
- Add tests proving Maya cannot activate segments, rewrite strategy, launch campaigns, spend, contact people, or mutate Sales state.
- Add UI tests for proposal comparison, source detail, confidence, missing evidence, target recommendation, impact preview, guarded commit, and failure states.
- Build API and Web and run shared-agent AI, Marketing, strategy, approval, Sales handoff, and Web test suites.

### 8. Definition of done

Maya can deliver grounded, structured customer-segmentation analysis and explicit downstream strategy-impact recommendations through shared orchestration, while deterministic policy, approval, versioning, and human/authorized commands remain authoritative. No automatic target activation, unsupported estimate, discriminatory segment, inferred consent, hidden downstream rewrite, or incomplete failure state remains.

---

## Prompt 4 — Let Maya generate grounded strategies and competitive analyses

### 1. Title and outcome

**Enable governed AI strategy and intelligence proposals.** Maya can use approved company knowledge, Marketing records, Sales evidence, and intelligence records to produce structured, source-linked strategy proposals and competitive analyses without silently committing them.

### 2. Current context

- `MarketingAgentAnalysisService` uses the shared `IAgentReasoningGateway` and supports seven analysis types.
- The current prompt is recommendation-only and returns structured claims, priorities, sources, missing evidence, and next actions.
- The shared orchestration, capability catalog, runtime profile, audit, and tool-guard systems already exist.
- Prompts 2, 3, 3A, and 3B add first-class strategy, intelligence, customer-segmentation, and segment-analysis capabilities.

### 3. Dependencies

- Prompts 1–3, 3A, and 3B.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add structured Marketing recommendation contracts for strategy proposal, 4P/7P assessment, STP, approved target-segment version references, positioning, market/customer synthesis, competitor comparison, SWOT, Five Forces, assumptions, risks, citations, and missing evidence.
- Require every material 4P, positioning, channel-mix, objective, and budget recommendation to identify the approved target segment(s) it serves and the segment evidence that supports it.
- Extend Marketing capability manifests and Maya's template with explicit read/recommend tools only.
- Build bounded company-scoped context from strategy, objectives, plans, intelligence, observations, Sales outcomes, Support themes through Application contracts, and accessible knowledge.
- Do not directly reference sibling capability implementation projects.
- Require source IDs for factual claims and explicit classifications for estimates and inference.
- Validate the model's structured output before returning or persisting it. Invalid or insufficiently grounded output must become a safe needs-review result.
- Support previewing a proposal and committing an accepted proposal as a strategy draft through a separate guarded command with stable idempotency.
- Persist orchestration run metadata, prompt/capability versions, rationale summary, claims, sources, confidence, missing evidence, and commit audit evidence.
- Extend the strategy and intelligence UI with `Ask Maya`, proposal comparison, source inspection, and `Create draft` actions. Do not let the model activate or approve a strategy.

### 5. Constraints and preservation rules

- Use the shared reasoning gateway; no direct model-provider calls from Marketing.
- Do not pass unrelated company data or hidden instructions into prompts.
- Do not commit provider output without deterministic validation and an explicit command.
- No campaign launch, publishing, contact, spending, or Sales mutation is permitted.
- Preserve current Marketing analysis types and API behavior.

### 6. Acceptance criteria

- **Given** sufficient grounded sources, **when** Maya prepares a strategy, **then** each factual claim references accessible company-scoped evidence.
- **Given** missing pricing or customer evidence, **when** a 4P strategy is generated, **then** the result identifies the gap and does not invent authoritative values.
- **Given** malformed or unsupported model output, **when** validation runs, **then** no strategy draft is created and an operator-visible needs-review result is returned.
- **Given** an accepted proposal and idempotency key, **when** `Create draft` is invoked twice, **then** only one strategy draft is created.
- **Given** a user without Marketing mutation authority, **when** draft commit is attempted, **then** it is denied even if Maya recommended it.

### 7. Verification

- Add structured-output validation and prompt/context-construction tests.
- Add grounding, missing-evidence, source-access, prompt-injection resistance, and content-safety tests.
- Add idempotent commit, authorization, audit, and tenant-isolation integration tests.
- Add UI tests for loading, needs-review, source detail, comparison, commit, and failure states.
- Build API and Web and run shared-agent AI and Marketing test suites.

### 8. Definition of done

Maya produces production-grade, validated, grounded proposals and can create only explicit reviewable drafts. No direct provider call, unbounded context, unsupported claim, or implicit activation remains.

---

## Prompt 5 — Decompose approved strategy into programs, campaigns, activities, and ownership

### 1. Title and outcome

**Turn strategy into executable Marketing work.** Maya and users can decompose an approved strategy into programs, existing Sales campaigns, activities, milestones, owners, budgets, dependencies, metrics, and approval requirements.

### 2. Current context

- Marketing plans can link to objectives.
- Sales campaigns already support initiative configuration, offers, activities, audiences, scheduling, approval policy, readiness gaps, execution modes, and lifecycle state.
- Campaign planning and scheduling services exist in `VirtualCompany.Infrastructure.Sales`.
- Marketing content briefs can link to a Marketing plan and Sales campaign.
- Work tasks, workflows, and approval tasks already exist and should represent durable operational state.

### 3. Dependencies

- Prompts 1, 2, 3A, 3B, and 4.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a Marketing program concept only if the current plan/campaign model cannot represent the hierarchy without ambiguity; choose the narrowest model after inspecting current entities.
- Add a structured decomposition proposal that links strategy outcomes to objectives, plans/programs, Sales campaigns, activities, content needs, audiences, owners, budgets, metrics, guardrails, and dependencies.
- Require every program and campaign to reference an approved target-segment version and derive its operational audience through explicit narrowing criteria, consent, eligibility, and exclusions.
- Reuse `SalesCampaign`, campaign activities, scheduling, readiness, and approval behavior rather than creating a duplicate Marketing campaign engine.
- Add deterministic validation for date ranges, ownership, objective linkage, audience presence, currency, budget, duplicate activity, and readiness.
- Provide preview/diff and idempotent commit commands. Commit drafts only; launching remains outside scope.
- Create or link durable tasks for assigned activity work using established task and workflow semantics.
- Preserve explicit cross-capability ownership: Marketing proposes and coordinates; Sales owns Sales state and acceptance of Marketing handoffs.
- Add API and UI support for strategy-to-campaign traceability and a reviewable decomposition tree/timeline.
- Generate `docs/design/references/marketing-strategy-decomposition-reference.png` before implementing significant UI.
- Add a migration only if new persisted concepts or relationships are necessary.

### 5. Constraints and preservation rules

- Do not create a second campaign aggregate.
- Do not launch a campaign, send a message, or spend budget in this prompt.
- Do not create chat messages as substitutes for tasks, workflows, campaigns, or approvals.
- Preserve existing Sales campaign routes, lifecycle values, and readiness behavior.
- Cross-module coordination must use Application contracts or durable workflow messages.

### 6. Acceptance criteria

- **Given** an approved strategy, **when** Maya prepares a decomposition, **then** every campaign and activity traces to an objective and measurable outcome.
- **Given** an incomplete audience, schedule, owner, or offer, **when** readiness is evaluated, **then** explicit plain-English gaps are returned.
- **Given** an accepted decomposition, **when** it is committed twice with the same business key, **then** no duplicate campaign, activity, or task is created.
- **Given** a Marketing campaign proposal requiring approval, **when** committed, **then** it remains in a pre-launch state.
- **Given** a cross-company objective or owner, **when** commit is attempted, **then** the entire command fails atomically.

### 7. Verification

- Add domain and application tests for hierarchy, readiness, traceability, validation, and idempotency.
- Add integration tests covering existing Sales campaign compatibility, task linkage, tenant isolation, and authorization.
- If schema changes, add and inspect an EF migration and validate SQL Server paths.
- Add Web tests for preview, diff, gaps, traceability, error states, and responsive layout.
- Build affected projects and run Marketing, campaign, workflow, and Web tests.

### 8. Definition of done

An approved strategy can become complete, persistent, reviewable operational work using existing campaign and workflow foundations. No duplicate campaign engine, launch shortcut, fake tasks, or deferred hierarchy behavior remains.

---

## Prompt 6 — Generate grounded content briefs and text variants

### 1. Title and outcome

**Make Maya a governed content-production partner.** Maya can create evidence-backed content briefs and versioned text variants for websites, landing pages, articles, social posts, email, ads, webinars, video scripts, case studies, and Sales enablement.

### 2. Current context

- `MarketingContentBrief` and `MarketingContentVariant` already persist campaign/plan linkage, audience, channel, language, tone, CTA, sources, AI-generation flag, status, and lifecycle.
- Deterministic content preflight already checks review readiness, and submitted content creates approval work.
- `MarketingAgentAnalysisService` already retrieves approved company knowledge for content advice.
- Current generation returns advice but does not produce typed, channel-specific content variants.

### 3. Dependencies

- Prompts 1, 3A, 3B, and 4; Prompt 5 is required for campaign-linked generation.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Extend content briefs with measurable objective, funnel stage, customer insight, key message, supporting points, offer, required/prohibited claims, SEO requirements, visual direction, desired formats, variant requirements, evidence requirements, and approval policy while preserving existing fields.
- Link each campaign/content brief to an approved segment version and include relevant needs, behavior, channel-presence, and price-sensitivity implications without copying the entire segment record.
- Add typed content-generation requests and results for supported formats and channels.
- Use shared reasoning and accessible company knowledge; include only bounded relevant sources.
- Require citations/evidence for factual claims and mark generated variants with model/capability/prompt version and source references.
- Add deterministic policies for unsupported claims, missing evidence, prohibited terms, regulated content, language, CTA, consent implications, and brand requirements.
- Generate multiple versioned variants without overwriting approved content.
- Add human edit, compare, request changes, submit, approve/reject, and retire behavior.
- Preserve idempotency for repeated generation and commit requests.
- Extend the existing Marketing Content UI with brief completeness, generation controls, variant comparison, evidence, preflight issues, and approval state.
- Generate `docs/design/references/marketing-content-studio-reference.png` before significant UI changes.
- Add a migration if the extended brief and generation metadata require schema changes.

### 5. Constraints and preservation rules

- Do not publish or send content in this prompt.
- Do not silently fabricate product facts, testimonials, statistics, prices, or competitor claims.
- Do not weaken the existing submit-before-review lifecycle or deterministic preflight.
- Do not store unlimited prompt context or hidden model reasoning.
- Preserve existing content API compatibility where possible.

### 6. Acceptance criteria

- **Given** an evidence-complete brief, **when** Maya generates three LinkedIn variants, **then** three separately versioned drafts with sources and generation metadata are stored.
- **Given** a requested factual claim without evidence, **when** generation/preflight runs, **then** the claim is omitted or clearly blocked and review is required.
- **Given** an approved variant, **when** a new generation is requested, **then** the approved version remains unchanged and auditable.
- **Given** repeated commit with the same idempotency key, **when** retries occur, **then** no duplicate variants are created.
- **Given** another company's brief ID, **when** generation is requested, **then** the operation is denied without leakage.

### 7. Verification

- Add brief validation, lifecycle, versioning, claim-policy, grounding, and content-safety tests.
- Add shared-reasoning structured-output and prompt-injection tests.
- Add API integration tests for authorization, tenant isolation, idempotency, approval, and failure paths.
- Add UI tests for generation, editing, comparison, citations, preflight, empty, loading, and error states.
- Validate any migration and build API and Web.

### 8. Definition of done

Maya creates real, persisted, grounded, reviewable text variants across defined formats with complete lifecycle, evidence, audit, API, UI, and tests. No publishing, fake citations, overwritten approvals, or unfinished content states remain.

---

## Prompt 7 — Add governed AI creative-image production and asset lifecycle

### 1. Title and outcome

**Enable Maya to produce reviewable visual assets.** Maya can turn an approved visual brief into versioned draft campaign images with provenance, brand review, accessibility metadata, approval state, and safe asset storage.

### 2. Current context

- Marketing content briefs have a natural place for visual direction after Prompt 6.
- Virtual Company has document/knowledge concepts but no confirmed Marketing creative-asset lifecycle in the current implementation.
- The shared AI architecture forbids creating a separate Marketing orchestration stack.
- Content submission and approval behavior already provides a pattern for reviewable Marketing work.

### 3. Dependencies

- Prompts 1, 6, and 8's policy contracts if Prompt 8 has already been completed; otherwise implement only the minimum deterministic brand and safety policy needed here and allow Prompt 8 to consolidate it without duplication.
- An approved image-generation provider configuration and credentials are required for external generation verification. Missing credentials must produce a safe operator-visible unavailable state, not mock images.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a tenant-owned Marketing creative asset and asset-version lifecycle linked to content brief, campaign, variant, format, dimensions, language, owner, and approval state.
- Persist generation request summary, prompt version, model/provider reference, source/reference asset IDs, brand profile version, safety result, alt text, file metadata, checksum, creation time, and audit reference.
- Store binary assets through the established file/document/blob abstraction after inspecting the repository; do not place large binary content in relational columns.
- Add a shared-orchestration capability/tool for preparing a visual prompt and a provider adapter for generation. Marketing must not call the provider directly.
- Validate supported formats, dimensions, file type, size, malware/safety results, privacy constraints, and company ownership.
- Add brand, likeness, copyright/provenance, prohibited-content, factual-visualization, and accessibility preflight.
- Support generate, regenerate as a new version, upload human-created alternative, compare, edit metadata, submit, approve/reject, retire, and download/preview.
- Add a Creative area within the Marketing Content workspace, following screenshot-first design with `docs/design/references/marketing-creative-studio-reference.png`.
- Add an EF migration for asset metadata and lifecycle state.

### 5. Constraints and preservation rules

- Never overwrite approved assets in place.
- Never use a generated visual as published content without approval.
- Do not expose provider credentials, raw safety payloads, or sensitive prompts.
- Do not claim copyright ownership or factual accuracy that is not established.
- Provider errors and unavailable configuration must be safe and actionable.

### 6. Acceptance criteria

- **Given** a complete approved visual brief, **when** Maya generates a draft, **then** the asset, version, provenance, safety state, and alt text are persisted.
- **Given** a regeneration request, **when** it completes, **then** a new immutable version exists and the prior version remains available.
- **Given** failed safety or brand preflight, **when** submission is attempted, **then** it is blocked with a plain-English reason.
- **Given** missing provider credentials, **when** generation is requested, **then** no fake asset is created and the user sees an actionable configuration state.
- **Given** a cross-company asset reference, **when** it is used as input, **then** the request is denied.

### 7. Verification

- Add domain lifecycle, versioning, checksum, metadata, and policy tests.
- Add tenant-isolation, authorization, provider-failure, idempotency, safety, and approval integration tests.
- Use a fake provider only in automated tests; do not expose it as production fallback.
- Validate migration/model state and storage cleanup behavior without destructive broad deletion.
- Add UI tests and visually compare the implementation with the saved reference.
- Run external provider verification only when explicitly configured and categorized.

### 8. Definition of done

Creative generation is a production, versioned, governed asset workflow with real provider integration boundaries, storage, safety, approval, audit, UI, and tests. No static mock assets, silent provider fallback, or unreviewed publication path exists.

---

## Prompt 8 — Implement authoritative Marketing governance policies and approvals

### 1. Title and outcome

**Centralize Marketing decision governance.** Every sensitive Marketing action receives a deterministic allowed/denied decision, stable reason, plain-English explanation, evidence, approval requirement, and audit trail before Maya or a user can proceed.

### 2. Current context

- Existing content preflight checks deterministic readiness and creates a Marketing approval task.
- Sales campaigns already support approval-required state and readiness gaps.
- The platform provides approval requests/chains, Work approvals, audit, tool guardrails, and workflow state.
- Maya's current analysis is recommend-only, but later prompts need governed publication, contact, launch, audience, and spend actions.

### 3. Dependencies

- Prompts 1, 2, 5, and 6. Prompt 7 should consume these policies when present.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Define narrow Marketing policies for strategy activation, campaign launch, audience activation, outbound communication, content publication, paid spend/budget change, tracking change, regulated claims, brand safety, consent/suppression, contact pressure, and destructive actions.
- Add segmentation-specific policy for disallowed sensitive/proxy criteria, fairness and discrimination risk, minimum evidence, target-selection approval, segment-version changes, and use of segment membership in campaign targeting.
- Return structured decisions containing allowed, stable reason code, plain-English explanation, approval/review requirement, required role/chain, and evidence.
- Configure conservative defaults. Missing policy/configuration/evidence must fail safely rather than allow execution.
- Reuse approval requests/chains and Work approval surfaces. Do not create Marketing-only approval infrastructure.
- Support rejection, request changes where established, cancellation, expiration, superseded target versions, and immediate pre-execution recheck.
- Tie approval to the immutable target/action/version so a changed payload invalidates stale approval.
- Persist business audit evidence for request, decision, approval, rejection, override, expiration, and execution recheck.
- Expose policy previews to APIs and show plain-English policy and approval state in Marketing and Work UI.
- Extend existing approval UI only as needed; if the change is significant, generate `docs/design/references/marketing-approval-detail-reference.png` first.

### 5. Constraints and preservation rules

- UI visibility or disabled buttons are not authorization.
- Prompt instructions are not policies.
- Do not create one generic policy engine for unrelated Marketing rules; use domain-specific policies with shared decision primitives where already established.
- Do not weaken existing content or campaign approval behavior.
- Approval does not bypass consent, authorization, permanent validation failures, or company isolation.

### 6. Acceptance criteria

- **Given** an approved content version, **when** the body changes, **then** the old publication approval cannot authorize the new version.
- **Given** a contact without valid communication permission, **when** an approver approves outbound delivery, **then** execution remains denied.
- **Given** a spend request above threshold, **when** policy is evaluated, **then** the required approval chain and evidence are returned.
- **Given** an expired or rejected approval, **when** execution is attempted, **then** it is blocked and audited.
- **Given** a valid low-risk internal draft action, **when** policy is evaluated, **then** it may proceed without external-action approval according to configured autonomy.

### 7. Verification

- Add pure deterministic policy tests for every allow, deny, review, threshold, missing-configuration, and version-change path.
- Add approval-chain, authorization, tenant-isolation, expiration, cancellation, and recheck integration tests.
- Add UI/presenter tests for explanations, evidence, state, and actions without raw identifiers.
- Run existing approval, campaign, content, agent guardrail, API, and Web tests.

### 8. Definition of done

All named sensitive Marketing actions are governed by authoritative backend policy and established approval infrastructure with complete recheck, audit, UI explanation, and tests. No prompt-only or UI-only safety decision remains.

---

## Prompt 9 — Build provider-neutral Marketing channel connections and durable action delivery

### 1. Title and outcome

**Create the safe channel-integration foundation.** Virtual Company can connect Marketing providers, prepare provider-independent actions, preview mapped payloads, approve immutable versions, dispatch them durably, and reconcile delivery without embedding provider schemas in core Marketing models.

### 2. Current context

- Integration adapters are required to remain outside core domain behavior.
- Durable company outbox, background workers, audit, approval, correlation, and retry patterns already exist.
- Support reply delivery provides an established approval-backed outbound dispatcher pattern.
- Marketing currently records normalized channel observations but has no provider-neutral outbound channel-action lifecycle.

### 3. Dependencies

- Prompts 1, 6, and 8.
- At least one real provider adapter is delivered by Prompts 10–12; this prompt must still deliver a complete connection/action foundation without pretending a provider is connected.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add Application contracts and tenant-owned persistence for Marketing channel connections, capabilities, connection health, channel destinations/accounts, proposed actions, immutable action versions, approval linkage, dispatch attempts, provider references, retry state, and reconciliation state.
- Model provider-independent action types such as publish post, schedule post, update/cancel scheduled post where supported, and request paid-media action. Do not assume every provider supports every action.
- Require channel plans and proposed-action rationale to reference the approved segment version and relevant channel-presence/reachability evidence when an action is segment-targeted.
- Isolate provider authentication, refresh, payload mapping, API endpoints, rate-limit translation, and raw error handling in adapters.
- Store secrets using the established protected connection/secret mechanism; never persist or log tokens in normal Marketing records.
- Add connection lifecycle, permission/capability discovery, safe disconnect, health checks, and operator-visible reauthorization state.
- Add prepare/preview/submit/approve/dispatch/reconcile/cancel lifecycle with stable idempotency derived from company, provider, destination, business action, target version, and schedule.
- Enqueue approved external actions and dispatch in a background worker. Classify retryable, permanent, authorization, validation, rate-limit, and ambiguous outcomes.
- Recheck company authorization, connection, policy, consent where applicable, target version, and approval immediately before dispatch.
- Normalize provider results and channel observations.
- Add connection settings and action-delivery status UI using existing Settings and Marketing/Work surfaces. Generate `docs/design/references/marketing-channel-connections-reference.png` before significant UI work.
- Add an EF migration and relevant configuration/options validation.

### 5. Constraints and preservation rules

- Do not implement a fake production provider or generic webhook that bypasses an adapter.
- Do not call providers in controllers or synchronous request handlers.
- Do not leak provider payloads into Domain entities or public UI errors.
- Do not treat timeout/unknown provider outcome as failure eligible for blind retry.
- Preserve local and Docker SQL Server compatibility.

### 6. Acceptance criteria

- **Given** a complete approved action, **when** it is submitted, **then** a durable immutable delivery request is created and the API returns before external dispatch.
- **Given** duplicate dispatcher delivery, **when** the same business idempotency key is processed, **then** the provider is not intentionally invoked twice.
- **Given** an ambiguous provider timeout, **when** dispatch returns, **then** the action enters reconciliation and is not marked successful or blindly retried.
- **Given** revoked credentials, **when** dispatch is attempted, **then** the action stops with an actionable reauthorization state and no secret leakage.
- **Given** an edited content version after approval, **when** dispatch is claimed, **then** delivery is blocked pending new approval.

### 7. Verification

- Add lifecycle, idempotency, claim/concurrency, retry classification, reconciliation, secret-safety, and mapping tests.
- Add authorization, tenant-isolation, approval-recheck, cancellation, and background-worker integration tests.
- Use contract-test fake adapters only in tests; verify no production fallback registration exists.
- Add migration/model checks and Web settings/status tests.
- Build all affected capability, API, worker, and Web projects.

### 8. Definition of done

The provider-neutral channel foundation is fully persistent, authorized, approval-backed, durable, idempotent, reconcilable, observable, and operable. It accurately reports that no channel is available until a real adapter and valid connection exist.

---

## Prompt 10 — Implement the LinkedIn Marketing channel adapter

### 1. Title and outcome

**Connect LinkedIn as the first real Marketing publishing channel.** Authorized companies can connect supported LinkedIn destinations, preview and approve posts, publish or schedule supported content through durable delivery, and import normalized results.

### 2. Current context

- Prompt 9 provides provider-neutral connections and action delivery.
- Marketing content variants provide approved source content and creative assets may be available from Prompt 7.
- No confirmed LinkedIn provider adapter exists in the current repository.

### 3. Dependencies

- Prompts 6, 8, and 9; Prompt 7 for image posts.
- Real LinkedIn application credentials, redirect URLs, permissions, and test organization/account access are required for external verification. If unavailable, implement and verify all local behavior with official-contract fixtures and report the external test blocker without fabricating success.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Verify current official LinkedIn API requirements before implementation and document required scopes, review requirements, supported destination types, media flow, scheduling limitations, and rate limits.
- Implement OAuth/connection lifecycle through established secret handling.
- Discover only destinations the authenticated identity is authorized to manage.
- Map supported provider-neutral post actions to LinkedIn payloads inside the adapter.
- Validate text/media limits, destination permission, scheduled time, asset readiness, and unsupported feature combinations before approval.
- Implement upload/publish/status lookup and provider-reference persistence as supported by the official API.
- Translate provider errors into safe retry, permanent, reauthorization, rate-limit, or reconciliation outcomes.
- Import normalized delivery state and available engagement observations without making unsupported attribution claims.
- Add Settings connection UI, destination selection, post preview, delivery state, and reauthorization guidance using existing surfaces.

### 5. Constraints and preservation rules

- Do not scrape LinkedIn or automate a browser session as the production integration.
- Do not claim support for personal/company publishing, media types, or scheduling that the authorized API does not provide.
- Do not store access tokens in Marketing tables or logs.
- Preserve provider-neutral core contracts and avoid LinkedIn conditionals outside the adapter/composition boundary.

### 6. Acceptance criteria

- **Given** a valid authorized LinkedIn connection, **when** destinations are refreshed, **then** only manageable destinations are stored and displayed.
- **Given** an approved supported post, **when** the worker dispatches it, **then** the provider reference and normalized delivery state are persisted once.
- **Given** invalid media or an unsupported action, **when** preview/preflight runs, **then** approval and dispatch are blocked before an API call.
- **Given** revoked authorization, **when** refresh or dispatch occurs, **then** the connection requires reauthorization and queued actions fail safely.
- **Given** an unknown publish result, **when** reconciliation runs, **then** status lookup is used before any retry.

### 7. Verification

- Add adapter mapping, limits, authentication error, rate-limit, retry, and reconciliation tests using sanitized fixtures.
- Add connection, destination authorization, tenant-isolation, approval-recheck, and worker integration tests.
- Add Web client/component tests for connect, preview, state, and reauthorization.
- Run an explicitly categorized external sandbox/test-account verification when credentials are available.

### 8. Definition of done

LinkedIn is a real, documented, provider-isolated production adapter using durable approved delivery and normalized results. No browser automation, mock production connection, unsupported capability claim, or secret leakage remains.

---

## Prompt 11 — Implement the Meta Facebook and Instagram Marketing channel adapter

### 1. Title and outcome

**Connect supported Meta destinations.** Authorized companies can connect eligible Facebook Pages and Instagram professional destinations, preview and approve supported content, publish through durable delivery, and ingest normalized delivery/performance state.

### 2. Current context

- Prompt 9 provides provider-neutral connection/action infrastructure.
- Prompt 10 establishes the first concrete adapter pattern, but Meta must remain independently implemented and tested.
- Marketing content and creative assets are available through Prompts 6 and 7.

### 3. Dependencies

- Prompts 6–9. Prompt 10 is a pattern reference, not a code dependency.
- Real Meta application configuration, permissions/app review, redirect URLs, and test Page/Instagram professional account are required for external verification.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Verify current official Meta Graph API requirements, supported destination relationships, permissions, media/container flow, publishing limitations, token lifecycle, and rate limits.
- Implement secure connection, reauthorization, destination discovery, and capability discovery through the established integration boundary.
- Support only officially available Facebook/Instagram publishing actions and media combinations.
- Map provider-neutral actions and assets inside the Meta adapter; keep Graph payloads out of core entities.
- Validate destination relationship, media availability, format/aspect constraints where authoritative, caption/text constraints, schedule, and approval version before dispatch.
- Implement asynchronous media/container status handling and reconciliation where required.
- Classify and expose safe permission, validation, rate-limit, transient, permanent, and ambiguous outcomes.
- Import normalized publication and available performance observations with source references and freshness.
- Add connection settings, destination selection, Meta-specific preview warnings, delivery state, and reauthorization guidance.

### 5. Constraints and preservation rules

- Do not automate consumer accounts or use browser/session scraping.
- Do not imply that all Facebook or Instagram account types are supported.
- Never log tokens, raw sensitive payloads, or personal data not needed for the business action.
- Do not bypass the provider-neutral approval, outbox, idempotency, and reconciliation lifecycle.

### 6. Acceptance criteria

- **Given** a valid Meta connection, **when** destinations are discovered, **then** only eligible manageable destinations and supported capabilities are shown.
- **Given** an approved supported asset/post, **when** it is dispatched, **then** media/container and publication references are tracked to a terminal normalized state.
- **Given** an unsupported account or media combination, **when** preflight runs, **then** the action is blocked with actionable guidance.
- **Given** a rate limit, **when** dispatch fails, **then** retry follows provider guidance and remains bounded.
- **Given** an ambiguous container/publication result, **when** reconciliation runs, **then** status is queried before retry or final classification.

### 7. Verification

- Add sanitized contract-fixture tests for payload mapping, media flow, capability discovery, token errors, limits, retry, and reconciliation.
- Add tenant-isolation, authorization, approval-recheck, worker concurrency, and idempotency integration tests.
- Add UI tests for connection, destination capability, preview warnings, delivery state, and errors.
- Run categorized external verification when valid test credentials are available.

### 8. Definition of done

Meta publishing is implemented through a real, provider-isolated, approval-backed, durable adapter with accurate capability discovery and safe media reconciliation. No fake provider, consumer-account automation, secret exposure, or unsupported success state remains.

---

## Prompt 12 — Implement the X Marketing channel adapter

### 1. Title and outcome

**Connect X as a governed Marketing channel.** Authorized companies can connect supported X accounts, preview and approve supported posts, publish through durable delivery, and import normalized results according to the company's available API tier.

### 2. Current context

- Prompt 9 provides provider-neutral connections and durable actions.
- Prompts 10 and 11 implement independent provider adapters.
- X capability, access tiers, limits, and commercial availability can change and must be verified against current official documentation during implementation.

### 3. Dependencies

- Prompts 6, 8, and 9; Prompt 7 for media posts.
- Real X developer credentials and a permitted test account/tier are required for external verification.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Verify current official X API authentication, access tier, write permissions, media support, rate limits, scheduling availability, and usage/cost constraints.
- Implement secure connection and reauthorization using established secret handling.
- Discover and display the authenticated destination identity and only capabilities actually available to that connection/tier.
- Map supported provider-neutral actions inside the X adapter.
- Validate text, media, reply/thread behavior if supported, destination, approval version, and schedule before dispatch.
- Implement publication and status/reconciliation behavior supported by the official API.
- Classify access-tier, quota, rate-limit, authorization, validation, transient, permanent, and ambiguous outcomes.
- Import normalized publication and available engagement observations with source and freshness metadata.
- Add connection, capability, cost/limit warning, preview, delivery state, and reauthorization UI.

### 5. Constraints and preservation rules

- Do not use browser automation, stored user sessions, scraping, or unofficial APIs.
- Do not claim scheduling, analytics, threads, media, or write access unless the connected tier supports them.
- Do not silently incur paid API usage outside configured operator policy and visibility.
- Preserve provider-neutral action and delivery semantics.

### 6. Acceptance criteria

- **Given** a connected account/tier, **when** capabilities are refreshed, **then** only supported actions are enabled.
- **Given** an approved supported post, **when** dispatched, **then** one provider reference and normalized outcome are persisted.
- **Given** missing write access or exhausted quota, **when** preview or dispatch occurs, **then** the action stops with actionable guidance.
- **Given** ambiguous delivery, **when** reconciliation is possible, **then** provider status is checked before retry.
- **Given** no configured X credentials, **when** the settings page loads, **then** it reports unavailable configuration without mock connectivity.

### 7. Verification

- Add mapping, tier capability, quota, authentication, validation, retry, and reconciliation adapter tests.
- Add tenant-isolation, authorization, approval-recheck, idempotency, and worker tests.
- Add UI tests for tier/capability messaging, preview, state, and errors.
- Run categorized external verification only with approved credentials and cost controls.

### 8. Definition of done

X is implemented as an accurate tier-aware official API adapter with secure connection, governed durable publishing, normalized results, and complete failure behavior. No unofficial automation, hidden cost behavior, or unsupported capability claim remains.

---

## Prompt 13 — Add lifecycle marketing and governed CRM journeys

### 1. Title and outcome

**Enable lifecycle Marketing without duplicating Sales or mailbox systems.** Maya can design and operate reviewable journeys for nurture, onboarding, trial activation, adoption, renewal support, advocacy, referral, re-engagement, and event follow-up under consent and contact-pressure policy.

### 2. Current context

- Marketing qualification definitions/evaluations and Marketing-to-Sales handoffs already exist.
- Sales owns prospects, leads, deals, campaigns, and email-ingestion behavior.
- Mailbox and Support have established outbound communication boundaries.
- Workflow supports scheduled, event, retry, escalation, blocked, and exception transitions.
- No first-class lifecycle journey model was confirmed in the current Marketing implementation.

### 3. Dependencies

- Prompts 1, 5, 6, 8, and 9. A concrete delivery provider is required before real outbound journey steps can execute.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a tenant-owned lifecycle journey definition and version model with audience eligibility, entry/exit criteria, steps, waits, goals, guardrails, consent requirements, suppression rules, frequency caps, owner, status, and validity period.
- Link journeys to approved segment versions and derive operational entry criteria explicitly; record the segment need, behavior, channel, timing, and value hypothesis the journey is intended to serve.
- Model journey instances and step state as durable workflow state, not UI or chat state.
- Support draft, preview/sample audience, validate, submit, approve, activate, pause, resume, complete, cancel, and supersede.
- Integrate with Marketing qualification and Sales handoff through Application contracts; do not mutate Sales state from Marketing implementations.
- Re-evaluate consent, suppression, eligibility, frequency, approval, and target version before every outbound step.
- Make inbound events and repeated polling idempotent per company, journey, contact, event, and version.
- Persist safe step outcomes, failures, exits, conversions, and audit evidence.
- Add journey list/detail, builder/review, eligibility preview, active-state, and exception UI. Generate `docs/design/references/marketing-lifecycle-journeys-reference.png` first.
- Add an EF migration and background/workflow coordination.

### 5. Constraints and preservation rules

- Do not build a second CRM, mailbox, campaign dispatcher, or generic workflow engine.
- Do not enter a contact without current authoritative eligibility and consent.
- Do not infer consent from engagement.
- Do not duplicate messages after retries, polling, webhook replay, or worker concurrency.
- Preserve Sales ownership of lead/deal state and Support ownership of support communication.

### 6. Acceptance criteria

- **Given** an eligible consented contact, **when** an approved journey activates, **then** one durable journey instance is created for the applicable version.
- **Given** a suppressed or opted-out contact, **when** an outbound step becomes due, **then** it exits or blocks according to policy without delivery.
- **Given** duplicate entry events or worker claims, **when** processed, **then** no duplicate instance or message is produced.
- **Given** a Marketing-qualified outcome, **when** Maya proposes a Sales handoff, **then** Sales must still accept it before Sales state changes.
- **Given** a journey version change, **when** active instances continue, **then** version behavior is explicit and auditable rather than silently mutated.

### 7. Verification

- Add lifecycle, versioning, eligibility, consent, suppression, frequency, and transition tests.
- Add workflow, worker-concurrency, webhook/event replay, idempotency, authorization, and tenant-isolation integration tests.
- Add provider-dispatch contract tests for outbound steps and safe failures.
- Validate migration/model state and add comprehensive UI tests and visual verification.

### 8. Definition of done

Lifecycle journeys are real durable, versioned, consent-aware workflows with governed delivery, exception visibility, Sales handoffs, audit, API, UI, and tests. No duplicate CRM/workflow/mailbox system or mock journey execution remains.

---

## Prompt 14 — Close the measurement, attribution, and experiment-learning loop

### 1. Title and outcome

**Give Maya evidence-based performance management.** Normalize channel and business observations, connect Marketing activity to funnel outcomes without overstating causality, evaluate experiments, and produce actionable performance reviews.

### 2. Current context

- `MarketingChannelObservation` persists provider, metric, value, unit, period, source reference, and retrieval time with idempotency.
- `MarketingExperiment` supports draft, active, completed, hypothesis, primary/guardrail metrics, sample size, dates, and decision.
- Marketing analysis already groups recent observations and reports missing evidence.
- Sales and Marketing handoff feedback provide business-outcome signals, but no complete attribution model was confirmed.

### 3. Dependencies

- Prompts 1, 3A, 3B, 5, and 8. Prompts 9–13 provide additional real channel and lifecycle observations but are not required for internal measurement foundations.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Define a normalized Marketing metric catalog with units, aggregation semantics, supported dimensions, freshness, and source quality.
- Extend observations only where necessary to support dimensions, campaign/activity/content/action linkage, deduplication, correction/supersession, and lineage.
- Add attribution touch/evidence and attribution-result concepts that distinguish direct observation, configured attribution rule, correlation, and inference.
- Support configurable bounded attribution models appropriate to available evidence; never label correlation as causal proof.
- Add experiment exposure/sample/result records as needed, deterministic readiness/completion policy, guardrails, stopping rule, and insufficient-sample outcome.
- Add performance queries for objective progress, campaign/content/channel efficiency, funnel leakage, handoff outcomes, budget variance, and data freshness.
- Add segment-level performance and learning projections for reach, conversion, acquisition cost, pipeline, revenue, retention, lifetime value, channel response, offer/price response, evidence confidence, and changes to size or attractiveness assumptions.
- Extend Maya's performance and experiment analyses with structured claims, confidence, sources, alternative explanations, and next actions.
- Add decision-oriented UI showing what changed, why it may have changed, confidence, data used, attribution limitations, and recommended action. Generate `docs/design/references/marketing-performance-reference.png` first.
- Add migration(s) if the existing observation and experiment schema is insufficient.

### 5. Constraints and preservation rules

- Preserve existing observation and experiment wire/storage values where possible.
- Do not invent missing metrics or silently combine incompatible units or periods.
- Do not declare experiment winners before deterministic sample/time/guardrail criteria are satisfied.
- Do not present inferred attribution as observed revenue causality.
- Keep read projections separate from transactional writes.

### 6. Acceptance criteria

- **Given** duplicate provider observations, **when** ingestion repeats, **then** one normalized observation lineage is retained.
- **Given** insufficient experiment sample, **when** completion is requested, **then** the system refuses or records an explicit inconclusive outcome according to policy.
- **Given** an attribution result based on a configured model, **when** displayed, **then** its model, evidence, limitations, and confidence are visible.
- **Given** stale or missing source data, **when** Maya reviews performance, **then** she reports the gap and avoids unsupported optimization advice.
- **Given** two companies with identical external references, **when** observations are queried, **then** results remain isolated by company.

### 7. Verification

- Add metric aggregation, unit, period, dedupe, correction, attribution, experiment, and confidence tests.
- Add provider-ingestion, Sales-outcome, tenant-isolation, authorization, and failure-path integration tests.
- Add AI grounding and insufficient-evidence tests.
- Validate migrations and add read-projection performance checks for representative data volume.
- Add UI tests and visual comparison; build API and Web.

### 8. Definition of done

Marketing measurement is normalized, traceable, correction-aware, appropriately attributed, and useful for deterministic experiment decisions and grounded Maya recommendations. No fabricated metric, causal overclaim, or incomplete experiment state remains.

---

## Prompt 15 — Add event-driven Marketing operations, alerts, and executive briefings

### 1. Title and outcome

**Make Maya continuously operational.** Durable Marketing events and conditions create deduplicated tasks, analyses, alerts, and review queues for missed objectives, stale data, campaign risks, content deadlines, qualified demand, experiment milestones, audience fatigue, consent incidents, and market changes.

### 2. Current context

- `RoleAgentCadenceBackgroundService` already runs daily, weekly, and monthly grounded Marketing analysis with bounded retry attempts per window.
- Workflow supports scheduled and event-triggered progression, exceptions, retries, and deduplication.
- Work tasks, notifications, approvals, agent orchestration runs, audit, and briefing delivery already exist.
- Current cadence analysis does not cover the full event catalog described in `marketing.md`.

### 3. Dependencies

- Prompts 1, 5, 8, and 14. Other prompts add more event sources and should be integrated when present.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Define stable Marketing event contracts for objective risk, campaign threshold, content due/overdue, stale observations, qualification, Sales handoff outcome, campaign completion, experiment threshold, audience fatigue/contact pressure, consent/unsubscribe incident, brand incident, provider failure, and intelligence freshness/change.
- Add events for material changes in segment size, needs, behavior, channel presence, price sensitivity, economics, evidence freshness, attractiveness, target state, and downstream-artifact staleness.
- Publish or translate events from owning modules through Application contracts, domain events, workflow events, or durable outbox messages without sibling infrastructure references.
- Add idempotent event processing keyed by company, event type, source, target, version, and relevant occurrence window.
- Route events through deterministic severity, action, escalation, and notification policies before invoking Maya.
- Create durable tasks/workflows/notifications and invoke bounded grounded analysis only where useful.
- Persist trigger, current state, related task/approval, correlation, evidence, retry/failure, and resolution.
- Extend cadence briefs to combine top priorities without duplicating event tasks or repeatedly notifying unchanged conditions.
- Surface Maya's daily/weekly/monthly brief, needs-attention queue, event evidence, and linked action in the existing Marketing workspace, Agent team, Overview, and Work surfaces as appropriate.
- Generate `docs/design/references/marketing-operating-brief-reference.png` before significant UI changes.

### 5. Constraints and preservation rules

- Do not invoke the model as an untracked event side effect.
- Do not create duplicate tasks, analyses, notifications, or approvals during replay or polling.
- Do not notify repeatedly when state and severity have not materially changed.
- Keep event names/internal diagnostics out of user-facing language.
- Preserve existing role-agent cadence behavior and retry limits.

### 6. Acceptance criteria

- **Given** repeated delivery of the same content-overdue event, **when** processed, **then** only one open task/notification exists for the occurrence.
- **Given** an objective risk that materially worsens, **when** the next event arrives, **then** severity and evidence update without losing history.
- **Given** a provider authorization failure, **when** policy evaluates it, **then** the operator sees a reauthorization action and Maya does not propose blind retry.
- **Given** no meaningful Marketing changes, **when** cadence runs, **then** the briefing does not create duplicate action items.
- **Given** a resolved condition, **when** processing completes, **then** the related work state becomes explainably resolved.

### 7. Verification

- Add event mapping, dedupe, severity, escalation, resolution, replay, and unchanged-state suppression tests.
- Add workflow, task, notification, AI invocation, worker concurrency, tenant-isolation, and failure-path integration tests.
- Add Web tests for briefing, evidence, actions, empty/all-clear state, and navigation.
- Perform visual comparison and run affected API, workflow, agent, notification, and Web suites.

### 8. Definition of done

Maya operates from durable, deduplicated events and cadence windows with actionable briefings, complete state, safe failure handling, and no notification or task storms. Every displayed priority links to evidence and an operational next step.

---

## Prompt 15A — Implement Maya's independent Marketing departmental operating loop

### 1. Title and outcome

**Let Maya independently run the Marketing function within governed company authority.** From company goals and coordinator assignments—or from material Marketing events and cadence—Maya continuously assembles company/product/customer evidence, selects needed Marketing work, plans and performs every permitted Marketing activity, monitors results, replans within scope, and reports durable evidence and recommendations back to the company operating loop.

### 2. Current context

- `company-orchestration.md` defines and implements the company control plane: goals, operating configuration, cycles, snapshots, plans, initiatives, validation, task creation, review/replanning, four autonomy levels, and company pause.
- Prompt 1A adds the typed assignment, Marketing snapshot, effective-authority, progress/outcome, and signal boundary between Marketing and company orchestration.
- Prompts 2–15 plus 3A and 3B add the intended Marketing capabilities: strategy, intelligence, segmentation, target selection, strategy impact, campaign decomposition, content and creative production, governance, provider-neutral delivery, LinkedIn/Meta/X adapters, lifecycle journeys, measurement, attribution, experiments, triggers, and briefings.
- `RoleAgentCadenceBackgroundService` currently invokes Marketing analysis on daily, weekly, and monthly windows, but it does not manage the complete department lifecycle or all Marketing artifact/action types.
- Shared tasks, workflows, single-agent orchestration, bounded multi-agent collaboration, approvals, outbox, audit, and company review already provide execution primitives.

### 3. Dependencies

- Prompts 1–15, Prompt 1A, Prompt 3A, and Prompt 3B.
- A provider-specific prompt may remain externally blocked by credentials/provider approval; the loop must represent that capability as unavailable and continue safe internal work rather than simulate execution.
- Read and follow `company-orchestration.md` completely in addition to all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a durable Marketing departmental operating-run model or extend an existing agent/workflow run only if it can represent all required state without overloading company `OperatingCycle`. It must record company/agent, trigger, company goal/initiative/task linkage, lease/claim, snapshot/evidence version, effective authority, selected work, status, budgets, attempts, review outcome, correlation, and recovery state.
- Keep company `OperatingPlan` and `OperatingInitiative` authoritative for company priorities and cross-functional outcomes. The Marketing run may decompose an assigned Marketing outcome into Marketing-owned artifacts/tasks/workflows but must not create competing company goals or executive plans.
- Support triggers from:
  - A company operating initiative/task assigned to Maya.
  - Scheduled daily, weekly, monthly, quarterly, or configured Marketing review windows in the company timezone.
  - Material Marketing events from Prompt 15.
  - Operator request or prior Marketing/company outcome review.
- Before every run, enforce company execution scope, renewable lease/concurrency control, pause/emergency stop, active goal/initiative/version, Maya availability, effective authority, minimum interval, material-change threshold, cooldown, workload, dependency, AI/tool/cost budget, and duplicate-work checks.
- Build a bounded Marketing snapshot using authoritative Application projections for:
  - Company identity, goals, operating instructions, budgets, constraints, deadlines, and current initiatives.
  - Product/service portfolio, value propositions, features, use cases, pricing/packaging, availability, roadmap evidence, approved claims, and product knowledge.
  - Customers, segments, needs, behaviors, channel presence, price sensitivity, consent, value, retention, expansion, and feedback.
  - Sales outcomes, objections, pipeline, wins/losses, handoffs, and revenue evidence.
  - Support themes, sentiment, recurring problems, knowledge gaps, and customer-impact risk.
  - Finance-approved Marketing budget, actual/committed spend, liquidity/unit-economics constraints, and approval thresholds.
  - Approved knowledge, research, competitors, brand/compliance rules, prior decisions, and freshness.
  - All current Marketing strategies, objectives, plans, campaigns, activities, audiences, content, assets, channels, journeys, experiments, observations, attribution, tasks, approvals, provider states, failures, and prior expected/actual outcomes.
- Persist source references, timestamps, missing/stale/inaccessible/contradictory data, and truncation. Do not continue into unsupported actions when required evidence is absent.
- Implement explicit instruction precedence in deterministic resolution:
  1. Authorization, tenant isolation, safety, consent, law/compliance, and domain policy.
  2. Company pause/configuration, approved goals/plans/initiatives, budgets/limits, and coordinator instructions.
  3. Approved Marketing strategy, segments, objectives, policies, budgets, brand/channel rules, and approvals.
  4. Assigned task/workflow and its dependencies/completion evidence.
  5. Maya's role profile, cadence, memory, and self-originated priorities.
  6. Model suggestions.
- Contradictory, stale, impossible, or unsafe instructions must produce blocked/review evidence and a request for company replanning or clarification; do not silently resolve conflicts in Maya's favor.
- Add a bounded structured Marketing-run plan covering only needed work across all implemented Marketing capabilities:
  - Market/customer/competitor intelligence and evidence refresh.
  - Segmentation, sizing, target recommendations, and downstream impact review.
  - Strategy/4P/STP and objective review.
  - Program, campaign, activity, calendar, ownership, and budget work.
  - Content briefs, copy, creative assets, brand/evidence preflight, and approval preparation.
  - Channel selection, connection readiness, publication/delivery preparation, and permitted approved execution.
  - Lifecycle journey design/operation and Sales handoffs.
  - Experiments, measurement, attribution, performance analysis, and learning.
  - Risks, approvals, exceptions, knowledge gaps, integrations, and cross-functional dependencies.
- Deterministically validate each selected action for company-goal relevance, duplicate work, capability, data scope, capacity, dependency, budget, consent, policy, approval, idempotency, provider availability, and observable completion evidence.
- Apply the four company autonomy levels:
  - Recommend: Maya independently observes and proposes, but creates no operational Marketing state beyond the reviewable run/proposal.
  - Organize: Maya may create and assign validated internal Marketing tasks/workflows, but performs no execute action.
  - Operate internally: Maya may execute permitted analysis, research, drafting, internal record creation, review preparation, measurement, and other low-risk internal actions.
  - Controlled execution: Maya may execute only explicitly permitted external Marketing actions through current policy, approval, outbox, stable idempotency, retries, and reconciliation.
- Company autonomy is a ceiling, never an automatic grant. Use the effective-authority decision from Prompt 1A for every action and re-evaluate it immediately before tool or provider execution.
- Execute work through existing Application commands, `ISingleAgentOrchestrationService`, known workflows, guarded explicit tools, approved bounded `IMultiAgentCoordinator` plans, approval services, and outbox dispatchers. Do not perform business mutations directly from a reasoning response.
- When collaboration is needed, use durable owner/contributor/reviewer/approver tasks and artifact handoffs. Maya cannot recursively form arbitrary teams or mutate Finance, Sales, Support, Product, or company operating state outside their Application contracts.
- Monitor linked tasks, workflows, approvals, dispatches, provider reconciliation, observations, and deadlines. Make repeated polling/notifications/work creation idempotent.
- Review actual versus expected completion evidence and business outcomes. Select close, continue, revise within current scope, request evidence, reassign through the company coordinator, escalate, pause, or stop.
- Report progress, artifacts, evidence versions, actual/expected outcome, confidence, lessons, changed forecasts, blockers, and requested next action to the linked company initiative and future company snapshot.
- When Maya identifies a company-level opportunity/risk or needs a cross-functional priority/budget decision, raise one durable company operating signal instead of creating a new company initiative herself.
- Replace or adapt the hard-coded Marketing path in `RoleAgentCadenceBackgroundService` so it requests the new loop without creating duplicate cadence runs. Preserve Finance, Sales, and Support cadence behavior.
- Add operator-visible recovery for invalid plans, missing evidence, unavailable product data, failed dependencies, budget exhaustion, AI/tool limits, dead-letter state, provider failure, approval rejection/expiry, and ambiguous external outcomes.
- Extend the Marketing workspace with a Marketing Operations view showing why the run started, company goal/instruction, effective autonomy, current plan, active work, approvals, blockers, budget/limits, evidence, expected/actual outcomes, and actions to pause/escalate/retry safely. Link it to `/company-operation`.
- Write and generate `docs/design/references/marketing-autonomous-operations-reference.png` before UI implementation and compare the result at desktop and narrow widths.
- Add EF migration(s) for new durable run/lease/review state and preserve local and Docker SQL Server migration/restore compatibility.

### 5. Constraints and preservation rules

- Independent operation is not unbounded autonomy. Maya must obey company instruction precedence, effective authority, pause, budgets, dependencies, policy, approval, consent, and provider limits.
- Do not create a second company orchestrator, company goal store, generic workflow engine, AI stack, approval system, outbox, or provider stack.
- Do not let a role cadence, prompt, model response, or agent profile override company operating instructions or backend policy.
- Do not allow Maya to invent or activate company goals, reprioritize other departments, allocate company-wide budgets, resolve cross-department conflicts, or form recursive agent teams.
- Do not let company orchestration bypass Marketing-specific policy or make unsupported external actions available.
- Do not recursively invoke the company planner or Marketing planner in the same transaction as a result event.
- Do not generate work when no material change or unmet validated outcome exists.
- Do not treat missing product/customer/financial/provider data as permission to assume favorable facts.
- Preserve all existing Marketing routes, storage values, lifecycle behavior, company operating-cycle behavior, and non-Marketing role cadences unless a versioned compatibility change is explicitly required.

### 6. Acceptance criteria

- **Given** a validated active company initiative assigned to Maya, **when** the Marketing loop runs, **then** it creates a bounded plan linked to the exact company goal/plan/initiative/task versions and uses the required completion evidence.
- **Given** no user click but a configured cadence or material Marketing event, **when** effective autonomy permits it, **then** Maya independently performs the allowed analysis/internal work and records a complete run.
- **Given** company Recommend mode, **when** Maya identifies campaigns, content, or experiments, **then** she records proposals without creating operational tasks or executing tools.
- **Given** Organize mode, **when** an internal Marketing plan validates, **then** Maya may create deduplicated tasks/workflows but does not execute them or perform external effects.
- **Given** Operate-internally mode, **when** allowed drafting, research, measurement, or internal record work is selected, **then** Maya executes it through guarded tools and records evidence.
- **Given** Controlled-execution mode and a valid immutable approval, **when** an explicitly permitted external Marketing action becomes due, **then** it is rechecked and dispatched once through the outbox with reconciliation support.
- **Given** company pause or emergency stop, **when** a scheduled or event run is due or an action is about to execute, **then** no new Marketing execution begins and claimed work stops safely at the next controlled boundary.
- **Given** a conflict between a Maya preference and a company instruction, **when** both are resolved, **then** the company instruction wins unless higher-precedence policy blocks it; blocked conflicts are returned to the coordinator.
- **Given** stale product facts, missing consent, exhausted budget, unavailable channel, unresolved dependency, or insufficient evidence, **when** work is evaluated, **then** Maya requests evidence/replanning or blocks the affected action without fabricating state.
- **Given** repeated schedules, events, polls, retries, webhooks, or worker claims, **when** processed, **then** no duplicate run, artifact, task, approval, notification, channel action, or company signal is produced.
- **Given** completed Marketing work, **when** outcome review runs, **then** actual versus expected evidence, confidence, lessons, and next recommendation update the company initiative review and future company snapshot.
- **Given** Maya discovers a material cross-functional opportunity, **when** it is outside current delegated scope, **then** one company operating signal requests evaluation and Maya does not self-authorize a company initiative.

### 7. Verification

- Add domain/run-state tests for triggers, leases, idempotency windows, plan/review lifecycle, pause, cooldown, budgets, material-change detection, retry/dead-letter, and recovery.
- Add deterministic instruction-precedence and effective-authority tests for every company autonomy level and for policy/approval/company-pause overrides.
- Add snapshot tests covering company, product, customer, segment, Sales, Support, Finance, knowledge, Marketing, provider, and agent state with freshness, gaps, contradiction, and truncation.
- Add structured-plan validation tests for unsupported action types, excessive work, duplicate work, dependency cycles, missing completion evidence, cross-company references, provider unavailability, and budget/capacity excess.
- Add integration tests for company initiative assignment through Marketing task/workflow/tool execution and back to company work review.
- Add end-to-end tests for each autonomy level and representative Marketing activities: strategy review, segment analysis, campaign planning, content creation, experiment evaluation, internal measurement, and one approval-backed provider fixture.
- Add concurrency/idempotency tests for scheduler/event races, repeated assignments, worker claims, approvals, dispatch, webhooks, observations, outcome reports, and company signals.
- Add authorization and tenant-isolation tests for every snapshot, run, artifact, task, workflow, approval, provider, progress, outcome, and signal boundary.
- Add failure tests for model/schema failure, missing product data, stale evidence, policy denial, pause, approval rejection/expiry, provider auth failure, rate limit, permanent failure, timeout/ambiguity, and reconciliation.
- Add audit/correlation tests from company goal through Marketing run and all downstream artifacts to outcome review.
- Add UI/component/browser tests for instruction display, effective autonomy, active work, approvals, blocked/recovery states, pause, evidence, expected/actual outcomes, and navigation between Marketing and Company operation.
- Create and inspect migrations, run pending-model-change validation, verify local SQL Server, and document/verify the equivalent Docker SQL Server restore/run path.
- Run focused and full affected builds/tests including Application, Domain, Persistence/Migrations, Operations, Sales Infrastructure, API, Web, Web contracts, company orchestration, agents, tasks, workflows, approvals, outbox, providers, Marketing, and background workers.

### 8. Definition of done

Maya independently performs the complete implemented Marketing operating cycle from authoritative company, product, customer, commercial, and Marketing data while remaining governed by company orchestration and backend policy. Every run and action is company-scoped, goal-aligned, versioned, bounded, explainable, idempotent, pauseable, recoverable, and linked to outcome evidence. No competing company planner, uncontrolled AI loop, authority escalation, recursive replanning, assumed data, duplicate action, silent failure, mock production execution, or deferred in-scope activity remains.

---

## Prompt 16 — Complete end-to-end Marketing release hardening and operator readiness

### 1. Title and outcome

**Release the complete Maya Marketing operating loop safely.** Validate and harden strategy-to-campaign-to-content-to-channel-to-measurement behavior across authorization, tenant isolation, approvals, workers, provider ambiguity, audit, UI, documentation, and operational recovery.

### 2. Current context

- Prompts 1–15 plus Prompts 1A, 3A, 3B, and 15A incrementally add company-orchestration integration, strategy, intelligence, customer segmentation, segment analysis, agent recommendations, decomposition, content, creative, governance, channels, lifecycle journeys, measurement, event-driven operations, and independent governed Marketing operation.
- The repository has modular tests for API, Web, contracts, Sales sources, workflows, approvals, agents, and Marketing domain behavior.
- SQL Server migrations, local and Docker restore scripts, observability, audit, background execution, and settings surfaces must remain coherent after cross-cutting delivery.

### 3. Dependencies

- Prompts 1–15 plus Prompts 1A, 3A, 3B, and 15A, except a provider-specific prompt may remain externally blocked only by unavailable credentials or provider approval. The application must still represent that provider as unavailable rather than simulated.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Audit the full implementation against `/docs/architecture-rules.md`, `marketing.md`, this prompt pack, and the current repository; fix in-scope boundary violations and incomplete intermediate states.
- Verify all Marketing routes and clients use established authorization, company context, correlation, typed transport, cancellation, safe errors, and consistent contracts.
- Verify all tenant-owned queries, commands, workflows, outbox messages, provider references, observations, assets, approvals, and audit records are company-scoped.
- Verify every external action has immutable target version, policy, approval recheck, stable idempotency, concurrency-safe claim, bounded retry, permanent failure, ambiguity/reconciliation, and operator-visible recovery.
- Verify all AI capabilities use shared orchestration, bounded relevant context, explicit tools, structured validation, source grounding, audit, and conservative autonomy.
- Verify Maya follows company instruction precedence, effective-authority ceilings, company pause/stop, validated initiative ownership, company budgets/limits, cross-functional dependencies, and company outcome-review feedback for both assigned and self-originated Marketing work.
- Add missing audit and observability for actor, action, target, outcome, rationale summary, sources, correlation, before/after evidence, worker attempts, and reconciliation.
- Consolidate Marketing UI into the existing information architecture without adding retired or duplicate primary navigation. Ensure every screen answers what is happening, what needs attention, and what to do next.
- Generate a final consolidated reference `docs/design/references/marketing-workspace-complete-reference.png` before any significant final UI consolidation, then compare desktop and responsive implementation against it.
- Update operator/developer documentation for configuration, provider credentials, permissions, webhooks where applicable, background workers, approval policies, retry/reconciliation, monitoring, migration, local SQL Server, Docker SQL Server, and safe troubleshooting.
- Remove no valid test and weaken no assertion. Resolve all in-scope failures.

### 5. Constraints and preservation rules

- Do not replace production behavior with mocks to complete the release.
- Do not mark unavailable provider credentials or approval as success.
- Do not perform destructive database resets or rewrite migration history.
- Do not introduce microservices, a second frontend, a second workflow engine, or direct provider/model calls.
- Preserve existing Finance, Sales, Support, Agent team, Work, and company-onboarding behavior.

### 6. Acceptance criteria

- **Given** an authorized company, **when** a user follows the complete path from segment analysis and target approval through strategy proposal, campaign/content creation, approved external delivery, and imported observation, **then** every transition is durable, version-linked, traceable, company-scoped, and recoverable.
- **Given** an assigned company initiative or an autonomous Marketing trigger, **when** Maya operates, **then** the work remains linked to active company goals/instructions and reports actual outcomes back to the company operating review.
- **Given** a cross-company identifier at any API or worker boundary, **when** it is processed, **then** no read, mutation, enqueue, delivery, audit, or reconciliation occurs under the wrong company.
- **Given** duplicate requests, messages, webhooks, and worker claims, **when** the complete system processes them, **then** no duplicate business action occurs.
- **Given** an external timeout with unknown outcome, **when** reconciliation runs, **then** the system reaches a justified state without blind duplication.
- **Given** missing credentials or provider approval, **when** the user attempts configuration or execution, **then** the UI and API show an actionable unavailable state without mock success.
- **Given** a Marketing recommendation or action, **when** its detail is opened, **then** rationale, evidence, confidence, policy, approval, status, and next action are understandable in plain English.

### 7. Verification

- Add an end-to-end Marketing integration suite covering company initiative assignment, independent Marketing run, segment analysis, target selection, strategy linkage, downstream impact, decomposition, content, approval, durable action, provider fixture, observation, attribution, segment learning, company outcome review, and briefing.
- Add systematic cross-company read/write/enqueue/dispatch/reconcile tests.
- Add authorization-role matrix, approval-version, idempotency, concurrency, retry, reconciliation, audit, and recovery tests.
- Run migration creation/inspection checks, pending-model-change verification, local SQL Server migration validation, and document the equivalent Docker restore/run path.
- Run focused and full solution builds/tests in proportion to the change, including API, Web, Web contracts, agents, workflows, approvals, Marketing, Sales source, and background workers.
- Perform final screenshot-based UI verification at desktop and narrow widths and test loading, empty, error, unauthorized, blocked, waiting-for-approval, completed, and reconciliation states.

### 8. Definition of done

The full Maya Marketing loop is production-ready with no scaffolding, mock production data, silent failures, unhandled intermediate states, deferred in-scope TODOs, architecture boundary violations, migration gaps, unsafe external effects, or inaccessible operator recovery. Any remaining external blocker is explicitly limited to credentials/provider approval and is represented safely in product behavior and documentation.

---

## Prompt 17 — Restore Marketing build integrity and database deployment readiness

### 1. Title and outcome

**Make the current Marketing and company-orchestration baseline compile and start against an up-to-date SQL Server database.** Eliminate regressions represented by the attached local-run evidence, make missing migrations fail early with an actionable message, and provide a safe repeatable local and Docker database-update path so background workers never discover missing tables only after startup.

### 2. Current context

- The attached run captured `Invalid object name 'company_operating_configurations'` from `CompanyOperatingCycleScheduler`, which means the application model and the running Docker SQL Server migration history were out of sync.
- `20260812072032_AddCompanyOrchestrationAndMarketingFoundation` creates the company operating configuration schema, and later Marketing migrations depend on it.
- `server.ps1` starts the Docker SQL Server container and then delegates to `run-api.ps1`; the repository also has `DatabaseInitializationService`, `StartupMigrationValidation`, and `docs/sqlserver-local-validation-runbook.md`.
- The same captured run showed stale compile errors referring to nonexistent `IsSuperseded` members and collection method groups in Marketing analysis/snapshot code. Current source no longer contains those exact expressions, so implementation must first reproduce the current build and must not reintroduce obsolete fixes.
- Marketing and company-orchestration changes currently span Domain, Application, Persistence, Operations, Sales Infrastructure, API, Web, and a substantial EF migration sequence.

### 3. Dependencies

- Prompts 1–16 have been attempted and their current repository implementation is authoritative.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Reproduce the current `LocalRun` and normal Debug builds before changing code. If the attached compile failures no longer reproduce, add regression coverage around the corrected queries instead of making speculative entity changes.
- Verify that Marketing attribution analysis includes current attribution rows without referencing observation-only properties and that the Marketing company snapshot uses valid materialized collection counts and current entity members.
- Make API startup migration validation happen before hosted schedulers and workers can query newly introduced tables. Preserve the existing explicit `DatabaseInitialization:ApplyMigrationsOnStartup` policy and do not add schema DDL to `Program.cs`.
- Update the supported local/Docker startup workflow so operators either apply pending migrations deliberately or receive a fail-fast message listing pending migration IDs and the exact safe `dotnet ef database update` command. Do not silently continue with a partially migrated database.
- Ensure `server.ps1`, `run-api.ps1`, configuration, and documentation agree about whether development startup applies migrations or requires a separate migration step. Keep production conservative and explicit.
- Verify the entire company-orchestration and Marketing migration chain applies in timestamp order to a clean Docker SQL Server database and upgrades a representative pre-Marketing database without destructive reset or migration-history rewriting.
- Add health/readiness evidence that distinguishes database unreachable, pending migrations, failed migration, and ready. Background workers must not produce repeated missing-table exceptions while the host is unready.
- Preserve the inactive Gmail connection behavior from the attached log: `TokenExpired` remains an operator-visible reconnect state and is not treated as a Marketing or database failure.
- Update the Marketing operations runbook with recovery steps for pending migrations, missing-table evidence, current migration inspection, Docker update, and rollback/escalation guidance.

### 5. Constraints and preservation rules

- Do not delete or squash existing migrations, reset the user's database, use `EnsureCreated`, or add ad hoc SQL schema creation outside EF migrations.
- Preserve local SQL Server and Docker SQL Server restore/update compatibility.
- Do not fix stale captured compile errors by adding meaningless properties to unrelated entities.
- Do not start multiple API hosts or stop unrelated `dotnet` processes during verification; follow the workspace local Web verification instructions.
- Preserve mailbox reconnect semantics and all non-Marketing worker behavior.

### 6. Acceptance criteria

- **Given** the current repository, **when** Sales Infrastructure, API, and Web are built in Debug and `LocalRun`, **then** no Marketing analysis or snapshot compile error occurs.
- **Given** a database missing `20260812072032_AddCompanyOrchestrationAndMarketingFoundation`, **when** the API starts, **then** startup fails before company schedulers run and identifies the pending migration and safe operator action.
- **Given** a clean Docker SQL Server database, **when** the documented update command runs, **then** all migrations apply and `company_operating_configurations` plus the complete Marketing schema are available.
- **Given** an already migrated database, **when** startup repeats, **then** no schema mutation or duplicate migration occurs.
- **Given** an inactive mailbox connection, **when** mailbox polling runs, **then** it remains skipped with reconnect guidance and does not prevent database or Marketing readiness.

### 7. Verification

- Add focused regression tests for Marketing attribution source construction and Marketing snapshot projection/count/truncation behavior.
- Add startup migration-validation tests for no pending migrations, pending migrations, and development versus production policy.
- Run Debug and `LocalRun` builds for Sales Infrastructure and API, plus the Web build.
- Run `dotnet ef migrations has-pending-model-changes` with the migrations project and API startup project.
- Apply the complete migration chain to a clean Docker SQL Server database and an upgrade fixture; inspect `__EFMigrationsHistory` and required tables.
- Start one bounded API host only after migration and verify readiness plus one company scheduler scan without SQL error 208.

### 8. Definition of done

The current code compiles, database drift is detected before workers start, the supported local and Docker flows apply the exact EF migration chain safely, and the attached missing-table failure has a tested operator recovery path. No destructive reset, speculative entity property, hidden migration, startup DDL, or repeated background exception remains.

---

## Prompt 18 — Complete the Marketing-to-company orchestration contract

### 1. Title and outcome

**Finish the governed typed boundary between Maya and company orchestration.** Company initiatives can be resolved and accepted with complete versioned instructions, Marketing can report structured progress and outcomes, and Maya can raise durable company-level signals without creating a second planner or recursive operating cycle.

### 2. Current context

- `MarketingOperatingLoopContracts.cs` currently exposes request/list run contracts but no complete assignment-context, progress/outcome, or company-signal contracts.
- `MarketingOperatingSnapshotContributor` contributes bounded Marketing state through `ICompanyOperatingSnapshotContributor`.
- `MarketingOperatingLoopService` validates basic initiative ownership/status and captures a company snapshot, but does not resolve all goal/plan/initiative versions, dependencies, reviewers, budgets, completion evidence, or correlation links required by Prompt 1A.
- Existing `CompanyGoal`, `OperatingCycle`, `OperatingPlan`, `OperatingInitiative`, tasks, workflows, approvals, outbox, audit, and company work review remain authoritative.

### 3. Dependencies

- Prompt 17.
- Existing Prompts 1A and 15A.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add Application contracts and queries for a company-scoped Marketing assignment context containing exact goal, cycle, snapshot, plan, initiative and task IDs/versions; desired outcome; priority; dates; owner/contributors/reviewer; dependencies; budget and capacity limits; completion evidence; validation state; action restrictions; and correlation chain.
- Deterministically reject paused company state, inactive goal, stale/rejected/superseded plan, stale initiative version, wrong owner, cross-company link, unsatisfied hard dependency, duplicate active assignment, missing completion evidence, and exhausted budget/capacity with stable reason codes and plain-English explanations.
- Replace the current two-input effective-authority calculation with an Application policy that returns the most restrictive result across company autonomy, pause/stop, goal and initiative constraints, Maya's availability/autonomy, capability/tool scope, Marketing action policy, consent, approval, provider health, workload, and cost/budget.
- Add durable, idempotent Marketing progress and outcome records or reuse an existing suitable versioned work-evidence model. Include completed artifacts, evidence version, expected and actual results, confidence, data gaps, blockers, dependencies, changed forecast, lessons, and requested next action.
- Feed those outcomes into the existing company initiative review and future company snapshots through Application abstractions; Operations must not reference Sales Infrastructure.
- Add a durable company operating signal contract and lifecycle for material Marketing opportunities, risks, segment changes, provider failures, product/customer evidence, budget needs, and cross-functional dependencies. Enqueue future company-cycle evaluation only after commit and never invoke company planning recursively in the same transaction.
- Preserve one correlation chain from company goal through run, artifact, task/workflow, approval, provider action, observation, outcome, signal, and company review.
- Extend `/company-operation` and Marketing Operations read models with assignment instruction, progress, outcome, evidence, blockers, and bidirectional navigation.
- Before significant UI work, explicitly write and generate `docs/design/references/company-operation-marketing-initiative-reference.png`, then implement desktop and narrow states using `ui-instructions.md` and `/docs/design.md`.
- Add an EF migration for any durable progress/outcome/signal state and preserve SQL Server/Docker compatibility.

### 5. Constraints and preservation rules

- Company goals, plans, initiatives, ownership, budgets, autonomy, and pause state remain company-orchestration owned.
- Marketing signals are proposals for a future cycle, not approved initiatives.
- Do not create direct Operations-to-Sales infrastructure references, chat-based system-of-record state, or recursive planning calls.
- Every `IgnoreQueryFilters` use must reapply company scope.
- Preserve current company task commit idempotency, reviews, and non-Marketing snapshot contributors.

### 6. Acceptance criteria

- **Given** a current approved initiative assigned to Maya, **when** it is resolved, **then** the exact versions, dependencies, budget, authority, completion evidence, and correlation chain are returned.
- **Given** any stale, paused, wrong-owner, duplicate, cross-company, dependency-blocked, or over-budget assignment, **when** Maya accepts it, **then** it is blocked with no Marketing mutation and a safe reason.
- **Given** Marketing progress or completion, **when** it is reported twice with the same idempotency key, **then** one versioned outcome is available to company work review.
- **Given** Maya discovers a company-level opportunity, **when** she raises a signal, **then** one durable signal requests a later cycle and no synchronous recursion occurs.
- **Given** company-level authority exceeds Marketing policy, **when** authority is evaluated, **then** the Marketing restriction wins.

### 7. Verification

- Add policy tests for every authority level and every restricting input.
- Add assignment tests for versions, dependencies, ownership, pause, duplicate work, budget, capacity, and tenant isolation.
- Add idempotency/concurrency tests for progress, outcome, signal, snapshot inclusion, and company review consumption.
- Add architecture tests preventing Operations Infrastructure from referencing Sales Infrastructure.
- Add API authorization and cross-company integration tests.
- Add Web component/browser tests and screenshot comparison for active, blocked, completed, and signal states.

### 8. Definition of done

Marketing and company orchestration exchange complete, versioned, company-scoped assignments, authority decisions, progress, outcomes, and signals through durable Application boundaries. No incomplete context, recursive planner, authority escalation, untracked chat instruction, duplicate signal, or missing company-review feedback remains.

---

## Prompt 19 — Execute Maya's permitted internal Marketing work through a durable run plan

### 1. Title and outcome

**Turn Maya's operating loop from analysis-plus-task creation into a governed departmental executor.** For permitted internal work, Maya creates real draft Marketing artifacts through existing Application commands, monitors their completion, and reports outcomes while every external or sensitive action remains separately approved.

### 2. Current context

- `MarketingOperatingLoopService` currently performs a cadence analysis, optionally executes another internal analysis, creates tasks, and stores JSON summaries.
- Strategy, segment, intelligence, decomposition, content, experiment, journey, channel-action, policy, approval, workflow, audit, and provider services already exist in `VirtualCompany.Infrastructure.Sales` behind Application contracts.
- `MarketingOperatingRun` has basic claim/status/evidence fields but lacks a versioned action plan, renewable lease, per-action decisions, review outcome, dead-letter lifecycle, and complete budget/tool/model usage accounting.
- Company `OperatingPlan` remains authoritative for company-level priorities.

### 3. Dependencies

- Prompts 17 and 18.
- Existing Prompts 2–15.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add a typed, bounded Marketing run plan with versioned action records. Each action must identify capability/tool, target artifact or proposed draft, source/evidence version, goal relevance, dependencies, expected completion evidence, authority decision, approval requirement, idempotency key, budget/cost estimate, status, attempts, and recovery state.
- Implement deterministic instruction precedence and conflict detection across safety/law/consent, company pause and instructions, approved company plan, Marketing policy/strategy/segments, assigned task, provider/capability state, and model suggestion.
- Support renewable lease/heartbeat, expired-lease recovery, concurrency-safe claims, cooldown/material-change checks, maximum work/model/tool/task/cost limits, retries, cancellation, pause, dead-letter, and operator retry/escalation.
- Map validated internal action types to existing Application commands for real draft work: intelligence review/research task, segment proposal draft, strategy proposal draft, campaign decomposition preview/draft, content brief/variant draft, experiment readiness/performance review, and internal Marketing briefing.
- Never mutate business state directly from unvalidated reasoning output. Parse structured plans, validate sources and policy, then invoke guarded commands with stable idempotency.
- Keep target activation, campaign launch, publication, spend, contact, Sales mutation, and other external/sensitive effects behind their existing policy, approval, workflow, and outbox boundaries.
- Re-evaluate effective authority, target version, policy, approval, provider availability, budget, and consent immediately before each command/tool/external enqueue.
- Monitor linked tasks, workflows, approvals, dispatches, observations, and deadlines. Persist expected versus actual evidence and choose close, continue, revise within scope, request evidence, escalate, pause, or stop.
- Publish structured progress/outcome through Prompt 18 and raise one durable company signal when reprioritization, cross-functional work, or budget decision is needed.
- Extend Marketing Operations UI with run plan, active action, instruction source, authority, budgets/limits, evidence, expected/actual result, blockers, retry/pause/escalate actions, and linked company initiative.
- Use the existing `marketing-autonomous-operations-reference.png` only if the design remains materially compatible; otherwise explicitly generate a new version before UI changes.
- Add migrations for durable plan/action/lease/review state.

### 5. Constraints and preservation rules

- Maya must not create company goals or company operating plans.
- Do not call EF/domain mutations from a model response branch; use Application command boundaries.
- Do not grant execute permission merely because company autonomy is high.
- Do not retry ambiguous external effects blindly or infer consent.
- Preserve Finance, Sales, Support, and existing cadence scheduling behavior.

### 6. Acceptance criteria

- **Given** `OperateInternally` authority and grounded evidence, **when** Maya selects a supported internal activity, **then** a real idempotent draft artifact is created through its Application command and linked to the run/action/evidence.
- **Given** recommendation-only authority, **when** the same plan is evaluated, **then** no artifact mutation occurs and a reviewable recommendation is recorded.
- **Given** a duplicate scheduler/event/assignment race, **when** claims occur, **then** one run/action owns the work.
- **Given** missing evidence, policy denial, budget exhaustion, stale target, or conflicting instruction, **when** execution is attempted, **then** the action blocks safely with an operator recovery path.
- **Given** an external action, **when** Maya reaches it, **then** she can only prepare or enqueue through current approval/outbox controls and cannot publish directly.

### 7. Verification

- Add domain tests for plan/action/lease/review/dead-letter lifecycle and instruction precedence.
- Add integration tests for every supported internal artifact command and all four autonomy levels.
- Add scheduler/event race, duplicate command, expired lease, retry, cancellation, and budget tests.
- Add negative tests proving Maya cannot activate targets/strategies, launch campaigns, spend, contact people, publish, or mutate Sales state without the established boundary.
- Add audit/correlation tests from run through artifact and outcome.
- Add Web tests for active, blocked, waiting, completed, dead-letter, retry, pause, and escalation states.

### 8. Definition of done

Maya independently executes every permitted internal Marketing activity represented by the run plan, using real existing commands and durable state, while sensitive actions remain governed. No analysis-only placeholder, direct reasoning mutation, duplicate work, unrenewed lease, hidden budget overrun, unsupported retry, or unreported outcome remains.

---

## Prompt 20 — Complete strategic segmentation and Maya's segment decision support

### 1. Title and outcome

**Finish customer segmentation as a fully queryable, versioned decision capability.** Segment sizing, economics, score policy, target decisions, lifecycle, Maya analysis, comparisons, and downstream review impact become explicit and production-ready rather than partially represented by generic JSON claims.

### 2. Current context

- `MarketingCustomerSegment` and immutable `MarketingCustomerSegmentVersion` preserve stable identity and basic lifecycle.
- `MarketingSegmentDimension` flattens JSON leaves for querying while compatibility JSON remains.
- Current size fields are low/high/method/confidence; target state stores state and rationale; service endpoints cover create, submit, activate, impact, and dimension listing.
- `MarketingSegmentProposalDto` currently returns generic classified claims and sources rather than the complete structured schema required by Prompts 3A/3B.
- Strategy, content briefs, journeys, campaign trace links, experiments, objectives, and impact projection already reference segment versions in several places.

### 3. Dependencies

- Prompts 17–19.
- Existing Prompts 2, 3, 3A, 3B, and 4–6.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Add relational version-owned records for size estimates, economics, score-policy dimensions, target decisions, and explicit mappings where the current generic dimension row cannot enforce meaning, units, ranges, methods, dates, and evidence.
- Size estimates must support count/value, low/high, unit, period, geography, currency, method (`top_down`, `bottom_up`, `triangulated`, or explicit supported extension), assumptions, source IDs, confidence, observed/as-of date, and freshness.
- Economics must support revenue, gross margin, acquisition cost, sales-cycle length, cost to serve, retention, lifetime value, and expansion hypotheses with range, unit/currency, method, confidence, and evidence.
- Version score policy with explicit dimensions, weights, thresholds, exclusions, missing-evidence behavior, evidence-quality and fairness/privacy/regulatory risk. Deterministic calculation remains authoritative and separate from target selection.
- Expand target decisions with target type, rationale, expected impact, confidence, risks, review date, approval status/request, actor, decision time, and immutable segment-version link.
- Complete lifecycle commands for update/new immutable version with concurrency, compare, submit, synchronize approval/rejection/request changes, activate, supersede, archive, and scheduled freshness review.
- Add explicit company-scoped mapping contracts between strategic segments and ICP/persona, campaign audience, qualification definition, and Sales handoff without merging those models or inferring individual membership/consent.
- Expand downstream impact to strategies, objectives, plans, campaigns, audiences, briefs, journeys, experiments, budgets, reports, qualification, and Sales handoffs; create idempotent review tasks without silently rewriting artifacts.
- Replace generic segment proposal claims with a structured reasoning result for segmentation basis, definition, needs/jobs/barriers, behavior, channel presence, price sensitivity, size/economics estimates, alternatives, risks, missing evidence, target recommendation, and 4P/downstream implications.
- Require source/classification for material claims and method/range/unit/period/geography/currency/confidence for estimates. Invalid, unsafe, discriminatory, falsely precise, or inaccessible evidence must produce `Needs review` and cannot commit.
- Keep proposal preview/compare, draft commit, target recommendation, target approval, and activation as distinct commands. Maya cannot activate a target.
- Extend Segments and Strategy UI with version comparison, structured estimates, score explanation, target recommendation, source inspection, freshness, impact, and guarded review actions.
- Before UI changes, explicitly generate the missing `marketing-customer-segments-reference.png`; generate `marketing-segment-analysis-reference.png` if the Maya analysis surface is materially distinct.
- Add EF migration(s) with SQL Server-compatible backfill from existing fields/JSON where reliable; preserve unverifiable legacy values as classified gaps rather than inventing data.

### 5. Constraints and preservation rules

- Do not infer consent, protected traits, proxies, or individual strategic-segment membership.
- Do not present estimates as observations or fabricate precision during migration/backfill.
- Do not silently mutate downstream artifacts when a segment changes.
- Keep deterministic policy outside model reasoning and preserve current segment IDs/version references.
- Preserve existing campaign audience and qualification behavior.

### 6. Acceptance criteria

- **Given** two independently sourced size estimates, **when** reviewed, **then** methods, ranges, units, dates, geography, assumptions, evidence, and triangulation are queryable and visible.
- **Given** missing evidence, **when** score policy runs, **then** the versioned missing-evidence rule changes the deterministic result and explains it.
- **Given** Maya proposes a target, **when** a manager accepts the proposal, **then** only a draft/recommendation is committed and separate approval plus activation remain required.
- **Given** a material new segment version, **when** impact runs, **then** every linked artifact is listed or receives one review task and no artifact is rewritten.
- **Given** a retry of proposal or review-task commit, **when** the same idempotency key is used, **then** no duplicate version, decision, or task is created.

### 7. Verification

- Add domain/policy tests for estimates, economics, score rules, missing evidence, target decisions, lifecycle, freshness, and concurrency.
- Add structured AI schema, grounding, false-precision, prompt-injection, privacy/fairness, and unsafe-criteria tests.
- Add tenant, authorization, idempotency, approval, audit, mapping, and downstream impact integration tests.
- Add Web client/component/browser tests for compare, proposal, recommendation, evidence, impact, and all empty/error/review states.
- Create and inspect migrations, run pending-model validation, and verify local/Docker SQL Server upgrades.

### 8. Definition of done

Segments are fully queryable, source-grounded, versioned, policy-scored, separately target-decided, approval-governed, mapped without model conflation, and propagated through explicit review impact. No opaque-only core decision, fabricated estimate, inferred consent, discriminatory shortcut, silent downstream rewrite, or generic unvalidated Maya proposal remains.

---

## Prompt 21 — Complete lifecycle journey eligibility and concurrency safety

### 1. Title and outcome

**Make lifecycle journeys enforce their declared audience and transition rules at runtime.** Every enrollment and outbound step uses deterministic eligibility, entry/exit, consent, suppression, frequency, target-version, approval, and concurrency checks with durable evidence.

### 2. Current context

- `MarketingLifecycleJourney` stores audience eligibility, entry/exit criteria, steps, guardrails, segment version, validity, lifecycle, lineage, and concurrency version.
- `MarketingJourneyEnrollment` stores durable step and frequency state.
- `MarketingJourneyExecutionService` currently checks journey/contact availability, target-segment status, consent, suppression, connection, adapter validation, frequency, and prior action state.
- The worker does not currently evaluate the stored eligibility/entry/exit JSON at each step, claim enrollments with a lease, process inbound events, or record conversion goals comprehensively.

### 3. Dependencies

- Prompts 17–20.
- Existing Prompt 13 and channel/governance prompts.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Define a versioned, allowlisted journey rule schema for strategic-segment mapping, operational audience eligibility, entry, exit, goal/conversion, timing, consent type, suppression, and permitted contact/customer fields. Validate and compile it deterministically; do not execute arbitrary expressions.
- Evaluate eligibility and entry criteria before enrollment and re-evaluate eligibility, exit, consent, suppression, contact pressure, frequency, journey approval/version, segment target version, provider health, and action policy immediately before every outbound step.
- Tie journey activation approval and each external action approval to immutable versions; stale, rejected, expired, or changed approvals must block.
- Add concurrency-safe enrollment claims/leases, retry schedule, maximum attempts, dead-letter/recovery, and safe handling for worker crashes.
- Add idempotent inbound-event processing keyed by company, journey definition/version, contact, event type/reference, and occurrence version/window.
- Persist step attempts/outcomes, waits, exits, blocks, failures, conversions/goals, approval and policy evidence, provider/action references, and correlation.
- Integrate qualification and Sales handoff only through Application contracts and durable tasks/events; Marketing must not mutate Sales state directly.
- Ensure pause defers work, resume preserves step state, cancellation/supersession terminates or migrates instances only by explicit policy, and completion preserves history.
- Extend journey UI with rule validation, sample eligibility reasons, instance timeline, conversion state, blocked/dead-letter recovery, and plain-English policy/consent evidence. Use the existing journey reference if compatible or generate an updated reference first.
- Add EF migrations for rule versions, inbound events, claims, attempts, conversions, or audit state as required.

### 5. Constraints and preservation rules

- Do not infer consent from segment, behavior, or model output.
- Do not permit arbitrary code/expression execution in rules.
- Do not enqueue outbound actions without current exact approval and policy checks.
- Keep all worker queries and idempotency keys company-scoped.
- Preserve existing journey/version IDs and completed history.

### 6. Acceptance criteria

- **Given** a contact that fails eligibility or entry criteria, **when** enrollment is attempted, **then** no active enrollment or outbound action is created and reasons are recorded safely.
- **Given** a contact becomes ineligible, suppressed, or withdraws consent between steps, **when** the next step is due, **then** the enrollment exits without dispatch.
- **Given** two workers claim the same due enrollment, **when** they race, **then** one owns the lease and one business action is produced.
- **Given** a replayed inbound event, **when** processed repeatedly, **then** one transition/conversion occurs.
- **Given** a stale approval or target version, **when** dispatch is considered, **then** the enrollment blocks with an operator-visible recovery path.

### 7. Verification

- Add rule-schema and deterministic evaluator tests for eligible/ineligible, entry, exit, goal, invalid, unsafe, and missing evidence cases.
- Add worker concurrency, lease recovery, retry/dead-letter, pause/resume, and terminal-state tests.
- Add inbound replay and polling idempotency tests.
- Add consent, suppression, frequency, approval-version, policy, segment-version, and provider failure integration tests.
- Add tenant-isolation and authorization tests across journey, contact, event, enrollment, action, and handoff boundaries.
- Add UI/browser tests for validation, sample reasoning, active timeline, conversion, blocked, and recovery states.

### 8. Definition of done

Journey declarations are enforced at runtime and every transition is deterministic, durable, company-scoped, idempotent, consent-safe, approval-bound, concurrency-safe, auditable, and recoverable. No stored-but-ignored rule, duplicate step, inferred consent, stale approval, arbitrary expression, or direct Sales mutation remains.

---

## Prompt 22 — Complete Marketing measurement, attribution, experiments, and segment learning

### 1. Title and outcome

**Build a defensible Marketing learning loop from exposure to outcome.** Normalized observations, attribution touches/evidence/models/results, experiment exposure and readiness, performance projections, and segment learning clearly distinguish observation, configured attribution, correlation, inference, and causal experiment evidence.

### 2. Current context

- Marketing has channel observations with correction/supersession lineage, a metric catalog, `MarketingAttributionResult`, basic experiment definitions, minimum-sample completion checks, and performance UI.
- Attribution currently persists result-level subject/model/classification/value/evidence, but there is no complete Marketing touch/exposure chain or versioned attribution-rule execution.
- Experiments do not yet persist complete exposure/assignment/sample/result/guardrail/stopping evidence.
- Segment impact exists, but segment-level reach, CAC, revenue, retention, LTV, price/offer response, and assumption-learning projections are incomplete.

### 3. Dependencies

- Prompts 17–21.
- Existing Prompt 14 and provider observation ingestion.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Complete the normalized metric catalog with units, aggregation, denominator rules, supported dimensions, freshness SLA, source quality, and compatibility validation.
- Add company-scoped attribution touch/exposure/evidence records and versioned bounded attribution model/rule definitions. Support only explainable models justified by available evidence, such as first/last/even/configured weighted touch; mark correlation/inference explicitly.
- Produce attribution results from immutable input/evidence versions with dedupe, correction handling, model version, period, confidence, limitations, and reproducible lineage.
- Add experiment variant, assignment/exposure, sample/result, guardrail, stopping-rule, and decision records. Deterministically evaluate readiness, minimum sample, data quality, contamination where measurable, guardrail breaches, insufficient sample, and completion.
- Do not label attribution or observational change as causal. Reserve causal language for experiments that meet an explicit deterministic policy and show limitations.
- Add performance projections for objective progress, campaign/content/channel efficiency, funnel leakage, Sales handoff outcomes, budget variance, data freshness, and provider coverage.
- Add segment learning projections for reach, conversion, acquisition cost, pipeline, revenue, retention, LTV, channel response, offer/price response, evidence confidence, and proposed changes to size/economics/attractiveness assumptions. Learning creates review proposals, not silent segment rewrites.
- Extend Maya's performance/experiment results with structured claims, alternative explanations, source/evidence IDs, confidence, limitations, and guarded next actions.
- Extend Performance UI with what changed, evidence chain, attribution model/limitations, experiment readiness/decision, segment learning, confidence, and next review action. Use or regenerate `marketing-performance-reference.png` before significant redesign.
- Add EF migration(s), indexes, retention bounds, and representative-volume query considerations.

### 5. Constraints and preservation rules

- Never overwrite corrected observations or original attribution inputs.
- Never claim causality from correlation or generic model output.
- Keep provider payload schemas out of Domain entities.
- Do not automatically change segment definitions, budgets, campaigns, or prices from learning output.
- Preserve existing observation and experiment IDs and compatible APIs where feasible.

### 6. Acceptance criteria

- **Given** duplicate/corrected provider observations, **when** attribution runs, **then** only current deduplicated evidence contributes and full lineage remains inspectable.
- **Given** a configured attribution rule, **when** results are computed, **then** inputs, model version, weights, limitations, and classification reproduce the result.
- **Given** insufficient or contaminated experiment evidence, **when** completion is requested, **then** the decision is `insufficient evidence` rather than a winner.
- **Given** credible segment performance changes, **when** learning runs, **then** a source-linked review proposal is created and the approved segment is unchanged.
- **Given** a user opens a result, **when** evidence is inspected, **then** observed, attributed, correlated, inferred, and experimental claims are visibly distinct.

### 7. Verification

- Add unit/aggregation/dimension/freshness, dedupe/correction, attribution-model, confidence, and lineage tests.
- Add experiment assignment/exposure, readiness, stopping, guardrail, contamination, insufficient-sample, and decision tests.
- Add provider ingestion, Sales outcome, authorization, tenant-isolation, and failure-path integration tests.
- Add AI grounding and alternative-explanation tests.
- Add representative-volume performance checks and required indexes.
- Validate migrations and add UI/browser tests plus screenshot comparison.

### 8. Definition of done

Marketing performance and learning are reproducible from normalized evidence, attribution limitations are explicit, experiments have durable sample and decision state, and segment learning produces governed review proposals. No double counting, overwritten correction, unsupported causality, invisible model rule, ungrounded AI conclusion, or silent strategic mutation remains.

---

## Prompt 23 — Connect owning modules to durable Marketing events and briefings

### 1. Title and outcome

**Make Marketing events automatic, idempotent, and action-oriented.** Owning modules publish or translate material changes through durable contracts so Maya's alerts, tasks, briefings, and operating runs no longer depend on manually posting event records through the Marketing API.

### 2. Current context

- `MarketingEventTypes` defines objective, campaign, content, observation, qualification, handoff, experiment, fatigue, consent, brand, provider, intelligence, segment, and downstream-staleness events.
- `MarketingDeliveryService` can create, process, link tasks/runs, notify, and resolve event triggers.
- Current repository usage creates Marketing events through `MarketingController`; owning modules do not yet emit the complete event set automatically.
- Existing domain/workflow events, company outbox, background execution, notifications, tasks, and audit infrastructure should be reused.

### 3. Dependencies

- Prompts 17–22.
- Existing Prompt 15.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Define Application-level event/signal contracts and translators for every supported Marketing event without introducing sibling Infrastructure references.
- Emit events from authoritative owning transactions for objective risk, campaign threshold/completion, content due/overdue, observation stale/correction, qualification, Sales handoff outcome, experiment threshold, contact pressure/fatigue, consent/unsubscribe, brand incident, provider failure, intelligence freshness/change, and material segment/downstream impact changes.
- Use transactional outbox or established workflow/domain-event boundaries where the event follows a business mutation. Do not synchronously invoke Maya in the owning transaction.
- Define deterministic source version and occurrence-window idempotency for each event type. Replays and unchanged conditions must produce at most one active event, linked task, notification, and Marketing run.
- Apply deterministic severity, action, escalation, notification audience, quiet-hours/cooldown, and resolution policy before AI invocation.
- Invoke bounded grounded Maya analysis only when it adds value; deterministic events may create a task/notification without a model call.
- Persist current condition, evidence, related task/workflow/approval/run, attempts, failure/retry, notification, correlation, acknowledgment, and resolution evidence.
- Consolidate daily/weekly/monthly Marketing briefs from unresolved priorities without duplicating event work or repeatedly notifying unchanged state.
- Extend Marketing, Overview, Agent team, and Work read models with linked event evidence and direct action navigation using existing information architecture.
- Use the current operating-brief reference if compatible or generate an updated screenshot first.

### 5. Constraints and preservation rules

- Owning modules publish facts; Marketing owns Marketing interpretation and response.
- Do not add Infrastructure-to-Infrastructure references or recursive synchronous planning.
- Do not notify or invoke AI repeatedly for unchanged conditions.
- Keep event IDs, source versions, tasks, notifications, and runs company-scoped and correlated.
- Preserve existing manual event endpoint only as an authorized operator/integration path, not the primary mechanism.

### 6. Acceptance criteria

- **Given** a provider dispatch permanently fails, **when** the owning worker commits, **then** one provider-failure Marketing event is durably queued with evidence and later processed.
- **Given** an unchanged stale observation condition is scanned repeatedly, **when** policy runs, **then** one active event/task/notification exists for its occurrence window.
- **Given** a Sales handoff outcome, **when** Sales records it, **then** Marketing receives a company-scoped event without Sales referencing Marketing Infrastructure.
- **Given** a resolved condition that recurs in a later valid window/version, **when** emitted, **then** a new traceable event can be created.
- **Given** an all-clear day, **when** the brief is built, **then** it shows no duplicated stale tasks or notifications.

### 7. Verification

- Add event mapping and contract tests for every event type.
- Add outbox atomicity, replay, dedupe, unchanged suppression, cooldown, severity, escalation, resolution, and recurrence tests.
- Add task, notification, AI invocation, worker concurrency, failure/retry, authorization, and tenant-isolation integration tests.
- Add architecture tests for capability boundaries.
- Add Web tests for briefing, evidence, linked actions, empty/all-clear, failure, and navigation states.

### 8. Definition of done

Every material Marketing condition is emitted from its authoritative owner through a durable, company-scoped contract and produces one policy-governed response per source version/window. No controller-dependent detection, infrastructure coupling, recursive planning, duplicate task, notification storm, ungrounded AI invocation, or unresolved silent failure remains.

---

## Prompt 24 — Harden creative safety and social-provider completeness

### 1. Title and outcome

**Close the remaining production gaps in creative assets and LinkedIn, Meta, and X delivery.** Human and generated assets have authoritative safety/provenance evidence, provider capabilities are discovered honestly, supported media flows and observation ingestion work, and all external outcomes are recoverable and tested.

### 2. Current context

- Creative generation uses `IMarketingCreativeImageGenerator`, protected credentials, OpenAI image generation, company document storage, immutable asset versions, checksums, provenance, and lifecycle state.
- Human uploads currently persist `malwareScan = "storage_provider_required"`, which is not evidence that an actual scan passed.
- Channel OAuth, destinations, protected secret references, immutable actions, approvals, background dispatch, retry, ambiguous reconciliation, and real provider HTTP publishers exist.
- Current deliberately advertised provider subset is LinkedIn text posts, X text posts, Facebook Page text posts, and Instagram single-image posts from an approved public URL.
- Real provider credentials, app review, scopes, access tiers, and costs remain environment-specific blockers and must never be simulated as success.

### 3. Dependencies

- Prompts 17–23.
- Existing Prompts 7–12 and 16.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Integrate an authoritative malware/content-safety scanning abstraction for uploaded files. Persist scan provider/reference/version/time/result; quarantine pending/failed/error assets and prohibit submission, approval, download/publication, or use until policy allows.
- Add deterministic image/file validation for signature versus extension/MIME, size, dimensions/aspect, decompression risk where applicable, privacy metadata, prohibited content, likeness/identity consent, copyright/license/provenance, factual visualization, brand profile, and accessibility/alt text.
- Route visual-prompt preparation through the shared reasoning gateway/tool boundary with bounded brief/brand/evidence context and structured validation before the image provider adapter is called.
- Audit creative prepare/generate/regenerate/upload/quarantine/metadata edit/submit/request changes/approve/reject/retire/download/use with actor, before/after, sources, target version, policy, and correlation.
- Implement explicit asset compare and request-changes lifecycle without overwriting approved versions.
- Re-verify provider contracts against current official documentation at implementation time. Update scopes, versions, limits, scheduling/media availability, cost/tier messaging, and docs only from primary provider sources.
- Complete destination/capability and health discovery so the UI advertises only actions, media types, destinations, and limits available to the actual connection/tier.
- Implement supported media upload/container/status flows where the approved provider contract enables them; otherwise show an explicit unavailable capability and prevent preparation/approval.
- Complete normalized publication/delivery and available engagement observation ingestion with source reference, retrieval time, freshness, dedupe, correction, and no unsupported attribution claim.
- Recheck immutable content/asset/segment target, policy, consent where applicable, approval version/status, destination capability, connection health, provider limits, and schedule immediately before dispatch.
- Complete bounded retry, rate-limit scheduling, auth recovery, permanent failure, timeout ambiguity, reconciliation, cancellation where supported, and operator-visible dead-letter/recovery.
- Extend Settings/Marketing/Work UI with scan/quarantine, provenance, capability/tier/cost warnings, media validation, preview, health, delivery, observation freshness, reauthorization, and reconciliation actions. Generate an updated reference before significant redesign.
- Add migrations/options validation without exposing tokens or raw provider payloads.

### 5. Constraints and preservation rules

- Do not claim an asset was scanned when only storage succeeded.
- Do not expose credentials, raw provider errors, personal destination data, or unbounded provider payloads.
- Do not advertise unsupported provider features or create mock success for unavailable credentials/tier/app approval.
- Do not blindly retry ambiguous external effects.
- Keep provider schemas in adapters and preserve protected secret storage.

### 6. Acceptance criteria

- **Given** an uploaded asset with pending or failed malware scan, **when** submission/publication/download is attempted, **then** it is blocked and quarantined with safe recovery guidance.
- **Given** an available supported media capability, **when** an approved action dispatches, **then** the correct provider flow persists immutable references and can reconcile an ambiguous outcome.
- **Given** a connection tier without a requested capability, **when** the action is prepared, **then** it is rejected before approval with plain-English tier/capability guidance.
- **Given** an expired/revoked token, **when** discovery or dispatch runs, **then** the connection becomes reauthorization-required without secret leakage or blind retry.
- **Given** provider engagement data, **when** ingested repeatedly or corrected, **then** normalized current observations are deduplicated and lineage is retained.

### 7. Verification

- Add scanner success/pending/failure/error/quarantine and malicious/polyglot/file-validation tests.
- Add creative policy, provenance, lifecycle, approval-version, audit, tenant, and authorization tests.
- Add sanitized provider contract fixtures for OAuth, discovery, media/container flow, limits, tiers, errors, retry, status, ingestion, and reconciliation.
- Add worker concurrency, idempotency, approval recheck, secret-leak, and cross-company tests.
- Add UI tests for quarantine, capability, tier/cost warning, preview, delivery, reauthorization, ambiguity, and recovery.
- Run categorized live-provider verification only with approved credentials, destinations, and cost controls; otherwise document the exact external blocker and verify unavailable behavior.

### 8. Definition of done

Creative assets have real safety/provenance evidence, providers expose only verified capabilities, supported media and observation flows are complete, and every action is immutable, approved, idempotent, reconciled, auditable, company-scoped, and recoverable. No fake scan, unsupported advertised feature, credential leak, raw schema coupling, blind retry, or mock provider success remains.

---

## Prompt 25 — Complete Marketing verification, UI evidence, and release hardening

### 1. Title and outcome

**Prove the remediated Marketing capability is production-ready end to end.** Close remaining authorization, tenant, audit, migration, worker, Web, responsive design, provider-fixture, performance, and operator-documentation gaps with executable evidence rather than build-only confidence.

### 2. Current context

- Marketing currently has broad Domain, Application, Persistence, Infrastructure, API, and Web implementation plus focused domain tests.
- Existing focused Marketing tests are concentrated in `MarketingDomainTests.cs` and `MarketingStrategyAndOperatingLoopTests.cs`; focused tests for the controller, Web client/dashboard, operating-loop service, channel dispatch worker, and journey worker are limited or absent.
- Existing references cover several Marketing surfaces, but the exact prompt-required `marketing-strategy-reference.png`, `marketing-customer-segments-reference.png`, `company-operation-marketing-initiative-reference.png`, `marketing-segment-analysis-reference.png`, and `marketing-workspace-complete-reference.png` may still be absent until prior prompts generate them.
- `docs/marketing-operations-runbook.md` and provider contracts document the current safe operating model but require reconciliation with the completed implementation.

### 3. Dependencies

- Prompts 17–24.
- All earlier Marketing prompts.
- Apply all mandatory instructions at the top of this document.

### 4. Implementation requirements

- Audit every requirement in Prompts 1–24 against concrete code, tests, migrations, UI evidence, and documentation; implement any remaining in-scope behavior rather than only recording a gap.
- Add an end-to-end Marketing integration suite covering company initiative assignment, effective authority, independent run, structured segment analysis, target review/approval, strategy linkage, downstream impact, decomposition, content/creative, approval, durable provider fixture, observation, attribution, experiment/segment learning, automatic event, company outcome review, and briefing.
- Add systematic cross-company tests for every read, write, mapping, task, workflow, approval, outbox, worker claim, provider dispatch/reconcile, observation, audit, outcome, and signal boundary.
- Add authorization-role matrix tests and verify all routes use company context, typed transport, cancellation, safe problem mapping, and correlation.
- Add focused service/worker tests for operating-loop plans, snapshot contributor, journey execution, channel dispatch, event translation, measurement, creative scanning, and provider ingestion.
- Add idempotency/concurrency/retry/reconciliation tests for scheduler-event races, repeated assignments, commands, approvals, outbox delivery, webhooks/inbound events, worker leases, provider ambiguity, observations, outcomes, and signals.
- Add complete audit/observability assertions for actor, action, target, target version, outcome, rationale, sources, correlation, before/after, attempts, approval, provider reference, and reconciliation.
- Create the missing exact design references before their UI changes and finally generate `docs/design/references/marketing-workspace-complete-reference.png`. Compare and refine the built UI at desktop and narrow widths.
- Add Web client, component, presenter, and browser tests for loading, empty, error, unauthorized, stale, blocked, paused, waiting for approval, rejected, completed, dead-letter, ambiguous, reconciliation, and unavailable-provider states.
- Verify accessibility basics: keyboard operation, focus, labels, semantic structure, contrast, responsive overflow, and plain-English status text.
- Run pending-model validation, generate and inspect migration SQL, migrate clean and upgrade local/Docker SQL Server databases, and verify backup/restore compatibility without rewriting history.
- Run focused and full affected builds/tests including Domain, Application, Persistence/Migrations, Operations, Sales Infrastructure, API, Web, Web contracts, company orchestration, agents, tasks, workflows, approvals, outbox, providers, Marketing, and background workers.
- Update the runbook with deployment, configuration, secrets, provider approval, workers, scanning, consent, approval, migration, monitoring, retry/reconciliation, dead-letter, backup/restore, and troubleshooting evidence.

### 5. Constraints and preservation rules

- Do not weaken or remove valid tests, replace production behavior with mocks, or mark external blockers as successful verification.
- Provider fixtures may simulate HTTP contracts in tests, but production behavior must use real configured adapters and safe unavailable states.
- Do not perform destructive database reset or migration-history rewrite.
- Preserve existing Finance, Sales, Support, Work, Agent team, company operation, and navigation behavior.
- Do not declare completion based only on compilation or focused unit tests.

### 6. Acceptance criteria

- **Given** the complete Marketing business path, **when** the end-to-end suite runs, **then** every transition from company instruction to outcome review is durable, versioned, source-linked, company-scoped, authorized, and recoverable.
- **Given** a foreign-company identifier at any boundary, **when** used, **then** no data existence, mutation, enqueue, provider action, audit, or signal leaks.
- **Given** duplicate requests/events/claims and ambiguous providers, **when** processed, **then** one business effect occurs and justified reconciliation reaches a stable state.
- **Given** every supported UI state, **when** rendered at desktop and narrow widths, **then** the user can understand what is happening, what needs attention, and the safe next action.
- **Given** clean and upgrade databases in local and Docker SQL Server, **when** migrations and startup validation run, **then** schema and model are consistent and background workers operate without missing-table errors.
- **Given** unavailable live credentials or provider approval, **when** release verification runs, **then** the exact blocker is documented and product behavior safely reports unavailable rather than passing falsely.

### 7. Verification

- Run the new end-to-end, authorization, tenant, worker, provider-fixture, UI, accessibility, migration, and performance suites.
- Run `dotnet ef migrations has-pending-model-changes` and inspect idempotent migration SQL where appropriate.
- Verify a clean Docker database, representative upgrade database, and documented local SQL Server path.
- Build API and Web in Debug and `LocalRun`; run the full affected test suite.
- Perform screenshot-based comparison for every new reference at desktop and narrow widths.
- Record any externally blocked live-provider check with provider, required scope/tier/approval, safe unavailable behavior, and no credentials or personal payloads.

### 8. Definition of done

All Marketing remediation prompts have production implementation and executable evidence. The system builds and starts against correctly migrated SQL Server, Maya operates within company authority, segmentation/journeys/measurement/events/creative/providers are complete and governed, UI states are verified, and operators can recover safely. No deferred in-scope TODO, missing required test, absent design reference, silent migration drift, tenant leak, unsafe external effect, or falsely claimed provider verification remains.
