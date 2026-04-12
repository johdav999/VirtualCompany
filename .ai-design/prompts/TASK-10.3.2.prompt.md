# Goal
Implement backlog task **TASK-10.3.2** for **ST-403 Approval requests and decision chains** so the approval system supports three targeting modes:

1. **Required role**
2. **Specific user**
3. **Ordered multi-step chain**

The implementation must fit the existing **.NET modular monolith** architecture, remain **tenant-scoped**, and preserve **auditability** and **workflow/task integration**. The result should enable approval requests for tasks, workflows, or actions to model either a single approver target or a sequenced chain of approvers.

# Scope
Implement the minimum end-to-end backend/domain support for approval targeting and ordered chains, including persistence, domain/application logic, and tests.

Include:

- Approval domain model updates to represent:
  - single-step role-based approval
  - single-step user-based approval
  - ordered multi-step approval chains
- Persistence updates for `approvals` and `approval_steps`
- Application commands/services to:
  - create approval requests with one of the supported targeting modes
  - determine current actionable step
  - record approve/reject decisions against the active step
  - advance to the next step when applicable
  - finalize approval status when chain completes or is rejected
- Linked entity state updates for supported entity types already present in codebase, at minimum:
  - task
  - workflow instance
  - generic action placeholder if already modeled
- Audit/event hooks where the current architecture already supports them
- Tenant isolation and authorization-safe query/update behavior
- Automated tests

Do **not** build full UI unless required by existing code paths. If API contracts already exist, extend them; otherwise add minimal API/application endpoints only if needed to exercise the feature. Do **not** overbuild notifications/mobile UX in this task.

# Files to touch
Inspect the solution first and then update the actual files that match the existing structure. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - approval entities/aggregates
  - enums/value objects for approval status, approver type, targeting mode
  - task/workflow entities if linked state transitions are handled in domain
- `src/VirtualCompany.Application/`
  - approval commands/handlers/services
  - DTOs/contracts for create approval and decision actions
  - validators
  - query models for current approval state
- `src/VirtualCompany.Infrastructure/`
  - EF Core configurations
  - repositories
  - migrations or SQL migration artifacts used by this repo
  - outbox/audit persistence hooks if applicable
- `src/VirtualCompany.Api/`
  - approval endpoints/controllers
  - request/response contracts if API layer owns them
- `tests/VirtualCompany.Api.Tests/`
  - integration/API tests
- Additional test projects if approval logic is covered elsewhere

Also inspect:
- `README.md`
- any architecture/conventions docs
- existing migration approach under `docs/postgresql-migrations-archive/README.md`

If the repository already has approval-related files, prefer extending them over creating parallel abstractions.

# Implementation plan
1. **Discover current approval implementation**
   - Find all existing approval-related code:
     - entities
     - EF configs
     - handlers/services
     - endpoints
     - tests
   - Identify whether approvals are currently modeled as:
     - flat record only
     - record + steps
     - partially implemented chain support
   - Reuse existing naming and patterns.

2. **Define the domain model clearly**
   - Support these concepts explicitly:
     - `Approval`
     - `ApprovalStep`
     - `ApprovalStatus`
     - `ApprovalStepStatus`
     - `ApproverType` = `Role` or `User`
   - Ensure an approval can be created in one of these shapes:
     - **Required role**: one step, `ApproverType=Role`, `ApproverRef=<role>`
     - **Specific user**: one step, `ApproverType=User`, `ApproverRef=<userId or canonical string ref>`
     - **Ordered multi-step chain**: multiple steps with ascending `SequenceNo`
   - Add invariants:
     - at least one step required
     - sequence numbers unique and ordered
     - only one active actionable step at a time for ordered chains
     - cannot decide a completed/rejected/cancelled/expired approval
     - cannot decide a non-current step
     - rejection finalizes the approval unless existing business rules say otherwise
     - approval is approved only when all steps are approved in order

3. **Align persistence with architecture schema**
   - Use the architecture as the target shape:
     - `approvals`
     - `approval_steps`
   - If schema differs, evolve it safely.
   - Ensure columns exist for:
     - `required_role` and `required_user_id` only if still needed for backward compatibility
     - step-based representation as source of truth going forward
     - `sequence_no`
     - `approver_type`
     - `approver_ref`
     - `status`
     - `decided_by_user_id`
     - `decided_at`
     - `comment`
   - Prefer making `approval_steps` the canonical targeting model even for single-step approvals.
   - Add indexes for:
     - `approval_id`
     - active/pending steps lookup
     - tenant-scoped approval queries
   - Create migration(s) consistent with repo conventions.

4. **Implement creation flow**
   - Add/extend a create approval command/service that accepts:
     - company/tenant context
     - entity type/id
     - requested by actor
     - approval type
     - threshold context
     - one of:
       - required role
       - required user
       - ordered steps
   - Normalize all inputs into `approval_steps`.
   - Validate:
     - exactly one targeting mode supplied
     - role target has non-empty role
     - user target references a valid user/membership in the tenant if required by current rules
     - chain has 1..N valid steps
     - no duplicate sequence numbers
     - no mixed invalid refs
   - Set initial statuses:
     - approval = `pending`
     - first step = actionable/pending according to current status model
     - later steps = waiting/not-started if such a status exists, otherwise still pending but not actionable by logic
   - If legacy fields `required_role` / `required_user_id` are retained, populate them only for single-step compatibility.

5. **Implement decision flow**
   - Add/extend approve/reject command/service:
     - resolve approval by tenant + id
     - verify approval is pending
     - resolve current active step
     - verify acting user is allowed for that step:
       - role-based step: user must hold matching company membership role
       - user-based step: acting user must match target user
   - On approve:
     - mark current step approved with actor, timestamp, comment
     - if more steps remain, activate/advance next step
     - else mark approval approved and set `decided_at`
   - On reject:
     - mark current step rejected with actor, timestamp, comment
     - mark approval rejected and set `decision_summary` / `decided_at` as supported
   - Prevent duplicate decisions and out-of-order approvals.

6. **Update linked entity state**
   - When approval is created, ensure linked entity enters awaiting approval state if that behavior exists:
     - task -> `awaiting_approval`
     - workflow instance -> blocked/awaiting approval equivalent based on current model
   - When approval is fully approved:
     - transition linked entity back to resumable/approved state according to existing orchestration pattern
   - When rejected/cancelled/expired:
     - ensure linked entity does **not** execute
     - move to blocked/failed/rejected equivalent based on current domain rules
   - Do not invent broad workflow engine behavior; integrate with existing state transitions conservatively.

7. **Add auditability hooks**
   - Emit or persist audit records if infrastructure already supports it for:
     - approval created
     - approval step approved
     - approval step rejected
     - approval completed
   - Include:
     - actor
     - target entity
     - approval id
     - step sequence
     - approver target
     - outcome
   - Keep rationale concise; do not store chain-of-thought.

8. **Expose query shape for consumers**
   - Ensure approval reads can return:
     - approval header/status
     - linked entity
     - threshold context
     - ordered steps
     - current actionable step
     - decision metadata/comments
   - If an inbox query already exists, update it so only current actionable approvals appear for the correct user/role.

9. **Handle backward compatibility carefully**
   - If existing approvals were single-record only:
     - migrate them to one-step chains where possible
     - or support reading legacy rows while writing new rows in step-based format
   - Document any assumptions in code comments and migration notes.

10. **Testing**
   - Add unit/integration tests for:
     - create approval with required role
     - create approval with specific user
     - create approval with ordered multi-step chain
     - invalid request when multiple targeting modes are supplied
     - invalid request when no targeting mode is supplied
     - role approver can approve current step
     - non-matching role cannot approve
     - wrong user cannot approve user-targeted step
     - second step cannot approve before first step completes
     - approving final step marks approval approved
     - rejecting any active step marks approval rejected
     - linked task/workflow state updates correctly
     - tenant isolation on read/write
   - Prefer integration tests where API/application/database behavior is important.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. If the repo uses EF Core migrations:
   - generate/apply migration per repo convention
   - verify schema includes approval step support and indexes

5. Manually validate via tests or API client:
   - create approval with role target
   - create approval with user target
   - create approval with 2+ ordered steps
   - approve step 1 and confirm step 2 becomes current
   - reject active step and confirm approval finalizes as rejected
   - verify linked task/workflow does not execute on rejected/cancelled/expired approval

6. Confirm tenant safety:
   - cross-tenant approval access should fail
   - user outside targeted role/user should not be able to decide

7. Confirm audit persistence if available:
   - approval creation and decisions produce expected audit records

# Risks and follow-ups
- **Schema drift risk:** the architecture shows `approval_steps`, but the repo may already diverge. Adapt to actual codebase rather than forcing a parallel model.
- **Authorization ambiguity:** role matching depends on how `company_memberships.role` is modeled; normalize comparisons carefully.
- **Legacy compatibility:** if old approvals exist without steps, migration strategy may need a follow-up task.
- **Entity state coupling:** task/workflow transitions may currently be scattered; avoid duplicating state logic and centralize if practical.
- **Inbox/notification gaps:** current task should focus on approval targeting and chain progression, not full notification fan-out.
- **Cancellation/expiration behavior:** story mentions explicit handling; if not already implemented, add minimal safe handling or leave a clearly documented follow-up.
- **Follow-up likely needed:** UI/inbox enhancements to visualize multi-step chains and current approver status.