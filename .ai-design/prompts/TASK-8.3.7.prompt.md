# Goal
Implement backlog task **TASK-8.3.7 — Default-deny when policy config is missing or ambiguous** for story **ST-203 Autonomy levels and policy guardrails**.

The coding agent should update the policy guardrail path so that **tool execution is denied by default whenever required policy configuration is absent, incomplete, invalid, or ambiguous**. This must happen **before any tool executes** and must produce **structured denial metadata** suitable for auditability and safe user-facing responses.

This task should align with the architecture and backlog expectations for:
- conservative autonomy defaults
- pre-execution guardrails
- structured policy decisions
- approval-first handling for sensitive actions
- explicit default-deny behavior when config cannot be confidently interpreted

# Scope
In scope:
- Policy evaluation logic used before tool execution
- Default-deny behavior for missing/ambiguous policy config
- Structured denial reasons and machine-readable decision payloads
- Safe handling of null/empty/malformed policy-related agent config
- Unit/integration tests covering deny-by-default scenarios
- Minimal supporting domain/application changes needed to represent denial reasons consistently
- Logging/audit hooks only if already part of the policy execution path

Out of scope unless required by existing code structure:
- New UI screens
- Broad refactors of orchestration or approval systems
- New database schema unless absolutely necessary
- Full audit/explainability feature work beyond what is needed for structured policy decisions
- Mobile/web UX changes
- New tools/connectors

Assumptions to preserve:
- Agents support autonomy levels 0–3
- Policy checks occur pre-execution
- Read/recommend/execute action types are distinct
- Missing or unclear config must not silently fall back to permissive behavior
- If approval requirements cannot be determined confidently, the result should be **deny**, not allow

# Files to touch
Inspect the solution first and then touch the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - policy decision models
  - enums/value objects for action type, autonomy level, denial reasons
  - agent policy/config validation primitives

- `src/VirtualCompany.Application/**`
  - policy guardrail service / evaluator
  - tool execution orchestration path
  - command/query handlers that invoke policy checks
  - DTOs/contracts for policy decision results

- `src/VirtualCompany.Infrastructure/**`
  - persistence mapping if policy decision payloads are stored
  - repository implementations if config loading behavior needs tightening

- `src/VirtualCompany.Api/**`
  - only if API contracts or error mapping need adjustment for safe denied responses

- Test projects under `tests/**` or existing test locations
  - add/extend unit tests for policy evaluator
  - add orchestration/tool execution tests if present

Also inspect:
- `README.md`
- solution/project files for test project discovery
- any existing files related to:
  - `PolicyGuardrailEngine`
  - `ToolExecutor`
  - `ToolExecution`
  - `Approval`
  - `Autonomy`
  - `Agent` config validation

# Implementation plan
1. **Discover the current policy enforcement path**
   - Find where tool execution requests are evaluated before execution.
   - Identify:
     - the policy evaluator entry point
     - the source of agent config/policy config
     - how action type is represented (`read`, `recommend`, `execute`)
     - how approvals/thresholds are currently resolved
     - how denied decisions are surfaced and persisted

2. **Define explicit default-deny rules**
   Implement a clear rule set such that evaluation returns **Denied** when any required policy input is missing or ambiguous. At minimum cover:
   - missing agent config object
   - missing autonomy level when required
   - autonomy level outside supported range
   - missing tool permissions config
   - missing action scope for the requested tool/action
   - conflicting config entries for the same tool/action
   - malformed threshold config
   - missing approval rule when an action requires threshold/approval interpretation
   - unknown action type
   - null/empty tool name if policy depends on tool identity
   - any parse/validation failure that prevents a confident allow/approval-required decision

   Prefer a small, explicit set of denial reason codes, e.g.:
   - `MissingPolicyConfiguration`
   - `AmbiguousPolicyConfiguration`
   - `InvalidPolicyConfiguration`
   - `UnknownActionType`
   - `ToolNotConfigured`
   - `AutonomyLevelMissingOrInvalid`

3. **Make policy evaluation deterministic and fail-closed**
   Update the evaluator so that:
   - it never returns allow on partial config
   - it does not infer permissive defaults from nulls/empties
   - unsupported or unrecognized values are treated as deny
   - approval-required is returned only when approval requirements are explicitly and validly configured
   - otherwise deny

   If there is currently permissive fallback logic, remove or invert it.

4. **Normalize policy decision output**
   Ensure the policy decision result is structured and includes enough detail for audit/logging and safe UX. Prefer fields like:
   - decision/outcome: `Allowed`, `Denied`, `ApprovalRequired`
   - reason code
   - human-readable reason summary
   - evaluated tool name
   - action type
   - autonomy level used, if any
   - whether config was missing/ambiguous/invalid
   - optional structured metadata for downstream persistence

   Keep user-facing messages safe and concise; do not expose internals unnecessarily.

5. **Harden config validation at the boundary**
   If agent policy/config is loaded from JSONB or flexible config objects:
   - validate required fields before evaluation
   - convert malformed config into explicit invalid/ambiguous results
   - avoid throwing unhandled exceptions for bad config
   - prefer returning a denied policy decision over bubbling raw parsing errors into execution flow

   If there is an existing validator, extend it rather than duplicating logic.

6. **Prevent execution on denied/ambiguous policy**
   In the tool execution/orchestration path:
   - ensure denied decisions short-circuit before any tool invocation
   - ensure ambiguous/missing config is treated exactly like denied
   - ensure approval requests are only created when policy explicitly resolves to approval-required
   - do not create approval requests from ambiguous config unless the existing domain model explicitly requires that behavior; default should remain deny

7. **Persist or propagate structured decision metadata**
   If the current flow stores policy decisions in `tool_executions.policy_decision_jsonb` or equivalent:
   - include the new reason codes and fail-closed metadata
   - ensure denied decisions can be audited consistently
   - keep serialization stable and minimal

   If persistence is not yet implemented, at least propagate the structured decision through application boundaries and logs/tests.

8. **Add tests**
   Add focused tests for the evaluator and, if present, orchestration integration tests. Cover at least:
   - allow when config is explicit and valid
   - deny when policy config is null
   - deny when tool permissions are missing
   - deny when action type is unknown
   - deny when autonomy level is missing/invalid
   - deny when threshold config is malformed
   - deny when approval config is ambiguous
   - deny when conflicting rules exist
   - approval-required only when explicit valid approval policy exists
   - tool executor is not called when decision is denied

   Prefer table-driven/unit-test style where practical.

9. **Keep changes aligned with existing architecture**
   - Respect modular boundaries
   - Keep policy logic out of controllers/UI
   - Avoid direct DB access from orchestration/tool abstractions
   - Use typed contracts where available
   - Keep tenant context intact in any touched path

10. **Document intent in code**
   Add concise comments only where needed to clarify the fail-closed/default-deny rule, especially if replacing previous fallback behavior.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the relevant policy/orchestration tests first, then full suite.

4. Manually verify code paths by inspecting or testing scenarios for:
   - valid explicit allow
   - explicit approval-required
   - missing config => denied
   - ambiguous config => denied
   - invalid config => denied
   - denied decision prevents tool execution

5. Confirm no permissive fallback remains by searching for patterns like:
   - default allow
   - null-coalescing to permissive values
   - empty config treated as unrestricted
   - unknown enum/string values mapped to allow

6. If policy decisions are persisted, verify serialized payload includes the new denial reason and decision metadata.

7. Ensure any API/application response for denied execution is safe and does not leak stack traces or raw config internals.

# Risks and follow-ups
- **Risk: hidden permissive defaults**  
  Existing code may contain multiple fallback layers across domain, application, and infrastructure. Search broadly before changing behavior.

- **Risk: ambiguous semantics between deny and approval-required**  
  Be strict: only explicit, valid approval configuration should yield approval-required. Everything else fail-closed to denied.

- **Risk: malformed JSON/config parsing exceptions**  
  Convert these into structured denied decisions rather than runtime failures where possible.

- **Risk: downstream consumers expecting nullable/loose policy results**  
  Tightening contracts may require small updates in orchestration, API mapping, or persistence serialization.

- **Risk: test gaps around current behavior**  
  Add regression tests to lock in fail-closed semantics.

Follow-ups to note in code comments or PR notes if not implemented here:
- centralize policy config schema validation for agent JSONB fields
- add audit event coverage for denied-by-default decisions if not already present
- add admin-facing diagnostics to help users fix invalid/ambiguous policy config
- consider startup/runtime validation for agent configs to catch issues before execution time