# Goal
Implement backlog task **TASK-25.2.3 — Add audit metadata and tenant isolation checks to reconciliation data access paths** for story **US-25.2 Persist reconciliation suggestions and accepted reconciliation outcomes**.

Ensure reconciliation persistence paths are fully **tenant-scoped** and **auditable**, with `created_by` / `updated_by` metadata captured for all reconciliation suggestion and reconciliation result state changes, and add database tests covering create, accept, reject, and query behavior across:
- payment-to-bank
- invoice-to-payment
- bill-to-payment

# Scope
Work only on backend/domain/infrastructure/test code needed to satisfy this task.

In scope:
- Reconciliation persistence model updates for audit metadata
- Tenant isolation enforcement in reconciliation repositories / query paths
- Accept/reject state transition persistence behavior
- Superseding logic when a suggestion is accepted
- Default open suggestion query behavior excluding rejected and superseded/non-actionable suggestions
- Database/integration tests for all required reconciliation pairings
- Any EF Core configuration, migrations, and supporting domain/application changes required

Out of scope unless required by compilation:
- UI changes
- API contract redesign beyond what is necessary to pass audit/tenant context
- Broader audit event subsystem work unrelated to reconciliation persistence
- Unrelated refactors

Follow existing project conventions and architecture boundaries:
- Domain rules in Domain/Application layers
- Persistence and EF mappings in Infrastructure
- Tests in the appropriate test project(s)

# Files to touch
Inspect the solution first and then update the actual relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - reconciliation entities/value objects/enums
  - audit metadata abstractions if already present
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for create/accept/reject/query reconciliation suggestions/results
  - tenant/user context interfaces if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories / query services
  - DbContext and migrations
- `src/VirtualCompany.Api/**`
  - only if request pipeline/context wiring is required for tenant/user metadata
- `tests/VirtualCompany.Api.Tests/**` or other existing DB/integration test projects
  - reconciliation persistence tests

Also inspect:
- existing migration patterns
- existing tenant-scoped repository/query conventions
- existing auditable entity conventions (`created_at`, `updated_at`, `created_by`, `updated_by`, etc.)
- any existing reconciliation tables/entities introduced by prior tasks in US-25.2

Do not invent new top-level patterns if equivalent project conventions already exist.

# Implementation plan
1. **Discover existing reconciliation implementation**
   - Locate all reconciliation-related entities, tables, repositories, handlers, and tests.
   - Identify current models for:
     - reconciliation suggestion
     - reconciliation result/outcome
     - suggestion status
     - source/target entity references
     - open/default query behavior
   - Identify how tenant context is currently passed/enforced elsewhere in the codebase.
   - Identify how audit metadata is currently modeled elsewhere in the system.

2. **Align persistence model with acceptance criteria**
   - Ensure reconciliation suggestion persistence includes:
     - tenant/company scope identifier
     - source entity IDs
     - target entity IDs
     - match type
     - confidence score
     - rule breakdown
     - status
     - timestamps
     - created-by
     - updated-by
   - Ensure reconciliation result persistence links the relevant records when a suggestion is accepted.
   - Ensure accepted suggestions cause superseded competing suggestions to become non-actionable.
   - Ensure rejected suggestions are marked rejected and excluded from default open queries.

3. **Add/complete audit metadata**
   - Reuse existing auditable base entity/interface/pattern if present.
   - Persist `created_by` and `updated_by` for:
     - suggestion creation
     - suggestion rejection
     - suggestion acceptance / status changes
     - reconciliation result creation
     - superseded suggestion updates
   - Ensure timestamps are updated consistently with state changes.
   - If the project uses save interceptors or DbContext hooks for audit fields, integrate with that pattern rather than duplicating logic.

4. **Enforce tenant isolation in all reconciliation data access paths**
   - Review every repository/query/handler path for reconciliation records.
   - Ensure all reads and writes are scoped by tenant/company ID.
   - Prevent cross-tenant accept/reject/query behavior.
   - If methods currently fetch by record ID only, update them to require tenant/company ID and filter accordingly.
   - Apply the same rule to superseding logic so only same-tenant related suggestions are affected.

5. **Implement state transition rules**
   - On create:
     - persist suggestion with full metadata and audit fields
   - On accept:
     - persist reconciliation result
     - update accepted suggestion status appropriately
     - mark superseded overlapping suggestions as non-actionable/superseded
     - update audit metadata on all changed rows
   - On reject:
     - update suggestion status to rejected
     - ensure default open queries exclude it
     - update audit metadata
   - Preserve idempotency/guardrails where appropriate:
     - reject invalid transitions
     - avoid duplicate reconciliation results for the same accepted suggestion if project conventions require this

6. **Update EF Core mappings and migration**
   - Add any missing columns/indexes/constraints for:
     - `company_id` / tenant key
     - `created_by`
     - `updated_by`
     - status fields needed for actionable/open filtering
   - Add indexes supporting tenant-scoped open suggestion queries and result lookups.
   - Generate a migration consistent with repository conventions.
   - Keep schema naming aligned with existing migration style.

7. **Add database tests**
   - Create integration/database tests that verify, for each pairing:
     - payment-to-bank
     - invoice-to-payment
     - bill-to-payment
   - Cover:
     - create suggestion persists required fields
     - accept creates reconciliation result and supersedes overlapping suggestions
     - reject updates status and excludes from default open queries
     - tenant isolation prevents cross-tenant visibility/modification
     - audit metadata is persisted and updated correctly
   - Prefer parameterized tests if the existing test style supports it.

8. **Keep changes minimal and consistent**
   - Avoid broad refactors.
   - Reuse existing enums, result types, exception patterns, and test fixtures.
   - If you must introduce a new abstraction, keep it narrowly scoped and justified by existing architecture.

9. **Document assumptions in code comments only where necessary**
   - Do not add noisy comments.
   - If superseding logic requires a specific overlap rule, make it explicit in code/tests.

# Validation steps
Run the relevant commands and ensure all touched code compiles and tests pass.

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test filters for reconciliation/infrastructure DB tests, run those as well.

4. Verify manually in tests or assertions that:
   - suggestion records persist all required fields
   - accepted suggestions create reconciliation results
   - superseded suggestions are no longer actionable
   - rejected suggestions are excluded from default open queries
   - all reconciliation records are tenant-scoped
   - `created_by` and `updated_by` are persisted for all state changes
   - behavior is covered for payment-to-bank, invoice-to-payment, and bill-to-payment

5. Include in your final implementation summary:
   - files changed
   - migration name
   - how tenant isolation is enforced
   - how audit metadata is populated
   - what tests were added

# Risks and follow-ups
- Existing reconciliation work may already partially implement these behaviors; avoid duplicating concepts or creating conflicting status models.
- Audit metadata may already be centrally managed; bypassing that pattern could create inconsistent behavior.
- Tenant isolation bugs often hide in “get by id” repository methods and superseding queries; review all indirect access paths carefully.
- Superseding logic can be ambiguous if overlap rules are not explicit; encode the intended rule in tests.
- If acceptance currently lacks transactional handling, create/update/supersede operations may need to be wrapped in one transaction.
- If migrations are archived or generated differently in this repo, follow the established process exactly.
- If API/user context does not currently expose actor identity to the application layer, a small follow-up may be needed to standardize actor propagation for auditable writes.