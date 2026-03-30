# Goal
Implement backlog task **TASK-8.3.4** for story **ST-203 — Autonomy levels and policy guardrails** so that **sensitive actions above configured thresholds create approval requests instead of executing directly**.

The coding agent should make this behavior part of the **pre-execution policy guardrail flow** in the shared orchestration/tool execution pipeline, aligned with the architecture requirement that guardrails run **before** tool execution and default to **conservative behavior**.

Expected end result:
- When an agent attempts a sensitive `execute` action that exceeds configured thresholds, the system does **not** perform the action immediately.
- Instead, it creates an **approval request** tied to the relevant task/workflow/action context.
- The attempted execution is recorded with a structured policy decision indicating approval is required.
- The user-facing result is safe and explicit: action is pending approval rather than completed.
- Tenant isolation, auditability, and typed application boundaries are preserved.

# Scope
Focus only on the minimum vertical slice needed for this task in the existing .NET modular monolith.

In scope:
- Policy decision path for tool/action execution
- Threshold evaluation for sensitive actions
- Creation of approval requests when threshold is exceeded
- Preventing direct execution in those cases
- Persisting structured policy decision metadata
- Updating linked task state if applicable (for example `awaiting_approval`)
- Tests covering the new behavior

Out of scope unless already partially implemented and required to complete this task:
- Full approval inbox UX
- Multi-step approval chains beyond the simplest supported model
- Mobile changes
- New workflow builder features
- Broad refactors unrelated to policy/approval execution
- New external integrations

Assumptions to validate in the codebase before implementation:
- There is already some concept of tool execution, policy evaluation, approvals, tasks, and audit events.
- There may already be enums/value objects for action type (`read`, `recommend`, `execute`) and approval status.
- There may already be an application service or orchestration service where tool execution requests are routed.

If existing patterns differ from the architecture notes, follow the repository’s established conventions and keep changes localized.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- possibly `src/VirtualCompany.Api`
- possibly `src/VirtualCompany.Web` only if there is already a minimal approval/task status surface that must reflect `awaiting_approval`

Likely file categories to touch:
- Domain entities / enums / value objects for:
  - policy decisions
  - approvals
  - tool executions
  - task status
- Application command/handler/service files for:
  - tool execution orchestration
  - policy guardrail evaluation
  - approval request creation
- Infrastructure persistence files for:
  - EF Core entity configurations
  - repositories
  - migrations
- Tests in corresponding test projects for:
  - policy engine behavior
  - application handler behavior
  - persistence if needed

Likely concrete targets based on naming conventions to search for:
- `*Policy*`
- `*Guardrail*`
- `*ToolExecution*`
- `*Approval*`
- `*Task*`
- `*Orchestration*`
- `*ExecuteTool*`
- `*ApprovalRequest*`
- `*Audit*`

Also inspect:
- `README.md`
- solution/project references
- existing migrations
- test projects and naming conventions

# Implementation plan
1. **Discover the current execution and approval flow**
   - Search the solution for:
     - tool execution entry points
     - policy evaluation services
     - approval creation handlers/services
     - task status transitions
   - Identify the single place where an agent action request is evaluated before execution.
   - Confirm how tenant/company context is passed through the application layer.
   - Confirm whether approvals already exist as a domain entity and whether there is an application service for creating them.

2. **Model the policy outcome for “approval required”**
   - Ensure the policy evaluation result can distinguish at least:
     - allowed
     - denied
     - approval required
   - If a structured policy decision object already exists, extend it rather than inventing a parallel model.
   - Include enough metadata for auditability, such as:
     - reason code
     - threshold name/type exceeded
     - threshold values/context
     - action type
     - tool name
     - sensitivity indicator
     - whether execution was blocked pending approval
   - Keep the decision serializable into `tool_executions.policy_decision_json` or equivalent persistence already used.

3. **Implement threshold evaluation for sensitive actions**
   - In the policy guardrail engine, add or complete logic so that:
     - `read` and `recommend` actions continue through existing rules
     - `execute` actions are checked against autonomy level, permissions, and thresholds
     - if the action is sensitive and above configured threshold, the result is `approval required`
   - Follow the story guidance:
     - pre-execution only
     - default-deny or conservative behavior when config is missing/ambiguous
   - Reuse existing agent configuration fields such as `approval_thresholds_json` if already mapped.
   - Do not hardcode business-specific thresholds unless the codebase already uses a simple generic threshold model. Prefer extending the existing threshold abstraction.

4. **Short-circuit direct execution when approval is required**
   - In the application/orchestration service that would normally execute the tool:
     - evaluate policy first
     - if result is `approval required`, do not call the actual tool/integration/domain action
   - Persist a `tool_execution` record or equivalent attempt record with:
     - request payload
     - status indicating not executed / awaiting approval / blocked pending approval, matching existing conventions
     - structured policy decision metadata
   - Return a safe application result indicating the action was submitted for approval rather than executed.

5. **Create an approval request**
   - Use the existing approval module if present.
   - Create an approval record tied to the relevant entity context:
     - `entity_type`: likely `action`, `task`, or existing equivalent
     - `entity_id`: whichever entity is the canonical link in the current design
     - `requested_by_actor_type`: likely `agent`
     - `requested_by_actor_id`: agent id if available
     - `approval_type`: use an existing type or add a narrowly scoped one for sensitive action execution
     - `threshold_context_json`: include threshold and action details
     - `status`: `pending`
   - If the system already supports role-based approvers, populate required role/user from existing policy config.
   - Avoid inventing a complex approval chain if the current implementation only supports single-step approvals.

6. **Update task/workflow state where applicable**
   - If the action is associated with a task, move the task to `awaiting_approval` if that status already exists in the domain.
   - If linked to a workflow step, use the existing blocked/awaiting approval mechanism if present.
   - Do not introduce inconsistent state transitions; follow current aggregate/application patterns.

7. **Add audit/event recording**
   - If the codebase already records business audit events for policy decisions or approval creation, emit:
     - policy evaluation outcome = approval required
     - approval request created
   - Keep rationale concise and operational.
   - Do not add raw chain-of-thought or verbose LLM internals.

8. **Persistence updates**
   - If needed, update EF mappings and add a migration for:
     - new enum/string values
     - new columns only if absolutely necessary
   - Prefer using existing JSON fields and status fields over schema expansion unless the current model cannot represent the new state.
   - Ensure tenant/company scoping is preserved on all new records.

9. **Tests**
   - Add unit tests for policy evaluation:
     - sensitive execute action above threshold => approval required
     - ambiguous/missing threshold config => conservative deny or approval-required per existing policy rules
     - below-threshold execute action => existing allowed path still works
   - Add application/service tests:
     - approval-required path does not invoke tool executor
     - approval record is created
     - task status becomes `awaiting_approval` when linked to a task
     - tool execution attempt stores structured policy decision metadata
   - Add integration/persistence tests if the repository already uses them for approvals/tool executions.

10. **Keep implementation aligned with existing architecture**
   - Respect clean boundaries:
     - domain rules in domain/application services
     - persistence in infrastructure
     - no direct DB access from orchestration/tool adapters
   - Use typed contracts between orchestration and domain modules.
   - Keep changes incremental and easy to review.

Suggested implementation shape if no exact pattern exists:
- Domain/Application:
  - extend `PolicyDecision` or equivalent with `ApprovalRequired`
  - add `ApprovalRequestFactory` / command / service usage
- Orchestration/Application:
  - in `ExecuteTool...Handler` or equivalent:
    1. build policy input
    2. evaluate policy
    3. if allowed => execute tool
    4. if denied => return safe denial
    5. if approval required => create approval, persist attempt, update task state, return pending result
- Infrastructure:
  - persist approval and tool execution metadata
  - migration only if required

# Validation steps
1. **Codebase inspection**
   - Confirm actual implementation points before editing:
     - `dotnet build`
     - search for policy/tool/approval/task handlers and entities

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run all tests:
     - `dotnet test`
   - If the suite is large, also run targeted tests for the touched modules first.

4. **Behavior verification**
   - Verify with automated tests or existing integration harness that:
     - a sensitive execute action above threshold does not execute
     - an approval row is created with `pending` status
     - the linked task becomes `awaiting_approval` if applicable
     - the tool execution record contains policy decision metadata
     - the returned result clearly indicates approval is required

5. **Regression checks**
   - Verify existing allowed execution paths still execute normally.
   - Verify denied actions still remain denied and are not converted into approvals unless policy explicitly requires approval.
   - Verify tenant/company scoping is preserved on approval and tool execution records.

6. **Migration check**
   - If a migration is added:
     - ensure it is included in the correct infrastructure project
     - verify the solution builds and tests pass with the migration present

# Risks and follow-ups
- **Risk: unclear existing policy model**
  - The repository may already encode policy outcomes differently. Extend existing abstractions instead of layering a second approval-decision model.

- **Risk: duplicate approval creation**
  - If retries or repeated orchestration calls occur, ensure approval creation is idempotent or guarded to avoid duplicate pending approvals for the same action attempt.

- **Risk: ambiguous threshold configuration**
  - Story guidance says default-deny when config is missing or ambiguous. Be explicit in code and tests about whether the result should be deny vs approval-required for each ambiguity case.

- **Risk: task/workflow state mismatch**
  - Only transition to `awaiting_approval` where the current aggregate/application flow supports it. Avoid partial updates across task, approval, and tool execution records.

- **Risk: status naming mismatch**
  - The actual code may use different status names than the architecture notes. Reuse existing enums/constants where possible.

- **Risk: approval linkage design**
  - The architecture allows approvals for `task`, `workflow`, or `action`. Use the canonical entity linkage already present in the codebase rather than inventing a new relationship unless necessary.

Follow-ups after this task, if not already covered elsewhere:
- approval decision resume flow so approved actions can continue execution safely
- approval inbox/web UI improvements
- richer threshold policy configuration and validation
- audit/explainability views surfacing threshold reason and approval context
- idempotent resubmission/resume semantics for approved actions