# Goal
Implement backlog task **TASK-29.4.2** for story **US-29.4 Dashboard and contextual insight surfacing with action-oriented UX** by wiring persisted **FinanceAgentInsight** data into the web UI and application query layer so that:

- the executive dashboard shows:
  - a **Financial Health** panel
  - a **top-3 finance actions** section
  - an **insights feed** sourced from persisted `FinanceAgentInsight` records
- the **invoice**, **bill**, and **payment** detail pages each show an **Agent Insights** panel filtered to the current entity reference
- duplicate insights representing the same underlying condition are **grouped** in the dashboard feed and displayed once with an **occurrence count**
- newly processed financial events become visible in the UI within **one refresh cycle** or via an existing live-update mechanism, without manual repair

Work within the existing **.NET modular monolith** architecture, preserving tenant scoping, CQRS-lite boundaries, and clean separation between domain/application/infrastructure/web layers.

# Scope
In scope:

- Discover the existing finance insight model, persistence, and any current dashboard/detail page implementations
- Add or extend **application queries/view models** for:
  - dashboard finance insight summary
  - entity-scoped insight retrieval for invoice, bill, and payment pages
- Implement duplicate grouping logic for dashboard feed items using a stable grouping key derived from the persisted insight shape already in the codebase
- Update Blazor web pages/components to render:
  - dashboard Financial Health panel
  - dashboard top-3 finance actions
  - dashboard grouped insights feed
  - reusable Agent Insights panel on invoice/bill/payment detail pages
- Ensure all queries are **tenant-scoped**
- Ensure insight refresh behavior works after new financial events are processed, at minimum on page refresh

Out of scope unless required by existing code patterns:

- redesigning the finance insight generation pipeline
- introducing a new real-time transport if none exists
- broad dashboard redesign unrelated to finance insights
- schema changes unless absolutely necessary to support grouping/querying and no existing fields can be used

# Files to touch
Likely areas to inspect and update. Adjust to actual repository structure after discovery.

- `src/VirtualCompany.Application/**`
  - finance insight query handlers
  - dashboard query handlers
  - DTOs/view models for dashboard and entity detail pages
- `src/VirtualCompany.Domain/**`
  - only if a missing domain concept/value object is required for grouping semantics
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/query implementations
  - repository/query service wiring
- `src/VirtualCompany.Web/**`
  - dashboard page/component
  - invoice detail page/component
  - bill detail page/component
  - payment detail page/component
  - shared/reusable Agent Insights panel component
- `src/VirtualCompany.Api/**`
  - only if the web app consumes API endpoints rather than direct application services
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for tenant-scoped insight retrieval and grouped dashboard output
- any existing web/component test project if present

Also inspect for these likely concepts before coding:

- `FinanceAgentInsight`
- dashboard/cockpit queries
- invoice/bill/payment detail pages
- finance event processing handlers/workers
- any existing notification/live refresh hooks
- tenant context abstractions

# Implementation plan
1. **Discover existing implementation surface**
   - Search for:
     - `FinanceAgentInsight`
     - `Insight`
     - `Financial Health`
     - invoice/bill/payment detail pages
     - dashboard/cockpit/executive page models
   - Identify:
     - persisted fields available on `FinanceAgentInsight`
     - how entity references are represented for invoice, bill, and payment
     - whether there is already a dashboard query/DTO to extend
     - whether the web app uses server-side application services, API calls, or mediator-based page loading

2. **Define the query contract for dashboard finance insights**
   - Add or extend a dashboard query result model to include:
     - `FinancialHealth` summary block
     - `TopFinanceActions` collection limited to 3
     - `InsightsFeed` collection of grouped insight items
   - Reuse existing persisted insight fields where possible:
     - title/summary
     - severity/priority
     - recommended action
     - created/updated timestamp
     - entity reference
     - condition/category/type
   - Do not fabricate data in UI; source from persisted `FinanceAgentInsight` records

3. **Implement grouped dashboard feed logic**
   - Group duplicate insights for the same underlying condition using the best existing stable key in the model, for example:
     - explicit condition key/code if present
     - otherwise a composite of normalized insight type/category + entity-independent condition discriminator
   - Grouping must avoid collapsing unrelated insights
   - For each grouped item, return:
     - representative title/summary
     - severity
     - occurrence count
     - latest timestamp
     - optional sample/related entity references if already supported
   - Prefer grouping in the application/infrastructure query layer, not in the Razor component

4. **Implement Financial Health panel projection**
   - Build a summary projection from persisted finance insights and/or existing finance aggregates already in the codebase
   - If a dedicated financial health aggregate already exists, compose it with insight data
   - If not, derive a lightweight panel from persisted insight severities/statuses without inventing unsupported business metrics
   - Keep the panel deterministic and tenant-scoped

5. **Implement top-3 finance actions projection**
   - Select the top 3 actionable finance insights from persisted records
   - Use existing priority/severity/recommended-action fields if available
   - Define stable ordering, e.g.:
     - highest severity first
     - then newest
     - then deterministic tie-breaker
   - Exclude non-actionable informational insights if the model distinguishes them

6. **Add entity-scoped insight queries**
   - Create or extend a reusable query for:
     - current tenant/company
     - entity type: invoice, bill, payment
     - entity reference/id
   - Return only insights linked to the current entity reference
   - Ensure the query shape is suitable for a shared UI component

7. **Build a reusable Agent Insights panel component**
   - Create a shared Blazor component in `VirtualCompany.Web` that accepts a view model like:
     - panel title
     - list of insight items
     - empty state
   - Render concise insight cards/rows with:
     - title/summary
     - severity badge
     - recommended action
     - timestamp
     - occurrence count only where relevant
   - Keep styling consistent with existing dashboard/detail page patterns

8. **Wire the dashboard UI**
   - Update the executive dashboard page to render:
     - Financial Health panel
     - top-3 finance actions section
     - grouped insights feed
   - Ensure empty states are graceful when no insights exist
   - Preserve existing tenant/company context loading patterns

9. **Wire invoice, bill, and payment detail pages**
   - Update each detail page to load entity-scoped insights using the current entity reference
   - Render the shared Agent Insights panel
   - Ensure page load remains resilient if no insights exist

10. **Refresh/update behavior**
   - Verify how new financial events currently update persisted insights
   - Ensure the UI reads fresh data on normal page refresh without stale caching issues
   - If there is existing polling, SignalR, or event-driven refresh infrastructure, hook into it only if low-risk and already established
   - Minimum acceptable outcome: updated insights appear after one browser refresh cycle

11. **Tenant isolation and authorization**
   - Ensure every query filters by `company_id`/tenant context
   - Ensure entity-scoped queries cannot leak insights across tenants even if entity IDs collide
   - Follow existing authorization/page access patterns

12. **Testing**
   - Add tests for:
     - grouped dashboard feed deduplicates same-condition insights and returns occurrence count
     - invoice/bill/payment entity-scoped queries return only matching insights
     - tenant scoping is enforced
     - top-3 action ordering is deterministic
   - Prefer integration-style tests around application/API query behavior if that is the project norm

13. **Keep changes minimal and idiomatic**
   - Match existing naming, mediator/query patterns, DTO conventions, and Razor component style
   - Avoid introducing new abstractions unless repeated usage clearly justifies them

# Validation steps
1. **Code discovery**
   - Confirm actual locations of:
     - `FinanceAgentInsight`
     - dashboard page
     - invoice/bill/payment detail pages
     - finance event processing flow

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Functional verification**
   - Seed or use existing test data with multiple `FinanceAgentInsight` records including:
     - duplicate insights for the same condition
     - insights tied to an invoice
     - insights tied to a bill
     - insights tied to a payment
   - Verify dashboard shows:
     - Financial Health panel
     - top-3 finance actions
     - grouped feed with occurrence count
   - Verify each detail page shows only insights for its current entity reference

5. **Refresh verification**
   - Trigger or simulate a new financial event that produces/updates persisted insights
   - Refresh the relevant page once
   - Confirm updated insight data appears without manual DB repair or cache clearing

6. **Tenant isolation verification**
   - With at least two tenants, confirm one tenant cannot see another tenant’s finance insights on dashboard or detail pages

# Risks and follow-ups
- **Risk: unclear grouping key**
  - If `FinanceAgentInsight` lacks an explicit condition identifier, grouping may require a carefully normalized composite key. Document the chosen rule in code comments and tests.
- **Risk: stale caching**
  - Existing dashboard caching may delay visibility of newly processed insights. If cache is present, ensure invalidation/TTL behavior satisfies the acceptance criterion.
- **Risk: inconsistent entity reference modeling**
  - Invoice, bill, and payment pages may use different identifiers than insight persistence. Add a small mapping layer if needed, but avoid schema churn unless unavoidable.
- **Risk: dashboard data source mismatch**
  - If the current dashboard uses a separate aggregate model, extend it rather than bypassing established query composition patterns.
- **Follow-up**
  - If live updates are not already implemented, note that current delivery satisfies acceptance via refresh-cycle visibility, and propose a later enhancement for push-based updates.
- **Follow-up**
  - If grouping logic becomes business-critical, consider promoting the grouping key to an explicit persisted field in a future task rather than relying on derived composites.