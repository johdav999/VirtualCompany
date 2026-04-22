# Goal
Implement deterministic regression tests for finance summary projections used by both UI and agent flows for backlog task **TASK-28.4.3**.

The coding agent should add automated test coverage that proves the finance projection/simulation pipeline produces stable, replayable outputs and correct derived finance summaries for key cause-effect scenarios:
- invoice issuance increases receivables
- invoice payment increases cash and reduces receivables
- bill receipt increases payables
- bill payment decreases cash and reduces payables
- overdue invoice and overdue bill states emerge from due dates + unpaid balances after time progression
- recurring costs and asset purchases produce expected payable or cash effects
- identical seed + profile + start date produce identical event timelines and derived summaries

Do not redesign the finance domain unless required. Prefer extending existing test fixtures/helpers and only make minimal production changes needed to expose deterministic, testable seams.

# Scope
In scope:
- Discover the existing finance projection/simulation and summary generation code paths used by UI and agents.
- Add regression/integration tests around those code paths.
- Add or reuse deterministic test fixtures for seed/profile/start-date driven generation.
- Assert both event timeline behavior and derived summary outputs where acceptance criteria require it.
- Make minimal production changes only if necessary to:
  - inject/fix deterministic clock/random behavior
  - expose stable summary DTOs/models for assertions
  - remove flaky ordering/non-deterministic behavior in projection outputs

Out of scope:
- New finance features beyond what is needed for tests
- UI changes unless a shared contract must be exposed for testability
- Broad refactors of unrelated modules
- Snapshot/golden-file infrastructure unless the repo already uses it and it is the cleanest fit

# Files to touch
Start by locating the actual finance projection implementation and tests, then update the smallest relevant set of files.

Likely areas:
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `tests/VirtualCompany.Api.Tests/**`

Also inspect:
- existing finance-related services, handlers, DTOs, query models, and test fixtures
- any seed/profile/start-date simulation generators
- any existing deterministic/random abstraction, clock abstraction, or replay helpers

If needed, add new test files under a finance-focused folder in:
- `tests/VirtualCompany.Api.Tests/**`

Possible file patterns to search for:
- `*Finance*`
- `*Projection*`
- `*Summary*`
- `*Simulation*`
- `*Forecast*`
- `*Receivable*`
- `*Payable*`
- `*Invoice*`
- `*Bill*`
- `*Recurring*`
- `*Asset*`
- `*Seed*`
- `*Profile*`

# Implementation plan
1. Inspect the codebase to identify the exact production path that computes finance summaries used by UI and agents.
   - Find the query/service/handler that returns finance summary outputs.
   - Find where event timelines are generated from seed/profile/start date.
   - Confirm whether tests should target application-layer services, API endpoints, or both.
   - Prefer testing the shared application/service layer if that is the common source for UI and agents.

2. Identify existing deterministic seams.
   - Look for abstractions around time (`IClock`, `TimeProvider`, etc.).
   - Look for seeded random generation (`Random(seed)`, custom RNG abstraction, profile generators).
   - Look for ordering guarantees on returned events/summaries.
   - If determinism is currently implicit or flaky, add the smallest possible seam/fix.

3. Create a focused regression test suite covering the acceptance criteria.
   - Add tests for invoice issuance:
     - arrange a scenario where an invoice is issued and unpaid
     - assert receivables increase by the expected amount
     - assert cash is unchanged at issuance unless current logic says otherwise
   - Add tests for invoice payment:
     - arrange payment after issuance
     - assert cash increases
     - assert receivables decrease by the same settled amount
   - Add tests for bill receipt:
     - arrange a received unpaid bill
     - assert payables increase
   - Add tests for bill payment:
     - arrange payment after bill receipt
     - assert cash decreases
     - assert payables decrease by the paid amount
   - Add tests for overdue emergence:
     - arrange invoice/bill with due dates and unpaid balances
     - advance time past due date using deterministic time progression
     - assert overdue state/count/amount appears in timeline and/or derived summary
   - Add tests for recurring costs and asset purchases:
     - recurring cost should create expected payable or immediate cash effect based on current domain rules
     - asset purchase should create expected payable or immediate cash effect based on current domain rules
   - Add replayability tests:
     - run the same seed/profile/start date twice
     - assert identical event timelines
     - assert identical derived finance summaries

4. Assert the right level of output.
   - Prefer explicit field assertions over vague object equality.
   - For replayability, assert:
     - same event count
     - same event ordering
     - same event dates/types/amounts/identifiers where stable
     - same summary totals and state breakdowns
   - If IDs are intentionally regenerated, compare stable semantic fields rather than transient IDs.

5. Keep tests resilient and readable.
   - Use builder/fixture helpers if available.
   - If no helpers exist, add small local test builders for finance scenarios.
   - Avoid brittle assertions on unrelated fields.
   - Name tests in behavior-first style, e.g.:
     - `Invoice_issuance_increases_receivables`
     - `Invoice_payment_increases_cash_and_reduces_receivables`
     - `Unpaid_invoice_becomes_overdue_after_due_date_passes`
     - `Same_seed_profile_and_start_date_produce_identical_finance_projection`

6. Make minimal production fixes if tests expose non-determinism.
   - Stabilize ordering with explicit sort keys before returning timelines.
   - Replace direct `DateTime.UtcNow` usage with existing clock abstraction if present.
   - Ensure seeded generation does not accidentally use unseeded randomness.
   - Do not change business behavior unless current behavior contradicts acceptance criteria and the shared finance logic clearly intends those outcomes.

7. Ensure the tests validate the shared outputs used by UI and agents.
   - If UI and agents consume the same DTO/query result, test that shared contract directly.
   - If there are multiple wrappers over the same core service, avoid duplicate coverage unless necessary.

# Validation steps
1. Search and inspect the relevant finance code paths before editing:
   - projection/simulation generator
   - finance summary query/service
   - existing finance tests

2. Run targeted tests during implementation:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Run the broader suite before finishing:
   - `dotnet test`

4. Verify each acceptance criterion is explicitly covered by at least one automated test.

5. In the final change summary, include:
   - which shared finance summary path is covered
   - which tests map to each acceptance criterion
   - whether any production determinism fixes were required

# Risks and follow-ups
- The finance logic may be spread across API/application/domain layers, making it easy to test the wrong abstraction. Prioritize the shared path actually used by UI and agents.
- Existing code may rely on ambient time or unseeded randomness, causing flaky tests. Fix only the minimal seams required.
- Event identifiers or timestamps may be unstable across runs. Compare stable semantic fields if IDs are not contractually deterministic.
- Overdue behavior may depend on timezone/date boundary logic. Use explicit UTC or the project’s standard time abstraction in tests.
- Recurring costs and asset purchases may have multiple accounting treatments in the domain. Match current intended business rules already encoded in the system rather than inventing new ones.
- If there is no existing finance test fixture infrastructure, add lightweight reusable builders so future regression scenarios can be added cheaply.