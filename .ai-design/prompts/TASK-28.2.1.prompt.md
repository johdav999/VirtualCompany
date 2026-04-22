# Goal
Implement backlog task **TASK-28.2.1 — seeded scenario generator for customer invoice issuance and collection behavior** in the existing .NET modular monolith so that deterministic finance simulation runs generate:
- customer invoices and their collection behavior
- supplier bills and their settlement behavior
- recurring cost events
- asset purchase events

The implementation must satisfy these acceptance criteria:
- A seeded simulation run generates invoices, bills, recurring costs, and asset purchases with deterministic dates, amounts, counterparties, and statuses.
- Generated invoices create receivable exposure until settled by incoming payments.
- Generated bills create payable exposure until settled by outgoing payments.
- Asset purchase events always create an asset record and either reduce cash immediately or create a payable based on configured funding behavior.

Work within the current architecture and stack:
- .NET solution with Domain / Application / Infrastructure / API layering
- PostgreSQL-backed persistence
- deterministic, testable application logic
- tenant-aware design where applicable

Return production-ready code, tests, and any required persistence/config wiring.

# Scope
Implement only what is necessary for this task, favoring minimal, clean extensions over speculative platform work.

Include:
- A **seeded finance scenario generator** service/component that accepts:
  - seed
  - simulation date range or anchor period
  - company/workspace context if required by existing patterns
  - configuration for invoice, bill, recurring cost, and asset purchase generation behavior
- Deterministic generation logic for:
  - invoice issuance events
  - invoice settlement / incoming payment events
  - bill issuance events
  - bill settlement / outgoing payment events
  - recurring cost events
  - asset purchase events
- Domain/application modeling so that:
  - invoices create receivables until settled
  - bills create payables until settled
  - asset purchases always create an asset record
  - asset purchases either:
    - reduce cash immediately, or
    - create a payable, based on funding behavior
- Tests proving deterministic outputs for a fixed seed
- Tests proving exposure behavior before and after settlement
- Any required persistence mappings, DTOs, or internal contracts

Do not include unless clearly required by existing code patterns:
- UI work
- mobile work
- broad workflow/orchestration changes
- unrelated accounting engine redesign
- speculative forecasting/analytics features

If the repo already contains adjacent finance simulation primitives, extend and reuse them rather than duplicating concepts.

# Files to touch
Inspect the solution first and then update only the relevant files. Expected areas to touch are likely:

- `src/VirtualCompany.Domain/**`
  - finance simulation domain models/value objects/entities
  - status enums for invoices, bills, payments, assets if missing
  - deterministic generation abstractions if domain-owned
- `src/VirtualCompany.Application/**`
  - scenario generator service
  - commands/queries/handlers if this is invoked through application layer
  - contracts/interfaces for simulation runs
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories
  - EF Core configurations
  - seed/run storage if applicable
- `src/VirtualCompany.Api/**`
  - only if an endpoint already exists or must be minimally exposed for invocation
- `tests/VirtualCompany.Api.Tests/**` and/or other existing test projects
  - deterministic generation tests
  - exposure lifecycle tests
  - asset funding behavior tests

Also inspect:
- `README.md`
- any finance-related modules under `src/**`
- migration guidance under `docs/postgresql-migrations-archive/README.md`

If schema changes are required, follow the repository’s established migration approach rather than inventing a new one.

# Implementation plan
1. **Discover existing finance model and extension points**
   - Search for existing concepts such as:
     - invoice, receivable, bill, payable, payment, recurring cost, asset, purchase, scenario, simulation, seed
   - Identify whether the repo already has:
     - finance entities
     - event models
     - ledger-like records
     - scenario generation services
     - deterministic random abstraction
   - Reuse existing naming and module boundaries.

2. **Define deterministic generation contract**
   - Introduce or extend a scenario generator contract with explicit seeded inputs.
   - Ensure the generator is deterministic by:
     - using a seeded PRNG instance scoped to the run
     - avoiding `DateTime.UtcNow`, unordered dictionary iteration, or non-stable GUID generation in generated outputs unless derived deterministically
     - sorting counterparties/config inputs before generation if needed
   - Prefer a shape like:
     - simulation request/config
     - generated scenario result containing invoices, bills, recurring costs, asset purchases, and settlement/payment events

3. **Model finance events and statuses clearly**
   - Ensure generated records can represent:
     - issued/open invoice
     - partially/fully settled invoice if partials exist in current model
     - issued/open bill
     - settled bill
     - recurring cost occurrence
     - asset purchase occurrence
     - incoming payment linked to invoice
     - outgoing payment linked to bill or asset payable
   - Keep statuses explicit and deterministic.
   - If the current domain already has richer accounting objects, map generation into those instead of creating parallel models.

4. **Implement seeded invoice generation**
   - Generate customer invoices with deterministic:
     - issue dates
     - due dates if modeled
     - amounts
     - counterparties/customers
     - statuses
   - For unpaid/open invoices:
     - ensure receivable exposure exists
   - For settled invoices:
     - generate linked incoming payment event(s) with deterministic settlement date/amount
   - Ensure no invoice is marked settled without corresponding settlement/payment representation.

5. **Implement seeded bill generation**
   - Generate supplier bills with deterministic:
     - issue dates
     - due dates if modeled
     - amounts
     - counterparties/vendors
     - statuses
   - For unpaid/open bills:
     - ensure payable exposure exists
   - For settled bills:
     - generate linked outgoing payment event(s)
   - Ensure no bill is marked settled without corresponding payment representation.

6. **Implement recurring cost generation**
   - Generate recurring cost events deterministically across the requested period.
   - Use stable recurrence logic based on config:
     - cadence/frequency
     - amount rules
     - counterparty/vendor
     - posting date rules
   - If recurring costs are represented as bills/expenses in the current model, generate through that path rather than duplicating semantics.

7. **Implement asset purchase generation**
   - Every asset purchase must always create:
     - an asset record/event
   - Then apply configured funding behavior:
     - **cash-funded**: immediate cash reduction / outgoing payment
     - **payable-funded**: create payable and optionally later outgoing settlement if config/model supports it
   - Ensure deterministic:
     - purchase date
     - amount
     - asset type/category/name
     - funding behavior
     - counterparty
     - resulting status/exposure

8. **Represent receivable/payable exposure correctly**
   - Make exposure derivable from generated records or explicitly materialized, depending on current architecture.
   - Required invariants:
     - invoice issued and not settled => receivable exposure exists
     - invoice settled => receivable exposure reduced/cleared by incoming payment
     - bill issued and not settled => payable exposure exists
     - bill settled => payable exposure reduced/cleared by outgoing payment
     - asset purchase payable-funded => payable exposure exists until settled
     - asset purchase cash-funded => no payable exposure from funding leg
   - Prefer asserting these invariants in domain/application tests.

9. **Wire persistence and DI**
   - Register the generator in the appropriate DI container.
   - Add persistence mappings only if generated scenarios/runs are stored.
   - If the task only requires in-memory generation for now, avoid unnecessary schema changes.
   - If persistence is required by existing patterns, store enough metadata to reproduce/audit:
     - seed
     - config snapshot
     - generated entities/events
     - run timestamp/context

10. **Add focused automated tests**
   - Add deterministic tests:
     - same seed + same config => identical outputs
     - different seed => different outputs where expected
   - Add lifecycle/exposure tests:
     - open invoice creates receivable exposure
     - settled invoice clears/reduces receivable exposure via incoming payment
     - open bill creates payable exposure
     - settled bill clears/reduces payable exposure via outgoing payment
     - asset purchase always creates asset record
     - cash-funded asset purchase reduces cash immediately
     - payable-funded asset purchase creates payable
   - Add edge-case tests for:
     - zero/empty config inputs if supported
     - date boundary handling
     - deterministic ordering of generated records

11. **Keep implementation clean and explainable**
   - Use small pure functions where possible for deterministic generation.
   - Keep randomization centralized behind a seeded abstraction/helper.
   - Document any assumptions in code comments only where necessary.
   - Do not expose raw internal randomness details through API contracts unless useful.

# Validation steps
Run these after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Targeted verification**
   - Add/execute tests that prove:
     - fixed seed produces stable invoices, bills, recurring costs, and asset purchases
     - generated dates, amounts, counterparties, and statuses are deterministic
     - receivable exposure exists for open invoices and is cleared/reduced on settlement
     - payable exposure exists for open bills and is cleared/reduced on settlement
     - asset purchase always creates an asset record
     - asset purchase funding behavior correctly chooses between immediate cash reduction and payable creation

4. **Code review checklist**
   - No non-deterministic time usage in generation path
   - No hidden randomness outside seeded generator
   - No settled invoice/bill without corresponding payment linkage
   - No asset purchase without asset creation
   - Tenant/application boundaries remain consistent with existing architecture
   - New code follows existing naming, dependency direction, and project conventions

# Risks and follow-ups
- **Risk: existing finance model may be incomplete or inconsistent**
  - If invoice/bill/payment/asset concepts are only partially present, implement the thinnest coherent extension needed for this task and avoid a full accounting redesign.

- **Risk: determinism can be broken by incidental behavior**
  - Watch for unordered LINQ over sets/dictionaries, ambient clock usage, random GUIDs, and locale/timezone-sensitive date logic.

- **Risk: exposure semantics may be duplicated**
  - Prefer one canonical way to derive receivable/payable exposure from generated records rather than parallel flags plus computed logic.

- **Risk: persistence scope may be unclear**
  - If the repo does not yet persist simulation runs, keep the generator application-facing and testable in-memory unless storage is explicitly required by existing code.

Follow-ups to note in comments or task summary if not implemented now:
- support partial settlements if/when finance model requires it
- support richer recurring schedules and asset depreciation later
- expose scenario generation through API/workflow entry points if not already present
- add audit/run history persistence if simulation runs become user-visible