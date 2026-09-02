# Responsibility-Driven Workspaces Implementation Prompts

Status: Ready for ordered implementation

These prompts implement the approach defined in
`/docs/responsibility-driven-workspaces.md`. Execute them in order. Each phase
must be completed and verified before starting the next phase. Do not stop at an
intermediate build or test report when later requested phases remain.

Every phase must follow `/AGENTS.md` and any scoped `AGENTS.md` files for the
production and test paths it changes. `/production-implementation.md`,
`/docs/architecture-rules.md`, and `/docs/design.md` are authoritative throughout
the sequence.

## Phase 1 Prompt: Responsibility Ownership Foundation

### 1. Title and outcome

**Implement company responsibility assignments and reusable company-size
presets.**

Deliver a production-ready, tenant-scoped responsibility model that records
which active company member owns each business responsibility, which company
agent works for that person, and where approval or escalation should go. A
micro-company can assign every responsibility to its owner, while the same model
supports distributing responsibilities to managers as the company grows.

This phase must deliver complete persisted behavior and authenticated APIs. It
must not add placeholder dashboard UI.

### 2. Current context

- `src/VirtualCompany.Domain/Entities/TenantEntities.cs` contains `Company`,
  `User`, and `CompanyMembership`. Membership roles are intentionally broad and
  include Owner, Admin, Manager, Employee, Finance Approver, Support Supervisor,
  Tester, and Accountant.
- `Company` stores onboarding profile fields but has no relational company-size
  band.
- `src/VirtualCompany.Domain/Entities/AgentResponsibilityPolicy.cs` governs
  **agent execution-domain authorization**. It is not human responsibility
  ownership and must not be repurposed.
- `DashboardDepartmentConfig` and `DashboardWidgetConfig` in
  `src/VirtualCompany.Domain/Entities/DashboardConfigurationEntities.cs` are
  presentation configuration. Their JSON visibility/navigation fields are not
  an authoritative responsibility model.
- Company membership administration already has owner/admin authorization,
  contracts, services, and routes in
  `CompanyMembershipAdministrationController`.
- Company onboarding contracts and persistence already support optional company
  profile and settings data. Preserve compatibility for existing onboarding
  requests.
- Company-owned persistence uses EF Core global query filters plus explicit
  company authorization. SQL Server migrations are owned by
  `VirtualCompany.Persistence.Migrations` with `VirtualCompany.Api` as the
  startup project.

### 3. Dependencies

None.

### 4. Implementation requirements

1. Add stable typed values for:
   - company size: `micro`, `small`, and `medium`, with a safe unspecified state
     for existing companies
   - responsibility areas: `company_performance`, `cash_and_accounting`,
     `sales`, `marketing`, `customer_support`, and `compliance`
   - assignment kind: primary responsibility and executive oversight
   - authority level using existing agent/autonomy terminology where it fits;
     do not introduce a conflicting authority vocabulary
2. Persist company size as relational company profile state. Extend onboarding
   and company profile contracts compatibly so older clients may omit it.
3. Add a company-owned responsibility assignment entity. Model assignment to an
   active `CompanyMembership`, not only a raw user ID, so tenant membership is
   explicit. Include:
   - company and responsibility area
   - assignment kind
   - assigned membership
   - optional primary company agent
   - authority level
   - optional approval policy reference using an established policy identifier
     if one exists
   - optional escalation membership
   - created/updated timestamps and concurrency protection consistent with
     nearby mutable settings entities
4. Enforce at most one primary assignment per company and responsibility at the
   database and application boundaries. Permit zero or more executive oversight
   assignments. A membership may own several responsibilities.
5. Validate that assigned and escalation memberships are active and belong to
   the same company. Validate that an assigned agent belongs to the same company
   and is eligible for the responsibility area. Do not let a responsibility
   assignment expand agent tool permissions or human record authorization.
6. Add an idempotent preset service for micro, small, and medium setups:
   - Micro assigns all primary responsibilities to the selected active owner and
     assigns compatible active functional agents when unambiguous.
   - Small keeps company performance and unassigned areas with the owner while
     allowing Finance to be assigned to a selected manager.
   - Medium accepts selected managers for functional responsibilities and adds
     the owner as executive oversight.
   - Presets fill missing assignments by default and never silently overwrite
     explicit assignments. Any replace operation must be an explicit command
     whose response previews or reports the changes.
7. Add authenticated company-scoped application contracts and API endpoints to:
   - read responsibility assignments and available preset metadata for any
     authorized active company member
   - preview a preset without mutation
   - apply a preset
   - create or update a responsibility assignment
   - remove an assignment while preserving the primary-assignment invariant or
     returning an actionable validation error
8. Restrict all mutation endpoints to company Owner/Admin policy. Recheck
   membership and company scope in application code; controller visibility is
   not the only boundary.
9. Emit business audit evidence for preset application, assignment changes, and
   removals, including actor, company, previous assignment, new assignment,
   responsibility area, reason, and correlation ID.
10. Add EF mappings, indexes, query filters, navigation properties where useful,
    a SQL Server migration, and an updated snapshot. Backfill existing companies
    safely:
    - apply micro defaults only when one deterministic active Owner membership is
      available and no responsibility assignments exist
    - leave companies without an unambiguous active owner unassigned for later
      setup
    - never overwrite existing responsibility data on repeat startup or upgrade
11. Register services through the owning feature registration methods. Keep
    controllers thin and use typed commands and queries.
12. Document the finalized storage values and API routes in
    `/docs/responsibility-driven-workspaces.md` if implementation details differ
    from the proposal.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md` and the Domain, Application, API,
  Multi-Tenancy and Authorization, Database and EF Core, Commands/Queries, Audit,
  and Test Architecture sections of `/docs/architecture-rules.md`.
- Read `/src/AGENTS.md`, `/tests/AGENTS.md`, and any nearer instructions before
  editing their paths.
- Do not add Finance Manager or Sales Manager as new global membership roles.
  Functional responsibility is modeled by assignments, not membership enum
  expansion.
- Do not change `AgentResponsibilityPolicy` semantics.
- Do not make dashboard JSON configuration authoritative business state.
- Do not use frontend visibility as authorization.
- Preserve existing onboarding and membership API compatibility.
- Do not add startup DDL or use `EnsureCreated`.

### 6. Acceptance criteria

- **Given** a micro company with one active Owner and active functional agents,
  **when** an Owner/Admin previews and applies the micro preset, **then** all six
  primary responsibilities are assigned to that owner, compatible agents are
  assigned where deterministically available, and a second identical apply is
  idempotent.
- **Given** an explicit existing Sales assignment, **when** a fill-missing micro
  preset is applied, **then** the Sales assignment is not overwritten.
- **Given** a medium company, selected active managers, and an active owner,
  **when** the medium preset is applied, **then** functional primary assignments
  use the selected managers and the owner receives executive oversight.
- **Given** a membership or agent from company B, **when** company A attempts to
  use it in an assignment, **then** the command is rejected without leaking
  company B data.
- **Given** a non-Owner/Admin active member, **when** they read assignments,
  **then** they receive only authorized company A assignment summaries; **when**
  they attempt mutation, **then** the request is forbidden.
- **Given** two concurrent attempts to create the primary assignment for the same
  company and area, **then** at most one succeeds and the other receives an
  actionable conflict response.
- **Given** a responsibility mutation, **then** an audit event records actor,
  scope, before/after evidence, outcome, and correlation ID.
- **Given** an existing company without an unambiguous active owner, **when** the
  migration and post-schema initialization complete, **then** no unsafe default
  assignment is invented and setup remains possible through the API.

### 7. Verification

- Add narrow domain tests for typed values, invariants, and normalization.
- Add application/service tests for preset preview, idempotency, fill-missing,
  replace behavior, validation, and audit creation.
- Add API integration tests for Owner/Admin mutation, member reads, forbidden
  writes, inactive memberships, and cross-company IDs.
- Add tenant-query-filter tests for the new company-owned entity.
- Add concurrency or unique-index coverage for primary assignments.
- Generate and inspect the EF migration and model snapshot.
- Run `dotnet ef migrations has-pending-model-changes` using the repository's
  documented migration and startup projects.
- Run the focused Domain/Application/API tests, then a full solution build.

### 8. Definition of done

The responsibility model, presets, authorization, audit trail, migration,
backfill, APIs, registrations, tests, and documentation are production-ready.
There is no mock production data, startup schema repair, cross-company leakage,
silent overwrite, placeholder service, or deferred in-scope TODO.

## Phase 2 Prompt: Responsibility-Aware Today Read Model

### 1. Title and outcome

**Implement the responsibility-aware Today workspace query, priority ranking,
and typed API.**

Deliver one company-scoped read model that adapts to the current user's primary
responsibilities or executive oversight. It must compose existing Finance,
Sales, Support, Marketing, task, approval, alert, briefing, and agent activity
data without creating a parallel dashboard data stack.

### 2. Current context

- Phase 1 provides company size and responsibility assignments.
- `VirtualCompany.Application.Cockpit` already defines a rich
  `ExecutiveCockpitDashboardDto`, Finance cockpit models, department sections,
  pending approvals, alerts, recent activity, cache contracts, and
  `ExecutiveCockpitCacheScope` with effective role, department filters, time
  range, and identity.
- `ExecutiveCockpitController`, its infrastructure service/adapters, and
  `ExecutiveCockpitApiClient` already expose and cache executive data.
- `CompanyActionInsightService`, `CompanyFocusEngine`,
  `DashboardFocusController`, `DashboardBriefingSummaryController`, and the
  dashboard summary service already aggregate tasks, approvals, workflows,
  alerts, and briefing material.
- `DashboardDepartmentConfig` and `DashboardWidgetConfig` can describe existing
  department presentation, but they do not determine responsibility ownership.
- Canonical drill-down routes are defined in `/docs/ui-route-inventory.md`.
- Existing Cockpit integration, cache, Finance, performance, focus, action, and
  dashboard tests provide reuse targets and regression coverage.

### 3. Dependencies

Phase 1 must be complete, including its migration and read APIs.

### 4. Implementation requirements

1. Add a typed Today workspace contract under the existing Cockpit/dashboard
   capability rather than creating a new catch-all module. Include:
   - header and active lens
   - available responsibility lenses
   - situation summary with freshness
   - ranked priorities
   - up to four relevant metrics
   - optional typed Finance, Sales, Support, and Marketing sections
   - decisions
   - agent updates
   - generated-at timestamp and safe partial-data diagnostics
2. Add a workspace lens resolver that uses current user identity, active company
   membership, primary assignments, executive oversight, permissions, and the
   requested lens. Job title and membership role may provide a safe fallback for
   companies not yet configured, but must not override explicit assignments.
3. Support stable lens values such as `company`, `finance`, `sales`,
   `marketing`, and `customers`. Return only lenses that are relevant and
   authorized. Company executive oversight may include material cross-company
   items without including every operational task.
4. Reuse or extract existing Executive Cockpit adapters and action/focus queries.
   Do not re-query the same records through a second implementation merely to
   populate a new DTO. Feature-owned contributors should retain their existing
   ownership and typed models where practical.
5. Implement feature-owned Today contributors for Finance, Sales, Support, and
   Marketing. A contributor returns summaries and candidates; the central
   composer owns cross-feature ranking and final limits, not domain-specific
   calculations.
6. Implement deterministic priority selection using, in order:
   - a decision required from the current user
   - deadline, cash, compliance, or SLA proximity
   - financial or customer impact
   - direct responsibility ownership
   - blocked agent or workflow
   - anomaly or opportunity severity
   - freshness and confidence
7. Deduplicate candidates referencing the same task, approval, workflow, alert,
   or underlying business record. Limit top priorities to three to five and
   metrics to four. Preserve stable ordering when evidence has not changed.
8. Include plain-English fields for what happened, why it matters, responsible
   person, working agent, required human action, freshness, evidence/source type,
   and canonical deep link. Do not expose raw storage tokens or policy internals.
9. Do not call an LLM or run all agents during the HTTP request. Reuse persisted
   briefings or deterministic summary composition. If an optional generated
   summary is stale or unavailable, return an honest deterministic fallback.
10. Expose a typed authenticated query endpoint, preferably:
    `GET /api/companies/{companyId}/workspace/today?lens={lens}`.
    Preserve the current Executive Cockpit endpoint for compatibility.
11. Use `ICurrentUserAccessor` and existing company authorization. Reading the
    Today endpoint requires active access; section inclusion must never leak data
    a user cannot access through the canonical feature endpoint.
12. Extend caching by current user identity, active membership/effective access,
    responsibility-assignment version or equivalent invalidation input, active
    lens, and existing time/filter scope. Responsibility changes must invalidate
    affected Today cache entries. Never share a personalized cache entry across
    users.
13. Add a typed Web client using `ICompanyApiTransport` and register it through
    `AddVirtualCompanyApiClients`. Do not use direct unscoped `HttpClient` route
    construction for the new client.
14. Return partial feature results safely when a non-critical contributor fails,
    with observable diagnostics and a truthful UI-facing state. Authorization
    failures and company-scope failures must fail the request rather than be
    hidden as partial data.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md` and the CQRS-lite, Multi-Tenancy,
  Authorization, Dashboard Read Models, Web API Clients, Agent Orchestration,
  Audit, and Observability rules in `/docs/architecture-rules.md`.
- Read all applicable `/src` and `/tests` scoped instructions before editing.
- Do not replace existing feature systems of record.
- Do not store arbitrary serialized UI cards in the database.
- Do not perform mutations, approvals, provider writes, or agent execution in
  the query handler.
- Preserve existing Executive Cockpit APIs and cache behavior unless a tested
  compatibility migration is included.
- Use canonical routes from `/docs/ui-route-inventory.md`.

### 6. Acceptance criteria

- **Given** a micro owner assigned all responsibilities, **when** they request
  the Company lens, **then** the response contains the highest-impact authorized
  Finance, Sales, Support, Marketing, decision, and agent information within the
  documented limits.
- **Given** a Sales Manager assigned only Sales, **when** they request Today,
  **then** Sales is the default lens, Sales metrics and priorities are returned,
  and unrelated Finance details are absent unless separately authorized and
  material executive oversight applies.
- **Given** an owner with executive oversight and a Finance Manager with primary
  Finance responsibility, **when** both request Today, **then** their responses
  differ by relevance while preserving their independent authorization.
- **Given** the same approval appears through both the action queue and cockpit,
  **when** priorities are composed, **then** it appears once with the canonical
  Work/Approval deep link.
- **Given** more than five eligible priorities, **then** the top list contains no
  more than five and ordering is deterministic for unchanged evidence.
- **Given** a responsibility assignment changes, **when** the affected user next
  requests Today, **then** stale personalized cache content is not returned.
- **Given** two users in different companies or with different responsibility
  assignments, **then** their cached responses cannot collide.
- **Given** a non-critical feature contributor fails, **then** the response marks
  that section unavailable with safe diagnostics while keeping valid sections;
  authentication and tenant failures are never downgraded to partial success.

### 7. Verification

- Add unit tests for lens eligibility, fallback behavior, priority ranking,
  stable ordering, limits, deduplication, visibility reasons, and route mapping.
- Add service tests for feature contribution and deterministic summary fallback.
- Add cache-key and invalidation tests covering company, user, responsibility
  revision, lens, and permission differences.
- Add API integration tests for owner, manager, ordinary member, inactive member,
  invalid lens, unauthorized section, and cross-company access.
- Add typed Web client transport/contract tests, including company and
  correlation headers and `Guid.Empty` rejection.
- Preserve and run existing Executive Cockpit, Dashboard Focus, Top Actions, and
  performance tests.
- Run the focused test projects and a full solution build.

### 8. Definition of done

The Today query, typed contracts, contributors, resolver, priority ranking,
cache isolation/invalidation, API endpoint, Web client, authorization,
observability, tests, and compatibility behavior are complete. The page-load
query performs no agent execution or sensitive side effects and returns no mock
production data.

## Phase 3 Prompt: Reusable Today Workspace UI

### 1. Title and outcome

**Replace the current dashboard content with one reusable, responsibility-aware
Today workspace.**

Deliver the canonical `/dashboard` experience with a stable layout whose content
adapts to the current user's available responsibility lenses. A micro owner sees
cross-company priorities; a Sales Manager sees Sales-focused content through the
same page and component structure.

### 2. Current context

- Phase 2 provides the typed Today API and Web client.
- `/dashboard` is the canonical Overview route in
  `/docs/ui-route-inventory.md`.
- `Dashboard.razor` currently preserves onboarding/company selection,
  interaction telemetry, a six-metric strip, a briefing summary, and
  `TopActionsList`. It loads Finance and Agent Staff data separately and
  hard-codes Laura as the briefing agent.
- `ExecutiveCockpitDashboard.razor` already contains richer Finance, business
  signal, activity, approval, and agent presentation that can be extracted or
  adapted.
- Existing reusable components include `TopActionsList`, `TodayFocusPanel`,
  `ActionCard`, `ContextualAgentSidebarCard`, `ApprovalInbox`, Finance insight
  panels, and feature-specific dashboard panels.
- `/work`, `/tasks`, `/approvals`, Finance, Sales, Marketing, and Support remain
  the canonical detailed destinations.
- The product design system requires a left application navigation, central work
  area, contextual right-side insight/action panel, actions over passive data,
  three to five priorities, and responsive behavior.

### 3. Dependencies

Phases 1 and 2 must be complete. The Today endpoint must be usable for at least a
micro owner and one manager-focused lens.

### 4. Implementation requirements

1. Before implementation, complete the mandatory screenshot-first workflow in
   `/docs/design.md`:
   - write the exact image-generation prompt based on the Today requirements and
     current SaaS design system
   - generate and store
     `/docs/design/references/responsibility-driven-today-workspace-reference.png`
   - store the source prompt beside it as
     `responsibility-driven-today-workspace-reference-prompt.md`
   - implement and visually compare responsive desktop and mobile states against
     the reference, with `/docs/design.md` remaining authoritative
2. Preserve `/dashboard`, company selection, onboarding redirect behavior,
   company query context, localization, and interaction telemetry.
3. Replace separate page-level Finance/Agent API calls with the Phase 2 Today
   Web client. The page should load one composed read model and must not perform
   independent business composition.
4. Implement a stable layout with:
   - workspace header, local date, active lens, and freshness
   - short situation summary
   - top three to five priorities
   - up to four relevant metrics
   - typed responsibility sections
   - decisions rail
   - agent briefing rail
5. Add a lens picker only when more than one lens is available. Persist the
   selected lens in the query string while preserving `companyId`. Invalid or no
   longer authorized lenses fall back safely to the server-selected default.
6. Render responsibility-specific data through typed components such as
   `FinanceTodaySection`, `SalesTodaySection`, `SupportTodaySection`, and
   `MarketingTodaySection`. Extract and adapt existing components instead of
   embedding complete pages or duplicating their visual/business logic.
7. Remove the fixed Laura presentation. Agent identity, avatar, role, state, and
   copy come from the read model. Support zero, one, or several assigned agents.
8. Every priority and decision must show the minimum useful combination of:
   what happened, why it matters, required action, owner/agent context,
   freshness, and a canonical action/deep link. Do not overload cards with all
   metadata when it is not needed to decide.
9. Implement honest loading, partial-data, stale-data, unauthorized, error, and
   empty states. A user with no configured responsibility must receive a useful
   setup explanation and authorized Settings link, not a blank page.
10. Use accessible semantic markup, keyboard-operable controls, visible focus,
    appropriate announcements for loaded/error states, and touch targets that
    meet the existing design system.
11. Keep the central workspace usable at narrow mobile widths by stacking the
    right rail below main content. Do not introduce an internal page scrollbar,
    clipped tables, or hover-only actions.
12. Keep Work, Tasks, Approvals, Finance, Sales, Marketing, and Support as
    canonical drill-down pages. The Today workspace must not create duplicate
    mutation state.
13. Update localization resources for every user-facing string and preserve the
    existing English/Swedish localization conventions.
14. Update `/docs/ui-route-inventory.md` only if query parameters or contextual
    behavior need documenting; do not introduce a competing primary route.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and all
  UI requirements in `/docs/design.md`.
- Read `/src/AGENTS.md`, `/tests/AGENTS.md`, and nearer instructions before
  editing.
- The mandatory reference screenshot workflow applies.
- Do not perform authorization or responsibility resolution in Razor.
- Do not call an LLM, run agents, or issue business commands during page load.
- Do not restore retired routes to primary navigation.
- Prefer adapting reusable components when that preserves or improves visual
  quality.
- Preserve canonical deep links and company context.

### 6. Acceptance criteria

- **Given** a micro owner with all responsibilities, **when** `/dashboard` loads,
  **then** the stable Today layout shows cross-company priorities, relevant
  metrics, functional summaries, decisions, and assigned agent updates without
  hard-coded agent identity.
- **Given** a Sales Manager with only a Sales lens, **when** the same route loads,
  **then** Sales content fills the same slots and no redundant lens picker is
  displayed.
- **Given** a user with multiple authorized lenses, **when** they select Finance,
  **then** the query string preserves `companyId`, the Finance read model is
  loaded, and browser navigation restores the selection.
- **Given** a deep link from a priority, **when** it is selected, **then** the user
  reaches the canonical authorized feature or Work route with company and record
  context preserved.
- **Given** one feature section is unavailable, **then** other valid sections
  remain usable and the unavailable state is honest and actionable.
- **Given** no responsibility assignments, **then** an Owner/Admin sees a setup
  action and an ordinary member sees a safe contact-your-administrator message.
- **Given** desktop and mobile viewport verification, **then** content has no
  overlap, clipping, inaccessible action, hover-only requirement, or duplicate
  information that violates `/docs/design.md`.

### 7. Verification

- Add bUnit tests in the narrowest Web test project for owner, Sales Manager,
  Finance Manager, single/multiple lenses, no assignments, partial data, loading,
  error, stale data, and unauthorized states.
- Add tests proving that `Dashboard.razor` uses the Today client and no longer
  hard-codes Laura or independently composes Finance/Agent Staff data.
- Add navigation tests for lens and company query preservation and canonical
  deep links.
- Run accessibility checks available in the repository and keyboard-test the
  lens selector and actions.
- Render and compare the implemented page with the approved reference at desktop
  and mobile widths. Store verification captures in the existing design
  reference/evidence convention when appropriate.
- Run focused Web and contract tests, then a full solution build.

### 8. Definition of done

The canonical dashboard is a production-ready reusable Today workspace for both
owner and manager responsibility profiles. Reference images, responsive UI,
typed client integration, localization, accessibility, tests, compatibility,
and drill-down behavior are complete, with no hard-coded persona, mock production
data, duplicated business logic, or deferred in-scope TODO.

## Phase 4 Prompt: Agent Activity, Decisions, and Manual Review

### 1. Title and outcome

**Make assigned agent work, human decisions, and on-demand company review fully
visible in Today.**

Deliver a consistent supervision loop: users can see what their agents are
monitoring, working on, recommending, waiting for, or have completed; understand
why an item appears; open the authoritative decision; and request a durable
company review without blocking page load.

### 2. Current context

- Phases 1-3 provide assignments, Today composition, and the reusable UI.
- Agent activity already exists through agent executions, scheduled cadences,
  work tasks, workflow state, activity feed/audit events, and feature-specific
  recommendations.
- `RoleAgentCadenceBackgroundService` runs scheduled functional reviews.
- `CompanyOperation.razor`, `ICompanyOperatingCycleService`,
  `ICompanyOperatingCycleAutomationService`, and
  `CompanyOrchestrationController` provide goals, daily cycles, planning,
  dispatch, and review operations.
- Work, Tasks, Inbox, and Approvals already own task and approval state and
  decision endpoints.
- Approval-backed sensitive actions and external side effects already require
  backend policy/workflow/outbox enforcement.

### 3. Dependencies

Phases 1-3 must be complete.

### 4. Implementation requirements

1. Define a normalized Today agent-update read contract with stable user-facing
   states:
   - monitoring
   - working
   - recommended
   - needs_user
   - blocked
   - completed
2. Map existing agent executions, work tasks, workflows, approvals, scheduled
   reviews, feature recommendations, and activity/audit records into those
   states in backend read-model code. Do not create a new system of record only
   to feed the dashboard.
3. Include agent identity, functional responsibility, concise activity summary,
   rationale/evidence summary, occurred/updated time, related task/workflow or
   approval, state, and canonical deep link. Exclude raw prompts, hidden
   instructions, secrets, and provider payloads.
4. Scope agent updates to agents assigned to the current user's responsibilities
   plus material executive-oversight exceptions. Deduplicate repeated evidence
   for one run or underlying work item.
5. Add visibility reasons to priorities, decisions, and agent updates. Derive
   them from responsibility, approval assignment, executive oversight, or direct
   task involvement. Use plain English rather than storage values.
6. Compose decisions from the authoritative approval/task/workflow subsystems.
   Reuse existing decision routes and APIs. If inline approval is implemented,
   it must call the existing approval command, display the backend policy result,
   prevent double submission, and refresh Today after success. Do not implement
   approval state locally.
7. Add a **Review now** action for authorized users. It must enqueue or request a
   Company Operating Cycle through the existing orchestration service, return a
   durable cycle/workflow identifier, and show queued/running/completed/blocked
   or failed state. It must not hold the HTTP request open while agents run.
8. Make Review now idempotent for an equivalent active request and prevent
   repeated clicks from creating duplicate cycles. Respect company operating
   mode, budgets, autonomy, policy, and emergency pause.
9. Add cache invalidation or targeted refresh for new tasks, decisions, agent
   activity, operating-cycle transitions, and responsibility changes.
10. Ensure background failure states are visible and actionable without exposing
    stack traces or secrets. Include retry or recovery links only where an
    existing authorized operation supports them.
11. Record audit evidence for manual review requests and any inline decision,
    including actor, scope, target, policy outcome, correlation ID, and resulting
    workflow/task IDs.
12. Update the Today UI to show the normalized states consistently, with compact
    agent identity and clear Needs you/Blocked emphasis. Avoid a decorative
    activity feed that displaces higher-value priorities.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md` and the Workflow and Approval, External
  Side Effects and Outbox, Agent Orchestration, Audit, Observability,
  Multi-Tenancy, and Authorization sections of
  `/docs/architecture-rules.md`.
- Follow `/docs/design.md`; use the Phase 3 reference unless the change becomes a
  significant redesign, in which case repeat the mandatory reference workflow.
- Do not create a second task, approval, activity, or operating-cycle system.
- Do not execute all agents in the page request.
- Do not bypass approval rechecks, outbox dispatch, idempotency, budgets, or
  emergency pause.
- Preserve canonical Work and Company Operation routes.

### 6. Acceptance criteria

- **Given** an assigned Finance agent that completed a scheduled review, **when**
  its owner opens Today, **then** a deduplicated Completed or Recommended update
  shows what was checked, the result, freshness, and a canonical evidence link.
- **Given** an agent blocked by a required approval, **when** Today loads, **then**
  the item appears as Needs you once and links to the authoritative approval.
- **Given** an owner with executive oversight but no primary Sales assignment,
  **when** a materially high-impact Sales risk occurs, **then** it may appear
  with an executive-oversight visibility reason while routine Sales tasks remain
  absent.
- **Given** an ordinary member without operating-cycle authority, **when** Today
  loads, **then** Review now is absent or disabled according to a backend policy
  decision and cannot be invoked through the API.
- **Given** an authorized owner presses Review now twice while an equivalent
  cycle is active, **then** one durable cycle exists and the UI shows its current
  state.
- **Given** operating mode is paused or budget policy rejects a review, **then**
  no agent execution begins and the user receives a stable, actionable reason.
- **Given** an inline approval submission is retried, **then** backend approval
  state remains authoritative and no sensitive side effect is duplicated.

### 7. Verification

- Add mapping tests for every normalized agent state and visibility-reason type.
- Add deduplication tests spanning tasks, workflows, approvals, and activity
  records for one underlying operation.
- Add operating-cycle request tests for authorization, idempotency, pause,
  budget/policy denial, durable state, audit, and cross-company isolation.
- If inline decisions are included, run existing approval integration and
  external-side-effect idempotency tests plus new UI double-submit tests.
- Add bUnit tests for Monitoring, Working, Recommended, Needs you, Blocked, and
  Completed presentation, as well as manual review progress/failure states.
- Run the focused orchestration, approval, activity, API, and Web tests, then a
  full solution build.

### 8. Definition of done

Today provides a trustworthy supervision loop using existing authoritative
agent, task, workflow, approval, activity, and operating-cycle systems. State,
visibility reasons, manual review, authorization, idempotency, audit, UI, and
tests are complete with no duplicate systems, hidden failures, direct side-effect
bypass, or deferred in-scope TODO.

## Phase 5 Prompt: Responsibility Settings and Onboarding

### 1. Title and outcome

**Add owner-facing responsibility assignment, agent delegation, and setup preset
UI.**

Deliver a clear Settings experience where an authorized owner can see who owns
each responsibility, which agent works for that person, what authority applies,
and where escalation goes. Integrate company size and recommended responsibility
presets into onboarding so a micro company receives a useful Today workspace
without API setup.

### 2. Current context

- Phase 1 provides company size, responsibility assignment, preset preview/apply,
  and mutation APIs.
- `SettingsHub.razor` is the canonical `/settings` entry point and already groups
  Company Setup, Agents, Connections, Automation, Briefings, Department Settings,
  Security/Audit, and User Preferences.
- Company onboarding already captures company profile fields and selected
  templates, supports save/resume/complete, and returns the canonical Dashboard
  path.
- Membership administration already provides the active company member directory
  and Owner/Admin mutation policy.
- Agent Settings and Agent Staff surfaces already provide agent roster, role,
  capability, access, and operating-profile context.
- Phase 3 displays a setup state when responsibility assignments are missing.

### 3. Dependencies

Phases 1-4 must be complete. The preset preview/apply and assignment APIs must be
stable.

### 4. Implementation requirements

1. Before UI implementation, complete the mandatory reference workflow in
   `/docs/design.md` for a new responsibility settings screen. Store:
   - `/docs/design/references/responsibility-assignments-settings-reference.png`
   - `/docs/design/references/responsibility-assignments-settings-reference-prompt.md`
2. Add a canonical contextual Settings route, preferably
   `/settings/responsibilities?companyId={companyId}`, and link it from the
   Company Setup or Agents group in `SettingsHub.razor`. Document it in
   `/docs/ui-route-inventory.md`.
3. Add a typed Web client using `ICompanyApiTransport` for assignment read,
   preset preview/apply, update, and removal. Register it through
   `AddVirtualCompanyApiClients`.
4. Present one readable responsibility matrix with:
   - responsibility area and plain-English purpose
   - responsible active company member
   - assigned compatible active agent
   - authority level
   - approval policy summary
   - escalation person
   - executive oversight
5. Separate responsibility relevance from authorization in the explanatory copy.
   Make clear that assigning a responsibility does not automatically grant access
   or expand an agent's tools.
6. Allow Owner/Admin users to edit assignments. Other active members may view
   assignments relevant to their authorized company context but cannot mutate
   them. The UI must reflect backend policy results rather than inferring access
   only from membership labels.
7. Filter member and agent pickers to compatible active same-company choices.
   Keep essential validation server-side and show returned field-specific errors.
8. Add a company-size/preset flow:
   - choose or confirm micro, small, or medium
   - show a preview of assignments that will be added, retained, or changed
   - default to fill-missing behavior
   - require explicit confirmation before replacing existing assignments
   - show the result and link to Today
9. Integrate company size into onboarding save/resume/complete contracts and UI.
   On completion, preview and apply the appropriate fill-missing preset using the
   selected owner and available agents. If an assignment is ambiguous, complete
   onboarding safely and direct the owner to finish responsibility setup rather
   than guessing.
10. Ensure onboarding replay and repeated completion are idempotent. Never
    overwrite explicit assignments on resume or repeat completion.
11. Provide useful empty states for no additional members, no compatible agent,
    unassigned responsibility, missing escalation target, and inactive previous
    assignee. Preserve the ability to resolve each state.
12. Use the existing design system, localization conventions, company context,
    and responsive Settings layout. Do not turn the matrix into a wide,
    horizontally unusable mobile table; use responsive rows or cards at narrow
    widths.
13. After a successful change, invalidate Today data and make the effect visible
    on the next Dashboard load without requiring application restart.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and
  `/docs/design.md`, including the mandatory reference screenshot workflow.
- Read applicable `/src` and `/tests` scoped instructions before editing.
- Preserve current onboarding resume, abandonment, completion, template, and
  company-selection behavior.
- Do not add role-specific dashboard settings or arbitrary layout builders.
- Do not let picker filtering replace backend company and authorization checks.
- Do not silently replace assignments when applying a preset.
- Do not change agent permissions merely because an agent is selected here.

### 6. Acceptance criteria

- **Given** a new micro company and active owner, **when** onboarding completes,
  **then** company size is persisted, fill-missing micro assignments are applied
  idempotently, and the owner can open a populated Today workspace.
- **Given** an onboarding setup with ambiguous managers or agents, **when** it
  completes, **then** no arbitrary assignment is invented and the owner receives
  a clear responsibility-setup action.
- **Given** an Owner/Admin on Responsibility Settings, **when** they change Sales
  from the owner to an active manager and confirm, **then** the matrix updates,
  audit evidence exists, and the affected users' Today composition changes.
- **Given** an existing explicit Finance assignment, **when** the user previews a
  fill-missing preset, **then** the preview states that Finance is retained and
  applying the preset does not overwrite it.
- **Given** a replace preset that would change assignments, **then** the UI shows
  the exact changes and requires explicit confirmation.
- **Given** a member or agent becomes inactive, **when** Settings loads, **then**
  the stale assignment is visibly actionable and cannot be re-saved as valid.
- **Given** a non-Owner/Admin, **when** they open Settings or call a mutation API,
  **then** no unauthorized control succeeds and the API remains authoritative.
- **Given** a narrow mobile viewport, **then** every responsibility and edit
  action remains readable and keyboard/touch accessible without horizontal page
  clipping.

### 7. Verification

- Add Web client contract/transport tests for every assignment and preset
  operation.
- Add bUnit tests for Owner/Admin edit, member read-only, preview, fill-missing,
  replace confirmation, validation, empty states, inactive choices, error, and
  success behavior.
- Add onboarding service/API/UI tests for company size, save/resume, completion,
  idempotency, ambiguity, and existing assignment preservation.
- Add navigation tests for Settings and Dashboard company-context preservation.
- Verify desktop and mobile implementation against the generated reference and
  run applicable accessibility checks.
- Run focused onboarding, membership, responsibility, Web, and contract tests,
  then a full solution build.

### 8. Definition of done

Owners can configure responsibility ownership and agent assignment without API
tools, and new micro companies receive safe default assignments through
onboarding. Screenshot references, Settings integration, onboarding compatibility,
authorization, validation, audit, cache invalidation, localization,
responsiveness, and tests are complete with no silent overwrite or mock data.

## Phase 6 Prompt: Responsibility-Aware Monthly Workspace

### 1. Title and outcome

**Extend the reusable workspace into a responsibility-aware monthly management
review.**

Deliver a Monthly period on the canonical Overview experience using the same
responsibility assignments, lenses, visual language, and feature-owned query
model as Today. A micro owner receives a cross-company monthly review, while a
manager receives a review focused on their assigned responsibility.

### 2. Current context

- Phases 1-5 provide responsibility ownership, Today composition, reusable UI,
  agent/decision visibility, Settings, and onboarding.
- Finance already has monthly summary, cash position/runway, receivables,
  payables, compliance calendar, VAT, accounting reports, close, and year-end
  capabilities.
- Sales, Marketing, and Support already expose operational metrics and scheduled
  review outputs, though coverage and period semantics differ by feature.
- Company briefings currently emphasize daily and weekly delivery. A monthly
  management workspace must not relabel a weekly briefing or daily metric as a
  monthly result.
- `/dashboard` remains the canonical Overview route and detailed feature routes
  remain canonical drill-down destinations.

### 3. Dependencies

Phases 1-5 must be complete and stable.

### 4. Implementation requirements

1. Add an explicit Monthly workspace period to the existing Overview contract
   and UI, using a query such as
   `/dashboard?companyId={companyId}&period=month&lens={lens}`. Today remains the
   default. Do not introduce a competing primary navigation destination.
2. Define a typed `MonthlyWorkspaceReadModel` or a strongly typed period variant
   that shares header, lens, priority, decision, agent, freshness, and section
   concepts without forcing daily and monthly facts into one ambiguous DTO.
3. Add feature-owned monthly contributors:
   - Finance: revenue, expenses, net result, cash and runway, receivables,
     payables, compliance/close readiness, and important exceptions
   - Sales: pipeline movement, forecast, conversion or stage movement, deals at
     risk, and follow-up priorities using true period-aware data
   - Marketing: campaign activity/outcomes and recommendations when authoritative
     data exists
   - Support: volume, SLA performance, unresolved customer risks, recurring issue
     signals, and knowledge gaps using true period-aware data
   - Company operation: completed initiatives, unresolved blockers, budget or
     autonomy constraints, and next-period priorities
4. Do not invent unavailable profitability, payroll, pricing, or forecasting
   metrics. Omit unsupported sections or label them as unavailable with an
   appropriate setup/action path. Do not manufacture values from unrelated
   records.
5. Reuse responsibility lens resolution and executive oversight. Monthly
   priority ranking should emphasize material change, unresolved risk,
   compliance/close deadlines, next-period decisions, and sustained trends rather
   than daily urgency alone.
6. Include period boundaries, comparison period, generated time, data freshness,
   and source coverage in the response. Respect company timezone and reporting
   period semantics.
7. Compose a deterministic management summary from authoritative structured
   results. If an optional AI narrative is used, it must use the shared AI
   orchestration subsystem, be clearly non-authoritative, preserve its data
   sources, and have a deterministic fallback.
8. Surface next-month priorities and human decisions through existing Company
   Operation, Work, Task, and Approval systems. Do not create chat-only follow-up
   state.
9. Extend caching and invalidation for company, user, responsibility revision,
   lens, period boundaries, and source updates. Daily and monthly cache entries
   must not collide.
10. Before the monthly UI is implemented, complete the mandatory screenshot-first
    workflow in `/docs/design.md` and store:
    - `/docs/design/references/responsibility-driven-monthly-workspace-reference.png`
    - `/docs/design/references/responsibility-driven-monthly-workspace-reference-prompt.md`
11. Reuse the Today workspace shell and interaction patterns while giving Monthly
    a distinct period summary. Do not copy every daily card or add overloaded
    passive chart grids.
12. Preserve company/lens/period query context, canonical drill-down routes,
    localization, accessibility, responsive behavior, and honest empty/partial
    states.
13. Update `/docs/ui-route-inventory.md` and
    `/docs/responsibility-driven-workspaces.md` with the finalized period behavior
    and any intentionally unavailable measures.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and
  `/docs/design.md`, including the mandatory reference workflow.
- Read applicable `/src` and `/tests` instructions before editing.
- Do not infer monthly figures by summing incompatible snapshots.
- Do not label net profit as cash flow or otherwise blur accounting semantics.
- Do not introduce new systems of record for tasks, decisions, goals, or reports.
- Do not call feature providers or an LLM directly from Razor.
- Do not show unauthorized cross-responsibility detail merely because an item is
  relevant to an executive summary.

### 6. Acceptance criteria

- **Given** a micro owner with all responsibilities, **when** they select Monthly,
  **then** the workspace shows an authorized cross-company review of Finance,
  Sales, customer/support, agent outcomes, decisions, and next-month priorities
  using true monthly or period-aware data.
- **Given** a Sales Manager, **when** they select Monthly on the same canonical
  route, **then** Sales period performance, risks, agent outcomes, and next steps
  fill the shared structure while unauthorized Finance details remain absent.
- **Given** an unsupported profitability-by-customer metric, **then** the workspace
  does not invent a value and either omits it or provides a truthful setup/not-
  available state.
- **Given** a reporting period in the company's timezone, **then** period
  boundaries and comparisons are correct and displayed clearly.
- **Given** Today and Monthly are opened for the same user and lens, **then** their
  cache entries and priority semantics remain distinct.
- **Given** an optional narrative provider is unavailable, **then** a useful
  deterministic monthly summary is returned with no false failure of the
  underlying workspace.
- **Given** a monthly priority or decision is selected, **then** the user reaches
  the existing canonical workflow or feature page with company and record context
  intact.
- **Given** desktop and mobile verification, **then** Today and Monthly share a
  recognizable interaction model without duplicating or overloading information.

### 7. Verification

- Add period-boundary and timezone unit tests, including month transitions and
  comparison periods.
- Add contributor tests proving accounting semantics, true period filtering,
  missing-data behavior, and responsibility filtering.
- Add priority-ranking and cache-key tests distinguishing Today from Monthly.
- Add API integration tests for owner, manager, unauthorized/cross-company access,
  partial contributor failure, and deterministic narrative fallback.
- Add bUnit tests for period switching, URL preservation, owner/manager variants,
  unsupported metrics, loading, partial, empty, and error states.
- Visually verify desktop and mobile Monthly UI against the generated reference
  and confirm Today remains visually and functionally intact.
- Run focused Finance/Sales/Support/Marketing/Application/API/Web tests as
  affected, then a full solution build.

### 8. Definition of done

The canonical Overview supports production-ready Today and Monthly periods using
the same responsibility model and feature-owned architecture. Period semantics,
authorization, source coverage, caching, summaries, UI references,
responsiveness, accessibility, tests, routes, and documentation are complete,
with no invented metrics, mock data, duplicated systems, or deferred in-scope
TODO.
