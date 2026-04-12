# Goal
Implement backlog task **TASK-10.2.7 — Version workflow definitions to avoid breaking in-flight instances** for story **ST-402 Workflow definitions, instances, and triggers**.

The coding agent should update the workflow subsystem so that:

- workflow definitions are explicitly versioned and immutable once used by running instances
- new workflow instances always bind to a specific definition version
- in-flight workflow instances continue using the exact version they started with
- activating a new version does not mutate or break existing instances
- workflow lookup for triggers/manual starts resolves the latest active version by workflow code within tenant scope
- persistence, application logic, and tests reflect this behavior

No explicit acceptance criteria were provided for the task, so derive implementation behavior from:
- ST-402 notes and acceptance criteria
- architecture guidance for PostgreSQL, modular monolith, CQRS-lite, background workers, and tenant isolation
- existing repository/project patterns in this solution

# Scope
In scope:

- Add or complete domain/application/infrastructure support for versioned workflow definitions
- Ensure workflow instances reference a concrete workflow definition record/version
- Prevent updates that mutate a definition version already in use; create new versions instead
- Support resolving the active version for:
  - manual workflow start
  - scheduled trigger execution
  - internal event trigger execution
- Add migration(s) if schema changes are required
- Add/adjust tests covering versioning and in-flight safety
- Preserve tenant scoping throughout

Out of scope unless required by existing code structure:

- Building a generic workflow designer UI
- Large refactors unrelated to workflow versioning
- New trigger types beyond manual/schedule/event
- Full historical audit UX
- Broker-based messaging changes

Implementation intent:

- Treat a workflow definition version as an immutable snapshot
- Prefer deactivating old versions and creating a new row/version rather than updating `definition_json` in place
- Keep existing `workflow_instances.workflow_definition_id` semantics, but ensure all start/progression paths rely on the bound definition record, not “latest by code”

# Files to touch
Inspect the solution first and then modify the actual relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - workflow definition/entity models
  - workflow instance models
  - domain enums/value objects if present
- `src/VirtualCompany.Application/**`
  - commands/handlers for create/update/publish workflow definitions
  - commands/handlers for starting workflow instances
  - trigger resolution services
  - DTOs/view models for workflow definitions and instances
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/mappings
  - repositories/query services
  - migrations
  - background worker trigger/scheduler persistence access
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if workflow definition create/update/start APIs exist
- `src/VirtualCompany.Web/**`
  - only if workflow definition management/start flows require contract updates already surfaced in UI
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially other test projects if present under `tests/**`

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repository uses MediatR, FluentValidation, EF Core, minimal APIs, or feature folders, follow existing conventions exactly.

# Implementation plan
1. **Discover current workflow implementation**
   - Find all workflow-related types, endpoints, handlers, repositories, and migrations.
   - Identify:
     - how `workflow_definitions` is currently modeled
     - whether `version`, `code`, and `active` already exist in code and DB
     - how workflow instances are started
     - whether any update path mutates `definition_json` in place
     - how scheduled/event triggers resolve definitions
   - Summarize findings in your working notes before changing code.

2. **Define target behavior**
   Implement these rules unless the codebase already has equivalent semantics:
   - A workflow definition is identified logically by `(company_id, code)` and physically by row `id`.
   - Each row represents one immutable version snapshot.
   - `version` is monotonically increasing per `(company_id, code)`.
   - Only one active version per `(company_id, code)` should be used for new starts, unless current code intentionally supports multiple actives; if so, tighten behavior carefully.
   - `workflow_instances.workflow_definition_id` must always point to the exact version row used at start time.
   - Workflow progression must load the definition by the instance’s bound `workflow_definition_id`, never by latest active code.

3. **Update domain model**
   - Add or refine invariants on workflow definitions:
     - version required and > 0
     - code required and stable
     - definition payload required
     - active flag semantics clear
   - If there is an update method that changes `definition_json` on an existing definition, replace or restrict it.
   - Prefer explicit operations such as:
     - create initial definition version
     - create new version from existing code
     - activate/deactivate version
   - Ensure workflow instance creation captures the bound definition id/version metadata if useful.

4. **Persistence/schema changes**
   If needed, add EF migration(s) to enforce versioning behavior. Consider:
   - unique constraint/index on `(company_id, code, version)` for tenant-owned definitions
   - if system templates with `company_id null` are supported, ensure uniqueness semantics still work correctly
   - optional filtered unique index for one active version per `(company_id, code)` if compatible with current design
   - indexes to support resolving latest active version by `(company_id, code, active)`
   - non-null constraints where safe
   - preserve existing data with a safe migration path

   Be careful with PostgreSQL null semantics if `company_id` can be null for system templates.

5. **Application-layer create/update/version flows**
   - For “edit workflow definition” behavior, do not overwrite an existing in-use version snapshot.
   - Implement versioned update semantics:
     - if editing a draft/unused version is already supported and safe, keep it only if no instances depend on it
     - otherwise create a new version row with incremented version and desired payload
   - Ensure activation of a new version does not alter existing instances.
   - If APIs currently expose “update definition”, preserve contract if possible but change implementation to create a new version under the hood where appropriate.

6. **Workflow start resolution**
   - Manual start:
     - resolve the active definition by `(company_id, code)` or explicit definition id/version if API supports it
     - bind the instance to that exact definition row
   - Scheduled trigger:
     - scheduler/background worker must resolve the active version at trigger time for new instances
   - Event trigger:
     - same behavior as schedule/manual
   - If explicit version start is supported, validate tenant scope and existence.

7. **Workflow progression safety**
   - Audit all workflow runner/progression code paths.
   - Ensure step execution loads the definition snapshot from the instance’s `workflow_definition_id`.
   - Remove any logic that re-resolves by code/latest version during execution.
   - This is the core in-flight safety requirement.

8. **Query/read model updates**
   - Ensure workflow definition queries can return:
     - current active version
     - version history if applicable
   - Ensure workflow instance reads can expose the bound definition version for diagnostics if useful.
   - Keep API/UI changes minimal unless necessary.

9. **Validation and error handling**
   Add or refine validation for:
   - duplicate version creation
   - starting a workflow when no active version exists
   - attempts to mutate a definition version that is already in use
   - tenant-scoped access failures
   - ambiguous active version state if legacy data exists

   Prefer safe, explicit application errors over silent fallback behavior.

10. **Tests**
   Add tests at the appropriate layer(s) following existing patterns. Cover at minimum:
   - creating initial workflow definition version
   - creating a second version for same code increments version
   - starting a workflow instance binds to the active version at start time
   - after a new version is activated, existing in-flight instance still uses old version
   - new instances use the newly active version
   - progression logic uses bound definition id, not latest version
   - tenant isolation for definition resolution
   - duplicate/invalid version scenarios rejected
   - if applicable, only one active version per code is allowed

   Prefer integration tests where persistence behavior matters.

11. **Keep changes idiomatic**
   - Follow existing naming, folder structure, and architectural boundaries.
   - Do not introduce speculative abstractions.
   - Keep migrations and code changes focused on this task.

12. **Document assumptions**
   In the final implementation notes/PR summary, include:
   - what existing behavior was found
   - what was changed
   - any data migration assumptions
   - any follow-up work needed for UI/history management if not fully exposed yet

# Validation steps
Run the relevant commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used, ensure:
   - migration compiles cleanly
   - startup can apply/recognize the migration
   - schema changes are consistent with PostgreSQL

4. Manually verify behavior through tests or targeted execution paths:
   - create workflow definition `code = X`, version 1 active
   - start instance A for `X`
   - create/activate version 2 for `X`
   - confirm instance A still resolves and progresses against version 1
   - start instance B for `X`
   - confirm instance B binds to version 2

5. Verify no tenant leakage:
   - same workflow code in different companies resolves only within company scope

6. Verify no accidental mutable update path remains:
   - search for any direct update of workflow definition payload/version on existing rows and fix/remove if unsafe

# Risks and follow-ups
Risks:
- Existing code may already assume workflow definitions are mutable; changing semantics could affect APIs/UI expectations.
- Legacy data may contain multiple active versions for the same code.
- Scheduler/event trigger code may resolve by code in multiple places; missing one path would leave in-flight breakage.
- PostgreSQL uniqueness with nullable `company_id` for system templates may require careful index design.
- If UI/API contracts expose “update definition”, behavior may need to remain backward-compatible while changing implementation semantics.

Follow-ups to note if not fully implemented now:
- Add explicit workflow definition history/version listing UI
- Add audit events for version creation/activation/deactivation
- Add optimistic concurrency/version stamps if workflow definition management becomes collaborative
- Add clearer draft/published lifecycle if product later distinguishes editable drafts from immutable published versions
- Add data repair migration or admin tooling if legacy duplicate-active definitions are discovered