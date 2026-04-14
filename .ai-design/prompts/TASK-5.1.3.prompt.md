# Goal
Implement **TASK-5.1.3 — Build chronological activity feed UI with live updates and pagination** for **US-5.1 Unified real-time agent activity feed**.

Deliver a tenant-scoped end-to-end activity feed experience that:
- exposes a reverse-chronological, cursor-paginated backend API,
- pushes newly persisted activity events to connected clients within 2 seconds through a real-time subscription channel,
- renders the feed in the Blazor web UI,
- supports incremental pagination,
- enforces tenant isolation and authorization,
- returns correct empty-state behavior.

This work should fit the existing modular monolith architecture and .NET stack, using ASP.NET Core on the backend and Blazor Web App on the frontend.

# Scope
Implement only what is required for this task and its acceptance criteria.

In scope:
- Backend activity feed query endpoint for tenant-scoped events
- Cursor-based pagination in reverse chronological order
- Activity event DTO/view model including:
  - `eventId`
  - `tenantId`
  - `agentId`
  - `eventType`
  - `occurredAt`
  - `status`
  - `summary`
  - `correlationId`
  - `source metadata`
- Authorization and tenant isolation enforcement
- HTTP 403 behavior for unauthorized tenant access attempts
- Empty result behavior with `nextCursor: null`
- Real-time subscription channel for newly persisted activity events
- Blazor UI component/page for chronological feed display
- Live update handling in UI
- “load more” / pagination UX
- Tests covering API ordering, pagination, tenant isolation, unauthorized access, empty results, and real-time delivery plumbing where practical

Out of scope unless already trivially supported by existing code:
- Mobile UI
- Advanced filtering beyond tenant scope
- Full audit/explainability redesign
- Broker-based pub/sub
- Rich notification center integration
- Historical backfill jobs
- Cross-tenant admin views

Assumptions to preserve:
- “tenant” maps to the company/workspace scope already used in the codebase
- Use the existing shared-schema multi-tenant approach with `company_id`/tenant enforcement
- Prefer ASP.NET Core SignalR for real-time delivery if no existing subscription mechanism exists
- Prefer CQRS-lite patterns already used in the solution

# Files to touch
Inspect the solution first and adjust exact file names to match existing conventions. Likely areas:

Backend:
- `src/VirtualCompany.Api/`
  - endpoint/controller/minimal API registration for activity feed
  - SignalR hub registration and endpoint mapping
- `src/VirtualCompany.Application/`
  - query + handler for activity feed retrieval
  - DTOs/contracts for paginated results and activity event payloads
  - authorization/tenant access service usage
  - event publication abstraction for live updates
- `src/VirtualCompany.Domain/`
  - activity feed domain model/value objects if needed
  - audit/activity event abstractions if missing
- `src/VirtualCompany.Infrastructure/`
  - EF Core repository/query implementation
  - persistence mapping for activity/audit events
  - outbox or post-persist dispatcher integration for real-time fan-out
  - SignalR publisher implementation
- `src/VirtualCompany.Shared/`
  - shared contracts if the solution centralizes API/DTO types here

Frontend:
- `src/VirtualCompany.Web/`
  - activity feed page/component
  - service for API calls
  - SignalR client subscription service
  - models/view models
  - empty/loading/error states
  - pagination UI

Tests:
- `tests/VirtualCompany.Api.Tests/`
  - API integration tests for ordering, pagination, tenant isolation, empty results, unauthorized access
  - possibly SignalR negotiation/subscription tests if test infrastructure supports it

Potential supporting files:
- DI registration files
- route constants
- app navigation/menu if the feed needs to be surfaced in dashboard/cockpit
- serialization helpers for cursor encoding/decoding

# Implementation plan
1. **Inspect existing architecture and reuse patterns**
   - Find how tenant context is resolved in API requests.
   - Find whether audit/activity events already exist, likely around `audit_events`, task history, or agent activity records.
   - Find whether the app already uses:
     - MediatR or similar request handlers
     - minimal APIs vs controllers
     - SignalR
     - cursor pagination helpers
     - authorization policies for company membership
   - Reuse existing conventions over introducing new patterns.

2. **Define the activity feed read contract**
   - Create a paginated response contract such as:
     - `items: List<ActivityFeedItemDto>`
     - `nextCursor: string?`
   - Define `ActivityFeedItemDto` with the exact acceptance-criteria fields:
     - `eventId`
     - `tenantId`
     - `agentId`
     - `eventType`
     - `occurredAt`
     - `status`
     - `summary`
     - `correlationId`
     - `sourceMetadata`
   - Keep `sourceMetadata` structured, e.g. dictionary/object/JSON-compatible DTO.
   - Ensure naming is consistent with existing API conventions.

3. **Choose and implement the backing data source**
   - Prefer an existing persisted business event source if available, likely `audit_events` or a similar activity table.
   - If `audit_events` is incomplete in the current codebase, extend the persistence model minimally to support the required fields.
   - Do not create a parallel redundant event store if the existing audit/activity persistence can satisfy the feed.
   - Ensure every queried row is tenant-owned and includes a stable ordering key.

4. **Implement reverse chronological cursor pagination**
   - Sort by:
     - `occurredAt DESC`
     - then a stable tiebreaker such as `eventId DESC`
   - Use cursor-based pagination, not offset pagination.
   - Cursor should encode enough information to continue pagination safely, e.g.:
     - `occurredAt`
     - `eventId`
   - Query semantics:
     - first page: latest events
     - next page: events strictly older than the cursor tuple
   - Return `nextCursor = null` when there are no more results.
   - For no events at all, return:
     - `items: []`
     - `nextCursor: null`

5. **Implement tenant-scoped authorization**
   - Require authenticated access.
   - Resolve requested tenant/company scope from route or current context, following existing app conventions.
   - Verify the caller has membership/access to that tenant.
   - Return HTTP 403 for unauthorized access attempts to another tenant’s feed.
   - Ensure repository/query layer also filters by tenant ID so authorization is enforced in depth.

6. **Add the API endpoint**
   - Add a GET endpoint such as tenant-scoped:
     - `/api/tenants/{tenantId}/activity-feed`
     - or the project’s existing company-scoped route convention
   - Support query params like:
     - `cursor`
     - `pageSize`
   - Validate page size with a safe upper bound.
   - Return the paginated response DTO.
   - Ensure response serialization is stable and documented in code comments if appropriate.

7. **Implement real-time delivery**
   - Prefer SignalR if no existing real-time mechanism exists.
   - Add a hub for activity feed subscriptions, scoped by tenant.
   - On connection/subscription:
     - validate authenticated user
     - validate tenant membership
     - join a tenant-specific group, e.g. `tenant:{tenantId}:activity`
   - Define a client event such as `ActivityEventReceived`.
   - Publish newly persisted activity events to the tenant group after persistence succeeds.
   - Delivery target: within 2 seconds of persistence.
   - If the app already has an outbox/background dispatcher for domain events, integrate with it in the simplest reliable way that still meets the latency target.
   - Avoid publishing before transaction commit.

8. **Wire event publication from persistence path**
   - Identify where agent activity/audit events are created.
   - Hook publication after successful persistence:
     - either directly after save in application flow,
     - or via domain event/outbox consumer if already present.
   - Map persisted event data to the feed DTO payload.
   - Ensure only the owning tenant’s group receives the event.

9. **Build the Blazor activity feed UI**
   - Add or update a page/component in the executive cockpit/dashboard area for recent agent activity.
   - Render items in reverse chronological order.
   - Show key fields:
     - event type
     - summary
     - status
     - agent reference
     - occurred time
     - optional correlation/source metadata summary
   - Add empty state text when no events exist.
   - Add loading and error states.
   - Add “Load more” pagination using `nextCursor`.

10. **Add live update behavior in the UI**
    - Create a client service to:
      - fetch initial page
      - connect to SignalR
      - subscribe to tenant activity events
    - On receiving a new event:
      - insert it at the top of the feed
      - deduplicate by `eventId`
      - preserve reverse chronological order
    - Avoid breaking pagination state when live items arrive.
    - If the user has already paged older items, keep them intact and prepend only new items.

11. **Handle edge cases**
    - Empty feed returns valid empty state and `nextCursor: null`
    - Duplicate live event delivery should not duplicate UI rows
    - Events with identical timestamps must still paginate correctly via tiebreaker
    - Unauthorized subscription attempts should be rejected
    - Tenant switching in UI should:
      - disconnect prior subscription
      - clear/reload feed
      - subscribe to the new tenant scope

12. **Testing**
    - Add API integration tests for:
      - reverse chronological ordering
      - cursor pagination across multiple pages
      - stable pagination when timestamps collide
      - tenant isolation
      - HTTP 403 for unauthorized tenant access
      - empty result with `nextCursor: null`
    - Add unit tests for cursor encode/decode if implemented separately.
    - Add tests for real-time publisher/group targeting where feasible.
    - Add UI/component tests only if the repo already has a pattern for them; otherwise keep frontend logic testable via services.

13. **Keep implementation minimal and production-safe**
    - Do not over-engineer filtering or search.
    - Do not introduce new infrastructure dependencies if SignalR and existing persistence are sufficient.
    - Keep contracts explicit and tenant-safe.
    - Add concise code comments only where behavior is non-obvious, especially cursor semantics and post-persist live publication.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. Run tests after implementation:
   - `dotnet test`

4. Manually validate API behavior:
   - Request first page for a tenant feed
   - Confirm items are in reverse chronological order
   - Confirm response includes all required fields
   - Request subsequent page using returned cursor
   - Confirm no duplicates and correct continuation
   - Validate empty tenant/activity scope returns:
     - empty items
     - `nextCursor: null`

5. Manually validate authorization:
   - Use a user without membership to the target tenant
   - Confirm API returns HTTP 403
   - Confirm SignalR subscription/join is rejected or not joined for unauthorized tenant access

6. Manually validate live updates:
   - Open the Blazor feed UI for an authorized tenant
   - Trigger/persist a new agent activity event
   - Confirm the new event appears in the UI within 2 seconds
   - Confirm it is inserted at the top
   - Confirm no duplicate row appears if the same event is later encountered in paged results

7. Validate tenant isolation:
   - Open two tenants with different users/sessions if possible
   - Persist an event in tenant A
   - Confirm tenant B feed does not receive or display it

8. Validate pagination edge cases:
   - Seed multiple events with identical `occurredAt`
   - Confirm cursor pagination remains stable using the tiebreaker
   - Confirm final page returns `nextCursor: null`

9. If applicable, verify dashboard integration:
   - Ensure the feed component renders correctly in the cockpit/dashboard
   - Ensure empty/loading/error states are user-friendly and non-breaking

# Risks and follow-ups
- **Unclear existing source of “activity events”**  
  The codebase may not yet have a complete persisted activity/audit model with all required fields. If so, extend the existing audit/event persistence minimally rather than inventing a separate parallel model.

- **Real-time publication timing**  
  Publishing before transaction commit can leak uncommitted events; publishing too late via slow background processing may violate the 2-second requirement. Prefer post-commit immediate publish or a fast outbox dispatcher already present in the app.

- **Cursor correctness**  
  Timestamp-only cursors can skip/duplicate rows when multiple events share the same `occurredAt`. Use a composite cursor with a stable tiebreaker.

- **Tenant leakage risk**  
  This task is security-sensitive. Enforce tenant scope in:
  - endpoint authorization,
  - application query handling,
  - repository filtering,
  - SignalR group subscription checks.

- **UI duplication/race conditions**  
  Live updates plus pagination can create duplicates or ordering issues. Deduplicate by `eventId` and keep a single source of truth in the client state.

- **Performance follow-up**  
  If the feed grows large, add/verify DB indexes for tenant + chronological access, likely on fields equivalent to:
  - `company_id`
  - `occurred_at DESC`
  - `id DESC`

- **Future follow-ups, not required now**
  - filtering by agent/event type/status/date range
  - richer source metadata rendering
  - mobile feed support
  - unread markers
  - cached dashboard aggregation
  - outbox-backed fan-out hardening if event volume increases