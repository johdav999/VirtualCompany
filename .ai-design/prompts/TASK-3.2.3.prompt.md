# Goal
Implement **TASK-3.2.3 — agent status card components and detail navigation** for **US-3.2 Agent status cards with workload, health, and recent activity** in the existing .NET solution.

Deliver a tenant-scoped web experience where:
- each agent card shows:
  - current workload
  - computed health status
  - active alerts count
  - 5 most recent actions
- status data is derived from live system data and refreshes within 60 seconds of source updates
- selecting a card opens an agent detail view or deep link showing active tasks, workflows, and alerts
- health is computed from defined thresholds for:
  - failed runs
  - stalled work
  - policy violations
- automated tests cover:
  - health/status calculation
  - recent action ordering
  - deep-link routing

Use the existing architecture and keep implementation aligned with:
- modular monolith
- CQRS-lite
- tenant-aware queries
- Blazor Web App frontend
- ASP.NET Core backend
- PostgreSQL-backed domain data
- no fake/static UI-only data except where unavoidable for transitional wiring

# Scope
In scope:
- add or extend backend query/service logic to produce an **agent status summary DTO/view model**
- compute workload, health, alerts count, and recent actions from persisted live data
- define health threshold logic in application/domain code with clear, testable rules
- expose tenant-scoped API or application query endpoint(s) for:
  - roster/status cards
  - agent detail navigation payload
- implement/update Blazor components/pages for:
  - agent status card UI
  - card click/deep-link navigation
  - agent detail page/section with active tasks, workflows, and alerts
- implement refresh behavior so UI data updates within 60 seconds
- add automated tests for acceptance criteria

Out of scope unless required for compilation/integration:
- redesigning the full roster page UX beyond what is needed for cards and navigation
- mobile app changes
- introducing new infrastructure like SignalR if polling/refresh is sufficient
- broad analytics/dashboard refactors unrelated to agent status cards
- changing unrelated task/workflow/audit schemas unless a minimal migration is required

Implementation constraints:
- preserve tenant isolation on all queries
- prefer server-side aggregation/query handlers over UI-side composition
- keep health computation deterministic and unit-testable
- use existing modules/entities first before adding new persistence
- if thresholds are not already configurable, implement sensible defaults in code and structure for future config-driven thresholds

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely backend areas:
- `src/VirtualCompany.Application/**`
  - add query/handler/service for agent status summaries
  - add DTOs/view models for card and detail payloads
  - add health calculation service/policy
- `src/VirtualCompany.Domain/**`
  - add value objects/enums/helpers if health status belongs in domain
- `src/VirtualCompany.Infrastructure/**`
  - implement repository/query access against agents/tasks/workflows/approvals/audit/tool execution data
- `src/VirtualCompany.Api/**`
  - expose endpoint(s) if the web app consumes API routes rather than direct application services

Likely web areas:
- `src/VirtualCompany.Web/**`
  - roster/dashboard page containing agent cards
  - new or updated reusable agent status card component
  - agent detail page with route/deep-link
  - refresh/polling logic
  - navigation wiring

Likely test areas:
- `tests/**`
  - application/domain tests for health calculation
  - query tests for recent action ordering and alert counts
  - web/component/routing tests if present in current test setup
  - API tests for deep-link endpoint behavior if applicable

Also inspect:
- `README.md`
- any existing architecture or conventions docs
- existing agent roster/profile/task/workflow pages and tests before creating new patterns

# Implementation plan
1. **Discover existing implementation surface**
   - Search for existing agent roster, profile, dashboard, task, workflow, approval, alert, and audit features.
   - Identify:
     - current agent list/detail pages
     - existing DTOs and query handlers
     - where “recent activity” is already sourced from
     - whether alerts are represented via approvals, notifications, workflow failures, audit events, or a dedicated model
   - Reuse existing routes and UI patterns where possible.

2. **Define the status contract**
   - Introduce a single application-layer contract for card data, e.g.:
     - `AgentStatusCardDto`
       - `AgentId`
       - `DisplayName`
       - `RoleName`
       - `Department`
       - `Status`
       - `Workload`
       - `HealthStatus`
       - `HealthReasons` or summary text
       - `ActiveAlertsCount`
       - `RecentActions` (top 5)
       - `LastUpdatedAt`
       - `DetailUrl` or route inputs
     - `AgentRecentActionDto`
       - action label/type
       - timestamp
       - target/entity reference if available
       - outcome/status
   - Add a detail DTO for the destination page:
     - active tasks
     - active workflows
     - active alerts
     - optional health breakdown

3. **Implement workload derivation**
   - Compute workload from live persisted task/workflow state, not static config.
   - Prefer a simple, explainable rule using current active work, for example:
     - count tasks assigned to the agent in statuses such as `new`, `in_progress`, `blocked`, `awaiting_approval`
     - optionally include active workflow items tied to the agent if the data model supports it
   - Expose both raw counts and a display label if useful.

4. **Implement health status calculation**
   - Add a dedicated calculator/service with explicit thresholds and comments.
   - Health must be based on:
     - failed runs
     - stalled work
     - policy violations
   - If no existing product thresholds are implemented, use code defaults such as:
     - **Healthy**: no threshold breaches
     - **Warning**: moderate failed runs / stalled items / policy denials in recent window
     - **Critical**: high failed runs / multiple stalled items / repeated policy violations
   - Use a recent time window that is easy to test and reason about.
   - Return both:
     - final health enum/status
     - contributing metrics/reasons for UI and tests
   - Keep thresholds centralized in one place for future configuration.

5. **Define recent actions source and ordering**
   - Build recent actions from the best available live source, preferring business/audit history over UI events.
   - Candidate sources in priority order:
     - `audit_events`
     - task updates/completions/failures
     - workflow instance transitions
     - tool executions / approvals if they represent meaningful agent actions
   - Normalize into a common recent-action DTO.
   - Ensure ordering is strictly descending by event/action timestamp.
   - Limit to 5 items per card.

6. **Implement active alerts count**
   - Derive from existing live exception/attention-needed records.
   - Prefer records that represent actionable issues, such as:
     - pending approvals tied to agent actions
     - failed workflows/tasks
     - escalations
     - policy violation events
     - unread/high-priority notifications if that is the established alert model
   - Document in code what qualifies as an “active alert”.
   - Reuse the same definition for card count and detail page list.

7. **Add application query handlers**
   - Implement tenant-scoped query/queries for:
     - list of agent status cards
     - single agent detail status payload
   - Keep query logic efficient:
     - avoid N+1 where possible
     - aggregate in SQL/repository layer when practical
     - use projection DTOs
   - Include `LastUpdatedAt` or equivalent freshness metadata.

8. **Expose API or web-consumable endpoints**
   - If the web app uses API controllers/endpoints, add:
     - roster/status endpoint
     - agent detail status endpoint
   - Ensure:
     - company/tenant scoping
     - authorization
     - not found/forbidden behavior is consistent with tenant isolation
   - If the web app calls application services directly, wire through the existing pattern instead.

9. **Build/update Blazor agent status card component**
   - Create or update a reusable component for the card.
   - Display:
     - agent identity basics
     - workload
     - health badge
     - active alerts count
     - 5 recent actions
   - Make the whole card or a clear CTA navigable to detail.
   - Keep markup accessible and testable.

10. **Implement detail navigation**
    - Add or update route for agent detail page, e.g. by agent id.
    - Card selection should navigate via deep link.
    - Detail page should show:
      - active tasks
      - workflows
      - alerts
      - optional health breakdown / recent activity
    - Preserve tenant context and existing navigation conventions.

11. **Implement refresh within 60 seconds**
    - Use a simple polling/refresh mechanism in Blazor unless a stronger existing pattern already exists.
    - Target refresh cadence at or under 60 seconds.
    - Ensure:
      - initial load is immediate
      - periodic refresh does not leak timers/resources
      - cancellation/disposal is handled correctly
      - stale/loading/error states are reasonable
    - Do not over-engineer with real-time infrastructure unless already present.

12. **Add automated tests**
    - Unit tests:
      - health calculation thresholds
      - health severity selection when multiple factors exist
    - Query/service tests:
      - recent actions sorted descending
      - only 5 recent actions returned
      - active alerts count logic
      - workload derivation
    - Routing/navigation tests:
      - card deep-link route generation
      - detail endpoint/page resolves expected agent
    - Tenant-scope tests if easy to add:
      - another company’s agent/status cannot be accessed

13. **Polish and align**
    - Keep naming consistent with existing stories/modules:
      - Agent Management
      - Analytics & Cockpit
    - Add concise comments where business rules are non-obvious.
    - Avoid introducing duplicate status concepts if existing enums/models already exist.

# Validation steps
1. Inspect and restore/build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify in web app:
   - open the agent roster/dashboard page containing status cards
   - confirm each card shows:
     - workload
     - health
     - active alerts count
     - 5 recent actions
   - click/select a card and verify navigation to the correct agent detail route
   - confirm detail view shows active tasks, workflows, and alerts

4. Verify refresh behavior:
   - change underlying source data for an agent if there is a seed/dev path available
   - confirm UI reflects updates within 60 seconds without full manual page reload

5. Verify health logic:
   - create or simulate cases for:
     - failed runs
     - stalled work
     - policy violations
   - confirm health status changes according to implemented thresholds

6. Verify recent action ordering:
   - ensure the newest actions appear first
   - ensure only 5 are shown on the card

7. Verify tenant isolation:
   - confirm agent status/detail queries do not expose another company’s data

# Risks and follow-ups
- **Ambiguous alert source model**: the backlog mentions alerts, but the current codebase may not yet have a dedicated alert entity. If so, define and document a temporary derived alert rule from existing approvals/failures/escalations without overcommitting the domain model.
- **Health threshold ambiguity**: acceptance criteria require defined thresholds, but architecture/backlog may not yet specify exact numeric values. Implement centralized defaults and note them clearly for later product confirmation.
- **Recent activity source fragmentation**: actions may exist across tasks, workflows, audit events, and tool executions. Prefer one canonical source if available; otherwise normalize carefully and document precedence.
- **Performance/N+1 risk**: card lists with per-agent aggregates can become expensive. Use grouped queries/projections and only fetch top 5 actions per agent efficiently.
- **Refresh implementation details**: polling is acceptable for the 60-second requirement, but ensure proper disposal in Blazor components to avoid duplicate timers.
- **Route consistency**: if an agent profile/detail route already exists, extend it rather than creating a competing detail page.
- **Future follow-up**:
  - make health thresholds configurable per company/agent
  - add richer alert taxonomy
  - consider push-based updates later if dashboard freshness requirements increase