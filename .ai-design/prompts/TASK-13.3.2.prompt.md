# Goal
Implement `TASK-13.3.2` for `ST-703 - Condition-based triggers` by adding a condition evaluation capability that supports comparisons against named metrics and entity fields, validates condition definitions, evaluates transition behavior correctly, and persists evaluation results for auditability and downstream trigger processing.

The implementation prompt should direct the coding agent to build this in the existing `.NET` modular monolith using clean module boundaries, tenant-aware persistence, and test coverage.

# Scope
Implement the backend/domain/application/infrastructure pieces needed so that:

- Users can define condition-based triggers using supported operators against:
  - named metrics
  - entity fields
- Supported operators include at minimum:
  - greater than
  - less than
  - equals
  - changed since last evaluation
- A condition trigger fires only when the evaluated condition transitions from `false -> true`
  - unless explicitly configured for repeated firing
- Each evaluation result is persisted with:
  - evaluation timestamp
  - input values
  - outcome
- Invalid condition definitions are rejected with field-level validation errors

Include:
- domain model/value objects/enums for condition definitions and evaluation results
- validation logic for condition definitions
- evaluator service for metric and entity-field comparisons
- persistence model and migration(s) for evaluation history/state
- application-layer command/query/service interfaces as needed
- unit tests and integration/persistence tests where appropriate

Do not build full UI flows unless required by existing APIs/contracts. If an API/controller already exists for trigger configuration, wire validation into it; otherwise keep the implementation application-service ready and testable.

# Files to touch
Inspect the solution first and adapt to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain`
  - trigger/condition domain models
  - enums/value objects for operators, operand source types, evaluation outcome
  - validation abstractions if domain-owned
- `src/VirtualCompany.Application`
  - DTOs/commands for condition definitions
  - validators
  - evaluator service interface/use case
  - field-level validation result mapping
- `src/VirtualCompany.Infrastructure`
  - EF Core entity mappings/configurations
  - repositories
  - migration(s)
  - persistence for evaluation history / last evaluation state
- `src/VirtualCompany.Api`
  - request/response contracts if existing endpoints need to expose this
  - controller wiring only if already aligned with current trigger module
- `tests/VirtualCompany.Api.Tests`
  - API/integration tests if endpoints exist
- Add test projects/files in existing test structure for:
  - domain/application unit tests
  - infrastructure persistence tests if present in repo conventions

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing trigger/workflow/task/approval modules
- existing validation/error response conventions
- existing tenant-scoping and audit patterns

# Implementation plan
1. **Discover existing trigger architecture before coding**
   - Search for existing concepts related to:
     - triggers
     - workflows
     - schedules/events
     - metrics
     - entity fields
     - validation error contracts
   - Reuse existing naming and module boundaries.
   - Identify whether condition triggers already have:
     - a definition entity
     - a scheduler/worker
     - an evaluation pipeline
     - persistence tables
   - Do not invent parallel abstractions if a trigger subsystem already exists.

2. **Define the condition model**
   Add or extend a domain model for a condition definition with explicit fields such as:
   - `SourceType` or equivalent:
     - `Metric`
     - `EntityField`
   - `SourceName` / metric name
   - `EntityType` and `FieldName` for entity-field conditions
   - `Operator`
     - `GreaterThan`
     - `LessThan`
     - `Equals`
     - `ChangedSinceLastEvaluation`
   - comparison target/value where applicable
   - optional repeated firing flag, e.g. `AllowRepeatedFiring`
   - tenant/company ownership if tenant-owned
   - optional metadata/config JSON only if consistent with existing architecture

   Keep the model strongly typed where possible. Avoid storing only opaque JSON if the rest of the codebase uses typed entities.

3. **Implement validation with field-level errors**
   Add validation rules so invalid condition definitions are rejected with field-level errors. At minimum validate:
   - source type is supported
   - metric name is required for metric-based conditions
   - entity type and field name are required for entity-field conditions
   - operator is supported
   - comparison value is required for:
     - greater than
     - less than
     - equals
   - comparison value is not required for:
     - changed since last evaluation
   - incompatible combinations are rejected, e.g.:
     - missing target value for threshold operators
     - unsupported value type for operator
     - empty names/identifiers
   - if the system has known metric/entity registries, validate against them where feasible without overcoupling

   Ensure validation output matches existing API/application conventions for field-level errors.

4. **Design evaluation inputs and outputs**
   Create a clear evaluator contract, e.g.:
   - input:
     - condition definition
     - current resolved value
     - previous resolved value and/or previous evaluation state
     - evaluation timestamp
   - output:
     - current boolean result
     - whether trigger should fire
     - resolved input values
     - operator used
     - transition info (`previousOutcome`, `currentOutcome`)
     - reason/details for diagnostics

   The evaluator should be deterministic and easy to unit test.

5. **Implement operator evaluation**
   Support these semantics:
   - `GreaterThan`: current value > target
   - `LessThan`: current value < target
   - `Equals`: current value == target
     - define equality behavior carefully for numeric/string/bool/date if applicable
     - prefer strict typed comparison over string coercion
   - `ChangedSinceLastEvaluation`:
     - true when current resolved value differs from the last evaluated value
     - if there is no previous evaluation/value, define behavior explicitly and document it in code/tests
       - recommended default: result is `false` on first evaluation because there is no prior value to compare against
   - Handle nulls explicitly and consistently.
   - If value type mismatches occur during evaluation, fail safely and persist enough detail for diagnosis if consistent with existing patterns.

6. **Implement false-to-true transition firing logic**
   Add trigger firing decision logic:
   - default behavior:
     - fire only when condition outcome transitions from `false` to `true`
   - repeated firing enabled:
     - fire on every evaluation where condition outcome is `true`
   - first evaluation behavior:
     - if no previous outcome exists and current outcome is `true`, decide based on existing trigger semantics
     - recommended default for transition-based behavior:
       - treat previous outcome as `false`, so first `true` can fire
     - but keep this aligned with any existing trigger engine conventions
   Persist enough state to support this logic on subsequent evaluations.

7. **Persist evaluation results**
   Add persistence for condition evaluation history/state. Include fields for:
   - id
   - company/tenant id
   - trigger/condition definition id
   - evaluation timestamp
   - source type
   - source identifier(s)
   - operator
   - target/comparison value if applicable
   - current resolved value
   - previous resolved value if available
   - previous outcome
   - current outcome
   - fired flag
   - repeated firing flag/config snapshot if useful
   - optional diagnostic/error details in structured form

   If the architecture already separates:
   - evaluation history
   - current state / last evaluation snapshot
   then follow that pattern.
   Otherwise, a history table plus query for latest prior evaluation is acceptable if performance is sufficient for current scope.

8. **Add EF Core mapping and migration**
   In infrastructure:
   - add entity configuration
   - ensure tenant/company scoping
   - add indexes likely needed for:
     - `company_id`
     - `condition/trigger id`
     - `evaluation timestamp desc`
   - create migration(s) following repo conventions
   - check `docs/postgresql-migrations-archive/README.md` for migration/archive expectations

9. **Integrate with existing trigger execution flow**
   If a trigger engine already exists:
   - plug the evaluator into the condition-trigger path
   - ensure it resolves current metric/entity-field values through existing services/repositories
   - do not bypass application/domain boundaries
   - preserve correlation/tenant context

   If no trigger execution flow exists yet:
   - implement the evaluator and persistence service behind an application interface
   - provide a focused command/service method that can be called by future scheduler/event workers

10. **Resolve metric and entity-field values cleanly**
    Do not hardcode direct DB access in the evaluator itself.
    Introduce abstractions such as:
    - metric value resolver
    - entity field resolver

    These should:
    - be tenant-aware
    - return typed values where possible
    - isolate source lookup from comparison logic
    - be mockable in tests

    If the codebase lacks a metric subsystem, implement only the abstraction and minimal plumbing needed for this task, not a broad metrics platform.

11. **Auditability and persistence detail**
    Since architecture emphasizes auditability:
    - persist evaluation timestamp, input values, and outcome exactly as required
    - include enough structured detail to explain why a condition fired or did not fire
    - avoid mixing business audit events with technical logs unless existing patterns require both

12. **Testing**
    Add comprehensive tests covering:
    - validation:
      - missing metric name
      - missing entity field
      - unsupported operator
      - missing comparison value
      - invalid source/operator combinations
    - evaluation:
      - greater than true/false
      - less than true/false
      - equals true/false
      - changed since last evaluation true/false
      - null handling
      - first evaluation behavior
      - type mismatch behavior
    - firing logic:
      - false -> true fires
      - true -> true does not fire by default
      - true -> true fires when repeated firing enabled
      - false -> false does not fire
      - true -> false does not fire
    - persistence:
      - evaluation record saved with timestamp/input/outcome
      - latest prior evaluation can be retrieved correctly
      - tenant scoping enforced

13. **Implementation quality constraints**
    - Follow existing solution conventions and naming.
    - Keep logic cohesive:
      - resolution logic separate from comparison logic
      - comparison logic separate from persistence
    - Prefer small, testable services over large orchestrators.
    - Avoid speculative support for operators not in acceptance criteria unless already present in the codebase.
    - Keep public contracts backward compatible unless no prior contract exists.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo, generate/apply/verify them according to existing conventions.

4. Manually verify through tests or endpoint-level execution that:
   - valid metric condition definitions are accepted
   - valid entity-field condition definitions are accepted
   - invalid definitions return field-level validation errors
   - greater than / less than / equals / changed-since-last-evaluation behave correctly
   - trigger firing occurs only on `false -> true` unless repeated firing is enabled
   - evaluation results persist timestamp, input values, and outcome
   - tenant scoping is preserved

5. Include in the final coding-agent output:
   - summary of files changed
   - migration name(s) created
   - assumptions made about first-evaluation semantics
   - any gaps due to missing existing trigger/metric infrastructure

# Risks and follow-ups
- **Unclear existing trigger model**
  - Risk: the repo may already have partial trigger abstractions with different naming.
  - Follow-up: align to existing model rather than introducing duplicate condition entities.

- **Metric source ambiguity**
  - Risk: “named metrics” may not yet have a concrete subsystem.
  - Follow-up: implement resolver abstractions and minimal integration points only; document assumptions.

- **Entity-field typing complexity**
  - Risk: entity fields may span numeric, string, bool, and date types.
  - Follow-up: support the types already present in the codebase and fail validation/evaluation safely for unsupported combinations.

- **First-evaluation semantics**
  - Risk: acceptance criteria do not fully define whether first `true` should fire.
  - Follow-up: choose a consistent default aligned with existing trigger behavior and document it in tests/comments.

- **Persistence shape**
  - Risk: storing raw input values may require JSON/typed columns tradeoffs.
  - Follow-up: prefer structured persistence compatible with current EF/PostgreSQL patterns and future audit queries.

- **Performance**
  - Risk: querying prior evaluations for every run may become expensive.
  - Follow-up: if needed later, add a denormalized “last evaluation state” table or cached snapshot once usage patterns justify it.