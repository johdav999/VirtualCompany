# Goal
Implement backlog task **TASK-26.1.2 — Add source traceability mappings between payments, bank transactions, and journal entries** for story **US-26.1 Implement ledger posting for incoming and outgoing cash events with source traceability**.

The coding agent should add domain, application, infrastructure, and test support so that:
- settled **customer payments** create exactly one balanced journal entry:
  - **debit cash**
  - **credit accounts receivable**
- settled **supplier payments** create exactly one balanced journal entry:
  - **debit accounts payable**
  - **credit cash**
- every journal entry created from a payment or bank event stores a traceable source reference containing:
  - `source type`
  - `source id`
  - `company id`
  - `posting timestamp`
- replaying/reprocessing the same settled payment or bank event is **idempotent**
  - no duplicate journal entries
  - existing posting result is returned
- automated tests cover:
  - incoming payment posting
  - outgoing payment posting
  - partial settlement handling
  - idempotent replay behavior

Use the existing solution structure and architectural style:
- modular monolith
- CQRS-lite application layer
- PostgreSQL-backed persistence
- tenant-aware `company_id` enforcement
- auditable, deterministic posting behavior

# Scope
In scope:
- inspect current finance/accounting/ledger/payment models and posting flow
- add or extend a **source traceability model** linking journal entries to originating payment or bank transaction events
- implement **idempotent posting** for settled payment/bank events
- support both **incoming/customer** and **outgoing/supplier** payment settlement posting
- ensure **partial settlement** posts only the settled amount and remains replay-safe
- persist source metadata in the transactional store
- add/adjust repository queries and constraints needed for uniqueness and lookup
- add/adjust automated tests

Out of scope unless already required by existing code paths:
- broad redesign of accounting architecture
- UI work
- unrelated workflow/approval changes
- external integrations
- full bank reconciliation logic beyond source traceability and idempotent posting hooks
- changing unrelated posting rules or chart-of-accounts strategy

If the codebase already has adjacent concepts with different names, prefer extending the existing model rather than introducing parallel abstractions.

# Files to touch
Start by locating the existing finance and ledger implementation, then update the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - payment entities/value objects
  - bank transaction entities/value objects
  - journal entry entities/value objects
  - source reference / posting result models
- `src/VirtualCompany.Application/**`
  - commands/handlers for payment settlement and posting
  - ledger posting services
  - idempotency/replay handling
  - DTOs/results returned by posting operations
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations / persistence mappings
  - repositories
  - migrations or SQL schema updates
  - unique indexes for source-based idempotency
- `src/VirtualCompany.Api/**`
  - only if API contracts or endpoints must expose posting results
- `tests/VirtualCompany.Api.Tests/**`
  - integration and/or API-level tests for posting behavior

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repository uses a dedicated migrations project or SQL scripts instead of EF migrations, follow the existing convention exactly.

# Implementation plan
1. **Discover existing accounting and payment flow**
   - Find current models for:
     - customer payments
     - supplier payments
     - bank transactions
     - journal entries
     - ledger posting services/handlers
   - Identify where a payment becomes “settled”.
   - Identify whether journal entries already exist and how balanced lines are represented.
   - Identify tenant scoping patterns and existing idempotency patterns.

2. **Design the traceability model using existing conventions**
   - Add a source reference representation on journal entries or a dedicated mapping table, depending on current architecture.
   - The persisted source traceability must include:
     - source type
     - source id
     - company id
     - posting timestamp
   - Prefer one of these patterns based on existing design:
     - fields directly on `JournalEntry`
     - a `JournalEntrySourceReference` owned entity/value object
     - a separate `JournalEntrySourceMapping` table with a uniqueness constraint
   - Ensure the design supports both:
     - payment-originated postings
     - bank-event-originated postings

3. **Enforce idempotency at the persistence boundary**
   - Add a uniqueness rule so the same source event cannot create multiple journal entries for the same company and source identity.
   - Recommended uniqueness shape:
     - `(company_id, source_type, source_id)`
   - If partial settlement requires multiple distinct postings for different settlement events, align uniqueness with the actual event identity already present in the model rather than raw payment id alone.
   - If the current model only has payment id and settled amount/state, implement idempotency around the exact posting trigger semantics already used by the system.
   - On replay:
     - detect existing posting by source reference
     - return the existing posting result
     - do not create a duplicate journal entry

4. **Implement posting rules**
   - For settled customer payment:
     - create exactly one journal entry
     - debit cash
     - credit accounts receivable
     - amount = settled amount
   - For settled supplier payment:
     - create exactly one journal entry
     - debit accounts payable
     - credit cash
     - amount = settled amount
   - Ensure the journal entry is balanced.
   - Reuse existing account resolution logic if present; do not hardcode account IDs if the system already has account lookup/configuration.

5. **Handle partial settlement correctly**
   - Determine how partial settlement is represented today:
     - cumulative settled amount
     - settlement event amount
     - installment/payment allocation
   - Implement posting so that only the newly settled amount or exact settled event amount is posted.
   - Preserve idempotency for replay of the same partial settlement trigger.
   - Do not over-post the full invoice/payment amount when only part is settled.

6. **Return a stable posting result**
   - Ensure posting operations return a result object that can represent:
     - newly created posting
     - existing posting returned due to replay/idempotency
   - Include journal entry identifier and source metadata if consistent with current patterns.

7. **Persist schema changes**
   - Add/update database schema and mappings.
   - Include:
     - source traceability columns/table
     - posting timestamp
     - unique index/constraint for idempotency
   - Follow the repository’s migration approach.
   - Keep names explicit and finance-safe.

8. **Add automated tests**
   - Add tests covering:
     - incoming payment settled → one balanced journal entry with correct debit/credit and source metadata
     - outgoing payment settled → one balanced journal entry with correct debit/credit and source metadata
     - partial settlement → posts only settled amount
     - replay/reprocessing same settled event → no duplicate journal entry, existing result returned
   - Assert tenant/company scoping where relevant.
   - Prefer integration tests if persistence/idempotency is enforced at DB level.

9. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - domain rules in domain/application services
     - persistence concerns in infrastructure
   - Avoid controller-heavy logic.
   - Keep behavior deterministic and auditable.

10. **Document assumptions in code comments or PR notes**
   - If the current codebase lacks explicit bank-event posting flow, implement the reusable source traceability abstraction now and wire payment settlement fully, leaving bank-event consumption ready for follow-up.
   - If account resolution is incomplete, use the existing finance configuration path and note any gap clearly.

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Verify automated tests specifically cover:
   - customer payment settlement posting
   - supplier payment settlement posting
   - partial settlement amount handling
   - idempotent replay behavior

3. Validate persistence behavior:
   - confirm exactly one journal entry exists after first processing
   - confirm replay does not create a second entry
   - confirm source metadata is persisted with:
     - source type
     - source id
     - company id
     - posting timestamp

4. Validate accounting correctness:
   - each created journal entry is balanced
   - incoming payment:
     - debit cash
     - credit accounts receivable
   - outgoing payment:
     - debit accounts payable
     - credit cash

5. Validate tenant safety:
   - source lookup and idempotency checks are scoped by `company_id`

6. If migrations are added:
   - ensure migration files are generated/applied according to repo conventions
   - ensure tests pass against the updated schema

# Risks and follow-ups
- **Partial settlement ambiguity:** if the current model does not distinguish settlement event identity from payment identity, idempotency and partial posting semantics may need a follow-up refinement. Prefer existing event identifiers if available.
- **Account resolution gaps:** if cash/AR/AP accounts are not yet configurable or modeled consistently, avoid inventing a parallel mechanism; integrate with current finance setup and note any missing configuration dependency.
- **Bank event coverage:** acceptance criteria mention payment or bank event source references. If bank transaction posting is not yet implemented, build the source traceability abstraction to support it and fully cover payment settlement now.
- **Migration strategy variance:** the repo may use archived/manual PostgreSQL migration patterns rather than standard EF migrations; follow the established approach.
- **Concurrency:** if settlement processing can run concurrently, DB-level uniqueness is required in addition to application-level checks.
- **Audit follow-up:** if there is an existing audit/event subsystem for finance postings, consider a follow-up task to emit explicit audit events for posting creation vs replay-returned results.