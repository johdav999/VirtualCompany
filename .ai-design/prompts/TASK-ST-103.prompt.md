# Goal
Implement backlog task **TASK-ST-103 — Human user invitation and role assignment** for the .NET modular monolith so that company **owner/admin** users can invite teammates by email, assign supported human roles, manage pending memberships, and have role changes reflected in subsequent authorization checks.

# Scope
Deliver the story in a way that fits the existing architecture and backlog context for **EP-1 Multi-tenant foundation and company setup**.

Include:

- Tenant-scoped invitation flow for human users
- Supported roles:
  - `owner`
  - `admin`
  - `manager`
  - `employee`
  - `finance_approver`
  - `support_supervisor`
- Pending membership state until invite acceptance
- Re-invite flow
- Revoke/cancel flow
- Role update flow for existing memberships
- Authorization enforcement so only `owner`/`admin` can manage invitations and roles
- Persistence model updates as needed
- API endpoints and application commands/queries
- Basic web UI support if the web project already has company/member administration patterns
- Outbox/event hook for invitation delivery, but do **not** build a full email provider integration if none exists yet; persist/send via existing outbox abstraction or create a minimal placeholder event for background dispatch
- Audit/business event hooks where consistent with current codebase patterns

Do **not**:
- Couple human roles to agent permissions
- Implement SSO
- Build a full notification center
- Over-engineer with microservices
- Add mobile-specific functionality for this task

If the current codebase already contains partial auth, tenant resolution, membership, or onboarding work from ST-101/ST-102, extend those patterns rather than introducing parallel implementations.

# Files to touch
Inspect the solution first and then update the appropriate files in these areas.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - membership entity/value objects/enums
  - invitation entity if needed
  - domain rules for role/state transitions
- `src/VirtualCompany.Application/**`
  - commands/handlers for invite, accept, re-invite, revoke, change role
  - queries for listing memberships/invitations
  - DTOs/contracts
  - authorization policies/requirements
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories
  - migrations
  - outbox persistence/event dispatch integration
- `src/VirtualCompany.Api/**`
  - endpoints/controllers/minimal APIs
  - auth policy wiring
  - request/response contracts if API-owned
- `src/VirtualCompany.Web/**`
  - membership/invitation management UI
  - accept invitation flow if web handles it
- `README.md`
  - only if setup/run notes must be updated

Expected persistence additions if not already present:

- Extend `company_memberships` with status support if missing
- Add invitation tracking table, e.g. `company_invitations`, with fields such as:
  - `id`
  - `company_id`
  - `email`
  - `role`
  - `status` (`pending`, `accepted`, `revoked`, `expired`, `cancelled`)
  - `invited_by_user_id`
  - `accepted_by_user_id` nullable
  - `token_hash` or secure acceptance key
  - `expires_at`
  - `last_sent_at`
  - `created_at`
  - `updated_at`

Prefer aligning with existing schema naming conventions if they differ.

# Implementation plan
1. **Inspect current foundation before coding**
   - Review existing implementations for:
     - authentication
     - tenant resolution
     - company membership model
     - authorization policies
     - onboarding/company creation
     - outbox/background dispatch
   - Determine whether invitations should be:
     - a separate entity/table, or
     - represented through membership + invite token metadata
   - Prefer a separate invitation record if the codebase does not already model invites cleanly.

2. **Model the domain**
   - Add or refine domain concepts for:
     - human membership roles
     - membership status
     - invitation status
   - Enforce allowed roles centrally via enum/value object/constants, not scattered strings.
   - Add domain behavior/rules such as:
     - only supported roles are assignable
     - invite email normalized consistently
     - owner/admin required for invite management
     - accepted invitation creates or activates membership
     - revoked/cancelled invitation cannot be accepted
     - role changes update membership and authorization-relevant state
   - Preserve separation between human roles and agent permissions.

3. **Design persistence**
   - Update EF models/configurations and add migration(s).
   - Ensure tenant-owned records include `company_id`.
   - Add indexes/constraints for common cases:
     - lookup by `company_id + email`
     - lookup by acceptance token hash
     - uniqueness rules that prevent duplicate active/pending invites where appropriate
   - If memberships already exist, ensure they support statuses like:
     - `pending`
     - `active`
     - `revoked` or equivalent
   - Keep schema pragmatic and aligned with the architecture’s shared-schema multi-tenancy.

4. **Implement application commands**
   - Add commands/handlers for:
     - `InviteUserToCompany`
     - `AcceptCompanyInvitation`
     - `ReinviteUserToCompany`
     - `RevokeCompanyInvitation`
     - `ChangeCompanyMembershipRole`
     - optionally `ListCompanyMemberships` / `ListPendingInvitations`
   - Command behavior:
     - **Invite**
       - validate caller is owner/admin in current company
       - validate role is supported
       - normalize email
       - prevent invalid duplicates
       - create pending invitation
       - optionally create pending membership only on acceptance, or create pending membership now if that matches current model
       - enqueue outbox event for invitation delivery
     - **Accept**
       - validate token
       - validate not expired/revoked/already accepted
       - resolve or create user account linkage as appropriate to current auth model
       - create/activate membership with assigned role
       - mark invitation accepted
     - **Re-invite**
       - allowed for pending/recently expired invitations per chosen rules
       - rotate token if appropriate
       - update timestamps
       - enqueue delivery event again
     - **Revoke**
       - mark invitation revoked/cancelled
       - ensure future acceptance fails safely
     - **Change role**
       - update existing membership role
       - ensure subsequent authorization checks use persisted role
       - consider guardrail for last owner demotion if ownership rules already exist or are easy to add safely

5. **Authorization**
   - Add or extend ASP.NET Core policy-based authorization.
   - Ensure only `owner` and `admin` can:
     - invite users
     - re-invite
     - revoke
     - change roles
   - Ensure all membership/invitation operations are tenant-scoped by current company context.
   - Ensure role changes are reflected on subsequent requests by reading current membership state from the authoritative store or invalidating any cached claims/session state if needed.
   - If the app stamps role claims at sign-in, add the minimal mechanism needed so changed roles do not remain stale longer than acceptable for “subsequent authorization checks.”

6. **API surface**
   - Add tenant-scoped endpoints, following existing API conventions, for example:
     - `POST /api/companies/{companyId}/invitations`
     - `POST /api/companies/{companyId}/invitations/{invitationId}/resend`
     - `POST /api/companies/{companyId}/invitations/{invitationId}/revoke`
     - `POST /api/invitations/accept`
     - `PATCH /api/companies/{companyId}/memberships/{membershipId}/role`
     - `GET /api/companies/{companyId}/memberships`
     - `GET /api/companies/{companyId}/invitations`
   - Use existing route patterns if different.
   - Return safe, clear errors for:
     - forbidden access
     - invalid role
     - duplicate invite
     - expired/revoked token
     - cross-tenant access attempts

7. **Outbox / delivery hook**
   - If an outbox pattern already exists, publish an invitation event/message containing only required delivery data.
   - If no delivery pipeline exists yet, still persist an outbox record or internal integration event so the story aligns with architecture and notes.
   - Do not block the request on actual email sending.
   - If necessary, add a temporary no-op/background log dispatcher with clear TODO comments.

8. **Web UI**
   - If the Blazor web app already has company settings/admin pages, add:
     - member list
     - pending invitations list
     - invite form with email + role
     - actions for resend/revoke
     - role edit for active memberships
   - Keep UI simple and server-driven.
   - Hide/disable admin actions for unauthorized users.
   - If no suitable UI exists yet, implement the backend fully and add only the thinnest viable page/component consistent with current app structure.

9. **Validation and edge cases**
   - Cover:
     - inviting an email already active in the company
     - inviting an email with an existing pending invite
     - accepting with a different signed-in email/account than invited, if auth model supports identity matching
     - re-inviting expired invites
     - revoking after resend
     - changing role for self
     - preventing accidental removal/demotion of the last owner if feasible within current scope
   - Prefer explicit business errors over silent overwrites.

10. **Tests**
   - Add unit and/or integration tests around:
     - invite creation
     - tenant scoping
     - owner/admin authorization
     - acceptance flow
     - role change effect
     - revoke/reinvite behavior
     - duplicate invite prevention
   - Favor application-layer and API integration tests over brittle UI-only tests.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. Apply migrations if this repo uses EF Core migrations in the normal workflow.
   - Add the migration for invitation/membership schema updates.
   - Verify the app starts with the new schema.

4. Validate happy paths:
   - Owner/admin invites a user by email with each supported role
   - Pending invitation is persisted
   - Outbox/event record is created
   - Invited user accepts and receives active membership
   - Membership appears in company member listing
   - Role change succeeds and is reflected on the next authorized request

5. Validate authorization:
   - Non-owner/admin cannot invite, revoke, resend, or change roles
   - Cross-tenant access to invitations/memberships is forbidden or not found per existing conventions

6. Validate edge cases:
   - Duplicate pending invite is rejected or handled per chosen rule
   - Revoked invitation cannot be accepted
   - Expired invitation cannot be accepted without re-invite
   - Changing to unsupported role fails validation
   - Existing active member cannot be re-invited incorrectly

7. Run full verification again:
   - `dotnet test`
   - `dotnet build`

8. If web UI is added, manually verify:
   - invite form renders
   - pending invites list updates
   - resend/revoke actions work
   - role edit updates visible state
   - unauthorized users do not see admin controls

# Risks and follow-ups
- **Current auth/session model may cache roles in claims**, causing stale authorization after role changes. If so, implement the smallest safe fix now and note any broader identity refresh work needed.
- **Invitation acceptance depends on existing authentication flow**. If sign-up/sign-in linkage is incomplete, keep acceptance flow compatible with current auth and document any follow-up needed for polished onboarding.
- **Last-owner protection** may not be trivial if ownership rules are not yet modeled. Add it if straightforward; otherwise document as a follow-up risk.
- **Email delivery may not exist yet**. Use outbox/event persistence now and avoid hard dependency on a real provider.
- **Duplicate identity scenarios** (same email, multiple memberships, multiple companies) must remain tenant-safe and should align with ST-101 membership resolution behavior.
- **Audit events** for invite, accept, revoke, and role change are desirable; implement if the audit module foundation already exists, otherwise leave structured hooks/TODOs.
- Follow-up candidates if not completed here:
  - invitation expiration policy configuration
  - self-service leave/remove membership
  - richer membership admin UI
  - email templates/provider integration
  - owner transfer workflow
  - stronger session invalidation/claim refresh strategy