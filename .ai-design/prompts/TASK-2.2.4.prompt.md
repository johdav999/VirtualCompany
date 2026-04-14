# Goal
Implement integration tests for `TASK-2.2.4` to verify role boundary enforcement and delegation behavior for `US-2.2 ST-A302`.

The tests must prove that:
- in-scope requests are processed normally
- out-of-scope requests do not receive direct answers and instead produce delegation/escalation behavior
- boundary decisions are evaluated deterministically against configured allow/deny responsibility rules
- out-of-scope handling events are persisted with the required audit fields:
  - agent id
  - requested domain
  - matched rule
  - delegation target

# Scope
Work only on the code needed to add or minimally extend integration test coverage for boundary enforcement and delegation behavior in the existing .NET solution.

Include:
- API/application/infrastructure integration tests in `tests/VirtualCompany.Api.Tests`
- any required test fixtures, builders, seed data, or helper utilities
- minimal production changes only if necessary to make the behavior testable and deterministic

Do not:
- redesign orchestration or policy architecture
- add unrelated features
- broaden into UI/mobile work
- replace existing test infrastructure unless strictly necessary

Assume the architecture is a modular monolith with ASP.NET Core, PostgreSQL-backed persistence patterns, and policy-enforced orchestration. Prefer exercising the real application pipeline with controlled test doubles only at external boundaries if needed.

# Files to touch
Start by inspecting and then update only the relevant files under these areas if needed:

- `tests/VirtualCompany.Api.Tests/**/*`
- `src/VirtualCompany.Api/**/*`
- `src/VirtualCompany.Application/**/*`
- `src/VirtualCompany.Infrastructure/**/*`
- `src/VirtualCompany.Domain/**/*`

Likely targets:
- integration test project setup and fixtures
- orchestration/policy guardrail integration entry points
- persistence mapping for boundary/audit events if already partially implemented
- test seed/configuration for agent roles and allow/deny responsibility rules

Avoid touching:
- `src/VirtualCompany.Web/**/*`
- `src/VirtualCompany.Mobile/**/*`

# Implementation plan
1. Inspect the existing implementation
   - Find the current orchestration flow for agent request handling.
   - Identify where agent role/domain responsibility rules are configured and evaluated.
   - Identify how out-of-scope decisions are currently represented:
     - response contract
     - domain event
     - audit event
     - persistence entity/table
   - Inspect existing integration test patterns in `tests/VirtualCompany.Api.Tests`.

2. Map the acceptance criteria to concrete test cases
   - Create at least one deterministic in-scope and one deterministic out-of-scope scenario per supported/seeded agent role under test.
   - At minimum, ensure there is explicit coverage for:
     - one in-scope request processed successfully without boundary violation event
     - one out-of-scope request returning delegation/escalation action instead of direct answer
     - persisted out-of-scope event containing agent id, requested domain, matched rule, delegation target

3. Build deterministic test setup
   - Seed or construct agents with clearly defined responsibility rules using allow/deny configuration.
   - Use stable domains and prompts such as:
     - finance agent + finance request => in-scope
     - finance agent + support/marketing/sales request => out-of-scope
   - If classification currently depends on nondeterministic LLM behavior, introduce a minimal test seam at the external model boundary so the integration test remains deterministic while still exercising the real application pipeline.
   - Prefer fixed fake/stub responses only for external AI/provider calls, not for internal policy evaluation.

4. Add integration tests
   - Add tests in `tests/VirtualCompany.Api.Tests` that execute the real request path.
   - Verify for in-scope:
     - normal processing result
     - no delegation/escalation action returned
     - no boundary violation/out-of-scope event persisted
   - Verify for out-of-scope:
     - response includes delegation or escalation action
     - response does not contain a direct final answer for the restricted domain
     - persisted event/audit record includes required fields
     - matched rule and delegation target are correct and deterministic

5. Validate persistence assertions
   - Query the test database/application persistence layer after execution.
   - Assert the out-of-scope handling record exists and includes:
     - correct `agent_id`
     - correct requested domain
     - correct matched allow/deny rule identifier or serialized rule value
     - correct delegation target
   - If the system uses `audit_events`, assert against that model.
   - If a dedicated boundary event table/entity exists, assert there instead or in addition.

6. Make minimal production adjustments if required
   - Only if tests cannot be written cleanly, add small improvements such as:
     - exposing structured response metadata for delegation actions
     - persisting missing audit fields required by acceptance criteria
     - stabilizing policy evaluation inputs for deterministic tests
   - Keep changes narrowly scoped and backward-compatible.

7. Keep test quality high
   - Use descriptive test names in Given/When/Then style.
   - Reuse fixture/builders to avoid duplication.
   - Ensure tests are isolated and repeatable.
   - Do not rely on test execution order.

# Validation steps
Run and report the results of:

1. Build
   - `dotnet build`

2. Targeted tests
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Full test suite if targeted tests pass
   - `dotnet test`

Validation checklist:
- integration tests pass locally
- at least one in-scope and one out-of-scope scenario per tested agent role are covered
- out-of-scope response contains delegation/escalation behavior rather than direct answer
- in-scope response does not create boundary violation events
- persisted out-of-scope event includes all required fields
- tests are deterministic and do not depend on live LLM behavior

# Risks and follow-ups
- The current codebase may not yet have a dedicated persisted boundary event model; if so, use existing `audit_events` infrastructure and add only the minimum missing fields needed for acceptance.
- If responsibility classification is currently embedded in prompt-only behavior, deterministic integration testing may require introducing a provider stub or classification seam.
- If multiple agent roles exist but only some are wired into tests today, implement coverage for the roles already supported by seed/config and note any remaining role gaps.
- If response contracts do not clearly distinguish direct answers from delegation actions, add minimal structured metadata now and recommend formalizing the contract in a follow-up task.
- Follow-up recommendation: add a compact test matrix covering all seeded agent roles and all configured deny/delegation paths once the initial deterministic integration coverage is in place.