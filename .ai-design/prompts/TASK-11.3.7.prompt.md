# Goal

Implement **TASK-11.3.7 — Start with internal tool abstractions and a small connector set** for **ST-503 Policy-enforced tool execution** in the existing .NET solution.

This task should establish the first production-ready foundation for **policy-enforced tool execution** by introducing:

- a **typed internal tool abstraction layer**
- a **small initial connector/tool set**
- orchestration-facing contracts that **never allow direct DB access or direct model-to-external-system calls**
- persistence of tool execution attempts/results into `tool_executions`
- policy decision capture for allowed/denied execution paths
- safe, user-facing failure/denial behavior

The implementation should align with the architecture’s modular monolith, clean boundaries, tenant isolation, and pre-execution guardrails.

# Scope

In scope:

- Define core abstractions for tool execution in the application/domain layers.
- Support the three action types called out by the story:
  - `read`
  - `recommend`
  - `execute`
- Introduce a **small initial tool set**, prioritizing **internal tools** over external connectors.
- Ensure tools call into domain/application services through **typed contracts**, not repositories/DbContext directly from orchestration.
- Add policy-aware execution flow that can:
  - evaluate a tool request before execution
  - persist allowed executions
  - persist denied executions where appropriate
  - return structured results and safe explanations
- Add or extend persistence model/mappings for `tool_executions` if not already present.
- Add tests for policy-enforced execution behavior and typed tool boundaries.

Suggested initial small connector/tool set:

1. **Task read tool**
   - Read/list task details for the current tenant and scoped agent context.
2. **Task update/status tool**
   - Limited execute action for task status updates through application commands.
3. **Approval creation/request tool**
   - Internal tool to create or route approval requests when policy requires escalation.
4. **Knowledge search tool**
   - Read-only internal retrieval tool returning structured references.

If the codebase already has adjacent orchestration/policy components, integrate with them rather than duplicating them.

Out of scope:

- Broad external SaaS connector implementation
- Full multi-agent planning
- UI work beyond what is strictly necessary for compile/test stability
- Direct LLM prompt changes unless required to wire tool schemas/contracts
- Full approval UX
- Full audit/explainability views

# Files to touch

Inspect the solution first and then touch the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - tool execution concepts
  - enums/value objects for action type/status
  - policy decision/result models if domain-owned
- `src/VirtualCompany.Application/**`
  - tool contracts/interfaces
  - tool request/response DTOs
  - orchestration-facing execution service
  - adapters to task/approval/knowledge application services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/mappings
  - repository implementations
  - persistence for `tool_executions`
  - concrete tool implementations if infrastructure-owned
- `src/VirtualCompany.Api/**`
  - DI registration
  - any API/orchestration wiring needed
- `src/VirtualCompany.Shared/**`
  - only if shared contracts already live here
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if execution is exposed through API
- potentially add/update tests in:
  - `tests/**Application*`
  - `tests/**Infrastructure*`
  - or existing test projects in the repo

Also inspect:

- existing migrations strategy and whether new migration files belong in the active migrations location
- any existing orchestration, policy, approval, task, or retrieval services that should be reused

# Implementation plan

1. **Discover existing architecture in code before changing anything**
   - Identify current module boundaries across Domain, Application, Infrastructure, Api.
   - Find whether these already exist:
     - orchestration service
     - policy guardrail service
     - task commands/queries
     - approval services
     - knowledge retrieval service
     - audit event creation
     - `tool_executions` entity/table mapping
   - Follow existing naming and folder conventions.

2. **Define the core tool abstraction**
   Create a minimal but extensible typed abstraction for internal tools. Prefer interfaces like:

   - `ITool`
   - `IToolExecutor` or `IToolExecutionService`
   - `IToolRegistry`
   - `IToolPolicyEvaluator` or adapter to existing policy engine
   - `IToolInvocationContext`

   The abstraction should support:
   - tenant/company context
   - agent context
   - optional task/workflow context
   - correlation ID
   - tool name
   - action type (`read`, `recommend`, `execute`)
   - structured request payload
   - structured response payload
   - policy decision metadata
   - safe user-facing denial/error message

   Prefer strongly typed request/response models per tool, but it is acceptable to use a common envelope with typed payload classes behind the interface.

3. **Model action types and execution outcomes**
   Add or reuse enums/value objects for:
   - tool action type
   - execution status
   - policy outcome

   Keep them explicit and serializable for persistence and auditability.

   Suggested statuses:
   - `Allowed`
   - `Denied`
   - `ApprovalRequired`
   - `Succeeded`
   - `Failed`

   If existing conventions differ, align to the codebase.

4. **Implement a registry for a small connector set**
   Add a registry that exposes only approved tools to orchestration.

   Start with a small set of internal tools:
   - `tasks.get` or `tasks.list`
   - `tasks.update_status`
   - `approvals.create_request`
   - `knowledge.search`

   Each tool should:
   - declare its name
   - declare supported action type
   - declare request/response contract
   - execute only through application-layer services
   - avoid direct DB access from orchestration/tool caller

5. **Implement concrete internal tools through typed contracts**
   For each initial tool:
   - inject the relevant application service/command handler/query service
   - validate request shape
   - enforce tenant-scoped inputs
   - return structured results only

   Examples:
   - Task read tool calls task query service
   - Task update tool calls task command/application service
   - Approval request tool calls approval application service
   - Knowledge search tool calls retrieval/query service

   Do not let tools:
   - instantiate repositories directly from orchestration
   - bypass application rules
   - call external systems directly from model output

6. **Add policy-enforced execution flow**
   Implement or extend a central execution service:

   Flow:
   1. receive tool request
   2. resolve tool from registry
   3. evaluate policy before execution
   4. if denied:
      - persist denied execution attempt if consistent with current design
      - create audit signal if existing audit path is available
      - return safe explanation
   5. if approval required:
      - optionally route through approval creation tool/service
      - persist policy decision metadata
      - return non-executed result indicating approval required
   6. if allowed:
      - execute tool
      - persist request/response/status/policy decision
      - return structured result

   Policy evaluation must consider, as available in current code:
   - tenant scope
   - action type
   - agent autonomy level
   - tool permission scope
   - thresholds
   - approval requirements

   Default to deny if required policy context is missing or ambiguous.

7. **Persist tool execution records**
   Ensure `tool_executions` is represented in persistence and captures:
   - company_id
   - task_id nullable
   - workflow_instance_id nullable
   - agent_id
   - tool_name
   - action_type
   - request_json
   - response_json
   - status
   - policy_decision_json
   - started_at
   - completed_at nullable

   If entity/mapping already exists, extend it only as needed.
   If not, add:
   - domain model/entity
   - EF configuration
   - migration if the project uses active migrations in source control

   Keep JSON payloads structured and stable enough for future audit views.

8. **Wire dependency injection**
   Register:
   - tool registry
   - concrete tools
   - execution service
   - policy evaluator adapter
   - any serializers/helpers

   Keep registrations modular and consistent with existing composition root patterns.

9. **Integrate with orchestration entry points**
   If an orchestration pipeline already exists, update it to use the new execution service.
   If not, create the minimal seam needed so future orchestration can call:
   - `ExecuteToolAsync(...)`

   The orchestration layer should only know about:
   - tool names
   - schemas/contracts
   - structured results
   - safe denial/approval-required outcomes

10. **Add tests**
   Add focused tests for:
   - allowed internal tool execution persists `tool_executions`
   - denied execution returns safe explanation and does not execute underlying action
   - approval-required path returns correct structured outcome
   - tool registry exposes only registered tools
   - internal tools call application services/contracts, not persistence shortcuts
   - tenant context is required and enforced
   - unsupported tool names are rejected safely

   Prefer unit tests for policy/execution logic and integration tests for persistence behavior.

11. **Document assumptions in code comments only where necessary**
   Keep comments concise.
   Do not add speculative architecture docs unless the repo already expects them.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow, verify migration generation/apply path:
   - create/update migration if needed
   - ensure build/tests still pass

4. Manually validate the following through tests or a minimal execution harness:
   - a valid `tasks.get` or equivalent read tool request succeeds
   - a valid `tasks.update_status` execute request succeeds only when policy allows
   - a denied request returns a safe explanation
   - an approval-required request does not execute the action directly
   - `tool_executions` persistence includes policy decision metadata

5. Confirm architectural constraints:
   - no direct DB access from orchestration/tool caller
   - tools use typed contracts/application services
   - no direct external system invocation from model-facing code
   - tenant/company context is always present in execution flow

# Risks and follow-ups

- **Risk: existing orchestration/policy code may already partially implement this**
  - Reuse and refactor rather than layering duplicate abstractions.

- **Risk: unclear current migrations location**
  - Inspect the active migration strategy before adding schema changes.

- **Risk: over-generalizing tool abstractions too early**
  - Keep the abstraction small and practical for the initial internal tool set.

- **Risk: policy engine may not yet expose all required inputs**
  - Implement a minimal adapter and default-deny behavior; note gaps with TODOs only if necessary.

- **Risk: approval flow may not yet be complete**
  - Return a structured `approval required` result and integrate with existing approval creation services where available.

Follow-ups after this task:
- add more internal tools across workflows, communications, and analytics
- add external connector adapters behind the same abstraction
- expose tool schemas to prompt builder/orchestration
- enrich audit event creation for denied/approved tool decisions
- add stronger idempotency/correlation handling for retries and long-running executions