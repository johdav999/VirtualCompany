# Goal
Implement backlog task **TASK-10.3.4** for **ST-403 Approval requests and decision chains** so that **expired and cancelled approvals are treated as terminal, explicit states and never allow the guarded action to execute**.

This work should ensure:
- approval lifecycle handling is explicit in domain and application logic
- execution paths block actions when approval status is `expired` or `cancelled`
- linked task/workflow/action state is updated consistently
- auditability is preserved
- tests cover the negative execution cases and lifecycle transitions

# Scope
Focus only on the backend/domain/application/infrastructure work needed to support this task in the existing .NET solution.

In scope:
- Approval status modeling and lifecycle rules for `expired` and `cancelled`
- Guarding execution so only valid approved requests can proceed
- Explicit handling for linked entity state when approval expires or is cancelled
- Audit/event emission or persistence if the current architecture already has a pattern for it
- API/application behavior updates if needed to surface correct status and prevent invalid actions
- Automated tests

Out of scope unless required by existing code structure:
- New UI/UX redesigns
- Mobile-specific work
- Broad approval inbox features beyond what is necessary
- New workflow engine capabilities unrelated to approval terminal-state enforcement
- Large refactors outside the approval/task/workflow/policy boundary

# Files to touch
Inspect the solution first and then update the minimal correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - approval aggregate/entity/value objects/enums
  - task/workflow status logic if linked state transitions are domain-owned
- `src/VirtualCompany.Application/**`
  - approval commands/handlers
  - orchestration or policy guardrail services
  - action execution services that consume approval decisions
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories
  - background processing for approval expiry if present
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if status validation or response mapping must change
- `src/VirtualCompany.Shared/**`
  - contracts/DTOs/enums if shared externally
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- any relevant application/domain test projects if present

Also inspect:
- existing migration approach and whether a schema change is needed
- any approval-related docs or README notes if behavior needs documenting

# Implementation plan
1. **Discover the current approval implementation**
   - Find all approval-related types, handlers, endpoints, repositories, and tests.
   - Identify:
     - current approval statuses
     - where approval decisions are made
     - where approved actions are later executed
     - whether expiry is time-based, manual, or both
     - how linked entities (`task`, `workflow`, `action`) are updated today
   - Trace the full flow from approval creation to action execution.

2. **Define and enforce lifecycle rules**
   - Ensure approval statuses explicitly include terminal states:
     - `Pending`
     - `Approved`
     - `Rejected`
     - `Expired`
     - `Cancelled`
   - If multi-step chains exist, ensure overall approval status cannot become executable unless the chain is fully approved.
   - Add domain rules such as:
     - `Expired` approvals cannot be approved later
     - `Cancelled` approvals cannot be approved later
     - `Expired` and `Cancelled` are terminal
     - execution is allowed only for the valid approved state
   - Prefer domain methods/guards over scattered conditional logic.

3. **Block execution for expired/cancelled approvals**
   - Find the exact service/handler that executes the protected action after approval.
   - Add a hard guard so execution only proceeds when approval is in the correct executable state.
   - Treat `Expired` and `Cancelled` as explicit non-executable outcomes, not as generic failures.
   - Return a safe, deterministic application result/error for these cases.
   - If there is a policy decision object or tool execution record, include a clear denial reason such as:
     - `approval_expired`
     - `approval_cancelled`

4. **Handle linked entity state explicitly**
   - For approvals tied to tasks/workflows/actions, update linked entity state consistently when approval expires or is cancelled.
   - Align with the architecture and story intent:
     - tasks likely remain or move to a blocked/non-executable reviewable state rather than silently continuing
     - workflows should not advance the guarded step
     - actions should be marked as not executed / cancelled / blocked according to existing model
   - Do not invent new statuses unless necessary; reuse existing statuses where possible.
   - If no suitable linked-state update exists, implement the smallest explicit behavior and document it in code comments/tests.

5. **Support expiry handling**
   - If expiry already exists:
     - ensure expired approvals are recognized before any execution attempt
     - ensure stale pending approvals transition to `Expired` consistently
   - If expiry is represented by timestamps but not enforced:
     - add enforcement in read/decision/execution paths
   - If a background worker exists for scheduled lifecycle progression:
     - update it to mark overdue pending approvals as expired
   - If no worker exists, at minimum enforce expiry lazily at decision/execution time so expired approvals cannot execute.

6. **Support cancellation handling**
   - Implement or complete cancellation behavior for approvals if partially present.
   - Ensure cancellation:
     - updates approval status to `Cancelled`
     - prevents future approval decisions and execution
     - updates linked entity state as appropriate
     - records audit information if the project already persists business audit events

7. **Persist and expose status correctly**
   - Update EF mappings / repository logic / DTO mappings as needed.
   - If enums are serialized or stored as strings/ints, keep consistency with existing conventions.
   - If API contracts expose approval status, ensure `expired` and `cancelled` are returned correctly.
   - Add migration only if the persistence model truly requires it.

8. **Add auditability hooks**
   - If the codebase already has audit event patterns, emit business audit events for:
     - approval expired
     - approval cancelled
     - execution blocked due to expired approval
     - execution blocked due to cancelled approval
   - Keep rationale concise and operational; do not add chain-of-thought.

9. **Test thoroughly**
   Add or update tests for at least these scenarios:
   - pending approval cannot execute guarded action
   - approved approval can execute guarded action
   - rejected approval cannot execute guarded action
   - expired approval cannot execute guarded action
   - cancelled approval cannot execute guarded action
   - expired approval cannot later be approved
   - cancelled approval cannot later be approved
   - linked task/workflow/action does not advance when approval is expired/cancelled
   - expiry enforcement works even if status was still pending but deadline has passed, if that model exists
   - multi-step chain does not execute if chain is cancelled/expired at any required point

10. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep approval logic in approval/task/workflow modules, not controllers.
   - Preserve tenant scoping in all queries and updates.
   - Prefer application commands + domain methods + repository persistence over ad hoc endpoint logic.
   - Keep side effects reliable if outbox/event dispatch patterns already exist.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted tests for approval-related areas if available.

4. Run full test suite:
   - `dotnet test`

5. Manually verify code paths by reading through:
   - approval creation
   - approval decision
   - approval expiry/cancellation
   - guarded action execution
   - linked task/workflow/action state updates

6. Confirm:
   - no execution path bypasses approval terminal-state checks
   - expired/cancelled approvals are explicit statuses, not inferred ambiguously
   - tenant scoping is preserved
   - API/application responses are deterministic and safe
   - tests clearly prove non-execution for expired/cancelled approvals

# Risks and follow-ups
- **Risk: scattered approval checks**
  - Execution gating may currently be duplicated across handlers/services. Consolidate into a single reusable guard where practical.

- **Risk: implicit expiry**
  - The system may store due/expiry timestamps without converting status to `Expired`. If so, enforce expiry both lazily and, if possible, via background processing.

- **Risk: linked entity ambiguity**
  - Existing task/workflow/action models may not have a perfect terminal or blocked state for expired/cancelled approvals. Reuse current statuses conservatively and document behavior in tests.

- **Risk: enum/storage compatibility**
  - If approval statuses are persisted as enums/strings, adding or renaming values may affect existing data or API clients. Preserve backward compatibility where possible.

- **Risk: multi-step chain edge cases**
  - Ordered approval chains may have partial approvals when cancellation/expiry occurs. Ensure overall approval becomes non-executable and later steps cannot proceed.

Follow-ups to note in code comments or task notes if not fully addressed here:
- dedicated scheduled expiry processing if only lazy expiry enforcement is implemented now
- richer audit/event surfacing in approval inbox/dashboard
- explicit user-facing reason codes/messages for blocked execution due to expired/cancelled approvals
- broader consistency review across all policy-gated tool execution paths