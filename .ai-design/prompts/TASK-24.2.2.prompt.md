# Goal
Implement backlog task **TASK-24.2.2 — Add payment create and list APIs with tenant scoping and request validation** for story **US-24.2 Implement payment records for incoming and outgoing cash movements**.

Deliver a vertical slice that adds:
- a persisted **Payment** domain/entity model
- database migration and indexes
- create/list backend APIs
- tenant/company scoping enforcement
- request validation
- seed/bootstrap generation of realistic payment data
- minimal payment inspection UI support for header/detail display

The implementation must satisfy these acceptance criteria:
- A Payment entity exists with `paymentType`, `amount`, `currency`, `paymentDate`, `method`, `status`, and `counterpartyReference` fields.
- Database constraints reject payment records with amount less than or equal to zero and invalid `paymentType` values.
- Payment APIs support creation and listing of both incoming and outgoing payments scoped to a tenant/company.
- Payment inspection UI shows payment header fields, status, amount, currency, and linked counterparty reference.
- A migration creates payment storage and indexes on tenant/company identifier, status, and paymentDate.
- Seed/bootstrap logic generates realistic payment records for existing companies and completes without duplicate key errors.

# Scope
In scope:
- Add `Payment` aggregate/entity and any supporting enums/value objects.
- Persist payments in PostgreSQL using the existing architecture and conventions.
- Add EF Core configuration and migration with:
  - table creation
  - check constraints for positive amount and valid payment type
  - indexes on company/tenant identifier, status, and payment date
- Add application-layer commands/queries for:
  - create payment
  - list payments for current company
- Add API endpoints in ASP.NET Core for:
  - `POST` create
  - `GET` list
- Enforce tenant scoping using the existing company context pattern already used elsewhere in the solution.
- Add request/DTO validation with clear field-level errors.
- Add seed/bootstrap logic for realistic incoming/outgoing payments per existing company, avoiding duplicate key issues and ensuring idempotent behavior.
- Add or update a minimal web UI inspection/detail surface so payment header fields are visible.

Out of scope unless required by existing patterns:
- payment editing/deletion
- external payment provider integrations
- approval workflows for payments
- full dashboard analytics
- mobile-specific payment UX
- advanced filtering beyond what is needed for list support
- linked counterparty entity modeling beyond storing/displaying `counterpartyReference`

# Files to touch
Inspect the solution first and then update the actual matching files/patterns. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add `Payment` entity
  - add `PaymentType` enum
  - add `PaymentStatus` enum if not already present
  - add `PaymentMethod` enum if appropriate
- `src/VirtualCompany.Application/**`
  - create payment command + handler
  - list payments query + handler
  - request/response DTOs
  - validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext updates
  - migration files
  - seed/bootstrap logic
  - repository/query implementation if used
- `src/VirtualCompany.Api/**`
  - payments controller or minimal API endpoint mapping
  - request binding and auth/tenant scoping wiring
- `src/VirtualCompany.Web/**`
  - payment inspection/detail page/component
  - payment list/detail DTO consumption if already wired through API clients
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for create/list and tenant scoping
- possibly:
  - `README.md` or docs if migration/seed instructions need updating

Before coding, search for existing patterns for:
- tenant/company scoped entities
- create/list endpoint conventions
- FluentValidation or equivalent validation
- EF Core migrations
- bootstrap/seed services
- Blazor detail pages

# Implementation plan
1. **Discover existing architecture patterns**
   - Inspect how tenant/company context is resolved in API and application layers.
   - Find an existing entity with create/list APIs and migration patterns to mirror.
   - Find how enums are stored in the database and serialized in API responses.
   - Find how seed/bootstrap logic is structured and how idempotency is handled.

2. **Add the domain model**
   - Create a `Payment` entity under the appropriate domain module.
   - Include at minimum:
     - `Id`
     - `CompanyId` or tenant identifier matching project conventions
     - `PaymentType`
     - `Amount`
     - `Currency`
     - `PaymentDate`
     - `Method`
     - `Status`
     - `CounterpartyReference`
     - `CreatedAt` / `UpdatedAt` if standard
   - Use enums or constrained string-backed values consistent with the codebase.
   - Ensure the model supports both incoming and outgoing payments.

3. **Add persistence mapping**
   - Register `Payment` in the DbContext.
   - Add EF Core configuration:
     - table name consistent with naming conventions
     - required fields
     - max lengths for string fields
     - enum conversions if needed
     - company foreign key if appropriate
   - Add DB constraints:
     - amount must be `> 0`
     - payment type must be one of the allowed values
   - Add indexes:
     - company/tenant identifier
     - status
     - payment date
   - Prefer a composite index involving company + payment date if that matches query patterns, but still satisfy the explicit acceptance criteria.

4. **Create migration**
   - Generate or hand-author migration files in the project’s migration style.
   - Ensure the migration creates the payment table and indexes.
   - Verify the generated SQL/check constraints are valid for PostgreSQL.
   - If enum values are stored as strings, ensure the check constraint matches the persisted representation.

5. **Implement application-layer create flow**
   - Add a create command and handler.
   - Input should include:
     - payment type
     - amount
     - currency
     - payment date
     - method
     - status
     - counterparty reference
   - Resolve company/tenant from the authenticated request context, not from arbitrary client input unless existing conventions require both and server verifies them.
   - Validate:
     - amount > 0
     - required fields present
     - valid enum values
     - currency format/length per existing standards
     - reasonable max length for counterparty reference
   - Persist and return a response DTO.

6. **Implement application-layer list flow**
   - Add a list query and handler.
   - Scope results to the current company only.
   - Support listing both incoming and outgoing payments.
   - Include fields needed by the UI:
     - header/identifier
     - status
     - amount
     - currency
     - payment date
     - method
     - counterparty reference
     - payment type
   - Add ordering by payment date descending unless existing conventions differ.

7. **Expose API endpoints**
   - Add endpoints under the project’s route conventions, likely something like:
     - `POST /api/payments`
     - `GET /api/payments`
   - Require authenticated company context.
   - Return proper status codes:
     - `201 Created` or project-standard success response for create
     - `200 OK` for list
     - `400 Bad Request` for validation failures
     - `403/404` for cross-tenant access attempts per existing conventions
   - Ensure no endpoint can read or create payments for another company.

8. **Add seed/bootstrap logic**
   - Extend bootstrap/seed services to generate realistic payment records for existing companies.
   - Include both incoming and outgoing examples.
   - Use deterministic/idempotent logic:
     - check for existing seeded records before inserting, or
     - use stable identifiers/business keys if the seed framework supports them
   - Avoid duplicate key errors on repeated runs.
   - Keep generated data realistic in amount, date spread, status, method, and counterparty reference.

9. **Add/update payment inspection UI**
   - Find the existing payment-related page or create a minimal inspection/detail view in the web app.
   - Display:
     - payment header fields
     - status
     - amount
     - currency
     - linked counterparty reference
   - Reuse existing API client/query patterns.
   - Keep UI minimal and consistent with current Blazor styling/components.

10. **Add tests**
   - Add API/integration tests covering:
     - create payment success
     - create payment rejects amount <= 0
     - create payment rejects invalid payment type
     - list returns only current company payments
     - list includes both incoming and outgoing payments
     - seed/bootstrap can run repeatedly without duplicate key errors if there is a test harness for seeding
   - If migration tests exist, add coverage for schema creation or at least verify app startup/migration succeeds.

11. **Keep implementation aligned with clean architecture**
   - No direct DB access from controllers/UI.
   - Keep tenant scoping enforced in application/query layer and API boundary.
   - Follow CQRS-lite patterns already present in the solution.

# Validation steps
Run these after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration verification**
   - Confirm the new migration is included in the infrastructure project.
   - Verify the payment table contains:
     - required columns
     - positive amount check constraint
     - valid payment type check constraint
     - indexes on company/tenant identifier, status, and payment date

4. **Manual API verification**
   - Create an incoming payment via API.
   - Create an outgoing payment via API.
   - Attempt to create a payment with `amount = 0` and confirm validation/db rejection.
   - Attempt to create a payment with invalid `paymentType` and confirm validation/db rejection.
   - List payments and confirm only current company records are returned.

5. **Tenant isolation verification**
   - Using two companies/tenants, create payments in both.
   - Confirm list endpoint for company A never returns company B payments.
   - Confirm any direct lookup/detail route, if added or already present, is also tenant-scoped.

6. **Seed verification**
   - Run bootstrap/seed logic twice.
   - Confirm realistic payment records exist for existing companies.
   - Confirm no duplicate key errors occur on repeated runs.

7. **UI verification**
   - Open the payment inspection/detail UI.
   - Confirm it shows header fields, status, amount, currency, and counterparty reference.

# Risks and follow-ups
- **Tenant scoping gaps:** The biggest risk is relying only on controller-level scoping. Enforce company filtering in queries/handlers too.
- **Enum persistence mismatch:** If enums are serialized differently between API, EF, and PostgreSQL check constraints, invalid values may slip through or valid values may fail. Keep one canonical representation.
- **Seed idempotency:** Randomized seed generation can cause duplicate key or duplicate business data issues. Use deterministic guards.
- **UI dependency mismatch:** The acceptance criteria mention inspection UI, but the task title is API-focused. Implement the smallest viable UI update needed to satisfy the criterion without expanding scope.
- **Migration portability:** PostgreSQL check constraint syntax and enum/string conversions must match the provider exactly.
- **Validation duplication:** Prefer application validation plus DB constraints; do not rely on only one layer.

Follow-ups to note in code comments or task notes if not already covered elsewhere:
- add payment detail endpoint if the UI needs a dedicated fetch route
- add filtering/paging for payment list if dataset growth is expected
- consider linking `counterpartyReference` to a future customer/vendor domain model
- consider audit events for payment creation in a later task if audit infrastructure already exists