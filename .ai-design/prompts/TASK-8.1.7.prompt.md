# Goal
Implement backlog task **TASK-8.1.7 — Avoid hardcoding role behavior outside config where possible** for story **ST-201 Agent template catalog and hiring flow**.

The coding agent should refactor or extend the current agent template/hiring implementation so that **role-specific behavior is driven by persisted template/config data**, not scattered conditional logic in services, orchestration setup, UI, or seed code.

This task supports the architecture decision of using **one shared orchestration engine with configurable agent personas** and the story note: **“Avoid hardcoding role behavior outside config where possible.”**

# Scope
Focus only on the parts of the codebase needed to ensure that agent role behavior is configuration-driven for the template catalog and hiring flow.

In scope:
- Agent template seed/config model and related persistence
- Hiring flow that creates company-owned agents from templates
- Any application/domain logic currently branching on role names like finance, sales, marketing, support, operations, executive assistant
- UI/API mapping that assumes role-specific defaults in code
- Validation and tests for template-driven behavior

Out of scope unless required to complete this task safely:
- Large redesign of orchestration runtime
- New product features beyond ST-201
- Full policy engine changes from ST-203
- Broad schema redesign unrelated to template-driven defaults
- Mobile work

Key implementation intent:
- If behavior differs by role, prefer expressing it in:
  - `agent_templates` seed data
  - template config objects / JSON fields
  - mapping/configuration services
- Avoid role-name `switch` / `if` logic outside a narrow compatibility layer if one is temporarily necessary
- Keep the system extensible so adding a new template should require **seed/config changes**, not new business logic branches

# Files to touch
Start by inspecting these likely areas and update the minimal correct set:

- `README.md` if seed/config behavior or developer setup docs need a short note
- `src/VirtualCompany.Domain/**/*`
  - Agent / AgentTemplate entities, value objects, enums, factory methods
- `src/VirtualCompany.Application/**/*`
  - Commands/handlers for hiring agents from templates
  - DTOs/view models for template catalog and agent creation
  - Validation logic
  - Any services that derive defaults from role names
- `src/VirtualCompany.Infrastructure/**/*`
  - EF Core entity configuration
  - Seed data / migrations
  - repositories
  - JSON serialization for template defaults
- `src/VirtualCompany.Api/**/*`
  - Endpoints/controllers for template catalog and hiring flow
- `src/VirtualCompany.Web/**/*`
  - Template catalog / hire-agent UI if it embeds role-specific assumptions
- `src/VirtualCompany.Shared/**/*`
  - Shared contracts if template config is exposed across layers
- Test projects under `tests/**/*` or any existing test folders
  - Unit tests
  - integration tests
  - seed/config tests

Also search the whole solution for likely hardcoded role behavior:
- `"finance"`
- `"sales"`
- `"marketing"`
- `"support"`
- `"operations"`
- `"executive assistant"`
- `"executive_assistant"`
- `"role_name"`
- `switch`
- `if` branches tied to agent role/template names

# Implementation plan
1. **Inspect current implementation and identify hardcoded role behavior**
   - Search for role-name comparisons across Domain, Application, Infrastructure, API, and Web.
   - Identify every place where defaults or behavior are derived from role names in code, such as:
     - default department
     - persona/personality
     - objectives/KPIs
     - tools/scopes/thresholds/escalation rules
     - avatar/name suggestions
     - UI labels or descriptions
   - Classify each occurrence:
     - acceptable display-only constant
     - should move to template config
     - temporary compatibility logic to keep

2. **Define or strengthen a template-driven configuration contract**
   - Ensure the `agent_templates` model can carry all role-specific defaults needed by ST-201.
   - Prefer existing fields from architecture/backlog:
     - `role_name`
     - `department`
     - `default_persona_json`
     - `default_objectives_json`
     - `default_kpis_json`
     - `default_tools_json`
     - `default_scopes_json`
     - `default_thresholds_json`
     - `default_escalation_rules_json`
   - If the current implementation uses fewer fields, add only what is necessary to remove hardcoded behavior safely.
   - Keep the model generic and future-friendly; do not add per-role columns.

3. **Refactor hiring flow to copy defaults from template config only**
   - Update the agent creation/hiring command handler or service so it:
     - loads the selected template
     - copies template defaults into the new company-owned agent record
     - applies user customizations such as name, avatar, department, role, personality, seniority
   - Remove role-specific branching from the hiring flow.
   - If customizations override template defaults, make precedence explicit and consistent.

4. **Refactor template catalog/read models to use persisted template data**
   - Ensure template listing/details shown in API/UI come from stored template records/config, not hardcoded in code.
   - If there is a static in-memory catalog, replace or isolate it behind a provider that reads from seed-backed persistence or a single configuration source.
   - Keep any fallback logic minimal and clearly marked if unavoidable.

5. **Consolidate role behavior behind a single resolver/provider if needed**
   - If multiple layers need template-derived defaults, introduce a focused abstraction such as:
     - `IAgentTemplateProvider`
     - `IAgentTemplateDefaultsResolver`
     - similar existing service
   - This service should read config/template data and return normalized defaults.
   - Do not let controllers/pages duplicate role logic.

6. **Preserve extensibility**
   - Make sure adding a new role template does not require:
     - new `switch` cases
     - new hardcoded UI branches
     - new handler logic
   - The system should work for any valid template record with supported config fields.

7. **Handle validation carefully**
   - Validate required template fields and malformed JSON/config payloads.
   - Fail safely if a template is incomplete rather than silently injecting hardcoded defaults.
   - If some defaults are optional, document and test the fallback behavior.

8. **Update seed data and migrations if needed**
   - If template records are seeded in Infrastructure, move role-specific defaults there.
   - Add/update EF migrations only if schema changes are required.
   - Ensure seed data includes at least the story roles:
     - finance
     - sales
     - marketing
     - support
     - operations
     - executive assistant

9. **Add tests proving behavior is config-driven**
   - Add or update tests to verify:
     - hiring an agent copies defaults from the selected template
     - changing template seed/config changes resulting agent defaults without code changes
     - no role-name branching is required for supported templates
     - unknown/new template records can still be hired if config is valid
   - Prefer tests at the application/service level, with integration tests if seed/persistence behavior is involved.

10. **Keep changes small and clean**
   - Do not over-engineer.
   - Remove obsolete helper methods/constants after refactor.
   - Keep naming aligned with existing project conventions.

Implementation notes for the coding agent:
- Favor existing architecture and patterns already present in the solution.
- If you find hardcoded behavior that cannot be fully removed without a larger redesign, isolate it in one place and leave a clear TODO with rationale.
- Do not introduce breaking API changes unless necessary; if needed, update all affected callers in the same change.

# Validation steps
Run the following after implementation:

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Perform targeted verification in code/tests:
   - Confirm no business logic branches on specific role names remain in hiring/template flow, except any explicitly documented compatibility shim.
   - Confirm template catalog data is sourced from config/persistence rather than scattered constants.
   - Confirm hired agents receive defaults from template configuration records.
   - Confirm user-provided customizations still override allowed fields correctly.
   - Confirm seed templates for the required roles still work.

4. If migrations were added:
   - Ensure migration compiles and database startup path remains valid.
   - Verify seed data inserts/updates cleanly.

5. If UI/API was touched:
   - Verify template list and hire flow still render/return expected values.
   - Verify no role-specific labels/descriptions are broken by the refactor.

# Risks and follow-ups
- **Risk: hidden hardcoded behavior remains** in UI text, mapping layers, or tests. Mitigate with solution-wide search for role names and role-based branching.
- **Risk: malformed or incomplete template config** could now surface at runtime. Mitigate with stronger validation and tests.
- **Risk: schema/seed changes may affect existing local databases**. Mitigate with careful migration/seed handling.
- **Risk: some role behavior may actually belong to later stories** like ST-202/ST-203. Keep this task focused on ST-201 hiring/template defaults and avoid broad orchestration changes.

Suggested follow-ups if discovered during implementation:
- Add explicit template config validation objects/schemas if JSON is currently too loose.
- Introduce versioning for template seed data if not already present.
- Add a dedicated admin/internal test ensuring new templates can be added without code changes.
- In later stories, continue the same pattern so orchestration, tools, and guardrails also resolve behavior from agent config rather than role-name conditionals.