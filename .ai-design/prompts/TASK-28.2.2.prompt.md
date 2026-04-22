# Goal
Implement backlog task **TASK-28.2.2 — Seeded recurring cost and supplier bill generation rules by company profile** for story **US-28.2 Build deterministic operational finance scenario generation for receivables, payables, recurring costs, and asset purchases**.

The coding agent should add deterministic, seeded finance scenario generation so that a simulation run can create:
- recurring costs
- supplier bills
- invoices
- asset purchases
- related settlement behavior

This task is specifically focused on **recurring cost and supplier bill generation rules by company profile**, while preserving the broader acceptance criteria that generated finance events produce correct receivable/payable exposure and deterministic outputs.

Success means:
- the same company profile + seed + simulation window always produces the same recurring costs and supplier bills
- generated bills have deterministic dates, amounts, counterparties, and statuses
- generated bills create payable exposure until settled by outgoing payments
- generation behavior varies by company profile in a predictable, configurable way
- implementation fits the existing modular monolith and tenant-scoped .NET architecture

# Scope
In scope:
- Inspect the existing finance/simulation domain and identify where seeded scenario generation currently lives
- Add or extend domain/application models for:
  - company-profile-based recurring cost rules
  - company-profile-based supplier bill rules
  - deterministic seeded generation inputs/outputs
- Implement deterministic generation logic for recurring costs and supplier bills
- Ensure generated bills affect payable exposure until settlement
- Ensure deterministic status assignment and payment timing behavior based on seed/config
- Add/extend persistence mappings and migrations if new tables/columns are required
- Add automated tests covering determinism and payable exposure behavior
- Keep all logic tenant-aware and company-scoped

Out of scope unless required by existing design:
- major UI work
- mobile changes
- broad refactors unrelated to finance simulation
- redesigning invoice or asset purchase generation beyond what is necessary to integrate with this task
- introducing external services or non-.NET dependencies

If related invoice/asset purchase code already exists, integrate carefully so acceptance criteria remain true end-to-end, but keep the implementation centered on recurring costs and supplier bills.

# Files to touch
Start by locating the existing finance simulation implementation and then update the relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance domain entities/value objects
  - simulation/scenario generation models
  - company profile models/config
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for seeded scenario generation
  - DTOs/contracts for generated finance events
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories
  - migrations or seed/config persistence
- `src/VirtualCompany.Api/**`
  - only if an endpoint contract must change
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if scenario generation is exercised through API
- potentially additional test projects if domain/application tests already exist elsewhere in solution

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, identify the exact existing files for:
- simulation run orchestration
- seeded random/deterministic utilities
- finance event generation
- payable/receivable exposure calculation
- company profile configuration

Prefer extending existing patterns over creating parallel abstractions.

# Implementation plan
1. **Discover the current finance simulation architecture**
   - Find the story/task area for operational finance scenario generation.
   - Identify:
     - simulation run aggregate/service
     - seed handling and deterministic randomization utilities
     - current invoice/bill/asset purchase generation code
     - payable/receivable exposure models
     - company profile source data
   - Document in code comments only where necessary; do not add speculative abstractions.

2. **Define deterministic generation contracts**
   - Ensure there is a clear input model containing at least:
     - `companyId`
     - simulation period/window
     - seed
     - company profile/business type/industry inputs
   - Ensure outputs for recurring costs and supplier bills include deterministic:
     - event date
     - amount
     - counterparty/supplier
     - status
     - settlement date or unpaid state
   - If a deterministic helper does not exist, add a small reusable utility that derives stable pseudo-random values from seed + company/profile + event key.

3. **Model company-profile-based generation rules**
   - Add or extend configuration/rule objects that map company profile characteristics to recurring cost and supplier bill behavior.
   - Examples of rule dimensions:
     - business type / industry
     - company size/stage if already modeled
     - recurring cost categories
     - supplier mix
     - billing cadence
     - amount ranges or formulas
     - payment terms
     - settlement likelihood/timing
   - Keep rules deterministic and data-driven where possible.
   - Avoid hardcoding scattered `switch` logic across services; centralize rule resolution in one domain/application service.

4. **Implement recurring cost generation**
   - Generate recurring cost events for the simulation window using profile rules.
   - Support deterministic cadence generation, such as:
     - monthly
     - quarterly
     - weekly if relevant to existing domain
   - Ensure each generated recurring cost has a stable identity/key so reruns with the same seed produce the same result.
   - If recurring costs materialize as bills in the domain, align with existing model rather than duplicating concepts.

5. **Implement supplier bill generation**
   - Generate supplier bills using company-profile-specific supplier patterns.
   - For each bill, deterministically derive:
     - supplier/counterparty
     - issue date
     - due date
     - amount
     - status
   - Status behavior must support payable exposure:
     - unpaid/open bills create payable exposure
     - paid bills should be linked to outgoing payment settlement if that concept exists
   - If partial payment is already supported, preserve consistency; otherwise keep to full unpaid/paid behavior unless existing domain requires more.

6. **Wire payable exposure behavior**
   - Ensure generated bills feed the same payable exposure pipeline as other bills.
   - Confirm:
     - bill creation increases payable exposure
     - outgoing payment settlement reduces/clears payable exposure
   - If exposure is computed rather than stored, update the computation inputs.
   - If exposure is persisted, ensure generated events create the correct records.

7. **Preserve broader acceptance criteria compatibility**
   - Verify the seeded run still produces deterministic invoices, bills, recurring costs, and asset purchases overall.
   - Do not break:
     - receivable exposure from invoices
     - asset purchase behavior creating asset records and cash/payable effects
   - If needed, add small integration points so recurring cost and supplier bill generation participates in the same seeded run orchestration.

8. **Persistence and migration updates**
   - If new rule/config tables or columns are needed:
     - add EF Core entity/configuration
     - create migration using project conventions
   - Prefer JSON/config storage only if that matches existing architecture.
   - Keep schema tenant-aware with `company_id` where applicable.

9. **Add automated tests**
   - Add deterministic tests proving same seed + same company profile => identical outputs.
   - Add variation tests proving different seeds or materially different profiles => different but valid outputs.
   - Add payable exposure tests proving:
     - open generated bills create payable exposure
     - settled generated bills reduce/clear payable exposure
   - Add end-to-end or application-level tests for a seeded simulation run if such tests already exist.

10. **Keep implementation clean and minimal**
   - Reuse existing domain language and naming.
   - Avoid introducing speculative frameworks.
   - Ensure null/validation handling for missing company profile data with sensible defaults or explicit failures based on existing conventions.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they are included correctly and application still builds.

4. Add or run targeted tests covering:
   - deterministic recurring cost generation for same seed/profile/window
   - deterministic supplier bill generation for same seed/profile/window
   - different profile behavior producing different supplier/cost patterns
   - payable exposure exists for open generated bills
   - payable exposure is cleared/reduced after outgoing payment settlement
   - no regression in seeded simulation orchestration

5. Manually inspect generated outputs in tests/logs/debug assertions for:
   - stable dates
   - stable amounts
   - stable counterparties
   - stable statuses
   - correct due/settlement behavior

6. Confirm code follows tenant-scoped patterns and does not bypass application/domain boundaries.

# Risks and follow-ups
- **Risk: existing finance simulation structure may differ from assumptions.**
  - Follow the actual codebase patterns first; adapt this plan to the discovered architecture.

- **Risk: recurring costs may already be represented as bills or journal-like events.**
  - Reuse the existing canonical model instead of introducing duplicate entities.

- **Risk: payable exposure may be derived in multiple places.**
  - Update the authoritative calculation path and add tests to prevent divergence.

- **Risk: company profile data may be incomplete or loosely structured.**
  - Add deterministic defaults and document follow-up work for richer profile-driven rules.

- **Risk: deterministic generation can break if using unstable ordering.**
  - Always sort inputs and derive event keys from stable identifiers before applying seeded selection.

Follow-up suggestions after completion:
- externalize profile rule catalogs into seed/config data for easier tuning
- add richer payment-term and settlement behavior distributions
- add audit metadata showing which profile rule produced each generated event
- extend deterministic scenario generation coverage for invoices and asset purchases if gaps remain