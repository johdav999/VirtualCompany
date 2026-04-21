# Goal
Implement **TASK-23.1.3 — Add database migration, uniqueness constraints, and bootstrap defaults for seeded charts of accounts** for story **US-23.1 Map ledger accounts to financial statement sections and reporting classifications**.

Deliver the persistence and bootstrap foundation for company-scoped financial statement mappings so the system can support Balance Sheet, Profit & Loss, and Cash Flow reporting classification with strong database guarantees and seeded defaults.

# Scope
Focus only on the infrastructure, schema, seed/bootstrap, and minimal supporting domain/application wiring needed for this task.

Include:
- A new persistence model/table for **financial statement mappings** that links a company account to:
  - company
  - account
  - statement type
  - report section
  - line classification
  - active/inactive state
  - audit timestamps
- Database migration(s) for PostgreSQL to create the table and indexes.
- Database constraints to enforce:
  - only one active mapping per **company account + statement type**
  - tenant/company scoping integrity
- Bootstrap/default seeding behavior for seeded charts of accounts so default mappings are created deterministically for known seeded accounts/classifications.
- Any required enums/value objects/constants for statement type, section, classification, and deterministic validation/error code support if needed by the persistence layer.
- Minimal repository/EF configuration updates required so later API work can build on this cleanly.

Do not fully implement:
- Full API endpoints unless absolutely required by existing architecture to compile.
- Full validation endpoint behavior beyond what is necessary to support deterministic persistence semantics.
- UI work.
- Broad refactors unrelated to this task.

If related code for accounts/chart-of-accounts seeding already exists, integrate with it rather than inventing a parallel path.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas as needed:

- `src/VirtualCompany.Domain/**`
  - add domain entity/enums/constants for financial statement mappings
- `src/VirtualCompany.Application/**`
  - add contracts/interfaces only if needed for bootstrap or repository access
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext updates
  - migrations
  - seed/bootstrap logic for default chart-of-accounts mappings
- `src/VirtualCompany.Api/**`
  - only if startup/registration changes are required for bootstrap execution
- `tests/**`
  - add/adjust tests for migration/configuration/bootstrap behavior

Likely file patterns:
- `*DbContext*.cs`
- `*EntityTypeConfiguration*.cs`
- `*Migration*.cs`
- `*Seeder*.cs`, `*Bootstrap*.cs`, `*SeedData*.cs`
- account/chart-of-accounts related entities and seed logic
- integration or infrastructure tests validating uniqueness constraints and seeded defaults

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing accounting model and seeding flow**
   - Find current entities for company accounts, ledger accounts, chart of accounts, and any seeded/default account bootstrap process.
   - Identify how tenant/company ownership is modeled and how migrations are currently authored.
   - Reuse existing naming conventions, enum patterns, and timestamp/base entity patterns.

2. **Add domain model for financial statement mapping**
   - Create a domain entity representing an account-to-statement mapping.
   - Include fields equivalent to:
     - `Id`
     - `CompanyId`
     - `CompanyAccountId` or equivalent account FK
     - `StatementType`
     - `ReportSection`
     - `LineClassification`
     - `IsActive`
     - `CreatedAt`
     - `UpdatedAt`
   - If the domain already uses base auditable entities, inherit appropriately.
   - Add enums/constants for:
     - statement types: `BalanceSheet`, `ProfitAndLoss`, `CashFlow`
     - report sections/classifications as required by current story language
   - Prefer explicit string-backed or int-backed persistence consistent with existing project conventions.

3. **Configure EF Core mapping**
   - Add entity configuration in Infrastructure.
   - Map to a dedicated table with clear column names.
   - Add foreign keys to company and account tables.
   - Add required indexes, including:
     - lookup index by `CompanyId`
     - lookup index by `CompanyAccountId`
     - uniqueness enforcement for one active mapping per account and statement type
   - For PostgreSQL, use a **partial unique index** if supported by current EF/migration approach:
     - unique on `(company_account_id, statement_type)` where `is_active = true`
   - If company integrity requires it, ensure the table includes `company_id` and index `(company_id, company_account_id, statement_type)` as appropriate.

4. **Add migration**
   - Create a migration that:
     - creates the new table
     - creates foreign keys
     - creates indexes
     - creates the partial unique index for active mappings
   - Ensure migration SQL is PostgreSQL-compatible.
   - Name the migration clearly around financial statement mappings / seeded chart-of-accounts defaults.

5. **Implement bootstrap defaults for seeded charts of accounts**
   - Extend existing chart-of-accounts seed/bootstrap flow so seeded accounts receive default financial statement mappings.
   - Seed defaults only for known seeded/system accounts where deterministic mapping is available.
   - Make seeding idempotent:
     - do not duplicate mappings on rerun
     - do not overwrite customer-customized mappings unless the current bootstrap pattern explicitly supports safe updates
   - If seeded accounts are template-based, map from template metadata rather than hardcoding scattered logic.
   - Keep bootstrap deterministic and company-scoped.

6. **Preserve future validation semantics**
   - Add minimal supporting constants or error code definitions if there is an existing validation/error-code pattern.
   - Ensure the persistence model can support later detection of:
     - unmapped active reportable accounts
     - conflicting mappings
   - If account model already has flags like `IsActive` and `IsReportable`, do not enforce cross-row “must be mapped” solely in DB if that would require complex triggers; instead ensure schema supports application validation later.

7. **Add tests**
   - Add tests covering:
     - migration/configuration creates the expected uniqueness behavior
     - cannot insert two active mappings for the same account + statement type
     - can retain historical inactive mappings if intended
     - seeded/bootstrap accounts receive default mappings
     - bootstrap is idempotent
   - Prefer infrastructure/integration tests over brittle unit tests for DB constraints.

8. **Keep implementation compile-safe**
   - Register any new configuration classes in DbContext.
   - Ensure all projects compile.
   - Avoid introducing incomplete API surface unless necessary.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration artifacts are present and coherent:
   - confirm new migration creates the financial statement mapping table
   - confirm PostgreSQL partial unique index or equivalent uniqueness constraint exists

4. Validate seeded bootstrap behavior in tests or existing startup/bootstrap path:
   - seeded chart-of-accounts accounts get default mappings
   - rerunning bootstrap does not create duplicates
   - customized or inactive mappings are not unintentionally replaced

5. Validate DB constraint behavior with integration tests:
   - inserting a second active mapping for same account + statement type fails deterministically
   - inserting inactive historical mapping does not violate the active uniqueness rule if that is the intended design

6. Sanity-check tenant scoping:
   - mappings are always associated with a company
   - queries/bootstrap logic do not cross company boundaries

# Risks and follow-ups
- The exact account/chart-of-accounts model may differ from assumptions; adapt to the existing aggregate rather than forcing a new abstraction.
- Enforcing “active + reportable accounts must be mapped” is usually better handled in application validation than a pure DB constraint; do not add fragile triggers unless the codebase already uses them.
- Cash Flow mappings may be more nuanced than Balance Sheet / P&L; keep the schema flexible enough for future refinement.
- If seeded accounts are currently identified only by display names, bootstrap logic may be brittle; prefer stable seed/template codes if available.
- If no integration test harness exists for PostgreSQL-specific indexes, add the smallest practical test coverage and document any provider limitations.
- Follow-up tasks will likely need:
  - company-scoped CRUD APIs for mappings
  - validation endpoint for unmapped/conflicting accounts with deterministic error codes
  - authorization and audit events for mapping changes