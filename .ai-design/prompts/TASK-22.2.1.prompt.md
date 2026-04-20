# Goal
Implement backlog task **TASK-22.2.1** for story **US-22.2 Refactor dashboard action queue into inline prioritized action list**.

Deliver a backend and UI change that:
- extends the queue/action service with:
  - `GetTopActions`
  - paginated `GetAllActions`
- enforces deterministic sorting by:
  1. priority descending
  2. due time ascending
  3. impact score descending
  4. stable tie-breaker for deterministic pagination
- updates the dashboard to remove the legacy right sidebar queue component
- renders the top 5 actions inline below **Today’s Focus**
- adds a visible **View full queue** link to the dedicated queue page

Preserve tenant scoping and align with the modular monolith / CQRS-lite architecture already used in the solution.

# Scope
In scope:
- inspect existing queue/action domain, application services, API endpoints, and dashboard UI
- add or extend query/service contracts for:
  - top 5 prioritized actions
  - paginated full queue retrieval
- ensure sorting is deterministic across pages
- update dashboard rendering to use the new top-actions query
- remove legacy sidebar queue rendering from dashboard
- add visible navigation link to full queue page
- add/update tests for sorting, pagination, and dashboard rendering behavior

Out of scope:
- redesign of the dedicated full queue page beyond wiring it to paginated data if needed
- unrelated dashboard layout refactors
- changing business semantics of priority or impact beyond what is required for sorting
- mobile app changes unless the same shared API contract is already consumed there and compilation requires updates

# Files to touch
Start by locating the actual implementations, then update the concrete files you find in these areas.

Likely backend areas:
- `src/VirtualCompany.Application/**`
  - queue/action query models
  - service interfaces
  - handlers or application services
- `src/VirtualCompany.Domain/**`
  - action/queue entities or value objects if sort fields are defined there
- `src/VirtualCompany.Infrastructure/**`
  - repository/query implementations
  - EF Core or SQL query logic
- `src/VirtualCompany.Api/**`
  - queue/dashboard endpoints
  - request/response DTOs if API-facing contracts exist
- migrations only if a required sort field is missing and cannot be derived

Likely web/UI areas:
- `src/VirtualCompany.Web/**`
  - dashboard page/component
  - `TopActionsList` component
  - legacy sidebar queue component usage
  - navigation link to full queue page
  - full queue page data loading if it needs pagination wiring

Likely test areas:
- `tests/VirtualCompany.Api.Tests/**`
- any application/web test projects already present in the repo

Before editing, identify the exact files that currently implement:
- dashboard action queue/sidebar
- `TopActionsList`
- queue page
- queue/action service and endpoint contracts

# Implementation plan
1. **Discover current implementation**
   - Search for:
     - `TopActionsList`
     - `ActionQueue`
     - `Queue`
     - `Today’s Focus` / `Today's Focus`
     - `GetAllActions`
     - any action service interface
   - Map the current flow:
     - data source
     - service/query layer
     - API endpoint
     - dashboard component tree
     - full queue page

2. **Define/extend application contracts**
   - Add or update a queue/action query service contract with methods equivalent to:
     - `GetTopActions(companyId, userContext, count = 5, cancellationToken)`
     - `GetAllActions(companyId, userContext, pageNumber, pageSize, cancellationToken)`
   - If the codebase uses CQRS handlers instead of service methods, implement equivalent query objects/handlers.
   - Return paginated results with enough metadata for UI paging:
     - items
     - page number
     - page size
     - total count if already standard in the app
     - optionally total pages / has next / has previous

3. **Implement required ordering**
   - Ensure the underlying query sorts by:
     1. priority descending
     2. due time ascending
     3. impact score descending
   - Add a final stable tie-breaker to guarantee deterministic ordering across pages, preferably:
     4. created timestamp ascending or descending, then
     5. unique id ascending
   - If priority is stored as text, map it explicitly to sortable numeric rank in query logic.
   - Handle null due times explicitly and consistently. Choose a rule that preserves “due time ascending” semantics and document it in code/tests, e.g. nulls last.
   - Do not rely on implicit database ordering.

4. **Implement `GetTopActions`**
   - Reuse the same ordering logic as the full queue query.
   - Return exactly up to 5 items.
   - Avoid duplicating sort logic in multiple places; centralize it in one query builder/specification/helper where practical.

5. **Implement paginated `GetAllActions`**
   - Accept pagination parameters from API/UI.
   - Validate page inputs defensively.
   - Apply the deterministic ordering before `Skip/Take` or equivalent.
   - Ensure tenant scoping is preserved in all queries.
   - If an endpoint already exists, extend it without breaking consumers if possible.
   - If no endpoint exists, add one under the existing queue/actions route conventions.

6. **Update API contracts**
   - Expose:
     - top actions endpoint or dashboard endpoint field for top actions
     - full queue endpoint with pagination parameters
   - Keep naming consistent with existing API style.
   - Ensure response DTOs include fields needed by UI:
     - id
     - title
     - priority
     - due time
     - impact score
     - any status/agent metadata already displayed

7. **Refactor dashboard UI**
   - Remove the legacy right sidebar queue component from the dashboard render path.
   - Render `TopActionsList` inline directly below **Today’s Focus**.
   - Ensure behavior matches acceptance criteria:
     - when 5 or more actions exist, render exactly the top 5
   - For fewer than 5 actions, preserve sensible behavior based on existing UX patterns unless a stricter existing requirement is found.
   - Add a visible **View full queue** link near the inline list that navigates to the dedicated queue page.

8. **Wire full queue page**
   - Ensure the dedicated queue page uses the paginated endpoint/query.
   - Confirm page navigation preserves deterministic ordering.
   - If pagination UI already exists, adapt it to the new contract.
   - If not, implement minimal paging controls consistent with existing app patterns only if required for the page to function.

9. **Add tests**
   - Backend/application/API tests:
     - sorting by priority desc
     - then due time asc
     - then impact desc
     - deterministic tie-breaking across pages
     - pagination returns stable non-overlapping pages
     - tenant scoping remains enforced
   - UI/component/integration tests if present in repo:
     - dashboard no longer renders legacy sidebar queue component
     - `TopActionsList` appears below Today’s Focus
     - top 5 are rendered when 5+ actions exist
     - visible **View full queue** link navigates correctly

10. **Keep implementation clean**
   - Prefer minimal, targeted changes.
   - Reuse existing abstractions and naming conventions.
   - Do not introduce parallel queue models if one already exists.
   - Add concise comments only where ordering/null handling is non-obvious.

# Validation steps
Run and report the results of the following after implementation:

1. Build:
   - `dotnet build`

2. Tests:
   - `dotnet test`

3. Manual verification:
   - open the dashboard
   - confirm the legacy right sidebar action queue is gone
   - confirm `TopActionsList` is inline below Today’s Focus
   - seed or use test data with 5+ actions and verify exactly 5 render
   - verify visible **View full queue** link navigates to the queue page
   - verify full queue pagination works and ordering is stable across repeated requests/pages

4. Data/order verification:
   - create or inspect actions with mixed:
     - priorities
     - due times
     - impact scores
     - identical sort values
   - confirm final ordering matches the required sort and remains deterministic

# Risks and follow-ups
- **Unknown existing model names:** the codebase may use “tasks”, “actions”, or “queue items” interchangeably. Resolve this before coding to avoid duplicate abstractions.
- **Priority stored as string:** lexical sorting may be wrong; explicit ranking may be required.
- **Null due times:** must be handled intentionally or ordering may be inconsistent across DB/provider behavior.
- **Deterministic pagination:** without a unique tie-breaker, page contents can shift between requests.
- **Dashboard coupling:** the legacy sidebar may be embedded in a shared layout/component rather than the dashboard page itself.
- **API compatibility:** if existing consumers use `GetAllActions`, preserve backward compatibility where feasible while adding pagination parameters.
- **Test gaps:** if UI tests are sparse, add at least backend/API coverage for ordering and pagination.

If you encounter ambiguity, prefer the smallest implementation that satisfies the acceptance criteria and document any assumptions in the final change summary.