# Goal
Implement `TASK-12.3.1` for `ST-603 — Alerts, notifications, and approval inbox` by adding backend support for system-generated in-app notifications covering:
- approval requests
- escalations
- workflow failures
- briefing availability

The implementation should fit the existing modular monolith architecture, use tenant-scoped persistence in PostgreSQL, and rely on reliable asynchronous delivery via the existing or newly added outbox/background dispatcher pattern rather than request-path fan-out.

Deliver the minimum cohesive slice needed so the system can:
- create notification records from domain/application events
- persist notification state with unread/read/actioned tracking
- expose notification and approval inbox query endpoints/services for web/mobile consumption
- prioritize approvals and exception alerts in sorting
- support reliable in-app dispatch processing

No email/push delivery is required unless already scaffolded; focus on in-app notifications and approval inbox behavior.

# Scope
Include:
- Notification domain model and persistence
- Tenant-scoped notification repository/query support
- Application commands/handlers for:
  - marking notifications read/unread
  - marking notifications actioned where appropriate
- Application/event handlers or background processing that generate notifications for:
  - new approval requests
  - escalations
  - workflow failures
  - daily/weekly briefing availability
- Approval inbox query that returns pending approvals with related notification metadata if useful
- In-app notification listing query with sorting that prioritizes approvals and exception alerts
- Reliable dispatcher/background worker integration using outbox or equivalent internal event processing
- API endpoints needed by web/mobile clients
- Tests for core generation, tenant isolation, state transitions, and sorting/prioritization

Do not include:
- Full web UI redesign unless a minimal endpoint contract requires a DTO adjustment
- Mobile UI work
- Email, SMS, or native push notifications
- Broad event bus refactors beyond what is necessary for this task
- Nonessential notification preferences unless already present and trivial to wire for briefing availability

Assume no explicit acceptance criteria beyond the story/backlog text; derive behavior from the story, notes, and architecture.

# Files to touch
Inspect the solution structure first and then update the appropriate projects. Expected areas:

- `src/VirtualCompany.Domain`
  - add notification entity, enums/value objects, domain events if applicable
- `src/VirtualCompany.Application`
  - commands, queries, DTOs, handlers, interfaces
  - event handlers for notification generation
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration
  - repositories
  - outbox/dispatcher/background processing integration
  - migrations or migration scaffolding location used by this repo
- `src/VirtualCompany.Api`
  - notification and approval inbox endpoints/controllers/minimal APIs
- `src/VirtualCompany.Web`
  - only if existing contracts/pages need small adjustments for inbox consumption
- `src/VirtualCompany.Mobile`
  - only if shared DTO contracts require compile fixes
- `src/VirtualCompany.Shared`
  - shared contracts if this repo centralizes API DTOs there
- `tests/VirtualCompany.Api.Tests`
  - integration/API tests
- possibly additional test projects if application/infrastructure tests already exist

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, discover the actual patterns already used for:
- entities and aggregate roots
- MediatR/CQRS conventions
- EF Core configurations and migrations
- outbox/background workers
- approval/workflow/task models
- tenant resolution and authorization
- API style

Follow existing conventions over inventing new ones.

# Implementation plan
1. **Recon and align with existing architecture**
   - Build a quick map of current implementations for:
     - approvals
     - workflows
     - briefings/summaries
     - outbox/background jobs
     - tenant-scoped repositories
     - API endpoint style
   - Identify whether notifications already exist partially. If so, extend rather than duplicate.
   - Identify how approval creation, workflow failure, and escalation are currently represented:
     - domain events
     - application events
     - status transitions
     - audit events
     - outbox messages

2. **Design the notification model**
   - Add a notification model separate from messages, per backlog note.
   - Recommended fields:
     - `Id`
     - `CompanyId`
     - `UserId` or recipient targeting model already used in repo
     - `Type` or category (`approval_request`, `escalation`, `workflow_failure`, `briefing_available`)
     - `Priority` (`high`, `normal`, etc.)
     - `Title`
     - `Body`
     - `Status` (`unread`, `read`, `actioned`)
     - `RelatedEntityType`
     - `RelatedEntityId`
     - `ActionUrl` or route token if the codebase uses navigable links
     - `MetadataJson`
     - `CreatedAt`
     - `ReadAt`
     - `ActionedAt`
   - Ensure tenant ownership via `CompanyId`.
   - If the system supports role-targeted notifications instead of direct user fan-out, align with existing membership/recipient patterns. Otherwise, fan out to concrete users in the company based on approval assignee/role rules.

3. **Persist notifications**
   - Add EF Core mapping and database migration.
   - Add indexes for:
     - `(company_id, user_id, status, created_at desc)`
     - `(company_id, user_id, type, created_at desc)`
     - related entity lookup if needed
   - Keep schema consistent with shared-schema multi-tenancy.

4. **Define notification generation triggers**
   - Wire notification creation from existing lifecycle points:
     - when an approval is created and enters pending state
     - when an escalation is created or a task/workflow enters escalated/blocked-exception state
     - when a workflow execution fails in a visible way
     - when a daily/weekly briefing is generated and becomes available
   - Prefer reacting to existing domain/application events or outbox events.
   - If no event exists, introduce a minimal internal event at the application boundary rather than tightly coupling notification creation into controllers.

5. **Implement recipient resolution**
   - For approvals:
     - notify required user directly if specified
     - notify users in required role if role-based
     - for ordered chains, notify only the current actionable approver(s), not future steps
   - For escalations/workflow failures:
     - notify relevant operators/managers based on existing ownership/escalation rules
     - if no explicit routing exists, use a conservative fallback such as admins/owners and document it
   - For briefing availability:
     - notify users eligible for briefings according to existing preferences/config if present
     - otherwise notify active company members or a sensible subset already used by dashboard/briefing features
   - Keep recipient resolution tenant-scoped and test it carefully.

6. **Implement reliable asynchronous dispatch**
   - Keep notification fan-out out of request path.
   - Use existing outbox/background dispatcher pattern if present.
   - Flow should be:
     - business event occurs
     - outbox/internal event persisted
     - background dispatcher processes event idempotently
     - notification records created
   - Add idempotency protection to avoid duplicate notifications on retries.
   - If the repo lacks a generalized outbox consumer for this path, add the smallest conforming implementation.

7. **Add application commands and queries**
   - Queries:
     - list notifications for current user/company with filters and pagination
     - list pending approvals for inbox view
   - Commands:
     - mark notification as read
     - mark notification as unread if supported by UX conventions
     - mark notification as actioned
     - optionally mark all visible notifications as read if trivial and already patterned
   - Sorting:
     - approvals first
     - escalations/workflow failures next
     - briefing availability after exceptions
     - then newest first within priority bands
   - Ensure actioned status is set when linked approval is decided, or expose an explicit command if that is how the system models it.

8. **Expose API endpoints**
   - Add tenant-aware authenticated endpoints for:
     - `GET /api/notifications`
     - `PATCH /api/notifications/{id}/read`
     - `PATCH /api/notifications/{id}/unread` if implemented
     - `PATCH /api/notifications/{id}/actioned` if needed
     - `GET /api/approvals/inbox` or equivalent existing approval route extension
   - Reuse existing authorization and company context resolution.
   - Return safe DTOs suitable for web/mobile.

9. **Integrate approval state changes**
   - When an approval is approved/rejected/expired/cancelled:
     - update linked notification status to `actioned` where appropriate
     - ensure stale pending approval notifications no longer appear as actionable
   - Keep this synchronized through existing approval handlers/events rather than ad hoc API logic.

10. **Testing**
   - Add tests for:
     - notification creation on approval creation
     - notification creation on workflow failure
     - notification creation on escalation
     - notification creation on briefing availability
     - tenant isolation on queries and state changes
     - recipient resolution for required user vs required role
     - sorting priority
     - idempotent dispatch on retry
     - approval decision updates notification to actioned
   - Prefer integration tests around API + persistence where feasible.

11. **Documentation and cleanup**
   - Add concise comments only where logic is non-obvious.
   - If migrations are added, follow repo guidance from `docs/postgresql-migrations-archive/README.md`.
   - Update any relevant README or developer notes only if necessary.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the repo workflow:
   - generate/apply the migration using the project’s established process
   - verify the notifications table/schema is created correctly

4. Manually verify via API or integration tests:
   - create or simulate a pending approval and confirm a notification is generated for the correct recipient(s)
   - simulate a workflow failure and confirm a high-priority notification appears
   - simulate an escalation and confirm notification generation
   - simulate briefing generation and confirm briefing availability notification
   - fetch notifications and verify sort order:
     - approvals first
     - escalations/workflow failures next
     - briefings later
   - mark a notification read and verify persisted state
   - complete/reject an approval and verify linked notification becomes actioned
   - verify a user from another company cannot query or mutate notifications outside their tenant

5. Regression check:
   - ensure approval/workflow creation paths still work
   - ensure background dispatcher retries do not create duplicate notifications
   - ensure API contracts compile for web/mobile consumers

# Risks and follow-ups
- **Recipient ambiguity:** escalation and briefing recipients may not be fully defined in current domain rules. Use the most conservative existing routing model and document assumptions in code/comments or PR notes.
- **Duplicate delivery risk:** retries can create duplicate notifications if idempotency is not enforced. Add a deterministic dedupe key where possible, e.g. event type + related entity + recipient + current actionable step.
- **Approval chain complexity:** multi-step approvals require notifying only current approvers. Be careful not to notify future approvers prematurely.
- **State synchronization:** notification `actioned` status can drift if approval decisions bypass the main application flow. Ensure all approval state transitions pass through a single handler/event path.
- **Existing partial implementations:** there may already be message/inbox concepts that overlap. Avoid creating parallel concepts unless clearly required; extend existing abstractions if they already model notifications cleanly.
- **UI dependency gaps:** if the frontend expects a different inbox shape, keep API DTOs backward-compatible where possible.
- **Follow-up candidates after this task:**
  - notification preferences per user/channel
  - batch mark-as-read
  - real-time delivery via SignalR/web sockets
  - email/mobile push channels
  - richer inbox filtering and grouping
  - audit linkage from notification detail to approval/task/workflow views