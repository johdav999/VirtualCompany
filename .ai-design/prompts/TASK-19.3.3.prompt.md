# Goal
Implement backlog task **TASK-19.3.3 — Implement simulation clock controls and progression history panel** for story **US-19.3 ST-FUI-303 — Anomaly injection and simulation time controls**.

Deliver a working admin UI flow in the existing .NET solution that allows users to:

- select an anomaly scenario profile and submit an anomaly injection request
- view an anomaly registry with anomaly ID, type, status, and affected record reference
- open anomaly details showing scenario metadata and a related finance record link when available
- advance simulation time by a specified increment
- start a simulation progression run from the UI
- view progression run status, run history, generated-record counts, and backend warning/failure messages

Use the existing architecture and coding conventions in the repo. Prefer extending current modules and patterns over introducing new frameworks or parallel abstractions.

# Scope
In scope:

- Admin-facing Blazor UI for anomaly injection and simulation time controls
- Query/command integration from web UI to backend APIs/application layer
- Registry/list/detail presentation for anomalies
- Progression controls and progression run history panel
- Display of backend-returned statuses, counts, warnings, and failures
- Tenant-aware data access and authorization consistent with existing app patterns
- Tests for application/API/UI-facing behavior where the repo already has coverage patterns

Out of scope unless required by existing code structure:

- Creating a brand-new simulation engine
- Reworking unrelated admin navigation/layout
- Mobile app support
- Major redesign of backend contracts if endpoints/services already exist
- Broad refactors outside what is needed to support this task

If backend endpoints/contracts for anomaly injection and simulation progression already exist, consume them. If they are missing or incomplete, add the minimal application/API surface necessary to satisfy the acceptance criteria.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas as needed.

Likely areas:

- `src/VirtualCompany.Web/**`
  - admin page(s), components, forms, tables, detail panels
  - typed client/service classes used by Blazor pages
  - shared DTO/view-model mapping if present in web layer
- `src/VirtualCompany.Api/**`
  - controllers/endpoints for simulation clock, progression runs, anomaly registry/detail, anomaly injection
- `src/VirtualCompany.Application/**`
  - commands/queries/handlers
  - service interfaces and DTOs
  - validation logic
- `src/VirtualCompany.Domain/**`
  - domain models/value objects/enums only if required
- `src/VirtualCompany.Infrastructure/**`
  - repository/query implementations
  - EF Core mappings/config if needed
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this repo uses shared request/response models across API/Web
- `tests/VirtualCompany.Api.Tests/**`
  - API/controller/integration-style tests
- other test projects if present for application/web behavior

Also inspect:

- `README.md`
- any existing docs for admin/simulation/anomaly features
- solution-wide patterns for MediatR/CQRS, Result types, validation, and tenant scoping

# Implementation plan
1. **Discover existing simulation/anomaly implementation**
   - Search the solution for terms like:
     - `simulation`
     - `clock`
     - `progression`
     - `anomaly`
     - `scenario profile`
     - `admin`
   - Identify:
     - existing entities/DTOs
     - API endpoints
     - application commands/queries
     - current admin page route and component structure
   - Reuse existing naming and patterns exactly where possible.

2. **Map acceptance criteria to concrete UI sections**
   Implement or extend an admin page with clearly separated sections:
   - anomaly injection form
   - anomaly registry table
   - anomaly detail panel/modal/drawer
   - simulation clock controls
   - progression run status + history panel

   Minimum UI behavior:
   - scenario profile selector
   - submit action for anomaly injection
   - anomaly list columns:
     - ID
     - type
     - status
     - affected record reference
   - selectable anomaly row
   - detail view with:
     - scenario metadata
     - related finance record link when available
   - time increment input/control
   - advance time action
   - start progression run action
   - progression display:
     - current/latest run status
     - run history
     - generated-record counts
     - warning/failure messages

3. **Add or complete backend contracts**
   If missing, add minimal request/response contracts for:
   - listing available anomaly scenario profiles
   - submitting anomaly injection
   - listing anomalies
   - getting anomaly detail
   - advancing simulation time
   - starting a progression run
   - listing progression run history / latest status

   Keep contracts explicit and UI-friendly. Include fields needed by acceptance criteria, for example:
   - anomaly summary:
     - `Id`
     - `Type`
     - `Status`
     - `AffectedRecordReference`
   - anomaly detail:
     - `Id`
     - `Type`
     - `Status`
     - `ScenarioProfileId/Name`
     - `ScenarioMetadata`
     - `AffectedRecordReference`
     - `RelatedFinanceRecordId`
     - `RelatedFinanceRecordDisplay`
   - progression run:
     - `RunId`
     - `Status`
     - `StartedAt`
     - `CompletedAt`
     - `GeneratedRecordCounts`
     - `Warnings`
     - `FailureMessage`

4. **Implement application-layer commands/queries**
   Follow existing CQRS-lite patterns.
   Add or complete handlers for:
   - get anomaly scenario profiles
   - inject anomaly
   - get anomaly registry
   - get anomaly detail
   - advance simulation clock
   - start progression run
   - get progression run history/status

   Requirements:
   - enforce tenant/company scoping
   - validate required inputs
   - return safe, structured errors/messages
   - preserve backend warning/failure messages for UI display

5. **Implement API endpoints**
   Add or update API endpoints to expose the above operations.
   Requirements:
   - tenant-aware authorization
   - proper HTTP semantics
   - validation error responses consistent with the project
   - no leaking cross-tenant data
   - stable response shapes for the Blazor UI

6. **Implement web client/service integration**
   In `VirtualCompany.Web`, add or extend typed service methods to call the API.
   Ensure:
   - async calls with cancellation where patterns exist
   - loading/error states
   - response mapping into page/component models
   - refresh behavior after mutation actions:
     - after anomaly injection, refresh registry and optionally select the new anomaly
     - after advancing time, refresh current simulation/progression state
     - after starting progression, refresh latest run and history

7. **Build the admin UI**
   Implement the page/components with practical UX:
   - anomaly injection form:
     - dropdown/select for scenario profile
     - optional metadata/notes fields only if backend supports them
     - submit button with disabled/loading state
     - success/error feedback
   - anomaly registry:
     - table/grid with clickable/selectable rows
     - empty state
     - loading state
   - anomaly detail:
     - side panel/card/modal
     - scenario metadata rendered clearly
     - finance record link only when related record exists
   - simulation controls:
     - increment input with validation
     - advance time button
     - start progression button
   - progression history:
     - latest run summary
     - historical list/table
     - generated-record counts
     - warnings/failure messages styled distinctly

   Keep styling aligned with existing Blazor components and app design system already in the repo.

8. **Handle state and refresh flows carefully**
   Make sure the page:
   - loads initial data on first render
   - does not issue unnecessary duplicate requests
   - preserves selected anomaly when refreshing if still present
   - clearly distinguishes:
     - loading
     - empty
     - success
     - warning
     - failure
   - surfaces backend messages without swallowing them

9. **Add tests**
   Add tests at the layers already used in the repo:
   - application tests for command/query behavior and validation
   - API tests for endpoint responses and tenant scoping
   - component/page tests only if the repo already uses a Blazor test pattern

   Cover at least:
   - anomaly injection succeeds with valid scenario profile
   - anomaly registry returns required columns/data
   - anomaly detail includes scenario metadata and finance link when available
   - advancing time with valid increment triggers expected backend call
   - starting progression run returns/refreshes run status
   - warnings and failure messages are preserved in responses
   - invalid input is rejected cleanly
   - tenant isolation is enforced

10. **Keep implementation minimal and cohesive**
   - Do not invent extra abstractions unless repeated patterns justify them.
   - Prefer extending existing admin page(s) over creating fragmented screens.
   - Keep DTOs and UI models focused on the acceptance criteria.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the admin UI flow:
   - navigate to the admin page for simulation/anomaly controls
   - confirm scenario profiles load
   - submit an anomaly injection request
   - verify the anomaly appears in the registry with:
     - ID
     - type
     - status
     - affected record reference
   - select an anomaly and verify detail view shows:
     - scenario metadata
     - finance record link when available
   - enter a valid time increment and advance simulation time
   - start a progression run
   - verify the UI shows:
     - run status
     - run history
     - generated-record counts
     - warning/failure messages from backend

4. Verify negative/error cases:
   - submit anomaly injection without selecting a profile
   - advance time with invalid increment
   - simulate backend warning/failure response and confirm it is visible in UI
   - verify unauthorized or cross-tenant access is blocked

5. If migrations/schema changes were required, ensure they are included and the app still builds/tests cleanly.

# Risks and follow-ups
- The repo may already contain partial simulation/anomaly functionality under different names; avoid duplicating it.
- Backend contracts may not yet expose all fields required by the UI, especially scenario metadata and generated-record counts.
- Long-running progression runs may need polling or refresh behavior; if no real-time mechanism exists, implement a simple refresh/polling approach consistent with current patterns.
- Finance record linking depends on existing routing and record identifiers; if route helpers are missing, add the smallest safe link-generation path.
- If warning/failure payloads are loosely structured today, normalize them enough for reliable UI rendering without broad backend redesign.
- If this task reveals missing domain support for progression history persistence, implement the minimal persistence/query path needed now and note any richer audit/history enhancements as follow-up work.