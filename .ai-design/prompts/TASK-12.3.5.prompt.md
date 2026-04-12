# Goal
Implement backlog task **TASK-12.3.5 — Model notifications separately from messages if needed for UX** for story **ST-603 Alerts, notifications, and approval inbox**.

The coding agent should introduce a dedicated notification model and supporting application flow so alerts/approval inbox UX is not forced through the existing conversation/message model. The implementation should align with the modular monolith, CQRS-lite, PostgreSQL, outbox-backed delivery, and tenant-scoped architecture.

Because no explicit acceptance criteria were provided for this task, derive completion from the story and architecture:
- notifications exist as a first-class domain concept separate from chat/messages
- approval/alert inbox use cases can query notifications directly
- notification state supports unread/read/actioned
- notification creation is compatible with background dispatch and outbox patterns
- tenant isolation is enforced throughout

# Scope
In scope:
- Add a dedicated **Notification** domain model separate from `messages`
- Add persistence/mapping for notifications in PostgreSQL
- Add application-layer commands/queries for:
  - creating notifications
  - listing inbox notifications
  - marking notifications read/unread
  - marking notifications actioned where appropriate
- Add API endpoints for notification inbox operations
- Support notification types at minimum for:
  - approval requests
  - escalations
  - workflow failures
  - briefing availability
- Ensure notifications can reference related entities such as approvals, tasks, workflows, or conversations
- Ensure sorting/prioritization favors approvals and exception alerts
- Keep implementation compatible with background dispatcher/outbox usage

Out of scope unless already trivial in the codebase:
- Full web/mobile UI implementation beyond minimal contract support
- Push notifications, email, SMS, or external delivery channels
- Rich notification preference management
- Reworking existing chat/message flows unless needed to remove misuse of messages for inbox alerts
- Large-scale event bus introduction beyond current outbox/background worker approach

# Files to touch
Inspect the solution first and adapt to actual conventions. Likely areas:

- `src/VirtualCompany.Domain/`
  - add notification aggregate/entity, enums/value objects, domain rules
- `src/VirtualCompany.Application/`
  - commands, queries, DTOs, validators, handlers for notifications
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration
  - repository/query implementations
  - migration for notifications table/indexes
  - outbox integration hooks if present
- `src/VirtualCompany.Api/`
  - notification inbox endpoints/controllers
  - request/response contracts if API owns them
- `src/VirtualCompany.Shared/`
  - shared DTOs/contracts if used across Web/Mobile
- `src/VirtualCompany.Web/`
  - only if lightweight inbox integration already exists and needs contract updates
- `src/VirtualCompany.Mobile/`
  - only if shared contracts require compile fixes
- `tests/VirtualCompany.Api.Tests/`
  - endpoint/integration tests
- other test projects if present for application/domain/infrastructure

Also check:
- existing approval, workflow, communication, and outbox code paths
- existing migrations approach referenced by `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover current architecture and conventions**
   - Inspect how entities, handlers, repositories, endpoints, and migrations are currently implemented.
   - Determine whether EF Core, Dapper, MediatR, minimal APIs, or controllers are in use.
   - Identify whether `messages` are currently being used for alerts/inbox behavior.
   - Identify existing approval and workflow failure event points where notifications should originate.

2. **Design the notification model**
   - Create a dedicated notification entity/table, separate from `messages`.
   - Recommended fields, adapted to project conventions:
     - `id`
     - `company_id`
     - `user_id` or recipient actor reference
     - `type` (`approval_request`, `escalation`, `workflow_failure`, `briefing_available`, etc.)
     - `priority` (`high`, `normal`, `low`) with approvals/exceptions sortable first
     - `title`
     - `body`
     - `status` or state fields supporting `unread`, `read`, `actioned`
     - `action_url` or route hint if the codebase uses navigational metadata
     - `related_entity_type`
     - `related_entity_id`
     - `payload_json` for compact structured metadata
     - `created_at`
     - `read_at`
     - `actioned_at`
   - Keep the model tenant-owned and user-targeted.
   - Do not overload conversation/message semantics.

3. **Add domain rules**
   - Enforce valid state transitions:
     - unread -> read
     - read -> unread if supported
     - unread/read -> actioned
   - Prevent cross-tenant access.
   - Keep notification behavior simple and explicit; avoid embedding chat logic.

4. **Add persistence**
   - Add EF/entity mapping and a PostgreSQL migration.
   - Add indexes for likely inbox queries:
     - `(company_id, user_id, created_at desc)`
     - `(company_id, user_id, status, priority, created_at desc)`
     - maybe `(company_id, related_entity_type, related_entity_id)` if useful
   - Use JSONB for payload metadata if consistent with architecture.

5. **Add application-layer use cases**
   - Implement commands/queries such as:
     - `CreateNotification`
     - `ListNotificationsForInbox`
     - `MarkNotificationRead`
     - `MarkNotificationUnread` if desired by UX
     - `MarkNotificationActioned`
   - Return DTOs suitable for web/mobile inbox views.
   - Include filters for:
     - unread only
     - type
     - priority
     - pagination
   - Default sort should prioritize approvals and exception alerts, then newest first.

6. **Integrate notification creation into business flows**
   - Find existing approval creation flow and create notifications for targeted approvers.
   - Find escalation/workflow failure/briefing generation flows and create notifications there.
   - Prefer outbox-backed creation/dispatch if the codebase already uses domain events or integration events.
   - Keep fan-out out of request path where possible, per story notes.

7. **Expose API endpoints**
   - Add tenant-scoped endpoints for inbox operations, following existing API style.
   - Suggested endpoints, adapted to conventions:
     - `GET /api/notifications`
     - `GET /api/notifications/unread-count`
     - `POST /api/notifications/{id}/read`
     - `POST /api/notifications/{id}/unread`
     - `POST /api/notifications/{id}/actioned`
   - Enforce that users can only access their own company-scoped notifications.

8. **Preserve separation from messages**
   - If current code stores alerts as `messages.message_type = alert|approval_request`, do not break existing chat behavior unnecessarily.
   - Refactor inbox/approval alert reads to use notifications instead of messages where feasible.
   - Leave messages for conversation history; use notifications for inbox attention/state/action UX.

9. **Add tests**
   - Domain/application tests for state transitions and validation.
   - API/integration tests for:
     - tenant scoping
     - recipient scoping
     - inbox listing
     - read/actioned transitions
     - approval notification creation if practical
   - Add regression coverage ensuring messages and notifications remain distinct concepts.

10. **Document assumptions in code comments or brief docs**
   - Note why notifications are separate from messages:
     - different UX lifecycle
     - per-user state
     - inbox prioritization
     - actionability
   - Keep docs concise.

# Validation steps
Run and verify using the repository’s actual setup:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow:
   - generate/apply the migration per project conventions
   - verify the notifications table exists with expected indexes and constraints

4. Validate behavior manually or via tests:
   - create an approval request and confirm a notification is created for the approver
   - list notifications for a user and confirm:
     - tenant scoping works
     - approvals/escalations sort ahead of lower-priority items
     - unread/read/actioned states behave correctly
   - confirm conversation messages still work independently
   - confirm a user cannot read or mutate another user’s notifications, even in the same tenant unless explicitly intended by design

5. If API can be exercised locally:
   - call inbox endpoints and verify response contracts are stable and compile for shared consumers

# Risks and follow-ups
- **Schema mismatch risk:** the current codebase may already have partial inbox or alert models. Reuse/extend carefully instead of duplicating concepts.
- **Recipient model ambiguity:** architecture implies user-facing inbox behavior, but exact recipient shape may vary. Prefer human-user-targeted notifications first unless the codebase already supports broader actor recipients.
- **Event integration risk:** approval/workflow flows may not yet emit clean domain events. If so, add minimal direct application integration now and leave deeper event refactoring for later.
- **Migration strategy risk:** follow the repository’s established migration/archive process exactly.
- **UI dependency risk:** web/mobile may assume alerts are messages. Update shared contracts carefully to avoid compile/runtime regressions.
- **Future follow-ups:**
  - notification preferences and channel settings
  - batch mark-read/actioned operations
  - unread badge counts/caching
  - push/mobile delivery
  - richer notification grouping/deduplication
  - background dispatcher hardening and outbox fan-out for all notification sources