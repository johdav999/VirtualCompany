# Goal
Implement backlog task **TASK-24.3.1 — Create payment allocation schema, foreign keys, and over-allocation validation rules** for story **US-24.3 Implement payment allocation and document settlement status updates**.

Deliver a production-ready vertical slice in the existing .NET modular monolith that:

- Adds a **PaymentAllocation** persistence model and relational constraints for allocating one payment across many invoices/bills and many payments against one invoice/bill.
- Enforces **over-allocation prevention** at the application/domain layer, with exact decimal handling and currency consistency.
- Supports **partial allocations** and persists exact allocated amounts.
- Automatically recalculates and persists **invoice/bill settlement status** as `unpaid`, `partially_paid`, or `paid` based on current allocations.
- Includes **automated tests** for rejection and recalculation scenarios.
- Adds **backfill logic** to infer/generate allocations for existing mocked paid invoices/bills where source payments exist or must be synthesized.

Use the architecture and backlog context provided. Keep the implementation tenant-aware if finance entities are tenant-owned.

# Scope
In scope:

- Domain model additions for payment allocations.
- Database schema/migration updates:
  - `payment_allocations` table
  - foreign keys to payment and target document(s)
  - indexes and uniqueness/consistency constraints where appropriate
- Validation rules:
  - total allocations for a payment cannot exceed payment amount
  - total allocations to an invoice/bill cannot exceed remaining/open amount
  - allocation currency must match payment and target document currency
  - partial allocations allowed
- Status recalculation logic for invoices and bills after allocation create/update/delete/backfill
- Backfill routine for mocked historical data
- Automated tests covering acceptance criteria

Out of scope unless required by existing code patterns:

- New UI screens
- API redesign beyond minimal endpoints/handlers needed by tests or current architecture
- Full accounting ledger redesign
- External payment provider integration
- Broad refactors unrelated to payment allocation

If the current codebase already has finance entities with different names, adapt to existing naming conventions rather than inventing parallel models.

# Files to touch
Inspect the solution first and then update the actual relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - payment, invoice, bill entities/value objects/enums
  - new `PaymentAllocation` entity
  - settlement status enum updates if needed
  - domain services or validation helpers
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for allocation create/update/delete/backfill
  - validation logic
  - status recalculation orchestration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations / DbContext mappings
  - migrations
  - repositories
  - seed/backfill job implementation
- `src/VirtualCompany.Api/**`
  - only if minimal API/controller wiring already exists or is needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if this project is the established test location
- Potentially migration/archive docs if the repo expects them:
  - `docs/postgresql-migrations-archive/README.md`

Before coding, locate the existing finance model for:
- payments
- invoices
- bills
- status enums
- money/currency handling
- migrations pattern
- test conventions

# Implementation plan
1. **Discover existing finance model and conventions**
   - Search for entities/tables/DTOs/handlers related to:
     - `Payment`
     - `Invoice`
     - `Bill`
     - `Status`
     - `Currency`
     - `Amount`
   - Determine:
     - whether invoices and bills share a base type/interface or are separate aggregates
     - whether payments are customer/vendor neutral or split by type
     - whether money is represented as decimal + currency code or a value object
     - whether tenant ownership is represented by `company_id`
     - whether EF Core migrations are active in-repo

2. **Design the allocation model to fit the existing domain**
   - Prefer a single `PaymentAllocation` entity with:
     - `Id`
     - `CompanyId` if tenant-owned
     - `PaymentId`
     - nullable `InvoiceId`
     - nullable `BillId`
     - exact `AllocatedAmount`
     - `Currency`
     - timestamps
   - Enforce exactly one target:
     - allocation must reference either an invoice or a bill, not both, not neither
   - Add navigation relationships:
     - payment -> many allocations
     - invoice -> many allocations
     - bill -> many allocations
   - If the codebase already has a more generic document abstraction, use that instead of nullable dual FKs.

3. **Add database schema and relational constraints**
   - Create migration for `payment_allocations`.
   - Add:
     - PK
     - FK to payments
     - FK to invoices nullable
     - FK to bills nullable
     - check constraint for exactly one target
     - indexes on `payment_id`, `invoice_id`, `bill_id`, and tenant-scoped lookup columns if applicable
   - Add uniqueness/duplicate-prevention only if business rules require it. Do not block legitimate multiple partial allocations unless the domain expects one allocation row per payment-document pair.
   - Ensure decimal precision is appropriate for money columns and consistent with existing finance schema.

4. **Implement domain/application validation**
   - On allocation create/update:
     - reject if allocation amount <= 0
     - reject if currency mismatches payment currency
     - reject if currency mismatches invoice/bill currency
     - reject if payment total allocated + new amount exceeds payment amount
     - reject if target total allocated + new amount exceeds target gross/open amount
   - For updates, exclude the current allocation row from aggregate calculations.
   - For deletes/reversals, ensure status recalculation still occurs.
   - Use exact decimal arithmetic only; no floating point.
   - If concurrency patterns exist, use them to reduce race-condition risk. At minimum, keep validation and persistence in one transaction.

5. **Implement settlement status recalculation**
   - Define or reuse status values:
     - `unpaid`
     - `partially_paid`
     - `paid`
   - Recalculate based on current allocation totals:
     - `0` => unpaid
     - `> 0 && < total due` => partially paid
     - `>= total due` => paid
   - Use the document’s payable/receivable amount consistent with existing schema:
     - invoice open amount / total amount due
     - bill open amount / total amount due
   - Trigger recalculation after:
     - allocation create
     - allocation update
     - allocation delete
     - backfill generation
   - Keep logic centralized in a domain service or application service to avoid duplication.

6. **Implement backfill logic**
   - Add a backfill routine for existing mocked paid invoices/bills:
     - if source payments are available, create inferred allocations from those payments
     - if no source payment exists but the mocked data requires a paid state, synthesize a payment record if that is the accepted domain pattern, then allocate it
   - Make the backfill idempotent:
     - do not duplicate allocations on rerun
   - Preserve currency consistency and status recalculation.
   - If the repo has seeders, hosted jobs, or migration-time data scripts, integrate with the established mechanism rather than inventing a new one.

7. **Add automated tests**
   - Cover at minimum:
     - payment over-allocation rejected
     - invoice over-allocation rejected
     - bill over-allocation rejected
     - partial allocation persisted exactly
     - one payment allocated across multiple documents
     - multiple payments allocated to one document
     - status transitions:
       - unpaid -> partially_paid
       - partially_paid -> paid
       - paid -> partially_paid after allocation change/removal if supported
     - currency mismatch rejected
     - backfill creates expected allocations and statuses
   - Prefer integration tests against the real persistence layer if that matches repo conventions; otherwise use application-layer tests plus persistence tests.

8. **Keep implementation aligned with clean boundaries**
   - Domain: invariants, entities, enums, value objects
   - Application: commands/use cases, orchestration, transactional validation
   - Infrastructure: EF mappings, migrations, repository queries
   - API: thin wiring only if needed

9. **Document assumptions in code comments or PR-ready notes**
   - If invoice/bill “remaining open amount” is derived rather than stored, note that.
   - If synthesized payments are created during backfill, document how they are identified.
   - If race conditions remain without stronger DB locking, note follow-up recommendations.

# Validation steps
1. Inspect and build before changes:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation:
   - `dotnet build`
   - `dotnet test`

4. Verify migration compiles and applies in the project’s normal pattern.
   - If EF Core tooling is configured, generate/apply the migration using the repo’s established approach.
   - Confirm the resulting schema includes:
     - `payment_allocations`
     - correct foreign keys
     - target check constraint
     - expected indexes
     - correct money precision

5. Validate behavior with automated tests:
   - over-allocation is rejected for payment and target document
   - partial allocations persist exact decimal amounts
   - multi-document and multi-payment allocations succeed when valid
   - invoice/bill statuses recalculate correctly after create/update/delete
   - backfill is idempotent and produces expected allocations/statuses

6. In the final implementation summary, include:
   - actual files changed
   - schema decisions made
   - any assumptions about existing finance entities
   - any follow-up gaps not addressed

# Risks and follow-ups
- **Unknown existing finance model**: payments/invoices/bills may already have partial settlement logic or conflicting status enums. Adapt rather than duplicate.
- **Concurrency risk**: application-layer aggregate validation alone can still race under concurrent allocations. If the repo supports it, consider transaction isolation, row locking, or a stronger persistence strategy as a follow-up.
- **Backfill ambiguity**: mocked paid documents may not have enough provenance to infer exact source payments. If synthesis is required, mark synthesized payments clearly and keep the process idempotent.
- **Document amount semantics**: ensure validation uses the correct amount basis:
  - total due
  - remaining open amount
  - net vs gross
  depending on the existing domain model.
- **Currency handling**: if the current system lacks a robust money value object, avoid broad refactors in this task; use existing decimal/currency conventions consistently.
- **Delete/update semantics**: if allocations are immutable in the current architecture, implement reversal/replacement patterns instead of in-place edits, but still satisfy acceptance criteria through recalculation behavior.