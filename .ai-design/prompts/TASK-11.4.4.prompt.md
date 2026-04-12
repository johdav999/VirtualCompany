# Goal
Implement backlog task **TASK-11.4.4** for **ST-504 Manager-worker multi-agent collaboration** so that **multi-agent collaboration is strictly bounded to explicit plans and uncontrolled agent loops are prevented**.

The coding agent should add the minimum complete implementation needed in the existing .NET solution to enforce bounded manager-worker orchestration with clear runtime limits and safe failure behavior.

This task specifically targets the story requirement:

- **Collaboration is bounded to explicit plans; uncontrolled agent loops are prevented.**

Because no separate acceptance criteria were provided for this task, derive implementation behavior from the story, notes, and architecture. The implementation must align with these architectural constraints:

- manager-worker pattern only
- no free-form agent-to-agent chatter
- explicit plan required before delegation
- bounded fan-out, depth, and runtime
- tenant-scoped orchestration
- auditable, structured behavior
- safe default-deny behavior when collaboration bounds are invalid or ambiguous

# Scope
In scope:

- Add or complete domain/application orchestration safeguards that require multi-agent collaboration to run from an **explicit plan structure**
- Enforce **hard limits** on:
  - delegation depth
  - number of subtasks / fan-out
  - total collaboration steps or iterations
  - runtime / timeout budget where applicable
- Prevent recursive or cyclic delegation patterns
- Ensure worker agents cannot freely spawn additional collaboration outside the approved plan
- Return safe failure results when limits are exceeded or plan validity fails
- Persist or emit enough structured metadata for downstream auditability/debugging
- Add automated tests covering bounded collaboration and loop prevention

Out of scope unless already partially implemented and necessary to complete this task:

- full UI for plan visualization
- broad workflow builder features
- new external integrations
- changing unrelated single-agent orchestration behavior
- speculative refactors outside the collaboration path

If the codebase already contains partial multi-agent orchestration, extend it rather than replacing it wholesale.

# Files to touch
Inspect the solution first and then touch only the files needed. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - domain models/value objects/enums for orchestration plans, delegation constraints, statuses, failure reasons
- `src/VirtualCompany.Application/**`
  - coordinator service / orchestration handlers
  - commands/queries for multi-agent task execution
  - validation logic for explicit plans and bounded execution
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings if new structured fields/entities are required
  - timeout/clock/runtime helpers if orchestration execution uses infrastructure services
- `src/VirtualCompany.Api/**`
  - only if API contracts need small updates for bounded collaboration responses
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts if used across layers
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if orchestration is exposed through API
- potentially add or update tests in application/domain test projects if they exist elsewhere in the repo

Before editing, inspect:

- existing orchestration engine implementation
- task/subtask models
- policy guardrail engine
- audit event patterns
- any existing manager-worker coordinator classes
- existing constants/options/configuration patterns

# Implementation plan
1. **Inspect current orchestration and collaboration flow**
   - Find the current implementation for:
     - coordinator / multi-agent orchestration
     - task creation and parent-child linking
     - tool/policy enforcement
     - audit/event recording
   - Identify where a manager agent currently:
     - decomposes work
     - assigns subtasks
     - receives worker outputs
     - may recursively trigger more collaboration
   - Determine whether collaboration is represented as:
     - task graph
     - workflow instance
     - in-memory execution plan
     - DTO-only orchestration contract

2. **Introduce an explicit collaboration plan model if missing**
   - Add a structured plan concept for manager-worker collaboration, such as:
     - parent task/work item id
     - coordinator agent id
     - allowed worker assignments
     - subtask list
     - max depth
     - max fan-out
     - max total steps
     - expiration / runtime budget
   - The plan should be explicit and machine-validated before execution begins.
   - If a plan model already exists, strengthen it with bounded-execution fields rather than duplicating concepts.

3. **Add plan validation**
   - Implement validation that rejects collaboration when:
     - no explicit plan exists
     - plan has zero or invalid subtasks
     - fan-out exceeds configured maximum
     - depth exceeds configured maximum
     - duplicate/cyclic delegation chain is detected
     - worker assignment is outside allowed plan participants
     - runtime budget is invalid or missing when required by current orchestration design
   - Validation should fail closed with a structured reason.

4. **Enforce bounded execution in the coordinator**
   - Update the multi-agent coordinator so execution only proceeds within the approved plan.
   - Enforce:
     - **fan-out limit**: maximum number of worker subtasks created from a parent collaboration
     - **depth limit**: workers cannot recursively create unbounded child collaborations
     - **step/iteration limit**: total collaboration actions are capped
     - **runtime limit**: stop execution when timeout/budget is exceeded
   - If workers are allowed to request further delegation, require that:
     - it is either disallowed entirely for this task, or
     - it must be explicitly represented in the original plan and still satisfy depth/fan-out constraints
   - Prefer the conservative interpretation: **workers cannot create new unplanned collaboration branches**.

5. **Prevent loops and cycles**
   - Add cycle detection for delegation chains, for example:
     - same agent repeatedly delegating back and forth
     - same task lineage revisited
     - same parent-child relationship recreated
   - Ensure the coordinator terminates safely with a bounded failure result instead of retrying indefinitely.
   - If there is a retry mechanism, distinguish:
     - transient execution failure
     - policy denial
     - collaboration loop / bounded-plan violation
   - Loop/bounds violations should be treated as non-retryable business/policy failures.

6. **Record structured outcomes**
   - Persist or emit structured metadata sufficient for audit and diagnostics, such as:
     - collaboration plan id/reference
     - actual depth reached
     - actual subtask count
     - termination reason
     - whether execution completed, was blocked, or was aborted due to bounds
   - Reuse existing audit/task/tool execution patterns where possible.
   - Do not add verbose chain-of-thought storage.

7. **Add configuration defaults**
   - Introduce conservative defaults for collaboration bounds if configuration/options patterns already exist.
   - Example categories:
     - max fan-out per collaboration
     - max delegation depth
     - max total collaboration steps
     - max runtime duration
   - Keep defaults centralized and easy to override later.
   - Do not hardcode magic numbers across multiple classes.

8. **Add tests**
   - Add automated tests for at least these cases:
     - collaboration with valid explicit plan succeeds
     - collaboration without explicit plan is rejected
     - fan-out above limit is rejected
     - recursive delegation beyond max depth is blocked
     - cyclic delegation is detected and terminated
     - worker cannot create unplanned subtasks
     - runtime/step budget exhaustion terminates safely
     - bounds violation is marked non-retryable and returns safe structured failure
   - Prefer focused unit tests for validation/coordinator logic plus integration tests if orchestration is API-driven.

9. **Keep implementation aligned with architecture**
   - Ensure:
     - tenant context is preserved
     - manager-worker only, no free-form chatter path introduced
     - orchestration remains separate from UI/controller concerns
     - typed contracts are used instead of direct DB shortcuts
     - behavior is deterministic and testable

10. **Document assumptions in code comments only where necessary**
   - Add concise comments around non-obvious guardrail logic.
   - Avoid excessive comments or unrelated cleanup.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test filters for orchestration/collaboration, run them as well.

4. Manually verify in code that:
   - multi-agent execution cannot start without an explicit plan
   - workers cannot recursively create uncontrolled collaboration
   - bounds violations terminate execution safely
   - loop/cycle conditions are non-retryable
   - parent/subtask linkage remains intact
   - structured metadata for termination reason is available for audit/debugging

5. In the final implementation summary, include:
   - what plan/bounds model was added or updated
   - where loop prevention is enforced
   - what defaults were chosen
   - what tests were added
   - any assumptions made due to current codebase structure

# Risks and follow-ups
- The codebase may not yet have a complete multi-agent coordinator; if so, implement the smallest viable bounded collaboration layer without overbuilding.
- Existing orchestration may be chat-oriented rather than task-plan-oriented; avoid introducing free-form agent messaging as a shortcut.
- If persistence changes are needed, keep schema changes minimal and aligned with current migration patterns in the repo.
- Be careful not to classify loop-prevention failures as transient retries.
- If runtime timeout enforcement is difficult in the current architecture, still implement deterministic step/depth/fan-out guards now and note timeout enforcement as a follow-up only if absolutely necessary.
- Follow-up work may include:
  - richer audit/explainability views for collaboration plans
  - UI surfacing of plan structure and termination reasons
  - configurable per-tenant/per-agent collaboration policies
  - workflow-level visualization of manager-worker execution graphs