# Goal

Implement backlog task **TASK-32.2.1** by creating the EF Core migration and provider foundation needed for real Fortnox data separation.

The coding agent should:

- add a new **EF Core migration targeting the current SQL Server-backed infrastructure**
- create the new tables:
  - `FinanceIntegrationConnections`
  - `FinanceIntegrationTokens`
  - `FinanceIntegrationSyncStates`
  - `FinanceExternalReferences`
  - `FinanceIntegrationAuditEvents`
- add required indexes, foreign keys, uniqueness constraints, and safe defaults
- extend existing finance tables that currently lack provider/source tracking so Fortnox-synced records are distinguishable from manual/simulated records
- preserve all existing finance data without deleting, overwriting, or reclassifying manual/simulated records incorrectly
- ensure Fortnox-linked records can be protected from simulation regeneration by using source/provider/external reference fields
- register a Fortnox provider implementation behind the existing finance integration abstraction so it can be resolved by provider key `fortnox`

The result must satisfy the acceptance criteria exactly and fit the existing repository structure and naming conventions.

# Scope

In scope:

- inspect the current finance domain/infrastructure model and existing repository table names
- identify all existing finance entities/tables that need new source/provider tracking columns
- update EF Core entity configurations/model mappings as needed
- create a migration that:
  - creates the five new integration tables
  - adds columns to existing finance tables
  - adds indexes and constraints
  - uses non-destructive defaults/backfill behavior
- add or complete the Fortnox provider registration in DI
- ensure provider resolution by key `fortnox`
- add/update tests if the solution already has infrastructure/provider registration tests

Out of scope unless required by compilation:

- implementing full Fortnox API sync behavior
- building UI
- changing unrelated database providers
- refactoring broad finance architecture beyond what is needed for this task
- deleting or transforming existing finance records beyond safe schema evolution

# Files to touch

Inspect first, then modify only the necessary files, likely including some of these:

- `src/VirtualCompany.Infrastructure/**`
  - DbContext
  - entity type configurations
  - migrations folder
  - finance integration persistence classes
  - DI registration/extensions
- `src/VirtualCompany.Application/**`
  - finance integration abstractions/interfaces if provider registration requires it
- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums if source/provider fields are modeled in domain
- `tests/VirtualCompany.Api.Tests/**`
  - DI/provider resolution tests
  - migration/integration tests if present
- possibly `src/VirtualCompany.Api/**`
  - startup/service registration wiring if infrastructure registration is composed there

Before editing, locate:

- the main EF Core `DbContext`
- existing finance entities/tables and their exact mapped table names
- any existing enums/constants for source type/provider key
- any existing finance integration abstraction and provider registry/factory
- current migration style and naming conventions

# Implementation plan

1. **Discover the current finance model and integration abstractions**
   - Search for:
     - `DbContext`
     - `Finance`
     - `SourceType`
     - `ProviderKey`
     - `ExternalReference`
     - `Fortnox`
     - service registration extensions
   - Identify the exact existing finance tables that must gain source/provider tracking fields.
   - Confirm whether the project currently uses SQL Server EF migrations despite the broader architecture notes mentioning PostgreSQL. Follow the actual repository implementation, not the architecture prose.

2. **Define the schema additions carefully**
   - Model the new tables with explicit SQL Server-compatible types and constraints consistent with the existing schema conventions.
   - Ensure these tables support:
     - connection metadata separate from token storage
     - sync state tracking per company/provider/entity scope as appropriate
     - external reference mapping between internal finance records and provider records
     - audit trail for integration actions/events
   - Enforce:
     - unique constraint on `FinanceIntegrationConnections` for `(CompanyId, ProviderKey)`
     - FK relationships and delete behaviors that do not risk destructive cascades on business data
     - indexes for expected lookup paths such as company/provider, connection, entity mapping, and audit chronology

3. **Add source/provider tracking to existing finance tables**
   - For each existing finance table lacking these fields, add the minimum required columns to distinguish:
     - manual records
     - simulated records
     - Fortnox-synced records
   - Use safe defaults for existing rows so current data remains preserved and semantically unchanged.
   - Do **not** mark old rows as `fortnox`.
   - If needed, default existing rows to the current non-external source classification already implied by the system, such as manual/simulated/unknown based on the table’s current behavior.
   - Add nullable provider/external reference linkage fields where appropriate so future Fortnox imports can be tied to external references without breaking existing rows.

4. **Protect synced records from simulation overwrite**
   - Ensure the schema supports the rule that Fortnox-synced records are identifiable via `SourceType=fortnox` plus provider/external reference linkage.
   - If there are existing regeneration filters or repository predicates that depend on source classification, update them only if required for compilation or to make the acceptance criteria true at the persistence layer.
   - Keep changes minimal and focused on enabling the separation contract.

5. **Implement EF Core configuration**
   - Add/update entity classes and fluent configurations for the new tables.
   - Configure:
     - keys
     - max lengths
     - required/optional columns
     - unique indexes
     - foreign keys
     - delete behaviors
     - default values where appropriate
   - Match existing naming conventions for table names, columns, and indexes.

6. **Create the migration**
   - Generate or hand-author a migration with a clear name, for example similar to:
     - `AddFortnoxIntegrationFoundation`
   - The migration must:
     - create the five new tables
     - add new columns to existing finance tables
     - create all required indexes and constraints
     - preserve existing data
   - If backfill SQL is needed, keep it idempotent within the migration and non-destructive.
   - Review the generated migration code manually to ensure no accidental drops, renames, or data-loss operations are included.

7. **Register the Fortnox provider in DI**
   - Find the finance integration abstraction/provider registry/factory.
   - Add a Fortnox provider implementation or registration adapter behind that abstraction.
   - Ensure it can be resolved by provider key exactly `fortnox`.
   - If a keyed/factory pattern already exists, extend it rather than inventing a parallel mechanism.
   - If no concrete provider exists yet, add the thinnest valid implementation/stub needed to satisfy registration and resolution without pretending to implement full sync behavior.

8. **Add or update tests**
   - Prefer focused tests for:
     - provider resolution by key `fortnox`
     - DI registration success
     - any existing model/configuration tests if present
   - If migration tests already exist, extend them to assert the new model can be built/applied.
   - Do not create heavy end-to-end tests unless the repo already follows that pattern.

9. **Self-review against acceptance criteria**
   - Verify each criterion explicitly:
     - all five tables exist in migration
     - indexes/constraints are present
     - existing finance tables gained source/provider tracking
     - unique `(CompanyId, ProviderKey)` on connections
     - tokens are stored separately from connection metadata
     - no destructive migration behavior
     - Fortnox records can be marked and linked to prevent simulation overwrite
     - provider resolves by key `fortnox`

# Validation steps

1. **Restore/build**
   - Run:
     - `dotnet build`

2. **Run tests**
   - Run:
     - `dotnet test`

3. **Inspect the migration**
   - Confirm the migration contains:
     - `CreateTable` for all five required tables
     - `AddColumn` for source/provider tracking on existing finance tables
     - unique index on `FinanceIntegrationConnections (CompanyId, ProviderKey)`
     - indexes for token, sync state, external reference, and audit lookup paths
     - no `DropTable`, destructive `DropColumn`, or unsafe data rewrite affecting existing finance data

4. **Validate model snapshot/configuration**
   - Ensure the EF model snapshot reflects the new schema correctly.
   - Confirm table names match existing repository naming, not guessed names.

5. **Validate DI/provider resolution**
   - Confirm there is a registration path where requesting the finance integration provider by key `fortnox` returns the Fortnox implementation.
   - If there is a factory/registry test pattern, add/assert against it.

6. **Sanity-check data preservation behavior**
   - Review defaults/backfill logic for existing rows.
   - Ensure existing manual/simulated records remain intact and are not reclassified as Fortnox.
   - Ensure token data is stored in `FinanceIntegrationTokens`, not duplicated into connection metadata.

# Risks and follow-ups

- **Architecture mismatch risk:** the architecture notes mention PostgreSQL, but the task explicitly requires SQL Server migration behavior. Use the actual repository EF provider and current migration conventions.
- **Unknown existing finance schema risk:** the exact finance tables needing source/provider tracking are not listed in the task. You must inspect the current model and update only the relevant existing finance tables.
- **Enum/string consistency risk:** if `SourceType` or provider keys are represented as enums/constants/strings in multiple layers, keep them consistent and avoid introducing conflicting values.
- **Cascade delete risk:** avoid FK delete behaviors that could remove business records or token/audit history unintentionally.
- **Sensitive data handling risk:** token values must remain separated from connection metadata and stored as encrypted values or encrypted payload fields per existing security patterns. Do not log token contents.
- **Simulation protection may need follow-up logic:** this task primarily establishes schema and provider foundation. If regeneration logic does not yet honor the new source/provider fields, a follow-up task may be needed to enforce overwrite protection in application workflows.
- **Provider registration may expose missing abstraction gaps:** if the current abstraction is incomplete for keyed resolution, implement the smallest compatible extension and note any broader provider-registry cleanup as follow-up.