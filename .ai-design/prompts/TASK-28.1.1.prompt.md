# Goal

Implement backlog task **TASK-28.1.1 — Add `SimulationEventRecord` persistence model with deterministic event identity and source metadata** for story **US-28.1 Implement simulation event causality and object linkage across invoices, bills, payments, assets, and cash**.

Deliver a production-ready vertical slice in the existing .NET solution that introduces a durable persistence model for simulation events and wires source-event references into simulated finance entities so causal links are queryable and deterministic across replays.

The implementation must satisfy these outcomes:

- Every simulated invoice, bill, payment, asset, and cash-affecting transaction persists a reference to its originating simulation event.
- Payment allocations can resolve linked payment, target document, and originating simulation event through a single query path.
- Cash-affecting events persist before/after cash values and delta amount for the affected company and simulation date.
- Replaying the same company seed and start date yields the same event identifiers, object references, and causal links.

# Scope

In scope:

- Add a new persistence model/entity for `SimulationEventRecord`.
- Define deterministic event identity generation rules based on replay-stable inputs.
- Persist source metadata needed for causality tracing.
- Add foreign key/reference fields from simulated financial entities to `SimulationEventRecord`.
- Ensure payment allocation linkage supports efficient traversal from allocation -> payment -> target document -> originating event.
- Persist cash before/after/delta values for cash-affecting events.
- Add EF Core configuration, migration, and any repository/query support needed.
- Add domain/application tests proving determinism and linkage behavior.
- Keep implementation tenant/company scoped.

Out of scope unless required to make this task compile and pass:

- Full simulation engine redesign.
- New UI screens.
- Broad refactors unrelated to simulation persistence.
- Event sourcing architecture.
- Non-deterministic/random ID generation for simulation artifacts.

Assumptions to validate in the codebase before implementation:

- Existing simulated entities for invoices, bills, payments, assets, cash transactions, and payment allocations already exist or have near-equivalent models.
- There is an existing simulation run/seed/start-date concept or adjacent model that can be used as deterministic identity input.
- EF Core migrations are the persistence mechanism in this repo.

# Files to touch

Inspect the solution first, then update the actual matching files. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/...`
  - Add `SimulationEventRecord` domain entity/value objects/enums if domain-owned.
  - Update simulated invoice/bill/payment/asset/cash-related entities with `SimulationEventRecord` reference fields.
  - Update payment allocation entity if needed for navigation/query path support.

- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configurations for `SimulationEventRecord`.
  - EF Core configuration updates for affected simulated entities.
  - DbContext updates.
  - Deterministic event ID generator implementation if infrastructure-owned.
  - Repository/query implementations.
  - New migration.

- `src/VirtualCompany.Application/...`
  - Application contracts/DTOs/services if simulation persistence is orchestrated here.
  - Query handlers or mapping logic for causal traversal if already patterned this way.

- `src/VirtualCompany.Api/...`
  - Only if API wiring or DI registration is required.

- `tests/...`
  - Unit tests for deterministic identity generation.
  - Integration tests for persistence, FK relationships, and single-query-path resolution.
  - Replay determinism tests.

Also inspect:

- Existing migration conventions.
- Existing naming conventions for entities, IDs, enums, and timestamps.
- Existing simulation-related modules to align terminology and boundaries.

# Implementation plan

1. **Discover existing simulation model and persistence boundaries**
   - Find all current simulation-related entities, especially invoices, bills, payments, assets, cash transactions, and payment allocations.
   - Identify where simulation runs, seeds, company IDs, and start dates are stored.
   - Identify whether IDs are GUIDs, ULIDs, strings, or composite keys.
   - Identify current query patterns for payment allocations and linked documents.

2. **Design the `SimulationEventRecord` model**
   - Add a new entity named exactly `SimulationEventRecord` unless a strongly established naming convention requires a suffix/prefix.
   - Include fields sufficient to satisfy acceptance criteria and future auditability. Prefer explicit columns over opaque JSON for core query fields.
   - Recommended fields, adapted to existing conventions:
     - `Id` or `EventId` — deterministic identifier
     - `CompanyId`
     - `SimulationRunId` if available
     - `SimulationDate`
     - `EventType`
     - `SourceEntityType` or produced object type
     - `SourceEntityId` or produced object reference if appropriate
     - `ParentEventId` or causal predecessor reference if useful and consistent with current model
     - `SequenceNumber` or stable ordinal within replay if needed
     - `ExternalReference`/`EventKey`/`DeterministicKeyMaterial` only if useful for debugging
     - `CashBefore`
     - `CashDelta`
     - `CashAfter`
     - `MetadataJson` only for non-core extensibility
     - `CreatedAt`
   - Make cash fields nullable for non-cash-affecting events and required for cash-affecting events if feasible via validation.

3. **Implement deterministic event identity**
   - Create a deterministic event identity generator service or value object.
   - Do not use random GUID generation.
   - Identity must be derived from replay-stable inputs only, such as:
     - `CompanyId`
     - simulation seed
     - simulation start date
     - simulation date
     - event type
     - stable sequence/ordinal within the run
     - stable business object discriminator where needed
   - Use a deterministic hash-based approach and map into the project’s ID type convention.
   - Ensure canonical input formatting:
     - invariant culture
     - normalized casing
     - explicit delimiters
     - ISO date formatting
   - Add tests proving same inputs => same ID and changed causal inputs => changed ID.

4. **Wire source event references into simulated entities**
   - For each simulated entity type in scope, add a nullable-or-required FK/reference to `SimulationEventRecord` as appropriate:
     - invoice
     - bill
     - payment
     - asset
     - cash-affecting transaction
   - Prefer required references for newly created simulated records if the domain guarantees all such records originate from simulation.
   - Add navigation properties where consistent with existing EF style.
   - Ensure indexes exist on `CompanyId + SimulationEventRecordId` or equivalent query paths.

5. **Support payment allocation causal traversal**
   - Ensure payment allocation can resolve:
     - linked payment
     - target document
     - originating simulation event
   - If payment allocation already references payment and target document, ensure those entities reference `SimulationEventRecord`.
   - If target document can be invoice or bill, preserve existing polymorphic strategy and make query traversal practical.
   - Add repository/query support that can fetch allocation with payment, target document, and event in one query path using includes/projections.
   - Avoid N+1 patterns.

6. **Persist cash before/after/delta**
   - For every cash-affecting simulation event, persist:
     - before cash value
     - delta amount
     - after cash value
     - company
     - simulation date
   - Add validation/guard logic so these values are internally consistent where possible:
     - `after = before + delta`
   - If there is an existing money type/value object, use it consistently.
   - Respect currency/company conventions already present in the domain.

7. **EF Core configuration and migration**
   - Add `DbSet<SimulationEventRecord>` if appropriate.
   - Configure table name, keys, lengths, required fields, precision for monetary columns, indexes, and relationships.
   - Add indexes for likely access patterns:
     - by `CompanyId`
     - by `SimulationRunId` if present
     - by `SimulationDate`
     - by deterministic event ID
     - by source event FK on simulated entities
   - Create a migration that:
     - creates `SimulationEventRecord`
     - adds FK columns to affected tables
     - adds indexes and constraints
   - If existing data exists, decide whether backfill is required or whether nullability is temporarily needed. Prefer minimal safe migration aligned to current environment.

8. **Add domain/application validation**
   - Enforce deterministic identity generation at creation time, not ad hoc later.
   - Prevent creation of simulated records without source event references where the workflow now requires them.
   - Add guard clauses or factory methods if the codebase uses them.
   - Keep validation consistent with existing architecture boundaries.

9. **Add tests**
   - Unit tests:
     - deterministic event ID generation
     - canonicalization behavior
     - cash before/delta/after consistency validation
   - Integration tests:
     - persisting `SimulationEventRecord`
     - persisting simulated invoice/bill/payment/asset/cash transaction with source event FK
     - payment allocation query path resolves payment + target document + event
     - replaying same seed/start date reproduces same event IDs and object references
   - If there is an existing test database pattern, follow it exactly.

10. **Document assumptions in code comments only where necessary**
   - Keep comments concise.
   - Prefer self-explanatory naming over verbose comments.

Implementation constraints:

- Follow existing project architecture and naming conventions over this prompt if they differ.
- Keep changes cohesive and minimal.
- Do not introduce speculative abstractions unless needed for determinism and persistence.
- If polymorphic target document linkage is awkward, implement the smallest robust pattern already used in the codebase.

# Validation steps

Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and model snapshot is correct.

4. Verify determinism with tests covering:
   - same company + same seed + same start date + same event inputs => same event ID
   - changed seed or changed stable ordinal => different event ID
   - replay produces same source references on generated objects

5. Verify relational linkage with tests:
   - simulated invoice stores source simulation event reference
   - simulated bill stores source simulation event reference
   - simulated payment stores source simulation event reference
   - simulated asset stores source simulation event reference
   - simulated cash-affecting transaction stores source simulation event reference
   - payment allocation query can resolve payment, target document, and originating event in one query path

6. Verify cash persistence with tests:
   - cash-affecting event stores before, delta, after, company, and simulation date
   - before + delta == after

7. If there is a local database migration workflow in the repo, run it per repo convention.

# Risks and follow-ups

- **Unknown existing simulation schema:** The exact entity names or module boundaries may differ. Resolve by inspecting the codebase first and adapting names without changing task intent.
- **Polymorphic payment target documents:** If invoice/bill targeting is modeled loosely, query-path support may require a small normalization step or projection query.
- **Backfill complexity:** Existing simulated records may lack source event references. If backfill is non-trivial, make schema nullable only where necessary and note follow-up work.
- **Deterministic ordering dependency:** Stable event IDs require a stable event sequence. If current simulation generation order is not deterministic, this task may expose a deeper issue; document it and implement the most stable available ordinal source.
- **Money precision/currency handling:** Use existing precision/value object conventions to avoid rounding drift in cash before/after/delta.
- **Potential follow-up tasks:**
  - add explicit simulation run entity if missing
  - add audit/explainability views over simulation causality
  - add backfill migration for legacy simulated data
  - add API/query endpoints for simulation event trace inspection