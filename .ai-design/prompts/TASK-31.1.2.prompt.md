# Goal
Implement backlog task **TASK-31.1.2 — Refactor callback handler to hydrate company, user, provider, and return URI from protected state payload** for story **US-31.1 Add stable provider-scoped mailbox OAuth callback endpoints**.

The coding agent must update the mailbox OAuth callback flow so that the stable callback endpoints:

- `GET /api/mailbox-connections/gmail/callback`
- `GET /api/mailbox-connections/microsoft365/callback`

are routable and invoke the mailbox OAuth callback completion flow for their respective providers.

The callback completion logic must resolve:

- `CompanyId`
- `UserId`
- `Provider`
- `ReturnUri`

**exclusively** from the protected OAuth state payload.

The stable callback routes must **not** read company or user identity from query string or route parameters. If the protected state is invalid, tampered, or expired, the request must fail as an authentication failure and must not create or update any mailbox connection.

# Scope
In scope:

- Add or update stable provider-scoped callback API routes for Gmail and Microsoft 365.
- Refactor callback completion so the protected state payload is the single source of truth for callback context.
- Ensure provider is derived from the protected state payload and/or validated against the stable route provider.
- Remove any dependency on query string or route parameters for company/user identity on stable callback routes.
- Return an authentication failure response for invalid/tampered/expired state.
- Add or update automated tests covering routing, state hydration, and failure behavior.

Out of scope:

- Reworking the full mailbox connection domain model unless required for this task.
- Changing OAuth initiation UX beyond what is needed to ensure the callback has the required protected state.
- Broad auth redesign outside mailbox OAuth callback handling.
- Adding new providers beyond Gmail and Microsoft 365.

Implementation constraints:

- Follow existing solution structure and conventions in `Api`, `Application`, `Infrastructure`, and tests.
- Prefer minimal, targeted refactoring.
- Keep orchestration/business logic out of controllers/endpoints where possible.
- Do not introduce query-string-based company/user fallback behavior on stable callback routes.

# Files to touch
Inspect the existing mailbox OAuth implementation first, then update the relevant files. Likely areas include:

- `src/VirtualCompany.Api/**`
  - mailbox connection controller/endpoints
  - route registration
  - request/response models if any
- `src/VirtualCompany.Application/**`
  - mailbox OAuth callback command/service/handler
  - protected state payload contract
  - validation/result handling
- `src/VirtualCompany.Infrastructure/**`
  - state protection/unprotection implementation
  - provider integration adapters if callback completion lives here
- `src/VirtualCompany.Domain/**`
  - only if provider/state concepts are domain-owned
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint routing/integration tests
  - invalid state failure tests
- `README.md` or docs only if there is already API route documentation that must stay accurate

Before editing, search for terms like:

- `mailbox`
- `oauth`
- `callback`
- `gmail`
- `microsoft365`
- `state`
- `IDataProtector`
- `returnUri`
- `companyId`
- `userId`

# Implementation plan
1. **Discover the current callback flow**
   - Locate the current mailbox OAuth initiation and callback endpoints.
   - Identify where the protected OAuth state is created and what fields it currently contains.
   - Identify whether current callback completion reads `companyId`, `userId`, `provider`, or `returnUri` from query string, route values, claims, or other request context.
   - Identify the current error mapping for invalid protected state.

2. **Define or update the protected state payload contract**
   - Ensure there is a single protected state model that includes at minimum:
     - `CompanyId`
     - `UserId`
     - `Provider`
     - `ReturnUri`
     - expiration metadata if the current protection approach requires it
   - If a state payload model already exists, extend/refine it rather than duplicating.
   - Keep naming aligned with existing conventions.
   - Ensure provider is represented in a strongly typed way if the codebase already has a mailbox provider enum/value object.

3. **Refactor OAuth initiation if needed**
   - Ensure the initiation flow generates protected state containing all required callback context.
   - Ensure the stable callback route does not need company/user/provider from query/route because the state already contains them.
   - If provider-scoped initiation already exists, preserve it while making sure the callback state is complete.

4. **Add or update stable provider-scoped callback routes**
   - Ensure these routes are routable:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`
   - Route handlers should invoke the shared callback completion flow with the route’s provider context only as a routing discriminator, not as the source of company/user identity.
   - Prefer a shared internal method/service to avoid duplicated Gmail/Microsoft 365 logic.

5. **Refactor callback completion to hydrate exclusively from protected state**
   - Unprotect and validate the `state` parameter first.
   - Resolve `CompanyId`, `UserId`, `Provider`, and `ReturnUri` from the unprotected payload only.
   - Do not read company or user identity from:
     - query string
     - route parameters
     - ambient tenant context
   - If the route is provider-scoped, validate that the route provider matches the protected state provider. Treat mismatch as authentication failure or invalid state.
   - Pass the hydrated values into the mailbox OAuth completion service/command.

6. **Harden invalid-state behavior**
   - If state is missing, invalid, tampered, malformed, or expired:
     - return an authentication failure response
     - do not create or update a mailbox connection
   - Reuse existing auth failure/result patterns if present.
   - Avoid leaking sensitive details in the response body.

7. **Remove legacy fallback behavior**
   - Eliminate any code path on stable callback routes that still accepts or prefers:
     - `companyId` from query string
     - `userId` from query string
     - provider from query string when route is provider-scoped
   - If legacy routes still exist for backward compatibility, keep their behavior isolated and do not let it affect the stable routes unless the task explicitly requires migration.

8. **Add tests**
   - Add or update API/integration tests to prove:
     - `GET /api/mailbox-connections/gmail/callback` is routable and invokes Gmail callback completion.
     - `GET /api/mailbox-connections/microsoft365/callback` is routable and invokes Microsoft 365 callback completion.
     - callback completion uses protected state as the exclusive source of `CompanyId`, `UserId`, `Provider`, and `ReturnUri`.
     - query string or route parameter company/user values are ignored or unsupported on stable routes.
     - invalid/tampered/expired state returns authentication failure.
     - invalid state does not create or update a mailbox connection.
   - Prefer tests that verify observable behavior over implementation details, but also add focused unit tests for state parsing/validation if that layer exists.

9. **Keep changes clean and idiomatic**
   - Preserve separation of concerns:
     - API layer handles routing and HTTP mapping
     - Application layer handles callback completion orchestration
     - Infrastructure handles protection/unprotection details
   - Reuse existing result/error abstractions.
   - Keep provider-specific branching minimal and explicit.

# Validation steps
1. **Code search validation**
   - Confirm stable callback routes exist for:
     - `/api/mailbox-connections/gmail/callback`
     - `/api/mailbox-connections/microsoft365/callback`
   - Confirm stable callback handlers no longer read `companyId` or `userId` from query string or route parameters.

2. **Automated tests**
   - Run:
     - `dotnet test`
   - If test scope is large/noisy, run targeted tests first, then full suite.

3. **Build validation**
   - Run:
     - `dotnet build`

4. **Behavior validation checklist**
   - Valid protected state on Gmail callback completes successfully.
   - Valid protected state on Microsoft 365 callback completes successfully.
   - Route provider mismatch vs protected state provider fails safely.
   - Missing state fails as authentication failure.
   - Tampered state fails as authentication failure.
   - Expired state fails as authentication failure.
   - No mailbox connection is created or updated on invalid state.

5. **Regression validation**
   - Verify initiation still produces a callback URL and protected state compatible with the new callback completion flow.
   - Verify no unrelated mailbox connection flows were broken.

# Risks and follow-ups
- **Risk: existing callback flow may mix route/query/provider/state concerns**
  - Mitigation: centralize callback context hydration into one application service or helper and make it the only path used by stable routes.

- **Risk: protected state format may not currently include all required fields**
  - Mitigation: update initiation and callback handling together; keep serialization version-tolerant if needed.

- **Risk: invalid state currently throws generic exceptions**
  - Mitigation: normalize to explicit authentication failure handling and add tests for each failure mode.

- **Risk: provider mismatch could be silently accepted**
  - Mitigation: explicitly compare route provider and protected state provider and fail closed.

- **Risk: tests may be too shallow and miss persistence side effects**
  - Mitigation: include a test asserting no mailbox connection create/update occurs when state validation fails.

Follow-ups after implementation if not already covered elsewhere:

- Consider documenting the stable callback contract and state payload expectations.
- Consider adding telemetry/audit for callback state validation failures without exposing secrets.
- If legacy callback routes still exist, plan a separate cleanup/deprecation task.