# Goal
Implement backlog task **TASK-12.3.3** for **ST-603 Alerts, notifications, and approval inbox** by adding notification state support for **unread/read** and **actioned** statuses across the backend domain, persistence, application layer, and any existing inbox/query surfaces.

The coding agent should deliver a cohesive implementation that:
- models notification state explicitly
- persists and transitions state safely
- supports tenant-scoped querying and updates
- aligns with the architecture’s modular monolith, CQRS-lite, PostgreSQL, and outbox/background-dispatcher approach

Because no task-specific acceptance criteria were provided, infer completion from the story and architecture:
- notifications can exist independently from messages
- notification records support unread/read and actioned lifecycle states
- approval/exception-oriented inbox scenarios can distinguish untouched items from reviewed and acted-on items
- implementation is production-safe, tenant-safe, and test-covered

# Scope
In scope:
- Add or update a **Notification** domain model and persistence schema if not already present.
- Support notification lifecycle fields/state for:
  - unread
  - read
  - actioned
- Preserve compatibility with approval inbox use cases where a notification may reference:
  - approval
  - task
  - workflow
  - briefing
  - escalation/failure alert
- Add application commands/handlers/services for:
  - marking a notification as read
  - marking a notification as unread if supported by current UX conventions
  - marking a notification as actioned when the user completes the associated action
- Update notification queries/DTOs so clients can see current state.
- Ensure all reads/writes are **company/tenant scoped**.
- Add or update tests.

Out of scope unless required by existing code structure:
- Full new inbox UI if none exists
- Push/mobile delivery implementation
- Broad redesign of approval workflows
- Email/SMS channels
- Real-time websocket updates unless already present and trivial to wire

Implementation should prefer minimal, architecture-consistent changes over speculative expansion.

# Files to touch
Inspect the solution first and then touch the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - notification entity/aggregate
  - enums/value objects for notification status/state/type
- `src/VirtualCompany.Application/**`
  - commands and handlers for notification state transitions
  - queries/DTOs/view models for inbox/notification lists
  - validators
  - interfaces for repositories/services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - repository implementation
  - migrations
  - query projections
- `src/VirtualCompany.Api/**`
  - notification endpoints or controller/minimal API mappings
- `src/VirtualCompany.Web/**`
  - only if an existing inbox page already consumes notification DTOs and needs state fields/actions
- `src/VirtualCompany.Mobile/**`
  - only if existing mobile inbox models break due to contract changes
- tests under the corresponding test projects if present

Also inspect:
- `README.md`
- solution-wide conventions in existing modules for tasks, approvals, messages, and audit events

# Implementation plan
1. **Discover existing notification/inbox implementation**
   - Search for:
     - `Notification`
     - `Inbox`
     - `Approval`
     - `Alert`
     - `Briefing`
     - `Outbox`
   - Determine whether notifications already exist as:
     - a dedicated table/entity
     - message records with flags
     - approval projections
     - dashboard-only DTOs
   - Reuse existing patterns for tenant scoping, auditing, commands, and EF configuration.

2. **Define the notification state model**
   - Prefer an explicit state representation rather than ad hoc booleans if consistent with current codebase.
   - Recommended options:
     - enum `NotificationStatus` with values such as `Unread`, `Read`, `Actioned`
     - or separate fields if existing design requires both read tracking and action tracking independently
   - If using a single enum would lose useful information, prefer a richer model such as:
     - `ReadAt` nullable timestamp
     - `ActionedAt` nullable timestamp
     - derived status in queries
   - Choose the model that best supports the story wording:
     - unread/read is one dimension
     - actioned is another lifecycle outcome
   - If no prior model exists, a robust design is:
     - `ReadAt timestamptz null`
     - `ActionedAt timestamptz null`
     - optional `ActionedByUserId uuid null`
     - optional `ActionSummary text null`
   - This avoids ambiguity where an actioned notification is also inherently read.

3. **Update domain entity**
   - Add fields and behavior methods, for example:
     - `MarkRead(...)`
     - `MarkUnread(...)` if allowed
     - `MarkActioned(...)`
   - Enforce invariants:
     - only tenant-owned/user-owned notifications can transition
     - actioned should set read state if not already read
     - repeated operations should be idempotent where practical
   - If the domain layer uses audit/domain events, emit appropriate events.

4. **Update persistence schema**
   - Add migration for notification state columns/table changes.
   - Ensure indexes support inbox queries, likely on:
     - `company_id`
     - recipient user/company membership
     - unread/read sorting
     - created date
     - priority/type if present
   - If notifications do not yet exist as a separate table and current architecture clearly expects them, introduce a dedicated `notifications` table with pragmatic fields such as:
     - `id`
     - `company_id`
     - recipient identifiers
     - `type`
     - `title`
     - `body`
     - `entity_type`
     - `entity_id`
     - `priority`
     - `created_at`
     - `read_at`
     - `actioned_at`
     - `actioned_by_user_id`
     - `metadata_json`
   - Keep schema aligned with shared-schema multi-tenancy.

5. **Implement application commands**
   - Add command(s) and handlers for state transitions:
     - `MarkNotificationReadCommand`
     - `MarkNotificationUnreadCommand` if supported
     - `MarkNotificationActionedCommand`
   - Validate:
     - notification exists
     - belongs to current company
     - current user is allowed to update it
   - If actioning is triggered indirectly by approval decisions or workflow actions, ensure the handler can be called from those flows or that those flows update notification state as part of their transaction.

6. **Update queries and DTOs**
   - Extend notification list/detail DTOs with state fields, e.g.:
     - `IsRead`
     - `ReadAt`
     - `IsActioned`
     - `ActionedAt`
     - `StatusLabel` if the UI uses one
   - Ensure inbox sorting prioritizes:
     - pending approvals / exception alerts
     - unread before read
     - non-actioned before actioned
     - newest first
   - Do not break existing consumers; evolve contracts carefully.

7. **Wire API endpoints**
   - Add or update endpoints for notification state transitions.
   - Follow existing API conventions for:
     - route naming
     - auth
     - company context resolution
     - problem details / error responses
   - Example capabilities:
     - `POST /notifications/{id}/read`
     - `POST /notifications/{id}/unread`
     - `POST /notifications/{id}/actioned`
   - If the project uses MediatR or similar, keep controllers/endpoints thin.

8. **Integrate with approval inbox behavior**
   - Inspect approval decision flows.
   - When an approval is approved/rejected from the inbox, ensure the linked notification becomes actioned.
   - If there are escalation/workflow-failure actions that can be resolved, mark those notifications actioned when resolution occurs.
   - Keep this transactional where possible so approval state and notification state do not drift.

9. **Add tests**
   - Add unit tests for domain/application behavior:
     - unread notification can be marked read
     - read notification can be marked actioned
     - actioning sets read state if needed
     - duplicate read/action operations are idempotent or safely rejected per convention
     - cross-tenant access is blocked
   - Add integration tests for persistence/API if test infrastructure exists:
     - notification query returns state fields
     - approval action updates notification to actioned
   - Prefer focused tests over broad brittle ones.

10. **Keep auditability in mind**
   - If the codebase already records business audit events, add audit entries for meaningful state transitions, especially actioned.
   - Do not add noisy technical logging in place of business audit records.

11. **Document assumptions in code comments or PR notes**
   - If no explicit notification model existed, note why the chosen schema/state representation was selected.
   - Keep comments concise and only where they clarify lifecycle semantics.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF Core migrations are used, create/apply/verify the migration:
   - ensure the migration compiles
   - verify schema changes match the chosen model
   - verify no accidental destructive changes

4. Manually validate the main flows via API/tests:
   - create or locate a notification
   - mark it read
   - confirm query shows read state
   - mark it actioned
   - confirm query shows actioned state and still reflects read
   - if supported, mark read notification unread and verify behavior
   - verify another tenant/company cannot access or mutate it

5. Validate approval inbox integration:
   - create or simulate an approval notification
   - approve/reject via existing flow
   - confirm linked notification becomes actioned

6. Validate sorting/filtering if inbox queries exist:
   - unread items appear ahead of read/actioned items as intended
   - actioned items remain queryable for history

# Risks and follow-ups
- **State modeling ambiguity:** “actioned” is not mutually exclusive with “read”. A single enum may be too limiting. Prefer timestamps/flags if needed to preserve both dimensions.
- **Existing schema mismatch:** notifications may currently be represented as messages or approval projections. Avoid forcing a large refactor unless necessary for correctness.
- **Tenant scoping risk:** inbox-style features are easy to under-scope. Verify every repository/query/update includes company context and recipient constraints.
- **Workflow drift risk:** if approval decisions and notification updates happen in separate paths, state can become inconsistent. Prefer transactional updates or reliable outbox-driven reconciliation.
- **UI contract risk:** adding/changing DTO fields may affect web/mobile consumers. Keep changes additive where possible.
- **Follow-up candidates after this task:**
  - notification filtering by state/type/priority
  - bulk mark-as-read
  - notification archival/dismissal
  - richer actor/action metadata
  - mobile-specific inbox optimizations
  - outbox-backed notification fan-out and retry hardening