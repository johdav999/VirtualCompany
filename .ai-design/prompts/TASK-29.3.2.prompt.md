# Goal
Implement backlog task **TASK-29.3.2 — Build receivables aging and customer payment-pattern scoring for invoice prioritization** for story **US-29.3 Cash, receivables, and payables intelligence with deterministic recommendations**.

Deliver a deterministic finance intelligence capability in the existing .NET modular monolith that:

- Computes **7-day** and **30-day cash projections** from:
  - current cash
  - open invoices
  - due bills
  - recurring outflows
- Produces a **ranked overdue receivables list** using:
  - overdue days
  - invoice amount
  - customer payment-pattern scoring
- Produces a **ranked bills-due-soon list** using:
  - due date
  - amount
  - cash impact
- Attaches a **concrete recommendation** to each ranked item:
  - receivables: follow-up recommendation
  - payables: pay-now or delay recommendation
- Ensures that for a **seeded deterministic scenario**, repeated runs with the same inputs return the **same rankings, severities, and recommendation texts**

The implementation must fit the existing architecture:
- ASP.NET Core backend
- Application/Domain/Infrastructure layering
- PostgreSQL-backed persistence patterns where relevant
- CQRS-lite query/service style
- deterministic business logic only, no LLM dependency for scoring or recommendation text generation

# Scope
In scope:

- Add domain/application logic for:
  - receivables aging buckets and ranking
  - customer payment-pattern scoring
  - payable urgency ranking
  - 7-day and 30-day cash projection calculation
  - deterministic recommendation generation
- Add DTOs/query models for a finance intelligence result payload
- Add a deterministic seeded scenario fixture and tests
- Add or extend API endpoint(s) or application query handler(s) to expose the result
- Keep all recommendation text template-driven and deterministic
- Ensure stable sorting and tie-breakers

Out of scope unless already trivially supported by existing code patterns:

- UI redesign beyond minimal wiring needed to expose data
- LLM-generated recommendations
- probabilistic forecasting or ML models
- external accounting integrations
- broad schema redesign unrelated to this task
- mobile-specific work

Assumptions to validate in the codebase before implementation:

- There is either an existing finance/accounting module or a suitable place under Application/Domain for finance intelligence
- Existing entities/DTOs for invoices, bills, and cash balances may already exist; reuse them if present
- If no persistence model exists yet for this feature, implement the intelligence over deterministic in-memory seeded data and/or existing finance records, but keep interfaces ready for repository-backed data

# Files to touch
Inspect first, then update only the minimum necessary set. Likely candidates:

- `src/VirtualCompany.Domain/...`
  - add value objects/enums/models for:
    - receivables aging
    - payment-pattern score
    - payable urgency
    - cash projection inputs/results
    - recommendation severity/category
- `src/VirtualCompany.Application/...`
  - add:
    - finance intelligence query/handler or service
    - deterministic ranking/calculation service
    - result DTOs
    - interfaces for data access if needed
- `src/VirtualCompany.Infrastructure/...`
  - implement repository/data provider if the feature reads persisted finance data
  - optionally add seeded deterministic scenario provider for tests/dev
- `src/VirtualCompany.Api/...`
  - add or extend endpoint/controller for finance intelligence output
- `src/VirtualCompany.Shared/...`
  - shared contracts only if this solution already uses Shared for API-facing DTOs
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint/integration tests
- add or update unit test project(s) if present for Application/Domain logic
- `README.md` or feature docs only if there is an established pattern for documenting new endpoints/features

Do not create new top-level architectural patterns. Follow existing project conventions, namespaces, folder structure, and naming.

# Implementation plan
1. **Discover existing finance structures**
   - Search the solution for:
     - invoice, receivable, bill, payable, cash, projection, aging, finance, accounting
   - Identify:
     - existing entities and repositories
     - existing API route conventions
     - existing CQRS/query handler patterns
     - existing test style and fixture patterns
   - Reuse existing models where possible; only introduce new abstractions where needed

2. **Define deterministic domain rules**
   - Implement explicit, documented formulas with no randomness and no time-dependent ambiguity beyond an injected/reference date.
   - Use a supplied `asOfDate` for all calculations.
   - Normalize all date math to whole days.

   Required deterministic rules:

   **Receivables aging**
   - For each open invoice:
     - `overdueDays = max(0, asOfDate - dueDate in days)`
     - assign aging bucket, e.g.:
       - Current: `overdueDays <= 0`
       - 1-30
       - 31-60
       - 61-90
       - 91+
   - Only overdue invoices appear in overdue ranking.

   **Customer payment-pattern scoring**
   - Build a deterministic score from historical paid invoices for the same customer.
   - Prefer a simple bounded formula such as:
     - average days late
     - percent of invoices paid late
     - count of severe late payments
   - Example shape:
     - score 0-100 where higher means worse payment behavior
     - derive severity bands from score:
       - Low risk
       - Medium risk
       - High risk
       - Critical risk
   - If customer history is sparse, use deterministic fallback rules:
     - no history => neutral/default score
     - 1-2 invoices => reduced confidence but still deterministic score
   - Do not use floating-point instability if avoidable; prefer decimal/int rounding rules explicitly.

   **Overdue receivables ranking**
   - Rank by a deterministic composite priority using:
     1. overdue days severity
     2. amount severity
     3. payment-pattern score
   - Define exact tie-breakers, for example:
     1. higher overdue days first
     2. higher amount first
     3. worse payment-pattern score first
     4. earlier due date first
     5. invoice ID ascending
   - Every item must include:
     - invoice/customer identifiers
     - amount
     - due date
     - overdue days
     - aging bucket
     - payment-pattern score/severity
     - rank
     - recommendation text
     - recommendation severity

   **Receivables recommendation generation**
   - Use deterministic templates based on thresholds.
   - Example pattern:
     - very overdue + high amount + poor payer => “Call customer today and escalate to account owner; pause further credit if policy allows.”
     - moderate overdue => “Send reminder email today and schedule follow-up in 3 business days.”
   - Recommendation text must be selected from fixed templates, not assembled with nondeterministic phrasing.

   **Bills due soon ranking**
   - Include unpaid bills due within a defined horizon, likely 30 days, and optionally already overdue bills.
   - Compute urgency from:
     - due date proximity/overdue status
     - amount
     - cash impact against projected cash
   - Define exact tie-breakers, for example:
     1. more urgent severity first
     2. earlier due date first
     3. higher amount first
     4. bill ID ascending
   - Every item must include:
     - bill/vendor identifiers
     - amount
     - due date
     - days until due / overdue
     - urgency score/severity
     - cash impact classification
     - rank
     - recommendation text (`pay now` or `delay`)
     - rationale fields

   **Payables recommendation generation**
   - Deterministically choose:
     - `Pay now`
     - `Delay`
   - Base on:
     - due date urgency
     - amount
     - whether paying now would materially worsen 7-day or 30-day cash position
   - Use fixed recommendation templates.

   **Cash projections**
   - Compute:
     - starting cash
     - expected inflows from open invoices within 7/30 days
     - expected outflows from due bills within 7/30 days
     - recurring outflows within 7/30 days
   - Return:
     - projected cash at 7 days
     - projected cash at 30 days
     - component breakdowns
   - Keep invoice inflow assumptions deterministic:
     - either due-date based
     - or due-date adjusted by customer payment-pattern expectation
   - If using payment-pattern adjustment, document exact formula and keep it deterministic.

3. **Model the result contract**
   - Create a single response model for the finance intelligence query/endpoint, likely containing:
     - `AsOfDate`
     - `CurrentCash`
     - `Projection7Day`
     - `Projection30Day`
     - `OverdueReceivables`
     - `BillsDueSoon`
     - optional summary counts/totals
   - Include enough structured fields for tests to assert exact outputs.
   - Avoid returning only free text.

4. **Implement application service/query handler**
   - Add a query/handler or service such as:
     - `GetCashFlowPrioritizationQuery`
     - `GetReceivablesAndPayablesIntelligenceQuery`
     - or equivalent matching project conventions
   - Responsibilities:
     - load finance inputs
     - call deterministic calculator/ranker
     - map to DTO
   - Keep business rules in Domain/Application services, not controllers.

5. **Implement deterministic calculators**
   - Create focused services/classes, for example:
     - `CashProjectionCalculator`
     - `ReceivablesAgingService`
     - `CustomerPaymentPatternScorer`
     - `InvoicePrioritizationService`
     - `BillUrgencyScoringService`
   - Ensure:
     - no `DateTime.UtcNow` inside core logic; inject/reference `asOfDate`
     - no random values
     - explicit decimal rounding
     - stable ordering with final deterministic tie-breakers

6. **Wire data access**
   - If finance entities already exist:
     - query current cash, open invoices, bills, recurring outflows from existing repositories/db context
   - If they do not exist:
     - create a narrow abstraction for input data retrieval
     - provide a deterministic seeded provider for tests
   - Do not overbuild persistence if the task can be satisfied with existing structures plus test fixtures

7. **Expose via API**
   - Add or extend an API endpoint following existing route conventions.
   - Suggested shape:
     - `GET /api/companies/{companyId}/finance/intelligence/priorities?asOfDate=YYYY-MM-DD`
   - Ensure tenant/company scoping follows existing authorization patterns.
   - Return structured JSON with rankings and projections.

8. **Add deterministic seeded scenario**
   - Create a fixed scenario with:
     - current cash
     - multiple open invoices across customers
     - historical paid invoices for payment-pattern scoring
     - multiple due bills
     - recurring outflows
   - Include edge cases:
     - same overdue days with different amounts
     - same amount with different payment histories
     - sparse customer history
     - bills where paying now harms short-term cash
   - Use this scenario in tests to assert exact:
     - ranking order
     - severity labels
     - recommendation texts
     - 7-day and 30-day projections

9. **Test thoroughly**
   - Add unit tests for:
     - aging bucket assignment
     - payment-pattern scoring
     - invoice ranking tie-breakers
     - bill urgency ranking tie-breakers
     - recommendation text selection
     - cash projection math
   - Add integration/API tests for:
     - endpoint returns expected deterministic payload
     - repeated calls with same seeded inputs return identical outputs
   - Prefer exact assertions over loose assertions for deterministic acceptance criteria

10. **Document formulas in code**
   - Add concise comments near scoring formulas and thresholds.
   - Make thresholds/constants centralized and named.
   - Avoid magic numbers scattered across files.

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Run targeted tests for the new feature if separate filters/projects exist.

3. Verify acceptance criteria explicitly:

   **Cash projections**
   - Assert 7-day projection equals expected deterministic value for seeded scenario
   - Assert 30-day projection equals expected deterministic value for seeded scenario
   - Assert breakdown includes current cash, invoice inflows, due bills, recurring outflows

   **Overdue invoice ranking**
   - Assert only overdue invoices are included
   - Assert ranking order matches expected order for seeded scenario
   - Assert each item includes:
     - overdue days
     - amount
     - recommendation text
   - Assert recommendation text is concrete and non-empty

   **Bills due soon ranking**
   - Assert due-soon bills are included and ranked by urgency
   - Assert each item includes:
     - urgency/severity
     - amount
     - due date
     - pay-now or delay recommendation
   - Assert recommendation text matches expected deterministic template

   **Determinism**
   - Execute the same query/endpoint multiple times with the same seeded inputs
   - Assert:
     - identical ordering
     - identical severity labels
     - identical recommendation texts
     - identical projection totals

4. If an API endpoint is added, validate response contract manually or via tests:
   - status code
   - tenant/company scoping
   - stable JSON payload shape

5. Ensure no core logic depends on wall-clock time:
   - search for `DateTime.UtcNow`, `DateTime.Now`, `Guid.NewGuid()` in the new ranking/scoring path
   - replace with injected/reference values where needed

# Risks and follow-ups
- **Missing finance persistence models**
  - Risk: the codebase may not yet have invoice/bill/cash entities.
  - Mitigation: implement behind a narrow input provider interface and use seeded deterministic fixtures/tests now.

- **Ambiguous business formulas**
  - Risk: acceptance criteria require deterministic outputs but do not prescribe exact formulas.
  - Mitigation: choose simple, explicit formulas and encode them centrally with tests documenting expected