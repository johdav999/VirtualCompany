# Goal

Implement backlog task **TASK-11.4.5 — Use manager-worker pattern only, not free-form chatter** for story **ST-504 Manager-worker multi-agent collaboration** in the existing .NET solution.

The coding agent should update the orchestration design and implementation so that multi-agent collaboration is **strictly coordinator-driven**:

- one manager/coordinator creates an explicit plan
- workers receive bounded subtasks
- workers do not engage in open-ended agent-to-agent conversation
- final output is consolidated back through the manager
- safeguards prevent uncontrolled loops, recursive chatter, excessive fan-out, and unbounded runtime

This task should align with the architecture decision: **“Manager-worker multi-agent pattern”** and explicitly enforce **no free-form chatter loops**.

# Scope

In scope:

- Add or refine orchestration domain/application contracts for manager-worker collaboration
- Ensure multi-agent execution is represented as:
  - parent task or orchestration request
  - explicit subtask plan
  - worker assignments
  - bounded execution metadata
  - consolidated final result
- Prevent direct free-form worker-to-worker chat as an orchestration mechanism
- Add guardrails for:
  - max fan-out
  - max depth / no recursive uncontrolled delegation
  - max runtime / iteration count
  - allowed communication path = manager -> worker -> manager
- Persist enough metadata for auditability and future explainability
- Add tests covering allowed and denied collaboration patterns

Out of scope unless already partially implemented and required to complete this task:

- Full UI for visualizing multi-agent plans
- New external integrations
- Mobile-specific changes
- Broad redesign of unrelated single-agent chat flows
- Arbitrary autonomous multi-agent conversation systems

If the codebase does not yet contain a multi-agent coordinator, implement the minimum vertical slice in the appropriate application/infrastructure layers so the pattern is explicit and testable.

# Files to touch

Prioritize these projects and likely areas based on the solution structure:

- `src/VirtualCompany.Application`
  - orchestration services
  - task/workflow coordination handlers
  - DTOs/commands/results for multi-agent planning and execution
  - policy/guardrail services
- `src/VirtualCompany.Domain`
  - domain models/value objects/enums for collaboration plan, subtask, execution limits, delegation rules
- `src/VirtualCompany.Infrastructure`
  - persistence for orchestration/task metadata if needed
  - implementations of coordinator execution services
- `src/VirtualCompany.Api`
  - only if an API contract or endpoint must be updated to invoke the new bounded collaboration flow
- `tests/VirtualCompany.Api.Tests`
  - integration/API tests if endpoints are involved
- add unit/integration tests in the appropriate test projects already present in the repo

Also inspect:

- `README.md`
- any existing orchestration, task, workflow, approval, audit, or tool execution code
- any existing agent chat / communication abstractions that might currently allow agent-to-agent free-form messaging

Avoid touching unless necessary:

- `src/VirtualCompany.Mobile`
- `src/VirtualCompany.Web` UI beyond minimal wiring
- archived migration docs unless needed for reference

# Implementation plan

1. **Inspect current orchestration and task model**
   - Find existing implementations for:
     - agent chat
     - task assignment
     - workflow execution
     - tool execution
     - audit/event persistence
     - any multi-agent or delegation logic
   - Determine whether multi-agent collaboration already exists as:
     - direct message passing
     - task delegation
     - workflow steps
     - ad hoc orchestration logic
   - Reuse existing task/workflow primitives where possible instead of inventing a parallel system.

2. **Define the manager-worker collaboration contract**
   - Introduce or refine explicit types for:
     - collaboration request
     - manager plan
     - worker subtask
     - consolidation result
     - execution limits / guardrails
   - Recommended shape:
     - parent orchestration request references a parent task/workflow
     - manager produces a structured plan with named worker assignments
     - each subtask includes:
       - assigned agent id
       - objective
       - structured input/context
       - expected output shape
       - status
       - parent reference
     - final result includes:
       - consolidated response
       - per-worker contribution summaries
       - source attribution by agent
   - Ensure the contract makes free-form chatter impossible by design.

3. **Enforce communication topology**
   - Implement a rule that worker agents cannot directly open arbitrary conversations with other worker agents during multi-agent execution.
   - Allowed pattern:
     - manager delegates to worker
     - worker returns structured result to manager
     - manager consolidates
   - Disallow:
     - worker -> worker free-form messaging
     - recursive worker spawning without explicit manager approval
     - uncontrolled back-and-forth loops
   - If there is an existing communication module abstraction, add a collaboration mode or policy that restricts routing accordingly.

4. **Add bounded execution guardrails**
   - Implement configurable defaults for:
     - maximum worker fan-out per collaboration
     - maximum delegation depth
     - maximum orchestration iterations / handoffs
     - maximum runtime duration
   - Default to conservative values.
   - On violation:
     - stop execution safely
     - mark result as blocked/failed/limited as appropriate
     - return a safe explanation
     - emit audit/diagnostic metadata
   - Prefer explicit policy objects or options classes over scattered constants.

5. **Integrate with task/workflow backbone**
   - Link collaboration to existing `tasks` / `parent_task_id` / `workflow_instance_id` concepts where available.
   - Each worker assignment should be represented as an explicit subtask or equivalent persisted execution record.
   - Ensure statuses are trackable and queryable.
   - Preserve architecture intent from ST-401/ST-402/ST-404:
     - explicit work tracking
     - bounded orchestration
     - reliable execution

6. **Update coordinator execution flow**
   - Implement or refine a `MultiAgentCoordinator`-style service in the application layer.
   - Expected flow:
     1. validate request and tenant/agent scope
     2. choose manager/coordinator
     3. build explicit plan
     4. validate plan against guardrails
     5. create subtask records
     6. execute worker subtasks
     7. collect structured worker outputs
     8. consolidate through manager
     9. persist final artifacts and attribution
   - Worker outputs should be structured and concise; do not introduce raw chain-of-thought persistence.

7. **Prevent free-form chatter in prompts/contracts**
   - Review prompt-building or orchestration instructions for multi-agent flows.
   - Remove or replace any language that encourages agents to “discuss”, “chat freely”, or “continue talking until consensus”.
   - Replace with explicit instructions such as:
     - manager creates plan
     - workers complete assigned subtask only
     - workers return structured findings
     - only manager synthesizes final answer
   - If prompts are config-driven, update the relevant templates or builders.

8. **Persist audit-friendly metadata**
   - Ensure the system records enough information for later explainability:
     - manager agent id
     - worker agent ids
     - parent task/workflow id
     - subtask linkage
     - plan summary
     - guardrail decisions
     - final attribution by contributing agent
   - Use existing audit/task/tool execution patterns where possible.
   - Do not store chain-of-thought; store rationale summaries only.

9. **Add tests**
   - Add unit and/or integration tests for at least:
     - manager can create explicit worker plan
     - worker subtasks are linked to parent task/workflow
     - final result consolidates worker outputs with attribution
     - worker-to-worker free-form communication is rejected or impossible
     - fan-out limit is enforced
     - depth/recursion limit is enforced
     - runtime/iteration guardrail is enforced where practical
   - Prefer deterministic tests around application services and policies.

10. **Keep implementation aligned with existing architecture**
   - Respect modular monolith boundaries.
   - Keep orchestration logic out of controllers/UI.
   - Use typed contracts, not direct DB access from orchestration logic.
   - Preserve tenant scoping and policy enforcement throughout.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If API endpoints were changed, run relevant API/integration tests and verify:
   - multi-agent request creates explicit subtasks
   - response is consolidated through manager
   - no direct worker-to-worker chatter path is available

4. Manually inspect the implementation for these invariants:
   - no free-form multi-agent conversation loop exists
   - collaboration requires an explicit plan
   - worker outputs return to manager, not to other workers
   - fan-out/depth/runtime limits are enforced in code, not just documented
   - parent/child task linkage is persisted or represented explicitly
   - attribution by contributing agent is included in final output

5. Verify no raw reasoning is persisted:
   - only summaries, structured outputs, and audit-safe metadata

# Risks and follow-ups

- **Risk: existing chat abstractions may implicitly allow agent-to-agent messaging**
  - Mitigation: add explicit collaboration routing restrictions or a dedicated manager-worker execution path.

- **Risk: current data model may not yet have a dedicated collaboration-plan table**
  - Mitigation: use existing task/subtask/workflow structures first; only add persistence changes if necessary and keep them minimal.

- **Risk: prompt changes alone are insufficient**
  - Mitigation: enforce the pattern in application logic and policies, not only in prompt text.

- **Risk: recursive delegation may still occur indirectly**
  - Mitigation: track delegation depth and origin in execution context and reject unauthorized nested delegation.

- **Risk: acceptance criteria are not separately specified for TASK-11.4.5**
  - Mitigation: treat ST-504 plus architecture decision #9 and backlog note “Use manager-worker pattern only, not free-form chatter” as the effective acceptance target.

Follow-ups to note in code comments or backlog notes if not completed here:

- expose collaboration plan and attribution in web UI
- add richer audit/explainability views for multi-agent plans
- make guardrail limits tenant-configurable through policy settings
- add metrics/telemetry for fan-out, runtime, blocked loops, and consolidation quality