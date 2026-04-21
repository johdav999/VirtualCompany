# Goal
Implement backlog task **TASK-23.3.3 — Create migration and API endpoints for close checks, lock, and unlock operations** for story **US-23.3 Support period-end close checks and reporting lock controls** in the existing **.NET modular monolith**.

Deliver a production-ready vertical slice that:
- adds persistence for fiscal period reporting lock state and operation history
- exposes tenant-scoped API endpoints for:
  - period close validation/checks
  - reporting lock
  - reporting unlock
- enforces lock behavior when regenerating stored statement outputs
- records audit trail entries for validation, lock, and unlock actions with actor and timestamp
- covers both API and background job execution paths with automated tests

Use existing project conventions and architecture. Prefer minimal, cohesive changes over broad refactors.

# Scope
In scope:
- Database migration(s) for reporting lock state and any supporting audit/operation fields
- Domain/application/infrastructure changes needed to model:
  - close validation results
  - reporting lock state for a fiscal period
  - lock/unlock metadata
- API endpoints for:
  - validating whether a fiscal period can be closed
  - locking a closed fiscal period for reporting
  - unlocking a locked fiscal period
- Authorization/state checks preventing regeneration of stored statement outputs for locked periods
- Consistent error response when regeneration is attempted for a locked period
- Audit event creation for validation, lock, and unlock operations
- Automated tests for:
  - validation blocking issues
  - lock/unlock state transitions
  - locked regeneration rejection in API path
  - locked regeneration rejection in background job path

Out of scope unless required by existing code structure:
- New UI pages/components
- Broad redesign of fiscal period or reporting modules
- New workflow engine features unrelated to this task
- Large audit subsystem refactors
- New permissions model beyond wiring into existing authorization patterns

# Files to touch
Inspect the solution first, then update the most relevant files in these areas as needed.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- API controllers or endpoint registration for finance/reporting/period close operations
- Application commands/queries/handlers for:
  - close validation
  - lock period
  - unlock period
  - report regeneration guard
- Domain entities/value objects/enums for fiscal period lock state and validation issue types
- Infrastructure persistence:
  - EF Core entity configurations
  - DbContext
  - repository/query implementations
  - migration files
- Audit event creation logic
- Background job/report generation handlers that regenerate stored statement outputs
- Integration/API tests and unit tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration conventions and local workflow expectations.

# Implementation plan
1. **Discover existing finance/reporting model**
   - Find current entities and endpoints related to:
     - fiscal periods
     - close operations
     - statement generation/storage
     - report regeneration
     - audit events
   - Identify how tenant context and actor identity are resolved.
   - Identify whether EF Core migrations are active in this repo and follow existing conventions exactly.

2. **Design persistence changes**
   - Add the smallest schema change that supports acceptance criteria.
   - Prefer extending an existing fiscal period table/entity if present; otherwise add a focused lock table tied to fiscal period.
   - Persist at minimum:
     - `is_reporting_locked` or equivalent
     - `reporting_locked_at`
     - `reporting_locked_by_user_id` or actor reference
     - `reporting_unlocked_at`
     - `reporting_unlocked_by_user_id` or actor reference
   - If the domain already tracks period status, ensure lock is only applicable to a **closed** fiscal period.
   - Add migration with clear names and reversible logic where supported.

3. **Model close validation**
   - Implement a query/service that returns blocking issues for a target fiscal period.
   - Validation must detect and return blocking issues when the period contains:
     - unposted source documents
     - unbalanced journal entries
     - missing statement mappings
   - Return structured issue payloads with stable machine-readable codes and concise messages.
   - Keep validation deterministic and tenant-scoped.

4. **Add API endpoints**
   - Implement endpoints consistent with existing API style, likely under a finance/reporting/fiscal-period route.
   - Required operations:
     - `POST` or `GET` close validation endpoint for a period
     - `POST` lock endpoint for a closed period
     - `POST` unlock endpoint for a locked period
   - Responses should:
     - include validation issues for close checks
     - return updated lock state metadata for lock/unlock
     - use consistent problem/error responses for invalid state or unauthorized actions

5. **Enforce business rules**
   - Lock can only be applied to a **closed** fiscal period.
   - Unlock requires authorized actor per existing authorization approach.
   - Regeneration of stored statement outputs for a locked period must be blocked unless an authorized unlock has occurred first.
   - Ensure both synchronous API-triggered regeneration and background job-triggered regeneration check the same lock guard, ideally via shared application/domain service rather than duplicated controller logic.

6. **Audit trail integration**
   - Create business audit events for:
     - close validation executed
     - reporting lock applied
     - reporting lock removed
   - Include:
     - company/tenant id
     - actor type/id
     - action
     - target type/id
     - outcome
     - timestamp
   - If rationale/details are supported, include summary metadata such as issue counts or lock reason/context without exposing sensitive internals.

7. **Consistent locked regeneration error**
   - When regeneration is attempted for a locked period, return a consistent authorization or invalid-state response aligned with existing API error patterns.
   - Reuse the same error code/message shape across API and job paths where practical.
   - For background jobs, ensure the failure is recorded as a business/state failure rather than retried indefinitely if lock is the cause.

8. **Tests**
   - Add automated tests covering:
     - validation returns blocking issues for each required condition
     - lock succeeds only for closed periods
     - unlock changes state and records audit event
     - regeneration endpoint/process fails for locked periods with expected error shape/state
     - background execution path also respects lock state
   - Prefer integration tests in `tests/VirtualCompany.Api.Tests` for endpoint behavior and persistence-backed enforcement.
   - Add focused unit tests for validation logic if the rules are implemented in a dedicated service.

9. **Keep implementation cohesive**
   - Reuse existing abstractions for:
     - tenant scoping
     - current user/actor
     - authorization
     - audit event persistence
     - background job execution
   - Avoid introducing parallel patterns.

# Validation steps
1. Review migration guidance and existing patterns:
   - inspect `README.md`
   - inspect `docs/postgresql-migrations-archive/README.md`

2. Restore/build:
   - `dotnet build`

3. Run tests before changes to establish baseline:
   - `dotnet test`

4. Apply implementation and generate/add migration per repo convention.

5. Run targeted and full tests:
   - `dotnet test`

6. Manually verify, via tests or local API execution, these scenarios:
   - close validation returns blocking issues for:
     - unposted source documents
     - unbalanced journal entries
     - missing statement mappings
   - locking a non-closed period is rejected
   - locking a closed period succeeds and persists actor/timestamp
   - unlocking a locked period succeeds and persists actor/timestamp
   - regeneration for a locked period returns the expected consistent error response
   - background regeneration path is blocked for locked periods and does not proceed

7. Confirm audit records are written for:
   - validation
   - lock
   - unlock

8. If migrations are checked in as files, ensure they are included and build cleanly.

# Risks and follow-ups
- **Unknown existing finance schema**: fiscal period, statement mapping, and report generation models may already exist under different names. Adapt to actual code rather than forcing new abstractions.
- **Authorization ambiguity**: acceptance criteria says “authorized unlock operation.” Use existing policy/role mechanisms; if no explicit policy exists, implement the narrowest reasonable authorization hook and note it.
- **Background job enforcement drift**: the biggest risk is checking lock state only in controllers. Centralize the guard in application/domain logic used by both API and workers.
- **Audit duplication/inconsistency**: if audit events are already emitted via pipeline behaviors or domain events, integrate there instead of double-writing.
- **Migration convention mismatch**: follow repository-specific migration practices exactly, especially if SQL scripts are archived separately from EF-generated migrations.
- **Potential follow-up**:
  - expose lock status in fiscal period query endpoints
  - add explicit permission names/policies for lock vs unlock
  - add UI affordances for finance users to view validation issues and lock state
  - add idempotency handling for repeated lock/unlock requests