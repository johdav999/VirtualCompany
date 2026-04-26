# Goal

Implement backlog task **TASK-26.3.1 — Implement cash position and cash flow summary API endpoints** for story **US-26.3 Deliver cash position and cash flow APIs with finance dashboard widgets**.

Deliver tenant-scoped backend APIs and finance dashboard integration for these five metrics:

1. current cash balance
2. expected incoming cash
3. expected outgoing cash
4. overdue receivables
5. upcoming payables

The implementation must satisfy these rules:

- All endpoints are **tenant/company scoped**
- **Current cash balance** = sum of **posted cash-account ledger movements** for the selected company and date context
- **Expected incoming/outgoing cash** are computed from **open receivables**, **open payables**, and **scheduled payment data** using documented query rules
- Finance dashboard widgets render all five metrics from **live API responses**
- API and UI tests cover seeded scenarios including:
  - overdue invoices
  - upcoming bills
  - recent cash movement

Use the existing modular monolith structure and keep logic in the application/domain/infrastructure layers, not in controllers or Blazor pages.

# Scope

In scope:

- Add or extend finance analytics query models/services for cash summary metrics
- Expose tenant-scoped ASP.NET Core API endpoints for cash summary data
- Implement documented query rules in code comments and/or developer docs near the query service
- Wire finance dashboard widgets in the Blazor web app to call the live API
- Add automated tests for API and UI/widget rendering behavior
- Ensure company scoping and date-context handling are enforced

Out of scope unless required by existing code patterns:

- New accounting domain redesign
- Mobile app changes
- Broad dashboard redesign outside the finance widgets
- New integration sync pipelines
- Forecasting beyond the accepted metrics
- Arbitrary reporting builder UX

Assumptions to validate from the codebase before coding:

- There are existing finance/accounting entities or tables for ledger movements, receivables, payables, and scheduled payments
- There is an existing tenant/company resolution pattern in API and app layers
- There is an existing dashboard page/widget area for finance metrics
- There are existing test seed helpers or fixtures for tenant/company financial scenarios

If any required finance entities do not exist, implement the smallest consistent extension needed and document it in the PR notes.

# Files to touch

Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Api/`
  - finance/cockpit controller or endpoint registration
  - request/response DTOs
- `src/VirtualCompany.Application/`
  - analytics/cockpit query handlers
  - finance summary service interfaces and implementations
  - tenant-scoped query contracts
- `src/VirtualCompany.Domain/`
  - value objects or enums if needed for ledger/account/payment status semantics
- `src/VirtualCompany.Infrastructure/`
  - EF Core or SQL query implementations
  - repository/query service wiring
  - migrations only if truly required
- `src/VirtualCompany.Web/`
  - finance dashboard widgets/components/pages
  - API client/service used by dashboard
- `src/VirtualCompany.Shared/`
  - shared DTOs/contracts if this solution uses shared API contracts
- `tests/VirtualCompany.Api.Tests/`
  - endpoint tests
  - seeded scenario tests for metric correctness

Also inspect for relevant existing files such as:

- dashboard/cockpit pages
- analytics query services
- tenant authorization helpers
- finance/accounting entities
- test fixture/seeding utilities

# Implementation plan

1. **Discover existing finance and dashboard architecture**
   - Find current patterns for:
     - tenant/company scoping
     - query handlers/services
     - dashboard widgets
     - API endpoint style
     - test seeding
   - Identify the canonical source for:
     - ledger movements
     - receivables
     - payables
     - scheduled payments
   - Do not invent parallel models if existing ones already support this task.

2. **Define the API contract**
   - Add a response contract for the five metrics, ideally a single summary payload for dashboard efficiency.
   - Prefer one endpoint such as:
     - `GET /api/companies/{companyId}/finance/cash-summary`
     - or the project’s existing tenant-scoped route convention
   - Include date context parameters if the app already supports them, for example:
     - `asOfDate`
     - `fromDate`
     - `toDate`
     - or a dashboard context object
   - Keep the response explicit and stable, e.g. amounts + currency + as-of metadata.

3. **Document and implement metric query rules**
   - Implement the rules in one application/infrastructure query service, not duplicated across API/UI.
   - Add concise code documentation for the exact rules used.
   - Minimum expected rules:
     - **Current cash balance**
       - sum posted ledger movements
       - include only cash accounts
       - scoped to selected company
       - filtered by date context/as-of date
     - **Expected incoming cash**
       - based on open receivables and scheduled incoming payments
       - exclude fully settled/closed items
       - use due/scheduled dates according to existing domain semantics
     - **Expected outgoing cash**
       - based on open payables and scheduled outgoing payments
       - exclude fully settled/closed items
     - **Overdue receivables**
       - open receivables with due date before as-of date
     - **Upcoming payables**
       - open payables due within the selected upcoming window or dashboard date context
   - Avoid double counting if both open items and scheduled payments reference the same obligation. Use the existing domain relationship if present; otherwise document the chosen deduplication rule in code.

4. **Implement tenant-safe query layer**
   - Add an application query/service interface, e.g. `IGetCashSummaryQuery` or similar per project conventions.
   - Implement in infrastructure using efficient SQL/EF queries.
   - Enforce:
     - `company_id` filtering
     - status filtering
     - posted/open semantics
     - date filtering
   - Keep read logic CQRS-lite and side-effect free.
   - If performance matters, prefer aggregate SQL over loading rows into memory.

5. **Expose API endpoint**
   - Add endpoint/controller action using existing auth and tenant membership patterns.
   - Validate company access before returning data.
   - Return safe 403/404 behavior consistent with the rest of the API.
   - Keep controller thin: delegate to application query service.

6. **Integrate finance dashboard widgets**
   - Find the finance dashboard/cockpit widget area in `src/VirtualCompany.Web`.
   - Add or update widgets to render all five metrics from the live API.
   - Ensure refresh/reload behavior works successfully.
   - Handle loading, empty, and error states consistently with existing dashboard UX.
   - Do not hardcode seeded values in UI.

7. **Add tests**
   - **API tests**
     - verify tenant scoping
     - verify metric correctness for seeded scenarios
     - verify date-context behavior
     - verify unauthorized cross-tenant access is blocked
   - **Scenario coverage**
     - recent posted cash movement affects current cash balance
     - overdue invoice contributes to overdue receivables and expected incoming as appropriate
     - upcoming bill contributes to upcoming payables and expected outgoing as appropriate
     - settled/closed items are excluded
   - **UI tests**
     - finance dashboard widgets render all five metrics
     - widgets refresh from live API responses
     - error/loading states behave correctly if there is an existing test pattern for this

8. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries
   - Keep business logic out of Blazor components and controllers
   - Reuse shared contracts if the solution already does so
   - Preserve tenant isolation in all layers

# Validation steps

Run these after implementation:

1. **Build**
   - `dotnet build`

2. **Automated tests**
   - `dotnet test`

3. **Targeted verification**
   - Run or add focused API tests for:
     - cash balance from posted cash ledger movements
     - expected incoming from open receivables + scheduled incoming payments
     - expected outgoing from open payables + scheduled outgoing payments
     - overdue receivables
     - upcoming payables
     - tenant isolation
   - Run or add focused web/UI tests for dashboard widget rendering and refresh

4. **Manual verification**
   - Start the app if local run instructions already exist in the repo
   - Open the finance dashboard for a seeded company
   - Confirm all five widgets display values from the API
   - Confirm refresh updates values after seeded/live data changes
   - Confirm another tenant/company cannot access the same data

5. **Code quality checks**
   - Ensure query rules are documented near the implementation
   - Ensure no duplicated metric logic exists between API and UI
   - Ensure no in-memory full-table aggregation where a database aggregate should be used

# Risks and follow-ups

- **Risk: unclear existing finance schema**
  - Mitigation: inspect current entities/migrations/tests first and adapt to existing semantics rather than guessing.

- **Risk: double counting between open items and scheduled payments**
  - Mitigation: explicitly document and test deduplication rules.

- **Risk: ambiguous date context**
  - Mitigation: align with existing dashboard date filter conventions; if none exist, implement a simple `asOfDate` plus documented upcoming window behavior.

- **Risk: tenant leakage**
  - Mitigation: enforce company scoping in every query and add cross-tenant tests.

- **Risk: dashboard widget coupling**
  - Mitigation: use a single API-backed summary contract and keep UI presentation-only.

- **Risk: performance on aggregates**
  - Mitigation: use SQL-side aggregation and add indexes only if needed by existing migration patterns.

Follow-ups if not fully covered by this task:

- add caching for expensive dashboard aggregates if needed
- add trend/history endpoints for cash metrics
- add explicit finance query documentation in `README` or module docs
- add observability around dashboard query latency and failures