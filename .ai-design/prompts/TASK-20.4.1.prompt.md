# Goal
Implement backlog task **TASK-20.4.1** for story **US-20.4 ST-FUI-412/ST-FUI-413** by adding finance UI actions and confirmation behavior for manual seed generation/regeneration, while ensuring finance-dependent API flows handle missing seeded data safely and consistently.

The implementation prompt should direct the coding agent to:

- Add a `/finance` UI action that shows **Generate finance data** when finance seed data is absent and **Regenerate finance data** when seed data already exists.
- Require explicit confirmation before overwrite when manual regenerate is invoked in **replace** mode.
- Support at minimum a manual trigger with **replace** mode; if **append** mode already exists or is added, keep it clearly differentiated in both API contract and UI.
- Ensure finance APIs that depend on seeded data either:
  - return a structured **`not_initialized`** response, or
  - trigger seeding through configured fallback behavior.
- Prevent unhandled exceptions in finance-dependent requests when seed data is missing.
- Route both manual and fallback seeding through the same idempotent orchestration service and emit audit/log events.

# Scope
In scope:

- Blazor `/finance` page updates for seed action visibility and interaction.
- Confirmation modal/dialog for destructive regenerate behavior in replace mode.
- API endpoint or command wiring for manual finance seed trigger.
- Shared application service/orchestration path for manual and fallback seeding.
- Structured API response for missing finance initialization state.
- Defensive handling in finance-dependent endpoints/services to avoid null/missing-data crashes.
- Audit/log emission for manual and fallback seed execution paths.
- Tests covering UI state, API behavior, and orchestration reuse.

Out of scope unless already partially implemented and trivial to complete:

- Broad redesign of finance domain models.
- New finance seed modes beyond `replace` and optional `append`.
- Large workflow engine changes unrelated to finance seeding.
- Mobile UI changes unless the same shared API contract requires updates.
- Full audit UI surfacing if only backend event emission is needed for this task.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Potential backend locations:
- `src/VirtualCompany.Api/**`
  - finance endpoints/controllers/minimal APIs
  - exception/response mapping
- `src/VirtualCompany.Application/**`
  - finance commands/queries
  - seed orchestration service interfaces and handlers
  - DTOs for structured `not_initialized` responses
- `src/VirtualCompany.Domain/**`
  - finance seed state/value objects/enums if needed
- `src/VirtualCompany.Infrastructure/**`
  - persistence/repositories for finance seed state
  - audit/log integrations
  - fallback seeding implementation details

Potential web UI locations:
- `src/VirtualCompany.Web/**`
  - `/finance` page/component
  - shared modal/dialog components
  - API client/service for finance actions
  - view models for seed state and action mode

Potential shared contract locations:
- `src/VirtualCompany.Shared/**`
  - request/response contracts if shared between API and Web

Potential tests:
- `tests/VirtualCompany.Api.Tests/**`
  - finance API tests
  - missing-seed behavior tests
  - orchestration/audit tests
- If web component tests exist, update/add them in the appropriate test project.

Before editing, locate:
- Existing finance pages/components
- Existing seed/init orchestration services
- Existing audit event patterns
- Existing modal/confirmation patterns in Blazor
- Existing structured error/ProblemDetails conventions

# Implementation plan
1. **Discover current finance seeding flow**
   - Find all finance-related endpoints, services, and UI components.
   - Identify how the app currently determines whether finance data is initialized.
   - Identify any existing seed orchestration service and whether manual and fallback paths already diverge.
   - Identify current exception behavior when finance data is missing.

2. **Define/normalize seed state and trigger contracts**
   - Introduce or reuse a clear seed status model exposed to the `/finance` UI, such as:
     - `isInitialized`
     - `lastSeededAt`
     - `lastSeedMode`
     - `availableModes`
   - Add or refine a manual trigger request contract with explicit mode:
     - `replace` required
     - `append` optional only if supported
   - Keep mode naming consistent across UI, API, application layer, and logs.

3. **Implement shared idempotent seed orchestration**
   - Ensure both manual trigger and fallback trigger call the same application service, e.g. a single orchestration entry point.
   - The orchestration service should:
     - be idempotent
     - accept tenant/company context
     - accept trigger source (`manual` vs `fallback`)
     - accept seed mode
     - emit structured logs/audit events
   - Avoid duplicating seed logic in controllers/endpoints or UI-specific handlers.

4. **Add safe missing-data handling for finance-dependent APIs**
   - Update finance-dependent queries/endpoints so missing seed data does not cause unhandled exceptions.
   - Implement one of the accepted behaviors based on existing architecture/config:
     - return a structured `not_initialized` response, or
     - trigger fallback seeding and return an appropriate in-progress/accepted/result response.
   - If fallback behavior is configurable, preserve that configuration and make the branch explicit in code.
   - Use existing API response conventions where possible instead of ad hoc payloads.

5. **Add structured `not_initialized` response**
   - Return a stable, machine-readable response shape for finance APIs when seed data is required but absent.
   - Include enough information for the UI to react, for example:
     - error/status code: `not_initialized`
     - domain/context: `finance`
     - optional message
     - optional `canTriggerSeed`
   - Prefer existing ProblemDetails or typed result conventions if already used in the codebase.

6. **Update `/finance` UI action state**
   - On page load, fetch finance seed status.
   - Show:
     - **Generate finance data** when not initialized
     - **Regenerate finance data** when initialized
   - Disable or show loading state while a seed request is in progress.
   - If append mode exists, present it distinctly from replace mode and label the overwrite implications clearly.

7. **Add confirmation modal for replace regenerate**
   - When the user selects regenerate in `replace` mode and data already exists, require explicit confirmation before submitting.
   - Reuse an existing modal/dialog component if available.
   - Confirmation copy should clearly state overwrite/destructive behavior.
   - Do not require confirmation for first-time generate unless product patterns already do so.

8. **Wire UI to API**
   - Add/update the web API client/service method for manual seed trigger.
   - Ensure the UI passes explicit mode and handles:
     - success
     - `not_initialized`
     - accepted/in-progress
     - validation/conflict errors
   - Refresh seed status after completion or accepted trigger.

9. **Emit audit/log events**
   - For both manual and fallback seeding, emit structured events with at least:
     - company/tenant context
     - actor/source (`manual`, `fallback`, possibly user/system)
     - mode (`replace`, optional `append`)
     - outcome
     - correlation/idempotency identifiers if available
   - Reuse the project’s audit/event patterns rather than inventing a parallel mechanism.

10. **Add tests**
   - API tests:
     - finance endpoint returns structured `not_initialized` instead of throwing
     - fallback path uses shared orchestration service
     - manual trigger uses same orchestration service
     - replace mode accepted and validated
   - UI/component/integration tests if available:
     - `/finance` shows Generate vs Regenerate based on state
     - replace regenerate requires confirmation
     - append mode, if present, is visually distinct
   - Regression tests for no unhandled exceptions on missing finance data.

11. **Keep implementation aligned with architecture**
   - Maintain clean boundaries:
     - Web/UI only handles presentation and user interaction
     - API maps HTTP to application commands/queries
     - Application layer owns orchestration and business rules
     - Infrastructure handles persistence/logging/audit plumbing
   - Keep tenant scoping enforced throughout.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate `/finance` UI:
   - With no finance seed data:
     - page shows **Generate finance data**
   - With existing finance seed data:
     - page shows **Regenerate finance data**
   - Trigger regenerate in `replace` mode:
     - confirmation modal appears
     - no request is sent before confirmation
   - Confirm action:
     - request is sent
     - UI shows progress/success/error appropriately

4. Validate API behavior for missing data:
   - Call finance-dependent endpoints before initialization.
   - Confirm they return structured `not_initialized` or configured fallback behavior.
   - Confirm no 500/unhandled exception occurs for expected missing-seed scenarios.

5. Validate orchestration reuse:
   - Verify manual trigger and fallback trigger both pass through the same application service.
   - Confirm logs/audit events are emitted for both paths.

6. Validate mode handling:
   - `replace` mode works end-to-end.
   - If `append` exists, verify it is clearly labeled in request payloads and UI and does not reuse replace wording.

7. Validate tenant safety:
   - Confirm finance seed status and trigger actions remain company-scoped.

# Risks and follow-ups
- **Unknown existing finance implementation:** The repo may already have partial seed/fallback logic under different naming. Reconcile rather than duplicate.
- **UI modal pattern mismatch:** If no shared confirmation modal exists, create the smallest reusable dialog consistent with current Blazor patterns.
- **Contract drift risk:** If Web and API use separate DTOs, ensure mode/status names stay identical.
- **Fallback ambiguity:** Acceptance criteria allow either structured `not_initialized` or fallback seeding depending on configuration. Preserve existing intended behavior and make it explicit in code/tests.
- **Audit/event inconsistency:** Prefer existing audit infrastructure; if only technical logs exist today, add minimal structured logging now and note richer audit persistence as follow-up.
- **Concurrency/idempotency edge cases:** Multiple manual/fallback triggers may race. Reuse existing idempotency/locking patterns if present; otherwise add a minimal guard and document any remaining gaps.
- **Follow-up candidates:**
  - expose seed history/last run details in `/finance`
  - add append mode only if product semantics are well-defined
  - add richer user-facing status for fallback-triggered seeding
  - add mobile parity later if finance actions are needed outside web