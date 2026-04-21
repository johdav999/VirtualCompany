# Goal
Implement backlog task **TASK-27.1.1 — Add ApprovalTask entity, workflow enums, and EF Core migration with tenant-scoped indexes** for story **US-27.1 Implement finance approval workflow state and pending approval APIs**.

Deliver the foundational domain, persistence, and migration work needed to support finance approval workflow state for bills, payments, and exceptions, while preserving tenant isolation and backward compatibility for existing bill data.

# Scope
In scope:
- Add a new approval-task domain entity for finance approvals.
- Add workflow enums/value objects for:
  - approval target types: `bill`, `payment`, `exception`
  - workflow states: `pending`, `approved`, `rejected`, `escalated`
- Map the entity in EF Core and generate a PostgreSQL migration.
- Ensure the schema includes tenant-scoped indexes covering:
  - `company_id`
  - `assignee_id`
  - `status`
  - `due_date`
- Ensure existing bill records remain valid when no approval task exists.
- Add the minimum persistence/model hooks needed so later application logic can:
  - auto-create pending approval tasks from threshold rules
  - query tenant-scoped pending/escalated approvals
  - backfill approval tasks idempotently for existing mock bills

Out of scope unless required to make the build pass:
- Full approval decision UI
- Full API implementation beyond any minimal DTO/query scaffolding already tightly coupled to this task
- Notification fan-out
- Complete background worker orchestration
- Broad refactors unrelated to approval workflow persistence

Important acceptance constraints to preserve:
- Multi-tenant shared-schema design with `company_id` enforcement.
- Existing bills must not require an approval task row.
- Backfill behavior must be designed to avoid duplicates on repeated runs.
- Pending approvals API will need to return only tenant-scoped `pending` and `escalated` tasks with target metadata, assignee, due date, and status.

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add `ApprovalTask` entity
  - add approval workflow enums
  - add any shared constants/value objects if the domain style uses them

- `src/VirtualCompany.Infrastructure/**`
  - `Persistence` / `DbContext` / entity configuration files
  - EF Core model builder registration
  - migration files under the infrastructure migrations folder
  - any repository/query model wiring if present

- `src/VirtualCompany.Application/**`
  - only if needed for compile-safe contracts or query models related to pending approvals/backfill hooks

- `src/VirtualCompany.Api/**`
  - only if compile requires registration changes

- `tests/**`
  - add or update tests for entity mapping, migration assumptions, and idempotent uniqueness behavior if test patterns already exist

Also inspect:
- existing bill/payment entities and configurations
- tenant-scoped query patterns
- enum persistence conventions
- migration naming conventions
- any existing finance mock seed/backfill jobs

# Implementation plan
1. **Discover current architecture and conventions**
   - Inspect the domain model for existing finance entities such as bills, payments, exceptions, approvals, tasks, and companies.
   - Inspect the EF Core `DbContext`, entity type configurations, and migration conventions.
   - Determine whether the codebase uses:
     - enum-to-string persistence
     - strongly typed IDs
     - base entity classes with audit fields
     - separate configuration classes per entity
     - snake_case PostgreSQL naming conventions

2. **Design the `ApprovalTask` entity**
   - Add a new tenant-owned entity representing a finance approval work item.
   - Include fields needed by current and near-term acceptance criteria:
     - `Id`
     - `CompanyId`
     - `TargetType`
     - `TargetId`
     - `AssigneeId` nullable if assignment can happen later
     - `Status`
     - `DueDate` nullable or required based on existing workflow conventions
     - timestamps such as `CreatedAt`, `UpdatedAt`
     - optional metadata needed for threshold/backfill safety, such as source/rule reference, if the existing model supports it
   - Keep the relationship from bill/payment to approval task optional. Do **not** add a required FK from bills to approval tasks.

3. **Add workflow enums**
   - Add enums for:
     - `ApprovalTargetType`: `Bill`, `Payment`, `Exception`
     - `ApprovalTaskStatus`: `Pending`, `Approved`, `Rejected`, `Escalated`
   - Persist them using the project’s established convention. Prefer string storage if that is already the standard for readability and migration safety.

4. **Model duplicate-prevention support**
   - Since acceptance requires idempotent backfill with no duplicates on repeated runs, design the schema so duplicate active tasks for the same tenant/target are prevented.
   - Prefer one of these approaches based on existing conventions:
     - unique index on `(company_id, target_type, target_id)` if only one approval task per target is allowed
     - or a more explicit uniqueness key including rule/source if multiple rule-generated tasks are possible
   - If business ambiguity exists, choose the simplest model that satisfies the acceptance criteria and document it in code comments or migration notes.

5. **Add EF Core configuration**
   - Create/update entity configuration for `ApprovalTask`.
   - Configure:
     - table name
     - primary key
     - enum conversions
     - required/optional columns
     - max lengths where appropriate
     - tenant-aware indexes
   - Add indexes explicitly for:
     - `company_id`
     - `assignee_id`
     - `status`
     - `due_date`
   - Prefer composite indexes that reflect tenant-scoped query patterns, especially for pending approvals, for example:
     - `(company_id, status, due_date)`
     - `(company_id, assignee_id, status)`
   - If acceptance is interpreted literally, also include single-column indexes where needed, but avoid redundant over-indexing unless necessary. Favor indexes that support real tenant-scoped queries.

6. **Update the DbContext**
   - Register the new `DbSet<ApprovalTask>`.
   - Ensure model configuration is applied.
   - Verify no existing bill mappings are broken by the new entity.

7. **Create EF Core migration**
   - Generate a migration with a clear name, e.g. `AddApprovalTasksWorkflowState`.
   - Ensure PostgreSQL column names/types align with project conventions.
   - Migration should:
     - create the approval task table
     - create required indexes
     - add any needed nullable fields only if truly required by the task
   - Verify the migration is safe for existing data and does not invalidate current bill rows.

8. **Prepare for threshold-based creation and pending approvals query**
   - If there is already a finance approval rule service or workflow hook, add the minimal persistence-facing contract needed for future automatic creation.
   - If there is already a pending approvals query handler or API contract, align the entity fields so it can return:
     - target type
     - target id
     - assignee
     - due date
     - status
   - Do not overbuild full business logic if it belongs to a later task, but avoid schema gaps that would block it.

9. **Backfill readiness**
   - If a mock-bill backfill job already exists, update it to use the new entity and enforce idempotency.
   - If it does not exist yet, add the minimal infrastructure seam or TODO-safe placeholder only if needed by compile/tests.
   - Ensure repeated runs do not create duplicates, ideally enforced both:
     - in application logic
     - and by a database uniqueness constraint/index

10. **Add/adjust tests**
   - Add tests matching existing patterns for:
     - enum mapping
     - entity configuration
     - optional relationship behavior for bills without approval tasks
     - duplicate-prevention behavior
     - tenant-scoped pending/escalated query assumptions if query tests already exist

11. **Document assumptions in code**
   - If you must make a business-rule choice not fully specified, keep it narrow and explicit:
     - one approval task per tenant target
     - assignee may be nullable until routing occurs
     - due date may be nullable unless existing workflow standards require it

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify the migration compiles and is included in the infrastructure project.

4. Inspect the generated migration to confirm:
   - approval task table is created
   - enum columns are mapped correctly
   - indexes exist for `company_id`, `assignee_id`, `status`, and `due_date`
   - any uniqueness/index strategy for idempotent backfill is present
   - no non-null constraint was added that would invalidate existing bill records

5. If the repo supports local database update, validate migration application:
   - run the project’s normal EF database update command if available
   - confirm schema shape in PostgreSQL

6. Validate query-readiness manually in code:
   - pending approvals can be filtered by `company_id`
   - status filter supports `Pending` and `Escalated`
   - projection fields exist for target type/id, assignee, due date, and status

7. Validate backward compatibility:
   - existing bill entities/configurations still allow bills with no approval task
   - no required navigation/property was introduced on bills

8. Validate idempotency design:
   - confirm repeated backfill attempts for the same tenant target would not create duplicate approval tasks

# Risks and follow-ups
- **Ambiguity around uniqueness semantics:** If multiple approval tasks per target may be needed later, a simple unique index on `(company_id, target_type, target_id)` could be too restrictive. For this task, prefer the simplest idempotent model unless the existing domain already supports rule/version-specific approvals.
- **Enum storage mismatch:** If the project currently stores enums as ints, introducing string enums inconsistently could cause maintenance issues. Follow existing conventions.
- **Index over/under-design:** Acceptance asks for indexes on several fields, but tenant-scoped queries are best served by composite indexes. Balance literal acceptance with practical query performance.
- **Backfill job may belong to a later task:** If full backfill implementation is not yet in this slice, at minimum ensure the schema and uniqueness constraints support it cleanly.
- **Pending approvals API likely needs a follow-up task:** This task should enable it at the persistence level, but endpoint/query implementation may still need separate application/API work.
- **Approval rule automation may need follow-up integration:** Threshold-based creation likely depends on finance rule configuration and bill/payment workflows not fully covered here. Ensure the entity shape supports that next step without migration churn.