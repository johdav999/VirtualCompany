# Goal
Implement backlog task **TASK-23.4.2 — Implement snapshot generation job and persistence workflow for financial statements** for story **US-23.4 Persist report snapshots and provide statement drilldown APIs**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:
- persists **versioned Profit & Loss and Balance Sheet snapshots**
- records snapshot metadata including **generation timestamp, source period, version number, and checksum/hash of balances**
- exposes a **drilldown API** for both **snapshot-based** and **live** report lines
- preserves prior snapshot versions when regenerating an unlocked period
- includes automated tests proving **drilldown totals reconcile** to the selected report line amount within the same company and fiscal period

Use the existing architecture and conventions in the repository. Keep all data and APIs strictly **tenant/company scoped**.

# Scope
In scope:
- Domain model additions for financial statement snapshots and snapshot lines
- Persistence schema/migrations in PostgreSQL
- Application services/commands/queries for:
  - generating snapshots after successful report generation
  - assigning next version number per company + statement type + fiscal period
  - computing and storing a deterministic checksum/hash of included balances
  - retrieving snapshot versions
  - drilldown for a selected report line from either a stored snapshot or a live report
- Background job/workflow integration for snapshot generation
- API endpoints for snapshot retrieval and drilldown
- Automated tests covering:
  - snapshot persistence
  - versioning behavior
  - no overwrite on regeneration
  - drilldown reconciliation
  - tenant/company isolation

Out of scope unless already partially implemented and required to complete this task:
- Full financial reporting UI
- Export/PDF generation
- Period locking implementation beyond honoring existing lock state if present
- General ledger redesign
- Cross-company aggregation
- Mobile changes

Assumptions to validate in code before implementation:
- There is already some financial reporting/report generation logic for P&L and Balance Sheet, or enough ledger/journal data to build it
- There are existing entities/tables for company, fiscal period, journal entries, and journal lines
- There may already be a background worker/job framework and API patterns to follow

# Files to touch
Inspect first, then update only the necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add snapshot aggregate/entities/value objects/enums
  - add repository interfaces if domain/application patterns require them

- `src/VirtualCompany.Application/**`
  - commands/handlers for snapshot generation
  - queries/handlers for snapshot retrieval and drilldown
  - DTOs/contracts for API responses
  - validation and authorization/tenant scoping logic

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repository implementations
  - migration(s)
  - checksum/hash implementation
  - background job wiring / worker integration
  - SQL/query optimizations for drilldown

- `src/VirtualCompany.Api/**`
  - controllers/endpoints for:
    - list/get snapshots
    - trigger/regenerate snapshot if applicable
    - drilldown API for snapshot or live report
  - request/response contracts if API project owns them

- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for snapshot persistence and drilldown reconciliation

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing migration patterns and test fixtures
- any existing finance/reporting modules under `src/**`

# Implementation plan
1. **Discover existing finance/reporting model and conventions**
   - Find current implementations for:
     - journal entries and journal lines
     - chart of accounts / account classifications
     - fiscal periods / period lock state
     - report generation for Profit & Loss and Balance Sheet
     - tenant/company scoping patterns
     - background jobs/workers
     - API endpoint style and test setup
   - Do not invent parallel patterns if equivalents already exist.

2. **Design snapshot persistence model**
   Add a versioned snapshot model with enough structure to support immutable historical retrieval and drilldown reconciliation.

   Recommended model:
   - `FinancialStatementSnapshot`
     - `Id`
     - `CompanyId`
     - `StatementType` (`ProfitAndLoss`, `BalanceSheet`)
     - `FiscalPeriodId` or normalized period fields if that is the existing pattern
     - `SourcePeriodStart`
     - `SourcePeriodEnd`
     - `VersionNumber`
     - `GeneratedAtUtc`
     - `BalanceChecksum` or `BalancesHash`
     - `Status` if needed (`Completed`, etc.)
     - optional metadata JSON only if repository conventions already support it
   - `FinancialStatementSnapshotLine`
     - `Id`
     - `SnapshotId`
     - stable `LineKey` / `LineCode`
     - `LineLabel`
     - `LineType` / grouping classification
     - `Amount`
     - parent/sort/display fields as needed to reconstruct statement shape
     - optional account/category mapping fields needed for drilldown resolution

   Important:
   - Snapshot lines must preserve the exact line amounts used at generation time.
   - Use immutable semantics for completed snapshots.
   - Add a unique constraint such as:
     - `(company_id, statement_type, fiscal_period_id, version_number)`

3. **Define versioning behavior**
   - On successful generation for an **unlocked** period:
     - determine the next version number as `max(version_number) + 1` for the same company + statement type + fiscal period
     - insert a new snapshot and lines
     - never update/overwrite prior completed snapshots
   - If lock state exists:
     - honor current domain rules
     - do not weaken lock protections
   - Ensure version assignment is safe under concurrency:
     - use transaction + appropriate locking/query strategy
     - document assumptions in code comments where needed

4. **Compute deterministic checksum/hash**
   - Build a deterministic canonical representation of included balances/lines, for example ordered by line key and amount.
   - Hash with a standard algorithm available in .NET, e.g. SHA-256.
   - Store the resulting checksum string on the snapshot record.
   - Ensure the hash is stable across repeated generation of identical balances.

5. **Hook snapshot creation into report generation workflow**
   - After successful report generation, persist snapshots for:
     - Profit & Loss
     - Balance Sheet
   - Prefer application-layer orchestration:
     - generate report data
     - validate success
     - persist snapshot + lines in a transaction
     - emit any audit/outbox events only after persistence succeeds
   - If there is already a background job for report generation, extend it rather than creating a duplicate path.
   - Keep idempotency in mind for retries; avoid duplicate versions from the same execution unless the intended behavior is explicitly “new version per successful regeneration.” If retries can duplicate work, add a correlation/idempotency strategy consistent with the existing job framework.

6. **Implement drilldown service**
   Create a query/service that returns the journal entries and journal lines contributing to a selected report line for either:
   - a **snapshot**
   - a **live report**

   Expected behavior:
   - Inputs should include:
     - `CompanyId`
     - statement type
     - selected line identifier/key
     - either `SnapshotId` or live period parameters
   - Output should include:
     - selected line metadata
     - selected line amount
     - contributing journal entries
     - contributing journal lines
     - total of contributing lines
     - reconciliation delta if useful for tests/debugging
   - Enforce:
     - same company only
     - same fiscal period only
     - no leakage across tenants
   - For snapshot drilldown:
     - resolve the selected snapshot line
     - use the same account/category mapping logic that produced the line
     - query contributing journal lines within the snapshot’s source period and company
   - For live drilldown:
     - use current report mapping logic directly against current ledger data for the requested period

   If current report generation logic does not expose line-to-source mapping cleanly:
   - refactor shared mapping logic into a reusable service used by both statement generation and drilldown
   - avoid duplicating financial classification rules in multiple places

7. **Add API endpoints**
   Implement endpoints consistent with existing API style. Likely endpoints:
   - `GET /api/companies/{companyId}/financial-statements/snapshots`
     - filter by statement type and period
   - `GET /api/companies/{companyId}/financial-statements/snapshots/{snapshotId}`
   - `GET /api/companies/{companyId}/financial-statements/drilldown`
     - supports either snapshot mode or live mode via query parameters

   API requirements:
   - tenant/company authorization enforced
   - return `404` for inaccessible or missing cross-company resources
   - validate mutually exclusive/required parameters
   - keep response contracts explicit and testable

8. **Persist and query efficiently**
   - Add indexes for common access paths, likely:
     - snapshots by company + statement type + fiscal period + version
     - snapshot lines by snapshot + line key
     - journal lines by company + period/date + account/category if needed
   - Keep drilldown queries bounded and deterministic.
   - Avoid N+1 loading for journal entries/lines.

9. **Add automated tests**
   Add integration tests that prove the acceptance criteria. At minimum cover:

   - **Snapshot persistence**
     - generating a P&L and Balance Sheet persists snapshot headers and lines
     - metadata includes generated timestamp, source period, version, checksum

   - **Versioning**
     - regenerate an unlocked period
     - assert a new version is created
     - assert prior version still exists unchanged

   - **Drilldown for snapshot**
     - select a snapshot line
     - returned journal lines and entries belong to same company and period
     - sum of contributing journal lines equals the selected snapshot line amount

   - **Drilldown for live report**
     - same reconciliation assertion for live mode

   - **Tenant isolation**
     - another company cannot access snapshot or drilldown data

   - **Hash determinism**
     - identical balances produce the same checksum if generated from the same canonical data set, unless version metadata is intentionally excluded/included; be explicit in test expectations

10. **Keep implementation auditable and maintainable**
   - Reuse CQRS-lite patterns already present
   - Keep domain logic out of controllers
   - Add concise comments only where the financial mapping/versioning behavior is non-obvious
   - If audit events already exist for finance/report generation, extend them rather than inventing a separate audit mechanism

# Validation steps
1. Inspect the solution structure and identify existing finance/reporting code paths.
2. Build after each major slice:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Specifically verify:
   - migrations apply cleanly in test/dev setup
   - snapshot generation persists both statement types
   - regeneration creates version `n+1` without overwriting prior versions
   - drilldown totals reconcile exactly or according to existing decimal rounding rules
   - cross-company access is blocked
5. If there are API integration tests or snapshot fixtures, update them to reflect the new contracts.
6. Include a short implementation note in the PR/task summary describing:
   - schema added
   - versioning strategy
   - checksum strategy
   - drilldown reconciliation approach

# Risks and follow-ups
- **Unknown existing finance model:** The repository may not yet contain full report generation logic. If missing, implement the minimum shared reporting service needed for this task, but avoid overbuilding.
- **Line-to-source mapping ambiguity:** Some report lines may be derived from grouped account classes. Centralize mapping rules so snapshot generation and drilldown use the same source-of-truth logic.
- **Concurrency on version numbers:** Parallel regenerations could create duplicate version numbers unless protected by transaction/locking and unique constraints.
- **Retry/idempotency behavior:** Background job retries may accidentally create extra versions. Align with existing worker semantics and add safeguards if needed.
- **Rounding/reconciliation edge cases:** Define and consistently apply decimal precision/rounding rules in both report generation and drilldown tests.
- **Period lock semantics:** If lock behavior is not yet implemented, do not invent broad locking rules; honor existing state and leave stricter lock enforcement as a follow-up if necessary.
- **Potential follow-up tasks:**
  - snapshot comparison/diff API across versions
  - UI for browsing snapshot history
  - exportable snapshot artifacts
  - audit event enrichment for report generation and regeneration reasons