# Goal
Implement backlog task **TASK-33.4.1 — Implement Fortnox outbound action executor behind approval workflow integration and audit persistence** in the existing .NET solution.

Deliver a production-ready implementation that ensures **sensitive outbound Fortnox write operations are approval-gated, tenant-scoped, auditable, and safely executed only after approval completion**, with EF Core migration coverage and automated tests aligned to the story and acceptance criteria.

# Scope
Implement the Fortnox outbound execution path across domain, application, infrastructure, API, and tests so that:

- Sensitive outbound Fortnox write requests are **persisted as approval-required actions**.
- These actions are **not sent to Fortnox before approval**.
- Approved actions execute through backend services using the tenant’s **active Fortnox connection**.
- Execution persists:
  - request payload
  - response status
  - safe response details
  - audit trail entries
- Rejected or expired approvals:
  - never call Fortnox
  - remain visible in audit history
- Failures store **safe user-facing summaries**
- Retry behavior is **explicitly action-type driven**, not generic
- Tests cover:
  - OAuth success/failure
  - token refresh success/failure
  - sync idempotency
  - tenant isolation
  - API failure translation
  - approval gating
  - Finance settings UI behavior
  - EF Core migration application

Do not introduce speculative architecture beyond what is needed for this task. Reuse existing approval, audit, integration, and Fortnox patterns where present.

# Files to touch
Inspect the solution first and then update the relevant files in these areas as needed.

## Likely projects
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

## Likely file categories
Adjust exact filenames to the current codebase structure.

### Domain
- Fortnox integration entities/value objects/enums
- Approval-linked outbound action aggregate/entity
- Audit event models if needed
- Retry policy/action capability definitions

### Application
- Commands/handlers for:
  - creating Fortnox outbound actions
  - approving/executing Fortnox outbound actions
  - rejecting/expiring actions
- Queries/view models for audit/history visibility
- DTOs for safe execution summaries
- Policy/approval orchestration services
- Tenant-scoped execution interfaces

### Infrastructure
- EF Core DbContext and entity configurations
- New migration(s) for outbound action persistence/schema changes
- Fortnox API client/executor implementation
- Token refresh handling
- Persistence repositories
- Background execution or post-approval dispatcher if approval completion is asynchronous
- Audit persistence implementation

### API
- Endpoints/controllers/minimal APIs for:
  - creating approval-required Fortnox actions
  - approval completion trigger or execution callback path
  - querying action/audit status if needed
- Error translation for Fortnox/API failures

### Web
- Finance settings UI behavior updates if this task affects approval/execution visibility or Fortnox connection state UX
- Any approval/audit status display required by existing acceptance tests

### Tests
- Integration tests for:
  - approval gating
  - approved execution
  - rejected/expired non-execution
  - OAuth/token refresh paths
  - tenant isolation
  - idempotency
  - API failure translation
  - migration application
- UI/integration tests for Finance settings behavior if already covered in test suite patterns

# Implementation plan
1. **Survey the existing implementation**
   - Find all Fortnox-related code, approval workflow code, audit persistence, and EF Core migration patterns.
   - Identify whether there is already:
     - a Fortnox connection entity
     - OAuth/token refresh support
     - approval entities/workflows
     - audit event persistence
     - tool execution or outbound action persistence
   - Prefer extending existing abstractions over creating parallel ones.

2. **Model approval-backed outbound Fortnox actions**
   - Introduce or extend a persisted entity for outbound Fortnox write actions.
   - Minimum fields should support:
     - action id
     - tenant/company id
     - Fortnox connection reference
     - action type
     - request payload
     - approval id/reference
     - status lifecycle
     - retry support flag/policy
     - execution timestamps
     - response status/code
     - safe response/error summary
     - idempotency/correlation key
     - created/updated timestamps
   - Ensure statuses clearly distinguish:
     - pending approval
     - approved awaiting execution
     - executed success
     - executed failure
     - rejected
     - expired
     - cancelled if already part of approval model

3. **Integrate with approval workflow**
   - When a sensitive outbound Fortnox write is requested:
     - persist the outbound action first
     - create/link the approval request
     - do not call Fortnox
   - Ensure approval completion transitions the action correctly.
   - On rejection or expiry:
     - mark the action terminal
     - do not execute
     - persist audit visibility

4. **Implement approved execution path**
   - Add an application service/handler that executes only approved Fortnox outbound actions.
   - Resolve the tenant’s active Fortnox connection in a tenant-safe way.
   - Validate the action is executable and not already completed.
   - Send the request through the Fortnox backend client.
   - Persist:
     - outbound request snapshot
     - HTTP/result status
     - safe response payload or summary
     - audit event(s)
   - Use idempotency protections so repeated approval/execution triggers do not duplicate writes.

5. **Handle OAuth and token refresh correctly**
   - Reuse existing OAuth/token refresh flows if present.
   - Ensure execution path:
     - uses active access token
     - refreshes token when required
     - persists refreshed token state safely
     - fails safely when refresh fails
   - Translate auth failures into safe user-facing summaries and audit outcomes.

6. **Implement failure handling and retry rules**
   - Distinguish:
     - approval/policy failures
     - auth failures
     - Fortnox API validation/business failures
     - transient transport/server failures
   - Persist safe user-facing summaries only; do not leak secrets or raw sensitive payloads.
   - Retry only when the action type explicitly supports it.
   - If retries are supported, encode that in the action metadata/policy rather than broad exception-based retries.

7. **Persist audit trail**
   - Record business audit events for:
     - outbound action requested
     - approval requested
     - approval approved/rejected/expired
     - execution started
     - execution succeeded
     - execution failed
     - execution skipped due to rejection/expiry
   - Ensure audit records are tenant-scoped and queryable in existing audit history surfaces.

8. **Update EF Core model and migrations**
   - Add/modify entity configurations and DbSet mappings.
   - Generate EF Core migration(s) for all schema changes.
   - Ensure migration names are clear and task-focused.
   - Verify migrations apply cleanly in integration tests.

9. **Update API and UI behavior as needed**
   - Ensure API responses reflect approval-gated behavior.
   - If Finance settings UI currently surfaces Fortnox connection/execution state, update it to show safe statuses and failure states consistent with tests.
   - Preserve tenant isolation and authorization checks.

10. **Add comprehensive automated tests**
   - Add or extend tests to cover:
     - OAuth success path
     - OAuth failure path
     - token refresh success path
     - token refresh failure path
     - sync/execution idempotency
     - tenant isolation
     - API failure translation
     - approval gating before execution
     - approved execution
     - rejected/expired non-execution
     - Finance settings UI behavior
     - EF Core migration application
   - Prefer integration tests over excessive mocking for persistence and workflow behavior.

11. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep Fortnox as an adapter, not a system of record.
   - Keep approvals and auditability as first-class persisted business concerns.
   - Ensure all execution is tenant-scoped and policy-enforced.

# Validation steps
Run these after implementation and fix any failures.

1. Restore/build:
   - `dotnet build`

2. Run full test suite:
   - `dotnet test`

3. Specifically verify integration behavior:
   - Sensitive Fortnox write request creates persisted outbound action + approval record
   - No Fortnox call occurs before approval
   - Approval triggers exactly-once execution semantics
   - Rejection/expiry never calls Fortnox
   - Execution stores request/response status and audit events
   - Failure stores safe summary
   - Retry occurs only for supported action types

4. Verify migration coverage:
   - Ensure new EF Core migration is included in source
   - Ensure integration tests apply migrations successfully from a clean database

5. Verify tenant isolation:
   - Cross-tenant access to actions, approvals, Fortnox connections, and audit history must fail or return not found/forbidden according to existing conventions

6. Verify auth/token behavior:
   - OAuth success/failure tests pass
   - token refresh success/failure tests pass

7. Verify UI behavior:
   - Finance settings UI tests pass without regressing existing connection/status behavior

# Risks and follow-ups
- The codebase may already have a generic approval-backed action model; if so, extend it rather than creating a Fortnox-only duplicate.
- Be careful not to couple approval completion directly to immediate HTTP execution if the existing architecture expects background processing or outbox-driven execution.
- Avoid storing secrets or unsafe raw external error bodies in audit/history tables.
- Idempotency is easy to get wrong; ensure duplicate approval callbacks, retries, or repeated commands do not produce duplicate Fortnox writes.
- If existing tests are sparse around Fortnox integration, add focused integration seams rather than over-mocking the client.
- If Finance settings UI acceptance tests depend on specific text/status values, preserve existing UX conventions where possible.
- If migration snapshots are used, update them correctly and ensure no unrelated schema churn is introduced.