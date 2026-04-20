# Goal
Implement backlog task **TASK-22.1.1 — Create FocusItem model and implement IFocusEngine with cross-domain scoring and normalization** for story **US-22.1 Implement Today’s Focus aggregation API and primary decision panel**.

Deliver the backend foundation for a tenant-scoped “Today’s Focus” experience that aggregates actionable items across multiple domains and returns **3 to 5 normalized `FocusItem` records** ordered by descending priority.

The implementation must satisfy these acceptance criteria:

- `GET /api/dashboard/focus` returns **3 to 5** `FocusItem` records for a valid `companyId` and `userId`, ordered by `PriorityScore` descending.
- Each `FocusItem` includes non-empty:
  - `Id`
  - `Title`
  - `Description`
  - `ActionType`
  - `PriorityScore`
  - `NavigationTarget`
- `PriorityScore` is an **integer from 0 to 100** for every returned item.
- Aggregation includes items from:
  - approvals
  - tasks
  - anomalies
  - finance alerts
  when data exists in those domains.
- The web `TodayFocusPanel` renders returned items as cards with title, short description, and CTA button.
- Clicking CTA navigates to the exact `NavigationTarget` returned by the API.

Work within the existing modular monolith and keep boundaries clean:
- Domain model in `VirtualCompany.Domain`
- Application contracts/services in `VirtualCompany.Application`
- Infrastructure data access in `VirtualCompany.Infrastructure`
- API endpoint in `VirtualCompany.Api`
- Blazor UI in `VirtualCompany.Web`
- Tests in `tests/VirtualCompany.Api.Tests` and any appropriate application/domain test projects if present

# Scope
Implement only what is required for this task, with pragmatic scaffolding where the broader feature is not yet fully built.

In scope:

1. **Create a `FocusItem` model/DTO contract**
   - Represents the API response shape.
   - Includes source/domain metadata if useful internally, but do not break the required response contract.

2. **Define and implement `IFocusEngine`**
   - Aggregates candidate items from multiple domains.
   - Applies cross-domain scoring.
   - Normalizes scores to `0..100`.
   - Returns top `3..5` items sorted descending.

3. **Cross-domain candidate sourcing**
   - Pull from existing domain data if available.
   - At minimum support approvals and tasks from real persisted data if those modules already exist.
   - For anomalies and finance alerts:
     - integrate with existing entities/services if present
     - otherwise add minimal query abstractions/stubs that safely return empty results without blocking delivery

4. **Normalization and ranking**
   - Raw scoring can differ by source type.
   - Final output must be normalized to integer `0..100`.
   - Ensure deterministic ordering.

5. **Expose API endpoint**
   - `GET /api/dashboard/focus`
   - Tenant/user scoped
   - Accept `companyId` and `userId` according to existing API conventions
   - Return the `FocusItem` list

6. **Wire up TodayFocusPanel**
   - Fetch from API
   - Render cards
   - CTA uses exact `NavigationTarget`

7. **Tests**
   - Cover normalization, ordering, count bounds, and required fields
   - Cover mixed-source aggregation behavior
   - Cover API contract shape

Out of scope unless required by existing code patterns:
- Full anomaly detection implementation
- Full finance alert generation engine
- New persistence schema for alerts unless already needed and lightweight
- Broad dashboard redesign
- Mobile implementation

# Files to touch
Use the actual existing project structure and naming conventions after inspection. Expected areas:

- `src/VirtualCompany.Domain/...`
  - Add any domain enums/value objects/constants needed for focus source types or action types
- `src/VirtualCompany.Application/...`
  - Add `FocusItem` response model/query model
  - Add `IFocusEngine`
  - Add query/handler or service contract for dashboard focus retrieval
  - Add source provider abstractions if needed, e.g. approvals/tasks/anomalies/finance candidate readers
- `src/VirtualCompany.Infrastructure/...`
  - Implement data readers/repositories for focus candidates from existing tables/entities
  - Register DI for `IFocusEngine` and any providers
- `src/VirtualCompany.Api/...`
  - Add or update dashboard controller/endpoint for `GET /api/dashboard/focus`
- `src/VirtualCompany.Web/...`
  - Add/update `TodayFocusPanel` component/page usage
  - Ensure CTA navigation uses returned `NavigationTarget`
- `tests/VirtualCompany.Api.Tests/...`
  - Add endpoint tests
- Any existing application/domain test project if available
  - Add scoring/normalization unit tests

Before editing, inspect the solution for:
- existing dashboard endpoints
- existing task/approval entities and repositories
- existing anomaly/alert concepts
- existing UI panel/component names
- existing API client patterns in Blazor
- existing result wrapper / CQRS / MediatR conventions
- existing tenant authorization patterns

# Implementation plan
1. **Inspect the codebase first**
   - Find existing patterns for:
     - API controllers/endpoints
     - application queries/handlers
     - DI registration
     - repository/query services
     - dashboard widgets/panels
     - navigation in Blazor
   - Reuse existing conventions exactly.

2. **Design the focus contract**
   - Add a `FocusItem` model with required fields:
     - `Id`
     - `Title`
     - `Description`
     - `ActionType`
     - `PriorityScore`
     - `NavigationTarget`
   - Prefer immutable record/DTO style if consistent with the codebase.
   - If useful, include internal-only fields such as:
     - `SourceType`
     - `RawScore`
   - Do not expose extra fields publicly unless consistent and harmless.

3. **Define focus engine abstractions**
   - Add `IFocusEngine` in the application layer.
   - Suggested shape:
     - input: `companyId`, `userId`, optional `CancellationToken`
     - output: `IReadOnlyList<FocusItem>`
   - If the codebase uses CQRS, implement a query + handler and have the handler call `IFocusEngine`.

4. **Model candidate inputs**
   - Internally define a candidate model for aggregation, e.g. `FocusCandidate`, containing:
     - source/domain type
     - source entity id
     - title
     - description
     - action type
     - navigation target
     - raw score inputs / urgency factors
     - timestamp / due date / severity / amount / pending age as available
   - Keep this internal to application/infrastructure.

5. **Implement source readers**
   - Add provider/query abstractions for each source:
     - approvals
     - tasks
     - anomalies
     - finance alerts
   - Each provider returns zero or more `FocusCandidate`s for the given tenant/user.
   - Use existing persisted data where available.
   - If anomalies or finance alerts are not yet implemented in the codebase:
     - create no-op providers returning empty lists
     - structure them so real implementations can be added later without changing `IFocusEngine`

6. **Implement scoring**
   - Create a deterministic raw scoring strategy per source.
   - Example guidance:
     - approvals: pending status, age, threshold/sensitivity, direct assignment to user/role
     - tasks: priority, due date proximity/overdue, blocked status, awaiting approval
     - anomalies: severity, recency, business impact
     - finance alerts: severity, amount/risk, recency
   - Keep scoring simple, explicit, and testable.
   - Avoid hidden heuristics.

7. **Implement normalization**
   - Normalize all selected candidates to integer `0..100`.
   - Requirements:
     - every returned item must be within bounds
     - preserve descending order
     - deterministic for equal scores
   - Recommended approach:
     - aggregate all raw candidates
     - sort by raw score descending, then stable tie-breakers
     - select top candidates
     - normalize using min/max scaling across the selected set or full candidate set
   - Handle edge cases:
     - no candidates => empty list or per endpoint contract if existing behavior dictates
     - one candidate => assign a sensible normalized score, e.g. `100`
     - equal raw scores => assign same normalized score or rank-based score, but keep deterministic
   - Ensure final result count is:
     - max 5
     - min 3 only when at least 3 candidates exist
     - if fewer than 3 candidates exist in the system, return what exists unless existing product behavior requires synthetic fillers; do not invent fake items unless explicitly necessary

8. **Build navigation targets**
   - Map each source type to a concrete route already supported by the web app.
   - Examples only if matching existing routes:
     - approvals -> `/approvals/{id}`
     - tasks -> `/tasks/{id}`
     - anomalies -> `/analytics/anomalies/{id}`
     - finance alerts -> `/finance/alerts/{id}`
   - Use exact route strings returned by the API in the UI CTA.
   - Do not hardcode separate UI navigation logic that diverges from API output.

9. **Expose the API**
   - Add/update `GET /api/dashboard/focus`.
   - Follow existing auth and tenant scoping patterns.
   - Validate `companyId` and `userId` according to current conventions.
   - Return serialized `FocusItem` list ordered by `PriorityScore` descending.

10. **Wire the Blazor panel**
    - Find or create `TodayFocusPanel`.
    - Fetch the endpoint using existing API client/service patterns.
    - Render each item as a card with:
      - title
      - short description
      - CTA button
    - CTA click must navigate to `NavigationTarget` exactly as returned.
    - Keep UI minimal and aligned with current component styling.

11. **Register dependencies**
    - Add DI registrations for:
      - `IFocusEngine`
      - source providers/readers
      - any query services
    - Keep application depending on abstractions, infrastructure on implementations.

12. **Add tests**
    - Unit tests for:
      - normalization range `0..100`
      - descending order
      - deterministic tie handling
      - top 5 cap
      - mixed-source inclusion when providers return data
    - API tests for:
      - endpoint returns expected shape
      - required fields non-empty
      - scores are integers in range
    - UI/component tests if the project already uses them; otherwise keep UI verification lightweight.

13. **Document assumptions in code comments only where necessary**
    - Especially for no-op anomaly/finance providers if those domains are not yet implemented.

# Validation steps
Run and verify using the real solution commands and any targeted test filters as helpful.

1. **Build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Manual/API verification**
   - Start the API and call:
     - `GET /api/dashboard/focus`
   - Verify for a seeded/test tenant-user pair:
     - response count is between 3 and 5 when enough data exists
     - items are ordered by `PriorityScore` descending
     - every item has non-empty:
       - `Id`
       - `Title`
       - `Description`
       - `ActionType`
       - `NavigationTarget`
     - every `PriorityScore` is an integer `0..100`

4. **Cross-domain verification**
   - Seed or use test fixtures with approvals, tasks, anomalies, and finance alerts.
   - Confirm returned items include those domains when data exists.

5. **UI verification**
   - Open the web dashboard.
   - Confirm `TodayFocusPanel` renders cards.
   - Confirm CTA button text/action is present.
   - Click each CTA and verify navigation matches the exact `NavigationTarget` from the API response.

6. **Edge-case verification**
   - No data for tenant/user: verify endpoint behavior is safe and UI empty state does not break.
   - Only one source has data: verify valid output and normalized scores.
   - Equal raw scores: verify deterministic ordering and valid normalization.

# Risks and follow-ups
- **Anomalies and finance alerts may not yet exist as persisted modules**
  - Mitigation: implement provider abstractions with no-op implementations now, and real adapters where entities already exist.
  - Follow-up: replace no-op providers with real domain-backed readers in later tasks.

- **Acceptance criterion says 3 to 5 items, but real data may contain fewer than 3**
  - Mitigation: do not fabricate fake focus items unless the existing product explicitly requires it.
  - Follow-up: clarify whether fallback/synthetic focus items are desired when fewer than 3 actionable records exist.

- **Normalization strategy can unintentionally compress scores**
  - Mitigation: keep the algorithm simple, deterministic, and unit-tested.
  - Follow-up: tune scoring weights after product review with realistic data.

- **Navigation routes may not yet exist for all source types**
  - Mitigation: inspect current web routes before finalizing `NavigationTarget` mapping.
  - Follow-up: add missing detail pages/routes in subsequent tasks if needed.

- **Tenant/user scoping may vary by module**
  - Mitigation: reuse existing authorization and repository filtering patterns; do not bypass module boundaries.

- **UI and API can drift if navigation logic is duplicated**
  - Mitigation: UI must navigate using the API-provided `NavigationTarget` exactly, with no remapping.