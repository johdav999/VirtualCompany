# Goal
Implement backlog task **TASK-3.4.3 — Build action queue UI with acknowledgment interactions** for story **US-3.4 Action-oriented insights and deep links to operational work**.

Deliver a tenant-scoped, user-facing **dashboard action queue** in the Blazor web app that surfaces prioritized operational actions and allows each user to **acknowledge** an insight/action item with persistence across refreshes.

The implementation must satisfy these acceptance criteria:

- Dashboard displays a prioritized action queue containing **approvals, risks, blocked workflows, and opportunities**.
- Each action item includes **priority, reason, owner, due time or SLA state, and a deep link** to the target task, workflow, or approval.
- Priority ordering is consistent with configured scoring rules and remains **stable for identical scores**.
- Users can mark an insight as **acknowledged**, and the acknowledgment state persists across refreshes **for that user**.
- Automated tests verify **action scoring**, **deep-link generation**, and **acknowledgment persistence**.

Use the existing architecture constraints:
- Modular monolith
- ASP.NET Core backend
- Blazor Web App frontend
- PostgreSQL primary store
- Shared-schema multi-tenancy with `company_id` enforcement
- CQRS-lite application layer
- No business logic in UI components beyond presentation and event wiring

# Scope
In scope:

1. **Dashboard action queue read model and query path**
   - Aggregate action candidates from existing operational entities where available:
     - approvals
     - blocked workflows
     - risk-like items from task/workflow states if already modeled
     - opportunities from existing dashboard/analytics signals if already modeled
   - If some source types do not yet exist as first-class entities, implement a pragmatic internal projection/query composition that can still produce the required queue shape without overbuilding.

2. **Action scoring and stable ordering**
   - Introduce a deterministic scoring mechanism with explicit tie-breaking.
   - Keep scoring configurable in code/config structure suitable for later externalization.
   - Stable ordering for identical scores must be guaranteed by deterministic secondary sort keys.

3. **Deep-link generation**
   - Generate links to target approval/task/workflow pages using existing route patterns.
   - Avoid hardcoding links in Razor markup; centralize in a mapper/helper/service.

4. **Acknowledgment persistence**
   - Add persistence for per-user acknowledgment of action items.
   - Acknowledgment must be tenant-scoped and user-scoped.
   - Refreshing the dashboard must preserve acknowledged state.

5. **Blazor dashboard UI**
   - Add/extend a dashboard widget/section for the action queue.
   - Show required fields:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep link
     - acknowledgment state/action
   - Prioritized ordering must match backend query result.

6. **Tests**
   - Unit tests for scoring and stable ordering
   - Unit tests for deep-link generation
   - Integration/application tests for acknowledgment persistence and tenant/user scoping where feasible

Out of scope unless required by existing patterns:
- Mobile implementation
- New notification delivery flows
- New workflow/approval domain behavior unrelated to queue display
- Full admin UI for scoring rule configuration
- Broad analytics redesign

# Files to touch
Touch the minimum set needed, but expect changes across these areas.

## Likely backend/application files
- `src/VirtualCompany.Application/...`
  - Add query/DTOs/handlers for dashboard action queue
  - Add scoring service or policy class
  - Add acknowledgment command/handler
- `src/VirtualCompany.Domain/...`
  - Add action queue acknowledgment entity/value object if domain layer owns it
  - Add enums/constants for action item type/SLA state if needed
- `src/VirtualCompany.Infrastructure/...`
  - EF Core configuration/repository/query implementation
  - Migration support for acknowledgment persistence
- `src/VirtualCompany.Api/...`
  - Expose endpoint(s) if dashboard data is loaded via API rather than direct server-side app service calls

## Likely web files
- `src/VirtualCompany.Web/...`
  - Dashboard page/component
  - New action queue component/partial
  - View models
  - Event handler for acknowledge action
  - Styling updates if needed

## Likely test files
- `tests/VirtualCompany.Api.Tests/...`
  - Integration tests for acknowledgment persistence and query behavior
- If there are existing application/domain/web test projects, use them for:
  - scoring tests
  - deep-link generation tests

## Persistence/migrations
- Add a new migration for a table similar to:
  - `user_action_acknowledgments`
- If migrations are managed elsewhere in this repo, follow the existing project convention from README / migration docs.

Before editing, inspect:
- Existing dashboard implementation for ST-601
- Existing approval/task/workflow routes
- Existing tenant/user context abstractions
- Existing CQRS patterns
- Existing EF entity configuration and migration conventions

# Implementation plan
1. **Inspect current dashboard and operational modules**
   - Find the current executive cockpit/dashboard implementation in `VirtualCompany.Web`.
   - Identify existing query services/endpoints used by dashboard widgets.
   - Locate route patterns for:
     - approvals
     - tasks
     - workflows
   - Locate tenant/user context access patterns.
   - Locate existing entities/tables for approvals, tasks, workflow instances.

2. **Define the action queue contract**
   Create a read model/DTO with at least:
   - `ActionItemId` or deterministic queue item key
   - `ActionType` (`Approval`, `Risk`, `BlockedWorkflow`, `Opportunity`)
   - `PriorityLabel`
   - `PriorityScore`
   - `Reason`
   - `OwnerDisplay`
   - `DueAt` nullable
   - `SlaState` nullable/string/enum
   - `TargetEntityType`
   - `TargetEntityId`
   - `DeepLink`
   - `IsAcknowledged`
   - `AcknowledgedAt` nullable
   - `StableSortKey`

   Important: define a **deterministic item identity** for acknowledgment. If queue items are projections over heterogeneous entities, use a stable composite key pattern such as:
   - `{ActionType}:{TargetEntityType}:{TargetEntityId}`
   or equivalent canonical format.

3. **Add acknowledgment persistence**
   Implement a persistence model for per-user acknowledgment, likely:
   - table/entity: `user_action_acknowledgments`
   - fields:
     - `id`
     - `company_id`
     - `user_id`
     - `action_item_key`
     - `acknowledged_at`
     - optional metadata JSON / source type
   - unique index on `(company_id, user_id, action_item_key)`

   Requirements:
   - tenant-scoped
   - user-scoped
   - idempotent acknowledge behavior
   - safe on repeated clicks/refreshes

4. **Implement scoring rules**
   Add a scoring service/class in application layer that computes a numeric score from available signals.

   Minimum rule design:
   - base score by action type, with approvals/blocked/risk/opportunity weighted appropriately
   - urgency boost for overdue/near-due items
   - blocked state boost
   - pending approval age boost if available
   - optional severity/risk boost if source data supports it

   Keep rules:
   - deterministic
   - easy to test
   - encapsulated in one place

   Stable ordering requirement:
   - primary sort: descending `PriorityScore`
   - secondary deterministic sort(s), e.g.:
     - due date ascending/nulls last
     - created/started time ascending
     - canonical `ActionItemKey` ascending
   - Do not rely on database incidental ordering.

5. **Implement action queue query**
   Build an application query handler/service that:
   - resolves current `company_id` and `user_id`
   - gathers candidate items from approvals/tasks/workflows/other existing sources
   - maps them into a unified action item shape
   - computes score and deep link
   - joins acknowledgment state for current user
   - returns ordered results

   Notes:
   - Keep query tenant-scoped at source.
   - Prefer projection over loading full aggregates where possible.
   - If some action categories are not yet fully modeled, derive them pragmatically from existing statuses:
     - approvals from pending approvals
     - blocked workflows from workflow instances with blocked/failed reviewable state
     - risks from overdue/high-priority blocked tasks or explicit exception states if present
     - opportunities from available recommendation/insight records if they already exist; otherwise use a minimal placeholder source only if already supported by current dashboard data model
   - Do not invent broad new domain subsystems just to satisfy one widget.

6. **Centralize deep-link generation**
   Add a mapper/helper/service that converts target entity info into app routes, e.g.:
   - approval → `/approvals/{id}`
   - task → `/tasks/{id}`
   - workflow → `/workflows/{id}`

   Requirements:
   - one place only
   - tested
   - returns safe fallback if route is unavailable
   - no route string duplication across UI and backend if avoidable

7. **Add acknowledgment command**
   Implement an application command/handler:
   - input: `actionItemKey`
   - resolves current tenant/user
   - upserts acknowledgment row
   - returns success and updated state

   Ensure:
   - cannot acknowledge another tenant’s item
   - repeated acknowledge is idempotent
   - invalid/nonexistent item keys are handled safely according to existing app conventions

8. **Wire the UI**
   In the dashboard Blazor page/component:
   - render the action queue section
   - show items in backend-provided order
   - display:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep-link CTA
     - acknowledge button / acknowledged badge
   - on acknowledge:
     - call command
     - update local item state optimistically or refresh query
   - preserve accessibility and empty states

   Suggested UX:
   - section title like “Action Queue”
   - clear visual priority treatment
   - acknowledged items either:
     - remain visible with acknowledged badge, or
     - move lower visually only if acceptance criteria still clearly satisfied; default is keep visible with state badge
   - avoid removing item immediately unless current UX patterns already do so

9. **Add tests**
   Implement automated tests covering:

   **Scoring**
   - higher urgency outranks lower urgency
   - blocked/approval/risk/opportunity scoring follows intended precedence
   - identical scores use deterministic stable ordering

   **Deep links**
   - approval item maps to approval route
   - task item maps to task route
   - workflow item maps to workflow route
   - unsupported target returns safe fallback if applicable

   **Acknowledgment persistence**
   - acknowledge action persists for current user
   - refresh/requery returns `IsAcknowledged = true`
   - another user in same company does not inherit acknowledgment
   - another company cannot see/use acknowledgment from different tenant

10. **Run build/tests and refine**
   - Ensure solution builds
   - Ensure tests pass
   - Verify no tenant leakage
   - Verify dashboard renders with empty and populated states

# Validation steps
1. **Code inspection**
   - Confirm all new queries/commands follow existing CQRS-lite patterns.
   - Confirm tenant and user context are enforced in every query/command path.
   - Confirm deep-link generation is centralized, not duplicated.

2. **Database validation**
   - Verify acknowledgment table/migration exists.
   - Verify unique constraint on `(company_id, user_id, action_item_key)`.
   - Verify indexes support dashboard lookup.

3. **Functional validation**
   - Seed or use test data with:
     - at least one pending approval
     - one blocked workflow
     - one risk-like item
     - one opportunity-like item if supported
   - Open dashboard and verify queue appears.
   - Verify each item shows:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep link
   - Click deep links and confirm navigation target is correct.
   - Acknowledge an item and refresh page; state must persist.

4. **Ordering validation**
   - Create multiple items with different scores and verify descending order.
   - Create multiple items with identical scores and verify order remains stable across refreshes/requeries.

5. **Automated validation**
   Run:
   - `dotnet build`
   - `dotnet test`

   If test filtering is useful, run targeted tests first, then full suite.

6. **Regression validation**
   - Confirm existing dashboard widgets still load.
   - Confirm approvals/tasks/workflows pages still resolve.
   - Confirm no unauthorized cross-tenant data appears.

# Risks and follow-ups
- **Risk: source data for “opportunities” may not yet exist**
  - Mitigation: inspect current analytics/insight models first.
  - If absent, implement the queue to support the type contract but only populate from existing sources; document any partial population clearly in code comments/TODOs without fabricating a new subsystem.

- **Risk: unstable ordering if query is partially sorted in memory and partially in SQL**
  - Mitigation: define one canonical ordering strategy and apply it consistently after score computation.

- **Risk: acknowledgment identity drift**
  - If action items are projections, unstable keys will break persistence.
  - Mitigation: use a canonical, deterministic `action_item_key` derived from durable source identifiers.

- **Risk: route duplication**
  - Mitigation: centralize deep-link generation and test it.

- **Risk: tenant/user leakage in acknowledgment table**
  - Mitigation: enforce both `company_id` and `user_id` in reads/writes and cover with tests.

- **Risk: overbuilding a new domain**
  - Mitigation: keep this task focused on a dashboard read model + acknowledgment persistence, reusing existing approvals/tasks/workflows.

Follow-ups to note in comments or TODOs only if needed:
- externalize scoring rules to configuration/admin UI later
- add unread/actioned semantics if action queue converges with notifications
- consider dismiss/unacknowledge behavior later if product requires it
- consider mobile reuse once web behavior is stable