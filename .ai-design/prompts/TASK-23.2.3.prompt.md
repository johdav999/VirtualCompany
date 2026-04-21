# Goal
Implement integration tests for `TASK-23.2.3` to verify fiscal-period financial reporting behavior for Profit & Loss and Balance Sheet APIs under realistic seeded accounting scenarios.

The tests must prove that:
- only **posted** journal entries are included
- **unposted** journal entries are excluded
- entries **outside the requested fiscal period** are excluded
- **cross-period** journal entry scenarios are handled correctly by date
- **closed period** report values remain stable and match the corresponding **trial balance snapshot totals**
- seeded Balance Sheet scenarios satisfy **Assets = Liabilities + Equity**

Use the existing .NET test stack and project conventions in `tests/VirtualCompany.Api.Tests`.

# Scope
In scope:
- Add or extend API integration tests for reporting endpoints related to:
  - Profit & Loss by fiscal period
  - Balance Sheet by fiscal period
- Seed deterministic accounting data for:
  - posted in-period entries
  - unposted in-period entries
  - posted out-of-period entries
  - cross-period entries spanning adjacent fiscal periods by entry date
  - closed-period snapshot comparison scenarios
- Assert response payloads at the level of:
  - line items
  - totals
  - net income
  - accounting equation balance
- Reuse existing test infrastructure, fixtures, factories, and seeding helpers where possible

Out of scope:
- Changing report calculation business logic unless tests expose a real defect that must be fixed for the acceptance criteria to pass
- Refactoring unrelated accounting modules
- UI tests
- Performance/load testing
- New production features beyond minimal testability support

# Files to touch
Inspect the repository first and then update only the minimum necessary files. Likely areas:

- `tests/VirtualCompany.Api.Tests/**`
  - reporting integration test classes
  - shared test fixtures
  - seeded data builders/factories
  - API client helpers / response DTO helpers
- Potentially:
  - `src/VirtualCompany.Api/**` only if test hooks, route discovery, or serialization alignment are needed
  - `src/VirtualCompany.Application/**` only if a bug is revealed and must be corrected
  - `src/VirtualCompany.Infrastructure/**` only if test seeding requires repository support already consistent with architecture

Prefer adding:
- one focused integration test file for fiscal-period reporting scenarios, or
- extending an existing reporting integration test file if one already exists

Do not create duplicate test infrastructure if equivalent helpers already exist.

# Implementation plan
1. **Discover existing reporting and test patterns**
   - Find the Profit & Loss and Balance Sheet API endpoints, request contracts, and response shapes.
   - Find any existing integration tests for:
     - accounting
     - journal entries
     - trial balance
     - fiscal periods
     - reporting APIs
   - Identify the standard integration test setup:
     - test host / `WebApplicationFactory`
     - database lifecycle
     - tenant/company seeding
     - authentication/company context handling

2. **Map domain entities needed for seeding**
   - Identify how the system models:
     - company
     - fiscal period
     - journal entry
     - journal entry lines
     - posting status
     - account types/categories
     - trial balance snapshots
     - closed periods
   - Confirm the exact fields used by report generation:
     - company/tenant scope
     - entry date / posting date
     - status enum/value for posted vs unposted
     - account classification into assets, liabilities, equity, revenue, expenses

3. **Design deterministic seeded scenarios**
   Create a compact but expressive seed set covering all acceptance criteria. Prefer one company with at least two adjacent fiscal periods, for example:
   - Period A: requested/closed period
   - Period B: adjacent period for cross-period exclusion checks

   Seed accounts needed for:
   - Revenue
   - Expense
   - Asset (e.g. cash, receivable)
   - Liability (e.g. payable)
   - Equity (e.g. retained earnings / owner equity)

   Seed journal entries such as:
   - **Posted, in-period revenue entry**
   - **Posted, in-period expense entry**
   - **Unposted, in-period entry** that would materially change totals if incorrectly included
   - **Posted, prior-period entry**
   - **Posted, next-period entry**
   - **Cross-period scenario** represented by separate entries on boundary dates to prove date filtering
   - **Closed-period snapshot** matching the expected trial balance totals for the closed period

   Keep amounts simple and easy to verify manually.

4. **Add Profit & Loss integration tests**
   Implement tests that call the real API and assert:
   - line items include expected revenue/expense accounts from **posted in-period** entries only
   - totals exclude:
     - unposted entries
     - entries before period start
     - entries after period end
   - net income equals expected revenue minus expenses
   - cross-period boundary dates are handled correctly

   Suggested test cases:
   - `ProfitAndLoss_returns_line_items_totals_and_net_income_from_posted_entries_only`
   - `ProfitAndLoss_excludes_unposted_and_out_of_period_entries`
   - `ProfitAndLoss_closed_period_matches_trial_balance_snapshot_totals`

5. **Add Balance Sheet integration tests**
   Implement tests that call the real API and assert:
   - assets, liabilities, and equity sections are derived from **posted in-period / as-of-period logic** according to the existing endpoint semantics
   - unposted and out-of-period entries are excluded
   - totals balance exactly for the seeded scenario:
     - `totalAssets == totalLiabilities + totalEquity`
   - closed-period values match the trial balance snapshot totals where applicable

   Suggested test cases:
   - `BalanceSheet_returns_assets_liabilities_equity_from_posted_entries_only`
   - `BalanceSheet_excludes_unposted_and_out_of_period_entries`
   - `BalanceSheet_total_assets_equals_total_liabilities_plus_equity_for_seeded_scenario`
   - `BalanceSheet_closed_period_matches_trial_balance_snapshot_totals`

6. **Verify closed-period snapshot behavior**
   - If a trial balance snapshot API/helper already exists, use it to obtain comparison values.
   - If snapshots are persisted directly in the database, seed them through the same supported path used by the application.
   - Avoid mocking snapshot behavior; this should remain an integration test.
   - Assert report totals match the snapshot totals for the same company and fiscal period.

7. **Keep tests tenant-safe and isolated**
   - Ensure all seeded data is scoped to the test company.
   - If the test suite supports parallelism, avoid shared mutable identifiers.
   - Use unique company names/IDs per test unless the fixture pattern explicitly supports shared seeded state.

8. **Fix production code only if tests reveal a genuine gap**
   - If report logic currently includes unposted or out-of-period entries, make the smallest production fix necessary.
   - Preserve architecture boundaries:
     - API thin
     - application/query layer owns report behavior
     - infrastructure handles persistence concerns

9. **Polish for maintainability**
   - Use helper methods/builders for journal entry seeding to keep tests readable.
   - Name tests in business terms matching the acceptance criteria.
   - Add concise comments only where scenario setup is non-obvious.

# Validation steps
1. Inspect and run the relevant tests locally:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

2. If needed, run the full suite:
   - `dotnet test`

3. Confirm each acceptance criterion is covered by at least one integration test:
   - P&L uses posted entries only
   - Balance Sheet uses posted entries only
   - closed periods match trial balance snapshot totals
   - unposted and out-of-period entries are excluded
   - Balance Sheet balances: assets = liabilities + equity

4. Verify assertions are meaningful:
   - not just HTTP 200
   - assert actual line items and numeric totals
   - include boundary-date checks for cross-period scenarios

5. If production code changes were required:
   - run `dotnet build`
   - rerun affected integration tests to confirm no regressions

# Risks and follow-ups
- **Unknown endpoint/DTO shapes**: reporting APIs may differ from assumptions; inspect actual contracts before implementing tests.
- **Existing fixture complexity**: integration tests may rely on custom host/database bootstrapping; follow established patterns rather than inventing new ones.
- **Accounting semantics ambiguity**: Balance Sheet may be “for period” or “as of period end”; align assertions to current API contract and fiscal-period story intent.
- **Snapshot availability**: if trial balance snapshots are not yet seedable through public paths, a small test-support enhancement may be needed.
- **Rounding/decimal precision**: use exact decimal assertions where possible; otherwise use precision-safe comparisons consistent with existing tests.
- **Cross-period interpretation**: ensure the scenario proves date-based exclusion clearly, especially around period start/end boundaries.

Follow-up suggestions after completion:
- Add shared reporting seed builders if multiple reporting tasks are upcoming.
- Consider parameterized tests for multiple fiscal period boundaries.
- If gaps are found in snapshot generation coverage, add dedicated integration tests for trial balance snapshot creation and retrieval.