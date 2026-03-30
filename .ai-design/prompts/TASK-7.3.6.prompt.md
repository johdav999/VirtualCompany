# Goal
Implement backlog task **TASK-7.3.6 — Support re-invite and revoke flows** for **ST-103 Human user invitation and role assignment** in the existing .NET solution.

The coding agent should extend the current invitation/membership flow so that owner/admin users can:
- **re-invite** a pending invited user by email to the same company
- **revoke** an outstanding invitation before it is accepted

The implementation must fit the existing architecture:
- multi-tenant, company-scoped behavior
- ASP.NET Core backend with clean module boundaries
- PostgreSQL-backed persistence
- outbox/background-dispatch friendly notification delivery
- role-based authorization for owner/admin only
- no coupling between human roles and agent permissions

Because no explicit acceptance criteria were provided for this task, derive behavior from:
- ST-103 acceptance criteria
- story notes explicitly calling out re-invite and revoke flows
- architecture guidance around tenant isolation, auditability, and outbox-backed side effects

# Scope
Include:
- domain/application/API support for **re-invite** and **revoke invitation**
- persistence updates needed to represent invitation lifecycle cleanly
- authorization checks ensuring only allowed company members can perform these actions
- tenant scoping on all reads/writes
- safe handling of edge cases:
  - invitation already accepted
  - invitation already revoked
  - invitation expired, if expiration already exists in codebase
  - duplicate pending invitations for same company/email
- audit/outbox integration if the existing invitation flow already uses those patterns
- tests covering command/service behavior and API outcomes

Do not include:
- redesign of the full identity system
- SSO or auth provider changes
- email template redesign beyond what is minimally required
- broad UI overhaul unrelated to re-invite/revoke
- mobile-specific work unless invitation management is already exposed there
- unrelated role/permission model refactors

Assume the preferred product behavior is:
- **Re-invite** is allowed only for invitations that are still outstanding and not accepted/revoked.
- Re-invite should refresh delivery metadata and resend the invitation.
- **Revoke** marks the invitation unusable and prevents later acceptance.
- If the system currently models invitations as pending memberships rather than a dedicated invitation entity, preserve the existing design unless a small, localized refactor is clearly necessary.

# Files to touch
Inspect the solution first, then update the relevant files you find. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - membership/invitation entities, enums, value objects, domain rules
- `src/VirtualCompany.Application/**`
  - commands, handlers, DTOs, validators, authorization policies, query models
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations, repositories, migrations, outbox integration, email dispatch hooks
- `src/VirtualCompany.Api/**`
  - invitation endpoints/controllers/minimal APIs, request/response contracts
- `src/VirtualCompany.Web/**`
  - invitation management UI if already present in web app
- tests under existing test projects
  - domain tests
  - application handler tests
  - API/integration tests

Also review:
- `README.md`
- existing membership/company setup modules
- any invitation-related migration/configuration files
- any outbox/email dispatcher implementation already used for initial invites

# Implementation plan
1. **Discover the current invitation model**
   - Find how ST-103 is currently implemented.
   - Determine whether invitations are represented as:
     - a dedicated `Invitation` entity/table, or
     - `company_memberships` rows with a `pending` status, or
     - another pattern.
   - Identify current statuses, timestamps, tokens, and acceptance flow.
   - Identify current authorization approach for owner/admin actions.

2. **Define the invitation lifecycle**
   - Normalize statuses/state transitions around the existing model.
   - Ensure the model can represent at least:
     - pending/outstanding
     - accepted
     - revoked
     - expired, if already supported
   - Add fields only if needed, such as:
     - `revoked_at`
     - `revoked_by_user_id`
     - `last_invited_at` / `resent_at`
     - `invite_token` refresh metadata if applicable
   - Prefer minimal schema changes.

3. **Implement re-invite behavior**
   - Add an application command/handler such as `ReinviteCompanyMemberCommand` or equivalent.
   - Enforce:
     - caller belongs to the same company
     - caller has owner/admin privileges
     - target invitation belongs to the same company
     - target invitation is still re-invitable
   - Behavior should:
     - refresh invitation send metadata
     - optionally rotate acceptance token if the current design benefits from it
     - queue/send invitation delivery through existing outbox/notification mechanism
     - return a clear result for API/UI consumption
   - If duplicate pending invites by email are possible today, consolidate behavior so re-invite targets the existing outstanding invite rather than creating a second one.

4. **Implement revoke behavior**
   - Add an application command/handler such as `RevokeCompanyInvitationCommand`.
   - Enforce:
     - same tenant/company scope
     - owner/admin authorization
     - only outstanding invitations can be revoked
   - Behavior should:
     - mark invitation as revoked
     - make acceptance impossible afterward
     - preserve auditability
   - Revoke should be idempotent where practical:
     - repeated revoke on already revoked invite should return a safe conflict/no-op response according to existing API conventions.

5. **Protect the acceptance flow**
   - Update invitation acceptance logic so revoked invitations cannot be accepted.
   - Also ensure expired/invalidated tokens are rejected safely.
   - If re-invite rotates tokens, old tokens must no longer work.

6. **Update persistence and EF configuration**
   - Add/adjust entity configuration and migration if schema changes are required.
   - Preserve tenant-scoped indexes and uniqueness where appropriate.
   - Consider a uniqueness rule like one outstanding invitation per `company_id + email` if consistent with current design.
   - Keep changes backward-compatible and minimal.

7. **Expose API endpoints**
   - Add endpoints for:
     - re-invite
     - revoke
   - Follow existing API style and route conventions, likely under company membership/invitation management.
   - Use proper response codes based on existing conventions, e.g.:
     - `200 OK` or `204 No Content` on success
     - `403 Forbidden` for unauthorized company members
     - `404 Not Found` for cross-tenant or missing records
     - `409 Conflict` for invalid state transitions
   - Do not leak cross-tenant existence.

8. **Update web UI if invitation management already exists**
   - If the web app already lists pending invites/memberships, add:
     - re-invite action
     - revoke action
     - disabled states/tooltips for non-actionable invites
   - Keep UI changes minimal and aligned with current patterns.
   - Do not build a new management surface if none exists and the task can be completed backend-only.

9. **Audit and outbox integration**
   - If the project already records business audit events, add events for:
     - invitation re-sent
     - invitation revoked
   - If initial invite delivery uses outbox/background dispatch, reuse the same mechanism for re-invite.
   - Avoid synchronous external email sending in request path if that is not the current pattern.

10. **Add tests**
   - Add/extend tests for:
     - owner/admin can re-invite pending invite
     - non-owner/admin cannot re-invite
     - cannot re-invite accepted invite
     - cannot re-invite revoked invite
     - owner/admin can revoke pending invite
     - cannot revoke accepted invite
     - revoked invite cannot be accepted
     - re-invite resends/queues delivery
     - tenant isolation on re-invite/revoke
     - duplicate pending invite handling
   - Prefer tests at the application layer plus API/integration coverage for status codes and tenant scoping.

11. **Keep implementation aligned with story intent**
   - Human roles remain assignable independently of agent permissions.
   - Role changes and invitation actions should remain company-scoped.
   - Preserve clean architecture boundaries:
     - API thin
     - application owns use cases
     - domain owns state rules
     - infrastructure owns persistence/delivery

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. If migrations were added, verify:
   - migration compiles
   - database update path is valid
   - EF model snapshot is consistent

5. Manually validate the main flows via API or existing UI:
   - create invite as owner/admin
   - re-invite pending invite
   - confirm resend metadata/outbox message created
   - revoke pending invite
   - verify revoked invite cannot be accepted
   - verify accepted invite cannot be re-invited/revoked
   - verify cross-company access returns not found/forbidden per existing conventions

6. Confirm no duplicate outstanding invitations remain possible for the same company/email unless explicitly intended by current design.

7. Confirm any audit/outbox records are created consistently with existing patterns.

# Risks and follow-ups
- **Unknown current model:** The codebase may not yet distinguish invitation records from pending memberships. Avoid over-engineering; implement the smallest coherent lifecycle extension.
- **Token semantics:** If acceptance tokens exist, re-invite may need token rotation. Be careful not to leave old tokens valid.
- **Duplicate invite cleanup:** Existing data may already contain multiple pending invites for the same email/company. Handle gracefully in code and consider a follow-up data cleanup if needed.
- **API convention mismatch:** Follow existing error/response patterns rather than inventing new ones.
- **UI surface uncertainty:** If no invitation management UI exists, backend/API completion may be sufficient; note any missing UI as a follow-up.
- **Audit gaps:** If audit infrastructure is incomplete for this module, add minimal hooks now and note richer audit views as follow-up.
- **Expiration behavior ambiguity:** If expiration is not implemented, do not invent a large expiry subsystem; just ensure re-invite/revoke work with current lifecycle.
- **Follow-up recommendation:** Add explicit acceptance criteria to the story/task after implementation, documenting final lifecycle rules for invite, re-invite, revoke, accept, and duplicate handling.