# Goal
Implement **TASK-24.3.4 — Write integration and edge-case tests for partial payments and status transitions** for **US-24.3 Implement payment allocation and document settlement status updates**.

Your objective is to add or extend automated tests that verify the payment allocation and settlement-status behavior end to end at the appropriate integration level for this .NET modular monolith. Focus on correctness, persistence, recalculation behavior, and backfill inference behavior rather than introducing speculative new architecture.

# Scope
Include test coverage for the following acceptance criteria:

1. **Many-to-many allocation support**
   - One payment can be allocated across multiple invoices or bills.
   - Multiple payments can be allocated to a single invoice or bill.

2. **Allocation validation**
   - Reject allocations when total allocated amount exceeds:
     - the payment amount, or
     - the remaining open amount on the target invoice or bill.
   - Enforce currency-consistent validation.

3. **Partial allocations**
   - Partial allocations are supported.
   - Exact allocated amounts are persisted and reloaded correctly.

4. **Automatic status transitions**
   - Invoice and bill statuses recalculate to:
     - unpaid
     - partially paid
     - paid
   - Recalculation must reflect current allocation totals, including after allocation changes.

5. **Automated test scenarios**
   - Over-allocation rejection
   - Partial payment handling
   - Multi-document allocation
   - Status recalculation after allocation changes

6. **Backfill logic**
   - Tests cover inference/generation of allocations for existing mocked paid invoices and bills where:
     - source payments are available, or
     - payments are synthesized

Out of scope unless required to make tests pass:
- Broad refactors unrelated to payment allocation
- UI work
- New product features beyond what tests require
- Replacing existing test infrastructure if current patterns are sufficient

# Files to touch
Inspect the existing implementation first, then update only the minimal necessary set. Likely areas:

- `tests/VirtualCompany.Api.Tests/**`
  - Add or extend integration test classes for payments, invoices, bills, and backfill flows
  - Add shared test fixtures/builders/helpers if needed

Potential production files only if required to support testability or fix uncovered defects:

- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`

Also review:
- `README.md`
- existing test setup in `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

Prefer extending existing payment/accounting test suites over creating disconnected new patterns.

# Implementation plan
1. **Discover current payment/accounting implementation**
   - Locate existing entities, commands, handlers, repositories, migrations, and tests related to:
     - payments
     - invoices
     - bills
     - allocations
     - settlement/payment statuses
     - backfill jobs or seed/mock data logic
   - Identify the current integration-test style:
     - API-level HTTP tests
     - application/service-level integration tests
     - database-backed tests
   - Reuse the established style.

2. **Map domain behavior to concrete test cases**
   Create a test matrix covering at minimum:

   - **Payment allocated to multiple documents**
     - single payment split across 2+ invoices/bills
     - verify persisted allocation rows and resulting statuses

   - **Multiple payments allocated to one document**
     - 2+ payments applied to one invoice/bill
     - verify cumulative totals and final status transitions

   - **Over-allocation against payment total**
     - attempt allocations whose sum exceeds payment amount
     - assert rejection with expected error/result

   - **Over-allocation against document open amount**
     - attempt allocation beyond remaining invoice/bill balance
     - assert rejection

   - **Partial allocation**
     - allocate less than full amount
     - verify exact decimal persistence and `partially paid` status

   - **Full settlement**
     - allocate exact remaining amount
     - verify `paid` status

   - **Allocation change/reversal/recalculation**
     - modify, remove, or replace allocations using the supported mechanism
     - verify status moves correctly, including from `paid` back to `partially paid` or `unpaid` if applicable

   - **Currency mismatch validation**
     - payment/document currency mismatch should fail if that is the intended rule in implementation/acceptance criteria

   - **Backfill with source payments available**
     - existing mocked paid invoice/bill gets inferred allocations from known payments
     - verify generated allocations and statuses

   - **Backfill with synthesized payments**
     - where no source payment exists but backfill logic synthesizes one
     - verify generated payment/allocation artifacts and resulting statuses

3. **Use realistic integration setup**
   - Prefer tests that exercise the real application stack and persistence boundaries used by the project.
   - Seed tenant/company-scoped data if multi-tenancy is enforced in tests.
   - Use deterministic amounts and currencies, especially decimal edge cases like:
     - `100.00`
     - `40.25`
     - `59.75`
   - Avoid flaky time-based assertions.

4. **Add reusable test helpers**
   If the suite lacks them, add small helpers/builders for:
   - creating company/tenant context
   - creating invoices, bills, and payments
   - applying allocations
   - reloading entities from persistence
   - asserting settlement status and allocated/open totals

   Keep helpers thin and local to tests.

5. **Fix implementation only where tests reveal gaps**
   If tests expose defects, make the smallest production changes necessary to satisfy the acceptance criteria. Examples:
   - missing validation
   - incorrect status recalculation
   - persistence bugs
   - backfill inference issues

   Do not redesign the module unless unavoidable.

6. **Ensure assertions verify persisted state**
   For each integration scenario, assert not only command success/failure but also:
   - allocation records exist with exact amounts
   - invoice/bill totals are recalculated correctly
   - statuses are persisted correctly after reload
   - backfill-generated artifacts are persisted and linked correctly

7. **Keep naming explicit**
   Use test names like:
   - `Allocating_single_payment_across_multiple_invoices_persists_exact_amounts_and_updates_statuses`
   - `Creating_allocations_that_exceed_payment_amount_is_rejected`
   - `Removing_or_changing_allocations_recalculates_document_status`
   - `Backfill_generates_allocations_for_paid_documents_when_source_payments_exist`

# Validation steps
1. Run targeted tests first:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

2. Then run the broader suite if feasible:
   - `dotnet test`

3. If production code changed, also verify build:
   - `dotnet build`

4. Confirm the final test coverage includes:
   - over-allocation rejection
   - partial payment handling
   - multi-document allocation
   - status recalculation after allocation changes
   - backfill inference/generation paths

5. In your final summary, report:
   - which test files were added/updated
   - which scenarios are covered
   - whether any production defects were fixed
   - any remaining gaps or assumptions discovered in the current implementation

# Risks and follow-ups
- The payment allocation implementation may not yet exist fully; if so, first align tests to the actual current architecture and note any missing prerequisites.
- Backfill behavior may be implemented in a worker, migration, seed routine, or application service; ensure tests target the real entry point rather than duplicating logic.
- Decimal and currency handling can be fragile; avoid floating-point assumptions and use exact decimal assertions.
- Status recalculation may depend on domain events or save hooks; integration tests should verify persisted outcomes after the full unit of work completes.
- If allocation removal/update is not directly supported, test the nearest supported allocation-change path and clearly document the limitation.
- If there is no existing accounting test harness, add the smallest reusable fixture necessary and avoid inventing a parallel testing style.