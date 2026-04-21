# Goal
Implement backlog task **TASK-24.1.1** for story **US-24.1 Implement finance counterparty master data for customers and suppliers** by extending customer and supplier master data across the full stack so finance-relevant fields are persisted, exposed via admin APIs, rendered in admin UI forms, and safely migrated/backfilled for existing seeded tenants.

# Scope
Deliver the following end-to-end changes:

- Extend **Customer** and **Supplier** persistence/domain/application contracts with:
  - `paymentTerms`
  - `taxId`
  - `creditLimit`
  - `preferredPaymentMethod`
  - `defaultAccountMapping`
- Add a **database migration** that:
  - introduces the new columns
  - preserves existing links from invoices/bills to counterparties
  - adds indexes on **tenant/company identifier + name or reference columns**
- Add/update **backfill/seed logic** so existing mock counterparties receive sensible defaults and seeded tenants migrate successfully
- Update **Admin API** create/update/get/list flows for customers and suppliers to accept and return the new fields
- Update **Admin UI** forms and list/detail views as needed so all finance fields display and persist without validation errors for valid payloads
- Add/adjust **integration tests** proving invoices and bills still resolve linked customer/supplier records after migration
- Add/adjust **API/UI tests** where the project already has patterns for them

Out of scope unless required by existing architecture/patterns:

- New finance workflows beyond master data capture
- New accounting posting logic using account mappings
- Advanced validation/business rules not already established in the codebase
- Breaking API contract changes unrelated to these fields

# Files to touch
Inspect the solution first and then update the concrete files that match existing patterns. Likely areas:

- **Domain**
  - `src/VirtualCompany.Domain/**`
  - Customer/Supplier entities, value objects, enums, constants
- **Application**
  - `src/VirtualCompany.Application/**`
  - Commands, queries, DTOs, validators, mapping profiles/assemblers, service interfaces
- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations, DbContext, repositories, migrations, seed/backfill logic
- **API**
  - `src/VirtualCompany.Api/**`
  - Admin controllers/endpoints, request/response contracts, model binding, OpenAPI annotations if present
- **Web**
  - `src/VirtualCompany.Web/**`
  - Blazor admin pages/components/forms for customer and supplier create/edit/detail/list
- **Tests**
  - `tests/VirtualCompany.Api.Tests/**`
  - Integration/API tests for CRUD and invoice/bill linkage after migration
  - Any web/component test project if present
- **Docs if needed**
  - `README.md`
  - `docs/postgresql-migrations-archive/README.md` only if migration workflow documentation must be updated

Before editing, locate the actual customer/supplier modules and follow existing naming and folder conventions exactly.

# Implementation plan
1. **Discover current implementation**
   - Find Customer and Supplier entities/models, their persistence mappings, API endpoints, UI forms, and tests.
   - Identify whether the project uses:
     - EF Core code-first migrations
     - MediatR/CQRS handlers
     - FluentValidation or data annotations
     - Shared DTOs in `VirtualCompany.Shared`
   - Identify invoice and bill foreign key relationships to customer/supplier and current seeded/mock data setup.

2. **Extend domain and persistence models**
   - Add the new finance fields to Customer and Supplier models using existing type conventions.
   - Prefer types that fit finance data safely:
     - `paymentTerms`: string or enum-backed string per existing style
     - `taxId`: string
     - `creditLimit`: decimal/nullable decimal with appropriate precision
     - `preferredPaymentMethod`: string or enum-backed string per existing style
     - `defaultAccountMapping`: string/JSON/value object depending on current account mapping design; if no existing mapping type exists, use the simplest non-breaking representation already used in the codebase
   - Keep nullability/backward compatibility in mind for existing rows unless acceptance criteria or current patterns require defaults.

3. **Update EF Core configuration and migration**
   - Add columns for both customer and supplier tables.
   - Configure lengths/precision/indexes consistently with current schema conventions.
   - Add indexes on:
     - `company_id`/tenant identifier + `name`
     - `company_id`/tenant identifier + reference column if such a reference/code column exists
   - Ensure migration is non-destructive and does not alter primary/foreign keys used by invoices/bills.
   - If seed/backfill is done in migration or startup seeding, implement it in the project’s established mechanism.

4. **Implement backfill/defaulting**
   - For existing mock/seeded counterparties, populate sensible defaults for the new fields.
   - Use deterministic defaults that will not break validation, for example:
     - payment terms like `Net30` or existing default term
     - preferred payment method like `BankTransfer` if aligned with current seed style
     - `creditLimit` default `0` or null depending on existing validation rules
     - `defaultAccountMapping` to a safe placeholder/default mapping if the app expects one
   - Make sure seeded tenants complete successfully on a fresh database and on migrated databases.

5. **Update application layer contracts**
   - Extend create/update commands, DTOs, query models, and validators for customers and suppliers.
   - Ensure list/get responses include the new fields.
   - Preserve backward compatibility where possible.
   - Add validation only if already consistent with existing patterns; do not introduce arbitrary strictness that could fail valid payloads.

6. **Update Admin API**
   - Ensure create, update, get, and list endpoints for customers and suppliers round-trip all new fields.
   - Update request/response contracts and endpoint mapping.
   - If OpenAPI/Swagger examples exist, update them.
   - Verify tenant/company scoping remains enforced.

7. **Update Admin UI**
   - Add form inputs for all new finance fields in customer and supplier admin screens.
   - Bind fields correctly for create/edit flows.
   - Ensure valid payloads submit without client-side or server-side validation errors.
   - Update detail/list rendering if the UI pattern expects finance fields to be visible after save.
   - Reuse existing input components and validation summary patterns.

8. **Protect invoice/bill linkage**
   - Add or update integration tests that:
     - create or load invoices linked to customers
     - create or load bills linked to suppliers
     - run against migrated schema / seeded data
     - verify linked records still resolve correctly after migration
   - Do not change invoice/bill FK semantics unless absolutely necessary.

9. **Add/adjust automated tests**
   - API tests:
     - customer CRUD with finance fields
     - supplier CRUD with finance fields
     - list/get includes finance fields
   - Migration/integration tests:
     - existing invoices and bills still resolve linked counterparties
     - seeded tenants backfill successfully
   - UI/component tests if present:
     - forms render and submit finance fields

10. **Run validation and fix issues**
   - Build solution
   - Run relevant tests
   - Check migration generation and snapshot consistency
   - Confirm no nullable/reference warnings or serialization issues are introduced

11. **Document assumptions in code comments or PR-style notes**
   - If `defaultAccountMapping` required a temporary representation because no richer account-mapping model exists yet, keep it explicit and non-breaking.
   - Note any follow-up task needed for stronger finance validation or richer mapping semantics.

# Validation steps
Run these after implementation, adapting paths/project names to the actual repo structure:

1. **Restore/build**
   - `dotnet build VirtualCompany.sln`

2. **Run tests**
   - `dotnet test VirtualCompany.sln`
   - If full suite is too slow, at minimum run:
     - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. **Migration verification**
   - Ensure the new EF migration is present and compiles.
   - If the repo supports local DB update, apply migration and verify schema contains:
     - new customer columns
     - new supplier columns
     - expected indexes on company/tenant + name/reference
   - Confirm migration does not drop/recreate customer/supplier tables in a way that would break invoice/bill links.

4. **API verification**
   - Verify customer admin endpoints:
     - create accepts all new fields
     - update persists changes
     - get returns all fields
     - list includes all fields or summary fields per existing API contract
   - Verify supplier admin endpoints with the same checks.

5. **UI verification**
   - Open admin customer and supplier forms.
   - Confirm all finance fields render.
   - Submit valid payloads and verify no validation errors.
   - Reload detail/edit page and confirm values persist.

6. **Integration verification**
   - Run or add automated test coverage proving:
     - pre-existing invoice -> customer relationship still resolves after migration
     - pre-existing bill -> supplier relationship still resolves after migration
     - seeded/mock counterparties are backfilled for seeded tenants

7. **Code quality checks**
   - Ensure tenant scoping remains intact in queries and indexes.
   - Ensure decimal precision and nullability are consistent across entity, DTO, API serialization, and UI binding.
   - Ensure no breaking changes to existing contracts beyond additive fields.

# Risks and follow-ups
- **Unknown current schema shape**: customer/supplier tables or modules may use different names or shared abstractions. Inspect first and align with existing architecture rather than inventing new layers.
- **`defaultAccountMapping` ambiguity**: if no account-mapping model exists yet, choose the least risky additive representation already compatible with the codebase, and keep it easy to evolve later.
- **Validation regressions**: avoid introducing stricter validation that causes valid existing payloads or seeded data to fail.
- **Migration safety**: be careful not to rebuild tables or alter FK columns in a way that breaks invoice/bill relationships.
- **Seed/backfill coupling**: seeded tenants may rely on fixed mock data assumptions; update all relevant seed paths, not just one.
- **UI binding issues**: Blazor forms can fail on nullable decimals/enums/complex objects if bindings are inconsistent; test create and edit flows manually or via component tests.
- **Index selection**: only add indexes that match actual query patterns and existing column names; do not guess a `reference` column if the model uses `code`, `number`, or another identifier.

Follow-up candidates if not already covered by this task:
- stronger finance-specific validation rules for tax IDs/payment terms/payment methods
- richer account-mapping model tied to chart-of-accounts entities
- search/filter enhancements using the new finance fields
- audit events for finance master data changes