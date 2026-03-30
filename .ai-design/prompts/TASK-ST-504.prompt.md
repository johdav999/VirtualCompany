# Goal
Implement backlog task **TASK-ST-504 — Manager-worker multi-agent collaboration** for the .NET modular monolith so the platform can coordinate a cross-functional request through an explicit **manager-worker orchestration flow** and return **one consolidated response**.

This implementation must align with story **ST-504 — Manager-worker multi-agent collaboration** and the architecture/backlog provided.

Deliver a production-ready vertical slice that:
- decomposes a parent request into explicit subtasks for multiple agents,
- persists and links those subtasks to a parent task and/or workflow context,
- executes collaboration through the shared orchestration subsystem,
- consolidates worker outputs into a single final response,
- attributes contributions by agent,
- enforces bounded collaboration with hard limits to prevent uncontrolled loops.

# Scope
Implement only what is necessary for ST-504 in the current solution structure and stack.

Include:
- Domain and application support for a **manager-worker collaboration plan**
- Explicit parent/child task linkage for multi-agent execution
- A **Multi-Agent Coordinator** service in the orchestration subsystem
- Bounded decomposition rules:
  - explicit plan required
  - max fan-out
  - max depth of delegation
  - max runtime / cancellation support
  - no free-form recursive agent chatter
- Consolidation of worker outputs into one response with:
  - per-agent attribution
  - rationale summaries per contributing agent
  - safe structured output for downstream UI/API use
- Persistence of collaboration metadata sufficient for audit and later explainability
- Integration with existing/shared orchestration pipeline rather than bespoke per-agent logic

Do not overbuild. Avoid:
- free-form agent-to-agent chat threads,
- autonomous recursive planning beyond manager-worker,
- new microservices,
- broad workflow-builder UX,
- mobile-specific work,
- speculative external integrations.

If some prerequisite pieces from ST-401/ST-502/ST-503 are incomplete, implement the minimum internal abstractions needed for ST-504 without rewriting unrelated areas.

# Files to touch
Inspect the repository first and adjust to actual code layout, but expect to touch files in these areas:

- `src/VirtualCompany.Domain`
  - task entities/value objects/enums
  - orchestration/collaboration domain models
  - guardrail/limit models if domain-owned
- `src/VirtualCompany.Application`
  - commands/queries/handlers for starting multi-agent collaboration
  - orchestration interfaces
  - DTOs/contracts for collaboration plan, worker assignment, consolidation result
  - validation logic
- `src/VirtualCompany.Infrastructure`
  - orchestration service implementation
  - persistence mappings/configurations
  - repository implementations
  - background execution support if collaboration runs asynchronously
- `src/VirtualCompany.Api`
  - endpoint/controller for initiating manager-worker collaboration or extending existing task/chat endpoint
  - request/response contracts if API-owned
- `src/VirtualCompany.Web`
  - only minimal changes if needed to surface or test the feature; prefer API/application completion first
- tests in the corresponding test projects
  - unit tests for decomposition/limits/consolidation
  - integration tests for persistence and orchestration flow

Also review:
- `README.md`
- solution and project references
- existing task/workflow/orchestration/policy/audit code paths

# Implementation plan
1. **Inspect existing architecture in code**
   - Identify current implementations for:
     - tasks and parent-child task relationships,
     - agent resolution,
     - orchestration pipeline,
     - tool execution policy checks,
     - audit/event persistence,
     - workflow linkage,
     - API patterns and CQRS conventions.
   - Reuse existing abstractions wherever possible.
   - Document any missing prerequisite in code comments/TODOs only when necessary.

2. **Add collaboration domain model**
   Introduce minimal domain concepts to represent manager-worker execution, for example:
   - `CollaborationPlan`
   - `CollaborationStep` or `WorkerSubtask`
   - `CollaborationLimits`
   - `CollaborationResult`
   - `AgentContribution`
   - optional status enum for plan lifecycle

   Requirements:
   - plan must be explicit and persisted or reconstructable from persisted task metadata,
   - each worker subtask must reference:
     - parent task id,
     - assigned agent id,
     - objective/instructions,
     - status,
     - sequence/order if relevant,
   - final result must preserve per-agent attribution and rationale summary.

   Prefer storing flexible collaboration metadata in JSONB where that matches the architecture and existing patterns.

3. **Extend task model for manager-worker linkage**
   Ensure the task model supports:
   - parent task with multiple child subtasks,
   - child tasks assigned to worker agents,
   - task metadata indicating collaboration role:
     - manager/coordinator task,
     - worker subtask,
     - consolidation/finalization step if needed.
   - linkage to workflow instance when present.

   If parent-child support already exists, reuse it and only add missing metadata.

4. **Define application contracts**
   Add application-layer contracts for:
   - starting a multi-agent collaboration request,
   - representing the generated plan,
   - worker execution request/result,
   - consolidated response.

   Include fields such as:
   - `CompanyId`
   - initiating user/actor
   - parent task id or request payload
   - coordinator agent id
   - requested worker agent ids or selection hints
   - collaboration objective
   - limits:
     - max workers
     - max depth (must effectively remain 1 for worker delegation unless existing architecture supports a stricter bounded value)
     - timeout/runtime budget
   - final response payload with attributed contributions

5. **Implement Multi-Agent Coordinator service**
   In the shared orchestration subsystem, implement a coordinator service that:
   - accepts a parent request,
   - creates an explicit collaboration plan,
   - validates the plan against limits and policy,
   - creates worker subtasks,
   - dispatches each worker through the existing single-agent orchestration path,
   - waits for/collects results,
   - consolidates them into one final response.

   Important behavior:
   - no worker may create further uncontrolled worker loops,
   - if delegation depth is tracked, enforce it strictly,
   - fan-out must be capped,
   - duplicate/irrelevant worker assignments should be rejected,
   - partial failures should be surfaced clearly in the consolidated result.

   Prefer deterministic orchestration logic over open-ended LLM-driven chatter:
   - manager plans,
   - workers execute assigned subtasks,
   - manager consolidates.

6. **Bound collaboration explicitly**
   Implement hard safeguards for ST-504 notes:
   - explicit plan required before worker execution,
   - maximum fan-out configurable with safe default,
   - maximum delegation depth configurable with safe default,
   - runtime timeout/cancellation token support,
   - prevent worker-to-worker direct chatter unless already modeled as explicit task delegation through the coordinator,
   - prevent recursive self-assignment or cycles.

   At minimum, validate:
   - no agent assigned twice for the same identical subtask unless intentional and justified,
   - no child task can spawn unrestricted descendants,
   - no collaboration starts without a parent task or equivalent tracked root context.

7. **Persist collaboration and attribution data**
   Ensure persistence captures enough for audit/explainability:
   - parent task
   - child tasks
   - assigned agents
   - plan summary
   - worker outputs
   - rationale summaries
   - final consolidated output
   - source attribution by agent
   - correlation IDs across orchestration/task/tool execution if such support exists

   Reuse existing tables where possible:
   - `tasks`
   - `messages`
   - `tool_executions`
   - `audit_events`
   - `workflow_instances`
   Add only minimal schema changes needed.

8. **Consolidation output**
   Implement a final consolidated response model that includes:
   - overall answer/recommendation,
   - summary of each contributing agent,
   - attribution list,
   - linked subtask ids,
   - any blocked/failed worker outcomes,
   - concise rationale summaries only,
   - no raw chain-of-thought.

   The consolidated result should be suitable for:
   - API response,
   - task detail display,
   - future audit/explainability views.

9. **Integrate with API**
   Add or extend an API endpoint to trigger manager-worker collaboration.
   Prefer one of:
   - a task-oriented endpoint under tasks/orchestration,
   - an orchestration endpoint if that pattern already exists.

   Endpoint behavior:
   - tenant-scoped,
   - validates coordinator and worker agents belong to the company,
   - rejects paused/restricted/archived agents according to existing rules,
   - returns parent task id and consolidated result or accepted status depending on current execution model.

   Keep HTTP concerns thin; orchestration logic belongs in application/infrastructure layers.

10. **Audit and safe failure handling**
    Record business-relevant events for:
    - collaboration started,
    - plan created,
    - worker subtask created,
    - worker completed/failed,
    - consolidation completed,
    - collaboration blocked by limits/policy.

    Fail safely:
    - if one worker fails, preserve completed worker outputs,
    - mark failed child tasks appropriately,
    - reflect partial completion in parent task output,
    - do not silently retry forever,
    - do not produce fabricated contributions.

11. **Testing**
    Add tests covering at least:
    - plan decomposition creates explicit child tasks,
    - child tasks link to parent task,
    - fan-out limit enforcement,
    - depth/loop prevention,
    - consolidation includes per-agent attribution,
    - partial worker failure handling,
    - tenant scoping and invalid agent rejection,
    - paused/archived/restricted agent assignment rejection if applicable,
    - persistence of collaboration metadata.

    Prefer:
    - unit tests for coordinator logic,
    - integration tests for repository/API flow.

12. **Implementation quality constraints**
    - Follow existing solution conventions and naming.
    - Keep clean architecture boundaries.
    - Use async/cancellation correctly.
    - Keep DTOs and persistence models explicit.
    - Avoid leaking infrastructure concerns into domain/application.
    - Add concise XML/docs/comments only where they clarify non-obvious orchestration rules.
    - Do not expose chain-of-thought; store only rationale summaries.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Validate the manager-worker flow manually or via integration tests:
   - create or invoke a parent collaboration request,
   - verify explicit plan creation,
   - verify child subtasks are created and linked to the parent,
   - verify each child is assigned to the intended worker agent,
   - verify final consolidated output contains per-agent attribution and rationale summaries,
   - verify no uncontrolled recursive delegation occurs.

4. Validate bounded behavior:
   - attempt fan-out above configured limit and confirm rejection,
   - attempt invalid depth/recursive delegation and confirm rejection,
   - attempt collaboration with paused/archived/out-of-tenant agent and confirm rejection,
   - confirm timeout/cancellation is handled safely.

5. Validate persistence/audit:
   - inspect stored parent/child tasks,
   - inspect collaboration metadata in payload/JSONB fields if used,
   - inspect audit records and tool execution linkage where applicable,
   - confirm correlation IDs flow through the orchestration path if supported.

6. Validate safe partial failure behavior:
   - simulate one worker failure,
   - confirm other worker outputs remain preserved,
   - confirm parent result indicates partial completion/failure clearly,
   - confirm no fabricated consolidated success.

# Risks and follow-ups
- **Prerequisite gaps:** ST-504 depends on task lifecycle, shared orchestration, and policy-enforced execution. If ST-401/ST-502/ST-503 are incomplete, implement the thinnest compatible slice and note follow-up work.
- **Schema uncertainty:** The architecture suggests task parent linkage and JSONB metadata, but actual code may differ. Prefer additive, backward-compatible schema changes.
- **Execution model ambiguity:** If current orchestration is synchronous, keep ST-504 workable synchronously for small fan-out and add cancellation/time-budget support. If background execution already exists, integrate rather than duplicate.
- **Policy integration:** Worker subtasks may need the same pre-execution guardrails as single-agent tasks. Ensure manager-worker does not bypass ST-503 protections.
- **Consolidation quality:** Keep consolidation deterministic and structured; avoid over-reliance on unconstrained model summarization.
- **Future follow-ups:** likely next steps include richer worker selection, workflow-triggered collaboration, explainability UI, and analytics on collaboration effectiveness—but do not implement those now unless required to complete this story.