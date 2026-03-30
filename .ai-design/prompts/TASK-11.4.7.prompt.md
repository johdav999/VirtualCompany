# Goal
Implement backlog task **TASK-11.4.7 — Consolidation should preserve rationale summaries per contributing agent** for story **ST-504 Manager-worker multi-agent collaboration**.

The coding agent should update the manager-worker multi-agent orchestration flow so that when a coordinator consolidates subtask results from multiple contributing agents, the final consolidated artifact/response preserves a **distinct rationale summary for each contributing agent**, rather than flattening or losing those summaries.

This must align with the architecture and backlog intent:
- manager-worker collaboration only
- explicit subtask planning and bounded execution
- final output consolidated into one response
- source attribution by agent
- no raw chain-of-thought exposure
- concise, operational rationale summaries only
- auditability as a domain feature

# Scope
In scope:
- Identify the current multi-agent coordination and consolidation path in the .NET solution.
- Extend the domain/application contracts used for multi-agent subtask results and final consolidation so that each contributing agent’s rationale summary is preserved.
- Ensure the consolidated result includes per-agent attribution and rationale summaries in a structured form.
- Persist this structured information where appropriate for downstream audit/explainability and task output usage.
- Update any DTOs/view models/response contracts involved in returning consolidated multi-agent results.
- Add or update tests covering the preservation of rationale summaries across consolidation.

Out of scope:
- New UI redesigns unless required to keep contracts compiling.
- Exposing raw reasoning or chain-of-thought.
- Broad audit subsystem redesign beyond what is necessary to carry/store per-agent rationale summaries.
- Free-form agent-to-agent chat loops or changes to collaboration limits/fan-out/depth policies.
- Mobile-specific work unless shared contracts force a compile fix.

# Files to touch
Start by inspecting and then modify only the relevant files you find in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- possibly `src/VirtualCompany.Api`
- possibly `src/VirtualCompany.Shared`
- possibly `src/VirtualCompany.Web` only if shared response models require compile fixes

Likely file categories to inspect:
- Multi-agent coordinator / orchestration services
- Consolidation/final response builders
- Task result / orchestration result domain models
- DTOs for task output or agent collaboration results
- Persistence mappings / EF configurations for task output or audit artifacts
- Explainability/audit models if they already carry rationale/source attribution
- Unit/integration tests for ST-504 or orchestration consolidation

Use the workspace context as anchors:
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`

# Implementation plan
1. **Discover the current manager-worker flow**
   - Locate the implementation for ST-504-style coordination:
     - coordinator service
     - subtask creation/assignment
     - subtask result collection
     - final consolidation
   - Identify where a worker agent’s result currently stores:
     - output
     - rationale summary
     - attribution metadata
   - Identify where consolidation currently drops or overwrites rationale summaries.

2. **Define the target shape for preserved rationale summaries**
   - Introduce or extend a structured model representing a contributing agent in a consolidated result, for example:
     - `AgentId`
     - `AgentDisplayName`
     - `RoleName` or equivalent
     - `SubtaskId` / `TaskId`
     - `OutputSummary`
     - `RationaleSummary`
     - optional `ConfidenceScore`
     - optional source references if already supported
   - Keep naming consistent with the existing codebase.
   - Do not store raw chain-of-thought; only preserve concise rationale summaries already intended for user/audit visibility.

3. **Update domain/application contracts**
   - Extend the relevant orchestration result model(s) so the final consolidated result can carry a collection of per-contributor summaries.
   - If there is already a source-attribution structure, enrich it with `RationaleSummary` rather than creating a parallel redundant structure.
   - Ensure null/empty handling is explicit:
     - contributors without rationale summaries should not break consolidation
     - preserve available summaries without inventing content

4. **Update consolidation logic**
   - Modify the consolidation step so it gathers rationale summaries from each completed contributing worker result.
   - Preserve one rationale summary per contributing agent in the final structured output.
   - Ensure the coordinator’s own final summary remains separate from worker rationale summaries.
   - If the final response includes a human-readable consolidated narrative, keep it concise and add structured contributor details alongside it rather than embedding everything into one blob.

5. **Persist structured contributor rationale where appropriate**
   - If consolidated outputs are stored in `tasks.output_payload`, `messages.structured_payload`, audit artifacts, or similar JSON-backed fields, include the per-agent rationale summary collection there.
   - If there is an existing typed persistence model for orchestration results, update mappings/configuration accordingly.
   - Avoid schema churn unless truly necessary; prefer existing JSON payload structures if the codebase already uses them for orchestration artifacts.
   - If a relational schema change is required, keep it minimal and justified.

6. **Preserve explainability/audit alignment**
   - Ensure the final consolidated artifact supports downstream explainability:
     - source attribution by agent
     - rationale summary per contributing agent
   - If audit events are emitted for consolidated completion, include enough structured metadata to reference contributor rationale summaries without duplicating excessive text.

7. **Update API/shared contracts if needed**
   - If the consolidated result is returned through API DTOs or shared models, update those contracts.
   - Keep backward compatibility in mind where possible:
     - additive fields preferred
     - avoid breaking existing consumers unless unavoidable

8. **Add tests**
   - Add or update unit tests for the consolidation logic to verify:
     - multiple worker outputs with distinct rationale summaries are all preserved
     - attribution remains matched to the correct agent
     - missing rationale on one worker does not remove others
     - coordinator summary does not overwrite worker summaries
   - Add integration/application tests if the codebase already has orchestration pipeline tests.

9. **Keep implementation bounded and idiomatic**
   - Follow existing project conventions, namespaces, serialization patterns, and test style.
   - Prefer small focused changes over broad refactors.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for orchestration/application logic, run them first or additionally.

4. Manually verify in code/tests that:
   - a parent/coordinator task with multiple worker subtasks produces a consolidated result containing a per-agent rationale summary collection
   - each entry is correctly attributed to the contributing agent
   - existing top-level consolidated summary still works
   - no raw reasoning is exposed
   - serialization/deserialization of the structured payload succeeds

5. If persistence payloads were changed, verify:
   - EF/configuration compiles
   - JSON payloads round-trip correctly in tests
   - no tenant-scoping or task linkage behavior regressed

# Risks and follow-ups
- **Risk: contract drift across layers**
  - Multi-agent result models may exist in domain, application, API, and UI layers. Keep changes additive and consistent.

- **Risk: rationale summary currently stored only as flat text**
  - If the current implementation only keeps one final rationale field, you may need to introduce a structured payload collection. Prefer minimal additive changes.

- **Risk: accidental chain-of-thought exposure**
  - Preserve only existing concise rationale summaries intended for user-facing explainability. Do not expose internal reasoning traces.

- **Risk: persistence compatibility**
  - If existing stored payloads are deserialized from JSON, ensure new fields are optional/backward compatible.

- **Risk: attribution mismatch**
  - Be careful that rationale summaries remain tied to the correct contributing agent/subtask, especially if consolidation aggregates asynchronously.

Suggested follow-ups after this task:
- Surface per-contributing-agent rationale summaries in audit/explainability views if not already visible.
- Add UI presentation for contributor rationale in consolidated task detail.
- Consider including contributor confidence and source references alongside rationale summaries where already available.