# Goal
Implement backlog task **TASK-24.2.1 — Create payment domain model, repository, and persistence constraints for cash movement records** for story **US-24.2 Implement payment records for incoming and outgoing cash movements**.

Deliver a tenant-scoped payment foundation in the existing .NET modular monolith that includes:
- a `Payment` domain entity with required fields
- persistence mapping and repository/query support
- database migration with constraints and indexes
- API support for create and list operations for incoming/outgoing payments
- enough UI wiring for payment inspection/header display
- seed/bootstrap generation of realistic payment records for existing companies without duplicate key failures

Keep the implementation aligned with the architecture:
- shared-schema multi-tenancy using `company_id`
- ASP.NET Core + PostgreSQL
- clean module boundaries
- CQRS-lite application layer
- no direct DB access from UI

# Scope
In scope:
- Add a payment aggregate/entity in the domain layer with:
  - `paymentType`
  - `amount`
  - `currency`
  - `paymentDate`
  - `method`
  - `status`
  - `counterpartyReference`
  - tenant/company ownership
- Model valid payment direction/type values for incoming and outgoing cash movement records
- Enforce DB constraints for:
  - `amount > 0`
  - valid `paymentType`
- Add EF Core persistence configuration and migration
- Add indexes on:
  - `company_id`
  - `status`
  - `payment_date`
- Add repository and/or query access patterns used by application services
- Add create/list payment APIs scoped to company
- Add minimal payment inspection UI support to show:
  - header fields
  - status
  - amount
  - currency
  - linked counterparty reference
- Add seed/bootstrap logic for realistic payment records for existing companies
- Ensure seeding is idempotent and does not produce duplicate key errors
- Add tests covering domain validation, persistence constraints, tenant scoping, and API behavior where practical

Out of scope unless required by existing patterns:
- full payment editing workflow
- reconciliation logic
- external payment provider integrations
- approval workflows for payments
- advanced search/filter UX beyond what is needed for listing/inspection
- accounting ledger postings

# Files to touch
Inspect the solution structure first and adapt to actual conventions. Likely files/folders to touch include:

- `src/VirtualCompany.Domain/**`
  - add `Payment` entity
  - add enums/value objects for payment type/status/method if the codebase uses them
- `src/VirtualCompany.Application/**`
  - create commands/queries for payment creation and listing
  - DTOs/view models for API/UI consumption
  - validators/handlers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext updates
  - repository implementation
  - migration files
  - seed/bootstrap logic
- `src/VirtualCompany.Api/**`
  - payment endpoints/controllers/minimal APIs
  - request/response contracts if API layer owns them
- `src/VirtualCompany.Web/**`
  - payment list/detail/inspection UI components/pages
- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests
- potentially additional test projects if present for domain/application/infrastructure tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration and seeding conventions before making changes.

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how current tenant-owned entities are modeled across Domain, Application, Infrastructure, Api, and Web.
   - Reuse existing conventions for:
     - base entity IDs
     - `company_id` scoping
     - enum persistence
     - migrations
     - seed/bootstrap execution
     - API routing and authorization
     - Blazor page/component structure
   - Identify whether the project uses:
     - MediatR or direct handlers
     - repositories vs DbContext in handlers
     - controllers vs minimal APIs
     - FluentValidation or custom validation

2. **Add the payment domain model**
   - Create a `Payment` entity in the domain layer.
   - Include at minimum:
     - `Id`
     - `CompanyId`
     - `PaymentType`
     - `Amount`
     - `Currency`
     - `PaymentDate`
     - `Method`
     - `Status`
     - `CounterpartyReference`
     - created/updated timestamps if standard in the project
   - Prefer strongly typed enums/value objects for:
     - payment type: incoming/outgoing
     - payment status
     - payment method
   - Add domain guard clauses for obviously invalid state where consistent with project style, especially:
     - amount must be greater than zero
     - required fields cannot be empty
   - Keep domain naming consistent with acceptance criteria and existing ubiquitous language.

3. **Add persistence mapping and constraints**
   - Register `Payment` in the DbContext.
   - Add EF Core configuration with:
     - table name following project conventions, likely `payments`
     - primary key
     - `company_id` foreign key/reference pattern if applicable
     - column types suitable for PostgreSQL
     - amount precision/scale appropriate for money values
     - required constraints for required fields
   - Add database-level check constraints for:
     - `amount > 0`
     - `payment_type` restricted to valid values
   - Add indexes for:
     - `company_id`
     - `status`
     - `payment_date`
   - If common query patterns suggest it, consider a composite index involving company scope, but do not replace the acceptance-criteria indexes.

4. **Add repository/query support**
   - Implement repository abstraction only if that is the established pattern; otherwise follow existing application query style.
   - Ensure create and list operations are always company-scoped.
   - Listing should support both incoming and outgoing payments, ideally via optional filter by `paymentType`.
   - Prevent cross-tenant access by requiring `companyId` in all repository/query methods.

5. **Add application layer commands/queries**
   - Create a command for payment creation.
   - Create a query for listing payments for a company.
   - Add request validation for:
     - positive amount
     - valid payment type
     - required currency/date/method/status
   - Map domain entities to response DTOs suitable for API and UI.
   - Keep command/query handlers free of UI concerns.

6. **Add API endpoints**
   - Implement endpoints to:
     - create a payment
     - list payments for the current tenant/company
   - Ensure tenant/company context is resolved using existing auth/membership patterns.
   - Return safe errors for invalid input and forbidden/not-found for cross-tenant access patterns.
   - Support incoming and outgoing records through request payload and/or query filter.
   - Keep route naming consistent with existing modules, e.g. `/api/companies/{companyId}/payments` only if that matches current conventions.

7. **Add payment inspection UI**
   - Add or extend a payment detail/inspection page/component in Blazor.
   - Show at minimum:
     - payment header fields
     - status
     - amount
     - currency
     - linked counterparty reference
   - If no payment UI exists yet, implement the smallest coherent list/detail flow needed to satisfy acceptance criteria.
   - Reuse existing layout/components for entity detail headers and status badges.

8. **Add migration**
   - Generate or hand-author a migration creating payment storage.
   - Ensure migration includes:
     - payments table
     - check constraints
     - indexes on `company_id`, `status`, `payment_date`
   - Verify migration naming and placement follow repository conventions.
   - If migration snapshots are used, update them correctly.

9. **Add seed/bootstrap logic**
   - Extend existing bootstrap/seed process to create realistic payment records for existing companies.
   - Generate a mix of:
     - incoming payments, e.g. customer receipts
     - outgoing payments, e.g. vendor bills, payroll-like disbursements, subscriptions
   - Ensure idempotency:
     - do not insert duplicates on repeated runs
     - do not reuse fixed IDs in a way that causes duplicate key errors
     - use deterministic uniqueness keys only if the project already has a seed-upsert pattern
   - Keep seeded data tenant-scoped and believable.

10. **Add tests**
   - Add tests for:
     - domain validation or constructor guards
     - persistence constraints if integration tests exist for PostgreSQL/EF
     - API create/list behavior
     - tenant scoping
     - invalid amount rejection
     - invalid payment type rejection
     - seed/bootstrap idempotency if there is an existing test harness for seeding
   - Prefer integration tests for DB constraints because acceptance criteria explicitly require database rejection.

11. **Document assumptions in code comments only where needed**
   - Avoid broad documentation churn.
   - If enum/string values are persisted, keep them explicit and stable.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration artifacts:
   - confirm the payment table is created with:
     - required columns
     - check constraint for positive amount
     - check constraint or equivalent restriction for valid `payment_type`
     - indexes on `company_id`, `status`, `payment_date`

4. Validate API behavior manually or via tests:
   - create an incoming payment for a company
   - create an outgoing payment for a company
   - list all payments for that company
   - list filtered incoming/outgoing payments if implemented
   - confirm another tenant/company cannot access them

5. Validate DB constraint enforcement:
   - attempt to persist a payment with `amount <= 0`
   - attempt to persist a payment with invalid `paymentType`
   - confirm the database rejects both

6. Validate UI:
   - open payment inspection/detail view
   - confirm header fields, status, amount, currency, and counterparty reference render correctly

7. Validate seed/bootstrap:
   - run bootstrap/seed once and confirm payments are created for existing companies
   - run bootstrap/seed again and confirm it completes without duplicate key errors
   - confirm seeded records include both incoming and outgoing examples

# Risks and follow-ups
- **Pattern mismatch risk:** The repository may already favor direct DbContext usage or a CQRS handler pattern. Match existing conventions rather than introducing a new abstraction style.
- **Enum persistence risk:** If enums are stored as strings, DB check constraints must match exact persisted values. Keep values stable and explicit.
- **Money precision risk:** Choose decimal precision carefully for PostgreSQL and EF Core to avoid rounding issues.
- **Tenant isolation risk:** Every query and endpoint must enforce `company_id` scoping; do not rely on client-provided IDs alone.
- **Seed idempotency risk:** Naive seeding can create duplicates or key collisions. Reuse existing seed-upsert patterns if available.
- **UI scope risk:** If no payment screens exist, keep UI additions minimal and focused on acceptance criteria rather than building a full payments module.
- **Migration compatibility risk:** Review existing migration guidance in `docs/postgresql-migrations-archive/README.md` before generating files.

Potential follow-ups after this task:
- payment detail endpoint if not added now
- richer filtering/sorting for payment lists
- links from payments to invoices/vendors/customers once those domains exist
- audit events for payment creation and status changes
- approval integration for high-value outgoing payments