# Goal

Implement backlog task **TASK-34.1.2** for story **US-34.1 Implement sales domain model, EF Core migration, and tenant-safe persistence** by creating a **deployable EF Core migration** and supporting persistence/test updates for the sales domain.

The coding agent must deliver:

- EF Core migration for sales tables:
  - `Leads`
  - `Deals`
  - `Contacts`
  - `CustomerCompanies`
  - `SalesActivities`
  - `SalesPipelineStages`
  - `SalesAgentRecommendations`
  - `SalesActionApprovals`
  - `SalesEmailLinks`
- Correct foreign keys matching the defined domain relationships already present or introduced in this task.
- Tenant-safe persistence with `CompanyId`, `CreatedAt`, `UpdatedAt`, and soft delete columns where the project’s global soft delete pattern applies.
- Indexes on `CompanyId`, `Status`, and `CreatedAt` for all applicable sales tables.
- Seed data for system pipeline stages:
  - `New`
  - `Qualified`
  - `Proposal`
  - `Won`
  - `Lost`
- No seed data for leads, deals, contacts, or customer companies.
- Automated tests proving tenant isolation for repository/query paths and rejecting cross-tenant reads/writes.

Work within the existing architecture and conventions of this repository. Prefer minimal, production-ready changes over speculative refactors.

# Scope

In scope:

- Inspect current domain, infrastructure, DbContext, entity configurations, migration patterns, and test conventions.
- Add or complete sales entity persistence mappings in EF Core.
- Ensure all tenant-owned sales tables include required audit and tenant columns.
- Apply soft delete columns only where the existing global soft delete infrastructure expects them.
- Create a new EF Core migration and verify generated SQL/schema intent.
- Add seed configuration for system pipeline stages in a tenant-safe/system-safe way consistent with current seeding patterns.
- Add or update repository/query enforcement and tests for tenant isolation.
- Validate migration builds, applies, and is testable.

Out of scope:

- UI work.
- API endpoint implementation unless required only to support existing persistence tests.
- Seeding any business/customer transactional records beyond system pipeline stages.
- Broad architecture refactors unrelated to sales persistence.
- Introducing a new tenancy model if one already exists.

# Files to touch

Inspect first, then modify only what is necessary. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - sales entities/value objects/enums if missing or incomplete
- `src/VirtualCompany.Infrastructure/**`
  - DbContext
  - entity type configurations
  - repository/query implementations
  - tenant enforcement infrastructure
  - seed configuration
  - migrations folder
- `src/VirtualCompany.Application/**`
  - only if repository/query contracts need small updates for tenant-safe access
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for tenant isolation and migration/schema verification if this test project already hosts those patterns

Also inspect:

- `docs/postgresql-migrations-archive/README.md`
- `README.md`

If present, prefer existing locations/conventions for:

- `IEntityTypeConfiguration<>`
- base entity/auditable entity/soft delete abstractions
- tenant provider or company context access
- migration naming
- integration test database setup

# Implementation plan

1. **Discover existing persistence conventions**
   - Identify:
     - the main EF Core `DbContext`
     - how `CompanyId` tenant scoping is modeled
     - audit column conventions for `CreatedAt` and `UpdatedAt`
     - soft delete implementation and which entities participate
     - how indexes are typically declared
     - how seed data is handled
     - how migrations are generated and stored
   - Confirm whether sales entities already exist in domain code and whether relationships are partially defined.

2. **Model or complete sales entity mappings**
   - Ensure the following entities are mapped in EF Core:
     - `Lead`
     - `Deal`
     - `Contact`
     - `CustomerCompany`
     - `SalesActivity`
     - `SalesPipelineStage`
     - `SalesAgentRecommendation`
     - `SalesActionApproval`
     - `SalesEmailLink`
   - For each tenant-owned sales table, ensure columns include:
     - `CompanyId`
     - `CreatedAt`
     - `UpdatedAt`
     - soft delete columns if the project’s global soft delete applies
   - Use PostgreSQL-friendly types and naming conventions already used by the project.
   - Ensure FK relationships match the domain model. Infer from names if not already defined, but do not invent unnecessary relationships. Typical expected relationships likely include:
     - leads/deals/contacts/customer companies scoped by `CompanyId`
     - deals linked to pipeline stages
     - activities linked to lead and/or deal and possibly contact/customer company if modeled
     - recommendations and approvals linked to lead/deal/activity as defined in domain
     - email links linked to sales records as defined in domain
   - If relationship ambiguity exists in code, align with existing domain classes first, not assumptions.

3. **Add indexes**
   - For all applicable sales tables, add indexes on:
     - `CompanyId`
     - `Status`
     - `CreatedAt`
   - “Applicable” means only where the column exists and is semantically valid.
   - If project conventions prefer composite indexes for tenant filtering, keep required single-column or convention-equivalent indexes visible and verifiable in migration output.
   - Ensure indexes are explicitly represented in entity configuration so they appear in the generated migration.

4. **Implement pipeline stage seed data**
   - Seed only system pipeline stages:
     - `New`
     - `Qualified`
     - `Proposal`
     - `Won`
     - `Lost`
   - Seed exactly once in a way that is safe for shared-schema multi-tenancy.
   - Do **not** seed tenant business data such as leads, deals, contacts, or customer companies.
   - Follow existing seed strategy:
     - if system-owned rows use `CompanyId = null`, use that only if already established by the codebase and compatible with acceptance criteria
     - otherwise use the project’s established “system configuration” pattern
   - Make the seed deterministic with stable IDs if the codebase convention supports it, so migrations remain repeatable.

5. **Enforce tenant isolation in repository/query paths**
   - Inspect existing repository/query services for sales entities.
   - Ensure all reads and writes are scoped by the active tenant/company context.
   - Cross-tenant access must be rejected, not silently allowed.
   - Prefer existing enforcement mechanisms:
     - global query filters
     - repository predicates
     - save interceptors/guards
     - application service validation
   - If writes can attach foreign entities from another tenant, add validation to prevent cross-tenant FK misuse.

6. **Create the EF Core migration**
   - Generate a migration with a clear name, e.g. similar to:
     - `AddSalesDomainTables`
     - or repository naming convention equivalent
   - Review the generated migration manually and adjust if needed so it clearly contains:
     - table creation
     - FK constraints
     - required columns
     - indexes
     - seed inserts for pipeline stages only
   - Ensure the migration is deployable and does not depend on local-only assumptions.

7. **Add automated tests**
   - Add/update integration tests to verify:
     - migration can be applied successfully
     - expected sales tables exist
     - expected indexes exist in model and/or target database depending on current test infrastructure
     - seeded pipeline stages exist exactly once
     - no seeded leads/deals/customers exist
     - cross-tenant reads are blocked or return no results per project convention
     - cross-tenant writes are rejected
   - Prefer existing integration test style in `tests/VirtualCompany.Api.Tests`.

8. **Keep changes focused**
   - Avoid renaming unrelated entities or changing broad conventions.
   - If you must introduce a small shared helper for tenant-safe sales persistence, keep it localized and consistent with existing architecture.

# Validation steps

Run and verify in this order where possible:

1. **Restore/build**
   - `dotnet build`

2. **Generate/apply migration if not already committed**
   - Use the repository’s established EF Core migration command pattern.
   - If no documented pattern exists, determine the startup project and infrastructure project before running `dotnet ef`.

3. **Inspect migration contents**
   - Confirm the migration includes:
     - all required sales tables
     - FK constraints
     - `CompanyId`, `CreatedAt`, `UpdatedAt`
     - soft delete columns where applicable
     - indexes on `CompanyId`, `Status`, `CreatedAt` for applicable tables
     - seed rows for exactly the five pipeline stages
     - no seed rows for leads/deals/contacts/customer companies

4. **Run tests**
   - `dotnet test`

5. **Schema verification**
   - If integration tests support direct DB inspection, verify:
     - indexes exist in the target database, not only in the EF model
     - seed rows are unique and not duplicated after re-application/setup

6. **Tenant isolation verification**
   - Confirm automated tests cover:
     - read isolation across two companies
     - write rejection when entity `CompanyId` mismatches active tenant
     - FK misuse rejection when linking records across tenants

7. **Final review**
   - Ensure migration files are included.
   - Ensure no accidental seed data for transactional sales records.
   - Ensure markdown/docs updates only if needed for migration workflow clarity.

# Risks and follow-ups

- **Risk: existing sales domain relationships may be incomplete or ambiguous**
  - Mitigation: derive FK mappings from current domain classes/configuration first; avoid inventing extra links.

- **Risk: soft delete conventions may not apply uniformly**
  - Mitigation: only add soft delete columns where the project’s global soft delete infrastructure expects them; do not force onto system tables unless consistent.

- **Risk: tenant-safe seed strategy may conflict with shared-schema rules**
  - Mitigation: follow the repository’s established system seed pattern exactly; if system-owned rows are global, ensure repository/query logic treats them safely.

- **Risk: indexes on `Status` may not apply to every table**
  - Mitigation: only add where a `Status` column exists; document applicability in code comments or test names if needed.

- **Risk: cross-tenant writes can slip through via navigation properties**
  - Mitigation: validate both root entity `CompanyId` and referenced FK targets in write paths/tests.

Follow-ups if discovered but not required for this task:

- Add database-level check constraints or partial indexes if the project wants stronger invariants later.
- Consider PostgreSQL row-level security in a future hardening task.
- Add explicit migration documentation if the repo currently lacks a standard EF migration workflow.