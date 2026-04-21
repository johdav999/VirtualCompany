# Goal
Implement backlog task **TASK-23.1.1 — Create financial statement mapping entities, enums, and EF Core configurations** for story **US-23.1 Map ledger accounts to financial statement sections and reporting classifications**.

Deliver the foundational domain and persistence layer for company-scoped financial statement mappings so later application/API work can build on it cleanly.

This task should create:
- Domain entities for account-to-financial-statement mappings
- Supporting enums/value types for statement type, report section, line classification, and validation/error codes as needed by the model layer
- EF Core configurations and DbSet registrations
- Database constraints and indexes enforcing the uniqueness and active-mapping rules required by the acceptance criteria
- A migration for PostgreSQL

Do **not** implement full API endpoints in this task unless required to support compilation scaffolding. Focus on the model, persistence, and constraints.

# Scope
In scope:
- Add a financial statement mapping aggregate/model that links a **company account** to:
  - statement type
  - report section
  - line classification
  - active/inactive state
  - effective audit timestamps
- Ensure the model is **company-scoped / tenant-safe**
- Support mappings for:
  - Balance Sheet
  - Profit & Loss
  - Cash Flow
- Add deterministic enum/code definitions that downstream validation and API layers can reuse
- Enforce database uniqueness for **one active mapping per company account and statement type**
- Add EF Core configuration with explicit table names, keys, foreign keys, indexes, enum conversions, lengths, and delete behavior
- Add migration artifacts

Out of scope:
- Full command/query handlers
- Full REST endpoints
- Validation endpoint implementation logic
- UI work
- Seed data unless the existing project patterns strongly require enum/reference seeding

Assumptions to honor:
- This is a **modular monolith** with clean boundaries
- Multi-tenancy uses shared schema with `company_id` enforcement
- PostgreSQL is the primary store
- Prefer explicit EF Core fluent configuration over convention magic
- Keep the design extensible for later validation endpoint and API work

# Files to touch
Inspect the solution structure first and follow existing conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - Add new entity/entities for financial statement mappings
  - Add enums for statement type, report section, line classification, and deterministic validation/error codes if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - Add EF Core entity type configuration(s)
  - Register DbSet(s) in the application DbContext
  - Add migration(s)
- Potentially `src/VirtualCompany.Application/**`
  - Only if shared contracts or enum exposure patterns already live here
- Potentially tests:
  - `tests/VirtualCompany.Api.Tests/**` or other existing test projects if there are persistence/model tests already following a pattern

Before coding, locate:
- The main EF Core DbContext
- Existing entity base classes/interfaces for:
  - `Id`
  - `CompanyId`
  - audit timestamps
  - soft delete / active flags
- Existing accounting or ledger account entity/table
- Existing configuration patterns for PostgreSQL partial indexes, filtered unique indexes, enum-to-string conversions, and tenant-scoped foreign keys

# Implementation plan
1. **Discover existing accounting model and conventions**
   - Find the ledger/company account entity that mappings should reference.
   - Confirm whether accounts are modeled as `Account`, `CompanyAccount`, `LedgerAccount`, or similar.
   - Identify base entity/auditing abstractions and naming conventions.
   - Identify whether enums are stored as strings or ints in this codebase; prefer existing convention.

2. **Design the domain model**
   Create a domain entity representing a mapping from a company account to a financial statement classification. Recommended shape:

   - `Id`
   - `CompanyId`
   - `CompanyAccountId` or equivalent FK
   - `StatementType`
   - `ReportSection`
   - `LineClassification`
   - `IsActive`
   - `CreatedAt`
   - `UpdatedAt`

   Optional only if consistent with current domain patterns:
   - `CreatedBy`
   - `UpdatedBy`
   - `Notes`
   - `EffectiveFrom` / `EffectiveTo`

   Keep the model minimal for this task unless the existing architecture already standardizes temporal/effective dating.

3. **Add enums/value definitions**
   Add strongly typed enums for at least:
   - `FinancialStatementType`
     - `BalanceSheet`
     - `ProfitAndLoss`
     - `CashFlow`
   - `FinancialStatementReportSection`
   - `FinancialStatementLineClassification`

   Design these carefully so they can support all three statements without immediate refactor. If one shared enum becomes awkward or ambiguous, prefer statement-aware classification design over forcing a weak generic enum.

   Also add deterministic validation/error code definitions if they belong in domain/shared constants, for example:
   - `UnmappedActiveReportableAccount`
   - `ConflictingStatementMapping`
   - `DuplicateActiveStatementMapping`

   Use the project’s existing style for error code constants/enums.

4. **Model the classification structure pragmatically**
   Because acceptance criteria require support for Balance Sheet, Profit & Loss, and Cash Flow, ensure the enum design can express common sections/classifications such as:
   - Balance Sheet: assets, liabilities, equity, current/non-current, etc.
   - Profit & Loss: revenue, cost of sales, operating expenses, other income/expense, taxes, etc.
   - Cash Flow: operating, investing, financing, non-cash/adjustment style classifications as needed

   Avoid overengineering a full accounting taxonomy if the backlog only requires mapping infrastructure. The goal is a stable persistence model, not exhaustive accounting logic.

5. **Implement entity invariants**
   Add constructor/factory/update methods if that is the domain style.
   Enforce basic invariants in domain code where appropriate:
   - `CompanyId` required
   - account FK required
   - statement type required
   - report section required
   - line classification required
   - active flag defaults to true for new mappings unless conventions say otherwise

   If the domain layer already uses guard helpers, reuse them.

6. **Add EF Core configuration**
   Create a dedicated configuration class with:
   - table name
   - primary key
   - required properties
   - max lengths where applicable
   - enum conversions
   - FK to company account entity
   - FK/delete behavior chosen to avoid accidental cascade data loss
   - indexes for:
     - `CompanyId`
     - `(CompanyId, CompanyAccountId)`
     - `(CompanyId, StatementType, IsActive)` if useful
   - unique filtered/partial index enforcing:
     - one active mapping per company account and statement type

   For PostgreSQL, use a partial unique index equivalent to:
   - unique on `(company_id, company_account_id, statement_type)` where `is_active = true`

   If tenant-safe FK composition is already used in the project, align with it.

7. **Register in DbContext**
   - Add `DbSet<FinancialStatementMapping>`
   - Ensure configuration is applied in `OnModelCreating`
   - Keep naming aligned with existing DbContext conventions

8. **Create migration**
   Generate a migration that:
   - creates the new table
   - creates indexes and unique partial index
   - adds FK constraints
   - uses PostgreSQL-compatible SQL where needed

   Review the generated migration manually; do not trust scaffolding blindly.

9. **Add minimal tests if patterns exist**
   If the repo already has infrastructure/domain tests for EF mappings or migration constraints, add focused tests for:
   - enum persistence
   - unique active mapping constraint
   - allowing multiple inactive historical mappings if supported
   - tenant scoping assumptions

   If no such pattern exists, do not invent a large new test harness for this task.

10. **Document any intentional gaps**
   In code comments or task notes, call out that:
   - unmapped-active-reportable-account prevention will require application/service validation when account activation/reportable flags change
   - validation endpoint behavior is a follow-up task
   - API endpoints are a follow-up task unless already scaffolded elsewhere

# Validation steps
Run and verify at minimum:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration sanity**
   - Ensure the migration compiles
   - Review generated SQL or migration code for:
     - correct table/column names
     - correct FK target
     - correct unique partial index on active mappings
     - correct enum storage strategy

4. **Model verification**
   Confirm the final model supports:
   - one account mapped separately for Balance Sheet, Profit & Loss, and Cash Flow
   - no duplicate active mapping for the same account + statement type
   - company scoping on all records

5. **Constraint verification**
   If there is an integration-test or local-db workflow available, verify:
   - inserting two active mappings for same company account + statement type fails
   - inserting one active and one inactive mapping for same company account + statement type succeeds if historical rows are allowed
   - same account identifier in different companies does not conflict if tenant model permits it

# Risks and follow-ups
- **Risk: unclear existing account entity**
  - The biggest implementation risk is attaching this to the wrong ledger/account entity. Confirm the canonical company-owned account table before coding.

- **Risk: enum design too generic**
  - A single shared `ReportSection` / `LineClassification` enum can become awkward across all three statements. Prefer clarity and future validation support over premature simplification.

- **Risk: acceptance criterion about preventing unmapped active reportable accounts is broader than DB schema**
  - The database model alone cannot fully prevent an account from being active + reportable + unmapped unless account lifecycle updates are validated in application logic. Note this explicitly as a follow-up if not already in scope.

- **Risk: tenant isolation**
  - Ensure `company_id` is present and indexed, and that FK/query patterns align with the project’s tenant enforcement strategy.

Follow-up tasks likely needed after this one:
- Application commands/queries for create/update/list mappings
- Validation service and endpoint returning deterministic error codes
- Account lifecycle validation to block active reportable accounts from remaining unmapped
- API/controller layer and authorization policies
- Possibly seed/reference metadata or UI dropdown contracts for sections/classifications