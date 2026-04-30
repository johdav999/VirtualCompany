# Goal

Implement backlog task **TASK-31.4.1 — Update mailbox integration settings UI copy to show stable provider callback URLs** for story **US-31.4 Update admin UI copy, tests, and setup documentation for stable mailbox redirect URIs**.

Deliver a focused change that updates the admin/app settings mailbox integration UI, related automated tests, and developer documentation so the product consistently communicates and validates **stable provider redirect URIs per environment**, not company-specific callback URLs.

# Scope

In scope:

- Update the mailbox integration settings UI copy to display these exact local-development redirect URIs:
  - **Gmail:** `http://localhost:5301/api/mailbox-connections/gmail/callback`
  - **Microsoft 365:** `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
- Remove or replace any UI wording that suggests users must register tenant/company-specific mailbox callback URLs.
- Ensure backend/start-flow behavior and tests reflect stable callback URL generation for Gmail and Microsoft 365.
- Add or update automated tests covering:
  - stable callback URL generation for Gmail start flow
  - stable callback URL generation for Microsoft 365 start flow
  - callback success with valid protected state
  - rejection of invalid protected state
  - rejection of expired protected state
  - legacy route compatibility
  - prevention of cross-tenant completion
- Update developer-facing documentation to list:
  - dev redirect URIs
  - production redirect URIs
  - explicit guidance that **one redirect URI per provider per environment is required**

Out of scope:

- Reworking the full mailbox OAuth architecture unless required to satisfy tests/acceptance criteria.
- Changing unrelated integration providers.
- Broad UI redesign beyond the mailbox settings copy.
- Refactoring unrelated auth, tenant, or routing systems.

# Files to touch

Start by locating the actual implementation points before editing. Expect to touch files in these areas:

- **Web UI / Blazor app**
  - mailbox integration settings page/component
  - any shared copy/resource/constants used by that page
- **API / application layer**
  - mailbox connection start-flow URL generation
  - callback endpoints and legacy route handling if tests reveal gaps
  - protected state validation logic if needed for acceptance coverage
- **Tests**
  - API integration tests for mailbox connection start/callback flows
  - UI/component tests if present for settings copy rendering
- **Docs**
  - `README.md`
  - integration/setup docs if separate
  - possibly `docs/...` if mailbox setup documentation already exists

Likely search terms:

- `mailbox-connections`
- `gmail`
- `microsoft365`
- `callback`
- `redirect uri`
- `redirect_uri`
- `state`
- `App settings`
- `Mailbox`
- `legacy route`

# Implementation plan

1. **Inspect current implementation**
   - Find the mailbox integration settings UI that currently displays provider setup instructions.
   - Identify how redirect URIs are currently generated or described.
   - Find the Gmail and Microsoft 365 OAuth start endpoints and callback endpoints.
   - Find existing tests around mailbox connection flows and state protection.
   - Find current setup documentation for mailbox integrations.

2. **Update UI copy to stable redirect URIs**
   - Replace any company-specific or tenant-specific callback URL examples with stable provider callback URLs.
   - Ensure the local-development values shown in the UI are exactly:
     - `http://localhost:5301/api/mailbox-connections/gmail/callback`
     - `http://localhost:5301/api/mailbox-connections/microsoft365/callback`
   - Update surrounding instructional text so it clearly says users register a single redirect URI per provider per environment, not per company.
   - Keep wording concise and admin-oriented.

3. **Align redirect URI generation with stable provider callbacks**
   - Verify the Gmail start flow uses the stable callback route above when generating provider authorization requests in local development.
   - Verify the Microsoft 365 start flow uses the stable callback route above when generating provider authorization requests in local development.
   - If current code derives callback URLs from tenant/company-specific paths, replace that behavior with provider-stable routes while preserving tenant context in protected state.
   - Preserve multi-tenant isolation by keeping tenant/company identity in protected state or equivalent server-side correlation, not in the redirect URI path.

4. **Preserve and verify callback security behavior**
   - Ensure callback completion succeeds only when protected state is valid and unexpired.
   - Ensure invalid or tampered state is rejected safely.
   - Ensure expired state is rejected safely.
   - Ensure cross-tenant completion is prevented even though the callback route is now stable/shared.
   - If legacy callback routes exist, keep them compatible if acceptance criteria require legacy route compatibility.

5. **Add or update automated tests**
   - Add tests that assert Gmail start flow generates the stable Gmail callback URL.
   - Add tests that assert Microsoft 365 start flow generates the stable Microsoft 365 callback URL.
   - Add/confirm callback tests for:
     - valid protected state success
     - invalid state rejection
     - expired state rejection
     - legacy route compatibility
     - cross-tenant completion prevention
   - Prefer existing test patterns and fixtures in `tests/VirtualCompany.Api.Tests`.

6. **Update developer documentation**
   - Document mailbox redirect URIs for development and production.
   - Include both providers explicitly.
   - State clearly: **one redirect URI per provider per environment is required**.
   - If production hostnames are environment-configured, document them using the project’s existing configuration conventions rather than inventing hardcoded domains. If no production hostname exists in repo docs, add a placeholder/example clearly marked as environment-specific.

7. **Keep changes minimal and coherent**
   - Avoid introducing new abstractions unless repeated logic clearly warrants it.
   - Reuse existing route/configuration helpers where possible.
   - Ensure naming and copy are consistent across UI, tests, and docs.

# Validation steps

Run and verify at minimum:

1. **Code search/manual review**
   - Confirm no remaining UI copy instructs users to register company-specific mailbox callback URLs.
   - Confirm the displayed local dev URIs exactly match acceptance criteria.

2. **Build**
   - `dotnet build`

3. **Tests**
   - `dotnet test`

4. **Targeted verification**
   - Inspect test assertions for Gmail start flow redirect URI.
   - Inspect test assertions for Microsoft 365 start flow redirect URI.
   - Verify callback tests cover:
     - valid protected state success
     - invalid state rejection
     - expired state rejection
     - legacy route compatibility
     - cross-tenant completion prevention

5. **Documentation review**
   - Confirm docs list:
     - dev Gmail redirect URI
     - dev Microsoft 365 redirect URI
     - production redirect URI guidance for both providers
     - statement that one redirect URI per provider per environment is required

6. **If runnable locally**
   - Open the app settings/admin mailbox integration UI and visually confirm the rendered copy shows the exact local URLs.

# Risks and follow-ups

- **Risk: existing code couples tenant identity to callback path**
  - Mitigation: move tenant correlation to protected state while keeping callback route stable.

- **Risk: legacy routes may be relied on by existing tests or setups**
  - Mitigation: preserve backward compatibility and add explicit tests for legacy route handling.

- **Risk: production URI format may not be fully defined in repo**
  - Mitigation: document production URIs using configured app base URL conventions and clearly note environment-specific hostname substitution.

- **Risk: UI copy may exist in multiple places**
  - Mitigation: search broadly for mailbox setup instructions and update all user-facing admin/setup references.

- **Follow-up**
  - Consider centralizing provider redirect URI generation in one helper/service if logic is currently duplicated across Gmail, Microsoft 365, UI copy, and tests.