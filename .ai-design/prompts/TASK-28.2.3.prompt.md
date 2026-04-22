# Goal
Implement backlog task **TASK-28.2.3: seeded asset purchase event generation with cash and payable funding modes** for story **US-28.2 Build deterministic operational finance scenario generation for receivables, payables, recurring costs, and asset purchases**.

The coding agent should extend the seeded finance scenario generator so that deterministic simulation runs produce **asset purchase events** in addition to invoices, bills, and recurring costs, with these required behaviors:

- Asset purchases are generated deterministically from the simulation seed.
- Each generated asset purchase always creates an **asset record**.
- Funding mode is deterministic and configurable:
  - **Cash-funded** purchases immediately reduce cash.
  - **Payable-funded** purchases create a payable liability instead of immediate cash reduction.
- Generated dates, amounts, counterparties, and statuses must be deterministic for a given seed and tenant/company context.
- The implementation must preserve or align with existing deterministic generation patterns for invoices, bills, and recurring costs.

# Scope
In scope:

- Find the existing seeded operational finance/scenario generation flow.
- Add or extend domain/application/infrastructure logic to generate **asset purchase events**.
- Support at least two funding behaviors:
  - `Cash`
  - `Payable`
- Ensure generated asset purchases:
  - create an asset record every time
  - create the correct balancing financial effect based on funding mode
- Keep generation deterministic by seed, company, and simulation configuration.
- Add or update tests covering:
  - deterministic repeatability
  - cash-funded asset purchase behavior
  - payable-funded asset purchase behavior
  - acceptance-criteria-level scenario assertions if there is an existing simulation test suite
- Update any relevant contracts/DTOs/config models if the generator is externally consumed.

Out of scope unless required by existing patterns:

- UI work in Blazor or MAUI
- New public API endpoints unless the generator is already exposed that way
- Broad refactors unrelated to seeded finance generation
- Full accounting engine redesign
- Non-deterministic randomness sources

# Files to touch
Inspect and modify only the files needed after discovery. Likely areas:

- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums for asset purchases, assets, payables, cash movements, statuses, funding modes
- `src/VirtualCompany.Application/**`
  - seeded scenario generation services
  - simulation orchestration
  - command/query handlers or DTOs used by generation
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories if new records/entities are stored
  - seed/config readers if applicable
- `src/VirtualCompany.Api/**`
  - only if request/response contracts expose simulation outputs
- `tests/**`
  - unit tests for deterministic generation
  - integration/application tests for persisted outputs and financial effects

Also inspect:
- `README.md`
- any docs or migration guidance under `docs/postgresql-migrations-archive/README.md`
- solution/project references in:
  - `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
  - `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
  - `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

# Implementation plan
1. **Discover the existing finance simulation architecture**
   - Locate the seeded simulation/scenario generation code for invoices, bills, and recurring costs.
   - Identify:
     - the deterministic randomization mechanism
     - seed propagation
     - simulation configuration models
     - persistence flow for generated finance events
     - current domain model for assets, liabilities, invoices, bills, and cash movements
   - Follow existing conventions rather than inventing a parallel implementation.

2. **Identify the correct extension point**
   - Determine whether asset purchases belong as:
     - a new generated event type in the existing scenario generator, or
     - a sub-generator invoked by the main seeded finance simulation pipeline.
   - Reuse the same deterministic sequencing approach used for invoices/bills/recurring costs so repeated runs with the same seed produce identical outputs.

3. **Model asset purchase funding behavior**
   - Introduce or reuse a funding mode enum/value object, e.g.:
     - `Cash`
     - `Payable`
   - Ensure the generated asset purchase event contains enough information to derive:
     - purchase date
     - amount
     - counterparty/vendor
     - asset category/type if the model supports it
     - status
     - funding mode
   - Keep naming aligned with existing domain terminology.

4. **Implement deterministic asset purchase generation**
   - Extend the seeded generator to produce asset purchase events from the same seed stream.
   - Ensure deterministic selection of:
     - event count/frequency if configurable
     - dates
     - amounts
     - counterparties/vendors
     - statuses
     - funding mode based on configuration or deterministic branching
   - Avoid `DateTime.UtcNow`, unordered collection iteration, or non-seeded `Random`.

5. **Create the required financial effects**
   - For every generated asset purchase:
     - always create/persist an **asset record**
   - For `Cash` funding:
     - create the corresponding immediate cash reduction effect using the project’s existing cash transaction/balance mechanism
   - For `Payable` funding:
     - create the corresponding payable/bill/liability exposure using the project’s existing payable mechanism
   - Do not create both cash reduction and payable unless the current domain explicitly supports split funding and it is already modeled; otherwise keep behavior mutually exclusive.

6. **Preserve acceptance-criteria semantics**
   - Verify the overall simulation still satisfies:
     - invoices create receivable exposure until settled by incoming payments
     - bills create payable exposure until settled by outgoing payments
     - asset purchases always create an asset and either reduce cash or create a payable
   - If there is an aggregate simulation result object, include asset purchase outputs there.

7. **Persist and map new data if needed**
   - If asset purchase events or asset records require persistence changes:
     - add/update EF Core entities/configuration/mappings
     - add migrations only if this repository’s current workflow expects them for this task
   - Keep tenant/company scoping intact on all tenant-owned records.

8. **Add tests**
   - Add focused tests for deterministic generation:
     - same seed/config => identical asset purchase outputs
     - different seed => different outputs where expected
   - Add behavior tests:
     - cash-funded purchase creates asset + cash reduction, no payable
     - payable-funded purchase creates asset + payable, no immediate cash reduction
   - Add scenario-level tests if present:
     - seeded run generates invoices, bills, recurring costs, and asset purchases deterministically
   - Prefer existing test patterns and fixtures.

9. **Keep implementation minimal and cohesive**
   - Avoid speculative abstractions.
   - Reuse existing event/status/counterparty models where possible.
   - Keep code readable and deterministic.

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`
2. Run relevant tests before changes to understand current state:
   - `dotnet test`
3. After implementation, run:
   - `dotnet build`
   - `dotnet test`
4. If there are targeted test projects or filters for finance simulation, run those specifically as well.
5. Manually verify in tests or debug assertions that:
   - same seed produces identical asset purchase outputs
   - asset purchases always create an asset record
   - cash-funded purchases reduce cash immediately
   - payable-funded purchases create payable exposure instead of immediate cash reduction
   - invoices and bills still behave as receivable/payable exposure generators until settlement
6. If persistence was changed, ensure mappings/migrations are consistent and tests pass against the configured provider.

# Risks and follow-ups
- **Risk: unclear existing finance domain model**
  - Mitigation: discover and align with current invoice/bill/recurring cost patterns before coding.
- **Risk: deterministic behavior broken by hidden non-seeded randomness**
  - Mitigation: audit all date/amount/vendor selection paths and remove non-seeded sources.
- **Risk: asset purchase overlaps with bill or recurring cost concepts**
  - Mitigation: keep asset purchase as a distinct generated event while reusing payable/cash effect mechanisms.
- **Risk: persistence changes may require migrations or fixture updates**
  - Mitigation: only add schema changes if truly necessary and update tests accordingly.
- **Risk: acceptance criteria are scenario-level, not just unit-level**
  - Mitigation: include at least one end-to-end seeded simulation test covering invoices, bills, recurring costs, and asset purchases together.

Follow-ups to note in code comments or task summary if not completed:
- richer asset categories/depreciation schedules
- split/partial funding modes
- settlement lifecycle for payable-funded asset purchases
- dashboard/reporting exposure for generated asset purchases