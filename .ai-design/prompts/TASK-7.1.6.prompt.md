# Goal

Implement backlog task **TASK-7.1.6 — Keep auth provider abstraction for future SSO** for story **ST-101 Tenant-aware authentication and membership**.

The coding agent should ensure the authentication design is **provider-agnostic**, so the current implementation does not hardcode a single auth mechanism into domain, application, or API flows. The result should preserve a clean abstraction that supports adding **SSO/OIDC/SAML/external identity providers later** without rewriting tenant membership resolution or authorization logic.

# Scope

In scope:

- Review the current authentication and user resolution flow across API, application, infrastructure, and web/mobile entry points.
- Introduce or refine an **auth provider abstraction** that separates:
  - external authentication identity
  - internal user record resolution
  - company membership resolution
  - tenant-scoped authorization
- Ensure the `users` model usage aligns with architecture guidance:
  - `auth_provider`
  - `auth_subject`
- Prevent direct coupling to a single provider in business logic.
- Keep ASP.NET Core policy-based authorization compatible with the abstraction.
- Update dependency injection and any claims-mapping code so provider-specific details are isolated.
- Add or update tests for provider abstraction behavior and regression coverage.

Out of scope unless required by existing code to complete the task:

- Full SSO implementation
- New external identity provider integration
- UI redesign
- Broad authorization redesign unrelated to auth provider abstraction
- Database redesign beyond minimal schema/config alignment needed for this task

# Files to touch

Start by inspecting these likely areas and modify only what is necessary:

- `src/VirtualCompany.Api/**`
  - authentication setup
  - claims mapping
  - current user / tenant context resolution
  - authorization policies
- `src/VirtualCompany.Application/**`
  - interfaces for current user identity
  - membership resolution services
  - auth-related application services
- `src/VirtualCompany.Domain/**`
  - user/auth identity value objects or entities, if present
- `src/VirtualCompany.Infrastructure/**`
  - concrete auth provider adapters
  - persistence for user auth identity fields
  - repository implementations
- `src/VirtualCompany.Web/**`
  - any auth bootstrapping or assumptions about provider-specific claims
- `src/VirtualCompany.Mobile/**`
  - only if it directly depends on provider-specific auth assumptions
- `README.md`
  - only if there is a concise architecture/config note worth updating

Also inspect:

- EF Core entity configurations and migrations
- options/config classes for auth
- test projects under `tests/**` or any existing test folders in `src/**`

# Implementation plan

1. **Inspect the current auth flow**
   - Identify how authenticated principals are currently created and consumed.
   - Find any hardcoded assumptions such as:
     - direct use of a specific claim type everywhere
     - provider-specific IDs treated as internal user IDs
     - business logic depending on ASP.NET `ClaimsPrincipal`
     - direct references to a specific auth vendor in application/domain layers

2. **Define a provider-agnostic identity contract**
   - Introduce or refine an application-layer abstraction such as:
     - `IExternalIdentityAccessor`
     - `IAuthenticatedUserContext`
     - `IAuthProviderIdentityResolver`
   - The abstraction should expose normalized identity data, for example:
     - provider name/key
     - provider subject/user identifier
     - email
     - display name
     - authentication status
     - raw claims only if truly needed and kept at boundary layers
   - Keep this contract in **Application** (or a shared boundary layer), not Infrastructure-only.

3. **Normalize provider identity mapping at the boundary**
   - In API/auth setup, map framework/provider claims into a normalized model once.
   - Avoid scattering claim-type lookups across controllers/services.
   - Encapsulate provider-specific claim extraction in a dedicated adapter/service.
   - If there is already a current-user service, refactor it so it returns normalized auth identity rather than provider-specific claim assumptions.

4. **Separate external identity from internal user resolution**
   - Ensure internal user lookup/creation uses `(auth_provider, auth_subject)` as the stable external identity key.
   - Do not assume email is immutable or unique across providers for identity binding unless the existing design explicitly requires it.
   - If needed, add a repository/service method like:
     - `GetByExternalIdentityAsync(provider, subject, ...)`
   - Keep membership resolution based on internal user records and company memberships, not directly on claims.

5. **Preserve tenant-aware authorization flow**
   - Ensure company membership and tenant scoping continue to work after abstraction changes.
   - Authorization checks should rely on:
     - resolved internal user
     - selected/current company context
     - membership role/permissions
   - Do not let provider-specific claims bypass membership checks.

6. **Keep ASP.NET Core policy-based authorization compatible**
   - If policies currently inspect raw claims, consider moving them toward normalized requirements/handlers where appropriate.
   - Keep changes minimal, but ensure future SSO providers can satisfy the same policies through normalized identity mapping.

7. **Refactor DI and configuration**
   - Register the abstraction and its concrete implementation(s).
   - If there is a single current provider, keep it behind the abstraction.
   - Add options/config naming that supports multiple providers later, without implementing them now.

8. **Update persistence alignment if needed**
   - Verify the user persistence model includes or supports:
     - `auth_provider`
     - `auth_subject`
   - If the schema/entity names differ, align code carefully with the existing migration strategy.
   - Only add a migration if the fields are missing and the task cannot be completed cleanly otherwise.

9. **Add tests**
   - Add unit tests for:
     - normalized claim/provider mapping
     - external identity to internal user resolution
     - behavior when provider/subject is missing or invalid
   - Add integration tests if the solution already has them for auth/tenant flows.
   - Ensure tests prove the app no longer depends on one provider-specific claim shape in core logic.

10. **Keep the implementation minimal and extensible**
    - Do not build full multi-provider orchestration now.
    - The goal is a clean seam for future SSO, not speculative complexity.
    - Prefer small abstractions with one current implementation.

# Validation steps

Run and verify:

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate code paths by inspection and, if runnable locally, confirm:
   - authentication setup maps external identity through a single abstraction
   - application services do not depend directly on provider-specific claims
   - user resolution uses normalized provider + subject identity
   - membership/tenant authorization still depends on internal user + company membership
   - no new direct coupling to a specific auth vendor was introduced

4. If migrations were added:
   - verify they compile
   - verify model snapshot consistency
   - ensure no destructive schema changes were introduced unintentionally

5. Confirm the final diff is focused and coherent:
   - abstraction in the correct layer
   - provider-specific logic isolated to boundary/infrastructure
   - no dead code or placeholder SSO implementation

# Risks and follow-ups

Risks:

- Existing code may already mix authentication, user provisioning, and tenant resolution tightly, making a clean refactor broader than expected.
- Claims may be consumed in multiple places, causing subtle regressions if not fully normalized.
- If internal user identity currently relies on email, shifting toward `(auth_provider, auth_subject)` may expose hidden assumptions.
- Authorization handlers or policies may still depend on raw claims after the refactor.

Follow-ups to note in code comments or PR summary if applicable:

- Add support for multiple configured auth providers when SSO work begins.
- Consider a dedicated external identity table if one internal user must later link to multiple providers.
- Add explicit company-selection/session handling if users can belong to multiple companies.
- Expand integration tests around forbidden/not-found tenant isolation behavior under different auth providers.
- Document the normalized identity contract and expected claim mapping for future SSO adapters.