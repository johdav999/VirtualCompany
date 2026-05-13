# Goal
Implement backlog task **TASK-34.3.1** by delivering the backend contract for tenant-authorized sales operations in the existing .NET solution. Add REST API endpoints for sales dashboard, leads, deals, activities, recommendations, qualification, conversion, stage changes, won/lost actions, and email processing with:

- strict tenant-aware authorization
- request/response validation
- business audit logging
- structured error responses
- integration tests covering happy path, validation failures, and tenant isolation

This task is backend-focused and should complete the API contract needed by the `/app/sales`, `/app/sales/leads`, `/app/sales/pipeline`, `/app/sales/deals/{id}`, and persistent sales agent panel UI surfaces to consume live tenant data.

# Scope
In scope:

- Add or complete a **Sales module** across API, Application, Domain, and Infrastructure layers.
- Expose tenant-scoped REST endpoints for:
  - dashboard summary
  - leads list/detail
  - deals list/detail
  - activities
  - recommendations
  - qualification action
  - reject lead action
  - convert lead to deal action
  - pipeline stage change action
  - won action
  - lost action
  - email processing action or ingestion endpoint
- Enforce company/tenant scoping on every query and command.
- Validate request payloads and return consistent structured API errors.
- Persist audit events for important sales actions and denied/failed attempts where appropriate.
- Add integration tests in `tests/VirtualCompany.Api.Tests` for:
  - authorized access within tenant
  - forbidden/not found cross-tenant access
  - validation errors
  - successful state transitions and audit creation
- Reuse existing architectural patterns already present in the repo where possible.

Out of scope unless required by existing code structure:

- Full Blazor UI implementation for the listed routes
- Drag-and-drop frontend behavior
- MAUI/mobile changes
- speculative CRM integrations beyond the API contract
- broad refactors unrelated to sales APIs

If sales entities already exist, extend them rather than replacing them. If they do not exist, implement the minimum domain model and persistence needed to satisfy the contract and tests.

# Files to touch
Inspect first, then update the most relevant files in these areas:

- `src/VirtualCompany.Api/**`
  - sales controllers/endpoints
  - auth/policy wiring
  - validation/error mapping middleware or filters
  - DI registration
- `src/VirtualCompany.Application/**`
  - sales commands/queries
  - DTOs/contracts
  - validators
  - handlers/services
- `src/VirtualCompany.Domain/**`
  - sales entities/value objects/enums
  - domain rules for qualification, conversion, stage changes, won/lost
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/repositories
  - tenant-aware query implementations
  - audit persistence
  - migrations if this repo uses code-first migrations here
- `src/VirtualCompany.Shared/**`
  - shared contracts/error envelope types if applicable
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for all new endpoints and behaviors

Also inspect these project files for references and conventions:

- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

Check for existing patterns in:

- auth/tenant resolution
- audit event creation
- exception-to-problem-details mapping
- MediatR/CQRS handlers
- FluentValidation or equivalent
- EF Core entity configuration and test fixtures

# Implementation plan
1. **Discover existing sales and platform patterns**
   - Search the solution for:
     - tenant resolution/current company context
     - authorization policies
     - audit event model and persistence
     - structured error response format
     - existing dashboard/task/workflow endpoint conventions
   - Follow existing naming and layering conventions exactly.
   - Do not introduce a parallel architecture.

2. **Define/confirm sales domain model**
   Implement only what is necessary for the API contract. Prefer existing entities if present. Likely minimum concepts:
   - `SalesLead`
   - `SalesDeal`
   - `SalesActivity`
   - `SalesRecommendation`
   - `SalesPipelineStage`
   - optional `SalesEmail` or email-processing command payload
   - enums/statuses such as:
     - lead temperature
     - qualification status
     - deal stage
     - deal outcome
     - activity type
   Ensure every tenant-owned entity includes `company_id`/tenant ownership.

3. **Design backend API contract**
   Add RESTful endpoints under a consistent route prefix such as `/api/sales` if no existing convention overrides it. Include endpoints equivalent to:

   - `GET /api/sales/dashboard`
   - `GET /api/sales/leads`
   - `GET /api/sales/leads/{id}`
   - `POST /api/sales/leads/{id}/qualify`
   - `POST /api/sales/leads/{id}/reject`
   - `POST /api/sales/leads/{id}/convert`
   - `GET /api/sales/pipeline`
   - `POST /api/sales/deals/{id}/stage-change`
   - `GET /api/sales/deals/{id}`
   - `GET /api/sales/deals/{id}/activities` if not embedded in detail
   - `GET /api/sales/recommendations`
   - `POST /api/sales/deals/{id}/mark-won`
   - `POST /api/sales/deals/{id}/mark-lost`
   - `POST /api/sales/email/process`

   Shape responses so the UI can render the acceptance-criteria views, including:
   - dashboard metrics:
     - pipeline value
     - new leads
     - hot leads
     - deals needing attention
     - forecast revenue
     - agent recommendations
     - recent activity
   - leads list fields:
     - source email
     - temperature
     - qualification status
     - confidence score
     - suggested next action
   - pipeline board grouped by real stages
   - deal detail fields:
     - summary
     - contact/company info
     - email timeline
     - activity timeline
     - agent analysis
     - suggested reply
     - follow-up actions
     - won/lost and finance document action affordances
   - agent panel fields:
     - active alerts
     - leads needing review
     - deals needing follow-up
     - recommendation links or action metadata

4. **Implement application-layer CQRS**
   Create queries/commands and handlers for each endpoint/action. Keep reads and writes separate.
   Examples:
   - `GetSalesDashboardQuery`
   - `GetSalesLeadsQuery`
   - `GetSalesLeadByIdQuery`
   - `QualifyLeadCommand`
   - `RejectLeadCommand`
   - `ConvertLeadToDealCommand`
   - `GetSalesPipelineQuery`
   - `ChangeDealStageCommand`
   - `GetDealDetailQuery`
   - `GetSalesRecommendationsQuery`
   - `MarkDealWonCommand`
   - `MarkDealLostCommand`
   - `ProcessSalesEmailCommand`

   Each handler must:
   - resolve current tenant/company context
   - query/update only tenant-owned records
   - enforce business rules
   - emit audit events for meaningful actions

5. **Add request validation**
   Use the repo’s existing validation approach. Validate:
   - required IDs and payload fields
   - allowed enum/status values
   - stage transitions
   - won/lost preconditions
   - qualification/conversion preconditions
   - email processing payload shape
   Return field-level validation errors in the project’s structured error format.

6. **Enforce authorization and tenant isolation**
   - Require authenticated access on all sales endpoints.
   - Apply existing policy-based authorization if available.
   - Ensure cross-tenant entity access returns the project-standard forbidden/not-found behavior from ST-101.
   - Never query by entity ID without tenant filter.
   - Prefer repository/query methods that require `companyId`.

7. **Implement audit logging as a domain feature**
   For at least these actions, create `audit_events` records using the existing audit subsystem:
   - lead qualified
   - lead rejected
   - lead converted to deal
   - deal stage changed
   - deal marked won
   - deal marked lost
   - email processed
   - optionally denied/invalid action attempts if consistent with existing patterns

   Include:
   - actor type/id
   - company/tenant
   - action
   - target type/id
   - outcome
   - concise rationale/summary
   - source references if available

8. **Structured error responses**
   Reuse existing exception handling and problem-details conventions. Ensure:
   - validation failures return 400 with field details
   - unauthorized/forbidden are consistent
   - missing tenant-scoped resources return 404 or project-standard safe response
   - business rule violations return consistent structured errors
   - unhandled exceptions remain safe and non-leaky

9. **Persistence and mapping**
   If sales tables/configurations do not exist:
   - add EF entities/configurations
   - add migration if this repo uses migrations in source control
   - seed minimal test data only where needed for integration tests

   Keep schema aligned with architecture guidance:
   - shared-schema multi-tenancy
   - `company_id` on tenant-owned tables
   - audit events persisted separately from technical logs

10. **Integration tests**
    Add API integration tests covering:
    - dashboard returns only current tenant data
    - leads list returns expected fields
    - qualify lead succeeds and writes audit event
    - reject lead succeeds and writes audit event
    - convert lead to deal succeeds and returns/creates deal
    - stage change persists and is reflected in pipeline/deal detail
    - mark won/lost succeeds with valid preconditions
    - invalid payloads return structured validation errors
    - cross-tenant access to lead/deal returns forbidden/not-found per convention
    - email processing endpoint validates and persists expected effects
    - recommendations and recent activity are tenant-scoped

    Prefer end-to-end API tests using the existing test host and database setup rather than mocking controllers.

11. **Keep UI contract in mind**
    Even though this task is backend-only, ensure response DTOs are practical for the listed routes:
    - `/app/sales`
    - `/app/sales/leads`
    - `/app/sales/pipeline`
    - `/app/sales/deals/{id}`
    - persistent sales agent panel

    Avoid under-shaped responses that would force immediate follow-up backend work.

12. **Document assumptions in code comments only where necessary**
    Do not add broad documentation files unless the repo already keeps API docs in code or markdown nearby.

# Validation steps
Run and report the results of the relevant commands:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted API test project flow, also run:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. Manually verify, via integration tests or existing test helpers, that:
   - tenant A cannot read or mutate tenant B sales data
   - validation errors are structured and field-specific
   - audit events are created for sales mutations
   - stage changes persist and are visible in subsequent reads
   - dashboard aggregates are based on live tenant data, not hardcoded placeholders

5. In your final implementation summary, include:
   - endpoints added/updated
   - commands/queries/validators added
   - entities/migrations added
   - tests added
   - any assumptions or gaps that need follow-up

# Risks and follow-ups
- The repo may not yet contain a sales domain model; if so, implement the thinnest viable model that satisfies the contract without overbuilding.
- The exact structured error format may already exist; reuse it rather than inventing a new envelope.
- Tenant resolution/auth may be partially implemented; align with existing ST-101 patterns and do not bypass them in tests.
- Audit logging may already have helper services; use them to avoid inconsistent business audit records.
- If acceptance criteria mention UI-only fields like “production-styled kanban” or “persistent panel,” satisfy the backend data contract that enables those views, not the UI itself.
- If finance document actions are not yet implemented, expose placeholder action metadata in deal detail only if consistent with current architecture; do not invent unsupported workflows.
- If email processing depends on future integration adapters, implement an internal command/endpoint contract and persistence/audit behavior that can later be wired to real inbox processing.
- Follow-up likely needed after this task: connect Blazor routes to these live endpoints and complete UI interactions for qualify/reject/convert, kanban drag-drop, deal detail actions, and agent panel execution flows.