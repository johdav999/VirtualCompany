# Goal
Implement backlog task **TASK-10.1.2** for **ST-401 Task lifecycle and assignment** by ensuring the task domain and all relevant application/infrastructure layers support the exact task statuses:

- `new`
- `in_progress`
- `blocked`
- `awaiting_approval`
- `completed`
- `failed`

The implementation should align with the architecture and backlog, preserve tenant-aware behavior, and fit the existing modular monolith / clean architecture structure in this repository.

# Scope
In scope:

- Add or update the canonical task status representation in the domain model.
- Ensure persistence mapping supports the required statuses.
- Update task creation/update flows so these statuses are valid and consistently handled.
- Ensure any DTOs, commands, validators, API contracts, query models, and UI-facing shared models use the same status set.
- Add or update tests covering allowed statuses and any status-related lifecycle behavior already present.
- Keep implementation CQRS-lite and tenant-safe.

Out of scope unless required by existing code paths:

- Building full workflow progression logic.
- Adding new approval engine behavior beyond status support.
- Large UI redesigns.
- Introducing statuses beyond the six listed above.
- Refactoring unrelated task orchestration concerns.

If the codebase already has task lifecycle logic, extend it minimally and safely rather than redesigning it.

# Files to touch
Inspect the solution first and then modify the smallest correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - Task aggregate/entity
  - Task status enum/value object/constants
- `src/VirtualCompany.Application/**`
  - Task commands/queries
  - Validators
  - DTOs/view models
  - Mapping profiles/assemblers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - Migrations
  - Repositories
- `src/VirtualCompany.Api/**`
  - Request/response contracts if exposed directly
  - Endpoints/controllers for tasks
- `src/VirtualCompany.Shared/**`
  - Shared contracts/enums if used across Web/Mobile/API
- `src/VirtualCompany.Web/**`
  - Any task status display/filter/input models if compilation requires it
- Tests in any existing test projects under `tests/**` or `src/**` if colocated

Also review:

- `README.md`
- `VirtualCompany.sln`

Do not assume exact filenames; discover the existing task implementation first.

# Implementation plan
1. **Discover current task implementation**
   - Search for task-related types such as:
     - `Task`
     - `TaskEntity`
     - `TaskStatus`
     - `Tasks`
     - `assigned_agent_id`
     - `workflow_instance_id`
   - Identify where task status is currently defined:
     - enum
     - string constants
     - plain string property
     - database constraint/configuration
   - Trace status usage through:
     - domain
     - application commands/queries
     - API contracts
     - UI/shared models
     - tests

2. **Define a single canonical status model**
   - Prefer the project’s existing pattern:
     - enum if enums are already used for similar concepts
     - otherwise a strongly controlled string-backed approach
   - The canonical allowed values must be exactly:
     - `new`
     - `in_progress`
     - `blocked`
     - `awaiting_approval`
     - `completed`
     - `failed`
   - Avoid duplicate status definitions across layers where possible.

3. **Update the domain model**
   - Ensure the task entity/aggregate supports the required statuses.
   - If there are domain methods for lifecycle transitions, update them to use the canonical statuses.
   - Preserve existing invariants such as:
     - `completed_at` set when status becomes `completed`
     - `completed_at` cleared or left unchanged according to existing conventions for non-completed states
   - Do not invent complex transition rules unless the codebase already enforces them.

4. **Update application layer contracts and validation**
   - Update create/update commands, DTOs, and validators so the new status set is accepted where status is user/system supplied.
   - If task creation defaults status, ensure it defaults to `new`.
   - If there are filters/search queries by status, update them to recognize the six statuses.
   - Ensure invalid statuses are rejected with existing validation/error handling patterns.

5. **Update persistence**
   - Ensure EF Core configuration maps the status correctly.
   - If the database uses:
     - string columns: ensure values persist exactly as specified
     - check constraints: update them
     - enum conversions: update them
   - Add a migration if schema or constraints need to change.
   - If legacy statuses exist in seed/dev data, migrate them safely if necessary.

6. **Update API/shared/web/mobile contracts as needed**
   - If status values are serialized externally, ensure wire values match the required snake_case strings exactly.
   - Update any shared enum/string conversion logic.
   - Fix any compile issues in Web/Mobile caused by status changes.
   - Keep changes minimal and backward-safe where possible, but prioritize the backlog requirement.

7. **Add or update tests**
   - Add focused tests for:
     - task status canonical values
     - default status on creation is `new` if applicable
     - persistence/serialization round-trip of statuses
     - validation rejects unsupported statuses
     - `completed` handling updates `completed_at` if that behavior exists
   - Prefer unit tests first; add integration tests if the repository already has patterns for API or persistence tests.

8. **Check for downstream usage**
   - Search for any logic that branches on old or incomplete statuses:
     - dashboards
     - workload summaries
     - task filters
     - agent health summaries
     - workflow/approval integration
   - Update only what is necessary to keep behavior coherent and compilation green.

9. **Keep implementation clean**
   - Follow existing naming, folder structure, and architectural boundaries.
   - Do not leak infrastructure concerns into domain/application.
   - Do not add speculative features.

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`

2. Run tests before and after changes if available:
   - `dotnet test`

3. Verify task status definitions are consistent across layers:
   - Search for all task status references and confirm only the required values remain supported.

4. If migrations were added:
   - Ensure the migration compiles and reflects only necessary schema/constraint changes.

5. Validate behavior manually through tests or endpoint-level checks:
   - Creating a task results in `new` by default if status is not explicitly provided.
   - Supported statuses persist and round-trip correctly.
   - Unsupported statuses fail validation.
   - `completed` status behavior correctly handles `completed_at` if implemented in the domain.

6. Confirm no tenant-scope regressions:
   - Any touched task queries/commands must continue to respect company scoping patterns already used in the codebase.

7. Final verification:
   - `dotnet build`
   - `dotnet test`

# Risks and follow-ups
- **Risk: duplicate status definitions across layers**
  - Mitigation: centralize or reuse the existing canonical representation.

- **Risk: serialization mismatch**
  - If enums are used, default .NET serialization may emit different casing than required.
  - Mitigation: explicitly configure string values or converters so wire/database values are exactly snake_case.

- **Risk: legacy data or constraints**
  - Existing rows or DB constraints may use older status names.
  - Mitigation: inspect migrations/schema and add a safe migration if needed.

- **Risk: hidden downstream assumptions**
  - Dashboards, filters, or summaries may assume a smaller status set.
  - Mitigation: search all references and update minimal dependent logic.

- **Risk: overengineering lifecycle transitions**
  - This task is about status support, not a full state machine.
  - Mitigation: implement only the lifecycle rules already implied by the current code and story.

Follow-up items to note in comments or PR summary if discovered but not implemented:
- explicit task state transition policy/state machine
- richer blocked/failed reason fields
- approval-driven automatic transitions into/out of `awaiting_approval`
- task analytics and workload summaries by status