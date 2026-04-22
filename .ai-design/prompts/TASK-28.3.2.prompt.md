# Goal
Implement backlog task **TASK-28.3.2** for story **US-28.3 Expose derived operational finance state for dashboards, agents, and debug workflows** by adding application-layer finance summary queries and/or API endpoints that expose point-in-time derived finance metrics for a company.

The implementation must provide queryable metrics for:
- current cash
- accounts receivable
- overdue receivables
- accounts payable
- overdue payables
- monthly revenue
- monthly costs
- recent asset purchases

The solution must be tenant-scoped, deterministic for repeated reads against the same simulation state, and performant for seeded companies in local and test environments.

# Scope
In scope:
- Discover the existing finance/simulation domain model, persistence model, and current query/API patterns.
- Add an application query contract and handler(s) for point-in-time finance summaries.
- Add API endpoint(s) exposing these summaries for:
  - dashboard consumption
  - agent context consumption
  - simulation debug workflows
- Ensure derived metrics are computed from the underlying simulated document, payment, and cash state for the same company and point in time.
- Ensure repeated reads against unchanged simulation state return consistent results.
- Add tests covering correctness, tenant scoping, determinism, and basic performance-oriented behavior.
- Reuse existing CQRS-lite, authorization, and tenant resolution patterns already present in the solution.

Out of scope:
- Large UI/dashboard rendering changes unless required to wire an endpoint.
- Reworking the simulation engine or changing source-of-truth finance state semantics unless necessary to fix query correctness.
- Introducing new infrastructure components.
- Premature caching unless needed and aligned with existing architecture patterns.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Application/**`
  - add finance summary query DTOs/contracts
  - add query handler(s)
  - add mapping/result models
- `src/VirtualCompany.Api/**`
  - add controller or minimal API endpoints for finance summaries
  - wire request/response contracts if API-specific models are used
- `src/VirtualCompany.Infrastructure/**`
  - add repository/query service implementations if application handlers need optimized SQL/EF access
  - add any read-model query helpers
- `src/VirtualCompany.Domain/**`
  - only if a shared value object or domain-level finance summary abstraction is clearly warranted
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- potentially `tests/**` for application/infrastructure tests if those projects already exist
- `README.md` or relevant docs only if there is an established API/query documentation pattern

Also inspect for existing related files before creating new ones:
- finance simulation entities/services
- dashboard query handlers
- agent context query handlers
- debug/simulation endpoints
- tenant-scoped query abstractions
- existing result envelope patterns
- existing test fixtures/seed data helpers

# Implementation plan
1. **Discover existing architecture and finance simulation model**
   - Inspect the solution structure to find:
     - current CQRS/query patterns in `Application`
     - API endpoint conventions in `Api`
     - finance/simulation entities in `Domain`/`Infrastructure`
     - tenant resolution and authorization patterns
     - any existing dashboard or debug query endpoints
   - Identify the authoritative source tables/entities for:
     - cash state
     - invoices/receivables
     - bills/payables
     - payments
     - asset purchases
     - simulation clock or point-in-time state
   - Determine whether the system already models “as-of” reads or whether summaries should be based on the current persisted simulation snapshot.

2. **Define a stable application query contract**
   - Add a query and result model for a finance summary, likely something like:
     - `GetFinanceSummaryQuery`
     - `FinanceSummaryDto`
   - Include fields for:
     - `CompanyId`
     - optional simulation state identifier and/or `AsOfUtc` if supported by the existing model
     - summary metrics listed in acceptance criteria
     - metadata useful for determinism/debugging, such as:
       - effective timestamp/state version
       - currency if available
   - Keep the contract application-focused and reusable by dashboard, agent, and debug callers.

3. **Implement deterministic finance summary derivation**
   - Build a query handler or read service that derives metrics from the underlying simulated state.
   - Use the existing persistence/query stack already used elsewhere in the repo.
   - Ensure calculations are explicit and testable. For example:
     - current cash = current simulated cash balance at the requested point in time
     - accounts receivable = unpaid receivable-side obligations due or not yet due
     - overdue receivables = unpaid receivables with due date before the effective point in time
     - accounts payable = unpaid payable-side obligations due or not yet due
     - overdue payables = unpaid payables with due date before the effective point in time
     - monthly revenue = revenue recognized/issued/posted according to existing simulation semantics within the effective month
     - monthly costs = costs recognized/issued/posted according to existing simulation semantics within the effective month
     - recent asset purchases = recent qualifying asset purchase records, likely capped and ordered descending
   - Do not invent accounting semantics if the codebase already defines them; align with existing simulation rules and naming.

4. **Preserve point-in-time consistency**
   - If the simulation model has a state version, tick, snapshot ID, or effective timestamp, anchor the query to it.
   - Ensure repeated reads against the same state produce the same result:
     - avoid non-deterministic ordering
     - use explicit ordering for recent asset purchases
     - avoid “now” inside handlers unless the requested/current simulation time is explicitly resolved once and reused
   - If needed, resolve the effective simulation state first, then compute all metrics against that same anchor.

5. **Expose API endpoints for the three consumers**
   - Add endpoint(s) under existing API conventions, ideally one reusable endpoint with optional consumer-specific route aliases only if needed.
   - Candidate shape:
     - dashboard endpoint for current finance summary
     - agent-context endpoint returning the same or a subset/superset contract
     - debug endpoint allowing explicit state/as-of selection if such capability exists
   - Reuse the same application query handler to avoid divergence.
   - Enforce tenant scoping and authorization using existing patterns.
   - Return concise, stable response payloads.

6. **Optimize query path within reasonable service thresholds**
   - Prefer a single efficient query flow or a small bounded number of queries.
   - Push aggregation to SQL/EF where appropriate.
   - Avoid N+1 access for recent asset purchases or document/payment joins.
   - If the repo already uses compiled queries, projections, or read services, follow that pattern.
   - Only add caching if:
     - there is an established cache pattern already used for dashboard aggregates, and
     - it does not compromise point-in-time consistency semantics.

7. **Add tests**
   - Add integration-style tests that validate:
     - correct metrics for seeded/simulated company data
     - overdue vs non-overdue separation
     - monthly revenue/cost windows
     - recent asset purchase ordering and limits
     - tenant isolation
     - repeated reads against unchanged state return identical results
   - Add API tests for endpoint shape/status codes.
   - If practical, add a test that exercises seeded-company query performance indirectly by ensuring no pathological query explosion; do not create flaky micro-benchmarks.

8. **Document assumptions in code**
   - Where finance semantics are inferred from existing entities, leave concise comments in the handler/query service.
   - If there is ambiguity in “monthly revenue” or “monthly costs,” align to existing simulation semantics and make that explicit in naming/comments.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify endpoint behavior manually or via tests:
   - finance summary endpoint returns all required metrics
   - dashboard/agent/debug routes all resolve correctly if multiple routes are added
   - unauthorized cross-tenant access is rejected

4. Validate correctness against seeded/simulated data:
   - compare returned summary values to underlying documents, payments, and cash records for the same company/state
   - verify overdue buckets change correctly relative to due dates and effective simulation time
   - verify monthly revenue/costs are scoped to the intended month anchor

5. Validate determinism:
   - call the same query/endpoint multiple times against unchanged simulation state
   - confirm identical values and stable ordering for recent asset purchases

6. Validate basic performance in local/test conditions:
   - ensure seeded-company queries complete within normal interactive thresholds
   - inspect logs/query count if available for obvious inefficiencies

# Risks and follow-ups
- **Finance semantic ambiguity:** The repo may not clearly define whether revenue/costs are based on issued documents, recognized postings, or settled payments. Follow existing simulation semantics rather than inventing new ones.
- **Point-in-time model gaps:** If the current simulation state lacks a clear snapshot/version/as-of anchor, determinism may require a small supporting abstraction.
- **Performance risk:** Naive aggregation across documents and payments may be too slow for seeded companies; be prepared to consolidate queries or add targeted projections.
- **Consumer contract drift:** Dashboard, agent context, and debug consumers may want slightly different shapes. Prefer one shared core DTO and adapt only at the API boundary if necessary.
- **Recent asset purchase definition:** Confirm what qualifies as an asset purchase in the existing model before implementing filters.
- **Follow-up candidates:** If this task exposes repeated expensive reads, a later task could add cached read models or materialized summaries keyed by simulation state/version.