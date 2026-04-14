# Goal

Implement backlog task **TASK-13.1.1 — Implement scheduled trigger data model and persistence schema** for story **ST-701 Scheduled agent triggers** in the existing .NET modular monolith.

The coding agent should add the **domain model, persistence schema, EF Core mapping, and repository/application persistence support** for **schedule-based triggers attached to agents**, with strong validation foundations for later API and worker execution work.

This task must satisfy the persistence-oriented portion of these acceptance criteria:

- Users can create, update, enable, disable, and delete schedule-based triggers for an agent through a persisted API.
- A trigger supports cron-like expressions with explicit timezone configuration and next-run time calculation.
- When a scheduled trigger is enabled, the system enqueues exactly one execution request for each due schedule window.
- Disabling a scheduled trigger prevents any new executions from being enqueued after the disable timestamp.
- Invalid cron expressions or unsupported timezones are rejected with validation errors and no trigger is persisted.

Focus on **schema and persistence readiness**, not full scheduler execution unless minimal scaffolding is required to model it correctly.

# Scope

Implement the following:

1. **Scheduled trigger domain entity/entities**
   - Model a persisted schedule trigger owned by a company and associated with an agent.
   - Include fields needed for lifecycle and scheduler correctness:
     - `id`
     - `company_id`
     - `agent_id`
     - name/code if project conventions support it
     - cron expression
     - timezone
     - enabled flag/status
     - next run timestamp
     - last evaluated/enqueued/run timestamps as appropriate
     - disable timestamp
     - created/updated timestamps
     - optional metadata/config payload if needed for future extensibility

2. **Execution deduplication / due-window persistence support**
   - Add a persistence model that allows the scheduler to guarantee **exactly one execution request per due schedule window**.
   - This can be a dedicated table such as scheduled trigger executions / schedule fire records / enqueue ledger.
   - It must support uniqueness at the schedule-window level.

3. **Validation support**
   - Add server-side validation primitives for:
     - cron expression validity
     - timezone validity
   - Validation should be usable by future command handlers/API endpoints.
   - If the codebase already has a validation pattern, follow it.

4. **Next-run calculation support**
   - Add a domain service/helper/utility abstraction for computing next run time from:
     - cron expression
     - timezone
     - reference timestamp
   - Persist `next_run_at` on the trigger.
   - Use a well-supported .NET cron library already present in the repo if available; otherwise add a minimal dependency only if necessary and consistent with project conventions.

5. **EF Core / infrastructure persistence**
   - Add entity configurations.
   - Add DbSet(s).
   - Add migration(s) for PostgreSQL.
   - Add indexes and constraints for:
     - tenant/company scoping
     - agent lookup
     - enabled/next-run scheduler scans
     - uniqueness for due-window enqueue records

6. **Repository/application support**
   - Add repository interfaces and implementations or extend existing persistence abstractions so future APIs can:
     - create
     - update
     - enable
     - disable
     - delete
     - query due triggers
     - record enqueue attempts/windows idempotently

7. **Tests**
   - Add unit/integration tests for:
     - cron validation
     - timezone validation
     - next-run calculation
     - persistence mapping
     - uniqueness/idempotency behavior for due-window records
     - disable semantics at the persistence/domain level where feasible

Out of scope unless required by existing architecture:
- Full HTTP API surface
- Full background worker implementation
- Full queue dispatch/outbox integration
- UI work

If some API command/handler scaffolding already exists and must be updated to compile, do the minimum necessary.

# Files to touch

Inspect the solution first and then update the most appropriate files in these areas.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - Add scheduled trigger aggregate/entity/value objects
  - Add validation/result types if needed
  - Add next-run calculation abstraction/interface

- `src/VirtualCompany.Application/**`
  - Add repository interfaces
  - Add command/query DTOs or validators only if needed for persistence workflows
  - Add application services/handlers only if required to keep architecture coherent

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity classes/configurations
  - DbContext updates
  - repository implementations
  - migration files
  - cron/timezone calculation service implementation

- `src/VirtualCompany.Api/**`
  - Only touch if DI registration or compile wiring is needed

- `tests/VirtualCompany.Api.Tests/**`
  - Add integration tests if this project is the established place for persistence/API tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repo uses a different migration or persistence pattern, follow the existing convention rather than inventing a new one.

# Implementation plan

1. **Discover existing patterns**
   - Inspect:
     - DbContext location
     - entity configuration style
     - migration workflow
     - repository pattern
     - validation approach
     - date/time conventions
     - tenant scoping conventions
   - Reuse existing base entity, auditable entity, strongly typed IDs, and result/error patterns if present.

2. **Design the persistence model**
   - Add a primary table for scheduled triggers, for example:
     - `agent_scheduled_triggers` or `scheduled_triggers`
   - Recommended columns:
     - `id uuid pk`
     - `company_id uuid not null`
     - `agent_id uuid not null`
     - `name text not null` or equivalent identifier
     - `cron_expression text not null`
     - `timezone text not null`
     - `status text not null` or `is_enabled boolean not null`
     - `next_run_at timestamptz null`
     - `last_evaluated_at timestamptz null`
     - `last_enqueued_at timestamptz null`
     - `disabled_at timestamptz null`
     - `created_at timestamptz not null`
     - `updated_at timestamptz not null`
     - optional `metadata_json jsonb null`
   - Add FK to `agents` and enforce `company_id` consistency according to existing conventions.

3. **Add due-window idempotency table**
   - Add a table such as `scheduled_trigger_enqueue_windows` with columns like:
     - `id uuid pk`
     - `company_id uuid not null`
     - `scheduled_trigger_id uuid not null`
     - `window_start_at timestamptz not null`
     - `window_end_at timestamptz not null`
     - `enqueued_at timestamptz not null`
     - optional `execution_request_id uuid null`
     - `created_at timestamptz not null`
   - Add a unique constraint/index on:
     - `(company_id, scheduled_trigger_id, window_start_at, window_end_at)`
   - This is the key persistence mechanism for “exactly one execution request for each due schedule window”.

4. **Model domain behavior**
   - Add methods such as:
     - create
     - update schedule
     - enable
     - disable
     - mark next run
     - soft/hard delete depending on project conventions
   - Ensure disabling sets `disabled_at` and prevents future enqueue eligibility after that timestamp.
   - If deletion style is not established, prefer the project’s existing pattern.

5. **Implement cron and timezone validation**
   - Add a validator/service abstraction, e.g.:
     - `IScheduleExpressionValidator`
     - `IScheduledTriggerCalculator`
   - Validation rules:
     - cron expression must parse successfully
     - timezone must resolve successfully using the project/platform convention
   - Be careful with Windows vs IANA timezone IDs:
     - PostgreSQL and modern cross-platform .NET often work best with IANA
     - inspect existing company timezone handling before deciding
   - If the system already stores company timezone as text, align with that convention and document assumptions in code comments/tests.

6. **Implement next-run calculation**
   - Add a service that computes the next occurrence in UTC based on:
     - cron expression
     - timezone
     - reference UTC timestamp
   - Persist `next_run_at` when:
     - trigger is created enabled
     - trigger schedule/timezone is updated
     - trigger is re-enabled
   - If disabled, `next_run_at` may be null depending on chosen semantics; keep behavior explicit and tested.

7. **Add EF Core configuration**
   - Configure:
     - table names
     - keys
     - required fields
     - max lengths where appropriate
     - JSONB mapping if used
     - indexes:
       - `(company_id, agent_id)`
       - `(company_id, is_enabled, next_run_at)` or status equivalent
       - unique due-window index
   - Ensure timestamptz is used consistently.

8. **Add migration**
   - Generate or hand-author migration according to repo conventions.
   - Ensure PostgreSQL-compatible SQL/types.
   - Include indexes and constraints.
   - Keep migration names descriptive.

9. **Add repository/query support**
   - Add methods for:
     - get by id scoped to company
     - list by agent/company
     - add/update/delete
     - list due enabled triggers before a timestamp
     - try record enqueue window idempotently
   - For idempotent enqueue recording, prefer a DB-enforced uniqueness approach.
   - If repository returns a boolean for “record inserted vs duplicate”, implement that cleanly.

10. **Register services**
    - Wire up DI for validation/calculation/repositories if needed.

11. **Add tests**
    - Unit tests:
      - valid cron accepted
      - invalid cron rejected
      - valid timezone accepted
      - invalid timezone rejected
      - next-run computed correctly for a known cron/timezone/reference
      - disabling sets disabled timestamp and affects eligibility semantics
    - Persistence/integration tests:
      - trigger persists and reloads correctly
      - due trigger query returns enabled due triggers only
      - duplicate enqueue window insert is prevented
      - disabled trigger is not returned for enqueue after disable timestamp
    - Keep tests deterministic with fixed timestamps.

12. **Document assumptions in code**
    - If timezone support is limited to IANA or mapped IDs, make that explicit.
    - If exact scheduler semantics depend on future worker implementation, ensure the persistence contract is clear.

# Validation steps

Run these after implementation:

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. If migrations are part of normal workflow, verify migration compiles and applies in test/dev environment.

4. Manually verify the schema supports the acceptance criteria:
   - A trigger can be persisted with cron + timezone + next run
   - Invalid cron/timezone cannot be persisted through validation path
   - Enabled/disabled state is persisted
   - Disable timestamp is stored
   - Due-window uniqueness prevents duplicate enqueue records

5. Include in your final implementation summary:
   - tables added/changed
   - indexes/constraints added
   - services/interfaces added
   - any dependency introduced for cron parsing
   - any assumptions about timezone identifier format

# Risks and follow-ups

- **Timezone identifier mismatch risk**
  - The product stores timezone as text in multiple places. Confirm whether the system standard is IANA, Windows, or mapped support. A wrong assumption here will cause cross-platform bugs.

- **Cron library choice**
  - Do not introduce a heavy or inconsistent dependency if an existing one is already used. If adding one, keep it minimal and note why.

- **Exactly-once semantics are persistence-backed, not globally guaranteed alone**
  - This task should provide the DB contract for idempotent enqueue per due window. Full end-to-end exactly-once behavior may still require worker/outbox coordination in a follow-up task.

- **Delete semantics**
  - If the codebase prefers soft delete, align with it. If hard delete is used, ensure FK and audit implications are considered.

- **Future follow-up likely needed**
  - Persisted API endpoints/commands for CRUD
  - Background scheduler worker using distributed locking
  - Outbox/event emission for execution requests
  - Audit events for trigger lifecycle changes
  - UI management for scheduled triggers