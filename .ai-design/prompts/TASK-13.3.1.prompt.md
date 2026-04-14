# Goal
Implement backlog task **TASK-13.3.1 — Implement condition expression model and validation rules** for story **ST-703 — Condition-based triggers**.

Deliver a production-ready first slice of the condition trigger domain model and validation behavior in the existing .NET solution so that:

- users can define condition-based triggers using supported operators against named metrics or entity fields
- the system can represent and validate threshold conditions for:
  - greater than
  - less than
  - equals
  - changed since last evaluation
- invalid condition definitions are rejected with field-level validation errors
- the model is designed to support later evaluation/runtime persistence work without requiring a redesign

This task should focus on **domain/application modeling and validation rules**, not full end-to-end trigger execution unless minimal plumbing is required to compile or support tests.

# Scope
In scope:

- Add a **condition expression model** for trigger definitions
- Add enums/value objects/constants for supported:
  - operand source types
  - operators
  - value types
  - repeat firing behavior if needed by the model
- Add **server-side validation rules** for condition definitions
- Ensure validation returns **field-level errors** suitable for API/UI consumption
- Add unit tests covering valid and invalid definitions
- If the codebase already has trigger/workflow configuration DTOs or JSON-backed config objects, integrate the new condition model into the appropriate trigger configuration contract
- Design the model so it can support persistence of evaluation results later, including:
  - evaluation timestamp
  - input values
  - outcome
  - false→true transition semantics
- If there is an existing validation abstraction/pipeline, use it consistently

Out of scope unless required for compile/test integrity:

- full evaluation engine implementation
- background worker scheduling
- database persistence of evaluation results
- UI screens
- migrations for new tables unless there is already a trigger definition persistence model that must be updated for serialization compatibility
- notification firing behavior

Acceptance criteria mapping:

- **Define condition-based triggers** → model + DTOs + serialization shape
- **Supported operators** → enum + validation
- **False→true transition unless repeated firing** → include configuration fields in model and validate them; runtime behavior can be deferred if not already present
- **Evaluation results persisted** → prepare model contracts/interfaces if needed, but do not build full persistence unless already partially scaffolded
- **Invalid definitions rejected with field-level validation errors** → required in this task

# Files to touch
Inspect the solution first and then update only the relevant files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - trigger/condition domain entities, value objects, enums
- `src/VirtualCompany.Application/**`
  - commands/DTOs/validators
  - mapping profiles or request models
  - validation result contracts
- `src/VirtualCompany.Infrastructure/**`
  - JSON serialization or EF configuration only if existing trigger config persistence requires it
- `src/VirtualCompany.Api/**`
  - request contracts or endpoint wiring only if needed to expose validation behavior
- `tests/**`
  - unit tests for validators
  - serialization tests if config is stored as JSON

Potential file patterns to look for before implementing:

- existing trigger/workflow config models
- existing validation framework usage, likely FluentValidation or custom validation
- existing result/error contract for field-level validation
- existing JSONB-backed config classes for agent/workflow trigger logic
- existing domain namespace for workflows/triggers/policies

Do not create parallel patterns if the repository already has an established approach.

# Implementation plan
1. **Inspect the current trigger/workflow configuration model**
   - Search for existing concepts such as:
     - `Trigger`
     - `TriggerLogic`
     - `WorkflowDefinition`
     - `Agent.trigger_logic_json`
     - policy/config DTOs
   - Determine whether condition triggers belong under:
     - workflow definitions
     - agent trigger logic
     - a shared trigger definition model
   - Reuse the existing module boundaries and naming conventions

2. **Introduce a condition expression model**
   Create a minimal but extensible model representing a single condition definition. Prefer explicit typed fields over opaque JSON dictionaries.

   The model should support:
   - a target reference:
     - named metric key and/or entity field path
   - an operator:
     - `GreaterThan`
     - `LessThan`
     - `Equals`
     - `ChangedSinceLastEvaluation`
   - a comparison value where applicable
   - value type metadata where needed for validation/serialization:
     - number
     - string
     - boolean
     - datetime if the existing architecture suggests it
   - optional repeated firing configuration:
     - default behavior should imply fire only on false→true transition
   - optional display/description metadata only if consistent with existing patterns

   Suggested shape, adapt to repository conventions:

   - `ConditionExpression`
   - `ConditionTargetReference`
   - `ConditionOperator`
   - `ConditionValueType`
   - `ConditionTriggerOptions` or `RepeatFiringMode`

3. **Model target references clearly**
   Users must be able to define conditions against:
   - named metrics
   - entity fields

   Represent this explicitly, for example:
   - `TargetType = Metric | EntityField`
   - `MetricName`
   - `EntityType` + `FieldPath`
   or equivalent

   Validation should ensure:
   - metric targets require metric name
   - entity field targets require entity type/field path or the repository’s equivalent field reference format
   - mutually exclusive target fields are enforced

4. **Define operator semantics in validation**
   Add validation rules so invalid definitions are rejected before runtime.

   Required rules:
   - target is required
   - operator is required and must be supported
   - comparison value is required for:
     - greater than
     - less than
     - equals
   - comparison value must be absent or ignored for:
     - changed since last evaluation
   - numeric operators (`greater than`, `less than`) only allow numeric-compatible value types
   - `equals` allows compatible primitive types according to the chosen model
   - `changed since last evaluation` requires a stable target reference and should not require a threshold value
   - repeated firing config, if present, must be internally consistent

   Also add defensive validation for:
   - blank metric names
   - blank field paths
   - unsupported value type/operator combinations
   - malformed literal values if values are stored as strings for transport
   - null/empty nested objects

5. **Return field-level validation errors**
   Integrate with the existing application validation pattern.

   Requirements:
   - validation failures must identify the specific field path, e.g.:
     - `condition.target.metricName`
     - `condition.target.fieldPath`
     - `condition.operator`
     - `condition.comparisonValue`
   - preserve consistency with existing API error contracts
   - do not invent a new error envelope if one already exists

   If the project uses FluentValidation:
   - create validators for the request/DTO and nested condition model
   - ensure nested property names flow through correctly

6. **Support future transition/evaluation persistence without overbuilding**
   Even if full runtime evaluation is out of scope, shape the model so later work can attach:
   - previous evaluation outcome
   - current evaluation outcome
   - evaluation timestamp
   - input snapshot values
   - repeated firing behavior

   If there is already a trigger runtime model, add minimal placeholders/interfaces only where useful and non-invasive.

7. **Integrate into existing trigger configuration**
   If there is an existing trigger definition/config object:
   - add a condition trigger variant or condition payload
   - ensure serialization/deserialization works
   - keep backward compatibility where possible

   If trigger logic is currently stored in JSONB:
   - ensure the new model serializes cleanly with the project’s JSON settings
   - avoid polymorphic serialization complexity unless already used in the codebase

8. **Add tests**
   Add focused tests for:
   - valid metric threshold condition
   - valid entity field equals condition
   - valid changed-since-last-evaluation condition
   - invalid missing target
   - invalid missing comparison value for threshold operators
   - invalid comparison value supplied for changed operator if disallowed
   - invalid non-numeric value type with greater/less than
   - invalid blank metric name
   - invalid blank field path
   - field-level error path assertions

   Prefer unit tests at validator level. Add serialization tests if config is persisted as JSON.

9. **Keep implementation small and idiomatic**
   - follow existing namespace/module structure
   - avoid speculative engine code
   - avoid introducing generic expression parsers
   - prefer explicit domain types and validators

# Validation steps
Run and verify the following after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Targeted verification**
   Confirm through tests or request-model validation that:
   - a metric condition with `GreaterThan` and numeric threshold is accepted
   - an entity field condition with `Equals` is accepted
   - a `ChangedSinceLastEvaluation` condition without comparison value is accepted
   - `GreaterThan` with string value is rejected
   - missing metric name for metric target is rejected with field-level error
   - missing field path for entity field target is rejected with field-level error
   - missing comparison value for `Equals`/`GreaterThan`/`LessThan` is rejected
   - unsupported operator/value combinations are rejected
   - repeated firing configuration defaults to false→true transition behavior unless explicitly configured otherwise

4. **If API endpoints are involved**
   Verify the API returns the repository’s standard validation response shape with field-specific errors.

# Risks and follow-ups
Risks:

- The repository may already have partial trigger models; avoid duplicating or conflicting abstractions.
- JSON serialization of polymorphic trigger definitions can become brittle if overengineered.
- Validation may need to align with existing API error contracts; mismatches could break clients/tests.
- If comparison values are stored as strings for transport, parsing/typing rules must be consistent and deterministic.

Follow-ups after this task:

- implement the actual condition evaluation engine
- persist evaluation results with timestamp, input values, and outcome
- implement false→true transition tracking and repeated firing runtime behavior
- add database schema/migrations for evaluation history if not already present
- expose condition trigger configuration in web/mobile UI
- add audit/event records for trigger evaluations and firings