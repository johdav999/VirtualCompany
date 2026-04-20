# Goal
Implement automated test coverage for **TASK-22.3.4 — Add automated tests for signal rule evaluation and finance snapshot calculations** so the codebase verifies the new decision-support dashboard behavior end to end at the appropriate layers.

The coding agent should add or update tests that prove:

- `ISignalEngine.GenerateSignals(companyId)` returns at least one `BusinessSignal` when seeded operational data meets threshold rules.
- `BusinessSignalsPanel` replaces legacy KPI cards and renders each signal with severity-specific color and icon treatment.
- `GET /api/dashboard/finance-snapshot` returns `Cash`, `BurnRate`, `RunwayDays`, and `RiskLevel` for tenants with finance data.
- `BurnRate` is computed from the average of the last 30 days of expenses.
- `RunwayDays` is computed as `Cash / BurnRate` when `BurnRate > 0`.
- `FinanceSnapshotCard` shows a **Connect accounting** CTA when finance data is missing or incomplete.
- `FinanceSnapshotCard` shows cash, runway days, and risk badge when finance data is available.

Prefer adding tests around existing implementation rather than changing production behavior unless a small fix is required to make the acceptance criteria testable and correct.

# Scope
In scope:

- Add backend automated tests for signal generation behavior.
- Add API automated tests for `/api/dashboard/finance-snapshot`.
- Add UI/component tests for:
  - `BusinessSignalsPanel`
  - `FinanceSnapshotCard`
- Add or update test fixtures/seed data needed to exercise threshold and finance scenarios.
- Make minimal production changes only if necessary to:
  - expose stable selectors/markup for UI tests,
  - fix incorrect finance calculations,
  - align route/DTO names with acceptance criteria,
  - remove/replace legacy KPI card usage if tests reveal stale wiring.

Out of scope:

- Broad dashboard redesign beyond what is needed for these tests.
- New feature work unrelated to signal evaluation or finance snapshot behavior.
- Refactoring unrelated modules.
- Mobile app changes unless the shared component under test is used there too.

# Files to touch
Inspect the solution first, then update the most relevant files you actually find. Likely areas include:

- `tests/VirtualCompany.Api.Tests/**`
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Web/**`
- Any existing dashboard, signal engine, finance snapshot, or Blazor component test projects/files
- Any shared test fixture or seeded data helpers used by API/integration tests

Probable targets by responsibility:

- Signal engine tests:
  - application/domain test files around `ISignalEngine`, `BusinessSignal`, threshold rules, or dashboard analytics
- Finance snapshot API tests:
  - endpoint/controller tests for `GET /api/dashboard/finance-snapshot`
  - query handler tests if CQRS-lite is used
- Web component tests:
  - `BusinessSignalsPanel.razor` and related tests
  - `FinanceSnapshotCard.razor` and related tests
  - dashboard page tests if the panel/card are composed there
- Supporting DTOs/view models:
  - finance snapshot response model
  - signal view model if UI tests need stable properties

If no web component test project exists, create the smallest appropriate one consistent with the repo’s current testing approach.

# Implementation plan
1. **Discover current implementation and test patterns**
   - Search for:
     - `ISignalEngine`
     - `GenerateSignals`
     - `BusinessSignal`
     - `BusinessSignalsPanel`
     - `FinanceSnapshotCard`
     - `finance-snapshot`
     - legacy KPI cards/dashboard widgets
   - Determine:
     - existing test frameworks in use (`xUnit`, `NUnit`, `bUnit`, `FluentAssertions`, `WebApplicationFactory`, etc.),
     - whether API tests are integration-style or controller/unit-style,
     - whether Blazor component tests already exist.

2. **Add signal engine automated tests**
   - Create or extend tests for `ISignalEngine.GenerateSignals(companyId)`.
   - Seed operational data for a tenant that clearly crosses at least one threshold rule.
   - Assert:
     - result is not null,
     - at least one `BusinessSignal` is returned,
     - returned signal belongs to the requested tenant/company,
     - severity/type/title fields are populated if those properties exist.
   - Also ensure tenant isolation is respected if there is an easy existing fixture pattern.

3. **Add finance snapshot calculation tests at the application/API boundary**
   - Create tests for a tenant with finance data covering the last 30 days of expenses.
   - Verify response includes:
     - `Cash`
     - `BurnRate`
     - `RunwayDays`
     - `RiskLevel`
   - Explicitly assert:
     - `BurnRate == average(last 30 days expenses)`
     - `RunwayDays == Cash / BurnRate` when `BurnRate > 0`
   - Add edge-case coverage if straightforward:
     - no finance data,
     - incomplete finance data,
     - zero burn rate should avoid divide-by-zero and produce expected null/0 behavior per current contract.

4. **Add API endpoint tests for `GET /api/dashboard/finance-snapshot`**
   - Use the repo’s preferred API test style.
   - Seed one tenant with finance data and another without.
   - Assert:
     - successful response for valid tenant context,
     - payload shape contains required fields,
     - tenant-scoped data is returned,
     - missing/incomplete data scenario still returns the contract expected by the UI.
   - If auth/company context is required, use the existing tenant-aware test setup.

5. **Add `FinanceSnapshotCard` UI/component tests**
   - Add component tests that render the card with:
     - missing/incomplete finance data,
     - available finance data.
   - Assert:
     - missing/incomplete state shows **Connect accounting** CTA,
     - available state shows cash,
     - available state shows runway days,
     - available state shows risk badge/label.
   - Prefer stable text assertions plus CSS class/data-attribute assertions if severity/risk styling is part of the component contract.

6. **Add `BusinessSignalsPanel` UI/component tests**
   - Render the panel with one or more sample signals of different severities if supported.
   - Assert:
     - legacy KPI cards are not rendered in this panel/path anymore,
     - each signal is rendered,
     - severity-specific color treatment is applied,
     - severity-specific icon treatment is applied.
   - If the current markup is hard to test, add minimal stable hooks such as:
     - semantic CSS classes,
     - `data-testid`,
     - accessible labels.

7. **Verify dashboard composition if needed**
   - If acceptance depends on the dashboard page using `BusinessSignalsPanel` instead of legacy KPI cards, add a page/component integration test or update an existing one.
   - Keep this lightweight; only test composition/wiring, not duplicate all panel assertions.

8. **Make minimal production fixes only if tests expose gaps**
   - Examples of acceptable small fixes:
     - correcting burn rate averaging window,
     - correcting runway calculation,
     - ensuring endpoint returns required fields,
     - replacing stale KPI card component usage,
     - adding testable CSS classes/icons/attributes.
   - Do not perform broad refactors.

9. **Keep tests deterministic**
   - Avoid dependence on `DateTime.UtcNow` without controlling time if the 30-day window matters.
   - Use fixed dates or an injectable clock if one already exists.
   - Seed exact numeric values so expected burn rate and runway calculations are unambiguous.

# Validation steps
Run the smallest relevant commands first, then the broader suite if practical.

1. Restore/build:
   - `dotnet build VirtualCompany.sln`

2. Run targeted tests for the touched projects:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. If there is a separate web/component test project, run it too:
   - `dotnet test <web test project path>`

4. If test filtering is helpful during iteration, use targeted filters for:
   - signal engine tests
   - finance snapshot endpoint tests
   - `BusinessSignalsPanel`
   - `FinanceSnapshotCard`

5. Before finishing, run the relevant broader suite:
   - `dotnet test`

6. In the final work summary, include:
   - which tests were added,
   - any production fixes made,
   - exact calculation assumptions used for burn rate/runway,
   - any remaining gaps if a test project/framework was missing and had to be introduced minimally.

# Risks and follow-ups
- **Unknown existing test stack**: confirm whether bUnit or another Blazor test framework is already used before introducing dependencies.
- **Time-dependent calculations**: finance snapshot tests may be flaky unless dates are fixed or time is controlled.
- **Tenant context setup**: API tests may require existing auth/company fixtures; reuse them rather than inventing a parallel pattern.
- **Legacy KPI card replacement ambiguity**: if the dashboard still contains KPI cards elsewhere, scope assertions specifically to the signals area or dashboard composition path tied to this story.
- **UI styling assertions can be brittle**: prefer stable semantic classes or `data-testid` hooks over fragile full-markup snapshots.
- **Incomplete finance contract behavior**: if the API contract for missing data is not explicit, align tests with current UI expectations and document any assumption.
- **Potential follow-up**: if no component test project exists, a future task may be needed to standardize Blazor UI testing across the solution.