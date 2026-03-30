# Goal
Implement backlog task **TASK-7.3.1 — Owner/admin can invite users by email to a company** for story **ST-103 Human user invitation and role assignment** in the existing .NET solution.

Deliver the minimum complete vertical slice needed so that:
- an authenticated **owner/admin** can invite a user by email into the current company
- the invite creates a **pending membership/invitation state until accepted**
- assignable roles include:
  - `owner`
  - `admin`
  - `manager`
  - `employee`
  - `finance_approver`
  - `support_supervisor`
- the implementation fits the documented architecture:
  - multi-tenant
  - ASP.NET Core modular monolith
  - PostgreSQL-backed persistence
  - policy-based authorization
  - outbox-ready invitation delivery flow

If the codebase already has partial membership/company/auth models, extend them instead of duplicating concepts.

# Scope
In scope:
- Domain and persistence support for company user invitations
- API/application command to invite by email
- Authorization so only owner/admin can invite
- Pending state for invited users until acceptance
- Role validation and persistence
- Re-invite behavior if a pending invite already exists
- Revoke/cancel support only if there is already an obvious pattern and it is low effort; otherwise leave as follow-up
- Audit/outbox hook if project patterns already exist

Out of scope unless already trivial and clearly scaffolded:
- Full email delivery provider integration
- Full invitation acceptance UX
- SSO
- Advanced permission matrices beyond role assignment
- Mobile app work
- Broad refactors unrelated to invitation flow

Assumptions to honor:
- Tenant isolation must be enforced by `company_id`
- Human roles must remain separate from agent permissions
- Role changes should be represented in membership data for later authorization checks
- Prefer CQRS-lite patterns already present in the solution

# Files to touch
Inspect first, then update the most relevant existing files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- optionally `src/VirtualCompany.Web` if there is already an admin/company members UI

Likely file categories to touch:
- Domain entities/value objects/enums for:
  - company membership
  - invitation
  - membership role
  - membership status
- Application layer:
  - invite user command + handler
  - validators
  - DTOs/contracts
  - authorization requirements/policies if defined here
- Infrastructure:
  - EF Core entity configuration
  - migrations
  - repositories/query services
  - outbox message creation if supported
- API:
  - endpoint/controller/minimal API for invite action
  - request/response contracts
  - authorization attributes/policies
- Tests:
  - domain tests
  - application tests
  - API/integration tests where patterns exist

Concrete files are unknown from the prompt, so first locate:
- membership/company/user models
- auth/tenant resolution code
- existing role enums/constants
- existing migration strategy
- existing outbox/audit abstractions
- existing endpoint conventions

# Implementation plan
1. **Inspect current architecture in code before changing anything**
   - Find how these concepts are currently modeled:
     - `Company`
     - `User`
     - `CompanyMembership`
     - tenant context / current company resolution
     - authorization policies / role checks
   - Determine whether invitations should be:
     - a separate `CompanyInvitation` entity/table, or
     - represented via `CompanyMembership` with `Pending` status and nullable `UserId`
   - Prefer the approach that best matches the existing model and ST-103 acceptance criteria.
   - If no invitation model exists, the pragmatic default is:
     - add a dedicated invitation record for email + token/expiry metadata if acceptance is expected soon, **or**
     - extend `company_memberships` to support pending invited email if the codebase is still early and simpler.
   - Choose the smallest design that cleanly supports:
     - invite by email
     - pending until accepted
     - re-invite
     - future acceptance flow

2. **Add/confirm role and status modeling**
   - Ensure assignable human roles exist and are centralized:
     - `owner`
     - `admin`
     - `manager`
     - `employee`
     - `finance_approver`
     - `support_supervisor`
   - Ensure membership/invitation status supports at least:
     - `pending`
     - `active`
     - optionally `revoked` / `cancelled`
   - Avoid stringly-typed role logic scattered across layers; use enum/constants/value object pattern consistent with the codebase.

3. **Implement domain rules**
   - Add domain behavior or application validation for:
     - only valid roles can be assigned
     - owner/admin can invite
     - email must be normalized
     - duplicate active membership for same company/email should be rejected
     - duplicate pending invite should be handled as re-invite/update rather than creating duplicates
   - Re-invite behavior:
     - if pending invite exists for same company + email, refresh invitation metadata and role if appropriate
     - do not create multiple pending records for the same company/email
   - Preserve tenant boundaries in all lookups.

4. **Persist invitation/pending membership state**
   - Update EF Core mappings and create a migration.
   - If using `company_memberships`, ensure schema can represent:
     - `company_id`
     - `user_id` nullable until acceptance if needed
     - invited email / normalized email
     - role
     - status = pending
     - timestamps
   - If using a dedicated invitation table, ensure it links cleanly to membership creation later.
   - Add unique constraints/indexes to prevent duplicate pending/active records where appropriate.

5. **Add application command**
   - Implement something like `InviteCompanyUserCommand` with fields:
     - `CompanyId` or implicit current company from tenant context
     - `Email`
     - `Role`
   - Handler responsibilities:
     - resolve current user and current company context
     - verify inviter has owner/admin rights in that company
     - normalize email
     - validate role
     - check for existing membership/invitation
     - create or re-invite pending record
     - persist changes
     - enqueue outbox notification if infrastructure exists
     - return a clear result DTO

6. **Add API endpoint**
   - Add a secure endpoint under the existing company/membership/admin route conventions, e.g.:
     - `POST /api/companies/{companyId}/memberships/invitations`
     - or tenant-context-based equivalent already used in the app
   - Require authenticated user and owner/admin authorization.
   - Request body should include:
     - `email`
     - `role`
   - Response should clearly indicate:
     - invited email
     - assigned role
     - status `pending`
     - whether it was newly created or re-invited

7. **Hook into authorization**
   - Reuse existing policy-based authorization if present.
   - Add or extend policy/requirement so invite action is limited to owner/admin memberships in the current company.
   - Do not rely only on UI hiding; enforce server-side.

8. **Add outbox/audit integration if patterns exist**
   - If an outbox abstraction already exists:
     - emit an invitation notification event/message for background delivery
   - If business audit events already exist:
     - record an audit event for invitation created/re-invited
   - If neither exists yet, leave a clear TODO/follow-up rather than inventing a large new subsystem for this task.

9. **Add tests**
   - Add tests aligned to existing test style.
   - Minimum scenarios:
     - owner can invite user by email
     - admin can invite user by email
     - non-owner/admin cannot invite
     - invite creates pending state
     - invalid role is rejected
     - duplicate active membership is rejected
     - duplicate pending invite results in re-invite/update, not duplicate row
     - tenant isolation is enforced
   - Prefer integration tests for endpoint + persistence if test infrastructure exists.

10. **Optional thin web UI support**
   - Only if the web project already has a company settings/members page and adding a simple form is straightforward.
   - If not, API + tests is sufficient for this task.

# Validation steps
1. Inspect and restore/build solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation:
   - run targeted tests for affected projects if available
   - then run full suite:
     - `dotnet test`

4. Verify migration compiles and applies in the project’s normal way.
   - If migrations are used, generate/update migration and ensure no broken model snapshot.
   - Do not guess CLI startup project paths; inspect existing migration conventions first.

5. Manually verify API behavior with the project’s existing API tooling pattern:
   - authenticated owner/admin request with valid email + role returns success
   - resulting record is pending
   - same email invited again re-invites instead of duplicating
   - non-owner/admin gets forbidden
   - cross-company access is forbidden/not found per existing tenant conventions

6. Confirm authorization semantics:
   - role changes and pending membership data are persisted in a way that subsequent authorization checks can consume once acceptance is implemented

# Risks and follow-ups
- **Modeling risk:** The codebase may already represent invitations differently than the backlog implies. Prefer extending existing membership/invitation patterns over introducing parallel concepts.
- **Acceptance flow gap:** This task is only the invite side. If no acceptance mechanism exists yet, design persistence so acceptance can later map pending invite to a real user membership cleanly.
- **Email delivery gap:** Outbox/event creation may be possible now, but actual email sending may belong to a later task.
- **Role naming drift:** The codebase may already use different casing or enum names. Keep external/API contracts stable and map internally if needed.
- **Authorization risk:** Ensure invite permission is checked against the inviter’s membership in the current company, not global app roles.
- **Tenant isolation risk:** All queries and uniqueness checks must include `company_id`.
- **Follow-up candidates:**
  - invitation acceptance endpoint and token flow
  - revoke/cancel invitation
  - list pending invitations and memberships in company settings
  - resend invitation endpoint
  - audit event surfacing in UI
  - background email dispatcher integration