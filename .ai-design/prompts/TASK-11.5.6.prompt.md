# Goal
Implement backlog task **TASK-11.5.6 — “Summaries should reference underlying tasks/approvals where possible”** for story **ST-505 Daily briefings and executive summaries**.

Update the daily/weekly summary generation flow so generated executive summaries include structured references to the underlying **tasks** and **approvals** that support each summary item whenever those entities are available and tenant-accessible.

The implementation should fit the existing **.NET modular monolith** architecture, preserve tenant isolation, and avoid exposing raw chain-of-thought. References should be concise, user-facing, and suitable for dashboard/mobile consumption.

# Scope
In scope:
- Identify where daily briefing / executive summary generation is implemented across application, domain, infrastructure, API, and web/mobile presentation layers.
- Extend the summary generation pipeline so summary items can carry references to:
  - task IDs / task titles / task status
  - approval IDs / approval type / approval status
- Prefer structured references in persisted payloads rather than only embedding plain text into summary body text.
- Ensure summaries reference underlying entities **when possible**, but still generate successfully when no references are available.
- Update persistence contracts / DTOs / view models as needed so dashboard and notification surfaces can render linked references.
- Add or update tests covering:
  - summaries with task references
  - summaries with approval references
  - summaries with mixed/no references
  - tenant scoping / authorization-safe behavior

Out of scope unless required by existing design:
- Large UX redesign of briefing pages
- Email delivery
- New notification channels
- Broad audit/explainability redesign outside what is needed for summary references
- Mobile-specific feature expansion beyond consuming the same backend payloads

# Files to touch
Inspect and modify the actual files that implement these concerns. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - summary/message/notification entities or value objects
  - task/approval reference models if they belong in domain contracts

- `src/VirtualCompany.Application/**`
  - daily briefing / executive summary generation handlers/services
  - query models / DTOs for dashboard or inbox retrieval
  - orchestration contracts for summary item composition
  - mapping logic from tasks/approvals into summary references

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - persistence models for messages/notifications/structured payloads
  - repositories or query services used to gather summary source data

- `src/VirtualCompany.Api/**`
  - endpoints returning briefing/summary payloads, if API contracts need updating

- `src/VirtualCompany.Web/**`
  - dashboard / briefing components that render summary items and links
  - any shared models consumed by the UI

- `src/VirtualCompany.Mobile/**`
  - only if it already consumes briefing payloads and requires model alignment

- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts if briefing payloads are defined here

- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for summary retrieval and rendering contracts

Also inspect:
- `README.md`
- any architecture/docs files describing summaries, messages, notifications, or structured payload conventions

Do not invent file paths if the implementation lives elsewhere; first locate the existing ST-505 code path and work within established patterns.

# Implementation plan
1. **Locate the current ST-505 implementation**
   - Find the scheduled daily/weekly summary generation flow.
   - Identify:
     - where summaries are composed
     - how they are persisted (messages, notifications, or both)
     - what structured payload shape already exists
     - how dashboard/mobile retrieve and render summaries

2. **Understand current summary item model**
   - Determine whether summaries are currently:
     - plain text only
     - markdown/body plus JSON payload
     - structured cards/items
   - Reuse the existing model if possible; extend it minimally.

3. **Add a structured reference model**
   - Introduce a small, explicit contract for summary references, for example:
     - reference type: `task` or `approval`
     - entity ID
     - display label/title
     - status
     - optional route/link token
   - Prefer a collection on each summary item, e.g. `References: []`.
   - Keep it future-safe for other entity types, but only implement task/approval now.

4. **Populate references during summary composition**
   - In the summary generation service, when building each summary item:
     - attach task references if the item is derived from or clearly tied to one or more tasks
     - attach approval references if the item is derived from or clearly tied to one or more approvals
   - Use existing source data and avoid expensive N+1 lookups.
   - If the summary is generated from aggregate data, include references only when there is a reliable underlying entity association.
   - Do not fail summary generation if references cannot be resolved.

5. **Preserve tenant and access boundaries**
   - Ensure all task/approval lookups are scoped by `company_id`.
   - Do not include references to entities outside the current tenant.
   - If an entity is unavailable, deleted, or inaccessible, omit the reference rather than leaking identifiers.

6. **Persist references in the stored summary payload**
   - Update the persistence model so references survive retrieval and are not recomputed only at render time.
   - If summaries are stored in `messages.structured_payload`, extend that JSON shape.
   - If there is a typed persistence model, update EF configuration and serialization accordingly.

7. **Render references in web/mobile-friendly form**
   - Update the dashboard/briefing UI to show linked underlying items where present.
   - Keep rendering concise, e.g.:
     - “Related tasks: Q3 Forecast Review, Vendor Invoice Reconciliation”
     - “Related approvals: Budget Increase Request (Pending)”
   - Use existing routing/navigation patterns for task and approval detail pages.
   - If no detail page exists, render non-clickable labels without breaking UX.

8. **Maintain backward compatibility**
   - Existing summaries without references must still deserialize and render correctly.
   - Make new fields optional.
   - Avoid breaking API consumers by using additive contract changes.

9. **Add tests**
   - Unit/integration coverage should verify:
     - summary item includes task references when source tasks exist
     - summary item includes approval references when source approvals exist
     - summary generation still succeeds with zero references
     - references are tenant-scoped
     - API/UI contract includes the new reference structure
     - old payloads without references still deserialize safely

10. **Keep implementation explainable**
   - References should support the story note: “Summaries should reference underlying tasks/approvals where possible.”
   - Avoid generating fabricated references from LLM text alone.
   - Prefer deterministic linkage from source records used to build the summary.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the summary generation flow:
   - Seed or create a company with:
     - at least one notable task
     - at least one pending/completed approval
   - Trigger or wait for daily briefing generation.
   - Confirm the persisted summary payload includes structured references.

4. Verify API contract behavior:
   - Retrieve the generated summary through the existing API/query path.
   - Confirm response includes reference metadata for linked tasks/approvals.
   - Confirm summaries without references still return valid payloads.

5. Verify web rendering:
   - Open dashboard/briefing view.
   - Confirm linked or labeled references appear under relevant summary items.
   - Confirm no UI errors for older summaries lacking the new field.

6. Verify tenant isolation:
   - Using another tenant/company context, confirm unrelated task/approval references are not exposed.

7. If mobile consumes the same payload:
   - Build/verify model compatibility and ensure additive payload changes do not break deserialization.

# Risks and follow-ups
- **Unclear existing summary model**: ST-505 may still be partially implemented or stored as plain text only. If so, introduce the smallest structured payload extension necessary rather than redesigning the whole feature.
- **LLM-generated summaries may not map cleanly to entities**: avoid heuristic overreach. Only attach references when the source pipeline has deterministic entity IDs.
- **N+1 query/performance risk**: summary generation may aggregate many items; batch-load tasks/approvals where possible.
- **Backward compatibility risk**: older stored summaries may not contain the new field. Ensure optional deserialization and null-safe rendering.
- **Routing mismatch risk**: task/approval detail routes may differ between web and mobile. Reuse existing route helpers/contracts if available.
- **Authorization leakage risk**: never include cross-tenant or unauthorized entity references in summary payloads.

Follow-ups to note in code comments or task notes if not completed here:
- add richer source attribution for other entity types later
- standardize summary item reference rendering across dashboard, inbox, and mobile
- consider storing human-readable deep links or route descriptors in a shared contract if navigation is currently duplicated