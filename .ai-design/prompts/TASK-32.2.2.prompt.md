# Goal
Implement backlog task **TASK-32.2.2** for story **US-32.2 Database migration and provider foundation for real Fortnox data separation**.

Deliver a safe, additive implementation that:
- adds the new finance integration persistence model via **EF Core migration**,
- adds source/provider separation fields to existing finance entity tables,
- backfills existing rows to preserve current manual/simulated behavior,
- prevents future Fortnox-synced records from being overwritten by simulation regeneration,
- registers a **Fortnox provider** behind the finance integration abstraction and makes it resolvable by provider key `fortnox`.

Do not delete, truncate, or overwrite existing finance business data during migration.

# Scope
In scope:
- Inspect current finance domain, infrastructure, DbContext mappings, existing finance tables, repository table names, and any finance integration abstractions.
- Add new entities/tables:
  - `FinanceIntegrationConnections`
  - `FinanceIntegrationTokens`
  - `FinanceIntegrationSyncStates`
  - `FinanceExternalReferences`
  - `FinanceIntegrationAuditEvents`
- Add source/provider tracking columns to existing finance tables that currently lack them.
- Create SQL Server-compatible EF Core migration with correct:
  - columns,
  - foreign keys,
  - indexes,
  - unique constraint on `CompanyId + ProviderKey` for `FinanceIntegrationConnections`.
- Backfill existing records so current rows are preserved and defaulted to `manual` or `simulation` as appropriate.
- Ensure Fortnox-linked records can be marked as `SourceType=fortnox` and linked through provider/external reference fields.
- Implement/register provider resolution for provider key `fortnox`.
- Add/update tests where practical for model configuration and provider registration.

Out of scope unless required by compilation:
- Full Fortnox sync workflow implementation.
- OAuth UI flows.
- End-to-end token encryption infrastructure beyond using the project’s existing encryption/value protection patterns.
- Broad refactors unrelated to finance integration persistence/provider registration.

# Files to touch
Inspect and update the actual files that exist in the repo. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities
  - enums/value objects for source/provider tracking
  - finance integration abstractions/interfaces
- `src/VirtualCompany.Application/**`
  - provider abstraction contracts if defined here
  - DI registration extensions if application-layer owned
- `src/VirtualCompany.Infrastructure/**`
  - `DbContext`
  - entity type configurations
  - migrations
  - repository implementations
  - provider implementation for Fortnox
  - DI registration/extensions
  - token storage/encryption handling
- `src/VirtualCompany.Api/**`
  - startup/DI composition root if provider registration happens here
- `tests/**`
  - infrastructure tests
  - DI/provider resolution tests
  - migration/model configuration tests

Also inspect:
- existing finance table/entity names and mappings,
- any existing simulation/manual generation services,
- any repository logic that regenerates simulated finance data,
- any existing provider registry/factory pattern.

# Implementation plan
1. **Discover current finance model and conventions**
   - Find all existing finance entities/tables and note exact table names already used by repositories/mappings.
   - Identify which existing finance tables currently lack source/provider tracking fields.
   - Find current abstractions for finance integrations/providers, if any.
   - Find existing enum/string conventions for statuses, provider keys, source types, timestamps, IDs, and tenant/company scoping.
   - Confirm whether the project currently targets SQL Server in EF migrations despite architecture docs mentioning PostgreSQL; follow the acceptance criteria and current codebase reality.

2. **Design additive source separation model**
   - Introduce or extend domain concepts for:
     - `SourceType` with at least `manual`, `simulation`, `fortnox`
     - provider key storage, likely string-based with value `fortnox`
     - external reference linkage fields on finance records as needed by current model
   - Keep the design additive and backward-compatible.
   - Prefer nullable provider/external reference fields for existing rows, with backfill setting only what is safe and known.
   - Ensure Fortnox-synced records can be distinguished from manual/simulated rows in a way simulation regeneration logic can respect.

3. **Add new integration entities**
   Implement entities/configurations for:
   - `FinanceIntegrationConnections`
     - company-scoped connection metadata
     - unique constraint/index on `(CompanyId, ProviderKey)`
   - `FinanceIntegrationTokens`
     - separate token storage from connection metadata
     - encrypted token values only; do not store plaintext
   - `FinanceIntegrationSyncStates`
     - per-connection or per-entity sync cursor/state tracking
   - `FinanceExternalReferences`
     - mapping between internal finance entities/records and provider external IDs
   - `FinanceIntegrationAuditEvents`
     - business audit trail for integration actions/events

   Use the project’s existing conventions for:
   - primary keys,
   - audit timestamps,
   - company foreign keys,
   - delete behavior,
   - max lengths,
   - required vs nullable fields.

4. **Extend existing finance entities/tables**
   - Add source/provider tracking columns to all relevant existing finance tables that currently lack them.
   - Use exact existing repository table names and preserve current mappings.
   - Typical fields may include:
     - `SourceType`
     - `ProviderKey`
     - `ExternalReferenceId` or equivalent linkage
     - provider entity/type keys if needed by current architecture
   - Do not introduce fields that conflict with existing naming conventions; align to current model.

5. **Create EF Core configurations**
   - Add/update entity type configurations for all new and changed entities.
   - Configure:
     - table names,
     - keys,
     - relationships,
     - indexes,
     - unique constraints,
     - column lengths/types,
     - delete behaviors.
   - Explicitly configure the unique index/constraint for `FinanceIntegrationConnections(CompanyId, ProviderKey)`.
   - Ensure SQL Server compatibility in generated migration.

6. **Author migration with safe backfill**
   - Generate or hand-author an EF Core migration that:
     - creates the five new tables,
     - adds new columns to existing finance tables,
     - backfills existing rows to `manual` or `simulation` defaults based on current data semantics.
   - Determine backfill rules from existing schema/business logic:
     - if a table is exclusively simulation-generated today, backfill to `simulation`
     - if a table represents user-entered/manual records, backfill to `manual`
     - if mixed, infer from existing flags/metadata where available; otherwise choose the safest default and document it
   - Ensure migration is non-destructive:
     - no dropping existing finance tables/data,
     - no overwriting existing business values,
     - no lossy transforms.
   - If needed, use raw SQL in migration for deterministic backfill updates.

7. **Protect Fortnox-synced records from simulation overwrite**
   - Inspect current simulation regeneration/update logic.
   - Update it so records marked `SourceType=fortnox` are excluded from overwrite/regeneration paths.
   - If regeneration currently replaces whole sets, add filtering/guard clauses using source/provider/external reference markers.
   - Keep existing behavior unchanged for `manual` and `simulation` records unless required for correctness.

8. **Implement Fortnox provider behind abstraction**
   - Add a provider implementation class for provider key `fortnox`.
   - Wire it into the existing finance integration abstraction/factory/registry.
   - Ensure it can be resolved by provider key `fortnox`.
   - If no registry exists, add a minimal one consistent with current architecture rather than overengineering.
   - Keep implementation foundation-focused; stub unsupported operations if necessary, but make resolution/registration real and testable.

9. **Handle token encryption correctly**
   - Store encrypted token values in `FinanceIntegrationTokens`.
   - Reuse existing encryption/data protection/secret handling patterns already present in the solution.
   - Do not place token secrets on `FinanceIntegrationConnections`.
   - If no encryption abstraction exists, add a minimal infrastructure service interface/implementation aligned with current patterns, but avoid inventing a large security subsystem.

10. **Add tests**
   Add or update focused tests for:
   - model configuration / unique constraint on `(CompanyId, ProviderKey)`,
   - provider resolution by key `fortnox`,
   - migration or schema expectations where test infrastructure allows,
   - simulation protection logic for `SourceType=fortnox` if there are existing tests around regeneration.

11. **Keep changes compile-safe and documented in code**
   - Update any affected constructors, mappings, repositories, and DI registrations.
   - Ensure nullable/reference handling is correct for pre-existing rows.
   - Add concise comments only where intent is not obvious, especially around backfill and overwrite protection.

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify EF model/migration artifacts:
   - confirm migration creates:
     - `FinanceIntegrationConnections`
     - `FinanceIntegrationTokens`
     - `FinanceIntegrationSyncStates`
     - `FinanceExternalReferences`
     - `FinanceIntegrationAuditEvents`
   - confirm existing finance tables receive new source/provider columns
   - confirm SQL Server migration contains:
     - foreign keys,
     - indexes,
     - unique constraint/index on `FinanceIntegrationConnections(CompanyId, ProviderKey)`

4. Validate backfill logic by inspecting migration SQL/code:
   - existing rows are assigned `manual` or `simulation`
   - no delete/update statements overwrite unrelated finance data
   - no destructive schema operations on existing finance records

5. Validate provider registration:
   - resolve provider by key `fortnox` through DI/registry/factory
   - ensure registration occurs in the actual composition root used by the app

6. Validate overwrite protection:
   - inspect and test simulation regeneration paths to confirm `SourceType=fortnox` records are not overwritten

7. Final check:
   - ensure token values are stored separately from connection metadata
   - ensure no plaintext token persistence is introduced
   - ensure all changes follow existing naming and architectural conventions

# Risks and follow-ups
- **Schema ambiguity risk:** The task references “specified columns” but this prompt does not include an explicit column-by-column contract. Derive from existing code/story context first; if missing, implement the minimal complete schema needed by acceptance criteria and keep it extensible.
- **Database provider mismatch risk:** Architecture docs mention PostgreSQL, but acceptance criteria require SQL Server migration. Follow the actual EF provider and migration setup in the repo.
- **Backfill classification risk:** Distinguishing `manual` vs `simulation` may not be obvious for all existing tables. Use current business logic and table purpose to infer safely; document assumptions in code comments or PR notes.
- **Simulation overwrite risk:** Existing regeneration may be coarse-grained. Be careful not to break current simulation flows while excluding Fortnox-sourced rows.
- **Token security risk:** Do not invent insecure placeholder encryption. Reuse existing protection mechanisms if present; if absent, add a minimal abstraction and clearly isolate encrypted payload handling.
- **Follow-up likely needed:** subsequent tasks may need:
  - actual Fortnox OAuth/token refresh flows,
  - sync jobs using `FinanceIntegrationSyncStates`,
  - richer external reference mapping rules,
  - audit event production during real sync operations.