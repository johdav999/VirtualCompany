# Goal
Implement backlog task **TASK-7.3.2 — Invited users receive a pending membership until accepted** for story **ST-103 Human user invitation and role assignment** in the existing .NET modular monolith.

The coding agent should add or complete the invitation/membership flow so that when an owner/admin invites a user by email to a company:

- a **company membership record is created immediately**
- that membership is created with **status = pending**
- the invited user does **not** receive active access to the company until acceptance
- acceptance transitions the membership to an active/accepted state
- subsequent authorization checks use the updated membership status

No explicit acceptance criteria were provided for this task beyond the story-level requirement, so implement the smallest complete vertical slice that fits the architecture and existing codebase conventions.

# Scope
In scope:

- Domain and persistence support for membership status lifecycle, at minimum:
  - pending
  - active/accepted
  - optionally revoked/cancelled if already modeled
- Invitation flow updates so invites create a pending membership instead of immediate active access
- Acceptance flow updates so accepting an invitation activates the pending membership
- Authorization/query behavior so pending memberships do not grant tenant access
- Application/API behavior for:
  - inviting a user by email
  - accepting an invitation or pending membership
  - reading memberships/invitations in a way that exposes pending state where needed
- Tests covering the pending-to-active transition and access restrictions

Out of scope unless already partially implemented and trivial to finish safely:

- Full email delivery UX/content beyond existing outbox hooks
- Re-invite and revoke flows unless required to support the pending membership implementation
- New frontend experiences beyond minimal wiring needed for existing screens/endpoints
- SSO or external identity provider changes
- Broad refactors unrelated to invitation/membership lifecycle

Implementation constraints:

- Preserve **shared-schema multi-tenancy** with `company_id` enforcement
- Keep human roles separate from agent permissions
- Follow **Clean Architecture / modular monolith** boundaries
- Prefer policy-based authorization in ASP.NET Core
- Do not grant company access from a pending membership

# Files to touch
Inspect the solution first and then update the actual files that match these responsibilities. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - membership aggregate/entity/value objects
  - invitation aggregate/entity if present
  - enums/constants for membership status and roles
  - domain methods for invite/accept lifecycle
- `src/VirtualCompany.Application/**`
  - commands/handlers for invite user
  - commands/handlers for accept invitation
  - DTOs/view models for membership status
  - validators
  - authorization or tenant resolution services that evaluate active memberships only
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations
  - query filters or tenant membership lookup implementations
  - outbox integration if invitation side effects are already modeled
- `src/VirtualCompany.Api/**`
  - invitation/membership endpoints/controllers
  - request/response contracts if API-owned
  - auth/tenant resolution middleware or services if API layer owns them
- `src/VirtualCompany.Web/**`
  - only if existing invite/accept UI needs small updates to reflect pending status
- Tests in whichever projects exist, likely under:
  - `tests/**` or `src/**.Tests/**`
  - application tests
  - domain tests
  - API/integration tests

Also review:

- `README.md`
- existing migration history
- any architecture/conventions docs in the repo

Do not invent new projects unless the solution already uses them.

# Implementation plan
1. **Inspect current invitation and membership model**
   - Find existing implementations for:
     - `company_memberships`
     - invitation entities/tables if any
     - role assignment
     - tenant resolution after sign-in
     - authorization checks based on membership
   - Determine whether the system currently:
     - creates memberships only after acceptance
     - creates active memberships immediately
     - lacks explicit membership status handling
   - Reuse existing naming and patterns.

2. **Model membership status explicitly**
   - Ensure the domain model for company membership has a status field with at least:
     - `Pending`
     - `Active` (or `Accepted`, but prefer one canonical active-access state)
   - If status already exists, verify semantics and normalize usage.
   - Add domain behavior such as:
     - `CreatePending(...)`
     - `Accept()`
     - guards against invalid transitions, e.g. accepting an already active or revoked membership
   - Keep role assignment on the membership record even while pending.

3. **Update persistence schema/configuration**
   - Ensure `company_memberships.status` is persisted and required.
   - If needed, add/update EF configuration and create a migration.
   - Backfill existing rows safely:
     - if prior behavior implied active memberships, migrate existing null/legacy rows to `Active`
   - Preserve tenant ownership via `company_id`.

4. **Change invite flow to create pending membership**
   - In the invite-user command/handler/service:
     - validate inviter is owner/admin
     - validate target role is assignable
     - create or upsert the membership for the target email/user in `Pending` state
   - If the system separates invitation from membership:
     - still ensure a pending membership exists at invite time
     - link invitation token/record to that membership if applicable
   - Handle duplicate cases carefully:
     - if an active membership already exists for that user/company, reject or no-op with a clear error
     - if a pending membership already exists, prefer idempotent behavior or refresh invitation metadata per existing conventions

5. **Implement/complete acceptance flow**
   - On invitation acceptance:
     - resolve the invitation or pending membership
     - ensure the accepting authenticated user matches the invited identity/email as required by current design
     - transition membership status from `Pending` to `Active`
     - persist timestamps/audit metadata if such fields already exist
   - If the user account is created on first sign-in, bind the accepted membership to the resolved `user_id`.

6. **Restrict access for pending memberships**
   - Update tenant resolution and authorization logic so only active memberships count for workspace access.
   - Verify:
     - pending memberships do not appear as selectable active companies at sign-in, unless the UX intentionally shows them as pending-only
     - tenant-scoped API access is denied for pending memberships
     - role-based authorization uses only active memberships
   - If there is a membership query used by auth middleware, ensure it filters to active status.

7. **Expose pending state where appropriate**
   - Update DTOs/responses so admin/company setup views can see membership status if already supported by the API.
   - If there is an invitation listing endpoint, include pending status.
   - Keep changes minimal and compatible with existing clients.

8. **Add tests**
   - Domain tests:
     - inviting creates pending membership
     - accepting pending membership transitions to active
     - invalid transitions are rejected
   - Application tests:
     - invite command persists pending membership with assigned role
     - accept command activates membership
     - duplicate active membership is rejected
   - Authorization/integration tests:
     - pending membership cannot access company-scoped endpoints
     - active membership can access after acceptance
   - Migration/persistence tests if the repo already includes them.

9. **Keep audit/outbox behavior aligned**
   - If invitation events or notifications already exist:
     - ensure they still fire from the invite flow
     - do not block this task on full delivery implementation
   - If business audit events are already modeled for membership changes, emit appropriate events for:
     - invitation created
     - membership accepted

10. **Document assumptions in code comments or PR notes**
   - Especially if the repo lacks explicit invitation entities or acceptance endpoints.
   - Prefer minimal, consistent implementation over speculative platform-wide redesign.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used, verify the new migration applies cleanly:
   - generate/apply migration per repo conventions
   - confirm `company_memberships.status` is populated correctly for existing rows

4. Manually verify the core flow using existing API/UI/test harness:
   - invite a user by email to a company
   - confirm a membership record exists with `Pending` status
   - confirm the invited user cannot access the company before acceptance
   - accept the invitation
   - confirm the membership becomes `Active` and access is granted afterward

5. Verify authorization regression scenarios:
   - owner/admin can still invite
   - pending membership is excluded from active tenant resolution
   - active membership role is available to authorization checks after acceptance

6. Verify duplicate/edge cases:
   - inviting an already active member
   - accepting the same invitation twice
   - inviting the same email again while pending, according to existing product conventions

# Risks and follow-ups
- **Unknown existing invitation design**: the repo may model invitations separately from memberships, or may not yet have acceptance endpoints. Adapt to the existing architecture rather than forcing a new pattern.
- **Status naming mismatch**: architecture mentions `pending membership until accepted`, but current code may use `Pending/Accepted` or `Pending/Active`. Choose one canonical runtime access state and apply it consistently.
- **Authorization regressions**: the highest risk is accidentally allowing pending memberships through tenant resolution or policy checks. Review all membership lookup paths carefully.
- **Identity binding edge cases**: if invites are email-based and users can sign in with different providers/casing, normalize email comparisons and ensure acceptance binds the correct `user_id`.
- **Duplicate invite semantics**: product notes mention re-invite/revoke flows, but they are not the focus here. If not already implemented, leave clear TODOs rather than overbuilding.
- **Migration safety**: if existing memberships lack status values, default legacy records to active access in migration to avoid locking out current users.
- **Frontend gaps**: if the web app currently assumes invited users are immediately active, a small UI follow-up may be needed to display pending state clearly.

Follow-up candidates after this task:
- re-invite flow
- revoke/cancel invitation flow
- invitation expiry
- invitation token/email delivery hardening via outbox
- admin membership management UI showing pending/active/revoked states