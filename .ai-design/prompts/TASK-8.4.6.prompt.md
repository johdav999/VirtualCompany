# Goal
Implement backlog task **TASK-8.4.6 — “Health summary can initially be derived from task state and last activity”** for **ST-204 Agent roster and profile views**.

Add a first-pass, tenant-safe **agent health summary** that is computed from existing data rather than introducing a new persisted health model. The health summary should be usable in the **agent roster** and **agent profile/detail** views, and should derive from:
- the agent’s current/recent **task state**
- the agent’s **last activity** timestamp

This should fit the current modular monolith / CQRS-lite architecture and avoid overengineering. Prefer a deterministic application-layer projection/query approach over domain persistence changes unless the existing code structure clearly requires a small supporting schema/query addition.

# Scope
In scope:
- Identify the existing roster/profile query path for agents.
- Add a derived **health summary** field/value object/DTO property for agent list and detail responses/view models.
- Compute health from existing task and activity signals, using simple documented rules.
- Ensure the computation is **company/tenant scoped**.
- Surface the derived health summary in the relevant web UI(s) for ST-204 if those views already exist.
- Add tests for the derivation logic and query behavior.

Out of scope:
- Introducing a long-term analytics/health scoring engine.
- Adding background jobs to precompute health.
- Adding ML/LLM-based health inference.
- Building a full workload forecasting model.
- Mobile changes unless the same shared DTO is already consumed there and the change is trivial/non-breaking.

Suggested initial health model:
- Keep it intentionally simple and explainable.
- Example categories are acceptable if they fit the codebase conventions, such as:
  - `Healthy`
  - `Busy`
  - `Blocked`
  - `NeedsAttention`
  - `Inactive`
- Derive from signals such as:
  - any assigned task in `blocked` => blocked/needs attention
  - any assigned task in `failed` => needs attention
  - many assigned tasks in `new`/`in_progress` => busy
  - no recent activity beyond threshold => inactive
  - otherwise healthy

If the codebase already has naming conventions or enums for summaries/status badges, reuse them instead of inventing parallel concepts.

# Files to touch
Inspect and update only the files needed after confirming actual project structure. Likely areas:

- `src/VirtualCompany.Application/**`
  - agent roster/profile queries
  - DTOs/view models
  - query handlers
  - any shared mapping/projection code
- `src/VirtualCompany.Domain/**`
  - only if a small enum/value object is warranted and consistent with existing patterns
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query/projection updates
  - repository/query service implementations
- `src/VirtualCompany.Web/**`
  - roster page/component
  - agent profile/detail page/component
  - badge/summary rendering
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for these views
- tests in the corresponding test project(s)
  - application tests for derivation rules
  - web/component tests if present in the solution

Do not create broad new layers or modules for this task.

# Implementation plan
1. **Discover the existing ST-204 implementation surface**
   - Find the current agent roster and profile/detail endpoints, queries, handlers, DTOs, and Blazor pages/components.
   - Identify where workload/health summary is currently missing, stubbed, or hardcoded.
   - Confirm how “last activity” is currently represented, if at all:
     - from agent updates
     - from latest assigned task update
     - from messages/audit/task activity
   - Prefer the simplest already-available signal. If no explicit agent activity exists, use the most recent relevant assigned task timestamp as the initial proxy.

2. **Define a minimal derived health contract**
   - Add a single derived field to the roster/profile response model, such as:
     - `HealthSummary`
     - optionally `HealthReason` if the UI already supports subtitle/help text
   - Keep the contract stable and UI-friendly.
   - If needed, define a small enum/string-backed type consistent with existing conventions. Avoid adding persistence for it.

3. **Implement deterministic derivation logic in the application layer**
   - Centralize the logic in one reusable place, e.g.:
     - `AgentHealthSummaryCalculator`
     - `AgentHealthDerivationService`
     - or a private helper in the query layer if that is the established pattern
   - Base the result on:
     - counts/presence of assigned tasks by status
     - latest activity timestamp
   - Use simple precedence rules, for example:
     1. blocked task present => `Blocked`
     2. failed task present => `NeedsAttention`
     3. no recent activity past threshold => `Inactive`
     4. active workload above threshold => `Busy`
     5. else => `Healthy`
   - Document the thresholds in code comments or constants.
   - Keep thresholds configurable only if the project already has a lightweight options pattern for query behavior; otherwise use internal constants.

4. **Update roster query/projection**
   - Extend the roster query to fetch the minimum data needed per agent:
     - assigned task status aggregates and/or existence checks
     - latest relevant activity timestamp
   - Ensure the query remains tenant-scoped by `company_id`.
   - Prefer efficient aggregation in SQL/EF projection over N+1 loading.
   - If the roster already returns workload summary, reuse that data to avoid duplicate computation.

5. **Update agent profile/detail query**
   - Add the same derived health summary to the detail view model/DTO.
   - Reuse the same derivation logic so roster and detail stay consistent.
   - If the detail page already shows recent activity, align the displayed timestamp with the health derivation source.

6. **Render the health summary in the web UI**
   - Update the roster list/table/cards to display the derived health summary.
   - Update the agent profile/detail page to display the same summary prominently but simply.
   - Reuse existing badge/chip/status components/styles if available.
   - Do not introduce heavy interactivity; keep with Blazor SSR-first guidance.

7. **Add tests**
   - Unit tests for derivation logic:
     - blocked task => blocked
     - failed task => needs attention
     - stale last activity => inactive
     - active workload => busy
     - no concerning signals => healthy
     - precedence cases (e.g. blocked + inactive should return the intended higher-priority state)
   - Query/handler tests:
     - tenant scoping is preserved
     - roster/detail include health summary
   - UI/component tests only if the solution already has a pattern for them.

8. **Keep implementation lightweight and future-friendly**
   - Add a short comment/TODO noting this is an initial derived summary for ST-204 and may later evolve into richer analytics under EP-6.
   - Do not add schema changes unless absolutely necessary for an existing query path.

# Validation steps
1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify roster behavior:
   - Open the web app and navigate to the agent roster view.
   - Confirm each visible agent shows a health summary.
   - Confirm filtering by department/status still works unchanged.
   - Confirm no cross-tenant data appears in health/workload calculations.

4. Manually verify profile/detail behavior:
   - Open an agent detail/profile page.
   - Confirm the same health summary appears and is consistent with the roster.
   - Confirm recent activity/task state shown on the page matches the derived summary logic.

5. Validate edge cases with seeded/dev data:
   - agent with blocked task
   - agent with failed task
   - agent with several active tasks
   - agent with no recent activity
   - agent with no tasks at all

6. If query SQL/logging is easy to inspect, confirm there is no obvious N+1 issue for roster loading.

# Risks and follow-ups
- **Ambiguous “last activity” source**: the architecture/backlog does not define a canonical agent activity stream yet. Use the best existing proxy and document it clearly.
- **Threshold mismatch**: inactivity/busy thresholds may be product-tunable later. Keep them simple and centralized so they can be adjusted.
- **Query performance**: naive per-agent task lookups could create N+1 issues on roster pages. Prefer grouped aggregates/projections.
- **UI wording drift**: if product language for health states is not yet standardized, keep labels easy to rename and avoid leaking implementation details.
- **Future evolution**: later stories under analytics/cockpit may want richer health/workload metrics, trendlines, alerts, or persisted summaries. This implementation should remain a derived v1 baseline, not a final analytics model.