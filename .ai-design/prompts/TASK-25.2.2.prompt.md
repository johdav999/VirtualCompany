# Goal
Implement the persistence layer and application service behavior for reconciliation suggestion lifecycle management under **TASK-25.2.2**: storing reconciliation suggestions, accepting suggestions into durable reconciliation results, rejecting suggestions, and querying open suggestions with correct tenant scoping and audit metadata.

# Scope
Implement the backlog task for **US-25.2 Persist reconciliation suggestions and accepted reconciliation outcomes** with the following required outcomes:

- Persist reconciliation suggestion records with:
  - tenant/company scope
  - source entity IDs
  - target entity IDs
  - match type
  - confidence score
  - rule breakdown
  - status
  - timestamps
  - created-by / updated-by metadata
- Persist reconciliation results when a suggestion is accepted:
  - link the relevant records
  - mark superseded/conflicting suggestions as no longer actionable
- Support rejection flow:
  - update suggestion status to rejected
  - exclude rejected suggestions from default open suggestion queries
- Ensure all records are tenant-scoped and auditable
- Add database/integration tests covering:
  - create
  - accept
  - reject
  - query behavior
  - for payment-to-bank, invoice-to-payment, and bill-to-payment reconciliation records

Constraints and expectations:

- Follow existing solution architecture and conventions already present in the repo.
- Keep implementation inside the current modular monolith boundaries.
- Prefer repository + application service changes over controller/UI work unless required by compile errors.
- Do not invent unrelated features.
- If schema/migrations for reconciliation tables are missing or incomplete, add the minimum required persistence changes.
- Use PostgreSQL-friendly mappings and patterns consistent with the existing Infrastructure project.
- Preserve CQRS-lite separation if the codebase already uses it.

# Files to touch
Inspect the repo first and then update the actual matching files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - reconciliation entities, enums, value objects, status definitions
- `src/VirtualCompany.Application/**`
  - reconciliation service interfaces/implementations
  - commands/handlers or application services for create/accept/reject/query
- `src/VirtualCompany.Infrastructure/**`
  - DbContext / EF Core configurations
  - repository implementations
  - migrations if needed
- `tests/**`
  - database/integration tests for reconciliation persistence behavior

Potential file patterns to look for:

- `*Reconciliation*`
- `*Suggestion*`
- `*Repository*`
- `*Service*`
- `*DbContext*`
- `*EntityTypeConfiguration*`
- `*Migrations*`
- `*Tests*`

Do not assume exact filenames from this prompt; discover and use the real project structure.

# Implementation plan
1. **Discover existing reconciliation model and persistence shape**
   - Search for existing reconciliation-related domain types, repositories, services, handlers, and tests.
   - Identify whether suggestion/result entities already exist partially.
   - Identify current multi-tenant and audit metadata conventions:
     - `company_id` / tenant ID usage
     - `created_at`, `updated_at`
     - `created_by`, `updated_by` or equivalent
   - Identify how entity relationships are modeled for payment, bank transaction, invoice, and bill records.

2. **Define or complete domain persistence model**
   - Ensure there is a durable entity for reconciliation suggestions with fields covering acceptance criteria:
     - ID
     - tenant/company ID
     - source entity type and source entity ID
     - target entity type and target entity ID
     - match type
     - confidence score
     - rule breakdown payload
     - status
     - created/updated timestamps
     - created-by / updated-by metadata
   - Ensure there is a durable entity for accepted reconciliation results with fields sufficient to link the reconciled records and audit the action.
   - Add/complete enums for statuses such as:
     - Open / Pending
     - Accepted
     - Rejected
     - Superseded / NoLongerActionable
   - Reuse existing enum/value object patterns if present.

3. **Implement repository methods**
   - Add or complete repository methods needed for:
     - creating a suggestion
     - retrieving suggestion by ID within tenant scope
     - querying default open suggestions
     - accepting a suggestion transactionally
     - rejecting a suggestion
     - finding conflicting/superseded suggestions for the same participating records
   - Acceptance behavior must:
     - persist a reconciliation result
     - update accepted suggestion status
     - mark superseded suggestions as no longer actionable
     - update audit metadata on all changed rows
   - Rejection behavior must:
     - set status to rejected
     - update audit metadata
   - Ensure default open query excludes:
     - rejected
     - accepted
     - superseded / no longer actionable

4. **Implement application service methods**
   - Add or complete service methods orchestrating repository behavior for:
     - create suggestion
     - accept suggestion
     - reject suggestion
     - query open suggestions
   - Ensure service methods require tenant/company context and actor/user context for audit fields.
   - Validate tenant ownership before state changes.
   - Keep transactional boundaries correct for accept flow so result persistence and suggestion updates succeed/fail together.

5. **Add or update EF Core mappings and migration**
   - If entities/tables are missing or incomplete, add/update:
     - DbSet registrations
     - entity configurations
     - indexes for tenant-scoped queries and actionable suggestion lookups
   - Recommended indexes include combinations around:
     - company/tenant ID + status
     - source entity type + source entity ID
     - target entity type + target entity ID
   - If rule breakdown is structured, map it as JSON/JSONB if that matches existing conventions.
   - Add migration only if required by the current codebase state.

6. **Handle supersession logic carefully**
   - When a suggestion is accepted, identify other suggestions involving the same reconciliation relationship or records that should no longer be actionable.
   - At minimum, supersede conflicting open suggestions for the same tenant and same participating records.
   - Keep the logic deterministic and testable.
   - Do not physically delete superseded suggestions.

7. **Add database/integration tests**
   - Add tests that verify:
     - suggestion creation persists all required fields
     - accepting a payment-to-bank suggestion creates a reconciliation result and supersedes conflicting suggestions
     - accepting an invoice-to-payment suggestion creates a reconciliation result and supersedes conflicting suggestions
     - accepting a bill-to-payment suggestion creates a reconciliation result and supersedes conflicting suggestions
     - rejecting a suggestion updates status correctly
     - default open suggestion queries exclude rejected and superseded suggestions
     - tenant scoping prevents cross-tenant reads/updates
     - created-by and updated-by metadata are persisted for create and state changes
   - Prefer real database integration tests if the project already has that pattern; otherwise use the closest existing persistence test style.

8. **Preserve consistency with existing architecture**
   - Keep business rules in domain/application services, not controllers.
   - Keep repository methods persistence-focused.
   - Follow existing naming, nullability, async, cancellation token, and result/error handling conventions.
   - Avoid introducing new abstractions unless necessary.

# Validation steps
1. Inspect and understand current reconciliation-related code before editing.
2. Build the solution:
   - `dotnet build`
3. Run relevant tests first if targeted test projects exist.
4. Run the full test suite or at minimum the affected test project:
   - `dotnet test`
5. Verify acceptance criteria explicitly:
   - suggestion record stores all required fields
   - accept persists reconciliation result and supersedes conflicting suggestions
   - reject updates status and removes from default open queries
   - tenant scoping is enforced
   - audit metadata is persisted on create and update
   - payment-to-bank, invoice-to-payment, and bill-to-payment scenarios are covered by tests
6. If migrations were added, ensure they compile and are included correctly in Infrastructure.

# Risks and follow-ups
- **Unknown existing schema**: reconciliation tables may already exist with different naming or partial fields; adapt to existing conventions rather than duplicating concepts.
- **Supersession ambiguity**: “superseded suggestions” may require more nuanced conflict detection than exact source/target matching. Implement the narrowest correct rule supported by current domain knowledge and document assumptions in code comments/tests.
- **Audit metadata conventions**: the repo may use a shared auditable base entity or interceptors; integrate with that instead of hand-rolling inconsistent fields.
- **Transaction handling**: accept flow must be atomic. If repository/service boundaries make this awkward, use the project’s existing unit-of-work or DbContext transaction pattern.
- **Polymorphic entity linking**: payment/bank/invoice/bill relationships may be represented differently across modules. Reuse existing entity type identifiers/constants to avoid mismatches.
- **Migration churn**: if the branch already contains pending reconciliation schema work, avoid conflicting migrations and update the existing model carefully.
- **Follow-up**: if not already present, a later task may need API/query DTOs and UI surfaces for actionable suggestions and reconciliation history; keep service contracts clean to support that.