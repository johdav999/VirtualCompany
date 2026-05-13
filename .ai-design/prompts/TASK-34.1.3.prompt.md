# Goal
Implement backlog task **TASK-34.1.3** for story **US-34.1** by adding **tenant-scoped EF Core persistence behavior** for the sales domain, including:
- tenant-safe repositories/query paths
- global query filters for tenant isolation and soft delete behavior
- migration coverage for the sales tables and indexes
- tenant-safe seeding for system pipeline stages
- integration tests proving cross-tenant isolation and soft delete filtering

The outcome must directly satisfy the acceptance criteria and specifically prevent **cross-tenant data leakage** in reads and writes.

# Scope
In scope:
- Sales persistence model configuration in Infrastructure for:
  - Leads
  - Deals
  - Contacts
  - CustomerCompanies
  - SalesActivities
  - SalesPipelineStages
  - SalesAgentRecommendations
  - SalesActionApprovals
  - SalesEmailLinks
- EF Core migration updates for the above tables, relationships, indexes, audit columns, tenant column, and soft delete columns where applicable
- Tenant-aware DbContext behavior using current tenant/company context
- Global query filters for:
  - `CompanyId` tenant isolation on tenant-owned sales entities
  - soft delete exclusion on entities with soft delete enabled
- Repository/query enforcement so cross-tenant reads/writes are rejected or impossible through normal application paths
- Seed logic for system pipeline stages:
  - New
  - Qualified
  - Proposal
  - Won
  - Lost
- Integration tests validating:
  - tenant isolation on reads
  - tenant isolation on writes/updates
  - soft delete filtering
  - seeded pipeline stages behavior
  - migration/index presence where practical

Out of scope unless required by existing code structure:
- UI changes
- API contract redesign
- non-sales domain persistence refactors beyond shared tenant infrastructure needed to support this task
- broad authorization changes unrelated to repository/query enforcement

# Files to touch
Inspect the solution first, then update the relevant files in these likely areas.

Likely Infrastructure files:
- `src/VirtualCompany.Infrastructure/.../Persistence/.../VirtualCompanyDbContext.cs`
- `src/VirtualCompany.Infrastructure/.../Persistence/.../Configurations/*`
- `src/VirtualCompany.Infrastructure/.../Repositories/*`
- `src/VirtualCompany.Infrastructure/.../Migrations/*`
- `src/VirtualCompany.Infrastructure/.../Seeding/*`
- any tenant provider/current company context abstractions used by Infrastructure

Likely Domain/Application files if needed:
- sales entities under `src/VirtualCompany.Domain/...`
- repository interfaces under `src/VirtualCompany.Application/...` or `src/VirtualCompany.Domain/...`
- shared base entity interfaces/classes for:
  - `CompanyId`
  - `CreatedAt`
  - `UpdatedAt`
  - soft delete fields

Likely test files:
- `tests/VirtualCompany.Api.Tests/...`
- or any Infrastructure integration test project already used for EF Core/database tests
- add new integration tests for tenant isolation and soft delete behavior in the most appropriate existing test location

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration conventions and local validation expectations.

# Implementation plan
1. **Discover existing persistence conventions**
   - Inspect the current architecture for:
     - DbContext name and registration
     - tenant context abstraction
     - base entity interfaces/classes
     - soft delete conventions
     - migration naming conventions
     - repository patterns already used
   - Reuse existing patterns rather than inventing parallel infrastructure.

2. **Identify all sales entities and relationships**
   - Confirm the sales domain entities already exist or add missing persistence mappings if domain entities are present.
   - Verify foreign key relationships match the defined domain model for:
     - Leads
     - Deals
     - Contacts
     - CustomerCompanies
     - SalesActivities
     - SalesPipelineStages
     - SalesAgentRecommendations
     - SalesActionApprovals
     - SalesEmailLinks
   - Ensure each tenant-owned table includes:
     - `CompanyId`
     - `CreatedAt`
     - `UpdatedAt`
     - soft delete columns where global soft delete is enabled in the project conventions

3. **Add or complete EF Core entity configurations**
   - Use `IEntityTypeConfiguration<T>` where that is the project pattern.
   - Configure:
     - table names
     - keys
     - required properties
     - FK relationships
     - delete behaviors appropriate to avoid accidental cascade leakage
     - indexes on:
       - `CompanyId`
       - `Status` where applicable
       - `CreatedAt` where applicable
   - If composite or covering indexes are already a project convention, follow that convention while still satisfying the acceptance criteria.

4. **Implement tenant-scoped global query filters**
   - Add query filters in DbContext/model configuration for all tenant-owned sales entities so queries are automatically scoped to the current company.
   - Add soft delete query filters for entities with soft delete enabled.
   - Ensure filters compose correctly, e.g. tenant filter AND not deleted.
   - Avoid hardcoding tenant IDs; use the existing tenant/company context service injected into DbContext or equivalent supported pattern.

5. **Enforce tenant-safe writes**
   - Ensure inserts/updates cannot write across tenants.
   - Preferred behavior:
     - new tenant-owned entities automatically receive the current `CompanyId` if that is the established convention
     - updates/deletes reject entities whose `CompanyId` does not match current tenant
   - Add SaveChanges interception/validation if needed to prevent:
     - attaching an entity from another tenant
     - changing `CompanyId`
     - updating/deleting cross-tenant rows
   - If the codebase already has a tenant-owned marker interface, use it.

6. **Implement soft delete behavior**
   - For entities with global soft delete enabled:
     - configure soft delete columns
     - ensure delete operations mark as deleted instead of physical delete where that is the project convention
     - ensure normal queries exclude soft-deleted rows
     - ensure tests can verify filtered vs unfiltered behavior if admin/internal access exists
   - Do not add soft delete to entities that are intentionally hard-deleted unless existing conventions require it.

7. **Add tenant-safe seeding for system pipeline stages**
   - Seed only the system pipeline stages:
     - New
     - Qualified
     - Proposal
     - Won
     - Lost
   - Do not seed leads, deals, contacts, or customer companies.
   - Make seeding idempotent and tenant-safe according to the project’s system configuration approach.
   - “Exactly once” means no duplicate stage rows from repeated startup/migration execution.
   - If stages are tenant-owned, ensure seeding respects tenant-safe configuration and uniqueness constraints.
   - If stages are system templates with tenant-safe consumption, align with the existing architecture and document the choice in code comments where helpful.

8. **Generate/update EF Core migration**
   - Create a migration covering:
     - all required sales tables
     - foreign keys
     - audit columns
     - tenant columns
     - soft delete columns where applicable
     - indexes
     - seed data for pipeline stages if the project uses migration-based seeding
   - Ensure the generated migration is clean and deterministic.
   - Verify the migration reflects PostgreSQL-compatible types and naming conventions.

9. **Add integration tests**
   - Add automated integration tests that prove:
     - tenant A cannot read tenant B sales records through repository/query paths
     - tenant A cannot update/delete/write against tenant B records
     - soft-deleted records are excluded from normal queries
     - seeded pipeline stages exist once and only once per intended tenant-safe configuration
     - no unintended seed data exists for leads/deals/customers
   - Prefer real database integration tests if the project already supports PostgreSQL test infrastructure.
   - If only a lighter integration setup exists, use the highest-fidelity option available; avoid EF InMemory for query filter behavior if possible.

10. **Verify indexes and migration output**
   - Assert index existence in one of these ways depending on current test infrastructure:
     - inspect generated migration operations
     - query PostgreSQL system catalogs in integration tests
     - validate via `DbContext.Database.GenerateCreateScript()` only if no better option exists
   - Confirm `CompanyId`, `Status`, and `CreatedAt` indexes exist for all applicable sales tables.

11. **Keep changes minimal and aligned**
   - Do not refactor unrelated modules.
   - Keep naming, namespaces, and folder structure consistent with the repository.
   - Add concise comments only where the tenant/soft-delete behavior is non-obvious.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If the repo supports targeted test execution, run the new integration tests directly as well.

4. Validate migration artifacts:
   - confirm the migration includes all required sales tables and foreign keys
   - confirm indexes on `CompanyId`, `Status`, and `CreatedAt` for applicable tables
   - confirm soft delete columns are present where required
   - confirm pipeline stage seed entries are present and no lead/deal/customer seed data was added

5. Validate tenant isolation behavior through tests:
   - cross-tenant read returns no result / not found as appropriate
   - cross-tenant update/delete/write is rejected
   - same-tenant access succeeds

6. Validate soft delete behavior through tests:
   - soft-deleted rows are excluded from normal queries
   - non-deleted rows remain visible
   - delete path performs soft delete where configured

7. In the final work summary, include:
   - files changed
   - migration name
   - tests added
   - any assumptions made about tenant seeding model

# Risks and follow-ups
- The repo may not yet have a unified tenant provider or soft delete abstraction; if missing, add the smallest reusable implementation needed without broad platform refactoring.
- EF Core global query filters can behave unexpectedly with required navigations and seeding; verify relationship loading carefully.
- SaveChanges-based tenant enforcement must not break legitimate system-level operations or migrations; keep system/seeding paths explicit.
- If pipeline stages are modeled as tenant-owned rows, uniqueness constraints may be needed to prevent duplicate stage names per company.
- If integration tests currently use EF InMemory, consider upgrading the new tests to SQLite/PostgreSQL-backed tests because InMemory can hide relational/query-filter issues.
- Follow-up work may be needed to extend the same tenant-safe repository/query enforcement pattern across non-sales modules if not already standardized.