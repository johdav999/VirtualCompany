# Goal
Implement backlog task **TASK-29.4.3 — UI grouping and plain-English rendering for duplicate or related financial insights** for story **US-29.4 Dashboard and contextual insight surfacing with action-oriented UX**.

Deliver an end-to-end implementation that makes persisted `FinanceAgentInsight` records visible and useful in the web UI by:
- rendering a dashboard **Financial Health** panel
- rendering a **top-3 finance actions** section
- rendering an **insights feed** sourced from persisted `FinanceAgentInsight` records
- rendering **Agent Insights** panels on invoice, bill, and payment detail pages filtered to the current entity reference
- grouping duplicate or closely related insights representing the same underlying condition into a single dashboard item with an occurrence count
- ensuring newly processed financial events surface updated insights in the UI within one refresh cycle or via existing live-update mechanisms, without manual repair

Use the existing architecture and coding conventions in this repository. Prefer incremental, production-ready changes over speculative abstractions.

# Scope
In scope:
- Query/application-layer support for retrieving dashboard and entity-scoped finance insights from persisted records
- Grouping logic for duplicate/related insights based on stable business keys or deterministic grouping heuristics already supported by the data model
- Plain-English rendering/view-model shaping so UI text is concise, executive-friendly, and action-oriented
- Blazor web UI updates for:
  - executive dashboard finance sections
  - invoice detail page insights panel
  - bill detail page insights panel
  - payment detail page insights panel
- Tests covering grouping, filtering, and refresh visibility behavior where practical
- Any minimal API/query contract changes required to support the UI

Out of scope unless required by existing code structure:
- Reworking the finance insight generation pipeline itself
- Introducing a new real-time infrastructure stack
- Large redesigns of dashboard layout outside the finance insight surfaces
- Schema redesign unless absolutely necessary to support deterministic grouping
- Mobile app changes

Implementation constraints:
- Source all displayed insights from persisted `FinanceAgentInsight` records, not transient in-memory generation
- Preserve tenant scoping on all queries
- Keep CQRS-lite boundaries intact
- Keep UI explanations concise and operational; do not expose chain-of-thought
- If grouping requires assumptions, prefer deterministic server-side grouping over client-only grouping

# Files to touch
Inspect the solution first, then update the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Application/**`
  - finance insight query handlers / dashboard query handlers
  - DTOs/view models for dashboard and entity detail insight panels
  - grouping and plain-English projection logic
- `src/VirtualCompany.Domain/**`
  - only if a small domain helper/value object is needed for grouping semantics
- `src/VirtualCompany.Infrastructure/**`
  - EF/query implementations for persisted `FinanceAgentInsight` retrieval
  - repository/query projections
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if dashboard or detail pages fetch via API
- `src/VirtualCompany.Web/**`
  - dashboard page/components
  - invoice detail page/components
  - bill detail page/components
  - payment detail page/components
  - shared finance insight display components if appropriate
- `tests/VirtualCompany.Api.Tests/**`
  - API/query integration tests
- other relevant test projects if present for application/web layers

Before coding, locate:
- the `FinanceAgentInsight` entity/model and its persistence mapping
- dashboard query/view model pipeline
- invoice, bill, and payment detail page implementations
- any existing finance dashboard widgets or insight components
- any existing refresh/live update patterns already used in the web app

# Implementation plan
1. **Discover current finance insight flow**
   - Find the persisted `FinanceAgentInsight` model, fields, and relationships.
   - Identify fields that can support grouping, such as:
     - tenant/company id
     - insight type/category/severity
     - entity type/entity reference
     - source event/reference
     - condition key/dedup key/correlation key if already present
     - created/updated timestamps
     - status/active flags
   - Find how the executive dashboard currently loads data and whether finance sections already exist partially.
   - Find invoice, bill, and payment detail pages and how they load related data.

2. **Define deterministic grouping behavior**
   - Implement grouping on the server side for dashboard feed items.
   - Prefer an existing stable key on `FinanceAgentInsight` if available.
   - If no explicit grouping key exists, derive one from persisted fields that represent the same underlying condition. Example approach:
     - same company
     - same normalized insight type/category
     - same normalized target/entity family or condition reference
     - same active/open state
   - Do **not** group unrelated insights merely because their rendered text is similar.
   - For grouped items, return:
     - representative insight id
     - plain-English title/summary
     - severity/priority
     - occurrence count
     - latest timestamp
     - optional related entity references for drill-in
   - Preserve ordering so the most recent/highest priority grouped insights appear first.

3. **Add plain-English projection**
   - Create or update an application-layer mapper/projection that converts raw persisted insight data into concise UI text.
   - Ensure wording is:
     - plain English
     - action-oriented
     - short enough for dashboard cards/feed rows
   - Prefer templates based on insight type/category rather than dumping raw internal text.
   - Include optional supporting metadata such as:
     - “Seen on 4 items”
     - “Affects overdue invoices”
     - “Review recent failed payments”
   - Keep this logic centralized so dashboard and detail pages render consistently.

4. **Implement dashboard finance query shape**
   - Update or add a dashboard query that returns:
     - Financial Health panel data
     - top-3 finance actions
     - grouped insights feed from persisted `FinanceAgentInsight`
   - Ensure the feed is grouped according to acceptance criteria.
   - Ensure top-3 actions are derived from persisted insights or existing finance action logic in a way consistent with the story.
   - If the dashboard already has a composite query, extend it rather than creating fragmented calls unless the current architecture clearly prefers separate endpoints.

5. **Implement entity-scoped insight queries**
   - Add or update queries for invoice, bill, and payment detail pages to fetch insights filtered by the current entity reference.
   - Filtering must be exact and tenant-scoped.
   - Return only insights relevant to the current entity.
   - For entity detail pages, do not over-group unless the UX already expects grouping there; acceptance criteria only requires filtering to current entity reference.

6. **Update Blazor UI**
   - Dashboard:
     - render Financial Health panel
     - render top-3 finance actions section
     - render grouped insights feed with occurrence count badges where count > 1
   - Detail pages:
     - add/render an Agent Insights panel on invoice, bill, and payment pages
     - show empty state when no insights exist
   - Use shared components if it reduces duplication without overengineering.
   - Keep styling aligned with existing design system/components in the repo.

7. **Handle refresh visibility**
   - Ensure the UI reads from persisted data on page load/refresh and does not require manual repair or cache invalidation hacks.
   - If dashboard/detail queries are cached, verify cache invalidation or short TTL behavior after new financial events are processed.
   - If the app already has live update patterns, wire into them only if straightforward; otherwise ensure one browser refresh cycle is sufficient per acceptance criteria.
   - Avoid introducing stale client-side memoization that hides newly persisted insights.

8. **Add tests**
   - Add focused tests for:
     - grouped dashboard insights collapse duplicates into one item with occurrence count
     - unrelated insights remain separate
     - invoice detail page only shows invoice-linked insights
     - bill detail page only shows bill-linked insights
     - payment detail page only shows payment-linked insights
     - newly persisted insights appear in subsequent query results
   - Prefer application/API tests over brittle UI snapshot tests unless the repo already uses component tests.

9. **Keep implementation safe and minimal**
   - Do not refactor unrelated finance modules.
   - Do not invent new persistence concepts if existing fields can support the requirement.
   - If a missing grouping key makes correctness impossible, add the smallest viable persisted field/migration and document why.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Open the executive dashboard and confirm:
     - Financial Health panel is visible
     - top-3 finance actions section is visible
     - insights feed is populated from persisted `FinanceAgentInsight` records
   - Seed or use existing data with duplicate/related insights and confirm:
     - duplicates are shown once in the dashboard feed
     - occurrence count is displayed correctly
   - Open an invoice detail page and confirm Agent Insights only shows invoice-linked insights.
   - Open a bill detail page and confirm Agent Insights only shows bill-linked insights.
   - Open a payment detail page and confirm Agent Insights only shows payment-linked insights.
   - Process or simulate a new financial event, then verify updated insights appear after one refresh cycle or through any existing live update path.

4. If caching exists, explicitly verify:
   - no stale dashboard/detail data remains after new insight persistence
   - tenant scoping remains correct across all queries

5. Include in your final implementation notes:
   - what grouping key/heuristic was used
   - where plain-English rendering is centralized
   - any limitations or assumptions

# Risks and follow-ups
- **Risk: no stable grouping key exists**
  - If `FinanceAgentInsight` lacks a reliable dedup/grouping field, heuristic grouping may accidentally merge distinct conditions or fail to merge true duplicates.
  - Follow-up: introduce an explicit persisted condition/group key generated upstream.

- **Risk: dashboard caching delays visibility**
  - Existing cache layers may prevent “visible within one refresh cycle.”
  - Follow-up: add targeted cache invalidation or reduce TTL for finance insight queries.

- **Risk: inconsistent text rendering**
  - If raw insight text is rendered directly in multiple places, UX may become inconsistent.
  - Follow-up: consolidate all finance insight display text into a single projection/formatter.

- **Risk: entity reference formats vary**
  - Invoice/bill/payment references may not be normalized consistently across persisted insights.
  - Follow-up: normalize entity reference persistence and filtering contracts.

- **Risk: over-grouping related insights**
  - Grouping “related” insights too aggressively can hide important distinctions.
  - Follow-up: keep grouping conservative and document the rule set.

- **Risk: acceptance ambiguity around live updates**
  - If no live update infrastructure exists, satisfy the requirement via refresh-cycle correctness rather than adding unnecessary complexity.
  - Follow-up: consider SignalR/live query updates later if dashboard freshness becomes a broader product requirement.