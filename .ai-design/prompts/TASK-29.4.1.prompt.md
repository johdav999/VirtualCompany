# Goal
Implement backlog task **TASK-29.4.1** for story **US-29.4 Dashboard and contextual insight surfacing with action-oriented UX** by extending backend dashboard/detail view models and API aggregation so the UI can consume:

- a **Financial Health** panel payload
- a **Top 3 finance actions** payload
- a **grouped dashboard insight feed** sourced from persisted `FinanceAgentInsight` records
- **entity-specific Agent Insights** payloads for invoice, bill, and payment detail pages

The implementation must ensure:

- dashboard insight duplicates for the same underlying condition are grouped into one item with an occurrence count
- detail pages can filter insights by the current entity reference
- newly processed financial events surface updated insights through normal query refresh/live update paths without manual repair
- all queries remain **tenant-scoped** and fit the existing **CQRS-lite modular monolith** architecture

# Scope
In scope:

- Discover existing finance dashboard query/view model/API patterns
- Extend application query models/DTOs for dashboard and finance detail pages
- Add aggregation logic over persisted `FinanceAgentInsight` records
- Define grouping rules for duplicate insights representing the same underlying condition
- Add top-3 finance actions selection logic
- Add financial health summary projection for dashboard consumption
- Expose filtered insight collections for:
  - invoice detail
  - bill detail
  - payment detail
- Ensure refresh behavior works from persisted data after new financial events are processed
- Add/adjust tests for grouping, filtering, tenant isolation, and refresh visibility

Out of scope unless required by existing code structure:

- major UI redesign beyond wiring existing/new fields
- creating a new insight generation pipeline from scratch
- introducing new infrastructure like message brokers
- manual data backfill scripts
- unrelated dashboard widgets

# Files to touch
Inspect the solution first and update the exact files that already own these responsibilities. Likely areas:

- `src/VirtualCompany.Application/**`
  - dashboard queries/handlers
  - finance queries/handlers
  - DTOs/view models for dashboard and finance detail pages
- `src/VirtualCompany.Domain/**`
  - `FinanceAgentInsight` entity/value objects/enums if grouping keys or helper methods belong in domain
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query implementations/repositories/read models
  - persistence mappings if needed
- `src/VirtualCompany.Api/**`
  - endpoints/controllers returning dashboard and finance detail payloads
- `src/VirtualCompany.Web/**`
  - only if existing pages/components must be updated to bind the new API/view model fields
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- possibly application/domain test projects if present for query logic

Before editing, locate:

- dashboard endpoint and its response contract
- invoice/bill/payment detail endpoints and response contracts
- `FinanceAgentInsight` persistence model and how entity references are stored
- any existing financial health or action recommendation logic
- any cache/refresh/live update hooks already used by dashboard queries

# Implementation plan
1. **Discover current implementation**
   - Search for:
     - `FinanceAgentInsight`
     - dashboard query handlers
     - invoice/bill/payment detail queries
     - finance actions / recommendations / health summary
   - Identify the canonical read path:
     - controller/endpoint
     - application query + handler
     - infrastructure query/repository
     - web binding model if applicable
   - Do not create parallel patterns if one already exists.

2. **Define/extend response contracts**
   - Extend the dashboard response/view model to include:
     - `FinancialHealth`
     - `TopFinanceActions` limited to 3
     - `InsightFeed` with grouped items and occurrence count
   - Suggested shape, adapted to existing conventions:
     - `FinancialHealthSummaryDto`
       - status/score/trend/summary
       - optional counts/totals if already derivable
     - `FinanceActionDto`
       - title
       - description
       - priority/severity
       - target entity type/id if applicable
       - action label/navigation target if existing UX supports it
     - `GroupedFinanceInsightDto`
       - grouping key or stable id
       - title
       - summary
       - severity
       - category/type
       - latest occurred/published timestamp
       - occurrence count
       - representative entity refs / primary entity ref if appropriate
   - Extend invoice, bill, and payment detail response models with:
     - `AgentInsights` collection filtered to the current entity reference

3. **Implement grouped insight feed aggregation**
   - Source dashboard insights from persisted `FinanceAgentInsight` records only.
   - Build grouping logic for “same underlying condition”.
   - Prefer an existing stable grouping field if present, such as:
     - condition key
     - insight type + entity-independent fingerprint
     - correlation/reference key
   - If no such field exists, implement the smallest safe grouping rule based on persisted fields, for example:
     - tenant/company
     - insight type/category
     - normalized condition/reference key
     - optional subject dimensions that identify the same condition
   - Group duplicates into one dashboard item with:
     - representative/latest insight data
     - `OccurrenceCount`
   - Preserve tenant isolation in all grouping queries.
   - Avoid grouping unrelated insights that merely share severity/title.

4. **Implement top-3 finance actions**
   - Determine whether actions are already persisted on `FinanceAgentInsight` or derived from insight metadata.
   - Select the top 3 actions using deterministic ordering, ideally:
     1. highest severity/priority
     2. most recent
     3. stable tie-breaker
   - Ensure actions are sourced from persisted insight data or existing finance recommendation data, not fabricated ad hoc.
   - If multiple grouped insights each expose actions, choose the top 3 across the grouped feed.

5. **Implement financial health summary**
   - Reuse existing finance aggregates if available.
   - If absent, derive a lightweight summary from persisted finance state and/or insight severity mix already available in the read model.
   - Keep this incremental and aligned with current architecture; do not invent a large scoring engine unless one already exists.
   - Ensure the dashboard can render a panel with meaningful fields even if some values are null/empty.

6. **Add entity-filtered Agent Insights for detail pages**
   - For invoice, bill, and payment detail queries:
     - filter `FinanceAgentInsight` by tenant/company
     - filter by current entity type/reference/id
   - Return only insights relevant to that entity.
   - Keep ordering deterministic, e.g. newest first, then severity.
   - If the detail page query already returns a composite DTO, append `AgentInsights` there rather than creating extra round trips unless the current architecture clearly separates them.

7. **Refresh/update visibility**
   - Ensure dashboard/detail queries read directly from persisted `FinanceAgentInsight` state so newly processed financial events appear on next refresh.
   - If there is dashboard caching:
     - verify cache invalidation/TTL behavior
     - update invalidation hooks if needed so new insights appear within one refresh cycle
   - If there is live update infrastructure already present:
     - wire it only if trivial and already patterned
     - otherwise ensure standard refresh path satisfies acceptance criteria
   - Do not require manual repair or recomputation steps.

8. **API wiring**
   - Update API endpoints/controllers to return the extended models.
   - Preserve backward compatibility where practical:
     - additive fields only
     - avoid renaming/removing existing fields unless necessary and coordinated
   - Ensure authorization and tenant scoping remain intact.

9. **Tests**
   - Add/adjust tests covering:
     - dashboard returns financial health, top actions, grouped insight feed
     - duplicate insights for same condition are grouped once with correct occurrence count
     - invoice detail returns only invoice-linked insights
     - bill detail returns only bill-linked insights
     - payment detail returns only payment-linked insights
     - tenant A cannot see tenant B insights
     - after inserting/processing a new financial event + persisted insight, next query reflects updated results
   - Prefer integration tests at API/query level where feasible, with focused unit tests for grouping logic if extracted.

10. **Implementation constraints**
   - Follow existing naming, folder structure, and CQRS patterns.
   - Keep logic in application/infrastructure layers, not controllers/pages.
   - Use async EF/query patterns consistently.
   - Avoid N+1 queries; project efficiently.
   - Keep changes minimal, cohesive, and production-ready.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If relevant tests are easy to target, run finance/dashboard-related tests first, then full suite.

4. Manually verify via code/tests that:
   - dashboard response includes:
     - financial health payload
     - exactly up to 3 top finance actions
     - grouped insight feed with occurrence counts
   - duplicate persisted insights for the same condition collapse into one dashboard item
   - invoice/bill/payment detail responses include only matching entity insights
   - newly persisted insights are visible on the next query/refresh path
   - tenant scoping is enforced in all queries

5. If the web project is touched, verify pages compile and bindings match the updated contracts.

# Risks and follow-ups
- **Risk: no stable grouping key exists on `FinanceAgentInsight`.**
  - Mitigation: use the safest available persisted fingerprint and document assumptions in code comments/tests.
  - Follow-up: add an explicit domain-level `ConditionKey`/fingerprint in a later task if grouping remains heuristic.

- **Risk: dashboard caching delays visibility of new insights.**
  - Mitigation: inspect cache usage and add invalidation or shorten TTL for affected queries.
  - Follow-up: add event-driven cache busting/live updates if not already present.

- **Risk: top actions semantics are ambiguous.**
  - Mitigation: derive from persisted insight recommendation/action metadata using deterministic priority ordering.
  - Follow-up: formalize action ranking rules in product/domain language.

- **Risk: detail pages use separate read models/endpoints with inconsistent patterns.**
  - Mitigation: extend each existing query in place rather than forcing a shared abstraction prematurely.

- **Risk: UI contracts may already be consumed elsewhere.**
  - Mitigation: make additive changes and preserve existing fields.

- **Follow-up candidates after completion:**
  - explicit insight grouping fingerprint in domain/persistence
  - SignalR/live dashboard updates for insight changes
  - richer financial health scoring model
  - drill-through from grouped dashboard insight to underlying occurrences