# Goal
Implement backlog task **TASK-8.1.2** for **ST-201 Agent template catalog and hiring flow** so a tenant-scoped user can create a company-owned agent from a seeded template and customize:

- name
- avatar
- department
- role
- personality
- seniority

The implementation should fit the existing **.NET modular monolith** architecture and preserve clean boundaries across **Web / API / Application / Domain / Infrastructure**.

This task should result in a working end-to-end hiring flow where:

- seeded agent templates are available
- a user can start from a template
- template defaults are copied into a new `agents` record
- the user’s customizations override the copied defaults
- the created agent is stored under the current `company_id`
- the new agent appears with **active** status by default

Because no explicit acceptance criteria were provided for the task itself, derive behavior from **ST-201** and the architecture/backlog context.

# Scope
Include only the work necessary for this task.

## In scope
- Add or complete domain/application support for creating an agent from a template
- Ensure template defaults are copied into a company-owned agent configuration
- Support customization of:
  - display name
  - avatar URL/file reference string
  - department
  - role name
  - personality
  - seniority
- Default newly hired agents to `active`
- Enforce tenant/company scoping on reads and writes
- Provide a web UI flow in Blazor for hiring from template
- Provide API/application validation and error handling
- Seed required template catalog entries for at least:
  - finance
  - sales
  - marketing
  - support
  - operations
  - executive assistant
- Add tests for application logic and any critical UI/API behavior already covered by project conventions

## Out of scope
Do not implement the following unless required as a minimal dependency:
- full agent profile management from ST-202
- autonomy/policy guardrail enforcement from ST-203
- roster analytics/health summaries from ST-204
- avatar file upload pipeline beyond storing a URL/reference string
- mobile app changes
- chat/orchestration behavior
- audit/event fan-out unless there is already an established lightweight pattern to hook into
- advanced versioning/migration UX for templates beyond seed/migration support

# Files to touch
Adjust to the actual repository structure after inspection, but expect to touch files in these areas.

## Domain
- `src/VirtualCompany.Domain/.../Agents/Agent.cs`
- `src/VirtualCompany.Domain/.../Agents/AgentTemplate.cs`
- `src/VirtualCompany.Domain/.../Agents/ValueObjects/*` or enums if present
- Any shared domain enums/constants for:
  - agent status
  - seniority
  - department
  - personality config shape

## Application
- `src/VirtualCompany.Application/.../Agents/Commands/CreateAgentFromTemplate/*`
- `src/VirtualCompany.Application/.../Agents/Queries/GetAgentTemplates/*`
- Validators for create command
- DTOs/contracts for template list and create request/response
- Tenant-aware authorization/access checks
- Mapping code between domain and transport/view models

## Infrastructure
- `src/VirtualCompany.Infrastructure/.../Persistence/*`
- EF Core entity configurations for:
  - `agent_templates`
  - `agents`
- Migrations for any missing schema pieces
- Seed data / migration-based seed logic for required templates
- Repository implementations or query handlers
- JSONB mapping for default persona/personality and copied config fields if not already present

## API
- `src/VirtualCompany.Api/.../Controllers/*` or endpoint registrations
- Endpoints for:
  - listing available templates
  - creating agent from template

## Web
- `src/VirtualCompany.Web/.../Pages/Agents/*` or feature-equivalent components
- Template catalog page or section
- Hire/customize form
- Validation display and submit flow
- Post-create navigation, likely to roster or agent detail if available

## Tests
- `tests/...` or project-specific test folders for:
  - create-agent-from-template command handler
  - validation rules
  - tenant scoping
  - template default copy behavior
  - active-by-default behavior

# Implementation plan
1. **Inspect current solution structure before coding**
   - Identify how modules are organized across Domain/Application/Infrastructure/Web/API.
   - Find existing patterns for:
     - commands/queries
     - MediatR or equivalent dispatch
     - FluentValidation or equivalent
     - EF Core configurations/migrations
     - tenant resolution and authorization
     - Blazor page/form patterns
   - Reuse existing conventions exactly rather than inventing a new feature style.

2. **Confirm current agent/template model**
   - Check whether `Agent` and `AgentTemplate` already exist.
   - Compare current schema to architecture expectations:
     - `agent_templates`
     - `agents`
   - Verify whether fields already exist for:
     - `display_name`
     - `role_name`
     - `department`
     - `avatar_url`
     - `seniority`
     - `personality_json`
     - status/config JSON fields
   - If fields are missing, add the minimum required schema and mappings.

3. **Define the create-from-template application contract**
   - Add a command such as `CreateAgentFromTemplateCommand` with:
     - `CompanyId` if required by current app pattern, otherwise derive from tenant context
     - `TemplateId`
     - `DisplayName`
     - `AvatarUrl`
     - `Department`
     - `RoleName`
     - `Personality` or `PersonalityJson`/structured DTO
     - `Seniority`
   - Add a result DTO containing at minimum:
     - `AgentId`
     - `DisplayName`
     - `Status`
     - `TemplateId`

4. **Implement validation**
   - Validate:
     - template exists and is available
     - required fields are present
     - string lengths are reasonable
     - seniority is within allowed values if enum/constrained
     - avatar is stored as URL/reference string only
     - personality payload shape is acceptable per current project conventions
   - Ensure validation errors are field-level and UI-friendly.

5. **Implement domain/application creation logic**
   - Load the selected template.
   - Create a new company-owned agent by copying template defaults into the agent record:
     - role/objective/KPI/tool/scope/threshold/escalation defaults if those fields exist
     - default persona/personality into agent personality config
   - Apply user customizations on top of copied defaults:
     - display name
     - avatar
     - department
     - role name
     - personality
     - seniority
   - Set:
     - `company_id` to current tenant/company
     - `template_id` to selected template
     - `status` to `active`
   - Persist with timestamps.

6. **Preserve tenant isolation**
   - Ensure template listing is safe for tenant use.
     - System templates may be global/shared.
   - Ensure agent creation always writes to the current company only.
   - Prevent cross-tenant access to created agents.
   - Follow existing authorization patterns from ST-101 foundations.

7. **Add template catalog query**
   - Implement a query/endpoint to return available templates for the hiring UI.
   - Include enough metadata for selection cards/list items, such as:
     - template id
     - role name
     - department
     - summary/description if available
     - default seniority/persona preview if already modeled
   - Keep response lightweight.

8. **Seed required templates**
   - Add versioned seed data or migration-based inserts for at least:
     - finance
     - sales
     - marketing
     - support
     - operations
     - executive assistant
   - Seed defaults in config-driven form where possible.
   - Avoid hardcoding behavior outside template/config records.
   - Make seeding idempotent according to existing migration/seed strategy.

9. **Expose API endpoints**
   - Add endpoints consistent with current API style, e.g.:
     - `GET /api/agent-templates`
     - `POST /api/agents/from-template`
   - Return appropriate status codes:
     - `200` for template list
     - `201` or `200` for successful creation depending on project convention
     - `400` for validation issues
     - `404` if template not found
     - `403`/`404` for tenant access violations per existing security pattern

10. **Build the Blazor hiring flow**
    - Add a page/component where the user can:
      - browse/select a template
      - open a customization form
      - edit the required fields
      - submit creation
    - Use existing form/input components and validation patterns.
    - Keep UX simple:
      - template selection
      - customization form
      - success redirect to roster/detail page if available
    - If roster page already exists, ensure the new agent appears there after creation.

11. **Ensure roster visibility if already implemented**
    - If a roster/list page exists, verify newly created agents are shown with:
      - active status
      - customized display name/role/department
    - Do not build a full new roster feature if not needed; only wire the created agent into existing list behavior.

12. **Add tests**
    - Application tests:
      - creates agent from template
      - copies template defaults
      - applies user overrides
      - defaults status to active
      - rejects missing/invalid template
      - enforces tenant scoping
    - Infrastructure tests if project supports them:
      - JSON/config persistence mapping
      - seed data presence
    - UI/API tests only if there is an established pattern in the repo.

13. **Keep implementation minimal and aligned**
    - Do not introduce speculative abstractions.
    - Do not implement ST-202 fields editing UI beyond what is necessary to copy defaults during creation.
    - Prefer existing shared DTOs/components/helpers over new ones.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify database/migrations behavior:
   - Apply migrations using the project’s normal startup/migration path
   - Confirm `agent_templates` contains seeded entries for:
     - finance
     - sales
     - marketing
     - support
     - operations
     - executive assistant

4. Manually verify template catalog:
   - Open the web app
   - Navigate to the agent hiring/template flow
   - Confirm templates are listed

5. Manually verify create-from-template flow:
   - Select a template
   - Enter custom:
     - name
     - avatar reference
     - department
     - role
     - personality
     - seniority
   - Submit
   - Confirm success response/navigation

6. Verify persistence:
   - Inspect created `agents` row
   - Confirm:
     - `company_id` is correct
     - `template_id` is set
     - customized fields are saved
     - template defaults were copied into company-owned config fields
     - `status = active`

7. Verify tenant isolation:
   - Attempt access under another company context if test tooling exists
   - Confirm agent is not visible/creatable across tenants improperly

8. Verify roster visibility:
   - Open the roster/list page if present
   - Confirm the new agent appears with active status by default

# Risks and follow-ups
- **Schema drift risk:** The actual repository may not yet contain the full `agents`/`agent_templates` schema from the architecture doc. Add only the minimum missing pieces needed for this task.
- **Personality shape ambiguity:** If `personality` is not yet strongly modeled, use the simplest structured representation consistent with current code, and document it for future ST-202 refinement.
- **Seed strategy mismatch:** The backlog calls for versioned/migrated seed data. Follow the repo’s existing migration/seed approach exactly; do not create a parallel seeding mechanism.
- **Authorization dependency:** This task assumes tenant-aware auth foundations from ST-101 exist. If they are incomplete, use the established company context pattern and avoid broad security refactors.
- **Roster dependency:** If roster UI is not yet implemented, do not overbuild it. Ensure the created agent is queryable and ready for ST-204.
- **Avatar handling:** Keep avatar as URL/reference string only for now; file upload/storage can be a later task.
- **Future follow-up:** ST-202 should extend this foundation to full operating profile management and richer validation/history/audit behavior.