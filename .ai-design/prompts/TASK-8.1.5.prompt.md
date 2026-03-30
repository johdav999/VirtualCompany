# Goal
Implement versioned, migration-backed seed data for the agent template catalog so `agent_templates` are created and updated through the normal .NET/PostgreSQL migration flow instead of ad hoc startup logic or manual SQL. This work supports **TASK-8.1.5** under **ST-201 Agent template catalog and hiring flow**.

The outcome should ensure:
- baseline system agent templates exist for the hiring flow,
- seed data changes are tracked in source control,
- updates are repeatable and environment-safe,
- future template revisions can be introduced via new migrations without breaking existing company-owned agents.

# Scope
In scope:
- Add or refine a durable seeding approach for `agent_templates` using EF Core migrations or an equivalent migration-backed mechanism already used in this solution.
- Seed at least the story-required templates for:
  - finance
  - sales
  - marketing
  - support
  - operations
  - executive assistant
- Ensure seeded template records include stable identifiers and structured default JSON payloads aligned with the architecture/backlog intent.
- Make the seeding idempotent from the perspective of database migration history.
- Keep template seed data clearly versioned in code so future changes are additive and reviewable.

Out of scope:
- Full hiring UI changes.
- Agent creation flow changes beyond what is necessary to keep compatibility with seeded templates.
- Runtime startup seeding that bypasses migrations.
- Introducing a separate seed-data framework unless the repo already uses one and it is clearly the established pattern.

# Files to touch
Inspect the repository first, then update the minimal correct set. Likely candidates include:

- `src/VirtualCompany.Infrastructure/...`  
  - EF Core `DbContext`
  - entity type configuration for `agent_templates`
  - migrations folder
  - any existing seed/migration helpers
- `src/VirtualCompany.Domain/...`
  - `AgentTemplate` entity if fields/constants need alignment
- `src/VirtualCompany.Application/...`
  - only if template DTO/query assumptions require updates
- `README.md`
  - only if there is an established migrations/seeding section that should mention the new approach

Also inspect for:
- existing `IEntityTypeConfiguration<>` classes,
- existing `HasData(...)` usage,
- existing migration naming conventions,
- JSON serialization conventions for JSONB columns,
- any startup seeding code that currently inserts agent templates and should be removed or deprecated.

# Implementation plan
1. **Discover the current persistence pattern**
   - Inspect the Infrastructure project for:
     - EF Core provider and migration setup,
     - `DbContext` registration,
     - `agent_templates` mapping,
     - any current startup seeding or bootstrap scripts.
   - Determine whether the project already uses:
     - `HasData(...)`,
     - custom migration SQL,
     - or a seed runner invoked during app startup.
   - Follow the existing project pattern unless it conflicts with the task requirement that seed data be versioned/migrated.

2. **Define a stable seed model for agent templates**
   - Ensure each system template has a stable primary key value that will not change across environments.
   - Prefer deterministic GUIDs committed in source control.
   - Seed records should cover the core columns expected by the schema, likely including:
     - `id`
     - `role_name`
     - `department`
     - `default_persona_json`
     - `default_objectives_json`
     - `default_kpis_json`
     - `default_tools_json`
     - `default_scopes_json`
     - `default_thresholds_json`
     - `default_escalation_rules_json`
     - `created_at`
   - Keep JSON payloads valid, minimal, and realistic for v1. Do not overdesign behavior into code when config can express it.

3. **Implement migration-backed seeding**
   - Use the repo’s established EF Core approach:
     - If `HasData(...)` is already used and works cleanly with the JSONB mapping, add seed definitions there and generate a migration.
     - If JSONB/value conversion makes `HasData(...)` awkward, create an explicit migration that inserts the seed rows with SQL or migration builder calls.
   - The migration should:
     - insert the required baseline templates,
     - be reversible where practical,
     - avoid mutating company-owned `agents` records.
   - If there is existing runtime seeding for these templates, remove it or make it a no-op to avoid duplicate inserts and drift.

4. **Create a maintainable seed definition structure**
   - Centralize template seed definitions in a single place in Infrastructure so future migrations can reference or copy from a known source.
   - Suggested pattern:
     - a static class like `AgentTemplateSeedData` with constants for template IDs and serialized JSON payloads,
     - plus migration code that uses those values.
   - Keep names and departments aligned with ST-201 and the architecture.
   - Make sure seeded templates are clearly system templates, not tenant-owned records.

5. **Preserve forward migration semantics**
   - Design this so future template changes are handled by new migrations rather than editing historical migrations.
   - If the repo currently edits old seed migrations, stop that pattern.
   - Add comments where helpful indicating:
     - seeded templates are defaults for new hires,
     - changes to template defaults should not retroactively rewrite already-created `agents` unless a future explicit migration requires it.

6. **Verify compatibility with the hiring flow**
   - Confirm any existing query/service that lists available templates can read the seeded records without additional changes.
   - If ordering or filtering is expected, ensure the seeded data supports it or document the gap.
   - Do not introduce breaking schema changes unless absolutely necessary.

7. **Keep implementation small and production-safe**
   - Prefer additive changes.
   - Avoid startup-time database writes.
   - Avoid environment-specific seed branching.
   - Ensure migration execution on a non-empty database is safe.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Generate/apply migrations if needed by the repo workflow, then verify the migration compiles cleanly.

3. Inspect the resulting database behavior:
   - confirm `agent_templates` contains the six required baseline templates,
   - confirm each row has stable IDs and valid JSON payloads,
   - confirm rerunning the app does not attempt duplicate seed inserts.

4. Run tests:
   - `dotnet test`

5. If there are integration or repository tests around templates/hiring flow, update or add focused coverage to verify:
   - templates are available after migration,
   - expected role names/departments are present,
   - no runtime seeding dependency remains.

6. Manually review migration contents to ensure:
   - no accidental destructive changes,
   - no tenant data is touched,
   - no existing hired agents are modified.

# Risks and follow-ups
- **JSONB seeding friction:** EF Core `HasData` can be awkward with JSON/value converters. If that happens, prefer explicit migration SQL over fragile model seeding.
- **Timestamp churn:** avoid dynamic timestamps in seed definitions if they cause migration diffs; use fixed UTC values.
- **Future template evolution:** changing defaults later should be done in new migrations and should not silently rewrite existing company agents created from earlier template versions.
- **Potential missing metadata:** if the current schema lacks fields needed for richer catalog behavior (e.g. code, sort order, active flag), do not expand scope unless clearly necessary; note it as a follow-up.
- **Existing startup seeding drift:** if ad hoc seeding already exists, removing it may affect local dev assumptions; update docs or comments as needed.
- Follow-up candidate: introduce explicit template version metadata in the schema if future stories require catalog evolution visibility beyond migration history alone.