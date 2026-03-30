# Goal
Implement backlog task **TASK-8.3.3 — Actions outside scope are blocked and logged with a reason** for story **ST-203 Autonomy levels and policy guardrails** in the existing .NET solution.

The coding agent should add pre-execution policy enforcement so that when an agent attempts a tool action outside its allowed scope, the action is **denied before execution**, the denial is **persisted in structured business records**, and a **safe reason** is returned to the caller.

This task must align with the architecture and backlog expectations:
- policy checks happen **before tool execution**
- policy behavior is **default-deny**
- denials are **structured and auditable**
- the implementation fits the modular monolith / clean architecture style
- no direct external side effects occur for denied actions

# Scope
Implement only what is necessary for this task, while keeping the design extensible for the rest of ST-203 and ST-503.

In scope:
- Add or complete a **policy decision model** that can represent:
  - allowed / denied
  - denial reason code
  - denial reason message
  - relevant evaluated dimensions such as autonomy level, action type, scope mismatch, missing config
- Add guardrail logic that evaluates a requested tool action against:
  - tenant/company scope
  - agent tool permission scope
  - action type (`read`, `recommend`, `execute`)
  - autonomy level if already modeled
  - missing/ambiguous policy config => deny
- Ensure denied actions are **not executed**
- Persist denied attempts in `tool_executions` with policy decision metadata
- Create a business audit event for denied actions with a concise rationale summary
- Return a safe application result/error that upstream callers can surface without exposing internals
- Add automated tests for the denial path and logging behavior

Out of scope unless required by existing code structure:
- Full approval-request creation flow for threshold breaches
- UI changes in Blazor or MAUI
- New external integrations
- Broad refactors unrelated to policy enforcement
- Implementing all autonomy/threshold rules if not needed for this task; only wire enough structure to support denial for out-of-scope actions

If the codebase already contains partial policy/guardrail abstractions, extend them rather than introducing parallel patterns.

# Files to touch
Inspect the solution first and then modify the minimum necessary files in the relevant layers. Likely areas:

- `src/VirtualCompany.Domain/...`
  - policy/guardrail value objects, enums, domain models
  - audit event model if needed
  - tool execution model if policy decision metadata needs to be formalized

- `src/VirtualCompany.Application/...`
  - orchestration or tool execution service
  - policy evaluation service / interface
  - command handlers or application services that initiate tool execution
  - DTOs/results for denied execution outcomes

- `src/VirtualCompany.Infrastructure/...`
  - persistence mappings for `tool_executions` and `audit_events`
  - repository implementations
  - JSON serialization for `policy_decision_json`
  - EF Core configurations / migrations if schema changes are required

- `src/VirtualCompany.Api/...`
  - only if API contracts or error mapping must be updated for safe denial responses

- Test projects in the solution
  - add unit tests for policy evaluation
  - add application/integration tests for “blocked and logged with reason”

Also review:
- `README.md`
- existing orchestration / tool execution pipeline
- existing audit logging patterns
- existing persistence model for `tool_executions` and `audit_events`

Prefer touching existing files over creating many new ones if the project already has the right abstractions.

# Implementation plan
1. **Discover existing implementation points**
   - Find the current tool execution flow end-to-end:
     - where a tool request is created
     - where policy checks currently happen, if at all
     - where `tool_executions` are persisted
     - where audit events are written
   - Identify existing domain concepts for:
     - agent permissions
     - autonomy level
     - action type
     - company/tenant scoping
   - Reuse existing naming and patterns.

2. **Define a structured policy decision contract**
   - Introduce or extend a model such as `PolicyDecision` with fields similar to:
     - `IsAllowed`
     - `Decision` or `Outcome`
     - `ReasonCode`
     - `Reason`
     - `EvaluatedActionType`
     - `EvaluatedAutonomyLevel`
     - `EvaluatedToolName`
     - `Metadata` or structured details
   - Ensure it is serializable into `policy_decision_json`.
   - Add stable reason codes, for example:
     - `out_of_scope`
     - `tool_not_permitted`
     - `action_type_not_allowed`
     - `autonomy_level_insufficient`
     - `tenant_scope_mismatch`
     - `policy_missing`
     - `policy_ambiguous`
   - Keep messages concise and safe for audit and user-facing summaries.

3. **Implement default-deny guardrail evaluation**
   - In the policy guardrail engine or equivalent service, evaluate the requested tool action before execution.
   - At minimum, deny when:
     - the tool is not in the agent’s permitted tools
     - the requested action type exceeds the configured scope
     - tenant/company context is missing or mismatched
     - policy configuration is missing or ambiguous
   - If autonomy level is already represented, include a simple check that can deny unsupported action types for lower autonomy levels.
   - Return a structured denial decision instead of throwing for expected policy failures.

4. **Short-circuit execution on denial**
   - Update the tool execution pipeline so that:
     - policy evaluation runs first
     - if denied, the actual tool handler is never invoked
     - a `tool_executions` record is still created with:
       - request payload
       - status indicating denied/blocked/failed-by-policy according to existing conventions
       - `policy_decision_json`
       - timestamps
       - no external side effects
   - Preserve correlation IDs and tenant context if those patterns already exist.

5. **Write business audit event for denied action**
   - Create an audit event when an action is blocked.
   - Use existing audit infrastructure if present.
   - Include:
     - company/tenant id
     - actor type = agent
     - actor id = agent id
     - action indicating attempted tool execution was blocked
     - target type/id if available
     - outcome = denied/blocked
     - rationale summary = concise reason
     - optional data source / metadata references if supported
   - Keep this separate from technical logs.

6. **Return safe denial result to caller**
   - Ensure the application layer returns a safe result that upstream API/chat/task flows can use.
   - The response should communicate that the action was blocked due to policy, without leaking internal implementation details.
   - Prefer a typed result over exceptions for expected denials.
   - If the current architecture uses exceptions for business rule failures, map them to a safe response consistently.

7. **Add tests**
   - Unit tests for policy evaluation:
     - denies when tool is not permitted
     - denies when action type exceeds scope
     - denies when policy config is missing
     - includes structured reason code/message
   - Application/service tests:
     - denied action does not invoke tool executor
     - denied action persists `tool_executions` with policy decision metadata
     - denied action creates audit event with reason
   - If integration tests exist for persistence:
     - verify serialized `policy_decision_json` contains the denial reason

8. **Keep implementation aligned with future stories**
   - Structure the code so threshold/approval logic can later branch from the same policy decision mechanism.
   - Do not overbuild approvals here, but avoid hardcoding denial-only assumptions into shared abstractions.

Implementation notes:
- Prefer enums/constants for reason codes to avoid string drift.
- If `tool_executions.status` lacks a suitable value, use the closest existing status unless adding a new one is low-risk and consistent.
- If schema changes are necessary, keep them minimal and backward-compatible.
- Do not expose raw chain-of-thought anywhere; use concise rationale summaries only.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Manually verify the denial path in code/tests:
   - create or use a test agent with restricted tool permissions
   - submit a tool execution request outside allowed scope
   - confirm:
     - policy decision is deny
     - tool handler is not called
     - `tool_executions` record is written with denial metadata
     - audit event is written with concise reason
     - caller receives safe blocked response

5. If persistence tests or local DB-backed tests exist, verify:
   - `policy_decision_json` is stored and queryable
   - tenant/company id is present on persisted records
   - no approval or external execution side effect occurs for denied actions

6. If API-level tests exist, verify response mapping:
   - denied action returns the expected safe status/result shape
   - no internal stack traces or sensitive details are exposed

# Risks and follow-ups
- **Existing abstractions may already partially implement guardrails.** Avoid duplicating policy logic in multiple layers; centralize evaluation in one service.
- **Status naming may be inconsistent** between domain and persistence for denied executions. Reuse existing conventions where possible.
- **Audit vs technical logging separation** must be preserved. This task requires business audit persistence, not just ILogger output.
- **Missing schema support** for structured policy metadata may require an EF migration; keep it minimal.
- **Approval branching is adjacent but separate.** Do not accidentally implement threshold-denial where future behavior should be “create approval request”.
- **User-facing messaging** should remain safe and concise; do not leak internal policy config or reasoning internals.
- Follow-up tasks likely needed after this one:
  - threshold-based approval creation
  - richer autonomy level matrix
  - UI surfacing of blocked actions and reasons
  - audit/explainability views for policy denials
  - standardized policy reason taxonomy across orchestration flows