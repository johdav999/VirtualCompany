# Goal
Implement frontend test coverage for Finance settings UI states related to Fortnox connections, specifically covering connection state rendering, sync history rendering, permission-based behavior, and safe error messaging, in support of **TASK-33.4.4** under **US-33.4 Implement approval-backed outbound Fortnox write operations and end-to-end test coverage**.

The coding agent should add or update Blazor/web frontend tests so the Finance settings experience is verified for:
- connected vs disconnected vs reconnect/error states
- sync history list rendering and empty/error states
- permission-gated actions and visibility
- safe, user-facing error messages for OAuth/token/sync/API failures
- approval-gated outbound behavior as surfaced in the UI where applicable

Keep the implementation aligned with the existing .NET solution structure and current test conventions. Prefer minimal production changes unless tests reveal missing seams required for deterministic UI testing.

# Scope
In scope:
- Inspect the current Finance settings UI implementation in `src/VirtualCompany.Web`
- Identify existing Fortnox-related components/pages/view models/services used by Finance settings
- Add frontend-focused automated tests for UI behavior
- Add supporting test doubles/mocks/builders as needed
- Make small production refactors only if necessary to enable testability
- Ensure tests reflect tenant isolation and permission-sensitive rendering where the UI exposes those states
- Verify safe error translation in the UI, not raw backend/internal exception leakage

Out of scope unless strictly required to make tests pass:
- Large backend feature implementation
- New Fortnox integration behavior beyond what is needed for test seams
- Broad redesign of Finance settings UX
- Mobile app changes
- Rewriting unrelated test infrastructure

Acceptance criteria to map into tests where UI-relevant:
- OAuth success and failure paths
- token refresh success and failure
- sync idempotency presentation where surfaced in UI
- tenant isolation in rendered data/context
- API failure translation to safe user-facing messages
- approval gating visibility/behavior in Finance settings UI
- Finance settings UI behavior for permissions and connection states

# Files to touch
Start by locating the actual Finance settings implementation and existing test patterns, then update only the relevant files. Likely areas:

Production:
- `src/VirtualCompany.Web/**` Fortnox/Finance settings pages, components, dialogs, view models, service abstractions
- Potential shared DTO/view-state files in:
  - `src/VirtualCompany.Shared/**`
  - `src/VirtualCompany.Application/**` only if UI contracts are sourced there

Tests:
- `tests/**` existing web/frontend test project if present
- If web component tests live elsewhere, use the established location rather than creating a new pattern
- Possible candidates to inspect:
  - `tests/VirtualCompany.Api.Tests/**` for naming/style references only
  - any existing `*.Web.Tests`, `bUnit`, or Razor component test projects in the solution

Also inspect:
- `VirtualCompany.sln`
- `src/VirtualCompany.Web/VirtualCompany.Web.csproj`
- any test project `.csproj` that already references bUnit, xUnit, NUnit, Playwright, or similar

If no dedicated frontend test project exists, create the smallest appropriate one consistent with the repo conventions and add it to the solution.

# Implementation plan
1. **Discover current implementation and test stack**
   - Search for:
     - `Fortnox`
     - `Finance Settings`
     - `Settings`
     - connection status/sync history components
   - Determine whether the frontend is tested with:
     - bUnit for Blazor component tests
     - Playwright for end-to-end UI tests
     - xUnit/NUnit/MSTest conventions
   - Reuse existing fixtures, auth helpers, and permission/tenant test utilities.

2. **Map UI states to explicit test cases**
   Create a test matrix for the Finance settings surface. At minimum cover:

   **Connection states**
   - no active Fortnox connection renders disconnected state and connect CTA
   - active connection renders connected state with expected metadata
   - connection requiring re-auth/token issue renders degraded/error state
   - OAuth success callback/result state renders success feedback
   - OAuth failure renders safe error feedback

   **Sync history rendering**
   - sync history entries render in correct order with status/timestamps
   - empty sync history renders appropriate empty state
   - failed sync history entry renders safe summary, not raw exception details
   - idempotent/repeated sync outcome is rendered correctly if the UI exposes it

   **Permissions**
   - authorized finance/admin users can see/connect/manage actions
   - unauthorized users see hidden or disabled controls per current UX
   - approval-related actions/messages are only shown when permitted
   - tenant-scoped data only shows the active tenant’s connection/history

   **Error messaging**
   - token refresh failure maps to safe user-facing message
   - API failure maps to safe user-facing message
   - backend execution failure summary is displayed safely
   - no stack traces, raw Fortnox payloads, or sensitive internals are rendered

   **Approval gating**
   - where Finance settings surfaces outbound write approval requirements, verify the UI indicates approval-required state rather than implying immediate execution
   - if there is a pending approval/history indicator in settings, verify rendering

3. **Implement deterministic test setup**
   - Build reusable test data builders/fakes for:
     - Fortnox connection state DTO/view model
     - sync history items
     - permission sets/roles
     - tenant context
     - API/service responses for success/failure
   - Prefer mocking service abstractions consumed by the component/page rather than mocking deep infrastructure.
   - If the page depends on auth state or tenant context providers, create test helpers to inject them cleanly.

4. **Add component/frontend tests**
   - For Blazor components/pages, use bUnit-style rendering tests if available in the repo.
   - Assert on:
     - visible text
     - buttons/links enabled/disabled state
     - presence/absence of sections
     - safe error banners/messages
     - rendered sync history rows/items
   - Avoid brittle markup assertions; prefer semantic text/role/test-id selectors if available.
   - If test IDs are missing and needed, add minimal stable selectors in production markup.

5. **Refactor for testability only where needed**
   If the current UI is hard to test:
   - extract state mapping into a small presenter/view-state mapper
   - inject service abstractions instead of static calls
   - separate callback/query loading logic from rendering logic
   - add stable loading/error/empty-state markers

   Keep refactors small and behavior-preserving.

6. **Align tests with acceptance criteria**
   Ensure the final test suite clearly covers the UI-relevant portions of the story acceptance criteria:
   - OAuth success/failure
   - token refresh success/failure
   - sync idempotency rendering if exposed
   - tenant isolation
   - API failure translation
   - approval gating
   - Finance settings UI behavior

7. **Document any uncovered gaps**
   - If some acceptance criteria cannot be covered at frontend level because the UI does not yet expose the state, note that explicitly in code comments or the final summary.
   - Do not invent unsupported UX; test the actual intended UI behavior.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the relevant frontend/web test project(s):
   - `dotnet test`

3. If a new frontend test project was added, ensure:
   - it is included in `VirtualCompany.sln`
   - it restores and runs via `dotnet test`

4. Verify tests are deterministic:
   - no real network calls
   - no dependency on external Fortnox services
   - no time-sensitive assertions without controlled clocks/data

5. Confirm assertions specifically validate:
   - connection state rendering
   - sync history rendering
   - permission-based visibility/disabled states
   - safe error messaging
   - tenant isolation in displayed data
   - approval-gated messaging/behavior where surfaced

6. In your final implementation summary, include:
   - exact files changed
   - test cases added
   - any production refactors made for testability
   - any acceptance-criteria gaps that remain outside frontend-test scope

# Risks and follow-ups
- The repo may not yet have a dedicated frontend/component test project; if so, create one only after confirming no existing pattern exists.
- Finance settings UI may currently mix data loading and rendering, making tests brittle; use minimal refactoring to introduce test seams.
- Permission behavior may be enforced partly server-side; frontend tests should validate rendering/UX, not replace backend authorization tests.
- Some acceptance criteria are backend/integration focused; only cover the UI-observable aspects here and call out the rest.
- If tenant context is implicit/global in the current UI, tests may expose missing seams for tenant-aware rendering.
- If safe error translation is not yet implemented in the UI, add the smallest mapping layer necessary and note that broader consistency across the app may need a follow-up task.