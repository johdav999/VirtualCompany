# Goal
Implement backlog task **TASK-2.2.1 — Create role responsibility policy model with allowed domains, denied domains, and delegation targets** for **US-2.2 ST-A302 — Role boundary enforcement and delegation behavior** in the existing .NET solution.

Deliver a production-ready, test-covered implementation that introduces a deterministic **role responsibility policy model** used by the orchestration/policy layer to decide whether an agent request is:
- **in scope** and can be processed normally, or
- **out of scope** and must return a **delegation/escalation action** instead of a direct answer.

The implementation must also persist out-of-scope handling events with the required audit fields.

# Scope
Implement only what is necessary to satisfy this task and its acceptance criteria, aligned to the current modular monolith architecture and existing project boundaries.

Include:

1. **Domain model**
   - Add a role responsibility policy model for agents with:
     - allowed domains
     - denied domains
     - delegation targets
   - Model deterministic rule matching and matched-rule output.

2. **Persistence**
   - Persist policy configuration in a way consistent with the current architecture and existing agent configuration approach.
   - Persist out-of-scope handling events with:
     - agent id
     - requested domain
     - matched rule
     - delegation target

3. **Application/service logic**
   - Add a service/policy evaluator that:
     - evaluates a requested domain against allow/deny rules
     - defaults safely and deterministically
     - returns a structured decision object
   - Ensure deny rules take precedence over allow rules unless the existing codebase has an established policy precedence pattern; if so, follow that pattern and document it in code/tests.

4. **Orchestration integration**
   - Integrate the responsibility policy evaluation into the agent request handling path at the appropriate application/orchestration boundary.
   - For out-of-scope requests, produce a structured response that includes delegation or escalation action rather than a direct answer.
   - For in-scope requests, continue normal processing and do not emit boundary violation events.

5. **Audit/event persistence**
   - Persist out-of-scope handling events in the appropriate business/audit store, not only technical logs.

6. **Tests**
   - Add deterministic automated tests covering at least:
     - one in-scope scenario
     - one out-of-scope scenario
     - per agent role under test
   - Prefer unit tests for rule evaluation and focused integration/application tests for persistence and orchestration behavior.

Do **not**:
- build full UI/profile editing unless required by existing compile paths
- redesign unrelated policy systems
- add speculative multi-agent planning features beyond delegation target selection
- expose raw chain-of-thought

# Files to touch
Inspect the solution first, then update the smallest coherent set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - agent domain entities/value objects
  - policy decision models
  - audit/out-of-scope event entities

- `src/VirtualCompany.Application/**`
  - orchestration or agent request handling services
  - policy evaluation interfaces/services
  - command/query DTOs
  - response models for delegation/escalation actions

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations
  - persistence mappings for policy config and out-of-scope events

- `src/VirtualCompany.Api/**`
  - only if request/response contracts or DI wiring must be updated

- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests if request pipeline behavior is covered there

Also check whether there are more appropriate test projects for domain/application tests and use them if present.

Before coding, identify:
- where agent configuration currently lives
- how JSONB-backed config is mapped
- where orchestration/policy guardrails are currently enforced
- how audit events are persisted today

# Implementation plan
1. **Inspect the current architecture in code**
   - Find existing agent entity/configuration models.
   - Find any current policy guardrail engine, orchestration service, audit event model, and EF Core DbContext/mappings.
   - Reuse existing naming and layering conventions.

2. **Design the responsibility policy model**
   - Introduce a clear, minimal model such as:
     - `ResponsibilityPolicy`
     - `ResponsibilityRule`
     - `DelegationTarget`
     - `ResponsibilityDecision`
   - Ensure the model can express:
     - allowed domains
     - denied domains
     - delegation target for out-of-scope handling
     - matched rule identity/type for persistence
   - Keep domains deterministic strings/enums/value objects based on existing conventions in the codebase. Prefer a constrained type if one already exists.

3. **Persist policy configuration**
   - If agent config already uses JSON/JSONB fields, extend that configuration rather than creating an unrelated table unless the existing model strongly favors normalization.
   - Add any required schema changes and EF mappings.
   - Keep migration names explicit and task-related.

4. **Add out-of-scope event persistence**
   - Implement a business-level persisted record for out-of-scope handling.
   - If `audit_events` is already the right abstraction, extend/reuse it cleanly.
   - If a dedicated table/entity is more appropriate, add it with the required fields and tenant/company scoping if applicable.
   - Ensure persisted data includes:
     - agent id
     - requested domain
     - matched rule
     - delegation target

5. **Implement deterministic evaluation**
   - Create a policy evaluator service with a method similar to:
     - input: agent policy config + requested domain (+ tenant/agent context if needed)
     - output: structured decision with:
       - in-scope / out-of-scope
       - matched rule
       - delegation target
       - reason / decision type
   - Make matching deterministic and simple.
   - Recommended precedence:
     1. explicit deny match
     2. explicit allow match
     3. fallback to out-of-scope/default deny
   - If wildcard/default rules are introduced, document and test precedence clearly.

6. **Integrate into request handling**
   - Locate the agent request/chat/task orchestration entry point.
   - Add responsibility evaluation before normal answer generation/tool execution for domain-boundary checks.
   - For out-of-scope:
     - do not generate a direct domain answer
     - return a structured delegation/escalation response
     - persist the out-of-scope event
   - For in-scope:
     - continue normal flow
     - do not persist a boundary violation event

7. **Shape the user-facing response**
   - Ensure the response includes a delegation or escalation action for out-of-scope cases.
   - Keep the response concise and operational, e.g.:
     - status/action type
     - delegated target role/agent
     - brief explanation
   - Do not expose internal-only policy details unless already part of the API contract.

8. **Add tests**
   - Unit tests for evaluator:
     - allow match returns in-scope
     - deny match returns out-of-scope with delegation target
     - unmatched domain returns default out-of-scope behavior
     - precedence tests if both allow and deny could match
   - Application/integration tests:
     - in-scope request processes normally and does not persist boundary event
     - out-of-scope request returns delegation/escalation action and persists event with required fields
   - Cover at least one in-scope and one out-of-scope scenario per tested agent role.

9. **Keep code quality high**
   - Follow existing solution patterns.
   - Use async/cancellation where appropriate.
   - Keep domain logic out of controllers.
   - Add XML/docs/comments only where they clarify non-obvious policy behavior.

# Validation steps
Run and verify all relevant checks locally.

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. Validate migration state
   - Ensure any new EF Core migration compiles and is included correctly.
   - If the repo uses a specific migration generation pattern, follow it.

4. Manually verify behavior in tests or existing endpoints
   - In-scope request:
     - normal processing path
     - no boundary violation/out-of-scope event persisted
   - Out-of-scope request:
     - response includes delegation or escalation action
     - persisted record contains:
       - agent id
       - requested domain
       - matched rule
       - delegation target

5. Confirm architectural alignment
   - Tenant/company scoping preserved
   - Business audit persistence used instead of only logs
   - No direct DB access from orchestration outside established infrastructure patterns

# Risks and follow-ups
- **Unknown existing policy model**: There may already be autonomy/tool-scope guardrails. Extend rather than duplicate them.
- **Domain classification gap**: If request domain classification is not yet explicit, implement the smallest deterministic mechanism needed for this task and avoid speculative NLP classification. Prefer test-driven, explicit domain input or existing intent/domain metadata.
- **Persistence choice ambiguity**: If audit events are generic today, decide carefully whether to reuse `audit_events` or add a dedicated out-of-scope event entity. Favor the existing business audit pattern.
- **Contract ripple effects**: Response contract changes may affect API/web/mobile consumers. Keep changes additive where possible.
- **Future follow-up**:
  - admin/profile editing UI for responsibility policies
  - richer delegation resolution from role target to concrete agent
  - analytics/reporting on boundary violations
  - broader per-role seeded responsibility templates