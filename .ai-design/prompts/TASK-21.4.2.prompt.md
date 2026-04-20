# Goal

Implement **TASK-21.4.2**: add backend guards so that when simulation execution is disabled by configuration, all simulation execution entry points are safely blocked and no simulation work can start or continue.

This task is specifically about **backend safe-disable behavior**, not the full UI visibility or history/audit feature set. The implementation must ensure:

- Simulation execution can be disabled independently from UI visibility.
- Disabled execution blocks:
  - simulation API routes/endpoints
  - admin/manual trigger actions
  - background jobs/workers that progress or generate simulation data
- Blocked requests return a **safe disabled response**, not an unhandled exception.
- Standard finance pages and finance APIs continue to work normally when simulation is disabled.
- Multi-tenant behavior remains intact; disabling execution must prevent simulation activity for all tenants/companies when the flag is off.

Use existing architecture and conventions in the solution. Prefer a centralized feature-gate service/policy over scattered inline checks.

# Scope

In scope:

- Add or extend configuration for simulation feature flags with separate controls for:
  - UI visibility
  - backend execution
- Add backend guard logic for all simulation execution entry points.
- Prevent new simulation sessions from starting when execution is disabled.
- Prevent background progression / finance generation jobs from running when simulation is disabled.
- Return a consistent disabled response from APIs/admin actions.
- Add/update tests covering disabled behavior and non-simulation finance behavior.

Out of scope unless required by existing code paths touched:

- Implementing simulation history persistence
- Implementing admin/finance-facing history retrieval views
- UI rendering changes for hiding simulation controls
- Large refactors unrelated to simulation gating
- New product surface area beyond what is needed to safely block execution

If you discover missing simulation abstractions that make safe gating impossible, introduce the smallest clean seam necessary.

# Files to touch

Inspect the codebase first, then update the relevant files you actually find. Likely areas include:

- `src/VirtualCompany.Api/**`
  - simulation controllers/endpoints
  - admin/manual trigger endpoints
  - API error/response mapping
  - DI/configuration registration
- `src/VirtualCompany.Application/**`
  - simulation commands/handlers
  - job trigger services
  - feature gate service/interface
  - result/error contracts
- `src/VirtualCompany.Domain/**`
  - domain errors or feature-disabled result types if domain-level modeling exists
- `src/VirtualCompany.Infrastructure/**`
  - configuration binding
  - background workers / schedulers / hosted services
  - job executors for simulation progression or finance generation
- `src/VirtualCompany.Shared/**`
  - shared contracts/constants if API responses use shared DTOs
- `src/VirtualCompany.Web/**`
  - only if backend-triggering admin actions are implemented via server-side handlers in web and need safe handling
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests for disabled responses
- Potentially other test projects if present for application/infrastructure layers

Also inspect:

- `README.md`
- existing options/config classes
- `appsettings*.json` files
- any simulation-related files under `src/**/Simulation*`, `Finance*`, `Jobs*`, `Workers*`, `Admin*`

# Implementation plan

1. **Discover current simulation architecture**
   - Find all simulation-related code paths:
     - start session endpoints
     - stop/pause/resume/manual trigger endpoints
     - admin actions
     - background workers/schedulers
     - finance generation jobs tied to simulation
   - Identify whether simulation execution is initiated:
     - directly in controllers
     - via MediatR/command handlers
     - via application services
     - via hosted services/background jobs
   - Identify current response patterns for business-rule failures.

2. **Add/confirm configuration model**
   - Introduce or extend a strongly typed options class for simulation feature flags, e.g.:
     - `Simulation:UiEnabled`
     - `Simulation:ExecutionEnabled`
   - Bind it through the existing ASP.NET Core options pattern.
   - Keep naming aligned with existing config conventions if a feature flag/options model already exists.
   - Default behavior should be conservative and explicit; do not silently enable execution in environments where config is missing unless the project already follows that convention.

3. **Create a centralized backend gate abstraction**
   - Add an application-facing service/interface such as:
     - `ISimulationFeatureGate`
     - methods like `IsUiEnabled`, `IsExecutionEnabled`, `EnsureExecutionEnabled()`
   - The service should encapsulate config access and produce a consistent disabled outcome.
   - Prefer returning a structured application result/error over throwing generic exceptions.
   - If the codebase already has a result pattern, domain error type, or problem-details mapping, integrate with that instead of inventing a parallel pattern.

4. **Guard all simulation execution entry points**
   - Add checks before any simulation session starts or manual trigger executes.
   - Ensure all relevant API/admin actions short-circuit with a safe disabled response when execution is off.
   - This includes:
     - simulation start/create/run endpoints
     - manual progression/generation triggers
     - admin actions that enqueue simulation work
     - any command handlers callable outside HTTP
   - Do not rely only on controller-level checks; add guards at the application/service layer too so non-HTTP callers are also protected.

5. **Guard background workers and scheduled jobs**
   - Update simulation-related hosted services, schedulers, or job executors so they no-op safely when execution is disabled.
   - Ensure no simulation progression or finance generation job runs for any tenant/company while disabled.
   - If jobs are dequeued from a queue/outbox/scheduler, they should:
     - skip execution safely
     - log a clear structured message
     - avoid creating new simulation state transitions
   - Be careful not to break unrelated finance jobs; only block simulation-specific work and finance generation that is part of simulation mode.

6. **Return a consistent safe disabled response**
   - Map disabled execution to the project’s standard API failure shape.
   - Response should be safe and explicit, e.g. semantic equivalent of:
     - HTTP 403 or 409 or 423 depending on existing conventions
     - message indicating simulation execution is currently disabled
   - Reuse existing problem details / error code infrastructure if available.
   - Avoid leaking stack traces or internal config details.

7. **Preserve normal finance behavior**
   - Verify that standard finance page/API loading paths do not depend on simulation execution being enabled.
   - If shared services currently assume simulation is available, decouple the execution-only paths from read-only finance behavior.
   - Do not block non-simulation finance APIs.

8. **Add structured logging/observability**
   - Log when simulation execution is blocked:
     - endpoint/action/job name
     - company/tenant context when available
     - correlation ID if available
   - Use existing structured logging conventions.
   - Keep these as technical logs; do not implement new business history persistence in this task unless already required by touched code.

9. **Add tests**
   - Add/extend tests for:
     - simulation start endpoint returns disabled response when execution disabled
     - admin/manual trigger endpoint returns disabled response when execution disabled
     - application service/command handler refuses to start a session when execution disabled
     - background worker/job does not run simulation progression when execution disabled
     - standard finance endpoint/page still loads when simulation execution disabled
   - Prefer integration tests for API behavior and focused unit tests for gate/service logic.

10. **Keep implementation minimal and cohesive**
   - Avoid duplicating flag checks in many places if a policy/gate can centralize them.
   - If multiple simulation jobs exist, factor a shared guard helper for workers.
   - Update comments/docs only where useful for maintainability.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If appsettings or options binding tests exist, verify configuration binding for:
   - simulation UI enabled/disabled
   - simulation execution enabled/disabled

4. Manually validate via tests or local run:
   - With `Simulation:ExecutionEnabled = false`
     - simulation start API returns expected disabled response
     - manual/admin trigger returns expected disabled response
     - no simulation session is created
     - simulation worker/job path exits safely without processing
   - With execution enabled
     - existing simulation execution path still works as before
   - With execution disabled
     - non-simulation finance API/page still succeeds

5. Confirm logs show a clear blocked/disabled message for denied execution attempts.

6. Ensure no unrelated warnings/errors were introduced and formatting/analyzers pass if configured.

# Risks and follow-ups

- **Hidden entry points:** simulation execution may be triggered from more places than obvious controllers. Search thoroughly for commands, hosted services, schedulers, queue consumers, and admin handlers.
- **Controller-only guard risk:** guarding only HTTP endpoints is insufficient if workers or internal services can still start sessions.
- **Shared finance job ambiguity:** be careful to distinguish simulation finance generation from normal finance processing so standard workflows remain unaffected.
- **Inconsistent error handling:** if the codebase lacks a unified result/error model, introducing one narrowly for this task may create inconsistency. Prefer adapting to existing patterns.
- **Config default behavior:** unclear defaults can accidentally enable or disable simulation in environments. Follow existing configuration conventions and document assumptions in code comments if needed.
- **Future follow-up:** UI visibility gating, simulation history persistence, and operator-facing history retrieval are separate acceptance-criteria areas and should be handled in subsequent tasks if not already implemented elsewhere.