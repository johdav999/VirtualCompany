# Goal
Implement backlog task **TASK-2.2.3 — Add delegation response templates and escalation action wiring for out-of-scope requests** for story **US-2.2 ST-A302 — Role boundary enforcement and delegation behavior**.

The change should ensure that when an agent receives a request outside its configured responsibility scope, the orchestration flow does **not** answer directly. Instead, it must return a structured delegation/escalation response and persist an out-of-scope handling event with the required audit fields.

# Scope
Implement the feature in the existing .NET modular-monolith architecture using current application/domain/infrastructure boundaries.

Deliverables must cover:

- Responsibility boundary evaluation against configured allow/deny rules per agent role
- Deterministic in-scope vs out-of-scope decisioning
- Delegation/escalation response template generation for out-of-scope requests
- Wiring of escalation/delegation action into orchestration response flow
- Persistence of out-of-scope handling events with:
  - `agent id`
  - `requested domain`
  - `matched rule`
  - `delegation target`
- Tests proving:
  - at least one in-scope scenario per agent role under test
  - at least one out-of-scope scenario per agent role under test
  - in-scope requests do not emit boundary violation events
  - out-of-scope requests do emit persisted handling events and delegation/escalation actions

Out of scope unless already required by existing patterns:

- New UI screens
- Mobile changes
- Broad refactors unrelated to boundary enforcement
- Full workflow-engine escalation implementation beyond the minimum wiring needed to return and persist the action

# Files to touch
Inspect the solution first and update the exact files that already own these concerns. Likely areas:

- `src/VirtualCompany.Domain/**`
  - agent policy / responsibility rule models
  - orchestration result models
  - audit or domain event models
- `src/VirtualCompany.Application/**`
  - orchestration handlers/services
  - policy guardrail engine
  - command/query handlers for agent chat/task execution
  - DTOs for structured responses
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity mappings / repositories
  - persistence for audit or out-of-scope events
  - migrations if schema changes are needed
- `src/VirtualCompany.Api/**`
  - API contracts only if response shape must expose delegation/escalation action
- `tests/**`
  - unit tests for rule evaluation
  - application/integration tests for orchestration behavior and persistence

Also inspect:
- existing migration approach in `docs/postgresql-migrations-archive/README.md`
- repository conventions from current infrastructure code
- any existing audit event, tool execution, or policy decision persistence that can be extended instead of duplicated

# Implementation plan
1. **Discover current boundary enforcement flow**
   - Find the orchestration entry point for direct agent requests/tasks.
   - Identify where agent role config, scopes, or policy rules are currently loaded.
   - Identify whether there is already:
     - a policy guardrail engine
     - audit event persistence
     - structured response/action objects
   - Reuse existing abstractions before adding new ones.

2. **Model responsibility boundary decisions**
   - Add or extend a domain/application model representing a boundary evaluation result, including:
     - `IsInScope`
     - `RequestedDomain`
     - `MatchedRule`
     - `DecisionType` (`Allow`, `Deny`, `Delegate`, `Escalate`)
     - `DelegationTarget`
     - optional user-facing explanation/template key
   - Ensure evaluation is deterministic and based on configured allow/deny responsibility rules.
   - Prefer explicit default-deny behavior when config is missing or ambiguous, consistent with architecture notes.

3. **Implement responsibility rule evaluator**
   - Add a dedicated service in application/domain layer to evaluate a request against an agent’s configured responsibility rules.
   - Inputs should minimally include:
     - agent identity/config
     - inferred or supplied requested domain
     - request context
   - Outputs should include the matched rule and action to take.
   - Keep logic pure and unit-testable.

4. **Add delegation/escalation response templates**
   - Introduce structured response templates for out-of-scope handling.
   - Response should clearly avoid answering the original request directly.
   - Include a safe user-facing message plus a structured action payload, e.g.:
     - `actionType: delegation`
     - `targetAgentRole` or `targetAgentId`
     - `reason`
     - `requestedDomain`
   - If escalation is configured instead of delegation, return:
     - `actionType: escalation`
     - escalation target / route
   - Keep wording concise, operational, and role-aware.

5. **Wire boundary evaluation into orchestration**
   - Before normal answer generation/tool execution, evaluate whether the request is in scope.
   - If in scope:
     - continue normal processing
     - do not emit boundary violation/out-of-scope events
   - If out of scope:
     - short-circuit normal answer generation
     - return delegation/escalation response template
     - persist out-of-scope handling event
   - Ensure this happens early enough that the agent does not produce a direct answer first.

6. **Persist out-of-scope handling events**
   - Reuse existing audit/event persistence if possible.
   - If no suitable table/entity exists, add a persistence model consistent with the architecture.
   - Required persisted fields:
     - company/tenant context if applicable
     - agent id
     - requested domain
     - matched rule
     - delegation target
     - timestamp
     - correlation/request/task/conversation identifiers if available
   - If using `audit_events`, map fields in a structured payload/metadata column if that is the established pattern.
   - Do not mix technical logs with business audit persistence.

7. **Expose structured action in response contracts**
   - If current orchestration/chat response DTOs do not support actions, extend them minimally to include delegation/escalation action metadata.
   - Preserve backward compatibility where possible.
   - Ensure API/application contracts clearly distinguish:
     - normal answer
     - out-of-scope delegated/escalated response

8. **Add deterministic tests**
   - Unit tests for rule evaluation:
     - allow rule match => in-scope
     - deny/delegate/escalate rule match => out-of-scope
     - ambiguous/missing config => deterministic default behavior
   - Application/integration tests:
     - in-scope request for a role returns normal processing path and no out-of-scope event
     - out-of-scope request for same role returns delegation/escalation action and persists event
     - repeat for at least one additional agent role if role-specific configs already exist
   - Assert persisted event contains:
     - agent id
     - requested domain
     - matched rule
     - delegation target

9. **Schema update if needed**
   - Only add a migration if persistence cannot be represented in existing audit/event storage.
   - Follow repository migration conventions already used in the solution.
   - Keep schema changes minimal and tenant-aware.

10. **Document assumptions in code**
   - Add concise comments only where needed to explain:
     - rule precedence
     - default-deny/default-delegate behavior
     - why out-of-scope requests short-circuit direct answering

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify deterministic boundary behavior with automated tests:
   - one in-scope scenario per tested role
   - one out-of-scope scenario per tested role

4. Verify out-of-scope orchestration result:
   - response contains delegation or escalation action
   - response does not contain a direct substantive answer to the out-of-scope request

5. Verify in-scope orchestration result:
   - request is processed normally
   - no boundary violation/out-of-scope event is persisted

6. Verify persistence:
   - out-of-scope handling event is stored with required fields:
     - agent id
     - requested domain
     - matched rule
     - delegation target

7. If API contracts changed, verify serialization/deserialization in relevant API tests.

# Risks and follow-ups
- **Requested domain inference may be weak or absent**
  - If the system does not yet classify request domain explicitly, implement the smallest deterministic mechanism possible and note follow-up work for richer intent/domain classification.

- **Existing policy models may overlap with responsibility rules**
  - Avoid duplicating guardrail concepts; extend current policy structures if they already represent role boundaries.

- **Response contract changes may ripple**
  - Keep additions backward-compatible and minimal.

- **Audit schema uncertainty**
  - Prefer extending existing audit event infrastructure over creating a parallel event store.

- **Role coverage may be limited by current seed/config data**
  - If only one or two roles are currently testable, cover those deterministically and note expansion as follow-up.

- **Follow-up suggestions**
  - add admin-configurable delegation templates
  - add UI surfacing of out-of-scope audit events
  - add analytics on frequent delegation/escalation patterns
  - add richer domain taxonomy and rule precedence documentation