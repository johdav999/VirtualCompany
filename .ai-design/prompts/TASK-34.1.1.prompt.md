# Goal
Implement backlog task **TASK-34.1.1** for story **US-34.1** by adding a production-ready, tenant-safe sales persistence model in the .NET solution using **EF Core + PostgreSQL**.

The coding agent must:
- Add sales domain entities and relationships for:
  - Leads
  - Deals
  - Contacts
  - CustomerCompanies
  - SalesActivities
  - SalesPipelineStages
  - SalesAgentRecommendations
  - SalesActionApprovals
  - SalesEmailLinks
- Add EF Core entity configurations and DbSet registrations.
- Generate an EF Core migration that creates the required tables, foreign keys, indexes, and seed data.
- Ensure all tenant-owned sales tables are tenant-scoped with `CompanyId`, `CreatedAt`, `UpdatedAt`, and soft delete columns where the project’s global soft delete pattern applies.
- Enforce tenant isolation in repository/query paths and add automated tests proving cross-tenant reads/writes are rejected.
- Seed only the system pipeline stages `New`, `Qualified`, `Proposal`, `Won`, and `Lost` in a tenant-safe way, without seeding leads, deals, contacts, or customer companies.

# Scope
In scope:
- Inspect existing domain base types, tenant interfaces, audit fields, soft delete conventions, and EF Core configuration patterns already used in the solution.
- Implement sales aggregate entities in the appropriate Domain project location.
- Define relationships and required/optional foreign keys consistent with the task and existing architecture.
- Add Infrastructure EF Core configurations for all new sales entities.
- Register entities in the application DbContext.
- Add indexes on `CompanyId`, `Status`, and `CreatedAt` for all applicable sales tables.
- Add migration and verify generated schema.
- Implement tenant-safe seeding strategy for system pipeline stages.
- Add or update repositories/query services/specifications so all sales reads/writes are company-scoped.
- Add automated tests for migration/schema expectations and tenant isolation behavior.

Out of scope:
- UI, API endpoints, Blazor pages, MAUI changes.
- Seeding business data such as leads, deals, contacts, or customers.
- Full CRM business workflows beyond persistence and tenant-safe access.
- Non-sales modules unless required to integrate with existing tenant/base abstractions.

# Files to touch
Likely files and folders to inspect/update, following existing project conventions:

- `src/VirtualCompany.Domain/**`
  - Add sales entities/value objects/enums under an appropriate sales namespace/folder.
- `src/VirtualCompany.Infrastructure/**`
  - DbContext
  - EntityTypeConfiguration classes
  - Migrations folder
  - Repository/query implementations
  - Seed/bootstrap logic if seeding is handled here
- `src/VirtualCompany.Application/**`
  - Contracts or query/repository abstractions if sales persistence is exposed here
- `tests/VirtualCompany.Api.Tests/**`
  - Integration tests for tenant isolation and persistence behavior
- Potentially:
  - `README.md`
  - `docs/postgresql-migrations-archive/README.md`
  if migration workflow documentation needs alignment

At minimum, identify and update the concrete equivalents of:
- Domain base entity abstractions
- Tenant-scoped interfaces/base classes
- Soft delete interfaces/base classes
- Main EF Core DbContext
- Existing configuration registration mechanism
- Existing test fixtures for database-backed integration tests

# Implementation plan
1. **Inspect existing persistence conventions first**
   - Find how the solution currently models:
     - `CompanyId`
     - `CreatedAt` / `UpdatedAt`
     - soft delete columns
     - entity IDs
     - enum/string status persistence
     - global query filters
     - tenant resolution in repositories/services/tests
   - Reuse existing patterns exactly; do not invent parallel conventions.

2. **Design the sales entity model**
   - Add entities for:
     - `Lead`
     - `Deal`
     - `Contact`
     - `CustomerCompany`
     - `SalesActivity`
     - `SalesPipelineStage`
     - `SalesAgentRecommendation`
     - `SalesActionApproval`
     - `SalesEmailLink`
   - Include tenant ownership on all tenant-owned sales entities via `CompanyId`.
   - Include audit fields `CreatedAt` and `UpdatedAt`.
   - Include soft delete fields only if the project’s global soft delete pattern applies to that entity type/category.
   - Prefer explicit navigation properties and FK properties.

3. **Model relationships explicitly**
   Implement foreign keys matching the defined sales relationships discovered from existing domain intent. If not already defined elsewhere, use a pragmatic model that supports the aggregates safely:
   - `Lead`:
     - belongs to `Company`
     - may reference primary `Contact`
     - may reference `CustomerCompany`
     - may reference current `SalesPipelineStage`
     - may convert to or relate to `Deal`
   - `Deal`:
     - belongs to `Company`
     - may reference `Lead`
     - may reference `CustomerCompany`
     - may reference primary `Contact`
     - references current `SalesPipelineStage`
   - `Contact`:
     - belongs to `Company`
     - may belong to `CustomerCompany`
   - `CustomerCompany`:
     - belongs to `Company`
   - `SalesActivity`:
     - belongs to `Company`
     - may reference `Lead`
     - may reference `Deal`
     - may reference `Contact`
     - may reference `CustomerCompany`
   - `SalesAgentRecommendation`:
     - belongs to `Company`
     - should reference the relevant sales entity, likely `Lead` and/or `Deal`
   - `SalesActionApproval`:
     - belongs to `Company`
     - should reference `SalesAgentRecommendation` and/or target sales entity per existing approval modeling conventions
   - `SalesEmailLink`:
     - belongs to `Company`
     - links email/inbox references to `Lead`, `Deal`, `Contact`, and/or `CustomerCompany` as appropriate
   - `SalesPipelineStage`:
     - tenant-safe/system-safe stage definition model; ensure seeding approach does not create duplicate stages per tenant configuration

   Important:
   - Avoid cascade delete paths that could violate tenant safety or conflict with soft delete.
   - Prefer `Restrict`/`NoAction` where appropriate.
   - Ensure all FK relationships are company-safe in usage, even if DB-level composite tenant FK enforcement is not already a project pattern.

4. **Add EF Core configurations**
   For each entity:
   - Map table name explicitly.
   - Configure primary key.
   - Configure required columns and lengths.
   - Configure timestamps.
   - Configure soft delete columns/query filters if applicable.
   - Configure all foreign keys and delete behaviors.
   - Add indexes:
     - `CompanyId`
     - `Status` where the entity has a status column
     - `CreatedAt`
     - plus any obvious FK indexes needed by EF or query patterns
   - Keep naming consistent with existing migration/config style.

5. **Register entities in DbContext**
   - Add `DbSet<>` properties.
   - Ensure configuration discovery/registration includes the new sales configurations.
   - Ensure global query filters for tenant and soft delete apply consistently.

6. **Implement tenant-safe pipeline stage seeding**
   - Seed exactly these stages:
     - `New`
     - `Qualified`
     - `Proposal`
     - `Won`
     - `Lost`
   - Seed them exactly once in a tenant-safe system configuration pattern already used by the solution.
   - Do **not** seed leads, deals, contacts, or customer companies.
   - If the project uses migration-based `HasData`, verify it is actually tenant-safe; if not, implement startup/bootstrap/idempotent seeding consistent with architecture.
   - Prevent duplicate stage creation across reruns.

7. **Generate EF Core migration**
   - Create a migration that:
     - creates all required sales tables
     - creates all required foreign keys
     - creates indexes on `CompanyId`, `Status`, and `CreatedAt` where applicable
     - includes stage seed operations if migration-based seeding is the established safe pattern
   - Review generated migration manually; adjust if needed for naming, delete behavior, indexes, or seed idempotency.

8. **Enforce tenant isolation in repositories/query paths**
   - Inspect existing repository/query patterns and update them so sales entities cannot be read or written across tenants.
   - Ensure:
     - reads are filtered by current tenant/company context
     - writes reject mismatched `CompanyId`
     - entity linking across tenants is blocked
   - If there is an existing tenant provider/current company accessor, use it.
   - If there are existing guard/helper methods for tenant validation, extend them rather than duplicating logic.

9. **Add automated tests**
   Add integration tests covering:
   - migration/schema creates all required tables
   - expected indexes exist in migration and/or target database
   - pipeline stages are seeded exactly once and only the allowed system stages are seeded
   - cross-tenant read attempts are rejected or return not found/forbidden per existing conventions
   - cross-tenant write/link attempts are rejected
   - soft delete/global filters behave correctly if enabled for these entities

10. **Keep implementation production-ready**
   - Use UTC timestamps if that is the project convention.
   - Avoid nullable chaos; make required fields explicit.
   - Keep statuses/enums consistent and queryable.
   - Do not bypass existing abstractions for tenant enforcement.
   - Ensure code builds cleanly and tests pass.

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, add and verify migration:
   - Use the solution’s existing EF Core migration command pattern.
   - Confirm migration contains creation of:
     - Leads
     - Deals
     - Contacts
     - CustomerCompanies
     - SalesActivities
     - SalesPipelineStages
     - SalesAgentRecommendations
     - SalesActionApprovals
     - SalesEmailLinks

4. Inspect migration code manually for:
   - correct foreign keys
   - correct delete behaviors
   - `CompanyId` on all tenant-owned sales tables
   - `CreatedAt` and `UpdatedAt`
   - soft delete columns where applicable
   - indexes on `CompanyId`, `Status`, and `CreatedAt` for applicable tables

5. Apply migration against the test/dev database using the project’s normal workflow.

6. Verify seeded pipeline stages:
   - exactly `New`, `Qualified`, `Proposal`, `Won`, `Lost`
   - no duplicate seeding on rerun
   - no seeded leads/deals/customers

7. Run full automated tests:
   - `dotnet test`

8. If integration tests inspect the live schema, verify:
   - tables exist
   - indexes exist in target PostgreSQL database
   - tenant isolation tests pass for both reads and writes

9. In the final implementation notes/PR summary, include:
   - list of entities added
   - relationship summary
   - migration name
   - seeding approach used
   - tenant isolation protections added
   - any assumptions made where the existing domain did not yet define a relationship explicitly

# Risks and follow-ups
- **Risk: existing tenant/soft-delete conventions may differ from assumptions.**
  - Mitigation: inspect and reuse current base abstractions and query filters before coding.

- **Risk: migration-based seeding may not be tenant-safe for system pipeline stages.**
  - Mitigation: prefer the project’s established idempotent bootstrap seeding pattern if `HasData` would duplicate or hardcode tenant-specific rows.

- **Risk: cross-tenant FK linking may still be possible if only simple FK constraints are used.**
  - Mitigation: enforce tenant validation in repositories/application services/tests, and consider composite uniqueness/FK strategies later if aligned with project conventions.

- **Risk: ambiguous sales relationships because the task references “defined relationships” but the exact model may not yet exist in code.**
  - Mitigation: inspect backlog/story/domain docs for existing intent; if absent, implement the smallest coherent relational model and document assumptions clearly.

- **Risk: global query filters can hide data in tests and create false positives.**
  - Mitigation: write tests that validate both filtered behavior and underlying persisted data where appropriate.

Follow-ups after this task, if not already covered elsewhere:
- Add application-layer commands/queries for sales CRUD and lifecycle transitions.
- Add API endpoints and authorization policies for sales entities.
- Add richer database constraints for tenant-safe relationship integrity if the architecture later standardizes composite tenant-aware keys.
- Add reporting/query optimizations once real sales dashboard workloads are known.