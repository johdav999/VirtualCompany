# Goal
Implement backlog task **TASK-23.3.2 — Add reporting lock state, audit fields, and authorization checks to period reporting workflows** for story **US-23.3 Support period-end close checks and reporting lock controls**.

Deliver a complete vertical slice in the existing .NET modular monolith so that:

- fiscal period close validation returns blocking issues for:
  - unposted source documents
  - unbalanced journal entries
  - missing statement mappings
- a reporting lock can be applied to a **closed** fiscal period
- locked periods prevent regeneration of stored statement outputs
- an authorized unlock operation is required before regeneration can proceed
- audit trail records who performed:
  - close validation
  - reporting lock
  - reporting unlock
  - with timestamps and tenant-scoped actor metadata
- API and background job execution paths both enforce the lock consistently
- automated tests cover API behavior, authorization/state failures, and worker/job enforcement

Use existing architecture and conventions in the repo. Prefer minimal, cohesive changes over speculative abstractions, but ensure the design is extensible for future period-end controls.

# Scope
Include:

1. **Domain model updates**
   - Add reporting lock state and audit fields to the fiscal period / reporting workflow aggregate or entity already responsible for period-end reporting.
   - Add any supporting value objects / enums for:
     - lock state
     - validation issue type / severity
     - operation audit metadata if needed

2. **Application layer**
   - Add command/query handlers or service methods for:
     - close validation
     - apply reporting lock
     - unlock reporting lock
     - guarded report regeneration
   - Enforce authorization and state transitions in application/domain logic, not only controllers.

3. **API endpoints**
   - Expose/extend endpoints for:
     - period close validation
     - lock reporting for a closed period
     - unlock reporting for authorized users
     - regenerate statement/report output
   - Return consistent error responses for locked-period regeneration attempts.

4. **Audit trail**
   - Persist business audit events for validation, lock, unlock, and denied regeneration attempts where appropriate.
   - Include actor, action, target, outcome, timestamp, and concise rationale/summary.

5. **Background job enforcement**
   - Ensure any background worker/job path that regenerates stored statement outputs checks the same lock state and fails safely/consistently.

6. **Persistence**
   - Add EF Core/entity configuration and migration(s) for new fields/tables as needed.

7. **Tests**
   - Add/extend automated tests for:
     - validation blocking issues
     - lock/unlock transitions
     - authorization failures
     - regeneration blocked while locked
     - background execution path enforcement
     - audit event creation

Out of scope unless required by existing code patterns:
- broad UI work beyond API contract compatibility
- redesigning the entire accounting/reporting model
- introducing a new authorization framework if one already exists
- unrelated refactors

# Files to touch
Inspect the solution first, then update the actual relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - fiscal period / reporting entities
  - enums / domain services / exceptions
- `src/VirtualCompany.Application/**`
  - commands, queries, handlers, DTOs
  - authorization/policy checks
  - audit integration
  - background job orchestration interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories
  - migrations
  - background worker/job implementations
  - audit persistence
- `src/VirtualCompany.Api/**`
  - controllers or minimal API endpoint mappings
  - request/response contracts if API-owned
  - error mapping
- `src/VirtualCompany.Shared/**`
  - shared contracts/error models if used across layers
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
  - worker-path enforcement tests if hosted here

Also inspect:
- `README.md`
- any architecture or migration guidance under `docs/**`

If there is an existing accounting/reporting module, stay within its boundaries and naming conventions.

# Implementation plan
1. **Discover existing reporting/period-end model**
   - Find current entities and endpoints for:
     - fiscal periods
     - period close
     - statement generation/regeneration
     - audit events
     - authorization policies/roles
   - Identify whether regeneration currently happens:
     - synchronously via API
     - asynchronously via background jobs
     - both
   - Identify the canonical entity that should own reporting lock state.

2. **Design the domain changes**
   - Add fields to the fiscal period/reporting entity, such as:
     - `ReportingLockState` or `IsReportingLocked`
     - `ReportingLockedAtUtc`
     - `ReportingLockedByUserId`
     - `ReportingUnlockedAtUtc`
     - `ReportingUnlockedByUserId`
     - optionally `LastCloseValidatedAtUtc`
     - optionally `LastCloseValidatedByUserId`
   - Prefer explicit fields over opaque JSON.
   - Add domain methods with invariant enforcement, e.g.:
     - `ValidateClose(...)`
     - `ApplyReportingLock(...)`
     - `UnlockReporting(...)`
     - `EnsureReportingRegenerationAllowed(...)`
   - Enforce:
     - only closed periods can be locked
     - locked periods cannot regenerate outputs
     - unlock must be explicit
   - If the domain already uses result/error objects, follow that pattern instead of throwing ad hoc exceptions.

3. **Implement close validation logic**
   - Add a validation service/handler that returns blocking issues for the target period.
   - Ensure it checks at minimum:
     - unposted source documents in the period
     - unbalanced journal entries in the period
     - missing statement mappings required for stored statement outputs
   - Return a structured response with:
     - period id
     - validation timestamp
     - blocking issues collection
     - issue code/type/message
   - Record an audit event for validation execution, including whether blocking issues were found.

4. **Implement authorization checks**
   - Reuse existing ASP.NET Core policy-based authorization or application-layer authorization patterns.
   - Define or reuse permissions for:
     - validating period close
     - locking reporting
     - unlocking reporting
     - regenerating reports/statements
   - Ensure unlock is more restrictive if the system distinguishes elevated finance/admin roles.
   - Do not rely solely on endpoint attributes; application handlers must also enforce authorization for worker-triggered or internal execution paths.

5. **Guard regeneration in one canonical place**
   - Identify the service/handler/job entry point used to regenerate stored statement outputs.
   - Add a single shared guard that checks:
     - tenant scope
     - period existence
     - period state
     - reporting lock state
     - caller authorization/context
   - Make both API-triggered and background-triggered regeneration use this same guard.
   - Return/map a consistent error for locked periods, such as:
     - domain/application error code like `reporting_period_locked`
     - HTTP `409 Conflict` or `403 Forbidden` depending existing API conventions
   - Follow existing error contract conventions in the repo; consistency matters more than a specific status code.

6. **Persist audit events**
   - Use the existing audit trail mechanism.
   - Add audit events for:
     - close validation executed
     - reporting lock applied
     - reporting unlock applied
     - regeneration denied due to lock
   - Include:
     - `company_id`
     - actor type/id
     - action
     - target type/id
     - outcome
     - rationale/summary
     - timestamp
   - If data source references are already supported, include relevant references for validation findings.

7. **Update persistence and migrations**
   - Add EF model/configuration updates for new columns.
   - Create a migration with clear names for lock/audit fields.
   - Ensure nullable handling is correct for historical rows.
   - If statement mappings or validation dependencies are modeled elsewhere, avoid schema churn unless necessary.

8. **Expose/extend API endpoints**
   - Add or update endpoints along these lines, matching existing route conventions:
     - `POST /.../periods/{id}/close-validation`
     - `POST /.../periods/{id}/reporting-lock`
     - `POST /.../periods/{id}/reporting-unlock`
     - `POST /.../periods/{id}/reports/regenerate`
   - Request/response payloads should be explicit and stable.
   - Include lock state and audit metadata in responses where useful.

9. **Handle background jobs**
   - Update the worker/job that regenerates stored statement outputs so it:
     - loads the period in tenant scope
     - checks reporting lock before work starts
     - fails deterministically with the same application/domain error semantics
     - does not silently bypass lock state
   - If jobs are retried automatically, ensure lock-state failures are treated as business/state failures, not transient infrastructure failures.

10. **Add tests**
   - Add integration tests for:
     - validation endpoint returns blocking issues for each required condition
     - lock can only be applied to closed periods
     - locked period regeneration returns the expected consistent error
     - unlock requires authorization
     - unlock allows regeneration afterward
     - audit events are written for validation/lock/unlock
   - Add tests covering background execution path:
     - queued/scheduled regeneration for locked period is blocked
     - no stored output is regenerated while locked
   - Prefer existing test fixtures and helpers over custom harnesses.

11. **Keep implementation clean**
   - Avoid duplicating lock checks in multiple places without a shared method/service.
   - Avoid embedding business rules in controllers.
   - Keep naming aligned with existing accounting/reporting terminology in the codebase.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the relevant automated tests:
   - `dotnet test`

3. If migrations are part of normal workflow, generate/apply/verify the new migration using the repo’s established process.

4. Manually verify behavior through tests or local API execution:
   - close validation returns blocking issues when:
     - unposted source documents exist
     - unbalanced journal entries exist
     - statement mappings are missing
   - locking a non-closed period fails
   - locking a closed period succeeds and records audit metadata
   - regeneration on a locked period fails with the expected consistent error response
   - authorized unlock succeeds and records audit metadata
   - regeneration succeeds after unlock if no other blockers exist
   - background regeneration path also fails while locked

5. Confirm audit records exist and contain:
   - actor
   - action
   - target
   - outcome
   - timestamp
   - concise summary

# Risks and follow-ups
- **Unknown existing model shape:** The repo may already have partial period-close/reporting concepts under different names. Adapt to existing terminology rather than forcing new abstractions.
- **Authorization ambiguity:** If finance-specific roles/permissions are not yet formalized, implement the narrowest safe policy consistent with current auth patterns and note any gaps.
- **Error contract consistency:** Existing APIs may already standardize business/state errors. Reuse that format exactly to avoid breaking clients.
- **Background job coupling:** Regeneration may be triggered from multiple paths. Ensure all paths converge on the same guard or document any remaining bypass risk.
- **Migration compatibility:** Historical periods will not have lock/audit values; ensure null-safe reads and backward-compatible defaults.
- **Validation query cost:** Checks for unposted documents, unbalanced entries, and missing mappings may be expensive. Keep queries scoped and efficient, but prioritize correctness first.
- **Follow-up suggestion:** If not already present, consider a future task to expose lock state and audit history in reporting/period detail views in the web app.