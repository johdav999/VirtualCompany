# Goal

Implement backlog task **TASK-8.2.1** for **ST-202 Agent operating profile management** in the existing .NET solution so that an admin can edit an agent’s operating profile, including:

- objectives
- KPIs
- role brief
- tool permissions
- data scopes
- approval thresholds
- escalation rules
- trigger logic
- working hours
- status (`active`, `paused`, `restricted`, `archived`)

The implementation must persist changes to the `agents` record, update `updated_at`, enforce tenant scoping, reject invalid configurations with field-level validation, and ensure archived agents are not eligible for new task assignment in downstream logic where applicable.

# Scope

In scope:

- Add or complete backend domain/application/API support for updating an agent operating profile.
- Model and validate the editable configuration fields using pragmatic JSON-backed DTOs/contracts aligned to the architecture.
- Add query support if needed for loading the editable profile.
- Add/update persistence mapping for `agents` fields:
  - `role_brief`
  - `objectives_json`
  - `kpis_json`
  - `tool_permissions_json`
  - `data_scopes_json`
  - `approval_thresholds_json`
  - `escalation_rules_json`
  - `trigger_logic_json`
  - `working_hours_json`
  - `status`
  - `updated_at`
- Expose tenant-scoped API endpoint(s) for reading/updating the profile.
- Add Blazor web UI for editing these fields on the agent profile page.
- Add server-side validation with field-level errors.
- Add tests for validation, tenant isolation, persistence, and status rules.

Out of scope unless already partially present and trivial to finish:

- Full audit history/versioning UI beyond normal business/audit event hooks.
- Full policy engine execution changes beyond ensuring updated config is persisted for later orchestration use.
- Mobile app support.
- Rich workflow builder UX for trigger logic/escalation authoring.
- New external integrations.

# Files to touch

Prefer existing feature/module structure if present. Touch the minimum set needed, likely across these projects:

- `src/VirtualCompany.Domain`
  - Agent aggregate/entity
  - value objects / enums for agent status
  - validation helpers if domain-centric validation exists

- `src/VirtualCompany.Application`
  - commands/handlers for update agent profile
  - queries/handlers for get agent profile
  - DTOs/contracts for editable profile sections
  - validators
  - authorization/tenant access checks

- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration / mappings
  - repository updates
  - JSONB serialization config
  - migration(s) if schema is missing any required columns or constraints

- `src/VirtualCompany.Api`
  - controller or minimal API endpoints for get/update agent profile
  - request/response contracts if API-local
  - model state / validation error mapping

- `src/VirtualCompany.Web`
  - agent profile page/component
  - edit form(s) for operating profile sections
  - status editing UI
  - validation display and save flow

- Tests
  - application tests
  - API integration tests
  - infrastructure persistence tests
  - web component tests if the repo already uses them

Also inspect these likely entry points before coding:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- `src/VirtualCompany.Web/VirtualCompany.Web.csproj`

# Implementation plan

1. **Inspect current agent management implementation**
   - Find existing agent entity, roster/profile endpoints, and ST-201/ST-204 work.
   - Reuse established patterns for:
     - tenant resolution
     - authorization
     - CQRS handlers
     - validation
     - EF mappings
     - Blazor forms
   - Confirm whether `agents` table and columns already exist in code/migrations.

2. **Define the editable operating profile contract**
   - Create or refine a single application-layer model for the editable profile, for example:
     - `AgentOperatingProfileDto`
     - `UpdateAgentOperatingProfileCommand`
   - Include:
     - `RoleBrief`
     - `Objectives`
     - `Kpis`
     - `ToolPermissions`
     - `DataScopes`
     - `ApprovalThresholds`
     - `EscalationRules`
     - `TriggerLogic`
     - `WorkingHours`
     - `Status`
   - Keep flexible sections as structured DTOs that serialize to JSONB cleanly.
   - Avoid raw untyped string blobs unless the codebase already standardizes on `JsonDocument`/`JsonNode`.

3. **Add validation rules**
   - Implement server-side validation with field-level messages.
   - At minimum validate:
     - required agent id / tenant context
     - `status` is one of allowed values
     - `role_brief` length bounds if applicable
     - objectives/KPIs collections are not malformed
     - tool permissions contain known action types or tool identifiers if such registries exist
     - data scopes are structurally valid
     - thresholds are non-negative and internally consistent
     - escalation rules are structurally valid
     - trigger logic is structurally valid
     - working hours contain valid timezone/day/time ranges and no impossible intervals
   - If exact business rules are not yet defined, implement pragmatic validation that rejects null/invalid structure and obviously inconsistent values without over-constraining future evolution.

4. **Update domain model**
   - Ensure the agent entity supports updating the operating profile atomically.
   - Add/confirm `AgentStatus` enum or equivalent constants:
     - `Active`
     - `Paused`
     - `Restricted`
     - `Archived`
   - Add a domain method such as `UpdateOperatingProfile(...)` to centralize mutation and `updated_at` handling.
   - Preserve template-derived defaults already copied at hire time.

5. **Update persistence**
   - Map all profile fields to the `agents` table.
   - Use PostgreSQL JSONB for flexible config fields.
   - Ensure serialization/deserialization is deterministic and compatible with existing conventions.
   - If columns are missing, add an EF migration.
   - Ensure `updated_at` is set on update.

6. **Implement application command/query flow**
   - Add:
     - query to fetch editable profile by `agentId` and `companyId`
     - command to update editable profile by `agentId` and `companyId`
   - Enforce tenant isolation in handler/repository query.
   - Return not found/forbidden-safe behavior consistent with the rest of the app.
   - Ensure updated configuration is what downstream orchestration would read on subsequent runs.

7. **Add API endpoints**
   - Add or extend agent management endpoints, e.g.:
     - `GET /api/companies/{companyId}/agents/{agentId}/profile`
     - `PUT /api/companies/{companyId}/agents/{agentId}/profile`
   - Reuse existing auth and company membership policies.
   - Return validation errors in the project’s standard format.
   - Do not expose internal persistence types directly.

8. **Implement Blazor profile editing UI**
   - Extend the agent detail/profile page with an operating profile editor.
   - Organize the form into clear sections:
     - Role brief
     - Objectives
     - KPIs
     - Tool permissions
     - Data scopes
     - Approval thresholds
     - Escalation rules
     - Trigger logic
     - Working hours
     - Status
   - Prefer simple, maintainable editors:
     - text area for role brief
     - repeatable list editors for objectives/KPIs
     - checkbox/list-based editors where options are known
     - structured JSON text area only as a fallback for complex sections if no better UI exists yet
   - Show field-level validation messages.
   - Disable or warn on invalid save.
   - Reflect saved values after round-trip.

9. **Protect archived agent behavior**
   - If task assignment logic already exists, add or confirm guard so archived agents cannot receive new task assignments.
   - If this rule is already implemented elsewhere, add/adjust tests only.
   - Do not broaden scope into unrelated workflow changes.

10. **Add tests**
   - Application tests:
     - valid update persists all fields
     - invalid status rejected
     - malformed working hours rejected
     - malformed thresholds/escalation/trigger config rejected
     - tenant mismatch cannot update another company’s agent
   - Infrastructure tests:
     - JSONB fields serialize/deserialize correctly
     - `updated_at` changes on update
   - API integration tests:
     - get/update endpoints work for authorized tenant member
     - validation returns field-level errors
     - cross-tenant access denied/not found
   - Domain/task tests if applicable:
     - archived agents rejected for new assignment

11. **Keep implementation aligned with architecture**
   - Modular monolith boundaries
   - CQRS-lite in application layer
   - tenant-scoped access everywhere
   - JSONB for flexible policy/config fields
   - no UI/controller prompt assembly or orchestration logic leakage

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API behavior:
   - Fetch an existing agent profile in the correct tenant.
   - Update all editable sections with valid data.
   - Confirm response reflects persisted values.
   - Submit invalid payloads and confirm field-level validation errors.
   - Attempt cross-tenant access and confirm forbidden/not found behavior per existing conventions.

4. Manually verify web UI:
   - Open agent roster/profile.
   - Edit role brief, objectives, KPIs, permissions, scopes, thresholds, escalation rules, trigger logic, working hours, and status.
   - Save and refresh.
   - Confirm values persist and validation messages render correctly.
   - Set status to archived and verify assignment UI/flow no longer allows new task assignment if that flow exists.

5. Persistence verification:
   - Confirm `agents.updated_at` changes after save.
   - Confirm JSON-backed fields are stored in expected JSONB shape.

# Risks and follow-ups

- **Risk: unclear existing JSON contract shapes**
  - Mitigation: inspect current template/default config structures and align update DTOs to those shapes rather than inventing incompatible ones.

- **Risk: overbuilding complex editors**
  - Mitigation: prefer pragmatic v1 forms and structured DTOs; use simpler text/list editors where possible.

- **Risk: validation rules may be underspecified**
  - Mitigation: implement strong structural validation and obvious business invariants now; document stricter future rules as follow-up.

- **Risk: tenant scoping regressions**
  - Mitigation: add integration tests for cross-tenant reads/updates.

- **Risk: archived-agent assignment rule may live in another story’s code path**
  - Mitigation: update only the existing assignment guard/test surface without expanding into broader workflow refactors.

Follow-ups to note in code comments or backlog if not completed here:

- add audit events for profile changes if not already present
- add config history/version diffing
- replace any fallback raw JSON editors with richer UX
- align tool permission/data scope editors with future policy engine registries
- surface “effective policy summary” on the profile page for easier admin review