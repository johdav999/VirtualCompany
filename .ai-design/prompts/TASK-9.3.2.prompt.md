# Goal

Implement backlog task **TASK-9.3.2** for story **ST-303 Agent and company memory persistence** by adding support for memory item types including:

- `preference`
- `decision_pattern`
- `summary`
- `role_memory`
- `company_memory`

The implementation should fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and align with the Knowledge & Memory module design. The result should make these memory types first-class, validated, persisted, and queryable in a way that supports later retrieval/filtering work for ST-303 and ST-304.

# Scope

In scope:

- Add a domain-level representation for supported memory types.
- Ensure persistence supports the required memory types in the `memory_items` model.
- Add application/infrastructure mapping and validation so only supported types are accepted.
- Update any create/update/query flows that currently treat memory type as an unbounded string.
- Add tests covering valid and invalid memory types.
- If the project already has EF Core configurations, migrations, repositories, DTOs, commands, or API endpoints for memory items, update them consistently.

Out of scope unless already partially implemented and required to complete compilation:

- Full semantic retrieval/ranking implementation.
- UI for memory management.
- Advanced policy controls for deletion/expiration.
- Embedding generation logic beyond preserving compatibility.
- Broad refactors unrelated to memory typing.

Assumptions:

- The solution likely uses layered projects such as Domain, Application, Infrastructure, and Api.
- `memory_items` may already exist or be partially scaffolded from the architecture/backlog.
- If no memory module exists yet, implement the minimum vertical slice needed to represent and persist typed memory safely without overbuilding unrelated features.

# Files to touch

Inspect and update the actual matching files in the repo. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - Memory domain entity/value object/enum files
  - Shared domain validation or constants
- `src/VirtualCompany.Application/**`
  - Commands/queries for memory creation and retrieval
  - DTOs/contracts/view models
  - Validators
  - Application service/handler logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext mappings
  - Repository implementations
  - Migrations for `memory_items`
- `src/VirtualCompany.Api/**`
  - Request/response contracts
  - Controllers/endpoints if memory APIs already exist
- `tests/**` or matching test projects if present
  - Domain tests
  - Application validation tests
  - Persistence mapping tests

Also inspect:

- `README.md` for conventions
- Existing solution/project structure in:
  - `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
  - `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
  - `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
  - `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

# Implementation plan

1. **Discover current memory implementation**
   - Search the solution for:
     - `memory_items`
     - `MemoryItem`
     - `memory type`
     - `role_memory`
     - `company_memory`
   - Determine whether memory is already modeled as:
     - raw string
     - enum
     - constants
     - JSON field
   - Identify all entry points where memory type is created, updated, deserialized, or queried.

2. **Introduce a canonical memory type model**
   - Prefer a domain enum or strongly typed value object named something like `MemoryType`.
   - Supported values must map exactly to the backlog/architecture vocabulary:
     - `Preference`
     - `DecisionPattern`
     - `Summary`
     - `RoleMemory`
     - `CompanyMemory`
   - If external/API/database contracts use snake_case strings, keep a clear conversion layer so persisted/API values remain:
     - `preference`
     - `decision_pattern`
     - `summary`
     - `role_memory`
     - `company_memory`

3. **Enforce validation at boundaries**
   - Update application validators and API request validation so unsupported memory types are rejected.
   - Avoid accepting arbitrary strings silently.
   - Return clear validation errors for invalid values.
   - Preserve backward compatibility only if existing data/contracts require it; if so, normalize legacy aliases explicitly rather than allowing free-form values.

4. **Update domain entity and invariants**
   - Ensure the memory entity stores a strongly validated type.
   - If the entity currently exposes mutable string properties, tighten this to reduce invalid state.
   - Preserve existing fields relevant to ST-303, such as:
     - `company_id`
     - `agent_id`
     - `summary`
     - `salience`
     - `valid_from`
     - `valid_to`
     - `embedding`
     - `metadata_json`

5. **Update persistence mapping**
   - In EF Core configuration, map the domain type cleanly to the database column.
   - If the DB stores text, use a value converter between enum/value object and canonical string values.
   - Ensure the `memory_type` column is required and constrained in configuration.
   - If migrations are used and needed:
     - add/update migration to enforce valid storage shape
     - optionally add a check constraint for allowed values if consistent with project conventions

6. **Handle existing data safely**
   - If a migration is added and the table already exists, include a safe data normalization step if needed.
   - Example: convert legacy values like `decisionPattern` or `role-memory` into canonical snake_case values before adding stricter constraints.
   - Do not delete tenant data.

7. **Update application contracts and handlers**
   - For create/update commands, replace raw string handling with the canonical type.
   - For query/read models, expose stable API-friendly values.
   - Ensure retrieval/filtering paths can filter by memory type without stringly-typed duplication.

8. **Add tests**
   - Domain tests:
     - valid memory types are accepted
     - invalid values are rejected
   - Application/API validation tests:
     - requests with supported types succeed
     - unsupported types fail with expected validation errors
   - Persistence tests:
     - values round-trip correctly through EF Core mapping
     - canonical DB values are stored/read correctly
   - If query logic exists, test filtering by each supported type.

9. **Keep implementation minimal and aligned**
   - Do not build full memory CRUD UI unless already present.
   - Do not introduce unrelated abstractions.
   - Follow existing naming, folder structure, and architectural patterns in the repo.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the repo workflow:
   - Generate/apply migration as appropriate for the existing setup.
   - Verify the `memory_items.memory_type` column stores canonical values.

4. Manually verify code paths:
   - Creating a memory item with each supported type succeeds:
     - `preference`
     - `decision_pattern`
     - `summary`
     - `role_memory`
     - `company_memory`
   - Creating a memory item with an unsupported type fails validation.
   - Reading persisted memory items returns the expected type representation.
   - Existing tenant scoping remains intact for any touched queries.

5. If API endpoints exist, test representative requests/responses for memory create/read flows.

# Risks and follow-ups

- **Risk: existing code uses raw strings broadly**
  - Tightening to a strong type may require touching more layers than expected.
  - Mitigation: add conversion helpers and update incrementally at boundaries.

- **Risk: existing persisted data contains inconsistent values**
  - Adding strict constraints may break migration/application startup.
  - Mitigation: normalize legacy values in migration before enforcing constraints.

- **Risk: enum serialization mismatches API/database expectations**
  - Default .NET enum serialization may produce unexpected casing/names.
  - Mitigation: use explicit converters/mappers and test round-trips.

- **Risk: partial ST-303 implementation may not yet exist**
  - You may need to create a minimal memory entity/configuration path to make this task meaningful.
  - Mitigation: implement the smallest vertical slice that compiles and supports typed persistence.

Follow-ups after this task:

- Add retrieval filtering by recency, salience, semantic relevance, and agent/company scope if not already implemented.
- Add delete/expire operations with policy controls.
- Add auditability around memory creation/update/deletion.
- Add source references and retrieval integration for ST-304 grounded context retrieval.