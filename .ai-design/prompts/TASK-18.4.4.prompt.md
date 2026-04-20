# Goal
Implement end-to-end test coverage for **TASK-18.4.4**: verify executive cockpit finance widget behavior, finance alert opening, finance action visibility, deep-link navigation, and role/policy-gated action triggering for **US-18.4 ST-FUI-204 — Cash position cockpit widgets and finance action entry points**.

The coding agent should add or extend automated E2E tests so they validate the user-visible behavior and backend integration points for finance cockpit widgets and finance actions without introducing new product behavior beyond what is needed for testability.

# Scope
Focus only on test implementation and minimal supporting changes required to make the tests reliable and maintainable.

Cover these acceptance criteria in E2E form:

1. **Cash position finance widget**
   - Displays current cash position value
   - Displays trend indicator
   - Displays last refreshed timestamp

2. **Runway visualization**
   - Displays current runway estimate
   - Applies threshold-based status styling for:
     - healthy
     - warning
     - critical

3. **Finance alert opening**
   - Low-cash alerts open a finance-specific detail panel or page
   - Detail view shows:
     - alert summary
     - contributing factors
     - links to detailed finance views

4. **Deep-link navigation**
   - Finance widgets deep-link appropriately to:
     - finance workspace
     - anomaly workbench
     - cash detail page

5. **Finance actions visible in UI**
   - Review invoice
   - Inspect anomaly
   - View cash position
   - Open finance summary

6. **Role/policy-gated visibility and orchestration**
   - Finance action entry points shown only when current user passes role/policy checks
   - Triggering actions calls existing backend orchestration endpoints

Out of scope unless strictly necessary:
- Reworking finance feature implementation
- Large UI refactors
- New backend business logic
- New product requirements not stated above

# Files to touch
Prefer the smallest possible set, but inspect and update the relevant test and UI files. Likely areas:

- `tests/VirtualCompany.Api.Tests/`  
  - Add API/integration coverage only if needed for orchestration endpoint verification or fixture support

- E2E/UI test project location if present in repo  
  - Search for Playwright, bUnit, Selenium, or existing web test harness
  - If no dedicated E2E project exists, place tests in the established web/integration test location rather than inventing an entirely new structure unless unavoidable

- `src/VirtualCompany.Web/`
  - Finance cockpit pages/components
  - Alert/detail panel/page components
  - Action button/menu components
  - Test IDs or accessibility attributes, only if needed for stable selectors

- `src/VirtualCompany.Api/`
  - Only minimal test-support changes if orchestration endpoint observability or mockability is required

- Shared test infrastructure / fixtures / seed data
  - Any existing test data builders, fake auth, policy fixtures, or seeded finance dashboard data

Before coding, identify:
- Existing finance cockpit widget implementation
- Existing alert detail route/panel implementation
- Existing authorization/policy checks
- Existing orchestration endpoint(s) used by finance actions
- Existing test conventions and selector strategy

# Implementation plan
1. **Discover existing test stack and feature implementation**
   - Inspect solution for current UI/E2E testing approach.
   - Search for:
     - cockpit/dashboard tests
     - finance-related components/pages
     - alert/inbox tests
     - role/policy visibility tests
     - orchestration endpoint tests
   - Reuse existing fixtures, auth helpers, seeded tenant/company data, and naming conventions.

2. **Map acceptance criteria to concrete test scenarios**
   Create a compact scenario matrix covering:
   - Finance widget renders value, trend, timestamp
   - Runway widget/status styling for healthy
   - Runway widget/status styling for warning
   - Runway widget/status styling for critical
   - Low-cash alert opens finance detail panel/page with required content
   - Widget deep-link to finance workspace
   - Widget/alert deep-link to anomaly workbench
   - Widget/alert deep-link to cash detail page
   - Authorized user sees all finance actions
   - Unauthorized user does not see finance actions
   - Authorized action click invokes orchestration endpoint
   - Unauthorized user cannot trigger hidden/blocked action

3. **Add stable selectors only where necessary**
   - Prefer accessible roles, labels, and visible text first.
   - If UI is too dynamic or ambiguous, add minimal `data-testid` attributes to:
     - cash position widget
     - runway widget
     - low-cash alert item
     - finance detail panel/page
     - each finance action entry point
   - Keep selector additions localized and non-invasive.

4. **Prepare deterministic test data**
   - Seed or mock finance cockpit data for:
     - cash position value
     - trend indicator
     - last refreshed timestamp
     - runway estimate
     - runway status variants
     - low-cash alert summary and contributing factors
     - deep-link targets
   - Seed users with at least:
     - finance-authorized role/policy
     - non-authorized role/policy
   - Ensure tenant-scoped data is isolated and repeatable.

5. **Implement widget rendering tests**
   - Navigate to executive cockpit.
   - Assert finance cash widget shows:
     - formatted value
     - trend indicator
     - refreshed timestamp
   - Assert runway widget shows estimate and expected status styling.
   - For status styling, verify via semantic class/attribute/state rather than brittle CSS snapshots if possible.

6. **Implement alert opening test**
   - From cockpit or alert list, open a low-cash alert.
   - Assert finance-specific detail panel/page appears.
   - Verify presence of:
     - alert summary
     - contributing factors
     - links to detailed finance views
   - If implementation supports either panel or page, write assertions flexible enough to validate the intended container without overfitting.

7. **Implement deep-link navigation tests**
   - Click finance widget/action links and verify navigation to:
     - finance workspace
     - anomaly workbench
     - cash detail page
   - Assert route, heading, or unique page marker for each destination.
   - Avoid relying only on URL if the app uses client-side routing; also assert destination content.

8. **Implement finance action visibility tests**
   - For authorized user:
     - assert Review invoice, Inspect anomaly, View cash position, Open finance summary are visible and enabled as appropriate
   - For unauthorized user:
     - assert these actions are absent or disabled according to current product behavior
   - Match existing authorization UX patterns in the app; do not invent a new hidden-vs-disabled rule unless already implemented.

9. **Implement orchestration trigger verification**
   - For at least one or more finance actions, verify clicking the action triggers the existing backend orchestration endpoint.
   - Use the project’s existing approach:
     - network interception
     - test server spy/fake
     - API assertion
     - audit/log side effect if already standard
   - Confirm request shape at a high-value level only:
     - correct endpoint hit
     - expected action identifier or payload marker
     - success path handled in UI
   - Do not replace real orchestration behavior unless the test harness already uses mocks/fakes.

10. **Keep tests maintainable**
   - Use page objects/helpers only if already standard in repo.
   - Group tests by feature area, e.g. finance cockpit / finance actions.
   - Name tests by business behavior, not implementation detail.
   - Avoid flaky timing; wait on stable UI states, network completion, or known page markers.

11. **Add minimal support code if required**
   - If current code lacks testability hooks, add only:
     - stable selectors
     - deterministic seed data
     - fake auth/policy fixture wiring
     - endpoint spy hooks in test environment
   - Do not broaden production code changes beyond what tests need.

12. **Document assumptions in code comments or test setup**
   - If a destination is implemented as panel vs page, note the expected behavior.
   - If policy visibility differs by role and policy combination, encode that explicitly in fixtures.

# Validation steps
Run the smallest relevant validation first, then broader checks.

1. Restore/build:
   - `dotnet build`

2. Run targeted tests for the modified test project(s):
   - `dotnet test`

3. If there is a dedicated web/E2E test command in the repo, run that as well.

4. Manually verify, if needed, that the implemented tests cover:
   - cash widget content
   - runway status variants
   - low-cash alert opening
   - deep-link destinations
   - action visibility for authorized vs unauthorized users
   - orchestration endpoint invocation on action trigger

5. Ensure tests are deterministic:
   - no arbitrary sleeps
   - no dependency on live external services
   - tenant/user/policy setup isolated per test or fixture

6. In the final work summary, include:
   - where tests were added
   - what scenarios are covered
   - any minimal production code changes made for testability
   - any acceptance criteria that could not be fully automated and why

# Risks and follow-ups
- **Unknown E2E framework in repo**  
  Follow existing conventions; if none exist, prefer the least disruptive approach and note the gap.

- **UI may not expose stable selectors**  
  Add minimal `data-testid` or accessibility improvements only where necessary.

- **Role vs policy behavior may be split across frontend and backend**  
  Ensure tests validate actual user-visible gating and not just mocked frontend state.

- **Orchestration endpoint verification may be hard to observe**  
  Reuse existing test server/mocking infrastructure; if absent, add a narrow test hook rather than broad refactoring.

- **Panel vs page ambiguity for alert details**  
  Write assertions against the intended finance detail experience using stable markers, not brittle DOM structure.

- **Styling assertions can be brittle**  
  Prefer semantic state markers, classes, or attributes that represent healthy/warning/critical instead of pixel/CSS snapshot checks.

- **Potential follow-up if gaps are found**
  - Add shared finance test fixtures/builders
  - Standardize selector strategy for cockpit widgets
  - Add explicit test environment support for policy/auth permutations
  - Add broader regression coverage for other cockpit widgets if this pattern is repeated