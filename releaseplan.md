# Virtual Company release plan

Date: 2026-10-01  
Status: Proposed delivery plan; implementation and release acceptance are pending.

## Purpose and delivery principle

Deliver the role-based experience in three releases, each with a visible user
outcome and complete journeys that can be tested and approved:

1. **My working day:** understand what matters and take the next action.
2. **My agent team:** understand, direct, and control agent work.
3. **My business over time:** connect daily work to results, commitments, and
   strategic choices.

Each release includes the subscreens, actions, reports, permissions, persistence,
and recovery behavior required by its journeys. A dashboard alone is not a
complete feature.

For example, the Sales journey is:

**Priority → opportunity → customer context → proposal review → action → updated
pipeline and report.**

The release rule is: **a required subscreen or report ships with the journey that
depends on it.** Optional analytical depth can follow later, with the boundary
recorded explicitly. Existing reports and workflows remain available even when
their redesign or extension is scheduled for a later release.

## Authoritative instructions and design baseline

Implementation of every release must follow:

- [Production implementation instructions](production-implementation.md).
- [Architecture rules](docs/architecture-rules.MD).
- [Product design rules](docs/design.md), including the mandatory reference-first
  workflow for new or substantially redesigned UI.
- The applicable scoped `AGENTS.md` files, including
  [implementation prompt guidance](docs/AGENTS.md) when deriving implementation
  prompts from this plan.
- [UI implementation companion](ui-instructions.md), subordinate to the product
  design rules.

Use the [UI route inventory](docs/ui-route-inventory.md) and current production
code to resolve actual destinations and compatibility behavior before changing
screens. Current implementation takes precedence over older planning material.

The [design guide and nine screen references](docs/design/references/role-time-agent-2026-10-01/index.html)
provide the visual baseline. They are concepts with illustrative data, not proof
that the proposed functionality is implemented. This three-release plan refines
the broader delivery sequence in that guide.

## Existing foundation and implementation boundaries

The repository already provides foundations to extend:

- `/dashboard` with Today and Monthly views and authorized responsibility lenses
  for Company, Finance, Sales, Marketing, and Customers.
- Company responsibility assignments and role-appropriate workspaces.
- Agent team, Work, approval, activity, and audit destinations.
- Shared orchestration, collaboration, operating-plan, and dispatch contracts.
- Company autonomy, effective agent authority, and Finance capability grants.

These are reuse points, not a claim that every proposed journey is ready or
validated. Weekly, quarterly, annual, and multi-year experiences require their
own content and behavior rather than additional tabs over Today data.

Company autonomy, agent authority, and Finance grants have distinct semantics.
The new GUI must explain them consistently while preserving their owning policy
checks, persisted values, and approval requirements.

## Release overview

| Release | User target | Visible approval scope | Dependencies |
| --- | --- | --- | --- |
| 1 — My working day | Know what matters and what to do next | Five role perspectives, priorities, evidence, essential subscreens, operational reports, and complete daily workflows | Existing company access, responsibilities, module workflows, and usable data sources |
| 2 — My agent team | Know what agents are doing and what they may do | Agent board, work details, collaboration, decisions, autonomy controls, pause behavior, and supervision reports | Release 1 navigation and journeys; durable work identity, authority, and approval behavior |
| 3 — My business over time | Understand performance and make future commitments | Period reviews, scheduled briefings, management reports, annual plans, and multi-year scenarios | Releases 1–2; defined measures, sufficient source coverage/history, and reproducible planning inputs |

Dates and effort estimates are not committed by this document. Confirm them
after mapping the journeys to the screen/report register and assessing the
implementation gaps.

## Release 1 — My working day

### Target and independently usable outcome

A user opens the application, sees the most important work for their
responsibilities, understands why it matters, and completes the next action in a
working business workflow.

Make Today the primary focus. Preserve existing Monthly, reporting, and module
destinations. Do not defer operational reports needed for daily work to Release 3.

### Visible functionality and screens

Deliver one consistent shell with five responsibility-based perspectives:

| Role | Home-screen priorities | Essential subscreens | Operational reports available with the journey |
| --- | --- | --- | --- |
| CEO | Company situation, consequential decisions, cash exposure, major risks, accountable owners | Priority details, risk evidence, decision review, departmental drill-down | Company health, cash outlook, performance against plan |
| Sales | Opportunities, meetings, follow-ups, commitments, pricing exceptions | Opportunity and customer/contact details, meeting preparation, proposals, follow-up actions | Pipeline, overdue activities, near-term sales forecast |
| Marketing | Campaign tasks, content approvals, upcoming launches, spend exceptions, missing attribution | Campaign details, content items, audience context, asset review, launch approvals | Campaign delivery, spend versus budget, available lead results |
| Finance/accounting | Due obligations, receivables, reconciliation exceptions, cash and close work | Invoice/bill details, reconciliation, payment review, cash and close workflows | Receivables/payables aging, cash forecast, existing financial statements |
| Customer support | Cases requiring attention, SLA deadlines, customer context, escalations | Case details, customer history, reply review, knowledge sources, specialist handoffs | SLA risk, backlog, unresolved and aging cases |

These are capability boundaries. Map each destination to its current route and
record whether it needs redesign, adaptation, or no visual change before
implementation. Do not duplicate an existing report under a new role-specific
name merely to populate this table.

Shared functionality:

- A responsibility selector for people with several roles, preserving access
  permissions and company context.
- Three to five ranked priorities with what happened, why it matters now,
  accountable human, working agent, deadline, and next action.
- Evidence/details with source timestamps and links to the underlying records.
- Consistent access to existing tasks and approvals through Work.
- Compact agent updates showing completed work and outstanding decisions.
- Useful loading, empty, partial-data, restricted-access, stale-data, and error
  states.
- Context-preserving navigation into and back from module subscreens.

### Dependencies and implementation content

Use existing responsibility resolution, Today/Monthly projections, typed API
clients, module workflows, and report definitions. Fill the specific backend,
query, navigation, and persistence gaps required by the selected journeys.

Do not introduce a second dashboard or orchestration stack. Missing sources
must produce an explicit unavailable/partial state. Connected-data acceptance
requires a usable test integration where the journey depends on one.

Complete a route/report treatment inventory before broad redesign. This release
does not require every existing screen to be redesigned, but retained screens
must remain reachable, authorized, and compatible with the new context.

### Acceptance demonstration

Use one realistic test company and test users covering all five roles, including
a person with two responsibilities:

1. Open the role home and identify the important priorities and their reasons.
2. Open a priority, inspect its evidence, and enter the relevant detail screen.
3. Complete or review the next action using the underlying business workflow.
4. Confirm the persisted result appears on the home and relevant report.
5. Switch responsibility and verify that access does not expand.
6. Repeat with an unavailable source and with an unauthorized record.

### Acceptance criteria and verification

- Given a ranked priority, opening its action reaches the correct authorized
  company record; returning preserves the relevant context.
- Given a completed action, reloading shows its persisted state consistently on
  the detail page, home, and affected report.
- Given a stale or missing source, the UI identifies the gap and does not show
  fabricated healthy status or substitute zero for unavailable data.
- Given a restricted user, direct links, summaries, counts, drill-downs, and
  exports do not disclose protected records.
- Existing Monthly and retained module workflows remain usable.

Run focused projection, authorization, tenant-isolation, navigation, and affected
module tests. Verify report-to-record reconciliation and browser journeys for
all five roles. Run the appropriate API/Web builds after cross-layer changes.

### Release gate

Approve Release 1 when all five perspectives provide useful actions and their
required subscreens and reports work end to end. Appearance alone is insufficient.
New production paths must use real APIs, authentication, and persistence, with
no mock production data or deferred in-scope workflow steps.

## Release 2 — My agent team

### Target and independently usable outcome

A user can explain what an agent is doing, what it is waiting for, who is
accountable, and what the agent is allowed to do. The user can review decisions
and change authorized controls with a visible effect on execution.

### Visible functionality and screens

| Screen | Functionality to test and approve |
| --- | --- |
| Agent team board | Planned, active, waiting for approval, and completed work; clear blocked, failed, and paused states |
| Work detail | Business objective, accountable human, working agents, outputs, evidence, current step, and next dependency |
| Collaboration flow | Contributions and handoffs, artifacts passed between agents, disagreements, and human decision points |
| Decision review | Proposed action, affected records, before/after changes, evidence, approve/reject/request-changes actions |
| Autonomy settings | Company limits, task-type policies, named levels, constraints, expiry, and effective permission |
| Policy change preview/history | Before/after policy, affected actions, simulated eligibility, application status, and audit history |
| Execution control | Authorized global/scoped pause and clear queued/in-flight/reconciliation states |

Extend the business subscreens from Release 1:

- Proposals show agent contributions, supporting evidence, and approval reasons.
- Support cases show grounded drafts, specialist handoffs, and delivery status.
- Invoices and bills distinguish proposed and completed actions, applicable
  authority, and outstanding approvals.
- Campaigns show prepared content, publication eligibility, and blockers.

Supervision reports cover verified outcomes, blocked work, approval turnaround,
corrections, execution failures, and policy exceptions. Each aggregate links to
the exact work item and history. Do not count one shared initiative as several
company outcomes because several agents contributed.

### Autonomy behavior

Use four explicit user-facing positions:

| Level | Meaning |
| --- | --- |
| 1 — Advise | Review allowed information and recommend proposed actions |
| 2 — Organize | Arrange permitted internal tasks and plans |
| 3 — Do internal work | Execute permitted internal work |
| 4 — Act within limits | Execute explicitly granted actions within defined boundaries |

Configure everyday authority primarily by task type. Researching an account,
drafting a proposal, sending it, approving a discount, posting a transaction, and
releasing payment are separate capabilities.

Apply company-wide limits, agent capabilities, and task-specific restrictions.
Show configured versus effective permission with a reason when execution is
restricted. Mandatory approval remains mandatory at every level. These four
labels must not be implemented as numeric equivalence between the different
company, agent, and Finance autonomy models.

Before increasing authority, show a precise change preview using historical
cases or a dry run with no external effects. Record who applied the policy, its
scope, version, limits, and expiry. Keep pause separate from the level selector.
Explain what has already happened and what can still be stopped.

### Dependencies and implementation content

Reuse Release 1 navigation and records, durable tasks/workflows, shared
orchestration, existing approval behavior, effective authority, and Finance
grants. Join views through durable identifiers rather than matching summary text.

Implement any missing projections, output/dependency links, policy previews, and
authorized control commands in their owning modules. Preserve backend approval
and side-effect boundaries from the architecture rules, including execution-time
checks, reliable dispatch, idempotency, and ambiguous-outcome reconciliation.

Declare the initial set of supported task types explicitly in the register.
Start with a small, complete set; do not suggest that a generic selector enables
unimplemented capabilities. Each supported type needs both UI and execution
evidence.

### Acceptance demonstration

1. Follow a renewal through Sales research, Finance and Support contributions,
   proposal revision, human approval, and confirmed delivery.
2. Open the same approval from the role home, Work, and collaboration flow and
   verify one current state and history.
3. Preview and apply a task policy, then demonstrate an eligible action, an action
   requiring review, and a blocked action.
4. Revoke or expire authority after queuing work and verify it cannot execute
   under the old grant.
5. Pause work and inspect queued, running, already-completed, and uncertain
   external outcomes.

### Acceptance criteria and verification

- Required approval is enforced before the external action, including retries
  and delayed execution.
- Changes to the approved action's material details trigger the required new
  review rather than reuse an unrelated approval.
- Delegation cannot expand authority or bypass pause, restrictions, or limits.
- A policy preview performs no live external action and is labeled as simulation.
- Pausing blocks new execution and accurately represents in-flight work; it does
  not imply that completed deliveries or payments have been reversed.
- All surfaces distinguish prepared output, approved action, confirmed delivery,
  and achieved business outcome.

Verify focused authority, approval, tenancy, stale-version, expiry, budget,
concurrency, duplicate-delivery, failure/recovery, and pause scenarios. Exercise
the real dispatch path in the appropriate test environment. Verify accessible
workflow lists as alternatives to the graphical flow and keyboard operation of
autonomy controls. Run appropriate API/Web builds and migration checks when the
implementation changes those layers.

### Release gate

Approve Release 2 when supported task types are understandable and their controls
have verified execution effects. A displayed autonomy level, approval button, or
pause indicator without the corresponding backend behavior is not complete.

## Release 3 — My business over time

### Target and independently usable outcome

Users move from operational activity to performance reviews, future commitments,
and strategic choices, with clear evidence and a traceable path back to owned work.

### Visible functionality and screens

Extend the common workspace with meaningful horizon-specific content:

| Horizon | Functionality |
| --- | --- |
| Today | Priorities, deadlines, changes since the last visit, and handover |
| Week | Commitments, pipeline movement, campaign delivery, backlog, short-term liquidity |
| Month | Extended role-specific results, comparisons, explanations, close status, management decisions |
| Quarter | Objective progress, resource allocation, capacity, forecast revisions |
| Year | Targets, budgets, accountable owners, milestones, investment decisions |
| Several years | Alternative scenarios, assumptions, dependencies, funding needs, strategic checkpoints |

Management reports and supporting detail views:

| Role | Reports and subscreens |
| --- | --- |
| CEO | Monthly/quarterly business review, objectives, annual operating plan, investment scenarios |
| Sales | Conversion trends, win/loss analysis, sales-cycle development, territory/capacity planning |
| Marketing | Channel comparisons, acquisition economics, experiment history, attribution coverage, budget planning |
| Finance/accounting | Variance analysis, rolling forecasts, historical comparisons, planning scenarios |
| Customer support | Service-quality trends, recurring issues, reopen rates, staffing/capacity planning |

Existing versions of these reports remain accessible in earlier releases.
Release 3 introduces or improves their management experience; it does not defer
reports required by Release 1 operational journeys.

Also deliver:

- Configurable morning, end-of-day, shift, weekly, and monthly briefings/reviews.
- Notification preferences, working hours, delegation, and escalation ownership.
- Saved review snapshots with source coverage and reproducible comparisons.
- Annual operating plans with draft, reviewed, approved, and revised states.
- Scenario comparison with explicit assumptions and deterministic calculations.
- A path from review decisions to owned tasks/initiatives under Release 2 controls.

### Dependencies and implementation content

Build on Releases 1 and 2, existing Monthly projections, briefing capabilities,
module-owned metrics, and operating-plan/workflow contracts.

Define each measure, source, period, comparison, and calculation before adding it
to a report. Require sufficient history for claimed trends; show limitations
when sources or history are incomplete. Preserve snapshot inputs and versions
needed to reproduce reviews and scenario calculations.

Use company timezone, fiscal calendar, working hours, and configured obligations.
Differentiate actuals, targets, forecasts, and scenarios. Period performance and
point-in-time balances require different comparison semantics. Planning approval
must not itself release a payment, publish content, or send customer commitments.

### Acceptance demonstration

1. Conduct a weekly operational review and drill into a reported change.
2. Explain a monthly variance from its underlying records and source coverage.
3. Revise an annual objective, its owner, milestones, and proposed funding.
4. Compare two multi-year scenarios and inspect their assumptions.
5. Turn one review decision into assigned work and trace it back to its snapshot.
6. Configure a briefing schedule and verify delivery to the correct person at the
   configured time, including absence/delegation behavior.

### Acceptance criteria and verification

- Period boundaries, timezones, fiscal periods, and comparisons are correct.
- Report totals reconcile with their drill-downs and exports under the same scope.
- Saved review results can be reproduced from their recorded inputs and versions.
- Users can distinguish recorded results from targets, forecasts, and scenarios.
- Scheduling respects working hours and routing, groups routine updates, and
  avoids repeated unchanged notifications.
- A review creates owned work without bypassing permissions, grants, or approvals.

Verify calculations and source lineage, period/timezone/daylight-saving
boundaries, snapshot persistence, report authorization/export behavior,
scheduler deduplication, delegation, and review-to-work transitions. Exercise
role-specific browser reviews and scenario changes. Run proportionate module,
integration, migration, API, and Web validation for the actual changes.

### Release gate

Approve Release 3 when management reviews, planning, and scheduled information
work with reproducible data and clear distinctions between results and proposals.
Planning and scenario screens must contain working calculations and persisted
decisions rather than static charts or sample production values.

## Screen and report scope management

Maintain one register entry per actual screen or report, including relevant
detail views, tabs, exports, and restricted states. Expand the capability tables
above into this register before committing implementation scope for each release.

| Register field | Required content |
| --- | --- |
| ID, screen/report, and route | Stable item ID and exact existing or proposed destination |
| Users and responsibility | Who needs the view and who may access its data/actions |
| Parent journey | The business process that depends on it |
| Release | When the specified change becomes available |
| Treatment | New, redesigned, adapted, or retained |
| Data and actions | Sources, definitions, calculations, filters, approvals, persistence, exports |
| Dependencies | Required capabilities, integrations, data/history, configuration, or migrations |
| Reference and evidence | Separate visual reference and implemented screenshots where applicable |
| Acceptance scenario | Repeatable steps and observable expected results |
| Approval state | Pending, approved, or changes requested, with reviewer and evidence/date |

Treatment rules:

- **New:** a missing destination/capability must be implemented end to end.
- **Redesigned:** substantial presentation change requires the reference-first
  workflow and verification of retained business behavior.
- **Adapted:** update navigation, context, evidence, or controls while reusing the
  underlying screen and workflow.
- **Retained:** keep the existing destination available and verify compatibility
  with the new journey; a later redesign is not an excuse for a broken handoff.

A shared report has one authoritative business definition with appropriately
authorized role-specific presentations. The CEO's summary and accountant's
detailed statement must reconcile rather than become competing calculations.

Navigation must preserve company, record, responsibility perspective, relevant
period/filters, and return location. Authorization must also protect report
drill-downs, previews, cached summaries, and exports.

## Visual reference allocation

References live in
`docs/design/references/role-time-agent-2026-10-01/`. They guide layout; current
business rules and the authoritative design specification govern implementation.

| Reference | Release use |
| --- | --- |
| [CEO Today](docs/design/references/role-time-agent-2026-10-01/01-ceo-today.png) | Release 1 company perspective |
| [Sales Today](docs/design/references/role-time-agent-2026-10-01/02-sales-today.png) | Release 1 sales perspective and daily journey |
| [Marketing Week](docs/design/references/role-time-agent-2026-10-01/03-marketing-week.png) | Release 3 weekly workspace; create a separate Today reference for Release 1 |
| [Finance Month](docs/design/references/role-time-agent-2026-10-01/04-finance-month.png) | Existing close/report workflows remain accessible in Release 1; extended period-review design belongs to Release 3 |
| [Support Now](docs/design/references/role-time-agent-2026-10-01/05-support-now.png) | Release 1 support perspective and case journey |
| [Annual plan](docs/design/references/role-time-agent-2026-10-01/06-ceo-year.png) | Release 3 annual operating plan |
| [Multi-year scenarios](docs/design/references/role-time-agent-2026-10-01/07-ceo-multiyear.png) | Release 3 strategic scenario workspace |
| [Agent collaboration](docs/design/references/role-time-agent-2026-10-01/08-agent-collaboration.png) | Release 2 collaboration detail |
| [Autonomy controls](docs/design/references/role-time-agent-2026-10-01/09-autonomy-controls.png) | Release 2 task-policy settings and preview |

Create separate references for new or substantially redesigned subscreens that
are not covered, including role-home variants, decision review, policy history,
quarterly review, and briefing preferences as applicable. Do not treat one
overview mockup as the specification for all of its subscreens.

## Common acceptance package and definition of done

For each release, provide:

1. A working test environment with realistic, repeatable scenarios and the
   required role-specific test accounts. Test fixtures must remain separate from
   production data paths.
2. The screen/report register identifying new, redesigned, adapted, and retained
   destinations.
3. Separate reference and implemented screenshots for each major changed screen,
   with responsive and accessibility evidence appropriate to the surface.
4. Guided acceptance scripts covering complete journeys and important failure
   states, including empty, stale, partial, unauthorized, failed, and retry states.
5. Automated verification results, integration prerequisites, and remaining
   limitations, with live-system evidence distinguished from isolated test results.
6. A recorded approve/revise decision for each journey and an overall release
   decision. A draft plan or reference image is not release approval.

Approve journey packages rather than isolated screenshots. A package includes
the overview, essential subscreens, actions, reports, permissions, and recovery
states. Verify that navigation retains context, data reconciles, actions persist,
and existing workflows continue to work.

All in-scope functionality must use the production architecture, real endpoints,
authentication, persistence, and applicable integrations. No scaffolding, mock
production data, silent failures, or deferred in-scope TODOs qualify as complete.
Apply the architecture rules' migration requirements to schema changes and its
workflow/outbox requirements to external effects. Do not reopen previously
verified areas without a change, failure, or identified regression concern.

Release approval is withheld for unresolved defects that prevent the promised
journeys, violate access or authority boundaries, misstate financial/report
results, or misrepresent execution outcomes. Optional future depth must be
explicitly outside the release scope rather than hidden behind an incomplete UI.
