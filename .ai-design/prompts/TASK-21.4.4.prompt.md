# Goal

Implement backlog task **TASK-21.4.4 — Expose recent simulation history and error state in sandbox admin or finance diagnostics view** for story **US-21.4 Add feature gating, observability, and safe-disable behavior for simulation**.

Deliver a production-ready vertical slice in the existing **.NET modular monolith** that:

- adds **configuration-driven feature flags** to independently disable:
  - **simulation UI visibility**
  - **simulation backend execution**
- ensures disabled states are **safe**, **non-breaking**, and **tenant/company scoped where applicable**
- persists and exposes **recent simulation run history**, including:
  - session identifier
  - status transitions
  - generated record counts per simulated day
  - injected anomalies
  - warnings
  - errors
- surfaces this history and per-day generation logs in an **existing admin or finance-facing diagnostics view**
- prevents any simulation execution or background progression when backend execution is disabled
- preserves normal finance page behavior and existing finance APIs when simulation is disabled

Use existing architecture and conventions in the repo. Prefer minimal, cohesive changes over speculative abstractions.

# Scope

In scope:

- Add or extend **simulation feature configuration** with separate flags for:
  - UI visibility
  - backend execution
- Update backend simulation entry points so disabled execution returns a **safe disabled response**
- Ensure background jobs/workers that progress simulation or generate finance data do **not run** when simulation backend execution is disabled
- Add/extend persistence for simulation observability/history
- Add query/API support to retrieve:
  - recent simulation sessions for a tenant/company
  - per-day generation logs for a selected session
  - surfaced warnings/errors
- Add UI changes in an existing **admin or finance diagnostics** surface to display:
  - recent simulation history
  - current/last error state
  - per-day generation details
- Hide all simulation controls/status panels when UI visibility is disabled
- Add tests covering feature gating, disabled behavior, and diagnostics retrieval

Out of scope unless required by existing code structure:

- Building a brand-new admin area if an existing diagnostics/admin/finance page can be extended
- Reworking unrelated finance workflows
- Introducing a new feature flag framework if simple options/config patterns already exist
- Large refactors of simulation architecture beyond what is needed for acceptance criteria

# Files to touch

Inspect the solution first, then update the most relevant files in these likely areas.

Backend:
- `src/VirtualCompany.Api/**`
  - simulation endpoints/controllers
  - finance/admin diagnostics endpoints
  - DI/options registration
- `src/VirtualCompany.Application/**`
  - simulation commands/queries/handlers
  - DTOs/view models for diagnostics
  - feature gating services/interfaces
- `src/VirtualCompany.Domain/**`
  - simulation session/history entities or value objects
  - status/error models if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/repositories
  - background workers/job guards
  - configuration binding
  - migrations support if this repo stores migrations here

Frontend:
- `src/VirtualCompany.Web/**`
  - existing finance/admin diagnostics page(s)
  - simulation controls/status components
  - conditional rendering based on UI visibility flag
  - diagnostics/history UI

Tests:
- `tests/VirtualCompany.Api.Tests/**`
  - API integration/endpoint tests
  - disabled-response tests
  - diagnostics retrieval tests
- add application/infrastructure tests if corresponding test projects already exist

Docs/config:
- `README.md`
- appsettings files under API/Web if feature flags are configured there
- migration docs only if this repo requires documentation updates

Also inspect:
- `src/VirtualCompany.Api/Program.cs`
- any existing `Options`, `FeatureFlags`, `Simulation`, `Finance`, `Sandbox`, `Diagnostics`, `Admin` namespaces/files
- any existing background worker registration and scheduling code

# Implementation plan

1. **Discover existing simulation and diagnostics implementation**
   - Search for:
     - `Simulation`
     - `Sandbox`
     - `Finance`
     - `Diagnostics`
     - `Admin`
     - `Feature`
     - `Options`
     - `BackgroundService` / hosted jobs
   - Identify:
     - current simulation APIs and UI entry points
     - current simulation persistence model
     - where finance/admin diagnostics already live
     - how configuration/options are currently bound
     - how background jobs are scheduled and guarded

2. **Add configuration model for independent simulation flags**
   - Introduce or extend a strongly typed options class, e.g. `SimulationFeatureOptions`, with explicit booleans such as:
     - `UiVisible`
     - `BackendExecutionEnabled`
   - Bind from configuration in API/Web as needed
   - Keep naming clear and future-safe
   - If there is already a feature flag/options pattern, follow it exactly rather than inventing a new one

3. **Centralize simulation availability checks**
   - Add a small application-level service/helper for simulation availability decisions, e.g.:
     - `IsUiVisible`
     - `IsBackendExecutionEnabled`
     - `GetDisabledResponse()`
   - Use this consistently across:
     - API endpoints
     - admin actions
     - background workers
     - UI rendering decisions
   - Avoid duplicating raw config checks throughout the codebase

4. **Implement safe backend disable behavior**
   - Update simulation control APIs/admin actions so when backend execution is disabled:
     - they do not start a session
     - they do not enqueue work
     - they return a safe, explicit disabled result
   - Prefer existing API response conventions; if none exist, return a stable structured payload with:
     - disabled status/code
     - user-safe message
   - Ensure no partial session records are created unless the existing audit model requires a denied attempt record

5. **Guard background progression and finance generation jobs**
   - Identify all workers/jobs that:
     - progress simulation sessions
     - generate simulated finance data
     - poll/process simulation state
   - Add an early exit guard when backend execution is disabled
   - Ensure “fully disabled” means:
     - no new sessions start
     - no in-flight progression continues
     - no finance generation jobs run for any tenant/company
   - If jobs are tenant-iterative, the global backend disable should short-circuit before tenant processing begins

6. **Persist simulation history and error state**
   - Extend existing simulation persistence to record:
     - session identifier
     - status transitions over time
     - generated record counts per simulated day
     - injected anomalies
     - warnings
     - errors
   - If the model already exists partially, extend it rather than duplicating tables/entities
   - Prefer normalized persistence if there are clear session/day-log concepts, e.g.:
     - simulation session
     - simulation status history/event
     - simulation day generation log
   - Include tenant/company scoping on all persisted records
   - Add EF configuration and migration if schema changes are needed

7. **Add diagnostics query surface**
   - Implement application queries and API endpoints to retrieve:
     - recent simulation sessions for a company
     - details for a selected session
     - per-day generation logs
     - latest/current error state
   - Keep responses optimized for diagnostics UI, not raw entity dumps
   - Ensure authorization and tenant/company scoping are enforced

8. **Expose diagnostics in existing admin or finance-facing UI**
   - Find the most appropriate existing page:
     - finance diagnostics
     - sandbox admin
     - admin diagnostics
   - Add a diagnostics section/panel/table showing:
     - recent sessions
     - status
     - started/completed timestamps
     - latest warning/error summary
     - anomaly summary
   - Add drill-in or expandable details for per-day generation logs:
     - simulated day
     - generated record counts
     - anomalies
     - warnings/errors
   - Surface current/last error state clearly but non-disruptively

9. **Hide simulation UI when UI visibility is disabled**
   - Ensure all simulation controls and related simulation status panels are not rendered anywhere in the app when UI visibility is disabled
   - Search for all simulation-related components/pages/partials and gate them
   - Do not merely disable buttons; remove rendering entirely per acceptance criteria
   - If navigation links/menu items exist, hide them too

10. **Preserve finance page stability**
    - Verify finance pages still load when:
      - UI visibility disabled
      - backend execution disabled
      - both disabled
    - Ensure diagnostics/history sections degrade gracefully:
      - hidden if UI visibility disabled
      - visible but showing disabled backend state if that matches product intent and existing page structure
    - Do not break existing finance APIs or page models

11. **Add tests**
    - API tests:
      - backend disabled returns safe disabled response
      - no session starts when backend disabled
      - diagnostics endpoints return recent history and day logs
      - tenant/company scoping enforced
    - Worker tests:
      - progression/generation jobs no-op when backend disabled
    - UI/component tests if present in repo; otherwise cover via page model/API tests as feasible
    - Regression tests:
      - finance page/API still loads without simulation enabled

12. **Document configuration**
    - Add/update config examples for the two flags
    - Keep documentation concise and implementation-specific

# Validation steps

1. **Code discovery**
   - Confirm actual simulation, finance, diagnostics, and worker locations before editing
   - Adjust implementation to existing patterns

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual configuration validation**
   - Test these configurations:
     - `UiVisible=true`, `BackendExecutionEnabled=true`
     - `UiVisible=false`, `BackendExecutionEnabled=true`
     - `UiVisible=true`, `BackendExecutionEnabled=false`
     - `UiVisible=false`, `BackendExecutionEnabled=false`

5. **Manual behavior checks**
   - With UI visibility disabled:
     - verify no simulation controls/status panels/nav items render anywhere relevant
   - With backend execution disabled:
     - call simulation start/control APIs and confirm safe disabled response
     - verify no session starts
     - verify no admin action can trigger execution
   - With fully disabled:
     - verify workers/jobs do not progress simulation or generate finance data
   - In enabled mode:
     - run a simulation and verify history captures:
       - session id
       - status transitions
       - per-day counts
       - anomalies
       - warnings/errors
     - verify diagnostics view shows recent history and day logs

6. **Regression checks**
   - Open standard finance pages and exercise existing finance workflows
   - Confirm no errors or broken API responses when simulation is disabled

7. **Schema validation**
   - If a migration is added, verify it applies cleanly and supports rollback if the repo has that convention

# Risks and follow-ups

- **Risk: simulation state is currently implicit or scattered**
  - Mitigation: centralize availability checks and diagnostics DTOs without over-refactoring

- **Risk: multiple UI surfaces reference simulation**
  - Mitigation: do a full-text search and gate all related components, nav items, and status panels

- **Risk: background jobs may have more than one execution path**
  - Mitigation: guard both scheduling/dispatch and worker execution paths where applicable

- **Risk: acceptance criteria require “not rendered anywhere”**
  - Mitigation: avoid disabled placeholders for simulation controls when UI visibility is off

- **Risk: diagnostics view choice may be ambiguous**
  - Mitigation: prefer extending an existing finance/admin diagnostics page already in the app; document the chosen surface in the PR summary

- **Risk: disabled backend behavior could accidentally return error semantics**
  - Mitigation: use a safe, explicit disabled response rather than exceptions/500s

Follow-ups if not fully covered by this task:
- add richer operator filtering/export for simulation history
- add audit events for denied simulation attempts if product wants operator traceability
- add retention policy for simulation logs/history
- add alerting on repeated simulation failures or anomaly-heavy runs