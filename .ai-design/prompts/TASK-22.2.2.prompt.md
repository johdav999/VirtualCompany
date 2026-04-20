# Goal

Implement backlog task **TASK-22.2.2 — Remove legacy right sidebar queue from Dashboard.razor and insert TopActionsList below Today’s Focus** for story **US-22.2 Refactor dashboard action queue into inline prioritized action list**.

Deliver the change end-to-end so that:

- The dashboard no longer renders the legacy right sidebar action queue component.
- `TopActionsList` renders inline directly below **Today’s Focus**.
- The inline list shows **exactly 5 actions** when **5 or more** actions exist.
- The action service returns actions ordered by:
  1. priority descending
  2. due time ascending
  3. impact score descending
- The dashboard shows a visible **View full queue** link to the dedicated queue page.
- The full queue endpoint supports pagination parameters and preserves deterministic ordering across pages.

Work within the existing **.NET / Blazor Web App / ASP.NET Core modular monolith** architecture and preserve tenant scoping.

# Scope

In scope:

- Update the dashboard UI composition in `Dashboard.razor` and any related view models/components.
- Remove the legacy right sidebar queue rendering from the dashboard.
- Insert or wire `TopActionsList` below the Today’s Focus section.
- Ensure the dashboard query/service only supplies the top 5 actions for the inline list.
- Update the action queue query/service/repository ordering to be deterministic and match acceptance criteria.
- Add or update the full queue API/page query to accept pagination parameters.
- Ensure deterministic pagination by including a stable tie-breaker in ordering if needed, such as action ID.
- Add or update tests covering:
  - dashboard no longer rendering legacy queue
  - top 5 behavior
  - sort order
  - paginated deterministic ordering

Out of scope:

- Broad redesign of dashboard layout beyond this task.
- New queue page UX beyond exposing the required visible navigation link and supporting paginated data.
- Any mobile changes.
- Any unrelated refactors unless required to make this task cleanly testable.

# Files to touch

Inspect the solution first and then update the actual files that implement dashboard rendering, action queries, and queue endpoints. Likely areas:

- `src/VirtualCompany.Web/**/Dashboard.razor`
- `src/VirtualCompany.Web/**/Dashboard.razor.cs`
- `src/VirtualCompany.Web/**/TopActionsList.razor`
- `src/VirtualCompany.Web/**/Today*Focus*`
- `src/VirtualCompany.Web/**/ActionQueue*`
- `src/VirtualCompany.Application/**/Dashboard*Query*`
- `src/VirtualCompany.Application/**/Actions*`
- `src/VirtualCompany.Api/**/Controllers/*Queue*`
- `src/VirtualCompany.Infrastructure/**/Repositories/*Action*`
- `src/VirtualCompany.Domain/**/Action*`
- `tests/VirtualCompany.Api.Tests/**`
- `tests/**/Web*`
- `tests/**/Application*`

If names differ, find the real equivalents before editing. Prefer modifying existing query/service paths rather than introducing parallel implementations.

# Implementation plan

1. **Discover current implementation**
   - Locate:
     - `Dashboard.razor`
     - the legacy right sidebar queue component currently rendered on the dashboard
     - the `TopActionsList` component
     - the application service/query that supplies dashboard actions
     - the full queue endpoint/page and its backing query
   - Trace the current action ordering from UI to application layer to persistence.
   - Identify whether pagination already exists and whether ordering is stable across pages.

2. **Remove legacy dashboard sidebar queue**
   - Delete the legacy right sidebar queue component usage from `Dashboard.razor`.
   - Remove any now-unused parameters, injected services, or view model properties that only supported the sidebar queue.
   - Do not leave hidden or dead markup behind.

3. **Insert `TopActionsList` below Today’s Focus**
   - Render `TopActionsList` immediately below the Today’s Focus section in the dashboard layout.
   - Preserve existing dashboard styling conventions.
   - Ensure the component receives the top actions collection from the dashboard model/query.
   - Add a visible **View full queue** link adjacent to or directly beneath the inline list, navigating to the dedicated queue page route already used by the app. If no route constant exists, create or reuse a centralized route definition if that is the local pattern.

4. **Enforce top-5 inline behavior**
   - In the dashboard application query/service, request or project only the first 5 actions for the inline list.
   - Ensure behavior is:
     - if actions count is 0–4, render available actions
     - if actions count is 5 or more, render exactly 5
   - Avoid relying on UI-only truncation if the application layer can provide a bounded result cleanly.

5. **Fix action ordering in service/repository**
   - Update the action query ordering to:
     - priority descending
     - due time ascending
     - impact score descending
   - For deterministic pagination and stable ordering, add a final unique tie-breaker, typically:
     - ID ascending
   - Make sure null due times are handled consistently and explicitly. Preserve or define a deterministic null sort rule and keep it consistent in tests.
   - Apply the same ordering logic to both:
     - dashboard top actions query
     - full queue query/endpoint

6. **Support pagination on full queue endpoint**
   - Ensure the full queue endpoint accepts pagination parameters, preferably existing conventions such as:
     - `page`
     - `pageSize`
     - or `skip` / `take`
   - Validate and clamp parameters to safe bounds if the project already follows that pattern.
   - Return paged results with deterministic ordering across pages.
   - If the endpoint already exists, extend rather than replace it.

7. **Keep tenant scoping intact**
   - Verify all action queries remain company/tenant scoped.
   - Do not introduce any cross-tenant leakage while changing repository or query logic.

8. **Add or update tests**
   - Add focused tests at the appropriate layers:
     - **Application/Infrastructure tests** for ordering:
       - priority desc
       - due time asc
       - impact score desc
       - stable tie-breaker
     - **API tests** for full queue pagination:
       - accepts pagination parameters
       - page 1 + page 2 produce deterministic non-overlapping ordered results
     - **Web/component tests** if present in the repo:
       - dashboard does not render legacy sidebar queue
       - `TopActionsList` appears below Today’s Focus
       - visible **View full queue** link is present
       - top 5 cap is respected
   - If there is no component test setup, cover as much as possible through page model/view model tests and keep UI changes minimal and obvious.

9. **Clean up**
   - Remove obsolete code paths, unused DTO fields, and stale comments related to the legacy sidebar queue.
   - Keep naming aligned with story language: inline prioritized action list, top actions, full queue.

# Validation steps

1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in the web app:
   - Open dashboard and confirm:
     - legacy right sidebar queue is gone
     - `TopActionsList` appears directly below Today’s Focus
     - **View full queue** link is visible
   - Seed or use data with:
     - fewer than 5 actions
     - exactly 5 actions
     - more than 5 actions
   - Confirm dashboard shows:
     - all available actions when fewer than 5
     - exactly 5 when 5 or more exist

4. Verify ordering manually or via tests:
   - Create actions with mixed priority, due time, and impact score.
   - Confirm order is:
     - higher priority first
     - for same priority, earlier due time first
     - for same priority and due time, higher impact score first
     - for exact ties, stable unique tie-breaker order

5. Verify full queue pagination:
   - Call or navigate to the full queue endpoint/page with pagination parameters.
   - Confirm page transitions preserve deterministic ordering and do not reshuffle tied items between requests.

# Risks and follow-ups

- **Unknown current naming/structure**: The actual dashboard and queue implementation may use different names than the task language. Resolve by tracing from `Dashboard.razor` and existing routes before coding.
- **Null due time semantics**: Acceptance criteria specify due time ascending but not null handling. Choose an explicit deterministic rule consistent with existing business behavior and document it in tests.
- **Priority representation**: If priority is stored as enum/string, ensure sorting is business-correct and not lexical.
- **Pagination contract mismatch**: If the API already uses a paging envelope or shared request model, conform to that instead of inventing a new shape.
- **UI placement ambiguity**: “Below Today’s Focus” should mean directly after that section in markup/layout order. Keep placement unambiguous.
- **Potential follow-up**:
  - centralize action queue ordering in one reusable query/specification to avoid drift between dashboard and full queue
  - add route/integration tests for the queue page link
  - add explicit product decision on null due-date ordering if not already defined