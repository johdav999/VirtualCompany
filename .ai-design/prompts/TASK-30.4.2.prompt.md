# Goal
Implement backlog task **TASK-30.4.2** for story **US-30.4 Deliver finance inbox review flow and bounded approval proposal experience** by adding backend API and persistence support for finance bill review actions:

- **Approve**
- **Reject**
- **Request clarification**

The implementation must ensure:

- Actions are persisted with full audit metadata: **actor, timestamp, prior status, new status, rationale**
- Approval is **bounded**: approving creates a **BillApprovalProposal** or equivalent approved-bill record only, and **must not trigger payment**
- Bills with unresolved validation failures **cannot transition to Approved** via API
- Status transitions remain constrained to the finance inbox lifecycle:
  - `Detected`
  - `Extracted`
  - `NeedsReview`
  - `ProposedForApproval`
  - `Approved`
  - `Rejected`
  - `SentToPaymentOrExported`

Use the existing modular monolith structure and keep the implementation aligned with:
- ASP.NET Core API
- Application-layer commands/handlers
- Domain-driven state transition rules
- PostgreSQL persistence via Infrastructure
- Auditability as a business feature, not just logging

# Scope
In scope:

- Add or extend domain model(s) for finance bill approval workflow state transitions
- Add persistence for workflow actions/history
- Add application commands/handlers for:
  - approve bill
  - reject bill
  - request clarification
- Add API endpoints for these actions
- Enforce transition rules, especially:
  - no approval when unresolved validation failures exist
  - no payment execution side effects on approval
- Persist action history with required metadata
- Add tests covering valid and invalid transitions

Out of scope:

- Payment execution or export execution
- Full finance inbox UI implementation
- LLM proposal generation itself, except any persistence contract needed to support approval proposal records
- Mobile-specific work
- Broad workflow engine refactors unless required for this task

If the codebase already contains partial finance bill, inbox, approval, or audit primitives, extend them rather than creating parallel abstractions.

# Files to touch
Inspect the solution first, then touch the minimum coherent set of files across these likely areas.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - finance bill aggregate/entity
  - bill status enum/value object
  - approval workflow action/history entity
  - domain rules for transitions
- `src/VirtualCompany.Application/**`
  - commands and handlers for approve/reject/clarification
  - DTOs/results
  - validators
  - query/application contracts if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations or migration scaffolding support
  - persistence mappings
- `src/VirtualCompany.Api/**`
  - finance bill controller/endpoints
  - request/response contracts
  - authorization/policy wiring if already present
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- Possibly:
  - `tests/**` domain/application tests if those projects already exist
  - `README.md` or docs only if there is an established API documentation pattern

Before coding, identify the actual existing finance-related files and adapt this plan to the current naming and architecture.

# Implementation plan
1. **Discover existing finance bill and approval structures**
   - Search for:
     - `FinanceBill`
     - `Bill`
     - `Approval`
     - `Inbox`
     - `AuditEvent`
     - status enums
     - existing command/handler patterns
   - Determine whether there is already:
     - a finance bill aggregate
     - a bill extraction/validation model
     - an approval proposal model
     - an audit/history table pattern
   - Reuse existing conventions for:
     - tenant scoping via `company_id`
     - actor identity capture
     - CQRS-lite command handling
     - API route style

2. **Model the finance bill workflow states**
   - Ensure there is a single authoritative enum/value object for bill inbox status with only the accepted states.
   - If statuses already exist, reconcile names without breaking existing behavior.
   - Add domain transition methods such as:
     - `Approve(...)`
     - `Reject(...)`
     - `RequestClarification(...)`
     - optionally `ProposeForApproval(...)` if needed by existing flow
   - Enforce legal transitions in the domain layer, not only in controllers.

3. **Add validation-failure gate for approval**
   - Identify how extracted field validation warnings/failures are currently represented.
   - Implement a domain/application rule:
     - if unresolved validation failures exist, approval must fail with a clear business error
   - Distinguish warnings from blocking failures if the model supports both.
   - Return an appropriate API response:
     - `400 Bad Request` or `409 Conflict` depending on existing API conventions
   - Include a safe, user-facing error message.

4. **Persist workflow action history**
   - Add a dedicated persistence model if one does not already exist, e.g.:
     - `FinanceBillWorkflowAction`
     - or `FinanceBillStatusHistory`
   - Each record must include:
     - id
     - company/tenant id
     - bill id
     - action type (`approve`, `reject`, `clarification_requested`)
     - actor type / actor id
     - timestamp
     - prior status
     - new status
     - rationale/comment
   - If the system already has a generic audit/business event model, still persist a bill-specific action history if needed for queryability, and optionally fan out to audit events too.

5. **Persist approval outcome without payment execution**
   - On approve:
     - update bill status to `Approved`
     - create a `BillApprovalProposal` or equivalent approved-bill record if the domain already has such a concept
     - explicitly do **not** invoke payment/export workflows, outbox messages, or external integrations
   - If there is an existing approval/proposal entity, use it and ensure its semantics reflect “approved for later payment handling” rather than “payment executed”.

6. **Implement application commands and handlers**
   - Add commands such as:
     - `ApproveFinanceBillCommand`
     - `RejectFinanceBillCommand`
     - `RequestFinanceBillClarificationCommand`
   - Each handler should:
     - resolve tenant-scoped bill
     - load current bill state and validation state
     - capture actor identity from application context
     - execute domain transition
     - append workflow action history
     - create proposal/approved record on approve if required
     - save changes transactionally
   - Follow existing MediatR or application service patterns if present.

7. **Add API endpoints**
   - Add endpoints under the existing finance bill route structure, for example:
     - `POST /api/finance/bills/{billId}/approve`
     - `POST /api/finance/bills/{billId}/reject`
     - `POST /api/finance/bills/{billId}/clarification`
   - Request body should minimally support:
     - `rationale`
   - Response should include updated bill status and enough metadata for UI refresh.
   - Ensure tenant scoping and authorization are enforced consistently with the rest of the API.

8. **Add persistence configuration and migration**
   - Update EF Core configurations for any new entities/columns.
   - Add indexes appropriate for:
     - `company_id`
     - `bill_id`
     - action history ordering by timestamp
   - Create a migration if this repo tracks migrations in source.
   - If migrations are managed differently, follow the repo’s documented pattern in `docs/postgresql-migrations-archive/README.md`.

9. **Add audit integration if available**
   - If there is an existing audit module/table, emit business audit events for each action:
     - action name
     - actor
     - target bill
     - outcome
     - rationale summary
   - Keep this additive; do not replace the required workflow action persistence with logs only.

10. **Test thoroughly**
   - Add tests for:
     - approve succeeds from valid state
     - approve fails when unresolved validation failures exist
     - reject succeeds and records history
     - clarification succeeds and records history
     - invalid transitions are rejected
     - approval does not trigger payment/export side effects
     - tenant isolation on endpoints
   - Prefer integration/API tests where possible, with domain unit tests for transition rules if the project structure supports them.

# Validation steps
1. **Inspect and build**
   - Run:
     - `dotnet build`
   - Confirm the solution compiles cleanly.

2. **Run tests**
   - Run:
     - `dotnet test`
   - Ensure all existing tests pass and new tests pass.

3. **Verify API behavior**
   - Exercise the new endpoints with representative cases:
     - valid approve
     - valid reject
     - valid clarification request
     - approve with unresolved validation failures
     - invalid status transition
     - cross-tenant access attempt
   - Confirm responses match existing API conventions.

4. **Verify persistence**
   - Confirm that each action creates a persisted history record containing:
     - actor
     - timestamp
     - prior status
     - new status
     - rationale
   - Confirm approve creates only the proposal/approved record and does not create payment/export execution artifacts.

5. **Verify status constraints**
   - Confirm bill statuses remain within the accepted set.
   - Confirm approval cannot bypass unresolved validation failures.

6. **Migration sanity check**
   - If a migration is added, verify it applies cleanly and matches the model.

# Risks and follow-ups
- **Existing model mismatch risk**: the repo may already have finance bill or approval abstractions with different naming. Reconcile carefully instead of duplicating concepts.
- **Validation semantics ambiguity**: “validation warnings” vs “validation failures” may not yet be clearly separated. If absent, implement the minimum blocking concept needed for approval gating and note any assumptions in code comments/tests.
- **Audit duplication risk**: avoid relying solely on generic audit logs when the task requires action records suitable for workflow history.
- **Side-effect leakage risk**: existing approval hooks or outbox handlers may automatically trigger downstream payment/export behavior. Ensure approve remains bounded in v1.
- **Authorization gaps**: if finance-specific roles/policies are not yet implemented, follow current authorization conventions and leave a clear TODO only if unavoidable.
- **Follow-up candidates**:
  - query endpoint for bill action history timeline
  - richer rationale/comment validation rules
  - explicit `BillApprovalProposal` read model/query support for UI
  - optimistic concurrency handling for simultaneous reviewers
  - UI wiring for disabled approve action when validation failures remain