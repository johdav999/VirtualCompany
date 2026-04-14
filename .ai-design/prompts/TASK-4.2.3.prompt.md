# Goal
Implement backlog task **TASK-4.2.3 — Expose narrative and structured briefing sections through briefing read APIs** for story **US-4.2 Aggregate cross-agent insights into unified briefing sections**.

Update the backend so briefing read APIs return both:
- the existing/high-level **narrative text**
- a new/expanded **structured sections** representation

The implementation must ensure generated briefings can surface multi-agent contributions with attribution and metadata, preserve conflict visibility, and enforce tenant/company scoping on all returned content.

# Scope
In scope:
- Read-side API and application query changes for briefing retrieval
- Domain/application DTOs/contracts needed to expose structured briefing sections
- Persistence mapping updates if briefing section/contribution metadata already exists but is not yet returned
- Aggregation/read-model shaping needed so API responses include:
  - narrative text
  - structured sections
  - per-section conflict marker
  - per-contribution agent identifier, source reference, timestamp, confidence metadata
- Tenant/company scope enforcement so out-of-scope contributions are excluded from output
- Tests covering acceptance criteria at API/application level

Out of scope unless required by existing code structure:
- Rebuilding the entire briefing generation pipeline from scratch
- UI/mobile rendering changes
- New scheduling behavior for briefing generation
- Broad refactors unrelated to briefing read APIs

Important behavioral requirements from acceptance criteria:
1. A generated briefing can include contributions from multiple agents and each contribution is stored with:
   - agent identifier
   - source reference
   - timestamp
   - confidence metadata
2. Aggregation groups related insights into a single section when they share the same:
   - company entity
   - workflow
   - task
   - or event correlation identifier
3. Conflicting assessments for the same topic must mark the section as conflicting and include both viewpoints.
4. API response must expose both narrative text and structured sections.
5. Aggregation must exclude contributions outside configured tenant and company scope.

# Files to touch
Inspect the solution first and then update the actual files that implement briefing persistence, queries, and API contracts. Likely areas:

- `src/VirtualCompany.Api/**`
  - briefing controller/endpoints
  - response contracts / API models
- `src/VirtualCompany.Application/**`
  - briefing queries/handlers
  - DTOs/view models
  - mapping logic from persistence/domain to API response
- `src/VirtualCompany.Domain/**`
  - briefing entities/value objects if structured sections/contributions are domain concepts
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL entity configuration
  - repositories/query services/read models
  - migrations if schema changes are required
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared API DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - API integration/contract tests
- potentially `tests/**Application**` or other existing test projects if present

Also inspect:
- `README.md`
- any architecture or module docs related to communication, briefings, orchestration, or auditability
- existing migration conventions under `docs/postgresql-migrations-archive/README.md`

Do not invent new top-level patterns if the repository already has established conventions.

# Implementation plan
1. **Discover the current briefing model and read path**
   - Find briefing-related entities, tables, endpoints, query handlers, and DTOs.
   - Determine whether briefings are stored as:
     - messages/notifications,
     - dedicated briefing entities,
     - JSON payloads,
     - or a combination.
   - Identify where narrative text currently comes from and whether structured sections already exist in storage but are not exposed.

2. **Map current data against required output**
   - Verify whether the persistence model already stores:
     - section identity/grouping keys
     - contribution records
     - agent IDs
     - source references
     - timestamps
     - confidence
     - conflict markers
   - If missing, add the minimum schema/domain changes needed to support read API acceptance criteria.
   - Prefer additive, backward-compatible changes.

3. **Define/extend the briefing read contract**
   - Update the read API response model to include both:
     - `narrative` or equivalent text field
     - `sections` collection
   - Each structured section should expose enough information to satisfy the task, e.g.:
     - section id/type/title
     - grouping references (entity/workflow/task/event correlation where applicable)
     - narrative/summary for the section if present
     - `isConflicting`
     - contributions[]
   - Each contribution should expose:
     - agent identifier
     - source reference
     - timestamp
     - confidence metadata
     - viewpoint/assessment text or structured assessment payload

4. **Implement grouping behavior for structured sections**
   - Ensure related insights are grouped into one section when they share the same:
     - company entity
     - workflow
     - task
     - event correlation identifier
   - Reuse existing aggregation artifacts if generation already persists grouped sections.
   - If grouping currently happens only at generation time, make the read model faithfully return those persisted groups.
   - If grouping is not persisted and must be assembled on read, implement deterministic grouping logic in the application/infrastructure query layer.

5. **Implement conflict handling**
   - Ensure sections with conflicting assessments are marked as conflicting.
   - Include both viewpoints in the returned section.
   - Prefer persisted conflict state if generation already computes it.
   - If conflict state must be derived on read, implement a clear rule based on existing assessment/status fields and keep it deterministic/testable.

6. **Enforce tenant and company scope**
   - Audit the query path so all briefing reads are filtered by tenant/company context.
   - Ensure out-of-scope contributions are not included even if linked to the same higher-level grouping.
   - Follow existing multi-tenant enforcement patterns in the codebase; do not bypass repository/query filters.

7. **Preserve backward compatibility**
   - Avoid breaking existing consumers where possible.
   - If the current response shape is already in use, add fields rather than renaming/removing existing ones unless the codebase clearly supports versioned contracts.

8. **Add tests**
   - Add/extend tests to cover:
     - response includes both narrative text and structured sections
     - multi-agent contributions are returned with required metadata
     - grouping by shared entity/workflow/task/event correlation
     - conflicting viewpoints mark section as conflicting and include both contributions
     - out-of-scope contributions are excluded
   - Prefer integration/API tests if the project already uses them for endpoint behavior.
   - Add focused unit tests for grouping/conflict logic if implemented in application services.

9. **Keep implementation aligned with architecture**
   - Use CQRS-lite query handlers for read concerns.
   - Keep orchestration/generation logic separate from HTTP concerns.
   - Keep tenant isolation enforced in repository/query/application layers.
   - Do not expose chain-of-thought; only operational summaries and structured contribution data.

Suggested response shape example only if needed by the codebase:
```json
{
  "briefingId": "uuid",
  "companyId": "uuid",
  "generatedAt": "2026-04-14T08:00:00Z",
  "narrative": "Today’s executive briefing...",
  "sections": [
    {
      "sectionId": "uuid",
      "title": "Customer churn risk",
      "topic": "customer-churn",
      "isConflicting": true,
      "companyEntityId": "uuid",
      "workflowInstanceId": null,
      "taskId": "uuid",
      "eventCorrelationId": "corr-123",
      "summary": "Two agents disagree on severity.",
      "contributions": [
        {
          "agentId": "uuid",
          "sourceReference": "task:123",
          "timestamp": "2026-04-14T07:45:00Z",
          "confidence": 0.82,
          "assessment": "Risk is rising due to support backlog."
        },
        {
          "agentId": "uuid",
          "sourceReference": "workflow:456",
          "timestamp": "2026-04-14T07:46:00Z",
          "confidence": 0.61,
          "assessment": "Risk is contained due to recent renewals."
        }
      ]
    }
  ]
}
```

Implementation guidance:
- Prefer existing naming conventions over the example above.
- If confidence is modeled as richer metadata rather than a single numeric value, expose that richer structure.
- If source reference is structured, do not flatten it unnecessarily.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run relevant tests:
   - `dotnet test`

3. Add/verify automated coverage for:
   - briefing read endpoint returns narrative + structured sections
   - section contributions include agent/source/timestamp/confidence
   - grouping behavior for shared entity/workflow/task/event correlation
   - conflicting sections include both viewpoints and are flagged
   - tenant/company scoping excludes out-of-scope contributions

4. If schema changes are required:
   - generate/apply migration using the repository’s existing conventions
   - verify migration is additive and safe
   - verify read endpoint works against migrated schema

5. Manually validate API payload shape using the existing endpoint tests or local HTTP tooling:
   - retrieve a briefing with multiple agent contributions
   - confirm both narrative and sections are present
   - confirm conflicting section behavior
   - confirm no foreign-tenant/company data leaks

# Risks and follow-ups
- **Risk: briefing model may already exist in a different module**
  - Mitigation: trace from endpoint inward before changing schema.

- **Risk: grouping/conflict logic may belong to generation, not read**
  - Mitigation: prefer exposing persisted aggregation results; only derive on read if necessary and consistent with current design.

- **Risk: tenant scoping bugs could leak data**
  - Mitigation: add explicit tests for cross-company/cross-tenant exclusion and reuse established query filters.

- **Risk: existing clients may depend on current response shape**
  - Mitigation: make additive contract changes and avoid removing fields.

- **Risk: source/confidence metadata may be inconsistently stored**
  - Mitigation: normalize in application DTOs and document any unavoidable nullability.

Follow-ups to note in code comments or task notes if not fully addressed here:
- UI/mobile consumption of structured briefing sections
- richer source reference typing and explainability links
- explicit API versioning if briefing payloads evolve further
- generation-side hardening if read-side reveals missing metadata quality