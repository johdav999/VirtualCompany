# Goal
Implement backlog task **TASK-8.2.5 — Store flexible policy/config fields in JSONB with server-side validation** for **ST-202 Agent operating profile management** in the existing .NET modular monolith.

The coding agent should update the agent profile persistence and application flow so that flexible operating-profile fields are stored in PostgreSQL **JSONB** columns while still enforcing **strong server-side validation** for create/update operations.

This work must support the story requirement that agent profiles can edit and persist:
- objectives
- KPIs
- role brief
- tool permissions
- data scopes
- approval thresholds
- escalation rules
- trigger logic
- working hours
- status changes

The implementation should fit the architecture:
- ASP.NET Core backend
- PostgreSQL primary store
- modular monolith / clean boundaries
- tenant-scoped agent management
- JSONB for flexible config
- validation in application/API layer, not only client-side

# Scope
In scope:
- Identify the current agent entity/model, persistence mapping, commands/DTOs, validators, and API endpoints for agent profile management.
- Ensure flexible agent operating-profile fields are persisted as JSONB in PostgreSQL.
- Add or refine typed application-layer request models for these flexible fields.
- Add robust server-side validation with field-level errors for invalid configurations.
- Ensure updates modify `updated_at` and are reflected in persisted agent records.
- Preserve tenant scoping and existing authorization patterns.
- Keep implementation compatible with future policy guardrail work in ST-203.

Out of scope:
- Full audit history/versioning of config changes.
- UI redesign beyond any minimal contract adjustments required to compile.
- Policy execution engine behavior.
- New workflow/task restrictions except where already needed by current domain rules.
- Mobile-specific changes unless shared contracts require them.

# Files to touch
Inspect first, then update the minimal correct set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Agent aggregate/entity
  - value objects or enums for agent status/autonomy if present

- `src/VirtualCompany.Application/**`
  - Agent profile commands/handlers
  - request/response DTOs
  - validators
  - mapping logic between DTOs and domain/persistence models

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration for `agents`
  - JSONB column mappings
  - migrations
  - serialization/value conversion if needed

- `src/VirtualCompany.Api/**`
  - agent management endpoints/controllers
  - model binding / validation response behavior

- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for agent profile payloads

- Tests:
  - `tests/**` if present, or existing test projects under `src/*` if tests are colocated
  - add/update unit/integration tests for validation and persistence

Also inspect:
- existing migration strategy
- any global JSON serializer settings
- any existing Result/ValidationProblem patterns

# Implementation plan
1. **Discover the current agent profile implementation**
   - Find the `Agent` entity and current storage for:
     - `personality_json`
     - `objectives_json`
     - `kpis_json`
     - `tool_permissions_json`
     - `data_scopes_json`
     - `approval_thresholds_json`
     - `escalation_rules_json`
     - `trigger_logic_json`
     - `working_hours_json`
   - Determine whether these are currently:
     - plain strings
     - typed objects
     - dictionaries
     - unmapped placeholders
   - Find the create/update command flow for ST-202-related profile editing.

2. **Define typed application contracts for flexible config**
   - Introduce or refine request models for each flexible config area instead of accepting raw arbitrary JSON blobs directly at the API boundary.
   - Keep them flexible enough for future evolution, but structured enough to validate.
   - Prefer shapes like:
     - objectives: collection of structured items
     - KPIs: collection with name/target/unit/period/status fields as appropriate
     - tool permissions: tool/action/scope entries
     - data scopes: typed scope rules
     - approval thresholds: typed threshold definitions
     - escalation rules: typed escalation conditions/actions
     - trigger logic: typed trigger definitions
     - working hours: timezone/day/time-window structure
   - If the codebase already has these contracts, extend rather than replace them.

3. **Add server-side validation**
   - Implement validation in the application layer using the project’s existing validation approach.
   - Validation must produce field-level errors for invalid configurations.
   - At minimum validate:
     - required top-level fields where applicable
     - max lengths for strings like role brief, names, labels, descriptions
     - enum/status values
     - duplicate entries where disallowed
     - numeric ranges for thresholds/targets
     - valid day-of-week/time ranges for working hours
     - non-empty tool names/action types
     - no malformed/null collection items
     - trigger definitions contain required fields for their trigger type
     - escalation rules contain valid conditions and destinations
   - Reject invalid payloads before persistence.
   - Do not rely on client-side validation only.

4. **Map flexible fields to JSONB persistence**
   - In Infrastructure, configure EF Core mappings for the relevant `agents` columns as PostgreSQL `jsonb`.
   - Use the project’s established pattern for JSON serialization:
     - owned types to JSON if already used and supported
     - or value converters with `System.Text.Json`
   - Ensure nullability and defaults are handled consistently.
   - Avoid storing double-serialized JSON strings inside JSONB columns.

5. **Update domain/entity behavior**
   - Ensure the `Agent` entity supports updating profile fields cleanly.
   - Preserve invariants for:
     - status transitions if already modeled
     - updated timestamp changes on profile edits
   - Keep archived/restricted semantics intact if already implemented.
   - If domain methods exist, route updates through them rather than setting properties ad hoc.

6. **Update command handlers / endpoints**
   - Wire the validated request models into create/update agent profile flows.
   - Ensure tenant-scoped lookup of the target agent.
   - Persist updated JSONB-backed fields and `updated_at`.
   - Return appropriate validation errors and success responses using existing API conventions.

7. **Database migration**
   - If columns are missing or incorrectly typed, add an EF migration.
   - Ensure PostgreSQL column types are explicitly `jsonb`.
   - If converting from text/json/string columns, include safe migration logic where feasible.
   - Do not introduce destructive migration behavior without necessity.

8. **Testing**
   - Add tests for:
     - valid profile update persists JSON-backed fields
     - invalid tool permissions rejected with field-level errors
     - invalid working hours rejected
     - invalid thresholds/escalation/trigger payloads rejected
     - `updated_at` changes on successful update
     - JSONB mapping round-trips correctly through EF Core
   - Prefer focused unit tests for validators plus integration tests for persistence/API if test infrastructure exists.

9. **Keep future compatibility in mind**
   - Structure JSON payloads so ST-203 policy guardrails can consume them later.
   - Favor explicit property names and versionable shapes.
   - If useful, include a lightweight internal schema version field in JSON objects only if consistent with existing patterns; otherwise skip.

# Validation steps
Run and verify the following after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the workflow:
   - generate/apply migration as appropriate for the repo conventions
   - verify the `agents` table uses PostgreSQL `jsonb` for the flexible config columns

4. Manual/API verification:
   - create or update an agent profile with valid payloads for:
     - objectives
     - KPIs
     - tool permissions
     - data scopes
     - approval thresholds
     - escalation rules
     - trigger logic
     - working hours
   - confirm persistence succeeds
   - confirm `updated_at` changes

5. Negative validation checks:
   - submit malformed working hours
   - submit invalid/duplicate tool permission entries
   - submit invalid threshold values
   - submit malformed trigger definitions
   - confirm API returns field-level validation errors and does not persist changes

6. Persistence verification:
   - inspect generated SQL/migration or database schema
   - confirm values are stored as structured JSONB, not escaped JSON strings

# Risks and follow-ups
- **Risk: over-modeling too early.** Keep contracts structured but not excessively rigid; these configs are intentionally flexible.
- **Risk: mismatch between API DTOs and DB JSON shape.** Centralize mapping/serialization to avoid drift.
- **Risk: EF Core JSON mapping differences by provider/version.** Use the repo’s existing Npgsql/EF pattern and verify generated schema carefully.
- **Risk: breaking existing clients.** If current payload contracts already exist, evolve compatibly where possible.
- **Risk: validation gaps.** Ensure nested collections and conditional rules are covered, not just top-level null checks.

Follow-ups after this task:
- add audit events for profile changes
- add config history/versioning
- align JSON config schema with ST-203 policy guardrail engine
- add richer UI validation mirroring server rules
- consider reusable JSON schema documentation/examples for agent profile payloads