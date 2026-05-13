# Goal
Implement backlog task **TASK-34.3.2** for story **US-34.3** by delivering a **production-ready sales UI** in the Blazor web app and ensuring it is **fully wired to live tenant-scoped APIs** for dashboard, leads, pipeline, deal detail, and persistent sales agent panel.

The end result should provide a usable **sales review and triage experience** at:

- `/app/sales`
- `/app/sales/leads`
- `/app/sales/pipeline`
- `/app/sales/deals/{id}`

and include a persistent sales agent panel visible across the sales area.

You must align implementation with the existing architecture:
- ASP.NET Core modular monolith
- Blazor Web App frontend
- PostgreSQL-backed tenant-scoped data
- CQRS-lite application layer
- authorization + validation + auditability
- structured API error responses
- design consistency with `design.md` styling standards if present in repo

Do not build mock/demo-only UI. Use real APIs and real tenant data paths end-to-end.

# Scope
In scope:

1. **Sales API completion or hardening**
   - Ensure endpoints exist and are production-usable for:
     - dashboard
     - leads
     - deals
     - activities
     - recommendations
     - qualification
     - conversion
     - stage changes
     - won/lost actions
     - email processing
   - Add or complete:
     - tenant-aware authorization
     - request validation
     - audit logging/business audit events where appropriate
     - structured error responses

2. **Blazor sales dashboard**
   - `/app/sales`
   - Show live tenant data for:
     - pipeline value
     - new leads
     - hot leads
     - deals needing attention
     - forecast revenue
     - agent recommendations
     - recent activity

3. **Blazor leads page**
   - `/app/sales/leads`
   - Show lead list with:
     - source email
     - temperature
     - qualification status
     - confidence score
     - suggested next action
   - Support actions:
     - qualify
     - reject
     - convert to deal

4. **Blazor pipeline page**
   - `/app/sales/pipeline`
   - Production-styled kanban board using real pipeline stages
   - Persist drag-and-drop stage changes through API

5. **Blazor deal detail page**
   - `/app/sales/deals/{id}`
   - Show:
     - deal summary
     - contact and company info
     - email timeline
     - activity timeline
     - agent analysis
     - suggested reply
     - follow-up actions
     - won/lost actions
     - finance document actions

6. **Persistent sales agent panel**
   - Visible throughout sales routes
   - Show:
     - active alerts
     - leads needing review
     - deals needing follow-up
     - links to recommendation execution screens

Out of scope unless required to satisfy acceptance criteria:
- MAUI/mobile work
- unrelated CRM/admin features
- speculative refactors outside sales module boundaries
- replacing existing app-wide layout patterns unless needed for sales area integration

# Files to touch
Inspect the repo first and update the exact files that fit existing patterns. Likely areas include:

## API/backend
- `src/VirtualCompany.Api/**`
  - sales controllers/endpoints
  - request/response contracts
  - authorization policies
  - exception/error mapping
- `src/VirtualCompany.Application/**`
  - sales queries/commands
  - validators
  - DTOs/view models
  - service interfaces/handlers
- `src/VirtualCompany.Domain/**`
  - sales entities/value objects/enums if missing
  - audit event types if needed
- `src/VirtualCompany.Infrastructure/**`
  - repositories/query services
  - EF/db mappings
  - audit persistence
  - tenant-scoped data access

## Web frontend
- `src/VirtualCompany.Web/**`
  - sales pages/components/layouts
  - route components for:
    - dashboard
    - leads
    - pipeline
    - deal detail
  - shared sales agent panel
  - API client/service layer for sales endpoints
  - styling aligned to design system
  - drag-and-drop behavior for kanban

## Shared contracts if used by solution
- `src/VirtualCompany.Shared/**`

## Tests
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint authorization/validation/error response tests
  - sales action tests
- add web/component tests only if project already has a pattern for them

Also inspect for:
- `design.md`
- existing sales-related files
- existing dashboard/kanban/list/detail component patterns
- existing audit/event logging conventions
- existing structured error response model

# Implementation plan
1. **Discover existing implementation before changing anything**
   - Search for existing sales domain/API/UI artifacts:
     - `Sales`
     - `Lead`
     - `Deal`
     - `Pipeline`
     - `Recommendation`
     - `Qualification`
   - Find current route structure in `VirtualCompany.Web`
   - Find current API route conventions in `VirtualCompany.Api`
   - Find tenant resolution, authorization, validation, and audit patterns already used elsewhere
   - Find `design.md` and follow it exactly where applicable
   - Identify whether this task is mostly additive, completion, or hardening

2. **Map acceptance criteria to concrete endpoints and UI screens**
   - Produce a quick internal checklist of required endpoints and pages
   - Confirm or add API contracts for:
     - dashboard summary query
     - leads list query
     - lead qualification action
     - lead rejection action
     - lead conversion action
     - pipeline board query
     - stage change command
     - deal detail query
     - won action
     - lost action
     - activities query
     - recommendations query/action links
     - email processing endpoint or trigger surface
   - Keep contracts tenant-scoped and consistent with CQRS-lite

3. **Complete/harden backend sales APIs**
   - Implement missing endpoints or finish incomplete ones
   - Ensure each endpoint has:
     - authorization checks
     - tenant scoping
     - request validation
     - structured success/error contracts
   - For state-changing actions:
     - qualify lead
     - reject lead
     - convert lead to deal
     - change pipeline stage
     - mark won/lost
     - finance document actions if already modeled
   - Persist business audit events for important actions
   - Return safe, structured errors for:
     - invalid input
     - forbidden access
     - missing records
     - invalid state transitions
   - Reuse existing application-layer handlers and validators where possible

4. **Implement sales dashboard UI at `/app/sales`**
   - Build a production-quality page using live API data
   - Include cards/sections for:
     - pipeline value
     - new leads
     - hot leads
     - deals needing attention
     - forecast revenue
     - agent recommendations
     - recent activity
   - Handle:
     - loading states
     - empty states
     - error states
   - Ensure links drill into leads, pipeline, and deal detail where appropriate
   - Keep styling aligned with design standards and existing app shell

5. **Implement leads page at `/app/sales/leads`**
   - Render a production-styled list/table of leads
   - Include required columns:
     - source email
     - temperature
     - qualification status
     - confidence score
     - suggested next action
   - Add row or detail actions:
     - qualify
     - reject
     - convert to deal
   - Ensure action UX includes:
     - disabled states while submitting
     - optimistic refresh only if safe; otherwise re-fetch after success
     - inline or toast success/error feedback
   - Respect authorization and invalid state handling from API

6. **Implement pipeline page at `/app/sales/pipeline`**
   - Render real kanban columns from live pipeline stages
   - Render deal cards with meaningful summary info
   - Add drag-and-drop stage movement
   - Persist stage changes via API
   - Handle failed stage changes by:
     - reverting UI state
     - showing clear error feedback
   - Ensure board remains usable with many cards and empty columns
   - Keep implementation accessible and keyboard-considerate if feasible within current patterns

7. **Implement deal detail page at `/app/sales/deals/{id}`**
   - Build a production detail screen with sections for:
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
   - Use live APIs only
   - Ensure state-changing actions refresh the page state correctly
   - Show meaningful empty states when some subsections have no data

8. **Implement persistent sales agent panel**
   - Add a reusable component/layout wrapper for sales routes
   - Panel should show:
     - active alerts
     - leads needing review
     - deals needing follow-up
     - links to recommendation execution screens
   - Keep it persistent across sales pages, not duplicated ad hoc per page
   - Ensure responsive behavior works within existing layout system

9. **Align styling with `design.md` and existing design system**
   - Reuse shared components/tokens/classes where available
   - Avoid one-off styling unless necessary
   - Ensure pages look production-ready and consistent:
     - spacing
     - typography
     - cards
     - tables
     - badges
     - timelines
     - action buttons
     - panel layout
   - If `design.md` is missing, follow the strongest existing app patterns instead

10. **Add tests**
   - API tests for:
     - authorized success paths
     - forbidden cross-tenant access
     - validation failures
     - invalid state transitions
     - structured error responses
   - Add tests for key commands:
     - qualify
     - reject
     - convert
     - stage change
     - won/lost
   - If there is an existing component/integration test pattern for web, add focused coverage for critical sales page rendering and action wiring

11. **Keep implementation bounded and production-safe**
   - Do not introduce mock data fallbacks in production paths
   - Do not bypass tenant scoping for convenience
   - Do not put business logic in Blazor pages that belongs in application layer
   - Do not directly access DB from UI or controllers outside established patterns
   - Preserve modular monolith boundaries

# Validation steps
1. **Repo inspection**
   - Locate `design.md` if present
   - Identify existing sales/API/UI patterns before coding

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual API verification**
   - Verify required sales endpoints exist and return structured responses
   - Verify unauthorized or cross-tenant access is rejected correctly
   - Verify validation failures return expected structured errors
   - Verify audit events are created for state-changing actions where applicable

5. **Manual UI verification**
   - Navigate to:
     - `/app/sales`
     - `/app/sales/leads`
     - `/app/sales/pipeline`
     - `/app/sales/deals/{id}`
   - Confirm all pages load with live tenant data
   - Confirm loading, empty, and error states are present and usable
   - Confirm sales agent panel persists across sales routes

6. **Action verification**
   - On leads page:
     - qualify a lead
     - reject a lead
     - convert a lead to a deal
   - On pipeline page:
     - drag a deal to another stage
     - verify persisted change after refresh
   - On deal detail page:
     - mark won/lost
     - trigger follow-up/finance document actions if available in domain
   - Confirm recent activity/recommendations update appropriately where expected

7. **UX/design verification**
   - Confirm styling matches `design.md` or existing design system
   - Confirm no obviously placeholder/demo styling remains
   - Confirm responsive layout is acceptable for common desktop widths

# Risks and follow-ups
- **Risk: partial existing sales implementation**
  - There may already be overlapping endpoints/pages. Prefer completing and standardizing them rather than duplicating.

- **Risk: missing `design.md`**
  - If absent, use established app styling conventions and note this in implementation summary.

- **Risk: drag-and-drop complexity in Blazor**
  - Keep implementation robust and simple; prioritize persistence correctness and graceful failure handling over fancy interactions.

- **Risk: incomplete backend domain support**
  - If finance document actions or email processing are only partially modeled, wire the UI to the real supported actions and complete minimal backend support needed for acceptance criteria.

- **Risk: audit logging gaps**
  - Important state transitions must create business audit records, not just technical logs.

- **Risk: tenant leakage**
  - Be especially careful with all list/detail queries and action endpoints to enforce `company_id` scoping.

Follow-ups to note if not fully achievable within task bounds:
- richer filtering/sorting/search on leads and pipeline
- pagination/virtualization for large datasets
- deeper recommendation execution flows
- broader component/integration test coverage for Blazor UI