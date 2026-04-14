# Goal
Implement backlog task **TASK-4.2.1 — Create cross-agent insight aggregation service with grouping and conflict detection** for story **US-4.2 Aggregate cross-agent insights into unified briefing sections**.

Deliver a production-ready .NET implementation that enables briefing generation to:
- aggregate contributions from multiple agents,
- preserve contribution metadata,
- group related insights into unified sections,
- detect and mark conflicts for the same topic,
- expose both narrative and structured briefing output,
- enforce tenant and company scope filtering.

The implementation must fit the existing modular monolith architecture and keep orchestration/application logic out of controllers/UI.

# Scope
Implement the minimum complete vertical slice needed to satisfy the acceptance criteria.

In scope:
- Add or extend domain/application models for briefing insight contributions and aggregated briefing sections.
- Create an **aggregation service** in the application layer that:
  - accepts multiple agent insight contributions,
  - filters out contributions outside tenant/company scope,
  - groups related insights by shared correlation keys:
    - company entity,
    - workflow,
    - task,
    - event correlation identifier,
  - detects conflicting assessments for the same grouped topic,
  - produces:
    - narrative text,
    - structured sections,
    - contribution metadata per section.
- Ensure each contribution stores/exposes:
  - agent identifier,
  - source reference,
  - timestamp,
  - confidence metadata.
- Update the briefing payload/DTO/API contract so responses include:
  - narrative text,
  - structured sections,
  - conflict markers and both viewpoints when conflicts exist.
- Add tests covering grouping, conflict detection, metadata preservation, and tenant/company scope exclusion.

Out of scope unless required by existing code structure:
- Full UI rendering changes beyond contract compatibility.
- New scheduling infrastructure.
- New mobile-specific behavior.
- Large refactors unrelated to briefing aggregation.
- LLM prompt redesign beyond what is necessary to consume the new aggregation output.

Assumptions to validate in the codebase before implementation:
- There is already some briefing generation flow under Communication / Analytics / Orchestration modules.
- There may already be message, summary, or briefing DTOs that should be extended rather than replaced.
- Tenant scope is represented by `company_id`; if a separate `tenant_id` abstraction exists in code, honor both.

# Files to touch
Inspect the solution first and then update the exact files that align with the existing architecture. Likely areas:

- `src/VirtualCompany.Domain/**`
  - briefing-related domain models/value objects/enums
  - possible conflict status/grouping key abstractions
- `src/VirtualCompany.Application/**`
  - briefing aggregation service interface + implementation
  - commands/queries/handlers for briefing generation
  - DTOs/contracts for briefing payloads
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories if briefing contributions or sections are stored
  - EF Core configurations if new entities are introduced
- `src/VirtualCompany.Api/**`
  - response contracts or endpoint mapping if API models are defined here
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared DTOs across app/web/mobile
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for briefing response shape and scope filtering
- Add tests in the most appropriate test project for:
  - application service unit tests
  - API contract/integration tests

Also inspect:
- existing briefing/summarization code,
- task/workflow/agent contribution models,
- any current message/notification payloads,
- tenant/company authorization helpers,
- repository/query patterns.

# Implementation plan
1. **Discover the current briefing flow**
   - Find existing briefing generation endpoints, handlers, services, and DTOs.
   - Identify where agent outputs are currently combined or summarized.
   - Determine whether contributions already exist as task outputs, messages, audit artifacts, or ad hoc models.
   - Reuse existing patterns for CQRS-lite, dependency injection, and result handling.

2. **Define the aggregation contract**
   - Introduce an application-layer service such as:
     - `IBriefingInsightAggregationService`
   - The service should accept a collection of raw insight contributions plus scope context.
   - Define clear input/output models, for example:
     - `BriefingInsightContribution`
     - `AggregatedBriefingSection`
     - `AggregatedBriefingPayload`
   - Keep models explicit and serializable.

3. **Model contribution metadata**
   - Ensure each contribution includes:
     - `AgentId`
     - `SourceReference` or structured source reference object
     - `Timestamp`
     - `Confidence`
     - `CompanyId`
     - optional `TenantId` if present in the codebase
     - grouping references:
       - `CompanyEntityId`
       - `WorkflowInstanceId`
       - `TaskId`
       - `EventCorrelationId`
     - topic/assessment content fields:
       - title/topic
       - narrative/body
       - assessment or stance
   - If an existing model already contains most of this, extend it instead of duplicating.

4. **Implement scope filtering first**
   - Before grouping, exclude any contribution not matching the configured scope.
   - Scope rules must satisfy:
     - only contributions for the active company are included,
     - if tenant context exists separately, contributions must also match tenant,
     - no cross-company leakage through source references or linked entities.
   - Prefer a dedicated helper/policy method so this logic is testable.

5. **Implement grouping logic**
   - Group related insights into one section when they share one of the accepted correlation identifiers.
   - Use a deterministic grouping strategy with clear precedence, for example:
     1. `CompanyEntityId`
     2. `WorkflowInstanceId`
     3. `TaskId`
     4. `EventCorrelationId`
     5. fallback topic key if one already exists in current code
   - Preserve all grouped contributions inside the section.
   - Generate a stable section identifier/key and a human-readable section title.
   - Do not merge unrelated contributions just because text is similar; grouping must be based on explicit correlation identifiers from the acceptance criteria.

6. **Implement conflict detection**
   - Detect conflicts when multiple agents provide incompatible assessments for the same grouped topic.
   - Prefer explicit structured assessment fields if available, such as:
     - status,
     - recommendation,
     - sentiment/risk level,
     - decision outcome.
   - If the current code only has text, implement a conservative rule-based conflict detector rather than speculative NLP.
   - Mark the section as conflicting when conflict is detected.
   - Include both viewpoints in the structured output.
   - Avoid collapsing conflicting contributions into a single synthesized conclusion that hides disagreement.

7. **Produce structured section output**
   - Each aggregated section should expose at minimum:
     - section id/key,
     - title,
     - grouping type/key,
     - narrative summary,
     - `IsConflicting`,
     - grouped contributions,
     - conflict viewpoints when applicable,
     - related references (task/workflow/entity/event ids).
   - Each contribution in the section must retain its metadata.

8. **Produce narrative briefing text**
   - Build top-level narrative text from the aggregated sections.
   - Keep the narrative concise and deterministic.
   - If a section is conflicting, the narrative should explicitly mention disagreement.
   - Do not omit structured details from the API payload; narrative is additive, not a replacement.

9. **Update API/application response contracts**
   - Extend the generated briefing payload so API responses expose:
     - top-level narrative text,
     - structured sections collection.
   - Ensure backward compatibility where practical:
     - additive fields preferred,
     - avoid breaking existing consumers unless unavoidable.
   - If there is an existing message/notification payload, embed the structured sections there cleanly.

10. **Persist data if the current design requires it**
    - If briefings are stored, ensure stored payloads include the new structured sections and contribution metadata.
    - If persistence is not yet part of the current flow, do not invent unnecessary tables unless required by acceptance criteria and architecture.
    - If new persistence is needed, add EF configuration and migrations in the project’s established pattern.

11. **Add tests**
    - Unit tests for aggregation service:
      - groups by company entity id,
      - groups by workflow id,
      - groups by task id,
      - groups by event correlation id,
      - excludes out-of-scope company/tenant contributions,
      - preserves contribution metadata,
      - marks conflicts and includes both viewpoints,
      - returns both narrative and structured sections.
    - API/integration tests:
      - briefing endpoint returns expected payload shape,
      - out-of-scope contributions are absent,
      - conflicting section is flagged in serialized response.

12. **Keep implementation aligned with architecture**
    - Application layer owns aggregation logic.
    - Infrastructure only handles persistence/query concerns.
    - API layer should map request/response only.
    - No direct DB access from controllers.
    - Keep logic deterministic and testable.

Suggested implementation details:
- Use immutable records where appropriate for DTOs/value objects.
- Prefer explicit enums for:
  - grouping type,
  - conflict status,
  - assessment stance if needed.
- Add XML comments only where the codebase already uses them.
- Follow existing naming, namespace, and folder conventions.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add and verify automated tests for these scenarios:
   - multiple agent contributions for same company entity aggregate into one section,
   - same for workflow/task/event correlation identifiers,
   - conflicting assessments mark section as conflicting,
   - both viewpoints remain present in output,
   - contribution metadata is preserved,
   - narrative text and structured sections are both returned,
   - tenant/company scope filtering excludes invalid contributions.

4. If there is an API endpoint for briefing generation/retrieval:
   - verify serialized JSON includes:
     - narrative text field,
     - structured sections array,
     - per-contribution metadata,
     - conflict marker.

5. If persistence is changed:
   - verify migrations/configuration compile and tests pass.
   - ensure no cross-tenant/company records are returned by repository queries.

6. Include a short implementation summary in your final output:
   - files changed,
   - key design decisions,
   - any assumptions made due to missing code paths.

# Risks and follow-ups
- **Risk: unclear existing briefing model**
  - Mitigation: inspect and extend current contracts instead of creating parallel models.

- **Risk: conflict detection may be under-specified**
  - Mitigation: implement conservative, explicit rule-based detection using structured assessment fields where available; document assumptions.

- **Risk: tenant vs company terminology mismatch**
  - Mitigation: inspect current auth/scope model and enforce both if both exist; do not assume they are interchangeable.

- **Risk: grouping collisions**
  - Mitigation: use deterministic grouping precedence and stable keys; add tests for mixed identifiers.

- **Risk: breaking existing API consumers**
  - Mitigation: prefer additive response changes and preserve existing fields where possible.

Follow-ups to note if not completed in this task:
- richer conflict taxonomy,
- UI rendering for structured/conflicting sections,
- persisted audit trail for aggregation decisions,
- scheduled briefing generation integration if not already wired,
- source reference normalization across tasks/messages/audit artifacts.