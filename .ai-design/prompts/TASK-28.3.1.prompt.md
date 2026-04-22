# Goal
Implement backlog task **TASK-28.3.1** for story **US-28.3 Expose derived operational finance state for dashboards, agents, and debug workflows** by adding a **tenant-scoped, point-in-time finance summary projection/query service** in the .NET backend.

The implementation must expose queryable derived metrics for a company and timestamp, computed from the underlying simulated finance state:

- current cash
- accounts receivable
- overdue receivables
- accounts payable
- overdue payables
- monthly revenue
- monthly costs
- recent asset purchases

The service must be deterministic for repeated reads against the same simulation state, aligned with the underlying invoices, bills, payments, assets, and cash snapshots, and performant for seeded companies in local/test environments.

# Scope
In scope:

- Discover the existing finance/simulation domain model and current persistence for:
  - invoices
  - bills
  - payments
  - assets / asset purchases
  - cash snapshots / balances
  - company/time-scoped simulation state
- Add an application-layer query service that computes a finance summary for:
  - `companyId`
  - optional `asOf` timestamp
- Ensure all queries are tenant-scoped and deterministic.
- Expose the summary through the appropriate API/query endpoint used by dashboards/agents/debug workflows.
- Add tests covering correctness, point-in-time consistency, and basic performance expectations.
- Reuse existing CQRS-lite patterns, repository/query abstractions, and DTO conventions already present in the solution.

Out of scope unless required by existing patterns:

- New UI/dashboard rendering
- New simulation generators/seeders beyond minimal test fixtures
- Broad refactors of unrelated finance modules
- Premature caching unless needed to meet thresholds
- Event sourcing or materialized view infrastructure unless the codebase already uses it for similar projections

# Files to touch
Inspect first, then update the most relevant files in these areas:

- `src/VirtualCompany.Application/**`
  - add finance summary query/handler/service
  - add DTO/read model for finance summary
  - add interfaces for projection/query access if needed
- `src/VirtualCompany.Domain/**`
  - only if shared domain value objects/constants are needed
  - avoid unnecessary domain churn
- `src/VirtualCompany.Infrastructure/**`
  - implement data access/query repository
  - add EF Core or SQL query logic
  - add configuration/registrations
- `src/VirtualCompany.Api/**`
  - expose endpoint/controller/minimal API for finance summary query if not already present
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared API contracts
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially other test projects if present for application/infrastructure layers

Also inspect:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- existing finance, analytics, dashboard, simulation, or query handler folders
- any existing migration strategy docs before changing persistence shape:
  - `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing architecture and finance model**
   - Identify how the solution structures:
     - commands/queries
     - handlers
     - repositories
     - API endpoints
     - tenant scoping
     - time-based simulation reads
   - Locate existing entities/tables for invoices, bills, payments, assets, and cash snapshots.
   - Determine whether “monthly revenue” and “monthly costs” should be based on:
     - document issue date
     - due date
     - payment date
     - snapshot month
   - Prefer the interpretation already implied by existing code/tests/docs. If ambiguous, implement the most operationally sensible rule and document it in code comments/tests.

2. **Define the finance summary contract**
   - Add a query/read model with fields similar to:
     - `CompanyId`
     - `AsOfUtc`
     - `CurrentCash`
     - `AccountsReceivable`
     - `OverdueReceivables`
     - `AccountsPayable`
     - `OverduePayables`
     - `MonthlyRevenue`
     - `MonthlyCosts`
     - `RecentAssetPurchases`
   - For recent asset purchases, return either:
     - a count + total amount, or
     - a small list of recent purchase items
   - Match existing dashboard/query conventions in the codebase. If acceptance criteria or existing consumers imply both summary and detail, include both where appropriate.

3. **Implement deterministic point-in-time projection rules**
   - Compute all metrics for the same `companyId` and `asOf` boundary.
   - Use a single consistent cutoff rule, e.g. include records with timestamps `<= asOf`.
   - Ensure repeated reads against unchanged state return identical results.
   - Suggested metric semantics unless the codebase already defines them differently:
     - **current cash**: latest cash snapshot at or before `asOf`, optionally adjusted by later cash-affecting transactions if snapshots are sparse and existing model requires it
     - **accounts receivable**: unpaid or partially paid invoice balances outstanding at `asOf`
     - **overdue receivables**: outstanding invoice balances with due date `< asOf`
     - **accounts payable**: unpaid or partially paid bill balances outstanding at `asOf`
     - **overdue payables**: outstanding bill balances with due date `< asOf`
     - **monthly revenue**: invoice amounts for the month containing `asOf` using the system’s existing accounting convention
     - **monthly costs**: bill amounts and/or recognized asset purchase costs for the month containing `asOf`, based on existing conventions
     - **recent asset purchases**: recent asset acquisitions up to `asOf`, ordered descending by purchase date, limited to a small fixed number if returning detail
   - Handle partial payments correctly.
   - Exclude future-dated records beyond `asOf`.

4. **Choose the query strategy**
   - Prefer a single optimized read path over loading large object graphs.
   - If using EF Core:
     - project directly in SQL where practical
     - avoid N+1 queries
     - use `AsNoTracking()`
   - If using raw SQL/Dapper-style patterns already exist, follow them.
   - Keep tenant filter and `asOf` filter in every query.
   - If consistency matters across multiple subqueries, ensure they run against the same database state/read transaction where appropriate.

5. **Add application-layer query and handler**
   - Create a query such as `GetFinanceSummaryQuery`.
   - Add a handler/service that:
     - validates tenant/company scope
     - normalizes `asOf` to UTC if needed
     - invokes infrastructure query repository/service
     - returns a stable DTO
   - Keep business rules in application/query service, not controller code.

6. **Expose API endpoint**
   - Add or extend an endpoint under the existing analytics/cockpit/finance route conventions.
   - Example shape:
     - `GET /api/companies/{companyId}/finance-summary?asOf=...`
   - Enforce authorization and tenant membership using existing policies.
   - Return safe, concise response contracts suitable for dashboards, agents, and debug workflows.

7. **Add tests**
   - Add integration tests that seed a company with:
     - invoices in current and prior month
     - bills in current and prior month
     - paid, unpaid, and partially paid documents
     - overdue and not-yet-due documents
     - asset purchases
     - cash snapshots
   - Verify:
     - metrics match expected values
     - future records are excluded
     - repeated reads for same `asOf` return same payload
     - tenant isolation prevents cross-company leakage
   - Add edge-case tests for:
     - no finance data
     - only snapshots
     - partial payments exceeding/meeting balances
     - multiple snapshots before `asOf`
     - exact-boundary timestamps

8. **Validate performance**
   - For seeded companies in local/test environments, ensure query completes within reasonable service thresholds.
   - If no explicit threshold exists in code/docs, capture timing in tests or diagnostics and avoid brittle hardcoded micro-benchmarks.
   - Add indexes only if clearly needed and consistent with current migration approach.

9. **Register dependencies and document assumptions**
   - Wire DI registrations in the appropriate startup/composition root.
   - Add concise code comments where metric definitions could be ambiguous.
   - If you must make a domain assumption, encode it in tests so behavior is explicit.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/execute focused tests for finance summary:
   - API/integration tests for the new endpoint/query
   - correctness tests for point-in-time calculations
   - tenant isolation tests

4. Manually verify with seeded data:
   - query finance summary for a company with and without `asOf`
   - repeat the same request multiple times and confirm identical results
   - compare returned values to underlying seeded invoices, bills, payments, assets, and cash snapshots

5. Check performance locally:
   - run the finance summary query against seeded company data
   - confirm no obvious N+1 behavior or excessive query count
   - inspect logs/SQL if available

6. If migrations/indexes are introduced:
   - ensure they follow the repo’s existing migration workflow
   - verify app still builds/tests cleanly after schema changes

# Risks and follow-ups
- **Ambiguous accounting semantics**: monthly revenue/costs and cash derivation may be interpreted differently. Prefer existing codebase conventions; otherwise document assumptions in tests.
- **Sparse or inconsistent snapshot model**: if cash snapshots are not authoritative or require transaction roll-forward, clarify and implement consistently.
- **Performance risk**: naive aggregation over many documents/payments may be slow. Use SQL-side aggregation and targeted indexes if necessary.
- **Determinism risk**: multiple independent queries without a consistent read boundary can produce drift under concurrent writes. Use a consistent read approach if the infrastructure supports it.
- **Tenant leakage risk**: every query path must enforce `company_id`.
- **Follow-up candidates**:
  - Redis caching for repeated dashboard reads if needed
  - materialized projections if seeded/test data grows significantly
  - richer finance trend/history endpoints once this summary contract is stable