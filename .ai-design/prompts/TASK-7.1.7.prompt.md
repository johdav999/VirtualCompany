# Goal
Implement backlog task **TASK-7.1.7 — Prefer policy-based authorization in ASP.NET Core** for story **ST-101 Tenant-aware authentication and membership**.

Update the solution so authorization decisions are expressed through **ASP.NET Core authorization policies and handlers** rather than scattered role checks, ad hoc claim inspection, or controller/page-level imperative logic. The implementation must support the multi-tenant membership model described in the backlog and architecture, with authorization grounded in **company membership and tenant context**.

The result should make it straightforward for the app to:
- authorize access based on authenticated user + active company context
- enforce membership presence before tenant-owned operations
- support role-based requirements through policies
- provide a clean foundation for future tenant-scoped resource authorization

# Scope
In scope:
- Inspect the current auth/authorization setup across API and web projects.
- Introduce or refactor to **policy-based authorization** using:
  - `AddAuthorization(...)`
  - named policies
  - custom authorization requirements/handlers where needed
  - reusable constants/extensions for policy names
- Ensure tenant-aware authorization is based on persisted membership/company context, not only generic identity roles.
- Replace obvious direct role checks / inline authorization logic with policy usage where practical in this task.
- Wire policies into endpoints/controllers/pages/components that already require authenticated tenant access.
- Keep implementation aligned with shared-schema multi-tenancy and `company_id` enforcement direction.

Out of scope unless required to complete compilation:
- Large redesign of authentication provider integration
- Full row-level data isolation implementation
- New UI flows for login/company switching
- Broad permission matrix beyond what is needed to establish policy-based membership/role authorization
- Mobile-specific authorization changes unless shared code requires it

# Files to touch
Start by inspecting these likely locations and adjust based on actual code structure:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Web/Program.cs`
- Any existing auth setup files under:
  - `src/VirtualCompany.Api/**/Auth*.cs`
  - `src/VirtualCompany.Web/**/Auth*.cs`
  - `src/VirtualCompany.Infrastructure/**/Identity*.cs`
  - `src/VirtualCompany.Application/**/Security*.cs`
- Membership/domain models and services:
  - `src/VirtualCompany.Domain/**/CompanyMembership*.cs`
  - `src/VirtualCompany.Application/**/Membership*.cs`
  - `src/VirtualCompany.Infrastructure/**/Membership*.cs`
- Endpoint/UI files currently using direct role checks or `[Authorize(Roles = ...)]`
- Shared constants/helpers if appropriate:
  - `src/VirtualCompany.Shared/**/Authorization*.cs`
  - or a new shared/application-level authorization constants file

Create new files as needed for a clean design, likely including:
- authorization policy name constants
- custom requirements
- authorization handlers
- service registration extensions

# Implementation plan
1. **Assess current authorization usage**
   - Search for:
     - `[Authorize]`
     - `[Authorize(Roles = ...)]`
     - `User.IsInRole(...)`
     - direct claim checks
     - manual membership checks in controllers/endpoints/pages
   - Identify the current source of tenant/company context:
     - route value
     - header
     - claim
     - session/state
     - selected company service
   - Identify how memberships are persisted and queried.

2. **Define a policy model**
   - Introduce a small, explicit set of named policies, for example:
     - `AuthenticatedUser`
     - `CompanyMember`
     - `CompanyOwnerOrAdmin`
     - optional role-specific policies such as `FinanceApprover`, `SupportSupervisor`, etc. only if already needed by current code
   - Prefer policy names in a central constants class to avoid string duplication.

3. **Implement tenant-aware requirements and handlers**
   - Add custom authorization requirements/handlers where role claims alone are insufficient.
   - At minimum, implement a requirement that verifies the current user has an active membership for the resolved company context.
   - If the app already has selected-company resolution, use that consistently.
   - If role authorization must be tenant-specific, implement a requirement that checks membership role within the active company rather than global ASP.NET identity roles.

4. **Resolve company context consistently**
   - Reuse existing company/tenant resolution if present.
   - If missing, add a minimal abstraction such as a current company/tenant accessor used by authorization handlers.
   - Do not invent a large framework; keep it small and composable.
   - Ensure behavior is safe when company context is absent:
     - policy should fail cleanly
     - no accidental authorization bypass

5. **Register policies in ASP.NET Core**
   - Update startup/Program registration in API and/or Web as appropriate.
   - Add authorization handlers to DI.
   - Configure named policies using requirements rather than role strings where tenant membership matters.

6. **Refactor endpoint/page authorization usage**
   - Replace direct role usage and inline checks with `[Authorize(Policy = ...)]` or equivalent endpoint policy attachment.
   - Keep imperative checks only where resource-specific authorization is unavoidable; if used, route them through `IAuthorizationService`.
   - Preserve existing behavior as much as possible while moving to policy-based enforcement.

7. **Keep authorization aligned with ST-101**
   - Ensure membership roles remain available to authorization checks.
   - Ensure tenant-owned requests require company membership.
   - Ensure unauthorized cross-company access fails through authorization rather than relying only on UI hiding.

8. **Add or update tests**
   - Add focused tests for authorization handlers/policies if the solution has a test project pattern.
   - If tests already exist for endpoints, update them to validate policy behavior.
   - Cover at least:
     - authenticated user with valid membership succeeds
     - authenticated user without membership fails
     - wrong company context fails
     - owner/admin policy succeeds only for matching membership role

9. **Document assumptions in code comments only where necessary**
   - Keep comments concise.
   - If tenant context resolution has limitations, note them in a targeted TODO rather than broad commentary.

# Validation steps
Run and verify the following:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Perform a code search to confirm migration toward policy-based authorization:
   - search for `[Authorize(Roles =`
   - search for `User.IsInRole(`
   - search for ad hoc membership checks in HTTP/UI entry points
   - replace or justify any remaining occurrences

4. Manually verify startup registration:
   - policies are registered
   - handlers are registered in DI
   - no missing service dependencies at app startup

5. Manually verify authorization flow in code:
   - tenant/company context is resolved before membership policy evaluation
   - missing company context denies access safely
   - membership role checks are tenant-specific, not global-role-based, where applicable

6. If runnable locally, smoke test protected routes/endpoints:
   - authenticated user with membership can access tenant-scoped endpoint
   - authenticated user without membership is denied
   - user cannot access another company’s route/context

# Risks and follow-ups
- **Tenant context ambiguity:** If the current app does not yet have a single authoritative company context resolver, policy enforcement may be inconsistent. Keep the implementation minimal but centralize resolution as much as possible.
- **Role source mismatch:** If current code relies on global identity roles, switching to membership-role authorization may expose hidden assumptions. Update only what is necessary for correctness and compilation, and note follow-up areas.
- **Resource vs. coarse-grained authorization:** Policies can enforce membership and tenant role checks, but resource ownership checks may still require `IAuthorizationService` with resource-based authorization later.
- **Web vs. API divergence:** If both projects configure auth separately, ensure policy definitions do not drift. Consider a shared registration extension if duplication appears.
- **Insufficient tests:** If there is no existing test project coverage for auth, add the smallest practical tests now and note broader authorization test coverage as a follow-up.
- **Future follow-up:** Extend the policy model to cover finer-grained permissions and resource-based checks for agents, approvals, workflows, and audit views as additional stories are implemented.