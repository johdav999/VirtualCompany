# Goal
Implement deterministic finance intelligence heuristics for **TASK-29.3.1** in the .NET modular monolith so the system can:

- compute **7-day** and **30-day cash projections**
- rank **overdue invoices** by overdue days and amount, with concrete follow-up recommendations
- rank **bills due soon** by urgency using due date, amount, and cash impact, with pay-now vs delay recommendations
- guarantee that for a **seeded deterministic scenario**, repeated runs produce the **same rankings, severities, and recommendation texts**

This work supports **US-29.3 Cash, receivables, and payables intelligence with deterministic recommendations** and should fit the existing architecture principles:
- modular monolith
- CQRS-lite
- tenant-scoped application services
- deterministic, testable business logic
- no LLM dependency for these recommendations

# Scope
In scope:

- Add domain/application logic for short-horizon finance heuristics:
  - cash runway inputs and projection calculation
  - obligation coverage / near-term cash pressure assessment
  - overdue invoice ranking
  - due-soon bill ranking
  - deterministic recommendation generation
- Add deterministic seeded scenario fixtures/tests
- Expose the results through an internal application service/query handler and, if a suitable finance endpoint already exists, wire it there
- Ensure all logic is pure/deterministic and independent of wall-clock time except where an explicit `asOf` date is provided

Out of scope unless already trivially aligned with existing patterns:

- new UI/dashboard pages
- mobile changes
- LLM orchestration changes
- broad accounting integration work
- non-deterministic forecasting/ML models
- schema expansion beyond what is minimally required for current normalized finance records already present in the codebase

If the repo does not yet contain finance entities for cash, invoices, bills, and recurring outflows, implement the heuristic engine and tests first behind clear contracts, and add only the minimum persistence/API wiring needed to support deterministic execution.

# Files to touch
Inspect the solution structure first and then touch the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add value objects/enums/models for:
    - cash projection inputs/results
    - ranked receivable/payable items
    - severity/recommendation types
- `src/VirtualCompany.Application/**`
  - add query/handler/service for finance intelligence heuristics
  - add DTOs/contracts for returning projections and ranked recommendations
- `src/VirtualCompany.Infrastructure/**`
  - implement repository/data access needed to load:
    - current cash
    - open invoices
    - due bills
    - recurring outflows
  - keep tenant scoping explicit
- `src/VirtualCompany.Api/**`
  - add or extend endpoint/controller for retrieving the finance intelligence result if an appropriate module/route exists
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests if API surface is added/changed
- `tests/**` in relevant test projects
  - deterministic unit tests for ranking/projection heuristics
  - seeded scenario regression tests

Also review:
- `README.md`
- any existing finance/accounting modules
- any existing patterns for:
  - MediatR/CQRS handlers
  - repository interfaces
  - result DTOs
  - tenant-aware queries
  - deterministic clock abstractions

# Implementation plan
1. **Discover existing finance model and extension points**
   - Search the repo for existing concepts such as:
     - `Invoice`, `Bill`, `Cash`, `Receivable`, `Payable`, `Recurring`, `Projection`, `Finance`, `Accounting`
   - Reuse existing modules and naming conventions rather than inventing parallel structures.
   - Identify whether there is already:
     - a finance aggregate/query service
     - a dashboard query endpoint
     - a clock abstraction
     - seeded test data conventions

2. **Define deterministic input/output contracts**
   - Create clear domain/application contracts for a finance intelligence snapshot using an explicit `asOfDate`.
   - Inputs should include:
     - current cash balance
     - open invoices with issue/due dates, outstanding amount, customer name/reference, status
     - bills with due dates, amount, vendor/reference, status
     - recurring outflows with cadence/next due date/amount
   - Outputs should include:
     - 7-day projection
     - 30-day projection
     - obligation coverage summary
     - ranked overdue invoices
     - ranked due-soon bills
   - Include stable fields for:
     - `rank`
     - `severity`
     - `recommendationCode`
     - `recommendationText`

3. **Implement cash projection heuristics**
   - Build a pure calculation service in Domain or Application (prefer Domain service if the repo uses that pattern for business rules).
   - Projection rules should be deterministic and simple:
     - start from current cash
     - add expected inflows from currently open invoices that are reasonably collectible within the horizon according to explicit deterministic rules
     - subtract due bills within the horizon
     - subtract recurring outflows within the horizon
   - Do not use randomization, fuzzy text generation, or current system time.
   - Use explicit horizon windows:
     - 7 days
     - 30 days
   - Return both:
     - projected ending cash
     - supporting totals (starting cash, projected inflows, projected outflows)
   - If acceptance criteria only require projections, keep assumptions transparent in code comments and tests.

4. **Implement obligation coverage / cash pressure heuristics**
   - Add a deterministic assessment that interprets whether near-term obligations are covered by projected cash.
   - Suggested deterministic outputs:
     - `Healthy`
     - `Watch`
     - `AtRisk`
     - `Critical`
   - Base this on explicit thresholds such as:
     - projected cash below zero
     - low remaining buffer after due obligations
     - ratio of available cash to near-term obligations
   - Keep thresholds centralized in one place so tests can lock behavior.

5. **Implement overdue invoice ranking**
   - Rank overdue invoices using a stable deterministic sort:
     1. overdue days descending
     2. outstanding amount descending
     3. stable tie-breaker ascending (e.g. invoice ID/reference)
   - Compute severity from deterministic thresholds, for example:
     - severe overdue + high amount => higher severity
   - Generate concrete recommendation text from templates, not free-form generation.
   - Example recommendation patterns:
     - send reminder today
     - call customer and confirm payment date
     - escalate to owner/finance lead
   - Ensure recommendation text is fully deterministic from the same inputs.

6. **Implement due-soon bill ranking**
   - Rank bills due soon using a deterministic urgency score or ordered tuple based on:
     - due date proximity
     - amount
     - cash impact
   - Keep the ranking stable and explainable. Prefer explicit sort keys over opaque scoring if possible.
   - Example sort approach:
     1. due date ascending
     2. cash impact category descending
     3. amount descending
     4. stable tie-breaker ascending
   - Recommendation must be deterministic and concrete:
     - `Pay now` when due imminently and affordable/critical
     - `Delay` when cash pressure is high and the bill is less urgent according to explicit rules
   - Recommendation text should mention the reason in a templated way.

7. **Centralize recommendation templates**
   - Implement recommendation generation as code-based templates or lookup tables.
   - Do not build strings ad hoc in multiple places.
   - Ensure the same input classification always yields the same exact text.
   - Consider using:
     - enum/constant recommendation codes
     - a formatter that maps code + facts to exact text

8. **Add seeded deterministic scenario coverage**
   - Create a fixed scenario fixture with:
     - explicit `asOfDate`
     - fixed cash balance
     - multiple invoices with varied overdue days and amounts
     - multiple bills with varied due dates and amounts
     - recurring outflows
   - Add regression tests asserting exact:
     - 7-day projection values
     - 30-day projection values
     - overdue invoice order
     - bill urgency order
     - severities
     - recommendation texts
   - Run the same scenario multiple times in the same test or separate tests to prove repeatability.

9. **Wire through application layer**
   - Add a query/handler such as a finance intelligence snapshot query.
   - Ensure tenant scoping is enforced in repository calls.
   - Pass `asOfDate` explicitly; if omitted by caller, use an injected clock abstraction in the application layer, but tests should always provide a fixed date.

10. **Expose via API if appropriate**
   - If there is an existing finance/cockpit endpoint pattern, extend it.
   - Otherwise add a minimal read-only endpoint under an appropriate route.
   - Response should return structured data, not prose blobs.
   - Keep API contract stable and aligned with acceptance criteria.

11. **Document assumptions in code**
   - Add concise comments where heuristics make business assumptions, especially:
     - which invoices count toward projection
     - how recurring outflows are included
     - how severity thresholds are determined
     - how pay-now vs delay is decided

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Add and run focused unit tests for:
   - 7-day projection calculation
   - 30-day projection calculation
   - overdue invoice ranking stability
   - due-soon bill ranking stability
   - severity classification
   - recommendation text determinism

3. Add seeded regression tests asserting exact outputs:
   - exact projection totals
   - exact ranked item order
   - exact severity values
   - exact recommendation strings

4. If API is added/changed, verify:
   - tenant scoping behavior
   - stable JSON response shape
   - deterministic response for repeated identical requests

5. Confirm no hidden nondeterminism:
   - no `DateTime.UtcNow` inside core heuristic logic
   - no unordered iteration over dictionaries/sets affecting output order
   - no culture-sensitive formatting in recommendation text
   - no random/LLM-generated text

6. If persistence/repositories are involved, verify tie-break stability:
   - repository queries should use explicit ordering where needed
   - in-memory ranking should apply final stable tie-breakers

# Risks and follow-ups
- **Risk: finance data model may not yet exist**
  - Mitigation: implement the heuristic engine behind interfaces and add minimal adapters/fixtures first.
- **Risk: ambiguous projection assumptions**
  - Mitigation: encode assumptions explicitly and lock them with tests; avoid “smart” behavior not required by acceptance criteria.
- **Risk: nondeterministic ordering from DB or LINQ**
  - Mitigation: always apply explicit sort keys and final tie-breakers.
- **Risk: recommendation text drift**
  - Mitigation: centralize recommendation templates and assert exact strings in tests.
- **Risk: hidden time dependency**
  - Mitigation: require explicit `asOfDate` in core calculations and use clock abstraction only at the edge.

Follow-ups after this task, if not already covered elsewhere:
- surface results in executive cockpit widgets
- add audit/explainability references for why each recommendation was produced
- integrate with accounting connectors for real source data
- add configurable policy thresholds per company while preserving deterministic behavior