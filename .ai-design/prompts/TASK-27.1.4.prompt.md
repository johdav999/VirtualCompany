# Goal
Implement backlog task **TASK-27.1.4 — Build idempotent backfill job for existing company approval tasks from seeded finance data** for story **US-27.1 Implement finance approval workflow state and pending approval APIs**.

The coding agent should deliver the approval backfill portion in a way that fits the existing .NET modular monolith, PostgreSQL persistence, tenant-scoped finance data, and background job patterns.

Primary outcome:
- Add an **idempotent backfill job** that scans existing seeded/mock finance records and creates missing approval tasks for records that meet approval rule criteria.
- Ensure **repeated runs do not create duplicates**.
- Preserve compatibility so **existing bill records remain valid even if no approval task exists**.

This task must align with the broader acceptance criteria, especially:
- approval workflow supports target types `bill`, `payment`, and `exception`
- workflow states include `pending`, `approved`, `rejected`, and `escalated`
- pending approvals API only returns tenant-scoped `pending` and `escalated` items
- migration/indexing support exists or is extended as needed for approval task querying and backfill lookup efficiency

# Scope
In scope:
- Inspect current finance seed/mock data model and approval workflow implementation status.
- Add or extend domain/application/infrastructure support for a **backfill use case** over existing bills and, if already modeled, payments.
- Implement a **tenant-safe, idempotent background job/service/command** that:
  - enumerates existing seeded finance records
  - evaluates approval rule criteria
  - creates approval tasks only when required
  - skips records that already have an approval task for the same target
- Add duplicate-prevention protections at both:
  - application logic level
  - database level where appropriate and safe
- Add tests proving:
  - qualifying existing mock bills get approval tasks
  - non-qualifying records do not
  - rerunning the backfill does not create duplicates
  - tenant scoping is preserved
  - bills without approval tasks remain valid after migration/backfill

Out of scope unless required by existing code coupling:
- Building the full approval UI
- Reworking unrelated workflow orchestration
- Introducing a new job framework if one already exists
- Large refactors outside approval/finance modules
- Expanding beyond seeded/mock finance data unless the current implementation already generalizes naturally

# Files to touch
Inspect first, then update the minimal correct set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - approval workflow entities/enums/value objects
  - finance bill/payment entities if approval linkage is modeled there

- `src/VirtualCompany.Application/**`
  - approval rule evaluation service
  - backfill command/job handler
  - DTOs/query models if needed
  - interfaces for repositories/background execution

- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/mappings
  - repositories/query implementations
  - migration(s) for approval workflow tables/indexes/unique constraints if not already present
  - background worker/job registration
  - seed-data-aware finance queries

- `src/VirtualCompany.Api/**`
  - DI registration for the backfill job
  - optional admin/internal trigger endpoint or startup hook only if the project already uses that pattern

- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for backfill behavior
  - pending approvals API tests if impacted
  - migration/idempotency/tenant-scope tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration conventions and execution expectations.

# Implementation plan
1. **Discover existing approval and finance implementation**
   - Search for:
     - approval entities/tables/endpoints
     - finance bill/payment seed data
     - background jobs/hosted services
     - migration conventions
     - tenant resolution patterns
   - Identify whether approval tasks already exist as:
     - a dedicated approval table
     - workflow/task records
     - a finance-specific approval entity
   - Reuse the existing model rather than inventing a parallel one.

2. **Confirm the canonical uniqueness rule for approval tasks**
   - Define the duplicate boundary clearly.
   - Preferred uniqueness for backfilled approval tasks:
     - `company_id + target_type + target_id`
     - possibly filtered to active/current approval task if the model allows historical multiple approvals
   - If the system needs historical re-approval cycles, do **not** block legitimate future records; instead use a uniqueness rule that matches the current active approval request/task concept.
   - Add a DB constraint/index only if it matches the domain model safely.

3. **Implement approval eligibility evaluation for existing finance records**
   - Reuse the same threshold/rule evaluation used for new bill/payment creation if it already exists.
   - If no shared evaluator exists, extract one so both:
     - real-time creation flow
     - backfill flow
     use the same logic.
   - Support target types required by the story model:
     - `bill`
     - `payment`
     - `exception`
   - For this task, ensure at minimum seeded/mock **bills** are backfilled correctly, and include payments too if already present in seeded finance data.

4. **Build the idempotent backfill job**
   - Create an application service/command/job such as:
     - `BackfillFinanceApprovalTasksJob`
     - `BackfillExistingFinanceApprovalsCommand`
   - Behavior:
     - iterate tenant-scoped finance records from seeded/mock data
     - evaluate whether each record requires approval
     - check for an existing approval task/request for the same target
     - create missing approval task/request with correct:
       - company/tenant id
       - target type
       - target id
       - assignee if derivable from rule/config
       - due date if part of current workflow rules
       - status `pending` initially, or `escalated` only if existing logic dictates
   - Make it safe for repeated execution:
     - pre-check existence
     - handle race/duplicate insert gracefully if a unique constraint exists
   - If the app has a background worker pattern, register it there.
   - If no scheduler exists yet, expose it as an internal application command and wire only the minimal invocation path already used in the codebase.

5. **Preserve backward compatibility for existing bills**
   - Ensure bill reads and existing finance APIs do not assume an approval task always exists.
   - Any joins or projections must tolerate null/missing approval rows.
   - Avoid adding non-null foreign keys from bills to approvals unless already established and backfilled safely.

6. **Add or update persistence and indexes**
   - Verify migration coverage for:
     - approval workflow tables/fields
     - indexes on `companyId`, `assigneeId`, `status`, `dueDate`
   - Add lookup support for backfill and pending approvals query performance.
   - If needed, add a uniqueness/index strategy for idempotency.
   - Follow repository migration conventions in the workspace.

7. **Ensure pending approvals query remains correct**
   - Confirm `GET /api/finance/approvals/pending` returns only tenant-scoped items with status:
     - `pending`
     - `escalated`
   - Verify backfilled records appear there with required fields:
     - target type
     - target id
     - assignee
     - due date
     - status
   - Do not broaden results to approved/rejected items.

8. **Testing**
   - Add integration tests covering:
     - seeded/mock bill above threshold => approval task created
     - seeded/mock bill below threshold => no approval task created
     - rerun backfill => still one approval task only
     - cross-tenant data isolation => one tenant’s backfill does not create/read another tenant’s approvals
     - existing bill with no approval task remains queryable/valid
     - pending approvals endpoint includes backfilled pending/escalated items only
   - Prefer integration tests over pure unit tests where persistence/idempotency is involved.

9. **Implementation quality constraints**
   - Keep logic deterministic and side-effect aware.
   - Use cancellation tokens and async APIs.
   - Log useful operational events for:
     - records scanned
     - records matched
     - records created
     - duplicates skipped
   - Do not leak tenant data in logs.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly using the project’s existing migration workflow.

4. Manually validate behavior through tests or local execution:
   - seed/mock finance data exists for at least one company
   - run the backfill job once
   - confirm approval tasks are created for qualifying existing bills
   - run the backfill job again
   - confirm no duplicate approval tasks are created

5. Validate pending approvals API behavior:
   - call `GET /api/finance/approvals/pending`
   - confirm only tenant-scoped `pending` and `escalated` tasks are returned
   - confirm backfilled items include target type, target id, assignee, due date, and status

6. Validate backward compatibility:
   - query/read existing bills that do not qualify for approval
   - confirm they remain valid and no null-reference/foreign-key assumptions break reads

# Risks and follow-ups
- **Domain-model ambiguity:** The codebase may model “approval tasks”, “approval requests”, and “workflow tasks” differently. Reuse the existing canonical concept instead of creating a duplicate abstraction.
- **Uniqueness design risk:** A hard unique constraint on `company + targetType + targetId` may be too strict if the domain allows multiple approval cycles over time. If so, scope uniqueness to the active/current approval record only.
- **Seed data assumptions:** Mock finance data shape may differ from production-oriented entities. Keep the backfill generic enough to work on persisted finance records, not only hardcoded seed identifiers.
- **Assignee resolution gaps:** If approval rules do not yet fully resolve assignees, use the current rule engine/default approver mechanism rather than inventing fallback behavior silently.
- **Concurrency risk:** If the backfill can run in parallel across workers, add duplicate-safe persistence handling and, if available, distributed coordination.
- **Migration coupling:** If approval schema work is incomplete, this task may need to be implemented on top of the migration/index work from the same story before the backfill can be completed.
- **Follow-up recommendation:** Add metrics/admin observability for backfill runs, including counts of scanned, matched, created, and skipped records.