# Goal
Implement backlog task **TASK-18.2.2 — Build invoice workflow history timeline component with audit and approval deep links** for story **US-18.2 ST-FUI-202 — Finance recommendation details and invoice workflow history**.

Deliver a production-ready invoice detail UI enhancement in the **Blazor Web App** that:

- Adds a **recommendation details** section to the invoice detail experience
- Renders recommendation explanations from **structured backend fields only**
- Adds a **workflow history timeline** for invoice-related events
- Supports **deep links** to related audit and approval detail views when identifiers are present
- Sorts timeline entries in **descending chronological order**
- Deduplicates entries by **event id**
- Shows a **non-blocking empty state** when recommendation/history data is unavailable, while preserving the base invoice record view

Do **not** expose raw chain-of-thought, prompt text, or any internal LLM reasoning artifacts.

# Scope
In scope:

- Web UI changes in the invoice detail page and supporting components
- Application/query contract updates needed to supply recommendation details and workflow history to the web layer
- Mapping from backend DTO/domain response into UI-friendly view models
- Timeline rendering for these event categories when present:
  - review
  - approval
  - rejection
  - task execution
  - tool events
- Deep-link generation for:
  - audit detail pages
  - approval detail pages
- Empty-state handling for missing recommendation/history data
- Sorting and deduplication logic in a deterministic, testable place
- Tests covering rendering logic, ordering, deduplication, and safe explanation rendering

Out of scope unless required by existing code structure:

- New invoice domain model creation from scratch
- New approval or audit detail pages if routes already exist
- Backend generation of recommendation content beyond shaping existing structured fields
- Any display of raw model prompts, hidden rationale, or internal orchestration traces
- Mobile app changes

Assumptions to verify in the codebase before implementation:

- There is already an invoice detail page or invoice details feature area
- There are existing audit and approval detail routes, or at minimum route patterns to target
- Recommendation/history data may already exist in API/application contracts but may need extension
- The app follows CQRS-lite patterns across Application/API/Web

# Files to touch
Touch only the minimum required files after inspecting the existing implementation. Likely areas:

- `src/VirtualCompany.Web/...`  
  - Invoice detail page/component
  - New recommendation details component
  - New workflow history timeline component
  - Supporting view models / mappers
- `src/VirtualCompany.Application/...`  
  - Invoice detail query/handler
  - DTOs/contracts for recommendation details and workflow history entries
- `src/VirtualCompany.Api/...`  
  - Endpoint response shaping if the web app consumes API DTOs directly
- `src/VirtualCompany.Shared/...`  
  - Shared contracts only if this solution uses shared DTOs between layers
- `tests/...`  
  - Web/component tests if present
  - Application/query tests for sorting/deduplication/mapping behavior
  - API contract tests if applicable

Before editing, locate the concrete files for:

- invoice detail page
- invoice query/DTO
- audit detail route
- approval detail route
- any existing timeline/history UI patterns that should be reused for consistency

# Implementation plan
1. **Inspect existing invoice detail flow**
   - Find the invoice detail page/component in `src/VirtualCompany.Web`
   - Trace where its data comes from:
     - direct API call
     - application query
     - shared DTO
   - Identify current invoice detail sections and extension points
   - Find any existing recommendation/explainability UI patterns and timeline components elsewhere in the app

2. **Define/extend the invoice detail data contract**
   - Add or extend structured fields for recommendation details:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - current workflow status
   - Add a workflow history collection with fields such as:
     - event id
     - event type
     - actor/system source display name
     - timestamp
     - audit id or audit reference
     - approval id or approval reference
     - optional task/tool metadata if already available
   - Keep naming explicit and user-facing
   - Ensure no raw reasoning/prompt fields are included in the UI contract

3. **Implement safe explanation rendering**
   - Render recommendation explanations only from structured backend fields
   - If backend currently exposes broader explanation payloads, map only approved fields into the UI DTO/view model
   - Do not bind raw JSON blobs, prompt text, hidden rationale, or chain-of-thought fields
   - Prefer a dedicated view model that whitelists safe fields rather than passing through backend objects directly

4. **Build the recommendation details section**
   - Add a focused UI section on the invoice detail page
   - Display:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - current workflow status
   - Use concise labels and existing design system styles/components where available
   - Handle partial data gracefully:
     - show only available fields
     - if the whole recommendation block is absent, show a non-blocking empty state

5. **Build the workflow history timeline component**
   - Create a reusable component for invoice workflow history if one does not already exist
   - Render one row/item per event with:
     - event type
     - actor or system source
     - timestamp
     - deep link to related audit or approval detail when identifier exists
   - Support event categories from backend data:
     - review
     - approval
     - rejection
     - task execution
     - tool events
   - Use clear visual hierarchy and accessible markup

6. **Implement sorting and deduplication**
   - Deduplicate by `event id`
   - Sort descending by timestamp after deduplication
   - Put this logic in a deterministic mapper/helper/view-model factory, not inline in Razor markup
   - Define behavior for edge cases:
     - null/empty event ids: preserve entries unless product conventions dictate otherwise
     - null timestamps: place after valid timestamps, unless existing app behavior differs
   - Keep implementation stable and testable

7. **Add deep-link generation**
   - For each timeline entry:
     - if `audit id` exists, render audit detail link
     - if `approval id` exists, render approval detail link
   - Reuse existing route helpers/constants if present
   - If both exist, either:
     - render both links, or
     - follow existing UX conventions in the app
   - If neither exists, render no link and keep the entry readable

8. **Add empty states**
   - If recommendation data is unavailable:
     - show a non-blocking empty state in that section
   - If history data is unavailable or empty:
     - show a non-blocking empty state in the timeline section
   - Ensure the base invoice record/details remain fully accessible and unaffected

9. **Testing**
   - Add tests for:
     - recommendation section renders only structured safe fields
     - timeline sorts descending by timestamp
     - duplicate event ids are rendered once
     - supported event types render correctly
     - deep links appear only when identifiers are available
     - empty states render without blocking invoice details
   - Prefer unit tests for mapping/sorting/deduplication and component tests for rendering if the project supports them

10. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - Web for presentation
     - Application for query shaping
     - API for transport
   - Avoid leaking infrastructure/domain internals into Razor components
   - Keep explainability operational and concise per ST-602 principles

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Manual validation in the web app:
   - Open an invoice detail page with full recommendation and history data
   - Confirm recommendation section shows:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - current workflow status
   - Confirm no raw prompt text, chain-of-thought, or internal reasoning appears anywhere

3. Timeline validation:
   - Confirm entries render for available backend event types:
     - review
     - approval
     - rejection
     - task execution
     - tool events
   - Confirm each entry shows:
     - event type
     - actor/system source
     - timestamp
     - related link when identifier exists
   - Confirm entries are sorted newest-first
   - Confirm duplicate event ids are not shown twice

4. Deep-link validation:
   - Click audit links and verify they navigate to the correct audit detail route
   - Click approval links and verify they navigate to the correct approval detail route
   - Confirm entries without identifiers do not show broken/empty links

5. Empty-state validation:
   - Test invoice detail with:
     - no recommendation data
     - no history data
     - neither recommendation nor history data
   - Confirm empty states are visible and non-blocking
   - Confirm base invoice details still render normally

6. Regression validation:
   - Verify existing invoice detail functionality is unchanged outside the new sections
   - Verify no tenant-scoping or authorization assumptions were broken in the query path

# Risks and follow-ups
- **Route uncertainty:** Audit/approval detail routes may not yet exist or may use different parameter conventions. Inspect before implementing links.
- **Data contract mismatch:** Backend may not currently provide normalized workflow history events. If so, add a shaping layer in Application rather than pushing transformation into the UI.
- **Unsafe explanation leakage:** Existing DTOs may include raw explanation payloads. Use explicit whitelisting to avoid accidental exposure.
- **Duplicate semantics:** If event ids are missing for some backend events, deduplication rules may need refinement. Document chosen behavior in tests.
- **Timestamp consistency:** Mixed timezone or null timestamp handling can affect ordering. Normalize and test carefully.
- **UI consistency:** There may already be a shared timeline or detail-card pattern elsewhere in the app; reuse it if available to avoid visual drift.
- **Follow-up opportunity:** If similar history timelines exist for tasks/workflows/approvals, consider extracting a reusable generic timeline component after this task is complete, but do not over-generalize prematurely.