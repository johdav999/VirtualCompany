# Goal

Implement backlog task **TASK-22.3.1** for story **US-22.3 Transform KPI and finance sections into decision-support signals and snapshots** in the existing .NET solution.

Deliver a vertical slice that:
- introduces a **BusinessSignal** domain model for tenant-scoped dashboard decision signals,
- implements **ISignalEngine.GenerateSignals(companyId)** with threshold-based rules for:
  - **operational load**
  - **approval bottleneck detection**
- replaces legacy KPI cards in the web dashboard with a **BusinessSignalsPanel** that renders signals using severity-specific color and icon treatment,
- adds **GET `/api/dashboard/finance-snapshot`** returning:
  - `Cash`
  - `BurnRate`
  - `RunwayDays`
  - `RiskLevel`
- computes finance snapshot values from tenant finance data using the acceptance criteria formulas,
- updates **FinanceSnapshotCard** to:
  - show a **"Connect accounting"** CTA when finance data is missing/incomplete,
  - otherwise show cash, runway days, and risk badge.

Keep the implementation aligned with the architecture:
- modular monolith,
- tenant-scoped queries,
- CQRS-lite application layer,
- ASP.NET Core API,
- Blazor Web App frontend,
- PostgreSQL-backed persistence patterns already used in the repo.

# Scope

In scope:
- Domain model(s) and enums/value objects needed for business signals.
- Application service contract and implementation for signal generation.
- Seed-compatible threshold rules that produce at least one signal for seeded operational data.
- Finance snapshot query/service and API endpoint.
- UI replacement of legacy KPI cards with a business signals panel.
- Finance snapshot card empty/connected states.
- Tests covering rule generation, finance calculations, and API/UI behavior where practical.

Out of scope unless required by existing patterns:
- Broad redesign of dashboard layout beyond replacing the KPI cards area.
- New external accounting integrations.
- Historical trend charts or forecasting beyond the required snapshot.
- Generic rule-builder infrastructure.
- Mobile app changes.
- Large schema redesigns if existing operational/finance tables already support the needed data.

Implementation constraints:
- Preserve tenant isolation on all reads.
- Prefer additive changes over disruptive refactors.
- Reuse existing dashboard/query patterns if present.
- If seeded data structures are incomplete, add the minimum seed/test data support necessary to satisfy acceptance criteria.

# Files to touch

Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

## Domain
Add or update files such as:
- `src/VirtualCompany.Domain/.../BusinessSignal.cs`
- `src/VirtualCompany.Domain/.../BusinessSignalSeverity.cs`
- `src/VirtualCompany.Domain/.../BusinessSignalType.cs`

If finance snapshot is represented in domain/shared contracts, also inspect:
- dashboard/analytics/finance DTO or model files.

## Application
Add or update:
- `ISignalEngine` interface and implementation
- dashboard query/service interfaces
- finance snapshot DTO/query handler
- any tenant-scoped repository abstractions needed

Possible locations:
- `src/VirtualCompany.Application/.../Dashboard/...`
- `src/VirtualCompany.Application/.../Analytics/...`

## Infrastructure
Add or update:
- repository/query implementations for operational metrics and finance data
- EF Core mappings/configurations if new persistence objects are introduced
- seed data support if needed for operational/finance test data

Possible locations:
- `src/VirtualCompany.Infrastructure/.../Persistence/...`
- `src/VirtualCompany.Infrastructure/.../Repositories/...`

## API
Add or update:
- dashboard controller or endpoint registration for:
  - `GET /api/dashboard/finance-snapshot`

Possible locations:
- `src/VirtualCompany.Api/.../Controllers/...`
- `src/VirtualCompany.Api/.../Endpoints/...`

## Web
Add or update dashboard UI:
- replace legacy KPI cards with `BusinessSignalsPanel`
- add/update `FinanceSnapshotCard`

Possible locations:
- `src/VirtualCompany.Web/.../Pages/...`
- `src/VirtualCompany.Web/.../Components/...`
- related CSS/razor files

## Tests
Add/update tests for:
- signal engine rule generation
- finance snapshot calculations
- finance snapshot API response
- dashboard component rendering logic if test infrastructure exists

Possible locations:
- `tests/VirtualCompany.Api.Tests/...`

Do not invent file paths blindly. First search for:
- dashboard
- KPI cards
- finance snapshot
- signal engine
- seeded data
- approvals
- operational metrics

# Implementation plan

1. **Discover existing dashboard and data model**
   - Search the solution for:
     - dashboard controllers/endpoints/components
     - KPI card components
     - finance-related entities/DTOs
     - approval/task/workflow entities
     - any existing analytics or cockpit services
   - Identify:
     - where seeded operational data lives,
     - what tables/entities can support operational load and approval bottleneck rules,
     - whether finance data already exists and how expenses/cash are stored.

2. **Define the BusinessSignal contract**
   - Add a tenant-safe, UI-friendly model for dashboard signals.
   - Include fields sufficient for rendering and future auditability, e.g.:
     - `Type`
     - `Severity`
     - `Title`
     - `Message` or `Summary`
     - `MetricValue`/`Context`
     - optional `ActionLabel`
     - optional `ActionUrl`
     - optional `DetectedAtUtc`
   - Add enums/constants for:
     - signal type
     - severity
   - Keep it simple and serializable.

3. **Add the signal engine abstraction**
   - Introduce or update:
     - `ISignalEngine`
     - `Task<IReadOnlyList<BusinessSignal>> GenerateSignals(Guid companyId, CancellationToken cancellationToken = default)`
   - Place it in the application layer.
   - Ensure the implementation depends on query/repository abstractions, not UI or controller code.

4. **Implement threshold rules for seeded operational data**
   - Build a concrete signal engine that evaluates at minimum:
     - **Operational load**
       - Example basis: too many open/in-progress/blocked tasks, high blocked ratio, overdue workload, or similar operational pressure metric supported by current schema.
     - **Approval bottleneck**
       - Example basis: too many pending approvals, oldest pending approval age above threshold, or approvals awaiting action causing blocked work.
   - Use deterministic thresholds that will trigger for seeded data.
   - If seeded data is absent or insufficient, minimally extend seed/test fixtures so at least one signal is generated for seeded operational data.
   - Keep thresholds centralized as constants or configuration-friendly values.

5. **Create finance snapshot contract and calculation logic**
   - Add a DTO/view model for the API and web layer with:
     - `Cash`
     - `BurnRate`
     - `RunwayDays`
     - `RiskLevel`
     - optionally a boolean like `HasFinanceData` if useful internally
   - Implement calculation rules exactly:
     - `BurnRate = average of last 30 days of expenses`
     - `RunwayDays = Cash / BurnRate` when `BurnRate > 0`
   - Define `RiskLevel` using a clear deterministic rule based on runway or missing data.
   - Handle missing/incomplete finance data safely.

6. **Implement finance data query path**
   - Add application/infrastructure query logic to fetch tenant finance data.
   - Ensure:
     - only the specified company’s data is used,
     - last 30 days expense averaging is date-based and deterministic,
     - missing data returns an empty/incomplete result rather than throwing.
   - If there is no existing finance table/entity, inspect current seed/test structures and add the smallest viable persistence/query support needed.

7. **Expose API endpoint**
   - Add `GET /api/dashboard/finance-snapshot`.
   - Return the finance snapshot payload for the current tenant/company.
   - Match existing API conventions for:
     - authorization
     - company resolution
     - response DTOs
     - problem details/error handling
   - If finance data is missing, still return a valid response shape that allows the UI to show the CTA state.

8. **Replace legacy KPI cards with BusinessSignalsPanel**
   - Find the current dashboard KPI cards component/section.
   - Replace that section with a `BusinessSignalsPanel`.
   - Render each signal with severity-specific:
     - color treatment
     - icon treatment
   - Keep styling consistent with the existing design system.
   - If there are no signals, show a sensible empty state rather than failing.

9. **Update FinanceSnapshotCard**
   - Add two explicit states:
     - **missing/incomplete finance data** → show `"Connect accounting"` CTA
     - **finance data available** → show cash, runway days, and risk badge
   - Ensure the card consumes the new API/query contract.
   - Format currency and runway days consistently with tenant/company settings if such helpers already exist; otherwise use existing app conventions.

10. **Wire dashboard data loading**
    - Ensure the dashboard page/component loads:
      - generated business signals
      - finance snapshot
    - Reuse existing dashboard view model composition if present.
    - Avoid duplicating API calls if the app already has a dashboard aggregate endpoint pattern, unless adding the dedicated finance endpoint is the established approach.

11. **Add tests**
    - Unit/integration tests for signal engine:
      - operational load signal emitted when thresholds are met
      - approval bottleneck signal emitted when thresholds are met
      - seeded operational data yields at least one signal
    - Finance tests:
      - burn rate uses average of last 30 days of expenses
      - runway days computed only when burn rate > 0
      - missing/incomplete data produces CTA-compatible state
    - API test:
      - `GET /api/dashboard/finance-snapshot` returns required fields
    - UI/component tests if supported:
      - severity styling/icon mapping
      - finance card empty vs populated state

12. **Keep changes production-safe**
    - Preserve backward compatibility where possible.
    - Remove or stop rendering legacy KPI cards only after the replacement panel is wired.
    - Avoid leaking cross-tenant data in any aggregate query.
    - Keep calculations timezone-safe and use UTC/date boundaries consistently.

# Validation steps

1. **Code discovery**
   - Search for existing implementations before editing:
     - `dashboard`
     - `kpi`
     - `finance`
     - `approval`
     - `GenerateSignals`
     - `seed`

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Targeted verification of acceptance criteria**
   - Confirm `ISignalEngine.GenerateSignals(companyId)` returns at least one `BusinessSignal` for seeded operational data.
   - Confirm dashboard no longer renders legacy KPI cards and instead renders `BusinessSignalsPanel`.
   - Confirm each signal has severity-based color/icon mapping.
   - Confirm `GET /api/dashboard/finance-snapshot` returns:
     - `Cash`
     - `BurnRate`
     - `RunwayDays`
     - `RiskLevel`
   - Confirm burn rate is based on average of the last 30 days of expenses.
   - Confirm runway days is only derived when burn rate is greater than zero.
   - Confirm `FinanceSnapshotCard` shows:
     - `"Connect accounting"` CTA when finance data is missing/incomplete
     - cash, runway days, and risk badge when data is available

5. **Manual sanity checks**
   - Start the app if feasible and inspect the dashboard UI.
   - Verify tenant-scoped behavior using any existing test tenant/company setup.
   - Verify no null-reference or empty-state rendering issues on companies without finance data.

# Risks and follow-ups

## Risks
- Existing repo may not yet have a finance persistence model, requiring a minimal new query/data path.
- Seeded operational data may not naturally trigger the new rules; thresholds or seed fixtures may need adjustment.
- Legacy KPI cards may be embedded in a larger dashboard component, making replacement slightly broader than expected.
- Risk level rules are not fully specified; choose a deterministic implementation and document it in code/tests.
- If there is no component test setup for Blazor, UI validation may rely on integration/manual checks.

## Follow-ups
- Externalize signal thresholds into configuration once the initial rules are validated.
- Add more signal types later, such as cash risk, workflow failure spikes, or agent inactivity.
- Consider a dashboard aggregate endpoint if multiple dashboard widgets begin making separate API calls.
- Add richer explainability metadata to signals, such as linked task/approval counts and drill-through URLs.
- Align finance risk thresholds with product/UX once business definitions are finalized.