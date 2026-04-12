# Goal
Implement backlog task **TASK-11.2.1** for **ST-502 — Shared orchestration pipeline for single-agent tasks** by adding the orchestration-layer capability to **resolve the target agent, determine intent/task type, and assemble runtime context** for a single-agent execution request.

This work should establish the **application/service-layer foundation** for the shared orchestration engine in the .NET modular monolith, without coupling orchestration logic to HTTP controllers or UI concerns.

The implementation must align with the architecture and story expectations:
- one shared orchestration engine for all agents
- tenant-scoped execution
- configurable agent behavior from persisted agent records
- runtime context composed from agent config + company context + task/request context
- correlation-friendly structured orchestration inputs/outputs
- designed to support later prompt building, retrieval, policy checks, tool execution, and audit persistence

# Scope
In scope:
- Add or extend orchestration application contracts/models for:
  - orchestration request
  - resolved target agent
  - resolved intent/task type
  - runtime context
  - orchestration resolution result
- Implement a **single-agent orchestration resolver service** that:
  - validates required tenant/company context
  - resolves the target agent from explicit identifiers and/or supported conversation/task context
  - determines intent/task type using deterministic rules first
  - builds a normalized runtime context object for downstream prompt builder/tool executor use
- Use existing domain/application patterns where possible
- Keep logic testable and deterministic
- Add unit tests for the resolver behavior

Out of scope unless already trivial and necessary:
- full prompt generation
- LLM invocation
- semantic retrieval implementation
- tool execution
- approval creation
- audit persistence beyond placeholders/contracts
- multi-agent coordination
- UI changes
- broad API redesign

Assumptions to preserve:
- shared-schema multi-tenancy with `company_id` enforcement
- orchestration service belongs in application/infrastructure layers, not UI
- final output of this task is a reusable orchestration resolution component that later pipeline steps can consume

# Files to touch
Touch only the minimum necessary files, likely within these projects:
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests` or the most appropriate existing test project for application/service tests

Likely areas to inspect before editing:
- existing task, agent, conversation, and company models/contracts
- any existing CQRS handlers or services related to chat/tasks/orchestration
- dependency injection registration in API/Infrastructure
- shared result/error abstractions
- existing tenant/company context abstractions
- existing repository interfaces for agents/tasks/conversations/companies

Prefer adding files in coherent folders such as:
- `Orchestration/Contracts/...`
- `Orchestration/Services/...`
- `Orchestration/Models/...`
- `Orchestration/Interfaces/...`

If the solution already has naming conventions, follow them exactly instead of inventing new structure.

# Implementation plan
1. **Inspect current architecture in code**
   - Find existing abstractions for:
     - tenant/company context
     - agent lookup
     - task lookup
     - conversation lookup
     - application service registration
     - result/error handling
   - Reuse existing patterns and avoid introducing parallel infrastructure.

2. **Define orchestration resolution contracts**
   Create or extend strongly typed models for the shared orchestration pipeline. At minimum include:
   - `OrchestrationRequest`:
     - `CompanyId`
     - correlation/request id if project conventions support it
     - optional `AgentId`
     - optional `TaskId`
     - optional `ConversationId`
     - optional user message/input text
     - actor metadata if available
     - optional explicit task type / intent hint
   - `ResolvedAgentContext`:
     - agent id
     - company id
     - display name
     - role name
     - department
     - status
     - autonomy level
     - role brief
     - tool permissions/scopes references as needed
   - `ResolvedIntent`:
     - normalized intent name
     - task type
     - source of resolution (`explicit`, `task`, `conversation`, `heuristic`, etc.)
     - confidence or determinism marker if appropriate
   - `RuntimeContext`:
     - company context summary
     - actor/request context
     - task context summary
     - conversation context summary
     - agent context
     - policy-relevant metadata placeholders
   - `OrchestrationResolutionResult`:
     - resolved agent
     - resolved intent
     - runtime context
     - correlation metadata

   Keep these models prompt-builder friendly and serialization-safe.

3. **Implement target agent resolution**
   Add a service such as `ISingleAgentOrchestrationResolver` / `IOrchestrationResolver` with a concrete implementation.

   Resolution rules should be deterministic and conservative:
   - require `CompanyId`
   - if `AgentId` is explicitly provided:
     - load agent by id + company id
     - fail if not found or not tenant-scoped
   - if `TaskId` is provided and no explicit agent:
     - load task by id + company id
     - use `assigned_agent_id` if present
   - if `ConversationId` is provided and no explicit agent:
     - resolve only if conversation type clearly maps to a direct agent conversation and the mapping exists in current model
   - if multiple possible agents exist or mapping is ambiguous:
     - return a clear domain/application error rather than guessing
   - reject paused/restricted/archived agents for execution if that matches current business rules; at minimum reject archived and any status already prohibited elsewhere
   - ensure all lookups are company-scoped

4. **Implement intent/task type resolution**
   Use deterministic precedence, not LLM classification.
   Suggested precedence:
   - explicit request task type / intent hint
   - task entity `type` if `TaskId` exists
   - conversation/channel type mapping if available
   - fallback heuristic based on request shape, e.g.:
     - direct chat message => `chat`
     - task-backed execution => `task_execution`
     - otherwise `general_agent_request`
   Normalize to a small stable set of values suitable for downstream branching.
   Include the resolution source in the result.

5. **Build runtime context**
   Assemble a normalized runtime context object from available data:
   - company:
     - company id
     - timezone
     - currency
     - language
     - compliance region
   - agent:
     - identity and role configuration needed downstream
   - task:
     - task id
     - title
     - description
     - priority
     - status
     - input payload
     - parent/workflow references if available
   - conversation:
     - conversation id
     - channel type
     - subject
     - recent message summary placeholder if easy to support, otherwise omit
   - actor/request:
     - initiating actor type/id if available
     - raw user input
     - correlation id
     - timestamp
   - policy placeholders:
     - autonomy level
     - tool permission snapshot references
     - data scope references

   Do not perform prompt assembly here. This service should produce structured context only.

6. **Error handling**
   Return structured failures for cases like:
   - missing company context
   - agent not found
   - task not found
   - conversation not found
   - no resolvable target agent
   - ambiguous target agent
   - agent status not executable
   - cross-tenant access attempt

   Use existing result/error conventions in the codebase. Avoid throwing generic exceptions for expected business failures.

7. **Dependency injection**
   Register the resolver service in the appropriate composition root.
   Keep dependencies narrow and interface-driven.

8. **Tests**
   Add focused tests covering:
   - resolves agent from explicit `AgentId`
   - resolves agent from assigned task when `AgentId` absent
   - fails when task has no assigned agent
   - fails when explicit agent belongs to another company
   - resolves intent from explicit hint over task type
   - resolves fallback intent for direct chat/general request
   - builds runtime context with company + agent + task fields populated
   - rejects non-executable agent status according to implemented rule
   - returns deterministic error on ambiguous conversation mapping if applicable

   Prefer unit tests around the resolver service with fakes/mocks over broad integration tests unless the project already standardizes otherwise.

9. **Keep future pipeline extension points obvious**
   Design the result so later tasks can plug in:
   - prompt builder
   - context retriever
   - policy guardrail engine
   - tool executor
   - audit/event persistence

   Add small comments only where necessary to clarify extension points.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify resolver behavior through tests and, if applicable, existing application-level entry points:
   - explicit agent resolution is company-scoped
   - task-based resolution uses assigned agent
   - intent precedence is deterministic
   - runtime context contains normalized downstream-friendly data
   - expected business failures return structured errors, not unhandled exceptions

4. If DI registration was added, ensure application startup/build succeeds without service registration errors.

# Risks and follow-ups
- **Risk: existing codebase may already contain partial orchestration abstractions.**
  - Reuse and extend them instead of duplicating concepts.

- **Risk: conversation-to-agent mapping may not yet exist in the schema/model.**
  - If absent, do not invent fragile persistence changes unless necessary; support explicit agent and task-based resolution first, and leave conversation resolution as a safe no-op/error path.

- **Risk: agent status execution rules may already be defined elsewhere.**
  - Follow existing business rules if present; otherwise implement the most conservative rule and document it in code/tests.

- **Risk: company context data may be spread across modules.**
  - Keep runtime context minimal and reliable rather than over-fetching.

Follow-up items likely after this task:
- prompt builder implementation
- retrieval/context enrichment integration
- policy guardrail integration
- tool execution pipeline
- orchestration audit/correlation persistence
- API/application handlers that invoke this resolver as the first stage of ST-502