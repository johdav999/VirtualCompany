# Goal
Implement backlog task **TASK-24.2.4 — Add migration and seed generation for payment tables and indexes** for story **US-24.2 Implement payment records for incoming and outgoing cash movements**.

Deliver the database and bootstrap foundation for payments in the existing .NET modular monolith so that:
- a tenant/company-scoped **Payment** entity can be persisted,
- the database enforces core validity constraints,
- storage is created via migration with required indexes,
- seed/bootstrap logic generates realistic payment records for existing companies,
- seeding is idempotent and completes without duplicate key errors.

This task is specifically about **migration + seed generation**, but implementation must align with the acceptance criteria and not block the related API/UI work.

# Scope
In scope:
- Add persistence support for a **payments** table in PostgreSQL.
- Add database constraints for:
  - `amount > 0`
  - valid `payment_type` values for incoming/outgoing payments
- Add indexes on:
  - tenant/company identifier
  - status
  - payment date
- Add EF Core configuration and migration artifacts if this repo uses EF migrations; otherwise follow the project’s existing PostgreSQL migration approach.
- Add bootstrap/seed logic to generate realistic payment records for existing companies.
- Ensure seed logic is **idempotent** and avoids duplicate key errors on repeated runs.
- Keep all payment data tenant/company scoped.

Out of scope unless required to keep build green:
- Full payment API implementation
- Full payment UI implementation
- Advanced business workflows around approvals/reconciliation
- Nonessential refactors outside payment persistence/seeding

Implementation constraints:
- Follow existing project conventions first; inspect the current migration and seed patterns before coding.
- Prefer minimal, clean changes in the correct layer boundaries:
  - Domain: entity/value definitions if needed
  - Infrastructure: EF mapping, migrations, seed/bootstrap
- Do not introduce direct cross-layer shortcuts.
- Preserve multi-tenant shared-schema design using `company_id`.
- Use realistic but deterministic-enough seed behavior where possible for repeatability.

# Files to touch
Inspect the repository and update the actual matching files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - Add or update `Payment` entity and any related enums/constants if not already present.
- `src/VirtualCompany.Infrastructure/**`
  - DbContext
  - EntityTypeConfiguration for Payment
  - migration files / SQL migration scripts
  - bootstrap/seed services
- `src/VirtualCompany.Api/**`
  - startup/registration only if seed/bootstrap wiring lives here
- `tests/**`
  - add or update tests covering migration constraints and seed idempotency if test patterns already exist

Also inspect:
- `docs/postgresql-migrations-archive/README.md`
- `README.md`

Use those docs to determine whether the repo prefers:
- EF Core generated migrations,
- hand-authored SQL migrations,
- or a hybrid approach.

# Implementation plan
1. **Discover existing patterns before editing**
   - Inspect:
     - Infrastructure persistence setup
     - DbContext and entity configurations
     - existing migration mechanism
     - existing seed/bootstrap services
     - any company-scoped entity examples
   - Identify naming conventions for:
     - table names
     - snake_case vs PascalCase columns
     - enum storage strategy
     - timestamps
     - UUID generation
     - indexes and check constraints

2. **Add/confirm the Payment domain model**
   - Ensure a `Payment` entity exists with fields required by acceptance criteria:
     - `paymentType`
     - `amount`
     - `currency`
     - `paymentDate`
     - `method`
     - `status`
     - `counterpartyReference`
   - Ensure it is company-scoped with `company_id` / `CompanyId`.
   - If the domain model does not exist yet, add the minimal entity needed for persistence.
   - Prefer strongly typed enums/value objects if consistent with the codebase.

3. **Add persistence mapping**
   - Configure the Payment entity in Infrastructure.
   - Map to PostgreSQL with explicit constraints:
     - primary key
     - `company_id` required
     - `amount` numeric/decimal with appropriate precision and scale
     - `currency` bounded length
     - `counterparty_reference` bounded length if conventions exist
   - Add a check constraint enforcing `amount > 0`.
   - Add a check constraint enforcing valid `payment_type` values.
     - If enums are stored as strings, constrain allowed string values.
     - If enums are stored as ints, constrain allowed numeric values.
   - Add indexes for:
     - `company_id`
     - `status`
     - `payment_date`
   - If common query patterns suggest it and conventions allow, consider a composite index such as `(company_id, payment_date)` only if it does not conflict with the explicit acceptance criteria and existing standards.

4. **Create the migration**
   - Generate or author the migration according to repo conventions.
   - Migration must create:
     - `payments` table
     - required constraints
     - required indexes
   - Ensure down/revert path is valid if the project supports rollback.
   - Keep migration names descriptive and tied to TASK-24.2.4 if conventions allow.

5. **Implement seed/bootstrap generation**
   - Find the existing bootstrap entry point for company data seeding.
   - Add payment generation for existing companies.
   - Generate realistic records covering both:
     - incoming payments
     - outgoing payments
   - Use plausible values:
     - positive amounts
     - company/default currency where available
     - recent payment dates
     - varied methods/statuses
     - realistic counterparty references
   - Ensure idempotency:
     - do not blindly insert duplicates on repeated runs
     - use deterministic identifiers, existence checks, or natural-key guards consistent with current seed strategy
   - If the seed system supports “seed only when table empty per company”, use that pattern unless a more granular pattern already exists.

6. **Prevent duplicate key errors**
   - Explicitly verify the seed path can run multiple times safely.
   - If IDs are generated in code, ensure reruns do not recreate the same rows in a conflicting way.
   - If using upsert semantics, follow the project’s established approach.
   - Avoid race-prone logic if bootstrap can run concurrently.

7. **Add tests where the repo supports them**
   - Add focused tests for:
     - payment persistence mapping
     - amount constraint rejects zero/negative values
     - invalid payment type is rejected
     - seed/bootstrap creates payments for companies
     - repeated seed/bootstrap runs do not throw duplicate key errors
   - Prefer integration tests if the repo already has DB-backed test coverage.
   - If full DB integration tests are not practical in this task, add the highest-value coverage available and document the gap.

8. **Keep related API/UI work unblocked**
   - Ensure schema and seed data support future creation/listing and inspection scenarios:
     - company-scoped queries
     - filtering by status/date
     - display of header/status/amount/currency/counterparty reference

# Validation steps
Run and verify the following after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration validation**
   - Apply the migration using the repo’s normal local workflow.
   - Confirm the `payments` table exists with expected columns.
   - Confirm indexes exist on:
     - company/tenant identifier
     - status
     - payment date
   - Confirm DB constraints reject:
     - `amount <= 0`
     - invalid `payment_type`

4. **Seed validation**
   - Run bootstrap/seed flow once and confirm payment rows are created for existing companies.
   - Run bootstrap/seed flow a second time and confirm:
     - no duplicate key errors occur
     - data is not duplicated unexpectedly according to the intended seed strategy

5. **Data sanity checks**
   - Verify seeded data includes both incoming and outgoing payments.
   - Verify records are scoped to valid companies.
   - Verify dates, statuses, methods, and references look realistic.

6. **Code quality**
   - Ensure no unrelated files were modified unnecessarily.
   - Ensure naming and architecture match existing project conventions.

# Risks and follow-ups
- **Migration mechanism ambiguity:** The repo may use archived/manual PostgreSQL migrations rather than standard EF Core migrations. Resolve this by inspecting existing patterns first and following them exactly.
- **Enum storage mismatch:** Payment type/status/method may be represented as strings or ints elsewhere. Match the established convention to avoid serialization/query inconsistencies.
- **Seed idempotency pitfalls:** Repeated bootstrap runs can still duplicate data if existence checks are too weak. Use a deterministic or convention-aligned idempotent strategy.
- **Tenant scoping risk:** Do not create any payment rows without a valid `company_id`.
- **Precision/currency handling:** Choose decimal precision appropriate for money and consistent with the rest of the codebase.
- **Future follow-up:** Related tasks will likely need:
  - payment create/list APIs,
  - tenant-scoped query handlers,
  - payment inspection UI,
  - possible foreign-key linkage to counterparties/invoices if those concepts exist later.

When finished, provide:
- a concise summary of files changed,
- the migration name,
- how idempotency is ensured in seed logic,
- and any assumptions made about existing migration/seed conventions.