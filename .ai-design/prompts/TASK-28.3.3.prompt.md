# Goal
Implement backlog task **TASK-28.3.3 — Add consistency checks validating projection totals against source simulation records** for story **US-28.3 Expose derived operational finance state for dashboards, agents, and debug workflows**.

The coding task is to add automated consistency validation around the finance summary/projection pipeline so that derived point-in-time finance metrics can be checked against the underlying simulation source records for the same company and timestamp.

The implementation must support and verify these outcomes:

- Queryable finance metrics exist for:
  - current cash
  - accounts receivable
  - overdue receivables
  - accounts payable
  - overdue payables
  - monthly revenue
  - monthly costs
  - recent asset purchases
- Derived metrics match the underlying simulated document, payment, and cash state.
- Repeated reads against the same simulation state return consistent results.
- Queries remain performant for seeded companies in local and test environments.

Produce production code plus tests. Prefer minimal, well-factored changes aligned with the existing modular monolith and CQRS-lite architecture.

# Scope
In scope:

- Locate the existing finance summary/projection query path and simulation data model.
- Add a consistency-checking component that recomputes or validates derived totals from source simulation records.
- Integrate the checks into the appropriate application/service/test layer without introducing UI-only logic.
- Add deterministic tests covering:
  - metric correctness against source records
  - repeated-read consistency for same simulation state
  - seeded/local-test performance guardrails where practical
- Add any small supporting domain/application DTOs, result objects, or internal diagnostics needed for validation.
- If there is already a finance summary query handler/service, extend it rather than duplicating logic.
- Keep tenant/company scoping enforced.

Out of scope unless required by existing code patterns:

- Large UI/dashboard changes
- New external dependencies unless already standard in the solution
- Broad refactors unrelated to finance summary consistency
- Premature optimization beyond acceptance criteria
- Changing architecture away from current .NET modular monolith structure

# Files to touch
Start by inspecting and then update only the files needed. Likely areas include:

- `src/VirtualCompany.Application/**`
  - finance summary query handlers/services
  - simulation read models
  - validation/checking services
  - DTOs/results for finance summary and consistency diagnostics
- `src/VirtualCompany.Domain/**`
  - finance/simulation domain types if invariants belong there
- `src/VirtualCompany.Infrastructure/**`
  - repositories/query implementations
  - SQL/EF/Dapper projections
  - seeded data query support
- `src/VirtualCompany.Api/**`
  - only if an API contract must expose validation metadata or diagnostics
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for finance summary correctness and consistency
- Potentially other test projects if finance application tests exist elsewhere in the solution

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- solution and project files only if needed for wiring tests or new files

Do not invent file paths blindly. First discover the actual finance/simulation-related files and follow existing naming and folder conventions.

# Implementation plan
1. **Discover the existing implementation**
   - Search the solution for:
     - finance summary
     - projection
     - simulation
     - receivable / payable
     - cash
     - revenue / costs
     - asset purchases
   - Identify:
     - the current query entry point
     - source simulation entities/tables
     - any seeded company fixtures
     - existing test patterns and performance assertions

2. **Map source-of-truth rules**
   - Determine how each metric should be derived from simulation records at a point in time:
     - `current cash`
     - `accounts receivable`
     - `overdue receivables`
     - `accounts payable`
     - `overdue payables`
     - `monthly revenue`
     - `monthly costs`
     - `recent asset purchases`
   - Document the exact inclusion/exclusion rules in code comments where non-obvious.
   - Ensure all calculations are scoped by:
     - `company_id`
     - point-in-time timestamp/state
     - any relevant status filters

3. **Add a consistency validation component**
   - Implement an internal validator/service that compares:
     - the derived finance summary/projection totals
     - independently aggregated totals from underlying simulation records
   - Keep it deterministic and side-effect free.
   - Prefer a shape like:
     - `FinanceSummaryConsistencyChecker`
     - `FinanceSummaryConsistencyResult`
     - per-metric expected vs actual comparisons
   - Include enough detail for tests/debugging, but do not expose raw internals publicly unless existing API patterns support diagnostics.

4. **Integrate with the finance summary query path**
   - If appropriate, invoke the checker:
     - in tests only, or
     - behind an internal/debug option, or
     - as part of application-layer validation for debug workflows
   - Do not degrade normal query performance unnecessarily.
   - If acceptance criteria imply queryable/debuggable consistency, prefer an internal application service method that can be exercised by tests and debug workflows.

5. **Guarantee repeated-read consistency**
   - Ensure the point-in-time query uses a stable simulation snapshot/input.
   - If current code risks inconsistent repeated reads due to unordered queries, time drift, or mutable “now” usage:
     - inject/parameterize the effective timestamp
     - enforce deterministic ordering
     - avoid multiple inconsistent reads of changing state within one request
   - Add tests that call the same query repeatedly for the same company and simulation state and assert identical results.

6. **Add correctness tests**
   - Create or extend integration tests that seed representative simulation data and assert:
     - finance summary values equal expected source-derived totals
     - overdue buckets are calculated correctly
     - monthly revenue/cost windows are correct
     - recent asset purchases are correctly included/excluded
   - Include edge cases:
     - no records
     - fully paid vs unpaid
     - overdue vs not overdue
     - mixed statuses
     - boundary dates around month cutoffs
     - asset purchase recency boundary

7. **Add consistency-check tests**
   - Add tests that explicitly compare summary output to independent source aggregation.
   - If possible, structure tests so the independent aggregation logic is not just reusing the same implementation path verbatim.
   - Assert zero mismatches for seeded scenarios.

8. **Add performance-oriented validation**
   - For seeded companies in local/test environments, add a lightweight performance assertion if the test suite already supports it.
   - If strict timing assertions are too flaky, at minimum:
     - measure execution
     - log/assert reasonable upper bounds only where stable
     - avoid N+1 query patterns
   - Prefer query consolidation and indexed filtering over in-memory post-processing when possible.

9. **Keep architecture aligned**
   - Respect modular boundaries:
     - domain invariants in Domain
     - orchestration/query logic in Application
     - persistence/query implementation in Infrastructure
   - Keep tenant isolation and CQRS-lite patterns intact.
   - Avoid direct DB access from controllers/UI.

10. **Final quality pass**
   - Ensure naming is clear and business-oriented.
   - Remove dead code and temporary diagnostics.
   - Keep comments concise and useful.
   - Make sure all new tests pass.

# Validation steps
Run and report the results of the relevant commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run targeted tests first if you add or identify a focused test project:
   - `dotnet test --filter Finance`
   - If no filterable naming exists, run the relevant test project directly.

3. Run the main automated tests:
   - `dotnet test`

4. Validate the implemented behavior specifically:
   - Confirm finance summary metrics are returned for the required categories.
   - Confirm consistency checker reports no mismatches for valid seeded scenarios.
   - Confirm repeated reads for the same company + simulation timestamp/state return identical results.
   - Confirm query execution remains within acceptable local/test thresholds, or document measured timings if hard assertions are not stable.

In your final coding report, include:

- files changed
- summary of derivation/consistency rules implemented
- tests added/updated
- any assumptions made about simulation record semantics
- any performance observations or follow-up recommendations

# Risks and follow-ups
- The existing code may not yet have a single canonical finance summary query path; if multiple paths exist, consolidate carefully or validate the primary one first.
- Source simulation semantics may be ambiguous, especially around:
  - overdue determination
  - monthly window boundaries
  - partial payments
  - asset purchase classification
- Reusing the exact same aggregation logic for both “actual” and “expected” values weakens the tests; keep at least one independent verification path.
- Performance assertions can be flaky in CI; prefer stable thresholds and document measured timings if needed.
- If there is no current debug/diagnostic surface for consistency results, implement the checker as an internal application service and leave API exposure as a follow-up.
- If query consistency currently depends on ambient system time, a follow-up may be needed to standardize clock injection across finance queries.
- If seeded data is insufficient to cover all acceptance criteria, add or extend deterministic fixtures as a follow-up-compatible improvement.