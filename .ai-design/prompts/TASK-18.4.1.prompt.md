# Goal
Implement backlog task **TASK-18.4.1 — Add cash position and runway widgets to the executive cockpit with finance data adapters** for story **US-18.4 ST-FUI-204**.

Deliver a vertical slice across web UI, application/query layer, finance data adapters, authorization/policy gating, and deep-link/action wiring so that the executive cockpit can surface finance-specific cash health information and finance workflow entry points.

The implementation must satisfy these outcomes:

- Show a **cash position widget** in the executive cockpit with:
  - current value
  - trend indicator
  - last refreshed timestamp
- Show a **runway widget/visualization** with:
  - current runway estimate
  - threshold-based status styling for `healthy`, `warning`, and `critical`
- Support **low-cash alerts** opening a finance-specific detail panel or page with:
  - alert summary
  - contributing factors
  - links to detailed finance views
- Add **deep links** from cockpit finance widgets to:
  - finance workspace
  - anomaly workbench
  - cash detail page
- Add explicit finance actions:
  - `Review invoice`
  - `Inspect anomaly`
  - `View cash position`
  - `Open finance summary`
- Ensure finance action entry points are shown only when:
  - the current user passes role checks
  - the current user passes policy checks
- Ensure action triggers call **existing backend orchestration endpoints**, not new bespoke execution paths.

# Scope
In scope:

- Executive cockpit finance widget UI in the Blazor web app
- Query/API support to populate cockpit finance widgets
- Finance adapter/service layer to normalize finance data for cockpit consumption
- Threshold/status mapping for runway health
- Alert detail panel/page for low-cash alerts
- Role/policy-gated finance action entry points
- Deep-link routing to finance destinations
- Wiring action triggers to existing orchestration/backend endpoints
- Tests for query mapping, authorization gating, and UI rendering logic

Out of scope unless required by existing patterns:

- New finance backend systems of record
- New orchestration engine behavior beyond invoking existing endpoints/contracts
- Mobile implementation
- Large redesign of the executive cockpit layout
- New generic authorization framework if one already exists
- Replacing existing alert infrastructure rather than extending it

Use existing architecture and conventions in the repo. Prefer extending current cockpit, alerts, finance, and orchestration patterns over introducing parallel abstractions.

# Files to touch
Inspect the solution first and then update the most relevant existing files. Expected areas include:

- `src/VirtualCompany.Web/**`
  - executive cockpit/dashboard pages and components
  - alert detail panel/page components
  - shared widget/card components
  - route/deep-link definitions
  - UI authorization helpers if present
- `src/VirtualCompany.Api/**`
  - cockpit/analytics/finance endpoints or controllers
  - alert detail endpoints if server-backed
  - orchestration action proxy endpoints only if needed to align with existing contracts
- `src/VirtualCompany.Application/**`
  - cockpit queries/view models
  - finance adapter interfaces and handlers
  - authorization/policy evaluation for finance actions
  - deep-link DTO shaping
- `src/VirtualCompany.Domain/**`
  - finance widget models/value objects/enums
  - runway status enum
  - alert detail models if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - finance data adapter implementations
  - repository/query implementations
  - integration normalization for finance metrics
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts used by web/api if applicable
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query authorization and response tests
- Other test projects if present for application/web components

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Only add migrations if persistence changes are truly required. Prefer deriving cockpit finance widgets from existing normalized finance/workflow data.

# Implementation plan
1. **Discover existing cockpit, finance, alerts, and orchestration patterns**
   - Find the executive cockpit/dashboard entry page and current widget composition.
   - Find existing finance-related modules, DTOs, queries, alerts, anomaly workbench routes, and orchestration endpoints.
   - Find current role/policy authorization approach:
     - ASP.NET Core policies
     - membership role checks
     - feature/action gating helpers
   - Find existing alert detail UX pattern:
     - drawer
     - modal
     - dedicated page
   - Reuse established conventions.

2. **Define the finance cockpit read model**
   - Introduce or extend a cockpit finance view model that includes:
     - `CashPosition`
       - amount
       - currency
       - trend direction/value
       - last refreshed at
       - deep link
     - `Runway`
       - estimated months/days
       - status enum: `Healthy`, `Warning`, `Critical`
       - threshold metadata if useful
       - deep link
     - `LowCashAlert`
       - alert id
       - summary
       - severity/status
       - contributing factors
       - destination links
     - `AvailableActions`
       - review invoice
       - inspect anomaly
       - view cash position
       - open finance summary
       - each with visibility/enabled state and target/orchestration metadata
   - Keep the model query-oriented and UI-friendly.

3. **Implement finance data adapter(s)**
   - Add or extend an application/infrastructure adapter that maps normalized finance/workflow data into cockpit finance metrics.
   - The adapter should provide:
     - current cash position
     - trend indicator data
     - last refresh timestamp
     - runway estimate
     - low-cash alert summary and contributing factors
   - If multiple sources exist, compose them behind a single application-facing interface.
   - Keep adapters tenant-scoped and deterministic.
   - Do not let UI assemble finance logic directly.

4. **Implement runway status classification**
   - Add a clear status mapping function for runway thresholds:
     - `Healthy`
     - `Warning`
     - `Critical`
   - Prefer configurable thresholds if an existing settings/policy source already exists.
   - If no existing threshold source exists, implement sensible application defaults and isolate them in one place.
   - Ensure styling can bind directly to the status enum/string.

5. **Extend cockpit query/API**
   - Add or extend the executive cockpit query/endpoint to return finance widget data.
   - Ensure tenant scoping is enforced.
   - Include only action entry points the current user is allowed to see.
   - Populate deep-link targets for:
     - finance workspace
     - anomaly workbench
     - cash detail page
   - Keep command/action execution separate from read models.

6. **Implement role and policy gating for finance actions**
   - For each finance action entry point, evaluate:
     - membership role eligibility
     - policy eligibility
     - any finance-specific workflow/action constraints
   - Hide actions entirely when the user does not satisfy checks, per acceptance criteria.
   - Reuse existing backend policy/orchestration checks where possible.
   - Avoid duplicating business rules in the UI; UI may consume server-computed visibility flags, with optional client-side defensive checks.

7. **Wire finance actions to existing orchestration endpoints**
   - Each action trigger must call existing backend orchestration endpoints/contracts.
   - Map actions:
     - `Review invoice`
     - `Inspect anomaly`
     - `View cash position`
     - `Open finance summary`
   - If the existing endpoint expects a generic action/task/orchestration request, use that.
   - Do not create custom one-off execution pipelines unless absolutely necessary to adapt to existing contracts.
   - Preserve correlation/audit metadata if the current system supports it.

8. **Build the cockpit widgets in Blazor**
   - Add a finance section/widget area to the executive cockpit.
   - Cash position widget must show:
     - formatted value
     - trend indicator
     - last refreshed timestamp
   - Runway widget must show:
     - runway estimate
     - status styling for healthy/warning/critical
   - Add clear CTA/deep-link affordances.
   - Keep styling aligned with existing cockpit cards/widgets.

9. **Implement low-cash alert detail experience**
   - Add a finance-specific detail panel or page for low-cash alerts.
   - It must show:
     - alert summary
     - contributing factors
     - links to detailed finance views
   - Ensure cockpit alerts navigate/open correctly.
   - Reuse existing alert detail shell if available.

10. **Add deep-link routing**
    - Ensure finance widgets and alert details can navigate to:
      - finance workspace
      - anomaly workbench
      - cash detail page
    - Use existing route constants/helpers if present.
    - Avoid hardcoded URLs scattered across components.

11. **Testing**
    - Add application tests for:
      - finance adapter mapping
      - runway threshold classification
      - action visibility gating
    - Add API tests for:
      - tenant scoping
      - authorized vs unauthorized action visibility
      - response shape for cockpit finance data
    - Add UI/component tests if the repo already uses them.
    - At minimum verify:
      - cash widget renders expected fields
      - runway status styling class selection is correct
      - low-cash alert opens detail destination
      - hidden actions are not rendered for unauthorized users

12. **Implementation constraints**
    - Follow existing solution layering:
      - Web/UI only for presentation
      - Application for use cases/query shaping
      - Infrastructure for adapters/data access
      - Domain for stable enums/value objects
    - Keep multi-tenant enforcement intact.
    - Prefer additive, low-risk changes.
    - Do not expose raw chain-of-thought or internal reasoning in alert details or finance summaries.
    - Keep timestamps, currency formatting, and trend display consistent with existing dashboard conventions.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full validation:
   - `dotnet build`
   - `dotnet test`

4. Manually verify in the web app:
   - Open executive cockpit
   - Confirm finance cash position widget appears with:
     - value
     - trend indicator
     - last refreshed timestamp
   - Confirm runway widget appears with:
     - estimate
     - healthy/warning/critical styling
   - Trigger or load a low-cash alert and confirm it opens a finance detail panel/page with:
     - summary
     - contributing factors
     - finance links
   - Confirm widget deep links navigate correctly to:
     - finance workspace
     - anomaly workbench
     - cash detail page
   - Confirm finance actions appear only for users with valid role/policy access
   - Confirm action triggers call existing orchestration endpoints and succeed/fail through normal backend handling

5. Validate authorization scenarios:
   - finance-authorized user sees allowed actions
   - non-finance or insufficient-policy user does not see restricted actions
   - cross-tenant access is denied or returns not found/forbidden per existing conventions

6. Validate edge cases:
   - no finance data yet
   - stale refresh timestamp
   - missing trend data
   - runway unavailable
   - no active low-cash alerts
   - policy denies action even when widget data is visible

# Risks and follow-ups
- **Repo structure mismatch risk:** actual cockpit/finance modules may use different naming or placement than expected. Adapt to existing conventions rather than forcing new structure.
- **Authorization duplication risk:** avoid implementing separate UI-only finance permission logic if server-side policy evaluation already exists.
- **Data availability risk:** finance runway/cash metrics may not yet exist in normalized form. If so, add the thinnest adapter possible and document any assumptions.
- **Threshold ambiguity risk:** runway healthy/warning/critical thresholds may not be defined yet. Centralize defaults and note them for product confirmation.
- **Deep-link destination risk:** finance workspace, anomaly workbench, or cash detail routes may be incomplete. Reuse existing routes where available and add TODOs only if a destination truly does not exist.
- **Action contract risk:** existing orchestration endpoints may require specific payload shapes. Reuse current request contracts and avoid inventing new action semantics.
- **UI consistency risk:** finance widgets should match existing cockpit card patterns and not introduce a disconnected visual language.

Follow-ups to note in code comments or task notes if encountered:
- externalize runway thresholds to tenant/company finance settings
- add richer trend history sparkline if product wants more than indicator-only
- add caching for finance cockpit aggregates if query cost is high
- extend mobile companion once web behavior is stable