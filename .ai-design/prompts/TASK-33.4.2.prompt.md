# Goal
Implement `TASK-33.4.2` for story `US-33.4` by adding approval-gated API endpoints and domain/application handlers for sensitive Fortnox write operations so that:

- sensitive outbound Fortnox write requests are persisted as approval-required actions,
- no sensitive write is sent to Fortnox before approval completion,
- approved actions execute using the tenant’s active Fortnox connection,
- execution request/response status and audit trail are recorded,
- rejected/expired approvals never call Fortnox and remain visible in history,
- failures store safe user-facing summaries and only retry when explicitly supported,
- automated and integration tests cover the required paths, including EF Core migration application.

Work within the existing modular monolith and preserve tenant isolation, approval-first execution, auditability, and CQRS-lite boundaries.

# Scope
In scope:

- Add or extend Fortnox-sensitive write action API endpoints in `VirtualCompany.Api`.
- Add application commands/handlers for:
  - creating approval-gated Fortnox write actions,
  - approving/rejecting/expiring and executing approved actions,
  - recording execution outcomes and safe summaries.
- Add/extend domain model(s) for approval-backed outbound integration actions if not already present.
- Ensure approved execution resolves the tenant’s active Fortnox connection and never crosses tenant boundaries.
- Persist request payload, execution status, response metadata, audit events, and approval linkage.
- Prevent Fortnox calls for pending, rejected, expired, or cancelled approvals.
- Implement retry behavior only for action types explicitly marked retryable.
- Add/update EF Core migrations and integration tests validating schema application.
- Add/update automated tests for the acceptance criteria, especially approval gating and Fortnox/OAuth/token handling paths already called out by the story.

Out of scope unless required to complete this task cleanly:

- Broad UI redesign beyond any minimal Finance settings behavior needed by existing acceptance criteria/tests.
- New connector families beyond Fortnox.
- Refactoring unrelated approval or workflow modules.
- Mobile-specific changes unless tests or shared contracts require them.

# Files to touch
Inspect the solution first and then modify the smallest coherent set of files. Expect to touch files in these areas:

- `src/VirtualCompany.Api`
  - endpoint/controller files for Fortnox integration and approvals
  - DI/registration files
  - request/response DTOs
- `src/VirtualCompany.Application`
  - commands, handlers, validators
  - approval/execution orchestration services
  - typed contracts for Fortnox write actions
- `src/VirtualCompany.Domain`
  - entities/value objects/enums for approval-backed outbound actions
  - domain rules for status transitions, retryability, and audit-safe summaries
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configurations
  - repositories
  - Fortnox client adapter changes
  - token refresh / connection resolution logic
  - migrations
- `src/VirtualCompany.Shared`
  - shared contracts/enums if already used for API/UI boundaries
- `src/VirtualCompany.Web`
  - only if Finance settings UI behavior is already covered here and needs test-aligned adjustments
- `tests/VirtualCompany.Api.Tests`
  - API/integration tests
  - approval gating tests
  - tenant isolation tests
  - OAuth/token refresh/API failure translation tests
  - migration application tests if housed here

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repository already has Fortnox-specific files, approval modules, or integration test infrastructure, extend those patterns rather than inventing parallel ones.

# Implementation plan
1. **Discover existing Fortnox and approval architecture**
   - Find current Fortnox connection, OAuth, token refresh, sync, and write-operation code paths.
   - Find approval domain/application flow and any existing “pending action” or “approval-required action” model.
   - Identify current audit event creation and tool/integration execution persistence patterns.
   - Identify how tenant context is resolved in API and application layers.

2. **Design the approval-backed outbound action flow**
   - Reuse existing approval module concepts if possible.
   - Introduce or extend a persisted entity representing a Fortnox outbound write action with fields covering:
     - tenant/company id,
     - action type,
     - target/reference identifiers,
     - request payload,
     - approval status / linked approval id,
     - execution status,
     - retry support flag/policy,
     - safe user-facing failure summary,
     - Fortnox response metadata/status,
     - timestamps,
     - audit correlation identifiers.
   - Model explicit statuses such as:
     - `PendingApproval`
     - `ApprovedPendingExecution`
     - `Executing`
     - `Succeeded`
     - `Failed`
     - `Rejected`
     - `Expired`
     - `Cancelled`
   - Ensure domain rules prevent execution from any non-approved state.

3. **Add application commands and handlers**
   - Add command to create a sensitive Fortnox write action.
     - Persist the action first.
     - Create/link approval request.
     - Do not call Fortnox here.
   - Add command/handler for approval completion reaction.
     - On approve: transition to executable state and invoke backend execution path.
     - On reject/expire: mark terminal non-executed state and audit it.
   - Add execution handler/service that:
     - resolves tenant-scoped active Fortnox connection,
     - refreshes token if needed using existing infrastructure,
     - sends the request only after approval,
     - records request/response status and safe summaries,
     - stores retry metadata only when action type supports retry.
   - Keep handlers idempotent where approval callbacks or retries may duplicate delivery.

4. **Implement API endpoints**
   - Add or extend endpoints for creating sensitive Fortnox write requests.
   - Ensure endpoint behavior returns a result indicating approval is required/pending rather than implying immediate execution.
   - If approval decision endpoints are in this task’s scope and not already present, wire them to the new action flow.
   - Map failures to safe API responses without leaking raw Fortnox details or secrets.

5. **Enforce tenant isolation**
   - Every query and mutation must be scoped by `company_id`/tenant context.
   - Ensure Fortnox connection lookup uses the current tenant only.
   - Add negative tests proving one tenant cannot view or execute another tenant’s pending/approved action.

6. **Persist audit trail**
   - Record business audit events for:
     - action requested,
     - approval created,
     - approval approved/rejected/expired,
     - execution started,
     - execution succeeded,
     - execution failed,
     - retry attempted/skipped.
   - Keep rationale and summaries concise and user-safe.
   - Do not store secrets/tokens/raw sensitive payloads beyond existing safe persistence conventions.

7. **Handle failure and retry policy**
   - Translate Fortnox/API/OAuth/token refresh failures into safe user-facing summaries.
   - Distinguish:
     - approval/business failures,
     - auth failures,
     - transient upstream failures,
     - permanent validation failures.
   - Retry only when the action type explicitly supports it.
   - For non-retryable failures, persist terminal failure state with summary and audit event.

8. **EF Core schema changes**
   - Add/update entity configurations and migration(s).
   - Ensure migration includes all new tables/columns/indexes/constraints needed for:
     - outbound action persistence,
     - approval linkage,
     - execution metadata,
     - audit references.
   - Prefer indexes on tenant + status + created/execution timestamps for operational queries.
   - Keep migration naming aligned with repository conventions.

9. **Testing**
   - Add/extend automated tests to cover the acceptance criteria:
     - sensitive write request persists approval-required action and does not call Fortnox pre-approval,
     - approved action executes with tenant’s active connection,
     - rejected/expired approvals never call Fortnox,
     - failures store safe summaries,
     - retry occurs only for supported action types,
     - OAuth success/failure,
     - token refresh success/failure,
     - sync idempotency,
     - tenant isolation,
     - API failure translation,
     - approval gating,
     - Finance settings UI behavior if applicable in current test suite.
   - Add integration test verifying all new EF Core migrations apply successfully.

10. **Keep implementation aligned with existing patterns**
   - Use existing DI, MediatR/CQRS, repository, Result/Error, and test fixture patterns.
   - Do not introduce a new architectural style.
   - Prefer extending existing Fortnox abstractions over adding duplicate clients/services.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run targeted tests first if available for Fortnox/integrations/approvals:
   - `dotnet test`

3. Verify the following behaviors with automated tests:
   - creating a sensitive Fortnox write request persists a pending approval-backed action,
   - no Fortnox client call occurs before approval,
   - approval triggers execution exactly once for idempotent duplicate approval signals,
   - rejection/expiry leaves action visible in audit/history and never calls Fortnox,
   - approved execution uses the correct tenant connection,
   - token refresh success path executes correctly,
   - token refresh failure path stores safe summary and no unsafe leakage,
   - OAuth success/failure paths still pass,
   - API failure translation returns safe responses,
   - retry behavior only occurs for explicitly retryable action types,
   - tenant isolation blocks cross-tenant access,
   - migration application test passes against the test database/provider.

4. If migrations are generated/updated, verify they apply in integration tests and are included in source control.

5. In the final change summary, explicitly list:
   - new/changed endpoints,
   - new commands/handlers/entities,
   - migration name,
   - tests added/updated,
   - any assumptions or follow-up gaps.

# Risks and follow-ups
- The repository may already contain a generic approval-backed action model; prefer reuse over adding a Fortnox-only duplicate.
- Existing Fortnox write operations may currently execute inline; be careful to reroute only sensitive writes without breaking non-sensitive reads/sync.
- Token refresh and OAuth logic may be shared across connectors; avoid regressions by keeping changes connector-scoped where possible.
- Safe failure summaries must not leak tokens, raw upstream payloads, or internal exception details.
- Approval completion may currently be asynchronous; ensure execution is idempotent and resilient to duplicate events/messages.
- Finance settings UI behavior is in the acceptance criteria; if current tests cover it, update only the minimum contracts/UI state needed.
- If full end-to-end coverage cannot be completed due to missing test infrastructure, still implement the production path and add the closest possible automated coverage, then document the gap clearly.