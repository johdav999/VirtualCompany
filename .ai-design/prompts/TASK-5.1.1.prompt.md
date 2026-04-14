# Goal
Implement backlog task **TASK-5.1.1 — Implement tenant-scoped activity event store and feed query API** for **US-5.1 Unified real-time agent activity feed** in the existing .NET solution.

Deliver a production-ready vertical slice that:
- persists tenant-scoped agent activity events,
- exposes a tenant-scoped feed API returning events in **reverse chronological order**,
- supports **cursor-based pagination**,
- enforces **tenant authorization/isolation**,
- returns **empty results with `nextCursor: null`** when no events exist,
- and provides a **real-time subscription channel** that delivers newly persisted events to connected authorized clients within **2 seconds**.

Use the existing architecture conventions:
- ASP.NET Core modular monolith
- CQRS-lite application layer
- PostgreSQL transactional persistence
- tenant isolation via `company_id` / `tenantId`
- policy-based authorization
- clean boundaries across Api / Application / Domain / Infrastructure

# Scope
In scope:
- Add an **activity event domain model** and persistence mapping.
- Add PostgreSQL migration(s) for an activity event store table.
- Add application-layer command/query contracts and handlers for:
  - persisting activity events,
  - querying paginated feed results.
- Add API endpoint(s) for querying the activity feed.
- Add real-time subscription support using the project’s existing ASP.NET Core patterns; if no real-time mechanism exists yet, implement **SignalR** as the subscription channel.
- Enforce tenant scoping and unauthorized access behavior:
  - no cross-tenant leakage,
  - return **403** for unauthorized access attempts.
- Add tests covering ordering, pagination, empty state, tenant isolation, unauthorized access, and real-time delivery behavior.

Out of scope unless required to complete this task cleanly:
- Full dashboard UI implementation.
- Mobile client implementation.
- Broad notification framework.
- Event sourcing conversion.
- Non-agent activity domains beyond the generic event store shape needed here.

Assumptions to follow:
- Treat **tenantId** as the company/workspace scope.
- Activity events are business-facing operational events, not low-level technical logs.
- If an existing audit/event model already partially fits, reuse patterns but do **not** overload unrelated tables if that would weaken the acceptance criteria.

# Files to touch
Inspect the solution first and then update the most appropriate files. Expect to touch files in these areas:

- **Domain**
  - `src/VirtualCompany.Domain/...`
  - Add activity event entity/value objects/enums if missing.

- **Application**
  - `src/VirtualCompany.Application/...`
  - Add commands/queries, DTOs, validators, handlers, and interfaces.

- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/...`
  - Add EF Core configuration/repository/query implementation.
  - Add migration support / SQL mapping.
  - Add real-time publisher implementation.

- **API**
  - `src/VirtualCompany.Api/...`
  - Add feed endpoint(s), auth policy wiring, SignalR hub/endpoints, DI registration, and request/response contracts.

- **Tests**
  - `tests/VirtualCompany.Api.Tests/...`
  - Add integration/API tests and, if applicable, application tests.

Also inspect:
- existing tenant resolution/auth code,
- existing pagination conventions,
- existing cursor patterns,
- existing SignalR usage if any,
- existing migration approach in repo docs such as `docs/postgresql-migrations-archive/README.md`.

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how the solution currently handles:
     - tenant resolution,
     - authorization policies,
     - EF Core entities/configurations,
     - migrations,
     - API route conventions,
     - pagination DTOs,
     - real-time infrastructure,
     - audit/event persistence.
   - Reuse naming and folder conventions already present in the repo.

2. **Design the activity event model**
   - Create a tenant-owned activity event entity with at least these fields:
     - `EventId`
     - `TenantId`
     - `AgentId`
     - `EventType`
     - `OccurredAt`
     - `Status`
     - `Summary`
     - `CorrelationId`
     - `SourceMetadata`
   - `SourceMetadata` can be JSON/JSONB-backed metadata.
   - Keep the model extensible for future feed consumers.
   - Prefer immutable or controlled mutation patterns consistent with the codebase.

3. **Add persistence**
   - Create a PostgreSQL table for activity events, likely something like `activity_events`.
   - Include indexes optimized for:
     - tenant-scoped reverse chronological queries,
     - stable cursor pagination.
   - Recommended ordering key:
     - primary sort: `occurred_at DESC`
     - tie-breaker: `id DESC`
   - Add indexes such as:
     - `(tenant_id, occurred_at DESC, id DESC)`
     - optionally `(tenant_id, agent_id, occurred_at DESC, id DESC)` if agent filtering is already natural in the API design.
   - Ensure JSONB mapping for source metadata if using EF Core.

4. **Implement cursor-based pagination**
   - Use a stable opaque cursor encoding enough information to continue pagination safely, e.g.:
     - `occurredAt`
     - `eventId`
   - Query semantics:
     - first page returns newest events,
     - subsequent pages return older events,
     - reverse chronological order is preserved,
     - `nextCursor` is `null` when there are no more results.
   - Avoid offset pagination.
   - Ensure deterministic ordering when multiple events share the same timestamp.

5. **Implement application query API**
   - Add a query and handler for fetching the tenant-scoped activity feed.
   - Response shape should include:
     - collection of events,
     - `nextCursor`.
   - If no events exist, return:
     - empty collection,
     - `nextCursor: null`.
   - Ensure the query always filters by resolved authorized tenant context.

6. **Implement event persistence path**
   - Add an application service/command for persisting activity events.
   - This can be internal-facing if no public create endpoint is required by the task.
   - On successful persistence, trigger publication to the real-time subscription channel.
   - Preserve `CorrelationId` through persistence and publication.

7. **Implement real-time subscription channel**
   - If no existing mechanism exists, add a **SignalR hub** for activity feed subscriptions.
   - Authorize hub connections and bind them to tenant-specific groups, e.g. one group per tenant.
   - On event persistence, publish the event to the correct tenant group only.
   - Delivery target: connected authorized clients receive the event within 2 seconds of persistence.
   - Keep payload aligned with feed DTO shape.
   - Do not broadcast across tenants.

8. **Implement API endpoint**
   - Add a tenant-scoped endpoint, likely under an API route consistent with existing conventions, for example:
     - `GET /api/tenants/{tenantId}/activity-feed`
     - or the project’s existing company-scoped route style.
   - Support query params such as:
     - `cursor`
     - `pageSize`
     - optional scope filters only if already implied by existing patterns.
   - Return **403** when the caller is authenticated but not authorized for the tenant.
   - Ensure no cross-tenant data is returned under any circumstance.

9. **Authorization and tenant isolation**
   - Reuse existing membership/tenant authorization policies from ST-101 foundations.
   - Explicitly verify:
     - authenticated user belongs to tenant,
     - requested tenant matches authorized context,
     - hub subscription is tenant-authorized.
   - Prefer centralized policy checks over ad hoc controller logic where possible.

10. **Testing**
   - Add tests for:
     - reverse chronological ordering,
     - stable cursor pagination across multiple pages,
     - empty result set with `nextCursor = null`,
     - tenant isolation,
     - unauthorized tenant access returns 403,
     - real-time event delivery to authorized subscribers,
     - no delivery to other-tenant subscribers.
   - If feasible, add a timing-tolerant integration test asserting delivery occurs within 2 seconds.
   - Keep tests deterministic.

11. **Documentation/comments**
   - Add concise code comments only where behavior is non-obvious.
   - If the repo has API docs/OpenAPI conventions, update them.
   - If a migration or setup step is needed, document it minimally in the relevant place.

Implementation notes:
- Prefer DTOs for API contracts rather than exposing domain entities directly.
- Keep `summary` concise and user-facing.
- Use UTC timestamps.
- Ensure `tenantId` naming is consistent with existing codebase conventions; if the codebase uses `CompanyId`, map carefully but satisfy the API contract.
- If there is already an `audit_events` table/entity, do not force-fit this feature into it unless it cleanly satisfies feed semantics and required fields. A dedicated `activity_events` store is preferred.

Suggested response contract shape:
```json
{
  "items": [
    {
      "eventId": "uuid",
      "tenantId": "uuid",
      "agentId": "uuid",
      "eventType": "task_completed",
      "occurredAt": "2026-01-01T12:00:00Z",
      "status": "completed",
      "summary": "Agent completed invoice review",
      "correlationId": "uuid-or-string",
      "sourceMetadata": {
        "sourceType": "task",
        "sourceId": "uuid"
      }
    }
  ],
  "nextCursor": "opaque-string-or-null"
}
```

Suggested SignalR behavior:
- Hub name: align with repo conventions, e.g. `ActivityFeedHub`
- Server event name: e.g. `activityEventReceived`
- Payload: single activity event DTO
- Tenant group membership established only after authorization

# Validation steps
Run and verify all relevant checks locally.

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual/API validation**
   - Create/persist several activity events for tenant A and tenant B.
   - Query tenant A feed:
     - confirm only tenant A events are returned,
     - confirm newest-first ordering,
     - confirm cursor pagination returns the next older page,
     - confirm final page returns `nextCursor: null`.
   - Query a tenant with no events:
     - confirm `items: []`,
     - confirm `nextCursor: null`.
   - Attempt access as a user without membership to the tenant:
     - confirm **403**.

4. **Real-time validation**
   - Connect two authorized clients to tenant A subscription.
   - Persist a new tenant A event.
   - Confirm both tenant A clients receive the event within 2 seconds.
   - Connect a tenant B client and confirm it does **not** receive tenant A events.
   - Attempt unauthorized hub subscription and confirm it is rejected or isolated.

5. **Data validation**
   - Inspect generated migration/schema.
   - Confirm indexes support tenant + chronological queries.
   - Confirm timestamps are UTC and cursor ordering is stable for identical timestamps.

# Risks and follow-ups
- **Tenant naming mismatch**: the codebase may use `CompanyId` instead of `TenantId`. Preserve internal consistency while exposing the required API contract cleanly.
- **Existing auth conventions**: avoid bypassing established tenant authorization middleware/policies.
- **Cursor correctness**: incorrect tie-breaker logic can cause duplicates or skipped records; use `(occurredAt, eventId)` ordering consistently.
- **Real-time delivery coupling**: publishing before transaction commit can leak uncommitted events. Prefer publish-after-successful-persist semantics.
- **SignalR auth/grouping**: group assignment must be tenant-authorized to prevent cross-tenant leakage.
- **Migration workflow uncertainty**: follow the repo’s documented migration approach rather than inventing a new one.
- **Future follow-up**:
  - add richer feed filters (agent, event type, status, date range),
  - add dashboard UI consumption,
  - consider outbox-backed real-time fan-out if reliability requirements increase,
  - add retention/archival strategy for high-volume activity events.