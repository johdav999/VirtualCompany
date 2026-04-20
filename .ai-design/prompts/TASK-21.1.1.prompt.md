# Goal
Implement backlog task **TASK-21.1.1 — Implement simulation state persistence model and tenant/company-scoped repository** for story **US-21.1 Build tenant-scoped simulation state service and progression runner**.

Deliver the foundational backend persistence layer and repository support for simulation state so later application services, APIs, and progression workers can build on it without modifying existing finance APIs.

The implementation must satisfy these core outcomes:

- Persist simulation state per **tenant/company** with strict isolation.
- Support additive backend operations for:
  - create/start
  - read/get current state
  - update
  - pause
  - resume
  - stop/end session
- Store and expose:
  - current status
  - current simulated date/time
  - last progression timestamp
  - generation enabled flag
  - seed
  - active session identifier
- Ensure stopped sessions are ended cleanly and a future start creates a fresh session.
- Preserve deterministic inputs needed for reproducible runs.
- Prepare for server-time-based progression of exactly **1 simulated day per 10 seconds while running**, but keep this task focused on the persistence model and repository needed to support that behavior.

# Scope
In scope:

- Add a new domain model/entity for simulation state.
- Add supporting enums/value objects if appropriate for simulation status/session semantics.
- Add EF Core persistence mapping and database migration.
- Add a tenant/company-scoped repository abstraction and implementation.
- Enforce uniqueness/isolation so one company has at most one active current simulation state record as designed.
- Include fields required by acceptance criteria:
  - tenant/company identifier
  - status
  - current simulated date/time
  - last progression timestamp
  - generation enabled flag
  - seed
  - active session identifier
  - start date/config fields needed for deterministic replay support
  - created/updated timestamps
- Add tests for repository behavior and tenant/company isolation.
- Keep implementation additive and compatible with existing architecture.

Out of scope unless required by existing patterns for compilation:

- Full HTTP API/controller endpoints.
- Full application command/query handlers.
- Background progression runner logic.
- Scheduler/worker implementation for 10-second advancement.
- UI/mobile work.
- Changes to existing finance APIs.

If the codebase already has patterns for application contracts/DTOs that must be added for compilation, keep them minimal and additive.

# Files to touch
Inspect the solution structure first and then update the most appropriate files consistent with existing conventions. Likely areas:

- `src/VirtualCompany.Domain/...`
  - add simulation state entity
  - add simulation status enum/value objects
  - add repository interface if domain/application conventions place it here
- `src/VirtualCompany.Application/...`
  - add repository contract if contracts live here instead of domain
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configuration
  - repository implementation
  - DbContext updates
  - migration files
- `src/VirtualCompany.Api/...`
  - only if registration/wiring is needed for DI
- `tests/VirtualCompany.Api.Tests/...` or other relevant test project
  - repository persistence/integration tests
  - tenant/company isolation tests
  - stop/pause/resume persistence behavior tests

Also inspect:

- existing DbContext and entity configuration patterns
- migration strategy in repo
- tenant/company ownership conventions
- repository naming conventions
- timestamp/clock abstractions already in use

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Review solution structure and existing modules for:
     - tenant/company-owned entities
     - repository interfaces and implementations
     - EF Core mappings
     - migration generation/storage conventions
     - timestamp handling
   - Follow established naming and layering rather than inventing a new pattern.

2. **Design the simulation state model**
   - Create a simulation state aggregate/entity representing the current simulation state for a company.
   - Include at minimum:
     - `Id`
     - `CompanyId`
     - `Status` (`Running`, `Paused`, `Stopped`; include `NotStarted` only if existing patterns justify it, otherwise model absence as no active state or stopped state)
     - `CurrentSimulatedAt` or equivalent simulated date/time field
     - `LastProgressedAt`
     - `GenerationEnabled`
     - `Seed`
     - `ActiveSessionId`
     - `StartSimulatedAt` or `StartDate` for deterministic run metadata
     - configuration payload/fields if needed for deterministic replay support
     - `CreatedAt`
     - `UpdatedAt`
     - optional `StoppedAt` if useful and consistent
   - Prefer strongly typed enum/value object for status.
   - Ensure the model supports:
     - pause preserving current simulated date
     - resume continuing same session
     - stop ending active session
     - new start creating a clean session state with a new session identifier

3. **Model deterministic run metadata**
   - Persist enough inputs to satisfy: same seed + start date + configuration => same sequence/decisions.
   - If configuration is not yet fully modeled elsewhere, add a minimal persisted configuration field such as JSON/text column or explicit fields, based on existing project conventions.
   - Do not overbuild; store only what is necessary to preserve deterministic inputs.

4. **Add database persistence**
   - Add EF Core configuration for the simulation state entity.
   - Use proper column types for PostgreSQL and existing conventions.
   - Add indexes/constraints:
     - index on `CompanyId`
     - uniqueness strategy for one current state per company if required by design
     - index on `(CompanyId, ActiveSessionId)` if useful
   - If the platform has tenant and company separately, include both according to current schema conventions. If company is the tenant boundary in this codebase, follow that convention exactly.

5. **Implement tenant/company-scoped repository**
   - Add repository abstraction with methods such as:
     - get current state by company
     - create/start state
     - update state
     - pause
     - resume
     - stop
     - get by active session id within company
   - Repository methods must always require company scope and never allow cross-company access.
   - Ensure stop semantics clear/end the active session appropriately.
   - Ensure a new start after stop creates a fresh session state rather than mutating a stopped session in a way that breaks auditability or determinism expectations.
   - If the codebase prefers generic repositories plus query services, adapt to that pattern while preserving explicit simulation semantics.

6. **Handle concurrency safely**
   - Add optimistic concurrency if the project already uses row version/concurrency tokens.
   - At minimum, implement updates in a way that avoids accidental cross-request corruption for pause/resume/stop transitions.
   - Keep this lightweight but production-sensible.

7. **Register infrastructure**
   - Wire repository into DI in the appropriate startup/composition root.
   - Do not expose new public APIs unless required for compilation or existing module registration patterns.

8. **Add tests**
   - Add tests covering:
     - create and read simulation state for a company
     - update fields persists correctly
     - pause preserves current simulated date/time and status
     - resume changes status without resetting preserved date
     - stop ends active session and prevents it from appearing active
     - new start after stop creates a clean session with a new session identifier
     - company isolation: company A cannot read/update company B state
     - deterministic metadata persistence: seed/start/config are stored and retrieved unchanged
   - Prefer integration-style persistence tests if the repo already uses them; otherwise use the project’s standard testing approach.

9. **Migration**
   - Add a migration for the new table/schema changes.
   - Name it clearly for simulation state persistence.
   - Ensure it is compatible with PostgreSQL provider conventions used in the repo.

10. **Keep finance APIs unchanged**
   - Verify no existing finance API contracts/routes/handlers are modified.
   - Any new code must be additive.

# Validation steps
Run and verify the following after implementation:

1. Build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Validate migration compiles and DbContext is consistent.

4. Confirm repository tests prove:
   - create/read/update works
   - pause preserves simulated date
   - resume preserves session and date
   - stop ends active session
   - new start creates fresh session state
   - tenant/company isolation is enforced
   - seed/start/config metadata round-trips unchanged

5. Manually review that:
   - no existing finance APIs were changed
   - all repository entry points are company-scoped
   - status transitions are explicit and safe
   - persistence fields match acceptance criteria

# Risks and follow-ups
- **Tenant model ambiguity:** The task says tenant/company-scoped. The codebase may use `CompanyId` as the tenant boundary or may have both tenant and company identifiers. Follow existing conventions exactly and do not invent a parallel tenancy model.
- **Current-state vs session-history design:** Acceptance criteria require an active session identifier and clean new sessions after stop. If the codebase would benefit from separate current-state and history tables, note it, but keep this task minimal unless necessary. A single current-state table is acceptable if it cleanly supports stop/start semantics.
- **Deterministic configuration shape may be underspecified:** If no simulation config contract exists yet, persist a minimal configuration payload field now and document that richer typed config can be introduced in the next task.
- **Concurrency edge cases:** Pause/resume/stop/start races may need stronger protection in later tasks when the progression runner is added.
- **Progression logic not implemented here:** The exact “1 day every 10 seconds based on server time” behavior should be implemented in the next task using this persistence model and repository.
- **Audit/history may be needed later:** This task focuses on current state persistence. Session history/audit events may be a follow-up if product requirements expand.