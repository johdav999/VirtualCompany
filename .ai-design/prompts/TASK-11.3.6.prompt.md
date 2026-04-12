# Goal
Implement backlog task **TASK-11.3.6 — Never let the model call external systems directly** for story **ST-503 Policy-enforced tool execution** in the existing .NET modular monolith.

The coding agent should enforce an architecture where:
- the LLM can only emit **tool intents / structured tool requests**
- all actual execution is performed by trusted application/infrastructure code
- external integrations are reachable **only through policy-enforced tool executor pathways**
- no model-generated content can directly invoke HTTP clients, SDKs, DB access, or integration adapters

This task should strengthen the orchestration boundary so the system complies with the story note:

> Never let the model call external systems directly.

Because no explicit acceptance criteria were provided for this task, derive implementation behavior from:
- ST-503 acceptance criteria
- architecture guidance around **Policy Guardrail Engine** and **Tool Executor**
- clean architecture boundaries in the solution

# Scope
In scope:
- Identify the current orchestration/tool execution path in the solution
- Introduce or tighten abstractions so model outputs are treated as **untrusted requests**
- Ensure only approved internal executors/adapters can perform external side effects
- Prevent direct model-to-external-system execution patterns
- Persist or surface policy/tool execution metadata where the current architecture supports it
- Add tests proving the model cannot directly trigger external calls and that execution must pass through policy/tool executor code

Out of scope unless required by existing code structure:
- Building a full connector ecosystem
- Implementing every future policy rule from ST-203/ST-503
- Large UI changes
- Reworking unrelated orchestration features
- Adding new external integrations beyond what is needed to enforce the boundary

# Files to touch
The coding agent should inspect the repo first, then update the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Application/**`
  - orchestration interfaces/services
  - tool request/response contracts
  - policy evaluation contracts
  - command handlers or application services that mediate tool execution
- `src/VirtualCompany.Domain/**`
  - domain concepts/enums/value objects for tool action types, policy decisions, execution status
- `src/VirtualCompany.Infrastructure/**`
  - concrete tool executor implementations
  - integration adapters
  - persistence for `tool_executions` and audit records if already present
  - any HTTP/integration clients that must be hidden behind trusted adapters
- `src/VirtualCompany.Api/**`
  - DI registration
  - endpoint wiring only if needed
- `src/VirtualCompany.Shared/**`
  - shared DTOs only if the current architecture already uses this project for orchestration contracts
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially new test projects or existing application/infrastructure test folders if present in solution

Also inspect:
- `README.md`
- any architecture docs or conventions in the repo
- existing migration/archive docs only for reference, not as the primary implementation target:
  - `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover the current orchestration and tool execution flow**
   - Search for:
     - orchestration services
     - prompt builder
     - tool executor
     - OpenAI/LLM client usage
     - integration clients
     - any direct HTTP/SDK calls triggered from model responses
   - Identify where the model output is parsed and how tool calls are currently represented.
   - Identify whether there is already a `ToolExecutor`, `PolicyGuardrailEngine`, `ToolExecution`, or similar concept.

2. **Define the trust boundary explicitly**
   - Treat model output as data only.
   - Introduce or refine a contract such as:
     - `ProposedToolCall`
     - `ToolExecutionRequest`
     - `ToolExecutionResult`
     - `PolicyDecision`
   - Ensure the LLM-facing layer can only return structured requests, never executable delegates, raw URLs, credentials, SQL, or direct adapter references.
   - If current code allows arbitrary function dispatch by model-provided names/arguments, replace it with a server-side registry/allowlist.

3. **Implement a server-side tool registry / allowlist**
   - Add a trusted registry mapping known tool names to internal handlers.
   - Tool names must resolve only to pre-registered handlers owned by application/infrastructure code.
   - Unknown tool names must be denied safely.
   - Model-supplied arguments must be validated against typed contracts before execution.
   - Do not allow the model to specify:
     - arbitrary endpoints
     - arbitrary SQL
     - arbitrary class/type names
     - arbitrary method names
     - arbitrary headers/tokens
     - arbitrary file paths outside approved abstractions

4. **Route all tool execution through policy enforcement**
   - Before any tool handler runs, require a policy evaluation step using available context:
     - company/tenant scope
     - agent identity
     - action type (`read`, `recommend`, `execute`)
     - autonomy level
     - thresholds / approval requirements if available in current code
   - Default deny on missing or ambiguous policy context.
   - Return safe user-facing denial messages and structured internal reasons.

5. **Ensure external systems are only reachable from trusted adapters**
   - Refactor any direct external calls out of model/orchestration parsing code.
   - External integrations must be invoked only by:
     - internal tool handlers
     - application services
     - infrastructure adapters behind interfaces
   - The model should never receive direct access to:
     - `HttpClient`
     - integration SDK instances
     - repository/DbContext access
     - raw connection strings or credentials
   - If there is existing code where model output is transformed into direct HTTP requests, remove that pattern.

6. **Constrain tool handlers to typed internal contracts**
   - Internal tools should call domain/application module services through typed interfaces rather than direct DB access where possible.
   - For external integrations, handlers should call a narrow adapter interface with validated parameters.
   - Keep the handler surface explicit and auditable.

7. **Persist execution and denial metadata where supported**
   - If the repo already contains persistence for tool executions, ensure both allowed and denied attempts capture useful metadata.
   - At minimum, record:
     - tool name
     - action type
     - request payload
     - status
     - policy decision / denial reason
     - timestamps
     - tenant/company and agent/task context where available
   - If full persistence is not yet implemented in this codebase, add the minimal structured logging/testable hooks needed without overreaching.

8. **Add defensive validation**
   - Validate tool arguments before execution.
   - Reject malformed, missing, or extra-dangerous fields.
   - Sanitize user-facing errors.
   - Ensure no raw exception leaks from denied or invalid tool requests.

9. **Add tests**
   Add focused tests that prove the boundary:
   - unknown tool name is denied
   - model output cannot directly invoke an external adapter
   - execution requires policy approval before handler invocation
   - denied requests do not call the underlying integration client
   - allowed requests call only the registered trusted handler
   - malformed arguments are rejected safely
   - if applicable, verify persistence/logging of denied and allowed execution attempts

10. **Keep changes aligned with the existing architecture**
   - Respect project boundaries:
     - Domain: core concepts
     - Application: orchestration contracts/use cases/policy mediation
     - Infrastructure: adapters/external calls/persistence
     - API: composition root
   - Prefer minimal, composable changes over broad rewrites.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add and run targeted tests for this task covering:
   - direct external execution is impossible from model output
   - only registered tools can execute
   - policy denial blocks execution
   - denied execution does not hit external adapters
   - safe explanation is returned for denied/unknown tools

4. Manually review the code for forbidden patterns:
   - model/parsing layer creating `HttpRequestMessage`
   - model/parsing layer calling `HttpClient`
   - model/parsing layer resolving arbitrary services from DI and invoking them
   - model output being used as SQL, endpoint URL, method name, or type name
   - direct DB access from tool abstraction where typed module contracts should be used

5. Confirm DI wiring ensures:
   - orchestration depends on abstractions
   - only trusted tool handlers/adapters are registered
   - external clients are not exposed to model-facing components

6. If logging/persistence exists, verify execution records include enough metadata to audit allow/deny outcomes.

# Risks and follow-ups
- **Risk: existing orchestration may already be tightly coupled to provider-specific function calling**
  - Mitigation: preserve provider integration but convert function calls into server-side validated `ProposedToolCall` objects before execution.

- **Risk: over-implementing future policy engine behavior**
  - Mitigation: implement only the boundary and minimum policy gate needed for this task; do not invent a full approval engine unless already present.

- **Risk: hidden direct external calls in infrastructure or experimental code paths**
  - Mitigation: search broadly for `HttpClient`, provider SDK usage, webhook calls, and integration service invocations tied to orchestration.

- **Risk: breaking current chat/task flows**
  - Mitigation: keep public contracts stable where possible and add regression tests around existing orchestration behavior.

- **Follow-up recommendation**
  - Add a formal architecture test or convention test that enforces:
    - model-facing/orchestration parsing layers cannot reference infrastructure integration clients directly
    - external integrations are only invoked through tool executor/adapter interfaces
  - Consider adding structured audit persistence for denied tool attempts if not yet present.