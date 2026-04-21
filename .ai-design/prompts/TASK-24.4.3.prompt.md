# Goal
Implement backlog task **TASK-24.4.3 — Implement reconciliation service linking bank transactions to payments with status updates** for story **US-24.4 Implement bank transaction ingestion, reconciliation workflow, and cash ledger integration** in the existing .NET modular monolith.

Deliver a production-ready vertical slice that:
- introduces a `BankTransaction` domain entity and persistence model,
- adds database migration(s) with the required indexes,
- provides mock/bootstrap import for realistic tenant/company bank transactions,
- exposes tenant-scoped list/detail APIs with filtering,
- implements reconciliation linking bank transactions to one or more payments,
- updates reconciliation status correctly (`unreconciled`, `partially reconciled`, `reconciled`),
- posts or links the corresponding cash ledger event **exactly once**,
- includes automated tests, especially idempotency coverage.

Use the existing architecture and coding conventions already present in the repository. Prefer incremental, clean changes over speculative abstractions.

# Scope
In scope:
- Domain model additions for bank transactions and reconciliation state.
- Persistence and EF Core configuration/migrations in PostgreSQL.
- Application commands/queries/services for:
  - importing/bootstraping bank transactions,
  - listing/filtering/detail retrieval,
  - reconciling transactions to payments.
- API endpoints/controllers/minimal APIs for list/detail and reconciliation actions.
- Cash ledger integration logic with idempotent behavior.
- Automated tests across domain/application/API/infrastructure layers as appropriate.

Out of scope unless required by existing patterns:
- Real bank integrations or external bank APIs.
- Full UI/Blazor screens.
- Advanced matching heuristics or AI-assisted reconciliation suggestions.
- New messaging infrastructure.
- Broad refactors unrelated to this task.

Implementation constraints:
- Enforce tenant/company scoping on all reads/writes.
- Follow CQRS-lite patterns already used in the solution.
- Keep side effects idempotent and testable.
- Do not duplicate cash ledger events on retries or repeated reconciliation requests.
- If payment and cash-ledger models already exist, integrate with them rather than inventing parallel concepts.
- If exact naming differs in the codebase, align with existing conventions while preserving acceptance criteria semantics.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add/update:
- Domain
  - bank transaction entity/value objects/enums
  - reconciliation link entity if needed for many-to-many between bank transactions and payments
  - domain service or helper for reconciliation status calculation
- Infrastructure
  - EF Core `DbContext`
  - entity type configurations
  - migration(s)
  - repository/query implementations
  - bootstrap/mock data seeding/import service
- Application
  - commands/handlers for reconciliation
  - queries/handlers for list/detail retrieval
  - DTOs/contracts
  - validators
  - idempotency guard logic around cash ledger posting/linking
- API
  - endpoints/controllers for:
    - `GET /.../bank-transactions`
    - `GET /.../bank-transactions/{id}`
    - `POST /.../bank-transactions/{id}/reconcile`
  - request/response contracts
- Tests
  - migration/persistence tests if present in repo style
  - API integration tests for filtering and detail retrieval
  - reconciliation status tests
  - idempotency tests for cash ledger event creation/linking

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repo has existing finance/accounting modules, payment entities, or cash ledger services, update those files instead of creating disconnected implementations.

# Implementation plan
1. **Discover existing finance/payment/cash-ledger patterns**
   - Search the solution for:
     - `Payment`
     - `CashLedger`
     - `Ledger`
     - `FinanceSettings`
     - `company_id`
     - existing migrations and endpoint patterns
   - Identify:
     - how tenant scoping is enforced,
     - how entities are configured,
     - whether payments already support partial allocation,
     - whether cash ledger events already have idempotency keys or unique constraints,
     - whether bootstrap/seed patterns already exist for onboarded companies.

2. **Add domain model for bank transactions**
   - Introduce a `BankTransaction` aggregate/entity with at least:
     - `Id`
     - `CompanyId` or tenant/company identifier
     - `BankAccount`
     - `BookingDate`
     - `ValueDate`
     - `Amount`
     - `ReferenceText`
     - `Counterparty`
     - `Status`
     - timestamps if standard in repo
   - Add a status enum/string-backed enum for:
     - `Unreconciled`
     - `PartiallyReconciled`
     - `Reconciled`
   - Model reconciliation links to one or more payments.
     - Prefer an explicit join entity such as `BankTransactionPaymentReconciliation` if needed.
     - Include allocated/reconciled amount per payment if partial reconciliation is supported/needed by existing payment model.
   - Encapsulate status recalculation in domain logic:
     - no links/allocated amount = unreconciled
     - allocated amount > 0 but less than transaction amount = partially reconciled
     - allocated amount equals transaction amount = reconciled
   - Preserve sign handling consistently for inflow/outflow amounts.

3. **Add persistence and migration**
   - Update EF Core model configuration for `BankTransaction` and reconciliation link entity.
   - Create migration(s) to add storage with required indexes:
     - tenant/company identifier
     - `bank_account`
     - `booking_date`
     - `amount`
   - Add foreign keys to payments and company/tenant tables as appropriate.
   - If using snake_case PostgreSQL naming, follow existing conventions.
   - Add uniqueness/idempotency constraints where needed for cash ledger linkage, for example:
     - unique cash ledger event reference per bank transaction, or
     - unique `(company_id, bank_transaction_id, event_type)` constraint.
   - Ensure migration is safe and reversible.

4. **Implement mock import/bootstrap**
   - Add a mock import/bootstrap service that creates realistic bank transactions for onboarded companies.
   - Map imported/generated bank accounts to tenant/company finance settings using existing finance settings structures.
   - Generate realistic combinations:
     - incoming customer payments,
     - outgoing supplier payments,
     - fees,
     - payroll-like entries,
     - unmatched items.
   - Ensure generated data is tenant-scoped and deterministic enough for tests if needed.
   - If the repo has startup seed/bootstrap hooks, integrate there; otherwise add an application service callable from existing bootstrap flows.

5. **Implement application queries for list/detail**
   - Add query models and handlers for:
     - list bank transactions with filters:
       - bank account
       - date range
       - status
       - amount or amount range if existing API patterns support ranges
     - detail retrieval by id
   - Return reconciliation summary in DTOs, including linked payments if appropriate.
   - Enforce tenant/company scoping in all queries.
   - Add pagination if existing API conventions require it.

6. **Implement reconciliation command/service**
   - Add a command such as `ReconcileBankTransactionCommand` with:
     - bank transaction id
     - one or more payment ids
     - allocated amounts if needed
   - Validate:
     - transaction exists in tenant scope,
     - payments exist in tenant scope,
     - no cross-tenant linking,
     - allocated amounts are positive and do not exceed transaction amount,
     - duplicate payment links are rejected or merged safely,
     - reconciliation can be retried idempotently.
   - Update reconciliation links and recompute transaction status.
   - If existing payment entities track settlement/reconciliation state, update them only if required and consistent with current domain rules.

7. **Integrate cash ledger exactly once**
   - Reuse existing cash ledger posting/linking service if present.
   - On successful reconciliation, create or link the corresponding cash ledger event exactly once.
   - Implement idempotency using the repo’s preferred pattern:
     - deterministic idempotency key derived from company + bank transaction + reconciliation semantic identity,
     - unique DB constraint,
     - upsert/check-before-insert inside transaction.
   - Ensure repeated command execution or retry does not create duplicate ledger events.
   - If reconciliation changes from partial to full over time, define behavior clearly:
     - either one stable ledger event linked to the bank transaction and updated safely, or
     - one event created on first reconciliation and reused thereafter.
   - Keep this behavior explicit in code comments and tests.

8. **Expose API endpoints**
   - Add tenant-scoped endpoints following existing API style:
     - list endpoint with filters
     - detail endpoint
     - reconcile endpoint
   - Return appropriate status codes:
     - `200` for list/detail
     - `200` or `204` for successful reconciliation per existing conventions
     - `400` for validation issues
     - `404` for missing/out-of-scope resources
   - Ensure request/response contracts are stable and minimal.
   - Add authorization attributes/policies consistent with finance module patterns.

9. **Add automated tests**
   - Domain/application tests:
     - status transitions:
       - none linked => unreconciled
       - partial allocation => partially reconciled
       - full allocation => reconciled
     - one-to-many payment linking
   - API/integration tests:
     - list filtering by bank account/date/status/amount
     - detail retrieval
     - tenant isolation
   - Idempotency tests:
     - same reconciliation command executed twice only creates/links one cash ledger event
     - retry after transient failure does not duplicate ledger event
   - Migration/persistence tests if the repo has that pattern.
   - Prefer integration tests against real persistence behavior where idempotency/constraints matter.

10. **Polish and align**
   - Run formatting/build/tests.
   - Update any developer docs if migration/bootstrap usage needs explanation.
   - Keep code comments concise and only where behavior is non-obvious, especially around idempotency and reconciliation semantics.

# Validation steps
1. Inspect and understand existing patterns before coding:
   - search for finance/payment/ledger modules and tenant enforcement.
2. Build after each major slice:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Verify migration generation/application is correct:
   - ensure new migration exists and matches PostgreSQL conventions used in repo.
5. Manually validate acceptance criteria in code:
   - `BankTransaction` entity contains required fields.
   - migration creates storage and indexes on:
     - tenant/company identifier
     - bank account
     - booking date
     - amount
   - mock/bootstrap import creates realistic tenant/company transactions and maps bank accounts to finance settings.
   - list/detail APIs support required filters.
   - reconciliation links one transaction to one or more payments.
   - status updates correctly across unreconciled/partial/reconciled.
   - cash ledger event is posted/linked exactly once.
6. Add or run focused tests for idempotency:
   - repeated reconciliation request against same transaction/payment set
   - verify only one ledger event exists/linked record exists.
7. Include a short implementation summary in your final output:
   - files changed,
   - migration name,
   - endpoints added,
   - tests added,
   - any assumptions made due to existing codebase structure.

# Risks and follow-ups
- **Existing payment model mismatch:** Payments may not currently support partial allocation or reconciliation amounts. If so, introduce the smallest compatible reconciliation link model rather than overhauling payments.
- **Cash ledger semantics may already exist:** Avoid creating a second source of truth. Reuse existing ledger event concepts and add idempotency around them.
- **Tenant scoping gaps:** This task touches finance data; verify every query and command is company-scoped and covered by tests.
- **Amount/sign ambiguity:** Be explicit about debit/credit sign conventions and use consistent reconciliation math.
- **Bootstrap coupling:** If onboarding/bootstrap flows are immature, keep mock import behind an application service and wire it into existing setup hooks only where safe.
- **API contract drift:** Follow existing endpoint naming and response conventions if they differ from the examples above.
- **Migration/index naming:** Match repository naming conventions exactly to avoid churn.
- **Follow-up candidates, not required now:**
  - reconciliation suggestions/matching heuristics,
  - unreconcile/reverse workflow,
  - audit trail for reconciliation actions,
  - background import job scheduling,
  - UI screens in Blazor,
  - approval/policy hooks for sensitive reconciliation actions if finance governance requires it.