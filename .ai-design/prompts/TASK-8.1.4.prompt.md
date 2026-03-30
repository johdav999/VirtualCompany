# Goal
Implement backlog task **TASK-8.1.4** for **ST-201 — Agent template catalog and hiring flow** so that when a company hires an agent from a system template, the template’s default configuration values are **materialized into the new company-owned `agents` record** rather than being referenced implicitly at runtime.

This task specifically ensures the story requirement:

- **Template defaults are copied into company-owned agent configuration records**

The implementation should fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and keep agent behavior driven by persisted configuration rather than hardcoded template logic.

# Scope
In scope:

- Identify the current agent hiring/create flow from template.
- Ensure creation of an `agents` record copies all relevant defaults from `agent_templates` into company-owned fields.
- Copy, at minimum, the template-backed configuration fields described by the architecture/data model:
  - `role_name`
  - `department`
  - `personality_json`
  - `objectives_json`
  - `kpis_json`
  - `tool_permissions_json`
  - `data_scopes_json`
  - `approval_thresholds_json`
  - `escalation_rules_json`
- Preserve `template_id` linkage for provenance, but do not depend on template values after creation.
- Ensure copied values are deep-copied / newly assigned so later template edits do not mutate existing hired agents.
- Add or update tests covering the copy behavior.
- Keep implementation tenant-scoped and aligned with CQRS-lite / application-service patterns already used in the repo.

Out of scope:

- Full template catalog seeding, migration authoring, or adding new templates unless required for tests.
- UI redesign of the hiring flow beyond wiring existing create inputs.
- Editing template defaults after hire and propagating changes to existing agents.
- Audit/event fan-out unless already part of the existing create-agent flow.
- Broader ST-202 profile editing behavior.

# Files to touch
Touch only the files needed after inspecting the codebase. Likely areas include:

- `src/VirtualCompany.Domain/...`
  - Agent aggregate/entity
  - Agent template entity/value objects
- `src/VirtualCompany.Application/...`
  - Create/hire agent command + handler
  - DTOs/contracts for template-based creation
  - Validation logic
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configuration
  - Repositories
  - JSON mapping/value conversion if needed
- `src/VirtualCompany.Api/...`
  - Endpoint/controller wiring only if request/response contracts must change
- `src/VirtualCompany.Web/...`
  - Only if the current UI is not sending enough data to support template-based creation
- Test projects in the solution
  - Unit tests for command handler/domain factory
  - Integration tests for persistence behavior

Before editing, inspect the actual implementation and update the plan to match the real code structure.

# Implementation plan
1. **Inspect the current hiring flow**
   - Find the command/endpoint/service used to create an agent from a template.
   - Determine whether the current implementation:
     - only stores `template_id`,
     - partially copies fields,
     - or resolves template defaults dynamically later.
   - Identify the exact domain and persistence models for `Agent` and `AgentTemplate`.

2. **Define the source-to-target field mapping**
   - Map template defaults to agent-owned fields using the architecture model as the baseline:
     - `agent_templates.role_name` -> `agents.role_name`
     - `agent_templates.department` -> `agents.department`
     - `agent_templates.default_persona_json` -> `agents.personality_json`
     - `agent_templates.default_objectives_json` -> `agents.objectives_json`
     - `agent_templates.default_kpis_json` -> `agents.kpis_json`
     - `agent_templates.default_tools_json` -> `agents.tool_permissions_json`
     - `agent_templates.default_scopes_json` -> `agents.data_scopes_json`
     - `agent_templates.default_thresholds_json` -> `agents.approval_thresholds_json`
     - `agent_templates.default_escalation_rules_json` -> `agents.escalation_rules_json`
   - If the real code uses different names, adapt to the existing model rather than forcing architecture names verbatim.
   - Preserve user-provided overrides from the hiring flow for fields such as:
     - display name
     - avatar
     - seniority
     - any explicitly editable identity/profile fields already supported by ST-201

3. **Implement copy-on-create behavior in the application/domain layer**
   - Centralize the copy logic in the domain factory, aggregate creation method, or command handler—prefer the place already responsible for constructing a new `Agent`.
   - Do not leave copy logic split across UI/API layers.
   - Ensure the new agent receives its own persisted values, not references to mutable template-owned objects.
   - If JSON/config fields are represented as strings, records, dictionaries, or JSON documents, create a true clone/new instance before assignment.

4. **Preserve provenance without runtime coupling**
   - Continue storing `template_id` on the agent if that relationship already exists or is expected by the model.
   - Ensure downstream reads of agent configuration use the agent’s own stored fields, not fallback-to-template behavior.
   - If any runtime fallback exists today, remove or constrain it so hired agents remain stable even if templates change later.

5. **Handle null/default behavior safely**
   - If some template default fields are null/empty, define consistent behavior:
     - copy null if the target field is nullable and that is already valid, or
     - normalize to empty JSON/object/collection only if that is the established project convention.
   - Do not introduce inconsistent serialization formats.
   - Keep validation aligned with existing create-agent rules.

6. **Update persistence mapping only if necessary**
   - If the `agents` table/entity already has the required columns, avoid schema churn.
   - If some target fields are missing from the current implementation but required by the story and architecture, add them through the project’s normal persistence pattern.
   - Keep tenant-owned data on the `agents` record and ensure `company_id` is always set.

7. **Add tests**
   - Add unit tests for the creation logic verifying:
     - template defaults are copied into the new agent
     - user-entered identity overrides are preserved
     - copied config remains unchanged if the template object is later modified in-memory
   - Add integration/persistence tests verifying:
     - a hired agent row contains copied values in persisted fields
     - the agent remains valid when loaded without re-reading template defaults
   - If there is an existing API test layer, add a happy-path create-from-template test there as well.

8. **Keep implementation minimal and story-focused**
   - Avoid broad refactors unless required to make the behavior correct.
   - Prefer small, explicit mapping code over speculative abstraction.
   - Add concise comments only where the copy-on-create behavior is non-obvious and business-critical.

# Validation steps
1. Inspect and run the relevant tests before changes:
   - `dotnet test`

2. Build after implementation:
   - `dotnet build`

3. Run targeted tests for the agent management area if available:
   - command handler/domain tests for agent creation
   - integration tests for create-from-template persistence

4. Manually verify behavior in code/tests:
   - Create an agent from a template.
   - Confirm the resulting `agents` entity/row contains copied configuration values.
   - Confirm `template_id` is retained for provenance if supported.
   - Confirm changing the source template afterward does not alter the existing agent’s stored config.

5. If there is an API or UI flow available locally, exercise the hire flow end-to-end and verify:
   - agent appears in roster with active/default status as already expected by ST-201
   - persisted agent config is self-contained

# Risks and follow-ups
- **Risk: hidden runtime fallback to template values**
  - Existing reads may still merge template defaults at query/runtime. Search for any such behavior and remove or document it.

- **Risk: shallow copy of mutable config objects**
  - If config fields are represented as mutable collections/documents, shallow assignment could cause accidental coupling. Ensure deep copy semantics.

- **Risk: schema/model drift**
  - The architecture describes several JSON fields, but the repo may use different names or partially implemented models. Adapt carefully to the actual codebase.

- **Risk: null/serialization inconsistencies**
  - JSONB-backed fields may have conventions around null vs `{}` vs `[]`. Follow existing patterns to avoid persistence/query bugs.

- **Risk: incomplete test coverage**
  - Without persistence-level tests, it is easy to pass unit tests while still relying on template reads elsewhere.

Follow-ups after this task, if needed:

- Add explicit acceptance tests for the full ST-201 hiring flow.
- Add audit events for “agent hired from template”.
- Consider storing template version metadata on the agent for future comparison/migration tooling.
- Consider a later admin feature to “refresh from template” explicitly, but never implicitly mutate existing company-owned agents.