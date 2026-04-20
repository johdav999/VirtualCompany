# Goal
Implement backlog task **TASK-21.1.4 — Add deterministic seed handling and session lifecycle validation tests** for **US-21.1 Build tenant-scoped simulation state service and progression runner**.

The coding agent should add or update automated tests that verify the simulation state service and progression runner satisfy the lifecycle, determinism, and tenant/company isolation requirements without modifying existing finance APIs.

Focus on proving behavior through tests, and only make minimal production changes required to enable deterministic and testable behavior.

# Scope
In scope:

- Add automated tests for simulation lifecycle:
  - create
  - read
  - update
  - pause
  - resume
  - stop
- Add tests for progression timing:
  - when running, simulated company date advances by exactly **1 day every 10 seconds** based on server time
  - when paused, simulated time does not advance
  - when stopped, progression ends and a new start creates a clean session
- Add tests for deterministic seed handling:
  - same seed + same start date + same configuration => same sequence of simulated dates and scheduled generation decisions
- Add tests for tenant/company isolation:
  - one company’s simulation state must not affect another company’s state
- Add tests for exposed state fields:
  - current status
  - current simulated date/time
  - last progression timestamp
  - generation enabled flag
  - seed
  - active session identifier
- Introduce or refine test seams as needed:
  - injectable clock / time provider
  - deterministic random/seed abstraction if missing
  - progression runner invocation seam
- Keep changes additive and aligned with modular monolith / CQRS-lite patterns.

Out of scope unless strictly necessary to make tests pass:

- UI work
- mobile changes
- unrelated finance API changes
- broad refactors outside simulation state service/progression runner
- introducing new infrastructure beyond what is needed for deterministic tests

# Files to touch
Prioritize inspection first, then update only the relevant files you find.

Likely areas:

- `src/VirtualCompany.Application/**`
  - simulation commands/queries
  - application services
  - progression runner orchestration
- `src/VirtualCompany.Domain/**`
  - simulation state/session entities or value objects
  - seed/session lifecycle rules
- `src/VirtualCompany.Infrastructure/**`
  - persistence/repositories
  - background runner implementations
  - time provider wiring if present
- `src/VirtualCompany.Api/**`
  - only if test host/API endpoints already expose simulation APIs and need wiring for tests
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for additive backend APIs
- Any existing test projects under `tests/**` that already cover application/domain behavior

Also inspect:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

If there is already a simulation-related test suite, extend it instead of creating a parallel pattern.

# Implementation plan
1. **Discover the current simulation implementation**
   - Locate all simulation-related types, endpoints, handlers, repositories, and workers.
   - Identify:
     - simulation state aggregate/model
     - session identifier handling
     - progression runner logic
     - seed usage
     - generation decision logic
     - tenant/company scoping
     - current test patterns and test host setup
   - Confirm whether deterministic seams already exist:
     - `TimeProvider`, `ISystemClock`, or equivalent
     - random abstraction or seedable RNG

2. **Map acceptance criteria to concrete tests**
   Create a test matrix covering:
   - create/read/update state
   - pause preserves last simulated date
   - resume continues from preserved date
   - stop ends active session and prevents further progression
   - restart after stop creates clean session state with new active session identifier
   - running progression advances exactly 1 day per 10 seconds of server time
   - same seed/start/config produces same generation decisions
   - tenant/company isolation across at least two companies in same tenant or two tenant/company combinations, depending on actual model
   - state payload exposes all required fields

3. **Add deterministic test seams if missing**
   If the code currently depends on ambient time or non-seeded randomness:
   - introduce an injectable clock abstraction using existing .NET patterns if possible
   - introduce a deterministic RNG abstraction or ensure seed is passed into the existing decision logic
   - keep production defaults unchanged
   - avoid large refactors; prefer constructor injection and narrow interfaces

4. **Implement lifecycle validation tests**
   Add tests that verify:
   - simulation can be created and retrieved
   - updates persist expected fields
   - pausing changes status and freezes progression
   - resuming changes status back to running and progression continues
   - stopping clears/ends active session and blocks future progression for that session
   - starting again after stop creates a clean session state
   Assertions should explicitly validate:
   - status transitions
   - simulated date/time
   - last progression timestamp behavior
   - active session identifier behavior

5. **Implement progression timing tests**
   Using a fake clock/time provider:
   - start simulation at a known date/time
   - advance server time by 10 seconds and trigger progression
   - assert simulated date advanced by exactly 1 day
   - advance by 20/30 seconds and assert exact day increments
   - verify no drift, no partial-day increments, and no double-advance when progression is invoked repeatedly without enough elapsed time
   - verify paused state does not advance even if server time moves forward

6. **Implement deterministic seed tests**
   Add tests that run the same simulation scenario twice with:
   - same seed
   - same start date
   - same configuration
   Then assert:
   - identical sequence of simulated dates
   - identical scheduled generation decisions
   Also add a contrast test if practical:
   - different seed should produce different decision sequence, unless business rules intentionally make decisions identical for the chosen scenario

7. **Implement tenant/company isolation tests**
   Add tests with multiple scoped simulation states:
   - company A progression does not mutate company B
   - reads/updates are scoped correctly
   - progression runner only advances the targeted simulation/session
   Prefer integration-style tests if repository/API scoping is involved.

8. **Validate additive API behavior**
   If simulation APIs already exist or are being introduced in this story branch:
   - add API tests proving the simulation endpoints are additive and do not alter existing finance APIs
   - do not rewrite finance tests; just ensure simulation coverage is isolated and additive

9. **Make minimal production fixes required by failing tests**
   Only after writing tests, patch implementation issues such as:
   - non-deterministic random usage
   - missing session reset on stop
   - incorrect elapsed-time progression math
   - missing tenant/company filters
   - missing exposed fields in DTOs/contracts

10. **Keep code quality high**
   - Reuse existing test fixtures/factories
   - Name tests by business behavior
   - Keep assertions precise and deterministic
   - Avoid sleeps or real-time waiting; use fake time
   - Prefer integration tests for API/repository scoping and focused unit tests for progression math/seed logic

# Validation steps
Run the smallest relevant commands first, then the broader suite.

1. Build:
   - `dotnet build`

2. Run targeted tests for the touched test project(s), for example:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. If there are application/domain test projects involved, run those too.

4. Run the full suite before finishing:
   - `dotnet test`

5. In the final summary, report:
   - which tests were added
   - any production code changed to support determinism/testability
   - whether fake time and seeded randomness were introduced or reused
   - any acceptance criteria not fully covered and why

# Risks and follow-ups
- **Risk: no existing simulation module yet**
  - If the implementation is partial, add tests around the currently available seams and make only the minimum production additions needed to support the task.
- **Risk: ambient `DateTime.UtcNow` usage**
  - Replace only in the simulation path with injected time abstraction to avoid broad churn.
- **Risk: randomness is embedded deep in logic**
  - Introduce a narrow deterministic decision abstraction rather than refactoring unrelated code.
- **Risk: session semantics are ambiguous**
  - Treat stop as ending the active session and requiring a clean new session on next start; document any assumptions in code comments/tests.
- **Risk: tenant vs company scoping model may differ from wording**
  - Follow the actual domain model in code, but ensure isolation is validated at every owned boundary.
- **Follow-up**
  - If not already present, consider a dedicated application/domain test project for simulation progression math and deterministic scheduling logic to keep API tests lean.
  - If generation decisions are not currently observable, expose them through an internal test seam or persisted artifact rather than relying on brittle indirect assertions.