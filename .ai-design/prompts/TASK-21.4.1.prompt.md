# Goal
Implement `TASK-21.4.1` for **US-21.4 Add feature gating, observability, and safe-disable behavior for simulation** in the existing .NET modular monolith so that simulation can be controlled by **configuration-driven, environment-aware feature flags** with separate gates for:

- **UI visibility**
- **Backend execution**
- **Background processing**

Also add durable **simulation run history and per-day generation logs**, expose them in an existing admin or finance-facing view, and ensure disabling simulation does **not** break normal finance experiences or APIs.

# Scope
Implement the backlog task end-to-end across configuration, backend, workers, persistence, API behavior, and web UI.

In scope:

- Add environment-aware configuration for simulation feature flags, with independent switches for:
  - simulation UI visibility
  - simulation backend execution
  - simulation background progression / finance generation jobs
- Ensure UI hides all simulation controls and simulation status panels when UI visibility is disabled.
- Ensure backend APIs/admin actions return a safe disabled response when backend execution is disabled.
- Prevent any simulation session from starting when backend execution is disabled.
- Ensure no simulation background progression or finance generation jobs run when simulation is fully disabled.
- Persist simulation run history with:
  - session identifier
  - status transitions
  - generated record counts per simulated day
  - injected anomalies
  - warnings
  - errors
- Expose recent simulation history and per-day generation logs in an existing admin or finance-facing view.
- Preserve normal finance page loading and finance workflows when simulation is disabled.
- Add/extend tests for config binding, gating behavior, worker suppression, persistence, and UI rendering.

Out of scope unless required by existing code patterns:

- New standalone admin module
- Mobile-specific simulation UX
- Reworking unrelated finance workflows
- Broad observability platform changes beyond what is needed for this task

# Files to touch
Inspect the solution first and update the actual files that match existing conventions. Expect to touch files in these areas:

- **API / host**
  - `src/VirtualCompany.Api/**`
  - app configuration files such as:
    - `src/VirtualCompany.Api/appsettings.json`
    - `src/VirtualCompany.Api/appsettings.Development.json`
    - environment-specific config if present
  - DI / options registration / endpoint mapping / controllers

- **Application**
  - `src/VirtualCompany.Application/**`
  - simulation commands, queries, handlers, DTOs, feature gate service abstractions

- **Domain**
  - `src/VirtualCompany.Domain/**`
  - simulation entities/value objects/enums for run history and daily logs

- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence, repositories, worker/job gating, configuration-backed feature flag provider

- **Web**
  - `src/VirtualCompany.Web/**`
  - finance/admin pages, components, nav/menu entries, simulation controls/status panels

- **Shared**
  - `src/VirtualCompany.Shared/**`
  - shared contracts/view models if used by both API and Web

- **Tests**
  - `tests/VirtualCompany.Api.Tests/**`
  - any existing application/web/integration test projects if present

Also add a migration if the project uses EF Core migrations in-source. If migrations are archived elsewhere, follow the repo’s current migration workflow rather than inventing a new one.

# Implementation plan
1. **Discover existing simulation implementation**
   - Find all current simulation-related code paths:
     - UI pages/components
     - API endpoints/controllers
     - command/query handlers
     - background workers/jobs
     - finance generation logic
     - admin actions
     - persistence models/tables
   - Identify the existing finance-facing or admin-facing view where simulation history should be surfaced.
   - Identify current configuration patterns:
     - `IOptions<>`
     - feature management library
     - custom settings classes
     - environment-specific appsettings overrides

2. **Add environment-aware feature flag configuration**
   - Introduce a strongly typed options class, e.g. `SimulationFeatureOptions`, with explicit booleans such as:
     - `UiVisible`
     - `BackendExecutionEnabled`
     - `BackgroundJobsEnabled`
   - If the codebase already has a feature flag abstraction, integrate with it instead of creating a parallel system.
   - Bind options from configuration and support environment-specific overrides through standard ASP.NET Core configuration layering.
   - Register options validation so missing/invalid config fails fast where appropriate.

3. **Create a single simulation feature gate service**
   - Add an application-facing abstraction, e.g. `ISimulationFeatureGate`, that centralizes decisions:
     - whether simulation UI should be shown
     - whether backend execution is allowed
     - whether background jobs may run
     - whether simulation is fully disabled
   - Ensure all callers use this service rather than reading config directly.
   - Include safe helper methods for disabled responses/messages to keep behavior consistent.

4. **Gate simulation UI visibility**
   - Update all Blazor pages/components/navigation/status panels so that when UI visibility is disabled:
     - simulation controls are not rendered
     - simulation status panels are not rendered
     - simulation-related nav/menu links are hidden
   - Search broadly to ensure there are no stray simulation widgets anywhere in the app.
   - Prefer not rendering over disabled buttons if the acceptance criteria says “not rendered anywhere in the app.”
   - Keep standard finance pages fully functional when simulation UI is hidden.

5. **Gate backend execution and admin actions**
   - Update simulation control APIs and admin actions so that when backend execution is disabled:
     - they do not start sessions
     - they do not mutate simulation state
     - they return a safe disabled response
   - Reuse existing API response conventions if present; otherwise return a clear non-breaking response shape/status aligned with current API style.
   - Ensure any command handlers or service methods that can start/progress simulation are also guarded server-side, not just controllers.
   - Add audit/log entries if the codebase already records denied/blocked operational actions.

6. **Suppress background progression and finance generation jobs**
   - Identify all workers/schedulers/background jobs that progress simulation or generate finance records for simulation.
   - Gate them through `ISimulationFeatureGate`.
   - When simulation is fully disabled, ensure no simulation progression or finance generation job runs for any tenant/company.
   - If jobs are multi-purpose, suppress only the simulation-specific branch without breaking unrelated finance processing.
   - Add structured logs indicating jobs were skipped due to feature disablement.

7. **Persist simulation run history and per-day generation logs**
   - Add or extend persistence for simulation history with fields covering:
     - tenant/company identifier
     - session identifier
     - current/final status
     - status transition history
     - generated record counts per simulated day
     - injected anomalies
     - warnings
     - errors
     - timestamps
   - If there is already a simulation session entity, extend it rather than duplicating concepts.
   - Model per-day generation logs as a child table/entity if needed.
   - Keep tenant scoping explicit with `company_id`.
   - Add indexes for recent-history retrieval by company and created date.

8. **Expose history in an existing admin or finance-facing view**
   - Add a query + UI to retrieve recent simulation history and per-day generation logs for a tenant/company.
   - Reuse an existing finance/admin page if possible, adding a section/tab/panel rather than creating a disconnected experience.
   - Ensure this history view itself respects the UI visibility flag if simulation UI is meant to disappear entirely; if operators still need access while controls are hidden, align behavior with existing product conventions and document the choice in code comments/tests.
   - Show warnings/errors/anomalies in a concise operator-friendly format.

9. **Preserve finance stability**
   - Verify finance pages, finance APIs, and normal workflows do not assume simulation is enabled.
   - Replace any hard dependency on simulation state with null-safe or feature-aware behavior.
   - Ensure page loads and API responses still succeed when simulation is disabled.

10. **Testing**
   - Add tests for:
     - configuration binding and environment override behavior
     - UI hidden rendering behavior
     - backend disabled response behavior
     - prevention of session start when backend execution is disabled
     - worker/job suppression when fully disabled
     - persistence and retrieval of run history + per-day logs
     - finance pages/APIs still working when simulation is disabled
   - Prefer targeted unit/integration tests following existing repo patterns.

11. **Documentation/comments**
   - Add concise comments only where the gating behavior is non-obvious.
   - If the repo has operational docs or README config sections, document the new simulation feature flags and expected behavior.

# Validation steps
Run and verify using the repo’s existing workflows.

1. **Build**
   - Run:
     - `dotnet build`

2. **Tests**
   - Run:
     - `dotnet test`

3. **Manual config validation**
   - Test at least these configurations via appsettings/environment overrides:
     - `UiVisible=false`, `BackendExecutionEnabled=true`, `BackgroundJobsEnabled=true`
       - simulation controls/status panels are not rendered anywhere
       - backend endpoints still function if directly invoked
     - `UiVisible=true`, `BackendExecutionEnabled=false`, `BackgroundJobsEnabled=true`
       - UI may show simulation area if configured visible
       - start/control/admin execution actions return safe disabled responses
       - no session can start
     - fully disabled:
       - `UiVisible=false`, `BackendExecutionEnabled=false`, `BackgroundJobsEnabled=false`
       - no simulation UI rendered
       - backend execution blocked
       - no simulation progression / finance generation jobs run
       - finance pages still load normally

4. **History validation**
   - Run a simulation in enabled mode and verify persisted history includes:
     - session identifier
     - status transitions
     - per-day generated record counts
     - anomalies
     - warnings
     - errors
   - Verify recent history and per-day logs are retrievable from the chosen admin/finance-facing view.

5. **Regression validation**
   - Verify standard finance page loading and existing finance APIs still work with simulation disabled.
   - Verify tenant/company scoping remains enforced for history retrieval.

# Risks and follow-ups
- **Risk: scattered simulation UI**
  - Simulation controls/status may exist in multiple components or layouts. Do a full-text search and gate all entry points.

- **Risk: incomplete server-side gating**
  - If only controllers are gated, background handlers or internal services may still start sessions. Guard at the application/service layer too.

- **Risk: worker coupling**
  - Simulation progression may be intertwined with finance generation. Be careful to suppress simulation-specific work without breaking normal finance jobs.

- **Risk: ambiguous “safe disabled response”**
  - Follow existing API conventions for disabled/forbidden/business-rule responses. Keep behavior consistent and non-breaking for clients.

- **Risk: history visibility vs UI-hidden requirement**
  - Acceptance criteria require simulation controls/status panels not be rendered anywhere when UI visibility is disabled, but operators also need history access. Prefer exposing history in an existing operator-facing finance/admin context without rendering interactive simulation controls; if ambiguity remains, implement the least surprising operator-safe behavior and note it in tests/PR summary.

- **Risk: migration strategy**
  - The repo includes `docs/postgresql-migrations-archive/README.md`; confirm the active migration workflow before adding schema changes.

Follow-ups to note if not fully covered by this task:
- Add richer operator filtering/export for simulation history
- Add audit-event integration for simulation gate denials if not already present
- Add metrics dashboards for skipped jobs / disabled execution counts
- Consider consolidating feature flags under a broader platform feature management pattern if multiple modules need similar gating