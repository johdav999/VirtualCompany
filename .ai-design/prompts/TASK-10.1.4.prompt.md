# Goal
Implement backlog task **TASK-10.1.4** for **ST-401 Task lifecycle and assignment** so that **task detail persists and exposes `input_payload`, `output_payload`, `rationale_summary`, and `confidence_score` where available**.

This should align with the architecture and backlog intent that the **task entity is the backbone for orchestration and audit**, and that rationale/confidence are stored as **business data**, not only transient runtime values.

# Scope
Include only the work needed to support persistence and application-level handling of these task detail fields in the existing .NET modular monolith:

- Ensure the **Task domain/entity model** includes:
  - `InputPayload`
  - `OutputPayload`
  - `RationaleSummary`
  - `ConfidenceScore`
- Ensure the **database schema / EF Core mapping** persists these fields correctly in PostgreSQL.
- Ensure **task create/update flows** can accept and store these values when provided.
- Ensure **task detail query/read models** return these values.
- Preserve **tenant scoping** and existing task lifecycle behavior.
- Keep implementation **CQRS-lite** and consistent with current project patterns.

Out of scope unless required by existing code structure:
- New UI screens beyond wiring existing task detail DTOs/models
- Full audit event fan-out
- Orchestration engine changes beyond passing through these fields if already part of task completion/update flows
- Backfilling historical data
- New business rules for confidence calculation

# Files to touch
Inspect the solution first and then update the minimal correct set of files, likely across these areas:

- `src/VirtualCompany.Domain/**`
  - Task aggregate/entity
  - Value objects or enums if relevant
- `src/VirtualCompany.Application/**`
  - Task commands/handlers
  - Task queries/handlers
  - Task DTOs/view models/contracts
  - Validation logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext mappings
  - Migrations
  - Repository/query projections if present
- `src/VirtualCompany.Api/**`
  - Request/response contracts or endpoint mappings if task APIs are defined here
- Potentially:
  - `src/VirtualCompany.Shared/**` if shared contracts are used
  - tests in corresponding test projects if present in the workspace

Before editing, locate the current implementation of:
- task entity/model
- task create command
- task update/complete command
- task detail query
- EF configuration and migrations

# Implementation plan
1. **Discover current task implementation**
   - Find the canonical task entity/aggregate and all task-related DTOs, commands, handlers, and query models.
   - Confirm whether the four fields already exist partially in some layer but are not fully wired through.
   - Identify naming conventions already used in the codebase for JSON payloads, nullable numerics, and summaries.

2. **Update the domain model**
   - Add the missing task properties if absent:
     - `InputPayload`: structured payload, likely string/JSON document/JSON node depending on existing conventions
     - `OutputPayload`
     - `RationaleSummary`
     - `ConfidenceScore`
   - Keep `ConfidenceScore` nullable.
   - Keep rationale concise and user-facing; do not introduce chain-of-thought storage.
   - If the domain uses methods instead of public setters, add/update methods for setting completion/result details in a controlled way.

3. **Update persistence mapping**
   - Configure EF Core/PostgreSQL mapping for:
     - `input_payload` as JSON/JSONB-compatible storage
     - `output_payload` as JSON/JSONB-compatible storage
     - `rationale_summary` as text or appropriate string column
     - `confidence_score` as nullable numeric compatible with architecture guidance (`numeric(5,2)` if consistent with current schema style)
   - If the table already exists without these columns, add an EF migration.
   - Ensure migration is safe and nullable for existing rows.

4. **Wire command-side flows**
   - Update task creation and any task result/completion/update commands so these fields can be supplied where appropriate.
   - Do not force them to be required on create; they are “where available”.
   - If there is a dedicated completion/result command, that is the preferred place for `output_payload`, `rationale_summary`, and `confidence_score`.
   - Preserve existing validation patterns:
     - confidence score nullable
     - if provided, validate range only if the codebase already has a convention; otherwise add a conservative validation such as `0.00` to `1.00` or document current assumption in code comments only if necessary
   - Avoid inventing new workflow semantics.

5. **Wire query/read-side flows**
   - Update task detail DTOs/view models/projections so these fields are returned.
   - Ensure list views are only changed if they already expose task detail fields or if required by existing contracts.
   - Prefer adding these fields to **detail** responses, not broad list payloads, unless already part of the model.

6. **Update API contracts**
   - If API request/response models exist separately from application contracts, add the fields there too.
   - Maintain backward compatibility where possible by making new request fields optional.
   - Ensure serialization works correctly for JSON payload fields.

7. **Add or update tests**
   Add focused tests matching existing project style:
   - persistence/mapping test or integration test proving the fields round-trip
   - command handler test proving create/update stores provided values
   - query test proving task detail returns the values
   - validation test for nullable/allowed confidence score behavior if validation exists

8. **Keep tenant and lifecycle integrity**
   - Verify no change bypasses company scoping.
   - Verify assignment/status logic remains unchanged.
   - Verify paused/archived agent assignment rules are unaffected.

9. **Document assumptions in code only where needed**
   - If payload fields are stored as JSON strings because of existing conventions, follow that convention rather than introducing a new abstraction.
   - If there is ambiguity around confidence score range, align with existing architecture/schema first.

# Validation steps
Run the relevant local validation after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo:
   - generate/apply the migration as appropriate for the existing workflow
   - verify the `tasks` table contains:
     - `input_payload`
     - `output_payload`
     - `rationale_summary`
     - `confidence_score`

4. Manually verify via tests or local API execution:
   - create a task without these fields → succeeds
   - create/update/complete a task with these fields → values persist
   - fetch task detail → values are returned
   - existing task flows still work for rows with null values

5. Confirm no tenant leakage:
   - existing tenant-scoped task queries still filter by company context

# Risks and follow-ups
- **Schema/type mismatch risk:** the codebase may already use a specific JSON representation (`string`, `JsonDocument`, `JsonElement`, custom converter). Reuse the existing pattern to avoid serialization bugs.
- **Migration drift risk:** if the database schema already contains some of these columns but code does not, reconcile carefully instead of creating duplicate/conflicting migrations.
- **Contract sprawl risk:** task models may exist in multiple layers; ensure all task detail projections/contracts are updated consistently.
- **Confidence score ambiguity:** acceptance text says “where available” but does not define scale. Prefer existing schema/usage; if none exists, keep nullable and minimally validated.
- **Audit/explainability follow-up:** later stories, especially **ST-602**, may require linking these task fields into audit and explainability views.
- **Orchestration follow-up:** **ST-502/ST-504** may later populate these fields automatically from agent execution outputs; keep the model ready without over-coupling this task to orchestration logic.