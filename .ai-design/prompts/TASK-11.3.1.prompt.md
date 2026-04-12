# Goal
Implement backlog task **TASK-11.3.1** for **ST-503 Policy-enforced tool execution** by adding the policy evaluation path that checks every tool execution request for:

- **tenant scope**
- **action type** (`read`, `recommend`, `execute`)
- **agent autonomy level**
- **configured thresholds**
- **approval requirements**

The implementation must ensure policy checks happen **before any tool execution**, default to **deny** when configuration is missing or ambiguous, persist structured policy decisions for allowed/denied outcomes, and support safe user-facing denial behavior plus auditability.

# Scope
In scope:

- Add or complete a **policy guardrail evaluation service** in the application/domain orchestration path.
- Define a **typed policy evaluation input/output contract** for tool execution requests.
- Enforce checks for:
  - company/tenant isolation
  - agent status and company ownership
  - tool/action permission scope
  - autonomy level gating
  - threshold evaluation
  - approval-required outcomes
- Update tool execution flow so:
  - **allowed** requests proceed
  - **approval-required** requests do **not** execute directly
  - **denied** requests do **not** execute
- Persist `tool_executions` metadata including structured `policy_decision_json`.
- Create audit records for denied and approval-gated actions where the current architecture supports it.
- Add tests for the policy decision matrix and orchestration behavior.

Out of scope unless required by existing code patterns:

- Building full approval UX
- Adding new external connectors
- Large schema redesigns beyond what is needed for policy decision persistence
- Broad refactors unrelated to tool execution guardrails

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:

- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

1. **Domain / policy models**
   - Tool execution request model
   - Policy decision/result model
   - Enums/value objects for action type, decision outcome, denial reasons

2. **Application / orchestration**
   - Tool executor service
   - Policy guardrail engine/service
   - Command/handler or orchestration pipeline that initiates tool execution
   - Approval creation integration point if already present

3. **Infrastructure / persistence**
   - EF Core entity/configuration for `tool_executions`
   - JSON serialization for `policy_decision_json`
   - Repository updates for persisting execution attempts and decisions
   - Migration if the current schema is missing required fields

4. **API / contracts**
   - Only if current API surfaces tool execution outcomes directly and needs safe denial messaging

5. **Tests**
   - Unit tests for policy evaluation
   - Integration/application tests for orchestration behavior
   - Persistence assertions for `tool_executions` and policy decision metadata

Also inspect:

- existing approval module contracts
- existing audit event creation patterns
- existing tenant resolution and authorization helpers
- existing agent configuration models for `autonomy_level`, `tool_permissions_json`, `approval_thresholds_json`, `data_scopes_json`, `status`

# Implementation plan
1. **Discover existing implementation and align with current architecture**
   - Search for:
     - tool executor
     - orchestration service
     - agent configuration models
     - approval services
     - audit event services
     - `tool_executions`
     - policy/guardrail-related classes
   - Reuse existing abstractions and naming where possible.
   - Do not introduce a parallel policy system if one already exists.

2. **Define the policy evaluation contract**
   Create or refine a typed request model for policy evaluation that includes at minimum:

   - `CompanyId`
   - `AgentId`
   - `TaskId` / `WorkflowInstanceId` if available
   - `ToolName`
   - `ActionType`
   - normalized request payload / threshold context
   - execution actor/context metadata

   Create a structured result model with fields such as:

   - `Outcome` = `Allowed | Denied | ApprovalRequired`
   - `Reasons` / `ReasonCodes`
   - `EvaluatedAutonomyLevel`
   - `RequiredAutonomyLevel` if applicable
   - `ThresholdEvaluations`
   - `ApprovalRequirement`
   - `PolicyVersion` or evaluation metadata if practical
   - safe user-facing explanation text

   Keep the result serializable for `policy_decision_json`.

3. **Implement pre-execution guardrail evaluation**
   In the orchestration/tool execution pipeline, ensure policy evaluation runs **before** any internal tool handler or integration adapter is invoked.

   Evaluation rules should include:

   - **Tenant scope**
     - Agent must belong to the same company as the execution context.
     - Any referenced task/workflow/company-owned entity must match the same `company_id`.
     - Tool request must not be able to target another tenant.

   - **Agent status**
     - Deny if agent is `paused`, `restricted`, or `archived` when execution should not proceed under current business rules.
     - At minimum, deny archived and restricted unless existing rules explicitly allow some read-only behavior.

   - **Action type scope**
     - Validate requested action type against the tool permission configuration.
     - Respect separation of `read`, `recommend`, and `execute`.
     - Default deny if tool permission config is missing or ambiguous.

   - **Autonomy level**
     - Enforce conservative defaults.
     - Example intent:
       - Level 0: likely no autonomous execution, recommendations only
       - Level 1: limited low-risk actions
       - Level 2/3: broader execution depending on policy
     - Use existing story guidance and current code conventions; if no mapping exists, implement a minimal explicit mapping and document it in code comments/tests.

   - **Thresholds**
     - Evaluate request payload against configured thresholds where applicable.
     - Threshold logic should be deterministic and typed where possible.
     - If threshold data required for a sensitive action is missing or cannot be interpreted, default to deny or approval-required based on safest existing policy convention; prefer deny when ambiguous.

   - **Approval requirements**
     - If action exceeds direct execution authority but is otherwise valid, return `ApprovalRequired`.
     - Do not execute the tool in this case.
     - If approval creation infrastructure exists, create/link an approval request.
     - If approval creation is not yet wired in this path, still return/persist the approval-required decision cleanly.

4. **Persist tool execution attempts and policy decisions**
   Update persistence so `tool_executions` records include:

   - company/task/workflow/agent linkage
   - tool name
   - action type
   - request payload
   - response payload when executed
   - status
   - structured `policy_decision_json`
   - timestamps

   Expected behavior:
   - **Allowed + executed**: persist request, response, status, policy decision
   - **Denied**: persist request, no tool execution response except safe structured denial metadata, status reflecting denial/block
   - **ApprovalRequired**: persist request and policy decision, status reflecting awaiting approval / not executed

   Use existing status conventions if present; otherwise add minimal values consistent with current model.

5. **Return safe denial/approval-required behavior**
   Ensure denied requests produce a safe explanation suitable for user-facing surfaces:
   - no internal stack traces
   - no sensitive policy internals
   - concise explanation such as action outside scope, approval required, or tenant mismatch

   Keep detailed reasons in structured policy metadata and audit records.

6. **Create audit records**
   Where the audit module already exists, emit business audit events for:
   - denied tool execution
   - approval-required tool execution
   - successful execution if not already covered elsewhere

   Include:
   - actor type = agent/system as appropriate
   - action
   - target type/id if known
   - outcome
   - rationale summary / reason summary
   - company scope

7. **Use typed internal tool contracts**
   Ensure internal tools continue to call domain/application services through typed contracts rather than direct DB access.
   If any current implementation bypasses this in the touched path, correct it minimally.

8. **Add tests**
   Add focused tests for the decision matrix, including at minimum:

   - allows execution when tenant, action type, autonomy, and thresholds all pass
   - denies when company/tenant scope mismatches
   - denies when tool permission/action type is not allowed
   - denies or blocks when agent status is not executable
   - denies when policy config is missing/ambiguous
   - returns approval-required when threshold/autonomy requires approval
   - does not invoke tool handler when denied
   - does not invoke tool handler when approval is required
   - persists `policy_decision_json` for all outcomes

9. **Keep implementation incremental and idiomatic**
   - Prefer small, composable services over one large method.
   - Keep business rules in application/domain layers, not controllers.
   - Avoid speculative abstractions beyond what this task needs.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Verify behavior through tests or existing integration harness:
   - allowed request executes tool and persists execution record
   - denied request does not execute tool and persists denial decision
   - approval-required request does not execute tool and persists approval-needed decision

5. If EF Core migrations are required:
   - generate the migration using the repo’s existing migration approach
   - verify the model snapshot/build remains valid
   - ensure migration only contains necessary schema changes

6. Manually inspect serialized `policy_decision_json` shape for:
   - stable field names
   - useful reason codes
   - no sensitive internals leaked into user-facing text

7. Confirm no cross-tenant execution path remains in touched code by reviewing:
   - repository filters
   - orchestration context resolution
   - tool request construction

# Risks and follow-ups
- **Risk: unclear existing autonomy/threshold model**
  - Mitigation: implement a minimal explicit rule set aligned to ST-203/ST-503 and encode it in tests.
  - Follow-up: formalize a shared policy DSL/config schema if not already present.

- **Risk: approval infrastructure may be incomplete in this path**
  - Mitigation: support `ApprovalRequired` as a first-class decision even if full approval creation is partially stubbed.
  - Follow-up: wire automatic approval request creation end-to-end with ST-403 integration if missing.

- **Risk: JSONB policy config may be loosely typed**
  - Mitigation: normalize into typed application models before evaluation and default deny on parse ambiguity.
  - Follow-up: add stronger validation at agent profile save time.

- **Risk: existing tool execution statuses may not distinguish denied vs awaiting approval**
  - Mitigation: add minimal status values only if necessary and keep naming consistent with current conventions.
  - Follow-up: standardize execution lifecycle states across orchestration and audit views.

- **Risk: audit/event patterns may not yet be centralized**
  - Mitigation: reuse existing audit service if present; otherwise keep additions minimal and localized.
  - Follow-up: unify audit emission across orchestration, approvals, and workflows.

- **Risk: hidden direct data access in internal tools**
  - Mitigation: correct any touched path to use typed contracts only.
  - Follow-up: audit all tools for compliance with the internal tool abstraction rule.