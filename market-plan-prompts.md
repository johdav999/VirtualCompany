# Strategy-grounded Marketing plan implementation prompts

Execute these prompts in order. Each prompt delivers a bounded production capability and includes its own mandatory project instructions. Do not stop at an intermediate checkpoint when executing the complete pack; continue until all prompts are complete or work is genuinely blocked under `AGENTS.md`.

The repository currently contains `production-implementation.md`, `ui-instructions.md`, `docs/architecture-rules.md`, and `docs/design.md`. `AGENTS.md` also requires `architecture-inst.md` for architecture-sensitive work, but that file is not currently present. For every architecture-sensitive prompt, check again for `architecture-inst.md`; if it exists, read and follow it. If it remains absent, record that fact in the implementation summary and follow `docs/architecture-rules.md` without inventing replacement instructions.

---

## Prompt 1: Establish the strategy-grounded plan and campaign portfolio data model

### 1. Title and outcome

**Create the durable strategy-to-plan-to-campaign portfolio model.**

Deliver a production data model in which a Marketing plan records its exact approved strategy basis, selected approved segment versions, company objectives, and owned Sales campaigns. This makes the plan the durable parent planning artifact while preserving `SalesCampaign` as the only campaign system of record.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md` as required by `AGENTS.md`; if it is absent, record the absence and continue with `docs/architecture-rules.md`.
- `MarketingPlan` and `MarketingPlanObjective` are currently defined in `src/VirtualCompany.Domain/Entities/MarketingEntities.cs`.
- `MarketingPlan` currently contains dates, budget, owner, idempotency key, status, and version, but no strategy or segment relationship.
- `MarketingStrategy`, `MarketingStrategySegment`, and `MarketingStrategyCampaignLink` are currently defined in `src/VirtualCompany.Domain/Entities/MarketingStrategyEntities.cs`.
- `MarketingStrategyCampaignLink` currently combines strategy, plan, Sales campaign, and one Marketing segment version.
- `SalesCampaign` in `src/VirtualCompany.Domain/Entities/SalesCampaign.cs` is the canonical campaign aggregate and must remain so.
- Existing Marketing EF configuration is under `src/VirtualCompany.Persistence/Persistence/MarketingEntityConfigurations.cs` and `src/VirtualCompany.Persistence/Persistence/Configurations/MarketingStrategyEntityConfigurations.cs`.
- `VirtualCompany.Persistence.Migrations` is the only SQL Server migration authority.
- Existing implementation wins over older documents. Preserve existing table data, public routes, status wire values, and restore paths unless this prompt explicitly changes them.

### 3. Dependencies

None.

### 4. Implementation requirements

- Extend `MarketingPlan` with a required strategy reference for newly created strategy-grounded plans, the captured strategy version, planning rationale, evidence references, missing evidence, and optional approval request reference.
- Preserve compatibility for existing plans during migration. Use an explicit legacy/unlinked state or nullable transition columns where necessary; do not invent strategy links for historical plans without evidence.
- Add a focused `MarketingPlanSegment` entity that records the exact `MarketingCustomerSegmentVersion`, role, priority, rationale, and expected contribution for a plan.
- Add a focused `MarketingPlanCampaign` entity that links one plan to one `SalesCampaign` and records purpose, allocated budget/currency, priority, expected contribution, status, creating agent, idempotency key, and timestamps.
- Add a focused `MarketingPlanCampaignSegment` entity that links a plan campaign to one of the plan's exact segment versions with campaign-specific rationale and expected audience contribution.
- Enforce one owning Marketing plan per Sales campaign through an appropriate company-scoped unique index.
- Preserve `MarketingPlanObjective` and its existing behavior.
- Add company-scoped foreign keys using composite company/id principal keys so cross-company relationships cannot be persisted.
- Use relational columns for all core queryable state. JSON is acceptable only for bounded evidence, rationale metadata, and expected-contribution details.
- Add appropriate company-scoped indexes for strategy/version, plan/status/period, plan segments, plan campaigns, campaign lookup, status, and idempotency.
- Define deterministic domain validation and focused lifecycle/status constants without adding a generic policy engine or catch-all entity file.
- Add `DbSet` registrations and focused EF configurations in the established Persistence locations.
- Create an EF Core SQL Server migration through `VirtualCompany.Persistence.Migrations` using `VirtualCompany.Api` as startup project, and update the model snapshot.
- Backfill existing `MarketingStrategyCampaignLink` records into the new plan-segment, plan-campaign, and campaign-segment structures without deleting the old records. Make the backfill idempotent and deterministic within the migration.
- Preserve the old link table and compatibility reads for later staged migration. Do not drop it in this prompt.
- Keep `restore-local-sql-db.ps1` and `restore-virtualcompany-db.ps1` compatible with the resulting migration history and document any required operational step in the existing database documentation location.
- Add business audit support only if this prompt introduces an explicit state-changing migration/backfill operation outside normal EF migration semantics; do not write runtime audit events from an EF migration.

### 5. Constraints and preservation rules

- Follow the modular-monolith dependency direction. Domain must not depend on Application, Infrastructure, API, or Web.
- Do not introduce a `MarketingCampaign` entity or another campaign table.
- Every new entity must be tenant-owned and every relationship must preserve company scope.
- Existing records must remain readable. Do not manufacture strategy, segment, or evidence associations for legacy plans.
- Do not drop or rename existing tables or columns merely for aesthetic consistency.
- Preserve SQL Server as production provider and SQLite only where provider-compatible tests are appropriate.
- Do not add startup DDL, `EnsureCreated`, or ad hoc repair SQL.
- Keep the migration compatible with both local SQL Server and Docker SQL Server restore/run flows.
- Preserve unrelated user changes in the working tree.

### 6. Acceptance criteria

- Given an approved strategy, a plan can persist its strategy ID and exact strategy version.
- Given two approved segment versions linked to that strategy, the plan can persist both with separate roles and rationales.
- Given two Sales campaign drafts, both can be linked to the plan while each campaign has only one owning plan.
- Given a campaign segment association, the referenced segment must already belong to the plan.
- Given a cross-company strategy, plan, segment, or campaign ID, persistence is rejected and no cross-company row is created.
- Given existing `MarketingStrategyCampaignLink` data, applying the migration creates equivalent new relationships without deleting the original link.
- Given an existing plan with no provable strategy association, the migration preserves it as explicitly unlinked/legacy rather than assigning invented data.
- Given the migration is applied once, a second application attempt produces no duplicate backfill data.
- Given the current model, `dotnet ef migrations has-pending-model-changes` reports no pending change after the migration is created.

### 7. Verification

- Add focused domain tests for new entity validation and allowed relationships.
- Add Persistence/API integration tests for company-scoped foreign keys, unique ownership, and idempotency indexes.
- Add a migration test or SQL Server validation that verifies legacy-link backfill and uniqueness.
- Verify the migration and snapshot by building `VirtualCompany.Persistence`, `VirtualCompany.Persistence.Migrations`, and the narrowest affected projects.
- Run `dotnet ef migrations has-pending-model-changes` with the repository's established projects.
- Verify both local SQL Server and Docker SQL Server restore/run documentation or scripts remain valid; perform the available non-destructive checks and report any external database check that could not run.

### 8. Definition of done

The production schema, migration, backfill, domain entities, EF configurations, indexes, and tests are complete. There is no second campaign system of record, no fabricated historical linkage, no pending model change, no startup DDL, no silent cross-company relationship, and no deferred in-scope TODO.

---

## Prompt 2: Implement strategy-aware plan proposals, lifecycle, readiness, and approval

### 1. Title and outcome

**Make Marketing plans governed, strategy-aware business artifacts.**

Deliver commands and queries that prepare and commit a plan draft grounded in an approved strategy, approved linked segment versions, and measurable Marketing objectives, then govern review, approval, activation, completion, cancellation, and impact review through authoritative backend policy.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompt 1 has added the strategy-grounded plan data model and retained compatibility with existing plans and `MarketingStrategyCampaignLink`.
- Current plan contracts are in `src/VirtualCompany.Application/Marketing/MarketingContracts.cs`.
- `MarketingOperationsService` and `MarketingGovernanceService` under `src/VirtualCompany.Infrastructure.Sales/Marketing` currently prepare and commit generic plan records.
- Current proposal preparation hashes plan fields and reports missing objectives/budget, but does not validate strategy/segment relationships.
- Marketing strategy and segment approval behavior is implemented through `IMarketingStrategyService` and the shared approval subsystem.
- Current controllers are in `src/VirtualCompany.Api/Controllers/MarketingController.cs` and must remain transport-only.

### 3. Dependencies

- Prompt 1: strategy-grounded plan and campaign portfolio data model and migration.

### 4. Implementation requirements

- Replace or extend the plan proposal contract so it includes strategy ID and expected strategy version, selected segment version IDs with roles/rationales, objective IDs, period, budget, currency, planning rationale, evidence references, assumptions, risks, and missing evidence.
- Preserve existing API compatibility where practical. If a new versioned route or request is necessary, keep the old route working for legacy/manual generic plans and clearly separate its behavior.
- Add a deterministic `MarketingPlanReadinessPolicy` in the owning Application/Domain boundary. It must return allowed state, stable reason codes, plain-English explanation, approval requirement, and evidence references.
- Validate that the strategy belongs to the company, is approved or active, is valid for the plan period, and matches the expected version.
- Validate that every included segment version belongs to the company, is approved or active, and is linked to the selected strategy.
- Validate that every objective belongs to the company, has an appropriate state, and overlaps the plan period according to an explicit documented rule.
- Validate plan dates, non-negative budget, three-letter currency, evidence shape, duplicate segment roles, and required primary segment coverage.
- Create the plan, its objectives, and its segment links in one explicit transaction with a stable business idempotency key.
- Add governed lifecycle transitions for draft, in-review, approved, active, completed, and cancelled while preserving compatibility with existing persisted plan status values.
- Use the shared approval subsystem for exact-version plan approval. Activation must re-read approval state and plan version immediately before transition.
- Add impact assessment for changed strategy versions or superseded segment versions. Do not silently rewrite an existing plan; mark it as needing review with evidence and a plain-English reason.
- Add list/detail/readiness/impact queries as focused read models. Read queries must not mutate state.
- Persist audit events for proposal commitment, review submission, approval-linked activation, cancellation, completion, and blocked transitions. Include actor, agent/user attribution, target version, rationale, evidence sources, correlation ID, and outcome.
- Keep controller actions limited to authorized company/user context, request mapping, service invocation, and safe problem responses.
- Update the typed Web API client contracts without duplicating business rules in Web.

### 5. Constraints and preservation rules

- Plan draft creation is an internal state change; it does not authorize spend, publication, contact enrollment, or campaign launch.
- Approval and transition policies are authoritative backend logic, not prompts or UI conditions.
- Use optimistic concurrency and exact expected versions for mutable plan operations.
- Do not activate a strategy, segment, or objective as a side effect of plan creation.
- Do not call an LLM provider from this service.
- All commands and queries must explicitly scope `CompanyId`, including deliberate `IgnoreQueryFilters` queries.
- Preserve existing routes and wire values unless a versioned compatibility path is necessary and documented.
- Do not put EF queries or business rules in controllers or Blazor components.

### 6. Acceptance criteria

- Given an approved strategy and approved linked segments, preparing a valid plan returns a ready proposal with a deterministic proposal key.
- Given the same plan command and idempotency key twice, only one plan and one set of relationship rows exist and both responses identify the same artifact.
- Given a draft or unapproved strategy, plan commitment is blocked with a stable reason and no plan is created.
- Given a segment not linked to the strategy, plan commitment is blocked without mutating any record.
- Given a stale expected strategy or plan version, the command fails safely and explains that the basis changed.
- Given a plan awaiting approval, activation is blocked until the exact approved version is confirmed.
- Given approval for an older plan version, activation is blocked and a new review is required.
- Given a selected segment version becomes superseded, the plan remains historically intact and its impact query reports review required.
- Given a company attempts to read or change another company's plan, the operation returns no data or a safe authorization/not-found result and changes nothing.

### 7. Verification

- Add policy unit tests for every readiness and lifecycle reason code.
- Add integration tests for transactional creation, idempotency, concurrency, approval version checks, impact assessment, tenant isolation, and authorization.
- Add API contract tests for proposal, commit, detail, readiness, submit, activate, cancel, and impact responses.
- Build the narrowest Domain, Application, Infrastructure.Sales, Api, and Web client projects affected.
- Verify old plan endpoints and existing plan records remain readable.

### 8. Definition of done

Plans are production-grade governed artifacts grounded in exact strategy and segment versions. Proposal, commit, lifecycle, approval, readiness, impact, audit, compatibility, tenant isolation, and tests are complete. No state transition depends only on AI text or UI state, and no in-scope behavior is left as scaffolding or TODO.

---

## Prompt 3: Create Sales campaign drafts and populate plans with campaign portfolios

### 1. Title and outcome

**Allow a Marketing plan to be populated with real Sales campaign drafts.**

Deliver a plan-first campaign portfolio workflow in which Marketing prepares a structured portfolio proposal and then creates idempotent `SalesCampaign` drafts through a Sales-owned application boundary, preserving all existing Sales readiness and launch controls.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompts 1 and 2 have delivered the plan portfolio relationships and governed strategy-aware plan behavior.
- `SalesCampaign` in `src/VirtualCompany.Domain/Entities/SalesCampaign.cs` owns the canonical lifecycle, planning dates, objectives, offers, activities, audience, approval, and launch readiness.
- `IOutboundCampaignService.CreateCampaignAsync` currently creates an outbound campaign together with an activated sequence and selected contacts, which is too complete for early portfolio planning.
- `ICampaignPlanningService` and `CampaignPlanningService` currently configure initiatives, activities, offers, audience snapshots, readiness, and performance.
- Existing Marketing decomposition in `MarketingStrategyService` requires an existing campaign and then creates a plan, activities, briefs, and tasks. This flow must be superseded by plan-first population while preserving compatibility.
- Sales and Marketing implementations both belong to `VirtualCompany.Infrastructure.Sales`, but coordination must still occur through Application contracts rather than direct cross-service EF mutation.

### 3. Dependencies

- Prompt 1: portfolio data model.
- Prompt 2: governed strategy-aware plan commands and readiness.

### 4. Implementation requirements

- Add a focused Sales application command/service for creating an incomplete campaign initiative draft without contacts or a completed outbound sequence.
- The draft command must accept company/user/agent ownership, name, purpose, campaign type, objective, target date, planning/launch/review/end dates, time zone, budget/currency, communication language, grounded offer requirement/reference where available, and a stable idempotency key.
- Create a real `SalesCampaign` with a valid draft `SalesSequence` or the narrowest compatible Sales-owned supporting state required by the existing aggregate. Do not weaken the existing requirement for a complete sequence before launch.
- Keep new campaigns in `draft` or `planning`; never enroll contacts, activate outbound execution, schedule provider work, or launch as part of this command.
- Add a Marketing campaign portfolio proposal contract containing the owning plan/version and, per proposed campaign, purpose, objective contribution, selected plan segment versions, budget allocation, dates, channels, offer basis, activities, content needs, audience approach, measurement approach, assumptions, risks, evidence, and missing evidence.
- Validate the plan is available, belongs to the company, is in a state that accepts draft population, and matches the expected version.
- Validate every proposed campaign segment is already included in the plan and every campaign date fits within the plan period.
- Validate campaign objective contributions refer to objectives linked to the plan.
- Add a deterministic portfolio coverage policy covering objective coverage, segment coverage, duplicate purpose, budget allocation/currency, dates, channel/schedule conflicts, missing offer basis, activities, content, audience evidence, and measurement.
- Commit a portfolio transactionally and idempotently: create Sales campaign drafts through the Sales application boundary, then create plan-campaign and campaign-segment links, activities, content briefs, and internal company tasks where requested and permitted.
- If a cross-service transaction cannot be safely shared through current boundaries, design a durable workflow with explicit intermediate states and compensation/recovery. Do not leave ambiguous partial success.
- Generate audience previews only through existing Sales audience policies and services. Do not enroll audience members or send communications.
- Replace the campaign-first decomposition UI/API path with a plan-first path while retaining a compatibility endpoint for existing callers until all usage is migrated.
- Ensure content briefs created from the portfolio retain plan, campaign, exact segment version, objective, evidence, and approval policy context.
- Persist audit evidence for proposal commitment, every created campaign, relationship, task, and blocked/partial recovery outcome.

### 5. Constraints and preservation rules

- `SalesCampaign` remains the only campaign system of record.
- Sales owns campaign aggregate invariants; Marketing orchestration may not write Sales tables directly.
- Campaign creation is internal draft work and must not bypass campaign readiness, consent, suppression, offer, sequence, approval, or launch policies.
- Use stable business idempotency based on company, plan/version, strategy/version, segment versions, objective contribution, purpose, and time window.
- Prevent equivalent campaign duplication within a plan.
- Preserve existing outbound campaign creation and launch behavior for current Sales users.
- All operations must be company-scoped, authorized, audited, retry-safe, and concurrency-safe.
- External side effects remain outbox/background operations and are out of scope for draft creation.

### 6. Acceptance criteria

- Given an approved strategy-grounded plan, a valid portfolio proposal can contain multiple campaign drafts targeting different included segments.
- Given a committed portfolio, real `SalesCampaign` drafts and their plan/segment links are created and visible through existing Sales queries.
- Given the same portfolio idempotency key twice, no duplicate campaigns, activities, briefs, tasks, or links are created.
- Given a proposed segment outside the plan, the entire affected command is rejected or enters an explicit recoverable blocked state without an orphan campaign.
- Given allocations exceeding the plan budget, commit is blocked with a stable reason.
- Given campaign dates outside the plan, commit is blocked.
- Given an equivalent campaign purpose, objective, segment set, and overlapping time window already exists in the plan, the proposal reports duplication and commit does not create another campaign.
- Given a newly created campaign draft, launch/readiness still fails until the existing Sales requirements are satisfied.
- Given a cross-company plan, segment, contact, offer, or campaign reference, creation is rejected and no cross-company data is returned or persisted.

### 7. Verification

- Add focused Sales domain/application tests for incomplete campaign draft creation and preservation of launch readiness rules.
- Add Marketing portfolio policy tests for objectives, segments, budgets, dates, duplicates, channels, and missing evidence.
- Add integration tests for multi-campaign commit, idempotency, concurrency, tenant isolation, rollback or durable recovery, content/task creation, and compatibility decomposition.
- Add tests proving no contacts are enrolled and no delivery/outbox/provider action is created by draft population.
- Build and test the narrowest Sales/Marketing, API, and contract projects.

### 8. Definition of done

A plan can be populated with real, governed Sales campaign drafts through production Application contracts. Portfolio proposal, coverage, transaction/recovery, idempotency, audit, content/tasks, tenant isolation, and tests are complete. Existing Sales campaign readiness and launch behavior is preserved, and no external action occurs from draft population.

---

## Prompt 4: Add explicit Marketing agent planning and campaign tools

### 1. Title and outcome

**Give Maya explicit grounded tools for plan and campaign portfolio work.**

Deliver structured read, recommend, and internal-execution tools so Maya can independently prepare and, within configured authority, create a strategy-grounded plan draft and populate it with campaign drafts without bypassing backend policies or approvals.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompts 1–3 have delivered the portfolio model, governed plans, campaign portfolio proposals, and Sales-owned campaign draft creation.
- Marketing tool IDs are in `src/VirtualCompany.Application/Marketing/MarketingAgentAccessContracts.cs`.
- Tool metadata is registered through `StaticCompanyToolRegistry` and capability mappings through `AgentCapabilityCatalog` in `VirtualCompany.Infrastructure.Operations`.
- `MarketingAgentAnalysisService` currently loads strategies and approved segments independently, but does not provide an authoritative strategy-to-segment-to-plan coverage context.
- `MarketingOperatingLoopService` currently treats `marketing.prepare_plan` as a recommendation tool but can commit a generic plan directly when authority permits.
- The shared AI orchestration subsystem, access guard, authority policy, tool registry, guardrails, audit, and company-scoped knowledge search must be reused. No direct provider call is allowed from Marketing.

### 3. Dependencies

- Prompt 1: portfolio schema.
- Prompt 2: plan proposal/commit/readiness.
- Prompt 3: campaign portfolio proposal and population.

### 4. Implementation requirements

- Add structured read tools for active strategies, strategy-linked segment versions, objectives, existing plans, plan coverage, campaigns, campaign readiness, and performance.
- Preserve and reuse existing approved-knowledge, segment-evidence, audience-evidence, observation, attribution, and Calendar reads.
- Keep `marketing.prepare_plan` recommendation-only. It must return a structured strategy-grounded plan proposal and never mutate records.
- Add `marketing.prepare_campaign_portfolio`, `marketing.assess_plan_coverage`, and any narrowly required recommendation tool.
- Add separate internal-execution tools: `marketing.create_plan_draft`, `marketing.create_campaign_drafts`, `marketing.populate_campaign_draft`, `marketing.submit_plan_for_review`, and `marketing.submit_campaign_for_readiness`.
- Classify every tool accurately as read, recommend, or execute in the tool registry. Do not leave a recommend-classified tool performing a hidden write.
- Update Marketing capability definitions so access is explicit and conservative by default.
- Update Marketing analysis context to load exact strategy-to-segment links, current strategy versions, plan basis, plan objectives, plan segments, campaign ownership, coverage results, readiness, and versioned evidence.
- Require proposal outputs to cite supplied source IDs and distinguish confirmed facts, inferences, unknowns, and missing evidence.
- Have execute tools accept structured proposal IDs/keys, expected versions, stable idempotency keys, and correlation IDs rather than unbounded prose.
- Run the existing access guard, tenant scope, tool permission, authority, capacity, budget, readiness, and approval policies before execution and recheck immediately before every state change.
- At Recommend authority, persist only the reviewable proposal/recommendation.
- At Organize authority, allow internal task creation but no plan/campaign mutation unless separately permitted.
- At OperateInternally authority, allow idempotent internal plan and campaign draft creation, but no approval, activation, audience enrollment, provider action, publication, spend, or contact.
- Persist rationale summaries, source/evidence versions, tool executions, artifacts, policy decisions, and audit events.
- Return safe, operator-visible blocked and missing-evidence outcomes; do not silently downgrade assumptions into facts.

### 5. Constraints and preservation rules

- Use the shared orchestration subsystem; do not create a Marketing-specific LLM stack.
- Tools are explicit, structured, company-scoped, permissioned, and guarded.
- Backend policies remain authoritative. Prompt text must never be the only guardrail.
- Do not expose hidden system instructions or retrieve outside the agent's company/data scope.
- Preserve current tool IDs where they retain the same semantics; introduce new IDs for new write semantics rather than changing hidden behavior under a recommend tool.
- No tool in this prompt may launch campaigns, contact people, spend money, or publish content.
- Preserve existing agent and company autonomy behavior and make any new ceiling explicit.

### 6. Acceptance criteria

- Given Recommend authority, Maya can prepare grounded plan and campaign portfolio proposals but creates no plan, campaign, task, or external action unless the specific authority permits it.
- Given Organize authority, Maya can create an internal review task from the proposal while plan/campaign records remain unchanged.
- Given OperateInternally authority and complete evidence, Maya can create one idempotent plan draft and populate it with campaign drafts.
- Given missing approved strategy or segments, Maya reports the missing prerequisite and creates no invented plan.
- Given a tool is not permitted for Maya, execution is blocked with a stable policy reason and audited.
- Given a stale proposal or expected version, execution is blocked and requests a refreshed proposal.
- Given the same execute tool request is retried, the same artifacts are returned and no duplicates are created.
- Given any execute tool completes, its audit evidence identifies the actor agent, tool, proposal/source version, policy decision, artifacts, and correlation ID.

### 7. Verification

- Add tool-registry tests proving correct read/recommend/execute classification and capability mapping.
- Add agent access, tool permission, authority-level, tenant-isolation, stale-version, idempotency, missing-evidence, and audit tests.
- Add reasoning contract tests proving supplied strategy/segment/plan sources are bounded and output citations are validated.
- Add tests proving recommendation tools do not write and internal-execution tools cannot perform external actions.
- Build the affected Application, Infrastructure.Operations, Infrastructure.Sales, API, and agent-related test projects.

### 8. Definition of done

Maya has production structured tools that accurately separate reading, recommending, and internal execution. Grounding, permissions, authority, policies, idempotency, audit, safe failures, and tests are complete. No recommend tool hides a mutation, no direct LLM call bypasses shared orchestration, and no external action is possible through these tools.

---

## Prompt 5: Trigger deterministic plan and campaign work checks in Maya's daily run

### 1. Title and outcome

**Make the existing daily Marketing cadence assess and perform needed plan/campaign work.**

Deliver a deterministic work-need assessment at the beginning of Maya's existing daily operating run. The daily run should decide whether Marketing planning or campaign work is required, do nothing safely when no work is needed, and route actual needs through the governed tools and authority boundaries from Prompts 2–4.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompts 1–4 have delivered the portfolio model, governed commands, campaign draft workflow, and explicit Maya tools.
- `RoleAgentCadenceBackgroundService` in `src/VirtualCompany.Infrastructure.Operations/Companies/RoleAgentCadenceBackgroundService.cs` already runs active Marketing agents daily after `RoleAgentCadence:DailyHourUtc` and invokes `IMarketingOperatingLoopService.RunAsync` with a stable daily key.
- `MarketingOperatingLoopService` already resolves agent access, company pause/autonomy, assignment, capacity, budget, operating snapshot, idempotency, cooldown, analysis, actions, artifacts, and safe blocked states.
- `MarketingAgentAnalysisService` currently invokes AI for an operating-cadence analysis after snapshot capture. It does not first determine deterministically whether there is actionable plan/campaign work.
- Existing daily, weekly, and monthly cadence behavior for Finance, Sales, Support, and Marketing must remain intact.

### 3. Dependencies

- Prompt 1: portfolio data.
- Prompt 2: plan readiness and lifecycle.
- Prompt 3: campaign portfolio/readiness behavior.
- Prompt 4: explicit Maya tools and authority behavior.

### 4. Implementation requirements

- Reuse `RoleAgentCadenceBackgroundService`; do not add another scheduler, hosted service, timer, cron source, or duplicate daily trigger.
- Add a focused `IMarketingWorkNeedAssessment` Application contract and capability-owned implementation. It must be deterministic and must not call an LLM or mutate state.
- Run the assessment after agent/company/snapshot guards and before any new planning AI call, task creation, plan creation, or campaign creation.
- Assess approved/active strategy availability and validity, exact linked approved segments, active objectives, current/overlapping plans, plan end horizon, plan campaign coverage, campaign readiness, budget allocation, segment version currency, performance, content/activity deadlines, and waiting approvals.
- Return ranked need records containing a stable reason code, urgency, actionable/information-only classification, affected IDs and versions, evidence/source references, plain-English explanation, recommended tool, approval/authority implications, and stable need fingerprint.
- Support at least these reasons: strategy missing/expired, approved segments missing, objective without plan, plan missing for horizon, plan ending soon, plan without campaigns, objective without campaign coverage, target segment without campaign, incomplete campaign draft, campaign readiness due, campaign schedule conflict, budget overallocated, segment superseded, performance below plan, overdue content/activity, and waiting approval.
- Explicitly prevent information-only conditions such as a waiting approval from causing duplicate replacement plans or campaigns.
- If no actionable work is required, complete the operating run with a stable `no_work_required` outcome, persist assessment evidence and rationale, consume no planning model call, and create no actions, tasks, plans, campaigns, or other business artifacts.
- If work is required, select the highest-value permitted needs within configured task, capacity, model-call, and budget limits.
- Route selected needs through the explicit read/recommend/execute tools and backend policies from Prompt 4.
- At Recommend authority, store recommendations only. At Organize authority, create bounded internal tasks if useful. At OperateInternally, permit internal plan/campaign drafts when evidence and policy allow.
- Preserve daily idempotency based on company, agent, cadence, and date. Derive action/artifact idempotency from the need fingerprint and affected business versions.
- Prevent a second poll, retry, or duplicate trigger from recreating actions or artifacts.
- Prevent a future daily run from creating an equivalent active plan or campaign unless the need fingerprint reflects a material strategy, segment, objective, performance, status, or time-horizon change.
- Preserve cooldown behavior while ensuring a completed no-work run counts as the daily assessment.
- Persist assessment and execution outcomes in operating-run/action evidence, audit, and observability with safe error summaries.
- Classify transient snapshot/database/orchestration failures as retryable where appropriate; validation, missing prerequisites, policy denial, and approval waiting must be non-retryable operator-visible outcomes.
- Keep weekly and monthly cadence working. They may use broader horizons, but daily must always perform the plan/campaign work check.

### 5. Constraints and preservation rules

- One existing cadence path is the source of the daily Marketing check.
- Deterministic need assessment precedes model usage and business mutations.
- No work means no model cost and no artifact churn.
- The daily check does not expand Maya's configured authority.
- A terminal instruction to run daily does not authorize launch, publication, provider writes, spend, audience enrollment, or contact.
- Every query and command is company-scoped; deliberate `IgnoreQueryFilters` use must explicitly reapply `CompanyId`.
- Use bounded batches and preserve existing maximum attempts, cooldown, capacity, task, model-call, and budget controls.
- Do not swallow failures or leave a running/claimed action without a recoverable state.

### 6. Acceptance criteria

- Given an active Marketing agent and no actionable gaps, the daily cadence creates one completed operating run with `no_work_required`, evidence of what was checked, zero model calls for planning, and no business artifacts.
- Given an active objective and approved strategy/segments but no covering plan, the daily assessment reports `objective_without_plan` or `plan_missing_for_horizon` and routes it to plan preparation.
- Given a plan with no campaigns, the daily assessment reports `plan_has_no_campaigns` and routes it to portfolio preparation rather than creating another plan.
- Given a plan whose campaign portfolio misses one target segment, the daily assessment reports the exact coverage gap and proposes or creates only the missing campaign work.
- Given an approval is waiting, the run records that state but does not create a replacement plan or campaign.
- Given Recommend, Organize, and OperateInternally authority, the same need produces respectively a recommendation, an internal task, or governed draft artifacts.
- Given the same daily window is polled or retried multiple times, only one run/action/artifact set exists.
- Given tomorrow's run sees no material version or need change, it does not create an equivalent active plan or campaign.
- Given a transient assessment dependency fails, the run/action is safely retryable with bounded attempts; given a permanent policy/prerequisite block, the outcome is visible and not blindly retried.
- Given another company's records exist, they do not influence the assessment, fingerprint, recommendation, or artifacts.

### 7. Verification

- Add deterministic policy/service tests for every reason code and ranking behavior.
- Add cadence integration tests for daily invocation, no-work behavior, idempotency, duplicate polling, retry, tomorrow-with-no-material-change, and weekly/monthly preservation.
- Add authority-level tests proving the daily run does not exceed Recommend, Organize, or OperateInternally boundaries.
- Add tests verifying no model call occurs for no-work runs.
- Add tenant-isolation tests with conflicting strategies, segments, objectives, plans, and campaigns in another company.
- Add failure-path tests for unavailable snapshot, stale versions, missing evidence, capacity, budget, policy, approval, and transient internal errors.
- Build and run the narrowest Operations, Sales/Marketing, API, and cadence/operating-loop test projects.

### 8. Definition of done

Maya's existing daily cadence reliably checks whether Marketing plan or campaign work is needed and takes only the permitted, idempotent internal action. No duplicate scheduler exists. No-work runs are cheap and visible. Needed work is grounded, governed, audited, retry-safe, tenant-isolated, and covered by tests, with no external execution added.

---

## Prompt 6: Deliver plan portfolio, daily-review, and Calendar read models

### 1. Title and outcome

**Expose coherent plan, campaign portfolio, daily-review, and Calendar projections.**

Deliver optimized query/read models and API contracts that let Web and agents understand the strategy-plan-campaign hierarchy, coverage/readiness, Maya's daily assessment, and the full dated Marketing schedule without assembling transactional data in the UI.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, and `market-plan.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompts 1–5 have delivered portfolio relationships, governed behavior, agent tools, and daily need assessment.
- `MarketingDashboardDto` and related contracts are in `src/VirtualCompany.Application/Marketing/MarketingContracts.cs`.
- `MarketingOperationsService.GetDashboardAsync` currently combines scheduled campaign launches and campaign activities into `MarketingCalendarItemDto`.
- The current Calendar does not include plan periods, campaign planning/review/end milestones, or content due dates.
- `MarketingOperatingLoopService.ListAsync` and `ListActionsAsync` expose operating runs/actions, but not a user-focused daily work-check summary.
- `MarketingApiClient` contains the typed Web contracts and endpoint knowledge.

### 3. Dependencies

- Prompt 1: portfolio schema.
- Prompt 2: plan readiness/impact.
- Prompt 3: campaign portfolio/coverage.
- Prompt 5: daily work-need outcomes.

### 4. Implementation requirements

- Add a plan list read model showing plan name, strategy title/version, period, objectives, target segments, total/allocated/remaining budget, campaign count, readiness, approval/review state, owner, version, and key attention reason.
- Add a plan detail read model showing evidence, missing evidence, objectives, exact segment versions, campaign portfolio, campaign contributions, coverage findings, approvals, audit references, and allowed actions from backend policy.
- Add campaign context to relevant Marketing and Sales read models: owning plan, inherited strategy/version, target Marketing segment versions, objective contribution, allocated budget, and plan coverage/readiness.
- Add a daily Marketing review read model summarizing whether Maya found work, what evidence and versions were checked, ranked needs, actions/recommendations/artifacts, blockers, approvals, and next human action.
- Keep internal reason codes and tool IDs available for diagnostics where authorized, but map them to plain-English user-facing labels in presenter/read-model fields.
- Expand Calendar projection kinds to include plan span/start/end, campaign planning start, launch, review, end, activities, and content due dates.
- Avoid representing a zero-duration campaign launch with misleading `ends` text. Use event versus span semantics in the read model.
- Add stable source record type/ID, parent plan/campaign IDs, status, owner, attention state, and navigation target to Calendar items.
- Filter Calendar data by the requested window using overlap semantics for spans and occurrence semantics for events.
- Preserve existing dashboard metrics and current Calendar consumers through compatibility fields or adapters.
- Add focused query services where necessary; do not mutate state from dashboard or detail queries.
- Keep API controllers transport-only and update typed Web clients through `ICompanyApiTransport`.
- Ensure all read models are company-scoped, bounded, ordered deterministically, and efficient enough to avoid N+1 query patterns.

### 5. Constraints and preservation rules

- Read models do not own business decisions; allowed actions and readiness come from authoritative policies.
- Do not expose raw enum/status tokens, policy class names, trigger terminology, or tenant/platform jargon in user-facing fields.
- Preserve existing routes and contracts where practical; introduce additive contracts when compatibility is safer.
- No dashboard/read query may create missing plan links, Calendar events, or daily runs.
- Keep queries tenant-isolated and bounded.
- Do not add provider-specific schemas to read contracts.

### 6. Acceptance criteria

- Given a plan with two campaigns and two segments, the plan detail returns the complete hierarchy, contributions, budget allocation, and policy-derived allowed actions.
- Given an unlinked legacy plan, the read model clearly reports that strategy grounding is unavailable without throwing or fabricating associations.
- Given a plan period overlapping the requested Calendar window, the Calendar returns its span even if the plan started before the window.
- Given a campaign, the Calendar returns planning, launch, review, end, activities, and content due events with correct parent navigation.
- Given a no-work daily run, the daily review says Maya checked the workspace and found no work, with evidence and no misleading action.
- Given a blocked or approval-waiting run, the daily review explains what needs attention without exposing raw storage values.
- Given records from another company, no plan, campaign context, daily need, or Calendar item leaks into the response.
- Given a bounded page/window, query counts and response size remain bounded without per-row database queries.

### 7. Verification

- Add query/integration tests for plan hierarchy, legacy plans, coverage, budgets, policy actions, daily review, Calendar event/span semantics, overlap windows, ordering, and navigation IDs.
- Add tenant-isolation tests for every new read model.
- Add Web/API contract tests for additive compatibility and typed client routes.
- Add a representative query-count or SQL logging assertion where the existing test architecture supports it to prevent obvious N+1 behavior.
- Build and run affected Application, Infrastructure.Sales, API, Web client, and contract test projects.

### 8. Definition of done

Production read models and APIs expose the full strategy-plan-campaign hierarchy, daily Marketing review, and accurate Calendar schedule. They are additive or compatibly migrated, tenant-isolated, policy-backed, bounded, tested, and free of write side effects or UI-owned business logic.

---

## Prompt 7: Implement the Marketing plan portfolio and daily-review user experience

### 1. Title and outcome

**Make plans, campaign portfolios, Calendar work, and Maya's daily check understandable and actionable.**

Deliver the Marketing Web experience for browsing strategy-grounded plans, reviewing a plan's campaign portfolio, asking Maya to populate a plan, understanding Maya's daily work check, and viewing complete plan/campaign dates in the Calendar.

### 2. Current context

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, `market-plan.md`, `ui-instructions.md`, and `docs/design.md`. Check for and follow `architecture-inst.md`; if absent, record that and continue with `docs/architecture-rules.md`.
- Prompts 1–6 have delivered backend behavior and read models.
- The current Marketing workspace is `src/VirtualCompany.Web/Pages/Marketing/MarketingDashboard.razor` with tabs including Strategy, Segments, Objectives, Plans, Calendar, Content, and Operations.
- The current Plans tab is a simple list/form and the Calendar is a linear list.
- Existing design tokens, components, spacing, typography, colors, navigation, agent identity, and interaction patterns are authoritative. Existing implemented design wins over older planning documents.
- The screenshot-first workflow in `ui-instructions.md` and `docs/design.md` is mandatory for significant new UI or redesign work.

### 3. Dependencies

- Prompt 2: plan lifecycle/readiness/actions.
- Prompt 3: portfolio proposal/population.
- Prompt 5: daily work-check outcomes.
- Prompt 6: plan, campaign, daily-review, and Calendar read models/APIs.

### 4. Implementation requirements

- Before changing the UI, write an explicit screenshot prompt based on `docs/design.md`, generate the reference image with the approved image model, and save it under `docs/design/references/marketing-plan-portfolio-reference.png`.
- The reference must show the existing Marketing workspace context, calm modern SaaS styling, Maya's identity, plan list/detail hierarchy, campaign portfolio, coverage/readiness, budget, daily review, clear actions, loading/empty/blocked states, and responsive behavior.
- Implement the UI to closely match the reference while reusing existing design-system components and tokens.
- Reshape the Plans experience into an operational list/detail pattern or focused detail view that shows strategy/version, objectives, exact target segments, period, budget allocation, campaign count, readiness, approval state, evidence, and what needs attention.
- Add a clear `Ask Maya to populate plan` action that invokes the governed proposal/population flow. Show a reviewable proposal before committing when authority requires review.
- Show policy-derived actions only: submit for review, activate after approval, review impact, or open linked campaigns. Do not recreate eligibility rules in Razor.
- Present campaign portfolio rows/cards with purpose, target segments, objective contribution, dates, allocated budget, readiness, owner, and direct navigation to campaign details.
- Add a coverage section that explains uncovered objectives/segments, duplicate purposes, budget/date conflicts, missing evidence, and the next useful action in plain English.
- Add a daily-review surface in Overview or Operations showing whether Maya found work today, what she checked, what she recommended/created, blockers/approvals, and the next human action.
- Use user-facing outcomes such as `No work needed today`, `Plan draft ready`, `Campaign drafts created`, `Waiting for approval`, and `Needs evidence`; do not expose raw reason codes or tool IDs.
- Update the Calendar to distinguish spans and milestones and show plan dates, campaign planning/launch/review/end, activities, and content due dates with appropriate links.
- Preserve existing module navigation and horizontal tab behavior. Do not add a new top-level application navigation destination.
- Preserve contextual Maya presence and a direct way to message or ask Maya about the selected plan.
- Implement loading, empty, unavailable, stale-version, missing-evidence, waiting-approval, blocked, partial-recovery, and success states without mock production data.
- Keep forms and API mutations version-aware and display safe concurrency refresh guidance.
- Ensure desktop and smaller-width responsive behavior, accessible labels, keyboard navigation, focus states, and sensible status semantics.
- After implementation, run the Web app only if runtime browser verification is necessary and follow the repository's Local Web Verification rules. Compare the rendered UI with the saved reference screenshot and refine material differences.

### 5. Constraints and preservation rules

- `docs/design.md` and the implemented design system are authoritative if the generated reference conflicts.
- The reference image is a design target only and must not ship as a UI asset.
- Do not introduce a new UI framework, visual style, or raw technical vocabulary.
- Blazor components consume backend policy/read-model decisions and must not duplicate plan or campaign business rules.
- Do not add mock production data or silently substitute offline values.
- Preserve current routes or provide compatibility redirects where a detail route is added.
- Use `ICompanyApiTransport` through focused typed Marketing/Sales API clients.
- Keep Maya helpful but controlled; the UI must not imply she can approve or launch work beyond configured authority.

### 6. Acceptance criteria

- Given a grounded plan, the user can see its strategy, exact segments, objectives, budget, campaign portfolio, readiness, evidence, and next action without opening raw records.
- Given a plan with no campaigns, the user can ask Maya to prepare/populate the portfolio and sees a reviewable proposal or governed draft result appropriate to authority.
- Given uncovered objectives or segments, the UI explains the gap and links to the appropriate action.
- Given today's daily run found no work, the UI clearly says no work was needed and shows what Maya checked.
- Given Maya created drafts, the UI identifies the plan/campaign artifacts and distinguishes them from approved or launched work.
- Given approval or evidence is missing, the UI shows the blocker and the correct human action without offering an invalid activation/launch button.
- Given Calendar data, plan spans and campaign/activity/content milestones are visually distinguishable and navigate to the right record.
- Given a stale version response, the UI preserves user context, asks for refresh/review, and does not silently overwrite changes.
- Given a smaller viewport, tabs, list/detail content, campaign cards, and actions remain usable without horizontal page overflow beyond intended tab scrolling.
- Given the final rendered UI, it is visually close to the saved reference and follows existing tokens and interaction patterns.

### 7. Verification

- Save and inspect `docs/design/references/marketing-plan-portfolio-reference.png` before implementation.
- Add focused Blazor component/page tests for plan list/detail, policy actions, portfolio proposal/commit, daily-review outcomes, coverage states, Calendar kinds, concurrency, loading, empty, blocked, and authorization-safe behavior.
- Add typed API client tests for all new routes, company headers, correlation, cancellation, not-found, and safe error mapping.
- Build `VirtualCompany.Web` and run the narrowest relevant Web and Web contract tests.
- If browser verification is required, check for an existing repository Web host first, use the prescribed startup method, record any started PID, use bounded health polling, visually compare against the reference, and stop only the recorded process.
- Verify accessibility basics and responsive layouts at representative desktop and narrow widths.

### 8. Definition of done

The screenshot-first workflow, production Blazor implementation, typed clients, states, navigation, accessibility, responsive behavior, visual comparison, builds, and tests are complete. The UI accurately communicates Maya's daily check and the strategy-plan-campaign hierarchy without exposing technical internals, duplicating backend rules, overstating autonomy, using mock data, or leaving in-scope TODOs.
