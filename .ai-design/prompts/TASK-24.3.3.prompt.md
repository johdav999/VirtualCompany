# Goal
Implement backlog task **TASK-24.3.3 — Add allocation APIs for creating and inspecting payment-to-document links** for story **US-24.3 Implement payment allocation and document settlement status updates**.

Deliver a production-ready vertical slice in the existing .NET modular monolith that:

- Introduces a **PaymentAllocation** domain model supporting:
  - one payment allocated across multiple invoices and/or bills
  - multiple payments allocated to a single invoice or bill
- Exposes **tenant-scoped APIs** to:
  - create allocations
  - inspect allocations by payment and by document
- Enforces validation so that:
  - total allocated amount for a payment never exceeds the payment amount
  - total allocated amount for an invoice or bill never exceeds its remaining open amount
  - allocations are currency-consistent
  - partial allocations are supported with exact persisted amounts
- Automatically recalculates and persists **invoice/bill settlement statuses** as:
  - unpaid
  - partially paid
  - paid
- Adds automated tests covering:
  - over-allocation rejection
  - partial payment handling
  - multi-document allocation
  - status recalculation after allocation changes
- Adds **backfill logic** to infer/generate allocations for existing mocked paid invoices and bills where source payments are available or can be synthesized

Work within current architecture and conventions. Prefer minimal, cohesive changes over speculative abstractions.

# Scope
In scope:

- Domain/entity changes for payment allocations and settlement status recalculation
- Persistence changes and EF Core mappings/migrations
- Application commands/queries/services for allocation creation and inspection
- API endpoints/controllers/contracts
- Validation and error handling
- Backfill/inference logic for existing mocked accounting data
- Automated tests at domain/application/API level as appropriate

Out of scope unless required by existing code patterns:

- UI work in Blazor or MAUI
- External accounting integrations
- Full edit/delete/reversal workflow unless necessary for acceptance criteria
- Event bus/outbox enhancements beyond what is needed for consistency
- Broad refactors unrelated to payment allocation

Assumptions to verify from the codebase before implementation:

- Existing entities likely include **Payment**, **Invoice**, and **Bill** or equivalent accounting documents
- Existing document status enums may already exist and should be extended/reused rather than duplicated
- Multi-tenancy is enforced via `company_id`/tenant scoping and must be preserved in all queries and writes
- Monetary values should use the project’s established money/decimal conventions; do not invent a new representation if one already exists

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely areas:

- `src/VirtualCompany.Domain/**`
  - payment, invoice, bill entities
  - status enums/value objects
  - new `PaymentAllocation` entity
  - domain services/helpers for allocation/status recalculation
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for create/get allocations
  - DTOs/contracts
  - validation logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext
  - entity configurations
  - repositories
  - migration files
  - backfill/seeding/mock-data upgrade logic if present here
- `src/VirtualCompany.Api/**`
  - controllers/endpoints
  - request/response models
  - DI registration if needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- Potentially additional test projects if domain/application tests already exist elsewhere

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If there are existing accounting modules, settlement logic, or mock data seeders, update those instead of creating parallel implementations.

# Implementation plan
1. **Discover existing accounting model and conventions**
   - Locate current entities for payments, invoices, bills, and any settlement/payment status fields.
   - Identify:
     - tenant/company scoping pattern
     - money/currency representation
     - API style
     - validation/error response conventions
     - migration workflow
   - Find any existing mocked paid invoices/bills seed/backfill logic.

2. **Design the allocation model to fit existing domain**
   - Add a `PaymentAllocation` entity/table with fields along these lines, adapted to project conventions:
     - `Id`
     - `CompanyId`
     - `PaymentId`
     - `DocumentType` or separate nullable `InvoiceId` / `BillId` pattern, whichever best matches current model
     - `DocumentId` if polymorphic approach is already used
     - `AllocatedAmount`
     - `Currency`
     - timestamps/audit fields if standard
   - Ensure the model supports:
     - many allocations per payment
     - many allocations per invoice
     - many allocations per bill
   - Add uniqueness/indexing constraints appropriate for lookup and duplicate prevention without blocking valid partial multi-payment scenarios.

3. **Implement domain validation rules**
   - On allocation creation, validate:
     - payment exists and belongs to tenant
     - target invoice/bill exists and belongs to tenant
     - allocation amount is positive and non-zero
     - allocation currency matches payment currency
     - allocation currency matches target document currency
     - sum(existing allocations for payment) + new allocation <= payment total amount
     - sum(existing allocations for document) + new allocation <= document open amount
   - Use exact decimal handling consistent with existing accounting precision.
   - Return domain/application validation failures using existing error patterns.

4. **Implement settlement status recalculation**
   - Add or update logic so invoice/bill status is derived from current allocation totals:
     - `unpaid` when allocated total == 0
     - `partially paid` when allocated total > 0 and < document total
     - `paid` when allocated total == document total
   - If open amount fields already exist, keep them synchronized or derive them consistently.
   - Ensure recalculation runs after allocation creation and after any backfill-generated allocations.
   - Centralize this logic to avoid invoice and bill divergence.

5. **Add application layer commands and queries**
   - Create command(s) for allocation creation, e.g.:
     - create single allocation
     - optionally create multiple allocations in one request if that better matches acceptance criteria and existing API style
   - Create query endpoints/services for:
     - allocations by payment
     - allocations by invoice
     - allocations by bill
   - Prefer a clean CQRS-lite shape aligned with the architecture.

6. **Add API endpoints**
   - Implement tenant-scoped endpoints in the existing API style, for example:
     - `POST /api/payments/{paymentId}/allocations`
     - `GET /api/payments/{paymentId}/allocations`
     - `GET /api/invoices/{invoiceId}/allocations`
     - `GET /api/bills/{billId}/allocations`
   - If the project uses minimal APIs instead of controllers, follow that pattern.
   - Responses should include enough detail to inspect links and totals:
     - allocation id
     - payment id
     - target document type/id
     - allocated amount/currency
     - created timestamp
     - optionally payment/document totals and current status if consistent with existing response style

7. **Persist with EF Core and add migration**
   - Update DbContext and entity configurations.
   - Add indexes for:
     - `(CompanyId, PaymentId)`
     - `(CompanyId, InvoiceId)` and/or `(CompanyId, BillId)` or equivalent polymorphic lookup
   - Add FK constraints and delete behavior carefully to avoid orphaned allocations.
   - Generate a migration with clear naming.

8. **Implement backfill logic**
   - Find where mocked accounting data is seeded or normalized.
   - Add a backfill routine that:
     - identifies existing mocked paid/partially paid invoices and bills lacking allocations
     - links them to available source payments where possible
     - synthesizes payments only when necessary and acceptable within current mock-data conventions
     - creates allocations that exactly match settled amounts
     - recalculates statuses after backfill
   - Make the backfill idempotent:
     - safe to run multiple times
     - no duplicate allocations/payments on rerun
   - Keep this logic clearly separated from runtime API behavior.

9. **Add automated tests**
   - Cover at minimum:
     - rejecting allocation when payment would be over-allocated
     - rejecting allocation when invoice/bill would be over-allocated
     - partial allocation persists exact amount and sets status to partially paid
     - one payment allocated across multiple documents
     - multiple payments allocated to one document
     - status recalculation to unpaid/partially paid/paid based on allocation totals
     - currency mismatch rejection
     - backfill creates inferred allocations for mocked paid docs and is idempotent
   - Prefer integration tests where they provide the most confidence, supplemented by focused domain tests if the project already uses them.

10. **Keep implementation aligned with existing patterns**
   - Reuse existing result/error abstractions, authorization, tenant resolution, and test fixtures.
   - Do not introduce a new generic accounting framework.
   - Add concise comments only where business rules are non-obvious.

# Validation steps
Run these checks after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration sanity**
   - Ensure the new migration compiles and applies cleanly in the project’s normal workflow.
   - Verify schema includes the new allocation table, FKs, and indexes.

4. **Manual/API verification**
   - Create a payment and two documents in the same tenant.
   - Allocate one payment across multiple documents and confirm:
     - allocations persist
     - totals are correct
     - document statuses update correctly
   - Allocate multiple payments to one invoice/bill and confirm status transitions.
   - Attempt over-allocation and confirm validation failure with safe error response.
   - Attempt currency mismatch and confirm rejection.
   - Query allocations by payment and by document and confirm returned links are correct and tenant-scoped.

5. **Backfill verification**
   - Run the backfill against existing mocked data.
   - Confirm allocations are generated for eligible paid/partially paid invoices and bills.
   - Confirm rerunning backfill does not duplicate records.
   - Confirm statuses remain correct after backfill.

6. **Code quality checks**
   - Ensure no cross-tenant data access paths were introduced.
   - Ensure settlement logic is centralized and not duplicated inconsistently across invoice and bill flows.

# Risks and follow-ups
- **Unknown existing accounting model**: payment/document entities may differ from assumptions. Adapt to actual code structure rather than forcing a polymorphic design.
- **Money precision risk**: avoid floating-point usage; use existing decimal precision conventions and database column definitions.
- **Status source-of-truth ambiguity**: if invoice/bill status is currently manually set, reconcile carefully so allocation-derived status does not conflict with legacy logic.
- **Backfill ambiguity**: mocked historical data may not contain enough provenance to infer exact source payments. If synthesis is required, keep it clearly marked, deterministic, and idempotent.
- **Concurrency risk**: allocation creation can race and allow over-allocation if implemented with naive read-then-write logic. Use transaction boundaries and locking/concurrency controls consistent with the current persistence approach.
- **API shape risk**: if there is already an accounting API pattern, follow it exactly rather than introducing new route conventions.

Follow-ups to note in code/TODOs only if truly needed:

- allocation reversal/update/delete flows
- audit events for allocation creation and status transitions
- outbox/domain events for downstream reporting
- richer settlement summaries on invoice/bill read models