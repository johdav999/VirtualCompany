# Goal
Implement backlog task **TASK-11.5.1** for **ST-505 Daily briefings and executive summaries** in the existing .NET solution so the system can **generate scheduled daily briefings and weekly summaries per company**.

The implementation prompt should direct the coding agent to add the minimum coherent vertical slice needed to support:
- scheduled generation of **daily briefings** and **weekly summaries**
- **per-company** tenant-scoped execution
- aggregation of relevant executive context such as alerts, approvals, KPI highlights, anomalies, and notable agent updates
- persistence of generated summaries as **messages and/or notifications**
- visibility in the dashboard-facing data layer
- configurable **delivery preferences** for in-app and mobile notifications

Because the architecture is a modular monolith with background workers, PostgreSQL, Redis, ASP.NET Core, Blazor, and .NET MAUI, the implementation should fit those patterns and avoid introducing a separate service.

# Scope
Include:
- Domain and application support for scheduled executive summaries
- Tenant-scoped scheduling logic for daily and weekly cadence
- Background worker/job entrypoint to generate summaries
- Summary aggregation service using existing task/workflow/approval/message/audit data where available
- Persistence of generated summaries into the communication model
- Notification creation for briefing availability
- Company-level or user-level delivery preference storage, whichever is most consistent with the current codebase
- Read/query support so web/mobile/dashboard can fetch the latest briefing/summary
- Tests for scheduling, tenant isolation, persistence, and basic aggregation behavior

Do not include unless already trivial in the codebase:
- email delivery
- push notification provider integration
- full LLM-authored narrative generation if deterministic templated summaries are sufficient for this task slice
- major dashboard redesign
- arbitrary workflow builder changes
- cross-service infrastructure

If the repository already contains adjacent primitives for notifications, scheduling, or dashboard aggregates, reuse them rather than creating parallel abstractions.

# Files to touch
Inspect the solution first and then touch only the relevant files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - add entities/value objects/enums for briefing cadence, delivery preferences, summary type, or notification type if missing
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers
  - scheduling and generation services/interfaces
  - DTOs for briefing payloads
  - validation
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - background worker/job scheduler implementation
  - persistence/migrations support
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for preferences and retrieval if API-driven
- `src/VirtualCompany.Web/**`
  - minimal UI wiring for viewing latest briefing and configuring preferences if this story slice already exposes dashboard settings here
- `src/VirtualCompany.Mobile/**`
  - only if there is already a briefing/notification consumption surface and adding a small client contract is straightforward
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially other test projects if present for application/infrastructure layers

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, identify existing patterns for:
- multi-tenant scoping via `company_id`
- background jobs/workers
- outbox/notification dispatch
- CQRS handlers
- EF migrations
- message/conversation persistence
- dashboard queries

# Implementation plan
1. **Inspect the current architecture in code**
   - Find how tenant context is resolved and enforced.
   - Find existing entities for:
     - companies
     - users/memberships
     - tasks
     - workflows
     - approvals
     - conversations/messages
     - notifications
     - audit events
   - Find any existing scheduler/background worker infrastructure.
   - Find whether there is already a dashboard aggregate/query service that can be reused for KPI highlights, alerts, and approvals.
   - Find whether there is already a settings model for company preferences or user preferences.

2. **Model the briefing domain**
   Implement the smallest consistent model needed. Prefer reuse of existing `messages` and `notifications` tables if possible.

   Add support for:
   - summary type:
     - `daily_briefing`
     - `weekly_summary`
   - delivery preferences:
     - in-app enabled
     - mobile enabled
     - daily briefing enabled
     - weekly summary enabled
     - preferred delivery time and timezone if needed and if not already covered by company timezone
   - generated summary payload containing structured sections such as:
     - alerts
     - pending approvals
     - KPI highlights
     - anomalies
     - notable agent updates
     - linked entity references

   Prefer storing the rendered summary in `messages` with `message_type = summary` and a `structured_payload` that includes:
   - summary type
   - company id
   - generation window
   - section data
   - source references

   If notifications exist separately, create a notification record pointing to the generated message.

3. **Add persistence support**
   - Add EF Core configuration and migration for any new tables/columns required.
   - If preferences can live in `companies.settings_json` or a similar existing JSONB field, prefer that over a new table unless the codebase strongly favors explicit relational tables.
   - Ensure all new tenant-owned records include `company_id`.
   - Add indexes appropriate for:
     - latest summary lookup by company and type
     - scheduled generation scans
     - unread notification retrieval if applicable

4. **Implement application services**
   Create a cohesive application-layer service, e.g.:
   - `IExecutiveSummaryGenerator`
   - `ExecutiveSummaryGenerator`
   - `IBriefingScheduleService`
   - `BriefingScheduleService`

   Responsibilities:
   - determine whether a company is due for a daily or weekly summary
   - gather source data for the relevant time window
   - build a deterministic structured summary
   - persist the summary message
   - create notification records/outbox events for enabled delivery channels
   - avoid duplicate generation for the same company + summary type + period

   Use deterministic summary composition first. If there is already an orchestration/LLM summarization pipeline with safe structured outputs, it may be used behind an interface, but do not make this task depend on a large AI integration if not already present.

5. **Define aggregation logic**
   Build a summary from existing data sources, scoped by company and time window:
   - pending approvals count and top items
   - alerts/escalations/workflow failures if available
   - KPI highlights from existing analytics/dashboard aggregates if available
   - notable agent updates from recent completed/failed/blocked tasks, workflow changes, or audit events
   - anomalies only if there is already a signal source; otherwise include a placeholder empty section rather than inventing anomaly detection

   Keep the aggregation logic:
   - tenant-scoped
   - deterministic
   - testable
   - resilient to missing modules/data

   If some source data is not yet implemented in the repo, degrade gracefully and still generate a valid summary with available sections.

6. **Implement scheduling**
   Add a background worker that:
   - periodically scans active companies
   - checks company timezone and preferences
   - generates due daily briefings and weekly summaries
   - uses distributed locking/idempotency to prevent duplicate generation
   - logs correlation IDs and tenant context

   Requirements:
   - daily briefing cadence should respect company timezone
   - weekly summary should use a consistent weekly boundary
   - retries should distinguish transient failures from permanent validation/business failures
   - generation should be safe to rerun without duplicate messages for the same period

   If the codebase already has a scheduler abstraction, plug into it instead of creating a new hosted service pattern.

7. **Expose retrieval/query support**
   Add query handlers and API endpoints as needed for:
   - get latest daily briefing for current company
   - get latest weekly summary for current company
   - list recent summaries
   - get/update delivery preferences

   Ensure authorization and tenant scoping are enforced consistently.

8. **Wire dashboard/mobile consumption**
   Add only minimal UI/API integration needed for this task:
   - dashboard-facing query can surface latest briefing
   - preferences can be configured
   - notifications can point to the generated summary

   Keep UI changes small and aligned with existing patterns. If UI is not yet ready, ensure backend contracts are complete and test-covered.

9. **Testing**
   Add tests covering:
   - company A cannot read company B summaries
   - scheduler generates one daily briefing per company per day window
   - scheduler generates one weekly summary per company per week window
   - disabled preferences prevent generation or delivery according to chosen design
   - generated summaries are persisted as messages with expected structured payload
   - notifications are created when enabled
   - duplicate job execution does not create duplicate summaries
   - aggregation handles empty data gracefully

10. **Documentation and code quality**
   - Follow existing naming and project conventions.
   - Keep modules cleanly separated.
   - Add concise comments only where logic is non-obvious.
   - If a migration is added, ensure it matches the repository’s migration workflow.

# Validation steps
Run and report the results of the relevant validation commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow, generate/apply/verify them according to the repository conventions.

4. Manually verify, via tests or local execution, these scenarios:
   - a company with summaries enabled gets a daily briefing generated
   - a company with weekly summaries enabled gets a weekly summary generated
   - summaries are stored and retrievable for the correct tenant only
   - notification records are created for enabled channels
   - rerunning the scheduler for the same period does not duplicate the summary
   - empty company data still yields a valid, non-crashing summary payload

5. In the final implementation notes, include:
   - what files were changed
   - what schema changes were introduced
   - what assumptions were made where the repo lacked explicit acceptance criteria
   - any intentionally deferred items

# Risks and follow-ups
- **No explicit acceptance criteria in the task header:** use the ST-505 story criteria from the backlog as the functional source of truth.
- **Unknown existing scheduler pattern:** avoid inventing a second scheduling mechanism if one already exists.
- **Unknown notification model:** prefer reuse over new tables unless necessary.
- **Analytics/anomaly data may be incomplete:** generate summaries from available signals and leave anomaly detection simple or empty.
- **Timezone correctness:** daily/weekly boundaries must be based on company timezone, not server local time.
- **Duplicate generation risk:** enforce idempotency by company + summary type + period key.
- **UI scope creep:** keep web/mobile changes minimal unless the repository already has clear extension points.
- **Future follow-ups likely needed:**
  - email delivery
  - push notifications
  - richer executive summary rendering
  - LLM-assisted narrative polishing
  - per-user delivery preferences beyond company defaults
  - dashboard widgets for summary history and drill-down