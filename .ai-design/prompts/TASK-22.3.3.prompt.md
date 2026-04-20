# Goal
Implement backlog task **TASK-22.3.3** for story **US-22.3 Transform KPI and finance sections into decision-support signals and snapshots**.

Deliver a vertical slice that:
- replaces legacy dashboard KPI cards with a new **BusinessSignalsPanel.razor**
- adds **FinanceSnapshotCard.razor**
- exposes a tenant-scoped finance snapshot API
- computes burn rate and runway from seeded finance data
- supports a missing-data CTA state for finance setup
- ensures `ISignalEngine.GenerateSignals(companyId)` returns at least one `BusinessSignal` when seeded operational thresholds are met

Work within the existing **.NET modular monolith** and preserve:
- tenant isolation
- CQRS-lite query boundaries
- clean separation between Web, Api, Application, Domain, and Infrastructure
- test coverage for application logic, API behavior, and UI rendering where practical

# Scope
In scope:
- Domain model additions for `BusinessSignal` and finance snapshot result types if missing
- Application-layer signal generation and finance snapshot query/service logic
- API endpoint: `GET /api/dashboard/finance-snapshot`
- Dashboard UI replacement of legacy KPI cards with `BusinessSignalsPanel.razor`
- New `FinanceSnapshotCard.razor` with:
  - available-data state
  - missing/incomplete-data state with **"Connect accounting"** CTA
- Burn rate calculation from average of last 30 days of expenses
- Runway days calculation as `Cash / BurnRate` when `BurnRate > 0`
- Risk level derivation and display badge
- Seed/test data support so acceptance criteria can be verified
- Automated tests

Out of scope unless required by existing patterns:
- New accounting integration flows
- Full finance onboarding wizard
- Mobile changes
- Broad dashboard redesign beyond replacing KPI cards and adding the finance snapshot card
- Refactoring unrelated dashboard widgets

# Files to touch
Inspect the solution first and update the exact files that match existing conventions. Expect to touch files in these areas:

- **Domain**
  - `src/VirtualCompany.Domain/...` for:
    - `BusinessSignal` entity/value object
    - signal severity enum
    - finance snapshot DTO/value object
    - risk level enum
- **Application**
  - `src/VirtualCompany.Application/...` for:
    - `ISignalEngine`
    - `SignalEngine`
    - dashboard query/service handlers
    - finance snapshot calculation logic
    - tenant-scoped repository/query interfaces
- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/...` for:
    - repository/query implementations
    - seeded data support if seeding lives here
    - EF Core or SQL query mappings
- **API**
  - `src/VirtualCompany.Api/...` for:
    - dashboard controller or minimal API endpoint for `GET /api/dashboard/finance-snapshot`
    - response contracts if API-specific DTOs are used
- **Web**
  - `src/VirtualCompany.Web/...` for:
    - dashboard page/component currently rendering legacy KPI cards
    - new `BusinessSignalsPanel.razor`
    - new `FinanceSnapshotCard.razor`
    - any supporting view models or CSS isolation files
- **Tests**
  - `tests/VirtualCompany.Api.Tests/...`
  - any Application/Web test projects already present or nearest equivalent

Before editing, locate the current dashboard implementation and identify:
- where legacy KPI cards are rendered
- where dashboard data is loaded
- whether there is already a dashboard aggregate endpoint/query
- whether finance/expense data already exists in schema and seed data

# Implementation plan
1. **Discover existing dashboard and finance patterns**
   - Search for:
     - `ISignalEngine`
     - `GenerateSignals`
     - dashboard controllers/endpoints
     - KPI card components
     - finance/accounting models
     - seeded operational data
   - Reuse existing naming and folder conventions rather than inventing new structure.
   - If `ISignalEngine` exists but is stubbed, extend it instead of replacing it.

2. **Define or complete domain contracts**
   - Ensure there is a `BusinessSignal` model with at least:
     - title
     - message/summary
     - severity
     - icon key or semantic type
     - optional source/metric metadata
   - Ensure severity supports UI treatment, e.g.:
     - Info
     - Warning
     - Critical
   - Add a finance snapshot model containing:
     - Cash
     - BurnRate
     - RunwayDays
     - RiskLevel
     - indicator for missing/incomplete data if needed
   - Keep these as domain/application contracts, not UI-only types.

3. **Implement signal generation**
   - Update `ISignalEngine.GenerateSignals(companyId)` and implementation to evaluate seeded operational data against threshold rules.
   - Acceptance requires at least one signal for seeded data when thresholds are met.
   - Prefer deterministic rule evaluation over ad hoc UI logic.
   - If thresholds/config already exist in agent or company settings, use them; otherwise implement a minimal rule set aligned with seeded operational data.
   - Ensure tenant scoping on all reads.

4. **Implement finance snapshot query/service**
   - Add an application query/service that returns finance snapshot data for a company.
   - Compute:
     - `BurnRate = average daily expenses over the last 30 days`
     - `RunwayDays = Cash / BurnRate` only when `BurnRate > 0`
   - Handle missing or incomplete finance data gracefully:
     - if cash is missing
     - if there are insufficient expense records
     - if finance source is not connected / no finance records exist
   - Derive `RiskLevel` using a simple deterministic rule set. If no existing rule exists, use a documented threshold approach and keep it centralized, e.g. based on runway bands.
   - Do not compute these values in the controller or Razor component.

5. **Expose API endpoint**
   - Implement `GET /api/dashboard/finance-snapshot`.
   - Return tenant-scoped data only.
   - Response should include:
     - `cash`
     - `burnRate`
     - `runwayDays`
     - `riskLevel`
     - optionally `hasData` / `isIncomplete` if needed by UI
   - For missing/incomplete data, return a successful response with state metadata rather than forcing the UI to infer from errors, unless the existing API pattern dictates otherwise.
   - Follow existing auth, company resolution, and response conventions.

6. **Replace legacy KPI cards with BusinessSignalsPanel**
   - Create `BusinessSignalsPanel.razor`.
   - Replace the old KPI card section in the dashboard with this component.
   - Render one card/item per signal.
   - Apply severity-specific visual treatment:
     - color
     - icon
     - emphasis
   - Keep styling consistent with the current design system if one exists.
   - Avoid hardcoded sample signals in the component; bind to real dashboard data.

7. **Add FinanceSnapshotCard**
   - Create `FinanceSnapshotCard.razor`.
   - Support two states:
     - **Data available**: show cash, runway days, risk badge
     - **Missing/incomplete data**: show **"Connect accounting"** CTA
   - CTA can initially navigate to the existing integrations/settings/accounting route if one exists; otherwise use the nearest valid placeholder route already used by the app.
   - Keep the component resilient to nulls and partial data.

8. **Wire dashboard data loading**
   - Update the dashboard page/view model to load:
     - business signals
     - finance snapshot
   - If the dashboard already uses a composite query/view model, extend it rather than adding fragmented calls unless the current architecture prefers separate API calls.
   - Ensure SSR/interactive behavior matches existing dashboard patterns.

9. **Seed/test data alignment**
   - Verify seeded operational data triggers at least one signal.
   - Verify seeded finance data exists for at least one tenant and produces:
     - cash
     - burn rate
     - runway days
     - risk level
   - If seed data is absent, add minimal deterministic seed records in the existing seeding mechanism.
   - Do not introduce brittle date-dependent seeds; anchor calculations relative to current UTC date in a stable way if existing tests support clock abstraction.

10. **Add tests**
   - Application tests:
     - `GenerateSignals` returns at least one signal for seeded threshold conditions
     - burn rate calculation uses average of last 30 days of expenses
     - runway days computed only when burn rate > 0
     - missing/incomplete finance data returns CTA-driving state
   - API tests:
     - `GET /api/dashboard/finance-snapshot` returns expected fields for tenant with finance data
     - tenant isolation enforced
   - UI/component tests if project supports them:
     - `BusinessSignalsPanel` renders severity-specific classes/icons
     - `FinanceSnapshotCard` renders CTA when data missing
     - `FinanceSnapshotCard` renders values and risk badge when data available

11. **Document assumptions in code comments only where needed**
   - Keep comments brief and focused on non-obvious business rules like risk thresholds or incomplete-data handling.
   - Do not add broad documentation files unless the repo already expects them for feature notes.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually verify dashboard behavior for a tenant with seeded operational and finance data:
   - dashboard no longer shows legacy KPI cards
   - `BusinessSignalsPanel` is visible
   - at least one signal renders
   - severity-specific color/icon treatment is visible
   - `FinanceSnapshotCard` shows cash, runway days, and risk badge

4. Manually verify missing-data state for a tenant without complete finance data:
   - `FinanceSnapshotCard` shows **"Connect accounting"** CTA
   - no broken/null formatting appears

5. Verify API directly:
   - call `GET /api/dashboard/finance-snapshot`
   - confirm response includes `cash`, `burnRate`, `runwayDays`, `riskLevel`
   - confirm missing/incomplete case returns UI-usable state metadata if implemented

6. Verify calculation correctness:
   - inspect seeded expense records used in the last 30 days
   - confirm `BurnRate` equals average daily expenses over that window
   - confirm `RunwayDays = Cash / BurnRate` only when burn rate is positive

7. Verify tenant isolation:
   - ensure one tenant cannot retrieve another tenant’s finance snapshot or signals

# Risks and follow-ups
- **Unknown existing finance schema**: finance data may not yet have a clean source model. If so, implement the thinnest query abstraction that fits current persistence without overdesign.
- **Seed fragility**: date-sensitive seeded expenses can make tests flaky. Prefer fixed clock injection if the codebase already supports it; otherwise keep tests deterministic with explicit test data.
- **Dashboard data duplication**: avoid duplicating signal/finance logic in both API and Web. Centralize in Application services/queries.
- **UI inconsistency**: if no shared badge/icon system exists, keep styling minimal and semantic rather than inventing a new design language.
- **Risk level ambiguity**: if no product-defined thresholds exist, implement a small centralized rule set and note it in code for future product tuning.
- **CTA destination uncertainty**: if no accounting integration route exists, wire the CTA to the closest existing integrations/settings page and leave a clear TODO only if necessary.
- Follow-up candidates after this task:
  - richer signal explanations and drill-down links
  - trend-aware finance snapshot with sparkline/history
  - configurable signal thresholds per company
  - integration-backed accounting connection flow