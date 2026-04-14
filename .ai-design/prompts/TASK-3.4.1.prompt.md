# Goal
Implement backlog task **TASK-3.4.1 — insight scoring and prioritization service for actionable items** for story **US-3.4 Action-oriented insights and deep links to operational work**.

Deliver a tenant-aware backend and dashboard-facing query flow that produces a **prioritized action queue** of actionable insights across:
- approvals
- risks
- blocked workflows
- opportunities

Each insight must include:
- priority
- reason
- owner
- due time or SLA state
- deep link to the target task, workflow, or approval

Also implement per-user acknowledgment persistence so a user can mark an insight as acknowledged and continue to see that state after refresh.

The implementation must satisfy these acceptance criteria:
1. Dashboard displays a prioritized action queue containing approvals, risks, blocked workflows, and opportunities.
2. Each action item includes priority, reason, owner, due time or SLA state, and a deep link to the target task, workflow, or approval.
3. Priority ordering is consistent with configured scoring rules and remains stable for identical scores.
4. Users can mark an insight as acknowledged, and the acknowledgment state persists across refreshes for that user.
5. Automated tests verify action scoring, deep-link generation, and acknowledgment persistence.

Use the existing architecture:
- modular monolith
- ASP.NET Core backend
- PostgreSQL primary store
- CQRS-lite application layer
- tenant-scoped access
- Blazor web dashboard
- tests in existing test projects

Return production-ready code only. Do not leave TODO placeholders.

# Scope
In scope:
- Add domain/application/infrastructure/web support for actionable insight generation
- Implement a scoring/prioritization service with deterministic stable ordering
- Support insight source types:
  - approval
  - blocked workflow
  - risk
  - opportunity
- Add persistence for per-user acknowledgment state
- Add deep-link generation for target entities
- Expose a query/API endpoint or existing dashboard data path for retrieving the action queue
- Add a command/API endpoint for acknowledging an insight
- Add automated tests for:
  - scoring behavior
  - stable ordering for equal scores
  - deep-link generation
  - acknowledgment persistence

Out of scope unless required by existing patterns:
- Full mobile implementation
- New notification fan-out
- Large dashboard redesign
- New external integrations
- Complex ML/LLM scoring; use deterministic rule-based scoring
- Broad audit UI changes beyond what is needed for this task

Implementation constraints:
- Keep all data tenant-scoped by `company_id`
- Keep user acknowledgment scoped by both `company_id` and `user_id`
- Prefer clean architecture boundaries:
  - Domain: entities/value objects/rules
  - Application: queries/commands/services/contracts
  - Infrastructure: EF Core/repositories/query handlers/persistence
  - Web/API: endpoints and Blazor consumption
- Follow existing project conventions and naming patterns already present in the repo
- If similar dashboard/query patterns already exist, extend them instead of inventing parallel infrastructure

# Files to touch
Inspect the solution first and then modify the appropriate files in these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or update:

## Domain
- Insight/action queue domain models, enums, and scoring abstractions
- User acknowledgment entity if persistence model belongs in domain
- Any value objects for:
  - insight type
  - priority level
  - SLA state
  - deep-link target type

Possible examples:
- `src/VirtualCompany.Domain/.../ActionInsight.cs`
- `src/VirtualCompany.Domain/.../ActionInsightType.cs`
- `src/VirtualCompany.Domain/.../InsightAcknowledgment.cs`

## Application
- Query to fetch prioritized action queue
- Command to acknowledge an insight
- DTOs/view models for dashboard action items
- Scoring service interface and implementation if application-owned
- Deep-link builder abstraction
- Validation and authorization hooks

Possible examples:
- `src/VirtualCompany.Application/.../Queries/GetActionQueueQuery.cs`
- `src/VirtualCompany.Application/.../Commands/AcknowledgeInsightCommand.cs`
- `src/VirtualCompany.Application/.../Dtos/ActionQueueItemDto.cs`
- `src/VirtualCompany.Application/.../Services/IInsightScoringService.cs`
- `src/VirtualCompany.Application/.../Services/IInsightDeepLinkBuilder.cs`

## Infrastructure
- EF Core entity configuration and persistence
- Repository/query implementation joining approvals/tasks/workflows/risk/opportunity sources
- Migration for acknowledgment persistence and any supporting tables
- Concrete deep-link builder if route generation is infrastructure/web-aware
- Query handlers and data access

Possible examples:
- `src/VirtualCompany.Infrastructure/.../Configurations/InsightAcknowledgmentConfiguration.cs`
- `src/VirtualCompany.Infrastructure/.../Queries/GetActionQueueQueryHandler.cs`
- `src/VirtualCompany.Infrastructure/.../Services/InsightScoringService.cs`
- `src/VirtualCompany.Infrastructure/.../Migrations/...`

## API / Web
- Endpoint/controller/minimal API for:
  - get action queue
  - acknowledge insight
- Dashboard integration to render the queue
- UI action to acknowledge an insight
- Deep links wired to existing routes

Possible examples:
- `src/VirtualCompany.Api/.../DashboardEndpoints.cs`
- `src/VirtualCompany.Api/.../InsightEndpoints.cs`
- `src/VirtualCompany.Web/.../Pages/Dashboard.razor`
- `src/VirtualCompany.Web/.../Components/ActionQueue.razor`

## Tests
- Unit tests for scoring and ordering
- Integration/API tests for queue retrieval and acknowledgment persistence
- Deep-link generation tests

Possible examples:
- `tests/VirtualCompany.Api.Tests/.../InsightScoringTests.cs`
- `tests/VirtualCompany.Api.Tests/.../ActionQueueEndpointTests.cs`
- `tests/VirtualCompany.Api.Tests/.../InsightAcknowledgmentTests.cs`

Do not assume these exact files exist; discover the actual structure first and fit into it.

# Implementation plan
1. **Inspect existing architecture and patterns**
   - Review how the solution currently handles:
     - tenant context
     - authenticated user context
     - CQRS queries/commands
     - dashboard data loading
     - EF Core persistence and migrations
     - route generation for tasks/workflows/approvals
   - Reuse existing abstractions for current user, company context, and authorization.

2. **Model actionable insights**
   - Introduce a normalized insight model that can represent all required queue items regardless of source.
   - Include at minimum:
     - `InsightId` or deterministic composite key
     - `CompanyId`
     - `InsightType` (`Approval`, `Risk`, `BlockedWorkflow`, `Opportunity`)
     - `SourceEntityType`
     - `SourceEntityId`
     - `Title` or concise label if needed by UI
     - `Reason`
     - `Owner`
     - `DueAt` and/or `SlaState`
     - `PriorityScore`
     - `PriorityLabel`
     - `DeepLink`
     - `IsAcknowledged`
     - `AcknowledgedAt`
   - Use a deterministic insight identity. Prefer a stable key derived from source type + source id + company id so acknowledgment can persist reliably.

3. **Implement acknowledgment persistence**
   - Add a persistence model/table for user-specific acknowledgment state.
   - Required fields:
     - `id`
     - `company_id`
     - `user_id`
     - `insight_key`
     - `acknowledged_at`
     - timestamps if consistent with project style
   - Add a unique constraint on `(company_id, user_id, insight_key)`.
   - Ensure acknowledgment is idempotent:
     - acknowledging an already acknowledged insight should not create duplicates
     - command should succeed safely
   - Add EF configuration and migration.

4. **Define scoring rules**
   - Implement deterministic rule-based scoring with explicit weighted factors.
   - Use configuration if there is already a settings/options pattern; otherwise implement a clear default ruleset in code with easy extension.
   - Suggested scoring dimensions:
     - source type base weight
     - overdue / SLA breached
     - due soon
     - blocked duration
     - risk severity if available
     - approval age / urgency
     - opportunity time sensitivity if available
   - Produce:
     - numeric score for sorting
     - derived priority label such as `Critical`, `High`, `Medium`, `Low`
   - Stable ordering requirement:
     - sort by descending score
     - then deterministic tie-breakers, e.g.:
       1. due time ascending with nulls last
       2. created time ascending or source created time ascending
       3. insight key ascending
     - identical scores must always return in the same order

5. **Source actionable items from existing operational entities**
   - Build a query service that gathers candidate insights from existing domain tables/entities.
   - At minimum support:
     - pending approvals from `approvals`
     - blocked workflows from `workflow_instances` and/or blocked tasks
     - risks from existing alert/exception/failure/task states if a dedicated risk entity does not exist
     - opportunities from existing task/workflow/analytics signals if available
   - If dedicated risk/opportunity entities do not yet exist, derive them from current operational data in a pragmatic way that still satisfies the acceptance criteria.
   - Keep derivation logic explicit and testable.
   - Ensure all source queries are tenant-scoped.

6. **Generate deep links**
   - Implement a deep-link builder that maps source entities to existing web routes.
   - Required targets:
     - task
     - workflow
     - approval
   - Return app-relative links consistent with current Blazor routing.
   - Examples:
     - approval -> `/approvals/{id}`
     - workflow -> `/workflows/{id}`
     - task -> `/tasks/{id}`
   - If route conventions differ in the repo, use actual existing routes.
   - Ensure every returned insight has a valid deep link to the most relevant target entity.

7. **Implement application query and command**
   - Add a query such as `GetActionQueueQuery(companyId, userId)` returning prioritized items.
   - Add a command such as `AcknowledgeInsightCommand(companyId, userId, insightKey)`.
   - Query behavior:
     - gather candidates
     - score them
     - sort deterministically
     - join acknowledgment state for current user
     - map to DTOs
   - Command behavior:
     - validate tenant/user context
     - upsert acknowledgment
     - persist changes
   - Keep handlers thin and delegate scoring/deep-link logic to services.

8. **Expose API/web endpoints**
   - Add or extend endpoints for:
     - `GET` action queue
     - `POST` or `PUT` acknowledge insight
   - Use existing auth and tenant resolution patterns.
   - Return only tenant/user-scoped data.
   - If the dashboard already loads via server-side application services instead of API, integrate through the existing path rather than duplicating endpoints.

9. **Integrate dashboard UI**
   - Update the dashboard to display the prioritized action queue.
   - Each item should visibly show:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep-link action
     - acknowledgment state/action
   - Add acknowledge interaction that updates persisted state and survives refresh.
   - Preserve current dashboard patterns and styling.

10. **Add automated tests**
   - Unit tests for scoring:
     - higher urgency yields higher score
     - overdue/SLA breach outranks non-urgent items
     - stable ordering for equal scores using deterministic tie-breakers
   - Unit tests for deep-link generation:
     - approval/task/workflow map to correct routes
   - Integration tests for acknowledgment:
     - acknowledge insight
     - refresh/re-query
     - same user sees acknowledged state
     - different user does not inherit acknowledgment
     - different tenant does not inherit acknowledgment
   - Add endpoint/query tests verifying queue contains required fields.

11. **Keep implementation production-safe**
   - Avoid N+1 queries when building the queue
   - Keep sorting in a deterministic and testable location
   - Handle missing optional owner/due data gracefully
   - Ensure null-safe mapping for SLA state and due time
   - Keep code formatted and consistent with repository conventions

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Specifically verify behavior manually or through tests:
   - Dashboard/action queue returns approvals, risks, blocked workflows, and opportunities when seeded
   - Each item includes:
     - priority
     - reason
     - owner
     - due time or SLA state
     - deep link
   - Ordering is deterministic for equal scores across repeated calls
   - Acknowledging an insight persists for the same user after refresh/reload
   - Acknowledgment does not leak across users or tenants
   - Deep links resolve to existing task/workflow/approval routes

4. If migrations are used in the repo, ensure:
   - migration is added correctly
   - app starts with updated schema
   - unique constraint for acknowledgment works as expected

5. Confirm no acceptance criterion is missed before finishing.

# Risks and follow-ups
- **Risk: no existing risk/opportunity entities**
  - Mitigation: derive these insight types from existing operational signals already present in tasks/workflows/approvals/alerts.
  - Document derivation in code comments or concise developer notes if needed.

- **Risk: route conventions may differ from assumptions**
  - Mitigation: inspect actual Blazor routes before implementing deep-link generation.

- **Risk: unstable ordering if relying on database default ordering**
  - Mitigation: enforce explicit deterministic ordering in application/query logic with tie-breakers.

- **Risk: acknowledgment identity mismatch**
  - Mitigation: use a stable deterministic `insight_key` derived from source type + source id + company id, not transient row order.

- **Risk: dashboard data path may already have an aggregate query**
  - Mitigation: extend the existing dashboard aggregate/query model instead of adding a disconnected endpoint.

- **Risk: performance degradation from multi-source aggregation**
  - Mitigation: keep candidate queries bounded and tenant-filtered; project