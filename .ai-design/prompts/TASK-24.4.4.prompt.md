# Goal
Implement backlog task **TASK-24.4.4 — Integrate reconciled cash events with ledger posting and add idempotency coverage** for story **US-24.4 Implement bank transaction ingestion, reconciliation workflow, and cash ledger integration**.

Deliver the missing application/domain/infrastructure changes so that:
- reconciling a bank transaction to one or more payments results in the corresponding cash ledger event being created or linked,
- that ledger side effect happens **exactly once** even if the reconciliation command is retried,
- automated tests prove idempotent behavior,
- and the implementation fits the existing modular monolith, CQRS-lite, PostgreSQL, and tenant-scoped patterns already used in the solution.

Use existing project conventions first. Prefer extending current finance, payments, reconciliation, and ledger flows over introducing parallel abstractions.

# Scope
In scope:
- Inspect the current implementation for:
  - `BankTransaction` entity/model and persistence
  - bank transaction APIs
  - reconciliation command/handler/service
  - payment linkage model
  - cash ledger event posting/linking flow
  - any existing idempotency or unique-key patterns
- Add or complete the domain/application logic that connects a successful reconciliation to cash ledger posting/linking.
- Ensure reconciliation status transitions support:
  - unreconciled
  - partially reconciled
  - reconciled
- Ensure one bank transaction can link to one or more payments.
- Add durable idempotency protection so repeated reconciliation attempts do not create duplicate cash ledger events.
- Add or update database constraints/indexes/migration if needed to enforce exact-once behavior at the persistence layer.
- Add automated tests covering:
  - first reconciliation creates/posts/links the ledger event
  - repeated identical reconciliation does not duplicate the ledger event
  - partial reconciliation behavior if supported by current model
  - tenant scoping and expected retrieval of linked ledger data where relevant

Out of scope unless required by existing code structure:
- building new UI screens
- redesigning the entire finance domain
- introducing a message broker
- broad refactors unrelated to reconciliation/cash ledger integration
- changing public API contracts beyond what is necessary for this task

# Files to touch
Start by locating the actual existing files, then update the concrete implementations you find. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - bank transaction aggregate/entity
  - reconciliation entities/value objects
  - payment linkage entities
  - cash ledger event entities
- `src/VirtualCompany.Application/**`
  - reconciliation commands/handlers
  - finance or ledger services
  - DTOs/query models for bank transaction detail
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories
  - migrations
  - seed/bootstrap/mock import logic
  - ledger posting persistence
- `src/VirtualCompany.Api/**`
  - reconciliation endpoints if API wiring is needed
  - bank transaction detail/list endpoints if linked ledger info is exposed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for reconciliation and idempotency
- possibly:
  - `README.md`
  - migration archive docs if the repo requires migration documentation updates

Before editing, identify the exact files that currently implement:
1. bank transaction storage,
2. reconciliation workflow,
3. payment entities,
4. cash ledger posting,
5. test fixtures/builders for finance scenarios.

# Implementation plan
1. **Inspect and map the current finance flow**
   - Find the current `BankTransaction` implementation and confirm acceptance criteria already covered by prior tasks.
   - Trace the reconciliation path from API/controller to application command handler to domain/infrastructure persistence.
   - Trace how cash ledger events are currently created for payments or other cash movements.
   - Identify whether there is already:
     - a `CashLedgerEvent`, `LedgerEntry`, or similar entity,
     - a reconciliation link table,
     - an idempotency key, external reference, or unique index pattern.

2. **Define the exact-once integration point**
   - Choose one canonical place where ledger posting/linking occurs when reconciliation succeeds:
     - ideally inside the reconciliation application service/command handler, or
     - a domain service invoked transactionally by the handler.
   - Do not scatter ledger creation across controller, repository, and background worker layers.
   - Ensure the integration is tenant-scoped and uses the same company/tenant identifier as the bank transaction and payment records.

3. **Model or complete reconciliation-to-ledger linkage**
   - If not already present, add a durable reference from the ledger event to the bank transaction reconciliation context, such as:
     - `bank_transaction_id`,
     - `reconciliation_id`,
     - or a deterministic idempotency key derived from tenant + bank transaction + reconciliation purpose.
   - Prefer a persistence-backed uniqueness guarantee, not only in-memory checks.
   - If the system distinguishes between “post new ledger event” and “link existing ledger event”, preserve that behavior and make the operation idempotent in both cases.

4. **Implement reconciliation status calculation**
   - Ensure the reconciliation flow recalculates bank transaction status based on linked payment amounts:
     - no links => `Unreconciled`
     - linked amount > 0 but < transaction amount => `PartiallyReconciled`
     - linked amount == transaction amount => `Reconciled`
   - Respect sign conventions and currency assumptions already present in the code.
   - If over-reconciliation is invalid, reject it with a domain/application validation error and add tests.

5. **Implement exact-once ledger posting/linking**
   - On successful reconciliation:
     - create or associate the cash ledger event corresponding to the bank transaction,
     - but only if one does not already exist for the same tenant/company and reconciliation identity.
   - Recommended approach:
     - first check for an existing ledger event/link by deterministic key,
     - then rely on a unique database constraint/index as the final guard,
     - handle duplicate-key races safely by reloading the existing record instead of failing the operation when appropriate.
   - Keep the reconciliation and ledger persistence in the same transaction if the current architecture allows it.

6. **Add or update EF Core configuration and migration**
   - Add any missing columns, foreign keys, and indexes needed for:
     - bank transaction to ledger linkage,
     - unique idempotency enforcement,
     - efficient lookup by tenant/company and bank transaction/reconciliation reference.
   - Generate a migration in the repo’s existing style.
   - If the repo archives migrations separately, update the expected docs/index accordingly.

7. **Expose linked ledger information only if already consistent with current API patterns**
   - If bank transaction detail responses already include reconciliation metadata, extend them to include linked ledger event identifiers/status only when useful and low-risk.
   - Do not invent a new API surface unless needed by tests or existing consumers.

8. **Add automated tests**
   - Add integration tests first, using the existing API/application test style.
   - Cover at minimum:
     - reconciling a bank transaction with one or more payments creates/posts/links one cash ledger event,
     - retrying the same reconciliation command/request does not create a second ledger event,
     - repeated handler execution after a simulated transient failure still results in one ledger event,
     - partial reconciliation sets `PartiallyReconciled`,
     - full reconciliation sets `Reconciled`,
     - removing or changing links behaves according to current domain rules if supported.
   - If there are repository-level tests for unique constraints or EF mappings, add those too.

9. **Preserve auditability and clean boundaries**
   - If the codebase already records audit events for finance actions, emit or update the relevant audit event for reconciliation/ledger posting.
   - Keep HTTP concerns in API, orchestration in Application, invariants in Domain, and persistence details in Infrastructure.

10. **Document assumptions in code comments or test names**
   - Be explicit about:
     - whether one bank transaction maps to one cash ledger event total,
     - whether partial reconciliations reuse the same ledger event or defer posting until fully reconciled,
     - whether the ledger event represents the bank movement itself versus payment settlement linkage.
   - Match the existing domain language in the repository.

# Validation steps
Run and report the results of the relevant commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If the repo has targeted test projects or filters for finance/API tests, run those as well.

4. Validate migration integrity:
   - ensure the new migration compiles,
   - ensure EF model snapshot is updated if the repo uses snapshots,
   - verify indexes/unique constraints match the idempotency design.

5. Manually verify in tests or debug assertions:
   - a reconciled transaction posts/links exactly one ledger event,
   - retrying the same reconciliation does not duplicate the ledger event,
   - bank transaction status transitions are correct,
   - tenant/company scoping is preserved.

Include in your final summary:
- files changed,
- migration name,
- idempotency strategy used,
- tests added/updated,
- any assumptions or follow-up gaps.

# Risks and follow-ups
- The existing code may already have partial ledger posting behavior under a different name; avoid duplicating concepts.
- If reconciliation currently allows mutation after posting, idempotency semantics may be subtle. Preserve existing business rules and document any ambiguity.
- If partial reconciliation semantics are not clearly defined in code, implement the smallest consistent rule set and call out the assumption.
- Concurrency races are possible if two reconciliation requests hit the same transaction simultaneously; prefer a DB unique constraint plus transactional handling.
- If there is no existing cash ledger module yet, integrate with the nearest current finance ledger abstraction rather than inventing a large new subsystem.
- If API tests are expensive or sparse, add focused application/integration tests around the command handler and persistence boundary.
- Follow-up candidates after this task:
  - explicit unreconcile flow and ledger reversal behavior,
  - richer audit trail for reconciliation decisions,
  - outbox/event publication for downstream finance reporting,
  - stronger concurrency tests around simultaneous reconciliation attempts.