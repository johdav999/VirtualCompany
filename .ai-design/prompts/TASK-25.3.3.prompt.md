# Goal
Implement API integration tests for `TASK-25.3.3` covering the reconciliation suggestion review workflow endpoints introduced under `US-25.3`, ensuring the API behavior matches acceptance criteria for listing open suggestions, accepting suggestions, rejecting suggestions, and handling deterministic validation/error cases.

# Scope
Add or extend integration tests in the API test project to verify:

- Listing open reconciliation suggestions with filtering by:
  - tenant
  - entity type
  - status
  - minimum confidence
- Accepting a reconciliation suggestion:
  - authorized access succeeds
  - persisted reconciliation result is returned
  - linked record identifiers are present in the response payload
- Rejecting a reconciliation suggestion:
  - authorized access succeeds
  - updated suggestion status is returned
- Deterministic validation behavior when attempting to accept:
  - an already accepted suggestion
  - an already rejected suggestion
- Integration coverage for:
  - authorization
  - validation
  - idempotency/state-transition behavior
  - response payload shape/content for accept and reject actions

Out of scope unless strictly required to make tests pass:

- Refactoring production code unrelated to reconciliation APIs
- Adding new business features beyond what tests require
- UI/mobile changes
- Broad test framework rewrites

# Files to touch
Prefer touching only the minimum necessary files, likely under:

- `tests/VirtualCompany.Api.Tests/...`
- Any existing reconciliation-specific integration test files
- Shared API integration test infrastructure if needed for:
  - authenticated tenant-scoped requests
  - test data seeding
  - response deserialization helpers

Potential files to inspect first:

- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
- Existing integration test base classes, fixtures, custom web application factory, auth helpers, and seed utilities
- Reconciliation API controller/endpoint files under:
  - `src/VirtualCompany.Api/...`
- Reconciliation application/domain contracts used by the API under:
  - `src/VirtualCompany.Application/...`
  - `src/VirtualCompany.Domain/...`
  - `src/VirtualCompany.Shared/...`

If no reconciliation integration test file exists, create a focused file with a clear name such as:

- `tests/VirtualCompany.Api.Tests/Reconciliation/ReconciliationWorkflowApiTests.cs`

Use actual project conventions if different.

# Implementation plan
1. Inspect existing reconciliation API surface and test conventions
   - Find the endpoints for:
     - listing suggestions
     - accepting a suggestion
     - rejecting a suggestion
   - Identify route patterns, request/response DTOs, auth requirements, and error response format
   - Reuse existing integration test patterns for:
     - tenant scoping
     - authenticated users
     - seeded data
     - validation assertions

2. Identify the expected workflow state model
   - Confirm valid suggestion statuses, likely including open/pending, accepted, and rejected
   - Confirm whether “open” is represented explicitly or inferred
   - Confirm deterministic error contract for invalid accept transitions:
     - HTTP status code
     - error code / problem details / validation payload structure
     - message expectations
   - Mirror actual API contract rather than inventing a new one

3. Build reusable seeded test data for reconciliation scenarios
   - Seed at least:
     - one tenant with multiple suggestions
     - another tenant with similar suggestions to verify isolation
   - Include suggestions with varied:
     - entity types
     - statuses
     - confidence scores
   - Include records needed to validate accept responses with linked record identifiers
   - Ensure seeded data is deterministic and easy to assert against

4. Add integration tests for list endpoint filtering
   - Verify only tenant-owned suggestions are returned
   - Verify filtering by entity type
   - Verify filtering by status
   - Verify filtering by minimum confidence
   - Verify endpoint returns open reconciliation suggestions per acceptance criteria
   - If filters are combinable, add at least one test covering combined filters

5. Add integration tests for accept endpoint success path
   - Arrange an open suggestion in the current tenant
   - Call accept endpoint as an authorized user
   - Assert success status code
   - Assert response contains:
     - persisted reconciliation result identifier if applicable
     - suggestion/reference identifiers
     - linked record identifiers
     - accepted/final status fields expected by contract
   - Optionally verify persistence by re-querying through API or database-backed test helpers if that is standard in the repo

6. Add integration tests for reject endpoint success path
   - Arrange an open suggestion in the current tenant
   - Call reject endpoint as an authorized user
   - Assert success status code
   - Assert response contains updated suggestion status reflecting rejection
   - Verify persisted state if existing test conventions support it

7. Add authorization coverage
   - Unauthenticated request should return the expected auth failure status
   - Authenticated user from another tenant should receive forbidden/not found according to existing tenant isolation conventions
   - If role/policy-based authorization exists for reconciliation review actions, add a test for insufficient permissions

8. Add invalid transition/idempotency behavior tests
   - Attempt to accept an already accepted suggestion
   - Attempt to accept an already rejected suggestion
   - Assert deterministic validation error response:
     - expected status code
     - stable error code/key/message shape
   - If reject-on-rejected or reject-on-accepted behavior is defined and easy to cover, add tests, but prioritize the explicit acceptance criteria first

9. Assert response payloads precisely but pragmatically
   - Validate important contract fields, not just status codes
   - Prefer strongly typed deserialization if DTOs are available to tests
   - If API uses `ProblemDetails` or validation problem responses, assert:
     - title/type/status if stable
     - domain error code if present
     - relevant field or message content
   - Avoid brittle assertions on incidental formatting or full raw JSON unless necessary

10. Keep tests isolated and maintainable
   - Use descriptive test names in `Given_When_Then` or existing project style
   - Minimize duplication with helper methods for:
     - seeding suggestions
     - creating authenticated clients
     - sending accept/reject requests
     - asserting common error responses

11. Run and fix
   - Run targeted tests first
   - Then run the full API test suite if feasible
   - Only make minimal production-code adjustments if tests expose missing deterministic behavior required by the task

# Validation steps
Run the relevant test suite and confirm all new tests pass.

Suggested commands:

```bash
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj
```

If needed, run the full solution tests/build:

```bash
dotnet test
dotnet build
```

Validation checklist:

- List endpoint integration tests pass for tenant and filter behavior
- Accept endpoint integration tests pass for success response and persisted result payload
- Reject endpoint integration tests pass for success response and updated status payload
- Invalid accept transition tests pass for already accepted/rejected suggestions with deterministic validation responses
- Authorization tests pass for unauthenticated and cross-tenant/unauthorized access
- No unrelated tests are broken

# Risks and follow-ups
- The exact reconciliation endpoint routes and DTOs may differ from assumptions; inspect implementation first and align tests to the real contract.
- Deterministic validation error behavior may not yet be fully implemented. If tests expose inconsistency, make the smallest production fix necessary and keep it aligned with existing API error conventions.
- Multi-tenant isolation may intentionally return `404` instead of `403`; follow existing platform behavior from `ST-101` patterns rather than forcing one status.
- If integration tests currently lack reconciliation seed helpers, adding lightweight reusable fixtures may be necessary.
- If accept creates downstream audit/outbox side effects, avoid asserting asynchronous side effects unless they are already stable in integration tests.
- Follow-up opportunity: add contract-level tests for pagination/sorting on suggestion listing if those capabilities exist but are not covered by this task.