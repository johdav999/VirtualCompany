# Goal

Implement `TASK-10.3.3` for `ST-403` so that approval approve/reject decisions:

- update the linked entity state correctly
- support single-step and ordered multi-step approval chains
- are persisted as business-auditable events
- prevent execution/progression for rejected, expired, or cancelled approvals

This work must fit the existing modular monolith and .NET architecture, preserve tenant isolation, and follow CQRS-lite patterns already used in the solution.

# Scope

In scope:

- Approval decision handling in the Approval module/application layer
- Updating linked entity state when an approval is approved or rejected
- Supporting both:
  - single-step approvals
  - ordered multi-step approval chains
- Persisting audit records for approval decisions and resulting entity state transitions
- Enforcing explicit handling for:
  - approved
  - rejected
  - expired
  - cancelled
- Tenant-scoped authorization and data access
- Tests covering decision flow, entity state transitions, and audit persistence

Out of scope unless already partially implemented and required to complete this task:

- Full approval inbox UX
- Mobile-specific UI work
- New workflow designer capabilities
- Notification fan-out beyond any existing hooks
- Broad refactors unrelated to approval decision processing

Assumptions to validate in the codebase before implementation:

- There is already an `Approval` aggregate/entity and likely `ApprovalStep`
- There are existing task/workflow/action entities with statuses such as `awaiting_approval`
- There is some audit event model or infrastructure already present, or a clear place to add it
- There are existing command handlers/endpoints for approval decisions, or a natural place to add them

# Files to touch

Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - approval entities/value objects/enums
  - task/workflow/action status logic
  - audit event domain models if present
- `src/VirtualCompany.Application/**`
  - commands/handlers for approve/reject
  - DTOs/contracts for decision requests/responses
  - application services coordinating linked entity updates
- `src/VirtualCompany.Infrastructure/**`
  - repositories / EF Core mappings / persistence logic
  - audit event persistence
  - transaction handling / outbox hooks if present
- `src/VirtualCompany.Api/**`
  - approval decision endpoints/controllers if needed
  - request validation / authorization wiring
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for approve/reject flows
- Potentially migration-related location if schema changes are required
  - check current migration approach from `docs/postgresql-migrations-archive/README.md`

Before coding, identify the exact concrete files by searching for:

- `Approval`
- `ApprovalStep`
- `approvals`
- `approval_steps`
- `AuditEvent` / `audit_events`
- task/workflow status enums
- existing approve/reject endpoints or handlers

# Implementation plan

1. **Discover current approval and audit implementation**
   - Inspect domain, application, infrastructure, and API layers for:
     - approval entities and statuses
     - approval step sequencing
     - linked entity references (`entity_type`, `entity_id`)
     - task/workflow/action status models
     - audit event persistence
   - Determine whether decisions are currently stored but not propagating state, or whether the full decision flow is missing.

2. **Define explicit decision/state transition rules**
   - Implement or codify clear rules for approval decisions:
     - `pending` approval + current active step approved:
       - if more ordered steps remain, mark current step approved and activate next step
       - linked entity remains in awaiting state
     - final required step approved:
       - mark approval `approved`
       - set `decided_at`
       - update linked entity from `awaiting_approval` to the appropriate next state
     - any required step rejected:
       - mark step rejected
       - mark approval `rejected`
       - set `decided_at`
       - update linked entity to a rejection/failure/blocked state appropriate to entity type
     - `expired` or `cancelled` approvals:
       - ensure linked action does not execute/progress
       - linked entity remains blocked/cancelled/awaiting intervention per existing domain conventions
   - Reuse existing status vocabulary where possible; do not invent inconsistent statuses if the project already has conventions.

3. **Model linked entity state updates behind a dedicated application/domain abstraction**
   - Add a focused service/strategy such as `IApprovalLinkedEntityStateUpdater` or equivalent if no clean abstraction exists.
   - It should:
     - resolve the linked entity by `entity_type` and `entity_id`
     - enforce `company_id` tenant scope
     - apply the correct state transition for approve/reject/expire/cancel
     - persist changes in the same transaction as the approval decision
   - Support at least the entity types required by the story/backlog:
     - `task`
     - `workflow`
     - `action` if present in the codebase
   - If `action` is not yet a first-class persisted entity, do not fabricate a large subsystem; implement only what the current model supports and leave a clear extension point.

4. **Implement approve decision command flow**
   - Add or update an application command/handler for approve:
     - validate tenant scope
     - validate approval exists and is actionable
     - validate caller is authorized for the current step (specific user or required role)
     - validate step ordering for multi-step chains
     - record approver identity, timestamp, and optional comment
     - update approval/step status
     - if final approval, update linked entity state
     - create audit event(s)
   - Ensure idempotent-safe behavior:
     - repeated approve on already-decided approval should fail safely or return a no-op according to existing conventions
     - do not double-advance steps or duplicate audit events

5. **Implement reject decision command flow**
   - Add or update an application command/handler for reject:
     - same tenant and authorization checks
     - require or at least support rejection comments if the current API contract allows
     - mark current step rejected
     - mark approval rejected
     - update linked entity state accordingly
     - create audit event(s)
   - Ensure rejection terminates the chain immediately.

6. **Persist auditable business events**
   - For each decision, persist business audit events, not just technical logs.
   - At minimum capture:
     - `company_id`
     - actor type/id
     - action (e.g. `approval.approved`, `approval.rejected`, `approval.step_approved`, `approval.step_rejected`, `approval.chain_advanced`, `linked_entity.state_updated`)
     - target type/id
     - outcome
     - rationale/summary/comment where appropriate
     - relevant metadata/data sources if the audit model supports structured payloads
   - If there is already an `audit_events` table/entity, use it consistently.
   - If schema additions are needed, keep them minimal and aligned with the architecture’s auditability guidance.

7. **Wire API surface if missing**
   - Expose or update endpoints for:
     - approve decision
     - reject decision
   - Ensure request contracts support:
     - approval id
     - comment/decision summary where applicable
   - Apply authorization and tenant context resolution consistently with the rest of the API.

8. **Handle expired/cancelled approvals explicitly**
   - If expiration/cancellation logic already exists, ensure decision handlers reject actions on non-actionable approvals.
   - If linked entity state handling for expired/cancelled is missing, add minimal explicit behavior so these approvals do not execute actions.
   - Prefer explicit guard clauses and tests over implicit assumptions.

9. **Add tests**
   - Cover domain/application/API behavior for:
     - single-step approval approved updates linked entity state
     - single-step rejection updates linked entity state
     - multi-step approval:
       - first step approval advances chain only
       - final step approval updates linked entity state
     - rejection at any step terminates chain and updates linked entity state
     - audit events are persisted for decisions and resulting state changes
     - unauthorized approver cannot decide
     - cross-tenant access is forbidden/not found per project conventions
     - already-decided approval cannot be decided again
     - expired/cancelled approvals cannot be approved/rejected into execution

10. **Keep implementation cohesive and minimal**
   - Avoid scattering approval state update logic across controllers, repositories, and entities.
   - Prefer:
     - domain rules in domain layer
     - orchestration in application layer
     - persistence in infrastructure
   - Preserve transaction boundaries so approval decision + linked entity update + audit persistence succeed or fail together.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add and run targeted tests for this task, including:
   - approve single-step approval updates linked task/workflow state
   - reject single-step approval updates linked task/workflow state
   - approve first step of multi-step chain advances next step without final entity transition
   - approve final step updates linked entity state
   - reject any step ends chain and updates linked entity state
   - audit events created for:
     - decision
     - linked entity state transition
   - tenant isolation and authorization checks
   - non-actionable approvals (`approved`, `rejected`, `expired`, `cancelled`) cannot be decided again

4. If API endpoints exist or are added, verify via integration tests:
   - correct HTTP status codes
   - tenant-scoped behavior
   - persisted state in approval, approval steps, linked entity, and audit records

5. If persistence schema changes are required:
   - generate/apply migration using the repository’s established migration approach
   - verify schema matches entity mappings
   - rerun `dotnet build` and `dotnet test`

# Risks and follow-ups

- **Unclear linked entity semantics**
  - The architecture/backlog says approvals can target `task`, `workflow`, or `action`, but the current codebase may not model all three equally.
  - Follow-up: document supported entity types and add extension points for future ones.

- **Status mismatch risk**
  - Existing task/workflow statuses may not include a perfect “rejected” or “cancelled due to approval” state.
  - Follow-up: align status taxonomy across modules if current states are ambiguous.

- **Audit model incompleteness**
  - The audit subsystem may be partially implemented.
  - Follow-up: standardize audit event naming and metadata shape across modules.

- **Authorization complexity**
  - Required role vs specific user vs ordered chain may already be partially implemented in inconsistent ways.
  - Follow-up: centralize approval authorization rules if logic is duplicated.

- **Transaction consistency**
  - Approval decision, linked entity update, and audit persistence must be atomic.
  - Follow-up: verify transaction boundaries and outbox integration for any side effects.

- **Expiration/cancellation lifecycle**
  - This task should explicitly block execution/progression, but full automated expiry processing may belong elsewhere.
  - Follow-up: ensure background workers handle approval expiry transitions if not already implemented.

- **UI gaps**
  - Backend support may land before web/mobile surfaces fully expose comments, chain progress, or audit detail.
  - Follow-up: connect approval detail/inbox UX to the new auditable decision data.