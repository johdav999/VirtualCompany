# Goal
Implement backlog task **TASK-9.3.4** for story **ST-303 Agent and company memory persistence**: enable users to **delete** or **expire** memory items under **policy-controlled, tenant-scoped rules** in the existing .NET modular monolith.

This work should fit the architecture and story intent:
- multi-tenant, company-scoped data access
- memory items stored in PostgreSQL
- policy-enforced operations
- auditability as a business feature
- no raw chain-of-thought exposure
- deletion/expiration must support future privacy controls

Deliver a production-ready vertical slice across domain, application, infrastructure, and API layers, with tests.

# Scope
Implement the minimum coherent feature set required for this task:

1. **Memory lifecycle operations**
   - Support **soft deletion** of memory items.
   - Support **expiration** of memory items by setting an effective end-of-validity timestamp/status.
   - Preserve historical/audit trace even when a memory item is deleted from active use.

2. **Policy controls**
   - Only authorized human users can delete or expire memory items.
   - Enforce:
     - tenant/company boundary
     - role/permission checks
     - optional policy restrictions by memory type and scope
   - Default to conservative behavior if policy configuration is absent.

3. **Retrieval behavior**
   - Deleted memory items must not be returned by normal retrieval.
   - Expired memory items must not be returned as active memory unless explicitly requested by an internal/admin query path.
   - Existing retrieval filters should continue to support agent/company scope, recency, salience, and semantic relevance.

4. **Auditability**
   - Record business audit events for delete and expire actions.
   - Include actor, target, action, outcome, and concise rationale/metadata.

5. **API/application surface**
   - Add command endpoints/use cases for:
     - delete memory item
     - expire memory item
   - Return safe error responses for forbidden/not found/invalid operations.

6. **Tests**
   - Unit and/or integration tests covering:
     - tenant isolation
     - authorization/policy enforcement
     - delete behavior
     - expire behavior
     - retrieval exclusion of deleted/expired items

Out of scope unless already trivial in the codebase:
- full UI management screens
- bulk delete/expire
- retention scheduler/background purge
- hard physical deletion from database
- advanced privacy workflows beyond the policy hooks needed now

# Files to touch
Inspect the solution first and adapt to actual project structure/naming, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - memory item entity/aggregate
  - domain enums/value objects for lifecycle/status
  - domain policy/authorization abstractions if present

- `src/VirtualCompany.Application/**`
  - commands/handlers for delete and expire
  - DTOs/contracts
  - validators
  - authorization/policy service interfaces
  - query/retrieval handlers updated to exclude deleted/expired items by default

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository updates
  - migrations
  - policy implementation if stored/config-driven
  - audit event persistence
  - retrieval query filtering

- `src/VirtualCompany.Api/**`
  - endpoints/controllers/minimal APIs for delete/expire operations
  - request/response contracts if API-owned
  - authorization wiring

- `src/VirtualCompany.Shared/**`
  - shared contracts/enums only if this solution uses shared API models

- Tests under existing test projects
  - command handler tests
  - repository/query tests
  - API/integration tests if available

Also review:
- `README.md`
- existing memory/retrieval implementation for ST-303
- existing audit event patterns
- existing tenant resolution and authorization patterns
- existing migration conventions

# Implementation plan
1. **Discover current memory model and conventions**
   - Find the existing `memory_items` entity/table mapping.
   - Confirm whether ST-303 base persistence/retrieval already exists.
   - Identify current patterns for:
     - tenant scoping
     - current user/company context
     - authorization
     - audit event creation
     - soft delete/status fields in other modules
   - Reuse established conventions rather than inventing new patterns.

2. **Extend the memory domain model for lifecycle state**
   - Add explicit lifecycle support to memory items if not already present.
   - Prefer fields such as:
     - `deleted_at timestamptz null`
     - `deleted_by_actor_type text null`
     - `deleted_by_actor_id uuid null`
     - `expiration_reason text null` or metadata entry
   - If the project already uses a status enum, align with it, e.g.:
     - `active`
     - `expired`
     - `deleted`
   - Keep `valid_to` as the canonical expiration boundary if that matches the architecture.
   - Add domain methods like:
     - `Expire(at, actor, reason)`
     - `Delete(at, actor, reason)`
   - Guard against invalid transitions:
     - cannot expire deleted item
     - deleting an already deleted item should be idempotent or return a domain/application validation error based on existing conventions
     - expiring an already expired item should be idempotent or validated consistently

3. **Define policy rules for memory management**
   - Implement a focused policy check for memory lifecycle actions.
   - At minimum enforce:
     - current user belongs to target company
     - user has appropriate role/permission
     - operation is allowed for the memory scope/type
   - Suggested permission model if no finer-grained one exists:
     - owner/admin can delete or expire any company memory item in tenant
     - manager may expire but not delete, or only manage agent-scoped memory they are allowed to administer
   - If the codebase already has permission constants/policies, add:
     - `Memory.Delete`
     - `Memory.Expire`
   - Default deny on ambiguous policy state.

4. **Add application commands**
   - Create commands such as:
     - `DeleteMemoryItemCommand`
     - `ExpireMemoryItemCommand`
   - Include:
     - `CompanyId`
     - `MemoryItemId`
     - optional `Reason`
     - for expire: optional `ExpiresAt` (default now if not provided)
   - In handlers:
     - resolve current actor/user context
     - load memory item by `company_id` + `id`
     - return not found if outside tenant
     - enforce policy
     - apply domain operation
     - persist changes
     - create audit event
   - Return concise result DTOs with updated lifecycle state.

5. **Update persistence and database schema**
   - Add migration(s) for any new columns/indexes.
   - Ensure indexes support active retrieval efficiently, e.g. partial or composite indexes involving:
     - `company_id`
     - `agent_id`
     - `deleted_at`
     - `valid_to`
   - Update EF configurations and repository mappings.
   - If embeddings/vector rows remain on the same record, do not physically remove them for now.

6. **Update retrieval logic**
   - Modify all normal memory retrieval paths to exclude:
     - deleted items
     - expired items where `valid_to <= now` or status indicates expired
   - If there is an admin/internal query path, make inclusion of inactive items explicit via a flag like:
     - `IncludeExpired`
     - `IncludeDeleted`
   - Ensure semantic retrieval still applies tenant and lifecycle filters before/with ranking.
   - Verify no orchestration path can accidentally retrieve deleted memory.

7. **Add API endpoints**
   - Expose endpoints consistent with existing API style, for example:
     - `DELETE /api/companies/{companyId}/memory/{memoryItemId}`
     - `POST /api/companies/{companyId}/memory/{memoryItemId}/expire`
   - Request body for expire may include:
     - `expiresAt`
     - `reason`
   - Map outcomes to:
     - `204 No Content` or `200 OK` for success per existing conventions
     - `403 Forbidden`
     - `404 Not Found`
     - `400 Bad Request` for invalid state/input
   - Do not leak cross-tenant existence in errors.

8. **Create audit events**
   - Persist business audit events for both operations.
   - Suggested actions:
     - `memory_item_deleted`
     - `memory_item_expired`
   - Include:
     - actor type/id
     - company id
     - target type/id
     - outcome
     - rationale summary/reason
     - relevant metadata such as memory type, agent scope, previous/new validity state
   - Follow existing audit schema and helper patterns.

9. **Add tests**
   - Domain tests:
     - expire active item
     - delete active item
     - invalid transitions
   - Application tests:
     - cannot operate across tenant boundary
     - unauthorized user denied
     - authorized user succeeds
     - audit event emitted
   - Retrieval tests:
     - deleted item excluded
     - expired item excluded
     - active item still returned
   - API/integration tests if available:
     - delete endpoint success/forbidden/not found
     - expire endpoint success/forbidden/not found

10. **Keep implementation aligned with architecture**
   - No direct DB access from API.
   - Keep CQRS-lite separation.
   - Keep policy checks pre-operation.
   - Keep auditability in business tables, not only logs.
   - Preserve tenant isolation in every query.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation:
   - run targeted tests for domain/application/infrastructure/API
   - then run full suite:
     - `dotnet test`

4. Validate migration generation/application if migrations are used:
   - generate migration using the solution’s existing EF workflow
   - verify schema includes lifecycle fields/indexes
   - verify app still builds after migration

5. Manually verify behavior through API/integration tests:
   - create or seed a memory item in company A
   - expire it as authorized user in company A
   - confirm retrieval no longer returns it
   - delete another memory item as authorized user in company A
   - confirm retrieval no longer returns it
   - attempt same operations from company B context
   - confirm forbidden/not found without data leakage

6. Verify auditability:
   - confirm delete/expire actions create audit records with correct actor, target, and outcome

7. Verify no regression in retrieval/orchestration:
   - existing memory retrieval tests still pass
   - semantic retrieval paths exclude inactive memory by default

# Risks and follow-ups
- **Schema mismatch risk:** the current codebase may already model memory lifecycle differently than the architecture doc. Reuse existing patterns instead of forcing a new status model.
- **Authorization ambiguity:** no explicit acceptance criteria define exact roles for delete vs expire. Choose conservative defaults and document them in code comments/tests.
- **Soft delete vs hard delete:** this task should prefer soft delete for auditability and future privacy controls unless the repository already has an established deletion pattern.
- **Vector retrieval filtering:** ensure lifecycle filters are applied in semantic retrieval queries, not only in post-processing, to avoid irrelevant ranking/results.
- **Policy evolution:** future work may require configurable retention/privacy policies by memory type, region, or agent scope.
- **UI gap:** if no admin UI exists yet, API-only support is acceptable for this task, but a follow-up backlog item may be needed for web management UX.
- **Background expiration handling:** if future policy requires automatic expiry/purge, add scheduled jobs later rather than overbuilding now.
- **Data privacy follow-up:** hard-delete/anonymization workflows may later be required for compliance requests; keep the model extensible for that path.