# Goal
Implement backlog task **TASK-26.3.2 — Build finance dashboard widgets for cash balance and receivable/payable views** for story **US-26.3 Deliver cash position and cash flow APIs with finance dashboard widgets**.

Deliver a tenant-scoped finance dashboard capability across backend and web UI that exposes and renders these five live metrics for the selected company and date context:

1. Current cash balance
2. Expected incoming cash
3. Expected outgoing cash
4. Overdue receivables
5. Upcoming payables

The implementation must satisfy these acceptance criteria:

- The API exposes tenant-scoped endpoints for current cash balance, expected incoming cash, expected outgoing cash, overdue receivables, and upcoming payables.
- Current cash balance equals the sum of posted cash-account ledger movements for the selected company and date context.
- Expected incoming and outgoing cash metrics are computed from open receivables, open payables, and scheduled payment data using documented query rules.
- Finance dashboard widgets render all five cash metrics and refresh successfully from live API responses.
- API and UI tests verify correct metric values for seeded scenarios including overdue invoices, upcoming bills, and recent cash movement.

# Scope
Work within the existing modular monolith and keep boundaries clean:

- **API layer** in `VirtualCompany.Api`
- **Application query/services layer** in `VirtualCompany.Application`
- **Domain contracts/value objects if needed** in `VirtualCompany.Domain`
- **Infrastructure/data access** in `VirtualCompany.Infrastructure`
- **Blazor dashboard UI** in `VirtualCompany.Web`
- **Automated tests** in `tests/VirtualCompany.Api.Tests` and any existing web/UI test location if present

Assumptions and constraints:

- Use **tenant-scoped company context** consistently on every query and endpoint.
- Follow **CQRS-lite**: implement this as read/query functionality, not command-heavy logic.
- Prefer **typed DTOs/query handlers** over controller-level SQL.
- Keep calculations deterministic and document the exact query rules in code comments and/or a short markdown note if there is an existing docs location.
- Do not introduce direct DB access from UI.
- Reuse existing auth, tenant resolution, dashboard, and finance/accounting patterns if already present.
- If finance entities already exist, extend them; if not, add the minimum required read model/query support without overbuilding a full accounting subsystem.

Out of scope unless required by existing patterns:

- Mobile UI changes
- New workflow/approval behavior
- New write-side accounting entry screens
- Broad dashboard redesign beyond the finance widgets needed for this task

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely backend files:
- `src/VirtualCompany.Api/**`
  - finance/cockpit controllers or minimal API endpoint registration
  - tenant authorization wiring if needed
- `src/VirtualCompany.Application/**`
  - finance dashboard query DTOs
  - query handlers/services for cash metrics
  - interfaces for finance metric retrieval
- `src/VirtualCompany.Domain/**`
  - finance enums/value objects/constants if needed for account type, document status, payment schedule status
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query implementations/repositories
  - entity configurations if missing
  - SQL/query composition for ledger, receivables, payables, scheduled payments
- `src/VirtualCompany.Web/**`
  - executive cockpit/dashboard page/component
  - finance widget components/cards
  - API client/service for fetching finance dashboard metrics
  - refresh/loading/error states

Likely test files:
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - seeded scenario tests for metric correctness
- Existing web test project if present; otherwise add focused component/integration tests in the established pattern

Potential docs:
- `README.md`
- `docs/**` if there is an existing place for query rule documentation

Before coding, identify the actual existing files for:
- dashboard page/component
- finance/accounting entities
- tenant-scoped API conventions
- test seeding helpers

Then update this prompt’s file list mentally to match the real codebase.

# Implementation plan
1. **Inspect existing architecture and patterns**
   - Find how tenant/company context is resolved in API requests.
   - Find existing dashboard endpoints/components.
   - Find finance-related entities/tables such as:
     - ledger entries / ledger movements
     - accounts / account types
     - invoices / receivables
     - bills / payables
     - payment schedules / due dates / installments
   - Find existing query handler and DTO conventions.
   - Find test fixture/seeding patterns.

2. **Define the finance dashboard read contract**
   - Add a single aggregate response DTO for the five metrics, or add both:
     - one aggregate endpoint for widget refresh efficiency
     - optional individual endpoints if the codebase already favors separate metric endpoints
   - Include date context inputs, likely:
     - `companyId` from tenant context, not route body
     - `asOfDate`
     - optional upcoming horizon window if needed, e.g. next 30 days
   - Suggested response shape:
     - `currentCashBalance`
     - `expectedIncomingCash`
     - `expectedOutgoingCash`
     - `overdueReceivables`
     - `upcomingPayables`
     - `asOfDate`
     - `currency`
   - Keep money handling consistent with existing conventions.

3. **Document and implement query rules**
   Implement explicit, testable rules. Use existing domain statuses if available; otherwise map carefully.

   Suggested rules:
   - **Current cash balance**
     - Sum posted ledger movements only
     - Include only accounts classified as cash/cash-equivalent
     - Filter by selected company
     - Filter by posting date `<= asOfDate`
   - **Expected incoming cash**
     - Sum open receivable amounts expected to be collected
     - Include scheduled incoming payments not yet received
     - Exclude fully paid, cancelled, voided, or written-off items
     - Use due/scheduled dates within the configured forecast window if that concept exists; otherwise document exact inclusion logic
   - **Expected outgoing cash**
     - Sum open payable amounts expected to be paid
     - Include scheduled outgoing payments not yet paid
     - Exclude fully paid, cancelled, voided items
   - **Overdue receivables**
     - Sum open receivable balances where due date `< asOfDate`
   - **Upcoming payables**
     - Sum open payable balances due on or after `asOfDate` and within the configured upcoming window
   - If scheduled payment data overlaps with open balances, avoid double counting by defining precedence:
     - either scheduled unpaid installments represent the expected amount
     - or remaining open balance is used when no schedule exists
   - Encode this in comments/tests so the behavior is unambiguous.

4. **Implement application-layer query service/handler**
   - Add a query such as `GetFinanceDashboardCashMetricsQuery`.
   - Add a handler/service that:
     - accepts tenant/company context and date context
     - calls infrastructure query methods
     - returns a typed DTO
   - Keep business calculation logic centralized here or in a dedicated read service, not spread across controller/UI.

5. **Implement infrastructure data queries**
   - Add efficient read queries against PostgreSQL via the project’s existing data access pattern.
   - Ensure all queries are tenant-scoped by `company_id`.
   - Prefer server-side aggregation in SQL/EF rather than loading rows into memory.
   - Handle null sums as zero.
   - Respect posted/open/overdue/upcoming status/date filters exactly.
   - If account classification is needed, use existing account type/category metadata to identify cash accounts.

6. **Expose tenant-scoped API endpoints**
   - Add endpoint(s) under the existing finance/dashboard route conventions.
   - Enforce authorization and tenant scoping.
   - Support live refresh from the web app.
   - Return safe, stable DTOs only.
   - If the acceptance criteria are interpreted literally as separate endpoints, provide:
     - `/finance/dashboard/current-cash-balance`
     - `/finance/dashboard/expected-incoming-cash`
     - `/finance/dashboard/expected-outgoing-cash`
     - `/finance/dashboard/overdue-receivables`
     - `/finance/dashboard/upcoming-payables`
   - If the codebase prefers one aggregate endpoint, add that too, and keep the UI on the aggregate endpoint for efficiency.

7. **Build/update Blazor finance dashboard widgets**
   - Add or update a finance section on the executive cockpit/dashboard.
   - Render all five metrics as widgets/cards.
   - Fetch from live API responses through the existing web API client/service pattern.
   - Include:
     - loading state
     - error state
     - empty/zero state
     - manual or automatic refresh behavior consistent with the dashboard
   - Ensure currency/date formatting matches company settings if available.

8. **Add seeded scenario tests for correctness**
   - Create deterministic test data covering:
     - recent posted cash movement affecting current cash balance
     - overdue invoice contributing to overdue receivables and expected incoming cash as applicable
     - upcoming bill contributing to upcoming payables and expected outgoing cash as applicable
     - paid/closed/cancelled items excluded from expected metrics
     - scheduled payment data included according to documented rules
   - Add API tests asserting exact values for each metric.
   - Add UI/component tests if the repo already has a pattern; otherwise at minimum verify the web layer binds and renders API values correctly.

9. **Keep implementation production-safe**
   - Avoid N+1 queries.
   - Keep endpoint payloads small.
   - Add cancellation tokens.
   - Follow existing logging/correlation patterns if present.
   - Do not bypass tenant filters in tests or production code.

# Validation steps
1. **Restore/build**
   - Run:
     - `dotnet build`
   - Fix all compile issues.

2. **Run automated tests**
   - Run:
     - `dotnet test`
   - Ensure new and existing tests pass.

3. **Verify API behavior**
   - Confirm tenant-scoped endpoint(s) return the five metrics.
   - Verify unauthorized cross-tenant access is rejected according to existing conventions.
   - Verify metric values match seeded scenarios exactly.

4. **Verify calculation rules**
   - Confirm current cash balance equals sum of posted cash-account ledger movements up to the selected date.
   - Confirm overdue receivables only include open receivables with due date before the as-of date.
   - Confirm upcoming payables only include open payables in the documented upcoming window.
   - Confirm expected incoming/outgoing metrics include scheduled payments per the documented rules and do not double count.

5. **Verify UI**
   - Launch the web app if practical in the local workflow.
   - Navigate to the dashboard/cockpit.
   - Confirm all five widgets render.
   - Confirm values load from live API responses.
   - Confirm refresh works and error/loading states are reasonable.

6. **Sanity-check code quality**
   - Ensure no finance calculation logic is duplicated between API and UI.
   - Ensure DTOs and query handlers are named clearly.
   - Ensure comments/docs explain the metric rules.

# Risks and follow-ups
- **Risk: finance schema may be incomplete or named differently**
  - Mitigation: inspect actual entities first and adapt the query plan to the real model rather than inventing new tables unnecessarily.

- **Risk: ambiguity in expected incoming/outgoing rules**
  - Mitigation: document exact inclusion/exclusion and precedence rules in code/tests so acceptance is testable.

- **Risk: double counting scheduled payments and open balances**
  - Mitigation: explicitly define whether schedules replace or supplement open document balances and cover this in tests.

- **Risk: tenant scoping mistakes in aggregate queries**
  - Mitigation: enforce `company_id` filters in every query path and add cross-tenant negative tests if patterns exist.

- **Risk: dashboard performance**
  - Mitigation: prefer one aggregate query/endpoint for widget refresh if consistent with architecture, and aggregate in SQL.

- **Risk: missing UI test harness**
  - Mitigation: if no mature UI automation exists, add focused component/integration tests in the established project style and ensure API tests carry the metric correctness burden.

Follow-ups after this task, only if naturally adjacent and not required to complete it:
- Add trend/history spark lines for cash metrics
- Add configurable forecast windows
- Add Redis caching for expensive dashboard aggregates
- Add drill-down links from widgets to receivables/payables detail views