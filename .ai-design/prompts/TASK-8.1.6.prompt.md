# Goal
Implement backlog task **TASK-8.1.6 — Avatar can initially be URL/file reference only** for **ST-201 Agent template catalog and hiring flow**.

The coding agent should update the agent hiring/configuration flow so that an agent avatar is treated as a simple reference value only, with no image processing pipeline, no binary storage in the agent record, and no advanced media management. The system should support:
- an external avatar URL, and/or
- a file/object-storage reference string

This should align with the architecture note that avatars/branding assets belong in object storage, while the `agents` record stores only a reference.

Because the story has no explicit acceptance criteria for this subtask, implement the smallest coherent vertical slice that makes avatar handling explicit, validated, persisted, and surfaced consistently in the current codebase.

# Scope
In scope:
- Identify how agent creation/editing currently models avatar data.
- Ensure avatar is represented as a **reference string**, not embedded file content.
- Support initial persistence as:
  - URL string, or
  - file/storage reference string
- Add server-side validation appropriate for a lightweight v1 implementation.
- Update DTOs/commands/view models/API contracts/UI bindings involved in hiring an agent from a template.
- Keep behavior backward-compatible if an existing `avatar_url`/`AvatarUrl` field already exists.
- Update any seed/template mapping logic so avatar values copy through correctly when applicable.
- Add or update tests for validation and persistence behavior.

Out of scope:
- Building a full upload pipeline
- Image resizing, thumbnails, MIME inspection, virus scanning, or CDN integration
- Rich media library UX
- Mobile-specific avatar upload UX unless already directly impacted by shared contracts
- Storing binary image data in SQL
- Introducing a new complex asset domain unless clearly required by existing architecture

Implementation intent:
- Prefer a minimal design such as a single avatar reference field if the codebase is still early.
- If the codebase already distinguishes source types, preserve that design and only tighten it to “reference only”.

# Files to touch
Touch only the files needed after inspecting the existing implementation. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - Agent entity/value objects
  - validation rules or domain invariants
- `src/VirtualCompany.Application/**`
  - commands/handlers for hiring or creating agents from templates
  - DTOs/contracts/view models
  - validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - migrations if schema changes are required
  - repository mappings
- `src/VirtualCompany.Api/**`
  - request/response contracts
  - endpoints/controllers for agent creation/update
- `src/VirtualCompany.Web/**`
  - hire-agent form
  - agent profile/edit form if part of the same flow
  - display components for avatar preview/reference handling
- `README.md`
  - only if there is already a section documenting agent setup contracts and it needs a brief update

Potential concrete files to inspect first:
- `src/VirtualCompany.Domain/.../Agent*.cs`
- `src/VirtualCompany.Application/.../Agents/...`
- `src/VirtualCompany.Infrastructure/.../Configurations/...Agent...`
- `src/VirtualCompany.Web/.../Agents/...`
- any existing migration folder under Infrastructure

# Implementation plan
1. **Inspect current avatar modeling**
   - Find the current agent entity and all references to avatar fields.
   - Determine whether the system already uses something like:
     - `AvatarUrl`
     - `avatar_url`
     - `Avatar`
     - `ProfileImage`
   - Trace the full flow:
     - template seed data
     - create/hire command
     - persistence mapping
     - API contract
     - web form binding
     - roster/profile display

2. **Choose the minimal compatible representation**
   - If the `agents` model already has `avatar_url` / `AvatarUrl`, keep it unless there is a strong reason not to.
   - Treat that field semantically as an **avatar reference**, allowing:
     - absolute URL values
     - object-storage/file reference values
   - If naming is misleading but already established, prefer preserving schema/API compatibility and clarifying behavior in validation/comments.
   - Only introduce a new field like `AvatarReference` if the current implementation is clearly broken or absent and the refactor cost is low.

3. **Define lightweight validation rules**
   - Accept null/empty avatar reference.
   - Accept:
     - well-formed absolute URLs (`http`/`https`)
     - non-empty file/storage reference strings such as object keys or app-relative asset references
   - Reject:
     - raw base64/image blobs
     - excessively long values
     - obviously malformed URLs when the value looks like a URL
   - Keep validation pragmatic:
     - max length constraint
     - trim whitespace
     - no binary payloads
   - If using FluentValidation or similar, implement rules there.
   - If domain invariants exist, enforce the same rule set there too.

4. **Update application contracts and handlers**
   - Ensure create/hire-agent command/request includes the avatar reference field.
   - Ensure template-to-agent copy logic preserves avatar reference values.
   - Ensure update/edit flows, if already present and shared, also use the same semantics.
   - Normalize input before persistence:
     - trim
     - convert empty string to null if consistent with project conventions

5. **Update persistence only if needed**
   - If an existing text column already stores avatar URL/reference, reuse it.
   - If schema changes are needed:
     - add/rename the column carefully
     - preserve existing data
     - generate an EF Core migration
   - Keep the database type as text/varchar, not binary.
   - Do not add a separate asset table unless the current design already requires it.

6. **Update web/API behavior**
   - In the hiring flow UI, label the field clearly, e.g.:
     - “Avatar URL or file reference”
   - If the UI currently implies upload support, revise wording to avoid misleading users.
   - If there is a preview component:
     - preview only when the value is a renderable URL
     - otherwise show a fallback avatar/placeholder and optionally display the reference text only if appropriate
   - Ensure API docs/contracts reflect that the field is a reference string.

7. **Preserve architecture alignment**
   - Keep avatars as references to object storage or external URLs.
   - Do not implement direct file upload in this task.
   - Do not couple avatar handling to orchestration or agent behavior logic.

8. **Add tests**
   - Add unit/application tests covering:
     - valid absolute URL accepted
     - valid file/object reference accepted
     - empty/null accepted
     - base64/blob-like payload rejected
     - overlong value rejected
   - Add persistence/handler tests if the project already has that pattern.
   - Update any existing tests that assumed URL-only behavior.

9. **Document assumptions in code comments or task notes**
   - Briefly note that avatar support is intentionally reference-only for v1.
   - If there is no upload pipeline yet, make that explicit in UI/help text rather than silently failing.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the hire-agent flow:
   - Create an agent from a template with:
     - no avatar
     - an `https://...` avatar URL
     - a file/object reference such as `avatars/company-123/agent-abc.png`
   - Confirm the agent is created successfully and appears in the roster.

4. Verify invalid input handling:
   - Try a base64-like string or obviously invalid oversized payload.
   - Confirm field-level validation or safe API error response.

5. Verify persistence:
   - Confirm the stored agent record contains only the reference string.
   - Confirm no binary/blob data is being persisted in the agent entity/table.

6. Verify display behavior:
   - If a URL is provided and preview exists, confirm it renders.
   - If a non-URL file reference is provided, confirm the UI degrades gracefully with placeholder behavior rather than breaking.

7. If a migration was added:
   - Apply migration locally as appropriate for the repo conventions.
   - Rebuild and rerun tests after migration generation.

# Risks and follow-ups
- **Ambiguous existing field naming:** The codebase may already use `AvatarUrl`. Renaming it everywhere could create unnecessary churn. Prefer semantic clarification over broad refactoring unless clearly justified.
- **UI preview assumptions:** Existing components may assume every avatar value is a browser-loadable URL. Non-URL file references may require fallback rendering logic.
- **Validation overreach:** Be careful not to reject legitimate storage keys by enforcing URL-only validation.
- **Schema drift risk:** If the DB already has `avatar_url`, avoid unnecessary migrations unless required.
- **Future follow-up likely needed:** A later task should add a proper upload flow that stores files in object storage and writes the resulting object key/reference into the agent avatar field.
- **Potential template seed updates:** If templates include avatar defaults, seed data may need adjustment to use reference strings consistently.
- **Mobile/shared contract impact:** If shared DTOs are used by web and mobile, ensure contract changes remain backward-compatible.