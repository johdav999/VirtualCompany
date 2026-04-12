# Goal
Implement **TASK-ST-502 — Shared orchestration pipeline for single-agent tasks** in the existing .NET solution.

Create a **shared, application-level orchestration pipeline** that all single-agent task executions use, with no role-specific orchestration stacks. The pipeline must:
- resolve the target agent and task intent
- assemble runtime context for execution
- build a prompt input from agent/company/task/policy/tool data
- execute tools through a structured abstraction
- return a final structured result containing:
  - user-facing output
  - task artifacts
  - audit-ready metadata
  - correlation identifiers

This work should align with the architecture and backlog notes:
- modular monolith
- orchestration service separated from HTTP/UI concerns
- shared engine with configurable agents
- policy/tool execution via typed abstractions
- correlation IDs persisted across orchestration, tool, task, and audit records

There are no explicit acceptance criteria beyond the story/backlog notes, so implement a pragmatic vertical slice that establishes the shared orchestration foundation and is extensible for ST-503/ST-504 later.

# Scope
In scope:
- Add a **shared orchestration service** in the application layer for **single-agent task execution**
- Define orchestration contracts/models for:
  - orchestration request
  - resolved runtime context
  - prompt payload
  - tool invocation/result
  - final orchestration result
- Resolve:
  - agent configuration
  - task context
  - company/tenant context
  - available tools metadata
- Add a **prompt builder abstraction** that composes:
  - role instructions / role brief
  - company context
  - task input
  - memory/context placeholders or retrieved context input
  - policy/tool schema summaries
- Add a **tool executor abstraction** that returns structured results only and captures execution metadata
- Add correlation ID propagation through the orchestration pipeline
- Persist or prepare persistence for:
  - task output/rationale summary/confidence if applicable
  - tool execution metadata
  - audit-ready orchestration metadata
- Add tests for orchestration behavior and service boundaries
- Wire DI registrations

Out of scope unless already trivial in the codebase:
- full LLM provider integration if none exists yet
- multi-agent coordination
- approval workflow execution logic beyond placeholders/integration points
- UI work beyond minimal API/controller integration if needed
- full retrieval implementation if ST-304 is not yet present; use an abstraction and compose available context
- external SaaS connectors
- free-form chat orchestration beyond what is needed for single-agent task execution

If the repository already contains adjacent implementations, extend them instead of duplicating patterns.

# Files to touch
Inspect first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

## Application layer
- Add orchestration contracts/services under a coherent namespace/folder such as:
  - `Orchestration/`
  - `Agents/Orchestration/`
  - `Tasks/Orchestration/`
- Candidate new files:
  - `ISingleAgentOrchestrationService.cs`
  - `SingleAgentOrchestrationService.cs`
  - `OrchestrationRequest.cs`
  - `OrchestrationResult.cs`
  - `AgentRuntimeContext.cs`
  - `PromptBuildRequest.cs`
  - `PromptBuildResult.cs`
  - `IPromptBuilder.cs`
  - `IToolExecutor.cs`
  - `ToolExecutionRequest.cs`
  - `ToolExecutionResult.cs`
  - `IOrchestrationAuditWriter.cs` or equivalent persistence abstraction
- Update existing task/agent service handlers if there is already a task execution command/query flow

## Domain layer
- Add or extend domain models/value objects/enums only if needed for:
  - orchestration status
  - action type
  - correlation ID wrapper/value object
  - structured rationale/audit references
- Reuse existing task/agent/tool execution entities where present

## Infrastructure layer
- Implement infrastructure-backed services for:
  - prompt builder dependencies
  - tool execution persistence
  - correlation/context propagation
  - optional LLM adapter stub/fake if needed
- Update DI registration modules/extensions

## API layer
- Only if needed, expose or adapt an endpoint/handler to invoke the shared orchestration service for a task
- Ensure HTTP concerns remain thin and orchestration stays in application services

## Tests
- Add unit/integration tests around:
  - agent resolution
  - prompt composition
  - structured tool execution handling
  - correlation ID propagation
  - final result shape
  - persistence side effects where testable

Also inspect:
- `README.md`
- any existing architecture/docs folders
- existing task, agent, approval, audit, and tool execution code paths
- existing DI composition roots
- existing test conventions and fixtures

# Implementation plan
1. **Inspect the current solution structure before coding**
   - Identify existing modules for:
     - tasks
     - agents
     - audit
     - approvals
     - tool executions
     - AI/LLM integration
     - tenant/company context
   - Reuse established patterns for:
     - commands/queries
     - repositories
     - result/error handling
     - DI registration
     - logging/correlation
   - Do not introduce a parallel architecture if one already exists.

2. **Define the shared orchestration contracts**
   - Create a single-agent orchestration entry point, e.g.:
     - `ISingleAgentOrchestrationService.ExecuteAsync(...)`
   - Request should include enough to support current and future use:
     - `CompanyId`
     - `TaskId`
     - `AgentId`
     - optional initiating actor info
     - correlation ID
     - optional execution mode / intent
   - Result should include:
     - user-facing output text/content
     - structured output payload
     - rationale summary
     - confidence score if available
     - tool execution summaries
     - source/context references if available
     - correlation ID
     - status/failure reason

3. **Model the runtime context assembly**
   - Implement a runtime context object that consolidates:
     - company context
     - agent profile/config
     - task details
     - recent/retrieved context available through existing services
     - policy/tool metadata
   - Keep this deterministic and testable.
   - If retrieval services already exist, call them through abstractions.
   - If not, create a minimal interface and compose what is available now.

4. **Implement the prompt builder abstraction**
   - Add `IPromptBuilder` with a concrete implementation that produces a structured prompt payload rather than raw ad hoc strings scattered across handlers.
   - Prompt composition should include:
     - agent identity and role brief
     - company context
     - task title/description/input payload
     - relevant memory/context snippets
     - policy constraints
     - available tool schemas/capabilities
     - output formatting expectations
   - Keep the output structured so it can later support different LLM providers.
   - Avoid embedding HTTP/UI concerns.

5. **Implement the shared orchestration service**
   - Orchestration flow should roughly be:
     1. validate request
     2. resolve task and agent
     3. verify tenant/company consistency
     4. build runtime context
     5. build prompt payload
     6. invoke model/tool planning layer or current execution adapter
     7. execute tools only through `IToolExecutor`
     8. collect structured tool results
     9. build final user-facing result + task/audit artifacts
     10. persist updates and metadata
   - Keep the service role-agnostic: behavior comes from agent config, not hardcoded role branches.

6. **Implement structured tool execution abstraction**
   - Add `IToolExecutor` that accepts typed requests and returns typed results only.
   - Ensure tool execution metadata includes:
     - company ID
     - task ID
     - agent ID
     - tool name
     - action type
     - request payload
     - response payload
     - status
     - policy decision metadata placeholder if available
     - timestamps
     - correlation ID
   - If `tool_executions` persistence already exists, integrate with it.
   - If not, add the minimal persistence path consistent with current data access patterns.
   - Do not allow direct DB access from model-facing code.

7. **Persist orchestration outputs**
   - Update the task record where appropriate with:
     - output payload
     - rationale summary
     - confidence score
     - status transition if part of existing task lifecycle
   - Create or extend audit-ready persistence for orchestration execution metadata.
   - Ensure correlation ID is stored or logged consistently across:
     - orchestration execution
     - task updates
     - tool executions
     - audit events/log entries where applicable

8. **Add correlation ID propagation**
   - Reuse existing correlation/request context infrastructure if present.
   - If absent, add a lightweight application-level correlation mechanism.
   - Correlation ID should flow through:
     - orchestration request
     - logs
     - tool execution records
     - task/audit artifacts
   - Prefer not to hard-couple to HTTP context.

9. **Integrate with existing task execution entry points**
   - If there is already a command/handler/API for running a task, route it through the new shared orchestration service.
   - Keep controllers/endpoints thin.
   - Do not create a UI-specific orchestration path.

10. **Add tests**
   - Unit tests for:
     - prompt builder composition
     - runtime context assembly
     - orchestration service happy path
     - invalid tenant/agent/task combinations
     - tool execution metadata capture
     - correlation ID propagation
   - Integration tests where feasible for:
     - invoking task execution path
     - persistence of task output/tool execution metadata
   - Prefer deterministic fakes for model/tool dependencies.

11. **Document key extension points in code**
   - Add concise comments where future stories will plug in:
     - ST-503 policy enforcement before tool execution
     - ST-504 multi-agent coordination
     - ST-304 richer retrieval grounding
   - Keep comments brief and architectural, not noisy.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify the shared orchestration path compiles and is wired through DI:
   - confirm application startup resolves the orchestration service
   - confirm no missing registrations

4. Validate behavior with tests or existing endpoints:
   - a single-agent task execution resolves the correct agent and task
   - prompt builder includes role/company/task/tool context
   - tool execution returns structured results only
   - task output artifacts are produced
   - correlation ID is present through the pipeline

5. Validate persistence behavior:
   - task output/rationale/confidence updates are saved if supported by current model
   - tool execution metadata is recorded
   - audit-ready metadata is captured or prepared through the chosen abstraction

6. Validate boundaries:
   - no orchestration logic leaked into controllers/UI
   - no role-specific orchestration branches unless driven by configuration
   - no direct external/system calls bypassing the tool executor abstraction

7. If migrations are required for new persistence:
   - add them using the repository’s existing migration approach
   - ensure migration files are included in the correct project/location
   - verify local build/tests still pass

# Risks and follow-ups
- **Repository mismatch risk:** The current codebase may not yet contain task execution, audit, or tool persistence primitives. If so, implement the smallest coherent abstraction set without overbuilding.
- **LLM integration uncertainty:** If no provider abstraction exists, use a stub/fake adapter behind an interface rather than coupling orchestration directly to a vendor SDK.
- **Policy enforcement gap:** Full pre-execution guardrails belong to ST-503. For this task, leave a clean interception point so tool execution can be policy-gated next.
- **Retrieval dependency gap:** If ST-304 is incomplete, keep context retrieval behind an interface and compose available task/agent/company context now.
- **Persistence scope ambiguity:** No explicit acceptance criteria were provided, so prefer minimal but real persistence for task/tool metadata over speculative schema expansion.
- **Correlation consistency:** Be careful not to tie correlation IDs only to HTTP requests; background and internal executions must also support them.
- **Future follow-ups:**
  - integrate policy guardrail engine before tool execution
  - add richer retrieval/source reference persistence
  - support approval handoff for blocked/sensitive actions
  - extend the shared pipeline into manager-worker multi-agent orchestration
  - add audit/explainability views consuming the stored artifacts