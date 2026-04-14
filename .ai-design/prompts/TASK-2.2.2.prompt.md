# Goal

Implement **TASK-2.2.2 — pre-response scope evaluation middleware for agent requests** in the shared orchestration pipeline so that agent responses are checked against configured responsibility boundaries **before a direct answer is returned**.

The implementation must ensure:

- If a request is **out of scope** for the addressed agent role, the system returns a **delegation or escalation action** instead of a direct answer.
- If a request is **in scope**, normal processing continues with **no boundary violation event**.
- Scope decisions are based on **configured allow/deny responsibility rules**.
- Out-of-scope events are **persisted** with:
  - `agent id`
  - `requested domain`
  - `matched rule`
  - `delegation target`

This work supports **US-2.2 ST-A302 — Role boundary enforcement and delegation behavior** and should fit the existing **modular monolith / clean architecture** structure.

# Scope

In scope:

- Add a **pre-response scope evaluation component** in the agent request/orchestration flow.
- Define a deterministic way to classify a request into a **requested domain**.
- Evaluate the requested domain against agent-configured **allow/deny responsibility rules**.
- Return a structured **delegation/escalation response** for out-of-scope requests.
- Persist an **out-of-scope handling event** in the audit/business persistence layer.
- Add deterministic tests covering:
  - at least one **in-scope** scenario
  - at least one **out-of-scope** scenario
  - for each supported/tested agent role fixture introduced in this task

Out of scope unless already trivial in the current codebase:

- Full UI work in Blazor or MAUI
- Broad redesign of the orchestration engine
- LLM-based fuzzy classification
- New external integrations
- Large schema refactors unrelated to boundary enforcement

Implementation expectations:

- Prefer **deterministic rule evaluation** over probabilistic behavior.
- Follow **default-deny** behavior when configuration is missing or ambiguous.
- Keep the feature **tenant-safe** and aligned with existing agent configuration patterns.
- Persist business events separately from technical logs where possible.

# Files to touch

Inspect the solution first and then update the most relevant files in these areas.

Likely projects:

- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

1. **Domain**
   - Agent responsibility/scope rule model
   - Boundary decision result model
   - Out-of-scope event model or audit event contract
   - Delegation/escalation action value object if needed

2. **Application**
   - Orchestration pipeline service/handler for agent requests
   - New middleware/behavior/service for pre-response scope evaluation
   - Request-domain classifier interface + implementation
   - Responsibility rule evaluator interface + implementation
   - Persistence command/service for boundary events
   - DTOs for structured response when delegation/escalation is required

3. **Infrastructure**
   - EF Core entity/configuration for persisted out-of-scope events or audit events
   - Repository implementation
   - Migration if a new table/columns are required
   - Wiring for DI

4. **API**
   - Endpoint/controller integration if the orchestration entrypoint is exposed here
   - Response contract updates if needed

5. **Tests**
   - Unit tests for deterministic rule evaluation
   - Integration/API tests for in-scope vs out-of-scope behavior
   - Persistence assertions for out-of-scope event storage

Also inspect:

- existing agent configuration models for fields like `role_brief`, `objectives_json`, `data_scopes_json`, `escalation_rules_json`
- existing orchestration services for the best insertion point
- existing audit event persistence patterns before introducing a new table

# Implementation plan

1. **Discover the current orchestration entrypoint**
   - Find where an agent request is received and transformed into a response.
   - Identify the narrowest point to insert a **pre-response boundary check** before final answer generation/return.
   - Reuse existing pipeline abstractions if present rather than adding HTTP-only middleware if the orchestration is application-layer based.

2. **Model responsibility rules**
   - Introduce or extend agent configuration to support deterministic responsibility rules, for example:
     - allowed domains
     - denied domains
     - optional delegation target per denied domain or rule
   - Keep the model simple and serializable, likely aligned with existing JSON-backed config patterns.
   - If the codebase already stores agent config in JSON, prefer extending that model rather than inventing a separate unrelated config source.

3. **Define requested-domain classification**
   - Add a deterministic classifier that maps an incoming request to a normalized domain string or enum.
   - Prefer explicit keyword/rule mapping or request metadata already available in the system.
   - Do not rely on LLM inference for acceptance-critical behavior.
   - Ensure the classifier returns enough detail for auditability:
     - requested domain
     - matched classifier rule or reason

4. **Implement boundary evaluation service**
   - Create a service such as `IAgentScopeEvaluator` that accepts:
     - agent identity/config
     - requested domain
     - responsibility rules
   - Return a structured decision object containing:
     - in-scope / out-of-scope
     - matched allow/deny rule
     - delegation target if any
     - escalation flag if no delegation target exists
   - Evaluation order should be deterministic, e.g.:
     1. explicit deny match
     2. explicit allow match
     3. fallback default deny
   - Document this precedence in code comments and tests.

5. **Integrate pre-response evaluation into orchestration**
   - Before producing a direct answer, invoke the classifier and scope evaluator.
   - If **in scope**:
     - continue normal orchestration unchanged
     - do not persist a boundary violation event
   - If **out of scope**:
     - short-circuit normal answer generation
     - return a structured delegation/escalation response instead of a direct answer
     - persist the out-of-scope handling event

6. **Persist out-of-scope handling events**
   - Prefer using the existing `audit_events` pattern if it already supports business audit records with structured metadata.
   - If needed, add a dedicated persistence structure, but avoid duplicating an existing audit mechanism.
   - Persist at minimum:
     - agent id
     - requested domain
     - matched rule
     - delegation target
   - Also include tenant/company context if available and consistent with existing persistence patterns.
   - Ensure the event is queryable and testable.

7. **Design the out-of-scope response contract**
   - The response should clearly indicate that the agent is not answering directly because the request is outside its role boundary.
   - Include a structured action payload such as:
     - `actionType: delegation` or `escalation`
     - `targetAgentRole` / `targetAgentId` / `targetQueue`
     - concise user-facing explanation
   - Keep the response safe and operational; do not expose internal reasoning.

8. **Seed or fixture role rules for tests**
   - Introduce deterministic test fixtures for at least the relevant agent roles covered by this task.
   - For each role fixture, include:
     - one in-scope request
     - one out-of-scope request
   - Example pattern:
     - finance agent: finance/budget request = in scope
     - finance agent: support ticket troubleshooting = out of scope
   - Use stable classifier inputs so tests do not become flaky.

9. **Add tests**
   - Unit tests:
     - classifier maps request text/metadata to expected domain
     - evaluator honors deny-over-allow precedence
     - missing/ambiguous config defaults to deny
   - Integration/application tests:
     - in-scope request returns normal processing path and no boundary event
     - out-of-scope request returns delegation/escalation action and persists event
   - Persistence tests:
     - stored event contains required fields exactly as acceptance criteria specify

10. **Keep implementation aligned with architecture**
   - Business logic belongs in Domain/Application, not controllers.
   - Infrastructure should only handle persistence and wiring.
   - Avoid coupling this feature to UI concerns.
   - Preserve tenant isolation and existing CQRS-lite/orchestration patterns.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run all tests:
   - `dotnet test`

3. Add/verify targeted tests for this task:
   - deterministic classifier test(s)
   - scope evaluator test(s)
   - orchestration integration test(s) for:
     - in-scope request
     - out-of-scope request
   - persistence assertion for out-of-scope event fields

4. Manually verify behavior in code/tests:
   - In-scope request:
     - normal response path executes
     - no boundary violation/out-of-scope event persisted
   - Out-of-scope request:
     - no direct answer is returned
     - delegation or escalation action is returned
     - event is persisted with:
       - agent id
       - requested domain
       - matched rule
       - delegation target

5. If a migration is added:
   - ensure migration is included in the correct infrastructure project
   - verify tests/database startup still pass

6. Confirm deterministic behavior:
   - same input + same agent config always yields same boundary decision
   - deny precedence and default-deny are covered by tests

# Risks and follow-ups

- **Risk: wrong insertion point**
  - If implemented at the API layer only, non-HTTP orchestration paths may bypass enforcement.
  - Prefer application/orchestration-layer integration.

- **Risk: overlapping config concepts**
  - The codebase may already have `data scopes`, `tool permissions`, or `escalation rules`.
  - Reuse/extend existing models instead of creating parallel policy concepts.

- **Risk: ambiguous domain classification**
  - Free-text classification can become brittle.
  - Keep v1 deterministic and narrow; document unsupported ambiguity as default-deny/escalate.

- **Risk: audit duplication**
  - There may already be an `audit_events` mechanism.
  - Prefer extending it before creating a new event store/table.

- **Risk: response contract churn**
  - If chat/task consumers expect plain text only, introducing structured delegation actions may require careful backward-compatible DTO changes.

Follow-ups after this task:

- Add richer domain taxonomy and admin-configurable responsibility rules.
- Surface boundary events in audit/explainability UI.
- Add delegation target resolution by agent roster/role rather than static config only.
- Extend coverage to more agent templates and multi-agent coordinator flows.