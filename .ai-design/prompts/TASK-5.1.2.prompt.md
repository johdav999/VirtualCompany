# Goal
Implement backlog task **TASK-5.1.2 — Publish agent activity events to real-time subscription channel** for **US-5.1 Unified real-time agent activity feed** in the existing .NET modular monolith.

Deliver a tenant-scoped agent activity feed that:
- returns persisted activity events in **reverse chronological order**
- supports **cursor-based pagination**
- enforces **tenant isolation and authorization**
- returns **empty results with `nextCursor: null`** when no events exist
- publishes newly persisted activity events to a **real-time subscription channel** so connected clients receive them **within 2 seconds**

Use the existing architecture principles:
- ASP.NET Core backend
- PostgreSQL as source of truth
- shared-schema multi-tenancy with strict tenant filtering
- CQRS-lite application layer
- outbox/background-dispatcher friendly design
- business audit/activity data separated from technical logs

# Scope
In scope:
- Add or complete a domain/application/infrastructure path for **agent activity events**
- Add a **tenant-scoped query endpoint** for historical feed retrieval
- Add a **real-time subscription channel** for new activity delivery
- Ensure event payload includes:
  - `eventId`
  - `tenantId`
  - `agentId`
  - `eventType`
  - `occurredAt`
  - `status`
  - `summary`
  - `correlationId`
  - `source` metadata
- Enforce authorization and tenant isolation with **403 for unauthorized access**
- Add tests covering pagination, ordering, empty state, tenant isolation, and real-time delivery behavior

Out of scope:
- Broad dashboard UI work
- Mobile client implementation
- Reworking unrelated audit/event schemas unless required for this task
- Introducing external brokers unless already present and necessary
- Full notification/inbox features beyond this activity feed

If the codebase already contains partial agent activity, audit event, SignalR, SSE, GraphQL subscription, or outbox infrastructure, extend the existing pattern rather than creating a parallel implementation.

# Files to touch
Inspect the solution first and then update the minimum necessary files in the appropriate projects. Likely areas:

- `src/VirtualCompany.Domain/**`
  - activity event entity/value objects/enums if missing
- `src/VirtualCompany.Application/**`
  - feed query + DTOs
  - authorization handling
  - publisher abstraction for real-time fan-out
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/PostgreSQL persistence mapping
  - repository/query implementation
  - outbox/background dispatch integration if used
  - SignalR/SSE/subscription implementation
- `src/VirtualCompany.Api/**`
  - HTTP endpoint for feed retrieval
  - real-time hub/endpoint registration
  - auth/tenant policy wiring
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for API/hub payloads
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
  - real-time subscription tests if feasible in current test setup

Also inspect:
- `README.md`
- existing API startup/program configuration
- existing auth/tenant resolution middleware
- existing pagination conventions
- existing outbox/background worker patterns
- existing test fixtures/utilities

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect the solution for:
     - tenant resolution and authorization policies
     - existing audit/activity event models
     - existing pagination/cursor conventions
     - existing real-time stack: SignalR, SSE, WebSockets, or subscription abstraction
     - existing outbox/background dispatcher
   - Reuse naming, layering, and registration patterns already present.

2. **Define the activity event contract**
   - Introduce or align a canonical activity event read model with these fields:
     - `EventId`
     - `TenantId`
     - `AgentId`
     - `EventType`
     - `OccurredAt`
     - `Status`
     - `Summary`
     - `CorrelationId`
     - `Source`
   - `Source` should be structured metadata, likely a JSON object or typed DTO, but keep it stable and serializable.
   - If persistence already exists under audit/events tables, map from that source instead of duplicating data unless duplication is already the established read-model pattern.

3. **Implement persistence/query support**
   - Ensure the backing store can query activity events by tenant efficiently.
   - Query requirements:
     - filter by tenant only
     - sort by `OccurredAt DESC`, then a stable tiebreaker such as `EventId DESC`
     - support cursor-based pagination
   - Cursor should be opaque if the codebase already uses opaque cursors; otherwise encode enough information to continue stable pagination, e.g. `(occurredAt, eventId)`.
   - Empty result behavior must return:
     - `items: []`
     - `nextCursor: null`
   - Add/adjust indexes if needed for tenant + occurredAt ordering.

4. **Add application query handler**
   - Create a query/handler for tenant-scoped activity feed retrieval.
   - Validate caller authorization against the resolved tenant/company context.
   - Return **403** when the caller is authenticated but not authorized for the requested tenant scope.
   - Ensure no cross-tenant leakage in handler, repository, or controller layers.

5. **Add HTTP endpoint**
   - Add a tenant-scoped endpoint consistent with existing route conventions, for example:
     - `/api/tenants/{tenantId}/agent-activity`
     - or the project’s established company-scoped route pattern
   - Support query parameters for cursor and page size.
   - Response shape should include:
     - collection of activity events
     - `nextCursor`
   - Ensure reverse chronological ordering in the response.
   - Map unauthorized access attempts to **403** per acceptance criteria.

6. **Implement real-time subscription channel**
   - Prefer the project’s existing real-time mechanism:
     - **SignalR** if present or appropriate in ASP.NET Core
     - otherwise SSE if already used
   - Subscription must be **tenant-scoped**:
     - clients only receive events for their authorized tenant
     - do not broadcast globally
   - Recommended SignalR pattern if none exists:
     - authenticated hub
     - on connect, validate tenant membership
     - add connection to a tenant-specific group such as `tenant:{tenantId}:agent-activity`
   - Define a stable event name/payload contract for new activity events.

7. **Publish events after persistence**
   - Ensure newly persisted activity events are published to the real-time channel **after successful persistence**, not before.
   - Prefer reliable delivery through the existing **outbox + background dispatcher** if available.
   - If no outbox exists for this path, implement the smallest reliable mechanism consistent with architecture:
     - persist event
     - enqueue outbox message
     - background dispatcher publishes to hub/group
   - The implementation must support delivery to connected clients within **2 seconds** under normal operation.
   - Preserve correlation IDs through persistence and publication.

8. **Authorization and tenant isolation**
   - Enforce tenant scope in all layers:
     - endpoint/hub authorization
     - application handler
     - repository query
   - For unauthorized tenant access:
     - feed endpoint returns **403**
     - subscription connection/join should reject unauthorized tenant access
   - Avoid relying on client-supplied tenant IDs without server-side membership validation.

9. **Observability**
   - Add structured logs around:
     - event persistence
     - outbox dispatch/publication
     - subscription delivery attempts/failures
   - Include tenant context and correlation ID where available.
   - Keep technical logs separate from business activity records.

10. **Testing**
   - Add/extend tests for:
     - feed returns events in reverse chronological order
     - cursor pagination returns stable next pages
     - empty feed returns `items: []` and `nextCursor: null`
     - tenant A cannot read tenant B events and receives **403**
     - subscription only receives events for the authorized tenant
     - newly persisted events are delivered to connected clients within expected timing bounds where testable
   - Prefer integration tests over isolated unit tests for endpoint + auth + persistence behavior.

11. **Keep implementation incremental**
   - Do not refactor unrelated modules.
   - If a missing shared abstraction is required, add the narrowest useful interface and wire it through DI.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify feed endpoint:
   - Seed or create multiple activity events for one tenant
   - Call the tenant-scoped feed endpoint
   - Confirm:
     - newest events appear first
     - payload includes all required fields
     - pagination returns a valid `nextCursor`
   - Request a page after the final page and confirm `nextCursor` is `null`

4. Verify empty state:
   - Query a tenant/scope with no events
   - Confirm response contains an empty result set and `nextCursor: null`

5. Verify authorization:
   - Call the endpoint as a user without membership/access to the tenant
   - Confirm **HTTP 403**
   - Confirm no data from other tenants is returned under any circumstance

6. Verify real-time delivery:
   - Connect a client to the subscription channel for an authorized tenant
   - Persist a new activity event
   - Confirm the client receives the event payload within 2 seconds
   - Confirm payload fields match the persisted event contract

7. Verify tenant-scoped subscriptions:
   - Connect clients for two different tenants
   - Persist an event for tenant A
   - Confirm only tenant A subscribers receive it

8. Verify reliability path:
   - If using outbox/background dispatch, confirm:
     - outbox record is created
     - dispatcher publishes successfully
     - duplicate publication is avoided or idempotent per existing pattern

# Risks and follow-ups
- **Existing model ambiguity:** The codebase may already distinguish between audit events, notifications, and activity events. Reuse the correct source of truth and avoid creating overlapping concepts.
- **Real-time stack mismatch:** If no subscription mechanism exists, SignalR is the likely fit in ASP.NET Core, but confirm before introducing it.
- **Cursor correctness:** Reverse chronological pagination can produce duplicates/skips if the cursor is based only on timestamp. Use a stable secondary key such as `eventId`.
- **Authorization gaps:** Tenant checks must not live only in controllers; enforce them in application and data access paths too.
- **Delivery timing:** Meeting the 2-second requirement may depend on polling/dispatcher intervals if using outbox workers. Tune intervals conservatively and document assumptions.
- **Testing real-time behavior:** End-to-end timing assertions can be flaky. Prefer bounded integration tests with reasonable tolerances and deterministic hooks where possible.
- **Indexing/performance:** Large tenant event volumes may require a composite index on tenant and descending occurrence timestamp.
- **Follow-up suggestion:** If not already present, consider a dedicated read model for dashboard/activity feed consumption to keep audit persistence and UI query concerns decoupled.