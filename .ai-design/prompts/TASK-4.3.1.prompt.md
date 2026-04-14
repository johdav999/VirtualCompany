# Goal
Implement `TASK-4.3.1` for **US-4.3 Link briefing content to tasks, approvals, workflows, and priorities** by adding structured priority scoring, deterministic section ordering, resilient linked-entity references, and summary counts to briefing generation and briefing API responses.

The implementation must ensure:
- briefing sections that reference operational work include resolvable links/identifiers to related `task`, `workflow_instance`, or `approval` records
- section priority is derived from **persisted severity rules**, not inferred from prose
- briefing sections are returned in priority order with alerts/risks/escalations before informational updates
- deleted/inaccessible linked entities produce a stable placeholder state instead of API failure
- structured output includes counts for:
  - critical alerts
  - open approvals
  - blocked workflows
  - overdue tasks

# Scope
Work only on the code required for this backlog item inside the existing .NET solution and architecture.

Include:
- domain/application models for briefing section priority and linked entity references
- persisted severity rule support if not already present in a suitable store
- briefing assembly logic updates to compute structured priority scores from persisted rules
- deterministic ordering of briefing sections by priority
- API contract updates for structured briefing output
- placeholder handling for missing/inaccessible linked entities
- tests covering ordering, counts, and placeholder behavior

Do not include:
- unrelated dashboard redesign
- mobile-specific UI work
- broad notification refactors
- narrative-text parsing for priority derivation
- speculative architecture changes beyond what is needed for this task

# Files to touch
Inspect the repository first and then update the most relevant files in these areas:

- `src/VirtualCompany.Domain/**`
  - briefing/domain models
  - severity/priority rule entities or value objects
  - linked entity reference models
- `src/VirtualCompany.Application/**`
  - briefing query/handler/services
  - DTOs/contracts for structured briefing output
  - ordering/scoring logic
  - placeholder resolution logic
- `src/VirtualCompany.Infrastructure/**`
  - persistence for severity rules
  - repositories/query projections
  - EF Core configurations/migrations if applicable
- `src/VirtualCompany.Api/**`
  - briefing endpoints/contracts/response mapping
- `src/VirtualCompany.Shared/**`
  - shared enums/contracts if API models live here
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for briefing responses
- potentially:
  - `tests/**` application/unit test projects if present for handlers/services

Before editing, locate the current implementation for:
- daily briefings / weekly summaries
- message or notification structured payloads
- task/workflow/approval query models
- any existing severity, alert, escalation, or KPI summary logic

# Implementation plan
1. **Discover current briefing flow**
   - Find where daily/weekly briefings are generated, stored, and returned.
   - Identify whether briefings are:
     - persisted as `messages`
     - generated on demand
     - backed by a dedicated DTO/view model
   - Trace the path from data aggregation -> structured model -> API response.

2. **Define structured priority model**
   - Introduce explicit structured fields for briefing sections, such as:
     - `sectionType`
     - `priorityCategory` (e.g. `critical`, `high`, `medium`, `informational`)
     - `priorityScore` (numeric, sortable)
     - `priorityRuleCode` or `severityRuleId`
   - Ensure priority can be validated from structured output alone, without reading narrative text.
   - Prefer enums/value objects over free-form strings where the codebase already uses them.

3. **Add linked operational reference model**
   - For each briefing section that references operational work, include structured references like:
     - `entityType` = `task | workflow_instance | approval`
     - `entityId`
     - `displayLabel`
     - `state`
     - `isAccessible`
     - `placeholderReason` or stable placeholder status
   - Support multiple references if a section can point to more than one operational item.
   - Keep the placeholder shape stable so clients can render it safely.

4. **Implement persisted severity rules**
   - Reuse an existing persisted configuration source if one already exists and is appropriate.
   - If none exists, add a minimal persisted severity rule model in the correct module/persistence layer.
   - Rules should map structured conditions to priority outcomes, for example:
     - blocked workflow -> high/critical
     - overdue task -> high
     - pending approval -> medium/high depending on age or threshold
     - informational update -> low/informational
   - Keep rules tenant-aware if the surrounding configuration model is tenant-scoped.
   - Seed sensible defaults if required for the feature to function.

5. **Compute priority from structured facts**
   - Update briefing aggregation so each section is built from structured facts first.
   - Derive `priorityScore` and `priorityCategory` from persisted rules, not from generated text.
   - Preserve a deterministic sort order, for example:
     1. descending `priorityScore`
     2. category precedence
     3. newest/most urgent timestamp
     4. stable tie-breaker such as section key
   - Make the ordering logic explicit and testable.

6. **Add summary counts to structured output**
   - Extend the briefing response payload to include:
     - `criticalAlertsCount`
     - `openApprovalsCount`
     - `blockedWorkflowsCount`
     - `overdueTasksCount`
   - Compute these from the same structured source used to build sections to avoid drift.
   - Ensure counts are tenant-scoped and consistent with authorization.

7. **Handle deleted/inaccessible linked entities safely**
   - When a referenced task/workflow/approval no longer exists or is not accessible:
     - do not throw
     - do not omit the section if the briefing item itself is still valid
     - return a stable placeholder reference state
   - Example placeholder behavior:
     - `state = "unavailable"`
     - `isAccessible = false`
     - `displayLabel = "Unavailable task"` / `"Unavailable workflow"` / `"Unavailable approval"`
     - `placeholderReason = "deleted_or_inaccessible"`
   - Keep this behavior consistent across all linked entity types.

8. **Update API contracts and mapping**
   - Expose the new structured fields in the briefing API response.
   - Maintain backward compatibility where practical; if breaking changes are unavoidable, keep them minimal and localized.
   - Ensure serialization is stable and documented through DTO naming and field usage.

9. **Add tests**
   - Unit/application tests for:
     - priority derivation from persisted rules
     - deterministic section ordering
     - summary count calculation
     - placeholder generation for missing/inaccessible entities
   - API/integration tests for:
     - briefing response includes structured references
     - alerts/risks/escalations sort before informational sections
     - counts are present and correct
     - missing linked entities do not fail the endpoint

10. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - domain models in Domain
     - orchestration/query logic in Application
     - persistence in Infrastructure
     - transport contracts in Api/Shared
   - Keep CQRS-lite patterns already used in the solution.
   - Avoid putting business logic in controllers/endpoints.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update targeted automated coverage for these scenarios:
   - briefing with mixed section severities returns sections ordered by structured priority
   - briefing section referencing a task includes resolvable identifier/link metadata
   - briefing section referencing a workflow includes resolvable identifier/link metadata
   - briefing section referencing an approval includes resolvable identifier/link metadata
   - deleted task/workflow/approval returns placeholder reference state instead of error
   - structured output includes:
     - `criticalAlertsCount`
     - `openApprovalsCount`
     - `blockedWorkflowsCount`
     - `overdueTasksCount`
   - priority is verifiable from structured fields without inspecting narrative text

4. If EF Core migrations are used in this repo:
   - generate/apply the migration for any new persisted severity rule schema
   - verify the app starts and briefing endpoints still serialize correctly

5. Manually inspect one representative briefing API response and confirm:
   - sections are sorted correctly
   - each operational section has structured references
   - placeholder references are stable
   - counts match the included data

# Risks and follow-ups
- **Risk: no existing briefing module abstraction**
  - You may need to first identify whether briefings are embedded in messages/notifications or a dedicated feature. Keep refactoring minimal and task-focused.

- **Risk: persisted severity rules are not modeled yet**
  - Add the smallest viable persisted rule model and seed defaults rather than inventing a large rules engine.

- **Risk: authorization vs deletion ambiguity**
  - Use one stable placeholder state for both deleted and inaccessible entities unless the existing API already distinguishes them safely.

- **Risk: breaking API consumers**
  - Prefer additive response changes. If replacing old fields is necessary, preserve old fields where possible and map new structured fields alongside them.

- **Risk: duplicated count logic**
  - Centralize count computation in the same aggregation path used for section generation.

Follow-ups to note in code comments or task notes if not completed here:
- admin UI for managing severity rules
- richer deep-link URL generation for web/mobile clients
- caching of briefing aggregates if query cost becomes high
- extending the same priority framework to weekly summaries and notification inbox ordering