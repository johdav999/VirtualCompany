# Goal
Implement `TASK-11.4.6` for `ST-504 — Manager-worker multi-agent collaboration` by adding hard guardrails that bound multi-agent execution across:
- **fan-out**: maximum number of worker subtasks a coordinator can create
- **depth**: maximum nesting level of manager→worker delegation
- **runtime**: maximum allowed elapsed time for a collaboration run

The implementation must ensure manager-worker collaboration remains **explicitly planned and bounded**, preventing uncontrolled loops or runaway orchestration. The solution should fit the existing **.NET modular monolith** architecture and preserve **tenant-scoped, auditable orchestration behavior**.

# Scope
In scope:
- Add bounded-execution policy/config model for multi-agent collaboration.
- Enforce limits in the orchestration / multi-agent coordinator path.
- Prevent creation of subtasks when fan-out or depth limits are exceeded.
- Stop or short-circuit collaboration when runtime budget is exhausted.
- Persist enough metadata for auditability and troubleshooting.
- Add tests covering allowed and denied/terminated scenarios.
- Keep behavior deterministic and safe by default.

Out of scope unless already required by existing code patterns:
- New UI for editing these limits.
- Broad workflow engine redesign.
- Free-form agent-to-agent chat.
- Changes to unrelated single-agent orchestration paths, except where shared abstractions require minimal updates.
- Full approval UX changes.

Assumptions to follow:
- Prefer configuration-driven defaults with conservative values.
- If no explicit config exists, use **default-deny / safe fallback** behavior for multi-agent expansion.
- Reuse existing task/workflow/audit/tool execution patterns where possible.
- Preserve tenant isolation and correlation IDs.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- Domain models / value objects for orchestration limits
- Application services / handlers for multi-agent coordination
- Policy / guardrail engine components
- Task creation / subtask linking logic
- Audit event creation logic
- Configuration/options classes
- DI registration / startup wiring
- Tests for orchestration guardrails

Potential concrete targets based on repository structure and naming you discover:
- Multi-agent coordinator service
- Orchestration service / pipeline
- Policy guardrail engine
- Task command handlers for subtask creation
- Workflow runner / background execution path if collaboration can continue asynchronously
- Shared contracts / DTOs for orchestration context and results

Do not invent new layers if existing ones already provide the right extension points.

# Implementation plan
1. **Discover current orchestration flow**
   - Find the implementation for:
     - single-agent orchestration
     - manager-worker coordination
     - task/subtask creation
     - policy guardrail evaluation
     - audit event persistence
   - Trace how a parent request becomes delegated subtasks and how results are consolidated.
   - Identify the narrowest enforcement point that all multi-agent delegation passes through.

2. **Introduce a bounded collaboration policy model**
   - Add a domain/application-level model representing collaboration limits, for example:
     - `MaxFanOut`
     - `MaxDepth`
     - `MaxRuntime`
   - Include current execution state/context fields as needed, for example:
     - current depth
     - delegated worker count
     - started-at timestamp
     - deadline / remaining budget
   - Prefer immutable request/context objects where consistent with the codebase.

3. **Define safe defaults**
   - Add configuration/options for default collaboration limits.
   - Use conservative defaults appropriate for manager-worker orchestration.
   - If agent-specific or tenant-specific config already exists, integrate with it; otherwise use application defaults.
   - Ensure missing/invalid config does not allow unbounded execution.

4. **Enforce fan-out limits**
   - Before creating worker subtasks from a coordinator plan, validate the number of requested delegations.
   - If the plan exceeds the allowed fan-out:
     - either reject the plan entirely, or truncate only if that matches existing product behavior and remains deterministic
   - Prefer explicit failure with a structured reason unless the current orchestration contract clearly expects partial execution.
   - Record the decision in:
     - task/workflow result metadata
     - audit event(s)
     - logs with correlation and tenant context

5. **Enforce depth limits**
   - Track delegation depth from the root collaboration request.
   - Prevent a worker from recursively spawning additional workers beyond the configured maximum depth.
   - Ensure depth is propagated through task/subtask or orchestration context, not inferred unreliably from prompt text.
   - If depth is exceeded, return a safe structured outcome indicating bounded-collaboration denial.

6. **Enforce runtime limits**
   - Add runtime budget tracking for the collaboration run.
   - Use a deadline or elapsed-time check that works for both synchronous and background execution paths.
   - Before starting each new delegation or consolidation step, verify remaining runtime budget.
   - If runtime is exhausted:
     - stop creating new subtasks
     - mark the collaboration as timed out / budget exhausted
     - preserve partial completed results only if existing orchestration semantics support that safely
   - Avoid abrupt cancellation that leaves inconsistent task state unless the codebase already has a safe cancellation pattern.

7. **Surface structured outcomes**
   - Extend orchestration result types as needed so callers can distinguish:
     - success
     - blocked by fan-out limit
     - blocked by depth limit
     - terminated due to runtime budget exhaustion
   - Include concise user-facing rationale summaries without exposing chain-of-thought.
   - Preserve source attribution by agent for any completed sub-results.

8. **Persist auditability**
   - Create or extend audit events for bounded-collaboration decisions.
   - Include structured metadata such as:
     - limit type
     - configured threshold
     - observed value
     - parent task/workflow/correlation ID
   - If tool execution or task records already store policy decision metadata, reuse that pattern.

9. **Update task/workflow state transitions**
   - Ensure blocked/terminated collaboration updates task status consistently.
   - Use existing statuses where possible, such as:
     - `failed`
     - `blocked`
     - `completed` with partial output only if already supported and safe
   - Keep parent-child task linkage intact for any subtasks that were created before a runtime stop.

10. **Add tests**
   - Unit and/or integration tests should cover at minimum:
     - coordinator plan within limits succeeds
     - plan exceeding max fan-out is denied
     - nested delegation beyond max depth is denied
     - runtime budget exhaustion prevents further delegation
     - audit metadata is written for bounded-execution decisions
     - tenant/correlation context remains intact
   - Prefer tests at the application/service level where orchestration behavior is exercised realistically.

11. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - domain/application for policy and orchestration rules
     - infrastructure for persistence/config wiring
     - API only for transport concerns if any endpoint contract changes are needed
   - Do not let models call external systems directly.
   - Keep the manager-worker pattern explicit; do not introduce free-form agent chatter.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update targeted automated tests for:
   - fan-out limit enforcement
   - depth limit enforcement
   - runtime limit enforcement
   - audit/event persistence for limit violations

4. If there are API or orchestration integration tests, verify:
   - parent task creates only allowed subtasks
   - over-limit requests return safe structured responses
   - timed-out collaboration does not continue spawning work

5. Manually review for:
   - tenant scoping on any persisted records
   - correlation IDs flowing through logs/audit/task records
   - no unbounded loops or recursive delegation paths remain

6. If configuration was added, verify:
   - defaults are loaded correctly
   - invalid config fails safely
   - missing config does not permit unlimited collaboration

# Risks and follow-ups
- **Unknown current implementation shape**: multi-agent coordination may be only partially implemented; adapt this task to the existing orchestration seams rather than building a large new subsystem.
- **Runtime enforcement complexity**: if background workers already own long-running execution, deadline propagation may require touching worker/job contracts.
- **Partial-result semantics**: be careful not to mark timed-out collaborations as successful unless the existing domain model explicitly supports partial completion.
- **Config duplication risk**: avoid scattering limits across agent config, app settings, and workflow definitions without a clear precedence rule.
- **Audit consistency**: ensure bounded-execution denials are captured as business audit events, not only technical logs.
- **Follow-up recommendation**: after backend enforcement, consider a later task to expose these limits in agent/workflow configuration and admin diagnostics UI.