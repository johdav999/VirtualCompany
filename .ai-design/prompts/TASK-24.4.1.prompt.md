# Goal

Implement backlog task **TASK-24.4.1 — Create bank transaction model, migration, indexes, and tenant bank account mapping** for story **US-24.4 Implement bank transaction ingestion, reconciliation workflow, and cash ledger integration** in the existing **.NET modular monolith**.

Deliver a vertical slice that introduces tenant-scoped bank transaction persistence and retrieval foundations, plus realistic bootstrap/import data and tenant finance bank account mapping, while preparing for reconciliation and cash-ledger idempotency support required by the acceptance criteria.

The implementation must align with:
- shared-schema multi-tenancy using `company_id`
- ASP.NET Core + application/domain/infrastructure layering
- PostgreSQL as source of truth
- CQRS-lite patterns already used in the solution
- automated tests for tenant isolation, filtering, and idempotent reconciliation side effects

# Scope

Implement the following in this task:

1. **Domain model**
   - Add a `BankTransaction` entity with at least:
     - `BankAccount`
     - `BookingDate`
     - `ValueDate`
     - `Amount`
     - `ReferenceText`
     - `Counterparty`
     - `Status`
   - Ensure tenant/company ownership via `CompanyId`
   - Add identifiers and audit timestamps consistent with existing conventions
   - Add reconciliation-related fields needed to support:
     - unreconciled
     - partially reconciled
     - reconciled
   - Add enough structure to support linking one bank transaction to one or more payments

2. **Tenant bank account mapping**
   - Introduce or extend tenant/company finance settings so onboarded companies can have mapped bank accounts
   - Store realistic bank account metadata sufficient for import/bootstrap and API filtering
   - Keep this tenant-scoped and compatible with future finance settings expansion

3. **Persistence**
   - Add EF Core configuration and PostgreSQL migration(s) for bank transactions and any required mapping/link tables
   - Create indexes covering:
     - tenant/company identifier
     - bank account
     - booking date
     - amount
   - Prefer composite indexes that match expected query patterns for list/filter APIs

4. **Bootstrap/mock import**
   - Add seed/bootstrap/mock import behavior that creates realistic bank transactions for onboarded companies
   - Ensure imported/generated transactions map to tenant finance bank accounts
   - Data should look plausible for reconciliation scenarios:
     - invoices/payments
     - payroll
     - subscriptions
     - bank fees
     - transfers
     - refunds

5. **APIs**
   - Add list and detail retrieval endpoints for bank transactions
   - Support filtering by:
     - bank account
     - date range
     - status
     - amount
   - Enforce tenant scoping on all queries

6. **Reconciliation foundation**
   - Implement a reconciliation process/service that can link a bank transaction to one or more payments
   - Update transaction reconciliation status based on linked amounts
   - Ensure reconciliation posts or links the corresponding cash ledger event exactly once
   - Add automated idempotency tests proving duplicate reconciliation attempts do not create duplicate cash ledger side effects

7. **Tests**
   - Add unit/integration/API tests for:
     - entity mapping
     - migration expectations where practical
     - list/detail filtering
     - tenant isolation
     - reconciliation status transitions
     - idempotent cash ledger posting/linking

Do not implement unrelated UI unless required for API contract coverage. Keep changes focused on backend/domain/infrastructure/tests.

# Files to touch

Inspect the solution first and adapt to actual project structure. Likely files/folders include:

- `src/VirtualCompany.Domain/...`
  - add `BankTransaction` entity
  - add reconciliation status enum/value object
  - add link entity for transaction-to-payment reconciliation if needed
  - add tenant finance settings/bank account mapping model if not already present

- `src/VirtualCompany.Application/...`
  - commands/queries for:
    - list bank transactions
    - get bank transaction detail
    - reconcile bank transaction
    - bootstrap/mock import
  - DTOs/contracts for API responses and filters
  - validators
  - application services/interfaces

- `src/VirtualCompany.Infrastructure/...`
  - EF Core `DbContext`
  - entity type configurations
  - migrations
  - repositories/query handlers
  - bootstrap/seed/import implementation
  - cash ledger integration/idempotency persistence support
  - outbox or side-effect guard if existing patterns support this

- `src/VirtualCompany.Api/...`
  - controllers or minimal API endpoints for bank transaction list/detail/reconcile
  - request models
  - route registration
  - authorization/tenant resolution wiring

- `tests/VirtualCompany.Api.Tests/...`
  - API/integration tests for filtering, detail retrieval, tenant isolation, reconciliation, idempotency

Also inspect for existing finance/payment/cash-ledger modules and extend those instead of duplicating concepts.

# Implementation plan

1. **Discover existing finance/payment/cash-ledger structures**
   - Search for:
     - payment entities
     - finance settings
     - bank account models
     - ledger/cash ledger entities
     - onboarding/bootstrap seed logic
     - tenant/company base entity conventions
   - Reuse existing abstractions and naming where possible
   - If a finance settings aggregate already exists, extend it with bank account mappings rather than creating a parallel settings model

2. **Design the domain model**
   - Add `BankTransaction` as a tenant-owned aggregate/entity with:
     - `Id`
     - `CompanyId`
     - `BankAccount` or `BankAccountId` if a mapped account entity exists
     - `BookingDate`
     - `ValueDate`
     - `Amount`
     - `ReferenceText`
     - `Counterparty`
     - `Status`
     - created/updated timestamps
   - Prefer explicit reconciliation fields such as:
     - `ReconciledAmount`
     - `Currency` if finance models require it
     - `ExternalReference` or import source reference if useful
   - Add a reconciliation link entity, e.g. `BankTransactionPaymentLink`, to support one-to-many links from transaction to payments
   - Add a status enum such as:
     - `Unreconciled`
     - `PartiallyReconciled`
     - `Reconciled`
   - Encapsulate status transitions in domain methods so status is derived from linked amounts, not set ad hoc

3. **Model tenant bank account mapping**
   - Extend tenant/company finance settings with mapped bank accounts, or add a dedicated tenant-owned bank account entity if that better fits the existing codebase
   - Include fields such as:
     - account display name
     - bank name
     - masked account number / IBAN
     - currency
     - active flag
     - optional external/import code
   - Ensure `BankTransaction` references this mapping consistently
   - Keep the model future-safe for multiple accounts per company

4. **Implement EF Core mappings**
   - Add entity configurations for:
     - `BankTransaction`
     - bank account mapping entity if new
     - reconciliation link entity
   - Configure required fields, lengths, precision for money/amounts, and enum conversions
   - Add indexes optimized for list/filter queries, likely including:
     - `company_id`
     - `(company_id, bank_account_id)` or `(company_id, bank_account)`
     - `(company_id, booking_date)`
     - `(company_id, amount)`
     - consider `(company_id, status, booking_date)` if query patterns justify it
   - Add foreign keys to company, bank account mapping, and payment entities

5. **Create migration**
   - Generate a PostgreSQL migration that creates:
     - bank transaction table
     - tenant bank account mapping table if needed
     - reconciliation link table
     - indexes required by acceptance criteria
   - Review generated SQL for naming consistency and index coverage
   - Do not leave dead columns or nullable fields without reason

6. **Add bootstrap/mock import**
   - Find existing onboarding/bootstrap hooks and extend them
   - For onboarded companies:
     - create one or more mapped bank accounts in finance settings
     - generate realistic transactions over a recent date range
   - Include varied statuses and references that support reconciliation tests
   - Ensure generated transactions are tenant-scoped and tied to mapped bank accounts
   - If there is an import service abstraction, implement a mock importer behind it rather than hardcoding in controllers

7. **Implement queries and API contracts**
   - Add list query with filters:
     - bank account
     - from/to booking date
     - status
     - min/max amount
   - Add detail query by transaction id
   - Return only tenant-owned records
   - Include enough detail in DTOs for reconciliation visibility, such as:
     - linked payment ids/summaries
     - reconciled amount
     - status
     - bank account display info
   - Wire endpoints in API layer using existing conventions for tenant resolution and authorization

8. **Implement reconciliation service**
   - Add an application command to reconcile a bank transaction against one or more payments
   - Validate:
     - transaction and payments belong to same company
     - links are not duplicated
     - amounts do not over-reconcile unless explicitly allowed by domain rules
   - Compute reconciliation status:
     - no linked amount => unreconciled
     - linked amount < transaction amount => partially reconciled
     - linked amount == transaction amount => reconciled
   - Persist reconciliation links atomically

9. **Ensure cash ledger idempotency**
   - Reuse existing cash ledger event model if present
   - On reconciliation, post or link the corresponding cash ledger event exactly once
   - Implement idempotency using one of:
     - unique constraint on a deterministic ledger linkage key
     - dedicated processed-operation table
     - existing outbox/idempotency mechanism
   - The idempotency key should be deterministic for the reconciliation side effect, not request-instance specific
   - Duplicate reconcile command execution must not create duplicate ledger events or duplicate links

10. **Add tests**
   - Unit tests:
     - status transition logic
     - reconciliation amount calculations
     - duplicate link prevention
   - Integration/API tests:
     - list endpoint returns only current tenant data
     - filters by bank account/date/status/amount work correctly
     - detail endpoint returns correct transaction and rejects cross-tenant access
     - bootstrap/import creates realistic mapped transactions for onboarded companies
     - reconciliation links one transaction to multiple payments
     - repeated reconciliation command is idempotent for cash ledger side effects
   - If migration tests are not already present, at minimum verify model configuration and runtime behavior against test database

11. **Keep implementation clean**
   - Follow existing naming, folder, and CQRS patterns
   - Avoid leaking EF entities directly through API
   - Keep tenant scoping enforced in query layer and handlers
   - Add comments only where non-obvious idempotency or reconciliation logic needs explanation

# Validation steps

Run these after implementation and fix any failures:

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. If the repo uses EF migrations tooling, verify migration compiles and applies cleanly
   - ensure migration is included in the infrastructure startup path if applicable

4. Manually validate API behavior with tests or local requests:
   - list bank transactions for a tenant
   - filter by bank account
   - filter by booking date range
   - filter by status
   - filter by amount range
   - fetch detail by id
   - verify cross-tenant access is blocked/not found

5. Validate bootstrap/mock import:
   - onboarded company receives mapped bank account(s)
   - realistic transactions exist and reference those accounts

6. Validate reconciliation:
   - reconcile one transaction to one payment
   - reconcile one transaction to multiple payments
   - verify status changes from unreconciled -> partially reconciled -> reconciled as appropriate
   - execute the same reconciliation flow twice and confirm cash ledger event/link exists exactly once

7. Review migration/indexes:
   - confirm indexes exist for tenant/company identifier, bank account, booking date, and amount
   - confirm foreign keys and uniqueness constraints supporting idempotency are present

# Risks and follow-ups

- **Existing finance model mismatch**
  - The repo may already have finance settings, payment, or ledger concepts with different naming. Prefer extension over duplication.

- **Ambiguity in `bankAccount` field**
  - Acceptance criteria names `bankAccount` as a field, but implementation should likely use a tenant bank account mapping entity plus a readable display field in DTOs.

- **Money precision/currency**
  - If the platform already has a money value object, use it. Otherwise choose safe decimal precision and avoid floating-point types.

- **Reconciliation semantics**
  - Clarify whether reconciliation is based on absolute amount equality, signed amounts, or payment allocation rules. Implement the simplest correct domain rule consistent with existing payment models.

- **Cash ledger integration dependency**
  - If cash ledger infrastructure is incomplete, implement the minimal linkage/idempotency mechanism now and leave richer ledger posting behavior for the next task, but still satisfy the exact-once acceptance criterion with tests.

- **Bootstrap realism vs determinism**
  - Seed data should be realistic but deterministic enough for tests. Use fixed seeds or predictable generation in test paths.

- **Potential follow-up tasks**
  - richer import pipeline from external bank feeds
  - reconciliation suggestion engine
  - UI for finance settings and reconciliation review
  - audit events for reconciliation actions
  - pagination/sorting enhancements for bank transaction APIs