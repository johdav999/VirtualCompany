# Goal
Implement backlog task **TASK-8.2.4** for **ST-202 Agent operating profile management** so that an agent’s status can be changed to **active**, **paused**, **restricted**, or **archived** across the .NET modular monolith.

This task should fit the existing architecture and domain model for the **Agent Management Module**, preserve **tenant isolation**, and support future auditability/policy enforcement. The implementation should ensure status changes are persisted correctly, validated server-side, and exposed through the appropriate application/API/UI layers already used for agent profile management.

# Scope
Include only the work required to support agent status changes as part of agent operating profile management:

- Add or complete domain support for the allowed agent statuses:
  - `active`
  - `paused`
  - `restricted`
  - `archived`
- Ensure agent status is represented consistently in:
  - domain model
  - application commands/DTOs
  - persistence mapping
  - API endpoints
  - web UI/profile editing flow if already present for ST-202
- Enforce validation so invalid statuses are rejected.
- Ensure `updated_at` / equivalent timestamp is updated when status changes.
- Preserve company scoping / tenant isolation for all reads and writes.
- Prevent illegal transitions only if the current codebase already has a transition policy pattern; otherwise keep transitions simple and allow changes among the four supported statuses.
- Ensure archived agents are clearly persisted as archived for downstream stories that will block new task assignment.

Do not expand scope into:
- full audit event history beyond minimal hooks if patterns already exist
- task assignment enforcement unless directly required by existing code touched here
- autonomy/policy engine changes
- mobile app work unless the same shared DTO/API contract requires a compile fix
- broad refactors unrelated to agent status

# Files to touch
Inspect the solution first and then update the smallest coherent set of files, likely in these areas:

- `src/VirtualCompany.Domain/**`
  - agent aggregate/entity
  - agent status enum/value object if missing
  - domain validation/behavior for status changes
- `src/VirtualCompany.Application/**`
  - commands/handlers for updating agent profile or status
  - validators
  - DTOs/view models returned to API/web
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - persistence mappings
  - migrations if schema/type constraints need adjustment
  - repository/query implementations
- `src/VirtualCompany.Api/**`
  - controller/minimal API endpoint for updating agent profile/status
  - request/response contracts if API-specific models exist
- `src/VirtualCompany.Web/**`
  - agent profile edit page/component
  - status dropdown/select/display badge mapping
  - form binding and validation messaging
- Tests in the corresponding test projects if present:
  - domain tests
  - application handler/validator tests
  - API/integration tests
  - UI/component tests if the repo already uses them

Before editing, confirm the actual project structure and naming conventions in the repository and follow existing patterns.

# Implementation plan
1. **Inspect current agent implementation**
   - Find the `Agent` entity/aggregate and determine how status is currently stored.
   - Check whether status is currently:
     - a raw string
     - enum
     - constants
     - missing/incomplete
   - Identify the existing ST-202 profile update flow and reuse it rather than creating a parallel path.

2. **Standardize the status model**
   - Introduce or complete a single canonical representation for agent status.
   - Prefer an enum or strongly typed value object in the domain, while persisting in a way consistent with the existing codebase.
   - Supported values must map exactly to:
     - `active`
     - `paused`
     - `restricted`
     - `archived`
   - If strings are persisted, ensure normalization is deterministic and case-safe.

3. **Add domain behavior**
   - Add a method on the agent aggregate/entity such as `SetStatus(...)` or equivalent.
   - Ensure it:
     - validates the incoming status
     - updates the status
     - updates the modified timestamp
   - If the domain already centralizes invariants, place validation there instead of duplicating it in controllers.

4. **Update application layer**
   - Extend the existing agent profile update command or add a focused status update command, depending on current architecture.
   - Add validation rules so unsupported values fail with field-level validation errors.
   - Ensure handlers load the agent by `company_id` + `agent_id`, not by `agent_id` alone.
   - Return the updated status in the response DTO/view model.

5. **Update persistence**
   - Ensure EF configuration maps the status field correctly.
   - If needed, add a migration to:
     - constrain column shape
     - set/normalize existing values
     - preserve compatibility with current schema
   - Do not introduce a breaking migration unless necessary.
   - If the schema already has `status text`, prefer keeping it and enforcing allowed values in code unless the repo already uses DB check constraints.

6. **Update API surface**
   - Wire the status field through the existing update endpoint or add a dedicated endpoint if that is the established pattern.
   - Ensure request validation returns safe, structured errors.
   - Ensure authorization and tenant scoping remain intact.
   - Keep API contracts aligned with existing serialization conventions.

7. **Update web UI**
   - In the agent profile management UI, expose status selection with the four allowed values.
   - Use existing form components/styles.
   - Show current status clearly.
   - Disable or hide unsupported actions rather than allowing free-text entry.
   - Ensure archived status is selectable if the story expects admins to set it manually.

8. **Add tests**
   - Domain tests:
     - valid statuses are accepted
     - invalid statuses are rejected
     - timestamp changes on status update
   - Application tests:
     - update command persists status change
     - invalid status returns validation error
     - cross-tenant update is rejected/not found
   - API/integration tests:
     - successful status update round-trip
     - invalid payload rejected
   - UI tests if applicable:
     - dropdown contains exactly the four statuses
     - selected status binds and submits correctly

9. **Check downstream compatibility**
   - Search for logic that assumes only a subset of statuses.
   - Update status display helpers, filters, badges, and query projections as needed.
   - Ensure roster/profile views can render all four statuses without errors.

10. **Keep implementation minimal and consistent**
   - Reuse existing patterns for commands, validation, EF mapping, and Blazor forms.
   - Avoid introducing a new abstraction unless the current code clearly needs one.

# Validation steps
Run the relevant checks after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they compile and apply cleanly using the repo’s existing migration workflow.

4. Manually verify the end-to-end flow in the web app/API:
   - open an agent profile
   - change status from active → paused
   - save and confirm persisted value reloads correctly
   - change paused → restricted
   - change restricted → archived
   - change archived → active if transitions are intentionally allowed
   - submit an invalid status via API/client tampering and confirm validation failure

5. Verify tenant safety:
   - confirm agent updates are scoped by company context
   - confirm an agent from another company cannot be updated

6. Verify UI rendering:
   - roster/profile pages display all four statuses correctly
   - no free-text status entry remains
   - no serialization mismatch between API and UI

# Risks and follow-ups
- **Status representation drift**: if some layers use enum names and others use lowercase strings, serialization/persistence bugs may occur. Normalize this carefully.
- **Hidden downstream assumptions**: task assignment, roster filters, or orchestration code may assume only `active` exists. Search usages of agent status and update obvious consumers.
- **Migration risk**: if existing data contains unexpected status values, a stricter mapping may fail. Add normalization or compatibility handling if needed.
- **Authorization gaps**: ensure only appropriate users can change agent status, following existing policy/role patterns.
- **Future follow-up**:
  - enforce archived-agent restrictions in task assignment and orchestration flows
  - add audit events for status changes
  - define explicit transition rules if product later requires them
  - expose status filters/badges consistently across roster, profile, and dashboard views