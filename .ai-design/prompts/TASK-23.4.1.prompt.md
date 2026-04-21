# Goal
Implement backlog task **TASK-23.4.1 — Create financial statement snapshot tables and versioning model** for story **US-23.4 Persist report snapshots and provide statement drilldown APIs** in the existing .NET modular monolith.

Deliver a production-ready vertical slice that:
- Persists **versioned Profit & Loss and Balance Sheet snapshots** per company and fiscal period after successful report generation
- Stores snapshot metadata including:
  - generation timestamp
  - source period
  - version number
  - deterministic hash/checksum of included balances
- Exposes a **drilldown API** that returns the journal entries and journal lines contributing to a selected report line for either:
  - a persisted snapshot version, or
  - a live report calculation
- Ensures regenerating an **unlocked** period creates a **new snapshot version** and never overwrites prior versions
- Includes automated tests proving drilldown totals reconcile to the selected report line amount within the same company and period

Work within the current architecture:
- ASP.NET Core modular monolith
- PostgreSQL primary store
- CQRS-lite application layer
- strict tenant/company scoping
- no direct DB access from controllers
- migrations managed in Infrastructure
- tests added in the existing test projects

# Scope
Implement only what is necessary to satisfy this task and acceptance criteria.

In scope:
- Domain model for financial statement snapshots and versioning
- Persistence schema and EF Core mappings/migrations for snapshot headers and lines
- Application commands/services to create snapshots after successful report generation
- Version allocation logic per company + statement type + fiscal period
- Deterministic checksum/hash generation for included balances
- Query/API support for drilldown by selected report line for:
  - snapshot-backed reports
  - live reports
- Reconciliation logic in tests to verify drilldown totals match report line totals
- Tenant/company isolation and period scoping
- Locked vs unlocked period behavior as required for version creation rules

Out of scope unless already trivially present and required to wire this task:
- Full redesign of report generation engine
- UI work beyond minimal API exposure
- Exporting snapshots to files/object storage
- General ledger redesign
- Broad audit/eventing enhancements unrelated to this task
- Mobile changes

If existing accounting/reporting primitives already exist, extend them. If they do not, add the smallest coherent implementation needed for snapshot persistence and drilldown.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:
- Domain entities/value objects/enums for:
  - statement snapshot
  - statement snapshot line
  - statement type
  - snapshot source/live mode identifiers if needed
- Application layer:
  - commands/handlers for snapshot creation
  - queries/handlers for drilldown retrieval
  - DTOs/contracts for snapshot metadata and drilldown response
  - interfaces for report snapshot repository/services
- Infrastructure:
  - EF Core entity configurations
  - DbContext updates
  - repository implementations
  - migration(s) for PostgreSQL
  - checksum/hash helper if infrastructure-owned
- API:
  - endpoints/controllers for drilldown
  - request/response contracts if API-specific
- Tests:
  - integration/API tests for snapshot versioning
  - drilldown reconciliation tests
  - tenant/company isolation tests if not already covered nearby

Also inspect:
- existing accounting/reporting models
- journal entry / journal line entities
- fiscal period and period lock models
- any existing report generation services
- migration conventions documented in:
  - `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing accounting/reporting structure**
   - Search for:
     - journal entries
     - journal lines
     - chart of accounts
     - fiscal periods
     - period lock/unlock
     - profit and loss / balance sheet generation
   - Identify current report generation flow and where “successful generation” is determined.
   - Reuse existing naming and module boundaries.

2. **Design the snapshot data model**
   Create a minimal normalized schema that supports versioned snapshots and drilldown.

   Recommended model:
   - `financial_statement_snapshots`
     - `id`
     - `company_id`
     - `statement_type` (`profit_loss`, `balance_sheet`)
     - `fiscal_period_id` or equivalent period reference
     - `source_period_start`
     - `source_period_end`
     - `version_number`
     - `balances_checksum`
     - `generated_at`
     - optional metadata such as currency, generation context, locked-state-at-generation
   - `financial_statement_snapshot_lines`
     - `id`
     - `snapshot_id`
     - `line_code` or stable line identifier
     - `line_name`
     - `line_order`
     - `amount`
     - optional parent/grouping fields if current report model requires hierarchy
     - optional account basis metadata if needed for drilldown mapping

   Important:
   - Add a unique constraint on `(company_id, statement_type, fiscal_period_id, version_number)` or equivalent period key.
   - Add indexes for:
     - company + statement type + period
     - snapshot id
     - line code lookup
   - Prefer stable line identifiers over display names for drilldown targeting.

3. **Model versioning behavior**
   - On successful generation for an unlocked period:
     - compute next version number as `max(version_number) + 1` for the same company + statement type + fiscal period
     - insert a new snapshot header and lines
     - never update/overwrite prior versions
   - If there is existing lock logic:
     - respect it
     - do not introduce overwrite behavior
   - Keep version allocation transactional to avoid duplicate versions under concurrency.

4. **Implement deterministic checksum/hash**
   - Build a deterministic canonical representation from included snapshot lines, for example:
     - ordered by stable line code/order
     - concatenate `line_code|amount`
   - Hash with a standard algorithm available in .NET, such as SHA-256.
   - Store the resulting hex/base64 string in `balances_checksum`.
   - Ensure the same balances produce the same checksum regardless of insertion order.

5. **Persist snapshots after successful generation**
   - Hook snapshot creation into the existing report generation application flow.
   - If report generation currently returns a structured statement model, map that model into snapshot header + lines.
   - Keep this in the application layer behind an interface, not in controllers.
   - Ensure both P&L and Balance Sheet are supported.

6. **Implement drilldown query model**
   Add a query/service that accepts enough information to resolve either a snapshot line or a live line, such as:
   - `companyId`
   - `statementType`
   - period reference
   - either:
     - `snapshotId` + `lineCode`, or
     - `live` + `lineCode`

   Response should include:
   - selected line metadata
   - report line amount
   - contributing journal entries
   - contributing journal lines
   - reconciliation total
   - period and company context

   Requirements:
   - all contributing data must be scoped to the same company and period
   - line selection must map to the same account set/rules used by report generation
   - snapshot drilldown should use the snapshot’s line identity/definition while still retrieving underlying journal detail from the same company/period basis
   - live drilldown should use current report calculation rules directly

7. **Align drilldown logic with report line composition**
   - Reuse existing report line/account mapping logic if available.
   - Do not duplicate business rules in a divergent way.
   - If no reusable abstraction exists, extract one shared component that both:
     - report generation
     - drilldown resolution
     use to determine contributing accounts/journal lines.

8. **Expose API endpoint(s)**
   Add minimal API/controller endpoints consistent with the current API style, for example:
   - `GET /api/companies/{companyId}/financial-statements/drilldown?...`
   or equivalent route structure already used in the codebase.

   Support:
   - snapshot drilldown
   - live drilldown

   Return safe, structured DTOs only.
   Enforce:
   - company scoping
   - authorization conventions already present
   - not found/forbidden behavior consistent with tenant-aware APIs

9. **Add EF Core configuration and migration**
   - Update DbContext
   - Add entity configurations
   - Generate a migration for PostgreSQL
   - Verify migration naming follows repo conventions
   - Ensure nullable/non-nullable choices match acceptance criteria

10. **Add automated tests**
   At minimum add integration tests covering:
   - snapshot persistence for P&L and Balance Sheet after successful generation
   - metadata persisted:
     - generated timestamp
     - source period
     - version number
     - checksum
   - regenerating an unlocked period creates a new version and preserves prior versions
   - drilldown API for snapshot returns journal entries/lines contributing to selected line
   - drilldown API for live report returns journal entries/lines contributing to selected line
   - reconciliation:
     - sum of returned contributing journal line amounts equals selected report line amount
     - scoped to same company and period
   - cross-company isolation:
     - another company cannot access snapshot/drilldown data

11. **Keep implementation clean**
   - Use domain-meaningful names
   - Keep commands and queries separate
   - Avoid leaking EF entities through API
   - Add comments only where logic is non-obvious
   - Prefer extending existing accounting/reporting abstractions over introducing parallel ones

# Validation steps
Run and verify the following after implementation:

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. Validate migration artifacts
   - Confirm new migration exists in the expected Infrastructure migrations location
   - Confirm schema includes:
     - snapshot header table
     - snapshot line table
     - unique version constraint
     - useful indexes

4. Manual/API validation if test harness supports it
   - Generate a P&L and Balance Sheet for a company/period
   - Confirm snapshot rows are created
   - Regenerate same unlocked period
   - Confirm version increments and prior version remains unchanged
   - Call drilldown for a selected line on:
     - snapshot version
     - live report
   - Confirm returned journal lines total equals selected line amount

5. Code quality checks
   - Ensure no controller/service bypasses application boundaries
   - Ensure all queries are company-scoped
   - Ensure checksum generation is deterministic
   - Ensure version creation is transactional/concurrency-safe

# Risks and follow-ups
- **Existing report engine mismatch:** If current report generation lacks stable line identifiers, introduce a stable `line_code` abstraction now; otherwise drilldown and snapshot version comparisons will be brittle.
- **Period model ambiguity:** If there is no single fiscal period entity, use explicit start/end dates consistently and document the chosen uniqueness key.
- **Concurrency risk on version numbers:** Use transactional version allocation and DB uniqueness constraints to prevent duplicate version numbers under simultaneous regenerations.
- **Drilldown rule duplication:** Biggest correctness risk is implementing drilldown logic separately from report generation logic. Prefer extracting shared line-to-account resolution.
- **Sign conventions:** P&L and Balance Sheet lines may use debit/credit sign normalization. Tests must validate reconciliation using the same sign convention as the report line amount.
- **Snapshot fidelity:** Current acceptance criteria require snapshot persistence and drilldown, but future work may require storing richer line hierarchy, account rollups, or source references per line.
- **Locked period behavior:** This task only requires new versions for unlocked periods. If locked-period regeneration behavior is undefined elsewhere, do not invent broad behavior beyond existing domain rules; note any gap clearly.
- **Future follow-up candidates:**
  - endpoint to list snapshot versions by company/statement/period
  - endpoint to fetch a full snapshot document
  - richer audit events for snapshot generation
  - retention policy for snapshot history
  - materialized/report cache optimization if live drilldown becomes expensive