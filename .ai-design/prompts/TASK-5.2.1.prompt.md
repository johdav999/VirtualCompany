# Goal

Implement **TASK-5.2.1 — Add correlation model and entity link resolution service for activity events** for **US-5.2 Correlated action timeline across tasks, workflows, approvals, and tools**.

The coding agent should add the backend/domain/application/infrastructure support needed to:

- model correlation across activity/audit records using a shared `correlationId`
- resolve linked entities for a selected activity item
- return linked entity summaries and current statuses without requiring a full page reload
- handle missing/deleted linked entities gracefully
- enforce strict tenant isolation and existing authorization rules
- support performant correlation queries for up to **10,000 related events** under **1 second** in test fixtures

This task should fit the existing modular monolith architecture and CQRS-lite approach, using the .NET backend and PostgreSQL.

# Scope

In scope:

- Add or complete a **correlation domain model** for activity events and linked entities
- Extend the persistence model for audit/activity records so correlation queries are first-class
- Implement an **entity link resolution service** that can resolve:
  - task
  - workflow instance
  - approval
  - tool execution
- Return a correlation/timeline DTO suitable for UI consumption
- Return linked entity status summaries for a selected activity item
- Represent unavailable/missing/deleted linked entities explicitly in API/application responses
- Enforce:
  - same-tenant restriction
  - existing authorization checks per entity type
- Add query handlers/endpoints needed by web/mobile UI to fetch:
  - grouped timeline by `correlationId`
  - linked entities for a selected activity item
- Add indexes/query optimizations and performance-oriented tests/fixtures
- Add unit/integration tests for correctness, authorization, missing entities, and performance-sensitive query shape

Out of scope unless required by existing code patterns:

- Full UI redesign
- Broad audit subsystem rewrite
- New auth model
- Cross-tenant correlation
- Background event ingestion redesign
- Mobile-specific UI work

If the repo already has partial activity/audit timeline code, extend it rather than duplicating it.

# Files to touch

Touch the minimum set needed, likely across these areas:

- `src/VirtualCompany.Domain/**`
  - add/update correlation-related entities/value objects/enums
  - add linked entity reference model if missing
- `src/VirtualCompany.Application/**`
  - queries/handlers for correlated activity timeline
  - DTOs/view models for activity items and linked entity summaries
  - service interfaces for entity link resolution
  - authorization-aware application services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations/mappings
  - repositories/query services
  - SQL/index configuration
  - concrete entity link resolution service
  - migration support if migrations are stored in-project
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for correlation timeline and activity item detail/link resolution
- `src/VirtualCompany.Web/**`
  - only if a minimal endpoint consumer or interactive partial is already present and needed for acceptance criteria
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- potentially other test projects if present for application/infrastructure layers

Also inspect before editing:

- existing audit/activity event model
- existing task/workflow/approval/tool execution query services
- tenant context and authorization abstractions
- migration conventions in repo
- any existing correlation ID handling from ST-502/ST-404/ST-104

# Implementation plan

1. **Inspect current architecture and existing models**
   - Find current representations for:
     - audit events
     - activity feed/timeline items
     - tasks
     - workflow instances
     - approvals
     - tool executions
   - Identify whether `correlationId` already exists anywhere in:
     - domain entities
     - API contracts
     - logging/observability
     - outbox/messages
   - Reuse existing patterns for:
     - tenant scoping
     - authorization
     - CQRS queries
     - EF Core mappings
     - endpoint style

2. **Define the correlation model**
   - Ensure the activity/audit event model supports:
     - `CorrelationId`
     - `CompanyId`
     - event timestamp
     - actor metadata
     - action/outcome
     - target entity reference
     - optional linked entity references
   - If the current audit/activity entity is incomplete, extend it conservatively.
   - Add a normalized linked entity reference structure, e.g. conceptually:
     - entity type
     - entity id
     - display label/title if available
     - status snapshot optional
   - Prefer explicit entity type enum/string constants over free-form magic strings if the codebase supports that.

3. **Design timeline query contracts**
   - Add application query/response models for:
     - `GetCorrelationTimelineQuery(correlationId, selectedActivityId?)`
     - `GetActivityLinkedEntitiesQuery(activityEventId)` or equivalent
   - Timeline response should include:
     - correlation id
     - ordered activity items
     - each item’s primary target
     - linked entities summary
     - unavailable state where applicable
   - Selected activity detail response should include current linked entity statuses resolved live from source entities where authorized.

4. **Implement entity link resolution service**
   - Add an application interface such as:
     - `IEntityLinkResolutionService`
   - Implement in infrastructure with per-entity-type resolution logic for:
     - tasks
     - workflow instances
     - approvals
     - tool executions
   - Service behavior:
     - resolve only within current tenant/company
     - apply existing authorization rules before returning entity details
     - if entity not found or deleted, return a safe unavailable result
     - do not fail the whole correlation response because one linked entity is unavailable
   - Return a normalized result model with states like:
     - available
     - unavailable_missing
     - unavailable_deleted
     - unavailable_forbidden
   - If authorization semantics in the product require hiding forbidden existence, map forbidden to a safe unavailable/not-found style consistent with existing API behavior.

5. **Implement correlation timeline query**
   - Query activity events by:
     - `company_id`
     - `correlation_id`
   - Order by event timestamp ascending or according to existing timeline convention
   - Include enough metadata to avoid N+1 issues
   - Resolve linked entities in a batched manner where possible
   - Ensure one missing entity does not break the timeline
   - If selected activity item support belongs in same response, include a focused linked-entity detail section for the selected item

6. **Optimize persistence and query performance**
   - Add/verify indexes for correlation lookup, likely on combinations such as:
     - `(company_id, correlation_id, created_at)`
     - `(company_id, id)` if not already present
     - any foreign-key/entity reference columns used in resolution
   - Avoid loading full entity graphs unnecessarily
   - Use projection queries instead of materializing large aggregates where possible
   - Batch linked entity fetches by type
   - Keep query count bounded for 10,000-event fixtures
   - If needed, add lightweight cached lookups only if consistent with current architecture; prefer efficient SQL first

7. **Add API surface**
   - Expose endpoints consistent with existing API conventions, for example conceptually:
     - `GET /api/activity/correlations/{correlationId}`
     - `GET /api/activity/{activityEventId}/links`
   - Ensure endpoints:
     - require authenticated tenant context
     - enforce authorization
     - return stable DTOs for partial UI refresh
   - Do not require full page reload semantics; backend should support incremental fetches cleanly.

8. **Handle unavailable states explicitly**
   - Ensure response contracts include a visible unavailable state for linked entities
   - Include enough metadata for UI rendering, e.g.:
     - entity type
     - entity id
     - availability state
     - display text/fallback text
     - current status if available
   - Missing/deleted entities must not throw unhandled exceptions or null-reference failures.

9. **Testing**
   - Add unit tests for:
     - correlation grouping by `correlationId`
     - tenant isolation
     - authorization filtering
     - missing/deleted entity handling
     - mixed entity-type resolution
   - Add integration/API tests for:
     - timeline endpoint returns grouped related records
     - selecting an activity item returns linked entities and statuses
     - unauthorized cross-tenant/cross-scope access is blocked
     - unavailable linked entity renders as explicit unavailable state in response
   - Add performance-oriented test fixture for up to 10,000 related events
     - verify query completes under target in test environment as reasonably as possible
     - if strict wall-clock assertions are flaky, at minimum validate efficient query shape and add a benchmark/integration test with documented threshold expectations

10. **Document assumptions in code**
   - If the repo lacks a formal activity event table and uses `audit_events`, implement correlation on top of that rather than inventing a parallel store unless clearly necessary.
   - If soft-delete semantics differ by entity type, normalize them in the resolution service.
   - Keep contracts extensible for future entity types.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration/model alignment
   - If EF migrations are used in-project, add/update migration and ensure schema includes needed columns/indexes.
   - Confirm correlation-related columns are mapped correctly.

4. Functional verification
   - Seed or create:
     - one tenant/company
     - multiple activity/audit events sharing the same `correlationId`
     - linked task, workflow instance, approval, and tool execution records
   - Call correlation timeline endpoint and verify:
     - all same-correlation events are grouped
     - ordering is correct
     - linked entities appear
   - Delete or simulate missing linked entity and verify:
     - response includes explicit unavailable state
     - remaining correlation view still returns successfully

5. Authorization verification
   - Verify same-tenant access succeeds
   - Verify different-tenant access returns forbidden/not found per existing conventions
   - Verify entity-specific authorization rules are respected for tasks/workflows/approvals/tools

6. Performance verification
   - Use a fixture with up to 10,000 related events for one correlation id
   - Measure the single-item correlation query path
   - Confirm response time is under 1 second in the intended test fixture environment, or document any environment-specific caveat while still optimizing query shape and indexes

7. API contract verification
   - Ensure DTOs are null-safe and stable
   - Ensure unavailable states are explicit and machine-readable
   - Ensure no full-page reload assumptions are embedded in backend contracts

# Risks and follow-ups

- **Existing model ambiguity:** The repo excerpt shows `audit_events` but the schema snippet is truncated. Confirm whether this table already includes correlation and target references before adding new structures.
- **Authorization complexity:** Existing authorization rules may differ by module. Reuse current policy/services instead of duplicating logic in the resolver.
- **Performance risk:** Naive per-item resolution will create N+1 queries and fail the 10,000-event target. Batch by entity type and project only required fields.
- **Deleted entity semantics:** Some modules may hard-delete while others soft-delete. Normalize to a common unavailable state contract.
- **API/UI contract drift:** If UI already expects a different activity DTO shape, adapt carefully and preserve backward compatibility where possible.
- **Migration location uncertainty:** The workspace includes archived migration docs; inspect actual migration strategy before generating schema changes.
- **Follow-up likely needed:** a dedicated Blazor interactive timeline component may be a separate task if not already present. This task should provide the backend/query contracts that enable no-reload linked-entity detail fetching.