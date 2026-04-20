# Goal
Implement backlog task **TASK-18.2.1** for story **US-18.2 ST-FUI-202 — Finance recommendation details and invoice workflow history**.

Add an invoice detail UI experience in the **Blazor Web app** that displays:

1. A **structured recommendation details panel** sourced from backend finance output models.
2. A **workflow history timeline** for the invoice, rendered from backend-provided structured events.

The implementation must satisfy these outcomes:

- Show recommendation fields:
  - classification
  - risk
  - rationale summary
  - confidence
  - recommended action
  - current workflow status
- Render explanations only from **structured backend fields**
  - never expose raw chain-of-thought
  - never expose internal prompt text
- Show workflow history entries for:
  - review
  - approval
  - rejection
  - task execution
  - tool events
  when present in backend data
- Each timeline item shows:
  - event type
  - actor or system source
  - timestamp
  - link to related audit or approval detail when identifier exists
- Sort timeline entries in **descending chronological order**
- Deduplicate entries by **event id**
- If recommendation/history data is missing, show a **non-blocking empty state**
  while preserving the base invoice detail experience

Follow existing project conventions and keep the implementation tenant-safe, UI-focused, and aligned with the architecture’s audit/explainability constraints.

# Scope
In scope:

- Invoice detail page updates in `VirtualCompany.Web`
- Query/view-model wiring needed to supply structured recommendation and workflow history data to the page
- Mapping from backend/domain/application DTOs into UI-safe display models
- Empty states for missing recommendation/history data
- Timeline sorting and deduplication in the UI or application-facing mapper layer
- Safe rendering of explanation/rationale summary from approved structured fields only
- Links to related audit/approval detail pages when IDs are available

Out of scope unless required by compilation:

- New AI generation logic
- Any backend storage redesign
- Raw prompt/LLM transcript exposure
- Major invoice workflow engine changes
- Mobile app changes
- Broad redesign of invoice pages unrelated to this task
- New audit/approval detail pages beyond linking to existing routes/placeholders

If backend query contracts for invoice detail do not yet expose the needed structured fields, add the minimum application/API support necessary, but do not over-engineer.

# Files to touch
Inspect the solution first, then update the most relevant existing files. Likely areas:

- `src/VirtualCompany.Web/**`
  - invoice detail page/component
  - related Razor components for panels/timelines
  - page-specific view models
- `src/VirtualCompany.Application/**`
  - invoice detail query/handler
  - DTOs/read models for finance recommendation details and workflow history
- `src/VirtualCompany.Api/**`
  - invoice detail endpoint contract if the web app consumes API DTOs
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums if already used for invoice detail transport
- `tests/VirtualCompany.Api.Tests/**`
  - API/query tests if endpoint behavior changes
- Any existing component test project or web test location if present

Before editing, locate the actual invoice-related files and prefer extending existing invoice detail flows over creating parallel ones.

# Implementation plan
1. **Discover the current invoice detail flow**
   - Find the invoice detail page/component in `VirtualCompany.Web`
   - Trace where its data comes from:
     - direct application service
     - API client
     - shared DTO
     - page model/view model
   - Identify any existing finance recommendation models, audit models, approval models, or workflow event DTOs

2. **Define or extend structured UI-safe models**
   - Ensure the invoice detail data contract includes a recommendation details object with fields for:
     - `Classification`
     - `Risk`
     - `RationaleSummary`
     - `Confidence`
     - `RecommendedAction`
     - `CurrentWorkflowStatus`
   - Ensure workflow history is represented as structured entries with:
     - `EventId`
     - `EventType`
     - `ActorDisplayName` or `SourceDisplayName`
     - `OccurredAt`
     - `RelatedAuditId` and/or `RelatedApprovalId`
   - If needed, add a page-facing display model that is explicitly safe for UI rendering
   - Do **not** include raw prompt text, hidden reasoning, or chain-of-thought fields in any UI model

3. **Map backend finance output models into invoice detail DTOs**
   - Reuse existing finance output models where available
   - Add mapping logic in the application layer or web mapping layer so the UI receives only approved structured fields
   - If source models contain unsafe fields, ignore them explicitly
   - Prefer a mapper/helper with clear naming indicating “summary” or “display” semantics

4. **Implement recommendation details panel**
   - Add a dedicated section on the invoice detail page
   - Render:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - current workflow status
   - Use existing design system/components/styles where possible
   - Keep the panel readable and resilient to partial data
   - If recommendation data is absent, show a non-blocking empty state such as:
     - “No recommendation details available yet.”
   - Ensure the base invoice record remains fully accessible

5. **Implement workflow history timeline**
   - Add a timeline section to the invoice detail page
   - Accept backend-provided events and normalize them for display
   - Include supported event categories:
     - review
     - approval
     - rejection
     - task execution
     - tool events
   - For each entry, show:
     - event type label
     - actor/system source
     - timestamp
     - related detail link when identifier exists
   - Build links only when IDs are present and routes are known
   - If no history exists, show a non-blocking empty state such as:
     - “No workflow history available yet.”

6. **Sort and deduplicate timeline entries**
   - Deduplicate by `EventId`
   - Sort descending by timestamp
   - Prefer doing this in one deterministic place:
     - application query projection, or
     - web view-model mapper
   - Handle null/empty IDs carefully:
     - only deduplicate by event id when present
     - do not accidentally collapse distinct events with missing IDs unless existing conventions require it

7. **Preserve explainability constraints**
   - Audit the rendered fields and confirm the UI only uses:
     - rationale summary
     - structured recommendation fields
     - structured workflow event metadata
   - Do not render:
     - raw model output blobs
     - prompt text
     - hidden reasoning
     - internal tool request payloads unless already approved for user-facing display
   - If existing DTOs expose unsafe fields, avoid binding them and consider tightening the contract

8. **Add minimal tests**
   Add tests at the most appropriate layer available in the repo:
   - Query/handler/API tests for invoice detail response shape
   - Mapper tests for:
     - recommendation field projection
     - timeline descending sort
     - duplicate event removal by event id
     - empty-state-compatible null handling
   - If UI/component tests exist, add coverage for:
     - recommendation panel rendering
     - empty state rendering
     - timeline link rendering when IDs exist

9. **Keep implementation incremental and consistent**
   - Reuse existing invoice detail layout and styling patterns
   - Avoid introducing a new architecture path
   - Keep naming aligned with finance/audit/workflow terminology already present in the codebase

# Validation steps
1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Manual verification in the web app**
   Validate an invoice detail page with:
   - full recommendation data
   - full workflow history data
   - missing recommendation data
   - missing history data
   - duplicate history events
   - mixed event types

3. **Acceptance criteria checklist**
   Confirm:
   - Recommendation section shows:
     - classification
     - risk
     - rationale summary
     - confidence
     - recommended action
     - current workflow status
   - Explanations come from structured fields only
   - No raw chain-of-thought or prompt text is displayed
   - Timeline includes supported event types when provided
   - Each entry shows type, actor/source, timestamp, and related link when available
   - Timeline is sorted newest first
   - Duplicate event IDs are not rendered twice
   - Missing recommendation/history shows non-blocking empty states
   - Base invoice detail remains usable

4. **Code quality checks**
   - Ensure null-safe rendering
   - Ensure tenant-scoped data flow is preserved
   - Ensure links do not break when related IDs are absent
   - Ensure no unsafe backend fields are accidentally serialized to the UI

# Risks and follow-ups
- **Risk: invoice detail contracts may not yet expose recommendation/history data**
  - Mitigation: add the smallest possible application/API extension to support the UI

- **Risk: existing backend models may contain unsafe explanation fields**
  - Mitigation: create explicit UI-safe DTOs and map only approved summary fields

- **Risk: timeline route targets for audit/approval detail may be missing or inconsistent**
  - Mitigation: link only when a known route exists; otherwise render plain text without blocking the page

- **Risk: duplicate events may appear with null or inconsistent IDs**
  - Mitigation: dedupe strictly on non-empty event IDs and document any remaining backend data quality issue

- **Risk: event timestamps may have mixed nullability/timezone handling**
  - Mitigation: normalize display formatting and sort using a consistent UTC-aware value where available

Follow-up items to note in your final implementation summary if encountered:

- any backend gaps discovered in finance recommendation output models
- any missing audit/approval detail routes
- any data quality issues in workflow event IDs or timestamps
- any opportunities to extract the recommendation panel/timeline into reusable shared invoice components