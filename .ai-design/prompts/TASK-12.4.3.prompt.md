# Goal
Implement backlog task **TASK-12.4.3** for **ST-604 Mobile companion for approvals, alerts, and quick chat** so that **approval decisions made in the .NET MAUI mobile app update the exact same backend approval state and business flow as the web app**.

This work must preserve the architecture principle: **reuse backend APIs and domain/application logic; do not introduce mobile-specific business logic**.

Outcome:
- Mobile approval actions call the same backend command/API path used by web, or a shared backend path that produces identical state transitions and side effects.
- Approval state, linked entity updates, audit behavior, and error handling remain consistent across web and mobile.
- The implementation is tenant-aware and authorization-safe.

# Scope
In scope:
- Inspect current approval decision flow in backend and identify the canonical command/service/API used by web.
- Wire the mobile app to use that same backend contract for approve/reject actions.
- If web currently bypasses a reusable application command, refactor toward a shared backend path without changing business behavior.
- Ensure approval decisions update:
  - `approvals.status`
  - `approvals.decision_summary` / decision metadata as applicable
  - `approvals.decided_at`
  - `approval_steps` state if multi-step chains exist
  - linked entity state transitions required by existing approval orchestration
  - audit/outbox side effects already expected by the backend
- Add/adjust tests proving mobile-triggered decisions hit the same backend behavior as web-triggered decisions.
- Keep payloads concise and mobile-friendly.

Out of scope:
- New approval business rules.
- New mobile UX beyond what is required to submit approval/rejection.
- Full offline approval queueing/sync.
- Push notifications.
- Admin/workflow parity on mobile.
- Reworking the entire approval domain unless necessary to establish one canonical decision path.

# Files to touch
Likely areas to inspect and update first, then refine based on actual code structure:

- `src/VirtualCompany.Mobile/**`
  - Approval inbox/detail viewmodels, pages, API clients, DTO mappings
- `src/VirtualCompany.Api/**`
  - Approval decision endpoints/controllers/minimal APIs
  - request/response contracts if mobile needs to consume existing endpoints
- `src/VirtualCompany.Application/**`
  - Approval decision commands/handlers/services
  - authorization and tenant-scoped application logic
- `src/VirtualCompany.Domain/**`
  - approval aggregate/entity methods if state transitions are currently duplicated elsewhere
- `src/VirtualCompany.Infrastructure/**`
  - repositories, persistence mappings, outbox/audit persistence if touched by refactor
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts if web and mobile already share transport models
- `src/VirtualCompany.Web/**`
  - only if needed to align web to the same backend contract/path as mobile
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests for approval decision behavior
- Potentially other test projects if present for application/domain layers

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- solution/project references in `VirtualCompany.sln`

# Implementation plan
1. **Discover the current canonical approval flow**
   - Find all approval approve/reject entry points in:
     - web UI
     - mobile app
     - API
     - application layer
   - Identify whether web already uses a backend command such as `ApproveApproval`, `RejectApproval`, `DecideApproval`, etc.
   - Confirm how linked entity state is updated today for web decisions.

2. **Establish one backend decision path**
   - If one already exists, make mobile call it directly.
   - If web and mobile use different paths or web contains embedded decision logic, refactor so both clients use one backend application command/service.
   - Prefer a single application-layer command handling:
     - tenant resolution
     - approver authorization
     - approval status transition validation
     - optional comment/decision summary
     - multi-step chain progression if supported
     - final approval/rejection resolution
     - linked task/workflow/action state updates
     - audit/outbox side effects

3. **Keep API contract client-agnostic**
   - Ensure the backend endpoint is not web-specific.
   - Use a request model like:
     - approval id
     - decision (`approve` / `reject`)
     - optional comment
     - company/tenant context from auth/session, not client trust
   - Return a concise response with updated approval state and any relevant linked entity summary for mobile refresh.

4. **Update mobile to use the shared backend contract**
   - Replace any local/mobile-specific approval state mutation with API-driven updates only.
   - Ensure mobile approval actions:
     - call the shared endpoint
     - handle success/failure cleanly
     - refresh approval detail/list from backend state
     - do not assume optimistic completion unless already supported consistently
   - Keep intermittent connectivity handling simple:
     - show actionable error
     - do not fake local approval completion if request fails

5. **Align web if necessary**
   - If web currently uses a different endpoint or direct service path, update it to use the same backend contract/command.
   - Preserve existing UX while consolidating business behavior.

6. **Preserve tenant and authorization guarantees**
   - Verify approval lookup is scoped by `company_id`.
   - Verify only valid approvers can decide.
   - Ensure cross-tenant or unauthorized decisions return forbidden/not found per existing conventions.

7. **Handle approval lifecycle correctly**
   - Enforce valid transitions only from pending/active states.
   - Prevent duplicate decisions.
   - If ordered approval steps exist:
     - update current step
     - advance next step or finalize approval
   - If rejection short-circuits the chain, ensure linked entity remains blocked/cancelled per existing rules.
   - Do not execute expired/cancelled approvals.

8. **Audit and side effects**
   - Ensure mobile-originated decisions trigger the same audit/business events as web.
   - If the system records actor/action/target/outcome/rationale, confirm mobile decisions populate them identically through the shared backend path.
   - Preserve outbox/event dispatch behavior.

9. **Testing**
   - Add or update tests to prove:
     - approving via API updates approval state correctly
     - rejecting via API updates approval state correctly
     - linked entity state changes match existing web behavior
     - unauthorized/cross-tenant decisions are blocked
     - already-decided approvals cannot be decided again
   - If feasible, add a regression test showing both web and mobile clients target the same endpoint/command abstraction.

10. **Keep changes minimal and architecture-aligned**
   - No mobile-only business rules.
   - No duplicate approval logic in UI layers.
   - Prefer shared DTOs/contracts where already idiomatic in the solution.

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification checklist:
   - Sign in as a valid approver in web and mobile against the same environment.
   - Open the same pending approval in web and mobile.
   - Approve in mobile.
   - Verify in web after refresh that:
     - approval status matches updated backend state
     - decision timestamp/comment appears if supported
     - linked task/workflow/action reflects the same post-approval state as a web approval would
   - Repeat with rejection.
   - Attempt duplicate decision from the other client and verify proper validation/error.
   - Attempt unauthorized or wrong-tenant access and verify forbidden/not found behavior.

4. If API tests exist, ensure coverage includes:
   - pending -> approved
   - pending -> rejected
   - invalid transition rejection
   - tenant isolation
   - approver authorization
   - multi-step progression if implemented

# Risks and follow-ups
- **Risk: duplicated approval logic already exists**
  - Refactoring may touch web and backend together. Favor consolidation over patching mobile separately.

- **Risk: web may be using server-side/internal application calls not exposed cleanly to mobile**
  - Introduce a proper shared API endpoint backed by the same application command.

- **Risk: approval side effects may be broader than status changes**
  - Verify linked task/workflow/action transitions, audit events, and outbox dispatch are preserved.

- **Risk: multi-step approvals may have edge cases**
  - Be careful with ordered chains, partial completion, rejection short-circuiting, and expired approvals.

- **Risk: stale mobile UI after decision**
  - Ensure mobile refreshes from backend response or re-queries after mutation.

Follow-ups after this task, if not already covered:
- Add explicit API contract documentation for approval decision endpoints.
- Add mobile-friendly error states for intermittent connectivity.
- Consider idempotency protections if approval actions may be retried from unstable networks.
- Add end-to-end tests spanning web/mobile parity around approval state transitions.