# Goal
Implement backlog task **TASK-29.2.2 — Refactor existing cash and transaction anomaly checks to the shared framework** for story **US-29.2 Unified financial check framework and insight persistence**.

The coding agent should:
- Refactor existing **cash risk** and **transaction anomaly** checks into a **shared financial check framework**
- Ensure **receivables** and **payables** checks also conform to the same contract if they already exist, or add/adapt them as needed to satisfy the acceptance criteria
- Introduce or complete a **normalized result model**
- Persist each check result as a **FinanceAgentInsight** record
- Resolve previously active insights when the condition is no longer true
- Expose a **single API response shape** for dashboard and entity pages without check-specific branching

Work within the existing **.NET modular monolith** architecture, preserving tenant scoping and clean application/domain/infrastructure boundaries.

# Scope
In scope:
- Domain contract for shared financial checks
- Normalized result model for all financial checks
- Refactor of existing check implementations to use the shared contract
- Persistence/update logic for `FinanceAgentInsight`
- Resolution behavior for no-longer-active conditions
- Unified query/DTO/API response shape for insight consumers
- Tests covering normalization, persistence, and resolution behavior

Out of scope unless required by compilation/tests:
- Broad UI redesign
- New unrelated financial analytics
- Reworking unrelated orchestration or agent systems
- Mobile-specific changes
- Large architectural rewrites beyond what is needed for this task

Assumptions to verify in code before implementing:
- Existing financial checks likely already exist in Application/Domain/Infrastructure layers under finance, analytics, dashboard, or agent modules
- `FinanceAgentInsight` may already exist partially; if not, create it in the appropriate domain/persistence layer
- There may already be dashboard/entity endpoints that return check-specific models and need consolidation

# Files to touch
Inspect first, then update the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - financial check contract interface(s)
  - normalized result model
  - `FinanceAgentInsight` entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - check orchestration service / handlers / commands
  - persistence coordination logic
  - unified insight query DTOs
  - API-facing response models
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository implementations
  - migrations or persistence mappings
  - existing check implementations if infrastructure-backed
- `src/VirtualCompany.Api/**`
  - controllers/endpoints for dashboard/entity insight responses
- `src/VirtualCompany.Shared/**`
  - shared contracts/DTOs if this solution uses Shared for API models
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- Potentially additional test projects if present for Application/Domain/Infrastructure

Also inspect:
- existing migrations strategy and whether a new migration is expected
- any finance-related folders/classes matching:
  - `CashRisk*`
  - `TransactionAnomaly*`
  - `Receivable*`
  - `Payable*`
  - `Insight*`
  - `Finance*Check*`

# Implementation plan
1. **Discover existing implementation**
   - Search the solution for:
     - cash risk checks
     - transaction anomaly checks
     - receivables/payables checks
     - finance insight persistence
     - dashboard/entity insight endpoints
   - Identify current contracts, result shapes, and persistence behavior
   - Map where tenant scoping and entity references are currently handled

2. **Define the shared financial check contract**
   - Introduce a single contract for all financial checks, for example:
     - check identity/code
     - supported entity scope/type
     - execution method returning normalized results
   - Keep naming aligned with existing conventions in the repo
   - The contract should support all four check types:
     - cash risk
     - transaction anomaly
     - receivables
     - payables

3. **Create/standardize the normalized result model**
   - Add a result model that all checks return, including at minimum:
     - check code/type
     - severity
     - message
     - recommendation
     - entity reference
     - status/active state
     - stable deduplication key or condition key if needed for update-vs-resolve behavior
     - timestamps if appropriate at result/application layer
   - Ensure the model is generic enough for dashboard and entity pages to consume without branching on check type

4. **Refactor existing checks to the shared framework**
   - Update cash risk and transaction anomaly checks to implement the shared contract
   - Update receivables and payables checks to implement the same contract
   - Remove or adapt old bespoke result models where safe
   - Preserve existing business logic thresholds unless the current implementation is clearly inconsistent with acceptance criteria

5. **Implement `FinanceAgentInsight` persistence behavior**
   - Ensure each normalized result maps to a persisted `FinanceAgentInsight` record with:
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - createdAt
     - updatedAt
   - If the entity already exists, extend it minimally to satisfy the acceptance criteria
   - Add/update EF configuration and repository logic as needed

6. **Implement active/update/resolve lifecycle**
   - Introduce matching logic so the system can identify whether a newly evaluated condition corresponds to an existing active insight
   - Expected behavior:
     - if condition is active and no matching active insight exists: create new insight
     - if condition is active and matching active insight exists: update existing insight as needed, do not create duplicate
     - if previously active matching insight is no longer true: mark it resolved and update `updatedAt`, do not create a new record
   - Use a stable key based on tenant + check code + entity reference + condition identity
   - Be careful not to resolve unrelated insights for the same entity

7. **Unify API response shape**
   - Identify current dashboard/entity endpoints returning check-specific payloads
   - Replace or adapt them to return one normalized insight DTO shape
   - Ensure consumers can render:
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - timestamps
     - check metadata if useful
   - Avoid introducing check-specific branching in the response contract

8. **Preserve architecture boundaries**
   - Domain:
     - contracts, entities, enums, core lifecycle rules
   - Application:
     - orchestration, handlers, DTO mapping, use cases
   - Infrastructure:
     - persistence, repositories, EF mappings
   - API:
     - endpoint contract only
   - Do not put business logic in controllers

9. **Add/adjust tests**
   - Unit tests for:
     - shared contract behavior
     - normalized result mapping
     - resolve-vs-create logic
   - Integration/API tests for:
     - unified response shape
     - persistence of new insights
     - resolving previously active insights
     - tenant-safe behavior if test infrastructure supports it

10. **Keep changes incremental and safe**
   - Prefer adapting existing classes over introducing parallel duplicate systems
   - If old DTOs/endpoints are used elsewhere, preserve compatibility only if easy; otherwise update callers in the same task scope
   - Document any unavoidable follow-up gaps in code comments only if the repo convention supports them

Implementation details to enforce:
- Use existing solution conventions for:
  - namespaces
  - MediatR/CQRS patterns if present
  - EF Core configuration
  - result wrappers / response envelopes
  - enum/string persistence
- Maintain tenant scoping on all reads/writes
- Use UTC timestamps
- Avoid duplicate insight creation during repeated check runs
- Prefer deterministic matching keys over fuzzy text matching

# Validation steps
1. Build and run tests:
   - `dotnet build`
   - `dotnet test`

2. Add or run targeted tests validating:
   - all four checks implement the shared contract
   - all four checks return the normalized result model
   - a triggered condition creates a `FinanceAgentInsight`
   - rerunning the same active condition updates/reuses the existing active insight instead of duplicating it
   - when the condition disappears, the existing insight is marked resolved
   - dashboard and entity endpoints return the same normalized insight shape

3. Manually inspect generated API payloads if possible:
   - confirm no check-specific branching fields are required by consumers
   - confirm timestamps and status fields are populated
   - confirm entity reference is included consistently

4. If persistence schema changes are required:
   - create/update migration according to repo conventions
   - verify schema maps correctly
   - ensure tests pass against the updated schema

5. Confirm no tenant leakage:
   - insight queries and updates must always be scoped by company/tenant

# Risks and follow-ups
- **Risk: existing checks may be implemented in inconsistent layers**
  - Mitigation: first map current implementations before refactoring; consolidate carefully without breaking unrelated flows

- **Risk: no stable condition identity exists for resolve behavior**
  - Mitigation: introduce a deterministic condition key based on check code + entity reference + normalized trigger identity

- **Risk: dashboard/entity consumers may depend on old bespoke DTOs**
  - Mitigation: search for all usages and update in the same change set where feasible

- **Risk: `FinanceAgentInsight` may be missing required fields or schema support**
  - Mitigation: extend entity and persistence mapping minimally; add migration if needed

- **Risk: duplicate insights from concurrent runs**
  - Mitigation: use repository matching logic and, if supported, unique constraints/indexes on active-condition identity

Follow-ups to note if not fully addressed in this task:
- add explicit DB uniqueness protections for active insight identity
- add richer audit/history for insight state transitions
- add pagination/filtering/sorting for unified insight APIs if not already present
- align UI components to the normalized response contract if any still branch on check type