# Goal
Implement backlog task **TASK-26.1.1 — Implement cash settlement posting service for AR and AP journal generation** for story **US-26.1 Implement ledger posting for incoming and outgoing cash events with source traceability**.

Create a coding change in the existing **.NET modular monolith** that adds an application/domain service responsible for posting settled cash events into the ledger as balanced journal entries, with strict **tenant/company scoping**, **source traceability**, and **idempotent replay behavior**.

The implementation must satisfy these acceptance criteria:

- When a **customer payment** is marked settled, create **exactly one balanced journal entry**:
  - **Debit Cash**
  - **Credit Accounts Receivable**
  - Amount = settled amount
- When a **supplier payment** is marked settled, create **exactly one balanced journal entry**:
  - **Debit Accounts Payable**
  - **Credit Cash**
  - Amount = settled amount
- Each journal entry created from a payment or bank event stores a traceable source reference including:
  - `source type`
  - `source id`
  - `company id`
  - `posting timestamp`
- Reprocessing the same settled payment or bank event must be **idempotent**:
  - no duplicate journal entries
  - return the existing posting result
- Add automated tests covering:
  - incoming payment posting
  - outgoing payment posting
  - partial settlement handling
  - idempotent replay behavior

Produce a clean, minimal implementation aligned with the current solution structure and existing patterns. Prefer extending current accounting/ledger modules rather than inventing parallel abstractions.

# Scope
In scope:

- Inspect the solution to find existing:
  - ledger/journal entities
  - payment entities for customer/supplier cash settlement
  - posting services/handlers
  - persistence/repository patterns
  - migrations approach
- Add or extend domain/application logic for **cash settlement posting**
- Persist **source traceability metadata**
- Enforce **idempotency** at both service logic and persistence level where practical
- Support **partial settlement** by posting the actual settled amount supplied by the event/payment state
- Add/adjust tests
- Add schema migration if required for source reference uniqueness or posting metadata

Out of scope unless required by existing architecture:

- UI changes
- Mobile changes
- Broad refactors of unrelated accounting modules
- New external integrations
- Full workflow engine changes beyond the minimum hook needed to invoke posting
- Reworking chart-of-accounts design unless necessary to map Cash / AR / AP accounts already expected by the domain

Implementation expectations:

- Follow **Clean Architecture boundaries**
- Keep all operations **company/tenant scoped**
- Use **typed contracts** and existing repositories/services
- Prefer deterministic, testable application services
- If account resolution is needed, use existing company ledger/account configuration patterns; if absent, add the smallest viable resolver abstraction

# Files to touch
Inspect first, then update only the necessary files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - journal entry aggregate/entity
  - journal line entity/value objects
  - source reference value object/entity
  - payment settlement domain concepts
- `src/VirtualCompany.Application/**`
  - posting service / command handler / use case
  - DTOs/results for posting outcome
  - interfaces for repositories/account resolution/clock
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repository implementations
  - migrations
  - idempotency/unique index persistence rules
- `src/VirtualCompany.Api/**`
  - only if an endpoint/handler wiring change is required
- `tests/**`
  - unit tests and/or integration tests for posting behavior

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- any existing accounting, ledger, payment, or finance-related folders/classes in:
  - `src/VirtualCompany.Application`
  - `src/VirtualCompany.Domain`
  - `src/VirtualCompany.Infrastructure`

If the exact file names differ, adapt to the actual project structure rather than forcing new conventions.

# Implementation plan
1. **Discover existing accounting and payment model**
   - Search the solution for:
     - `Journal`
     - `Ledger`
     - `AccountsReceivable`
     - `AccountsPayable`
     - `Payment`
     - `Settlement`
     - `Posted`
     - `CompanyId`
     - `SourceType`
   - Identify:
     - current journal entry aggregate shape
     - how debit/credit lines are represented
     - whether source references already exist
     - where settled customer/supplier payments are modeled
     - whether there is already a posting pipeline or domain event handler

2. **Design the posting contract**
   - Add or extend an application service with a clear entry point, for example:
     - `PostCashSettlementAsync(...)`
   - Input should include enough data to support deterministic posting:
     - `companyId`
     - `sourceType`
     - `sourceId`
     - `payment direction/type` or equivalent
     - `settledAmount`
     - `settledAt` or posting timestamp source
     - any currency/account identifiers required by the existing model
   - Return a result indicating:
     - created vs existing
     - journal entry id/reference
     - posted amount
     - posting timestamp

3. **Implement source traceability**
   - Ensure the journal entry stores:
     - source type
     - source id
     - company id
     - posting timestamp
   - Reuse existing fields if already present
   - If missing, add the minimal schema/domain changes needed
   - Keep the source reference human-auditable and queryable

4. **Implement balanced journal generation**
   - For **incoming/customer payment settled**:
     - create one journal entry with two lines:
       - debit cash
       - credit accounts receivable
   - For **outgoing/supplier payment settled**:
     - create one journal entry with two lines:
       - debit accounts payable
       - credit cash
   - Use the **settled amount**, not invoice total or payment authorization amount
   - Ensure the journal entry is balanced exactly once per source event/payment settlement
   - If partial settlement occurs, post only the partial settled amount represented by the triggering settlement

5. **Resolve ledger accounts safely**
   - Use existing account resolution/configuration if available
   - If no resolver exists, add a small abstraction such as a company-scoped account resolver for:
     - Cash
     - Accounts Receivable
     - Accounts Payable
   - Do not hardcode database IDs
   - Fail with a clear domain/application error if required accounts are not configured

6. **Implement idempotency**
   - Before creating a journal entry, check whether a posting already exists for the same:
     - company id
     - source type
     - source id
     - optionally posting kind/category if needed by the model
   - If found:
     - return the existing posting result
     - do not create another journal entry
   - Add a **database uniqueness constraint/index** if appropriate to guarantee no duplicates under concurrency
   - Make the service resilient to race conditions:
     - either transactional check + insert with unique constraint handling
     - or repository method that atomically enforces uniqueness

7. **Wire invocation from settlement flow**
   - Find where customer/supplier payments become `settled`
   - Hook the posting service into the existing application flow with minimal disruption
   - If event-driven patterns already exist, attach to the relevant internal event/handler
   - If not, invoke directly from the settlement command handler/service
   - Ensure replaying the same event/command returns the same posting result

8. **Add tests**
   - Add automated tests for:
     - **incoming payment posting**
       - one journal entry
       - debit cash / credit AR
       - balanced totals
       - correct source metadata
     - **outgoing payment posting**
       - one journal entry
       - debit AP / credit cash
       - balanced totals
       - correct source metadata
     - **partial settlement handling**
       - posts settled amount only
       - remains balanced
     - **idempotent replay**
       - second processing does not create duplicate journal entry
       - returns existing result
   - Prefer unit tests for posting rules plus integration tests if repository uniqueness/mapping is involved

9. **Keep implementation production-safe**
   - Use existing transaction boundaries
   - Preserve tenant isolation via `company_id`
   - Keep timestamps UTC
   - Avoid exposing raw persistence exceptions; translate unique constraint collisions into idempotent success where appropriate

# Validation steps
1. Restore/build/test locally:
   - `dotnet build`
   - `dotnet test`

2. Verify behavior through automated tests:
   - Incoming/customer settled payment creates exactly one balanced journal entry
   - Outgoing/supplier settled payment creates exactly one balanced journal entry
   - Partial settlement posts only the settled amount
   - Replay/reprocessing returns existing posting result and does not duplicate entries

3. Verify persistence/model correctness:
   - Journal entry stores:
     - source type
     - source id
     - company id
     - posting timestamp
   - Unique/idempotency rule is enforced in code and, if added, in DB schema

4. If migrations are added:
   - Ensure migration files are generated/applied according to repo conventions
   - Confirm no unrelated schema drift is introduced

5. Sanity-check accounting invariants:
   - Debit total equals credit total
   - Correct account pairing by payment direction
   - No cross-company posting leakage

# Risks and follow-ups
- **Unknown existing accounting model**: the repo may already have journal posting abstractions or naming that differ from the task wording. Adapt to existing patterns rather than duplicating concepts.
- **Settlement granularity ambiguity**: if `source id` refers to payment id while multiple partial settlements can occur per payment, clarify whether idempotency should be per payment or per settlement event. If the codebase distinguishes bank event/settlement event IDs, prefer the most granular settled source that preserves acceptance criteria without blocking partial settlements.
- **Account resolution gaps**: if Cash/AR/AP accounts are not yet configurable in the domain, implement the smallest safe resolver and leave broader account setup UX for a follow-up.
- **Concurrency**: service-level checks alone are insufficient under parallel replay; back them with a unique constraint where feasible.
- **Migration conventions**: follow the repository’s established migration workflow from `docs/postgresql-migrations-archive/README.md`.
- **Follow-up candidates**:
  - expose posting audit events
  - add posting result query endpoint
  - support bank-event-originated settlement posting if not fully covered in this task
  - add observability around posting retries and duplicate replay detection