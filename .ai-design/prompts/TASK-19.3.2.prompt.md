# Goal

Implement **TASK-19.3.2 — Connect anomaly injection, registry, and detail endpoints** for **US-19.3 ST-FUI-303 — Anomaly injection and simulation time controls**.

Deliver an end-to-end vertical slice that wires the admin UI to backend APIs so users can:

- choose an anomaly scenario profile and submit an anomaly injection request
- view an anomaly registry with ID, type, status, and affected record reference
- open anomaly details showing scenario metadata and a related finance record link when available
- advance simulation time by a specified increment
- start a progression run from the UI
- view progression run status, run history, generated-record counts, and backend warning/failure messages

Use the existing solution architecture and coding conventions. Prefer minimal, cohesive changes over speculative abstractions.

# Scope

In scope:

- Backend API/query/command wiring for:
  - anomaly scenario profile selection and injection submission
  - anomaly registry listing
  - anomaly detail retrieval
  - simulation time advance
  - progression run start
  - progression run status/history retrieval
- Application-layer DTOs, handlers, validators, and mappings needed by the UI
- Web admin page updates to call the endpoints and render:
  - injection form
  - registry table
  - detail panel/view
  - simulation time controls
  - progression run status/history/messages/counts
- Error and warning display from backend responses
- Tenant-aware scoping consistent with platform patterns
- Tests for the new/updated handlers and API/UI integration points where the repo already has patterns

Out of scope unless required by existing code structure:

- New domain concepts beyond what is necessary to expose existing anomaly/progression capabilities
- Mobile app changes
- Major redesign of admin UX
- Reworking unrelated simulation engine internals
- Introducing new infrastructure patterns if existing ones already support the feature

# Files to touch

Inspect first, then update the smallest correct set. Likely areas:

- `src/VirtualCompany.Api`
  - endpoint/controller files for anomaly and simulation admin APIs
  - request/response contracts if API owns them
  - DI registration if needed
- `src/VirtualCompany.Application`
  - commands/queries and handlers for:
    - inject anomaly
    - list anomalies
    - get anomaly detail
    - advance simulation time
    - start progression run
    - get progression run status/history
  - validators
  - application DTOs/view models
- `src/VirtualCompany.Domain`
  - only if missing domain enums/value objects/contracts are required
- `src/VirtualCompany.Infrastructure`
  - repository/query implementations
  - persistence mappings
  - service adapters to existing simulation/anomaly services
- `src/VirtualCompany.Web`
  - admin page/component(s)
  - client service(s) for API calls
  - view models/state containers
  - route/link handling for anomaly detail and finance record navigation
- `src/VirtualCompany.Shared`
  - shared contracts only if this solution already centralizes API DTOs there
- `tests/VirtualCompany.Api.Tests`
  - endpoint/contract tests
- Other test projects if present and relevant

Before coding, locate the existing implementation for:
- anomaly scenario profiles
- anomaly registry/detail models
- simulation time/progression run services
- finance record detail routes
- admin page/component for this story area

# Implementation plan

1. **Discover existing feature seams**
   - Search for anomaly, simulation time, progression run, scenario profile, finance record, and admin page references.
   - Identify whether the project uses controllers, minimal APIs, MediatR/CQRS handlers, typed clients, and Blazor SSR/components.
   - Reuse existing naming and folder conventions exactly.

2. **Map acceptance criteria to concrete endpoints/contracts**
   Ensure there are backend operations for:
   - `GET` scenario profiles for anomaly injection selection, if not already exposed
   - `POST` anomaly injection request
   - `GET` anomaly registry list
   - `GET` anomaly detail by ID
   - `POST` advance simulation time
   - `POST` start progression run
   - `GET` progression run current status
   - `GET` progression run history
   If similar endpoints already exist but are not connected, prefer adapting the UI/client wiring instead of duplicating APIs.

3. **Define/normalize response shapes**
   Make sure the UI can render all required fields:
   - anomaly registry item:
     - `id`
     - `type`
     - `status`
     - `affectedRecordReference`
   - anomaly detail:
     - `id`
     - `type`
     - `status`
     - `scenarioMetadata`
     - `affectedRecordReference`
     - `relatedFinanceRecordId` or routeable reference when available
   - progression run:
     - current status
     - run history entries
     - generated-record counts
     - warnings
     - failures/messages
   Keep contracts additive and backward-compatible where possible.

4. **Implement application-layer commands and queries**
   Add or complete handlers for:
   - anomaly injection submission
   - anomaly registry retrieval
   - anomaly detail retrieval
   - simulation time advancement
   - progression run start
   - progression run status/history retrieval
   Include:
   - tenant/company scoping
   - validation for required fields and increment values
   - safe handling of missing records
   - structured warning/failure propagation from backend services

5. **Wire infrastructure/repositories/services**
   - Connect handlers to existing persistence/services.
   - If the simulation engine already returns warnings/failures/counts, preserve them in DTOs instead of flattening away useful detail.
   - If anomaly detail can resolve a related finance record, expose enough information for the web app to build a link.
   - Avoid direct DB access from UI/API layers.

6. **Expose or update API endpoints**
   - Add/update endpoints in `VirtualCompany.Api`.
   - Keep authorization and tenant resolution consistent with existing patterns.
   - Return appropriate status codes:
     - `200` for successful queries/actions with result payloads
     - `400` for validation issues
     - `404` for missing anomaly/detail where appropriate
     - `403` if tenant/role rules deny access
   - Preserve correlation-friendly error payloads if the API already has a standard envelope.

7. **Implement/update web API client**
   In `VirtualCompany.Web`:
   - add typed client methods for the above endpoints
   - keep request/response models aligned with API contracts
   - handle backend warnings/failures without swallowing them
   - use cancellation tokens if existing code does

8. **Update the admin UI**
   Add or complete the admin page so it supports:
   - scenario profile dropdown/select
   - anomaly injection submit action
   - registry table with required columns
   - selectable anomaly row or link to open detail
   - detail panel/page with scenario metadata and finance record link
   - simulation time increment input and advance action
   - progression run start action
   - current progression status display
   - run history list/table
   - generated-record counts
   - warning/failure message rendering
   Follow existing Blazor patterns for forms, validation, loading states, and error banners.

9. **Finance record navigation**
   - Reuse existing finance record route conventions.
   - Only show the related record link when a related finance record exists.
   - If the route requires a company/tenant context, ensure the link includes it consistently with the rest of the app.

10. **Handle refresh/state synchronization**
   After user actions:
   - successful anomaly injection should refresh registry and, if appropriate, select the new anomaly
   - advancing simulation time should refresh displayed simulation/progression state
   - starting a progression run should refresh current status/history
   - warnings/failures should remain visible after the action completes
   Avoid stale UI state.

11. **Testing**
   Add focused tests for:
   - command/query validation
   - anomaly registry/detail mapping
   - progression status/history mapping including warnings/failures/counts
   - endpoint success/failure cases
   - any existing web component tests if the repo supports them
   Prefer deterministic tests over broad snapshot coverage.

12. **Keep implementation clean**
   - Do not introduce unrelated refactors.
   - Do not duplicate DTOs if a shared contract pattern already exists.
   - Keep methods small and names explicit.
   - Add concise comments only where behavior is non-obvious.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in the web app:
   - Open the relevant admin page.
   - Confirm scenario profiles load into a selectable control.
   - Submit an anomaly injection request and verify success feedback.
   - Confirm the anomaly appears in the registry with:
     - ID
     - type
     - status
     - affected record reference
   - Select an anomaly and verify detail view shows:
     - scenario metadata
     - related finance record link when available
   - Enter a simulation time increment and advance time.
   - Start a progression run.
   - Verify the UI shows:
     - current run status
     - run history
     - generated-record counts
     - warning/failure messages from backend responses

4. Negative-path checks:
   - submit injection without a required scenario profile and verify validation
   - use invalid simulation increment and verify validation/error handling
   - request a missing anomaly detail and verify safe not-found behavior
   - verify warnings/failures are rendered, not silently ignored

5. Tenant/authorization checks:
   - confirm data is scoped to the active company/tenant
   - confirm unauthorized access patterns follow existing app behavior

# Risks and follow-ups

- **Contract drift risk:** anomaly/progression backend models may already exist with slightly different shapes than the UI needs. Prefer adapter DTOs over changing stable domain models unnecessarily.
- **Tenant scoping risk:** admin/simulation endpoints may be easy to wire incorrectly if company context is implicit. Verify every query/action is tenant-aware.
- **UI state risk:** progression status/history can become stale after actions. Ensure explicit refresh behavior after mutation calls.
- **Route/link risk:** finance record detail navigation may use an existing route pattern that is easy to guess wrong. Confirm actual route definitions before building links.
- **Warning/failure visibility risk:** backend messages are often dropped during mapping. Preserve them end-to-end.
- **Test coverage gap:** if web component tests are not established, prioritize application/API tests and perform careful manual verification.

Follow-ups after completion if needed:
- add polling or auto-refresh for long-running progression runs if current UX is manual refresh only
- add richer filtering/sorting for anomaly registry
- add audit trail hooks for anomaly injection and progression actions if not already present
- add loading/empty-state polish once the vertical slice is working