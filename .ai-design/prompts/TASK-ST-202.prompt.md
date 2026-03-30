# Goal
Implement backlog task **TASK-ST-202 — Agent operating profile management** for the .NET multi-tenant SaaS solution.

Deliver end-to-end support for managing an agent’s operating profile so that admins can configure and persist:
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

The implementation must ensure:
- tenant-scoped access
- server-side validation with field-level errors for invalid configurations
- persistence with updated timestamps
- subsequent orchestration reads the latest saved configuration
- archived agents are protected from future assignment paths where applicable

No explicit acceptance criteria were provided in the task wrapper, so implement against the story definition in the backlog for **ST-202**.

# Scope
In scope:
- Domain and application support for updating agent operating profile fields on existing agents
- Validation rules for profile payloads and status transitions
- API endpoints/handlers for retrieving and updating agent profile data
- Persistence updates in PostgreSQL-backed infrastructure
- Web UI for editing agent operating profile from the agent profile/detail experience
- Multi-tenant authorization and company scoping
- Updated timestamps on successful changes
- Tests covering validation, authorization/tenant scoping, persistence, and status behavior

Out of scope:
- Full audit history/versioning beyond what already exists
- Policy guardrail execution logic for runtime tool calls beyond ensuring config is stored in a shape usable by later stories
- New orchestration features beyond reading current agent config
- Mobile UI changes
- Advanced workflow/task assignment refactors unless required to block archived agents from new assignment
- Broad roster analytics/health features from ST-204 except where needed to surface/edit status

# Files to touch
Inspect the solution first and adapt to the existing architecture, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - agent aggregate/entity
  - value objects or enums for agent status/autonomy/config sections
  - domain validation helpers if present

- `src/VirtualCompany.Application/**`
  - commands/queries for agent profile retrieval and update
  - DTOs/view models for operating profile
  - validators
  - authorization/tenant access checks
  - mapping logic

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/mappings
  - repository/query implementations
  - migrations if schema changes are needed
  - JSONB serialization configuration if not already in place

- `src/VirtualCompany.Api/**`
  - controller or minimal API endpoints for get/update agent profile
  - request/response contracts
  - problem details / validation response wiring

- `src/VirtualCompany.Web/**`
  - agent profile page/components/forms
  - status controls
  - validation display
  - service client calls to API

- Tests:
  - `tests/**` or existing test projects if present
  - application tests for command validation/handling
  - API/integration tests for tenant scoping and validation
  - UI/component tests only if the repo already uses them

Also review:
- `README.md`
- existing agent-related files from ST-201
- any task assignment logic from ST-401 that should reject archived agents

# Implementation plan
1. **Inspect current implementation and align with existing patterns**
   - Find how agents are currently modeled from ST-201.
   - Identify whether the solution uses:
     - Clean Architecture with MediatR-style handlers
     - FluentValidation
     - EF Core entity configurations
     - Blazor SSR/forms patterns
   - Reuse existing conventions rather than introducing a new pattern.

2. **Confirm or extend the agent domain model**
   - Ensure the `agents` model supports these fields from the architecture/backlog:
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
   - If some fields are missing, add them in the domain/infrastructure model and create a migration.
   - Prefer strongly typed config objects in code with JSONB persistence, rather than passing raw unvalidated dictionaries everywhere.
   - Add/confirm an `AgentStatus` enum or equivalent constrained representation with:
     - `Active`
     - `Paused`
     - `Restricted`
     - `Archived`

3. **Define typed operating profile contracts**
   - Create application-layer request/response models for the editable operating profile.
   - Suggested shape:
     - `AgentOperatingProfileDto`
     - `UpdateAgentOperatingProfileCommand`
     - nested DTOs for objectives, KPIs, tool permissions, data scopes, thresholds, escalation rules, trigger logic, working hours
   - Keep contracts explicit and versionable.
   - If the current codebase already uses JSON node models for flexibility, wrap them with validation rather than exposing unbounded blobs directly.

4. **Implement validation rules**
   - Add server-side validation with field-level errors.
   - Validate at minimum:
     - required identifiers and tenant/company context
     - status is one of allowed values
     - role brief length bounds if applicable
     - objectives/KPIs collections are structurally valid
     - tool permissions contain known action types/scopes if such catalogs exist
     - data scopes are structurally valid and not empty when required
     - approval thresholds are non-negative and internally consistent
     - escalation rules are structurally valid
     - trigger logic is structurally valid
     - working hours contain valid timezone/day/time ranges and no malformed overlaps if modeled
   - If catalogs for tools/scopes do not yet exist, validate shape and obvious invariants now, and leave TODOs for stricter catalog validation later.
   - Return validation failures in the project’s standard problem-details/validation format.

5. **Implement update use case**
   - Add an application command handler to update an agent’s operating profile.
   - Enforce:
     - tenant/company ownership of the target agent
     - caller authorization (admin/owner or existing equivalent)
     - update of all editable fields
     - `updated_at` refresh on success
   - Preserve non-profile identity fields unless explicitly editable in this story.
   - Ensure subsequent reads/orchestration use the persisted values, not stale cached copies.

6. **Implement query use case**
   - Add a query/endpoint to fetch the current operating profile for an agent detail/edit screen.
   - Return normalized data suitable for the web form.
   - Ensure tenant scoping and role-based access are enforced.

7. **Handle status transition rules**
   - Support changing status to:
     - active
     - paused
     - restricted
     - archived
   - Enforce at least:
     - archived agents cannot receive new task assignments
   - If assignment logic already exists, update it to reject archived agents with a clear domain/application error.
   - If paused/restricted behavior is already partially implemented elsewhere, align with it and do not break existing flows.

8. **Persist JSONB-backed config cleanly**
   - In EF Core/infrastructure, map flexible config sections to JSONB columns.
   - Use typed owned entities/value converters/JSON serialization patterns already used in the repo.
   - Ensure null/default handling is deterministic.
   - If migrations are needed, generate and include them.

9. **Wire API endpoints**
   - Add or extend endpoints such as:
     - `GET /api/companies/{companyId}/agents/{agentId}/profile`
     - `PUT/PATCH /api/companies/{companyId}/agents/{agentId}/profile`
   - Match existing routing conventions if different.
   - Return:
     - `200` on success
     - `400` for validation errors
     - `403/404` for unauthorized or cross-tenant access per existing API policy
   - Use correlation/tenant context patterns already established in the API.

10. **Build/update Blazor web UI**
   - Add an agent operating profile edit experience in the web app.
   - Include editable sections for:
     - role brief
     - objectives
     - KPIs
     - tool permissions
     - data scopes
     - approval thresholds
     - escalation rules
     - trigger logic
     - working hours
     - status
   - Show field-level validation messages.
   - Disable or guard actions based on human role/authorization.
   - Keep the UI pragmatic: structured forms/textareas/repeaters are fine; do not over-engineer a schema builder.

11. **Ensure orchestration reads latest config**
   - Find where agent configuration is loaded for orchestration/runtime resolution.
   - Confirm it reads from persisted agent state.
   - If there is caching, invalidate or bypass stale cache after updates.
   - Do not implement new orchestration behavior; just ensure updated profile values are the source of truth.

12. **Add tests**
   - Domain/application tests:
     - valid profile update succeeds
     - invalid status rejected
     - malformed working hours/thresholds rejected
     - tenant mismatch rejected
   - Infrastructure/integration tests:
     - JSONB fields persist and round-trip correctly
     - `updated_at` changes on update
     - archived agents are rejected by assignment path
   - API tests:
     - validation returns field-level errors
     - cross-tenant access forbidden/not found per existing conventions
   - UI tests only if there is an established pattern; otherwise focus on application/API coverage.

13. **Keep implementation incremental and reviewable**
   - Prefer a small number of cohesive commits:
     1. domain + persistence
     2. application + API
     3. web UI
     4. tests
   - Avoid unrelated cleanup.

# Validation steps
Run and verify using the repo’s existing commands and any project-specific test targets you discover.

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF Core migrations are used, generate/apply as appropriate and verify schema alignment.

4. Manually verify happy path:
   - create or use an existing company and agent
   - open agent profile
   - update role brief, objectives, KPIs, permissions/scopes, thresholds, trigger logic, working hours, and status
   - save successfully
   - reload and confirm values persist
   - confirm `updated_at` changed

5. Manually verify validation:
   - submit malformed working hours
   - submit invalid status
   - submit invalid threshold values
   - confirm field-level validation errors are shown and API returns validation details

6. Verify tenant isolation:
   - attempt to fetch/update an agent from another company context
   - confirm forbidden/not found behavior matches existing tenant access policy

7. Verify archived-agent protection:
   - set an agent to `archived`
   - attempt new task assignment through any existing assignment path
   - confirm assignment is rejected with a safe, clear error

8. Verify orchestration config freshness:
   - update an agent profile
   - trigger any existing flow that resolves agent configuration
   - confirm latest persisted values are used

# Risks and follow-ups
- **Config shape ambiguity:** Some profile sections are intentionally flexible JSONB. Without a finalized schema catalog, validation may be too weak or too rigid. Favor typed minimal schemas with extensibility points.
- **Existing model mismatch:** ST-201 may already have partial agent config fields implemented differently. Reconcile carefully to avoid duplicate representations.
- **UI complexity:** Editing nested JSON-like policy structures in Blazor can become unwieldy. Keep v1 forms simple and structured; defer advanced editors.
- **Assignment rule coverage:** “Archived agents cannot receive new task assignment” may require touching task/workflow code outside this story’s immediate area.
- **Authorization assumptions:** Human role names and policies may not yet be fully implemented. Reuse existing authorization primitives and add the narrowest necessary checks.
- **Caching/staleness:** If agent definitions are cached for orchestration, updates may not be reflected immediately unless invalidation is added.
- **Auditability deferred:** The story notes config history should be auditable later. If lightweight audit hooks already exist, use them, but do not build full version history unless already scaffolded.
- **Follow-up recommendation:** After this task, align with ST-203 by formalizing policy config schemas so runtime guardrails can validate against the same typed structures used here.