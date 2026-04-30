# Goal
Implement backlog task **TASK-31.2.2** for story **US-31.2 Update OAuth start flow to generate stable redirect URIs**.

The coding agent must update the OAuth start flow so that:
- Gmail start requests generate `redirect_uri` as `{scheme}://{host}/api/mailbox-connections/gmail/callback`
- Microsoft 365 start requests generate `redirect_uri` as `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
- Callback paths no longer include `/api/companies/{companyId}/`
- The protected OAuth state issued during the start flow includes:
  - `CompanyId`
  - `UserId`
  - `Provider`
  - `ReturnUri`
- OAuth start requests still require authenticated tenant/user context before state is created

Preserve existing architecture and conventions in this .NET modular monolith. Prefer minimal, targeted changes over broad refactors.

# Scope
In scope:
- Locate the mailbox connection OAuth start flow for Gmail and Microsoft 365
- Update redirect URI generation to use stable provider callback endpoints rooted at `/api/mailbox-connections/{provider}/callback`
- Update the protected OAuth state payload/model so it carries `CompanyId`, `UserId`, `Provider`, and `ReturnUri`
- Ensure state issuance only occurs when authenticated tenant/user context is present
- Update or add tests covering redirect URI generation, state contents, and auth/context requirements

Out of scope:
- Reworking the callback flow beyond what is required to consume the new state shape
- Changing unrelated OAuth providers
- Large-scale redesign of auth, tenancy, or routing
- UI changes unless required by compile/test fixes

Implementation constraints:
- Follow existing solution layering and patterns
- Keep tenant isolation intact
- Do not introduce insecure/plaintext state handling; continue using the existing protected state mechanism
- If existing callback endpoints are tenant-scoped, add or wire stable non-tenant-scoped callback routes as required by this task, but avoid unnecessary duplication

# Files to touch
Start by inspecting and then modify only the files necessary. Likely areas include:

- `src/VirtualCompany.Api/**`
  - Mailbox connection controllers/endpoints
  - OAuth route definitions
  - Request context / tenant resolution usage
- `src/VirtualCompany.Application/**`
  - OAuth start flow handlers/services
  - DTOs/commands for mailbox connection start
  - Protected state payload types
- `src/VirtualCompany.Domain/**`
  - Provider enums/value objects if state payload depends on domain types
- `src/VirtualCompany.Infrastructure/**`
  - OAuth provider adapters/clients
  - State protection/serialization helpers
  - Redirect URI builders
- `tests/VirtualCompany.Api.Tests/**`
  - Endpoint/integration tests for start flow
  - Tests asserting redirect URI and protected state contents

Also inspect:
- `README.md`
- Any existing mailbox connection or OAuth documentation/comments near the touched code

Do not edit archive/docs files unless absolutely necessary.

# Implementation plan
1. **Discover the current OAuth start flow**
   - Search for:
     - `mailbox-connections`
     - `gmail`
     - `microsoft365`
     - `redirect_uri`
     - `state`
     - `ReturnUri`
     - `CompanyId`
     - `UserId`
   - Identify:
     - API endpoint(s) that start mailbox OAuth
     - Service/handler that builds provider authorization URLs
     - Current protected state payload/model
     - Current callback route definitions
     - How authenticated company/user context is resolved and enforced

2. **Map the current route and redirect behavior**
   - Confirm whether current redirect URIs incorrectly include `/api/companies/{companyId}/...`
   - Identify whether callback endpoints already exist at:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`
   - If not, add stable callback routes or adjust route templates so these exact paths are supported

3. **Update redirect URI generation**
   - Refactor the redirect URI builder so it derives:
     - scheme from the current request
     - host from the current request
     - fixed callback path by provider
   - Required outputs:
     - Gmail: `{scheme}://{host}/api/mailbox-connections/gmail/callback`
     - Microsoft 365: `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - Ensure no generated redirect URI includes `/api/companies/{companyId}/`
   - Keep implementation centralized if there is already a shared provider URL builder

4. **Extend the protected OAuth state payload**
   - Update the protected state model/record/class to include:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Preserve any existing required fields already used by the callback flow
   - Ensure serialization/protection still works after the shape change
   - If provider is currently implied externally, make it explicit in state per acceptance criteria

5. **Enforce authenticated tenant/user context before issuing state**
   - In the start flow, verify the code requires authenticated context before generating state
   - If enforcement is currently only implicit, make it explicit:
     - require authenticated user
     - require resolved company/tenant context
   - Fail safely with the project’s established unauthorized/forbidden behavior before state generation if context is missing

6. **Wire callback flow compatibility if needed**
   - If callback processing currently depends on company/provider from route values, update it to read from protected state where appropriate
   - Keep changes minimal and backward-safe within this task’s scope
   - Ensure provider-specific callback endpoints still dispatch correctly

7. **Add/update tests**
   - Add tests for Gmail start flow asserting generated authorization request uses:
     - redirect URI `{scheme}://{host}/api/mailbox-connections/gmail/callback`
   - Add tests for Microsoft 365 start flow asserting generated authorization request uses:
     - redirect URI `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - Add tests asserting redirect URIs do **not** contain `/api/companies/{companyId}/`
   - Add tests asserting protected state contains:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Add tests asserting unauthenticated or missing tenant/user context requests do not issue state and are rejected

8. **Keep changes clean and minimal**
   - Avoid introducing duplicate provider-specific logic if a small shared abstraction works
   - Preserve naming and coding conventions already used in the repo
   - Prefer focused commits in spirit, even though you are returning a single patch

# Validation steps
Run the smallest relevant checks first, then broader ones.

1. **Build**
   - `dotnet build`

2. **Run targeted tests if available**
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. **Run full test suite if targeted tests pass or if test layout is unclear**
   - `dotnet test`

4. **Manual code validation**
   - Confirm Gmail redirect URI path is exactly:
     - `/api/mailbox-connections/gmail/callback`
   - Confirm Microsoft 365 redirect URI path is exactly:
     - `/api/mailbox-connections/microsoft365/callback`
   - Confirm generated redirect URIs do not include `/api/companies/{companyId}/`
   - Confirm protected state payload includes all required fields
   - Confirm state is only created after authenticated company/user context is available
   - Confirm callback route definitions align with generated redirect URIs

5. **If tests inspect protected state**
   - Use the existing state protector/unprotector in tests rather than brittle string assertions where possible
   - Assert semantic contents of the state payload, not just presence of a serialized blob

# Risks and follow-ups
- **Risk: callback flow currently depends on tenant-scoped route values**
  - Follow-up: if callback handling breaks due to removal of company from callback path, update callback processing to rely on protected state for company/provider resolution

- **Risk: redirect URI generation is duplicated across providers**
  - Follow-up: consolidate into a shared redirect URI builder after this task if duplication remains

- **Risk: tests may be sparse around OAuth URL generation**
  - Follow-up: add focused API/integration coverage around provider authorization URL construction and callback handling

- **Risk: provider enum/string mismatches**
  - Follow-up: normalize provider identifiers across route names, state payload, and OAuth adapter code

- **Risk: reverse proxy host/scheme handling**
  - Follow-up: verify the app already respects forwarded headers in environments where external scheme/host differ from internal hosting

- **Risk: state model changes may affect backward compatibility**
  - Follow-up: if there are in-flight OAuth sessions in non-dev environments, consider whether callback handling should tolerate older state payloads during rollout