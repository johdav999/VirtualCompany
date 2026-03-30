# Goal
Implement `TASK-8.2.3` for **ST-202 — Agent operating profile management** by ensuring invalid agent operating profile configurations are rejected with **field-level validation** across the .NET application stack.

This task should add robust server-side validation for agent profile updates covering fields such as:
- objectives
- KPIs
- role brief
- tool permissions
- data scopes
- approval thresholds
- escalation rules
- trigger logic
- working hours
- status

The implementation must return validation errors in a way the web app can map to specific fields, and must preserve tenant-scoped behavior and clean architecture boundaries.

# Scope
In scope:
- Add or extend request/command validation for agent profile create/update flows in the **Application** layer.
- Enforce validation rules for both scalar and JSON-backed configuration fields.
- Return structured field-level validation failures from API endpoints.
- Ensure invalid status/config combinations are rejected before persistence.
- Add/adjust tests for validation behavior.
- Update any web-facing contracts if needed so field-level errors can be surfaced.

Out of scope:
- Full audit history/versioning of config changes.
- New UX redesigns beyond wiring existing forms to field-level errors.
- Policy guardrail runtime enforcement during tool execution (`ST-203`).
- New database schema changes unless absolutely required.
- Mobile-specific validation UX unless it already consumes the same API contracts.

Assumptions:
- Agent profile management endpoints/commands already exist or are partially implemented.
- Validation should live primarily in the Application layer, not only in controllers/UI.
- JSONB-backed fields are represented as typed DTOs/contracts or can be validated as structured objects rather than opaque strings where possible.

# Files to touch
Touch only the files needed after inspecting the existing implementation. Likely areas:

- `src/VirtualCompany.Application/...`
  - agent profile commands/handlers
  - validators
  - request/response DTOs
  - shared validation/error abstractions

- `src/VirtualCompany.Api/...`
  - agent profile endpoints/controllers
  - validation error mapping to HTTP responses

- `src/VirtualCompany.Domain/...`
  - value objects/enums/guard methods if domain invariants belong there

- `src/VirtualCompany.Web/...`
  - form models or API client error handling only if needed to surface field-level messages

- Test projects/files related to:
  - application command validation
  - API validation responses
  - possibly web form binding behavior

Before editing, inspect:
- existing agent management module structure
- current command/query patterns
- whether FluentValidation or custom validation is already used
- current API error response format
- existing enums/constants for agent status and autonomy/config fields

# Implementation plan
1. **Inspect current agent profile flow**
   - Locate the command(s) and endpoint(s) used to create/update agent operating profiles.
   - Identify the request contract shape for:
     - objectives
     - KPIs
     - role brief
     - tool permissions
     - data scopes
     - approval thresholds
     - escalation rules
     - trigger logic
     - working hours
     - status
   - Determine whether these are typed models, dictionaries, JSON strings, or domain objects.

2. **Align validation with architecture**
   - Keep validation in the **Application** layer as the primary enforcement point.
   - Add domain guards only for true invariants that should never be bypassed.
   - Avoid placing business validation solely in Blazor/UI or controllers.

3. **Define concrete validation rules**
   Implement field-level rules that are practical and deterministic. Prefer typed validation over generic “invalid config” messages.

   At minimum validate:
   - `roleBrief`
     - required or non-empty if the story expects it editable
     - max length
   - `objectives`
     - collection not null
     - no blank entries
     - reasonable max item count
     - reasonable max item length
   - `kpis`
     - collection not null
     - required KPI name/label
     - no duplicates if applicable
   - `toolPermissions`
     - only allowed action values / tool identifiers
     - no duplicate entries
   - `dataScopes`
     - required scope structure
     - no empty scope identifiers
   - `approvalThresholds`
     - numeric values non-negative
     - min/max relationships valid
   - `escalationRules`
     - valid trigger/target combinations
     - required destination/condition fields present
   - `triggerLogic`
     - valid schedule/trigger shape if enabled
   - `workingHours`
     - valid day-of-week values
     - start < end
     - timezone/format consistency if included
   - `status`
     - must be one of `active`, `paused`, `restricted`, `archived`
   - cross-field rules
     - archived agents cannot be configured in ways that imply active scheduling if that invariant already exists
     - invalid combinations should attach errors to the most relevant field(s)

4. **Use field-addressable error keys**
   Return validation errors with keys that map cleanly to form fields, for example:
   - `roleBrief`
   - `objectives[0]`
   - `kpis[1].name`
   - `toolPermissions[0].toolName`
   - `approvalThresholds.maxAmount`
   - `workingHours[2].start`
   - `status`

   If the project already uses a standard validation format, follow it exactly.

5. **Implement validators**
   - If FluentValidation is already present, create/update validators for the relevant command/request.
   - Add nested validators for complex child objects instead of one large validator.
   - Keep messages concise and user-facing.
   - Ensure null handling is explicit so malformed payloads fail predictably.

6. **Map validation failures through the API**
   - Ensure invalid requests return the project’s standard validation response, typically `400 Bad Request` or `422 Unprocessable Entity` depending on existing conventions.
   - Preserve structured field-level errors in the response body.
   - Do not allow invalid configs to reach persistence.

7. **Preserve tenant and authorization boundaries**
   - Validation should happen alongside existing tenant-scoped agent lookup and authorization checks.
   - Do not leak whether another tenant’s agent exists through validation behavior.

8. **Add tests**
   Add focused tests for:
   - valid profile update succeeds
   - invalid scalar field returns field-level error
   - invalid nested collection item returns indexed field-level error
   - invalid enum/status rejected
   - invalid cross-field combination rejected
   - API returns structured validation payload
   - persistence is not called on validation failure if testable in current architecture

9. **Keep contracts maintainable**
   - If current JSON-backed config fields are too opaque to validate well, introduce typed request DTOs for the API/application boundary without changing persistence shape unnecessarily.
   - Serialize to existing JSONB persistence models after validation.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API behavior for invalid payloads:
   - submit an agent profile update with:
     - blank objective entry
     - invalid status
     - malformed working hours range
     - negative threshold
   - confirm response contains field-level errors keyed to the offending fields

4. Manually verify valid payload:
   - submit a valid agent profile update
   - confirm persistence succeeds and `updated_at` behavior remains correct if already implemented

5. If web form wiring is touched:
   - verify validation messages appear next to the correct fields
   - verify nested/collection errors are surfaced sensibly

# Risks and follow-ups
- **Risk: JSON fields are currently opaque strings**
  - Follow-up: introduce typed contracts for config sections to improve validation quality and future maintainability.

- **Risk: inconsistent error response format across APIs**
  - Follow-up: standardize validation problem details across the API surface.

- **Risk: domain invariants vs application validation are blurred**
  - Follow-up: move non-negotiable invariants into domain types/value objects once the profile model stabilizes.

- **Risk: archived/restricted behavior may overlap with later workflow/task rules**
  - Follow-up: align with `ST-401` and `ST-203` so status-based restrictions are enforced consistently across assignment and execution.

- **Risk: acceptance criteria are implicit**
  - For this task, treat success as:
    - invalid configurations are rejected before persistence
    - errors are field-specific and consumable by the web UI
    - tests cover representative invalid cases for nested config structures