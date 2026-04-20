# Goal
Implement TASK-18.3.3 for US-18.3 ST-FUI-203 by wiring backend deduplication metadata into the finance anomaly workbench UI and detail experience, with conditional rendering so anomaly key and detection window are shown only when present and omitted cleanly when absent.

# Scope
Focus only on the UI/data-contract wiring needed for deduplication metadata display and conditional rendering in the finance anomaly workbench flow.

Include:
- Extend anomaly list/detail view models or DTO mappings used by the web app so deduplication metadata from existing anomaly APIs is available to the UI.
- Update anomaly row rendering to show deduplication key and/or detection window when provided.
- Update anomaly detail rendering to show a deduplication metadata section only when backend metadata exists.
- Ensure missing metadata does not render placeholder labels, empty values, null text, or component errors.
- Preserve existing filtering, pagination/infinite loading, navigation, and return-with-filters behavior.
- Add/adjust tests covering present vs absent metadata rendering.

Do not include:
- New backend anomaly generation logic.
- New API endpoints unless absolutely required to expose already-available fields.
- Redesign of anomaly workbench layout beyond what is necessary to display the metadata.
- Changes unrelated to anomaly key/window conditional rendering.

# Files to touch
Inspect the existing implementation first and then update the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Web/...`  
  - Finance anomaly workbench page/component
  - Anomaly row/list item component
  - Anomaly detail page/component
  - UI models/view models used by anomaly pages
  - API client or query service used to fetch anomalies
- `src/VirtualCompany.Shared/...`  
  - Shared contracts/DTOs for anomaly list/detail payloads, if shared between API and Web
- `src/VirtualCompany.Api/...` or `src/VirtualCompany.Application/...`  
  - Only if current API response mapping is dropping deduplication fields that already exist in domain/application responses
- `tests/...`  
  - Web/UI/component tests
  - API contract tests if DTO exposure changes are needed

Before editing, locate:
- anomaly list page
- anomaly detail page
- anomaly DTOs/contracts
- any existing deduplication metadata types
- tests for finance anomaly rendering

# Implementation plan
1. Discover the current anomaly workbench implementation
   - Search for finance anomaly workbench pages, routes, components, and API clients.
   - Identify the route used for the anomaly list and the detail page.
   - Find the DTO/view model currently used for anomaly rows and detail rendering.
   - Confirm whether backend APIs already return deduplication metadata and whether it is currently ignored, unmapped, or unavailable in the web layer.

2. Trace deduplication metadata through the stack
   - Determine the exact backend field names for:
     - anomaly key
     - detection window
     - any parent deduplication metadata object
   - If the API already returns these fields, wire them through shared/web contracts without changing endpoint behavior.
   - If application/API mapping is dropping existing fields, add the minimal mapping needed to expose them.

3. Update contracts/view models
   - Add nullable properties for deduplication metadata to the relevant anomaly list and detail models.
   - Prefer a small dedicated metadata object if the existing API shape already uses one.
   - Keep fields nullable/optional to reflect backend absence.
   - Do not introduce fake defaults like empty strings.

4. Update anomaly list row rendering
   - Render deduplication key when available.
   - Render detection window when available.
   - If both are absent, render nothing for that metadata area.
   - If only one is present, render only that one.
   - Ensure formatting is concise and consistent with the existing row layout.
   - Avoid showing labels with blank values.

5. Update anomaly detail rendering
   - Add a deduplication metadata section that appears only when at least one deduplication field exists.
   - Show:
     - anomaly key when present
     - detection window when present
   - Hide the entire section when both are absent.
   - Ensure null-safe rendering so no exceptions occur during SSR/component rendering.

6. Handle detection window formatting
   - Reuse existing date/time formatting helpers if available.
   - If the backend provides a structured window, format it consistently, e.g. start/end or a human-readable range.
   - If the backend provides a preformatted string, display it directly.
   - Do not invent formatting that conflicts with existing app conventions.

7. Preserve existing workbench behavior
   - Verify no regressions to:
     - filters
     - pagination/infinite loading
     - row selection
     - detail navigation
     - return to workbench with filters intact
   - Keep route/query-string behavior unchanged unless a tiny fix is required for existing state preservation.

8. Add tests
   - Add or update tests for list row rendering:
     - key and window both present
     - only key present
     - only window present
     - neither present
   - Add or update tests for detail rendering:
     - section visible when metadata exists
     - section hidden when metadata absent
     - no placeholder/null text rendered
   - If API/shared contract changes are made, add a serialization/mapping test to ensure deduplication fields flow through correctly.

9. Keep implementation minimal and aligned with existing patterns
   - Follow current naming, component structure, and styling conventions.
   - Prefer extending existing components over creating new abstractions unless the current code clearly benefits from a small reusable metadata partial/component.

# Validation steps
1. Build and test
   - Run:
     - `dotnet build`
     - `dotnet test`

2. Manual verification in the web app
   - Open the finance anomaly workbench route.
   - Verify anomaly rows with deduplication metadata show anomaly key and/or detection window.
   - Verify rows without metadata do not show empty placeholders or broken labels.
   - Open anomaly detail for an item with metadata and confirm the deduplication section appears correctly.
   - Open anomaly detail for an item without metadata and confirm the section is fully hidden.
   - Navigate back to the workbench and confirm current filters remain intact.

3. Contract verification
   - Confirm the network/API payload used by the web app includes deduplication metadata when backend data is present.
   - Confirm null/absent metadata does not break deserialization or rendering.

4. Regression checks
   - Verify existing anomaly fields still render:
     - anomaly type
     - affected record reference
     - explanation summary/explanation
     - confidence
     - recommended action
     - follow-up status/tasks
     - links to invoice/transaction record
   - Verify filtering and pagination/infinite loading still function for datasets over 50 items if test data exists.

# Risks and follow-ups
- Risk: deduplication metadata may exist in domain/application models but not in API contracts, requiring a small cross-layer contract update.
- Risk: list and detail pages may use different DTOs, so wiring one path may not automatically fix the other.
- Risk: detection window shape may be inconsistent across APIs, requiring normalization or defensive formatting.
- Risk: SSR/component tests may be sparse; you may need to add focused rendering tests rather than rely only on end-to-end coverage.

Follow-ups if discovered during implementation:
- Standardize deduplication metadata into a shared contract/type if currently duplicated.
- Add a small formatter/helper for anomaly detection windows if formatting logic is repeated.
- Consider a reusable anomaly metadata component if both list and detail views duplicate the same conditional rendering logic.