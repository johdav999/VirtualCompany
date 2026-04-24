# Goal
Implement `TASK-29.3.3` for **US-29.3 Cash, receivables, and payables intelligence with deterministic recommendations** by adding deterministic **payables urgency scoring** and the supporting cash intelligence logic needed to satisfy the acceptance criteria.

The implementation must:
- Compute **7-day** and **30-day cash projections** from:
  - current cash
  - open invoices
  - due bills
  - recurring outflows
- Rank **overdue invoices** by overdue days and amount, with a concrete follow-up recommendation per item
- Rank **bills due soon** by urgency using:
  - due date
  - amount
  - vendor criticality heuristic
  - cash pressure
- Produce a **pay-now** or **delay** recommendation per payable item
- Be **fully deterministic** for seeded scenarios:
  - same inputs
  - same rankings
  - same severities
  - same recommendation texts
  - repeated runs yield identical outputs

Use the existing .NET modular monolith conventions and keep the implementation inside the appropriate Domain/Application/Infrastructure/API boundaries. Prefer deterministic business logic over any LLM dependency.

# Scope
In scope:
- Add or extend domain models/value objects/services for:
  - cash projection inputs and outputs
  - overdue receivables ranking
  - payable urgency scoring
  - recommendation generation
- Add application-layer query/service orchestration for the cash intelligence result
- Add deterministic heuristics for:
  - vendor criticality
  - cash pressure
  - severity bands
  - recommendation text generation
- Add persistence/query support if needed to read:
  - current cash
  - open invoices
  - due bills
  - recurring outflows
- Add API contract/endpoint updates if this intelligence is exposed via HTTP
- Add seeded test fixtures and automated tests proving deterministic repeated output
- Keep all recommendation text template-driven and deterministic

Out of scope:
- Any non-deterministic LLM-generated recommendation text
- UI polish beyond minimal contract compatibility
- Mobile-specific work
- Broad accounting integration work beyond what is required for this task
- Re-architecting unrelated finance modules

# Files to touch
Inspect the solution first and then touch only the relevant files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - finance/cash intelligence domain models
  - scoring services
  - recommendation policy classes
- `src/VirtualCompany.Application/**`
  - query handlers / application services
  - DTOs / response models
  - deterministic scenario seed support if located here
- `src/VirtualCompany.Infrastructure/**`
  - repositories / read models / SQL access
  - seed data or fixture loading support if applicable
- `src/VirtualCompany.Api/**`
  - endpoint/controller wiring
  - request/response contracts if exposed externally
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for finance intelligence responses
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for response determinism and ranking output
- Potentially other test projects if present for Domain/Application layers

Before coding, locate existing finance-related code and prefer extending it rather than creating parallel structures.

# Implementation plan
1. **Discover existing finance intelligence structure**
   - Search for existing modules/entities/services related to:
     - cash
     - invoices
     - bills
     - payables
     - receivables
     - forecasting
     - recommendations
   - Identify current architectural patterns:
     - MediatR/CQRS handlers
     - repository abstractions
     - endpoint style
     - DTO naming
     - test fixture conventions
   - Reuse existing namespaces and patterns.

2. **Define deterministic domain contract**
   - Introduce or refine domain models for:
     - `CashProjectionWindow` / `CashProjectionResult`
     - `OverdueInvoiceRankedItem`
     - `PayableUrgencyRankedItem`
     - `CashIntelligenceSnapshot` or equivalent aggregate result
   - Include explicit fields for:
     - rank
     - score
     - severity
     - recommendation type
     - recommendation text
     - explanation factors used
   - Ensure stable ordering with deterministic tie-breakers, e.g.:
     - primary score desc
     - due date asc / overdue days desc
     - amount desc
     - stable entity id asc

3. **Implement 7-day and 30-day cash projection logic**
   - Build deterministic projection logic using:
     - starting current cash
     - expected inflows from open invoices within window
     - expected outflows from due bills within window
     - recurring outflows within window
   - Make assumptions explicit in code and tests:
     - whether overdue invoices count as collectible in projection
     - whether only due-date-within-window items are included
     - whether recurring outflows are expanded into dated occurrences
   - Return both:
     - net projected cash
     - contributing totals by category for explainability

4. **Implement overdue invoice ranking**
   - Score overdue invoices using deterministic weighted factors:
     - overdue days
     - amount
   - Map score to severity bands such as:
     - low
     - medium
     - high
     - critical
   - Generate concrete follow-up recommendations from templates, for example based on overdue age and amount:
     - send reminder today
     - call customer today
     - escalate to account owner
     - pause further work / review credit hold
   - Ensure recommendation text is rule-based and identical across runs.

5. **Implement payable urgency scoring**
   - Score bills due soon using deterministic weighted factors:
     - due date proximity
     - amount
     - vendor criticality heuristic
     - cash pressure
   - Suggested heuristic structure:
     - **Due date factor**: overdue/imminent bills score higher than later bills
     - **Amount factor**: normalize against scenario totals or fixed thresholds; keep deterministic
     - **Vendor criticality heuristic**: derive from deterministic attributes such as:
       - recurring vendor
       - essential category if available
       - payroll/tax/rent/utilities/software/infrastructure priority buckets
       - fallback default when metadata is missing
     - **Cash pressure**: derive from projected cash after near-term obligations; higher pressure can either:
       - increase urgency for critical bills
       - decrease pay-now recommendation for non-critical bills
   - Separate:
     - **urgency score**
     - **recommended action** (`pay_now` vs `delay`)
   - Recommendation policy should be deterministic and explainable:
     - pay now if critical and affordable / due immediately
     - delay if low criticality and cash pressure is high
   - Include rationale fields listing the exact factors used.

6. **Create deterministic recommendation text policy**
   - Centralize recommendation text generation in a single policy/service.
   - Do not build text from unordered collections.
   - Use fixed templates keyed by:
     - severity
     - action
     - factor bands
   - Example outputs:
     - “Pay now: bill is due within 3 days, vendor is operationally critical, and projected 7-day cash remains positive.”
     - “Delay 7 days: vendor criticality is low and projected 7-day cash is constrained.”
   - Ensure punctuation, casing, and factor ordering are fixed.

7. **Wire application-layer query/service**
   - Add or update an application query/handler that:
     - loads finance inputs
     - invokes deterministic domain services
     - returns a single response containing:
       - 7-day projection
       - 30-day projection
       - overdue invoice ranking
       - bills due soon ranking
   - Keep orchestration in Application and scoring logic in Domain.

8. **Add repository/query support**
   - If needed, implement read-side access for:
     - current cash balance
     - open invoices
     - due bills
     - recurring outflows
   - Preserve tenant scoping.
   - Keep data access deterministic:
     - explicit ordering in SQL/LINQ
     - avoid reliance on database default ordering

9. **Expose via API**
   - If an endpoint already exists, extend it.
   - Otherwise add a finance intelligence endpoint consistent with current API conventions.
   - Response should include all fields needed by acceptance criteria and tests.

10. **Add seeded deterministic scenario**
   - Create a fixed scenario fixture with known:
     - current cash
     - invoices
     - bills
     - recurring outflows
     - vendor categories/criticality inputs
   - Use fixed dates via injected clock or explicit scenario reference date.
   - Avoid `DateTime.UtcNow` directly in scoring logic.
   - Ensure repeated runs against the same seed produce byte-for-byte equivalent relevant outputs where practical.

11. **Add automated tests**
   - Domain tests:
     - cash projection math
     - overdue ranking order
     - payable urgency scoring
     - recommendation text mapping
     - tie-break determinism
   - Application/API tests:
     - seeded scenario returns expected rankings
     - repeated calls return identical severities and recommendation texts
     - tenant scoping remains intact if applicable
   - Include edge cases:
     - no bills
     - no invoices
     - overdue and due-today items
     - equal scores requiring tie-breakers
     - missing vendor metadata using fallback heuristic

12. **Keep implementation auditable and maintainable**
   - Add concise comments where business rules are non-obvious.
   - Prefer named constants/config objects for thresholds and weights.
   - Do not hide scoring rules in magic numbers without explanation.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run all tests:
   - `dotnet test`

3. Run targeted tests for the new finance intelligence behavior:
   - domain/application/api tests added for this task

4. Verify acceptance criteria explicitly:
   - Confirm 7-day and 30-day projections include:
     - current cash
     - open invoices
     - due bills
     - recurring outflows
   - Confirm overdue invoices are ranked by deterministic score based on overdue days and amount
   - Confirm each overdue invoice includes a concrete follow-up recommendation
   - Confirm bills due soon are ranked by urgency using:
     - due date
     - amount
     - vendor criticality heuristic
     - cash pressure
   - Confirm each bill includes a deterministic pay-now or delay recommendation
   - Confirm repeated execution of the seeded scenario returns identical:
     - ranking order
     - severity values
     - recommendation text

5. If an API endpoint is added/updated, validate response shape manually or via tests:
   - stable JSON ordering is not required, but semantic values must be identical
   - no null/missing fields required by consumers

6. Check for determinism hazards:
   - no direct `UtcNow` in scoring logic
   - no unordered dictionary/set iteration affecting text
   - no random values
   - explicit sort tie-breakers everywhere rankings are produced

# Risks and follow-ups
- **Risk: unclear existing finance model**
  - The repo may not yet have canonical invoice/bill/cash entities.
  - Mitigation: inspect first and align with existing abstractions rather than inventing broad new ones.

- **Risk: vendor criticality data may be sparse**
  - If vendor metadata is limited, heuristic quality may be constrained.
  - Mitigation: implement a deterministic fallback based on category/recurrence/default priority and document assumptions in code/tests.

- **Risk: projection assumptions may be ambiguous**
  - For example, whether overdue invoices count as expected inflows.
  - Mitigation: encode explicit rules and cover them in tests so behavior is stable and reviewable.

- **Risk: hidden non-determinism**
  - Current code may use system time or implicit ordering.
  - Mitigation: inject a clock/reference date and enforce explicit ordering in all queries and ranking pipelines.

- **Risk: recommendation text drift**
  - If text is assembled ad hoc in multiple places, repeated outputs may diverge.
  - Mitigation: centralize text generation in one deterministic policy.

Follow-ups after implementation:
- Externalize scoring weights and thresholds into validated configuration if product wants tunable heuristics later
- Add audit/explainability persistence for factor-level scoring details if not already present
- Extend vendor criticality with richer accounting/integration metadata in future tasks
- Add UI surfacing for factor explanations and recommendation rationale if not already implemented