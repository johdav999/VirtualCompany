# Goal
Implement backlog task **TASK-31.2.1 — Update mailbox OAuth callback URI builder to emit provider-scoped callback paths** for story **US-31.2 Update OAuth start flow to generate stable redirect URIs**.

The coding agent should update the mailbox OAuth start flow so that generated `redirect_uri` values are stable, provider-specific, and not tenant-path-scoped. The implementation must ensure:

- Gmail start flow emits  
  `{scheme}://{host}/api/mailbox-connections/gmail/callback`
- Microsoft 365 start flow emits  
  `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
- Callback URIs do **not** include `/api/companies/{companyId}/`
- Protected OAuth state includes:
  - `CompanyId`
  - `UserId`
  - `Provider`
  - `ReturnUri`
- OAuth start requests still require authenticated tenant/user context before state is issued

Preserve existing architecture and conventions in this .NET modular monolith. Prefer minimal, targeted changes over broad refactors.

# Scope
In scope:

- Locate the mailbox OAuth start flow and the callback URI builder used for mailbox connection providers
- Change redirect URI generation to use provider-scoped callback paths
- Ensure provider mapping is explicit and stable for at least:
  - Gmail
  - Microsoft 365
- Ensure protected OAuth state payload contains the required fields
- Preserve or strengthen authentication/tenant-context requirements before issuing OAuth state
- Update/add automated tests covering redirect URI generation and state contents

Out of scope:

- Reworking the full OAuth callback handling flow
- Changing provider registration beyond what is needed for callback path generation
- Broad API route redesign unrelated to mailbox OAuth start
- UI changes unless required by compile/test fixes
- Database schema changes unless the current state model is persisted and truly requires one

# Files to touch
Inspect and update the actual files you find, likely in these areas:

- `src/VirtualCompany.Api/**`
  - mailbox connection endpoints/controllers/minimal API route definitions
  - callback route definitions if needed for consistency
- `src/VirtualCompany.Application/**`
  - OAuth start flow handlers/services/commands
  - state payload models / DTOs
  - provider enum or provider-specific routing helpers
- `src/VirtualCompany.Infrastructure/**`
  - OAuth URL builders
  - state protection/serialization helpers
  - provider adapter implementations
- `src/VirtualCompany.Domain/**`
  - provider identifiers/value objects if defined there
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests for OAuth start
- Potentially:
  - `tests/**Application**`
  - `tests/**Infrastructure**`

Before editing, search for terms like:

- `redirect_uri`
- `mailbox-connections`
- `gmail`
- `microsoft365`
- `callback`
- `OAuth`
- `state`
- `CompanyId`
- `ReturnUri`

# Implementation plan
1. **Locate the current OAuth start flow**
   - Find the API endpoint used to start mailbox OAuth connections.
   - Identify how authenticated user context and company context are resolved.
   - Identify where `redirect_uri` is built and where OAuth `state` is created/protected.

2. **Confirm current routing shape**
   - Determine whether current callback URIs are incorrectly built under a tenant-scoped route such as:
     `/api/companies/{companyId}/mailbox-connections/...`
   - Identify the canonical callback endpoints already present or add the provider-scoped callback route shape if missing:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`

3. **Introduce or update provider-to-callback-path mapping**
   - Centralize callback path generation in one place if not already centralized.
   - Use explicit provider mapping rather than string concatenation from display names.
   - Expected mapping:
     - Gmail → `/api/mailbox-connections/gmail/callback`
     - Microsoft365 → `/api/mailbox-connections/microsoft365/callback`
   - If provider identifiers already exist as enum/constants, reuse them.
   - Avoid embedding `companyId` in callback path generation.

4. **Build redirect URIs from request origin only**
   - Generate `redirect_uri` as:
     `{scheme}://{host}{providerCallbackPath}`
   - Preserve scheme, host, and port from the incoming request/base URL abstraction.
   - Do not append tenant-scoped path segments.
   - Be careful not to lose support for local development ports.

5. **Ensure protected OAuth state includes required fields**
   - Inspect the state payload model and update it if needed so it includes:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Ensure the protected/serialized state emitted by the start flow contains all four fields.
   - If `ReturnUri` is optional in current behavior, preserve validation semantics but still include it in the payload when state is issued, per current contract expectations.

6. **Preserve authenticated tenant/user gating**
   - Verify the start endpoint requires authenticated user context.
   - Verify company/tenant context is resolved before state issuance.
   - If current code allows state generation before tenant/user validation, fix the order:
     1. authenticate user
     2. resolve/validate company membership or tenant context
     3. create protected state
     4. generate provider authorization URL

7. **Update provider-specific authorization request generation**
   - Ensure Gmail start flow uses the Gmail callback URI.
   - Ensure Microsoft 365 start flow uses the Microsoft 365 callback URI.
   - Confirm the generated authorization URL passes the correct `redirect_uri` to the provider.

8. **Add/adjust tests**
   Add focused tests that verify:
   - Gmail start flow generates redirect URI:
     `{scheme}://{host}/api/mailbox-connections/gmail/callback`
   - Microsoft 365 start flow generates redirect URI:
     `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - Generated redirect URI does not contain `/api/companies/{companyId}/`
   - Protected state payload includes `CompanyId`, `UserId`, `Provider`, and `ReturnUri`
   - Start flow rejects unauthenticated or missing tenant/user context before state issuance

9. **Keep changes small and consistent**
   - Do not introduce unnecessary abstractions.
   - Reuse existing request URL builders, data protection helpers, and auth context services where possible.
   - Keep naming aligned with existing provider naming conventions in the codebase.

# Validation steps
1. **Static review**
   - Confirm callback path generation is centralized or at least consistent.
   - Confirm no generated `redirect_uri` includes tenant-scoped path segments.
   - Confirm state payload model includes all required fields.

2. **Run targeted tests**
   - Run the relevant API/application test projects first.
   - Add or update tests for both Gmail and Microsoft 365 start flows.

3. **Run full test suite**
   ```bash
   dotnet test
   ```

4. **Build solution if needed**
   ```bash
   dotnet build
   ```

5. **Manual verification if integration tests are limited**
   - Start the API locally if practical.
   - Hit the OAuth start endpoint for Gmail and inspect the generated authorization URL.
   - Verify `redirect_uri` resolves to:
     - `https://localhost:{port}/api/mailbox-connections/gmail/callback`
   - Repeat for Microsoft 365:
     - `https://localhost:{port}/api/mailbox-connections/microsoft365/callback`
   - Verify no `/api/companies/{companyId}/` appears in the callback path.
   - If state can be unprotected in tests/dev helpers, verify it contains:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`

6. **Regression check**
   - Ensure callback endpoints still align with provider app registration expectations.
   - Ensure authenticated tenant/user context is still mandatory for start requests.

# Risks and follow-ups
- **Provider identifier mismatch risk**  
  Existing code may use inconsistent names like `Microsoft`, `Microsoft365`, `Office365`, or `Graph`. Normalize carefully without breaking existing behavior.

- **Route mismatch risk**  
  If callback endpoints are still defined under tenant-scoped routes, updating only the builder will create broken redirects. Verify actual callback route definitions match the emitted URIs.

- **State compatibility risk**  
  If there are existing consumers of the protected state payload, adding fields may affect deserialization/versioning. Prefer additive, backward-compatible changes.

- **Proxy/base URL risk**  
  If the app runs behind a reverse proxy, redirect URI generation may depend on forwarded headers or a base URL abstraction. Reuse the existing mechanism rather than inventing a new one.

- **Auth ordering risk**  
  Some implementations may currently build state before full tenant validation. Ensure validation happens first to satisfy acceptance criteria.

- **Follow-up suggestion**  
  If not already present, consider a small dedicated unit test around provider-to-callback-path mapping so future providers can be added safely without reintroducing tenant-scoped callback paths.