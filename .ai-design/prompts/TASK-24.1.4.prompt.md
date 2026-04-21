# Goal
Implement backlog task **TASK-24.1.4 — Build admin UI screens for viewing and editing finance counterparty details** for story **US-24.1 Implement finance counterparty master data for customers and suppliers**.

Deliver an end-to-end vertical slice across domain, persistence, migration, API, backfill, integration tests, and Blazor admin UI so that admins can create, view, edit, and list **customers** and **suppliers** with the new finance fields:

- `paymentTerms`
- `taxId`
- `creditLimit`
- `preferredPaymentMethod`
- `defaultAccountMapping`

The implementation must satisfy all acceptance criteria, preserve tenant/company scoping, and avoid breaking existing invoice/bill links after migration.

# Scope
In scope:

- Extend customer and supplier persistence/domain/application contracts with the new finance fields.
- Add a PostgreSQL migration for the new columns and required indexes on tenant/company identifier plus name/reference columns.
- Add/extend backfill logic for seeded/mock counterparties with sensible defaults.
- Ensure admin API supports create, update, get, and list for customers and suppliers including the new fields.
- Build/update Blazor admin UI screens/forms for customer and supplier create/edit/detail/list flows.
- Add/adjust automated integration tests proving:
  - invoices still resolve linked customers after migration
  - bills still resolve linked suppliers after migration
  - valid payloads persist through API/UI without validation errors
  - seeded tenants complete backfill successfully

Out of scope unless required by existing patterns:

- New mobile UI
- New accounting workflows beyond counterparty master data
- Broad redesign of admin navigation
- Advanced validation/business rules not already established in the module
- Refactoring unrelated modules

# Files to touch
Inspect the solution first and then update the actual matching files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - customer/supplier entities or aggregates
  - value objects/enums if payment method or payment terms are modeled strongly
- `src/VirtualCompany.Application/**`
  - DTOs, commands, queries, validators, handlers
  - admin API contracts for customer/supplier CRUD
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repository/query implementations
  - migrations / seed / backfill logic
- `src/VirtualCompany.Api/**`
  - admin controller/endpoints for customers and suppliers
  - request/response models if API layer owns them
- `src/VirtualCompany.Web/**`
  - Blazor pages/components for admin customer/supplier list/detail/edit/create
  - shared form components if present
  - client service layer used by web app
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for migration compatibility and CRUD
  - seeded tenant/backfill tests
- `docs/postgresql-migrations-archive/README.md`
  - only if this repo requires migration documentation updates

Also inspect for existing finance/counterparty-related files before creating new ones. Prefer extending established module structure over inventing a new one.

# Implementation plan
1. **Discover existing counterparty implementation**
   - Locate current customer and supplier models, DB mappings, API endpoints, and Blazor admin pages.
   - Identify how tenant/company scoping is enforced.
   - Identify how invoices reference customers and bills reference suppliers today.
   - Identify existing seed/mock data and any backfill/migration conventions.

2. **Extend domain and persistence models**
   - Add the five finance fields to both customer and supplier models.
   - Use nullable vs required semantics consistent with existing data and acceptance criteria; do not break existing records.
   - If enums already exist for payment methods, reuse them; otherwise prefer a simple string field unless the codebase clearly standardizes enums.
   - Ensure serialization and mapping are consistent across layers.

3. **Create database migration**
   - Add migration introducing the new columns to customer and supplier tables.
   - Preserve existing PK/FK relationships so invoice/customer and bill/supplier links remain intact.
   - Add indexes combining tenant/company identifier with name and/or reference columns per current schema conventions.
   - Do not rename or recreate customer/supplier tables unless absolutely necessary.
   - If seeded/mock counterparties exist, include migration-safe backfill or post-migration seed update logic.

4. **Implement backfill/defaulting**
   - Add logic to populate default values for existing mock/seeded counterparties for seeded tenants.
   - Keep defaults deterministic and low-risk, for example:
     - `paymentTerms`: sensible default like `Net 30`
     - `preferredPaymentMethod`: existing standard default if one exists
     - `creditLimit`: `0` or another established safe default
     - `defaultAccountMapping`: stable placeholder/default mapping per seed conventions
     - `taxId`: only populate if mock seed conventions already support it; otherwise safe mock value
   - Make backfill idempotent if possible.

5. **Update application layer and API**
   - Extend create/update/get/list DTOs and handlers for customers and suppliers.
   - Ensure admin API returns and accepts all finance fields.
   - Preserve existing validation behavior; valid payloads must not fail.
   - Keep tenant/company authorization and filtering intact.
   - Ensure list endpoints include enough fields for admin grids.

6. **Update Blazor admin UI**
   - Add the new finance fields to customer and supplier forms.
   - Ensure fields are bound correctly and persisted on save.
   - Update detail/read-only views and list/grid columns if appropriate.
   - Reuse existing form patterns, validation summaries, edit forms, and API client abstractions.
   - Keep UX simple and consistent with current admin screens.

7. **Protect migration compatibility**
   - Add/extend integration tests that start from pre-migration-like seeded data or equivalent fixture state.
   - Verify invoices still resolve linked customer records after migration.
   - Verify bills still resolve linked supplier records after migration.
   - Verify CRUD endpoints round-trip the new finance fields.
   - Verify backfill completes for seeded tenants.

8. **Keep implementation cohesive**
   - Avoid duplicate customer/supplier form logic if a shared component pattern already exists.
   - Avoid introducing breaking API contract changes beyond additive fields.
   - Follow existing naming, namespaces, and project boundaries.

# Validation steps
Run these after implementation and fix any failures:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Targeted verification**
   - Confirm migration applies cleanly on a fresh database.
   - Confirm migration applies cleanly on a database with existing seeded/mock customer and supplier data.
   - Confirm customer admin API:
     - create with all finance fields
     - update with all finance fields
     - get returns all finance fields
     - list includes all finance fields or the expected subset used by UI
   - Confirm supplier admin API with the same coverage.
   - Confirm Blazor forms for customer and supplier:
     - render all finance fields
     - submit valid payloads without validation errors
     - reload persisted values correctly
   - Confirm invoice/customer and bill/supplier integration tests pass after migration/backfill.

4. **Code quality checks**
   - Ensure no tenant/company scoping regressions.
   - Ensure no nullability/runtime mapping exceptions.
   - Ensure indexes added by migration match actual table/column names.

# Risks and follow-ups
- **Schema mismatch risk:** actual table/entity names may differ from “customer/supplier”; inspect first and adapt carefully.
- **Migration compatibility risk:** recreating tables or changing keys could break invoice/bill relationships; use additive migration only.
- **Validation risk:** if UI uses strict data annotations or FluentValidation, new fields may accidentally become required; keep valid payloads accepted.
- **Seed/backfill risk:** seeded tenant data may be created outside migrations; update whichever seed path the repo actually uses.
- **Enum risk:** introducing enums for payment method/terms may create serialization friction; prefer existing conventions.
- **UI duplication risk:** customer and supplier screens may share patterns; reuse components if already present, but do not over-refactor.
- **Index risk:** acceptance criteria require indexes on tenant/company identifier plus name or reference columns; verify exact existing identifier column names before generating migration.

Follow-ups if not already covered by this task:
- Add richer finance-specific validation rules and lookup lists for payment methods/terms.
- Add audit events for counterparty finance field changes.
- Add role-based restrictions for finance-only editing if the admin area already supports granular permissions.