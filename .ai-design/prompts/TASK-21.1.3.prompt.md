# Goal
Implement backlog task **TASK-21.1.3**: create a **background progression runner** that advances a tenant/company simulation by **exactly 1 simulated day every 10 seconds** while the simulation session is in **Running** state, using **server time** as the source of truth.

This work must fit the existing **.NET modular monolith** architecture, preserve **tenant/company isolation**, and support deterministic progression behavior needed by **US-21.1 Build tenant-scoped simulation state service and progression runner**.

# Scope
Implement only what is necessary to deliver the progression runner and its supporting backend behavior for simulation state progression.

Include:
- Background worker/service that periodically scans active simulation sessions and progresses them.
- Progression logic based on persisted timestamps, not in-memory timers.
- Correct handling of statuses:
  - `Running`: advance 1 day per 10 seconds elapsed.
  - `Paused`: no advancement.
  - `Stopped`: no advancement; active session considered ended.
- Persistence of:
  - current status
  - current simulated date/time
  - last progression timestamp
  - generation enabled flag
  - seed
  - active session identifier
- Tenant/company isolation in all reads/writes.
- Deterministic progression calculations based on persisted state and server time.
- Additive APIs only if needed to support runner integration; do not modify existing finance APIs.
- Tests covering progression, pause/resume, stop/reset semantics, and tenant isolation.

Do not include:
- UI work.
- Mobile work.
- Changes to existing finance APIs.
- Full scheduled generation implementation beyond preserving deterministic hooks/state needed for future generation decisions.
- Unrelated workflow engine refactors.

# Files to touch
Inspect the solution first and adapt to actual project structure. Likely areas:

- `src/VirtualCompany.Domain/**`
  - simulation state/session entities, enums, value objects
- `src/VirtualCompany.Application/**`
  - commands/queries/services for simulation state
  - progression runner service interface and logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence/configuration/repositories
  - hosted background service implementation
  - migrations
  - time abstraction if not already present
- `src/VirtualCompany.Api/**`
  - additive endpoints/controllers if missing for create/read/update/pause/resume/stop
  - DI registration
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- Possibly:
  - `README.md` or relevant docs if simulation service docs exist
  - migration files under the project’s actual migrations folder

Before coding, locate:
- existing tenant/company scoping patterns
- background worker patterns
- time provider abstraction
- repository/unit-of-work conventions
- API style for additive endpoints
- migration approach in this repo

# Implementation plan
1. **Discover existing simulation-related code**
   - Search for any existing simulation state, session, runner, or finance simulation code.
   - Reuse existing patterns if TASK-21.1.1 / TASK-21.1.2 groundwork already exists.
   - Identify whether simulation state is modeled as one current state row plus session history, or another pattern.

2. **Model simulation state and session semantics**
   - Ensure there is a tenant/company-scoped simulation aggregate with at least:
     - `TenantId`/`CompanyId`
     - `Status` (`Running`, `Paused`, `Stopped`)
     - `CurrentSimulatedAt`
     - `LastProgressedAt`
     - `GenerationEnabled`
     - `Seed`
     - `ActiveSessionId`
     - `StartDate`
     - optional config payload if already defined
   - Ensure stop semantics end the active session and require a new clean session on next start.
   - If not already present, separate durable session identity/history from mutable current state.

3. **Implement deterministic progression calculation**
   - Use server time via a shared abstraction (`TimeProvider`, `ISystemClock`, etc.), not `DateTime.UtcNow` directly in domain logic.
   - Progression rule:
     - while status is `Running`
     - compute elapsed whole 10-second intervals since `LastProgressedAt`
     - advance simulated date by exactly that many days
     - update `LastProgressedAt` by the same number of consumed intervals
   - Preserve remainder time less than 10 seconds by not over-advancing.
   - Make the logic idempotent and safe to run repeatedly.

4. **Implement background progression runner**
   - Add a hosted background service in Infrastructure.
   - Poll on a short cadence, e.g. every 1 second or similar reasonable interval.
   - On each cycle:
     - fetch running simulation states in bounded batches
     - for each, apply progression logic
     - persist only when advancement occurred
   - Ensure the runner does not rely on long-lived in-memory per-session timers.
   - Prefer database-driven progression so restarts do not lose correctness.

5. **Handle concurrency and multi-instance safety**
   - Assume future horizontal scaling.
   - Prevent double progression if multiple app instances run workers.
   - Use one of the repo’s established patterns:
     - optimistic concurrency token/version column
     - row locking / `FOR UPDATE SKIP LOCKED`
     - distributed lock if already used
   - Keep progression updates atomic per simulation state/session.
   - If a conflict occurs, retry minimally or skip safely.

6. **Enforce status behavior**
   - `Running`: progression occurs.
   - `Paused`: no progression; preserve current simulated date and last meaningful progression state.
   - `Stopped`: no progression; active session ended.
   - `Resume`: continue from preserved simulated date using a fresh `LastProgressedAt = now` baseline so paused elapsed wall time does not count.
   - `Start after stop`: create a clean session state with new `ActiveSessionId`, reset simulated date from configured start date, and deterministic seed/config preserved per request.

7. **Expose/add additive backend APIs if missing**
   - Ensure backend supports:
     - create/start simulation state/session
     - read current state
     - update config if applicable
     - pause
     - resume
     - stop
   - Keep endpoints additive and isolated from finance APIs.
   - Follow existing API conventions for tenant/company authorization and route design.

8. **Preserve deterministic hooks for future generation decisions**
   - Ensure session state contains seed and session identifier.
   - If generation decision plumbing already exists, make sure progression uses deterministic inputs only:
     - seed
     - start date
     - config
     - session id / progression index if already modeled
   - Do not introduce nondeterministic behavior tied to worker timing beyond server-time interval calculation.

9. **Persistence and migration**
   - Add/update EF Core entity configuration and migration(s).
   - Add indexes supporting:
     - lookup by company/tenant
     - lookup of running sessions
     - uniqueness of active state per tenant/company if required
   - If session history exists, ensure stopped sessions are retained and active session uniqueness is enforced.

10. **Testing**
   - Add unit tests for progression math:
     - 0–9 seconds elapsed => 0 day advance
     - 10 seconds => +1 day
     - 25 seconds => +2 days, 5 seconds remainder preserved via `LastProgressedAt`
     - paused => no advance
     - stopped => no advance
     - resume => paused duration does not advance time
   - Add integration/API tests for:
     - tenant/company isolation
     - stop ends active session
     - new start after stop creates clean session with new session id
     - repeated runner execution is idempotent
     - deterministic same-seed/start/config runs produce same simulated date sequence
   - If background services are hard to test directly, test the progression application service separately and keep the hosted service thin.

11. **Registration and operational behavior**
   - Register the hosted service in DI.
   - Ensure logging includes tenant/company/session identifiers where appropriate.
   - Keep logs operational, not noisy.
   - Fail one simulation safely without crashing the whole worker loop.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the repo’s normal way.

4. Manually validate progression behavior with controlled time abstraction or integration tests:
   - Start a simulation in `Running` state with known start date.
   - Set `LastProgressedAt` to 10 seconds in the past and run progression once.
   - Confirm simulated date advanced by exactly 1 day.
   - Set 30 seconds elapsed and confirm +3 days.
   - Set `Paused` and confirm no change.
   - Resume and confirm elapsed paused wall time is ignored.
   - Stop and confirm no further progression occurs.
   - Start again and confirm a new clean session with a new active session id.

5. Validate tenant isolation:
   - Create simulations for two different companies/tenants.
   - Progress one and confirm the other is unaffected.
   - Confirm APIs cannot read/update another tenant’s simulation state.

6. Validate idempotency/concurrency:
   - Run progression twice without additional elapsed interval and confirm no extra day is added.
   - If possible, simulate concurrent updates and confirm no double advancement.

# Risks and follow-ups
- **Concurrency risk:** multiple worker instances may double-advance unless locking/concurrency control is implemented correctly.
- **Time-source risk:** direct use of `DateTime.UtcNow` in multiple places can make tests flaky and behavior inconsistent; centralize time access.
- **Pause/resume semantics risk:** if `LastProgressedAt` is not reset correctly on resume, paused wall time may incorrectly advance simulation.
- **Stop/start semantics risk:** reusing old session state after stop would violate the clean-session requirement.
- **Determinism risk:** future generation decisions must not depend on worker polling cadence; progression should be based on persisted elapsed intervals only.
- **Performance risk:** scanning all running sessions every second may need batching/indexing; implement efficient queries and keep updates minimal.
- **Follow-up:** if not already present, add explicit session history/audit records for simulation lifecycle transitions.
- **Follow-up:** consider extracting progression interval constants into configuration with defaults, but keep acceptance behavior at exactly 10 seconds per day unless product explicitly asks for configurability.