# Goal
Implement backlog task **TASK-11.4.3** for story **ST-504 Manager-worker multi-agent collaboration** so that the **final manager/coordinator response consolidates worker sub-results into a single response with clear source attribution by contributing agent**.

This work should fit the existing **.NET modular monolith** architecture and shared orchestration subsystem. The implementation must preserve the manager-worker pattern, avoid uncontrolled agent chatter, and support downstream audit/explainability by retaining per-agent rationale summaries and contribution metadata.

# Scope
Focus only on the consolidation/output portion of multi-agent collaboration, not the entire manager-worker system from scratch.

In scope:
- Extend the multi-agent coordination result model so worker outputs can be consolidated into one final response.
- Add explicit **source attribution by agent** in the final consolidated response.
- Preserve and expose **per-contributing-agent rationale summaries** in structured artifacts.
- Ensure the final response shape is suitable for:
  - user-facing display
  - task output persistence
  - audit/explainability linkage
- Update orchestration/application logic so the coordinator produces:
  - one synthesized final answer
  - a structured list of contributing agents and their sub-results
- Add tests covering consolidation behavior and attribution formatting/structure.

Out of scope unless required by existing code paths:
- Building the full decomposition planner from scratch
- New UI pages beyond minimal DTO/view-model compatibility
- New external integrations
- Major schema redesigns unless persistence models already require a small additive change

# Files to touch
Inspect the solution first and then touch the minimum necessary files, likely in these areas:

- `src/VirtualCompany.Application/**`
  - orchestration/coordinator service(s)
  - task/application DTOs
  - response/result models
  - command/query handlers related to multi-agent execution
- `src/VirtualCompany.Domain/**`
  - domain models/value objects for multi-agent result aggregation if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories if consolidated output or attribution metadata is stored
- `src/VirtualCompany.Api/**`
  - API contracts/controllers only if response contracts must expose attribution
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts if used across API/Web/Mobile
- `src/VirtualCompany.Web/**`
  - only if existing UI bindings break due to contract changes
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for final response payloads
- Add or update tests in the relevant test project(s) for application/domain behavior

Also review:
- `README.md`
- any existing orchestration, task, audit, or explainability docs/comments
- project structure under `src/VirtualCompany.Application` for existing naming conventions

# Implementation plan
1. **Discover existing multi-agent orchestration flow**
   - Find the current implementation for ST-504/ST-502/ST-503-related orchestration.
   - Identify:
     - coordinator/manager service
     - worker subtask result model
     - final response builder
     - task output persistence path
     - audit/explainability hooks
   - Do not invent parallel abstractions if suitable ones already exist.

2. **Define a structured consolidated result contract**
   - Introduce or extend a result model to represent:
     - final synthesized response text/summary
     - contributing agents
     - each agent’s attributed sub-result
     - each agent’s rationale summary
     - optional confidence/status/source task references if already available
   - Prefer additive, backward-compatible changes.
   - Suggested shape conceptually:
     - `FinalResponse`
     - `Contributions[]`
       - `AgentId`
       - `AgentName`
       - `AgentRole` if available
       - `Summary` or `ContributionText`
       - `RationaleSummary`
       - `SourceTaskId` / `SubtaskId` if available

3. **Implement consolidation logic in the coordinator**
   - Update the manager/coordinator finalization step to:
     - collect completed worker outputs
     - normalize them into a common contribution structure
     - synthesize one final answer from those sub-results
     - attach explicit attribution by agent
   - Ensure the final response is bounded to explicit worker results only.
   - Do not allow hidden/untracked contributions.
   - If some worker tasks fail or are unavailable, include only valid contributions and handle partial completion safely.

4. **Preserve explainability metadata**
   - Ensure each contribution retains the worker agent identity and rationale summary.
   - If the system already stores task `output_payload` and `rationale_summary`, map those into the consolidated artifact rather than duplicating logic.
   - Where appropriate, include source references to subtask/task IDs for audit traceability.

5. **Persist consolidated output**
   - Update the relevant task/workflow output persistence so the parent/coordinator task stores:
     - the final consolidated response
     - structured contribution metadata
   - Keep persistence aligned with existing `tasks.output_payload` / `rationale_summary` patterns.
   - If a schema change is needed, make it minimal and additive; prefer JSON payload extension over broad relational changes unless the codebase already models this explicitly.

6. **Expose attribution in API/shared contracts**
   - If the final response is returned through API or shared DTOs, update contracts so consumers can access:
     - final response text
     - attributed contributions by agent
   - Maintain backward compatibility where possible.

7. **Add tests**
   - Add unit and/or integration tests for:
     - consolidation of multiple worker outputs into one final response
     - attribution includes correct agent identity
     - rationale summaries are preserved per agent
     - partial worker failure does not break final consolidation
     - no unattributed contribution appears in the final structured result
   - Prefer deterministic tests over LLM-dependent behavior; mock synthesis if needed.

8. **Keep formatting user-friendly but structured**
   - The user-facing final response should read as one coherent answer.
   - The structured artifact should separately expose attribution details for UI/audit use.
   - Avoid raw chain-of-thought; only use concise rationale summaries.

9. **Document assumptions in code comments if needed**
   - If there is ambiguity in how final synthesis should be phrased, keep the implementation generic and structured.
   - Favor machine-readable attribution over brittle string-only formatting.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify multi-agent consolidation behavior through tests or existing endpoints:
   - parent/coordinator result contains a single final response
   - structured contributions list contains one entry per contributing worker agent
   - each contribution includes agent attribution and rationale summary
   - parent output remains tenant-safe and aligned with existing task models

4. If API contracts are involved, verify serialized payloads:
   - final response field present
   - contribution/source attribution fields present
   - no breaking contract regressions for existing consumers unless unavoidable

5. If persistence changed, verify:
   - migrations/build mappings are valid
   - parent task/workflow output stores consolidated artifact correctly
   - existing reads do not fail on older records without contribution metadata

# Risks and follow-ups
- **Risk: unclear existing orchestration abstractions**
  - Mitigation: inspect current ST-502/ST-504 implementation first and extend it rather than introducing duplicate coordinator models.

- **Risk: over-coupling user-facing text with audit structure**
  - Mitigation: keep a structured contributions model separate from the synthesized final text.

- **Risk: breaking existing consumers**
  - Mitigation: make DTO/schema changes additive and preserve existing fields where possible.

- **Risk: partial worker failures**
  - Mitigation: support consolidation from successful sub-results only and surface incomplete coverage in a safe way if needed.

- **Risk: rationale leakage**
  - Mitigation: store and expose only concise rationale summaries, never raw chain-of-thought.

Follow-ups after this task, if not already implemented:
- UI rendering of attributed contributions in task detail/audit views
- richer source references linking contributions to subtasks/tool executions
- explicit fan-out/depth/runtime guardrails for manager-worker plans
- confidence scoring or completeness indicators on consolidated outputs