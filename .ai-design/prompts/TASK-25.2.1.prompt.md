# Goal
Implement backlog task **TASK-25.2.1** for story **US-25.2 Persist reconciliation suggestions and accepted reconciliation outcomes** by designing and applying the PostgreSQL schema, EF Core mappings, and database tests needed to persist:

- reconciliation suggestions
- reconciliation results created from accepted suggestions
- suggestion rejection state
- tenant-scoped audit metadata for all state changes

The implementation must satisfy these behaviors:

- store suggestion records with source entity IDs, target entity IDs, match type, confidence score, rule breakdown, status, timestamps
- on acceptance, persist a reconciliation result linking the relevant records and mark superseded suggestions as no longer actionable
- on rejection, update suggestion status to rejected and exclude it from default open suggestion queries
- ensure all records are tenant-scoped and auditable with created-by and updated-by metadata
- verify create, accept, reject, and query behavior for:
  - payment-to-bank
  - invoice-to-payment
  - bill-to-payment

# Scope
In scope:

- inspect the existing finance/accounting/reconciliation-related domain and infrastructure patterns before coding
- add new domain entities/value objects/enums as needed for reconciliation suggestions and reconciliation results
- add EF Core entity configurations and DbSet registrations
- create and apply a migration for PostgreSQL
- model tenant scoping via `company_id`
- persist audit metadata via created/updated timestamps and created-by/updated-by actor/user fields consistent with existing conventions
- support suggestion lifecycle states at minimum for open/pending, accepted, rejected, and superseded/non-actionable
- support polymorphic reconciliation pairs for the three required record combinations
- implement repository/query behavior or persistence helpers needed for:
  - creating suggestions
  - accepting a suggestion
  - rejecting a suggestion
  - querying default open suggestions
- add database-focused tests covering acceptance criteria

Out of scope unless required by existing architecture/tests:

- UI work
- API endpoints unless already partially scaffolded and necessary for tests
- orchestration/LLM logic
- broad audit event pipeline changes beyond persistence metadata on these tables
- unrelated refactors

# Files to touch
Inspect first, then update the appropriate files under these likely areas:

- `src/VirtualCompany.Domain/**`
  - reconciliation-related entities, enums, and domain logic
- `src/VirtualCompany.Infrastructure/**`
  - DbContext
  - EF Core configurations
  - repositories/query services
  - migrations
- `src/VirtualCompany.Application/**`
  - only if application-layer contracts/handlers are required for persistence tests
- `tests/**`
  - infrastructure/database tests for reconciliation persistence behavior

Likely concrete targets include files such as:

- `src/VirtualCompany.Infrastructure/*DbContext*.cs`
- `src/VirtualCompany.Infrastructure/**/Configurations/*.cs`
- `src/VirtualCompany.Infrastructure/Migrations/*`
- `tests/**/Infrastructure/*`
- `tests/**/Database/*`

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to align with migration and database workflow conventions already used in the repo.

# Implementation plan
1. **Discover existing patterns before coding**
   - Find the primary EF Core `DbContext` and existing entity configuration style.
   - Identify how tenant-owned entities currently model:
     - `company_id`
     - timestamps
     - created-by / updated-by metadata
   - Identify whether actor metadata uses user IDs only or actor type + actor ID.
   - Search for existing finance entities representing:
     - payments
     - bank transactions / bank records
     - invoices
     - bills
   - Reuse naming and schema conventions already present in the codebase.

2. **Design the persistence model**
   - Add a reconciliation suggestion entity/table that includes at minimum:
     - `id`
     - `company_id`
     - source record type
     - source record ID
     - target record type
     - target record ID
     - match type
     - confidence score
     - rule breakdown as JSON/JSONB
     - status
     - optional accepted/rejected/superseded timestamps if useful
     - `created_at`, `updated_at`
     - `created_by`, `updated_by` metadata per project convention
   - Add a reconciliation result entity/table that includes at minimum:
     - `id`
     - `company_id`
     - accepted suggestion ID
     - source record type / ID
     - target record type / ID
     - accepted match type
     - optional snapshot of confidence/rule breakdown if useful for auditability
     - `created_at`, `updated_at`
     - `created_by`, `updated_by`
   - Use enums or strongly typed constants for:
     - reconciliation pair type or record type
     - suggestion status
     - match type
   - Prefer a generalized model that supports all three required pairings without separate tables.

3. **Model suggestion lifecycle and actionability**
   - Define statuses so default open queries exclude at least:
     - rejected
     - accepted
     - superseded
   - Ensure acceptance flow:
     - creates a reconciliation result
     - marks the accepted suggestion appropriately
     - marks competing/superseded suggestions for the same linked records as non-actionable
   - Ensure rejection flow:
     - updates status to rejected
     - updates audit metadata
   - Be explicit about what counts as “superseded suggestions”:
     - at minimum, suggestions involving the same source-target pair or same records made obsolete by the accepted result
   - Document assumptions in code comments if business rules are not already defined elsewhere.

4. **Implement domain and infrastructure**
   - Add domain entities and any domain methods for:
     - create suggestion
     - accept suggestion
     - reject suggestion
     - supersede related suggestions
   - Add EF Core configurations:
     - table names
     - keys
     - enum conversions if needed
     - JSONB mapping for rule breakdown
     - indexes
   - Add indexes for likely query paths:
     - `company_id`
     - status
     - source type + source ID
     - target type + target ID
     - accepted suggestion/result uniqueness where appropriate
   - Consider uniqueness/constraints to prevent duplicate active reconciliation results for the same record pair if that matches current business rules.

5. **Create migration**
   - Generate a migration that creates the new tables, constraints, and indexes.
   - Ensure PostgreSQL-compatible types are used:
     - `uuid`
     - `timestamptz`
     - `jsonb`
     - numeric/decimal for confidence score
   - Keep migration naming aligned with repo conventions.
   - Do not edit archived migration docs unless necessary.

6. **Implement query behavior**
   - Add repository/query methods or testable DbContext queries for:
     - inserting suggestions
     - retrieving default open suggestions
     - accepting a suggestion transactionally
     - rejecting a suggestion
   - Acceptance should be transactional so result creation and suggestion status updates happen atomically.
   - Ensure all queries are tenant-scoped.

7. **Add database tests**
   - Add tests that verify for each pair type:
     - payment-to-bank suggestion can be created and queried as open
     - invoice-to-payment suggestion can be created and queried as open
     - bill-to-payment suggestion can be created and queried as open
   - Add tests that verify:
     - accepting a suggestion creates a reconciliation result
     - accepted suggestion is no longer returned in default open queries
     - superseded competing suggestions are marked non-actionable and excluded from open queries
     - rejecting a suggestion marks it rejected and excludes it from open queries
     - tenant scoping prevents cross-tenant leakage
     - created-by and updated-by metadata are persisted and updated on state changes
   - Prefer integration/database tests over pure unit tests for this task.

8. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep persistence in PostgreSQL via infrastructure layer.
   - Maintain shared-schema multi-tenancy with `company_id` enforcement.
   - Preserve auditability as a domain feature through persisted metadata.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run targeted tests if a focused test project exists for infrastructure/database persistence:
   - `dotnet test`

3. If migrations are part of test/setup flow, verify the new migration applies cleanly in the existing test database strategy.

4. Manually confirm in code review that acceptance criteria are met:
   - suggestion record stores all required fields
   - acceptance creates reconciliation result and supersedes related suggestions
   - rejection updates status and removes from default open queries
   - all records include tenant scope and audit metadata
   - tests cover payment-to-bank, invoice-to-payment, bill-to-payment

5. Include in your final summary:
   - files changed
   - schema/tables added
   - statuses introduced
   - indexes/constraints added
   - tests added
   - any assumptions made about superseding logic or actor metadata

# Risks and follow-ups
- **Unknown existing finance schema:** payment, bank, invoice, and bill entities may already use specific naming or bounded contexts; align to those exactly rather than inventing parallel terminology.
- **Audit metadata convention ambiguity:** if the repo already uses a base auditable entity or interceptors, integrate with that instead of duplicating fields manually.
- **Supersede rule ambiguity:** if business rules do not define whether superseding applies to same exact pair only or broader overlapping records, implement the narrowest safe rule and document it clearly.
- **Uniqueness constraints:** be careful not to over-constrain valid many-to-many reconciliation scenarios if partial allocations are expected later.
- **Migration generation environment:** if local migration tooling differs from CI expectations, follow repo conventions exactly.
- **Follow-up likely needed:** application commands/handlers and API endpoints for reconciliation workflows may be separate tasks after schema persistence is in place.