# Goal

Implement backlog task **TASK-31.4.2** for story **US-31.4 Update admin UI copy, tests, and setup documentation for stable mailbox redirect URIs** by adding automated **integration and security tests** that verify:

- stable callback URL generation for Gmail and Microsoft 365 start flows
- callback success with valid protected state
- rejection of invalid or expired state
- compatibility with legacy callback routes
- prevention of cross-tenant callback completion

Also update any necessary test fixtures/helpers so the tests reflect the intended stable redirect URI behavior and tenant isolation guarantees.

# Scope

In scope:

- Add or extend backend integration tests in the existing `.NET` test project(s) for mailbox connection OAuth start/callback flows.
- Cover both providers:
  - Gmail
  - Microsoft 365
- Verify local-development stable callback URIs are used in generated start flow output.
- Verify callback handling behavior for:
  - valid protected state
  - invalid protected state
  - expired protected state
  - legacy callback route compatibility
  - cross-tenant completion attempts
- Reuse existing app/test infrastructure where possible.
- Add minimal test-only helpers/fixtures if needed.

Out of scope unless required to make tests pass:

- Broad refactors of mailbox connection architecture
- UI redesign
- unrelated OAuth/provider changes
- production deployment changes

If tests expose implementation gaps, make the smallest production changes necessary to satisfy the acceptance criteria without expanding scope.

# Files to touch

Start by inspecting and likely updating only the smallest relevant set among these areas:

- `tests/VirtualCompany.Api.Tests/**/*`
- `src/VirtualCompany.Api/**/*Mailbox*`
- `src/VirtualCompany.Api/**/*Gmail*`
- `src/VirtualCompany.Api/**/*Microsoft*`
- `src/VirtualCompany.Api/**/*OAuth*`
- `src/VirtualCompany.Application/**/*Mailbox*`
- `src/VirtualCompany.Infrastructure/**/*Mailbox*`
- `src/VirtualCompany.Web/**/*AppSettings*`
- `README.md`
- any existing docs/setup file that already documents mailbox redirect URIs

Prefer modifying existing test classes/files if mailbox connection integration tests already exist. Create new focused test files only if coverage is currently missing.

# Implementation plan

1. **Discover current implementation and test surface**
   - Find the mailbox connection start and callback endpoints for Gmail and Microsoft 365.
   - Identify:
     - stable callback routes
     - any legacy callback routes
     - state protection/unprotection mechanism
     - tenant/company resolution during callback completion
   - Locate existing integration test patterns, test server factory, auth helpers, and seeded tenant/company fixtures.

2. **Map acceptance criteria to concrete test cases**
   Create a concise matrix before coding. At minimum include:
   - Gmail start flow returns/uses stable callback URI:
     - `http://localhost:5301/api/mailbox-connections/gmail/callback`
   - Microsoft 365 start flow returns/uses stable callback URI:
     - `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
   - Callback succeeds when state is valid and protected correctly.
   - Callback rejects malformed/tampered state.
   - Callback rejects expired state.
   - Legacy callback route still completes successfully.
   - Cross-tenant callback completion is blocked when state/tenant context does not match the completing tenant/user/company.

3. **Implement integration tests for start flow URL generation**
   - Add tests that invoke the Gmail and Microsoft 365 start endpoints through the real API pipeline.
   - Assert the generated authorization request or redirect target contains the expected stable callback URI.
   - Do not assert brittle provider-specific query ordering; parse query parameters and assert by key/value.
   - Ensure tests are environment-aware only where necessary; local-dev expectation should match acceptance criteria exactly.

4. **Implement callback success and failure-path tests**
   - Use the real state protection mechanism if possible rather than mocking internals.
   - Generate a valid protected state through the same application services used by production, then invoke callback endpoints with realistic query parameters.
   - Add negative tests for:
     - invalid/tampered state payload
     - expired state payload
   - Assert appropriate HTTP result and safe failure behavior:
     - no connection completion
     - no tenant leakage
     - no unintended side effects persisted

5. **Implement legacy route compatibility tests**
   - Identify the old callback route(s).
   - Add tests proving the legacy route still accepts a valid callback and completes through the same underlying flow.
   - If legacy routes redirect internally, assert the observable success behavior rather than implementation details.

6. **Implement tenant isolation/security tests**
   - Seed or use two distinct companies/tenants.
   - Start a mailbox connection flow for tenant A.
   - Attempt to complete the callback under tenant B context or with mismatched tenant resolution.
   - Assert completion is denied and no mailbox connection is created/updated for the wrong tenant.
   - Verify tenant A data is not exposed in the response.

7. **Add minimal production fixes only if tests reveal gaps**
   If needed, make narrowly scoped changes to:
   - callback route registration
   - state validation/expiration handling
   - tenant/company ownership checks during callback completion
   - stable redirect URI generation
   Keep changes small and aligned to the story.

8. **Keep tests maintainable**
   - Prefer helper methods for:
     - parsing redirect URLs
     - creating valid/expired/tampered state
     - seeding tenant/company/mailbox connection setup
   - Name tests in behavior-first style, e.g.:
     - `Gmail_StartFlow_UsesStableCallbackUri`
     - `Microsoft365_Callback_RejectsExpiredState`
     - `LegacyGmailCallbackRoute_AllowsCompletion`
     - `Callback_RejectsCrossTenantCompletion`

9. **Documentation alignment check**
   - If this task’s test work requires doc updates already covered by the story acceptance criteria, update the existing setup docs/README with:
     - dev redirect URIs
     - production redirect URI guidance
     - one redirect URI per provider per environment
   - Keep doc edits minimal and factual.

# Validation steps

Run these after implementation:

1. Build and run tests:
   - `dotnet build`
   - `dotnet test`

2. If the mailbox tests are in a large suite, also run targeted tests if supported, for example:
   - `dotnet test --filter Mailbox`
   - or provider-specific test filters based on actual class names

3. Manually verify assertions in code cover all acceptance criteria:
   - stable Gmail callback URI in local dev
   - stable Microsoft 365 callback URI in local dev
   - no UI/doc copy instructs company-specific callback URLs
   - valid protected state succeeds
   - invalid state rejected
   - expired state rejected
   - legacy route works
   - cross-tenant completion prevented

4. Confirm tests are not brittle:
   - no dependence on query parameter ordering
   - no dependence on wall-clock timing without controllable clock/tolerance
   - no hidden coupling to a single seeded tenant unless intentional

# Risks and follow-ups

- **Risk: existing tests may mock too much**
  - Prefer full integration coverage through the API pipeline; otherwise security-sensitive behavior may be missed.

- **Risk: state expiration tests can be flaky**
  - Use a controllable clock/test time provider if available. If not, create clearly expired state rather than waiting for timeout.

- **Risk: tenant isolation may be enforced in multiple layers**
  - Validate both HTTP response behavior and persistence side effects to ensure no partial completion occurs.

- **Risk: legacy route behavior may be ambiguous**
  - Preserve backward compatibility in tests based on externally observable behavior, not internal routing assumptions.

- **Follow-up**
  - If not already present, consider adding a dedicated mailbox OAuth test helper/fixture for provider-agnostic callback testing to reduce duplication across Gmail and Microsoft 365 cases.