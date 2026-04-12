# Goal
Implement backlog task **TASK-12.3.2** for **ST-603 — Alerts, notifications, and approval inbox** so that **users can view and act on pending approvals from a dedicated inbox** in the web application, backed by tenant-scoped ASP.NET Core APIs and persisted approval/notification state.

This task should deliver the smallest coherent vertical slice that enables:
- a dedicated inbox view for pending approvals
- listing approval items relevant to the current company/user context
- opening an approval item to inspect concise decision context
- approving or rejecting from the inbox
- updating linked approval state reliably and audibly/auditably
- reflecting actioned state in the inbox without page inconsistencies

No explicit acceptance criteria were provided for the task itself, so align implementation to the parent story and existing architecture/backlog conventions.

# Scope
In scope:
- Add or complete a **dedicated approval inbox** in the **Blazor Web App**
- Add or complete **application query/command handlers** for:
  - fetching pending approvals for inbox display
  - fetching approval detail for review
  - approving an approval request
  - rejecting an approval request with optional/required comment based on existing conventions
- Ensure all approval inbox operations are:
  - **tenant-scoped**
  - **authorization-aware**
  - limited to approvals in actionable states, primarily `pending`
- Show concise approval context in the inbox/detail UI, such as:
  - approval type
  - entity type/entity id or friendly reference if available
  - requested by actor
  - created date
  - threshold context summary
  - required role/user
  - current status
- On approve/reject:
  - persist approval decision
  - set decision metadata (`decided_at`, decision summary/comment where applicable)
  - update any linked entity state if that behavior already exists in the approval module patterns
  - create/emit audit/business events if the codebase already has the mechanism
  - mark related inbox/notification item as actioned/read if such model already exists
- Add tests for core command/query behavior and any API endpoints/components introduced

Out of scope unless already trivial and clearly supported by existing patterns:
- Full alerts/notifications center beyond approvals
- Mobile companion changes
- Push/email delivery
- Multi-step approval workflow redesign
- New notification infrastructure if none exists
- Broad dashboard work outside linking to the inbox
- Reworking approval policy generation logic

If the repo already contains partial implementations for approvals, notifications, inbox, or audit events, extend them rather than introducing parallel patterns.

# Files to touch
Inspect the solution first and then touch only the files needed. Likely areas:

- `src/VirtualCompany.Domain/**`
  - approval entities/value objects/enums
  - notification/inbox entities if present
- `src/VirtualCompany.Application/**`
  - approval inbox queries
  - approval decision commands
  - DTO/view models
  - validators
  - authorization/policy checks
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repositories/query services
  - migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - approval inbox endpoints/controllers/minimal APIs
- `src/VirtualCompany.Web/**`
  - dedicated inbox page/component
  - approval detail/action UI
  - service clients/view models
- `src/VirtualCompany.Shared/**`
  - shared contracts if the solution uses shared request/response models
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- potentially application/domain test projects if present elsewhere in the repo

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, identify the actual existing locations for:
- approval aggregate/model
- approval steps model
- tenant resolution
- current user context
- authorization conventions
- CQRS handler patterns
- Blazor page/layout/navigation patterns
- audit event creation
- notification/inbox model, if any

# Implementation plan
1. **Recon the existing implementation**
   - Search for:
     - `Approval`, `Approvals`, `ApprovalStep`
     - `Notification`, `Inbox`
     - `AuditEvent`
     - `CompanyId`, tenant filters, current user abstractions
     - existing command/query handler patterns
   - Determine:
     - whether approvals already have approve/reject commands
     - whether notifications already exist as a separate model
     - whether web UI already has an approvals list or dashboard widget to reuse
   - Prefer extending existing modules over creating new abstractions.

2. **Define the approval inbox application contract**
   - Add a query for listing inbox approvals, e.g.:
     - pending approvals for current company
     - optionally filtered to approvals actionable by current user/role
     - sorted with newest/highest priority first
   - Add a query for approval detail.
   - Add commands for:
     - approve approval
     - reject approval
   - Return concise DTOs tailored for inbox UX, not raw entities.

3. **Enforce tenant and approver scoping**
   - Every query/command must resolve the current company context and reject cross-tenant access.
   - Ensure only valid approvers can act:
     - specific required user
     - required role
     - approval step assignee if step-based logic exists
   - If current authorization patterns are incomplete, implement the minimum consistent checks in the application layer.

4. **Implement decision behavior**
   - For approve:
     - only allow when approval status is `pending`
     - update approval status and timestamps
     - update current step if ordered steps exist
     - if final approval is reached, transition linked entity if existing domain behavior supports it
   - For reject:
     - only allow when approval status is `pending`
     - persist rejection comment/summary
     - prevent downstream execution
     - transition linked entity appropriately if existing behavior exists
   - Preserve idempotency/safe failure behavior:
     - actioning an already-decided approval should return a safe validation/business error

5. **Handle notification/inbox state**
   - If a notification/inbox table/model already exists:
     - associate approval items to it
     - mark related item as read/actioned after decision
   - If no notification model exists yet, do **not** invent a large notification subsystem.
     - Implement the dedicated inbox directly from approvals as the source of truth.
     - Optionally include lightweight UI state only if already supported.
   - Keep the implementation aligned with ST-603’s approval inbox requirement first.

6. **Build the web inbox UI**
   - Add a dedicated page in `VirtualCompany.Web`, likely under an inbox/approvals route.
   - Display:
     - pending approvals list
     - status badges
     - requested date
     - approval type/entity
     - requester
     - concise summary/context
   - Add detail panel/page/modal with:
     - threshold context summary
     - required approver info
     - linked entity reference
     - approve/reject actions
   - On action:
     - call backend command
     - show success/error feedback
     - refresh list/detail state
   - Keep UX simple and SSR-first, with minimal interactivity as needed.

7. **Navigation and discoverability**
   - Add a nav entry or dashboard link to the dedicated inbox if appropriate.
   - If a dashboard widget already exists for pending approvals, link it to the new inbox route.

8. **Persistence/schema changes only if necessary**
   - Only add migration/schema changes if required for:
     - decision summary/comment persistence
     - notification actioned/read state if a notification model already exists but is incomplete
   - Keep schema changes minimal and consistent with the architecture:
     - PostgreSQL
     - shared-schema multi-tenancy
     - business-state persistence in domain tables

9. **Auditability**
   - If audit event creation patterns exist, emit business audit events for:
     - approval viewed if already tracked in business terms
     - approval approved
     - approval rejected
   - Include actor, target, outcome, and concise rationale/decision summary.
   - Do not add technical logging as a substitute for business audit.

10. **Testing**
   - Add tests for:
     - tenant isolation on list/detail/action
     - only pending approvals are actionable
     - unauthorized users cannot act
     - approve updates status/decision metadata
     - reject updates status/decision metadata
     - inbox query returns expected ordering/filtering
   - Add API tests for endpoints if the project uses API-level integration tests.
   - Add component/UI tests only if the repo already has that pattern.

11. **Keep implementation small and coherent**
   - Do not attempt to complete all of ST-603.
   - Focus this task on the dedicated approval inbox vertical slice.
   - Leave broader alerts/notification fan-out for follow-up if not already present.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Manually validate the web flow:
   - sign in as a user with access to a company containing pending approvals
   - navigate to the dedicated inbox page
   - verify only tenant-scoped approvals appear
   - open an approval and inspect detail/context
   - approve one item
   - verify:
     - status changes from pending
     - item no longer appears in pending list or is marked actioned per UX
     - linked entity state updates if supported
   - reject one item
   - verify rejection comment/summary persists if implemented
   - verify unauthorized user cannot act on approvals outside role/user scope

5. If migrations were added:
   - apply migration using the repo’s established process
   - verify schema matches EF/domain expectations

6. Confirm no cross-tenant leakage by test or manual seeded data checks.

# Risks and follow-ups
- **Risk: notification model may not exist yet**
  - Mitigation: implement the inbox directly over approvals rather than blocking on a full notification subsystem.

- **Risk: approval action side effects may already be partially implemented elsewhere**
  - Mitigation: reuse existing domain/application flows; avoid duplicating approval decision logic in UI/API layers.

- **Risk: approver authorization rules may be underspecified**
  - Mitigation: implement conservative checks based on `required_user_id`, `required_role`, and current step ownership; default deny on ambiguity.

- **Risk: multi-step approvals may complicate “approve” semantics**
  - Mitigation: support the existing step model if present, but keep UI focused on the current actionable step and final status.

- **Risk: no explicit acceptance criteria for this task**
  - Mitigation: optimize for a minimal, production-sensible slice aligned to ST-603 and architecture principles: tenant isolation, auditability, CQRS-lite, and conservative authorization.

Follow-ups after this task:
- unify approvals with broader alerts/notifications feed
- add unread/read/actioned notification state if not yet modeled
- add dashboard widget deep-linking and counts
- add mobile companion support for the same inbox APIs
- add prioritization/sorting for escalations and workflow failures alongside approvals