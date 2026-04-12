# Goal

Implement backlog task **TASK-10.3.1** for story **ST-403 Approval requests and decision chains** by enabling creation of approval requests for **tasks**, **workflow instances**, and **actions**, including persisted **threshold context** and support for **single-step** and **ordered multi-step approval chains**.

This task should establish the domain and application backbone for approvals so later work can add richer UX, notifications, and orchestration integration without reworking the core model.

# Scope

Implement the minimum production-ready backend slice needed to support the story intent:

- Add/complete approval domain modeling for:
  - approval request
  - approval target entity type: `task`, `workflow`, `action`
  - threshold context payload
  - required role / required user
  - ordered approval steps
  - approval status lifecycle
- Add persistence mappings and migrations for approvals and approval steps if not already present.
- Add application commands/services to create approval requests.
- Ensure approval creation can link to:
  - `tasks.id`
  - `workflow_instances.id`
  - an action reference using a stable identifier pattern already used in the codebase, or introduce a safe `action` entity reference convention if no dedicated action table exists yet.
- Add validation and tenant scoping.
- Add tests covering creation scenarios and invalid cases.

Out of scope unless already trivial in the existing codebase:

- Full approval decision UI
- Notification fan-out/inbox work
- Expiration processors
- Full workflow state transition engine
- Rich audit/explainability screens
- Mobile support
- Deep orchestration integration beyond creating approval requests

# Files to touch

Inspect the solution first and then update the appropriate files in these areas.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - approval aggregate/entity/value objects/enums
  - task/workflow linkage rules if needed
- `src/VirtualCompany.Application/**`
  - create approval request command + handler
  - DTOs/contracts
  - validation
  - repository/service interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migration(s)
  - tenant-aware query enforcement
- `src/VirtualCompany.Api/**`
  - endpoint/controller for creating approval requests, if API surface for this task belongs here
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums only if this solution already centralizes API contracts there
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- Possibly:
  - `tests/**Application.Tests**` or `tests/**Infrastructure.Tests**` if such projects already exist

Do not create new architectural layers or patterns if the repository already has an established approach. Follow the existing conventions.

# Implementation plan

1. **Inspect current approval/task/workflow implementation**
   - Find whether `approvals` and `approval_steps` already exist in domain, EF configs, or migrations.
   - Find existing patterns for:
     - commands/handlers
     - result/error handling
     - tenant context resolution
     - authorization
     - entity status enums
   - Find whether there is already an audit event abstraction to hook into.
   - Find whether “action” is already represented in:
     - `tool_executions`
     - orchestration records
     - another action/request table
   - Reuse existing terminology and naming.

2. **Model the approval request domain**
   - Add or complete an approval aggregate/entity with fields aligned to architecture/backlog:
     - `Id`
     - `CompanyId`
     - `EntityType` with allowed values `task`, `workflow`, `action`
     - `EntityId` or equivalent target reference
     - `RequestedByActorType`
     - `RequestedByActorId`
     - `ApprovalType`
     - `ThresholdContextJson` or strongly typed value object persisted as JSON
     - `RequiredRole`
     - `RequiredUserId`
     - `Status`
     - `DecisionSummary`
     - `CreatedAt`
     - `DecidedAt`
   - Add or complete approval step entity:
     - `Id`
     - `ApprovalId`
     - `SequenceNo`
     - `ApproverType` (`role`, `user`)
     - `ApproverRef`
     - `Status`
     - `DecidedByUserId`
     - `DecidedAt`
     - `Comment`
   - Prefer enums/value objects over raw strings if the codebase already does that.
   - Add guard methods/factory methods so invalid approvals cannot be created:
     - entity type must be supported
     - at least one approver target must exist:
       - required role, or
       - required user, or
       - one or more steps
     - ordered steps must have unique positive sequence numbers
     - company/tenant ownership must be explicit
     - threshold context must be present for this task

3. **Define target reference rules**
   - For `task` approvals:
     - validate target task exists and belongs to the same company
   - For `workflow` approvals:
     - validate target workflow instance exists and belongs to the same company
   - For `action` approvals:
     - if a first-class action record exists, validate it
     - otherwise use the existing stable action reference pattern in the codebase
     - if no pattern exists, introduce a minimal safe reference contract, e.g. action target uses a GUID identifier tied to an existing execution/request record rather than free-form text
   - Do not allow cross-tenant references.

4. **Add create approval request application flow**
   - Implement a command such as `CreateApprovalRequestCommand`.
   - Suggested request shape:
     - target entity type
     - target entity id/reference
     - requested by actor type/id
     - approval type
     - threshold context
     - optional required role
     - optional required user id
     - optional ordered steps collection
   - Handler responsibilities:
     - resolve current company/tenant context
     - validate target entity exists in tenant
     - validate approver configuration
     - create approval + steps
     - persist atomically
     - optionally update linked entity state to `awaiting_approval` when target is a task and such behavior already fits existing domain patterns
   - If workflow/action state updates are already modeled, apply them consistently; otherwise keep this task focused on approval creation and leave downstream transitions for follow-up.

5. **Persist threshold context cleanly**
   - Threshold context should capture why approval was required.
   - Use structured JSON, not opaque text.
   - Include fields that are useful and generic, for example:
     - threshold rule/code
     - action type
     - requested amount/value if applicable
     - currency if applicable
     - autonomy level at evaluation time
     - policy reason
     - risk flags
     - source module
   - Keep it extensible and nullable only if existing schema requires it; for this task prefer required threshold context.

6. **Support single-step and ordered multi-step chains**
   - Single-step cases:
     - required role only
     - required user only
   - Multi-step case:
     - explicit ordered `approval_steps`
   - If both top-level required approver fields and steps are supplied, define and enforce one rule consistently:
     - either reject mixed mode, or
     - normalize single-step top-level approver into one step
   - Preferred approach: normalize to steps internally if that fits the codebase, but preserve top-level fields if schema already expects them.

7. **Infrastructure and EF Core**
   - Add/update EF configurations for approval entities.
   - Ensure:
     - table names match existing conventions
     - JSON/JSONB mapping is correct for PostgreSQL
     - indexes exist for common lookups:
       - `company_id`
       - `(company_id, entity_type, entity_id)`
       - `status`
       - `approval_id, sequence_no` on steps
   - Add migration if schema changes are needed.
   - Keep migration names descriptive.

8. **API surface**
   - If this repository exposes approvals via minimal APIs/controllers, add a create endpoint.
   - Ensure:
     - tenant context is enforced
     - authorization uses existing policy conventions
     - request validation returns the project’s standard error format
   - Return enough data for callers to continue:
     - approval id
     - status
     - target reference
     - created steps summary

9. **Audit integration**
   - If audit event creation already exists, emit an audit event for approval creation.
   - Include:
     - actor
     - action like `approval.created`
     - target type/id
     - outcome `success` or `rejected`
     - concise rationale/summary
   - Do not build a new audit subsystem here.

10. **Tests**
    - Add tests for:
      - create approval for task with threshold context
      - create approval for workflow with threshold context
      - create approval for action with threshold context
      - create approval with required role
      - create approval with required user
      - create approval with ordered multi-step chain
      - reject creation when target entity does not exist
      - reject creation when target belongs to another tenant
      - reject creation when no approver target is provided
      - reject invalid step ordering/duplicates
      - verify persisted threshold context
      - verify task state update to `awaiting_approval` only if implemented in this slice
    - Prefer integration tests over mocks where the repository already supports API/database-backed tests.

11. **Implementation quality constraints**
    - Follow existing clean architecture/module boundaries.
    - Keep tenant isolation explicit in every query and write path.
    - Do not expose raw internal exception details.
    - Do not add speculative features beyond this task.
    - Keep code small, composable, and aligned with current patterns.

# Validation steps

1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo, generate/apply or verify migration consistency using the repository’s existing EF workflow.

4. Manually verify through tests or local API calls:
   - creating a task approval succeeds with threshold context
   - creating a workflow approval succeeds with threshold context
   - creating an action approval succeeds with threshold context
   - multi-step chain persists in correct sequence order
   - invalid tenant/target combinations are rejected
   - threshold context is stored as structured data
   - linked task enters `awaiting_approval` if that behavior is implemented

5. Confirm no regressions in:
   - task APIs
   - workflow APIs
   - tenant scoping
   - existing approval-related code paths

# Risks and follow-ups

- **Action target ambiguity:** the architecture mentions approvals for actions, but the schema does not define a dedicated `actions` table. Reuse an existing stable action/execution record if present; otherwise introduce only a minimal reference convention and document it clearly.
- **State transition coupling:** acceptance intent says decisions update linked entity state, but this task is specifically about creation. If decision handling is not already present, avoid overreaching and note follow-up work for approve/reject transitions.
- **Schema drift risk:** approvals may already partially exist. Prefer extending existing entities/migrations rather than duplicating concepts.
- **Tenant safety:** approval creation must validate target ownership before persistence.
- **Follow-up tasks likely needed:**
  - approval decision commands/endpoints
  - expiration/cancellation handling
  - approval inbox/query endpoints
  - notification fan-out
  - richer audit/explainability linkage
  - orchestration guardrail integration so threshold breaches automatically create approvals