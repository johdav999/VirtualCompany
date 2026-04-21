# Goal
Implement backlog task **TASK-23.4.3 — Build report line drilldown queries from statement lines to journal entries and lines** for story **US-23.4 Persist report snapshots and provide statement drilldown APIs**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:

- supports drilldown from a selected **Profit & Loss** or **Balance Sheet** report line to the underlying **journal entries** and **journal lines**
- works for both:
  - a **persisted snapshot** version
  - a **live report** generated for the same company and fiscal period
- guarantees **tenant/company isolation**
- ensures drilldown totals **reconcile exactly** to the selected report line amount for the same company and period
- includes automated tests

Do not implement unrelated UI work unless required for API contract coverage or testability.

# Scope
Focus on the minimum complete vertical slice needed to satisfy the acceptance criteria for this task, with emphasis on the **query/model/API/test path** for drilldown.

Include:

1. **Domain/application/infrastructure support** for report snapshots if not already present and required by this task’s API/query flow:
   - versioned snapshot metadata
   - generation timestamp
   - source period
   - version number
   - hash/checksum of included balances

2. **Drilldown query capability** from a report line to contributing journal data:
   - input supports either:
     - `snapshotId` + report line identifier
     - live report parameters + report line identifier
   - output includes:
     - journal entries
     - journal lines
     - enough metadata to reconcile totals

3. **Correct accounting filters**:
   - same company only
   - same fiscal period only
   - only journal lines contributing to the selected statement line
   - correct sign/aggregation behavior for P&L and Balance Sheet line mappings

4. **API endpoint(s)** in the existing API project using established conventions

5. **Automated tests** covering:
   - snapshot versioning behavior for unlocked periods
   - drilldown reconciliation
   - tenant isolation
   - snapshot vs live consistency where applicable

Out of scope unless necessary:
- broad report rendering redesign
- frontend drilldown UX
- unrelated accounting refactors
- speculative optimization beyond obvious indexing/query hygiene

# Files to touch
Inspect the solution first and update the exact files that align with existing patterns. Likely areas:

- `src/VirtualCompany.Domain/**`
  - report snapshot entities/value objects
  - report line identifiers / statement types
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for snapshot persistence and drilldown
  - DTOs/contracts for drilldown responses
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories/query services
  - SQL/linq implementations for drilldown joins
  - migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - controller/endpoints for drilldown and snapshot retrieval
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- possibly:
  - `tests/**` application/infrastructure tests if that is where query tests live
  - `README.md` or docs only if API surface requires brief documentation

Before coding, locate existing implementations for:
- journal entries / journal lines
- chart of accounts / account classification
- financial statement generation
- fiscal period handling / period lock state
- tenant/company authorization
- API route conventions
- migrations strategy

Prefer extending existing report-generation code rather than creating parallel logic.

# Implementation plan
1. **Discover existing accounting/reporting model**
   - Find current entities and tables for:
     - journal entries
     - journal lines
     - accounts
     - fiscal periods
     - report generation
     - statement lines / line mapping
   - Identify how report lines are currently derived:
     - by account type
     - by account range
     - by explicit mapping table
     - by computed hierarchy
   - Reuse the same mapping logic for drilldown so totals reconcile by construction.

2. **Define/confirm snapshot model**
   If snapshot persistence is not already implemented, add the minimal model needed:
   - report snapshot header:
     - id
     - company_id
     - statement_type (`ProfitLoss`, `BalanceSheet`)
     - fiscal period reference or source period fields
     - version number
     - generated_at
     - balances checksum/hash
     - optional lock/source metadata
   - report snapshot lines:
     - snapshot id
     - stable line key / code
     - line label
     - amount
     - ordering / hierarchy fields as needed

   Requirements:
   - regenerating an **unlocked** period creates a **new version**
   - prior versions remain intact
   - version uniqueness should be enforced per company + statement type + period + version
   - checksum/hash should be deterministic from included balances

3. **Establish stable report line identity**
   Drilldown must target a line deterministically. If not already present, introduce a stable identifier such as:
   - line code
   - canonical line key
   - statement section + line key

   Avoid using display label alone.

4. **Implement line-to-source resolution**
   Build a reusable application/infrastructure service that, given:
   - company id
   - statement type
   - fiscal period
   - report line key
   - optional snapshot id

   resolves:
   - the account set or source rule behind that line
   - the journal lines in scope
   - parent journal entries

   Important:
   - for **snapshot drilldown**, use the snapshot’s stored period/statement context and validate the requested line exists in that snapshot
   - for **live drilldown**, use the same live report mapping logic used by report generation
   - if snapshots store only aggregated lines, drilldown may still query live journal data constrained by the snapshot’s source period and line mapping, but document this in code comments and ensure reconciliation remains exact for immutable accounting data assumptions; if the architecture already supports storing source membership, prefer that

5. **Design drilldown response contract**
   Return a response shaped for auditability and reconciliation, for example:
   - statement type
   - report line key / label
   - company id
   - period
   - snapshot id nullable
   - report line amount
   - drilldown total
   - reconciliation delta
   - journal entries:
     - entry id
     - entry number/reference
     - posting date
     - description/status
     - lines:
       - journal line id
       - account id/code/name
       - debit
       - credit
       - signed contribution amount
       - description / memo

   Include totals that make test assertions straightforward.

6. **Implement reconciliation-safe query logic**
   Query should:
   - filter by `company_id`
   - filter by posting date / fiscal period boundaries
   - include only posted/finalized entries if that is what report generation uses
   - join journal lines to accounts and line mapping
   - compute signed contribution using the same sign convention as the report line amount

   Ensure:
   - no cross-company leakage
   - no duplicate line inflation from joins
   - balance sheet logic respects period-end semantics if current reporting code does

7. **Add API endpoint(s)**
   Follow existing API conventions. Prefer one endpoint with mutually exclusive request modes or two explicit endpoints, whichever matches current style better.

   Example shapes:
   - `GET /api/companies/{companyId}/reports/snapshots/{snapshotId}/lines/{lineKey}/drilldown`
   - `GET /api/companies/{companyId}/reports/{statementType}/drilldown?periodId=...&lineKey=...`

   Requirements:
   - authorize by company context
   - validate snapshot belongs to company
   - return 404 for missing snapshot/line in company scope
   - return 400 for invalid parameter combinations
   - return safe problem details on reconciliation failure if such a guard is added

8. **Add persistence/migration changes**
   If schema changes are needed:
   - add EF entities/configurations
   - create migration(s)
   - keep naming consistent with existing conventions
   - add indexes for likely access paths, e.g.:
     - snapshot header by company + statement type + period + version
     - snapshot lines by snapshot id + line key
     - journal lines by company + account + posting period if appropriate through existing schema

9. **Automated tests**
   Add integration-style tests that seed realistic accounting data.

   Minimum scenarios:
   - **snapshot versioning**
     - generate snapshot for unlocked period twice
     - assert version 1 and version 2 both exist
     - assert version 1 remains unchanged
   - **drilldown reconciliation**
     - seed accounts, journal entries, and lines contributing to a known report line
     - call drilldown
     - assert sum of returned signed contributions equals selected report line amount
     - assert delta is zero
   - **company isolation**
     - seed another company with similar accounts/entries
     - assert drilldown excludes other company data
   - **snapshot/live parity**
     - for same period and line, assert snapshot drilldown total equals live drilldown total when underlying data is unchanged
   - **not found / invalid access**
     - snapshot from another company returns 404/forbidden per existing API pattern
     - unknown line key returns 404

10. **Guardrails and code quality**
   - Keep commands and queries separated per CQRS-lite style
   - Keep business logic out of controllers
   - Reuse existing authorization and tenant resolution
   - Add concise comments only where accounting logic is non-obvious
   - Do not expose internal DB schema details in API contracts

# Validation steps
Run these after implementation and fix any failures:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly in the project’s normal way.

4. Manually validate the API behavior with seeded test data:
   - create/generate a report snapshot for a company/period
   - call snapshot drilldown for a known line
   - call live drilldown for the same line/period
   - verify:
     - returned journal lines belong only to that company
     - totals reconcile exactly
     - prior snapshot versions remain queryable after regeneration

5. Confirm acceptance criteria explicitly:
   - snapshots persist with version metadata and checksum/hash
   - unlocked regeneration creates new version, not overwrite
   - drilldown returns journal entries and lines
   - drilldown totals reconcile to selected line amount
   - company/period scoping is enforced

# Risks and follow-ups
- **Existing report mapping may be implicit or duplicated**  
  Risk: drilldown logic diverges from report generation logic and causes reconciliation mismatches.  
  Mitigation: centralize line mapping/source resolution in one shared service used by both report generation and drilldown.

- **Snapshot semantics may be underspecified**  
  Risk: if snapshots store only aggregated totals and accounting data can change later, snapshot drilldown may not perfectly represent historical source membership.  
  Mitigation: if feasible, persist enough source context or document current assumption; consider a follow-up task to persist snapshot source lineage.

- **Accounting sign conventions are easy to get wrong**  
  Risk: debit/credit polarity differs by account class and statement type.  
  Mitigation: derive signed contribution using the same code path as statement aggregation and cover with tests.

- **Period filtering may be inconsistent**  
  Risk: live report and drilldown use different date/status filters.  
  Mitigation: reuse fiscal period boundary and posting-status rules from report generation.

- **Performance on large ledgers**  
  Risk: drilldown queries may be expensive.  
  Mitigation: add targeted indexes and keep projection lean; if needed later, add pagination and summary-first responses.

- **Likely follow-up tasks**
  - persist snapshot source lineage for immutable historical drilldown
  - add pagination/filtering/sorting to drilldown results
  - expose drilldown summaries by account before full journal-line expansion
  - add UI drilldown in web dashboard/report views