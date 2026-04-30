# Goal
Implement backlog task **TASK-31.3.1 — Add legacy callback route shim that forwards to the shared stable callback handler** for story **US-31.3 Preserve backward compatibility and redirect behavior during callback migration**.

Deliver a minimal, production-safe change in the .NET backend so that the legacy callback route:

`/api/companies/{companyId}/mailbox-connections/{provider}/callback`

continues to work, but delegates to the existing shared/stable callback completion flow rather than duplicating callback logic.

The implementation must preserve tenant safety and redirect behavior by treating the **protected callback state** as the source of truth for identity and tenant resolution.

# Scope
In scope:

- Add or update the legacy callback endpoint so it forwards into the shared callback completion handler/service.
- Ensure callback completion logic ignores legacy route `companyId` for identity resolution.
- Ensure successful completion redirects to `state.ReturnUri` when present and allowed.
- Ensure fallback redirect generation uses `CompanyId` from protected state when `ReturnUri` is missing or invalid.
- Ensure no route/query mismatch can cause cross-tenant mailbox connection completion.
- Add/adjust tests covering legacy route behavior, redirect behavior, and tenant conflict safety.

Out of scope:

- Reworking the stable callback handler’s broader design unless required for safe delegation.
- UI changes.
- New OAuth provider features.
- Refactoring unrelated mailbox connection flows.

# Files to touch
Inspect the codebase first and then update the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Api/...`  
  - mailbox connection callback controller/endpoints
  - route mappings for legacy and stable callback paths
- `src/VirtualCompany.Application/...`  
  - shared callback completion handler/service
  - redirect validation / callback state handling
- `src/VirtualCompany.Domain/...`  
  - only if callback state or redirect validation contracts need minor adjustment
- `src/VirtualCompany.Infrastructure/...`  
  - only if provider callback plumbing lives here
- `tests/VirtualCompany.Api.Tests/...`  
  - integration/API tests for legacy callback route shim
  - redirect and tenant-conflict tests

Before editing, locate:
- the current stable/shared mailbox callback completion handler
- the legacy callback route, if it already exists
- the callback state model containing `CompanyId` and `ReturnUri`
- redirect allowlist/validation logic

# Implementation plan
1. **Discover the existing callback flow**
   - Find the stable/shared mailbox callback endpoint and the service/handler it uses.
   - Find any existing legacy callback endpoint.
   - Identify where protected state is unprotected/validated and where company identity is resolved.
   - Identify how redirect URIs are validated and how fallback redirects are generated today.

2. **Define the delegation shape**
   - Do not duplicate callback completion logic in the legacy route.
   - Make the legacy route a thin shim that forwards to the same shared completion handler used by the stable route.
   - Prefer extracting a single application/service method if the logic is currently embedded in a controller action.

3. **Enforce protected state as source of truth**
   - Ensure the shared completion path resolves tenant/company identity from protected state only.
   - The legacy route parameter `companyId` may remain in the route for backward compatibility, but it must not drive completion identity.
   - If route/query values conflict with protected state, do not allow cross-tenant completion. Either:
     - ignore non-authoritative route values while completing strictly from state, and/or
     - reject obviously conflicting values if the current design expects consistency checks.
   - Preserve existing security invariants and fail closed.

4. **Implement redirect behavior**
   - After successful callback completion:
     - redirect to `state.ReturnUri` if it exists and passes existing allow/validation rules
     - otherwise generate the fallback redirect using `state.CompanyId`
   - Ensure fallback generation never uses legacy route `companyId`.
   - Reuse existing redirect validation utilities if present; do not invent a parallel validator.

5. **Add/update the legacy route shim**
   - Map `/api/companies/{companyId}/mailbox-connections/{provider}/callback`
   - Forward all relevant callback inputs unchanged to the shared handler:
     - provider
     - query string values such as `code`, `state`, `error`, etc.
     - cancellation token / request context as appropriate
   - Keep the shim intentionally thin and documented as backward compatibility behavior.

6. **Add tests**
   Add focused tests that prove the acceptance criteria:
   - legacy route remains functional and reaches shared completion logic
   - route `companyId` is ignored for identity resolution in favor of protected state
   - valid `state.ReturnUri` is used for redirect
   - missing/invalid `ReturnUri` falls back using `state.CompanyId`
   - conflicting route/query values cannot complete a mailbox connection for another tenant
   - if there are existing stable-route tests, mirror them for the legacy route where useful

7. **Keep changes minimal and consistent**
   - Follow existing project conventions, naming, and endpoint style.
   - Avoid broad refactors unless needed to centralize callback completion safely.
   - Add concise comments only where the backward-compatibility shim or security behavior may be non-obvious.

# Validation steps
Run the relevant validation locally after implementation:

1. Build and test:
   - `dotnet build`
   - `dotnet test`

2. Verify legacy route behavior through tests or existing integration harness:
   - callback via legacy route succeeds when protected state is valid
   - completion path matches stable handler behavior

3. Verify redirect behavior:
   - valid allowed `ReturnUri` redirects there
   - missing `ReturnUri` redirects to fallback based on protected state company
   - rejected `ReturnUri` also falls back based on protected state company

4. Verify tenant safety:
   - legacy route with mismatched route `companyId` does not cause cross-tenant completion
   - any conflicting route/query values do not override protected state

5. If there are endpoint-specific tests or snapshots, update them only if behavior intentionally changed.

# Risks and follow-ups
- **Risk: duplicated callback logic**  
  If the legacy route reimplements completion logic, behavior may drift from the stable route. Avoid this by centralizing on one shared handler.

- **Risk: accidental trust in route values**  
  The legacy route includes `companyId`, which is now non-authoritative. Be careful not to use it in completion, lookup, or fallback redirect generation.

- **Risk: open redirect regression**  
  Redirect handling must continue to validate `ReturnUri`. Reuse existing validation and fail closed to fallback.

- **Risk: hidden tenant coupling in downstream services**  
  Even if the controller ignores route `companyId`, downstream services may still accept a company argument. Audit the call chain and ensure protected state remains authoritative.

Follow-ups if needed:
- Add a short code comment or ADR note documenting that legacy callback routes are compatibility shims and protected state is authoritative.
- If both stable and legacy routes still contain HTTP-specific duplication, consider a small follow-up extraction to a single callback completion application service.