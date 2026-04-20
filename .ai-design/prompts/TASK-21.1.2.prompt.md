# Goal
Implement **TASK-21.1.2 — Add simulation control APIs for start, pause, resume, stop, and state retrieval** for **US-21.1 Build tenant-scoped simulation state service and progression runner**.

Deliver additive backend functionality in the existing .NET solution that introduces a tenant/company-scoped simulation state subsystem and APIs without modifying existing finance APIs.

The implementation must satisfy these outcomes:

- A simulation can be **started, read, updated, paused, resumed, and stopped** through new backend APIs.
- While **running**, the simulated company date advances by **exactly 1 day every 10 seconds**, based on **server time**.
- While **paused**, simulated time does **not** advance and the last simulated date is preserved.
- While **stopped**, the active session ends, progression ceases, and a subsequent start creates a **clean new session**.
- State is isolated by **tenant/company**.
- Given the same **seed**, **start date**, and **configuration**, runs are deterministic for simulated dates and scheduled generation decisions.
- The service stores and exposes:
  - current status
  - current simulated date/time
  - last progression timestamp
  - generation enabled flag
  - seed
  - active session identifier

# Scope
Implement the feature across domain, application, infrastructure, and API layers using the existing modular monolith structure.

Include:

- New simulation domain model(s) and status representation
- Persistence for simulation state in PostgreSQL
- Application commands/queries for:
  - start simulation
  - get simulation state
  - update simulation settings if needed for CRUD acceptance
  - pause simulation
  - resume simulation
  - stop simulation
- Background progression runner that advances simulated date using server time
- Deterministic progression/session behavior using persisted seed/config/session state
- Tenant/company scoping on all reads/writes
- API endpoints/controllers for additive simulation operations
- Tests for core progression, pause/resume/stop semantics, tenant isolation, and determinism

Do not:

- Change existing finance APIs
- Introduce UI/mobile work
- Add unrelated workflow/generation execution beyond what is needed to persist/expose deterministic generation decision state hooks if required
- Over-engineer into microservices or external schedulers if existing background worker patterns suffice

# Files to touch
Inspect the solution first and adapt to actual conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - add simulation entities/value objects/enums
- `src/VirtualCompany.Application/`
  - add commands, queries, DTOs, interfaces, validators, handlers
- `src/VirtualCompany.Infrastructure/`
  - add EF Core/PostgreSQL persistence mappings, repositories, migrations, background runner, clock/random abstractions if needed
- `src/VirtualCompany.Api/`
  - add additive simulation controller/endpoints and DI registration
- `tests/VirtualCompany.Api.Tests/`
  - add integration/API tests
- possibly shared test projects or application test areas if present

Also inspect for existing patterns in:

- tenant/company resolution
- CQRS/MediatR usage
- EF Core DbContext and entity configurations
- background services/hosted services
- migrations strategy
- API route/versioning conventions
- authorization policies

# Implementation plan
1. **Discover existing architecture and follow local patterns**
   - Inspect:
     - API controller style
     - command/query dispatch mechanism
     - DbContext and migrations setup
     - tenant/company context resolution
     - background worker registration
   - Reuse existing abstractions for:
     - current tenant/company context
     - time provider / clock if present
     - correlation/logging patterns
   - Do not invent a parallel architecture if a standard project pattern already exists.

2. **Design the simulation state model**
   Create a tenant/company-scoped simulation aggregate or equivalent persistence model with at least:

   - `CompanyId`
   - `Status` enum/string: `NotStarted` or absent, `Running`, `Paused`, `Stopped`
   - `CurrentSimulatedAt` (date/time)
   - `LastProgressedAt` (server timestamp)
   - `GenerationEnabled`
   - `Seed`
   - `ActiveSessionId`
   - `StartedAt`
   - `PausedAt` nullable
   - `StoppedAt` nullable
   - configuration fields needed for deterministic restarts, such as:
     - `StartSimulatedAt`
     - optional config JSON / structured fields
   - concurrency token if the project uses optimistic concurrency

   Important behavior rules:

   - **Running**: effective simulated time is derived from persisted state plus elapsed server time in 10-second intervals, or advanced by the runner in exact 1-day increments.
   - **Paused**: no advancement; preserve exact last simulated date/time.
   - **Stopped**: session is ended; no further advancement.
   - **Start after stop**: create a clean new session state with a new `ActiveSessionId`.
   - State must be unique per tenant/company.

3. **Choose and implement deterministic progression semantics**
   Use a deterministic rule set:

   - Every full **10 seconds of server time** while status is `Running` advances simulated date by **exactly 1 day**
   - No partial-day advancement for incomplete intervals
   - Use UTC server timestamps consistently unless the codebase already standardizes differently
   - Persist enough state so repeated reads produce stable results and progression is not double-counted

   Recommended approach:
   - Persist:
     - `CurrentSimulatedAt`
     - `LastProgressedAt`
   - Background runner periodically:
     - loads running simulations
     - computes `elapsed = now - LastProgressedAt`
     - computes `daysToAdvance = floor(elapsed.TotalSeconds / 10)`
     - if `daysToAdvance > 0`, updates:
       - `CurrentSimulatedAt += daysToAdvance days`
       - `LastProgressedAt += daysToAdvance * 10 seconds`
   - This ensures exact interval-based progression and avoids drift.

4. **Model deterministic session/config behavior**
   To satisfy determinism acceptance criteria:
   - Persist the exact run inputs:
     - `Seed`
     - `StartSimulatedAt`
     - `GenerationEnabled`
     - any simulation config used for generation decisions
   - Ensure a new start with the same seed/start/config produces the same progression and decision sequence.
   - If generation decisions are not fully implemented in this task, at minimum:
     - define a deterministic decision service abstraction using seed + session/config + simulated date
     - persist/expose enough state to support deterministic downstream behavior
     - avoid non-deterministic `Random()` usage without a seeded abstraction

   If generation decision persistence is in scope for this task, use a seeded deterministic PRNG abstraction scoped to session and date tick.

5. **Add persistence**
   Add a new table/entity for simulation state, e.g. `company_simulations` or similar, with a unique constraint on company scope.

   Include:
   - primary key
   - `company_id`
   - status
   - current simulated timestamp
   - last progression timestamp
   - generation enabled
   - seed
   - active session id
   - start/config fields
   - created/updated timestamps

   Add indexes/constraints:
   - unique on `company_id` if only one current state row exists per company
   - or unique active-state strategy if using session history rows
   - index on status for runner queries

   Prefer a simple current-state table unless the codebase already uses history/event tables.

6. **Implement application layer commands and queries**
   Add command/query handlers for:

   - `StartSimulation`
     - if no state exists, create one
     - if stopped or not started, create a clean session
     - if paused, decide whether start is invalid or treated distinctly from resume; prefer explicit semantics:
       - `start` creates a new clean session only when no active running/paused session exists
       - return validation error/conflict if already running or paused
     - initialize:
       - status = running
       - current simulated date/time = requested start date/time
       - last progression timestamp = current server time
       - generation enabled
       - seed
       - active session id = new guid
   - `GetSimulationState`
     - return current persisted state
     - optionally compute catch-up before returning if your design requires read-time consistency, but avoid side effects in pure queries unless that is already accepted in the codebase
   - `UpdateSimulationState` or `UpdateSimulationSettings`
     - support additive update semantics needed for CRUD acceptance
     - likely limited to mutable config such as `GenerationEnabled`
     - do not allow invalid mutation of active session identity/history
   - `PauseSimulation`
     - first catch up progression to now
     - set status = paused
     - preserve current simulated date/time
     - set paused timestamp if modeled
   - `ResumeSimulation`
     - require paused state
     - set status = running
     - set `LastProgressedAt = now` so paused duration does not advance time
   - `StopSimulation`
     - optionally catch up to now first if stopping from running
     - set status = stopped
     - end active session
     - ensure no further progression occurs
     - preserve final visible state for retrieval, but a future start must create a clean new session

   Return DTOs exposing exactly the required fields.

7. **Implement API endpoints**
   Add additive endpoints under a new route area, following project conventions, for example:

   - `POST /api/companies/{companyId}/simulation/start`
   - `GET /api/companies/{companyId}/simulation`
   - `PATCH /api/companies/{companyId}/simulation`
   - `POST /api/companies/{companyId}/simulation/pause`
   - `POST /api/companies/{companyId}/simulation/resume`
   - `POST /api/companies/{companyId}/simulation/stop`

   Requirements:
   - enforce tenant/company authorization
   - do not alter existing finance routes
   - validate request payloads
   - return appropriate status codes:
     - `200 OK` for reads/actions
     - `201 Created` if local conventions prefer for first start
     - `400` for invalid requests
     - `404` or `403` per existing tenant isolation conventions
     - `409` for invalid state transitions if used in the codebase

8. **Implement the progression runner**
   Add a hosted background service in infrastructure/API host registration that:

   - runs on a short polling interval, e.g. every 1–5 seconds
   - queries running simulations
   - advances each by exact 10-second buckets
   - updates only when at least one full bucket elapsed
   - is safe under concurrent execution

   Concurrency considerations:
   - if only one app instance is assumed in dev, still code defensively
   - if the project already uses distributed locks/row versioning, reuse them
   - avoid double advancement by:
     - optimistic concurrency token
     - transactional update with current `LastProgressedAt`
     - or database locking pattern already used elsewhere

   Keep progression tenant/company isolated.

9. **Handle state transitions carefully**
   Enforce valid transitions:

   - none/not-started -> running via start
   - running -> paused via pause
   - paused -> running via resume
   - running -> stopped via stop
   - paused -> stopped via stop
   - stopped -> running via start with new clean session

   Invalid examples:
   - pause when already paused/stopped/not started
   - resume when not paused
   - start when already running
   - resume after stopped without a new start

10. **Expose required response shape**
    Ensure the API returns at least:

    - `status`
    - `currentSimulatedDateTime`
    - `lastProgressionTimestamp`
    - `generationEnabled`
    - `seed`
    - `activeSessionId`

    Include company/session metadata if helpful, but do not omit required fields.

11. **Add tests**
    Add automated tests covering:

    - start creates simulation state for a company
    - get returns current state
    - update changes allowed config fields
    - pause preserves last simulated date and halts advancement
    - resume restarts advancement from pause point
    - stop ends progression and future start creates new session id and clean state
    - running advances by exactly 1 day per 10 seconds
    - no advancement for partial intervals
    - tenant/company isolation across two companies
    - deterministic behavior for same seed/start/config
    - invalid transitions return expected errors
    - existing finance APIs remain untouched if there are regression tests nearby

    Prefer integration tests where possible, especially for API + persistence + background progression behavior.

12. **Document assumptions in code comments only where needed**
    Keep comments concise and explain:
    - exact 10-second progression rule
    - why `LastProgressedAt` is advanced in bucket increments
    - how stop/start session reset works

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify API behavior manually or via integration tests:
   - Start a simulation for company A with:
     - start date
     - seed
     - generation enabled flag
   - Fetch state and confirm:
     - status is `Running`
     - current simulated date/time equals requested start date/time initially
     - session id is present
   - Wait just over 10 seconds, fetch state again, confirm:
     - simulated date advanced by exactly 1 day
   - Wait less than 10 additional seconds, confirm:
     - no extra advancement
   - Pause simulation, wait over 10 seconds, confirm:
     - no advancement while paused
   - Resume, wait over 10 seconds, confirm:
     - advancement resumes from preserved date
   - Stop, wait over 10 seconds, confirm:
     - no further advancement
   - Start again, confirm:
     - new session id
     - clean state from new requested start date/config

4. Verify tenant isolation:
   - create/start simulations for two different companies
   - confirm progression/state changes are independent

5. Verify determinism:
   - run simulation twice with same seed/start/config in isolated clean sessions
   - confirm same sequence of simulated