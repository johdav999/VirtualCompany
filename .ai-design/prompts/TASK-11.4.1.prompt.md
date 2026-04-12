# Goal
Implement backlog task **TASK-11.4.1** for **ST-504 Manager-worker multi-agent collaboration** by adding a **coordinator flow** that can take a cross-functional request, generate an **explicit bounded plan**, create and link **subtasks for multiple agents**, execute those subtasks through the shared orchestration pipeline, and produce a **single consolidated result** with **per-agent attribution**.

This implementation must align with the architecture’s **manager-worker pattern** and explicitly avoid uncontrolled agent-to-agent chatter loops. The coordinator must operate through **planned delegation only**, with limits on **fan-out, depth, and runtime**, and must persist enough state for task tracking and future auditability.

# Scope
Implement the minimum vertical slice needed to support explicit multi-agent coordination in the existing modular monolith.

Include:

- Domain/application support for a **parent task** that can be decomposed into **child subtasks**
- A **coordinator service** in the AI orchestration subsystem that:
  - accepts a parent task/request
  - determines whether multi-agent decomposition is needed
  - creates an explicit plan
  - assigns subtasks to selected worker agents
  - collects worker outputs
  - consolidates a final response
- Persistence of:
  - parent/child task linkage
  - structured coordination plan metadata
  - per-subtask attribution
  - consolidated output metadata
- Guardrails to ensure:
  - no free-form recursive delegation
  - bounded fan-out/depth/runtime
  - tenant and agent scope are preserved
- Tests covering the coordinator happy path and key guardrail behavior

Do not include unless already trivial in the codebase:

- Full UI for visualizing plans
- Arbitrary workflow-builder UX
- Real-time agent-to-agent chat threads
- Deep approval-chain integration beyond preserving extension points
- New external integrations

# Files to touch
Touch the smallest set of files consistent with the existing solution structure. Prefer extending existing orchestration, task, and persistence layers rather than creating parallel patterns.

Likely areas:

- `src/VirtualCompany.Domain`
  - task entities/value objects/enums related to parent-child task relationships
  - any orchestration plan domain models if domain-owned
- `src/VirtualCompany.Application`
  - coordinator use case / application service
  - commands/queries/DTOs for creating and tracking coordinated subtasks
  - orchestration interfaces
  - task application services
- `src/VirtualCompany.Infrastructure`
  - persistence mappings/repositories
  - orchestration service implementation
  - any JSONB persistence for plan/consolidation metadata
- `src/VirtualCompany.Api`
  - endpoint/controller wiring if a task execution or orchestration endpoint must expose this flow
- `tests/VirtualCompany.Api.Tests`
  - integration/API tests
- additional test project(s) if present and appropriate for application/domain tests

Also inspect before coding:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- existing task/orchestration/policy-related code paths
- existing migration approach referenced by `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Inspect current architecture in code**
   - Find existing implementations for:
     - task creation and assignment
     - orchestration pipeline for single-agent execution
     - agent lookup/registry
     - policy enforcement
     - persistence of task payloads and rationale summaries
   - Reuse existing abstractions and naming conventions.
   - Identify whether migrations are active in code or archived/manual.

2. **Define the coordinator data contract**
   Add a structured model for explicit decomposition and consolidation. Keep it simple and serializable.

   Suggested shape:
   - `CoordinationPlan`
     - `CoordinatorAgentId`
     - `ParentTaskId`
     - `Goal`
     - `PlanStatus`
     - `MaxFanOut`
     - `MaxDepth`
     - `Subtasks[]`
   - `PlannedSubtask`
     - `SubtaskKey`
     - `AssignedAgentId`
     - `Title`
     - `Instructions`
     - `ExpectedOutputSchema` or simple output guidance
     - `Status`
   - `ConsolidatedResult`
     - `Summary`
     - `ContributingAgents[]`
     - `SubtaskResults[]`
     - `SourceAttribution[]`

   Persist these in existing task payload fields if that is the established pattern, preferably:
   - parent task `input_payload` contains coordination request/plan
   - parent task `output_payload` contains consolidated result
   - child tasks `input_payload` contain delegated instructions
   - child tasks `output_payload` contain worker result + attribution metadata

   If the codebase already has a better place for orchestration metadata, use that instead.

3. **Extend task model for explicit subtask linkage**
   Ensure the task model supports:
   - `parent_task_id`
   - child tasks assigned to worker agents
   - status tracking from parent to children
   - parent task remaining the anchor for final consolidated output

   If already present in schema/domain, wire it through application logic rather than changing schema unnecessarily.

4. **Add a bounded multi-agent coordinator service**
   Create or extend a service in the orchestration subsystem, e.g.:
   - `IMultiAgentCoordinator`
   - `MultiAgentCoordinator`

   Responsibilities:
   - accept a parent task and coordinator agent context
   - generate an explicit plan
   - validate the plan against guardrails
   - create child tasks
   - invoke worker execution through the existing single-agent orchestration path
   - collect child outputs
   - consolidate final response

   Important constraints:
   - no worker may create further worker subtasks in this flow unless explicitly allowed by a hardcoded depth check
   - default depth should be `1`
   - fan-out should be capped to a small number, e.g. `3-5`
   - runtime should be bounded; if async execution already exists, use it, otherwise keep the first implementation synchronous but still bounded

5. **Plan generation behavior**
   Implement a deterministic first version. Do not build uncontrolled autonomous planning.

   Preferred approach:
   - coordinator receives a request plus available worker agents
   - coordinator selects relevant agents based on role/department/capability already present in agent config
   - coordinator creates explicit subtasks with clear instructions
   - if no decomposition is needed, fall back to existing single-agent handling or a single subtask

   Keep planning logic simple and testable:
   - use explicit heuristics and/or structured LLM output if the orchestration layer already supports structured generation
   - reject malformed plans
   - require every subtask to name a concrete assigned agent

6. **Reuse the shared single-agent orchestration pipeline for workers**
   Each child task should be executed by the existing orchestration engine for a single agent, not by bespoke worker logic.

   For each child task:
   - resolve assigned agent
   - build runtime context
   - execute within existing policy/tool boundaries
   - persist output payload and rationale summary
   - mark task status appropriately

   This preserves the architecture decision of one shared orchestration engine with configurable agents.

7. **Implement consolidation**
   After worker subtasks complete:
   - gather child task outputs
   - build a consolidated parent result
   - include per-agent attribution in a structured way
   - persist a concise rationale summary on the parent task
   - ensure the final response references contributing agents and their sub-results

   The final output should clearly preserve:
   - which agent contributed what
   - summary of each contribution
   - overall recommendation/answer

8. **Guardrails and loop prevention**
   Add explicit protections:
   - reject plans with duplicate/empty agent assignments
   - reject plans exceeding max fan-out
   - reject nested delegation beyond allowed depth
   - prevent worker tasks from re-entering coordinator flow by default
   - preserve tenant scoping on all task/agent lookups
   - ensure paused/restricted/archived agents are not assigned if existing rules already prohibit assignment

   If there is an existing policy/guardrail engine, integrate these checks there or immediately adjacent to coordinator execution.

9. **API/application entry point**
   If there is already a task execution or orchestration command/endpoint, extend it to support coordinator execution for cross-functional requests.
   Otherwise add a minimal application command and API surface that can:
   - create a parent task
   - trigger coordinator decomposition
   - return parent task plus child task references and consolidated output when complete

   Keep the API contract aligned with existing patterns.

10. **Persistence and migration**
   Only add schema changes if required.
   Prefer existing columns:
   - `tasks.parent_task_id`
   - `tasks.input_payload`
   - `tasks.output_payload`
   - `tasks.rationale_summary`

   If schema changes are necessary, keep them minimal and consistent with the project’s migration strategy.

11. **Testing**
   Add tests for:
   - coordinator decomposes one parent request into multiple explicit subtasks
   - child tasks are linked to parent task
   - worker outputs are consolidated into parent output
   - attribution by agent is preserved
   - fan-out limit is enforced
   - nested delegation/loop prevention is enforced
   - invalid or unavailable agent assignment is rejected safely

   Prefer integration tests where possible, with focused unit tests for plan validation logic.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify the main scenario manually or via integration test:
   - create or trigger a cross-functional parent task
   - confirm an explicit plan is produced
   - confirm multiple child tasks are created with `parent_task_id` set
   - confirm each child task is assigned to a specific worker agent
   - confirm child task outputs are persisted
   - confirm parent task output contains a consolidated result with per-agent attribution

4. Verify guardrails:
   - attempt a plan exceeding max fan-out and confirm rejection
   - attempt nested delegation from a worker task and confirm it is blocked
   - attempt assignment to an invalid/paused/archived agent and confirm safe failure

5. Verify no regression to single-agent orchestration:
   - run an existing single-agent task path
   - confirm it still executes normally

6. If migrations were added:
   - apply them using the repo’s established process
   - verify schema matches code expectations

# Risks and follow-ups
- **Risk: unclear existing orchestration abstractions**
  - Mitigation: inspect and extend current single-agent pipeline rather than inventing a new one.

- **Risk: overusing JSON payloads without clear contracts**
  - Mitigation: define strongly typed DTOs/models in application/domain and serialize them consistently.

- **Risk: coordinator accidentally enables recursive agent loops**
  - Mitigation: hard-cap depth to 1 for this task and explicitly block worker-initiated coordination.

- **Risk: task status aggregation becomes inconsistent**
  - Mitigation: define simple parent status rules, e.g. parent is `in_progress` while children run, `completed` when consolidation succeeds, `failed` if planning or consolidation fails.

- **Risk: policy enforcement bypass**
  - Mitigation: all worker execution must go through the existing shared orchestration and tool policy path.

Follow-ups after this task:
- add richer audit events for coordination plans and consolidation lineage
- expose coordinator plan and child task graph in web UI
- support async/background execution for long-running multi-agent coordination
- add approval integration for coordinator-generated plans when thresholds require it
- add richer capability-based agent selection instead of simple role heuristics