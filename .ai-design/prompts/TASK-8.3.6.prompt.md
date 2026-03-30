# Goal
Implement **TASK-8.3.6 — Keep policy decisions structured for auditability** for **ST-203 Autonomy levels and policy guardrails** in the existing .NET solution.

The coding agent should ensure that policy/guardrail decisions made before tool execution are captured in a **structured, durable, tenant-scoped format** suitable for downstream audit, explainability, approvals, and reporting.

This task should align with the architecture guidance that:
- guardrails run **pre-execution**
- policy behavior is **default-deny**
- blocked/out-of-scope actions are **logged with reason**
- policy decisions remain **structured for auditability**, not buried in free-form logs

# Scope
Focus only on the implementation needed to make policy decisions first-class structured artifacts in the backend domain/application/infrastructure layers.

Include:
- A structured policy decision model/value object/DTO that captures:
  - tenant/company context
  - actor/agent context
  - tool/action context
  - evaluated autonomy/action type
  - decision outcome
  - machine-readable denial/approval reasons
  - threshold/approval requirement details where applicable
  - timestamp/correlation metadata as appropriate
- Persistence mapping so policy decisions can be stored in `tool_executions.policy_decision_json` and/or linked audit records in a consistent schema
- Updates to the policy guardrail evaluation flow so every evaluation returns a structured decision object
- Safe serialization of the structured decision into persistence
- Audit event creation or enrichment using the structured decision where appropriate
- Unit/integration tests for serialization, default-deny behavior, and representative allow/deny/approval-required outcomes

Do not expand scope into:
- full UI/audit screens
- mobile work
- broad workflow/approval UX
- unrelated orchestration redesign
- introducing microservices or external infra

If the codebase already has partial policy enforcement, extend/refactor it rather than duplicating concepts.

# Files to touch
Inspect and update the most relevant files under these projects as needed:

- `src/VirtualCompany.Domain`
  - policy/guardrail domain models
  - tool execution entities
  - audit-related entities/value objects
- `src/VirtualCompany.Application`
  - orchestration services
  - policy evaluation interfaces/services
  - command/query handlers related to tool execution
  - DTOs/contracts for structured policy decisions
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration
  - JSON serialization/value conversion for `policy_decision_json`
  - repositories/persistence adapters
  - audit persistence wiring
- `src/VirtualCompany.Api`
  - DI registration or API-layer contract adjustments only if required
- `src/VirtualCompany.Shared`
  - shared enums/contracts only if there is an established pattern here
- Test projects in the solution
  - add or update tests covering policy decision structure and persistence

Also inspect:
- `README.md`
- solution/project files only if new files must be included

# Implementation plan
1. **Discover existing policy/tool execution flow**
   - Find current implementations for:
     - policy guardrail engine
     - tool execution pipeline
     - `tool_executions`
     - audit event creation
     - approval request creation
   - Identify existing enums/types for:
     - autonomy levels
     - action types (`read`, `recommend`, `execute`)
     - execution status
     - approval requirements
   - Reuse existing naming and layering conventions.

2. **Define a structured policy decision contract**
   - Introduce a strongly typed model for policy decisions, preferably in Domain or Application depending on current architecture.
   - The model should be explicit and stable, with machine-readable fields rather than only prose.
   - Suggested shape:
     - `Decision` / `Outcome` (`Allowed`, `Denied`, `ApprovalRequired`)
     - `DecisionReasonCodes` collection
     - `DecisionSummary`
     - `CompanyId`
     - `AgentId`
     - `TaskId` / `WorkflowInstanceId` if available
     - `ToolName`
     - `ActionType`
     - `AutonomyLevel`
     - `ThresholdEvaluations`
     - `ApprovalRequirement`
     - `EvaluatedPolicyVersion` if available
     - `EvaluatedAt`
     - `CorrelationId` / `ExecutionId` if available
     - optional `Metadata`
   - Add nested typed records for threshold checks / approval requirement details instead of opaque blobs where practical.

3. **Add reason codes and default-deny semantics**
   - Introduce machine-readable reason codes for common outcomes, such as:
     - missing_policy_configuration
     - ambiguous_policy_configuration
     - action_type_not_permitted
     - tool_not_permitted
     - autonomy_level_insufficient
     - threshold_exceeded
     - approval_required
     - tenant_scope_violation
     - data_scope_violation
   - Ensure missing or ambiguous config yields a structured **Denied** decision with explicit reason code(s).
   - Keep any human-readable message concise and derived from structured fields.

4. **Refactor policy evaluation to always return structured decisions**
   - Update the policy guardrail service/interface so evaluation returns the structured decision object, not just bool/exception/string.
   - Ensure all branches produce a decision:
     - allowed
     - denied
     - approval required
   - Preserve pre-execution enforcement.
   - Avoid throwing for expected business denials; reserve exceptions for technical failures.

5. **Persist structured decisions on tool executions**
   - Update the `tool_executions` persistence path so `policy_decision_json` stores the structured decision schema.
   - Use typed serialization with deterministic property names.
   - If EF Core currently maps this as string/JSONB, add or refine the converter/configuration.
   - Ensure persisted JSON is queryable and stable enough for future audit/reporting.

6. **Enrich audit events from structured decisions**
   - Where tool execution attempts or denials create audit events, populate them from the structured decision.
   - Ensure audit records can reflect:
     - actor
     - action attempted
     - target/tool
     - outcome
     - rationale summary
     - structured source details or references
   - Do not store chain-of-thought; only operationally useful summaries and structured reasons.

7. **Handle approval-required outcomes cleanly**
   - If the current flow creates approval requests for threshold-sensitive actions, ensure the structured decision explicitly records:
     - why approval is required
     - which threshold/rule triggered it
     - any required role/user if known at evaluation time
   - Persist the decision even when execution does not proceed.

8. **Backwards compatibility and migration**
   - If needed, add a migration or configuration update for `policy_decision_json` handling.
   - Do not require destructive schema changes unless unavoidable.
   - If legacy free-form policy decision data exists, keep compatibility where practical.

9. **Add tests**
   - Unit tests for policy evaluation:
     - allowed decision
     - denied due to missing config
     - denied due to scope violation
     - approval required due to threshold
   - Serialization/persistence tests:
     - structured decision round-trips to/from JSON
     - persisted JSON contains expected machine-readable fields
   - If integration tests exist for tool execution/audit:
     - verify denied or approval-required attempts still persist structured policy decisions

10. **Keep implementation minimal and aligned**
   - Prefer small, composable changes over broad framework additions.
   - Follow existing project conventions, namespaces, and test style.
   - Add brief code comments only where the structure or default-deny behavior is non-obvious.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify code-level behavior:
   - Confirm policy evaluation returns a structured decision object for all expected paths.
   - Confirm missing/ambiguous policy config results in explicit **Denied** with reason code(s).
   - Confirm approval-required outcomes are distinct from allowed/denied outcomes.

4. Verify persistence behavior:
   - Confirm `tool_executions.policy_decision_json` is populated with structured JSON for attempted tool executions.
   - Confirm JSON includes stable machine-readable fields such as outcome, reason codes, action type, autonomy level, and threshold/approval details.

5. Verify auditability behavior:
   - Confirm blocked/out-of-scope actions produce audit-friendly structured information.
   - Confirm human-readable summaries are concise and derived from structured decision data.

6. If migrations/config changes are introduced:
   - Ensure the app still builds and tests pass after applying them.
   - Confirm no unrelated schema drift is introduced.

# Risks and follow-ups
- **Risk: existing policy flow may return booleans/exceptions only**
  - Mitigation: introduce an adapter layer that preserves current callers while migrating to structured decisions.

- **Risk: inconsistent enums/strings across layers**
  - Mitigation: centralize outcome/action/reason code definitions and reuse them across serialization and tests.

- **Risk: JSON schema instability**
  - Mitigation: use explicit typed contracts and deterministic serialization settings; avoid anonymous objects.

- **Risk: audit duplication between tool execution and audit events**
  - Mitigation: keep `policy_decision_json` as the source structured artifact and derive audit summaries from it.

- **Risk: overreaching into approvals/workflows**
  - Mitigation: only capture approval-required decision details needed for auditability; do not redesign approval orchestration here.

Follow-ups to note in code comments or task notes if not completed here:
- add query/reporting support over structured policy decision JSON
- expose structured policy decisions in audit/explainability UI
- standardize policy versioning/rule identifiers if not yet present
- consider indexing selected JSON fields in PostgreSQL if audit queries become frequent