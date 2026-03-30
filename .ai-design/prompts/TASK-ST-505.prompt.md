# Goal
Implement backlog task **TASK-ST-505 — Daily briefings and executive summaries** for the Virtual Company solution.

Deliver a first production-ready slice of scheduled executive summaries aligned to story **ST-505**:
- generate **daily briefings** and **weekly summaries** per company on a schedule
- aggregate content from existing domain data such as alerts, approvals, KPI highlights, anomalies, and notable agent/task/workflow updates
- persist generated summaries as **messages and/or notifications**
- surface them for dashboard consumption
- support **user delivery preferences** for **in-app** and **mobile** notification channels

Because the story acceptance criteria are absent at the task level, use the backlog story acceptance criteria for ST-505 as the implementation target.

# Scope
Implement the minimum coherent vertical slice across domain, application, infrastructure, API, workers, and web/mobile-facing contracts.

Include:
- tenant-scoped scheduled briefing generation
- daily and weekly cadence support
- summary aggregation service with deterministic structured inputs
- persistence of generated briefings into communication/notification storage
- user/company delivery preference model and CRUD/query path
- background worker job(s) for schedule execution
- dashboard/query support to fetch latest briefing
- audit-friendly linkage to underlying entities where practical
- tests for scheduling, tenant scoping, persistence, and preference behavior

Do not overreach into:
- email delivery
- push notification provider integration
- full MAUI UI implementation unless shared contracts are required
- advanced LLM prompt optimization if a deterministic template-based summary is sufficient for v1
- broad analytics redesign

Prefer a v1 that is reliable, tenant-safe, and extensible.

# Files to touch
Inspect the solution first and then update the appropriate files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `src/VirtualCompany.Shared`
- tests projects if present

Likely file categories to add/update:
- Domain entities/value objects/enums for:
  - briefing summary
  - briefing cadence/type
  - delivery preferences
  - notification/message linkage
- Application commands/queries/handlers for:
  - generate company briefing
  - generate due briefings across tenants
  - get latest briefing
  - update/get delivery preferences
- Application DTOs/contracts for:
  - briefing summary payload
  - dashboard briefing card/view model
  - preference settings
- Infrastructure persistence:
  - EF Core entity configs
  - migrations
  - repositories/query services
  - background job scheduler/worker
- API endpoints/controllers/minimal APIs for:
  - preferences
  - latest briefing retrieval if not already covered by dashboard endpoint
- Web components/pages for:
  - dashboard briefing display
  - settings/preferences UI if in scope for this task
- Shared contracts used by web/mobile
- Tests:
  - unit tests
  - integration tests
  - worker scheduling tests

If an existing notifications/messages model already supports this, extend it instead of introducing parallel concepts.

# Implementation plan
1. **Inspect current architecture and existing primitives**
   - Review current implementations for:
     - messages/conversations
     - notifications/inbox
     - tasks/workflows/approvals
     - dashboard aggregates
     - background workers/scheduler
     - tenant resolution
   - Reuse existing modules and naming conventions.
   - Identify whether there is already a notification entity separate from messages.
   - Identify whether there is already a user settings/preferences model.

2. **Define the domain model for briefings**
   - Add a domain concept for generated executive summaries, either:
     - a dedicated `Briefing` entity/table, or
     - a structured `message` subtype plus metadata
   - Prefer a dedicated entity if needed for schedule state, source references, and dashboard retrieval; otherwise use `messages` with `message_type` such as:
     - `daily_briefing`
     - `weekly_summary`
   - Include fields such as:
     - `id`
     - `company_id`
     - `briefing_type` (`daily`, `weekly`)
     - `period_start_utc`
     - `period_end_utc`
     - `title`
     - `summary_body`
     - `structured_payload_json`
     - `generated_at`
     - `status`
     - `source_refs_json`
   - Add delivery preference model, likely tenant-scoped per user:
     - in-app enabled
     - mobile enabled
     - daily enabled
     - weekly enabled
     - preferred delivery time / timezone-aware schedule if feasible
   - Keep defaults conservative and useful:
     - in-app enabled by default
     - mobile enabled only if app notifications are supported logically
     - daily + weekly enabled by default for executive/admin roles if role-aware defaults exist, otherwise all users opt-in defaults can be simple

3. **Design persistence**
   - Add EF Core entities/configurations and migration(s).
   - Ensure all tenant-owned records include `company_id`.
   - Add indexes for:
     - latest briefing by company/type/generated time
     - preferences by company/user
     - uniqueness/idempotency for one briefing per company/type/period
   - Recommended uniqueness:
     - `(company_id, briefing_type, period_start_utc, period_end_utc)`
   - If using existing `messages`, ensure structured payload can store:
     - KPI highlights
     - alert counts/items
     - pending approvals
     - anomalies
     - notable agent updates
     - linked task/approval/workflow references

4. **Implement briefing aggregation service**
   - Create an application service that builds a structured briefing input from existing data.
   - Aggregate at minimum:
     - pending/high-priority approvals
     - workflow failures/escalations
     - task counts/status changes
     - notable agent activity/health changes
     - KPI highlights if available from existing analytics queries
     - anomalies/alerts if available; otherwise derive simple anomalies from failed/blocked/escalated items
   - Keep the aggregation deterministic and testable.
   - Return a structured model like:
     - headline
     - key highlights
     - approvals section
     - alerts/anomalies section
     - KPI section
     - notable updates section
     - source references
   - If LLM summarization infrastructure already exists and is low-risk, optionally pass the structured aggregate through a summarizer. If not, use template-based rendering for v1.

5. **Implement generation command flow**
   - Add command/handler for generating a briefing for a single company and cadence.
   - Steps:
     - resolve tenant/company
     - compute reporting window based on company timezone
     - check idempotency / existing generated briefing for that period
     - aggregate data
     - render summary text + structured payload
     - persist briefing/message
     - create notification records for users whose preferences allow delivery
     - emit audit event if audit infrastructure exists
   - Ensure generation is safe to retry.

6. **Implement scheduled background execution**
   - Add/extend a background worker to scan for companies due for:
     - daily briefing
     - weekly summary
   - Respect company timezone where available.
   - Use distributed locking / idempotency to avoid duplicate generation.
   - Keep execution tenant-scoped and correlation-ID aware.
   - If there is an outbox pattern in place, use it for notification fan-out.
   - Log failures distinctly:
     - transient infra failures => retry
     - business/config issues => mark and continue

7. **Implement delivery preferences**
   - Add application/API support to:
     - get current user briefing preferences for a company
     - update preferences
   - Include fields for:
     - daily enabled
     - weekly enabled
     - in-app enabled
     - mobile enabled
   - If user role filtering is needed, enforce authorization.
   - If there is an existing settings page/module, integrate there rather than creating a new standalone pattern.

8. **Persist as messages/notifications and expose for dashboard**
   - Ensure generated summaries are visible in the dashboard via query endpoint/service.
   - Add query for:
     - latest daily briefing
     - latest weekly summary
     - recent briefing history if easy
   - If the dashboard already has a “daily briefing” slot, wire it to the new query.
   - Notifications should point back to the generated briefing/message.

9. **UI integration**
   - In `VirtualCompany.Web`, add/update:
     - dashboard component/card for latest briefing
     - settings UI for delivery preferences if this task includes web UX
   - Keep SSR-first and simple.
   - Show:
     - title
     - generated time
     - concise sections
     - links to underlying approvals/tasks/workflows where available
   - If no briefing exists yet, show a clear empty state.

10. **Mobile/shared contract support**
   - Update shared DTOs/contracts so the mobile app can consume:
     - latest briefing
     - notification metadata
     - preference settings
   - Do not build full MAUI UI unless already trivial and in scope.

11. **Testing**
   - Add unit tests for:
     - reporting window calculation by timezone/cadence
     - aggregation logic
     - idempotent generation
     - preference filtering
   - Add integration tests for:
     - tenant-scoped persistence
     - scheduled generation path
     - dashboard/latest briefing query
     - notification creation
   - Add migration verification if the repo has migration tests or startup validation.

12. **Documentation**
   - Update relevant README or module docs with:
     - how briefings are generated
     - schedule assumptions
     - preference behavior
     - known limitations (e.g., no email yet)

# Validation steps
1. Restore/build the solution:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. Verify database migration applies cleanly.
4. Create or seed at least:
   - one company with timezone
   - one executive/admin user membership
   - sample tasks, approvals, alerts/workflow failures, and agent activity
5. Trigger single-company generation manually via handler/API/debug path and verify:
   - one daily briefing is created
   - one weekly summary is created for the correct period
   - duplicate generation for the same period is prevented
6. Verify persisted output:
   - briefing/message record exists with tenant scope
   - structured payload contains expected sections
   - source references link to underlying entities where implemented
7. Verify notification behavior:
   - users with in-app enabled receive notification records
   - users with disabled preferences do not
   - mobile-enabled preference is persisted even if push delivery is not yet implemented
8. Verify dashboard query/UI:
   - latest briefing appears for the correct company
   - empty state appears when none exists
9. Verify authorization/tenant isolation:
   - another company cannot access the briefing or preferences
10. Verify worker behavior:
   - scheduled worker only generates due briefings
   - retries do not create duplicates
   - failures are logged with correlation/tenant context

# Risks and follow-ups
- **Schema choice risk:** If you force briefings into `messages` only, future reporting/schedule state may become awkward. Consider a dedicated entity with optional message/notification projection if the current model is too limited.
- **Timezone complexity:** Daily/weekly windows must use company timezone correctly. Be explicit about week boundary assumptions.
- **Data availability:** KPI highlights/anomalies may not yet exist as first-class data. For v1, derive from available tasks, approvals, workflow exceptions, and agent activity rather than blocking delivery.
- **Notification model ambiguity:** If notifications are not yet implemented, keep the design compatible with ST-603 and avoid painting the system into a corner.
- **LLM cost/reliability:** Prefer deterministic summary composition first. Add LLM polishing later only if existing orchestration support is mature and testable.
- **Duplicate generation:** Scheduled jobs across instances require idempotency and locking.
- **Preference defaults:** Be careful not to spam all users. If role-aware targeting is easy, prioritize executive/admin/owner users first.
- **Dashboard coupling:** Avoid embedding aggregation logic in UI queries; keep it in application services.

Follow-up suggestions after this task:
- add email delivery
- add push/mobile dispatch integration
- add richer anomaly detection from analytics
- add briefing history page and drill-down UX
- add per-user preferred delivery time
- add audit event views for briefing generation lineage