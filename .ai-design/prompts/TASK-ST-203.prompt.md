# Goal
Implement backlog task **TASK-ST-203 — Autonomy levels and policy guardrails** for story **ST-203 Autonomy levels and policy guardrails** in the existing .NET solution.

Deliver a production-ready vertical slice that introduces **agent autonomy levels (0–3)** and a **pre-execution policy guardrail engine** that evaluates whether a requested tool/action is allowed, denied, or requires approval before execution proceeds.

The implementation must align with the provided architecture:
- modular monolith
- ASP.NET Core backend
- PostgreSQL persistence
- policy-enforced tool execution
- human-in-the-loop approvals for sensitive actions
- tenant-scoped behavior
- structured auditability

Use conservative defaults:
- new or existing agents without explicit autonomy configuration should behave as **Level 0 or Level 1**
- **default-deny** when policy configuration is missing, invalid, or ambiguous

# Scope
Implement only what is necessary to satisfy **ST-203** and create a solid foundation for later stories such as ST-403 and ST-503.

Include:

1. **Domain support for autonomy levels**
   - Represent autonomy levels 0–3 explicitly
   - Ensure agent configuration supports and validates these levels
   - Preserve conservative defaults

2. **Policy guardrail engine**
   - Add an application/domain service that evaluates a requested action before tool execution
   - Inputs should include at minimum:
     - tenant/company context
     - agent
     - requested tool/action
     - action type (`read`, `recommend`, `execute`)
     - threshold context if applicable
   - Output should be a structured decision object with:
     - decision result (`allow`, `deny`, `require_approval`)
     - machine-readable reason codes
     - human-readable explanation
     - evaluated autonomy level
     - evaluated thresholds / approval requirement details
     - enough metadata for persistence and audit

3. **Pre-execution enforcement**
   - Ensure policy evaluation happens **before** any tool execution
   - Block out-of-scope actions
   - Route sensitive above-threshold actions into approval creation instead of direct execution

4. **Structured persistence/audit hooks**
   - Persist policy decision metadata alongside tool execution attempts where appropriate
   - Ensure denied actions are logged in a structured way suitable for later audit views
   - If approval is required, create an approval request record rather than executing directly

5. **Tests**
   - Add unit tests for policy evaluation behavior
   - Add application/service tests for pre-execution enforcement paths
   - Cover default-deny and threshold/approval scenarios

Out of scope unless required by existing code structure:
- full UI for configuring all guardrails
- full approval inbox UX
- external integrations
- broad refactors unrelated to ST-203
- speculative implementation of future stories beyond minimal extension points

# Files to touch
Inspect the solution first and adjust to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - agent entity/value objects/enums
  - policy decision models
  - approval-related domain models if already present
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for policy evaluation and tool execution orchestration
  - DTOs/contracts for policy requests and decisions
  - approval creation flow integration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/migrations
  - repositories
  - persistence of policy decision JSON / approval records / audit hooks
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if any API surface is needed for testing or orchestration entry points
- `src/VirtualCompany.Shared/**`
  - shared enums/contracts only if this solution already centralizes them here
- `src/VirtualCompany.Web/**`
  - only minimal changes if existing agent profile screens already expose autonomy level and need wiring
- `README.md`
  - only if setup or behavior documentation is already maintained there

Also inspect for likely existing files/classes such as:
- `Agent`, `AgentConfiguration`, `ToolExecution`, `Approval`, `AuditEvent`
- orchestration services
- command handlers for task/tool execution
- EF DbContext and migrations
- test projects under `tests/**` or similarly named folders

Do not invent unnecessary new layers if equivalent patterns already exist in the codebase. Follow the established architecture and naming conventions.

# Implementation plan
1. **Explore the codebase and map existing patterns**
   - Identify:
     - how agents are modeled
     - where autonomy level currently exists, if at all
     - whether approvals and tool executions already have entities/tables
     - how commands/handlers/services are organized
     - whether there is already an orchestration or tool execution abstraction
   - Reuse existing CQRS, repository, and validation patterns
   - Reuse existing tenant scoping conventions

2. **Add or normalize autonomy level modeling**
   - Introduce a strongly typed representation for autonomy levels 0–3
     - enum or value object, depending on existing style
   - Enforce valid range centrally
   - Ensure agent defaults are conservative
   - If the database already has `autonomy_level`, wire validation and defaults through domain/application layers
   - If not yet persisted, add the necessary persistence mapping and migration

3. **Define policy evaluation contracts**
   - Create request/response models for guardrail evaluation, for example:
     - `PolicyEvaluationRequest`
     - `PolicyDecision`
     - `PolicyDecisionReasonCode`
     - `PolicyDecisionOutcome`
   - Request should include:
     - `CompanyId`
     - `AgentId`
     - `ToolName`
     - `ActionType`
     - requested payload/context
     - threshold context (amount, risk marker, affected entity, etc.) where available
   - Decision should include structured fields suitable for JSON persistence and audit

4. **Implement the policy guardrail engine**
   - Add a dedicated service, e.g. `IPolicyGuardrailEngine` / `PolicyGuardrailEngine`
   - Evaluation rules must include:
     - tenant scope presence/consistency
     - agent permission scope
     - autonomy level
     - threshold rules
     - approval requirements
   - Required behavior:
     - **deny** when tenant context is invalid or missing
     - **deny** when tool/action is outside configured permissions
     - **deny** when policy config is missing or ambiguous
     - **deny** when autonomy level does not permit the requested action type
     - **require approval** when action is sensitive or above threshold but otherwise eligible
     - **allow** only when all checks pass
   - Keep the implementation deterministic and testable
   - Prefer explicit reason codes over free-form strings

5. **Define baseline autonomy semantics**
   - Implement a clear, minimal policy matrix unless the codebase already defines one.
   - Use this baseline:
     - **Level 0**: read-only or no external action; deny `execute`; recommendations may be limited by config
     - **Level 1**: can read and recommend within scope; execution requires approval or is denied unless explicitly allowed by policy
     - **Level 2**: can execute low-risk/in-threshold actions within scope; above-threshold actions require approval
     - **Level 3**: can execute broader in-scope actions, but still subject to thresholds, explicit restrictions, and approval rules
   - Do not bypass thresholds or approval rules at higher levels
   - If existing product conventions differ, adapt while preserving conservative behavior

6. **Integrate guardrails into pre-execution flow**
   - Find the earliest point before tool execution and insert policy evaluation there
   - Ensure no tool/domain action runs before a decision is made
   - For each outcome:
     - `allow`: continue to execution
     - `deny`: stop execution, return safe explanation, persist structured decision
     - `require_approval`: create approval request, mark related task/action as awaiting approval if applicable, do not execute tool
   - Ensure internal tools are still invoked through typed contracts, not direct DB access

7. **Persist decision metadata**
   - If `tool_executions` already exists or is being introduced, persist:
     - tool name
     - action type
     - request payload
     - status
     - `policy_decision_json`
     - timestamps
   - For denied attempts, persist enough data for later auditability even if no tool ran
   - For approval-required outcomes:
     - create an `approvals` record
     - populate threshold context JSON
     - set status to pending
     - link to task/workflow/entity if such linkage already exists
   - Use existing outbox/audit patterns if present; otherwise keep implementation minimal and consistent

8. **Add validation and safe defaults**
   - Validate autonomy level on create/update agent flows
   - Validate policy-related configuration structures if they already exist on the agent
   - When configuration is absent:
     - do not infer permissive behavior
     - return deny with a reason like `missing_policy_configuration`
   - Ensure archived/restricted/paused agent status is respected if relevant in the execution path

9. **Add tests**
   - Unit tests for guardrail engine:
     - valid allow case
     - deny when tool not permitted
     - deny when action type exceeds autonomy level
     - deny when config missing/ambiguous
     - require approval when threshold exceeded
     - allow when in threshold and permitted
   - Application/integration-style tests for orchestration/tool execution path:
     - denied action does not execute tool
     - approval-required action creates approval and does not execute tool
     - allowed action executes tool and persists decision metadata
   - Include tenant-scoping assertions where feasible

10. **Document assumptions in code**
   - Add concise comments where policy semantics may not yet have full product configuration support
   - Keep extension points obvious for ST-403 and ST-503
   - Do not add verbose or speculative documentation

# Validation steps
1. Restore and build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are added, ensure they compile and are included in the correct startup project/persistence project pattern used by the repo.

4. Manually verify these scenarios through tests or a minimal execution path:
   - agent with default autonomy level cannot execute a sensitive action directly
   - out-of-scope action is denied before tool execution
   - missing/ambiguous policy config results in deny
   - above-threshold action creates approval instead of executing
   - allowed in-scope action proceeds and stores structured policy decision metadata

5. Confirm persistence shape:
   - policy decision metadata is structured and serializable
   - approval request records contain threshold context
   - denied attempts are traceable for future audit views

6. Confirm no bypass path exists:
   - search for tool execution entry points and ensure guardrail evaluation is enforced consistently before execution

# Risks and follow-ups
- **Risk: unclear existing orchestration structure**
  - Mitigation: integrate at the narrowest existing tool execution seam rather than introducing parallel execution paths.

- **Risk: approval domain may be incomplete**
  - Mitigation: implement the smallest viable approval creation path needed for pending requests, without overbuilding full approval workflows.

- **Risk: policy config shape may still be evolving**
  - Mitigation: keep decision logic explicit and conservative; default-deny on missing or ambiguous config.

- **Risk: autonomy semantics may be interpreted inconsistently later**
  - Mitigation: centralize semantics in one guardrail engine and one set of reason codes so future stories can extend rather than duplicate logic.

- **Risk: denied actions may currently not have a persistence path**
  - Mitigation: persist structured decision metadata in the nearest existing execution/audit record model.

Follow-ups after this task, if not already covered elsewhere:
- richer approval routing and decision chains for **ST-403**
- broader policy-enforced tool execution coverage for **ST-503**
- audit/explainability UI for **ST-602**
- agent profile UX for autonomy/guardrail configuration under **ST-204**