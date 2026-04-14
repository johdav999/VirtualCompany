# Goal
Implement backlog task **TASK-4.2.2 — Extend briefing data model to persist agent contribution metadata and source references** for story **US-4.2 Aggregate cross-agent insights into unified briefing sections**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that updates the briefing domain, persistence, aggregation logic, and API response shape so that generated briefings can:

- persist **multiple agent contributions** per section
- store contribution metadata:
  - `agent identifier`
  - `source reference`
  - `timestamp`
  - `confidence metadata`
- group related insights into a single section when they share the same:
  - company entity
  - workflow
  - task
  - event correlation identifier
- mark a section as **conflicting** when multiple agents provide conflicting assessments for the same topic, while preserving both viewpoints
- expose both:
  - narrative text
  - structured sections
- exclude contributions outside the configured **tenant/company scope**

Keep the implementation aligned with the architecture and backlog direction:
- multi-tenant shared-schema PostgreSQL
- ASP.NET Core backend
- clean module boundaries
- CQRS-lite application layer
- auditability and explainability as first-class concerns

# Scope
In scope:

1. **Domain model changes**
   - Extend briefing-related entities/value objects to represent:
     - briefing sections
     - section grouping keys/correlation keys
     - contribution metadata
     - source references
     - conflict state
   - Ensure the model supports many contributions per section.

2. **Persistence changes**
   - Add or update PostgreSQL schema and EF Core mappings for the new briefing structures.
   - Preserve tenant/company scoping in all persisted records.
   - Support structured storage of source references and confidence metadata.

3. **Aggregation service changes**
   - Update briefing generation/aggregation logic to:
     - merge related insights into one section by shared correlation dimensions
     - detect conflicting assessments for the same topic
     - mark the section as conflicting
     - include both viewpoints in structured output
     - filter out out-of-scope contributions by tenant/company

4. **API contract changes**
   - Update briefing response DTOs/contracts so API consumers receive:
     - narrative text
     - structured sections
     - contribution metadata/source references
     - conflict markers

5. **Tests**
   - Add/adjust unit and integration tests covering all acceptance criteria.

Out of scope unless required by existing code structure:
- UI redesign in Blazor or MAUI
- notification delivery changes
- unrelated refactors
- broad rework of orchestration/task systems beyond what is necessary to support briefing aggregation

# Files to touch
Inspect the solution first and then modify the actual briefing-related files you find. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - briefing entities/value objects
  - enums/status types for conflict markers if needed

- `src/VirtualCompany.Application/**`
  - briefing generation/aggregation services
  - commands/queries/handlers
  - DTOs/view models/API response contracts
  - mapping logic

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations
  - repositories/query implementations

- `src/VirtualCompany.Api/**`
  - response models/endpoints if API contracts are defined here
  - serialization config if needed

- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for briefing payload shape and scoping

Potentially also:
- `README.md` only if there is an established convention for documenting API contract changes
- `docs/postgresql-migrations-archive/README.md` only if migration workflow documentation must be updated

Do **not** invent new top-level projects or architectural layers.

# Implementation plan
1. **Discover the current briefing implementation**
   - Search the solution for:
     - `Briefing`
     - `Summary`
     - `Section`
     - `Aggregation`
     - `Executive`
     - `Daily briefing`
     - `Weekly summary`
   - Identify:
     - current domain entities
     - current persistence model
     - current API response shape
     - where aggregation currently happens
     - whether briefings are stored as messages, notifications, dedicated entities, or a mix

2. **Map the existing model to the acceptance criteria**
   - Determine whether the current implementation already has:
     - a briefing root entity
     - section records
     - source attribution
     - agent references
     - confidence fields
     - correlation identifiers
   - Prefer extending existing models over introducing parallel models.

3. **Design the domain changes**
   - Add or extend briefing structures so each section can contain a collection of contributions.
   - Each contribution should minimally include:
     - contribution id
     - `company_id`
     - briefing/section linkage
     - `agent_id`
     - source reference data
     - contribution timestamp
     - confidence metadata
     - viewpoint/assessment text or structured assessment payload
     - optional topic/correlation metadata
   - Add section-level grouping fields for:
     - `company entity id` or equivalent
     - `workflow instance id`
     - `task id`
     - `event correlation id`
   - Add section-level conflict marker, e.g.:
     - `is_conflicting`
     - optional `conflict_reason` or `conflict_summary`
   - If the codebase uses enums/value objects, follow that pattern.

4. **Design persistence changes**
   - Update EF Core mappings and create a migration.
   - Prefer normalized relational storage for sections and contributions.
   - If source references or confidence details are variable, use JSONB only where appropriate and consistent with existing patterns.
   - Ensure all tenant-owned records include `company_id`.
   - Ensure indexes support likely query patterns:
     - by briefing id
     - by company id
     - by section grouping keys
   - If there is an existing messages table storing generated briefings, keep narrative text there if appropriate, but persist structured sections/contributions in dedicated tables or existing structured columns as fits the current design.

5. **Implement aggregation grouping**
   - Update the aggregation service so related insights are grouped into one section when they share any relevant correlation dimension defined by the task:
     - same company entity
     - same workflow
     - same task
     - same event correlation identifier
   - Be explicit and deterministic about grouping precedence if multiple keys exist.
   - Preserve all grouped contributions in the section.

6. **Implement conflict detection**
   - Add logic to detect conflicting assessments for the same topic.
   - Use the simplest robust rule supported by current data, for example:
     - differing normalized assessment status/sentiment/recommendation for the same grouping key/topic
     - or explicit contradiction flags if already present upstream
   - When conflict is detected:
     - mark the section as conflicting
     - include both/all viewpoints in structured output
     - ensure narrative text reflects the conflict clearly without dropping either contribution

7. **Implement tenant/company scope filtering**
   - Before aggregation and persistence, exclude contributions that do not match the configured tenant/company scope.
   - Enforce this in both:
     - query/repository layer
     - aggregation service guard clauses
   - Do not rely on UI filtering.
   - Add tests proving out-of-scope contributions are excluded.

8. **Update API response contracts**
   - Ensure the generated briefing payload exposes:
     - top-level narrative text
     - structured sections
   - Each section should expose enough structured data for downstream clients, including:
     - title/type if available
     - grouping/correlation identifiers
     - conflict flag
     - section narrative/summary
     - contributions collection
   - Each contribution should expose:
     - agent identifier
     - source reference
     - timestamp
     - confidence metadata
     - viewpoint/assessment content
   - Keep backward compatibility where practical; if breaking changes are unavoidable, minimize them and update tests accordingly.

9. **Add tests**
   - Unit tests for aggregation logic:
     - groups by shared company entity
     - groups by shared workflow
     - groups by shared task
     - groups by shared event correlation id
     - marks conflict when assessments disagree
     - preserves both viewpoints
     - excludes out-of-scope contributions
   - Persistence tests:
     - contribution metadata is stored and retrieved correctly
   - API/integration tests:
     - response includes narrative text and structured sections
     - structured sections include contribution metadata and source references
     - conflict flag appears when expected

10. **Keep implementation aligned with existing conventions**
   - Follow current naming, folder structure, MediatR/CQRS patterns, EF configuration style, and test conventions.
   - Avoid speculative abstractions.
   - Keep changes cohesive and limited to this task.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow, generate/apply/verify the migration using the repository’s established approach.

4. Manually verify the acceptance criteria through tests or local inspection:
   - A generated briefing can store multiple contributions in one section with:
     - agent identifier
     - source reference
     - timestamp
     - confidence metadata
   - Aggregation groups related insights into one section by shared:
     - company entity
     - workflow
     - task
     - event correlation identifier
   - Conflicting assessments for the same topic:
     - mark the section as conflicting
     - include both viewpoints
   - API response exposes:
     - narrative text
     - structured sections
   - Out-of-scope tenant/company contributions are excluded

5. Verify no tenant isolation regressions:
   - inspect queries and tests for `company_id` enforcement
   - ensure no cross-company contribution leakage is possible

# Risks and follow-ups
- **Unknown existing briefing model**: the codebase may already store briefings as messages or denormalized JSON. Extend carefully rather than duplicating concepts.
- **Conflict detection ambiguity**: if upstream contribution payloads do not yet encode assessment polarity/topic clearly, implement the most deterministic rule possible and document assumptions in code comments/tests.
- **Migration impact**: schema changes may require data backfill or nullable transitional columns if existing briefing records already exist.
- **API compatibility**: adding structured sections may affect existing consumers; preserve old fields where possible.
- **Scope enforcement gaps**: tenant/company filtering must be enforced server-side in all relevant query paths, not just in aggregation.
- **Future follow-up likely needed**:
  - richer source reference model
  - more explicit topic taxonomy for conflict detection
  - audit/explainability linkage from briefing contributions back to tasks/tool executions/audit events