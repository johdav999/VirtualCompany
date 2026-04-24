# Goal

Implement backlog task **TASK-29.2.3 — Create FinanceAgentInsight persistence model, repository, and resolve-update lifecycle logic** for story **US-29.2 Unified financial check framework and insight persistence** in the existing .NET modular monolith.

The implementation must:

- Introduce a persisted `FinanceAgentInsight` model for normalized financial check outputs.
- Add repository/data access support with tenant-safe querying and update behavior.
- Implement lifecycle logic so active conditions create or update active insights, and cleared conditions mark prior insights as resolved instead of creating duplicates.
- Support a single normalized API-facing response shape that dashboard and entity pages can consume without check-specific branching.
- Align with the architecture: ASP.NET Core + modular monolith + PostgreSQL + shared-schema multi-tenancy + CQRS-lite.

Do not redesign unrelated finance features. Keep the implementation focused, incremental, and consistent with existing project patterns.

# Scope

In scope:

- Domain model(s) and enums/value objects needed for `FinanceAgentInsight`.
- Persistence mapping and migration for a new finance insights table.
- Repository abstraction and implementation for insight lookup/upsert/resolve flows.
- Shared normalized financial check result contract used by:
  - cash risk
  - transaction anomaly
  - receivables
  - payables
- Application/service lifecycle logic that:
  - identifies an insight by stable condition identity
  - creates a new active insight when condition first appears
  - updates an existing active insight when condition remains true
  - resolves an existing active insight when condition is no longer true
- A normalized DTO/response shape for UI/API consumers.
- Tests covering persistence and resolve-update lifecycle behavior.

Out of scope unless required by existing code structure:

- Full UI implementation.
- New finance analytics beyond insight persistence.
- Broad refactors outside finance check execution flow.
- Introducing microservices, event sourcing, or non-standard persistence patterns.

# Files to touch

Inspect the solution first and adapt to actual conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - finance domain entities/models
  - shared enums/value objects
  - repository interfaces if domain-owned
- `src/VirtualCompany.Application/`
  - finance check contracts
  - normalized result DTOs
  - lifecycle orchestration service/handler
  - query DTO for unified API response shape
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration
  - repository implementation
  - DbContext updates
  - migration files
- `src/VirtualCompany.Api/`
  - only if an existing endpoint/query needs wiring to expose the unified response shape
- `tests/VirtualCompany.Api.Tests/` and/or other test projects
  - integration and lifecycle tests

Likely concrete file categories to add/update:

- `FinanceAgentInsight.cs`
- `FinanceInsightStatus.cs`
- `FinanceInsightSeverity.cs`
- `IFinanceAgentInsightRepository.cs`
- `FinanceAgentInsightRepository.cs`
- `ISharedFinancialCheck.cs` or equivalent
- `FinancialCheckResult.cs` or equivalent normalized result model
- `FinanceInsightDto.cs` or equivalent API response model
- `DbContext` registration/configuration
- EF Core migration for `finance_agent_insights`

If there is already a finance module or naming convention, follow that instead of inventing new folder structures.

# Implementation plan

1. **Survey existing finance and persistence patterns**
   - Find current implementations for cash risk, transaction anomaly, receivables, and payables checks.
   - Identify whether there is already a finance dashboard/entity query layer.
   - Reuse existing conventions for:
     - entities and aggregate roots
     - repository interfaces
     - EF Core configurations
     - tenant scoping
     - timestamps
     - status/severity enums
     - CQRS handlers and DTOs

2. **Define a shared financial check contract**
   - Introduce a common interface/base contract for all financial checks.
   - Each check must return a normalized result model rather than bespoke output.
   - The normalized result should include enough data to persist and render insights consistently, such as:
     - company/tenant reference
     - check type
     - stable condition key or fingerprint
     - severity
     - message
     - recommendation
     - entity reference type/id
     - whether the condition is currently active
     - optional metadata payload if project conventions allow
     - observed timestamp
   - Ensure all four check types implement this shared contract.

3. **Design the `FinanceAgentInsight` persistence model**
   - Add a domain entity representing a persisted insight.
   - Required persisted fields per acceptance criteria:
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - createdAt
     - updatedAt
   - Also include fields needed for lifecycle correctness:
     - `Id`
     - `CompanyId`
     - `CheckType`
     - stable `ConditionKey` or equivalent deduplication key
     - `EntityType`
     - `EntityId` or string reference
     - `ResolvedAt` if consistent with project patterns
   - Use explicit status values, e.g. `Active`, `Resolved`.
   - Prefer deterministic identity for “same condition” matching:
     - same tenant
     - same check type
     - same entity reference
     - same condition key

4. **Add EF Core mapping and database migration**
   - Register the new entity in the infrastructure persistence layer.
   - Create a migration for a `finance_agent_insights` table using existing naming conventions.
   - Include indexes to support lifecycle queries efficiently, likely:
     - `(company_id, status)`
     - `(company_id, check_type, condition_key, entity_type, entity_id)`
     - possibly a filtered/unique active index if supported by current migration style
   - Ensure all tenant-owned rows include `company_id`.
   - Use PostgreSQL-friendly types and timestamp conventions already used in the solution.

5. **Create repository abstraction and implementation**
   - Add repository methods needed for lifecycle operations, such as:
     - get active insight by company + check type + condition key + entity reference
     - add insight
     - update insight
     - resolve matching active insight(s)
     - list insights for dashboard/entity views
   - Keep repository methods tenant-safe and explicit.
   - Avoid generic “save anything” methods if the codebase prefers task-specific repository methods.

6. **Implement resolve-update lifecycle logic**
   - Add an application service/handler that processes normalized check results.
   - For each normalized result:
     - if condition is active:
       - look up existing active insight by stable identity
       - if none exists, create a new active insight
       - if one exists, update severity/message/recommendation/entity fields and `updatedAt`
     - if condition is inactive/no longer true:
       - find corresponding active insight
       - mark it resolved
       - set `updatedAt` and optionally `resolvedAt`
       - do not create a new record
   - If checks are run in batches, support processing a set of results atomically where practical.
   - Preserve idempotency: repeated runs with unchanged active conditions should not create duplicates.

7. **Provide a unified API/query response shape**
   - Add a DTO/view model that is independent of check-specific implementations.
   - It should be suitable for both dashboard and entity pages, for example:
     - insight id
     - check type/category
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - createdAt
     - updatedAt
   - If an existing query endpoint exists, adapt it to return this normalized shape.
   - Do not require consumers to branch on cash risk vs receivables vs payables vs anomaly just to render common insight cards/lists.

8. **Update existing financial checks to use the shared contract**
   - Refactor each of the four checks to emit the normalized result model.
   - Remove or adapt any check-specific persistence assumptions.
   - Keep business logic intact; only normalize outputs and route them through the shared lifecycle service.

9. **Add tests**
   - Add focused tests for:
     - each check implementing the shared contract
     - active condition creates a new insight
     - repeated active condition updates existing insight instead of duplicating
     - cleared condition resolves existing active insight
     - resolved condition does not create a new record when simply clearing prior state
     - unified query/DTO shape is returned consistently
     - tenant isolation on repository queries
   - Prefer integration tests for persistence lifecycle if the project already uses them; otherwise combine unit tests for orchestration with repository tests.

10. **Keep implementation aligned with project standards**
    - Follow existing namespace, folder, and naming conventions.
    - Use existing clock/time abstractions if present.
    - Use existing result/error handling patterns.
    - Keep changes small, reviewable, and production-safe.

# Validation steps

1. **Build and inspect**
   - Run:
     - `dotnet build`
   - Ensure all projects compile.

2. **Run tests**
   - Run:
     - `dotnet test`
   - Confirm new and existing tests pass.

3. **Migration validation**
   - Verify the EF Core migration is generated correctly and matches project conventions.
   - Confirm the new table includes required columns and indexes.
   - If the repo supports local DB migration execution, apply and verify schema.

4. **Behavior validation**
   - Validate these scenarios through tests or targeted execution:
     - cash risk check returns normalized result
     - transaction anomaly check returns normalized result
     - receivables check returns normalized result
     - payables check returns normalized result
     - first detection creates active `FinanceAgentInsight`
     - second run with same active condition updates same record
     - condition no longer true marks prior active record as resolved
     - dashboard/entity query returns same response shape for all insight types

5. **Data integrity validation**
   - Confirm no duplicate active insights exist for the same tenant + condition identity.
   - Confirm `createdAt` remains original on updates and `updatedAt` changes appropriately.
   - Confirm resolved insights remain historically visible if that matches existing query behavior.

# Risks and follow-ups

- **Stable condition identity risk**
  - If `ConditionKey` is poorly defined, duplicate or incorrectly merged insights may occur.
  - Follow-up: document and standardize condition key generation per check type.

- **Concurrency risk**
  - Parallel check runs could create duplicate active insights without a uniqueness strategy.
  - Follow-up: add a database uniqueness constraint for active identity if feasible, or handle optimistic concurrency/retry.

- **Existing finance check divergence**
  - Current checks may have inconsistent outputs or execution paths.
  - Follow-up: consolidate remaining finance check orchestration behind a single application service if not already present.

- **Query shape drift**
  - If dashboard and entity pages later add special-case fields, the unified contract could erode.
  - Follow-up: keep a core shared DTO and add optional metadata only when truly necessary.

- **Historical audit expectations**
  - Resolving instead of recreating records preserves continuity, but some stakeholders may later want resolution reason/history.
  - Follow-up: consider `resolvedAt`, `resolutionSource`, or audit event linkage if not included now.

- **Tenant enforcement**
  - Repository methods must always scope by `company_id`.
  - Follow-up: add explicit tenant-isolation tests if current coverage is weak.