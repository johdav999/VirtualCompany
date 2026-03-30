# Goal
Implement backlog task **TASK-7.1.1** for story **ST-101 Tenant-aware authentication and membership** so that users can:

- authenticate through an abstraction that supports future provider changes/SSO,
- resolve **zero, one, or many** company memberships after sign-in,
- establish an active company context for subsequent tenant-scoped requests,
- expose membership role data for downstream authorization.

This task specifically covers the acceptance slice: **“Users can authenticate and resolve one or more company memberships.”**

# Scope
In scope:

- Add or complete domain/application/infrastructure/API support for:
  - user identity lookup/creation from authenticated principal/provider claims,
  - company membership persistence and retrieval,
  - membership resolution for the signed-in user,
  - active company selection when a user belongs to multiple companies,
  - returning membership role/status data needed by authorization later.
- Introduce a clean auth provider abstraction rather than hard-coding one provider.
- Add API endpoints and/or application handlers for:
  - current authenticated user,
  - current user memberships,
  - selecting active company context if needed.
- Ensure the implementation fits the modular monolith / clean architecture structure already present.
- Add tests for the core membership resolution behavior.

Out of scope unless required by existing code structure:

- Full SSO implementation.
- Complete policy-based authorization across all modules.
- Full tenant scoping of every API request in the system.
- Invitation flows, onboarding wizard, or role management UX beyond what is necessary to validate membership resolution.
- Mobile-specific implementation unless shared APIs/contracts require no extra work.

# Files to touch
Inspect the solution first and then touch only the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - user, company, membership entities/value objects/enums
- `src/VirtualCompany.Application/**`
  - auth abstractions
  - current user service/contracts
  - membership query/command handlers
  - DTOs for resolved memberships and active company selection
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence mappings
  - repository implementations
  - auth provider / claims mapping implementation
  - migrations if persistence is not yet in place
- `src/VirtualCompany.Api/**`
  - authentication setup
  - endpoints/controllers for current user and memberships
  - middleware/services for active company context resolution if applicable
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared request/response models
- `src/VirtualCompany.Web/**`
  - only if a minimal company-selection UI already exists or is required to exercise the flow

Also review:

- `README.md`
- existing `Program.cs` / DI registration files
- existing test projects if present

Do not create new projects unless absolutely necessary.

# Implementation plan
1. **Inspect the existing architecture and auth setup**
   - Determine:
     - whether the app already uses ASP.NET Core Identity, JWT bearer, cookie auth, or external OIDC,
     - whether `User`, `Company`, and `CompanyMembership` already exist,
     - whether EF Core and migrations are already configured,
     - whether there is an existing current-user or tenant-context abstraction.
   - Preserve existing patterns and naming conventions.

2. **Model the membership resolution domain**
   - Ensure the core entities support the architecture/backlog model:
     - `User`
     - `Company`
     - `CompanyMembership`
   - Membership should include at least:
     - `Id`
     - `UserId`
     - `CompanyId`
     - `Role`
     - `Status`
     - timestamps
   - If enums/value objects exist, reuse them; otherwise add pragmatic ones for:
     - membership status (e.g. pending, active, suspended/revoked)
     - role as currently needed by story notes/backlog
   - Keep the model aligned with the shared-schema multi-tenant architecture.

3. **Add an authentication provider abstraction**
   - Introduce or complete an abstraction in the application layer for resolving the authenticated principal into an internal user identity.
   - The abstraction should support:
     - provider name
     - provider subject/identifier
     - email
     - display name
   - Infrastructure should implement mapping from ASP.NET Core `ClaimsPrincipal` to this abstraction.
   - Do not hard-code assumptions that prevent future SSO providers.

4. **Implement sign-in user resolution**
   - On authenticated requests, resolve the external/authenticated principal to an internal `User` record.
   - Behavior:
     - if a matching user exists by provider subject, use it;
     - if not, optionally match by normalized email if that is already an accepted pattern in the codebase;
     - otherwise create a new user record if the current architecture expects just-in-time provisioning.
   - Be conservative and avoid duplicate user creation.
   - Persist `auth_provider` and `auth_subject` consistently.

5. **Implement membership retrieval for the current user**
   - Add an application query/service that returns the signed-in user’s memberships.
   - Include enough data for downstream selection and authorization:
     - company id
     - company name
     - membership id
     - role
     - status
   - Filter out non-usable memberships if the product rules require it, but preserve status visibility where useful.
   - Sort deterministically.

6. **Implement active company resolution behavior**
   - Support these cases:
     - **0 memberships**: return authenticated user with no accessible companies; do not fabricate access.
     - **1 membership**: active company can be auto-resolved.
     - **multiple memberships**: require explicit selection or return a response indicating selection is required.
   - If the codebase already has a tenant-context mechanism, integrate with it.
   - If not, implement a minimal active-company selection flow using one of these existing-friendly approaches:
     - session/cookie-backed selected company for web,
     - claim/token augmentation if already supported,
     - request header only if there is already a tenant-context pattern.
   - Do not over-engineer; keep it compatible with later tenant-scoped authorization.

7. **Expose API endpoints**
   - Add or complete endpoints such as:
     - `GET /api/auth/me`
     - `GET /api/auth/memberships`
     - `POST /api/auth/active-company` or similar
   - Responses should clearly communicate:
     - authenticated user info,
     - memberships,
     - whether active company is resolved,
     - whether company selection is required.
   - Use existing API style (controllers vs minimal APIs, result wrappers, etc.).

8. **Persist and expose membership roles for authorization**
   - Ensure membership role data is available from the application layer and included in the resolved membership response.
   - If there is already a current-user context object, enrich it with:
     - user id
     - active company id if resolved
     - membership role
   - Do not implement full authorization policies here unless needed by existing plumbing, but make the data available for later stories.

9. **Database and persistence updates**
   - If tables/mappings do not exist, add EF Core configuration and a migration for:
     - `users`
     - `companies`
     - `company_memberships`
   - Respect the architecture guidance:
     - PostgreSQL-friendly schema
     - tenant-owned data linked by `company_id`
   - Add indexes/constraints that help correctness:
     - unique user email if intended by current model
     - uniqueness or duplicate prevention for membership combinations as appropriate
     - foreign keys between memberships, users, and companies

10. **Seed or test data support**
   - If the solution has a seed/dev-data mechanism, add minimal seed coverage for:
     - one user with one membership
     - one user with multiple memberships
   - Keep seed data deterministic and non-production.

11. **Add tests**
   - Add unit and/or integration tests for:
     - authenticated user resolves to internal user,
     - user with one membership gets auto-resolved company,
     - user with multiple memberships gets membership list and selection-required state,
     - user with no memberships gets no active company,
     - membership role/status are returned correctly.
   - Prefer application/integration tests over brittle controller-only tests.

12. **Document assumptions inline**
   - If no explicit acceptance criteria exist for this task beyond the story slice, encode behavior clearly in tests and comments where needed.
   - Keep implementation small, composable, and ready for the next ST-101 tasks:
     - tenant-scoped request enforcement,
     - forbidden/not-found behavior,
     - policy-based authorization.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly in the project’s normal workflow.

4. Manually validate the auth/membership flow using the implemented endpoints:
   - authenticated user with no memberships,
   - authenticated user with one membership,
   - authenticated user with multiple memberships.

5. Confirm response payloads include:
   - internal user identity,
   - membership list,
   - role/status per membership,
   - active company resolution state.

6. Confirm no cross-tenant access is implied or granted when no active membership exists.

7. Summarize in the final agent output:
   - files changed,
   - schema/migration changes,
   - endpoint contracts added/updated,
   - assumptions made about auth and active company selection.

# Risks and follow-ups
- **Existing auth stack mismatch**: the repository may already use a specific auth approach; adapt to it rather than replacing it.
- **Ambiguous active-company persistence**: if there is no existing tenant-context pattern, choose the smallest viable mechanism and note it clearly.
- **User deduplication edge cases**: matching by email vs provider subject can create account-linking ambiguity; be conservative.
- **Missing test infrastructure**: if integration tests are not set up, add focused unit tests and note the gap.
- **No explicit AC for this task slice**: behavior around zero memberships and selection UX may require assumptions; document them.

Follow-up tasks likely needed after this one:

- enforce company context on every tenant-owned API request,
- implement forbidden/not-found behavior for cross-tenant access,
- wire membership roles into ASP.NET Core policy-based authorization,
- add invitation acceptance and pending-membership flows,
- add web/mobile company switcher UX if not already present.