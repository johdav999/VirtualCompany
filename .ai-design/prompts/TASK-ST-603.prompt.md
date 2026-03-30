# Goal
Implement **TASK-ST-603 — Alerts, notifications, and approval inbox** in the existing .NET solution so that tenant-scoped users receive reliable in-app notifications for approvals, escalations, workflow failures, and briefing availability, and can review and act on pending approvals from a dedicated inbox.

This implementation should fit the documented architecture:
- modular monolith
- ASP.NET Core backend
- Blazor web frontend
- PostgreSQL primary store
- background workers + outbox-backed dispatch
- strict tenant isolation
- approval-centric human-in-the-loop workflows

Because no explicit acceptance criteria were provided beyond the story notes/backlog entry, derive behavior from **ST-603** and adjacent stories **ST-403**, **ST-404**, **ST-505**, **ST-601**, and **ST-604**.

# Scope
Implement the minimum cohesive vertical slice for ST-603 with production-appropriate structure.

Include:

1. **Notification domain model**
   - Add a dedicated notification model separate from chat/messages.
   - Support at least:
     - approval requested
     - escalation
     - workflow failure
     - briefing available
   - Support notification state:
     - unread
     - read
     - actioned
   - Include tenant scoping and recipient targeting.

2. **Persistence**
   - Add database schema/migrations for notifications.
   - Ensure all tenant-owned records include `company_id`.
   - Add indexes for inbox queries and priority sorting.

3. **Application layer**
   - Queries to fetch a user’s inbox with priority ordering.
   - Commands to:
     - mark notification read/unread
     - mark notification actioned where appropriate
   - Approval inbox query that returns pending approvals with concise context.
   - Approval action command integration if not already present; otherwise wire existing approval actions into inbox UX.

4. **Notification generation**
   - Generate notifications when relevant business events occur:
     - approval created
     - escalation created
     - workflow failure/blocked exception created
     - daily/weekly briefing generated
   - Prefer event/outbox-driven fan-out, not inline request-path fan-out.

5. **Background dispatch**
   - Implement or extend a background dispatcher that processes queued notification outbox events reliably and idempotently for in-app delivery.
   - Keep this in-app only for now; do not add email/push unless trivial hooks already exist.

6. **Web UI**
   - Add a dedicated inbox page in Blazor Web.
   - Show:
     - prioritized notifications
     - pending approvals
     - status badges
     - timestamps
     - concise rationale/context
   - Allow users to act on approvals from the inbox.
   - Allow read/unread state changes.

7. **Authorization and tenancy**
   - Enforce company scoping on all notification and approval inbox endpoints.
   - Only intended recipients or eligible approvers should see actionable items.
   - Respect existing membership/role authorization patterns.

8. **Audit/consistency hooks**
   - Where approval actions are taken from inbox, ensure linked approval state updates consistently.
   - If audit events already exist, emit/extend them for notification action and approval decision flows where appropriate.

Out of scope unless already nearly complete:
- mobile UI
- email delivery
- push notifications
- advanced notification preferences UI
- real-time websockets/signalr
- complex batching/digests beyond briefing availability
- arbitrary notification rule builder

# Files to touch
Inspect the solution first and then update the most relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - notification entity/value objects/enums
  - approval-related domain integration
  - domain events if used

- `src/VirtualCompany.Application/**`
  - inbox queries
  - notification commands
  - approval inbox DTOs/view models
  - event handlers / application services for notification creation
  - validation and authorization hooks

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - repositories/query services
  - migrations
  - outbox dispatcher / background worker integration
  - persistence for notification delivery state

- `src/VirtualCompany.Api/**`
  - endpoints/controllers for inbox queries and notification state changes
  - approval action endpoints if needed for inbox flow
  - DI registration

- `src/VirtualCompany.Web/**`
  - inbox page/components
  - approval action UI
  - notification list UI
  - navigation entry point

- `src/VirtualCompany.Shared/**`
  - shared contracts/DTOs if this solution uses shared request/response models

Also inspect:
- existing approval implementation from **ST-403**
- existing workflow failure/escalation surfaces from **ST-404**
- existing briefing generation from **ST-505**
- existing dashboard integration from **ST-601**
- tenancy/auth patterns from **ST-101**
- outbox/background processing patterns already present in the repo

Do not create parallel patterns if the repository already has established conventions.

# Implementation plan
1. **Discover existing architecture and conventions**
   - Build a quick map of:
     - domain entities
     - EF DbContext and configurations
     - CQRS/mediator patterns
     - approval model and endpoints
     - outbox/background worker implementation
     - tenant resolution and authorization
     - Blazor page/component structure
   - Reuse existing naming, foldering, and dependency patterns.

2. **Design the notification model**
   - Add a tenant-scoped `Notification` aggregate/entity with fields similar to:
     - `Id`
     - `CompanyId`
     - `RecipientUserId`
     - `Type`
     - `Category` or `Priority`
     - `Title`
     - `Body` / concise summary
     - `RelatedEntityType`
     - `RelatedEntityId`
     - `Status` (`Unread`, `Read`, `Actioned`)
     - `ActionUrl` or route metadata if useful
     - `MetadataJson`
     - `CreatedAt`
     - `ReadAt`
     - `ActionedAt`
   - Add enums/constants for notification type and status.
   - Keep the model UX-oriented and separate from `messages`.

3. **Add persistence**
   - Add EF configuration and migration for notifications.
   - Add indexes for:
     - `(company_id, recipient_user_id, status, created_at desc)`
     - `(company_id, recipient_user_id, type, created_at desc)`
     - priority sorting if stored explicitly
   - If outbox tables already exist, reuse them rather than inventing a second queue.

4. **Implement application contracts**
   - Add inbox query models returning:
     - notifications list
     - pending approvals list
     - counts for unread/pending if useful
   - Add commands:
     - `MarkNotificationRead`
     - `MarkNotificationUnread`
     - `MarkNotificationActioned`
   - Add or reuse approval decision command:
     - approve
     - reject with optional comment
   - Ensure field-level and authorization validation.

5. **Wire notification creation from business events**
   - On approval creation:
     - create notifications for target user(s) or role-based eligible approvers according to current approval model.
   - On escalation/workflow failure:
     - notify relevant users/roles based on existing escalation ownership rules.
   - On briefing generation:
     - notify intended recipients that a briefing is available.
   - Prefer:
     - domain event -> outbox record -> background dispatcher -> notification row creation/update
     - or existing equivalent pattern in the repo
   - Keep fan-out outside request path.

6. **Implement reliable in-app dispatcher**
   - Extend background worker to process notification-related outbox events.
   - Ensure:
     - idempotency
     - retry on transient failures
     - safe logging with tenant context
     - no duplicate notifications for the same event/recipient when retried
   - If needed, add a dedupe key such as `(event type, related entity, recipient)`.

7. **Implement inbox queries**
   - Create a dedicated query service or handler that returns:
     - prioritized approvals first
     - then escalations/workflow failures
     - then briefing notifications
   - Sorting guidance:
     - pending approvals and exceptions first
     - unread before read
     - newest first within priority bands
   - Keep all queries tenant-scoped and recipient-scoped.

8. **Implement API endpoints**
   - Add endpoints for:
     - get inbox
     - mark notification read/unread
     - act on approval from inbox
   - Reuse existing approval endpoints if already suitable.
   - Return safe 403/404 behavior for cross-tenant or unauthorized access.

9. **Build Blazor inbox UI**
   - Add a dedicated page, likely under a route such as `/inbox` or `/approvals`.
   - Include:
     - notification list with badges and timestamps
     - pending approvals section
     - concise rationale/threshold context
     - approve/reject actions
     - read/unread toggles
     - empty state
   - Add navigation entry from dashboard/shell.
   - Keep UI simple and server-driven first.

10. **Integrate with dashboard if low effort**
   - If dashboard shell already exists, add:
     - inbox badge/count
     - link to inbox
   - Do not over-expand scope.

11. **Add tests**
   - Add unit/integration tests for:
     - tenant isolation
     - notification creation on approval creation
     - inbox ordering
     - read/unread/actioned transitions
     - approval action from inbox updates approval state
     - dispatcher idempotency/retry behavior where practical

12. **Document assumptions**
   - If recipient resolution for role-based approvals is ambiguous, implement the simplest correct v1 behavior and note follow-up gaps.
   - If escalation ownership is not modeled yet, use a conservative fallback and document it.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. Apply migrations and verify schema compiles/runs according to repo conventions.

4. Validate notification generation flows end-to-end:
   - create an approval request
   - confirm notification is created for intended recipient(s)
   - confirm it appears in inbox
   - approve/reject from inbox
   - confirm approval state updates and notification becomes actioned/read as designed

5. Validate exception flows:
   - simulate or trigger workflow failure/escalation
   - confirm notification appears with higher priority than briefing items

6. Validate briefing flow:
   - trigger briefing generation path
   - confirm briefing availability notification appears

7. Validate state transitions:
   - unread -> read
   - read -> unread
   - pending approval acted on -> actioned

8. Validate tenant isolation:
   - user from company A cannot query or mutate notifications/approvals for company B
   - unauthorized access returns expected forbidden/not found behavior per existing conventions

9. Validate dispatcher reliability:
   - retry processing of the same outbox event does not create duplicate notifications
   - transient failure is retried and logged

10. Run final verification:
   - `dotnet test`
   - `dotnet build`

If UI tests are not present, include at least manual verification notes/screenshots in the PR summary if that is the repo norm.

# Risks and follow-ups
- **Recipient resolution ambiguity:** Role-targeted approvals may require fan-out to all eligible users or deferred resolution at query time. Prefer the simplest approach consistent with existing approval design and document tradeoffs.
- **Missing upstream event hooks:** If approvals, workflow failures, or briefings do not yet emit events, you may need to add lightweight domain/application events. Keep changes minimal and aligned with current patterns.
- **Duplicate delivery risk:** Outbox retries can create duplicate notifications unless dedupe is explicit. Add idempotency safeguards.
- **Authorization complexity:** Approval visibility may differ from notification visibility. Ensure actionable approvals are only shown to eligible approvers.
- **UI scope creep:** Keep inbox focused on approvals and exception alerts; do not build a full notification center with preferences.
- **Audit consistency:** If audit events are only partially implemented, avoid blocking delivery but leave clear follow-up notes.
- **Mobile follow-up:** ST-604 should reuse these APIs/contracts; keep contracts mobile-friendly.
- **Future enhancements to note, not implement now:**
  - notification preferences per user/company
  - email/push channels
  - real-time updates
  - bulk actions
  - richer filtering/search
  - SLA/escalation timers and reminder notifications