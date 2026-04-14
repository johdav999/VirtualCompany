# Goal
Implement `TASK-5.4.1` for **US-5.4 Filtering, drill-down, and audit deep linking** by adding server-side activity feed filtering with indexed queries, URL-synced filter state, and activity detail deep linking to audit records within the same tenant. Include automated integration coverage for the top 5 filter combinations and the audit deep-link flow.

# Scope
Deliver the following end-to-end behavior:

- Add server-side filtering for the activity feed by:
  - agent
  - department
  - task
  - event type
  - status
  - timeframe/date range
- Ensure results match **all selected filters** with tenant isolation enforced.
- Implement performant query paths backed by appropriate PostgreSQL indexes.
- Update the web UI so applying/clearing filters updates the URL query string and can be reloaded/shared without losing state.
- Add activity item detail view support showing:
  - raw payload
  - summary
  - correlation links
  - audit deep link when available
- Ensure audit deep links navigate to the correct audit detail page for the same tenant only.
- Add automated integration tests covering:
  - at least 5 meaningful filter combinations
  - filter clear/reset behavior
  - URL query persistence/reload behavior if feasible at integration level
  - audit deep-link flow

Out of scope unless required by existing patterns:
- Broad redesign of the activity feed UI
- New mobile functionality
- Non-activity dashboard widgets
- Full-text search beyond the listed dimensions

# Files to touch
Inspect the solution first and then update the actual files that align with existing architecture. Likely areas:

- `src/VirtualCompany.Domain/**`
  - activity feed/audit entities, enums, value objects, specifications if present
- `src/VirtualCompany.Application/**`
  - query DTOs/contracts for activity feed filters
  - query handlers/services
  - detail view DTOs including audit link metadata
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repository/query implementations
  - migrations or SQL/index definitions
- `src/VirtualCompany.Api/**`
  - activity feed endpoints/controllers
  - request binding for query string filters
- `src/VirtualCompany.Web/**`
  - activity feed page/components
  - filter UI state binding
  - URL query string synchronization
  - detail drawer/page/modal and audit link rendering
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for filtered queries and audit deep-link behavior

Also inspect:
- existing migration approach under `docs/postgresql-migrations-archive/README.md`
- any existing audit/activity pages and route conventions
- any shared query parameter helpers in web project

# Implementation plan
1. **Discover existing activity/audit implementation**
   - Find current activity feed source:
     - entity/table name
     - API endpoint(s)
     - UI page/component
     - audit detail route
   - Identify whether activity feed is backed by `audit_events`, a separate activity table/view, or composed query logic.
   - Reuse existing CQRS-lite patterns and tenant scoping conventions.

2. **Define filter contract**
   - Add/extend an application query request model with optional fields:
     - `AgentId`
     - `Department`
     - `TaskId`
     - `EventType`
     - `Status`
     - `FromUtc`
     - `ToUtc`
     - paging/sort fields if already supported
   - Normalize semantics:
     - all provided filters are combined with `AND`
     - timeframe is inclusive/exclusive per existing conventions; document and test it
     - empty/null filters mean “not applied”
   - Add validation for malformed IDs, invalid date ranges, and unsupported enum values.

3. **Implement server-side filtered query**
   - Update the application query handler/service to compose filters incrementally.
   - Enforce tenant/company scoping first.
   - Ensure joins/projections support:
     - agent display name
     - department
     - task reference
     - event type
     - status
     - summary
     - raw payload
     - correlation references
     - audit record ID/linkability
   - Avoid client-side filtering in web code.

4. **Add indexed query support**
   - Review actual table schema and add PostgreSQL indexes for the filter dimensions used most often.
   - Prefer composite indexes that match tenant-first access patterns, for example:
     - `(company_id, created_at desc)`
     - `(company_id, agent_id, created_at desc)`
     - `(company_id, department, created_at desc)`
     - `(company_id, task_id, created_at desc)`
     - `(company_id, event_type, created_at desc)`
     - `(company_id, status, created_at desc)`
   - If the activity feed is backed by `audit_events`, adapt indexes to actual column names.
   - Do not over-index blindly; align with real query shape and existing indexes.
   - Add migration using the repo’s established migration pattern.

5. **Extend detail view DTO/API**
   - Ensure activity detail response includes:
     - raw payload JSON/string
     - summary/rationale summary
     - correlation IDs/related entity links
     - audit record identifier if available
     - resolved audit detail URL or enough metadata for UI to build it
   - Ensure audit deep link is only returned when the linked audit record belongs to the same tenant.

6. **Implement API query-string binding**
   - Update API endpoint to accept filter parameters from query string.
   - Keep parameter names stable and URL-friendly, e.g.:
     - `agentId`
     - `department`
     - `taskId`
     - `eventType`
     - `status`
     - `from`
     - `to`
   - Return filtered results and preserve existing paging contract if present.

7. **Update Blazor activity feed UI**
   - Bind filter controls to the server query model.
   - On apply/change/clear:
     - update URL query string
     - reload data from server using query params
   - On initial page load:
     - hydrate filter state from URL query string
     - fetch matching results
   - Ensure clear/reset removes corresponding query params.
   - Keep implementation idiomatic for the current Blazor rendering mode already used in the app.

8. **Implement activity detail interaction**
   - Clicking an activity item should open the existing detail surface (drawer/page/modal) or add one consistent with current UX.
   - Show:
     - summary
     - raw payload
     - correlation links
     - audit deep link when available
   - Audit deep link should navigate to the existing audit detail route for the same tenant context.

9. **Add integration tests**
   - Add API integration tests for at least 5 top filter combinations, such as:
     1. agent + timeframe
     2. department + event type
     3. task + status
     4. agent + department + status
     5. event type + status + timeframe
   - Verify returned rows satisfy all selected filters.
   - Add test for clearing filters / no filters returning broader result set if practical.
   - Add audit deep-link test:
     - activity event with linked audit record returns correct audit metadata/link
     - link resolves to correct tenant-scoped audit detail
     - cross-tenant mismatch is not exposed
   - If web integration tests already exist, add URL-state tests there; otherwise at minimum verify API/query contract and keep UI logic simple and deterministic.

10. **Document assumptions in code**
   - Add concise comments only where needed for:
     - filter combination semantics
     - tenant-safe audit linking
     - index rationale if migration naming is not self-evident

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API behavior:
   - call the activity feed endpoint with:
     - no filters
     - single filters
     - combined filters
     - invalid date range
   - confirm results are tenant-scoped and AND-combined

4. Manually verify UI behavior:
   - open activity feed page
   - apply each filter type
   - confirm URL query string updates
   - refresh page and confirm state persists
   - clear filters and confirm query string is cleaned up
   - click an activity item and confirm detail view shows payload, summary, correlations, and audit link when present

5. Verify audit deep-link behavior:
   - open an activity with linked audit record
   - click deep link
   - confirm correct audit detail page opens for same tenant
   - confirm no link is shown for activities without audit records

6. Verify migration/index changes:
   - inspect generated migration
   - ensure indexes target actual query columns and tenant-first access patterns
   - confirm no duplicate/redundant indexes versus existing schema

# Risks and follow-ups
- **Schema ambiguity risk:** The activity feed may be backed by a different table/view than implied by the architecture snippet. Inspect first and adapt rather than forcing a new model.
- **Index bloat risk:** Adding too many overlapping indexes can hurt writes. Prefer a minimal set based on actual query shape.
- **URL sync complexity in Blazor:** Depending on current routing/state patterns, query-string synchronization may require careful handling to avoid duplicate loads or navigation loops.
- **Tenant safety risk:** Audit deep links must never expose cross-tenant IDs or routes. Validate tenant ownership before returning link metadata.
- **Test harness limitations:** If current tests are API-only, cover the server contract thoroughly and keep UI query-string logic straightforward; add dedicated web tests later if the repo introduces them.
- **Potential follow-up:** If feed volume grows, consider a dedicated read model/materialized projection for activity feed queries rather than increasingly complex joins on transactional tables.