# Goal
Implement backlog task **TASK-9.3.7 — Deletion must respect tenant boundaries and future privacy controls** for story **ST-303 Agent and company memory persistence**.

The coding agent should update the memory deletion/expiration flow so that:
- memory items can only be deleted or expired within the active tenant/company boundary,
- cross-tenant access is impossible through API, application, repository, or query paths,
- the design leaves a clear extension point for future privacy controls such as soft-delete, retention rules, legal hold, actor/audit metadata, and policy-based deletion restrictions,
- behavior is consistent with the architecture’s shared-schema multi-tenancy model using `company_id` enforcement.

Because no explicit acceptance criteria were provided for this task, derive implementation behavior from:
- ST-303 notes: “Deletion must respect tenant boundaries and future privacy controls.”
- ST-101 tenant-aware access requirements,
- architecture guidance around tenant-isolated data and auditability.

# Scope
In scope:
- Find the existing memory item delete/expire command, endpoint, handler, service, and repository/query path.
- Enforce tenant scoping on all deletion/expiration operations for `memory_items`.
- Prefer delete-by-`company_id` + `memory_item_id` semantics rather than delete-by-id only.
- Return safe not-found/forbidden behavior consistent with existing tenant isolation patterns.
- Add or extend domain/application contracts so future privacy controls can be layered in without redesign.
- Add tests covering:
  - same-tenant delete success,
  - cross-tenant delete failure/non-disclosure,
  - agent-specific and company-wide memory deletion within tenant,
  - expiration path if separate from hard delete,
  - audit/privacy metadata behavior if already present.

Out of scope unless required by existing code patterns:
- Full privacy policy engine,
- legal hold implementation,
- retention scheduler,
- UI redesign,
- broad refactors unrelated to memory deletion,
- introducing a new persistence technology.

If the codebase currently lacks delete/expire functionality, implement the minimum vertical slice necessary for memory deletion/expiration in the existing architectural style.

# Files to touch
Inspect first, then modify the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - memory entity/value objects/specifications if deletion state or privacy hooks belong in domain
- `src/VirtualCompany.Application/**`
  - commands/handlers for deleting or expiring memory items
  - interfaces for repositories/services
  - authorization/tenant context abstractions
  - DTOs/results
- `src/VirtualCompany.Infrastructure/**`
  - EF Core repository/query implementations
  - DbContext configurations for `memory_items`
  - migrations only if schema changes are necessary
- `src/VirtualCompany.Api/**`
  - memory endpoints/controllers/minimal APIs
  - request models and tenant-aware route handling
- `src/VirtualCompany.Web/**`
  - only if there is already a memory management UI wired to deletion/expiration
- tests in corresponding test projects
  - application tests
  - infrastructure integration tests
  - API endpoint tests

Also inspect:
- tenant resolution/access patterns already used for other tenant-owned entities,
- audit event creation patterns,
- any existing soft-delete or status/expiration conventions,
- any existing policy/authorization abstractions that should be reused.

# Implementation plan
1. **Discover current memory model and deletion flow**
   - Locate `memory_items` entity/model and all references.
   - Identify whether deletion is currently:
     - hard delete,
     - soft delete,
     - expiration via `valid_to`,
     - or not implemented.
   - Identify how tenant context is resolved in the app today.
   - Identify existing patterns for tenant-scoped commands on other entities like tasks, documents, approvals, or agents.

2. **Define the target behavior**
   - Deletion/expiration must require active tenant/company context.
   - The operation must target a memory item by both:
     - `companyId`
     - `memoryItemId`
   - Repository queries must filter by `company_id` before mutation.
   - Cross-tenant requests must not reveal whether the target exists in another tenant.
   - Prefer returning the same result shape used elsewhere for tenant-isolated missing resources, typically not found.

3. **Add an application-level contract that is privacy-ready**
   - If a delete command exists, evolve it to include:
     - `CompanyId`
     - `MemoryItemId`
     - optional actor metadata if consistent with current patterns
     - optional reason/comment if low-cost and useful
   - If no command exists, add one such as:
     - `DeleteMemoryItemCommand`
     - and/or `ExpireMemoryItemCommand`
   - Keep the contract extensible for future privacy controls. For example:
     - actor/user id,
     - deletion mode,
     - requested reason,
     - policy context placeholder.
   - Do not overengineer a full policy engine now; just avoid a dead-end API.

4. **Enforce tenant-scoped lookup and mutation**
   - In handler/service/repository, replace any `GetById` / `Delete(id)` logic with tenant-scoped access:
     - query memory item where `Id == memoryItemId && CompanyId == companyId`
   - Only mutate/delete if the tenant-scoped record is found.
   - If expiration is supported, ensure the update is also tenant-scoped.
   - If agent-specific memory exists, do not require `agent_id` for deletion unless current design does; `company_id` remains the primary tenant boundary.

5. **Preserve future privacy-control extension points**
   - If hard delete is currently used and no schema change is justified, keep implementation minimal but structure code so future policy checks can be inserted before mutation.
   - Introduce a small abstraction if helpful, e.g.:
     - `IMemoryPrivacyGuard`
     - `IMemoryDeletionPolicy`
     - or a private method in handler marked as the policy checkpoint
   - This checkpoint should currently enforce at least:
     - tenant match,
     - optional authorization if existing role checks already apply.
   - Add comments only where necessary to indicate future hooks for retention/legal hold/privacy rules.

6. **Audit if the project already audits business actions**
   - If memory deletion actions are already audited elsewhere, emit an audit event using existing patterns.
   - Include tenant/company id, actor, action, target id, and outcome.
   - Do not invent a parallel audit system if none exists in this slice.

7. **API/endpoint hardening**
   - Ensure endpoint/controller does not accept or act on a bare memory id without tenant context.
   - Use resolved company context from auth/session/request pipeline, not caller-supplied arbitrary tenant ids unless that is the established API pattern.
   - Validate route/request models.
   - Ensure response semantics do not leak cross-tenant existence.

8. **Data model changes only if necessary**
   - Prefer no schema change if tenant-safe deletion can be implemented with existing `company_id` and `valid_to`.
   - If the current model truly needs a privacy-ready field and the codebase already uses soft-delete conventions, consider a minimal additive schema change such as:
     - `deleted_at`,
     - `deleted_by_actor_id`,
     - `deletion_reason`
   - Only do this if it aligns with existing architecture and won’t create inconsistent patterns.
   - If adding schema, update EF configuration and create a migration.

9. **Testing**
   - Add unit/integration/API tests for:
     - deleting a memory item in the same tenant succeeds,
     - deleting a memory item from another tenant fails with non-leaking semantics,
     - expiring a memory item in the same tenant succeeds,
     - expiring another tenant’s memory item fails,
     - company-wide memory and agent-specific memory both respect tenant boundaries,
     - repository queries include `company_id` filter,
     - audit event emitted if applicable.
   - Prefer integration tests around persistence if possible, since tenant bugs often hide in query implementation.

10. **Keep implementation aligned with modular monolith boundaries**
   - Domain: entity invariants only if needed.
   - Application: command/use-case orchestration and policy checkpoint.
   - Infrastructure: EF/query enforcement.
   - API: transport and tenant-context wiring only.

# Validation steps
1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the relevant suites first:
   - application tests for memory commands/handlers
   - infrastructure tests for repository tenant scoping
   - API tests for endpoint behavior

4. Manually verify code paths:
   - confirm no delete/update path for memory items uses id-only lookup,
   - confirm all mutation queries include `company_id`,
   - confirm cross-tenant requests return safe non-disclosing results,
   - confirm expiration path, if separate, follows the same tenant enforcement,
   - confirm any audit event includes tenant context.

5. If migrations were added:
   - verify migration compiles,
   - verify DbContext model snapshot is updated correctly,
   - verify tests pass against migrated schema.

# Risks and follow-ups
- **Risk: hidden id-only repository methods**
  - There may be shared generic repository methods that bypass tenant filtering.
  - Follow-up: audit other memory read/update/delete paths for the same issue.

- **Risk: inconsistent not-found vs forbidden semantics**
  - Existing APIs may differ in how they mask cross-tenant access.
  - Follow-up: align with the project’s established tenant isolation convention.

- **Risk: future privacy controls need soft delete**
  - Hard delete may satisfy current task but limit retention/legal hold features.
  - Follow-up: consider a dedicated privacy lifecycle for memory items:
    - active,
    - expired,
    - soft-deleted,
    - purged.

- **Risk: authorization and tenant isolation are conflated**
  - Tenant boundary checks must happen even if role authorization exists.
  - Follow-up: ensure both are enforced independently.

- **Risk: retrieval paths may still surface expired/deleted memory**
  - Deletion work may be correct while retrieval still returns stale records.
  - Follow-up: audit ST-303/ST-304 retrieval filters for `valid_to` and any future deletion markers.

- **Risk: missing auditability**
  - If deletion is not audited, future compliance review may be harder.
  - Follow-up: add business audit coverage for memory lifecycle actions if not already present.