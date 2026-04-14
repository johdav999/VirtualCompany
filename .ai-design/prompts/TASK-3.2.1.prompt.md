# Goal
Implement **TASK-3.2.1 — Create agent status aggregation service for workload, health, and alerts** for **US-3.2 Agent status cards with workload, health, and recent activity**.

Build a tenant-aware backend aggregation capability that powers agent roster/status cards and agent deep-link navigation. The implementation must compute and expose, per agent:

- current workload
- computed health status
- active alerts count
- 5 most recent actions

It must derive values from live system data, support refresh within 60 seconds of source updates, and provide routing/deep-link metadata for agent detail views showing active tasks, workflows, and alerts.

Also add automated tests covering:

- status calculation
- recent action ordering
- deep-link routing

# Scope
Implement the backlog task in the existing .NET modular monolith using clean boundaries.

Include:

- Application-layer **agent status aggregation service**
- Query/DTO models for agent status cards
- Infrastructure data access for aggregating from transactional tables
- Health status computation using explicit thresholds for:
  - failed runs
  - stalled work
  - policy violations
- Recent activity retrieval limited to the latest 5 actions in descending time order
- Active alerts count aggregation
- Deep-link/detail route metadata for each agent card
- API endpoint(s) needed by web/mobile/UI consumers if not already present
- Unit/integration tests for aggregation logic and routing payloads

Assumptions to align with architecture and backlog:

- Multi-tenant enforcement via `company_id`
- CQRS-lite query path
- PostgreSQL as source of truth
- Prefer deterministic application logic over embedding business rules in controllers/UI
- Use existing modules/entities where possible rather than inventing parallel models

Out of scope unless required to complete the task cleanly:

- Full UI implementation of cards/detail pages
- Mobile-specific UI work
- New notification delivery systems
- Broad caching strategy beyond what is minimally needed for the 60-second freshness expectation
- Large schema redesigns unrelated to status aggregation

# Files to touch
Inspect the solution first, then update the most appropriate files in these areas.

Likely projects:
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Domain`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

## Domain
- Agent status/health enums or value objects if not already present
- Threshold/config models if agent health rules belong in domain/application contracts

Possible examples:
- `src/VirtualCompany.Domain/.../AgentHealthStatus.cs`
- `src/VirtualCompany.Domain/.../AgentStatusCard.cs` or equivalent query-facing contract only if domain already hosts such concepts

## Application
- Query service interface for agent status aggregation
- DTOs/view models for:
  - workload summary
  - health status
  - alerts count
  - recent actions
  - deep-link metadata
- Health calculation policy/service
- Query handler if MediatR/CQRS pattern exists

Possible examples:
- `src/VirtualCompany.Application/.../Agents/Queries/GetAgentStatuses/...`
- `src/VirtualCompany.Application/.../Agents/Services/IAgentStatusAggregationService.cs`
- `src/VirtualCompany.Application/.../Agents/Services/AgentHealthCalculator.cs`

## Infrastructure
- EF Core query/repository implementation
- SQL/linq projections across agents, tasks, workflows, approvals, tool executions, audit events, alerts/notifications if modeled
- Optional read-model optimization if existing patterns support it

Possible examples:
- `src/VirtualCompany.Infrastructure/.../Agents/AgentStatusAggregationService.cs`
- `src/VirtualCompany.Infrastructure/.../Persistence/...`

## API
- Endpoint/controller/minimal API route to fetch agent status cards
- Route/deep-link payload contract if API owns it
- DI registration

Possible examples:
- `src/VirtualCompany.Api/.../Controllers/AgentsController.cs`
- `src/VirtualCompany.Api/.../Endpoints/...`

## Tests
- Unit tests for health calculation thresholds
- Query/service tests for recent action ordering and top-5 limit
- API tests for tenant scoping and deep-link payload/routing

Possible examples:
- `tests/VirtualCompany.Api.Tests/.../AgentStatusAggregationTests.cs`
- `tests/VirtualCompany.Api.Tests/.../AgentStatusEndpointTests.cs`

# Implementation plan
1. **Inspect current architecture and existing agent/task/workflow APIs**
   - Find existing patterns for:
     - queries/handlers
     - DTOs
     - tenant resolution
     - authorization
     - EF DbContext and entity mappings
   - Reuse existing roster/profile endpoints if a status summary extension fits better than a new endpoint.
   - Identify whether alerts already exist as a first-class entity or must be derived from approvals/escalations/failures/policy denials.

2. **Define the status card contract**
   Create a response model that supports the acceptance criteria. It should include at minimum:
   - `AgentId`
   - `DisplayName`
   - `RoleName`
   - `Department`
   - `Workload`
     - recommended fields:
       - active task count
       - blocked task count
       - awaiting approval count
       - active workflow count
       - optional derived workload level
   - `HealthStatus`
     - enum/string such as `Healthy`, `Warning`, `Critical`
   - `ActiveAlertsCount`
   - `RecentActions` (max 5)
     - action type
     - summary/title
     - timestamp
     - related entity type/id
   - `DetailLink`
     - route or deep-link token/path to agent detail view with active tasks/workflows/alerts context

   Keep the contract UI-friendly but backend-owned.

3. **Define health computation rules explicitly**
   Implement deterministic threshold logic in one place, not scattered across queries.

   Minimum required signals:
   - **failed runs**
     - derive from failed tasks, failed workflow instances, failed tool executions, or existing run records if present
   - **stalled work**
     - derive from tasks/workflows in `in_progress`/`blocked` beyond threshold age
   - **policy violations**
     - derive from denied tool executions, policy-blocked actions, or audit events indicating policy violations

   Recommended approach:
   - Create a calculator that accepts aggregated metrics and returns:
     - health status
     - optional reasons list for future UI/debugging
   - Use sensible defaults if no per-agent thresholds exist yet
   - If agent-specific thresholds already exist in `approval_thresholds_json` or similar config, only use them if there is already a clean way to parse them; otherwise implement system defaults and document follow-up

   Example default logic:
   - `Critical` if any severe threshold breached, e.g.:
     - failed runs >= critical threshold
     - stalled work >= critical threshold
     - policy violations > 0 or >= threshold
   - `Warning` if lower threshold breached
   - else `Healthy`

   Keep thresholds centralized and testable.

4. **Implement workload aggregation**
   Aggregate live data per agent from current transactional tables.
   Suggested sources:
   - `tasks`
     - active: `new`, `in_progress`, `blocked`, `awaiting_approval`
   - `workflow_instances`
     - active/non-terminal states
   - `approvals`
     - pending approvals linked to agent-owned work if needed for workload/alerts
   - `tool_executions`
     - failed/denied executions for health signals
   - `audit_events`
     - recent actions and policy-related events if richer than task/workflow timestamps

   Ensure:
   - strict `company_id` filtering
   - no cross-tenant joins without tenant predicates
   - efficient grouping/projection

5. **Implement active alerts count**
   Prefer existing alert/notification model if present. If not, derive “active alerts” from currently actionable exception states, such as:
   - pending approvals tied to the agent
   - blocked tasks
   - failed tasks/workflows not resolved
   - policy-denied actions needing review
   - escalation/failure notifications if modeled

   Document the exact derivation in code comments and keep it deterministic.

6. **Implement recent actions feed**
   Build a unified recent activity projection for each agent and return the latest 5 actions ordered by descending timestamp.

   Candidate sources, in priority order depending on available data:
   - `audit_events` where actor is the agent or target is the agent’s work
   - task lifecycle changes for tasks assigned to the agent
   - workflow instance updates involving the agent
   - tool execution completions/denials
   - approval events related to the agent’s tasks

   Requirements:
   - stable descending ordering by timestamp
   - deterministic tie-breaker if timestamps match
   - limit to 5
   - concise action summary suitable for cards

7. **Implement deep-link/detail metadata**
   Each card must support opening a detail view or deep link with active tasks, workflows, and alerts.

   Do one of these based on existing routing conventions:
   - return a canonical web route like `/agents/{agentId}`
   - or return a route object with:
     - route name
     - parameters
     - optional query string for active tab/filter

   Recommended payload:
   - `DetailLink.Path = "/agents/{id}"`
   - optional:
     - `DetailLink.ActiveTab = "overview"` or `"work"`
     - `DetailLink.Query = { show: "active" }`

   Keep it consistent with current web/mobile route patterns if they already exist.

8. **Meet the 60-second freshness requirement**
   Since acceptance requires refresh within 60 seconds of source updates:
   - use live DB-backed queries as the default
   - avoid long-lived cache unless existing dashboard cache infrastructure is already in place
   - if caching is used, cap TTL at <= 60 seconds and ensure invalidation strategy is acceptable

   Prefer correctness and freshness over premature optimization.

9. **Expose the query through API**
   Add or extend an endpoint for agent status cards, for example:
   - `GET /api/agents/status`
   - or enrich existing roster endpoint

   Endpoint requirements:
   - tenant-scoped
   - authorization-aware
   - returns status card collection
   - optionally supports filtering by department/status if existing roster supports it

10. **Register dependencies**
   - Add DI registrations for:
     - aggregation service
     - health calculator
     - any query handlers
   - Keep application/infrastructure boundaries intact

11. **Add automated tests**
   Implement tests that directly map to acceptance criteria.

   Minimum test coverage:
   - **status calculation**
     - healthy when no thresholds breached
     - warning when lower threshold breached
     - critical when severe threshold breached
     - policy violations affect health as defined
   - **recent action ordering**
     - returns newest first
     - returns only 5 items
     - tie-breaking is deterministic
   - **deep-link routing**
     - each returned card includes expected route/path/parameters
   - **tenant isolation**
     - one company cannot see another company’s agent status data
   - **live data derivation**
     - aggregation reflects current persisted task/workflow/approval/tool execution state

12. **Keep implementation production-friendly**
   - Use async query patterns
   - Avoid N+1 queries
   - Prefer grouped projections/batched retrieval
   - Add concise comments where derivation rules are non-obvious
   - Do not leak internal-only fields into API contracts

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. If there is a focused test project or filter available, run targeted tests for the new feature.

4. Manually verify API behavior using existing local setup:
   - call the agent status endpoint for a tenant with seeded agents/tasks/workflows
   - confirm each card includes:
     - workload
     - health status
     - active alerts count
     - 5 most recent actions
     - detail/deep-link metadata

5. Validate freshness behavior:
   - update a task/workflow/approval/tool execution record
   - re-query within 60 seconds
   - confirm the status response reflects the change

6. Validate health thresholds:
   - create data representing:
     - failed runs
     - stalled work
     - policy violations
   - confirm health transitions match the implemented rules

7. Validate recent action ordering:
   - seed more than 5 actions with mixed timestamps
   - confirm only the latest 5 are returned in descending order

8. Validate tenant isolation:
   - query as tenant A and ensure tenant B agent/activity data is not returned

9. If endpoint enriches an existing roster API, verify no regressions in existing consumers/tests.

# Risks and follow-ups
- **Alert source ambiguity:** the backlog requires active alerts count, but the current model excerpt does not clearly define a dedicated alerts table. If no alert entity exists, derive alerts from pending approvals, blocked/failed work, and policy denials, and document this clearly.
- **Health threshold ownership:** if thresholds are not yet modeled per agent in a consumable way, implement system defaults now and note a follow-up to support configurable thresholds from agent policy/config.
- **Recent activity source consistency:** if audit events are incomplete, recent actions may need to be composed from multiple tables. Keep the projection deterministic and document source precedence.
- **Routing contract uncertainty:** if web/mobile route conventions are not yet standardized, return a canonical path string now and align later with shared navigation contracts.
- **Performance risk on roster pages:** aggregating many agents with recent activity can become expensive. Start with efficient grouped queries; if needed later, add a read model or short-lived Redis cache with <=60s TTL.
- **Schema gaps:** if current entities do not expose enough timestamps/statuses for stalled work or policy violations, add the smallest necessary fields/mappings only if already present in DB or migrations are straightforward.
- **Follow-up candidates:**
  - configurable per-agent health thresholds
  - dedicated alert domain model
  - cached dashboard read model
  - richer agent detail endpoint for active tasks/workflows/alerts tabs
  - UI card integration in Blazor roster/dashboard