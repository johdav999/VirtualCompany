# Goal
Implement backlog task **TASK-22.2.3 — Implement TopActionsList.razor with top 5 rendering and full queue navigation** for story **US-22.2 Refactor dashboard action queue into inline prioritized action list**.

Deliver the change so that:
- the dashboard no longer renders the legacy right-sidebar action queue component,
- a new/updated `TopActionsList.razor` renders the **top 5** actions inline below **Today’s Focus**,
- action ordering is deterministic and matches the required sort:
  1. priority descending
  2. due time ascending
  3. impact score descending
- the dashboard shows a visible **View full queue** link that navigates to the dedicated queue page,
- the full queue API/query path supports pagination parameters and preserves deterministic ordering across pages.

Use the existing solution structure and patterns already present in the repo. Prefer minimal, cohesive changes over broad refactors.

# Scope
In scope:
- Blazor dashboard UI changes in `VirtualCompany.Web`
- `TopActionsList.razor` implementation or completion
- removal/replacement of the legacy dashboard sidebar action queue rendering
- navigation link to the full queue page
- application/query/service updates needed to guarantee required ordering
- backend endpoint/query support for paginated full queue retrieval with deterministic ordering
- tests covering sorting, top-5 behavior, pagination determinism, and dashboard rendering expectations where practical

Out of scope:
- redesigning the queue domain model
- changing unrelated dashboard widgets
- introducing a new architecture pattern
- mobile changes
- broad API versioning changes unless already required by existing conventions

# Files to touch
Inspect the repo first, then update the exact files that already own this behavior. Likely areas include:

- `src/VirtualCompany.Web/**/Dashboard*.razor`
- `src/VirtualCompany.Web/**/TopActionsList.razor`
- `src/VirtualCompany.Web/**/TopActionsList.razor.cs`
- `src/VirtualCompany.Web/**/Today*Focus*.razor`
- `src/VirtualCompany.Web/**/ActionQueue*.razor`
- `src/VirtualCompany.Web/**/Pages/Queue*.razor`
- `src/VirtualCompany.Api/**/Controllers/*Queue*`
- `src/VirtualCompany.Application/**/Queries/*Action*`
- `src/VirtualCompany.Application/**/Services/*Action*`
- `src/VirtualCompany.Infrastructure/**/Repositories/*Action*`
- `src/VirtualCompany.Shared/**/Dtos/*Action*`
- `tests/VirtualCompany.Api.Tests/**`
- any relevant web/component test project files if present

If names differ, follow the existing dashboard/action-queue implementation rather than forcing new file names.

# Implementation plan
1. **Discover the current implementation**
   - Find the dashboard page/component that currently renders the legacy right sidebar action queue.
   - Find any existing `TopActionsList.razor` stub or partial implementation.
   - Trace the data flow for dashboard actions:
     - UI component
     - web client/service
     - API endpoint
     - application query/service
     - infrastructure/repository/EF query
   - Identify the dedicated full queue page and its current navigation route, if it already exists.

2. **Remove legacy sidebar queue rendering**
   - Update the dashboard layout/component so the old right sidebar action queue component is no longer rendered.
   - Preserve surrounding layout integrity and spacing.
   - Ensure the new inline list appears **below Today’s Focus** per task intent.

3. **Implement `TopActionsList.razor`**
   - Render actions inline in the dashboard.
   - Show **exactly 5 items when 5 or more actions exist**.
   - If fewer than 5 actions exist, render the available count without placeholders unless the existing UX pattern requires them.
   - Add a visible **View full queue** link/button near the list header or footer.
   - Wire the link to the dedicated queue page using existing Blazor navigation conventions.
   - Keep markup accessible and consistent with existing component styling patterns.

4. **Enforce required sorting in the action service/query**
   - Update the action retrieval logic so ordering is:
     - priority descending
     - due time ascending
     - impact score descending
   - Make the ordering deterministic by adding a final stable tie-breaker if needed, such as action ID ascending or created timestamp + ID, depending on the existing model.
   - Apply the same ordering rules to:
     - dashboard top-actions retrieval
     - full queue retrieval
   - Do not rely on UI-side sorting if the acceptance criteria say the service returns sorted actions.

5. **Support deterministic pagination for the full queue**
   - Ensure the full queue endpoint/query accepts pagination parameters already used in the codebase, or add them using existing API conventions.
   - Apply ordering before pagination.
   - Add a stable final tie-breaker so page boundaries do not shuffle between requests.
   - Return paginated results in the project’s established response shape.

6. **Update contracts if needed**
   - If the UI needs fields not currently exposed, extend DTOs/view models minimally.
   - Avoid leaking domain entities directly.
   - Keep tenant scoping intact throughout the query path.

7. **Add or update tests**
   - Add application/API tests to verify sorting order.
   - Add pagination tests to verify deterministic ordering across pages.
   - Add component/UI tests if the repo already uses them; otherwise keep UI validation lightweight and rely on integration tests plus manual verification.
   - Specifically cover:
     - top 5 returned/rendered when more than 5 exist
     - fewer than 5 renders all available
     - `View full queue` navigation target is present
     - legacy sidebar queue is no longer rendered on dashboard
     - stable ordering with ties

8. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - UI in Web
     - query orchestration in Application
     - persistence/query translation in Infrastructure
     - transport contracts in Shared/API
   - Preserve tenant-aware filtering on all queue queries.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in the web app:
   - Open dashboard and confirm the legacy right sidebar action queue is gone.
   - Confirm `TopActionsList` appears below Today’s Focus.
   - Seed or use data with 6+ actions and verify only the top 5 render.
   - Verify visible **View full queue** link is present.
   - Click the link and confirm navigation to the dedicated queue page.
   - Verify full queue pagination works and items remain in deterministic order across repeated requests/page transitions.

4. Sorting verification:
   - Use test data that exercises ties:
     - different priorities
     - same priority with different due times
     - same priority and due time with different impact scores
     - exact ties requiring final tie-breaker
   - Confirm returned order matches the required sort sequence.

5. Regression check:
   - Ensure no tenant-scoping regressions.
   - Ensure dashboard still renders correctly with zero actions and fewer than five actions.

# Risks and follow-ups
- **Risk: ambiguous priority representation**
  - Priority may be stored as enum, string, or numeric rank. Confirm the canonical descending sort mapping and do not sort lexicographically unless that is already the established representation.

- **Risk: nondeterministic pagination**
  - If multiple rows share the same priority, due time, and impact score, pagination can shuffle without a final tie-breaker. Add one explicitly.

- **Risk: duplicate sorting logic**
  - Avoid separate UI and backend sorting implementations that can drift. Centralize ordering in the application/infrastructure query path.

- **Risk: route mismatch**
  - Reuse the existing full queue route if present instead of inventing a new one.

- **Risk: hidden dependency on legacy sidebar component**
  - The old component may contain data-loading or styling side effects. Remove rendering carefully and migrate any required behavior into the new inline list.

Follow-ups if needed:
- add component snapshot/markup tests if the web project already supports them,
- consider extracting shared action queue ordering into a reusable query helper/specification,
- document the deterministic ordering contract for future queue-related tasks.