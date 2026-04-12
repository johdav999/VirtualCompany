# Goal
Implement backlog task **TASK-11.3.5 — Separate read/recommend/execute action types** for story **ST-503 Policy-enforced tool execution**.

The coding agent should update the orchestration and policy-enforcement flow so that tool calls are explicitly classified and handled as **read**, **recommend**, or **execute** actions, rather than relying on ambiguous or loosely inferred action semantics.

This change should strengthen pre-execution guardrails and make policy decisions, persistence, and downstream audit behavior more explicit and testable.

# Scope
In scope:

- Introduce or normalize a first-class representation of tool action types:
  - `read`
  - `recommend`
  - `execute`
- Ensure policy evaluation consumes this explicit action type before any tool runs.
- Ensure tool execution persistence records the explicit action type consistently.
- Update internal tool contracts and orchestration flow so tools declare or are invoked with one of these action types.
- Apply default-deny behavior when action type is missing, invalid, or ambiguous.
- Add/adjust tests covering policy behavior and persistence for the three action types.
- Keep implementation aligned with architecture guidance:
  - policy-enforced tool execution
  - typed internal tool contracts
  - no direct DB access from tools
  - safe user-facing denial behavior

Out of scope unless required by the existing code structure:

- Broad UI changes
- New external integrations
- Major schema redesign beyond what is necessary to support explicit action typing
- Full approval workflow redesign
- Large refactors unrelated to tool execution policy enforcement

# Files to touch
Start by inspecting these projects and likely touch only the minimum necessary files:

- `src/VirtualCompany.Domain`
  - tool execution domain models/value objects/enums
  - policy decision models
- `src/VirtualCompany.Application`
  - orchestration services
  - tool execution request/response contracts
  - policy guardrail engine interfaces/handlers
  - command/query handlers if tool execution is command-driven
- `src/VirtualCompany.Infrastructure`
  - persistence mappings for `tool_executions`
  - repository implementations
  - any tool registry/executor wiring
- `src/VirtualCompany.Api`
  - only if API contracts or DI registration need adjustment
- `tests/VirtualCompany.Api.Tests`
  - integration/API tests if tool execution behavior is exposed there
- Any existing test project covering application/domain behavior

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the current implementation stores `action_type` already but does not enforce it strongly, prefer tightening behavior over introducing unnecessary schema churn.

# Implementation plan
1. **Inspect current tool execution flow**
   - Find the end-to-end path for:
     - orchestration request
     - tool selection/invocation
     - policy evaluation
     - persistence to `tool_executions`
     - audit/denial handling
   - Identify where action type is currently:
     - absent
     - stringly typed
     - inferred too late
     - conflated with tool name or intent

2. **Introduce a canonical action type model**
   - Add a strongly typed representation in the domain or application layer, preferably an enum/value object such as `ToolActionType`.
   - Supported values must map clearly to:
     - `Read`
     - `Recommend`
     - `Execute`
   - Ensure serialization/persistence uses stable lowercase values if the codebase follows that convention.
   - Reject unknown values explicitly.

3. **Update tool execution request contracts**
   - Modify the internal request model used by the orchestration engine and tool executor so action type is required.
   - If there is a tool descriptor/registry, ensure each tool or tool operation declares its supported/default action type explicitly.
   - Avoid hidden inference where possible.
   - If some tools support multiple operations, action type should be attached to the specific operation/request, not just the tool family.

4. **Enforce action type in policy guardrail evaluation**
   - Update the policy engine input contract so action type is mandatory.
   - Ensure policy checks explicitly branch on:
     - read scope
     - recommend scope
     - execute scope
   - Preserve or implement default-deny behavior when:
     - action type is null/missing
     - action type is invalid
     - policy config is ambiguous
   - Ensure approval/threshold logic can distinguish between recommend vs execute where relevant.

5. **Update orchestration flow**
   - Before any tool invocation, ensure the orchestrator resolves the intended action type and passes it into policy evaluation.
   - Only allow tool execution to proceed after a successful policy decision for that exact action type.
   - For denied actions, return a safe user-facing explanation without exposing internals or chain-of-thought.

6. **Persist explicit action type**
   - Ensure `tool_executions.action_type` is always populated with one of the three canonical values.
   - If EF/entity mappings exist, update them as needed.
   - Add a migration only if the current schema cannot support the stricter contract.
   - Prefer non-breaking migration strategy if production compatibility matters.

7. **Harden internal tool abstractions**
   - Ensure internal tools are invoked through typed contracts and not by ad hoc payloads that omit action semantics.
   - If there is a base interface for tools, consider adding action type awareness to the invocation contract rather than embedding policy logic inside tools.
   - Keep policy enforcement centralized before execution.

8. **Add tests**
   - Add unit/integration tests for at least:
     - read action allowed when read scope permits
     - recommend action denied when recommend scope does not permit
     - execute action requiring approval or denial based on policy
     - missing/unknown action type is denied
     - persisted tool execution records contain the correct action type
   - If existing tests cover policy engine behavior, extend them rather than duplicating patterns.

9. **Keep changes cohesive**
   - Do not introduce parallel representations of action type.
   - Refactor existing string constants to the canonical model where touched.
   - Preserve backward compatibility only where necessary; otherwise prefer correctness and explicitness.

# Validation steps
Run and verify the following:

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are added or updated, verify:
   - migration compiles
   - persistence mapping for `tool_executions.action_type` works
   - no obvious mismatch between entity model and schema

4. Manually validate code paths by inspection or tests:
   - a `read` tool request reaches policy evaluation with `Read`
   - a `recommend` tool request is distinguishable from `execute`
   - an `execute` request cannot bypass policy checks
   - denied requests produce safe, non-leaky explanations
   - successful executions persist the canonical action type

5. Confirm architecture alignment:
   - policy checks happen pre-execution
   - tools use typed internal contracts
   - no direct external/model-driven execution bypass is introduced

# Risks and follow-ups
- **Risk: action type currently inferred from prompt/tool name**
  - Follow-up may be needed to make tool definitions more explicit across the registry/catalog.

- **Risk: existing persisted records use inconsistent strings**
  - If encountered, normalize carefully and document any migration assumptions.

- **Risk: recommend vs execute semantics may be blurred in current business logic**
  - If ambiguity appears, prefer conservative behavior:
    - classify only clearly non-mutating operations as `read`
    - classify advisory/non-side-effect outputs as `recommend`
    - classify any state-changing or external-effect operation as `execute`

- **Risk: approval logic may currently trigger only on a generic “action”**
  - Follow-up may be needed to refine thresholds and approval rules per action type.

- **Risk: multiple layers may each define their own action type constants**
  - Consolidate to one canonical representation to avoid drift.

- **Follow-up suggestion**
  - After implementation, consider a small backlog item to audit all registered tools/operations and explicitly annotate each with supported action types and side-effect expectations.