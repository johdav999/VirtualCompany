# Goal

Implement backlog task **TASK-11.3.3** for **ST-503 Policy-enforced tool execution** so that when a tool execution is denied by policy, the system:

- returns a **safe, user-facing explanation**
- creates **business audit records**
- preserves structured denial metadata for traceability
- remains tenant-scoped and aligned with the existing modular monolith architecture

This work should fit the existing **.NET backend** architecture and support the story requirement:

> Denied executions return a safe user-facing explanation and create audit records.

Because no explicit acceptance criteria were provided for the task itself, derive behavior from **ST-503**, the architecture notes, and adjacent auditability requirements in **ST-602**.

# Scope

In scope:

- Update the policy-enforced tool execution flow so denied tool calls do **not** fail with raw/internal errors.
- Introduce or refine a **safe denial result contract** that can be surfaced to users or upstream orchestration layers.
- Persist an **audit event** for denied executions with:
  - tenant/company context
  - actor/agent context
  - tool/action attempted
  - denial outcome
  - concise rationale summary
  - structured metadata references where appropriate
- Ensure denied executions are still represented consistently in execution records if the current design expects `tool_executions` to capture attempted calls and policy decisions.
- Add/adjust tests covering denial behavior and audit creation.

Out of scope unless required by existing patterns:

- New UI screens
- Broad redesign of the policy engine
- New approval workflows
- Large schema redesigns beyond minimal additions needed for audit persistence
- Exposing raw chain-of-thought or internal policy internals to end users

Implementation constraints:

- Follow existing solution/module boundaries:
  - `VirtualCompany.Domain`
  - `VirtualCompany.Application`
  - `VirtualCompany.Infrastructure`
  - `VirtualCompany.Api`
- Keep business audit events separate from technical logging.
- Use typed contracts and existing application patterns where possible.
- Default to **safe, concise, operational explanations** for denied actions.

# Files to touch

Inspect first, then modify only what is necessary. Likely areas:

- `src/VirtualCompany.Domain/**`
  - policy decision/result models
  - audit event domain entities/value objects
  - tool execution domain contracts
- `src/VirtualCompany.Application/**`
  - orchestration/tool execution services
  - policy guardrail handling
  - command/query handlers related to tool execution
  - audit-writing abstractions/services
  - user-facing response/result DTOs
- `src/VirtualCompany.Infrastructure/**`
  - persistence for audit events and tool executions
  - EF Core configurations/mappings
  - repository implementations
  - migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - endpoint/controller response mapping if denial results surface through API contracts
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for denied execution responses
- Potentially additional test projects if present in the repo for application/infrastructure layers

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to align with repo conventions for migrations and persistence changes.

# Implementation plan

1. **Discover the current tool execution and policy flow**
   - Find the orchestration path for ST-503:
     - tool executor
     - policy guardrail engine
     - tool execution persistence
     - audit event persistence
   - Identify how denied policy decisions are currently represented:
     - exception
     - boolean result
     - status enum
     - null/empty response
   - Identify whether `tool_executions` already stores denied attempts and `policy_decision_json`.

2. **Define the desired denial behavior**
   - Standardize denied execution handling into an explicit result, not an unhandled exception.
   - The denial result should include:
     - machine-readable status/outcome, e.g. `Denied`
     - safe user-facing explanation, e.g. “I’m not allowed to perform that action under the current policy.”
     - optional internal/admin rationale summary for audit
     - structured policy decision metadata for persistence
   - Do **not** expose sensitive internal rule details, secrets, or raw policy evaluation traces in the user-facing message.

3. **Implement or refine a denial result contract**
   - Add or update a domain/application contract for tool execution outcomes.
   - Prefer a shape that can distinguish:
     - allowed + executed
     - denied
     - approval required
     - failed
   - If there is already a result type, extend it rather than introducing parallel models.
   - Ensure upstream orchestration can consume the denial result and continue gracefully.

4. **Persist denied execution metadata consistently**
   - If the current architecture records all attempted tool calls in `tool_executions`, ensure denied attempts are persisted with:
     - `company_id`
     - `task_id` / `workflow_instance_id` if available
     - `agent_id`
     - `tool_name`
     - `action_type`
     - request payload
     - status indicating denial
     - `policy_decision_json`
     - timestamps
   - If denied attempts are not currently persisted there, preserve the existing design unless story alignment clearly requires adding it.
   - Keep request/response persistence safe and avoid storing sensitive raw content unnecessarily.

5. **Create business audit records for denied executions**
   - Add audit creation at the denial decision point or immediately after denial is returned.
   - Audit event should capture at minimum:
     - actor type = agent/system as appropriate
     - actor id = agent id if available
     - action = denied tool execution attempt
     - target type/id = tool execution, task, workflow, or tool target per existing conventions
     - outcome = denied/blocked
     - rationale summary = concise operational explanation
     - data source / metadata references if supported by current schema
   - Reuse the existing audit module/service if present; do not create ad hoc logging-only behavior.

6. **Map denial to a safe user-facing explanation**
   - Ensure the API/application response returned to the caller contains a safe explanation suitable for end users.
   - Examples of acceptable tone:
     - “This action was blocked by policy.”
     - “I’m not permitted to perform that action with the current permissions.”
     - “This request requires different permissions or approval before it can proceed.”
   - Avoid:
     - stack traces
     - internal rule IDs unless already intended for admin UX
     - raw exception messages
     - detailed security-sensitive policy internals

7. **Handle orchestration integration**
   - Ensure the shared orchestration pipeline can consume denied tool results without crashing the whole interaction.
   - If the orchestration layer composes final agent responses, make sure it uses the safe explanation and still emits structured task/audit artifacts.
   - Preserve correlation IDs if the project already supports them across prompt, tool, task, and audit records.

8. **Add or update persistence configuration**
   - If schema changes are needed, keep them minimal.
   - Add migration only if required by the existing persistence model.
   - Follow repo conventions from the migration docs/archive.
   - Prefer JSONB for structured policy decision metadata if that pattern already exists.

9. **Add tests**
   - Cover at least:
     - denied execution returns safe explanation
     - denied execution creates audit record
     - denied execution does not expose internal exception/policy details
     - tenant/company context is preserved in persisted records
   - Add integration-style coverage where practical, not only unit tests.

10. **Keep implementation small and cohesive**
   - Avoid broad refactors.
   - Prefer extending existing services and contracts.
   - Document any assumptions in code comments only where necessary.

# Validation steps

1. **Build the solution**
   - Run:
     - `dotnet build`

2. **Run tests**
   - Run:
     - `dotnet test`

3. **Verify denied execution behavior in tests**
   - Confirm a policy-denied tool request returns:
     - a non-success execution outcome or denied status
     - a safe user-facing explanation
   - Confirm no raw exception text or sensitive policy internals are exposed.

4. **Verify persistence behavior**
   - Confirm denied attempts create the expected audit record.
   - If applicable, confirm `tool_executions` contains a denied/blocked record with policy metadata.

5. **Verify tenant scoping**
   - Ensure persisted audit/tool execution records include the correct `company_id`.
   - Ensure tests do not allow cross-tenant leakage.

6. **Regression check**
   - Confirm allowed tool executions still behave as before.
   - Confirm approval-required flows are not broken by denial handling changes.

# Risks and follow-ups

- **Risk: unclear existing execution contract**
  - The repo may already have a result model for tool execution. Extending it incorrectly could create duplicate pathways.
  - Mitigation: inspect current orchestration and reuse existing abstractions.

- **Risk: audit model may be incomplete or partially implemented**
  - The architecture references `audit_events`, but the current codebase may not fully expose this yet.
  - Mitigation: integrate with the existing audit persistence path if present; otherwise add the smallest viable implementation aligned to current patterns.

- **Risk: denial persistence semantics**
  - The story explicitly requires allowed executions to be persisted in `tool_executions`, but denied attempts may or may not already belong there in the current design.
  - Mitigation: preserve existing conventions; if adding denied records there, do so consistently and safely.

- **Risk: leaking sensitive policy details**
  - It is easy to accidentally return internal denial reasons directly from exceptions or policy evaluators.
  - Mitigation: centralize safe explanation mapping and test for redaction/safe wording.

- **Risk: schema drift**
  - Adding fields or tables without checking current EF/migration conventions could create friction.
  - Mitigation: inspect infrastructure and migration guidance before changing persistence.

Follow-ups to note in code comments or task notes if discovered:

- standardize denial reason codes for future UI/admin explainability
- add audit history query coverage if not already present
- align denied tool execution details with future ST-602 audit/explainability views
- consider correlation ID propagation tests if the project already supports them