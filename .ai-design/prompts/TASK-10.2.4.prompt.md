# Goal
Implement backlog task **TASK-10.2.4 — Failed or blocked workflow steps surface exceptions for review** for story **ST-402 Workflow definitions, instances, and triggers**.

The coding agent should add the minimum complete vertical slice needed so that when a workflow step execution fails or becomes blocked, the system persists a reviewable exception record and exposes it through application/query APIs suitable for web UI and future inbox/alert integration.

This work must align with the existing architecture:
- modular monolith
- ASP.NET Core + application layer + domain layer + infrastructure layer
- PostgreSQL-backed persistence
- tenant-scoped workflow execution
- auditability as a business feature, not just logging

Because explicit acceptance criteria were not provided for the task, derive implementation behavior from:
- ST-402: “Failed or blocked workflow steps surface exceptions for review.”
- ST-404: blocked or failed executions create visible exceptions/escalations
- architecture guidance around workflow engine, auditability, and tenant isolation

# Scope
Include:
- domain model support for workflow execution exceptions/review items
- persistence schema and EF Core mapping for workflow exceptions
- application-layer creation of exception records when workflow steps fail or are blocked
- query support to list and inspect workflow exceptions by company/workflow instance
- API endpoints or existing feature handlers needed to retrieve exceptions for review
- tenant scoping and safe status transitions
- tests covering creation and retrieval behavior

Do not include unless already trivially adjacent in the codebase:
- full notification fan-out
- full inbox UX
- mobile changes
- broad workflow designer changes
- unrelated approval/escalation orchestration
- speculative generic incident management abstractions

Prefer a focused implementation that can later feed:
- dashboard alerts
- approval inbox
- workflow review screens
- audit views

# Files to touch
Inspect the solution first and then update the appropriate files in these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- possibly `src/VirtualCompany.Web` only if a minimal review page already exists and is clearly the established pattern

Likely file categories to add or modify:
- Domain
  - workflow aggregate/entity files
  - enums/value objects for workflow state / exception status / exception type
  - domain methods for recording step failure/block conditions
- Application
  - commands/handlers used by workflow runner
  - queries/handlers for exception review
  - DTOs/view models
  - validators
- Infrastructure
  - EF Core entity configurations
  - DbContext
  - migrations
  - repositories if used
- API
  - controller/endpoints for listing and getting workflow exceptions
- Tests
  - domain tests
  - application handler tests
  - API/integration tests if the repo already uses them

If the repository already has workflow-related files, extend those instead of introducing parallel patterns.

# Implementation plan
1. **Discover existing workflow implementation**
   - Find current workflow definition, workflow instance, and workflow runner code paths.
   - Identify where step execution outcomes are currently represented:
     - success
     - failed
     - blocked
     - retryable/transient failure
   - Identify whether there is already an exception/escalation/audit concept that should be reused rather than duplicated.

2. **Design the workflow exception model**
   - Introduce a tenant-scoped business entity for reviewable workflow exceptions, likely something like:
     - `WorkflowException`
   - Recommended fields:
     - `Id`
     - `CompanyId`
     - `WorkflowInstanceId`
     - `WorkflowDefinitionId` if convenient/available
     - `StepKey` or `StepName`
     - `ExceptionType` (`Failed`, `Blocked`)
     - `Status` (`Open`, `Reviewed`, `Resolved`, maybe `Dismissed` only if needed)
     - `Title`
     - `Details` / `Reason`
     - `ErrorCode` nullable
     - `TechnicalDetailsJson` or structured payload nullable
     - `OccurredAt`
     - `ReviewedAt` nullable
     - `ReviewedByUserId` nullable
     - `ResolutionNotes` nullable
     - correlation/reference fields if the codebase already uses them
   - Keep it business-facing and review-oriented. Do not store raw stack traces as user-facing text. If technical details are stored, keep them structured and safe.

3. **Add persistence**
   - Add EF Core entity/configuration and DbSet.
   - Create a migration for the new table.
   - Ensure indexes support likely queries:
     - by `CompanyId`
     - by `WorkflowInstanceId`
     - by `Status`
     - by `OccurredAt`
   - Ensure foreign keys are consistent with existing workflow tables.

4. **Hook exception creation into workflow execution**
   - Update the workflow execution path so that:
     - when a step enters a failed state, an open workflow exception is created
     - when a step enters a blocked state, an open workflow exception is created
   - Avoid duplicate open exceptions for the same workflow instance + step + exception type unless duplicates are already the established pattern.
   - If retries occur, decide conservatively:
     - create exception only when the workflow/step is actually marked failed or blocked
     - not for every transient retry attempt
   - If the workflow instance already stores `state` and `current_step`, ensure those remain authoritative and exception records are supplemental for review.

5. **Add application queries for review**
   - Implement query handlers to support:
     - list workflow exceptions for the current company
     - filter by status and optionally workflow instance
     - get workflow exception detail by id
   - Return review-friendly DTOs including:
     - workflow instance id
     - workflow definition/name if available
     - step key/name
     - exception type
     - status
     - reason/details
     - occurred timestamp
   - Keep queries tenant-scoped.

6. **Add review action if low effort and consistent**
   - If the existing application architecture already supports command handlers cleanly, add a minimal review command:
     - mark exception as reviewed/resolved
   - Only do this if it fits naturally and does not expand scope too much.
   - If added, require tenant scoping and user identity for reviewer attribution.

7. **Expose API endpoints**
   - Add endpoints consistent with existing API conventions, for example:
     - `GET /api/workflows/exceptions`
     - `GET /api/workflows/exceptions/{id}`
     - optionally `POST /api/workflows/exceptions/{id}/review`
   - Ensure company context enforcement matches the rest of the app.
   - Return safe business data only.

8. **Audit integration**
   - If the codebase already writes business audit events for workflow state changes, add audit events for:
     - workflow exception created
     - workflow exception reviewed/resolved
   - Reuse existing audit infrastructure rather than inventing a new one.

9. **Testing**
   - Add tests for:
     - failed step creates open exception
     - blocked step creates open exception
     - duplicate exception creation is prevented if the same open condition is reprocessed
     - list query returns only current tenant’s exceptions
     - detail query forbids or hides cross-tenant access
     - review command updates status correctly if implemented

10. **Keep implementation incremental**
   - Prefer a narrow, production-usable slice over a broad but incomplete framework.
   - Match naming and layering already present in the repository.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation:
   - run targeted tests for domain/application/infrastructure changes
   - then run full suite:
     - `dotnet test`

4. Validate migration generation/application if migrations are used in-repo:
   - ensure the new workflow exception table is included
   - verify schema names and FK/index conventions match existing patterns

5. Manually verify behavior through API or handler tests:
   - create or simulate a workflow instance
   - force a step into `Failed`
   - confirm a workflow exception record is persisted
   - force a step into `Blocked`
   - confirm a workflow exception record is persisted
   - query exception list and confirm returned item includes workflow/step context
   - verify another tenant cannot access the record

6. If review action is implemented:
   - mark an exception reviewed/resolved
   - confirm reviewer and timestamp are persisted
   - confirm list/detail reflects updated status

7. Confirm no regression in workflow instance persistence:
   - workflow state/current step still update correctly
   - exception creation does not break retries or normal completion paths

# Risks and follow-ups
- **Risk: duplicate concepts already exist**
  - The repo may already have alerts, escalations, or audit entities that overlap. Reuse existing concepts where appropriate instead of creating a conflicting model.

- **Risk: workflow runner semantics may be immature**
  - If failed vs blocked is not clearly modeled yet, define the smallest explicit distinction needed and document assumptions in code comments/tests.

- **Risk: transient retry noise**
  - Do not create review exceptions for every transient execution error unless the workflow step is actually persisted as failed/blocked.

- **Risk: tenant leakage**
  - All queries and commands must enforce `CompanyId` scoping consistently.

- **Risk: exposing technical internals**
  - Keep user-facing exception details concise and operational. Avoid raw stack traces in API responses.

Follow-up items after this task:
- surface workflow exceptions in dashboard/inbox UI
- add notification generation for new open workflow exceptions
- support richer exception resolution workflows and escalation assignment
- link exceptions more deeply into audit/explainability views
- add metrics/reporting for workflow exception rates by definition and step