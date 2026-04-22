# Goal
Implement backlog task **TASK-28.1.2 — Extend finance entities with source event references and causal linkage fields** for story **US-28.1 Implement simulation event causality and object linkage across invoices, bills, payments, assets, and cash**.

The coding agent should update the .NET modular monolith so that simulated finance records persist deterministic simulation causality metadata and cash snapshots, enabling traceability and replay consistency.

Success means:
- Every simulated **invoice, bill, payment, asset, and cash-affecting transaction** stores a **source simulation event reference**.
- Any **payment allocation** can resolve the **payment**, **target document**, and **originating simulation event** through a single query path.
- Cash-affecting events persist **before cash**, **after cash**, and **delta amount** for the affected company and simulation date.
- Replaying the same company seed and start date yields the same **event identifiers**, **object references**, and **causal links**.

# Scope
In scope:
- Inspect the existing finance/simulation domain model, EF Core mappings, migrations approach, and any seed/replay identifier generation logic.
- Add or extend persistence fields on relevant finance entities for:
  - source simulation event reference
  - causal linkage identifiers
  - cash before/after/delta values where applicable
- Update payment allocation persistence so linked traversal is explicit and efficient.
- Ensure deterministic identifier/reference generation for replayed simulations.
- Add/update EF Core configuration, migrations, repositories/queries, and tests.
- Add minimal documentation/comments where needed to clarify deterministic causality rules.

Out of scope unless required by existing architecture:
- New UI screens
- Broad refactors unrelated to finance simulation causality
- Reworking the entire simulation engine
- Event sourcing adoption
- Non-simulated/manual finance record backfill beyond what is necessary to keep schema valid

Implementation constraints:
- Preserve tenant/company scoping.
- Prefer additive schema changes and backward-compatible defaults where possible.
- Follow existing modular monolith boundaries and CQRS-lite patterns.
- Do not introduce direct DB-only shortcuts that bypass domain/application contracts already in use.
- Keep deterministic replay logic explicit and testable.

# Files to touch
Start by locating the actual finance and simulation implementation, then update the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities: invoices, bills, payments, assets, cash transactions, payment allocations
  - simulation event/value objects or identifiers
- `src/VirtualCompany.Application/**`
  - commands/handlers/services that create simulated finance records
  - query handlers for payment allocation traversal
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations
  - repository/query implementations
  - deterministic ID/reference generation persistence support
- `src/VirtualCompany.Api/**`
  - only if contracts/endpoints need adjustment for existing tests or exposed DTOs
- `tests/**`
  - domain/application/integration tests covering causality persistence, query path resolution, and deterministic replay

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If finance simulation code is not yet isolated, identify the concrete files before editing and keep changes localized.

# Implementation plan
1. **Discover current model and flow**
   - Find all finance entities involved in simulation:
     - invoice
     - bill
     - payment
     - payment allocation
     - asset
     - cash-affecting transaction/journal-like record
   - Find the simulation event model and how event IDs are currently generated.
   - Find where replay determinism is currently enforced or implied.
   - Identify whether payment allocations currently point to both payment and target document, and whether target document is polymorphic.

2. **Design the persistence additions**
   - Add a consistent source event reference shape across simulated finance entities. Prefer existing naming conventions, but the target model should support:
     - `SourceSimulationEventId` or equivalent deterministic event identifier
     - optional causal/reference fields if separate from source event
   - For payment allocations, ensure the persisted model can traverse in one query path to:
     - allocation -> payment
     - allocation -> target document
     - allocation -> source/originating simulation event
   - For cash-affecting records, add:
     - `CashBefore`
     - `CashDelta`
     - `CashAfter`
     - simulation date if not already present in the cash-affecting record
   - If target document can be invoice or bill, use the existing polymorphic pattern in the codebase; otherwise add a minimal explicit target type + target id pair.

3. **Make deterministic replay explicit**
   - Review current event/object ID generation.
   - Ensure event identifiers and downstream object references are derived deterministically from stable inputs such as:
     - company id
     - seed
     - simulation start date
     - simulation date
     - event sequence/order
     - event type
   - Remove any accidental nondeterminism from GUID generation, timestamps used as IDs, unordered iteration, or random allocation ordering.
   - If needed, introduce a deterministic ID factory/service already aligned with project patterns rather than ad hoc generation in handlers.

4. **Update domain entities and invariants**
   - Extend entities/value objects with the new fields.
   - Enforce invariants where appropriate:
     - simulated records must have a source simulation event reference
     - cash-affecting simulated records must have before/delta/after values
     - payment allocations must retain enough linkage to resolve target and origin
   - Keep manual/non-simulated records valid via nullable fields only if required by existing data model.

5. **Update EF Core mappings and schema**
   - Add columns and relationships in infrastructure mappings.
   - Add indexes supporting likely query paths, especially:
     - by `CompanyId`
     - by `SourceSimulationEventId`
     - payment allocation traversal fields
     - cash-affecting transaction lookup by company/date/event
   - Create a migration with clear names and safe defaults.
   - Ensure PostgreSQL types are appropriate for money/decimal precision already used in the project.

6. **Update creation flows**
   - Modify simulation handlers/services so every simulated finance object persists the source event reference.
   - Ensure payment allocations persist causal linkage at creation time, not via later enrichment.
   - Ensure cash-affecting events compute and persist before/delta/after atomically within the same transaction scope as the affected record creation.

7. **Implement/query the single-path resolution**
   - Add or update a repository/query method that can load payment allocation linkage in one query path.
   - This should return enough data to prove acceptance criteria:
     - allocation id
     - payment id/reference
     - target document id/type/reference
     - originating simulation event id/reference
   - Prefer eager loading/projection over multiple round trips.

8. **Add tests**
   - Add focused tests for:
     - simulated invoice/bill/payment/asset/cash transaction stores source event reference
     - payment allocation query resolves payment + target document + originating event in one logical query path
     - cash-affecting events persist before/delta/after correctly
     - replaying same company seed and start date reproduces same event IDs/object references/causal links
   - Include at least one integration test around persistence if the repo already supports EF/PostgreSQL or provider-backed tests.
   - If deterministic replay depends on ordering, add a regression test for stable ordering.

9. **Document assumptions**
   - If the codebase lacks a unified “cash-affecting transaction” abstraction, document which entity/entities were treated as cash-affecting for this task.
   - If polymorphic target documents are modeled in a specific way, note that in code comments or test names.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and model snapshot is consistent:
   - Ensure the new migration is included and builds cleanly.

4. Manually validate acceptance criteria in tests or a focused local run:
   - Create/replay a simulation for the same company seed and start date twice.
   - Confirm identical:
     - simulation event IDs
     - simulated object references/IDs where expected
     - payment allocation causal links
   - Confirm each simulated:
     - invoice
     - bill
     - payment
     - asset
     - cash-affecting transaction
     has a source simulation event reference.
   - Confirm a payment allocation query/projection can return:
     - linked payment
     - target document
     - originating simulation event
   - Confirm cash-affecting records persist:
     - before
     - delta
     - after
     for the correct company and simulation date.

5. If there are API or query contract tests, update and rerun them.

# Risks and follow-ups
- **Model ambiguity risk:** The codebase may not yet have a single canonical “cash-affecting transaction” entity. If multiple entities affect cash, apply the metadata consistently and document the chosen coverage.
- **Polymorphic linkage risk:** Payment allocations targeting multiple document types may require explicit discriminator fields if not already modeled.
- **Determinism risk:** Hidden nondeterminism can come from unordered collections, DB-generated IDs, `Guid.NewGuid()`, `DateTime.UtcNow`, or unstable LINQ ordering. Audit all simulation creation paths.
- **Migration risk:** Existing data may require nullable additions or backfill defaults to avoid breaking environments.
- **Query-path interpretation risk:** “Single query path” should be implemented as a single navigable projection/load path, not a chain of separate lookups.
- **Precision risk:** Cash before/delta/after fields must use the project’s established decimal precision to avoid replay mismatches.

Follow-ups to note if not fully covered by this task:
- Add user-facing audit/explainability views for simulation causality.
- Add indexes/perf tuning after real query plans are observed.
- Consider a shared simulation causality interface/base type if multiple entities now duplicate the same fields.
- Add backfill/migration strategy for legacy simulated records if historical data already exists.