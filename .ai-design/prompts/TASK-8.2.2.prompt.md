# Goal
Implement backlog task **TASK-8.2.2** for **ST-202 Agent operating profile management** so that **agent profile changes are persisted, update the agent `updated_at` timestamp, and are used by subsequent orchestration runs**.

This task should ensure the system behaves correctly end-to-end across:
- agent profile update command/API flow
- persistence in PostgreSQL via the existing infrastructure patterns
- timestamp mutation on every meaningful profile change
- orchestration/runtime agent resolution using the latest persisted configuration rather than stale in-memory or hardcoded values
- automated tests covering persistence and orchestration-read-after-write behavior

No explicit acceptance criteria were provided for the task itself, so derive implementation expectations from the story acceptance criteria:
- profile edits persist correctly
- `updated_at` changes when profile configuration changes
- later orchestration runs reflect the new profile values
- invalid configurations remain rejected with field-level validation
- status rules remain respected

# Scope
In scope:
- Review the current agent profile domain model, update command/handler, repository, EF mapping, and orchestration agent-resolution path.
- Ensure profile updates mutate `updated_at` consistently.
- Ensure orchestration fetches current persisted agent configuration for each run or otherwise invalidates stale cached state.
- Add or update tests proving:
  - persisted values are saved
  - `updated_at` changes after update
  - a subsequent orchestration run uses the updated role brief/objectives/tools/scopes/status as applicable
- Preserve tenant scoping and existing validation/authorization patterns.

Out of scope:
- Full config history/audit timeline beyond what already exists
- New UI redesigns beyond any minimal wiring needed
- Broad caching architecture changes unless required to prevent stale orchestration reads
- New story work for ST-203, ST-204, or unrelated orchestration features

# Files to touch
Inspect and update the relevant existing files in these areas. Prefer modifying existing implementations over introducing parallel patterns.

Likely targets:
- `src/VirtualCompany.Domain/**`
  - agent aggregate/entity/value objects
  - domain validation or update methods
- `src/VirtualCompany.Application/**`
  - agent profile update command/query handlers
  - DTOs/contracts for agent profile editing
  - orchestration services that resolve/load agent configuration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - repositories
  - persistence mappings for `updated_at`
- `src/VirtualCompany.Api/**`
  - controller/endpoints if update flow is exposed here
- `src/VirtualCompany.Web/**`
  - only if needed for request contract alignment
- tests under the corresponding test projects if present in the solution

Also inspect:
- `README.md`
- `VirtualCompany.sln`

If there is an existing orchestration subsystem implementation, prioritize files related to:
- agent registry
- prompt builder inputs
- orchestration context loading
- runtime agent resolution

# Implementation plan
1. **Discover current implementation**
   - Find the agent entity/model and identify how profile fields are represented:
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
   - Find the current update flow for ST-202:
     - command/request DTO
     - validator
     - handler/service
     - repository save path
   - Find how orchestration currently resolves an agent for execution:
     - direct DB query
     - repository lookup
     - cached registry
     - static config/template fallback

2. **Make `updated_at` reliable on profile changes**
   - Ensure the agent entity has a single authoritative way to apply profile updates.
   - If not already present, add/update a domain method such as `UpdateOperatingProfile(...)` and/or `SetStatus(...)` that:
     - applies validated values
     - updates `UpdatedAt` to current UTC time when a meaningful change occurs
   - Avoid scattering timestamp mutation across controllers/handlers.
   - Preserve `CreatedAt`.
   - If the project uses a base auditable entity or EF save interceptor for timestamps, align with the existing pattern instead of duplicating logic.
   - Ensure JSON-backed config fields are treated as modified when changed.

3. **Preserve validation behavior**
   - Keep or add field-level validation in the application layer for invalid profile payloads.
   - Ensure invalid configs do not persist partial updates.
   - Reuse existing validators if present; extend only where needed.
   - Do not weaken status constraints, especially around archived/restricted behavior.

4. **Ensure persistence writes the updated profile**
   - Verify EF mappings for JSON/JSONB-backed fields are correct and changes are tracked.
   - If mutable collection/reference types are stored as JSON, ensure EF value comparers or owned/value conversion patterns correctly detect modifications.
   - Confirm repository/unit-of-work save path persists both changed config fields and `updated_at`.

5. **Ensure subsequent orchestration runs use latest persisted config**
   - Review orchestration entry points and agent resolution.
   - Remove or fix any stale configuration behavior, for example:
     - long-lived cached agent definitions without invalidation
     - template-derived runtime config overriding company-owned agent config
     - singleton-held agent profile snapshots
   - Preferred behavior: each orchestration run resolves the current agent configuration from the persistence-backed source for the tenant-scoped agent.
   - If caching exists and is necessary, add safe invalidation on profile update keyed by tenant + agent id.
   - Ensure prompt/context/tool policy inputs are built from the updated persisted agent profile.

6. **Add automated tests**
   - Add/update unit and integration tests covering the task intent.
   - Minimum expected coverage:
     - **Profile update persistence test**
       - update an agent profile
       - reload from persistence
       - assert changed fields are saved
     - **Timestamp update test**
       - capture original `updated_at`
       - perform profile update
       - assert `updated_at` is greater than original
     - **Subsequent orchestration uses latest config test**
       - create agent with initial role brief/objective/tool scope
       - run or simulate orchestration resolution once
       - update profile
       - run orchestration again
       - assert second run uses updated values
   - If full orchestration integration is expensive, test the service responsible for runtime agent resolution/prompt input assembly.
   - Keep tests deterministic; inject clock abstraction if the codebase already supports it.

7. **Keep tenant isolation intact**
   - Ensure update and orchestration reads remain scoped by `company_id`.
   - Add a regression test if tenant scoping is easy to cover in the same area.

8. **Document any assumptions in code comments only where necessary**
   - Avoid excessive comments.
   - Prefer clear naming and focused tests.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the relevant subset first, then full suite.

4. Manually verify code paths:
   - update an agent operating profile through the application/API path
   - confirm persisted record has changed values and newer `updated_at`
   - trigger a subsequent orchestration run for that same agent
   - confirm runtime resolution/prompt input uses the updated profile values

5. Check for common regressions:
   - unchanged updates do not create incorrect timestamp churn unless existing conventions require it
   - archived/paused/restricted status behavior is not broken
   - JSONB fields are actually detected as modified by EF
   - no stale cache remains in orchestration path

# Risks and follow-ups
- **EF JSON change tracking risk:** if profile fields are stored as mutable JSON-backed objects/collections, EF may miss updates without proper value comparers or replacement semantics.
- **Timestamp consistency risk:** if timestamps are managed in multiple layers, behavior may become inconsistent. Prefer one established pattern.
- **Stale orchestration cache risk:** if agent registry/orchestration uses singleton or memory cache snapshots, subsequent runs may ignore updates until restart unless invalidation is added.
- **Testability risk:** orchestration may be tightly coupled to external providers; in that case, test the agent-resolution/prompt-construction boundary rather than full LLM execution.
- **Concurrency risk:** simultaneous profile edits could overwrite each other if no concurrency handling exists. Do not expand scope unless the code already supports row versioning, but note it for future hardening.
- **Follow-up suggestion:** if not already present, a later task should add explicit audit events for profile changes and possibly optimistic concurrency protection for admin edits.