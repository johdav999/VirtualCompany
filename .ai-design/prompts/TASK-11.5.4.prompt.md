# Goal
Implement backlog task **TASK-11.5.4** for **ST-505 — Daily briefings and executive summaries** by adding the ability for users to **configure delivery preferences for in-app and mobile notifications** related to scheduled briefings/summaries.

The implementation should fit the existing **multi-tenant modular monolith** architecture and support:
- tenant-scoped user preferences
- separate delivery controls for **in-app** and **mobile**
- preference use by the notification/briefing delivery flow
- web-first management UX, with backend contracts reusable by mobile

Because no explicit acceptance criteria were provided for this task beyond the story-level requirement, define and implement a pragmatic v1 slice that is consistent with the story and architecture.

# Scope
Implement a focused vertical slice for **briefing delivery preferences** only.

Include:
- A tenant-scoped persistence model for user notification delivery preferences
- Application-layer commands/queries to read and update preferences
- API endpoints for retrieving and saving preferences
- Web UI to manage preferences
- Integration of preferences into briefing notification delivery decision logic
- Tests for tenant isolation, validation, default behavior, and preference-aware delivery

Assume v1 preference coverage includes:
- notification category: **daily briefing / executive summary**
- channels:
  - **in-app**
  - **mobile**
- enable/disable flags per channel
- optional frequency granularity if already natural in the codebase, but do **not** expand into a broad notification-center redesign unless the repository already has that foundation

Out of scope unless already trivial and clearly supported by existing code:
- email delivery
- push provider integration details
- a full generic notification preferences framework for every notification type
- major redesign of notification domain models
- mobile UI implementation in MAUI unless the existing app already has an obvious settings screen pattern and the work is very small

# Files to touch
Inspect the solution first and then update the most appropriate files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:
- Domain entity/value object/enums for notification preferences
- EF Core configuration and DbContext mappings
- Migration or migration artifact consistent with repo conventions
- Application commands/queries/DTOs/validators/handlers
- API controller or minimal API endpoints
- Web page/component for user preferences
- Briefing generation or notification dispatch service/worker logic
- Tests covering API and application behavior

Before coding, inspect:
- existing tenant-scoped user settings/preferences patterns
- existing notification/message/inbox models
- existing daily briefing / summary generation flow
- existing outbox/dispatcher/background worker patterns
- existing API style and CQRS conventions
- existing Blazor page patterns for settings forms

# Implementation plan
1. **Inspect the current architecture in code**
   - Find whether there is already:
     - a `Notification`, `UserSettings`, `Preferences`, or `Profile` model
     - a briefing generation service
     - a notification dispatcher/background worker
     - a current inbox/in-app notification implementation
   - Reuse existing abstractions instead of introducing parallel patterns.
   - Confirm how tenant context and current user context are resolved in API/application layers.

2. **Define the v1 domain model**
   - Add a tenant-scoped, user-scoped preference model for briefing delivery.
   - Prefer a simple explicit model over an over-generalized framework.
   - Recommended shape:
     - `company_id`
     - `user_id`
     - category/key for briefing notifications
     - `in_app_enabled`
     - `mobile_enabled`
     - timestamps
   - If the codebase already uses JSON settings blobs for user preferences, it is acceptable to store this in a structured JSON field instead, but only if validation and querying remain straightforward.
   - Define default behavior clearly:
     - default to **in-app enabled**
     - default to **mobile enabled** only if that aligns with current mobile notification assumptions; otherwise default conservatively and document it in code/tests
   - Ensure uniqueness per `(company_id, user_id, category)` or equivalent.

3. **Persist the model**
   - Add EF Core entity/configuration and wire it into the infrastructure persistence layer.
   - Add a migration using the repository’s established migration approach.
   - Enforce tenant ownership and uniqueness constraints.
   - Include indexes appropriate for lookup during dispatch.

4. **Add application-layer contracts**
   - Create:
     - query to get current user’s briefing delivery preferences for the active company
     - command to update current user’s briefing delivery preferences
   - Add validation:
     - active company context required
     - current user required
     - reject invalid category/channel combinations if category is exposed
   - Return a simple DTO suitable for both web and mobile clients.

5. **Expose API endpoints**
   - Add authenticated tenant-scoped endpoints such as:
     - `GET /api/notification-preferences/briefings`
     - `PUT /api/notification-preferences/briefings`
   - Follow existing API conventions for:
     - authorization
     - company context resolution
     - problem details / validation responses
   - Ensure users can update only **their own** preferences unless an existing admin-managed settings pattern already exists.

6. **Implement web UI**
   - Add or extend a settings/preferences page in Blazor Web.
   - Provide controls for:
     - in-app delivery enabled/disabled
     - mobile delivery enabled/disabled
   - Load current values from the API/application layer.
   - Save changes with clear success/error feedback.
   - Keep UX simple and consistent with the app’s existing settings patterns.

7. **Integrate preferences into briefing delivery**
   - Find the scheduled daily briefing / weekly summary generation and delivery path.
   - Apply preference checks when creating/delivering notifications:
     - if `in_app_enabled = true`, create/store in-app notification/message
     - if `mobile_enabled = true`, enqueue mobile delivery or mark for mobile channel delivery according to existing architecture
   - Do not block summary generation itself if one or both channels are disabled; only suppress channel delivery.
   - If mobile push infrastructure does not yet exist, still persist or emit the mobile delivery intent through the existing notification pipeline in the most architecture-consistent way, without inventing a fake push provider.

8. **Handle defaults and backward compatibility**
   - Existing users without a preference record should receive deterministic defaults.
   - On first read, either:
     - materialize defaults without writing, or
     - create-on-write when the user saves
   - Avoid requiring a backfill job unless necessary.

9. **Add tests**
   - Cover:
     - get preferences returns defaults when no record exists
     - update preferences persists correctly
     - tenant isolation prevents cross-company access
     - one user cannot update another user’s preferences
     - briefing delivery respects in-app disabled
     - briefing delivery respects mobile disabled
     - both enabled results in both applicable delivery paths
   - Prefer API/integration tests where possible, plus focused unit tests for delivery decision logic.

10. **Keep implementation aligned with story intent**
   - This task is about **configurable delivery preferences**, not full notification infrastructure expansion.
   - Favor the smallest coherent implementation that satisfies ST-505 and is easy to extend later to other notification categories.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in the normal workflow, generate/apply them per repo convention and verify:
   - the new table/columns exist
   - uniqueness and tenant-scoped constraints are correct

4. Manually verify via web UI:
   - sign in as a user in company A
   - open the notification/briefing preferences screen
   - confirm defaults render correctly
   - disable in-app and save
   - re-open and confirm persistence
   - enable in-app, disable mobile, save, and confirm persistence

5. Manually verify API behavior:
   - `GET` returns current or default preferences
   - `PUT` updates preferences
   - invalid payloads return validation errors
   - requests without valid tenant/user context are rejected appropriately

6. Manually verify delivery behavior:
   - trigger or simulate a daily briefing/summary generation
   - confirm in-app notification/message is created only when enabled
   - confirm mobile delivery path is invoked/enqueued only when enabled
   - confirm summary generation still succeeds even if both channels are disabled

7. Verify multi-tenant safety:
   - same user with multiple company memberships can have different preferences per company
   - no preference leakage across tenants

# Risks and follow-ups
- **Unclear existing notification model:** The repo may not yet distinguish messages, notifications, and mobile delivery cleanly. Reuse existing patterns and avoid over-abstracting.
- **Missing mobile delivery infrastructure:** If push delivery is not implemented yet, integrate preference checks at the dispatch intent level and leave provider-specific delivery for a later task.
- **Schema choice risk:** If there is already a user settings JSON model, adding a separate table may be unnecessary. Prefer consistency with the existing codebase over the suggested table shape.
- **Acceptance ambiguity:** Since this task has no explicit acceptance criteria, document any assumptions in code comments and PR notes, especially defaults and category scope.
- **Likely follow-ups:**
  - extend preferences to approvals, escalations, and workflow failures
  - add weekly summary-specific controls if daily and weekly need separate toggles
  - expose the same preferences in the MAUI app
  - add admin/org-level defaults
  - add quiet hours / schedule-aware delivery controls