# Goal
Implement `TASK-23.2.1` for `US-23.2 Generate Profit & Loss and Balance Sheet reports by fiscal period` in the existing .NET modular monolith.

Deliver period-based ledger aggregation services and API support that:
- Generate Profit & Loss from **posted journal entries only**
- Generate Balance Sheet from **posted journal entries only**
- Exclude unposted entries and entries outside the requested fiscal period
- Return stable values for closed periods that match the corresponding trial balance snapshot totals
- Include automated tests proving seeded Balance Sheet scenarios balance: **assets = liabilities + equity**

Use the existing architecture and code conventions already present in the solution. Prefer clean application-layer query/services, infrastructure-backed data access, and tenant/company scoping throughout.

# Scope
In scope:
- Add or complete domain/application services for ledger aggregation by fiscal period
- Implement query models/DTOs for:
  - Profit & Loss report
  - Balance Sheet report
- Add repository/query access needed to aggregate posted journal entries by company and fiscal period
- Support closed-period behavior using trial balance snapshot totals if that concept already exists in the codebase; otherwise integrate with the nearest existing snapshot/reporting abstraction without inventing unrelated architecture
- Expose or complete API endpoints for both reports
- Add automated tests covering:
  - posted-only filtering
  - fiscal-period date filtering
  - closed-period stable values
  - balance sheet balancing assertion for seeded scenarios

Out of scope unless required by existing code patterns:
- New UI work in Blazor or MAUI
- New accounting workflows for posting/closing periods
- Reworking the chart of accounts model
- Broad refactors outside reporting/accounting boundaries

# Files to touch
Inspect the solution first, then update the most relevant files in these areas as needed:

- `src/VirtualCompany.Domain/**`
  - accounting/reporting domain models or enums
  - fiscal period / journal entry / account classification types
- `src/VirtualCompany.Application/**`
  - reporting queries, handlers, services, interfaces, DTOs
  - company-scoped authorization/query orchestration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core or SQL-backed repositories/query services
  - snapshot lookup and ledger aggregation persistence logic
- `src/VirtualCompany.Api/**`
  - report endpoints/controllers/minimal APIs
  - request/response contracts if API layer owns them
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for report endpoints
  - seeded reporting scenarios
- Potentially:
  - migration or seed files if reporting test data requires them
  - shared contracts in `src/VirtualCompany.Shared/**` only if already used for API DTOs

Do not touch unrelated mobile/web presentation code unless the existing API contract wiring requires it.

# Implementation plan
1. **Discover existing accounting/reporting model**
   - Find current implementations for:
     - journal entries and journal lines
     - posted/unposted status
     - fiscal periods
     - chart of accounts / account types / normal balance
     - trial balance snapshots or period close snapshots
     - existing report endpoints or placeholders
   - Identify the canonical company/tenant scoping pattern and reuse it.

2. **Define report contracts**
   - Add application/API response models for:
     - `ProfitAndLossReport`
       - company/fiscal period identifiers
       - revenue/income lines
       - expense lines
       - totals
       - net income
     - `BalanceSheetReport`
       - assets lines
       - liabilities lines
       - equity lines
       - totals
       - balancing check fields if appropriate
   - Keep contracts deterministic and easy to test.

3. **Implement ledger aggregation rules**
   - Aggregate from **posted journal entries only**
   - Filter entries to the requested fiscal period date range
   - Exclude:
     - unposted entries
     - entries outside the period
   - Use account classification to map balances into:
     - P&L: income/revenue and expenses
     - Balance Sheet: assets, liabilities, equity
   - Ensure sign handling is correct and user-facing totals are normalized consistently.

4. **Handle closed-period stability**
   - If the requested fiscal period is closed and a trial balance snapshot exists, derive report values from the snapshot-backed balances so values remain stable.
   - Ensure totals match the corresponding trial balance snapshot totals for the same period.
   - If the codebase already has a period-close abstraction, integrate with it rather than duplicating logic.
   - If no snapshot abstraction exists but tests/data already imply one, add the minimal reporting-facing query abstraction necessary.

5. **Add application-layer query services**
   - Create query handlers/services such as:
     - `GetProfitAndLossReportQuery`
     - `GetBalanceSheetReportQuery`
   - Keep business logic out of controllers/endpoints.
   - Return structured line items and totals, not raw ledger rows.

6. **Implement infrastructure data access**
   - Add repository/query methods that efficiently aggregate balances by account for a company and fiscal period.
   - Prefer server-side aggregation in SQL/EF where practical.
   - Preserve tenant/company isolation in every query.
   - Reuse existing persistence abstractions and naming conventions.

7. **Expose API endpoints**
   - Add or complete endpoints for:
     - `GET /api/companies/{companyId}/reports/profit-loss?fiscalPeriodId=...`
     - `GET /api/companies/{companyId}/reports/balance-sheet?fiscalPeriodId=...`
   - Match existing routing/versioning conventions if different.
   - Validate company and fiscal period inputs.
   - Return safe not found/forbidden behavior consistent with the rest of the API.

8. **Add automated tests**
   - Cover acceptance criteria explicitly:
     - P&L returns line items, totals, and net income from posted entries only
     - Balance Sheet returns assets, liabilities, equity, and balancing totals from posted entries only
     - closed periods return stable values matching trial balance snapshot totals
     - unposted and out-of-period entries are excluded
     - seeded scenario proves `total assets == total liabilities + equity`
   - Prefer integration tests through the API if the test project supports it.
   - Add focused application/service tests if needed for sign/account classification edge cases.

9. **Keep implementation aligned with architecture**
   - Modular monolith
   - CQRS-lite queries
   - tenant-aware data access
   - no direct DB logic in controllers
   - deterministic/reporting-safe behavior

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`

2. Run tests before changes to understand current state:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Manually verify report behavior through tests or endpoint inspection:
   - P&L excludes unposted entries
   - P&L excludes entries outside fiscal period
   - Balance Sheet excludes unposted entries
   - Balance Sheet excludes entries outside fiscal period
   - Closed-period report values match trial balance snapshot totals
   - Balance Sheet balances in seeded scenarios

5. If the solution has API integration test helpers or snapshot assertions, use them to verify response payload shape and totals.

# Risks and follow-ups
- **Existing accounting model may differ from assumptions**  
  Journal posting, account typing, or fiscal period closure may already be implemented with different names. Adapt to the existing model instead of forcing new abstractions.

- **Snapshot support may be partial or absent**  
  If trial balance snapshots are not fully implemented, add only the minimum reporting integration needed and document any gap clearly.

- **Sign conventions can be error-prone**  
  Revenue, expenses, equity, and contra accounts may require careful normalization. Add tests around totals and net income to avoid inverted values.

- **Balance Sheet classification may depend on account metadata quality**  
  If seeded accounts are inconsistently typed, tests may fail for data reasons rather than aggregation logic. Fix only the minimal seed data required for deterministic reporting tests.

- **Performance risk on large ledgers**  
  Prefer grouped database aggregation over loading journal lines into memory. If needed, leave a follow-up note for indexing/report caching, but do not over-engineer now.

- **Follow-up candidates**
  - comparative period reporting
  - retained earnings roll-forward handling if not already modeled
  - report export formats
  - Redis/report caching for repeated reads
  - audit events for report generation if reporting access should be tracked