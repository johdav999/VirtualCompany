# Goal
Implement backlog task **TASK-31.2.3 — Remove companyId dependency from provider authorization request construction** for story **US-31.2 Update OAuth start flow to generate stable redirect URIs**.

The coding agent must update the OAuth start flow so that provider authorization requests build **stable callback redirect URIs that do not include `companyId` in the path**, while still preserving tenant/user context inside the **protected OAuth state**.

Required outcomes:
- Gmail start flow generates `redirect_uri` as:
  - `{scheme}://{host}/api/mailbox-connections/gmail/callback`
- Microsoft 365 start flow generates `redirect_uri` as:
  - `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
- No generated `redirect_uri` contains `/api/companies/{companyId}/`
- Protected OAuth state includes:
  - `CompanyId`
  - `UserId`
  - `Provider`
  - `ReturnUri`
- OAuth start requests still require authenticated tenant/user context before state is issued

# Scope
In scope:
- Locate the mailbox connection OAuth start flow in the ASP.NET Core backend
- Refactor redirect URI construction to use provider-specific stable callback routes
- Ensure company context is removed from callback path construction
- Ensure company/user/provider/returnUri are encoded in protected state payload
- Preserve existing authorization/authentication requirements for start endpoints
- Update or add tests covering redirect URI generation and state contents
- Update any affected callback handling assumptions if they currently depend on route `companyId`

Out of scope:
- Reworking the entire OAuth callback processing flow unless required to keep compilation/tests passing
- Changing provider scopes, consent parameters, token exchange logic, or persistence behavior beyond what is necessary
- Frontend UX changes unless tests or contracts require minor updates
- Database schema changes unless the current implementation incorrectly persists route-based company context and cannot function without a minimal adjustment

# Files to touch
Start by inspecting these likely areas, then adjust based on actual code structure:

- `src/VirtualCompany.Api/**`
  - Controllers/endpoints for mailbox connection OAuth start/callback routes
  - Route templates that may currently include `/api/companies/{companyId}/...`
- `src/VirtualCompany.Application/**`
  - OAuth start flow handlers/services/commands
  - State payload models and protection/unprotection logic
  - Provider authorization request builders
- `src/VirtualCompany.Infrastructure/**`
  - Provider-specific Gmail/Microsoft365 auth URL builders
  - URL generation helpers if redirect URI construction lives here
- `src/VirtualCompany.Domain/**`
  - Value objects or contracts for mailbox connection provider/state, if present
- `tests/VirtualCompany.Api.Tests/**`
  - Endpoint/integration tests for mailbox connection start flow
- `tests/**` or other test projects
  - Unit tests for state serialization/protection and provider auth request construction

Also inspect:
- `README.md`
- Any docs or route conventions referenced by mailbox connection APIs

# Implementation plan
1. **Find the current OAuth start flow**
   - Search for:
     - `mailbox-connections`
     - `gmail`
     - `microsoft365`
     - `redirect_uri`
     - `companyId`
     - `state`
   - Identify:
     - Start endpoints
     - Callback endpoints
     - Service/handler that constructs provider authorization URLs
     - State payload type and protector usage

2. **Understand current route and context flow**
   - Determine whether current start endpoints are nested under company routes such as:
     - `/api/companies/{companyId}/mailbox-connections/...`
   - Determine whether callback endpoints currently also include `companyId`
   - Determine how authenticated tenant/user context is resolved today:
     - route value
     - claims
     - membership context service
     - request-scoped tenant context
   - Preserve the requirement that state is only issued when authenticated tenant/user context is available

3. **Refactor redirect URI construction**
   - Replace any callback URI generation that depends on route `companyId`
   - Build stable provider callback URIs exactly as:
     - Gmail: `{scheme}://{host}/api/mailbox-connections/gmail/callback`
     - Microsoft 365: `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - Prefer centralized construction logic so both providers follow the same pattern
   - Use request-aware scheme/host generation already established in the API if available
   - Do not hardcode environment-specific hosts

4. **Move tenant context responsibility into protected state**
   - Update the protected OAuth state payload model so it explicitly contains:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - If these fields already exist, verify names and actual population
   - If not, add them and ensure they are populated from authenticated/request context
   - Ensure provider-specific start flows pass the correct provider enum/string into state

5. **Preserve authorization requirements**
   - Verify start endpoints still require authenticated access
   - Verify tenant/user context must be resolved before state is generated
   - If current implementation relied on route `companyId`, replace that dependency with the existing authenticated tenant context abstraction rather than making the endpoint anonymous
   - If no abstraction exists, use the established membership/company context pattern already present in the codebase

6. **Adjust callback assumptions if needed**
   - If callback handlers currently expect `companyId` from route values, update them to resolve company context from the unprotected state instead
   - Keep changes minimal and focused
   - Ensure provider validation still occurs and callback route/provider align with state/provider expectations

7. **Add or update tests**
   - Add tests for Gmail start flow asserting generated authorization request contains:
     - `redirect_uri={scheme}://{host}/api/mailbox-connections/gmail/callback`
   - Add tests for Microsoft 365 start flow asserting generated authorization request contains:
     - `redirect_uri={scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - Add assertions that generated redirect URIs do **not** contain `/api/companies/`
   - Add tests for protected state payload contents:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Add/retain tests proving unauthenticated or missing tenant context requests cannot obtain state / cannot start OAuth flow

8. **Keep implementation clean**
   - Prefer small, composable changes
   - Avoid duplicating provider-specific URI logic
   - Keep naming aligned with existing architecture and conventions
   - Do not introduce unrelated refactors

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If targeted tests are easier during iteration, run mailbox/OAuth-related tests first, then full suite

4. Manually verify in code/tests that:
   - Gmail redirect URI is exactly:
     - `{scheme}://{host}/api/mailbox-connections/gmail/callback`
   - Microsoft 365 redirect URI is exactly:
     - `{scheme}://{host}/api/mailbox-connections/microsoft365/callback`
   - No redirect URI includes:
     - `/api/companies/{companyId}/`
   - Protected state includes:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Start flow still requires authenticated tenant/user context before issuing state

5. In the final implementation summary, include:
   - Which files were changed
   - How redirect URI generation was centralized or updated
   - How tenant context is now carried in protected state
   - What tests were added/updated

# Risks and follow-ups
- **Risk: callback handlers still depend on route `companyId`**
  - Mitigation: update callback processing to trust protected state for company resolution

- **Risk: existing provider app registrations may still be configured with old callback URLs**
  - Mitigation: note this in summary if relevant; code should still implement the new stable callback paths

- **Risk: tests may be brittle if they assert full provider URLs with parameter ordering**
  - Mitigation: parse query strings or assert specific parameters rather than raw full-string equality where appropriate

- **Risk: tenant context resolution may currently be route-bound**
  - Mitigation: switch to the project’s authenticated company membership context pattern, not anonymous request data

Follow-up notes to mention if encountered:
- Provider registration/config may need environment updates for the new callback URIs
- Any legacy routes including companyId may need deprecation handling if externally referenced
- If callback route attributes changed, ensure API route tests and documentation remain aligned