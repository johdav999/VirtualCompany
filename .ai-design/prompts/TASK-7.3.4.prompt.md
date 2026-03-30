# Goal
Implement backlog task **TASK-7.3.4 — Role changes take effect on subsequent authorization checks** for story **ST-103 Human user invitation and role assignment** in the existing .NET solution.

Ensure that when a company membership role is changed, all **subsequent** authorization decisions use the updated role without requiring stale cached membership data, stale claims, or app restart. The implementation should preserve tenant scoping and align with the architecture’s **policy-based authorization in ASP.NET Core** and shared-schema multi-tenancy model.

# Scope
Focus only on the behavior required for this task:

- Make role updates on `company_memberships.role` effective for the next authorized request/check.
- Ensure authorization resolves membership/role from a current source of truth for the active company context.
- Update or add tests proving:
  - a user previously authorized for an action loses access after downgrade on a subsequent check/request
  - a user previously unauthorized gains access after upgrade on a subsequent check/request
- Keep implementation minimal and consistent with current architecture and code patterns.

Out of scope unless required by existing code structure:

- Full invitation flow
- Re-invite/revoke UX
- New role definitions beyond those already modeled
- Broad auth redesign
- SSO or token refresh redesign
- Mobile-specific changes unless shared auth code requires it

# Files to touch
Inspect and update the smallest set of relevant files, likely in these areas:

- `src/VirtualCompany.Api/**`
  - authorization policies/handlers
  - current company/tenant resolution
  - auth setup and middleware
  - membership-related endpoints if role update behavior is exercised there
- `src/VirtualCompany.Application/**`
  - membership queries/commands
  - authorization-facing services or abstractions
- `src/VirtualCompany.Domain/**`
  - membership entity/value objects if role semantics are modeled there
- `src/VirtualCompany.Infrastructure/**`
  - membership repository/query implementation
  - caching behavior if membership/role data is cached
- `src/VirtualCompany.Web/**`
  - only if server-side authorization state or role display logic depends on stale membership data
- Test projects in the solution
  - add/update unit/integration tests around authorization after role change

Before coding, identify the concrete files currently responsible for:

1. resolving current user identity
2. resolving active company context
3. loading membership/role
4. enforcing authorization policies
5. updating membership roles

# Implementation plan
1. **Trace the current authorization path**
   - Find how authenticated users are mapped to company memberships.
   - Determine whether authorization uses:
     - JWT/claims-only role data
     - cookie claims
     - cached membership snapshots
     - DB-backed policy handlers/services
   - Identify why role changes may not take effect immediately on subsequent checks.

2. **Define the intended source of truth**
   - Treat `company_memberships` in PostgreSQL as the source of truth for tenant-scoped human role authorization.
   - If current implementation stores role in auth claims, keep claims only for identity, not as the final authority for tenant membership role checks.
   - Authorization for tenant-scoped actions should resolve the current membership for:
     - current user
     - current company
     - active membership status
     - current role

3. **Refactor authorization to use fresh membership data**
   - Introduce or update a dedicated abstraction such as a current membership access service if one does not already exist.
   - Ensure policy handlers or authorization services query current membership state from the repository/DbContext on each request or per-request scoped load.
   - If caching exists, either:
     - remove it for membership role checks, or
     - invalidate it reliably on role change, with preference for the simpler and safer option for this task.
   - Preserve per-request efficiency by using scoped memoization if needed, but do not allow cross-request stale role data.

4. **Handle role updates cleanly**
   - Review the command/service that changes a membership role.
   - Ensure it updates `updated_at` and persists immediately.
   - If any app-side cache/session state exists for membership authorization, invalidate it on successful role change.
   - Do not rely on the affected user re-authenticating for the new role to apply on later requests.

5. **Align policy checks with tenant context**
   - Confirm authorization checks are tenant-aware and use both `user_id` and `company_id`.
   - Ensure missing/inactive membership results in deny.
   - Ensure role comparison supports the story roles already defined:
     - owner
     - admin
     - manager
     - employee
     - finance approver
     - support supervisor
   - Reuse existing role constants/enums if present; do not invent parallel role models.

6. **Add tests for subsequent authorization behavior**
   - Add focused tests at the highest-value level available in the repo, preferably integration tests if auth/policy plumbing is already testable.
   - Required scenarios:
     - User starts as `admin`, passes an admin-only authorization check, role changed to `employee`, next authorization check fails.
     - User starts as `employee`, fails an admin/manager-gated check, role changed to `admin` or `manager`, next authorization check succeeds.
   - If there is a membership status concept, include a deny case for non-active membership if easy to cover.
   - Avoid brittle UI-only tests; prefer API/application authorization tests.

7. **Keep backward compatibility and minimal surface area**
   - Do not redesign authentication unless necessary.
   - If claims currently include role, leave them in place only if harmless, but ensure policy evaluation does not trust stale tenant role claims over DB membership state.
   - Document any intentional distinction between platform auth identity and tenant membership authorization.

8. **Add concise code comments where needed**
   - Only where the behavior is non-obvious, especially if overriding default claims-based role assumptions.
   - Explain that tenant membership role is evaluated from current persisted membership so role changes apply on subsequent checks.

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Run targeted tests for membership authorization if test filtering is available.

3. Manually verify the behavior through tests or local API flow:
   - Create or seed a user with membership in a company.
   - Perform an action requiring elevated role and confirm success.
   - Change the membership role in the supported application path.
   - Repeat the protected action in a new request using the same authenticated session/token.
   - Confirm authorization now reflects the updated role.

4. Verify the inverse:
   - Start with insufficient role.
   - Confirm protected action is denied.
   - Upgrade role.
   - Retry in a subsequent request.
   - Confirm access is granted.

5. Confirm no tenant isolation regressions:
   - Authorization must still be scoped by active company context.
   - A role in one company must not authorize access in another company.

6. If caching was touched, verify:
   - no stale authorization across requests
   - no obvious duplicate-query/performance issue within a single request if scoped memoization is used

# Risks and follow-ups
- **Risk: stale claims-based authorization**
  - If current policies use `User.IsInRole(...)` or role claims directly, role changes may remain stale until re-login.
  - Mitigation: move tenant role checks to DB-backed membership authorization.

- **Risk: hidden caching layers**
  - Membership data may be cached in infrastructure, session, or Blazor server state.
  - Mitigation: audit and remove/invalidate any cross-request membership role cache.

- **Risk: ambiguous role hierarchy**
  - If role precedence is implicit or duplicated in multiple places, authorization bugs may persist.
  - Mitigation: centralize role evaluation logic if not already centralized.

- **Risk: multi-tenant leakage**
  - A DB-backed role lookup that ignores `company_id` could create cross-tenant authorization bugs.
  - Mitigation: always query by both `user_id` and active `company_id`.

Follow-ups after this task, only if discovered and not required to complete it:
- Consolidate tenant membership authorization behind a single service/policy helper.
- Add audit events for membership role changes if story/task chain expects it elsewhere.
- Review Blazor UI authorization helpers to ensure they do not over-trust stale role claims for visibility of sensitive actions.