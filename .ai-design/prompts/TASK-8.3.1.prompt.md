# Goal
Implement backlog task **TASK-8.3.1** for **ST-203 — Autonomy levels and policy guardrails** by adding support for **agent autonomy levels 0–3** with **conservative defaults of 0 or 1**, and ensuring the policy/guardrail layer can evaluate autonomy as part of pre-execution tool authorization.

This task should produce a vertical slice that is consistent with the architecture and backlog:
- autonomy is modeled on agents
- defaults are conservative for new agents/templates
- policy evaluation includes autonomy level
- missing/ambiguous policy config results in **default deny**
- implementation is structured for auditability and future approval integration

No explicit acceptance criteria were provided for this task, so derive behavior from:
- ST-203 acceptance criteria
- architecture notes about pre-execution guardrails
- backlog notes about default-deny and structured policy decisions

# Scope
In scope:
- Add or complete a domain-level representation for **autonomy levels 0, 1, 2, 3**
- Ensure `Agent` creation/defaulting uses a conservative autonomy default
- Add application/infrastructure support so policy evaluation can consume autonomy level
- Implement or extend a **policy guardrail service/engine contract** that evaluates:
  - action type (`read`, `recommend`, `execute`)
  - autonomy level
  - tool permission/scope presence
  - ambiguous or missing config => deny
- Return a **structured policy decision** object suitable for persistence/audit later
- Add unit tests for autonomy defaults and policy decisions
- If there is already a tool execution pipeline, wire autonomy checks into it without overbuilding approvals

Out of scope unless already trivial and clearly adjacent:
- Full approval workflow UX
- Full audit UI
- Broad roster/profile UI work beyond exposing autonomy if already required by existing code
- New external integrations
- Large refactors unrelated to autonomy/policy enforcement

Implementation guidance:
- Prefer incremental changes within the existing modular monolith boundaries
- Keep policy logic in application/domain services, not controllers/UI
- Do not invent speculative features beyond what this task needs
- If some ST-203 infrastructure is missing, implement the smallest clean abstraction that unblocks future tasks

# Files to touch
Inspect the solution first and then update the most relevant files in these areas as needed.

Likely targets:
- `src/VirtualCompany.Domain/**`
  - agent entity/value objects/enums
  - policy decision models
  - tool action type models
- `src/VirtualCompany.Application/**`
  - commands/handlers for creating or updating agents
  - policy guardrail service interfaces and implementations
  - orchestration/tool execution pipeline contracts
  - validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / persistence mapping
  - migrations if needed
  - repository implementations
- `src/VirtualCompany.Api/**`
  - request/response DTOs if autonomy is set or returned via API
  - DI registration for policy services
- `src/VirtualCompany.Web/**`
  - only if existing agent create/edit flows already expose autonomy and need default-safe handling
- Tests across:
  - `tests/**` if present
  - otherwise the existing test projects in the solution

Also review:
- `README.md`
- solution/project structure under `src/`
- any existing policy, approval, agent management, or orchestration code before making changes

# Implementation plan
1. **Discover current implementation**
   - Search for:
     - `autonomy`
     - `Agent`
     - `tool_executions`
     - `policy`
     - `approval`
     - `ToolExecutor`
     - `orchestration`
   - Determine:
     - whether `agents.autonomy_level` is already modeled
     - whether agent create/hire flows already set a default
     - whether a policy engine/service already exists
     - where tool execution authorization currently happens

2. **Model autonomy levels explicitly**
   - Introduce or normalize a strongly typed representation, preferably an enum/value object such as:
     - `AutonomyLevel` with values `Level0`, `Level1`, `Level2`, `Level3`
   - Keep persistence compatible with the architecture’s integer `autonomy_level`
   - Add guard clauses/validation so invalid values outside 0–3 are rejected

3. **Set conservative defaults**
   - Ensure new agents default to **0 or 1**, choosing the safest option that best fits current code paths
   - Prefer **Level0** unless there is a strong existing product convention for **Level1**
   - Apply the default consistently in:
     - domain factory/constructor
     - application command handler for hire/create agent
     - template-to-agent copy logic
   - If templates can specify autonomy, clamp/validate them and still preserve conservative defaults when unspecified

4. **Define structured policy decision output**
   - Add a model such as:
     - decision result: `Allowed`, `Denied`, `RequiresApproval`
     - reason code
     - human-readable reason
     - evaluated autonomy level
     - evaluated action type
     - optional threshold/approval metadata
   - Keep it serializable so it can later be stored in `tool_executions.policy_decision_json`

5. **Implement autonomy-aware policy evaluation**
   - Add or extend a service like `IPolicyGuardrailEngine` / `IPolicyEvaluator`
   - Inputs should minimally include:
     - company/tenant context
     - agent
     - tool name
     - action type (`read`, `recommend`, `execute`)
     - relevant permission/scope config
   - Enforce rules conservatively. At minimum:
     - missing agent config => deny
     - unknown tool/action => deny
     - invalid autonomy => deny
     - ambiguous permission mapping => deny
   - Suggested baseline behavior if no stronger existing rules exist:
     - **Level 0**: allow read-only or recommendation-only behavior; deny direct execute
     - **Level 1**: allow low-risk scoped actions already explicitly permitted; deny or require approval for execute depending on current architecture
     - **Level 2/3**: only meaningful when explicitly configured; do not auto-open broad permissions
   - Important: autonomy level alone must **not** grant access; it only constrains what otherwise-permitted actions may do

6. **Wire into pre-execution tool authorization**
   - If a tool execution pipeline exists, invoke policy evaluation **before** any tool runs
   - On deny:
     - do not execute the tool
     - return a safe application/user-facing failure
     - preserve structured decision metadata
   - On requires approval:
     - if approval infrastructure already exists, route accordingly
     - otherwise return a structured non-executed result that clearly indicates approval is required
   - On allow:
     - continue existing execution flow unchanged

7. **Persist or surface autonomy where appropriate**
   - Ensure API/application DTOs include autonomy level where needed for create/read/update flows
   - If EF mapping or migrations are needed, add them cleanly
   - Do not add unnecessary UI if not already present, but avoid breaking existing agent profile/roster flows

8. **Add tests**
   - Unit tests for:
     - default autonomy on new agent creation
     - invalid autonomy values rejected
     - level 0 denies execute
     - missing/ambiguous config denies
     - autonomy does not override missing permission
     - structured decision includes reason codes/messages
   - Integration tests if existing patterns support them:
     - tool execution pipeline blocks denied actions pre-execution

9. **Keep implementation clean and minimal**
   - Follow existing naming, layering, and DI patterns
   - Avoid embedding policy logic in controllers or UI
   - Avoid introducing broad approval/workflow logic unless already present and easy to connect

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly in the project’s existing pattern.

4. Manually verify code behavior through tests or targeted debugging:
   - creating an agent without autonomy specified results in default **0 or 1** conservatively
   - invalid autonomy values are rejected
   - a level 0 agent attempting an `execute` tool action is blocked before execution
   - missing tool permission config returns a deny decision
   - policy decision object contains structured reason data

5. Confirm architectural alignment:
   - pre-execution guardrails, not post-hoc only
   - default-deny on missing/ambiguous config
   - structured decision ready for audit persistence

# Risks and follow-ups
- **Risk: existing code already stores autonomy as raw int everywhere**
  - Mitigate by introducing a typed wrapper/enum while preserving DB compatibility.

- **Risk: policy logic may already exist in multiple places**
  - Consolidate carefully into a single reusable evaluator to avoid inconsistent enforcement.

- **Risk: approval flow may not yet exist**
  - Return a structured `RequiresApproval` decision without overbuilding workflow plumbing unless already available.

- **Risk: UI/API contracts may assume unrestricted execution**
  - Keep changes backward-compatible where possible and surface safe failure messages.

Follow-ups after this task:
- connect `RequiresApproval` decisions to full approval request creation
- persist policy decisions into `tool_executions` and `audit_events`
- expose autonomy and guardrail outcomes in agent profile and audit views
- refine per-level policy semantics with explicit product rules if not yet documented