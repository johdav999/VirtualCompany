# Goal
Implement backlog task **TASK-23.2.2 — Expose read APIs for Profit & Loss and Balance Sheet with period and company filters** for story **US-23.2 Generate Profit & Loss and Balance Sheet reports by fiscal period**.

Deliver tenant-scoped, read-only ASP.NET Core APIs that:
- return **Profit & Loss** data for a given `companyId` and fiscal period
- return **Balance Sheet** data for a given `companyId` and fiscal period
- derive values from **posted journal entries only**
- exclude unposted entries and entries outside the requested fiscal period
- produce stable values for closed periods that match any corresponding trial balance snapshot totals
- include automated tests, including a seeded scenario asserting **assets = liabilities + equity**

Use the existing modular monolith and CQRS-lite approach. Keep implementation cleanly separated across API, Application, Domain, and Infrastructure layers. Prefer query handlers/services over controller-heavy logic.

# Scope
In scope:
- Discover existing accounting/reporting domain models, persistence, and any fiscal period / journal entry / trial balance snapshot structures already present.
- Add or extend application query models and handlers for:
  - Profit & Loss by company + fiscal period
  - Balance Sheet by company + fiscal period
- Add API endpoints for these read operations.
- Enforce tenant/company scoping consistently.
- Ensure report calculations use only:
  - posted journal entries
  - entries dated within the requested fiscal period
- For closed periods, prefer stable snapshot-backed totals if the architecture already supports trial balance snapshots; otherwise implement deterministic closed-period behavior and document the gap.
- Add automated tests covering:
  - filtering by company
  - filtering by fiscal period
  - exclusion of unposted entries
  - exclusion of out-of-period entries
  - P&L totals and net income
  - Balance Sheet sections and balancing equation
  - closed-period stability / snapshot matching where supported

Out of scope:
- write/update APIs for reports
- UI work unless required for DTO/shared contract compilation
- redesign of accounting domain model
- background snapshot generation jobs unless absolutely required to satisfy existing closed-period behavior
- broad refactors unrelated to reporting

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Api/...`
  - reporting/accounting controllers or endpoint registration
  - request/response contracts if API-specific
- `src/VirtualCompany.Application/...`
  - query objects
  - query handlers
  - report DTOs/view models
  - interfaces for reporting read services/repositories
- `src/VirtualCompany.Domain/...`
  - accounting/reporting domain types if missing and truly domain-worthy
  - fiscal period/report line abstractions only if needed
- `src/VirtualCompany.Infrastructure/...`
  - EF Core/db access implementations
  - SQL/reporting repositories
  - mappings/configurations
- `src/VirtualCompany.Shared/...`
  - shared contracts only if already used for API/web/mobile boundaries
- `tests/VirtualCompany.Api.Tests/...`
  - integration/API tests for both endpoints
  - seeded reporting scenarios
  - assertions for accounting equation and filtering rules

Also inspect:
- existing migrations or archived SQL docs for accounting/reporting schema hints
- `README.md` and project conventions
- any existing journal entry, ledger, account, fiscal period, or trial balance code paths

# Implementation plan
1. **Discover existing accounting/reporting model**
   - Search for:
     - journal entries / journal lines
     - posted status
     - accounts / chart of accounts / account types
     - fiscal periods
     - trial balance snapshots
     - tenant/company authorization patterns
   - Identify whether reports should be built from:
     - journal entry lines joined to accounts
     - ledger balances
     - precomputed snapshots for closed periods
   - Reuse existing conventions for queries, endpoint routing, and test seeding.

2. **Define report contracts**
   - Add application/API response models for:
     - `ProfitAndLossReportDto`
     - `BalanceSheetReportDto`
   - Include enough structure to satisfy acceptance criteria, e.g.:
     - company id
     - fiscal period id or date range
     - currency if available
     - line items grouped by section
     - totals
     - net income for P&L
     - assets / liabilities / equity totals for Balance Sheet
     - balancing total / validation flag if appropriate
     - metadata indicating source basis (`postedEntries` vs `trialBalanceSnapshot`) if useful
   - Keep contracts stable and explicit; avoid leaking EF entities.

3. **Implement application queries**
   - Add query objects such as:
     - `GetProfitAndLossReportQuery`
     - `GetBalanceSheetReportQuery`
   - Add handlers that:
     - validate company and fiscal period inputs
     - resolve fiscal period boundaries
     - call a reporting read service/repository
     - return normalized DTOs
   - Keep business/reporting rules in application/infrastructure services, not controllers.

4. **Implement reporting read service/repository**
   - Build a reporting query service in Infrastructure that:
     - scopes by `company_id`
     - filters to requested fiscal period date range
     - filters to **posted** journal entries only
     - excludes unposted and out-of-period entries
   - Aggregate by account classification:
     - P&L: revenue, cost of sales/COGS if modeled, expenses, other income/expenses if modeled
     - Balance Sheet: assets, liabilities, equity
   - Use the existing account type/category mapping in the system. Do not invent a parallel classification model if one already exists.
   - Be careful with sign conventions:
     - normalize values for report presentation
     - ensure net income and balance sheet totals are mathematically correct
   - If trial balance snapshots exist for closed periods:
     - detect closed period
     - use snapshot totals or reconcile against snapshot-backed balances
     - ensure returned values match snapshot totals for the same period
   - If snapshots do not yet exist but closed periods are represented:
     - compute deterministically from posted entries and note the follow-up in risks.

5. **Expose API endpoints**
   - Add read endpoints under the existing API style, likely something like:
     - `GET /api/companies/{companyId}/reports/profit-loss?fiscalPeriodId=...`
     - `GET /api/companies/{companyId}/reports/balance-sheet?fiscalPeriodId=...`
   - Match existing route/versioning conventions if present.
   - Return appropriate status codes:
     - `200 OK` for success
     - `404` if company/fiscal period not found in scope
     - `403/404` per existing tenant isolation pattern
     - `400` for invalid input if that is the project convention
   - Keep endpoints thin and delegate to application layer.

6. **Tenant and authorization enforcement**
   - Follow existing multi-tenant enforcement patterns from ST-101.
   - Ensure a caller cannot retrieve another company’s report by changing `companyId`.
   - Reuse existing company membership/authorization helpers or policies.

7. **Add automated tests**
   - Prefer integration tests in `tests/VirtualCompany.Api.Tests`.
   - Seed scenarios with:
     - at least two companies to verify tenant isolation
     - one fiscal period with in-period posted entries
     - unposted entries that should be excluded
     - out-of-period posted entries that should be excluded
     - accounts spanning revenue, expenses, assets, liabilities, equity
   - Add tests for P&L:
     - returns line items and totals for requested company/period
     - net income derived from posted entries only
     - excludes unposted and out-of-period entries
   - Add tests for Balance Sheet:
     - returns assets, liabilities, equity
     - total assets equals total liabilities plus equity
     - excludes unposted and out-of-period entries
   - Add closed-period test:
     - if snapshot support exists, assert report values match trial balance snapshot totals
     - otherwise add the strongest deterministic closed-period test possible and document the missing snapshot follow-up

8. **Keep implementation pragmatic**
   - Avoid overengineering a full financial reporting engine.
   - Reuse existing schema and account semantics.
   - Prefer explicit SQL/EF projections for report queries over loading large object graphs.
   - Keep calculations deterministic and testable.

# Validation steps
1. Inspect the solution structure and existing accounting/reporting code.
2. Build after implementation:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Specifically verify:
   - P&L endpoint returns expected line items, totals, and net income
   - Balance Sheet endpoint returns expected assets, liabilities, equity, and balancing totals
   - unposted entries are excluded
   - out-of-period entries are excluded
   - cross-company access is blocked/scoped correctly
   - closed-period values are stable and match trial balance snapshot totals where supported
   - seeded Balance Sheet scenario satisfies `assets == liabilities + equity`
5. If there are snapshot-related assumptions, confirm them with tests or clearly document why they could not be fully implemented from current workspace state.

# Risks and follow-ups
- **Schema uncertainty:** The workspace context does not list accounting-specific files. You may need to discover whether journal entries, account classifications, fiscal periods, and trial balance snapshots already exist.
- **Closed-period snapshot support may be incomplete:** If trial balance snapshots are not implemented yet, fully satisfying the snapshot-matching acceptance criterion may require a follow-up task.
- **Sign convention complexity:** Financial reports are easy to get wrong if debit/credit normalization and account-type presentation rules are inconsistent. Validate with seeded scenarios and existing accounting conventions.
- **Tenant isolation risk:** Reporting endpoints must not bypass company scoping through direct repository access.
- **Performance risk:** Large journal datasets may require aggregate SQL queries rather than in-memory calculations.

If gaps are discovered, document them clearly in code comments/tests and propose focused follow-ups such as:
- add/reporting snapshot repository support for closed periods
- formalize account category mapping for financial statements
- add reusable reporting seed builders for accounting integration tests