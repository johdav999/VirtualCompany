# Goal
Implement backlog task **TASK-31.3.3 — Update fallback redirect builder to derive tenant context from protected state instead of route values** for story **US-31.3 Preserve backward compatibility and redirect behavior during callback migration**.

Ensure the legacy OAuth callback route remains supported, but tenant identity and redirect fallback behavior are driven by the **protected callback state** as the source of truth, not by route or query values. The implementation must prevent any cross-tenant completion when route/query values conflict with protected state.

# Scope
In scope:
- Keep legacy callback route `/api/companies/{companyId}/mailbox-connections/{provider}/callback` functional.
- Ensure the legacy route delegates into the same shared callback completion flow used by the newer callback path.
- Update callback completion logic so:
  - `companyId` from route is **not** used for identity resolution.
  - protected state is the authoritative source for `CompanyId`.
  - successful completion redirects to `state.ReturnUri` when present and allowed.
  - fallback redirect generation uses `state.CompanyId` when `ReturnUri` is missing or rejected.
- Add/adjust validation to ensure conflicting route/query tenant values cannot cause cross-tenant mailbox connection completion.
- Add or update automated tests covering the acceptance criteria.

Out of scope:
- Broad redesign of OAuth callback architecture.
- Changes to unrelated providers or mailbox connection flows beyond what is needed for shared callback completion and redirect generation.
- UI changes outside redirect target behavior.
- Database schema changes unless strictly required, which is unlikely for this task.

# Files to touch
Inspect and update the actual files that implement mailbox OAuth callback handling and redirect generation. Likely candidates include:

- `src/VirtualCompany.Api/...` files containing:
  - mailbox connection callback endpoints/controllers/minimal API mappings
  - legacy callback route mapping
  - callback completion orchestration
- `src/VirtualCompany.Application/...` files containing:
  - mailbox connection callback completion handlers/services
  - protected state parsing/validation
  - redirect builder / fallback redirect builder
- `src/VirtualCompany.Infrastructure/...` files if provider callback adapters or token exchange services live there
- `tests/VirtualCompany.Api.Tests/...` and/or `tests/...Application.Tests/...` for:
  - legacy callback route behavior
  - redirect behavior
  - tenant conflict protection

Before editing, search for symbols and strings such as:
- `mailbox-connections`
- `/callback`
- `ReturnUri`
- `companyId`
- `protected state`
- `state.CompanyId`
- redirect builder / fallback redirect
- callback completion handler
- provider callback handler

Prefer minimal, targeted changes in existing abstractions rather than introducing parallel logic.

# Implementation plan
1. **Locate the shared callback flow**
   - Find the current mailbox OAuth callback implementation and identify:
     - the legacy route handler
     - the newer/shared callback completion handler
     - the protected state model and unprotection logic
     - the redirect validation and fallback redirect builder
   - Confirm where tenant/company resolution currently happens and whether any route `companyId` is still being used downstream.

2. **Preserve legacy route, but make it a thin delegator**
   - Ensure `/api/companies/{companyId}/mailbox-connections/{provider}/callback` still exists.
   - Refactor only if needed so the legacy route forwards into the same shared completion path as the newer callback route.
   - The legacy route may accept `companyId` for backward compatibility, but it must not be authoritative for completion.

3. **Make protected state the source of truth**
   - In the shared callback completion handler, resolve tenant/company context exclusively from the protected state payload.
   - Remove or bypass any use of route/query `companyId` for:
     - mailbox connection lookup
     - identity resolution
     - tenant scoping
     - fallback redirect generation
   - If route/query tenant values are present and conflict with protected state, do not allow them to influence completion.

4. **Harden conflict handling**
   - Add explicit guard logic for mismatch scenarios if not already present.
   - Expected behavior:
     - protected state drives completion
     - conflicting route/query values cannot switch tenant context
   - If the codebase has an established pattern, either:
     - ignore conflicting route/query tenant values while safely completing only for `state.CompanyId`, or
     - reject the callback when mismatch is detected if that is more consistent with existing security behavior
   - Choose the option that best satisfies the acceptance criteria and existing conventions, but ensure no cross-tenant completion path exists.

5. **Update redirect behavior**
   - Keep current `ReturnUri` validation in place.
   - After successful callback completion:
     - if `state.ReturnUri` is present and passes allow-list/validation, redirect there
     - otherwise build the fallback redirect using `state.CompanyId`
   - Audit the fallback redirect builder and remove any dependency on route `companyId`.

6. **Keep behavior cohesive and explicit**
   - If redirect generation currently happens in multiple places, consolidate enough to ensure a single authoritative rule:
     - validated `state.ReturnUri` first
     - fallback based on `state.CompanyId`
   - Avoid duplicating redirect rules between legacy and new routes.

7. **Add or update tests**
   - Add focused tests for:
     - legacy callback route still works and delegates to shared completion
     - route `companyId` is ignored for identity resolution when protected state contains a different `CompanyId`
     - valid `state.ReturnUri` is used after successful completion
     - missing/rejected `ReturnUri` falls back using `state.CompanyId`
     - conflicting route/query tenant values cannot cause cross-tenant completion
   - Prefer integration/API tests for route and redirect behavior, plus unit tests for redirect builder/state-based resolution if those seams already exist.

8. **Keep changes small and aligned with architecture**
   - Respect modular boundaries:
     - API layer handles HTTP route binding
     - application layer handles callback completion and tenant-safe business logic
   - Do not move business rules into controllers/endpoints if a handler/service already exists.

# Validation steps
1. **Code search validation**
   - Search for remaining callback completion paths that still use route/query `companyId` after state unprotection.
   - Search for fallback redirect generation still taking route `companyId`.

2. **Automated tests**
   - Run targeted tests first, then broader suite:
   ```bash
   dotnet test
   ```
   If needed:
   ```bash
   dotnet build
   ```

3. **Behavioral verification via tests or local repro**
   Validate these scenarios:
   - Legacy route `/api/companies/{companyId}/mailbox-connections/{provider}/callback` returns/redirects successfully through shared completion flow.
   - When protected state has `CompanyId=A` and route has `companyId=B`, completion is scoped to `A` only and cannot complete for `B`.
   - When `state.ReturnUri` is valid, redirect target equals that URI.
   - When `state.ReturnUri` is null/empty/invalid, fallback redirect uses `state.CompanyId`.
   - Query tampering or route mismatch cannot produce cross-tenant completion.

4. **Regression check**
   - Confirm newer callback path still behaves the same.
   - Confirm provider callback success/failure handling remains intact.
   - Confirm no compile-time warnings/errors introduced by signature changes.

# Risks and follow-ups
- **Risk: hidden route-company coupling**
  - There may be multiple downstream services assuming route `companyId` is authoritative. Search thoroughly before finalizing.
- **Risk: inconsistent mismatch behavior**
  - If some code paths ignore mismatches and others reject them, behavior may become confusing. Keep one consistent rule in shared completion.
- **Risk: open redirect regression**
  - Be careful not to weaken existing `ReturnUri` validation while changing fallback behavior.
- **Risk: test fragility**
  - Callback tests may depend on protected state generation helpers or provider mocks; reuse existing fixtures/helpers where possible.

Follow-ups to note in code comments or task notes if discovered:
- Consolidate all callback routes onto one internal completion method if not already done.
- Add explicit documentation for callback state fields and trust boundaries.
- Consider a dedicated security test suite for callback tampering and tenant isolation if current coverage is thin.