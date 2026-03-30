# Goal
Implement **TASK-ST-201 — Agent template catalog and hiring flow** for the existing .NET solution so founders can hire named agents from seeded templates and see them appear in their company roster with copied, company-owned configuration.

This task should deliver the first usable slice of the **Agent Management Module** for:
- seeded agent templates
- template catalog query/listing
- hire-agent flow
- persistence of company-owned agent records copied from template defaults
- default active status on creation
- basic web UX to browse templates and hire an agent

Because no explicit acceptance criteria were provided on the task itself, implement against the backlog story **ST-201** acceptance criteria and architecture notes.

# Scope
Implement the following end-to-end behavior:

1. **Seed agent templates**
   - Provide versioned seed data/migration for at least these template roles:
     - finance
     - sales
     - marketing
     - support
     - operations
     - executive assistant
   - Templates should be data-driven and configurable, not hardcoded in orchestration logic.

2. **Template catalog**
   - Add application/API/query support to list available agent templates.
   - Include enough metadata for UI display:
     - template id
     - role name
     - department
     - default persona summary or equivalent display fields
     - default seniority if modeled
     - avatar/default avatar reference if available

3. **Hire flow**
   - User can create an agent from a selected template.
   - User can customize:
     - name
     - avatar
     - department
     - role
     - personality
     - seniority
   - On creation, copy template defaults into the new `agents` record:
     - objectives
     - KPIs
     - tools
     - scopes
     - thresholds
     - escalation rules
     - persona/personality defaults, merged or overridden by user input as appropriate

4. **Roster visibility**
   - Newly hired agents must appear in the company roster.
   - New agents default to `active` status.

5. **Tenant safety**
   - All reads/writes must be company-scoped.
   - Hiring creates company-owned agent records only for the current tenant context.

6. **Tests**
   - Add focused tests for:
     - template listing
     - hiring from template
     - copied defaults
     - active-by-default status
     - tenant scoping protections

Out of scope unless required by existing architecture patterns:
- full agent profile editing beyond fields needed for hire flow
- autonomy/policy enforcement logic from ST-202/ST-203
- mobile implementation
- advanced avatar upload pipeline; URL/file reference is sufficient
- deep analytics/health summaries

# Files to touch
Inspect the solution first and adapt to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - agent template entity/value objects/enums if missing
  - agent entity updates if needed
- `src/VirtualCompany.Application/**`
  - commands/queries for template catalog and hire flow
  - DTOs/view models
  - validators
  - handlers/services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - migrations
  - seed data / startup seeding
  - repositories/query implementations
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for template catalog and hiring
- `src/VirtualCompany.Web/**`
  - Blazor pages/components for:
    - template catalog
    - hire form/modal/page
    - roster listing integration
- `README.md`
  - only if setup/run notes must be updated
- test projects under `tests/**` or existing test locations
  - application tests
  - integration tests if present

Also inspect these likely entry points:
- `src/VirtualCompany.Api/Program.cs`
- DbContext and migration assembly in Infrastructure
- existing tenant context abstractions
- existing auth/membership policies from ST-101 if already implemented
- existing company/agent navigation in Web

# Implementation plan
1. **Discover current implementation and align with existing patterns**
   - Inspect the solution structure and existing conventions for:
     - CQRS/MediatR usage
     - Result/error handling
     - EF Core mappings
     - tenant resolution
     - authorization
     - Blazor routing and page composition
   - Reuse existing patterns instead of introducing a parallel style.

2. **Model agent templates if not already present**
   - Confirm whether `agent_templates` and `agents` already exist in the domain/infrastructure.
   - If missing, add domain models aligned to the architecture:
     - `AgentTemplate`
     - `Agent`
   - Ensure `Agent` supports:
     - `CompanyId`
     - `TemplateId`
     - `DisplayName`
     - `RoleName`
     - `Department`
     - `AvatarUrl`
     - `Seniority`
     - `Status`
     - `PersonalityJson`
     - `ObjectivesJson`
     - `KpisJson`
     - `ToolPermissionsJson`
     - `DataScopesJson`
     - `ApprovalThresholdsJson`
     - `EscalationRulesJson`
     - timestamps
   - Use JSON/JSONB-backed properties consistent with current persistence strategy.

3. **Add or complete EF Core configuration**
   - Configure `agent_templates` and `agents` tables per architecture.
   - Add indexes that make sense for v1:
     - templates by role/department if useful
     - agents by `company_id`, `status`, `department`
   - Ensure tenant-owned tables include `company_id`.
   - Keep template table global/system-owned unless current architecture uses another pattern.

4. **Create versioned seed data for templates**
   - Seed at least six templates:
     - Finance Manager/Analyst
     - Sales Rep/Manager
     - Marketing Manager
     - Support Lead/Agent
     - Operations Manager
     - Executive Assistant
   - Each template should include realistic defaults in config JSON:
     - persona/personality
     - objectives
     - KPIs
     - tools
     - scopes
     - thresholds
     - escalation rules
   - Prefer migration-based or startup-seeding approach already used by the repo.
   - Make seeding idempotent and version-aware where possible.
   - Do not hardcode behavior elsewhere that duplicates template config.

5. **Implement template catalog query**
   - Add application query and handler to list templates.
   - Return a UI-friendly DTO with only needed fields.
   - If there is role-based authorization already, require authenticated tenant user access.
   - Since templates are system-level, listing should not require company ownership of the template, but access should still occur within an authenticated company session.

6. **Implement hire-agent command**
   - Add command/handler for hiring from a template.
   - Inputs:
     - `TemplateId`
     - `DisplayName`
     - `AvatarUrl`
     - `Department`
     - `RoleName`
     - `Personality` or personality overrides
     - `Seniority`
   - Behavior:
     - resolve current company context
     - load template
     - validate template exists
     - create new `Agent`
     - copy template defaults into company-owned agent config fields
     - apply user overrides to allowed identity/profile fields
     - set `Status = active`
     - set `TemplateId`
     - persist timestamps
   - Be explicit about merge behavior:
     - identity/profile fields are overridden by user input
     - config defaults are copied from template
     - if personality is both template-driven and user-editable, merge user-provided values over template defaults rather than discarding defaults
   - Return created agent id and summary DTO.

7. **Validation**
   - Add server-side validation for hire request:
     - required template id
     - required display name
     - max lengths where appropriate
     - valid URL/reference format for avatar if existing conventions require it
     - allowed enum/status/seniority values if modeled
   - Reject invalid or cross-tenant operations safely.
   - Keep error responses field-oriented if the app already supports validation summaries.

8. **Expose API endpoints**
   - Add endpoints for:
     - `GET` template catalog
     - `POST` hire agent from template
     - optionally `GET` roster if not already available and needed for UI completion
   - Follow existing API route conventions.
   - Ensure tenant context and authorization are enforced consistently.

9. **Implement/update Blazor web UX**
   - Add a page or section for browsing agent templates.
   - Show at minimum:
     - role name
     - department
     - short description/persona summary
   - Add hire action leading to a form/modal/page.
   - Hire form should allow editing:
     - name
     - avatar
     - department
     - role
     - personality
     - seniority
   - On success:
     - create the agent
     - navigate to roster or refresh roster
     - show success feedback
   - Keep SSR-first and simple.

10. **Roster integration**
   - Ensure the roster view lists newly created agents with:
     - name
     - role
     - department
     - status
   - If roster page already exists, integrate with it.
   - If not, add a minimal roster page sufficient to verify ST-201 outcome.

11. **Audit and timestamps**
   - Even if full audit views are later work, preserve created/updated timestamps.
   - If lightweight business audit hooks already exist, emit an event like `agent.hired` with actor/company/template references.
   - Do not build a large audit subsystem in this task if absent.

12. **Testing**
   - Add tests covering:
     - seeded templates exist
     - template catalog returns required seeded roles
     - hiring from template creates an `agents` row for the current company
     - created agent has `active` status by default
     - template defaults are copied, not referenced indirectly
     - user overrides are applied
     - tenant A cannot create/read agent data for tenant B
   - Prefer integration tests around application + persistence if test infrastructure exists.

13. **Keep implementation extensible**
   - Avoid embedding role-specific logic in code paths outside seed/config data.
   - Structure DTOs and commands so ST-202 can extend agent profile management without rework.
   - Keep template schema flexible for future additions.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify database/migrations:
   - apply migrations using the repo’s normal startup path or EF tooling if configured
   - confirm `agent_templates` contains at least the six required seeded roles

4. Manual verification in web app:
   - sign in as a user with a valid company membership
   - open the agent template catalog
   - confirm templates for finance, sales, marketing, support, operations, and executive assistant are visible
   - hire an agent from one template
   - customize name, avatar, department, role, personality, and seniority
   - submit and confirm success
   - open roster and verify the new agent appears with `active` status

5. Data verification:
   - inspect persisted agent record
   - confirm:
     - `company_id` is set correctly
     - `template_id` references the selected template
     - template defaults were copied into agent config JSON fields
     - overridden identity/profile fields reflect user input
     - timestamps were set

6. Negative-path verification:
   - attempt hire with invalid template id and confirm safe validation/not-found behavior
   - attempt cross-tenant access if test harness supports it and confirm forbidden/not found behavior
   - submit invalid form data and confirm field-level validation

# Risks and follow-ups
- **Existing schema mismatch risk**
  - The repo may already contain partial agent models or different naming. Adapt to current conventions rather than forcing the architecture doc literally.

- **Seeding strategy ambiguity**
  - If the solution lacks a standard seed mechanism, choose the least invasive option consistent with production safety and idempotency.

- **JSON config shape drift**
  - Template and agent config JSON contracts may evolve in ST-202/ST-203. Keep contracts explicit and centralized to avoid migration pain.

- **Tenant enforcement gaps**
  - If tenant scoping is not consistently implemented yet, do not silently bypass it for system templates or agent creation. Use the existing tenant context abstraction everywhere.

- **UI scope creep**
  - Keep the web UX minimal and functional. Do not build full profile management or analytics in this task.

- **Follow-ups likely needed after this task**
  - ST-202 agent operating profile management
  - ST-203 autonomy levels and policy guardrails
  - ST-204 richer roster/profile views
  - audit event enrichment for hire actions
  - avatar upload/storage flow beyond URL/reference only