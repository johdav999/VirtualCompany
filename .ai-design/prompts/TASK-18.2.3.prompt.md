# Goal

Implement **TASK-18.2.3: Add client-side normalization to suppress duplicate history events and enforce sort order** for the invoice detail experience in the Blazor web app.

This task is part of **US-18.2 ST-FUI-202 — Finance recommendation details and invoice workflow history**.

The coding agent should update the invoice detail UI pipeline so that workflow/recommendation history data received from backend APIs is normalized on the client before rendering, specifically to ensure:

- timeline/history entries are sorted in **descending chronological order**
- duplicate events with the same **event id** are rendered only once
- the UI remains resilient when recommendation/history data is missing or partially populated
- recommendation explanations continue to use only structured backend fields and never expose raw chain-of-thought or internal prompt text

Do not redesign the feature or introduce backend contract changes unless absolutely required to compile existing code. Prefer a focused client-side implementation in the web layer.

# Scope

In scope:

- Locate the existing invoice detail page, view model, DTO mapping, or component(s) responsible for:
  - recommendation details display
  - workflow history/timeline rendering
- Add a **client-side normalization step** for history/timeline data before binding/rendering
- Ensure normalization:
  - removes duplicates by stable event identifier
  - sorts by timestamp descending
  - handles null/empty/malformed collections safely
- Preserve rendering of supported event categories when present:
  - review
  - approval
  - rejection
  - task execution
  - tool events
- Ensure each rendered entry still exposes:
  - event type
  - actor/system source
  - timestamp
  - related audit/approval link when identifier exists
- Preserve or add a non-blocking empty state when recommendation/history data is unavailable while keeping the base invoice record accessible
- Add or update tests for normalization behavior

Out of scope:

- Backend schema or API redesign
- New persistence or migration work
- Mobile app changes unless shared UI/view-model code requires it
- Broad UX restyling unrelated to normalization
- Introducing raw LLM explanation fields into the UI

# Files to touch

Likely areas to inspect and update:

- `src/VirtualCompany.Web/**`
  - invoice detail page/component
  - finance/invoice recommendation details component
  - workflow history/timeline component
  - client-side mapping/view-model helpers
  - shared UI models used by invoice detail rendering
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts if the web app consumes shared response models
- `src/VirtualCompany.Application/**`
  - only if a query/view model is shared directly with the web app and a safe non-breaking helper belongs there
- `tests/**`
  - web/component/unit tests for normalization and rendering behavior
  - possibly API/application tests only if existing tests assert ordering and need adjustment

Before editing, identify the exact files in the repo that currently implement invoice detail recommendation/history rendering and list them in your work log / summary.

# Implementation plan

1. **Discover the current invoice detail flow**
   - Search for invoice detail pages/components and any models related to:
     - invoice detail
     - recommendation details
     - workflow history
     - timeline
     - audit/approval links
   - Identify where backend response data is transformed for UI use.
   - Prefer implementing normalization at the closest shared client-side mapping layer so all render paths benefit.

2. **Understand the current contracts**
   - Inspect the DTO/view model fields for history events.
   - Determine the available fields for:
     - unique event id
     - event type/category
     - actor/source
     - timestamp
     - audit id / approval id / related entity id
   - If multiple possible identifiers exist, use the canonical event id field first.
   - Do not invent new API fields if existing ones are sufficient.

3. **Add a client-side normalization helper**
   - Create or update a dedicated normalization method/function for invoice workflow history.
   - Expected behavior:
     - accept null/empty input safely
     - filter out null entries if applicable
     - deduplicate by event id
     - sort descending by timestamp
     - return a stable collection suitable for rendering
   - Deduplication guidance:
     - use event id as the primary key
     - if event id is null/empty, keep the item rather than dropping it unless current product conventions say otherwise
     - for duplicate ids, keep the best representative deterministically:
       - prefer the entry with the most complete data if easy to determine, otherwise keep the first occurrence after a pre-sort by timestamp descending
   - Sorting guidance:
     - sort by timestamp descending
     - handle null timestamps deterministically, placing null/unknown timestamps last
     - use a stable secondary ordering if needed to avoid flicker

4. **Integrate normalization into the invoice detail UI**
   - Ensure the timeline component/page binds to the normalized collection, not raw backend data.
   - Keep recommendation details rendering separate from history normalization.
   - Confirm recommendation explanation fields are sourced only from structured fields such as:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - workflow status
   - Do not render any raw prompt text, hidden reasoning, or chain-of-thought fields even if present on a DTO.

5. **Preserve empty-state behavior**
   - If recommendation data is missing:
     - show a non-blocking empty state for that section
   - If history data is missing or normalizes to an empty collection:
     - show a non-blocking empty state for the timeline
   - Ensure the base invoice detail record still renders normally.

6. **Preserve links and event metadata**
   - For each normalized timeline entry, ensure the UI still shows:
     - event type
     - actor or system source
     - timestamp
     - link to related audit or approval detail when an identifier is available
   - Do not remove existing link behavior during refactor.

7. **Add tests**
   - Add focused tests around the normalization helper and/or rendered component behavior.
   - Minimum scenarios:
     - duplicate events with same id render once
     - events are sorted newest to oldest
     - null/empty history yields empty state without breaking invoice detail page
     - events without optional link identifiers still render without link
     - null timestamps sort last if such values are possible in the model
   - Prefer small deterministic unit tests over brittle snapshot tests.

8. **Keep implementation minimal and idiomatic**
   - Follow existing project patterns for:
     - Blazor components
     - shared models
     - helper/extension placement
     - test conventions
   - Avoid unnecessary abstractions if a small mapper/helper is enough.

9. **Document assumptions in code comments only where needed**
   - If dedupe behavior for duplicate ids requires a tie-break rule, add a concise comment explaining it.
   - Do not add noisy comments.

# Validation steps

Run the relevant validation locally after changes:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for web/UI/component behavior, run those specifically first if helpful.

4. Manually verify in code or local app run that:
   - invoice detail page still loads when recommendation/history is absent
   - duplicate history events with the same event id are not rendered twice
   - timeline order is descending by timestamp
   - audit/approval links still appear when identifiers are present
   - recommendation details still use structured explanation fields only

If the app can be run locally, exercise a sample invoice detail route with:
- unsorted history data
- duplicate event ids
- missing recommendation/history payloads

# Risks and follow-ups

- **Risk: event id semantics may be inconsistent**
  - Some backend events may lack ids or reuse ids across event shapes.
  - Mitigation: dedupe only when a non-empty canonical event id exists; otherwise retain the event.

- **Risk: timestamps may be nullable or inconsistently formatted**
  - Mitigation: normalize using the typed timestamp field if available; place null/invalid timestamps last.

- **Risk: normalization may be duplicated in multiple UI paths**
  - Mitigation: centralize in one mapper/helper used by the invoice detail timeline.

- **Risk: existing tests may implicitly depend on backend order**
  - Mitigation: update tests to assert normalized client behavior instead of raw payload order.

- **Risk: recommendation rendering could accidentally expose raw explanation fields**
  - Mitigation: explicitly verify only approved structured fields are bound in the UI.

Follow-ups to note in your summary if discovered but not implemented:
- backend should ideally return already deduplicated/sorted history for consistency across clients
- shared normalization logic may later be extracted if mobile or other clients render the same timeline
- if event ids are frequently missing, a future contract improvement may be needed for stronger dedupe guarantees