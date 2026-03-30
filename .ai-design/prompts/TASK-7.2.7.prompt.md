# Goal
Implement backlog task **TASK-7.2.7 — Keep template model extensible without code changes** for **ST-102 Company workspace creation and onboarding wizard**.

The coding agent should update the onboarding/template design so that **industry/business templates are data-driven and extensible**, with no need to add new C# enums, switch statements, or hardcoded mappings when introducing new templates or template fields.

This work should align with the architecture guidance:
- modular monolith
- ASP.NET Core + Blazor Web App
- PostgreSQL with flexible JSONB where appropriate
- onboarding under the Company Setup module
- template behavior driven by persisted configuration

Because this story has no explicit acceptance criteria for the task itself, treat the intended outcome as:

- onboarding templates are represented as persisted/configurable data
- template payloads support flexible fields via JSON/config objects
- application and UI logic consume template definitions generically
- adding or changing a template should primarily be a data/seed change, not a code change
- existing onboarding flow remains functional

# Scope
Focus only on the minimum vertical slice needed to make the template model extensible in the onboarding/company setup area.

Include:
- discovery of current onboarding template implementation
- refactor of any hardcoded template definitions into a persisted/config-driven model
- support for flexible template metadata/defaults using JSON-backed structures where appropriate
- application/query layer changes so UI/API reads available templates dynamically
- seed/migration updates for initial templates
- preservation of tenant-safe behavior and clean boundaries

Do not include:
- a full admin UI for managing templates unless trivial and already scaffolded
- unrelated onboarding redesign
- broad refactors outside Company Setup unless required to remove hardcoded template coupling
- speculative support for every future template type; implement a clean extensible foundation

If the current codebase does not yet have onboarding templates implemented, create the smallest architecture-consistent foundation that supports:
- listing available templates
- selecting a template during onboarding
- applying template defaults to company setup data through generic config structures

# Files to touch
Inspect first, then update only the relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - company setup entities/value objects
  - template entities or configuration models
- `src/VirtualCompany.Application/**`
  - onboarding commands/queries/DTOs
  - validators
  - mapping/application services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - migrations
  - seed data
  - repositories/query implementations
- `src/VirtualCompany.Web/**`
  - onboarding wizard pages/components
  - view models
  - API client bindings if applicable
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if templates are exposed via API
- `README.md`
  - only if a brief note is needed for template extensibility or seed behavior

Also inspect solution-wide for hardcoded template usage:
- enums for industry/business templates
- switch/if chains mapping template names to defaults
- static lists in Blazor pages
- constants duplicated across layers

# Implementation plan
1. **Discover the current implementation**
   - Search the solution for onboarding/template-related terms such as:
     - `template`
     - `industry`
     - `business type`
     - `onboarding`
     - `workspace creation`
     - `wizard`
   - Identify:
     - where templates are defined
     - whether they are hardcoded in UI, application, or domain
     - how defaults are applied to company creation/setup
     - whether JSON/config fields already exist on company/settings models

2. **Design the extensible template model**
   - Introduce or refine a persisted template entity for onboarding/company setup if one does not already exist.
   - Prefer a model that supports:
     - stable identifier/code
     - display name
     - category/type if needed
     - optional industry/business type tags
     - active/sort metadata
     - flexible defaults payload in JSON
     - optional metadata payload in JSON
   - Keep the model generic enough that new template fields can be added in data without requiring schema or code changes for every addition.
   - If architecture already suggests JSONB for flexible settings, use that pattern.

3. **Remove hardcoded template behavior**
   - Replace switch statements/static dictionaries/static lists with repository/query-driven retrieval.
   - Ensure the onboarding flow reads available templates from persistence/seeded data.
   - Ensure applying a template uses generic defaults/config payloads rather than role-specific code branches where possible.
   - If some mapping logic is unavoidable, isolate it behind a single translator/service and make it schema-tolerant.

4. **Persist template definitions**
   - Add EF Core model/configuration and migration if needed.
   - Seed initial onboarding templates in Infrastructure.
   - Seed enough examples to prove extensibility, e.g. multiple industries/business types with differing defaults.
   - Make seed data idempotent and version-friendly.

5. **Update application contracts**
   - Add/adjust query DTOs for template listing.
   - Add/adjust command DTOs for selecting/applying a template during onboarding.
   - Ensure validators allow flexible template payloads while still validating required top-level fields.
   - Avoid leaking EF entities directly to UI/API.

6. **Update onboarding UI/API flow**
   - Replace any hardcoded dropdown/options with dynamic template data.
   - Ensure the wizard can:
     - load templates
     - display template names/descriptions
     - persist selected template identifier/code
     - apply defaults into onboarding state
   - Keep UI resilient if template metadata contains optional/unknown fields.

7. **Preserve backward compatibility**
   - If existing records store old template identifiers or assumptions, avoid breaking them.
   - Add null-safe handling for companies created before this change.
   - If necessary, support fallback behavior when no template is selected.

8. **Add tests**
   - Add or update tests around:
     - template retrieval from persistence
     - applying template defaults without hardcoded branching
     - onboarding behavior when a new seeded template is added
     - graceful handling of unknown optional JSON fields
   - Prefer application/integration tests over brittle UI-only tests where possible.

9. **Keep implementation clean**
   - Follow existing project conventions.
   - Keep module boundaries intact:
     - Domain defines concepts
     - Application orchestrates use cases
     - Infrastructure persists/seeds
     - Web/API consumes DTOs/services
   - Do not introduce unnecessary abstractions.

# Validation steps
Run the relevant validation after implementation:

1. Restore/build/tests
   - `dotnet build`
   - `dotnet test`

2. If migrations are used, verify they apply cleanly
   - generate/apply migration per repo conventions
   - confirm seeded templates exist

3. Manual verification of onboarding flow
   - start the app if practical under existing repo conventions
   - navigate to workspace creation/onboarding
   - confirm template options are loaded dynamically, not hardcoded
   - select a template and verify defaults are applied/persisted
   - confirm onboarding still works without template selection if that path exists

4. Extensibility verification
   - add one additional seeded template entry by data only
   - verify it appears in the onboarding flow without further code changes
   - verify unknown optional metadata/default fields do not break deserialization or rendering

5. Regression checks
   - confirm company creation fields from ST-102 still work:
     - name
     - industry
     - business type
     - timezone
     - currency
     - language
     - compliance region
   - confirm wizard progress/resume behavior is not broken if already implemented

# Risks and follow-ups
- **Risk: hidden hardcoding in UI or validators**
  - Search all layers thoroughly for duplicated template assumptions.

- **Risk: over-modeling too early**
  - Keep the template schema simple and JSON-friendly; do not build a full template engine.

- **Risk: brittle JSON contracts**
  - Use tolerant deserialization and validate only required top-level invariants.

- **Risk: migration/seed drift**
  - Make seed data deterministic and safe for repeated runs.

- **Risk: coupling template structure to one onboarding screen**
  - Keep template retrieval/application in application services, not embedded in Blazor page logic.

Suggested follow-ups if not completed in this task:
- admin management UI for onboarding templates
- versioning strategy for template definitions
- audit trail for template selection/application during onboarding
- richer template metadata for branding, starter agents, workflows, and knowledge packs