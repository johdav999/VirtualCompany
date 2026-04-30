# Goal
Implement backlog task **TASK-31.1.1 — Add provider-scoped callback routes for Gmail and Microsoft 365 outside company-scoped APIs** for story **US-31.1 Add stable provider-scoped mailbox OAuth callback endpoints**.

The implementation must add stable, provider-scoped OAuth callback endpoints:

- `GET /api/mailbox-connections/gmail/callback`
- `GET /api/mailbox-connections/microsoft365/callback`

These routes must complete mailbox OAuth using **only protected OAuth state** to resolve:

- `CompanyId`
- `UserId`
- `Provider`
- `ReturnUri`

Do **not** read company or user identity from route values or query string on these stable callback routes. If protected state is invalid, tampered, or expired, return an authentication failure response and ensure no mailbox connection is created or updated.

# Scope
In scope:

- Add routable API endpoints for Gmail and Microsoft 365 callback completion outside company-scoped API routing.
- Reuse or refactor existing mailbox OAuth callback completion logic so provider-scoped routes invoke the same application/service flow.
- Ensure callback completion derives tenant/user/provider/return URI exclusively from protected state.
- Ensure stable callback routes ignore any company/user identifiers in query or route parameters.
- Add/adjust tests for:
  - route reachability
  - provider dispatch
  - protected state-only resolution
  - invalid/tampered/expired state failure behavior
  - no persistence side effects on invalid state

Out of scope:

- Changing mailbox connection initiation UX unless required to emit compatible protected state.
- Broad redesign of OAuth provider abstractions beyond what is needed for these stable callback routes.
- Non-mailbox OAuth integrations.
- Mobile/web UI changes unless a hardcoded callback URI reference must be updated.

# Files to touch
Inspect the codebase first, then update the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Api/...`
  - API route/controller/minimal endpoint definitions for mailbox connections
  - any route registration or endpoint mapping files
- `src/VirtualCompany.Application/...`
  - mailbox OAuth callback command/handler/service
  - protected state unprotection/validation logic
- `src/VirtualCompany.Infrastructure/...`
  - provider-specific OAuth integration adapters
  - data protection/state serializer helpers if implemented there
- `src/VirtualCompany.Domain/...`
  - only if provider enum/value object updates are needed
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint routing/integration tests
  - invalid state behavior tests
- `README.md` or integration docs only if callback route documentation already exists and must stay accurate

Before editing, locate:
- existing mailbox connection endpoints
- existing OAuth callback handlers
- where protected OAuth state is created/unprotected
- any current company-scoped callback route patterns
- persistence path for mailbox connection create/update

# Implementation plan
1. **Discover current mailbox OAuth flow**
   - Find existing mailbox connection initiation and callback endpoints.
   - Identify whether callback completion currently expects company/user/provider from route/query.
   - Trace the application service/command used to exchange auth code and persist mailbox connection.
   - Identify how protected OAuth state is generated and validated today.

2. **Define stable provider-scoped routes**
   - Add two GET endpoints exactly at:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`
   - Keep them outside company-scoped route groups.
   - Route each endpoint to the shared callback completion flow, passing the expected provider from the route definition rather than trusting request query input.

3. **Refactor callback completion contract if needed**
   - Update the callback completion request model/command so it accepts only:
     - provider implied by endpoint
     - OAuth `code`
     - OAuth `state`
     - any provider-standard error fields if already supported
   - Remove any dependency on route/query `companyId`, `userId`, or similar identity inputs for stable routes.
   - Ensure the completion flow unprotects state and resolves:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
   - Validate that the provider in protected state matches the provider-scoped endpoint being invoked.

4. **Enforce protected state as the exclusive identity source**
   - In callback completion logic:
     - unprotect state
     - reject null/invalid/unreadable/tampered/expired state
     - reject state missing required fields
     - reject provider mismatch between route and state
   - Do not read company/user identity from query string or route parameters.
   - If current code binds these values from request DTOs, remove or ignore them for stable callback routes.

5. **Failure handling**
   - For invalid/tampered/expired state, return an authentication failure response consistent with existing API conventions.
   - Ensure failure occurs before any mailbox connection create/update side effect.
   - If provider returns OAuth error parameters, preserve existing safe handling while still requiring valid protected state for tenant/user resolution.

6. **Persistence safety**
   - Confirm no repository save/create/update occurs until after state validation succeeds.
   - If necessary, reorder logic so token exchange/persistence only happens after state is validated and tenant/user/provider context is established.
   - Add guards to prevent accidental upsert on invalid callback requests.

7. **Tests**
   - Add API/integration tests covering:
     - Gmail callback route is routable and invokes completion flow.
     - Microsoft 365 callback route is routable and invokes completion flow.
     - Completion resolves company/user/provider/return URI from protected state only.
     - Query/route attempts to spoof company/user are ignored or unsupported.
     - Invalid state returns authentication failure.
     - Tampered state returns authentication failure.
     - Expired state returns authentication failure if expiration is enforced by protector.
     - Invalid state does not create/update mailbox connection.
     - Provider mismatch between route and state fails safely.
   - Prefer tests that assert observable behavior over implementation details, but verify no persistence side effects using repository/db assertions where feasible.

8. **Keep implementation aligned with architecture**
   - HTTP layer should remain thin.
   - Application layer should own callback completion orchestration.
   - Infrastructure should handle provider token exchange and state protection mechanics.
   - Maintain tenant isolation by deriving tenant context from protected state, not ambient route structure.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted API tests, run them first during iteration:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. Manually verify route registration in code:
   - confirm exact paths exist:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`

5. Verify acceptance criteria against implementation:
   - Gmail route is reachable and dispatches callback completion.
   - Microsoft 365 route is reachable and dispatches callback completion.
   - `CompanyId`, `UserId`, `Provider`, and `ReturnUri` come only from protected state.
   - No company/user identity is read from query string or route parameters on stable routes.
   - Invalid/tampered/expired state returns auth failure and produces no mailbox connection mutation.

6. If test infrastructure supports it, add assertions for:
   - provider mismatch rejection
   - no DB writes on invalid state
   - return URI comes from state, not query

# Risks and follow-ups
- **Existing route coupling risk:** Current callback flow may be tightly coupled to company-scoped routes or route-bound IDs. Refactor carefully to avoid breaking existing initiation/completion flows.
- **State schema compatibility risk:** If protected state payload currently lacks provider or return URI, initiation logic may need a coordinated update.
- **Provider mismatch risk:** Stable routes can be abused if state provider is not cross-checked against endpoint provider.
- **Failure response consistency risk:** Use existing authentication/problem response conventions rather than inventing a new shape.
- **Testability risk:** If state protection uses real ASP.NET Data Protection with time-sensitive payloads, tests may need deterministic helpers/fakes.
- **Backward compatibility follow-up:** If legacy company-scoped callback routes exist, decide whether to keep them temporarily, redirect them, or deprecate them in a separate task.
- **Documentation follow-up:** Update any provider app registration docs/config samples to use the new stable callback URLs if such docs already exist in the repo.