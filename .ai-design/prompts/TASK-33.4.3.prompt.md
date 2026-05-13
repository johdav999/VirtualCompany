# Goal
Implement `TASK-33.4.3` by adding backend integration tests that verify Fortnox OAuth flows, token refresh behavior, sync idempotency, tenant isolation, outbound approval enforcement, API failure translation, Finance settings UI behavior, and EF Core migration application for all new schema changes introduced by approval-backed outbound Fortnox write operations.

# Scope
Focus only on test and minimal supporting test-infrastructure changes required to validate the acceptance criteria for `US-33.4 Implement approval-backed outbound Fortnox write operations and end-to-end test coverage`.

Include:
- Backend integration tests for:
  - OAuth success path
  - OAuth failure path
  - Token refresh success path
  - Token refresh failure path
  - Sync idempotency
  - Tenant isolation
  - Approval gating for sensitive outbound writes
  - Approved execution path
  - Rejected approval path
  - Expired approval path
  - Outbound execution failure persistence and safe summary behavior
  - API failure translation behavior
  - EF Core migration application success
- Finance settings UI behavior coverage if this already exists in the API/web test surface and can be validated through integration-style tests without introducing a new UI test framework
- Minimal test fixtures, fake Fortnox handlers, seeded data builders, and helpers needed to make the tests reliable and readable

Do not:
- Re-architect production code unless a very small seam is required for testability
- Introduce broad new frameworks if existing test infrastructure can support the scenarios
- Change business behavior beyond what is necessary to make the tests pass and align with acceptance criteria

# Files to touch
Prefer touching only files in these areas, plus any narrowly necessary production seams:
- `tests/VirtualCompany.Api.Tests/**`
- Existing test fixture/setup files under `tests/VirtualCompany.Api.Tests`
- API host factory / integration test bootstrap files
- Test doubles for Fortnox HTTP interactions
- Seed/data builder helpers for tenants, connections, approvals, and outbound actions
- If needed, minimal production files in:
  - `src/VirtualCompany.Api/**`
  - `src/VirtualCompany.Infrastructure/**`
  - `src/VirtualCompany.Application/**`
only to expose testable seams, deterministic clocks, or injectable HTTP clients/handlers already consistent with the architecture

Before editing, inspect:
- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
- Existing integration test patterns and `WebApplicationFactory` setup
- Current Fortnox integration implementation in API/Application/Infrastructure
- Existing EF Core DbContext, migrations, and test database setup
- Any approval, audit, and outbound execution persistence models already added by prior tasks
- Any Finance settings endpoints/pages already present in `src/VirtualCompany.Web` or API-backed settings handlers

# Implementation plan
1. Inspect the current implementation and test harness
   - Find existing integration test conventions, database lifecycle strategy, authentication/tenant setup helpers, and HTTP mocking approach
   - Identify Fortnox-related endpoints/services for:
     - OAuth callback/connect
     - token refresh
     - sync jobs or sync endpoints
     - outbound write execution
     - approval completion handling
     - finance settings retrieval/update
   - Identify persistence entities/tables for:
     - Fortnox connections/tokens
     - approval-required outbound actions
     - audit trail
     - request/response status storage
     - retry metadata / failure summaries

2. Extend test infrastructure for deterministic Fortnox integration testing
   - Add or reuse a fake/stub `HttpMessageHandler` or test server for Fortnox API responses
   - Ensure tests can script:
     - OAuth token exchange success/failure
     - refresh token success/failure
     - outbound write success/failure
     - API error payloads and status codes
   - Ensure the test host can inject this fake cleanly without affecting production behavior
   - Add deterministic clock/time control if approval expiry tests need it

3. Add reusable test data builders/helpers
   - Tenant/company builder
   - Membership/authenticated user helper
   - Fortnox connection builder with active/expired tokens
   - Approval request/action builder
   - Pending outbound action builder
   - Audit assertion helpers
   - Keep helpers explicit and tenant-aware to avoid hidden coupling

4. Implement OAuth integration tests
   - Success case:
     - simulate valid OAuth callback/token exchange
     - assert active Fortnox connection persisted for the correct tenant
     - assert expected status/fields are stored
   - Failure case:
     - simulate failed token exchange or invalid callback state
     - assert no active connection is created
     - assert safe API response / error translation
     - assert no cross-tenant side effects

5. Implement token refresh integration tests
   - Refresh success:
     - seed expired/near-expiry token
     - trigger operation requiring refresh
     - assert refresh request sent
     - assert updated token values persisted
     - assert downstream operation can proceed when appropriate
   - Refresh failure:
     - simulate refresh rejection
     - assert connection is not silently treated as valid
     - assert safe failure surfaced
     - assert audit/failure state recorded if required
     - assert no outbound write is sent after refresh failure

6. Implement sync idempotency integration tests
   - Trigger the same sync twice with the same correlation/idempotency conditions
   - Assert duplicate records are not created
   - Assert repeated execution does not duplicate side effects, audit entries, or imported entities beyond intended semantics
   - If idempotency is keyed, verify the key is honored and tenant-scoped

7. Implement tenant isolation integration tests
   - Seed two tenants with separate Fortnox connections, approvals, and actions
   - Assert:
     - tenant A cannot access tenant B finance settings or action history
     - approval completion in tenant A cannot execute tenant B action
     - outbound execution always uses the active connection belonging to the action’s tenant
   - Verify forbidden/not found behavior matches existing API conventions

8. Implement approval gating integration tests for sensitive outbound writes
   - For a sensitive Fortnox write request:
     - invoke the backend operation
     - assert the action is persisted as approval-required
     - assert no Fortnox outbound HTTP request is sent before approval
     - assert audit/history entry exists
   - Approved path:
     - approve the action
     - assert backend executes using the tenant’s active Fortnox connection
     - assert request payload, response status, and audit trail are recorded
   - Rejected path:
     - reject the approval
     - assert no Fortnox request is sent
     - assert action remains visible in audit history with rejected status
   - Expired path:
     - expire approval via clock manipulation or seeded timestamps
     - assert no Fortnox request is sent
     - assert action remains visible in audit history with expired status

9. Implement outbound failure and API failure translation tests
   - Simulate Fortnox API failures for approved outbound actions
   - Assert:
     - safe user-facing summary is stored
     - raw sensitive details are not exposed in user-facing fields
     - retry metadata/eligibility only reflects explicit support by action type
     - translated API response/error contract matches existing conventions
   - Cover representative failure classes:
     - validation/business error
     - unauthorized/expired token
     - transient server error if supported

10. Cover Finance settings UI behavior in the most appropriate existing test layer
   - If there are API endpoints backing Finance settings:
     - verify settings reflect connection/approval state correctly
     - verify pending approval/outbound statuses are surfaced as expected
   - If there are existing web integration tests:
     - add focused tests for visible behavior only
   - Do not introduce browser automation unless the repo already uses it and the behavior cannot be validated otherwise

11. Add migration application integration coverage
   - Create or extend a test that boots the real DbContext against a test database and applies EF Core migrations
   - Assert migrations apply successfully from a clean database
   - If practical in current harness, verify the expected new tables/columns/indexes relevant to approvals/outbound actions exist after migration
   - Keep this test stable and environment-appropriate for CI

12. Keep assertions aligned to acceptance criteria
   - Specifically verify:
     - approval-required actions are persisted and gated
     - approved actions execute and record request/response/audit
     - rejected/expired approvals never send requests
     - failures store safe summaries and retry only when supported
     - OAuth, refresh, idempotency, tenant isolation, API failure translation, approval gating, finance settings behavior, and migrations are all covered

13. Final cleanup
   - Refactor duplicated setup into helpers
   - Keep test names descriptive and scenario-based
   - Ensure tests are isolated, deterministic, and parallel-safe if the suite supports parallelization

# Validation steps
1. Restore/build:
   - `dotnet build VirtualCompany.sln`

2. Run the API test project:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. If the full suite is reasonable, run all tests:
   - `dotnet test`

4. Verify new tests cover at least these scenario groups:
   - OAuth success/failure
   - token refresh success/failure
   - sync idempotency
   - tenant isolation
   - approval gating
   - approved execution
   - rejected/expired approvals
   - outbound failure safe-summary behavior
   - API failure translation
   - finance settings behavior
   - EF Core migration application

5. Manually inspect assertions for the most critical acceptance criteria:
   - no Fortnox request before approval
   - approved action uses correct tenant connection
   - rejected/expired approvals never send
   - audit history remains visible
   - safe summaries do not leak sensitive details
   - retry behavior is action-type aware

# Risks and follow-ups
- Existing test infrastructure may not yet support controllable external HTTP behavior; add the smallest possible seam
- Finance settings “UI behavior” may be ambiguous if no web integration harness exists; prefer validating API-backed behavior unless an existing web test pattern is already present
- Migration tests can be flaky if they depend on unavailable local infrastructure; align with the repo’s current integration DB strategy
- If approval expiry depends on wall-clock time, introduce an injectable clock rather than using sleeps
- If current production code tightly couples Fortnox HTTP calls, a small refactor to injectable clients/handlers may be required before tests can be added cleanly
- Follow-up if gaps remain:
  - add explicit contract tests for Fortnox error payload mapping
  - add web-layer tests for Finance settings rendering if not currently covered
  - add CI enforcement for migration-application test execution