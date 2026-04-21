# Goal
Implement backlog task **TASK-26.3.3 — Add integration tests for cash metrics against seeded ledger and payment scenarios** for story **US-26.3 Deliver cash position and cash flow APIs with finance dashboard widgets**.

Create robust **integration tests** that verify tenant-scoped cash metric APIs and finance dashboard widget behavior against **seeded accounting scenarios** covering:
- current cash balance
- expected incoming cash
- expected outgoing cash
- overdue receivables
- upcoming payables

The tests must prove that API and UI outputs match documented business rules for seeded scenarios including:
- overdue invoices
- upcoming bills
- recent cash-account ledger movement
- scheduled payment data
- tenant/company isolation

# Scope
In scope:
- Add or extend **API integration tests** in the existing test project for cash metrics endpoints.
- Add or extend **seed/test data setup** needed to represent realistic finance scenarios.
- Add or extend **UI/integration tests** for finance dashboard widgets if the repo already has a practical pattern for Blazor/web integration testing.
- Verify tenant scoping and selected company/date context behavior.
- Assert exact metric values for seeded scenarios.

Out of scope unless required to make tests pass:
- Large production refactors of finance domain logic.
- Reworking endpoint contracts unless tests reveal a clear mismatch with acceptance criteria.
- Adding new business features beyond what is necessary to test existing/expected behavior.
- Broad UI redesign.

If missing pieces are discovered:
- Prefer the **smallest production changes** necessary to expose stable, testable behavior.
- Document any gaps between acceptance criteria and current implementation in the final notes.

# Files to touch
Start by inspecting and then modify only the minimum necessary set. Likely areas:

- `tests/VirtualCompany.Api.Tests/`
  - cash metrics integration test files
  - shared test fixture/setup files
  - seeded database/test data builders
  - authenticated tenant/company test helpers
- `src/VirtualCompany.Api/`
  - finance/cash metric endpoint wiring if tests expose missing coverage hooks
- `src/VirtualCompany.Application/`
  - query handlers/services for cash metrics if minor fixes are required
- `src/VirtualCompany.Infrastructure/`
  - persistence/query implementations used by cash metric calculations
  - test seeding support if located here
- `src/VirtualCompany.Web/`
  - finance dashboard widget components/pages
  - web test harness files if present
- `README.md` or relevant docs only if you must document test execution or seeded scenario assumptions

Before editing, locate:
- cash metric endpoint definitions
- finance dashboard widget implementation
- existing integration test conventions
- existing seeded finance/accounting entities:
  - ledger entries/movements
  - receivables/invoices
  - payables/bills
  - scheduled payments
  - company/tenant context

# Implementation plan
1. **Discover current implementation**
   - Find the cash metrics API endpoints and their request/response contracts.
   - Identify how tenant scope and company context are resolved in tests.
   - Find existing finance dashboard widget components and how they fetch live API data.
   - Find any existing integration tests for finance, analytics, or dashboard widgets and mirror their style.
   - Identify the documented query rules already implemented in code or docs for:
     - current cash balance
     - expected incoming cash
     - expected outgoing cash
     - overdue receivables
     - upcoming payables

2. **Define deterministic seeded scenarios**
   Create explicit seeded scenarios with fixed dates and amounts so assertions are stable. Prefer one or two compact scenarios over many fragmented ones. Include:
   - a company/tenant with:
     - posted cash-account ledger movements that net to a known current cash balance
     - open receivables including at least one overdue invoice and one not-yet-due invoice
     - open payables including at least one upcoming bill
     - scheduled payment records that affect expected incoming/outgoing metrics per current rules
   - a second tenant/company with conflicting data to verify isolation
   - a fixed “as of” date/context to avoid flaky date-sensitive assertions

   Example shape, adapt to actual schema:
   - Cash ledger posted movements: `+1000`, `-250`, `+400` => current cash balance `1150`
   - Open receivables:
     - overdue invoice `300`
     - upcoming invoice `500`
   - Open payables:
     - upcoming bill `200`
     - later bill `150` depending on query window/rules
   - Scheduled incoming payment `120`
   - Scheduled outgoing payment `80`

   Do not invent rules; align expected values to actual documented logic in code/docs.

3. **Add API integration tests**
   Add tests that hit real HTTP endpoints through the existing integration test host. Cover at minimum:
   - tenant-scoped endpoint returns all five metrics for the selected company
   - current cash balance equals sum of **posted cash-account ledger movements**
   - expected incoming cash equals the sum dictated by open receivables + scheduled incoming payments under current rules
   - expected outgoing cash equals the sum dictated by open payables + scheduled outgoing payments under current rules
   - overdue receivables includes only overdue open receivables
   - upcoming payables includes only upcoming/open payable amounts under current rules
   - recent cash movement scenario is reflected in current cash balance
   - cross-tenant data is excluded
   - optional: date-context variation if endpoint supports date/as-of parameters

   Prefer clear test names such as:
   - `GetCashMetrics_ReturnsExpectedValues_ForSeededFinanceScenario`
   - `GetCashMetrics_UsesOnlyPostedCashAccountLedgerMovements`
   - `GetCashMetrics_ExcludesOtherTenantData`
   - `GetCashMetrics_CalculatesOverdueAndUpcomingAmounts_PerSeededScenario`

4. **Add dashboard widget integration coverage**
   If the repo already supports web/component/integration tests:
   - add tests ensuring the finance dashboard renders all five cash metric widgets
   - verify widgets refresh from live API responses
   - assert displayed values match the seeded scenario

   If there is no practical UI integration test harness:
   - add the lightest viable test at the highest existing level of confidence
   - if only API integration tests are feasible in current repo patterns, note the UI test gap explicitly and add any missing component/service tests that validate widget binding logic

5. **Keep tests maintainable**
   - Centralize seeded finance scenario creation in reusable builders/helpers.
   - Use named constants for dates and expected amounts.
   - Avoid brittle assertions on formatting unless testing UI display.
   - Prefer asserting semantic response fields over raw JSON strings.
   - Ensure tests are isolated and idempotent.

6. **Make minimal production fixes if needed**
   If tests reveal issues:
   - fix only the smallest necessary production code to satisfy acceptance criteria
   - preserve tenant isolation
   - preserve CQRS/query boundaries
   - avoid bypassing application services with direct DB shortcuts in tests unless that is the established integration-test pattern

7. **Document assumptions in code comments**
   Where business rules are subtle, add concise comments in tests explaining why the expected amount is correct for the seeded scenario.

# Validation steps
1. Inspect and run the relevant tests first:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

2. Run the full test suite if feasible:
   - `dotnet test`

3. If production code was touched, also verify build:
   - `dotnet build`

4. Confirm the new tests cover acceptance criteria:
   - API exposes tenant-scoped cash metric endpoints
   - current cash balance matches posted cash ledger movement sum
   - expected incoming/outgoing metrics match open receivables/payables plus scheduled payments per rules
   - overdue receivables and upcoming payables are correct for seeded scenarios
   - finance dashboard widgets render all five metrics and refresh from live API responses, if UI integration harness exists

5. In your final summary, include:
   - files changed
   - seeded scenarios added
   - exact tests added
   - any production fixes made
   - any acceptance-criteria gaps that could not be fully validated due to missing UI test infrastructure

# Risks and follow-ups
- **Schema/model uncertainty:** finance entities may use different names than “ledger”, “receivables”, “payables”, or “scheduled payments”. Adapt to actual domain types rather than forcing new abstractions.
- **Date-sensitive flakiness:** use fixed clocks or explicit as-of dates wherever possible.
- **Tenant scoping gaps:** ensure tests authenticate with one tenant and seed another to catch leakage.
- **UI test infrastructure may be absent:** if so, do not invent a heavy framework; use existing patterns and clearly report the gap.
- **Business rule ambiguity:** if documented query rules are not obvious, derive them from existing handlers/specs and state the source in comments/final notes.
- **Over-seeding complexity:** keep seeded scenarios compact and deterministic so failures are easy to diagnose.

When done, provide a concise implementation summary and list any unresolved questions.