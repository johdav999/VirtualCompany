# Goal
Implement backlog task **TASK-8.3.2** for **ST-203 Autonomy levels and policy guardrails** by adding a **pre-execution policy evaluation component** in the .NET backend that determines whether a requested tool action is allowed, denied, or requires approval before any tool runs.

The implementation must ensure the policy engine evaluates:
- **action scope**: `read`, `recommend`, `execute`
- **agent autonomy level**: `0-3`
- **configured thresholds**
- **approval requirements**

The result must support the architecture’s guardrail requirements:
- **default deny** when config is missing or ambiguous
- **pre-execution enforcement**
- **structured decision output** suitable for persistence in `tool_executions.policy_decision_json` and future audit events
- **tenant-aware and agent-aware evaluation**

Produce a clean, testable implementation aligned with the modular monolith / clean architecture approach already used in the solution.

# Scope
In scope:
- Add domain/application models for policy evaluation inputs and outputs
- Implement a policy evaluation service for tool execution requests
- Encode decision outcomes such as:
  - allowed
  - denied
  - approval required
- Evaluate:
  - tenant/company context
  - agent status and autonomy level
  - action type (`read`, `recommend`, `execute`)
  - tool permission scope
  - threshold rules
  - approval requirement rules
- Return structured denial/approval reasons
- Add unit tests covering conservative/default-deny behavior and representative scenarios
- Integrate at the application/service layer where tool execution requests are prepared, if a suitable orchestration/tool execution seam already exists

Out of scope unless already trivial in the codebase:
- Full approval workflow creation UI
- Full persistence wiring for `tool_executions`
- New external integrations
- Broad refactors unrelated to policy evaluation
- Post-execution auditing screens

If the exact orchestration seam does not yet exist, create the smallest clean abstraction needed now so later tool execution code can call the policy engine before execution.

# Files to touch
Inspect the solution first and then touch only the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/...`
  - policy-related enums/value objects/entities
  - agent/autonomy/tool permission models if missing
- `src/VirtualCompany.Application/...`
  - policy evaluation contracts and DTOs
  - orchestration/tool execution application service integration
- `src/VirtualCompany.Infrastructure/...`
  - only if an implementation belongs there due to existing dependency patterns
- test projects in the solution
  - add unit tests for policy engine behavior

Potential file patterns to look for before coding:
- agent configuration models
- tool execution abstractions
- approval-related models
- orchestration services
- existing enums for agent status, autonomy, action type, or approval state

Prefer extending existing files/types over creating parallel duplicates.

# Implementation plan
1. **Inspect the current architecture in code**
   - Find where agent configuration is modeled.
   - Find whether there is already:
     - a tool executor
     - orchestration service
     - approval model
     - policy/authorization abstraction
   - Reuse existing enums and contracts where possible.

2. **Define policy evaluation contract**
   Create a clear application-facing contract, for example:
   - `IPolicyGuardrailEngine` or `IToolExecutionPolicyEvaluator`
   - input model containing:
     - `CompanyId`
     - `AgentId`
     - agent status
     - autonomy level
     - tool name
     - action type
     - request payload / threshold context
     - agent tool permissions config
     - agent approval thresholds config
     - company compliance region if already available
   - output model containing:
     - decision: `Allowed | Denied | ApprovalRequired`
     - machine-readable reason codes
     - human-readable summary
     - normalized threshold/approval context
     - structured metadata for persistence/audit

3. **Model decision semantics explicitly**
   Add enums/value objects if missing:
   - `ToolActionType`: `Read`, `Recommend`, `Execute`
   - `PolicyDecisionType`: `Allowed`, `Denied`, `ApprovalRequired`
   - reason codes such as:
     - `AgentInactive`
     - `AutonomyLevelInsufficient`
     - `ActionOutsideScope`
     - `MissingPolicyConfiguration`
     - `ThresholdExceeded`
     - `ApprovalRequiredByPolicy`
     - `AmbiguousPolicy`
   Keep these stable and serializable.

4. **Implement conservative guardrail rules**
   The evaluator should enforce at least:
   - deny if agent is not active or is restricted/archived/paused for execution
   - deny if tool/action is not explicitly permitted
   - deny if policy config is missing or ambiguous
   - deny if autonomy level does not permit the requested action
   - require approval if thresholds or policy rules indicate approval is needed
   - allow only when all checks pass clearly

   Suggested baseline autonomy behavior if not already encoded elsewhere:
   - `0`: no direct tool execution; likely only constrained/non-operative behavior
   - `1`: read/recommend only within explicit scope
   - `2`: limited execute allowed within explicit scope and thresholds
   - `3`: broader execute allowed, still subject to thresholds/approval
   If the codebase already defines autonomy semantics, follow that instead of inventing a conflicting model.

5. **Support threshold evaluation**
   Threshold handling should be generic and structured, not hardcoded to one business domain.
   Examples:
   - request payload may include threshold-relevant values such as amount/risk/sensitivity
   - compare against configured threshold rules if present
   - if threshold exceeds auto-execution allowance, return `ApprovalRequired`
   - if threshold config is required for that tool/action but absent, default deny

   Keep implementation pragmatic:
   - use a simple normalized threshold context object
   - avoid overengineering a full rules engine if not needed for this task

6. **Support approval requirement evaluation**
   If policy config indicates approval is required for:
   - specific tools
   - execute actions
   - threshold exceedance
   - sensitive operations

   then return a decision object that clearly signals approval is required and includes enough context for a later approval creation flow.

7. **Integrate at the pre-execution seam**
   If there is an existing tool execution/orchestration service:
   - invoke the policy evaluator before any tool call
   - short-circuit execution on `Denied` or `ApprovalRequired`
   - ensure no downstream tool invocation occurs in those cases

   If no seam exists:
   - add a minimal orchestration-facing method or wrapper that demonstrates intended usage
   - do not build a full tool executor if absent

8. **Make output persistence-ready**
   Ensure the decision result can be serialized cleanly to JSON for future storage in:
   - `tool_executions.policy_decision_json`
   and future audit events.
   Include:
   - decision
   - reason codes
   - evaluated action/tool
   - autonomy level used
   - threshold evaluation details
   - approval requirement details

9. **Add tests**
   Add focused unit tests for:
   - allowed read within scope
   - denied when tool/action not in scope
   - denied when policy config missing
   - denied when autonomy insufficient
   - approval required when threshold exceeded
   - approval required when execute action requires approval by policy
   - denied/blocked when agent status is paused/restricted/archived as appropriate
   - no tool execution occurs when decision is denied or approval-required, if integration tests are feasible in current structure

10. **Keep code clean**
   - Follow existing naming and project conventions
   - Keep business rules in domain/application layer, not controllers/UI
   - Avoid direct DB dependencies in the evaluator unless the existing architecture requires repository access
   - Prefer pure functions / deterministic evaluation where possible

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify policy evaluator behavior through automated tests covering:
   - explicit allow
   - explicit deny
   - approval required
   - default deny on missing/ambiguous config

4. If an orchestration/tool execution seam exists, verify:
   - policy evaluation happens before execution
   - denied requests do not invoke the tool
   - approval-required requests do not invoke the tool

5. Confirm output models are JSON-serializable and suitable for persistence/audit metadata.

6. Ensure no unrelated project warnings/errors were introduced.

# Risks and follow-ups
- **Risk: autonomy semantics may already exist elsewhere**
  - Mitigation: inspect and reuse existing definitions rather than introducing conflicting rules.

- **Risk: agent config may be stored as flexible JSON without strong types**
  - Mitigation: add minimal typed adapters/normalizers for policy evaluation instead of spreading JSON parsing logic.

- **Risk: no current tool execution seam exists**
  - Mitigation: implement the evaluator and a minimal integration abstraction now, leaving full orchestration hookup for the next task.

- **Risk: threshold rules may be underspecified**
  - Mitigation: implement a generic threshold context and conservative behavior; document assumptions in code comments/tests.

- **Risk: approval creation may belong to another task**
  - Mitigation: for this task, return structured `ApprovalRequired` results rather than building the full approval workflow unless already present.

Follow-up items to note in comments/TODOs only if necessary:
- wire policy decisions into persisted `tool_executions`
- emit business audit events for denied/approval-required outcomes
- connect `ApprovalRequired` decisions to approval request creation flow
- extend policy engine later for compliance region rules and redaction rules