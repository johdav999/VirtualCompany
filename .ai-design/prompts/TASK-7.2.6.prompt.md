# Goal
Implement backlog task **TASK-7.2.6 — Store branding/settings in JSONB where flexibility is needed** for **ST-102 Company workspace creation and onboarding wizard**.

The coding agent should update the company/workspace persistence model so that flexible branding and settings data are stored in PostgreSQL **JSONB** columns, while keeping strongly-typed core onboarding fields relational. The implementation should fit the existing **.NET modular monolith** architecture and support current onboarding needs without overfitting the schema.

This task is specifically about:
- enabling flexible persistence for company branding/settings
- wiring the domain, application, infrastructure, and database layers consistently
- preserving tenant-safe company creation/update flows
- avoiding premature hardcoding of fields likely to evolve during onboarding

# Scope
In scope:
- Add or confirm `branding_json` and `settings_json` storage on the `companies` table using PostgreSQL JSONB.
- Introduce appropriate domain/application representations for branding/settings.
- Map those representations through EF Core/Npgsql to JSONB.
- Ensure company creation/onboarding flows can persist and retrieve branding/settings safely.
- Add validation/defaulting only where needed for current onboarding behavior.
- Add/update tests covering persistence and round-trip behavior.

Out of scope:
- Full onboarding wizard UX redesign.
- Broad template engine implementation.
- Arbitrary schema-less storage for all company fields.
- New unrelated company settings screens.
- Mobile-specific changes.
- Large refactors outside the company setup module.

Implementation should follow these architectural constraints:
- Keep core company identity fields relational: name, industry, business type, timezone, currency, language, compliance region, etc.
- Use JSONB only for flexible/extensible configuration such as branding and non-core settings.
- Keep the model extensible without requiring code changes for every future branding/settings addition.
- Maintain clean architecture boundaries across Domain, Application, Infrastructure, and API/Web.

# Files to touch
Inspect the existing solution first, then update the most relevant files in these areas as needed.

Likely files/modules:
- `src/VirtualCompany.Domain/...`
  - company aggregate/entity
  - value objects or configuration models for branding/settings
- `src/VirtualCompany.Application/...`
  - company creation/update commands and handlers
  - onboarding DTOs/contracts
  - validators
- `src/VirtualCompany.Infrastructure/...`
  - EF Core `DbContext`
  - entity type configurations
  - migrations
  - JSON serialization/value conversion if needed
- `src/VirtualCompany.Api/...`
  - request/response contracts if API owns onboarding endpoints
- `src/VirtualCompany.Web/...`
  - onboarding form models or bindings only if required for compile/runtime consistency
- Tests project(s), if present
  - domain/application tests
  - infrastructure persistence tests

At minimum, identify and touch:
- the `Company` persistence model
- EF configuration for `companies`
- migration(s) for JSONB columns
- any command/DTO path used by workspace creation

# Implementation plan
1. **Inspect current company setup implementation**
   - Find the `Company` entity/aggregate and current onboarding flow for ST-102.
   - Determine whether `branding_json` and `settings_json` already exist in code or database migrations.
   - Identify whether the project uses:
     - EF Core owned types with JSON mapping
     - string-backed JSON serialization
     - raw `JsonDocument`
     - dictionaries/POCOs
   - Reuse the project’s existing persistence conventions.

2. **Define a pragmatic flexible model**
   - Keep relational fields for stable onboarding data.
   - Introduce or refine flexible models for:
     - `Branding`
     - `CompanySettings`
   - Prefer strongly-typed top-level containers with room for extension, for example:
     - branding: logo URL, primary color, secondary color, theme hints, etc.
     - settings: onboarding progress, wizard preferences, feature toggles, locale/display preferences, template selections, or nested extension data
   - If the codebase already uses extension dictionaries or metadata bags, align with that pattern.
   - Do not create an overly rigid schema that defeats the purpose of JSONB.

3. **Update the domain model**
   - Add properties to the company entity if missing:
     - `Branding`
     - `Settings`
   - Ensure sensible defaults so new companies do not get null-reference issues.
   - Preserve invariants:
     - core required fields remain validated separately
     - branding/settings are optional/flexible
   - If domain methods exist for company creation or onboarding updates, extend them to accept branding/settings.

4. **Update application contracts and handlers**
   - Review workspace creation and onboarding commands/requests.
   - Add branding/settings payload support only where appropriate.
   - If onboarding currently persists wizard progress elsewhere, decide whether current task should move that flexible state into `settings_json` or simply make the storage available for it.
   - Add validation for obvious constraints only, such as:
     - max lengths for URLs/labels if modeled
     - valid color format if explicitly supported
     - null-safe handling
   - Avoid overvalidating unknown future JSON fields.

5. **Configure EF Core JSONB mapping**
   - In Infrastructure, map company branding/settings to PostgreSQL `jsonb`.
   - Prefer native Npgsql JSON mapping if already used in the solution.
   - Ensure the generated schema uses:
     - `branding_json jsonb`
     - `settings_json jsonb`
   - If naming conventions differ, preserve project conventions while still satisfying the task intent.
   - Ensure change tracking works correctly for nested JSON objects.

6. **Add or update database migration**
   - If columns do not exist:
     - add `branding_json` and `settings_json` as JSONB columns on `companies`
   - If columns exist but are incorrectly typed:
     - migrate them safely to JSONB
   - Set appropriate nullability/defaults based on current model conventions.
   - Be careful with existing data:
     - backfill empty objects if needed
     - avoid destructive migration behavior

7. **Wire serialization and defaults**
   - Ensure reads/writes round-trip cleanly.
   - Use consistent JSON serializer options if the project centralizes them.
   - Avoid storing large opaque blobs when a small structured object is enough.
   - Ensure empty/default branding/settings serialize predictably.

8. **Update any onboarding/web bindings only as needed**
   - If the web onboarding form or API contracts break due to model changes, update them minimally.
   - Do not expand UX beyond what is necessary to support persistence.
   - If branding/settings are not yet exposed in UI, it is acceptable to support them at the backend/domain level only, provided current flows still work.

9. **Add tests**
   - Add persistence tests verifying:
     - company saves with branding/settings
     - JSONB fields round-trip correctly
     - null/default values behave as expected
   - Add application tests for company creation/update handlers if present.
   - If migration tests are part of the repo conventions, include them.

10. **Keep the implementation future-friendly**
   - Document via code comments only where necessary.
   - Avoid locking the team into a brittle schema for branding/settings.
   - Keep the design aligned with backlog notes:
     - “Store branding/settings in JSONB where flexibility is needed.”
     - “Keep template model extensible without code changes.”

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in the normal workflow:
   - generate/apply the migration locally
   - verify the `companies` table contains JSONB columns for branding/settings

4. Validate persistence behavior manually or via tests:
   - create a company with standard relational onboarding fields only
   - create/update a company with branding/settings payloads
   - confirm values are persisted and retrieved correctly
   - confirm empty/default branding/settings do not break company creation

5. Validate no regression in onboarding flow:
   - existing workspace creation still succeeds
   - existing required fields remain enforced
   - tenant/company scoping behavior is unchanged

6. If infrastructure tests support SQL inspection:
   - verify generated column types are `jsonb`, not `text`

# Risks and follow-ups
- **Risk: over-modeling JSON fields**
  - Making branding/settings too rigid undermines the flexibility goal.
  - Prefer a small typed shell with optional nested extensibility.

- **Risk: weak EF change tracking for nested JSON**
  - Depending on mapping style, updates to nested properties may not be detected reliably.
  - Verify round-trip update behavior in tests.

- **Risk: migration/data compatibility**
  - If a prior schema already stores branding/settings differently, migration must preserve existing data.

- **Risk: null/default handling**
  - JSON-backed properties can cause runtime issues if defaults are not initialized consistently.

- **Risk: leaking flexible config into core invariants**
  - Do not move stable required company fields into JSONB.

Suggested follow-ups after this task:
- Persist onboarding wizard progress/resume state in `settings_json` if not already implemented.
- Add template-driven default branding/settings population during workspace creation.
- Add server-side validation helpers for known branding fields as the UI matures.
- Consider indexing specific JSONB paths later only if query patterns emerge.