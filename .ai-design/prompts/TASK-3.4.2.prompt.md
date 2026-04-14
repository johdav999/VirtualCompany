# Goal
Implement backlog task **TASK-3.4.2 — Create unified deep-link resolver for tasks, workflows, and approvals** for story **US-3.4 Action-oriented insights and deep links to operational work**.

Deliver a tenant-safe, testable action-linking capability that supports the executive/dashboard action queue by:
- generating a unified deep link for action items targeting **tasks**, **workflow instances**, and **approvals**
- preserving stable priority ordering for equal scores
- persisting per-user acknowledgment state across refreshes
- covering scoring, deep-link generation, and acknowledgment persistence with automated tests

This should fit the existing **modular monolith / CQRS-lite / ASP.NET Core + Blazor + PostgreSQL** architecture and avoid UI-specific link logic being scattered across modules.

# Scope
In scope:
- Add a **unified deep-link resolver** in the application layer or a shared dashboard/action-queue service.
- Support deep links for at least:
  - task detail targets
  - workflow instance detail targets
  - approval detail targets
- Ensure generated links are:
  - tenant-safe
  - deterministic
  - based on canonical route patterns already used by the web app, or introduce centralized route definitions if missing
- Add/extend the action queue query model so each item includes:
  - priority
  - reason
  - owner
  - due time or SLA state
  - deep link
  - acknowledgment state for current user
- Implement stable ordering for identical scores using a deterministic tie-breaker.
- Persist acknowledgment state per user and per action item.
- Add automated tests for:
  - scoring behavior
  - stable ordering
  - deep-link generation
  - acknowledgment persistence

Out of scope unless required by existing code structure:
- Large dashboard redesign
- Mobile-specific deep-link handling beyond returning canonical app/web target identifiers if already supported
- Reworking unrelated notification/inbox systems
- Introducing a new microservice or external routing system

# Files to touch
Inspect the solution first, then update the most appropriate files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:
- Domain models/value objects for action queue items and acknowledgment state
- Application queries/handlers/services for dashboard action queue assembly
- A new centralized deep-link resolver interface + implementation
- Persistence configuration/entities/migrations for acknowledgment storage
- API endpoints or existing dashboard endpoints returning action queue items
- Blazor pages/components consuming the action queue and acknowledgment action
- Tests for resolver, ordering, and persistence

If present, prefer touching existing equivalents such as:
- dashboard/cockpit query handlers
- analytics/cockpit module services
- approval/task/workflow detail route definitions
- existing EF Core DbContext + entity configurations
- existing migration mechanism in infrastructure

# Implementation plan
1. **Discover existing dashboard and routing structure**
   - Find current executive cockpit/dashboard action queue implementation, if any.
   - Identify existing route patterns in `VirtualCompany.Web` for:
     - task details
     - workflow details
     - approval details
   - Identify current API/query models used by dashboard widgets.
   - Identify whether action scoring already exists; if so, extend rather than replace.

2. **Define a canonical action target model**
   - Introduce a small, explicit target abstraction, e.g.:
     - `ActionTargetType` = `Task | Workflow | Approval`
     - `TargetId`
     - optional `CompanyId` only if needed internally, not exposed in unsafe ways
   - Ensure action queue items can carry enough metadata to resolve links without UI-specific branching spread everywhere.

3. **Create a unified deep-link resolver**
   - Add an interface in application/shared layer, e.g. `IActionDeepLinkResolver`.
   - Implement a single resolver that maps:
     - task target -> canonical task route
     - workflow target -> canonical workflow route
     - approval target -> canonical approval route
   - Prefer returning a structured result if useful, e.g.:
     - `Href`
     - `TargetType`
     - `TargetId`
   - Keep route generation deterministic and centralized.
   - Do not hardcode links in multiple handlers/components.

4. **Extend action queue DTO/view model**
   - Ensure each action item includes:
     - unique action item id
     - priority/score
     - reason
     - owner
     - due time or SLA state
     - deep link
     - acknowledgment state for current user
   - If the queue aggregates from multiple modules, normalize them into one application DTO.

5. **Implement stable priority ordering**
   - Apply configured scoring rules consistently.
   - For equal scores, add deterministic tie-breakers, for example:
     1. score descending
     2. due time / SLA urgency
     3. created time ascending or descending, whichever best matches existing semantics
     4. stable entity id ascending
   - Document the tie-breaker in code comments/tests.
   - Ensure ordering is stable across refreshes for identical inputs.

6. **Persist acknowledgment state**
   - Add a persistence model/table for per-user acknowledgment of action insights.
   - Recommended shape:
     - `id`
     - `company_id`
     - `user_id`
     - `action_item_key`
     - `acknowledged_at`
   - `action_item_key` must be deterministic and derived from the normalized action item identity, not transient UI position.
   - Add EF Core configuration and migration.
   - Ensure tenant scoping and user scoping are enforced in queries and commands.

7. **Add acknowledgment command/API**
   - Implement a command/endpoint to mark an insight/action item as acknowledged.
   - Make it idempotent.
   - On subsequent dashboard loads, merge acknowledgment state into returned action items for the current user.
   - If unacknowledge is already a pattern in the codebase, support it only if straightforward; otherwise just implement acknowledge.

8. **Integrate into dashboard query**
   - Update the dashboard/cockpit action queue query handler to:
     - gather approvals, risks, blocked workflows, and opportunities from existing sources
     - compute score/priority
     - resolve deep links via the unified resolver
     - merge acknowledgment state
     - return deterministically ordered items
   - Keep orchestration in application layer, not in controllers or Blazor components.

9. **Update web UI**
   - Ensure the dashboard action queue renders:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep link CTA/navigation
     - acknowledgment action
   - Use the returned deep link directly rather than reconstructing routes in the component.
   - Preserve current UX patterns and styling.

10. **Add automated tests**
    - Unit tests:
      - deep-link resolver returns correct route for task/workflow/approval
      - scoring and tie-break ordering are deterministic
      - action item key generation is stable
    - Integration/API tests:
      - acknowledged item remains acknowledged after refresh/query reload
      - acknowledgment is user-specific
      - tenant isolation is preserved
    - Prefer existing test patterns in `tests/VirtualCompany.Api.Tests`.

11. **Keep implementation architecture-aligned**
    - CQRS-lite: query for queue, command for acknowledgment
    - tenant-aware everywhere
    - no direct DB access from UI
    - no duplicated route logic across modules

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify web behavior:
   - Open dashboard/cockpit
   - Confirm action queue shows items with:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep link
   - Click deep links for:
     - a task item
     - a workflow item
     - an approval item
   - Confirm each navigates to the correct detail page

4. Verify stable ordering:
   - Seed or use test data with identical scores
   - Refresh multiple times
   - Confirm ordering remains unchanged

5. Verify acknowledgment persistence:
   - Acknowledge an action item
   - Refresh page
   - Confirm acknowledged state persists for same user
   - Sign in as different user in same tenant
   - Confirm acknowledgment does not leak unless intentionally shared
   - Confirm cross-tenant access is not possible

6. If migrations are used in repo workflow:
   - Add/apply migration for acknowledgment persistence
   - Verify schema updates cleanly and tests still pass

# Risks and follow-ups
- **Route mismatch risk:** Existing Blazor routes may not be standardized. If so, first centralize route templates/constants to avoid brittle string duplication.
- **Identity of action items:** If action queue items are synthesized from multiple sources, defining a stable `action_item_key` is critical. Do not use array index or ephemeral timestamps.
- **Scoring ambiguity:** Acceptance criteria mention configured scoring rules, but config storage may not yet exist. Reuse existing scoring config if present; otherwise implement deterministic rules in a clearly isolated service with TODOs for future configuration.
- **Dashboard source fragmentation:** Approvals, risks, blocked workflows, and opportunities may currently come from different modules. Avoid over-coupling by normalizing through an application-layer assembler.
- **Tenant/user scoping:** Acknowledgment persistence must be both tenant-scoped and user-scoped.
- **Mobile follow-up:** If mobile also needs unified deep links later, consider evolving resolver output from plain URL to a structured navigation descriptor usable by both web and MAUI.
- **Future follow-up:** Consider adding:
  - unacknowledge support
  - bulk acknowledge
  - audit event for acknowledgment
  - shared route registry for all entity detail pages