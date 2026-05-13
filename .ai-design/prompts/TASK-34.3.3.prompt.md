# Goal
Implement backlog task **TASK-34.3.3** for story **US-34.3** by delivering the production sales UI and wiring it to real tenant-scoped APIs for end-to-end deal management.

The coding agent should:
- build the real web experience for:
  - `/app/sales`
  - `/app/sales/leads`
  - `/app/sales/pipeline`
  - `/app/sales/deals/{id}`
  - a persistent sales agent panel
- ensure the UI uses live backend data, not mocks
- implement or complete missing sales API endpoints required by the acceptance criteria
- enforce authorization, validation, audit logging, and structured error responses on all sales endpoints
- persist pipeline drag-and-drop stage changes through the API
- support real-time or near-real-time action flows for lead qualification, rejection, conversion, deal stage changes, won/lost actions, recommendations, and email/activity refresh
- keep the implementation aligned with the existing modular monolith architecture, tenant isolation model, CQRS-lite application layer, and Blazor Web App frontend

# Scope
In scope:
- ASP.NET Core API work for sales domain endpoints covering:
  - dashboard summary
  - leads list and actions
  - deals list/detail
  - activities/timeline
  - recommendations
  - qualification
  - conversion to deal
  - stage changes
  - won/lost actions
  - email processing/status/timeline retrieval as needed by UI
- Blazor Web App pages/components for:
  - sales dashboard
  - leads page
  - pipeline kanban
  - deal detail page
  - persistent sales agent panel
- application-layer commands/queries and DTOs needed to support the above
- tenant-aware authorization and validation
- audit event creation for important actions
- structured API error responses consistent with existing patterns
- production styling using the app’s existing design system/layout patterns
- tests for API behavior and critical UI-backed flows where practical

Out of scope unless required to complete acceptance criteria:
- MAUI mobile work
- broad redesign of unrelated dashboard areas
- introducing a new frontend framework
- replacing existing architecture with SignalR unless the repo already uses it and it is the lowest-risk path
- speculative refactors outside the sales module

Implementation expectations:
- prefer extending existing modules and patterns over inventing parallel structures
- if some sales APIs already exist, complete and normalize them rather than duplicating
- if real-time is not already implemented, use pragmatic near-real-time refresh/polling for action flows unless there is an established real-time mechanism in the repo
- preserve shared-schema multi-tenancy with `company_id` enforcement throughout

# Files to touch
Inspect first, then update the relevant existing files in these areas.

Likely backend/API areas:
- `src/VirtualCompany.Api/**`
  - sales controllers/endpoints
  - request/response contracts
  - authorization/policy wiring
  - exception/error mapping if sales-specific handling is needed
- `src/VirtualCompany.Application/**`
  - sales queries/commands
  - handlers/services
  - validation
  - DTO/view models
- `src/VirtualCompany.Domain/**`
  - sales entities/value objects/enums if missing
  - audit-related domain concepts if needed
- `src/VirtualCompany.Infrastructure/**`
  - repositories/query services
  - EF Core mappings/configuration
  - audit persistence
  - email/activity/deal data access
- migrations if schema changes are required
  - follow the repo’s existing migration approach referenced by `docs/postgresql-migrations-archive/README.md`

Likely web UI areas:
- `src/VirtualCompany.Web/**`
  - routing for `/app/sales`, `/app/sales/leads`, `/app/sales/pipeline`, `/app/sales/deals/{id}`
  - sales page components
  - kanban board component
  - persistent sales agent panel component/layout integration
  - API client/service layer for sales endpoints
  - shared styling/components used by sales pages

Likely tests:
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint authorization/validation/error response tests
  - sales action flow tests
- add web/component tests only if the repo already has an established pattern

Also inspect:
- `README.md`
- solution and project files for existing conventions:
  - `VirtualCompany.sln`
  - `src/VirtualCompany.Web/VirtualCompany.Web.csproj`
  - `src/VirtualCompany.Api/VirtualCompany.Api.csproj`

# Implementation plan
1. **Discover existing sales implementation and conventions**
   - Search the solution for:
     - sales/deals/leads/pipeline/recommendations controllers
     - dashboard widgets
     - audit event creation
     - tenant resolution
     - authorization policies
     - structured error response middleware
     - Blazor route/layout patterns under `/app/*`
   - Identify what already exists for US-34.3 and what is missing.
   - Reuse existing naming, folder structure, MediatR/CQRS patterns, DTO conventions, and API client patterns.

2. **Map acceptance criteria to concrete endpoints and UI contracts**
   - Define or confirm the minimum endpoint set needed by the UI, for example:
     - `GET /api/sales/dashboard`
     - `GET /api/sales/leads`
     - `POST /api/sales/leads/{id}/qualify`
     - `POST /api/sales/leads/{id}/reject`
     - `POST /api/sales/leads/{id}/convert`
     - `GET /api/sales/pipeline`
     - `POST /api/sales/deals/{id}/stage`
     - `GET /api/sales/deals/{id}`
     - `GET /api/sales/deals/{id}/activities`
     - `GET /api/sales/deals/{id}/emails`
     - `GET /api/sales/recommendations`
     - `POST /api/sales/deals/{id}/won`
     - `POST /api/sales/deals/{id}/lost`
     - any email processing/status endpoint required by the detail page
   - Do not blindly use these exact routes if the repo already has a consistent route scheme; adapt to existing conventions.
   - Ensure all responses are tenant-scoped and shaped for direct UI consumption.

3. **Complete application-layer sales queries and commands**
   - Add or finish query handlers for:
     - dashboard aggregates
     - leads list
     - pipeline board grouped by stage
     - deal detail aggregate
     - recent activity
     - recommendations
   - Add or finish command handlers for:
     - qualify lead
     - reject lead
     - convert lead to deal
     - change deal stage
     - mark won
     - mark lost
     - finance document action trigger if required by existing domain
   - Add validation for all command inputs.
   - Ensure commands emit audit events for important business actions.

4. **Enforce authorization, tenant isolation, and structured errors**
   - Apply existing auth policies to all sales endpoints.
   - Ensure every query and command resolves the current company/tenant and filters by `company_id`.
   - Return forbidden/not found safely for cross-tenant access.
   - Use existing validation/error middleware patterns so sales endpoints return structured error responses.
   - Avoid leaking internal exception details.

5. **Implement dashboard page `/app/sales`**
   - Build a production-ready page showing:
     - pipeline value
     - new leads
     - hot leads
     - deals needing attention
     - forecast revenue
     - agent recommendations
     - recent activity
   - Use live API data.
   - Include loading, empty, and error states.
   - Make cards link to leads, pipeline, deal detail, or recommendation execution screens where appropriate.

6. **Implement leads page `/app/sales/leads`**
   - Render a real lead list/table with:
     - source email
     - temperature
     - qualification status
     - confidence score
     - suggested next action
   - Add actions:
     - qualify
     - reject
     - convert to deal
   - Use optimistic updates only if the app already does so consistently; otherwise refresh after success.
   - Show action feedback and validation errors inline or via existing notification patterns.

7. **Implement pipeline kanban `/app/sales/pipeline`**
   - Build a production-styled kanban board grouped by real pipeline stages from backend data.
   - Support drag-and-drop between stages.
   - Persist stage changes through the API.
   - On success, update the board and recent activity/relevant counters.
   - On failure, revert the card position and show a safe error message.
   - If stage ordering is domain-driven, fetch it from the API rather than hardcoding.
   - Keep accessibility in mind; if drag-and-drop library support is limited, provide a fallback stage-change action.

8. **Implement deal detail page `/app/sales/deals/{id}`**
   - Show:
     - deal summary
     - contact info
     - company info
     - email timeline
     - activity timeline
     - agent analysis
     - suggested reply
     - follow-up actions
     - won/lost actions
     - finance document actions
   - Use a composed detail API/view model rather than many fragmented page-level calls if practical.
   - Ensure timelines are ordered and readable.
   - Link related actions back to recommendations or execution screens if those exist.

9. **Implement persistent sales agent panel**
   - Add a persistent panel in the sales area layout, or app shell if that is the established pattern.
   - Show:
     - active alerts
     - leads needing review
     - deals needing follow-up
     - links to recommendation execution screens
   - Keep it visible across sales routes.
   - Use lightweight refresh behavior so it stays current without excessive API chatter.

10. **Add real-time or near-real-time action flow behavior**
    - Prefer existing real-time infrastructure if present.
    - Otherwise implement pragmatic refresh triggers:
      - after lead/deal actions
      - on page revisit
      - optional periodic polling for panel/activity freshness
    - Keep the implementation simple and reliable.
    - Document any near-real-time compromise in code comments or task notes if true push updates are not already supported.

11. **Audit logging and business traceability**
    - For qualify/reject/convert/stage change/won/lost/email-processing-related actions, create business audit events with:
      - actor
      - action
      - target
      - outcome
      - rationale summary where available
    - Ensure audit data is tenant-scoped and consistent with architecture guidance.

12. **Styling and UX polish**
    - Match existing production UI patterns in the Blazor app.
    - Avoid placeholder styling.
    - Include:
      - loading skeletons/spinners where appropriate
      - empty states
      - disabled states during actions
      - clear success/error feedback
      - responsive behavior within the existing layout system

13. **Testing**
    - Add API tests for:
      - authorized access
      - tenant isolation
      - validation failures
      - structured error responses
      - lead qualification/rejection/conversion
      - stage change persistence
      - won/lost actions
    - Add tests for dashboard/deal detail query handlers if that is the established test style.
    - Do not over-invest in brittle UI tests if the repo lacks a pattern; prioritize backend correctness and integration coverage.

14. **Keep changes reviewable**
    - Make focused commits/changes by layer if possible.
    - Do not mix unrelated refactors.
    - If schema changes are necessary, keep them minimal and backward-compatible where practical.

# Validation steps
1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **API verification**
   - Verify sales endpoints exist and return tenant-scoped live data.
   - Confirm unauthorized or cross-tenant requests return correct forbidden/not found behavior.
   - Confirm invalid requests return structured validation errors.
   - Confirm audit events are written for key actions.

3. **Manual UI verification**
   - Navigate to:
     - `/app/sales`
     - `/app/sales/leads`
     - `/app/sales/pipeline`
     - `/app/sales/deals/{id}`
   - Confirm all pages render with real data and production styling.
   - Confirm the persistent sales agent panel appears across relevant sales routes.

4. **Acceptance flow checks**
   - On `/app/sales`, verify display of:
     - pipeline value
     - new leads
     - hot leads
     - deals needing attention
     - forecast revenue
     - agent recommendations
     - recent activity
   - On `/app/sales/leads`, verify list columns and actions:
     - source email
     - temperature
     - qualification status
     - confidence score
     - suggested next action
     - qualify/reject/convert
   - On `/app/sales/pipeline`, drag a deal to a new stage and verify:
     - UI updates
     - API persists the change
     - refresh still shows the new stage
   - On `/app/sales/deals/{id}`, verify presence of:
     - summary
     - contact/company info
     - email timeline
     - activity timeline
     - agent analysis
     - suggested reply
     - follow-up actions
     - won/lost actions
     - finance document actions

5. **Real-time/refresh behavior**
   - Perform lead/deal actions and verify dependent widgets/panel/timelines refresh appropriately.
   - If polling is used, verify it is bounded and not excessive.

6. **Regression checks**
   - Confirm no unrelated routes break.
   - Confirm shared layout/navigation still works.
   - Confirm no mock/demo data remains in the production sales pages.

# Risks and follow-ups
- **Risk: existing sales APIs may be partial or inconsistent**
  - Mitigation: normalize around existing patterns rather than creating duplicate endpoint families.

- **Risk: true real-time infrastructure may not exist**
  - Mitigation: implement reliable near-real-time refresh/polling now, and note SignalR as a follow-up only if needed.

- **Risk: drag-and-drop in Blazor can become brittle**
  - Mitigation: use the simplest proven approach in the repo; provide a fallback stage-change interaction if necessary.

- **Risk: deal detail page may