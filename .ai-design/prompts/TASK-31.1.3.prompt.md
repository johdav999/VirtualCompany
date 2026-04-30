# Goal
Implement `TASK-31.1.3` by updating the stable provider-scoped mailbox OAuth callback completion flow so that:

- `GET /api/mailbox-connections/gmail/callback` routes correctly and completes Gmail OAuth callbacks.
- `GET /api/mailbox-connections/microsoft365/callback` routes correctly and completes Microsoft 365 OAuth callbacks.
- callback completion resolves `CompanyId`, `UserId`, `Provider`, and `ReturnUri` only from protected OAuth state.
- stable callback routes do **not** read company or user identity from route values or query string.
- invalid, tampered, or expired protected state produces an authentication failure response and does **not** create or update a mailbox connection.

Work within the existing .NET solution and preserve current architecture boundaries between API, Application, Domain, and Infrastructure.

# Scope
In scope:

- Find the existing mailbox OAuth callback initiation/completion flow.
- Add or update stable provider-scoped callback endpoints for Gmail and Microsoft 365.
- Ensure callback completion accepts provider from the stable route and all other contextual identity from protected state only.
- Enforce protected state validation, including expiry.
- Ensure failure handling for invalid/tampered/expired state returns an authentication failure response and prevents persistence side effects.
- Add/update automated tests covering routing, state-only resolution, and failure/no-write behavior.

Out of scope:

- New providers beyond Gmail and Microsoft 365.
- UI changes beyond what is required for callback routing.
- Broad refactors unrelated to mailbox OAuth callback completion.
- Changing the overall OAuth initiation design unless required to support protected-state-only completion.

# Files to touch
Inspect first, then update only the necessary files. Likely areas:

- `src/VirtualCompany.Api/**`
  - mailbox connections controller/endpoints
  - route mapping for stable callback URLs
  - auth failure response mapping
- `src/VirtualCompany.Application/**`
  - mailbox OAuth callback completion command/handler/service
  - state validation logic and contracts
- `src/VirtualCompany.Domain/**`
  - provider enums/value objects if needed
  - domain invariants around mailbox connection creation/update
- `src/VirtualCompany.Infrastructure/**`
  - OAuth state protection/unprotection implementation
  - provider-specific OAuth adapter/services
  - persistence guards if needed
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint routing/integration tests
  - invalid/tampered/expired state tests
  - assertions that no mailbox connection is created/updated on auth failure

Also inspect:
- `README.md`
- any existing mailbox connection or OAuth-related files discovered via search

Before editing, search for:
- `mailbox`
- `oauth`
- `callback`
- `gmail`
- `microsoft365`
- `state`
- `IDataProtector`
- `AuthenticationFailure`
- mailbox connection persistence entities/repositories

# Implementation plan
1. **Discover the current flow**
   - Locate:
     - mailbox connection callback endpoints/controllers
     - initiation endpoint that generates OAuth state
     - callback completion handler/service
     - state protection/unprotection implementation
     - mailbox connection persistence path
   - Identify whether current callback completion reads `companyId`, `userId`, `provider`, or `returnUri` from query/route values. Document this in your working notes and remove that dependency.

2. **Define the stable callback contract**
   - Ensure there are routable GET endpoints:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`
   - The route should determine only the provider identity for dispatch to the correct provider callback flow.
   - Do not accept or use company/user identity from route or query on these stable routes.

3. **Harden protected OAuth state**
   - Ensure the protected state payload contains at minimum:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
     - issued/expiry metadata sufficient to reject expired state
   - If the payload shape already exists, extend it minimally rather than redesigning everything.
   - Use existing ASP.NET Core data protection or current protection mechanism already in the codebase.
   - Validation rules:
     - state must be present
     - state must successfully unprotect/deserialize
     - state must not be expired
     - provider in protected state must match the provider-scoped callback route
   - Treat any failure as authentication failure, not as a generic server error.

4. **Update callback completion logic**
   - Refactor the completion handler/service so it resolves:
     - `CompanyId` from protected state only
     - `UserId` from protected state only
     - `Provider` from protected state, validated against route provider
     - `ReturnUri` from protected state only
   - Remove any fallback behavior that reads company/user identity from query string or route parameters on stable callback routes.
   - If there are legacy callback routes, do not break them unless they are directly in scope; keep changes focused on stable routes.

5. **Enforce provider match**
   - In the stable Gmail callback endpoint, require protected state provider = Gmail.
   - In the stable Microsoft 365 callback endpoint, require protected state provider = Microsoft365.
   - If mismatch occurs, return authentication failure and stop processing before any token exchange persistence/update side effects.

6. **Enforce expiry**
   - Validate expiry before mailbox connection creation/update.
   - If expired, return authentication failure and stop processing.
   - Prefer deterministic clock usage if the codebase already has a clock abstraction; otherwise keep implementation minimal and testable.

7. **Prevent writes on invalid state**
   - Ensure invalid/tampered/expired/mismatched state exits before:
     - token exchange persistence
     - mailbox connection create/update
     - audit/outbox side effects related to successful connection
   - If needed, add guard clauses at the application layer before repository calls.

8. **Return the correct failure response**
   - Map invalid/tampered/expired state to an authentication failure response.
   - Reuse existing API conventions if present.
   - Do not leak sensitive details in the response body.
   - Keep logs safe and useful.

9. **Add tests**
   - Add/adjust API tests to verify:
     - Gmail callback route is routable and invokes callback completion
     - Microsoft 365 callback route is routable and invokes callback completion
     - callback completion uses protected state for `CompanyId`, `UserId`, `Provider`, `ReturnUri`
     - stable callback routes ignore company/user identity in query string/route parameters
     - invalid protected state returns authentication failure and no mailbox connection is created/updated
     - tampered protected state returns authentication failure and no mailbox connection is created/updated
     - expired protected state returns authentication failure and no mailbox connection is created/updated
     - provider mismatch between route and state returns authentication failure and no mailbox connection is created/updated
   - Prefer integration-style API tests where feasible, with repository assertions or test doubles proving no writes occurred.

10. **Keep changes clean**
   - Follow existing naming, layering, and dependency direction.
   - Avoid introducing provider-specific duplication if a shared callback completion service can handle both providers with route-scoped provider input.
   - Keep public API changes minimal and aligned with the task.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically verify:
   - `GET /api/mailbox-connections/gmail/callback` is mapped.
   - `GET /api/mailbox-connections/microsoft365/callback` is mapped.
   - Valid protected state completes successfully for the matching provider.
   - Query string attempts to override `companyId` or `userId` are ignored/not used.
   - Tampered state fails with authentication failure.
   - Expired state fails with authentication failure.
   - Provider mismatch between route and protected state fails with authentication failure.
   - No mailbox connection create/update occurs on any invalid-state path.

4. If there are existing endpoint/integration test patterns, use those and ensure all new tests pass without breaking unrelated suites.

# Risks and follow-ups
- There may be an existing legacy callback route shape that still passes company/user/provider via query or route values. Do not accidentally break unrelated legacy flows unless they are shared with the stable routes; isolate the stable-route behavior carefully.
- Expiry validation may depend on how state is currently serialized. If no expiry exists yet, add it in the smallest compatible way and update initiation + completion together.
- If provider-specific adapters currently infer provider from query/route instead of state, ensure the final source of truth is protected state with explicit route/state match validation.
- If tests are hard to write due to missing seams around time or state protection, introduce minimal abstractions only where necessary.
- Follow-up task if needed: standardize all OAuth callback flows, including legacy routes, on the same protected-state-only contract.