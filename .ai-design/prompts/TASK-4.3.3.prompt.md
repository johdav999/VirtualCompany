# Goal
Implement backlog task **TASK-4.3.3** for story **US-4.3 Link briefing content to tasks, approvals, workflows, and priorities** by extending the briefing generation and briefing API pipeline so that structured briefing output:

- includes **resolvable links or stable identifiers** for referenced operational entities
- orders sections by **persisted priority/severity rules**
- exposes **machine-validated priority fields** in structured output
- returns **stable placeholder states** when linked entities are deleted or inaccessible
- includes summary counts for:
  - critical alerts
  - open approvals
  - blocked workflows
  - overdue tasks

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant scoping**, and avoid requiring consumers to parse narrative text to determine priority or metrics.

# Scope
In scope:

- Briefing domain/application models for structured operational sections
- Query/service logic that assembles briefing data from tasks, workflows, approvals, and alerts/escalations
- Priority derivation from persisted severity/priority rules already present in storage/config, or a clearly persisted rules source if missing
- Stable link/reference contract for:
  - task
  - workflow instance
  - approval
- Placeholder reference state for deleted/inaccessible linked records
- API response updates for structured briefing metrics and ordered sections
- Tests covering ordering, counts, placeholder behavior, and tenant-safe resolution

Out of scope unless required to satisfy compilation/tests:

- Major UI redesign in Blazor or MAUI
- New notification delivery channels
- Reworking unrelated briefing narrative generation
- Introducing microservices, brokers, or new infrastructure
- Large schema redesign beyond minimal additions needed for persisted severity rules or briefing payload persistence

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - briefing models/value objects
  - enums for severity/priority/reference state
- `src/VirtualCompany.Application/**`
  - briefing queries/handlers/services
  - DTOs/contracts returned by API
  - mapping and ordering logic
  - reference resolution logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/repositories/query implementations
  - SQL/query projections for counts and linked entity lookups
  - migrations if persisted severity rules storage is missing
- `src/VirtualCompany.Api/**`
  - briefing endpoints/controllers
  - response contracts if API layer owns them
- `src/VirtualCompany.Shared/**`
  - shared contracts if briefing DTOs are shared with web/mobile
- `src/VirtualCompany.Web/**`
  - only if compile breaks due to contract changes
- `src/VirtualCompany.Mobile/**`
  - only if compile breaks due to shared contract changes
- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests
- other relevant test projects if present under `tests/**`

Also inspect:

- existing migrations approach and whether active migrations live outside `docs/postgresql-migrations-archive/README.md`
- any existing briefing, dashboard, analytics, approval, workflow, or task aggregate query code

# Implementation plan
1. **Discover current briefing flow**
   - Find the current daily/weekly briefing generation path end-to-end:
     - scheduler/background worker
     - application service/query
     - persistence model
     - API endpoint
     - DTO returned to clients
   - Identify where briefing sections are currently assembled and where narrative vs structured data is stored.

2. **Define/extend structured briefing contract**
   - Add or update a structured response model with explicit fields such as:
     - `metrics`
       - `criticalAlertsCount`
       - `openApprovalsCount`
       - `blockedWorkflowsCount`
       - `overdueTasksCount`
     - `sections[]`
       - `sectionType`
       - `priority`
       - `priorityRank`
       - `severityRuleId` or equivalent persisted rule identifier if available
       - `title`
       - `summary`
       - `references[]`
     - `references[]`
       - `entityType` (`task`, `workflowInstance`, `approval`)
       - `entityId`
       - `displayIdentifier` or stable human-readable identifier if available
       - `state` (`available`, `deleted`, `inaccessible`, `unknown`)
       - `href` or route token if the API already exposes resolvable links
   - Ensure priority can be validated from structured fields alone.

3. **Implement persisted priority/severity derivation**
   - Reuse existing persisted severity/priority rules if they already exist in config/tables/entities.
   - If no persisted rule source exists, add the smallest viable persisted representation consistent with architecture.
   - Do **not** hardcode priority only in transient code if acceptance requires persisted rules.
   - Add deterministic mapping from rule outcome to:
     - structured `priority`
     - sort order/rank
   - Make alerts/risks/escalations sort before informational updates.

4. **Add operational metrics aggregation**
   - Implement tenant-scoped aggregate queries for:
     - critical alerts
     - open approvals
     - blocked workflows
     - overdue tasks
   - Prefer efficient DB-side aggregation.
   - Define “overdue task” using persisted due date + non-terminal status.
   - Define “open approvals” using pending/active statuses only.
   - Define “blocked workflows” from workflow instance state.
   - Define “critical alerts” from alert/escalation/severity source already in system; if alerts are represented indirectly, document and implement the canonical source.

5. **Add reference resolution and placeholder behavior**
   - For each briefing section tied to operational work, attach references to the related entity records.
   - Resolve references tenant-safely.
   - If the linked entity is missing or inaccessible:
     - do not fail the briefing API
     - return a stable placeholder reference object with the original identifier if known
     - set `state` appropriately (`deleted` or `inaccessible`)
     - keep the section in the response
   - Avoid null-only ambiguous behavior; use explicit placeholder state.

6. **Preserve or add resolvable links/identifiers**
   - If the API convention supports URLs, populate `href` using existing route patterns.
   - If not, return stable identifiers sufficient for clients to resolve via existing endpoints.
   - Ensure every operational section has at least one resolvable identifier/reference when applicable.

7. **Update briefing assembly and ordering**
   - Sort sections by structured priority rank before returning/storing.
   - Keep narrative text independent from ordering logic.
   - If briefings are persisted, persist the structured payload or enough structured fields to avoid recomputation inconsistencies.

8. **API compatibility**
   - Extend existing response contracts in a backward-compatible way where possible.
   - If breaking changes are unavoidable, update all internal consumers and tests.
   - Ensure web/mobile shared contracts still compile.

9. **Testing**
   - Add tests for:
     - sections ordered by priority
     - priority present in structured output
     - counts are correct
     - deleted linked task/workflow/approval returns placeholder state
     - inaccessible cross-tenant or unauthorized entity returns placeholder state, not failure
     - briefing API remains successful when one or more references cannot resolve
   - Prefer integration tests at API level plus focused application-level unit tests for ordering/reference mapping.

10. **Document assumptions**
   - If the codebase lacks a first-class alert entity, document the chosen source for “critical alerts”.
   - If persisted severity rules had to be introduced, note where they live and how they are applied.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the briefing API response for a tenant with seeded data:
   - includes `metrics` with all four counts
   - includes `sections` ordered by priority
   - each operational section includes references with identifiers/links
   - priority is explicit in structured fields, not only implied by text

4. Add/verify test scenarios:
   - a critical alert + info update returns critical first
   - pending approvals increment `openApprovalsCount`
   - blocked workflow instances increment `blockedWorkflowsCount`
   - overdue non-completed tasks increment `overdueTasksCount`
   - deleting a referenced task after briefing generation returns placeholder reference state
   - inaccessible entity due to tenant/authorization filtering returns placeholder state and HTTP success for briefing retrieval

5. If migrations were added:
   - apply migration locally using the project’s existing migration workflow
   - rerun build/tests after migration generation

# Risks and follow-ups
- **Risk: no existing alert entity/model**
  - The codebase may represent alerts via notifications, escalations, workflow failures, or briefing-only synthesis. Choose one canonical persisted source and keep it explicit in code/tests.

- **Risk: persisted severity rules may not exist yet**
  - Acceptance requires persisted derivation. If absent, introduce the smallest durable rules representation rather than embedding constants only in application code.

- **Risk: contract ripple effects**
  - Shared DTO changes may affect Web/Mobile compile. Keep additions additive where possible.

- **Risk: placeholder semantics ambiguity**
  - Distinguish clearly between:
    - deleted
    - inaccessible
    - unresolved/unknown
  - Keep these stable for clients.

- **Risk: tenant leakage**
  - Reference resolution must never reveal cross-tenant existence through detailed errors. Placeholder state should be safe and non-leaky.

Follow-ups after implementation:
- Consider exposing a reusable reference envelope across dashboard, inbox, audit, and briefing APIs.
- Consider centralizing severity/priority rules in a dedicated policy/rules service if multiple modules need the same ordering logic.
- Consider adding UI affordances for placeholder references so users understand when linked records are no longer available.