# Goal
Implement backlog task **TASK-24.1.2 — Create migration and backfill scripts for counterparty finance fields and lookup indexes** for story **US-24.1 Implement finance counterparty master data for customers and suppliers**.

Deliver a complete, production-ready change across persistence, migration/backfill, API, UI, and tests so that:
- `Customer` and `Supplier` persistence models include:
  - `paymentTerms`
  - `taxId`
  - `creditLimit`
  - `preferredPaymentMethod`
  - `defaultAccountMapping`
- A PostgreSQL migration adds these fields and creates lookup indexes on tenant/company identifier plus name or reference columns.
- Existing invoices and bills still resolve linked customer/supplier records after migration.
- Admin APIs support create, update, get, and list with the new finance fields.
- Admin UI forms display and persist the new fields.
- Backfill logic populates sensible defaults for existing seeded/mock counterparties and succeeds for seeded tenants.

Work within the existing **.NET modular monolith** architecture, shared-schema multi-tenancy, PostgreSQL persistence, and current solution structure.

# Scope
In scope:
- Update domain/infrastructure persistence models for customers and suppliers.
- Add EF Core configuration and database migration for new finance columns.
- Add/adjust indexes for efficient tenant-scoped lookup by name/reference.
- Implement backfill logic for existing records, preferably as part of migration or a deterministic seed/backfill routine invoked during startup/test setup if that is the project convention.
- Update application/API contracts, commands, queries, validators, and handlers so new fields round-trip correctly.
- Update admin UI forms/pages/components for customer and supplier create/edit/detail/list flows as needed.
- Add or update integration tests proving:
  - migration compatibility
  - linked invoice/customer resolution still works
  - linked bill/supplier resolution still works
  - CRUD/list API coverage for new fields
  - backfill success for seeded tenants

Out of scope:
- Redesigning counterparty domain boundaries beyond what is needed for this task.
- New accounting workflows unrelated to customer/supplier master data.
- Major UI redesign.
- Mobile app changes unless shared DTO changes force compile fixes.

# Files to touch
Inspect the repo first and then modify the actual matching files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - customer/supplier entities or value objects
- `src/VirtualCompany.Application/**`
  - admin commands/queries/DTOs/validators for customers and suppliers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entities/configurations
  - DbContext
  - migrations
  - seed/backfill logic
  - repository/query implementations
- `src/VirtualCompany.Api/**`
  - admin endpoints/controllers/minimal API mappings
  - request/response contracts if API layer owns them
- `src/VirtualCompany.Web/**`
  - admin forms/pages/components for customer/supplier create/edit/list/detail
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for migration, CRUD, list, and invoice/bill linkage
- `docs/postgresql-migrations-archive/README.md`
  - only if migration archival/process docs need updating

Also inspect for existing counterparty-related files by searching for:
- `Customer`
- `Supplier`
- `Invoice`
- `Bill`
- `Counterparty`
- `payment terms`
- `account mapping`

# Implementation plan
1. **Discover current implementation**
   - Find the existing customer and supplier domain/persistence models.
   - Identify where invoice-to-customer and bill-to-supplier relationships are stored and resolved.
   - Identify current admin API endpoints and UI forms for customers/suppliers.
   - Identify migration approach:
     - EF Core migrations
     - SQL scripts archive
     - startup-applied migrations
   - Identify seeded/mock tenant data path and where backfill should run.

2. **Model the new finance fields**
   - Add the new fields to customer and supplier persistence/domain models using existing naming conventions.
   - Use types consistent with finance data:
     - `paymentTerms`: string or enum-backed string per existing conventions
     - `taxId`: string
     - `creditLimit`: decimal/numeric with explicit precision
     - `preferredPaymentMethod`: string or enum-backed string
     - `defaultAccountMapping`: string/JSON/value object depending existing accounting model; prefer the smallest change that satisfies acceptance criteria and current architecture
   - Keep nullability/backward compatibility in mind for existing rows.

3. **Update EF Core mappings**
   - Configure new columns with explicit lengths/precision where appropriate.
   - Ensure tenant/company scoping remains intact.
   - Add indexes on `(company_id, name)` and `(company_id, reference)` or the actual equivalent columns used by customer/supplier lookup.
   - If only one of name/reference exists per entity, create the appropriate tenant-scoped lookup indexes that match the schema and acceptance criteria intent.
   - Do not break existing foreign keys from invoices/bills.

4. **Create migration**
   - Generate or hand-author the EF Core migration.
   - Migration should:
     - add new columns to customer and supplier tables
     - preserve existing data
     - create the new indexes
     - include backfill SQL/data updates where appropriate
   - Backfill should populate defaults for existing mock/seeded counterparties. Prefer deterministic defaults such as:
     - `paymentTerms`: `"Net 30"` or project-standard default
     - `preferredPaymentMethod`: `"BankTransfer"` or project-standard default
     - `creditLimit`: `0` or another safe default if required
     - `taxId`: leave null/empty unless seeded conventions require a placeholder
     - `defaultAccountMapping`: project-standard default mapping code/reference if one exists; otherwise safe nullable default
   - Make sure migration is idempotent in the way EF migrations normally are and safe for existing environments.

5. **Implement backfill logic**
   - If migration SQL alone is sufficient, keep it there.
   - If seeded tenants/mock data are created after migration in tests/dev setup, also update the seed path so newly seeded data includes the finance fields.
   - Ensure backfill completes successfully for seeded tenants and does not overwrite non-empty existing values.
   - Log or expose enough information in tests to verify success.

6. **Update application layer**
   - Extend customer/supplier create and update commands with the new fields.
   - Extend get/list DTOs/view models to return the new fields.
   - Add validation aligned with current patterns:
     - valid payloads must not fail
     - avoid over-restrictive validation
     - enforce decimal ranges/lengths only if existing conventions require it
   - Ensure mapping between API contracts, application DTOs, and persistence models is complete.

7. **Update admin API**
   - Ensure create, update, get, and list endpoints for customers and suppliers accept/return the new fields.
   - Preserve backward compatibility where possible.
   - Confirm serialization naming matches current API conventions.

8. **Update admin UI**
   - Add fields to customer and supplier forms.
   - Ensure values load on edit/detail and persist on save.
   - Ensure valid payloads submit without validation errors.
   - Update list/detail displays if needed to surface finance-relevant fields, but prioritize form persistence and correctness.

9. **Protect invoice/bill linkage**
   - Verify no migration or mapping change alters primary keys, foreign keys, or lookup behavior used by invoices/bills.
   - Add/adjust integration tests that migrate existing data and then fetch invoices/bills with linked customer/supplier resolution intact.

10. **Add tests**
   - Add integration tests covering:
     - migration applies successfully
     - existing invoice still resolves linked customer after migration
     - existing bill still resolves linked supplier after migration
     - customer admin API create/update/get/list includes finance fields
     - supplier admin API create/update/get/list includes finance fields
     - backfill populates defaults for seeded/mock counterparties
   - Prefer end-to-end API tests over isolated unit tests for acceptance coverage.

11. **Keep code quality high**
   - Follow existing naming, folder structure, and architectural boundaries.
   - Avoid introducing duplicate DTOs or parallel models if existing ones can be extended.
   - Keep changes minimal but complete.

# Validation steps
1. Search the solution to confirm all customer/supplier request/response and persistence paths include the new fields.
2. Build the solution:
   - `dotnet build`
3. Run relevant tests first if targeted test filters exist, then full API/integration tests:
   - `dotnet test`
4. Verify migration artifacts are present and compile.
5. If the repo supports local DB migration execution, apply migrations and confirm schema includes:
   - new customer columns
   - new supplier columns
   - tenant/company + name/reference indexes
6. Verify integration tests specifically prove:
   - invoice → customer link still resolves
   - bill → supplier link still resolves
   - create/update/get/list round-trip all new finance fields
   - seeded/mock counterparties receive backfilled defaults
7. Manually inspect UI form bindings/components to ensure no missing field causes null submission or validation mismatch.

# Risks and follow-ups
- **Schema mismatch risk:** actual table/entity names may differ from “customer/supplier”; inspect before coding.
- **Index ambiguity:** acceptance criteria says “tenant/company identifier plus name or reference columns”; use the real lookup columns in the schema and document the choice in code/comments if needed.
- **Backfill default risk:** `defaultAccountMapping` may depend on a broader accounting model not yet implemented. Use the safest nullable/default approach consistent with current code and seeded data.
- **Validation risk:** over-validating finance fields could fail the acceptance criterion requiring valid payloads to persist without errors.
- **Migration safety risk:** avoid destructive changes or FK rewiring that could break invoice/bill linkage.
- **Seed/test divergence risk:** if tests use separate seeding paths from production startup, update both as needed.
- **Follow-up:** if finance fields should become shared value objects or enums later, defer that refactor unless already established in the codebase.