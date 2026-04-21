# Goal
Implement backlog task **TASK-26.1.3 — Add idempotency guards and replay-safe posting logic for cash events** for story **US-26.1 Implement ledger posting for incoming and outgoing cash events with source traceability**.

Deliver a production-ready .NET implementation in the existing modular monolith that ensures settled customer and supplier cash events post to the ledger **exactly once**, remain **balanced**, are **traceable to their source**, and are **safe to replay** without creating duplicate journal entries.

# Scope
Implement the minimum complete slice needed to satisfy the acceptance criteria:

- Add or extend domain/application/infrastructure support for posting ledger journal entries from:
  - settled customer payments
  - settled supplier payments
  - replayed payment or bank-event settlement notifications
- Enforce **idempotency** using a durable uniqueness strategy tied to source reference:
  - source type
  - source id
  - company id
  - posting timestamp
- Ensure replay of the same settled event returns the existing posting result instead of creating a duplicate
- Support **partial settlement handling** according to current domain model semantics:
  - if partial settlements are represented as distinct settlement events, each unique settlement event should post once
  - if a payment can transition through partial settled amounts cumulatively, post only the newly settled amount once per unique source settlement reference
- Persist source traceability on journal entries
- Add automated tests covering:
  - incoming payment posting
  - outgoing payment posting
  - partial settlement handling
  - idempotent replay behavior

Do **not** broaden scope into unrelated accounting features, reporting, UI work, or generalized event bus redesign unless required by existing architecture.

# Files to touch
Inspect the solution first and update the actual relevant files, likely across these projects:

- `src/VirtualCompany.Domain`
  - payment / bank event entities and enums
  - journal entry / ledger posting entities and value objects
  - source reference or posting result models
- `src/VirtualCompany.Application`
  - commands / handlers / services for settlement-triggered posting
  - idempotent posting orchestration
  - DTOs / result contracts
- `src/VirtualCompany.Infrastructure`
  - EF Core configurations
  - repositories
  - migrations or persistence mappings
  - unique indexes / concurrency handling
- `src/VirtualCompany.Api`
  - only if an endpoint or webhook handler must return existing posting results on replay
- `tests/VirtualCompany.Api.Tests`
  - integration or API-level tests
- Potentially add a migration if schema changes are required for:
  - source reference columns
  - posting timestamp
  - unique constraints/indexes for idempotency

Before coding, locate the existing accounting/ledger/payment modules and align with current naming and patterns rather than inventing parallel abstractions.

# Implementation plan
1. **Discover current implementation**
   - Find existing models for:
     - customer payments
     - supplier payments
     - bank events
     - journal entries / ledger lines
     - settlement status transitions
   - Identify where posting currently happens, or where it should happen in the application layer
   - Determine whether partial settlements are modeled as:
     - separate settlement records/events, or
     - mutable payment status/amount fields

2. **Define idempotency strategy**
   - Use a durable source-based idempotency key derived from the business source, not an in-memory lock
   - Preferred uniqueness dimensions:
     - `company_id`
     - `source_type`
     - `source_id`
     - optional settlement discriminator if needed for partial settlements
   - Store posting metadata on the journal entry itself or a dedicated posting/source-reference table if that better matches the current model
   - Add a database-level unique constraint/index to guarantee exactly-once behavior under concurrency

3. **Add source traceability**
   - Ensure each journal entry created from a payment or bank event stores:
     - source type
     - source id
     - company id
     - posting timestamp
   - If the current journal entry aggregate already has metadata fields, extend them cleanly
   - If not, introduce a dedicated source reference value object/entity and map it via EF Core

4. **Implement replay-safe posting service**
   - Create or update an application service/command handler that:
     - validates the payment/bank event is in a settled state
     - computes the amount to post
     - checks for an existing posting by source reference
     - returns the existing posting result if found
     - otherwise creates exactly one balanced journal entry
   - Incoming customer payment posting:
     - debit cash
     - credit accounts receivable
   - Outgoing supplier payment posting:
     - debit accounts payable
     - credit cash
   - Ensure journal lines balance exactly to the settled amount

5. **Handle concurrency safely**
   - Make the implementation robust when the same event is processed multiple times concurrently
   - Preferred pattern:
     - check for existing posting
     - attempt insert within transaction
     - on unique constraint violation, re-query and return existing result
   - Do not rely solely on pre-insert existence checks

6. **Implement partial settlement behavior**
   - Match the existing domain semantics discovered in step 1
   - Ensure partial settlement posting is idempotent per unique settlement occurrence
   - Avoid duplicate posting when the same partial settlement is replayed
   - Add comments in code where the settlement identity rules are important

7. **Return stable posting result**
   - Standardize a result contract such as:
     - created/new posting
     - existing/replayed posting
     - journal entry id
     - posted amount
     - source reference
     - posting timestamp
   - Use this result consistently so replay callers receive the same effective outcome

8. **Persistence and migration**
   - Add EF configuration and migration for any new columns/indexes
   - Name indexes/constraints clearly for maintainability
   - Keep multi-tenant scoping explicit with `company_id`

9. **Tests**
   - Add automated tests that prove:
     - settled customer payment creates one balanced journal entry with correct debit/credit accounts
     - settled supplier payment creates one balanced journal entry with correct debit/credit accounts
     - partial settlement posts the correct amount once per unique settlement
     - replaying the same settled event returns the existing posting result and does not create duplicates
     - source traceability fields are persisted
   - Prefer integration tests against the real persistence layer if the project already uses them for accounting flows

10. **Code quality**
   - Follow existing solution conventions, namespaces, MediatR/CQRS patterns, and EF configuration style
   - Keep logic in application/domain layers, not controllers
   - Add concise comments only where idempotency or settlement semantics are non-obvious

# Validation steps
Run and report the results of the relevant validation commands:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If test scope is large, at minimum run the most relevant project:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. Manually verify through tests or targeted assertions that:
   - one settled customer payment => exactly one journal entry
   - one settled supplier payment => exactly one journal entry
   - each created journal entry is balanced
   - source type, source id, company id, and posting timestamp are stored
   - replay returns existing posting result
   - duplicate journal entries are prevented even under repeated processing

Include in your final summary:
- files changed
- migration added or not
- idempotency key/uniqueness approach used
- how partial settlement identity was handled
- test coverage added

# Risks and follow-ups
- The biggest risk is ambiguity in how **partial settlements** are represented in the current domain. Resolve this by inspecting the existing model first and implementing idempotency around the actual settlement identity, not assumptions.
- If there is no existing ledger source-reference structure, avoid overengineering; add the smallest durable schema that supports traceability and uniqueness.
- If bank events and payment settlements are separate ingestion paths, ensure both converge on the same replay-safe posting rules.
- If concurrency tests are not practical in the current suite, at least enforce correctness with a database unique constraint and test replay behavior deterministically.
- If account selection for cash/AR/AP is currently hardcoded or incomplete, keep changes minimal and consistent with existing accounting configuration, but note any gaps as follow-up work.
- Follow-up candidates after this task:
  - explicit posting audit events
  - outbox/event publication for successful postings
  - reversal/correction flows
  - stronger concurrency/integration tests against PostgreSQL-specific unique constraint behavior