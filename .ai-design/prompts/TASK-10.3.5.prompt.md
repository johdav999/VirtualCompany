# Goal
Implement backlog task **TASK-10.3.5** for **ST-403 Approval requests and decision chains** so that v1 supports both:

- **single-step approvals**
- **ordered multi-step approval chains**

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and align with the approval domain model described in the architecture and backlog.

This task should enable the system to represent, create, progress, and decide approval chains where:
- an approval may have exactly one step, or
- an approval may have multiple steps evaluated in strict `sequence_no` order.

The result should be production-ready enough for v1, with clear domain rules, persistence, application services, and tests.

# Scope
In scope for this task:

- Add or complete domain/application support for approval chains using:
  - `approvals`
  - `approval_steps`
- Support approval targets for:
  - required role
  - specific user
  - ordered multi-step chain
- Ensure only the **current active step** in an ordered chain can be decided
- Ensure approval status progression is correct:
  - pending
  - approved
  - rejected
  - expired
  - cancelled
- Ensure linked entity state can be updated or surfaced for later integration when approval decisions occur
- Add CQRS-style commands/handlers/services as appropriate
- Add persistence mappings and migrations if missing
- Add tests for single-step and multi-step behavior

Out of scope unless required to make this task coherent:

- Full approval inbox UX
- Mobile UI
- Rich notification fan-out
- Arbitrary workflow builder UX
- Parallel approval branches
- Escalation routing beyond ordered chains
- SLA timers beyond basic status support
- Complex role resolution if the project does not yet have it; use the simplest existing membership/authorization model compatible with the codebase

If parts of ST-403 are already partially implemented, extend them rather than duplicating patterns.

# Files to touch
Inspect the solution first and then touch the minimum coherent set of files across these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- optionally `src/VirtualCompany.Shared` if contracts/DTOs live there

Likely file categories to update or add:

- **Domain**
  - approval aggregate/entity
  - approval step entity/value objects
  - status enums/constants
  - domain methods for:
    - create single-step approval
    - create ordered chain
    - approve current step
    - reject current step
    - cancel approval
    - expire approval
    - compute current active step
- **Application**
  - commands and handlers for:
    - create approval request
    - approve approval step
    - reject approval step
    - cancel approval
  - queries for approval details if needed by tests/API
  - DTOs/contracts for chain definitions
  - validation logic
- **Infrastructure**
  - EF Core entity configurations
  - repository implementations
  - migration(s) for approvals/approval_steps if schema is incomplete
  - tenant-scoped query enforcement
- **API**
  - endpoints/controllers/minimal APIs for create/approve/reject/cancel if this story already exposes approval APIs
- **Tests**
  - domain unit tests
  - application handler tests
  - integration tests for persistence and tenant scoping where test infrastructure exists

Do not invent broad new layers if the repository already has established patterns. Follow the existing conventions.

# Implementation plan
1. **Inspect existing approval implementation**
   - Find all current approval-related code across Domain/Application/Infrastructure/API.
   - Determine whether `approvals` and `approval_steps` already exist in:
     - entities
     - EF mappings
     - migrations
     - handlers
     - endpoints
   - Reuse existing naming and architectural patterns.

2. **Model the approval chain rules in the domain**
   Implement or refine the approval aggregate so it can represent:
   - a single-step approval as one `approval_step` with `sequence_no = 1`
   - an ordered multi-step chain as multiple steps with increasing `sequence_no`

   Domain invariants:
   - approval belongs to a company/tenant
   - step sequence numbers must be unique and increasing
   - at least one step is required for chain-based approvals
   - only one step can be active at a time in ordered chains
   - a later step cannot be decided before all prior steps are approved
   - once approval is approved/rejected/cancelled/expired, no further decisions are allowed
   - rejection of any active step rejects the whole approval
   - approval is fully approved only when the final step is approved

   Support approver targeting per step:
   - `approver_type = role | user`
   - `approver_ref` stores role name or user id/string reference per existing conventions

3. **Define creation inputs**
   Add application-layer request models/commands that support:
   - entity linkage:
     - `entity_type`
     - `entity_id`
   - requester:
     - `requested_by_actor_type`
     - `requested_by_actor_id`
   - approval metadata:
     - `approval_type`
     - `threshold_context_json`
   - approval routing:
     - single required role
     - single required user
     - explicit ordered step list

   Normalize all creation paths into a step list internally.
   For example:
   - required role => one step
   - required user => one step
   - ordered chain => N steps

4. **Implement decision progression**
   Add command handlers/domain methods for:
   - approve current step
   - reject current step with optional comment
   - cancel approval
   - expire approval

   Approval behavior:
   - approving a non-final active step marks that step approved and advances the chain
   - approving the final active step marks the approval approved
   - rejecting the active step marks the approval rejected
   - comments should be stored on the step and/or approval summary fields per existing schema
   - `decided_by_user_id` and `decided_at` must be persisted

5. **Enforce actor eligibility**
   When deciding a step, validate the acting user against the current step:
   - if `approver_type = user`, only that user can decide
   - if `approver_type = role`, only a user with that company membership role can decide

   Keep this tenant-scoped:
   - the acting user must belong to the same company
   - cross-tenant access must fail safely

   If the codebase already has authorization services/policies, use them instead of duplicating role checks.

6. **Persist current state cleanly**
   Ensure EF mappings and persistence support:
   - approval with child steps
   - ordered retrieval by `sequence_no`
   - status fields on approval and steps
   - nullable decision fields until decided
   - comments and summaries
   - timestamps

   Add or update migration(s) only if needed.
   Do not break existing data. If schema already exists, prefer additive/non-breaking changes.

7. **Update linked entity state integration**
   ST-403 says decisions update linked entity state. For this task:
   - if there is already a task/workflow/action state integration, wire approval outcomes into it
   - otherwise implement the smallest clean extension point, such as an application service/domain event/outbox message indicating:
     - approval approved
     - approval rejected
     - approval cancelled
     - approval expired

   Prefer existing patterns for side effects.
   Do not hardcode broad workflow logic into the approval aggregate.

8. **Expose API surface if appropriate**
   If approval APIs already exist, extend them to support:
   - creating single-step approvals
   - creating ordered multi-step approvals
   - approving current step
   - rejecting current step
   - cancelling approval
   - fetching approval details including ordered steps and current status

   Keep request/response contracts concise and aligned with existing API style.

9. **Add tests**
   Minimum expected test coverage:

   **Domain tests**
   - create single-step approval
   - create ordered multi-step approval
   - approving first step advances to second
   - approving final step completes approval
   - rejecting active step rejects approval
   - cannot approve step out of order
   - cannot decide completed/cancelled/expired approval
   - invalid chain definitions are rejected

   **Application tests**
   - user-targeted step only allows specified user
   - role-targeted step allows matching company role
   - cross-tenant decision is blocked
   - create command normalizes single-step and chain inputs correctly

   **Integration tests** if test infrastructure exists
   - approval and steps persist and reload in order
   - tenant-scoped queries do not leak approvals across companies

10. **Keep implementation v1-simple**
   Explicitly avoid:
   - parallel approvals
   - dynamic branching
   - delegated approvers
   - quorum logic
   - escalation timers
   - free-form chain mutation after creation

   Ordered sequential chains only.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly in the existing development setup.

4. Manually validate these scenarios through tests or API calls:
   - create approval with one role-based step
   - create approval with one user-based step
   - create approval with ordered steps:
     - step 1 role
     - step 2 specific user
   - approve step 1 and confirm:
     - approval remains pending
     - step 1 is approved
     - step 2 becomes current/active
   - reject step 2 and confirm:
     - approval becomes rejected
     - linked entity integration hook is triggered if implemented
   - attempt to approve step 2 before step 1 and confirm failure
   - attempt decision by wrong user and confirm forbidden/validation failure
   - attempt cross-tenant access and confirm forbidden/not found behavior

5. Confirm persistence details:
   - `approval_steps` are stored in correct sequence order
   - `decided_by_user_id`, `decided_at`, and `comment` are populated correctly
   - final approval status matches chain outcome

6. Confirm no regressions in existing approval/task/workflow behavior.

# Risks and follow-ups
- **Existing partial implementation mismatch**: The repository may already model approvals differently than the architecture doc. Prefer adapting to current code patterns rather than forcing a rewrite.
- **Role resolution ambiguity**: If membership role checks are not centralized yet, implement the smallest reusable authorization helper and note follow-up refactoring.
- **Linked entity state coupling**: Updating tasks/workflows/actions directly inside approval logic can create tight coupling. Prefer events/application services if available.
- **Migration risk**: If approval tables already exist in production-like migrations, avoid destructive schema changes.
- **Status semantics drift**: Ensure approval-level and step-level statuses are clearly separated and consistently named.

Follow-up items to note in code comments or implementation notes if not completed here:
- approval inbox/read models
- notification fan-out via outbox
- expiry scheduling/background worker support
- richer audit/event history for each step transition
- UI surfacing of rationale and affected data
- future support for parallel or conditional chains