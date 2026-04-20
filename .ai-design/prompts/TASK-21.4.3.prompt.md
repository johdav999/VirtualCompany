# Goal
Implement backlog task **TASK-21.4.3 — Persist simulation event logs, run history, anomaly reasons, and generation errors for operator review** for story **US-21.4 Add feature gating, observability, and safe-disable behavior for simulation**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:

- Adds **configuration-driven feature flags** to independently disable:
  - **simulation UI visibility**
  - **simulation backend execution**
- Ensures disabled states are **safe**, **tenant-aware**, and **non-breaking** for normal finance workflows.
- Persists **simulation run history** and **per-day event/generation logs** for operator review, including:
  - session identifier
  - status transitions
  - generated record counts per simulated day
  - injected anomalies
  - warnings
  - errors / generation failures
- Exposes recent simulation history and detailed logs in an **existing admin or finance-facing view**.
- Prevents any simulation progression or finance generation background work when simulation is fully disabled.

Use the existing architecture and conventions already present in the repo. Prefer incremental changes over broad refactors.

# Scope
In scope:

- Add or extend **feature flag/configuration model** for simulation:
  - UI visibility flag
  - backend execution flag
- Enforce feature flags in:
  - Blazor UI rendering
  - API endpoints / admin actions
  - background jobs / workers
- Add persistence for simulation observability/business audit data:
  - run/session history
  - status transitions
  - per-day generation logs
  - anomaly reasons
  - warnings/errors
- Add application query surface for operators to retrieve:
  - recent simulation runs by tenant/company
  - per-run or per-day detailed logs
- Add/update finance/admin UI to display this information.
- Ensure disabled backend returns a **safe disabled response**, not a crash or generic 500.
- Ensure standard finance pages and APIs still load/work when simulation is disabled.
- Add tests covering flag behavior, persistence, retrieval, and non-regression.

Out of scope unless required by existing code patterns:

- Rebuilding the simulation subsystem from scratch
- Introducing a new feature flag platform/service
- Large UI redesigns
- New standalone observability infrastructure outside the app database
- Mobile app changes unless the existing shared UI/components require a compile fix

# Files to touch
Inspect the solution first and then update the most relevant files. Expected areas include:

- **API**
  - `src/VirtualCompany.Api/**`
  - simulation/finance controllers or minimal API endpoint registrations
  - DI/configuration registration
- **Application**
  - `src/VirtualCompany.Application/**`
  - commands/queries/handlers for simulation history persistence and retrieval
  - feature flag access abstractions
  - safe disabled response contracts
- **Domain**
  - `src/VirtualCompany.Domain/**`
  - simulation run/log entities, enums, value objects, domain rules
- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL persistence mappings
  - repositories
  - background worker gating
  - configuration binding
- **Web**
  - `src/VirtualCompany.Web/**`
  - finance/admin pages/components
  - conditional rendering for simulation controls/status panels
  - operator history/log views
- **Shared**
  - `src/VirtualCompany.Shared/**`
  - DTOs/view models/contracts if shared across API/Web
- **Tests**
  - `tests/VirtualCompany.Api.Tests/**`
  - add integration/unit coverage for acceptance criteria
- **Docs / config**
  - `README.md` or feature/config docs if needed
  - appsettings files if simulation flags are configured there
  - migration files / SQL scripts in the project’s current migration location

Also inspect:
- `docs/postgresql-migrations-archive/README.md`
to understand migration conventions before adding schema changes.

# Implementation plan
1. **Discover existing simulation implementation**
   - Search the repo for:
     - `simulation`
     - `finance generation`
     - `session`
     - `anomaly`
     - `background worker`
     - `feature flag`
   - Identify:
     - current simulation domain model
     - APIs/admin actions that start/control simulation
     - UI components/pages that render simulation controls/status
     - background jobs that progress simulation or generate finance data
     - current persistence for simulation state, if any
   - Do not assume names; align with actual code structure.

2. **Add configuration-driven feature flags**
   - Introduce or extend a typed options class, e.g. `SimulationFeatureOptions`, with independent booleans such as:
     - `UiVisible`
     - `BackendExecutionEnabled`
   - Bind from configuration and register in DI.
   - If there is an existing feature flag abstraction, use it instead of inventing a parallel pattern.
   - Define clear semantics:
     - `UiVisible = false` → no simulation controls/status panels rendered anywhere
     - `BackendExecutionEnabled = false` → APIs/admin actions return safe disabled response; no session starts
     - both false → fully disabled; no background progression/generation jobs run

3. **Enforce UI visibility gating**
   - Update all relevant Blazor finance/admin pages/components so that when UI visibility is disabled:
     - simulation controls are not rendered
     - simulation status panels are not rendered
     - no broken placeholders or null-reference behavior occurs
   - Search for all simulation-related components and entry points to ensure there are no stray renders.
   - Keep standard finance page content intact.

4. **Enforce backend safe-disable behavior**
   - Update simulation control APIs/admin actions to check backend execution flag before any work starts.
   - Return a safe, explicit disabled response using existing API response conventions.
   - Avoid 500s or misleading success responses.
   - Ensure no simulation session is created/persisted when backend execution is disabled.
   - If there are command handlers or services below the controller layer, enforce the guard there too so non-HTTP callers are also protected.

5. **Stop background work when fully disabled**
   - Identify all scheduled/background jobs related to:
     - simulation progression
     - finance generation for simulation
     - retry/recovery loops for simulation sessions
   - Gate them so that when simulation is fully disabled, they do not run for any tenant/company.
   - Prefer early exit with structured logs.
   - Ensure this does not affect standard non-simulation finance jobs/workflows.
   - If there is ambiguity between “simulation-only generation” and “real finance generation,” preserve normal finance behavior and only stop simulation-related work.

6. **Design persistence for operator review**
   - Add/extend business persistence tables/entities for simulation observability, likely including:
     - `simulation_runs`
       - id
       - company_id
       - session_identifier
       - status
       - started_at / completed_at
       - current_simulated_day
       - total_simulated_days
       - summary counts
       - created_by / trigger source if available
     - `simulation_run_events` or `simulation_day_logs`
       - id
       - simulation_run_id
       - company_id
       - simulated_day
       - event type / status transition
       - generated record counts
       - anomaly code/reason/details
       - warning message
       - error message / stack-safe details
       - timestamp
       - metadata jsonb
   - Keep tenant/company scoping explicit on all tenant-owned records.
   - Use business/audit persistence, not only technical logs.
   - Prefer enums/constants for event/status types.

7. **Capture required run history details**
   - Ensure the system records:
     - session identifier
     - status transitions
     - generated record counts per simulated day
     - injected anomalies
     - warnings
     - errors / generation failures
   - Hook persistence into the actual simulation lifecycle:
     - session start
     - day progression
     - generation completion
     - anomaly injection
     - warning conditions
     - failures
     - completion/cancellation/disable rejection
   - If there is already a simulation aggregate, integrate there rather than duplicating state.

8. **Expose retrieval queries**
   - Add application queries and API endpoints for operators to retrieve:
     - recent simulation history for a company
     - detailed per-day/per-event logs for a selected run
   - Reuse existing admin/finance authorization and tenant scoping patterns.
   - Keep responses paginated or bounded if existing conventions support that.
   - Include enough detail for operator review without exposing raw internal exception dumps or chain-of-thought-like content.

9. **Add operator-facing UI**
   - In an existing admin or finance-facing view, add:
     - recent simulation run history list/table
     - drill-down or expandable detail for per-day generation logs
     - anomaly reasons, warnings, and errors
   - Keep the UI simple and consistent with existing styling/components.
   - Ensure the view itself respects UI visibility rules if simulation UI is disabled:
     - if acceptance requires no simulation status panels anywhere, do not render simulation-specific panels when hidden
     - if operator review must still be accessible in an existing admin/finance view, implement it in a way that matches the product intent and existing patterns; if needed, treat operator review as an admin-only history section that is also hidden when UI visibility is false unless the codebase already distinguishes operator-only diagnostics
   - If acceptance criteria conflict in implementation details, choose the interpretation that strictly satisfies:
     - no simulation controls/status panels rendered anywhere when UI hidden
     - operators can retrieve recent history/logs from an existing admin or finance-facing view

10. **Preserve finance non-regression**
    - Verify finance pages, finance workflows, and finance APIs continue to work when:
      - UI hidden only
      - backend disabled only
      - fully disabled
    - Avoid coupling page load or finance API composition to simulation-only services that may now be disabled.

11. **Database migration**
    - Add the required PostgreSQL/EF migration using the repo’s established migration approach.
    - Include indexes appropriate for:
      - `company_id`
      - recent run retrieval by time
      - run detail lookup by run/session id
    - Keep schema names and naming conventions consistent with the project.

12. **Testing**
    - Add/extend tests for:
      - UI visibility disabled → simulation controls/status not rendered
      - backend execution disabled → control APIs/admin actions return safe disabled response
      - backend disabled → no session starts
      - fully disabled → no simulation background jobs run
      - run history persistence captures required fields
      - operator retrieval returns recent history and per-day logs scoped by company
      - finance pages/APIs still work when simulation disabled
    - Prefer existing test patterns in `tests/VirtualCompany.Api.Tests`.

13. **Implementation constraints**
    - Keep changes cohesive and minimal.
    - Do not break tenant isolation.
    - Do not expose sensitive raw exception internals to end users.
    - Use structured application/domain models rather than ad hoc JSON blobs except where metadata JSONB is already the project convention.
    - Follow existing naming, DI, CQRS-lite, and repository conventions.

# Validation steps
Run and report the results of the relevant validation steps you can execute locally:

1. Restore/build:
   - `dotnet build`

2. Test suite:
   - `dotnet test`

3. If migrations are part of the normal workflow, generate/apply/validate them according to the repo convention.

4. Manually verify code paths by reasoning and, if runnable, by tests for these scenarios:
   - **UI hidden, backend enabled**
     - finance pages load
     - no simulation controls/status panels rendered anywhere
   - **UI visible, backend disabled**
     - simulation UI may render
     - start/control actions return safe disabled response
     - no session starts
   - **fully disabled**
     - no simulation background progression/generation jobs run
     - finance pages/APIs still work
   - **enabled**
     - simulation run persists history/events
     - operator can retrieve recent runs and per-day logs
     - anomalies/warnings/errors are visible in operator review

5. Include in your final implementation summary:
   - files changed
   - schema changes
   - config keys added
   - acceptance criteria mapping
   - any unresolved ambiguity or follow-up recommendation

# Risks and follow-ups
- **Acceptance ambiguity:** “no simulation status panels rendered anywhere” may conflict with “operators can retrieve recent simulation history from an existing admin or finance-facing view.” Resolve conservatively and document the chosen interpretation.
- **Existing simulation architecture unknown:** there may already be partial persistence or feature gating. Reuse and extend rather than duplicate.
- **Background job scope risk:** be careful not to disable standard finance generation/workflows that are not simulation-specific.
- **Migration location risk:** confirm the active migration mechanism before adding schema changes because the repo includes a migrations archive doc.
- **UI coverage risk:** simulation controls/status may appear in multiple pages/components; search comprehensively.
- **Safe response consistency:** use existing API error/disabled response patterns so clients remain stable.
- **Follow-up suggestion:** if not already present, consider a dedicated operator audit/history page with filtering and pagination once the core persistence/query path is in place.