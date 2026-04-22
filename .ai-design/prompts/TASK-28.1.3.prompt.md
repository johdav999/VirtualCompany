# Goal
Implement backlog task **TASK-28.1.3 — Persist cash delta records with before and after values during event execution** for story **US-28.1 Implement simulation event causality and object linkage across invoices, bills, payments, assets, and cash**.

The implementation must ensure:

- Every simulated invoice, bill, payment, asset, and cash-affecting transaction persists a **source simulation event reference**.
- Any payment allocation can resolve the **linked payment**, **target document**, and **originating simulation event** through a single query path.
- Cash-affecting events persist **before cash**, **after cash**, and **delta amount** for the affected company and simulation date.
- Replaying the same company seed and start date produces the same **event identifiers**, **object references**, and **causal links**.

Use the existing .NET modular monolith structure and PostgreSQL persistence patterns already present in the solution. Keep the implementation deterministic, tenant-safe, and aligned with CQRS-lite and clean architecture boundaries.

# Scope
In scope:

- Domain model changes for simulation event linkage and cash delta persistence.
- Persistence/model configuration updates in Infrastructure.
- Database migration(s) for new columns/tables/indexes/constraints.
- Event execution pipeline updates so cash-affecting events write before/after/delta records at execution time.
- Deterministic identifier/reference generation for replay stability.
- Query-path support for payment allocation linkage traversal.
- Automated tests covering deterministic replay, persistence, and linkage behavior.

Out of scope unless required by compilation/tests:

- UI changes.
- Broad refactors unrelated to simulation persistence.
- New external integrations.
- Non-simulation accounting redesign beyond what is needed for this task.

# Files to touch
Inspect the workspace first, then update the actual files that own simulation, finance, and persistence concerns. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - Simulation event entities/value objects
  - Invoice/bill/payment/asset domain entities
  - Payment allocation entities
  - Cash transaction or ledger-related entities
- `src/VirtualCompany.Application/**`
  - Simulation execution services/handlers
  - Commands and orchestration logic for event replay/execution
  - Query handlers for payment allocation traversal if present
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext mappings
  - Repository implementations
  - Deterministic ID/reference persistence support
  - Migrations
- `src/VirtualCompany.Api/**`
  - Only if API contracts or query endpoints must expose the new linkage fields
- `tests/**`
  - Unit tests for deterministic ID/link generation
  - Integration tests for persistence and query path resolution

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repository already has a simulation module or naming conventions for generated entities, follow those exactly rather than inventing parallel patterns.

# Implementation plan
1. **Discover the current simulation model**
   - Locate the existing simulation event aggregate/entity and all simulated finance entities: invoices, bills, payments, assets, payment allocations, and cash-affecting transactions.
   - Identify how event execution currently creates downstream objects and whether deterministic IDs already exist.
   - Identify the current query path for payment allocations and where to extend it.

2. **Add source simulation event references**
   - For each simulated object type required by acceptance criteria, add a persisted `SourceSimulationEventId` or equivalent strongly typed reference:
     - simulated invoice
     - simulated bill
     - simulated payment
     - simulated asset
     - any cash-affecting transaction/ledger entry
   - Ensure the field is required where the object is simulation-generated.
   - If entities can be both simulated and non-simulated, make the field nullable only when necessary and document/enforce the distinction in code.

3. **Model cash delta records explicitly**
   - Add a dedicated persistence model for cash deltas if one does not already exist, e.g. `SimulationCashDeltaRecord`, with at minimum:
     - `Id`
     - `CompanyId`
     - `SimulationEventId`
     - `SimulationDate`
     - `AffectedEntityType` or transaction type if useful
     - `AffectedEntityId` if applicable
     - `CashBefore`
     - `CashDelta`
     - `CashAfter`
     - timestamps if consistent with project conventions
   - Prefer a dedicated table over embedding these values in opaque JSON if the project uses relational reporting/querying patterns.
   - Add indexes for:
     - `(CompanyId, SimulationDate)`
     - `SimulationEventId`
     - any common traversal path used by replay/debugging

4. **Ensure payment allocation linkage is queryable in one path**
   - Update payment allocation persistence so it can resolve:
     - allocation -> payment
     - allocation -> target document (invoice/bill/etc.)
     - payment -> originating simulation event
     - target document -> originating simulation event
   - If needed, add explicit foreign keys and navigation properties rather than relying on indirect JSON payloads.
   - Keep the query path relational and efficient; avoid requiring multiple disconnected lookups.

5. **Implement deterministic identifiers and references**
   - Review how simulation event IDs are generated today.
   - If not already deterministic, implement deterministic generation based on stable inputs such as:
     - company ID
     - seed
     - simulation start date
     - event sequence/order
     - event type / causal key
   - Apply the same deterministic strategy to downstream simulated object identifiers/references where required for replay stability.
   - Do not use random GUID generation for simulation-created entities that must replay identically.
   - Encapsulate deterministic ID generation in a reusable service/helper in Domain or Application, not ad hoc string concatenation across handlers.

6. **Update event execution flow**
   - In the simulation event executor/handler:
     - resolve current company cash before applying the event
     - compute the event cash delta
     - apply the event mutation
     - persist the resulting cash after value
     - create the cash delta record in the same unit of work/transaction
   - Ensure all downstream objects created during execution receive the correct `SourceSimulationEventId`.
   - Preserve ordering so replay produces identical causal links.

7. **Add EF Core configuration and migration**
   - Update entity configurations with:
     - column types for money/decimal values consistent with existing finance tables
     - foreign keys
     - required/optional constraints
     - indexes
   - Create migration(s) for:
     - new source event reference columns
     - new cash delta table or added columns
     - foreign keys and indexes
   - Keep migration names descriptive and aligned with repository conventions.

8. **Backfill or compatibility handling**
   - If existing simulation data may already exist, decide whether to:
     - backfill deterministically where possible, or
     - leave nullable for legacy rows and enforce non-null for new rows in application logic
   - Prefer safe forward-compatible migration behavior unless the codebase clearly expects destructive resets in development only.

9. **Add tests**
   - Unit tests:
     - deterministic event ID generation
     - deterministic simulated object ID/reference generation
     - cash delta calculation from before/delta/after
   - Integration tests:
     - executing a cash-affecting event persists a cash delta record with correct before/after/delta
     - simulated invoice/bill/payment/asset rows persist source simulation event references
     - payment allocation query path resolves payment, target document, and originating event
     - replaying same company seed and start date yields identical IDs and links
   - Prefer database-backed tests if the project already uses EF integration tests.

10. **Keep architecture boundaries clean**
   - Domain: invariants and core concepts.
   - Application: orchestration/execution flow.
   - Infrastructure: EF mappings, migrations, repositories.
   - Avoid leaking EF-specific concerns into Domain entities unless already established in the codebase.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and is included in the Infrastructure startup path if applicable.

4. Manually validate via tests or a focused repro that:
   - a simulated cash-affecting event writes one cash delta record
   - `CashBefore + CashDelta == CashAfter`
   - created invoice/bill/payment/asset rows contain `SourceSimulationEventId`
   - payment allocation traversal can reach payment, target document, and event without custom reconstruction
   - replaying the same seed/start date twice produces identical event/object IDs and references

5. If there is an existing seed/replay test harness, extend it and use it as the primary determinism proof.

# Risks and follow-ups
- **Risk: existing entities may mix simulated and non-simulated records**
  - Be careful with nullability and constraints to avoid breaking non-simulation flows.

- **Risk: deterministic IDs may conflict with current random GUID assumptions**
  - Audit any code that assumes new IDs are always random or generated by the database.

- **Risk: money precision inconsistencies**
  - Use the project’s established decimal precision/scale for cash values and avoid floating-point types.

- **Risk: query-path ambiguity for target documents**
  - If allocations can target multiple document types, model this explicitly and keep traversal consistent.

- **Risk: replay determinism can be broken by hidden nondeterminism**
  - Watch for `DateTime.UtcNow`, unordered LINQ, random number generation, DB-generated IDs, and unstable iteration order.

Follow-ups to note in code comments or task notes if not fully addressed here:

- Add explicit audit/explainability views over simulation causal chains.
- Consider a unified simulation object linkage abstraction if more simulated entity types are added later.
- Consider performance tuning/index review once larger replay volumes are available.