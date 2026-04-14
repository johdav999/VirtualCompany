# Goal
Implement backlog task **TASK-4.3.2 — Add entity linking from briefing sections to tasks, approvals, and workflows** for story **US-4.3 Link briefing content to tasks, approvals, workflows, and priorities**.

Deliver a production-ready change in the existing .NET solution so that briefing API responses expose **structured, resolvable entity links**, **priority-ordered sections**, **persisted severity-derived priority**, **stable placeholder states for missing/inaccessible linked entities**, and **summary counts** for critical alerts, open approvals, blocked workflows, and overdue tasks.

The implementation must align with the modular monolith architecture, CQRS-lite application boundaries, tenant-scoped access, and structured outputs suitable for web/mobile clients without requiring narrative parsing.

# Scope
In scope:

- Extend the briefing domain/application contract so each briefing section can include structured links to:
  - related task
  - related workflow instance
  - related approval
- Ensure links are resolvable by identifier and entity type from structured output.
- Add/confirm a **priority model** for briefing sections derived from **persisted severity rules**, not inferred from generated prose.
- Order briefing sections by priority so:
  - alerts
  - risks
  - escalations
  appear before informational updates.
- Add stable placeholder behavior for linked entities that are:
  - deleted
  - inaccessible due to authorization/tenant scope
  - otherwise unavailable
- Include structured aggregate counts in briefing output for:
  - critical alerts
  - open approvals
  - blocked workflows
  - overdue tasks
- Add/update tests covering ordering, linking, placeholder behavior, and counts.
- Keep implementation tenant-scoped and safe for API consumers.

Out of scope unless required by existing code structure:

- New UI redesigns beyond minimal contract compatibility updates.
- New workflow/task/approval business rules unrelated to briefing output.
- LLM prompt redesign except where needed to emit/consume structured briefing data.
- Broad notification delivery changes.
- Mobile-specific business logic.

# Files to touch
Inspect the solution first and then update the actual files that own briefing generation/query/API behavior. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - briefing-related entities/value objects/enums
  - severity/priority rule models if they exist
- `src/VirtualCompany.Application/**`
  - briefing query handlers/services
  - DTOs/contracts/view models
  - entity resolution logic
  - tenant-scoped query composition
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories/query services
  - migrations if persistence changes are required
- `src/VirtualCompany.Api/**`
  - briefing endpoints/controllers/minimal APIs
  - response contracts if API layer maps application DTOs
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums if used by API/web/mobile
- `src/VirtualCompany.Web/**`
  - only if compile errors require adapting consumers of the briefing contract
- `src/VirtualCompany.Mobile/**`
  - only if compile errors require adapting consumers of the briefing contract
- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests for briefing response shape and placeholder behavior
- Potentially other test projects if present for application/domain coverage

Also inspect:
- `README.md`
- any architecture/docs files describing briefing payloads or API contracts
- existing migration guidance under `docs/postgresql-migrations-archive/README.md`

Do not invent file names in the final implementation; use the repository’s actual structure and naming conventions.

# Implementation plan
1. **Discover current briefing implementation**
   - Locate all briefing-related code paths:
     - scheduled briefing generation
     - briefing query/read models
     - API endpoints returning briefing data
     - any persisted briefing/message/notification records
   - Identify whether briefing sections are:
     - generated on demand
     - persisted as structured JSON
     - stored as messages with `structured_payload`
   - Identify existing models for:
     - task references
     - workflow references
     - approval references
     - alert severity / escalation / risk classification

2. **Define/extend structured briefing contract**
   - Introduce or update a structured response model with explicit fields such as:
     - `sections[]`
     - per section:
       - `id`
       - `type` or `category`
       - `title`
       - `summary`
       - `priority`
       - `severityRuleId` or equivalent persisted rule reference if available
       - `linkedEntities[]`
     - per linked entity:
       - `entityType` (`task`, `workflow_instance`, `approval`)
       - `entityId`
       - `state`
       - `displayLabel`
       - optional lightweight metadata for rendering
   - Add top-level counts object:
     - `criticalAlerts`
     - `openApprovals`
     - `blockedWorkflows`
     - `overdueTasks`
   - Ensure the contract is stable and does not require clients to parse prose.

3. **Model stable linked-entity placeholder states**
   - Add a stable enum/string contract for linked entity resolution state, e.g.:
     - `available`
     - `deleted`
     - `inaccessible`
     - `unknown`
   - When a referenced entity cannot be loaded in tenant scope or no longer exists, return a placeholder object with:
     - original `entityType`
     - original `entityId` if known
     - stable `state`
     - safe `displayLabel` such as “Deleted task” / “Unavailable approval”
   - Do not throw or fail the entire briefing response because one linked entity is unavailable.

4. **Implement persisted severity-rule-based priority**
   - Find the persisted source of severity/priority rules.
   - Ensure briefing section priority is derived from persisted structured data, not from narrative text.
   - If current implementation only stores prose or transient severity, add the minimum persistence/modeling needed to support validation.
   - Prefer explicit enum/orderable values, e.g.:
     - `Critical`
     - `High`
     - `Medium`
     - `Low`
     - `Info`
   - Add deterministic sorting so alerts/risks/escalations sort before informational updates.
   - If multiple dimensions exist, define a stable sort order:
     1. priority descending
     2. section category precedence (`alert`, `risk`, `escalation`, then informational)
     3. created/updated timestamp descending or existing deterministic tie-breaker

5. **Add entity resolution layer for briefing links**
   - Implement a tenant-scoped resolver service in the application layer that can resolve references for:
     - tasks
     - workflow instances
     - approvals
   - The resolver should:
     - batch load where possible
     - enforce company/tenant scope
     - map missing/inaccessible entities to placeholder states
   - Avoid direct DB access from API/controllers.
   - Keep contracts typed and reusable for web/mobile/API.

6. **Populate counts in structured output**
   - Add query logic to compute:
     - critical alerts count
     - open approvals count
     - blocked workflows count
     - overdue tasks count
   - Ensure counts are tenant-scoped and aligned with the same briefing context/time window used by the endpoint.
   - If “critical alerts” are represented by briefing sections rather than a dedicated table, compute from structured severity-derived section data.
   - Keep count logic explicit and testable.

7. **Update briefing generation/mapping**
   - Wherever briefing sections are assembled, enrich them with:
     - structured priority
     - linked entity references
     - resolved placeholder-safe linked entity projections
   - Preserve backward compatibility where reasonable, but prefer correctness of the structured contract.
   - If briefings are persisted as message payloads, decide whether to:
     - persist enriched structured payload, or
     - enrich at read time
   - Prefer the smallest change that satisfies acceptance criteria and existing architecture.

8. **Update API response and serialization**
   - Ensure the briefing API returns the new structured fields.
   - Confirm JSON serialization is stable and uses existing conventions.
   - Do not leak unauthorized entity details in placeholder states.
   - Keep nullability intentional and consistent.

9. **Add tests**
   - Domain/application tests for:
     - priority derivation from persisted severity rules
     - deterministic ordering
     - placeholder mapping for deleted/inaccessible entities
     - count calculation
   - API/integration tests for:
     - briefing response includes linked entities
     - linked entities are resolvable by type/id
     - missing linked entities return placeholder state instead of failure
     - sections are returned in correct priority order
     - counts are present and correct
   - Include multi-tenant safety coverage if test infrastructure supports it.

10. **If persistence changes are required**
    - Add EF Core migration(s) using repository conventions.
    - Keep schema changes minimal and backward-compatible where possible.
    - Update configurations and seed/default values as needed.
    - Document any migration or rollout considerations in code comments or relevant docs.

11. **Implementation constraints**
    - Follow existing solution patterns and naming.
    - Preserve clean boundaries:
      - Domain for core concepts
      - Application for orchestration/query logic
      - Infrastructure for persistence
      - API for transport
    - Keep all queries tenant-scoped.
    - Prefer additive changes over breaking changes unless the current contract is internal-only.

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`

2. Run tests before changes to understand current state:
   - `dotnet test`

3. After implementation:
   - `dotnet build`
   - `dotnet test`

4. Verify briefing API contract manually or via tests:
   - response contains `sections`
   - each operational section includes structured linked entities to task/workflow/approval where applicable
   - each link includes stable `entityType`, `entityId`, and resolution `state`
   - top-level counts object includes:
     - critical alerts
     - open approvals
     - blocked workflows
     - overdue tasks

5. Validate ordering:
   - create/seed mixed briefing sections with alert/risk/escalation/info categories and varying severities
   - confirm API returns them in expected priority order

6. Validate placeholder behavior:
   - create a briefing referencing a task/workflow/approval
   - delete or hide the linked entity from the querying tenant/user context
   - confirm API still returns success with placeholder state, not 500/not found for the whole briefing

7. Validate structured priority:
   - confirm priority can be asserted directly from response fields without parsing summary/body text
   - confirm priority source is tied to persisted severity rules or persisted structured severity data

8. If migrations were added:
   - ensure migration applies cleanly in local dev flow
   - ensure tests using database fixtures still pass

# Risks and follow-ups
- **Risk: briefing implementation may currently be narrative-first**
  - If sections are only stored as prose, adding structured priority and links may require introducing or backfilling structured payload fields.
- **Risk: no existing persisted severity rule model**
  - If severity is currently computed ad hoc, implement the smallest persisted structured representation needed to satisfy acceptance criteria and note any broader rule-engine follow-up.
- **Risk: inaccessible vs deleted distinction**
  - Authorization boundaries may make it impossible to distinguish these in some paths without leaking information. If so, use a safe stable placeholder state that does not expose unauthorized details, while still satisfying the “stable placeholder state” requirement.
- **Risk: contract consumers**
  - Web/mobile/shared consumers may assume the old briefing shape. Update only what is necessary to keep the solution compiling and preserve compatibility where possible.
- **Risk: count semantics**
  - “critical alerts” may be ambiguous if alerts are derived from multiple sources. Match existing domain semantics and document assumptions in tests.

Follow-ups to note in code comments or task notes if not fully addressed:
- dedicated reusable entity-link DTOs across dashboard, inbox, and briefing surfaces
- richer placeholder metadata for UI rendering
- caching/optimization for batched entity resolution and counts
- explicit severity rule administration if not yet modeled centrally