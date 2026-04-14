# Goal
Implement backlog task **TASK-3.2.2 — Expose agent status card endpoint with recent activity payload** for story **US-3.2 Agent status cards with workload, health, and recent activity**.

Deliver a backend API capability that returns tenant-scoped agent status card data for the web/mobile cockpit, including:

- current workload
- computed health status
- active alerts count
- 5 most recent actions
- deep-link/detail route metadata for the selected agent

The implementation must satisfy these acceptance criteria:

- Each agent card displays current workload, health status, active alerts count, and the 5 most recent actions.
- Agent status values are derived from live system data and refresh within 60 seconds of source updates.
- Selecting an agent card opens a detail view or deep link with the agent's active tasks, workflows, and alerts.
- Health status is computed using defined thresholds for failed runs, stalled work, and policy violations.
- Automated tests validate status calculation, recent action ordering, and deep-link routing.

Use the existing .NET modular monolith structure and keep the implementation aligned with CQRS-lite, tenant isolation, and clean architecture boundaries.

# Scope
In scope:

- Add or extend an **agent status cards query endpoint** in the ASP.NET Core API.
- Implement an application-layer query/handler that returns a list of agent status cards for the current company.
- Compute status card fields from live persisted data, not hardcoded or static values.
- Define and implement health calculation rules using available domain data:
  - failed runs / failed tasks
  - stalled work
  - policy violations / denied executions / active alerts if available
- Return the **5 most recent actions** in descending recency order.
- Include a **deep-link/detail route payload** for each agent card that the frontend can use to navigate to the agent detail view.
- Ensure tenant scoping is enforced.
- Add automated tests for:
  - health/status calculation
  - recent activity ordering and max count
  - deep-link routing payload
- If needed, add lightweight DTOs/contracts and repository/query methods.

Out of scope unless required to make this task work:

- Full UI implementation in Blazor or MAUI
- New background refresh infrastructure beyond what is needed for query freshness
- Broad analytics/dashboard redesign
- New notification/alerting subsystem unless a minimal query abstraction is required
- Schema redesign unrelated to this endpoint

If the codebase already has adjacent roster/profile/dashboard APIs, prefer extending existing patterns instead of introducing parallel ones.

# Files to touch
Inspect the solution first, then update the most appropriate files in these areas.

Likely files/projects:

- `src/VirtualCompany.Api/...`
  - agent/cockpit/dashboard controller or minimal API endpoint registration
  - request/response contract mapping
- `src/VirtualCompany.Application/...`
  - query DTOs
  - query handler/service for agent status cards
  - interfaces for data access if needed
- `src/VirtualCompany.Domain/...`
  - health status enum/value object if one does not already exist
  - threshold logic abstractions only if domain placement is clearly appropriate
- `src/VirtualCompany.Infrastructure/...`
  - EF Core/query repository implementation
  - SQL/linq projections for tasks, workflows, approvals, audit events, tool executions, alerts
- `src/VirtualCompany.Shared/...`
  - shared response contracts only if this repo uses shared API contracts
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint/integration tests
- potentially:
  - `tests/...Application.Tests/...` if such a project exists for query logic
  - existing test fixtures/seeding helpers

Also inspect:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

Do not invent file paths blindly. First discover the existing structure for:
- agents
- tasks
- workflows
- approvals
- audit events
- tool executions
- tenant resolution
- API endpoint conventions
- tests and fixtures

# Implementation plan
1. **Discover existing architecture and conventions**
   - Search for:
     - agent roster/profile endpoints
     - dashboard/cockpit queries
     - tenant/company context resolution
     - MediatR/CQRS query handlers or equivalent
     - existing DTO naming conventions
     - task/workflow/approval/tool execution repositories
   - Reuse existing patterns for endpoint registration, authorization, and response envelopes.

2. **Define the response contract**
   Create a response model for an agent status card list, likely shaped roughly as:

   - `agentId`
   - `displayName`
   - `roleName`
   - `department`
   - `status`
   - `workload`
     - e.g. active task count, blocked count, awaiting approval count
   - `health`
     - enum/string such as `Healthy`, `Warning`, `Critical`
     - include optional reasons/signals if consistent with existing API style
   - `activeAlertsCount`
   - `recentActions` (max 5)
     - action type
     - summary/title
     - timestamp
     - related entity type/id if available
   - `detailRoute`
     - route/deep-link string or structured route object
   - `lastUpdatedAt`

   Keep the contract frontend-friendly and deterministic.

3. **Implement application query**
   Add a query and handler/service to fetch all active/relevant agents for the current company and compute card data.

   Suggested data sources based on architecture/backlog:
   - `agents` for identity and roster inclusion
   - `tasks` for workload, failed runs, stalled work, active tasks
   - `workflow_instances` for active workflows and stalled workflow signals
   - `approvals` for awaiting approval counts if relevant to workload/alerts
   - `tool_executions` and/or `audit_events` for policy violations / denied actions
   - notifications/alerts table if one already exists; otherwise derive active alerts from available exception-like records

   Keep this query read-only and tenant-scoped.

4. **Define health calculation rules**
   Implement explicit, testable thresholds. Prefer configuration/constants local to the feature if no central policy exists yet.

   Minimum required logic:
   - **Critical**
     - recent failed runs/tasks above threshold, or
     - stalled work above threshold, or
     - policy violations above threshold
   - **Warning**
     - some failures/stalls/violations present but below critical threshold
   - **Healthy**
     - none of the above thresholds exceeded

   Example fallback thresholds if no existing definitions are present:
   - failed runs in recent window:
     - warning: `>= 1`
     - critical: `>= 3`
   - stalled active work:
     - warning: `>= 1` task/workflow stale beyond threshold
     - critical: `>= 3`
   - policy violations / denied executions in recent window:
     - warning: `>= 1`
     - critical: `>= 2`

   Use a clear time window and stale threshold, for example:
   - recent window: last 24 hours
   - stalled threshold: active item not updated in 60+ minutes

   Before finalizing, check whether the codebase already defines thresholds or status semantics. Reuse them if present.

5. **Compute workload**
   Derive workload from live task/workflow state. At minimum include:
   - active tasks count (`new`, `in_progress`, `blocked`, `awaiting_approval` as appropriate)
   - optionally split counts by status if useful and easy
   - active workflows count if available
   - pending approvals count tied to the agent if available

   Keep the payload useful for status cards without overloading it.

6. **Build recent activity payload**
   Return the 5 most recent actions for each agent, ordered descending by timestamp.

   Candidate sources:
   - recent task updates/completions/failures
   - workflow progress events
   - tool executions
   - audit events

   Prefer a single normalized projection:
   - timestamp
   - action category/type
   - summary
   - target entity reference

   If multiple sources are combined, merge and sort in-memory only after filtering by tenant and agent, or use a SQL projection if practical.

7. **Add deep-link/detail route metadata**
   Include a route payload that supports the acceptance criterion:
   - selecting an agent card opens a detail view or deep link with the agent's active tasks, workflows, and alerts

   Prefer one of:
   - a canonical route string like `/agents/{agentId}`
   - a structured route object with route name and parameters
   - optional tabs/query params for `tasks`, `workflows`, `alerts` if consistent with current routing conventions

   Do not hardcode a frontend route format if the solution already has shared route constants/contracts.

8. **Expose API endpoint**
   Add or extend an endpoint such as:
   - `GET /api/agents/status-cards`
   - or under dashboard/cockpit if that is the established convention

   Requirements:
   - tenant/company scoped
   - authorized
   - returns deterministic JSON
   - no-cache or short-lived cache semantics consistent with “refresh within 60 seconds”
   - avoid stale server-side caching beyond 60 seconds

   If caching exists, ensure TTL is <= 60 seconds and invalidation behavior is acceptable.

9. **Ensure freshness requirement**
   The acceptance criterion says refresh within 60 seconds of source updates.

   Implementation guidance:
   - Prefer direct live query from PostgreSQL for correctness.
   - If using Redis or in-memory caching for expensive aggregates, cap TTL at 60 seconds.
   - Document the chosen freshness approach in code comments if not obvious.

10. **Add automated tests**
   Add tests covering:
   - **status calculation**
     - healthy when no failures/stalls/violations
     - warning when warning threshold met
     - critical when critical threshold met
   - **recent action ordering**
     - returns descending timestamp order
     - returns only 5 items
   - **deep-link routing**
     - route payload contains expected agent detail link/parameters
   - **tenant isolation**
     - data from another company is not returned
   - **endpoint contract**
     - response shape and HTTP success behavior

   Prefer integration-style API tests if the repo already uses them; otherwise test handler logic directly and add at least one endpoint test.

11. **Keep implementation clean**
   - Avoid embedding business logic in controllers.
   - Keep health computation in a dedicated helper/service/value object if non-trivial.
   - Keep query projections efficient; avoid N+1 per agent if possible.
   - Use async and cancellation tokens consistently.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify endpoint behavior:
   - locate the local API launch profile
   - call the new endpoint for a seeded/test tenant
   - confirm each returned card includes:
     - workload
     - health status
     - active alerts count
     - 5 recent actions max
     - detail/deep-link payload

4. Verify ordering and limits:
   - confirm recent actions are newest-first
   - confirm only 5 are returned even when more exist

5. Verify health logic:
   - seed or arrange data for:
     - no issues
     - warning threshold
     - critical threshold
   - confirm computed status matches expectations

6. Verify tenant isolation:
   - arrange data for two companies
   - confirm only current company agents and activity are returned

7. Verify freshness approach:
   - if uncached, confirm query reads current data
   - if cached, confirm TTL is no more than 60 seconds and test/inspect behavior accordingly

8. If Swagger/OpenAPI is enabled:
   - verify the endpoint appears with the correct response contract

# Risks and follow-ups
- **Alert source ambiguity:** the architecture/backlog references alerts, but the exact persistence model may not yet exist. If no dedicated alerts table exists, derive `activeAlertsCount` from available exception-like records and document the approximation in code/TODOs.
- **Health threshold ambiguity:** acceptance criteria require defined thresholds, but the repo may not yet centralize them. If absent, implement explicit local thresholds with named constants and leave a follow-up to externalize/configure them.
- **Recent activity source fragmentation:** actions may be spread across tasks, workflows, tool executions, and audit events. Normalize carefully and avoid inconsistent summaries.
- **Route contract mismatch:** frontend route expectations may not yet be formalized. Reuse existing route conventions if present; otherwise return a stable canonical detail URL and note any UI alignment follow-up.
- **Performance/N+1 risk:** per-agent aggregation can become expensive. Prefer grouped queries/projections and only introduce short TTL caching if needed while preserving the 60-second freshness requirement.
- **Status semantics drift:** if roster/profile pages already compute health/workload differently, consolidate rather than duplicate logic.
- **Potential follow-up tasks:**
  - expose detailed agent status breakdown endpoint
  - centralize health threshold configuration
  - add Redis-backed aggregate caching with <=60s TTL if query cost becomes high
  - align web/mobile clients to consume the new contract consistently