# Goal
Implement backlog task **TASK-24.1.3 — Add admin APIs and validation for customer and supplier finance data management** for story **US-24.1 Implement finance counterparty master data for customers and suppliers**.

Deliver a complete vertical slice across persistence, migration, application/API, validation, seeded/backfill data, integration tests, and admin UI so that customer and supplier master data supports finance-specific fields:

- `paymentTerms`
- `taxId`
- `creditLimit`
- `preferredPaymentMethod`
- `defaultAccountMapping`

The implementation must preserve multi-tenant isolation via `company_id`, keep existing invoice/bill links intact after migration, and ensure valid admin create/update/get/list flows work end-to-end for both customers and suppliers.

# Scope
In scope:

- Extend **Customer** and **Supplier** persistence/domain models with the new finance fields.
- Add a **PostgreSQL migration** for schema changes and required indexes on tenant/company identifier plus name/reference lookup columns.
- Add/extend **admin API endpoints** for create, update, get, and list operations for customers and suppliers with the new fields.
- Add **server-side validation** for valid payload handling and field-level errors for invalid payloads.
- Update **admin UI forms/pages/components** to display, edit, submit, and persist all finance fields.
- Add **backfill/seed update logic** so existing mock counterparties receive defaults and seeded tenants migrate successfully.
- Add/extend **integration tests** proving:
  - invoices still resolve linked customers after migration
  - bills still resolve linked suppliers after migration
  - admin APIs round-trip the new fields
- Keep implementation aligned with modular monolith, CQRS-lite, ASP.NET Core, PostgreSQL, and tenant-scoped access patterns.

Out of scope unless required by existing code patterns:

- New mobile UI
- New accounting workflows beyond master data CRUD
- New external integrations
- Broad refactors unrelated to customer/supplier finance data
- Breaking changes to existing invoice/bill contracts unless unavoidable and fully updated

# Files to touch
Inspect the solution first and then update the actual files that match the existing architecture. Likely areas:

- `src/VirtualCompany.Domain/**`
  - customer and supplier entities/value objects/enums
- `src/VirtualCompany.Application/**`
  - commands/queries/DTOs/validators/handlers for customer and supplier admin operations
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations
  - seed/backfill logic
  - repositories/read models if present
- `src/VirtualCompany.Api/**`
  - admin controllers or minimal API endpoint mappings
  - request/response contracts if API layer owns them
- `src/VirtualCompany.Web/**`
  - admin customer/supplier forms, edit pages, list/detail views, validation bindings
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for migration safety and CRUD/list/get/update coverage
- `docs/postgresql-migrations-archive/README.md`
  - only if migration documentation/process needs updating

Also inspect for existing files with names similar to:

- `Customer.cs`, `Supplier.cs`
- `CustomerConfiguration.cs`, `SupplierConfiguration.cs`
- `ApplicationDbContext.cs`, `VirtualCompanyDbContext.cs`
- `CreateCustomer*`, `UpdateCustomer*`, `GetCustomer*`, `ListCustomers*`
- `CreateSupplier*`, `UpdateSupplier*`, `GetSupplier*`, `ListSuppliers*`
- `CustomersController.cs`, `SuppliersController.cs`
- Blazor pages/components under admin/finance/setup/counterparties
- seed/mock tenant bootstrap classes

# Implementation plan
1. **Discover current model and flow before changing anything**
   - Find existing customer and supplier domain/persistence models.
   - Trace how invoices reference customers and bills reference suppliers.
   - Identify current admin API routes and UI forms.
   - Identify validation approach already used in the solution:
     - FluentValidation
     - data annotations
     - custom validators
   - Identify migration mechanism and naming conventions in Infrastructure.
   - Identify seed/mock data initialization path for counterparties.

2. **Extend domain and persistence models**
   - Add the following fields to both customer and supplier models using types consistent with existing conventions:
     - `paymentTerms` → likely string or enum-backed string
     - `taxId` → string
     - `creditLimit` → decimal / numeric
     - `preferredPaymentMethod` → likely string or enum-backed string
     - `defaultAccountMapping` → string or JSON/string reference depending current accounting model
   - Preserve nullability carefully:
     - prefer non-breaking nullable additions at DB level if existing rows exist
     - enforce requiredness in API validation only where acceptance criteria and current UX support it
   - If enums already exist for payment method/terms, reuse them; otherwise prefer stable string fields over premature enum rigidity unless the codebase already standardizes enums.

3. **Update EF Core mappings / database configuration**
   - Add column mappings for all new fields on customer and supplier tables.
   - Ensure appropriate lengths/precision:
     - `taxId`: bounded varchar/text with sensible max length
     - `creditLimit`: numeric with explicit precision/scale
     - `paymentTerms`, `preferredPaymentMethod`, `defaultAccountMapping`: bounded varchar/text as appropriate
   - Add indexes required by acceptance criteria:
     - composite index on `company_id` + customer name and/or reference column
     - composite index on `company_id` + supplier name and/or reference column
   - Do not alter foreign keys used by invoices/bills unless necessary.

4. **Create migration**
   - Generate or hand-author a migration that:
     - adds the five finance columns to customer table
     - adds the five finance columns to supplier table
     - creates the required composite indexes
     - includes safe defaults/backfill SQL or invokes application-level backfill path if that is the established pattern
   - Make migration idempotent within normal EF migration flow.
   - Ensure down migration is reasonable if the project maintains reversible migrations.

5. **Implement backfill for existing mock/seeded counterparties**
   - Update seed data or startup seeding logic so existing mock customers/suppliers get default values.
   - If seeded tenants are created through fixtures or migration-time SQL, update those paths too.
   - Use deterministic defaults, for example:
     - `paymentTerms`: `"Net 30"` or existing standard
     - `preferredPaymentMethod`: `"BankTransfer"` or existing standard
     - `creditLimit`: `0` or a sensible seeded value
     - `defaultAccountMapping`: existing receivable/payable mapping code if available
     - `taxId`: empty/null unless a seeded fake value is already used
   - Ensure defaults do not violate validation rules.

6. **Update application contracts and handlers**
   - Extend create/update request DTOs and response DTOs for customers and suppliers to include all five fields.
   - Extend get/list projections so the new fields are returned.
   - Update command/query handlers and mapping code.
   - Ensure list endpoints include the fields if current API returns full records; if list uses summary DTOs, include finance fields only if consistent with current UX/API patterns.

7. **Add validation**
   - Implement or extend validators for create/update operations.
   - Validation should reject clearly invalid payloads while allowing valid payloads without false positives.
   - Suggested validation rules unless existing conventions differ:
     - `paymentTerms`: max length and optional allowed values if standardized
     - `taxId`: max length, trimmed, optional format validation only if current domain supports region-specific rules
     - `creditLimit`: non-negative
     - `preferredPaymentMethod`: max length and optional allowed values if standardized
     - `defaultAccountMapping`: max length / required format if mapping codes exist
   - Avoid over-constraining `taxId` across regions unless the app already has compliance-region-aware validation.

8. **Update admin API endpoints**
   - Ensure admin endpoints support:
     - create customer
     - update customer
     - get customer
     - list customers
     - create supplier
     - update supplier
     - get supplier
     - list suppliers
   - Preserve tenant scoping using `company_id`/membership context.
   - Return proper validation responses for invalid payloads.
   - Keep route patterns and response envelopes consistent with existing API style.

9. **Update admin UI**
   - Find customer and supplier admin forms in Blazor web app.
   - Add inputs for:
     - payment terms
     - tax ID
     - credit limit
     - preferred payment method
     - default account mapping
   - Bind fields to request models and display validation messages.
   - Ensure edit and create forms both round-trip values.
   - Ensure detail/list pages show the new fields where appropriate and useful.
   - Do not introduce validation errors for valid payloads.

10. **Protect invoice/bill linkage behavior**
    - Verify migration does not rename or recreate customer/supplier primary keys or relationship columns.
    - If projections or includes changed, ensure invoice and bill retrieval still hydrate linked customer/supplier records.
    - Add explicit integration tests covering pre-existing linked records after migration.

11. **Add/extend tests**
    - Add integration tests for:
      - customer create/get/update/list with finance fields
      - supplier create/get/update/list with finance fields
      - migration/backfill success for seeded tenants
      - invoice linked customer resolution after migration
      - bill linked supplier resolution after migration
      - validation failure cases such as negative credit limit
    - Prefer end-to-end API tests over isolated unit tests where acceptance criteria are integration-focused.

12. **Run build/test and fix issues**
    - Run targeted tests first, then full solution tests if feasible.
    - Resolve nullable warnings, mapping issues, and serialization mismatches.
    - Confirm migration applies cleanly to a fresh DB and to an existing seeded DB state if test harness supports both.

13. **Document assumptions in code comments or PR notes**
    - If `defaultAccountMapping` is stored as a simple string code for now, keep that explicit.
    - If payment terms/payment method are strings pending future normalization, keep naming and validation future-friendly.

# Validation steps
1. **Build**
   - Run:
     - `dotnet build`
   - Ensure all projects compile.

2. **Run tests**
   - Run:
     - `dotnet test`
   - If the suite is large, first run targeted API/integration tests for counterparties and finance flows.

3. **Migration verification**
   - Confirm the new migration:
     - adds all five fields to both customer and supplier tables
     - creates composite indexes on `company_id` plus name/reference columns
   - Verify schema matches EF model.

4. **CRUD API verification**
   - For both customers and suppliers, verify:
     - create persists all finance fields
     - get returns all finance fields
     - update changes all finance fields
     - list includes records with the new fields as expected

5. **Validation verification**
   - Confirm valid payloads do not fail.
   - Confirm invalid payloads return field-level validation errors, especially for:
     - negative `creditLimit`
     - overlong strings if max lengths are enforced

6. **Migration compatibility verification**
   - Using integration tests or fixture setup, verify:
     - existing invoices still resolve linked customer records after migration
     - existing bills still resolve linked supplier records after migration

7. **Backfill verification**
   - Confirm seeded/mock tenants complete startup/migration successfully.
   - Confirm existing mock counterparties have populated defaults where required.

8. **UI verification**
   - In the Blazor admin UI, verify create/edit forms for customers and suppliers:
     - display all finance fields
     - submit successfully with valid values
     - reload persisted values correctly
     - show validation messages only for invalid inputs

9. **Tenant isolation verification**
   - Confirm customer/supplier list/get/update remain scoped to the active company and do not leak cross-tenant data.

# Risks and follow-ups
- **Risk: unclear existing customer/supplier model location**
  - Mitigation: inspect solution structure first and follow existing patterns rather than inventing new layers.

- **Risk: over-constraining finance field validation**
  - Mitigation: keep validation practical and region-agnostic unless the codebase already models country/compliance-specific rules.

- **Risk: breaking invoice/bill relationships during migration**
  - Mitigation: additive migration only; do not modify PK/FK columns or recreate tables.

- **Risk: `defaultAccountMapping` semantics may be underspecified**
  - Mitigation: store as a stable string/reference now and note future normalization if a chart-of-accounts module arrives later.

- **Risk: list DTOs may intentionally be summary-only**
  - Mitigation: preserve existing API shape conventions; only add fields where appropriate, but ensure acceptance criteria for admin operations are satisfied.

- **Risk: seeded data paths may exist in multiple places**
  - Mitigation: search for all mock tenant/bootstrap/fixture seeders and update each relevant path.

Follow-ups to note if not fully addressed by this task:
- Normalize `paymentTerms` and `preferredPaymentMethod` into reference data/enums if finance domain expands.
- Introduce stronger `taxId` validation by compliance region when regional tax rules are implemented.
- Link `defaultAccountMapping` to a future chart-of-accounts/account-mapping module instead of free text.
- Add audit events for counterparty finance field changes if audit coverage is not already present.