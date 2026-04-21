# Goal
Implement backlog task **TASK-24.4.2 — Build mock bank transaction importer and seeded bootstrap generation for existing companies** for story **US-24.4 Implement bank transaction ingestion, reconciliation workflow, and cash ledger integration**.

Deliver a production-aligned vertical slice in the existing .NET modular monolith that adds:
- a persisted **BankTransaction** domain entity
- database migration and indexes
- seeded/mock transaction generation for already onboarded companies
- tenant/company-aware bank account mapping to finance settings
- list/detail APIs with filtering
- reconciliation support linking transactions to one or more payments
- reconciliation status transitions
- exactly-once cash ledger posting/linking with automated idempotency coverage

Use existing project conventions, architecture boundaries, and multi-tenant enforcement. Prefer incremental, testable implementation over speculative abstraction.

# Scope
In scope:
- Add a **BankTransaction** entity with fields:
  - `bankAccount`
  - `bookingDate`
  - `valueDate`
  - `amount`
  - `referenceText`
  - `counterparty`
  - `status`
- Add persistence model/configuration and PostgreSQL migration
- Add indexes on:
  - tenant/company identifier
  - bank account
  - booking date
  - amount
- Implement mock/bootstrap transaction generation for existing onboarded companies
- Ensure generated/imported transactions map to tenant finance settings / bank accounts
- Add API endpoints for:
  - list retrieval
  - detail retrieval
  - filtering by bank account, date range, status, amount
- Implement reconciliation model/process so a bank transaction can link to one or more payments
- Support reconciliation states:
  - unreconciled
  - partially reconciled
  - reconciled
- Ensure reconciliation posts or links the corresponding cash ledger event **exactly once**
- Add automated tests, including idempotency tests

Out of scope unless required by existing code structure:
- Real external bank integrations
- Full UI workflow beyond any minimal API contract support
- Broad finance module redesign
- New mobile functionality
- Large refactors unrelated to this task

If related finance/payment/cash-ledger primitives already exist, integrate with them rather than duplicating concepts.

# Files to touch
Inspect the solution first and then update the appropriate files in these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:
- Domain entities/value objects/enums
- Application commands, queries, DTOs, validators, handlers
- Infrastructure EF Core entity configurations
- DbContext and migrations
- Seed/bootstrap services
- API controllers or minimal endpoint registrations
- Reconciliation and cash-ledger integration services
- Automated tests

Likely concrete targets to inspect:
- `src/VirtualCompany.Domain/*` for finance, payment, ledger, tenant-owned entities
- `src/VirtualCompany.Application/*` for CQRS patterns and query/command organization
- `src/VirtualCompany.Infrastructure/*` for EF mappings, repositories, migrations, seed/bootstrap jobs
- `src/VirtualCompany.Api/*` for route conventions and auth/tenant resolution
- `tests/VirtualCompany.Api.Tests/*` for integration/API/idempotency test patterns
- `docs/postgresql-migrations-archive/README.md` if migration process guidance is needed
- `README.md` for setup/build/test conventions

# Implementation plan
1. **Discover existing finance primitives**
   - Inspect the codebase for existing concepts related to:
     - company/tenant scoping
     - finance settings
     - bank accounts
     - payments
     - cash ledger / ledger events
     - onboarding/bootstrap/seed jobs
   - Reuse existing naming and module boundaries.
   - Identify whether there is already a finance settings aggregate or seeded company bootstrap pipeline to extend.

2. **Add domain model**
   - Introduce a `BankTransaction` entity in the appropriate finance/accounting domain area.
   - Include tenant/company ownership consistent with the shared-schema multi-tenant model.
   - Add a `BankTransactionStatus` enum or equivalent with at least:
     - `Unreconciled`
     - `PartiallyReconciled`
     - `Reconciled`
   - Model reconciliation links so one bank transaction can be associated with one or more payments.
   - If needed, add a join entity such as `BankTransactionPaymentReconciliation` containing:
     - bank transaction id
     - payment id
     - reconciled amount
     - timestamps / metadata
   - Add domain behavior or application-layer logic to compute reconciliation status from linked payments and amounts.
   - Preserve idempotency semantics for cash ledger linkage/posting.

3. **Add persistence and migration**
   - Add EF Core configuration for the new entity/entities.
   - Create PostgreSQL migration(s) for:
     - bank transactions table
     - reconciliation link table if needed
     - any cash-ledger idempotency support column/table if needed
   - Ensure indexes exist on:
     - `company_id` or equivalent tenant/company identifier
     - `bank_account`
     - `booking_date`
     - `amount`
   - Add foreign keys to company/tenant and related payment entities as appropriate.
   - Keep schema names and naming conventions aligned with the rest of the project.

4. **Implement seeded/mock importer/bootstrap**
   - Extend the existing onboarding/bootstrap/seed mechanism so already onboarded companies can receive realistic mock bank transactions.
   - Generated data should:
     - look realistic
     - vary dates, amounts, counterparties, and references
     - include both inflows and outflows if the domain supports signed amounts
     - map to the company’s configured finance/bank account settings
   - If no bank account exists for a company, follow existing conventions:
     - either create a sensible seeded bank account/settings record if appropriate
     - or skip generation with explicit logging and test coverage
   - Make bootstrap deterministic enough for tests where possible, e.g. seeded randomization.
   - Avoid duplicate generation on repeated bootstrap runs unless duplication is explicitly intended; prefer idempotent or guarded seeding.

5. **Implement query APIs**
   - Add list and detail retrieval endpoints in the API layer using existing CQRS-lite patterns.
   - List endpoint must support filtering by:
     - bank account
     - date range
     - status
     - amount
   - Ensure all queries are tenant/company scoped.
   - Return stable DTOs with the required fields and reconciliation summary data if useful.
   - Add pagination if the project already uses it for list endpoints; otherwise follow existing API conventions.

6. **Implement reconciliation workflow**
   - Add an application command/service to reconcile a bank transaction against one or more payments.
   - Validate:
     - tenant/company ownership across all linked records
     - bank transaction existence
     - payment existence
     - no invalid over-reconciliation
     - duplicate payment link prevention where appropriate
   - Update bank transaction status based on total reconciled amount versus transaction amount.
   - Ensure reconciliation can be re-run safely without creating duplicate cash ledger effects.

7. **Integrate cash ledger exactly-once behavior**
   - Reuse existing cash ledger event model if present.
   - On reconciliation, either:
     - create the corresponding cash ledger event once, or
     - link to an existing ledger event once
   - Enforce idempotency using a durable mechanism, such as:
     - unique constraint on a reconciliation/ledger correlation key
     - idempotency key persisted with ledger event
     - transactionally checked link table
   - The implementation must guarantee that retries or repeated reconciliation commands do not double-post the cash ledger event.

8. **Add tests**
   - Add automated coverage for:
     - entity persistence and migration assumptions where practical
     - list API filtering
     - detail API retrieval
     - bootstrap/mock generation for existing companies
     - reconciliation status transitions:
       - unreconciled
       - partially reconciled
       - reconciled
     - multi-payment reconciliation
     - tenant isolation
     - idempotent cash ledger posting/linking
   - Include at least one test that executes reconciliation twice and verifies only one cash ledger event/link exists.

9. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep HTTP concerns in API, orchestration in Application, persistence in Infrastructure, and core rules in Domain.
   - Do not bypass tenant scoping.
   - Prefer typed contracts over direct DB access from controllers.

# Validation steps
1. Inspect and restore/build the solution:
   - `dotnet build`

2. Run the relevant automated tests during development:
   - `dotnet test`

3. Verify migration generation and application follow repository conventions.
   - If migrations are committed in-source, ensure the new migration is included and builds cleanly.

4. Validate acceptance criteria explicitly:
   - Confirm `BankTransaction` exists with all required fields.
   - Confirm migration creates storage and required indexes.
   - Confirm mock/bootstrap generation creates realistic transactions for existing companies and maps to finance settings.
   - Confirm list/detail APIs work and filters behave correctly.
   - Confirm reconciliation can link one transaction to one or more payments.
   - Confirm status transitions are correct.
   - Confirm cash ledger event/link is created exactly once under repeated reconciliation attempts.

5. Add or update tests to prove:
   - tenant/company scoping
   - filter correctness
   - reconciliation correctness
   - idempotency correctness

# Risks and follow-ups
- **Unknown existing finance model:** The repository may already contain partial payment, bank account, or ledger concepts. Reconcile with existing structures instead of introducing parallel models.
- **Ambiguous finance settings mapping:** If tenant finance settings are not yet formalized, implement the smallest compatible mapping and document assumptions in code comments/tests.
- **Bootstrap duplication risk:** Re-running seed/bootstrap logic may create duplicate transactions unless guarded. Prefer deterministic or idempotent generation.
- **Amount semantics:** Be careful with signed amounts, currency assumptions, and partial reconciliation math.
- **Idempotency race conditions:** Exactly-once ledger posting must be enforced durably, not just in-memory. Prefer database-backed uniqueness/transactional guarantees.
- **API contract drift:** Follow existing API patterns and DTO conventions to avoid inconsistent endpoints.
- **Follow-up candidates:** real bank import adapters, reconciliation audit trail, approval workflow for manual reconciliation overrides, and UI surfaces for finance operations.