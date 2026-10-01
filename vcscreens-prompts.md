# Virtual Company screens — implementation prompts

Date: 2026-10-01  
Status: Proposed implementation sequence. No prompt, journey, or release is marked implemented or approved by this document.

## Purpose

Implement [releaseplan.md](releaseplan.md) in 30 ordered prompts. Each prompt delivers a reviewable business outcome, including the subscreens, reports, data, controls, and recovery behavior required to use it. This document specifies implementation work; creating it does not execute that work.

| Release | Prompts | Target | Approval package |
| --- | --- | --- | --- |
| 1 — My working day | P01–P09 | Understand today's priorities and complete the next action | Five responsibility perspectives and their complete daily journeys |
| 2 — My agent team | P10–P18 | Understand, direct, and control agent work | Agent work, collaboration, decisions, supported autonomy policies, and verified execution controls |
| 3 — My business over time | P19–P30 | Review performance and turn future choices into owned work | Period reviews, management reports, annual plans, scenarios, and timely briefings |

Prompts are units of delivery, not estimates or sprint commitments. The dependency column gives prerequisites; the numbered sequence is the default execution order.

## How to execute this pack

Read the shared implementation contract and relevant current-code map below, then execute the selected prompt. These sections are part of every prompt. No conversational context is required.

For example: **“Implement P01 from vcscreens-prompts.md, including its shared implementation contract, and finish its verification and evidence.”**

When a release or the full sequence is authorized, continue through its ordered prompts under the repository's persistence rules. Fix in-scope failures and continue; an intermediate build result is not completion. Release approval is a recorded human decision, separate from implementation readiness. Prepare concrete working journeys before requesting that decision. Do not record approval, deploy, or enable additional production authority merely because this pack exists.

Reinspect the targeted implementation before changing it. The code map records the baseline inspected on 2026-10-01; it is a starting point, not permission to replace newer work. Preserve unrelated working-tree changes.

## Shared implementation contract

### Required instructions

Every prompt follows:

- [Production implementation instructions](production-implementation.md).
- [Architecture rules](docs/architecture-rules.MD), especially the sections relevant to the changed modules.
- [Product design rules](docs/design.md) and the subordinate [UI companion](ui-instructions.md).
- [Prompt guidance](docs/AGENTS.md), root [repository guidance](AGENTS.md), and all applicable scoped instructions under `src`, `tests`, and `docs`.
- [Release scope](releaseplan.md) and the [canonical route inventory](docs/ui-route-inventory.md).

Every UI prompt below requires the mandatory reference-first workflow in `docs/design.md`. Use the existing [reference gallery](docs/design/references/role-time-agent-2026-10-01/index.html) where it covers the screen. Before implementing an uncovered new or substantially redesigned screen, write its image-generation prompt and create its own reference under `docs/design/references/`. One overview reference does not cover all subscreens. Compare the built screen with its reference and refine it. Reference images contain illustrative values; never put these values into production data paths.

For actual UI review, polish, screenshot comparison, and user-flow acceptance work, invoke the installed `polish-uat-loop` skill as required by `src/AGENTS.md`. Follow `src/VirtualCompany.Web/AGENTS.md` for browser-host startup and process ownership. Do not claim live acceptance from a static reference or an isolated component test.

### Scope, screen register, and evidence

P01 creates `docs/vcscreens/screen-report-register.md`; every later prompt updates it. Create one entry per actual screen/report and significant detail view, tab, or export, with:

- Stable ID, exact route or explicit proposed route, role/access scope, parent journey, release, and implementing prompt.
- Treatment: **New**, **Redesigned**, **Adapted**, or **Retained**.
- Sources, authoritative measure/calculation, filters, actions, approval rules, persistence, and export behavior.
- Dependencies, reference image, implemented screenshot, repeatable acceptance scenario, and known limitations.
- Separate implementation, automated verification, live verification, and human approval states, with evidence and reviewer/date where applicable.

Map all release-plan capabilities in P01. Expand detailed rows before implementing their release; future rows may be explicitly proposed. No unclassified destination or silent scope removal. Retained screens stay usable and are tested through the new entry points. A missing screen needed to finish an in-scope journey must be implemented with that journey.

Keep progress in `docs/vcscreens/implementation-status.md`. Store per-prompt evidence under `docs/verification/vcscreens/Pxx/`; release acceptance packages go under `docs/verification/vcscreens/release-1/`, `release-2/`, and `release-3/`. Record actual commands, results, environment prerequisites, screenshots, and unresolved issues. These are outputs to create during implementation, not files claimed to exist now.

### Production, persistence, and execution

Use real authenticated APIs, typed clients, existing owning modules, and durable business records. Shared UI does not justify a second orchestration, reporting, authorization, or approval implementation.

Each prompt names its expected persistence effects. Apply the **Database and EF Core** section of `docs/architecture-rules.MD` to schema changes, including migrations and real SQL Server checks where required. Document when no migration is needed; do not add tables merely to mirror a view model.

For any external side effect named in a prompt, follow the **Workflow and Approval** and **External Side Effects and Outbox** sections. Approval, current policy, scope, expiry, and limits must be evaluated at the appropriate execution boundary. Prepared, approved, sent, provider-confirmed, failed, uncertain, and achieved outcomes must remain distinct.

### Shared UI and reporting acceptance

Every changed journey must preserve company, authorized responsibility, record, period, filters, and return context. Role selection changes presentation; it does not grant access. Direct links, summaries, counts, caches, evidence, and exports enforce the same relevant scope.

Implement loading, empty, stale, partial, restricted, validation-error, concurrency-conflict, failed, and retry states where applicable. Distinguish unavailable data from zero. Dates use the appropriate company/user timezone and reports disclose period, as-of time, source coverage, currency, and calculation meaning.

Every report has one authoritative definition in its owning module. Totals, filtered drill-downs, and exports reconcile under the same scope. Preserve existing reports, including Monthly and separate financial statements, throughout all releases. A report's later redesign does not delay its existing or required operational use.

### Verification and common definition of done

Use the owning test projects described by `docs/architecture-rules.MD` and `tests/AGENTS.md`:

- `tests/VirtualCompany.Api.Tests`: composed API and infrastructure behavior.
- `tests/VirtualCompany.Finance.Tests`: focused Finance rules.
- `tests/VirtualCompany.SupportGrounding.Tests`: support grounding and safety.
- `tests/VirtualCompany.SalesSource.Tests`: Sales source and provider behavior.
- `tests/VirtualCompany.Web.Tests`: presentation, typed clients, components, and navigation.
- `tests/VirtualCompany.Web.Contract.Tests`: Web/API compatibility when contracts change.

Run focused tests for the changed behavior and the appropriate API/Web builds. Add meaningful tests for new policies, calculations, transitions, isolation, and concurrency; do not replace behavioral checks with source-text assertions. Run migration tests and SQL Server integration where the changes require them. Broaden checks at release integration or when evidence of regression warrants it; avoid unchanged full-suite repetitions.

Browser acceptance uses repeatable test-company data and role accounts, with fixtures separate from production behavior. Provider-dependent acceptance needs a configured test integration, approved test recipients/resources, and usable credentials. Do not expose secrets, perform live money movement, or contact real customers merely to demonstrate a screen. Missing credentials block the affected live check, not unrelated implementation; report the exact gap and leave that check unverified.

Every prompt's definition of done includes: complete production behavior, no scaffolding, mock production data, silent failure, unhandled intermediate state, or deferred in-scope TODO; passing applicable checks; updated register and evidence; and an honest separation of implemented, automatically verified, live verified, and approved.

## Current-code map

Paths below are reuse points verified during preparation. Inspect their current collaborators, routes, contracts, and tests before editing; the presence of a service does not prove the proposed journey is complete.

| Area | Existing implementation and useful tests |
| --- | --- |
| C1 — Role workspace | `src/VirtualCompany.Web/Pages/Dashboard.razor`; `Components/Dashboard/TodayWorkspace.razor` and `MonthlyWorkspace.razor`; Web `Services/TodayWorkspaceViewModels.cs`, `MonthlyWorkspaceViewModels.cs`, `DashboardRoutes.cs`, `ReturnUrlNavigation.cs`; `Pages/ResponsibilitySettings.razor`. Operations `Companies/CompanyTodayWorkspaceQueryService.cs`, `CompanyTodayWorkspaceLensResolver.cs`, `CompanyMonthlyWorkspaceQueryService.cs`. Application `Cockpit/TodayWorkspaceContracts.cs` and `MonthlyWorkspaceContracts.cs`. Existing TodayWorkspace, MonthlyWorkspace, and responsibility tests. |
| C2 — Human work and agents | Web `Pages/Work.razor`, `AgentStaffOverview.razor`, `Components/ApprovalInbox.razor`, `ApprovalDetail.razor`, `ActivityCorrelationPanel.razor`, `ActivityDetailDrawer.razor`. Operations `Companies/CompanyAgentStaffOverviewQueryService.cs`, `CompanyTaskService.cs`, `CompanyTaskCommandService.cs`, `CompanyApprovalRequestService.cs`. Existing AgentStaffOverview and collaboration tests. |
| C3 — Orchestration and authority | Application `Orchestration/MultiAgentCollaborationContracts.cs` and `CompanyOperatingContracts.cs`; Domain `Entities/CompanyOperatingEntities.cs`, `OperatingDispatch.cs`, and `Enums/CompanyOrchestrationEnums.cs`. Operations `Companies/CompanyOperatingAutonomyPolicy.cs`, `CompanyOperatingConfigurationService.cs`, `OperatingPlanValidationService.cs`, `OperatingWorkDispatcher.cs`, `AgentEffectiveAuthorityResolver.cs`. Web `Services/AgentAuthorityTransparencyPresenter.cs`. Existing CompanyOperatingAutonomyPolicy, AgentEffectiveAuthorityResolver, and MultiAgentCollaboration tests. |
| C4 — Finance grants | Application `Finance/Contracts/FinanceAutonomyGrantContracts.cs` and `FinanceAutonomyWorkflowTemplateCatalogue.cs`; Operations `Companies/FinanceAutonomyGrantService.cs`, `FinanceAutonomyExecutor.cs` and its approval, budget, run, and trigger services; API FinanceAutonomy controllers. Existing FinanceAutonomy policy, lifecycle, migration, executor, trigger, and SQL Server tests. |
| C5 — Sales | `src/VirtualCompany.Infrastructure.Sales/Sales/SalesTodayWorkspaceContributor.cs`, `SalesMonthlyWorkspaceContributor.cs`, `RevenueForecastService.cs`; Domain `Entities/RevenueForecastSnapshot.cs`; Web `Pages/Sales/` and existing opportunity, contact, meeting preparation, and closing workflows. Existing SalesOperationsApiIntegration, SalesAnalyticsDashboardEndpoint, RevenueForecastService, and Sales browser/closing tests. |
| C6 — Marketing | `src/VirtualCompany.Web/Pages/Marketing/MarketingDashboard.razor` at `/marketing`; Marketing is owned by `src/VirtualCompany.Infrastructure.Sales/Marketing/`, including Today/Monthly contributors, MarketingPlanPortfolioService, MarketingMeasurementService, MarketingGovernanceService, and MarketingDeliveryService. Existing MarketingWorkspaceSurface, MarketingPlanPortfolio, MarketingMeasurementPolicy, and MarketingOperatingAction tests. |
| C7 — Finance and accounting | `src/VirtualCompany.Infrastructure.Finance/Finance/` owns Finance Today/Monthly contributors, reports, and accounting workflows. Web `Pages/Finance/AccountingReportsPage.razor`, `Components/Finance/FinancialStatementReport.razor`, and `FinancialStatementWorkspace.razor`; Application `Finance/Contracts/FinancialStatementWorkspaceContracts.cs`; `FinancialStatementWorkspaceService.cs` in Infrastructure.Finance. Existing treasury, close, reporting, and financial-statement tests. |
| C8 — Support | `src/VirtualCompany.Infrastructure.Support/Support/` owns Today/Monthly contributors and case/agent services; `src/VirtualCompany.Api/Controllers/SupportController.cs`; Web support queue, case, and knowledge screens. Existing SupportAgentService, SupportAgentDecisionService, and SupportGroundingSafety tests. |
| C9 — Planning and briefings | Domain `Entities/PlanningEntities.cs` already contains Budget and Forecast; operating plans/initiatives are in C3. Finance planning contracts and Operations `Companies/FinancePlanningContextProjector.cs` exist. Web `Pages/BriefingPreferences.razor`; Application `Briefings/`; Operations `Companies/CompanyBriefingService.cs`, `CompanyBriefingSchedulerInfrastructure.cs`, and `CompanyBriefingUpdateJobRunner.cs`. Existing FinancePlanning persistence/endpoint tests and Briefing scheduler/preference/aggregation tests. |

In this map, Web-relative paths start at `src/VirtualCompany.Web/`, Application at `src/VirtualCompany.Application/`, Domain at `src/VirtualCompany.Domain/`, and Operations at `src/VirtualCompany.Infrastructure.Operations/`. Test names identify existing suites to inspect, not a claim they already cover the new requirements.

## Prompt index by release

### Release 1 — My working day: P01–P09

| Prompt | Reviewable outcome | Dependencies |
| --- | --- | --- |
| P01 | Authorized role shell and connected navigation | None |
| P02 | Priority → evidence → next action | P01 |
| P03 | CEO daily decision journey | P02 |
| P04 | Sales daily opportunity journey and operational reports | P02 |
| P05 | Marketing daily campaign journey and operational reports | P02 |
| P06 | Finance cash and obligations journey | P02 |
| P07 | Accounting close and statement journey | P06 |
| P08 | Support case journey and operational reports | P02 |
| P09 | Release 1 integrated journeys and approval package | P01–P08 |

### Release 2 — My agent team: P10–P18

| Prompt | Reviewable outcome | Dependencies |
| --- | --- | --- |
| P10 | Agent board and durable work detail | P09 |
| P11 | Collaboration and handoff detail | P10 |
| P12 | One consistent human decision workflow | P10–P11 |
| P13 | Agent work and decisions inside business records | P12 |
| P14 | Configured and effective authority explained | P10, P12 |
| P15 | Preview, apply, and enforce task-type policies | P14 |
| P16 | Pause, resume, and recovery with truthful execution state | P15 |
| P17 | Agent supervision reports | P11–P16 |
| P18 | Release 2 integrated control evidence and approval package | P10–P17 |

### Release 3 — My business over time: P19–P30

| Prompt | Reviewable outcome | Dependencies |
| --- | --- | --- |
| P19 | Weekly reviews for all five roles | P18 |
| P20 | Monthly reviews and reproducible saved snapshots | P19 |
| P21 | Sales management analysis and capacity planning | P20 |
| P22 | Marketing performance and budget decisions | P20 |
| P23 | Finance variance and rolling forecast | P20 |
| P24 | Support quality and capacity planning | P20 |
| P25 | Quarterly objectives and resource review | P21–P24 |
| P26 | Versioned annual operating plan | P25 |
| P27 | Deterministic multi-year scenario comparison | P23, P26 |
| P28 | Review decisions become traceable owned work | P20, P25–P27 |
| P29 | Timely briefings, preferences, and absence routing | P19–P20, P28 |
| P30 | Release 3 integrated reviews and approval package | P19–P29 |

## Release 1 — My working day

### P01 — Authorized role shell and connected navigation

**Title and outcome.** Make the existing Overview a dependable entry point for CEO, Sales, Marketing, Finance/accounting, and Support responsibilities, with working navigation into existing business screens.

**Current context.** C1 already supports Today/Monthly at `/dashboard` and authorized company, finance, sales, marketing, and customers lenses. MainLayout and NavMenu already expose business areas. Support is a user-facing responsibility over the existing customers lens; do not invent a new permission role from a label.

**Dependencies.** None. Use existing company membership and responsibility configuration. No new provider credentials.

**Implementation requirements.**
- Create the screen/report register covering every release-plan capability; fully resolve Release 1 routes and treatment before changing them.
- Implement consistent role selection, defaults, company switching, deep links, and return navigation. Handle no assigned responsibility and multiple responsibilities.
- Preserve Today and existing Monthly and module destinations. Make changes observable through the real lens resolver, typed clients, and authenticated endpoints.
- Reference-first workflow applies. Reuse the current shell and references 01, 02, and 05; create separate missing role-home references before their implementation prompts.
- Persistence: reuse memberships/responsibilities; persist any new view preference through the existing preference owner with a migration only if required. Document route and access decisions.

**Constraints and preservation rules.** Apply the shared contract. Role choice must never expand permissions or retain another company's cached state. Preserve canonical/compatibility routes and restricted utilities.

**Acceptance criteria.** A dual-responsibility user can switch perspectives and open the correct module record; company switching clears incompatible context. A restricted direct link cannot expose data. Existing Monthly remains usable after reload and browser back.

**Verification.** Extend TodayWorkspace API/component and lens-resolution tests for switching, unauthorized lenses, context restoration, and cache isolation. Verify keyboard navigation and responsive shell in the browser; run affected builds.

**Definition of done.** Shared definition of done is met; all five authorized perspectives are reachable, retained routes are classified, and real navigation works. This prompt does not claim later role-specific content is complete.

### P02 — Ranked priorities with evidence and a working next action

**Title and outcome.** Let a user understand an urgent item, verify its evidence, and reach the action that resolves it.

**Current context.** C1 already carries priority reasons, human/agent ownership, deadlines, freshness, evidence identifiers, deep links, and visibility reasons. C2 provides Work, tasks, approvals, and activity details. Extend this contract rather than introducing a parallel notification queue.

**Dependencies.** P01. Existing domain records are sufficient; provider-backed freshness checks require the relevant configured source.

**Implementation requirements.**
- Deliver three to five ranked priorities with deadline, business impact, accountable human, working agent, next action, and a concise explanation of why the item matters now.
- Complete an evidence/details surface with source timestamps, authorized record links, partial-source warnings, and current task/approval state.
- Make ordering stable and explainable; incorporate urgency and materiality without hiding overdue work. Distinguish intraday changes from the daily summary and refresh state after relevant actions.
- Reuse durable task/approval IDs and return context. Reference-first workflow applies to the priority detail surface.
- Persistence: use existing work and evidence records; store a per-user “last seen” marker only if needed for change indicators. Do not duplicate financial or module evidence.

**Constraints and preservation rules.** Apply the shared contract. Showing a priority or acknowledging it must not approve or execute its proposed action. Missing evidence is visible, not silently replaced by AI text.

**Acceptance criteria.** Opening a priority shows matching source records and the correct action. Completing the action updates the persisted detail and refreshed priority state. A stale or unavailable source is labeled, while protected evidence remains inaccessible through direct links and summaries.

**Verification.** Extend TodayWorkspacePriorityOrdering, TodayWorkspaceQueryService, integration, and Web tests. Exercise stale/partial data, deadline boundaries, duplicate priorities, unauthorized evidence, and a real priority-to-action browser journey.

**Definition of done.** Shared definition of done is met; the priority flow is functional across contributor types, with no dead-end primary action or fabricated freshness.

### P03 — CEO daily decisions and company health

**Title and outcome.** Give the CEO a concise company situation and a complete path from a consequential risk to an owned decision and its evidence.

**Current context.** C1 aggregates domain contributions; C2/C3 provide decisions and operating work. Existing Finance reports and budget/forecast records can support cash exposure and performance-against-plan without waiting for new annual planning.

**Dependencies.** P02; existing department data and permissions. No new integration, but connected-source validation requires the configured test company.

**Implementation requirements.**
- Implement the company Today perspective using reference 01: material risks, decisions, cash outlook, existing plan comparison, accountable owners, and compact agent outcomes.
- Complete risk/decision drill-down and departmental handoffs; reuse existing approval/task commands for review, assignment, or follow-up.
- Deliver or adapt the company-health operational report with source coverage and authorized drill-downs. Reuse Finance/Sales measures; show “no approved baseline” when there is no usable plan.
- Reference-first workflow applies to new risk/report details. Identify actual retained report routes in the register.
- Persistence: reuse work/decision records; add only missing durable decision/evidence links with the required migration and audit. Log failed projections without disclosing protected source content.

**Constraints and preservation rules.** Apply the shared contract. A CEO label is not permission to expose restricted accounting details. Management acknowledgement does not satisfy transaction approval.

**Acceptance criteria.** A CEO opens a cash or revenue risk, sees its dated evidence, assigns or reviews the next step, and sees that action after reload. The summary and authorized department report reconcile. An absent budget produces a gap rather than an invented favorable variance.

**Verification.** Test cross-module aggregation, duplicate initiative suppression, evidence authorization, and persisted decisions. Browser-test risk → detail → action → updated overview and report; run affected API/Web checks.

**Definition of done.** Shared definition of done is met; the CEO journey and its operational reports are independently usable.

### P04 — Sales opportunity-to-action journey

**Title and outcome.** Let Sales move from today's opportunity or commitment through customer context and proposal/follow-up review to an updated pipeline.

**Current context.** C5 includes Sales Today/Monthly contributors, opportunity/contact screens, meeting preparation and closing, analytics, and persisted revenue forecast snapshots. Existing Sales routes are under `/app/sales`.

**Dependencies.** P02; configured Sales data. Email/calendar or proposal-delivery acceptance requires the existing controlled test integration and recipients.

**Implementation requirements.**
- Implement Sales Today from reference 02 with follow-ups, meetings, due commitments, and exceptions.
- Connect opportunity, contact/customer history, meeting preparation, proposal review, and next-action details. Preserve the canonical presentation-preset preparation flow and existing room closing/capture behavior.
- Deliver reachable pipeline, overdue-activity, and near-term forecast reports with filters, calculation meaning, currency, evidence, and supported exports.
- Persist follow-ups and review actions through existing owning services; refresh affected overview/report state. Reference-first workflow applies to significantly changed subscreens.
- Persistence: reuse opportunity, activity, approval, and forecast entities; migrate only missing durable context links. External effects include calendar changes and outbound proposal/follow-up delivery; use existing workflow/outbox boundaries and confirmed delivery states.

**Constraints and preservation rules.** Apply the shared contract. Do not redesign voice/media or expand sending/discount authority as part of navigation work.

**Acceptance criteria.** A salesperson follows one opportunity to a reviewed next action and sees the resulting activity and pipeline after reload. Forecast drill-down matches its included deals. Stale approval, provider failure, and uncertain delivery have explicit recovery states.

**Verification.** Extend SalesOperationsApiIntegration, SalesAnalyticsDashboardEndpoint, RevenueForecastService, affected closing/client tests, and authorization coverage. Browser-test the complete journey and reconcile report filters/exports; use live test-provider checks only where available.

**Definition of done.** Shared definition of done is met; every essential Sales subscreen and operational report is shipped with the journey.

### P05 — Marketing campaign-to-review journey

**Title and outcome.** Let Marketing identify today's campaign work, inspect the content and audience, and complete the next launch or revision decision.

**Current context.** C6 owns the existing `/marketing` workspace, campaign/plan portfolio, content and creative flows, governance, delivery, and measurement. It lives in Infrastructure.Sales; do not create a parallel Marketing backend.

**Dependencies.** P02; existing campaigns, budgets, and channel configuration. Controlled publishing/delivery acceptance requires the existing test channel.

**Implementation requirements.**
- Create a separate Marketing Today reference; reference 03 is a weekly view reserved for P19. Implement daily launches, content reviews, spend exceptions, and missing-attribution priorities.
- Complete campaign → content/asset → audience/context → review → revision or authorized launch navigation with durable selected-record URLs.
- Deliver operational campaign-delivery, spend-versus-budget, and available lead-result reports. Show attribution coverage, unknown spend, and unavailable outcomes explicitly.
- Reference-first workflow applies to new/major campaign and review details.
- Persistence: reuse portfolio, content, governance, measurement, and approval records; migrate only required missing links/state. Publishing or channel delivery is an external effect and must use existing dispatch, approval, and outbox boundaries. Surface rejected assets and delivery failures.

**Constraints and preservation rules.** Apply the shared contract. Approval of content does not by itself grant publication authority or a spend increase. Do not infer causal campaign success from incomplete attribution.

**Acceptance criteria.** A marketer opens a due launch, reviews its exact content and audience, requests a revision or performs an authorized next step, and sees persisted status in the overview and report. Missing budget or attribution is visible. Unauthorized users cannot access assets or exports.

**Verification.** Extend MarketingWorkspaceSurface, MarketingPlanPortfolio, MarketingOperatingAction, and measurement-policy tests. Check content-version approval binding, tenancy, report reconciliation, retry/delivery states, and the browser campaign journey.

**Definition of done.** Shared definition of done is met; Marketing has a daily workflow and its required operational reports, independently of later management analysis.

### P06 — Finance cash and obligations journey

**Title and outcome.** Let Finance move from a cash or due-obligation priority through the underlying invoice, bill, transaction, reconciliation, or payment review.

**Current context.** C7 already has cash, customer invoice, supplier bill, payment, transaction, issue, and reconciliation workflows. C4 and existing accounting authority impose independent controls.

**Dependencies.** P02; the existing finance configuration and test ledger/bank/provider data. Provider confirmation checks need a controlled test integration.

**Implementation requirements.**
- Create a Finance Today reference and implement due obligations, receivables, cash exposure, reconciliation exceptions, and clear close-work links.
- Connect the complete authorized invoice/bill/payment/transaction and reconciliation paths, preserving filters and return location.
- Deliver accessible receivables/payables aging and short-term cash forecast reports, reusing owning calculations. Define as-of semantics, currency, forecast assumptions, and drill-down/export scope.
- Reference-first workflow applies to changed detail/report surfaces.
- Persistence: reuse financial records, approvals, and reconciliation state; migrate required missing links only. External effects can include payment-provider instructions, provider updates, and financial messages; use canonical workflow/outbox boundaries, with pending/confirmed/uncertain outcomes and audit.

**Constraints and preservation rules.** Apply the shared contract. No GUI level or dashboard action changes accounting/payment approval rules. Do not mark a requested payment settled or aggregate incompatible currencies without the owning conversion policy.

**Acceptance criteria.** Finance inspects an overdue item, follows the authorized next action, and sees consistent persisted status and aging/cash results. Reviewers can explain a forecast value from its source/assumption. Duplicate requests and uncertain provider results do not produce duplicated money movement.

**Verification.** Run affected treasury authorization, Finance policy, reporting, and API/client tests, including cutoff dates, tenancy, approval enforcement, duplicate/recovery behavior, and report reconciliation. Browser-test the full path with controlled data.

**Definition of done.** Shared definition of done is met; daily Finance actions and operational reports are usable, with provider checks accurately labeled.

### P07 — Accounting close and separate financial statements

**Title and outcome.** Let accounting move from a close blocker or report discrepancy to its source and back to a consistent close/report workspace.

**Current context.** C7 includes the close workspace and accounting reports. FinancialStatementWorkspace and FinancialStatementReport already implement distinct statement presentation; current working-tree work must be preserved. Existing statement references are available in `docs/design/references/`.

**Dependencies.** P06; configured accounting permissions, periods, accounts, and test ledger. No new external provider is required for the local report journey.

**Implementation requirements.**
- Connect Finance Today to close tasks, journals, reconciliations, compliance evidence, and existing financial reports through canonical destinations.
- Preserve distinct income statement and balance sheet screens, their filters, export/print behavior, drill-downs, and available saved snapshots.
- Show close status, period boundaries, account/source evidence, and actionable discrepancies. Preserve separate movement and point-in-time balance semantics.
- Reference-first workflow applies to changed screens; use separate statement references rather than combining the reports into one mockup.
- Persistence: reuse statement/report snapshots and close state; migrate missing provenance links only. Preserve existing posting, locking, reopening, and provider-sync workflows and their audit/approval boundaries.

**Constraints and preservation rules.** Apply the shared contract. Do not replace ledger calculations with dashboard calculations or imply statutory approval from a successful technical check. Keep accountant-portfolio access within its explicit membership scope.

**Acceptance criteria.** An authorized accountant opens a close blocker, resolves or records the permitted next step, and sees the persisted close state. Each statement reconciles with its ledger drill-down and export for the same period and filters. Restricted roles cannot reach accounting detail through CEO summaries.

**Verification.** Run affected FinancialStatementWorkspace, financial-statement drill-down, Finance period-reporting, close-policy, component, and authorization tests. Verify both statements and the close round trip in the browser, including no-data and locked-period behavior.

**Definition of done.** Shared definition of done is met; close and existing statements are reachable and validated in Release 1, regardless of later analytical enhancements.

### P08 — Support case-to-resolution journey

**Title and outcome.** Let Support move from an urgent case or SLA risk through customer and knowledge context to a reviewed response or specialist handoff.

**Current context.** C8 includes support queue, case details, knowledge, agent decisions, and grounding. The role workspace uses the authorized customers lens.

**Dependencies.** P02; test cases, SLA policies, and accessible knowledge sources. Outbound reply acceptance requires the configured controlled support channel.

**Implementation requirements.**
- Implement the Support Now perspective from reference 05: due cases, SLA deadlines, escalations, waiting states, and accountable owners.
- Complete case → customer history → knowledge evidence → reply review or specialist handoff → updated queue navigation.
- Deliver SLA-risk, backlog, unresolved-case, and aging reports with clearly defined business-calendar and paused/waiting semantics.
- Reference-first workflow applies to new/major case and report details.
- Persistence: reuse case, reply, assignment, and escalation records; migrate missing durable handoff/evidence links only. Customer replies are external effects under the existing workflow/outbox rules. Surface grounding gaps, rejected replies, retries, and uncertain delivery.

**Constraints and preservation rules.** Apply the shared contract and the architecture's Support Grounding and Safety section. Customer text and retrieved content cannot authorize agent actions. A draft is not a delivered reply or resolved case.

**Acceptance criteria.** A support user follows an SLA priority, checks sources, requests or approves the authorized response/handoff, and sees the persisted case and report state. Unsupported answers remain reviewable with an explicit evidence gap. Reopened and waiting cases are counted consistently.

**Verification.** Extend SupportAgentService, SupportAgentDecisionService, grounding/safety, report, and Web tests. Check tenancy, restricted knowledge, business-hour deadlines, duplicate delivery, and browser case-to-queue round trips.

**Definition of done.** Shared definition of done is met; the complete Support journey and operational reports can be tested and approved together.

### P09 — Complete and verify Release 1

**Title and outcome.** Deliver one coherent daily-work release and a concrete approval package covering all five roles.

**Current context.** P01–P08 provide the individual journeys. Integration can expose mismatched context, totals, states, responsive layouts, or permissions that isolated checks miss.

**Dependencies.** P01–P08 implemented with recorded evidence. Role-specific test accounts and configured integrations are needed for their corresponding live checks.

**Implementation requirements.**
- Run each release-plan daily demonstration from overview through evidence, essential subscreens, action, persisted result, and operational report.
- Use the required polish/UAT workflow to fix discovered in-scope defects, then rerun the failing journey. This is a completion-and-fix prompt, not a report-only checkpoint.
- Cover dual responsibilities, company switching, retained Monthly/screens, partial/stale data, restricted records, failed actions, and return navigation.
- Finish the register and release-1 acceptance package with separate reference/implemented screenshots, report reconciliation evidence, repeatable scripts, issue ledger, and explicit approve/revise fields.
- Persistence and external effects: validate migrations and effect boundaries introduced by P01–P08; add further changes only to fix evidenced defects. Reference-first workflow applies if a fix materially redesigns a screen.

**Constraints and preservation rules.** Apply the shared contract. Do not hide incomplete required subscreens behind “future release” labels or mark unavailable live checks passed.

**Acceptance criteria.** Every role completes its defined daily journey and sees matching persisted/report state. No unresolved defect blocks a promised path, leaks data, misstates a report, or misrepresents an action. Human release approval is pending until actually supplied.

**Verification.** Run the focused regression set for fixes, integrated authorization/report checks, and appropriate API/Web builds. Record browser and controlled integration evidence separately from automated tests.

**Definition of done.** The working release and review package meet the shared definition of done. Report implementation readiness, unresolved external validation, and actual approval status separately.

## Release 2 — My agent team

### P10 — Agent team board and durable work detail

**Title and outcome.** Let a user see what agents are doing and open a work item that explains its business objective, ownership, evidence, and next dependency.

**Current context.** C2 already provides the Agent team overview at `/agents/staff`, Work, and activity details. C3 has durable operating initiatives, dispatches, tasks, and collaboration records. The existing board is a reuse point, not evidence of complete state coverage.

**Dependencies.** P09 implementation readiness; existing agent roster and durable work data. No new integration credentials.

**Implementation requirements.**
- Extend the board with planned, active, awaiting approval, completed, blocked, failed, and paused work, with filters by responsibility, agent, and objective.
- Deliver a work-detail destination with human accountability, participating agents, current step, dependency, outputs, evidence, and timestamps. Provide contextual links from Today and Work.
- Derive labels from authoritative lifecycle records and join by durable identifiers. Display a shared initiative once at company-outcome level.
- Create separate board and work-detail references before implementation; the reference-first workflow applies.
- Persistence: reuse tasks/initiatives/dispatches; add missing durable cross-record links via migration if needed. Expose permission-filtered query endpoints and pagination; instrument missing/stale projections and failed refreshes.

**Constraints and preservation rules.** Apply the shared contract. Do not rename persisted lifecycle values or infer completion from a generated artifact or an inactive worker.

**Acceptance criteria.** A user opens a board item and sees the same identity/state in Work and its business record. A blocked item names the actionable dependency. Refresh and navigation preserve filters; unauthorized work cannot be inferred from counts or agent summaries.

**Verification.** Extend AgentStaffOverviewIntegration, AgentStaffOverviewApiClient, lifecycle query, and Web tests. Verify state mapping, duplicate suppression, partial data, tenant isolation, pagination, and board-to-detail browser navigation.

**Definition of done.** Shared definition of done is met; the board is backed by real durable work and every displayed item has a usable detail path.

### P11 — Collaboration, contributions, and handoffs

**Title and outcome.** Let a user follow how agents contribute to one business objective and identify the handoff or decision holding it up.

**Current context.** C3 already models collaboration plans, workers, contribution artifacts, parallel/sequential handoffs, and contributor/reviewer/challenger roles. P10 establishes the shared work identity and detail destination.

**Dependencies.** P10; existing collaboration execution paths and permitted artifacts. No new external provider.

**Implementation requirements.**
- Implement the collaboration flow from reference 08 with participating agents, current steps, dependencies, handoffs, failures, and human decision points.
- Add accessible chronological/list alternatives and an artifact panel showing the version passed, source references, review outcome, and concise business rationale.
- Show disagreements or revisions only when supported by recorded contributions. Connect the final result to the parent objective and relevant business record.
- Reference-first workflow applies to graph, list, and contribution detail where newly designed.
- Persistence: reuse collaboration records; persist missing typed handoff/artifact-version relationships and events in the owning orchestration model with migrations. Support retries without creating duplicate apparent contributions. Audit access-sensitive operations and expose projection failures.

**Constraints and preservation rules.** Apply the shared contract. Show business evidence and recorded conclusions, not hidden reasoning traces. Delegation must not broaden access or execution authority.

**Acceptance criteria.** A Sales renewal can be followed through Finance/Support contributions to a proposal revision and human decision. The graph and list show the same durable sequence, artifacts, and current blockers. Restricted contributions are handled without leaking content.

**Verification.** Extend MultiAgentCollaboration tests for parallel/sequential work, failed handoffs, retries, versioned outputs, and restricted artifacts. Verify graph/list parity, keyboard navigation, responsive layout, and a complete collaboration browser journey.

**Definition of done.** Shared definition of done is met; users can explain the actual collaboration and open each permitted contribution without relying on a decorative animation.

### P12 — One consistent human decision workflow

**Title and outcome.** Let an authorized reviewer inspect, approve, reject, or request changes to the exact proposed action from any entry point.

**Current context.** C2 supplies ApprovalInbox, ApprovalDetail, and CompanyApprovalRequestService. C3/C4 and owning business modules enforce different approval policies. P10–P11 expose work and collaboration links.

**Dependencies.** P10–P11; actual approver permissions and proposed actions. Controlled provider credentials are needed only when validating an approved external delivery.

**Implementation requirements.**
- Create a decision-review reference and implement proposal summary, affected records/recipients, before/after values, evidence, policy reason, expiry, and current approval status.
- Use one approval identity/version from Today, Work, agent work, collaboration, and the business record.
- Complete approve/reject/request-changes behavior, validation, concurrent-review handling, changed-proposal invalidation, and return navigation.
- Persistence: reuse approval and decision records; migrate missing proposal binding/version fields if necessary. Audit reviewer, time, scope, decision, and material changes.
- External effects include the existing approved send, publication, provider, or accounting workflow reached by the decision. Review commands must respect each owner's workflow/outbox boundary and never imply provider completion.

**Constraints and preservation rules.** Apply the shared contract. Preserve self-approval restrictions, mandatory reviewers, expiry, execution-time revalidation, and owning-module rules. Requesting changes must not leave the original proposal executable.

**Acceptance criteria.** The same approval has one current state from all entry points. Material edits require the required new review. Concurrent or repeated approval requests cannot produce duplicate execution. Rejection and change requests visibly stop the pending proposal.

**Verification.** Test authorization, tenancy, material-change binding, stale versions, expiry, concurrency, repeated commands, and dispatch revalidation. Browser-test all review outcomes and a controlled approved-delivery path.

**Definition of done.** Shared definition of done is met; review is a functioning business control with consistent state and history.

### P13 — Agent contributions inside business subscreens

**Title and outcome.** Let each role understand and supervise agent work directly from the business record they are handling.

**Current context.** P04–P08 supply the business journeys; P10–P12 supply work detail, collaboration, and consistent decisions. C5–C8 retain ownership of proposals, cases, invoices/bills, and campaigns.

**Dependencies.** P12 and the Release 1 business screens. Existing test integrations suffice for any delivery checks.

**Implementation requirements.**
- Add contextual agent work/evidence panels to Sales proposals, Support cases, Finance invoices/bills, and Marketing campaigns/content.
- Show contributed artifacts, active handoffs, proposed versus completed actions, applicable approval reason, and the next permitted user action.
- Link to the same P10–P12 work and decision records; reuse shared presentation components while preserving each module's workflow semantics.
- Reference-first workflow applies to substantial screen/panel changes; create separate role-specific references where not already covered.
- Persistence: reuse durable associations; add only missing typed business-record/work links with migration. Handle refresh/concurrency and record failed associations without inventing a status. External actions remain owned by the existing business command and outbox.

**Constraints and preservation rules.** Apply the shared contract. A reused widget must not flatten important distinctions such as draft versus delivered reply or proposed versus posted accounting change.

**Acceptance criteria.** From each named business screen, an authorized user sees the correct agent contributions and can reach the current decision. A decision elsewhere updates the business view after refresh. Missing evidence and failed delivery remain visible, and restricted artifacts stay restricted.

**Verification.** Exercise at least one full path per business area, including stale approval and partial contribution data. Test shared component mapping, cross-company link rejection, authorization, and affected typed-client contracts.

**Definition of done.** Shared definition of done is met; agent supervision is integrated into all four business journeys, with no separate copy of approval logic.

### P14 — Explain configured and effective authority

**Title and outcome.** Let users understand what an agent may do for a task type and why a configured level may be restricted.

**Current context.** C3's company autonomy values are Recommend, Organize, OperateInternally, and ControlledExecution. C4 Finance grants use different capability/level semantics. AgentAuthorityTransparencyPresenter already distinguishes permission, approval, configuration, integration, and implementation limits.

**Dependencies.** P10 and P12; existing company configuration, agent capabilities, and policy evaluation. No new credentials.

**Implementation requirements.**
- Implement an authority summary in work detail and a settings view using four clear positions: **Advise**, **Organize**, **Do internal work**, **Act within limits**.
- Use an accessible labeled segmented/radio control presentation, with explicit consequence descriptions; do not rely on a decorative gauge alone. Editing is delivered by P15.
- Show company limits, agent capability, task-type policy, effective actions, mandatory review, amount/volume/time limits, expiry, and a plain-language restriction reason.
- Reference-first workflow applies; use reference 09 as the baseline and create missing authority-detail references.
- Persistence: no new authority model is expected. Add typed projections/adapters over owning policy decisions; version/cache them correctly and record diagnostic evaluation failures.

**Constraints and preservation rules.** Apply the shared contract. Never equate numeric values across company, agent, and Finance models. Existing company ControlledExecution does not remove its external-action human review. Unsupported capability states must remain explicit.

**Acceptance criteria.** A user can distinguish configured intent from current permission for a supported task. Expiry, missing integration, required review, or denied access gives the correct reason. A settings view cannot grant the reader extra authority.

**Verification.** Extend AgentEffectiveAuthorityResolver and AgentAuthorityTransparencyPresenter/component tests with a company/agent/task policy matrix, expiry, missing integration, and unsupported capabilities. Verify keyboard/screen-reader labels and browser explanation consistency.

**Definition of done.** Shared definition of done is met; the visible authority explanation comes from authoritative policy evaluation and makes no false execution promise.

### P15 — Task-type policy preview, activation, and enforcement

**Title and outcome.** Let an authorized user preview and apply a bounded task policy, then see its actual effect on eligible work.

**Current context.** C3/C4 already provide company limits, capability evaluation, versioned Finance grants, templates, trigger controls, budgets, approval coordination, and execution checks. P14 supplies the common explanation layer.

**Dependencies.** P14; policy-management permission and configured agents. Use existing approved execution paths; no new provider capability is implied.

**Implementation requirements.**
- Deliver task-type settings, a no-effect preview, policy history, activation, revision, expiry, and revocation. Show changed permissions, affected agents/actions, limits, simulation inputs, and before/after versions before applying.
- Start with an explicit catalogue: Sales account research and proposal drafting as separate types; Marketing content drafting; Support grounded reply drafting; Finance stale-cash monitoring and its bounded internal review-task action from the existing template catalogue. Keep drafting and customer delivery separate capabilities.
- Resolve catalogue entries to real owning-module capability IDs and supported levels. Implement the missing adapter/command/check needed for each declared type end to end; do not render an enabled policy for unsupported execution. List other types as unavailable or existing separately governed workflows.
- Use company policy as a limit, task type as the everyday setting, and narrower agent/task constraints where supported. Evaluate effective permission through owning policies rather than a numeric minimum.
- Persist versioned policies/diffs, actor, scope, limits, activation/expiry, and preview-input references. Reuse existing grant stores; add only justified owning-model persistence with migrations.
- Reference-first workflow applies, using reference 09 plus separate preview/history details. Recheck policy, grants, budgets, and approvals on execution, retries, and delegation. Preview must not invoke delivery tools or consume live action budgets.

**Constraints and preservation rules.** Apply the shared contract. Finance templates explicitly exclude sensitive actions such as money movement, posting, final close, statutory signoff, external communication, self-approval, and ambiguous-outcome resolution from generic enablement. Preserve those exclusions. Existing approved external workflows remain separately governed under workflow/outbox rules.

**Acceptance criteria.** For every supported catalogue type, demonstrate an eligible action, a review-required or configured restriction case, and a denied case with accurate explanations. A policy changed, revoked, or expired after queuing is honored before execution. Concurrent activation cannot overwrite a newer version silently.

**Verification.** Extend owning policy/command tests and FinanceAutonomy grant, executor, budget, trigger, lifecycle, and migration tests where affected. Test no-effect simulation, tenant isolation, stale versions, expiry, concurrency, delegation, budget limits, and real dispatch-path enforcement. Browser-test preview → apply → history → observed work behavior.

**Definition of done.** Shared definition of done is met; every supported task type has both user-facing controls and verified enforcement. Record the exact capability catalogue and unsupported boundaries in the register.

### P16 — Pause, resume, and recover execution

**Title and outcome.** Let authorized users stop new agent execution and understand what is queued, already running, completed, or awaiting reconciliation.

**Current context.** C3 includes company pause/emergency-stop checks and durable dispatch state; C4 adds Finance runs and execution controls. P15 supplies current policy versions and eligibility.

**Dependencies.** P15; test dispatch workers and controlled provider stubs/integrations for in-flight and ambiguous-outcome scenarios.

**Implementation requirements.**
- Deliver company-wide pause and supported scoped pause/resume controls, separate from autonomy levels, with a precise scope and impact preview.
- Enforce pause at scheduling/claim/execution/retry boundaries and across delegation. Define which already-running steps can stop cooperatively and which must complete or reconcile.
- Show queued, paused-before-start, stopping, in-flight, confirmed-completed, and uncertain outcomes based on durable state, with authorized recovery actions.
- Reference-first workflow applies to execution-control and recovery screens.
- Persistence: reuse company configuration and dispatch/run lifecycle; persist missing scope, control revision, actor, timestamps, and recovery state through migrations. Make control commands idempotent and expose propagation lag/failure.
- External effects include already-dispatched sends/provider instructions; use canonical outbox/reconciliation rules and safe retry eligibility.

**Constraints and preservation rules.** Apply the shared contract. Pause cannot undo completed delivery/payment. Resume must reevaluate current policies and approvals; uncertain execution must not automatically resend.

**Acceptance criteria.** After pause takes effect, queued work cannot begin a new controlled effect; running work has an accurate status. Other allowed scopes behave as documented. Resume cannot revive revoked authority, expired approval, or an unresolved ambiguous provider outcome.

**Verification.** Exercise dispatch concurrency and races around claim, approval, send, retry, pause, and resume. Run affected SQL Server lifecycle/migration checks. Browser-test scoped/global controls and recovery with unauthorized-user and duplicate-command cases.

**Definition of done.** Shared definition of done is met; visible controls have demonstrated execution effects and truthful recovery behavior.

### P17 — Supervision reports with traceable outcomes

**Title and outcome.** Let managers review agent outcomes, bottlenecks, corrections, and policy exceptions and open the exact work behind every aggregate.

**Current context.** P10–P16 provide durable work, contributions, approvals, policy changes, and execution controls. Existing activity/history and reporting components can host the presentation; owning records remain authoritative.

**Dependencies.** P11–P16; enough test lifecycle history to validate each measure. No new provider credentials for recorded-data reporting.

**Implementation requirements.**
- Deliver reports for verified outcomes, blocked work, approval turnaround, corrections/rework, execution failures, and policy exceptions, scoped by company, responsibility, task type, and time.
- Define denominator, status, timestamp, and deduplication rules. Separate prepared output, provider-confirmed execution, and demonstrated business outcome.
- Count a shared initiative once at company-outcome level while exposing agent contributions separately. Do not label unmeasured productivity savings as verified benefits.
- Provide filtered drill-downs, evidence/history, and authorized export where required by the register. Reference-first workflow applies to report and detail screens.
- Persistence: derive from durable history; add only missing typed lifecycle events/projections needed for reproducibility, with migration and bounded backfill. Mark unavailable pre-instrumentation history rather than fabricating it.

**Constraints and preservation rules.** Apply the shared reporting contract. Suppressed restricted records cannot leak through counts or exports; technical retries must not inflate business outcomes.

**Acceptance criteria.** Every reported total reconciles with its permitted detail rows. One collaborative initiative is not several company outcomes. Waiting time and failure/correction measures reproduce from their events, including ongoing work and period boundaries.

**Verification.** Test aggregation definitions, duplicate/retry handling, time calculations, partial history, authorization, and export parity. Browser-test a bottleneck → work → decision/history path.

**Definition of done.** Shared definition of done is met; supervision reports are explainable and traceable to actual work.

### P18 — Complete and verify Release 2

**Title and outcome.** Deliver a coherent agent-supervision release whose controls can be demonstrated against real execution behavior.

**Current context.** P10–P17 implement the board, collaboration, review, authority, policies, pause, and reports. The critical remaining risk is disagreement between what the UI promises and what dispatch actually does.

**Dependencies.** P10–P17; test agents, approvers, dispatcher, and controlled integrations for supported external-action checks.

**Implementation requirements.**
- Run the renewal demonstration: Sales research → Finance/Support contribution → proposal revision → human approval → confirmed controlled delivery.
- Open one decision from Today, Work, collaboration, and its business record; verify one state and history.
- Demonstrate policy preview/activation, review and denial cases, revocation/expiry after queueing, delegation, pause races, and ambiguous-outcome recovery.
- Use the required polish/UAT workflow, fix discovered in-scope gaps, and rerun affected flows. Finish the release-2 package and supported task-type catalogue.
- Include graphical/list and keyboard acceptance, execution evidence, migrations, report reconciliation, and clear unsupported capability states. Reference-first workflow applies to major corrective redesigns.
- Persistence/external effects: validate those introduced by this release; fixes must remain in the owning workflow/outbox and migration boundaries.

**Constraints and preservation rules.** Apply the shared contract. A changed badge or successful policy API response alone does not prove enforcement. Preserve all Release 1 journeys.

**Acceptance criteria.** Supported policies have demonstrated effects, mandatory review survives retries/material changes, and pause/delegation cannot bypass restrictions. Every outcome is represented accurately. Unresolved control, access, duplicate-action, or truthful-state defects prevent readiness.

**Verification.** Run focused regression checks for fixes, integrated authority/approval/dispatch tests, required SQL Server checks, and appropriate builds. Attach separate browser and controlled-provider evidence.

**Definition of done.** Shared definition of done is met; the reviewable release package distinguishes implementation readiness, external validation gaps, and actual human approval.

## Release 3 — My business over time

### P19 — Weekly reviews for all five roles

**Title and outcome.** Let each role review this week's commitments, changes, and near-term risks using meaningful weekly measures.

**Current context.** C1 already has Today and Monthly projections, period contracts, timezone-aware comparisons, and role contributors. A weekly experience must extend these foundations with weekly semantics rather than reuse Today's values under a new tab.

**Dependencies.** P18 implementation readiness; source dates/history and company timezone/calendar configuration. No new provider is required for recorded data.

**Implementation requirements.**
- Deliver Week in the existing workspace, with period selection and context-preserving drill-down.
- Cover CEO commitments and risks; Sales pipeline movement and follow-ups; Marketing campaign delivery and launches; Finance short-term liquidity and obligations; Support backlog movement and SLA performance.
- Define week boundaries, comparable prior periods, week-to-date versus completed week, movement versus balance measures, and incomplete history. Use module-owned calculations.
- Reuse reference 03 for Marketing Week; create separate references for uncovered role variants and weekly details. Reference-first workflow applies.
- Persistence: use existing records and period services where sufficient; add missing historical events/snapshots only where necessary for true movement measures, with migrations and explicit coverage start dates. Preserve existing Today/Monthly contracts and record projection failures.

**Constraints and preservation rules.** Apply the shared reporting contract. Do not estimate historical pipeline/backlog movement from current state without identifying the limitation. Do not add unusable future-horizon tabs.

**Acceptance criteria.** Each role reviews a weekly change, opens the underlying records, and returns to the same period. Partial weeks and sparse history are labeled. A balance is compared at the correct points in time; period activity uses the correct interval.

**Verification.** Extend period/contributor and client/component tests for timezone, week starts, daylight-saving changes, fiscal/calendar configuration, authorization, and report reconciliation. Browser-test all five weekly perspectives.

**Definition of done.** Shared definition of done is met; Week provides distinct useful information for every role and retains working Today/Monthly navigation.

### P20 — Monthly management review and reproducible snapshots

**Title and outcome.** Let each role conduct a monthly review and save a dated, reproducible record of what was reviewed.

**Current context.** C1 already offers Monthly results, comparisons, source coverage, decisions, and agent outcomes. C7 has existing financial snapshots/reports; C9 has operating snapshots. Reuse these where their semantics fit instead of moving all reports into a new generic store.

**Dependencies.** P19; existing monthly measures and permitted source history. No new provider credentials for saved-data review.

**Implementation requirements.**
- Extend the five Monthly perspectives with results against available targets, material variances, source-linked explanations, close/data readiness, and review decisions.
- Deliver save/open/list snapshot behavior with company, responsibility, period, filters, as-of time, source coverage, measure definitions, input values or immutable input versions, and calculation version.
- Preserve immutable reviewed results when source data later changes. Offer an explicit refreshed version and explain the difference; references to mutable IDs alone are insufficient for reproduction.
- Reuse reference 04 for Finance Month; create missing role-review and snapshot-history references. Reference-first workflow applies.
- Persistence: implement the missing review aggregate and versioned snapshot payload/provenance in the appropriate owner, with migrations, concurrency rules, size limits, and retention/access behavior. Audit save/revise and report reproduction failures.
- Keep deeper Sales/Marketing/Finance/Support analysis for P21–P24 while providing a complete monthly review using existing authoritative measures now.

**Constraints and preservation rules.** Apply the shared contract. Saving a snapshot does not freeze or expand the viewer's access rights. Do not overwrite existing financial snapshots or disguise a forecast as an actual.

**Acceptance criteria.** A saved review reopens with the same results after underlying data changes, and a refreshed revision shows its new sources/version. Each role can drill down or see a clear source-access/history limitation. Review totals reconcile with its snapshot-scoped export.

**Verification.** Test snapshot reproduction, immutability, source correction/deletion handling, concurrency, authorization, retention boundaries, and migrations on SQL Server. Extend MonthlyWorkspace tests and browser-test save → change source → reopen → refresh.

**Definition of done.** Shared definition of done is met; monthly reviews are usable and their saved results can be reproduced from preserved inputs and definitions.

### P21 — Sales management analysis and capacity planning

**Title and outcome.** Let Sales explain performance trends and record a capacity or territory proposal grounded in real opportunity history.

**Current context.** C5 has Sales analytics, opportunity history, and RevenueForecastSnapshot with 30/60/90-day projections. P19–P20 provide period navigation and reproducible reviews.

**Dependencies.** P20; sufficient opportunity stage/outcome history and permitted ownership data. No new external integration.

**Implementation requirements.**
- Deliver conversion, win/loss, sales-cycle, forecast movement, and territory/owner analysis with explicit cohorts, denominators, stage definitions, currency, and missing-history coverage.
- Link every summary to its included opportunities and recorded outcome reasons. Distinguish missing win/loss reasons from inferred explanations.
- Add a bounded capacity/territory proposal: versioned assumptions for available selling capacity, expected workload, and target allocation, with deterministic calculations and an accountable owner.
- Integrate report results with P20 snapshots; reuse existing Sales report destinations where appropriate. Reference-first workflow applies to new report and planning detail screens.
- Persistence: reuse Sales history and forecast snapshots; add only missing history or versioned planning assumptions/proposals with migrations. Audit revisions and expose insufficient data rather than synthesizing past events.

**Constraints and preservation rules.** Apply the shared reporting contract. A capacity proposal does not reassign customers, change compensation, or send commitments. Actuals, forecasts, and planning assumptions remain visibly distinct.

**Acceptance criteria.** A sales manager explains a conversion change from the selected cohort, reconciles a forecast to its opportunities, saves a capacity proposal, and reopens the same assumptions/results. Incomplete cohorts or zero denominators produce an accurate limited state.

**Verification.** Extend Sales analytics/forecast tests with cohort boundaries, reopened/lost deals, stage-history gaps, multi-currency scope, and authorization. Test proposal persistence/calculation and browser report-to-opportunity/snapshot paths.

**Definition of done.** Shared definition of done is met; the management report and bounded planning subscreen are operational and reproducible.

### P22 — Marketing performance and budget decisions

**Title and outcome.** Let Marketing compare channel performance, understand attribution limits, and record an evidence-based budget proposal.

**Current context.** C6 already owns attribution runs, allocations, experiment evidence/decisions, campaign budgets, and plan portfolios. P20 supplies review snapshots; P05's operational reports remain available.

**Dependencies.** P20; campaign spend, outcomes, attribution model/version, and experiment evidence where available. Existing connected channels are needed only for live ingestion validation.

**Implementation requirements.**
- Deliver channel comparison, available acquisition economics, experiment history, attribution coverage, and campaign/segment drill-down.
- Show formulas, denominators, attribution windows/model versions, spend coverage, and the difference between attributed outcomes and causal experiment evidence.
- Implement a versioned budget proposal with current/proposed allocations, available constraints, accountable owner, and impact assumptions. Validate totals and currency.
- Integrate proposal and report snapshots; reuse existing measurement/governance services. Reference-first workflow applies to analytical and budget-detail screens.
- Persistence: reuse measurement and portfolio records; migrate missing versioned budget proposal and source-snapshot links only. Audit revisions and report ingestion/coverage failures.

**Constraints and preservation rules.** Apply the shared contract and existing marketing measurement policies. A budget proposal or favorable metric does not publish content or raise actual spend limits; those actions remain separate controlled workflows.

**Acceptance criteria.** A marketer can explain channel results from spend/outcome evidence, identify unmeasured coverage, inspect an experiment decision, and save/reopen a reconciled budget proposal. A channel with missing costs cannot display a fabricated acquisition cost.

**Verification.** Extend MarketingMeasurementPolicy, MarketingPlanPortfolio, attribution and Web/client tests for model versions, duplicates, incomplete sources, experiment guardrails, currency, and export reconciliation. Browser-test analysis → evidence → budget proposal → snapshot.

**Definition of done.** Shared definition of done is met; Marketing management decisions use traceable measures and persisted assumptions.

### P23 — Finance variance and rolling forecasts

**Title and outcome.** Let Finance explain actual-versus-plan differences and maintain a reproducible rolling forecast.

**Current context.** C7 owns actuals and financial reporting. C9 already contains Budget and Forecast entities, planning endpoints, and FinancePlanningContextProjector. C5's revenue forecast is a possible named input, not a replacement for finance actuals.

**Dependencies.** P20; configured accounts/dimensions, comparable budget versions, actuals, and approved currency/conversion rules. No new provider is required for persisted inputs.

**Implementation requirements.**
- Deliver variance analysis with period/dimension/version selection, source drill-down, explanatory notes, and historical comparisons.
- Implement a rolling-forecast editor that preserves actual periods, supports explicit assumptions for future periods, validates currency and dimensions, and saves named versions.
- Show actual, budget, forecast, and scenario values distinctly. Make revenue/cash timing, opening balances, and source gaps explicit where used.
- Reuse authoritative report/planning APIs and integrate with review snapshots. Reference-first workflow applies to report, forecast editor, and version comparison.
- Persistence: extend existing Budget/Forecast versioning and provenance only where needed; migrate missing revision, assumption, or audit links. Do not replace current finance planning storage solely to support a new layout.

**Constraints and preservation rules.** Apply the shared contract. Forecast approval does not post journals, change closed actuals, release payments, or constitute statutory approval. Preserve separate financial-statement semantics.

**Acceptance criteria.** Finance explains a variance through source records, changes a forecast assumption, sees the deterministic result, saves a version, and reopens identical values. A closed period's actuals remain unchanged. Reports and exports reconcile for the selected dimensions/version.

**Verification.** Extend FinancePlanning persistence/endpoint/context and period-reporting tests for revisions, concurrency, decimal/currency rules, fiscal periods, negative values, source gaps, and tenancy. Run relevant Finance/migration checks and browser-test the variance-to-forecast journey.

**Definition of done.** Shared definition of done is met; forecast editing and comparison use real persisted financial inputs and reproducible calculations.

### P24 — Support quality and capacity planning

**Title and outcome.** Let Support explain service-quality trends and plan staffing from observed workload and explicit assumptions.

**Current context.** C8 owns cases, SLA behavior, knowledge, and agent decisions. P08 provides operational reports; P20 adds reproducible period reviews.

**Dependencies.** P20; case lifecycle history, SLA calendars, and available staffing/workload inputs. No new external provider.

**Implementation requirements.**
- Deliver service-quality, recurring-issue, reopen-rate, response/resolution-time, and backlog trends with precise cohorts and waiting/paused-time definitions.
- Link recurring issues to permitted source cases and evidence. Label machine-proposed groupings and allow correction rather than asserting an unsupported root cause.
- Implement a bounded capacity proposal using arrival volume, handling-time assumptions, business hours, available capacity, and service targets. Show calculation limits and accountable owner.
- Connect reports/proposals to saved reviews and existing case/knowledge destinations. Reference-first workflow applies to trend, issue, and capacity detail screens.
- Persistence: reuse case events; migrate missing historical events or versioned grouping/capacity assumptions where necessary. Record coverage start, revisions, audit, and failed aggregations.

**Constraints and preservation rules.** Apply the shared contract. A staffing proposal does not change case permissions, staffing schedules, or customer promises automatically. Do not infer employee performance from incomplete agent/case activity.

**Acceptance criteria.** A support lead reproduces a reopen-rate result from its cohort, drills into a recurring issue, changes a capacity assumption, and saves/reopens the proposal. Waiting cases and cross-period reopen events are handled consistently.

**Verification.** Test lifecycle cohorts, business calendars, reopened/merged cases, partial history, calculation boundaries, tenant restrictions, and snapshot/export parity. Browser-test quality → case evidence → capacity proposal.

**Definition of done.** Shared definition of done is met; service trends and capacity proposals are usable and evidence-based.

### P25 — Quarterly objectives and resource review

**Title and outcome.** Let leadership review objectives, capacity, and forecast changes across departments and record a coordinated quarterly decision.

**Current context.** C3 already has operating plans, initiatives, dependencies, decisions, and reviews. P21–P24 supply department-level reports and bounded planning proposals.

**Dependencies.** P21–P24; company fiscal calendar, accountable owners, and usable review snapshots. No new external integration.

**Implementation requirements.**
- Deliver Quarter with objective progress, milestones, resource/capacity constraints, dependency risks, and revisions to the outlook.
- Link each measure and proposal to the relevant department snapshot and responsible owner; distinguish a missed objective from unavailable evidence.
- Implement objective/owner/milestone updates and a versioned resource-allocation proposal with conflicting commitments visible before save.
- Create separate quarterly review and objective/resource detail references; reference-first workflow applies.
- Persistence: reuse operating plans/initiatives where semantics fit; add missing fiscal-quarter objective targets, measure links, review revisions, and resource proposal records with migrations. Audit changes and preserve reviewed versions.

**Constraints and preservation rules.** Apply the shared contract. Quarterly allocation is a planning decision, not an automatic authorization to spend, publish, pay, or grant data access.

**Acceptance criteria.** A leader inspects an objective's actual progress, reviews the supporting department evidence, changes its owner/milestone or resource proposal, and reopens the persisted revision. Conflicting resource assumptions are visible; unauthorized departmental detail remains protected.

**Verification.** Test fiscal-quarter boundaries, objective aggregation, cross-plan dependencies, ownership validation, concurrency, and planning authorization. Verify snapshot reconciliation and a quarterly review browser journey.

**Definition of done.** Shared definition of done is met; Quarter supports a real coordinated review with persisted decisions and visible dependencies.

### P26 — Versioned annual operating plan

**Title and outcome.** Let leadership draft, review, approve, and revise an annual plan with clear targets, budgets, owners, and milestones.

**Current context.** P25 establishes objective/resource review over C3 operating plans. C9 already holds financial budgets/forecasts. These concepts must be connected without assuming a short operating cycle is already a complete annual-plan model.

**Dependencies.** P25; fiscal-year configuration, authorized planners/reviewers, and department/Finance planning versions. No new provider credentials.

**Implementation requirements.**
- Implement the annual workspace using reference 06, with objective detail, quarterly milestones, owners, budget references, proposed investments, dependencies, and progress.
- Deliver explicit draft → reviewed → approved lifecycle and revision behavior. Preserve the approved version when a new revision is proposed; show its diff and approval state.
- Bind targets, units, baseline measures, periods, budget versions, and owners consistently. Provide validation for missing accountability, incompatible periods/currencies, and unbalanced allocations.
- Reference-first workflow applies to plan detail, review, and version history.
- Persistence: extend/reuse operating-plan and budget entities appropriately; migrate annual version, status, approval binding, target, and milestone/provenance fields where missing. Audit every lifecycle transition and reject stale writes.

**Constraints and preservation rules.** Apply the shared contract. Annual-plan approval records intent and budget governance; it does not authorize a payment, accounting posting, publication, or customer commitment.

**Acceptance criteria.** Leadership reviews and approves a complete annual plan, then revises an objective or investment while the prior approved plan remains readable. Current versus proposed values, owners, and milestones are unambiguous and survive reload.

**Verification.** Test lifecycle permissions, required fields, version binding, concurrent revisions, budget reconciliation, and fiscal-year boundaries. Validate migrations and browser-test draft → review → approve → revise → compare.

**Definition of done.** Shared definition of done is met; annual planning has working calculations, persisted versions, review controls, and traceable targets.

### P27 — Deterministic multi-year scenarios

**Title and outcome.** Let leadership compare strategic options by changing explicit assumptions and inspecting reproducible financial and capacity consequences.

**Current context.** P23 provides versioned Finance forecasts; P26 provides an annual baseline with targets, budgets, and milestones. Existing planning data is reusable, but it does not by itself supply a reproducible multi-year scenario model.

**Dependencies.** P23 and P26; a selected baseline version and explicit planning assumptions. No new external provider or AI model is required for calculation.

**Implementation requirements.**
- Implement reference 07 with at least baseline and alternative scenario comparison over a configurable several-year horizon.
- Deliver editable assumptions for the supported model, including revenue drivers, cost/capacity changes, cash timing, investments, and funding inputs where used. State units, period, source, owner, and limitations.
- Calculate results deterministically in the owning backend: opening/closing cash, revenue/cost implications, capacity constraints, and funding gap under the defined model. Publish the formulas and calculation version in the detail view/documentation.
- Show dependencies and strategic checkpoints; let users save, duplicate, compare, and reopen scenarios without altering actuals or approved plans.
- Reference-first workflow applies to assumption editor and comparison detail. Persistence: add missing versioned scenario definitions, input snapshots, computed outputs, and provenance with migrations and audit. Reuse existing planning ownership rather than creating a second forecast engine.

**Constraints and preservation rules.** Apply the shared contract. AI may explain recorded results, but must not invent the numeric outcome. Label assumptions and sensitivity results as scenarios, not promises or approved funding.

**Acceptance criteria.** Changing one assumption changes the expected dependent results, and identical inputs/calculation version reproduce identical outputs. Missing inputs block or explicitly limit the calculation. Saved scenarios remain stable when live actuals later change.

**Verification.** Use hand-checkable numeric fixtures for cash roll-forward, growth/cost timing, investments, negative cash, currency boundaries, and scenario isolation. Test versioning, authorization, migrations, and browser compare → edit → save → reopen.

**Definition of done.** Shared definition of done is met; scenarios contain a working bounded model, explicit assumptions, and reproducible saved comparisons.

### P28 — Review decisions become owned work

**Title and outcome.** Let a user turn a review or planning decision into an accountable task or initiative and trace subsequent execution back to its origin.

**Current context.** P20/P25–P27 provide saved reviews and plans/scenarios. C2/C3 already provide task commands, initiatives, orchestration, and dependencies; P12–P16 enforce decisions and execution authority.

**Dependencies.** P20 and P25–P27; valid assignees and existing task/initiative permissions. Existing integrations are needed only for controlled downstream effect validation.

**Implementation requirements.**
- Add a “Create work” path from a review finding, objective, annual-plan decision, or scenario checkpoint.
- Preview the objective, owner, due date, evidence snapshot/version, acceptance outcome, proposed collaborators, and required review before confirming.
- Persist the task/initiative through the existing command path with an idempotent origin link. Show both “work created from this review” and “source decision” navigation.
- Propagate context and proposed constraints without copying or granting authority. Source revisions are visible and do not silently rewrite in-progress work.
- Reference-first workflow applies to the creation/review panel and traceability view.
- Persistence: add missing typed origin/version links, creation idempotency, and audit via migrations. Downstream external sends, publication, or financial actions use existing approval/outbox boundaries and current execution policy.

**Constraints and preservation rules.** Apply the shared contract. Approval of a plan or creation of a task cannot satisfy a separate execution approval. A scenario remains hypothetical until the user explicitly creates owned work.

**Acceptance criteria.** Confirming a review action creates exactly one owned task/initiative; retrying returns the same result. Its source snapshot remains inspectable. Work requiring approval waits correctly, and subsequent source revisions do not expand its authority.

**Verification.** Test idempotency, authorization, cross-company source rejection, stale source versions, ownership, audit, and execution-policy inheritance. Browser-test review → create → Work → source snapshot and a review-required dispatch.

**Definition of done.** Shared definition of done is met; management choices have a real, traceable path into execution under Release 2 controls.

### P29 — Briefings at the right time and to the right person

**Title and outcome.** Let each role receive useful briefings and escalations at an appropriate cadence, with controllable working hours and absence routing.

**Current context.** C9 already provides briefing preferences, aggregation, scheduling infrastructure, generation, and update jobs. P19–P20 supply period reviews, and P28 supplies accountable follow-up work.

**Dependencies.** P19–P20 and P28; company/user timezones, role responsibilities, and configured delivery channels. Live delivery needs controlled test recipients and channel credentials.

**Implementation requirements.**
- Extend briefing preferences and preview with morning, end-of-day, shift handover, weekly, and monthly schedules; support review reminders for configured quarterly/annual checkpoints.
- Make role-relevant defaults editable: CEO decisions/risks, Sales meetings and commitments, Marketing launches/reviews, Finance obligations/close dates, Support SLA and shift handover. Define intraday urgent escalation separately from routine digests.
- Implement working hours, timezone, quiet periods, notification grouping, content-change deduplication, absence intervals, delegation, and accountable fallback/escalation ownership.
- Preview sample content from authorized real test data and show next scheduled delivery in local time. Link delivered items to current work and period snapshots, with source freshness.
- Reference-first workflow applies to schedule, preview, and delegation screens.
- Persistence: extend existing briefing preferences/jobs, delivery deduplication, and routing records with migrations where needed. Use the existing scheduler and external delivery outbox; record sent/failed/uncertain outcomes, retries, and delivery audit.

**Constraints and preservation rules.** Apply the shared contract. Delegation routes work only to eligible recipients and never grants record access or approval authority. Recheck permissions at generation/delivery; avoid sending restricted snapshot content to a newly ineligible recipient.

**Acceptance criteria.** A briefing arrives once at the configured local time, including daylight-saving transitions and retry. Routine unchanged updates are grouped/suppressed as configured. During absence, eligible delegates receive the appropriate content; otherwise a clear permitted escalation occurs. Urgent and routine notification rules are distinguishable.

**Verification.** Extend BriefingSchedulerCoordinator, BriefingPreferenceIntegration, BriefingUpdateJobRunner, and aggregation tests for timezones/DST, working hours, job concurrency, deduplication, permission changes, absence, and delivery failure. Browser-test preferences/preview and validate controlled delivery separately.

**Definition of done.** Shared definition of done is met; preferences have demonstrated scheduling and delivery effects for role-relevant information.

### P30 — Complete and verify Release 3

**Title and outcome.** Deliver a coherent review-and-planning release with reproducible numbers, functioning planning decisions, and timely information.

**Current context.** P19–P29 connect daily work to weekly/monthly reviews, departmental analysis, quarterly objectives, annual plans, scenarios, owned work, and briefings.

**Dependencies.** P19–P29; representative history, role test accounts, company calendar, and controlled delivery integration for live briefing checks.

**Implementation requirements.**
- Execute the release-plan demonstration: weekly change → evidence; monthly variance → source; annual objective revision; two saved scenarios → assumptions; review decision → owned work; scheduled briefing → eligible recipient.
- Complete role-specific subscreen/report/export checks and reconcile shared measures across CEO and department views.
- Use the required polish/UAT workflow, fix evidenced gaps, and rerun affected journeys. Check Today and Release 1–2 control compatibility after the new period/planning paths.
- Finish the release-3 register and acceptance package with reference/implemented screenshots, reproducibility fixtures, formula/version documentation, migration results, schedule evidence, issue ledger, and approve/revise fields.
- Reference-first workflow applies to major corrective redesigns. Persistence and external effects remain within the ownership, migration, and workflow/outbox boundaries established by the implementing prompts.

**Constraints and preservation rules.** Apply the shared contract. Do not accept static scenario charts, rewritten historical snapshots, unverified provider delivery, or planning approvals that bypass execution controls.

**Acceptance criteria.** All horizons show their intended information; role reports reconcile with evidence and exports; saved reviews/scenarios reproduce; plans and owned work persist; schedules honor routing and timing. Missing history and external validation limitations are visible. Material calculation, authorization, or execution-control defects prevent readiness.

**Verification.** Run the integrated report/period/snapshot/planning/scheduler regression set and focused tests for fixes, required SQL Server migration checks, and appropriate builds. Attach browser journeys and controlled integration results separately.

**Definition of done.** Shared definition of done is met; the completed release is concrete and reviewable, with implementation readiness and human approval recorded separately.

## Release approval checklist

The executing agent prepares this checklist and its linked evidence at P09, P18, and P30. The reviewer makes the actual approve/revise decision.

| Check | Release 1 | Release 2 | Release 3 |
| --- | --- | --- | --- |
| All scoped prompts implemented | Pending | Pending | Pending |
| Essential subscreens, reports, actions, and retained paths complete | Pending | Pending | Pending |
| Reference and implemented-screen comparisons recorded | Pending | Pending | Pending |
| Role, company, record, report, and export authorization verified | Pending | Pending | Pending |
| Relevant calculations and report reconciliation verified | Pending | Pending | Pending |
| Persistence, migration, concurrency, and recovery verified where applicable | Pending | Pending | Pending |
| Required controlled integration checks verified or explicitly blocked | Pending | Pending | Pending |
| Complete browser journeys verified | Pending | Pending | Pending |
| Material defects resolved and remaining limitations documented | Pending | Pending | Pending |
| Human release decision, reviewer, date, and evidence | Pending | Pending | Pending |

The three releases remain independently reviewable. Operational reports ship with Release 1 journeys; supervision reports ship with Release 2 controls; deeper management reports and planning ship in Release 3. No release is reduced to its top-level dashboard.

