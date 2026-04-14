# Goal

Implement `TASK-13.3.3` for `ST-703 - Condition-based triggers` by persisting condition evaluation state so the trigger engine can support **edge-triggered firing**.

The implementation must ensure that:

- condition evaluations are persisted with:
  - evaluation timestamp
  - input values
  - outcome
- the engine can determine whether a condition transitioned from `false -> true`
- triggers fire only on that transition unless explicitly configured for repeated firing
- support remains compatible with supported operators, including:
  - greater than
  - less than
  - equals
  - changed since last evaluation
- invalid condition definitions are rejected with field-level validation errors

Keep the implementation aligned with the existing modular monolith, CQRS-lite application layer, PostgreSQL persistence, and tenant-scoped data access.

# Scope

In scope:

- Add persistence model(s) for condition evaluation history and/or latest evaluation state
- Update domain/application logic for condition evaluation to:
  - load prior evaluation state
  - evaluate current condition
  - detect transition semantics
  - decide whether to fire
  - persist the new evaluation result
- Support repeated-firing configuration if already present; if not present, add the minimal configuration flag needed
- Persist enough input context to support auditability and future explainability
- Add validation for invalid condition definitions with field-level errors
- Add or update tests covering:
  - first evaluation behavior
  - false -> true transition fires
  - true -> true does not fire by default
  - repeated firing mode
  - changed-since-last-evaluation behavior
  - invalid definitions rejected
  - tenant scoping where relevant

Out of scope unless required by existing code structure:

- New UI for trigger authoring beyond DTO/validation support
- Broad redesign of trigger architecture
- Full audit/event visualization UX
- Non-condition trigger types unrelated to this task

# Files to touch

Inspect the solution first and then update the actual files that match the existing trigger implementation. Likely areas include:

- `src/VirtualCompany.Domain/...`
  - trigger entities/value objects
  - condition/operator enums
  - domain services or specifications for condition evaluation
- `src/VirtualCompany.Application/...`
  - commands/handlers for create/update trigger definitions
  - validation classes
  - trigger evaluation orchestration services
  - DTOs/contracts for persisted evaluation results
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configurations
  - repositories/query services
  - migrations
  - persistence implementations for evaluation state/history
- `src/VirtualCompany.Api/...`
  - request models if validation surfaces through API contracts
  - endpoint wiring if needed
- `tests/VirtualCompany.Api.Tests/...`
  - API/integration tests for validation and behavior
- potentially additional test projects if domain/application tests already exist in the repo

Also check for:

- existing migrations strategy and conventions
- any trigger-related modules under workflow/task/orchestration areas
- shared result/validation error patterns already used in the codebase

# Implementation plan

1. **Discover the existing trigger model**
   - Find all code related to:
     - triggers
     - workflow triggers
     - condition definitions
     - evaluation engine
     - scheduler/background workers
   - Identify:
     - where condition definitions are stored
     - how operators are represented
     - where trigger firing decisions are currently made
     - whether there is already a concept of repeated firing
     - whether there is already an audit/evaluation table

2. **Design persistence for evaluation state**
   - Add a persistence model that supports both:
     - historical evaluation records
     - efficient lookup of the latest prior evaluation
   - Prefer one of these approaches based on existing patterns:
     - a history table plus query for latest row
     - a history table plus a denormalized latest-state table
   - Minimum fields to persist:
     - `id`
     - `company_id`
     - `trigger_id` or equivalent owning entity id
     - condition identifier/index if a trigger can contain multiple conditions
     - evaluated at timestamp
     - operator
     - left/input value snapshot
     - right/threshold value snapshot
     - normalized comparable value(s) if useful
     - outcome (`true/false`)
     - previous outcome if convenient
     - fired (`true/false`)
     - firing mode / repeated flag snapshot if useful
     - metadata JSON for extensibility
   - Ensure tenant ownership via `company_id`
   - Use PostgreSQL-friendly types and existing EF conventions

3. **Add migration and EF configuration**
   - Create the new table(s) and indexes
   - Add indexes for:
     - `(company_id, trigger_id, evaluated_at desc)`
     - any lookup used to fetch latest evaluation quickly
   - Follow the repo’s migration conventions exactly
   - If the project uses archived/manual migration docs, update any required references

4. **Implement evaluation state loading**
   - Before evaluating a condition, load the most recent prior evaluation for the same:
     - tenant
     - trigger
     - condition identity
   - Make sure the lookup is deterministic and efficient
   - If no prior evaluation exists:
     - define explicit first-run semantics
     - for edge-triggering, a first evaluation that is `true` should generally count as a transition only if that matches existing product expectations; otherwise document and implement the intended behavior consistently
   - Prefer to encode this behavior clearly in code and tests rather than leaving it implicit

5. **Implement transition-based firing logic**
   - Compute:
     - current outcome
     - previous outcome
     - whether the condition transitioned `false -> true`
     - whether repeated firing is enabled
   - Recommended decision logic:
     - if current outcome is `false`: do not fire
     - if current outcome is `true` and previous outcome is `false` or missing per first-run rule: fire
     - if current outcome is `true` and previous outcome is `true`:
       - fire only when repeated firing is enabled
   - Keep this logic isolated in a domain service or clearly testable application service

6. **Support `changed since last evaluation`**
   - For the `changed since last evaluation` operator:
     - compare current input value to the prior input value snapshot
     - current outcome is `true` when the value differs from the previous value
   - Define first-run semantics explicitly:
     - usually first run should evaluate to `false` because there is no prior value to compare against
   - Persist the current input value so the next evaluation can compare against it
   - Ensure comparison handles nulls and type normalization consistently

7. **Persist evaluation result after each evaluation**
   - Persist a record regardless of whether the trigger fired
   - Include:
     - timestamp
     - input values
     - outcome
     - whether it fired
     - any prior-state references needed for debugging
   - Save atomically with any downstream trigger firing state changes if they occur in the same transaction boundary
   - If the system emits tasks/events on fire, preserve idempotency expectations

8. **Add validation for condition definitions**
   - Validate supported operators and required operands
   - Reject invalid combinations with field-level validation errors, for example:
     - missing metric/entity field name
     - unsupported operator
     - missing threshold value for comparison operators
     - invalid value type for operator
     - malformed repeated-firing configuration
   - Reuse existing validation framework/patterns in the application layer
   - Ensure API responses expose field-level errors in the project’s standard format

9. **Update any trigger definition contracts**
   - If repeated firing is not already represented, add a minimal flag such as:
     - `AllowRepeatedFiring`
     - or equivalent
   - Thread it through:
     - domain model
     - persistence
     - command DTOs
     - validators
     - evaluation engine
   - Keep backward compatibility in mind for existing records

10. **Add tests**
    - Add focused unit tests for evaluation logic:
      - `greater than`
      - `less than`
      - `equals`
      - `changed since last evaluation`
      - null/first-run behavior
      - repeated firing behavior
    - Add integration/API tests for:
      - invalid definitions return field-level validation errors
      - persisted evaluation records are created
      - edge-triggered firing only occurs on transition
      - tenant-scoped isolation of evaluation state
    - Prefer deterministic timestamps via injected clock if the codebase supports it; otherwise add minimal abstraction if needed

11. **Preserve architecture boundaries**
    - Keep domain logic out of controllers/endpoints
    - Keep persistence concerns in infrastructure
    - Use application services/handlers to orchestrate evaluation and persistence
    - Do not bypass tenant scoping or repository patterns already established in the solution

12. **Document assumptions in code comments or PR notes**
    - Especially document:
      - first-run semantics
      - `changed since last evaluation` semantics
      - repeated firing behavior
      - why evaluation records are persisted even when not fired

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow:
   - generate/apply the migration using the repo’s established process
   - verify schema includes the new evaluation persistence table(s) and indexes

4. Verify behavior with tests or targeted integration coverage for:
   - valid condition definitions accepted
   - invalid condition definitions rejected with field-level errors
   - first evaluation persisted
   - false -> true transition fires
   - true -> true does not fire by default
   - true -> true fires when repeated firing is enabled
   - changed-since-last-evaluation compares against prior input value
   - evaluation timestamp, input values, and outcome are persisted
   - tenant A evaluation state does not affect tenant B

5. If there is an API or handler for manual/scheduled evaluation, verify:
   - firing decision is based on persisted prior state, not in-memory only
   - restarting the app does not lose edge-trigger behavior

# Risks and follow-ups

- **First-run ambiguity:** acceptance criteria do not fully define whether an initial `true` should fire. Match existing product behavior if present; otherwise choose a consistent rule and cover it with tests.
- **Changed-operator typing:** comparing serialized values can be error-prone across numeric/string/null/date types. Normalize values before comparison.
- **Performance:** querying latest evaluation per trigger/condition can become hot in scheduled processing; add proper indexes and consider a latest-state table later if needed.
- **Concurrency:** simultaneous evaluations of the same trigger could produce duplicate firings. If this path is concurrent, consider transaction boundaries, row locking, or idempotency safeguards.
- **Backward compatibility:** if existing trigger definitions lack repeated-firing config, default to non-repeating behavior unless current behavior dictates otherwise.
- **Audit alignment:** this task persists evaluation results, but a later follow-up may be needed to expose them in audit/explainability views.
- **Migration naming/conventions:** follow the repo’s exact migration pattern to avoid drift with archived migration docs or existing tooling.