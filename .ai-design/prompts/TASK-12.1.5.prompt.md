# Goal
Implement **TASK-12.1.5 / ST-601 Executive cockpit dashboard** so the **web app becomes the primary command center** for a tenant’s workspace. Deliver an initial executive cockpit in the **Blazor Web App** that surfaces the most important company-wide operational information in one place: daily briefing, pending approvals, alerts, KPI summaries, and recent activity, with drill-in navigation and setup-oriented empty states.

Because no explicit acceptance criteria were provided for the task itself, align implementation to **ST-601** and the architecture/backlog guidance:
- Web-first experience
- Tenant-scoped dashboard queries
- Interactive-performance-friendly reads
- Empty states for unconfigured workspaces
- Foundation for later auditability, alerts, and mobile companion work

# Scope
In scope for this task:
- Add a **dashboard landing page** in `VirtualCompany.Web` as the primary post-login/workspace command center
- Add or extend **application-layer query models/services** to fetch executive cockpit data
- Add **tenant-scoped read APIs** in `VirtualCompany.Api` for dashboard data
- Add **query-side infrastructure/repositories** in `VirtualCompany.Infrastructure`
- Compose dashboard sections for:
  - daily briefing summary
  - pending approvals summary/list
  - alerts/exceptions summary/list
  - department KPI cards
  - recent activity feed
- Add **empty states** when the tenant has no agents, workflows, tasks, approvals, or knowledge
- Add **drill-in links/navigation** from widgets to existing or placeholder routes for agents, tasks, workflows, approvals, and activity details
- Keep implementation **CQRS-lite** and read-optimized
- Add basic caching hooks if there is already an app pattern for Redis-backed caching; otherwise structure code so caching can be added cleanly later

Out of scope unless already trivial and consistent with existing patterns:
- Full analytics engine or forecasting
- Complex charting library integration
- Mobile implementation
- New notification subsystem
- Full audit/explainability UI
- Background generation of daily briefings if not already present
- Heavy redesign of auth/tenant plumbing
- Broad refactors unrelated to dashboard delivery

# Files to touch
Touch only the files needed after inspecting the current solution structure. Expect to work primarily in:

- `src/VirtualCompany.Web/**`
  - dashboard page/component(s)
  - shared layout/navigation if dashboard should be the default landing page
  - view models and UI components for cards/lists/empty states
- `src/VirtualCompany.Api/**`
  - dashboard controller/endpoints
  - request/response DTOs if API-backed web calls are used
- `src/VirtualCompany.Application/**`
  - dashboard query contracts
  - query handlers/services
  - read models for executive cockpit data
- `src/VirtualCompany.Infrastructure/**`
  - query repositories / EF Core read access
  - tenant-scoped aggregate queries
  - optional caching adapter usage if already established
- `src/VirtualCompany.Domain/**`
  - only if a small domain enum/value object is truly required; avoid unnecessary domain churn
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts only if this is the established cross-project pattern
- `README.md`
  - only if a short note is needed for new dashboard route or developer usage

Also inspect:
- existing auth/tenant resolution
- existing navigation shell
- existing task/approval/agent/workflow entities and statuses
- existing API conventions and query patterns
- existing test projects, if present

# Implementation plan
1. **Inspect current solution and establish existing patterns**
   - Review project boundaries and how `Web` talks to backend:
     - direct application service
     - HTTP API
     - shared contracts
   - Identify current tenant resolution mechanism and authorization conventions
   - Find existing entities/tables for:
     - tasks
     - approvals
     - agents
     - workflows
     - messages/conversations or summaries
     - audit/activity records
   - Reuse existing route/layout conventions so the dashboard feels native

2. **Define a minimal executive cockpit read model**
   Create a single query result tailored for the dashboard, e.g.:
   - workspace/company summary
   - daily briefing snippet
   - counts:
     - pending approvals
     - active alerts/exceptions
     - active agents
     - in-progress tasks
     - blocked tasks
     - running/blocked workflows
   - department KPI cards:
     - start with pragmatic summary metrics derived from available data
     - if true KPI data is not yet modeled, use clearly labeled operational proxy metrics
   - recent activity feed:
     - recent tasks
     - approvals
     - workflow state changes
     - notable agent activity if available
   - setup state flags:
     - hasAgents
     - hasKnowledge
     - hasWorkflows
     - hasTasks
     - hasApprovals
     - hasBriefing

   Keep the response shape stable and UI-friendly.

3. **Implement application-layer query contract**
   Add a query/service in `VirtualCompany.Application` such as:
   - `GetExecutiveCockpitQuery`
   - `ExecutiveCockpitDto`
   - supporting DTOs for cards, alerts, approvals, activity items, and empty-state flags

   Requirements:
   - tenant/company ID required
   - read-only
   - no business mutations
   - deterministic and testable composition
   - no UI-specific formatting beyond simple labels where appropriate

4. **Implement infrastructure read aggregation**
   In `VirtualCompany.Infrastructure`, build tenant-scoped queries against the transactional store.
   Prioritize correctness and simplicity:
   - pending approvals from `approvals` with pending-like statuses
   - alerts from blocked/failed workflows/tasks and pending escalations if modeled
   - recent activity from tasks, approvals, workflow instances, and possibly audit events if available
   - KPI cards from available operational data grouped by department/role where possible

   Guidance:
   - enforce `company_id` filtering everywhere
   - prefer projection queries over loading full aggregates
   - cap list sizes for dashboard widgets
   - sort by recency/priority
   - if no dedicated alert model exists, derive alerts from:
     - blocked tasks
     - failed tasks
     - awaiting approval tasks
     - blocked/failed workflows
     - pending approvals nearing expiry if expiry exists

5. **Add API endpoint(s)**
   In `VirtualCompany.Api`, expose a tenant-scoped endpoint such as:
   - `GET /api/dashboard/executive-cockpit`
   or equivalent existing route convention

   Requirements:
   - authenticated
   - company context resolved from current tenant/workspace selection
   - forbidden/not found behavior consistent with existing tenant access rules
   - return a single aggregated payload for efficient page load

6. **Build the Blazor dashboard page**
   In `VirtualCompany.Web`, create the executive cockpit page as the primary command center.
   Include sections:
   - **Daily briefing**
     - show latest summary if available
     - otherwise show a helpful empty state
   - **Pending approvals**
     - top pending items with count and CTA
   - **Alerts / exceptions**
     - prioritized operational issues
   - **Department KPI cards**
     - concise cards with value and simple trend/secondary label if available
   - **Recent activity**
     - compact feed with timestamps and links
   - **Quick navigation / drill-ins**
     - links to agents, tasks, workflows, approvals

   UI expectations:
   - responsive desktop-first layout
   - clear hierarchy for executive scanning
   - loading, error, and empty states
   - avoid overengineering charts if data is not ready; cards and trend badges are enough

7. **Make dashboard the primary landing experience**
   If consistent with current app flow:
   - route authenticated users to the dashboard after workspace selection/onboarding
   - ensure nav highlights dashboard as the main destination
   - do not break onboarding flows or deep links

8. **Implement empty states carefully**
   Since ST-601 explicitly requires setup guidance:
   - if no agents: prompt to hire/configure agents
   - if no workflows: prompt to enable workflows
   - if no knowledge: prompt to upload SOPs/docs
   - if no approvals/tasks/activity: explain that activity will appear as the workspace becomes active

   Empty states should include actionable links where routes exist.

9. **Add performance-minded safeguards**
   - keep widget list sizes small
   - avoid N+1 queries
   - use projections
   - if a caching abstraction already exists, cache the aggregate briefly per tenant
   - otherwise isolate aggregation behind a service interface so Redis caching can be added later without UI/API changes

10. **Add tests where the repo pattern supports them**
   Prefer focused tests for:
   - tenant scoping
   - empty-state composition
   - alert derivation logic
   - pending approval counts
   - recent activity ordering
   - API authorization behavior

11. **Document assumptions in code comments or task notes**
   Since acceptance criteria are sparse, explicitly note any temporary KPI proxies or derived alert logic so future stories can replace them cleanly.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate the web dashboard flow:
   - sign in as a user with a valid company membership
   - confirm the dashboard loads as the main command center
   - confirm data shown is scoped to the selected company only

4. Validate widget behavior with seeded or local test data:
   - tenant with no setup data shows guided empty states
   - tenant with approvals shows pending approval count/list
   - tenant with blocked/failed tasks/workflows shows alerts
   - tenant with recent activity shows ordered feed
   - KPI cards render without layout breakage even when some values are zero/missing

5. Validate drill-in navigation:
   - approvals widget links to approvals page/route
   - activity items link to related tasks/workflows/agents where routes exist
   - empty-state CTAs navigate to relevant setup areas

6. Validate authorization and tenant isolation:
   - attempt access with another tenant context and confirm forbidden/not found behavior per existing conventions
   - verify no cross-tenant records appear in aggregate counts or lists

7. Validate resilience and UX:
   - loading state appears while data fetch is in progress
   - API or query failure shows safe error UI
   - page remains usable with partial/empty data

8. If caching is added:
   - verify cache key includes tenant/company context
   - verify stale data window is acceptable for dashboard use
   - verify cache invalidation/TTL is documented or obvious in code

# Risks and follow-ups
- **Risk: true KPI data may not yet exist**
  - Mitigation: use clearly labeled operational summary cards as placeholders
  - Follow-up: replace with richer analytics in later cockpit stories

- **Risk: no dedicated alerts model exists**
  - Mitigation: derive alerts from blocked/failed tasks, workflows, and pending approvals
  - Follow-up: introduce first-class alert/notification entities in ST-603

- **Risk: daily briefing generation may not yet be implemented**
  - Mitigation: show latest available summary if present; otherwise a setup/coming-soon empty state
  - Follow-up: connect to ST-505 scheduled briefings

- **Risk: route targets for drill-ins may be incomplete**
  - Mitigation: link only to existing pages or add safe placeholder routes if already acceptable in the app
  - Follow-up: flesh out detail pages in related stories

- **Risk: aggregate queries may become expensive**
  - Mitigation: keep projections lean, cap result sizes, and structure for Redis caching
  - Follow-up: add explicit dashboard cache and precomputed aggregates if needed

- **Risk: ambiguity from missing task-level acceptance criteria**
  - Mitigation: implement directly against ST-601 and architecture notes, keeping scope modest and extensible
  - Follow-up: capture any assumptions in PR notes and TODOs for product confirmation

- **Follow-up candidates after this task**
  - richer trend indicators and charts
  - first-class alert severity/prioritization
  - audit/explainability drill-through integration with ST-602
  - notification inbox integration with ST-603
  - mobile summary parity with ST-604