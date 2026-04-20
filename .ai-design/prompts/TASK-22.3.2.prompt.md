# Goal
Implement backlog task **TASK-22.3.2** for story **US-22.3 Transform KPI and finance sections into decision-support signals and snapshots** in the existing .NET solution.

Deliver:
1. A backend **finance snapshot computation service**.
2. A tenant-scoped **GET `/api/dashboard/finance-snapshot`** endpoint.
3. Signal generation support so seeded operational data can produce at least one `BusinessSignal` when threshold rules are met.
4. Web dashboard UI updates so:
   - **BusinessSignalsPanel** replaces legacy KPI cards.
   - **FinanceSnapshotCard** shows either:
     - a **“Connect accounting”** CTA when finance data is missing/incomplete, or
     - **cash, burn rate, runway days, and risk badge** when finance data is available.

Keep implementation aligned with the modular monolith / CQRS-lite architecture, tenant isolation, and existing coding patterns in the repo.

# Scope
In scope:
- Domain/application/infrastructure/API/web changes required to support finance snapshot computation and dashboard rendering.
- Add or extend models/contracts for:
  - `BusinessSignal`
  - finance snapshot response DTO/view model
  - risk level representation
- Implement burn rate and runway calculations per acceptance criteria:
  - `BurnRate = average of last 30 days of expenses`
  - `RunwayDays = Cash / BurnRate` when `BurnRate > 0`
- Handle missing/incomplete finance data explicitly.
- Add tests covering service logic and endpoint behavior.
- Update seeded/demo path only as needed so acceptance criteria can be validated against seeded operational data.

Out of scope unless required by existing architecture:
- New external accounting integration.
- Major dashboard redesign beyond replacing legacy KPI cards with signals panel and updating finance snapshot card.
- Mobile app changes.
- Broad refactors unrelated to this task.

# Files to touch
Inspect the solution first and then modify the appropriate files. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - add/update domain models or enums for `BusinessSignal`, severity, finance snapshot, risk level
- `src/VirtualCompany.Application/**`
  - add `ISignalEngine.GenerateSignals(companyId)` implementation or complete existing stub
  - add finance snapshot query/service
  - add DTOs/query handlers/interfaces
- `src/VirtualCompany.Infrastructure/**`
  - data access for finance inputs and seeded operational data
  - repository/query implementations
- `src/VirtualCompany.Api/**`
  - add `GET /api/dashboard/finance-snapshot`
  - wire DI and endpoint/controller
- `src/VirtualCompany.Web/**`
  - replace legacy KPI cards with `BusinessSignalsPanel`
  - update/add `FinanceSnapshotCard`
  - dashboard page/view models and API client calls
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
- Add application/domain tests if there is an existing test project/pattern for them; otherwise keep tests in the API test project if appropriate.

Also inspect:
- `README.md`
- existing dashboard, analytics, cockpit, or finance-related code
- existing seed/demo data setup
- any existing API route conventions and tenant resolution patterns

# Implementation plan
1. **Discover existing patterns before coding**
   - Find current dashboard implementation, legacy KPI cards, and any finance-related models/endpoints.
   - Find how tenant/company context is resolved in API requests.
   - Find whether `ISignalEngine` already exists; if so, extend it rather than creating parallel abstractions.
   - Find seeded data sources for operational and finance data.
   - Follow existing naming, folder structure, endpoint style, and DI registration patterns.

2. **Model the dashboard signal contract**
   - Add or refine a `BusinessSignal` model with fields sufficient for UI rendering, likely including:
     - id/code
     - title
     - message/summary
     - severity
     - icon key or semantic type
     - optional metric/value/context
   - Add a severity enum/value set that supports severity-specific color and icon treatment in the UI.
   - Keep the model presentation-friendly but not tightly coupled to Blazor components.

3. **Implement/complete `ISignalEngine.GenerateSignals(companyId)`**
   - Ensure the method returns at least one `BusinessSignal` when seeded operational data crosses threshold rules.
   - Use deterministic threshold logic, not AI/LLM behavior.
   - Keep logic testable and tenant-scoped.
   - If there is no existing threshold config, implement a minimal ruleset based on available seeded operational data.
   - Make sure the output is stable enough for tests and demo validation.

4. **Add finance snapshot domain/application contract**
   - Create a finance snapshot result model/DTO with:
     - `Cash`
     - `BurnRate`
     - `RunwayDays`
     - `RiskLevel`
     - optionally `HasData` / `IsComplete` if useful for UI branching
   - Define clear semantics for missing/incomplete data:
     - if finance data is absent or insufficient, return a result that allows the UI to show the CTA
     - do not fabricate values

5. **Implement finance snapshot computation service**
   - Add an application service/query handler that loads tenant finance data and computes:
     - `Cash`
     - `BurnRate = average of last 30 days of expenses`
     - `RunwayDays = Cash / BurnRate` only when `BurnRate > 0`
   - Decide and document how “average of last 30 days of expenses” is computed from available data:
     - preferred: total expenses in the last 30 days divided by 30
   - Handle edge cases:
     - no finance records
     - partial records
     - zero burn rate
     - negative or invalid values in seed data
   - Compute `RiskLevel` from runway/cash state using a simple deterministic rule set. If no existing rule exists, add a minimal one and keep it easy to understand, e.g.:
     - High risk: no cash data or very low runway
     - Medium risk: moderate runway
     - Low risk: healthy runway
   - If the repo already has a risk taxonomy, reuse it.

6. **Implement infrastructure/query access**
   - Add repository/query methods needed to fetch:
     - current cash balance
     - expense records for the last 30 days
   - Enforce `company_id` scoping in all queries.
   - Reuse existing EF Core/Dapper/query patterns already present in the solution.
   - If seed data is missing required finance records, add the minimum seed updates needed for acceptance criteria.

7. **Expose API endpoint**
   - Add `GET /api/dashboard/finance-snapshot`.
   - Match existing API conventions for:
     - auth
     - tenant resolution
     - response envelopes vs raw DTOs
     - error handling
   - Return finance snapshot data for tenants with finance data.
   - For missing/incomplete data, return a successful response that allows the UI to render the CTA state unless existing API conventions require another pattern.
   - Do not leak cross-tenant data.

8. **Update dashboard UI: BusinessSignalsPanel**
   - Replace legacy KPI cards on the dashboard with `BusinessSignalsPanel`.
   - Render each signal with severity-specific color and icon treatment.
   - Reuse existing design system/component patterns if present.
   - Keep empty state sensible if no signals are returned.

9. **Update dashboard UI: FinanceSnapshotCard**
   - Add data loading from the new endpoint.
   - Render:
     - CTA state: **“Connect accounting”** when finance data is missing or incomplete
     - data state: cash, runway days, and risk badge when finance data is available
   - Include burn rate in the card if the current design supports it, but at minimum ensure the API returns it and the required visible fields are shown.
   - Keep formatting consistent with app conventions for currency and numeric display.

10. **Wire dependencies**
    - Register new services/query handlers/repositories in DI.
    - Ensure API and web projects can consume the new contracts cleanly.
    - Avoid circular references.

11. **Add tests**
    - Service tests for finance snapshot computation:
      - computes burn rate from last 30 days of expenses
      - computes runway days only when burn rate > 0
      - returns missing/incomplete state correctly
      - assigns risk level deterministically
    - Signal engine tests:
      - seeded operational data produces at least one signal when thresholds are met
    - API tests:
      - `GET /api/dashboard/finance-snapshot` returns expected fields for tenant with finance data
      - tenant scoping is enforced
      - missing/incomplete finance data returns the expected response shape/state
    - If feasible, add a lightweight component/render test for dashboard state selection; otherwise keep UI validation manual.

12. **Keep implementation clean**
    - Prefer small focused classes.
    - Do not introduce unnecessary abstractions if existing code is simple.
    - Add concise comments only where business rules are non-obvious.
    - Preserve backward compatibility where possible, but remove/replace legacy KPI card usage in the dashboard UI as required by the task.

# Validation steps
1. Restore/build:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. Manually verify API:
   - call `GET /api/dashboard/finance-snapshot` for a tenant with finance data
   - confirm response includes:
     - `Cash`
     - `BurnRate`
     - `RunwayDays`
     - `RiskLevel`
4. Manually verify finance calculations against seeded/test data:
   - burn rate equals average daily expenses over the last 30 days
   - runway days equals cash divided by burn rate when burn rate > 0
5. Manually verify dashboard UI:
   - legacy KPI cards are no longer shown
   - `BusinessSignalsPanel` renders signals
   - severity-specific color/icon treatment is visible
   - `FinanceSnapshotCard` shows:
     - CTA when finance data is missing/incomplete
     - cash, runway days, and risk badge when finance data is available
6. Verify seeded operational data path:
   - `ISignalEngine.GenerateSignals(companyId)` returns at least one signal when threshold conditions are met
7. Verify tenant isolation:
   - ensure one company cannot retrieve another company’s finance snapshot

# Risks and follow-ups
- **Unknown existing data model**: finance data tables/entities may not yet exist or may be named differently. Adapt to actual schema rather than inventing a parallel model.
- **Seed data gaps**: acceptance criteria depend on seeded operational/finance data. You may need minimal seed updates to make deterministic tests and demo behavior possible.
- **Risk level ambiguity**: acceptance criteria require `RiskLevel` but do not define thresholds. Reuse existing business rules if present; otherwise implement a simple documented rule set and keep it isolated for future tuning.
- **UI coupling risk**: avoid embedding business logic in Blazor components; keep calculations in application services.
- **Endpoint response shape**: if the API uses envelopes or shared response contracts, conform to them instead of introducing a one-off format.
- **Follow-up suggestion**: if not already present, consider a future task to centralize dashboard query composition so signals and finance snapshot can be fetched together efficiently.