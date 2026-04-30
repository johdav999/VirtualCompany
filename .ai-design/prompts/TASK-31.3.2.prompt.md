# Goal

Implement **TASK-31.3.2 — post-callback redirect resolution using validated `ReturnUri` from protected state** for the mailbox connection OAuth callback flow.

The coding agent must update the callback handling so that:

- The **legacy callback route** `/api/companies/{companyId}/mailbox-connections/{provider}/callback` remains supported.
- The legacy route **delegates into the shared callback completion handler** rather than duplicating logic.
- **Protected state is the source of truth** for tenant/company identity and redirect behavior.
- After successful callback completion, the app redirects to **`state.ReturnUri` when present and allowed**.
- If `ReturnUri` is missing or rejected, redirect generation falls back using **`CompanyId` from protected state**, not route/query values.
- No route/query mismatch can enable **cross-tenant mailbox connection completion**.

Produce a minimal, clean implementation consistent with the existing .NET architecture and preserve backward compatibility.

# Scope

In scope:

- Inspect the current mailbox connection OAuth callback flow in API/application layers.
- Identify:
  - the shared callback completion handler/service,
  - the legacy callback endpoint,
  - the protected callback state model,
  - any existing redirect generation and URI validation utilities.
- Update callback completion logic to:
  - resolve company identity from protected state only,
  - prefer validated `ReturnUri` from protected state for post-success redirect,
  - fall back to generated redirect using state `CompanyId`.
- Ensure the legacy route remains functional and delegates to the shared completion path.
- Add/adjust tests covering:
  - legacy route compatibility,
  - `ReturnUri` accepted path,
  - `ReturnUri` rejected path,
  - route/state company mismatch safety.

Out of scope:

- Broad refactors unrelated to callback completion.
- Changing OAuth provider contracts unless required by this task.
- UI redesign.
- Introducing new persistence models unless absolutely necessary.
- Modifying unrelated redirect behavior outside mailbox connection callback completion.

# Files to touch

Likely areas to inspect and update:

- `src/VirtualCompany.Api/**`
  - mailbox connection callback controller/endpoints
  - route mappings for legacy and shared callback handlers
- `src/VirtualCompany.Application/**`
  - callback completion command/handler/service
  - protected state parsing/validation
  - redirect resolution logic
- `src/VirtualCompany.Domain/**`
  - only if callback state or redirect policy types live here
- `src/VirtualCompany.Infrastructure/**`
  - only if state protection/unprotection or URL validation helpers live here
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests for callback behavior
- Possibly `tests/**Application**` if there are handler-level tests already covering callback completion

Before editing, search for terms like:

- `mailbox-connections`
- `callback`
- `ReturnUri`
- `returnUri`
- `state`
- `protected state`
- `redirect`
- `companyId`
- `provider`

Prefer modifying existing types and tests over creating parallel abstractions.

# Implementation plan

1. **Locate the callback flow**
   - Find the current mailbox connection callback endpoints and determine:
     - which route is the canonical/shared callback route,
     - whether the legacy route already exists,
     - where callback completion actually happens.
   - Identify the protected state model and confirm it contains at least company identity and optional `ReturnUri`.

2. **Confirm source-of-truth boundaries**
   - Audit current logic for any use of:
     - route `companyId`,
     - query `companyId`,
     - provider from route/query,
     - other request values
     during identity resolution or redirect generation.
   - Preserve provider routing as needed for endpoint compatibility, but ensure **tenant/company resolution comes from protected state**.

3. **Keep legacy route functional via delegation**
   - Ensure `/api/companies/{companyId}/mailbox-connections/{provider}/callback` remains mapped.
   - Make the legacy route call the same shared callback completion path as the newer route.
   - Avoid duplicating callback completion logic between routes.

4. **Implement redirect resolution order**
   - In the shared callback completion logic, after successful state validation and callback completion:
     1. Read `ReturnUri` from protected state.
     2. Validate it using existing safe redirect/local URL/allowlist logic if available.
     3. If valid, redirect to `state.ReturnUri`.
     4. If absent or invalid, generate the fallback redirect using **`state.CompanyId`**.
   - Do not use route `companyId` for fallback redirect generation.

5. **Enforce mismatch safety**
   - If route/query values conflict with protected state, do not allow them to influence tenant resolution or completion.
   - If the existing code explicitly checks mismatch and rejects, preserve or strengthen that behavior.
   - At minimum, ensure completion and redirect target are derived from protected state so cross-tenant completion cannot occur through conflicting route/query values.

6. **Use existing validation primitives**
   - Reuse any existing helpers for:
     - local redirect validation,
     - allowed return URL validation,
     - URI normalization.
   - If no helper exists, add a narrowly scoped validator aligned with current app conventions:
     - prefer local relative paths or explicitly allowed app URLs,
     - reject external/untrusted URLs,
     - fail closed.

7. **Add/adjust tests**
   - Add tests that prove:
     - **Legacy route works** and reaches the shared completion behavior.
     - **Successful callback redirects to `state.ReturnUri`** when valid.
     - **Missing `ReturnUri` falls back** using `state.CompanyId`.
     - **Rejected `ReturnUri` falls back** using `state.CompanyId`.
     - **Route company mismatch does not cause cross-tenant completion** and fallback still uses protected state company.
   - If test style is integration-first in this repo, prefer API tests over isolated unit tests.

8. **Keep changes minimal and explicit**
   - Avoid hidden behavior changes.
   - Add concise comments only where the source-of-truth rule may be non-obvious:
     - protected state determines company identity,
     - route values are compatibility-only for legacy callback routing.

# Validation steps

Run the smallest relevant checks first, then broader validation.

1. **Build**
   - `dotnet build`

2. **Run targeted tests if discoverable**
   - Run mailbox connection / callback related tests first if the suite supports filtering.
   - Otherwise run:
     - `dotnet test`

3. **Verify behavior in tests or manually via endpoint coverage**
   - Confirm legacy route `/api/companies/{companyId}/mailbox-connections/{provider}/callback` still returns the expected redirect/result.
   - Confirm valid protected-state `ReturnUri` is used.
   - Confirm invalid or missing `ReturnUri` falls back to redirect built from protected-state `CompanyId`.
   - Confirm conflicting route `companyId` cannot alter tenant resolution.

4. **Code review checklist**
   - No callback completion path uses route/query `companyId` as the source of truth.
   - Shared callback completion logic is not duplicated.
   - Redirect validation fails closed.
   - Backward compatibility is preserved.

# Risks and follow-ups

- **Risk: open redirect regression**
  - If `ReturnUri` validation is too permissive, this could introduce an open redirect vulnerability.
  - Mitigation: reuse existing safe redirect validation and default to fallback on any ambiguity.

- **Risk: hidden dependency on route companyId**
  - Existing downstream code may still rely on route `companyId`.
  - Mitigation: trace the full completion path and ensure state-derived company identity is passed through explicitly.

- **Risk: legacy route/provider assumptions**
  - The legacy route may carry provider-specific assumptions not obvious at first glance.
  - Mitigation: preserve route shape and delegate only the completion logic.

- **Risk: test gaps around protected state**
  - If current tests mock too much, cross-tenant safety may not be proven.
  - Mitigation: prefer endpoint/integration coverage where possible.

Follow-ups if needed, but do not expand scope unless required:

- Centralize callback redirect resolution into a dedicated helper if logic is currently duplicated elsewhere.
- Add explicit security-focused tests for rejected absolute/external `ReturnUri` values.
- Document the callback source-of-truth rule in code comments or developer docs if the flow is subtle.