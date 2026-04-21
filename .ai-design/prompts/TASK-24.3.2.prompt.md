# Goal
Implement backlog task **TASK-24.3.2 — Implement settlement service to recalculate invoice and bill payment statuses from allocations** for story **US-24.3 Implement payment allocation and document settlement status updates**.

The coding agent should add the domain/application/infrastructure behavior needed so that payment allocations become the source of truth for settlement state on invoices and bills, with exact amount validation, automatic status recalculation, test coverage, and backfill support for existing mocked paid documents.

# Scope
Implement only what is necessary to satisfy this task and its acceptance criteria within the existing modular monolith and .NET solution structure.

Required outcomes:

- Add or complete a **PaymentAllocation** model that supports:
  - one payment allocated across multiple invoices and/or bills
  - multiple payments allocated to a single invoice or bill
- Enforce allocation validation:
  - total allocations for a payment cannot exceed the payment amount
  - total allocations against an invoice or bill cannot exceed its remaining open amount
  - partial allocations are allowed
  - currency consistency is enforced
  - exact monetary precision is preserved
- Implement a **settlement recalculation service** that derives invoice and bill payment status from current allocations:
  - unpaid
  - partially paid
  - paid
- Ensure recalculation runs after allocation create/update/delete or equivalent allocation changes
- Add automated tests covering:
  - over-allocation rejection
  - partial payment handling
  - multi-document allocation
  - status recalculation after allocation changes
- Add **backfill logic** for existing mocked paid invoices and bills:
  - infer allocations from available source payments where possible
  - synthesize allocations/payments where necessary and clearly isolate this behavior to seed/mock/backfill paths
- Keep implementation tenant-safe if finance entities are tenant-owned
- Prefer clean architecture boundaries:
  - domain rules in domain/application services
  - persistence in infrastructure
  - no UI/mobile work unless required by compile-time dependencies

Out of scope unless required by existing code patterns:

- New end-user UI
- Broad finance module redesign
- External accounting integrations
- Full historical event sourcing
- Non-mock production migration heuristics beyond what acceptance criteria require

# Files to touch
Inspect the solution first and then update the actual relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities such as payment, invoice, bill, allocation, money/value objects, status enums
  - domain services or specifications for settlement validation/recalculation
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for creating/updating/removing allocations
  - settlement orchestration service
  - backfill use case or startup/seed/backfill command
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext mappings
  - repositories
  - migrations or seed/backfill persistence support
- `src/VirtualCompany.Api/**`
  - only if endpoints or startup wiring must be updated for compilation or exposure of new application services
- `tests/VirtualCompany.Api.Tests/**`
  - integration and/or application-level tests for allocation validation and status recalculation

Also inspect for existing finance-related files before creating new ones. Reuse established naming and folder conventions.

# Implementation plan
1. **Discover current finance model and conventions**
   - Search for existing entities/types related to:
     - invoices
     - bills
     - payments
     - payment status / settlement status
     - money/currency value objects
     - tenant/company ownership
     - seed/mock/backfill infrastructure
   - Determine whether invoice and bill share a common abstraction or require parallel handling.
   - Determine whether EF Core is already used and how migrations are managed in this repo.

2. **Design the allocation model to fit the existing domain**
   - If `PaymentAllocation` does not exist, add it.
   - Model fields should likely include:
     - `Id`
     - `CompanyId` or tenant key if applicable
     - `PaymentId`
     - target document discriminator or explicit nullable foreign keys:
       - `InvoiceId` and/or `BillId`
     - `AllocatedAmount`
     - `Currency`
     - timestamps
   - Enforce that an allocation targets exactly one document type.
   - Preserve exact decimal precision consistent with existing money handling.
   - If a money value object exists, use it instead of raw primitives.

3. **Implement domain validation rules**
   - Add validation logic so that:
     - sum of allocations for a payment `<= payment total amount`
     - sum of allocations for a target invoice/bill `<= document gross/open amount`
     - allocation currency matches payment currency
     - allocation currency matches target document currency
     - allocation amount must be positive and non-zero unless existing conventions allow zero
   - Validation should account for updates by excluding the current allocation from aggregate checks.
   - Prefer a dedicated domain/application service such as:
     - `SettlementService`
     - `PaymentAllocationValidator`
     - `DocumentSettlementCalculator`
   - Avoid burying cross-aggregate validation solely inside entities if repository access is required.

4. **Implement settlement recalculation**
   - Create a service that computes allocated totals for a document from current allocations.
   - Recalculate invoice and bill statuses using rules:
     - `unpaid` when allocated total is `0`
     - `partially paid` when allocated total is `> 0` and `< total due`
     - `paid` when allocated total equals total due
   - If the domain already tracks paid amount/open amount, update those consistently.
   - Ensure recalculation is triggered after:
     - allocation creation
     - allocation update
     - allocation deletion
     - backfill-generated allocations
   - Keep recalculation deterministic and idempotent.

5. **Wire persistence**
   - Add EF Core configuration for `PaymentAllocation`.
   - Configure relationships:
     - payment -> allocations (one-to-many)
     - invoice -> allocations (one-to-many if invoice target)
     - bill -> allocations (one-to-many if bill target)
   - Add indexes for common aggregate queries:
     - by `PaymentId`
     - by `InvoiceId`
     - by `BillId`
     - by tenant/company if applicable
   - Add database constraints where practical:
     - exactly one target type
     - decimal precision
   - If the repo uses migrations, add one. If migrations are intentionally deferred, follow repo convention and document what changed.

6. **Implement application workflows for allocation changes**
   - Add or update command handlers/services for:
     - create allocation
     - update allocation
     - delete allocation
   - Each workflow should:
     - load payment and target document
     - validate currency and aggregate limits
     - persist allocation change
     - recalculate affected payment/document settlement state
     - save atomically in one transaction
   - If payment itself has a settlement state like unallocated/partially allocated/fully allocated, update it only if already part of the model; do not expand scope unnecessarily.

7. **Implement backfill logic for mocked paid invoices and bills**
   - Find existing seed/mock data generation path.
   - Add a backfill routine that:
     - identifies mocked invoices/bills marked paid or partially paid but lacking allocations
     - attempts to match existing source payments by company, currency, counterparty, amount, and/or date heuristics if such data exists
     - creates inferred allocations where a clear match exists
     - synthesizes payment records only when no source payment exists and acceptance criteria require it
   - Keep synthesized records clearly marked as mock/backfill-generated if the model supports metadata/notes/source type.
   - Recalculate statuses after backfill so resulting state is allocation-driven.
   - Make backfill safe to rerun:
     - avoid duplicate allocations
     - skip already-backfilled records

8. **Add tests**
   - Add automated tests covering at minimum:
     - rejecting allocation when payment total would be exceeded
     - rejecting allocation when invoice/bill open amount would be exceeded
     - partial allocation persists exact amount and sets status to partially paid
     - one payment allocated across multiple documents
     - multiple payments allocated to one document
     - status recalculates from paid back to partially paid/unpaid after allocation change or deletion
     - currency mismatch rejection
     - backfill creates inferred/synthesized allocations for mocked paid docs
   - Prefer integration-style tests if the project already uses API/application+EF tests; otherwise use the dominant test style in the repo.

9. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep business rules out of controllers.
   - Ensure tenant/company scoping is applied in queries and writes.
   - Use CQRS-lite patterns if already present.

10. **Document assumptions in code comments only where necessary**
   - Do not add broad documentation files unless needed.
   - If backfill heuristics are necessarily approximate, keep them explicit and conservative.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, ensure they apply cleanly in the project’s expected way.

4. Manually verify through tests or targeted execution that:
   - a payment can allocate to multiple invoices/bills
   - multiple payments can allocate to one invoice/bill
   - over-allocation is rejected for both payment and target document
   - partial allocations preserve exact decimal amounts
   - invoice and bill statuses transition correctly:
     - unpaid -> partially paid -> paid
     - paid -> partially paid/unpaid after allocation removal or reduction
   - currency mismatch is rejected
   - backfill is idempotent and does not duplicate allocations on rerun

5. Include in the final agent report:
   - files changed
   - whether a migration was added
   - any assumptions made about existing finance entities/status enums
   - any follow-up gaps discovered

# Risks and follow-ups
- The repo may not yet contain a finance domain model for payments/invoices/bills; if so, implement the minimum viable shape needed for this task without inventing unrelated features.
- Existing status enums may use different names than `unpaid`, `partially paid`, `paid`; map to existing conventions rather than forcing new terminology unless necessary.
- If invoice and bill totals include tax/credits/adjustments, use the existing “amount due/open amount” source of truth instead of guessing from gross totals.
- Backfill heuristics can be ambiguous; prefer conservative matching and synthesis over risky inferred links.
- If there is no existing migration workflow in active use, avoid breaking the build by adding speculative migration artifacts without confirming conventions.
- If payment allocation changes should emit audit/outbox events, note this as a follow-up unless already required by nearby patterns.
- If payment status recalculation is also desirable but not in acceptance criteria, keep it as a follow-up unless already trivial within the current model.