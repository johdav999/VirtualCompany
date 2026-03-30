# Goal
Implement `TASK-8.3.5` for `ST-203` so that policy guardrails are enforced **before any tool or action execution is attempted**, not only after an LLM response is produced.

The coding agent should update the orchestration flow so that:
- every tool/action request is evaluated by the policy guardrail engine pre-execution,
- denied or approval-required actions never reach the executor,
- policy outcomes are structured and persisted for auditability,
- behavior is default-deny when policy configuration is missing, ambiguous, or incomplete.

Because no explicit acceptance criteria were provided for this task, align implementation to the story and architecture notes for `ST-203` and related stories `ST-403`, `ST-503`, and `ST-602`.

# Scope
In scope:
- Identify the current orchestration/tool execution path in the .NET solution.
- Insert or refactor a **pre-execution policy evaluation step** ahead of any internal or external tool invocation.
- Ensure policy evaluation covers, at minimum:
  - tenant scope,
  - agent permission scope,
  - autonomy level,
  - action type (`read`, `recommend`, `execute`),
  - threshold rules,
  - approval requirements.
- Ensure denied actions are blocked before execution and surfaced safely.
- Ensure approval-required actions create or trigger approval flow instead of executing directly.
- Persist structured policy decision metadata into `tool_executions` and/or audit records.
- Add or update tests proving pre-execution enforcement.

Out of scope unless required by existing code structure:
- New UI screens.
- Broad redesign of approval UX.
- New external integrations.
- Full policy DSL redesign.
- Post-response moderation unrelated to tool execution.

# Files to touch
Prioritize inspection and likely edits in these areas, adjusting to actual repository structure:

- `src/VirtualCompany.Application/**`
  - orchestration services / handlers
  - tool execution abstractions
  - policy guardrail interfaces and implementations
  - approval creation commands/services
  - task/workflow command handlers if tool execution is initiated there
- `src/VirtualCompany.Domain/**`
  - policy decision models / enums / value objects
  - tool execution domain entities if they need richer decision states
- `src/VirtualCompany.Infrastructure/**`
  - persistence for `tool_executions`
  - audit event persistence
  - approval persistence wiring
  - any concrete tool executor adapters
- `src/VirtualCompany.Api/**`
  - DI registration if new services/pipeline behaviors are introduced
  - endpoints only if response contracts must expose denial/approval-required outcomes
- Tests:
  - `tests/**` if present
  - otherwise corresponding test projects in solution for Application/Domain/Infrastructure

Also inspect:
- `README.md` for architecture or conventions
- solution-wide references to:
  - `ToolExecutor`
  - `PolicyGuardrail`
  - `tool_executions`
  - `approval`
  - `orchestration`
  - `autonomy`
  - `threshold`
  - `audit`

# Implementation plan
1. **Discover the current execution path**
   - Trace how an agent request becomes a tool call.
   - Identify:
     - where tool requests are constructed,
     - where execution currently happens,
     - whether policy checks exist today,
     - whether those checks are post-response only, advisory only, or inconsistently applied.
   - Document the exact choke point where all tool executions can be intercepted.

2. **Define a single pre-execution guardrail contract**
   - Introduce or refine a central application-layer contract such as:
     - `IPolicyGuardrailEngine`
     - `EvaluateAsync(ToolExecutionRequest request, AgentContext context, CancellationToken ct)`
   - The result should be structured, not boolean-only. Include fields like:
     - decision/outcome: `Allowed`, `Denied`, `ApprovalRequired`
     - denial reasons / rule hits
     - evaluated autonomy level
     - threshold context
     - approval requirement details
     - policy snapshot or normalized metadata for audit
   - Keep this model serializable for persistence in JSON.

3. **Move enforcement ahead of execution**
   - Refactor the orchestration pipeline so the sequence is:
     1. resolve agent/task/tenant context,
     2. build normalized tool execution request,
     3. run policy evaluation,
     4. if allowed, execute tool,
     5. if denied, do not execute,
     6. if approval required, create approval and do not execute.
   - Ensure there is no alternate path that bypasses guardrails.
   - If multiple executors exist, centralize enforcement in a shared wrapper/decorator/facade rather than relying on each caller.

4. **Implement default-deny behavior**
   - If required policy inputs are missing or ambiguous, return `Denied`.
   - Examples:
     - missing agent autonomy level,
     - missing tool permission mapping,
     - unknown action type,
     - missing tenant context,
     - malformed threshold config.
   - Log structured reasons suitable for audit and debugging.

5. **Handle approval-required outcomes**
   - When policy says approval is required:
     - do not execute the tool,
     - create an approval request through the existing approval module/service if available,
     - link it to the relevant task/workflow/action context,
     - return a safe structured result to the caller indicating pending approval.
   - Reuse existing `ST-403` patterns if already implemented; otherwise create the minimum integration point without overbuilding.

6. **Persist structured execution and policy metadata**
   - Ensure attempted tool actions produce a durable record even when denied or awaiting approval, if that matches current domain patterns.
   - Update `tool_executions` persistence so records can capture:
     - request payload,
     - status (`denied`, `awaiting_approval`, `completed`, `failed`, etc.),
     - `policy_decision_json`,
     - timestamps,
     - linked task/workflow/agent/company identifiers.
   - If the current design only records successful executions, extend it to record blocked attempts as well.
   - Add audit event creation for denied and approval-routed actions.

7. **Return safe user-facing outcomes**
   - Ensure callers receive a concise operational explanation:
     - denied: action blocked due to policy/autonomy/threshold scope,
     - approval required: action queued for approval.
   - Do not expose raw chain-of-thought or internal prompt details.
   - Preserve structured machine-readable outcome for downstream workflow/task handling.

8. **Protect internal tools from direct bypass**
   - Verify internal tools call domain/application services through typed contracts, not direct DB access.
   - If there are direct execution shortcuts, route them through the guarded executor.
   - Add comments or architecture notes where useful to make the invariant explicit: **all tool execution must pass pre-execution guardrails**.

9. **Add tests**
   - Add unit tests for policy engine decisions:
     - allowed action within scope,
     - denied action outside scope,
     - approval-required action above threshold,
     - default-deny on missing config.
   - Add application/integration tests for orchestration:
     - executor is not called when denied,
     - executor is not called when approval is required,
     - approval record is created when required,
     - `tool_executions` stores policy decision metadata,
     - audit event is created for denied/approval-required outcomes.
   - If mocking is used, explicitly assert zero executor invocations on blocked paths.

10. **Keep implementation aligned with modular monolith boundaries**
   - Domain: policy concepts, enums, invariants.
   - Application: orchestration flow, policy evaluation coordination, approval routing.
   - Infrastructure: persistence and adapters.
   - API/UI: only minimal contract changes if necessary.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or run targeted tests covering:
   - allowed pre-execution path,
   - denied pre-execution path,
   - approval-required pre-execution path,
   - default-deny path.

4. Manually verify in code that:
   - no tool executor implementation can be reached without a prior policy decision,
   - denied and approval-required outcomes short-circuit before execution,
   - policy decision metadata is persisted,
   - audit/approval hooks are invoked where appropriate.

5. If there are API or orchestration contract tests, verify response semantics remain safe and deterministic.

# Risks and follow-ups
- **Risk: hidden bypass paths**
  - There may be multiple tool execution entry points. Search thoroughly before finalizing.
- **Risk: approval module not fully wired yet**
  - If approval creation is incomplete, implement the smallest viable integration and leave a clear TODO/follow-up.
- **Risk: persistence schema mismatch**
  - `tool_executions` may not currently support denied/awaiting-approval states cleanly; migration may be needed.
- **Risk: policy config ambiguity**
  - Existing agent configs may be incomplete. Favor explicit default-deny and document any migration/backfill need.
- **Risk: breaking existing happy-path orchestration**
  - Keep changes centralized and covered by tests to avoid regressions.

Follow-ups to note in code comments or task output if discovered:
- add DB migration for richer `tool_executions.status` and `policy_decision_json` if missing,
- add audit event taxonomy for policy denials and approval routing,
- add metrics/observability for guardrail decisions,
- consider a guarded executor decorator as the only DI-registered executor if the current design is fragmented.