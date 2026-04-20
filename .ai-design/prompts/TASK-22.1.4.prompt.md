# Goal
Implement automated test coverage for **TASK-22.1.4** under **US-22.1 Implement Today’s Focus aggregation API and primary decision panel**.

Add integration and component tests that verify:

- `GET /api/dashboard/focus` returns **3 to 5** `FocusItem` records for a valid `companyId` and `userId`
- returned items are ordered by `PriorityScore` descending
- every returned item includes non-empty `Id`, `Title`, `Description`, `ActionType`, `PriorityScore`, and `NavigationTarget`
- every `PriorityScore` is an integer normalized to **0..100**
- aggregation includes items from approvals, tasks, anomalies, and finance alerts when those domains contain data
- `TodayFocusPanel` renders focus items as cards with title, short description, and CTA button
- clicking a CTA navigates to the exact `NavigationTarget` returned by the API

The task is test-focused. Prefer minimal production changes only if required to make the feature testable or to fix clear defects exposed by the tests.

# Scope
In scope:

- API integration tests for the dashboard focus endpoint
- test data setup covering approvals, tasks, anomalies, and finance alerts
- assertions for ordering, completeness, normalization, and source coverage
- Blazor component tests for `TodayFocusPanel`
- navigation behavior tests for CTA clicks
- any small testability improvements needed in API/web code

Out of scope unless strictly necessary:

- redesigning focus aggregation logic
- changing API contracts beyond fixing obvious mismatches with acceptance criteria
- broad refactors across dashboard modules
- mobile app changes
- unrelated dashboard widgets or stories

# Files to touch
Inspect first, then update only the relevant files. Likely candidates include:

- `tests/VirtualCompany.Api.Tests/...`
  - add or extend integration tests for `/api/dashboard/focus`
  - shared API test fixture / test host / seeded database helpers
- `src/VirtualCompany.Api/...`
  - only if needed for test hooks, route discovery, auth/test setup, or bug fixes exposed by tests
- `src/VirtualCompany.Application/...`
  - only if acceptance-criteria defects are found in aggregation/query behavior
- `src/VirtualCompany.Infrastructure/...`
  - only if test seeding requires repository or persistence support
- `src/VirtualCompany.Web/...`
  - locate `TodayFocusPanel` component and any related view models/services
- web test project if present; otherwise determine whether component tests belong in an existing test project or whether a new test project is needed
- solution/project files only if adding missing test dependencies such as bUnit or test utilities

Before editing, locate:

- the focus endpoint implementation
- the `FocusItem` contract/DTO
- the aggregation query/service
- the `TodayFocusPanel` component
- existing test conventions, fixtures, auth helpers, and component test patterns

# Implementation plan
1. **Discover existing implementation**
   - Find the endpoint for `GET /api/dashboard/focus`
   - Identify the request shape: route/query params, auth context, and expected response DTO
   - Find the aggregation service/query handler that builds `FocusItem` records
   - Find the `TodayFocusPanel` component and how it receives data
   - Review existing API integration test patterns and any Blazor component test setup

2. **Design API integration test coverage**
   Create integration tests that seed a valid tenant/user context and verify:
   - valid request returns HTTP 200
   - response count is between 3 and 5 when enough source data exists
   - items are sorted by `PriorityScore` descending
   - each item has non-empty required fields:
     - `Id`
     - `Title`
     - `Description`
     - `ActionType`
     - `PriorityScore`
     - `NavigationTarget`
   - each `PriorityScore` is an integer in `[0,100]`
   - source coverage includes approvals, tasks, anomalies, and finance alerts when seeded

   Prefer deterministic seeded data with clearly different expected scores so ordering assertions are stable.

3. **Seed representative cross-domain data**
   Build or reuse test fixtures/helpers to create data for:
   - approval records
   - task records
   - anomaly records
   - finance alert records

   Ensure all seeded records are:
   - scoped to the same `companyId`
   - visible to the same `userId` if user filtering exists
   - eligible for focus aggregation
   - distinct enough to validate source inclusion and ordering

   Also ensure there is no accidental cross-tenant leakage by keeping unrelated tenant data in at least one test if that pattern already exists.

4. **Add focused API assertions**
   Include assertions that:
   - `PriorityScore` values are actual integers, not decimals serialized unexpectedly
   - `NavigationTarget` is non-empty and plausibly routable
   - if the response exposes source/type metadata, verify domain presence directly
   - if source/type metadata is not exposed, infer inclusion from seeded titles/IDs/descriptions unique to each domain

5. **Add component tests for `TodayFocusPanel`**
   Using the project’s existing component test approach, or bUnit if not yet present:
   - render the panel with a known set of `FocusItem` models
   - assert cards are rendered for each item
   - assert each card shows:
     - title
     - short description
     - CTA button
   - assert rendered order matches input order if the component is expected to preserve API ordering

6. **Add navigation behavior tests**
   Verify CTA click behavior:
   - inject/mock `NavigationManager`
   - click the CTA for a rendered focus item
   - assert navigation goes to the exact `NavigationTarget` value from the model/API
   - include at least one test with a nested route or query string if supported, to ensure exact target preservation

7. **Make minimal production fixes if tests expose gaps**
   Only if needed:
   - fix ordering logic
   - normalize `PriorityScore` to integer `0..100`
   - ensure required fields are populated
   - ensure panel uses API-provided `NavigationTarget` directly for CTA navigation
   - improve component testability without changing behavior

8. **Keep changes aligned with existing architecture**
   - preserve tenant scoping
   - keep tests deterministic and isolated
   - follow current naming and fixture conventions
   - avoid introducing brittle timing-based assertions

# Validation steps
Run the smallest relevant scope first, then broader validation.

1. Build:
   - `dotnet build`

2. Run API tests:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Run web/component tests:
   - run the specific web test project if one exists
   - if component tests are added to an existing project, run that project explicitly
   - otherwise run full test suite:
     - `dotnet test`

4. Confirm test coverage behavior manually from assertions:
   - endpoint returns 3 to 5 items
   - descending `PriorityScore` ordering
   - required fields non-empty
   - `PriorityScore` integer and within 0..100
   - approvals/tasks/anomalies/finance alerts represented when seeded
   - panel renders cards with title, description, CTA
   - CTA navigates to exact `NavigationTarget`

5. If production fixes were required, verify no regressions by rerunning:
   - `dotnet test`

# Risks and follow-ups
- The repository may not yet have a dedicated Blazor component test project; if so, add the smallest viable test setup and keep dependencies minimal.
- Domain entities for anomalies or finance alerts may not yet exist under those exact names; map tests to the actual implemented source modules while preserving acceptance-criteria intent.
- If the endpoint derives `userId` from auth context rather than query input, adapt tests to the real contract instead of inventing parameters.
- If source metadata is not returned in `FocusItem`, use deterministic seeded content to prove cross-domain inclusion without changing the contract unless necessary.
- If current implementation returns fewer than 3 items because of sparse seeded data or top-N logic, ensure test fixtures create enough eligible records before changing production behavior.
- Follow-up recommendation: add a small contract test or snapshot-style DTO serialization test for `FocusItem` if the API contract is still evolving.