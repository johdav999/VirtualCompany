# Goal
Implement automated integration tests for **TASK-5.4.4 — Write integration tests for filter combinations and audit navigation** for **US-5.4 Filtering, drill-down, and audit deep linking**.

The coding agent should add or extend integration tests that verify:

- activity feed filtering by **agent, department, task, event type, status, and timeframe**
- combined filters return only results matching **all selected filters**
- applying and clearing filters updates the **URL query string**
- the current filtered view can be reloaded from the URL without losing state
- clicking an activity item opens a detail view with:
  - raw payload
  - summary
  - correlation links
  - audit deep link when available
- audit deep links navigate to the **correct audit detail page for the same tenant**
- at least the **top 5 filter combinations** are covered
- the **audit deep-link flow** is covered end to end at the integration-test level

Use the existing solution structure and conventions. Prefer extending current test infrastructure over inventing a parallel pattern.

# Scope
In scope:

- Inspect the existing web and test projects to find:
  - current activity feed pages/components
  - audit detail pages/routes
  - existing integration/UI test patterns
  - test host, seeded data, auth/tenant setup helpers
- Add integration tests in the most appropriate existing test project, likely:
  - `tests/VirtualCompany.Api.Tests`
  - and/or any existing web integration test location if present in the repo
- Seed or arrange tenant-scoped test data covering:
  - multiple agents
  - multiple departments
  - multiple tasks
  - multiple event types
  - multiple statuses
  - multiple timestamps/timeframes
  - events with and without linked audit records
  - at least two tenants to verify tenant isolation in navigation
- Verify URL/query-string behavior for filter apply/clear/reload
- Verify detail-view rendering and audit-link navigation behavior

Out of scope unless required by failing tests:

- large production refactors
- redesigning feed or audit UI
- changing domain behavior beyond minimal fixes needed to make tests pass
- adding a new test framework if an existing one already supports this scenario

If the current implementation is missing small hooks needed for testability, add the smallest possible changes and keep them production-safe.

# Files to touch
Start by inspecting these areas, then update the minimal necessary set:

- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
- existing test files under `tests/VirtualCompany.Api.Tests/**`
- `src/VirtualCompany.Web/**` for:
  - activity feed page/component
  - filter state/query-string sync
  - detail drawer/page/modal
  - audit deep-link rendering
- `src/VirtualCompany.Api/**` if integration tests exercise HTTP endpoints directly
- `src/VirtualCompany.Application/**` if query/filter behavior needs small corrections
- `src/VirtualCompany.Infrastructure/**` if test data setup or repository filtering needs fixes

Likely file categories to touch:

- integration test class(es) for activity feed and audit navigation
- shared test fixtures/factories/builders
- seeded test data helpers
- possibly route/query parsing helpers in web layer
- possibly page/component test IDs or stable selectors if UI-level integration tests require them

Do not broaden changes beyond what is needed for this task.

# Implementation plan
1. **Discover existing implementation and test strategy**
   - Search the repo for:
     - activity feed
     - audit trail / audit detail
     - query-string handling
     - integration tests using `WebApplicationFactory`, bUnit, Playwright, or similar
   - Reuse the dominant pattern already present.
   - Identify the exact route names and parameter names used by the feed and audit detail pages.

2. **Map acceptance criteria to concrete test cases**
   Create a compact test matrix covering at least these top 5 combinations, adjusting to actual supported values in code:
   - agent + timeframe
   - department + status
   - task + event type
   - agent + department + status
   - event type + status + timeframe
   Also add:
   - all-filters-combined case if practical
   - clear-filters case
   - reload-from-query-string case
   - detail-view-with-audit-link case
   - detail-view-without-audit-link case
   - cross-tenant audit-link safety case

3. **Prepare deterministic tenant-scoped test data**
   - Seed data for at least:
     - Tenant A
     - Tenant B
   - For Tenant A, create activity events with varied combinations across:
     - agent
     - department
     - task
     - event type
     - status
     - timestamps
   - Ensure some events have linked audit records and some do not.
   - Ensure at least one similarly shaped audit/event record exists in Tenant B so tests can prove same-tenant navigation/isolation.
   - Use explicit IDs/names/timestamps to avoid flaky assertions.

4. **Implement filter combination integration tests**
   Depending on the existing test style:
   - If API/query integration tests:
     - call the feed endpoint with query-string combinations
     - assert returned items all satisfy every selected filter
   - If web/component integration tests:
     - render/navigate to the feed page
     - apply filters through the UI
     - assert visible results match expected records only
   In either case, verify conjunction semantics: selected filters are combined with logical AND.

5. **Implement URL query-string synchronization tests**
   Add tests that verify:
   - applying filters updates the URL with expected query parameters
   - clearing filters removes or resets the relevant query parameters
   - navigating directly to a URL with filter query parameters restores the same filtered view
   Keep assertions resilient to parameter ordering if ordering is not guaranteed.

6. **Implement activity detail-view tests**
   Add tests for clicking/selecting an activity item and asserting the detail view shows:
   - raw payload
   - summary
   - correlation links
   - audit deep link when an audit record exists
   Also verify the audit link is absent or disabled when no audit record exists, based on actual UX behavior.

7. **Implement audit deep-link navigation tests**
   Add an integration test that:
   - opens an activity item with a linked audit record
   - activates the audit deep link
   - verifies navigation lands on the correct audit detail route/page
   - verifies the audit detail corresponds to the same tenant and expected audit record
   Add a tenant-isolation assertion so a record from another tenant cannot be reached through the link flow.

8. **Add minimal production fixes only if tests expose gaps**
   If tests fail because of implementation defects, make the smallest targeted fixes, such as:
   - missing query-string sync on filter apply/clear
   - incorrect filter composition
   - missing tenant scoping in audit lookup/navigation
   - unstable or inaccessible selectors/test hooks
   Keep behavior aligned with the story and acceptance criteria.

9. **Keep tests maintainable**
   - Use helper methods/builders for repetitive setup
   - Prefer descriptive test names tied to acceptance criteria
   - Avoid brittle assertions on incidental markup
   - Document any assumptions in comments only where necessary

# Validation steps
1. Inspect and run the relevant tests locally:
   - `dotnet test`
2. If the full suite is too slow during iteration, run the targeted test project first:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
3. Confirm the new tests cover:
   - at least 5 filter combinations
   - URL apply/clear/reload behavior
   - detail view rendering
   - audit deep-link flow
   - tenant-safe navigation
4. If production code was changed, also run:
   - `dotnet build`
5. In the final result, summarize:
   - which test files were added/updated
   - which combinations were covered
   - any production fixes made to satisfy the tests
   - any remaining gaps if full UI-level navigation is not currently supported by existing test infrastructure

# Risks and follow-ups
- The repo may not yet have a true browser-level integration framework. If so, use the strongest existing integration level available and note the limitation clearly.
- Query-string synchronization may live in Blazor component code that is harder to validate without component/in-browser tests; prefer existing patterns rather than introducing a heavy new framework unless already present.
- Test data can become flaky if timestamps rely on `DateTime.UtcNow`; use fixed timestamps or injectable clocks where possible.
- Tenant isolation is critical; ensure tests do not accidentally pass because only one tenant is seeded.
- If selectors are unstable, add minimal `data-testid` or equivalent hooks only where necessary.
- Follow-up if needed: add broader coverage for all filter permutations, pagination interactions, and mobile/web parity once the core integration path is stable.