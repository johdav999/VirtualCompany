# Goal

Implement backlog task **TASK-31.4.3 — Revise local setup and deployment docs for Gmail and Microsoft 365 OAuth redirect URI registration** for story **US-31.4 Update admin UI copy, tests, and setup documentation for stable mailbox redirect URIs**.

Deliver a complete change that updates:
- admin/app settings UI copy for local development redirect URIs
- automated tests covering stable callback URL behavior and mailbox OAuth callback handling
- developer/setup documentation for dev and production redirect URI registration

The implementation must satisfy all acceptance criteria exactly.

# Scope

In scope:
- Update the app settings/admin UI so local development shows these exact stable redirect URIs:
  - Gmail: `http://localhost:5301/api/mailbox-connections/gmail/callback`
  - Microsoft 365: `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
- Remove any UI copy that tells users to register company-specific mailbox callback URLs.
- Ensure callback URL generation for Gmail and Microsoft 365 start flows is stable and environment-based, not tenant/company-specific.
- Add or update automated tests for:
  - stable callback URL generation for Gmail and Microsoft 365 start flows
  - callback success with valid protected state
  - rejection of invalid state
  - rejection of expired state
  - legacy route compatibility
  - prevention of cross-tenant completion
- Update developer documentation to list dev and production redirect URIs and explicitly state that one redirect URI per provider per environment is required.

Out of scope:
- Changing provider credentials/secrets handling beyond what is needed for tests/docs
- Reworking the broader OAuth architecture unless required to satisfy acceptance criteria
- Mobile app changes unless shared copy/components force it
- Introducing new providers beyond Gmail and Microsoft 365

# Files to touch

Start by locating the actual implementation points before editing. Likely areas include:

- `src/VirtualCompany.Web/**`
  - app settings/admin UI pages/components
  - mailbox connection setup copy
  - any shared view models used to display redirect URIs
- `src/VirtualCompany.Api/**`
  - mailbox connection start/callback endpoints
  - redirect URI generation logic
  - legacy route handling if implemented at API layer
- `src/VirtualCompany.Application/**`
  - mailbox OAuth orchestration/services
  - protected state generation/validation
  - tenant validation logic
- `src/VirtualCompany.Infrastructure/**`
  - provider-specific OAuth configuration/adapters
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for mailbox OAuth start/callback flows
- `README.md`
  - local setup and deployment/setup guidance
- `docs/**`
  - any developer setup, deployment, or integration docs related to mailbox OAuth
  - especially any existing docs that mention callback URL registration
- If present, also inspect:
  - mailbox connection DTOs/view models
  - configuration classes/options for public base URL / app URL
  - existing tests for Gmail/Microsoft 365 mailbox connections

Do not edit archive/reference files unless absolutely necessary:
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan

1. **Discover current mailbox OAuth implementation**
   - Search for:
     - `mailbox-connections`
     - `gmail/callback`
     - `microsoft365/callback`
     - `redirect uri`
     - `redirectUri`
     - `state`
     - `legacy`
   - Identify:
     - where redirect URIs are generated
     - whether current generation includes tenant/company-specific paths or query values
     - where UI copy is sourced
     - what existing tests already cover

2. **Confirm the intended stable redirect URI design**
   - Redirect URIs must be provider-specific and environment-specific, but not company-specific.
   - Local development values must be exactly:
     - `http://localhost:5301/api/mailbox-connections/gmail/callback`
     - `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
   - If current logic derives callback URLs from tenant-specific routes, refactor to use a stable API callback endpoint per provider.
   - Preserve tenant context through protected state, not through the callback path.

3. **Update redirect URI generation logic**
   - Centralize callback URL generation if it is duplicated.
   - Use configured application/API base URL for environment-specific generation.
   - Ensure Gmail start flow uses the stable Gmail callback endpoint.
   - Ensure Microsoft 365 start flow uses the stable Microsoft 365 callback endpoint.
   - Avoid embedding company identifiers in callback URLs.
   - Keep legacy route compatibility if older callback routes still need to resolve.

4. **Update protected state validation behavior if needed**
   - Verify callback completion depends on protected state carrying the required tenant/company context.
   - Ensure callback succeeds only when:
     - state is valid
     - state is unexpired
     - state tenant/company matches the completion context rules
   - Ensure invalid, tampered, expired, or cross-tenant state is rejected safely.
   - Do not rely on callback URL path tenancy for authorization.

5. **Update admin/app settings UI copy**
   - Find the UI that displays mailbox provider setup instructions.
   - Replace any company-specific callback guidance with stable environment-based guidance.
   - For local development, display the exact URIs from acceptance criteria.
   - Ensure copy says effectively:
     - one redirect URI per provider per environment
     - dev and production each have their own provider callback URI
   - Remove wording that implies registering a separate callback URL per company/workspace/tenant.

6. **Add or update automated tests for start flow redirect URI generation**
   - Add tests for Gmail start flow callback URL generation.
   - Add tests for Microsoft 365 start flow callback URL generation.
   - Assert the generated redirect URI is stable and matches the configured environment base URL plus provider callback path.
   - Assert no tenant/company-specific path segments are included.

7. **Add or update automated tests for callback handling**
   - Cover:
     - success with valid protected state
     - rejection of invalid state
     - rejection of expired state
     - legacy route compatibility
     - prevention of cross-tenant completion
   - Prefer API/integration tests if the behavior spans routing + state protection + tenant validation.
   - If there are existing unit tests around state services, keep them and add integration coverage where missing.

8. **Update documentation**
   - Update `README.md` and/or the most relevant docs page for developer setup.
   - Document both dev and production redirect URIs for Gmail and Microsoft 365.
   - Explicitly state:
     - one redirect URI per provider per environment is required
     - redirect URIs are stable and not company-specific
   - Include local development examples using `http://localhost:5301`.
   - If production hostname is configurable, document the pattern clearly, e.g.:
     - `https://<app-host>/api/mailbox-connections/gmail/callback`
     - `https://<app-host>/api/mailbox-connections/microsoft365/callback`
   - Remove outdated instructions that mention per-company registration.

9. **Keep changes cohesive and minimal**
   - Prefer updating existing services/components/docs rather than introducing parallel implementations.
   - If you introduce a shared helper for callback URI generation, place it in the appropriate application/API layer and use it consistently across providers.

# Validation steps

1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`
   - If the full suite is too slow, at minimum run:
     - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

2. **Verify UI copy**
   - Confirm the app settings/admin UI displays:
     - Gmail: `http://localhost:5301/api/mailbox-connections/gmail/callback`
     - Microsoft 365: `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
   - Confirm there is no copy instructing users to register company-specific callback URLs.

3. **Verify redirect URI generation**
   - Confirm automated tests prove Gmail and Microsoft 365 start flows generate stable callback URLs.
   - Confirm generated URLs are environment-based and not tenant-specific.

4. **Verify callback behavior**
   - Confirm tests cover and pass for:
     - valid protected state success
     - invalid state rejection
     - expired state rejection
     - legacy route compatibility
     - cross-tenant completion prevention

5. **Verify documentation**
   - Confirm docs list:
     - dev redirect URIs
     - production redirect URI patterns
     - statement that one redirect URI per provider per environment is required
   - Confirm outdated company-specific registration guidance is removed.

6. **Summarize in final change notes**
   - Include:
     - files changed
     - where redirect URI generation now lives
     - tests added/updated
     - docs updated
     - any assumptions about production host configuration

# Risks and follow-ups

- **Risk: callback URL generation is duplicated**
  - If multiple layers generate provider callback URLs independently, inconsistencies may remain. Consolidate if found.

- **Risk: legacy route behavior is unclear**
  - If legacy callback routes exist but are untested, preserve them and add explicit compatibility tests before refactoring.

- **Risk: tenant context may currently depend on route structure**
  - If old behavior encoded tenant/company in the callback path, ensure protected state fully replaces that dependency before removing assumptions.

- **Risk: docs may exist in multiple places**
  - Search broadly to avoid leaving contradictory setup instructions in README, docs pages, or inline UI help text.

- **Risk: environment base URL may be ambiguous**
  - If the app has separate web and API origins in some environments, document and use the actual provider-facing callback origin consistently.

Follow-ups if needed:
- Add a small shared test helper for mailbox OAuth state creation/expiration scenarios if current tests are repetitive.
- Consider a dedicated documentation page for external OAuth provider registration if setup guidance is currently fragmented.
- If production hostnames vary by deployment topology, add a config reference explaining how the stable callback base URL is derived.