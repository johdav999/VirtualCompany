# Goal
Implement backlog task **TASK-23.3.1 — Implement period-end validation service for reporting readiness checks** for story **US-23.3 Support period-end close checks and reporting lock controls**.

Deliver a production-ready vertical slice in the existing .NET modular monolith that:

- Adds a **period close validation service and endpoint**
- Detects and returns **blocking issues** for:
  - unposted source documents
  - unbalanced journal entries
  - missing statement mappings
- Adds **reporting lock state** for a closed fiscal period
- Prevents **stored statement regeneration** for locked periods unless an authorized unlock occurs
- Records **audit trail entries** for validation, lock, and unlock operations with actor and timestamp
- Enforces lock behavior across both **API** and **background job** execution paths
- Includes automated tests covering endpoint behavior, authorization/state failures, and worker/job enforcement

Use existing architectural patterns in the repo where possible:
- CQRS-lite application layer
- ASP.NET Core API endpoints/controllers
- PostgreSQL persistence via Infrastructure
- tenant-scoped authorization and data access
- business audit events as domain/application behavior, not just logs

# Scope
In scope:

1. **Domain model changes**
   - Represent fiscal/reporting period close and reporting lock state
   - Represent validation result/blocking issue types
   - Represent authorized unlock semantics if not already present

2. **Application services / commands / queries**
   - Validate period-end readiness
   - Apply reporting lock
   - Remove reporting lock
   - Guard report regeneration requests/jobs against lock state

3. **API surface**
   - Endpoint to run period close validation and return blocking issues
   - Endpoint to lock reporting for a closed period
   - Endpoint to unlock reporting for authorized users
   - Consistent error response when regeneration is attempted for a locked period

4. **Audit trail**
   - Persist audit events for:
     - close validation executed
     - reporting lock applied
     - reporting lock removed
   - Include actor, target period, outcome, timestamp, and concise rationale/details

5. **Background execution enforcement**
   - Ensure any report regeneration background path checks lock state before execution
   - Return/fail consistently with the same business rule semantics used by API path

6. **Tests**
   - Application/API tests for validation issues
   - Tests for lock/unlock authorization and state transitions
   - Tests proving locked periods block regeneration in both synchronous/API and background job paths

Out of scope unless required by existing code structure:
- Broad UI work in Blazor/MAUI
- New workflow engine features unrelated to period close
- Reworking accounting/report generation architecture beyond necessary lock enforcement
- Large schema redesigns outside the minimum needed for this task

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

1. **Domain**
   - Fiscal period / reporting period entity or aggregate
   - Report generation domain service or policy
   - Lock state/value objects/enums
   - Validation issue types/enums/constants

2. **Application**
   - `ValidatePeriodClose...` command/query + handler
   - `LockReportingPeriod...` command + handler
   - `UnlockReportingPeriod...` command + handler
   - DTOs for validation response and blocking issues
   - Authorization/policy checks
   - Audit event creation
   - Shared guard/service for “can regenerate report for period?”

3. **Infrastructure**
   - EF Core entity configuration / repository updates
   - Migration for period lock fields/table and any audit persistence additions
   - Query implementations for:
     - unposted source documents
     - unbalanced journal entries
     - missing statement mappings
   - Background job enforcement hook

4. **API**
   - Endpoints/controllers for validate/lock/unlock
   - Error mapping for locked-period regeneration attempts
   - Consistent problem details/state error response

5. **Tests**
   - Endpoint tests
   - Authorization tests
   - Background job enforcement tests
   - Audit persistence assertions where practical

If the repo already has accounting/reporting modules with different naming, follow existing conventions instead of inventing new structure.

# Implementation plan
1. **Discover existing accounting/reporting model**
   - Find current entities/endpoints for:
     - fiscal periods
     - source documents
     - journal entries
     - statement mappings
     - stored statement/report generation
     - audit events
     - background jobs/workers
   - Identify current authorization approach and error response conventions.
   - Reuse existing period status concepts if “closed” already exists.

2. **Design minimal domain additions**
   - Add reporting lock state to the fiscal/reporting period model, or create a dedicated lock record if that better matches current design.
   - Capture:
     - `IsReportingLocked`
     - `ReportingLockedAt`
     - `ReportingLockedBy`
     - `ReportingUnlockedAt`
     - `ReportingUnlockedBy`
   - If current model prefers immutable audit-only transitions, adapt accordingly.
   - Add a validation issue model with stable machine-readable codes, e.g.:
     - `unposted_source_documents`
     - `unbalanced_journal_entries`
     - `missing_statement_mappings`

3. **Implement period-end validation service**
   - Create an application service/handler that accepts tenant/company context and period identifier.
   - Validate the period exists and is tenant-scoped.
   - Query for blocking issues:
     - source documents in the period not posted
     - journal entries in the period whose debits/credits do not balance
     - accounts/statement lines required for reporting but missing mappings
   - Return a response containing:
     - period id
     - validation timestamp
     - executed by actor
     - `isReadyToClose`
     - collection of blocking issues with code, message, count, and sample references if available
   - Persist an audit event for validation execution regardless of pass/fail outcome.

4. **Implement lock command**
   - Add command/endpoint to apply reporting lock.
   - Enforce preconditions:
     - period exists
     - tenant-scoped
     - period is closed
     - caller is authorized
     - lock is not already applied, or handle idempotently per existing conventions
   - Persist lock state and audit event.
   - Return updated lock status.

5. **Implement unlock command**
   - Add command/endpoint to remove reporting lock.
   - Enforce:
     - period exists
     - tenant-scoped
     - caller has elevated/explicit authorization for unlock
   - Decide based on existing auth model whether unlock requires owner/admin/finance approver or a dedicated permission.
   - Persist unlock state and audit event.
   - Return updated lock status.

6. **Enforce lock on report regeneration**
   - Identify all report regeneration entry points:
     - API-triggered regeneration
     - background worker/job-triggered regeneration
   - Centralize the check in a shared application/domain policy service to avoid drift.
   - If period is locked:
     - block regeneration
     - return/throw a consistent business exception/state exception
     - map API response to a consistent authorization-or-state error shape
   - Ensure background jobs fail/skip in a deterministic, testable way and do not regenerate artifacts.

7. **Audit trail integration**
   - Use existing `audit_events` persistence model.
   - Record for each operation:
     - actor type/id
     - action
     - target type/id
     - outcome
     - timestamp
     - rationale/details in structured metadata if supported
   - Suggested actions:
     - `period_close_validation_executed`
     - `reporting_lock_applied`
     - `reporting_lock_removed`
     - optionally `report_regeneration_blocked_by_lock`

8. **API contract and error consistency**
   - Expose endpoint(s) using existing routing conventions.
   - Validation endpoint should return blocking issues in a stable response contract.
   - Locked regeneration attempts should return a consistent error response:
     - likely `409 Conflict` for invalid state, unless repo conventions use `403 Forbidden`
     - prefer existing problem details/error envelope conventions over inventing a new one
   - Keep machine-readable error code stable, e.g. `reporting_period_locked`.

9. **Persistence and migration**
   - Add EF migration or equivalent schema change for lock state storage.
   - Ensure tenant-safe indexes/constraints if needed.
   - If audit table already exists, reuse it without schema changes unless required.

10. **Automated tests**
   - Add tests for validation endpoint:
     - returns blocking issues for each acceptance criterion condition
     - returns ready/no issues when none exist
   - Add tests for lock/unlock:
     - lock allowed only on closed period
     - unlock requires authorization
     - audit events recorded
   - Add tests for regeneration blocking:
     - API path blocked when locked
     - background job path blocked when locked
     - unlocked period allows regeneration again
   - Add multi-tenant scoping tests if similar tests already exist nearby.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the full relevant test suite:
   - `dotnet test`

3. Verify API behavior with automated tests covering:
   - validation endpoint returns blocking issues for:
     - unposted source documents
     - unbalanced journal entries
     - missing statement mappings
   - lock endpoint rejects non-closed periods
   - unlock endpoint rejects unauthorized callers
   - regeneration of locked period reports returns the expected consistent error response

4. Verify persistence behavior:
   - migration applies cleanly
   - lock/unlock state persists correctly
   - audit events are written with actor and timestamp

5. Verify background enforcement:
   - queued/simulated report regeneration job for locked period is blocked
   - no regenerated stored statement output is produced while locked
   - after authorized unlock, regeneration succeeds

6. Confirm no tenant isolation regressions:
   - period operations cannot access another tenant’s records
   - audit events remain tenant-scoped

# Risks and follow-ups
- **Unknown existing accounting model**: fiscal periods, source docs, mappings, and report generation may already exist under different names. Adapt to existing module boundaries rather than forcing new abstractions.
- **Authorization ambiguity**: acceptance criteria says “authorized unlock operation” but does not define the exact role/permission. Reuse existing finance/admin authorization patterns and document the chosen rule in code/tests.
- **Error semantics ambiguity**: “authorization or state error response” could map to `403` or `409`. Prefer the repo’s established API error convention and keep the machine-readable error code consistent.
- **Background job architecture variance**: if workers are thin wrappers over application services, enforce lock in the shared service. Avoid duplicating checks in controller and worker separately.
- **Statement mapping definition may be domain-specific**: determine what constitutes a required mapping from existing reporting configuration; if missing, implement the narrowest rule that satisfies current reporting model and acceptance criteria.
- **Audit schema may be incomplete in code vs architecture doc**: if audit persistence is partial, implement the minimum viable business audit event path without broad audit subsystem expansion.

Follow-ups to note in code comments or task notes if not fully addressed here:
- add dedicated permission/role for reporting unlock if current auth is too coarse
- expose lock status in period query/read models if not already surfaced
- add UI affordances for validation results and lock state later
- consider idempotency semantics for repeated lock/unlock requests
- consider emitting internal domain/outbox events for lock state changes if downstream consumers exist