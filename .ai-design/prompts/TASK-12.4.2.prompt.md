# Goal
Implement backlog task **TASK-12.4.2 — Mobile can display quick company status and task follow-up summaries** for story **ST-604 Mobile companion for approvals, alerts, and quick chat**.

Deliver a focused vertical slice in the existing .NET solution so the **.NET MAUI mobile app** can show:
- a concise **company status summary**
- a concise **task follow-up summary list**

Use existing backend/domain patterns where possible. Reuse shared contracts and backend APIs. Keep the mobile experience intentionally lightweight and executive-focused.

# Scope
Implement only what is needed for this task.

In scope:
- Add or extend a backend **mobile summary query endpoint** that returns:
  - quick company status summary
  - recent task follow-up summaries
- Add application/query logic to assemble this data in a tenant-scoped way
- Add shared DTO/contracts for the mobile payload
- Add MAUI mobile UI to fetch and display:
  - company status card/section
  - task follow-up summary list
- Handle loading, empty, and error states in mobile
- Ensure company/tenant context is respected
- Keep payload concise and mobile-friendly

Out of scope unless already trivially supported by existing code:
- New workflow engine behavior
- New approval decision logic
- Full dashboard parity with web
- Push notifications
- Offline sync beyond basic resilient fetch behavior
- New analytics infrastructure
- Large schema redesigns
- Mobile-specific business rules separate from backend

Assumptions to follow:
- If no dedicated notification/summary tables exist yet, derive summaries from existing task/approval/activity data already available in the system.
- If daily briefing infrastructure already exists, reuse it where sensible, but do not block this task on full briefing generation.
- Prefer **query-side composition** over introducing new persistence unless clearly necessary.
- Keep summaries concise, operational, and safe; do not expose chain-of-thought.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely backend files:
- `src/VirtualCompany.Api/...`
  - mobile-facing controller/endpoints
  - DI registration if needed
- `src/VirtualCompany.Application/...`
  - query/handler/service for mobile company status + task follow-up summaries
  - interfaces/contracts for read models
- `src/VirtualCompany.Domain/...`
  - only if a small domain enum/value object is truly needed
- `src/VirtualCompany.Infrastructure/...`
  - query repository / EF / Dapper implementation
  - tenant-scoped data access

Likely shared contract files:
- `src/VirtualCompany.Shared/...`
  - DTOs for mobile summary response
  - request/response models if shared between API and mobile

Likely mobile files:
- `src/VirtualCompany.Mobile/...`
  - API client/service
  - view models
  - pages/views
  - XAML and code-behind as applicable
  - DI registration/navigation wiring

Likely tests:
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint/query tests for tenant scoping and response shape
- add application/integration tests if the project structure already supports them

Also review:
- `README.md`
- any existing architecture or API conventions in the repo
- existing auth/company context handling
- existing mobile pages for alerts, approvals, or briefings to align UX and patterns

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect API structure, application query conventions, shared DTO usage, and MAUI app architecture.
   - Find existing implementations for:
     - company selection / tenant context
     - dashboard or briefing summary queries
     - task list/read models
     - mobile API clients and authenticated requests
   - Reuse naming, folder structure, and dependency injection patterns already present.

2. **Define the mobile summary contract**
   - Add a concise shared response model, for example:
     - `MobileCompanyStatusSummaryDto`
     - `MobileTaskFollowUpSummaryDto`
     - `MobileHomeSummaryResponse` or similarly named aggregate DTO
   - Include only compact fields useful on mobile, such as:
     - company name
     - generated/updated timestamp
     - counts for pending approvals, active alerts, open tasks, blocked tasks, overdue tasks
     - optional short status headline/subtitle
     - recent task follow-up items with:
       - task id
       - title
       - status
       - priority
       - assigned agent display name if available
       - short rationale/follow-up summary
       - updated timestamp
   - Keep fields nullable where data may not exist.

3. **Implement backend query composition**
   - Add an application-layer query/service that assembles the mobile summary from existing data sources.
   - Ensure strict tenant scoping by `company_id`.
   - Prefer read-only query composition from existing entities such as:
     - tasks
     - approvals
     - alerts/notifications if available
     - recent activity/briefings if available
   - If no explicit “company status” entity exists, derive a lightweight summary from aggregates:
     - pending approvals count
     - open task count
     - blocked task count
     - overdue task count
     - recent activity timestamp
   - For task follow-up summaries:
     - return a small recent list, sorted by most recently updated
     - include only tasks with meaningful follow-up context if possible
     - fall back to title/status/updated timestamp when rationale summary is absent

4. **Expose API endpoint**
   - Add a mobile-oriented GET endpoint, following existing routing conventions, e.g. under a mobile or summary route.
   - Reuse existing auth and company resolution mechanisms.
   - Return a compact response with proper HTTP status handling:
     - `200 OK` with data
     - safe empty state if no tasks/activity exist
     - `403/404` according to existing tenant access conventions
   - Do not add mobile-specific business logic in the controller.

5. **Implement infrastructure/query access**
   - Add repository/query implementation using the project’s existing data access style.
   - Keep queries efficient:
     - tenant-filtered
     - select only needed columns
     - bounded result size for follow-up items
   - Avoid N+1 queries.
   - If there is existing caching for dashboard aggregates and it is easy to reuse safely, do so; otherwise keep it simple.

6. **Build MAUI client integration**
   - Add/update a mobile API client method to fetch the summary endpoint.
   - Ensure authenticated requests include company context the same way other mobile calls do.
   - Add resilient error handling consistent with the app’s current networking patterns.

7. **Build mobile UI**
   - Add or extend the relevant mobile page, likely the mobile home/dashboard/briefing area.
   - Display:
     - quick company status section/card
     - task follow-up summaries section/list
   - Include:
     - loading state/skeleton or activity indicator
     - empty state when no summary data exists
     - error state with retry
   - Keep the UI concise and touch-friendly.
   - Do not attempt full dashboard parity.

8. **View model/state management**
   - Add a view model that:
     - loads the summary on page appearance
     - exposes loading/error/empty flags
     - supports manual refresh if the app already uses pull-to-refresh or refresh commands
   - Keep formatting mobile-friendly:
     - relative timestamps if existing helpers exist
     - short labels
     - truncated summaries

9. **Testing**
   - Add backend tests for:
     - tenant scoping
     - response shape
     - empty data behavior
     - task ordering and bounded result count
   - Add tests for any application query logic if test infrastructure exists.
   - If MAUI UI tests are not present, at least add unit tests for the mobile view model if feasible in current repo patterns.

10. **Document minimal assumptions in code comments or PR notes**
   - If “company status” is derived rather than persisted, make that explicit in code naming/comments.
   - Keep implementation extensible for future richer mobile summaries.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate backend:
   - Authenticate as a user with access to a company
   - Call the new mobile summary endpoint
   - Confirm response includes:
     - company status summary
     - recent task follow-up summaries
   - Confirm another company’s data is not accessible

4. Manually validate data scenarios:
   - Company with no tasks/activity returns a valid empty response
   - Company with mixed task states shows sensible counts
   - Tasks with and without rationale summaries render safely

5. Manually validate mobile app:
   - Sign in
   - Select company if applicable
   - Navigate to the relevant mobile summary/home screen
   - Confirm loading state appears
   - Confirm company status section renders
   - Confirm task follow-up list renders
   - Confirm empty state and retry behavior work
   - Confirm API/network failure shows a non-crashing error state

6. Regression check:
   - Ensure approvals, alerts, briefing, and chat flows still build and function if present
   - Ensure no web behavior is broken by shared contract changes

# Risks and follow-ups
- **Risk: no existing summary source**
  - Mitigation: derive quick status from task/approval aggregates without introducing new persistence.

- **Risk: unclear mobile architecture patterns**
  - Mitigation: inspect existing MAUI pages/services first and conform to established patterns rather than inventing a new structure.

- **Risk: tenant scoping mistakes**
  - Mitigation: reuse existing company resolution and authorization mechanisms; add explicit tests.

- **Risk: payload becomes too dashboard-like**
  - Mitigation: keep response intentionally compact and bounded for mobile companion use.

- **Risk: missing rationale/follow-up text on tasks**
  - Mitigation: gracefully fall back to status/title/updated timestamp and expose nullable summary fields.

Follow-up candidates, but do not implement unless already trivial:
- deep links from task follow-up items into task detail
- cached mobile summary endpoint
- richer alert/approval rollups
- relative time formatting helpers shared across mobile views
- optional pull-to-refresh and background refresh behavior